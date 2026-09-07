import { updateAvailable } from './cliVersions';
import { CoaiSettings } from './settingsShape';
import { serverSettingsJson, writtenBy } from './serverSettingsFile';
import { Vendor } from './vendors';

/** Everything the sync needs from the editor, so the sync itself needs nothing from it. */
export type ReadConfiguration = () => { settings: CoaiSettings; vendors: readonly Vendor[] };
export type WriteFile = (json: string) => Promise<void>;
/**
 * What the settings file says, or why it could not be asked.
 *
 * <p><b>Three answers, not two, and the third is the point.</b> An earlier draft returned a string
 * and answered `''` for everything that went wrong — so a file that is LOCKED, or on a volume that
 * blinked, was indistinguishable from a file that is not there. One of those is permission to write
 * and the other is the exact overwrite this class exists to prevent, reached through a different
 * door. Three reviewers found it independently on this story's code round.</p>
 */
export type ExistingFile =
  | { readonly kind: 'contents'; readonly text: string }
  | { readonly kind: 'absent' }
  | { readonly kind: 'unreadable' };

export type ReadExisting = () => Promise<ExistingFile>;
/** Told which build's file was left alone. Called once per distinct version, not once per run. */
export type ReportRefusal = (theirVersion: string) => void;

/**
 * Runs the read, the comparison and the write with nobody else in the file.
 *
 * <p>A callback rather than a lock this class takes for itself, for the same reason it takes a
 * writer rather than opening a file: it holds no VS Code type and no filesystem. The caller owns
 * the mechanism; this owns the fact that the three steps are ONE step.</p>
 *
 * <p>A section that declines to run the work is not an error. Somebody else is mid-write, and they
 * are writing the same settings from the same configuration — the only cost of standing aside is a
 * write that happens on the next configuration change instead of this one.</p>
 */
export type CriticalSection = (work: () => Promise<void>) => Promise<boolean>;

/**
 * What one call to {@link ServerSettingsSync.sync} did.
 *
 * <p>`busy` is the one the caller has to act on: the work did not run because another window is in
 * the file, so this configuration is still unwritten and nothing will fire again by itself. The
 * others are terminal — the content is on disk, or it was already, or a newer build owns it, or the
 * disk refused and the next change will try again.</p>
 */
export type SyncOutcome = 'written' | 'unchanged' | 'stood-down' | 'busy' | 'failed';

/**
 * Mirrors the workspace's `coai.*` settings into the file the server reads.
 *
 * <p><b>Why this is not the panel's job.</b> It used to be, and that was the defect: the write sat
 * in `PanelProvider.render()` behind the view guard, and the `onDidChangeConfiguration`
 * subscription was registered inside `resolveWebviewView`. VS Code resolves a webview view lazily
 * — only when somebody first opens it — so in a window where nobody had opened the ConnectOtherAIs
 * panel, nothing watched the settings and nothing mirrored them. A person could set
 * `coai.onExhausted` to `good_enough`, restart everything, and the server would go on answering
 * `call_human` from whatever `env` block had been pasted into a client config months before.</p>
 *
 * <p>The server needs its settings whether or not a person is looking at a webview, so this takes
 * no view, holds no VS Code type, and is created at activation.</p>
 *
 * <p><b>And the file has more than one writer.</b> Every open VS Code window runs its own
 * extension host, they all receive `onDidChangeConfiguration`, and they all write this one path —
 * including hosts still running a build from before the last update, because VS Code keeps the
 * loaded extension until a window reloads. On 2026-09-07 that reverted a Team-server reviewer:
 * `coai.vendors` held `runtime: "remote"`, the file held `runtime: "codex"`, written 0.4 seconds
 * later by a host on 0.31.0 whose `RUNTIMES` has no `remote`. `vendorsFrom` rewrites an unknown
 * runtime so the row still launches something — a sensible fallback for RENDERING that must never
 * have been persisted. So the file records who wrote it, and an older build stands down.</p>
 */
