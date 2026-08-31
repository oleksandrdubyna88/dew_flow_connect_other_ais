import * as vscode from 'vscode';
import {
  answerJson,
  Escalation,
  modalText,
  parseEscalation,
  shouldPrompt,
  statusBarText,
} from './escalations';

/**
 * Watches the server's escalation directory and puts the question in front of a person.
 *
 * <p>Three surfaces, on purpose: a modal so it arrives, a status-bar item so a dismissed modal
 * does not lose it, and the rounds view so it can be read in full. A round is BLOCKED behind this
 * question — a notification that can be missed is the wrong shape for that.</p>
 *
 * <p>The directory may not exist yet (no escalation has ever happened), so the watcher is a
 * glob over the data dir rather than a handle on a folder, and a poll backs it up: a file created
 * by another process on a network or virtualised path does not always raise a watcher event.</p>
 */
export class EscalationWatcher {
  private readonly prompted = new Set<string>();
  private readonly statusItem: vscode.StatusBarItem;
  private readonly disposables: vscode.Disposable[] = [];
  private open: Escalation[] = [];

  /** Called after every refresh, so a view can repaint without polling on its own. */
  public onChanged: () => void = () => {};

  constructor(private readonly dataDir: vscode.Uri) {
    this.statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    this.statusItem.command = 'coai.showRounds';
    this.statusItem.tooltip = 'A ConnectOtherAIs review is waiting on your answer';
    this.disposables.push(this.statusItem);
  }

  /** Everything currently unanswered — the rounds view renders these. */
  get openQuestions(): readonly Escalation[] {
    return this.open;
  }

  start(): void {
    const pattern = new vscode.RelativePattern(this.dataDir, 'escalations/*.json');
    const watcher = vscode.workspace.createFileSystemWatcher(pattern);
    this.disposables.push(
      watcher,
      watcher.onDidCreate(() => void this.refresh()),
      watcher.onDidChange(() => void this.refresh()),
      watcher.onDidDelete(() => void this.refresh()),
    );

    // A watcher on a path outside the workspace is not guaranteed on every platform; the poll is
    // what makes the promise "you will see the question" true rather than likely.
    const timer = setInterval(() => void this.refresh(), 5000);
    this.disposables.push(new vscode.Disposable(() => clearInterval(timer)));
    void this.refresh();
  }

  dispose(): void {
    for (const d of this.disposables) {
      d.dispose();
    }
  }

  async refresh(): Promise<void> {
    this.open = await this.readOpen();
    const text = statusBarText(this.open.length);
    if (text.length === 0) {
      this.statusItem.hide();
    } else {
      this.statusItem.text = text;
      this.statusItem.show();
    }

    this.onChanged();

    for (const escalation of this.open) {
      if (shouldPrompt(escalation.id, this.prompted, false)) {
        this.prompted.add(escalation.id);
        void this.prompt(escalation);
      }
    }
  }

  /** The modal. Dismissing it is safe: the status bar and the rounds view still hold the question. */
  private async prompt(escalation: Escalation): Promise<void> {
    const answer = await vscode.window.showWarningMessage(
      modalText(escalation),
      { modal: true },
      'Answer…',
    );
    if (answer !== 'Answer…') {
      return;
    }
    await this.answerCommand(escalation);
  }

  /** Asks for the text and writes the answer file the server is waiting for. */
  async answerCommand(escalation: Escalation): Promise<void> {
    const text = await vscode.window.showInputBox({
      title: `ConnectOtherAIs — ${escalation.branch}`,
      prompt: escalation.question,
      placeHolder: 'Your answer goes back to the AI that asked',
      ignoreFocusOut: true,
    });
    if (text === undefined || text.trim().length === 0) {
      return; // dismissing is not answering; the question stays open
    }

    // Atomic: the server polls this directory, and half a file must never resolve a question.
    const dir = vscode.Uri.joinPath(this.dataDir, 'escalations');
    const target = vscode.Uri.joinPath(dir, `${escalation.id}.answer.json`);
    const temp = vscode.Uri.joinPath(dir, `${escalation.id}.answer.json.tmp`);
    const bytes = new TextEncoder().encode(answerJson(escalation.id, text.trim(), new Date().toISOString()));
    await vscode.workspace.fs.writeFile(temp, bytes);
    await vscode.workspace.fs.rename(temp, target, { overwrite: true });
    await this.refresh();
  }

  /** Every question whose answer file is not there yet. */
  private async readOpen(): Promise<Escalation[]> {
    const dir = vscode.Uri.joinPath(this.dataDir, 'escalations');
    const found: Escalation[] = [];
    try {
      const entries = await vscode.workspace.fs.readDirectory(dir);
      const names = entries
        .filter(([name, kind]) => kind === vscode.FileType.File && name.endsWith('.json') && !name.endsWith('.answer.json') && !name.endsWith('.tmp'))
        .map(([name]) => name);
      const answered = new Set(entries.map(([name]) => name).filter((n) => n.endsWith('.answer.json')));

      for (const name of names) {
        if (answered.has(name.replace(/\.json$/, '.answer.json'))) {
          continue;
        }
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(dir, name));
        const escalation = parseEscalation(new TextDecoder().decode(bytes));
        if (escalation !== undefined) {
          found.push(escalation);
        }
      }
    } catch {
      // No escalations directory yet — nothing has ever been asked.
    }
    return found;
  }
}
