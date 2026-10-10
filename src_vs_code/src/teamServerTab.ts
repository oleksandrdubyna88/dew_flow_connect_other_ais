import { escapeHtml } from './escapeHtml';
import { PersonUsage, Usage, VendorUsage } from './teamServerApi';
import { TeamServerState } from './teamServerView';
import { selectedServer, TeamSelection, TeamWindow, windowOf } from './teamTabSelection';
import { UsageCell } from './teamUsageCache';
import { shortNumber } from './usage';
import { VendorPalette } from './vendorColour';

/**
 * The Team server tab's figures, as markup (todo/PLAN_team_usage_by_person.md, story 1.3) — pure, so every sentence an
 * admin reads is a test rather than something only a real server can show.
 *
 * <p><b>Launches, not rounds.</b> The server's ledger has a line per vendor RUN and no round id, so grouping it by
 * person or day yields launches; the tab counts those and says so. Rounds, retries, cached share and the vendor quota
 * gauges in the approved mockup belong to PLAN_every_round_is_counted.md and appear here when its fields arrive.</p>
 *
 * <p><b>Absent is never zero (D8).</b> What today's server cannot say — a model per row, a price, who is signed in, a
 * per-day series — says which server it needs in its place. A server is recognised as older by the ABSENCE of
 * `daily`, never by an empty `models`, which a newer server answers too.</p>
 *
 * <p>Every name and email is the server's text and goes through the one escaper; nothing from the wire becomes a
 * selector — the fold keys are compared as attribute values on the page.</p>
 */

/** The Team server release that says which model ran, who is signed in, and launches per day (the plan's epic 2). */
export const TEAM_SERVER_WITH_PEOPLE = '0.11.0';

/** The pieces of the tab the page replaces on a push — everything but its toolbar, search and sort, which stay put. */
export interface TeamParts {
  readonly status: string;
  readonly badges: string;
  readonly summary: string;
  readonly people: string;
  readonly idle: string;
  readonly vendors: string;
  readonly chart: string;
  readonly notes: string;
}

/** What the page is told about the tab. */
export interface TeamPush {
  /** Whether any configured server says this account is an admin — the tab exists only then (D1). */
  readonly admin: boolean;
  /** The admin servers, as `<option>`s for the server picker. */
  readonly servers: string;
  readonly selected: string;
  readonly window: TeamWindow;
  /** When the shown answer landed, ISO, or empty — the page prints it in the viewer's own time. */
  readonly readUtc: string;
  readonly parts: TeamParts;
}

export interface TeamTabInput {
  readonly states: readonly TeamServerState[];
  readonly selection: TeamSelection;
  /** The company figures held for a server over a window. */
  readonly cell: (serverId: string, window: TeamWindow) => UsageCell | undefined;
  readonly asking: (serverId: string, window: TeamWindow) => boolean;
  readonly palette: VendorPalette;
}

const EMPTY_PARTS: TeamParts = { status: '', badges: '', summary: '', people: '', idle: '', vendors: '', chart: '', notes: '' };

/** The admin servers this side is signed in to. */
export function adminStates(states: readonly TeamServerState[]): readonly TeamServerState[] {
  return states.filter((state) => state.catalog?.isAdmin === true && state.email.length > 0);
}

/** The whole push: nothing but the admin flag for a non-admin, everything for an admin. */
export function teamTabPush(input: TeamTabInput): TeamPush {
  const admins = adminStates(input.states);
  const selected = selectedServer(input.selection, admins.map((state) => state.server.id));
  const state = admins.find((one) => one.server.id === selected);
  const window = windowOf(input.selection, selected);
  if (state === undefined) {
    return { admin: false, servers: '', selected: '', window, readUtc: '', parts: EMPTY_PARTS };
  }
  const cell = input.cell(selected, window);

  return {
    admin: true,
    servers: serverOptions(admins, selected),
    selected,
    window,
    readUtc: cell === undefined ? '' : new Date(cell.answeredAt).toISOString(),
    parts: teamParts(state, cell, input.asking(selected, window), input.palette),
  };
}

function serverOptions(admins: readonly TeamServerState[], selected: string): string {
  return admins
    .map((state) => `<option value="${escapeHtml(state.server.id)}"${state.server.id === selected ? ' selected' : ''}>`
      + `${escapeHtml(state.server.name)}</option>`)
    .join('');
}

/** Every replaced piece, for one server's answer — or for its absence. */
export function teamParts(state: TeamServerState, cell: UsageCell | undefined, asking: boolean, palette: VendorPalette): TeamParts {
  const usage = cell?.usage;
  const status = statusLine(state, cell, asking);
  if (usage === undefined) {
    return { ...EMPTY_PARTS, status, badges: badges(state, undefined), notes: NOTES };
  }

  return {
    status,
    badges: badges(state, usage),
    summary: summary(usage),
    people: people(state.server.id, usage.people ?? [], palette),
    idle: idle(state.server.id, usage),
    vendors: vendors(usage, palette),
    chart: chart(usage),
    notes: NOTES,
  };
}

