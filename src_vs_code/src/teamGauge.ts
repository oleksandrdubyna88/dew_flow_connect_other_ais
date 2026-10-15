import { escapeHtml } from './escapeHtml';
import { type GaugeRatio, gaugeLines, ratioLine } from './gaugeWords';
import { planPriceKey, type PlanPrices, shareLine } from './planPrice';
import type { CatalogVendor, GaugeReading } from './teamServerApi';

/**
 * The Team server tab's gauge block (story 6.2 of todo/PLAN_every_round_is_counted.md) — pure, so every sentence an admin
 * reads is a test.
 *
 * <p><b>Two answers, each read by hand.</b> The vendor's own windows come from the CATALOG (`slots.gauge`, the newest
 * reading among the vendor's accounts, story 6.1a); each window's rounds per 1 % comes from the company USAGE answer
 * (`gauges`, the shared library's derivation over the server's ledger). A server older than either says nothing, and the
 * block says so where the figure would be — never a 0 %, never 0 rounds.</p>
 *
 * <p><b>The share</b> (`planPrice.ts`) is drawn only beside a weekly figure that is SHOWN and only when a price is typed
 * for this server's vendor: hidden whenever the figure is, labelled as a share, and never added to the ~$ list-price
 * estimate above it.</p>
 */

/** What the Team server tab's gauge block is drawn from. */
export interface GaugeBlockView {
  readonly serverId: string;
  readonly vendors: readonly CatalogVendor[];
  /** The company usage answer, as the server sent it. */
  readonly usage: unknown;
  readonly prices: PlanPrices;
  readonly now: number;
  readonly palette: (vendor: string) => string;
  /** For tests; a person reads every instant in their own zone. */
  readonly timeZone?: string;
}

/** Said where the figures would be when the server's answer carries none — a server older than story 6.2. */
export const RATIO_NOT_DERIVED = 'rounds per 1 %: this Team server does not derive it yet';

const HEAD = '<div class="colhead"><h3>Vendor gauges</h3><span class="spacer"></span>'
  + '<span class="hint">the vendor\'s own numbers · read after each job</span></div>';

/**
 * Each window's figure as a newer server sends it — or undefined when the answer carries no `gauges` at all, which is a
 * server older than the field and never a list of none. An entry that is not the contract is dropped.
 */
export function serverGauges(usage: unknown): readonly GaugeRatio[] | undefined {
  const listed = isRecord(usage) ? usage['gauges'] : undefined;

  return Array.isArray(listed) ? listed.flatMap(ratioOf) : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

const TEXT_FIELDS = ['vendor', 'limitId', 'unavailable', 'missingConsumer'] as const;

/** One entry, every field checked — or nothing. */
function ratioOf(raw: unknown): readonly GaugeRatio[] {
  const row = isRecord(raw) ? raw : {};

  return CHECKS.every((check) => check(row)) ? [row as unknown as GaugeRatio] : [];
}

/** What an entry must be to be read at all — one check per field kind. */
const CHECKS: readonly ((row: Record<string, unknown>) => boolean)[] = [
  (row) => TEXT_FIELDS.every((field) => typeof row[field] === 'string'),
  (row) => isWhole(row['windowMins']),
  (row) => isWhole(row['intervals']),
  (row) => isFigure(row['roundsPerPercent']),
];

function isWhole(value: unknown): boolean {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

/** A figure on the wire: null (unavailable or hidden), or a finite number. */
function isFigure(value: unknown): boolean {
  return value === null || (typeof value === 'number' && Number.isFinite(value));
}

/** The block: one part per vendor the server read, or one quiet sentence when it read none. */
export function teamGaugeBlock(view: GaugeBlockView): string {
  const read = view.vendors.filter((vendor) => vendor.slots?.gauge !== undefined);
  const body = read.length === 0
    ? '<div class="quiet">No vendor\'s own gauge has been read on this server yet.</div>'
    : read.map((vendor) => vendorPart(vendor, vendor.slots.gauge!, view)).join('');

  return `<div class="team-gauges">${HEAD}${body}</div>`;
}

function vendorPart(vendor: CatalogVendor, reading: GaugeReading, view: GaugeBlockView): string {
  const lines = [...gaugeLines(reading, view.now, view.timeZone), ...figureLines(vendor.id, view)];

  return `<div class="vendor" style="--vc:${escapeHtml(view.palette(vendor.id))}">`
    + `<div class="vhead"><b>${escapeHtml(vendor.id)}</b></div>`
    + lines.map((line) => `<div class="vline">${escapeHtml(line)}</div>`).join('')
    + '</div>';
}

/** The vendor's figures — each with its share when one applies — or the older server's sentence. */
function figureLines(vendor: string, view: GaugeBlockView): readonly string[] {
  const figures = serverGauges(view.usage);
  if (figures === undefined) {
    return [RATIO_NOT_DERIVED];
  }
  const price = view.prices[planPriceKey(view.serverId, vendor)];

  return figures
    .filter((ratio) => ratio.vendor === vendor)
    .flatMap((ratio) => [ratioLine(ratio), shareLine(price, ratio, view.now)].filter((line) => line.length > 0));
}
