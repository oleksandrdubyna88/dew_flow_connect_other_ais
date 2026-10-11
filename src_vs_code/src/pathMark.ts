import { escapeHtml } from './escapeHtml';
import { type PathFinding, markSentence } from './pathFindings';

/**
 * The bright mark beside a path of this side that is not where the settings say it is (todo/PLAN_paths_per_side.md
 * E5.4) — ONE builder for every place that draws one: a question-consultant root, a Models card's CLI path, a watched
 * data folder and the data directory on Setup › MCP server.
 *
 * <p>The shape is `.skew`'s — a 3px left border and an 8% tint in the editor's error colour — with a leading `●` and the
 * word <i>missing</i> beside the value, so a high-contrast theme that drops the tint still shows it (never colour
 * alone). The field or row carries `aria-invalid="true"` and `aria-describedby` the sentence. An `unknown` path — the disk
 * could not tell — takes the warning tone and "could not be checked", and is not invalid: nothing is known against it.</p>
 */

/** The class the row or field carries — `path-missing` in the error tone, `path-unknown` in the warning tone — or none. */
export function markClass(finding: PathFinding | undefined): string {
  if (finding === undefined) {
    return '';
  }

  return finding.state === 'missing' ? ' path-missing' : ' path-unknown';
}

/** The id of the sentence an `aria-describedby` points at — stable for one `(key, value)`, so two renders agree. */
export function markId(finding: PathFinding): string {
  let hash = 5381;
  for (const char of `${finding.key}|${finding.value}`) {
    hash = ((hash * 33) ^ char.codePointAt(0)!) >>> 0;
  }

  return `path-note-${hash.toString(36)}`;
}

/** The attributes the marked control carries: invalid and described for a missing path, described for an unknown one. */
export function markAttributes(finding: PathFinding | undefined): string {
  if (finding === undefined) {
    return '';
  }
  const described = ` aria-describedby="${markId(finding)}"`;

  return finding.state === 'missing' ? ` aria-invalid="true"${described}` : described;
}

/** The sentence under the value, with its `●`, or nothing. */
export function markNote(finding: PathFinding | undefined): string {
  return finding === undefined
    ? ''
    : `<div class="hint path-note" id="${markId(finding)}"><span class="path-mark" aria-hidden="true">●</span> ${escapeHtml(markSentence(finding))}</div>`;
}