/** What is happening, in a sentence — or nothing, when the figures below speak for themselves. */
export function statusLine(state: TeamServerState, cell: UsageCell | undefined, asking: boolean): string {
  const name = escapeHtml(state.server.name);
  if (asking || cell === undefined) {
    return `<div class="team-status" role="status">Asking ${name}…</div>`;
  }

  return cell.refused === true
    ? `<div class="team-status warn" role="status">You are no longer an admin here — ${name} refused the company view: `
      + `${escapeHtml(cell.problem)}</div>`
    : problemLine(cell);
}

/** A failure said in the server's words — and, when an older answer is still on screen, that it is the older one. */
function problemLine(cell: UsageCell): string {
  if (cell.problem.length === 0) {
    return '';
  }
  const older = cell.usage === undefined ? '' : ' — showing what it last said.';

  return `<div class="team-status warn" role="status">${escapeHtml(cell.problem)}${older}</div>`;
}

function badges(state: TeamServerState, usage: Usage | undefined): string {
  return [versionBadge(state), '<span class="badge">you are an admin</span>', unreadableBadge(usage)].join('');
}

/** The server's version, marked healthy unless the row has a problem or is showing an older answer. */
function versionBadge(state: TeamServerState): string {
  const version = state.catalog?.serverVersion ?? '';

  return version.length > 0 ? `<span class="badge health ${healthOf(state)}">server ${escapeHtml(version)}</span>` : '';
}

function healthOf(state: TeamServerState): 'ok' | 'warn' {
  return state.problem.length > 0 || state.stale ? 'warn' : 'ok';
}

function unreadableBadge(usage: Usage | undefined): string {
  const unreadable = usage?.unreadableLines ?? 0;

  return unreadable > 0 ? `<span class="badge health warn">${unreadable} ledger line(s) the server could not read</span>` : '';
}

/** Whether this answer came from a server too old to say per-model, per-day and who-is-signed-in. */
export function isOlderServer(usage: Usage): boolean {
  return usage.daily === undefined;
}

/** The sentence standing where a figure would be: which server says it, or — on a newer one — that this build does not. */
export function notYet(usage: Usage): string {
  return isOlderServer(usage)
    ? `needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE}`
    : 'not shown by this version of the extension yet';
}

interface Totals {
  readonly runs: number;
  readonly failed: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
}

function totalsOf(rows: readonly VendorUsage[]): Totals {
  return rows.reduce((sum, row) => ({
    runs: sum.runs + row.runs,
    failed: sum.failed + row.failed,
    tokensIn: sum.tokensIn + row.tokensIn,
    tokensOut: sum.tokensOut + row.tokensOut,
  }), { runs: 0, failed: 0, tokensIn: 0, tokensOut: 0 });
}

function tile(key: string, value: string, sub: string): string {
  return `<div><div class="k">${key}</div><div class="v num">${value}</div><div class="s num">${sub}</div></div>`;
}

function summary(usage: Usage): string {
  const total = totalsOf(usage.vendors);
  const active = (usage.people ?? []).filter((person) => totalsOf(person.vendors).runs > 0).length;
  const later = escapeHtml(notYet(usage));

  return `<div class="team-summary">${[
    tile('Launches', shortNumber(total.runs), total.failed > 0 ? `${shortNumber(total.failed)} failed` : 'none failed'),
    tile('Tokens', shortNumber(total.tokensIn + total.tokensOut),
      `${shortNumber(total.tokensIn)} in · ${shortNumber(total.tokensOut)} out`),
    tile('~ List price', '—', `an estimate, never a bill — ${later}`),
    tile('People', String(active), `signed in with no runs: ${later}`),
  ].join('')}</div>`;
}

/** Two letters for the round mark beside a card, from the email the server sent — decoration, hidden from readers. */
function initials(email: string): string {
  const local = email.split('@')[0] ?? '';
  const parts = local.split(/[._-]+/).filter((part) => part.length > 0);

  return (parts.length > 1 ? `${parts[0]![0]}${parts[1]![0]}` : local.slice(0, 2)).toUpperCase();
}

/** A bar split by vendor, by launches. */
function stack(rows: readonly VendorUsage[], palette: VendorPalette): string {
  const runs = Math.max(totalsOf(rows).runs, 1);

  return rows
    .filter((row) => row.runs > 0)
    .map((row) => `<i style="width:${((row.runs / runs) * 100).toFixed(1)}%;background:${escapeHtml(palette(row.vendor))}" `
      + `title="${escapeHtml(row.vendor)}"></i>`)
    .join('');
}

