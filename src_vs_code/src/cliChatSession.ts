import { ReportedUsage, turnCost, turnTokens } from './chatUsage';
import { ProcessHandle, Unsubscribe } from './processLauncher';
import { ChatAdapter } from './chatAdapter';
import { agyAdapter } from './agyAdapter';
import { ChatSession, TurnBudgets, TurnResult } from './chatSession';

/**
 * A conversation held in one long-lived vendor process.
 *
 * <p>The protocol was MEASURED, not read out of a manual: `agy --input-format stream-json
 * --output-format stream-json` takes one NDJSON message per line and answers `init`, then
 * `step_update`s, then a `result`. Three turns down one pipe were answered by one process with the
 * context preserved — turn 3 resolved "now simpler" against turn 2 without the passage being
 * repeated. The schema came out of the binary's own error strings after it refused `type:` with
 * `stream input message is missing the "event" field`.</p>
 *
 * <p><b>A long-lived child has four ways to end, and only one of them is a person closing a tab.</b>
 * The first version of the plan described that one; the gate raised the other three as five separate
 * findings. They are the shape of this file:</p>
 *
 * <ol>
 *   <li><b>Disposal</b> — the tab closed. The tree is killed, and anything waiting is told at once,
 *       including a start that has not finished: without that, closing a tab left the caller waiting
 *       the whole thirty-second startup budget for an answer nobody was going to read.</li>
 *   <li><b>It exits on its own</b> — token exhaustion, a crash, an OS kill. The turn in flight fails
 *       with a sentence, and the NEXT send starts a new process rather than writing into a closed
 *       pipe. `EPIPE` is a state to report, never an unhandled rejection.</li>
 *   <li><b>It never starts</b> — no `init` inside the startup budget. Killed, and said differently
 *       from the case below, because "it would not start" and "it will not answer" send a person to
 *       different places.</li>
 *   <li><b>It never answers</b> — no `result` inside the turn budget. Killed, so the next turn is
 *       not asked of a process that has already stopped listening.</li>
 * </ol>
 *
 * <p><b>A conversation that restarts says so.</b> A dead process takes the conversation with it —
 * the replacement never heard the passage or anything already said, and its answer reads as a model
 * that has lost the thread. The session cannot prevent that; what it must not do is hide it. The
 * failure that reports the death says the conversation has to start again, and the first answer
 * afterwards carries `contextLost`. It is counted from the first turn SENT, not the first ANSWERED:
 * a process that died before answering still took a question with it.</p>
 *
 * <p><b>One turn at a time, enforced here rather than hoped for in the page.</b> The page disables
 * its composer, but a page is a suggestion: two `send` calls arriving together must not interleave
 * two turns down one pipe, and the second waits rather than being refused — refusing would lose what
 * somebody typed.</p>
 *
 * <p>Everything that touches the world is injected: the launcher, and the timers. That is what lets
 * the failure paths above be tested at all — a suite that waited three minutes for a turn budget is
 * a suite nobody runs.</p>
 */

/** A cancellable delay. Injected so a test can expire a budget without waiting for one. */
export interface Timers {
  after(ms: number, run: () => void): () => void;
}

export const REAL_TIMERS: Timers = {
  after: (ms, run) => {
    const handle = setTimeout(run, ms);

    return () => clearTimeout(handle);
  },
};

/** What a turn is waiting for, and how to end its wait. */
interface Pending {
  readonly settle: (result: TurnResult) => void;
  readonly cancelBudget: () => void;
}

const CLOSED = 'the conversation was closed';
const STOPPED = 'you stopped this answer';

