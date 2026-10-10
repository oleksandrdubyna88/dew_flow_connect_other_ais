import * as vscode from 'vscode';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { securityPromptId, type SecurityLane } from './securityLane';
import { promptFile } from './rolesPrompts';
import { refuseUnderMissingRoot } from './findingsBoard';

/** The ordinary text editor saves the same override the MCP reads on the next round. */
export async function editSecurityPrompt(dataDir: vscode.Uri, id: string, lane: SecurityLane): Promise<void> {
  if (!securityPromptId(id) || !lane.prompts.some(p => p.id === id)) return;
  const path = promptFile(dataDir.fsPath, id);
  if (path === undefined) return;
  const file = vscode.Uri.file(path);
  await ensureOverride(path);
  await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
}
async function ensureOverride(path: string): Promise<void> {
  // Never the missing NAMED data root (E5.0 of paths per side): an override cannot be edited into a folder that is not there.
  refuseUnderMissingRoot(dirname(path));
  await mkdir(dirname(path), { recursive: true });
  try { await writeFile(path, '', { flag: 'wx' }); }
  catch (error: unknown) {
    if (!alreadyExists(error)) throw error;
  }
}
function alreadyExists(error: unknown): boolean {
  return error instanceof Error && 'code' in error && error.code === 'EEXIST';
}
