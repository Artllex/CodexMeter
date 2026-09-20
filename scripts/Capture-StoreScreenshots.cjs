const fs = require('fs');
const http = require('http');
const path = require('path');
const { spawn } = require('child_process');

const root = path.resolve(__dirname, '..');
const browserExecutable = fs.existsSync('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe')
  ? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
  : 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const output = path.join(root, 'artifacts', 'store-assets');
fs.mkdirSync(output, { recursive: true });

let html = fs.readFileSync(path.join(root, 'src', 'WidgetPackage', 'Web', 'widget.html'), 'utf8');
html = html
  .replace("const language=(navigator.languages?.[0]||navigator.language||'en').toLowerCase().startsWith('pl')?'pl':'en';",
    "const language=(new URLSearchParams(location.search).get('lang')||((navigator.languages?.[0]||navigator.language||'en').toLowerCase().startsWith('pl')?'pl':'en'));" )
  .replace('https://codexmeter.local/data.json', '/data.json');

// The widget host draws the title row outside the WebView. Recreate that row
// for Store artwork so the screenshot matches what customers actually see.
html = html
  .replace('</style>', `
  .store-header { position:absolute; inset:16px 18px auto; z-index:2; display:flex; align-items:center; height:24px; color:var(--fg); }
  .store-header img { width:19px; height:19px; margin-right:9px; object-fit:contain; }
  .store-header strong { font-size:15px; line-height:1; font-weight:500; }
  .store-header span { margin-left:auto; font-size:21px; line-height:1; letter-spacing:2px; transform:translateY(-3px); }
  </style>`)
  .replace('<main class="widget-surface">', `<main class="widget-surface">
  <header class="store-header"><img src="/icon.png" alt=""><strong>Codex Meter</strong><span aria-hidden="true">•••</span></header>`);

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
  } else if (request.url.startsWith('/icon.png')) {
    response.writeHead(200, { 'Content-Type': 'image/png', 'Cache-Control': 'no-store' });
    response.end(fs.readFileSync(path.join(root, 'src', 'WidgetPackage', 'ProviderAssets', 'CodexMeter_Icon.png')));
  } else {
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    response.end(html);
  }
});

function capture(port, language) {
  return new Promise((resolve, reject) => {
    const target = path.join(output, `CodexMeter-${language.toUpperCase()}-1600x2200.png`);
    const profile = path.join(output, `edge-${language}-${process.pid}`);
    const child = spawn(browserExecutable, [
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
