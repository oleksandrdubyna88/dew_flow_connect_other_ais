import { member } from './rounds';

/**
 * The round card's "written with" line (E5.S6 of `todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md`): who wrote the
 * code a round reviewed, and how much, as ONE line — from the session file's `authors` snapshot and `size`, never from the database.
 *
 * <p>Its own module, not more lines in `rounds.ts`: that file is past the family's ceiling with the line's helpers in it, and this is
 * one unit with one job. Every parameter is `unknown` because nothing read off the disk has been validated — a member of the wrong
 * type makes the whole line unusable (`''`), never half a sentence, and no type assertion stands in for the check.</p>
 *
 * <p>Two rules are shared with the server and held to it by vectors both read: which stored STATE is well formed
 * (`shared/authors-state-vectors.json`, `AuthorsSnapshot.IsWellFormed`) and how a model id is SHOWN and which family it counts under
 * (`shared/model-display-vectors.json`, which the report's coder mix reads too).</p>
 */

export interface ModelShown {
  readonly display: string;
  readonly family: string;
}

interface AuthorRow {
  readonly who: string;
  readonly agent: string;
  readonly model: string;
  readonly association: string;
}

interface Detail {
  readonly rows: readonly AuthorRow[];
  readonly uncoveredWrites: number;
}

interface State {
  readonly kind: string;
  readonly reasons: readonly string[];
}

interface SizeCell {
  readonly coverage: string;
  readonly files: number;
  readonly added: number;
  readonly removed: number;
}

const MODEL_ID = /^claude-([a-z]+)-(\d+)-(\d{1,2})(?:-\d{8})?$/u;

/** One reason: lower-case letters, spaces and hyphens, starting and ending with a letter — `AuthorsSnapshot.IsWellFormed`'s rule. */
const REASON = '[a-z](?:[a-z -]*[a-z])?';
const STATE = new RegExp(`^(?:observed|(?:partial|unobserved): ${REASON}(?:, ${REASON})*)$`, 'u');

const JOINER = ', ';
const NOT_STATED = 'unknown';

/** Whether a stored state is one the engine could have written: the shape, and no reason twice. */
export function isWellFormedAuthorsState(value: string): boolean {
  if (!STATE.test(value)) {
    return false;
  }
  const at = value.indexOf(': ');
  const reasons = at < 0 ? [] : value.slice(at + 2).split(JOINER);

  return new Set(reasons).size === reasons.length;
}

/** A model id as shown, and the family it counts under: `claude-opus-5-5` is `Opus 5.5`/`opus`; anything the rule does not parse is itself/`other`. */
export function modelShown(model: string): ModelShown {
  if (model === '' || model === NOT_STATED) {
    return { display: NOT_STATED, family: NOT_STATED };
  }
  const found = MODEL_ID.exec(model);

  return found === null ? { display: model, family: 'other' } : shownFrom(found);
}

function shownFrom(found: RegExpExecArray): ModelShown {
  const family = found[1] ?? '';

  return { display: `${capitalised(family)} ${found[2] ?? ''}.${found[3] ?? ''}`, family };
}

function capitalised(word: string): string {
  return word.charAt(0).toUpperCase() + word.slice(1);
}

/** The card line for one round, or '' for a round with no usable snapshot. */
export function writtenWith(authors: unknown, size: unknown): string {
  const state = isObject(authors) ? stateOf(authors) : undefined;

  return isObject(authors) && state !== undefined ? lineFor(authors, state, size) : '';
}

function lineFor(authors: object, state: State, size: unknown): string {
  if (state.kind === 'unobserved') {
    return `authors not observed — ${state.reasons.join(JOINER)}`;
  }
  const detail = detailOf(authors);

  return detail === undefined ? '' : lineOf(state, detail, size);
}

function lineOf(state: State, detail: Detail, size: unknown): string {
  return [
    mainClause(detail.rows),
    coderClause(detail.rows),
    sizeClause(size),
    state.kind === 'partial' ? `partial — ${state.reasons.join(JOINER)}` : '',
    detail.uncoveredWrites > 0 ? 'size not covered' : '',
  ].filter((clause) => clause !== '').join(' · ');
}

