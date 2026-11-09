//go:build windows

package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

var (
	kernel32         = syscall.NewLazyDLL("kernel32.dll")
	shell32          = syscall.NewLazyDLL("shell32.dll")
	user32           = syscall.NewLazyDLL("user32.dll")
	procCreateMutex  = kernel32.NewProc("CreateMutexW")
	procShellExecute = shell32.NewProc("ShellExecuteExW")
	procEnumWindows  = user32.NewProc("EnumWindows")
	procWindowPid    = user32.NewProc("GetWindowThreadProcessId")
	procIsVisible    = user32.NewProc("IsWindowVisible")
	procIsIconic     = user32.NewProc("IsIconic")
	procShowWindow   = user32.NewProc("ShowWindow")
	procSetFg        = user32.NewProc("SetForegroundWindow")
	procWindowText   = user32.NewProc("GetWindowTextW")
)

// ------------------------------------------------------------ une seule copie de MyVerse

var mutexName, _ = syscall.UTF16PtrFromString(`Local\VerseLightBridge`)

// tryMutex : vrai si ce bridge est le seul (le verrou est gardé jusqu'à la fin du processus).
func tryMutex() bool {
	h, _, err := procCreateMutex.Call(0, 0, uintptr(unsafe.Pointer(mutexName)))
	if h != 0 && err != syscall.ERROR_ALREADY_EXISTS {
		return true
	}
	if h != 0 {
		syscall.CloseHandle(syscall.Handle(h))
	}
	return false
}

func waitMutex(max time.Duration) bool {
	end := time.Now().Add(max)
	for {
		if tryMutex() {
			return true
		}
		if time.Now().After(end) {
			return false
		}
		time.Sleep(300 * time.Millisecond)
	}
}

// findAppProcess : le MyVerse.exe qui a lancé ce bridge (bridge <- cmd.exe <- MyVerse.exe).
func findAppProcess() (int, string) {
	self, err := os.Executable()
	if err != nil {
		return 0, ""
	}
	appDir := filepath.Dir(filepath.Dir(filepath.Dir(self)))
	snap, err := syscall.CreateToolhelp32Snapshot(syscall.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return 0, ""
	}
	defer syscall.CloseHandle(snap)
	parent := map[uint32]uint32{}
	name := map[uint32]string{}
	var e syscall.ProcessEntry32
	e.Size = uint32(unsafe.Sizeof(e))
	for err = syscall.Process32First(snap, &e); err == nil; err = syscall.Process32Next(snap, &e) {
		parent[e.ProcessID] = e.ParentProcessID
		name[e.ProcessID] = syscall.UTF16ToString(e.ExeFile[:])
	}
	pid := uint32(os.Getpid())
	for i := 0; i < 5; i++ {
		pp, ok := parent[pid]
		if !ok || pp == 0 {
			break
		}
		pid = pp
		n := name[pid]
		if strings.EqualFold(n, "cmd.exe") || strings.EqualFold(n, "conhost.exe") {
			continue
		}
		p := filepath.Join(appDir, n)
		if _, err := os.Stat(p); err == nil && !strings.EqualFold(n, filepath.Base(self)) {
			return int(pid), p
		}
		break
	}
	return 0, ""
}

// focusWindowOf : ramène au premier plan la fenêtre principale d'un processus.
func focusWindowOf(pid int) {
	if pid <= 0 {
		return
	}
	cb := syscall.NewCallback(func(hwnd uintptr, _ uintptr) uintptr {
		var p uint32
		procWindowPid.Call(hwnd, uintptr(unsafe.Pointer(&p)))
		if int(p) != pid {
			return 1
		}
		if v, _, _ := procIsVisible.Call(hwnd); v == 0 {
			return 1
		}
		buf := make([]uint16, 256)
		procWindowText.Call(hwnd, uintptr(unsafe.Pointer(&buf[0])), 256)
		if strings.Contains(syscall.UTF16ToString(buf), "écran") {
			return 1
		}
		if ic, _, _ := procIsIconic.Call(hwnd); ic != 0 {
			procShowWindow.Call(hwnd, 9) // SW_RESTORE
		}
		procSetFg.Call(hwnd)
		return 0
	})
	procEnumWindows.Call(cb, 0)
}

func hiddenCmd(name string, args ...string) *exec.Cmd {
	c := exec.Command(name, args...)
	c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	return c
}

func killPID(pid int) {
	if pid > 0 {
		hiddenCmd("taskkill", "/PID", strconv.Itoa(pid), "/F").Run()
	}
}