export class CliChatSession implements ChatSession {
  private child: ProcessHandle | undefined;
  private unsubscribe: Unsubscribe | undefined;
  private ready = false;
  private pending: Pending | undefined;
  private waitingForInit: ((started: string) => void) | undefined;
  private cancelStartBudget: (() => void) | undefined;
  /**
   * What the start already came to, when it came to it before anybody was waiting.
   *
   * <p>The launcher REPLAYS what a child said before its first subscriber — which is the whole
   * reason it does, and it means `init`, or a death, can land INSIDE the subscription below, at a
   * moment when there is nobody to tell. The first version waited for a signal that had already been
   * sent and hung for the entire startup budget on every turn. Found by its own tests.</p>
   */
  private startOutcome: string | undefined;
  /**
   * Which child the callbacks belong to.
   *
   * <p>A killed child can deliver its `exit` AFTER the next send has started a replacement, and the
   * stale callback would then clear the new child and fail its turn. Every subscription carries the
   * generation it was made in and does nothing once that generation is over. (codex, the code round.)</p>
   */
  private generation = 0;
  /** Whether anything has been asked yet — a first turn has no conversation to lose. */
  private everSent = false;
  /**
   * The conversation's id in a PER-TURN vendor's own store.
   *
   * <p>Empty until the vendor names one. Every turn after that carries it, which is what makes a
   * process-per-question hold a conversation at all — and it is an ID rather than "the last
   * session", because "the last session" is the last one on the MACHINE and two chat tabs would
   * then answer each other's questions.</p>
   */
  private sessionId = '';
  /** The next answer comes from a process that never heard the earlier turns. Told once, then cleared. */
  private contextLost = false;
  private queue: Promise<unknown> = Promise.resolve();
  private disposed = false;
  /**
   * How to end the turn that is running now, as a stop. Undefined when none is.
   *
   * <p>This IS the turn's identity, and it is a closure rather than a number so that it cannot name
   * the wrong turn: it is created by the turn it ends and cleared the moment that turn settles by
   * ANY route — answered, failed, timed out, stopped. A stop arriving one tick late therefore finds
   * `undefined` and does nothing, and a stop arriving after the NEXT turn has started finds that
   * turn's own closure. A counter compared by hand would have had to be right in six places; this is
   * right because there is nowhere else to put it. (codex and gemini, the plan round — "a delayed
   * stop can terminate the following turn because stop has no turn identity".)</p>
   */
  private endTurnAsStopped: (() => void) | undefined;

  /**
   * What the vendor last said a turn cost, when it said it on an event of its own.
   *
   * <p>`codex` puts the answer on one line and the numbers on the next; a `classify` that reads one
   * line at a time cannot join them, so the join happens here. Cleared per turn, because a stale
   * figure attached to the NEXT answer would be worse than none.</p>
   */
  /**
   * What the vendor has said about the turn now in flight, RAW, before any differencing.
   *
   * <p>Set by a `usage` event, which the vendors that have one send BEFORE or AFTER the answer and
   * never as it. Read once, by `settle`, so that every way a turn can end carries what the vendor
   * managed to report — including a stop and a failure, which are the turns most worth accounting
   * for and which used to carry nothing. (codex and gemini, the code round, four findings.)</p>
   */
  private lastUsage: ReportedUsage | undefined;

  /**
   * The RUNNING TOTAL this vendor thread last reported, for the vendors that count that way.
   *
   * <p>It lives on the SESSION because a session's life is exactly a vendor thread's life: the id it
   * resumes by is `this.sessionId`, it is minted by the first turn and dies with this object, and a
   * replacement session — a model switch, a window reload — starts a new thread whose count starts
   * again. Keeping the baseline anywhere else means keeping two things in step that nothing forces
   * to agree, which is what a reviewer objected to: the rule reached out of the adapter layer and
   * into the command that orchestrates the page. (gemini, the code round.)</p>
   *
   * <p>So a `TurnResult` always carries the cost of ONE turn, whatever its vendor counts in.</p>
   */
  private threadTotals: ReportedUsage | undefined;

