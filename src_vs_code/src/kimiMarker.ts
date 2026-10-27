import type { ProvidersAnswer, SurvivorMarker } from './providers';
import type { CaptureEnd, Captured } from './versionProbe';

/**
 * The Kimi card's *Clear the stuck Kimi marker* button — E6 of research/PLAN_kimi_and_vibe_on_linux.md, DL9, S-K18.
 *
 * <p>When a Kimi that coai started may still be running, coai-mcp keeps a marker file and answers every Kimi turn on that sign-in
 * Busy (two refreshers of one rotating token would sign the person out). The Busy sentence already carries the manual way out —
 * the marker's path, the pids, "safe to delete once those processes are gone" — so an older extension without this button still
 * has an answer; this button is the owner's rule that every action is a button.</p>
 *
 * <p><b>The button shows only while the server reports a standing marker for the row</b> (`providers[].survivorMarker`, which
 * `providers.ts` reads). Pressing it asks the server AGAIN, takes the path from that fresh answer — never from the page, which
 * could hand back any path — and runs `coai-mcp --kimi-clear-marker <path>`. That mode clears the marker only when it can
 * positively verify that every process it names is gone, and signals nothing. Its exit codes are fixed (A3 spec §7): 0 cleared or
 * no marker, 65 a path that is not a marker under the Kimi lock root, 74 a member could not be verified, 75 a member still lives.</p>
 *
 * <p>Pure and `vscode`-free: the host wires the two ports (`providers.ts`'s probe and `serverRun`) and shows the sentence.</p>
 */

/**
 * One notice per OUTCOME of a press, so a person who dismisses "kept" is not also silenced about "cleared". The inventory (`notification-sites.json`)
 * reads these literals, which is why each `code` stands beside its `source`.
 */
const NOTICES = {
  cleared: { source: 'kimiMarker', code: 'kimi-marker-cleared' },
  absent: { source: 'kimiMarker', code: 'kimi-marker-absent' },
  kept: { source: 'kimiMarker', code: 'kimi-marker-kept' },
  refused: { source: 'kimiMarker', code: 'kimi-marker-refused' },
} as const;

/** Which notice a sentence is said as — the `source` and `code` `notify` takes. */
export type MarkerNotice = (typeof NOTICES)[keyof typeof NOTICES];

/** The sentence to show, how loudly, and as which notice. */
export interface MarkerSaid {
  readonly as: 'information' | 'warning';
  readonly title: string;
  readonly notice: MarkerNotice;
}

/** What the clear mode printed on stdout — `{"path":…,"outcome":"cleared|kept|absent","exit":0,"members":[{"pid":…,"liveness":"alive|gone|unknown","detail":…}]}`. */
interface ClearAnswer {
  readonly outcome: string;
  readonly members: readonly { readonly pid: number; readonly liveness: string; readonly detail: string }[];
}

/** The mode's own JSON, read defensively: anything else is "no detail", and the exit code alone still decides the sentence. */
export function clearAnswerOf(output: string): ClearAnswer {
  const fields = recordFrom(output);
  const members = Array.isArray(fields['members']) ? (fields['members'] as unknown[]) : [];

  return { outcome: typeof fields['outcome'] === 'string' ? fields['outcome'] : '', members: members.flatMap(memberOf) };
}

