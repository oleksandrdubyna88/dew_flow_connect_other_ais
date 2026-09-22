import type { Decision, ReviewPair } from './reviewPair';
import { escapeHtml } from './webviewHtml';

/**
 * What a person writes about a pair on the review page — the box, the sentence beside it, and the
 * little the panel needs to hold a draft (story 4.2 of `PLAN_a_comment_crosses_the_machine_boundary.md`).
 *
 * <p><b>A module of its own</b> rather than more of `bugzReviewPage.ts` and `bugzReviewPanel.ts`,
 * which were 709 and 759 lines against the 800 the lint allows. Everything here is pure: markup
 * from a pair, the words a pair's box should say, and which decisions a press or a pause writes.</p>
 *
 * <p><b>The charset rule is NOT here, and must never be.</b> `CommentRule` in the server's core is the
 * one implementation; a comment it refuses comes back from `--pairs-decide` as 65 with a sentence
 * naming the pair and the code point, which the panel shows. This module holds the NUMBER only, and a
 * structural test pins it against `CommentRule.MostChars`.</p>
 */

/** The most a comment may be, in UTF-16 code units — `.length` here, `string.Length` there. */
export const COMMENT_MOST_CHARS = 1000;

/** Where the counter turns warning-coloured, so a long comment is noticed before it is stopped. */
export const COMMENT_NEAR = 900;

/**
 * The sentence beside every box that has not been sent. The operator's decision of 2026-09-18 is
 * that a comment is public; this is what lets a person choose what to put in it knowingly.
 *
 * <p>"Apart from blank space at either end" because that is what `--pairs-decide` does — it trims,
 * and makes line endings LF — and a notice claiming "exactly as typed" over a trimmed text would be
 * a small lie on the one surface whose whole job is to be exact. (Plan round of 4.2, codex.)</p>
 */
export const LEAVES_THE_MACHINE =
  'Sent with this pair to the corpus server, in public and not anonymised — as you typed it, '
  + 'apart from blank space at either end.';

/** What the panel says when the binary answered 64 to a write that carried words. */
export const TOO_OLD_FOR_COMMENTS =
  'This coai-mcp is too old to keep a comment. Install the newest server from the panel; your '
  + 'comment is still in its box, and nothing was written.';

/** The box, its counter and its sentence, for the detail row of one pair. */
export function commentBlock(pair: ReviewPair, draft: string | undefined): string {
  const id = escapeHtml(String(pair.findingId));
  const text = draft ?? pair.comment;
  const readOnly = pair.sentUtc.length > 0 ? ' readonly' : '';

  return `<div class="comment">
      <label for="comment-${id}">Your comment on this pair</label>
      <textarea id="comment-${id}" data-comment="${id}" maxlength="${COMMENT_MOST_CHARS}" rows="2"${readOnly}>${escapeHtml(text)}</textarea>
      <span class="${counterClass(text)}" data-count="${id}">${text.length} / ${COMMENT_MOST_CHARS}</span>
      <p class="notice" data-notice="${id}">${escapeHtml(noticeFor(pair))}</p>
    </div>`;
}

function counterClass(text: string): string {
  return text.length >= COMMENT_NEAR ? 'count near' : 'count';
}

/**
 * The sentence a pair's box says: where the words go, or where they went, or that they did not.
 *
 * <p>Once the server has acknowledged the pair the box is read-only and says so — a change would
 * never follow it. When the pair landed and its words did not, the server's own reason is shown
 * instead, because "Sent on …" above words that never crossed is the silent loss this story exists
 * to prevent.</p>
 */
export function noticeFor(pair: ReviewPair): string {
  if (pair.sentUtc.length === 0) {
    return LEAVES_THE_MACHINE;
  }

  const on = pair.sentUtc.slice(0, 10);

  return pair.commentLost.length > 0
    ? `Sent on ${on}, but your comment was not stored: ${pair.commentLost}`
    : `Sent on ${on}. A change here will not follow it.`;
}

/**
 * A decision press, as the writes it is: every chosen pair with the keep pressed AND its words.
 *
 * <p>The words are the DRAFT when one is held, else what the store has. Sending the keep alone would
 * leave `--pairs-decide` writing an empty comment over a stored one — a decision erasing a comment,
 * which the one-write rule exists to prevent.</p>
 */
export function decisionsFor(
  ids: readonly number[], keep: number, pairs: readonly ReviewPair[], drafts: ReadonlyMap<number, string>,
): Decision[] {
  const stored = new Map(pairs.map((pair) => [pair.findingId, pair.comment] as const));

  return ids.map((findingId) => ({ findingId, keep, comment: drafts.get(findingId) ?? stored.get(findingId) ?? '' }));
}

/**
 * A pause or a blur in one box, as the one write it is: that pair's CURRENT keep, and the new words.
 *
 * <p>Nothing for a pair this panel does not hold — an id from a page drawn before a recollection —
 * because a keep invented for it would be a decision nobody made.</p>
 */
export function commentWrite(findingId: number, text: string, pairs: readonly ReviewPair[]): Decision[] {
  const pair = pairs.find((one) => one.findingId === findingId);

  return pair === undefined ? [] : [{ findingId, keep: pair.keep, comment: text }];
}

/**
 * The drafts still worth holding once a write has landed.
 *
 * <p>Only a draft that is exactly what was written is let go. A person may have typed on while the
 * write was in the air, and that newer text is still theirs and still unsaved.</p>
 */
export function settled(drafts: ReadonlyMap<number, string>, written: readonly Decision[]): ReadonlyMap<number, string> {
  const landed = new Map(written.map((one) => [one.findingId, one.comment] as const));

  return new Map([...drafts].filter(([id, text]) => landed.get(id) !== text));
}

/**
 * The page's half: count as a person types, and post the words on a pause and when the box is left.
 *
 * <p><b>Not on every keystroke</b> — each post is one `--pairs-decide` process — and <b>not only on
 * `change`</b>, which fires when the box loses focus: text typed and never blurred was lost when the
 * panel closed, which a plan reviewer named (gemini). A pause of a second and a half is a write; the
 * blur flushes whatever is left.</p>
 *
 * <p>ES5 and `var`, like the rest of the page's script, and inserted into it rather than being a
 * script of its own, so it runs under the page's one nonce.</p>
 */
export const COMMENT_SCRIPT = `
  var commentPauses = {};
  function commentFrom(target) {
    var box = target && target.closest ? target.closest('[data-comment]') : null;
    return box && !box.readOnly ? box : null;
  }
  function postComment(box) {
    var id = box.getAttribute('data-comment');
    clearTimeout(commentPauses[id]);
    delete commentPauses[id];
    vscode.postMessage({ type: 'comment', id: Number(id), text: box.value });
  }
  function countComment(box) {
    var counter = document.querySelector('[data-count="' + box.getAttribute('data-comment') + '"]');
    if (!counter) { return; }
    counter.textContent = box.value.length + ' / ${COMMENT_MOST_CHARS}';
    counter.className = box.value.length >= ${COMMENT_NEAR} ? 'count near' : 'count';
  }
  document.addEventListener('input', function (event) {
    var box = commentFrom(event.target);
    if (!box) { return; }
    countComment(box);
    var id = box.getAttribute('data-comment');
    clearTimeout(commentPauses[id]);
    commentPauses[id] = setTimeout(function () { postComment(box); }, 1500);
  });
  document.addEventListener('change', function (event) {
    var box = commentFrom(event.target);
    if (box) { postComment(box); }
  });
`;
