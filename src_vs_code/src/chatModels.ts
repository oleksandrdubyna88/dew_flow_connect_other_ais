import { ChatModelChoice } from './chatPage';
import { CHAT_RUNTIMES } from './cliChatLaunch';
import { LocalEngine } from './localEngines';
import { allowedModelsFor, ModelChoice, modelsFor } from './models';
import { REMOTE_TURNS } from './remoteAsk';
import { TeamServerState } from './teamServerView';
import { Vendor } from './vendors';
import { ModelPreset, chatRunSpec } from './chatPresets';

/**
 * Which models this feature may put a question to.
 *
 * <p>Pure, over the vendor rows the panel already keeps. It exists so the picker and the command
 * agree about what is on offer: the panel boundary refuses a `pick` naming a model the conversation
 * was not offered, and "was not offered" has to mean the same thing in both places or that check is
 * theatre.</p>
 *
 * <p><b>Only runtimes with an ADAPTER appear, and since 2026-09-09 that is all three vendor CLIs.</b>
 * Each speaks its own shape — two hold a conversation in one process and `codex` resumes a stored
 * one — and every shape was measured rather than read out of a manual:
 * [research/PLAN_three_chat_adapters.md](../../research/PLAN_three_chat_adapters.md). What is
 * refused now is a runtime with no adapter at all — a local OpenAI endpoint, a Team server — and it
 * is refused BY NAME rather than routed through a protocol it does not speak: `vendor-routing.md`
 * is explicit that a Claude model never goes through `agy`, and the adapter map keeps that by
 * choosing the adapter and the executable together.</p>
 */

/** Why a row is not on offer, in words a person can act on. */
export interface RefusedModel {
  readonly id: string;
  readonly reason: string;
}

export interface ChatModelList {
  readonly offered: readonly ChatModelChoice[];
  /** Rows that exist and are enabled, but cannot answer yet. Shown, not hidden. */
  readonly refused: readonly RefusedModel[];
}

/**
 * What a person sees under a model's name — what it is, and what it cannot do.
 *
 * <p>The difference between the two kinds is not cosmetic and the person is the one who pays for it:
 * a local CLI holds the conversation in its own process, and a Team server holds none at all, so
 * every turn there re-sends everything said so far and the conversation stops at three. Said in the
 * picker, before the model is chosen.</p>
 */
function captionOf(vendor: Vendor): string {
  return vendor.runtime === 'remote'
    ? `team server · no memory, so each turn re-sends the conversation · ${REMOTE_TURNS} turns`
    : `local · ${vendor.runtime} · keeps the conversation`;
}

/**
 * The models on offer, and the reason for every row that is not.
 *
 * <p>A refused row is REPORTED rather than filtered away. A person who configured a reviewer called
 * `codex` and finds the chat picker silently missing it has no way to tell a bug from a policy; a
 * line saying which runtimes can answer costs nothing and answers that.</p>
 */
/**
 * Is this row answered by a Team server rather than by a process on this machine?
 *
 * <p>One predicate, because three places ask it and a literal `'remote'` in each is three chances to
 * mean something slightly different. (gemini, the code round, on two of them.)</p>
 */
export function isRemote(vendor: Vendor): boolean {
  return vendor.runtime === 'remote';
}

/** Whether this row can answer a chat at all — a CLI with an adapter, or a Team server. */
export function canChat(vendor: Vendor): boolean {
  return CHAT_RUNTIMES.includes(vendor.runtime) || isRemote(vendor);
}

/**
 * What a conversation remembers, and how much of it is left.
 *
 * <p>Its own type so that {@link memoryOf} is checked against it at the source and the thread that
 * holds it EXTENDS it. That is what makes a field added here reach both places a session starts:
 * one would not compile without it, and the other takes the whole object.</p>
 */
export interface ChatMemory {
  /** This model keeps no conversation of its own, so every turn re-sends one. */
  forgetful: boolean;
  /** How many turns THIS model has answered. The cap is on turns, not on messages. */
  asked: number;
}

