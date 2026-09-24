import { readFile, rm } from 'node:fs/promises';
import { homedir } from 'node:os';
import { basename, join } from 'node:path';
import * as vscode from 'vscode';
import { writeFileAtomically } from './atomicFile';
import { applyImport, failureSentence, type Applied, type ApplyIo } from './configApply';
import { promptNames, promptTexts, readPromptOrAbsent } from './configPromptFiles';
import { configFile, declaredSettings, exportedSettings, importQuestion, importedConfig, type Imported } from './configTransfer';
import { coaiDataDir } from './dataDir';
import { notify, notifyAndAsk } from './notify';
import { oneAtATime } from './oneAtATime';
import { promptFile, promptsDir } from './rolesPrompts';

/**
 * Export config / Import config, the `…` menu's pair — issue #467. Only VS Code here: what travels is
 * `configTransfer`, all-or-nothing is `configApply`, and what a prompts folder holds is `configPromptFiles`.
 */

const IMPORT = 'Import';
const JSON_FILTER = { 'ConnectOtherAIs config': ['json'] };

/**
 * One import at a time: a second press while the first is writing is ignored, not queued — two imports
 * interleaving would each take the other's half-written values as the state to roll back to. (gemini, the
 * code round.) The first is visible the whole time, as a progress notification.
 */
const oneImport = oneAtATime<void>(undefined);

export function registerConfigTransfer(context: vscode.ExtensionContext): vscode.Disposable[] {
  return [
    vscode.commands.registerCommand('coai.exportConfig', () => exportConfig(context)),
    vscode.commands.registerCommand('coai.importConfig', () => oneImport(() => importConfig(context))),
  ];
}

/** The dialog's starting point: the home folder — a bare file name resolves to the drive's root. */
function suggestedFile(): vscode.Uri {
  return vscode.Uri.file(join(homedir(), 'coai-config.json'));
}

async function exportConfig(context: vscode.ExtensionContext): Promise<void> {
  const target = await vscode.window.showSaveDialog({ defaultUri: suggestedFile(), filters: JSON_FILTER, saveLabel: 'Export' });
  if (target === undefined) {
    return;
  }
  try {
    const config = vscode.workspace.getConfiguration('coai');
    const settings = exportedSettings(declaredSettings(context.extension.packageJSON), (key) => config.inspect(key)?.globalValue);
    const prompts = await promptTexts(promptsDir(coaiDataDir()));
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
  if (file === undefined) {
    return;
  }
  // EVERY failure is said, with the words the thrower used: a file that could not be read is not "not
  // JSON", and a prompts folder that could not be listed must not escape as a bare "command failed".
  // (our own reviewer, gemini, the code round.)
  try {
    await importFrom(file, importedConfig(await readFile(file.fsPath, 'utf8'), declaredSettings(context.extension.packageJSON)));
  } catch (error) {
    refuse(messageOf(error));
  }
}

async function importFrom(file: vscode.Uri, imported: Imported): Promise<void> {
  if (!imported.ok) {
    refuse(imported.why);

    return;
  }
  const answer = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'configTransfer', code: 'import-config', modal: true,
    title: importQuestion(imported, basename(file.fsPath), await promptNames(promptsDir(coaiDataDir()))), action: IMPORT,
  });
  if (answer === IMPORT) {
    await reportApplied(await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: `Importing ${basename(file.fsPath)}…` },
      () => applyImport(imported, hostIo()),
    ));
  }
}

function refuse(why: string): void {
  void notify({
    as: 'error', class: 'failure', source: 'configTransfer', code: 'config-not-imported',
    title: 'The config was not imported.', detail: why,
  });
}

async function reportApplied(applied: Applied): Promise<void> {
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
    readPrompt: (id) => readPromptOrAbsent(pathOf(id)),
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

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
