import { stat } from 'node:fs/promises';
import type { Run } from './roundsDbRead';

/**
 * What the installed coai-mcp says it accepts — its `--features` answer (todo/PLAN_one_model_catalog.md, epic 2, as
 * revised by its plan round: "capability, not version numbers").
 *
 * <p>The extension sends a catalog field, or passes a flag, only when the binary lists it. A `*_SINCE` constant would
 * guess the release number before release-please cuts it, and a binary built from a branch has none at all; the binary
 * is the one thing that knows what it does.</p>
 *
 * <p><b>Anything short of a clean answer is NO features.</b> A field sent to a binary that ignores it is the silence
 * the list exists to end; holding one back from a binary that would have taken it costs only that feature, and
 * {@link BinaryFeatures.why} says why, for the card that names what is not sent.</p>
 */

/** The capabilities this extension knows how to use, each with the story that added it to coai-mcp. */
export const FEATURES = {
  /** E2.1: `--collect-bugs --runtime <runtime>` — the Bugz ranking model allowed by its row's runtime. */
  bugzRuntime: 'bugzRuntime',
} as const;

export interface BinaryFeatures {
  readonly features: readonly string[];
  /** Empty for a clean answer; otherwise why the list is empty. */
  readonly why: string;
  /**
   * Whether this answer holds for the life of the binary file: a clean list, or a definite 64 (a binary older than
   * the list). A spawn that timed out or a garbled answer may pass, and is asked again (PR #686's review).
   */
  readonly settled: boolean;
}

/** `unknown argument` — a binary from before the list. */
const EX_USAGE = 64;

/** The binary answers from memory: no model, no file beyond its own. */
const CAP_MS = 15_000;

export async function readBinaryFeatures(run: Run): Promise<BinaryFeatures> {
  const { code, output } = await run(['--features'], CAP_MS);
  if (code === EX_USAGE) {
    return { ...none('this coai-mcp is older than the list of what it accepts'), settled: true };
  }
  if (code !== 0) {
    return none(`coai-mcp exited ${code}`);
  }

  return listed(output);
}

export function hasFeature(answer: BinaryFeatures, feature: string): boolean {
  return answer.features.includes(feature);
}

/** The answer read — and refused whole when it is not a list of names, never read in part. */
function listed(output: string): BinaryFeatures {
  const features = featuresIn(parsed(output));

  return features === undefined
    ? none('coai-mcp answered --features with something that is not the list')
    : { features: [...new Set(features)], why: '', settled: true };
}

function featuresIn(value: unknown): readonly string[] | undefined {
  const list = fieldOf(value, 'features');

  return Array.isArray(list) && list.every((one) => typeof one === 'string') ? list : undefined;
}

function fieldOf(value: unknown, name: string): unknown {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>)[name] : undefined;
}

function parsed(output: string): unknown {
  try {
    return JSON.parse(output);
  } catch {
    return undefined;
  }
}

/**
 * One answer per binary FILE: the path and its modification time are the key, so an update — a new file at the same
 * path — is asked again, and a render that runs every few seconds is not a spawn every few seconds.
 */
export class FeaturesCache {
  private asked: { readonly key: string; readonly answer: Promise<BinaryFeatures> } | undefined = undefined;

  async of(path: string | undefined, runFor: (path: string) => Run): Promise<BinaryFeatures> {
    if (path === undefined) {
      return none('the MCP server is not installed');
    }
    const key = `${path}|${await modifiedMs(path)}`;
    if (this.asked?.key !== key) {
      const answer = readBinaryFeatures(runFor(path));
      this.asked = { key, answer };
      void answer.then((got) => this.forgetUnsettled(answer, got));
    }

    return this.asked.answer;
  }

  /** An answer that may pass is not kept: the next render asks again, rather than the window living on a cold start. */
  private forgetUnsettled(answer: Promise<BinaryFeatures>, got: BinaryFeatures): void {
    if (!got.settled && this.asked?.answer === answer) {
      this.asked = undefined;
    }
  }
}

function none(why: string): BinaryFeatures {
  return { features: [], why, settled: false };
}

/**
 * When the binary file last changed — the half of the cache key that makes an update a new question. 0 when it cannot
 * be read, which still keys the path and is asked again once it can.
 */
export async function modifiedMs(path: string): Promise<number> {
  try {
    return (await stat(path)).mtimeMs;
  } catch {
    return 0;
  }
}
