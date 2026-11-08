// VerseLight — import de chants (VideoPsalm, OpenLyrics, OpenSong, ChordPro, SongSelect, texte, ProPresenter 6)
(function () {
// StreamRat — lecture des fichiers de chants
// VideoPsalm, OpenLyrics (OpenLP), OpenSong, ChordPro, SongSelect (CCLI), ProPresenter 6 et texte simple.
// Fonctionne dans le navigateur et dans Node (tests), sans dépendance.
//
// Format d'un chant :
//   { title, author, copyright?, ccli?, book?, sections: [{ label, text }] }

const ACCEPT = '.json,.txt,.xml,.vpc,.vpsong,.cho,.chordpro,.chopro,.crd,.pro,.pro6,.song,.usr,application/json,text/plain,text/xml,application/xml';

// ============================================================ outils texte
function clean(text) {
  return String(text ?? '')
    .replace(/^﻿/, '')
    .replace(/\r\n?/g, '\n')
    .replace(/[​‌‍⁠]/g, '')
    .replace(/ {2,}/g, ' ');
}

// Nettoie le texte d'une section : lignes sans espaces superflus, sans lignes vides au bord
function tidy(text) {
  const lines = clean(text).split('\n').map((l) => l.replace(/[ \t]+/g, ' ').trim());
  while (lines.length && !lines[0]) lines.shift();
  while (lines.length && !lines[lines.length - 1]) lines.pop();
  return lines.join('\n');
}

const stripChordsInline = (line) => line.replace(/\[[^\]\n]*\]/g, '');

// ============================================================ étiquettes en français
const LABELS = [
  [/^(v|vs|verse|vers|couplet|strophe|cplt|cpl)$/, 'Couplet'],
  [/^(c|ch|chorus|refrain|ref|r|refr)$/, 'Refrain'],
  [/^(p|pc|pre-?chorus|pre-?refrain|pré-?refrain|prechorus|pré refrain|pre refrain)$/, 'Pré-refrain'],
  [/^(b|br|bridge|pont)$/, 'Pont'],
  [/^(e|end|ending|outro|fin|coda)$/, 'Fin'],
  [/^(i|intro|introduction)$/, 'Intro'],
  [/^(t|tag|final|finale)$/, 'Final'],
  [/^(instrumental|interlude|inst)$/, 'Interlude'],
];

function frLabel(raw) {
  const s = String(raw ?? '').trim().replace(/[:：]\s*$/, '');
  if (!s) return '';
  const m = s.toLowerCase().match(/^([a-zéèêàçùû][a-zéèêàçùû\- ]*?)\s*(?:(\d+)\s*([a-z])?)?$/);
  if (m) {
    for (const [re, fr] of LABELS) {
      if (re.test(m[1].trim())) return m[2] ? `${fr} ${parseInt(m[2], 10)}` : fr;
    }
  }
  return s.charAt(0).toUpperCase() + s.slice(1);
}

// Numérote « Couplet » quand il y en a plusieurs sans numéro ; ôte le numéro s'il n'y a qu'un couplet 1
function finishLabels(sections) {
  const plain = sections.filter((s) => s.label === 'Couplet');
  const used = new Set(sections.map((s) => s.label.match(/^Couplet (\d+)$/)).filter(Boolean).map((m) => +m[1]));
  const distinctVerses = new Set(sections.filter((s) => /^Couplet( \d+)?$/.test(s.label)).map((s) => s.label));
  if (plain.length > 1 || (plain.length && used.size)) {
    // numérote les couplets sans numéro en sautant les numéros déjà pris
    let n = 0;
    for (const s of sections) {
      if (s.label !== 'Couplet') continue;
      do n++; while (used.has(n));
      s.label = `Couplet ${n}`;
    }
  } else if (distinctVerses.size === 1 && distinctVerses.has('Couplet 1')) {
    for (const s of sections) if (s.label === 'Couplet 1') s.label = 'Couplet';
  }
  for (const s of sections) if (!s.label) s.label = 'Couplet';
  return sections;
}

function makeSong(o) {
  const sections = finishLabels((o.sections || [])
    .map((s) => ({ label: frLabel(s.label) || 'Couplet', text: tidy(s.text) }))
    .filter((s) => s.text));
  const song = { title: String(o.title || '').trim() || 'Sans titre', author: String(o.author || '').trim(), sections };
  if (o.copyright) song.copyright = String(o.copyright).trim();
  if (o.ccli) song.ccli = String(o.ccli).trim();
  if (o.book) song.book = String(o.book).trim();
  return song;
}

// ============================================================ petit analyseur XML tolérant
const ENT = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' };
function decodeEntities(s) {
  return s.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (m, e) => {
    if (e[0] === '#') {
      const code = e[1] === 'x' || e[1] === 'X' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
      try { return String.fromCodePoint(code); } catch { return m; }
    }
    return ENT[e.toLowerCase()] ?? m;
  });
}

