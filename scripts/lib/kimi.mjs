// lib/kimi.mjs — what the Kimi measurement script launches with, and how it treats the person's Kimi home.
//
// Shared by scripts/probe-kimi-confinement.mjs (the confinement cells, research/RESULTS_kimi_confinement.md §4–§13). Kimi-only:
// the generic pieces — the tree kill by pid, the file walk, the minimal environment — are lib/vibe.mjs's and are imported,
// not copied. Everything here that is a FACT about Kimi names the measurement it came from.
//
// The person's ~/.kimi-code is never edited by anything here: an arm that needs a user-level change (a hook, an MCP server)
// runs on a COPY of the home under KIMI_CODE_HOME, which Kimi documents as relocating its whole data root. The one thing a
// copy carries that matters is the OAuth credential pair; `CredentialGuard` watches whether a refresh ROTATES the refresh
// token and, if it does, puts the live pair back where the person's Kimi expects it (RESULTS §12).
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { basename, dirname, join, posix, relative, resolve, sep, win32 } from 'node:path';
import { dirLink, files, fwd, killTree, pythonCommand, removeDirLink, run } from './vibe.mjs';

export const REAL_HOME = join(homedir(), '.kimi-code');

/**
 * Where Moonshot's installer puts `kimi`: on Windows `~/.kimi-code/bin/kimi.exe` (KimiAcpProfile.InstalledExecutableFor, the shim's
 * own default); elsewhere `<KIMI_INSTALL_DIR or ~/.kimi-code>/bin/kimi` (DL5 of research/PLAN_kimi_and_vibe_on_linux.md).
 */
export function kimiExecutable(platform = process.platform, env = process.env, home = homedir()) {
  if (platform === 'win32') return win32.join(home, '.kimi-code', 'bin', 'kimi.exe');
  return posix.join(env.KIMI_INSTALL_DIR || posix.join(home, '.kimi-code'), 'bin', 'kimi');
}

export const KIMI_EXE = kimiExecutable();
export const DEFAULT_MODEL = 'kimi-code/k3-256k';
/** The shim's exit codes (AskAcpMode): the contract the probe reads, never the stdout text. */
export const EXIT = { ok: 0, badRequest: 65, unavailable: 69, failed: 70, spent: 75, protocol: 76, refused: 77 };
/** KimiAcpProfile.Exclusions — the names the shipped boundary refuses at every depth; the M-K5 client refuses the same. */
export const EXCLUSIONS = ['AGENTS.md', 'agents.md', '.kimi-code', '.vibe', '.agents/skills', '.git'];

/** The product's owned-home layout (E4.2/E4.3): the shim's `--home` is the ROOT; `<root>/home` is KIMI_CODE_HOME, `<root>/os` the empty OS home. */
export const OWNED_HOME = 'home';
export const OWNED_OS = 'os';

/** The owned root a KIMI_CODE_HOME belongs to (`<root>/home` → `<root>`), or '' for a folder not laid out that way — the shim refuses it. */
export const rootOfHome = (home) => (home && basename(home) === OWNED_HOME ? dirname(home) : '');

/**
 * `coai-mcp --ask-acp …` as KimiRuntime builds it (KimiRuntime.ForLaunch; the shim's own contract, E4.3): the prompt rides on stdin, never here.
 * `root` is the owned root and brings the three flags a Kimi turn is refused without — `--home`, `--persons-bin` (the person's tool folder) and
 * `--locks` (the lock folder). Without a root the argv is the pre-E4.3 shape, which the shim answers 65.
 */
export function shimArgs({ cwd, model, deadlineSeconds, executable = '', root = '', personsBin = '', locks = '' }) {
  if (root && !personsBin) throw new Error('shimArgs: a root without --persons-bin — the shim refuses a Kimi turn without all three of --home, --persons-bin and --locks');
  if (root && !locks) throw new Error('shimArgs: a root without --locks — the shim refuses a Kimi turn without all three of --home, --persons-bin and --locks');
  return ['--ask-acp', '--profile', 'kimi', '--cwd', cwd, '--model', model, '--deadline-seconds', String(deadlineSeconds),
    ...(executable ? ['--executable', executable] : []),
    ...(root ? ['--home', root, '--persons-bin', personsBin, '--locks', locks] : [])];
}

/**
 * What an arm would vary that the shim now FIXES: `KimiEnvironment.Built` always pins KIMI_CODE_NO_AUTO_UPDATE and always redirects the OS home
 * to `<root>/os` (E4.3), so an arm that measured their absence cannot be run through it — and is refused by name, never run as something else.
 */
export function shimRefusal(spec) {
  if (spec.pin === false) return 'the shim always sets KIMI_CODE_NO_AUTO_UPDATE=1 (KimiEnvironment.Built) — run this arm with kind "prompt" to measure its absence';
  if (spec.profile === '') return 'the shim always redirects USERPROFILE/HOME to <root>/os (KimiEnvironment.Built) — run this arm with kind "prompt" to measure the real OS home';
  return '';
}

