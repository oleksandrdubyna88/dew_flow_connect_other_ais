import { escapeHtml } from './escapeHtml';
import type { Escalation, EscalationAdvice } from './escalations';
import { questionHtml } from './questionLayout';
import { CONSULTING, type QuestionConsult, type QuestionConsultRow } from './questionConsults';
import { shortDuration } from './usage';

/**
 * **Active questions** — the sidebar's first region (todo/PLAN_question_consultant.md, S4, §0 item 6): what an AI
 * is asking right now, in two stages.
 *
 * <p><b>Stage 1, consulting:</b> a question put to the consultants, one line per model with its status and how
 * long it has taken, advancing as each row settles (the server rewrites the record per row). <b>Stage 2, waiting
 * for you:</b> the person's card — the question, what still gates, a production risk with its reason, the
 * consultation it followed, every consultant's answer folded under it, and Answer. It REPLACES the old "A review
 * is waiting on you" card, and a question is drawn ONCE: a record bound to an open card is drawn inside the
 * card, never also as a line of its own. An expired card is not here at all — the watcher does not keep it.</p>
 *
 * <p>Never collapsible, like the region it replaced: a blocked question is the one thing on the panel that must
 * not be tidied away behind an arrow. Every word a model or a server wrote is escaped.</p>
 */
export function activeQuestionsBody(cards: readonly Escalation[], records: readonly QuestionConsult[], nowMs: number): string {
  const bound = new Set(cards.flatMap((card) => boundRecords(card, records).map((one) => one.id)));
  const consulting = records
    .filter((one) => one.status === CONSULTING && !bound.has(one.id))
    .sort((a, b) => b.startedUtc.localeCompare(a.startedUtc));
  if (cards.length === 0 && consulting.length === 0) {
    return '';
  }

  return [
    '<h2>Active questions</h2>',
    stage('Consulting', consulting.map((one) => consultingCard(one, nowMs))),
    stage('A question is waiting on you', cards.map((card) => waitingCard(card, records, nowMs))),
  ].filter((part) => part.length > 0).join('\n');
}

/** One stage under its heading, or nothing when it has nothing. */
function stage(heading: string, parts: readonly string[]): string {
  return parts.length === 0 ? '' : `<h3>${heading}</h3>\n${parts.join('\n')}`;
}

/** The records a card stands for: the consultation it followed (verified by the server), and one run beside it. */
function boundRecords(card: Escalation, records: readonly QuestionConsult[]): readonly QuestionConsult[] {
  return records.filter((one) => one.id === (card.consultId ?? '') || one.escalationId === card.id);
}

/** Stage 1: one question and one line per model. */
function consultingCard(record: QuestionConsult, nowMs: number): string {
  return `<div class="round qconsult" data-qconsult="${escapeHtml(record.id)}">
  <div class="subject">${escapeHtml(shortened(record.question))}</div>
  <div class="line branch">${escapeHtml(record.branch)}</div>
${record.rows.map((row) => `  <div class="line">${rowLine(row, record, nowMs)}</div>`).join('\n')}
${alertLine(record)}</div>`;
}

/** A model's line: its status chip, who it is, its prompt, the flag beside it, and how long. */
function rowLine(row: QuestionConsultRow, record: QuestionConsult, nowMs: number): string {
  return `${chip(row.status)} ${escapeHtml(who(row.vendor, row.model))} — ${escapeHtml(row.promptTitle)}${flagged(row.flag)} · ${escapeHtml(elapsed(row, record, nowMs))}`;
}

/** Running rows count from the question's start; a settled row says what it took. */
function elapsed(row: QuestionConsultRow, record: QuestionConsult, nowMs: number): string {
  if (row.status !== CONSULTING) {
    return shortDuration(row.seconds);
  }
  const since = Date.parse(record.startedUtc);

  return Number.isFinite(since) ? shortDuration(Math.max(0, (nowMs - since) / 1000)) : 'just started';
}

/** Stage 2: the card, with every answer folded under it. */
function waitingCard(card: Escalation, records: readonly QuestionConsult[], nowMs: number): string {
  return `<div class="question" data-question="${escapeHtml(card.id)}">
  <div class="said">${questionHtml(card.question)}</div>
${findings(card)}${riskLine(card)}  <div class="meta">${escapeHtml(card.branch)}${card.translationNote ? ` · shown untranslated: ${escapeHtml(card.translationNote)}` : ''}</div>
${followed(card)}${answersOf(card, records, nowMs)}
  <button data-command="answer" data-id="${escapeHtml(card.id)}">Answer…</button>
</div>`;
}