function parseXml(text) {
  const root = { name: '#document', attrs: {}, children: [] };
  const stack = [root];
  const src = clean(text);
  const re = /<!--[\s\S]*?-->|<!\[CDATA\[([\s\S]*?)\]\]>|<\?[\s\S]*?\?>|<!DOCTYPE[^>[]*(?:\[[\s\S]*?\])?\s*>|<\/\s*([^\s>]+)\s*>|<([^\s/>!?]+)((?:\s+[^\s=/>]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?)*)\s*(\/?)>/g;
  let last = 0;
  let m;
  const top = () => stack[stack.length - 1];
  while ((m = re.exec(src))) {
    if (m.index > last) top().children.push(decodeEntities(src.slice(last, m.index)));
    last = re.lastIndex;
    if (m[1] !== undefined) { top().children.push(m[1]); continue; }
    if (m[2] !== undefined) {
      const name = local(m[2]);
      for (let i = stack.length - 1; i > 0; i--) {
        if (stack[i].name === name) { stack.length = i; break; }
      }
      continue;
    }
    if (m[3] !== undefined) {
      const node = { name: local(m[3]), attrs: {}, children: [] };
      const ar = /([^\s=/>]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g;
      let a;
      while ((a = ar.exec(m[4] || ''))) node.attrs[local(a[1])] = decodeEntities(a[2] ?? a[3] ?? a[4] ?? '');
      top().children.push(node);
      if (!m[5]) stack.push(node);
    }
  }
  if (last < src.length) top().children.push(decodeEntities(src.slice(last)));
  return root;
}
function local(n) { const i = n.indexOf(':'); return (i >= 0 ? n.slice(i + 1) : n).toLowerCase(); }
const elems = (n) => (n?.children || []).filter((c) => typeof c === 'object');
function findAll(n, name, out = []) {
  for (const c of elems(n)) { if (c.name === name) out.push(c); findAll(c, name, out); }
  return out;
}
const find = (n, name) => findAll(n, name)[0] || null;
function textOf(n) {
  if (!n) return '';
  if (typeof n === 'string') return n;
  return n.children.map(textOf).join('');
}

// ============================================================ VideoPsalm (JSON « relâché »)
function looseJson(text) {
  const t = clean(text).replace(/^\uFEFF/, '');
  try { return JSON.parse(t); } catch {}
  // JSON « relâché » de VideoPsalm : clés sans guillemets, retours à la ligne dans les textes, virgules finales
  let out = '', i = 0, inStr = false, q = '';
  while (i < t.length) {
    const c = t[i];
    if (inStr) {
      if (c === '\\') { out += c + (t[i + 1] || ''); i += 2; continue; }
      if (c === q) { inStr = false; out += '"'; i++; continue; }
      if (c === '\n') out += '\\n'; else if (c === '\r') { /* ignoré */ } else if (c === '\t') out += '\\t';
      else if (c === '"') out += '\\"';
      else if (c < ' ') out += c === '\v' || c === '\f' ? '\\n' : ' '; // autres caractères de contrôle
      else out += c;
      i++; continue;
    }
    if (c === '"' || c === "'") { inStr = true; q = c; out += '"'; i++; continue; }
    if (/[A-Za-z_$]/.test(c)) {
      let j = i; while (j < t.length && /[A-Za-z0-9_$]/.test(t[j])) j++;
      const word = t.slice(i, j);
      let k = j; while (k < t.length && /\s/.test(t[k])) k++;
      if (t[k] === ':') out += '"' + word + '"'; else out += word;
      i = j; continue;
    }
    out += c; i++;
  }
  return JSON.parse(out.replace(/,\s*([}\]])/g, '$1'));
}