/**
 * The memory rules that belong to the MODEL, so that changing one changes them together.
 *
 * <p>A Team server answers one question and forgets it: every turn must carry the conversation, and
 * the conversation must be capped, because the bill for turn N is the bill for everything before it.
 * A local CLI holds it in its own process and has neither problem.</p>
 *
 * <p><b>Why this is a function and not two assignments.</b> A conversation that switches model
 * replaces the session, the directory and the model id — and the first version left these two fields
 * describing the model it had just thrown away. Both directions broke, each invisibly: local to a
 * server left `forgetful` false, so the server was asked turn two with no transcript behind it and
 * answered as if it were turn one, while the three-turn cap never applied at all; server to local
 * left it true, so a CLI that HAS a memory was refused a fourth question. Spreading one object is
 * what makes the next field added here impossible to update in one place and forget in the other.
 * (codex, the code round.)</p>
 *
 * <p>`asked` is zero, and that is the half worth saying out loud: the cap counts the turns THIS
 * model was asked, not the turns the tab has held. Carrying the count across would meet a person
 * who chose a server model after four local turns with "this conversation is full" before they had
 * asked it anything.</p>
 */
export function memoryOf(vendor: Vendor): ChatMemory {
  return { forgetful: isRemote(vendor), asked: 0 };
}

export function chatModelsFrom(vendors: readonly Vendor[]): ChatModelList {
  const enabled = vendors.filter((vendor) => vendor.enabled);
  const offered = enabled
    .filter(canChat)
    .map((vendor): ChatModelChoice => ({
      id: vendor.id,
      label: vendor.model.length > 0 ? `${vendor.id} · ${vendor.model}` : vendor.id,
      caption: captionOf(vendor),
    }));
  const refused = enabled
    .filter((vendor) => !canChat(vendor))
    .map((vendor): RefusedModel => ({
      id: vendor.id,
      reason: `the chat can only speak to ${CHAT_RUNTIMES.join(', ')} and Team servers so far`
        + ` — ${vendor.id} runs on ${vendor.runtime}`,
    }));

  return { offered, refused };
}

/**
 * The model a turn should go to: the one asked for, or the first on offer.
 *
 * <p>Empty when nothing is offered, which is a state the caller must handle rather than a default it
 * can invent — asking a vendor that is not configured is how a feature spends somebody's money on a
 * refusal.</p>
 */
export function chosenModel(list: ChatModelList, asked: string): string {
  if (asked.length > 0 && list.offered.some((model) => model.id === asked)) {
    return asked;
  }

  return list.offered[0]?.id ?? '';
}

/** Either the model that will answer, or the sentence saying why none will. Never both. */
export interface ChatChoice {
  readonly modelId: string;
  readonly refusal: string;
}

/**
 * Which model answers, when the person may have NAMED one.
 *
 * <p>`chosenModel` falls back to the first row on offer, which is right for a blank setting and
 * wrong for a filled one: `coai.chatModel` naming `codex` means the passage went to somebody else's
 * model — billed to a vendor they did not choose, answered in a voice they did not ask for, and
 * with no line anywhere saying so. A name that cannot be honoured is refused by that name.
 * (codex, the code round.)</p>
 *
 * <p>An EMPTY setting is not a choice, so the first row on offer answers it exactly as before.</p>
 */
export function chatChoice(list: ChatModelList, asked: string): ChatChoice {
  if (asked.length === 0) {
    const first = list.offered[0]?.id ?? '';

    return first.length > 0
      ? { modelId: first, refusal: '' }
      : { modelId: '', refusal: refusalWhenNothingIsOffered(list) };
  }

  if (list.offered.some((model) => model.id === asked)) {
    return { modelId: asked, refusal: '' };
  }

  const refused = list.refused.find((row) => row.id === asked);

  return {
    modelId: '',
    refusal: refused !== undefined
      ? `${asked} cannot answer a chat: ${refused.reason}`
      : `The chat is set to ${asked}, which is not a configured reviewer any more.`,
  };
}

/** Every reason there is, rather than the first — the person has to fix all of them anyway. */
function refusalWhenNothingIsOffered(list: ChatModelList): string {
  const why = list.refused.map((row) => row.reason).join('; ');

  return why.length > 0
    ? `No model can answer a chat yet: ${why}`
    : 'Enable a reviewer on the antigravity runtime to chat with it.';
}

