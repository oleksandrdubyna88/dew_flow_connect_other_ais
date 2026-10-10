import { CALLER_KINDS, DEFAULT_CONSULT, type ConsultantChoice, type ConsultSettings, type ResolvedConsultant, isCallersOwnRuntime } from './consultSettings';
import { optionHtml, pickRefusal, rowName, rowPicks, rowsFor, type PickOption } from './catalogPicks';
import { type ConsultantHealthState, callerHealth } from './consultantHealthState';
import { healthBlock } from './consultantHealthView';
import { escapeHtml } from './escapeHtml';
import { help } from './panelControls';
import type { Vendor } from './vendors';

/**
 * The Settings page's consultant picks (research/PLAN_one_model_catalog.md E4.2): a caller's consultant is CHOSEN from
 * the rows ticked Consultant on Models, where the model itself is edited — and stored as a reference to that row.
 * Pure: the page draws the view, the host applies the writes.
 *
 * <p>Absent is the shipped pair (D2). A pick of a row that is no longer ticked, or no longer there, is shown, selected
 * and named — never cleared (D3): only the person moves it. The caller's own vendor is offered like any other row, with
 * a word about it: a stronger model of the same vendor is a real choice, and the server sees the vendor, never the
 * model.</p>
 */

/** What one caller's picker shows, decided. */
export interface ConsultantPickView {
  readonly options: readonly PickOption[];
  readonly selected: string;
  /** What to say under the picker — a stranded pick, the caller's own vendor — or ''. */
  readonly note: string;
  /** The rows ticked consultant this caller does not ask while it is on its shipped pair, said by name — or ''. */
  readonly unpicked: string;
}

/** One setting the host saves, in order. */
export interface PickWrite {
  readonly key: string;
  readonly value: unknown;
}

/** What a pick asks the host to save — nothing, with the reason, when the pick is not one the page offers. */
export interface PickWrites {
  readonly writes: readonly PickWrite[];
  readonly refusal: string;
}

/** The row id a stored entry picks — '' when it is the caller's shipped pair, which is what absence means (D2). */
function pickedId(caller: string, stored: ConsultantChoice): string {
  const shipped = DEFAULT_CONSULT.stored[caller];
  const same = shipped !== undefined && shipped.vendor === stored.vendor && stored.runtime === '';

  return same ? '' : stored.vendor;
}

/**
 * A word about a pick of the caller's own vendor — offered, never refused — or ''. Whether a runtime IS the caller is
 * `consultSettings`' rule: the Gemini CLI's own vendor runs on `antigravity`, so a name-to-name comparison said nothing
 * to the one caller most likely to be pointed back at itself.
 */
function sameVendorNote(caller: string, picked: string, rows: readonly Vendor[]): string {
  const row = rows.find((one) => one.id === picked);

  return row !== undefined && isCallersOwnRuntime(caller, row.runtime) ? 'The same vendor as the caller: it can be a stronger model, but it shares the caller’s blind spots.' : '';
}

/** The words a sentence about one row, or about several, takes. */
const ONE_ROW = { verb: 'is', pick: 'it' } as const;
const SOME_ROWS = { verb: 'are', pick: 'one' } as const;

