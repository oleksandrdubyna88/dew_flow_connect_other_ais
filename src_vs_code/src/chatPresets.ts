import { Vendor } from './vendors';
import { Runtime } from './models';
/**
 * The two lists a person builds: named prompts, and named models.
 *
 * <p>Pure, over whatever `settings.json` actually holds — no `vscode` handle, so every rule below is
 * a unit test rather than a claim. That matters more here than in most settings modules for a reason
 * this feature is the first to meet: a preset is something a person COMPOSES, not something they
 * pick from a catalog this product shipped. `prompts.ts` is a list we wrote and they override;
 * this is a list they wrote, and it is the only place in the extension where losing a value means
 * losing their words.</p>
 *
 * <p><b>So a bad row is dropped and the rest survives.</b> Refusing the whole list because one entry
 * was mistyped would take away every prompt somebody had saved for the sake of the one they got
 * wrong — and `settings.json` is a file people edit by hand, so a mistyped entry is not an exotic
 * case. Nothing here throws.</p>
 *
 * <p><b>These are person-level settings and do not cross to the server</b>, the same two decisions
 * `chatSettings.ts` records for the four it owns: they are absent from `envBlock` and from the
 * settings mirrored into `coai-mcp`, and they are not in `OVERLAID_SETTINGS`. A prompt library is
 * exactly the kind of thing that would otherwise start travelling.</p>
 */

/** How much of a name fits on a button in a row of buttons, before the row stops being a row. */
const NAME_LIMIT = 60;

export interface PromptPreset {
  readonly id: string;
  readonly name: string;
  readonly text: string;
  /** The one a trigger that sends BY ITSELF uses. Exactly one list-wide; the first claim wins. */
  readonly main: boolean;
}

/**
 * A saved model — and the WHOLE of what the chat needs to run it.
 *
 * <p><b>The chat is independent of the review gate, and this type is where that became true.</b> A
 * preset used to name a reviewer ROW and borrow its runtime, its CLI path and its endpoint, so a
 * reviewer switched off took the chat with it and every picker read as a list of reviewers. Said by
 * the operator five times before it was built: *"это полностью независимый функционал этот чат…
 * чат никак не должен трогать ревьюы"*.</p>
 *
 * <p>A Team-server preset is the one that still points OUTWARD, and at the right thing: it names the
 * server entry and the vendor on it, and the address, the token and the allowlist come from the
 * *Team servers* section — which is its own configuration and has nothing to do with reviewers
 * either.</p>
 */
export interface ModelPreset {
  readonly id: string;
  readonly name: string;
  /** The vendor this preset runs on — a runtime, not somebody's reviewer. */
  readonly runtime: Runtime;
  /** Which of that vendor's models. Empty means the vendor's own default. */
  readonly model: string;
  /**
   * The one a NEW conversation opens on. Exactly one list-wide; the first claim wins.
   *
   * <p>The rule the prompt list already keeps, for the same reason: a capture has to open on some
   * model, and "whichever id was saved last" is a choice nobody made — the operator asked for the
   * prompt list's tick in front of the models as well, and for the one thing it means.</p>
   */
  readonly main: boolean;
  /** What the composer opens with when this preset is chosen. Optional, and usually absent. */
  readonly startingPrompt?: string | undefined;
  /** Where that vendor's CLI is. Empty = look it up on PATH. */
  readonly executablePath: string;
  /** An OpenAI-compatible endpoint, for a vendor riding the codex runtime. Empty = the CLI's own. */
  readonly baseUrl: string;
  /** For a `remote` preset: which entry in the *Team servers* section answers it. */
  readonly teamServerId?: string | undefined;
  /** For a `remote` preset: the vendor name THAT SERVER knows — `codex`, not the preset's own id. */
  readonly remoteVendor?: string | undefined;
}

/** A string from a file a person edits: trimmed, and empty for anything that is not one. */
function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

