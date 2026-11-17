import type { Inspected } from './modelKeys';

/**
 * The skip list of the terminal: does the person's own `terminal.integrated.commandsToSkipShell` hide the terminal keys of CoAI,
 * and may they be added (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, E3.S3).
 *
 * <p><b>Why it exists.</b> The manifest puts the commands bound under `terminalFocus` into the DEFAULT of the setting, so
 * Ctrl+Alt+A and Ctrl+Alt+G in a terminal reach VS Code and not the shell. A list of the person's own hides that default, and
 * the chord then goes to the shell. This module reads the layers of the setting, decides whether anyone is told, and — only
 * after a press of **Add** — appends the missing ids to the USER list. Every effect is a seam, so the whole road runs under test.</p>
 *
 * <p><b>The write is the person's consent and nothing else.</b> It appends to a list the person already has, only in the user
 * layer, only the ids that are missing, never reorders or de-duplicates their entries, re-reads the list after the press and
 * reads it back after the write. A workspace or folder list is never written: the person is told once which level to fix.</p>
 */

/** The commands the manifest binds under `terminalFocus`, in the order of its `configurationDefaults` list (S3-T9 holds them equal). */
export const SKIP_SHELL_IDS = ['coai.askFromTerminal', 'coai.goToFromTerminal'] as const;

/** The one button of the offer. */
export const ADD = 'Add';

/** Where a workspace or folder list was found. */
export type RepositoryLayer = 'workspace' | 'folder';

/** What the layers of the setting say, read in VS Code's order: folder, workspace, user, default. */
export type SkipShellState =
  /** A folder or workspace value is set, so it wins over the person's own list; `missing` is over THAT list. */
  | { readonly kind: 'repository'; readonly layer: RepositoryLayer; readonly missing: readonly string[] }
  /** The user value is set and is not a list of strings: nothing is offered and nothing is written. */
  | { readonly kind: 'unreadable' }
  /** The person's own list. */
  | { readonly kind: 'user'; readonly list: readonly string[]; readonly missing: readonly string[] }
  /** No value of the person's: the default itself. A default that lacks the ids is a named limit (plan O4): nothing is offered. */
  | { readonly kind: 'default'; readonly missing: readonly string[] };

const isListOfStrings = (value: unknown): value is readonly string[] =>
  Array.isArray(value) && value.every((one) => typeof one === 'string');

/** The ids a list does not carry and the person has not removed with a leading `-`: a `-id` entry is their choice and is kept. */
const missingFrom = (value: unknown, ids: readonly string[]): readonly string[] =>
  isListOfStrings(value) ? ids.filter((id) => !value.includes(id) && !value.includes(`-${id}`)) : [...ids];

/** The layers a repository can set, in VS Code's order: the folder wins over the workspace. */
const REPOSITORY_LAYERS = [['workspaceFolderValue', 'folder'], ['workspaceValue', 'workspace']] as const;

const repositoryState = (inspected: Inspected, ids: readonly string[]): SkipShellState | undefined => {
  const found = REPOSITORY_LAYERS.find(([key]) => inspected[key] !== undefined);

  return found === undefined ? undefined : { kind: 'repository', layer: found[1], missing: missingFrom(inspected[found[0]], ids) };
};

const ownState = (inspected: Inspected, ids: readonly string[]): SkipShellState => {
  const value = inspected.globalValue;
  if (value === undefined) {
    return { kind: 'default', missing: inspected.defaultValue === undefined ? [] : missingFrom(inspected.defaultValue, ids) };
  }

  return isListOfStrings(value) ? { kind: 'user', list: value, missing: missingFrom(value, ids) } : { kind: 'unreadable' };
};

/** The layers of the setting, over the `inspect()` shape. */
export function skipShellState(inspected: Inspected | undefined, ids: readonly string[]): SkipShellState {
  const layers = inspected ?? {};

  return repositoryState(layers, ids) ?? ownState(layers, ids);
}