/** Names as a sentence lists them: "A", "A and B", "A, B and C". */
function namesSaid(names: readonly string[]): string {
  return names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)!}`;
}

/** The rows ticked consultant other than the caller's shipped pair's own row — none once the caller has picked. */
function unpickedRows(caller: string, picked: string, rows: readonly Vendor[]): readonly string[] {
  const shipped = DEFAULT_CONSULT.stored[caller]?.vendor ?? '';

  return picked === '' ? rowsFor('consultant', rows).filter((row) => row.id !== shipped).map(rowName) : [];
}

/**
 * Why a ticked row is not asked (operator, 2026-10-10): a tick on Models only makes a row AVAILABLE — the caller's pick
 * decides, and absence is the shipped pair (D2), which stays the rule. So a caller on its pair says, by name, which
 * ticked rows it is not asking and where to pick them. '' when it has picked, or nothing else is ticked.
 */
function unpickedNote(caller: string, picked: string, rows: readonly Vendor[]): string {
  const names = unpickedRows(caller, picked, rows);
  const words = names.length === 1 ? ONE_ROW : SOME_ROWS;

  return names.length === 0 ? '' : `${namesSaid(names)} ${words.verb} ticked consultant on Models, but ${callerLabel(caller)} `
    + `still asks the shipped pair — pick ${words.pick} here to use it.`;
}

/** A caller as the page names it — "Claude Code" — or its id for one this build does not list. */
function callerLabel(caller: string): string {
  return CALLER_KINDS.find((one) => one.id === caller)?.label ?? caller;
}

/** A quiet line under a pick, of one kind — '' when there is nothing to say. */
function hintLine(kind: string, text: string): string {
  return text.length === 0 ? '' : `\n  <div class="hint ${kind}">${escapeHtml(text)}</div>`;
}

/**
 * One caller's picker.
 *
 * @param stored the caller's entry as stored (`ConsultSettings.stored`, the shipped pair where absent)
 * @param rows every catalog row (`PanelState.catalogRows`)
 */
export function consultantPickView(caller: string, stored: ConsultantChoice, rows: readonly Vendor[]): ConsultantPickView {
  const picked = pickedId(caller, stored);
  const shipped = DEFAULT_CONSULT.stored[caller]?.vendor ?? '';
  const picks = rowPicks('consultant', picked, rows, 'This caller');

  return {
    options: [{ value: '', label: `The shipped pair — ${shipped}`, disabled: false }, ...picks.options],
    selected: picked,
    note: picks.note || sameVendorNote(caller, picked, rows),
    unpicked: unpickedNote(caller, picked, rows),
  };
}

function refused(refusal: string): PickWrites {
  return { writes: [], refusal };
}

/**
 * What a pick saves: the `consultants` map with this caller's entry a reference to the row — or removed, for the
 * shipped pair. Never the rows: a pick moves which row a caller asks, and a row is removed only on Models.
 *
 * @param current the stored map, read through the side-aware reader
 */
export function consultantPickWrites(current: Readonly<Record<string, unknown>>, caller: string, rowId: string, rows: readonly Vendor[]): PickWrites {
  const refusal = callerPickRefusal(caller, rowId, rows);
  if (refusal.length > 0) {
    return refused(refusal);
  }
  const { [caller]: _was, ...others } = current;

  return { writes: [{ key: 'consultants', value: rowId === '' ? others : { ...others, [caller]: { vendor: rowId } } }], refusal: '' };
}

/** Why a pick cannot be what the page offered — '' when it can. Both arrive in a webview message. */
function callerPickRefusal(caller: string, rowId: string, rows: readonly Vendor[]): string {
  // An id this build does not emit is refused before it indexes anything (`__proto__` would read Object.prototype).
  if (!CALLER_KINDS.some((one) => one.id === caller)) {
    return `There is no caller called ${caller}.`;
  }

  return rowId === '' ? '' : pickRefusal('consultant', rowId, rows);
}

/** What the caller's consultant runs on, as resolved — or why it cannot run. */
function runsOn(resolved: ResolvedConsultant): string {
  return resolved.kind === 'definition' ? `Runs on ${resolved.runtime}, ${modelWords(resolved.model)}.` : resolved.why;
}

function modelWords(model: string): string {
  return model.length > 0 ? `model ${model}` : 'its own default model';
}

function pickHtml(caller: { id: string; label: string }, consult: ConsultSettings, rows: readonly Vendor[], health: ConsultantHealthState | undefined): string {
  const view = consultantPickView(caller.id, consult.stored[caller.id] ?? DEFAULT_CONSULT.stored[caller.id]!, rows);
  const note = hintLine('stranded', view.note) + hintLine('unpicked', view.unpicked);

  return `<div class="field consult-pick">
  <label for="consult-row-${caller.id}">${help('consultCaller')}${escapeHtml(caller.label)} asks</label>
  <select id="consult-row-${caller.id}" data-setting="consultantRow" data-caller="${caller.id}">${view.options.map((one) => optionHtml(one, view.selected)).join('')}</select>
  <div class="hint">${escapeHtml(runsOn(consult.byCaller[caller.id] ?? DEFAULT_CONSULT.byCaller[caller.id]!))}</div>${note}
${pickHealth(caller.id, consult, health)}
</div>`;
}

/**
 * The caller's health block under its pick (each side's facts, this side's paid Check, agy's allow rule and its Copy),
 * decided by `callerHealth` and drawn by `healthBlock` (research/PLAN_one_model_catalog.md E5.1b). Empty while the panel has
 * no health to give.
 */
function pickHealth(kind: string, consult: ConsultSettings, health: ConsultantHealthState | undefined): string {
  return healthBlock(health === undefined ? undefined : callerHealth(kind, consult, health));
}

/**
 * Every caller's picker — what the Consultant tab draws under its switch.
 *
 * @param health what the server says about each caller's consultant (`PanelState.consultantHealth`), or `undefined`
 *   before the first probe
 */
export function consultantPicksHtml(consult: ConsultSettings, rows: readonly Vendor[], health: ConsultantHealthState | undefined): string {
  return `${CALLER_KINDS.map((caller) => pickHtml(caller, consult, rows, health)).join('\n')}
<div class="hint">Each caller asks the model you pick here. Its model, effort and prompt are edited on its card on Models.</div>`;
}
