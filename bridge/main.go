package main

// verselight-bridge : extension Neutralino de VerseLight.
// Téléphone (WebSocket LAN, port 47900+) <-> bridge <-> régie (WebSocket Neutralino).

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"
)

var version = "dev"

// délais (variables pour les tests)
var (
	phoneIdle = 45 * time.Second // le téléphone envoie un ping toutes les 10 s
	nlPing    = 5 * time.Second
)

const (
	firstPort     = 47900
	lastPort      = 47909
	discoveryPort = 47901
	protoVersion  = 1
)

type nlInfo struct {
	Port         string `json:"nlPort"`
	Token        string `json:"nlToken"`
	ConnectToken string `json:"nlConnectToken"`
	ExtensionID  string `json:"nlExtensionId"`
}

// ------------------------------------------------------------ état global

type phone struct {
	ws       *wsConn
	deviceID string
	name     string
	key      []byte // clé du téléphone (chiffrement des réglages OBS)
}

type bridge struct {
	st   *store
	dir  string
	name string
	port int

	nlMu sync.Mutex
	nl   *wsConn
	info nlInfo

	appPID int    // MyVerse.exe qui a lancé ce bridge
	appExe string //   et son chemin (pour le relancer)

	started time.Time

	mu         sync.Mutex
	closing    bool // MyVerse s'est fermé : ce bridge s'arrête
	ecranPid   int  // fenêtre de projection, fermée avec MyVerse
	lastRegie  time.Time
	restarting bool
	phones     map[*phone]bool
	state      json.RawMessage
	pairKey    []byte
	pairUntil  time.Time
	obs        *obsConfig // réglages OBS de MyVerse (pour piloter OBS depuis le téléphone)
}

func main() {
	if len(os.Args) > 1 {
		switch os.Args[1] {
		case "--migrer-profil":
			migrateProfile(os.Args[2:])
			return
		case "--parefeu": // lancé en administrateur (installateur ou demande UAC du bridge)
			if err := applyFirewallRules(); err != nil {
				os.Exit(1)
			}
			return
		}
	}
	dir := os.Getenv("VERSELIGHT_DATA_DIR")
	if dir == "" {
		base, err := os.UserConfigDir()
		if err != nil {
			base = "."
		}
		dir = filepath.Join(base, "VerseLight")
	}
	os.MkdirAll(dir, 0o700)
	if f, err := os.OpenFile(filepath.Join(dir, "bridge.log"), os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o600); err == nil {
		if st, e := f.Stat(); e == nil && st.Size() > 512<<10 {
			f.Truncate(0)
		}
		log.SetOutput(io.MultiWriter(f, os.Stderr))
	}
	log.Printf("verselight-bridge %s", version)
	appPID, appExe := findAppProcess()

	// une seule copie de MyVerse à la fois
	if !tryMutex() {
		if !otherInstance(appPID) {
			return
		}
	}

	info, err := readInfo(os.Stdin)
	if err != nil {
		log.Printf("lancez MyVerse.exe : ce programme démarre tout seul avec lui (%v)", err)
		return
	}

	st, err := openStore(dir)
	if err != nil {
		log.Fatalf("données : %v", err)
	}
	host, _ := os.Hostname()
	if n := os.Getenv("COMPUTERNAME"); n != "" {
		host = n
	}
	b := &bridge{st: st, dir: dir, name: host, phones: map[*phone]bool{}, info: info, appPID: appPID, appExe: appExe, started: time.Now()}

	ln, port, err := listenFirstFree()
	if err != nil {
		log.Fatalf("aucun port libre entre %d et %d : %v", firstPort, lastPort, err)
	}
	b.port = port
	log.Printf("écoute des téléphones sur le port %d", port)

	go b.serveDiscovery()
	go func() {
		mux := http.NewServeMux()
		mux.HandleFunc("/", b.handlePhone)
		http.Serve(ln, mux)
	}()
	go b.checkFirewall(false)
	go b.watchdog()

	// la régie : quand cette connexion tombe, MyVerse est fermé, on s'arrête
	if err := b.runNeutralino(); err != nil {
		log.Printf("Neutralino : %v", err)
	}
	b.mu.Lock()
	b.closing = true
	restarting := b.restarting
	pid := b.ecranPid
	b.mu.Unlock()
	if restarting {
		select {} // restartApp relance MyVerse puis termine le processus
	}
	log.Print("MyVerse fermé : arrêt du bridge")
	if pid > 0 {
		killTree(pid) // ferme aussi la fenêtre de projection restée ouverte
	}
	os.Exit(0) // libère tout de suite le port et le verrou pour le prochain lancement
}

