// Generates the flags of the 41 countries: src/EmpireSim.Core/Models/Flags.Data.cs (inline SVG, 3:2) and a contact sheet for looking at them.
//   node make-flags.js                 -> writes the C# data and out/flags-sheet.html
//   node make-flags.js --sheet <file>  -> also writes the contact sheet to <file>
// The flags are simplified banners of c. 1600, drawn with a handful of primitives (no text, no images, no ids): every flag fits a 300 x 200 box and the
// game shows the middle of it inside a round emblem (preserveAspectRatio "slice"), so the charges sit in the centre. Where a country had no established
// flag in 1600 (Micronesia, Easter Island, several Asian courts) the banner is an emblem-style design in its own colours, not a borrowed flag.
'use strict';
const fs = require('fs'), path = require('path');

// ---------------------------------------------------------------- primitives (300 x 200)
const f1 = n => +n.toFixed(1);
const R = (x, y, w, h, fill) => `<rect x='${f1(x)}' y='${f1(y)}' width='${f1(w)}' height='${f1(h)}' fill='${fill}'/>`;
const C = (cx, cy, r, fill, extra = '') => `<circle cx='${f1(cx)}' cy='${f1(cy)}' r='${f1(r)}' fill='${fill}'${extra ? ' ' + extra : ''}/>`;
const POLY = (pts, fill, extra = '') => `<polygon points='${pts.map(p => f1(p[0]) + ',' + f1(p[1])).join(' ')}' fill='${fill}'${extra ? ' ' + extra : ''}/>`;
const PATH = (d, fill, extra = '') => `<path d='${d}' fill='${fill}'${extra ? ' ' + extra : ''}/>`;
const LINE = (x1, y1, x2, y2, stroke, w, cap = 'butt') => `<line x1='${f1(x1)}' y1='${f1(y1)}' x2='${f1(x2)}' y2='${f1(y2)}' stroke='${stroke}' stroke-width='${w}' stroke-linecap='${cap}'/>`;
const G = (transform, body) => `<g transform='${transform}'>${body}</g>`;
function starPts(cx, cy, r, n, ratio, rot = -90) {
  const pts = [];
  for (let i = 0; i < n * 2; i++) { const rr = i % 2 === 0 ? r : r * ratio; const a = (rot + i * 180 / n) * Math.PI / 180; pts.push([cx + rr * Math.cos(a), cy + rr * Math.sin(a)]); }
  return pts;
}
const star = (cx, cy, r, n, fill, ratio = 0.382, rot = -90) => POLY(starPts(cx, cy, r, n, ratio, rot), fill);
const crescent = (cx, cy, r, fill, field, open = 1) => C(cx, cy, r, fill) + C(cx + open * r * 0.3, cy, r * 0.8, field);
function sun(cx, cy, r, fill, rays = 16, inner = 1.25, outer = 1.9) {
  let s = '';
  for (let i = 0; i < rays; i++) {
    const a = i * 2 * Math.PI / rays, w = Math.PI / rays * 0.55;
    s += POLY([[cx + r * inner * Math.cos(a - w), cy + r * inner * Math.sin(a - w)], [cx + r * outer * Math.cos(a), cy + r * outer * Math.sin(a)], [cx + r * inner * Math.cos(a + w), cy + r * inner * Math.sin(a + w)]], fill);
  }
  return s + C(cx, cy, r, fill);
}
const saltire = (fill, t = 34) => LINE(0, 0, 300, 200, fill, t) + LINE(0, 200, 300, 0, fill, t);
const nordic = (cx, fill, t = 34) => R(cx - t / 2, 0, t, 200, fill) + R(0, 100 - t / 2, 300, t, fill);