/**
 * One provider a chat can be sent to — which is a configured vendor ROW, not a runtime.
 *
 * <p><b>The distinction is the whole design, and it was not the first draft's.</b> The plan
 * recommended resolving a chosen runtime to "the first enabled row of that runtime". Three vendors'
 * reviewers rejected that independently on one round, and they were right: two `codex` rows with
 * different executables, base URLs or prices are two different backends, and picking whichever comes
 * first is a coin toss that bills the wrong one with nothing on screen saying which — and the answer
 * would change on a restart if the rows were ever reordered. A fourth finding extended it to Team
 * servers, where ONE server hosts several vendor rows (`<server>-codex`, `<server>-claude`) so a
 * server id alone cannot say which vendor is to answer.</p>
 *
 * <p>So a provider is a row, local and remote alike, and resolution is a LOOKUP rather than a
 * search. Nothing here can be ambiguous, because nothing here searches.</p>
 */
export interface ChatProvider {
  /** The vendor ROW id. This is the identity a saved choice stores and resolution looks up. */
  readonly id: string;
  readonly label: string;
  /**
   * The VENDOR this one runs on — `codex`, `antigravity`, `claude`, `remote`.
   *
   * <p>The picker shows it beside the model, because those are the two questions a person has:
   * who answers, and with what. It used to show the label twice over — the saved name in one select
   * and its model in the other — which is two dropdowns that read alike and say one thing.</p>
   */
  readonly vendor: string;
  readonly caption: string;
  /** What this row can be pointed at — discovered, curated or a server's allowlist. */
  readonly models: readonly ModelChoice[];
}

export interface ChatProviderList {
  readonly providers: readonly ChatProvider[];
  /** Rows that exist and are enabled, but cannot answer. Shown, not hidden. */
  readonly refused: readonly RefusedModel[];
}

/**
 * Everything a provider's model list is derived FROM, passed in rather than reached for.
 *
 * <p>`modelsFor` needs discovered Codex and agy lists, a local engine and a server allowlist. A
 * function that took only the vendor rows would have to read those from somewhere else, and then it
 * would not be pure — it would be a function whose answer depends on state its caller cannot see.
 * (codex, the plan round.)</p>
 */
export interface ChatCatalog {
  readonly discoveredCodex: readonly ModelChoice[];
  readonly discoveredAgy: readonly ModelChoice[];
  readonly localEngine?: LocalEngine | undefined;
  readonly teamServers: readonly TeamServerState[];
}

/**
 * Whether a model may be POINTED AT through this runtime — `vendor-routing.md`, which is MANDATORY.
 *
 * <p>Antigravity's subscription bundles Gemini, Claude and GPT-OSS behind one CLI, so
 * `claude-sonnet-4-6` and `claude-opus-4-6-thinking` are selectable there and must not be selected
 * there: the same models sit on an unlimited Claude subscription while every antigravity call is
 * drawn against a quota that is neither unlimited nor cheap. The model-comparison campaign of
 * 2026-09-01 ran both through `agy`, exhausted the account in fifty runs, and lost three cells of
 * the measurement — and nothing in the output said which CLI had answered.</p>
 *
 * <p>Applied to the CHAT's list only, and to local runtimes only: a Team server's allowlist is that
 * server's own routing decision, made on the other side of the seam by the vendor rows it hosts.</p>
 */
function routableOn(runtime: string, modelId: string): boolean {
  return !(modelId.toLowerCase().startsWith('claude') && (runtime === 'antigravity' || runtime === 'codex'));
}

