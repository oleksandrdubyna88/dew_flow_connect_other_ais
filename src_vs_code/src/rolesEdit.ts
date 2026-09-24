import {
  MAX_ACTIVE_PER_BUCKET, RESULT_STAGE, activeCount, bucketAt, bucketOf, idFor, isActive,
  isBuiltIn, promptIdFor, promptIdsInUse, stageOf, whyNotAskable, type RoleRow,
} from './roles';
import { isShippedPrompt, type RolesCommand } from './rolesPage';

/**
 * What one command from the roles page does to the rows — every rule, and nothing a host can do.
 *
 * <p><b>Why this is a module and not three private functions of the panel host.</b> "Remove this
 * role" did nothing for the whole of this plan, and no test in this extension could have caught it:
 * the rule lived inside a webview host, which the house style deliberately leaves untested, and the
 * structural scans that cover a host's obligations have nothing to say about a branch. Four
 * reviewers found it by reading. Reading is not a process, so the rules moved out here, where each
 * one is reachable from a test, and the host kept only what a host can do — read a setting, write
 * one, write a file, repaint.</p>
 *
 * <p><b>The answer is a union, and that is the other half of the same defect.</b> It used to be
 * `RoleRow | undefined`, where `undefined` meant BOTH "this command changes nothing" and "this
 * command is refused" — so removal, which produces no row by definition, took the refusal's
 * behaviour and wrote nothing. Two meanings in one absence is how one of them goes missing; the
 * doctrine's own words for it are that "not captured" and "empty" are different states.</p>
 *
 * <p><b>Every refusal here has a twin in `RoleComposition` on the server</b>, which is the boundary
 * that actually holds. These exist so the page never SAVES an action the server would refuse — the
 * page also disables the control, and this is what catches a webview that posts anyway.</p>
 */
export type RowsOutcome =
  /** The rows to store, and the prompt-override files that no row points at any more. */
  | { readonly kind: 'rows'; readonly rows: readonly RoleRow[]; readonly forget: readonly string[] }
  /** Refused, with the sentence to show the person — never silently dropped. */
  | { readonly kind: 'refused'; readonly why: string }
  /** Nothing to store: the command was a no-op, or it belongs to the host rather than to the rows. */
  | { readonly kind: 'unchanged' };

const UNCHANGED: RowsOutcome = { kind: 'unchanged' };

/** What a row may never say about a role this product ships. The server reads none of them. */
const FIXED_ON_SHIPPED: readonly string[] = ['name', 'stage', 'programmingTask'];

function refused(why: string): RowsOutcome {
  return { kind: 'refused', why };
}

function stored(rows: readonly RoleRow[], forget: readonly string[] = []): RowsOutcome {
  return { kind: 'rows', rows, forget };
}

/**
 * @param reserved ids that are TAKEN although no row holds them — every deletion still on its way
 * out. Without it the next role of a deleted name gets the same id, and inherits a stranger's round
 * budget, threshold and enabled flag out of the four records keyed by it.
 */
export function rowsAfter(
  current: readonly RoleRow[],
  command: RolesCommand,
  reserved: ReadonlySet<string> = new Set(),
  /**
   * The prompt bodies that exist, by id — what decides whether a role can be switched on (issue #338).
   * Empty means none are known, and a role of one's own with none is not switched on.
   */
  texts: Readonly<Record<string, string>> = {},
): RowsOutcome {
  // `text` is a FILE and `restorePrompt` deletes one; zoom is a different setting entirely; and
  // `finishDeletion` and `reloadWindow` are about a deletion that has already left the rows. All of
  // them are the host's, and none touches a row.
  if (command.kind === 'ignore' || command.kind === 'zoom' || command.kind === 'restorePrompt'
      || command.kind === 'tab' || command.kind === 'finishDeletion' || command.kind === 'reloadWindow') {
    return UNCHANGED;
  }
  if (command.kind === 'editPrompt' && command.field === 'text') {
    return UNCHANGED;
  }
  if (command.kind === 'add') {
    return added(current, reserved);
  }
  if (command.kind === 'remove') {
    return removed(current, command.id);
  }

  return onRow(current, command, texts);
}

/**
 * A new role of the person's own — created switched OFF, always.
 *
 * <p>It has no question to ask yet: its one prompt has no text until somebody writes it, and the server
 * skips a role like that in every round. It used to be switched on whenever its stage had room, which is
 * how issue #338's `Role2` sat enabled, counted and never asked through six rounds. Before that it was
 * stored active whatever the count was. A role created switched off is a role a person can see is
 * switched off; switching it on is refused until its question exists (`switched`).</p>
 */
