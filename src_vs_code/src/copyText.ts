/**
 * Putting TEXT on the clipboard, and saying what happened.
 *
 * <p>Pure of `vscode`, and that is the point rather than a habit: the interesting paths here are the
 * ones that FAIL, and a rule living inside the panel host is a rule no test can reach. The clipboard
 * and the status bar arrive as two functions, so a test can make the write reject and watch what the
 * person is told.</p>
 *
 * <p><b>This is the half that `phraseCopy.ts` and the chat's block controls share.</b> It was
 * extracted rather than copied when the second caller arrived, because the two problems it solves are
 * not obvious and a second implementation would have been written without them — the ordering of two
 * presses, and the stacking of confirmations. Both are recorded on the functions below. Neither
 * caller may keep its own copy of this; what belongs to a caller is only which text to copy and what
 * to say about it.</p>
 */

/** What a message on the status bar must be able to do: go away. */
export interface Said {
  dispose(): void;
}

export interface CopyPorts {
  /** `vscode.env.clipboard.writeText`. It REJECTS — a clipboard held elsewhere, a session without one. */
  readonly writeText: (text: string) => Promise<void>;
  /** `vscode.window.setStatusBarMessage`, which answers with the handle that removes the line again. */
  readonly say: (message: string, forMs: number) => Said;
}

export interface CopyReport {
  /** True only when the write RESOLVED. A page flips a control to *Copied* on this and nothing else. */
  readonly copied: boolean;
  readonly said: string;
}

/**
 * What a caller decided: copy this and say that, or refuse and say why.
 *
 * <p>A decision rather than a `string | undefined`, so that a refusal always arrives WITH its
 * sentence. The shape is what stops a caller returning nothing and leaving the person told nothing —
 * which, for a copy, means pasting whatever was on the clipboard before.</p>
 */
export type CopyDecision =
  | {
    readonly kind: 'copy';
    readonly text: string;
    readonly done: string;
    /**
     * What to say if the WRITE fails — the caller's own words, not this module's.
     *
     * <p>Required rather than defaulted, because extracting this module silently replaced one
     * caller's sentence with a generic one: a phrase that could not be copied stopped saying it was
     * a phrase. A shared mechanism may own the machinery and must not own the wording. (codex, the
     * code round.)</p>
     */
    readonly failed: string;
  }
  | { readonly kind: 'refused'; readonly said: string };

/** How long a confirmation stays on the status bar. Long enough to notice, short enough to ignore. */
export const SAID_FOR_MS = 3000;

/**
 * How long a single write may take before the queue stops waiting for it.
 *
 * <p>Every wait has a ceiling. A write that never SETTLES — a permission prompt nobody answers, a
 * host that forgets to resolve — is worse than one that rejects: the chain behind it never runs, so
 * the next press does nothing, for ever, with nothing said. A rejection is already handled; this is
 * the case that has no natural end. (codex, the plan round.)</p>
 *
 * <p>Five seconds is far past any real clipboard write and short enough that a person pressing again
 * gets an answer rather than silence. Giving up on the wait does not cancel the write: if it lands
 * afterwards the clipboard simply holds it, which is the same outcome as a slow success.</p>
 */
export const WRITE_CEILING_MS = 5000;

export interface TextCopier {
  copy(decide: () => CopyDecision): Promise<CopyReport>;
}

/**
 * A copier that keeps ONE line on the status bar and writes in the order it was asked.
 *
 * <p><b>One line.</b> `setStatusBarMessage` returns a disposable, and five copies in five seconds
 * would otherwise leave five of them competing over the same strip. The previous one is disposed
 * before the next is set, so what is there is always the last thing that happened.</p>
 *
 * <p><b>In order.</b> Two presses start two asynchronous writes, and whichever RESOLVES last is what
 * the clipboard ends up holding — so pressing A and then B could leave A on it, which is the one
 * thing a copy control must never do. Chaining them makes the last press the last write.</p>
 *
 * <p>The decision is taken as a FUNCTION, evaluated when its turn comes rather than when the press
 * arrived: what a caller resolves may depend on state a press ahead of it in the queue changed.</p>
 */
/**
 * The writing half: one bounded write at a time, and nothing stale left holding the clipboard.
 *
 * <p>Its own unit because it owns state the queue does not care about — which attempt is newest —
 * and because the copier around it was over this repository's length limit with it inlined.</p>
 */
/**
 * The newest text ANYBODY asked the clipboard to hold, and when they asked.
 *
 * <p><b>Module-level, because there is one system clipboard and there were two opinions about it.</b>
 * Every copier used to keep its own record, so a correction belonging to the chat could land after a
 * phrase had been copied from the panel and put the answer back over it — two callers, one resource,
 * and neither able to see the other. (codex, the plan round.) The counter only ever increases, which
 * is what makes "is something newer wanted than the write that just landed" answerable at all.</p>
 *
 * <p><b>The guarantee is bounded by this MODULE, and the clipboard is not.</b> Two VS Code windows are
 * two extension hosts and therefore two of this record: a write stalled in one window can settle after
 * a newer copy in the other and take the clipboard, and nothing here can see it happen. Coordinating
 * that would mean a resource shared by every host, which is a great deal of machinery for a repair of
 * a timeout. So the promise is "the newest press in THIS window wins", and it is written down rather
 * than quietly widened to the system. (codex, the second code round, and it was right that the plan
 * claimed more than the code can hold.)</p>
 */
