import type { ModelChoice } from './models';

/**
 * Which Claude models this machine can actually reach — asked, not listed.
 *
 * <h2>Why asking is necessary at all</h2>
 *
 * <p>Claude was the one runtime with no discovery path. Codex publishes `~/.codex/models_cache.json`,
 * antigravity answers `agy models`, a local engine answers `/api/tags`, a Team server answers a
 * catalog — and Claude had three curated aliases resting on a stated theory: *the CLI resolves an
 * alias to the latest of that family*. That holds while a family only gains versions and breaks the
 * moment a NEW family appears, which is exactly what Fable is. Measured 2026-09-16: asking the CLI
 * for `fable` is answered by `claude-fable-5-1`, and the picker did not offer it.</p>
 *
 * <h2>Why the obvious probe does not work, which is the whole design</h2>
 *
 * <p><b>`claude --model <anything> -p "hi"` exits 0 and answers.</b> An unknown model name is
 * silently ignored and the default replies — measured with `definitely-not-a-model-xyz`, answered by
 * `claude-opus-5[1m]`. So a probe built on exit codes reports every invented name as available, and
 * "the CLI accepted it" is evidence of nothing.</p>
 *
 * <p>What works is `--output-format json`, whose `modelUsage` is keyed by the model that ACTUALLY
 * answered. The question is therefore never *did it succeed* but {@link answeredAsAsked} — and the
 * answer hands back the concrete id for free, so *the highest version, if available* is something the
 * CLI says rather than something this file assumes.</p>
 */

/** What one candidate turned out to be. */
export interface ProbedModel {
  /** What was asked for — an alias like `fable`, or a concrete id somebody typed. */
  readonly asked: string;
  /** The model that answered, as the CLI named it. Empty when nothing could be read. */
  readonly answered: string;
  /**
   * Whether the model that answered is the one that was asked for.
   *
   * <p>False is not "broken": it is the measured behaviour of a name this account cannot reach, and
   * the reason a candidate must never be offered as confirmed on the strength of an exit code.</p>
   */
  readonly verified: boolean;
}

/** Everything one probe run found, and when — the shape the cache holds. */
export interface ProbeResult {
  /** The CLI that answered, so a different binary invalidates rather than inherits. */
  readonly cliVersion: string;
  readonly checkedUtc: string;
  readonly models: readonly ProbedModel[];
}

/** The aliases worth asking about. Short on purpose: each one is a real, billed request. */
/** What a dropdown says while the probe is out. Named here, beside the probe it describes. */
export const ASKING_CLAUDE = 'asking the Claude CLI which models it reaches\u2026';

/**
 * What a model dropdown has to say about ITSELF right now, or nothing when it has nothing.
 *
 * <p>The probe is four real requests to a real CLI and takes seconds. A dropdown that simply sat
 * there holding the curated list for that long would read as the finished answer, and a person
 * would choose from it believing nothing else was coming.</p>
 *
 * <p>Only the Claude branch, because only the Claude branch is what this probe asks. A codex row
 * is not waiting for it and must not borrow its spinner — a control that says it is working while
 * nothing is working for it is the same defect one runtime over.</p>
 */
export function claudeNote(runtime: string, asking: boolean): string {
  return runtime === 'claude' && asking ? ASKING_CLAUDE : '';
}

export const CLAUDE_CANDIDATES: readonly string[] = ['haiku', 'sonnet', 'opus', 'fable'];

/** How long an answer is trusted. A subscription does not gain a model family in an afternoon. */
export const PROBE_GOOD_FOR_MS = 7 * 24 * 60 * 60 * 1000;

/**
 * The family a model id belongs to.
 *
 * <p>`claude-fable-5-1` → `fable`, `claude-opus-5[1m]` → `opus`, and a bare alias is its own family.
 * The version, the vendor prefix and a context-window suffix are all dropped, because none of them
 * is what was asked for.</p>
 */
export function familyOf(model: string): string {
  const bare = model.trim().toLowerCase().replace(/^claude[-.]?/, '').replace(/\[[^\]]*\]$/, '');
  const word = /^([a-z]+)/.exec(bare);

  return word?.[1] ?? '';
}

/**
 * Did the model that answered belong to the family that was asked for?
 *
 * <p><b>By FAMILY, and that is the finding two reviewers raised independently.</b> Exact equality
 * rejects the real thing — `fable` is answered by `claude-fable-5-1`, which is not the same string —
 * while a loose prefix or substring test accepts the invented one, since `claude-opus-5[1m]` shares
 * a prefix with every Claude model there is. The family is the unit the question was asked in.</p>
 */
export function answeredAsAsked(asked: string, answered: string): boolean {
  const wanted = familyOf(asked);

  return wanted.length > 0 && wanted === familyOf(answered);
}

/**
 * The model that answered, out of one `--output-format json` reply.
 *
 * <p>`modelUsage` is an object keyed by model id; the reply carries no `model` field at all, which is
 * why this reads the key rather than a value. An answer this cannot read is EMPTY rather than
 * assumed — a candidate nobody could confirm is not a candidate that failed.</p>
 */
export function modelThatAnswered(output: string): string {
  try {
    const parsed: unknown = JSON.parse(output);
    const usage = (parsed as { modelUsage?: unknown } | null)?.modelUsage;
    if (usage === null || typeof usage !== 'object') {
      return '';
    }
    const named = Object.keys(usage as Record<string, unknown>);

    return named.length === 1 ? named[0] ?? '' : '';
  } catch {
    return '';
  }
}

/** Is this answer still worth trusting, or is it from another CLI or another week? */
export function stillGood(probe: ProbeResult | undefined, cliVersion: string, now: number): boolean {
  if (probe === undefined || probe.cliVersion !== cliVersion) {
    return false;
  }
  const at = Date.parse(probe.checkedUtc);

  return Number.isFinite(at) && now - at < PROBE_GOOD_FOR_MS && now >= at;
}

/**
 * What the Claude dropdown offers.
 *
 * <p><b>A probe never subtracts.</b> Everything curated is still offered when nothing could be
 * asked — an account whose allowance is spent, a CLI that is not installed, a first run — because a
 * discovery that cannot run must not take away what a person could already choose.</p>
 *
 * <p><b>But an unverified entry says so.</b> This is the finding that mattered most on the plan
 * round: offering `fable` on a machine that cannot reach it means `claude --model fable` exits 0 and
 * runs the DEFAULT, so a round is reviewed by a model nobody chose and nothing reports it. A verified
 * entry names the concrete model the CLI resolved it to; an unverified one says it was not asked.</p>
 */
export function claudeModels(probe: ProbeResult | undefined, curated: readonly ModelChoice[]): ModelChoice[] {
  const verified = new Map((probe?.models ?? []).filter((m) => m.verified).map((m) => [familyOf(m.asked), m]));

  return curated.map((choice) => {
    const found = verified.get(familyOf(choice.id));

    return found === undefined
      ? { id: choice.id, label: `${choice.label} — not asked yet` }
      : { id: choice.id, label: `${choice.label} — ${found.answered}` };
  });
}
