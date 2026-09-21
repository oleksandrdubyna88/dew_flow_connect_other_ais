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
  /** The checkout the ROW named, echoed back so the reader can prove the answer is about this row. */
  readonly repoPath: string;
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
    return `this machine already holds ${tree.trees.length} review checkouts, which is the limit — remove one to make room. ${held(tree.trees)}`;
  }
  if (tree.reason === 'in_progress') {
    // The checkout is running in the SERVER, so it survives this window being reloaded or this page
    // being redrawn — which is why the answer is "still going" and not "start again". The truth is
    // the tree on disk and the record beside it; this side asks on every press and caches nothing.
    return 'this commit is already being checked out — that run continues even if this page is reloaded, so press again in a moment to open it';
  }
  if (tree.reason === 'incomplete_and_dirty') {
    return `a half-made checkout at ${tree.path} has changes in it, so it was left alone — look at it, then remove it by hand`;
  }

  return SENTENCES[tree.reason] ?? 'the checkout could not be made';
}

/**
 * The checkouts that are using up the cap, named.
 *
 * <p>The server sends every one of them with its repository, commit, path and creation time, and the
 * refusal used to print only how MANY there were — which tells a person they must remove something
 * without telling them what there is to remove. Ten paths is a lot of text for a row, so each is one
 * line: the commit, the day, and the path they can look at.</p>
 */
function held(trees: readonly HeldTree[]): string {
  return trees.length === 0
    ? ''
    : `they are: ${trees.map((t) => `${t.sha.slice(0, 7)} (${t.created.slice(0, 10)}) at ${t.path}`).join('; ')}`;
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

// --------------------------------------------------------------------------------------------
// Story 3.2b: what this machine holds, and giving one back.
// --------------------------------------------------------------------------------------------

/** What the server says when it does not know `--trees` / `--tree-remove` — exit 64, and only that. */
export const TOO_OLD_FOR_TREES =
  'this server is too old to list or remove review checkouts; update the extension’s server binary';

/** One review tree, as the list shows it. */
export interface ListedTree {
  readonly name: string;
  readonly repository: string;
  readonly repoPath: string;
  readonly sha: string;
  readonly path: string;
  readonly created: string;
  readonly state: string;
}

/** Everything this machine holds. */
export interface TreesAnswer {
  readonly root: string;
  readonly trees: readonly ListedTree[];
  readonly reason: string;
}

/** What happened to a tree somebody asked to give back. */
export interface RemovalAnswer {
  readonly name: string;
  readonly reason: string;
  readonly inTheWay: readonly string[];
  readonly ignored: number;
  readonly ignoredSample: readonly string[];
}

export type TreesRead =
  | { readonly ok: true; readonly answer: TreesAnswer }
  | { readonly ok: false; readonly tooOld: boolean; readonly why: string };

export type RemovalRead =
  | { readonly ok: true; readonly answer: RemovalAnswer }
  | { readonly ok: false; readonly tooOld: boolean; readonly why: string };

/**
 * Every word `--tree-remove` can answer, spelled once on this side and asserted against the C#
 * constants by the live-contract suite.
 */
export const REMOVAL_REASONS = [
  'removed',
  'forgotten',
  'dirty',
  'has_ignored',
  'not_ours',
  'git_failed',
  'unregistered',
  'unreachable',
] as const;

/** Every state a listed tree can be in, likewise. */
export const TREE_STATES = ['ready', 'incomplete', 'vanished', 'unregistered', 'unreachable'] as const;

/** How a tree reads in a list: what it is, and whether it can be opened. */
export function treeLine(tree: ListedTree): string {
  const where = lastPart(tree.repository.replace(/[/\\]\.git$/u, '')) || lastPart(tree.repoPath);
  const day = tree.created.slice(0, 10);

  return `${where || 'an unrecorded repository'} @ ${tree.sha.slice(0, 7) || '???????'}${day ? ` · ${day}` : ''} · ${STATES[tree.state] ?? tree.state}`;
}

const STATES: Readonly<Record<string, string>> = {
  ready: 'ready',
  incomplete: 'never finished being made',
  vanished: 'its folder is gone',
  unregistered: 'git no longer knows it',
  unreachable: 'the checkout it came from is gone',
};

/** Only a tree that is really there and really a worktree can be opened. */
export function canOpen(tree: ListedTree): boolean {
  return tree.state === 'ready';
}

/**
 * What a person is told after asking for a tree back — and it is never the reason word.
 *
 * <p>Each sentence ends where their next move begins, which for the two refusals means naming what
 * is in the way rather than counting it: a refusal nobody can act on is not a refusal.</p>
 */
export function removalSentence(answer: RemovalAnswer): string {
  if (answer.reason === 'dirty') {
    return `it holds work of yours, so nothing was removed: ${answer.inTheWay.join(', ')}`;
  }
  if (answer.reason === 'removed') {
    return answer.ignored > 0
      ? `removed, and ${answer.ignored} ignored ${answer.ignored === 1 ? 'file' : 'files'} went with it`
      : 'removed';
  }
  if (answer.reason === 'has_ignored') {
    return `it holds ${answer.ignored} ignored ${answer.ignored === 1 ? 'file' : 'files'} — ${answer.ignoredSample.join(', ')}`;
  }

  return REMOVALS[answer.reason] ?? 'it could not be removed';
}

const REMOVALS: Readonly<Record<string, string>> = {
  forgotten: 'its folder was already gone, so only this product’s record of it was dropped — git still holds a registration, which the next checkout of that commit clears',
  not_ours: 'this product holds no record of that checkout, so it will not touch it',
  git_failed: 'git did not answer — nothing was learned and nothing was touched, so it is worth asking again',
  unregistered: 'git no longer knows this folder as a worktree, so this product cannot tell whose the files in it are — remove it by hand if it is yours',
  unreachable: 'the checkout it was made from is gone, so there is no repository to deregister it from — remove the folder by hand',
};

/** The question asked before ignored files go: the only confirmation this product has. */
export function ignoredAsk(answer: RemovalAnswer): string {
  return `Remove it anyway, including ${answer.ignored} ignored ${answer.ignored === 1 ? 'file' : 'files'}`;
}

function lastPart(path: string): string {
  const parts = path.replace(/[/\\]+$/u, '').split(/[/\\]/u);

  return parts[parts.length - 1] ?? '';
}