let wanted: { readonly at: number; readonly text: string; readonly ports: CopyPorts } | undefined;

/**
 * Stamped when the PRESS happens, not when its write starts.
 *
 * <p>Stamping at the write let a press that had been waiting behind a slow one take a number newer
 * than a press made after it — so the person's last press lost to their first. The number has to mean
 * "when this was asked for". (codex, the code round.)</p>
 */
let pressed = 0;

/**
 * Write, and put the newest text back if this write is overtaken while it is in flight.
 *
 * <p>A clipboard write cannot be cancelled, so the only honest strategy is to let every write finish
 * and re-assert whatever is newest afterwards. <b>Every</b> write goes through here — the first one
 * and every correction — which is the part that took two rounds to get right: the first version
 * corrected with a bare `ports.writeText` that nothing watched, so a correction overtaken by a press
 * settled last and restored text already copied past, and a correction that hung was never
 * reconsidered at all. (CodeRabbit on #273, then codex and gemini on this plan.)</p>
 *
 * <p>It terminates because a re-launch happens only when the generation has MOVED, and generations
 * are handed out one per press: with no new press, `now.at === at` and it stops.</p>
 */
function launch(ports: CopyPorts, at: number, text: string): Promise<void> {
  const write = ports.writeText(text);
  void write.then(
    () => {
      const now = wanted;
      if (now !== undefined && now.at !== at) {
        // Through the ports of whoever WANTED this text, never the ports of the write that happened
        // to settle last. They are the same clipboard today, but they are not the same object: each
        // copier is handed its own, and a correction routed through a foreign one would be writing
        // one caller's words down another caller's channel. (gemini, the code round, twice.)
        void launch(now.ports, now.at, now.text);
      }
    },
    // A correction that is REFUSED ends the chain, deliberately rather than by omission. The press it
    // is repairing has already been reported to the person as failed, and a clipboard that refused
    // one write will refuse the retry — so repeating would be a loop with no exit that says nothing
    // new. Nothing waits on this promise either, so a correction that never settles cannot hold up a
    // later press: the ceiling that releases the QUEUE is separate from minding what lands.
    () => undefined,
  );

  return write;
}

function boundedWriter(ports: CopyPorts, ceilingMs: number): (text: string, at: number) => Promise<void> {
  /**
   * The write, or the ceiling — whichever comes first.
   *
   * <p>The ceiling governs only how long the QUEUE waits, never whether the write is still watched:
   * giving up on the wait releases the next press, and `launch` goes on minding what lands. The timer
   * never outlives the attempt.</p>
   */
  return async function written(text: string, mine: number): Promise<void> {
    // FORWARD only. A press that waited behind a slow one still writes — its own press asked for it —
    // but it must not make itself the newest thing anybody wanted, or a later press would be undone
    // by an earlier one finally getting its turn.
    if (wanted === undefined || mine > wanted.at) {
      wanted = { at: mine, text, ports };
    }
    const write = launch(ports, mine, text);
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([write, new Promise<never>((_resolve, reject) => {
        timer = setTimeout(() => reject(new Error('the clipboard did not answer')), ceilingMs);
      })]);
    } finally {
      clearTimeout(timer);
    }
  };
}

/** What the caller decided, or a refusal if deciding itself threw. */
function decided(decide: () => CopyDecision): CopyDecision {
  try {
    return decide();
  } catch {
    // Taken OUTSIDE the write's own guard before, so a caller whose lookup threw rejected the
    // queue's promise and told the person nothing. (gemini, the code round.)
    return { kind: 'refused', said: 'It could not be copied — working out what to copy failed.' };
  }
}

export function textCopier(ports: CopyPorts, ceilingMs = WRITE_CEILING_MS): TextCopier {
  let live: Said | undefined;
  let working: Promise<unknown> = Promise.resolve();
  const written = boundedWriter(ports, ceilingMs);

  function tell(said: string): void {
    live?.dispose();
    live = ports.say(said, SAID_FOR_MS);
  }

  async function once(decide: () => CopyDecision, mine: number): Promise<CopyReport> {
    const decision = decided(decide);
    if (decision.kind === 'refused') {
      tell(decision.said);

      return { copied: false, said: decision.said };
    }
    try {
      await written(decision.text, mine);
    } catch {
      // A person who is told nothing pastes whatever was on the clipboard before — which is the
      // failure this sentence exists to stop. The words are the CALLER's.
      tell(decision.failed);

      return { copied: false, said: decision.failed };
    }
    tell(decision.done);

    return { copied: true, said: decision.done };
  }

  return {
    copy(decide): Promise<CopyReport> {
      // STAMPED HERE, at the press, before anything queues. What the queue decides is the ORDER the
      // writes go out in; what this number decides is whose text is newest, and those are different
      // questions the moment one press waits behind another.
      pressed += 1;
      const mine = pressed;
      // Chained onto whatever is in flight, and onto its FAILURE too — a refused write must not
      // break the queue for the press after it.
      const next = working.then(() => once(decide, mine), () => once(decide, mine));
      working = next;

      return next;
    },
  };
}
