import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, join, matchesGlob, resolve } from 'node:path';
import {
  SECURITY_PROMPT_MAX_BYTES, SECURITY_PROMPT_GLOB, SECURITY_PROMPT_WATCH, SecurityPromptTextCache, securityTextState, securityTextStates,
} from '../securityPromptFiles';
import { promptFile, promptsDir } from '../rolesPrompts';

/** A data directory with these files written under prompts/, as raw bytes; the test removes it. */
function withPromptFiles(files: Readonly<Record<string, Uint8Array>>): string {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-security-prompts-'));
  mkdirSync(promptsDir(dataDir), { recursive: true });
  for (const [id, bytes] of Object.entries(files)) writeFileSync(promptFile(dataDir, id)!, bytes);
  return dataDir;
}
const PLACEHOLDER = '<!-- OPERATOR: write it -->';
const utf16le = (text: string): Uint8Array => Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(text, 'utf16le')]);
const utf16be = (text: string): Uint8Array => Buffer.from(utf16le(text)).swap16();
const utf32le = (text: string): Uint8Array => {
  const points = [...text].map(c => c.codePointAt(0)!);
  const out = Buffer.alloc(4 + points.length * 4);
  out.writeUInt32LE(0x0000feff, 0);
  points.forEach((p, i) => out.writeUInt32LE(p, 4 + i * 4));
  return out;
};

test('a file is decoded the way the server reads it: its byte-order mark picks the encoding', async () => {
  // PowerShell 5's Out-File writes UTF-16LE with a mark: the server reads the placeholder in it, and so must the card,
  // or the card says "written" for a pairing the server drops (own review, epic 2).
  const dataDir = withPromptFiles({
    'redteam-u16le-placeholder': utf16le(PLACEHOLDER),
    'redteam-u16le-blank': utf16le('\r\n'),
    'redteam-u16be-text': utf16be('Review the change.'),
    'redteam-u32le-placeholder': utf32le(PLACEHOLDER),
    'redteam-u8bom-placeholder': Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from(PLACEHOLDER)]),
    'redteam-u8bom-only': Buffer.from([0xef, 0xbb, 0xbf]),
    // Two marks: the server drops one and keeps the second as text, which is not whitespace to .NET (found by the seam).
    'redteam-u8bom-bom': Buffer.from([0xef, 0xbb, 0xbf, 0xef, 0xbb, 0xbf]),
  });
  try {
    assert.deepEqual(await securityTextStates(dataDir, ['redteam-u16le-placeholder', 'redteam-u16le-blank', 'redteam-u16be-text',
      'redteam-u32le-placeholder', 'redteam-u8bom-placeholder', 'redteam-u8bom-only', 'redteam-u8bom-bom']), {
      'redteam-u16le-placeholder': 'placeholder', 'redteam-u16le-blank': 'blank', 'redteam-u16be-text': 'written',
      'redteam-u32le-placeholder': 'placeholder', 'redteam-u8bom-placeholder': 'placeholder', 'redteam-u8bom-only': 'blank', 'redteam-u8bom-bom': 'written',
    });
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
});

