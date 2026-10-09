import { builtInFor } from './roles';
import { textControlFrom } from './textControls';

/**
 * What a message about the review roles MEANS, decided without a host or a page.
 *
 * <p><b>Why this is not in `rolesPage.ts` any more.</b> Two pages posted these messages until E5.1 deleted the first: the Review roles tab, and Reviews ›
 * Roles &amp; prompts on the new Settings page (todo/PLAN_one_model_catalog.md E4.3), whose `roles` messages reach the
 * same editing core (`rolesHost.ts`). Epic 5 deleted the tab (E5.1), and everything the new page and the core still take
 * from it has to live somewhere that survives the deletion — moved out first, prerequisite (b) of that epic, rather than
 * found by a red build on the day the page goes. The contract moved as it was: the vocabulary, the parser, the settle
 * key and the one rule the core reads off a prompt id.</p>
 *
 * <p><b>The edges point one way.</b> This module imports the roles and the text controls and nothing that draws: the
 * tab, the new page's blocks (`rolesBlocks.ts`), the core and the panel all import it, and it imports none of them —
 * least of all `panelView.ts`, which reaches the roles through these modules, so an edge back would close a ring the
 * import-cycle ratchet (`importCycles.test.mjs`) refuses.</p>
 *
 * <p>An unknown message is IGNORED, never refused, for the reason `chatMessages.ts` records: a retained webview can be
 * older or newer than the extension it talks to.</p>
 */

/** The four tabs the Review roles tab is divided into, in the order they are drawn — one per bucket that has a round. */
export const ROLE_TABS: readonly string[] = ['plan', 'code', 'documents', 'feature'];

/** Every message a roles page can send, decided without a host so a test can reach the decision. */
export type RolesCommand =
  | { readonly kind: 'add' }
  | { readonly kind: 'remove'; readonly id: string }
  | { readonly kind: 'edit'; readonly id: string; readonly field: RoleField; readonly value: string | boolean }
  | { readonly kind: 'addPrompt'; readonly id: string }
  | { readonly kind: 'removePrompt'; readonly id: string; readonly promptId: string }
  | { readonly kind: 'editPrompt'; readonly id: string; readonly promptId: string; readonly field: PromptField; readonly value: string }
  | { readonly kind: 'restorePrompt'; readonly id: string; readonly promptId: string }
  | { readonly kind: 'zoom'; readonly delta: number }
  | { readonly kind: 'tone'; readonly delta: number }
  /** Which of the three sections is open — decided on the page, remembered by the host. */
  | { readonly kind: 'tab'; readonly id: string }
  /** A deletion the mirror could not carry, finished locally with the cost accepted. */
  | { readonly kind: 'finishDeletion'; readonly id: string }
  /** The cure for a stand-down, offered beside the deletion it is stuck behind. */
  | { readonly kind: 'reloadWindow' }
  | { readonly kind: 'ignore' };

const IGNORE: RolesCommand = { kind: 'ignore' };

/** The fields a ROLE has. A name this does not know is not a field — `__proto__` included. */
const ROLE_FIELDS = ['name', 'stage', 'programmingTask', 'active'] as const;
type RoleField = (typeof ROLE_FIELDS)[number];

/** The fields a PROMPT has. `text` is the body; it goes to a file rather than into the setting. */
const PROMPT_FIELDS = ['label', 'purpose', 'text'] as const;
type PromptField = (typeof PROMPT_FIELDS)[number];

/** The two fields that are booleans on the wire; everything else arrives as a string. */
const FLAGS: readonly string[] = ['programmingTask', 'active'];

function idOf(value: unknown): string {
  return typeof value === 'string' && value.length > 0 ? value : '';
}

/**
 * The key a typed field settles under, or nothing for a command that is not typing — what `settledWrites` debounces.
 * Moved here from `rolesPanel.ts`, which cannot be built in a test, so the rule is run (E3 code round).
 */
export function rolesFieldOf(command: RolesCommand): string | undefined {
  if (command.kind === 'editPrompt') {
    return `${command.id}/${command.promptId}/${command.field}`;
  }

  return typedEdit(command) ? `${command.id}/${command.field}` : undefined;
}

/**
 * The role fields drawn as a `<select>`: a pick, applied at once like a switch, never settled like typing. Settled, a
 * stage pick waited 300 ms before it started — time the busy mark then counted — skipped the drain that stores pending
 * typing first, and was not redrawn into its new stage (E3 code round, gemini).
 */
const PICKED_ROLE_FIELDS: ReadonlySet<string> = new Set(['stage']);

/** A role edit that is somebody typing: a text value, in a field that is not a pick. */
function typedEdit(command: RolesCommand): command is Extract<RolesCommand, { readonly kind: 'edit' }> {
  return command.kind === 'edit' && typeof command.value === 'string' && !PICKED_ROLE_FIELDS.has(command.field);
}

