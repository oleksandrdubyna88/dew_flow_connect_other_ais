// usage: agycraft.mjs <mode> <srcTokenFile> <outHomeDir>   modes: norefresh | expired_norefresh | expired_full
// Reads the SCRATCH COPY of the agy sign-in and writes <outHomeDir>/.gemini/antigravity-cli/antigravity-oauth-token (0600). Prints names and fingerprints only.
import fs from 'node:fs'; import crypto from 'node:crypto';
const [mode, src, out] = process.argv.slice(2);
const j = JSON.parse(fs.readFileSync(src, 'utf8'));
const past = new Date(Date.now() - 2 * 3600e3).toISOString();
if (mode === 'norefresh' || mode === 'expired_norefresh') delete j.token.refresh_token;
if (mode === 'expired_norefresh' || mode === 'expired_full') j.token.expiry = past;
const d = out + '/.gemini/antigravity-cli'; fs.mkdirSync(d, { recursive: true, mode: 0o700 });
fs.writeFileSync(d + '/antigravity-oauth-token', JSON.stringify(j), { mode: 0o600 });
const fp = s => typeof s === 'string' ? crypto.createHash('sha256').update(s).digest('hex').slice(0, 8) : '-';
console.log(JSON.stringify({ mode, keys: Object.keys(j.token).join(','), refresh_fp: fp(j.token.refresh_token), expiry: j.token.expiry }));
