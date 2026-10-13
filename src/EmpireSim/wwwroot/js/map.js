/* EmpireSim world map: canvas rendering with pan, zoom and tap-to-select.
   Shapes arrive from .NET as { shapes: [...], selectedId } where each shape
   is { id, name, kind, color, polygons: [[{x,y}...], ...], labelX, labelY, annexedCities }.
   kind is "nation", "neutral", "frontier" or "colony". A shape is one country (possibly several
   polygons: islands, exclaves, annexed lands), a neutral territory, or a colony region. A colony region is
   "frontier" while nobody has settled it (status "unclaimed": an open claim, or "mission": a country's colonists
   are on their way, with progress 0..1) and "colony" once it is settled: it then has the colour of its owner and
   realm = the owner's id, so the renderer treats it as part of that realm (a faint dotted line where it meets the
   owner's own land, no border).
   Property names may arrive camelCase or PascalCase.

   The political map is one planar partition: two realms that meet share the SAME points along
   their border. The renderer relies on that to tell, edge by edge, what kind of line it is:
     - border: an edge used by two different realms        -> one thin, slightly darkened line (drawn once)
     - coast:  an edge used by one realm only               -> a hair-thin line in the realm's own darkened colour
     - inner:  an edge between two polygons of one realm    -> a faint dotted province line (an annexed land or a colony beside its master)
     - bridge: an edge used twice by one polygon (the zero-width bridge to an enclave or lake) -> not drawn
     - claim:  an edge of an unsettled colony region (open or under a mission), whoever is on its other side -> a dashed line
   Colours are muted towards the parchment so the painted terrain stays visible underneath. */

