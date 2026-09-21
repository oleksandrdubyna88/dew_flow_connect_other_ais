import * as vscode from 'vscode';

import { RevisionDocument, revisionDocumentPath } from './openAtRevision';

/**
 * The two calls that reach the editor for the review page's revision actions — and nothing else.
 *
 * <p>Everything decidable is decided in `openAtRevision.ts`, where it is a unit test; this module
 * is what remains once the decision is made, and `bugzReviewWiring.test.ts` pins that the panel
 * reaches it through the hooks `panelProvider.ts` wires.</p>
 */

/** The scheme of a file opened at its revision — this product's own, so nothing else claims the URI. */
export const REVISION_SCHEME = 'coai-revision';

/**
 * Read-only documents of {@link REVISION_SCHEME}: the file at a commit, as text, one per (commit, path)
 * a person opened.
 *
 * <p><b>A content provider, not an untitled document</b> — two plan reviewers, independently: an
 * untitled buffer is dirty, Ctrl+S prompts to save it to disk, and it is not read-only. Its tab
 * names the revision too, because the URI carries the short sha in the file's own name
 * ({@link revisionDocumentPath}).</p>
 *
 * <p><b>What read-only means here, measured in a real editor rather than assumed.</b> This block
 * used to say a document of a registered scheme is read-only BY CONSTRUCTION, and the host
 * scenario written for story 3.1's code round proved that wrong: `workspace.applyEdit` APPLIES to
 * a provider buffer. What holds is the narrower and sufficient promise — `save()` refuses and the
 * URI is not a `file:`, so an edit has nowhere to be written and the working tree is never at
 * risk. Both facts are pinned by that scenario, because a comment that overclaims is worse than
 * none: the next reader stops checking.</p>
 *
 * <p>The text is held only while its document is open: `onDidCloseTextDocument` forgets it, so the
 * map is bounded by the tabs a person has open rather than by everything they ever pressed. The
 * content at one (commit, path) never changes, so a document reopened after being closed is simply
 * held again before it is opened; no change event is ever needed.</p>
 */
export class RevisionDocuments implements vscode.TextDocumentContentProvider {
  private held: ReadonlyMap<string, string> = new Map<string, string>();

  /** Registered once, for the extension's lifetime, with its two subscriptions disposed by the host. */
  static register(context: vscode.ExtensionContext): RevisionDocuments {
    const documents = new RevisionDocuments();
    context.subscriptions.push(
      vscode.workspace.registerTextDocumentContentProvider(REVISION_SCHEME, documents),
      vscode.workspace.onDidCloseTextDocument((closed) => documents.forget(closed.uri)),
    );

    return documents;
  }

  provideTextDocumentContent(uri: vscode.Uri): string {
    return this.held.get(uri.toString()) ?? '';
  }

  /** Shows the file at its revision, with the cursor on the finding's line — exact there. */
  async show(document: RevisionDocument): Promise<void> {
    const at = revisionDocumentPath(document.sha, document.file);
    const uri = vscode.Uri.from({ scheme: REVISION_SCHEME, path: at.path, query: at.query });
    this.held = new Map([...this.held, [uri.toString(), document.text]]);
    const opened = await vscode.workspace.openTextDocument(uri);
    await vscode.window.showTextDocument(opened, { preview: true, ...selectionAt(document.line) });
  }

  private forget(uri: vscode.Uri): void {
    if (uri.scheme === REVISION_SCHEME) {
      this.held = new Map([...this.held].filter(([key]) => key !== uri.toString()));
    }
  }
}

/**
 * Shows the CURRENT file, from the live filesystem — only ever after `currentFileIn` has said where
 * it really is. The recorded line is where a person looks first; the page says it may differ.
 */
export async function showCurrentFile(file: string, line: number): Promise<void> {
  await vscode.window.showTextDocument(vscode.Uri.file(file), { ...selectionAt(line) });
}

/** The open workspace folders as OS paths — `fsPath`, never `path`, because the guard compares against `realpath`. */
export function workspaceFolderPaths(): readonly string[] {
  return (vscode.workspace.workspaceFolders ?? []).map((folder) => folder.uri.fsPath);
}

/** A selection on one line, or nothing when no line was recorded — spread, because the option is optional. */
function selectionAt(line: number): { readonly selection?: vscode.Range } {
  if (line <= 0) {
    return {};
  }
  const at = new vscode.Position(line - 1, 0);

  return { selection: new vscode.Range(at, at) };
}
