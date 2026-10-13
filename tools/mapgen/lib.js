// Planar-partition toolkit for the EmpireSim world map.
// A label raster (0 = sea, 1..N = owner) is turned into polygons whose shared borders are the SAME vertices on both sides.
'use strict';
const W = 2200, H = 1150;

// ---------- raster helpers ----------
function fillPoly(grid, poly, value, mask) {
  // even-odd scanline fill at pixel centres; `mask` (optional): only write where mask[i] != 0
  let y0 = Infinity, y1 = -Infinity;
  for (const p of poly) { y0 = Math.min(y0, p[1]); y1 = Math.max(y1, p[1]); }
  y0 = Math.max(0, Math.floor(y0)); y1 = Math.min(H - 1, Math.ceil(y1));
  const n = poly.length;
  for (let y = y0; y <= y1; y++) {
    const yc = y + 0.5, xs = [];
    for (let i = 0, j = n - 1; i < n; j = i++) {
      const a = poly[i], b = poly[j];
      if ((a[1] > yc) !== (b[1] > yc)) xs.push(a[0] + (yc - a[1]) * (b[0] - a[0]) / (b[1] - a[1]));
    }
    xs.sort((p, q) => p - q);
    for (let k = 0; k + 1 < xs.length; k += 2) {
      const xa = Math.max(0, Math.ceil(xs[k] - 0.5)), xb = Math.min(W - 1, Math.ceil(xs[k + 1] - 0.5) - 1);   // half-open: a pixel centre exactly on a shared edge belongs to one side only
      for (let x = xa; x <= xb; x++) { const i = y * W + x; if (!mask || mask[i]) grid[i] = value; }
    }
  }
}

function polyArea(p) { let a = 0; for (let i = 0; i < p.length; i++) { const j = (i + 1) % p.length; a += p[i][0] * p[j][1] - p[j][0] * p[i][1]; } return a / 2; }

// 4-connected components of equal value; returns {comp: Int32Array (ids from 1), count, sizes[]}
function components(grid, pred) {
  const comp = new Int32Array(W * H); const sizes = [0]; let count = 0; const stack = new Int32Array(W * H);
  for (let s = 0; s < W * H; s++) {
    if (comp[s] || !pred(s)) continue;
    const v = grid[s]; count++; let sp = 0, sz = 0; stack[sp++] = s; comp[s] = count;
    while (sp) {
      const i = stack[--sp]; sz++; const x = i % W, y = (i / W) | 0;
      if (x > 0 && !comp[i - 1] && grid[i - 1] === v && pred(i - 1)) { comp[i - 1] = count; stack[sp++] = i - 1; }
      if (x < W - 1 && !comp[i + 1] && grid[i + 1] === v && pred(i + 1)) { comp[i + 1] = count; stack[sp++] = i + 1; }
      if (y > 0 && !comp[i - W] && grid[i - W] === v && pred(i - W)) { comp[i - W] = count; stack[sp++] = i - W; }
      if (y < H - 1 && !comp[i + W] && grid[i + W] === v && pred(i + W)) { comp[i + W] = count; stack[sp++] = i + W; }
    }
    sizes.push(sz);
  }
  return { comp, count, sizes };
}

// ---------- arcs ----------
// Boundary edges live on the pixel-corner lattice (vertex id = y*(W+1)+x).
// An edge is stored with the lower label on its LEFT (screen coordinates, y down: left of travel direction).
function extractArcs(lab) {
  const VW = W + 1;
  const vid = (x, y) => y * VW + x;
  // edges: arrays
  const ea = [], eb = [], ela = [], elb = [];   // from-vertex, to-vertex, left label, right label
  // vertical edges between (x-1,y) and (x,y) at corner column x, from (x,y) to (x,y+1)
  for (let y = 0; y < H; y++) for (let x = 0; x <= W; x++) {
    const l = x > 0 ? lab[y * W + x - 1] : 0, r = x < W ? lab[y * W + x] : 0;   // outside the picture counts as sea
    if (l === r) continue;
    // travelling south (y increasing) from (x,y) to (x,y+1): left-hand side (screen) is EAST = pixel x, right is WEST = pixel x-1
    // we want lower label on the left. east pixel label = r, west = l.
    if (r < l) { ea.push(vid(x, y)); eb.push(vid(x, y + 1)); ela.push(r); elb.push(l); }
    else { ea.push(vid(x, y + 1)); eb.push(vid(x, y)); ela.push(l); elb.push(r); }   // travelling north: left = WEST = l
  }
  // horizontal edges between (x,y-1) and (x,y) at corner row y, from (x,y) to (x+1,y)
  for (let y = 0; y <= H; y++) for (let x = 0; x < W; x++) {
    const u = y > 0 ? lab[(y - 1) * W + x] : 0, d = y < H ? lab[y * W + x] : 0;
    if (u === d) continue;
    // travelling east: left = NORTH = pixel above = u
    if (u < d) { ea.push(vid(x, y)); eb.push(vid(x + 1, y)); ela.push(u); elb.push(d); }
    else { ea.push(vid(x + 1, y)); eb.push(vid(x, y)); ela.push(d); elb.push(u); }   // travelling west: left = SOUTH = d
  }
  // frame edges against the image border are not boundaries (labels outside are treated as the same as inside)
  const E = ea.length;
  // outgoing edge lists per vertex (as the start vertex) and incident count per vertex
  const outHead = new Map(); const deg = new Map();
  const outNext = new Int32Array(E).fill(-1);
  for (let e = 0; e < E; e++) {
    const s = ea[e];
    outNext[e] = outHead.has(s) ? outHead.get(s) : -1; outHead.set(s, e);
    deg.set(s, (deg.get(s) || 0) + 1); deg.set(eb[e], (deg.get(eb[e]) || 0) + 1);
  }
  const arcOfEdge = new Int32Array(E).fill(-1);
  const arcs = [];   // {pts:[[x,y]...] (vertex coords), l, r, closed}
  const vx = v => v % VW, vy = v => (v / VW) | 0;
  // node = vertex whose degree != 2
  const isNode = v => deg.get(v) !== 2;
  function walk(startEdge) {
    const pts = []; let e = startEdge; const id = arcs.length; const l = ela[e], r = elb[e];
    pts.push([vx(ea[e]), vy(ea[e])]);
    for (;;) {
      arcOfEdge[e] = id; const t = eb[e]; pts.push([vx(t), vy(t)]);
      if (isNode(t)) { arcs.push({ pts, l, r, closed: false }); return; }
      // continue with the unique outgoing edge at t having the same label pair
      let nxt = -1;
      for (let c = outHead.get(t); c !== undefined && c !== -1; c = outNext[c]) if (ela[c] === l && elb[c] === r && arcOfEdge[c] === -1) { nxt = c; break; }
      if (nxt === -1) { // closed ring back to the start edge
        arcs.push({ pts, l, r, closed: pts[0][0] === pts[pts.length - 1][0] && pts[0][1] === pts[pts.length - 1][1] }); return;
      }
      e = nxt;
    }
  }
  // arcs starting at nodes first
  for (let e = 0; e < E; e++) if (arcOfEdge[e] === -1 && isNode(ea[e])) walk(e);
  // remaining edges belong to closed loops with no node on them
  for (let e = 0; e < E; e++) if (arcOfEdge[e] === -1) walk(e);
  return { arcs, arcOfEdge, ea, eb, ela, elb, E, vid, VW };
}