export const sha256 = (data) => createHash('sha256').update(data).digest('hex');
export const fileHash = (path) => sha256(readFileSync(path));

/** Every file under `dir` with its SHA-256 — what a before/after comparison of a home is made of. */
export const isCredentialPath = (rel) => rel === 'credentials' || rel.startsWith('credentials/');

/** A credential slot's name carries its hex (`kimi-code-env-<16 hex>.json`); the record keeps the shape and drops the slot (DN9: redacted). */
export const redactSlot = (rel) => rel.replaceAll(/[0-9a-f]{8,}/giu, '<hex>');

/** `lstat:<size>:<mode>:<mtime ms>` — a file recorded WITHOUT opening it; a rotated refresh token moves the size or the time. */
function lstatShape(path) {
  const s = lstatSync(path);
  return `lstat:${s.size}:${s.mode & 0o7777}:${Math.round(s.mtimeMs)}`;
}

/** `name`, or `name#2`, `name#3`… when two redacted names would collide — the record never loses an entry. */
const uniqueName = (taken, name) => (name in taken ? uniqueName(taken, `${name.replace(/#\d+$/u, '')}#${Number(/#(\d+)$/u.exec(name)?.[1] ?? 1) + 1}`) : name);

/**
 * Every file under `dir` with its SHA-256 — what a before/after comparison of a home is made of. Nothing under `credentials/` is EVER opened
 * (DN5 item 2, gate finding 0: a hash reads the bytes, and those bytes are a refresh token): those paths — and any path `skip(rel)` names —
 * are recorded by `lstat` alone, credential names with the slot's hex redacted. `hash` is injected so a test proves it is never called for them.
 */
export function manifest(dir, { skip = () => false, hash = fileHash } = {}) {
  const record = {}; // built locally in one pass: a home can hold thousands of files, and a spread per file would be quadratic
  for (const rel of files(dir)) {
    const credential = isCredentialPath(rel);
    if (!credential && !skip(rel)) record[rel] = hash(join(dir, rel));
    else record[uniqueName(record, credential ? redactSlot(rel) : rel)] = lstatShape(join(dir, rel));
  }
  return record;
}

/** Whether each marker occurs in the session's main wire — the system prompt and tool results — whatever the answer did with it. */
export function inWire(sessionDir, markers) {
  const wire = join(sessionDir ?? '', 'agents', 'main', 'wire.jsonl');
  if (!sessionDir || !existsSync(wire)) return {};
  const text = readFileSync(wire, 'utf8');
  return Object.fromEntries(Object.entries(markers).map(([k, t]) => [k, text.includes(t)]));
}

/** The paths whose hash differs, or that exist on one side only. */
export function manifestDiff(before, after) {
  const names = new Set([...Object.keys(before), ...Object.keys(after)]);
  return [...names].filter((n) => before[n] !== after[n]).sort((a, b) => a.localeCompare(b))
    .map((n) => ({ path: n, change: !(n in before) ? 'added' : !(n in after) ? 'removed' : 'changed' }));
}

/**
 * Kimi's key for a working folder (measured 2026-10-13 against the person's own store): the session folder is
 * `wd_<basename>_<sha256(forward-slash path)[:12]>`, the workspace-trust entry the same over the LOWER-CASED path.
 */
export const wdKey = (workdir) => `wd_${basename(workdir)}_${sha256(fwd(workdir)).slice(0, 12)}`;
export const trustKey = (workdir) => `wd_${basename(workdir).toLowerCase()}_${sha256(fwd(workdir).toLowerCase()).slice(0, 12)}`;

/** Writes the trust entry Kimi itself writes (`workspace-trust/<key>`: `{root, trustedAt}`) into `home` — a positive control's lever. */
export function trustInHome(home, workdir) {
  const path = join(home, 'workspace-trust', trustKey(workdir));
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify({ root: workdir, trustedAt: Date.now() }));
  return path;
}

// ---------------------------------------------------------------------------------------------------- the copied home

/** What a copied home takes from the real one: the sign-in and the config, the device identity, and the bundled rg/fd (never kimi.exe). */
const HOME_FILES = ['config.toml', 'tui.toml', 'device_id', 'region', 'migrations-effort.json'];
/** The tools a person's `~/.kimi-code/bin` may hold beside `kimi`, by the platform's names (L-K1: on WSL `fd` is there and `rg` is not). */
export const homeBin = (platform = process.platform) => (platform === 'win32' ? ['rg.exe', 'fd.exe'] : ['rg', 'fd']);
const HOME_BIN = homeBin();

/**
 * A fresh home at `home` copied from `real`. Every copy is verified by hash; the list of what was copied is returned.
 * `credentials: false` leaves the sign-in out (the not-signed-in arm); `config: false` leaves config.toml out too.
 */
