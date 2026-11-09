package main

// Réglages OBS transmis aux téléphones jumelés : le téléphone peut ensuite piloter OBS tout seul,
// même quand MyVerse est fermé sur le PC. Le mot de passe WebSocket d'OBS est chiffré avec la clé
// propre à chaque téléphone (AES-256-GCM, clé = HMAC-SHA256(clé du téléphone, "verselight-obs")).

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/json"
)

type obsConfig struct {
	Actif bool   `json:"actif"`
	Port  int    `json:"port"`
	Mdp   string `json:"mdp"`
}

func obsKey(deviceKey []byte) []byte {
	m := hmac.New(sha256.New, deviceKey)
	m.Write([]byte("verselight-obs"))
	return m.Sum(nil)
}

// sealFor chiffre un texte pour un téléphone : base64url(nonce 12 octets | texte chiffré + étiquette).
func sealFor(deviceKey []byte, plain string) (string, error) {
	block, err := aes.NewCipher(obsKey(deviceKey))
	if err != nil {
		return "", err
	}
	gcm, err := cipher.NewGCM(block)
	if err != nil {
		return "", err
	}
	nonce := make([]byte, gcm.NonceSize())
	if _, err := rand.Read(nonce); err != nil {
		return "", err
	}
	return b64.EncodeToString(gcm.Seal(nonce, nonce, []byte(plain), nil)), nil
}

func openFor(deviceKey []byte, sealed string) (string, error) {
	raw, err := b64.DecodeString(sealed)
	if err != nil {
		return "", err
	}
	block, err := aes.NewCipher(obsKey(deviceKey))
	if err != nil {
		return "", err
	}
	gcm, err := cipher.NewGCM(block)
	if err != nil {
		return "", err
	}
	if len(raw) < gcm.NonceSize() {
		return "", err
	}
	out, err := gcm.Open(nil, raw[:gcm.NonceSize()], raw[gcm.NonceSize():], nil)
	return string(out), err
}

// obsMessage : message « obs » pour un téléphone donné (nil si OBS n'est pas configuré dans MyVerse).
func obsMessage(cfg *obsConfig, deviceKey []byte) []byte {
	if cfg == nil {
		return nil
	}
	mdp := ""
	if cfg.Mdp != "" {
		s, err := sealFor(deviceKey, cfg.Mdp)
		if err != nil {
			return nil
		}
		mdp = s
	}
	port := cfg.Port
	if port <= 0 {
		port = 4455
	}
	return mustJSON(map[string]any{"t": "obs", "actif": cfg.Actif, "port": port, "mdp": mdp})
}

// setObs : nouveaux réglages venus de la régie ; envoyés aux téléphones connectés s'ils ont changé.
func (b *bridge) setObs(data json.RawMessage) {
	var c obsConfig
	if json.Unmarshal(data, &c) != nil {
		return
	}
	b.mu.Lock()
	same := b.obs != nil && *b.obs == c
	b.obs = &c
	list := make([]*phone, 0, len(b.phones))
	for p := range b.phones {
		list = append(list, p)
	}
	b.mu.Unlock()
	if same {
		return
	}
	for _, p := range list {
		if m := obsMessage(&c, p.key); m != nil {
			p.ws.WriteText(m)
		}
	}
}
