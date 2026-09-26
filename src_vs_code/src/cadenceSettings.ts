import { positive } from './consultSettings';
import { escapeHtml } from './escapeHtml';
import { help, segmentedRadio } from './panelControls';

/**
 * The consultation cadence, as the panel holds it (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.1).
 *
 * <p>The rule is the operator's: one consultation for every three epics of a plan, and at five or
 * more epics the caller is asked which epics and stories carry the most risk, each of which gets a
 * consultation of its own. "Make 3 and 5 editable in the panel" is these four settings. The server
 * owns the arithmetic and the refusal (`CadenceRule`, `CadenceGate`); this module is only what a
 * person chooses, and the four `COAI_CADENCE_*` keys it crosses the seam as.</p>
 */

/** What the gate does about a group of epics nobody consulted on. */
export type CadenceMode = 'off' | 'remind' | 'require';

/** The modes, in the order the panel offers them — the extension's copy of the C# `CadenceMode`, checked by a test. */
export const CADENCE_MODES: readonly CadenceMode[] = ['off', 'remind', 'require'];

export interface CadenceSettings {
  /** Off: no orders. Remind: the orders, nothing refused. Require: an unconsulted group's code round is refused. */
  readonly mode: CadenceMode;
  /** One consultation per this many epics. */
  readonly every: number;
  /** From this many epics the caller is asked which epics and stories carry the most risk. */
  readonly riskThreshold: number;
  /** The most risky epics and stories one plan may name, each consulted on separately. */
  readonly riskMax: number;
}

/**
 * What the server does with no configuration — its `CadenceRule` constants and `PanelSettings` default.
 *
 * <p>`remind` rather than `require` because a refusal from a Marketplace extension nobody configured
 * would stop a stranger's work on a rule they never chose; `require` is one click away. A test reads
 * the C# for every number here, because `envBlock` writes a key only when it DIFFERS from this.</p>
 */
export const DEFAULT_CADENCE: CadenceSettings = { mode: 'remind', every: 3, riskThreshold: 5, riskMax: 3 };

/** The stored keys this feature owns, so no list of them is written out twice. */
export const CADENCE_SETTINGS: readonly string[] = ['cadenceMode', 'cadenceEvery', 'cadenceRiskThreshold', 'cadenceRiskMax'];

/** A raw configuration reader, as `settingsShape` defines it. */
type Read = (section: string) => unknown;

/**
 * The four settings, parsed from whatever `settings.json` actually holds.
 *
 * <p>Read the way the SERVER reads them, so the panel never shows a cadence the server does not run:
 * a mode is matched whatever its case and spacing, and anything else is `remind` — the server's own
 * catch-all; a count is a positive whole number the server can hold, or the default — the server's
 * `IntVar`.</p>
 */
export function cadenceSettingsFrom(read: Read): CadenceSettings {
  return {
    mode: modeOf(read('cadenceMode')),
    every: positive(read('cadenceEvery'), DEFAULT_CADENCE.every),
    riskThreshold: positive(read('cadenceRiskThreshold'), DEFAULT_CADENCE.riskThreshold),
    riskMax: positive(read('cadenceRiskMax'), DEFAULT_CADENCE.riskMax),
  };
}

function modeOf(value: unknown): CadenceMode {
  const said = typeof value === 'string' ? value.trim().toLowerCase() : '';

  return CADENCE_MODES.find((mode) => mode === said) ?? DEFAULT_CADENCE.mode;
}

/**
 * The keys this cadence adds to the server's environment — each only when it differs from the default.
 *
 * <p>So a pristine panel sends nothing and the server's own fallback is what runs, which is safe only
 * while the defaults agree — the test that reads the C# is what keeps them agreeing.</p>
 */
export function cadenceEnv(cadence: CadenceSettings): Record<string, string> {
  return Object.fromEntries(CADENCE_KEYS
    .filter(([, of]) => of(cadence) !== of(DEFAULT_CADENCE))
    .map(([key, of]) => [key, of(cadence)]));
}

/** Each env key and the value it carries — one row per setting, so no fifth can be half-wired. */
const CADENCE_KEYS: readonly (readonly [string, (cadence: CadenceSettings) => string])[] = [
  ['COAI_CADENCE_MODE', (cadence) => cadence.mode],
  ['COAI_CADENCE_EVERY', (cadence) => String(cadence.every)],
  ['COAI_CADENCE_RISK_THRESHOLD', (cadence) => String(cadence.riskThreshold)],
  ['COAI_CADENCE_RISK_MAX', (cadence) => String(cadence.riskMax)],
];

/** The labels the three modes wear in the panel. */
const MODE_WORDS: readonly (readonly [CadenceMode, string])[] = [
  ['off', 'Off'],
  ['remind', 'Remind'],
  ['require', 'Require'],
];

/**
 * The cadence's block in the Consultant section: the mode, and the three numbers.
 *
 * <p>In the Consultant section rather than the gate's, because what it decides is WHEN the consultant
 * is asked; the gate is only where the reminder is delivered. The ranges are the panel's; the server
 * accepts any positive whole number, and a hand-edited `settings.json` can still hold one.</p>
 */
export function cadenceBlock(cadence: CadenceSettings): string {
  return `<div class="field">
  <label>${help('cadence')}Consultation cadence</label>
  ${segmentedRadio('cadenceMode', cadence.mode, 'What the gate does about an epic group nobody consulted on', MODE_WORDS)}
  <div class="hint">After a plan is split, every group of epics owes one consultation — that the group is right, where it is weak, what it forgot. Remind puts the order in every review reply; Require also refuses the group’s first code round until it is taken.</div>
</div>
<div class="field inline">
  <label for="cadenceEvery">One consultation per this many epics</label>
  <input type="number" id="cadenceEvery" min="1" max="14" data-setting="cadenceEvery" value="${escapeHtml(String(cadence.every))}">
</div>
<div class="field inline">
  <label for="cadenceRiskThreshold">Ask for the riskiest pieces from this many epics</label>
  <input type="number" id="cadenceRiskThreshold" min="1" max="14" data-setting="cadenceRiskThreshold" value="${escapeHtml(String(cadence.riskThreshold))}">
</div>
<div class="field inline">
  <label for="cadenceRiskMax">Riskiest pieces per plan, at most</label>
  <input type="number" id="cadenceRiskMax" min="1" max="14" data-setting="cadenceRiskMax" value="${escapeHtml(String(cadence.riskMax))}">
</div>`;
}