/**
 * The person's list with the missing ids after it: a NEW array, every entry of `list` in its order and unchanged (its own
 * duplicates too), then each missing id once in {@link SKIP_SHELL_IDS} order, skipping one that is already there.
 */
export function withIds(list: readonly string[], missing: readonly string[]): readonly string[] {
  return [...list, ...SKIP_SHELL_IDS.filter((id) => missing.includes(id) && !list.includes(id))];
}

/** What the person answered, for one id. `layer` only on `told`. */
export interface SkipShellAnswer {
  readonly id: string;
  readonly answer: 'added' | 'dismissed' | 'told';
  readonly layer?: RepositoryLayer;
  readonly askedUtc: string;
}

const ANSWERS: readonly string[] = ['added', 'dismissed', 'told'];

const LAYER_NAMES: readonly unknown[] = [undefined, 'workspace', 'folder'];

const hasRowFields = (row: Record<string, unknown>): boolean =>
  typeof row.id === 'string' && typeof row.askedUtc === 'string' && ANSWERS.includes(String(row.answer)) && LAYER_NAMES.includes(row.layer);

const isRow = (value: unknown): value is SkipShellAnswer => typeof value === 'object' && value !== null && hasRowFields(value as Record<string, unknown>);

/** The remembered rows. A memory that is not a list of valid rows reads as empty, so the person is asked again — never a write without a press. */
const rowsOf = (remembered: unknown): readonly SkipShellAnswer[] =>
  Array.isArray(remembered) && remembered.every(isRow) ? remembered : [];

/** The missing ids with no remembered `added` or `dismissed` row. */
export function offered(missing: readonly string[], remembered: unknown): readonly string[] {
  const rows = rowsOf(remembered).filter((row) => row.answer !== 'told');

  return missing.filter((id) => !rows.some((row) => row.id === id));
}

/** The missing ids with no `told` row for that layer. */
export function toldAbout(missing: readonly string[], layer: RepositoryLayer, remembered: unknown): readonly string[] {
  const rows = rowsOf(remembered).filter((row) => row.answer === 'told' && row.layer === layer);

  return missing.filter((id) => !rows.some((row) => row.id === id));
}

/** The same answer for the same id (and layer, for `told`): one slot. `added` and `dismissed` share a slot, as an id has one answer to the offer. */
const sameSlot = (one: SkipShellAnswer, other: SkipShellAnswer): boolean =>
  one.id === other.id && (one.answer === 'told' ? other.answer === 'told' && one.layer === other.layer : other.answer !== 'told');

/** A new list: the remembered rows of ids still in {@link SKIP_SHELL_IDS}, then one row per id for this answer, replacing an older row of its slot. */
export function rememberedWith(
  remembered: unknown,
  ids: readonly string[],
  answer: SkipShellAnswer['answer'],
  now: Date,
  layer?: RepositoryLayer,
): readonly SkipShellAnswer[] {
  const fresh = ids.map((id): SkipShellAnswer => ({ id, answer, ...(layer === undefined ? {} : { layer }), askedUtc: now.toISOString() }));

  return [
    ...rowsOf(remembered).filter((row) => (SKIP_SHELL_IDS as readonly string[]).includes(row.id) && !fresh.some((one) => sameSlot(one, row))),
    ...fresh,
  ];
}

const named = (ids: readonly string[]): string => ids.join(' and ');

/** The offer: only the ids that would be added are named. */
export const askTitle = (ids: readonly string[]): string =>
  'Your own setting terminal.integrated.commandsToSkipShell hides the terminal keys of CoAI, so the shell gets them. '
  + `Add ${named(ids)} to the end of that list in your user settings?`;

/** The notice with no button for a workspace or folder list. It names the level and says what CoAI does not do. */
export const toldTitle = (layer: RepositoryLayer, ids: readonly string[]): string => {
  const level = layer === 'workspace' ? 'workspace settings' : 'folder settings';

  return `Your ${level} set terminal.integrated.commandsToSkipShell, and that list hides the terminal keys of CoAI, so the shell gets them. `
    + `Add ${named(ids)} to that list in your ${level}. CoAI does not change workspace or folder settings.`;
};

