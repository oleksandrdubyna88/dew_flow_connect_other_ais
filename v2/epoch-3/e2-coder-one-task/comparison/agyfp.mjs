// prints fingerprints (first 8 hex of sha256, not reversible to the token) and expiry only
import fs from 'node:fs'; import crypto from 'node:crypto';
const f=process.argv[2]; const j=JSON.parse(fs.readFileSync(f,'utf8')); const t=j.token||{};
const fp=s=>typeof s==='string'?crypto.createHash('sha256').update(s).digest('hex').slice(0,8):'-';
const st=fs.statSync(f);
console.log(JSON.stringify({file:f.split('/').slice(-3).join('/'),size:st.size,mode:(st.mode&0o777).toString(8),mtime:st.mtime.toISOString(),keys:Object.keys(j).join(','),tokenKeys:Object.keys(t).join(','),access_fp:fp(t.access_token),refresh_fp:fp(t.refresh_token),expiry:t.expiry,expires_in:t.expires_in}));
