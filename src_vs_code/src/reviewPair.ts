/**
 * One row of the review page, as the server hands it over.
 *
 * <p>Its own module, and that is the whole point: this is the review MODEL, and it used to be
 * declared inside `bugzReviewPage.ts` — the module whose job is to turn rows into HTML. Ten modules
 * import the type, so loading the calls STATE also pulled in the page renderer and everything it
 * needs to make markup. A layering inversion that is invisible until it bites: an alternate view over
 * the same rows would have had to import the renderer to learn what a row is.</p>
 *
 * <p>Named by a code-round reviewer on `callsPanel.ts` and rejected there on purpose — doing it for
 * the newer of two siblings would have left one panel holding two row models. Doing it for both is
 * the move that was always correct. (Story 3.3's second code round, codex; the operator's call.)</p>
 */

/**
 * One pair as the server hands it over.
 *
 * <p>The seven fields after `title` arrived with story 2.1. They are OPTIONAL on the wire —
 * `roundsDbRead.pairOf` fills each from a server too old to send it, with empty text and a line of
 * 0 — and REQUIRED here, so that the page cannot forget to decide what an empty one looks like.</p>
 */
export interface ReviewPair {
  readonly findingId: number;
  readonly symbolName: string;
  readonly language: string;
  readonly skeletonBefore: string;
  readonly skeletonAfter: string;
  /** -1 nobody has looked, 0 dropped, 1 kept. */
  readonly keep: number;
  readonly severity: string;
  readonly category: string;
  readonly title: string;
  /** The checkout the round reviewed, as the session recorded it. Empty when the server did not say. */
  readonly repoPath: string;
  /** The commit the reviewers read — the BEFORE skeleton is the method at this commit. */
  readonly headSha: string;
  /** The commit the fix was found in — the AFTER skeleton is the method at this one. */
  readonly fixSha: string;
  /** The finding's path at `headSha`, relative to `repoPath`. */
  readonly file: string;
  /** The finding's line at `headSha`; 0 when none was recorded. */
  readonly line: number;
  /** The reviewers' cause, verbatim — a model's prose about somebody's code, escaped on the way in. */
  readonly why: string;
  /** The reviewers' proposed fix, verbatim. */
  readonly fix: string;
}
