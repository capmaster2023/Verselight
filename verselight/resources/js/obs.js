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
      } else if (m.op === 2) { ok = true; pastille('OBS : connecté', true); if (window.VLJournal) VLJournal.info('OBS connecté (port ' + cfg.port + ')'); }
      else if (m.op === 7) { const r = attente.get(m.d.requestId); if (r) { attente.delete(m.d.requestId); r(m.d); } }
    };
    ws.onclose = (ev) => {
      if (ok && window.VLJournal) VLJournal.info('OBS déconnecté (code ' + ev.code + ')');
      if (ev.code === 4009 && window.VLJournal) VLJournal.erreur('OBS : mot de passe WebSocket incorrect');
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
  // « Afficher » : la source MyVerse passe AU PREMIER PLAN de la scène en cours (créée si besoin, rien à configurer).
  // « Retirer » : la source temporaire est supprimée et la scène redevient comme avant (sources remises à leur place).
  const AUTO = 'MyVerse (temporaire)';
  const places = new Map(); // sources de l'utilisateur déplacées au premier plan : leur place d'origine
  const ok_ = (r) => !!(r && r.requestStatus && r.requestStatus.result);

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
  async function scenePrincipale() {
    const r = await req('GetCurrentProgramScene');
    return r && r.responseData ? (r.responseData.currentProgramSceneName || r.responseData.sceneName) : null;
  }
  async function elements(scene) {
    const r = await req('GetSceneItemList', { sceneName: scene });
    return r && r.responseData ? r.responseData.sceneItems : [];
  }
  async function premierPlan(scene, id) {
    const n = (await elements(scene)).length;
    if (n > 0) await req('SetSceneItemIndex', { sceneName: scene, sceneItemId: id, sceneItemIndex: n - 1 });
  }

  async function montrer() {
    const scene = await scenePrincipale();
    if (!scene) return;
    const items = await elements(scene);
    let id = null;
    const auto = items.find((it) => it.sourceName === AUTO);
    if (auto) id = auto.sceneItemId;
    if (id === null) for (const it of items) {
      if (it.sourceName === cfg.source || await sourceMyVerse(it)) {
        const cle = scene + '|' + it.sceneItemId;
        if (!places.has(cle)) places.set(cle, { scene, id: it.sceneItemId, index: it.sceneItemIndex, visible: it.sceneItemEnabled });
        if (!it.sceneItemEnabled) await req('SetSceneItemEnabled', { sceneName: scene, sceneItemId: it.sceneItemId, sceneItemEnabled: true });
        id = it.sceneItemId;
        break;
      }
    }
    if (id === null) {
      // aucune source MyVerse dans cette scène : source navigateur temporaire
      if (ok_(await req('GetInputSettings', { inputName: AUTO }))) {
        const r = await req('CreateSceneItem', { sceneName: scene, sourceName: AUTO, sceneItemEnabled: true });
        if (ok_(r)) id = r.responseData.sceneItemId;
      } else {
        const dossier = window.VLRegie && window.VLRegie.dossierObs ? await window.VLRegie.dossierObs() : '';
        if (!dossier) return;
        const r = await req('CreateInput', {
          sceneName: scene, inputName: AUTO, inputKind: 'browser_source', sceneItemEnabled: true,
          inputSettings: { is_local_file: true, local_file: dossier.replace(/\\/g, '/') + '/ecran.html', width: 1920, height: 1080 },
        });
        if (ok_(r)) id = r.responseData.sceneItemId;
      }
    }
    if (id !== null) await premierPlan(scene, id);
  }

  async function cacher() {
    await req('RemoveInput', { inputName: AUTO });
    for (const p of places.values()) {
      await req('SetSceneItemEnabled', { sceneName: p.scene, sceneItemId: p.id, sceneItemEnabled: false });
      await req('SetSceneItemIndex', { sceneName: p.scene, sceneItemId: p.id, sceneItemIndex: p.index });
    }
    places.clear();
  }

  // une commande à la fois (Afficher puis Retirer très vite restent dans l'ordre)
  let file = Promise.resolve();
  function oeil(visible) {
    if (!cfg.actif || !ok) return Promise.resolve();
    file = file.then(() => (visible ? montrer() : cacher())).catch(() => {});
    return file;
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
