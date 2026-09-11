import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { ChatProvider, chatProvidersFrom } from './chatModels';
import {
  ModelPreset,
  PromptPreset,
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  freshPromptRow,
  modelRowsAfterAdd,
  promptRowsAfterMain,
  deadModelRow,
} from './chatPresets';
import { PresetCommand, chatPresetsHtml, editRepaints, editedRows, presetEdit } from './chatPresetsPage';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';
import { teamServersFrom } from './teamServers';
import { vendorsFrom } from './vendors';

/**
 * The presets tab: one webview, reused while open.
 *
 * <p>Thin on purpose — the page decides what a message MEANS (`presetEdit`) and this decides what
 * `vscode` does about it. That is the arrangement the rounds log and the help page already use, and
 * the reason it is worth keeping is that everything interesting stays reachable from a unit test.</p>
 *
 * <p><b>Saved as it is typed, and re-rendered only when the shape changes.</b> An edit writes the
 * setting and leaves the page alone: re-rendering on every keystroke would move the caret to the end
 * of the box somebody is typing in the middle of, which is the defect the sidebar's prompt box had
 * and which its own plan fixed. Adding and removing a row DO re-render, because the list's shape is
 * what changed.</p>
 */

const SECTION = 'coai';
const PROMPTS_KEY = 'chatPromptPresets';
const MODELS_KEY = 'chatModelPresets';

let panel: vscode.WebviewPanel | undefined;

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

function prompts(): readonly PromptPreset[] {
  const legacy = config().get('chatPrompt');

  return chatPromptPresetsFrom(config().get(PROMPTS_KEY), typeof legacy === 'string' ? legacy : '');
}

function models(): readonly ModelPreset[] {
  return chatModelPresetsFrom(config().get(MODELS_KEY));
}

/** The rows a model preset may point at — the same list the chat's own picker offers. */
function providers(): readonly ChatProvider[] {
  const vendors = vendorsFrom(config().get('vendors'));

  return chatProvidersFrom(vendors, {
    discoveredCodex: [],
    discoveredAgy: [],
    localEngine: undefined,
    teamServers: teamServersFrom(config().get('teamServers'))
      .map((server) => ({ server, email: '', problem: '', stale: false })),
  }).providers;
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
    : prompts().map((preset) => ({ id: preset.id, name: preset.name, text: preset.text, main: preset.main }));
}

async function apply(command: PresetCommand): Promise<boolean> {
  if (command.kind === 'zoom') {
    await applyZoomDelta(command.delta);

    return false;
  }
  const key = command.kind === 'ignore' ? '' : (command.list === 'prompt' ? PROMPTS_KEY : MODELS_KEY);
  if (command.kind === 'add') {
    const rows = key === PROMPTS_KEY ? promptRowsToWrite() : rowsOf(key);
    if (command.list === 'prompt') {
      await write(key, [...rows, freshPromptRow(rows as { id: string }[])]);

      return true;
    }

    return addModel(rows);
  }
  if (command.kind === 'remove') {
    const rows = key === PROMPTS_KEY ? promptRowsToWrite() : rowsOf(key);
    await write(key, rows.filter((row) => row['id'] !== command.id));

    return true;
  }
  if (command.kind === 'edit') {
    const rows = key === PROMPTS_KEY ? promptRowsToWrite() : rowsOf(key);
    // The `main` box is a rule of its own — exactly one, and the last one cannot be turned off —
    // so it is decided beside the reader that enforces the same thing, not in `edited`.
    // The LIST as well as the field: `main` is a rule of the PROMPT list — exactly one, and the last
    // cannot be turned off — and a message naming the model list must not be given it. No model row
    // carries a `main` today, which is exactly why the guard belongs here rather than in a comment.
    const next = command.list === 'prompt' && command.field === 'main'
      ? promptRowsAfterMain(rows, command.id, command.value === true)
      : editedRows(rows, command);
    // The SAME array back means the rule refused — unticking the last main one, or an id naming no
    // row. Writing it would be a configuration change event that says nothing, on every click.
    if (next === rows) {
      // Refused, or asked for what the file already said. Nothing was written, so there is nothing to
      // draw again — and a repaint would move a caret in some other row for an edit that did nothing.
      return false;
    }
    await write(key, next);

    // `editRepaints` decides, beside the message parser and tested with it. Typing must NOT repaint —
    // the caret is in a box somebody is writing in — and the `main` tick MUST, because its whole
    // effect is on the rows it is not in.
    return editRepaints(command);
  }

  return false;
}

function render(): void {
  if (panel === undefined) {
    return;
  }
  panel.webview.html = chatPresetsHtml(
    { prompts: prompts(), models: models(), providers: providers(), uiScale: currentUiScale() },
    crypto.randomBytes(16).toString('hex'),
  );
}

/** Open the tab, or bring back the one that is already open. */
export function openChatPresets(): void {
  if (panel !== undefined) {
    panel.reveal();

    return;
  }
  // AWAITED before anything is drawn, which the code round was right about: the prune writes, the
  // render reads, and started side by side the page paints the very rows the prune is removing. The
  // tab opens after it either way — a cleanup that cannot run is not a reason to withhold the tab,
  // and it is said out loud rather than swallowed.
  void pruneDeadModelRows()
    .catch((error: unknown) => { console.error('coai: the unusable model presets could not be cleared', error); })
    .then(openPanel);
}

