import { ChatSession, TurnBudgets, TurnResult } from './chatSession';
import { RemoteStep, acceptedId, backoffMs, readStep, requestBody, turnKey, waitSecondsFor } from './remoteAsk';
import { Timers } from './cliChatSession';

/**
 * A conversation held by a Team server, which holds no conversation.
 *
 * <p>The same `ChatSession` the local half implements, over a queue instead of a pipe: submit a
 * review, poll it while it waits its turn behind everybody else's, take the answer, and cancel what
 * is still queued when the tab closes. There is no process here to die and nothing to resume — the
 * server forgets each turn the moment it answers — so the caller re-sends the transcript, bounded,
 * and the page says so.</p>
 *
 * <p><b>Everything that touches the world is injected.</b> One function that performs a request and
 * one clock: that is what lets a queue that answers on the fourth poll, a `429` that clears, a
 * server that goes away mid-turn and a deadline that expires all be tests rather than descriptions.
 * It is the same shape `cliChatSession` uses for the launcher and its timers, for the same reason.</p>
 */

/**
 * The shortest gap between two polls.
 *
 * <p>Nothing when the server holds its connection, which is what the long poll is for. Everything
 * when it does not: without it a server answering instantly turns this into a loop that never
 * yields, and in an extension host that means a frozen window rather than a busy one.</p>
 */
const POLL_GAP_MS = 500;

/**
 * What a stopped remote turn is.
 *
 * <p>No `contextLost`. A Team server holds no conversation to lose — the transcript is re-sent on
 * every turn anyway, which is what `forgetful` means — so stopping one costs the answer and nothing
 * else. Saying the conversation restarted here would be a sentence about a loss that did not happen.</p>
 */
const STOPPED: TurnResult = { ok: false, failure: 'you stopped this answer', stopped: true };

/** One request to the server, already carrying its token and its base URL. */
export interface RemoteTransport {
  /** POST a review. Answers the parsed body, or a sentence saying why not. */
  submit(body: Record<string, unknown>): Promise<{ readonly body: unknown; readonly failure: string; readonly status: number }>;
  /** GET one review, holding the connection for up to `waitSeconds`. */
  poll(id: string, waitSeconds: number): Promise<{ readonly body: unknown; readonly failure: string; readonly status: number }>;
  /** DELETE a review that is no longer wanted. Never rejects; nothing waits for it. */
  cancel(id: string): Promise<void>;
}

/** What this session needs to know about the model it is asking. */
export interface RemoteVendor {
  /** The vendor id the SERVER knows — not the row's id, which is `<server>-<vendor>`. */
  readonly vendor: string;
  readonly model: string;
}

export class RemoteChatSession implements ChatSession {
  private inFlight = '';
  private disposed = false;
  private queue: Promise<unknown> = Promise.resolve();

  /**
   * The key of a turn that FAILED, kept so the person's own retry is recognised as one.
   *
   * <p>This is the half that makes the key worth having, and the first version did not have it: a
   * key minted per `send` is a key minted per ATTEMPT, and the attempt this exists for is the second
   * one. The failure is a POST that arrived and whose answer did not come back — the person presses
   * send again on the same question, and with a fresh key the server has no way to know it is the
   * same question and makes a second paid job. (codex, the code round, twice.)</p>
   *
   * <p>Kept only across a FAILURE and only for identical text. A turn that answered is finished, so
   * the next question is a new one; different text is a different question, which the server would
   * refuse under this key anyway — it binds a key to a fingerprint of what was asked.</p>
   */
  private retry: { readonly text: string; readonly key: string } | undefined;

  /** Whether a turn is between its submit and its result. What makes an idle `stop` a no-op. */
  private running = false;

  /**
   * The person stopped THIS turn. Cleared when the next one starts.
   *
   * <p>Deliberately not `disposed`: that flag is terminal and every guard reading it means "this
   * session is over". A stop ends a turn and leaves the session able to take another, so it needs a
   * flag of its own — reusing `disposed` would have made the second question after a stop return
   * "the conversation was closed" for a conversation that is open.</p>
   */
  private stopped = false;

  /** Settles the turn without waiting for the poll in flight. Undefined when no turn is waiting. */
  private wakeOnStop: (() => void) | undefined;

