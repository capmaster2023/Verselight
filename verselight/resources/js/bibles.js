// MyVerse — versions de la Bible : Louis Segond 1910 et KJV intégrées, Bibles importées par l'utilisateur
// (Zefania XML, OSIS XML, USFX, JSON, texte). Version principale + 2e version affichée sous le verset (bilingue).
(function () {
  const B = window.Bible;
  const natif = typeof window.NL_PORT !== 'undefined' && !!window.Neutralino;
  const lire = (k, d) => { try { const v = JSON.parse(localStorage.getItem(k)); return v ?? d; } catch (e) { return d; } };
  const ecrire = (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); return true; } catch (e) { return false; } };

  const OSIS = ['Gen', 'Exod', 'Lev', 'Num', 'Deut', 'Josh', 'Judg', 'Ruth', '1Sam', '2Sam', '1Kgs', '2Kgs', '1Chr', '2Chr', 'Ezra', 'Neh', 'Esth', 'Job', 'Ps', 'Prov', 'Eccl', 'Song', 'Isa', 'Jer', 'Lam', 'Ezek', 'Dan', 'Hos', 'Joel', 'Amos', 'Obad', 'Jonah', 'Mic', 'Nah', 'Hab', 'Zeph', 'Hag', 'Zech', 'Mal', 'Matt', 'Mark', 'Luke', 'John', 'Acts', 'Rom', '1Cor', '2Cor', 'Gal', 'Eph', 'Phil', 'Col', '1Thess', '2Thess', '1Tim', '2Tim', 'Titus', 'Phlm', 'Heb', 'Jas', '1Pet', '2Pet', '1John', '2John', '3John', 'Jude', 'Rev'];
  const USFM = ['GEN', 'EXO', 'LEV', 'NUM', 'DEU', 'JOS', 'JDG', 'RUT', '1SA', '2SA', '1KI', '2KI', '1CH', '2CH', 'EZR', 'NEH', 'EST', 'JOB', 'PSA', 'PRO', 'ECC', 'SNG', 'ISA', 'JER', 'LAM', 'EZK', 'DAN', 'HOS', 'JOL', 'AMO', 'OBA', 'JON', 'MIC', 'NAM', 'HAB', 'ZEP', 'HAG', 'ZEC', 'MAL', 'MAT', 'MRK', 'LUK', 'JHN', 'ACT', 'ROM', '1CO', '2CO', 'GAL', 'EPH', 'PHP', 'COL', '1TH', '2TH', '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JN', '2JN', '3JN', 'JUD', 'REV'];
  const ANGLAIS = ['genesis', 'exodus', 'leviticus', 'numbers', 'deuteronomy', 'joshua', 'judges', 'ruth', '1samuel', '2samuel', '1kings', '2kings', '1chronicles', '2chronicles', 'ezra', 'nehemiah', 'esther', 'job', 'psalms', 'proverbs', 'ecclesiastes', 'songofsolomon', 'isaiah', 'jeremiah', 'lamentations', 'ezekiel', 'daniel', 'hosea', 'joel', 'amos', 'obadiah', 'jonah', 'micah', 'nahum', 'habakkuk', 'zephaniah', 'haggai', 'zechariah', 'malachi', 'matthew', 'mark', 'luke', 'john', 'acts', 'romans', '1corinthians', '2corinthians', 'galatians', 'ephesians', 'philippians', 'colossians', '1thessalonians', '2thessalonians', '1timothy', '2timothy', 'titus', 'philemon', 'hebrews', 'james', '1peter', '2peter', '1john', '2john', '3john', 'jude', 'revelation'];
  const compact = (s) => B.norm(String(s || '')).replace(/[^a-z0-9]/g, '');
  const idxOsis = new Map(OSIS.map((o, i) => [o.toLowerCase(), i]));
  const idxUsfm = new Map(USFM.map((o, i) => [o.toLowerCase(), i]));

  // numéro de livre (0-65) d'après un nom ou un code connu, sinon -1
  function livreConnu(nom) {
    const c = compact(nom);
    if (!c) return -1;
    if (idxOsis.has(String(nom).toLowerCase())) return idxOsis.get(String(nom).toLowerCase());
    if (idxUsfm.has(String(nom).toLowerCase())) return idxUsfm.get(String(nom).toLowerCase());
    let i = ANGLAIS.indexOf(c.replace(/^(i{1,3})(?=[a-z])/, (m) => String(m.length)).replace(/^psalm$/, 'psalms').replace(/^songofsongs$/, 'songofsolomon').replace(/^revelations$/, 'revelation'));
    if (i >= 0) return i;
    const r = B.parseRef(String(nom) + ' 1');
    if (r && compact(B.bookName(r.b)).slice(0, 4) === c.slice(0, 4)) return r.b;
    return -1;
  }

  const vide = () => Array.from({ length: 66 }, () => ({ name: '', ch: [] }));
  function poser(books, b, c, v, t) {
    if (b < 0 || b > 65 || c < 1 || v < 1 || c > 200 || v > 250) return;
    const bk = books[b];
    while (bk.ch.length < c) bk.ch.push([]);
    const ch = bk.ch[c - 1];
    while (ch.length < v) ch.push('');
    ch[v - 1] = String(t).replace(/\s+/g, ' ').trim();
  }
  const sansBalises = (s) => String(s)
    .replace(/<(note|NOTE|rf|f|x|DIV|div)\b[^>]*>[\s\S]*?<\/\1>/g, '')
    .replace(/<[^>]+>/g, '')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, '’').replace(/&#(\d+);/g, (_, n) => String.fromCharCode(+n)).replace(/&amp;/g, '&');
  const attr = (tag, a) => { const m = tag.match(new RegExp('\\b' + a + '\\s*=\\s*"([^"]*)"', 'i')) || tag.match(new RegExp('\\b' + a + "\\s*=\\s*'([^']*)'", 'i')); return m ? m[1] : ''; };

  // ---------------------------------------------------------------- formats
  function zefania(x) {
    const books = vide();
    const titre = (x.match(/<title>([\s\S]*?)<\/title>/i) || [])[1];
    const reB = /<BIBLEBOOK\b([^>]*)>([\s\S]*?)<\/BIBLEBOOK>/gi;
    let mb;
    while ((mb = reB.exec(x))) {
      const b = (+attr(mb[1], 'bnumber') || 0) - 1;
      if (b < 0 || b > 65) continue;
      books[b].name = attr(mb[1], 'bname') || '';
      const reC = /<CHAPTER\b([^>]*)>([\s\S]*?)<\/CHAPTER>/gi;
      let mc;
      while ((mc = reC.exec(mb[2]))) {
        const c = +attr(mc[1], 'cnumber');
        const reV = /<VERS\b([^>]*)>([\s\S]*?)<\/VERS>/gi;
        let mv;
        while ((mv = reV.exec(mc[2]))) poser(books, b, c, +attr(mv[1], 'vnumber'), sansBalises(mv[2]));
      }
    }
    return { books, titre: titre ? sansBalises(titre).trim() : '' };
  }
  function osis(x) {
    const books = vide();
    const titre = (x.match(/<title[^>]*>([\s\S]*?)<\/title>/i) || [])[1];
    const parId = (id, t) => { const m = String(id).split(/\s+/)[0].match(/^([^.]+)\.(\d+)\.(\d+)/); if (m) poser(books, idxOsis.has(m[1].toLowerCase()) ? idxOsis.get(m[1].toLowerCase()) : -1, +m[2], +m[3], sansBalises(t)); };
    let m;
    const conteneur = /<verse\b([^>]*\bosisID\s*=\s*"[^"]+"[^>]*)>([\s\S]*?)<\/verse>/gi;
    let n = 0;
    while ((m = conteneur.exec(x))) { parId(attr(m[1], 'osisID'), m[2]); n++; }
    if (!n) { // jalons <verse sID="Gen.1.1"/> … <verse eID="Gen.1.1"/>
      const jalon = /<verse\b[^>]*\bsID\s*=\s*"([^"]+)"[^>]*\/>([\s\S]*?)<verse\b[^>]*\beID\s*=\s*"\1"[^>]*\/>/gi;
      while ((m = jalon.exec(x))) parId(m[1], m[2]);
    }
    return { books, titre: titre ? sansBalises(titre).trim() : '' };
  }
  function usfx(x) {
    const books = vide();
    const reB = /<book\b[^>]*\bid\s*=\s*"([^"]+)"[^>]*>([\s\S]*?)<\/book>/gi;
    let mb;
    while ((mb = reB.exec(x))) {
      const b = idxUsfm.has(mb[1].toLowerCase()) ? idxUsfm.get(mb[1].toLowerCase()) : -1;
      if (b < 0) continue;
      let c = 0;
      const parts = mb[2].split(/(<c\b[^>]*\/>|<v\b[^>]*\/>|<ve\s*\/>)/i);
      let v = 0, buf = '';
      const fin = () => { if (c && v && buf.trim()) poser(books, b, c, v, sansBalises(buf)); buf = ''; };
      for (const p of parts) {
        if (/^<c\b/i.test(p)) { fin(); c = +attr(p, 'id'); v = 0; }
        else if (/^<v\b/i.test(p)) { fin(); v = +attr(p, 'id'); }
        else if (/^<ve/i.test(p)) { fin(); v = 0; }
        else if (v) buf += p;
      }
      fin();
    }
    return { books, titre: '' };
  }
  function json(o) {
    const books = vide();
    let titre = o && !Array.isArray(o) ? (o.name || o.translation || o.title || '') : '';
    const liste = Array.isArray(o) ? o : (o.books || o.Books || null);
    if (Array.isArray(liste) && liste.length) {
      liste.forEach((bk, i) => {
        let b = typeof bk.book_number === 'number' ? bk.book_number - 1 : (bk.osis ? livreConnu(bk.osis) : livreConnu(bk.name || bk.abbrev || ''));
        if (b < 0) b = liste.length === 27 ? 39 + i : i;
        books[b].name = bk.name || '';
        const chs = bk.ch || bk.chapters || [];
        chs.forEach((ch, ci) => {
          if (Array.isArray(ch)) ch.forEach((t, vi) => poser(books, b, ci + 1, vi + 1, typeof t === 'string' ? t : (t.text || '')));
          else if (ch && Array.isArray(ch.verses)) ch.verses.forEach((v, vi) => poser(books, b, +(ch.chapter || ci + 1), +(v.verse || vi + 1), v.text || ''));
        });
      });
      return { books, titre };
    }
    const versets = o.verses || o.Verses;
    if (Array.isArray(versets)) {
      const ordre = [];
      for (const v of versets) {
        const nom = v.book_name || v.book || v.bookname || '';
        let b = typeof v.book === 'number' ? v.book - 1 : livreConnu(nom);
        if (b < 0) { if (!ordre.includes(nom)) ordre.push(nom); b = ordre.indexOf(nom); }
        poser(books, b, +v.chapter, +v.verse, v.text || '');
      }
      return { books, titre };
    }
    return { books, titre };
  }
  // texte : « Genèse 1:1 Au commencement… », « Itanguriro 1 1 … », ou « livre<TAB>chapitre<TAB>verset<TAB>texte »
  function texte(t) {
    const books = vide();
    const ordre = [];
    let livreCourant = '';
    const numero = (nom) => {
      let b = livreConnu(nom);
      if (b >= 0) return b;
      if (!ordre.includes(nom)) ordre.push(nom);
      return ordre.indexOf(nom);
    };
    const lignes = String(t).replace(/\r\n?/g, '\n').split('\n');
    const vus = new Set();
    for (const l of lignes) { const m = l.match(/^\s*(.+?)\s+\d+\s*[:.,\s]\s*\d+\s+\S/); if (m) vus.add(m[1].trim()); }
    const nt = vus.size === 27;
    for (const brut of lignes) {
      const l = brut.trim();
      if (!l) continue;
      let m = l.split('\t');
      if (m.length >= 4 && /^\d+$/.test(m[1]) && /^\d+$/.test(m[2])) { poser(books, numero(m[0]) + (nt && livreConnu(m[0]) < 0 ? 39 : 0), +m[1], +m[2], m.slice(3).join(' ')); continue; }
      m = l.match(/^(.+?)\s+(\d+)\s*[:.,]\s*(\d+)\s+(.+)$/) || l.match(/^(\D.*?)\s+(\d+)\s+(\d+)\s+(.+)$/);
      if (m) { livreCourant = m[1].trim(); const b = numero(livreCourant); poser(books, livreConnu(livreCourant) < 0 && nt ? b + 39 : b, +m[2], +m[3], m[4]); continue; }
      m = l.match(/^(\d+)\s*[:.]\s*(\d+)\s+(.+)$/);
      if (m && livreCourant) { const b = numero(livreCourant); poser(books, livreConnu(livreCourant) < 0 && nt ? b + 39 : b, +m[1], +m[2], m[3]); continue; }
      if (!/\d/.test(l) && l.length < 40) livreCourant = l; // titre de livre seul sur sa ligne
    }
    return { books, titre: '' };
  }

  /** Lit un fichier de Bible et le met au format de MyVerse. */
  function analyser(nomFichier, contenu) {
    const s = String(contenu).replace(/^﻿/, '');
    let r;
    if (/<XMLBIBLE|<BIBLEBOOK/i.test(s)) r = zefania(s);
    else if (/<osis/i.test(s)) r = osis(s);
    else if (/<usfx/i.test(s) || /<book\b[^>]*id="GEN"/i.test(s)) r = usfx(s);
    else if (/^\s*[[{]/.test(s)) r = json(JSON.parse(s));
    else r = texte(s);
    const n = r.books.reduce((t, b) => t + b.ch.reduce((u, c) => u + c.filter(Boolean).length, 0), 0);
    if (n < 50) throw new Error('aucun verset reconnu dans ce fichier (formats : Zefania XML, OSIS, USFX, JSON, texte « Livre 1:1 … »)');
    // livres sans nom : celui de la Louis Segond
    r.books.forEach((b, i) => { if (!b.name) b.name = B.BOOKS[i] ? B.BOOKS[i].name : ''; });
    const base = (r.titre || nomFichier.replace(/\.[^.]+$/, '')).trim().slice(0, 60) || 'Bible';
    return { name: base, short: base.replace(/[^A-Za-zÀ-ÿ0-9 ]/g, '').split(/\s+/).filter(Boolean).map((w) => w[0]).join('').toUpperCase().slice(0, 5) || 'BIB', books: r.books, versets: n };
  }

  // ---------------------------------------------------------------- stockage
  let reglages = lire('myverse-bibles', { principale: 'lsg', seconde: '', importees: [] });
  if (!Array.isArray(reglages.importees)) reglages.importees = [];
  const cache = new Map([['lsg', window.LSG]]);
  const INTEGREES = [
    { id: 'lsg', name: 'Louis Segond 1910', short: 'LSG' },
    { id: 'kjv', name: 'King James (anglais)', short: 'KJV' },
  ];
  const toutes = () => INTEGREES.concat(reglages.importees);
  const info = (id) => toutes().find((v) => v.id === id) || null;
  async function dossier() {
    const a = await Neutralino.os.getEnv('APPDATA');
    const d = a + '\\VerseLight\\bibles';
    try { await Neutralino.filesystem.createDirectory(d); } catch (e) {}
    return d;
  }
  async function charger(id) {
    if (!id) return null;
    if (cache.has(id)) return cache.get(id);
    let d = null;
    try {
      if (id === 'kjv') d = await (await fetch('bibles/kjv.json')).json();
      else if (natif) d = JSON.parse(await Neutralino.filesystem.readFile((await dossier()) + '\\' + id + '.json'));
      else d = lire('myverse-bible-' + id, null);
    } catch (e) { if (window.VLJournal) window.VLJournal.erreur('Bible « ' + id + ' » illisible : ' + (e.message || e)); }
    if (d) cache.set(id, d);
    return d;
  }
  async function stocker(id, d) {
    cache.set(id, d);
    if (natif) await Neutralino.filesystem.writeFile((await dossier()) + '\\' + id + '.json', JSON.stringify({ name: d.name, short: d.short, books: d.books }));
    else if (!ecrire('myverse-bible-' + id, d)) throw new Error('mémoire du navigateur pleine');
  }

  // ---------------------------------------------------------------- utilisation
  let seconde = null; // données de la 2e version (chargées)
  const etiquette = () => {
    const p = info(reglages.principale) || INTEGREES[0];
    const s = reglages.seconde && seconde ? info(reglages.seconde) : null;
    return p.short + (s ? ' · ' + s.short : '');
  };
  function texte2(b, c, v1, v2) {
    if (!seconde || !seconde.books || !seconde.books[b]) return '';
    const ch = seconde.books[b].ch[c - 1] || [];
    const out = [];
    for (let v = v1; v <= v2; v++) if (ch[v - 1]) out.push(ch[v - 1]);
    return out.join(' ');
  }
  async function appliquer() {
    const p = await charger(reglages.principale) || window.LSG;
    if (!p || !p.books) { reglages.principale = 'lsg'; B._setData(window.LSG); } else B._setData(p);
    seconde = reglages.seconde ? await charger(reglages.seconde) : null;
    if (window.VLRegie && window.VLRegie.rafraichir) window.VLRegie.rafraichir();
    majBouton();
  }
  function majBouton() {
    const l = document.querySelector('#versions-lab');
    if (l) l.textContent = 'Version : ' + etiquette() + (reglages.seconde ? ' (bilingue)' : '');
    remplirChoix();
  }

  // ---------------------------------------------------------------- liste des Bibles (colonne de gauche)
  // clic sur une Bible = elle s'affiche ; « 2e » = elle s'affiche aussi sous le verset (versets bilingues)
  function etatCarte(t) { const e = document.querySelector('#bibles-carte-etat'); if (e) e.textContent = t; }
  function remplirChoix() {
    const L = document.querySelector('#bibles-choix');
    if (!L) return;
    L.textContent = '';
    for (const v of toutes()) {
      const principale = v.id === reglages.principale, deuxieme = v.id === reglages.seconde;
      const ligne = document.createElement('div');
      ligne.className = 'bible-ligne' + (principale ? ' principale' : '') + (deuxieme ? ' seconde' : '');
      const nom = document.createElement('button');
      nom.type = 'button';
      nom.className = 'bible-nom';
      nom.setAttribute('role', 'radio');
      nom.setAttribute('aria-checked', principale ? 'true' : 'false');
      nom.title = principale ? 'Bible affichée à l’écran' : 'Afficher cette Bible';
      const rond = document.createElement('span'); rond.className = 'rond';
      const t = document.createElement('span'); t.className = 'nom'; t.textContent = v.name;
      const c = document.createElement('small'); c.textContent = v.short;
      nom.append(rond, t, c);
      nom.addEventListener('click', () => choisir(v.id));
      const deux = document.createElement('button');
      deux.type = 'button';
      deux.className = 'bible-2e' + (deuxieme ? ' on' : '');
      deux.textContent = '2e';
      deux.title = deuxieme ? 'Ne plus l’afficher sous le verset' : 'L’afficher aussi sous le verset (versets bilingues)';
      deux.setAttribute('aria-pressed', deuxieme ? 'true' : 'false');
      deux.disabled = principale;
      deux.addEventListener('click', () => basculerSeconde(v.id));
      ligne.append(nom, deux);
      L.appendChild(ligne);
    }
  }
  async function choisir(id) {
    if (reglages.principale === id) return;
    reglages.principale = id;
    if (reglages.seconde === id) reglages.seconde = '';
    ecrire('myverse-bibles', reglages);
    etatCarte('Chargement de la Bible…');
    await appliquer();
    etatCarte(`« ${(info(reglages.principale) || {}).name || reglages.principale} » est affichée.`);
  }
  async function basculerSeconde(id) {
    if (id === reglages.principale) return;
    reglages.seconde = reglages.seconde === id ? '' : id;
    ecrire('myverse-bibles', reglages);
    etatCarte(reglages.seconde ? 'Chargement de la 2e version…' : '');
    await appliquer();
    etatCarte(reglages.seconde ? (seconde ? 'Versets bilingues : ' + etiquette() : 'Cette version n’a pas pu être chargée.') : 'Une seule version à l’écran.');
  }

  // ---------------------------------------------------------------- fenêtre « Bibles »
  const $ = (s) => document.querySelector(s);
  const dlg = $('#bibles-dlg');
  function remplir() {
    const sp = $('#bible-principale'), ss = $('#bible-seconde');
    sp.length = 0; ss.length = 0;
    ss.add(new Option('Aucune', ''));
    for (const v of toutes()) {
      sp.add(new Option(`${v.name} (${v.short})`, v.id));
      ss.add(new Option(`${v.name} (${v.short})`, v.id));
    }
    sp.value = reglages.principale; ss.value = reglages.seconde || '';
    const L = $('#bibles-liste');
    L.textContent = '';
    if (!reglages.importees.length) { const li = document.createElement('li'); li.className = 'muet'; li.textContent = 'Aucune Bible importée.'; L.appendChild(li); }
    for (const v of reglages.importees) {
      const li = document.createElement('li');
      const b = document.createElement('b'); b.textContent = `${v.name} (${v.short})`;
      const sm = document.createElement('small'); sm.textContent = ` ${v.versets || ''} versets`;
      const x = document.createElement('button'); x.type = 'button'; x.className = 'btn petit danger'; x.textContent = 'Supprimer';
      x.onclick = async () => {
        if (!confirm(`Supprimer « ${v.name} » ?`)) return;
        reglages.importees = reglages.importees.filter((y) => y.id !== v.id);
        if (reglages.principale === v.id) reglages.principale = 'lsg';
        if (reglages.seconde === v.id) reglages.seconde = '';
        cache.delete(v.id);
        try { if (natif) await Neutralino.filesystem.remove((await dossier()) + '\\' + v.id + '.json'); else localStorage.removeItem('myverse-bible-' + v.id); } catch (e) {}
        ecrire('myverse-bibles', reglages); remplir(); appliquer();
      };
      li.append(b, sm, x);
      L.appendChild(li);
    }
  }
  if (dlg) {
    $('#bibles-btn').addEventListener('click', () => { $('#bible-etat').textContent = ''; remplir(); dlg.showModal(); });
    $('#bible-principale').addEventListener('change', (e) => { reglages.principale = e.target.value; ecrire('myverse-bibles', reglages); appliquer(); });
    $('#bible-seconde').addEventListener('change', async (e) => {
      reglages.seconde = e.target.value; ecrire('myverse-bibles', reglages);
      $('#bible-etat').textContent = e.target.value ? 'Chargement de la 2e version…' : '';
      await appliquer();
      $('#bible-etat').textContent = reglages.seconde ? (seconde ? 'Versets bilingues activés.' : 'Cette version n’a pas pu être chargée.') : '';
    });
    $('#bible-importer').addEventListener('click', () => $('#bible-fichier').click());
    const imp2 = $('#bibles-importer');
    if (imp2) imp2.addEventListener('click', () => $('#bible-fichier').click());
    $('#bible-fichier').addEventListener('change', async (e) => {
      const f = e.target.files[0];
      e.target.value = '';
      if (!f) return;
      $('#bible-etat').textContent = 'Lecture de « ' + f.name + ' »…';
      etatCarte('Lecture de « ' + f.name + ' »…');
      try {
        const brut = window.Songs && window.Songs.decodeBytes ? window.Songs.decodeBytes(new Uint8Array(await f.arrayBuffer())) : await f.text();
        const d = analyser(f.name, brut);
        const court = (window.prompt('Nom court affiché après la référence (ex. KIR, SW, DARBY) :', d.short) || d.short).trim().slice(0, 8) || d.short;
        d.short = court;
        const id = 'b' + Date.now().toString(36);
        await stocker(id, d);
        reglages.importees.push({ id, name: d.name, short: d.short, versets: d.versets });
        ecrire('myverse-bibles', reglages);
        remplir();
        remplirChoix();
        $('#bible-etat').textContent = `« ${d.name} » importée : ${d.versets} versets. Choisissez-la comme version principale ou 2e version.`;
        etatCarte(`« ${d.name} » importée (${d.versets} versets).`);
        if (window.VLJournal) window.VLJournal.info(`Bible importée : ${d.name} (${d.versets} versets)`);
        // tout de suite : l'afficher ? (sinon : cliquer dessus dans la liste, ou « 2e » pour les versets bilingues)
        if (window.confirm(`« ${d.name} » est importée.\n\nL’afficher maintenant à l’écran ?\n(OK = l’afficher · Annuler = la garder dans la liste)`)) await choisir(id);
        else etatCarte(`« ${d.name} » importée : cliquez-la dans la liste pour l’afficher, ou « 2e » pour les versets bilingues.`);
      } catch (x) { $('#bible-etat').textContent = 'Import impossible : ' + (x.message || x); etatCarte('Import impossible : ' + (x.message || x)); }
    });
  }

  /** Bibles où chercher des mots : version principale, 2e version, puis toutes les Bibles importées. */
  async function versions() {
    const ids = [...new Set([reglages.principale, reglages.seconde, ...reglages.importees.map((v) => v.id)].filter(Boolean))];
    const out = [];
    for (const id of ids) {
      const d = id === reglages.principale ? (B.bible() || await charger(id)) : await charger(id);
      if (d && d.books) out.push({ id, short: (info(id) || { short: id.toUpperCase() }).short, data: d });
    }
    if (!out.length && window.LSG) out.push({ id: 'lsg', short: 'LSG', data: window.LSG });
    return out;
  }

  const liste = () => toutes().map((v) => ({ id: v.id, short: v.short, name: v.name }));
  const donnees = async (id) => (id === reglages.principale ? (B.bible() || await charger(id)) : await charger(id));

  window.VLBibles = { etiquette, texte2, analyser, appliquer, versions, liste, donnees, reglages: () => reglages, seconde: () => !!seconde };
  if (reglages.principale !== 'lsg' || reglages.seconde) appliquer(); else majBouton();
})();
