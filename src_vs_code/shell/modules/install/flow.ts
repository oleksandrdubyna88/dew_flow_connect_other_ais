// The binary install and the update offer (v2 E4 story s5-install-module): what the old `activate()` did for `coai.installServer` and at its end, cut out of
// `src/extension.ts` WITHOUT a change of logic. Only the place moved: it runs when the install module loads (`./index.ts`), which the shell does only after the
// sidebar's first `menuReady` or the first command — never inside the cold start's window. `installer.ts` and `coaiInstall.ts` are untouched.
import * as vscode from 'vscode';

import { askWhereDataLives } from '../../../src/dataCommands';
import { serverEnv } from '../../../src/dataDir';
import { installFailureHint, SingleFlight } from '../../../src/coaiInstall';
import { clientTargetsLine, CLIENT_TARGETS, installedMessage, mcpServerBlock } from '../../../src/mcpBlock';
import { installedVersion, installLatest, latestServerVersion, serverOnThisSide } from '../../../src/installer';
import { notify, notifyAndAsk } from '../../../src/notify';

/** One install at a time: the panel button and the ⋯ menu are two doors to the same work. */
const installing = new SingleFlight<boolean>();

/** Installs the published server on this side; answers whether it is there now. A failure is said to the person here, and is `false`. */
export async function installServer(context: vscode.ExtensionContext): Promise<boolean> {
  return installing.run(() => install(context));
}

async function install(context: vscode.ExtensionContext): Promise<boolean> {
  try {
    const { target, firstTime } = await download(context);
    if (firstTime && await askWhereDataLives(context, true) === 'refused') {
      sayDataDirectoryNotSaved(target);

      return true;
    }
    await copyTheBlockAndSay(target);

    return true;
  } catch (error) {
    sayNotInstalled(error);

    return false;
  }
}

/** Downloads and installs the published server, with the progress notification. */
async function download(context: vscode.ExtensionContext): Promise<{ readonly target: vscode.Uri; readonly firstTime: boolean }> {
  // Asked BEFORE the download, because `installLatest` writes this side's record at the end of it
  // and the question is "has this side ever installed", not "did that just succeed".
  const firstTime = installedVersion(context.globalState, context.globalStorageUri) === undefined;
  const target = await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: 'Installing coai-mcp…' },
    () => installLatest(context.globalStorageUri, context.globalState),
  );

  return { target, firstTime };
}

/** The choice of where the data lives did not land, so the block would carry a configuration nobody asked for — and the install record written moments ago stops the question ever being asked again. (codex, code round.) */
function sayDataDirectoryNotSaved(target: vscode.Uri): void {
  void notify({
    as: 'error',
    class: 'failure',
    source: 'installServer',
    code: 'install-data-directory-not-saved',
    title: `coai-mcp is installed at ${target.fsPath}, but where its data should live could not be `
      + 'saved. Nothing has been copied to your clipboard: set it with "ConnectOtherAIs: Change '
      + 'where your data lives" and paste the block it gives you.',
    cure: 'Set the folder with "ConnectOtherAIs: Change where your data lives", then copy the block again.',
  });
}

/**
 * The block carries the two variables — the ONE configuration a client entry must hold, because
 * the settings file that carries everything else lives inside the directory they select. See
 * `serverEnv`, and the guard in `install.test.ts` that allows these two keys and no others.
 */
async function copyTheBlockAndSay(target: vscode.Uri): Promise<void> {
  await vscode.env.clipboard.writeText(mcpServerBlock(target.fsPath, serverEnv()));
  const targets = clientTargetsLine(CLIENT_TARGETS);
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'installServer',
    code: 'server-installed',
    title: `${installedMessage(target.fsPath)} Paste it into: ${targets}`,
  });
}

function sayNotInstalled(error: unknown): void {
  const raw = message(error);
  const hint = installFailureHint(raw, codeOf(error));
  void notify({
    as: 'error',
    class: 'failure',
    source: 'installServer',
    code: 'server-not-installed',
    title: hint.length > 0 ? `coai-mcp was not updated: ${hint}` : `coai-mcp was not installed: ${raw}`,
    // The raw reason as well as the hint: a hint that matched nothing is exactly the case where
    // somebody needs what the process actually said.
    detail: raw,
  });
}

/** Asks whether a newer server is published and, when one is, offers it. `absent` never offers: an install is not an update. Answers when the person has answered. */
export async function offerUpdate(context: vscode.ExtensionContext, installed: () => void): Promise<void> {
  const server = await serverOnThisSide(context.globalStorageUri, context.globalState, await publishedVersion());
  // `absent` never offers: an install is not an update, and this runs at activation — a machine
  // with nothing on this side would otherwise be told to update something it does not have.
  if (!server.updateOffered) {
    return;
  }
  const answer = await notifyAndAsk({
    as: 'information',
    class: 'offer',
    source: 'installServer',
    code: 'newer-server-published',
    title: 'A newer coai-mcp is published.',
    action: 'Install it',
  });
  if (answer === 'Install it' && await installServer(context)) {
    installed();
  }
}

/** The newest published version, or empty when GitHub is away (an offline machine is not an error worth showing). */
async function publishedVersion(): Promise<string> {
  return (await latestServerVersion()) ?? '';
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Node puts an errno here; `vscode.FileSystemError` puts its own name. Absent is empty, never a guess. */
function codeOf(error: unknown): string {
  const code = (error as { code?: unknown } | null)?.code;
  return typeof code === 'string' ? code : '';
}
