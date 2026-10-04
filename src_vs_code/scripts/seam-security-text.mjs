/**
 * The seam's security-prompt-text leg: real override files, read by the EXTENSION and by the REAL server, compared file
 * by file (todo/PLAN_the_security_tab_reads_at_a_glance.md, epic 2).
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
 * @param {{binary: string, fail: Function, timeoutMs: number}} runner the binary under test and the runner's failure road
 */
export async function securityTextSeam({ binary, fail, timeoutMs }) {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-security-text-'));
  try {
    mkdirSync(promptsDir(dataDir), { recursive: true });
    for (const [id, bytes] of Object.entries(FILES)) if (bytes !== null) writeFileSync(promptFile(dataDir, id), bytes);
    const ids = Object.keys(FILES);
    const extension = await new SecurityPromptTextCache().states(dataDir, ids);
    const server = await serverStates(binary, ids, dataDir, timeoutMs, fail);
    const disagree = ids.filter((id) => extension[id] !== server[id]);
    if (disagree.length > 0) {
      fail('the Security lane tab and the server read prompt files differently: '
        + disagree.map((id) => `${id}: extension ${extension[id]}, server ${server[id]}`).join('; '));
    }
    return { compared: ids.length };
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
}

/** The server's own reading, through a data directory nothing else shares. */
function serverStates(binary, ids, dataDir, timeoutMs, fail) {
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
      if (code !== 0) fail(`--security-prompt-text exited ${code}: ${err}`);
      done(JSON.parse(out));
    });
  });
}
