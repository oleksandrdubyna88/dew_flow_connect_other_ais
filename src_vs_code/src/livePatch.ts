/**
 * The live-patch channel: one owner for the three facts that have to agree.
 *
 * <p>A host-side panel decides what one row says, renders it, and posts it; the page finds that row's
 * container and puts the markup in. That is three facts — the message type, the container attribute,
 * and the item shape — and before this module they were written twice each, once in the generator and
 * once in the page script, with nothing tying the two together. A rename was a RUNTIME failure: the
 * patch found nothing, wrote nowhere, and the row went on showing a stale answer with no error
 * anywhere. (Story 3.3's code round, codex, on both panels.)</p>
 *
 * <p><b>The page script's selector is interpolated from here</b>, not typed out a second time — that
 * is the whole mechanism. `bugzReviewPage.ts` builds the script as a template literal, so the
 * attribute reaches the browser from the same constant the generator used, and renaming it changes
 * both sides or neither.</p>
 *
 * <p><b>What this deliberately does NOT do</b> is replace `{id, html}` with a typed payload rendered
 * by the page. That was the reviewer's literal suggestion and it is the wrong trade here: the markup
 * is built by `callsBlock.ts` and `revisionActions.ts`, typed modules with tests, and moving it into
 * the page would mean rewriting both as hand-written ES5 inside a template literal. The defect was
 * never the string — it was that nothing owned the ADDRESS the string is delivered to.</p>
 */

/** One channel from a host-side panel to a row's container. */
export interface LivePatchChannel {
  /** The `type` on the posted message, matched by the page's dispatch. */
  readonly message: string;

  /** The data attribute naming a row's container, written by the generator and queried by the page. */
  readonly attribute: string;
}

/** Who calls this method and what it calls (story 3.3). */
export const CALLS: LivePatchChannel = { message: 'calls', attribute: 'data-calls-for' };

/** How to reach a row's code, at its revision or as it is now (story 3.1). */
export const REVISIONS: LivePatchChannel = { message: 'revisions', attribute: 'data-revision' };

/**
 * One row's answer, rendered.
 *
 * <p>Both panels publish this shape. It was two structurally identical anonymous types before, which
 * is how a page can go on compiling while it patches nothing.</p>
 */
export interface LivePatchItem {
  readonly id: number;
  readonly html: string;
}

/** The container attribute as the page WRITES it, escaped for markup. */
export function containerAttribute(channel: LivePatchChannel, findingId: number): string {
  return `${channel.attribute}="${findingId}"`;
}

/** The container selector as the page SCRIPT queries it, for a row id it holds at runtime. */
export function containerQuery(channel: LivePatchChannel, idExpression: string): string {
  return `'[${channel.attribute}="' + ${idExpression} + '"]'`;
}
