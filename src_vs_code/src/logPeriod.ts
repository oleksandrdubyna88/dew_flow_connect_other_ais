import { DAYS_OF, startOfPeriodMs, Window, WINDOWS } from './usage';
import { escapeHtml } from './webviewHtml';

/**
 * The period switch on the Review rounds page's other tabs — Conversations, Consultations and What it
 * keeps missing (operator, 2026-09-25: "нужно добавить аналогичный переключатель на конверсейшин,
 * консультейшин, вот ит кипс мисинг").
 *
 * <p>The same four windows as the spending tab — one table of days, `WINDOWS`, so "this week" is one
 * answer on every tab — plus **All**, because these three are histories and a history a person cannot
 * see whole is a history with a hole in it. Today by default, like the spending tab (the operator's
 * answer).</p>
 */
export type LogPeriod = Window | 'all';

export const LOG_PERIODS: readonly { readonly id: LogPeriod; readonly label: string }[] = [
  ...WINDOWS.map((w) => ({ id: w.id, label: w.label })),
  { id: 'all', label: 'All' },
];

/** The period a tab opens on. */
export const DEFAULT_PERIOD: LogPeriod = 'day';

/** The period a page or a command named, or `undefined` when it named none of them. */
export function periodOf(value: unknown): LogPeriod | undefined {
  return LOG_PERIODS.find((one) => one.id === value)?.id;
}

/** The first instant of the period, in ms — the spending tab's own rule, and minus infinity for All. */
export function periodStart(period: LogPeriod, now: Date): number {
  return startOfPeriodMs(period, now.getTime(), DAYS_OF);
}

/** What `--log --since` is handed: the period's first instant in UTC, or nothing for All. */
export function sinceOf(period: LogPeriod, now: Date): string {
  const start = periodStart(period, now);

  return Number.isFinite(start) ? new Date(start).toISOString() : '';
}

/** Which tab a row belongs to — and so what its buttons say when pressed. */
export type PeriodTab = 'conversations' | 'consultations' | 'spots';

/**
 * The row of period buttons, drawn like the spending tab's.
 *
 * <p>Conversations and Consultations are filtered by the PAGE — their rows are already on it — so their
 * buttons carry `data-period`. What it keeps missing is counted by the server, so its buttons are host
 * commands (`spotsPeriod`), exactly as the spending tab's `usageWindow` is.</p>
 */
export function periodButtonsHtml(tab: PeriodTab, chosen: LogPeriod): string {
  const buttons = LOG_PERIODS.map((one) => {
    const on = one.id === chosen ? ' on' : '';
    const action = tab === 'spots'
      ? `data-command="spotsPeriod" data-id="${one.id}"`
      : `data-period="${one.id}"`;

    return `<button type="button" class="tab${on}" ${action}>${escapeHtml(one.label)}</button>`;
  }).join('');

  return `<div class="windows" data-periods="${tab}">${buttons}</div>`;
}
