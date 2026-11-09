// MyVerse — journal d'erreurs : erreurs JavaScript et événements importants.
// Visible dans Outils › Journal d'erreurs ; copie dans %APPDATA%\VerseLight\journal-myverse.txt
// (lisible même si la fenêtre de MyVerse ne s'affiche pas).
(function () {
  const CLE = 'myverse-journal';
  const MAX = 300;
  let lignes = [];
  try { lignes = JSON.parse(localStorage.getItem(CLE) || '[]'); } catch (e) { lignes = []; }
  if (!Array.isArray(lignes)) lignes = [];

  const heure = () => { const d = new Date(); return d.toLocaleDateString('fr-CA') + ' ' + d.toLocaleTimeString('fr-CA'); };
  let minuterie = null;
  function sauver() {
    clearTimeout(minuterie);
    minuterie = setTimeout(async () => {
      try { localStorage.setItem(CLE, JSON.stringify(lignes)); } catch (e) {}
      try {
        if (window.Neutralino && typeof window.NL_PORT !== 'undefined') {
          const appdata = await Neutralino.os.getEnv('APPDATA');
          if (appdata) await Neutralino.filesystem.writeFile(appdata + '\\VerseLight\\journal-myverse.txt', lignes.join('\r\n') + '\r\n');
        }
      } catch (e) {}
    }, 800);
  }
  function ajouter(type, msg) {
    lignes.push(`[${heure()}] ${type} ${String(msg).replace(/\s+/g, ' ').slice(0, 800)}`);
    if (lignes.length > MAX) lignes = lignes.slice(-MAX);
    sauver();
  }
  window.addEventListener('error', (e) => {
    ajouter('ERREUR', `${e.message || e} (${String(e.filename || '').split('/').pop()}:${e.lineno || 0})`);
  });
  window.addEventListener('unhandledrejection', (e) => {
    const r = e.reason;
    ajouter('ERREUR', 'promesse rejetée : ' + (r && typeof r === 'object' ? [r.code, r.message].filter(Boolean).join(' ') || JSON.stringify(r) : String(r)));
  });
  window.VLJournal = {
    info: (m) => ajouter('INFO', m),
    erreur: (m) => ajouter('ERREUR', m),
    lignes: () => lignes.slice(),
    vider: () => { lignes = []; sauver(); },
  };
  ajouter('INFO', '— démarrage de MyVerse ' + (window.NL_APPVERSION || '') + ' —');
})();
