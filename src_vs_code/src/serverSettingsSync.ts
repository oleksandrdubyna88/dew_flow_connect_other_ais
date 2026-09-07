import { updateAvailable } from './cliVersions';
import { CoaiSettings } from './settingsShape';
import { serverSettingsJson, writtenBy } from './serverSettingsFile';
import { Vendor } from './vendors';

/** Everything the sync needs from the editor, so the sync itself needs nothing from it. */
export type ReadConfiguration = () => { settings: CoaiSettings; vendors: readonly Vendor[] };
export type WriteFile = (json: string) => Promise<void>;
/** The file as it is now, or an empty string when there is none. Never throws. */
export type ReadExisting = () => Promise<string>;
/** Told which build's file was left alone. Called once per distinct version, not once per run. */
export type ReportRefusal = (theirVersion: string) => void;

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

  constructor(
    private readonly read: ReadConfiguration,
    private readonly write: WriteFile,
    private readonly version = '',
    private readonly readExisting: ReadExisting = async () => '',
    private readonly report: ReportRefusal = () => {},
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
  async sync(): Promise<void> {
    const { settings, vendors } = this.read();
    const json = serverSettingsJson(settings, vendors, this.version);
    if (json === this.lastWritten) {
      return;
    }

    if (await this.wouldOverwriteANewerBuild()) {
      return;
    }

    try {
      await this.write(json);
      this.lastWritten = json;
      // The situation is over. A stand-down that happens again after this is news, not a repeat.
      this.reportedFor = '';
    } catch {
      // Not writable. The pasted env block remains a way in, and this runs from a configuration
      // listener — throwing here would put an extension error in front of somebody for every
      // keystroke in their settings file, over a disk problem a settings panel cannot fix.
    }
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

    const theirs = writtenBy(await this.readExisting().catch(() => ''));
    if (theirs.length === 0 || !updateAvailable(release(this.version), release(theirs))) {
      return false;
    }

    if (this.reportedFor !== theirs) {
      this.reportedFor = theirs;
      this.report(theirs);
    }

    return true;
  }
}

/** `0.31.3-alpha.1` and `0.31.3+build.7` → `0.31.3`. The release a build belongs to, and no more. */
function release(version: string): string {
  return version.split(/[-+]/)[0] ?? version;
}
