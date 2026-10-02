import * as vscode from 'vscode';
import { POLL_MS, WATCH_DEBOUNCE_MS, debounced, needsPoll } from './debounced';
import { type QuestionConsult, isKept, parseQuestionConsult, questionsSignature } from './questionConsults';

/** An absent folder is no questions; any other failure keeps the last snapshot (`undefined`). */
function emptyIfAbsent(error: unknown): QuestionConsult[] | undefined {
  return error instanceof vscode.FileSystemError && error.code === 'FileNotFound' ? [] : undefined;
}

/** A record file: a `.json` file, not a write in flight (`.tmp.json`) and not the `answers/` subdirectory. */
function isRecordFile([name, kind]: [string, vscode.FileType]): boolean {
  return kind === vscode.FileType.File && name.endsWith('.json') && !name.endsWith('.tmp.json');
}

/**
 * Watches `<dataDir>/question-consults/` so **Active questions** shows a question while its consultants work
 * (todo/PLAN_question_consultant.md, S4) — the `ConsultationWatcher` shape: a glob over the data directory,
 * because the folder may not exist until the first question; every file event gathered into one refresh within
 * 175 ms (A5), because the server rewrites the record as each row settles; and the five-second poll only for a
 * `\\wsl.localhost` or network data folder, where events are not delivered.
 *
 * <p>One surface: nothing is blocked while the consultants answer, so nothing modal is raised. The card that
 * blocks is the escalation watcher's.</p>
 */
export class QuestionConsultWatcher {
  private readonly disposables: vscode.Disposable[] = [];
  private kept: QuestionConsult[] = [];

  /** Called after a refresh that changed what a person would see. */
  public onChanged: () => void = () => {};

  constructor(private readonly dataDir: vscode.Uri) {}

  /** The records the sidebar draws from: every question still consulting, and those finished a short while ago. */
  get questions(): readonly QuestionConsult[] {
    return this.kept;
  }

  start(): void {
    const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(this.dataDir, 'question-consults/*.json'));
    const changed = debounced(() => void this.refresh(), WATCH_DEBOUNCE_MS);
    this.disposables.push(
      watcher,
      watcher.onDidCreate(changed),
      watcher.onDidChange(changed),
      watcher.onDidDelete(changed),
      new vscode.Disposable(() => changed.cancel()),
    );
    if (needsPoll([this.dataDir.fsPath])) {
      const timer = setInterval(() => void this.refresh(), POLL_MS);
      this.disposables.push(new vscode.Disposable(() => clearInterval(timer)));
    }
    void this.refresh();
  }

  dispose(): void {
    for (const one of this.disposables.splice(0)) {
      one.dispose();
    }
  }

  /** Re-reads the directory, and tells the view only when something a person would SEE changed. */
  async refresh(): Promise<void> {
    const read = await this.read();
    if (read === undefined) {
      return; // a read that threw keeps the last good snapshot — see `read`
    }
    const before = questionsSignature(this.kept);
    this.kept = read;
    if (questionsSignature(read) !== before) {
      this.onChanged();
    }
  }

  /**
   * The kept records, or NOTHING when the directory could not be read: absent is the ordinary state (nobody has
   * asked yet) and reads as none; any other failure keeps what was there, so a rename landing on an open handle
   * does not blank a question somebody is watching.
   */
  private async read(): Promise<QuestionConsult[] | undefined> {
    const dir = vscode.Uri.joinPath(this.dataDir, 'question-consults');
    let entries: [string, vscode.FileType][];
    try {
      entries = await vscode.workspace.fs.readDirectory(dir);
    } catch (error) {
      return emptyIfAbsent(error);
    }
    const now = Date.now();
    const found: QuestionConsult[] = [];
    // `answers/` is a subdirectory of vendor output files and `.tmp` a write in flight — neither is a record.
    for (const [name] of entries.filter(isRecordFile)) {
      found.push(...await this.one(vscode.Uri.joinPath(dir, name), name, now));
    }

    return found;
  }

  /** One file: its record if it is kept, the last snapshot's copy if it could not be read this pass. */
  private async one(file: vscode.Uri, name: string, now: number): Promise<readonly QuestionConsult[]> {
    try {
      const record = parseQuestionConsult(new TextDecoder().decode(await vscode.workspace.fs.readFile(file)));

      return record !== undefined && isKept(record, now) ? [record] : [];
    } catch {
      return this.kept.filter((one) => `${one.id}.json` === name);
    }
  }
}
