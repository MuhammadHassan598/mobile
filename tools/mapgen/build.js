// Builds the planar partition of the world map and writes out/geometry.json
// usage: node build.js --overrides regions/all.json   (writes out/geometry.json and its companions)
'use strict';
const fs = require('fs');
const L = require('./lib');
const P = require('./paths');
const { W, H } = L;
const args = process.argv.slice(2);
const outFile = (args.indexOf('--out') >= 0 ? args[args.indexOf('--out') + 1] : P.out('geometry.json'));
const overridesFile = args.indexOf('--overrides') >= 0 ? args[args.indexOf('--overrides') + 1] : null;
const log = (...a) => console.log(...a);

const world = P.world();
const rawMask = P.mask();

// ---------------------------------------------------------------- 1. land mask clean-up
function cleanMask(m) {
  let g = Uint8Array.from(m);
  // 3x3 majority to remove one-pixel jaggies
  for (let pass = 0; pass < 1; pass++) {
    const o = Uint8Array.from(g);   // (the one-pixel frame keeps its values: land that runs off the picture stays land to the edge)
    for (let y = 1; y < H - 1; y++) for (let x = 1; x < W - 1; x++) {
      const i = y * W + x; const s = g[i - W - 1] + g[i - W] + g[i - W + 1] + g[i - 1] + g[i] + g[i + 1] + g[i + W - 1] + g[i + W] + g[i + W + 1];
      o[i] = s >= 5 ? 1 : 0;
    }
    g = o;
  }
  // small enclosed sea specks -> land ; small land specks -> sea
  let c = L.components(g, i => g[i] === 0);
  for (let i = 0; i < g.length; i++) if (g[i] === 0 && c.sizes[c.comp[i]] < 120) g[i] = 1;
  c = L.components(g, i => g[i] === 1);
  for (let i = 0; i < g.length; i++) if (g[i] === 1 && c.sizes[c.comp[i]] < 45) g[i] = 0;
  return g;
}
const land = cleanMask(rawMask);
let landPx = 0; for (let i = 0; i < land.length; i++) landPx += land[i];
log('land pixels after clean-up:', landPx);

// ---------------------------------------------------------------- 2. owners
const owners = [];   // {id, kind, name, color, polys, free}
for (const n of world.nations) owners.push({ id: n.id, kind: 'nation', name: n.name, polys: n.polygons, free: false });
for (const n of world.neutral) owners.push({ id: 'n:' + n.name, kind: 'neutral', name: n.name, polys: n.polygons, free: false });
for (const n of world.frontier) owners.push({ id: 'f:' + n.name, kind: 'frontier', name: n.name, polys: n.polygons, free: true });
let overrides = { capitals: {}, claims: [], extraNeutrals: [], free: [], replace: [], drop: [], carve: [], islands: [] };
if (overridesFile) {
  // an overrides file may "include" others (processed in order) so regions can be authored in separate files
  const load = f => { const j = JSON.parse(fs.readFileSync(f, 'utf8')); for (const inc of (j.include || [])) load(require('path').resolve(require('path').dirname(f), inc));
    for (const k of ['claims', 'extraNeutrals', 'free', 'replace', 'drop', 'carve', 'islands']) overrides[k] = overrides[k].concat(j[k] || []);
    Object.assign(overrides.capitals, j.capitals || {}); };
  load(overridesFile);
}
for (const id of overrides.replace) { const o = world.nations.find(n => n.id === id) ? null : null; }
for (const o of owners) if (overrides.replace.includes(o.id)) o.polys = [];
for (const d of overrides.drop) {
  const o = owners.find(x => x.id === d.owner); if (!o) throw new Error('drop: unknown owner ' + d.owner);
  const [x0, y0, x1, y1] = d.within; const before = o.polys.length;
  o.polys = o.polys.filter(p => !p.every(q => q[0] >= x0 && q[0] <= x1 && q[1] >= y0 && q[1] <= y1));
  if (o.polys.length === before) console.log('WARNING: drop matched nothing for', d.owner, d.within);
}
const idx = new Map(); owners.forEach((o, i) => idx.set(o.id, i + 1));
for (const x of overrides.extraNeutrals) { owners.push({ id: 'n:' + x.name, kind: 'neutral', name: x.name, polys: [], free: false }); idx.set('n:' + x.name, owners.length); }

