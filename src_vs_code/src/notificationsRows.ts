import { escapeHtml } from './escapeHtml';
import { CLASS_ORDER } from './notifications';
import { Grouped, UNKNOWN_TAB, repeatsSaid, tabOf } from './notificationsRead';

/**
 * The notifications page's table and tab strip, as markup.
 *
 * <p>Pure, and separate from the page shell so that neither file grows past the 400 lines the S5
 * plan budgets per module. Every value that reaches this file came out of a ledger — a vendor's
 * stderr, a server's response body, an exception message — so <b>every one of them is escaped</b>,
 * and a payload test drives all six fields through the rendered page. Naming the helper is not the
 * same as using it. (codex, the S5 plan round.)</p>
 */

/** A column, and how to read it off a row. */
export interface Column {
  readonly key: string;
  readonly label: string;
  /** Right-aligned, for the two numeric ones. */
  readonly figure?: boolean;
}

/**
 * What a row shows, in the order a person reads it.
 *
 * <p>`When` is the LATEST occurrence — "when did this last happen" is what the page is opened to
 * answer — and sorting on it sorts on that. The row carries its first occurrence too, which is what
 * "since" beside the rate is drawn from.</p>
 */
export const COLUMNS: readonly Column[] = [
  { key: 'when', label: 'When' },
  { key: 'class', label: 'Class' },
  { key: 'source', label: 'Source' },
  { key: 'title', label: 'What happened' },
  { key: 'cure', label: 'What to do' },
  { key: 'repeats', label: 'Repeats', figure: true },
  { key: 'rate', label: 'Rate', figure: true },
  { key: 'subject', label: 'Where' },
];

/**
 * Every tab the page offers: the one class catalog, plus one for what this build does not know.
 *
 * <p>DERIVED, never listed again. `tabOf` decides membership through `isKnownClass`, which reads
 * the same catalog, so a class added to the parser gets a tab by arriving rather than by somebody
 * remembering. Listing them here a second time meant a new class would be 'known' — so not sent
 * to `other` — and have no tab of its own either: its rows would simply not appear, with nothing
 * red. (codex, the S5 code round, from two roles.)</p>
 */
export const TABS: readonly string[] = [...CLASS_ORDER, UNKNOWN_TAB];

/** A rate as a figure, or an em dash where no window worth measuring was observed. */
export function rateSaid(row: Grouped): string {
  return row.ratePerMin === undefined ? '—' : `${row.ratePerMin.toFixed(1)}/min`;
}

/** A UTC instant as the local time a person reads, with the instant kept for the machine. */
export function whenCell(utc: string): string {
  const at = new Date(utc);

  return Number.isFinite(at.getTime())
    ? `<time datetime="${escapeHtml(utc)}">${escapeHtml(at.toLocaleString())}</time>`
    : escapeHtml(utc);
}

/**
 * One row, with every cell escaped and the sort keys carried as data attributes.
 *
 * <p>The sort keys are separate from what is DISPLAYED on purpose: `when` sorts on the ISO instant
 * and shows local time, `repeats` sorts on the number and shows "1000+ in each of 3 runs", and a
 * rate that could not be measured sorts as a blank so that it goes last in both directions rather
 * than to the top of the one column the storm feature exists for.</p>
 */
export function rowHtml(row: Grouped): string {
  const cells = [
    `<td data-sort="${escapeHtml(row.when)}">${whenCell(row.when)}</td>`,
    `<td data-sort="${escapeHtml(row.class)}">${escapeHtml(row.class)}</td>`,
    `<td data-sort="${escapeHtml(row.source)}">${escapeHtml(row.source)}</td>`,
    `<td data-sort="${escapeHtml(row.title)}">${escapeHtml(row.title)}</td>`,
    `<td data-sort="${escapeHtml(row.cure)}">${escapeHtml(row.cure)}</td>`,
    `<td class="figure" data-sort="${row.repeats}">${escapeHtml(repeatsSaid(row))}</td>`,
    `<td class="figure" data-sort="${row.ratePerMin ?? ''}">${escapeHtml(rateSaid(row))}</td>`,
    `<td data-sort="${escapeHtml(row.subject)}">${escapeHtml(row.subject)}</td>`,
  ];

  // The haystack carries the MESSAGE TEXT, which is the whole point: a log whose search cannot find
  // the sentence on screen is the trap `rowMatches` set in the rounds log once already.
  const haystack = [row.when, row.class, row.source, row.title, row.cure, row.subject, row.code]
    .join(' ')
    .toLowerCase();

  return `<tr data-tab="${escapeHtml(tabOf(row))}" data-read="${row.read ? 'yes' : 'no'}"`
    + ` data-source="${escapeHtml(row.source)}" data-when="${escapeHtml(row.when)}"`
    + ` data-find="${escapeHtml(haystack)}">${cells.join('')}</tr>`;
}

/** The header, every column sortable, the current one marked for a reader and for a screen reader. */
export function headHtml(): string {
  const cells = COLUMNS.map((column) =>
    `<th scope="col" data-key="${escapeHtml(column.key)}" aria-sort="none"`
    + `${column.figure === true ? ' class="figure"' : ''}>`
    + `<button type="button" class="sort">${escapeHtml(column.label)}</button></th>`);

  return `<thead><tr>${cells.join('')}</tr></thead>`;
}

/**
 * The tab strip, on the roles page's pattern.
 *
 * <p>`role="tablist"` / `role="tab"` / `aria-controls` / `aria-selected`, and the panels below
 * carry `data-section` so the handler DERIVES which sections exist rather than naming them. A
 * literal list is the defect S6 is queued to fix in the rounds log, and reproducing it here first
 * would be perverse.</p>
 */
export function tabStrip(rows: readonly Grouped[], openTab: string): string {
  const counted = new Map<string, number>();
  for (const row of rows) {
    counted.set(tabOf(row), (counted.get(tabOf(row)) ?? 0) + 1);
  }
  const buttons = TABS.map((tab) => {
    const many = counted.get(tab) ?? 0;
    const chosen = tab === openTab;

    return `<button type="button" role="tab" id="tab-${escapeHtml(tab)}"`
      + ` aria-controls="section-${escapeHtml(tab)}" aria-selected="${chosen ? 'true' : 'false'}"`
      + ` data-tab="${escapeHtml(tab)}"${many === 0 ? ' class="empty"' : ''}>`
      + `${escapeHtml(tab)} <span class="many">${many}</span></button>`;
  });

  return `<div class="tabs" role="tablist" aria-label="Which notifications to read">${buttons.join('')}</div>`;
}

/** Which tab opens first: the one with something in it, preferring the ones that need acting on. */
export function firstTab(rows: readonly Grouped[]): string {
  const has = new Set(rows.map((row) => tabOf(row)));

  return TABS.find((tab) => has.has(tab)) ?? TABS[0] as string;
}

/** The table for one tab. Empty is SAID, not left blank. */
export function tableHtml(rows: readonly Grouped[], tab: string): string {
  const mine = rows.filter((row) => tabOf(row) === tab);
  const body = mine.length === 0
    ? `<p class="empty-note">Nothing in ${escapeHtml(tab)}.</p>`
    : `<table><${''}caption class="sr-only">Notifications of class ${escapeHtml(tab)}</caption>`
      + `${headHtml()}<tbody>${mine.map((row) => rowHtml(row)).join('')}</tbody></table>`;

  return `<section id="section-${escapeHtml(tab)}" role="tabpanel" aria-labelledby="tab-${escapeHtml(tab)}"`
    + ` data-section="${escapeHtml(tab)}">${body}</section>`;
}