// fleur-de-lis, drawn in a 100 x 100 box
const FLEUR = (fill) => PATH('M50 4C62 22 64 40 50 58C36 40 38 22 50 4Z', fill)
  + PATH('M46 56C34 46 14 38 8 52C4 64 16 74 30 68C26 64 28 60 34 58C38 58 42 58 46 56Z', fill)
  + G('translate(100 0) scale(-1 1)', PATH('M46 56C34 46 14 38 8 52C4 64 16 74 30 68C26 64 28 60 34 58C38 58 42 58 46 56Z', fill))
  + R(30, 64, 40, 9, fill) + PATH('M50 73L40 97L50 89L60 97Z', fill);
const fleur = (x, y, s, fill) => G(`translate(${x - 50 * s} ${y - 50 * s}) scale(${s})`, FLEUR(fill));

// eagles, drawn in a 100 x 100 box centred on (0,0); one half is mirrored into the other
const WING_AND_TAIL = [[-30, -29], [-42, -35], [-50, -26], [-47, -19], [-50, -14], [-44, -10], [-46, -3], [-39, -1], [-40, 6], [-32, 5], [-31, 12], [-24, 10], [-20, 16], [-24, 24], [-31, 30], [-24, 31], [-18, 27], [-14, 33], [-13, 44], [-7, 40], [-4, 48], [0, 43]];
const mirror = pts => pts.map(p => [-p[0], p[1]]);
function doubleEagle(fill) {
  const left = [[0, -26], [-5, -33], [-8, -44], [-13, -49], [-21, -47], [-30, -41], [-22, -39], [-17, -35], [-18, -27], ...WING_AND_TAIL.slice(0, -1), [0, 43]];
  const right = mirror(left.slice(1, -1)).reverse();
  return POLY([...left, ...right], fill);
}
function singleEagle(fill) {
  const down = WING_AND_TAIL;                       // the left side, from the shoulder to the tail centre
  const rightSide = mirror(WING_AND_TAIL.slice(0, -1));
  const head = [[2, -50], [10, -47], [20, -42], [11, -39], [7, -35], [7, -27]];
  const pts = [...head, ...rightSide, [0, 43], ...WING_AND_TAIL.slice(0, -1).reverse(), [-7, -27], [-6, -35], [-6, -40], [-4, -46]];
  return POLY(pts, fill);
}
const placed = (x, y, s, body) => G(`translate(${x} ${y}) scale(${s})`, body);

// winged lion of St Mark, a gold silhouette in a 100 x 100 box
const LION = (fill) =>
  PATH('M-30 6C-30 -8 -10 -12 8 -10C26 -8 34 -2 34 8C34 16 28 20 22 20L22 38L13 38L13 24L-8 24L-8 38L-18 38L-18 22C-26 20 -30 14 -30 6Z', fill)
  + C(26, -12, 15, fill) + PATH('M34 -4L48 0L40 8Z', fill)
  + PATH('M-30 6C-46 2 -50 -16 -40 -28C-46 -12 -38 -2 -28 -2Z', fill)
  + PATH('M-10 -10C-24 -44 10 -58 30 -50C14 -46 8 -32 16 -8Z', fill)
  + PATH('M-4 -12C-18 -34 2 -46 18 -44C6 -38 4 -26 10 -10Z', 'rgba(0,0,0,0.28)');

// crossed keys of the Papal States, in a 100 x 100 box
const KEY = (fill) => R(-3.5, -34, 7, 76, fill) + C(0, -40, 11, 'none', `stroke='${fill}' stroke-width='6'`) + R(3, 28, 14, 6, fill) + R(3, 38, 10, 6, fill);
const KEYS = (a, b) => G('rotate(40)', KEY(a)) + G('rotate(-40)', KEY(b));

// taegeuk: a circle of two commas
const taegeuk = (cx, cy, r, top, bottom, rot = -33) => G(`rotate(${rot} ${cx} ${cy})`,
  C(cx, cy, r, bottom) + PATH(`M${cx - r} ${cy}A${r} ${r} 0 0 1 ${cx + r} ${cy}A${r / 2} ${r / 2} 0 0 1 ${cx} ${cy}A${r / 2} ${r / 2} 0 0 0 ${cx - r} ${cy}Z`, top));