/** A JSON object's fields, or none for anything that is not one. */
function recordFrom(output: string): Record<string, unknown> {
  try {
    const parsed: unknown = JSON.parse(output);

    return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/** One member the mode reported, kept only whole: a numeric pid and a liveness word. */
function memberOf(raw: unknown): ClearAnswer['members'] {
  const member = recordOf(raw);

  return typeof member['pid'] === 'number' && typeof member['liveness'] === 'string'
    ? [{ pid: member['pid'], liveness: member['liveness'], detail: typeof member['detail'] === 'string' ? member['detail'] : '' }]
    : [];
}

function recordOf(raw: unknown): Record<string, unknown> {
  return typeof raw === 'object' && raw !== null ? (raw as Record<string, unknown>) : {};
}

/** The first member the mode named in a given state, as "pid N" plus its detail when it had one. */
function memberIn(answer: ClearAnswer, liveness: string): string {
  const member = answer.members.find((one) => one.liveness === liveness);
  if (member === undefined) {
    return 'a process it names';
  }

  return member.detail.length > 0 ? `pid ${member.pid} (${member.detail})` : `pid ${member.pid}`;
}

const CLEARED: MarkerSaid = { as: 'information', notice: NOTICES.cleared, title: 'The stuck Kimi marker is cleared: every process it named is gone. Kimi turns on this sign-in can start again.' };
const ABSENT: MarkerSaid = { as: 'information', notice: NOTICES.absent, title: 'There was no marker to clear: coai-mcp no longer has one for this sign-in.' };

/**
 * The exit code of `--kimi-clear-marker`, in plain words. The marker stays for every code but 0, and the sentence says so — and
 * for an unknown code (64 from a coai-mcp too old to know the mode, a timeout) it says that the file named in the Busy
 * sentence is still safe to delete by hand once those processes are gone, which is the way out that never needed this button.
 */
export function clearMarkerSaid(code: number, output: string, ended?: CaptureEnd): MarkerSaid {
  return (SAID_BY_CODE[code] ?? unknownCode)(clearAnswerOf(output), code, ended);
}

/** The marker STAYS — the mode ran and said so, could not tell, or did not answer. */
const warning = (title: string): MarkerSaid => ({ as: 'warning', notice: NOTICES.kept, title });

/** The mode did not take the request at all: refused it, or is not one this coai-mcp knows. */
const refusal = (title: string): MarkerSaid => ({ as: 'warning', notice: NOTICES.refused, title });

/** Exit 0 means cleared OR no marker there, and only the mode's own report says which — so a report that cannot be read says neither. */
const UNREADABLE_SUCCESS: MarkerSaid = { as: 'information', notice: NOTICES.kept, title: 'coai-mcp ended the clear mode with success, but its report could not be read, so this does not say whether a marker was cleared. The card shows what stands.' };

const ZERO_BY_OUTCOME: Readonly<Record<string, MarkerSaid>> = { cleared: CLEARED, absent: ABSENT };

/**
 * `-1` is no exit code at all, and the reason there is none differs: the run took longer than its cap, the program could not be started, or it
 * ended in a way nobody could read. Each is said as what it was — a program that never started did not "take 20 seconds".
 */
const NO_ANSWER: Readonly<Record<string, string>> = {
  timeout: 'coai-mcp did not answer within 20 seconds, so nothing is known about the marker: the card shows what stands.',
  'not-started': 'coai-mcp could not be started, so nothing was run and nothing is known about the marker: the card shows what stands.',
  unknown: 'coai-mcp ended without an answer the extension could read, so nothing is known about the marker: the card shows what stands.',
};

/** One arm per exit code the mode fixes (A3 spec §7); a code with no arm is {@link unknownCode}. */
const SAID_BY_CODE: Readonly<Record<number, (answer: ClearAnswer, code: number, ended?: CaptureEnd) => MarkerSaid>> = {
  0: (answer) => ZERO_BY_OUTCOME[answer.outcome] ?? UNREADABLE_SUCCESS,
  [-1]: (_answer, _code, ended) => warning(NO_ANSWER[ended ?? 'unknown'] ?? NO_ANSWER['unknown']!),
  75: (answer) => warning(`The marker stays: ${memberIn(answer, 'alive')} is still running. coai never ends a process for you — close it (or wait for it to end), then press the button again.`),
  74: (answer) => warning(`The marker stays: coai-mcp could not check ${memberIn(answer, 'unknown')}, and a process that cannot be checked is not known to be gone. Check it yourself, then press the button again.`),
  65: () => refusal('coai-mcp refused the request: the file is not a Kimi marker under its lock folder. Nothing was deleted.'),
};

function unknownCode(_answer: ClearAnswer, code: number): MarkerSaid {
  return refusal(`coai-mcp could not run the clear mode (exit ${code}) — it may be too old for it. The marker stays. The Busy sentence names the file: it is safe to delete once the processes it names are gone.`);
}

/** The providers question itself got no answer — a non-zero exit, a timeout, an unparseable body: what stands is unknown, and nothing was run. */
export const NOT_ANSWERED: MarkerSaid = { as: 'warning', notice: NOTICES.kept, title: 'coai-mcp did not answer the providers question, so nothing was run. If a marker stands it still stands — the Busy sentence names the file.' };

/** The press when there is no coai-mcp on this side at all: nothing is run, and whatever marker stands still stands. */
export const NOT_INSTALLED: MarkerSaid = { as: 'warning', notice: NOTICES.kept, title: 'coai-mcp is not installed on this side, so there is no marker to clear from here.' };

/** The button's place: what a host needs to run it. */
export interface MarkerPorts {
  /** Asks the server what it reports now — the marker path is taken from THIS answer. */
  readonly providers: () => Promise<ProvidersAnswer>;
  /** Runs `coai-mcp --kimi-clear-marker <path>`. */
  readonly clear: (path: string) => Promise<Captured>;
}

/** What the server reports for the row, and nothing when no marker stands or the server did not answer. */
export function standingMarker(answer: ProvidersAnswer, rowId: string): SurvivorMarker | undefined {
  return answer.answered ? answer.reported[rowId]?.survivorMarker : undefined;
}

/**
 * One press: ask the server, and run the mode on the path IT names for this row. No marker reported now means the button was
 * stale (another window cleared it, or the processes ended and the next launch did) — said, and nothing is run.
 */
export async function pressClearMarker(rowId: string, ports: MarkerPorts): Promise<MarkerSaid> {
  const answer = await ports.providers();
  if (!answer.answered) {
    return NOT_ANSWERED;
  }
  const marker = standingMarker(answer, rowId);
  if (marker === undefined) {
    return ABSENT;
  }
  const { code, output, ended } = await ports.clear(marker.path);

  return clearMarkerSaid(code, output, ended);
}

/** The card's line about a standing marker: which file, which pids — and that the Busy sentence carries the same way out by hand. */
export function markerLine(marker: SurvivorMarker): string {
  const who = marker.pids.length > 0 ? `it names pid ${marker.pids.join(', ')}` : 'it names no process (the last turn ended before Kimi started)';

  return `A Kimi that coai started may still be running, so Kimi turns on this sign-in are Busy. The marker is ${marker.path}; ${who}. `
    + 'The button checks every process and clears the marker only when all of them are gone — or delete the file yourself once they are.';
}
