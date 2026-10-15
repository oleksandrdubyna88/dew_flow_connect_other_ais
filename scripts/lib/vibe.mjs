// lib/vibe.mjs — what every Vibe measurement script here launches with, and how it starts a child.
//
// Shared by scripts/probe-vibe-confinement.mjs (the confinement cells, research/RESULTS_vibe_confinement.md §3–§21) and
// scripts/vibe-home-manifest.mjs (the owned home's hash manifest, D17 of todo/PLAN_vibe_and_kimi_runtimes.md). It was
// private to the probe until the manifest step needed the same flag set, the same minimal environment and the same
// tree kill: two copies of the flag set would be two measurements of two different launches the day one of them moved.
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';

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

/** This process's values for MINIMAL_WINDOWS, matched case-insensitively as Windows does — absent names stay absent. */
export function minimalWindowsEnv() {
  const env = {};
  for (const name of MINIMAL_WINDOWS) {
    const key = Object.keys(process.env).find((k) => k.toLowerCase() === name.toLowerCase());
    if (key !== undefined) env[name] = process.env[key];
  }
  return env;
}

/**
 * The FIXED variables of D17's built environment — every name coai sets beside the home and the model, with its value:
 * the Python CLI pinned (§3) and Python's UTF-8 mode on (§26: without it a character outside cp1252 killed the run with
 * exit 2). The product builds the same in VibeLaunch.Built; VibePrintsUtf8OnEveryPathTests (src_mcp/tests) parses this
 * literal and fails unless it is the product's set exactly. Keep it ONE object literal of quoted values.
 */
export const BUILT_FIXED = { VIBE_CLI: 'python', PYTHONUTF8: '1' };

/** D17's BUILT environment for `home` and `model`: MinimalWindows, the home, the fixed variables and the model — nothing else. */
export const builtEnv = (home, model) => ({ ...minimalWindowsEnv(), VIBE_HOME: home, ...BUILT_FIXED, VIBE_ACTIVE_MODEL: model });

export const fwd = (p) => p.replaceAll('\\', '/');

/** A Windows system tool by its absolute path, never looked up through PATH (SonarCloud S4036, the code round 2026-10-12). */
export const SYSTEM32 = (exe) => join(process.env.SystemRoot ?? String.raw`C:\Windows`, 'System32', exe);

/**
 * Every child these scripts start goes through here: exe + argv (never a shell string, except mklink, which is a cmd
 * builtin), always a timeout, and a timeout kills the child's WHOLE tree by its PID before this returns — a stuck vibe
 * leaves python children behind otherwise (the code round, codex, 2026-10-12). Never by image name.
 */
export function run(file, args, options) {
  const r = spawnSync(file, args, { ...options, timeout: options.timeout ?? 60_000 });
  const timedOut = r.error?.code === 'ETIMEDOUT' || Boolean(r.signal);
  if (timedOut && r.pid) {
    if (process.platform === 'win32') {
      spawnSync(SYSTEM32('taskkill.exe'), ['/PID', String(r.pid), '/T', '/F'], { encoding: 'utf8', timeout: 15_000 });
    } else {
      try { process.kill(-r.pid, 'SIGKILL'); } catch { /* the group is already gone */ }
    }
  }
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

