import { ChatModelChoice } from './chatPage';
import { CHAT_RUNTIMES } from './cliChatLaunch';
import { REMOTE_TURNS } from './remoteAsk';
import { Vendor } from './vendors';

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
/** Whether this row can answer a chat at all — a CLI with an adapter, or a Team server. */
export function canChat(vendor: Vendor): boolean {
  return CHAT_RUNTIMES.includes(vendor.runtime) || vendor.runtime === 'remote';
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
export function memoryOf(vendor: Vendor): { readonly forgetful: boolean; readonly asked: number } {
  return { forgetful: vendor.runtime === 'remote', asked: 0 };
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
