// Headless checks of the map renderer's logic (no canvas needed): runs the real wwwroot/js/map.js against the real generated geometry.
// usage: node maptest.js [repoRoot]
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const root = process.argv[2] || path.resolve(__dirname, '..', '..');
const dataCs = fs.readFileSync(path.join(root, 'src/EmpireSim.Core/Models/WorldGeometry.Data.cs'), 'utf8');
const gameState = fs.readFileSync(path.join(root, 'src/EmpireSim.Core/Models/GameState.cs'), 'utf8');
let failures = 0;
const check = (ok, name) => { console.log((ok ? '  PASS ' : '  FAIL ') + name); if (!ok) failures++; };

// ---- load the renderer exactly as the page does
const sandbox = { window: {}, document: {}, console };
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(root, 'src/EmpireSim/wwwroot/js/map.js'), 'utf8'), sandbox);
const map = sandbox.window.empireMap;

// ---- the polygons, parsed from the generated data
function dict(name) {
  const a = dataCs.indexOf(`${name} = new()`); const b = dataCs.indexOf('};', a); const body = dataCs.slice(a, b);
  const out = {}; for (const m of body.matchAll(/\["([^"]+)"\] = "([^"]*)"/g)) out[m[1]] = m[2].length ? m[2].split(';').map(r => r.split(' ').map(p => { const [x, y] = p.split(',').map(Number); return { x, y }; })) : [];
  return out;
}
const Nations = dict('Nations'), Neutrals = dict('Neutrals'), Frontiers = dict('Frontiers');
const colors = {}; for (const m of gameState.matchAll(/Id = "([a-z]+)", Name = "[^"]+", ColorHex = "(#[0-9A-Fa-f]{6})"/g)) colors[m[1]] = m[2];
const caps = {}; for (const m of gameState.matchAll(/Id = "([a-z]+)"[\s\S]*?MapX = ([\d.]+), MapY = ([\d.]+)/g)) if (!caps[m[1]]) caps[m[1]] = [parseFloat(m[2]), parseFloat(m[3])];
const labelMatches = [...dataCs.matchAll(/\["([^"]+)"\] = \((\d+), (\d+)\),/g)];

function worldShapes(merge) {
  const shapes = [];
  for (const [n, p] of Object.entries(Neutrals)) shapes.push({ id: 'n:' + n, name: n, kind: 'neutral', color: '#4A4438', polygons: p });
  for (const [n, p] of Object.entries(Nations)) shapes.push({ id: n, name: n, kind: 'nation', color: colors[n], polygons: p, labelX: caps[n][0], labelY: caps[n][1] });
  for (const [n, p] of Object.entries(Frontiers)) shapes.push({ id: 'f:' + n, name: n, kind: 'frontier', color: '#6B7280', polygons: p, painted: true });
  if (merge) {   // an annexation: the loser's polygons join the winner's list, the loser has none (exactly what Warfare.AnnexNation does)
    const [winner, loser] = merge; const w = shapes.find(s => s.id === winner), l = shapes.find(s => s.id === loser);
    w.polygons = w.polygons.concat(l.polygons); l.polygons = [];
  }
  return shapes;
}
function inspect(shapes, selectedId) { map.render({ shapes, selectedId }); return map.inspect(); }

// ---- 1. a world of independent countries
console.log('== map renderer: edge classification on the real geometry ==');
const world = inspect(worldShapes(null));
const sum = k => world.reduce((s, x) => s + x[k], 0);
const edgesTotal = Object.values({ ...Nations, ...Neutrals, ...Frontiers }).reduce((s, rings) => s + rings.reduce((t, r) => t + r.length - 1, 0), 0);
check(world.length === 41 + 13 + 3 && world.every(s => s.rings > 0), 'all 57 shapes (41 countries, 13 neutral, 3 frontier) are loaded with their polygons');
const sharedPairs = sum('border');   // each shared edge is drawn by ONE of its two realms
check(sharedPairs > 3500 && sharedPairs < 4200, `each border segment between two realms is drawn once, not twice (${sharedPairs} segments)`);
check(sum('inner') === 0, 'no lines inside a realm in a world of independent countries');
const bridgeCount = edgesTotal - (sum('coast') + 2 * sharedPairs);   // every edge is a coast, one half of a shared border (two uses), or part of a hidden bridge (two uses in one ring)
check(bridgeCount >= 0 && bridgeCount % 2 === 0 && bridgeCount / 2 >= 1 && bridgeCount / 2 <= 30, `bridges to enclaves and lakes are hidden, not drawn (${bridgeCount / 2} of them)`);
check(world.every(s => s.outer >= s.coast) && sum('outer') === sum('coast') + 2 * sharedPairs, 'a realm\'s selection outline is its coasts plus its borders (each border is the outline of both neighbours)');

// ---- 2. an annexation: Persia joins the Ottoman realm
const annexed = inspect(worldShapes(['ottoman', 'persia']));
const ott = annexed.find(s => s.id === 'ottoman'), ottBefore = world.find(s => s.id === 'ottoman'), per = annexed.find(s => s.id === 'persia'), perBefore = world.find(s => s.id === 'persia');
check(per.rings === 0 && per.border === 0 && per.coast === 0 && per.inner === 0, 'the annexed country draws nothing of its own any more');
check(ott.rings === ottBefore.rings + perBefore.rings && ott.inner > 0, `the master\'s realm has both lands, and the line where they meet is one faint inner line (${ott.inner} segments)`);
const sharedBefore = world.reduce((s, x) => s + x.border, 0), sharedAfter = annexed.reduce((s, x) => s + x.border, 0);
check(sharedAfter < sharedBefore && sharedBefore - sharedAfter >= ott.inner, `the border between the two is no longer a border (drawn borders ${sharedBefore} -> ${sharedAfter}); nothing is drawn twice`);
check(ott.coast === ottBefore.coast + perBefore.coast, 'coasts are unchanged by an annexation');
check(ott.outer < ottBefore.outer + perBefore.outer, 'the selected merged realm is outlined without the line between its parts');

// ---- 3. hit-testing: a tap on a capital finds the country (and, after annexation, the master)
map.render({ shapes: worldShapes(null) });
const hits = Object.entries(caps).filter(([id]) => Nations[id]).map(([id, [x, y]]) => [id, map.shapeAtWorld(x, y)]);
check(hits.length === 41 && hits.every(([id, hit]) => hit === id), 'tapping each of the 41 capitals selects its own country');
map.render({ shapes: worldShapes(['ottoman', 'persia']) });
check(map.shapeAtWorld(caps.persia[0], caps.persia[1]) === 'ottoman', 'after the annexation a tap on Persia\'s lands selects its new master');
map.render({ shapes: worldShapes(null) });
check(map.shapeAtWorld(1950, 300) === null && map.shapeAtWorld(600, 340) === null, 'open sea and the Black Sea select nothing (no polygon covers water)');
const tibet = Neutrals.Tibet; const tLab = labelMatches.find(m => m[1] === 'Tibet');
check(map.shapeAtWorld(+tLab[2], +tLab[3]) === 'n:Tibet', 'a tap on the Tibetan plateau selects Tibet, not Ming');

// ---- 4. palette: muted, mid-tone, still distinguishable between neighbours
function rgb(h) { return [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16)); }
function hsl(r, g, b) { r /= 255; g /= 255; b /= 255; const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2; let s = 0; if (mx !== mn) { const d = mx - mn; s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn); } return [s, l]; }
{
  const all = Object.keys(Nations).map(id => [id, map.mutedRgb(colors[id])]);
  check(all.every(([, m]) => { const [s, l] = hsl(...m); return s <= 0.52 && l >= 0.33 && l <= 0.69; }), 'every country colour is restrained: saturation at most half, lightness in the middle band (no glaring or black realms)');
}
// neighbouring countries can be told apart: after the renderer's nudging, no two bordering countries are close in colour
{
  map.render({ shapes: worldShapes(null) });
  const col = {}; for (const c of map.shapeColors()) col[c.id] = c.rgb;
  const key = (p, q) => (p.x < q.x || (p.x === q.x && p.y <= q.y)) ? p.x + ',' + p.y + '|' + q.x + ',' + q.y : q.x + ',' + q.y + '|' + p.x + ',' + p.y;
  const use = {};
  for (const [id, rings] of Object.entries(Nations)) for (const r of rings) for (let i = 0; i < r.length - 1; i++) (use[key(r[i], r[i + 1])] = use[key(r[i], r[i + 1])] || new Set()).add(id);
  const pairs = new Set(); for (const u of Object.values(use)) if (u.size === 2) pairs.add([...u].sort().join('|'));
  const lin = v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
  const lab = c => { const [r, g, b] = c.map(lin); const x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * b, z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883; const f = t => t > 0.008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116; return [116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z))]; };
  const dist = (p, q) => { const A = lab(col[p]), B = lab(col[q]); return Math.hypot(A[0] - B[0], A[1] - B[1], A[2] - B[2]); };
  const list = [...pairs].map(k => { const [p, q] = k.split('|'); return { k, d: dist(p, q) }; }).sort((x, y) => x.d - y.d);
  console.log('  closest neighbours after separation:', list.slice(0, 6).map(x => x.k + ' ' + x.d.toFixed(1)).join(' · '));
  check(list.length > 55 && list[0].d >= 20, `no two bordering countries are close in colour (${list.length} neighbouring pairs; the closest differ by ${list[0].d.toFixed(1)} in Lab units, was as little as 7)`);
  const hues = id => { const [r, g, b] = col[id]; return [r, g, b]; };
  check(Object.values(col).every(c => { const mx = Math.max(...c), mn = Math.min(...c); const l = (mx + mn) / 510; return l >= 0.28 && l <= 0.75; }), 'colours stay in the mid-tone band after separation');
}
console.log(failures ? `\n${failures} CHECK(S) FAILED` : '\nMAP RENDERER CHECKS PASSED');
process.exit(failures ? 1 : 0);
