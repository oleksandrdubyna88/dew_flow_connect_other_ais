import { randomBytes } from 'node:crypto';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import * as vscode from 'vscode';

import { asText } from './asText';
import { coaiDataDir } from './dataDir';
import { notify, notifyAndAsk } from './notify';
import { composed, isBuiltIn, promptIdsInUse, rolesFrom, type RoleRow } from './roles';
import { promptBelongsTo, rowsAfter } from './rolesEdit';
import { DEFAULT_ROLE_TAB, nextTab, roleEdit, rolesHtml, type RolesCommand } from './rolesPage';
import { promptFile, promptsDir } from './rolesPrompts';
import { settledWrites } from './settledWrites';
import { serverOnThisSide } from './installer';
import { readerFor, reportRefusal, saveSetting } from './sideConfig';
import { RoleDeletions, Tombstone, reserved } from './roleDeletion';
import { tombstonesIn } from './roleDeletionStore';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';

/**
 * The tab that edits review roles — a thin host over pure modules, the arrangement this extension
 * has three times already.
 *
 * <p>Everything DECIDED here is decided in `rolesEdit.ts`, `rolesPage.ts` or `roles.ts`, all of
 * which a test can reach without a `vscode`. What is left is what only a host can do: read a
 * setting, write one, write a file, repaint — and the two things a host must never get wrong,
 * WHICH settings layer it writes to and what order concurrent writes land in.</p>
 */

const SECTION = 'coai';
const KEY = 'roles';

let panel: vscode.WebviewPanel | undefined;

/**
 * Which of the three sections is open.
 *
 * <p>Held HERE rather than on the page, because a redraw replaces `panel.webview.html` wholesale —
 * a new document, with no memory of anything the last one knew. Every shape-changing action
 * redraws: add a prompt, add a role, a switch, a stage, a remove, a restore. A page-local tab would
 * mean pressing "Add a prompt" on an architecture role and being thrown back to the plan tab, with
 * the prompt just added on a tab you can no longer see.</p>
 *
 * <p>A module variable, not a stored setting: it outlives the panel, so closing and reopening the
 * page in the same window keeps it, and there is nothing on disk to validate or migrate. Which tab
 * somebody was last looking at is not worth a key in their settings.</p>
 */
let tab: string = DEFAULT_ROLE_TAB;
let context: vscode.ExtensionContext | undefined;

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

/**
 * The extension context, which is the only thing that can say WHICH SIDE this window is.
 *
 * <p>Loud rather than absent: every read and write below happens while the panel is open, and the
 * panel can only be opened by {@link openRoles}, which takes the context. Reaching this with none is
 * a wiring mistake, and the alternative — quietly reading the shared settings instead — is the exact
 * defect this whole path was changed to remove.</p>
 */
function side(): vscode.ExtensionContext {
  if (context === undefined) {
    throw new Error('the roles page was asked for settings before it was opened');
  }

  return context;
}

/**
 * The rows as THIS SIDE has them.
 *
 * <p>Through `readerFor`, not `config().get`, because `roles` is in `OVERLAID_SETTINGS`: with the
 * per-side switch on, a bare read answers from the shared `settings.json` while the gate answers
 * from this side's overlay, and the page would edit one set of roles while the round ran another.
 * The doc on `sideConfigReader` already said it about the read side — <i>"a read that goes around
 * it is a setting that silently stays shared"</i> — and this was the fourth read that went around
 * it.</p>
 */
function rows(): readonly RoleRow[] {
  return rolesFrom(readerFor(side(), config())(KEY));
}

async function write(next: readonly RoleRow[]): Promise<void> {
  await saveSetting(side(), config(), KEY, next);
}

