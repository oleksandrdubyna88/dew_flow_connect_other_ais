#!/usr/bin/env node
/**
 * `node scripts/coldstart-overlay.mjs <repoRoot>` — puts the cold-start measurement onto an OLDER checkout of this repository, so CI can measure a
 * ref that never had it (the v1 baseline, commit c24e5141: the last one before the thin shell). It is run BY HAND on a local branch made from that commit
 * (`research/RESULTS_v2_start.md` § 2): the branch is committed, pushed as `measure/v1-baseline`, and a draft pull request makes CI measure it.
 *
 * <p>The harness is copied over (`run-host.mjs` with its `measure` launch, `coldstart-lib.mjs`, `coldstart-output.mjs`, the scenario `measureScenario.ts`,
 * and the mark recorder `shell/measure.ts` as `src/measure.ts` with its hook file `src/measureMarks.ts`), and `scripts/measure-v1.patch` adds the
 * old extension's marks: the recorder installed, `coai.__measure` registered when COAI_MEASURE=1, `activateStart` and `activateReturn`, the phases of the
 * old `activate()` and of the sidebar's first render (the same names the shell's legacy module records), the sidebar's `ready` and its first data. Nothing
 * else of the old tree changes. The patch is made against c24e5141 and `git apply --check` runs first, so a ref it does not fit fails here, naming the
 * file, instead of measuring a tree without marks.</p>
 *
 * <p>c24e5141 has no cold-start job in its `ci.yml`, so the overlay also writes one workflow of its own, `.github/workflows/cold-start-baseline.yml`
 * (from `scripts/coldstart-baseline.workflow.yml`): the same steps as the `measure` job of the current tree, with `--record-only`. A pull request from the
 * baseline branch then runs it and prints the numbers.</p>
 *
 * <p>The old extension activates itself at start-up (`onStartupFinished`), so it is measured with `--record-only`: no limits, and whether it was
 * already active when the clock started is recorded in the result (see `research/RESULTS_v2_start.md`).</p>
 */
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isMain } from './bundle-lib.mjs';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

/** What is copied, as `[from, to]` paths inside the package (`src_vs_code`). */
export const COPIES = [
  ['scripts/run-host.mjs', 'scripts/run-host.mjs'],
  ['scripts/coldstart-lib.mjs', 'scripts/coldstart-lib.mjs'],
  ['scripts/coldstart-output.mjs', 'scripts/coldstart-output.mjs'],
  ['src/test/host/measureScenario.ts', 'src/test/host/measureScenario.ts'],
  ['shell/measure.ts', 'src/measure.ts'],
  ['src/measureMarks.ts', 'src/measureMarks.ts'],
];

/** The workflow written beside the old tree's own: `[from (inside the package), to (inside the repository root)]`. */
export const WORKFLOW = ['scripts/coldstart-baseline.workflow.yml', '.github/workflows/cold-start-baseline.yml'];

export function overlay(repoRoot, { run = (args, cwd) => execFileSync('git', args, { cwd, stdio: 'inherit' }), log = console.log } = {}) {
  const target = join(resolve(repoRoot), 'src_vs_code');
  if (!existsSync(join(target, 'package.json'))) {
    throw new Error(`${target} is not the extension's package: pass the root of a checkout of this repository`);
  }
  run(['apply', '--check', join(ROOT, 'scripts', 'measure-v1.patch')], resolve(repoRoot));
  run(['apply', join(ROOT, 'scripts', 'measure-v1.patch')], resolve(repoRoot));
  for (const [from, to] of COPIES) {
    mkdirSync(dirname(join(target, to)), { recursive: true });
    copyFileSync(join(ROOT, from), join(target, to));
    log(`coldstart-overlay: ${to}`);
  }
  const [workflowFrom, workflowTo] = WORKFLOW;
  mkdirSync(dirname(join(resolve(repoRoot), workflowTo)), { recursive: true });
  copyFileSync(join(ROOT, workflowFrom), join(resolve(repoRoot), workflowTo));
  log(`coldstart-overlay: ${workflowTo}`);
}

if (isMain(import.meta.url)) {
  const root = process.argv[2];
  if (root === undefined) {
    console.error('usage: node scripts/coldstart-overlay.mjs <repoRoot>');
    process.exit(64);
  }
  overlay(root);
}
