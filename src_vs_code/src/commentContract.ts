/**
 * The two facts about a person's comment that more than the page needs to know (story 4.2 of
 * `PLAN_a_comment_crosses_the_machine_boundary.md`).
 *
 * <p><b>A module of its own, with no markup in it.</b> The process reader (`roundsDbRead.ts`) needs
 * the too-old sentence, and it took it from `reviewComment.ts` — the module that renders the box and
 * carries the page's script — so the boundary that spawns processes depended on the one that draws
 * HTML. A code reviewer named it (codex): a future non-page consumer of the reader would have pulled
 * the page in with it. Both now import from here, and this imports nothing.</p>
 */

/**
 * The most a comment may be, in UTF-16 code units — `.length` here, `string.Length` in C#.
 *
 * <p>Held against `shared/comment-limit.json` by BOTH halves; the page renders it as `maxlength`.</p>
 */
export const COMMENT_MOST_CHARS = 1000;

/** What the panel says when the binary answered 64 to a write that carried words. */
export const TOO_OLD_FOR_COMMENTS =
  'This coai-mcp is too old to keep a comment. Install the newest server from the panel; your '
  + 'comment is still in its box, and nothing was written.';
