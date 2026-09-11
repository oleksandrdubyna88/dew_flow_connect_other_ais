import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { ChatCatalog, ChatProvider, chatProvidersFromPresets } from './chatModels';
import { DISCOVERY_KEY, EMPTY_DISCOVERY, catalogUsing, discoveryFrom } from './chatDiscovery';
import {
  ModelPreset,
  PromptPreset,
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  ChatVendorChoice,
  chatRunSpec,
  freshPromptRow,
  modelRowsAfterAdd,
  promptRowsAfterMain,
  deadModelRow,
} from './chatPresets';
import { PresetCommand, chatPresetsHtml, editRepaints, editedRows, presetEdit } from './chatPresetsPage';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';
import { CHAT_RUNTIMES } from './cliChatLaunch';
import { allowedModelsFor, modelsFor } from './models';
import { teamServersFrom } from './teamServers';
import { VENDOR_PRESETS } from './vendors';

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
  const store = presetsContext?.globalState;

  return catalogUsing(
    store === undefined ? EMPTY_DISCOVERY : discoveryFrom(store.get(DISCOVERY_KEY)),
    teamServersFrom(config().get('teamServers')),
  );
}

function providers(): readonly ChatProvider[] {
  return chatProvidersFromPresets(models(), chatCatalogHere()).providers;
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
/**
 * This extension host, so the presets tab can read what the panel discovered.
 *
 * <p>The bind-once shape `chatReadsThisSide` and `rememberChatsIn` already use, and for the reason
 * recorded there: a host has ONE context for its whole life, and threading it through a command, a
 * webview and four dialogs would put a parameter on six signatures to carry a value that never
 * changes. Absent only in tests of this file's pure neighbours, and every use is guarded.</p>
 */
let presetsContext: vscode.ExtensionContext | undefined;

export function presetsReadDiscoveriesFrom(context: vscode.ExtensionContext): void {
  presetsContext = context;
}

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
    : modelRowsAfterAdd(rows, chosen.vendor, chosen.model, chosen.name, chosen.startingPrompt);
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
{ vendor: ChatVendorChoice; model: string; name: string; startingPrompt: string } | undefined> {
  const chosen = await askWhichVendor();
  if (chosen === undefined) {
    return undefined;
  }
  const model = await askWhichModel(chosen.vendor, chosen.label);
  if (model === undefined) {
    return undefined;
  }
  const name = await vscode.window.showInputBox({
    title: 'Add a model — step 3 of 4',
    prompt: 'A name for this preset — it is what the button above the composer says',
    value: model.label.length > 0 ? model.label : chosen.label,
  });
  if (name === undefined) {
    return undefined;
  }
  // The LAST step is optional, and escaping it means "none" rather than "throw the other three
  // away". Escape is how a person skips an optional field in every other VS Code dialog, and
  // discarding a finished preset for using it is the wizard punishing the ordinary gesture.
  const startingPrompt = await vscode.window.showInputBox({
    title: 'Add a model — step 4 of 4, optional',
    prompt: 'What the composer opens with when this model is chosen. Leave it empty for none.',
    placeHolder: 'You are a business analyst…',
  });

  return { vendor: chosen.vendor, model: model.id, name, startingPrompt: startingPrompt ?? '' };
}

/**
 * Step 1 — the VENDORS, which is the list *Add a reviewer* offers.
 *
 * <p>Asked for five times before it was built, and the reason is the one that made the chat
 * independent in the first place: the person is choosing what will ANSWER, not borrowing somebody's
 * reviewer. One source — `VENDOR_PRESETS` and the *Team servers* section — so a vendor added to
 * the product appears here without anybody remembering to, and the sentence under each is the
 * vendor's own.</p>
 *
 * <p>Only vendors the chat can speak to are offered; the rest would be an entry that cannot answer.
 * A Team server contributes one entry per vendor IT hosts, because the server is the endpoint and
 * the vendor on it is what runs.</p>
 */
async function askWhichVendor(): Promise<{ label: string; vendor: ChatVendorChoice } | undefined> {
  const local = VENDOR_PRESETS
    .filter((preset) => CHAT_RUNTIMES.includes(preset.runtime))
    .map((preset) => ({
      label: preset.label,
      detail: preset.hint,
      vendor: { runtime: preset.runtime, baseUrl: preset.baseUrl } as ChatVendorChoice,
    }));
  const remote = teamServersFrom(config().get('teamServers')).flatMap((server) =>
    serverVendorsOf(server.id).map((name) => ({
      label: server.name + ' · ' + name,
      detail: 'on ' + server.url + " — the company's subscription, nothing to install",
      vendor: { runtime: 'remote', teamServerId: server.id, remoteVendor: name } as ChatVendorChoice,
    })));

  return vscode.window.showQuickPick(
    [...local, ...remote],
    { title: 'Add a model — step 1 of 4', placeHolder: 'Which vendor should answer?' },
  );
}

/** What a Team server said it hosts, from the catalog the panel last fetched. */
function serverVendorsOf(serverId: string): readonly string[] {
  const cached = chatCatalogHere().teamServers.find((one) => one.server.id === serverId);

  return (cached?.catalog?.vendors ?? []).map((one) => one.id);
}

/**
 * Step 2 — that provider's models.
 *
 * <p>A row whose models are DISCOVERED and whose probe has not answered offers none. Asking anyway
 * would be a dialog with nothing in it, so the answer is an empty model — which everywhere else in
 * this feature means "whatever the row is set to".</p>
 */
/**
 * Step 2 — the models that vendor offers, from the catalog the panel discovered.
 *
 * <p>A vendor whose models are DISCOVERED and whose probe has not answered offers none. Asking
 * anyway would be a dialog with nothing in it, so the answer is an empty model — which everywhere
 * else in this feature means "whatever that vendor is set to".</p>
 */
async function askWhichModel(vendor: ChatVendorChoice, label: string):
Promise<{ id: string; label: string } | undefined> {
  const catalog = chatCatalogHere();
  const spec = chatRunSpec({
    id: '',
    name: '',
    runtime: vendor.runtime,
    model: '',
    executablePath: '',
    baseUrl: vendor.baseUrl ?? '',
    teamServerId: vendor.teamServerId,
    remoteVendor: vendor.remoteVendor,
  });
  const offered = modelsFor(
    vendor.runtime,
    catalog.discoveredCodex,
    '',
    catalog.localEngine,
    catalog.discoveredAgy,
    allowedModelsFor(spec, catalog.teamServers).models,
  );
  if (offered.length === 0) {
    return { id: '', label: '' };
  }

  return vscode.window.showQuickPick(
    offered.map((one) => ({ label: one.label, id: one.id })),
    { title: 'Add a model — step 2 of 4', placeHolder: 'Which of ' + label + ' models?' },
  );
}