/** A record, or nothing — `settings.json` can hold a string, a number or a null in an array. */
function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/**
 * An id for every preset, unique across the list.
 *
 * <p>A person editing `settings.json` by hand will not write one, and two presets sharing an id
 * makes a click ambiguous — the buttons name the preset they chose, and the host looks it up. So a
 * missing or repeated id is replaced by a positional one, which is stable for as long as the list
 * is not reordered and is regenerated the moment it is.</p>
 */
function withId(candidate: string, index: number, taken: Set<string>): string {
  const id = candidate.length > 0 && !taken.has(candidate) ? candidate : `preset-${index + 1}`;
  taken.add(id);

  return id;
}

/**
 * The prompt presets, and the migration of the single prompt that came before them.
 *
 * @param saved what `coai.chatPromptPresets` holds
 * @param legacy what `coai.chatPrompt` holds — a single string a person has been editing since the
 *   chat shipped. It becomes their first preset, ticked as the main one, **only when they have no
 *   presets at all**: somebody who has a list has already moved, and re-adding the old string on
 *   every read would resurrect a prompt they deleted.
 */
export function chatPromptPresetsFrom(saved: unknown, legacy = ''): readonly PromptPreset[] {
  const rows = Array.isArray(saved) ? saved : [];
  const taken = new Set<string>();
  const presets = rows.flatMap((row, index): PromptPreset[] => {
    const one = record(row);
    if (one === undefined) {
      return [];
    }
    const name = text(one['name']).slice(0, NAME_LIMIT);
    // The TEXT is not truncated: a name has a width to respect and a prompt has meaning to keep.
    const body = typeof one['text'] === 'string' ? one['text'].trim() : '';

    return name.length === 0 || body.length === 0
      ? []
      : [{ id: withId(text(one['id']), index, taken), name, text: body, main: one['main'] === true }];
  });

  if (presets.length === 0) {
    const carried = legacy.trim();

    return carried.length === 0
      ? []
      : [{ id: 'preset-1', name: nameFor(carried), text: carried, main: true }];
  }

  return onlyOneMain(presets);
}

/**
 * A name for the migrated prompt, since the setting it came from never had one.
 *
 * <p>Its own first words, so a person recognises it on a button as the thing they wrote — a generic
 * label would make their own prompt look like something this product put there.</p>
 */
function nameFor(prompt: string): string {
  const firstLine = prompt.split('\n')[0] ?? prompt;

  return firstLine.length <= NAME_LIMIT ? firstLine : `${firstLine.slice(0, NAME_LIMIT - 1)}…`;
}

/** Exactly one main row, and the first claim wins — two would make the choice arbitrary. */
function onlyOneMain<T extends { readonly main: boolean }>(presets: readonly T[]): readonly T[] {
  const first = presets.findIndex((preset) => preset.main);

  return presets.map((preset, index) => ({ ...preset, main: index === first }));
}

/**
 * Which prompt a trigger that sends by itself uses.
 *
 * <p>The ticked one, or the FIRST when nothing is ticked: while there is any prompt at all there
 * must be one to send, or the keybinding path has nothing to say. Nothing at all when the list is
 * empty, which the caller handles rather than this inventing a prompt nobody wrote.</p>
 */
export function mainPrompt(presets: readonly PromptPreset[]): PromptPreset | undefined {
  return presets.find((preset) => preset.main) ?? presets[0];
}

/**
 * The model presets. A row with no VENDOR is not one: the vendor is what answers.
 *
 * @param rows the reviewer rows, passed in for the MIGRATION and for nothing else. A preset written
 *   before the chat had its own configuration names a reviewer row in `provider`; its runtime, CLI
 *   path, endpoint and Team-server fields are copied out of that row once, and after the next write
 *   the chat never asks about reviewers again. Somebody who built three presets should not lose them
 *   to an architecture decision they did not make. A preset naming a row that is gone cannot be run
 *   and is dropped, like any other row this reader refuses.
 */