// otherInstance : un autre bridge tourne déjà. Renvoie vrai si ce bridge doit continuer.
// - même MyVerse (relance de secours en double) : ce bridge s'arrête sans rien toucher ;
// - l'autre MyVerse se ferme : on attend qu'il libère la place ;
// - l'autre MyVerse fonctionne ou vient de démarrer (double-clic) : on le ramène devant et on ferme ce double ;
// - l'autre est bloqué depuis plus d'une minute (fenêtre blanche) : on le ferme et on prend sa place.
func otherInstance(myApp int) bool {
	client := &http.Client{Timeout: 1500 * time.Millisecond}
	for p := firstPort; p <= lastPort; p++ {
		resp, err := client.Get(fmt.Sprintf("http://127.0.0.1:%d/instance", p))
		if err != nil {
			continue
		}
		var r struct {
			MyVerse bool    `json:"myverse"`
			Alive   bool    `json:"alive"`
			Closing bool    `json:"closing"`
			App     int     `json:"app"`
			Age     float64 `json:"age"`
		}
		json.NewDecoder(resp.Body).Decode(&r)
		resp.Body.Close()
		if !r.MyVerse {
			continue
		}
		if myApp != 0 && r.App == myApp {
			log.Print("un bridge tourne déjà pour ce MyVerse : arrêt")
			return false
		}
		if r.Closing {
			break
		}
		if r.Alive || r.Age < 60 {
			log.Print("MyVerse est déjà ouvert : on le ramène au premier plan")
			if resp, err := client.Get(fmt.Sprintf("http://127.0.0.1:%d/focus", p)); err == nil {
				resp.Body.Close()
			}
			killPID(myApp)
			return false
		}
		log.Print("l'ancien MyVerse est bloqué : on le ferme")
		if resp, err := client.Get(fmt.Sprintf("http://127.0.0.1:%d/quit", p)); err == nil {
			resp.Body.Close()
		}
		break
	}
	if waitMutex(20 * time.Second) {
		return true
	}
	log.Print("un autre bridge tourne déjà : arrêt")
	return false
}

// watchdog : si la page de MyVerse ne s'est jamais manifestée (fenêtre restée blanche), on relance MyVerse.
func (b *bridge) watchdog() {
	time.Sleep(60 * time.Second)
	if b.regieAlive(0) {
		return
	}
	b.restartApp()
}

func (b *bridge) regieAlive(within time.Duration) bool {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.lastRegie.IsZero() {
		return false
	}
	return within == 0 || time.Since(b.lastRegie) < within
}

func (b *bridge) restartApp() {
	if b.appPID == 0 || b.appExe == "" {
		return
	}
	guard := filepath.Join(b.dir, "relance-auto.txt")
	if t, err := os.ReadFile(guard); err == nil {
		if last, err := time.Parse(time.RFC3339, strings.TrimSpace(string(t))); err == nil && time.Since(last) < 10*time.Minute {
			log.Print("fenêtre MyVerse vide, mais déjà relancée il y a peu : on ne relance pas")
			return
		}
	}
	os.WriteFile(guard, []byte(time.Now().Format(time.RFC3339)), 0o600)
	log.Print("fenêtre MyVerse restée vide : redémarrage automatique")
	b.mu.Lock()
	b.restarting = true
	ecran := b.ecranPid
	b.mu.Unlock()
	killTree(ecran)
	killPID(b.appPID) // pas /T : ce bridge doit survivre pour relancer MyVerse
	time.Sleep(2 * time.Second)
	cleanupWebViews(filepath.Base(b.appExe))
	time.Sleep(time.Second)
	if err := startDetached(b.appExe); err != nil {
		log.Printf("relance : %v", err)
	}
	os.Exit(0)
}

func readInfo(r io.Reader) (nlInfo, error) {
	var info nlInfo
	ch := make(chan error, 1)
	go func() {
		line, err := bufio.NewReader(r).ReadString('\n')
		if err != nil && line == "" {
			ch <- err
			return
		}
		ch <- json.Unmarshal([]byte(strings.TrimSpace(line)), &info)
	}()
	select {
	case err := <-ch:
		if err != nil {
			return info, err
		}
	case <-time.After(20 * time.Second):
		return info, errors.New("aucune information reçue de Neutralino")
	}
	if info.Port == "" || info.ConnectToken == "" || info.ExtensionID == "" {
		return info, errors.New("informations Neutralino incomplètes")
	}
	return info, nil
}

