import { type ConsultantsReport, parseConsultantsAnswer } from './consultantHealth';
import type { ProbeShown } from './consultantHealthState';

/**
 * `coai-mcp --consultants`, asked by a render and NEVER awaited by one — E5.4 of
 * `research/PLAN_the_consultant_works_on_every_vendor.md`.
 *
 * <p>The `providerHealth` / `CadenceProbes` shape, and for their reason: the mode asks each consultant's CLI for its
 * version (and a claude for its `--help`), up to the server's own sixty-second ceiling, and a render that waited on
 * that would be a panel frozen for a minute. {@link ConsultantsProbe.shown} answers from what is already known and
 * starts a spawn for whatever is stale; the spawn asks for a repaint only when what is drawn CHANGED, which with the
 * freshness check is what stops a repaint from starting the next probe for ever. The panel's renders run side by side
 * and must keep doing so (`renderTracker.ts`) — nothing here serialises them.</p>
 *
 * <p><b>One spawn at a time.</b> <b>A failure keeps the last answer</b> on screen, says why, and is logged once per
 * reason. <b>{@link ConsultantsProbe.forget}</b> — a check started or ended, a state file changed — makes the next render
 * ask again; a probe already in flight when it was called stores its answer as stale and asks for a repaint, so the
 * render that follows asks once more (the cadence probes' lesson, PR #556).</p>
 */

/** How long an answer is fresh. The watcher's `forget` resets it for every change a person caused. */
export const CONSULTANTS_TTL_MS = 60_000;

/** The spawn's cap: past the server's own sixty-second probe ceiling, so the panel never kills a survey still allowed to run. */
export const CONSULTANTS_CAP_MS = 90_000;

/** What the probe needs from the host. */
export interface ConsultantsProbeHost {
  /** The server binary on this side, or empty when none is installed. */
  readonly executable: () => string;
  readonly run: (executable: string, args: readonly string[]) => Promise<{ code: number; output: string }>;
  readonly now: () => number;
  /** Asked to repaint when what is drawn changed. */
  readonly render: () => void;
  readonly log: (message: string) => void;
}

/** The last thing known, when it was learned, and from which binary. */
interface Known {
  readonly shown: ProbeShown;
  readonly at: number;
  readonly executable: string;
}

export class ConsultantsProbe {
  private known: Known | undefined;

  private inFlight = false;

  /** Moves on every {@link forget}; an answer to a probe started before the move is stored stale. */
  private generation = 0;

  private readonly logged = new Set<string>();

  constructor(private readonly host: ConsultantsProbeHost) {}

  /** What to draw now — and a spawn started when what is known is stale or about another binary. */
  shown(): ProbeShown {
    const executable = this.host.executable();
    if (executable.length === 0) {
      return { kind: 'not-installed' };
    }
    this.startIfStale(executable);

    return this.known?.executable === executable ? this.known.shown : { kind: 'asking' };
  }

  private startIfStale(executable: string): void {
    if (this.stale(executable) && !this.inFlight) {
      // The detached edge: nothing awaits this promise, and a probe that threw is said, never unhandled.
      this.probe(executable).then(undefined, (error: unknown) => this.host.log(`ConnectOtherAIs: the consultants probe failed: ${String(error)}`));
    }
  }

  /** Something changed on disk or a check ran: the next render asks again, and what is drawn stays up until it lands. */
  forget(): void {
    this.generation += 1;
    this.known = this.known === undefined ? undefined : { ...this.known, at: Number.NEGATIVE_INFINITY };
  }

  private stale(executable: string): boolean {
    return this.known === undefined || this.known.executable !== executable || this.host.now() - this.known.at >= CONSULTANTS_TTL_MS;
  }

  private async probe(executable: string): Promise<void> {
    this.inFlight = true;
    const started = this.generation;
    try {
      const shown = await this.asked(executable);
      this.store(shown, executable, started);
    } finally {
      this.inFlight = false;
    }
  }

  /** The spawn's answer as something to draw — a rejected spawn is an unanswered probe, with the last answer kept. */
  private async asked(executable: string): Promise<ProbeShown> {
    try {
      const { code, output } = await this.host.run(executable, ['--consultants']);
      const answer = parseConsultantsAnswer(code, output);

      return answer.kind === 'unanswered' ? this.unanswered(answer.why) : answer;
    } catch (error: unknown) {
      return this.unanswered(`the server could not be started: ${String(error)}`);
    }
  }

  private unanswered(why: string): ProbeShown {
    if (!this.logged.has(why)) {
      this.logged.add(why);
      this.host.log(`ConnectOtherAIs: the consultants could not be read (${why}); the last answer stays up`);
    }

    return { kind: 'unanswered', why, last: lastReport(this.known?.shown) };
  }

  /**
   * Kept — fresh when asked in this generation, stale when a forget overtook it — and a repaint when it changed or was
   * overtaken. "Changed" leaves the answer's own `utc` out (the whole-branch review, N1): every `--consultants` is stamped
   * afresh, so counting the stamp repainted the Settings page on every ask though nothing drawn had moved; the latest
   * answer is still the one KEPT.
   */
  private store(shown: ProbeShown, executable: string, started: number): void {
    const changed = drawn(shown) !== drawn(this.known?.shown);
    const overtaken = started !== this.generation;
    this.known = { shown, executable, at: overtaken ? Number.NEGATIVE_INFINITY : this.host.now() };
    this.renderIf(changed || overtaken);
  }

  private renderIf(wanted: boolean): void {
    if (wanted) {
      this.host.render();
    }
  }
}

/** What an answer DRAWS, as one string — everything but the stamp the server puts on every answer. */
function drawn(shown: ProbeShown | undefined): string {
  return JSON.stringify(shown?.kind === 'answered' ? { ...shown, report: { ...shown.report, utc: '' } } : shown);
}

/** The report a previous answer carried, so a failed refresh never blanks what a person was reading. */
function lastReport(shown: ProbeShown | undefined): ConsultantsReport | undefined {
  if (shown === undefined) {
    return undefined;
  }

  return shown.kind === 'answered' ? shown.report : unansweredLast(shown);
}

function unansweredLast(shown: ProbeShown): ConsultantsReport | undefined {
  return shown.kind === 'unanswered' ? shown.last : undefined;
}
