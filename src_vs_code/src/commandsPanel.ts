import { randomBytes } from 'node:crypto';
import { readdir, readFile, rm } from 'node:fs/promises';
import * as vscode from 'vscode';
import { asText } from './asText';
import { writeFileAtomically } from './atomicFile';
import { COMMAND_PREFIX, commandsFrom, type CommandRow } from './commands';
import { commandsAfter, forgettable, textBelongs, type RowCommand } from './commandsEdit';
import { commandEdit, commandsHtml, type PageCommand } from './commandsPage';
import { coaiDataDir } from './dataDir';
import { serverOnThisSide } from './installer';
import { notify, notifyAndAsk } from './notify';
import { promptFile, promptsDir } from './rolesPrompts';
import { settledWrites } from './settledWrites';
import { appliedTextControl, pushTextControlsTo } from './textControlsHost';
import { currentTextTone } from './textToneHost';
import { currentUiScale } from './uiScaleHost';
import { readerFor, reportRefusal, saveSetting } from './sideConfig';
import { BusyHost } from './busyHost';
import { type BusySnapshot, IDLE } from './busySnapshot';
import { webviewNonce } from './webviewNonce';

/**
 * The tab that edits the gate's commands — issue #467, Epic B. A thin host over `commands.ts`,
 * `commandsEdit.ts` and `commandsPage.ts`, in the roles page's shape: read a setting, write one, write a
 * file, repaint.
 */

const SECTION = 'coai';
const KEY = 'commands';

let panel: vscode.WebviewPanel | undefined;

/**
 * What the tab asked for and the host has not finished — its busy mark (research/PLAN_busy_marks_on_every_webview.md, E3).
 * One per open tab: made with it, painted into every redraw, settled when it closes.
 */
let busy: BusyHost | undefined;
let context: vscode.ExtensionContext | undefined;
let serverVersion = '';

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

function side(): vscode.ExtensionContext {
  if (context === undefined) {
    throw new Error('the commands page was asked for settings before it was opened');
  }

  return context;
}

/** The rows as THIS SIDE has them — through `readerFor`, because `commands` is per side. */
function rows(): readonly CommandRow[] {
  return commandsFrom(readerFor(side(), config())(KEY));
}

/**
 * Hears the tab, and holds each structural change under its busy mark (research/PLAN_busy_marks_on_every_webview.md, E3).
 * One `BusyHost` per open tab, settled by the caller when it closes.
 */
function listen(opened: vscode.WebviewPanel): BusyHost {
  const marks = new BusyHost({ post: (message) => { void opened.webview.postMessage(message); } });
  busy = marks;
  opened.webview.onDidReceiveMessage((message: unknown) => {
    // A press on a text control is a setting of the person's, not an edit of the commands: it never
    // enters the write queue.
    if (!appliedTextControl(message, 'gate commands page')) {
      // A fresh tab's `ready` is told what is running; a numbered change is held until it has been written and the
      // tab redrawn. Typing posts unnumbered and settles on its own.
      if (marks.heard(message as object)) {
        return;
      }
      void marks.track(message as object, () => writes.queue(commandEdit(message)));
    }
  });

  return marks;
}

/** What a redraw paints as still running: nothing when no tab is open to hold any. */
function stillRunning(): BusySnapshot {
  return busy?.snapshot() ?? IDLE;
}

