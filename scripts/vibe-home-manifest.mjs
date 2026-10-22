// The Vibe home manifest (D17 of todo/PLAN_vibe_and_kimi_runtimes.md): what Vibe itself writes into an EMPTY coai-owned
// home, hashed, labelled with the Vibe version that wrote it. PAID: one tiny turn on the person's Mistral subscription
// (about 7 000 input tokens; research/RESULTS_vibe_confinement.md §24 holds the first run).
//
// Why it exists: every coai launch of Vibe runs on a coai-owned VIBE_HOME that is VALIDATED before the launch (VibeHome,
// src_mcp/runners/Reviewers/VibeHome.cs). Vibe unpacks its bundled plugin artifacts into that home on its first start
// (§22 of the RESULTS: names and sizes, never hashes), and "any file under logs/session/plugins/" would admit whatever a
// tampered or upgraded Vibe put there. So the validation admits those bytes only through this manifest: one entry per Vibe
// version, path → SHA-256. A Vibe version with no entry is RUN with a warning naming this script (the owner's decision of
// 2026-10-13: warn, not refuse) — its plugin files admitted by name, its home's shape still checked; one run of this script
// measures it.
//
//   node scripts/vibe-home-manifest.mjs [--vibe <exe>] [--scratch <dir>] [--write]
//   node scripts/vibe-home-manifest.mjs --from <scratch> [--write]   rebuild from a run's saved steps.json — no launch
//
// Without --write it prints the entry and writes nothing in the repository. With --write it replaces (or adds) this
// version's entry in shared/vibe-home-manifest.json and leaves every other version's entry as it was.
//
// What it runs, every launch in the BUILT environment coai gives Vibe (D17): ProcessEnvironment.MinimalWindows plus exactly
// VIBE_HOME (a fresh, empty, short folder under <scratch>), VIBE_CLI=python (§3: the Python CLI, the measured one) and
// VIBE_ACTIVE_MODEL (the curated default, mistral-medium-3.5). Nothing else of this process's environment reaches Vibe.
//   1. home A: `vibe --version`                          → the version label, and what a bare start writes
//   2. home A: one tiny turn with the SHIPPED flag set    → what a review's first launch leaves (the §22 inventory)
//   3. home A: `vibe --version` again                     → whether a later start rewrites what the first wrote
//   4. home B (fresh): `vibe --version`                   → whether the plugin bytes are the same in another home
// Every path Vibe wrote must match the plugin prefix (pinned by hash) or one of VARYING below (admitted by name, because
// its bytes are Vibe's caches, logs and sessions). A path that matches neither stops the script without writing: a person
// decides whether VibeHome may admit it, and that decision is a code change, not a manifest edit.
//
// The person's ~/.vibe is never read or written, and nothing outside <scratch> is created or deleted.
//
// PER PLATFORM (DL3 of research/PLAN_kimi_and_vibe_on_linux.md): the entry carries `platform` (node's spelling) and replaces only the
// entry of the same version AND platform — on Linux Vibe unpacks a different plugin package (L-V-manifest: 11 of 12 pinned
// files hash differently), so a `linux` entry sits beside the `win32` one. On Linux the homes are signed in through the `.env`
// LINK to the person's Vibe home's `.env` (L-V1: no keyring on WSL) — made by lstat alone, never opened, and never hashed: the
// inventory names a link without reading through it, and the sign-in link is coai's, so it is not counted as something Vibe wrote.
//
//   --stage            write the entry into `staged`, not `versions`: coai-mcp up to 0.57.x reads `versions` by version alone
//                      (VibeLaunch.Verdict matches exactly ONE entry), so a second 2.26.1 there would turn every Windows 2.26.1
//                      launch into an "unmeasured" one. A staged entry is not enforced by anything until it is moved.
//   --unstage <platform>  move that platform's staged entries into `versions` (no launch) — for the change that makes the C# read `platform` (E2).
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolved } from '../.github/scripts/lib/resolved.mjs';
import { runsOnNativeLinux } from './lib/host-facts.mjs';
import { manifestPlan } from './lib/cli-args.mjs';
import { builtEnv as builtEnvFor, classOf as classOfIn, deepestChild, linkSignIn, personsVibeHome, run, vibeArgs } from './lib/vibe.mjs';
import { inventoryOf, unstaged, withEntry, withoutSignInLink } from './lib/vibe-manifest.mjs';

