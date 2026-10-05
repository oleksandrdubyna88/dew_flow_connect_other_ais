import { randomBytes } from 'node:crypto';
import { readdir, readFile, rm } from 'node:fs/promises';
import * as vscode from 'vscode';
import { asText } from './asText';
import { writeFileAtomically } from './atomicFile';
import { COMMAND_PREFIX, commandsFrom, type CommandRow } from './commands';
import { commandsAfter, forgettable, textBelongs, type RowCommand } from './commandsEdit';
import type { PageCommand } from './commandsPage';
import { coaiDataDir } from './dataDir';
import { notify, notifyAndAsk } from './notify';
import { promptFile, promptsDir } from './rolesPrompts';
import { settledWrites } from './settledWrites';
import { readerFor, reportRefusal, saveSetting } from './sideConfig';

/**
 * The editing core of the gate's commands — what both pages that edit them call (todo/PLAN_one_model_catalog.md E4.4):
 * the Gate commands tab (`commandsPanel.ts`) and, on the new Settings page, Reviews › Commands. Moved here from
 * `commandsPanel.ts`, never copied — the roles' arrangement (`rolesHost.ts`).
 *
 * <p>Everything DECIDED is `commands.ts` and `commandsEdit.ts`. What is here is what only a host can do: read a setting,
 * write one, write a file — through ONE settled-write queue for both pages, so an edit on one cannot overtake the
 * other's. Whoever draws the commands listens with {@link onCommandsRedraw}.</p>
 */

const SECTION = 'coai';
const KEY = 'commands';

let context: vscode.ExtensionContext | undefined;

/** The window this extension runs in — the only thing that can say WHICH SIDE it is. Set at activation. */
export function bindCommands(extension: vscode.ExtensionContext): void {
  context = extension;
}

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

/** The extension context — loud rather than absent: reading the shared settings instead is the defect it prevents. */
export function commandsSide(): vscode.ExtensionContext {
  if (context === undefined) {
    throw new Error('the gate commands were asked for before the extension bound them');
  }

  return context;
}

/** The rows as THIS SIDE has them — through `readerFor`, because `commands` is per side. */
export function commandRows(): readonly CommandRow[] {
  return commandsFrom(readerFor(commandsSide(), config())(KEY));
}

/**
 * Every command text on disk, by file id — blank ones too, because a new id must avoid a file a removed command left
 * (`nextId`). A file that is there and cannot be read is reported, never drawn as empty.
 */
