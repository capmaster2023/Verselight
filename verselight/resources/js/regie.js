// VerseLight — régie : Bible Louis Segond 1910 et chants, projetés sur une seconde fenêtre
(function () {
  const $ = (s) => document.querySelector(s);
  const B = window.Bible, S = window.Songs;
  const h = (tag, cls, txt) => { const e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; };
  const lire = (k, d) => { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : d; } catch (e) { return d; } };
  const ecrire = (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { toast('Impossible d’enregistrer : mémoire du navigateur pleine ou bloquée.'); } };
  function toast(t) { if (window.VLJournal) window.VLJournal.info('message : ' + t); const e = $('#toast'); e.textContent = t; e.classList.add('vu'); clearTimeout(e._t); e._t = setTimeout(() => e.classList.remove('vu'), 3200); }

  // ------------------------------------------------------------ écran
  let ecran = null;
  let etat = { mode: 'vide', lignes: [], ref: '', theme: lire('verselight-theme', 'nuit'), taille: lire('verselight-taille', 1) };
  etat.presentation = lire('verselight-presentation', 'plein');
  let cible = null; // ce qui est à l'écran : { type: 'verset', b, c, v1, v2 } ou { type: 'chant', id, i }

  // ----- application Windows (Neutralino + WebView2) : l'écran est une vraie fenêtre native
  const natif = typeof window.NL_PORT !== 'undefined' && window.Neutralino;
  let ecranPid = 0;
  if (natif) {
    try { Neutralino.init(); } catch (e) {}
    Neutralino.events.on('windowClose', async () => {
      if (ecranPid > 0) try { await Neutralino.os.execCommand('taskkill /PID ' + ecranPid + ' /T /F', { background: true }); } catch (e) {}
      Neutralino.app.exit();
    });
  }
  // ----- choix de l'écran de projection (comme « Projecteur plein écran » d'OBS)
  const PS = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -Name D -Namespace W -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool SetProcessDPIAware();'; [W.D]::SetProcessDPIAware() | Out-Null; " +
    "$s = [System.Windows.Forms.Screen]::AllScreens | ForEach-Object { [pscustomobject]@{ p = $_.Primary; x = $_.Bounds.X; y = $_.Bounds.Y; w = $_.Bounds.Width; h = $_.Bounds.Height } }; " +
    "$n = @(Get-CimInstance -Namespace root\\wmi WmiMonitorID -ErrorAction SilentlyContinue | ForEach-Object { ($_.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join '' }); " +
    "@{ ecrans = @($s); noms = @($n) } | ConvertTo-Json -Compress -Depth 4";
  async function listerEcrans() {
    try {
      const r = await Neutralino.os.execCommand('powershell -NoProfile -ExecutionPolicy Bypass -Command "' + PS.replace(/"/g, '\\"') + '"');
      const d = JSON.parse(r.stdOut.trim());
      const ecrans = d.ecrans || [], noms = d.noms || [];
      return ecrans.map((e, i) => ({ ...e, nom: (ecrans.length === noms.length && noms[i]) || ('Écran ' + (i + 1)) }));
    } catch (e) { return [{ p: true, x: 0, y: 0, w: window.screen.width, h: window.screen.height, nom: 'Écran principal' }]; }
  }
  async function fermerEcranNatif() {
    if (ecranPid > 0) try { await Neutralino.os.execCommand('taskkill /PID ' + ecranPid + ' /T /F'); } catch (e) {}
    ecranPid = 0;
  }
  async function ouvrirSur(choix) {
    await fermerEcranNatif();
    const plein = choix.mode !== 'fenetre';
    const opts = plein
      ? { x: choix.x + 40, y: choix.y + 40, width: Math.min(1280, choix.w - 80), height: Math.min(720, choix.h - 80), fullScreen: true, borderless: true }
      : { x: choix.x + 80, y: choix.y + 80, width: Math.round(choix.w * 0.6), height: Math.round(choix.w * 0.6 * 9 / 16), fullScreen: false, borderless: false };
    try {
      const w = await Neutralino.window.create('http://127.0.0.1:' + window.NL_PORT + '/ecran.html', {
        title: 'MyVerse — écran', icon: '/resources/icone.png', enableInspector: false,
        alwaysOnTop: false, resizable: true, exitProcessOnClose: true, ...opts, useSavedState: false, processArgs: '--enable-server=false --enable-extensions=false',
      });
      ecranPid = w && w.pid ? w.pid : -1;
      ecrire('verselight-ecran-choisi', choix);
      toast(plein ? `Projection plein écran sur ${choix.nom}.` : 'Projection en fenêtre.');
      setTimeout(envoyer, 800);
      setTimeout(envoyer, 2000);
    } catch (e) { if (window.VLJournal) window.VLJournal.erreur('ouverture de l’écran : ' + JSON.stringify(e)); toast('Impossible d’ouvrir l’écran : ' + (e.message || e)); }
  }
  // ----- sortie OBS : dossier avec ecran.html (à mettre en « Source navigateur › Fichier local »)
  let dossierObs = '';
  async function preparerObs() {
    if (!natif || dossierObs) return dossierObs;
    try {
      const base = (await Neutralino.os.getPath('data')).replace(/\\/g, '/') + '/VerseLight-OBS';
      for (const d of [base, base + '/css', base + '/fonts']) try { await Neutralino.filesystem.createDirectory(d); } catch (e) {}
      const fichiers = ['ecran.html', 'css/commun.css', 'fonts/literata-latin-400-normal.woff2', 'fonts/literata-latin-400-italic.woff2', 'fonts/literata-latin-600-normal.woff2',
        'fonts/atkinson-hyperlegible-next-latin-400-normal.woff2', 'fonts/atkinson-hyperlegible-next-latin-700-normal.woff2', 'fonts/unbounded-latin-700-normal.woff2'];
      for (const f of fichiers) {
        const buf = await (await fetch('/' + f)).arrayBuffer();
        await Neutralino.filesystem.writeBinaryFile(base + '/' + f, buf);
      }
      dossierObs = base;
    } catch (e) { toast('Sortie OBS indisponible : ' + (e.message || e)); }
    return dossierObs;
  }
  let obsMinuterie = null;
  function ecrireObs() {
    if (!natif || !dossierObs) return;
    clearTimeout(obsMinuterie);
    obsMinuterie = setTimeout(() => Neutralino.filesystem.writeFile(dossierObs + '/etat.json', JSON.stringify({ ...etat, t: Date.now() })).catch(() => {}), 30);
  }
  if (natif) preparerObs().then(ecrireObs);

  // menu sous le bouton « Ouvrir l'écran »
  async function menuEcrans() {
    const vieux = document.getElementById('menu-ecrans');
    if (vieux) { vieux.remove(); return; }
    const m = h('div', 'menu-ecrans');
    m.id = 'menu-ecrans';
    m.appendChild(h('div', 'menu-titre', 'Recherche des écrans…'));
    const r = $('#ouvrir').getBoundingClientRect();
    m.style.top = r.bottom + 6 + 'px';
    m.style.right = (window.innerWidth - r.right) + 'px';
    document.body.appendChild(m);
    const ecrans = await listerEcrans();
    m.textContent = '';
    m.appendChild(h('div', 'menu-titre', 'Projecteur plein écran'));
    ecrans.forEach((e) => {
      const b = h('button', '', `${e.nom} : ${e.w}×${e.h} @ ${e.x},${e.y}${e.p ? ' (principal)' : ''}`);
      b.onclick = () => { m.remove(); ouvrirSur({ ...e, mode: 'plein' }); };
      m.appendChild(b);
    });
    m.appendChild(h('div', 'menu-titre', 'Dans OBS (sans fenêtre)'));
    const o = h('button', '', 'Copier le chemin pour une source navigateur OBS');
    o.onclick = async () => {
      m.remove();
      const d = await preparerObs();
      if (!d) return;
      await fermerEcranNatif();
      ecrire('verselight-ecran-choisi', { mode: 'obs' });
      const chemin = d.replace(/\//g, '\\') + '\\ecran.html';
      try { await Neutralino.clipboard.writeText(chemin); } catch (e) {}
      ecrireObs();
      toast('Chemin copié. Dans OBS : Source navigateur › cocher « Fichier local » › coller : ' + chemin + ' (1920×1080).');
      envoyer();
    };
    m.appendChild(o);
    m.appendChild(h('div', 'menu-titre', 'Projecteur fenêtré'));
    const f = h('button', '', 'Fenêtre sur l’écran principal');
    const prim = ecrans.find((e) => e.p) || ecrans[0];
    f.onclick = () => { m.remove(); ouvrirSur({ ...prim, mode: 'fenetre' }); };
    m.appendChild(f);
    if (ecranPid) {
      const x = h('button', 'fermer', 'Fermer l’écran de projection');
      x.onclick = () => { m.remove(); fermerEcranNatif().then(envoyer); };
      m.appendChild(x);
    }
    setTimeout(() => document.addEventListener('click', function fermer(ev) { if (!m.contains(ev.target)) { m.remove(); document.removeEventListener('click', fermer); } }), 0);
  }
  async function ouvrirEcranNatif() {
    if (ecranPid) return;
    if ((lire('verselight-ecran-choisi', {}) || {}).mode === 'obs') return;
    // dernier écran choisi, s'il est encore branché ; sinon on demande
    const dernier = lire('verselight-ecran-choisi', null);
    if (dernier) {
      const ecrans = await listerEcrans();
      const ok = ecrans.find((e) => e.x === dernier.x && e.y === dernier.y && e.w === dernier.w && e.h === dernier.h);
      if (ok || dernier.mode === 'fenetre') { ouvrirSur(ok ? { ...ok, mode: dernier.mode } : dernier); return; }
    }
    menuEcrans();
  }
  const obsBranche = () => !!(window.VLObs && window.VLObs.actif() && window.VLObs.connecte());
  function ecranOuvert() { return natif ? ecranPid !== 0 || obsBranche() || (lire('verselight-ecran-choisi', {}) || {}).mode === 'obs' : ecran && !ecran.closed; }
  // l'écran de projection ne s'ouvre que si on le choisit (« Écran de projection ▾ ») : rien ne s'ouvre tout seul
  function assurerEcran() { if (!natif && !ecranOuvert()) ouvrirEcran(); }
  function envoyer() {
    if (!natif && ecranOuvert()) ecran.postMessage({ verselight: true, etat }, '*');
    ecrire('verselight-etat', { ...etat, t: Date.now() });
    if (typeof ecrireObs === 'function') ecrireObs();
    const a = $('#apercu');
    a.className = 'apercu theme-' + etat.theme + (etat.mode !== 'vide' ? ' vivant' : '') + (etat.mode === 'chant' ? ' chant' : '');
    a.querySelector('.t').textContent = etat.lignes.join('\n');
    a.querySelector('.r').textContent = etat.ref || '';
    a.querySelector('.t2').textContent = etat.mode === 'verset' && etat.second ? etat.second.texte : '';
    const p = $('#etat-ecran');
    p.textContent = ecranOuvert() ? (etat.mode === 'vide' ? 'Écran ouvert · vide' : 'À l’écran') : 'Écran fermé';
    p.classList.toggle('ouvert', ecranOuvert());
    marquerDirect();
    majSuivant();
    for (const f of ecouteurs) try { f(); } catch (e) {}
  }
  // la diapositive qui viendra avec « Suivant » (verset suivant, morceau suivant, couplet suivant)
  function suivante() {
    if (!cible) return null;
    if (cible.type === 'chant') {
      const c = trouver(cible.id), s = c && c.sections[cible.i + 1];
      return s ? { type: 'chant', id: cible.id, i: cible.i + 1, label: s.label || 'Strophe', texte: s.text } : null;
    }
    if (cible.type === 'verset') {
      if (cible.parts > 1 && cible.part + 1 < cible.parts) {
        return { type: 'verset', b: cible.b, c: cible.c, v: cible.v1, label: `${B.refLabel(cible.b, cible.c, cible.v1)} · morceau ${cible.part + 2}/${cible.parts}`, texte: morceaux(cible.b, cible.c, cible.v1)[cible.part + 1] };
      }
      let c = cible.c, v = cible.v2 + 1;
      if (v > B.verses(cible.b, c).length) { if (c < B.chapterCount(cible.b)) { c++; v = 1; } else return null; }
      const p = morceaux(cible.b, c, v);
      return { type: 'verset', b: cible.b, c, v, label: B.refLabel(cible.b, c, v) + (p ? ` · morceau 1/${p.length}` : ''), texte: p ? p[0] : B.verses(cible.b, c)[v - 1] };
    }
    return null;
  }
  function majSuivant() {
    const s = suivante();
    $('#suivant-lab').textContent = s ? s.label : '';
    $('#suivant-texte').textContent = s ? s.texte : '—';
    $('#decouper').disabled = !s;
  }
  const ecouteurs = [];
  function ouvrirEcran() {
    if (natif) { ouvrirEcranNatif(); return true; }
    if (ecranOuvert()) { ecran.focus(); return true; }
    // ouverture immédiate (le navigateur l'exige pendant le clic), puis déplacement sur le bon écran
    ecran = window.open('ecran.html', 'verselight-ecran', 'popup,width=1280,height=720');
    if (!ecran) { toast('La fenêtre a été bloquée : autorisez les fenêtres surgissantes pour VerseLight.'); return false; }
    setTimeout(envoyer, 400);
    let fini = false;
    const message = (n) => { if (fini) return; fini = true; toast(n > 1 ? 'Écran ouvert sur le 2e écran. Double-cliquez dedans pour le plein écran.' : 'Un seul écran détecté : l’écran est ouvert ici. Double-cliquez dedans pour le plein écran.'); };
    if (window.getScreenDetails) {
      window.getScreenDetails().then((sd) => {
        const cible = sd.screens.find((x) => x !== sd.currentScreen) || sd.currentScreen;
        try { ecran.moveTo(cible.availLeft, cible.availTop); ecran.resizeTo(cible.availWidth, cible.availHeight); } catch (e) {}
        message(sd.screens.length);
      }).catch(() => message(1));
      setTimeout(() => message(1), 4000);
    } else {
      try { ecran.moveTo(window.screen.availLeft || 0, 0); ecran.resizeTo(screen.availWidth, screen.availHeight); } catch (e) {}
      message(1);
    }
    return true;
  }
  $('#ouvrir').addEventListener('click', () => (natif ? menuEcrans() : ouvrirEcran()));
  window.addEventListener('message', (ev) => { if (ev.data && ev.data.verselightDemande) { ecran = ev.source; envoyer(); } });
  setInterval(() => { const p = $('#etat-ecran'); if (p.classList.contains('ouvert') !== !!ecranOuvert()) envoyer(); }, 1000);

  $('#theme').value = etat.theme;
  $('#theme').addEventListener('change', (e) => { etat.theme = e.target.value; ecrire('verselight-theme', etat.theme); envoyer(); });
  $('#taille').value = etat.taille;
  $('#taille').addEventListener('input', (e) => { etat.taille = +e.target.value; ecrire('verselight-taille', etat.taille); envoyer(); });

  function oeilObs(v) { if (window.VLObs) window.VLObs.oeil(v); }
  function effacer() {
    oeilObs(false); etat = { ...etat, mode: 'vide', lignes: [], ref: '' }; cible = null; envoyer(); }
  $('#effacer').addEventListener('click', effacer);

  // ------------------------------------------------------------ onglets
  let onglet = 'bible';
  function montrerOnglet(k) {
    onglet = k;
    document.querySelectorAll('.onglets button').forEach((b) => b.classList.toggle('on', b.dataset.onglet === k));
    $('#p-bible').classList.toggle('cache', k !== 'bible');
    $('#p-chants').classList.toggle('cache', k !== 'chants');
    // la grande recherche en haut cherche dans la Bible ou dans les chants, selon l'onglet
    document.querySelectorAll('[data-recherche]').forEach((l) => l.classList.toggle('cache', l.dataset.recherche !== k));
  }
  document.querySelectorAll('.onglets button').forEach((b) => b.addEventListener('click', () => montrerOnglet(b.dataset.onglet)));

  // ------------------------------------------------------------ Bible
  B.loadBible();
  let vue = null; // { b, c } chapitre affiché
  let sel = null; // { b, c, v1, v2 } versets choisis
  // livres : Ancien Testament et Nouveau Testament, noms courts (nom complet au survol)
  const COURTS = ['Genèse', 'Exode', 'Lévitique', 'Nombres', 'Deutér.', 'Josué', 'Juges', 'Ruth', '1 Samuel', '2 Samuel', '1 Rois', '2 Rois',
    '1 Chron.', '2 Chron.', 'Esdras', 'Néhémie', 'Esther', 'Job', 'Psaumes', 'Proverbes', 'Ecclés.', 'Cantique', 'Ésaïe', 'Jérémie',
    'Lament.', 'Ézéchiel', 'Daniel', 'Osée', 'Joël', 'Amos', 'Abdias', 'Jonas', 'Michée', 'Nahum', 'Habacuc', 'Sophonie', 'Aggée',
    'Zacharie', 'Malachie', 'Matthieu', 'Marc', 'Luc', 'Jean', 'Actes', 'Romains', '1 Cor.', '2 Cor.', 'Galates', 'Éphés.', 'Philipp.',
    'Coloss.', '1 Thess.', '2 Thess.', '1 Tim.', '2 Tim.', 'Tite', 'Philémon', 'Hébreux', 'Jacques', '1 Pierre', '2 Pierre', '1 Jean',
    '2 Jean', '3 Jean', 'Jude', 'Apoc.'];
  B.BOOKS.forEach((bk) => {
    const x = h('button', '', COURTS[bk.i] || bk.name);
    x.type = 'button';
    x.title = bk.name;
    x.addEventListener('click', () => ouvrirChapitre(bk.i, 1));
    ($(bk.i < 39 ? '#livres' : '#livres-nt') || $('#livres')).appendChild(x);
  });

  function ouvrirChapitre(b, c, v1 = 0, v2 = 0) {
    const n = B.chapterCount(b);
    c = Math.max(1, Math.min(n, c));
    vue = { b, c };
    sel = v1 ? { b, c, v1, v2: v2 || v1 } : null;
    document.querySelectorAll('.livres button').forEach((x, i) => x.classList.toggle('on', i === b));
    const t = $('#titre-chap');
    t.textContent = '';
    const pr = h('button', 'btn petit', '‹'); pr.title = 'Chapitre précédent'; pr.disabled = c <= 1; pr.onclick = () => ouvrirChapitre(b, c - 1);
    const sv = h('button', 'btn petit', '›'); sv.title = 'Chapitre suivant'; sv.disabled = c >= n; sv.onclick = () => ouvrirChapitre(b, c + 1);
    t.append(pr, h('span', '', `${B.bookName(b)} ${c}`), sv);
    const L = $('#versets');
    L.textContent = '';
    B.verses(b, c).forEach((txt, i) => {
      const v = i + 1;
      const row = h('div', 'verset');
      row.dataset.v = v;
      row.append(h('span', 'n', String(v)), h('span', '', txt));
      row.addEventListener('click', (e) => {
        if (e.shiftKey && sel && sel.b === b && sel.c === c) sel = { b, c, v1: Math.min(sel.v1, v), v2: Math.max(sel.v1, v) };
        else sel = { b, c, v1: v, v2: v };
        marquer();
      });
      row.addEventListener('dblclick', () => { sel = { b, c, v1: v, v2: v }; afficherVerset(sel); });
      L.appendChild(row);
    });
    marquer();
    const premier = sel && L.querySelector(`[data-v="${sel.v1}"]`);
    (premier || L.firstChild)?.scrollIntoView({ block: premier ? 'center' : 'start' });
  }
  function marquer() {
    document.querySelectorAll('#versets .verset').forEach((r) => {
      const v = +r.dataset.v;
      r.classList.toggle('choisi', !!sel && vue && sel.b === vue.b && sel.c === vue.c && v >= sel.v1 && v <= sel.v2);
    });
    marquerDirect();
  }
  function marquerDirect() {
    document.querySelectorAll('#versets .verset').forEach((r) => {
      const v = +r.dataset.v;
      r.classList.toggle('direct', !!cible && cible.type === 'verset' && vue && cible.b === vue.b && cible.c === vue.c && v >= cible.v1 && v <= cible.v2);
    });
    document.querySelectorAll('#sections .section').forEach((s) => s.classList.toggle('direct', !!cible && cible.type === 'chant' && cible.id === chantOuvert && cible.i === +s.dataset.i));
  }
  // versets coupés en plusieurs diapositives (✂ Découper) : clé « livre.chapitre.verset » -> morceaux
  let decoupes = lire('myverse-decoupes', {});
  const cleVerset = (b, c, v) => `${b}.${c}.${v}`;
  function morceaux(b, c, v) { const p = decoupes[cleVerset(b, c, v)]; return Array.isArray(p) && p.length > 1 ? p : null; }
  function afficherVerset(r) {
    const n = B.verses(r.b, r.c).length;
    if (!n) return;
    const v1 = Math.max(1, Math.min(n, r.v1)), v2 = Math.max(v1, Math.min(n, r.v2));
    const parts = v1 === v2 ? morceaux(r.b, r.c, v1) : null;
    const part = parts ? (r.part === -1 ? parts.length - 1 : Math.max(0, Math.min(parts.length - 1, r.part || 0))) : 0;
    cible = { type: 'verset', b: r.b, c: r.c, v1, v2, part, parts: parts ? parts.length : 0 };
    assurerEcran();
    oeilObs(true);
    const VB = window.VLBibles;
    const t2 = !parts && VB ? VB.texte2(r.b, r.c, v1, v2) : '';
    etat = { ...etat, mode: 'verset', lignes: [parts ? parts[part] : B.versesText(r.b, r.c, v1, v2)], ref: B.refLabel(r.b, r.c, v1, v2) + ` (${VB ? VB.etiquette() : 'LSG'})`, second: t2 ? { texte: t2 } : null };
    if (vue && (vue.b !== r.b || vue.c !== r.c) && onglet === 'bible') ouvrirChapitre(r.b, r.c, v1, v2);
    envoyer();
  }

  // recherche : référence (« jn 3 16 ») ou mots
  let minuterie = null;
  $('#q').addEventListener('input', () => { clearTimeout(minuterie); minuterie = setTimeout(chercher, 180); });
  $('#q').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); clearTimeout(minuterie); chercher(); if (sel) afficherVerset(sel); }
  });
  let recherches = 0;
  async function chercher() {
    const q = $('#q').value.trim();
    if (!q) return;
    const r = B.parseRef(q);
    if (r && /\d/.test(q)) { ouvrirChapitre(r.b, r.c, r.v1, r.v2); return; }
    if (r && q.length <= 4) { ouvrirChapitre(r.b, r.c); return; }
    if (q.length < 3) return;
    // mots (même mal écrits) cherchés dans toutes les Bibles installées : version principale, 2e version, Bibles importées
    const n = ++recherches;
    let res = [];
    try { res = window.VLRecherche ? await window.VLRecherche.chercher(q, { limite: 120 }) : B.search(q, 120); }
    catch (e) { if (window.VLJournal) window.VLJournal.erreur('Recherche « ' + q + ' » : ' + (e.message || e)); }
    if (n !== recherches || $('#q').value.trim() !== q) return; // une recherche plus récente est en cours
    vue = null; sel = null;
    $('#titre-chap').textContent = res.length ? `${res.length}${res.length >= 120 ? '+' : ''} résultat${res.length > 1 ? 's' : ''} pour « ${q} »` : '';
    const L = $('#versets');
    L.textContent = '';
    if (!res.length) { L.appendChild(h('div', 'vide', r ? '' : 'Aucun verset trouvé. Essayez une référence comme « jn 3 16 » ou d’autres mots.')); if (r) ouvrirChapitre(r.b, r.c); return; }
    const termes = res.termes;
    for (const x of res) {
      const row = h('div', 'verset');
      const t = h('span');
      const ref = h('span', 'ref', B.refLabel(x.b, x.c, x.v));
      if (x.principale === false) ref.appendChild(h('span', 'ver', x.version));
      t.appendChild(ref);
      if (window.VLRecherche && termes) for (const m of window.VLRecherche.morceaux(x.text, termes)) t.appendChild(m.mot ? h('mark', '', m.t) : document.createTextNode(m.t));
      else t.appendChild(document.createTextNode(x.text));
      row.append(h('span', 'n', String(x.v)), t);
      row.addEventListener('click', () => ouvrirChapitre(x.b, x.c, x.v, x.v));
      row.addEventListener('dblclick', () => afficherVerset({ b: x.b, c: x.c, v1: x.v, v2: x.v }));
      L.appendChild(row);
    }
  }
  // l'index de recherche se prépare en arrière-plan : la première recherche est instantanée
  setTimeout(() => { if (window.VLRecherche) window.VLRecherche.preparer(); }, 2500);

  // ------------------------------------------------------------ chants
  let chants = lire('verselight-chants', []);
  // recueils de l'église (VideoPsalm) ajoutés une fois au premier démarrage
  if (window.CHANTS_INCLUS && !lire('verselight-inclus-v1', false)) {
    for (const s of window.CHANTS_INCLUS) chants.push({ id: Math.random().toString(36).slice(2, 10), title: s.title, author: s.author, sections: s.sections });
    ecrire('verselight-chants', chants);
    ecrire('verselight-inclus-v1', true);
  }
  let chantOuvert = null;
  const sauver = () => ecrire('verselight-chants', chants);
  const trouver = (id) => chants.find((c) => c.id === id);

  function listeChants() {
    const q = B.norm($('#qc').value.trim());
    const L = $('#liste-chants');
    L.textContent = '';
    const rec = $('#recueil').value;
    const vus = chants.filter((c) => (!rec || (c.author || '') === rec) && (!q || B.norm(c.title + ' ' + (c.author || '') + ' ' + c.sections.map((s) => s.text).join(' ')).includes(q)))
      .sort((a, b) => (a.author || '').localeCompare(b.author || '', 'fr') || a.title.localeCompare(b.title, 'fr', { numeric: true }));
    const livres = [...new Set(chants.map((c) => c.author || '').filter(Boolean))].sort((a, b) => a.localeCompare(b, 'fr'));
    const sel = $('#recueil');
    if (sel.options.length !== livres.length + 1) {
      sel.length = 1;
      for (const l of livres) sel.add(new Option(`${l} (${chants.filter((c) => c.author === l).length})`, l));
      sel.value = livres.includes(rec) ? rec : '';
    }
    if (!chants.length) L.appendChild(h('div', 'vide', 'Aucun chant. Importez vos fichiers (VideoPsalm, OpenLyrics, ChordPro, texte…) ou créez un chant.'));
    for (const c of vus) {
      const e = h('div', 'chant' + (c.id === chantOuvert ? ' choisi' : ''), c.title);
      if (c.author) e.appendChild(h('small', '', c.author));
      e.addEventListener('click', () => ouvrirChant(c.id));
      L.appendChild(e);
    }
  }
  function ouvrirChant(id) {
    chantOuvert = id;
    const c = trouver(id);
    $('#chant-titre').textContent = c ? c.title : 'Aucun chant choisi';
    $('#modifier').disabled = $('#supprimer').disabled = !c;
    const box = $('#sections');
    box.textContent = '';
    (c ? c.sections : []).forEach((s, i) => {
      const e = h('div', 'section');
      e.dataset.i = i;
      const lab = h('div', 'lab', s.label || 'Strophe');
      if (i < 9) lab.appendChild(h('kbd', '', String(i + 1)));
      e.append(lab, document.createTextNode(s.text));
      e.addEventListener('click', () => afficherSection(id, i));
      e.addEventListener('dblclick', () => editer(c, i));
      e.title = 'Clic : afficher · double-clic : modifier ou couper (Entrée devant une ligne)';
      box.appendChild(e);
    });
    listeChants();
    marquerDirect();
  }
  function afficherSection(id, i) {
    const c = trouver(id);
    if (!c || !c.sections[i]) return;
    cible = { type: 'chant', id, i };
    assurerEcran();
    oeilObs(true);
    etat = { ...etat, mode: 'chant', lignes: c.sections[i].text.split('\n').filter((l) => l.trim()), ref: '', second: null };
    envoyer();
  }
  $('#qc').addEventListener('input', listeChants);
  $('#recueil').addEventListener('change', listeChants);

  $('#importer').addEventListener('click', () => { $('#imp-etat').textContent = ''; $('#import').showModal(); });
  $('#import [data-imp="fichiers"]').addEventListener('click', () => { $('#import').close(); $('#fichiers').click(); });
  // utilisé par import-recueils.js
  window.VL = {
    ajouterChants(liste) { const rec = recueilImport(); for (const s of liste) chants.push({ id: Math.random().toString(36).slice(2, 10), title: s.title, author: rec || s.author || '', sections: s.sections }); sauver(); listeChants(); },
    editerNouveau: () => { $('#import').close(); editer(null); setTimeout(() => $('#ed-texte').focus(), 50); },
    toast, natif: () => natif,
  };
  $('#fichiers').accept = S.ACCEPT;
  $('#fichiers').addEventListener('change', async (e) => {
    let n = 0; const err = [];
    for (const f of e.target.files) {
      try {
        for (const s of await S.readSongFile(f)) {
          chants.push({ id: Math.random().toString(36).slice(2, 10), title: s.title || f.name, author: recueilImport() || s.author || '', sections: s.sections });
          n++;
        }
      } catch (x) { err.push(f.name + ' : ' + x.message); }
    }
    e.target.value = '';
    sauver(); listeChants();
    toast((n ? `${n} chant${n > 1 ? 's' : ''} importé${n > 1 ? 's' : ''}.` : '') + (err.length ? ' Erreurs : ' + err.join(' · ') : ''));
  });

  const recueilImport = () => ($('#imp-recueil') ? $('#imp-recueil').value.trim() : '');
  let edition = null, edBrut = '', edRecueil = '';
  function editer(c, focus) {
    edition = c ? c.id : null;
    edBrut = '';
    edRecueil = c ? '' : recueilImport();
    $('#ed-brut').hidden = true;
    $('#ed-titre').value = c ? c.title : '';
    $('#ed-texte').value = c ? c.sections.map((s) => `[${s.label}]\n${s.text}`).join('\n\n') : '';
    apercuEditeur();
    $('#editeur').showModal();
    if (c && focus != null) {
      // curseur au début de la strophe double-cliquée
      const ta = $('#ed-texte');
      const pos = c.sections.slice(0, focus).reduce((n, s) => n + `[${s.label}]\n${s.text}`.length + 2, 0);
      setTimeout(() => { ta.focus(); ta.setSelectionRange(pos, pos); ta.scrollTop = Math.max(0, ta.value.slice(0, pos).split('\n').length - 3) * 21; }, 30);
    }
  }
  // ---------- rangement automatique et aperçu des diapositives
  const A = window.Arrange;
  function apercuEditeur() {
    const secs = S.splitSections($('#ed-texte').value);
    const box = $('#ed-apercu');
    box.textContent = '';
    $('#ed-compte').textContent = secs.length ? `(${secs.length})` : '';
    secs.forEach((s, i) => {
      const e = h('div', 'ed-diapo' + (s.text.split('\n').length > 6 ? ' long' : ''));
      e.title = s.text.split('\n').length > 6 ? 'Diapositive longue : appuyez sur Entrée devant une ligne pour la couper' : '';
      e.append(h('b', '', `${i + 1}. ${s.label || 'Strophe'}`), document.createTextNode(s.text));
      box.appendChild(e);
    });
  }
  const sansEntetes = (t) => t.split('\n').filter((l) => !/^\s*\[[^\]]*\]\s*$/.test(l)).join('\n');
  function rangerTexte(brut, silence) {
    const r = A.ranger(brut, { max: $('#ed-lignes').value, titre: $('#ed-titre').value });
    if (!r.sections.length) return;
    if (!$('#ed-titre').value.trim() && r.title) $('#ed-titre').value = r.title;
    $('#ed-texte').value = A.versTexte(r.sections);
    $('#ed-brut').hidden = !edBrut;
    apercuEditeur();
    if (!silence) toast(`Chant rangé : ${r.sections.length} diapositive${r.sections.length > 1 ? 's' : ''}.`);
  }
  $('#ed-texte').addEventListener('paste', (e) => {
    const t = (e.clipboardData || window.clipboardData).getData('text') || '';
    const ta = e.target;
    const tout = ta.selectionStart === 0 && ta.selectionEnd === ta.value.length;
    // un chant complet collé dans un éditeur vide (ou tout sélectionné) : rangé directement
    if (t.split('\n').filter((l) => l.trim()).length >= 3 && (!ta.value.trim() || tout)) {
      e.preventDefault();
      edBrut = t;
      rangerTexte(t);
    }
  });
  $('#ed-texte').addEventListener('input', apercuEditeur);
  $('#ed-ranger').addEventListener('click', () => { if (!edBrut) edBrut = sansEntetes($('#ed-texte').value); rangerTexte(edBrut); });
  $('#ed-lignes').addEventListener('change', () => { if (!edBrut) edBrut = sansEntetes($('#ed-texte').value); rangerTexte(edBrut); });
  $('#ed-brut').addEventListener('click', () => { $('#ed-texte').value = edBrut; apercuEditeur(); });
  $('#nouveau').addEventListener('click', () => editer(null));
  $('#modifier').addEventListener('click', () => editer(trouver(chantOuvert)));
  $('#editeur').addEventListener('close', () => {
    if ($('#editeur').returnValue !== 'ok') return;
    const titre = $('#ed-titre').value.trim() || 'Sans titre';
    const sections = S.splitSections($('#ed-texte').value);
    if (!sections.length) { toast('Le chant est vide.'); return; }
    if (edition) Object.assign(trouver(edition), { title: titre, sections });
    else { edition = Math.random().toString(36).slice(2, 10); chants.push({ id: edition, title: titre, author: edRecueil, sections }); }
    sauver(); ouvrirChant(edition);
  });
  // ---------- ✂ Découper : Entrée devant une ligne (ou dans un verset) = nouvelle diapositive
  let aDecouper = null;
  function morceauxTexte(t, cible_) {
    if (cible_.type === 'verset') return t.split('\n').map((l) => l.trim()).filter(Boolean); // une ligne = une diapositive
    return t.split(/\n\s*\n/).map((b) => b.split('\n').map((l) => l.trim()).filter(Boolean).join('\n')).filter(Boolean);
  }
  function apercuDecoupe() {
    const parts = morceauxTexte($('#decoupe-texte').value, aDecouper);
    const box = $('#decoupe-apercu');
    box.textContent = '';
    $('#decoupe-compte').textContent = `(${parts.length})`;
    parts.forEach((p, i) => { const e = h('div', 'ed-diapo'); e.append(h('b', '', String(i + 1)), document.createTextNode(p)); box.appendChild(e); });
  }
  function ouvrirDecoupe(t) {
    if (!t) return;
    aDecouper = t;
    if (t.type === 'verset') {
      const p = morceaux(t.b, t.c, t.v);
      $('#decoupe-aide').textContent = `${B.refLabel(t.b, t.c, t.v)} — placez le curseur dans le texte et appuyez sur Entrée : chaque ligne devient une diapositive.`;
      $('#decoupe-texte').value = p ? p.join('\n') : B.verses(t.b, t.c)[t.v - 1];
    } else {
      const c = trouver(t.id), sec = c && c.sections[t.i];
      if (!sec) return;
      $('#decoupe-aide').textContent = `${c.title} — ${sec.label || 'Strophe'} : appuyez sur Entrée devant une ligne pour commencer une nouvelle diapositive.`;
      $('#decoupe-texte').value = sec.text;
    }
    apercuDecoupe();
    $('#decoupe-dlg').showModal();
    $('#decoupe-texte').focus();
  }
  $('#decoupe-texte').addEventListener('input', apercuDecoupe);
  $('#decoupe-reset').addEventListener('click', () => {
    const t = $('#decoupe-texte').value;
    $('#decoupe-texte').value = aDecouper.type === 'verset' ? t.split('\n').map((l) => l.trim()).filter(Boolean).join(' ') : t.split('\n').filter((l) => l.trim()).join('\n');
    apercuDecoupe();
  });
  $('#decoupe-dlg').addEventListener('close', () => {
    if ($('#decoupe-dlg').returnValue !== 'ok' || !aDecouper) return;
    const t = aDecouper;
    const parts = morceauxTexte($('#decoupe-texte').value, t);
    if (!parts.length) return;
    if (t.type === 'verset') {
      if (parts.length > 1) decoupes[cleVerset(t.b, t.c, t.v)] = parts; else delete decoupes[cleVerset(t.b, t.c, t.v)];
      ecrire('myverse-decoupes', decoupes);
      // le verset à l'écran est mis à jour tout de suite
      if (cible && cible.type === 'verset' && cible.b === t.b && cible.c === t.c && cible.v1 === t.v && cible.v2 === t.v) afficherVerset({ b: t.b, c: t.c, v1: t.v, v2: t.v, part: 0 });
      else majSuivant();
      toast(parts.length > 1 ? `${B.refLabel(t.b, t.c, t.v)} : ${parts.length} diapositives.` : 'Verset remis en une seule diapositive.');
    } else {
      const c = trouver(t.id);
      if (!c || !c.sections[t.i]) return;
      const label = c.sections[t.i].label || 'Strophe';
      const base = label.replace(/\s*\(\d+\/\d+\)$/, '');
      const nouvelles = parts.map((p, j) => ({ label: parts.length > 1 ? `${base} (${j + 1}/${parts.length})` : base, text: p }));
      c.sections.splice(t.i, 1, ...nouvelles);
      sauver();
      if (chantOuvert === t.id) ouvrirChant(t.id);
      if (cible && cible.type === 'chant' && cible.id === t.id && cible.i === t.i) afficherSection(t.id, t.i); else majSuivant();
      toast(`${label} : ${parts.length} diapositive${parts.length > 1 ? 's' : ''}.`);
    }
  });
  $('#decouper').addEventListener('click', () => ouvrirDecoupe(suivante()));
  $('#apercu').addEventListener('dblclick', () => {
    if (!cible) return;
    if (cible.type === 'chant') ouvrirDecoupe({ type: 'chant', id: cible.id, i: cible.i });
    else if (cible.v1 === cible.v2) ouvrirDecoupe({ type: 'verset', b: cible.b, c: cible.c, v: cible.v1 });
    else toast('Découper : affichez un seul verset à la fois.');
  });

  $('#supprimer').addEventListener('click', () => {
    const c = trouver(chantOuvert);
    if (!c || !confirm(`Supprimer « ${c.title} » ?`)) return;
    chants = chants.filter((x) => x.id !== c.id);
    sauver(); ouvrirChant(null);
  });

  // ------------------------------------------------------------ suivant / précédent
  function bouger(d) {
    if (cible && cible.type === 'chant') {
      const c = trouver(cible.id);
      if (c && c.sections[cible.i + d]) afficherSection(cible.id, cible.i + d);
      return;
    }
    if (cible && cible.type === 'verset') {
      // d'abord les morceaux d'un verset découpé
      if (cible.parts > 1 && cible.part + d >= 0 && cible.part + d < cible.parts) {
        afficherVerset({ b: cible.b, c: cible.c, v1: cible.v1, v2: cible.v2, part: cible.part + d });
        return;
      }
      const n = B.verses(cible.b, cible.c).length;
      let v = d > 0 ? cible.v2 + 1 : cible.v1 - 1;
      let c = cible.c;
      if (v > n && c < B.chapterCount(cible.b)) { c++; v = 1; }
      if (v < 1 && c > 1) { c--; v = B.verses(cible.b, c).length; }
      if (v >= 1 && v <= B.verses(cible.b, c).length) { sel = { b: cible.b, c, v1: v, v2: v }; if (!vue || vue.c !== c) ouvrirChapitre(cible.b, c, v, v); afficherVerset({ ...sel, part: d < 0 ? -1 : 0 }); marquer(); }
      return;
    }
    if (onglet === 'bible' && sel) afficherVerset(sel);
  }
  $('#suiv').addEventListener('click', () => bouger(1));
  $('#prec').addEventListener('click', () => bouger(-1));

  document.addEventListener('keydown', (e) => {
    if ($('#editeur').open) return;
    const champ = /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName);
    if (e.key === 'Escape') { effacer(); document.activeElement.blur(); return; }
    if (champ) return;
    if (e.key === '/') { e.preventDefault(); (onglet === 'bible' ? $('#q') : $('#qc')).focus(); }
    else if (e.key === 'ArrowRight' || e.key === ' ' || e.key === 'PageDown') { e.preventDefault(); bouger(1); }
    else if (e.key === 'ArrowLeft' || e.key === 'PageUp') { e.preventDefault(); bouger(-1); }
    else if (e.key === 'Enter') afficherCourant();
    else if (onglet === 'chants' && chantOuvert && /^[1-9]$/.test(e.key)) afficherSection(chantOuvert, +e.key - 1);
  });

  // ------------------------------------------------------------ barres d'envoi (Bible et chants)
  function afficherCourant() {
    if (onglet === 'bible') { if (sel) afficherVerset(sel); else toast('Choisissez d’abord un verset.'); return; }
    if (!chantOuvert) { toast('Choisissez d’abord un chant.'); return; }
    const i = cible && cible.type === 'chant' && cible.id === chantOuvert ? cible.i : 0;
    afficherSection(chantOuvert, i);
  }
  document.querySelectorAll('.envoi-afficher').forEach((b) => b.addEventListener('click', afficherCourant));
  document.querySelectorAll('.envoi-prec').forEach((b) => b.addEventListener('click', () => bouger(-1)));
  document.querySelectorAll('.envoi-suiv').forEach((b) => b.addEventListener('click', () => bouger(1)));
  document.querySelectorAll('.envoi-retirer').forEach((b) => b.addEventListener('click', effacer));
  document.querySelectorAll('.envoi-mode').forEach((s) => {
    s.value = etat.presentation;
    s.addEventListener('change', () => {
      etat.presentation = s.value; ecrire('verselight-presentation', s.value);
      document.querySelectorAll('.envoi-mode').forEach((o) => (o.value = s.value));
      envoyer();
    });
  });

  // commandes envoyées depuis la fenêtre de projection
  function commande(k) { if (k === 'suiv') bouger(1); else if (k === 'prec') bouger(-1); else if (k === 'retirer') effacer(); }
  window.addEventListener('storage', (ev) => { if (ev.key === 'verselight-cmd' && ev.newValue) try { commande(JSON.parse(ev.newValue).k); } catch (x) {} });
  window.addEventListener('message', (ev) => { if (ev.data && ev.data.verselightCmd) commande(ev.data.verselightCmd); });


  // ------------------------------------------------------------ API pour la télécommande (remote.js)
  function lireRef(txt) {
    const r = B.parseRef(txt || '');
    if (!r || !B.verses(r.b, r.c).length) return null;
    const n = B.verses(r.b, r.c).length;
    const v1 = r.v1 ? Math.min(r.v1, n) : 1, v2 = r.v1 ? Math.min(Math.max(r.v2, v1), n) : n;
    return { b: r.b, c: r.c, v1, v2, entier: !r.v1 };
  }
  window.VLRegie = {
    etat: () => etat, cible: () => cible, suivante: () => suivante(), ecranOuvert: () => !!ecranOuvert(), trouver, chants: () => chants, ecranPid: () => ecranPid, dossierObs: () => preparerObs(),
    surChangement: (f) => ecouteurs.push(f),
    apercu(txt) {
      const r = lireRef(txt);
      if (!r) return null;
      const versets = [];
      for (let v = r.v1; v <= r.v2; v++) versets.push({ v, t: B.verses(r.b, r.c)[v - 1] });
      return { ref: B.refLabel(r.b, r.c, r.entier ? 0 : r.v1, r.v2) + ` (${window.VLBibles ? window.VLBibles.etiquette() : 'LSG'})`, lignes: [B.versesText(r.b, r.c, r.v1, r.v2)], versets, livre: B.bookName(r.b), c: r.c };
    },
    afficherRef(txt) {
      const r = lireRef(txt);
      if (!r) return false;
      const v2 = r.entier ? r.v1 : r.v2;
      sel = { b: r.b, c: r.c, v1: r.v1, v2 };
      if (onglet === 'bible') ouvrirChapitre(r.b, r.c, r.v1, v2);
      afficherVerset(sel);
      return true;
    },
    afficherSection, bouger, effacer,
    // nouvelle version de la Bible choisie : on redessine le chapitre et le verset à l'écran
    rafraichir() {
      if (vue) ouvrirChapitre(vue.b, vue.c, sel ? sel.v1 : 0, sel ? sel.v2 : 0);
      if (cible && cible.type === 'verset') afficherVerset({ b: cible.b, c: cible.c, v1: cible.v1, v2: cible.v2, part: cible.part });
    },
  };

  // ------------------------------------------------------------ départ
  ouvrirChapitre(42, 3, 16, 16);
  listeChants();
  envoyer();
  window.addEventListener('beforeunload', () => { if (!natif && ecranOuvert()) ecran.close(); });
})();
