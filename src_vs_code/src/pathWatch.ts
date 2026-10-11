import { type NamedRoot, dataDirFinding } from './dataDir';
import { watchedDirs } from './escalationDirs';
import { absentDataRoot, expectDataRoot, noteDataRoot, publishFindings } from './findingsBoard';
import { type ExecutableSide, pathForThisSide, qualified, storedForThisSide, type StoredExecutable } from './pathFamily';
import { type PathFinding, type PathValue, type Presence, type ProbeKind, agreed, asked, findingSubject, findingsOf, probeKindOf, valueSubject } from './pathFindings';
import type { PathProbes } from './pathProbes';
import { DATA_DIRECTORY, QCONSULT_ROOTS, VENDOR_EXECUTABLE, WATCHED_DIRECTORIES } from './pathSettings';
import type { SettingProblem } from './providers';

/**
 * The watch over this side's paths (research/PLAN_paths_per_side.md E5.3): what the settings name, asked of the disk through
 * the bounded probes, published as findings for the page, and SAID — once per window run and once per missing path, at
 * every start and again whenever a path goes missing after being found.
 *
 * <p><b>Why it runs from activation and not from the panel.</b> VS Code resolves a webview view lazily: in a window where
 * nobody has opened the ConnectOtherAIs sidebar nothing of the panel runs, and a notice that depended on it would be the
 * silence this plan exists to end. So the watch starts with the extension, before the settings file is written, and the
 * panel merely reads what it published (`findingsBoard.ts`).</p>
 *
 * <p><b>Who says what.</b> The question consultant's roots are coai-mcp's to judge: once the server has answered with
 * `settingProblems` and lists the capability, its answer replaces this window's own probe for them (E5.6), so the page
 * and the server cannot disagree; a server that has not answered, or is older, leaves this window's own findings. A CLI
 * path, a watched folder and the named data root are this window's own. A value spelled for the other side is never a
 * finding here — E1 already says whose it is.</p>
 *
 * <p>`vscode`-free: what it reads, how it asks the disk and how it speaks are handed in (`pathWatchHost.ts` is the
 * wiring), so a test runs the operator's case without an editor.</p>
 */

/** What the watch reads, each time it refreshes — the settings as THIS side reads them. */
export interface PathReads {
  roots(): readonly string[];
  /** The catalog rows WHOLE — the per-side value included (E3.1), so this side's is the one watched. */
  vendors(): readonly (StoredExecutable & { readonly id: string })[];
  watched(): readonly string[];
  /** This window's own data directory — the first watched folder, never probed. */
  ownDataDir(): string;
  dataRoot(): NamedRoot | undefined;
}

export interface PathWatchDeps {
  readonly reads: PathReads;
  readonly probes: PathProbes;
  /** This host's family, system drive and file probe — what decides whether a CLI path is this side's. */
  readonly side: ExecutableSide;
  readonly platform: NodeJS.Platform;
  /** A path went missing: raise the notice (once per run is the funnel's, through `notifyOnceThen`). */
  readonly say: (finding: PathFinding) => void;
  /** A missing path came back or was changed: the next time it goes missing is news. */
  readonly resolved: (finding: PathFinding) => void;
  /**
   * A repeating timer — the host's `setInterval`, unref'd and disposed with the extension. Absent (a test drives
   * {@link PathWatch.recheck} by hand), nothing repeats.
   */
  readonly every?: (work: () => void, ms: number) => void;
}

/**
 * How often a path the disk said is ABSENT is asked again — so a folder made again, or a drive mounted, is seen with no
 * settings edit: the notice clears, the guard lets the writers through and the settings mirror writes (E5.7, risk item
 * F). Only absent paths are asked, one outstanding probe each, so a quiet window asks nothing at all.
 */
export const RECHECK_MS = 5_000;

/** A path the disk is asked about again, and as what. */
type Recheck = readonly [string, ProbeKind];

/** One asked value's answer, by the subject the notice is keyed on. */
type Answer = readonly [string, Presence];

/** What one refresh knows of this window's own values: the findings, and every asked value's answer by subject. */
interface Own {
  readonly findings: readonly PathFinding[];
  readonly answers: ReadonlyMap<string, Presence>;
  /** The named data root and what the disk says of it now — what the storage guard keeps its own state from. */
  readonly root: RootAnswer;
  /** Every asked path the disk said is absent — what the recheck asks again. */
  readonly absent: readonly Recheck[];
}

