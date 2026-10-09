import * as vscode from 'vscode';
import { ChatCatalog } from './chatModels';
import { DISCOVERY_KEY, EMPTY_DISCOVERY, catalogUsing, discoveryFrom } from './chatDiscovery';
import {
  ModelPreset,
  PromptPreset,
  chatPromptPresetsFrom,
  freshPromptRow,
  rowsAfterMain,
  deadModelRow,
} from './chatPresets';
import { chatRead, savedModels } from './chatConfig';
import { chatModelAdd, chatModelEdit, type ChatModelStores, type ChatModelWrite } from './chatModelEdits';
import { askForAModel } from './chatModelWizard';
import { inCatalogTurn } from './catalogMigrationHost';
import { reportRefusal, saveSetting } from './sideConfig';
import { PresetCommand, editRepaints, editedRows, presetSettlesAs } from './chatPresetsMessages';
import { applyRevisionChoice, type RevisionPorts } from './chatPresetRevision';
import { userChatPresets } from './modelKeys';
import { settledWrites } from './settledWrites';
import { teamServersFrom } from './teamServers';

/**
 * The editing core of the chat presets — what both pages that edited them called (research/PLAN_one_model_catalog.md E4.6b):
 * the Chat presets tab (`chatPresetsPanel.ts`, deleted in E5.1) and, on the Settings page, Chat. Moved here from
 * `chatPresetsPanel.ts`, never copied — the roles' and the commands' arrangement (`rolesHost.ts`, `commandsHost.ts`).
 *
 * <p>Everything DECIDED is `chatPresetsMessages.ts`, `chatPresets.ts` and `chatModelEdits.ts`. What is here is what only a
 * host can do: read a setting and write one — through ONE settled-write queue, so one edit cannot overtake another. Whoever draws the presets listens with {@link onChatPresetsRedraw}.</p>
 */

const SECTION = 'coai';
const PROMPTS_KEY = 'chatPromptPresets';
const MODELS_KEY = 'chatModelPresets';

/**
 * This extension host, so the presets can be written through this side and read what the panel discovered.
 *
 * <p>The bind-once shape `chatReadsThisSide` and `rememberChatsIn` already use: a host has ONE context for its whole
 * life. Absent only in tests of the pure neighbours, and every use is guarded.</p>
 */
let context: vscode.ExtensionContext | undefined;

export function bindChatPresets(extension: vscode.ExtensionContext): void {
  context = extension;
}

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

export function savedPromptPresets(): readonly PromptPreset[] {
  const legacy = config().get('chatPrompt');

  return chatPromptPresetsFrom(config().get(PROMPTS_KEY), typeof legacy === 'string' ? legacy : '');
}

/**
 * The chat's models as the chat lists them (PLAN_one_model_catalog.md E4.6a): this side's rows ticked Chat, and a preset
 * only while the move has not taken it — with MAIN shown on the model the chat opens on, `coai.chatModel`, since the
 * catalog has no main of its own.
 */
export function presetModels(): readonly ModelPreset[] {
  const opensOn = chatRead(config())('chatModel');

  return savedModels(config()).map((model) => (model.main || model.id !== opensOn ? model : { ...model, main: true }));
}

/** This side's raw catalog rows, the raw presets and the chat model — what a model edit reads (E4.6a). */
function modelStores(): ChatModelStores {
  const read = chatRead(config());
  const rows = read('vendors');

  return {
    rows: Array.isArray(rows) ? rows.filter((row): row is Record<string, unknown> => typeof row === 'object' && row !== null) : [],
    presets: rowsOf(MODELS_KEY),
    chatModel: String(read('chatModel') ?? ''),
  };
}

/**
 * One model write, to the store it names: the catalog rows in the catalog's turn and through this side, the chat model
 * through this side, a preset the move has not taken where presets have always been written.
 */
async function saveModelWrite(one: ChatModelWrite): Promise<void> {
  if (one.key === 'chatModelPresets') {
    await write(MODELS_KEY, one.value);

    return;
  }
  const side = boundSide();
  // A refusal is said — `saveSetting` throws it, so the one cure it has (a window that has not caught up with an update
  // cannot store a key it never registered) is offered rather than lost in a log.
  await (one.key === 'vendors'
    ? inCatalogTurn(() => saveSetting(side, config(), one.key, one.value))
    : saveSetting(side, config(), one.key, one.value)
  ).catch((error: unknown) => {
    reportRefusal(side, one.key, error, { ordinary: 'ConnectOtherAIs could not save that change to your chat models.' });
  });
}

