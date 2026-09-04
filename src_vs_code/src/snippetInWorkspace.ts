import * as vscode from 'vscode';
import { SNIPPET_LOCATIONS, SNIPPET_MARKER, SnippetStatus, snippetStatus } from './claudeSnippet';

/**
 * Which generation of the snippet this workspace is carrying, if any.
 *
 * <p>It lived inside `PanelProvider` and served the panel's one line about staleness. It is here
 * because the COPY command needs the same answer — a person clicking "Copy the CLAUDE.md snippet"
 * is entitled to be told that the copy already in this repository is older than the one they just
 * took — and two readers of the same four files would drift the moment somebody added a fifth.</p>
 *
 * <p><b>Since the block became a shared rule</b> (`dew_flow_conventions/common/coai-review-gate.md`,
 * mounted at `.claude/rules/shared`), a repository in that family keeps NO copy of its own and the
 * four instruction files are empty of it — so the mounted rule is read too, and such a repository
 * reports `current` instead of the `absent` it would have reported before.</p>
 */
export async function pastedSnippetStatus(): Promise<SnippetStatus> {
  const root = vscode.workspace.workspaceFolders?.[0]?.uri;
  if (root === undefined) {
    return snippetStatus(undefined);
  }

  for (const name of SNIPPET_LOCATIONS) {
    const text = await readIfPresent(vscode.Uri.joinPath(root, name));
    // The FIRST location that carries it wins, and the instruction files are first on that list on
    // purpose: a paste in CLAUDE.md is what the AI here actually READS, so a stale one has to be
    // the sentence this reports. Answering with the mounted rule's version instead would put a
    // green light over text three revisions old that is still being obeyed. The duplicate itself
    // is somebody else's job — `gate-snippet-check.mjs` fails the build over it.
    if (text.includes(SNIPPET_MARKER)) {
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