// organic islands: a noisy ellipse (never a regular polygon), deterministic in its seed
function island(cx, cy, rx, ry, seed, rot) {
  const n = 84, pts = []; const ph = [0, 1, 2, 3, 4, 5].map(k => L.hash(seed, k, 11) * 6.2832); const am = [0.22, 0.17, 0.13, 0.10, 0.07, 0.05];
  for (let i = 0; i < n; i++) {
    const t = i / n * 6.2832; let r = 1; for (let k = 0; k < 6; k++) r += am[k] * Math.sin(t * (k + 2) + ph[k]);
    r += (L.hash(seed, i, 29) - 0.5) * 0.11;
    let x = Math.cos(t) * rx * r, y = Math.sin(t) * ry * r; const c = Math.cos(rot || 0), sn = Math.sin(rot || 0);
    pts.push([Math.round((cx + x * c - y * sn) * 10) / 10, Math.round((cy + x * sn + y * c) * 10) / 10]);
  }
  return pts;
}
for (const isl of overrides.islands) overrides.free.push({ owner: isl.owner, polygons: isl.parts.map((p, i) => island(p[0], p[1], p[2], p[3], isl.seed * 31 + i, p[4] || 0)) });
// owners whose polygons are used verbatim (open-sea islands / frontier shapes): they bring their own land
const forcedLand = new Uint8Array(W * H);
const freeOwners = new Set(owners.filter(o => o.free).map(o => o.id));
for (const f of overrides.free) { freeOwners.add(f.owner); }

// ---------------------------------------------------------------- 3. initial ownership (priority: neutral < nation, bigger < smaller, overrides last)
const lab = new Int16Array(W * H);
const ownerArea = owners.map(o => o.polys.reduce((s, p) => s + Math.abs(L.polyArea(p)), 0));
const order = owners.map((o, i) => i).filter(i => !freeOwners.has(owners[i].id))
  .sort((a, b) => (owners[a].kind === owners[b].kind ? ownerArea[b] - ownerArea[a] : (owners[a].kind === 'neutral' ? -1 : 1)));
for (const oi of order) for (const poly of owners[oi].polys) L.fillPoly(lab, poly, oi + 1, land);
// authored claims (land is trimmed by the mask, so claims may overshoot into the sea)
// authored claims are drawn with a steady hand; real borders are not: every edge gets fractal wobble (unless the claim says "exact")
function jitter(poly, seed) {
  const out = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i], b = poly[(i + 1) % poly.length]; const len = Math.hypot(b[0] - a[0], b[1] - a[1]);
    const seg = len > 8 ? L.meander(a, b, Math.min(9, len * 0.16), 5, seed + i) : [a, b];
    for (let k = 0; k < seg.length - 1; k++) out.push(seg[k]);
  }
  return out;
}
for (const c of overrides.claims) {
  if (!c.exact) c.polygons = c.polygons.map((p, i) => jitter(p, (c.owner.length * 17 + i * 5) % 211));
  const v = idx.get(c.owner); if (!v) throw new Error('unknown owner ' + c.owner);
  let allow = land;
  if (c.only) {   // only overwrite pixels currently owned by these owners ("__unowned__" = land nobody owns yet)
    const set = new Set(c.only.map(id => id === '__unowned__' ? 0 : idx.get(id))); allow = new Uint8Array(W * H);
    for (let i = 0; i < allow.length; i++) allow[i] = (land[i] && set.has(lab[i])) ? 1 : 0;
  }
  for (const poly of c.polygons) L.fillPoly(lab, poly, v, c.free ? null : allow);
}
for (const c of overrides.carve) for (const poly of c.polygons) L.fillPoly(lab, poly, 0, null);   // straits and lake-fills: back to sea
// free shapes (islands that are not on the land mask)
for (const f of overrides.free) { const v = idx.get(f.owner); if (!v) throw new Error('unknown owner ' + f.owner); for (const poly of f.polygons) L.fillPoly(lab, poly, v, null); }
for (const oi of owners.keys()) if (freeOwners.has(owners[oi].id) && !overrides.free.some(f => f.owner === owners[oi].id)) for (const poly of owners[oi].polys) L.fillPoly(lab, poly, oi + 1, null);
// forced-land layer = everything labelled that is not on the mask
for (let i = 0; i < lab.length; i++) if (lab[i] && !land[i]) forcedLand[i] = 1;
const solid = new Uint8Array(W * H); for (let i = 0; i < solid.length; i++) solid[i] = land[i] | forcedLand[i];

