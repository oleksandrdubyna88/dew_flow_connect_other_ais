import { cyclomatic } from './cyclomatic';
import { commit, none, text } from './reviewText';
import { revisionActions, RevisionState } from './revisionActions';
import { CALLS, containerAttribute, REVISIONS } from './livePatch';
import { escapeHtml } from './webviewHtml';

/**
 * What a row says about itself, above its code: where it was, why, the fix, how complex — and the
 * two ways to reach the code.
 *
 * <p>Its own module for the reason `realMethodView.ts` is: the page had reached the 800-line ceiling
 * the coding-style rule sets and `max-lines` enforces, and this is a unit with a boundary of its own
 * — everything here is what a row SAYS, none of it is what a row DOES. Extracted, not copied, when
 * story 3.1 added the revision actions to the block.</p>
 *
 * <p>Pure, and free of `node:` and `vscode`, like every module the page bundle is built from — and
 * free of the page: the fields it reads off a pair are {@link AboutRow}, which a `ReviewPair`
 * satisfies structurally, so the edge runs one way (`importCycles.test.mjs`).</p>
 */

/** The fields the block reads off a pair — a `ReviewPair` is one, structurally. */
export interface AboutRow {
  readonly findingId: number;
  readonly language: string;
  readonly skeletonBefore: string;
  readonly skeletonAfter: string;
  readonly repoPath: string;
  readonly headSha: string;
  readonly fixSha: string;
  readonly file: string;
  readonly line: number;
  readonly why: string;
  readonly fix: string;
}

/**
 * Reviewer prose, escaped on the way into the page.
 *
 * <p>`why` and `fix` are what a model wrote about somebody's code — external data, arriving through
 * a JSON document the server printed — and this page is where a person decides what leaves the
 * machine. An `<img onerror>` in a finding's "fix" renders as the text it is.</p>
 */
const prose = (value: string): string => {
  const said = text(value);

  return said.length > 0 ? escapeHtml(said) : none('none recorded');
};

/** Where the finding was: its path and line at the commit the reviewers read, and in which checkout. */
function where(pair: AboutRow): string {
  const file = text(pair.file);
  const line = Number.isInteger(pair.line) && pair.line > 0 ? `:${pair.line}` : '';
  const place = file.length > 0
    ? `<code class="path">${escapeHtml(file)}${line}</code>`
    : none('no file recorded');
  const repo = text(pair.repoPath);
  const checkout = repo.length > 0 ? ` in <span class="repo">${escapeHtml(repo)}</span>` : '';

  return `${place} at ${commit(pair.headSha)}${checkout}`;
}

/**
 * The two counts, each labelled with the revision its skeleton came from.
 *
 * <p>"Of the method at `aaaa111`", never "of the method": the before side is the method as the
 * reviewers read it, the after side is the method at the commit the fix was found in — two
 * different commits — and both may have changed since. A number without its revision is confidently
 * wrong about today's file. A language the count does not read gets the count's own sentence, not a
 * zero.</p>
 */
function complexity(pair: AboutRow): string {
  const before = cyclomatic(pair.skeletonBefore, pair.language);
  const after = cyclomatic(pair.skeletonAfter, pair.language);
  if (!before.known) {
    return none(before.why);
  }
  if (!after.known) {
    return none(after.why);
  }

  return `${before.value} at ${commit(pair.headSha)} → ${after.value} at ${commit(pair.fixSha)}`;
}

/**
 * The block, whole.
 *
 * <p>The last line is the revision rule's two actions (story 3.1), rendered from what the panel
 * remembers and posted into again when it learns more — the page never decides what they say.</p>
 */
export function about(pair: AboutRow, revision: RevisionState, calls = ''): string {
  return `<dl class="about">
      <dt>Where</dt><dd>${where(pair)}</dd>
      <dt>Why</dt><dd>${prose(pair.why)}</dd>
      <dt>Fix</dt><dd>${prose(pair.fix)}</dd>
      <dt title="cyclomatic complexity of the method as it was at that commit, counted from the skeleton — not of the file today">Complexity</dt><dd>${complexity(pair)}</dd>
      <dt>Open</dt><dd class="open" ${containerAttribute(REVISIONS, pair.findingId)}>${revisionActions(pair, revision)}</dd>
      <dt>Calls</dt><dd class="open" ${containerAttribute(CALLS, pair.findingId)}>${calls}</dd>
    </dl>`;
}