// ---------------------------------------------------------------- the flags
const RED = '#C8102E', GOLD = '#E8B923', CREAM = '#F4ECD0', WHITE = '#FFFFFF', NAVY = '#0B2A6B', GREEN = '#1B7F4B', BLUE = '#1F5FAE';
const flags = [];
const add = (id, name, body, note) => flags.push({ id, name, body, note });

add('ottoman', 'Ottoman Empire', R(0, 0, 300, 200, RED) + crescent(138, 100, 56, WHITE, RED) + star(178, 100, 22, 5, WHITE), 'red field, white crescent and star');
add('spain', 'Iberian Union', R(0, 0, 300, 200, WHITE) + saltire(RED, 34), 'Cross of Burgundy of the Habsburg fleets');
add('france', 'France', R(0, 0, 300, 200, NAVY) + fleur(150, 56, 0.62, GOLD) + fleur(104, 142, 0.62, GOLD) + fleur(196, 142, 0.62, GOLD), 'blue field, three golden fleurs-de-lis');
add('england', 'England', R(0, 0, 300, 200, WHITE) + nordic(150, RED, 38), "St George's Cross");
add('dutch', 'Netherlands', R(0, 0, 300, 67, '#E1731E') + R(0, 67, 300, 66, WHITE) + R(0, 133, 300, 67, '#21468B'), "the Prince's flag: orange, white, blue");
add('austria', 'Habsburg Monarchy', R(0, 0, 300, 200, RED) + R(0, 66, 300, 68, WHITE), 'red, white, red of Austria');
add('poland', 'Polish-Lithuanian Commonwealth', R(0, 0, 300, 200, RED) + placed(150, 100, 1.55, singleEagle(WHITE)), 'white eagle on red');
add('russia', 'Russia', R(0, 0, 300, 200, '#B4141E') + placed(150, 100, 1.6, doubleEagle(GOLD)), 'golden double-headed eagle on red');
add('sweden', 'Sweden', R(0, 0, 300, 200, '#1F5FAE') + nordic(105, '#F2C318', 36), 'blue field, golden Nordic cross');
add('venice', 'Venice', (() => { const bg = '#B3202A'; let s = R(0, 0, 300, 200, bg); const wing = PATH('M150 96C112 50 56 52 40 78C70 70 96 84 118 112Z', GOLD) + PATH('M150 96C188 50 244 52 260 78C230 70 204 84 182 112Z', GOLD); s += wing; for (let i = 0; i < 18; i++) { const a = i * 2 * Math.PI / 18, w = 0.17; s += POLY([[150 + 44 * Math.cos(a - w), 108 + 44 * Math.sin(a - w)], [150 + 66 * Math.cos(a), 108 + 66 * Math.sin(a)], [150 + 44 * Math.cos(a + w), 108 + 44 * Math.sin(a + w)]], GOLD); } s += C(150, 108, 46, GOLD) + C(150, 108, 32, '#F6D65A'); s += C(138, 100, 4.5, bg) + C(162, 100, 4.5, bg) + POLY([[143, 112], [157, 112], [150, 122]], bg) + PATH('M150 122V130M150 130C144 138 138 136 134 132M150 130C156 138 162 136 166 132', 'none', "stroke='" + bg + "' stroke-width='3.2' stroke-linecap='round'"); return s; })(), 'winged lion of St Mark (head and wings) on red');
add('persia', 'Iran', R(0, 0, 300, 200, '#1F7A3E') + sun(150, 100, 34, GOLD, 16), 'green banner with the golden sun');
add('mughal', 'Mughal Empire', R(0, 0, 300, 200, '#A8261C') + sun(150, 100, 34, GOLD, 12) + C(150, 100, 14, '#A8261C'), 'red banner with the golden sun');
add('ming', 'Ming Dynasty', R(0, 0, 300, 200, '#F1BF1C') + C(112, 100, 44, '#C8102E') + crescent(208, 100, 40, '#C8102E', '#F1BF1C', -1), 'imperial yellow with the red sun and moon (the two halves of the name)');
add('japan', 'Japan', R(0, 0, 300, 200, WHITE) + C(150, 100, 58, '#BC002D'), 'red disc on white');
add('kazakh', 'Kazakh Khanate', R(0, 0, 300, 200, '#3C93C8') + sun(150, 100, 30, GOLD, 24, 1.3, 1.75), 'sky-blue banner with the golden sun');
add('morocco', 'Morocco', R(0, 0, 300, 200, '#C1272D') + POLY(starPts(150, 100, 52, 5, 0.382), 'none', `stroke='${GREEN}' stroke-width='9' stroke-linejoin='miter'`), 'red banner with the green pentagram');
add('denmark', 'Denmark-Norway', R(0, 0, 300, 200, '#C8102E') + nordic(105, WHITE, 36), 'the Dannebrog');
add('crimea', 'Crimean Khanate', R(0, 0, 300, 200, '#4DB1D4') + G('translate(150 100)', PATH('M-34 -52V-18C-34 6 34 6 34 -18V-52', 'none', `stroke='${GOLD}' stroke-width='11' stroke-linecap='round' stroke-linejoin='round'`) + LINE(0, -62, 0, 58, GOLD, 11, 'round') + LINE(-26, 36, 26, 36, GOLD, 11, 'round')), 'sky-blue banner with the golden Giray tamga');
add('ethiopia', 'Ethiopia', R(0, 0, 300, 67, '#2F9E44') + R(0, 67, 300, 66, '#F6D21E') + R(0, 133, 300, 67, '#D62B2B'), 'green, yellow, red');
add('siam', 'Thailand', R(0, 0, 300, 200, '#B01E2D') + C(150, 100, 40, 'none', `stroke='${WHITE}' stroke-width='7'`) + [0, 1, 2, 3, 4, 5, 6, 7].map(i => LINE(150, 100, 150 + 40 * Math.cos(i * Math.PI / 4), 100 + 40 * Math.sin(i * Math.PI / 4), WHITE, 6)).join('') + C(150, 100, 9, WHITE), 'red banner with the white wheel');
add('korea', 'Korea', R(0, 0, 300, 200, WHITE) + taegeuk(150, 100, 50, '#CD2E3A', '#0047A0'), 'white field, red and blue taegeuk');
add('bukhara', 'Bukhara', R(0, 0, 300, 200, '#1C7F6B') + star(150, 100, 56, 8, WHITE, 0.55, -90) + C(150, 100, 20, '#1C7F6B'), 'green banner with the white octagram');
add('hre', 'Holy Roman Empire', R(0, 0, 300, 200, '#F2C318') + placed(150, 100, 1.7, doubleEagle('#1A1A1A')), 'black double-headed eagle on gold');
add('scotland', 'Scotland', R(0, 0, 300, 200, '#0A56A8') + saltire(WHITE, 42), "St Andrew's Cross");
add('genoa', 'Genoa', R(0, 0, 300, 200, WHITE) + nordic(150, RED, 38), "St George's Cross of the Republic of Genoa");
add('papal', 'Papal States', R(0, 0, 150, 200, '#F2C318') + R(150, 0, 150, 200, WHITE) + placed(150, 104, 1.85, KEYS('#B8860B', '#8E949B')), 'gold and white with the crossed keys');
add('italy', 'Italy', R(0, 0, 300, 200, '#C8102E') + nordic(150, WHITE, 40), 'the white cross of Savoy and Piedmont');
add('croatia', 'Croatia', (() => { let s = R(0, 0, 300, 200, WHITE); for (let r = 0; r < 4; r++) for (let c = 0; c < 6; c++) if ((r + c) % 2 === 0) s += R(c * 50, r * 50, 50, 50, '#D3202F'); return s; })(), 'the red and white chequy');
add('vietnam', 'Vietnam', R(0, 0, 300, 200, '#E9BC22') + `<rect x='12' y='12' width='276' height='176' fill='none' stroke='#C8102E' stroke-width='12'/>` + star(150, 100, 34, 5, '#C8102E'), 'yellow banner with a red border and star');
add('burma', 'Burma', R(0, 0, 300, 200, '#8E1B2C') + (() => { let s = ''; const cx = 150, cy = 150, n = 9; for (let i = 0; i < n; i++) { const a0 = Math.PI + i * Math.PI / n, a1 = a0 + Math.PI / n, r = 112; s += POLY([[cx, cy], [cx + r * Math.cos(a0), cy + r * Math.sin(a0)], [cx + r * Math.cos(a1), cy + r * Math.sin(a1)]], i % 2 ? '#F2C318' : '#E8A21A'); } return s + C(cx, cy, 26, '#F2C318'); })(), 'red banner with the golden peacock fan');
add('ahom', 'Ahom Kingdom', R(0, 0, 300, 200, '#1A5276') + C(150, 100, 52, GOLD) + C(150, 100, 38, '#1A5276') + C(150, 100, 22, GOLD), 'blue banner with the golden ringed sun');
add('yuan', 'Northern Yuan', R(0, 0, 300, 200, '#2A5CAA') + C(150, 108, 40, GOLD) + crescent(150, 52, 22, WHITE, '#2A5CAA', 1), 'blue banner with the golden sun below the white moon');
add('nepal', 'Nepal', R(0, 0, 300, 200, '#DC143C') + `<rect x='9' y='9' width='282' height='182' fill='none' stroke='#003893' stroke-width='18'/>` + crescent(150, 62, 26, WHITE, '#DC143C', 1) + sun(150, 142, 18, WHITE, 12, 1.25, 1.65), 'crimson banner with a blue border, moon and sun');
add('kongo', 'Kongo', R(0, 0, 300, 200, '#0F6B57') + nordic(150, WHITE, 30) + C(150, 100, 16, '#0F6B57'), 'green banner with the white cross');
add('jurchens', 'Jianzhou Jurchens', R(0, 0, 300, 200, '#F2C318'), 'the plain yellow banner of the Eight Banners');
add('cambodia', 'Cambodia', R(0, 0, 300, 200, '#C8102E') + (() => { const T = (x, y, s) => G(`translate(${x} ${y}) scale(${s})`, PATH('M-12 42V14H-7V-2H-2V-20L0 -42L2 -20V-2H7V14H12V42Z', WHITE)); return T(150, 96, 1.6) + T(100, 120, 1.1) + T(200, 120, 1.1); })(), 'red banner with the white towers of Angkor');
add('laos', 'Laos', R(0, 0, 300, 200, '#B8242C') + (() => { const P = (x, y, s) => G(`translate(${x} ${y}) scale(${s})`, PATH('M-44 8A44 40 0 0 1 44 8Q33 18 22 8Q11 18 0 8Q-11 18 -22 8Q-33 18 -44 8Z', WHITE) + R(-3, 8, 6, 52, WHITE) + C(0, -34, 6, WHITE)); return P(150, 78, 1.35) + P(78, 128, 0.7) + P(222, 128, 0.7); })(), 'red banner with white parasols');
add('malaysia', 'Malaysia', R(0, 0, 300, 200, '#E6B422') + crescent(142, 100, 54, '#1F7A3E', '#E6B422') + star(176, 100, 19, 5, '#1F7A3E'), 'yellow banner with the green crescent and star');
add('uae', 'United Arab Emirates', R(0, 0, 300, 200, '#C8102E') + POLY([[0, 0], [70, 0], [100, 25], [70, 50], [100, 75], [70, 100], [100, 125], [70, 150], [100, 175], [70, 200], [0, 200]], WHITE) + R(0, 0, 40, 200, WHITE), 'red banner with the white hoist');
add('micronesia', 'Micronesia', R(0, 0, 300, 200, '#4A97D8') + star(150, 52, 24, 5, WHITE) + star(150, 148, 24, 5, WHITE) + star(100, 100, 24, 5, WHITE) + star(200, 100, 24, 5, WHITE), 'sea-blue banner with four white stars');
add('easter', 'Easter Island', R(0, 0, 300, 200, WHITE) + PATH('M62 138C84 58 216 58 238 138C216 100 84 100 62 138Z', '#C0392B') + C(62, 138, 11, '#C0392B') + C(238, 138, 11, '#C0392B'), 'white banner with the red reimiro');

