// lib/vibe.mjs — what every Vibe measurement script here launches with, and how it starts a child.
//
// Shared by scripts/probe-vibe-confinement.mjs (the confinement cells, research/RESULTS_vibe_confinement.md §3–§21) and
// scripts/vibe-home-manifest.mjs (the owned home's hash manifest, D17 of todo/PLAN_vibe_and_kimi_runtimes.md). It was
// private to the probe until the manifest step needed the same flag set, the same minimal environment and the same
// tree kill: two copies of the flag set would be two measurements of two different launches the day one of them moved.
//
// Linux (E1 of research/PLAN_kimi_and_vibe_on_linux.md, §5): every platform fact below takes the platform as an argument —
// defaulting to this process's — so both arms are tested on both operating systems, and an arm is never chosen by guessing.
import { spawnSync } from 'node:child_process';
import { existsSync, lstatSync, mkdirSync, readdirSync, rmdirSync, symlinkSync, unlinkSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, posix, relative, win32 } from 'node:path';

/**
 * ProcessEnvironment.MinimalWindows (src_mcp/runners/Processes/ProcessEnvironment.cs) — the question row's child
 * environment, and (D17) the base of every Vibe launch's BUILT environment. A copy of a C# list (a Node script cannot load
 * the assembly), so it is named here as one, and it cannot drift: TheProbeScriptsMinimalEnvironmentIsTheProductsTests
 * (src_mcp/tests) parses this literal and fails unless it is the C# set exactly. Keep it ONE array literal of quoted names;
 * every other script imports it from here.
 */
export const MINIMAL_WINDOWS = ['PATH', 'PATHEXT', 'SystemRoot', 'windir', 'ComSpec', 'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH',
  'APPDATA', 'LOCALAPPDATA', 'TEMP', 'TMP', 'USERNAME', 'ProgramData', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432',
  'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER', 'PROCESSOR_LEVEL', 'PROCESSOR_REVISION', 'OS', 'NUMBER_OF_PROCESSORS'];

/**
 * ProcessEnvironment.MinimalUnix — the same list for every other host. A copy of a C# list for the same reason as
 * MINIMAL_WINDOWS; lib/vibe.test.mjs parses ProcessEnvironment.cs and fails unless the two are the same set.
 */
export const MINIMAL_UNIX = ['PATH', 'HOME', 'USER', 'LOGNAME', 'SHELL', 'LANG', 'LC_ALL', 'LC_CTYPE', 'TERM', 'TMPDIR', 'TMP', 'TEMP', 'DOTNET_ROOT'];

const isWindows = (platform) => platform === 'win32';
/** The path flavour of a platform — a Linux path is built with `/` even by a test running on Windows. */
const pathOf = (platform) => (isWindows(platform) ? win32 : posix);

/**
 * `env`'s values for the platform's minimal list — absent names stay absent. Windows matches names case-insensitively, as
 * Windows does; every other platform takes the exact name only.
 */
export function minimalEnv(platform = process.platform, env = process.env) {
  const out = {};
  const names = isWindows(platform) ? MINIMAL_WINDOWS : MINIMAL_UNIX;
  for (const name of names) {
    const key = isWindows(platform) ? Object.keys(env).find((k) => k.toLowerCase() === name.toLowerCase()) : (name in env ? name : undefined);
    if (key !== undefined) out[name] = env[key];
  }
  return out;
}

/** This process's values for MINIMAL_WINDOWS, matched case-insensitively as Windows does — absent names stay absent. */
export const minimalWindowsEnv = () => minimalEnv('win32');

/**
 * The FIXED variables of D17's built environment — every name coai sets beside the home and the model, with its value:
 * the Python CLI pinned (§3) and Python's UTF-8 mode on (§26: without it a character outside cp1252 killed the run with
 * exit 2). The product builds the same in VibeLaunch.Built; VibePrintsUtf8OnEveryPathTests (src_mcp/tests) parses this
 * literal and fails unless it is the product's set exactly. Keep it ONE object literal of quoted values.
 */
export const BUILT_FIXED = { VIBE_CLI: 'python', PYTHONUTF8: '1' };

