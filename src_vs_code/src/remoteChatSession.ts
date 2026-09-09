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
   * Which turn is current. Bumped by every new turn AND by every stop.
   *
   * <p>A GENERATION rather than a boolean, and the difference is a defect three reviewers found in
   * the first version. That one used a `stopped` flag reset at the start of each turn — so a poll
   * loop belonging to turn A, sitting in an `await` when A was stopped, woke up after turn B had
   * cleared the flag, decided it was not stopped after all, and carried on polling A's cancelled job
   * and pushing A's queue positions into B's tab. A number cannot be un-stopped: turn A captures its
   * own value and every later stop or turn moves the session past it for good.</p>
   */
  private generation = 0;

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
    // Past this line the turn that was running is stale for good, whatever it is awaiting and
    // whatever turn comes next.
    this.generation += 1;
    const id = this.inFlight;
    this.inFlight = '';
    if (id.length > 0) {
      this.drop(id);
    }
    // Undefined only in the window between `submit` going out and its id coming back; that window is
    // covered where the id first exists, by the same staleness check.
    this.wakeOnStop?.();
  }

  /**
   * Tell the server to drop a job, and never let saying so break anything.
   *
   * <p>Nothing waits for this and nothing can act on its failure — the turn is already over on this
   * side. What matters is that a `DELETE` refused by a network blip becomes a dropped promise rather
   * than an unhandled rejection in the extension host, which is a window taken down for a job that
   * the server's own sweep will collect anyway. (codex and gemini, the code round.)</p>
   */
  private drop(id: string): void {
    void this.transport.cancel(id).catch(() => undefined);
  }

  /** Whether the turn that captured `mine` is still the one this session is running. */
  private stale(mine: number): boolean {
    return this.disposed || mine !== this.generation;
  }

  dispose(): void {
    this.disposed = true;
    const id = this.inFlight;
    this.inFlight = '';
    if (id.length > 0) {
      // A queued review nobody is waiting for is a vendor slot somebody else could have had, and on
      // a shared account that is the scarcest thing the server has. Cancelled, not abandoned.
      this.drop(id);
    }
  }

  private async turn(text: string, onWaiting?: (position: number) => void): Promise<TurnResult> {
    if (this.disposed) {
      return { ok: false, failure: 'the conversation was closed' };
    }
    const mine = this.generation + 1;
    this.generation = mine;
    this.running = true;
    // The stop signal is armed BEFORE the submit, and the race covers the submit as well as the
    // polling. Armed after, a stop pressed while the POST was still in flight had no resolver to
    // call and nothing to cancel, so the person went on waiting out a request that can take twenty
    // seconds. (codex, the code round, twice.)
    const stopping = new Promise<TurnResult>((resolve) => {
      this.wakeOnStop = () => resolve(STOPPED);
    });
    const asking = this.submitAndWait(text, mine, onWaiting);
    // Attached before the race: the loser still settles, with nobody reading it, and a rejection
    // nobody handles is an unhandled rejection in the extension host.
    asking.catch(() => undefined);

    try {
      return await Promise.race([stopping, asking]);
    } finally {
      this.running = false;
      this.wakeOnStop = undefined;
      this.inFlight = '';
    }
  }

  /** Submit the question and wait for its answer; `mine` is the generation this turn belongs to. */
  private async submitAndWait(
    text: string,
    mine: number,
    onWaiting?: (position: number) => void,
  ): Promise<TurnResult> {
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
    // A close or a stop that ran while the POST was in flight found no id to cancel, because there
    // was none yet. Checked HERE because this is the first moment the job has a name — otherwise it
    // runs to the end of its budget on a shared vendor account and answers into nothing. The two
    // keep their own sentences: a closed tab and a stopped answer send a person to different places.
    if (this.stale(mine)) {
      this.drop(id);

      return this.disposed ? { ok: false, failure: 'the conversation was closed' } : STOPPED;
    }
    this.inFlight = id;

    return this.waitFor(id, deadline, mine, onWaiting);
  }

  /** Poll until it answers, fails, or the deadline passes. */
  private async waitFor(
    id: string,
    deadline: number,
    mine: number,
    onWaiting?: (position: number) => void,
  ): Promise<TurnResult> {
    let refusals = 0;
    // Staleness, not just disposal: once this turn has been stopped the race has already answered
    // its caller, and carrying on would poll a job the server has been told to drop.
    while (!this.stale(mine)) {
      const remaining = deadline - this.now();
      if (remaining <= 0) {
        this.drop(id);

        return { ok: false, failure: 'the server did not answer in time' };
      }

      const asked = await this.transport.poll(id, waitSecondsFor(remaining));
      // Checked the instant the await returns, not at the top of the next iteration. A poll holds the
      // connection for up to eight seconds, and a stop pressed during one must not be answered by
      // reading its result, calling this turn's `onWaiting` into a tab now showing a different turn,
      // or looping once more. (gemini, codex and the local reviewer, the code round.)
      if (this.stale(mine)) {
        break;
      }
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
