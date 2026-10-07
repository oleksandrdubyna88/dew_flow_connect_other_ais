import { readFile, rm } from 'node:fs/promises';
import * as vscode from 'vscode';

import { asText } from './asText';
import { writeFileAtomically } from './atomicFile';
import { coaiDataDir } from './dataDir';
import { notify, notifyAndAsk } from './notify';
import { composed, isBuiltIn, promptIdsInUse, rolesFrom, type RoleRow } from './roles';
import { promptBelongsTo, rowsAfter } from './rolesEdit';
import { rolesFieldOf, type RolesCommand } from './rolesPage';
import { promptFile } from './rolesPrompts';
import { settledWrites } from './settledWrites';
import { readerFor, reportRefusal, saveSetting } from './sideConfig';
import { roleDeletions } from './roleDeletionsHost';
import { isTextControl } from './textControls';
import { applyTextControl } from './textControlsHost';
import type { RolesEmbedState } from './rolesEmbed';

/**
 * The editing core of the review roles — what both pages that edit them call (todo/PLAN_one_model_catalog.md E4.3): the
 * Review roles tab (`rolesPanel.ts`) and, on the new Settings page, Reviews › Roles & prompts. Moved here from
 * `rolesPanel.ts`, never copied: two copies of "which layer is written, in what order, with which refusals" is the
 * defect the reuse rule exists for.
 *
 * <p>Everything DECIDED is in `rolesEdit.ts`, `rolesPage.ts` or `roles.ts`. What is here is what only a host can do:
 * read a setting, write one, write a file — and the two things a host must never get wrong, WHICH settings layer it
 * writes to and what order concurrent writes land in. Whoever draws the roles listens with {@link onRolesRedraw}; a
 * change that alters the shape of the roles redraws every page that shows them.</p>
 */

const SECTION = 'coai';
const KEY = 'roles';

let context: vscode.ExtensionContext | undefined;

/** The window this extension runs in — the only thing that can say WHICH SIDE it is. Set at activation. */
export function bindRoles(extension: vscode.ExtensionContext): void {
  context = extension;
}

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

/**
 * The extension context. Loud rather than absent: reaching this with none is a wiring mistake, and the alternative —
 * quietly reading the shared settings instead — is the exact defect this whole path was changed to remove.
 */
export function rolesSide(): vscode.ExtensionContext {
  if (context === undefined) {
    throw new Error('the review roles were asked for before the extension bound them');
  }

  return context;
}

/**
 * The rows as THIS SIDE has them — through `readerFor`, because `roles` is in `OVERLAID_SETTINGS`: a bare read answers
 * from the shared `settings.json` while the gate answers from this side's overlay.
 */
export function roleRows(): readonly RoleRow[] {
  return rolesFrom(readerFor(rolesSide(), config())(KEY));
}

async function write(next: readonly RoleRow[]): Promise<void> {
  await saveSetting(rolesSide(), config(), KEY, next);
}

/** The body of every prompt that has one, by id — absent means "what this product ships". */
export async function roleTexts(): Promise<Record<string, string>> {
  const ids = [...promptIdsInUse(roleRows())];
  const found: Record<string, string> = {};
  await Promise.all(ids.map(async (id) => {
    const file = promptFile(coaiDataDir(), id);
    if (file !== undefined) {
      const body = await bodyOf(file);
      if (body !== undefined) {
        found[id] = body;
      }
    }
  }));

  return found;
}

/**
 * One prompt's stored text, or nothing when there is no file.
 *
 * <p><b>Absent and unreadable are different, and only one of them is normal.</b> A prompt nobody has rewritten simply
 * has no file. A file that EXISTS and cannot be read is a person's own writing that a page is about to draw as an empty
 * box, over which the next keystroke would write — so it is reported rather than swallowed.</p>
 */
async function bodyOf(file: string): Promise<string | undefined> {
  try {
    return await readFile(file, 'utf8');
  } catch (error: unknown) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') {
      reportRolesFailure(`ConnectOtherAIs could not read a saved prompt (${file}). It is shown empty — do not save over it.`, error);
    }

    return undefined;
  }
}

/** Who redraws when the roles change shape — every page that draws them. */
const redraws = new Set<() => Promise<void>>();

/** A page that draws the roles, redrawn after every change that alters their shape. */
export function onRolesRedraw(redraw: () => Promise<void>): vscode.Disposable {
  redraws.add(redraw);

  return new vscode.Disposable(() => { redraws.delete(redraw); });
}

async function redrawAll(): Promise<void> {
  await Promise.all([...redraws].map((redraw) => redraw()));
}

/**
 * Commands are applied ONE AT A TIME, and a typed field waits to settle before it is stored — `settledWrites.ts`'s two
 * rules. ONE queue for both pages, so an edit on one cannot overtake an edit on the other.
 */
