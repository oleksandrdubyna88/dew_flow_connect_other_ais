import { stat, writeFile } from 'node:fs/promises';
import * as vscode from 'vscode';
import { renameWithRetry } from './atomicFile';
import { sortedJoin } from './codeUnitOrder';
import { answerChoices } from './escalationAnswer';
import { type AnswerFiles, type AnswerOutcome, type Picked, answerEscalation } from './escalationAnswering';
import {
  Escalation,
  isOpenEscalation,
  modalText,
  needsADecision,
  parseEscalation,
  shouldPrompt,
  statusBarText,
} from './escalations';
import { usableDirs, watchedDirs } from './escalationDirs';
import { type DirEntry, type JsonDirectoryShape } from './jsonDirectory';
import { JsonDirectoryWatcher } from './jsonDirectoryWatcher';
import { notify, notifyAndAsk } from './notify';
import { askPerson } from './personWait';

/**
 * The disk an answer is written to: ONE API family for the answer file's exists, write and rename — Node's `fs.promises`, never VS Code's.
 * VS Code's write and rename make missing parent folders, so an answer typed after the question's folder went away (a share unmounted) made
 * the folder again — a data directory recreated by a reply (research/PLAN_paths_per_side.md E5.7, D3). Node's fail with ENOENT, which
 * `answerEscalation` returns as a failure. The existence check is Node's `stat` too (it was VS Code's, mixing the two); a stat that throws is
 * "no answer", not a crash.
 */
const ANSWER_FILES: AnswerFiles = {
  exists: async (path) => {
    try {
      await stat(path);

      return true;
    } catch {
      return false;
    }
  },
  write: (path, bytes) => writeFile(path, bytes),
  rename: renameWithRetry,
};

/** The outcomes after which the card's state has moved, so the views read the directories again: it left (written) or someone else made it leave. */
const REFRESHING: ReadonlySet<AnswerOutcome['kind']> = new Set(['written', 'already-answered']);

/** An id that is not a name cannot become a path. It arrived in a file this window did not write, and answering it would mean writing wherever that file asked us to. */
function tellRefusedId(escalation: Escalation): void {
  void notify({
    as: 'error',
    class: 'refusal',
    source: 'escalationWatcher',
    code: 'question-id-is-not-a-filename',
    subject: escalation.id,
    title: `That question's id is not a name this window can write a file for: ${escalation.id}`,
    cure: 'The file was written by something else; this window will not write wherever it asks.',
  });
}

function tellAlreadyAnswered(escalation: Escalation): void {
  void notify({
    as: 'information',
    class: 'refusal',
    source: 'escalationWatcher',
    code: 'question-already-answered',
    subject: escalation.id,
    title: 'That question has already been answered — in another window, or by somebody else on this one.',
  });
}

/**
 * KEPT, not swallowed. A mount can go read-only or disappear between the question being found and the answer being typed, and an answer that
 * silently failed to land looks exactly like one that did — while the round it was for blocks. The question stays open so it can be tried
 * again. (codex and local, the plan round.)
 */
function tellNotWritten(escalation: Escalation, dir: string, reason: unknown): void {
  void notify({
    as: 'error',
    class: 'failure',
    source: 'escalationWatcher',
    code: 'answer-not-written',
    subject: escalation.id,
    title: `The answer could not be written to ${dir} — the question is still open. ${asText(reason)}`,
    cure: 'The question stays open, so it can be answered again once the folder is writable.',
    detail: asText(reason),
  });
}

/** What a person is told about an answer that did not simply land; the other outcomes say nothing. */
function tell(escalation: Escalation, outcome: AnswerOutcome): void {
  switch (outcome.kind) {
    case 'refused-id':
      tellRefusedId(escalation);
      break;
    case 'already-answered':
      tellAlreadyAnswered(escalation);
      break;
    case 'failed':
      tellNotWritten(escalation, outcome.dir, outcome.reason);
      break;
    default:
      break;
  }
}

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

