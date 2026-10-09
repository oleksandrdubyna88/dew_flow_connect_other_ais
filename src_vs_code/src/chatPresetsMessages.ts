import { textControlFrom } from './textControls';

/**
 * What a message about the chat presets MEANS, and the edit decisions taken on it — without a host or a page.
 *
 * <p><b>Why this is not in `chatPresetsPage.ts` any more.</b> Two pages posted these messages until E5.1 deleted the first: the Chat presets tab, and
 * Chat on the new Settings page (todo/PLAN_one_model_catalog.md E4.6b), whose `chatPresets` messages reach the same
 * editing core (`chatPresetsHost.ts`). That core and `chatModelEdits.ts` took their command type and their edit
 * decisions from the tab, which epic 5 deleted (E5.1), so they were moved out first — prerequisite (b) of that epic —
 * exactly as they were.</p>
 *
 * <p><b>The edges point one way.</b> This module imports the text controls and nothing else of this extension. The core
 * and `chatModelEdits.ts` import it — and the core imports `chatModelEdits.ts` — so an import from here into either would
 * close a ring the import-cycle ratchet (`importCycles.test.mjs`) refuses.</p>
 */

/**
 * The two answers to a preset an older build edited after the move (todo/PLAN_one_model_catalog.md, epic 5 prerequisite
 * (a), R7): `use` — the row takes the edited values — or `keep` — the row stays as it is. Either way the record's
 * snapshot takes the preset's whole state (`chatPresetRevision.ts`). Declared here, beside the message that carries it,
 * because this module imports nothing of the chat's.
 */
export type RevisionChoice = 'use' | 'keep';

/** Every message a presets page can send, decided without a host so a test can reach the decision. */
export type PresetCommand =
  | { readonly kind: 'edit'; readonly list: 'prompt' | 'model'; readonly id: string; readonly field: string; readonly value: string | boolean }
  | { readonly kind: 'add'; readonly list: 'prompt' | 'model' }
  | { readonly kind: 'remove'; readonly list: 'prompt' | 'model'; readonly id: string }
  | { readonly kind: 'revision'; readonly presetId: string; readonly choice: RevisionChoice }
  | { readonly kind: 'zoom'; readonly delta: number }
  | { readonly kind: 'tone'; readonly delta: number }
  | { readonly kind: 'ignore' };

const IGNORE: PresetCommand = { kind: 'ignore' };

/** The fields each list HAS. A name this does not know is not a field — `__proto__` included. */
const FIELDS: Record<'prompt' | 'model', readonly string[]> = {
  prompt: ['name', 'text', 'main'],
  model: ['name', 'provider', 'model', 'startingPrompt', 'main'],
};

function listOf(value: unknown): 'prompt' | 'model' | undefined {
  return value === 'prompt' || value === 'model' ? value : undefined;
}

function idOf(value: unknown): string {
  return typeof value === 'string' && value.length > 0 ? value : '';
}

/**
 * One edit, applied to the list it names.
 *
 * <p>The whole list is written back, because that is what a settings array IS — there is no way to
 * update one element of one. Ticking `main` untick's the others here rather than in the reader, so
 * what is SAVED is already true: a reader that had to correct the file every time it read it would
 * be hiding a file nobody could trust.</p>
 */
export function editedRows(
  rows: readonly Record<string, unknown>[],
  command: Extract<PresetCommand, { kind: 'edit' }>,
): readonly Record<string, unknown>[] {
  // The `main` box does NOT come through here any more — `promptRowsAfterMain` owns that rule, next
  // to the reader that enforces the same thing. The branch that used to untick the siblings was left
  // dead by that move, and dead code beside a live rule is the second copy that drifts.
  // The list ITSELF when the row already holds that value — choosing the option a select is already
  // on is a click somebody makes, and writing the file back to say what it says is a configuration
  // event every window then reacts to. The host writes only when the reference moved.
  // The list ITSELF when nothing would change — the row already holds that value, or there is no row
  // with that id at all. A stale message naming a removed row used to produce a new array with
  // identical content, which the host read as a change and wrote back. (CodeRabbit, PR #198.)
  return !rows.some((row) => row['id'] === command.id)
    || rows.some((row) => row['id'] === command.id && row[command.field] === command.value)
    ? rows
    : rows.map((row) => (row['id'] === command.id ? { ...row, [command.field]: command.value } : row));
}