// ---------- the snapshot, read defensively ----------

function isObject(value: unknown): value is object {
  return typeof value === 'object' && value !== null;
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function textOf(source: object, key: string): string | undefined {
  const value = member(source, key);

  return typeof value === 'string' ? value : undefined;
}

/** The state, and the reasons — only when the pair renders to a state the shared rule accepts. */
function stateOf(authors: object): State | undefined {
  const kind = textOf(authors, 'state');
  const reasons = reasonsOf(member(authors, 'reasons'));

  return kind === undefined || reasons === undefined ? undefined : checked({ kind, reasons });
}

/** The snapshot's own kinds. A kind that already carries its reasons is the database's rendered column, never a snapshot's state. */
const KINDS: ReadonlySet<string> = new Set(['observed', 'partial', 'unobserved']);

function checked(state: State): State | undefined {
  if (!KINDS.has(state.kind)) {
    return undefined;
  }

  const text = state.reasons.length === 0 ? state.kind : `${state.kind}: ${state.reasons.join(JOINER)}`;

  return isWellFormedAuthorsState(text) ? state : undefined;
}

/** A list of strings; an absent member is an empty list, anything else that is not one is unusable. */
function reasonsOf(value: unknown): readonly string[] | undefined {
  if (value === undefined) {
    return [];
  }

  return Array.isArray(value) && value.every((reason): reason is string => typeof reason === 'string') ? value : undefined;
}

function detailOf(authors: object): Detail | undefined {
  const rows = rowsOf(member(authors, 'rows'));
  const uncovered = uncoveredOf(member(authors, 'uncoveredWrites'));

  return rows === undefined || uncovered === undefined ? undefined : { rows, uncoveredWrites: uncovered };
}

/** A count of writes into other checkouts: a non-negative integer, or `-1` for one nothing measured. */
function uncoveredOf(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= -1 ? value : undefined;
}

function rowsOf(value: unknown): readonly AuthorRow[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }
  const rows = value.map(rowOf);

  return rows.every((row) => row !== undefined) ? rows.filter((row): row is AuthorRow => row !== undefined) : undefined;
}

const ROW_TEXTS = ['window', 'stage', 'who', 'agentType', 'requested', 'model', 'effort', 'association'];
const ROW_COUNTS = ['number', 'launches', 'turns', 'writeCalls'];
const TOKEN_COUNTS = ['input', 'output', 'cacheCreation', 'cacheRead'];

/** One row of the snapshot, every member of the shape the server writes checked — or nothing. */
function rowOf(row: unknown): AuthorRow | undefined {
  return isObject(row) && hasRowShape(row) ? readRow(row) : undefined;
}

/** Every text, count and token member the server writes for a row is present and of its type. */
function hasRowShape(row: object): boolean {
  const tokens = member(row, 'tokens');

  return hasAgent(row) && isObject(tokens) && hasMembers(row, tokens);
}

function hasAgent(row: object): boolean {
  const agent = member(row, 'agent');

  return isObject(agent) && textOf(agent, 'id') !== undefined;
}

function hasMembers(row: object, tokens: object): boolean {
  return ROW_TEXTS.every((key) => textOf(row, key) !== undefined)
    && ROW_COUNTS.every((key) => isCount(member(row, key)))
    && TOKEN_COUNTS.every((key) => isCount(member(tokens, key)));
}

/** A text member, or '' — only after the shape check, so the fallback is never reached for a real row. */
function textOr(source: unknown, key: string): string {
  return isObject(source) ? textOf(source, key) ?? '' : '';
}

function readRow(row: object): AuthorRow {
  return {
    who: textOr(row, 'who'),
    agent: textOr(member(row, 'agent'), 'id'),
    model: textOr(row, 'model'),
    association: textOr(row, 'association'),
  };
}

// ---------- the clauses ----------

function mainClause(rows: readonly AuthorRow[]): string {
  const models = [...new Set(rows.filter((row) => row.who === 'main').map((row) => modelShown(row.model).display))];

  return models.length === 0 ? 'written with main model unknown' : `written with ${models.join(' + ')} (main)`;
}

