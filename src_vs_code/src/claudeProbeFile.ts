import { ProbeResult } from './claudeModels';

/**
 * Where a probe's answer is kept between windows, and the three things that keeps it honest.
 *
 * <p><b>It is bounded by construction.</b> The file holds one entry per CANDIDATE FAMILY and nothing
 * else — four today — so no amount of typing model names into the panel can grow it. That was a
 * reviewer's question on the plan round and it is answered by the shape rather than by an eviction
 * policy nobody would ever see run.</p>
 *
 * <p><b>It is written atomically.</b> A window killed mid-write would otherwise leave a truncated
 * file that the next launch reads as no answer at all — which is survivable but silently costs four
 * requests, and the same trick every other writer here uses (`chatStoreFile`, the escalation answers)
 * costs one rename.</p>
 *
 * <p><b>It is never trusted across a different binary.</b> The CLI version is part of the record and
 * `stillGood` refuses a mismatch: a new CLI may reach a family the old one could not, and inheriting
 * the old answer is how a model stays invisible for a week after it arrives.</p>
 */

/** One file, named for what it holds rather than for who wrote it. */
export const PROBE_FILE = 'claude-models.json';

/** Everything read back, or nothing — a file that cannot be understood is not an answer. */
export function parseProbe(text: string): ProbeResult | undefined {
  try {
    const parsed: unknown = JSON.parse(text);
    const record = parsed as Partial<ProbeResult> | null;
    if (record === null || typeof record.cliVersion !== 'string' || typeof record.checkedUtc !== 'string') {
      return undefined;
    }
    const models = Array.isArray(record.models) ? record.models : [];

    return {
      cliVersion: record.cliVersion,
      checkedUtc: record.checkedUtc,
      // Each entry is rebuilt rather than trusted: this file is on disk, an older build wrote it,
      // and a half-written or hand-edited entry must not reach a dropdown as a confirmed model.
      models: models
        .filter((m): m is Record<string, unknown> => m !== null && typeof m === 'object')
        .filter((m) => typeof m['asked'] === 'string' && (m['asked'] as string).length > 0)
        .map((m) => ({
          asked: m['asked'] as string,
          answered: typeof m['answered'] === 'string' ? m['answered'] : '',
          verified: m['verified'] === true,
        })),
    };
  } catch {
    return undefined;
  }
}

/** What goes on disk. Two spaces, because a person chasing a wrong dropdown will open it. */
export function writeProbe(probe: ProbeResult): string {
  return `${JSON.stringify(probe, null, 2)}\n`;
}
