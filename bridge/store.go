package main

// Appareils jumelés (%APPDATA%\VerseLight\devices.json) et calculs HMAC.

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"time"
)

type device struct {
	DeviceID string `json:"deviceId"`
	Name     string `json:"name"`
	Key      string `json:"key"` // secret dérivé (base64url), jamais transmis
	Paired   string `json:"paired"`
	LastSeen string `json:"lastSeen,omitempty"`
}

type storeData struct {
	PcID     string   `json:"pcId"`
	Firewall string   `json:"firewall2,omitempty"` // règles MyVerse (tous profils) : "", "ok", "refused", "error"
	Devices  []device `json:"devices"`
}

type store struct {
	mu   sync.Mutex
	path string
	d    storeData
}

var b64 = base64.RawURLEncoding

func randomB64(n int) string {
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return b64.EncodeToString(b)
}

func newUUID() string {
	b := make([]byte, 16)
	rand.Read(b)
	b[6] = b[6]&0x0f | 0x40
	b[8] = b[8]&0x3f | 0x80
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}

func hmacB64(key []byte, msg string) string {
	m := hmac.New(sha256.New, key)
	m.Write([]byte(msg))
	return b64.EncodeToString(m.Sum(nil))
}

// deriveKey : clé propre à un appareil, tirée du secret du QR.
func deriveKey(pairSecret []byte, pcID, deviceID string) []byte {
	m := hmac.New(sha256.New, pairSecret)
	m.Write([]byte("verselight-pair|" + pcID + "|" + deviceID))
	return m.Sum(nil)
}

func serverProof(key []byte, cnonce, nonce string) string {
	return hmacB64(key, "verselight-srv|"+cnonce+"|"+nonce)
}

func clientMAC(key []byte, nonce, deviceID string) string {
	return hmacB64(key, nonce+deviceID)
}

func macEqual(a, b string) bool { return hmac.Equal([]byte(a), []byte(b)) }

func openStore(dir string) (*store, error) {
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return nil, err
	}
	s := &store{path: filepath.Join(dir, "devices.json")}
	if b, err := os.ReadFile(s.path); err == nil {
		if err := json.Unmarshal(b, &s.d); err != nil {
			os.Rename(s.path, s.path+".abime")
			s.d = storeData{}
		}
	}
	if s.d.PcID == "" {
		s.d.PcID = newUUID()
		if err := s.saveLocked(); err != nil {
			return nil, err
		}
	}
	return s, nil
}

func (s *store) saveLocked() error {
	b, err := json.MarshalIndent(s.d, "", "  ")
	if err != nil {
		return err
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, b, 0o600); err != nil {
		return err
	}
	return os.Rename(tmp, s.path)
}

func (s *store) pcID() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.d.PcID
}

func (s *store) firewall() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.d.Firewall
}

func (s *store) setFirewall(v string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.d.Firewall = v
	s.saveLocked()
}

func (s *store) key(deviceID string) []byte {
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, d := range s.d.Devices {
		if d.DeviceID == deviceID {
			k, err := b64.DecodeString(d.Key)
			if err != nil {
				return nil
			}
			return k
		}
	}
	return nil
}

func (s *store) add(deviceID, name string, key []byte) {
	s.mu.Lock()
	defer s.mu.Unlock()
	now := time.Now().Format(time.RFC3339)
	for i, d := range s.d.Devices {
		if d.DeviceID == deviceID {
			s.d.Devices[i] = device{DeviceID: deviceID, Name: name, Key: b64.EncodeToString(key), Paired: now, LastSeen: now}
			s.saveLocked()
			return
		}
	}
	s.d.Devices = append(s.d.Devices, device{DeviceID: deviceID, Name: name, Key: b64.EncodeToString(key), Paired: now, LastSeen: now})
	s.saveLocked()
}

func (s *store) seen(deviceID, name string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for i, d := range s.d.Devices {
		if d.DeviceID == deviceID {
			s.d.Devices[i].LastSeen = time.Now().Format(time.RFC3339)
			if name != "" {
				s.d.Devices[i].Name = name
			}
			s.saveLocked()
		}
	}
}

func (s *store) revoke(deviceID string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	for i, d := range s.d.Devices {
		if d.DeviceID == deviceID {
			s.d.Devices = append(s.d.Devices[:i], s.d.Devices[i+1:]...)
			s.saveLocked()
			return true
		}
	}
	return false
}

// list : copie sans les clés, pour la régie.
func (s *store) list() []map[string]string {
	s.mu.Lock()
	defer s.mu.Unlock()
	out := []map[string]string{}
	for _, d := range s.d.Devices {
		out = append(out, map[string]string{"deviceId": d.DeviceID, "name": d.Name, "paired": d.Paired, "lastSeen": d.LastSeen})
	}
	return out
}
