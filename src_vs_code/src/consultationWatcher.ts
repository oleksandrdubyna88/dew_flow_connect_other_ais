import * as vscode from 'vscode';
import { Consultation, isLive, parseConsultation } from './consultations';

/**
 * Watches the server's consultation directory so the sidebar can say what is being asked right now.
 *
 * <p>The `EscalationWatcher` shape, deliberately: a glob over the data directory rather than a handle
 * on a folder, because the directory may not exist until the first consultation, plus a 5 second poll
 * because a file created by another process on a network or virtualised path does not always raise a
 * watcher event. That is not a guess — it is why the escalation watcher has a poll, and a
 * consultation is written by the same server into a directory beside it.</p>
 *
 * <p><b>One surface, not three.</b> The escalation watcher raises a modal and holds a status-bar item
 * because a round is BLOCKED behind its question. Nothing is blocked here: an AI asked another vendor
 * and is waiting on it, which is information rather than a demand, so it appears where a person is
 * already looking and nowhere else.</p>
 */
/**
 * How often the directory is re-read when no watcher event arrives.
 *
 * <p>The same five seconds the escalation watcher uses, and for the same reason: a watcher on a path
 * outside the workspace is not guaranteed on every platform, so this is what makes "you will see it"
 * true rather than likely.</p>
 */
export const POLL_MS = 5000;

export class ConsultationWatcher {
  private readonly disposables: vscode.Disposable[] = [];
  private live: Consultation[] = [];

  /** Called after every refresh, so a view can repaint without polling on its own. */
  public onChanged: () => void = () => {};

  constructor(private readonly dataDir: vscode.Uri) {}

  /** Everything still going — the sidebar renders these and nothing else. */
  get running(): readonly Consultation[] {
    return this.live;
  }

  start(): void {
    const pattern = new vscode.RelativePattern(this.dataDir, 'consultations/*.json');
    const watcher = vscode.workspace.createFileSystemWatcher(pattern);
    this.disposables.push(
      watcher,
      watcher.onDidCreate(() => void this.refresh()),
      watcher.onDidChange(() => void this.refresh()),
      watcher.onDidDelete(() => void this.refresh()),
    );

    const timer = setInterval(() => void this.refresh(), POLL_MS);
    this.disposables.push(new vscode.Disposable(() => clearInterval(timer)));
    void this.refresh();
  }

  dispose(): void {
    for (const d of this.disposables) {
      d.dispose();
    }
  }

  /**
   * Re-reads the directory, and tells the view only when something a person would SEE changed.
   *
   * <p>Every five seconds forever, so the comparison matters: repainting the sidebar on an unchanged
   * directory is a rebuilt DOM under whatever somebody is typing into, twelve times a minute.</p>
   */
  async refresh(): Promise<void> {
    const live = await this.readLive();
    if (live === undefined) {
      return; // the last good snapshot stands; see `readLive`
    }
    const before = signature(this.live);
    this.live = live;
    if (signature(live) !== before) {
      this.onChanged();
    }
  }

  /**
   * What is running, or NOTHING when the directory could not be read.
   *
   * <p>The distinction is the point, and it is the one the plan round asked for. An absent directory
   * is the ordinary state — nobody has ever been stuck enough to ask — and reads as no consultations.
   * A read that THROWS is a different thing: a rename landing on an open handle is the ordinary
   * Windows case, and blanking the region for it would take a running consultation off the screen
   * somebody is reading, for five seconds, at random. The last good snapshot stands instead.</p>
   *
   * <p>A single file that will not parse is skipped rather than failing the pass: the server
   * publishes records atomically (temp plus rename), so anything unreadable is not a record.</p>
   */
  private async readLive(): Promise<Consultation[] | undefined> {
    const dir = vscode.Uri.joinPath(this.dataDir, 'consultations');
    let entries: [string, vscode.FileType][];
    try {
      entries = await vscode.workspace.fs.readDirectory(dir);
    } catch (error) {
      // Absent is empty; anything ELSE keeps what we had — and the error's own code is what tells
      // them apart. It used to ask `stat`, whose own failure returned false: so a permission error
      // or a provider hiccup, which fails BOTH calls, read as "the directory is gone" and wiped every
      // live consultation off the panel mid-conversation. A code is a contract; the absence of an
      // answer is not. (CodeRabbit, on the pull request.)
      return error instanceof vscode.FileSystemError && error.code === 'FileNotFound' ? [] : undefined;
    }

    const found: Consultation[] = [];
    for (const [name, kind] of entries) {
      // `.tmp` is a write in flight, and the `answers` subdirectory holds a vendor's own output
      // files — neither is a consultation, and reading one would be reading somebody's half-file.
      if (kind !== vscode.FileType.File || !name.endsWith('.json') || name.endsWith('.tmp.json')) {
        continue;
      }
      try {
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(dir, name));
        const consultation = parseConsultation(new TextDecoder().decode(bytes));
        if (consultation !== undefined && isLive(consultation)) {
          found.push(consultation);
        }
      } catch {
        // One file being replaced right now — a rename landing on an open handle, the ordinary
        // Windows case. The consultation it describes is KEPT from the last pass rather than dropped:
        // a card blinking off a section somebody is reading, for five seconds, at random, is the
        // defect. It goes when a pass reads the file and finds it over. (codex, code round, twice.)
        const last = this.live.find((one) => one.id === idOf(name));
        if (last !== undefined) {
          found.push(last);
        }
        continue;
      }
    }

    return found;
  }

}

/**
 * What a person can see, as one string.
 *
 * <p>The turn count and the status are in it because those are what the card SHOWS; the timestamps
 * are not, because the card's "3 min ago" is computed at paint time and would make every poll a
 * change.</p>
 */
/** A consultation file is named by its id, which is how a failed read still knows what it lost. */
function idOf(name: string): string {
  return name.replace(/\.json$/, '');
}

function signature(consultations: readonly Consultation[]): string {
  return consultations
    .map((one) => `${one.id}:${one.status}:${one.turns.length}:${one.alert}`)
    // Compared explicitly rather than by the default sort, which orders by UTF-16 code unit: this
    // string is only ever compared with another one built the same way, so any total order would do
    // — and a sort whose ORDER is implicit is the shape that stops being harmless the day somebody
    // reads the output rather than diffing it. (SonarCloud S2871, on the pull request.)
    .sort((one, other) => one.localeCompare(other))
    .join('|');
}
