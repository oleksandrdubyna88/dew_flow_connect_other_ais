import { readFile, stat } from 'node:fs/promises';
import { PROMPTS_FOLDER, promptFile, promptFilesGlob } from './rolesPrompts';

/**
 * Whether a security prompt's override file holds text the server will send — the extension half of
 * `shared/security-prompt-text-vectors.json`; the server's `SecurityPromptText.Classify` answers the same vectors, and
 * the seam compares this module's reading of real files with the server's own (`--security-prompt-text`), so the card
 * the Security lane tab draws and the pairing the server runs cannot disagree
 * (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 2).
 *
 * <p>The data directory is the one `editSecurityPrompt` opens files in — `PanelProvider`'s `dataDir`, which the seam
 * suite compares with the server's own resolver — so the state read here is the file the server reads.</p>
 */

/** No file, an empty or whitespace file, the bare operator placeholder, too large, not readable, or text the server sends. */
export type SecurityTextState = 'none' | 'blank' | 'placeholder' | 'oversized' | 'unreadable' | 'written';

/** The server's `SecurityContext.MaxPromptBytes`; the shared vectors file declares it and both halves test against it. */
export const SECURITY_PROMPT_MAX_BYTES = 65536;

/** The security prompts' files under `promptsDir` — built where `promptFile` names them, so the two cannot drift. */
export const SECURITY_PROMPT_GLOB = promptFilesGlob('redteam-');

/** What the provider's watcher globs for, based at the DATA DIRECTORY: `prompts/` may not exist until the first override. */
export const SECURITY_PROMPT_WATCH = `${PROMPTS_FOLDER}/${SECURITY_PROMPT_GLOB}`;

/**
 * .NET's `char.IsWhiteSpace`, which the server's `IsNullOrWhiteSpace` and `Trim` use, as code points. JavaScript's own
 * `trim` differs on two of them — it keeps U+0085 and removes U+FEFF — so it is never used here.
 */
const DOTNET_SPACE: ReadonlySet<number> = new Set([
  0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, 0x85, 0xa0, 0x1680,
  0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a,
  0x2028, 0x2029, 0x202f, 0x205f, 0x3000,
]);
const isSpace = (c: string): boolean => DOTNET_SPACE.has(c.codePointAt(0) ?? 0);

/** `Trim()` as .NET does it: the same set, from both ends. */
function dotnetTrim(text: string): string {
  const chars = [...text];
  const from = chars.findIndex(c => !isSpace(c));
  if (from < 0) return '';
  let to = chars.length - 1;
  while (isSpace(chars[to]!)) to -= 1;
  return chars.slice(from, to + 1).join('');
}

export function securityTextState(text: string | undefined): SecurityTextState {
  return text === undefined ? 'none' : presentState(text);
}
function presentState(text: string): SecurityTextState {
  if ([...text].every(isSpace)) return 'blank';
  if (Buffer.byteLength(text, 'utf8') > SECURITY_PROMPT_MAX_BYTES) return 'oversized';
  return onlyThePlaceholder(text) ? 'placeholder' : 'written';
}
/** The operator's unfilled template: one `<!-- OPERATOR: … -->` comment and nothing after it. */
function onlyThePlaceholder(text: string): boolean {
  const trimmed = dotnetTrim(text);
  return trimmed.startsWith('<!-- OPERATOR:') && trimmed.indexOf('-->') === trimmed.length - 3;
}

/**
 * A file's bytes as `File.ReadAllText` decodes them for the server: its byte-order mark picks UTF-32 or UTF-16 (either
 * order) or UTF-8 and is dropped; with none, UTF-8. Without this a placeholder saved by PowerShell 5's `Out-File`
 * (UTF-16) would read as written text here while the server drops the pairing. Exactly ONE mark is dropped: the
 * decoder is told to keep any further one (`ignoreBOM`), because `ReadAllText` keeps it as text — a file of two marks
 * is written text to the server, and the seam caught this reading it as blank.
 */
