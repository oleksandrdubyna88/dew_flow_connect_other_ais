import { freshPhraseRow } from './phrases';
import { record } from './savedRows';
import type { PhraseCommand, PhraseRowView } from './phrasesPage';

/**
 * What each command does to the stored rows — the rules, outside the host that writes them.
 *
 * <p>`rolesEdit.ts` exists for a reason this file inherits: a rule that lives inside a webview host
 * is a rule no test can reach, and *Remove this role* silently did nothing for a whole plan because
 * of it. Everything decided here is decided without a `vscode`.</p>
 *
 * <p><b>The outcome is a union, never `rows | undefined`.</b> Undefined meant both "no change" and
 * "refused" in the code this replaces, and the two need opposite handling: one is a write the host
 * must skip, the other is a sentence somebody has to read.</p>
 */

/** A row as it sits in the setting. The host writes these back verbatim, unread fields included. */
export type SavedPhraseRow = Record<string, unknown>;

export type RowsOutcome =
  | { readonly kind: 'rows'; readonly rows: readonly SavedPhraseRow[] }
  /** Nothing would change. Writing anyway is a configuration event every window reacts to, saying nothing. */
  | { readonly kind: 'unchanged' };

const UNCHANGED: RowsOutcome = { kind: 'unchanged' };

/** The stored value as a list of rows, with anything that is not a row left out. */
export function rowsOf(saved: unknown): SavedPhraseRow[] {
  return Array.isArray(saved) ? saved.flatMap((row) => {
    const one = record(row);

    return one === undefined ? [] : [one];
  }) : [];
}

/** What the page shows: the rows as stored, with the two fields it edits read as strings. */
export function viewOf(rows: readonly SavedPhraseRow[]): readonly PhraseRowView[] {
  return rows.map((row, index) => ({
    id: typeof row['id'] === 'string' ? row['id'] : `row-${index + 1}`,
    name: typeof row['name'] === 'string' ? row['name'] : '',
    text: typeof row['text'] === 'string' ? row['text'] : '',
  }));
}

function afterEdit(rows: readonly SavedPhraseRow[], command: Extract<PhraseCommand, { kind: 'edit' }>): RowsOutcome {
  // The list ITSELF when nothing would change — the row already holds that value, or there is no row
  // with that id at all. A stale message naming a removed row would otherwise produce a new array of
  // identical content, which the host reads as a change and writes back.
  const found = rows.some((row) => row['id'] === command.id);
  const same = rows.some((row) => row['id'] === command.id && row[command.field] === command.value);

  return !found || same
    ? UNCHANGED
    : { kind: 'rows', rows: rows.map((row) => (row['id'] === command.id ? { ...row, [command.field]: command.value } : row)) };
}

/**
 * One command, applied to the whole list.
 *
 * <p>The whole list is written back, because that is what a settings array IS — there is no way to
 * update one element of one.</p>
 */
export function rowsAfter(rows: readonly SavedPhraseRow[], command: PhraseCommand): RowsOutcome {
  if (command.kind === 'add') {
    const fresh = freshPhraseRow(rows.map((row, index) => ({ id: typeof row['id'] === 'string' ? row['id'] : `row-${index}` })));

    return { kind: 'rows', rows: [...rows, { id: fresh.id, name: fresh.name, text: fresh.text }] };
  }
  if (command.kind === 'remove') {
    const left = rows.filter((row) => row['id'] !== command.id);

    return left.length === rows.length ? UNCHANGED : { kind: 'rows', rows: left };
  }

  return command.kind === 'edit' ? afterEdit(rows, command) : UNCHANGED;
}
