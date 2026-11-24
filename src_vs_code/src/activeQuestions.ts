import { escapeHtml } from './escapeHtml';
import { CONSULTING, INTERRUPTED, type QuestionConsult, type QuestionConsultRow } from './questionConsults';
import { shortDuration } from './usage';

/**
 * **Active questions** — the sidebar's first region (todo/PLAN_question_consultant.md, S4, §0 item 6): what the consultants are answering right now,
 * one line per model with its status and how long it has taken, advancing as each row settles (the server rewrites the record per row).
 *
 * <p>v2 (E4, story s6-dead-v1-out): the second stage — the person's card with its Answer button — is gone with the held gate it answered; the server
 * has no held gates and no cards. What stays is the consulting stage and the interrupted one (a question whose server stopped answering is terminal here
 * rather than spinning for ever). Nothing feeds this region until the sidebar polls for it again, so it reads as empty.</p>
 *
 * <p>Never collapsible. Every word a model or a server wrote is escaped.</p>
 */
export function activeQuestionsBody(records: readonly QuestionConsult[], nowMs: number): string {
  const withStatus = (status: string): readonly QuestionConsult[] => records
    .filter((one) => one.status === status)
    .sort((a, b) => b.startedUtc.localeCompare(a.startedUtc));
  const consulting = withStatus(CONSULTING);
  const interrupted = withStatus(INTERRUPTED);
  if (consulting.length === 0 && interrupted.length === 0) {
    return '';
  }

  return [
    '<h2>Active questions</h2>',
    stage('Consulting', consulting.map((one) => consultingCard(one, nowMs))),
    stage('Interrupted — its server stopped answering', interrupted.map((one) => consultingCard(one, nowMs))),
  ].filter((part) => part.length > 0).join('\n');
}

/** One stage under its heading, or nothing when it has nothing. */
function stage(heading: string, parts: readonly string[]): string {
  return parts.length === 0 ? '' : `<h3>${heading}</h3>\n${parts.join('\n')}`;
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
  interrupted: ' stopped',
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
