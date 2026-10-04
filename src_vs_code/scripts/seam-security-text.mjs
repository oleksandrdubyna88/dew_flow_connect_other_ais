/**
 * The seam's security-prompt-text leg: real override files, read by the EXTENSION and by the REAL server, compared file
 * by file (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 2).
 *
 * <p>The Security lane tab draws each prompt card from the extension's reading of its override file; the server decides
 * whether the pairing runs from its own. `shared/security-prompt-text-vectors.json` pins the RULE on text, and both
 * halves answer it — but two suites agreeing with one file is not a check of a contract with two implementations. This
 * leg is: the same bytes on disk, the extension's `SecurityPromptTextCache` and the server's `--security-prompt-text`,
 * and a failure naming every file the two read differently. It covers what only real reading shows — byte-order marks,
 * UTF-16 and UTF-32, .NET's whitespace, the size limit.</p>
 *
 * <p>Nothing is launched but the one read-only command, and nothing is billed.</p>
 */
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { SecurityPromptTextCache, SECURITY_PROMPT_MAX_BYTES } = await import('../out/securityPromptFiles.js');
const { promptFile, promptsDir } = await import('../out/rolesPrompts.js');

const PLACEHOLDER = '<!-- OPERATOR: write the review prompt -->';
const point = (...codes) => String.fromCodePoint(...codes);
const utf8 = (text) => Buffer.from(text, 'utf8');
const bom8 = (text) => Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), utf8(text)]);
const utf16le = (text) => Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(text, 'utf16le')]);
const utf16be = (text) => Buffer.from(utf16le(text)).swap16();
const utf32le = (text) => {
  const points = [...text].map((c) => c.codePointAt(0));
  const out = Buffer.alloc(4 + points.length * 4);
  out.writeUInt32LE(0xfeff, 0);
  points.forEach((p, i) => out.writeUInt32LE(p, 4 + i * 4));
  return out;
};

/** Said beside a refusal nobody declared: how to run the seam against a server from before the mode, on purpose. */
const OLDER_HINT = ' — if COAI_MCP_DLL names a server from before MCP 0.43.0, set COAI_SEAM_OLDER_SERVER=1 to skip this leg';

/** Raw UTF-32LE code units, valid or not — what a damaged or hand-made file holds. */
const u32le = (...units) => Buffer.concat(units.map((u) => { const b = Buffer.alloc(4); b.writeUInt32LE(u); return b; }));

/** Every kind of file a person or a tool may leave, by the prompt id it is written under; `null` = no file. */
const FILES = {
  'redteam-none': null,
  'redteam-empty': utf8(''),
  'redteam-blank': utf8('  \r\n\t'),
  'redteam-placeholder': utf8(PLACEHOLDER),
  'redteam-written': utf8('Review the change for exploitable failures.'),
  'redteam-limit': utf8('a'.repeat(SECURITY_PROMPT_MAX_BYTES)),
  'redteam-over': utf8('a'.repeat(SECURITY_PROMPT_MAX_BYTES + 1)),
  'redteam-nel': utf8(point(0x85, 0x85)),
  'redteam-nbsp': utf8(point(0xa0)),
  'redteam-zwsp': utf8(point(0x200b)),
  'redteam-nel-placeholder': utf8(point(0x85) + PLACEHOLDER + point(0x85)),
  'redteam-bom-only': bom8(''),
  'redteam-bom-placeholder': bom8(PLACEHOLDER),
  'redteam-bom-bom': bom8(point(0xfeff)),
  'redteam-u16le-placeholder': utf16le(PLACEHOLDER),
  'redteam-u16le-blank': utf16le('\r\n'),
  'redteam-u16be-written': utf16be('Review it.'),
  'redteam-u32le-placeholder': utf32le(PLACEHOLDER),
  // Not cleanly decodable: .NET reads each as U+FFFD, text (CodeRabbit on #675, measured on the binary).
  'redteam-u32le-beyond': u32le(0xfeff, 0x110000),
  'redteam-u32le-surrogate': u32le(0xfeff, 0xd800),
  'redteam-u32le-partial': Buffer.concat([u32le(0xfeff), Buffer.from([0x20, 0x00])]),
  'redteam-u32le-space-partial': Buffer.concat([u32le(0xfeff, 0x20), Buffer.from([0x20])]),
};

