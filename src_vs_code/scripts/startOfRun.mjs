/**
 * What the extension's test run does about the machine's old `coai-*` directories at its start: it COUNTS them and says so — the ONE entry
 * point `run-tests.mjs` calls (`todo/PLAN_test_leftovers_E5_S2_spec.md`, E5 S2-A).
 *
 * Since E5 S2-A nothing here takes a directory: the age-only `coai-` sweep that stood in `sweepTemp.mjs` is retired, and a count reads NAMES
 * ONLY — no age, no `stat`, no claim. Whether a name is a person's chat, the product's round or a dead run's folder is not a question a name
 * can answer; the one-off clean-up (`scripts/old-coai-leftovers.mjs`) lists them with their ages and claims and takes only what a person picks.
 * This module imports `node:fs` and `node:path` and calls nothing but a read; a test holds it to that (N6).
 */
import fs from 'node:fs';
import path from 'node:path';

/** The shared file, read from the checkout's `shared/` folder: the product's own prefixes, which the count leaves out. */
export function readRule(repoRoot) {
  return JSON.parse(fs.readFileSync(path.join(repoRoot, 'shared', 'temp-sweep.json'), 'utf8'));
}

/** `coai-*` that is not a downloaded server (`coai-0.17.5`) and starts with no product prefix of `neverSwept`. */
function isOldTestName(name, neverSwept) {
  return name.startsWith('coai-') && !/^coai-[0-9]/.test(name) && !neverSwept.some((owned) => name.startsWith(owned));
}

/**
 * What earlier runs and the product left in `dir`, by name alone: `{ leftovers }` — the old `coai-*` directories. (Until E5 S2-B a second number counted the legacy
 * `testsToDelete-coai-run-*` run folders; the owner-aware sweep now LISTS each one by name with its command, and that count is retired.)
 */
export function startOfRun({ dir, rule, io = fs }) {
  const directories = io.readdirSync(dir, { withFileTypes: true }).filter((entry) => entry.isDirectory());

  return { leftovers: directories.filter((entry) => isOldTestName(entry.name, rule.neverSwept)).length };
}

/** The ONE switch of the owner-aware sweep (plan D11), shared with every C# suite; a repository-relative path. */
export const SWITCH_FILE = 'shared/leftovers-sweep.json';

const firstLine = (text) => String(text).split('\n')[0].trimEnd();

/**
 * The sweep's mode from `shared/leftovers-sweep.json`: `'collect'` (the exact, case-sensitive string) or `'list-only'` — for everything else too: another value, a missing
 * key, bad JSON, an unreadable or missing file. The safe side is list-only (a typo switches removal OFF, never on), and every case but the literal `list-only` says why in ONE
 * line through `warn`, prefixed with the runner's name. The C# twin is `StartOfRunSweep.ReadMode`; both read the same file.
 */
export function readSweepSwitch(repoRoot, { io = fs, warn = () => {}, prefix = 'run-tests' } = {}) {
  const because = judge(repoRoot, io);
  if (because === 'collect') {
    return 'collect';
  }
  if (because !== 'list-only') {
    warn(`${prefix}: the start-of-run sweep is list-only because ${SWITCH_FILE} startOfRunSweep is ${because}`);
  }
  return 'list-only';
}

/** `'collect'`, `'list-only'`, or the words that say why neither: the value, `missing`, or `unreadable: <reason>`. */
function judge(repoRoot, io) {
  let document;
  try {
    document = JSON.parse(io.readFileSync(path.join(repoRoot, 'shared', 'leftovers-sweep.json'), 'utf8').replace(/^\uFEFF/, '')); // a leading byte order mark is not part of the JSON: C#'s File.ReadAllText drops it too
  } catch (error) {
    return `unreadable: ${firstLine(error.message)}`;
  }
  const value = document !== null && typeof document === 'object' ? document.startOfRunSweep : undefined;
  if (value === undefined) {
    return 'missing';
  }
  return typeof value === 'string' ? value : JSON.stringify(value);
}
