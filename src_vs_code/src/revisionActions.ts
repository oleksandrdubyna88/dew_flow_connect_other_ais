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

/** A sentence beside an action, quiet, so the buttons stay the thing a person reads first. */
const why = (said: string): string => (said.length > 0 ? `<span class="why">${escapeHtml(said)}</span>` : '');

/** The row's actions, as markup for its `data-revision` container. */
export function revisionActions(row: RevisionRow, state: RevisionState): string {
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

  return `${at}${why(atNote)}<span class="sep" aria-hidden="true">·</span>${current}${why(state.currentNote)}`;
}