export function decodeAsTheServerReads(bytes: Uint8Array): string {
  const [encoding, skip] = byteOrderOf(bytes);
  const body = bytes.subarray(skip);
  return encoding.startsWith('utf-32') ? decodeUtf32(body, encoding === 'utf-32le') : new TextDecoder(encoding, { ignoreBOM: true }).decode(body);
}
type Encoding = 'utf-8' | 'utf-16le' | 'utf-16be' | 'utf-32le' | 'utf-32be';
const MARKS: readonly (readonly [Encoding, readonly number[]])[] = [
  ['utf-32le', [0xff, 0xfe, 0x00, 0x00]], ['utf-32be', [0x00, 0x00, 0xfe, 0xff]],
  ['utf-8', [0xef, 0xbb, 0xbf]], ['utf-16le', [0xff, 0xfe]], ['utf-16be', [0xfe, 0xff]],
];
function byteOrderOf(bytes: Uint8Array): readonly [Encoding, number] {
  const mark = MARKS.find(([, prefix]) => prefix.every((b, i) => bytes[i] === b));
  return mark === undefined ? ['utf-8', 0] : [mark[0], mark[1].length];
}
function decodeUtf32(body: Uint8Array, little: boolean): string {
  const view = new DataView(body.buffer, body.byteOffset, body.byteLength - (body.byteLength % 4));
  const points: number[] = [];
  for (let at = 0; at < view.byteLength; at += 4) points.push(view.getUint32(at, little));
  return String.fromCodePoint(...points.filter(p => p <= 0x10ffff));
}

/** What a stat answered for one file. */
interface FileFacts { readonly mtimeMs: number; readonly size: number }
interface Seen extends FileFacts { readonly state: SecurityTextState }

/**
 * Each prompt's file state, read without blocking. A file is read only when its modification time or size changed
 * since the last read — the panel repaints for many reasons that have nothing to do with prompts — and the size is
 * checked first, so an oversized file is not read at all (a file that grows between the size check and the read is
 * still classified oversized, from the bytes read). An id `promptFile` refuses counts as no file; a file that exists
 * but cannot be read is `unreadable` — the server cannot read it either, and excludes the pairing.
 */
export class SecurityPromptTextCache {
  private readonly seen = new Map<string, Seen>();

  async states(dataDir: string, ids: readonly string[]): Promise<Readonly<Record<string, SecurityTextState>>> {
    const states = await Promise.all(ids.map(async id => [id, await this.fileState(promptFile(dataDir, id))] as const));
    return Object.fromEntries(states);
  }

  private async fileState(path: string | undefined): Promise<SecurityTextState> {
    if (path === undefined) return 'none';
    const facts = await statOf(path);
    return facts === undefined ? 'none' : this.unchanged(path, facts) ?? await this.read(path, facts);
  }

  private unchanged(path: string, facts: FileFacts): SecurityTextState | undefined {
    const known = this.seen.get(path);
    return known !== undefined && known.mtimeMs === facts.mtimeMs && known.size === facts.size ? known.state : undefined;
  }

  private async read(path: string, facts: FileFacts): Promise<SecurityTextState> {
    const state = facts.size > SECURITY_PROMPT_MAX_BYTES ? 'oversized' : await readState(path);
    this.seen.set(path, { mtimeMs: facts.mtimeMs, size: facts.size, state });
    return state;
  }
}

/** Every prompt's state, uncached — for a single read such as a test or a one-off check. */
export function securityTextStates(dataDir: string, ids: readonly string[]): Promise<Readonly<Record<string, SecurityTextState>>> {
  return new SecurityPromptTextCache().states(dataDir, ids);
}

async function statOf(path: string): Promise<FileFacts | undefined> {
  try {
    return await stat(path);
  } catch {
    return undefined;
  }
}
async function readState(path: string): Promise<SecurityTextState> {
  try {
    return securityTextState(decodeAsTheServerReads(await readFile(path)));
  } catch {
    return 'unreadable';
  }
}
