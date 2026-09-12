import { randomBytes } from 'node:crypto';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import * as vscode from 'vscode';

import { coaiDataDir } from './dataDir';
import { composed, isBuiltIn, promptIdsInUse, rolesFrom, type RoleRow } from './roles';
import { promptBelongsTo, rowsAfter } from './rolesEdit';
import { roleEdit, rolesHtml, type RolesCommand } from './rolesPage';
import { promptFile, promptsDir } from './rolesPrompts';
import { readerFor, saveSetting } from './sideConfig';
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

/** How long to wait after the last keystroke before storing a typed field. */
const SETTLE_MS = 300;

let panel: vscode.WebviewPanel | undefined;
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
}

/** What the panel section shows as the installed server, so the page can warn about an old one. */
let serverVersion = '';

/**
 * The panel telling this page what it found.
 *
 * <p>It REPAINTS when the answer changes. It used to only assign: `coai.editRoles` is on the command
 * palette, so this page can be open before the panel has ever rendered, and the version then arrived
 * into a variable nothing read again — the banner about a server too old to run any of these roles
 * stayed hidden for the whole session.</p>
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
      uiScale: currentUiScale(),
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
 * Commands are applied ONE AT A TIME, in the order they arrived.
 *
 * <p>Every one of them is a read-modify-write of the same setting. Fired concurrently — which is what
 * a webview does when somebody types — two of them read the same rows, and whichever `update`
 * resolves last wins: the earlier keystroke's value overwrites the later one, and the field reverts
 * under the person's hands. A single chain is the whole fix; it is short because nothing here is
 * slow, and a queue that can only grow while a key is held is bounded by the typing.</p>
 */
let working: Promise<void> = Promise.resolve();

function run(command: RolesCommand): void {
  working = working
    .then(() => apply(command))
    .then((again) => (again ? render() : undefined))
    .catch((error: unknown) => { report('ConnectOtherAIs could not save that change to your roles.', error); });
}

/** One message from the page: a typed field waits to settle, anything else goes straight through. */
function queue(command: RolesCommand): void {
  const field = fieldOf(command);
  if (field !== undefined) {
    settle(field, command);

    return;
  }

  // A structural command redraws the page, and the redraw replaces whatever is half-typed in it.
  // Store the settling fields first, in the order they were typed.
  drain();
  run(command);
}

/**
 * A field the person is still typing in, waiting to be stored.
 *
 * <p><b>Why not store every keystroke.</b> Each one is a write to `settings.json` or to a file, and a
 * forty-character role name was forty of them in four seconds — every one of which VS Code broadcasts
 * as a configuration change to every listener in the window. Settling for {@link SETTLE_MS} after the
 * last keystroke turns that into one write. Keyed by the field, so typing in a name and then in a
 * prompt stores both rather than the last one.</p>
 */
const settling = new Map<string, { readonly command: RolesCommand; readonly timer: NodeJS.Timeout }>();

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

function settle(field: string, command: RolesCommand): void {
  clearTimeout(settling.get(field)?.timer);
  settling.set(field, {
    command,
    timer: setTimeout(() => {
      settling.delete(field);
      run(command);
    }, SETTLE_MS),
  });
}

/** Everything still settling, stored now rather than when its timer would have fired. */
function drain(): void {
  const pending = [...settling.values()];
  settling.clear();
  for (const { command, timer } of pending) {
    clearTimeout(timer);
    run(command);
  }
}

/** Drained, and waited for — the tab is closing and nothing else will carry these. */
async function flush(): Promise<void> {
  drain();
  await working;
}

/**
 * One command, applied. Answers whether the page must be redrawn.
 *
 * <p>A text edit does NOT redraw: the person is typing in the box, and replacing the document under
 * them would move the caret to the end of it on every keystroke. Everything that changes the SHAPE
 * of the page — a role added or removed, a switch, a stage — does.</p>
 */
async function apply(command: RolesCommand): Promise<boolean> {
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

  return await store(command);
}

/** Everything that changes a ROW rather than a file. */
async function store(command: RolesCommand): Promise<boolean> {
  const outcome = rowsAfter(rows(), command);
  if (outcome.kind === 'unchanged') {
    return false;
  }
  if (outcome.kind === 'refused') {
    void vscode.window.showInformationMessage(outcome.why);

    return true;
  }

  await write(outcome.rows);
  await forget(outcome.forget);

  return true;
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
    void vscode.window.showInformationMessage('That is a role this product ships — it can be switched off, but not removed.');

    return true;
  }

  const name = rows().find((r) => r.id === id)?.name ?? id;
  const answer = await vscode.window.showWarningMessage(
    `Remove the role “${name}”?`,
    { modal: true, detail: 'Its prompts and everything you wrote in them are deleted. Rounds already recorded keep their findings.' },
    'Remove',
  );
  if (answer !== 'Remove') {
    return false;
  }

  return await store({ kind: 'remove', id });
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
  void vscode.window.showErrorMessage(message);
}