// ---------------------------------------------------------------- 4. gaps: land with no owner
const kindOf = v => owners[v - 1].kind;
function bfsNearest(sourcePred) {
  const dist = new Int32Array(W * H).fill(-1), near = new Int16Array(W * H);
  const q = new Int32Array(W * H); let qh = 0, qt = 0;
  for (let i = 0; i < lab.length; i++) if (lab[i] && sourcePred(lab[i])) { dist[i] = 0; near[i] = lab[i]; q[qt++] = i; }
  while (qh < qt) {
    const i = q[qh++]; const x = i % W, y = (i / W) | 0; const d = dist[i] + 1;
    for (const j of [x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, y > 0 ? i - W : -1, y < H - 1 ? i + W : -1]) {
      if (j < 0 || !solid[j] || dist[j] !== -1) continue; dist[j] = d; near[j] = near[i]; q[qt++] = j;
    }
  }
  return { dist, near };
}
const anyOwner = bfsNearest(() => true);
const neutralOnly = bfsNearest(v => kindOf(v) === 'neutral');
const NATION_REACH = 26;
let filled = 0, isolated = 0;
for (let i = 0; i < lab.length; i++) {
  if (!solid[i] || lab[i]) continue;
  if (anyOwner.dist[i] === -1) { isolated++; continue; }
  const n = anyOwner.near[i];
  if (kindOf(n) === 'nation' && anyOwner.dist[i] > NATION_REACH && neutralOnly.dist[i] !== -1) lab[i] = neutralOnly.near[i]; else lab[i] = n;
  filled++;
}
log('gap pixels filled:', filled, ' isolated (no land link to any owner):', isolated);

// capitals: a capital must lie in its own realm. Overrides move one explicitly (Copenhagen); a harbour town that the mask puts in the sea moves to the nearest own land (a few pixels).
const capitalOut = {};
{
  const cap = overrides.capitals || {};
  for (const n of world.nations) {
    const c = n.capital; if (!c) continue; const v = idx.get(n.id);
    let x = c.x, y = c.y, name = c.name, why = '';
    if (cap[n.id]) { x = cap[n.id].x; y = cap[n.id].y; name = cap[n.id].name || name; why = 'authored'; }
    const at = (px, py) => lab[Math.round(py) * W + Math.round(px)];
    if (at(x, y) !== v) {
      let best = 1e9, bx = x, by = y;
      const R = 40;
      for (const margin of [3, 2, 1, 0]) {   // prefer a spot well inside the realm (all pixels within `margin` are its own)
        for (let dy = -R; dy <= R; dy++) for (let dx = -R; dx <= R; dx++) {
          const px = Math.round(x) + dx, py = Math.round(y) + dy; if (px < margin || py < margin || px >= W - margin || py >= H - margin) continue;
          if (lab[py * W + px] !== v) continue;
          let inner = true; for (let my = -margin; my <= margin && inner; my++) for (let mx = -margin; mx <= margin; mx++) if (lab[(py + my) * W + px + mx] !== v) { inner = false; break; }
          if (!inner) continue; const d = dx * dx + dy * dy; if (d < best) { best = d; bx = px; by = py; }
        }
        if (best < 1e9) break;
      }
      if (best < 1e9) { why = (why ? why + '+' : '') + 'moved ' + Math.sqrt(best).toFixed(1) + 'px to own land'; x = bx; y = by; }
      else why = 'NO OWN LAND NEAR: capital is inside ' + (at(x, y) ? owners[at(x, y) - 1].id : 'the sea');
    }
    capitalOut[n.id] = { name, x: Math.round(x * 10) / 10, y: Math.round(y * 10) / 10, was: [c.x, c.y], note: why };
    if (why) log('CAPITAL', n.id, name, '(' + c.x + ',' + c.y + ') ->', '(' + x + ',' + y + ')', why);
  }
}
// isolated land components: report (and give them to the nearest owner by Euclid distance if they are big enough)
{
  const un = new Uint8Array(W * H); for (let i = 0; i < un.length; i++) un[i] = (solid[i] && !lab[i]) ? 1 : 0;
  const c = L.components(un, i => un[i] === 1);
  const sum = new Map();
  for (let i = 0; i < un.length; i++) if (un[i]) { const k = c.comp[i]; const s = sum.get(k) || { n: 0, x: 0, y: 0 }; s.n++; s.x += i % W; s.y += (i / W) | 0; sum.set(k, s); }
  const big = [...sum.entries()].filter(([k, s]) => s.n >= 30).sort((a, b) => b[1].n - a[1].n);
  log('isolated components >= 30 px:', big.length, big.slice(0, 25).map(([k, s]) => `${s.n}px@(${Math.round(s.x / s.n)},${Math.round(s.y / s.n)})`).join(' '));
  // nearest owner by Euclid distance from the component centre, searched over a coarse sample of owned pixels
  const samples = []; for (let y = 0; y < H; y += 3) for (let x = 0; x < W; x += 3) if (lab[y * W + x]) samples.push([x, y, lab[y * W + x]]);
  for (const [k, s] of sum.entries()) {
    const cx = s.x / s.n, cy = s.y / s.n; let best = 1e18, bo = 0;
    for (const [x, y, v] of samples) { const d = (x - cx) ** 2 + (y - cy) ** 2; if (d < best) { best = d; bo = v; } }
    s.owner = best <= 70 * 70 ? bo : 0;
  }
  for (let i = 0; i < un.length; i++) if (un[i]) { const s = sum.get(c.comp[i]); lab[i] = (s.n >= 12 && s.owner) ? s.owner : 0; if (!lab[i]) solid[i] = 0; }
}

