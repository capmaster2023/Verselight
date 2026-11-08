package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

// faux Neutralino : enregistre ce que le bridge envoie à la régie
func fakeNeutralino(t *testing.T) (*httptest.Server, chan map[string]any) {
	got := make(chan map[string]any, 50)
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Query().Get("connectToken") != "ct" || r.URL.Query().Get("extensionId") != "ext" {
			http.Error(w, "refus", 403)
			return
		}
		ws, err := wsUpgrade(w, r)
		if err != nil {
			return
		}
		for {
			b, err := ws.ReadMessage()
			if err != nil {
				return
			}
			var m map[string]any
			json.Unmarshal(b, &m)
			got <- m
		}
	}))
	return srv, got
}

func dialPhone(t *testing.T, srv *httptest.Server) *wsConn {
	ws, err := wsDial(strings.TrimPrefix(srv.URL, "http://"), "/", 3*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	return ws
}

func readJSON(t *testing.T, ws *wsConn) map[string]any {
	ws.SetReadDeadline(time.Now().Add(3 * time.Second))
	b, err := ws.ReadMessage()
	if err != nil {
		t.Fatal(err)
	}
	var m map[string]any
	json.Unmarshal(b, &m)
	return m
}

func connectPhone(t *testing.T, srv *httptest.Server, pcID, deviceID string, pair bool, key []byte) (*wsConn, map[string]any) {
	ws := dialPhone(t, srv)
	cnonce := randomB64(16)
	ws.WriteText(mustJSON(map[string]any{"t": "hello", "v": 1, "pcId": pcID, "deviceId": deviceID, "deviceName": "Pixel", "pair": pair, "cnonce": cnonce}))
	ch := readJSON(t, ws)
	if ch["t"] != "challenge" {
		return ws, ch
	}
	nonce := ch["nonce"].(string)
	if ch["proof"] != serverProof(key, cnonce, nonce) {
		t.Fatal("preuve du PC invalide")
	}
	ws.WriteText(mustJSON(map[string]any{"t": "auth", "mac": clientMAC(key, nonce, deviceID)}))
	return ws, readJSON(t, ws)
}

func TestPairAuthRelayRevoke(t *testing.T) {
	nl, fromBridge := fakeNeutralino(t)
	defer nl.Close()
	st, err := openStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	b := &bridge{st: st, name: "PC-EGLISE", port: 47900, phones: map[*phone]bool{},
		info: nlInfo{Port: strings.Split(nl.URL, ":")[2], Token: "tok", ConnectToken: "ct", ExtensionID: "ext"}}
	go b.runNeutralino()
	waitEvent(t, fromBridge, "bridgeEvent")

	phones := httptest.NewServer(http.HandlerFunc(b.handlePhone))
	defer phones.Close()

	// jumelage refusé tant que la fenêtre Téléphone n'est pas ouverte
	_, m := connectPhone(t, phones, st.pcID(), "dev1", true, nil)
	if m["code"] != "pairing_closed" {
		t.Fatalf("attendu pairing_closed, reçu %v", m)
	}

	b.fromRegie("pairStart", nil)
	ev := waitEvent(t, fromBridge, "bridgeEvent")
	u := ev["url"].(string)
	secret := u[strings.Index(u, "secret=")+7:]
	sb, _ := b64.DecodeString(secret)
	key := deriveKey(sb, st.pcID(), "dev1")

	ws, m := connectPhone(t, phones, st.pcID(), "dev1", true, key)
	if m["t"] != "welcome" {
		t.Fatalf("jumelage : %v", m)
	}
	ws.Close()
	b.fromRegie("pairStop", nil)

	// reconnexion normale avec la clé dérivée
	ws, m = connectPhone(t, phones, st.pcID(), "dev1", false, key)
	if m["t"] != "welcome" {
		t.Fatalf("reconnexion : %v", m)
	}
	ws.WriteText(mustJSON(map[string]any{"t": "bible.show", "ref": "jn 3 16"}))
	cmd := waitEvent(t, fromBridge, "remoteCommand")
	if cmd["t"] != "bible.show" || cmd["ref"] != "jn 3 16" || cmd["device"] != "dev1" {
		t.Fatalf("commande relayée : %v", cmd)
	}

	// état de la régie -> téléphone
	b.fromRegie("state", json.RawMessage(`{"mode":"verset","ref":"Jean 3:16"}`))
	for {
		m = readJSON(t, ws)
		if m["t"] == "state" {
			break
		}
	}
	if m["ref"] != "Jean 3:16" {
		t.Fatalf("état : %v", m)
	}

	// mauvaise clé = refus
	_, m = connectPhone2(t, phones, st.pcID(), "dev1", []byte("mauvaise"))
	if m["code"] != "revoked" {
		t.Fatalf("mauvaise clé acceptée : %v", m)
	}

	// révocation
	b.fromRegie("revoke", json.RawMessage(`{"deviceId":"dev1"}`))
	_, m = connectPhone(t, phones, st.pcID(), "dev1", false, key)
	if m["code"] != "revoked" {
		t.Fatalf("appareil révoqué accepté : %v", m)
	}

	// le pcId est stable entre deux lancements
	st2, _ := openStore(st.path[:len(st.path)-len("/devices.json")])
	if st2.pcID() != st.pcID() {
		t.Fatal("pcId non persistant")
	}
}

// connectPhone2 : envoie un MAC faux sans vérifier la preuve
func connectPhone2(t *testing.T, srv *httptest.Server, pcID, deviceID string, key []byte) (*wsConn, map[string]any) {
	ws := dialPhone(t, srv)
	ws.WriteText(mustJSON(map[string]any{"t": "hello", "v": 1, "pcId": pcID, "deviceId": deviceID, "cnonce": "x"}))
	ch := readJSON(t, ws)
	ws.WriteText(mustJSON(map[string]any{"t": "auth", "mac": clientMAC(key, ch["nonce"].(string), deviceID)}))
	return ws, readJSON(t, ws)
}

func waitEvent(t *testing.T, ch chan map[string]any, event string) map[string]any {
	deadline := time.After(3 * time.Second)
	for {
		select {
		case m := <-ch:
			d, _ := m["data"].(map[string]any)
			if m["method"] == "app.broadcast" && d["event"] == event {
				data, _ := d["data"].(map[string]any)
				if event == "bridgeEvent" && data["type"] == "firewall" {
					continue
				}
				return data
			}
		case <-deadline:
			t.Fatalf("événement %s non reçu", event)
		}
	}
}

func TestReadInfo(t *testing.T) {
	info, err := readInfo(strings.NewReader(`{"nlPort":"47836","nlToken":"a.b","nlConnectToken":"b","nlExtensionId":"ca.mepc.verselight.bridge"}`))
	if err != nil || info.Port != "47836" || info.ExtensionID != "ca.mepc.verselight.bridge" {
		t.Fatal(info, err)
	}
}
