// usage: craft.mjs <mode> <srcAuth> <outDir>   modes: last9d | exp60 | placeholder
// Reads the SCRATCH COPY of the sign-in (never the real file) and writes a crafted auth.json (0600) into <outDir>. Prints key names and lengths only, never a value.
import fs from 'node:fs';
const [mode, src, outDir] = process.argv.slice(2);
const j = JSON.parse(fs.readFileSync(src, 'utf8'));
const now = Date.now();
const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
if (mode === 'last9d') { j.last_refresh = new Date(now - 9 * 86400e3).toISOString(); }
else if (mode === 'exp60') {
  j.tokens.access_token = b64({ alg: 'none', typ: 'JWT' }) + '.' + b64({ iat: Math.floor(now / 1000), nbf: Math.floor(now / 1000), exp: Math.floor(now / 1000) + 60 }) + '.sig';
  j.tokens.refresh_token = 'placeholder-refresh-token-not-real';
  j.last_refresh = new Date(now - 9 * 86400e3).toISOString();
} else if (mode === 'placeholder') { j.tokens.refresh_token = 'placeholder-refresh-token-not-real'; }
else throw new Error('mode');
fs.mkdirSync(outDir, { recursive: true, mode: 0o700 });
fs.writeFileSync(outDir + '/auth.json', JSON.stringify(j), { mode: 0o600 });
console.log(JSON.stringify({ mode, out: outDir.replace(process.env.HOME, '~'), access_len: j.tokens.access_token.length, refresh_len: j.tokens.refresh_token.length, last_refresh: j.last_refresh }));