// ---------------------------------------------------------------- 5. clean fragments and pinches
function relabelSmall(minPx) {
  // each 4-connected region of one owner smaller than minPx that touches another owner is absorbed by its longest-border neighbour
  const c = L.components(lab, i => lab[i] !== 0);
  let changed = 0;
  const border = new Map();   // comp -> Map(neighbourLabel -> count)
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const i = y * W + x; if (!lab[i]) continue; const k = c.comp[i]; if (c.sizes[k] >= minPx) continue;
    for (const j of [x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, y > 0 ? i - W : -1, y < H - 1 ? i + W : -1]) {
      if (j < 0) continue; const o = lab[j]; if (!o || o === lab[i]) continue;
      let m = border.get(k); if (!m) { m = new Map(); border.set(k, m); } m.set(o, (m.get(o) || 0) + 1);
    }
  }
  const choose = new Map(); for (const [k, m] of border) { let b = 0, bc = -1; for (const [o, n] of m) if (n > bc) { bc = n; b = o; } choose.set(k, b); }
  for (let i = 0; i < lab.length; i++) if (lab[i]) { const k = c.comp[i]; if (c.sizes[k] < minPx && choose.has(k)) { lab[i] = choose.get(k); changed++; } }
  return changed;
}
log('tiny fragments absorbed (px):', relabelSmall(30));

function removePinches() {
  let total = 0;
  for (let pass = 0; pass < 8; pass++) {
    let n = 0;
    for (let y = 0; y < H - 1; y++) for (let x = 0; x < W - 1; x++) {
      const a = y * W + x, b = a + 1, c = a + W, d = c + 1;   // a=NW b=NE c=SW d=SE
      const A = lab[a], B = lab[b], C = lab[c], D = lab[d];
      if (A === D && B === C && A !== B) { const win = A === 0 ? B : A; lab[a] = lab[b] = lab[c] = lab[d] = win; solid[a] = solid[b] = solid[c] = solid[d] = 1; n++; continue; }   // checkerboard of two labels: one wins (land beats sea)
      if (A === D && A !== B && A !== C && B !== C) { lab[b] = A; if (A) solid[b] = 1; n++; continue; }   // A touches itself diagonally, B and C differ
      if (B === C && B !== A && B !== D && A !== D) { lab[a] = B; if (B) solid[a] = 1; n++; continue; }
    }
    total += n; if (!n) break;
  }
  return total;
}
for (let k = 0; k < 4; k++) { const p = removePinches(); const s = relabelSmall(8); if (!p && !s) break; }
log('pinch fixes done');

// ---------------------------------------------------------------- 6. arcs
const stat = {};
const res = L.extractArcs(lab);
log('arcs:', res.arcs.length, 'edges:', res.E);
const VW = res.VW;

