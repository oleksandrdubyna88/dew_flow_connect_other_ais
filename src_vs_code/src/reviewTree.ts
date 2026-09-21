/**
 * The review tree: a whole checkout of the commit the reviewers read, for a person to open.
 *
 * <p><b>Why this exists beside {@link ./openAtRevision}.</b> Story 3.1 opens ONE file out of the
 * object database, read-only. That answers "what did this line say"; it cannot answer "what called
 * it", because a `coai-revision:` document has no project behind it — no imports resolved, no
 * go-to-definition, no find-references. For the 55.7 % of pairs whose commit is orphaned there is no
 * other way to see the code AROUND the finding as it was.</p>
 *
 * <p><b>Nothing here spawns git.</b> The extension has not, since epic 2 established it, and this
 * story does not start: `--tree-at` is a server mode, and what arrives here is its answer.</p>
 */

/** What the page says when the server does not know the mode at all — exit 64 and only that. */
export const TOO_OLD_FOR_A_TREE =
  'this server is too old to check a commit out for reading; update the extension’s server binary';

/** One tree this machine already holds, as a cap refusal names it. */
export interface HeldTree {
  readonly repository: string;
  readonly sha: string;
  readonly path: string;
  readonly created: string;
}

/** The server's answer to `--tree-at`. */
export interface ReviewTreeAnswer {
  readonly findingId: number;
  readonly sha: string;
  /** The tree to open, or empty — except for a dirty leftover, which is named so it can be found. */
  readonly path: string;
  readonly repository: string;
  readonly reused: boolean;
  readonly reason: string;
  readonly emptyMounts: readonly string[];
  readonly trees: readonly HeldTree[];
}

/**
 * The read, on {@link ./openAtRevision.FileAtRead}'s shape: `ok: false` is the REQUEST having
 * failed, never the repository having an answer a person will not like. A commit that is gone, a cap
 * that is full and another press already building the tree are all `ok: true` with a reason.
 */
export type TreeRead =
  | { readonly ok: true; readonly tree: ReviewTreeAnswer }
  | { readonly ok: false; readonly tooOld: boolean; readonly why: string };

/**
 * Every reason word the server can answer, spelled once on this side.
 *
 * <p>The live-contract suite asserts this union against the C# constants, so a word added on one
 * side and not the other is a red test rather than a row that says nothing.</p>
 */
export const TREE_REASONS = [
  'pair_not_found',
  'repo_path_missing',
  'git_failed',
  'commit_unreachable',
  'budget',
  'in_progress',
  'incomplete_and_dirty',
] as const;

/**
 * What a person is told, and it is never the reason word.
 *
 * <p>Each sentence ends where the person's next move begins: three of these are worth pressing
 * again, two are not, and one asks them to look at a directory. A row that printed
 * `incomplete_and_dirty` would be telling them our vocabulary instead.</p>
 */
export function treeSentence(tree: ReviewTreeAnswer): string {
  if (tree.reason === 'budget') {
    return `this machine already holds ${tree.trees.length} review checkouts, which is the limit — remove one to make room`;
  }
  if (tree.reason === 'in_progress') {
    return 'another press is preparing this checkout — try again in a moment';
  }
  if (tree.reason === 'incomplete_and_dirty') {
    return `a half-made checkout at ${tree.path} has changes in it, so it was left alone — look at it, then remove it by hand`;
  }

  return SENTENCES[tree.reason] ?? 'the checkout could not be made';
}

const SENTENCES: Readonly<Record<string, string>> = {
  pair_not_found: 'this row is not in the database any more',
  repo_path_missing: 'the checkout this session recorded is gone',
  git_failed: 'git did not answer — nothing was learned, so it is worth pressing again',
  commit_unreachable: 'that commit is not in the repository any more',
};

/**
 * The note about submodule mounts that stayed empty — said, rather than discovered.
 *
 * <p>There is deliberately no "should this be offered again" rule beside it. Every refusal here is
 * one a person can act on and then retry — a cap they can make room in, a press that will finish, a
 * repository they can restore — so the button stays and the note says what to do. A rule that
 * disabled it for the permanent-looking cases would be guessing which of those a person is about to
 * fix.</p>
 */
export function mountsNote(tree: ReviewTreeAnswer): string {
  return tree.emptyMounts.length === 0
    ? ''
    : `submodules not filled: ${tree.emptyMounts.join(', ')} — navigation into them will find nothing`;
}