  constructor(
    private readonly transport: RemoteTransport,
    private readonly vendor: RemoteVendor,
    private readonly budgets: TurnBudgets,
    private readonly timers: Timers,
    private readonly now: () => number = Date.now,
    /** Injected so a test can watch the same turn keep one key and two turns take two. */
    private readonly key: () => string = turnKey,
  ) {}

  send(text: string, onWaiting?: (position: number) => void): Promise<TurnResult> {
    // One turn at a time, exactly as the local session does it: the server would take two, and the
    // transcript this side keeps would then be written out of order.
    const mine = this.queue.then(() => this.turn(text, onWaiting)).catch((reason: unknown) => ({
      ok: false as const,
      failure: `the turn failed unexpectedly: ${message(reason)}`,
    }));
    this.queue = mine;

    return mine;
  }

  /**
   * End the turn the server is running, and keep the conversation.
   *
   * <p>Two obligations that pull in opposite directions, which is why this is not simply `dispose`
   * without the flag. The person must see the turn end AT ONCE — a poll can be holding the
   * connection for eight seconds and waiting that out is the very complaint this feature answers —
   * and the server must be TOLD, because a queued job holds a slot on a shared vendor account and
   * the drop-on-no-poll sweep would not take it back for three minutes.</p>
   *
   * <p>So the cancel goes out here, and `wakeOnStop` settles the turn without waiting for the poll.
   * Whatever that poll eventually answers is dropped: `turn` races the two and a race settles once,
   * so an answer landing after a stop cannot turn a stopped turn into an answered one. (gemini and
   * codex, the plan round, independently.)</p>
   */
  stop(): void {
    if (this.disposed || !this.running) {
      // Nothing is in flight — an idle session, or a stop that lost a race with the answer it meant
      // to prevent. Cancelling a job id this session no longer owns would be cancelling somebody's
      // finished work for nothing.
      return;
    }
    this.stopped = true;
    const id = this.inFlight;
    this.inFlight = '';
    if (id.length > 0) {
      void this.transport.cancel(id);
    }
    // Undefined in the window between `submit` going out and its id coming back. That window is not
    // lost: `turn` checks `stopped` where the id first exists, exactly as it already checks
    // `disposed` there, and cancels then.
    this.wakeOnStop?.();
  }

  dispose(): void {
    this.disposed = true;
    const id = this.inFlight;
    this.inFlight = '';
    if (id.length > 0) {
      // A queued review nobody is waiting for is a vendor slot somebody else could have had, and on
      // a shared account that is the scarcest thing the server has. Cancelled, not abandoned.
      void this.transport.cancel(id);
    }
  }

  private async turn(text: string, onWaiting?: (position: number) => void): Promise<TurnResult> {
    if (this.disposed) {
      return { ok: false, failure: 'the conversation was closed' };
    }
    // A NEW turn, so last turn's stop is spent. Reset here rather than in `stop` itself, because
    // between the two is exactly where a person decides whether to ask again.
    this.stopped = false;
    this.running = true;
    try {
      return await this.submitAndWait(text, onWaiting);
    } finally {
      this.running = false;
      this.wakeOnStop = undefined;
      this.inFlight = '';
    }
  }

