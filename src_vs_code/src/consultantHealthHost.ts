import { CALLER_KINDS } from './consultSettings';
import { isModelCheck } from './consultantCheckRun';
import { type CheckRunResult, canaryWording, failureLabel } from './consultantHealth';
import type { LandedRun } from './consultantHealthState';
import type { CopyReport, TextCopier } from './copyText';

/**
 * The Check button and the snippet's Copy, on ports — the host half of E5.3 of
 * `research/PLAN_the_consultant_works_on_every_vendor.md`.
 *
 * <p>Pure of `vscode`, for the reason `phraseCopy.ts` is: the paths worth a test are the ones that must NOT happen. A
 * Check is one real, paid turn, so it runs only after a modal confirmation that says so — naming the vendor and model,
 * the scratch folder it runs in, and the session it leaves in the vendor's own store — and never twice at once: a press
 * while the first is being confirmed, or while it runs, does nothing. A press naming no caller kind this build knows
 * does nothing either; the id comes from a webview message and ends up in an argv.</p>
 *
 * <p><b>The in-flight flag is optimistic</b> (`durable-status.md`): it paints *Checking…* the moment the person says yes,
 * and it clears however the run ends — a throw included — because the truth is the state file the server persists
 * before it launches anything, which the probe and the watcher re-read. The run is `void`: the press returns at once,
 * so the panel's command handler never waits out a paid turn, and the busy mark settles on the press, not the turn.</p>
 */

/** What a check is about, as the confirmation names it — the consultant the server reported for that caller kind. */
export interface CheckAbout {
  readonly vendor: string;
  readonly model: string;
}

/** The snippet a Copy press is for, as this side's server reported it — undefined when there is none to copy. */
export interface SnippetToCopy {
  readonly text: string;
  readonly settingsPath: string;
}

export interface HealthHostPorts {
  /** A MODAL question; true only when the person chose `go`. */
  readonly confirm: (title: string, detail: string, go: string) => Promise<boolean>;
  /** `--check-consultant --caller <kind>` run to its end (`consultantCheckRun.ts`). */
  readonly runCheck: (kind: string) => Promise<CheckRunResult>;
  /** A check ended: ask the server again and repaint. */
  readonly settled: () => void;
  /**
   * Says that a press landed, in one sentence, through the notification funnel — the screen reader's way to hear it,
   * since the page's check lines are rebuilt with every repaint and so can never be a live region (the review, P).
   */
  readonly announce: (kind: string, sentence: string) => void;
  readonly render: () => void;
  readonly nowUtc: () => string;
  /** The shared clipboard writer (`copyText.ts`): one line on the status bar, writes in press order. */
  readonly copier: TextCopier;
}

/**
 * What one landed press comes to, in ONE sentence a person hears without looking at the page — announced through the
 * notification funnel when the run ends (the whole-branch review, P).
 */
export function landedSentence(kind: string, result: CheckRunResult): string {
  return `The ${kind} consultant's check ${LANDED[result.kind](result)}.`;
}

type LandedRecord = Extract<CheckRunResult, { kind: 'reported' }>['record'];

/** One clause per way a press can land — a table, so each is one line and none hides in a branch. */
const LANDED: Readonly<Record<CheckRunResult['kind'], (result: CheckRunResult) => string>> = {
  'reported': (result) => (result.kind === 'reported' ? reportedClause(result.record) : ''),
  'too-old': () => 'could not run: this MCP server is too old for it — update the MCP server',
  'refused': (result) => `was refused by the MCP server${said(result.kind === 'refused' ? result.said : '')}`,
  'crashed': (result) => `did not finish: ${result.kind === 'crashed' ? result.why : ''}`,
};

/** The clause a reported record comes to: answered (marker, canary), failed (its kind), or the state it ended in. */
function reportedClause(record: LandedRecord): string {
  return (REPORTED[record.state] ?? endedAs)(record);
}

const REPORTED: Readonly<Record<string, (record: LandedRecord) => string>> = {
  'answered': (record) => `answered${record.markerRead ? ' and read the marker' : ', but did NOT read the marker'}${canaryClause(record.canary)}`,
  'failed': (record) => `failed — ${failureLabel(record.failureKind)}`,
};

function endedAs(record: LandedRecord): string {
  return `ended as ${record.state}${record.reason.length > 0 ? ` — ${record.reason}` : ''}`;
}

function canaryClause(canary: string): string {
  return canary.length > 0 ? `; canary: ${canaryWording(canary)}` : '';
}

