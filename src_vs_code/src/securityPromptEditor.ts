import * as vscode from 'vscode';
import { securityPromptId, type SecurityLane } from './securityLane';
import { promptFile } from './rolesPrompts';
import { ensureOverride } from './securityPromptOverride';

/** The ordinary text editor saves the same override the MCP reads on the next round. */
export async function editSecurityPrompt(dataDir: vscode.Uri, id: string, lane: SecurityLane): Promise<void> {
  if (!securityPromptId(id) || !lane.prompts.some(p => p.id === id)) return;
  const path = promptFile(dataDir.fsPath, id);
  if (path === undefined) return;
  const file = vscode.Uri.file(path);
  await ensureOverride(path);
  await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
}
