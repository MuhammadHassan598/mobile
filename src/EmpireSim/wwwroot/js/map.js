/* EmpireSim world map: canvas rendering with pan, zoom and tap-to-select.
   Shapes arrive from .NET as { shapes: [...], selectedId } where each shape
   is { id, name, color, polygons: [[{x,y}...], ...], labelX, labelY }.
   A shape is one country (possibly several polygons), a frontier region or
   a neutral territory. Property names may arrive camelCase or PascalCase. */

window.empireMap = (() => {
    const WORLD_W = 2200, WORLD_H = 1150;
    let canvas = null, ctx = null, dotNetRef = null;
    let data = { shapes: [], selectedId: null };
    let view = { scale: 1, ox: 0, oy: 0 };
    let dpr = 1;

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

    function init(canvasId, ref) {
        canvas = document.getElementById(canvasId);
        if (!canvas) return false;
        dotNetRef = ref;
        ctx = canvas.getContext('2d');

        // Start fitted to the whole world.
        const s = Math.min(cssW() / WORLD_W, cssH() / WORLD_H) || 1;
        view.scale = s;
        view.ox = (cssW() - WORLD_W * s) / 2;
        view.oy = (cssH() - WORLD_H * s) / 2;

        new ResizeObserver(resize).observe(canvas);
        canvas.addEventListener('pointerdown', onDown);
        canvas.addEventListener('pointermove', onMove);
        canvas.addEventListener('pointerup', onUp);
        canvas.addEventListener('pointercancel', onUp);
        canvas.addEventListener('wheel', onWheel, { passive: false });
        canvas.style.touchAction = 'none';
        resize();
        return true;
    }

    function render(newData) {
        data = newData || { shapes: [], selectedId: null };
        draw();
    }

    // ---- coordinate helpers ----
    function toScreen(px, py) { return [px * view.scale + view.ox, py * view.scale + view.oy]; }
    function toWorld(sx, sy) { return [(sx - view.ox) / view.scale, (sy - view.oy) / view.scale]; }

    function poly(p) { return p.polygon || p.Polygon || []; }
    function polys(p) { return p.polygons || p.Polygons || [poly(p)]; }
    function ptX(pt) { return Array.isArray(pt) ? pt[0] : (pt.x ?? pt.X); }
    function ptY(pt) { return Array.isArray(pt) ? pt[1] : (pt.y ?? pt.Y); }

    // ---- drawing ----
    function draw() {
        if (!ctx) return;
        const w = cssW(), h = cssH();
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        // Sea background.
        ctx.fillStyle = '#0B1B33';
        ctx.fillRect(0, 0, w, h);

        for (const p of (data.shapes || [])) {
            const isSel = (p.id ?? p.Id) === (data.selectedId ?? data.SelectedId);
            for (const pts of polys(p)) {
                if (pts.length < 3) continue;
                ctx.beginPath();
                const [sx0, sy0] = toScreen(ptX(pts[0]), ptY(pts[0]));
                ctx.moveTo(sx0, sy0);
                for (let i = 1; i < pts.length; i++) {
                    const [sx, sy] = toScreen(ptX(pts[i]), ptY(pts[i]));
                    ctx.lineTo(sx, sy);
                }
                ctx.closePath();

                ctx.globalAlpha = 0.9;
                ctx.fillStyle = p.color || p.Color || '#555';
                ctx.fill();
                ctx.globalAlpha = 1;

                ctx.lineWidth = isSel ? 3 : 1.25;
                ctx.strokeStyle = isSel ? '#C9A227' : '#0E1626';
                ctx.stroke();
            }

            // Label (skip when zoomed far out and text would be tiny).
            if (view.scale > 0.25) {
                const lx = p.labelX ?? p.LabelX, ly = p.labelY ?? p.LabelY;
                if (lx !== undefined) {
                    const [lsx, lsy] = toScreen(lx, ly);
                    ctx.font = `${Math.max(10, 13 * Math.min(view.scale, 1.4))}px Georgia, serif`;
                    ctx.textAlign = 'center';
                    ctx.textBaseline = 'middle';
                    ctx.fillStyle = 'rgba(0,0,0,0.55)';
                    ctx.fillText(p.name ?? p.Name ?? '', lsx + 1, lsy + 1);
                    ctx.fillStyle = '#EDE6D6';
                    ctx.fillText(p.name ?? p.Name ?? '', lsx, lsy);
                }
            }
        }
    }

    // ---- hit testing ----
    function pointInPoly(wx, wy, pts) {
        let inside = false;
        for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
            const xi = ptX(pts[i]), yi = ptY(pts[i]);
            const xj = ptX(pts[j]), yj = ptY(pts[j]);
            if ((yi > wy) !== (yj > wy) &&
                wx < ((xj - xi) * (wy - yi)) / (yj - yi) + xi) {
                inside = !inside;
            }
        }
        return inside;
    }

    function shapeAt(sx, sy) {
        const [wx, wy] = toWorld(sx, sy);
        const shapes = data.shapes || [];
        for (let i = shapes.length - 1; i >= 0; i--) {
            for (const pts of polys(shapes[i])) {
                if (pts.length >= 3 && pointInPoly(wx, wy, pts))
                    return shapes[i].id ?? shapes[i].Id;
            }
        }
        return null;
    }

    // ---- interaction ----
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

    function zoomAt(cx, cy, newScale) {
        newScale = Math.min(8, Math.max(0.2, newScale));
        const [wx, wy] = toWorld(cx, cy);
        view.scale = newScale;
        view.ox = cx - wx * newScale;
        view.oy = cy - wy * newScale;
        draw();
    }

    return { init, render };
})();