// Douglas-Peucker on an open polyline (keeps first and last)
function dp(pts, eps) {
  const n = pts.length; if (n <= 2) return pts.slice();
  const keep = new Uint8Array(n); keep[0] = keep[n - 1] = 1; const st = [[0, n - 1]];
  while (st.length) {
    const [a, b] = st.pop(); let md = -1, mi = -1;
    const ax = pts[a][0], ay = pts[a][1], bx = pts[b][0], by = pts[b][1]; const dx = bx - ax, dy = by - ay; const L = Math.hypot(dx, dy);
    for (let i = a + 1; i < b; i++) {
      const d = L === 0 ? Math.hypot(pts[i][0] - ax, pts[i][1] - ay) : Math.abs(dy * pts[i][0] - dx * pts[i][1] + bx * ay - by * ax) / L;
      if (d > md) { md = d; mi = i; }
    }
    if (md > eps) { keep[mi] = 1; st.push([a, mi], [mi, b]); }
  }
  const out = []; for (let i = 0; i < n; i++) if (keep[i]) out.push(pts[i]); return out;
}

// Chaikin corner cutting: open (ends fixed) or closed
function chaikin(pts, iters, closed) {
  let p = pts;
  for (let it = 0; it < iters; it++) {
    const out = []; const n = p.length;
    if (closed) {
      const m = n - 1;   // last == first
      for (let i = 0; i < m; i++) { const a = p[i], b = p[(i + 1) % m]; out.push([0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]], [0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1]]); }
      out.push(out[0].slice());
    } else {
      out.push(p[0]);
      for (let i = 0; i < n - 1; i++) {
        const a = p[i], b = p[i + 1];
        if (i > 0) out.push([0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]]);
        if (i < n - 2) out.push([0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1]]);
      }
      out.push(p[n - 1]);
    }
    p = out;
  }
  return p;
}

// deterministic hash -> [0,1)
function hash(a, b, c) { let h = (a * 374761393 + b * 668265263 + c * 2147483647) | 0; h = (h ^ (h >>> 13)) * 1274126177 | 0; h ^= h >>> 16; return (h >>> 0) / 4294967296; }

// Midpoint displacement of a straight segment into a natural meander. Endpoints fixed; deterministic in the endpoints.
function meander(a, b, amp, minStep, seed) {
  const pts = [a, b];
  let level = 0;
  function rec(p, q, amplitude, depth) {
    const dx = q[0] - p[0], dy = q[1] - p[1]; const len = Math.hypot(dx, dy);
    if (len < minStep || depth > 6) return [p, q];
    const nx = -dy / len, ny = dx / len;
    const h = hash(Math.round(p[0] * 10), Math.round(p[1] * 10) + seed, Math.round(q[0] * 10) + Math.round(q[1] * 7));
    const off = (h - 0.5) * 2 * amplitude;
    const m = [(p[0] + q[0]) / 2 + nx * off, (p[1] + q[1]) / 2 + ny * off];
    const left = rec(p, m, amplitude * 0.55, depth + 1), right = rec(m, q, amplitude * 0.55, depth + 1);
    return left.concat(right.slice(1));
  }
  return rec(a, b, amp, 0);
}

module.exports = { W, H, fillPoly, polyArea, components, extractArcs, dp, chaikin, meander, hash };