function said(sentence: string): string {
  return sentence.length > 0 ? `: ${sentence}` : '';
}

/** The modal's words: a paid call, on whom, where, and what it leaves behind. */
/** A model's check names its row; a caller kind's names the consultant it resolves to. */
function titleOf(kind: string, vendor: string, who: string): string {
  return isModelCheck(kind)
    ? `Check ${vendor} with one real, paid turn of ${who}?`
    : `Check the ${kind} consultant with one real, paid turn of ${who.length > 0 ? who : 'the consultant it resolves to'}?`;
}

export function confirmationOf(kind: string, about: CheckAbout): { title: string; detail: string; go: string } {
  const who = about.model.length > 0 ? `${about.vendor} · ${about.model}` : about.vendor;

  return {
    title: titleOf(kind, about.vendor, who),
    detail: 'The turn runs in a scratch git folder under the temp directory (coai-check-…, removed afterwards) — never your code — '
      + 'and asks the consultant to read a marker file there and to try a file outside it. It is billed like a consultation and '
      + `leaves one session in ${about.vendor.length > 0 ? about.vendor : 'the vendor'}'s own session store, which coai does not delete.`,
    go: 'Run the paid check',
  };
}

const KINDS: readonly string[] = CALLER_KINDS.map((one) => one.id);

export class ConsultantHealthHost {
  /** Kinds whose confirmation is open — a second press then asks nothing. */
  private readonly asking = new Set<string>();

  /** Kinds this window is running a check of. */
  private readonly running = new Set<string>();

  private landed: Readonly<Record<string, LandedRun>> = {};

  constructor(private readonly ports: HealthHostPorts) {}

  /** What this window is checking right now — optimistic, added to what the server persisted. */
  checking(): readonly string[] {
    return [...this.running];
  }

  /** How each kind's last press here ended, and when — so a check that could not even report is said. */
  runs(): Readonly<Record<string, LandedRun>> {
    return this.landed;
  }

  /** A press of Check: confirmed, marked, started — and returned from before the paid turn ends. */
  async check(kind: string, about: CheckAbout): Promise<void> {
    if (!this.pressable(kind)) {
      return;
    }
    this.asking.add(kind);
    const { title, detail, go } = confirmationOf(kind, about);
    const confirmed = await this.ports.confirm(title, detail, go).finally(() => this.asking.delete(kind));
    if (confirmed) {
      this.start(kind);
    }
  }

  /** A caller kind this build knows, with no confirmation open and no check running. */
  private pressable(kind: string): boolean {
    // A caller kind's consultant, or a catalog row (`model-<id>`, the Models tab's ✓ Check — PLAN_one_model_catalog.md D10).
    return (KINDS.includes(kind) || isModelCheck(kind)) && !this.asking.has(kind) && !this.running.has(kind);
  }

  /**
   * The snippet onto the clipboard, in its own words — and `told` only when the write RESOLVED, so the page that was
   * pressed says *Copied* for a rule that is really there. `told` is the pressing page's, which is why it is an argument.
   */
  async copySnippet(kind: string, snippet: SnippetToCopy | undefined, told: (kind: string) => void): Promise<CopyReport> {
    const report = await this.ports.copier.copy(() => (snippet === undefined
      ? { kind: 'refused', said: 'There is no allow rule to copy for that consultant here — the panel will catch up in a moment.' }
      : {
        kind: 'copy',
        text: snippet.text,
        done: `Copied the agy allow rule — paste it into ${snippet.settingsPath}. A prefix rule is not read-only: it allows every command that starts with it.`,
        failed: 'The agy allow rule could not be copied — the clipboard is held by another program.',
      }));
    if (report.copied) {
      told(kind);
    }

    return report;
  }

  private start(kind: string): void {
    this.running.add(kind);
    this.landed = Object.fromEntries(Object.entries(this.landed).filter(([one]) => one !== kind));
    this.ports.render();
    // Detached on purpose — the press returns now. Its catch-all is `finished`: a run that threw is a crash, said.
    void this.finished(kind);
  }

  private async finished(kind: string): Promise<void> {
    const result = await this.ports.runCheck(kind).catch((error: unknown): CheckRunResult => ({ kind: 'crashed', why: `the check could not run: ${String(error)}` }));
    this.running.delete(kind);
    this.landed = { ...this.landed, [kind]: { result, landedUtc: this.ports.nowUtc() } };
    this.ports.announce(kind, landedSentence(kind, result));
    this.ports.settled();
  }
}