/** The named data root (none for the default location) and the disk's answer about it. */
interface RootAnswer {
  readonly path: string | undefined;
  readonly presence: Presence;
}

/**
 * A definitive answer, or no value to answer for any more — what may RESOLVE a said path. `pending` and `unknown` may
 * not: a settings change drops every answer and asks again, and resolving on that gap re-said every still-missing path
 * on every keystroke in the settings (sixteen ledger rows for one CLI path, on E5's first run in a real editor).
 */
function settled(answer: Presence | undefined): boolean {
  return answer === undefined || answer === 'here' || answer === 'absent';
}

export class PathWatch {
  /** The missing paths said in this run, by subject — so a finding is said when it APPEARS, not on every refresh. */
  private said: ReadonlyMap<string, PathFinding> = new Map();

  /** The server's problems, once it has answered and is trusted; `undefined` leaves this window's own findings. */
  private serverProblems: readonly SettingProblem[] | undefined = undefined;

  /** The paths the last refresh found absent — what {@link recheck} asks again. */
  private absent: readonly Recheck[] = [];

  constructor(private readonly deps: PathWatchDeps) {}

  /** Asks, publishes, says — and keeps doing so as the disk answers, rechecking what it found absent. */
  start(): void {
    this.deps.probes.onChange(() => { this.refresh(); });
    expectDataRoot(this.dataRootSettled());
    this.refresh();
    this.deps.every?.(() => { this.recheck(); }, RECHECK_MS);
  }

  /** Every path the last refresh found absent, asked of the disk again — an answer that changed refreshes the watch. */
  recheck(): void {
    for (const [path, kind] of this.absent) {
      this.deps.probes.recheck(path, kind);
    }
  }

  /** The settings or the disk's answers changed: decide again from what is known now. */
  refresh(): void {
    try {
      const own = this.ownFindings();
      const findings = agreed(own.findings, this.serverProblems, this.deps.side.family);
      // The guard's own state first: only a confirmed root lifts a kept absence, a re-ask or a timeout never does.
      noteDataRoot(own.root.path, own.root.presence);
      this.absent = own.absent;
      publishFindings(findings);
      this.tell(findings, own.answers);
    } catch (reason) {
      // The detached edge: a probe landing calls this with nobody above it, and a read that threw must not take the
      // watch down in silence.
      console.error('ConnectOtherAIs: the paths could not be checked', reason);
    }
  }

  /** A setting changed: what the disk said is dropped and asked again. */
  forget(): void {
    this.deps.probes.forget();
    this.refresh();
  }

  /** What coai-mcp said about its settings — `undefined` from a server that has not answered or predates the field. */
  serverSaid(problems: readonly SettingProblem[] | undefined): void {
    this.serverProblems = problems;
    this.refresh();
  }

  /** Resolved once the named data root has been asked about (the probe's deadline at most) — nothing to wait for by default. */
  async dataRootSettled(): Promise<void> {
    const named = this.deps.reads.dataRoot();
    if (named !== undefined) {
      await this.deps.probes.settled(named.root, 'folder');
    }
  }

  /**
   * Whether the NAMED data root is refused, once the disk has been asked (E5.7): what the settings mirror asks before it
   * writes. An absence the disk SAID is kept until a probe confirms the root is back — the re-ask after a settings change
   * and a probe past its deadline do not lift it (`findingsBoard.absentDataRoot`, the risk consultation's item D); the
   * default location is never refused.
   */
  async dataRootMissing(): Promise<boolean> {
    await this.dataRootSettled();

    return absentDataRoot() !== undefined;
  }

  /** Every stored value of this side the disk is asked about, with the path it is asked at. */
  values(): readonly PathValue[] {
    return [...this.rootValues(), ...this.cliValues(), ...this.watchedValues()];
  }

  private rootValues(): readonly PathValue[] {
    const windows = this.deps.side.family === 'windows';

    return this.deps.reads.roots().map((root) => ({ setting: QCONSULT_ROOTS, value: root.trim(), path: qualified(root, windows, this.deps.side.systemDrive) }));
  }

