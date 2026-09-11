import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { ChatProvider, chatProvidersFrom } from './chatModels';
import {
  ModelPreset,
  PromptPreset,
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  freshPreset,
  readableModelRow,
} from './chatPresets';
import { PresetCommand, chatPresetsHtml, presetEdit, repaintsAfter } from './chatPresetsPage';
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

/**
 * One edit, applied to the list it names.
 *
 * <p>The whole list is written back, because that is what a settings array IS — there is no way to
 * update one element of one. Ticking `main` untick's the others here rather than in the reader, so
 * what is SAVED is already true: a reader that had to correct the file every time it read it would
 * be hiding a file nobody could trust.</p>
 */
function edited(
  rows: readonly Record<string, unknown>[],
  command: Extract<PresetCommand, { kind: 'edit' }>,
): Record<string, unknown>[] {
  return rows.map((row) => {
    if (row['id'] !== command.id) {
      return command.field === 'main' && command.value === true ? { ...row, main: false } : row;
    }

    return { ...row, [command.field]: command.value };
  });
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
      await write(key, [...rows, freshPreset('prompt', rows as { id: string }[])]);

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
    await write(key, edited(rows, command));

    // `repaintsAfter` decides, beside the message parser and tested with it. Typing must NOT
    // repaint — the caret is in a box somebody is writing in — and the `main` tick MUST, because
    // its whole effect is on the rows it is not in.
    return repaintsAfter(command);
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
  const answering = providers()[0]?.id ?? '';
  if (answering.length === 0) {
    void vscode.window.showWarningMessage(
      'A model preset names the reviewer that answers, and no configured reviewer can chat yet.'
      + ' Enable one in the panel first — the chat speaks to antigravity, claude, codex and Team servers.',
    );

    return false;
  }
  const kept = rows.filter(readableModelRow);

  await write(MODELS_KEY, [...kept, freshPreset('model', kept as { id: string }[], answering)]);

  return true;
}