/**
 * The ONE name coai's NATIVE-Linux Vibe launch adds to the built environment beyond the four of BUILT_FIXED + VIBE_HOME/VIBE_ACTIVE_MODEL (DN10 of
 * todo/PLAN_kimi_and_vibe_on_native_linux.md, E2 of its plan): `keyring` 25.7.0 takes PYTHON_KEYRING_BACKEND from the environment BEFORE its config
 * file (core.py:96-115) and `fail.Keyring` raises on every call, which Vibe catches (vibe/utils/keyring.py:165-170) — so no keyring is reachable and
 * the sign-in can only come through the linked `.env`. The product's VibeLaunch.Built adds the same on HostKind.Linux. ONE object literal of quoted
 * values, like BUILT_FIXED, so a C# test can parse it (S-N21). Every probe child on a native host carries it (E2.S0) except arm a3c.
 */
export const NATIVE_LINUX_BUILT = { PYTHON_KEYRING_BACKEND: 'keyring.backends.fail.Keyring' };

/**
 * Whether the host is a NATIVE Linux one: this is Linux AND its kernel banner (host-facts.mjs readBanner: `{ read, text, namesWsl }`) was read and names
 * neither Microsoft nor WSL — the product's own rule (HostKind), and the one place the probes decide it. An unread banner is not native, as the
 * product's is not. PURE: the banner comes in, nothing is imported, so the probe's cut-out test can hand it over.
 */
export const isNativeLinuxHost = ({ platform, banner }) => platform === 'linux' && banner?.read === true && banner.namesWsl === false;

/** `closed` when the child environment carries the forced backend (the launch E2 ships on a native host), else `open` — printed in the probe's header and rows. */
export const keyringRoadOf = (env) => (env.PYTHON_KEYRING_BACKEND === NATIVE_LINUX_BUILT.PYTHON_KEYRING_BACKEND ? 'closed' : 'open');

/**
 * D17's BUILT environment for `home` and `model`: the platform's minimal list, the home, the fixed variables and the model — nothing else; on a
 * NATIVE Linux host (`nativeLinux`, read by the caller from the kernel banner through isNativeLinuxHost) also NATIVE_LINUX_BUILT, exactly as VibeLaunch.Built.
 */
export const builtEnv = (home, model, platform = process.platform, env = process.env, { nativeLinux = false } = {}) =>
  ({ ...minimalEnv(platform, env), VIBE_HOME: home, ...BUILT_FIXED, ...(nativeLinux ? NATIVE_LINUX_BUILT : {}), VIBE_ACTIVE_MODEL: model });

export const fwd = (p) => p.replaceAll('\\', '/');

/** A Windows system tool by its absolute path, never looked up through PATH (SonarCloud S4036, the code round 2026-10-12). */
export const SYSTEM32 = (exe) => join(process.env.SystemRoot ?? String.raw`C:\Windows`, 'System32', exe);

/** The bare name a canary hook or MCP server runs Python by: Ubuntu ships `python3` and no `python` unless python-is-python3 is installed. */
export const pythonCommand = (platform = process.platform) => (isWindows(platform) ? 'python' : 'python3');

/**
 * Vibe's OWN interpreter, where uv puts a tool: `%APPDATA%\uv\tools\mistral-vibe\Scripts\python.exe` on Windows; on Linux
 * `$UV_TOOL_DIR`, else `$XDG_DATA_HOME/uv/tools`, else `~/.local/share/uv/tools` (uv's storage docs), `mistral-vibe/bin/python`.
 */
export function vibePython(platform = process.platform, env = process.env, home = homedir()) {
  const p = pathOf(platform);
  if (isWindows(platform)) return p.join(env.APPDATA ?? '', 'uv', 'tools', 'mistral-vibe', 'Scripts', 'python.exe');
  const tools = env.UV_TOOL_DIR || p.join(env.XDG_DATA_HOME || p.join(home, '.local', 'share'), 'uv', 'tools');
  return p.join(tools, 'mistral-vibe', 'bin', 'python');
}

/** The person's Vibe home (DL2): `VIBE_HOME` of THIS process's environment when set, else `~/.vibe`. */
export const personsVibeHome = (env = process.env, home = homedir(), platform = process.platform) => env.VIBE_HOME || pathOf(platform).join(home, '.vibe');

const lstatOrNull = (p) => {
  try {
    return lstatSync(p);
  } catch {
    return null;
  }
};

