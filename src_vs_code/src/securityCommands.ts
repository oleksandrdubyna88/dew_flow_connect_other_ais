import * as vscode from 'vscode';
import { unlink } from 'node:fs/promises';
import { askPerson } from './personWait';
import { notify, notifyAndAsk } from './notify';
import { promptFile, promptsDir } from './rolesPrompts';
import type { SecurityLane } from './securityLane';
import type { SecurityFlowHost } from './securityFlows';
import { editSecurityPrompt } from './securityPromptEditor';
import { securityTextStates } from './securityPromptFiles';

/**
 * The Security lane tab's flows (`securityFlows.ts`) bound to VS Code: the input box, the modal and the messages through
 * the `notify()` funnel, the editor's open documents, the files on disk. Everything that DECIDES is in the flows and is
 * tested there; this module only connects.
 */
export interface SecurityHostBase {
  readonly lane: () => SecurityLane;
  readonly dataDir: vscode.Uri;
  readonly enqueue: (work: () => Promise<void>) => void;
  readonly write: (field: string, value: unknown) => Promise<void>;
}

export function securityFlowHost(base: SecurityHostBase): SecurityFlowHost {
  const dir = base.dataDir.fsPath;
  return {
    ...base,
    promptsDir: promptsDir(dir),
    fileOf: (id) => promptFile(dir, id),
    textState: async (id) => (await securityTextStates(dir, [id]))[id] ?? 'none',
    unsaved,
    remove: removeFile,
    askName: (problem) => askPerson(() => vscode.window.showInputBox({
      title: 'New custom security prompt', value: 'redteam-', valueSelection: [8, 8],
      prompt: `Its text lives in ${promptsDir(dir)}/<name>.md — a file already there keeps its text.`,
      validateInput: (typed) => problem(typed) || undefined,
    })),
    confirm,
    tell,
    open: (id) => editSecurityPrompt(base.dataDir, id, base.lane()),
  };
}

/** Any open document on that file with unsaved changes — background tabs included, which `textDocuments` lists. */
const unsaved = (file: string): boolean =>
  vscode.workspace.textDocuments.some(d => d.isDirty && d.uri.fsPath.toLowerCase() === vscode.Uri.file(file).fsPath.toLowerCase());

async function removeFile(file: string): Promise<void> {
  try {
    await unlink(file);
  } catch (error: unknown) {
    if (!gone(error)) throw error;
  }
}
const gone = (error: unknown): boolean => error instanceof Error && 'code' in error && error.code === 'ENOENT';

/** Why a press did nothing, or did less than asked — through the one door every message of this extension uses. */
function tell(message: string): void {
  void notify({ as: 'warning', class: 'refusal', source: 'securityLane', code: 'security-lane-command', title: message });
}

/** A modal question through the funnel, which pauses the busy mark while the person decides (`askPerson`). */
async function confirm(question: string, action: string): Promise<boolean> {
  const pressed = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'securityLane', code: 'security-lane-confirm', modal: true, title: question, action,
  });
  return pressed === action;
}
