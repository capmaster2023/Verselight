package main

// Récupération unique des données de VerseLight après le renommage en MyVerse.
// WebView2 range localStorage (chants, réglages OBS, thème) dans %APPDATA%\<nom de l'exe> :
// VerseLight.exe -> MyVerse.exe. La régie lance : verselight-bridge.exe --migrer-profil <pid> <ancien> <nouveau> <exe>
// puis se ferme ; on attend la fin de MyVerse, on copie l'ancien profil et on relance MyVerse.

import (
	"io"
	"io/fs"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

var cacheDirs = map[string]bool{
	"cache": true, "code cache": true, "gpucache": true, "grshadercache": true, "shadercache": true,
	"dawncache": true, "dawngraphitecache": true, "dawnwebgpucache": true, "crashpad": true,
}

func migrateProfile(args []string) {
	if len(args) != 4 {
		return
	}
	if base, err := os.UserConfigDir(); err == nil {
		os.MkdirAll(filepath.Join(base, "VerseLight"), 0o700)
		if f, err := os.OpenFile(filepath.Join(base, "VerseLight", "migration.log"), os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o600); err == nil {
			log.SetOutput(f)
		}
	}
	pid, _ := strconv.Atoi(args[0])
	oldDir, newDir, exe := args[1], args[2], args[3]
	log.Printf("migration %s -> %s", oldDir, newDir)
	if pid > 0 {
		waitPid(pid, 30*time.Second)
	}
	// le profil neuf de MyVerse est libre quand WebView2 l'a lâché : on peut alors le renommer
	moved := false
	backup := newDir + ".vide-" + time.Now().Format("20060102-150405")
	for i := 0; i < 60; i++ {
		if _, err := os.Stat(newDir); os.IsNotExist(err) {
			moved = true
			break
		}
		if os.Rename(newDir, backup) == nil {
			moved = true
			break
		}
		time.Sleep(500 * time.Millisecond)
	}
	if moved {
		if err := copyTree(oldDir, newDir); err != nil {
			log.Printf("copie : %v", err)
		} else {
			log.Print("profil récupéré")
		}
	} else {
		log.Print("profil MyVerse toujours utilisé : abandon")
	}
	c := exec.Command(exe)
	c.Dir = filepath.Dir(exe)
	if err := c.Start(); err != nil {
		log.Printf("relance : %v", err)
	}
}

func copyTree(src, dst string) error {
	return filepath.WalkDir(src, func(p string, d fs.DirEntry, err error) error {
		if err != nil {
			return nil
		}
		rel, err := filepath.Rel(src, p)
		if err != nil {
			return nil
		}
		target := filepath.Join(dst, rel)
		if d.IsDir() {
			if p != src && cacheDirs[strings.ToLower(d.Name())] {
				return filepath.SkipDir
			}
			os.MkdirAll(target, 0o755)
			return nil
		}
		if !d.Type().IsRegular() {
			return nil
		}
		copyFile(p, target)
		return nil
	})
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	if _, err := io.Copy(out, in); err != nil {
		out.Close()
		return err
	}
	return out.Close()
}
