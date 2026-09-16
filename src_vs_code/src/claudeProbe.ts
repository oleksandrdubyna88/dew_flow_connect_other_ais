import { CLAUDE_CANDIDATES, ProbeResult, ProbedModel, answeredAsAsked, modelThatAnswered } from './claudeModels';

/**
 * Running the probe, and the rule that it must never cost anybody a model.
 *
 * <p>The decisions are in `claudeModels.ts`, which is pure. This is the part that spends time and
 * money: one small request per candidate, through the same `capture` seam that already runs
 * `agy models` and every `--version` check — which kills the process TREE on timeout, never lets a
 * spawn error escape, and answers `code: -1` for a throw, a timeout and an unreadable exit alike.</p>
 *
 * <p><b>A candidate nobody could ask about is UNVERIFIED, never absent.</b> The three ways this run
 * fails are all states this installation is really in from time to time: an allowance that is spent
 * (issue #165 measured four reviewers meeting exactly that), a CLI that is not installed, and a first
 * run before anything has been asked. None of them may subtract from what a person can choose, so a
 * failure returns the previous answer rather than an empty one.</p>
 */

/** What this needs from the outside — so the whole thing is testable without spending a request. */
export interface ProbePorts {
  /** `versionProbe.ts`'s own `capture`, narrowed to what this uses. */
  readonly run: (args: readonly string[]) => Promise<{ code: number; output: string }>;
  /** Which binary `run` launches, so the answer can say what it is evidence about. */
  readonly executable?: string | undefined;
  /** The CLI's own version, so an answer is not inherited by a different binary. */
  readonly cliVersion: () => Promise<string>;
  readonly now: () => number;
}

/** Long enough for a small answer, short enough that four of them are not a minute. */
export const PROBE_CAP_MS = 25_000;

/** The prompt. One word, because the answer is thrown away and only `modelUsage` is read. */
export const PROBE_PROMPT = 'hi';

/**
 * How many candidates in a row may fail to run before the CLI is judged absent.
 *
 * <p>Two rather than one, because one is a hiccup and two in a row is a binary that is not there.
 * Two rather than four, because asking a CLI that cannot start four times is four timeouts a
 * person waits through to learn what the first two already said.</p>
 */
export const GIVE_UP_AFTER = 2;

/**
 * Ask the CLI about each candidate, once.
 *
 * <p>Sequential rather than parallel, deliberately: four concurrent requests against an account that
 * is near its limit is how a probe becomes the thing that exhausts it, and the caller runs this in
 * the background anyway. The whole run is abandoned the moment {@link ProbePorts.run} reports a CLI
 * that could not be started — there is nothing to learn from three more of the same failure.</p>
 */
export async function probeClaudeModels(
  ports: ProbePorts,
  candidates: readonly string[] = CLAUDE_CANDIDATES,
  /** Asked before every candidate: true means whoever wanted this has gone, so stop spending. */
  givenUp: () => boolean = () => false,
): Promise<ProbeResult | undefined> {
  const cliVersion = await ports.cliVersion();
  if (cliVersion.length === 0) {
    return undefined;
  }
  const models: ProbedModel[] = [];
  let couldNotRun = 0;
  for (const asked of candidates) {
    if (givenUp()) {
      // The window closed, or a newer probe started. Four candidates is up to a hundred seconds of
      // BILLED requests, and finishing them for an answer nobody will read is the worst outcome
      // available. (gemini, this round.)
      break;
    }
    const { code, output } = await ports.run(['--model', asked, '-p', PROBE_PROMPT, '--output-format', 'json']);
    if (code === -1) {
      // `capture` answers -1 for a spawn error, a timeout and an unreadable exit alike, so this
      // cannot tell "no such binary" from "that one request hung". Both were treated as the first
      // and the whole run was abandoned — which meant one transient hiccup on `haiku` left sonnet,
      // opus and fable unasked for a week. Now only a CLI that has failed to run TWICE RUNNING is
      // given up on; a single failure costs its own candidate and nothing else.
      couldNotRun += 1;
      if (couldNotRun >= GIVE_UP_AFTER) {
        break;
      }
      continue;
    }
    couldNotRun = 0;
    const answered = modelThatAnswered(output);
    models.push({ asked, answered, verified: answeredAsAsked(asked, answered) });
  }

  // NOTHING LEARNED IS NOT AN ANSWER. Writing an empty result would age into the cache and read, a
  // minute later, as "this machine reaches no Claude models at all".
  return models.length === 0
    ? undefined
    : {
      cliVersion,
      executable: ports.executable ?? '',
      checkedUtc: new Date(ports.now()).toISOString(),
      models,
    };
}

/**
 * Did this answer reach every candidate?
 *
 * <p>An abandoned run and a complete one are the same SHAPE, and the caller needs to tell them
 * apart: VS Code disposes a view when it is hidden, so switching to another sidebar view mid-probe
 * stops it — and a trigger that read that as a success would never ask again this session, leaving
 * the candidates it never reached saying "not asked yet" until the editor restarted.</p>
 */
export function askedEverything(
  probe: ProbeResult | undefined,
  candidates: readonly string[] = CLAUDE_CANDIDATES,
): boolean {
  return candidates.every((one) => (probe?.models ?? []).some((m) => m.asked === one));
}

/**
 * The answer to keep, given what was found and what was already held.
 *
 * <p>Its own function because it is the rule the plan round pressed hardest on, and a rule inside an
 * async orchestration is a rule no test reaches. A run that found nothing keeps the previous answer;
 * a run that found something replaces it.</p>
 */
export function probeToKeep(found: ProbeResult | undefined, held: ProbeResult | undefined): ProbeResult | undefined {
  if (found === undefined) {
    return held;
  }
  // Nothing to carry forward, or nothing that MAY be carried forward. A different VERSION can
  // reach different families, so inheriting the old one's answers is how a model stays wrong for a
  // week after the CLI changed under it - and a different BINARY at the same version is a different
  // INSTALLATION, very possibly a different account, so its verdicts were never about this one. A
  // record that names no binary was written before that field existed: nothing about it says it is
  // another installation, and refusing it would throw away a good answer on the day this shipped.
  const elsewhere = (held?.executable ?? '').length > 0
    && (found.executable ?? '').length > 0
    && held?.executable !== found.executable;
  if (held === undefined || held.cliVersion !== found.cliVersion || elsewhere) {
    return found;
  }

  // A run that could not ask every candidate used to REPLACE the whole answer with its prefix, so
  // three families confirmed yesterday silently became "not asked yet" — a discovery subtracting,
  // which is the one thing this feature promised never to do. What this run learned wins for the
  // families it reached; the rest stand. (Raised independently by three reviewers this round.)
  // AND AN UNCONFIRMED ANSWER NEVER DISPLACES A CONFIRMED ONE. Round 1's merge carried forward the
  // families a run did not REACH, which missed the commonest failure by far: a candidate whose
  // request failed with an ordinary non-zero exit is recorded as asked-and-unverified, not skipped —
  // and a spent allowance produces exactly that for every candidate. The merge then let those
  // unverified entries win, so a person waited through a probe to watch four confirmed families go
  // back to "not asked yet". A verdict is only overwritten by another verdict. (local, round 2.)
  const confirmed = new Map(held.models.filter((m) => m.verified).map((m) => [m.asked, m]));
  const taken = found.models.map((m) => (m.verified ? m : confirmed.get(m.asked) ?? m));
  const asked = new Set(taken.map((m) => m.asked));

  return {
    ...found,
    models: [...taken, ...held.models.filter((m) => !asked.has(m.asked))],
  };
}