/** The providers a chat may be sent to, and the reason for every configured row that is not one. */
export function chatProvidersFrom(
  vendors: readonly Vendor[],
  catalog: ChatCatalog,
): ChatProviderList {
  const enabled = vendors.filter((vendor) => vendor.enabled);
  const providers = enabled.filter(canChat).map((vendor): ChatProvider => ({
    id: vendor.id,
    vendor: vendor.runtime,
    // The ROW's name and nothing else. It used to append the model that row is configured to —
    // `codex · gpt-5.6-luna` — which came from the flat list this replaced, where one entry WAS one
    // model. Beside a model select it makes two dropdowns that read alike, and the operator asked
    // the obvious question: what is the difference? The left is which reviewer answers, the right is
    // which of its models, and the label has to say only the first. Two rows on one runtime are told
    // apart by their names, which is the whole reason a provider is a row and not a runtime.
    label: vendor.id,
    caption: captionOf(vendor),
    models: modelsFor(
      vendor.runtime,
      catalog.discoveredCodex,
      vendor.model,
      catalog.localEngine,
      catalog.discoveredAgy,
      allowedModelsFor(vendor, catalog.teamServers).models,
    ).filter((model) => routableOn(vendor.runtime, model.id)),
  }));
  const refused = enabled.filter((vendor) => !canChat(vendor)).map((vendor): RefusedModel => ({
    id: vendor.id,
    reason: `the chat can only speak to ${CHAT_RUNTIMES.join(', ')} and Team servers so far`
      + ` — ${vendor.id} runs on ${vendor.runtime}`,
  }));

  return { providers, refused };
}

/** Either the row and model that will answer, or the sentence saying why nothing will. Never both. */
export type ChatPick =
  | { readonly ok: true; readonly row: Vendor; readonly model: string }
  | { readonly ok: false; readonly refusal: string };

/**
 * A chosen (provider, model) pair, resolved to the row that says HOW to run it.
 *
 * <p><b>The pair is checked as a PAIR.</b> A model-only membership check would accept `sonnet`
 * against the `agy` row whenever some other provider offers a model by that name — and
 * `vendor-routing.md` forbids a Claude model going through `agy` by name. The check is therefore
 * "this provider offers this model", never "somebody offers this model". (codex, the plan round.)</p>
 *
 * <p>The row is what the caller actually needs: it carries the runtime, the executable, the base
 * URL, the price, and for a Team server the server AND `remoteVendor` — the field this family lost
 * once for three releases. The chosen MODEL is returned beside it rather than written into the row,
 * because a row's configured model belongs to the reviewer that row is, and a chat picking another
 * model must not edit somebody's reviewer.</p>
 */
export function resolveChatPick(
  vendors: readonly Vendor[],
  list: ChatProviderList,
  providerId: string,
  modelId: string,
): ChatPick {
  const provider = list.providers.find((one) => one.id === providerId);
  if (provider === undefined) {
    const refused = list.refused.find((one) => one.id === providerId);
    if (refused !== undefined) {
      return { ok: false, refusal: refused.reason };
    }
    // A row that exists and is switched OFF is not the same thing as a row that is gone, and the
    // person can act on exactly one of them. `chatProvidersFrom` filters disabled rows out of BOTH
    // lists — correctly, since a disabled row is not configured for anything — so without this the
    // two states arrive here indistinguishable and both read as "no longer configured".
    // (gemini, the code round.)
    const disabled = vendors.find((one) => one.id === providerId && !one.enabled);

    // NEVER built out of an empty name. An empty provider is not a model that went away — it is a
    // chat with nothing saved to send to, and the sentence has to say that and where to fix it.
    if (providerId.length === 0) {
      return {
        ok: false,
        refusal: list.providers.length === 0
          ? 'There is no saved model to send this to yet — add one with Edit chat presets.'
          : 'No saved model is chosen — pick one in the panel, or add one with Edit chat presets.',
      };
    }

    return {
      ok: false,
      refusal: disabled !== undefined
        ? `${providerId} is switched off in the panel — turn it back on to send a chat to it`
        : `${providerId} is not a model this conversation can be sent to any more`,
    };
  }
  const row = vendors.find((one) => one.id === providerId);
  if (row === undefined) {
    // The list and the rows disagreeing is a defect rather than a state, but it is reported as a
    // sentence instead of thrown: the caller is a command handler, and a throw there closes a tab.
    return { ok: false, refusal: `${providerId} is no longer configured` };
  }
  if (!provider.models.some((model) => model.id === modelId)) {
    return { ok: false, refusal: `${providerId} does not offer ${modelId}` };
  }

  return { ok: true, row, model: modelId };
}

/**
 * What a legacy value resolved to, and — when it did not — which providers were in the way.
 *
 * <p>`candidates` is empty on every path but one: a saved MODEL that more than one provider offers.
 * It exists so the caller can name them ("`gpt-5.2` is offered by `codex-cheap` and `codex-paid`")
 * instead of asking a person to pick something out of a list they were never shown.</p>
 */
