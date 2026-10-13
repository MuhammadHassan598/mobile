// Where the generator reads and writes: data/ (inputs kept in the repo), out/ (generated, not kept).
'use strict';
const fs = require('fs'), path = require('path');
const OUT = path.join(__dirname, 'out');
fs.mkdirSync(OUT, { recursive: true });
module.exports = {
  OUT,
  world: () => JSON.parse(fs.readFileSync(path.join(__dirname, 'data', 'world.json'), 'utf8')),
  /** The land mask as one byte per pixel (1 = land), decoded from data/land-mask.json. */
  mask: () => {
    const j = JSON.parse(fs.readFileSync(path.join(__dirname, 'data', 'land-mask.json'), 'utf8'));
    const m = Buffer.alloc(j.width * j.height); let pos = 0, v = 0;
    for (const run of j.runs) { if (v) m.fill(1, pos, pos + run); pos += run; v ^= 1; }
    return m;
  },
  out: name => path.join(OUT, name),
};
