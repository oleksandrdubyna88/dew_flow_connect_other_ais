import { type CheckRecord, type CheckRunResult, MODE_UNKNOWN, parseCheckDocument } from './consultantHealth';
import { refusalIn } from './consultations';

/**
 * One Check press, as a process: `coai-mcp --check-consultant --caller <kind>` — E5.3 of
 * `research/PLAN_the_consultant_works_on_every_vendor.md`.
 *
 * <p>Through ports, so the exit-code contract and the kill cap are tests: the spawn is `serverRun` in production (the
 * door that carries this window's data directory — a check run against the default directory would write its state
 * where this panel is not reading), the state file is read through the same port the watcher uses, and the clock and
 * the poll are handed in.</p>
 *
 * <p><b>Exit codes</b> (`.agents/PROJECT.md`): 0 and one JSON document for every classified outcome — answered,
 * failed, unavailable, already-checking; 64 for a binary that has never heard of the mode, which is "update the MCP
 * server" and nothing else; 65 for a request it refused. The server writes a refusal's sentence to stderr, which
 * `capture` does not collect — so the sentence is shown when stdout has one and the exit code is named when it has
 * none. Anything else is a crash, named.</p>
 *
 * <p><b>The kill cap</b> is epic 5's plan-round rule. The process STARTS under {@link CHECK_INITIAL_CAP_MS}, a static,
 * generous allowance: no file is read at launch, because a previous check's `<kind>.check.json` carries an expired
 * deadline and on a first run there is none. The cap is TIGHTENED — to the check's own `deadlineUtc`, which already
 * includes the server's teardown, plus {@link CHECK_DEADLINE_SLACK_MS} — only from a state file whose `startedUtc` is
 * newer than the spawn, i.e. one this process wrote. It is never extended. An early exit (64, 65) ends the run at once,
 * whatever the cap, and the poll stops with it.</p>
 */

/**
 * The static cap a check starts under. The server's own budget is at most four minutes of turn plus two of setup plus
 * the teardown allowance; fifteen is well past that, so only a check that ignored its own deadline ever meets it.
 */
export const CHECK_INITIAL_CAP_MS = 15 * 60_000;

/** How far past its own promised end a check may run before the panel kills it. */
export const CHECK_DEADLINE_SLACK_MS = 30_000;

/** How often the state file is read for the deadline the check wrote. */
export const CHECK_STATE_POLL_MS = 2_000;

export interface CheckRunPorts {
  /** Spawns the server with these arguments; `stop` is asked repeatedly and true KILLS the process (`capture`). */
  readonly run: (args: readonly string[], capMs: number, stop: () => boolean, input: string) => Promise<{ code: number; output: string }>;
  /** This side's `<kind>.check.json`, as it is now — undefined when there is none or it cannot be read. */
  readonly readState: () => Promise<CheckRecord | undefined>;
  readonly nowMs: () => number;
  /** Calls `tick` every `ms` until the returned function is called. */
  readonly every: (ms: number, tick: () => void) => () => void;
}

/** Runs one check to its end and says what came of it. Never rejects for a state read that failed. */
/** A model's check is kept under `model-<row id>` — the record `--check-model` writes (PLAN_one_model_catalog.md D10, E2.4). */
export const MODEL_CHECK = 'model-';

/** Whether a check key is a catalog row's rather than a caller kind's. */
export function isModelCheck(kind: string): boolean {
  return kind.startsWith(MODEL_CHECK) && kind.length > MODEL_CHECK.length;
}

/** The arguments of one check: a caller kind's consultant by name, or a catalog row read on stdin (`--check-model`). */
export function checkArgs(kind: string): readonly string[] {
  return isModelCheck(kind) ? ['--check-model'] : ['--check-consultant', '--caller', kind];
}

/**
 * @param input what the server reads on stdin — a model check's `{"row": …}`; empty for a caller kind's
 */
export async function runConsultantCheck(kind: string, ports: CheckRunPorts, input = ''): Promise<CheckRunResult> {
  // A model's check with no row to read: the row was removed while the paid turn was being confirmed. Nothing is spawned.
  if (isModelCheck(kind) && input.length === 0) {
    return { kind: 'crashed', why: 'the model was removed before its check could run — nothing was sent' };
  }
  const spawnedMs = ports.nowMs();
  let capAtMs = spawnedMs + CHECK_INITIAL_CAP_MS;
  const stopPolling = ports.every(CHECK_STATE_POLL_MS, () => {
    // The detached edge: a read that failed is a tick that learned nothing, never the run's failure.
    ports.readState().then((state) => { capAtMs = tightenedCap(capAtMs, spawnedMs, state); }, () => undefined);
  });
  try {
    const { code, output } = await ports.run(checkArgs(kind), CHECK_INITIAL_CAP_MS, () => ports.nowMs() > capAtMs, input);

    return resultOf(code, output);
  } finally {
    stopPolling();
  }
}

/**
 * The cap after reading a state file: its deadline plus slack — but only from a file this check wrote (started at or
 * after the spawn), only when the deadline parses, and only ever EARLIER than the cap it had.
 */
export function tightenedCap(capAtMs: number, spawnedMs: number, state: CheckRecord | undefined): number {
  if (state === undefined || !(Date.parse(state.startedUtc) >= spawnedMs)) {
    return capAtMs;
  }
  const promised = Date.parse(state.deadlineUtc) + CHECK_DEADLINE_SLACK_MS;

  return Number.isFinite(promised) ? Math.min(capAtMs, promised) : capAtMs;
}

/** Exit 0 needs a document; 64 and 65 mean what the contract says; anything else is a crash, named. */
function resultOf(code: number, output: string): CheckRunResult {
  switch (code) {
    case 0:
      return reported(output);
    case MODE_UNKNOWN:
      return { kind: 'too-old' };
    case REFUSED:
      return { kind: 'refused', said: refusalSaid(output) };
    default:
      return { kind: 'crashed', why: crashOf(code) };
  }
}

/** `EX_DATAERR`: the binary knows the mode and refused the request. */
const REFUSED = 65;

/** The server's sentence when stdout carries one; its refusal otherwise went to stderr, which is not collected. */
function refusalSaid(output: string): string {
  return output.trim().length > 0 ? refusalIn(output) : 'the server refused the request (exit 65); its reason went to its error stream';
}

function reported(output: string): CheckRunResult {
  const record = parseCheckDocument(output);

  return record === undefined
    ? { kind: 'crashed', why: 'the check ended without printing a result this panel can read' }
    : { kind: 'reported', record };
}

function crashOf(code: number): string {
  return code === -1
    ? 'it was stopped — it ran past its own deadline, or the server could not be started'
    : `the server exited ${code}${code === 74 ? ', which means it could not use its data directory' : ''}`;
}
