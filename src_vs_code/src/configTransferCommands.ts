import { mkdir, readdir, readFile, rm } from 'node:fs/promises';
import { homedir } from 'node:os';
import { basename, join } from 'node:path';
import * as vscode from 'vscode';
import { writeFileAtomically } from './atomicFile';
import { applyImport, failureSentence, type Applied, type ApplyIo } from './configApply';
import { promptNames, promptTexts, readPromptOrAbsent } from './configPromptFiles';
import { backupFileName, configFile, declaredSettings, exportedSettings, importQuestion, importedConfig, modelsSentence, staleBackups, type Imported } from './configTransfer';
import { userLayer } from './sideConfig';
import { vendorsFrom } from './vendors';
import { coaiDataDir } from './dataDir';
import { notify, notifyAndAsk } from './notify';
import { oneAtATime } from './oneAtATime';
import { promptFile, promptsDir } from './rolesPrompts';
import { askPerson } from './personWait';

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

/** The key names this side's vault holds, or `undefined` while nobody has asked coai-mcp. */
type VaultKeys = () => readonly string[] | undefined;

export function registerConfigTransfer(context: vscode.ExtensionContext, vaultKeys: VaultKeys): vscode.Disposable[] {
  return [
    vscode.commands.registerCommand('coai.exportConfig', () => exportConfig(context)),
    vscode.commands.registerCommand('coai.importConfig', () => oneImport(() => importConfig(context, vaultKeys))),
  ];
}

/** The dialog's starting point: the home folder — a bare file name resolves to the drive's root. */
function suggestedFile(): vscode.Uri {
  return vscode.Uri.file(join(homedir(), 'coai-config.json'));
}

/** This side's base settings and prompt texts as an export writes them — to a file a person chose, or a backup. */
async function writeExport(context: vscode.ExtensionContext, path: string): Promise<{ settings: number; prompts: number }> {
  const config = vscode.workspace.getConfiguration('coai');
  const settings = exportedSettings(declaredSettings(context.extension.packageJSON), (key) => config.inspect(key)?.globalValue);
  const prompts = await promptTexts(promptsDir(coaiDataDir()));
  await writeFileAtomically(path, `${JSON.stringify(configFile(settings, prompts, new Date().toISOString()), null, 2)}\n`);

  return { settings: Object.keys(settings).length, prompts: Object.keys(prompts).length };
}

async function exportConfig(context: vscode.ExtensionContext): Promise<void> {
  const target = await askPerson(() => vscode.window.showSaveDialog({ defaultUri: suggestedFile(), filters: JSON_FILTER, saveLabel: 'Export' }));
  if (target === undefined) {
    return;
  }
  try {
    const written = await writeExport(context, target.fsPath);
    void notify({
      as: 'information', class: 'outcome', source: 'configTransfer', code: 'config-exported',
      title: `Exported ${written.settings} setting(s) and ${written.prompts} prompt text(s) to ${basename(target.fsPath)}.`,
    });
  } catch (error) {
    void notify({
      as: 'error', class: 'failure', source: 'configTransfer', code: 'config-not-exported',
      title: 'The config was not exported.', detail: messageOf(error),
    });
  }
}

async function importConfig(context: vscode.ExtensionContext, vaultKeys: VaultKeys): Promise<void> {
  const picked = await askPerson(() => vscode.window.showOpenDialog({ canSelectMany: false, filters: JSON_FILTER, openLabel: IMPORT }));
  const file = picked?.[0];
  if (file === undefined) {
    return;
  }
  // EVERY failure is said, with the words the thrower used: a file that could not be read is not "not
  // JSON", and a prompts folder that could not be listed must not escape as a bare "command failed".
  // (our own reviewer, gemini, the code round.)
  try {
    await importFrom(context, file, importedConfig(await readFile(file.fsPath, 'utf8'), declaredSettings(context.extension.packageJSON)), vaultKeys);
  } catch (error) {
    refuse(messageOf(error));
  }
}

async function importFrom(context: vscode.ExtensionContext, file: vscode.Uri, imported: Imported, vaultKeys: VaultKeys): Promise<void> {
  if (!imported.ok) {
    refuse(imported.why);

    return;
  }
  const backup = backupPathFor(imported);
  const answer = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'configTransfer', code: 'import-config', modal: true,
    title: await importTitle(imported, file, backup, vaultKeys),
    action: IMPORT,
  });
  if (answer === IMPORT && await readyToImport(context, backup)) {
    await reportApplied(await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: `Importing ${basename(file.fsPath)}…` },
      () => applyImport(imported, hostIo()),
    ));
  }
}

/** The confirmation: what the import applies, what it does to the models, and where the setup it replaces goes. */
async function importTitle(imported: Extract<Imported, { ok: true }>, file: vscode.Uri, backup: string, vaultKeys: VaultKeys): Promise<string> {
  return [
    importQuestion(imported, basename(file.fsPath), await promptNames(promptsDir(coaiDataDir()))),
    modelsSentence(currentModels(), imported.settings['vendors'], vaultKeys()),
    backup.length === 0 ? '' : `Your current setup is saved to ${backup} first; Import config with that file puts it back.`,
  ].filter((part) => part.length > 0).join(' ');
}

/** Nothing to save first, or the setup saved — never an import that replaces models it could not keep a copy of. */
async function readyToImport(context: vscode.ExtensionContext, backup: string): Promise<boolean> {
  return backup.length === 0 || backedUp(context, backup);
}

/**
 * Whether an import touches the models — the catalog rows or a setting that refers to them (PLAN_one_model_catalog.md
 * E1.5). Such an import replaces them whole, so the setup it replaces is saved to a file first.
 */
function replacesModels(imported: Extract<Imported, { ok: true }>): boolean {
  return ['vendors', 'consultants', 'qconsultRows'].some((key) => Object.hasOwn(imported.settings, key));
}

/** The models this side's base layer holds now — what an import writes over. */
function currentModels(): ReturnType<typeof vendorsFrom> {
  return vendorsFrom(userLayer(vscode.workspace.getConfiguration('coai'))('vendors'));
}

/** Where this import saves the setup it replaces, or `''` when it replaces no model. */
function backupPathFor(imported: Extract<Imported, { ok: true }>): string {
  return replacesModels(imported) ? join(backupsDir(), backupFileName(new Date())) : '';
}

function backupsDir(): string {
  return join(coaiDataDir(), 'config-backups');
}

/** The current setup written to `path`, and all but the newest ten such files removed. A failure stops the import. */
async function backedUp(context: vscode.ExtensionContext, path: string): Promise<boolean> {
  try {
    await mkdir(backupsDir(), { recursive: true });
    await writeExport(context, path);
    await Promise.all(staleBackups(await readdir(backupsDir())).map((name) => rm(join(backupsDir(), name), { force: true })));

    return true;
  } catch (error) {
    refuse(`The current setup could not be saved to ${path} first, so nothing was imported: ${messageOf(error)}`);

    return false;
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
