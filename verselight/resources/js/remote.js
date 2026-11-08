// VerseLight — télécommande Android : liaison avec verselight-bridge (extension Neutralino) et jumelage
(function () {
  const EXT = 'ca.mepc.verselight.bridge';
  const $ = (s) => document.querySelector(s);
  const R = window.VLRegie, B = window.Bible;
  const natif = typeof window.NL_PORT !== 'undefined' && window.Neutralino && R;
  const h = (tag, cls, txt) => { const e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; };
  if (!natif) { const b = $('#tel-btn'); if (b) b.hidden = true; return; }

  let pret = false;
  function versBridge(ev, data) {
    if (!pret) return;
    Neutralino.extensions.dispatch(EXT, ev, data || {}).catch(() => {});
  }

  // ------------------------------------------------------------ état envoyé aux téléphones
  function etatTel() {
    const e = R.etat(), c = R.cible();
    let titre = '', suivant = '';
    if (c && c.type === 'chant') {
      const s = R.trouver(c.id);
      if (s) {
        titre = s.title;
        const n = s.sections[c.i + 1];
        suivant = n ? (n.label || 'Strophe') + ' · ' + n.text.split('\n')[0] : '';
      }
    } else if (c && c.type === 'verset') {
      const n = B.verses(c.b, c.c).length;
      if (c.v2 < n) suivant = B.refLabel(c.b, c.c, c.v2 + 1) + ' · ' + B.verses(c.b, c.c)[c.v2];
      else if (c.c < B.chapterCount(c.b)) suivant = B.refLabel(c.b, c.c + 1, 1) + ' · ' + B.verses(c.b, c.c + 1)[0];
    }
    const obs = window.VLObs || {};
    return {
      mode: e.mode, lignes: e.lignes, ref: e.ref, titre, suivant,
      chant: c && c.type === 'chant' ? { id: c.id, i: c.i } : null,
      ecranOuvert: R.ecranOuvert(),
      obsActif: obs.actif ? !!obs.actif() : false, obsConnecte: obs.connecte ? !!obs.connecte() : false,
    };
  }
  let minuterie = null, dernier = '';
  function pousser(force) {
    clearTimeout(minuterie);
    minuterie = setTimeout(() => {
      const e = etatTel(), k = JSON.stringify(e);
      if (!force && k === dernier) return;
      dernier = k;
      versBridge('state', e);
    }, 60);
  }
  R.surChangement(() => pousser(false));
  setInterval(() => pousser(false), 2000); // OBS connecté/déconnecté

  // ------------------------------------------------------------ commandes reçues des téléphones
  function chercherChants(q) {
    const tous = R.chants();
    q = (q || '').trim();
    let res;
    if (/^\d+$/.test(q)) {
      const n = parseInt(q, 10);
      res = tous.filter((c) => { const m = c.title.match(/^\s*(\d+)/); return m && parseInt(m[1], 10) === n; });
    } else {
      const nq = B.norm(q);
      res = !nq ? tous.slice() : tous.filter((c) => B.norm(c.title).includes(nq));
      if (nq && res.length < 50) {
        const ids = new Set(res.map((c) => c.id));
        res = res.concat(tous.filter((c) => !ids.has(c.id) && B.norm(c.sections.map((s) => s.text).join(' ')).includes(nq)));
      }
    }
    return res.slice(0, 60).map((c) => ({ id: c.id, title: c.title.replace(/\s+/g, ' '), author: c.author || '' }));
  }

  function traiter(d) {
    const rep = (msg) => versBridge('reply', { to: d.device, msg: { rid: d.rid, ...msg } });
    switch (d.t) {
      case 'bible.preview': {
        const a = R.apercu(d.ref);
        rep(a ? { t: 'preview', ...a } : { t: 'error', message: `Référence introuvable : « ${d.ref || ''} »` });
        break;
      }
      case 'bible.show':
        if (!R.afficherRef(d.ref)) rep({ t: 'error', message: `Référence introuvable : « ${d.ref || ''} »` });
        break;
      case 'song.search':
        rep({ t: 'songs', q: d.q || '', liste: chercherChants(d.q) });
        break;
      case 'song.open': {
        const c = R.trouver(d.id);
        rep(c ? { t: 'song', id: c.id, title: c.title.replace(/\s+/g, ' '), author: c.author || '', sections: c.sections.map((s) => ({ label: s.label || '', text: s.text })) }
          : { t: 'error', message: 'Chant introuvable.' });
        break;
      }
      case 'song.show':
        if (R.trouver(d.id)) R.afficherSection(d.id, +d.i || 0); else rep({ t: 'error', message: 'Chant introuvable.' });
        break;
      case 'next': R.bouger(1); break;
      case 'prev': R.bouger(-1); break;
      case 'clear': R.effacer(); break;
      case 'state.get': pousser(true); break;
      default: rep({ t: 'error', message: 'Commande inconnue : ' + d.t });
    }
  }
  Neutralino.events.on('remoteCommand', (e) => { try { traiter(e.detail || {}); } catch (x) { console.error(x); } });

  // ------------------------------------------------------------ fenêtre « Téléphone »
  const dlg = $('#tel-dlg');
  let appareils = [], connectes = [], parefeu = '';
  function majParefeu() {
    const p = $('#tel-pf');
    const msg = {
      ok: 'Pare-feu Windows : autorisé (réseau privé).',
      refused: 'Pare-feu : autorisation refusée. Le téléphone ne pourra pas se connecter. Cliquez « Autoriser dans le pare-feu » puis acceptez la demande Windows.',
      error: 'Pare-feu : la règle n’a pas pu être ajoutée. Cliquez « Autoriser dans le pare-feu ».',
    }[parefeu] || 'Pare-feu : vérification…';
    p.textContent = msg + ' Le Wi-Fi du PC doit être en réseau « Privé ».';
    p.className = 'muet' + (parefeu && parefeu !== 'ok' ? ' alerte' : '');
    $('#tel-pf-btn').hidden = parefeu === 'ok';
  }
  function majAppareils() {
    const L = $('#tel-liste');
    L.textContent = '';
    if (!appareils.length) { L.appendChild(h('li', 'muet', 'Aucun téléphone jumelé.')); return; }
    for (const a of appareils) {
      const li = h('li');
      const en = connectes.includes(a.deviceId);
      li.append(h('span', 'point' + (en ? ' on' : '')), h('b', '', a.name || 'Téléphone'),
        h('small', '', en ? ' connecté' : (a.lastSeen ? ' vu le ' + new Date(a.lastSeen).toLocaleString('fr-CA') : '')));
      const x = h('button', 'btn petit danger', 'Révoquer');
      x.type = 'button';
      x.onclick = () => { if (confirm(`Révoquer « ${a.name} » ? Il devra scanner un nouveau code.`)) versBridge('revoke', { deviceId: a.deviceId }); };
      li.appendChild(x);
      L.appendChild(li);
    }
  }
  function evenement(d) {
    if (d.firewall) { parefeu = d.firewall; majParefeu(); }
    if (d.type === 'pairInfo') {
      const q = qrcode(0, 'M');
      q.addData(d.url);
      q.make();
      $('#tel-qr').innerHTML = q.createSvgTag(5, 4);
      $('#tel-info').textContent = `${d.name} · ${(d.ips || []).join(', ') || 'aucune adresse réseau'} · port ${d.port}`;
    } else if (d.type === 'devices') {
      appareils = d.devices || []; connectes = d.connected || [];
      majAppareils();
      majBouton();
    } else if (d.type === 'status') {
      connectes = d.connected || [];
      majBouton();
    }
  }
  Neutralino.events.on('bridgeEvent', (e) => evenement(e.detail || {}));

  function majBouton() {
    const b = $('#tel-btn');
    b.classList.toggle('bien', pret && connectes.length > 0);
    b.title = !pret ? 'Module réseau non démarré' : connectes.length ? connectes.length + ' téléphone(s) connecté(s)' : 'Aucun téléphone connecté';
  }
  $('#tel-btn').addEventListener('click', () => {
    $('#tel-qr').innerHTML = '';
    $('#tel-info').textContent = pret ? 'Préparation du code…'
      : 'Le module réseau n’est pas démarré : vérifiez que le dossier extensions\\bridge (verselight-bridge.exe) est bien à côté de VerseLight.exe, puis relancez VerseLight.';
    majParefeu();
    majAppareils();
    dlg.showModal();
    versBridge('pairStart');
    versBridge('devices');
  });
  dlg.addEventListener('close', () => { versBridge('pairStop'); $('#tel-qr').innerHTML = ''; });
  $('#tel-pf-btn').addEventListener('click', () => { parefeu = ''; majParefeu(); versBridge('firewall'); });

  // ------------------------------------------------------------ connexion au bridge
  function bridgePret() {
    if (pret) return;
    pret = true;
    versBridge('status');
    versBridge('devices');
    pousser(true);
    majBouton();
  }
  Neutralino.events.on('extensionReady', (e) => { if (e.detail === EXT) bridgePret(); });
  Neutralino.events.on('extClientDisconnect', (e) => { if (e.detail === EXT) { pret = false; connectes = []; majBouton(); } });
  Neutralino.events.on('ready', () => {
    Neutralino.extensions.getStats().then((s) => { if (s && s.connected && s.connected.includes(EXT)) bridgePret(); }).catch(() => {});
  });
  majBouton();
})();