export function copyHome(real, home, { credentials = true, config = true } = {}) {
  // Owner-only where the platform has modes (POSIX); on Windows the mode is ignored and the profile's ACL holds (scratchRefusal).
  mkdirSync(join(home, 'bin'), { recursive: true, mode: 0o700 });
  const copied = [];
  const take = (rel) => {
    const from = join(real, rel);
    if (!existsSync(from)) return;
    const to = join(home, rel);
    mkdirSync(dirname(to), { recursive: true });
    copyFileSync(from, to);
    if (fileHash(from) !== fileHash(to)) throw new Error(`copy of ${rel} into ${home} does not hash like its source`);
    copied.push(rel);
  };
  for (const f of HOME_FILES) if (config || f !== 'config.toml') take(f);
  for (const f of HOME_BIN) take(join('bin', f));
  if (credentials && existsSync(join(real, 'credentials'))) {
    for (const f of readdirSync(join(real, 'credentials'))) if (statSync(join(real, 'credentials', f)).isFile()) take(join('credentials', f));
  }
  return copied;
}

/**
 * Why `scratch` may NOT hold copies of the person's Kimi sign-in — or '' when it may. A copied home carries the OAuth
 * refresh token until the scrub, so the folder it lives in must be one no other local user can read (epic 2's code round,
 * codex, security). The rule:
 * - **inside the user's own profile** (`homedir()`, links resolved) — and on POSIX also the temp folder, where the probe
 *   makes its folders `0700`; on Windows only the profile, because `%TEMP%` can be redirected somewhere shared. What it
 *   relies on there: a folder created under the profile inherits the profile's ACL (the user, SYSTEM, Administrators), so
 *   no other standard user can read it — Windows has no mode bits to set;
 * - **reached through no link** anywhere on the way (`realpathSync.native` of the nearest existing ancestor is the path
 *   itself); a dangling link is refused, never mistaken for an absent folder;
 * - on POSIX, an EXISTING folder that is not owner-only (`mode & 0o077`) is refused.
 * The product never needs this: D19 gives every Kimi launch an owned home whose `credentials/` is a JUNCTION to the
 * person's, so the sign-in is never copied anywhere. Only this probe's copied-home arms copy it.
 */
export function scratchRefusal(scratch, { profile = homedir(), temp = tmpdir() } = {}) {
  const wanted = resolve(scratch);
  const real = realOf(wanted);
  if (real.length === 0) return `refusing --scratch ${wanted}: a link on the way to it does not resolve`;
  if (!samePath(real, wanted)) return `refusing --scratch ${wanted}: it is reached through a link (really ${real}), and a copied sign-in never goes through one`;
  const roots = (WINDOWS ? [profile] : [profile, temp]).map((root) => realOf(resolve(root))).filter((root) => root.length > 0);
  if (!roots.some((root) => isInside(wanted, root))) {
    return `refusing --scratch ${wanted}: it is not inside your own ${WINDOWS ? 'profile' : 'profile or temp folder'} (${roots.join(', ')}), so another local user may be able to read the copied sign-in`;
  }
  return !WINDOWS && existsSync(wanted) && (statSync(wanted).mode & 0o077) !== 0
    ? `refusing --scratch ${wanted}: other users can read it (mode ${(statSync(wanted).mode & 0o777).toString(8)}); give a fresh folder, which the probe makes 0700`
    : '';
}

const WINDOWS = process.platform === 'win32';

/** Whether anything is at `p`, a dangling link included (lstat, which never follows). */
const present = (p) => { try { lstatSync(p); return true; } catch { return false; } };

/** `p` with every link resolved: its nearest present ancestor's real path plus the rest — or '' when a link dangles. */
function realOf(p) {
  let existing = p;
  while (!present(existing) && dirname(existing) !== existing) existing = dirname(existing);
  try {
    return join(realpathSync.native(existing), relative(existing, p));
  } catch {
    return '';
  }
}

const fold = (p) => (WINDOWS ? p.toLowerCase() : p);

/** `p` without trailing separators — a loop, because an end-anchored `[\\/]+$` backtracks quadratically on a long run of them. */
export function withoutTrailingSeparators(p) {
  let end = p.length;
  while (end > 0 && (p[end - 1] === '/' || p[end - 1] === '\\')) end--;
  return p.slice(0, end);
}

const samePath = (a, b) => fold(withoutTrailingSeparators(a)) === fold(withoutTrailingSeparators(b));
const isInside = (child, parent) => fold(child).startsWith(fold(withoutTrailingSeparators(parent)) + sep);

/**
 * The sensitive files a copied home must not keep once the run is over: the sign-in and the config (it carries an API key).
 * A `credentials` that is a LINK (the shared-sign-in arm, M-K8b) is removed by its OWN name (lib/vibe.mjs removeDirLink: `rmdir`
 * of a junction on Windows, `unlink` of a symlink elsewhere — `rmdir` refuses a symlink with ENOTDIR, measured red on WSL first)
 * and never what it points at; a recursive remove would have followed it.
 */
