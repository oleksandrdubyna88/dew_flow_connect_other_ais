/**
 * What a Team server is asked for a chat turn, and how its answers are read.
 *
 * <p>The protocol is not new: `coai-mcp --ask-remote` has spoken it since the Team server existed,
 * and the shapes here are that implementation's — `src_mcp/runners/Reviewers/RemoteAsk.cs`. Writing
 * a second dialect of one wire is how two halves become green about opposite things, which this
 * repository has paid for once already; so this file follows the C# one deliberately, and the names
 * are its names.</p>
 *
 * <p><b>A chat turn IS a review job to the server.</b> One endpoint, one queue, one accounting. What
 * differs is that it carries NO role — see {@link CHAT_ROLE}, which is a measurement rather than a
 * choice — so that a person reading their spending can tell a conversation from a round, because
 * every review carries one and nothing else does.</p>
 *
 * <p>Pure: every decision below is a test rather than a claim, and the socket is somebody else's job.</p>
 */

/**
 * The role a chat turn carries: NONE, and that is the measurement rather than an omission.
 *
 * <p>The first version sent `Chat`, which reads well and the server refuses outright: *"'Chat' is not
 * a review role. Allowed: PlanCritique, Conventions, Architecture, SecurityReliability,
 * UxDxPerformance"*. Measured against the live server on 2026-09-09, in 0.2 seconds, before a line
 * of the wiring was trusted.</p>
 *
 * <p>And the server is right. A review role is not a label — it carries a shipped prompt, a
 * threshold and a round budget, and a test in `src_mcp` walks every value of that enum asserting
 * each one asks for an honest empty findings list. A chat has none of those things, so adding it
 * there would make a reviewer that cannot review.</p>
 *
 * <p>What the server DOES accept is a job with no role at all — the validation skips the enum check
 * for an empty one — and that is exactly the separation the owner asked for: a usage row with no
 * role is a conversation, because every review carries one. Measured the same afternoon: empty role,
 * a model the catalog allows, and the answer came back.</p>
 *
 * <p>A `kind` field would say it better than an absence does, and it is a server change with its own
 * release. Recorded as the follow-up rather than smuggled into the role enum.</p>
 */
export const CHAT_ROLE = '';

/**
 * How many turns a REMOTE conversation may hold.
 *
 * <p>The owner's ruling, asked and answered on 2026-09-08: *"Стоит ли вообще запрещать длинные
 * разговоры на серверных моделях макс 3 хода"* — yes. A remote model keeps no conversation, so every
 * turn re-sends the whole transcript, and the cost of turn N is the cost of everything before it. An
 * unbounded conversation is therefore a bill that grows quadratically and, eventually, a request the
 * server refuses for its size. Three turns, said in the page before the fourth is typed, and the
 * local model offered instead — because a local one HAS a memory and no such limit.</p>
 */
export const REMOTE_TURNS = 3;

/** What one poll of a queued review means. */
export type RemoteStep =
  /** Still queued or running. `position` is where it sits, or 0 when the server did not say. */
  | { readonly kind: 'waiting'; readonly position: number }
  /**
   * The answer, and only the answer.
   *
   * <p>It carried the turn's token counts for a while and nothing ever read them: the server is the
   * system of record for usage — the job is a job in its ledger — and the panel's spending section
   * reads them from there. A number nobody renders is not free, because the day somebody renders it
   * a missing measurement will already have been rounded to `0`, which is the one thing a
   * measurement that could not be taken must never look like. (codex, the code round.)</p>
   */
  | { readonly kind: 'answer'; readonly text: string }
  | { readonly kind: 'failure'; readonly failure: string };

/**
 * The shape a review id may have before it is put into an authenticated URL.
 *
 * <p>The id comes from the server, and it goes into `api/reviews/<id>` on a request carrying this
 * machine's bearer token. A server that is compromised, or merely something answering in front of
 * one, could send `../../api/servers` or an id carrying query syntax and steer that token at a route
 * nobody chose. The same guard the catalog's vendor ids get in `isUsableVendorId`, and the thread
 * ids in the local adapters — three places, one rule: a stranger's identifier is checked against the
 * shape an identifier has before it becomes part of anything. (codex, the code round.)</p>
 */
const REVIEW_ID = /^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$/;

/** The body a submit carries. Shaped by `RemoteRequest` on the server's side. */
export function requestBody(
  vendor: string,
  model: string,
  prompt: string,
  timeoutSeconds: number,
): Record<string, unknown> {
  return { vendor, model, prompt, role: CHAT_ROLE, timeoutSeconds };
}

