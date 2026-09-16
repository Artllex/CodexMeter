const fs = require('fs');
const http = require('http');
const path = require('path');
const { spawn } = require('child_process');

const root = path.resolve(__dirname, '..');
const edge = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const output = path.join(root, 'artifacts', 'store-assets');
fs.mkdirSync(output, { recursive: true });

let html = fs.readFileSync(path.join(root, 'src', 'WidgetPackage', 'Web', 'widget.html'), 'utf8');
html = html
  .replace("const language=(navigator.languages?.[0]||navigator.language||'en').toLowerCase().startsWith('pl')?'pl':'en';",
    "const language=(new URLSearchParams(location.search).get('lang')||((navigator.languages?.[0]||navigator.language||'en').toLowerCase().startsWith('pl')?'pl':'en'));" )
  .replace('https://codexmeter.local/data.json', '/data.json');

const now = Date.now();
const buckets = Array.from({ length: 24 }, (_, index) => ({
  start: new Date(now - (23 - index) * 3600000).toISOString(),
  tokens: [30000, 100000, 80000, 170000, 110000, 60000][index % 6]
}));
const data = JSON.stringify({
  limits: { rateLimitsByLimitId: { codex: { primary: { usedPercent: 32, resetsAt: Math.floor((now + 14 * 3600000) / 1000) } } } },
  widgetUsageRanges: { '24h': { buckets, totalTokens: buckets.reduce((sum, row) => sum + row.tokens, 0) } }
});

const server = http.createServer((request, response) => {
  if (request.url.startsWith('/data.json')) {
    response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
    response.end(data);
  } else {
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    response.end(html);
  }
});

function capture(port, language) {
  return new Promise((resolve, reject) => {
    const target = path.join(output, `CodexMeter-${language.toUpperCase()}-1600x2200.png`);
    const profile = path.join(output, `edge-${language}`);
    const child = spawn(edge, [
      '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-dark-mode', '--force-device-scale-factor=2',
      `--user-data-dir=${profile}`, '--window-size=800,1100',
      `--screenshot=${target}`, `http://127.0.0.1:${port}/?lang=${language}`
    ], { stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve(target) : reject(new Error(`Edge exited with ${code}`)));
  });
}

server.listen(0, '127.0.0.1', async () => {
  try {
    const port = server.address().port;
    for (const language of ['pl', 'en']) console.log(await capture(port, language));
    server.close();
  } catch (error) {
    console.error(error);
    server.close(() => process.exit(1));
  }
});
