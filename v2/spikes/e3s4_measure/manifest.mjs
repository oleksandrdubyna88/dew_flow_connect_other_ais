// usage: manifest.mjs <out.tsv> <hashFile1,hashFile2,...> <root1> [root2...]
// Writes path<TAB>size<TAB>mtimeMs[<TAB>sha256-12 for listed files]. No file contents are printed.
import fs from 'node:fs'; import path from 'node:path'; import crypto from 'node:crypto';
const [out, hashList, ...roots] = process.argv.slice(2);
const hashSet = new Set(hashList.split(',').filter(Boolean));
const rows = [];
function walk(p){ let st; try{ st = fs.lstatSync(p);}catch{return;}
  const isd = st.isDirectory();
  let h=''; if(!isd && hashSet.has(p)){ try{ h=crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex').slice(0,12);}catch{h='unreadable';} }
  rows.push([p, isd?'D':st.isSymbolicLink()?'L':String(st.size), st.mtimeMs.toFixed(0), h].join('\t'));
  if(isd) for(const e of fs.readdirSync(p).sort()) walk(path.join(p,e)); }
for(const r of roots) walk(r);
fs.writeFileSync(out, rows.join('\n')+'\n'); console.log(out, rows.length,'rows');