// A flag that takes a value and gets none (or another --flag) is a USAGE error, decided by lib/cli-args.mjs before anything is read or launched. It used to be read as
// "flag not given": `--unstage` alone fell through to measure() and started Vibe on a paid turn.
const plan = manifestPlan(process.argv.slice(2));
if (plan.mode === 'usage') {
  console.error(`usage: vibe-home-manifest.mjs [--vibe <exe>] [--scratch <dir>] [--write] | --from <scratch> [--write] | --unstage <platform>
${plan.error}`);
  process.exit(65);
}
const flag = (name, fallback) => plan.values[name] ?? fallback;
const write = plan.flags.includes('--write');
const stage = plan.flags.includes('--stage');
const unstage = flag('--unstage', '');
const from = flag('--from', '');

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = join(ROOT, 'shared', 'vibe-home-manifest.json');

if (unstage) {
  const moved = unstaged(JSON.parse(readFileSync(MANIFEST, 'utf8')), unstage);
  writeFileSync(MANIFEST, `${JSON.stringify(moved, null, 2)}\n`);
  console.error(`moved the staged ${unstage} entries into versions: ${MANIFEST}`);
  process.exit(0);
}

const exe = from ? '' : flag('--vibe', '') || resolved('vibe');
const scratch = from || flag('--scratch', '') || mkdtempSync(join(tmpdir(), 'testsToDelete-vhm-'));
mkdirSync(scratch, { recursive: true });

/** The curated default model (D10): what a row with no model is launched on. */
const MODEL = 'mistral-medium-3.5';

/** Where Vibe's bundled plugin artifacts land: every file under it is pinned by its hash. */
const PINNED_PREFIX = 'logs/session/plugins/';

/**
 * Paths Vibe writes whose bytes are its own caches, logs and sessions — admitted by name (RESULTS §22's inventory). The
 * same list VibeHome holds; `*` is one path segment, `**` any number. Written into the manifest so the C# side reads it.
 */
const VARYING = [
  'connector_bootstrap_cache.json',
  '.connector_bootstrap_cache.json.lock',
  'experiment_eval_cache.json',
  'trusted_folders.toml',
  'vibehistory',
  'whoami_cache.json',
  'logs/vibe.log',
  'logs/session/active/.registry',
  'logs/session/unified/*/**',
  // Vibe's session index, one file beside the session folders (L-V-manifest, 2.26.1 on WSL2: it appears on a resumed home) - session data on every platform.
  'logs/session/unified/.session-index.json',
  // Vibe's plugin store stages an unpack here (vibe/plugins/_store.py:204): re-digested before it is renamed into packages/,
  // swept by Vibe itself, never loaded as a package - so it is admitted by name even though it lies under the pinned prefix.
  'logs/session/plugins/tmp/**',
];

/**
 * FOLDERS admitted - and no file under them (D4 of research/PLAN_vibe_live_round_fixes.md). A plugin's per-session data is where
 * Vibe adopts a plugin's runtime code without re-checking it (vibe/core/plugins/_materialize.py), so turn 1's two EMPTY folders
 * there are Vibe's own and a file is not: the product refuses one, and so does this script.
 */
const VARYING_FOLDERS = [
  'logs/session/plugins/data/**',
];

const RULES = { pinnedPrefix: PINNED_PREFIX, varying: VARYING, varyingFolders: VARYING_FOLDERS };
/** lib/vibe.mjs's classifier (the product's rules, one place): a folder is written `a/b/`, and is never hashed. */
const classIn = (inventoryOfAHome) => {
  const listing = inventoryOfAHome.map((entry) => entry.path);
  return (path) => classOfIn(path, RULES, listing);
};

/** The home's files and folders (lib/vibe-manifest.mjs inventoryOf: a link named, never read through). */
const inventory = (home) => inventoryOf(home);
/**
 * The `.env` sign-in link this script made is coai's, not Vibe's: it is left out of what Vibe wrote — only a link whose target is exactly
 * the one linkSignIn makes (the person's Vibe home's `.env`). Any OTHER link, and a changed `.env` link, stays in, as unknown.
 */
const vibesOwn = (listing) => withoutSignInLink(listing, join(personsVibeHome(), '.env'));