/**
 * Run the leg. Answers how many files were compared when every one agreed; ends the process through `fail` when one did
 * not, or when the server could not answer.
 *
 * @param {{binary: string, olderExpected: boolean, fail: Function, timeoutMs: number}} runner the binary under test,
 *   whether the run declared it older than the mode, and the runner's failure road
 * @returns {Promise<{compared: number, older: boolean}>} `older` when a declared-older binary refused the mode
 */
export async function securityTextSeam({ binary, olderExpected, fail, timeoutMs }) {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-security-text-'));
  try {
    writeFiles(dataDir);
    const ids = Object.keys(FILES);
    const extension = await new SecurityPromptTextCache().states(dataDir, ids);
    const server = await serverStates(binary, olderExpected, ids, dataDir, timeoutMs, fail);
    return server === undefined ? { compared: 0, older: true } : compared(ids, extension, server, fail);
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
}

/** The server's printed reading — or the leg's failure, never a throw that skips the runner's cleanup. */
function readingOf(out, fail) {
  try {
    return JSON.parse(out);
  } catch {
    return fail(`--security-prompt-text answered something that is not JSON: ${out.slice(0, 200)}`);
  }
}

/** Every fixture file, written under the prompt id it stands for; `null` writes nothing. */
function writeFiles(dataDir) {
  mkdirSync(promptsDir(dataDir), { recursive: true });
  for (const [id, bytes] of Object.entries(FILES)) if (bytes !== null) writeFileSync(promptFile(dataDir, id), bytes);
}

/** Both readings, file by file: a failure naming every file read differently, or how many were compared. */
function compared(ids, extension, server, fail) {
  const disagree = ids.filter((id) => extension[id] !== server[id]);
  if (disagree.length > 0) {
    fail('the Security lane tab and the server read prompt files differently: '
      + disagree.map((id) => `${id}: extension ${extension[id]}, server ${server[id]}`).join('; '));
  }
  return { compared: ids.length, older: false };
}

/**
 * What a `--security-prompt-text` exit means. The mode is new in MCP 0.43.0, so the mixed-version run against a
 * released server — `COAI_MCP_DLL` naming it AND `COAI_SEAM_OLDER_SERVER=1` saying it predates the mode — may see it
 * refused as an unknown argument, and that is `older`: nothing to compare, and the seam goes on to the legs after it.
 * Without that declaration the same refusal is a regression — from this repository's own build, or from a named
 * binary that should have the mode and lost it (CodeRabbit on #675) — and so is any other failure.
 *
 * @param {{olderExpected: boolean, code: number | null, stderr: string}} exit
 * @returns {'ok' | 'older' | 'fail'}
 */
export function textLegVerdict({ olderExpected, code, stderr }) {
  if (code === 0) return 'ok';
  return olderExpected && refusedAsUnknown(code, stderr) ? 'older' : 'fail';
}
/** The refusal a binary from before the mode gives: an argument it does not know (exit 64, the usage error). */
const refusedAsUnknown = (code, stderr) => code === 64 && stderr.includes("unknown argument '--security-prompt-text'");

/** The server's own reading, through a data directory nothing else shares; `undefined` when a declared-older binary refused it. */
function serverStates(binary, olderExpected, ids, dataDir, timeoutMs, fail) {
  const env = {
    ...Object.fromEntries(Object.keys(process.env).filter((key) => key.startsWith('COAI_')).map((key) => [key, ''])),
    COAI_DATA_DIR: dataDir,
  };
  return new Promise((done) => {
    const child = spawn('dotnet', [binary, '--security-prompt-text', '--ids', ids.join(',')], {
      env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => { child.kill('SIGKILL'); fail(`--security-prompt-text did not answer within ${timeoutMs} ms`); }, timeoutMs);
    child.stdout.on('data', (b) => { out += String(b); });
    child.stderr.on('data', (b) => { err += String(b); });
    child.on('error', (e) => { clearTimeout(deadline); fail(`--security-prompt-text could not start: ${e.message}`); });
    child.on('close', (code) => {
      clearTimeout(deadline);
      const verdict = textLegVerdict({ olderExpected, code, stderr: err });
      if (verdict === 'fail') fail(`--security-prompt-text exited ${code}: ${err}${refusedAsUnknown(code, err) ? OLDER_HINT : ''}`);
      done(verdict === 'older' ? undefined : readingOf(out, fail));
    });
  });
}
