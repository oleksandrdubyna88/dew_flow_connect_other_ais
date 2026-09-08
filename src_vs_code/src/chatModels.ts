import { ChatModelChoice } from './chatPage';
import { Vendor } from './vendors';

/**
 * Which models this feature may put a question to.
 *
 * <p>Pure, over the vendor rows the panel already keeps. It exists so the picker and the command
 * agree about what is on offer: the panel boundary refuses a `pick` naming a model the conversation
 * was not offered, and "was not offered" has to mean the same thing in both places or that check is
 * theatre.</p>
 *
 * <p><b>Only runtimes with an adapter appear, and today that is `antigravity`.</b> Not a preference
 * — a measurement: the NDJSON schema, the ready event and the answer event were established by
 * running `agy` and reading its refusals. `claude` and `codex` were measured on 2026-09-08 and BOTH
 * hold a conversation, but each in its own shape, which is a seam this module does not have yet:
 * [todo/PLAN_three_chat_adapters.md](../../todo/PLAN_three_chat_adapters.md). Until it lands, a row
 * on another runtime is refused BY NAME rather than routed through a protocol it does not speak —
 * `vendor-routing.md` is explicit that a Claude model never goes through `agy`, and silently doing
 * it would be the exact failure that rule was written for.</p>
 */

/** The runtimes this feature can actually talk to. One today; three when the adapter plan lands. */
export const CHAT_RUNTIMES: readonly string[] = ['antigravity'];

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

/** What a person sees under a model's name — what it is, and what it cannot do. */
function captionOf(vendor: Vendor): string {
  return `local · ${vendor.runtime} · keeps the conversation`;
}

/**
 * The models on offer, and the reason for every row that is not.
 *
 * <p>A refused row is REPORTED rather than filtered away. A person who configured a reviewer called
 * `codex` and finds the chat picker silently missing it has no way to tell a bug from a policy; a
 * line saying which runtimes can answer costs nothing and answers that.</p>
 */
export function chatModelsFrom(vendors: readonly Vendor[]): ChatModelList {
  const enabled = vendors.filter((vendor) => vendor.enabled);
  const offered = enabled
    .filter((vendor) => CHAT_RUNTIMES.includes(vendor.runtime))
    .map((vendor): ChatModelChoice => ({
      id: vendor.id,
      label: vendor.model.length > 0 ? `${vendor.id} · ${vendor.model}` : vendor.id,
      caption: captionOf(vendor),
    }));
  const refused = enabled
    .filter((vendor) => !CHAT_RUNTIMES.includes(vendor.runtime))
    .map((vendor): RefusedModel => ({
      id: vendor.id,
      reason: `the chat can only speak to ${CHAT_RUNTIMES.join(', ')} so far — ${vendor.id} runs on ${vendor.runtime}`,
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