/**
 * DL2's sign-in on Linux: `<home>/.env` a SYMLINK to the person's `<Vibe home>/.env` — made by `lstat` alone, so the person's
 * file is never opened, read or copied (a fresh owned home is signed out on WSL, L-V1). No link on Windows (the Credential
 * Manager holds the key there), none when the person has no `.env`, and none when theirs is itself a link (never chained).
 * Returns `{ linked, why }`; a refusal leaves nothing at `<home>/.env`. The product's own judgement of the target (owner,
 * mode, one descriptor) is E2's; this is the probe's.
 */
export function linkSignIn(home, { personsHome = personsVibeHome(), platform = process.platform } = {}) {
  if (isWindows(platform)) return { linked: false, why: 'on Windows Vibe reads the sign-in from the Credential Manager, so no .env link is made' };
  const target = pathOf(platform).join(personsHome, '.env');
  const stat = lstatOrNull(target);
  if (stat === null) return { linked: false, why: `no .env in ${personsHome} — the person is not signed in on this side` };
  if (stat.isSymbolicLink()) return { linked: false, why: `${target} is itself a link — a sign-in link is never chained` };
  if (!stat.isFile()) return { linked: false, why: `${target} is not a regular file` };
  try {
    symlinkSync(target, pathOf(platform).join(home, '.env'), 'file');
    return { linked: true, why: '' };
  } catch (e) {
    return { linked: false, why: `the .env link could not be made: ${e.code ?? e.message}` };
  }
}

/** A FOLDER link `link` → `target`: a junction on Windows (mklink /J, a cmd builtin, by cmd.exe's absolute path), a symlink elsewhere; the error text, or ''. */
export function dirLink(link, target, platform = process.platform) {
  if (isWindows(platform)) {
    const r = run(SYSTEM32('cmd.exe'), ['/d', '/c', 'mklink', '/J', link, target], { encoding: 'utf8', timeout: 30_000 });
    return r.status === 0 ? '' : `mklink /J failed: ${(r.stdout + r.stderr).trim()}`;
  }
  try {
    symlinkSync(target, link, 'dir');
    return '';
  } catch (e) {
    return `symlink failed: ${e.code ?? e.message}`;
  }
}

/** Removes a folder link by its OWN name, never what it points at: rmdir unlinks a junction on Windows; on POSIX rmdir refuses a symlink (ENOTDIR), unlink is the call. */
export const removeDirLink = (link, platform = process.platform) => (isWindows(platform) ? rmdirSync(link) : unlinkSync(link));

/** Ends a process tree by the pid this script started — never by image name: `taskkill /T` on Windows, its process GROUP elsewhere. */
export function killTree(pid, platform = process.platform) {
  if (isWindows(platform)) {
    spawnSync(SYSTEM32('taskkill.exe'), ['/PID', String(pid), '/T', '/F'], { encoding: 'utf8', timeout: 15_000 });
    return;
  }
  try {
    process.kill(-pid, 'SIGKILL');
  } catch {
    try { process.kill(pid, 'SIGKILL'); } catch { /* already gone */ }
  }
}

/**
 * util-linux's setsid(1) by its absolute path (S4036). spawnSync cannot detach a child, so on POSIX the child is started
 * THROUGH setsid: a child of node is no group leader, so setsid calls setsid(2) and execs in place — the same pid, now the
 * leader of its own session and process group, which is what a group kill needs. '' where it is not installed.
 */
const SETSID = ['/usr/bin/setsid', '/bin/setsid'].find((p) => existsSync(p)) ?? '';

/**
 * Every child these scripts start goes through here: exe + argv (never a shell string, except mklink, which is a cmd
 * builtin), always a timeout, and a timeout kills the child's WHOLE tree by its PID before this returns — a stuck vibe
 * leaves python children behind otherwise (the code round, codex, 2026-10-12). Never by image name. On POSIX the child
 * leads its own process group (through setsid), so the kill reaches every descendant that stayed in it (plan §5, measured
 * red first: without the group a grandchild outlived the timeout); a descendant that opened its own session is out of reach
 * of any group kill (L-K4 harness), which is DL9's subreaper question, not this script's.
 */
export function run(file, args, options) {
  const grouped = process.platform !== 'win32' && SETSID !== '';
  const r = grouped
    ? spawnSync(SETSID, [file, ...args], { ...options, timeout: options.timeout ?? 60_000 })
    : spawnSync(file, args, { ...options, timeout: options.timeout ?? 60_000 });
  const timedOut = r.error?.code === 'ETIMEDOUT' || Boolean(r.signal);
  if (timedOut && r.pid) killTree(r.pid);
  return r;
}