/** The built environment of D17, for `home` — lib/vibe.mjs's, the product's (VibeLaunch.Built). */
/**
 * On a NATIVE Linux host (E2.S0: the kernel banner read once, lib/host-facts.mjs runsOnNativeLinux) the built environment also carries the forced keyring backend, as the product's
 * VibeLaunch.Built does there — so the manifest's launches are the signed-in launches N-V4 measures. Recorded in steps.json as `keyringRoad`.
 */
const NATIVE_LINUX = runsOnNativeLinux();
const builtEnv = (home) => builtEnvFor(home, MODEL, process.platform, process.env, { nativeLinux: NATIVE_LINUX });

function launch(label, args, home, cwd, input = '') {
  console.error(`[manifest] ${label} …`);
  const r = run(exe, args, { cwd, input, encoding: 'utf8', timeout: 240_000, env: builtEnv(home), maxBuffer: 64 * 1024 * 1024 });
  return { label, exit: r.status, signal: r.signal, spawnError: r.error ? String(r.error) : '', stdout: String(r.stdout ?? ''), stderrTail: String(r.stderr ?? '').slice(-1500) };
}

/** The four steps, run — or, with --from, read back from the run that already paid for them. */
function measure() {
  const homeA = join(scratch, 'a');
  const homeB = join(scratch, 'b');
  const work = join(scratch, 'w');
  for (const d of [homeA, homeB, work]) mkdirSync(d, { recursive: true });
  // Linux: the sign-in is a LINK to the person's .env (L-V1); Windows needs none (the Credential Manager) and gets none.
  const signIn = process.platform === 'win32' ? 'none (Windows)' : [homeA, homeB].map((home) => linkSignIn(home)).map((r) => (r.linked ? 'linked' : r.why)).join('; ');
  console.error(`[manifest] sign-in: ${signIn}`);

  const steps = [];
  const v1 = launch('home A: vibe --version', ['--version'], homeA, work);
  steps.push({ ...v1, after: inventory(homeA) });
  // By piece, not by an unanchored `(\d+\.\d+\.\d+)`, which backtracks quadratically on a long run of digits (SonarCloud
  // S8786 on #803; 1.5 s at 40 000). The pieces are the runs of digits and dots; the version is the first that is N.N.N.
  const version = v1.stdout.split(/[^\d.]+/u).find((piece) => /^\d+\.\d+\.\d+$/u.test(piece)) ?? '';
  if (v1.exit !== 0 || version.length === 0) {
    console.error(`vibe --version did not answer a version (exit ${v1.exit}): ${v1.stdout}${v1.stderrTail}`);
    process.exit(69);
}

const exportDir = join(scratch, 'export');
const turn = launch('home A: one tiny turn', vibeArgs({ workdir: work, exportDir }), homeA, work, 'Reply with exactly: OK');
const exported = existsSync(join(exportDir, 'export.json')) ? JSON.parse(readFileSync(join(exportDir, 'export.json'), 'utf8')) : null;
steps.push({ ...turn, stdout: turn.stdout.slice(0, 400), after: inventory(homeA),
  export: exported && { outcome: exported.outcome, vibe_version: exported.vibe_version, active_model: exported.config?.active_model ?? null, usage: exported.usage } });
if (turn.exit !== 0) {
  console.error(`the turn failed (exit ${turn.exit}): ${turn.stderrTail}`);
  process.exit(70);
}

const v2 = launch('home A: vibe --version again', ['--version'], homeA, work);
steps.push({ ...v2, after: inventory(homeA) });
const v3 = launch('home B: vibe --version', ['--version'], homeB, work);
steps.push({ ...v3, after: inventory(homeB) });
writeFileSync(join(scratch, 'steps.json'), JSON.stringify({ exe, version, scratch, platform: process.platform, keyringRoad: NATIVE_LINUX ? 'closed' : 'open', signIn, steps }, null, 2));
return { version, steps };
}

const { version, steps: measured } = from ? JSON.parse(readFileSync(join(from, 'steps.json'), 'utf8')) : measure();
const steps = measured.map((step) => ({ ...step, after: vibesOwn(step.after) }));