  /**
   * @param start launches the vendor process — injected, so this file spawns nothing itself
   * @param budgets how long the start and each turn may take
   * @param timers the clock, injected so a test can expire a budget in a millisecond
   */
  constructor(
    private readonly start: (resume: string) => ProcessHandle,
    private readonly budgets: TurnBudgets,
    private readonly timers: Timers = REAL_TIMERS,
    /**
     * Which vendor's protocol this conversation speaks.
     *
     * <p>Defaulted, and the default is the one this file used to hardcode. That is deliberate: the
     * seam arrived as a refactor, and every test written against the old behaviour had to pass
     * unchanged to prove the refactor changed nothing. A caller that means another vendor passes
     * one — `cliChatLaunch` does.</p>
     */
    private readonly adapter: ChatAdapter = agyAdapter,
  ) {}

  /** Whether a process is currently alive. For the tests, and for a caller that wants to say so. */
  get running(): boolean {
    return this.child !== undefined;
  }

  send(text: string): Promise<TurnResult> {
    // The queue IS the serialisation. Chaining on the previous turn's settlement means a second
    // caller waits rather than opening a second turn on the same pipe. The catch is not decoration:
    // `send` promises never to reject, and an injected launcher that throws would otherwise break
    // that promise and the queue with it. (Four reviewers, one finding.)
    const mine = this.queue.then(() => this.turn(text)).catch((reason: unknown) => failed(reason));
    this.queue = mine;

    return mine;
  }

  /**
   * End the turn that is running now; leave the session able to answer the next question.
   *
   * <p>The kill is the one `dispose` has always used. What is different is the sequel: `disposed` is
   * NOT set, so the next `send` starts a process again, and for a vendor that held the conversation
   * in the killed process the turn reports `contextLost` so its caller re-sends the transcript. For
   * a per-turn vendor `killChild` leaves the thread id alone and the next turn resumes by it, which
   * is why this method needs no branch of its own for the two shapes.</p>
   */
  stop(): void {
    // Not guarded on `disposed` separately: a disposed session has already settled its turn, so the
    // closure is gone and this is the same no-op by the same rule.
    this.endTurnAsStopped?.();
  }

  dispose(): void {
    this.disposed = true;
    // A start that has not finished is waiting too, and nobody was telling it. Closing a tab used to
    // leave the caller waiting the whole startup budget. (codex, the code round, twice.)
    this.cancelStartBudget?.();
    this.cancelStartBudget = undefined;
    const waiting = this.waitingForInit;
    this.waitingForInit = undefined;
    waiting?.(CLOSED);
    this.settle({ ok: false, failure: CLOSED });
    this.killChild();
  }

  /** One turn, start to finish, with nothing else in the pipe. */
  private async turn(text: string): Promise<TurnResult> {
    if (this.disposed) {
      return { ok: false, failure: CLOSED };
    }
    if (this.adapter.shape === 'per-turn') {
      return this.perTurn(text);
    }

    // The stop handle is installed BEFORE the process is started, not after the question is written.
    // Startup is 3.6-6.6 s measured — a large share of the wait a person presses stop to end — and a
    // handle installed after it made a stop during launch a silent no-op: the process went on
    // starting, took the question and answered it. (Four reviewers across three vendors, the code
    // round, which is as clear a signal as that gate gives.)
    const stopping = new Promise<TurnResult>((resolve) => {
      this.endTurnAsStopped = (): void => {
        // Everything `dispose` does to a start in progress, minus the disposal itself. A waiter
        // nobody tells is a caller sitting out the whole startup budget for a turn already over.
        this.cancelStartBudget?.();
        this.cancelStartBudget = undefined;
        const waiting = this.waitingForInit;
        this.waitingForInit = undefined;
        waiting?.(STOPPED);
        // Kill FIRST, so `contextLost` is already true when the result is built. Reversed, the turn
        // would report a conversation it no longer has and the caller would carry nothing into a
        // process that heard nothing. (the local reviewer, the plan round.)
        this.killChild();
        const result = this.stopped();
        // Whichever stage this turn had reached. `settle` ends one already waiting on the pipe and
        // returns early when there is none; `resolve` ends one still in startup. Both are safe to
        // run: a race settles once, and so does `settle`.
        this.settle(result);
        resolve(result);
      };
    });
    const asking = this.askAndWait(text);
    // Attached before the race. If the stop wins, this promise still settles later with nobody
    // reading it, and a rejection nobody handles takes down the extension host rather than the turn.
    asking.catch(() => undefined);

    try {
      return await Promise.race([stopping, asking]);
    } finally {
      // Every exit from this turn, including the ones that never reach `settle` — a start that
      // failed returns its sentence directly. A closure left behind here is a stop that would kill
      // an idle process later.
      this.endTurnAsStopped = undefined;
    }
  }