const EPS = 0.8;
const STRAIGHT = 13;   // simplified segments longer than this on a land-land border are turned into natural meanders
function processArc(a, ai, wobble) {
  const coast = (a.l === 0 || a.r === 0);
  let pts = L.dp(a.pts, EPS);
  if (!coast && !a.closed && wobble > 0) {
    // political borders become meanders (deterministic, shared by both neighbours); `wobble` scales the amplitude
    const out = [pts[0]];
    for (let i = 0; i < pts.length - 1; i++) {
      const p = pts[i], q = pts[i + 1]; const len = Math.hypot(q[0] - p[0], q[1] - p[1]);
      if (len > STRAIGHT) { const seg = L.meander(p, q, Math.min(7, len * 0.11) * wobble, 4.5, ai % 97); for (let k = 1; k < seg.length; k++) out.push(seg[k]); }
      else out.push(q);
    }
    pts = out;
  }
  pts = L.chaikin(pts, 2, a.closed);
  pts = L.dp(pts, 0.22);   // the smoothing makes a point every pixel or so; this drops the redundant ones (once per arc, so both neighbours keep identical points)
  return pts.map(p => [Math.round(p[0] * 10) / 10, Math.round(p[1] * 10) / 10]);
}
const wobbleOf = new Float32Array(res.arcs.length).fill(1);
let processed = res.arcs.map((a, ai) => processArc(a, ai, 1));

// A meander can make an arc cross a neighbouring one where the two run close together. Find every proper crossing between any two
// segments of the whole drawing and calm just those arcs (half the wobble, a quarter, none) until nothing crosses.
function crossingArcs(arcs) {
  const cell = 16, grid = new Map(); const bad = new Set();
  const o = (p, q, r) => (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0]);
  const segs = [];
  arcs.forEach((pts, ai) => { for (let i = 0; i < pts.length - 1; i++) segs.push([ai, pts[i], pts[i + 1]]); });
  segs.forEach(([ai, a, b], si) => {
    for (let gx = Math.floor(Math.min(a[0], b[0]) / cell); gx <= Math.floor(Math.max(a[0], b[0]) / cell); gx++)
      for (let gy = Math.floor(Math.min(a[1], b[1]) / cell); gy <= Math.floor(Math.max(a[1], b[1]) / cell); gy++) {
        const k = gx * 100003 + gy; let l = grid.get(k); if (!l) { l = []; grid.set(k, l); } l.push(si);
      }
  });
  const seen = new Set();
  for (const l of grid.values()) for (let x = 0; x < l.length; x++) for (let y = x + 1; y < l.length; y++) {
    const si = l[x], sj = l[y]; const pk = si * 4e6 + sj; if (seen.has(pk)) continue; seen.add(pk);
    const [ai, a, b] = segs[si], [aj, c, d] = segs[sj];
    if (ai === aj && Math.abs(si - sj) <= 1) continue;
    const o1 = o(a, b, c), o2 = o(a, b, d), o3 = o(c, d, a), o4 = o(c, d, b);
    if (o1 * o2 < 0 && o3 * o4 < 0) { bad.add(ai); bad.add(aj); }
  }
  return bad;
}
for (let pass = 0; pass < 4; pass++) {
  const bad = crossingArcs(processed);
  if (!bad.size) break;
  for (const ai of bad) { wobbleOf[ai] = wobbleOf[ai] > 0.3 ? wobbleOf[ai] * 0.5 : 0; processed[ai] = processArc(res.arcs[ai], ai, wobbleOf[ai]); }
  log('arcs calmed after crossing (pass ' + (pass + 1) + '):', bad.size);
}

