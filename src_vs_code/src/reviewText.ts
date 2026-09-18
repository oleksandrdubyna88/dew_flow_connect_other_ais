import { escapeHtml } from './webviewHtml';

/**
 * The three ways the review page says a small thing — a trimmed column, an absence, a commit.
 *
 * <p>They were private to `bugzReviewPage.ts` until the un-anonymised view (story 2.3) needed the
 * same three from a module of its own, and the page had reached the 800-line ceiling the family's
 * coding-style rule sets. Extracted rather than copied, per the reuse rule; pure, and free of `node:`
 * and `vscode`, like everything the page bundle is built from.</p>
 */

/** A column that should be text, trimmed — and empty rather than a throw when it is not text at all. */
export const text = (value: unknown): string => (typeof value === 'string' ? value.trim() : '');

/** Nothing recorded, said as such: never an invented cause, never a guessed commit. */
export const none = (what: string): string => `<span class="none">${escapeHtml(what)}</span>`;

/**
 * A commit, abbreviated the way git abbreviates one — as TEXT.
 *
 * <p>Not a link. A link promises "open the file at this revision", and whether that promise can be
 * kept is epic 3's revision rule (`head_sha` is orphaned 55.7 % of the time, measured, while 99.6 %
 * of orphaned blobs still read); a hash shown as text promises only what it is. Empty is said,
 * never guessed.</p>
 */
export const commit = (sha: string): string => {
  const said = text(sha);

  return said.length > 0
    ? `<code class="sha">${escapeHtml(said.slice(0, 7))}</code>`
    : none('an unrecorded commit');
};