const writes = settledWrites<RolesCommand>({
  apply,
  render: redrawAll,
  // Through `reportRefusal`, so the one refusal that HAS a cure — a window that has not caught up with an update cannot
  // store a key it never registered — offers the reload instead of this module's sentence.
  report: (error) => { reportRefusal(rolesSide(), KEY, error, { ordinary: 'ConnectOtherAIs could not save that change to your roles.' }); },
  fieldOf: rolesFieldOf,
});

/** One edit, from either page, in the one queue. */
export const queueRoleEdit = (command: RolesCommand): Promise<void> => writes.queue(command);

/** Whatever is still settling — written before a page that typed it goes away. */
export const flushRoleEdits = (): Promise<void> => writes.flush();

/**
 * One command, applied. Answers whether the pages must be redrawn.
 *
 * <p>A text edit does NOT redraw: the person is typing in the box, and replacing the document under them would move the
 * caret to the end of it on every keystroke. Everything that changes the SHAPE of the roles — a role added or removed,
 * a switch, a stage — does. Which tab is open is the old page's own (`rolesPanel.ts`), and changes nothing here.</p>
 */
async function apply(command: RolesCommand): Promise<boolean> {
  if (command.kind === 'tab' || command.kind === 'ignore') {
    return false;
  }
  if (isTextControl(command)) {
    await applyTextControl(command);

    return false;
  }

  return await applied(command);
}

/** A command that acts on the roles themselves. */
type RoleAct = Exclude<RolesCommand, { kind: 'tab' | 'ignore' | 'zoom' | 'tone' }>;

/** What each command that is not a plain row edit does — a table, so each is one small rule. Answers: redraw? */
const ACTS: { readonly [K in RoleAct['kind']]?: (command: Extract<RoleAct, { kind: K }>) => Promise<boolean> } = {
  editPrompt: async (command) => {
    if (command.field !== 'text') {
      return store(command);
    }
    await writeText(command.id, command.promptId, command.value);

    return false;
  },
  // Restoring a shipped prompt is DELETING the override: the default is embedded in the server's binary. It repaints,
  // because the box has to empty and the button has to go grey.
  restorePrompt: async (command) => {
    await writeText(command.id, command.promptId, '');

    return true;
  },
  remove: (command) => removeRole(command.id),
  // The cure for a stand-down: a newer build owns the settings file, and reloading is how this window becomes it.
  reloadWindow: () => {
    void vscode.commands.executeCommand('workbench.action.reloadWindow');

    return Promise.resolve(false);
  },
  finishDeletion: async (command) => {
    await roleDeletions(rolesSide()).finishAnyway(command.id);

    return true;
  },
};

/** The commands that act on the roles themselves: their own rule, or a plain row edit. */
async function applied(command: RoleAct): Promise<boolean> {
  const act = ACTS[command.kind] as ((one: RoleAct) => Promise<boolean>) | undefined;

  return act === undefined ? store(command) : act(command);
}

/** A row edit the rules refuse, said once in one place. */
function sayRefused(why: string): void {
  void notify({
    as: 'information',
    class: 'refusal',
    source: 'rolesPage',
    code: 'role-edit-refused',
    title: why,
  });
}

/** The prompt bodies a command needs: all of them for a switch ON, none for anything else. */
function textsFor(command: RolesCommand): Promise<Record<string, string>> {
  return activating(command) ? roleTexts() : Promise.resolve({});
}

/** Whether this command switches a role ON — the one that has to know whether it can be asked. */
function activating(command: RolesCommand): boolean {
  return command.kind === 'edit' && command.field === 'active' && command.value === true;
}

/** Everything that changes a ROW rather than a file. */
async function store(command: RolesCommand): Promise<boolean> {
  // Only `add` pays for the reservations: an id whose deletion has not finished is not free. Through the coordinator,
  // which owns the store. And only a switch ON reads the prompt bodies: a role is not switched on without a question.
  const taken = command.kind === 'add' ? await roleDeletions(rolesSide()).reserved() : new Set<string>();
  const outcome = rowsAfter(roleRows(), command, taken, await textsFor(command));
  if (outcome.kind === 'unchanged') {
    return false;
  }
  if (outcome.kind === 'refused') {
    sayRefused(outcome.why);

    // A refusal DOES redraw: the control has just moved to a state that was not saved, and putting it back is what
    // makes the message about it true.
    return true;
  }

  await write(outcome.rows);
  await forget(outcome.forget);

  // A TYPED field never redraws: storing a name replaced the document under the person and put the caret at the end.
  return rolesFieldOf(command) === undefined;
}

