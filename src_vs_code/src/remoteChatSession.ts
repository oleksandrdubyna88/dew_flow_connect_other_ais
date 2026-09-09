import { ChatSession, TurnBudgets, TurnResult } from './chatSession';
import { RemoteStep, acceptedId, backoffMs, readStep, requestBody, waitSecondsFor } from './remoteAsk';
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

  constructor(
    private readonly transport: RemoteTransport,
    private readonly vendor: RemoteVendor,
    private readonly budgets: TurnBudgets,
    private readonly timers: Timers,
    private readonly now: () => number = Date.now,
  ) {}

  send(text: string): Promise<TurnResult> {
    // One turn at a time, exactly as the local session does it: the server would take two, and the
    // transcript this side keeps would then be written out of order.
    const mine = this.queue.then(() => this.turn(text)).catch((reason: unknown) => ({
      ok: false as const,
      failure: `the turn failed unexpectedly: ${message(reason)}`,
    }));
    this.queue = mine;

    return mine;
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

  private async turn(text: string): Promise<TurnResult> {
    if (this.disposed) {
      return { ok: false, failure: 'the conversation was closed' };
    }
    const deadline = this.now() + this.budgets.turnMs;
    const seconds = Math.max(1, Math.floor(this.budgets.turnMs / 1000));
    const sent = await this.transport.submit(
      requestBody(this.vendor.vendor, this.vendor.model, text, seconds),
    );
    if (sent.failure.length > 0) {
      return { ok: false, failure: sent.failure };
    }
    const id = acceptedId(sent.body);
    if (id.length === 0) {
      // A 2xx whose body is not a review: something in front of the server can answer 200 with a
      // login page, and reporting that as an answer would put a login page in the conversation.
      return { ok: false, failure: 'the server accepted the question but did not say which review it is' };
    }
    this.inFlight = id;

    try {
      return await this.waitFor(id, deadline);
    } finally {
      this.inFlight = '';
    }
  }

  /** Poll until it answers, fails, or the deadline passes. */
  private async waitFor(id: string, deadline: number): Promise<TurnResult> {
    let refusals = 0;
    while (!this.disposed) {
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