// a neutral emblem for a country without a flag of its own: a grey pennant, no letters
const FALLBACK = `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 300 200' preserveAspectRatio='xMidYMid slice' class='flag-svg flag-fallback' role='img' aria-label='No flag' focusable='false'>`
  + R(0, 0, 300, 200, '#5F646B') + POLY([[104, 40], [212, 40], [212, 160], [158, 128], [104, 160]], '#D9D3C1') + POLY([[120, 54], [196, 54], [196, 134], [158, 112], [120, 134]], '#8A8F96') + `</svg>`;

// ---------------------------------------------------------------- output
const svgOf = f => `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 300 200' preserveAspectRatio='xMidYMid slice' class='flag-svg' role='img' aria-label='Flag of ${f.name}' focusable='false'>${f.body}</svg>`;
const lines = [];
const P_ = s => lines.push(s);
P_('// <auto-generated>');
P_('// Generated by tools/flags/make-flags.js. Do not edit by hand: change the designs there and run it again.');
P_('// </auto-generated>');
P_('namespace EmpireSim.Core.Models;');
P_('');
P_('public static partial class FlagCatalog');
P_('{');
P_('    /// <summary>The flag of every country, as inline SVG (3:2, no text, no images): simplified banners of about 1600.</summary>');
P_('    private static readonly Dictionary<string, string> Svgs = new()');
P_('    {');
for (const f of flags) P_(`        ["${f.id}"] = "${svgOf(f)}",   // ${f.name}: ${f.note}`);
P_('    };');
P_('');
P_('    /// <summary>A neutral emblem (a grey pennant, no letters) for a country that has no flag.</summary>');
P_(`    public const string FallbackSvg = "${FALLBACK}";`);
P_('}');
const out = path.resolve(__dirname, '../../src/EmpireSim.Core/Models/Flags.Data.cs');
fs.writeFileSync(out, lines.join('\r\n') + '\r\n');
console.log('wrote', out, (fs.statSync(out).size / 1024).toFixed(0) + ' KB,', flags.length, 'flags');

