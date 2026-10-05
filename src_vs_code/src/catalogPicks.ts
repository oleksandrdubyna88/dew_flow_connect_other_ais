import type { CatalogUse } from './catalogFields';
import { escapeHtml } from './escapeHtml';
import { USE_LABELS } from './modelCardFields';
import type { Vendor } from './vendors';

/**
 * A feature picking a catalog row on the new Settings page (todo/PLAN_one_model_catalog.md E4.2) — the half the
 * consultant's callers, the question consultant's rows and the security lane's pairs share. Pure.
 *
 * <p>The list is the rows ticked for that use on Models. A pick of a row no longer ticked, or no longer there, stays
 * on the list, selected and named — never cleared (D3): a picker that dropped it would store the first option on the
 * next change, and only the person moves a pick.</p>
 */

/** One option of a picker. */
export interface PickOption {
  readonly value: string;
  readonly label: string;
  readonly disabled: boolean;
}

/** What a picker lists, and what to say about the pick it holds. */
export interface RowPicks {
  readonly options: readonly PickOption[];
  /** A pick that is stranded — gone, or not ticked — said in a sentence; '' otherwise. */
  readonly note: string;
}

export function tickedFor(row: Vendor, use: CatalogUse): boolean {
  return (row.uses ?? []).includes(use);
}

/** The option for a pick the list does not hold: still offered, so the next change cannot lose it silently. */
function strandedOption(use: CatalogUse, picked: string, rows: readonly Vendor[]): readonly PickOption[] {
  const listed = picked === '' || rows.some((row) => row.id === picked && tickedFor(row, use));

  return listed ? [] : [{ value: picked, label: `${picked} (stranded)`, disabled: false }];
}

/** Why a pick is stranded, in words — or '' when it is not. */
function strandedNote(use: CatalogUse, picked: string, rows: readonly Vendor[], who: string): string {
  if (picked === '') {
    return '';
  }
  const row = rows.find((one) => one.id === picked);
  if (row === undefined) {
    return `${picked} is no longer on Models. ${who} keeps asking for it until you pick another.`;
  }

  return tickedFor(row, use) ? '' : `${picked} is not ticked for the ${USE_LABELS[use]} on Models. ${who} keeps it until you pick another.`;
}

/**
 * The rows a feature may pick, and the stranded pick kept.
 *
 * @param picked the row id stored — '' for none
 * @param who what holds the pick, for the sentence ("This caller", "This row")
 */
export function rowPicks(use: CatalogUse, picked: string, rows: readonly Vendor[], who: string): RowPicks {
  const ticked = rows.filter((row) => tickedFor(row, use)).map((row) => ({ value: row.id, label: `${row.id}${row.enabled ? '' : ' (switched off)'}`, disabled: false }));

  return { options: [...ticked, ...strandedOption(use, picked, rows)], note: strandedNote(use, picked, rows, who) };
}

/** Why a picked id cannot be what the page offered for `use` — '' when it can. It arrives in a webview message. */
export function pickRefusal(use: CatalogUse, rowId: string, rows: readonly Vendor[]): string {
  return rows.some((row) => row.id === rowId && tickedFor(row, use)) ? '' : `${rowId} is not ticked for the ${USE_LABELS[use]} on Models.`;
}

export function optionHtml(option: PickOption, selected: string): string {
  return `<option value="${escapeHtml(option.value)}"${option.value === selected ? ' selected' : ''}>${escapeHtml(option.label)}</option>`;
}