/**
 * Whether the page must be REDRAWN after this command was applied.
 *
 * <p>An edit deliberately does not repaint: the caret is in a box somebody is typing in, and moving
 * it to the end of what they wrote is the defect the sidebar's own prompt box had. The `main` tick is
 * the one edit that has to, and for a reason the rule did not anticipate — its whole effect is on the
 * rows it is NOT in. Ticking one unticks the others in what is saved, so a page that is not redrawn
 * shows two prompts marked main and a file that holds one. Reported with a screenshot of exactly
 * that, 2026-09-11.</p>
 *
 * <p>A checkbox has no caret, so the rule it is carved out of does not apply to it. Decided here
 * rather than by unticking the siblings in the page's own script: that would be the same rule
 * written twice, and the second copy is the one that drifts.</p>
 */
export function editRepaints(command: Extract<PresetCommand, { kind: 'edit' }>): boolean {
  return command.field === 'main';
}

/** The fields a person TYPES into. */
const TYPED: readonly string[] = ['name', 'text', 'startingPrompt'];

/**
 * The key a typed edit settles under in the one queue both pages share (`chatPresetsHost.ts`,
 * todo/PLAN_one_model_catalog.md E4.6b) — per list, row and field, so typing in two boxes stores both — or nothing for a
 * tick, a pick or a press, which has no caret to disturb and goes straight through.
 */
export function presetSettlesAs(command: PresetCommand): string | undefined {
  return command.kind === 'edit' && TYPED.includes(command.field) ? `${command.list}/${command.id}/${command.field}` : undefined;
}

/**
 * What a message from the page means.
 *
 * <p>The same split `chatMessages.ts` makes for the chat page, for the reason recorded there: the
 * module that maps a webview message to an action is otherwise the one no unit test can reach, and a
 * wrong mapping would ship with every test green.</p>
 *
 * <p>A table of the message types rather than the chain of `if`s this was in `chatPresetsPage.ts`: the move put it in
 * a module held to the complexity rule, and the chain was one of that file's recorded exceptions. Its own tests
 * (`chatPresetsMessages.test.ts`, `chatOnTheNewPage.test.ts`) run against this one.</p>
 */
export function presetEdit(message: unknown): PresetCommand {
  const said = recordOf(message);
  const type = said['type'];

  return typeof type === 'string' && Object.hasOwn(READERS, type) ? (READERS[type] ?? ignored)(said) : IGNORE;
}

const ignored = (): PresetCommand => IGNORE;

function recordOf(message: unknown): Record<string, unknown> {
  return typeof message === 'object' && message !== null ? message as Record<string, unknown> : {};
}

/** The text size or tone, read by the controls every page shares — the one message that names no list. */
const textControl = (said: Record<string, unknown>): PresetCommand => textControlFrom(said) ?? IGNORE;

const READERS: Readonly<Record<string, (said: Record<string, unknown>) => PresetCommand>> = {
  zoom: textControl,
  tone: textControl,
  add: (said) => inList(said, (list) => ({ kind: 'add', list })),
  remove: (said) => inList(said, (list) => removalOf(list, idOf(said['id']))),
  edit: (said) => inList(said, (list) => editOf(list, said)),
  revision: (said) => revisionOf(idOf(said['id']), said['choice']),
};

/**
 * A choice on a preset an older build edited after the move (R7) — its preset id and one of the two answers, each
 * compared by value as a field name is, so nothing else the page could send — `__proto__` included — is taken for one.
 */
function revisionOf(presetId: string, choice: unknown): PresetCommand {
  return presetId.length > 0 && (choice === 'use' || choice === 'keep') ? { kind: 'revision', presetId, choice } : IGNORE;
}

/** The command a message about one of the two lists makes — or `ignore` when it names neither. */
function inList(said: Record<string, unknown>, make: (list: 'prompt' | 'model') => PresetCommand): PresetCommand {
  const list = listOf(said['list']);

  return list === undefined ? IGNORE : make(list);
}

function removalOf(list: 'prompt' | 'model', id: string): PresetCommand {
  return id.length === 0 ? IGNORE : { kind: 'remove', list, id };
}

/**
 * An edit of one row's field. A field this list does not have is not an edit. The check is against a list of names
 * rather than a `in` test on the object, so no key of Object.prototype can ever be one of them.
 */
function editOf(list: 'prompt' | 'model', said: Record<string, unknown>): PresetCommand {
  const id = idOf(said['id']);
  const field = said['field'];
  if (id.length === 0 || !isFieldOf(list, field)) {
    return IGNORE;
  }

  return valueEdit(list, id, field, said['value']);
}

function isFieldOf(list: 'prompt' | 'model', field: unknown): field is string {
  return typeof field === 'string' && FIELDS[list].includes(field);
}

/** The `main` tick arrives as a boolean; every other field as a string. */
function valueEdit(list: 'prompt' | 'model', id: string, field: string, value: unknown): PresetCommand {
  const wanted = field === 'main' ? 'boolean' : 'string';

  return typeof value === wanted ? { kind: 'edit', list, id, field, value: value as string | boolean } : IGNORE;
}