func listenFirstFree() (net.Listener, int, error) {
	var last error
	for p := firstPort; p <= lastPort; p++ {
		ln, err := net.Listen("tcp4", fmt.Sprintf(":%d", p))
		if err == nil {
			return ln, p, nil
		}
		last = err
	}
	return nil, 0, last
}

// ------------------------------------------------------------ Neutralino

func (b *bridge) runNeutralino() error {
	path := "/?extensionId=" + b.info.ExtensionID + "&connectToken=" + b.info.ConnectToken
	var ws *wsConn
	var err error
	for i := 0; i < 40; i++ {
		ws, err = wsDial("127.0.0.1:"+b.info.Port, path, 5*time.Second)
		if err == nil {
			break
		}
		time.Sleep(250 * time.Millisecond)
	}
	if err != nil {
		return err
	}
	b.nlMu.Lock()
	b.nl = ws
	b.nlMu.Unlock()
	log.Print("connecté à VerseLight")
	b.toRegie("bridgeEvent", b.status())
	// battement : si l'écriture échoue, MyVerse est fermé, on s'arrête
	// (fermeture normale ou plantage : Windows coupe la connexion et la lecture échoue aussi)
	interval := nlPing
	go func() {
		for {
			time.Sleep(interval)
			if ws.Ping() != nil {
				ws.conn.Close()
				return
			}
		}
	}()
	for {
		msg, err := ws.ReadMessage()
		if err != nil {
			return err
		}
		var m struct {
			Event string          `json:"event"`
			Data  json.RawMessage `json:"data"`
		}
		if json.Unmarshal(msg, &m) != nil || m.Event == "" {
			continue // réponse à un appel natif
		}
		b.fromRegie(m.Event, m.Data)
	}
}

// toRegie envoie un événement à la page de la régie (Neutralino.events.on).
func (b *bridge) toRegie(event string, data any) {
	b.nlMu.Lock()
	ws := b.nl
	b.nlMu.Unlock()
	if ws == nil {
		return
	}
	payload, _ := json.Marshal(map[string]any{
		"id":          newUUID(),
		"method":      "app.broadcast",
		"accessToken": b.info.Token,
		"data":        map[string]any{"event": event, "data": data},
	})
	ws.WriteText(payload)
}

func (b *bridge) status() map[string]any {
	return map[string]any{
		"type": "status", "pcId": b.st.pcID(), "name": b.name, "port": b.port,
		"ips": localIPs(), "firewall": b.st.firewall(), "version": version,
		"connected": b.connectedIDs(),
	}
}

func (b *bridge) connectedIDs() []string {
	b.mu.Lock()
	defer b.mu.Unlock()
	ids := []string{}
	for p := range b.phones {
		ids = append(ids, p.deviceID)
	}
	return ids
}

func (b *bridge) sendDevices() {
	b.toRegie("bridgeEvent", map[string]any{"type": "devices", "devices": b.st.list(), "connected": b.connectedIDs()})
}

// événements qui prouvent que la page de MyVerse est vivante (pas une fenêtre blanche)
var regieEvents = map[string]bool{
	"appClientConnect": true, "alive": true, "state": true, "reply": true, "status": true, "devices": true,
	"pairStart": true, "pairStop": true, "revoke": true, "firewall": true, "ecranPid": true,
	"update.check": true, "update.install": true, "obs": true,
}

