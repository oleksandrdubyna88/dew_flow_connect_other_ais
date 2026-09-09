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
  | {
    readonly ok: true;
    readonly answer: string;
    /**
     * The process answering this turn is not the one that heard the earlier ones.
     *
     * <p>A vendor CLI that died takes the conversation with it - the next question starts a new
     * process with no memory of the passage or of anything already said, and its answer will read
     * as a model that has lost the thread. The session cannot prevent that; what it must not do is
     * HIDE it. A reviewer raised exactly this on the plan round, and it is the difference between
     * "the model is being obtuse" and "the conversation restarted".</p>
     */
    readonly contextLost?: true;
  }
  | {
    readonly ok: false;
    readonly failure: string;
    /**
     * The turn ended with no answer AND took the conversation with it.
     *
     * <p>The same fact the `ok: true` arm reports, on the arm where it is ACTIONABLE rather than
     * merely honest. A vendor that holds the conversation inside its own process loses it whenever
     * that process is killed — by a budget, by a crash, or now by a person pressing stop — and the
     * only thing that can put it back is the caller re-sending the transcript on the next turn.
     * Without a flag on this arm the caller cannot tell the two failures apart, so it either carries
     * the transcript after every failure (paying for it) or after none (losing it).</p>
     *
     * <p><b>Set for a STOP and not for a crash, deliberately.</b> A stop is the person's own
     * decision, so re-sending the conversation is finishing what they asked for. A process that FELL
     * OVER is nobody's decision, and silently re-sending a whole transcript because a third-party CLI
     * crashed bills somebody for an accident — which is why that case is a separate plan of its own,
     * gated on measuring how often it actually happens
     * (`todo/PLAN_a_dead_process_could_carry_the_conversation_too.md`). A crashed turn still SAYS the
     * conversation restarted, as it always has; what it does not do is spend money on it unasked.</p>
     */
    readonly contextLost?: true;
    /**
     * The person ended this turn. Nothing went wrong.
     *
     * <p>A stop and a crash both arrive as `ok: false` with a sentence, and they are opposite things:
     * one is an instruction that was obeyed, the other is a defect. Matching on the sentence would
     * tie every caller to its wording and to its translations.</p>
     */
    readonly stopped?: true;
  };

export interface ChatSession {
  /**
   * Ask, and wait for the answer.
   *
   * <p>Never rejects: a turn that fails answers `ok: false` with a sentence, because every caller
   * would otherwise have to write the same try/catch and the page has a region for exactly this.
   * Turns are serialised — a second `send` while one is in flight waits its turn rather than
   * interleaving down the same pipe.</p>
   */
  send(text: string, onWaiting?: (position: number) => void): Promise<TurnResult>;

  /**
   * End the turn that is running NOW, and leave the conversation able to take another.
   *
   * <p>Not `dispose`. A stop ends one turn; the session stays open and the next `send` works. What
   * that costs differs by vendor and the session knows which it is: a process that HOLDS the
   * conversation loses it when it is killed, and says so with `contextLost` so the caller re-sends
   * the transcript; a vendor that keeps the conversation in its own store resumes by id and loses
   * nothing; a Team server holds no conversation at all and only needs telling to drop the job.</p>
   *
   * <p><b>A stop that names nothing does nothing.</b> With no turn in flight this is a no-op — it
   * does not kill an idle process and it does not mark a conversation lost. That matters because a
   * stop can arrive late: a queued bridge message, a second press, a keybinding pressed as the
   * answer lands. The alternative is a healthy conversation thrown away for a keypress that was
   * already too late to mean anything. (gemini, codex and the local reviewer, the plan round.)</p>
   */
  stop(): void;

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
