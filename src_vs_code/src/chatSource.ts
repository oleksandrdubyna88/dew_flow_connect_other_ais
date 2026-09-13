import { ConversationSource } from './chatStore';

/**
 * Which conversation belongs to which tab, and which workspace root it is filed under — decided
 * without a host.
 *
 * <p>Epic A gave a record a `source` field and a `workspace` field; nothing has ever written a real
 * value into either. `source` was always `none`, and `workspace` was always the FIRST root the window
 * had open. This module holds the two rules that fix both, and it is pure for the reason every
 * decision in this feature is: a rule about which conversation is yours, buried inside a `vscode`
 * event handler, is a rule no test can reach.</p>
 *
 * <h2>The workspace is CAPTURED, never derived on read</h2>
 *
 * <p>The first draft of this story said the workspace would be a pure function of the source and the
 * roots, worked out whenever anybody asked. Three reviewers refused it independently and they were
 * right: a Claude conversation's source is a session UUID, and a UUID names no directory. There is
 * nothing in it to match against a list of roots, so such a function would fall back to the first
 * root every single time — which is precisely the defect this story exists to remove, rebuilt inside
 * its own repair.</p>
 *
 * <p>So the root is worked out ONCE, at the moment the source is established, from something that
 * genuinely knows it: the file's own path, or the folder the Claude session was found in. It is
 * written onto the record and read back verbatim. {@link rootOf} is that one-time derivation.</p>
 *
 * <h2>What is followed, and what deliberately is not</h2>
 *
 * <p>A file that is MOVED is followed: `onDidRenameFiles` carries an explicit old-to-new mapping, so
 * there is no guessing, and {@link movedTo} is the rule. A file moved into another root changes two
 * facts at once — where it is and which project it belongs to — so the caller recomputes the root
 * from the new path and writes both in one swap. Writing the URI alone would file a conversation
 * whose source is under root B under root A, invisible in exactly the scope the person is looking
 * at.</p>
 *
 * <p>An UNTITLED buffer that is saved is NOT followed, and that is a decision rather than an
 * omission. `onDidSaveTextDocument` hands over the saved document and no previous URI; saving an
 * untitled buffer closes that document and opens a different one, so nothing links the two. A
 * listener built on it would either do nothing or attach a conversation to the wrong file, and this
 * repository has no extension-host harness to prove which. The conversation keeps its `untitled:`
 * source and is found in the picker, which is already the rule for a source that no longer names
 * anything. (gemini, the plan round.)</p>
 */

/**
 * A Claude session's id, read off the file that holds it — the basename without its extension.
 *
 * <p><b>The UUID and never the path.</b> A stored home-directory path was refused earlier in this
 * plan, and rightly: it names a machine and a person, it is wrong the moment a profile moves, and it
 * is of no use to any window but the one that wrote it. The id names the conversation itself, which
 * is what a tab has to be matched against — and Claude refines a session's TITLE as it goes, while a
 * file does not move.</p>
 *
 * <p>Empty for anything that is not one of those files, so a caller that is handed a directory, an
 * empty string or a name of another shape writes no source rather than a wrong one.</p>
 */
export function sessionIdOf(file: string): string {
  const name = file.split(/[\\/]/u).pop() ?? '';
  const id = name.endsWith('.jsonl') ? name.slice(0, -'.jsonl'.length) : '';

  // AND IT MUST LOOK LIKE ONE. Any other `.jsonl` in that directory — a `notes.jsonl`, a file a
  // future version of Claude writes beside the sessions — would otherwise be stored as a source, and
  // stored sources are not repaired: the restore path re-pins only a conversation that has NONE, so a
  // wrong one is permanent. An id that does not look like a session is no id. (codex, the code round.)
  return SESSION_ID.test(id) ? id : '';
}

/** What Claude names a session file: a UUID, and this build stores nothing else as one. */
const SESSION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;

/**
 * The folder a source itself names, or empty when it names none — exhaustive over the kinds.
 *
 * <p>A `file` knows where it is; a `claude` session is a UUID and knows nothing, so its folder is
 * carried from the search that found it and this answers empty for it on purpose; `none` has nothing
 * to say. It is HERE rather than as a ternary in the host because a fourth kind added for a later
 * story would otherwise slip through that ternary's else branch and be filed under the first root in
 * silence — matched for navigation and wrong in the picker. A `never` makes it a compile error
 * instead. (codex, the code round.)</p>
 *
 * @param fsPath the host's uri-to-path spelling; this module has none of its own
 */