/**
 * Every setting keyed by a role id, and there are four of them.
 *
 * <p>Deleting a role left all four behind, so the next role that took the freed id opened with a
 * stranger’s round budget, threshold and enabled flag. They are part of the payload the mirror
 * writes, which is why they are pruned WITH the row rather than after the mirror carries it —
 * pruned afterwards, the server holds orphaned keys until some unrelated setting changes, possibly
 * for ever. (antigravity, the plan round, blocking.)</p>
 */
const KEYED_BY_ROLE = ['rounds', 'thresholds', 'roleEnabled', 'promptsPerRound'] as const;

/** Step 2: the row and the four records, in one act. Idempotent, so the sweep may redo it. */
async function pruneRole(roleId: string): Promise<void> {
  const current = rows();
  if (current.some((row) => row.id === roleId)) {
    await write(current.filter((row) => row.id !== roleId));
  }
  const read = readerFor(side(), config());
  for (const key of KEYED_BY_ROLE) {
    const held = read(key);
    if (typeof held === 'object' && held !== null && roleId in (held as Record<string, unknown>)) {
      const { [roleId]: dropped, ...rest } = held as Record<string, unknown>;

      void dropped;
      await saveSetting(side(), config(), key, rest);
    }
  }
}

/**
 * Half of the condition a deletion finishes on: is the role absent from the configuration NOW?
 *
 * <p>The other half is the mirror having carried a write, and it takes both — `pruneRole` is
 * several `config.update` calls and each one fires the configuration listener, so a sync that landed
 * between the first and the last carried an incomplete removal.</p>
 */
function goneFromSettings(roleId: string): boolean {
  if (rows().some((row) => row.id === roleId)) {
    return false;
  }
  const read = readerFor(side(), config());

  return !KEYED_BY_ROLE.some((key) => {
    const held = read(key);

    return typeof held === 'object' && held !== null && roleId in (held as Record<string, unknown>);
  });
}

/** The deletions of this window, built once the page has its context. */
let deletions: RoleDeletions | undefined;

export function roleDeletions(): RoleDeletions {
  deletions ??= new RoleDeletions({
    store: tombstonesIn(() => coaiDataDir()),
    prune: pruneRole,
    gone: goneFromSettings,
    forget,
    now: () => new Date(),
    say: sayNotDeleted,
  });

  return deletions;
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

/** The body of every prompt that has one, by id — absent means "what this product ships". */
async function texts(): Promise<Record<string, string>> {
  const ids = [...promptIdsInUse(rows())];
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
 * <p><b>Absent and unreadable are different, and only one of them is normal.</b> A prompt nobody has
 * rewritten simply has no file, and the server answers from the text embedded in its binary — that
 * is the ordinary case and it says nothing. A file that EXISTS and cannot be read is a person's own
 * writing that this page is about to draw as an empty box, over which the next keystroke would write
 * — so it is reported rather than swallowed.</p>
 */
async function bodyOf(file: string): Promise<string | undefined> {
  try {
    return await readFile(file, 'utf8');
  } catch (error: unknown) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') {
      report(`ConnectOtherAIs could not read a saved prompt (${file}). It is shown empty — do not save over it.`, error);
    }

    return undefined;
  }
}