export function scrubHome(home) {
  const gone = [];
  for (const rel of ['credentials', 'config.toml']) {
    const p = join(home, rel);
    const link = isLink(p);
    if (!existsSync(p) && !link) continue;
    if (link) removeDirLink(p); else rmSync(p, { recursive: true, force: true });
    if (existsSync(p) || isLink(p)) throw new Error(`${p} still exists after the scrub`);
    gone.push(link ? `${rel} (link)` : rel);
  }
  return gone;
}

const isLink = (p) => { try { return lstatSync(p).isSymbolicLink(); } catch { return false; } };

/**
 * A folder link `link` → `target`: a directory junction on Windows (mklink /J through cmd.exe by its absolute path), a symlink on
 * Linux (DL6 of research/PLAN_kimi_and_vibe_on_linux.md) — lib/vibe.mjs's dirLink, one maker for both probes. The error text, or ''.
 */
export const junction = (link, target) => dirLink(link, target);

// ------------------------------------------------------------------------------------------------- the credential guard

/** Fingerprints of a home's OAuth files — the file's hash and the refresh token's hash; the values themselves are never held. */
export function credentialFingerprint(home) {
  const dir = join(home, 'credentials');
  if (!existsSync(dir)) return {};
  const out = {};
  for (const f of readdirSync(dir).filter((n) => n.endsWith('.json'))) {
    const text = readFileSync(join(dir, f), 'utf8');
    let refresh = '';
    let expiresAt = 0;
    try {
      const j = JSON.parse(text);
      refresh = typeof j.refresh_token === 'string' ? sha256(j.refresh_token).slice(0, 12) : '';
      expiresAt = Number(j.expires_at ?? 0);
    } catch { /* not JSON: the file hash still says whether it moved */ }
    out[f] = { file: sha256(text).slice(0, 12), refresh, expiresAt };
  }
  return out;
}

const refreshOf = (fp) => Object.fromEntries(Object.entries(fp).map(([f, v]) => [f, v.refresh]));
const sameRefresh = (a, b) => JSON.stringify(refreshOf(a)) === JSON.stringify(refreshOf(b));

/** Copies every credential file from one home to another, verified by hash; returns the names. */
export function syncCredentials(from, to) {
  const src = join(from, 'credentials');
  if (!existsSync(src)) return [];
  mkdirSync(join(to, 'credentials'), { recursive: true });
  const names = [];
  for (const f of readdirSync(src).filter((n) => statSync(join(src, n)).isFile())) {
    copyFileSync(join(src, f), join(to, 'credentials', f));
    if (fileHash(join(src, f)) !== fileHash(join(to, 'credentials', f))) throw new Error(`credential ${f} did not copy byte for byte`);
    names.push(f);
  }
  return names;
}

/**
 * Watches the sign-in across homes. Kimi's access token lives 900 s (`expires_in`, read off the file), so nearly every
 * launch refreshes it; whether the REFRESH token rotates on refresh is measured on the real home's first refresh. A copied
 * home that refreshed under rotation holds the only live pair — it is written back to the real home, verified, and the
 * write-back is recorded. Under no rotation nothing is ever written to the real home.
 */
export class CredentialGuard {
  #real;
  #last;
  #read;
  rotates = null; // unknown until a refresh of the real home has been seen
  events = [];

  /** `read` is injected so a test proves WHEN the person's credential files are opened (S-N2's twin: never for an owned-only run). */
  constructor(real, { read = credentialFingerprint } = {}) {
    this.#real = real;
    this.#read = read;
    this.#last = read(real);
  }