func (b *bridge) fromRegie(event string, data json.RawMessage) {
	if regieEvents[event] {
		b.mu.Lock()
		b.lastRegie = time.Now()
		b.mu.Unlock()
	}
	switch event {
	case "state":
		b.mu.Lock()
		b.state = data
		b.mu.Unlock()
		b.broadcastState()
	case "reply":
		var r struct {
			To  string          `json:"to"`
			Msg json.RawMessage `json:"msg"`
		}
		if json.Unmarshal(data, &r) == nil && len(r.Msg) > 0 {
			b.sendTo(r.To, r.Msg)
		}
	case "status":
		b.toRegie("bridgeEvent", b.status())
	case "devices":
		b.sendDevices()
	case "pairStart":
		secret := randomB64(32)
		key, _ := b64.DecodeString(secret)
		b.mu.Lock()
		b.pairKey = key
		b.pairUntil = time.Now().Add(10 * time.Minute)
		b.mu.Unlock()
		ips := localIPs()
		u := "verselight://pair?pcId=" + url.QueryEscape(b.st.pcID()) + "&name=" + url.QueryEscape(b.name) +
			"&ips=" + strings.Join(ips, ",") + fmt.Sprintf("&port=%d", b.port) + "&secret=" + secret
		b.toRegie("bridgeEvent", map[string]any{"type": "pairInfo", "url": u, "name": b.name, "ips": ips, "port": b.port, "firewall": b.st.firewall()})
	case "pairStop":
		b.mu.Lock()
		b.pairKey = nil
		b.mu.Unlock()
	case "revoke":
		var r struct {
			DeviceID string `json:"deviceId"`
		}
		if json.Unmarshal(data, &r) == nil && r.DeviceID != "" {
			b.st.revoke(r.DeviceID)
			b.mu.Lock()
			for p := range b.phones {
				if p.deviceID == r.DeviceID {
					p.ws.WriteText(mustJSON(map[string]any{"t": "auth.fail", "code": "revoked", "message": "Ce téléphone a été retiré dans VerseLight."}))
					p.ws.Close()
				}
			}
			b.mu.Unlock()
			b.sendDevices()
		}
	case "update.check":
		var r struct {
			Current string `json:"current"`
			Manual  bool   `json:"manual"`
		}
		json.Unmarshal(data, &r)
		go b.checkUpdate(r.Current, r.Manual)
	case "update.install":
		var r struct {
			URL string `json:"url"`
		}
		json.Unmarshal(data, &r)
		go b.installUpdate(r.URL)
	case "obs":
		b.setObs(data)
	case "firewall":
		go b.checkFirewall(true)
	case "ecranPid":
		var r struct {
			Pid int `json:"pid"`
		}
		if json.Unmarshal(data, &r) == nil {
			b.mu.Lock()
			b.ecranPid = r.Pid
			b.mu.Unlock()
		}
	}
}

func mustJSON(v any) []byte {
	out, _ := json.Marshal(v)
	return out
}

// ------------------------------------------------------------ téléphones

func (b *bridge) broadcastState() {
	b.mu.Lock()
	st := b.state
	list := make([]*phone, 0, len(b.phones))
	for p := range b.phones {
		list = append(list, p)
	}
	b.mu.Unlock()
	if len(st) == 0 {
		return
	}
	msg := stateMessage(st)
	for _, p := range list {
		p.ws.WriteText(msg)
	}
}

func stateMessage(st json.RawMessage) []byte {
	var m map[string]any
	if json.Unmarshal(st, &m) != nil || m == nil {
		m = map[string]any{}
	}
	m["t"] = "state"
	return mustJSON(m)
}

func (b *bridge) sendTo(deviceID string, msg []byte) {
	b.mu.Lock()
	defer b.mu.Unlock()
	for p := range b.phones {
		if deviceID == "" || p.deviceID == deviceID {
			p.ws.WriteText(msg)
		}
	}
}

type hello struct {
	T          string `json:"t"`
	V          int    `json:"v"`
	PcID       string `json:"pcId"`
	DeviceID   string `json:"deviceId"`
	DeviceName string `json:"deviceName"`
	Pair       bool   `json:"pair"`
	Cnonce     string `json:"cnonce"`
	Mac        string `json:"mac"`
}