function added(current: readonly RoleRow[], reserved: ReadonlySet<string>): RowsOutcome {
  // Rows AND tombstones. An id whose deletion has not finished is not free: the four records keyed
  // by it — `rounds`, `thresholds`, `roleEnabled`, `promptsPerRound` — are still there until the
  // mirror carries the removal, so a new role taking that id opens with a stranger's budget.
  const id = idFor('', new Set([...current.map((r) => r.id.toLowerCase()), ...reserved]));
  const promptId = promptIdFor(id, 'general', promptIdsInUse(current));

  return stored([
    ...current,
    { id, name: 'A new role', stage: RESULT_STAGE, programmingTask: true, active: false,
      prompts: [{ id: promptId, label: 'General', purpose: '' }] },
  ]);
}

/**
 * A role taken out — and the override files that went with it named, so the host can delete them.
 *
 * <p>Left behind, those files come BACK: the next role called "Requirements" generates the same id,
 * which generates the same prompt id, and opens with text the person believed they had deleted.</p>
 */
function removed(current: readonly RoleRow[], id: string): RowsOutcome {
  if (isBuiltIn(id)) {
    return refused('That is a role this product ships — it can be switched off, but not removed.');
  }

  const mine = current.find((r) => r.id === id);
  if (mine === undefined) {
    return UNCHANGED;
  }
  if (lastStanding(mine, current)) {
    return refused(EMPTY_STAGE);
  }

  return stored(
    current.filter((r) => r.id !== id),
    (mine.prompts ?? []).map((p) => p.id).filter((promptId) => !isShippedPrompt(id, promptId)),
  );
}

/** Everything that edits one existing row — or creates the override row that will hold the edit. */
function onRow(
  current: readonly RoleRow[],
  command: Exclude<RolesCommand, {
    kind: 'ignore' | 'zoom' | 'tab' | 'add' | 'remove' | 'restorePrompt' | 'finishDeletion' | 'reloadWindow';
  }>,
  texts: Readonly<Record<string, string>>,
): RowsOutcome {
  const mine = current.find((r) => r.id === command.id);
  const known = mine ?? (isBuiltIn(command.id) ? { id: command.id } : undefined);
  if (known === undefined) {
    return UNCHANGED;
  }

  const outcome = changed(known, command, current, texts);
  if (outcome.kind !== 'rows') {
    return outcome;
  }

  const next = outcome.rows[0]!;

  return stored(
    mine === undefined ? [...current, next] : current.map((r) => (r.id === command.id ? next : r)),
    outcome.forget,
  );
}

/** One row, edited — returned as a one-row outcome so a refusal can carry its sentence out. */
function changed(
  row: RoleRow,
  command: Exclude<RolesCommand, {
    kind: 'ignore' | 'zoom' | 'tab' | 'add' | 'remove' | 'restorePrompt' | 'finishDeletion' | 'reloadWindow';
  }>,
  all: readonly RoleRow[],
  texts: Readonly<Record<string, string>>,
): RowsOutcome {
  if (command.kind === 'edit') {
    return edited(row, command, all, texts);
  }
  if (command.kind === 'addPrompt') {
    const label = 'A new prompt';
    const id = promptIdFor(row.id, label, promptIdsInUse(all));

    return stored([{ ...row, prompts: [...(row.prompts ?? []), { id, label, purpose: '' }] }]);
  }
  if (command.kind === 'removePrompt') {
    return promptRemoved(row, command.promptId);
  }

  return promptEdited(row, command.promptId, command.field, command.value);
}

/** One field of a role. Which fields may be set at all depends on whose role it is. */
function edited(
  row: RoleRow,
  { field, value }: { readonly field: string; readonly value: string | boolean },
  all: readonly RoleRow[],
  texts: Readonly<Record<string, string>>,
): RowsOutcome {
  if (fixedOnShipped(row, field)) {
    return refused(
      'A role this product ships keeps its name, its stage and its kind: they key your settings, '
      + 'your open sessions and every round already recorded, and the review server reads none of '
      + 'them from your configuration. Its prompt text is yours to rewrite.',
    );
  }
  if (field === 'active') {
    return switched(row, value === true, all, texts);
  }
  if (field === 'stage') {
    return restaged(row, String(value), all);
  }

  return stored([{ ...row, [field]: value }]);
}

function fixedOnShipped(row: RoleRow, field: string): boolean {
  return isBuiltIn(row.id) && FIXED_ON_SHIPPED.includes(field);
}

/**
 * The sentence for a stage with no reviewer in it.
 *
 * <p>Said by three commands, because THREE of them change how many roles are active in a stage:
 * switching one off, removing it, and moving it to the other stage. Only the switch was guarded —
 * so the page would save an empty stage by either of the other two routes, and the round that
 * followed would have no reviewer, which the session counts as unresolved and never lets anybody
 * retry. Found by two reviewers on the second code round.</p>
 */