// killTree : ferme un processus et ses enfants (la fenêtre de projection lancée par cmd.exe).
func killTree(pid int) {
	if pid > 0 {
		hiddenCmd("taskkill", "/PID", strconv.Itoa(pid), "/T", "/F").Run()
	}
}

// cleanupWebViews : ferme les processus WebView2 restés accrochés au profil de MyVerse.
func cleanupWebViews(exeName string) {
	if exeName == "" || strings.ContainsAny(exeName, `'"*?`) {
		return
	}
	script := `Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -like '*\` + exeName + `\EBWebView*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`
	hiddenCmd("powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script).Run()
}

func startDetached(exe string) error {
	c := exec.Command(exe)
	c.Dir = filepath.Dir(exe)
	return c.Start()
}

// waitPid : attend la fin d'un processus (au plus max).
func waitPid(pid int, max time.Duration) {
	h, err := syscall.OpenProcess(0x00100000 /* SYNCHRONIZE */, false, uint32(pid))
	if err != nil {
		return // déjà terminé
	}
	defer syscall.CloseHandle(h)
	syscall.WaitForSingleObject(h, uint32(max/time.Millisecond))
}

// ------------------------------------------------------------ pare-feu

var (
	fwRules = []string{"MyVerse (TCP)", "MyVerse (UDP)", "MyVerse (programme)"}
	// anciennes règles, et règle automatique de Windows (souvent « Bloquer ») créée quand on ferme son alerte
	fwOld = []string{"verselight-bridge.exe", "VerseLight Bridge (TCP)", "VerseLight Bridge (UDP)"}
)

func ruleExists(name string) bool {
	return hiddenCmd("netsh", "advfirewall", "firewall", "show", "rule", "name="+name).Run() == nil
}

func firewallRulesPresent() bool {
	for _, r := range fwRules {
		if !ruleExists(r) {
			return false
		}
	}
	return !ruleExists("verselight-bridge.exe")
}

// applyFirewallRules : avec les droits administrateur (installateur ou « --parefeu »).
// Tous les profils réseau (privé ET public) : le Wi-Fi est souvent classé « public » par Windows.
func applyFirewallRules() error {
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	for _, r := range append(append([]string{}, fwOld...), fwRules...) {
		if ruleExists(r) {
			hiddenCmd("netsh", "advfirewall", "firewall", "delete", "rule", "name="+r).Run()
		}
	}
	add := [][]string{
		{"advfirewall", "firewall", "add", "rule", "name=MyVerse (TCP)", "dir=in", "action=allow", "protocol=TCP", "localport=47900-47909", "profile=any"},
		{"advfirewall", "firewall", "add", "rule", "name=MyVerse (UDP)", "dir=in", "action=allow", "protocol=UDP", "localport=47901", "profile=any"},
		{"advfirewall", "firewall", "add", "rule", "name=MyVerse (programme)", "dir=in", "action=allow", "program=" + exe, "enable=yes", "profile=any"},
	}
	for _, a := range add {
		if err := hiddenCmd("netsh", a...).Run(); err != nil {
			return err
		}
	}
	return nil
}

type shellExecuteInfo struct {
	cbSize       uint32
	fMask        uint32
	hwnd         uintptr
	lpVerb       *uint16
	lpFile       *uint16
	lpParameters *uint16
	lpDirectory  *uint16
	nShow        int32
	hInstApp     uintptr
	lpIDList     uintptr
	lpClass      *uint16
	hkeyClass    uintptr
	dwHotKey     uint32
	hIcon        uintptr
	hProcess     syscall.Handle
}

// addFirewallRules : relance ce programme en administrateur (une seule demande UAC) avec « --parefeu ».
func addFirewallRules() string {
	exe, err := os.Executable()
	if err != nil {
		return "error"
	}
	verb, _ := syscall.UTF16PtrFromString("runas")
	file, _ := syscall.UTF16PtrFromString(exe)
	params, _ := syscall.UTF16PtrFromString("--parefeu")
	info := shellExecuteInfo{fMask: 0x40 | 0x100, lpVerb: verb, lpFile: file, lpParameters: params, nShow: 0}
	info.cbSize = uint32(unsafe.Sizeof(info))
	r, _, err := procShellExecute.Call(uintptr(unsafe.Pointer(&info)))
	if r == 0 {
		if errno, ok := err.(syscall.Errno); ok && errno == 1223 {
			return "refused" // l'utilisateur a dit non à l'UAC
		}
		return "error"
	}
	if info.hProcess != 0 {
		syscall.WaitForSingleObject(info.hProcess, 120000)
		syscall.CloseHandle(info.hProcess)
	}
	if firewallRulesPresent() {
		return "ok"
	}
	return "error"
}
