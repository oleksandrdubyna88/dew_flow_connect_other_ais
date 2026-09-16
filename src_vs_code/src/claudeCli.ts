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

/** Would anything on this machine actually USE a Claude model? Nothing else is worth a request. */
export function claudeIsWanted(vendors: readonly Vendor[], consult: ConsultSettings): boolean {
  return vendors.some((v) => v.runtime === 'claude')
    || CALLER_KINDS.some(({ id }) => {
      const one = consult.byCaller[id];

      return one !== undefined && one.kind === 'definition' && one.runtime === 'claude';
    });
}