  /**
   * This side's CLI paths: the other side's are skipped by E1's rule and asked about nowhere. The finding's subject is
   * THIS side's stored value (paths per side E3.1: the row's per-side value for this family when one was saved, else the
   * legacy value) — what the card's box draws and keys its mark on, never a legacy value the other side mirrored.
   */
  private cliValues(): readonly PathValue[] {
    return this.deps.reads.vendors().flatMap((row) => {
      const here = pathForThisSide(row, this.deps.side);
      const value = storedForThisSide(row, this.deps.side.family);

      return here.path.length === 0 ? [] : [{ setting: VENDOR_EXECUTABLE, value, path: here.path, owner: row.id }];
    });
  }

  /** The other installations' folders this side reads — never its own, never the other side's. */
  private watchedValues(): readonly PathValue[] {
    return watchedDirs(this.deps.reads.ownDataDir(), this.deps.reads.watched(), this.deps.platform)
      .slice(1)
      .filter((dir) => dir.path.length > 0)
      .map((dir) => ({ setting: WATCHED_DIRECTORIES, value: dir.asked, path: dir.path }));
  }

  private ownFindings(): Own {
    const family = this.deps.side.family;
    const answers = asked(this.values(), family, (path, kind) => this.deps.probes.presence(path, kind));
    const root = this.dataRootAnswer();
    const findings = [...findingsOf(answers, family), ...(root.finding === undefined ? [] : [root.finding])];
    const absent = [
      ...answers.filter((one) => one.presence === 'absent').map((one): Recheck => [one.value.path, probeKindOf(one.value.setting.kind)]),
      ...(root.root.presence === 'absent' && root.root.path !== undefined ? [[root.root.path, 'folder'] as const] : []),
    ];

    return {
      findings,
      answers: new Map([...answers.map((one): Answer => [valueSubject(one.value), one.presence]), ...root.answer]),
      root: root.root,
      absent,
    };
  }

  /** The named data root's finding and its answer — none for the default location, which is never asked (D3). */
  private dataRootAnswer(): { readonly finding: PathFinding | undefined; readonly answer: readonly Answer[]; readonly root: RootAnswer } {
    const named = this.deps.reads.dataRoot();
    if (named === undefined) {
      return { finding: undefined, answer: [], root: { path: undefined, presence: 'pending' } };
    }
    const presence = this.deps.probes.presence(named.root, 'folder');

    return {
      finding: dataDirFinding(named, presence, this.deps.side.family),
      answer: [[valueSubject({ setting: DATA_DIRECTORY, value: named.root }), presence]],
      root: { path: named.root, presence },
    };
  }

  /**
   * Say what is NEWLY missing, resolve what no longer is — on a settled answer. Said when a finding appears rather than on
   * every refresh: the funnel records every arrival, and a repaint-driven re-say would be a ledger row per repaint — the
   * storm this product bounds elsewhere. A resolved path going missing again is said again: the situation ended, so its
   * return is news.
   */
  private tell(findings: readonly PathFinding[], answers: ReadonlyMap<string, Presence>): void {
    const missing = new Map(findings.filter((one) => one.state === 'missing').map((one) => [findingSubject(one), one] as const));
    this.sayNew(missing);
    this.said = new Map([...this.stillSaid(missing, answers), ...missing]);
  }

  private sayNew(missing: ReadonlyMap<string, PathFinding>): void {
    for (const [subject, finding] of missing) {
      if (!this.said.has(subject)) {
        this.deps.say(finding);
      }
    }
  }

  /**
   * Resolves a said path that is no longer missing on a SETTLED answer — the disk says it is there, the server took it, or
   * the value left the settings — and keeps one whose answer is pending or unknown, so a settings change re-asks the disk
   * without a resolve-and-say-again for every path that is still missing (`settled`).
   */
  private stillSaid(missing: ReadonlyMap<string, PathFinding>, answers: ReadonlyMap<string, Presence>): ReadonlyMap<string, PathFinding> {
    const kept = new Map<string, PathFinding>();
    for (const [subject, finding] of this.said) {
      if (missing.has(subject)) {
        continue;
      }
      if (settled(answers.get(subject))) {
        this.deps.resolved(finding);
      } else {
        kept.set(subject, finding);
      }
    }

    return kept;
  }
}

/** The subject the notice and the funnel key a finding on. */
export { findingSubject };
