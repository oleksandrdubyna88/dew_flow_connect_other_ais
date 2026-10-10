import { escapeHtml } from './escapeHtml';
import { DailyBucket, DailyUsage } from './teamServerApi';
import { VendorPalette } from './vendorColour';

/**
 * The Team server tab's 30-day launches-per-day chart (todo/PLAN_team_usage_by_person.md, story 3.3, D7) — an inline
 * SVG, drawn to scale from the company answer's `daily`, coloured through the vendor palette (the editor's own theme
 * colours), so nothing here names a colour of its own.
 *
 * <p><b>The days are the server's strings, printed as they came.</b> A `yyyy-MM-dd` passed through `new Date()` is
 * midnight UTC and prints as the previous day anywhere west of Greenwich; the server already decided which UTC day each
 * bucket is, so the chart only writes it down. The 30 calendar days are not the summary's Month (the trailing 30 × 24
 * hours), and the chart says so.</p>
 *
 * <p><b>Not by colour alone.</b> A legend names each vendor with its launches, the image carries a summary as its label,
 * and the numbers behind it are a table under a fold. Every string from the server is escaped.</p>
 */

const WIDTH = 560;
const HEIGHT = 180;
const LEFT = 36;
const RIGHT = 6;
const TOP = 16;
const BOTTOM = 22;
const PLOT_HEIGHT = HEIGHT - TOP - BOTTOM;

function dayTotal(day: DailyBucket): number {
  return day.vendors.reduce((sum, one) => sum + one.runs, 0);
}

/** Every vendor named on any day, busiest first — the stacking order, bottom up, and the legend's order. */
function vendorsOf(daily: DailyUsage): readonly { readonly vendor: string; readonly runs: number }[] {
  const totals = new Map<string, number>();
  for (const one of daily.days.flatMap((day) => day.vendors)) {
    totals.set(one.vendor, (totals.get(one.vendor) ?? 0) + one.runs);
  }

  return [...totals].map(([vendor, runs]) => ({ vendor, runs })).sort((a, b) => b.runs - a.runs);
}

/** A round top for the axis: 1, 2 or 5 times a power of ten, never below the busiest day. */
function axisTop(busiest: number): number {
  const power = 10 ** Math.floor(Math.log10(Math.max(busiest, 1)));

  return [1, 2, 5, 10].map((step) => step * power).find((top) => top >= busiest) ?? busiest;
}

/** One day's stacked bars, bottom up in the legend's order, each saying its day, vendor and launches. */
function dayBars(day: DailyBucket, x: number, width: number, unit: number, order: readonly string[], palette: VendorPalette): string {
  let base = TOP + PLOT_HEIGHT;

  return order.map((vendor) => {
    const runs = day.vendors.find((one) => one.vendor === vendor)?.runs ?? 0;
    if (runs <= 0) {
      return '';
    }
    const height = runs * unit;
    base -= height;

    return `<rect x="${x.toFixed(2)}" y="${base.toFixed(4)}" width="${width.toFixed(2)}" height="${height}" `
      + `style="fill:${escapeHtml(palette(vendor))}" data-day="${escapeHtml(day.day)}" data-vendor="${escapeHtml(vendor)}" `
      + `data-runs="${runs}"><title>${escapeHtml(day.day)} · ${escapeHtml(vendor)}: ${runs} launches</title></rect>`;
  }).join('');
}

/** The grid: a line at zero, half way and the top, each labelled, and the axis's name. */
function grid(top: number): string {
  const lines = [0, top / 2, top].filter((value) => Number.isInteger(value)).map((value) => {
    const y = TOP + PLOT_HEIGHT * (1 - value / top);

    return `<line x1="${LEFT}" x2="${WIDTH - RIGHT}" y1="${y}" y2="${y}" class="gridline"></line>`
      + `<text x="${LEFT - 4}" y="${y + 3}" text-anchor="end">${value}</text>`;
  }).join('');

  // At the plot's left edge, anchored at its START: end-anchored at the svg's edge it ran off the drawing and was cut.
  return `${lines}<text x="${LEFT}" y="${TOP - 6}" text-anchor="start" class="axis">launches</text>`;
}