export interface LegacyPick {
  readonly providerId: string;
  readonly modelId: string;
  readonly candidates: readonly string[];
}

/**
 * What a saved `coai.chatModel` from before the pair existed means now.
 *
 * <p>Every installation has one string today: the id of a reviewer row. That keeps working — it
 * names a provider, and the provider's own configured model comes with it.</p>
 *
 * <p>A value naming a MODEL rather than a row resolves only when exactly ONE provider offers it.
 * With two, this code would be choosing a vendor — and a bill — on somebody's behalf from a value
 * that never meant to say which. Ambiguity keeps the model and leaves the provider empty, so the
 * caller strands it and asks rather than guessing. (codex, the plan round.)</p>
 */
export function legacyPick(
  list: ChatProviderList,
  vendors: readonly Vendor[],
  saved: string,
): LegacyPick {
  if (saved.length === 0) {
    // The FIRST provider that can answer, which is what the panel's own empty option says it is.
    // This used to answer `{ providerId: '', … }` under a test titled "so the first provider can
    // answer as it always did" — and nothing downstream made that true: `resolveChatPick` finds no
    // provider with an empty id and refuses. `coai.chatModel` is empty by DEFAULT, so a fresh
    // installation pressing the keybinding was told `" is not a model this conversation can be sent
    // to any more"` — a sentence with a leading space and no name in it. Found by the code round.
    const first = list.providers[0];

    return first === undefined
      ? { providerId: '', modelId: '', candidates: [] }
      : { providerId: first.id, modelId: vendors.find((one) => one.id === first.id)?.model ?? '', candidates: [] };
  }
  // A ROW first, and the precedence is not arbitrary: every legacy value IS a row id, because that
  // is the only thing the old picker ever offered (`chatModelsFrom` mapped each row to one entry
  // keyed by `vendor.id`). A saved string that also happens to name a model is therefore a row that
  // somebody named after a model, and reading it as the row is reading it as what it was written as.
  // The model branch below exists for a value typed by hand into `settings.json`, which is the only
  // way one can arrive. (gemini, the code round, asked for this precedence to be stated.)
  const asRow = list.providers.find((one) => one.id === saved);
  if (asRow !== undefined) {
    return {
      providerId: saved,
      modelId: vendors.find((one) => one.id === saved)?.model ?? '',
      candidates: [],
    };
  }
  const offering = list.providers.filter((one) => one.models.some((model) => model.id === saved));
  if (offering.length === 1) {
    return { providerId: offering[0]!.id, modelId: saved, candidates: [] };
  }

  if (offering.length > 1) {
    // Ambiguous. The model is KEPT so the caller can strand it under its own name, and the candidates
    // come with it so the question can be "which of these two?" rather than "pick something".
    // (gemini, the code round.)
    return { providerId: '', modelId: saved, candidates: offering.map((one) => one.id) };
  }

  // Offered by NOBODY — which after the chat got its own saved models is the ordinary case, not an
  // exotic one: `coai.chatModel` holds a reviewer ROW id on every installation that predates it, and
  // a reviewer names nothing in this feature's world any more. A value that names nothing HERE is not
  // a choice, so the first saved model answers, exactly as an empty setting does. It used to answer
  // with an empty provider, and one step later that empty string was built into the sentence
  // `" is not a model this conversation can be sent to any more"` — a leading space and no name,
  // which is what the operator was shown when he opened a passage for analysis.
  const first = list.providers[0];

  return first === undefined
    ? { providerId: '', modelId: '', candidates: [] }
    : { providerId: first.id, modelId: vendors.find((one) => one.id === first.id)?.model ?? '', candidates: [] };
}

/**
 * Which model a NEW conversation opens with, given what the panel saved beside the provider.
 *
 * <p>`coai.chatModel` has always held a ROW and `legacyPick` reads it as one, bringing that row's
 * own model with it. `coai.chatModelName` is the panel's second half — which of that row's models —
 * and it is a LOOKUP with a fallback rather than a second source of truth: a name the chosen
 * provider does not offer is not a pick at all, and the row's own model answers instead.</p>
 *
 * <p>The fallback is the point. `settings.json` is edited by hand and a Team server withdraws models
 * from its allowlist without asking, so a saved name that no longer resolves is the ordinary case —
 * and refusing to open a conversation over it would punish a person for something a server did. One
 * step later `resolveChatPick` applies the same pair rule, where a name the person picked THIS
 * minute is worth a refusal by name.</p>
 */