func (b *bridge) handlePhone(w http.ResponseWriter, r *http.Request) {
	if !strings.EqualFold(r.Header.Get("Upgrade"), "websocket") {
		b.handleHTTP(w, r)
		return
	}
	ws, err := wsUpgrade(w, r)
	if err != nil {
		return
	}
	defer ws.Close()
	who := r.RemoteAddr
	fail := func(code, message string) {
		log.Printf("téléphone %s refusé : %s (%s)", who, code, message)
		ws.WriteText(mustJSON(map[string]any{"t": "auth.fail", "code": code, "message": message}))
	}

	// 1. hello
	ws.SetReadDeadline(time.Now().Add(10 * time.Second))
	raw, err := ws.ReadMessage()
	if err != nil {
		return
	}
	var h hello
	if json.Unmarshal(raw, &h) != nil || h.T != "hello" || h.DeviceID == "" || h.Cnonce == "" {
		fail("protocol", "Message de connexion invalide.")
		return
	}
	who = fmt.Sprintf("« %s » (%s)", h.DeviceName, r.RemoteAddr)
	if h.PcID != b.st.pcID() {
		fail("wrong_pc", "Ce n'est pas le PC jumelé.")
		return
	}
	var key []byte
	if h.Pair {
		b.mu.Lock()
		if b.pairKey != nil && time.Now().Before(b.pairUntil) {
			key = deriveKey(b.pairKey, h.PcID, h.DeviceID)
		}
		b.mu.Unlock()
		if key == nil {
			fail("pairing_closed", "Code QR expiré : ouvrez « Téléphone » dans VerseLight et scannez de nouveau.")
			return
		}
	} else {
		key = b.st.key(h.DeviceID)
		if key == nil {
			fail("revoked", "Ce téléphone n'est plus jumelé avec ce PC.")
			return
		}
	}

	// 2. défi : nonce + preuve que le PC connaît la clé
	nonce := randomB64(32)
	ws.WriteText(mustJSON(map[string]any{"t": "challenge", "v": protoVersion, "nonce": nonce, "proof": serverProof(key, h.Cnonce, nonce), "pcName": b.name}))

	// 3. réponse HMAC(clé, nonce + deviceId)
	raw, err = ws.ReadMessage()
	if err != nil {
		return
	}
	var a hello
	if json.Unmarshal(raw, &a) != nil || a.T != "auth" || !macEqual(a.Mac, clientMAC(key, nonce, h.DeviceID)) {
		if h.Pair {
			fail("bad_secret", "Code QR invalide : scannez le nouveau code.")
		} else {
			fail("revoked", "Ce téléphone n'est plus jumelé avec ce PC.")
		}
		return
	}
	name := strings.TrimSpace(h.DeviceName)
	if name == "" {
		name = "Téléphone"
	}
	if h.Pair {
		b.st.add(h.DeviceID, name, key)
		log.Printf("nouveau téléphone jumelé : %s", name)
	} else {
		b.st.seen(h.DeviceID, name)
	}
	ws.SetReadDeadline(time.Time{})
	ws.idle = phoneIdle

	log.Printf("téléphone connecté : %s", who)
	p := &phone{ws: ws, deviceID: h.DeviceID, name: name, key: key}
	b.mu.Lock()
	b.phones[p] = true
	st := b.state
	obs := b.obs
	b.mu.Unlock()
	ws.WriteText(mustJSON(map[string]any{"t": "welcome", "v": protoVersion, "pcName": b.name, "paired": h.Pair}))
	if len(st) > 0 {
		ws.WriteText(stateMessage(st))
	}
	if m := obsMessage(obs, key); m != nil {
		ws.WriteText(m)
	}
	b.sendDevices()
	defer func() {
		b.mu.Lock()
		delete(b.phones, p)
		b.mu.Unlock()
		log.Printf("téléphone déconnecté : %s", who)
		b.sendDevices()
	}()

	// 4. commandes : relayées telles quelles à la régie
	for {
		raw, err := ws.ReadMessage()
		if err != nil {
			return
		}
		var m map[string]any
		if json.Unmarshal(raw, &m) != nil {
			continue
		}
		t, _ := m["t"].(string)
		switch t {
		case "":
			continue
		case "ping":
			ws.WriteText(mustJSON(map[string]any{"t": "pong"}))
			continue
		}
		b.nlMu.Lock()
		online := b.nl != nil
		b.nlMu.Unlock()
		if !online {
			ws.WriteText(mustJSON(map[string]any{"t": "error", "rid": m["rid"], "message": "VerseLight démarre, réessayez."}))
			continue
		}
		m["device"] = p.deviceID
		m["deviceName"] = p.name
		b.toRegie("remoteCommand", m)
	}
}

// ------------------------------------------------------------ découverte UDP

func (b *bridge) serveDiscovery() {
	pc, err := net.ListenPacket("udp4", fmt.Sprintf(":%d", discoveryPort))
	if err != nil {
		log.Printf("découverte UDP indisponible : %v", err)
		return
	}
	defer pc.Close()
	buf := make([]byte, 512)
	vus := map[string]time.Time{}
	for {
		n, addr, err := pc.ReadFrom(buf)
		if err != nil {
			return
		}
		if !strings.HasPrefix(string(buf[:n]), "VERSELIGHT_DISCOVER") {
			continue
		}
		if ip := strings.Split(addr.String(), ":")[0]; time.Since(vus[ip]) > time.Minute {
			vus[ip] = time.Now()
			log.Printf("recherche du PC par le téléphone %s : réponse envoyée", ip)
		}
		reply := mustJSON(map[string]any{"t": "verselight", "v": protoVersion, "pcId": b.st.pcID(), "name": b.name, "port": b.port})
		pc.WriteTo(reply, addr)
	}
}

