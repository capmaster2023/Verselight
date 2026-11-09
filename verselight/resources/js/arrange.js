// MyVerse — rangement automatique d'un chant collé brut :
// titre, refrain (marqué ou répété), couplets numérotés, accords retirés,
// lignes trop longues coupées, diapositives équilibrées de 3 à 5 lignes.
(function () {
  const norm = (s) => (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, ' ').trim();

  // ligne d'accords seule : « C  G  Am  F », « Do Ré Mim Sol », « | C / G | »
  const ACCORD = /^(?:[A-G](?:#|b)?|(?:do|ré|re|mi|fa|sol|la|si)(?:#|b)?)(?:m|min|maj|dim|aug|sus|add|M)?\d{0,2}(?:sus\d|add\d|maj\d)?(?:\/[A-G](?:#|b)?)?$/i;
  function ligneAccords(l) {
    const t = l.replace(/[|/()\-.]/g, ' ').trim().split(/\s+/).filter(Boolean);
    return t.length > 0 && t.every((x) => ACCORD.test(x)) && !/^(la|si|do|mi|sol|fa|re|ré)$/i.test(l.trim());
  }

  // en-têtes de strophe : « Refrain », « R/ », « Chœur : », « Couplet 2 », « 2. », « Pont »…
  const ENTETE = /^\s*(?:\[\s*)?(refrain|ref|r\/|r\.|chœur|choeur|chorus|couplet|strophe|verse|verset|pont|bridge|pré-?refrain|pre-?chorus|intro|fin|final|coda)\s*(\d+)?\s*(?:\])?\s*[:.\-–)]?\s*(.*)$/i;
  const NUMERO = /^\s*(\d{1,2})\s*[.)\-–]\s+(.+)$/;
  function nomEntete(mot, num) {
    const m = mot.toLowerCase();
    if (/^(refrain|ref|r\/|r\.|chœur|choeur|chorus)$/.test(m)) return 'Refrain';
    if (/^(pont|bridge)$/.test(m)) return 'Pont';
    if (/^(pré-?refrain|pre-?chorus)$/.test(m)) return 'Pré-refrain';
    if (/^(intro)$/.test(m)) return 'Intro';
    if (/^(fin|final|coda)$/.test(m)) return 'Fin';
    return 'Couplet' + (num ? ' ' + num : '');
  }

  // coupe une ligne trop longue en deux, à la virgule (ou à l'espace) la plus proche du milieu
  function couperLigne(l, max) {
    if (l.length <= max) return [l];
    const mil = l.length / 2;
    let best = -1;
    for (let i = 0; i < l.length; i++) {
      if ((l[i] === ',' || l[i] === ';' || l[i] === ':') && l[i + 1] === ' ' && (best < 0 || Math.abs(i - mil) < Math.abs(best - mil))) best = i + 1;
    }
    if (best < 0 || Math.abs(best - mil) > l.length * 0.3) {
      best = -1;
      for (let i = 0; i < l.length; i++) if (l[i] === ' ' && (best < 0 || Math.abs(i - mil) < Math.abs(best - mil))) best = i;
    }
    if (best <= 0) return [l];
    return [...couperLigne(l.slice(0, best).trim(), max), ...couperLigne(l.slice(best).trim(), max)];
  }

  // découpe équilibrée : n lignes -> morceaux de 3 à 5 lignes (auto) ou de « max » lignes au plus
  function tailles(n, max) {
    const k = Math.max(1, Math.ceil(n / max));
    const base = Math.floor(n / k), reste = n % k;
    return Array.from({ length: k }, (_, i) => base + (i < reste ? 1 : 0));
  }

  /**
   * Range un chant brut.
   * @param {string} brut texte collé
   * @param {{max?: number|'auto', titre?: string, longueur?: number}} o
   * @returns {{title: string, sections: {label: string, text: string}[]}}
   */
  function ranger(brut, o = {}) {
    const max = o.max && o.max !== 'auto' ? Math.max(2, Math.min(8, +o.max)) : 5;
    const longueur = o.longueur || 64;
    let texte = String(brut || '')
      .replace(/\r\n?/g, '\n').replace(/[​-‍﻿]/g, '').replace(/\t/g, ' ')
      .replace(/\[[A-G](?:#|b)?[^\]\n]{0,6}\]/g, '') // accords ChordPro dans les paroles
      .replace(/ /g, ' ');
    let lignes = texte.split('\n').map((l) => l.replace(/\s+$/g, '').replace(/^\s+/, ''));
    lignes = lignes.filter((l) => !ligneAccords(l) || !l.trim());

    // titre : première ligne suivie d'une ligne vide (ou titre fourni)
    let title = (o.titre || '').trim();
    while (lignes.length && !lignes[0].trim()) lignes.shift();
    if (!title && lignes.length > 2 && lignes[0].length <= 70 && !lignes[1].trim() && !ENTETE.test(lignes[0]) && !NUMERO.test(lignes[0])) {
      title = lignes.shift().replace(/^[#*\s]+|[*\s]+$/g, '');
    }

    // blocs : lignes vides et en-têtes
    const blocs = [];
    let cur = null;
    const nouveau = (label) => { cur = { label, lignes: [] }; blocs.push(cur); };
    for (const l of lignes) {
      if (!l.trim()) { cur = null; continue; }
      let m = l.match(ENTETE);
      if (m && (m[3] === '' || l.length < 40 || /^\s*\[/.test(l) || /[:.)]/.test(l.slice(0, (m[1] || '').length + 4)))) {
        nouveau(nomEntete(m[1], m[2]));
        if (m[3] && m[3].trim()) cur.lignes.push(m[3].trim());
        continue;
      }
      m = l.match(NUMERO);
      if (m && (!cur || cur.lignes.length > 0)) {
        nouveau('Couplet ' + m[1]);
        cur.lignes.push(m[2].trim());
        continue;
      }
      if (!cur) nouveau('');
      cur.lignes.push(l.replace(/\s{2,}/g, ' '));
    }
    const pleins = blocs.filter((b) => b.lignes.length);

    // refrain répété sans nom : un bloc identique à un autre est le refrain
    const cles = pleins.map((b) => norm(b.lignes.join(' ')));
    const refrain = new Set();
    cles.forEach((k, i) => { if (k && cles.indexOf(k) !== i) refrain.add(k); });
    const refrainCle = pleins.find((b) => b.label === 'Refrain');
    if (refrainCle) refrain.add(norm(refrainCle.lignes.join(' ')));
    // « Refrain » seul sans paroles (reprise) : on recopie le premier refrain
    const premierRefrain = pleins.find((b) => b.label === 'Refrain' || refrain.has(norm(b.lignes.join(' '))));
    for (const b of blocs) if (!b.lignes.length && b.label === 'Refrain' && premierRefrain) b.lignes = premierRefrain.lignes.slice();

    // noms des strophes
    let n = 0;
    const nommes = blocs.filter((b) => b.lignes.length).map((b) => {
      let label = b.label;
      if (refrain.has(norm(b.lignes.join(' ')))) label = 'Refrain';
      if (!label) label = 'Couplet ' + ++n;
      else if (/^Couplet \d+$/.test(label)) n = Math.max(n, +label.split(' ')[1]);
      else if (label === 'Couplet') label = 'Couplet ' + ++n;
      return { label, lignes: b.lignes.flatMap((l) => couperLigne(l, longueur)) };
    });

    // diapositives de 3 à 5 lignes
    const sections = [];
    for (const b of nommes) {
      const t = tailles(b.lignes.length, max);
      let i = 0;
      t.forEach((k, j) => {
        sections.push({ label: t.length > 1 ? `${b.label} (${j + 1}/${t.length})` : b.label, text: b.lignes.slice(i, i + k).join('\n') });
        i += k;
      });
    }
    return { title, sections };
  }

  // forme texte de l'éditeur : [Nom] + paroles, une ligne vide entre les diapositives
  const versTexte = (sections) => sections.map((s) => `[${s.label}]\n${s.text}`).join('\n\n');

  window.Arrange = { ranger, versTexte, couperLigne, tailles, ligneAccords };
})();