  /** Start the process if it is not up, write the question, and wait for the answer. */
  private async askAndWait(text: string): Promise<TurnResult> {
    const started = await this.ensureStarted();
    if (started !== '') {
      return { ok: false, failure: started };
    }

    this.everSent = true;
    const line = this.adapter.encode(text);
    if (this.child?.writeLine(line) !== true) {
      // The pipe went between the last event and this write. Not an error to throw: the next send
      // starts a new process, which is what a person pressing Enter again expects to happen.
      this.killChild();

      return { ok: false, failure: 'the model’s process had already gone; ask again to start a new one' };
    }

    return new Promise<TurnResult>((resolve) => {
      const cancelBudget = this.timers.after(this.budgets.turnMs, () => {
        // Killed, not merely abandoned: a process that has stopped answering must not be handed the
        // next turn as though nothing happened.
        this.killChild();
        this.settle({ ok: false, failure: this.withRestart('the model did not answer in time') });
      });
      this.pending = { settle: resolve, cancelBudget };
    });
  }

  /** An empty string when a process is up and has said `init`; otherwise the reason it is not. */
  private ensureStarted(): Promise<string> {
    if (this.child !== undefined && this.ready) {
      return Promise.resolve('');
    }

    this.startOutcome = undefined;
    let child: ProcessHandle;
    try {
      child = this.start(this.sessionId);
    } catch (reason) {
      // `send` promises never to reject, and an injected launcher is somebody else's code.
      return Promise.resolve(`the model’s process could not be started: ${message(reason)}`);
    }
    this.child = child;
    this.ready = false;
    this.generation += 1;
    this.listen(child, this.generation);

    // A vendor that never announces itself is ready the moment it is running: `claude` says nothing
    // at all until a turn arrives, so waiting for a ready event spends the whole startup budget and
    // then reports a CLI that never started — for one that was working and had not been asked. Its
    // first turn is bounded by the turn budget instead, which is the honest thing to bound.
    if (!this.adapter.announces) {
      this.ready = true;

      return Promise.resolve('');
    }
    // Ask what happened rather than wait to be told: the replay above may already have delivered it.
    if (this.ready) {
      return Promise.resolve('');
    }
    if (this.startOutcome !== undefined) {
      return Promise.resolve(this.startOutcome);
    }

    return new Promise<string>((resolve) => {
      const cancelBudget = this.timers.after(this.budgets.startupMs, () => {
        this.waitingForInit = undefined;
        this.cancelStartBudget = undefined;
        // What the child managed to say before giving up is the difference between "not installed"
        // and "installed, and refusing your sign-in". Read BEFORE the kill, which reads better
        // beside it. (local, the plan round.)
        const said = child.stderrTail().trim().slice(-300);
        this.killChild();
        resolve(said.length === 0
          ? 'the model’s process did not start'
          : `the model’s process did not start: ${said}`);
      });
      this.cancelStartBudget = cancelBudget;
      this.waitingForInit = (failure: string) => {
        cancelBudget();
        this.cancelStartBudget = undefined;
        this.waitingForInit = undefined;
        resolve(failure);
      };
    });
  }

