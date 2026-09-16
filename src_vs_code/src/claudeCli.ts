import { CALLER_KINDS, ConsultSettings, ResolvedConsultant } from './consultSettings';
import { executableForRuntime } from './vendorTerminal';
import { Vendor } from './vendors';

/**
 * Which Claude binary this installation would actually run — and therefore which one to ask.
 *
 * <p>Its own module, and not a private function inside the panel, because the panel imports
 * `vscode` and nothing that does can be called by a test. This is a decision over two lists, which
 * is exactly the shape that belongs outside a provider.</p>
 */

/** A consultant entry the resolution rule could place: it names its own runtime and CLI path. */
type Placed = Extract<ResolvedConsultant, { kind: 'definition' }>;

/**
 * The reviewer rows first, then the consultant definitions, then the bare name.
 *
 * <p><b>Reviewer rows first</b> because a row is what every other probe here already reads, and a
 * person running reviews on Claude has said which binary that is.</p>
 *
 * <p><b>But a consultant counts.</b> A Claude CONSULTANT with its own CLI path and no reviewer row
 * is a real configuration — it is what the consultant section was split off to allow — and it was
 * being probed with whatever PATH answered instead: a different installation, possibly a different
 * account, spending somebody else's requests to produce labels about a CLI this product would never
 * run. Three reviewers raised it independently on this change's code round.</p>
 *
 * <p>An entry the rule could NOT place has no runtime and no path to offer, so it is passed over
 * rather than read — reading a field off it would be reading one that is not there.</p>
 */
export function claudeExecutableFor(vendors: readonly Vendor[], consult: ConsultSettings): string {
  const row = vendors.find((v) => v.runtime === 'claude' && v.executablePath.length > 0);
  if (row !== undefined) {
    return row.executablePath;
  }
  const caller = CALLER_KINDS
    .map(({ id }) => consult.byCaller[id])
    .filter((one): one is Placed => one !== undefined && one.kind === 'definition' && one.runtime === 'claude')
    .find((one) => one.executablePath.length > 0);

  return caller === undefined ? executableForRuntime('claude', vendors) : caller.executablePath;
}

/**
 * How long a probe that FAILED waits before it may be tried again.
 *
 * <p>Ten minutes: long enough that a spent allowance is not re-asked every few seconds while a person
 * works, short enough that somebody who has just signed in, or whose network came back, does not have
 * to close the window. The failure this bounds is the one the trigger could not express at all — it
 * recorded which CLI it had asked about and never cleared it, so a single timeout left the dropdown
 * saying "not asked yet" until the editor restarted.</p>
 */
export const RETRY_AFTER_MS = 10 * 60 * 1000;

/**
 * May a probe be started right now?
 *
 * <p>Three states, and the middle one is the whole point. A CLI nobody has asked about is always
 * asked. One that was asked and ANSWERED is never asked again this session — that is the edge
 * trigger which stopped a render from spawning a process, and it stays. One that was asked and
 * FAILED gets `failedAt` set, and may be asked again once the backoff has passed.</p>
 *
 * <p>Pure, and here rather than inside the provider, because a rule with a clock in it that lives
 * inside an async orchestration is a rule no test reaches. (gemini Architecture, round 2.)</p>
 */
export function mayAsk(askedFor: string, executable: string, now: number, failedAt: number): boolean {
  if (askedFor !== executable) {
    return true;
  }

  return failedAt > 0 && now - failedAt >= RETRY_AFTER_MS;
}

/** Would anything on this machine actually USE a Claude model? Nothing else is worth a request. */
export function claudeIsWanted(vendors: readonly Vendor[], consult: ConsultSettings): boolean {
  return vendors.some((v) => v.runtime === 'claude')
    || CALLER_KINDS.some(({ id }) => {
      const one = consult.byCaller[id];

      return one !== undefined && one.kind === 'definition' && one.runtime === 'claude';
    });
}
