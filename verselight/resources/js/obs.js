// VerseLight — pilotage de l'œil d'une source dans OBS (obs-websocket 5, inclus dans OBS 28 et plus)
(function () {
  const $ = (s) => document.querySelector(s);
  const lire = (k, d) => { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : d; } catch (e) { return d; } };
  let cfg = Object.assign({ actif: false, port: 4455, mdp: '', source: 'BIBLE' }, lire('verselight-obs', {}));
  let ws = null, ok = false, n = 1, attente = new Map(), reco = null;

  async function sha(b64) { const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(b64)); return btoa(String.fromCharCode(...new Uint8Array(d))); }
  function pastille(t, bien) { const p = $('#obs-etat'); if (p) { p.textContent = t; p.className = 'obs-etat' + (bien ? ' bien' : ''); } }

  function connecter() {
    clearTimeout(reco);
    if (ws) try { ws.close(); } catch (e) {}
    ok = false;
    if (!cfg.actif) { pastille('OBS : désactivé'); return; }
    pastille('OBS : connexion…');
    ws = new WebSocket('ws://127.0.0.1:' + cfg.port);
    ws.onmessage = async (ev) => {
      const m = JSON.parse(ev.data);
      if (m.op === 0) {
        const id = { op: 1, d: { rpcVersion: 1 } };
        if (m.d.authentication) id.d.authentication = await sha((await sha(cfg.mdp + m.d.authentication.salt)) + m.d.authentication.challenge);
        ws.send(JSON.stringify(id));
      } else if (m.op === 2) { ok = true; pastille('OBS : connecté', true); }
      else if (m.op === 7) { const r = attente.get(m.d.requestId); if (r) { attente.delete(m.d.requestId); r(m.d); } }
    };
    ws.onclose = (ev) => {
      ok = false;
      pastille(ev.code === 4009 ? 'OBS : mot de passe incorrect' : 'OBS : non connecté');
      if (cfg.actif && ev.code !== 4009) reco = setTimeout(connecter, 5000);
    };
  }
  function req(type, data) {
    return new Promise((res) => {
      if (!ok) return res(null);
      const id = String(n++);
      attente.set(id, res);
      ws.send(JSON.stringify({ op: 6, d: { requestType: type, requestId: id, requestData: data || {} } }));
      setTimeout(() => { if (attente.has(id)) { attente.delete(id); res(null); } }, 3000);
    });
  }
  // une source qui affiche MyVerse : capture de la fenêtre « MyVerse — écran » ou source navigateur VerseLight-OBS
  const estMyVerse = new Map();
  async function sourceMyVerse(it) {
    if (!['window_capture', 'game_capture', 'browser_source'].includes(it.inputKind)) return false;
    if (estMyVerse.has(it.sourceName)) return estMyVerse.get(it.sourceName);
    const r = await req('GetInputSettings', { inputName: it.sourceName });
    const s = (r && r.responseData && r.responseData.inputSettings) || {};
    const oui = /MyVerse|VerseLight/i.test([s.window, s.local_file, s.url].join(' '));
    estMyVerse.set(it.sourceName, oui);
    return oui;
  }
  // allume ou éteint l'œil de la source dans toutes les scènes où elle se trouve
  async function oeil(visible) {
    if (!cfg.actif || !ok) return;
    const scenes = await req('GetSceneList');
    if (!scenes || !scenes.responseData) return;
    let trouve = false;
    for (const sc of scenes.responseData.scenes) {
      const items = await req('GetSceneItemList', { sceneName: sc.sceneName });
      for (const it of (items && items.responseData ? items.responseData.sceneItems : [])) {
        if (it.sourceName === cfg.source || await sourceMyVerse(it)) {
          trouve = true;
          if (it.sceneItemEnabled !== visible) await req('SetSceneItemEnabled', { sceneName: sc.sceneName, sceneItemId: it.sceneItemId, sceneItemEnabled: visible });
        }
      }
    }
    if (!trouve) window.VL && window.VL.toast(`OBS : aucune source MyVerse trouvée (ni capture de la fenêtre « MyVerse — écran », ni source nommée « ${cfg.source} »).`);
  }
  window.VLObs = { oeil, connecte: () => ok, actif: () => cfg.actif };

  // réglages
  $('#obs-btn').addEventListener('click', () => {
    $('#obs-actif').checked = cfg.actif; $('#obs-port').value = cfg.port; $('#obs-mdp').value = cfg.mdp; $('#obs-source').value = cfg.source;
    $('#obs-dlg').showModal();
  });
  $('#obs-dlg').addEventListener('close', () => {
    if ($('#obs-dlg').returnValue !== 'ok') return;
    cfg = { actif: $('#obs-actif').checked, port: +$('#obs-port').value || 4455, mdp: $('#obs-mdp').value, source: $('#obs-source').value.trim() || 'BIBLE' };
    try { localStorage.setItem('verselight-obs', JSON.stringify(cfg)); } catch (e) {}
    connecter();
  });
  connecter();
})();