/** The question files of a listing whose answer file is not there yet — an answered question is not open. */
function unanswered(entries: readonly DirEntry[]): readonly string[] {
  const answered = new Set(entries.map((entry) => entry.name).filter((name) => name.endsWith('.answer.json')));

  return entries
    .filter((entry) => entry.isFile && entry.name.endsWith('.json') && !entry.name.endsWith('.answer.json') && !entry.name.endsWith('.tmp'))
    .map((entry) => entry.name)
    .filter((name) => !answered.has(name.replace(/\.json$/, '.answer.json')));
}

/**
 * `<dir>/escalations/*.json` in every watched directory, as the cards are read: the unanswered questions, each TAGGED
 * with the directory it came from, so its answer goes back beside it rather than into this window's own store, where
 * the server that asked polls nowhere.
 *
 * <p>An EXPIRED question is not open: the server told the AI to ask in the chat when its wait ran out (S3 of the
 * question consultant, A10), and a card that stayed would be a question two people answer. The file stays for the log;
 * the sidebar does not draw it. The signature is the whole card, because the card shows nearly all of it — the
 * consultants' answers arrive under it as their rows settle.</p>
 */
export const ESCALATIONS: JsonDirectoryShape<vscode.Uri, Escalation> = {
  subdir: 'escalations',
  records: unanswered,
  // As before the watchers were one: an unreachable named directory shows no cards rather than its last ones.
  unreadable: 'none',
  parse: (text, root) => parseEscalation(text, root.fsPath),
  keep: (one) => isOpenEscalation(one),
  fileOf: (one) => `${one.id}.json`,
  signature: (cards) => sortedJoin(cards.map((one) => JSON.stringify(one)), '\n'),
};

/**
 * Watches the server's escalation directory and puts the question in front of a person.
 *
 * <p>Three surfaces, on purpose: a modal so it arrives, a status-bar item so a dismissed modal
 * does not lose it, and the rounds view so it can be read in full. A round is BLOCKED behind this
 * question — a notification that can be missed is the wrong shape for that.</p>
 *
 * <p>The reading is `JsonDirectoryWatcher`'s (S4b item 12) — a glob per watched directory, the 175 ms debounce, the
 * poll only for a UNC folder, the last good snapshot and the generation guard — over every directory this window
 * answers for: its own first, then the ones the setting names. <b>Each directory's failures and latency stay its
 * own</b>: a disconnected NAS, a WSL distribution that is shut down or a typo keeps that directory's last snapshot and
 * never stops this window's own questions being seen. (codex, the plan and code rounds.)</p>
 */
export class EscalationWatcher {
  private readonly prompted = new Set<string>();
  private readonly statusItem: vscode.StatusBarItem;
  private readonly disposables: vscode.Disposable[] = [];
  private readonly records: JsonDirectoryWatcher<Escalation>;

  /** Called after every refresh that changed what a person sees, so a view can repaint without polling on its own. */
  public onChanged: () => void = () => {};

  constructor(private readonly dataDir: vscode.Uri) {
    this.statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    this.statusItem.command = 'coai.showRounds';
    this.statusItem.tooltip = 'A ConnectOtherAIs review is waiting on your answer';
    this.records = new JsonDirectoryWatcher<Escalation>([], { shape: ESCALATIONS });
    this.records.onChanged = () => this.changed();
    this.disposables.push(this.statusItem, this.records);
  }

  /** Everything currently unanswered — the rounds view renders these. */
  get openQuestions(): readonly Escalation[] {
    return this.records.items;
  }