export function chatModelPresetsFrom(saved: unknown, reviewerRows: readonly Vendor[] = []): readonly ModelPreset[] {
  const rows = Array.isArray(saved) ? saved : [];
  const taken = new Set<string>();

  return onlyOneMain(rows.flatMap((row, index): ModelPreset[] => {
    const one = record(row);
    if (one === undefined) {
      return [];
    }
    const name = text(one['name']).slice(0, NAME_LIMIT);
    // The vendor, from the preset itself — or carried out of the reviewer row it used to name.
    const carried = text(one['runtime']).length > 0 ? undefined : reviewerRows.find((row) => row.id === text(one['provider']));
    const runtime = text(one['runtime']).length > 0 ? text(one['runtime']) : (carried?.runtime ?? '');
    if (name.length === 0 || runtime.length === 0) {
      return [];
    }
    const starting = typeof one['startingPrompt'] === 'string' ? one['startingPrompt'].trim() : '';
    const server = text(one['teamServerId']).length > 0 ? text(one['teamServerId']) : (carried?.teamServerId ?? '');
    const onServer = text(one['remoteVendor']).length > 0 ? text(one['remoteVendor']) : (carried?.remoteVendor ?? '');

    return [{
      id: withId(text(one['id']), index, taken),
      name,
      main: one['main'] === true,
      runtime: runtime as Runtime,
      model: text(one['model']).length > 0 ? text(one['model']) : (carried?.model ?? ''),
      executablePath: text(one['executablePath']).length > 0 ? text(one['executablePath']) : (carried?.executablePath ?? ''),
      baseUrl: text(one['baseUrl']).length > 0 ? text(one['baseUrl']) : (carried?.baseUrl ?? ''),
      ...(server.length > 0 ? { teamServerId: server } : {}),
      ...(onServer.length > 0 ? { remoteVendor: onServer } : {}),
      ...(starting.length > 0 ? { startingPrompt: starting } : {}),
    }];
  }));
}

/**
 * Which model a NEW conversation opens on, when the list says.
 *
 * <p>The TICKED one and nothing else — where `mainPrompt` falls back to the first row, this does
 * not, and the difference is deliberate. `coai.chatModel` is a value a person sets in the panel,
 * and a list nobody has ticked must not silently overrule it. "Nothing ticked" is answered further
 * out by `legacyPick`, which already turns a saved id naming nothing into the first provider.</p>
 */
export function mainModel(presets: readonly ModelPreset[]): ModelPreset | undefined {
  return presets.find((preset) => preset.main);
}

/**
 * What a re-ask would consist of, or nothing when there is nothing to re-ask.
 *
 * <p>Entry 24 of the operator's list: *"maybe I don't like gemini's answer and want to switch to
 * Fable. If I switch the model and press Enter with an empty box — take the previous context (except
 * the last answer) and feed it to the new model."*</p>
 *
 * <p><b>Except the last answer</b>, and the reason is sound: an answer somebody rejected, handed to
 * the next model, is a model being asked to agree with it. The QUESTION is kept — it is what they
 * want answered again — and everything before it stays, because that is the conversation the answer
 * was given in.</p>
 *
 * <p>On offer only when the model has CHANGED since that answer. Pressing Enter on an empty box with
 * the same model chosen is the gesture doing nothing, which is what it has always done.</p>
 */
export function reaskFrom(
  messages: readonly { readonly role: 'you' | 'model'; readonly text: string; readonly model?: { readonly id: string } | undefined }[],
  chosen: string,
): { readonly said: readonly { readonly role: 'you' | 'model'; readonly text: string }[]; readonly question: string } | undefined {
  const last = messages.length - 1;
  if (last < 1 || messages[last]?.role !== 'model' || chosen.length === 0) {
    return undefined;
  }
  const answered = messages[last]?.model?.id ?? '';
  if (answered.length === 0 || answered === chosen) {
    return undefined;
  }
  const question = messages[last - 1];
  if (question?.role !== 'you' || question.text.trim().length === 0) {
    return undefined;
  }

  return {
    said: messages.slice(0, last - 1).map((message) => ({ role: message.role, text: message.text })),
    question: question.text,
  };
}

/** The preset a button named, or nothing — a click naming an id that is gone chooses nothing. */
export function presetById<T extends { readonly id: string }>(
  presets: readonly T[],
  id: string,
): T | undefined {
  return id.length === 0 ? undefined : presets.find((preset) => preset.id === id);
}