export function knownFolder(source: ConversationSource, fsPath: (uri: string) => string): string {
  switch (source.kind) {
    case 'file':
      return fsPath(source.uri);
    case 'claude':
    case 'none':
      return '';
    default: {
      const unhandled: never = source;

      throw new Error(
        `a conversation source this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the kinds it may be are file, claude and none',
      );
    }
  }
}

/** One rename as the editor reports it — old path and new path, both as filesystem paths. */
export interface Moved {
  readonly from: string;
  readonly to: string;
}

/**
 * The workspace root a path belongs to: the LONGEST root that contains it, or empty for a path under
 * none of them.
 *
 * <p>Longest rather than first, because roots nest. A window open on both `D:\rsd` and
 * `D:\rsd\coai` would otherwise file every conversation in the inner project under the outer one,
 * which is the same misfiling this story is removing, one level down.</p>
 *
 * <p>The comparison is on segment boundaries, so `D:\rsd\coai-old` is not inside `D:\rsd\coai`, and
 * it is case-blind on both separators because Windows paths reach here spelled either way and from
 * two programs — the editor's and Claude's.</p>
 */
export function rootOf(path: string, roots: readonly string[]): string {
  const inside = roots.filter((root) => root.length > 0 && contains(root, path));

  return inside.reduce((longest, root) => (root.length > longest.length ? root : longest), '');
}

/** Whether `path` is the folder `root` itself or something under it. */
function contains(root: string, path: string): boolean {
  const one = normal(root);
  const other = normal(path);

  return other === one || other.startsWith(one.endsWith('/') ? one : `${one}/`);
}

/**
 * A path as it compares: one kind of separator, no trailing one, and case-blind.
 *
 * <p>`toLowerCase`, NOT `toLocaleLowerCase`. A path comparison has no business being
 * locale-sensitive: under a Turkish locale `I` lowercases to a dotless `ı`, so two spellings of
 * one ASCII path stop matching and the conversation is filed under no root at all. (gemini, the
 * code round.)</p>
 */
const normal = (path: string): string => path.replace(/[\\/]+/gu, '/').replace(/\/+$/u, '').toLowerCase();

/**
 * Where a conversation is filed, given what its source knows and what the window has open.
 *
 * @param known the folder the source itself names — the file's own directory, or the root a Claude
 *   session was found in; empty when the source knows nothing, which is every record written before
 *   this story
 * @param fallback the answer for a source that names nothing: the first root, which is what every
 *   record had before this story and is therefore not a change for any of them
 */
export function filedUnder(known: string, roots: readonly string[], fallback: string): string {
  const root = rootOf(known, roots);

  return root.length > 0 ? root : fallback;
}

/**
 * The renames, with each old path put into comparable form ONCE.
 *
 * <p>{@link movedTo} is asked about every conversation in the store, and it used to re-normalise
 * every rename's old path for each of them — a folder refactor reporting fifty moves against five
 * hundred conversations normalised the same fifty strings five hundred times. The work belongs
 * outside the loop, and saying so in the type keeps it there. (gemini, the code round.)</p>
 */
export interface Prepared {
  readonly move: Moved;
  /** `move.from`, normalised for comparison. */
  readonly from: string;
}

/** Put the renames into comparable form, once, before anything is asked about them. */
export const prepareMoves = (renames: readonly Moved[]): readonly Prepared[] =>
  renames.map((move) => ({ move, from: normal(move.from) }));

/**
 * Where a renamed file went, for a conversation whose source is a `file:` URI — or empty when this
 * rename says nothing about it.
 *
 * <p>A FOLDER rename moves everything under it, and the editor reports that as one entry rather than
 * one per file, so a source inside a renamed directory is rewritten by replacing the prefix. The
 * longest matching rename wins, for the same reason the longest root does: a move of `src` and a move
 * of `src/chat` in one gesture are both true of a file in the second, and only the closer one has the
 * right new path.</p>
 *
 * @param uri the conversation's source URI, as `Uri.toString()` spelled it
 * @param renames what the editor said moved, as filesystem paths
 * @param toUri how to spell a filesystem path back as a URI — the host's, so this module needs none
 */
export function movedTo(
  uri: string,
  renames: readonly Prepared[],
  toUri: (path: string) => string,
  fsPath: (uri: string) => string,
): string {
  const was = fsPath(uri);
  if (was.length === 0) {
    return '';
  }
  const here = normal(was);
  const hit = renames
    .filter((one) => here === one.from || here.startsWith(`${one.from}/`))
    .reduce<Prepared | undefined>((closest, one) => (closest === undefined || one.from.length > closest.from.length ? one : closest), undefined);
  if (hit === undefined) {
    return '';
  }

  return toUri(hit.move.to + was.slice(hit.move.from.length));
}

/**
 * Whether this record is one a rename should rewrite: a live `file:` conversation, and nothing else.
 *
 * <p>A Claude conversation is named by a session UUID, which no file move can change. An `untitled:`
 * one names a buffer that has no path to move. A record with no source has nothing to follow.</p>
 */
export const followable = (source: ConversationSource): source is { readonly kind: 'file'; readonly uri: string } =>
  source.kind === 'file' && source.uri.startsWith('file:');