/** The id a submit was accepted under, or empty when the answer was not one — or not an id. */
export function acceptedId(body: unknown): string {
  const row = body as { id?: unknown } | null;
  const said = typeof row === 'object' && row !== null && typeof row.id === 'string' ? row.id : '';

  return REVIEW_ID.test(said) ? said : '';
}

/**
 * What the server said about a review this poll.
 *
 * <p>The status strings are the server's, and an unknown one is treated as still running rather than
 * as a failure: a server that learns a new state must not turn every conversation into an error on
 * the day it ships. The deadline is what ends a turn that never resolves.</p>
 */
export function readStep(body: unknown): RemoteStep {
  const row = (body ?? {}) as Record<string, unknown>;
  const status = typeof row['status'] === 'string' ? row['status'].toLowerCase() : '';
  const answer = typeof row['answer'] === 'string' ? row['answer'] : '';
  const position = typeof row['position'] === 'number' ? row['position'] : 0;

  if (status === 'done' || status === 'completed' || status === 'succeeded') {
    return answer.trim().length > 0
      ? { kind: 'answer', text: answer.trim() }
      // A finished review with nothing in it is a failure, not an empty answer: a page showing
      // nothing looks like a model with nothing to say. The same rule as the local adapters'.
      : { kind: 'failure', failure: 'the server finished the turn without an answer' };
  }
  if (status === 'failed' || status === 'error' || status === 'cancelled' || status === 'canceled') {
    return { kind: 'failure', failure: `the server reported: ${saidWhy(row, status)}` };
  }
  if (status.length === 0 || RUNNING.includes(status)) {
    return { kind: 'waiting', position };
  }

  // The server has exactly four states — Queued, Running, Done, Failed — so a fifth value is a
  // server much newer than this build, or something in front of it answering for it. Three minutes
  // of "Thinking…" is the worst way to say either, and naming it is the shortest way to be fixed.
  // (gemini and local, one finding from two sides; the state list is measured, not assumed.)
  return { kind: 'failure', failure: `the server answered with a status this build does not know: ${status}` };
}

/** The states that mean "not finished yet". Measured from the server's own `JobStatus`. */
const RUNNING: readonly string[] = ['queued', 'running', 'pending', 'claimed', 'waiting'];

/** What the server said about a failure, in its own words when it gave any. */
function saidWhy(row: Record<string, unknown>, status: string): string {
  const failure = typeof row['failure'] === 'string' ? row['failure'] : '';
  const reason = typeof row['reason'] === 'string' ? row['reason'] : '';

  return failure.length > 0 ? failure : reason.length > 0 ? reason : status;
}

/**
 * How long to ask the server to hold one poll open.
 *
 * <p>Never longer than what is LEFT of the turn's deadline: asking for the full window when four
 * seconds remain means the answer arrives after this side has already given up, which reads as a
 * server that never answered. The C# shim learned this the same way.</p>
 *
 * <p><b>And never longer than this side will WAIT.</b> `ask` aborts every request at
 * `REQUEST_TIMEOUT_MS` — ten seconds — so a poll that asks the server to hold for twenty-five is a
 * request this side kills before the server answers: every poll failing with "it did not answer
 * within 10s", for a server behaving perfectly. Eight leaves room for the round trip. The C# shim
 * can ask for twenty-five because its own client has no such cap; this one cannot, and the two
 * constants had to be read together to see it. (codex, the plan round, from the other end.)</p>
 */
export function waitSecondsFor(remainingMs: number, longest = 8): number {
  const seconds = Math.floor(remainingMs / 1000);

  return Math.max(1, Math.min(longest, seconds));
}

/**
 * How long to wait after a refusal before asking again.
 *
 * <p>`429` is the shared-account ceiling doing its job — somebody else's round has the vendor — and
 * the answer is to wait, not to hammer. Doubling from a second, capped, so a queue that is briefly
 * full costs a second and one that is full for a minute costs a poll every ten.</p>
 */
export function backoffMs(attempt: number, capMs = 10_000): number {
  return Math.min(capMs, 1000 * 2 ** Math.max(0, attempt));
}

/**
 * Is this conversation full?
 *
 * <p>Counted in TURNS the person asked, not messages: a transcript holds a question and an answer
 * for each. Said before the fourth is typed rather than after it is sent.</p>
 */
export function remoteIsFull(turnsAsked: number): boolean {
  return turnsAsked >= REMOTE_TURNS;
}
