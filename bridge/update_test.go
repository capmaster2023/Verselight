package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestNewer(t *testing.T) {
	cases := []struct {
		a, b string
		want bool
	}{
		{"2.1.3", "2.1.2", true}, {"v2.2.0", "2.1.9", true}, {"2.1.2", "2.1.2", false},
		{"2.1.1", "2.1.2", false}, {"3.0.0", "2.10.10", true}, {"2.1.10", "2.1.9", true}, {"2.1.3-beta", "2.1.2", true},
	}
	for _, c := range cases {
		if got := newer(c.a, c.b); got != c.want {
			t.Errorf("newer(%q, %q) = %v", c.a, c.b, got)
		}
	}
}

func TestCheckUpdate(t *testing.T) {
	gh := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte(`{"tag_name":"v2.1.4","assets":[{"name":"MyVerse.apk","browser_download_url":"https://github.com/capmaster2023/Verselight/releases/download/v2.1.4/MyVerse.apk"},{"name":"MyVerse-Setup-2.1.4.exe","browser_download_url":"https://github.com/capmaster2023/Verselight/releases/download/v2.1.4/MyVerse-Setup-2.1.4.exe"}]}`))
	}))
	defer gh.Close()
	old := releasesAPI
	releasesAPI = gh.URL
	defer func() { releasesAPI = old }()

	nl, fromBridge := fakeNeutralino(t)
	defer nl.Close()
	st, _ := openStore(t.TempDir())
	b := &bridge{st: st, phones: map[*phone]bool{}, info: nlInfo{Port: strings.Split(nl.URL, ":")[2], Token: "tok", ConnectToken: "ct", ExtensionID: "ext"}}
	go b.runNeutralino()
	waitEvent(t, fromBridge, "bridgeEvent")

	b.checkUpdate("2.1.3", true)
	ev := waitEvent(t, fromBridge, "bridgeEvent")
	if ev["type"] != "update" || ev["available"] != true || ev["version"] != "2.1.4" || !strings.HasSuffix(ev["url"].(string), "MyVerse-Setup-2.1.4.exe") {
		t.Fatalf("mise à jour non détectée : %v", ev)
	}
	b.checkUpdate("2.1.4", true)
	if ev = waitEvent(t, fromBridge, "bridgeEvent"); ev["available"] != false {
		t.Fatalf("déjà à jour : %v", ev)
	}
	// adresse hors du dépôt refusée
	b.installUpdate("https://exemple.com/virus.exe")
	if ev = waitEvent(t, fromBridge, "bridgeEvent"); ev["state"] != "error" {
		t.Fatalf("adresse étrangère acceptée : %v", ev)
	}
}
