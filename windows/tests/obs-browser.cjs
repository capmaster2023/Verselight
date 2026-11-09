// Render the actual OBS browser page in Edge, with a synthetic capture frame.
// No browser automation packages or external server are required.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const edge = process.argv[2], screenshot = path.resolve(process.argv[3]);
const root = path.resolve('windows/MyVerse/Ressources/obs');
const w = 1280, h = 720, bmp = Buffer.alloc(54 + w * h * 3);
bmp.write('BM'); bmp.writeUInt32LE(bmp.length, 2); bmp.writeUInt32LE(54, 10);
bmp.writeUInt32LE(40, 14); bmp.writeInt32LE(w, 18); bmp.writeInt32LE(h, 22);
bmp.writeUInt16LE(1, 26); bmp.writeUInt16LE(24, 28); bmp.writeUInt32LE(w * h * 3, 34);
for (let i = 54; i < bmp.length; i += 3) { bmp[i] = 40; bmp[i + 1] = 30; bmp[i + 2] = 200; }
const state = { mode: 'media', media: { type: 'camera', peripherique: 'test' }, lignes: [], ref: 'Video test', theme: 'nuit' };
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  res.setHeader('Cache-Control', 'no-store');
  if (url.pathname === '/etat.json') { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(state)); return; }
  if (url.pathname === '/display.jpg') { res.setHeader('Content-Type', 'image/bmp'); res.end(bmp); return; }
  const file = path.resolve(root, '.' + (url.pathname === '/' ? '/ecran.html' : url.pathname));
  if (!file.startsWith(root + path.sep)) { res.writeHead(403).end(); return; }
  try { res.end(fs.readFileSync(file)); } catch { res.writeHead(404).end(); }
});
server.listen(0, '127.0.0.1', () => {
  const profile = path.join(require('os').tmpdir(), 'mychurch-edge-' + process.pid);
  const child = spawn(edge, ['--headless', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--hide-scrollbars', '--user-data-dir=' + profile, '--window-size=1280,720', '--force-device-scale-factor=1', '--virtual-time-budget=3000', '--screenshot=' + screenshot, 'http://127.0.0.1:' + server.address().port + '/?obs=1'], { stdio: ['ignore', 'ignore', 'pipe'] });
  let log = ''; child.stderr.on('data', c => { log = (log + c).slice(-3000); });
  const timeout = setTimeout(() => { child.kill(); server.close(); console.error('OBS browser screenshot timed out. ' + log); process.exit(1); }, 60000);
  child.on('error', e => { clearTimeout(timeout); server.close(); console.error(e); process.exit(1); });
  child.on('exit', code => { clearTimeout(timeout); server.close(); if (code !== 0 || !fs.existsSync(screenshot)) { console.error(log); process.exit(1); } console.log('OBS browser screenshot: ' + screenshot); });
});
