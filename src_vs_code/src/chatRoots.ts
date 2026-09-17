import * as os from 'node:os';
import * as vscode from 'vscode';
import { ConversationSource } from './chatStore';
import { Thread } from './chatThread';
import { filedUnder, knownFolder } from './chatSource';
import { foldersToSearch } from './claudeSessions';

/**
 * Where a conversation is looked for, and which root it is filed under.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. Every folder this window has open is asked at once,
 * never the first that answers: a workspace with two roots, each holding a session by this tab's
 * name, would otherwise be shown whichever VS Code happened to list first.</p>
 *
 * <p>Both directions of the path-to-uri conversion live here, because they are one fact spelled two
 * ways and a reader who finds one wants the other beside it.</p>
 */

/**
 * One question asked of every folder this window has open, at once.
 *
 * <p>EVERY folder, never the first that answers: a workspace with two roots, each holding a session
 * by this tab's name, would otherwise be shown whichever VS Code happened to list first. What to do
 * with the answers is `oneAnswerFrom`'s to decide.</p>
 */
export async function everyFolder<T>(ask: (folder: string, caseBlind: boolean) => Promise<T>): Promise<readonly T[]> {
  const caseBlind = NAMES_ARE_CASE_BLIND;

  return await Promise.all(whereToLook().map((folder) => ask(folder, caseBlind)));
}

/**
 * The folders a session may be in — the workspace's, or the home directory when it has none.
 *
 * <p>`foldersToSearch` is where the decision lives, in the module with no `vscode` in it, because a
 * decision inside this file is one no unit test can reach — and this one was wrong in two readers at
 * once until an operator with no folder open found it.</p>
 */
/**
 * Whether this filesystem treats two spellings of one name as the same name.
 *
 * <p>Windows and macOS do; Linux does not. It was already the rule the session search used, in two
 * places, and it is now also the rule paths are COMPARED by — folding case on a case-sensitive
 * filesystem would make /work/App and /work/app one folder, picking the wrong root for a conversation
 * and applying a rename to an unrelated one. One constant, so the answers cannot drift apart.
 * (CodeRabbit, on the pull request.)</p>
 */
export const NAMES_ARE_CASE_BLIND = process.platform === 'win32' || process.platform === 'darwin';

export function whereToLook(): readonly string[] {
  return foldersToSearch(
    (vscode.workspace.workspaceFolders ?? []).map((folder) => folder.uri.fsPath),
    os.homedir(),
  );
}

/**
 * The workspace a conversation is FILED under in the store: the first folder `whereToLook` gives,
 * so a conversation is filed where its Claude session is; empty for a window with none at all.
 *
 * <p>ONE function, and exported, because three writers must agree on it or a picker filtered on
 * this workspace shows two of the three: the dual write here, the migration of the memento, and
 * the serializer's memento fallback. Story C1 is what teaches a record which root it really belongs
 * to; until then every writer gives this answer, and the boundary is named in that story's row.</p>
 */
export function conversationWorkspace(): string {
  return whereToLook()[0] ?? '';
}

/**
 * The root a conversation with THIS source is filed under — the host's half of {@link filedUnder}.
 *
 * <p>A file says where it is; a Claude session does not, so its folder is carried from the search
 * that found it and this is not the path that files one (see {@link pinSession}). Anything else
 * falls back to the first root, which is what every record had before story C1.</p>
 */
export function filedFor(source: ConversationSource): string {
  return filedUnder(knownFolder(source, fsPathOf), whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND);
}

/**
 * A conversation's durable origin, as ONE value: what it was opened from and where that is filed.
 *
 * <p>The two are one fact written in two fields, and nothing in the types made them move together —
 * a later repair that set `source` and forgot `workspace` would leave *go to* matching by the new
 * origin while the picker went on filtering by the old root. Every path that establishes an origin
 * goes through this, so the pair cannot be set by halves. (codex, the code round.)</p>
 */
export function originOf(source: ConversationSource): { readonly source: ConversationSource; readonly workspace: string } {
  return { source, workspace: filedFor(source) };
}

/** Give a live conversation a new origin — both fields, from the one value. */
export function reorigin(thread: Thread, source: ConversationSource): void {
  const origin = originOf(source);
  thread.source = origin.source;
  thread.workspace = origin.workspace;
}

/** A uri as a filesystem path, or empty for one that names no file. The host's spelling, so rules need none. */
export function fsPathOf(uri: string): string {
  try {
    const parsed = vscode.Uri.parse(uri, true);

    return parsed.scheme === 'file' ? parsed.fsPath : '';
  } catch {
    // A source a person could not have produced, or one from a build that spelled them differently.
    // It names no file here, which is the honest answer and not a failure — but it is SAID, because
    // nothing else would ever mention it and the conversation quietly files under the fallback root.
    console.warn(`ConnectOtherAIs: a conversation names a source this build cannot read as a uri: ${uri}`);

    return '';
  }
}

/** A filesystem path as a uri, the way a record spells one. The host's, so `chatSource.ts` needs none. */
export const asUri = (path: string): string => vscode.Uri.file(path).toString();
