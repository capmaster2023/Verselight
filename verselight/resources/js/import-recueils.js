// VerseLight — import de recueils : PDF de cantiques numérotés et pages web de chants
(function () {
  const $ = (s) => document.querySelector(s);
  const etat = (t) => { $('#imp-etat').textContent = t; };
  const S = window.Songs;

  // mots qui annoncent un refrain (français, anglais, kirundi, kinyarwanda, swahili)
  const REFRAIN = /^(refrain|chœur|choeur|chorus|inyishu|inyikirizo|ikigarukirwamwo|gusubiramwo|kiitikio|pambio|korasi)\b[\s.:–-]*/i;

  // Découpe le texte d'un chant en strophes : « 1. … », « 2) … », refrain, lignes vides
  function strophes(lignes) {
    const out = [];
    let cur = null;
    const pousser = () => { if (cur && cur.l.some((x) => x.trim())) out.push({ label: cur.label, text: cur.l.map((x) => x.trim()).filter(Boolean).join('\n') }); cur = null; };
    let n = 0;
    for (const brut of lignes) {
      const l = brut.replace(/\s+/g, ' ').trim();
      if (!l) { if (cur && cur.l.length) { pousser(); } continue; }
      let m = l.match(/^(\d{1,2})[.)]\s*(.*)$/);
      if (m) { pousser(); n = +m[1]; cur = { label: 'Couplet ' + n, l: m[2] ? [m[2]] : [] }; continue; }
      m = l.match(REFRAIN);
      if (m) { pousser(); const reste = l.slice(m[0].length); cur = { label: 'Refrain', l: reste ? [reste] : [] }; continue; }
      if (!cur) cur = { label: 'Couplet', l: [] };
      cur.l.push(l);
    }
    pousser();
    // strophes trop longues pour l'écran : coupées en morceaux de 6 lignes
    const fin = [];
    for (const s of out) {
      const ls = s.text.split('\n');
      if (ls.length <= 8) { fin.push(s); continue; }
      for (let i = 0; i < ls.length; i += 6) fin.push({ label: s.label + (i ? ' (suite)' : ''), text: ls.slice(i, i + 6).join('\n') });
    }
    return fin;
  }

  // ============================================================ PDF
  async function lignesPdf(buf) {
    pdfjsLib.GlobalWorkerOptions.workerSrc = 'js/pdf.worker.min.js';
    const doc = await pdfjsLib.getDocument({ data: buf }).promise;
    const toutes = [];
    for (let p = 1; p <= doc.numPages; p++) {
      if (p % 10 === 0) etat(`Lecture du PDF : page ${p} sur ${doc.numPages}…`);
      const tc = await (await doc.getPage(p)).getTextContent();
      // regroupe les morceaux de texte par ligne (même hauteur)
      const rangs = [];
      for (const it of tc.items) {
        if (!it.str || !it.str.trim()) continue;
        const y = Math.round(it.transform[5]), x = it.transform[4], hau = Math.hypot(it.transform[2], it.transform[3]);
        let r = rangs.find((q) => Math.abs(q.y - y) <= Math.max(2, hau * 0.3));
        if (!r) { r = { y, morceaux: [], h: hau }; rangs.push(r); }
        r.morceaux.push({ x, s: it.str });
        r.h = Math.max(r.h, hau);
      }
      rangs.sort((a, b) => b.y - a.y);
      let prevY = null, prevH = 0;
      for (const r of rangs) {
        r.morceaux.sort((a, b) => a.x - b.x);
        const txt = r.morceaux.map((m) => m.s).join(' ').replace(/\s+/g, ' ').trim();
        // grand écart vertical = ligne vide (séparation de strophes)
        if (prevY !== null && prevY - r.y > Math.max(prevH, r.h) * 1.9) toutes.push({ t: '', h: 0 });
        toutes.push({ t: txt, h: r.h, page: p });
        prevY = r.y; prevH = r.h;
      }
      toutes.push({ t: '', h: 0 });
    }
    return toutes;
  }

  function chantsDepuisLignes(lignes) {
    const hs = lignes.filter((l) => l.t).map((l) => l.h).sort((a, b) => a - b);
    const mediane = hs[Math.floor(hs.length / 2)] || 10;
    const majuscules = (s) => { const lettres = s.replace(/[^A-Za-zÀ-ÿ]/g, ''); return lettres.length > 3 && lettres === lettres.toUpperCase(); };
    const chants = [];
    let cur = null, attendu = 1;
    for (const l of lignes) {
      const m = l.t.match(/^(\d{1,4})\s*[.)\-–:]?\s+(.{2,90})$/) || l.t.match(/^(\d{1,4})[.)]?$/);
      if (m) {
        const n = +m[1];
        const titre = (m[2] || '').trim();
        // nouveau chant : numéro qui suit le précédent et en-tête visible (plus grand, en majuscules ou seul)
        const entete = l.h > mediane * 1.12 || majuscules(titre) || !titre;
        if ((n === attendu || (n > attendu && n <= attendu + 3)) && entete) {
          if (cur) chants.push(cur);
          cur = { num: n, title: `${n}. ${titre}`.replace(/\.\s*$/, ''), lignes: [] };
          attendu = n + 1;
          continue;
        }
      }
      if (cur) {
        if (cur.title.match(/^\d+\.?$/) && l.t && !cur.lignes.length) { cur.title = `${cur.num}. ${l.t}`; continue; }
        cur.lignes.push(l.t);
      }
    }
    if (cur) chants.push(cur);
    return chants.map((c) => ({ title: c.title, sections: strophes(c.lignes) })).filter((c) => c.sections.length);
  }

  $('#import [data-imp="pdf"]').addEventListener('click', () => $('#pdf').click());
  $('#pdf').addEventListener('change', async (e) => {
    const f = e.target.files[0];
    e.target.value = '';
    if (!f) return;
    try {
      etat('Lecture du PDF…');
      const lignes = await lignesPdf(new Uint8Array(await f.arrayBuffer()));
      const chants = chantsDepuisLignes(lignes);
      if (!chants.length) { etat('Aucun chant numéroté trouvé dans ce PDF (le texte est peut-être une image scannée).'); return; }
      window.VL.ajouterChants(chants);
      etat(`${chants.length} chants importés de « ${f.name} ». Vérifiez quelques chants : un PDF mal formé peut couper des strophes.`);
    } catch (x) { etat('Erreur de lecture du PDF : ' + (x.message || x)); }
  });

  // ============================================================ web
  async function telecharger(url) {
    if (window.VL.natif()) {
      // l'application passe par curl.exe (inclus dans Windows 10 et 11) : pas de blocage CORS
      const r = await Neutralino.os.execCommand(`curl.exe -sL --max-time 30 -A "Mozilla/5.0 VerseLight" "${url.replace(/"/g, '')}"`);
      if (!r.stdOut) throw new Error('page vide ou inaccessible');
      return r.stdOut;
    }
    const r = await fetch(url);
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.text();
  }
  function texteBloc(el) {
    const c = el.cloneNode(true);
    c.querySelectorAll('script,style,nav,header,footer,aside,form,iframe,.sharedaddy,.share,.related,.comments,#comments,button,noscript').forEach((x) => x.remove());
    c.querySelectorAll('br').forEach((b) => b.replaceWith('\n'));
    c.querySelectorAll('p,div,li,h2,h3,h4').forEach((b) => b.append('\n\n'));
    return c.textContent.replace(/ /g, ' ').replace(/[ \t]+/g, ' ').replace(/\n[ \t]+/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  }
  function chantDepuisPage(html, url) {
    const d = new DOMParser().parseFromString(html, 'text/html');
    const titre = (d.querySelector('h1.entry-title, h1.post-title, article h1, h1') || d.querySelector('title') || {}).textContent || url;
    const bloc = d.querySelector('.entry-content, .post-content, .song-content, .lyrics, article, main') || d.body;
    const txt = texteBloc(bloc);
    const sections = strophes(txt.split('\n'));
    return sections.length ? { title: titre.trim().replace(/\s+/g, ' '), sections } : null;
  }
  function liensDeChants(html, url) {
    const d = new DOMParser().parseFromString(html, 'text/html');
    const base = new URL(url);
    const prefixe = base.pathname.replace(/\/$/, '') + '/';
    const vus = new Set();
    for (const a of d.querySelectorAll('a[href]')) {
      let u;
      try { u = new URL(a.getAttribute('href'), base); } catch (x) { continue; }
      if (u.host !== base.host) continue;
      u.hash = '';
      const p = u.pathname;
      if (p.startsWith(prefixe) && p.length > prefixe.length + 1) vus.add(u.href);
    }
    return [...vus];
  }

  $('#import [data-imp="web"]').addEventListener('click', async () => {
    let url = $('#imp-url').value.trim();
    if (!url) return;
    if (!/^https?:\/\//i.test(url)) url = 'https://' + url;
    try {
      etat('Téléchargement de la page…');
      const html = await telecharger(url);
      const liens = liensDeChants(html, url);
      if (liens.length >= 3) {
        // page qui liste des chants : on les importe tous
        const res = [];
        for (let i = 0; i < liens.length && i < 1500; i++) {
          etat(`Import des chants : ${i + 1} sur ${liens.length}…`);
          try { const c = chantDepuisPage(await telecharger(liens[i]), liens[i]); if (c) res.push(c); } catch (x) {}
        }
        window.VL.ajouterChants(res);
        etat(`${res.length} chants importés depuis ${new URL(url).host}.`);
        return;
      }
      const c = chantDepuisPage(html, url);
      if (!c) { etat('Aucune parole trouvée sur cette page.'); return; }
      window.VL.ajouterChants([c]);
      etat(`« ${c.title} » importé.`);
    } catch (x) { etat('Impossible de lire cette page : ' + (x.message || x)); }
  });
})();
