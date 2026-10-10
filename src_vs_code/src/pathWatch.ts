import { type NamedRoot, dataDirFinding } from './dataDir';
import { watchedDirs } from './escalationDirs';
import { expectDataRoot, missingDataRoot, publishFindings } from './findingsBoard';
import { type ExecutableSide, pathForThisSide, qualified } from './pathFamily';
import { type PathFinding, type PathValue, agreed, findingSubject, pathFindings } from './pathFindings';
import type { PathProbes } from './pathProbes';
import { QCONSULT_ROOTS, VENDOR_EXECUTABLE, WATCHED_DIRECTORIES } from './pathSettings';
import type { SettingProblem } from './providers';

/**
 * The watch over this side's paths (todo/PLAN_paths_per_side.md E5.3): what the settings name, asked of the disk through
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
  vendors(): readonly { readonly id: string; readonly executablePath: string }[];
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
}

export class PathWatch {
  /** The missing paths said in this run, by subject — so a finding is said when it APPEARS, not on every refresh. */
  private said: ReadonlyMap<string, PathFinding> = new Map();

  /** The server's problems, once it has answered and is trusted; `undefined` leaves this window's own findings. */
  private serverProblems: readonly SettingProblem[] | undefined = undefined;

  constructor(private readonly deps: PathWatchDeps) {}

  /** Asks, publishes, says — and keeps doing so as the disk answers. */
  start(): void {
    this.deps.probes.onChange(() => { this.refresh(); });
    expectDataRoot(this.dataRootSettled());
    this.refresh();
  }

  /** The settings or the disk's answers changed: decide again from what is known now. */
  refresh(): void {
    try {
      const findings = agreed(this.ownFindings(), this.serverProblems, this.deps.side.family);
      publishFindings(findings);
      this.tell(findings);
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
   * Whether the NAMED data root is missing, once the disk has been asked (E5.7): what the settings mirror asks before it
   * writes — a confirmed absence, never an unknown answer or the default location, which are written as today.
   */
  async dataRootMissing(): Promise<boolean> {
    await this.dataRootSettled();

    return missingDataRoot() !== undefined;
  }

  /** Every stored value of this side the disk is asked about, with the path it is asked at. */
  values(): readonly PathValue[] {
    return [...this.rootValues(), ...this.cliValues(), ...this.watchedValues()];
  }

  private rootValues(): readonly PathValue[] {
    const windows = this.deps.side.family === 'windows';

    return this.deps.reads.roots().map((root) => ({ setting: QCONSULT_ROOTS, value: root.trim(), path: qualified(root, windows, this.deps.side.systemDrive) }));
  }

  /** This side's CLI paths: the other side's are skipped by E1's rule and asked about nowhere. */
  private cliValues(): readonly PathValue[] {
    return this.deps.reads.vendors().flatMap((row) => {
      const here = pathForThisSide(row, this.deps.side);

      return here.path.length === 0 ? [] : [{ setting: VENDOR_EXECUTABLE, value: row.executablePath.trim(), path: here.path, owner: row.id }];
    });
  }

  /** The other installations' folders this side reads — never its own, never the other side's. */
  private watchedValues(): readonly PathValue[] {
    return watchedDirs(this.deps.reads.ownDataDir(), this.deps.reads.watched(), this.deps.platform)
      .slice(1)
      .filter((dir) => dir.path.length > 0)
      .map((dir) => ({ setting: WATCHED_DIRECTORIES, value: dir.asked, path: dir.path }));
  }

  private ownFindings(): readonly PathFinding[] {
    const probes = this.deps.probes;
    const own = pathFindings(this.values(), this.deps.side.family, (path, kind) => probes.presence(path, kind));
    const named = this.deps.reads.dataRoot();
    const data = dataDirFinding(named, named === undefined ? 'pending' : probes.presence(named.root, 'folder'), this.deps.side.family);

    return data === undefined ? own : [...own, data];
  }

  /**
   * Say what is NEWLY missing, resolve what no longer is. Said when a finding appears rather than on every refresh: the
   * funnel records every arrival, and a repaint-driven re-say would be a ledger row per repaint — the storm this product
   * bounds elsewhere. A resolved path going missing again is said again: the situation ended, so its return is news.
   */
  private tell(findings: readonly PathFinding[]): void {
    const missing = new Map(findings.filter((one) => one.state === 'missing').map((one) => [findingSubject(one), one] as const));
    this.sayNew(missing);
    this.resolveGone(missing);
    this.said = missing;
  }

  private sayNew(missing: ReadonlyMap<string, PathFinding>): void {
    for (const [subject, finding] of missing) {
      if (!this.said.has(subject)) {
        this.deps.say(finding);
      }
    }
  }

  private resolveGone(missing: ReadonlyMap<string, PathFinding>): void {
    for (const [subject, finding] of this.said) {
      if (!missing.has(subject)) {
        this.deps.resolved(finding);
      }
    }
  }
}

/** The subject the notice and the funnel key a finding on. */
export { findingSubject };
