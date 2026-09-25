import { LogPeriod, periodOf } from './logPeriod';

/**
 * What a message from the rounds log page MEANS, decided without a host.
 *
 * <p>The same reasoning as [chatMessages.ts](chatMessages.ts), and the same shape: the only code
 * that turns a webview message into an action lived inside a class importing `vscode`, which no unit
 * test in this suite can construct. So the DECISION lives here and the panel keeps the wiring, which
 * is the half a test genuinely cannot reach.</p>
 *
 * <p>It matters more here than it looks, because one of these words is now load-bearing: `ready` is
 * what tells the panel the page can actually receive a push. A typo in that branch would ship with
 * every test green and leave the page exactly as empty as the defect this module was written
 * for.</p>
 *
 * <p><b>An unknown message is IGNORED</b>, for the reason `chatMessages.ts` sets out at length: a
 * retained webview can be older or newer than the extension talking to it, and refusing a word would
 * break the half that had done nothing wrong.</p>
 */

/** Anything the page might post. Every field optional, because the page is not to be trusted. */
export interface LogPageMessage {
  readonly type?: unknown;
  readonly command?: unknown;
  readonly id?: unknown;
  /** The model half of a CHAT row's identity — a vendor alone cannot name one of those rows. */
  readonly model?: unknown;
  /**
   * The three fields the rounds database keys a round by, carried BY the request.
   *
   * <p>They used to be looked up in a module-level copy of the last rows the extension built, which
   * is shared mutable state that answers wrongly for any row a later refresh dropped. Three
   * reviewers of the code round objected to it independently; the row already holds these, so it
   * sends them.</p>
   */
  readonly session?: unknown;
  readonly stage?: unknown;
  readonly number?: unknown;
  /**
   * The ROWS an export is about, sent by the page rather than looked up here.
   *
   * <p>The host's newest rows are the unfiltered set as of the last tick, and a row somebody
   * selected may already have left it — so looking a selection up in host-held state answers
   * wrongly for exactly the rows a person cared about. It is the same objection three reviewers
   * made about the findings request, and the same answer: the request carries what it is about.</p>
   */
  readonly rounds?: unknown;
}

export type LogCommand =
  | { readonly kind: 'ready' }
  | { readonly kind: 'answer'; readonly id: string }
  | { readonly kind: 'usageWindow'; readonly window: string }
  /** The period *What it keeps missing* is counted over — the server counts it, so the host is asked. */
  | { readonly kind: 'spotsPeriod'; readonly period: LogPeriod }
  | { readonly kind: 'forget'; readonly provider: string }
  /** Record how a consultation ended. The id is the consultation, not a round. */
  | { readonly kind: 'closeConsultation'; readonly id: string }
  /** One CHAT row, which is a vendor AND a model — one id cannot name the pair. */
  | { readonly kind: 'forgetChat'; readonly provider: string; readonly model: string }
  | {
    readonly kind: 'findings';
    readonly key: string;
    readonly sessionId: string;
    readonly stage: string;
    readonly number: number;
  }
  | { readonly kind: 'export'; readonly rows: readonly ExportedRow[] }
  | { readonly kind: 'ignore' };

/**
 * One row of an export request, believed only as far as its shape.
 *
 * <p>Deliberately NOT `LogRow`. The page is not trusted, and thirty fields validated one by one
 * would be a story of its own; what must be true here is that the thing is an object with a key —
 * everything downstream treats each value as untrusted anyway, because the one cell writer in
 * `roundsCsv` escapes whatever it is handed and renders what it cannot use as empty.</p>
 */
export interface ExportedRow {
  readonly key: string;
  readonly [field: string]: unknown;
}

const IGNORE: LogCommand = { kind: 'ignore' };

/** A period the page named, or nothing — a word the switch never offers is not a period. */
function spotsPeriodOf(id: string): LogCommand {
  const period = periodOf(id);

  return period === undefined ? IGNORE : { kind: 'spotsPeriod', period };
}

/** A string, or nothing at all — the page's values arrive over a bridge and are not typed there. */
function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * A findings request, believed only as far as it is complete.
 *
 * <p>A round with no session or no stage cannot be looked up, and asking the server for one would
 * spend a process to be told so. A message missing either is IGNORED, which is what this module does
 * with everything it cannot read.</p>
 */
function findingsOf(message: LogPageMessage, key: string): LogCommand {
  const sessionId = text(message.session);
  const stage = text(message.stage);
  const number = typeof message.number === 'number' && Number.isInteger(message.number)
    ? message.number
    : -1;

  return sessionId.length > 0 && stage.length > 0 && number >= 0
    ? { kind: 'findings', key, sessionId, stage, number }
    : IGNORE;
}

/**
 * An export request, believed only as far as it is usable.
 *
 * <p>A request naming no rows is IGNORED rather than answered: it would open a save dialog for an
 * empty file. Anything in the list that is not an object with a key is dropped, and a list that is
 * nothing but those is the same as an empty one.</p>
 */
function exportOf(message: LogPageMessage): LogCommand {
  const rows = Array.isArray(message.rounds) ? message.rounds : [];
  const usable = rows.filter((row): row is ExportedRow =>
    typeof row === 'object' && row !== null && typeof (row as { key?: unknown }).key === 'string'
    && (row as { key: string }).key.length > 0);

  return usable.length === 0 ? IGNORE : { kind: 'export', rows: usable };
}

/**
 * One message, read.
 *
 * <p>`ready` carries nothing: it is a page saying its listeners are attached, and the panel answers
 * it from its own newest state rather than from anything the page claims.</p>
 */
export function logCommandOf(message: LogPageMessage | undefined | null): LogCommand {
  if (typeof message !== 'object' || message === null || message === undefined) {
    return IGNORE;
  }
  if (message.type === 'ready') {
    return { kind: 'ready' };
  }
  if (message.type !== 'command') {
    return IGNORE;
  }

  const id = text(message.id);
  if (id.length === 0) {
    return IGNORE;
  }

  switch (message.command) {
    case 'answer':
      return { kind: 'answer', id };
    case 'usageWindow':
      return { kind: 'usageWindow', window: id };
    case 'spotsPeriod':
      return spotsPeriodOf(id);
    case 'closeConsultation':
      return { kind: 'closeConsultation', id };
    case 'forgetUsage':
      return { kind: 'forget', provider: id };
    case 'forgetChat':
      return { kind: 'forgetChat', provider: id, model: text(message.model) };
    case 'findings':
      return findingsOf(message, id);
    case 'export':
      return exportOf(message);
    default:
      return IGNORE;
  }
}
