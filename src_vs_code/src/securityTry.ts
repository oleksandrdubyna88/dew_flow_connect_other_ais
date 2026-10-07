import { FEATURES } from './binaryFeatures';
import { escapeHtml } from './escapeHtml';
import { securityWire, type SecurityLane } from './securityLane';

/**
 * "Try it" on the new page's Security lane tab (todo/PLAN_one_model_catalog.md E4.2): a sample is put to the INSTALLED
 * binary — `coai-mcp --check-security`, one JSON object on stdin (E2.4) — and what it answers is drawn. Never a
 * JavaScript copy of the matcher: the binary is what runs the lane, so it is the only honest judge of a sample. Pure,
 * apart from the spawn the host makes with {@link securityTryRequest}.
 *
 * <p>The sample goes on STDIN, never in an argument: it can hold a token, and argv is in process listings.</p>
 */

/** The longest sample the page sends, in characters — far under the binary's 2 MB request limit. */
export const SECURITY_SAMPLE_MAX = 64 * 1024;

/** One pattern the binary refused, by name. */
export interface RefusedPattern {
  readonly signal: string;
  readonly pattern: string;
  readonly why: string;
}

/** What the binary said about a sample — or, in `failure`, why it said nothing. */
export interface SecurityTryResult {
  readonly sample: string;
  readonly signals: readonly string[];
  readonly cards: readonly string[];
  readonly refused: readonly RefusedPattern[];
  readonly complaints: readonly string[];
  /** The detector did not finish (too large, a pattern out of time): an empty signal list is then not "none". */
  readonly incomplete: boolean;
  readonly failure: string;
}

/** The request, as the binary reads it: the sample, and the lane as a round would send it to THIS binary. */
export function securityTryRequest(sample: string, lane: SecurityLane, features: readonly string[]): string {
  const valid = !('invalidConfiguration' in lane);

  return JSON.stringify(valid ? { text: sample, lane: securityWire(lane, features) } : { text: sample });
}

/** Why a sample is not sent — '' when it is. */
export function securityTryRefusal(sample: string, features: readonly string[]): string {
  if (!features.includes(FEATURES.checkSecurity)) {
    return 'The installed coai-mcp cannot try a sample. Update coai-mcp on the MCP server tab, then try again.';
  }

  return sample.length > SECURITY_SAMPLE_MAX ? `The sample is too long: ${sample.length} characters, and Try it reads at most ${SECURITY_SAMPLE_MAX}.` : '';
}

/** A result that is only a failure, keeping the sample so the box still holds it. */
export function securityTryFailed(sample: string, failure: string): SecurityTryResult {
  return { sample, signals: [], cards: [], refused: [], complaints: [], incomplete: false, failure };
}

/** What the binary answered, read as it said it — or the failure, named. */
export function securityTryAnswer(sample: string, code: number, output: string): SecurityTryResult {
  if (code !== 0) {
    return securityTryFailed(sample, code < 0 ? 'coai-mcp did not answer in time.' : `coai-mcp refused the sample (exit ${code}).`);
  }
  try {
    return { ...answerOf(JSON.parse(output) as unknown), sample, failure: '' };
  } catch {
    return securityTryFailed(sample, 'What coai-mcp answered could not be read.');
  }
}

function answerOf(parsed: unknown): Omit<SecurityTryResult, 'sample' | 'failure'> {
  const json = typeof parsed === 'object' && parsed !== null ? parsed as Record<string, unknown> : {};

  return {
    signals: strings(json['signals']),
    cards: strings(json['cards']),
    refused: (Array.isArray(json['refused']) ? json['refused'] : []).map(refusedOf),
    complaints: strings(json['complaints']),
    incomplete: json['detectionIncomplete'] === true,
  };
}

function strings(value: unknown): readonly string[] {
  return Array.isArray(value) ? value.filter((one): one is string => typeof one === 'string') : [];
}

function refusedOf(value: unknown): RefusedPattern {
  const one = typeof value === 'object' && value !== null ? value as Record<string, unknown> : {};

  return { signal: textOf(one['signal']), pattern: textOf(one['pattern']), why: textOf(one['why']) };
}

function textOf(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

// ---------------------------------------------------------------- the markup

function listed(label: string, values: readonly string[]): string {
  return `<div><b>${label}:</b> ${values.length === 0 ? 'none' : values.map((one) => `<code>${escapeHtml(one)}</code>`).join(', ')}</div>`;
}

function refusedHtml(refused: readonly RefusedPattern[]): string {
  return refused.map((one) => `<div class="stale">Refused for <code>${escapeHtml(one.signal)}</code>: <code>${escapeHtml(one.pattern)}</code> — ${escapeHtml(one.why)}</div>`).join('');
}

function answerHtml(result: SecurityTryResult): string {
  if (result.failure.length > 0) {
    return `<div class="stale" role="status">${escapeHtml(result.failure)}</div>`;
  }
  const incomplete = result.incomplete ? '<div class="stale">The detector did not finish on this sample, so a missing signal is not proof of none.</div>' : '';

  return `<div role="status">${listed('Signals', result.signals)}${listed('Cards that would be due', result.cards)}${refusedHtml(result.refused)}`
    + `${result.complaints.map((one) => `<div class="stale">${escapeHtml(one)}</div>`).join('')}${incomplete}</div>`;
}

/**
 * The Try it block: the sample box, the button, and the last answer.
 *
 * @param result the last answer this window got — absent before the first try
 */
export function securityTryHtml(result: SecurityTryResult | undefined): string {
  return `<h3>Try it</h3>
<p class="hint">Paste a piece of code. The installed coai-mcp says which signals it raises and which cards would be due — with the words and patterns saved above.</p>
<textarea id="security-sample" rows="6" maxlength="${SECURITY_SAMPLE_MAX}" aria-label="A sample to try">${escapeHtml(result?.sample ?? '')}</textarea>
<p><button type="button" data-security-try="">Try it</button></p>
${result === undefined ? '' : answerHtml(result)}`;
}
