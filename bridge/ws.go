package main

// WebSocket minimal (RFC 6455), bibliothèque standard uniquement :
// côté serveur pour les téléphones, côté client pour Neutralino.

import (
	"bufio"
	"crypto/rand"
	"crypto/sha1"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"
)

const wsGUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
const wsMaxMessage = 4 << 20

type wsConn struct {
	conn   net.Conn
	br     *bufio.Reader
	client bool          // un client masque ses trames
	idle   time.Duration // délai max sans aucune trame (pings compris), 0 = sans limite
	wmu    sync.Mutex
	closed bool
}

func wsAcceptKey(key string) string {
	h := sha1.Sum([]byte(key + wsGUID))
	return base64.StdEncoding.EncodeToString(h[:])
}

// wsUpgrade transforme une requête HTTP en connexion WebSocket (serveur).
func wsUpgrade(w http.ResponseWriter, r *http.Request) (*wsConn, error) {
	if !strings.EqualFold(r.Header.Get("Upgrade"), "websocket") {
		http.Error(w, "WebSocket attendu", http.StatusBadRequest)
		return nil, errors.New("pas de demande websocket")
	}
	key := r.Header.Get("Sec-WebSocket-Key")
	if key == "" {
		http.Error(w, "clé manquante", http.StatusBadRequest)
		return nil, errors.New("clé manquante")
	}
	hj, ok := w.(http.Hijacker)
	if !ok {
		return nil, errors.New("hijack impossible")
	}
	conn, rw, err := hj.Hijack()
	if err != nil {
		return nil, err
	}
	resp := "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + wsAcceptKey(key) + "\r\n\r\n"
	if _, err := rw.WriteString(resp); err != nil {
		conn.Close()
		return nil, err
	}
	if err := rw.Flush(); err != nil {
		conn.Close()
		return nil, err
	}
	return &wsConn{conn: conn, br: rw.Reader}, nil
}

// wsDial ouvre une connexion WebSocket cliente (ws://host:port/chemin).
func wsDial(hostport, path string, timeout time.Duration) (*wsConn, error) {
	conn, err := net.DialTimeout("tcp", hostport, timeout)
	if err != nil {
		return nil, err
	}
	kb := make([]byte, 16)
	rand.Read(kb)
	key := base64.StdEncoding.EncodeToString(kb)
	req := "GET " + path + " HTTP/1.1\r\nHost: " + hostport + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n"
	conn.SetDeadline(time.Now().Add(timeout))
	if _, err := conn.Write([]byte(req)); err != nil {
		conn.Close()
		return nil, err
	}
	br := bufio.NewReader(conn)
	resp, err := http.ReadResponse(br, nil)
	if err != nil {
		conn.Close()
		return nil, err
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusSwitchingProtocols || resp.Header.Get("Sec-WebSocket-Accept") != wsAcceptKey(key) {
		conn.Close()
		return nil, fmt.Errorf("poignée de main refusée : %s", resp.Status)
	}
	conn.SetDeadline(time.Time{})
	return &wsConn{conn: conn, br: br, client: true}, nil
}

func (c *wsConn) writeFrame(op byte, payload []byte) error {
	c.wmu.Lock()
	defer c.wmu.Unlock()
	if c.closed {
		return net.ErrClosed
	}
	hdr := make([]byte, 0, 14)
	hdr = append(hdr, 0x80|op)
	mask := byte(0)
	if c.client {
		mask = 0x80
	}
	n := len(payload)
	switch {
	case n < 126:
		hdr = append(hdr, mask|byte(n))
	case n <= 0xFFFF:
		hdr = append(hdr, mask|126, byte(n>>8), byte(n))
	default:
		hdr = append(hdr, mask|127)
		var b [8]byte
		binary.BigEndian.PutUint64(b[:], uint64(n))
		hdr = append(hdr, b[:]...)
	}
	data := payload
	if c.client {
		var k [4]byte
		rand.Read(k[:])
		hdr = append(hdr, k[:]...)
		data = make([]byte, n)
		for i := range payload {
			data[i] = payload[i] ^ k[i%4]
		}
	}
	c.conn.SetWriteDeadline(time.Now().Add(10 * time.Second))
	if _, err := c.conn.Write(append(hdr, data...)); err != nil {
		return err
	}
	return nil
}

func (c *wsConn) WriteText(b []byte) error { return c.writeFrame(0x1, b) }
func (c *wsConn) Ping() error              { return c.writeFrame(0x9, nil) }

// ReadMessage renvoie le prochain message texte ou binaire complet.
func (c *wsConn) ReadMessage() ([]byte, error) {
	var msg []byte
	for {
		if c.idle > 0 { // chaque trame, même un ping, prouve que l'autre côté est vivant
			c.conn.SetReadDeadline(time.Now().Add(c.idle))
		}
		var h [2]byte
		if _, err := io.ReadFull(c.br, h[:]); err != nil {
			return nil, err
		}
		fin := h[0]&0x80 != 0
		op := h[0] & 0x0F
		masked := h[1]&0x80 != 0
		n := uint64(h[1] & 0x7F)
		if n == 126 {
			var b [2]byte
			if _, err := io.ReadFull(c.br, b[:]); err != nil {
				return nil, err
			}
			n = uint64(binary.BigEndian.Uint16(b[:]))
		} else if n == 127 {
			var b [8]byte
			if _, err := io.ReadFull(c.br, b[:]); err != nil {
				return nil, err
			}
			n = binary.BigEndian.Uint64(b[:])
		}
		if n > wsMaxMessage || uint64(len(msg))+n > wsMaxMessage {
			return nil, errors.New("message trop long")
		}
		var key [4]byte
		if masked {
			if _, err := io.ReadFull(c.br, key[:]); err != nil {
				return nil, err
			}
		}
		payload := make([]byte, n)
		if _, err := io.ReadFull(c.br, payload); err != nil {
			return nil, err
		}
		if masked {
			for i := range payload {
				payload[i] ^= key[i%4]
			}
		}
		switch op {
		case 0x8: // fermeture
			c.writeFrame(0x8, nil)
			return nil, io.EOF
		case 0x9: // ping
			c.writeFrame(0xA, payload)
		case 0xA: // pong
		case 0x0, 0x1, 0x2:
			msg = append(msg, payload...)
			if fin {
				return msg, nil
			}
		default:
			return nil, errors.New("trame inconnue")
		}
	}
}

func (c *wsConn) SetReadDeadline(t time.Time) { c.conn.SetReadDeadline(t) }

func (c *wsConn) Close() {
	c.wmu.Lock()
	already := c.closed
	c.wmu.Unlock()
	if already {
		return
	}
	c.writeFrame(0x8, []byte{0x03, 0xE8})
	c.wmu.Lock()
	c.closed = true
	c.wmu.Unlock()
	c.conn.Close()
}