export function openCommands(extension: vscode.ExtensionContext): void {
  context = extension;
  if (panel !== undefined) {
    panel.reveal();

    return;
  }

  panel = vscode.window.createWebviewPanel(
    'coaiCommands',
    'Gate commands',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const text = pushTextControlsTo(panel.webview);
  const marks = listen(panel);
  panel.onDidDispose(() => {
    marks.dispose();
    busy = undefined;
    text.dispose();
    panel = undefined;
    // Whatever is still settling is somebody's typing; closing the tab must not lose it.
    void writes.flush();
  });
  void render().catch((error: unknown) => { report('ConnectOtherAIs could not draw the commands page.', error); });
  void askTheServer().catch((error: unknown) => {
    console.error('[coai] commands page: the installed server could not be identified', error);
  });
}

/** Once per opening: the page says when the installed server is too old to read any of this. */
async function askTheServer(): Promise<void> {
  const status = await serverOnThisSide(side().globalStorageUri, side().globalState, '');
  serverVersion = status.kind === 'absent' ? '' : status.version;
  await render();
}

async function render(): Promise<void> {
  if (panel === undefined) {
    return;
  }
  const html = commandsHtml(
    { rows: rows(), texts: await texts(), serverVersion, perSide: config().get('perSideSettings') === true, uiScale: currentUiScale(), textTone: currentTextTone(), busy: stillRunning() },
    webviewNonce(),
  );
  // Re-checked: `await texts()` is a suspension point, and the tab can close across it.
  if (panel !== undefined) {
    panel.webview.html = html;
  }
}

/**
 * Every command text on disk, by file id — blank ones too, because a new id must avoid a file a removed
 * command left (`nextId`). A file that is there and cannot be read is reported, never drawn as empty.
 */
async function texts(): Promise<Record<string, string>> {
  const dir = promptsDir(coaiDataDir());
  const found: Record<string, string> = {};
  for (const name of (await namesIn(dir)).filter((one) => one.startsWith(COMMAND_PREFIX) && one.endsWith('.md'))) {
    const body = await readFile(`${dir}/${name}`, 'utf8').catch((error: unknown) => unreadable(name, error));
    if (body !== undefined) {
      found[name.slice(0, -'.md'.length)] = body;
    }
  }

  return found;
}

/**
 * The prompts folder's names; none when there is no folder yet. Any OTHER failure is said: a folder that
 * cannot be listed would otherwise read as "no texts", every override would be drawn as the shipped
 * text, and a new id could land on a file still there. (our own reviewer, the code round.)
 */
async function namesIn(dir: string): Promise<string[]> {
  return readdir(dir).catch((error: unknown) => {
    if (codeOf(error) !== 'ENOENT') {
      report('ConnectOtherAIs could not list your saved command texts. What the page shows may not be what is saved.', error);
    }

    return [];
  });
}

/**
 * A file deleted between the listing and the read (another window) is simply gone; a directory is there
 * and says nothing; anything else is a person's own writing this page must not draw as empty unsaid.
 */
function unreadable(name: string, error: unknown): string | undefined {
  const code = codeOf(error);
  if (code === 'ENOENT' || code === 'EISDIR') {
    return code === 'EISDIR' ? '' : undefined;
  }
  report(`ConnectOtherAIs could not read a saved command text (${name}). It is shown empty — do not save over it.`, error);

  return '';
}

function codeOf(error: unknown): string {
  return (error as NodeJS.ErrnoException | undefined)?.code ?? '';
}

const writes = settledWrites<PageCommand>({
  apply,
  render,
  report: (error) => { reportRefusal(side(), KEY, error, { ordinary: 'ConnectOtherAIs could not save that change to your commands.' }); },
  fieldOf,
});

/** A typed field settles under its own key; a click goes straight through. */
function fieldOf(command: PageCommand): string | undefined {
  if (command.kind === 'text') {
    return `text/${command.fileId}`;
  }

  return command.kind === 'retitle' ? `title/${command.id}` : undefined;
}

/** Store one command. Answers whether the page must be redrawn. */
async function apply(command: PageCommand): Promise<boolean> {
  switch (command.kind) {
    case 'ignore':
      return false;
    case 'text':
      // Typing never redraws, even when the box is emptied: a redraw mid-sentence paints the box from
      // disk and the next keystroke saves alone over what was typed. (our own reviewer, the code round.)
      await writeText(command.fileId, command.value);

      return false;
    case 'restore':
      return writeText(command.fileId, '');
    default:
      return store(command);
  }
}

/** Everything that changes a ROW; a refusal is said and redraws, so the box goes back. */
async function store(command: RowCommand): Promise<boolean> {
  if (command.kind === 'remove' && !(await confirmedRemoval(command.id))) {
    return false;
  }

  return storeRows(command);
}

async function storeRows(command: RowCommand): Promise<boolean> {
  const outcome = commandsAfter(rows(), withToken(command), await texts());
  if (outcome.kind === 'refused') {
    sayRefused(outcome.why);

    return true;
  }
  if (outcome.kind === 'rows') {
    await saveSetting(side(), config(), KEY, outcome.rows);
    await Promise.all(outcome.forget.map((fileId) => writeText(fileId, '', true)));
  }

  // A retitle is typing: its box already shows what was typed.
  return command.kind !== 'retitle' && outcome.kind === 'rows';
}

/** An add gets its random token here — the page sends none, and the pure rules take it as given. */
function withToken(command: RowCommand): RowCommand {
  return command.kind === 'add' ? { kind: 'add', token: randomBytes(4).toString('hex') } : command;
}

/**
 * Removing a command deletes the words a person wrote for it, so it asks first — as the roles page does.
 * (our own reviewer, the code round.)
 */
async function confirmedRemoval(id: string): Promise<boolean> {
  const title = rows().find((one) => one.id === id)?.title ?? id;
  const answer = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'commandsPage', code: 'remove-command', modal: true,
    title: `Remove the command "${title}" and the text you wrote for it?`, action: REMOVE,
  });

  return answer === REMOVE;
}

const REMOVE = 'Remove';

/**
 * Write one text, or remove it when it says nothing — which is also what Restore is.
 *
 * @param forgetting a removed command's file, which no longer belongs to any row and may still go
 * @returns whether the page must be redrawn — a restore empties a box and hides its button
 */
async function writeText(fileId: string, text: string, forgetting = false): Promise<boolean> {
  const file = allowedFile(fileId, forgetting);
  if (file.length === 0) {
    report(`ConnectOtherAIs ignored a command text it does not recognise (${fileId}).`, new Error('unknown command text'));

    return false;
  }
  const empty = text.trim().length === 0;
  await (empty ? rm(file, { force: true }) : writeFileAtomically(file, text)).catch((error: unknown) => {
    report('ConnectOtherAIs could not save that command text. Your words are still on the page — copy them before closing the tab.', error);
  });

  return empty;
}

/**
 * The file a text may be written to, or empty. The id reaches a PATH: `promptFile` refuses anything that
 * is not a slug, and `textBelongs` refuses a slug that is neither a shipped text nor one of this side's
 * commands — unless it is a removed command's own file, being forgotten.
 */
function allowedFile(fileId: string, forgetting: boolean): string {
  const file = promptFile(coaiDataDir(), fileId) ?? '';
  const allowed = forgetting ? forgettable(fileId) : textBelongs(rows(), fileId);

  return allowed ? file : '';
}

function sayRefused(why: string): void {
  void notify({ as: 'information', class: 'refusal', source: 'commandsPage', code: 'command-edit-refused', title: why });
}

function report(message: string, error: unknown): void {
  console.error('[coai] commands page:', message, error);
  void notify({
    as: 'error', class: 'failure', source: 'commandsPage', code: 'commands-page-failure', subject: message, title: message, detail: asText(error),
  });
}
