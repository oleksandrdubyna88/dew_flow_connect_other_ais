import { highlight } from './codeHighlight';
import { LineMark, pairDiff } from './lineDiff';
import { commit, text } from './reviewText';
import { escapeHtml } from './webviewHtml';

/**
 * The un-anonymised view's half of a row: the real method as `--real-method` answers it, and the
 * ONE renderer that turns the answer into what the row shows.
 *
 * <p>Its own module for two reasons. The page had reached the 800-line ceiling the coding-style rule
 * sets — `max-lines` in `eslint.config.mjs` enforces it — and this is a unit with a boundary of its
 * own: everything here is about text a person asked to see un-anonymised, and nothing here is
 * reachable from a decision or a send. `realView` is called at paint for a cached row and by the
 * panel for a fetch that has just completed, so the two cannot disagree.</p>
 *
 * <p>Pure, and free of `node:` and `vscode`, like every module the page bundle is built from — and
 * free of the PAGE, too. The page imports this; an import back, even of a type, is the cycle
 * `importCycles.test.mjs` ratchets against, and it went red the first time this file was written
 * with `import type { ReviewPair }` at the top. So the four fields the view reads off a pair are
 * named here as {@link RealMethodRow}, which `ReviewPair` satisfies structurally.</p>
 */

/** The four things about a pair the real view needs — a `ReviewPair` is one, structurally. */
export interface RealMethodRow {
  readonly repoPath: string;
  readonly language: string;
  /** The commit the reviewers read; the BEFORE side is the method there. */
  readonly headSha: string;
  /** The commit the fix was found in; the AFTER side is the method there. */
  readonly fixSha: string;
}

/**
 * One side of the real method — the function's own text as it was at that commit — or why it
 * cannot be shown.
 *
 * <p>The server's `MethodSide`, by the same names. `reason` is empty exactly when `source` is the
 * real text; otherwise it is one of the collector's own words — `commit_unreachable`,
 * `file_not_in_commit`, `symbol_not_resolved`, `symbol_ambiguous`, `symbol_gone`, `git_failed` —
 * and {@link realView} turns each into the sentence a person acts on. They are different facts:
 * a pruned commit, an overload set and a renamed method send somebody to three different places.</p>
 */
export interface MethodSide {
  readonly reason: string;
  readonly source: string;
  /** The innermost type around the function, or empty for a top-level function. Never guessed. */
  readonly className: string;
  readonly kind: string;
  readonly startLine: number;
  readonly endLine: number;
}

/** The real method behind one pair, at both of its commits — what `--real-method` answers. */
export interface RealMethod {
  readonly findingId: number;
  readonly language: string;
  /** The function's name, as the pair stores it. */
  readonly name: string;
  /** A reason that stopped BOTH sides — no such pair, checkout gone, language not read, git failed — or empty. */
  readonly reason: string;
  /** Found by LINE at the commit the reviewers read. */
  readonly before: MethodSide;
  /** Found by NAME at the commit the fix was found in. */
  readonly after: MethodSide;
}

/**
 * What one fetch of the real method came to, as the panel holds it per pair.
 *
 * <p>`tooOld` is the 64 branch: a server that has never heard of `--real-method`. Its sentence says
 * to update the server, not that the method could not be read — the same distinction the send's
 * outcomes draw, because the two send a person to different places.</p>
 */
export type RealRead =
  | { readonly ok: true; readonly method: RealMethod }
  | { readonly ok: false; readonly why: string; readonly tooOld: boolean };

/** What a fetched method renders to: the markup, and whether it REPLACES the skeleton or sits above it. */
export interface RealView {
  /** True when at least one side carries real text and the skeleton steps aside for it. */
  readonly shown: boolean;
  readonly html: string;
}

/**
 * The sentence a person reads when the server has never heard of the view.
 *
 * <p>Exported so the reader and the page say the same thing: `readRealMethod` fills it in for exit
 * code 64, and the page renders it. 64 means "this binary does not have that mode" and nothing
 * else — `.agents/PROJECT.md` reserves it — so the sentence sends somebody to update, not to
 * wonder what happened to their repository.</p>
 */
export const TOO_OLD_FOR_THE_REAL_METHOD =
  'this machine’s coai-mcp is older than the un-anonymised view; update it from the panel';

/** A note in a real-method container: fetching, or why there is nothing to show. */
const realNote = (said: string): string => `<p class="realNote">${escapeHtml(said)}</p>`;

