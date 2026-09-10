import * as vscode from 'vscode';
import { readSnippetStatus, SnippetStatus, snippetStatus } from './claudeSnippet';

/**
 * Which generation of the snippet this workspace is carrying, if any.
 *
 * <p>It lived inside `PanelProvider` and served the panel's one line about staleness. It is here
 * because the COPY command needs the same answer — a person clicking "Copy the CLAUDE.md snippet"
 * is entitled to be told that the copy already in this repository is older than the one they just
 * took — and two readers of the same four files would drift the moment somebody added a fifth.</p>
 *
 * <p><b>Since the block became a shared rule</b> (`dew_flow_conventions/common/coai-review-gate.md`,
 * mounted at `.agents/conventions`, with legacy mounts still supported), a repository in that family keeps NO copy of its own and the
 * four instruction files are empty of it — so the mounted rule is read too, and such a repository
 * reports `current` instead of the `absent` it would have reported before.</p>
 */
export async function pastedSnippetStatus(): Promise<SnippetStatus> {
  const root = vscode.workspace.workspaceFolders?.[0]?.uri;
  if (root === undefined) {
    return snippetStatus(undefined);
  }

  return readSnippetStatus(name => readIfPresent(vscode.Uri.joinPath(root, name)));
}

async function readIfPresent(uri: vscode.Uri): Promise<string> {
  try {
    return new TextDecoder().decode(await vscode.workspace.fs.readFile(uri));
  } catch {
    return ''; // an absent instruction file is the normal case, not a failure
  }
}
