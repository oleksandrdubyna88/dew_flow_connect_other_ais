import * as vscode from 'vscode';
import {
  ModelPreset,
  PromptPreset,
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  chatRunSpec,
  mainPrompt,
} from './chatPresets';
import {
  ChatCatalog,
  ChatProvider,
  LegacyPick,
  chatModelsFrom,
  chatProvidersFromPresets,
  legacyPick,
  resolveChatPick,
} from './chatModels';
import { ChatModelChoice } from './chatPage';
import { DISCOVERY_KEY, EMPTY_DISCOVERY, catalogUsing, discoveryFrom } from './chatDiscovery';
import { teamServersFrom } from './teamServers';
import { chatRuntimeRefusal } from './cliChatLaunch';
import { Vendor } from './vendors';

/**
 * What the chat reads out of the settings, and the host it reads the panel's discoveries on.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. Every function here takes a
 * `WorkspaceConfiguration` and answers with data — which prompts exist, which models, which row
 * will answer — and not one of them touches a conversation. That is the line this was cut along:
 * the three functions that WRITE an instruction into a composer mutate a thread and drive the
 * screen, so they stayed with the thread they mutate and went on to the hooks.</p>
 *
 * <p>`vendorFor` is here rather than with the launch, and it is worth knowing why: it reads
 * `savedModels(config)` and shapes one row, which is what every other function in this module does.
 * It is also what breaks a cycle — the page push reads `pairOf`, `pairOf` calls `vendorFor`, and
 * `switchModel` calls the page push. Moving it back to the launch re-forms that loop.</p>
 *
 * <p>The host lives here because it is read in exactly one place — `chatCatalogFrom`, for the
 * discoveries the panel left in `globalState` — and a binder whose only reader is in this module
 * belongs in this module.</p>
 */

/**
 * This extension host, so the chat can read the settings of the side it is actually on.
 *
 * <p>The same bind-once shape as `rememberChatsIn` above and as `chatOrphans.openLedger`, and for the
 * same reason: an extension host has ONE context for its whole life, and threading it from `activate`
 * through the command, the conversation, the picker and the model switch would put a parameter on six
 * signatures to carry a value that never changes.</p>
 *
 * <p>A worry raised on the plan round — that this could go stale across a workspace switch — does not
 * arise: `ExtensionContext` is made once per extension host, and a different workspace is a different
 * host. What CAN change between two invocations is the settings themselves, which is why the reader
 * below is built per call rather than kept here.</p>
 */
let hostContext: vscode.ExtensionContext | undefined;

/** Bind the host whose settings this chat reads. Called once, from `activate`. */
export function chatReadsThisSide(context: vscode.ExtensionContext): void {
  hostContext = context;
}

/**
 * Everything needed to start one conversation, or the sentence saying why it cannot start.
 *
 * <p>Two arms rather than one shape with an empty field. The single-shape version had to put
 * SOMETHING in `vendor` on the refusal path and reached for `vendors[0] as Vendor` — a cast that is
 * `undefined` whenever the person has no vendors configured at all, and a promise to keep a shape
 * by hand that comes due the first time somebody reads the field before checking the sentence.
 * The compiler keeps that promise instead. (gemini, the code round.)</p>
 */
export type Ready =
  | {
    readonly ok: true;
    readonly vendor: Vendor;
    readonly models: readonly ChatModelChoice[];
    /** Every row that can answer, each with its own models — what the picker offers. */
    readonly providers: readonly ChatProvider[];
    /** The row that answers. */
    readonly providerId: string;
    /** Which of that row's models, or empty for whatever the row is set to. */
    readonly modelId: string;
  }
  | { readonly ok: false; readonly refusal: string };

/**
 * What each row may be pointed at.
 *
 * <p><b>Discovery is not here, and that is the honest limit of this step.</b> Three of the four
 * sources are FETCHED rather than read: a local engine's models and the codex and agy CLIs' own
 * lists are discovered by asking the machine, and a Team server's allowlist is fetched from the
 * server. All three live in the panel, which has already done that work and holds the answers; the
 * chat command has no such state and starting subprocesses or HTTP calls to open a tab would trade
 * the operator's complaint for a slower one.</p>
 *
 * <p>So a row whose list must be fetched offers the model it is CONFIGURED to and nothing else —
 * exactly what the flat list offered before, which makes this strictly not worse. What gains a real
 * choice today is the one source that needs no fetching: Claude's curated three. Handing the panel's
 * discovered lists to this function is the plan's open tail, and it is written down as one.</p>
 */
/**
 * What a row can be pointed at, with the panel's discoveries in it.
 *
 * <p>This used to pass every discovered list EMPTY, with a comment saying the fetches live in the
 * panel — true, and harmless while the panel offered a flat list of rows. The moment the panel
 * offered a two-step picker over those discoveries it became a defect: somebody picks a model that
 * exists only in what `agy models` answered, and this catalog has never heard of it, so the
 * conversation opens on the row's own model instead. The panel leaves what it found in
 * `DISCOVERY_KEY`; this reads it back. Nothing is fetched here — a command must not wait on three
 * probes to open a tab.</p>
 */
