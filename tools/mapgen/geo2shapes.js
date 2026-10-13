// geometry.json -> shapes json for render.ps1.  usage: node geo2shapes.js [geometry.json] [out.json]
const fs=require('fs');
const P=require('./paths');
const g=JSON.parse(fs.readFileSync(process.argv[2]||P.out('geometry.json'),'utf8'));
const w=P.world();
const col={}; for(const n of w.nations) col[n.id]=n.color;
const shapes=[]; 
for(const o of g.owners){ const polys=g.geometry[o.id]||[]; if(!polys.length) continue;
  let c = o.kind==='nation'?col[o.id]: o.kind==='neutral'?'#6b5a40':'#7a8590';
  shapes.push({id:o.id,name:o.name,color:c,polygons:polys,lx:0,ly:0}); }
const cities=w.nations.filter(n=>n.capital).map(n=>({name:n.capital.name,x:n.capital.x,y:n.capital.y}));
fs.writeFileSync(process.argv[3]||P.out('shapes.json'),JSON.stringify({shapes,cities}));
console.log('shapes',shapes.length);