function vpLabel(v, i, n) {
  const tag = v.Tag ?? v.tag;
  const txt = String(v.Text || v.text || '').toLowerCase();
  if (tag === 2 || tag === '2' || /^(refrain|chorus)\b/.test(txt)) return 'Refrain';
  if (tag === 3 || tag === '3') return 'Pont';
  return n > 1 ? `Couplet ${i + 1}` : 'Couplet';
}

function parseVideoPsalm(text) {
  let obj;
  try { obj = looseJson(text); } catch { throw new Error('Ce fichier ne ressemble pas à un recueil VideoPsalm valide.'); }
  let arr = obj.Songs || obj.songs || obj.Song || (Array.isArray(obj) ? obj : [obj]);
  if (!Array.isArray(arr)) arr = [arr];
  const book = obj.Text || obj.Name || '';
  return arr.filter((s) => s && typeof s === 'object').map((s) => {
    const verses = s.Verses || s.verses || s.Lyrics || s.Sections || [];
    let couplet = 0;
    const sections = (Array.isArray(verses) ? verses : []).map((v, i) => {
      let label = vpLabel(v, i, verses.length);
      if (label.startsWith('Couplet')) label = `Couplet ${++couplet}`;
      return { label, text: String(v.Text || v.text || '') };
    });
    return makeSong({
      title: s.Text || s.Title || s.title || s.Name || 'Sans titre',
      author: s.Author || s.author || '',
      copyright: s.Copyright || s.copyright || '',
      ccli: s.CCLI || s.Ccli || '',
      book: s.Book || book,
      sections,
    });
  }).filter((s) => s.sections.length);
}

// ============================================================ OpenLyrics (OpenLP)
function olLines(n) {
  if (typeof n === 'string') return n.replace(/\s*\n\s*/g, ' ');
  if (n.name === 'br') return '\n';
  if (n.name === 'comment') return '';
  if (n.name === 'line') return n.children.map(olLines).join('') + '\n'; // OpenLyrics 0.7
  return n.children.map(olLines).join('');
}

function parseOpenLyrics(text) {
  const doc = parseXml(text);
  const song = find(doc, 'song');
  if (!song) throw new Error('Ce fichier ne ressemble pas à un chant OpenLyrics valide.');
  const props = find(song, 'properties');
  const title = textOf(find(props, 'title')).trim();
  const authors = findAll(props, 'author').map((a) => textOf(a).trim()).filter(Boolean);
  const verses = findAll(find(song, 'lyrics'), 'verse');
  const langs = verses.map((v) => v.attrs.lang).filter(Boolean);
  const lang = langs[0];
  const byName = new Map();
  const ordered = [];
  for (const v of verses) {
    if (lang && v.attrs.lang && v.attrs.lang !== lang) continue;
    const name = (v.attrs.name || '').trim();
    const parts = findAll(v, 'lines').map((l) => olLines(l));
    const txt = (parts.length ? parts : [olLines(v)]).join('\n');
    const sec = { key: name.toLowerCase(), label: olName(name), text: txt };
    if (!byName.has(sec.key)) byName.set(sec.key, sec);
    ordered.push(sec);
  }
  const order = textOf(find(props, 'verseorder')).trim().toLowerCase().split(/\s+/).filter(Boolean);
  let sections = ordered;
  if (order.length) {
    const seq = order.map((k) => byName.get(k)).filter(Boolean);
    if (seq.length) sections = seq.map((s) => ({ ...s }));
  }
  return [makeSong({
    title: title || 'Sans titre',
    author: authors.join(', '),
    copyright: textOf(find(props, 'copyright')).trim(),
    ccli: textOf(find(props, 'cclino')).trim(),
    sections,
  })].filter((s) => s.sections.length);
}

