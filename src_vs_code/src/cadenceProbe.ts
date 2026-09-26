import { CadenceAnswer, CadenceLine, cadenceWanted, parseCadence, planOf } from './cadenceLine';
import type { SessionFile } from './rounds';

/**
 * The sidebar's cadence probes: `coai-mcp --cadence` per session worth asking about, bounded
 * (todo/PLAN_consult_on_a_cadence.md, epic 4 story 4.2; the risk consultation for that story, point 3).
 *
 * <p><b>Never awaited by a render.</b> {@link CadenceProbes.lines} answers from what is already known and
 * starts a probe for whatever is stale; the probe repaints when it lands — and only when an answer CHANGED,
 * which together with the TTL is what stops a repaint from starting the next probe for ever.</p>
 *
 * <p><b>One probe at a time</b>, across every session: a sidebar with five branches in flight is five
 * processes in a row, never five at once. <b>A failure keeps the last answer</b> — a spawn that timed out, a
 * torn session file (65), a body that is not an answer — because the line blinking out is worse than a line
 * half a minute old. <b>A 64 is a server too old for the mode</b>: nothing more is asked and no line is drawn,
 * rather than asking every half minute of the window's life.</p>
 *
 * <p>The spawn, the clock and the log are handed in, so every one of those rules is a test.</p>
 */

/** How long an answer is fresh. The consultation watcher resets it for the common case; this is the rest. */
export const CADENCE_TTL_MS = 30_000;

/** Long enough for a cold binary on a slow disk; the spawn is killed at it, so a hung probe parks nothing. */
export const CADENCE_CAP_MS = 8_000;

/** What the probes need from the host. */
export interface CadenceProbeHost {
  /** The server binary, or empty when none is installed. */
  readonly executable: () => string;
  readonly run: (executable: string, args: readonly string[]) => Promise<{ code: number; output: string }>;
  readonly now: () => number;
  /** Asked to repaint when an answer changed. */
  readonly render: () => void;
  readonly log: (message: string) => void;
}

/** One session's last answer: `null` is "nothing to draw", which is an answer too. */
interface Known {
  readonly at: number;
  readonly answer: CadenceAnswer | null;
}

/** 64: this binary has never heard of the mode (`.agents/PROJECT.md`). */
const TOO_OLD = 64;

export class CadenceProbes {
  private readonly known = new Map<string, Known>();

  private readonly logged = new Set<string>();

  private inFlight = false;

  private tooOld = false;

  /**
   * Moves on every {@link forget}. A probe remembers the generation it STARTED in, and an answer from an
   * older one is stored as already stale: it may predate the consultation that changed, and taking it as
   * fresh would undo the `forget` for a whole TTL (PR #556, CodeRabbit).
   */
  private generation = 0;

  constructor(private readonly host: CadenceProbeHost) {}

  /** The lines to draw now, from what is known — and a probe started for whatever is stale. */
  lines(sessions: readonly SessionFile[]): readonly CadenceLine[] {
    if (this.tooOld) {
      return [];
    }

    const now = this.host.now();
    const wanted = sessions.filter((session) => cadenceWanted(session, now));
    const stale = wanted.filter((session) => now - (this.known.get(keyOf(session))?.at ?? Number.NEGATIVE_INFINITY) >= CADENCE_TTL_MS);
    if (stale.length > 0 && !this.inFlight) {
      this.probe(stale).then(undefined, (error: unknown) => {
        // The detached edge, per the try/catch rule: nothing awaits this promise.
        this.host.log(`ConnectOtherAIs: the cadence probe failed: ${String(error)}`);
      });
    }

    return wanted.flatMap((session) => {
      const answer = this.known.get(keyOf(session))?.answer;
      return answer === undefined || answer === null
        ? []
        : [{ repoPath: session.state.repoPath, branch: session.state.branch, answer }];
    });
  }

  /** A consultation changed: every answer is stale, and each stays drawn until its next one lands. */
  forget(): void {
    this.generation += 1;
    for (const [key, known] of this.known) {
      this.known.set(key, { ...known, at: Number.NEGATIVE_INFINITY });
    }
  }

  private async probe(sessions: readonly SessionFile[]): Promise<void> {
    const executable = this.host.executable();
    if (executable.length === 0) {
      return;
    }

    this.inFlight = true;
    await this.probeAll(executable, sessions).finally(() => {
      this.inFlight = false;
    });
  }

  /**
   * Each session in turn, stopping at a server too old for the mode — and a repaint as each changed answer
   * LANDS, not once the batch is done: twenty cold probes in a row were a minute of no lines while the first
   * answers were already in hand (the code round, codex).
   */
  private async probeAll(executable: string, sessions: readonly SessionFile[]): Promise<void> {
    for (const session of sessions) {
      if (this.tooOld) {
        break;
      }
      if (await this.probeOne(executable, session)) {
        this.host.render();
      }
    }
  }

  /** One session asked; whether what is drawn for it changed. */
  private async probeOne(executable: string, session: SessionFile): Promise<boolean> {
    const key = keyOf(session);
    const asked = this.generation;
    const { code, output } = await this.host.run(executable, [
      '--cadence', '--repo', session.state.repoPath, '--branch', session.state.branch, '--plan', planOf(session),
    ]);
    const previous = this.lastAnswer(key);
    const answer = code === 0 ? parseCadence(output) : undefined;

    const at = this.stampFor(asked);

    return answer === undefined ? this.failed(key, code, previous, at) : this.answered(key, answer, previous, at);
  }

  /** Now, for an answer asked in this generation; never, for one asked before the last `forget`. */
  private stampFor(asked: number): number {
    return asked === this.generation ? this.host.now() : Number.NEGATIVE_INFINITY;
  }

  private lastAnswer(key: string): CadenceAnswer | null {
    return this.known.get(key)?.answer ?? null;
  }

  private answered(key: string, answer: CadenceAnswer | null, previous: CadenceAnswer | null, at: number): boolean {
    this.known.set(key, { at, answer });

    return JSON.stringify(answer) !== JSON.stringify(previous);
  }

  /**
   * A probe that did not answer: the last answer stays, and the reason is said once per session. Whether
   * what is drawn changed — which only a 64 does, by taking every line away.
   */
  private failed(key: string, code: number, previous: CadenceAnswer | null, at: number): boolean {
    if (code === TOO_OLD) {
      this.tooOld = true;
      this.known.clear();
      return previous !== null;
    }

    // Kept, and its clock restarted, so a session that keeps failing is asked at the TTL and not on every render.
    this.known.set(key, { at, answer: previous });
    if (!this.logged.has(key)) {
      this.logged.add(key);
      this.host.log(`ConnectOtherAIs: the cadence of ${key} could not be read (exit ${code}); the last answer stays up`);
    }

    return false;
  }
}

function keyOf(session: SessionFile): string {
  return `${session.state.repoPath}|${session.state.branch}|${planOf(session)}`;
}
