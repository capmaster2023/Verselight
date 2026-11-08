package main

// Mise à jour de MyVerse : dernière version publiée sur GitHub Releases,
// téléchargement de l'installateur puis installation silencieuse (/S) qui rouvre MyVerse.

import (
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

var (
	releasesAPI    = "https://api.github.com/repos/capmaster2023/Verselight/releases/latest"
	downloadPrefix = "https://github.com/capmaster2023/Verselight/releases/download/"
)

type release struct {
	TagName string `json:"tag_name"`
	Assets  []struct {
		Name string `json:"name"`
		URL  string `json:"browser_download_url"`
	} `json:"assets"`
}

func versionParts(v string) [3]int {
	var p [3]int
	for i, s := range strings.SplitN(strings.TrimPrefix(strings.TrimSpace(v), "v"), ".", 3) {
		s = strings.TrimFunc(s, func(r rune) bool { return r < '0' || r > '9' })
		if j := strings.IndexFunc(s, func(r rune) bool { return r < '0' || r > '9' }); j >= 0 {
			s = s[:j]
		}
		p[i], _ = strconv.Atoi(s)
	}
	return p
}

// newer : a est plus récent que b
func newer(a, b string) bool {
	pa, pb := versionParts(a), versionParts(b)
	for i := 0; i < 3; i++ {
		if pa[i] != pb[i] {
			return pa[i] > pb[i]
		}
	}
	return false
}

func (b *bridge) updateEvent(m map[string]any) {
	m["type"] = "update"
	b.toRegie("bridgeEvent", m)
}

func (b *bridge) checkUpdate(current string, manual bool) {
	req, _ := http.NewRequest("GET", releasesAPI, nil)
	req.Header.Set("User-Agent", "MyVerse-Updater")
	req.Header.Set("Accept", "application/vnd.github+json")
	resp, err := (&http.Client{Timeout: 15 * time.Second}).Do(req)
	if err != nil {
		b.updateEvent(map[string]any{"available": false, "manual": manual, "error": "Impossible de vérifier les mises à jour : pas de connexion Internet."})
		return
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		b.updateEvent(map[string]any{"available": false, "manual": manual, "error": fmt.Sprintf("Impossible de vérifier les mises à jour (GitHub : %d). Le dépôt doit être public.", resp.StatusCode)})
		return
	}
	var rel release
	if err := json.NewDecoder(io.LimitReader(resp.Body, 1<<20)).Decode(&rel); err != nil {
		b.updateEvent(map[string]any{"available": false, "manual": manual, "error": "Réponse de GitHub illisible."})
		return
	}
	setup := ""
	for _, a := range rel.Assets {
		if strings.HasPrefix(a.Name, "MyVerse-Setup-") && strings.HasSuffix(a.Name, ".exe") && strings.HasPrefix(a.URL, downloadPrefix) {
			setup = a.URL
		}
	}
	latest := strings.TrimPrefix(rel.TagName, "v")
	b.updateEvent(map[string]any{"available": setup != "" && newer(latest, current), "version": latest, "url": setup, "manual": manual, "current": current})
}

func (b *bridge) installUpdate(url string) {
	if !strings.HasPrefix(url, downloadPrefix) || !strings.HasSuffix(url, ".exe") {
		b.updateEvent(map[string]any{"state": "error", "message": "Adresse de mise à jour refusée."})
		return
	}
	fail := func(err error) {
		log.Printf("mise à jour : %v", err)
		b.updateEvent(map[string]any{"state": "error", "message": "Téléchargement de la mise à jour impossible. Réessayez plus tard."})
	}
	req, _ := http.NewRequest("GET", url, nil)
	req.Header.Set("User-Agent", "MyVerse-Updater")
	resp, err := (&http.Client{Timeout: 10 * time.Minute}).Do(req)
	if err != nil {
		fail(err)
		return
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		fail(fmt.Errorf("HTTP %d", resp.StatusCode))
		return
	}
	path := filepath.Join(os.TempDir(), "MyVerse-Setup.exe")
	f, err := os.Create(path)
	if err != nil {
		fail(err)
		return
	}
	total := resp.ContentLength
	var done int64
	last := time.Now()
	buf := make([]byte, 64<<10)
	for {
		n, rerr := resp.Body.Read(buf)
		if n > 0 {
			if _, werr := f.Write(buf[:n]); werr != nil {
				f.Close()
				fail(werr)
				return
			}
			done += int64(n)
			if total > 0 && time.Since(last) > 400*time.Millisecond {
				last = time.Now()
				b.updateEvent(map[string]any{"state": "download", "pct": done * 100 / total})
			}
		}
		if rerr == io.EOF {
			break
		}
		if rerr != nil {
			f.Close()
			fail(rerr)
			return
		}
	}
	f.Close()
	if done < 100000 || (total > 0 && done != total) {
		fail(fmt.Errorf("fichier incomplet (%d octets)", done))
		return
	}
	b.updateEvent(map[string]any{"state": "installing"})
	time.Sleep(800 * time.Millisecond)
	// l'installateur ferme MyVerse et ce bridge, installe, puis rouvre MyVerse
	if err := exec.Command(path, "/S").Start(); err != nil {
		fail(err)
	}
}