export async function commandTexts(): Promise<Record<string, string>> {
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
 * The prompts folder's names; none when there is no folder yet. Any OTHER failure is said: a folder that cannot be
 * listed would otherwise read as "no texts", every override would be drawn as the shipped text, and a new id could land
 * on a file still there. (our own reviewer, the code round.)
 */
async function namesIn(dir: string): Promise<string[]> {
  return readdir(dir).catch((error: unknown) => {
    if (codeOf(error) !== 'ENOENT') {
      reportCommandsFailure('ConnectOtherAIs could not list your saved command texts. What the page shows may not be what is saved.', error);
    }

    return [];
  });
}

/**
 * A file deleted between the listing and the read (another window) is simply gone; a directory is there and says
 * nothing; anything else is a person's own writing a page must not draw as empty unsaid.
 */
function unreadable(name: string, error: unknown): string | undefined {
  const code = codeOf(error);
  if (code === 'ENOENT' || code === 'EISDIR') {
    return code === 'EISDIR' ? '' : undefined;
  }
  reportCommandsFailure(`ConnectOtherAIs could not read a saved command text (${name}). It is shown empty — do not save over it.`, error);

  return '';
}

function codeOf(error: unknown): string {
  return (error as NodeJS.ErrnoException | undefined)?.code ?? '';
}

/** Who redraws when the commands change shape — every page that draws them. */
const redraws = new Set<() => Promise<void>>();

/** A page that draws the commands, redrawn after every change that alters their shape. */
export function onCommandsRedraw(redraw: () => Promise<void>): vscode.Disposable {
  redraws.add(redraw);

  return new vscode.Disposable(() => { redraws.delete(redraw); });
}

async function redrawAll(): Promise<void> {
  await Promise.all([...redraws].map((redraw) => redraw()));
}

const writes = settledWrites<PageCommand>({
  apply,
  render: redrawAll,
  report: (error) => { reportRefusal(commandsSide(), KEY, error, { ordinary: 'ConnectOtherAIs could not save that change to your commands.' }); },
  fieldOf,
});

/** One edit, from either page, in the one queue. */
export const queueCommandEdit = (command: PageCommand): Promise<void> => writes.queue(command);

/** Whatever is still settling — written before a page that typed it goes away. */
export const flushCommandEdits = (): Promise<void> => writes.flush();

/** A typed field settles under its own key; a click goes straight through. */
function fieldOf(command: PageCommand): string | undefined {
  if (command.kind === 'text') {
    return `text/${command.fileId}`;
  }

  return command.kind === 'retitle' ? `title/${command.id}` : undefined;
}

/** Store one command. Answers whether the pages must be redrawn. */
async function apply(command: PageCommand): Promise<boolean> {
  switch (command.kind) {
    case 'ignore':
      return false;
    case 'text':
      // Typing never redraws, even when the box is emptied: a redraw mid-sentence paints the box from disk and the next
      // keystroke saves alone over what was typed. (our own reviewer, the code round.)
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
  const outcome = commandsAfter(commandRows(), withToken(command), await commandTexts());
  if (outcome.kind === 'refused') {
    sayRefused(outcome.why);

    return true;
  }
  if (outcome.kind === 'rows') {
    await saveSetting(commandsSide(), config(), KEY, outcome.rows);
    await Promise.all(outcome.forget.map((fileId) => writeText(fileId, '', true)));
  }

  // A retitle is typing: its box already shows what was typed.
  return command.kind !== 'retitle' && outcome.kind === 'rows';
}

/** An add gets its random token here — the page sends none, and the pure rules take it as given. */
function withToken(command: RowCommand): RowCommand {
  return command.kind === 'add' ? { kind: 'add', token: randomBytes(4).toString('hex') } : command;
}

const REMOVE = 'Remove';

/** Removing a command deletes the words a person wrote for it, so it asks first — as the roles do. */
async function confirmedRemoval(id: string): Promise<boolean> {
  const title = commandRows().find((one) => one.id === id)?.title ?? id;
  const answer = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'commandsPage', code: 'remove-command', modal: true,
    title: `Remove the command "${title}" and the text you wrote for it?`, action: REMOVE,
  });

  return answer === REMOVE;
}

/**
 * Write one text, or remove it when it says nothing — which is also what Restore is.
 *
 * @param forgetting a removed command's file, which no longer belongs to any row and may still go
 * @returns whether the pages must be redrawn — a restore empties a box and hides its button
 */
async function writeText(fileId: string, text: string, forgetting = false): Promise<boolean> {
  const file = allowedFile(fileId, forgetting);
  if (file.length === 0) {
    reportCommandsFailure(`ConnectOtherAIs ignored a command text it does not recognise (${fileId}).`, new Error('unknown command text'));

    return false;
  }
  const empty = text.trim().length === 0;
  await (empty ? rm(file, { force: true }) : writeFileAtomically(file, text)).catch((error: unknown) => {
    reportCommandsFailure('ConnectOtherAIs could not save that command text. Your words are still on the page — copy them before closing the tab.', error);
  });

  return empty;
}

/**
 * The file a text may be written to, or empty. The id reaches a PATH: `promptFile` refuses anything that is not a slug,
 * and `textBelongs` refuses a slug that is neither a shipped text nor one of this side's commands — unless it is a
 * removed command's own file, being forgotten.
 */
function allowedFile(fileId: string, forgetting: boolean): string {
  const file = promptFile(coaiDataDir(), fileId) ?? '';
  const allowed = forgetting ? forgettable(fileId) : textBelongs(commandRows(), fileId);

  return allowed ? file : '';
}

function sayRefused(why: string): void {
  void notify({ as: 'information', class: 'refusal', source: 'commandsPage', code: 'command-edit-refused', title: why });
}

export function reportCommandsFailure(message: string, error: unknown): void {
  console.error('[coai] commands page:', message, error);
  void notify({
    as: 'error', class: 'failure', source: 'commandsPage', code: 'commands-page-failure', subject: message, title: message, detail: asText(error),
  });
}