  /** After a launch on the real home: learn whether the refresh token rotated. */
  afterRealRun(tag) {
    const now = this.#read(this.#real);
    const fileMoved = JSON.stringify(now) !== JSON.stringify(this.#last);
    if (fileMoved) {
      this.rotates = !sameRefresh(now, this.#last);
      this.events.push({ tag, where: 'real', refreshed: true, rotated: this.rotates });
    }
    this.#last = now;
    return fileMoved;
  }

  /** Before a launch on a copied home that carries credentials: give it the freshest pair. */
  beforeCopiedRun(home, tag) {
    if (!existsSync(join(home, 'credentials'))) return;
    this.events.push({ tag, where: 'copy', synced: syncCredentials(this.#real, home) });
  }

  /** After a launch on a copied home: if it refreshed and rotation is possible, the live pair goes back to the real home. */
  afterCopiedRun(home, tag) {
    if (!existsSync(join(home, 'credentials'))) return false;
    const copy = this.#read(home);
    if (sameRefresh(copy, this.#last)) return false;
    const refreshedInCopy = Object.keys(copy).length > 0;
    if (refreshedInCopy && (this.rotates ?? true)) {
      this.events.push({ tag, where: 'copy', rotatedInCopy: true, writtenBack: syncCredentials(home, this.#real) });
      this.#last = this.#read(this.#real);
      return true;
    }
    this.events.push({ tag, where: 'copy', rotatedInCopy: refreshedInCopy, writtenBack: [] });
    return false;
  }
}

/**
 * A home's credential files by `lstat` ALONE — name, inode, size, mtime; no file is opened (J1 of the Opus judge on E1: an owned launch must not read
 * the person's refresh token to learn that a refresh happened — a refresh rewrites the file, so its size or time moves). `list`/`stat` are injected.
 */
export function credentialShape(home, { list = readdirSync, stat = lstatSync } = {}) {
  const dir = join(home, 'credentials');
  if (!existsSync(dir)) return {};
  return Object.fromEntries(list(dir).filter((n) => n.endsWith('.json')).sort((a, b) => a.localeCompare(b)).map((n) => {
    const s = stat(join(dir, n));
    return [n, `${s.ino}:${s.size}:${Math.round(s.mtimeMs)}`];
  }));
}

/**
 * The sign-in watch of one probe run. Every launch is watched by SHAPE (`credentialShape` — nothing opened). The content-reading
 * `CredentialGuard` — which parses every credential file and hashes its refresh token — is built LAZILY, by `content()`, only when a copied-credentials
 * (legacy) cell actually runs; once built, real-home runs feed it too, so the rotation is still learned. `read` is injected for the S-N2 twin.
 */
export class CredentialWatch {
  #real;
  #read;
  #shape;
  #last;
  #content = null;
  #events = [];

  constructor(real, { read = credentialFingerprint, shape = credentialShape } = {}) {
    this.#real = real;
    this.#read = read;
    this.#shape = shape;
    this.#last = JSON.stringify(shape(real));
  }

  /** The content-reading guard — built on the first call, never before. */
  content() {
    this.#content ??= new CredentialGuard(this.#real, { read: this.#read });
    return this.#content;
  }

  get rotates() { return this.#content?.rotates ?? null; }
  get events() { return [...this.#events, ...(this.#content?.events ?? [])]; }

  /** After a launch on the real home or an owned home: did the sign-in's files move (by shape)? Whether the token rotated is unknown here. */
  afterRealRun(tag) {
    const now = JSON.stringify(this.#shape(this.#real));
    const moved = now !== this.#last;
    if (moved) this.#events.push({ tag, where: 'real', refreshed: true, rotated: null });
    this.#last = now;
    const contentMoved = this.#content?.afterRealRun(tag) ?? false;
    return moved || contentMoved;
  }

  /**
   * After a launch on a copied home: the content guard may write the live pair BACK into the real home, so the shape baseline is re-read afterwards -
   * otherwise the next real-home run would be blamed for a write this launch made.
   */
  afterCopiedRun(home, tag) {
    const refreshed = this.content().afterCopiedRun(home, tag);
    this.#last = JSON.stringify(this.#shape(this.#real));
    return refreshed;
  }
}

/** The refusal of a cell that would COPY the person's sign-in (and so read it): not unless the person passed `--allow-copied-credentials`. */
export const copiedCredentialsRefusal = (allowed) => (allowed ? '' : 'this cell copies the credentials of the person into a scratch home and reads them to keep the refresh token alive; it is refused unless --allow-copied-credentials is passed (a native-host series never passes it)');

// ----------------------------------------------------------------------------------------------- configs and canaries

/** A fixed Python snippet: the path arrives as argv[1], so nothing about the path is ever Python source (the Vibe probe's rule). */
export const CANARY_SNIPPET = "import sys; open(sys.argv[1], 'w').write('x')";
const toml = (s) => JSON.stringify(s); // a JSON string is a valid TOML basic string
export const canaryCommand = (path, platform = process.platform) => `${pythonCommand(platform)} -c "${CANARY_SNIPPET}" "${fwd(path)}"`;

/** `[[hooks]]` as Kimi's own docs spell it (customization/hooks): event, command, timeout; nothing else, or the file fails to load. */
export function hooksToml(canary, event = 'SessionStart') {
  return `\n[[hooks]]\nevent = ${toml(event)}\ncommand = ${toml(canaryCommand(canary))}\ntimeout = 30\n`;
}

/** `mcp.json` as Kimi's own docs spell it (customization/mcp): a stdio server whose command writes the canary and exits. */
export function mcpJson(canary) {
  return `${JSON.stringify({ mcpServers: { 'probe-canary': { command: pythonCommand(), args: ['-c', CANARY_SNIPPET, fwd(canary)] } } }, null, 2)}\n`;
}

// --------------------------------------------------------------------------------------------------------- prompt mode

/** `kimi -p` — the positive-control launch: no client, Kimi's own `auto` permission (RESULTS §1), stream-json on stdout. */
export function promptMode(exe, { cwd, env, prompt, timeoutMs, viaStdin = false }) {
  const args = ['-p', viaStdin ? '-' : prompt, '--output-format', 'stream-json'];
  return run(exe, args, { cwd, env, input: viaStdin ? prompt : '', encoding: 'utf8', timeout: timeoutMs, maxBuffer: 64 * 1024 * 1024 });
}

/** One stream-json line as an object, or null when it is blank or not JSON. */
function jsonLine(raw) {
  if (!raw.trim()) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

/** A message's text, whether its content is a string or an array of parts. */
function contentText(content) {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content.map((part) => (typeof part === 'string' ? part : part?.text ?? '')).join('');
}

/**
 * The assistant text and the session id out of stream-json lines: the LAST non-blank assistant message, the LAST resume
 * hint's session id, how many lines parsed, and how many of each role. (Split at a cognitive complexity of 20, SonarCloud
 * on #803; a `null` line, which the one function threw on, is now skipped like any line that is not an object.)
 */
export function parseStreamJson(text) {
  const elements = String(text ?? '').split('\n').map(jsonLine).filter((el) => el !== null);
  const roles = {};
  for (const el of elements) roles[el.role ?? '?'] = (roles[el.role ?? '?'] ?? 0) + 1;
  const answer = elements.filter((el) => el.role === 'assistant').map((el) => contentText(el.content)).filter((t) => t.trim()).at(-1) ?? '';
  const hint = elements.filter((el) => el.role === 'meta' && el.type === 'session.resume_hint').at(-1);
  return { answer, sessionId: hint ? hint.session_id ?? '' : '', lines: elements.length, roles };
}

// ------------------------------------------------------------------------------------------------- the session store

/** The session folder Kimi recorded for `sessionId` in `home`, and what it holds — the facts the shim's report cannot carry. */
export function sessionFacts(home, sessionId) {
  const index = join(home, 'session_index.jsonl');
  if (!sessionId || !existsSync(index)) return { found: false };
  const line = readFileSync(index, 'utf8').split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch { return null; } })
    .find((l) => l?.sessionId === sessionId);
  if (!line) return { found: false };
  const dir = line.sessionDir;
  try {
    const agents = existsSync(join(dir, 'agents')) ? readdirSync(join(dir, 'agents')) : [];
    const wire = join(dir, 'agents', 'main', 'wire.jsonl');
    const wireText = existsSync(wire) ? readFileSync(wire, 'utf8') : '';
    const count = (re) => (wireText.match(re) ?? []).length;
    const log = join(dir, 'logs', 'kimi-code.log');
    return {
      found: true, sessionDir: dir, workDir: line.workDir, subagents: agents.filter((a) => a !== 'main'), wireBytes: wireText.length,
      wireMentions: { hook: count(/hook/giu), mcp: count(/mcp/giu), trust: count(/trust/giu), skill: count(/skill/giu), agentsMd: count(/AGENTS\.md/gu) },
      log: existsSync(log) ? readFileSync(log, 'utf8') : '',
    };
  } catch (e) {
    // A deep home's session path can be one this process cannot open with long paths off (M-K8): the index line is the fact.
    return { found: true, sessionDir: dir, workDir: line.workDir, subagents: [], wireBytes: 0, error: String(e.code ?? e.message), log: '' };
  }
}

// ------------------------------------------------------------------------------------- a small ACP client (M-K5, M-K7)

/** The option that says no, as AcpSession.Choose does: reject_once, else the first reject*, else cancelled. */
function choose(options) {
  const offered = Array.isArray(options) ? options.filter((o) => o && typeof o === 'object') : [];
  const hit = offered.find((o) => o.kind === 'reject_once') ?? offered.find((o) => String(o.kind ?? '').startsWith('reject'));
  return hit && offered.filter((o) => o.optionId === hit.optionId).length === 1 ? { optionId: hit.optionId, kind: hit.kind } : null;
}

/**
 * FolderReadBoundary's rule, lexically: an absolute path inside `root` whose components hold no excluded name. The root prefix
 * is case-folded on Windows only — on Linux `/w/Repo` and `/w/repo` are two folders; the excluded names stay case-insensitive
 * everywhere (the stricter reading).
 */
function boundaryAnswer(root, requested) {
  const rootFull = fold(resolve(root)) + sep;
  const full = resolve(String(requested ?? ''));
  if (!fold(full).startsWith(rootFull)) return { served: false, reason: 'outside the working folder' };
  const parts = full.slice(rootFull.length).split(/[\\/]/u).filter(Boolean).map((p) => p.toLowerCase());
  for (const ex of EXCLUSIONS) {
    const exParts = ex.toLowerCase().split('/');
    for (let at = 0; at + exParts.length <= parts.length; at++) {
      if (exParts.every((p, i) => parts[at + i] === p)) return { served: false, reason: `'${ex}' is not served — a vendor configuration or instruction path` };
    }
  }
  if (!existsSync(full) || statSync(full).isDirectory()) return { served: false, reason: 'no such file in the working folder' };
  return { served: true, text: readFileSync(full, 'utf8') };
}

/**
 * Whether a child may still be killed by its pid: it has neither exited nor been killed. After either, the pid may already be another
 * process's on POSIX (the E5.2 review, M3) — so a kill that comes late is skipped, never sent to whoever holds the number now.
 */
export const stillRunning = (child) => child.exitCode === null && child.signalCode === null;

/** How much of a replayed text chunk a `loadUpdates` row keeps — enough to recognise an earlier answer, small enough to keep a row small. */
export const LOAD_TEXT_CHARS = 120;

/** One `session/update` that arrived while `session/load` was open: its kind, and for a text chunk the start of its text (E5.1 decision 1 (a)). */
export function loadUpdate(update) {
  const kind = String(update?.sessionUpdate ?? '');
  return update?.content?.type === 'text' ? { kind, text: String(update.content.text ?? '').slice(0, LOAD_TEXT_CHARS) } : { kind };
}

/** How long a turn ended like the shim waits for the agent to go quiet after `session/cancel` — and the most it waits (E5.1 decision 1 (b)). */
const QUIET_MS = 250;
const QUIET_AT_MOST_MS = 1000;

/** Resolves once the agent has written nothing for QUIET_MS, or QUIET_AT_MOST_MS after it was asked — whichever comes first. */
function quiet(lastOutput) {
  const started = Date.now();
  return new Promise((done) => {
    const look = () => (Date.now() - lastOutput() >= QUIET_MS || Date.now() - started >= QUIET_AT_MOST_MS ? done() : setTimeout(look, 25));
    look();
  });
}

/** Resolves when the child has exited, or after `ms` — a kill that did not land must not hold the probe. */
const exitedWithin = (exited, ms) => Promise.race([exited, new Promise((done) => setTimeout(done, ms))]);

/**
 * One ACP conversation against `kimi acp`, NOT through the shim — used only where the shim has no path yet: `session/load`
 * (M-K5; S5 adds it to the shim) and a `session/new` whose `configOptions` name the models (M-K7, no prompt, no spend).
 * It mirrors the shim's client decisions — the same `initialize` offer, `mcpServers: []`, every permission refused by kind,
 * reads served by the boundary rule, every other request an error — and records every line both ways.
 */
export function acpTurn({ exe, args = ['acp'], cwd, env, deadlineMs, model = '', prompt = '', load = '', followUp = '', endLikeTheShim = false }) {
  return new Promise((done) => {
    // On POSIX the agent leads its own process group, so the end below reaches every descendant that stayed in it (plan §5).
    const child = spawn(exe, args, { cwd, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, detached: process.platform !== 'win32' });
    const state = { pid: child.pid, lines: [], answer: '', answer2: '', sessionId: load, refusals: [], reads: [], toolCalls: {}, usage: null,
      sessionResult: null, stopReason: '', error: '', exit: null, stderrTail: '', prompting: false, second: false, loading: false, loadUpdates: [] };
    const pending = new Map();
    let nextId = 1;
    let finished = false;
    let buffered = '';
    let stderr = '';
    let lastOutput = Date.now();
    const exited = new Promise((gone) => child.on('exit', gone));
    child.stdin.on('error', () => { /* the agent is gone; its pipe with it */ });
    // The probe's own ending (M-K5): stdin closed, 1.5 s for the agent to leave, then the tree kill.
    const stdinFirst = () => new Promise((ended) => {
      try { child.stdin.end(); } catch { /* already closed */ }
      setTimeout(() => { if (stillRunning(child)) killTree(child.pid); setTimeout(ended, 300); }, 1500);
    });
    // The shim's ending (AcpClient + AcpTransport.EndAsync): session/cancel when a session exists, a short quiet, the tree kill while the agent
    // is still alive, and only then stdin closed — so an agent that would leave on EOF never takes its children out of the kill's reach.
    const likeTheShim = async () => {
      if (state.sessionId) {
        send({ jsonrpc: '2.0', method: 'session/cancel', params: { sessionId: state.sessionId } });
        lastOutput = Date.now();
        await quiet(() => lastOutput);
      }
      if (stillRunning(child)) killTree(child.pid);
      try { child.stdin.end(); } catch { /* already closed */ }
      await exitedWithin(exited, 5000);
    };
    const finish = (why) => {
      if (finished) return;
      finished = true;
      state.error ||= why;
      clearTimeout(timer);
      // Not awaited (finish runs from timers and stream events); an ending that throws still ends the turn, so done() is always called.
      void (endLikeTheShim ? likeTheShim() : stdinFirst())
        .catch((e) => { state.error ||= `the ending failed: ${e?.message ?? e}`; })
        .then(() => { state.stderrTail = stderr.slice(-1500); done(state); });
    };
    const timer = setTimeout(() => finish(`deadline ${deadlineMs} ms`), deadlineMs);
    const send = (obj) => { const line = JSON.stringify(obj); state.lines.push(`> ${line}`); child.stdin.write(`${line}\n`); };
    const call = (method, params) => new Promise((res, rej) => { const id = nextId++; pending.set(id, { res, rej }); send({ jsonrpc: '2.0', id, method, params }); });
    const reply = (id, result) => send({ jsonrpc: '2.0', id, result });
    const fail = (id, code, message) => send({ jsonrpc: '2.0', id, error: { code, message } });
    const onRequest = (m) => {
      if (m.method === 'session/request_permission') {
        const c = choose(m.params?.options);
        state.refusals.push({ tool: m.params?.toolCall?.title ?? '', kind: m.params?.toolCall?.kind ?? '', option: c ? c.kind : 'cancelled' });
        return reply(m.id, { outcome: c ? { outcome: 'selected', optionId: c.optionId } : { outcome: 'cancelled' } });
      }
      if (m.method === 'fs/read_text_file') {
        const a = boundaryAnswer(cwd, m.params?.path);
        state.reads.push({ path: m.params?.path ?? '', served: a.served, reason: a.reason ?? '' });
        return a.served ? reply(m.id, { content: a.text }) : fail(m.id, -32602, a.reason);
      }
      return fail(m.id, -32601, `\`${m.method}\` is not offered by this client`);
    };
    const onNotification = (m) => {
      const u = m.params?.update ?? {};
      if (state.loading) state.loadUpdates.push(loadUpdate(u));
      if (u.sessionUpdate === 'agent_message_chunk' && state.prompting && u.content?.type === 'text') state[state.second ? 'answer2' : 'answer'] += u.content.text ?? '';
      if (u.sessionUpdate === 'usage_update') state.usage = { used: u.used, size: u.size };
      if ((u.sessionUpdate === 'tool_call' || u.sessionUpdate === 'tool_call_update') && u.toolCallId) {
        const was = state.toolCalls[u.toolCallId] ?? {};
        state.toolCalls[u.toolCallId] = { title: u.title ?? was.title ?? '', kind: u.kind ?? was.kind ?? '', status: u.status ?? was.status ?? '' };
      }
    };
    child.stdout.on('data', (chunk) => {
      lastOutput = Date.now();
      buffered += chunk.toString('utf8');
      let at;
      while ((at = buffered.indexOf('\n')) >= 0) {
        const line = buffered.slice(0, at).replace(/\r$/u, '');
        buffered = buffered.slice(at + 1);
        if (!line.trim()) continue;
        state.lines.push(`< ${line}`);
        let m;
        try { m = JSON.parse(line); } catch { continue; }
        if (m.method && m.id !== undefined && m.id !== null) onRequest(m);
        else if (m.method) onNotification(m);
        else if (pending.has(m.id)) {
          const p = pending.get(m.id);
          pending.delete(m.id);
          if (m.error) p.rej(new Error(`${m.error.code}: ${m.error.message}`)); else p.res(m.result ?? {});
        }
      }
    });
    child.stderr.on('data', (c) => { stderr += c.toString('utf8'); });
    child.on('exit', (code) => {
      state.exit = code;
      for (const p of pending.values()) {
        p.rej(new Error(`the agent exited with code ${code}`));
      }
      pending.clear();
    });
    child.on('error', (e) => finish(`spawn: ${e.message}`));
    (async () => {
      try {
        await call('initialize', { protocolVersion: 1, clientCapabilities: { fs: { readTextFile: true, writeTextFile: false }, terminal: false } });
        state.loading = Boolean(load);
        state.sessionResult = load
          ? await call('session/load', { sessionId: load, cwd, mcpServers: [] }).finally(() => { state.loading = false; })
          : await call('session/new', { cwd, mcpServers: [] });
        state.sessionId = state.sessionResult?.sessionId || load;
        if (model) await call('session/set_config_option', { sessionId: state.sessionId, configId: 'model', value: model });
        if (prompt) {
          state.prompting = true;
          const r = await call('session/prompt', { sessionId: state.sessionId, prompt: [{ type: 'text', text: prompt }] });
          state.stopReason = r?.stopReason ?? '';
        }
        if (followUp) {
          // A second prompt on the SAME session — what the one-turn shim cannot do: `/usage` after a paid turn (M-K2).
          state.second = true;
          await call('session/prompt', { sessionId: state.sessionId, prompt: [{ type: 'text', text: followUp }] });
        }
        finish('');
      } catch (e) {
        finish(String(e.message ?? e));
      }
    })().catch((e) => finish(`client: ${String(e?.message ?? e)}`));
  });
}

/**
 * The sentence that refuses an arm which asked for an owned root (`requireOwned`, set by lib/kimi-arms.mjs for the cells of the 2.2.0 window) and has none —
 * no `<root>/home` beside `<root>/os`, or not marked `owned` — or '' (an arm that never asked is not judged). Checked before anything launches.
 */
export function ownedRefusal(spec) {
  if (!spec.requireOwned) return '';
  if (spec.owned === true && rootOfHome(spec.home ?? '') !== '') return '';
  return 'this arm has no owned root (<root>/home beside <root>/os) - it is refused, not run on the real KIMI_CODE_HOME (E4.3)';
}

/**
 * Whether a launch is on a home the probe FILLED with the person's credentials (`filled`: the homes `copiedHome` put credentials into). Only those use the
 * content-reading guard: an owned home, a junctioned one, an empty one and a copy made `credentials: false` carry none, so none of them may build it
 * (re-check of J1: M-K7's empty-home and no-credentials arms used to, and every later launch then re-read the sign-in).
 */
export const launchesOnCopiedCredentials = (spec, filled) => Boolean(spec.home) && filled.has(spec.home);