  start(): void {
    this.records.setRoots(this.roots());
    // The setting names other installations' directories, and a list read once at activation is a
    // setting that appears not to work until the window is reloaded. (gemini, the plan round.)
    this.disposables.push(vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration(ALSO_WATCH_SETTING)) {
        this.records.setRoots(this.roots());
      }
    }));
  }

  /**
   * Every directory whose questions this window answers — its own first, then what was named.
   *
   * <p>FILESYSTEM PATHS on both sides. The own directory used to go in as a URI string while the
   * setting's values are paths, so `dirKey` compared `file:///c%3A/...` with `C:\...` and the
   * own directory named again in the setting was watched twice. (gemini and codex, the code round.)</p>
   */
  private roots(): readonly vscode.Uri[] {
    return usableDirs(watchedDirs(this.dataDir.fsPath, alsoWatchDataDirectories(), process.platform)).map((dir) => vscode.Uri.file(dir));
  }

  dispose(): void {
    for (const d of this.disposables) {
      d.dispose();
    }
  }

  /** Reads every watched directory again — after an answer is written, so its card leaves at once. */
  async refresh(): Promise<void> {
    await this.records.refresh();
  }

  /** What a person sees moved: the status bar, the views, and a modal for a question nobody was prompted with yet. */
  private changed(): void {
    // Only the cards that ask for a decision are "questions": an informational one (kind `info`) is told, not asked, so it raises no modal and
    // does not make the status bar claim a review is waiting on an answer. It stays in the sidebar and the log until dismissed.
    const open = this.records.items.filter(needsADecision);
    const text = statusBarText(open.length);
    if (text.length === 0) {
      this.statusItem.hide();
    } else {
      this.statusItem.text = text;
      this.statusItem.show();
    }

    this.onChanged();

    for (const escalation of open) {
      if (shouldPrompt(escalation.id, this.prompted, false)) {
        this.prompted.add(escalation.id);
        void this.prompt(escalation);
      }
    }
  }

  /** The modal. Dismissing it is safe: the status bar and the rounds view still hold the question. */
  private async prompt(escalation: Escalation): Promise<void> {
    const answer = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'escalationWatcher',
      code: 'a-review-is-waiting',
      subject: escalation.id,
      modal: true,
      title: modalText(escalation),
      action: 'Answer…',
      branch: escalation.branch,
      repo: escalation.repoPath,
    });
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
    // BESIDE THE QUESTION, not in this window's own directory. A question can come from another
    // installation — a Claude Code session inside WSL writing into the WSL store — and the server
    // that asked polls the directory it wrote in and nowhere else. An answer written here would
    // leave that round blocked for ever, having been answered. The decision of who is asked, what is
    // written and where is `answerEscalation` (pure, with the dialog and the disk injected — and tested,
    // including an informational card's dismissal); this method is the editor and the notifications.
    const outcome = await answerEscalation(escalation, {
      dataDir: this.dataDir.fsPath,
      files: ANSWER_FILES,
      ask: (card) => this.ask(card),
      now: () => new Date().toISOString(),
    });
    await this.report(escalation, outcome);
  }

  /** What an answer's outcome means to the person: a notification for each way it did not land, and a refresh for the ways the card's state moved. */
  private async report(escalation: Escalation, outcome: AnswerOutcome): Promise<void> {
    tell(escalation, outcome);
    if (REFRESHING.has(outcome.kind)) {
      await this.refresh();
    }
  }

  /**
   * The dialog for a card that asks for a decision: the choices (and, for a typed answer, the box), or `undefined` when the person closed it.
   * The question's branch says which review is asking: a feature session's first choice is also the person's request for that review's second
   * round, and its detail says so. Never reached for an informational card (`chooseAnswer`).
   */
  private async ask(escalation: Escalation): Promise<Picked | undefined> {
    const choices = answerChoices(escalation);
    const picked = await askPerson(() => vscode.window.showQuickPick(
      choices.map((c) => ({ label: c.label, detail: c.detail, choice: c })),
      {
        title: `ConnectOtherAIs — ${escalation.branch}`,
        placeHolder: escalation.question,
        ignoreFocusOut: true,
        matchOnDetail: true,
      },
    ));
    if (picked === undefined) {
      return undefined;
    }

    return picked.choice.decision === ''
      ? this.typedAnswer(escalation)
      : { decision: picked.choice.decision, text: picked.choice.label };
  }

  /** The box for the person's own words — `undefined` when it was closed or left blank. */
  private async typedAnswer(escalation: Escalation): Promise<Picked | undefined> {
    const typed = (await askPerson(() => vscode.window.showInputBox({
      title: `ConnectOtherAIs — ${escalation.branch}`,
      prompt: escalation.question,
      placeHolder: 'Your answer goes back to the AI that asked',
      ignoreFocusOut: true,
    })))?.trim() ?? '';

    return typed.length === 0 ? undefined : { decision: '', text: typed };
  }
}