  private listen(child: ProcessHandle, generation: number): void {
    const mine = (run: () => void): void => {
      if (generation === this.generation) {
        run();
      }
    };
    this.unsubscribe = child.onLine((line) => mine(() => this.onLine(line)));
    child.onError((reason) => mine(() => this.onGone(`the model’s process failed to run: ${reason}`)));
    child.onExit(() => mine(() => this.onGone(ended(child.stderrTail()))));
  }

  /** One line, as the vendor's own adapter reads it. Anything it does not recognise is skipped. */
  private onLine(line: string): void {
    const event = this.adapter.classify(line);
    if (event.kind === 'ready') {
      this.ready = true;
      this.waitingForInit?.('');

      return;
    }
    if (event.kind === 'usage') {
      // Held rather than settled: the vendors that put usage on an event of its own send it BEFORE
      // or AFTER the answer, never as it. The answer's own numbers win when it has any.
      this.lastUsage = event.usage;

      return;
    }
    if (event.kind === 'answer') {
      const lost = this.contextLost;
      this.contextLost = false;
      // The answer's own numbers win over a held `usage` event; `settle` differences whichever it is.
      this.lastUsage = event.usage ?? this.lastUsage;
      this.settle(lost
        ? { ok: true, answer: event.text, contextLost: true }
        : { ok: true, answer: event.text });

      return;
    }
    if (event.kind === 'failure') {
      this.settle({ ok: false, failure: event.failure });
    }
  }

  /**
   * One turn for a vendor that holds no pipe: a process, a prompt, an answer, an exit.
   *
   * <p>Nothing here waits for a ready event, because there is none — the process starts working the
   * moment its input closes. The conversation survives the exit: it lives in the vendor's own store
   * and the next turn resumes it by the id this one was told. That is the inverted half of the
   * context rule, and it is why `stop()` does not mark a per-turn conversation lost.</p>
   */
  private perTurn(text: string): Promise<TurnResult> {
    let child: ProcessHandle;
    try {
      child = this.start(this.sessionId);
    } catch (reason) {
      return Promise.resolve({ ok: false, failure: `the model’s process could not be started: ${message(reason)}` });
    }
    this.child = child;
    this.generation += 1;
    // A per-turn conversation is lost only when its THREAD is. A first turn that died before the
    // vendor named one leaves nothing to resume, so this question opens a brand new conversation —
    // and the answer must say so, exactly as a dead pipe's would. (gemini, the plan round.)
    this.contextLost = this.contextLost || (this.everSent && this.sessionId.length === 0);
    this.everSent = true;

    // Written and then CLOSED: `codex exec -` reads until end of input, so a turn whose stream stays
    // open is a turn that never starts. Measured, and the reason `writeAndEnd` exists at all.
    if (!child.writeAndEnd(this.adapter.encode(text))) {
      this.killChild();

      return Promise.resolve({ ok: false, failure: 'the model’s process had already gone; ask again to start a new one' });
    }

    const resuming = this.sessionId;

    return new Promise<TurnResult>((resolve) => {
      let answer = '';
      let failure = '';
      let named = false;
      let done = false;
      // Per TURN, not per session: a per-turn vendor gets a fresh process each time, so there is no
      // earlier figure that could belong to this one.
      let reported: ReportedUsage | undefined;
      const finish = (result: TurnResult): void => {
        if (done) {
          return;
        }
        done = true;
        // Cleared on EVERY exit from this turn, exactly as `settle` does for the persistent shape:
        // a stop that arrives after the turn ended must find nothing to end.
        this.endTurnAsStopped = undefined;
        cancelBudget();
        this.killChild();
        // Whatever the vendor managed to report reaches EVERY ending, not only the answered one: a
        // codex turn emits `turn.completed` and can then be stopped, and the turn it priced really
        // was paid for. (codex and gemini, the code round.)
        resolve(withUsage(result, this.perTurnUsage(reported)));
      };
      const cancelBudget = this.timers.after(this.budgets.turnMs, () => {
        finish({ ok: false, failure: 'the model did not answer in time' });
      });
      // No `contextLost` here, and that is the inversion this shape is built on: `killChild` marks a
      // conversation lost only for a PERSISTENT adapter, and the thread id this turn was told lives
      // on in `this.sessionId`, so the next question resumes the same conversation. Stopping a codex
      // turn costs the answer and nothing else. (codex, the plan round, asked for this to be proven
      // rather than asserted — `perTurnSession.test.ts` does.)
      this.endTurnAsStopped = (): void => {
        finish({ ok: false, failure: STOPPED, stopped: true });
      };

      child.onLine((line) => {
        const event = this.adapter.classify(line);
        if (event.kind === 'session') {
          this.sessionId = event.id;
          named = true;
        } else if (event.kind === 'usage') {
          // This is the shape that NEEDS it: `codex` puts the answer on `item.completed` and the
          // numbers on `turn.completed`, which arrives afterwards — so the figure is kept until the
          // process exits and the turn is finished.
          reported = event.usage;
        } else if (event.kind === 'answer') {
          answer = event.text;
        } else if (event.kind === 'failure') {
          failure = event.failure;
        }
      });
      child.onError((reason) => finish({ ok: false, failure: `the model’s process failed to run: ${reason}` }));
      // The exit IS the end of the turn here, not a death to report: whatever was said before it is
      // the answer, and nothing was said means the process left without one.
      child.onExit((code) => {
        if (failure.length > 0) {
          finish({ ok: false, failure });

          return;
        }
        if (answer.length > 0) {
          const lost = this.contextLost;
          this.contextLost = false;
          finish(lost ? { ok: true, answer, contextLost: true } : { ok: true, answer });

          return;
        }
        // A RESUME that produced neither a thread nor an answer is a thread that is gone — pruned
        // by the vendor, left behind by a version change, on a machine that was re-imaged. Keeping
        // the id would retry the same dead conversation for ever; dropping it costs this one turn
        // and the next question starts a new conversation and says so. (gemini, the code round.)
        if (resuming.length > 0 && !named && answer.length === 0) {
          this.sessionId = '';
        }
        // The exit code is the one fact the operating system gives away for free, and it is the
        // difference between "it crashed" and "it refused". (gemini, the plan round.)
        finish({ ok: false, failure: leftWithNothing(code, child.stderrTail()) });
      });
    });
  }