// localIPs : adresses IPv4 privées, les cartes Wi-Fi/Ethernet réelles d'abord.
func localIPs() []string {
	type cand struct {
		ip    string
		score int
	}
	var cs []cand
	ifaces, _ := net.Interfaces()
	for _, ifc := range ifaces {
		if ifc.Flags&net.FlagUp == 0 || ifc.Flags&net.FlagLoopback != 0 {
			continue
		}
		name := strings.ToLower(ifc.Name)
		score := 0
		for _, v := range []string{"vethernet", "virtualbox", "vmware", "hyper-v", "wsl", "docker", "vpn", "tailscale", "zerotier", "tap", "bluetooth", "loopback"} {
			if strings.Contains(name, v) {
				score += 10
			}
		}
		if strings.Contains(name, "wi-fi") || strings.Contains(name, "wlan") || strings.Contains(name, "wireless") {
			score--
		}
		addrs, _ := ifc.Addrs()
		for _, a := range addrs {
			ipn, ok := a.(*net.IPNet)
			if !ok {
				continue
			}
			ip := ipn.IP.To4()
			if ip == nil || !ip.IsPrivate() {
				continue
			}
			cs = append(cs, cand{ip.String(), score})
		}
	}
	sort.SliceStable(cs, func(i, j int) bool { return cs[i].score < cs[j].score })
	out := []string{}
	for _, c := range cs {
		out = append(out, c.ip)
	}
	return out
}

// ------------------------------------------------------------ pare-feu

// handleHTTP : page de test (ouvrir http://IP-du-PC:47900 sur le téléphone) et commandes locales entre copies de MyVerse.
func (b *bridge) handleHTTP(w http.ResponseWriter, r *http.Request) {
	local := strings.HasPrefix(r.RemoteAddr, "127.0.0.1:")
	switch {
	case local && r.URL.Path == "/instance":
		w.Header().Set("Content-Type", "application/json")
		b.mu.Lock()
		closing := b.closing
		b.mu.Unlock()
		w.Write(mustJSON(map[string]any{"myverse": true, "alive": !closing && b.regieAlive(20*time.Second), "closing": closing,
			"app": b.appPID, "age": time.Since(b.started).Seconds()}))
	case local && r.URL.Path == "/focus":
		w.Write([]byte("ok"))
		go focusWindowOf(b.appPID)
	case local && r.URL.Path == "/quit":
		w.Write([]byte("ok"))
		go func() {
			time.Sleep(200 * time.Millisecond)
			b.mu.Lock()
			ecran := b.ecranPid
			b.mu.Unlock()
			killTree(ecran)
			killTree(b.appPID)
			os.Exit(0)
		}()
	default:
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		fmt.Fprintf(w, `<!doctype html><meta name="viewport" content="width=device-width"><body style="font:20px sans-serif;background:#0A1028;color:#F7F4EC;padding:24px"><h1 style="color:#E4AF41">MyVerse ✓</h1><p>Le PC <b>%s</b> est joignable sur le port %d.</p><p>Si l'app ne se connecte pas, ouvrez MyVerse sur le PC et scannez de nouveau le code QR (bouton « Téléphone »).</p></body>`, b.name, b.port)
	}
}

func (b *bridge) checkFirewall(userAsked bool) {
	if firewallRulesPresent() {
		if b.st.firewall() != "ok" {
			b.st.setFirewall("ok")
		}
		b.toRegie("bridgeEvent", map[string]any{"type": "firewall", "firewall": "ok"})
		return
	}
	// au démarrage : aucune fenêtre Windows, on signale seulement (la demande se fait à l'ouverture de « Téléphone »)
	if !userAsked {
		if b.st.firewall() == "" || b.st.firewall() == "ok" {
			b.st.setFirewall("missing")
		}
		b.toRegie("bridgeEvent", map[string]any{"type": "firewall", "firewall": b.st.firewall()})
		return
	}
	res := addFirewallRules()
	b.st.setFirewall(res)
	log.Printf("pare-feu : %s", res)
	b.toRegie("bridgeEvent", map[string]any{"type": "firewall", "firewall": res})
}
