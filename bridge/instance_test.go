package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestHTTPTestPageAndInstance(t *testing.T) {
	st, _ := openStore(t.TempDir())
	b := &bridge{st: st, name: "PC-EGLISE", port: 47900, phones: map[*phone]bool{}}
	srv := httptest.NewServer(http.HandlerFunc(b.handlePhone))
	defer srv.Close()

	resp, err := http.Get(srv.URL + "/")
	if err != nil {
		t.Fatal(err)
	}
	body, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	if resp.StatusCode != 200 || !strings.Contains(string(body), "MyVerse") || !strings.Contains(string(body), "PC-EGLISE") {
		t.Fatalf("page de test : %d %s", resp.StatusCode, body)
	}

	get := func() map[string]any {
		resp, err := http.Get(srv.URL + "/instance")
		if err != nil {
			t.Fatal(err)
		}
		defer resp.Body.Close()
		var m map[string]any
		json.NewDecoder(resp.Body).Decode(&m)
		return m
	}
	if m := get(); m["myverse"] != true || m["alive"] != false {
		t.Fatalf("avant la régie : %v", m)
	}
	b.fromRegie("alive", nil)
	if m := get(); m["alive"] != true {
		t.Fatalf("après la régie : %v", m)
	}
	// un événement système de Neutralino ne prouve pas que la page est vivante
	b2 := &bridge{st: st, phones: map[*phone]bool{}}
	b2.fromRegie("extClientConnect", nil)
	if b2.regieAlive(0) {
		t.Fatal("extClientConnect ne doit pas compter")
	}
}

// Une deuxième copie de MyVerse : si la première fonctionne, on la ramène devant et on s'arrête.
func TestOtherInstance(t *testing.T) {
	var ln net.Listener
	var err error
	for p := firstPort; p <= lastPort; p++ {
		if ln, err = net.Listen("tcp4", fmt.Sprintf("127.0.0.1:%d", p)); err == nil {
			break
		}
	}
	if ln == nil {
		t.Skip("aucun port libre")
	}
	alive, focused, quit, app, age, closing := true, false, false, 0, 120.0, false
	go http.Serve(ln, http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/instance":
			w.Write(mustJSON(map[string]any{"myverse": true, "alive": alive, "app": app, "age": age, "closing": closing}))
		case "/focus":
			focused = true
		case "/quit":
			quit = true
		}
	}))
	defer ln.Close()
	if otherInstance(0) || !focused {
		t.Fatal("la copie en double aurait dû s'arrêter après avoir ramené la première")
	}
	// double-clic : l'autre vient de démarrer (page pas encore chargée) → on ne le ferme pas
	alive, focused, age = false, false, 5
	if otherInstance(0) || !focused || quit {
		t.Fatal("une copie qui démarre ne doit pas être fermée")
	}
	// bloquée depuis plus d'une minute → fermée et remplacée
	age = 120
	if !otherInstance(0) || !quit {
		t.Fatal("la copie bloquée aurait dû être fermée et remplacée")
	}
	// relance de secours du même MyVerse : on s'arrête sans rien toucher
	app, alive, focused, quit = 4242, true, false, false
	if otherInstance(4242) || focused || quit {
		t.Fatal("un bridge en double pour le même MyVerse doit juste s'arrêter")
	}
	// l'autre MyVerse est en train de se fermer : on attend sa place
	app, closing, focused, quit = 0, true, false, false
	if !otherInstance(0) || focused || quit {
		t.Fatal("pendant la fermeture de l'autre, on attend puis on continue")
	}
}
