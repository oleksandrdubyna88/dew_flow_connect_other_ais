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

/** The folder a run makes and ends itself — a legacy name of the rule (its second segment is no owner), left behind by a killed run. */
const LEGACY_RUN_FOLDER = /^testsToDelete-coai-run-[0-9a-f]{6}$/;

/** `coai-*` that is not a downloaded server (`coai-0.17.5`) and starts with no product prefix of `neverSwept`. */
function isOldTestName(name, neverSwept) {
  return name.startsWith('coai-') && !/^coai-[0-9]/.test(name) && !neverSwept.some((owned) => name.startsWith(owned));
}

/**
 * What earlier runs and the product left in `dir`, by name alone: `{ leftovers, legacyRuns }` — the old `coai-*` directories, and the legacy
 * `testsToDelete-coai-run-<6 hex>` run folders (some of them perhaps another run's, still going; a name cannot say). `ownFolder`, when the caller has one
 * already, is left out (the C# count does the same with `RunTemp.Folder`).
 */
export function startOfRun({ dir, rule, io = fs, ownFolder }) {
  const directories = io.readdirSync(dir, { withFileTypes: true }).filter((entry) => entry.isDirectory() && path.join(dir, entry.name) !== ownFolder);

  return {
    leftovers: directories.filter((entry) => isOldTestName(entry.name, rule.neverSwept)).length,
    legacyRuns: directories.filter((entry) => LEGACY_RUN_FOLDER.test(entry.name)).length,
  };
}
