import { commit, none, text } from './reviewText';
import { escapeHtml } from './webviewHtml';

/**
 * The two ways out of a row to its code — the file at the revision the reviewers read, and the file
 * as it is now — as the row renders them.
 *
 * <p>Two buttons, not one, because they answer two different questions (`PLAN_the_review_page_can_be_read.md`,
 * *The revision rule*). `head_sha` is orphaned 55.7 % of the time and 99.6 % of orphaned blobs still
 * read, so the revision a person wants is usually unreachable as a ref and almost always readable as
 * an object — that is *Open at &lt;sha&gt;*. The working-tree file is where a fix would be made, and
 * it is the one that can mislead, so it is labelled CURRENT and never presented as "the" file.</p>
 *
 * <p>Pure, and free of `node:` and `vscode`, like every module the page bundle is built from — and
 * free of the page too: the four fields it reads off a pair are {@link RevisionRow}, which a
 * `ReviewPair` satisfies structurally, so the edge runs one way (`importCycles.test.mjs`).</p>
 *
 * <p>What a row SAYS is decided by the panel, from what it remembers ({@link RevisionState}); this
 * module only renders it. The page paints neither answer itself — the host opens an editor and posts
 * what the row should now say.</p>
 */

/** The four things about a pair the revision actions need — a `ReviewPair` is one, structurally. */
export interface RevisionRow {
  readonly findingId: number;
  /** The checkout the session recorded. Empty when the server did not say. */
  readonly repoPath: string;
  /** The commit the reviewers read. */
  readonly headSha: string;
  /** The finding's path at that commit, relative to `repoPath`. */
  readonly file: string;
}

/** What a row can offer about reaching its code, as the panel decided it from what it remembers. */
export interface RevisionState {
  /** Whether *Open at &lt;sha&gt;* is offered at all. */
  readonly offered: boolean;
  /** What the row says beside the revision action — why it is not offered, that it will check first, or nothing. */
  readonly note: string;
  /** What the row says beside the current-file action — why the last press could not open it, or nothing. */
  readonly currentNote: string;
}

/**
 * Nothing is known yet: the action is offered and says it will check first.
 *
 * <p>The middle path the plan round settled on: no probe per row at paint (200 rows would be 200
 * processes for a page a person may never scroll), and no offer derived from nothing either. The
 * first press in a repository is the probe, and what it learns is remembered for every row of that
 * repository.</p>
 */
export const UNPROBED: RevisionState = {
  offered: true,
  note: 'not checked yet: the first press checks that the repository still answers',
  currentNote: '',
};

/**
 * The row while its press is out: the action is not offered again and the row says why.
 *
 * <p>The first press can wait through a server launch and several git reads, and the row used to sit
 * on 'not checked yet' beside an enabled button the whole time — so a person could not tell whether
 * it had registered, and pressing again was the reasonable thing to do. CLAUDE.md §8 asks a
 * status-changing action to show its real state while it runs; this is that state, and every ending
 * replaces it. (Code round, codex.)</p>
 */
export const WORKING: RevisionState = {
  offered: false,
  note: 'reading it out of git\u2026',
  currentNote: '',
};

/**
 * What a row can offer about checking its commit OUT — a separate state from {@link RevisionState}
 * on purpose.
 *
 * <p>The two are not the same fact and do not move together: reading a file out of git is instant
 * and cannot fail for want of disk, while a checkout takes a minute, can be refused by a cap, and
 * can be in flight in another press. Folding them into one record would mean every field asking
 * which of the two it is about.</p>
 */
export interface TreeState {
  /** Whether *Check out &lt;sha&gt;* is offered. */
  readonly offered: boolean;
  /** What the row says beside it — a refusal, a note about submodules, or nothing. */
  readonly note: string;
}

/** Nothing has been pressed: the action is offered and says nothing. */
export const TREE_READY: TreeState = { offered: true, note: '' };

/**
 * The press is out. A checkout of a large repository plus its submodules is a minute or more, which
 * is exactly the window in which a person presses a second time — CLAUDE.md §8, and the reason the
 * state is said before the first await rather than after the answer.
 */
export const TREE_WORKING: TreeState = {
  offered: false,
  note: 'checking the commit out… the first time also fetches the submodules',
};

/** A sentence beside an action, quiet, so the buttons stay the thing a person reads first. */
const why = (said: string): string => (said.length > 0 ? `<span class="why">${escapeHtml(said)}</span>` : '');

/** The row's actions, as markup for its `data-revision` container. */
export function revisionActions(row: RevisionRow, state: RevisionState, tree: TreeState = TREE_READY): string {
  const id = escapeHtml(String(row.findingId));
  if (text(row.file).length === 0) {
    return none('no file recorded, so there is nothing to open');
  }
  const sha = text(row.headSha);
  const at = state.offered && sha.length > 0
    ? `<button type="button" class="quiet" data-open-at="${id}" title="The file as it was at the commit the reviewers read, read-only, straight out of git — even when no branch reaches that commit any more.">Open at ${commit(sha)}</button>`
    : '';
  const atNote = sha.length > 0 ? state.note : 'an unrecorded commit, so nothing can be opened at it';
  const current = text(row.repoPath).length > 0
    ? `<button type="button" class="quiet" data-open-current="${id}" title="The file as it is in the working tree now — CURRENT, which may no longer be the code the reviewers read. This is where a fix would be made.">Open CURRENT (may differ)</button>`
    : none('no checkout recorded, so the current file cannot be found');

  return `${at}${why(atNote)}<span class="sep" aria-hidden="true">·</span>${current}${why(state.currentNote)}`
    + checkout(id, sha, row, tree);
}

/**
 * The third way out of a row: the WHOLE repository at that commit, in a window of its own.
 *
 * <p>It is last and it says what it costs, because the other two are instant and this one is not —
 * a person reaching for "show me that line" should meet the cheap action first. What it buys is the
 * code AROUND the finding: imports resolved, go-to-definition, find-references, none of which a
 * single read-only document can have.</p>
 */
function checkout(id: string, sha: string, row: RevisionRow, tree: TreeState): string {
  if (sha.length === 0 || text(row.repoPath).length === 0) {
    return '';
  }
  const button = tree.offered
    ? `<button type="button" class="quiet" data-open-tree="${id}" title="Check the whole repository out at this commit, into a folder of its own, and open it in a NEW window — the review page stays where it is. Slower than the other two: it is a real checkout, and the first one also fetches the submodules.">Check out ${commit(sha)} in a new window</button>`
    : '';

  return `<span class="sep" aria-hidden="true">·</span>${button}${why(tree.note)}`;
}
