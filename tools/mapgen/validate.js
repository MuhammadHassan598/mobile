// Structural validation of geometry.json. usage: node validate.js [geometry.json]
'use strict';
const fs = require('fs');
const L = require('./lib');
const P = require('./paths');
const { W, H } = L;
const file = process.argv[2] || P.out('geometry.json');
const g = JSON.parse(fs.readFileSync(file, 'utf8'));
const caps = JSON.parse(fs.readFileSync(file.replace('.json', '.capitals.json'), 'utf8'));
const land = P.mask();
const world = P.world();
let bad = 0;
const fail = (...a) => { bad++; console.log('FAIL', ...a); };
const ok = (...a) => console.log('ok  ', ...a);

// ---- 1. shared edges: every undirected edge is used by at most two rings, and by two only when they belong to different owners
const key = (a, b) => (a[0] < b[0] || (a[0] === b[0] && a[1] <= b[1])) ? `${a[0]},${a[1]}|${b[0]},${b[1]}` : `${b[0]},${b[1]}|${a[0]},${a[1]}`;
const edgeUse = new Map(); let ringCount = 0, vertCount = 0;
for (const o of g.owners) for (const [ri, ring] of (g.geometry[o.id] || []).entries()) {
  ringCount++; vertCount += ring.length;
  for (let i = 0; i < ring.length; i++) {
    const a = ring[i], b = ring[(i + 1) % ring.length]; if (a[0] === b[0] && a[1] === b[1]) continue;
    const k = key(a, b); let u = edgeUse.get(k); if (!u) { u = []; edgeUse.set(k, u); } u.push(o.id);
  }
}
let shared = 0, single = 0, triple = 0, samePair = 0;
for (const [k, u] of edgeUse) { if (u.length === 1) single++; else if (u.length === 2) { if (u[0] === u[1]) samePair++; else shared++; } else triple++; }
(triple === 0 ? ok : fail)(`edges used by 3+ rings: ${triple}`);
ok(`${shared} shared border edges are identical vertex-for-vertex on both neighbours; ${single} coast/frame edges; ${samePair} same-owner touching edges`);

// ---- 2. rasterise the polygons independently and compare with the label raster
const lab = new Int16Array(fs.readFileSync(P.out('labels.bin')).buffer.slice(0));
const ownersList = JSON.parse(fs.readFileSync(P.out('labels.owners.json'), 'utf8'));
const idxOf = new Map(ownersList.map((id, i) => [id, i + 1]));
const cover = new Int16Array(W * H), covers = new Uint8Array(W * H);
for (const o of g.owners) {
  const v = idxOf.get(o.id); const mine = new Uint8Array(W * H);
  for (const ring of g.geometry[o.id] || []) L.fillPoly(mine, ring, 1, null);
  for (let i = 0; i < mine.length; i++) if (mine[i]) { covers[i]++; cover[i] = v; }
}
let overlap = 0, hole = 0, wrongOwner = 0, polyLand = 0, labLand = 0;
for (let i = 0; i < W * H; i++) {
  if (covers[i] > 1) overlap++;
  if (lab[i]) labLand++;
  if (lab[i] && !covers[i]) hole++;
  if (lab[i] && covers[i] === 1 && cover[i] !== lab[i]) wrongOwner++;
  if (covers[i]) polyLand++;
}
const pct = (n, d) => (100 * n / d).toFixed(3) + '%';
(overlap === 0 ? ok : fail)(`pixels covered by two owners' polygons: ${overlap}`);
(hole / labLand < 0.005 ? ok : fail)(`land pixels not covered by any polygon: ${hole} (${pct(hole, labLand)} of ${labLand})`);
(wrongOwner / labLand < 0.012 ? ok : fail)(`pixels whose polygon owner differs from the intended owner: ${wrongOwner} (${pct(wrongOwner, labLand)})`);

// ---- 3. self-intersections within a ring (segment pairs), skipping adjacent segments
function segInt(a, b, c, d) {
  const o = (p, q, r) => (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0]);
  const o1 = o(a, b, c), o2 = o(a, b, d), o3 = o(c, d, a), o4 = o(c, d, b);
  return ((o1 > 0) !== (o2 > 0)) && ((o3 > 0) !== (o4 > 0)) && o1 * o2 < 0 && o3 * o4 < 0;
}
let selfX = 0; const selfList = [];
for (const o of g.owners) for (const ring of g.geometry[o.id] || []) {
  const n = ring.length; if (n < 4) continue;
  // bucket segments on a coarse grid to keep it fast
  const cell = 24, grid = new Map();
  for (let i = 0; i < n; i++) { const a = ring[i], b = ring[(i + 1) % n]; for (let gx = Math.floor(Math.min(a[0], b[0]) / cell); gx <= Math.floor(Math.max(a[0], b[0]) / cell); gx++) for (let gy = Math.floor(Math.min(a[1], b[1]) / cell); gy <= Math.floor(Math.max(a[1], b[1]) / cell); gy++) { const k = gx * 100003 + gy; let l = grid.get(k); if (!l) { l = []; grid.set(k, l); } l.push(i); } }
  const seen = new Set();
  for (const l of grid.values()) for (let x = 0; x < l.length; x++) for (let y = x + 1; y < l.length; y++) {
    const i = l[x], j = l[y]; if (Math.abs(i - j) <= 1 || (i === 0 && j === n - 1) || (j === 0 && i === n - 1)) continue;
    const pk = i * 1e6 + j; if (seen.has(pk)) continue; seen.add(pk);
    if (segInt(ring[i], ring[(i + 1) % n], ring[j], ring[(j + 1) % n])) { selfX++; if (selfList.length < 6) selfList.push(o.id + '@' + ring[i]); }
  }
}
(selfX === 0 ? ok : fail)(`self-intersecting ring segments: ${selfX} ${selfList.join(' ')}`);

// ---- 4. capitals inside their own realm, on land (or on the coast)
function inRing(x, y, p) { let c = false; for (let i = 0, j = p.length - 1; i < p.length; j = i++) { const [xi, yi] = p[i], [xj, yj] = p[j]; if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c; } return c; }
let capBad = 0;
for (const n of world.nations) {
  const c = caps[n.id]; if (!c) { fail('no capital for', n.id); continue; }
  const inside = (g.geometry[n.id] || []).some(r => inRing(c.x, c.y, r));
  if (!inside) { capBad++; fail(`capital of ${n.id} (${c.name} ${c.x},${c.y}) is outside its own territory`); }
}
if (!capBad) ok(`all ${world.nations.length} capitals are inside their own territory`);

// ---- 5. every owner has land; tiny polygons
let tiny = 0, empty = [];
for (const o of g.owners) { const rs = g.geometry[o.id] || []; if (!rs.length) empty.push(o.id); for (const r of rs) if (Math.abs(L.polyArea(r)) < 6) tiny++; }
const dissolved = empty.filter(id => id.startsWith('n:')); const missing = empty.filter(id => !id.startsWith('n:'));
(missing.length === 0 ? ok : fail)(`countries and colony regions without any polygon: ${missing.join(', ') || 'none'}`);
if (dissolved.length) ok(`neutral territories fully carved into colony regions (dissolved): ${dissolved.join(', ')}`);
ok(`${ringCount} polygons, ${vertCount} vertices in total; polygons under 6 px²: ${tiny}`);
console.log(bad ? `\n${bad} validation failure(s)` : '\nGEOMETRY VALID');
process.exit(bad ? 1 : 0);
