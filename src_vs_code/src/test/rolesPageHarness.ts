import { rolesHtml, type RolesPageState } from '../rolesPage';
import { type PageClock } from './panelPageHarness';
import { type Node, type Page, runPageHtml } from './pageScriptHarness';

/**
 * The Review roles tab's own script, RUN — the one thing the roles page's tests need that no other page does.
 *
 * <p>This file used to hold the whole DOM shim. The shim is `pageScriptHarness.ts` now, because the new Settings page's
 * tests run on it too and epic 5 deletes the roles page this file is named for (todo/PLAN_one_model_catalog.md, E5
 * prerequisite (b)); when the page goes, this file goes with it and the shim stays.</p>
 */

/**
 * Run the roles page's script and collect everything it posts.
 *
 * <p>`inDocument` are the nodes `document.querySelectorAll` will answer with, keyed by the selector
 * the page asks for. A test that presses a tab needs the page to FIND the other tabs and the
 * sections, and a shim that answers every selector with nothing would let a broken switch look
 * exactly like a working one.</p>
 */
export function runRolesPage(
  state: RolesPageState,
  inDocument: Readonly<Record<string, readonly Node[]>> = {},
  clock?: PageClock,
): Page {
  return runPageHtml(rolesHtml(state, 'test-nonce'), inDocument, clock);
}
