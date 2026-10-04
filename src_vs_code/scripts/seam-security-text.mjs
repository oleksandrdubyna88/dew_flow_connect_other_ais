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
};

/**
 * Run the leg. Answers how many files were compared when every one agreed; ends the process through `fail` when one did
 * not, or when the server could not answer.
 *
 * @param {{binary: string, named: boolean, fail: Function, timeoutMs: number}} runner the binary under test, whether
 *   `COAI_MCP_DLL` named it, and the runner's failure road
 * @returns {Promise<{compared: number, older: boolean}>} `older` when a named binary predates the mode and nothing was compared
 */
export async function securityTextSeam({ binary, named, fail, timeoutMs }) {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-security-text-'));
  try {
    writeFiles(dataDir);
    const ids = Object.keys(FILES);
    const extension = await new SecurityPromptTextCache().states(dataDir, ids);
    const server = await serverStates(binary, named, ids, dataDir, timeoutMs, fail);
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
 * What a `--security-prompt-text` exit means. The mode is new in MCP 0.43.0, so a binary `COAI_MCP_DLL` NAMES — the
 * mixed-version run against a released server — may refuse it as an unknown argument, and that is `older`: the leg
 * has nothing to compare and the seam goes on to the legs after it. The binary this repository just built has no such
 * excuse; the same refusal from it is a regression, and so is any other failure from either.
 *
 * @param {{named: boolean, code: number | null, stderr: string}} exit
 * @returns {'ok' | 'older' | 'fail'}
 */
export function textLegVerdict({ named, code, stderr }) {
  if (code === 0) return 'ok';
  return predatesTheMode(named, code, stderr) ? 'older' : 'fail';
}
/** A binary COAI_MCP_DLL named, refusing the mode as an argument it does not know (exit 64, the usage error). */
const predatesTheMode = (named, code, stderr) => named && code === 64 && stderr.includes("unknown argument '--security-prompt-text'");

/** The server's own reading, through a data directory nothing else shares; `undefined` when a named binary predates it. */
function serverStates(binary, named, ids, dataDir, timeoutMs, fail) {
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
      const verdict = textLegVerdict({ named, code, stderr: err });
      if (verdict === 'fail') fail(`--security-prompt-text exited ${code}: ${err}`);
      done(verdict === 'older' ? undefined : readingOf(out, fail));
    });
  });
}