// ---------------------------------------------------------------- contact sheet (for looking at them)
const sheetArg = process.argv.indexOf('--sheet');
const sheetFile = sheetArg > 0 ? process.argv[sheetArg + 1] : path.resolve(__dirname, 'out/flags-sheet.html');
fs.mkdirSync(path.dirname(sheetFile), { recursive: true });
const compact = process.argv.includes('--compact');
const inner = f => svgOf(f).replace("class='flag-svg'", "class='flag-svg' style='width:100%;height:100%;display:block'");
const cell = f => compact
  ? `<div class='c'><div class='o'>${inner(f)}</div><small>${f.id}</small></div>`
  : `<div class='c'><div class='r'>${inner(f)}</div><div class='o'>${inner(f)}</div><small>${f.id}</small></div>`;
fs.writeFileSync(sheetFile, `<!doctype html><meta charset='utf-8'><title>Flags</title><style>body{background:#0e1626;color:#ddd;font:11px Georgia;margin:6px}.g{display:flex;flex-wrap:wrap;gap:6px}.c{width:${compact ? 92 : 196}px;text-align:center}.r{width:150px;height:100px;display:inline-block;overflow:hidden;border:1px solid #444}.o{width:${compact ? 72 : 96}px;height:${compact ? 72 : 96}px;border-radius:50%;overflow:hidden;display:inline-block;${compact ? '' : 'margin-left:8px;'}border:3px solid #b8860b}small{display:block}</style><div class='g'>${flags.map(cell).join('')}</div>`);
console.log('sheet', sheetFile);
