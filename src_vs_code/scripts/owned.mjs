/**
 * The ONE module of this repository that holds the leftovers token for Node (E5 S2-B, plan D3): the mounted `owned.mjs` bound to `coai`, and the mounted
 * reference sweep bound to the same token. Every other script imports from HERE; nothing else writes the token. S4 extends this module with the owned run folder.
 *
 * A missing mount throws at LOAD (the static import below): a caller that wants a controlled exit imports this module inside its own `try`
 * (`run-tests.mjs` `loadOwned`, `scripts/run-lib-tests.mjs`) and says `git submodule update --init .agents/conventions`.
 *
 * This module imports `node:` modules and repository files only: the `windows` CI job runs the sweep's test file without `npm ci` (L5 pins that).
 */
import * as path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ownedFor } from '../../.agents/conventions/tools/lib/owned.mjs';

/** Rule §1: this repository's token — written ONCE in Node, here. */
export const REPO_TOKEN = 'coai';

/** The `repository` column of the `coai` row of the rule's `## Tokens` table: what the guard's identity check reads. */
export const REPO_ROW = 'the multi-model review extension and server';

/** The mounted helper bound to this repository's token. */
export const owned = ownedFor({ token: REPO_TOKEN, row: REPO_ROW });
export { REPO_TOKEN as token };

const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));
const SWEEP_MODULE = path.join(REPO_ROOT, '.agents', 'conventions', 'tools', 'lib', 'leftovers-sweep.mjs');

/**
 * The mounted reference's `sweepLeftovers` with THIS repository's token bound AFTER the caller's options (F23), so nothing a caller passes can override it.
 * Loaded by file URL in its own step: a load failure is the caller's ONE line and no sweep, never a failed run. `importer` is the seam of the test that
 * hands in a recording sweep.
 */
export async function loadStartOfRunSweep({ importer = (href) => import(href) } = {}) {
  const { sweepLeftovers } = await importer(pathToFileURL(SWEEP_MODULE).href);
  if (typeof sweepLeftovers !== 'function') {
    throw new Error(`${SWEEP_MODULE} exports no sweepLeftovers`);
  }
  return (options = {}) => sweepLeftovers({ ...options, token: REPO_TOKEN });
}
