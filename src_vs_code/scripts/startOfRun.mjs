/**
 * What the extension's test run does about the machine's old `coai-*` directories at its start — the ONE entry point `run-tests.mjs` calls
 * (`todo/PLAN_test_leftovers_E5_S2_spec.md`, E5 S2-A).
 *
 * A seam, extracted first so the tests of the retirement call the same entry before and after it: the age-only sweep of `sweepTemp.mjs`
 * is still behind it in this commit, behaviour unchanged.
 */
import fs from 'node:fs';
import { sweep } from './sweepTemp.mjs';

/** Sweeps `dir` at `now` as `sweepTemp.mjs` always did: `{ removed, failed }`. */
export function startOfRun({ dir, rule, io = fs, now = Date.now() }) {
  return sweep(dir, now, rule, io);
}