/**
 * Every file under `dir` (relative, forward slashes, sorted), not following links. With `folders`, every FOLDER too, as
 * `a/b/` — an empty one is invisible otherwise, and Vibe's turn 1 leaves two (PLAN_vibe_live_round_fixes.md §1(2)).
 */
export function files(dir, { folders = false } = {}) {
  if (!existsSync(dir)) return [];
  const out = [];
  const walk = (d) => {
    for (const e of readdirSync(d, { withFileTypes: true })) {
      const p = join(d, e.name);
      if (!e.isDirectory()) out.push(fwd(relative(dir, p)));
      else {
        if (folders) out.push(`${fwd(relative(dir, p))}/`);
        walk(p);
      }
    }
  };
  walk(dir);
  return out.sort((a, b) => a.localeCompare(b));
}

/**
 * A home's listing for a before/after comparison — names, sizes and modification times by `lstat`, so a link is named (and
 * flagged) and never followed, and no file is opened. The probes compare the person's own `~/.vibe` with it (plan §5).
 */
export function homeListing(dir) {
  return files(dir, { folders: true }).map((path) => {
    const s = lstatSync(join(dir, path));
    return { path, link: s.isSymbolicLink(), bytes: path.endsWith('/') ? 0 : s.size, mtimeMs: Math.round(s.mtimeMs) };
  });
}

const segments = (path) => path.split('/').filter((seg) => seg.length > 0);
const anyDepth = (glob) => glob.length > 0 && glob.at(-1) === '**';
const prefixMatches = (glob, parts, count) => Array.from({ length: count }, (_, at) => at).every((at) => glob[at] === '*' || glob[at] === parts[at]);

/** VibeHome.Matches: `*` is one segment, a final `**` one or more. */
const matches = (glob, parts) => (anyDepth(glob) ? parts.length >= glob.length && prefixMatches(glob, parts, glob.length - 1)
  : parts.length === glob.length && prefixMatches(glob, parts, glob.length));

/** VibeHome.Leads: `parts` is a folder a file matching the glob could live in. */
const leads = (glob, parts) => (anyDepth(glob) ? prefixMatches(glob, parts, Math.min(parts.length, glob.length - 1))
  : parts.length < glob.length && prefixMatches(glob, parts, parts.length));

/**
 * What the product's home check (src_mcp/runners/Vibe/VibeHome.cs) would do with `path` — a file, or a folder written as
 * `a/b/`: `pinned` (a file under the prefix, by hash), `varies` (a file admitted by name), `admitted-folder` (a folder a
 * varyingFolders glob matches or leads to), `pinned-lead` (a folder on the way to a pinned file in `listing`), `varies-lead`
 * (a folder on the way to a name), else `unknown`. varying and varyingFolders are judged BEFORE the pinned prefix, and a
 * FILE in a varyingFolders folder is `unknown` — coai admits a plugin's per-session data only as empty folders. `rules` is
 * `{ pinnedPrefix, varying, varyingFolders }`; `listing` the whole listing the path came from.
 */
export function classOf(path, rules, listing) {
  const parts = segments(path);
  const globs = (list) => list.map(segments);
  if (path.endsWith('/')) return folderClass(parts, rules, listing, globs);
  if (globs(rules.varyingFolders).some((glob) => matches(glob, parts))) return 'unknown';
  if (globs(rules.varying).some((glob) => matches(glob, parts))) return 'varies';
  return path.startsWith(rules.pinnedPrefix) ? 'pinned' : 'unknown';
}

function folderClass(parts, rules, listing, globs) {
  if (globs(rules.varyingFolders).some((glob) => matches(glob, parts) || leads(glob, parts))) return 'admitted-folder';
  if (globs(rules.varying).some((glob) => leads(glob, parts))) return 'varies-lead';
  const folder = `${parts.join('/')}/`;
  const leadsToPinned = rules.pinnedPrefix.startsWith(folder)
    || listing.some((other) => !other.endsWith('/') && other.startsWith(folder) && classOf(other, rules, listing) === 'pinned');
  return leadsToPinned ? 'pinned-lead' : 'unknown';
}

/** The extra characters Vibe's atomic write puts around a file name: `.` + name + `.<16 hex>.tmp` (RESULTS §27). */
const ATOMIC_WRAPPER = 22;