/**
 * A new row for one of the two lists, in a shape that list's own reader will KEEP.
 *
 * <p><b>That is the whole point of it living here.</b> It used to sit in the panel host beside the
 * write, and it seeded a model row with `provider: ''` — the one field `chatModelPresetsFrom`
 * refuses, because a preset that names no row names nothing that can answer. So *Add a model* wrote
 * a row, the page re-read the list, the reader dropped it, and the screen showed what it showed
 * before. Every press left a dead row in `settings.json` that nothing could display, edit or remove.
 * Next to its reader, that cannot happen without a test going red.</p>
 *
 * @param providerId the row a model preset will answer through — required for the preset to exist at
 *   all, so the caller resolves one before offering to create it rather than writing an empty string
 *   and hoping.
 */
function freshId(taken: readonly { readonly id: string }[]): string {
  return `preset-${Date.now().toString(36)}-${taken.length + 1}`;
}

export function freshPromptRow(taken: readonly { readonly id: string }[]): SavedRow {
  return { id: freshId(taken), name: 'New prompt', text: 'Explain', main: false };
}

/**
 * @param providerId the row this preset answers through. REQUIRED, and that is the second half of
 *   the fix: one factory with a DEFAULTED provider left the original defect available to the next
 *   caller who forgot the argument. `model` is deliberately empty — the documented "whatever the row
 *   is set to" — because seeding one would pick a model on somebody's behalf.
 */
export function freshModelRow(
  taken: readonly { readonly id: string }[],
  vendor: ChatVendorChoice,
  modelId = '',
  name = 'New model',
): SavedRow {
  return {
    id: freshId(taken),
    name: name.trim().length > 0 ? name.trim() : 'New model',
    // NOT main. A row added to a list that already has one would otherwise take the tick from it by
    // arriving — and adding a model is not a decision about which one a capture opens on.
    main: false,
    runtime: vendor.runtime,
    model: modelId,
    executablePath: vendor.executablePath ?? '',
    baseUrl: vendor.baseUrl ?? '',
    ...(vendor.teamServerId !== undefined ? { teamServerId: vendor.teamServerId } : {}),
    ...(vendor.remoteVendor !== undefined ? { remoteVendor: vendor.remoteVendor } : {}),
  };
}

/** What the wizard decided a preset runs on — the vendor, and how to reach it. */
export interface ChatVendorChoice {
  readonly runtime: Runtime;
  readonly executablePath?: string | undefined;
  readonly baseUrl?: string | undefined;
  readonly teamServerId?: string | undefined;
  readonly remoteVendor?: string | undefined;
}

/**
 * Whether a row is the LITTER the empty-provider seed wrote — a rule of its own, deliberately.
 *
 * <p>The obvious implementation was `chatModelPresetsFrom([row]).length === 0`: whatever the reader
 * refuses. The code round showed why that is wrong as a cleanup rule (codex, Major): it makes the
 * CURRENT reader's acceptance the authority on what may be deleted, so an older build opening a
 * newer settings file would permanently remove rows it merely does not understand yet. What may be
 * deleted is what this product wrote by mistake, and that has a signature — a `provider` written as
 * an empty string, which no surface has ever been able to produce any other way. A row that is
 * strange for any other reason, or has no provider FIELD at all, is left where it is.</p>
 */
export function deadModelRow(row: unknown): boolean {
  const one = record(row);

  return one !== undefined && one['provider'] === '';
}

/** The rows a settings list holds, before any reader has had an opinion about them. */
export type SavedRow = Record<string, unknown>;

/**
 * What the model list should hold after *Add a model* — or nothing, when it must refuse.
 *
 * <p>Here rather than in the panel host, because the plan round said the obvious thing: a test of
 * the seed passes while the command is still wired to the old one. The rule this has to respect is
 * two lines up, and both are now one file.</p>
 *
 * <p>It prunes as it writes. A row the reader discards cannot be shown, edited or removed on any
 * surface, so it is not a draft — it is what the empty-provider seed left behind, and the write that
 * adds a real row is the moment to be rid of it.</p>
 *
 * @param providerId the row the new preset answers through. EMPTY means nothing configured can chat,
 *   and the answer is `undefined`: no write at all, so the caller says why instead of leaving
 *   something invisible in the file.
 */