test('a UTF-32 file the server cannot decode cleanly is still text to it, and so to the card', async () => {
  // CodeRabbit on #675, then measured on the real binary: .NET decodes a scalar beyond U+10FFFF, a surrogate, and a
  // trailing sequence shorter than four bytes as U+FFFD — text, so "written" — while this decoder dropped them and
  // called three of the four files blank. The seam's text leg carries the same four files.
  const u32 = (...points: number[]): Buffer => Buffer.concat(points.map(p => { const b = Buffer.alloc(4); b.writeUInt32LE(p); return b; }));
  const dataDir = withPromptFiles({
    'redteam-beyond': u32(0xfeff, 0x110000),
    'redteam-surrogate': u32(0xfeff, 0xd800),
    'redteam-partial': Buffer.concat([u32(0xfeff), Buffer.from([0x20, 0x00])]),
    'redteam-space-partial': Buffer.concat([u32(0xfeff, 0x20), Buffer.from([0x20])]),
  });
  try {
    assert.deepEqual(await securityTextStates(dataDir, ['redteam-beyond', 'redteam-surrogate', 'redteam-partial', 'redteam-space-partial']), {
      'redteam-beyond': 'written', 'redteam-surrogate': 'written', 'redteam-partial': 'written', 'redteam-space-partial': 'written',
    });
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
});

test('an unchanged file is not read again, and a changed one is', async () => {
  const dataDir = withPromptFiles({ 'redteam-x': Buffer.from(PLACEHOLDER) });
  try {
    const cache = new SecurityPromptTextCache();
    assert.equal((await cache.states(dataDir, ['redteam-x']))['redteam-x'], 'placeholder');
    writeFileSync(promptFile(dataDir, 'redteam-x')!, 'Review the change for exploitable failures.');
    assert.equal((await cache.states(dataDir, ['redteam-x']))['redteam-x'], 'written', 'a new size is a new read');
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
});

test('the provider watches exactly the security prompt files under the data directory', () => {
  // The watcher is based at the data directory — prompts/ may not exist yet — so its pattern carries the folder.
  assert.ok(matchesGlob('prompts/redteam-sql.md', SECURITY_PROMPT_WATCH));
  assert.equal(matchesGlob('coai.db', SECURITY_PROMPT_WATCH), false, 'the database beside it is not a prompt');
  assert.equal(matchesGlob('prompts/architecture.md', SECURITY_PROMPT_WATCH), false, 'a role prompt is not a security prompt');
  assert.equal(matchesGlob('redteam-sql.md', SECURITY_PROMPT_WATCH), false, 'only inside prompts/');
});

// The extension half of shared/security-prompt-text-vectors.json; SecurityPromptTextVectorsTests is the server's.
// One set of vectors, so the card the tab draws and the pairing the server runs cannot disagree.

interface Vector { readonly name: string; readonly text?: string | null; readonly repeat?: string; readonly count?: number; readonly state: string }
const SHARED = JSON.parse(readFileSync(resolve(__dirname, '../../../shared/security-prompt-text-vectors.json'), 'utf8')) as
  { readonly maxBytes: number; readonly vectors: readonly Vector[] };
const textOf = (v: Vector): string | undefined => v.repeat !== undefined ? v.repeat.repeat(v.count ?? 0) : v.text ?? undefined;

test('the extension classifies every shared vector as the server does', () => {
  assert.ok(SHARED.vectors.length >= 10);
  for (const vector of SHARED.vectors) assert.equal(securityTextState(textOf(vector)), vector.state, vector.name);
});

test('the size limit is the one the shared file declares', () => {
  assert.equal(SECURITY_PROMPT_MAX_BYTES, SHARED.maxBytes);
});

test('the files on disk read as the same five states, and an id that may not become a path reads as none', async () => {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-security-prompts-'));
  try {
    mkdirSync(promptsDir(dataDir), { recursive: true });
    const write = (id: string, text: string): void => writeFileSync(promptFile(dataDir, id)!, text, 'utf8');
    write('redteam-blank', '  \n');
    write('redteam-placeholder', '<!-- OPERATOR: write it -->');
    write('redteam-written', 'Review the change.');
    write('redteam-oversized', 'a'.repeat(SECURITY_PROMPT_MAX_BYTES + 1));
    write('redteam-limit', 'a'.repeat(SECURITY_PROMPT_MAX_BYTES));
    const states = await securityTextStates(dataDir, ['redteam-none', 'redteam-blank', 'redteam-placeholder', 'redteam-written',
      'redteam-oversized', 'redteam-limit', 'con']);
    assert.deepEqual(states, {
      'redteam-none': 'none', 'redteam-blank': 'blank', 'redteam-placeholder': 'placeholder', 'redteam-written': 'written',
      'redteam-oversized': 'oversized', 'redteam-limit': 'written', con: 'none',
    });
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
});

test('the watcher pattern matches exactly the files promptFile names for security prompts', () => {
  const name = (id: string): string => basename(promptFile('/data', id)!);
  assert.ok(matchesGlob(name('redteam-sql'), SECURITY_PROMPT_GLOB));
  assert.ok(matchesGlob(name('redteam-my-own-2'), SECURITY_PROMPT_GLOB));
  assert.equal(matchesGlob(name('architecture'), SECURITY_PROMPT_GLOB), false, 'a role prompt is not a security prompt');
});