  /** The process has gone, whether it meant to or not. */
  private onGone(failure: string): void {
    this.child = undefined;
    this.ready = false;
    // Only a conversation that HAD something to lose can lose it, and what it takes is the question
    // as much as the answer: a process that died before replying still swallowed a turn.
    this.contextLost = this.contextLost || this.everSent;
    this.startOutcome = failure;
    this.waitingForInit?.(failure);
    this.settle({ ok: false, failure: this.withRestart(failure) });
  }

  /** The same sentence, saying that the thread is gone when it is. */
  private withRestart(failure: string): string {
    return this.everSent && this.contextLost
      ? `${failure}. The conversation has to start again — the next answer will not remember this one`
      : failure;
  }

  /** What a stopped turn on THIS vendor looks like, asked after the process is already gone. */
  private stopped(): TurnResult {
    return this.contextLost
      ? {
        ok: false,
        failure: `${STOPPED} — the next question re-sends the conversation so far`,
        contextLost: true,
        stopped: true,
      }
      : { ok: false, failure: STOPPED, stopped: true };
  }

  /**
   * End whatever turn is waiting, once.
   *
   * <p>The single choke point for a persistent turn, which is why the stop closure is cleared here:
   * every way a turn can end goes through this method, so there is no route that leaves a stale
   * closure behind for a late stop to find.</p>
   */
  private settle(result: TurnResult): void {
    this.endTurnAsStopped = undefined;
    const pending = this.pending;
    if (pending === undefined) {
      return;
    }
    this.pending = undefined;
    pending.cancelBudget();
    pending.settle(withUsage(result, this.perTurnUsage()));
  }

