import { SHIPPED_COMMANDS, fileIdOf, whyNotGivable, type CommandRow, type CommandStageName } from './commands';

/**
 * What an edit on the Edit commands page does to the rows — issue #467, Epic B. Pure, so every rule is a
 * test; the host only writes what this answers.
 */

export type RowCommand =
  /** `token` is the host's random one; the page sends none and the host fills it in. */
  | { readonly kind: 'add'; readonly token: string }
  | { readonly kind: 'remove'; readonly id: string }
  | { readonly kind: 'retitle'; readonly id: string; readonly value: string }
  | { readonly kind: 'restage'; readonly id: string; readonly value: CommandStageName }
  | { readonly kind: 'switch'; readonly id: string; readonly value: boolean };

export type CommandsOutcome =
  /** `forget` are text files that belong to nothing any more — a removed command's. */
  | { readonly kind: 'rows'; readonly rows: readonly CommandRow[]; readonly forget: readonly string[] }
  | { readonly kind: 'refused'; readonly why: string }
  | { readonly kind: 'unchanged' };

const UNCHANGED: CommandsOutcome = { kind: 'unchanged' };

/**
 * @param texts every command text on disk, by file id — ANY file, blank or not, so a new id can avoid
 *   one a removed command left behind
 */
export function commandsAfter(
  rows: readonly CommandRow[],
  command: RowCommand,
  texts: Readonly<Record<string, string>>,
): CommandsOutcome {
  if (command.kind === 'add') {
    return stored([...rows, { id: nextId(rows, texts, sequence(command.token)), title: 'New command', enabled: false, stage: 'any' }]);
  }
  const row = rows.find((one) => one.id === command.id);

  return row === undefined ? UNCHANGED : edited(rows, row, command, texts);
}

function edited(
  rows: readonly CommandRow[],
  row: CommandRow,
  command: Exclude<RowCommand, { kind: 'add' }>,
  texts: Readonly<Record<string, string>>,
): CommandsOutcome {
  if (command.kind === 'remove') {
    return { kind: 'rows', rows: rows.filter((one) => one.id !== row.id), forget: [fileIdOf(row.id)] };
  }
  if (command.kind === 'switch') {
    return switched(rows, row, command.value, texts);
  }

  return stored(replaced(rows, command.kind === 'retitle' ? { ...row, title: command.value } : { ...row, stage: command.value }));
}

/** Switching ON needs a text; switching off never refuses. */
function switched(rows: readonly CommandRow[], row: CommandRow, on: boolean, texts: Readonly<Record<string, string>>): CommandsOutcome {
  const why = on ? whyNotGivable(row, texts) : '';

  return why.length > 0 ? { kind: 'refused', why } : stored(replaced(rows, { ...row, enabled: on }));
}

function replaced(rows: readonly CommandRow[], row: CommandRow): readonly CommandRow[] {
  return rows.map((one) => (one.id === row.id ? row : one));
}

function stored(rows: readonly CommandRow[]): CommandsOutcome {
  return { kind: 'rows', rows, forget: [] };
}

/**
 * `custom-<token>` for the first token that is neither a row nor a file still on disk — an orphaned file
 * must never hand a new command someone else's words (codex and gemini, the plan round).
 *
 * <p>RANDOM tokens, not a count: the rows are per side and the texts are shared, so two sides counting
 * their own rows would both reach `custom-1` and write one file. (codex, the code round.)</p>
 */
export function nextId(rows: readonly CommandRow[], texts: Readonly<Record<string, string>>, fresh: () => string): string {
  let id = `custom-${fresh()}`;
  while (taken(id, rows, texts)) {
    id = `custom-${fresh()}`;
  }

  return id;
}

/** The token given, then it with -2, -3… — so a taken token still ends in a free id, deterministically. */
function sequence(token: string): () => string {
  const base = token.length > 0 ? token : 'x';
  let n = 0;

  return () => {
    n++;

    return n === 1 ? base : `${base}-${n}`;
  };
}

function taken(id: string, rows: readonly CommandRow[], texts: Readonly<Record<string, string>>): boolean {
  return rows.some((one) => one.id === id) || Object.hasOwn(texts, fileIdOf(id));
}

/**
 * Whether the page may write this text file: a shipped text, or the text of a command in the rows.
 * The id reaches a PATH, so a page naming anything else — another command's removed file, a role's
 * prompt — is refused here as well as by `promptFile`'s slug guard.
 */
export function textBelongs(rows: readonly CommandRow[], fileId: string): boolean {
  return isShipped(fileId) || rows.some((one) => fileIdOf(one.id) === fileId);
}

/** Whether a removed command may take this file with it: never a shipped text's, which is the person's. */
export function forgettable(fileId: string): boolean {
  return !isShipped(fileId);
}

function isShipped(fileId: string): boolean {
  return SHIPPED_COMMANDS.some((one) => one.id === fileId);
}