/**
 * The defaults, as module constants rather than async arrows written into the parameter list.
 *
 * <p>An analyser reads an `async` expression in a constructor's default as an asynchronous operation
 * IN the constructor and calls it critical, which is a fair reading of the shape even though nothing
 * here awaits during construction — the value is a function, and it runs when a caller runs it. A
 * named constant says that plainly and costs nothing.</p>
 */
const NO_FILE: ReadExisting = async () => ({ kind: 'absent' });

/** No lock: run the work and report that it ran. What a caller with no filesystem gets. */
const NO_LOCK: CriticalSection = async (work) => {
  await work();

  return true;
};

export class ServerSettingsSync {
  /** The last content actually written, so an unchanged configuration touches nothing. */
  private lastWritten = '';

  /**
   * Which stamp this window has already complained about.
   *
   * <p>The version rather than a bare flag, and cleared on a successful write. A boolean makes the
   * FIRST stand-down the only one a window ever mentions: somebody who updates the other window to
   * a newer build and hits the same wall again is told nothing, and somebody whose write later
   * lands and then stands down a second time is told nothing either. What must not repeat is one
   * sentence about one version, which is a different thing from saying it once per lifetime.
   * Accepted finding, this story's plan round.</p>
   */
  private reportedFor = '';

  /**
   * Did the last attempt stand down rather than write?
   *
   * <p>Because the "nothing changed, nothing to do" short-circuit compares against the last
   * SUCCESSFUL write, and after a stand-down that is an older state than the file holds. Change a
   * setting, stand down, change it back, and the content matches `lastWritten` exactly — so the
   * write is skipped and the pending value never lands at all. Accepted finding, this story's code
   * round.</p>
   */
  private stoodDown = false;

  constructor(
    private readonly read: ReadConfiguration,
    private readonly write: WriteFile,
    private readonly version = '',
    private readonly readExisting: ReadExisting = NO_FILE,
    private readonly report: ReportRefusal = () => {},
    private readonly critical: CriticalSection = NO_LOCK,
  ) {}

  /**
   * Writes the settings file when — and only when — its content would change AND this build is not
   * about to overwrite a newer one.
   *
   * <p>The server reloads on this file's mtime and length (`PanelServiceHost`), and the panel
   * repaints on every live poll, so rewriting identical content would ask the server to re-read its
   * settings several times a minute for nothing.</p>
   *
   * <p>A failed write is not remembered as done: the disk being unwritable is not worth
   * interrupting anyone over, but the next change must still try. A REFUSAL is not remembered
   * either, so the pending content is written the moment writing becomes possible.</p>
   *
   * <p><b>Which is not the same as "when the other window closes".</b> Nothing here observes that:
   * a window closing does not touch this file, fires no configuration event, and this class has no
   * watcher. The write lands on the NEXT configuration change or the next activation — and the
   * cure a person actually has is the Reload Window button on the warning, which is an activation.
   * The story's own requirement claimed a trigger that does not exist, and the plan round said so.</p>
   */
  async sync(): Promise<SyncOutcome> {
    const { settings, vendors } = this.read();
    const json = serverSettingsJson(settings, vendors, this.version);
    if (json === this.lastWritten && !this.stoodDown) {
      return 'unchanged';
    }

    // The comparison and the write are ONE step, or they are a race. Two guard-aware hosts can
    // otherwise both read a stamp they are allowed to overwrite, and the second to finish wins with
    // a payload it decided on before the first one's write existed. The collision that started this
    // epic was 0.4 seconds wide, which is four orders of magnitude more than this section holds.
    // Raised three times on this plan's own round.
    let outcome: SyncOutcome = 'failed';
    let entered = false;
    try {
      entered = await this.critical(async () => {
        if (await this.wouldOverwriteANewerBuild()) {
          this.stoodDown = true;
          outcome = 'stood-down';
          return;
        }

        try {
          await this.write(json);
          this.lastWritten = json;
          this.stoodDown = false;
          // The situation is over. A stand-down after this is news, not a repeat.
          this.reportedFor = '';
          outcome = 'written';
        } catch {
          // Not writable. The pasted env block remains a way in, and this runs from a configuration
          // listener — throwing here would put an extension error in front of somebody for every
          // keystroke in their settings file, over a disk problem a settings panel cannot fix.
          outcome = 'failed';
        }
      });
    } catch {
      // The section itself failed — a lock that could not be created, a filesystem that threw. Same
      // reasoning as the write: this runs from a configuration listener and must not surface.
      return 'failed';
    }

    // Busy is not failure and not success: nobody wrote, and nothing will fire again on its own.
    // Saying so is what lets the caller schedule the ONE retry that keeps a configuration from
    // being dropped because another window happened to be writing when it changed.
    return entered ? outcome : 'busy';
  }