// ---------------------------------------------------------------- 7. rings per owner
const outArcs = new Map();   // owner -> list of {pts (oriented owner-left), arc}
res.arcs.forEach((a, ai) => {
  for (const [lab0, left] of [[a.l, true], [a.r, false]]) {
    if (!lab0) continue; const pts = processed[ai];
    const oriented = left ? pts : pts.slice().reverse();
    let l = outArcs.get(lab0); if (!l) { l = []; outArcs.set(lab0, l); } l.push({ pts: oriented, ai });
  }
});
// key start vertex by rounded original lattice coordinate (arc endpoints are unchanged lattice vertices)
const geometry = {}; const holes = [];
for (const [o, list] of outArcs) {
  const byStart = new Map();
  for (const a of list) { const k = a.pts[0][0] + ',' + a.pts[0][1]; if (byStart.has(k)) { byStart.get(k).push(a); } else byStart.set(k, [a]); }
  const used = new Set(); const rings = [];
  for (const a of list) {
    if (used.has(a)) continue; const ring = []; let cur = a; let guard = 0;
    for (;;) {
      used.add(cur); for (let i = (ring.length ? 1 : 0); i < cur.pts.length; i++) ring.push(cur.pts[i]);
      const k = cur.pts[cur.pts.length - 1][0] + ',' + cur.pts[cur.pts.length - 1][1];
      const cand = (byStart.get(k) || []).filter(c => !used.has(c));
      if (!cand.length) break; cur = cand[0]; if (++guard > 100000) break;
    }
    rings.push(ring);
  }
  geometry[o] = rings;
}
// classify outer rings / holes by signed area sign (outer rings share the sign of the biggest ring);
// a hole (an enclave of another owner, a lake, a sea enclosed by the realm) is joined to its outer ring by a zero-width bridge ("keyhole"),
// so a polygon stays one simple list of points that fills correctly with the non-zero rule and is hit-tested correctly by even-odd.
function inRing(x, y, p) { let c = false; for (let i = 0, j = p.length - 1; i < p.length; j = i++) { const xi = p[i][0], yi = p[i][1], xj = p[j][0], yj = p[j][1]; if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c; } return c; }
function closed(r) { const a0 = r[0], z = r[r.length - 1]; return (a0[0] === z[0] && a0[1] === z[1]) ? r.slice() : r.concat([a0]); }
function keyhole(outer, hole) {
  const O = closed(outer), Hh = closed(hole); const h = Hh.slice(0, -1);
  let bj = 0, bi = 0, bd = 1e18;
  for (let j = 0; j < O.length - 1; j++) for (let i = 0; i < h.length; i++) { const d = (O[j][0] - h[i][0]) ** 2 + (O[j][1] - h[i][1]) ** 2; if (d < bd) { bd = d; bj = j; bi = i; } }
  const ring = O.slice(0, bj + 1);
  for (let k = 0; k < h.length; k++) ring.push(h[(bi + k) % h.length]);
  ring.push(h[bi]);
  for (let k = bj; k < O.length; k++) ring.push(O[k]);
  return ring;
}
const result = {}; let holesJoined = 0, holesFilled = 0;
for (const [o, rings] of Object.entries(geometry)) {
  const areas = rings.map(r => L.polyArea(r)); const big = areas.reduce((m, v) => Math.abs(v) > Math.abs(m) ? v : m, 0);
  const outer = [], hl = [];
  rings.forEach((r, i) => { if ((areas[i] > 0) === (big > 0)) outer.push({ r: closed(r), a: Math.abs(areas[i]), holes: [] }); else hl.push({ r, a: Math.abs(areas[i]) }); });
  for (const h of hl) {
    if (h.a < 12) { holesFilled++; continue; }   // pixel-sized specks are not holes
    const host = outer.filter(x => inRing(h.r[0][0], h.r[0][1], x.r)).sort((p, q) => p.a - q.a)[0];
    if (host) { host.holes.push(h.r); holesJoined++; } else holesFilled++;
  }
  outer.sort((p, q) => q.a - p.a);
  const keep = outer.filter(x => x.a >= 8);   // a polygon of a few pixels is not a place
  result[owners[o - 1].id] = keep.map(x => { let r = x.r; for (const h of x.holes.sort((p, q) => L.polyArea(q) - L.polyArea(p))) r = keyhole(r, h); return r; });
}
log('holes joined into rings as keyholes:', holesJoined, ' specks filled:', holesFilled);
for (const o of owners) { const p = result[o.id]; log(o.id.padEnd(22), 'polygons', String(p ? p.length : 0).padStart(3), 'verts', String(p ? p.reduce((s, r) => s + r.length, 0) : 0).padStart(5)); }
fs.writeFileSync(outFile.replace('.json', '.painted.json'), JSON.stringify(overrides.islands.map(i => i.owner)));
fs.writeFileSync(outFile.replace('.json', '.capitals.json'), JSON.stringify(capitalOut, null, 1));
fs.writeFileSync(outFile, JSON.stringify({ owners: owners.map(o => ({ id: o.id, kind: o.kind, name: o.name })), geometry: result }));
// label raster for validation tools
fs.writeFileSync(P.out('labels.bin'), Buffer.from(lab.buffer, lab.byteOffset, lab.byteLength));
fs.writeFileSync(P.out('labels.owners.json'), JSON.stringify(owners.map(o => o.id)));
log('wrote', outFile);