function dayLabels(days: readonly DailyBucket[], step: number): string {
  const first = days[0];
  const last = days[days.length - 1];
  if (first === undefined || last === undefined) {
    return '';
  }

  return `<text x="${LEFT}" y="${HEIGHT - 6}" text-anchor="start">${escapeHtml(first.day)}</text>`
    + `<text x="${LEFT + step * days.length}" y="${HEIGHT - 6}" text-anchor="end">${escapeHtml(last.day)}</text>`;
}

function summary(daily: DailyUsage): string {
  const all = daily.days.reduce((sum, day) => sum + dayTotal(day), 0);
  const busiest = daily.days.reduce<DailyBucket | undefined>((best, day) => (best === undefined || dayTotal(day) > dayTotal(best) ? day : best), undefined);

  return busiest === undefined ? 'Launches per day: no days.'
    : `Launches per day over the last ${daily.days.length} UTC days, by vendor: ${all} in all; the busiest day ${busiest.day}, with ${dayTotal(busiest)}.`;
}

function svg(daily: DailyUsage, palette: VendorPalette): string {
  const order = vendorsOf(daily).map((one) => one.vendor);
  const top = axisTop(Math.max(0, ...daily.days.map(dayTotal)));
  const step = (WIDTH - LEFT - RIGHT) / Math.max(daily.days.length, 1);
  const unit = PLOT_HEIGHT / top;
  const bars = daily.days.map((day, i) => dayBars(day, LEFT + i * step + 1, Math.max(step - 2, 1), unit, order, palette)).join('');

  return `<svg viewBox="0 0 ${WIDTH} ${HEIGHT}" role="img" aria-label="${escapeHtml(summary(daily))}">`
    + `${grid(top)}${bars}${dayLabels(daily.days, step)}</svg>`;
}

function legend(daily: DailyUsage, palette: VendorPalette): string {
  return `<div class="legend">${vendorsOf(daily).map((one) => `<span><i class="dot" style="background:${escapeHtml(palette(one.vendor))}"></i>`
    + `${escapeHtml(one.vendor)} <span class="num">${one.runs}</span></span>`).join('')}</div>`;
}

/** The numbers behind the bars, one row per day — the chart's text alternative. */
function table(daily: DailyUsage, serverId: string): string {
  const order = vendorsOf(daily).map((one) => one.vendor);
  const head = `<tr><th scope="col">Day</th>${order.map((vendor) => `<th scope="col">${escapeHtml(vendor)}</th>`).join('')}<th scope="col">All</th></tr>`;
  const rows = daily.days.map((day) => `<tr><td>${escapeHtml(day.day)}</td>`
    + `${order.map((vendor) => `<td class="num">${day.vendors.find((one) => one.vendor === vendor)?.runs ?? 0}</td>`).join('')}`
    + `<td class="num">${dayTotal(day)}</td></tr>`).join('');

  // A fold key, so a push keeps it open for whoever is reading it (the page compares it as an attribute value).
  return `<details class="chart-data" data-fold="${escapeHtml(`${serverId}|chart-data`)}"><summary>The numbers</summary><div class="tablewrap"><table class="t"><thead>${head}</thead>`
    + `<tbody>${rows}</tbody></table></div></details>`;
}

const NOTE = '<div class="hint">UTC calendar days, today included — not the Month window, which is the trailing 30 × 24 hours. '
  + 'A launch is one run of a vendor CLI; a round launches several.</div>';

/** The chart, its legend, its numbers and its note. */
export function launchesChart(daily: DailyUsage, palette: VendorPalette, serverId = ''): string {
  const quiet = daily.days.every((day) => dayTotal(day) === 0);

  return quiet
    ? `<div class="quiet">No launches in these 30 days.</div>${NOTE}`
    : `${svg(daily, palette)}${legend(daily, palette)}${table(daily, serverId)}${NOTE}`;
}
