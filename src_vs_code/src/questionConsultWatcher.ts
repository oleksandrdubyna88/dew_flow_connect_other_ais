import * as vscode from 'vscode';
import { type JsonDirectoryShape, jsonRecordFiles } from './jsonDirectory';
import { JsonDirectoryWatcher } from './jsonDirectoryWatcher';
import { type QuestionConsult, isKept, nextStaleAt, parseQuestionConsult, questionsSignature, shownAt } from './questionConsults';

/**
 * `<dataDir>/question-consults/*.json`, as **Active questions** reads it (todo/PLAN_question_consultant.md, S4): every
 * question still consulting, and those finished a short while ago so a card that follows one can fold its answers.
 *
 * <p>A record is kept as it is SHOWN (S4b item 9): a consulting question whose heartbeat has been silent for over two
 * minutes is interrupted — the server running it died or wedged, and its own sweep cannot run without it. The
 * recheck is what makes that true without an event: nothing writes the file of a dead server, so the watcher reads it
 * again at the first heartbeat deadline.</p>
 */
export const QUESTION_CONSULTS: JsonDirectoryShape<vscode.Uri, QuestionConsult> = {
  subdir: 'question-consults',
  // `answers/` is a subdirectory of vendor output files and `.tmp` a write in flight — neither is a record.
  records: jsonRecordFiles,
  parse: (text) => parseQuestionConsult(text),
  shown: shownAt,
  keep: isKept,
  fileOf: (one) => `${one.id}.json`,
  signature: questionsSignature,
};

/**
 * Watches the question-consult directory so **Active questions** shows a question while its consultants work — a
 * configuration of `JsonDirectoryWatcher` (S4b item 12). One surface: nothing is blocked while the consultants answer,
 * so nothing modal is raised. The card that blocks is the escalation watcher's.
 */
export class QuestionConsultWatcher extends JsonDirectoryWatcher<QuestionConsult> {
  constructor(dataDir: vscode.Uri) {
    super([dataDir], { shape: QUESTION_CONSULTS, recheckAt: (records) => nextStaleAt(records) });
  }

  /** The records the sidebar draws from: every question still consulting, and those finished a short while ago. */
  get questions(): readonly QuestionConsult[] {
    return this.items;
  }
}
