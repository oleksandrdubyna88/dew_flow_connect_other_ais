import { escapeHtml } from './escapeHtml';
import { shortDuration } from './usage';

/**
 * The Logs page's **Questions** tab (todo/PLAN_question_consultant.md, S4, §0 item 5): one row per question an AI
 * put to the question consultant, newest first — when, where, how many models answered, what it cost, how long it
 * took and what happened next — opening to one row per model with its prompt, its status, its flag and its advice.
 *
 * <p>Read from `--log`'s `questionConsults` (the server's `question_consults` and `question_consult_rows`, S2's
 * projection), parsed here and not in `roundsDb.ts`. A server older than the field sends none, which is an EMPTY
 * tab — never an error: the two halves of this product update separately. Pure and `vscode`-free; every word a
 * model or the server wrote is escaped.</p>
 */

/** One model row of a question, as `--log` carries it (the server's `QuestionConsultRowEntry`). */
export interface DbQuestionAnswer {
  readonly rowId: string;
  readonly vendor: string;
  readonly model: string;
  readonly promptTitle: string;
  readonly capability: string;
  readonly flag: string;
  readonly status: string;
  readonly reason: string;
  readonly seconds: number;
  readonly costUsd: number | null;
  readonly advice: string;
  /** The row's note — a disk root the invariant could not watch (S4b item 5). */
  readonly note: string;
}

/** One question (the server's `QuestionConsultRow`), with its model rows. */
export interface DbQuestion {
  readonly id: string;
  readonly callerKind: string;
  readonly repoPath: string;
  readonly branch: string;
  readonly question: string;
  readonly context: string;
  readonly productionRisk: boolean;
  readonly riskReason: string;
  readonly status: string;
  readonly outcome: string;
  readonly escalationId: string;
  readonly startedUtc: string;
  readonly rows: number;
  readonly answered: number;
  readonly seconds: number;
  readonly costUsd: number | null;
  readonly alert: string;
  readonly answers: readonly DbQuestionAnswer[];
}

/** The questions of a `--log` answer — `questionConsults`, read loosely; absent or malformed is none. */
export function questionsOf(raw: unknown): readonly DbQuestion[] {
  return (Array.isArray(raw) ? raw : []).flatMap(questionFrom);
}

type Fields = Record<string, unknown>;

function fieldsOf(value: unknown): Fields {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Fields : {};
}

function questionFrom(value: unknown): readonly DbQuestion[] {
  const one = fieldsOf(value);
  const asked = fieldsOf(one['asked']);

  return text(asked['id']).length === 0 ? [] : [{
    id: text(asked['id']),
    callerKind: text(asked['callerKind']),
    repoPath: text(asked['repoPath']),
    branch: text(asked['branch']),
    question: text(asked['question']),
    context: text(asked['context']),
    productionRisk: asked['productionRisk'] === true,
    riskReason: text(asked['riskReason']),
    status: text(asked['status']),
    outcome: text(asked['outcome']),
    escalationId: text(asked['escalationId']),
    startedUtc: text(asked['startedUtc']),
    rows: whole(asked['rows']),
    answered: whole(asked['answered']),
    seconds: whole(asked['seconds']),
    costUsd: price(asked['costUsd']),
    alert: text(asked['alert']),
    answers: (Array.isArray(one['answers']) ? one['answers'] : []).map(answerFrom),
  }];
}