export function openingModel(list: ChatProviderList, saved: LegacyPick, named: string): string {
  const provider = list.providers.find((one) => one.id === saved.providerId);

  return named.length > 0 && (provider?.models.some((model) => model.id === named) ?? false)
    ? named
    : saved.modelId;
}

/** What a switch will run, or the sentence saying why it will not. Never both. */
export type ModelToRun =
  | { readonly ok: true; readonly model: string }
  | { readonly ok: false; readonly refusal: string };

/**
 * Which of a provider's models a switch runs, given what was asked for.
 *
 * <p><b>The fallback belongs to the EMPTY ask alone.</b> An empty model is the page saying the
 * PROVIDER moved and it will not carry a model across — which of the new row's models answers is
 * this side's to decide, because it holds the catalog. A model that was NAMED and is not offered is
 * a refusal: four reviewers across three vendors raised the same thing on one round, and they were
 * right. A person presses a button labelled `gpt-5.6-luna`, a catalog changes under them, and the
 * turns go somewhere else — billed, in a voice nobody chose, which is what every rule in this
 * feature exists to prevent. A stale preset must say so, not quietly become another model.</p>
 *
 * <p>The row's own model is preferred for an empty ask and is not assumed to be offered:
 * `routableOn` drops a Claude model from an `agy` row, so a row configured that way has a configured
 * model its own picker never shows.</p>
 */
export function modelToRun(
  models: readonly ModelChoice[],
  asked: string,
  own: string,
): ModelToRun {
  const offers = (id: string): boolean => models.some((one) => one.id === id);
  if (asked.length > 0) {
    return offers(asked)
      ? { ok: true, model: asked }
      : { ok: false, refusal: `${asked} is not one of the models this reviewer offers any more.` };
  }
  const fallback = offers(own) ? own : (models[0]?.id ?? '');

  return fallback.length === 0
    ? { ok: false, refusal: 'That reviewer offers no model this chat can run.' }
    : { ok: true, model: fallback };
}

/**
 * The providers a CHAT may be sent to — its own saved models, and nothing a reviewer owns.
 *
 * <p>This replaces `chatProvidersFrom(vendors, …)` on every chat path. That one answers "which
 * REVIEWER rows can chat", which is how the feature was built and what made it impossible to use
 * without touching the review gate: a reviewer switched off took the chat with it, and every picker
 * read as a list of somebody's reviewers. A preset carries its own vendor, model and CLI, so the
 * list is the person's saved models — named by THEM.</p>
 *
 * <p>The models each one can be pointed at still come from the catalog: what the CLIs listed on this
 * machine, and what a Team server allows. A preset whose vendor cannot chat at all is refused by
 * name rather than hidden, the rule every other list here keeps.</p>
 */
export function chatProvidersFromPresets(
  presets: readonly ModelPreset[],
  catalog: ChatCatalog,
): ChatProviderList {
  const providers = presets.filter((preset) => canChat(chatRunSpec(preset))).map((preset): ChatProvider => {
    const spec = chatRunSpec(preset);

    return {
      id: preset.id,
      label: preset.name,
      vendor: preset.runtime,
      caption: captionOf(spec),
      models: modelsFor(
        preset.runtime,
        catalog.discoveredCodex,
        preset.model,
        catalog.localEngine,
        catalog.discoveredAgy,
        allowedModelsFor(spec, catalog.teamServers).models,
      ).filter((model) => routableOn(preset.runtime, model.id)),
    };
  });
  const refused = presets.filter((preset) => !canChat(chatRunSpec(preset))).map((preset): RefusedModel => ({
    id: preset.id,
    reason: `the chat can only speak to ${CHAT_RUNTIMES.join(', ')} and Team servers so far`
      + ` — ${preset.name} runs on ${preset.runtime}`,
  }));

  return { providers, refused };
}