  /** The turn proper, with `running` already true so a stop arriving mid-flight is not a no-op. */
  private async submitAndWait(text: string, onWaiting?: (position: number) => void): Promise<TurnResult> {
    const deadline = this.now() + this.budgets.turnMs;
    const seconds = Math.max(1, Math.floor(this.budgets.turnMs / 1000));
    // One name for this TURN — the SAME one when this is the person retrying a turn that failed,
    // which is the whole point: the server then hands back the job the first attempt made rather
    // than starting a second on an account where a slot is the scarcest thing there is. A server
    // that has not learned the field ignores it, which is measured rather than assumed.
    const key = this.retry?.text === text ? this.retry.key : this.key();
    const sent = await this.transport.submit(
      requestBody(this.vendor.vendor, this.vendor.model, text, seconds, key),
    );
    if (sent.failure.length > 0) {
      // Kept, because THIS is the case the key exists for: the request may well have been accepted
      // and only its answer lost, and the next press of send must be able to say so.
      this.retry = { text, key };

      return { ok: false, failure: sent.failure };
    }
    // Accepted, so there is nothing left to repeat: from here the id is what identifies the turn.
    this.retry = undefined;
    const id = acceptedId(sent.body);
    if (id.length === 0) {
      // A 2xx whose body is not a review: something in front of the server can answer 200 with a
      // login page, and reporting that as an answer would put a login page in the conversation.
      return { ok: false, failure: 'the server accepted the question but did not say which review it is' };
    }
    // The window `dispose` cannot see: it ran while the POST was in flight, found `inFlight` empty
    // and cancelled nothing — and the job the server has just accepted would then run, hold a vendor
    // slot on a shared account and answer into a tab that closed. Checked HERE, where the id first
    // exists, because that is the first moment there is anything to cancel. (codex, the code round.)
    if (this.disposed) {
      void this.transport.cancel(id);

      return { ok: false, failure: 'the conversation was closed' };
    }
    // The same window, for the same reason, for a stop instead of a close: `stop` ran while the POST
    // was in flight, found no id to cancel and settled nothing. Checked HERE because this is the
    // first moment the job has a name. (The dispose case above is the precedent; a stop needed it
    // too and would otherwise have left the job running for the sweep to find.)
    if (this.stopped) {
      void this.transport.cancel(id);

      return STOPPED;
    }
    this.inFlight = id;

    const waiting = this.waitFor(id, deadline, onWaiting);
    // Attached BEFORE the race. If the stop wins, this promise still settles later with nobody
    // reading it, and a rejection nobody handles is an unhandled rejection that takes the window
    // down rather than the turn.
    waiting.catch(() => undefined);

    return Promise.race([
      waiting,
      new Promise<TurnResult>((resolve) => {
        this.wakeOnStop = () => resolve(STOPPED);
      }),
    ]);
  }

  /** Poll until it answers, fails, or the deadline passes. */
  private async waitFor(
    id: string,
    deadline: number,
    onWaiting?: (position: number) => void,
  ): Promise<TurnResult> {
    let refusals = 0;
    // `stopped` as well as `disposed`: the race has already settled the turn, and this loop carrying
    // on would keep polling a job the server has been told to cancel.
    while (!this.disposed && !this.stopped) {
      const remaining = deadline - this.now();
      if (remaining <= 0) {
        void this.transport.cancel(id);

        return { ok: false, failure: 'the server did not answer in time' };
      }

      const asked = await this.transport.poll(id, waitSecondsFor(remaining));
      if (asked.status === 429) {
        // The shared-account ceiling doing its job: somebody else's round has the vendor. Waiting is
        // the answer, and the wait grows so that a queue full for a minute is not polled every second.
        refusals += 1;
        await this.pause(Math.min(backoffMs(refusals), Math.max(0, deadline - this.now())));
        continue;
      }
      if (asked.failure.length > 0) {
        return { ok: false, failure: asked.failure };
      }
      refusals = 0;

      const step: RemoteStep = readStep(asked.body);
      if (step.kind === 'answer') {
        return { ok: true, answer: step.text };
      }
      if (step.kind === 'failure') {
        return { ok: false, failure: step.failure };
      }
      // Where they are in the queue, said out loud. A shared Team server queues twenty deep per
      // person by design, so "still waiting" can be minutes — and the position was already parsed
      // here and thrown away, which left an unchanging spinner as the only thing a person could tell
      // a busy server from a broken tab by. (gemini, the code round.)
      onWaiting?.(step.position);
      // Still waiting. The server is SUPPOSED to have held the connection for as long as it was
      // asked to, in which case this pause is nothing beside it — but a server that answers
      // "queued" the instant it is asked would otherwise spin this loop with no yield at all, and
      // an async loop over resolved promises starves the event loop rather than merely being busy.
      // Found by the test for closing a tab, which could not get its own callback to run.
      await this.pause(Math.min(POLL_GAP_MS, Math.max(0, deadline - this.now())));
    }

    return { ok: false, failure: 'the conversation was closed' };
  }

  private pause(ms: number): Promise<void> {
    return new Promise((resolve) => {
      const cancel = this.timers.after(ms, () => {
        cancel();
        resolve();
      });
    });
  }
}

/** A thrown thing, as a sentence. */
function message(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}