/** The distinct `wrote here` subagents (a straddler once), grouped by family: the larger group first, a tie by family name. */
function coderClause(rows: readonly AuthorRow[]): string {
  const families = coderFamilies(rows);
  const counts = new Map<string, number>();
  for (const family of families.values()) {
    counts.set(family, (counts.get(family) ?? 0) + 1);
  }

  return groupsClause([...counts].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1)), families.size);
}

/** Each coder once, under the family of the first model it was seen on. */
function coderFamilies(rows: readonly AuthorRow[]): ReadonlyMap<string, string> {
  const families = new Map<string, string>();
  for (const row of rows.filter((candidate) => candidate.who === 'subagent' && candidate.association === 'wrote here')) {
    families.set(row.agent, families.get(row.agent) ?? modelShown(row.model).family);
  }

  return families;
}

function groupsClause(groups: readonly (readonly [string, number])[], total: number): string {
  if (groups.length === 0) {
    return 'main only';
  }
  const noun = total === 1 ? 'coder' : 'coders';

  return groups.length > 2
    ? `${total} coders (mixed)`
    : `${groups.map(([name, count]) => `${count} ${familyLabel(name)}`).join(' + ')} ${noun}`;
}

/** A Claude family by its capitalised word; `other` and `unknown` as themselves — the groups the report counts, none invented. */
function familyLabel(family: string): string {
  return family === 'other' || family === NOT_STATED ? family : capitalised(family);
}

// ---------- the size ----------

/** The `delta` scope when it is recorded, else the `whole` one when it is, else nothing. */
function sizeClause(size: unknown): string {
  if (!isObject(size)) {
    return '';
  }
  const scope = ['delta', 'whole'].map((name) => member(size, name)).find((one) => isObject(one) && member(one, 'state') === 'recorded');
  const cells = isObject(scope) ? cellsOf(member(scope, 'cells')) : undefined;

  return cells === undefined ? '' : describe(cells);
}

function cellsOf(value: unknown): readonly SizeCell[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }
  const cells = value.map(cellOf);

  return cells.every((cell) => cell !== undefined) ? cells.filter((cell): cell is SizeCell => cell !== undefined) : undefined;
}

function cellOf(cell: unknown): SizeCell | undefined {
  return isObject(cell) ? readCell(cell) : undefined;
}

function readCell(cell: object): SizeCell | undefined {
  const coverage = textOf(cell, 'coverage');
  const numbers = cellNumbers(cell);

  return coverage !== undefined && textOf(cell, 'kind') !== undefined && numbers !== undefined ? { coverage, ...numbers } : undefined;
}

function cellNumbers(cell: object): Pick<SizeCell, 'files' | 'added' | 'removed'> | undefined {
  const files = member(cell, 'files');
  const added = optionalCount(member(cell, 'added'));
  const removed = optionalCount(member(cell, 'removed'));

  return isCount(files) && added !== undefined && removed !== undefined ? { files, added, removed } : undefined;
}

/** A line count that a binary cell leaves out — absent or null reads as none — and anything else that is not a count is unusable. */
function optionalCount(value: unknown): number | undefined {
  if (value === undefined || value === null) {
    return 0;
  }

  return isCount(value) ? value : undefined;
}

/** Lines of what the reviewers were shown or told of; files of that and the binary ones — a binary has files and no lines, never 0. */
function describe(cells: readonly SizeCell[]): string {
  const read = cells.filter((cell) => cell.coverage === 'shown' || cell.coverage === 'elided');
  const files = cells.filter((cell) => cell.coverage !== 'excluded').reduce((sum, cell) => sum + cell.files, 0);
  const text = `${files} ${files === 1 ? 'file' : 'files'}`;
  const lines = `+${read.reduce((sum, cell) => sum + cell.added, 0)} −${read.reduce((sum, cell) => sum + cell.removed, 0)}`;

  return read.length > 0 || files === 0 ? `${lines} in ${text}` : `in ${text}`;
}
