import { rm } from 'node:fs/promises';
import * as vscode from 'vscode';
import { coaiDataDir } from './dataDir';
import { notify } from './notify';
import { RoleDeletions, Tombstone } from './roleDeletion';
import { tombstonesIn } from './roleDeletionStore';
import { rolesFrom, type RoleRow } from './roles';
import { promptFile } from './rolesPrompts';
import { payloadMentionsRole } from './serverSettingsFile';
import { readerFor, reportRefusal, saveSetting } from './sideConfig';

/**
 * The window's one role-deletion coordinator, and the host functions it is built from.
 *
 * <p><b>Why it is not in `rolesPanel.ts`.</b> It was, for one round, and the round was right about
 * it: `extension.ts` reaches for this at activation and again every time the settings mirror
 * settles, and both of those are background work. Making them import the webview panel module
 * inverts the layering and pins the panel — with its page, its styles and its whole render path —
 * into the activation path of a window nobody has opened the Roles tab in. (antigravity, the code
 * round.)</p>
 *
 * <p>So the coordinator lives here, built from `sideConfig` and the prompts directory directly, and
 * both the panel and the host consume it from the same place.</p>
 */

/** The setting that holds the rows. */
const ROLES_KEY = 'roles';

/**
 * Every setting keyed by a role id, and there are four of them.
 *
 * <p>Deleting a role left all four behind, so the next role that took the freed id opened with a
 * stranger's round budget, threshold and enabled flag. They are part of the payload the mirror
 * writes, which is why they are pruned WITH the row rather than after the mirror carries it —
 * pruned afterwards, the server holds orphaned keys until some unrelated setting changes, possibly
 * for ever. (antigravity, the plan round, blocking.)</p>
 */
const KEYED_BY_ROLE = ['rounds', 'thresholds', 'roleEnabled', 'promptsPerRound'] as const;

let deletions: RoleDeletions | undefined;

/** Who wants to know that what the Roles page would draw has changed. Set when the page opens. */
let watcher: ((inMs: number) => void) | undefined;

/** The Roles page, while it is open. */
export function whenDeletionsChange(redraw: (inMs: number) => void): { dispose: () => void } {
  watcher = redraw;

  return {
    dispose: () => {
      watcher = undefined;
    },
  };
}

/**
 * The coordinator of this window, built once.
 *
 * <p>Once, and not per call: the page and the host that drives it must be looking at the same
 * store, the same clock and the same reporter, or the page shows the tombstones of a store nothing
 * writes.</p>
 */
export function roleDeletions(context: vscode.ExtensionContext): RoleDeletions {
  deletions ??= new RoleDeletions({
    store: tombstonesIn(() => coaiDataDir()),
    prune: async (roleId) => await pruneRole(context, roleId),
    mentions: payloadMentionsRole,
    forget,
    now: () => new Date(),
    say: sayNotDeleted,
    changed: (inMs) => watcher?.(inMs),
  });

  return deletions;
}

/** Deactivation, and the tests that must not inherit a previous one's store. */
export function forgetTheDeletions(): void {
  deletions = undefined;
}

/**
 * Step 2: the row and the four records, in one act. Idempotent, so the sweep may redo it.
 *
 * <p><b>A refused write is REPORTED, and then this stops.</b> `saveSetting` no longer swallows a
 * refusal — every caller picks one of its two reporting shapes — and the shape here is the panel's:
 * say it and return, because everything after this point is about a row that did not move. The
 * deletion does not fail; it simply does not finish, its prompts stay on disk, and the next
 * settling records the reason on the tombstone.</p>
 */
async function pruneRole(context: vscode.ExtensionContext, roleId: string): Promise<void> {
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  const read = readerFor(context, config());
  const current: readonly RoleRow[] = rolesFrom(read(ROLES_KEY));
  const rest = (held: Record<string, unknown>): Record<string, unknown> => {
    const { [roleId]: dropped, ...without } = held;

    void dropped;

    return without;
  };

  try {
    if (current.some((row) => row.id === roleId)) {
      await saveSetting(context, config(), ROLES_KEY, current.filter((row) => row.id !== roleId));
    }
    for (const key of KEYED_BY_ROLE) {
      const held = read(key);
      if (typeof held === 'object' && held !== null && roleId in (held as Record<string, unknown>)) {
        await saveSetting(context, config(), key, rest(held as Record<string, unknown>));
      }
    }
  } catch (error: unknown) {
    reportRefusal(context, ROLES_KEY, error, {
      recognised: `“${roleId}” could not be removed from your settings, so the server still has it.`,
    });
  }
}

/** Step 4: the prompt override files, and only once the mirror has carried the row. */
async function forget(promptIds: readonly string[]): Promise<void> {
  for (const promptId of promptIds) {
    const file = promptFile(coaiDataDir(), promptId);
    if (file !== undefined) {
      await rm(file, { force: true }).catch((error: unknown) => {
        console.error('[coai] a deleted role\'s saved prompt could not be removed', error);
      });
    }
  }
}

/** Said through the funnel: the ledger keeps it whether or not anybody sees the toast. */
function sayNotDeleted(tombstone: Tombstone, reason: string): void {
  void notify({
    as: 'warning',
    class: 'stand-down',
    source: 'rolesPage',
    code: 'role-not-deleted-yet',
    subject: tombstone.roleId,
    title: `“${tombstone.name}” was removed here, and the server has not been told yet.`,
    detail: reason,
    cure: 'Its prompts are kept until the server has it. The Roles tab can finish the deletion anyway.',
  });
}
