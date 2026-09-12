import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import * as vscode from 'vscode';

import { coaiDataDir } from './dataDir';
import { composed, idFor, isBuiltIn, promptIdFor, promptIdsInUse, rolesFrom, type PromptRow, type RoleRow } from './roles';
import { isShippedPrompt, roleEdit, rolesHtml, type RolesCommand } from './rolesPage';
import { promptFile, promptsDir } from './rolesPrompts';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';

/**
 * The tab that edits review roles — a thin host over a pure page, the arrangement this extension
 * has three times already.
 *
 * <p>Everything decided here is decided in `rolesPage.ts` or `roles.ts`, both of which a test can
 * reach without a `vscode`. What is left is what only a host can do: read a setting, write one,
 * write a file, and repaint.</p>
 */

const SECTION = 'coai';
const KEY = 'roles';

let panel: vscode.WebviewPanel | undefined;

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

function rows(): readonly RoleRow[] {
  return rolesFrom(config().get(KEY));
}

async function write(next: readonly RoleRow[]): Promise<void> {
  await config().update(KEY, next, vscode.ConfigurationTarget.Global);
}

/** The body of every prompt that has one, by id — absent means "what this product ships". */
async function texts(): Promise<Record<string, string>> {
  const ids = [...promptIdsInUse(rows())];
  const found: Record<string, string> = {};
  await Promise.all(ids.map(async (id) => {
    const file = promptFile(coaiDataDir(), id);
    if (file === undefined) {
      return;
    }
    try {
      found[id] = await readFile(file, 'utf8');
    } catch {
      // Absent is the normal state: a prompt nobody has rewritten has no file, and the server
      // answers from the text embedded in its binary. Unreadable is rare and reads the same way
      // here — the page shows an empty box, and writing into it creates the file.
    }
  }));

  return found;
}

export function openRoles(): void {
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
  panel.webview.onDidReceiveMessage((message: unknown) => {
    void apply(roleEdit(message)).then((again) => {
      if (again) {
        void render();
      }
    });
  });
  panel.onDidDispose(() => {
    scale.dispose();
    panel = undefined;
  });
  void render();
}

/** What the panel section shows as the installed server, so the page can warn about an old one. */
let serverVersion = '';

export function rolesKnowTheServer(version: string): void {
  serverVersion = version;
}

async function render(): Promise<void> {
  if (panel === undefined) {
    return;
  }

  panel.webview.html = rolesHtml(
    {
      rows: rows(),
      texts: await texts(),
      serverVersion,
      perSide: config().get('perSideSettings') === true,
      uiScale: currentUiScale(),
    },
    nonce(),
  );
}

function nonce(): string {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
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
  if (command.kind === 'add') {
    return added();
  }
  if (command.kind === 'editPrompt' && command.field === 'text') {
    await writeText(command.promptId, command.value);

    return false;
  }
  // Restoring a shipped prompt is DELETING the override, which is what restore has meant to
  // `RolePrompts` since it was written: the default is embedded in the server's binary, so there is
  // nothing to copy back. It repaints, because the box has to empty and the button has to go grey.
  if (command.kind === 'restorePrompt') {
    await writeText(command.promptId, '');

    return true;
  }

  return rowsAfter(command);
}

async function added(): Promise<boolean> {
  const current = rows();
  const id = idFor('', new Set(current.map((r) => r.id.toLowerCase())));
  const promptId = promptIdFor(id, 'general', promptIdsInUse(current));
  await write([...current, { id, name: 'A new role', stage: 'result', programmingTask: true, active: true,
                             prompts: [{ id: promptId, label: 'General', purpose: '' }] }]);

  return true;
}

/** Everything that changes a ROW rather than a file. */
async function rowsAfter(command: Exclude<RolesCommand, { kind: 'ignore' | 'zoom' | 'add' }>): Promise<boolean> {
  const current = rows();
  const mine = current.find((r) => r.id === command.id);
  const known = mine ?? (isBuiltIn(command.id) ? { id: command.id } : undefined);
  if (known === undefined) {
    return false;
  }

  const next = changed(known, command, current);
  if (next === undefined) {
    return false;
  }

  await write(mine === undefined ? [...current, next] : current.map((r) => (r.id === command.id ? next : r)));

  return true;
}

function changed(row: RoleRow, command: Exclude<RolesCommand, { kind: 'ignore' | 'zoom' | 'add' }>, all: readonly RoleRow[]): RoleRow | undefined {
  if (command.kind === 'remove') {
    return undefined;
  }
  if (command.kind === 'edit') {
    return { ...row, [command.field]: command.value };
  }
  if (command.kind === 'addPrompt') {
    const label = 'A new prompt';
    const id = promptIdFor(row.id, label, promptIdsInUse(all));

    return { ...row, prompts: [...(row.prompts ?? []), { id, label, purpose: '' }] };
  }
  if (command.kind === 'removePrompt') {
    // A shipped prompt has no Remove button, and this refuses one anyway: its text is embedded in
    // the server's binary, so there would be nothing left to restore.
    if (isShippedPrompt(row.id, command.promptId)) {
      return undefined;
    }

    return { ...row, prompts: (row.prompts ?? []).filter((p) => p.id !== command.promptId) };
  }
  if (command.kind === 'editPrompt') {
    return { ...row, prompts: (row.prompts ?? []).map((p) => (p.id === command.promptId ? edited(p, command.field, command.value) : p)) };
  }

  return row;
}

function edited(prompt: PromptRow, field: 'label' | 'purpose' | 'text', value: string): PromptRow {
  // `text` never reaches a row — it is a file. Guarded rather than assumed, because a caller that
  // sent one would otherwise put a paragraph of prose into the setting.
  return field === 'text' ? prompt : { ...prompt, [field]: value };
}

/**
 * A prompt's text, written where the server reads it — or the file removed, which is how a shipped
 * prompt goes back to the text this product ships.
 */
async function writeText(promptId: string, text: string): Promise<void> {
  const file = promptFile(coaiDataDir(), promptId);
  if (file === undefined) {
    return;
  }

  try {
    if (text.trim().length === 0) {
      await rm(file, { force: true });

      return;
    }
    await mkdir(promptsDir(coaiDataDir()), { recursive: true });
    await writeFile(file, text, 'utf8');
  } catch (error: unknown) {
    void vscode.window.showErrorMessage(`ConnectOtherAIs could not save that prompt: ${String(error)}`);
  }
}

/** Exposed for the panel's own button: how many roles a person has of their own. */
export function ownRoleCount(): number {
  return composed(rows()).filter((r) => !isBuiltIn(r.id)).length;
}