/**
 * Removing a role: asked first, then the row and its prompt bodies together. Asked because it is the one irreversible
 * thing here; the files go WITH it, or the next role named the same opens with text the person believed deleted.
 */
async function removeRole(id: string): Promise<boolean> {
  if (isBuiltIn(id)) {
    void notify({
      as: 'information',
      class: 'refusal',
      source: 'rolesPage',
      code: 'shipped-role-not-removable',
      subject: id,
      title: 'That is a role this product ships — it can be switched off, but not removed.',
    });

    return true;
  }

  const name = roleName(id);

  return (await removalConfirmed(id, name)) ? await removeConfirmed(id, name) : false;
}

/** What a role is called, for a sentence — its id when it has no name. */
function roleName(id: string): string {
  return roleRows().find((r) => r.id === id)?.name ?? id;
}

/** A question, so both the asking and the answer are written down: what a person DECLINED to delete is as much a fact. */
async function removalConfirmed(id: string, name: string): Promise<boolean> {
  const answer = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'rolesPage',
    code: 'remove-role',
    subject: id,
    modal: true,
    title: `Remove the role “${name}”?`,
    detail: 'Its prompts and everything you wrote in them are deleted. Rounds already recorded keep their findings.',
    action: 'Remove',
  });

  return answer === 'Remove';
}

/**
 * The removal itself — not `store`: the text waits on the far side of the mirror having carried the row, and `begin`
 * writes the tombstone first, so a host that dies in the middle leaves evidence rather than an orphaned prompt and a
 * freed id.
 */
async function removeConfirmed(id: string, name: string): Promise<boolean> {
  const outcome = rowsAfter(roleRows(), { kind: 'remove', id });
  if (outcome.kind === 'refused') {
    sayRefused(outcome.why);

    return true;
  }
  if (outcome.kind === 'unchanged') {
    return false;
  }
  await roleDeletions(rolesSide()).begin({ id, name, promptIds: outcome.forget });

  return true;
}

/** The override files of prompts no row points at any more. */
async function forget(promptIds: readonly string[]): Promise<void> {
  for (const promptId of promptIds) {
    const file = promptFile(coaiDataDir(), promptId);
    if (file !== undefined) {
      await rm(file, { force: true }).catch((error: unknown) => {
        reportRolesFailure('ConnectOtherAIs removed the role but could not delete one of its saved prompts.', error);
      });
    }
  }
}

/**
 * A prompt's text, written where the server reads it — or the file removed, which is how a shipped prompt goes back to
 * the text this product ships. Written beside the destination and RENAMED over it (`writeFileAtomically`): `writeFile`
 * truncates first, and a crash between the truncation and the last byte would leave half a question.
 */
async function writeText(roleId: string, promptId: string, text: string): Promise<void> {
  const file = promptFileOf(roleId, promptId);
  if (file.length === 0) {
    reportRolesFailure(`ConnectOtherAIs ignored a prompt it does not recognise (${promptId}).`, new Error('unknown prompt id'));

    return;
  }
  await (text.trim().length === 0 ? rm(file, { force: true }) : writeFileAtomically(file, text)).catch((error: unknown) => {
    reportRolesFailure('ConnectOtherAIs could not save that prompt. Your text is still on the page — copy it somewhere before closing the tab.', error);
  });
}

/**
 * The file a prompt's text goes to, or '' when it may not be written. The id reaches a PATH, so it is checked twice:
 * `promptFile` refuses anything that is not a slug, and this refuses a slug that belongs to no prompt of this role — so a
 * webview cannot write over an unrelated role's override.
 */
function promptFileOf(roleId: string, promptId: string): string {
  const file = promptFile(coaiDataDir(), promptId) ?? '';

  return file.length > 0 && promptBelongsTo(composed(roleRows()), roleId, promptId) ? file : '';
}

/**
 * What a person is told, and what is kept for whoever has to diagnose it: a sentence to the window, the exception to the
 * log. One code for the roles' failures, with the SENTENCE as the subject, so five different failures are five counters.
 */
export function reportRolesFailure(message: string, error: unknown): void {
  console.error('[coai] roles page:', message, error);
  void notify({
    as: 'error',
    class: 'failure',
    source: 'rolesPage',
    code: 'roles-page-failure',
    subject: message,
    title: message,
    detail: asText(error),
  });
}

/**
 * The roles as the new Settings page's Roles & prompts draws them (E4.3): the rows, the prompt bodies and the deletions
 * that cannot clear — the same reads the Review roles tab is drawn from.
 */
export async function rolesEmbedState(serverVersion: string): Promise<RolesEmbedState> {
  return {
    rows: roleRows(),
    texts: await roleTexts(),
    serverVersion,
    perSide: config().get('perSideSettings') === true,
    stranded: await roleDeletions(rolesSide()).stranded(),
  };
}
