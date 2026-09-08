import { ProcessHandle, Unsubscribe } from './processLauncher';
import { ChatSession, TurnBudgets, TurnResult } from './chatSession';

/**
 * A conversation held in one long-lived vendor process.
 *
 * <p>The protocol was MEASURED, not read out of a manual: `agy --input-format stream-json
 * --output-format stream-json` takes one NDJSON message per line and answers `init`, then
 * `step_update`s, then a `result`, per turn. Three turns down one pipe were answered by one process
 * with the context preserved — turn 3 resolved "now simpler" against turn 2 without the passage
 * being repeated. The schema came out of the binary's own error strings after it refused `type:`
 * with `stream input message is missing the "event" field`.</p>
 *
 * <p><b>A long-lived child has four ways to end, and only one of them is a person closing a tab.</b>
 * The first version of the plan described that one; the gate raised the other three as five separate
 * findings. They are the shape of this file:</p>
 *
 * <ol>
 *   <li><b>Disposal</b> — the tab closed. The tree is killed.</li>
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

export class CliChatSession implements ChatSession {
  private child: ProcessHandle | undefined;
  private unsubscribe: Unsubscribe | undefined;
  private ready = false;
  private pending: Pending | undefined;
  private waitingForInit: ((started: string) => void) | undefined;
  /**
   * What the start already came to, when it came to it before anybody was waiting.
   *
   * <p>The launcher REPLAYS what a child said before its first subscriber — which is the whole
   * reason it does, and it means `init`, or a death, can land INSIDE the subscription below, at a
   * moment when there is nobody to tell. The first version waited for a signal that had already been
   * sent and hung for the entire startup budget on every turn. Found by its own tests.</p>
   */
  private startOutcome: string | undefined;
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
    // caller waits rather than opening a second turn on the same pipe.
    const mine = this.queue.then(() => this.turn(text));
    this.queue = mine.catch(() => undefined);

    return mine;
  }

  dispose(): void {
    this.disposed = true;
    this.settle({ ok: false, failure: 'the conversation was closed' });
    this.stop();
  }

  /** One turn, start to finish, with nothing else in the pipe. */
  private async turn(text: string): Promise<TurnResult> {
    if (this.disposed) {
      return { ok: false, failure: 'the conversation was closed' };
    }

    const started = await this.ensureStarted();
    if (started !== '') {
      return { ok: false, failure: started };
    }

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
        this.settle({ ok: false, failure: 'the model did not answer in time' });
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
    const child = this.start();
    this.child = child;
    this.ready = false;
    this.listen(child);

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
        this.stop();
        resolve('the model’s process did not start');
      });
      this.waitingForInit = (failure: string) => {
        cancelBudget();
        this.waitingForInit = undefined;
        resolve(failure);
      };
    });
  }

  private listen(child: ProcessHandle): void {
    this.unsubscribe = child.onLine((line) => this.onLine(line));
    child.onError((reason) => this.onGone(`the model’s process failed to run: ${reason}`));
    child.onExit(() => this.onGone(`the model’s process ended: ${child.stderrTail().trim().slice(-400)}`));
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
      this.settle({ ok: true, answer: answer.trim() });

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
    this.startOutcome = failure;
    this.waitingForInit?.(failure);
    this.settle({ ok: false, failure });
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
    const child = this.child;
    this.child = undefined;
    this.ready = false;
    this.unsubscribe?.();
    this.unsubscribe = undefined;
    child?.kill();
  }
}
