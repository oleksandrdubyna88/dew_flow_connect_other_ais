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
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolved } from '../.github/scripts/lib/resolved.mjs';
import { builtEnv as builtEnvFor, files, run, vibeArgs } from './lib/vibe.mjs';

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = argv.indexOf(name);
  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : fallback;
};
const write = argv.includes('--write');
const from = flag('--from', '');
const exe = from ? '' : flag('--vibe', '') || resolved('vibe');
const scratch = from || flag('--scratch', '') || mkdtempSync(join(tmpdir(), 'vhm-'));
mkdirSync(scratch, { recursive: true });

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = join(ROOT, 'shared', 'vibe-home-manifest.json');

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
];

const globToRegex = (glob) => new RegExp(`^${glob.split('/').map((seg) => (seg === '**' ? '.+' : seg === '*' ? '[^/]+'
  : seg.replaceAll(/[.+?^${}()|[\]\\]/gu, String.raw`\$&`))).join('/')}$`, 'u');
const VARYING_RE = VARYING.map(globToRegex);
const classOf = (path) => (path.startsWith(PINNED_PREFIX) ? 'pinned' : VARYING_RE.some((re) => re.test(path)) ? 'varies' : 'unknown');

const sha256 = (path) => createHash('sha256').update(readFileSync(path)).digest('hex');
const inventory = (home) => files(home).map((path) => ({ path, sha256: sha256(join(home, path)), bytes: readFileSync(join(home, path)).length }));

/** The built environment of D17, for `home` — lib/vibe.mjs's, the product's (VibeLaunch.Built). */
const builtEnv = (home) => builtEnvFor(home, MODEL);

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
writeFileSync(join(scratch, 'steps.json'), JSON.stringify({ exe, version, scratch, steps }, null, 2));
return { version, steps };
}

const { version, steps } = from ? JSON.parse(readFileSync(join(from, 'steps.json'), 'utf8')) : measure();

const afterTurn = steps[1].after;
const unknown = afterTurn.filter((f) => classOf(f.path) === 'unknown');
const pinned = afterTurn.filter((f) => classOf(f.path) === 'pinned');
// Stability: a pinned file must keep its bytes across a later start in the same home, and match in a fresh home that has it.
const drift = [steps[2].after, steps[3].after].flatMap((later) => later.filter((f) => classOf(f.path) === 'pinned')
  .filter((f) => pinned.some((p) => p.path === f.path && p.sha256 !== f.sha256)).map((f) => f.path));
const appeared = steps[2].after.filter((f) => classOf(f.path) === 'pinned' && !pinned.some((p) => p.path === f.path)).map((f) => f.path);

console.log(JSON.stringify({ version, pinned: pinned.length, varies: afterTurn.length - pinned.length - unknown.length,
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
  '`varying` is admitted by name (its bytes are caches, logs and sessions), anything else - a hooks.toml, a config.toml, an',
  'agents/ or tools/ folder, a link - refuses the launch with the path named. A Vibe version with no entry here is RUN',
  "with a warning (the owner, 2026-10-13: warn, not refuse) - its plugin files admitted by name, its home's shape still checked;",
  're-run this script after every Vibe update to measure it. `seen` is informational.',
];
const prior = existsSync(MANIFEST) ? JSON.parse(readFileSync(MANIFEST, 'utf8')) : { versions: [] };
const manifest = {
  why: WHY,
  pinnedPrefix: PINNED_PREFIX,
  varying: VARYING,
  versions: [...(prior.versions ?? []).filter((v) => v.version !== version), entry].sort((a, b) => a.version.localeCompare(b.version, 'en', { numeric: true })),
};
writeFileSync(MANIFEST, `${JSON.stringify(manifest, null, 2)}\n`);
console.error(`wrote ${MANIFEST} for vibe ${version}; scratch: ${scratch}`);
