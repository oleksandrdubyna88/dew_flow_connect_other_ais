import * as vscode from 'vscode';
import { ridFor } from './coaiInstall';
import { claudeSnippet } from './claudeSnippet';
import { CLIENT_TARGETS, installedMessage, mcpServerBlock } from './mcpBlock';
import { binaryPath, installLatest, installedVersion, updateIsAvailable } from './installer';
import { parseSession, renderRounds, SessionFile } from './rounds';
import { envBlock, settingsFrom } from './settingsShape';

/**
 * ConnectOtherAIs — the human surface. Four commands and no background work: the review itself
 * lives in `coai-mcp`, which an MCP client owns and starts.
 *
 * <p>The restraint is deliberate and inherited from CredsForDevs: the config block is OFFERED on
 * the clipboard, never written into another program's file; the binary goes into extension
 * storage, never onto the `PATH`.</p>
 */
export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.commands.registerCommand('coai.installServer', () => installServer(context)),
    vscode.commands.registerCommand('coai.copyConfigBlock', () => copyConfigBlock(context)),
    vscode.commands.registerCommand('coai.copyClaudeSnippet', copyClaudeSnippet),
    vscode.commands.registerCommand('coai.showRounds', showRounds),
  );

  void offerUpdate(context);
}

export function deactivate(): void {
  // Nothing to tear down: no server, no watcher, no port.
}

function settings(): ReturnType<typeof settingsFrom> {
  const config = vscode.workspace.getConfiguration('coai');
  return settingsFrom((section) => config.get(section));
}

async function installServer(context: vscode.ExtensionContext): Promise<void> {
  try {
    const target = await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: 'Installing coai-mcp…' },
      () => installLatest(context.globalStorageUri, context.globalState),
    );
    await vscode.env.clipboard.writeText(mcpServerBlock(target.fsPath, envBlock(settings())));
    const targets = CLIENT_TARGETS.map((t) => `${t.label} (${t.path})`).join(', ');
    void vscode.window.showInformationMessage(`${installedMessage(target.fsPath)} Paste it into: ${targets}`);
  } catch (error) {
    void vscode.window.showErrorMessage(`coai-mcp was not installed: ${message(error)}`);
  }
}

async function copyConfigBlock(context: vscode.ExtensionContext): Promise<void> {
  const rid = ridFor(process.platform, process.arch);
  if (rid === undefined) {
    void vscode.window.showErrorMessage('There is no published coai-mcp build for this platform yet.');
    return;
  }
  const path = binaryPath(context.globalStorageUri, rid);
  const installed = installedVersion(context.globalState) !== undefined;
  await vscode.env.clipboard.writeText(mcpServerBlock(path.fsPath, envBlock(settings())));
  void vscode.window.showInformationMessage(
    installed
      ? 'The MCP config block is on your clipboard — paste it into your client and restart it.'
      : 'The block is on your clipboard, but coai-mcp is not installed yet — run "Install the MCP Server…" first.',
  );
}

async function copyClaudeSnippet(): Promise<void> {
  const folder = vscode.workspace.workspaceFolders?.[0];
  const name = folder === undefined ? 'this repository' : folder.name;
  await vscode.env.clipboard.writeText(claudeSnippet(name));
  void vscode.window.showInformationMessage(
    `The CLAUDE.md snippet for ${name} is on your clipboard — paste it into that repository's CLAUDE.md.`,
  );
}

async function showRounds(): Promise<void> {
  const sessions = await readSessions();
  const document = await vscode.workspace.openTextDocument({
    content: renderRounds(sessions),
    language: 'markdown',
  });
  await vscode.window.showTextDocument(document, { preview: true });
}

/** The server's own session files: its data dir, or `COAI_DATA_DIR` when the person set one. */
async function readSessions(): Promise<SessionFile[]> {
  const configured = process.env['COAI_DATA_DIR'];
  const localAppData = process.env['LOCALAPPDATA'] ?? `${process.env['HOME'] ?? '.'}/.local/share`;
  const dir = vscode.Uri.file(configured ?? `${localAppData}/coai-mcp`);
  const sessionsDir = vscode.Uri.joinPath(dir, 'sessions');
  const sessions: SessionFile[] = [];
  try {
    for (const [name, kind] of await vscode.workspace.fs.readDirectory(sessionsDir)) {
      if (kind !== vscode.FileType.File || !name.endsWith('.json')) {
        continue;
      }
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(sessionsDir, name));
      const session = parseSession(new TextDecoder().decode(bytes));
      if (session !== undefined) {
        sessions.push(session);
      }
    }
  } catch {
    // No data dir yet — an empty view says so honestly.
  }
  return sessions;
}

async function offerUpdate(context: vscode.ExtensionContext): Promise<void> {
  if (installedVersion(context.globalState) === undefined || !(await updateIsAvailable(context.globalState))) {
    return;
  }
  const answer = await vscode.window.showInformationMessage(
    'A newer coai-mcp is published.',
    'Install it',
  );
  if (answer === 'Install it') {
    await installServer(context);
  }
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
