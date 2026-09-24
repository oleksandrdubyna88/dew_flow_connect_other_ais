import { SHIPPED_COMMAND_TEXTS } from './commandTexts.generated';
import { unknownFields } from './roles';

/**
 * The gate's commands, as the Edit commands page shows them — issue #467, Epic B.
 *
 * <p>Two kinds. The fifteen SHIPPED texts are what the gate's orders are made of (the server embeds
 * `shared/commands/`, this page gets a generated copy); a person rewords one by writing
 * `<dataDir>/prompts/<id>.md`, and Restore deletes that file. A person's OWN commands are rows of
 * `coai.commands`, mirrored to the server as `COAI_COMMANDS`, each with its text in
 * `<dataDir>/prompts/command-<id>.md` — the server adds the prefix, and so does {@link fileIdOf}.</p>
 */

export type CommandStageName = 'plan' | 'code' | 'any';

/** One of a person's own commands — the setting's row, which is also what the server reads. */
export interface CommandRow {
  readonly id: string;
  readonly title: string;
  readonly enabled: boolean;
  readonly stage: CommandStageName;
}

/** One shipped text, as the page draws it. */
export interface ShippedCommand {
  readonly id: string;
  /** What it is, in the words of the switch it belongs to. */
  readonly title: string;
  /** The words the server keeps BEFORE the text, which no override can change. Empty for most. */
  readonly marker: string;
  /** What the server fills in, when the text says them. */
  readonly placeholders: readonly string[];
}

/** What every command text's file name starts with — the server's `CommandTexts.Prefix`. */
export const COMMAND_PREFIX = 'command-';

const GATE_MARKER = 'THE GATE runs once ';
const ALREADY_SPLIT_MARKER = 'This plan is a PIECE of a split that is already under way';

const shipped = (id: string, title: string, marker = '', placeholders: readonly string[] = []): ShippedCommand =>
  ({ id: COMMAND_PREFIX + id, title, marker, placeholders });

/** The fifteen, in the order an order is read. A test holds these ids and the generated texts equal. */
export const SHIPPED_COMMANDS: readonly ShippedCommand[] = [
  shipped('preamble', 'The sentence before the orders'),
  shipped('autonomy', 'Work autonomously', 'Work AUTONOMOUSLY. ', ['{scope}']),
  shipped('split-none', 'Split: a plan small enough to build as it stands'),
  shipped('split-small', 'Split: stories only'),
  shipped('split-medium', 'Split: 2-3 epics'),
  shipped('split-large', 'Split: 3-4 epics'),
  shipped('split-huge', 'Split: 4-5 epics'),
  shipped('split-measured', 'Split: what the plan was measured at', '', ['{numbers}', '{verdict}']),
  shipped('cadence-epic', 'Gate: once per epic', GATE_MARKER),
  shipped('cadence-task', 'Gate: once for the whole task', GATE_MARKER),
  shipped('cadence-single', 'Gate: once, for a plan with no epics', GATE_MARKER),
  shipped('another-code-round', 'Gate: the door to a second code round'),
  shipped('already-split-epic', 'A piece of a split, gated per epic', ALREADY_SPLIT_MARKER),
  shipped('already-split-task', 'A piece of a split, gated once for the task', ALREADY_SPLIT_MARKER),
  shipped('model', 'Split with the strongest model', 'Do the SPLIT itself with ', ['{strongest}', '{implementation}']),
];

/** The shipped text of one id, or empty for an id this build does not ship. */
export function shippedTextOf(id: string): string {
  return Object.hasOwn(SHIPPED_COMMAND_TEXTS, id) ? SHIPPED_COMMAND_TEXTS[id] ?? '' : '';
}

/** A custom command's text file id: the stored slug with the prefix the server adds. */
export function fileIdOf(id: string): string {
  return COMMAND_PREFIX + id;
}

/** The slug a custom id must be — the server's rule, and `promptFile`'s. */
const COMMAND_ID = /^[a-z0-9][a-z0-9-]*$/;

const STAGES: readonly CommandStageName[] = ['plan', 'code', 'any'];

const ROW_FIELDS: ReadonlySet<string> = new Set(['id', 'title', 'enabled', 'stage']);

/**
 * The rows of `coai.commands` this page can use: a slug id, listed once; the rest defaulted as the
 * server defaults them (off, titled by the id, every round). Fields this build does not know are
 * CARRIED, as `rolesFrom` carries them, so a newer server's field survives the next edit here.
 */
export function commandsFrom(raw: unknown): readonly CommandRow[] {
  const rows: CommandRow[] = [];
  for (const one of (Array.isArray(raw) ? raw : []).flatMap(commandRow)) {
    if (!rows.some((kept) => kept.id === one.id)) {
      rows.push(one);
    }
  }

  return rows;
}

/**
 * A usable row, or none — as a list, so a caller's `flatMap` drops the unusable ones.
 *
 * <p>Read as the SERVER reads it (`CommandsSetting`), because these rows are what it is sent: the id
 * trimmed, a shipped text's name refused — `{"id":"preamble"}` would be `command-preamble`, the shipped
 * Preamble's own override, edited and on Remove deleted from here (our own reviewer, the code round).</p>
 */
function commandRow(raw: unknown): CommandRow[] {
  const row = recordOf(raw);
  const id = typeof row['id'] === 'string' ? row['id'].trim() : '';
  if (!COMMAND_ID.test(id) || SHIPPED_COMMANDS.some((one) => one.id === fileIdOf(id))) {
    return [];
  }

  return [Object.assign({ ...unknownFields(row, ROW_FIELDS), ...known(row, id) }, asWritten(row['stage']))];
}

/**
 * A stage this page cannot read, KEPT as written: the server refuses the row and says so on the panel,
 * where turning it into `any` here would make it a live command in every round.
 */
function asWritten(stage: unknown): Readonly<Record<string, unknown>> {
  return stage === undefined || readStage(stage) !== undefined ? {} : { stage };
}

function recordOf(raw: unknown): Record<string, unknown> {
  return typeof raw === 'object' && raw !== null ? raw as Record<string, unknown> : {};
}

function known(row: Record<string, unknown>, id: string): CommandRow {
  const title = typeof row['title'] === 'string' && row['title'].trim().length > 0 ? row['title'] : id;

  return { id, title, enabled: row['enabled'] === true, stage: stageOf(row['stage']) };
}

function stageOf(value: unknown): CommandStageName {
  return readStage(value) ?? 'any';
}

/** One of the three names, in any case and trimmed — the server's reading. */
function readStage(value: unknown): CommandStageName | undefined {
  const name = typeof value === 'string' ? value.trim().toLowerCase() : '';

  return STAGES.find((stage) => stage === name);
}

/** Whether a text says something — a blank file is no text, as the server reads it. */
export function hasText(texts: Readonly<Record<string, string>>, fileId: string): boolean {
  return Object.hasOwn(texts, fileId) && (texts[fileId] ?? '').trim().length > 0;
}

/**
 * Why this command may not be switched on, or empty — the page's half of the server's
 * `commandsSkipped`, as `whyNotAskable` is for a role (issue #338).
 */
export function whyNotGivable(row: CommandRow, texts: Readonly<Record<string, string>>): string {
  return hasText(texts, fileIdOf(row.id))
    ? ''
    : `"${row.title}" has no text yet — write what it tells the AI first, then switch it on.`;
}
