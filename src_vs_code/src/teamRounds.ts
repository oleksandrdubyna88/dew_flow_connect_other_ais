import { escapeHtml } from './escapeHtml';
import { RoundWindow, serverRounds, Usage, VendorRounds, WindowRounds } from './teamServerApi';

/**
 * The Team server tab's Rounds block (story 5.2 of todo/PLAN_every_round_is_counted.md) — pure, so every sentence an
 * admin reads is a test.
 *
 * <p><b>Rounds as the SERVER counted them</b>: per vendor, the distinct rounds its review launches belonged to over the
 * trailing 5 h, 24 h and 7 d — never this machine's guess from launches. Beside them, over 7 d: the launches whose
 * client sent no round (an older extension or `coai-mcp`, counted apart and never folded into a round), the client's
 * retries, repairs and follow-ups, the cancelled launches and the budget refusals.</p>
 *
 * <p><b>Absent is never zero.</b> A server older than round counting answers the request with its own range and no
 * counts; {@link serverRounds} says so, and the block says <i>count unavailable</i> with the reason — never a column of
 * zeroes. A window the server DID count and found empty is a real 0.</p>
 *
 * <p>Drawn into the tab's Vendors column under the vendor rows, so the page needs no new part.</p>
 */

/** How each round window is labelled — the local Rounds block's words, so the two halves read alike. */
const WINDOW_LABELS: Readonly<Record<RoundWindow, string>> = { '5h': '5 h', '24h': '24 h', week: '7 d' };

/** A vendor a window has no row for: the server counted that window and found none of its lines. */
const NONE: Omit<VendorRounds, 'vendor'> = {
  rounds: 0, runsWithoutRound: 0, retries: 0, repairs: 0, followUps: 0, cancelled: 0, noBudget: 0, launchesByKind: {},
};

const HEAD = '<div class="colhead"><h3>Rounds</h3><span class="spacer"></span>'
  + '<span class="hint">counted by the server · trailing, UTC</span></div>';

export function teamRoundsBlock(usage: Usage, palette: (vendor: string) => string): string {
  const read = serverRounds(usage);

  return read.kind === 'counted'
    ? block(counted(read.windows, palette))
    : block(`<div class="hint">Rounds: count unavailable — ${escapeHtml(read.reason)}.</div>`);
}

function block(body: string): string {
  return `<div class="team-rounds">${HEAD}${body}</div>`;
}

/** One line per vendor of the widest window — the narrower ones are inside it, so it names every vendor they do. */
function counted(windows: readonly WindowRounds[], palette: (vendor: string) => string): string {
  const widest = windows[windows.length - 1]?.vendors ?? [];
  if (widest.length === 0) {
    return '<div class="quiet">No round ran on this server in the last 7 days.</div>';
  }

  return widest.map((row) => vendorLine(row, windows, palette)).join('');
}

function vendorLine(row: VendorRounds, windows: readonly WindowRounds[], palette: (vendor: string) => string): string {
  const perWindow = windows.map((one) => `${WINDOW_LABELS[one.window]} ${rowIn(one, row.vendor).rounds}`).join(' · ');

  return `<div class="vendor" style="--vc:${escapeHtml(palette(row.vendor))}">`
    + `<div class="vhead"><b>${escapeHtml(row.vendor)}</b><span class="spacer"></span><span class="num">${perWindow}</span></div>`
    + `<div class="vline num">7 d: ${apart(row)}</div></div>`;
}

function rowIn(window: WindowRounds, vendor: string): Omit<VendorRounds, 'vendor'> {
  return window.vendors.find((one) => one.vendor === vendor) ?? NONE;
}

/** What the week held beside its rounds: the runs no round named, the client's own words, and how launches ended. */
function apart(row: VendorRounds): string {
  return [
    counting(row.runsWithoutRound, 'run without a round', 'runs without a round'),
    counting(row.retries, 'retry', 'retries'),
    counting(row.repairs, 'repair', 'repairs'),
    counting(row.followUps, 'follow-up', 'follow-ups'),
    `${row.cancelled} cancelled`,
    counting(row.noBudget, 'budget refusal', 'budget refusals'),
  ].join(' · ');
}

function counting(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}
