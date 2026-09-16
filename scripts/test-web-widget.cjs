const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const html = fs.readFileSync(path.join(__dirname, '../src/WidgetPackage/Web/widget.html'), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];

async function run(language) {
  const listeners = {};
  const frames = [];
  let requests = 0, writes = 0, strokes = 0;
  let data = { limits: { rateLimits: { primary: { usedPercent: 41, resetsAt: 1800000000 } } },
    widgetUsageRanges: { '24h': { totalTokens: 1000000, buckets: [{ start: '2026-09-16T12:00:00Z', tokens: 1000000 }] } } };
  let ok = true;
  const context = new Proxy({}, { get(target, key) {
    return key in target ? target[key] : (...args) => { if (key === 'stroke') strokes++; };
  } });
  const elements = Object.fromEntries(['remaining','used','reset','total','chart','range','error'].map(id => [id, {
    textContent: '', hidden: false, value: '24h',
    options: ['7d','24h','8h','1h'].map(value => ({value})),
    setAttribute() {}, addEventListener(name, callback) { listeners[id + ':' + name] = callback; }
  }]));
  let width = 330;
  elements.chart.parentElement = { getBoundingClientRect: () => ({width, height: 166}) };
  elements.chart.getContext = () => context;
  const document = { hidden: false, documentElement: {}, getElementById: id => elements[id],
    querySelectorAll: () => [], addEventListener: (name, callback) => listeners[name] = callback };
  const sandbox = vm.createContext({
    document, navigator: {language}, Intl, Date, console, AbortController,
    localStorage: { getItem: () => null, setItem: () => writes++ },
    ResizeObserver: class { observe() {} },
    matchMedia: () => ({addEventListener() {}}),
    requestAnimationFrame: callback => frames.push(callback),
    setTimeout: () => 1, clearTimeout() {}, setInterval() {},
    devicePixelRatio: 2, getComputedStyle: () => ({getPropertyValue: () => '#303030'}),
    fetch: async () => { requests++; return {ok, status: ok ? 200 : 500, json: async () => data}; }
  });
  vm.runInContext(script, sandbox);
  const settle = async () => { for (let i=0;i<8;i++) await Promise.resolve(); while(frames.length) frames.shift()(); };
  await settle();
  assert.equal(elements.remaining.textContent, '59%');
  assert.equal(document.documentElement.lang, language.startsWith('pl') ? 'pl' : 'en');
  assert.equal(elements.total.textContent, language.startsWith('pl') ? '1 mln tokenów' : '1 M tokens');
  assert.equal(writes, 0, 'redrawing must not write preferences');
  assert.ok(strokes > 0);
  const before = requests;
  await vm.runInContext('Promise.all([load(), load(), load()])', sandbox);
  assert.equal(requests, before + 1, 'overlapping refreshes are coalesced');
  await settle();
  document.hidden = true;
  await vm.runInContext('load()', sandbox);
  assert.equal(requests, before + 1, 'hidden widgets do not poll');
  document.hidden = false;
  elements.range.value = '1h';
  listeners['range:change']();
  await settle();
  assert.equal(writes, 1);
  assert.equal(elements.total.textContent, language.startsWith('pl') ? 'brak danych' : 'no data');
  width = 0;
  const previousStrokes = strokes;
  vm.runInContext('draw()', sandbox);
  assert.equal(strokes, previousStrokes);
  data = {};
  await vm.runInContext('load()', sandbox);
  assert.equal(elements.remaining.textContent, '—', 'missing usage is not 100 percent remaining');
  ok = false;
  await vm.runInContext('load()', sandbox);
  assert.equal(elements.error.hidden, false);
}
(async () => {
  for (const language of ['pl-PL', 'en-US', 'de-DE']) await run(language);
  console.log('PASS: PL/EN fallback, chart, missing data, preferences, concurrent refresh, visibility, HTTP failure');
})().catch(error => { console.error(error); process.exitCode = 1; });
