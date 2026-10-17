import type { ProvidersAnswer, SurvivorMarker } from './providers';

/**
 * The Kimi card's *Clear the stuck Kimi marker* button — E6 of todo/PLAN_kimi_and_vibe_on_linux.md, DL9, S-K18.
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

/** The sentence to show, and how loudly. */
export interface MarkerSaid {
  readonly as: 'information' | 'warning';
  readonly title: string;
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

const CLEARED: MarkerSaid = { as: 'information', title: 'The stuck Kimi marker is cleared: every process it named is gone. Kimi turns on this sign-in can start again.' };
const ABSENT: MarkerSaid = { as: 'information', title: 'There was no marker to clear: coai-mcp no longer has one for this sign-in.' };

/**
 * The exit code of `--kimi-clear-marker`, in plain words. The marker stays for every code but 0, and the sentence says so — and
 * for an unknown code (64 from a coai-mcp too old to know the mode, a timeout) it says that the file named in the Busy
 * sentence is still safe to delete by hand once those processes are gone, which is the way out that never needed this button.
 */
export function clearMarkerSaid(code: number, output: string): MarkerSaid {
  return (SAID_BY_CODE[code] ?? unknownCode)(clearAnswerOf(output), code);
}

const warning = (title: string): MarkerSaid => ({ as: 'warning', title });

/** One arm per exit code the mode fixes (A3 spec §7); a code with no arm is {@link unknownCode}. */
const SAID_BY_CODE: Readonly<Record<number, (answer: ClearAnswer, code: number) => MarkerSaid>> = {
  0: (answer) => (answer.outcome === 'absent' ? ABSENT : CLEARED),
  75: (answer) => warning(`The marker stays: ${memberIn(answer, 'alive')} is still running. coai never ends a process for you — close it (or wait for it to end), then press the button again.`),
  74: (answer) => warning(`The marker stays: coai-mcp could not check ${memberIn(answer, 'unknown')}, and a process that cannot be checked is not known to be gone. Check it yourself, then press the button again.`),
  65: () => warning('coai-mcp refused the request: the file is not a Kimi marker under its lock folder. Nothing was deleted.'),
};

function unknownCode(_answer: ClearAnswer, code: number): MarkerSaid {
  return warning(`coai-mcp could not run the clear mode (exit ${code}) — it may be too old for it. The marker stays. The Busy sentence names the file: it is safe to delete once the processes it names are gone.`);
}

/** The button's place: what a host needs to run it. */
export interface MarkerPorts {
  /** Asks the server what it reports now — the marker path is taken from THIS answer. */
  readonly providers: () => Promise<ProvidersAnswer>;
  /** Runs `coai-mcp --kimi-clear-marker <path>`. */
  readonly clear: (path: string) => Promise<{ readonly code: number; readonly output: string }>;
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
  const marker = standingMarker(await ports.providers(), rowId);
  if (marker === undefined) {
    return ABSENT;
  }
  const { code, output } = await ports.clear(marker.path);

  return clearMarkerSaid(code, output);
}

/** The card's line about a standing marker: which file, which pids — and that the Busy sentence carries the same way out by hand. */
export function markerLine(marker: SurvivorMarker): string {
  const who = marker.pids.length > 0 ? `it names pid ${marker.pids.join(', ')}` : 'it names no process (the last turn ended before Kimi started)';

  return `A Kimi that coai started may still be running, so Kimi turns on this sign-in are Busy. The marker is ${marker.path}; ${who}. `
    + 'The button checks every process and clears the marker only when all of them are gone — or delete the file yourself once they are.';
}