function answerFrom(value: unknown): DbQuestionAnswer {
  const row = fieldsOf(value);

  return {
    rowId: text(row['rowId']),
    vendor: text(row['vendor']),
    model: text(row['model']),
    promptTitle: text(row['promptTitle']),
    capability: text(row['capability']),
    flag: text(row['flag']),
    status: text(row['status']),
    reason: text(row['reason']),
    seconds: whole(row['seconds']),
    costUsd: price(row['costUsd']),
    advice: text(row['advice']),
    note: text(row['note']),
  };
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function whole(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

/** A price, or `null` — absent and not-a-number alike: an unpriced question is a dash, never a zero that reads as free. */
function price(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** What happened next, in a sentence a person reads — the server's `QuestionOutcomes`, and the status before one. */
const OUTCOMES: Readonly<Record<string, string>> = {
  answered_by_consultants: 'answered by the consultants',
  person_asked: 'then asked the person',
  production_risk: 'production risk — the person was asked at once',
  // research/PLAN_ask_human_is_for_the_gate.md: the AI's own questions are asked in its chat, not on a card.
  person_asked_in_conversation: 'then sent back to ask the person in the AI’s chat',
  production_risk_consulted_first: 'production risk — the consultants first, then sent back to ask in the AI’s chat',
  quota_spent: 'the session’s questions were spent',
  none_available: 'nobody to ask',
};

function outcomeOf(question: DbQuestion): string {
  return OUTCOMES[question.outcome] ?? (question.status.length > 0 ? question.status : '—');
}

/** The tab's body: the table, or the sentence saying why there is none. */
export function qconsultLogHtml(questions: readonly DbQuestion[]): string {
  if (questions.length === 0) {
    return '<div class="empty">No question has been put to the consultants yet. One is, when an AI calls <code>ask_consultants</code> before it asks you — '
      + 'the <b>Question consultant</b> tab of ConnectOtherAIs Settings says who answers.</div>';
  }

  return `<table class="qconsults"><thead><tr>
    <th>Started</th><th>Where</th><th>Question</th><th class="num">Answered</th><th class="num">Cost</th><th class="num">Time</th><th>What happened next</th>
  </tr></thead><tbody>${questions.map(questionRows).join('')}</tbody></table>`;
}

/** One question's row, and the row under it that opens to its models. */
function questionRows(question: DbQuestion): string {
  const id = escapeHtml(question.id);

  return `<tr data-question="${id}">
    <td>${escapeHtml(question.startedUtc.slice(0, 16).replace('T', ' '))}</td>
    <td>${escapeHtml(where(question))}</td>
    <td class="what">${folded(question.question, `${id}:question`)}${risk(question)}</td>
    <td class="num">${question.answered} of ${question.rows}</td>
    <td class="num">${escapeHtml(money(question.costUsd))}</td>
    <td class="num">${escapeHtml(shortDuration(question.seconds))}</td>
    <td>${escapeHtml(outcomeOf(question))}</td>
  </tr>
  <tr data-models="${id}"><td colspan="7"><details data-fold="${id}:models"><summary>${question.answers.length} model${question.answers.length === 1 ? '' : 's'}</summary>
    <table class="qmodels"><tbody>${question.answers.map(modelRow).join('')}</tbody></table>
  </details>${question.alert.length === 0 ? '' : `<div class="failed">${escapeHtml(question.alert)}</div>`}</td></tr>`;
}

/** One model of a question: who, which prompt, how it ended, the flag, its time and cost, and what it said. */
function modelRow(answer: DbQuestionAnswer): string {
  const said = answer.advice.length > 0 ? answer.advice : answer.reason;

  return `<tr data-row="${escapeHtml(answer.rowId)}">
      <td>${escapeHtml(answer.model.length > 0 ? `${answer.vendor} · ${answer.model}` : answer.vendor)}</td>
      <td>${escapeHtml(answer.promptTitle)} (${escapeHtml(answer.capability)})</td>
      <td>${escapeHtml(answer.status.replace(/_/g, ' '))}${answer.flag.length === 0 ? '' : ` <span class="flag" data-flag="${escapeHtml(answer.flag)}">can read this machine (${escapeHtml(answer.flag)})</span>`}</td>
      <td class="num">${escapeHtml(shortDuration(answer.seconds))}</td>
      <td class="num">${escapeHtml(money(answer.costUsd))}</td>
      <td class="what">${folded(said, `${escapeHtml(answer.rowId)}:advice`)}${noteOf(answer.note)}</td>
    </tr>`;
}

/** What the row's record says beside its answer — a disk root nobody watched (S4b item 5) is read WITH the advice it qualifies. */
function noteOf(note: string): string {
  return note.length === 0 ? '' : `<div class="hint" data-note>${escapeHtml(note)}</div>`;
}

function where(question: DbQuestion): string {
  const repo = question.repoPath.split(/[\\/]/).filter((part) => part.length > 0).at(-1) ?? question.repoPath;

  return question.branch.length > 0 ? `${repo} · ${question.branch}` : repo;
}

function risk(question: DbQuestion): string {
  return question.productionRisk ? `<div class="hint">Production risk — ${escapeHtml(question.riskReason)}</div>` : '';
}

function money(cost: number | null): string {
  return cost === null ? '—' : `$${cost.toFixed(cost < 0.01 ? 4 : 2)}`;
}

/** A long text folded: its first line as the summary, the whole of it on a press — the Consultations tab's shape. */
function folded(said: string, key: string): string {
  const first = said.split('\n')[0] ?? '';
  const summary = first.length > 90 ? `${first.slice(0, 87)}…` : first;

  return said.length === 0 ? '—' : `<details data-fold="${key}"><summary>${escapeHtml(summary)}</summary><div class="said">${escapeHtml(said)}</div></details>`;
}
