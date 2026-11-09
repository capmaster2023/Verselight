// VerseLight — Bible Louis Segond 1910 (domaine public)
(function () {
// StreamRat — Bible Louis Segond 1910 (domaine public) : références françaises et recherche
const BOOKS = [
  ['Genèse', 'gn gen ge genese'], ['Exode', 'ex exo exod exode'], ['Lévitique', 'lv lev levit levitique'],
  ['Nombres', 'nb nom nomb nombres'], ['Deutéronome', 'dt deut deuteronome'], ['Josué', 'jos josue'],
  ['Juges', 'jg jug juges'], ['Ruth', 'rt ru ruth'], ['1 Samuel', '1s 1sa 1sam 1samuel'], ['2 Samuel', '2s 2sa 2sam 2samuel'],
  ['1 Rois', '1r 1ro 1rois'], ['2 Rois', '2r 2ro 2rois'], ['1 Chroniques', '1ch 1chr 1chron 1chroniques'],
  ['2 Chroniques', '2ch 2chr 2chron 2chroniques'], ['Esdras', 'esd esdr esdras'], ['Néhémie', 'ne neh nehemie'],
  ['Esther', 'est esth esther'], ['Job', 'jb job'], ['Psaumes', 'ps psa psau psaume psaumes'],
  ['Proverbes', 'pr pro prov proverbe proverbes'], ['Ecclésiaste', 'ec ecc eccl ecclesiaste qo'],
  ['Cantique des cantiques', 'ct ca cant cantique cantiques cantiquedescantiques'], ['Ésaïe', 'es esa esaie is isaie'],
  ['Jérémie', 'jr jer jerem jeremie'], ['Lamentations', 'lm lam lament lamentations'], ['Ézéchiel', 'ez eze ezech ezechiel'],
  ['Daniel', 'dn da dan daniel'], ['Osée', 'os osee'], ['Joël', 'jl joe joel'], ['Amos', 'am amo amos'],
  ['Abdias', 'ab abd abdias'], ['Jonas', 'jon jonas'], ['Michée', 'mi mic michee'], ['Nahum', 'na nah nahum'],
  ['Habacuc', 'ha hab habacuc'], ['Sophonie', 'so sop soph sophonie'], ['Aggée', 'ag agg aggee'],
  ['Zacharie', 'za zac zach zacharie'], ['Malachie', 'ml mal malachie'], ['Matthieu', 'mt mat matt matthieu'],
  ['Marc', 'mc mr marc'], ['Luc', 'lc lu luc'], ['Jean', 'jn jea jean'], ['Actes', 'ac act actes'],
  ['Romains', 'rm ro rom romains'], ['1 Corinthiens', '1co 1cor 1corinthiens'], ['2 Corinthiens', '2co 2cor 2corinthiens'],
  ['Galates', 'ga gal galates'], ['Éphésiens', 'ep eph ephes ephesiens'], ['Philippiens', 'ph phi phil philip philippiens'],
  ['Colossiens', 'col colos colossiens'], ['1 Thessaloniciens', '1th 1thes 1thess 1thessaloniciens'],
  ['2 Thessaloniciens', '2th 2thes 2thess 2thessaloniciens'], ['1 Timothée', '1tm 1ti 1tim 1timothee'],
  ['2 Timothée', '2tm 2ti 2tim 2timothee'], ['Tite', 'tt tit tite'], ['Philémon', 'phm phl philem philemon'],
  ['Hébreux', 'he heb hebr hebreux'], ['Jacques', 'jc jq jac jacq jacques'], ['1 Pierre', '1p 1pi 1pie 1pierre'],
  ['2 Pierre', '2p 2pi 2pie 2pierre'], ['1 Jean', '1jn 1j 1jean'], ['2 Jean', '2jn 2j 2jean'], ['3 Jean', '3jn 3j 3jean'],
  ['Jude', 'jud jude'], ['Apocalypse', 'ap apo apoc apocalypse'],
].map(([name, ab], i) => ({ i, name, ab: ab.split(' ') }));

function norm(s) {
  return (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/[’'`]/g, ' ').replace(/œ/g, 'oe').replace(/æ/g, 'ae');
}

const alias = new Map();
const fullNames = [];
for (const b of BOOKS) {
  for (const a of b.ab) alias.set(a, b.i);
  const compact = norm(b.name).replace(/[^a-z0-9]/g, '');
  alias.set(compact, b.i);
  fullNames.push([compact, b.i]);
}

let data = null;
let loading = null;
let normCache = null;
function loadBible() {
  if (data) return Promise.resolve(data);
  if (!loading) loading = Promise.resolve(window.LSG).then((d) => (data = d));
  return loading;
}
const bible = () => data;

function chapterCount(b) { return data ? data.books[b].ch.length : 0; }
function verses(b, c) { return data?.books[b]?.ch[c - 1] || []; }
function bookName(b) { return BOOKS[b]?.name || ''; }
function refLabel(b, c, v1, v2) {
  let s = `${bookName(b)} ${c}`;
  if (v1) s += `:${v1}` + (v2 && v2 !== v1 ? `-${v2}` : '');
  return s;
}

// « jn 3 16 », « Jean 3:16-18 », « 1 co 13 », « ps 23 », « premier jean 1,9 », « Rm 8.28 »
function parseRef(input) {
  let s = norm(input).trim();
  if (!s) return null;
  s = s.replace(/\bchap(itre)?s?\b/g, ' ').replace(/\bversets?\b/g, ':').replace(/\bv(?=\s*\d)/g, ':')
    .replace(/\b(premiere|premier|1ere|1re|1er)\b/g, '1').replace(/\b(deuxieme|seconde|second|2e|2eme)\b/g, '2')
    .replace(/\b(troisieme|3e|3eme)\b/g, '3').replace(/^(iii)\s+(?=[a-z])/, '3 ').replace(/^(ii)\s+(?=[a-z])/, '2 ').replace(/^(i)\s+(?=[a-z])/, '1 ')
    .replace(/\s+/g, ' ').trim();
  const m = s.match(/^([123])?\s*([a-z][a-z ]*?)\s*(?:(\d{1,3})(?:\s*[:.,]\s*|\s+)?(\d{1,3})?(?:\s*[-–à]\s*(\d{1,3}))?)?$/);
  if (!m) return null;
  const num = m[1] || '';
  const word = m[2].replace(/\s+/g, '');
  const key = num + word;
  let b = alias.has(key) ? alias.get(key) : -1;
  if (b < 0 && word.length >= 3) {
    const cands = fullNames.filter(([n]) => n.startsWith(key));
    if (cands.length === 1) b = cands[0][1];
    else if (cands.length > 1 && !num) b = cands[0][1];
    if (b < 0 && !num) {
      const c2 = fullNames.filter(([n]) => n.startsWith('1' + word));
      if (c2.length) b = c2[0][1];
    }
  }
  if (b < 0) return null;
  const c = m[3] ? parseInt(m[3], 10) : 1;
  const v1 = m[4] ? parseInt(m[4], 10) : 0;
  const v2 = m[5] ? parseInt(m[5], 10) : v1;
  if (data) {
    if (c < 1 || c > chapterCount(b)) return { b, c: Math.min(Math.max(1, c), chapterCount(b)), v1: 0, v2: 0, partial: true };
    const n = verses(b, c).length;
    if (v1 > n) return { b, c, v1: 0, v2: 0, partial: true };
    return { b, c, v1, v2: Math.min(Math.max(v2, v1), n) };
  }
  return { b, c, v1, v2 };
}

function search(q, limit = 80) {
  if (!data) return [];
  const words = norm(q).replace(/[^a-z0-9 ]/g, ' ').split(/\s+/).filter((w) => w.length > 1);
  if (!words.length) return [];
  if (!normCache) {
    normCache = data.books.map((bk) => bk.ch.map((ch) => ch.map((v) => norm(v))));
  }
  const out = [];
  for (let b = 0; b < normCache.length && out.length < limit; b++) {
    const bk = normCache[b];
    for (let c = 0; c < bk.length && out.length < limit; c++) {
      const ch = bk[c];
      for (let v = 0; v < ch.length; v++) {
        const t = ch[v];
        if (words.every((w) => t.includes(w))) {
          out.push({ b, c: c + 1, v: v + 1, text: data.books[b].ch[c][v] });
          if (out.length >= limit) break;
        }
      }
    }
  }
  return out;
}

function versesText(b, c, v1, v2) {
  const vs = verses(b, c);
  const out = [];
  for (let v = v1; v <= v2 && v <= vs.length; v++) out.push(vs[v - 1]);
  return out.join(' ');
}
function _setData(d) { data = d; normCache = null; }

window.Bible = { BOOKS, norm, loadBible, bible, chapterCount, verses, bookName, refLabel, parseRef, search, versesText, _setData };
})();