// « v1 » → Couplet 1, « c » → Refrain, « b » → Pont, « p » → Pré-refrain, « e » → Fin
function olName(name) {
  const m = String(name).toLowerCase().match(/^([a-z]+)(\d*)([a-z]?)$/);
  if (!m) return frLabel(name);
  return frLabel(m[1] + (m[2] ? ' ' + m[2] : ''));
}

// ============================================================ OpenSong
function parseOpenSong(text) {
  const doc = parseXml(text);
  const song = find(doc, 'song');
  if (!song) throw new Error('Ce fichier ne ressemble pas à un chant OpenSong valide.');
  const lyrics = clean(textOf(find(song, 'lyrics')));
  const blocks = [];
  let cur = null;
  for (const raw of lyrics.split('\n')) {
    const line = raw.replace(/\s+$/, '');
    const head = line.match(/^\s*\[([^\]]+)\]\s*$/);
    if (head) { cur = { key: head[1].trim().toUpperCase(), lines: [] }; blocks.push(cur); continue; }
    if (/^\./.test(line) || /^;/.test(line)) continue; // accords, commentaires
    if (!cur) { cur = { key: 'V', lines: [] }; blocks.push(cur); }
    cur.lines.push(line.replace(/^ /, '').replace(/\|\|?/g, '\n').replace(/_/g, ''));
  }
  // lignes « 1 texte », « 2 texte » : plusieurs couplets dans un même bloc
  const sections = new Map();
  const order = [];
  for (const b of blocks) {
    const numbered = b.lines.filter((l) => /^\d\s/.test(l));
    if (numbered.length && numbered.length >= b.lines.filter((l) => l.trim()).length / 2) {
      const per = new Map();
      for (const l of b.lines) {
        const m = l.match(/^(\d)\s(.*)$/);
        if (m) { if (!per.has(m[1])) per.set(m[1], []); per.get(m[1]).push(m[2]); }
      }
      for (const [n, ls] of per) {
        const key = b.key.replace(/\d+$/, '') + n;
        sections.set(key, { key, label: osName(key), text: ls.join('\n') });
        order.push(key);
      }
    } else {
      sections.set(b.key, { key: b.key, label: osName(b.key), text: b.lines.join('\n') });
      order.push(b.key);
    }
  }
  const pres = textOf(find(song, 'presentation')).trim().toUpperCase().split(/\s+/).filter(Boolean);
  const seq = (pres.length ? pres : order).map((k) => sections.get(k)).filter(Boolean).map((s) => ({ ...s }));
  return [makeSong({
    title: textOf(find(song, 'title')).trim() || 'Sans titre',
    author: textOf(find(song, 'author')).trim(),
    copyright: textOf(find(song, 'copyright')).trim(),
    ccli: textOf(find(song, 'ccli')).trim(),
    sections: seq,
  })].filter((s) => s.sections.length);
}
function osName(key) {
  const m = key.match(/^([A-Z]+)(\d*)$/);
  return m ? frLabel(m[1].toLowerCase() + (m[2] ? ' ' + m[2] : '')) : frLabel(key);
}

