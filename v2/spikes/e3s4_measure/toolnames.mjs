import fs from 'node:fs'; const j=JSON.parse(fs.readFileSync(process.argv[2],'utf8')); const at=j.find(m=>m.type==='additional_tools');
const out=[]; (function w(ts,ns){ for(const t of ts){ if(t.tools) w(t.tools,t.name+'.'); else out.push(ns+t.name);} })(at?at.tools:[], '');
const ex=at?.tools?.[0]?.tools?.find(t=>t.name==='exec')?.description||''; const nested=[...new Set([...ex.matchAll(/### `(\w+)`/g)].map(m=>m[1]))];
console.log('top-level tools:', out.join(', ')); console.log('nested (code-mode) tools:', nested.join(', '));