  /**
   * What the turn now ending cost, from what the vendor reported and what the thread reported before.
   *
   * <p>Called exactly once per turn — from `settle` for a persistent vendor and from `finish` for a
   * per-turn one — because it MOVES the baseline as well as reading it. Calling it twice for one turn
   * would difference a turn against itself and report the second half as free.</p>
   */
  private perTurnUsage(raw: ReportedUsage | undefined = this.lastUsage): ReportedUsage | undefined {
    this.lastUsage = undefined;
    if (raw === undefined) {
      return undefined;
    }
    const cumulative = this.adapter.cumulative;
    const tokens = turnTokens(cumulative, raw, this.threadTotals);
    const costUsd = turnCost(cumulative, raw, this.threadTotals);
    // The RAW figures, never the differenced ones: the next turn subtracts from what the vendor last
    // SAID, and subtracting from an already-differenced number would make every turn but the first
    // the difference of two differences.
    this.threadTotals = raw;

    return { tokensIn: tokens.tokensIn, tokensOut: tokens.tokensOut, costUsd };
  }

  /**
   * Kill the process and forget it. Idempotent — `dispose` and a budget can both arrive.
   *
   * <p>Named for what it does rather than for what asks for it, because a PUBLIC `stop` now exists
   * beside it and means something narrower: this ends the process, that ends the turn.</p>
   */
  private killChild(): void {
    // A killed process takes the conversation with it exactly as a dead one does - the budget that
    // killed it does not make the loss less real, and the next answer must still say so. UNLESS the
    // vendor keeps the conversation itself: a per-turn child exits after every single answer, and
    // reporting that as a loss would put "this answer does not remember the earlier ones" under
    // every turn of a conversation that remembers all of them.
    this.contextLost = this.adapter.shape === 'persistent' && (this.contextLost || this.everSent);
    const child = this.child;
    this.child = undefined;
    this.ready = false;
    // Past this line no callback of that child is ours any more.
    this.generation += 1;
    this.unsubscribe?.();
    this.unsubscribe = undefined;
    child?.kill();
  }
}

/** A per-turn child that exited without an answer, with whatever it gave away about why. */
function leftWithNothing(code: number, stderr: string): string {
  const tail = stderr.trim().slice(-400);
  const said = tail.length === 0 ? '' : `: ${tail}`;

  return code === 0
    ? `the model’s process ended without answering${said}`
    : `the model’s process ended with code ${code}${said}`;
}

/** What a child left behind, or that it left nothing — never a sentence ending in a bare colon. */
function ended(stderr: string): string {
  const tail = stderr.trim().slice(-400);

  return tail.length === 0 ? 'the model’s process ended unexpectedly' : `the model’s process ended: ${tail}`;
}

/** A thrown thing, as a sentence. */
function message(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

/** The last resort: a turn that threw where nothing was supposed to. */
function failed(reason: unknown): TurnResult {
  return { ok: false, failure: `the turn failed unexpectedly: ${message(reason)}` };
}

/**
 * A result with the turn's usage on it, or exactly the result it was given.
 *
 * <p>A spread rather than `usage: maybeUndefined`, and the difference is visible from outside: with
 * `exactOptionalPropertyTypes`, writing the key with an undefined value puts the KEY there, and every
 * test that compares a whole result would then see a shape it did not have before. A turn nobody
 * reported numbers for must look exactly as it always did.</p>
 *
 * <p>It branches on `ok` rather than spreading the union, because the two arms are different types
 * and a spread of the union widens both.</p>
 */
function withUsage(result: TurnResult, usage: ReportedUsage | undefined): TurnResult {
  if (usage === undefined) {
    return result;
  }

  return result.ok ? { ...result, usage } : { ...result, usage };
}