/** The write failed. */
export const notAddedSentence = (ids: readonly string[], reason: string): string =>
  // VS Code's own write errors already end with a period; one is stripped so the sentence does not end a clause with two.
  `${named(ids)} could not be added to terminal.integrated.commandsToSkipShell: ${reason.replace(/\.$/u, '')}. Nothing else in the setting was changed.`;

/** The write answered, and the setting does not show the ids when it is read back. */
export const notReadBackSentence = (ids: readonly string[]): string =>
  `${named(ids)} were written to terminal.integrated.commandsToSkipShell, but the setting does not show them when it is read back. Check your user settings.`;

/** Where the answers are kept: the host's `globalState`, under one key of its own. */
export interface SkipShellMemory {
  read(): unknown;
  write(next: readonly SkipShellAnswer[]): PromiseLike<void>;
}

/** Every effect of the road. */
export interface SkipShellSeams {
  /** The layers of `terminal.integrated.commandsToSkipShell`. */
  readonly inspect: () => Inspected | undefined;
  /** The offer with its one button: the answer, or `undefined` when it was closed. */
  readonly ask: (ids: readonly string[]) => PromiseLike<string | undefined>;
  /** The notice with no button for a workspace or folder list. */
  readonly tell: (layer: RepositoryLayer, ids: readonly string[]) => PromiseLike<void>;
  /** Writes the USER value. The host's target is the user settings; nothing else is ever a target. */
  readonly update: (next: readonly string[]) => PromiseLike<void>;
  readonly memory: SkipShellMemory;
  /** A write that did not take, said in a sentence. */
  readonly failed: (sentence: string) => PromiseLike<void>;
  readonly now: () => Date;
}

/** What one check did. */
export type SkipShellOutcome = 'nothing' | 'told' | 'dismissed' | 'added' | 'failed';

const remember = (
  seams: SkipShellSeams,
  ids: readonly string[],
  answer: SkipShellAnswer['answer'],
  layer?: RepositoryLayer,
): PromiseLike<void> => seams.memory.write(rememberedWith(seams.memory.read(), ids, answer, seams.now(), layer));

async function tellAboutRepository(seams: SkipShellSeams, layer: RepositoryLayer, missing: readonly string[]): Promise<SkipShellOutcome> {
  const ids = toldAbout(missing, layer, seams.memory.read());
  if (ids.length === 0) {
    return 'nothing';
  }
  await seams.tell(layer, ids);
  await remember(seams, ids, 'told', layer);

  return 'told';
}

const reasonOf = (error: unknown): string => (error instanceof Error ? error.message : String(error));

/** Whether the user value holds every one of the ids, as the setting shows it now. */
const userListHolds = (inspected: Inspected | undefined, ids: readonly string[]): boolean => {
  const state = skipShellState(inspected, ids);

  return state.kind === 'user' && state.missing.length === 0;
};

/** Writes `ids` after the person's list and reads them back; `byHand` are the asked ids the person added themselves while the notice was open. */
async function writeAndReadBack(
  seams: SkipShellSeams,
  list: readonly string[],
  ids: readonly string[],
  byHand: readonly string[],
): Promise<SkipShellOutcome> {
  try {
    await seams.update(withIds(list, ids));
  } catch (error) {
    await seams.failed(notAddedSentence(ids, reasonOf(error)));

    return 'failed';
  }
  if (!userListHolds(seams.inspect(), ids)) {
    await seams.failed(notReadBackSentence(ids));

    return 'failed';
  }
  await remember(seams, [...byHand, ...ids], 'added');

  return 'added';
}