/**
 * The deepest write below a home: the longest PERSISTED file path in `listing` plus Vibe's atomic-write wrapper. The
 * transient `.tmp` itself cannot be seen after a run, so it is computed (D4, review R3); the session and chunk names are
 * written as roles in `deepestPersisted`.
 */
export function deepestChild(listing) {
  const longest = listing.filter((path) => !path.endsWith('/')).reduce((a, b) => (b.length > a.length ? b : a), '');
  const role = segments(longest).map((seg, at, all) => (all[at - 1] === 'unified' ? '<id>' : seg)).map((seg) => seg.replace(/[0-9a-f]{64}/u, '<64 hex>')).join('/');
  return { deepestChild: longest.length === 0 ? 0 : longest.length + ATOMIC_WRAPPER, deepestPersisted: role };
}

/**
 * The SHIPPED flag set (D2, D3, D7, D8 of the plan; src_mcp/runners/Reviewers/VibeRuntime.cs builds the same argv), with
 * a probe arm's deviations — `--trust` only for a positive control, `--resume` for a second turn.
 */
export function vibeArgs({ workdir, exportDir, trust = false, timeLimit = 120, resume = '' }) {
  return ['-p', '--output', 'json', '--agent', 'plan', '--enabled-tools', 'read_file', '--workdir', workdir,
    '--time-limit', String(timeLimit), '--output-dir', exportDir, ...(trust ? ['--trust'] : []), ...(resume ? ['--resume', resume] : [])];
}


// ------------------------------------------------------------------------------------------------------ M-V7's keyring arms (S-N17)

/** The backend coai's native-Linux Vibe launch forces (DN10) — NATIVE_LINUX_BUILT's value, the one spelling (E2.S0). */
export const KEYRING_FAIL_BACKEND = NATIVE_LINUX_BUILT.PYTHON_KEYRING_BACKEND;

const KEYRING_ARMS = ['a', 'a2', 'a3', 'a3c', 'b'];

/**
 * M-V7's arm `arm` (a, a2, a3, a3c, b) as the environment it adds to the child and the files it needs — PURE, no file is made. On Linux (the
 * launch E2 ships): arms a and a2 carry the forced backend; a3 carries it AND a `HOME` whose keyringrc.cfg names a keyring that IS available
 * (`<scratch>/fake-home`, written by `writeProbeKeyring`) — the forced backend must beat it, so the marker never appears; a3c is the same fake
 * HOME WITHOUT the variable — its teeth: the marker must appear, else a3 proved nothing. a3c ALONE has dropsKeyring: true: even on a native host, where
 * every probe child carries NATIVE_LINUX_BUILT, the probe deletes the name from a3c's child (E2.S0). On win32 every arm is today's (the Credential Manager
 * road, RESULTS §12) and a3/a3c do not apply. `applies: false` = the probe does not run that arm on this platform.
 */
export function keyringArm(arm, platform = process.platform, { scratch }) {
  if (!KEYRING_ARMS.includes(arm)) throw new Error(`keyringArm: no M-V7 arm "${arm}" (${KEYRING_ARMS.join(', ')})`);
  const none = { arm, applies: true, dropsKeyring: false, env: {}, fakeHome: '', markerFile: '' };
  if (isWindows(platform)) return { ...none, applies: !['a3', 'a3c'].includes(arm), env: arm === 'a2' ? { VIBE_TEST_DISABLE_KEYRING: '1' } : {} };
  const p = pathOf(platform);
  const fakeHome = p.join(scratch, 'fake-home');
  const forced = { ...NATIVE_LINUX_BUILT };
  return {
    a: { ...none, env: forced },
    a2: { ...none, env: { VIBE_TEST_DISABLE_KEYRING: '1', ...forced } },
    a3: { ...none, env: { ...forced, HOME: fakeHome }, fakeHome, markerFile: p.join(scratch, 'keyring-marker.txt') },
    a3c: { ...none, dropsKeyring: true, env: { HOME: fakeHome }, fakeHome, markerFile: p.join(scratch, 'keyring-marker.txt') },
    b: none,
  }[arm];
}

