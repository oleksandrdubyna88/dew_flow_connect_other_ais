import { ProbeResult, answeredAsAsked } from './claudeModels';

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

/** Longer than any version string or timestamp this writes, and short enough to refuse a novel. */
export const LONGEST_FIELD = 200;

/** Far more entries than there are candidate families, and far fewer than a file can hold. */
export const MOST_ENTRIES = 64;

/** Everything read back, or nothing — a file that cannot be understood is not an answer. */
export function parseProbe(text: string): ProbeResult | undefined {
  try {
    return probeFrom(JSON.parse(text));
  } catch {
    return undefined;
  }
}

/**
 * The same rebuilding, over a VALUE rather than a string.
 *
 * <p>Two callers read a stored probe: this file, from disk, and the chat's discovery snapshot, which
 * holds an object. The snapshot used to serialise it only to hand it back to `JSON.parse` one line
 * later — a round trip that did nothing but make the validator look like a file concern. It is a
 * value concern; the file parser is the one that has to turn text into a value first. (gemini,
 * round 2.)</p>
 */
export function probeFrom(parsed: unknown): ProbeResult | undefined {
  // ANYTHING at all arrives here: this is called on a value out of a settings store as well as on
  // a parse of a file, so undefined, a number and a string are all real inputs. The null check
  // alone let undefined through to a property read - caught by the discovery suite.
  if (typeof parsed !== 'object' || parsed === null) {
    return undefined;
  }
  const record = parsed as Partial<ProbeResult>;
  if (typeof record.cliVersion !== 'string' || typeof record.checkedUtc !== 'string') {
    return undefined;
  }
  // Bounded on the way IN as well as by construction. The file holds one entry per candidate
  // family and nothing here can grow it — but it is a file on a disk other things can write, and
  // a parser that will map a million entries at panel start is one an editor slip can hang.
  if (record.cliVersion.length > LONGEST_FIELD || record.checkedUtc.length > LONGEST_FIELD) {
    return undefined;
  }
  const models = (Array.isArray(record.models) ? record.models : []).slice(0, MOST_ENTRIES);

  return {
    cliVersion: record.cliVersion,
    checkedUtc: record.checkedUtc,
    // Each entry is rebuilt rather than trusted: this file is on disk, an older build wrote it,
    // and a half-written or hand-edited entry must not reach a dropdown as a confirmed model.
    models: models
      .filter((m): m is Record<string, unknown> => m !== null && typeof m === 'object')
      .filter((m) => typeof m['asked'] === 'string' && (m['asked'] as string).length > 0)
      .map((m) => {
        const asked = m['asked'] as string;
        const answered = typeof m['answered'] === 'string' ? m['answered'] : '';

        // RE-DERIVED, never read. The file held one decision written twice, and the two can
        // disagree: an entry saying `fable` was confirmed while recording that `claude-opus-5`
        // answered would have labelled Fable verified off a record proving it was not. The answer
        // is the evidence; the verdict is a function of it. (codex Architecture, this round.)
        return { asked, answered, verified: answeredAsAsked(asked, answered) };
      }),
    ...(typeof record.executable === 'string' ? { executable: record.executable } : {}),
  };
}

/** What goes on disk. Two spaces, because a person chasing a wrong dropdown will open it. */
export function writeProbe(probe: ProbeResult): string {
  return `${JSON.stringify(probe, null, 2)}\n`;
}