/** After the press: the list as it is NOW, never the one read before the prompt. */
async function writeAfterPress(seams: SkipShellSeams, asked: readonly string[]): Promise<SkipShellOutcome> {
  const fresh = skipShellState(seams.inspect(), SKIP_SHELL_IDS);
  if (fresh.kind !== 'user') {
    return 'nothing';
  }
  const ids = fresh.missing.filter((id) => asked.includes(id));
  // The ids the person put there themselves while the notice was open are there: remembered as added, never offered again.
  const byHand = asked.filter((id) => !fresh.missing.includes(id));
  if (ids.length > 0) {
    return writeAndReadBack(seams, fresh.list, ids, byHand);
  }
  await remember(seams, byHand, 'added');

  return 'added';
}

async function offerAdd(seams: SkipShellSeams, ids: readonly string[]): Promise<SkipShellOutcome> {
  if ((await seams.ask(ids)) !== ADD) {
    await remember(seams, ids, 'dismissed');

    return 'dismissed';
  }

  return writeAfterPress(seams, ids);
}

/** What to offer the person: only their own user list is ever offered an Add, and only the ids they have not answered. */
const toOffer = (seams: SkipShellSeams, state: SkipShellState): readonly string[] =>
  state.kind === 'user' ? offered(state.missing, seams.memory.read()) : [];

/** The one road: read the layers, tell or offer, and write only after the press. */
export async function checkSkipShell(seams: SkipShellSeams): Promise<SkipShellOutcome> {
  const state = skipShellState(seams.inspect(), SKIP_SHELL_IDS);
  if (state.kind === 'repository') {
    return tellAboutRepository(seams, state.layer, state.missing);
  }
  const ids = toOffer(seams, state);

  return ids.length === 0 ? 'nothing' : offerAdd(seams, ids);
}

/**
 * {@link checkSkipShell} behind ONE latch: a trigger while a check is running never starts a second check beside it, so there is never
 * more than one notification at a time (S3-T8). It is not DROPPED either (S3-T14): whatever changed meanwhile — a workspace list set while
 * the offer is open — is read by ONE follow-up check, however many triggers arrive, which starts only after the running check has settled
 * and so never while a notice is open (S3-T14b). The first caller gets the first check's outcome. The host calls this for the activation
 * and for every change of the setting.
 */
export function latchedCheck(seams: SkipShellSeams): () => Promise<SkipShellOutcome> {
  let running = false;
  let again = false;
  const once = async (): Promise<SkipShellOutcome> => {
    running = true;
    try {
      return await checkSkipShell(seams);
    } finally {
      running = false;
    }
  };

  return async () => {
    if (running) {
      again = true;

      return 'nothing';
    }
    const outcome = await once();
    while (again) {
      again = false;
      await once();
    }

    return outcome;
  };
}

/** What the focus gate needs of the window. */
export interface FocusSeams {
  /** Whether this window is the one the person works in. */
  readonly focused: () => boolean;
  /** Calls `run` at the first moment the window is focused. The host drops its listener after that call; `run` is safe to call again. */
  readonly whenFocused: (run: () => void) => void;
}

/**
 * The startup and change-of-setting check runs only in a FOCUSED window: when several windows are restored at once, only the one the person
 * works in asks. An unfocused window waits for its first focus (one wait at a time, however many triggers arrive meanwhile) and then runs
 * `check` once — through the same latch and the same memory, so a window focused after the person answered elsewhere finds the answer, or
 * the ids already in the list, and asks nothing.
 *
 * @param onError where a failure of the deferred run goes: nobody awaits it
 */
export function gatedByFocus(
  check: () => Promise<SkipShellOutcome>,
  seams: FocusSeams,
  onError: (reason: unknown) => void,
): () => Promise<SkipShellOutcome> {
  let waiting = false;

  return async () => {
    if (seams.focused()) {
      return check();
    }
    if (!waiting) {
      waiting = true;
      let spent = false;
      seams.whenFocused(() => {
        if (!spent) {
          spent = true;
          waiting = false;
          check().catch(onError);
        }
      });
    }

    return 'nothing';
  };
}