/** The probe's own keyring backend: AVAILABLE (so `keyring` would pick it) and empty (so no key exists anywhere); it notes only the SERVICE it was asked about. */
function probeBackendSource(markerFile) {
  return [
    '# The probe\'s keyring backend (scripts/lib/vibe.mjs writeProbeKeyring). It is available and holds nothing: every call appends the',
    '# service name it was asked about to the marker file and answers None, so the marker proves the backend was reached and no secret exists.',
    'from keyring.backend import KeyringBackend',
    '',
    `MARKER = ${JSON.stringify(fwd(markerFile))}`,
    '',
    '',
    'def _note(service):',
    '    with open(MARKER, \'a\', encoding=\'utf-8\') as marker:',
    '        marker.write(service + \'\\n\')',
    '',
    '',
    'class MarkerKeyring(KeyringBackend):',
    '    priority = 1',
    '',
    '    def get_password(self, service, username):',
    '        _note(service)',
    '        return None',
    '',
    '    def set_password(self, service, username, password):',
    '        _note(service)',
    '        return None',
    '',
    '    def delete_password(self, service, username):',
    '        _note(service)',
    '        return None',
    '',
  ].join('\n');
}

/**
 * Writes the fake home's keyring config and backend — ONLY under `fakeHome`; the marker file is named in the backend and made by it, never here.
 * Returns the written paths, relative to `fakeHome` with `/`. Python's `keyring` reads `<HOME>/.config/python_keyring/keyringrc.cfg`.
 */
export function writeProbeKeyring(fakeHome, markerFile) {
  const config = ['[backend]', 'default-keyring = probe_marker.MarkerKeyring', `keyring-path = ${fwd(join(fakeHome, 'keyring-backend'))}`, ''].join('\n');
  const parts = [['.config/python_keyring/keyringrc.cfg', config], ['keyring-backend/probe_marker.py', probeBackendSource(markerFile)]];
  for (const [rel, text] of parts) {
    mkdirSync(join(fakeHome, rel, '..'), { recursive: true });
    writeFileSync(join(fakeHome, rel), text);
  }
  return parts.map(([rel]) => rel);
}

/** `keyringArm`, and — for the two arms that need it — the fake home made. The one call the probe's M-V7 setup makes. */
export function keyringSetup(arm, platform = process.platform, { scratch }) {
  const spec = keyringArm(arm, platform, { scratch });
  if (spec.fakeHome) writeProbeKeyring(spec.fakeHome, spec.markerFile);
  return spec;
}

/** `--arms a3,a3c` (the Vibe probe): an arm runs when no filter is given, or when its name up to the first colon (`a3: forced backend …` → `a3`) is listed. */
export const armWanted = (arm, filter) => !filter || filter.length === 0 || filter.includes(arm.split(':')[0]);

/**
 * The `--arms` option of `argv`: `{ filter: [], error: '' }` when it is omitted (no filter = every arm), `{ filter: [names], error: '' }` when it names arms,
 * and an ERROR when it is given without a name (`--arms ""`, `--arms ,`, or no value at all). An empty list must not fall through to `armWanted`'s "no
 * filter", which would run every arm - the paid arm b included - for a caller who asked for a subset.
 */
export function armFilterOf(argv) {
  const at = argv.indexOf('--arms');
  if (at < 0) return { filter: [], error: '' };
  const raw = String(argv[at + 1] ?? '').trim();
  const names = raw.startsWith('-') ? [] : raw.split(',').map((n) => n.trim()).filter(Boolean); // another option (`--cells`) is not an arm name
  return names.length > 0 ? { filter: names, error: '' } : { filter: [], error: '--arms was given without an arm name (a3,a3c ...): refusing to run every arm, the paid one included' };
}

/**
 * With `--arms`, every selected cell must be an arm cell (`armsByCell[cell]`) that has at least one wanted arm; else a message naming the first cell that is not.
 * The filter only reaches the cells that run arms: M-V5 (the paid two-turn resume) has none and ran unchecked, and M-V9 launches a positive control after its
 * ordinary arm is filtered out - so the default cell list is not a compatible selection and the caller names the cells (`--cells M-V7`). '' without `--arms`.
 */
export function armsCellsError(filter, cells, armsByCell) {
  if (!filter || filter.length === 0) return '';
  const bad = cells.find((cell) => !(armsByCell[cell] ?? []).some((a) => armWanted(a.arm, filter)));
  return bad === undefined ? '' : `--arms ${filter.join(',')} selects no arm in cell ${bad}: name the cells it applies to (--cells M-V7) - without that ${bad} would still launch what the filter was meant to keep out`;
}
