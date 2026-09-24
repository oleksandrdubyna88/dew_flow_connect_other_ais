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

/**
 * How the block says its small things — as page markup, or as the plain text a chat is handed.
 *
 * <p>Two voices over ONE sentence structure (issue #487): *CoAI: choose* hands a chat the same Where
 * and Complexity the row shows, and a second formatter beside this one would be a second place for a
 * line number or a revision to be said differently.</p>
 */
interface Voice {
  readonly path: (said: string) => string;
  readonly repo: (said: string) => string;
  readonly sha: (sha: string) => string;
  readonly absent: (what: string) => string;
}

const MARKUP: Voice = {
  path: (said) => `<code class="path">${escapeHtml(said)}</code>`,
  repo: (said) => `<span class="repo">${escapeHtml(said)}</span>`,
  sha: commit,
  absent: none,
};

const PLAIN: Voice = {
  path: (said) => said,
  repo: (said) => said,
  sha: (sha) => (text(sha).length > 0 ? text(sha).slice(0, 7) : 'an unrecorded commit'),
  absent: (what) => `(${what})`,
};

/** Where the finding was: its path and line at the commit the reviewers read, and in which checkout. */
function where(pair: AboutRow, voice: Voice = MARKUP): string {
  const file = text(pair.file);
  const line = Number.isInteger(pair.line) && pair.line > 0 ? `:${pair.line}` : '';
  const place = file.length > 0 ? voice.path(`${file}${line}`) : voice.absent('no file recorded');
  const repo = text(pair.repoPath);
  const checkout = repo.length > 0 ? ` in ${voice.repo(repo)}` : '';

  return `${place} at ${voice.sha(pair.headSha)}${checkout}`;
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
function complexity(pair: AboutRow, voice: Voice = MARKUP): string {
  const before = cyclomatic(pair.skeletonBefore, pair.language);
  const after = cyclomatic(pair.skeletonAfter, pair.language);
  if (!before.known) {
    return voice.absent(before.why);
  }
  if (!after.known) {
    return voice.absent(after.why);
  }

  return `${before.value} at ${voice.sha(pair.headSha)} → ${after.value} at ${voice.sha(pair.fixSha)}`;
}

/**
 * The same four lines the block shows, as plain text — what *CoAI: choose* hands a chat (issue #487).
 *
 * <p>Why and Fix are the reviewer's words trimmed; nothing recorded is said as such, never left blank.</p>
 */
export function aboutText(pair: AboutRow): { where: string; why: string; fix: string; complexity: string } {
  const said = (value: string): string => (text(value).length > 0 ? text(value) : PLAIN.absent('none recorded'));

  return { where: where(pair, PLAIN), why: said(pair.why), fix: said(pair.fix), complexity: complexity(pair, PLAIN) };
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
