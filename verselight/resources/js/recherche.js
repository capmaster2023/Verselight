// MyVerse — recherche par mots dans toutes les Bibles installées (Louis Segond, KJV, Bibles importées :
// kirundi, swahili…). Indépendante de la langue, tolère les fautes de frappe et d'orthographe,
// classe les versets par pertinence et indique où ils se trouvent.
(function () {
  'use strict';
  const MOT = /[\p{L}\p{N}]+/gu;
  const norm = (s) => String(s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/œ/g, 'oe').replace(/æ/g, 'ae').replace(/ß/g, 'ss');
  const mots = (s) => norm(s).match(MOT) || [];

  // ------------------------------------------------------------ index (un par Bible, construit une fois)
  const index = new WeakMap();
  function indexer(d) {
    let ix = index.get(d);
    if (ix) return ix;
    const refs = [];
    const termes = new Map();
    (d.books || []).forEach((bk, b) => ((bk && bk.ch) || []).forEach((ch, c) => (ch || []).forEach((t, v) => {
      if (!t) return;
      const id = refs.length;
      refs.push([b, c + 1, v + 1]);
      for (const w of new Set(mots(t))) {
        const l = termes.get(w);
        if (l) l.push(id); else termes.set(w, [id]);
      }
    })));
    const parLongueur = [];
    for (const w of termes.keys()) (parLongueur[w.length] || (parLongueur[w.length] = [])).push(w);
    ix = { d, refs, termes, parLongueur, n: refs.length, sons: null };
    index.set(d, ix);
    return ix;
  }

  // ------------------------------------------------------------ comparaison de mots
  // distance de Damerau-Levenshtein (lettres inversées, oubliées, en trop, remplacées), bornée à « max »
  const L0 = new Int32Array(80), L1 = new Int32Array(80), L2 = new Int32Array(80);
  function distance(a, b, max) {
    const la = a.length, lb = b.length;
    if (Math.abs(la - lb) > max) return max + 1;
    if (la >= 79 || lb >= 79) return a === b ? 0 : max + 1;
    let p2 = L2, p = L0, c = L1;
    for (let j = 0; j <= lb; j++) p[j] = j;
    for (let i = 1; i <= la; i++) {
      c[0] = i;
      let min = i;
      const ai = a.charCodeAt(i - 1);
      for (let j = 1; j <= lb; j++) {
        const bj = b.charCodeAt(j - 1);
        let v = p[j - 1] + (ai === bj ? 0 : 1);
        if (p[j] + 1 < v) v = p[j] + 1;
        if (c[j - 1] + 1 < v) v = c[j - 1] + 1;
        if (i > 1 && j > 1 && ai === b.charCodeAt(j - 2) && a.charCodeAt(i - 2) === bj && p2[j - 2] + 1 < v) v = p2[j - 2] + 1;
        c[j] = v;
        if (v < min) min = v;
      }
      if (min > max) return max + 1;
      const t = p2; p2 = p; p = c; c = t;
    }
    return p[lb];
  }
  // fautes « à l'oreille » (keur → coeur, sein → saint, batème → baptême) : mots écrits comme ils se prononcent
  function son(w) {
    let s = w.replace(/(?<=...)(er|ez)$/, 'e')
      .replace(/ph/g, 'f').replace(/ch(?=[rl])/g, 'k').replace(/ch/g, 'x').replace(/qu|q/g, 'k').replace(/c(?=[eiy])/g, 's').replace(/c/g, 'k')
      .replace(/[jg]e(?=[ao])/g, 'j').replace(/g(?=[eiy])/g, 'j').replace(/gu(?=[eiy])/g, 'g')
      .replace(/h/g, '').replace(/y/g, 'i').replace(/w/g, 'v').replace(/(.)\1+/g, '$1')
      .replace(/eau|au/g, 'o').replace(/oeu|oe|eu/g, 'e').replace(/ou/g, 'u')
      .replace(/[ae]i[nm](?![aeiou])|[iu][nm](?![aeiou])/g, '1').replace(/[ae][nm](?![aeiou])/g, '2').replace(/o[nm](?![aeiou])/g, '3')
      .replace(/ai|ei/g, 'e').replace(/pt/g, 't');
    return s.replace(/(?<=..)(s|x|t|d)$/, '').replace(/(?<=..)e$/, '').replace(/(?<=..)(s|x|t|d)$/, '');
  }
  const sonUtile = (s) => s.length >= 3 || (s.length === 2 && /\d/.test(s));
  const tolerance = (n) => (n <= 3 ? 0 : n <= 6 ? 1 : 2);

  // mots de la Bible qui correspondent à un mot tapé : terme -> qualité (1 = exact)
  function candidats(ix, q) {
    const out = new Map();
    const ajouter = (t, s) => { if ((out.get(t) || 0) < s) out.set(t, s); };
    const L = q.length;
    if (ix.termes.has(q)) ajouter(q, 1);
    const k = tolerance(L);
    for (let n = Math.max(1, L - k); n < ix.parLongueur.length; n++) {
      const liste = ix.parLongueur[n];
      if (!liste) continue;
      const prefixe = L >= 3 && n > L;
      const flou = k > 0 && n <= L + k;
      if (!prefixe && !flou) continue;
      for (const t of liste) {
        if (prefixe && t.startsWith(q)) { ajouter(t, n - L <= 3 ? 0.9 : 0.7); continue; }
        // une faute sur la première lettre est plus rare : un peu moins de points
        if (flou && t !== q) { const e = distance(q, t, k); if (e <= k) ajouter(t, (e === 1 ? 0.75 : 0.55) * (t[0] === q[0] ? 1 : 0.85)); }
      }
    }
    if (L >= 4 && /[a-z]/.test(q)) {
      if (!ix.sons) {
        ix.sons = new Map();
        for (const t of ix.termes.keys()) if (t.length >= 3) { const s = son(t); if (!sonUtile(s)) continue; const l = ix.sons.get(s); if (l) l.push(t); else ix.sons.set(s, [t]); }
      }
      const s = son(q);
      if (sonUtile(s)) for (const t of ix.sons.get(s) || []) ajouter(t, 0.85);
    }
    return out;
  }

  // ------------------------------------------------------------ recherche dans une Bible
  function chercherDans(d, requete) {
    const ix = indexer(d);
    const qs = [...new Set(mots(requete))].filter((w) => w.length > 1 || /\d/.test(w));
    const vide = { res: [], termes: new Set() };
    if (!qs.length || !ix.n) return vide;
    const n = ix.n;
    const score = new Float32Array(n);
    const groupes = [];
    let total = 0;
    for (const q of qs) {
      const cands = candidats(ix, q);
      const meilleur = new Float32Array(n);
      let df = 0;
      for (const [t, s] of cands) for (const id of ix.termes.get(t)) { if (!meilleur[id]) df++; if (s > meilleur[id]) meilleur[id] = s; }
      // les mots rares (« patiente ») comptent plus que les mots courants (« et », « le »)
      const w = Math.log(1 + n / (df + 1));
      total += w;
      if (df) for (let id = 0; id < n; id++) if (meilleur[id]) score[id] += w * meilleur[id];
      groupes.push({ q, w, termes: new Set(cands.keys()) });
    }
    let best = 0;
    const ids = [];
    for (let id = 0; id < n; id++) if (score[id] > 0) { ids.push(id); if (score[id] > best) best = score[id]; }
    if (!best) return vide;
    ids.sort((a, b) => score[b] - score[a] || a - b);
    const garde = ids.slice(0, 600).filter((id) => score[id] >= best * 0.6 && score[id] >= total * 0.34);
    const texte = (id) => { const [b, c, v] = ix.refs[id]; return d.books[b].ch[c - 1][v - 1]; };
    const res = garde.map((id) => {
      let bonus = 0;
      if (groupes.length > 1) {
        // mots tapés dans le même ordre, l'un après l'autre (« tant aimé le monde ») : bonus
        const ws = mots(texte(id));
        for (let g = 0; g + 1 < groupes.length; g++) {
          const A = groupes[g].termes, Bn = groupes[g + 1].termes;
          for (let p = 0; p + 1 < ws.length; p++) if (A.has(ws[p]) && Bn.has(ws[p + 1])) { bonus += 0.3 * (groupes[g].w + groupes[g + 1].w) / 2; break; }
        }
      }
      const [b, c, v] = ix.refs[id];
      return { b, c, v, text: texte(id), note: (score[id] + bonus) / total };
    });
    const termes = new Set();
    for (const g of groupes) for (const t of g.termes) termes.add(t);
    return { res, termes };
  }

  // ------------------------------------------------------------ recherche dans toutes les versions
  async function versions() {
    if (window.VLBibles && window.VLBibles.versions) return window.VLBibles.versions();
    const B = window.Bible;
    return [{ id: 'lsg', short: 'LSG', data: (B && B.bible()) || window.LSG }];
  }
  /** Cherche des mots (même mal écrits) dans la version principale, la 2e version et les Bibles importées. */
  async function chercher(requete, opts = {}) {
    const limite = opts.limite || 100;
    const vs = (await versions()).filter((v) => v.data && v.data.books);
    const parRef = new Map();
    const termes = new Set();
    vs.forEach((ver, rang) => {
      const r = chercherDans(ver.data, requete);
      r.termes.forEach((t) => termes.add(t));
      for (const x of r.res) {
        const cle = x.b + '.' + x.c + '.' + x.v;
        const p = parRef.get(cle);
        if (!p || x.note > p.note + 0.05) parRef.set(cle, { ...x, version: ver.short, principale: rang === 0, rang });
      }
    });
    const tous = [...parRef.values()];
    let best = 0;
    for (const x of tous) if (x.note > best) best = x.note;
    const res = tous.filter((x) => x.note >= best * 0.6)
      .sort((a, b) => b.note - a.note || a.rang - b.rang || a.b - b.b || a.c - b.c || a.v - b.v)
      .slice(0, limite);
    res.termes = termes;
    return res;
  }
  /** Découpe un texte en morceaux { t, mot } pour surligner les mots trouvés. */
  function morceaux(texte, termes) {
    const out = [];
    let i = 0;
    for (const m of String(texte).matchAll(MOT)) {
      const w = norm(m[0]);
      if (termes && termes.has(w)) {
        if (m.index > i) out.push({ t: texte.slice(i, m.index) });
        out.push({ t: m[0], mot: true });
        i = m.index + m[0].length;
      }
    }
    if (i < texte.length) out.push({ t: texte.slice(i) });
    return out;
  }
  /** Prépare l'index de la version principale à l'avance (la première recherche est alors instantanée). */
  async function preparer() {
    try { for (const v of await versions()) if (v.data && v.data.books) indexer(v.data); } catch (e) {}
  }

  window.VLRecherche = { chercher, chercherDans, morceaux, preparer, distance, son, norm };
})();