window.empireMap = (() => {
    const WORLD_W = 2200, WORLD_H = 1150;
    let canvas = null, ctx = null, dotNetRef = null;
    let data = { shapes: [], selectedId: null };
    let view = { scale: 1, ox: 0, oy: 0 };
    let dpr = 1;
    let terrainImg = null, terrainReady = false;

    // Sea labels in world coordinates (engraved cartouche style).
    const SEA_LABELS = [
        ["ATLANTIC OCEAN", 100, 460], ["NORTH SEA", 280, 221],
        ["MEDITERRANEAN SEA", 430, 405], ["BLACK SEA", 590, 340],
        ["CASPIAN SEA", 760, 350], ["RED SEA", 630, 552],
        ["PERSIAN GULF", 760, 497], ["ARABIAN SEA", 900, 626],
        ["INDIAN OCEAN", 1050, 966], ["BAY OF BENGAL", 1130, 626],
        ["SOUTH CHINA SEA", 1390, 626], ["PACIFIC OCEAN", 1900, 828],
        ["SEA OF JAPAN", 1600, 368],
    ];

    // ---- pointer state ----
    const pointers = new Map();
    let downInfo = null;      // {x, y, time, moved}
    let pinchStart = null;    // {dist, scale, cx, cy}

    function cssW() { return canvas.clientWidth || 300; }
    function cssH() { return canvas.clientHeight || 300; }

    function resize() {
        dpr = window.devicePixelRatio || 1;
        canvas.width = Math.max(1, Math.round(cssW() * dpr));
        canvas.height = Math.max(1, Math.round(cssH() * dpr));
        draw();
    }

    let viewKept = false;   // the view survives leaving the map (this module lives as long as the page does), so Back can return to it

    // keepView: the player is coming back to the map (Back): leave the view as they left it instead of starting at their capital.
    function init(canvasId, ref, focusX, focusY, keepView) {
        canvas = document.getElementById(canvasId);
        if (!canvas) return false;
        dotNetRef = ref;
        ctx = canvas.getContext('2d');

        if (keepView && viewKept) {
            clampView();   // (the window may have been resized meanwhile)
        } else {
            // Start zoomed into the player's lands like a war-room chart,
            // not fitted to the whole tiny world.
            const fit = Math.min(cssW() / WORLD_W, cssH() / WORLD_H) || 1;
            const s = Math.min(fit * 3.2, 1.2);
            view.scale = s;
            const fx = (focusX ?? WORLD_W / 2), fy = (focusY ?? WORLD_H / 2);
            view.ox = cssW() / 2 - fx * s;
            view.oy = cssH() / 2 - fy * s;
            clampView();
        }
        viewKept = true;

        new ResizeObserver(resize).observe(canvas);
        canvas.addEventListener('pointerdown', onDown);
        canvas.addEventListener('pointermove', onMove);
        canvas.addEventListener('pointerup', onUp);
        canvas.addEventListener('pointercancel', onUp);
        canvas.addEventListener('wheel', onWheel, { passive: false });
        canvas.style.touchAction = 'none';
        loadTerrain();
        resize();
        return true;
    }

    function loadTerrain() {
        if (terrainImg) return;
        terrainImg = new Image();
        terrainImg.onload = () => { terrainReady = true; draw(); };
        terrainImg.src = 'images/world-terrain.jpg';
    }

    // ---------------------------------------------------------------- colour
    function hexRgb(hex) {
        const h = (hex || '#555555').replace('#', '');
        return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
    }
    function rgbToHsl(r, g, b) {
        r /= 255; g /= 255; b /= 255;
        const mx = Math.max(r, g, b), mn = Math.min(r, g, b); let h = 0, s = 0; const l = (mx + mn) / 2;
        if (mx !== mn) {
            const d = mx - mn; s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx === r) h = (g - b) / d + (g < b ? 6 : 0); else if (mx === g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
            h /= 6;
        }
        return [h, s, l];
    }
    function hslToRgb(h, s, l) {
        if (s === 0) { const v = Math.round(l * 255); return [v, v, v]; }
        const q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        const f = t => { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1 / 6) return p + (q - p) * 6 * t; if (t < 1 / 2) return q; if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6; return p; };
        return [Math.round(f(h + 1 / 3) * 255), Math.round(f(h) * 255), Math.round(f(h - 1 / 3) * 255)];
    }
    // A restrained atlas palette: keep the hue (so countries stay distinguishable), soften the saturation and bring the
    // lightness into a mid band, so no realm is glaring or black and the terrain shows through its wash.
    const mutedCache = new Map();
    function muted(hex) {
        let m = mutedCache.get(hex); if (m) return m;
        const [r, g, b] = hexRgb(hex); const [h, s, l] = rgbToHsl(r, g, b);
        const ln = Math.min(1, Math.max(0, (l - 0.08) / 0.84));
        m = hslToRgb(h, Math.min(0.82, s) * 0.6, 0.36 + 0.3 * ln);
        mutedCache.set(hex, m); return m;
    }
    // perceptual distance (CIE Lab), to tell whether two neighbouring washes can be told apart
    function rgbToLab(rgb) {
        const lin = v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
        const r = lin(rgb[0]), g = lin(rgb[1]), b = lin(rgb[2]);
        const x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * b, z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        const f = t => t > 0.008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116;
        return [116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z))];
    }
    function labDistance(a, b) { const p = rgbToLab(a), q = rgbToLab(b); return Math.hypot(p[0] - q[0], p[1] - q[1], p[2] - q[2]); }
    function rgba(rgb, a) { return `rgba(${rgb[0]},${rgb[1]},${rgb[2]},${a})`; }
    function darker(rgb, f) { return [Math.round(rgb[0] * f), Math.round(rgb[1] * f), Math.round(rgb[2] * f)]; }

    // ---------------------------------------------------------------- geometry
    function ptX(pt) { return !pt ? 0 : (Array.isArray(pt) ? pt[0] : (pt.x ?? pt.X ?? 0)); }
    function ptY(pt) { return !pt ? 0 : (Array.isArray(pt) ? pt[1] : (pt.y ?? pt.Y ?? 0)); }
    function polys(p) { return p.polygons || p.Polygons || []; }

    let shapesCache = null;   // [{ p, rgb, kind, rings: [[ [x,y]... ]], coast: [], border: [], inner: [], outer: [] }]

    // Builds, once per data update, the edge classification described at the top of the file.
    function buildShapes() {
        const shapes = (data.shapes || []).map((p, si) => {
            const rings = [];
            for (const pts of polys(p)) {
                const clean = (pts || []).filter(pt => !!pt).map(pt => [ptX(pt), ptY(pt)]);
                if (clean.length >= 3) rings.push(clean);
            }
            const kind = p.kind || p.Kind || 'nation';
            const status = p.status || p.Status || '';
            const open = kind === 'frontier' && status !== 'mission';   // an open claim: nobody's colour
            return {
                p, si, kind, status, painted: !!(p.painted ?? p.Painted),
                realm: String(p.realm ?? p.Realm ?? (p.id ?? p.Id)),
                progress: Math.max(0, Math.min(1, +(p.progress ?? p.Progress ?? 0))),
                rgb: open ? OPEN_CLAIM_RGB : muted(p.color || p.Color || '#555555'),
                rings, coast: [], border: [], inner: [], outer: [], claim: [],
            };
        });

        // every edge, keyed by its two end points (same key whichever way round it is walked)
        const uses = new Map();
        const key = (x1, y1, x2, y2) => (x1 < x2 || (x1 === x2 && y1 <= y2)) ? `${x1},${y1}|${x2},${y2}` : `${x2},${y2}|${x1},${y1}`;
        shapes.forEach(sh => sh.rings.forEach((ring, ri) => {
            for (let i = 0; i < ring.length; i++) {
                const a = ring[i], b = ring[(i + 1) % ring.length];
                if (a[0] === b[0] && a[1] === b[1]) continue;
                const k = key(a[0], a[1], b[0], b[1]);
                let u = uses.get(k); if (!u) { u = []; uses.set(k, u); }
                u.push({ sh, ri });
            }
        }));

        shapes.forEach(sh => sh.rings.forEach((ring, ri) => {
            for (let i = 0; i < ring.length; i++) {
                const a = ring[i], b = ring[(i + 1) % ring.length];
                if (a[0] === b[0] && a[1] === b[1]) continue;
                const u = uses.get(key(a[0], a[1], b[0], b[1]));
                const seg = [a[0], a[1], b[0], b[1]];
                if (u.length === 1) { sh.coast.push(seg); sh.outer.push(seg); continue; }
                const first = u[0].sh === sh && u[0].ri === ri;
                if (u.length === 2 && u[0].sh.realm === u[1].sh.realm) {
                    if (u[0].sh === u[1].sh && u[0].ri === u[1].ri) continue;   // a bridge to an enclave or lake: never drawn
                    if (first) sh.inner.push(seg);                     // two polygons of one realm meet (a colony beside its owner's land too): a faint province line, once
                    continue;
                }
                const open = u.filter(x => x.sh.kind === 'frontier');
                if (open.length) {                                     // an unsettled colony region on one side: the dashed claim line, drawn by it
                    sh.outer.push(seg);
                    if (open[0].sh === sh && open[0].ri === ri) sh.claim.push(seg);
                    continue;
                }
                // between two different realms: one line, drawn by the first of them; both realms outline it when selected
                sh.outer.push(seg);
                if (first) sh.border.push(seg);
            }
        }));
        separateNeighbours(shapes, uses);
        // a settled colony is drawn in exactly its owner's (possibly nudged) wash
        const byId = new Map(shapes.filter(sh => sh.kind === 'nation').map(sh => [String(sh.p.id ?? sh.p.Id), sh]));
        shapes.forEach(sh => { if (sh.kind === 'colony' && byId.has(sh.realm)) sh.rgb = byId.get(sh.realm).rgb; });
        return shapes;
    }

    // Muting pulls every colour towards the middle, and a few neighbours (Iran and Russia, Burma and Laos, Ming and the Mughals, Genoa and the
    // Iberian Union, Austria and the Empire) start out close in hue. For each pair of neighbouring countries that would be hard to tell apart,
    // nudge the lightness / hue / saturation of one of them (the smallest change that helps), deterministically, so the border between two
    // realms is always a visible change of colour. A realm keeps its own hue family: only the neighbours of a clash are ever touched.
    const MIN_NEIGHBOUR_DISTANCE = 30;
    function separateNeighbours(shapes, uses) {
        const nations = shapes.filter(sh => sh.kind === 'nation' && sh.rings.length);
        const adj = new Map(nations.map(sh => [sh, new Set()]));
        // a settled colony counts as part of its owner when asking who borders whom
        const byRealm = new Map(nations.map(sh => [sh.realm, sh]));
        const ownerOf = sh => sh.kind === 'nation' ? sh : (sh.kind === 'colony' ? byRealm.get(sh.realm) : undefined);
        for (const u of uses.values()) {
            if (u.length !== 2) continue;
            const a = ownerOf(u[0].sh), b = ownerOf(u[1].sh);
            if (!a || !b || a === b) continue;
            adj.get(a).add(b); adj.get(b).add(a);
        }
        const id = sh => String(sh.p.id ?? sh.p.Id);
        const base = new Map(nations.map(sh => [sh, rgbToHsl(sh.rgb[0], sh.rgb[1], sh.rgb[2])]));
        const worst = (sh, rgb) => { let m = 1e9; for (const o of adj.get(sh)) m = Math.min(m, labDistance(rgb, o.rgb)); return m; };
        const order = nations.slice().sort((a, b) => id(a).localeCompare(id(b)));
        for (let pass = 0; pass < 4; pass++) {
            let changed = false;
            for (const sh of order) {
                if (adj.get(sh).size === 0 || worst(sh, sh.rgb) >= MIN_NEIGHBOUR_DISTANCE) continue;
                const [h0, s0, l0] = base.get(sh); let best = sh.rgb, bestScore = Math.min(worst(sh, sh.rgb), MIN_NEIGHBOUR_DISTANCE) - 0;
                for (const dl of [0, 0.07, -0.07, 0.14, -0.14]) for (const dh of [0, 0.03, -0.03, 0.06, -0.06]) for (const ds of [1, 1.3, 0.75]) {
                    if (dl === 0 && dh === 0 && ds === 1) continue;
                    const l = Math.min(0.72, Math.max(0.30, l0 + dl)), h = (h0 + dh + 1) % 1, sat = Math.min(0.55, s0 * ds);
                    const cand = hslToRgb(h, sat, l);
                    const score = Math.min(worst(sh, cand), MIN_NEIGHBOUR_DISTANCE) - 60 * (Math.abs(dl) + 2 * Math.abs(dh)) * 0.25 - Math.abs(ds - 1) * 2;
                    if (score > bestScore) { bestScore = score; best = cand; }
                }
                if (best !== sh.rgb) { sh.rgb = best; changed = true; }
            }
            if (!changed) break;
        }
    }

    function getShapes() { return shapesCache || (shapesCache = buildShapes()); }

    // An open claim wears amber, so it reads as "land to be had" against both the countries' washes and the neutral umber.
    const OPEN_CLAIM_RGB = [240, 232, 196];

    // The diagonal hatch of land with colonists on the way, in the claimant's own darkened colour (one pattern per colour).
    const hatchCache = new Map();
    function hatchFill(rgb) {
        const k = rgb.join(',');
        let pat = hatchCache.get(k);
        if (pat) return pat;
        const c = document.createElement('canvas'); c.width = 9; c.height = 9;
        const g = c.getContext('2d');
        g.strokeStyle = rgba(darker(rgb, 0.5), 0.7); g.lineWidth = 2;
        g.beginPath(); g.moveTo(-1, 10); g.lineTo(10, -1); g.moveTo(-1, 1); g.lineTo(1, -1); g.moveTo(8, 10); g.lineTo(10, 8); g.stroke();
        pat = ctx.createPattern(c, 'repeat');
        hatchCache.set(k, pat);
        return pat;
    }

    function washAlpha(sh, isSel) {
        switch (sh.kind) {
            case 'nation': case 'colony': return isSel ? 0.58 : 0.42;
            case 'frontier': return sh.status === 'mission' ? (isSel ? 0.62 : 0.4) : (isSel ? 0.7 : 0.5);
            default: return isSel ? 0.42 : 0.17;
        }
    }

    function render(newData) {
        data = newData || { shapes: [], selectedId: null };
        shapesCache = null;
        draw();
    }

    // ---- coordinate helpers ----
    function toScreen(px, py) { return [px * view.scale + view.ox, py * view.scale + view.oy]; }
    function toWorld(sx, sy) { return [(sx - view.ox) / view.scale, (sy - view.oy) / view.scale]; }

    // ---------------------------------------------------------------- drawing
    function ringPath(ring) {
        const [sx0, sy0] = toScreen(ring[0][0], ring[0][1]);
        ctx.moveTo(sx0, sy0);
        for (let i = 1; i < ring.length; i++) { const [sx, sy] = toScreen(ring[i][0], ring[i][1]); ctx.lineTo(sx, sy); }
        ctx.closePath();
    }

    function segPath(segs) {
        ctx.beginPath();
        for (const s of segs) {
            const [x0, y0] = toScreen(s[0], s[1]), [x1, y1] = toScreen(s[2], s[3]);
            ctx.moveTo(x0, y0); ctx.lineTo(x1, y1);
        }
    }

    function draw() {
        if (!ctx) return;
        const w = cssW(), h = cssH();
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        // Deep-sea backdrop, then the painted terrain of the world.
        ctx.fillStyle = '#0B1B33';
        ctx.fillRect(0, 0, w, h);
        if (terrainReady) {
            const [tx0, ty0] = toScreen(0, 0);
            const [tx1, ty1] = toScreen(WORLD_W, WORLD_H);
            ctx.drawImage(terrainImg, tx0, ty0, tx1 - tx0, ty1 - ty0);
        }

        // Country labels are laid out first (biggest wins, no overlaps); the sea labels then keep clear of them.
        const labels = layoutNationLabels();
        drawSeaLabels(labels.map(l => l.box));

        const shapes = getShapes();
        const selected = data.selectedId ?? data.SelectedId;

        // 0. islands the map paints onto open sea (the terrain image has no land there): a land base and a coast like the terrain's own
        for (const sh of shapes) {
            if (!sh.painted) continue;
            ctx.beginPath();
            for (const ring of sh.rings) ringPath(ring);
            ctx.fillStyle = 'rgb(196,160,98)';
            ctx.fill();
            if (terrainReady) {
                // the terrain's own land texture (a patch of the Sahara plain), clipped to the island, so it matches the painted continents
                ctx.save();
                ctx.clip();
                for (const ring of sh.rings) {
                    let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
                    for (const p of ring) { x0 = Math.min(x0, p[0]); y0 = Math.min(y0, p[1]); x1 = Math.max(x1, p[0]); y1 = Math.max(y1, p[1]); }
                    const bw = x1 - x0, bh = y1 - y0; if (bw < 1 || bh < 1) continue;
                    const [dx, dy] = toScreen(x0, y0);
                    ctx.drawImage(terrainImg, 330, 580, Math.min(bw, 220), Math.min(bh, 220), dx, dy, bw * view.scale, bh * view.scale);
                }
                ctx.restore();
            }
            ctx.lineJoin = 'round';
            ctx.lineWidth = Math.max(2.2, 2.6 * Math.min(view.scale, 1.5));
            ctx.strokeStyle = 'rgba(66,96,100,0.9)';
            ctx.stroke();
        }

        // 1. the washes: a translucent, muted colour over the terrain (neutral land and frontier isles are fainter)
        for (const sh of shapes) {
            const isSel = (sh.p.id ?? sh.p.Id) === selected;
            ctx.fillStyle = rgba(sh.rgb, washAlpha(sh, isSel));
            ctx.beginPath();
            for (const ring of sh.rings) ringPath(ring);
            ctx.fill();
            if (sh.kind === 'frontier' && sh.status === 'mission') { ctx.fillStyle = hatchFill(sh.rgb); ctx.fill(); }   // colonists on their way: hatched
        }

        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        // line weights ease off when the whole world is on screen, so borders never crowd the washes at small zoom
        const wBorder = Math.min(1.1, 0.5 + 0.7 * view.scale), wCoast = Math.min(0.8, 0.4 + 0.5 * view.scale);

        // 2. lines. Thin and the same everywhere; nothing is drawn twice.
        // coasts: a hair of the realm's own darkened colour, so the painted shoreline stays the terrain's own
        for (const sh of shapes) {
            if (!sh.coast.length) continue;
            segPath(sh.coast);
            ctx.lineWidth = wCoast;
            ctx.strokeStyle = rgba(darker(sh.rgb, 0.55), (sh.kind === 'nation' || sh.kind === 'colony') ? 0.55 : sh.kind === 'frontier' ? 0.5 : 0.3);
            ctx.stroke();
        }
        // lines between two parts of one realm (an annexed land beside its master): faint and dotted, once
        ctx.setLineDash([2, 3]);
        for (const sh of shapes) {
            if (!sh.inner.length) continue;
            segPath(sh.inner);
            ctx.lineWidth = 0.7;
            ctx.strokeStyle = rgba(darker(sh.rgb, 0.6), 0.32);
            ctx.stroke();
        }
        ctx.setLineDash([]);
        // borders between realms: one slightly darkened line, drawn once per shared edge.
        // Neutral regions (not crowns) get the same line, lighter. A settled colony is a crown's land: the crown's line.
        for (const kindSet of [['neutral'], ['nation', 'colony']]) {
            ctx.beginPath(); let any = false;
            for (const sh of shapes) {
                if (!kindSet.includes(sh.kind) || !sh.border.length) continue;
                for (const s of sh.border) { const [x0, y0] = toScreen(s[0], s[1]), [x1, y1] = toScreen(s[2], s[3]); ctx.moveTo(x0, y0); ctx.lineTo(x1, y1); any = true; }
            }
            if (!any) continue;
            const nation = kindSet[0] === 'nation';
            ctx.lineWidth = nation ? wBorder : wBorder * 0.75;
            ctx.strokeStyle = nation ? 'rgba(52,36,22,0.72)' : 'rgba(52,36,22,0.38)';
            ctx.stroke();
        }
        // the edges of colony regions nobody has settled: dashed, amber-brown (the claimant's colour once a mission is on its way)
        ctx.setLineDash([5, 4]);
        for (const sh of shapes) {
            if (!sh.claim.length) continue;
            segPath(sh.claim);
            ctx.lineWidth = Math.max(0.9, wBorder * 1.25);
            ctx.strokeStyle = sh.status === 'mission' ? rgba(darker(sh.rgb, 0.45), 0.85) : 'rgba(110,70,15,0.8)';
            ctx.stroke();
        }
        ctx.setLineDash([]);

        // 3. the selected realm's gold outline (its outer edge only: coasts and borders, never the lines inside the realm)
        for (const sh of shapes) {
            if ((sh.p.id ?? sh.p.Id) !== selected || !sh.outer.length) continue;
            segPath(sh.outer);
            ctx.lineWidth = 7; ctx.strokeStyle = 'rgba(255,215,0,0.22)'; ctx.stroke();
            ctx.lineWidth = 3.2; ctx.strokeStyle = 'rgba(52,38,24,0.85)'; ctx.stroke();
            ctx.lineWidth = 1.7; ctx.strokeStyle = '#FFD700'; ctx.stroke();
        }
        ctx.lineCap = 'butt';
        ctx.lineJoin = 'miter';

        paintNationLabels(labels);
        drawMissionProgress();
        drawAnnexedCities(labels.map(l => l.box));
    }

    // A small bar under the name of every colony region with colonists on the way: how far along, in the claimant's colour.
    function drawMissionProgress() {
        if (view.scale < 0.4) return;
        const w = cssW(), h = cssH();
        const fs = Math.max(9, 10 * Math.min(view.scale, 1.3));
        ctx.font = `${fs}px 'IM Fell English', Georgia, serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        for (const sh of getShapes()) {
            if (sh.kind !== 'frontier' || sh.status !== 'mission') continue;
            const lx = sh.p.labelX ?? sh.p.LabelX, ly = sh.p.labelY ?? sh.p.LabelY;
            if (lx === undefined) continue;
            const [sx, sy] = toScreen(lx, ly);
            if (sx < -60 || sx > w + 60 || sy < -30 || sy > h + 30) continue;
            const bw = 46, bh = 6, x = sx - bw / 2, y = sy + Math.max(10, 12 * Math.min(view.scale, 1.3)) / 2 + 5;
            ctx.fillStyle = 'rgba(30,22,14,0.75)'; ctx.fillRect(x - 1, y - 1, bw + 2, bh + 2);
            ctx.fillStyle = 'rgba(245,240,225,0.9)'; ctx.fillRect(x, y, bw, bh);
            ctx.fillStyle = rgba(darker(sh.rgb, 0.7), 1); ctx.fillRect(x, y, bw * sh.progress, bh);
            const text = Math.round(sh.progress * 100) + '%';
            ctx.lineWidth = 3; ctx.strokeStyle = 'rgba(245,240,225,0.85)'; ctx.strokeText(text, sx, y + bh + 2);
            ctx.fillStyle = '#2A2118'; ctx.fillText(text, sx, y + bh + 2);
        }
    }

    // The old capitals of annexed countries stay on the map as cities of the realm that swallowed them (shown once zoomed in).
    function drawAnnexedCities(avoid) {
        if (view.scale < 0.5) return;
        const fs = Math.max(9, 10.5 * Math.min(view.scale, 1.3));
        ctx.font = `italic ${fs}px 'IM Fell English', Georgia, serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        const w = cssW(), h = cssH();
        const taken = (avoid || []).slice();   // the places names already hold: a colony's settlement name keeps clear of them (its dot is still drawn)
        for (const sh of getShapes()) {
            const p = sh.p;
            const cities = p.annexedCities || p.AnnexedCities || [];
            for (const c of cities) {
                const name = c.name ?? c.Name;
                if (!name) continue;
                if (view.scale < (c.minScale ?? c.MinScale ?? 0)) continue;   // a colony's settlement shows only once zoomed in
                const [sx, sy] = toScreen(c.x ?? c.X, c.y ?? c.Y);
                if (sx < -60 || sx > w + 60 || sy < -30 || sy > h + 30) continue;
                ctx.beginPath();
                ctx.arc(sx, sy, 3, 0, Math.PI * 2);
                ctx.fillStyle = rgba(darker(sh.rgb, 0.5), 1);
                ctx.fill();
                ctx.lineWidth = 1;
                ctx.strokeStyle = 'rgba(245,240,225,0.9)';
                ctx.stroke();
                if ((c.minScale ?? c.MinScale ?? 0) > 0) {
                    const tw = ctx.measureText(name).width, box = { x0: sx - tw / 2 - 2, y0: sy + 3, x1: sx + tw / 2 + 2, y1: sy + 5 + fs + 2 };
                    if (taken.some(b => b.x0 < box.x1 && b.x1 > box.x0 && b.y0 < box.y1 && b.y1 > box.y0)) continue;
                    taken.push(box);
                }
                ctx.lineWidth = 3;
                ctx.strokeStyle = 'rgba(245,240,225,0.8)';
                ctx.strokeText(name, sx, sy + 5);
                ctx.fillStyle = '#3A2C1E';
                ctx.fillText(name, sx, sy + 5);
            }
        }
    }

    function ringArea(ring) {
        let a = 0;
        for (let i = 0; i < ring.length - 1; i++) a += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1];
        return Math.abs(a) / 2;
    }

    // Labels with collision detection: biggest territories win, the rest wait
    // until you zoom in. No more unreadable overlapping text.
    // Set in IM Fell English, the 17th-century typeface.
    function layoutNationLabels() {
        if (view.scale < 0.22) return [];
        const w = cssW(), h = cssH();
        const fs = Math.max(10, 12 * Math.min(view.scale, 1.3));
        ctx.font = `${fs}px 'IM Fell English', Georgia, serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        const cands = getShapes()
            .map(sh => { let area = 0; for (const r of sh.rings) area += ringArea(r); return { sh, area }; })
            .filter(c => {
                const lx = c.sh.p.labelX ?? c.sh.p.LabelX;
                return lx !== undefined && c.area * view.scale * view.scale > 900;
            })
            .sort((a, b) => b.area - a.area);

        const placed = [];
        for (const { sh } of cands) {
            const p = sh.p;
            const lx = p.labelX ?? p.LabelX, ly = p.labelY ?? p.LabelY;
            const [sx, sy] = toScreen(lx, ly);
            if (sx < -80 || sx > w + 80 || sy < -40 || sy > h + 40) continue;
            const label = (p.name ?? p.Name ?? '').toUpperCase();
            if (!label) continue;
            const tw = ctx.measureText(label).width;
            const box = { x0: sx - tw / 2 - 6, y0: sy - fs / 2 - 5, x1: sx + tw / 2 + 6, y1: sy + fs / 2 + 5 };
            if (placed.some(q => q.box.x0 < box.x1 && q.box.x1 > box.x0 && q.box.y0 < box.y1 && q.box.y1 > box.y0)) continue;
            placed.push({ label, sx, sy, box, fs });
        }
        return placed;
    }

    function paintNationLabels(placed) {
        if (!placed.length) return;
        ctx.font = `${placed[0].fs}px 'IM Fell English', Georgia, serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        for (const { label, sx, sy } of placed) {
            ctx.lineWidth = 3.2;
            ctx.strokeStyle = 'rgba(245,240,225,0.88)';
            ctx.strokeText(label, sx, sy);
            ctx.fillStyle = '#2A2118';
            ctx.fillText(label, sx, sy);
        }
    }

    function drawSeaLabels(avoid) {
        if (view.scale < 0.28) return;
        const fs = Math.max(10, 16 * Math.min(view.scale, 1.2));
        ctx.font = `italic ${fs}px 'IM Fell English', Georgia, serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        try { ctx.letterSpacing = '3px'; } catch (e) { /* older browsers */ }
        for (const [txt, wx, wy] of SEA_LABELS) {
            const [sx, sy] = toScreen(wx, wy);
            const tw = ctx.measureText(txt).width;
            // a sea name never sits under a country's name
            if ((avoid || []).some(b => b.x0 < sx + tw / 2 && b.x1 > sx - tw / 2 && b.y0 < sy + fs / 2 && b.y1 > sy - fs / 2)) continue;
            // Antique cartouche: dark umber ink on the parchment sea.
            ctx.lineWidth = 3;
            ctx.strokeStyle = 'rgba(235,225,200,0.5)';
            ctx.strokeText(txt, sx, sy);
            ctx.fillStyle = 'rgba(62,50,36,0.92)';
            ctx.fillText(txt, sx, sy);
        }
        try { ctx.letterSpacing = '0px'; } catch (e) { /* older browsers */ }
    }

    // ---------------------------------------------------------------- hit testing
    // Even-odd, so the zero-width bridge to an enclave or lake leaves the enclave out. The partition has no overlaps,
    // so at most one shape can contain a point (the last-drawn one wins, as before, should a hand-made shape ever overlap).
    function pointInRing(wx, wy, pts) {
        let inside = false;
        for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
            const xi = pts[i][0], yi = pts[i][1];
            const xj = pts[j][0], yj = pts[j][1];
            if (((yi > wy) !== (yj > wy)) &&
                (wx < (xj - xi) * (wy - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }

    function shapeAt(sx, sy) {
        const [wx, wy] = toWorld(sx, sy);
        const shapes = getShapes();
        for (let i = shapes.length - 1; i >= 0; i--) {
            const sh = shapes[i];
            for (const ring of sh.rings) {
                if (pointInRing(wx, wy, ring)) return sh.p.id ?? sh.p.Id;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- interaction
    function localPos(e) {
        const r = canvas.getBoundingClientRect();
        return [e.clientX - r.left, e.clientY - r.top];
    }

    function onDown(e) {
        canvas.setPointerCapture(e.pointerId);
        pointers.set(e.pointerId, localPos(e));
        if (pointers.size === 2) {
            const [a, b] = [...pointers.values()];
            pinchStart = { dist: Math.hypot(a[0] - b[0], a[1] - b[1]), scale: view.scale };
            downInfo = null;
        } else {
            const [x, y] = localPos(e);
            downInfo = { x, y, time: Date.now(), moved: false, ox: view.ox, oy: view.oy };
        }
    }

    function onMove(e) {
        if (!pointers.has(e.pointerId)) return;
        pointers.set(e.pointerId, localPos(e));

        if (pointers.size === 2 && pinchStart) {
            const [a, b] = [...pointers.values()];
            const dist = Math.hypot(a[0] - b[0], a[1] - b[1]);
            if (dist > 0) zoomAt((a[0] + b[0]) / 2, (a[1] + b[1]) / 2,
                pinchStart.scale * (dist / pinchStart.dist));
            return;
        }

        if (downInfo) {
            const [x, y] = localPos(e);
            const dx = x - downInfo.x, dy = y - downInfo.y;
            if (Math.hypot(dx, dy) > 8) downInfo.moved = true;
            view.ox = downInfo.ox + dx;
            view.oy = downInfo.oy + dy;
            clampView();
            draw();
        }
    }

    function onUp(e) {
        pointers.delete(e.pointerId);
        if (pointers.size < 2) pinchStart = null;

        if (downInfo && !downInfo.moved && Date.now() - downInfo.time < 500) {
            const [x, y] = localPos(e);
            const id = shapeAt(x, y);
            if (id && dotNetRef) dotNetRef.invokeMethodAsync('OnMapTapped', id);
        }
        downInfo = null;
    }

    function onWheel(e) {
        e.preventDefault();
        const [x, y] = localPos(e);
        const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
        zoomAt(x, y, view.scale * factor);
    }

    function fitScale() {
        return Math.min(cssW() / WORLD_W, cssH() / WORLD_H);
    }

    // Keep the map inside the viewport: no black voids when panning,
    // and never zoom out past the fit-to-screen level.
    function clampView() {
        const minS = fitScale();
        if (view.scale < minS) view.scale = minS;
        if (view.scale > 8) view.scale = 8;
        const mapW = WORLD_W * view.scale, mapH = WORLD_H * view.scale;
        const w = cssW(), h = cssH();
        if (mapW <= w) view.ox = (w - mapW) / 2;
        else view.ox = Math.min(0, Math.max(w - mapW, view.ox));
        if (mapH <= h) view.oy = (h - mapH) / 2;
        else view.oy = Math.min(0, Math.max(h - mapH, view.oy));
    }

    function zoomAt(cx, cy, newScale) {
        newScale = Math.min(8, Math.max(fitScale(), newScale));
        const [wx, wy] = toWorld(cx, cy);
        view.scale = newScale;
        view.ox = cx - wx * newScale;
        view.oy = cy - wy * newScale;
        clampView();
        draw();
    }

    // Exposed for the headless checks (tools/mapgen/maptest.js) and for debugging: what the renderer decided about every edge.
    function inspect() {
        return getShapes().map(sh => ({ id: sh.p.id ?? sh.p.Id, kind: sh.kind, status: sh.status, realm: sh.realm, rings: sh.rings.length, coast: sh.coast.length, border: sh.border.length, inner: sh.inner.length, outer: sh.outer.length, claim: sh.claim.length }));
    }

    // Centre the view on a map point at a zoom (clamped like any other zoom); for debugging and the headless checks.
    function focusOn(wx, wy, scale) {
        view.scale = scale;
        view.ox = cssW() / 2 - wx * view.scale;
        view.oy = cssH() / 2 - wy * view.scale;
        clampView();
        draw();
    }

    return { init, render, inspect, focusOn, mutedRgb: muted, shapeColors: () => getShapes().map(sh => ({ id: sh.p.id ?? sh.p.Id, kind: sh.kind, rgb: sh.rgb })), shapeAtWorld: (wx, wy) => { const shapes = getShapes(); for (let i = shapes.length - 1; i >= 0; i--) for (const r of shapes[i].rings) if (pointInRing(wx, wy, r)) return shapes[i].p.id ?? shapes[i].p.Id; return null; } };
})();