export function chatCatalogFrom(config: vscode.WorkspaceConfiguration): ChatCatalog {
  const discovery = hostContext === undefined
    ? EMPTY_DISCOVERY
    : discoveryFrom(hostContext.globalState.get(DISCOVERY_KEY));

  return catalogUsing(discovery, teamServersFrom(config.get('teamServers')));
}

/**
 * The saved prompts, carrying the migration with them.
 *
 * <p>One reader, because two call sites reading the same settings is two places to forget the second
 * argument — and the second argument IS the migration: without it a person's own prompt, the one
 * they have been editing since the chat shipped, simply does not appear.</p>
 */
export function savedPrompts(config: vscode.WorkspaceConfiguration): readonly PromptPreset[] {
  const legacy = config.get('chatPrompt');

  return chatPromptPresetsFrom(
    config.get('chatPromptPresets'),
    typeof legacy === 'string' ? legacy : '',
  );
}

export function savedModels(config: vscode.WorkspaceConfiguration): readonly ModelPreset[] {
  return chatModelPresetsFrom(config.get('chatModelPresets'));
}

/**
 * The ROLE this conversation's model carries, or none.
 *
 * <p>A model preset's starting prompt — "you are an architect of distributed systems" — is a
 * standing fact about who is answering, so it belongs to the model rather than to a turn, and it is
 * read from the model in force rather than remembered separately.</p>
 */
export function roleOf(config: vscode.WorkspaceConfiguration, providerId: string): string {
  return savedModels(config).find((one) => one.id === providerId)?.startingPrompt ?? '';
}

/**
 * The prompt a conversation OPENS on: the one ticked main, or the first when nothing is ticked.
 *
 * <p>A preset list is the configuration now — *"Пресет И ЕСТЬ конфиг"* — so "which prompt does a
 * capture use" is answered by the tick in that list and not by a separate setting. A tab that opened
 * on none of them showed a row of buttons with nothing pressed and sent something the row did not
 * name.</p>
 */
export function openingPrompt(config: vscode.WorkspaceConfiguration): string {
  return mainPrompt(savedPrompts(config))?.id ?? '';
}

/**
 * The TASK this conversation is asking for: the prompt button pressed in it, or the panel's choice.
 */
export function taskOf(config: vscode.WorkspaceConfiguration, promptId: string, fallback: string): string {
  if (promptId.length === 0) {
    return fallback;
  }

  return savedPrompts(config).find((one) => one.id === promptId)?.text ?? fallback;
}

/**
 * A value saved before the pair existed, read as the pair it always was.
 *
 * <p>`coai.chatModel` and a restored tab's `modelId` have always held a ROW id — both predate the
 * two-step choice — so passing either as a MODEL would look for a model of that name and find
 * nothing. `legacyPick` is the pure half's function for exactly this, and it is called through one
 * place so the two callers cannot drift.</p>
 */
export function savedPick(config: vscode.WorkspaceConfiguration, saved: string): LegacyPick {
  const specs = savedModels(config).map(chatRunSpec);

  return legacyPick(chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config)), specs, saved);
}

/**
 * The row that will answer, and which of its models.
 *
 * <p>A PROVIDER is a vendor row, not a runtime, and that was settled by measurement rather than by
 * preference: three vendors' reviewers independently overturned the plan's recommendation on the
 * pure half's round. The row carries the runtime, the executable, the base URL, the price and — for
 * a Team server — the server and the vendor name on it, so it is the identity a saved choice stores
 * and the identity resolution looks up.</p>
 *
 * <p>A row the person NAMED and which cannot answer is refused BY NAME — never quietly replaced by
 * another vendor's, which is somebody else's model, billed, in a voice nobody chose.</p>
 */
export function readyToChat(
  config: vscode.WorkspaceConfiguration,
  askedProvider: string,
  askedModel: string,
): Ready {
  const specs = savedModels(config).map(chatRunSpec);
  const list = chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config));
  const pick = resolveChatPick(specs, list, askedProvider, askedModel);
  if (!pick.ok) {
    return { ok: false, refusal: pick.refusal };
  }

  const refusal = chatRuntimeRefusal(pick.row);

  return refusal.length > 0
    ? { ok: false, refusal }
    : {
      ok: true,
      vendor: pick.row,
      models: chatModelsFrom(specs).offered,
      providers: list.providers,
      providerId: pick.row.id,
      modelId: pick.model,
    };
}

/**
 * How to RUN the preset with this id — built from the preset itself, never from a reviewer row.
 *
 * <p>This looked the id up in `coai.vendors`, which is what made the chat depend on the review gate:
 * a reviewer switched off, renamed or removed took a conversation with it. A preset carries its own
 * vendor, model, CLI path and endpoint, and `chatRunSpec` shapes them into the `Vendor` every
 * launcher, session and Team-server client here was already written against.</p>
 */
export function vendorFor(presetId: string): Vendor | undefined {
  const preset = savedModels(vscode.workspace.getConfiguration('coai')).find((one) => one.id === presetId);

  return preset === undefined ? undefined : chatRunSpec(preset);
}
