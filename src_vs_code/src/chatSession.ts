/**
 * What a conversation is, whichever side of the machine answers it.
 *
 * <p>Two implementations will satisfy this: `cliChatSession.ts` holds a real conversation in a
 * long-lived vendor process, and `remoteChatSession.ts` has none and re-sends its transcript to the
 * Team server. The panel must not know which it holds — but the PERSON must, because the difference
 * shows up in latency, in cost, and in a three-turn limit that only one of them has.</p>
 */

/** What one turn produced: an answer, or a sentence saying why there is none. */
export type TurnResult =
  | { readonly ok: true; readonly answer: string }
  | { readonly ok: false; readonly failure: string };

export interface ChatSession {
  /**
   * Ask, and wait for the answer.
   *
   * <p>Never rejects: a turn that fails answers `ok: false` with a sentence, because every caller
   * would otherwise have to write the same try/catch and the page has a region for exactly this.
   * Turns are serialised — a second `send` while one is in flight waits its turn rather than
   * interleaving down the same pipe.</p>
   */
  send(text: string): Promise<TurnResult>;

  /** End it. Safe to call twice; a session already gone stays gone. */
  dispose(): void;
}

/** How long each phase may take. Separate, because they fail differently and read differently. */
export interface TurnBudgets {
  /**
   * From launch to the CLI's `init` event.
   *
   * <p>Measured on this machine: 3.6–6.6 s. A process that never reaches `init` failed to START —
   * a missing binary, a refused sign-in — which is a different sentence from one that started and
   * then said nothing.</p>
   */
  readonly startupMs: number;

  /**
   * From the line being written to the turn's `result`.
   *
   * <p>Measured: 9.4 s for a real explanation, 4.3 s for a short follow-up. The budget is not the
   * measurement — it is the point past which waiting is no longer reasonable.</p>
   */
  readonly turnMs: number;
}

export const DEFAULT_BUDGETS: TurnBudgets = { startupMs: 30_000, turnMs: 180_000 };