/** The extension context — loud rather than absent: writing the shared settings instead is the defect it prevents. */
function boundSide(): vscode.ExtensionContext {
  if (context === undefined) {
    throw new Error('the chat presets were saved before the extension bound them');
  }

  return context;
}

/**
 * What a row can be pointed at, with the PANEL's discoveries in it.
 *
 * <p>This passed every discovered list EMPTY, so `modelsFor` had nothing to build from and a `codex`
 * row offered exactly one model: the one it is configured to, marked `(yours)`. The operator opened
 * the chooser and asked why he could not see the models his reviewer card lists — and that card sits
 * in the panel, which is where the fetch happens. Same handoff the chat command was given:
 * `DISCOVERY_KEY` holds what the panel last found. Nothing is fetched here; a dialog must not wait on
 * three probes to open.</p>
 */
function chatCatalogHere(): ChatCatalog {
  const store = context?.globalState;

  return catalogUsing(
    store === undefined ? EMPTY_DISCOVERY : discoveryFrom(store.get(DISCOVERY_KEY)),
    teamServersFrom(config().get('teamServers')),
  );
}

async function write(key: string, value: unknown): Promise<void> {
  await config().update(key, value, vscode.ConfigurationTarget.Global);
}

function rowsOf(key: string): Record<string, unknown>[] {
  const saved = config().get(key);

  return Array.isArray(saved)
    ? saved.filter((row): row is Record<string, unknown> => typeof row === 'object' && row !== null)
    : [];
}

/**
 * The saved rows, with the migrated prompt written down if that is what is being edited.
 *
 * <p>Without this, the first edit to a migrated prompt would write a list containing only that edit
 * and lose the prompt it came from: the page shows what `chatPromptPresetsFrom` produced, and the
 * setting still holds nothing.</p>
 */
function promptRowsToWrite(): Record<string, unknown>[] {
  const saved = rowsOf(PROMPTS_KEY);

  return saved.length > 0
    ? saved
    : savedPromptPresets().map((preset) => ({ id: preset.id, name: preset.name, text: preset.text, main: preset.main }));
}

// ---------------------------------------------------------------- the one queue

/** Who redraws when the presets change shape — every page that draws them. */
const redraws = new Set<() => void | Promise<void>>();

/** A page that draws the presets, redrawn after every change that alters their shape. */
export function onChatPresetsRedraw(redraw: () => void | Promise<void>): vscode.Disposable {
  redraws.add(redraw);

  return new vscode.Disposable(() => { redraws.delete(redraw); });
}

async function redrawAll(): Promise<void> {
  await Promise.all([...redraws].map((redraw) => redraw()));
}

const writes = settledWrites<PresetCommand>({
  apply,
  render: redrawAll,
  report: (error) => {
    if (context === undefined) {
      console.error('[coai] chat presets: a change could not be saved', error);

      return;
    }
    reportRefusal(context, PROMPTS_KEY, error, { ordinary: 'ConnectOtherAIs could not save that change to your chat presets.' });
  },
  fieldOf: presetSettlesAs,
});

/** One edit, in the one queue. */
export const queueChatPresetEdit = (command: PresetCommand): Promise<void> => writes.queue(command);

/** Whatever is still settling — written before a page that typed it goes away. */
export const flushChatPresetEdits = (): Promise<void> => writes.flush();

type ListCommand = Extract<PresetCommand, { readonly list: 'prompt' | 'model' }>;

/** Store one command. Answers whether the pages must be redrawn. */
function apply(command: PresetCommand): Promise<boolean> {
  if (command.kind === 'revision') {
    return applyRevision(command);
  }

  return 'list' in command ? applyToList(command) : Promise.resolve(false);
}

/**
 * An answer to a preset an older build edited after the move (research/PLAN_one_model_catalog.md, epic 5 prerequisite (a),
 * R7) — carried out by `chatPresetRevision.applyRevisionChoice`: read and written in ONE catalog turn, so no migration
 * reads the row changed and the record not yet. Through this side, where the record and the rows it names live; never
 * the presets, which stay as the older build left them. A refusal stops the writes: the row goes first, so a choice
 * stopped before the record is still on Chat to answer again.
 */