function openPanel(): void {
  if (panel !== undefined) {
    panel.reveal();

    return;
  }
  panel = vscode.window.createWebviewPanel(
    'coaiChatPresets',
    'Chat presets',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const scale = pushUiScaleTo(panel.webview);
  panel.webview.onDidReceiveMessage((message: unknown) => {
    void apply(presetEdit(message)).then((again) => {
      if (again) {
        render();
      }
    });
  });
  panel.onDidDispose(() => {
    scale.dispose();
    panel = undefined;
  });
  render();
}

/**
 * A new model preset, which cannot exist without a row for it to answer through.
 *
 * <p>`chatModelPresetsFrom` refuses a row with no provider, so seeding one with an empty string
 * wrote a preset nothing could ever render, edit or remove — which is exactly what *Add a model* did
 * until 2026-09-11. The provider is resolved FIRST: the first row that can chat, which is the same
 * default the chat itself takes, and the person changes it in the select on the row.</p>
 *
 * <p>With no such row there is nothing to seed, and a button that silently writes litter is worse
 * than one that says why it cannot. The dead rows an earlier build left behind go at the same time —
 * they are not drafts, because no surface has ever been able to show one.</p>
 */
async function addModel(rows: readonly Record<string, unknown>[]): Promise<boolean> {
  const chosen = await askForAModel();
  const written = chosen === undefined
    ? undefined
    : modelRowsAfterAdd(rows, chosen.provider, chosen.model, chosen.name, chosen.startingPrompt);
  if (written === undefined) {
    return false;
  }
  await write(MODELS_KEY, written);

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
async function pruneDeadModelRows(): Promise<void> {
  const rows = rowsOf(MODELS_KEY);
  const kept = rows.filter((row) => !deadModelRow(row));
  if (kept.length !== rows.length) {
    await write(MODELS_KEY, kept);
  }
}

/**
 * The four questions a model preset is made of, asked one at a time.
 *
 * <p>The shape of *Add a reviewer* — `showQuickPick` with a label and the sentence under it — because
 * that is what the operator asked for by name, and because a row id alone ("remsoftdev-codex") does
 * not say what it reaches. The list is the configured ROWS rather than the vendor kinds that dialog
 * offers: a vendor cannot say WHICH row answers, and two `codex` rows with different keys or prices
 * are two different backends, which is the decision three reviewers settled on the provider plan.</p>
 *
 * <p>Each step can be escaped, and escaping writes nothing — a half-made preset is the defect this
 * whole area has just been cleared of. The starting prompt is the one optional step: empty is a
 * preset that only changes the model.</p>
 */
async function askForAModel(): Promise<
{ provider: string; model: string; name: string; startingPrompt: string } | undefined> {
  const rows = providers();
  if (rows.length === 0) {
    void vscode.window.showWarningMessage(
      'A model preset names the reviewer that answers, and no configured reviewer can chat yet.'
      + ' Enable one in the panel first — the chat speaks to antigravity, claude, codex and Team servers.',
    );

    return undefined;
  }
  const provider = await askWhichProvider(rows);
  if (provider === undefined) {
    return undefined;
  }
  const model = await askWhichModel(provider.row);
  if (model === undefined) {
    return undefined;
  }
  const name = await vscode.window.showInputBox({
    title: 'Add a model — step 3 of 4',
    prompt: 'A name for this preset — it is what the button above the composer says',
    value: model.label.length > 0 ? model.label : provider.label,
  });
  if (name === undefined) {
    return undefined;
  }
  // The LAST step is optional, and escaping it means "none" rather than "throw the other three
  // away". Escape is how a person skips an optional field in every other VS Code dialog, and
  // discarding a finished preset for using it is the wizard punishing the ordinary gesture.
  // (local, the code round, Blocking.)
  const startingPrompt = await vscode.window.showInputBox({
    title: 'Add a model — step 4 of 4, optional',
    prompt: 'What the composer opens with when this model is chosen. Leave it empty for none.',
    placeHolder: 'You are a business analyst…',
  });

  return { provider: provider.row.id, model: model.id, name, startingPrompt: startingPrompt ?? '' };
}

/** Step 1 — the configured rows, each with the sentence that says what it reaches. */
function askWhichProvider(rows: readonly ChatProvider[]):
Thenable<{ label: string; row: ChatProvider } | undefined> {
  return vscode.window.showQuickPick(
    rows.map((one) => ({ label: one.id, detail: one.caption, row: one })),
    { title: 'Add a model — step 1 of 4', placeHolder: 'Which provider answers?' },
  );
}

/**
 * Step 2 — that provider's models.
 *
 * <p>A row whose models are DISCOVERED and whose probe has not answered offers none. Asking anyway
 * would be a dialog with nothing in it, so the answer is an empty model — which everywhere else in
 * this feature means "whatever the row is set to".</p>
 */
async function askWhichModel(row: ChatProvider): Promise<{ id: string; label: string } | undefined> {
  if (row.models.length === 0) {
    return { id: '', label: '' };
  }

  return vscode.window.showQuickPick(
    row.models.map((one) => ({ label: one.label, id: one.id })),
    { title: 'Add a model — step 2 of 4', placeHolder: `Which of ${row.id}'s models?` },
  );
}