/**
 * Why a whole pair has no real text — the reasons that stop both sides.
 *
 * <p>An unknown word is rendered as itself rather than refused: a retained webview can be older or
 * newer than the server talking to it, and the page's message boundary already ignores an unknown
 * message type for that reason (`asReviewMessage`, and `narrow`'s docblock in the panel).</p>
 */
function wholeReason(method: RealMethod, pair: RealMethodRow): string {
  switch (method.reason) {
    case 'pair_not_found':
      return 'The real method could not be read: this pair is not in the database any more.';
    case 'repo_path_missing':
      return `The real method could not be read: ${pair.repoPath || 'the recorded checkout'} is not a git repository any more.`;
    case 'language_unsupported':
      return `The real method could not be read: ${pair.language || 'this'} is not a language the normaliser reads.`;
    case 'git_failed':
      return `The real method could not be read: git did not answer for ${pair.repoPath || 'the recorded checkout'}.`;
    default:
      return `The real method could not be read: ${method.reason}.`;
  }
}

/** Why ONE side has no real text, naming the commit it is about and the method it looked for. */
function sideReason(side: MethodSide, sha: string, name: string): string {
  const at = text(sha).slice(0, 7) || 'an unrecorded commit';
  switch (side.reason) {
    case 'commit_unreachable':
      return `the commit ${at} is not in the repository any more`;
    case 'file_not_in_commit':
      return `the file was not in commit ${at}`;
    case 'symbol_not_resolved':
      return `the line is inside no named function at ${at}`;
    case 'symbol_ambiguous':
      return `two functions are called ${name} at ${at}, so neither can be shown as this one`;
    case 'symbol_gone':
      return `no function called ${name} is in commit ${at} any more`;
    case 'git_failed':
      return `git did not answer for commit ${at}`;
    default:
      return `not shown: ${side.reason}`;
  }
}

/** Where one shown side sits: its class and name, its kind, its lines, and the commit it is from. */
function sideWho(side: MethodSide, name: string, sha: string): string {
  const owner = side.className.length > 0 ? `<span class="cls">${escapeHtml(side.className)}</span>.` : '';

  return `${owner}${escapeHtml(name)} — ${escapeHtml(side.kind)}, lines ${side.startLine}–${side.endLine} at ${commit(sha)}`;
}

/**
 * One pane of the real view: the real text, coloured and diffed — or the reason there is none. Numbered as
 * the FILE is at that commit, from the side's own first line (issue #488).
 */
function realPane(label: string, side: MethodSide, pair: RealMethodRow, marks: readonly LineMark[], sha: string, name: string): string {
  const body = side.reason.length === 0
    ? highlight(side.source, pair.language, marks, side.startLine > 0 ? side.startLine : 1)
    : `<p class="none">${escapeHtml(sideReason(side, sha, name))}</p>`;

  return `<div class="side">
        <div class="sideName">${label}</div>${body}
      </div>`;
}

/**
 * A fetched method as the row shows it.
 *
 * <p>Both sides real: the two panes, diffed and coloured exactly as the skeletons are — `pairDiff`
 * masks placeholder indices, and on real text there is nothing to mask, but the masking is also
 * what makes the too-big comparison honest, so the same call serves both. One side real: its pane
 * and, opposite it, the sentence for the other. Neither: a note above the skeleton, which stays.
 * A read that failed or a server too old: the same note.</p>
 */
export function realView(pair: RealMethodRow, read: RealRead): RealView {
  if (!read.ok) {
    return { shown: false, html: realNote(read.tooOld ? TOO_OLD_FOR_THE_REAL_METHOD : `The real method could not be read: ${read.why}`) };
  }
  const method = read.method;
  if (method.reason.length > 0) {
    return { shown: false, html: realNote(wholeReason(method, pair)) };
  }
  const before = method.before.reason.length === 0;
  const after = method.after.reason.length === 0;
  if (!before && !after) {
    return {
      shown: false,
      html: realNote(`The real method could not be read: ${sideReason(method.before, pair.headSha, method.name)}; ${sideReason(method.after, pair.fixSha, method.name)}.`),
    };
  }
  const differs = before && after ? pairDiff(method.before.source, method.after.source) : { before: [], after: [] };
  const who = [
    before ? sideWho(method.before, method.name, pair.headSha) : '',
    after ? sideWho(method.after, method.name, pair.fixSha) : '',
  ].filter((one) => one.length > 0).join(' → ');

  return {
    shown: true,
    html: `<p class="who">${who}</p>
    <div class="sides">
      ${realPane('Before', method.before, pair, differs.before, pair.headSha, method.name)}
      ${realPane('After', method.after, pair, differs.after, pair.fixSha, method.name)}
    </div>`,
  };
}
