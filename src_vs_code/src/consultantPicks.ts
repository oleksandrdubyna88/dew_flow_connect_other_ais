import { CALLER_KINDS, DEFAULT_CONSULT, type ConsultantChoice, type ConsultSettings, type ResolvedConsultant } from './consultSettings';
import { optionHtml, pickRefusal, rowPicks, type PickOption } from './catalogPicks';
import { escapeHtml } from './escapeHtml';
import type { Vendor } from './vendors';

/**
 * The new Settings page's consultant picks (todo/PLAN_one_model_catalog.md E4.2): a caller's consultant is CHOSEN from
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

/** The runtime a caller kind runs on — the vendor a consultant would share with it — or '' for another client. */
const CALLER_RUNTIME: Readonly<Record<string, string>> = { claude: 'claude', codex: 'codex', gemini: 'gemini' };

/** The row id a stored entry picks — '' when it is the caller's shipped pair, which is what absence means (D2). */
function pickedId(caller: string, stored: ConsultantChoice): string {
  const shipped = DEFAULT_CONSULT.stored[caller];
  const same = shipped !== undefined && shipped.vendor === stored.vendor && stored.runtime === '';

  return same ? '' : stored.vendor;
}

/** A word about a pick of the caller's own vendor — offered, never refused — or ''. */
function sameVendorNote(caller: string, picked: string, rows: readonly Vendor[]): string {
  const row = rows.find((one) => one.id === picked);

  return row !== undefined && row.runtime === CALLER_RUNTIME[caller] ? 'The same vendor as the caller: it can be a stronger model, but it shares the caller’s blind spots.' : '';
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

function pickHtml(caller: { id: string; label: string }, consult: ConsultSettings, rows: readonly Vendor[]): string {
  const view = consultantPickView(caller.id, consult.stored[caller.id] ?? DEFAULT_CONSULT.stored[caller.id]!, rows);
  const note = view.note.length === 0 ? '' : `\n  <div class="hint stranded">${escapeHtml(view.note)}</div>`;

  return `<div class="field consult-pick">
  <label for="consult-row-${caller.id}">${escapeHtml(caller.label)} asks</label>
  <select id="consult-row-${caller.id}" data-setting="consultantRow" data-caller="${caller.id}">${view.options.map((one) => optionHtml(one, view.selected)).join('')}</select>
  <div class="hint">${escapeHtml(runsOn(consult.byCaller[caller.id] ?? DEFAULT_CONSULT.byCaller[caller.id]!))}</div>${note}
</div>`;
}

/** Every caller's picker — what the new page draws where the current page draws each caller's own definition. */
export function consultantPicksHtml(consult: ConsultSettings, rows: readonly Vendor[]): string {
  return `${CALLER_KINDS.map((caller) => pickHtml(caller, consult, rows)).join('\n')}
<div class="hint">Each caller asks the model you pick here. Its model, effort and prompt are edited on its card on Models.</div>`;
}