export function modelRowsAfterAdd(
  rows: readonly SavedRow[],
  vendor: ChatVendorChoice | undefined,
  modelId = '',
  name = 'New model',
  startingPrompt = '',
): readonly SavedRow[] | undefined {
  if (vendor === undefined) {
    return undefined;
  }
  const kept = rows.filter((row) => !deadModelRow(row));
  const fresh = freshModelRow(kept as { id: string }[], vendor, modelId, name);

  return [...kept, startingPrompt.trim().length > 0 ? { ...fresh, startingPrompt: startingPrompt.trim() } : fresh];
}

/**
 * What a preset list should hold after the `main` box on one row was ticked or unticked.
 *
 * <p>BOTH lists, one implementation: the prompts' tick and the models' tick are the same rule about
 * the same field, and two copies of it would be two chances to disagree about what unticking the
 * last one does.</p>
 *
 * <p><b>Unticking the only main one is refused</b>, which makes the box a radio in everything but
 * appearance. `onlyOneMain` marks nothing when nothing is ticked and `mainPrompt` then falls back to
 * the FIRST prompt — so a list with no tick shows none while the first one is quietly what a capture
 * sends. A control that cannot be turned off is honest; one that turns off and changes nothing is
 * not. (gemini, the plan round.)</p>
 */
export function rowsAfterMain(
  rows: readonly SavedRow[],
  id: string,
  ticked: boolean,
): readonly SavedRow[] {
  // An id naming no row rewrites NOTHING. A webview message can arrive after the prompt it names was
  // removed in another window, and mapping over the list would then set every flag to false: no tick
  // on the page while `mainPrompt` falls back to the first prompt. The list ITSELF comes back, so the
  // caller can tell a no-op from a change and skip a write that would say the same thing.
  // (codex and gemini, the code round, from two directions.)
  if (!rows.some((row) => row['id'] === id)) {
    return rows;
  }
  // What the list WOULD hold. Unticking the only main one is refused — `onlyOneMain` marks nothing
  // when nothing is ticked and `mainPrompt` then falls back to the first prompt, so the page would
  // show no tick while the first one is quietly what a capture sends.
  const wanted = (row: SavedRow): boolean => (ticked
    ? row['id'] === id
    : (row['id'] === id ? !rows.some((other) => other['id'] !== id && other['main'] === true) : row['main'] === true));

  // The list ITSELF when every flag already says that. The host writes only when the reference moved,
  // so re-ticking the row that is already main, or unticking one that was never main, stops being a
  // configuration event that says what the file said. (gemini and local, the code round.)
  return rows.every((row) => (row['main'] === true) === wanted(row))
    ? rows
    : rows.map((row) => ({ ...row, main: wanted(row) }));
}

/**
 * What a preset looks like to everything that RUNS a conversation.
 *
 * <p>The launcher, the session, the price and the Team-server client were all written against a
 * `Vendor`, and they are right to be: that type IS "how to run this thing". What was wrong was where
 * the value came from — somebody's reviewer row. It is built from the preset now, so nothing
 * downstream had to change and nothing downstream reads `coai.vendors`.</p>
 *
 * <p>`enabled` is always true: a preset a person saved is a preset they want, and there is no
 * reviewer switch above it to turn it off. The id is the PRESET's, so the ledger and the spending
 * view name the thing that was chosen.</p>
 */
export function chatRunSpec(preset: ModelPreset): Vendor {
  return {
    id: preset.id,
    runtime: preset.runtime,
    model: preset.model,
    enabled: true,
    plan: false,
    code: false,
    baseUrl: preset.baseUrl,
    executablePath: preset.executablePath,
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...(preset.teamServerId !== undefined ? { teamServerId: preset.teamServerId } : {}),
    ...(preset.remoteVendor !== undefined ? { remoteVendor: preset.remoteVendor } : {}),
  };
}
