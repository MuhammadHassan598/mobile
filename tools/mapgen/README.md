# mapgen — the political map of the 1600 world

The countries, neutral territories and frontier regions of the map are **one planar partition of the land of
`src/EmpireSim/wwwroot/images/world-terrain.jpg`** (2200 × 1150 map units). This folder is the tool that makes it. Its result is committed as
`src/EmpireSim.Core/Models/WorldGeometry.Data.cs` (generated — do not edit by hand); the game reads polygons from there
(`WorldGeometry.Nation(id)`, `.Neutral(name)`, `.Frontier(name)`).

What the partition guarantees (checked by `validate.js` and by section 74 of `tests/SimTests`):

* **Coastlines are the terrain image's own.** A land mask is read off the image, so a border can never put a country into the sea.
* **Neighbours share exactly the same points.** Every border between two realms is one list of points, used by both. There are no gaps and no
  overlaps, and no corner of one realm lies on another's edge.
* **Natural lines.** Borders are simplified, smoothed and given a deterministic fractal meander (once per border, so both neighbours get the
  identical result). Nothing is a rectangle, a regular polygon or a ruler-straight line.
* **Holes are rings.** An enclave, a lake or an inner sea (the Aegean) is joined to its realm's outline by a zero-width bridge, so a polygon is
  still one plain list of points: it fills correctly (non-zero rule), is hit-tested correctly (even-odd) and the renderer never draws the bridge.
* **Who owns what is preserved.** The ownership comes from `data/world.json`, the polygons of the first stylised map (as they were in
  `GameState.cs` at commit `031284f`), laid onto the real land; overlaps between old shapes and gaps between them are resolved by rule.
  The few places where the old shapes were boxes, octagons or simply in the wrong place are redrawn by hand in `regions/*.json`.

## Making it

```
node build.js --overrides regions/all.json      # -> out/geometry.json (+ companions)
node validate.js                                # structure checks; must print GEOMETRY VALID
node emit.js ../../src/EmpireSim.Core/Models/WorldGeometry.Data.cs 2   # the C# data; the last argument is WorldGeometry.Version
node maptest.js                                 # the renderer's logic (wwwroot/js/map.js) on the real geometry
```

Everything is deterministic: building twice gives byte-identical output. If you change a polygon, **bump the version** (`emit.js … <n>`):
`WorldGeometry.Upgrade` then redraws the map of older saves on load (ownership is not touched).

`mask.ps1` (Windows PowerShell, System.Drawing, nothing to install) re-reads the land mask from the terrain image; the result is kept in
`data/land-mask.json`, run-length encoded, so the rest needs only Node. `render.ps1` draws the geometry over the terrain with a coordinate
grid and the owner names, to see what you are doing; `geo2shapes.js` makes its input.

## Authoring a region

`regions/*.json` are applied in order (see `all.json`). A file may contain:

| key | meaning |
|---|---|
| `claims` | `{ owner, polygons, only?, exact? }` — paint polygons for an owner (`id`, `n:<neutral>` or `f:<frontier>`). They may overshoot into the sea: the coast mask trims them. `only` restricts what they may overwrite (`"__unowned__"` = land nobody owns yet). Edges get fractal wobble unless `exact`. |
| `replace` | owners whose old shape is dropped before painting (the octagons) |
| `drop` | remove an owner's old polygons lying inside a rectangle |
| `carve` | polygons that become sea again (a strait) |
| `islands` | `{ owner, seed, parts: [[x, y, rx, ry, rotation]] }` — organic islands painted onto open sea (Micronesia, Easter Island, the frontier archipelagos); the map flags these as "painted" so the renderer gives them a land texture and a coast |
| `extraNeutrals` | neutral territories that did not exist before (Greenland, Svalbard) |
| `capitals` | `{ id: { name, x, y } }` — move a capital explicitly; any other capital that the coast leaves in the sea is moved to its nearest own land (a few units) |
| `include` | other region files |

Coordinates are map units, read off the terrain image (use `render.ps1 -Grid`).
