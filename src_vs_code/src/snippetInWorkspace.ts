import * as vscode from 'vscode';
import { SnippetStatus, snippetStatus } from './claudeSnippet';

/**
 * Which generation of the snippet this workspace is carrying, if any.
 *
 * <p>It lived inside `PanelProvider` and served the panel's one line about staleness. It is here
 * because the COPY command needs the same answer — a person clicking "Copy the CLAUDE.md snippet"
 * is entitled to be told that the copy already in this repository is older than the one they just
 * took — and two readers of the same four files would drift the moment somebody added a fifth.</p>
 */
export async function pastedSnippetStatus(): Promise<SnippetStatus> {
  const root = vscode.workspace.workspaceFolders?.[0]?.uri;
  if (root === undefined) {
    return snippetStatus(undefined);
  }

  for (const name of ['CLAUDE.md', 'AGENTS.md', 'GEMINI.md', '.github/copilot-instructions.md']) {
    const text = await readIfPresent(vscode.Uri.joinPath(root, name));
    // The FIRST file that carries it wins. A repository with the block in two files has a problem
    // this panel cannot fix, and reporting the older of the two would be arbitrary.
    if (text.includes('Multi-model review gate (ConnectOtherAIs)')) {
      return snippetStatus(text);
    }
  }

  return snippetStatus(undefined);
}

async function readIfPresent(uri: vscode.Uri): Promise<string> {
  try {
    return new TextDecoder().decode(await vscode.workspace.fs.readFile(uri));
  } catch {
    return ''; // an absent instruction file is the normal case, not a failure
  }
}