const afterTurn = steps[1].after;
const classOf = classIn(afterTurn);
const unknown = afterTurn.filter((f) => classOf(f.path) === 'unknown');
const pinned = afterTurn.filter((f) => classOf(f.path) === 'pinned');
// Stability: a pinned file must keep its bytes across a later start in the same home, and match in a fresh home that has it.
// One classifier per inventory, built BEFORE the filter: classIn lists the whole inventory once, and a call per entry listed it again.
const drift = [steps[2].after, steps[3].after].flatMap((later) => {
  const classOfLater = classIn(later);
  return later.filter((f) => classOfLater(f.path) === 'pinned')
    .filter((f) => pinned.some((p) => p.path === f.path && p.sha256 !== f.sha256)).map((f) => f.path);
});
const classOfSecond = classIn(steps[2].after);
const appeared = steps[2].after.filter((f) => classOfSecond(f.path) === 'pinned' && !pinned.some((p) => p.path === f.path)).map((f) => f.path);

// The deepest write below a home (review R3): the longest persisted file + Vibe's atomic-write wrapper. VibePathBudget's Windows
// DeepestChild must not be below it (a parity test); a Vibe that writes deeper turns the suite red until the budget is raised.
const depth = deepestChild(afterTurn.map((f) => f.path));

console.log(JSON.stringify({ version, pinned: pinned.length, varies: afterTurn.length - pinned.length - unknown.length, ...depth,
  unknown: unknown.map((f) => f.path), drift, appeared,
  bareStartWrote: steps[0].after.map((f) => f.path), homeBWrote: steps[3].after.map((f) => f.path), export: steps[1].export }, null, 2));

if (unknown.length > 0 || drift.length > 0 || appeared.length > 0) {
  console.error('not written: a path Vibe wrote is not classified, or a pinned file is not stable — a person decides (see steps.json)');
  process.exit(65);
}

const entry = {
  version,
  measured: new Date().toISOString().slice(0, 10),
  platform: process.platform,
  cli: 'python',
  harness: 'scripts/vibe-home-manifest.mjs',
  ...depth,
  pinned: pinned.map(({ path, sha256: hash, bytes }) => ({ path, sha256: hash, bytes })),
  seen: afterTurn.map(({ path, bytes }) => ({ path, bytes, class: classOf(path) })),
};

if (!write) {
  console.log(JSON.stringify(entry, null, 2));
  console.error(`not written (no --write); scratch: ${scratch}`);
  process.exit(0);
}

const WHY = [
  'What Vibe itself writes into an EMPTY coai-owned VIBE_HOME, per Vibe version (D17 of todo/PLAN_vibe_and_kimi_runtimes.md;',
  'the run is recorded in research/RESULTS_vibe_confinement.md). Generated by `node scripts/vibe-home-manifest.mjs --write`,',
  'never edited by hand. coai-mcp embeds it (VibeHome): before EVERY Vibe launch the owned home is validated - a file under',
  'pinnedPrefix is admitted only when its path and SHA-256 are in the entry for the INSTALLED Vibe version, a path matching',
  '`varying` is admitted by name (its bytes are caches, logs and sessions), a FOLDER matching `varyingFolders` is admitted and no',
  'file under it (a plugin per-session data folder), anything else - a hooks.toml, a config.toml, an',
  'agents/ or tools/ folder, a link - refuses the launch with the path named. A Vibe version with no entry here is RUN',
  "with a warning (the owner, 2026-10-13: warn, not refuse) - its plugin files admitted by name, its home's shape still checked;",
  're-run this script after every Vibe update to measure it. `seen` is informational. One entry per version PER PLATFORM',
  '(`platform` as node spells it; an entry without it is win32). `staged`: entries measured for a platform the shipped coai-mcp',
  'does not tell apart yet (it reads `versions` by version alone) - NOT enforced; `--unstage <platform>` moves them into',
  '`versions` in the change that makes the C# read `platform` (DL3 of research/PLAN_kimi_and_vibe_on_linux.md).',
];
const prior = existsSync(MANIFEST) ? JSON.parse(readFileSync(MANIFEST, 'utf8')) : { versions: [] };
const next = withEntry(prior, entry, { stage });
const manifest = {
  why: WHY,
  pinnedPrefix: PINNED_PREFIX,
  varying: VARYING,
  varyingFolders: VARYING_FOLDERS,
  versions: next.versions,
  ...(next.staged?.length ? { staged: next.staged } : {}),
};
writeFileSync(MANIFEST, `${JSON.stringify(manifest, null, 2)}\n`);
console.error(`wrote ${MANIFEST} for vibe ${version} (${entry.platform})${stage ? ', STAGED' : ''}; scratch: ${scratch}`);