function findings(card: Escalation): string {
  return card.openFindings
    .map((f) => `  <div class="finding">• ${escapeHtml(f.severity)} ${escapeHtml(f.category)} — ${escapeHtml(f.title)}</div>\n`)
    .join('');
}

/** D8: the AI declared a production risk — the card says so, and why, before anything else of it. */
function riskLine(card: Escalation): string {
  return card.productionRisk === true
    ? `  <div class="hint stale" data-risk>Production risk — ${escapeHtml(card.riskReason ?? '')}</div>\n`
    : '';
}

/** The consultation the card followed — the id the AI passed, which the server verified (D14 a). */
function followed(card: Escalation): string {
  return (card.consultId ?? '').length === 0
    ? ''
    : `  <div class="meta" data-followed>Followed the consultation <code>${escapeHtml(card.consultId ?? '')}</code></div>\n`;
}

/**
 * The answers under a card: the ones the server folded into the card (a production risk, S3), or else the rows
 * of the consultation it followed. Each is a fold — the advice can be pages — with its chip and its flag outside.
 */
function answersOf(card: Escalation, records: readonly QuestionConsult[], nowMs: number): string {
  const folded = card.consultantAnswers ?? [];
  const answers: readonly EscalationAdvice[] = folded.length > 0 ? folded : rowsAsAdvice(boundRecords(card, records));
  if (answers.length === 0) {
    return '';
  }

  return `  <div class="hint">What the consultants answered:</div>\n${answers.map((one) => answerFold(one, nowMs)).join('\n')}\n`;
}

function rowsAsAdvice(records: readonly QuestionConsult[]): readonly EscalationAdvice[] {
  return records.flatMap((record) => record.rows.map((row) => ({
    rowId: row.rowId, vendor: row.vendor, model: row.model, promptTitle: row.promptTitle, capability: row.capability,
    flag: row.flag, status: row.status, reason: row.reason, advice: row.advice, note: row.note,
  })));
}

function answerFold(answer: EscalationAdvice, _nowMs: number): string {
  const said = answer.advice.length > 0 ? answer.advice : answer.reason;

  return `  <details class="qanswer" data-row="${escapeHtml(answer.rowId)}"><summary>${chip(answer.status)} ${escapeHtml(who(answer.vendor, answer.model))} — ${escapeHtml(answer.promptTitle)}${flagged(answer.flag)}</summary>
    <div class="advice">${escapeHtml(said.length > 0 ? said : 'nothing yet')}</div>${noteLine(answer.note ?? '')}
  </details>`;
}

/** What the row's record says beside its answer — a disk root nobody watched (S4b item 5) is read WITH the advice it qualifies. */
function noteLine(note: string): string {
  return note.length === 0 ? '' : `\n    <div class="hint" data-note>${escapeHtml(note)}</div>`;
}

/** D13: the flag beside every answer from a row that can read this machine. */
function flagged(flag: string): string {
  return flag.length === 0 ? '' : ` · <span class="flag" data-flag="${escapeHtml(flag)}">can read this machine (${escapeHtml(flag)})</span>`;
}

/** A row's status as a chip — consulting is the running colour; a row that did not answer is the stopped one. */
function chip(status: string): string {
  return `<span class="badge${CHIP_CLASS[status] ?? ''}">${escapeHtml(status.replace(/_/g, ' '))}</span>`;
}

const CHIP_CLASS: Readonly<Record<string, string>> = {
  consulting: ' running',
  timed_out: ' stopped',
  failed: ' stopped',
  refused: ' stopped',
  blocked: ' stopped',
};

function who(vendor: string, model: string): string {
  return model.length > 0 ? `${vendor} · ${model}` : vendor;
}

/** A sidebar line, not the whole question — the card holds the whole of it once the person is asked. */
function shortened(question: string): string {
  const flat = question.replace(/\s+/g, ' ').trim();

  return flat.length > 160 ? `${flat.slice(0, 157)}…` : flat;
}

function alertLine(record: QuestionConsult): string {
  return record.alert.length === 0 ? '' : `  <div class="hint stale">${escapeHtml(record.alert)}</div>\n`;
}
