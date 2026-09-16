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
  /** The CLI's own version, so an answer is not inherited by a different binary. */
  readonly cliVersion: () => Promise<string>;
  readonly now: () => number;
}

/** Long enough for a small answer, short enough that four of them are not a minute. */
export const PROBE_CAP_MS = 25_000;

/** The prompt. One word, because the answer is thrown away and only `modelUsage` is read. */
export const PROBE_PROMPT = 'hi';

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
): Promise<ProbeResult | undefined> {
  const cliVersion = await ports.cliVersion();
  if (cliVersion.length === 0) {
    return undefined;
  }
  const models: ProbedModel[] = [];
  for (const asked of candidates) {
    const { code, output } = await ports.run(['--model', asked, '-p', PROBE_PROMPT, '--output-format', 'json']);
    if (code === -1) {
      // The CLI did not run at all — not installed, not on the path, killed at the cap. Anything
      // learned so far is kept; what was not asked stays unasked rather than becoming a denial.
      break;
    }
    const answered = modelThatAnswered(output);
    models.push({ asked, answered, verified: answeredAsAsked(asked, answered) });
  }

  // NOTHING LEARNED IS NOT AN ANSWER. Writing an empty result would age into the cache and read, a
  // minute later, as "this machine reaches no Claude models at all".
  return models.length === 0
    ? undefined
    : { cliVersion, checkedUtc: new Date(ports.now()).toISOString(), models };
}

/**
 * The answer to keep, given what was found and what was already held.
 *
 * <p>Its own function because it is the rule the plan round pressed hardest on, and a rule inside an
 * async orchestration is a rule no test reaches. A run that found nothing keeps the previous answer;
 * a run that found something replaces it.</p>
 */
export function probeToKeep(found: ProbeResult | undefined, held: ProbeResult | undefined): ProbeResult | undefined {
  return found ?? held;
}
