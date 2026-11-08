//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"syscall"
	"time"
	"unsafe"
)

var (
	kernel32         = syscall.NewLazyDLL("kernel32.dll")
	shell32          = syscall.NewLazyDLL("shell32.dll")
	procCreateMutex  = kernel32.NewProc("CreateMutexW")
	procShellExecute = shell32.NewProc("ShellExecuteExW")
)

const (
	ruleTCP = "VerseLight Bridge (TCP)"
	ruleUDP = "VerseLight Bridge (UDP)"
)

// singleInstance : un seul bridge à la fois (mutex nommé Windows).
func singleInstance() bool {
	name, _ := syscall.UTF16PtrFromString(`Local\VerseLightBridge`)
	for i := 0; i < 60; i++ {
		h, _, err := procCreateMutex.Call(0, 0, uintptr(unsafe.Pointer(name)))
		if h != 0 && err != syscall.ERROR_ALREADY_EXISTS {
			return true // poignée gardée jusqu'à la fin du processus
		}
		if h != 0 {
			syscall.CloseHandle(syscall.Handle(h))
		}
		time.Sleep(300 * time.Millisecond) // l'ancien bridge est peut-être en train de s'arrêter
	}
	return false
}

func hiddenCmd(name string, args ...string) *exec.Cmd {
	c := exec.Command(name, args...)
	c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	return c
}

// killTree : ferme un processus et ses enfants (la fenêtre de projection lancée par cmd.exe).
func killTree(pid int) {
	hiddenCmd("taskkill", "/PID", strconv.Itoa(pid), "/T", "/F").Run()
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

func ruleExists(name string) bool {
	return hiddenCmd("netsh", "advfirewall", "firewall", "show", "rule", "name="+name).Run() == nil
}

func firewallRulesPresent() bool { return ruleExists(ruleTCP) && ruleExists(ruleUDP) }

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

// addFirewallRules : une seule élévation UAC (runas) pour toutes les règles, réseau privé.
func addFirewallRules(tcpFrom, tcpTo, udp int) string {
	script := ""
	if !ruleExists(ruleTCP) {
		script += fmt.Sprintf("advfirewall firewall add rule name=\"%s\" dir=in action=allow protocol=TCP localport=%d-%d profile=private\r\n", ruleTCP, tcpFrom, tcpTo)
	}
	if !ruleExists(ruleUDP) {
		script += fmt.Sprintf("advfirewall firewall add rule name=\"%s\" dir=in action=allow protocol=UDP localport=%d profile=private\r\n", ruleUDP, udp)
	}
	if script == "" {
		return "ok"
	}
	path := filepath.Join(os.TempDir(), "verselight-parefeu.txt")
	if err := os.WriteFile(path, []byte(script), 0o600); err != nil {
		return "error"
	}
	defer os.Remove(path)

	verb, _ := syscall.UTF16PtrFromString("runas")
	file, _ := syscall.UTF16PtrFromString("netsh.exe")
	params, _ := syscall.UTF16PtrFromString(`-f "` + path + `"`)
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
