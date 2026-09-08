import { ProcessHandle, Unsubscribe } from './processLauncher';
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

/** One line of the CLI's output, as far as this file cares. */
interface CliEvent {
  readonly event?: unknown;
  readonly result?: {
    readonly status?: unknown;
    readonly response?: unknown;
    readonly error?: unknown;
  };
}

/** What a turn is waiting for, and how to end its wait. */
interface Pending {
  readonly settle: (result: TurnResult) => void;
  readonly cancelBudget: () => void;
}

const CLOSED = 'the conversation was closed';

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
  /** The next answer comes from a process that never heard the earlier turns. Told once, then cleared. */
  private contextLost = false;
  private queue: Promise<unknown> = Promise.resolve();
  private disposed = false;

  /**
   * @param start launches the vendor process — injected, so this file spawns nothing itself
   * @param budgets how long the start and each turn may take
   * @param timers the clock, injected so a test can expire a budget in a millisecond
   */
  constructor(
    private readonly start: () => ProcessHandle,
    private readonly budgets: TurnBudgets,
    private readonly timers: Timers = REAL_TIMERS,
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
    this.stop();
  }

  /** One turn, start to finish, with nothing else in the pipe. */
  private async turn(text: string): Promise<TurnResult> {
    if (this.disposed) {
      return { ok: false, failure: CLOSED };
    }

    const started = await this.ensureStarted();
    if (started !== '') {
      return { ok: false, failure: started };
    }

    this.everSent = true;
    const line = JSON.stringify({ event: 'user', message: { role: 'user', content: text } });
    if (this.child?.writeLine(line) !== true) {
      // The pipe went between the last event and this write. Not an error to throw: the next send
      // starts a new process, which is what a person pressing Enter again expects to happen.
      this.stop();

      return { ok: false, failure: 'the model’s process had already gone; ask again to start a new one' };
    }

    return new Promise<TurnResult>((resolve) => {
      const cancelBudget = this.timers.after(this.budgets.turnMs, () => {
        // Killed, not merely abandoned: a process that has stopped answering must not be handed the
        // next turn as though nothing happened.
        this.stop();
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
      child = this.start();
    } catch (reason) {
      // `send` promises never to reject, and an injected launcher is somebody else's code.
      return Promise.resolve(`the model’s process could not be started: ${message(reason)}`);
    }
    this.child = child;
    this.ready = false;
    this.generation += 1;
    this.listen(child, this.generation);

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
        this.stop();
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

  /** One line of NDJSON. Anything unreadable is skipped: a CLI may log where it pleases. */
  private onLine(line: string): void {
    let event: CliEvent;
    try {
      event = JSON.parse(line) as CliEvent;
    } catch {
      return;
    }

    if (event.event === 'init') {
      this.ready = true;
      this.waitingForInit?.('');

      return;
    }
    if (event.event !== 'result') {
      return;
    }

    const status = typeof event.result?.status === 'string' ? event.result.status : '';
    const answer = typeof event.result?.response === 'string' ? event.result.response : '';
    const error = typeof event.result?.error === 'string' ? event.result.error : '';
    if (status === 'SUCCESS') {
      const lost = this.contextLost;
      this.contextLost = false;
      this.settle(lost
        ? { ok: true, answer: answer.trim(), contextLost: true }
        : { ok: true, answer: answer.trim() });

      return;
    }
    // An ERROR result is an error, not an empty answer. The CLI reports a refused input this way -
    // it is how the NDJSON schema was discovered - and a page showing nothing would look like a
    // model with nothing to say.
    this.settle({ ok: false, failure: error.length > 0 ? error : `the model answered with ${status || 'no status'}` });
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

  /** End whatever turn is waiting, once. */
  private settle(result: TurnResult): void {
    const pending = this.pending;
    if (pending === undefined) {
      return;
    }
    this.pending = undefined;
    pending.cancelBudget();
    pending.settle(result);
  }

  /** Kill the process and forget it. Idempotent — `dispose` and a budget can both arrive. */
  private stop(): void {
    // A killed process takes the conversation with it exactly as a dead one does - the budget that
    // killed it does not make the loss less real, and the next answer must still say so.
    this.contextLost = this.contextLost || this.everSent;
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