/**
 * One message from the page, as a command — or `ignore`.
 *
 * <p>Pure, and separate from the host, so every shape a webview can post is reachable from a test. The message type is
 * looked up in a table of its OWN keys (`Object.hasOwn`), and a field is checked against a LIST of names rather than
 * with `in`, so no key of `Object.prototype` can be either — the arrangement `commandsMessages.commandEdit` has.</p>
 *
 * <p>A table rather than the chain of `if`s this was in `rolesPage.ts`: the move put it in a module held to the
 * complexity rule, and the chain was one of the file's recorded exceptions. Every message reads as it did — the
 * parser's own tests (`rolesPlace.test.ts`, `rolesPlaceColours.test.ts`, `featureStageOnTheRolesPlace.test.ts` since E5.1
 * moved them onto the new page) run against this one.</p>
 */
export function roleEdit(message: unknown): RolesCommand {
  const said = recordOf(message);
  const type = said['type'];

  return typeof type === 'string' && Object.hasOwn(READERS, type) ? (READERS[type] ?? ignored)(said) : IGNORE;
}

const ignored = (): RolesCommand => IGNORE;

function recordOf(message: unknown): Record<string, unknown> {
  return typeof message === 'object' && message !== null ? message as Record<string, unknown> : {};
}

/** The text size or tone, read by the controls every page shares. */
const textControl = (said: Record<string, unknown>): RolesCommand => textControlFrom(said) ?? IGNORE;

const READERS: Readonly<Record<string, (said: Record<string, unknown>) => RolesCommand>> = {
  zoom: textControl,
  tone: textControl,
  add: () => ({ kind: 'add' }),
  reloadWindow: () => ({ kind: 'reloadWindow' }),
  // Through `idOf`, like every other id off this page: the value reaches a file path, and a page
  // that can be older or newer than the extension it talks to is not a source this side trusts.
  finishDeletion: (said) => withId(said, (id) => ({ kind: 'finishDeletion', id })),
  tab: (said) => tabCommand(idOf(said['id'])),
  remove: (said) => withId(said, (id) => ({ kind: 'remove', id })),
  addPrompt: (said) => withId(said, (id) => ({ kind: 'addPrompt', id })),
  removePrompt: (said) => withPrompt(said, (id, promptId) => ({ kind: 'removePrompt', id, promptId })),
  restorePrompt: (said) => withPrompt(said, (id, promptId) => ({ kind: 'restorePrompt', id, promptId })),
  editPrompt: (said) => withPrompt(said, (id, promptId) => promptEditOf(id, promptId, said)),
  edit: (said) => withId(said, (id) => roleFieldEdit(id, said)),
};

/** The command a message about one role makes — or `ignore` when it names no role. */
function withId(said: Record<string, unknown>, make: (id: string) => RolesCommand): RolesCommand {
  const id = idOf(said['id']);

  return id.length === 0 ? IGNORE : make(id);
}

/** The command a message about one prompt of one role makes — or `ignore` when it names no role or no prompt. */
function withPrompt(said: Record<string, unknown>, make: (id: string, promptId: string) => RolesCommand): RolesCommand {
  const id = idOf(said['id']);
  const promptId = idOf(said['promptId']);

  return id.length === 0 || promptId.length === 0 ? IGNORE : make(id, promptId);
}

/**
 * A press on a tab. Against the LIST, so a word this page has no section for is never stored. The drawing side
 * normalises too — two halves, neither trusting the other, because a retained webview can be older or newer than the
 * extension it is talking to.
 */
function tabCommand(wanted: string): RolesCommand {
  return ROLE_TABS.includes(wanted) ? { kind: 'tab', id: wanted } : IGNORE;
}

function isRoleField(field: unknown): field is RoleField {
  return typeof field === 'string' && (ROLE_FIELDS as readonly string[]).includes(field);
}

/** An edit of one of a role's own fields: a flag arrives as a boolean, everything else as a string. */
function roleFieldEdit(id: string, said: Record<string, unknown>): RolesCommand {
  const field = said['field'];
  if (!isRoleField(field)) {
    return IGNORE;
  }

  const value = said['value'];
  const wanted = FLAGS.includes(field) ? 'boolean' : 'string';

  return typeof value === wanted ? { kind: 'edit', id, field, value: value as string | boolean } : IGNORE;
}

/** An edit of one of a prompt's fields — every one of them a string. */
function promptEditOf(id: string, promptId: string, said: Record<string, unknown>): RolesCommand {
  const field = said['field'];
  const value = said['value'];
  if (typeof field !== 'string' || !(PROMPT_FIELDS as readonly string[]).includes(field) || typeof value !== 'string') {
    return IGNORE;
  }

  return { kind: 'editPrompt', id, promptId, field: field as PromptField, value };
}

/** Whether a prompt is one this product ships — its text is embedded, so it cannot be deleted. */
export function isShippedPrompt(roleId: string, promptId: string): boolean {
  return builtInFor(roleId)?.prompts.some((p) => p.id === promptId) === true;
}
