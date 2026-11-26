// Prints ONLY key names, value lengths/types, and times. Never a value of a string field.
import fs from 'node:fs';
const f = process.argv[2];
const st = fs.statSync(f);
console.log('file', f.replace(process.env.HOME,'~'), 'size', st.size, 'mode', (st.mode & 0o777).toString(8), 'mtime', st.mtime.toISOString());
let j; try { j = JSON.parse(fs.readFileSync(f,'utf8')); } catch(e){ console.log('not-json', e.name); process.exit(0); }
function jwtTimes(s){ try{ const p=JSON.parse(Buffer.from(s.split('.')[1],'base64url').toString()); const o={}; for(const k of ['iat','exp','nbf','auth_time']) if(typeof p[k]==='number') o[k]=new Date(p[k]*1000).toISOString(); o.claimKeys=Object.keys(p).length; return o;}catch{return null;} }
function walk(o, path){
  for (const [k,v] of Object.entries(o)) {
    const p = path? path+'.'+k : k;
    if (v && typeof v==='object' && !Array.isArray(v)) { console.log(p,'object keys:',Object.keys(v).join(',')); walk(v,p); }
    else if (typeof v==='string') { const t = v.split('.').length===3 ? jwtTimes(v) : null; console.log(p,'string len',v.length, t? 'JWT '+JSON.stringify(t):'', /^\d{4}-\d\d-\d\dT/.test(v)? 'TIME '+v : ''); }
    else console.log(p, typeof v, Array.isArray(v)?'array len '+v.length: (typeof v==='number'||typeof v==='boolean'||v===null? String(v):''));
  }
}
walk(j,'');