export function openRoles(extension: vscode.ExtensionContext): void {
  context = extension;
  if (panel !== undefined) {
    panel.reveal();

    return;
  }

  panel = vscode.window.createWebviewPanel(
    'coaiRoles',
    'Review roles',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const scale = pushUiScaleTo(panel.webview);
  panel.webview.onDidReceiveMessage((message: unknown) => { queue(roleEdit(message)); });
  panel.onDidDispose(() => {
    scale.dispose();
    panel = undefined;
    // Whatever is still settling belongs to a person who typed it. Losing it because they closed the
    // tab would be the one data loss this page can cause.
    void flush();
  });
  void render().catch((error: unknown) => report('ConnectOtherAIs could not draw the roles page.', error));
  // Not awaited, and deliberately not fatal: the page is useful without knowing the server, and it
  // says as much until this answers.
  void askTheServer().catch((error: unknown) => {
    console.error('[coai] roles page: the installed server could not be identified', error);
  });
}

/** The installed server, so the page can warn about one too old to read any of this. */
let serverVersion = '';

/**
 * What this page knows about the installed server, and how it comes to know it.
 *
 * <p><b>It asks, rather than waiting to be told.</b> The version used to arrive only through
 * {@link rolesKnowTheServer}, called by the sidebar while it repainted — so a window whose sidebar
 * was collapsed, or focused on Explorer, never resolved that view, never called this, and left the
 * page permanently unable to say whether the roles on it would run. `coai.editRoles` is on the
 * command palette; it does not need the sidebar to have been looked at. (gemini, the second code
 * round.)</p>
 *
 * <p>Once per opening, not once per repaint: the full status runs the binary to ask its version, and
 * a process per keystroke-settled redraw is the cost that argument is usually about.</p>
 */
async function askTheServer(): Promise<void> {
  const context = side();
  // The published version is what an UPDATE offer is measured against, and this page offers none —
  // it only needs to know what is installed. Empty skips the network read the panel does.
  const status = await serverOnThisSide(context.globalStorageUri, context.globalState, '');
  rolesKnowTheServer(status.kind === 'absent' ? '' : status.version);
}

/**
 * The sidebar telling this page what IT found — a second, cheaper source of the same answer.
 *
 * <p>Kept beside {@link askTheServer} rather than replacing it: the sidebar has the status in hand
 * every time it repaints, so a server installed while this page is open reaches it without another
 * process. It REPAINTS when the answer changes; it used to only assign, which is how the version
 * could arrive into a variable nothing read again.</p>
 */
export function rolesKnowTheServer(version: string): void {
  if (version === serverVersion) {
    return;
  }

  serverVersion = version;
  void render().catch((error: unknown) => report('ConnectOtherAIs could not draw the roles page.', error));
}

async function render(): Promise<void> {
  if (panel === undefined) {
    return;
  }

  const html = rolesHtml(
    {
      rows: rows(),
      texts: await texts(),
      serverVersion,
      perSide: config().get('perSideSettings') === true,
      tab,
      uiScale: currentUiScale(),
      stranded: await roleDeletions().stranded(),
    },
    nonce(),
  );
  // Re-checked: `await texts()` is a suspension point, and the panel can be disposed across it.
  if (panel !== undefined) {
    panel.webview.html = html;
  }
}

/**
 * A nonce for the page's one script.
 *
 * <p>From `crypto`, not `Math.random`: this is the value the Content-Security-Policy trusts, and a
 * predictable one is a policy that trusts whatever guessed it. Hex, so the guarantee that it cannot
 * carry a quote into the policy string is a property of this function rather than of a coincidence
 * about base-36.</p>
 */
function nonce(): string {
  return randomBytes(16).toString('hex');
}

/**
 * Commands are applied ONE AT A TIME, and a typed field waits to settle before it is stored.
 *
 * <p>Both rules, and the reasons for them, are `settledWrites.ts` — they were written here and
 * moved out when the phrases tab needed exactly them. What stays here is the only part that is
 * this page's own: WHICH commands are typing.</p>
 */
const writes = settledWrites<RolesCommand>({
  apply,
  render,
  // Through `reportRefusal`, so the one refusal that HAS a cure — a window that has not caught up
  // with an update cannot store a key it never registered — offers the reload instead of this page's
  // sentence, which would leave somebody with nothing to do about it. Every other failure keeps the
  // sentence: see the argument on `report` below about where an errno belongs.
  report: (error) => { reportRefusal(side(), KEY, error, { ordinary: 'ConnectOtherAIs could not save that change to your roles.' }); },
  fieldOf,
});

const queue = (command: RolesCommand): void => { writes.queue(command); };
const flush = (): Promise<void> => writes.flush();

/** The key a typed field settles under, or nothing for a command that is not typing. */
function fieldOf(command: RolesCommand): string | undefined {
  if (command.kind === 'edit' && typeof command.value === 'string') {
    return `${command.id}/${command.field}`;
  }
  if (command.kind === 'editPrompt') {
    return `${command.id}/${command.promptId}/${command.field}`;
  }

  return undefined;
}

/**
 * One command, applied. Answers whether the page must be redrawn.
 *
 * <p>A text edit does NOT redraw: the person is typing in the box, and replacing the document under
 * them would move the caret to the end of it on every keystroke. Everything that changes the SHAPE
 * of the page — a role added or removed, a switch, a stage — does.</p>
 */
async function apply(command: RolesCommand): Promise<boolean> {
  // The whole rule is `nextTab`, a pure function next to the page that draws it, so that pressing a
  // tab and then adding a role is something a test can execute rather than read.
  tab = nextTab(tab, command);
  if (command.kind === 'tab') {
    // No redraw: the page switched on the press, without waiting for this. Replacing the document
    // now would be a second, slower switch that also moved the caret out of whatever was being
    // typed in the tab left behind.
    return false;
  }
  if (command.kind === 'ignore') {
    return false;
  }
  if (command.kind === 'zoom') {
    await applyZoomDelta(command.delta);

    return false;
  }
  if (command.kind === 'editPrompt' && command.field === 'text') {
    await writeText(command.id, command.promptId, command.value);

    return false;
  }
  // Restoring a shipped prompt is DELETING the override, which is what restore has meant to
  // `RolePrompts` since it was written: the default is embedded in the server's binary, so there is
  // nothing to copy back. It repaints, because the box has to empty and the button has to go grey.
  if (command.kind === 'restorePrompt') {
    await writeText(command.id, command.promptId, '');

    return true;
  }
  if (command.kind === 'remove') {
    return await removeRole(command.id);
  }
  if (command.kind === 'reloadWindow') {
    // The cure for a stand-down, offered beside the deletion it is stuck behind: a newer build owns
    // the settings file, and reloading is how this window becomes that build.
    void vscode.commands.executeCommand('workbench.action.reloadWindow');

    return false;
  }
  if (command.kind === 'finishDeletion') {
    await roleDeletions().finishAnyway(command.id);

    return true;
  }

  return await store(command);
}

/**
 * A row edit the rules refuse, said once in one place.
 *
 * <p>It was written twice for about ten minutes — once in `store` and once in `removeRole` when the
 * deletion stopped going through `store` — and the census caught it before anything else did: two
 * places speaking where one condition exists is two places to change when the wording does.</p>
 */
function sayRefused(why: string): void {
  void notify({
    as: 'information',
    class: 'refusal',
    source: 'rolesPage',
    code: 'role-edit-refused',
    title: why,
  });
}

/** Everything that changes a ROW rather than a file. */
async function store(command: RolesCommand): Promise<boolean> {
  // Only `add` needs them, and only `add` pays for the read: an id whose deletion has not finished
  // is not free, because the four records keyed by it are still there.
  const taken = command.kind === 'add'
    ? reserved(await tombstonesIn(() => coaiDataDir()).all())
    : new Set<string>();
  const outcome = rowsAfter(rows(), command, taken);
  if (outcome.kind === 'unchanged') {
    return false;
  }
  if (outcome.kind === 'refused') {
    sayRefused(outcome.why);

    // A refusal DOES redraw: the control has just moved to a state that was not saved, and putting
    // it back is what makes the message about it true.
    return true;
  }

  await write(outcome.rows);
  await forget(outcome.forget);

  // A TYPED field never redraws. The prompt body was already exempt; a role's name and a prompt's
  // label were not, so storing them replaced the document under the person and put the caret at the
  // end of whatever they were half-way through writing. The summary line above the field catches up
  // with the name at the next structural change, which is a stale word against an unusable field.
  return fieldOf(command) === undefined;
}

/**
 * Removing a role: asked first, then the row and its prompt bodies together.
 *
 * <p>Asked because it is the one irreversible thing this page does — the row can be typed again, the
 * paragraphs of prose under it cannot. The files go WITH it: left behind they come back, because the
 * next role named the same generates the same id, which generates the same prompt id, and opens with
 * text the person believed they had deleted.</p>
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

  const name = rows().find((r) => r.id === id)?.name ?? id;
  // A question, so both the asking and the answer are written down. What a person DECLINED to
  // delete is as much a fact as what they deleted, and the 2026-09-16 incident turned on a role
  // whose removal nobody could later account for.
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
  if (answer !== 'Remove') {
    return false;
  }

  // Not `store`, which would write the row and delete the text in the same breath. The text waits
  // on the far side of the mirror having carried the row; `begin` writes the tombstone first, so a
  // host that dies in the middle leaves evidence rather than an orphaned prompt and a freed id.
  const outcome = rowsAfter(rows(), { kind: 'remove', id });
  if (outcome.kind === 'refused') {
    sayRefused(outcome.why);

    return true;
  }
  if (outcome.kind === 'unchanged') {
    return false;
  }
  await roleDeletions().begin({ id, name, promptIds: outcome.forget });

  return true;
}

/** The override files of prompts no row points at any more. */
async function forget(promptIds: readonly string[]): Promise<void> {
  for (const promptId of promptIds) {
    const file = promptFile(coaiDataDir(), promptId);
    if (file !== undefined) {
      await rm(file, { force: true }).catch((error: unknown) => {
        report('ConnectOtherAIs removed the role but could not delete one of its saved prompts.', error);
      });
    }
  }
}

/**
 * A prompt's text, written where the server reads it — or the file removed, which is how a shipped
 * prompt goes back to the text this product ships.
 *
 * <p>Written to a neighbouring file and RENAMED over the destination, because `writeFile` truncates
 * first: a crash between the truncation and the last byte leaves a prompt that is empty or half a
 * sentence, and the round after it asks a question nobody wrote. A rename either happened or did
 * not.</p>
 */
async function writeText(roleId: string, promptId: string, text: string): Promise<void> {
  // The id reaches a PATH, so it is checked twice: `promptFile` refuses anything that is not a slug,
  // which is what keeps it inside the prompts directory, and this refuses a slug that belongs to no
  // prompt of this role — so a webview cannot write over an unrelated role's override by naming it.
  const file = promptFile(coaiDataDir(), promptId);
  if (file === undefined || !promptBelongsTo(composed(rows()), roleId, promptId)) {
    report(`ConnectOtherAIs ignored a prompt it does not recognise (${promptId}).`, new Error('unknown prompt id'));

    return;
  }

  try {
    if (text.trim().length === 0) {
      await rm(file, { force: true });

      return;
    }

    const partial = `${file}.writing`;
    await mkdir(promptsDir(coaiDataDir()), { recursive: true });
    await writeFile(partial, text, 'utf8');
    await rename(partial, file);
  } catch (error: unknown) {
    report('ConnectOtherAIs could not save that prompt. Your text is still on the page — copy it somewhere before closing the tab.', error);
  }
}

/**
 * What a person is told, and what is kept for whoever has to diagnose it.
 *
 * <p>Two audiences and two texts: a sentence that says what happened and what to do about it goes to
 * the window, and the exception — whose message is a path, an errno and a stack — goes to the log,
 * where it is useful and where it is not being read by somebody who just wanted to rename a role.</p>
 */
function report(message: string, error: unknown): void {
  console.error('[coai] roles page:', message, error);
  // One code for the whole page's failures, with the SENTENCE as the subject: they are five
  // different things going wrong (a prompt that would not read, a page that would not draw, a
  // prompt file that would not delete) and giving them one counter would hide four behind one.
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