const EMPTY_STAGE =
  'That is the only role still active in this stage. Switch another one on first — a stage with '
  + 'nothing in it produces a round with no reviewer at all.';

/** And the sentence for a stage that already has as many as it may run. */
const STAGE_FULL = 'Five roles are already active in that stage. Switch one off to make room.';

/** Whether this role is the last reviewer its stage has. */
function lastStanding(row: RoleRow, all: readonly RoleRow[]): boolean {
  return isActive(row) && activeCount(all, bucketOf(row)) <= 1;
}

/**
 * A role's switch, refused when it would leave a stage with nothing in it or put a sixth role in one.
 *
 * <p>The page disables both of these; this is the twin that catches a webview posting anyway.</p>
 */
function switched(row: RoleRow, on: boolean, all: readonly RoleRow[], texts: Readonly<Record<string, string>>): RowsOutcome {
  const why = on ? whyNotOn(row, all, texts) : whyNotOff(row, all);

  return why.length > 0 ? refused(why) : stored([{ ...row, active: on }]);
}

/**
 * What stops a role being switched on, or nothing. Only a role that is OFF is asked about — switching on
 * what is already on changes nothing — and the stage's room first, because a full stage is the answer
 * whatever the role holds.
 */
function whyNotOn(row: RoleRow, all: readonly RoleRow[], texts: Readonly<Record<string, string>>): string {
  if (isActive(row)) {
    return '';
  }
  if (activeCount(all, bucketOf(row)) >= MAX_ACTIVE_PER_BUCKET) {
    return 'Five roles are already active in this stage. Switch one off to make room.';
  }
  const unaskable = whyNotAskable(row, texts);

  return unaskable.length > 0 ? `${unaskable} Write the question it asks in the box under it, then switch it on.` : '';
}

function whyNotOff(row: RoleRow, all: readonly RoleRow[]): string {
  return lastStanding(row, all) ? EMPTY_STAGE : '';
}

/**
 * A role moved to the other stage — which is a role leaving one stage and joining another, so both
 * stages' rules apply to it.
 *
 * <p>A role that is switched off moves freely: it is a reviewer in neither stage, so neither count
 * changes. So does one "moved" to the stage it is already in, which is what a select fires when
 * somebody opens it and picks the same thing.</p>
 */
function restaged(row: RoleRow, to: string, all: readonly RoleRow[]): RowsOutcome {
  const moved: RoleRow = { ...row, stage: to };
  if (!isActive(row) || to === stageOf(row)) {
    return stored([moved]);
  }

  const why = whyNotMoved(row, to, all);

  return why.length > 0 ? refused(why) : stored([moved]);
}

/** What is wrong with the move, or nothing at all. */
function whyNotMoved(row: RoleRow, to: string, all: readonly RoleRow[]): string {
  if (lastStanding(row, all)) {
    return EMPTY_STAGE;
  }

  // The bucket it would land IN, not the stage it is moving to: a role keeps its kind across a
  // move, and counting the whole stage would refuse a document role for the code roles already in it.
  return activeCount(all, bucketAt(row, to)) >= MAX_ACTIVE_PER_BUCKET ? STAGE_FULL : '';
}

function promptRemoved(row: RoleRow, promptId: string): RowsOutcome {
  // A shipped prompt has no Remove button, and this refuses one anyway: its text is embedded in the
  // server's binary, so there would be nothing left to restore.
  if (isShippedPrompt(row.id, promptId)) {
    return refused('That is a prompt this product ships. Empty its text to go back to the shipped question.');
  }

  return stored([{ ...row, prompts: (row.prompts ?? []).filter((p) => p.id !== promptId) }], [promptId]);
}

function promptEdited(row: RoleRow, promptId: string, field: 'label' | 'purpose' | 'text', value: string): RowsOutcome {
  if (isShippedPrompt(row.id, promptId)) {
    return refused('A prompt this product ships keeps its label — the help articles name it. Its text is yours.');
  }

  return stored([{
    ...row,
    prompts: (row.prompts ?? []).map((p) => (p.id === promptId ? { ...p, [field]: value } : p)),
  }]);
}

/**
 * Whether this prompt id is one the catalog actually holds for this role.
 *
 * <p>The host writes `&lt;dataDir&gt;/prompts/&lt;id&gt;.md` for it, and `promptFile` already refuses
 * anything that is not a slug — so no id can escape that directory. This is the narrower question:
 * whether the id belongs to the role the message claims, so a webview cannot write over an unrelated
 * role's override by naming it.</p>
 */
export function promptBelongsTo(rows: readonly RoleRow[], roleId: string, promptId: string): boolean {
  if (isShippedPrompt(roleId, promptId)) {
    return true;
  }

  return (rows.find((r) => r.id === roleId)?.prompts ?? []).some((p) => p.id === promptId);
}