  /**
   * Is the file on disk the work of a build newer than this one?
   *
   * <p>The comparison is `updateAvailable` — the same one the CLI update buttons have used since
   * they existed — rather than a second version comparator written beside it. It reads numeric
   * segments, so `0.31.10` is after `0.31.9`, which is the comparison a string check always gets
   * wrong.</p>
   *
   * <p><b>With one normalisation in front of it, because it splits on dots and nothing else.</b>
   * `0.31.3+build.7` becomes four segments there, the fourth being 7 against this build's nothing,
   * and a local build would have blocked every released one. So a `-pre` or `+build` suffix is cut
   * before the comparison and a pre-release compares as its release: `0.31.3-alpha.1` and `0.31.3`
   * are one version to this guard, and neither blocks the other. That is the right reading of the
   * question it asks — a SHIPPED build must not overwrite a newer SHIPPED build — and between two
   * builds of the same version the last writer wins, exactly as before. Written as a claim in the
   * comment first, and the test refuted it; the cut is what makes the claim true.</p>
   *
   * <p>An unstamped build cannot ask this question at all, so an empty version never refuses: a run
   * with no version configured behaves exactly as this class did before the stamp existed.</p>
   */
  private async wouldOverwriteANewerBuild(): Promise<boolean> {
    if (this.version.length === 0) {
      return false;
    }

    // A guard that cannot answer stands the write DOWN rather than waving it through, and that
    // direction is the whole point: the cost of a needless wait is one configuration change, and
    // the cost of a needless write is somebody's reviewer disappearing. `.catch` on the promise is
    // not enough on its own — a caller-supplied reader can throw synchronously, and this is awaited
    // outside the try that protects the write.
    let existing: ExistingFile;
    try {
      existing = await this.readExisting();
    } catch {
      return true;
    }

    if (existing.kind === 'unreadable') {
      // Silent, like the failed-write branch below and for the same reason: a volume that will not
      // answer is not a thing a settings panel can fix, and the write would very likely fail too.
      return true;
    }

    if (existing.kind === 'absent') {
      return false;
    }

    const theirs = writtenBy(existing.text);
    if (theirs.length === 0 || !updateAvailable(release(this.version), release(theirs))) {
      return false;
    }

    // Suppressed on the NORMALISED version, shown as the stamp actually found: if `0.31.9-alpha.1`
    // and `0.31.9` are one version to the comparison, they are one version to the sentence too.
    const seen = release(theirs);
    if (this.reportedFor !== seen) {
      this.reportedFor = seen;
      this.report(displayable(theirs));
    }

    return true;
  }
}

/** `0.31.3-alpha.1` and `0.31.3+build.7` → `0.31.3`. The release a build belongs to, and no more. */
function release(version: string): string {
  return (version.trim().split(/[-+]/)[0] ?? version).trim();
}

/**
 * A stamp read out of a file, made fit for a notification.
 *
 * <p>It is arbitrary text from a file anybody can edit, and it goes into a VS Code dialog. Control
 * characters and an unbounded length are not an attack anyone is expecting here — they are how a
 * corrupted file produces a message nobody can read.</p>
 */
function displayable(stamp: string): string {
  // Written as a code-point comparison rather than a control-character class, because the
  // class has to be SPELLED, and the first attempt at spelling it put a real NUL byte in this
  // file. Everything below the space, and DEL, is dropped.
  return [...stamp].filter((c) => c >= ' ' && c !== '\u007f').join('').slice(0, 64);
}
