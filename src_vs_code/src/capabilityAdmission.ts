import {
  CAPABILITIES,
  RUNTIME_CAPABILITIES,
  RUNTIMES,
  STANDINGS,
  type Capability,
  type CapabilityStanding,
  type RuntimeCapabilityRow,
} from './runtimeCapabilities.generated';

export { CAPABILITIES, RUNTIMES, STANDINGS };
export type { Capability, CapabilityStanding, RuntimeCapabilityRow };

/**
 * Whether a model + prompt pair may run on the question consultant, and with which caveat — the
 * extension's half of one rule (PLAN_question_consultant.md, A3, D3, D13).
 *
 * <p>The server decides the same question with `CapabilityMatrix.Admit` over the same table; this is
 * what the settings tab DISABLES an incompatible pair with, before it can be saved. Both halves answer
 * `shared/capability-matrix-vectors.json`, which is what makes "blocked in the UI and refused by the
 * server" one rule rather than two copies. Pure: no VS Code, no I/O.</p>
 *
 * <p>Confined admits; unconfined and default-deny admit FLAGGED — the row runs, and its flag is shown on the row
 * and beside every answer (D13, revised 2026-10-03); unsupported and unmeasured refuse; a runtime or a capability
 * the table does not know refuses by name and never defaults.</p>
 */

/** What an admitted pair carries beside its standing: nothing, or the caveat the row shows. */
export type AdmissionFlag = '' | 'unconfined' | 'default-deny';

export interface Admission {
  readonly admitted: boolean;
  /** The row the decision was made from; empty when no row exists. */
  readonly standing: CapabilityStanding | '';
  readonly flag: AdmissionFlag;
  /** Why a pair was refused, naming the pair and the cure; empty when admitted. */
  readonly reason: string;
  /** The sentence a flagged row is shown beside — in the settings, the sidebar and the log. */
  readonly caveat: string;
}

const RECORD = 'research/RESULTS_question_consultant_capabilities.md';

const CAVEATS: Readonly<Record<AdmissionFlag, string>> = {
  '': '',
  unconfined: 'unconfined: can read this machine — a shell is always there, and no flag removes it',
  'default-deny': "held only by the CLI's headless permission default, which nobody configured",
};

/** The file's spelling and nothing else: the panel writes `disk`, never `Disk`. */
export function isCapability(word: string): word is Capability {
  return (CAPABILITIES as readonly string[]).includes(word);
}

/** The table's row for a pair, or nothing for a runtime it does not know. */
export function rowOf(runtime: string, capability: Capability): RuntimeCapabilityRow | undefined {
  return RUNTIME_CAPABILITIES.find((row) => row.runtime === runtime && row.capability === capability);
}

export function admit(runtime: string, capability: string): Admission {
  if (!isCapability(capability)) {
    return refused('', `'${capability}' is not a capability a prompt can declare — a prompt needs one of: `
      + `${CAPABILITIES.join(', ')} (asked for runtime '${runtime}')`);
  }
  const row = rowOf(runtime, capability);
  if (row === undefined) {
    return refused('', `'${runtime}' is not a runtime the question consultant can launch for '${capability}' — `
      + `it launches on: ${RUNTIMES.join(', ')}`);
  }

  return DECISIONS[row.standing](row);
}

/** One decision per standing — a table rather than a switch, so a sixth word is a compile error here and a red test there. */
const DECISIONS: Readonly<Record<CapabilityStanding, (row: RuntimeCapabilityRow) => Admission>> = {
  confined: (row) => admitted(row, ''),
  unconfined: (row) => admitted(row, 'unconfined'),
  'default-deny': (row) => admitted(row, 'default-deny'),
  unsupported: (row) => refused(row.standing, `'${row.runtime}' cannot do '${row.capability}': ${row.measuredWith.note} — `
    + 'give this row a prompt of another capability, or this prompt another runtime'),
  unmeasured: (row) => refused(row.standing, `'${row.runtime}' has not been measured for '${row.capability}' `
    + `(${row.measuredWith.note}) — a measured cell in ${RECORD} is what admits it; until then give this row a prompt of another capability`),
};

function admitted(row: RuntimeCapabilityRow, flag: AdmissionFlag): Admission {
  return { admitted: true, standing: row.standing, flag, reason: '', caveat: CAVEATS[flag] };
}

function refused(standing: CapabilityStanding | '', reason: string): Admission {
  return { admitted: false, standing, flag: '', reason, caveat: '' };
}