function vendorTable(rows: readonly VendorUsage[], palette: VendorPalette): string {
  const body = rows.map((row) => `<tr><td><i class="dot" style="background:${escapeHtml(palette(row.vendor))}"></i>`
    + `${escapeHtml(row.vendor)}</td><td class="num">${shortNumber(row.runs)}</td><td class="num">${shortNumber(row.failed)}</td>`
    + `<td class="num">${shortNumber(row.tokensIn)}</td><td class="num">${shortNumber(row.tokensOut)}</td></tr>`).join('');

  return '<div class="tablewrap"><table class="t"><thead><tr><th scope="col">Vendor</th><th scope="col">Launches</th>'
    + '<th scope="col">Failed</th><th scope="col">In</th><th scope="col">Out</th></tr></thead>'
    + `<tbody>${body}</tbody></table></div>`;
}

/**
 * One person, as a card that opens. Its fold key is the SERVER and the email, lower-cased — the server already groups
 * one person's casings together (D2), so this never regroups; it only keeps an open card open across a push, and keeps
 * the same email on another server a different card.
 */
export function personCard(serverId: string, person: PersonUsage, palette: VendorPalette): string {
  const email = person.email.toLowerCase();
  const total = totalsOf(person.vendors);
  const failed = total.failed > 0 ? `<span class="warn">${shortNumber(total.failed)} failed</span>` : '<span>no failures</span>';

  return `<details class="person" data-fold="${escapeHtml(`${serverId}|${email}`)}" data-search="${escapeHtml(email)}" `
    + `data-launches="${total.runs}" data-tokens="${total.tokensIn + total.tokensOut}">`
    + `<summary><span class="avatar" aria-hidden="true">${escapeHtml(initials(person.email))}</span>`
    + `<span class="who"><b>${escapeHtml(person.email)}</b></span>`
    + '<span class="cost num" title="~$ needs the model each run used">~$ —</span>'
    + `<span class="line2 num"><span>${shortNumber(total.runs)} launches</span>`
    + `<span>${shortNumber(total.tokensIn)} in · ${shortNumber(total.tokensOut)} out</span>`
    + '<span>last seen —</span></span>'
    + `<span class="stack" aria-hidden="true">${stack(person.vendors, palette)}</span></summary>`
    + `<div class="detail">${vendorTable(person.vendors, palette)}<div class="fails">${failed}</div></div></details>`;
}

function people(serverId: string, rows: readonly PersonUsage[], palette: VendorPalette): string {
  return rows.length === 0
    ? '<div class="quiet">Nobody ran anything on this server in this window.</div>'
    : rows.map((person) => personCard(serverId, person, palette)).join('');
}

/** The collapsed list of people signed in with no runs (D3) — the server must list sessions for it. */
function idle(serverId: string, usage: Usage): string {
  return `<details class="quiet" data-fold="${escapeHtml(`${serverId}|idle`)}">`
    + '<summary>Signed in, no recorded runs in this window</summary>'
    + `<div class="hint">Who is signed in ${escapeHtml(notYet(usage))}, which lists the people with an unexpired session.</div></details>`;
}

function models(row: VendorUsage, usage: Usage): string {
  const ran = row.models ?? [];

  return ran.length === 0
    ? `<span class="model hint">models: ${escapeHtml(notYet(usage))}</span>`
    : `<span class="model">${ran.map((one) => escapeHtml(one.model.length > 0 ? one.model : 'unknown')).join(', ')}</span>`;
}

function vendors(usage: Usage, palette: VendorPalette): string {
  if (usage.vendors.length === 0) {
    return '<div class="quiet">No vendor ran in this window.</div>';
  }

  return usage.vendors.map((row) => `<div class="vendor" style="--vc:${escapeHtml(palette(row.vendor))}">`
    + `<div class="vhead"><b>${escapeHtml(row.vendor)}</b>${models(row, usage)}<span class="spacer"></span>`
    + '<span class="num" title="~$ needs the model each run used">~$ —</span></div>'
    + `<div class="vline num">${shortNumber(row.runs)} launches · ${shortNumber(row.failed)} failed · `
    + `${shortNumber(row.tokensIn)} in · ${shortNumber(row.tokensOut)} out</div></div>`).join('');
}

function chart(usage: Usage): string {
  return '<div class="chart"><div class="colhead"><h2>Launches per day</h2><span class="spacer"></span>'
    + '<span class="hint">last 30 UTC days</span></div>'
    + `<div class="hint">The chart ${escapeHtml(notYet(usage))}.</div></div>`;
}

const NOTES = '<div><b>Launches</b> are runs of a vendor\'s CLI, not review rounds: one round launches several reviewers, and '
  + 'a retry is another launch. Rounds are counted here once the server records which round a run belonged to.</div>'
  + '<div><b>~$</b> is an estimate at public list prices, never a bill — the company pays flat subscriptions.</div>'
  + '<div>The windows are the server\'s own: trailing, and counted in UTC — <b>Today</b> here is not since your midnight.</div>';

/** What the page is told before anything is known — no tab, which is also what a non-admin is told. */
export const NO_TEAM_PUSH: TeamPush = { admin: false, servers: '', selected: '', window: 'week', readUtc: '', parts: EMPTY_PARTS };
