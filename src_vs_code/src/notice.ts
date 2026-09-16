import { NotificationClass, NotificationRecord } from './notifications';

/**
 * What a caller says about one message, and what that becomes on disk.
 *
 * <p>The pure half of the funnel. `notify.ts` is the thin host that touches `vscode` and the disk;
 * everything decided here is decided without either, which is the split `chatDoors.ts` /
 * `chatDoorsFile.ts` already uses and the only shape this suite can test at all — a module that
 * imports `vscode` cannot be imported by a test in this repository.</p>
 */

/**
 * Which `window.show*Message` a notice uses.
 *
 * <p>Stated by the CALLER rather than derived from the class, and that is deliberate. Deriving it
 * would quietly promote about a dozen warnings to errors the day the funnel landed, and the plan
 * promises the opposite: *recording is additive — no message stops being a toast, and none is
 * added*. What changes is that the message is written down; how it looks does not change at
 * all.</p>
 */
export type NoticeSurface = 'information' | 'warning' | 'error';

/** One message, as the place that raises it describes it. */
export interface Notice {
  readonly as: NoticeSurface;
  readonly class: NotificationClass;
  /** The module raising it — `serverSettingsSync`, `rolesPanel`. Grouped on, so keep it stable. */
  readonly source: string;
  /**
   * The key. A STRING LITERAL at the call site, never composed — a scan enforces it.
   *
   * <p>Keying on the rendered title was the plan's second draft, and it would have missed the
   * incident the whole feature exists for: a title carrying a round number mints a fresh key every
   * round.</p>
   */
  readonly code: string;
  /** Which resource this one is about, when a `code` can be about more than one. */
  readonly subject?: string;
  /** What the person sees. */
  readonly title: string;
  /** What it says underneath — an exception's text, a server's answer, the long half. */
  readonly detail?: string;
  /** What to do about it, in this product's own words. */
  readonly cure?: string;
  /** The button, when there is one. Only `notifyAndAsk` waits for it. */
  readonly action?: string;
  /**
   * Whether this is a modal question rather than a toast.
   *
   * <p>A modal is not an event — it is a question, and what gets recorded is the ANSWER as well as
   * the asking. Sixteen of the call sites are these.</p>
   */
  readonly modal?: boolean;
  readonly repo?: string;
  readonly branch?: string;
  readonly session?: string;
  readonly provider?: string;
  readonly role?: string;
}

/** Only the optional fields that were given, so a record carries no empty strings. */
function given(notice: Notice): Partial<NotificationRecord> {
  const fields: Array<keyof Notice & keyof NotificationRecord> = [
    'subject', 'title', 'detail', 'cure', 'action', 'repo', 'branch', 'session', 'provider', 'role',
  ];
  const kept: Record<string, string> = {};
  for (const field of fields) {
    const value = notice[field];
    if (typeof value === 'string' && value.length > 0) {
      kept[field] = value;
    }
  }

  return kept as Partial<NotificationRecord>;
}

/**
 * One notice as the record that goes on disk.
 *
 * <p>The clock, the run and the pid are PARAMETERS. The clock because `utc-timestamps.md` asks for
 * one a test can control and because the host that calls this imports `vscode`, so a test can only
 * reach the record through here; the run because it is minted once per host start and a pure
 * function may not mint anything; the pid for the same reason.</p>
 */
export function noticeRecord(notice: Notice, run: string, pid: number, at: Date): NotificationRecord {
  return {
    utc: at.toISOString(),
    class: notice.class,
    source: notice.source,
    code: notice.code,
    run,
    pid,
    ...given(notice),
  };
}

/** The same record, with what the person pressed — an answer is the event a question produces. */
export function answered(record: NotificationRecord, answer: string | undefined): NotificationRecord {
  return { ...record, answer: answer ?? 'dismissed' };
}

/**
 * What the panel and the page say while writes are failing.
 *
 * <p>Not a notification — a notification about a notification that could not be written is an
 * infinite regress with a disk error at the bottom of it. A STATE instead, which is the difference
 * between "the ledger is honest about its own gap" and "the ledger quietly loses the records from
 * exactly the period the machine was in trouble". The plan's own thesis, applied to the plan.</p>
 */
export function gapSentence(lost: number, sinceLocal: string): string {
  if (lost === 0) {
    return '';
  }
  const records = lost === 1 ? '1 record' : `${lost} records`;

  return `${records} could not be written since ${sinceLocal}`;
}
