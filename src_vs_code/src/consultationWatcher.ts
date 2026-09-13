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

    const timer = setInterval(() => void this.refresh(), 5000);
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
    const before = signature(this.live);
    this.live = live;
    if (signature(live) !== before) {
      this.onChanged();
    }
  }

  private async readLive(): Promise<Consultation[]> {
    const dir = vscode.Uri.joinPath(this.dataDir, 'consultations');
    const found: Consultation[] = [];
    try {
      for (const [name, kind] of await vscode.workspace.fs.readDirectory(dir)) {
        // `.tmp` is a write in flight, and the `answers` subdirectory holds a vendor's own output
        // files — neither is a consultation, and reading one would be reading somebody's half-file.
        if (kind !== vscode.FileType.File || !name.endsWith('.json') || name.endsWith('.tmp.json')) {
          continue;
        }
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(dir, name));
        const consultation = parseConsultation(new TextDecoder().decode(bytes));
        if (consultation !== undefined && isLive(consultation)) {
          found.push(consultation);
        }
      }
    } catch {
      // No consultations directory yet — nobody has ever been stuck enough to ask.
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
function signature(consultations: readonly Consultation[]): string {
  return consultations
    .map((one) => `${one.id}:${one.status}:${one.turns.length}:${one.alert}`)
    .sort()
    .join('|');
}
