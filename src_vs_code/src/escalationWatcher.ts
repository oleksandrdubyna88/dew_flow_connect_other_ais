import * as vscode from 'vscode';
import { answerJson, decisionChoices } from './escalationAnswer';
import {
  Escalation,
  modalText,
  parseEscalation,
  shouldPrompt,
  statusBarText,
} from './escalations';
import { WatchedDir, answerPaths, usableDirs, watchedDirs } from './escalationDirs';

/** The setting that names other installations' data directories. */
export const ALSO_WATCH_SETTING = 'coai.alsoWatchDataDirectories';

/**
 * The directories named in the setting, as typed.
 *
 * <p>Trimmed and emptied here and normalised in `escalationDirs.ts`, which is where the rules a test
 * can reach live. A value that is not a list of strings is no list at all rather than a guess.</p>
 */
export function alsoWatchDataDirectories(): readonly string[] {
  const value: unknown = vscode.workspace.getConfiguration().get(ALSO_WATCH_SETTING);

  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : [];
}

/** A thrown thing, as a sentence — the same shape `cliChatLaunch.ts` uses. */
function asText(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

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

  /**
   * Every directory whose questions this window answers — its own first, then what was named.
   *
   * <p>Rebuilt whenever the setting changes, because a list read once at activation is a setting that
   * appears not to work until the window is reloaded. (gemini, the plan round.)</p>
   */
  private watchedRoots: readonly vscode.Uri[] = [];
  /** What was asked for and what came of it, for the panel to render. */
  private asked: readonly WatchedDir[] = [];
  /** The per-directory file watchers, torn down and rebuilt when the setting moves. */
  private readonly watchers: vscode.Disposable[] = [];

  constructor(private readonly dataDir: vscode.Uri) {
    this.statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    this.statusItem.command = 'coai.showRounds';
    this.statusItem.tooltip = 'A ConnectOtherAIs review is waiting on your answer';
    this.disposables.push(this.statusItem);
  }

  /** What the panel names: each directory asked for, and the reason one cannot be watched. */
  get watchedDirectories(): readonly WatchedDir[] {
    return this.asked;
  }

  /** Everything currently unanswered — the rounds view renders these. */
  get openQuestions(): readonly Escalation[] {
    return this.open;
  }

  start(): void {
    this.rebuild();
    // The setting names other installations' directories, and a list read once at activation is a
    // setting that appears not to work until the window is reloaded.
    this.disposables.push(vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration(ALSO_WATCH_SETTING)) {
        this.rebuild();
        void this.refresh();
      }
    }));

    // A watcher on a path outside the workspace is not guaranteed on every platform; the poll is
    // what makes the promise "you will see the question" true rather than likely. It is also the
    // whole mechanism on a \\wsl.localhost or a network path, where events are not delivered at all.
    const timer = setInterval(() => void this.refresh(), 5000);
    this.disposables.push(new vscode.Disposable(() => clearInterval(timer)));
    void this.refresh();
  }

  /** Read the setting, work out the list, and put a watcher on each directory that can have one. */
  private rebuild(): void {
    for (const watcher of this.watchers.splice(0)) {
      watcher.dispose();
    }
    // FILESYSTEM PATHS on both sides. The own directory used to go in as a URI string while the
    // setting's values are paths, so `dirKey` compared `file:///c%3A/...` with `C:\...` and the
    // own directory named again in the setting was watched twice. (gemini and codex, the code round.)
    this.asked = watchedDirs(this.dataDir.fsPath, alsoWatchDataDirectories(), process.platform);
    this.watchedRoots = usableDirs(this.asked).map((dir) => vscode.Uri.file(dir));

    for (const root of this.watchedRoots) {
      // Each in its own try: a path that cannot be watched — a distribution that is not running, a
      // share that is not mounted — must not stop the others being watched, this window's own above
      // all. The poll still reaches it if it comes back.
      try {
        const watcher = vscode.workspace.createFileSystemWatcher(
          new vscode.RelativePattern(root, 'escalations/*.json'),
        );
        this.watchers.push(
          watcher,
          watcher.onDidCreate(() => void this.refresh()),
          watcher.onDidChange(() => void this.refresh()),
          watcher.onDidDelete(() => void this.refresh()),
        );
      } catch {
        // Watched by the poll alone from here. Nothing is lost but the immediacy.
      }
    }
    // NOT a disposable pushed per rebuild: `rebuild` runs on every change to the setting, and each
    // run would add another teardown to a list nothing empties — a leak that grows with how often
    // somebody edits their settings. `dispose()` drains `this.watchers` directly instead. (gemini,
    // the code round, Blocking.)
  }

  dispose(): void {
    for (const watcher of this.watchers.splice(0)) {
      watcher.dispose();
    }
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

  /**
   * Puts the decision in front of the person and writes what they chose.
   *
   * <p>Choices rather than a blank box: "the rounds ran out, now what" has three answers, and a
   * free-text field for it invites a sentence no code can act on — which is exactly what happened,
   * silently, because nothing read the file either. The escape hatch is still there as the third
   * item, for a person who wants to say something the buttons do not cover.</p>
   */
  async answerCommand(escalation: Escalation): Promise<void> {
    const choices = decisionChoices();
    const picked = await vscode.window.showQuickPick(
      choices.map((c) => ({ label: c.label, detail: c.detail, choice: c })),
      {
        title: `ConnectOtherAIs — ${escalation.branch}`,
        placeHolder: escalation.question,
        ignoreFocusOut: true,
        matchOnDetail: true,
      },
    );
    if (picked === undefined) {
      return; // dismissing is not answering; the question stays open
    }

    let decision = picked.choice.decision;
    let text = picked.choice.label;
    if (decision === '') {
      const typed = await vscode.window.showInputBox({
        title: `ConnectOtherAIs — ${escalation.branch}`,
        prompt: escalation.question,
        placeHolder: 'Your answer goes back to the AI that asked',
        ignoreFocusOut: true,
      });
      if (typed === undefined || typed.trim().length === 0) {
        return;
      }

      text = typed.trim();
    }

    // BESIDE THE QUESTION, not in this window's own directory. A question can come from another
    // installation — a Claude Code session inside WSL writing into the WSL store — and the server
    // that asked polls the directory it wrote in and nowhere else. An answer written here would
    // leave that round blocked for ever, having been answered.
    // Atomic: the server polls this directory, and half a file must never resolve a question. WHERE
    // those two paths are is `answerPaths`, which is pure and tested — the directory comes from the
    // question, and the temp is beside the target because a rename across filesystems throws EXDEV.
    // Both were comments here until the plan round pointed out that a comment is not a guarantee.
    const paths = answerPaths(escalation.id, this.dataDir.fsPath, escalation.from);
    if (paths === undefined) {
      // An id that is not a name cannot become a path. It arrived in a file this window did not
      // write, and answering it would mean writing wherever that file asked us to.
      void vscode.window.showErrorMessage(
        `That question's id is not a name this window can write a file for: ${escalation.id}`,
      );

      return;
    }
    const dir = vscode.Uri.file(paths.dir);
    const target = vscode.Uri.file(paths.target);
    const temp = vscode.Uri.file(paths.temp);
    const bytes = new TextEncoder().encode(
      answerJson(escalation.id, text.trim(), new Date().toISOString(), decision),
    );
    try {
      // FIRST WRITER WINS. Two windows can be told to watch each other, and then both show the same
      // modal; the second one to be answered would otherwise overwrite a decision already given and
      // acted on. Checked here, immediately before the write, rather than at discovery — the gap
      // between the two is exactly where the other window answers. (codex and local, the plan round.)
      const already = await this.answered(target);
      if (already) {
        void vscode.window.showInformationMessage(
          'That question has already been answered — in another window, or by somebody else on this one.',
        );
        await this.refresh();

        return;
      }
      await vscode.workspace.fs.writeFile(temp, bytes);
      await vscode.workspace.fs.rename(temp, target, { overwrite: true });
    } catch (reason) {
      // KEPT, not swallowed. A mount can go read-only or disappear between the question being found
      // and the answer being typed, and an answer that silently failed to land looks exactly like one
      // that did — while the round it was for blocks. The question stays open so it can be tried
      // again. (codex and local, the plan round.)
      void vscode.window.showErrorMessage(
        `The answer could not be written to ${dir.fsPath} — the question is still open. ${asText(reason)}`,
      );

      return;
    }
    await this.refresh();
  }

  /**
   * Every question whose answer file is not there yet, from every directory being watched.
   *
   * <p><b>Each directory is read on its own and its failures stay inside it.</b> A disconnected NAS,
   * a WSL distribution that is shut down, a path with a typo: any of them throws on read, and one
   * unhandled throw would stop the questions from THIS window's own directory being seen — turning a
   * setting that adds a directory into a setting that silences the feature. (codex, the plan round.)
   * </p>
   */
  private async readOpen(): Promise<Escalation[]> {
    // IN PARALLEL, and settled rather than awaited in turn. A disconnected share does not fail fast:
    // it hangs until the operating system gives up, and read one after another that hang is this
    // window's OWN questions waiting behind somebody else's unplugged NAS — on a five-second poll,
    // for ever. Each directory's failures were already its own; this makes its latency its own too.
    // (codex, the code round.)
    const each = await Promise.allSettled(this.watchedRoots.map((root) => this.readOpenIn(root)));

    return each.flatMap((one) => (one.status === 'fulfilled' ? one.value : []));
  }

  /** Whether an answer is already sitting there. A read that throws is "no answer", not a crash. */
  private async answered(target: vscode.Uri): Promise<boolean> {
    try {
      await vscode.workspace.fs.stat(target);

      return true;
    } catch {
      return false;
    }
  }

  /** The questions in ONE directory, tagged with where they came from. */
  private async readOpenIn(root: vscode.Uri): Promise<Escalation[]> {
    const dir = vscode.Uri.joinPath(root, 'escalations');
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
        // TAGGED with the directory it came from, so its answer goes back beside it rather than into
        // this window's own store, where the server that asked polls nowhere.
        const escalation = parseEscalation(new TextDecoder().decode(bytes), root.fsPath);
        if (escalation !== undefined) {
          found.push(escalation);
        }
      }
    } catch {
      // No escalations directory yet — nothing has ever been asked — or a directory somebody named
      // that is not reachable from here. Neither is this window's problem to throw about; the panel
      // is where an unreadable directory is reported.
    }
    return found;
  }
}