// ============================================================ ChordPro
function parseChordPro(text) {
  const songs = [];
  let song = null;
  let sec = null;
  let env = null; // verse | chorus | bridge | tab | grid
  let pendingLabel = '';
  let lastChorus = null;
  const newSong = () => { song = { title: '', author: '', copyright: '', ccli: '', sections: [] }; songs.push(song); sec = null; env = null; lastChorus = null; };
  const close = () => {
    if (sec && sec.lines.some((l) => l.trim())) {
      const s = { label: sec.label, text: sec.lines.join('\n') };
      song.sections.push(s);
      if (sec.kind === 'chorus') lastChorus = s;
    }
    sec = null;
  };
  const open = (kind, label) => { close(); sec = { kind, label: label || pendingLabel || (kind === 'chorus' ? 'Refrain' : kind === 'bridge' ? 'Pont' : ''), lines: [] }; pendingLabel = ''; };
  newSong();
  for (const raw of clean(text).split('\n')) {
    const line = raw.replace(/\s+$/, '');
    const d = line.match(/^\s*\{\s*([a-z_]+)\s*(?:[:\s]\s*(.*?))?\s*\}\s*$/i);
    if (d) {
      const k = d[1].toLowerCase();
      const v = (d[2] || '').trim();
      if (k === 'new_song' || k === 'ns') { close(); newSong(); continue; }
      if (k === 'title' || k === 't') { song.title = v; continue; }
      if (k === 'artist' || k === 'composer' || k === 'lyricist' || k === 'subtitle' || k === 'st') { if (!song.author) song.author = v; continue; }
      if (k === 'copyright') { song.copyright = v; continue; }
      if (k === 'ccli') { song.ccli = v; continue; }
      if (k === 'start_of_chorus' || k === 'soc') { env = 'chorus'; open('chorus', v ? frLabel(v) : 'Refrain'); continue; }
      if (k === 'start_of_verse' || k === 'sov') { env = 'verse'; open('verse', v ? frLabel(v) : ''); continue; }
      if (k === 'start_of_bridge' || k === 'sob') { env = 'bridge'; open('bridge', v ? frLabel(v) : 'Pont'); continue; }
      if (k === 'start_of_tab' || k === 'sot' || k === 'start_of_grid' || k === 'sog') { close(); env = 'skip'; continue; }
      if (/^end_of_|^eo[cvbtg]$/.test(k)) { close(); env = null; continue; }
      if (k === 'chorus') { close(); if (lastChorus) song.sections.push({ label: lastChorus.label, text: lastChorus.text }); continue; }
      if (k === 'comment' || k === 'c' || k === 'comment_italic' || k === 'ci' || k === 'comment_box' || k === 'cb') {
        if (v && /^(verse|vers|couplet|chorus|refrain|bridge|pont|pre-?chorus|pré-?refrain|intro|outro|ending|fin|tag|final)\b/i.test(v)) {
          close();
          pendingLabel = frLabel(v);
        }
        continue;
      }
      continue; // autres directives ignorées
    }
    if (env === 'skip') continue;
    if (/^\s*#/.test(line)) continue;
    if (!line.trim()) { if (!env) close(); else if (sec) sec.lines.push(''); continue; }
    // étiquette seule sur une ligne : « Verse 1: », « Refrain : »
    const lab = line.trim().match(/^(verse|vers|couplet|chorus|refrain|bridge|pont|pre-?chorus|pré-?refrain|intro|outro|ending|tag|final)\s*\d*\s*:?$/i);
    if (lab && !env) { close(); pendingLabel = frLabel(line.trim()); continue; }
    if (!sec) open(env || 'verse', '');
    sec.lines.push(stripChordsInline(line));
  }
  close();
  return songs.map(makeSong).filter((s) => s.sections.length);
}

// ============================================================ SongSelect (CCLI, texte)
const SS_HEAD = /^(verse|chorus|bridge|pre-?chorus|tag|ending|intro|outro|instrumental|interlude|misc|vamp|channel|couplet|refrain|pont|pré-?refrain|fin|strophe)(\s+\d+[a-z]?)?\s*$/i;
const SS_FOOT = /^(ccli song|ccli-liednummer|ccli song number|chant ccli|ccli license|ccli-lizenz|licence ccli|for use solely|pour une utilisation|nur zur verwendung)/i;

function parseSongSelect(text) {
  const lines = clean(text).split('\n').map((l) => l.replace(/\s+$/, ''));
  while (lines.length && !lines[0].trim()) lines.shift();
  const title = (lines.shift() || '').trim();
  const out = { title, author: '', copyright: '', ccli: '', sections: [] };
  // pied de page
  const footAt = lines.findIndex((l) => SS_FOOT.test(l.trim()));
  if (footAt >= 0) {
    // les auteurs (« A | B ») et le © peuvent précéder « CCLI Song # »
    let start = footAt;
    while (start > 0 && /\s\|\s|^©|^\(c\)|^copyright|^public domain|^domaine public/i.test(lines[start - 1].trim())) start--;
    const foot = lines.slice(start);
    lines.length = start;
    for (const l of foot) {
      const t = l.trim();
      if (!t) continue;
      const n = t.match(/(\d{3,})/);
      if (/^(ccli song|ccli-liednummer|ccli song number|chant ccli)/i.test(t)) { if (n) out.ccli = n[1]; }
      else if (SS_FOOT.test(t)) continue;
      else if (/^(©|\(c\)|copyright|public domain|domaine public)/i.test(t)) out.copyright = t.replace(/^copyright\s*/i, '');
      else if (!out.author) out.author = t.split(/\s*\|\s*/).join(', ');
      else if (!out.copyright) out.copyright = t;
    }
  }
  let cur = null;
  for (const l of lines) {
    const t = l.trim();
    if (SS_HEAD.test(t)) { cur = { label: frLabel(t), lines: [] }; out.sections.push(cur); continue; }
    if (!t) { if (cur && cur.lines.length) cur.lines.push(''); continue; }
    if (!cur) { cur = { label: 'Couplet', lines: [] }; out.sections.push(cur); }
    cur.lines.push(stripChordsInline(t));
  }
  out.sections = out.sections.flatMap((s) => {
    // une ligne vide au milieu d'une section SongSelect = nouvelle diapositive de la même section
    const parts = s.lines.join('\n').split(/\n\s*\n/).map((p) => p.trim()).filter(Boolean);
    return parts.map((p) => ({ label: s.label, text: p }));
  });
  return [makeSong(out)].filter((s) => s.sections.length);
}

// ============================================================ texte simple
function splitSections(text) {
  const blocks = clean(text).split(/\n\s*\n/).map((b) => b.trim()).filter(Boolean);
  const out = [];
  for (const b of blocks) {
    const lines = b.split('\n');
    let label = '';
    const first = lines[0].trim();
    let m = first.match(/^\[(.+?)\]\s*(.*)$/);
    if (m) {
      label = frLabel(m[1]);
      lines[0] = m[2];
    } else if ((m = first.match(/^(refrain|chorus|couplet|verse|pont|bridge|pré-?refrain|pre-?chorus|intro|fin|final|coda)(\s*\d+)?\s*[:.\-–]\s*(.*)$/i))) {
      label = frLabel(m[1] + (m[2] || ''));
      lines[0] = m[3];
    } else if (SS_HEAD.test(first) && lines.length > 1) {
      label = frLabel(first);
      lines.shift();
    }
    const body = tidy(lines.join('\n'));
    if (body) out.push({ label: label || 'Couplet', text: body });
  }
  return finishLabels(out);
}

function parsePlainText(name, text) {
  const lines = clean(text).split('\n');
  while (lines.length && !lines[0].trim()) lines.shift();
  let title = (lines[0] || '').trim();
  if (/^\[.+\]$/.test(title) || !title) title = baseName(name);
  else lines.shift();
  return [makeSong({ title, author: '', sections: splitSections(lines.join('\n')) })].filter((s) => s.sections.length);
}

// ============================================================ ProPresenter 6 (.pro6)
function b64decode(s) {
  const t = String(s).replace(/\s+/g, '');
  if (typeof atob === 'function') {
    const bin = atob(t);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
  }
  return new Uint8Array(Buffer.from(t, 'base64'));
}

// RTF → texte brut (suffisant pour les diapositives de ProPresenter)
function rtfToText(rtf) {
  let out = '';
  let i = 0;
  const skip = [];
  let depth = 0;
  let uc = 1;
  let skipping = 0;
  const n = rtf.length;
  const cp1252 = new TextDecoder('windows-1252');
  while (i < n) {
    const ch = rtf[i];
    if (ch === '{') { depth++; skip.push(skipping); i++; continue; }
    if (ch === '}') { depth--; skipping = skip.pop() || 0; i++; continue; }
    if (ch === '\\') {
      const next = rtf[i + 1];
      if (next === '\\' || next === '{' || next === '}') { if (!skipping) out += next; i += 2; continue; }
      if (next === "'") {
        const hex = rtf.substr(i + 2, 2);
        if (!skipping) out += cp1252.decode(new Uint8Array([parseInt(hex, 16)]));
        i += 4;
        continue;
      }
      if (next === '*') { skipping = 1; i += 2; continue; }
      const m = /^\\([a-zA-Z]+)(-?\d+)? ?/.exec(rtf.slice(i, i + 40));
      if (!m) { i += 2; continue; }
      const word = m[1];
      const arg = m[2] !== undefined ? parseInt(m[2], 10) : null;
      i += m[0].length;
      if (['fonttbl', 'colortbl', 'stylesheet', 'info', 'pict', 'expandedcolortbl', 'listtable', 'listoverridetable'].includes(word)) skipping = 1;
      else if (word === 'par' || word === 'line' || word === 'sect') { if (!skipping) out += '\n'; }
      else if (word === 'tab') { if (!skipping) out += ' '; }
      else if (word === 'uc') uc = arg ?? 1;
      else if (word === 'u' && arg !== null) {
        if (!skipping) out += String.fromCharCode(arg < 0 ? arg + 65536 : arg);
        // saute les caractères de remplacement
        let k = uc;
        while (k > 0 && i < n) {
          if (rtf[i] === '\\' && rtf[i + 1] === "'") i += 4;
          else if (rtf[i] === '{' || rtf[i] === '}') break;
          else i++;
          k--;
        }
      }
      continue;
    }
    if (ch === '\r' || ch === '\n') { i++; continue; }
    if (!skipping) out += ch;
    i++;
  }
  return out;
}

function parsePro6(name, text) {
  const doc = parseXml(text);
  const pres = find(doc, 'rvpresentationdocument');
  if (!pres) throw new Error('Ce fichier ne ressemble pas à une présentation ProPresenter 6.');
  const utf8 = new TextDecoder('utf-8');
  const sections = [];
  for (const g of findAll(pres, 'rvslidegrouping')) {
    const label = g.attrs.name || '';
    for (const s of findAll(g, 'rvdisplayslide')) {
      if (s.attrs.enabled === 'false') continue;
      const texts = [];
      for (const t of findAll(s, 'rvtextelement')) {
        const strings = findAll(t, 'nsstring');
        const plain = strings.find((x) => (x.attrs.rvxmlivarname || '').toLowerCase() === 'plaintext');
        const rtf = strings.find((x) => (x.attrs.rvxmlivarname || '').toLowerCase() === 'rtfdata');
        let val = '';
        if (plain && textOf(plain).trim()) val = utf8.decode(b64decode(textOf(plain)));
        else if (rtf && textOf(rtf).trim()) val = rtfToText(new TextDecoder('windows-1252').decode(b64decode(textOf(rtf))));
        else if (t.attrs.rtfdata) val = rtfToText(new TextDecoder('windows-1252').decode(b64decode(t.attrs.rtfdata)));
        if (val.trim()) texts.push(val);
      }
      const txt = texts.join('\n');
      if (txt.trim()) sections.push({ label, text: txt });
    }
  }
  const year = pres.attrs.cclicopyrightyear || pres.attrs.cclicopyrightinfo || '';
  const publisher = pres.attrs.cclipublisher || '';
  return [makeSong({
    title: pres.attrs.cclisongtitle || baseName(name),
    author: pres.attrs.ccliauthor || pres.attrs.artist || '',
    copyright: [year, publisher].filter(Boolean).join(' '),
    ccli: pres.attrs.cclisongnumber || '',
    sections,
  })].filter((s) => s.sections.length);
}

// ============================================================ détection et point d'entrée
function baseName(name) {
  return String(name || '').split(/[\\/]/).pop().replace(/\.[^.]+$/, '').trim() || 'Sans titre';
}

function detectFormat(name, text) {
  const n = String(name || '').toLowerCase();
  const t = clean(text).trimStart();
  if (n.endsWith('.pro6') || /<RVPresentationDocument/i.test(t.slice(0, 2000))) return 'propresenter';
  if (n.endsWith('.pro') && !/^\s*[{<]/.test(t)) return 'propresenter7';
  if (/^\{\s*$|^\{\s*"?[A-Za-z]/.test(t.slice(0, 200)) && /"?Songs"?\s*:|"?Verses"?\s*:/.test(t.slice(0, 5000))) return 'videopsalm';
  if (/\.(json|vpc|vpsong)$/.test(n)) return 'videopsalm';
  if (/openlyrics\.info\/namespace/i.test(t.slice(0, 3000))) return 'openlyrics';
  if (/^(<\?xml[^>]*>\s*)?(<!--[\s\S]*?-->\s*)*<song[\s>]/i.test(t)) return /<lyrics[\s>]/i.test(t) && !/<verse[\s>]/i.test(t) ? 'opensong' : 'openlyrics';
  if (/\.(cho|chordpro|chopro|crd)$/.test(n) || /^\s*\{\s*(title|t|start_of_chorus|soc)\s*[:}]/im.test(t)) return 'chordpro';
  if (/^(ccli song|ccli-liednummer|ccli song number|chant ccli)\s*#?/im.test(t) || /for use solely with the songselect/i.test(t)) return 'songselect';
  return 'texte';
}

function parseSongFile(name, text) {
  const fmt = detectFormat(name, text);
  let songs;
  switch (fmt) {
    case 'videopsalm': songs = parseVideoPsalm(text); break;
    case 'openlyrics': songs = parseOpenLyrics(text); break;
    case 'opensong': songs = parseOpenSong(text); break;
    case 'chordpro': songs = parseChordPro(text); break;
    case 'songselect': songs = parseSongSelect(text); break;
    case 'propresenter': songs = parsePro6(name, text); break;
    case 'propresenter7': throw new Error('Les fichiers ProPresenter 7 (.pro) ne sont pas lisibles : dans ProPresenter, exportez le chant en texte ou en ProPresenter 6.');
    default: songs = parsePlainText(name, text);
  }
  if (!songs.length) throw new Error('Aucun chant trouvé dans ce fichier.');
  return songs;
}

// Détecte l'encodage (UTF-8 avec ou sans BOM, UTF-16, sinon Windows-1252) puis analyse
function decodeBytes(bytes) {
  const b = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  if (b[0] === 0xEF && b[1] === 0xBB && b[2] === 0xBF) return new TextDecoder('utf-8').decode(b.subarray(3));
  if (b[0] === 0xFF && b[1] === 0xFE) return new TextDecoder('utf-16le').decode(b.subarray(2));
  if (b[0] === 0xFE && b[1] === 0xFF) return new TextDecoder('utf-16be').decode(b.subarray(2));
  // UTF-16 sans BOM : beaucoup d'octets nuls
  let zeros = 0;
  const sample = Math.min(b.length, 400);
  for (let i = 0; i < sample; i++) if (b[i] === 0) zeros++;
  if (sample > 20 && zeros > sample / 4) return new TextDecoder(b[0] === 0 ? 'utf-16be' : 'utf-16le').decode(b);
  try { return new TextDecoder('utf-8', { fatal: true }).decode(b); } catch {}
  return new TextDecoder('windows-1252').decode(b);
}

async function readSongFile(file) {
  const buf = new Uint8Array(await file.arrayBuffer());
  return parseSongFile(file.name || '', decodeBytes(buf));
}

window.Songs = { ACCEPT, frLabel, parseXml, splitSections, rtfToText, detectFormat, parseSongFile, decodeBytes, readSongFile };
})();
