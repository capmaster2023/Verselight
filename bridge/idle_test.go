package main

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// La connexion à MyVerse ne doit pas tomber quand rien ne se passe (bug 2.1.1),
// et un téléphone qui envoie seulement des pings doit rester connecté.
func TestIdleConnectionsStayAlive(t *testing.T) {
	oldIdle, oldPing := phoneIdle, nlPing
	phoneIdle, nlPing = 1*time.Second, 200*time.Millisecond
	defer func() { phoneIdle, nlPing = oldIdle, oldPing }()

	nl, fromBridge := fakeNeutralino(t)
	defer nl.Close()
	st, _ := openStore(t.TempDir())
	b := &bridge{st: st, name: "PC", port: 47900, phones: map[*phone]bool{},
		info: nlInfo{Port: strings.Split(nl.URL, ":")[2], Token: "tok", ConnectToken: "ct", ExtensionID: "ext"}}
	ended := make(chan error, 1)
	go func() { ended <- b.runNeutralino() }()
	waitEvent(t, fromBridge, "bridgeEvent")

	b.fromRegie("pairStart", nil)
	ev := waitEvent(t, fromBridge, "bridgeEvent")
	u := ev["url"].(string)
	sb, _ := b64.DecodeString(u[strings.Index(u, "secret=")+7:])
	key := deriveKey(sb, st.pcID(), "dev1")

	phones := httptest.NewServer(http.HandlerFunc(b.handlePhone))
	defer phones.Close()
	ws, m := connectPhone(t, phones, st.pcID(), "dev1", true, key)
	if m["t"] != "welcome" {
		t.Fatal(m)
	}
	silent, m2 := connectPhone(t, phones, st.pcID(), "dev1", false, key)
	if m2["t"] != "welcome" {
		t.Fatal(m2)
	}

	// 2,5 s d'inactivité : le téléphone envoie seulement des pings
	for i := 0; i < 8; i++ {
		time.Sleep(300 * time.Millisecond)
		if err := ws.Ping(); err != nil {
			t.Fatal("téléphone déconnecté pendant l'inactivité")
		}
	}
	select {
	case err := <-ended:
		t.Fatalf("le bridge a quitté MyVerse pendant l'inactivité : %v", err)
	default:
	}
	ws.WriteText(mustJSON(map[string]any{"t": "next"}))
	if cmd := waitEvent(t, fromBridge, "remoteCommand"); cmd["t"] != "next" {
		t.Fatal(cmd)
	}

	// le téléphone muet (sans ping) est déconnecté après le délai
	silent.SetReadDeadline(time.Now().Add(3 * time.Second))
	for {
		if _, err := silent.ReadMessage(); err != nil {
			break
		}
	}
}

func TestCopyTreeSkipsCaches(t *testing.T) {
	src, dst := t.TempDir(), filepath.Join(t.TempDir(), "MyVerse.exe")
	os.MkdirAll(filepath.Join(src, "EBWebView", "Default", "Local Storage", "leveldb"), 0o755)
	os.MkdirAll(filepath.Join(src, "EBWebView", "Default", "Cache"), 0o755)
	os.WriteFile(filepath.Join(src, "EBWebView", "Default", "Local Storage", "leveldb", "000003.log"), []byte("chants"), 0o644)
	os.WriteFile(filepath.Join(src, "EBWebView", "Default", "Cache", "data_0"), []byte("x"), 0o644)
	if err := copyTree(src, dst); err != nil {
		t.Fatal(err)
	}
	b, err := os.ReadFile(filepath.Join(dst, "EBWebView", "Default", "Local Storage", "leveldb", "000003.log"))
	if err != nil || string(b) != "chants" {
		t.Fatal("localStorage non copié")
	}
	if _, err := os.Stat(filepath.Join(dst, "EBWebView", "Default", "Cache")); err == nil {
		t.Fatal("le cache ne devait pas être copié")
	}
}
