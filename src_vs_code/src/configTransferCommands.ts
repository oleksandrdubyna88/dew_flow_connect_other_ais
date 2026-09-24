import { readdir, readFile, rm } from 'node:fs/promises';
import { basename } from 'node:path';
import * as vscode from 'vscode';
import { writeFileAtomically } from './atomicFile';
import { applyImport, failureSentence, type ApplyIo } from './configApply';
import { configFile, declaredSettings, exportedSettings, importQuestion, importedConfig, isPromptName } from './configTransfer';
import { coaiDataDir } from './dataDir';
import { notify, notifyAndAsk } from './notify';
import { promptFile, promptsDir } from './rolesPrompts';

/**
 * Export config / Import config, the `…` menu's pair — issue #467. Only VS Code here: every decision is
 * in `configTransfer` (what travels) and `configApply` (all or nothing).
 */

const IMPORT = 'Import';
const JSON_FILTER = { 'ConnectOtherAIs config': ['json'] };

export function registerConfigTransfer(context: vscode.ExtensionContext): vscode.Disposable[] {
  return [
    vscode.commands.registerCommand('coai.exportConfig', () => exportConfig(context)),
    vscode.commands.registerCommand('coai.importConfig', () => importConfig(context)),
  ];
}

async function exportConfig(context: vscode.ExtensionContext): Promise<void> {
  const target = await vscode.window.showSaveDialog({
    defaultUri: vscode.Uri.file('coai-config.json'), filters: JSON_FILTER, saveLabel: 'Export',
  });
  if (target === undefined) {
    return;
  }
  try {
    const config = vscode.workspace.getConfiguration('coai');
    const settings = exportedSettings(declaredSettings(context.extension.packageJSON), (key) => config.inspect(key)?.globalValue);
    const prompts = await promptTexts();
    await writeFileAtomically(target.fsPath, `${JSON.stringify(configFile(settings, prompts, new Date().toISOString()), null, 2)}\n`);
    void notify({
      as: 'information', class: 'outcome', source: 'configTransfer', code: 'config-exported',
      title: `Exported ${Object.keys(settings).length} setting(s) and ${Object.keys(prompts).length} prompt text(s) to ${basename(target.fsPath)}.`,
    });
  } catch (error) {
    void notify({
      as: 'error', class: 'failure', source: 'configTransfer', code: 'config-not-exported',
      title: 'The config was not exported.', detail: messageOf(error),
    });
  }
}

async function importConfig(context: vscode.ExtensionContext): Promise<void> {
  const picked = await vscode.window.showOpenDialog({ canSelectMany: false, filters: JSON_FILTER, openLabel: IMPORT });
  const file = picked?.[0];
  if (file !== undefined) {
    await importFrom(context, file);
  }
}

async function importFrom(context: vscode.ExtensionContext, file: vscode.Uri): Promise<void> {
  const imported = importedConfig(await readFile(file.fsPath, 'utf8').catch(() => ''), declaredSettings(context.extension.packageJSON));
  if (!imported.ok) {
    void notify({
      as: 'error', class: 'failure', source: 'configTransfer', code: 'config-not-imported',
      title: 'The config was not imported.', detail: imported.why,
    });

    return;
  }
  const answer = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'configTransfer', code: 'import-config', modal: true,
    title: importQuestion(imported, basename(file.fsPath), Object.keys(await promptTexts())), action: IMPORT,
  });
  if (answer === IMPORT) {
    await reportApplied(await applyImport(imported, hostIo()));
  }
}

async function reportApplied(applied: Awaited<ReturnType<typeof applyImport>>): Promise<void> {
  await (applied.ok
    ? notify({
      as: 'information', class: 'outcome', source: 'configTransfer', code: 'config-imported',
      title: `Imported ${applied.settings} setting(s) and ${applied.prompts} prompt text(s).`,
    })
    : notify({
      as: 'error', class: 'failure', source: 'configTransfer', code: 'config-import-rolled-back',
      title: 'The config was not imported.', detail: failureSentence(applied),
    }));
}

/** The reads and writes an import makes: base values, and prompt files through the SAME id guard. */
function hostIo(): ApplyIo {
  const config = vscode.workspace.getConfiguration('coai');

  return {
    baseValue: (key) => config.inspect(key)?.globalValue,
    writeSetting: async (key, value) => config.update(key, value, vscode.ConfigurationTarget.Global),
    readPrompt: async (id) => readFile(pathOf(id), 'utf8').catch(() => undefined),
    writePrompt: (id, text) => writeFileAtomically(pathOf(id), text),
    removePrompt: (id) => rm(pathOf(id), { force: true }),
  };
}

function pathOf(id: string): string {
  const file = promptFile(coaiDataDir(), id);
  if (file === undefined) {
    // `importedConfig` already refused it; reaching here is a caller that skipped that, and must not
    // turn into a path.
    throw new Error(`"${id}" is not a valid prompt name`);
  }

  return file;
}

/** Every prompt text a person wrote, by id — the files whose names pass the guard, nothing else. */
async function promptTexts(): Promise<Record<string, string>> {
  const names = await readdir(promptsDir(coaiDataDir())).catch(() => [] as string[]);
  const ids = names.filter((name) => name.endsWith('.md')).map((name) => name.slice(0, -'.md'.length)).filter(isPromptName);
  const texts = await Promise.all(ids.map(async (id) => [id, await readFile(pathOf(id), 'utf8')] as const));

  return Object.fromEntries(texts);
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