function applyRevision(command: Extract<PresetCommand, { kind: 'revision' }>): Promise<boolean> {
  return applyRevisionChoice(command, revisionPorts(boundSide()));
}

/**
 * The window's half of a choice: the presets as the move reads them (`userChatPresets`, finding 6 of R7's code round),
 * this side's reader and its one save, a refusal said with its cure, and the catalog's turn.
 */
function revisionPorts(side: vscode.ExtensionContext): RevisionPorts {
  return {
    presets: () => userChatPresets(config()),
    reader: () => chatRead(config()),
    save: (key, value) => saveSetting(side, config(), key, value),
    refused: (key, error) => {
      reportRefusal(side, key, error, { ordinary: 'ConnectOtherAIs could not save your choice about the edited chat model.' });
    },
    turn: inCatalogTurn,
  };
}

function applyToList(command: ListCommand): Promise<boolean> {
  if (command.kind === 'add') {
    return command.list === 'model' ? addModel() : addPrompt();
  }

  return command.list === 'model' ? applyModel(command) : applyToPrompts(command);
}

/**
 * A model edited in the ONE store it lives in (E4.6a) — its catalog row, or a preset the move has not taken. A removal
 * and the main tick redraw; typing does not.
 */
async function applyModel(command: Extract<PresetCommand, { kind: 'edit' | 'remove' }>): Promise<boolean> {
  const changes = chatModelEdit(command, modelStores());
  for (const one of changes) {
    await saveModelWrite(one);
  }

  return changes.length > 0 && (command.kind === 'remove' || editRepaints(command));
}

async function addPrompt(): Promise<boolean> {
  const rows = promptRowsToWrite();
  await write(PROMPTS_KEY, [...rows, freshPromptRow(rows as { id: string }[])]);

  return true;
}

/**
 * A prompt edited or removed. The SAME list back means nothing changes — the rule refused (unticking the last main
 * one), the row already says that, or no row has that id — and writing it would be a configuration event that says
 * nothing, on every click. Typing does not redraw: the caret is in the box somebody is writing in; the `main` tick and a
 * removal do, because their effect is on rows the person is not in.
 */
async function applyToPrompts(command: Extract<PresetCommand, { kind: 'edit' | 'remove' }>): Promise<boolean> {
  const rows = promptRowsToWrite();
  const next = promptsAfter(command, rows);
  if (next === rows) {
    return false;
  }
  await write(PROMPTS_KEY, next);

  return command.kind === 'remove' || editRepaints(command);
}

/**
 * The `main` box is a rule of its own — exactly one, and the last one cannot be turned off — so it is decided beside
 * the reader that enforces the same thing, not in `editedRows`.
 */
function promptsAfter(command: Extract<PresetCommand, { kind: 'edit' | 'remove' }>, rows: readonly Record<string, unknown>[]): readonly Record<string, unknown>[] {
  if (command.kind === 'remove') {
    return rows.some((row) => row['id'] === command.id) ? rows.filter((row) => row['id'] !== command.id) : rows;
  }

  return command.field === 'main' ? rowsAfterMain(rows, command.id, command.value === true) : editedRows(rows, command);
}

/**
 * A new chat model, which cannot exist without a vendor for it to answer through: a catalog row ticked Chat, never a
 * preset (E4.6a) — the chat reads its models from the catalog. Escaping any step writes nothing.
 */
async function addModel(): Promise<boolean> {
  const chosen = await askForAModel(chatCatalogHere());
  if (chosen === undefined) {
    return false;
  }
  await saveModelWrite({ key: 'vendors', value: chatModelAdd(modelStores().rows, chosen.vendor, chosen.model, chosen.name, chosen.startingPrompt) });

  return true;
}

/**
 * The dead rows an older build wrote, cleared when the tab OPENS rather than on the next add.
 *
 * <p>Deferring it to the next write was the plan round's finding from all three vendors: somebody
 * who opens the tab, sees their list and closes it again never triggers one, and somebody with no
 * reviewer that can chat cannot trigger one at all — the add refuses before it writes. So the tab
 * cleans up when it is shown. It writes ONLY when something was actually dropped, so it runs once
 * and the render it triggers finds nothing left to do.</p>
 */
export async function pruneDeadModelRows(): Promise<void> {
  const rows = rowsOf(MODELS_KEY);
  const kept = rows.filter((row) => !deadModelRow(row));
  if (kept.length !== rows.length) {
    await write(MODELS_KEY, kept);
  }
}
