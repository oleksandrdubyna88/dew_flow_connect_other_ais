import { escapeHtml } from './escapeHtml';
import { PersonListing, Usage, VendorUsage } from './teamServerApi';
import { costText, ListPrice, totalCost, vendorCost } from './teamCost';
import { launchesChart } from './teamChart';
import { idleList, joinPeople, peopleCards, PeopleView, totalsOf } from './teamPeople';
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
  /** Whether names arrived, so the search can say it matches them (3.1). */
  readonly named?: boolean;
  /** Which of the sorts the answer can serve: ~$ needs per-model figures, last seen needs the listing. */
  readonly sorts?: { readonly cost: boolean; readonly seen: boolean };
  /** Why each unavailable sort is unavailable — empty for one the answer serves — so its label says the truth. */
  readonly sortWhy?: { readonly cost: string; readonly seen: string };
}

export interface TeamTabInput {
  readonly states: readonly TeamServerState[];
  readonly selection: TeamSelection;
  /** The company figures held for a server over a window. */
  readonly cell: (serverId: string, window: TeamWindow) => UsageCell | undefined;
  readonly asking: (serverId: string, window: TeamWindow) => boolean;
  readonly palette: VendorPalette;
  /** The people listing held for a server (`GET /api/people`), or nothing. */
  readonly people: (serverId: string) => UsageCell | undefined;
  /** Whether the people listing for a server is being asked right now. */
  readonly peopleAsking: (serverId: string) => boolean;
  /** Each model's public list price — the window's price book, never a person's typed row rate (D4). */
  readonly price: ListPrice;
  /** Now, UTC milliseconds — what "last seen" is measured from. */
  readonly now: number;
}

/** The person card's extra inputs, gathered: the listing, the prices, the clock. */
export interface PartsExtras {
  readonly people?: UsageCell | undefined;
  /** Whether the people listing is on its way. */
  readonly peopleAsking?: boolean;
  readonly price: ListPrice;
  readonly now: number;
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
    readUtc: readOf(cell),
    parts: teamParts(state, cell, input.asking(selected, window), input.palette, extrasOf(input, selected)),
    ...abilities(cell, input.people(selected)),
    sortWhy: sortWhy(cell?.usage, listingOf(cell?.usage, extrasOf(input, selected))),
  };
}

function extrasOf(input: TeamTabInput, serverId: string): PartsExtras {
  return { people: input.people(serverId), peopleAsking: input.peopleAsking(serverId), price: input.price, now: input.now };
}

/**
 * Where the people listing stands: held, on its way, failed, or not served at all by an older server. Three of those
 * are not "missing" in the same way, and saying the older server's sentence for the other two told a newer server it
 * was too old — or that this extension cannot show what it was merely still loading (own review of E3).
 */
export type Listing =
  | { readonly kind: 'held'; readonly people: readonly PersonListing[] }
  | { readonly kind: 'asking' }
  | { readonly kind: 'failed'; readonly problem: string }
  | { readonly kind: 'older' };

/** The listing's state, from the company answer it goes with and the cache's cell and flight for it. */
export function listingOf(usage: Usage | undefined, extras: Pick<PartsExtras, 'people' | 'peopleAsking'>): Listing {
  if (fromOlderServer(usage)) {
    return { kind: 'older' };
  }
  const held = extras.people?.people;

  return held !== undefined ? { kind: 'held', people: held } : unheld(extras);
}

function fromOlderServer(usage: Usage | undefined): boolean {
  return usage !== undefined && isOlderServer(usage);
}

/** No listing held: on its way (or about to be asked, the tab having just shown), or failed in the server's words. */
function unheld(extras: Pick<PartsExtras, 'people' | 'peopleAsking'>): Listing {
  const cell = extras.people;

  return extras.peopleAsking === true || cell === undefined ? { kind: 'asking' } : { kind: 'failed', problem: cell.problem };
}

/** Why each sort that needs a newer server is unavailable now — empty when the answer serves it. */
function sortWhy(usage: Usage | undefined, listing: Listing): { readonly cost: string; readonly seen: string } {
  return { cost: costWhy(usage), seen: LISTING_WHY[listing.kind] };
}

function costWhy(usage: Usage | undefined): string {
  if (usage === undefined) {
    return 'asking…';
  }

  return isOlderServer(usage) ? `needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE}` : '';
}

const LISTING_WHY: Readonly<Record<Listing['kind'], string>> = {
  held: '',
  asking: 'asking…',
  failed: 'the people listing could not be read',
  older: `needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE}`,
};

/** When the shown answer landed, for the page to print in local time — or nothing. */
function readOf(cell: UsageCell | undefined): string {
  return cell === undefined ? '' : new Date(cell.answeredAt).toISOString();
}

/** What the page may offer for this answer: names to search, and the two sorts that need a newer server. */
function abilities(cell: UsageCell | undefined, people: UsageCell | undefined): Pick<TeamPush, 'named' | 'sorts'> {
  return { named: isNamed(people?.people), sorts: { cost: servesCost(cell?.usage), seen: people?.people !== undefined } };
}

function isNamed(listed: readonly PersonListing[] | undefined): boolean {
  return (listed ?? []).some((one) => one.displayName.length > 0);
}

/** ~$ needs the per-model figures, which only a newer server sends. */
function servesCost(usage: Usage | undefined): boolean {
  return usage !== undefined && !isOlderServer(usage);
}

function serverOptions(admins: readonly TeamServerState[], selected: string): string {
  return admins
    .map((state) => `<option value="${escapeHtml(state.server.id)}"${state.server.id === selected ? ' selected' : ''}>`
      + `${escapeHtml(state.server.name)}</option>`)
    .join('');
}

/** Every replaced piece, for one server's answer — or for its absence. */
export function teamParts(
  state: TeamServerState,
  cell: UsageCell | undefined,
  asking: boolean,
  palette: VendorPalette,
  extras: PartsExtras = { price: NO_PRICES, now: Date.now() },
): TeamParts {
  const usage = cell?.usage;
  const status = statusLine(state, cell, asking);
  if (usage === undefined) {
    return { ...EMPTY_PARTS, status, badges: badges(state, undefined), notes: NOTES };
  }
  const view = viewOf(state, usage, palette, extras);
  const column = people(view);

  return {
    status,
    badges: badges(state, usage),
    summary: summary(view),
    people: column.cards,
    idle: column.idle,
    vendors: vendors(view),
    chart: chart(view),
    notes: NOTES,
  };
}

function viewOf(state: TeamServerState, usage: Usage, palette: VendorPalette, extras: PartsExtras): View {
  const listing = listingOf(usage, extras);

  return {
    serverId: state.server.id,
    serverName: state.server.name,
    usage,
    palette,
    price: extras.price,
    now: extras.now,
    older: isOlderServer(usage),
    listing,
    listed: listing.kind === 'held' ? listing.people : undefined,
  };
}

/** No price book at all: every model unpriced — a dash, never a guess. */
const NO_PRICES: ListPrice = () => undefined;

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

function tile(key: string, value: string, sub: string): string {
  return `<div><div class="k">${key}</div><div class="v num">${escapeHtml(value)}</div><div class="s num">${sub}</div></div>`;
}

/** What one server's answer is drawn with: the figures, who is signed in, the prices, the colours and the clock. */
interface View extends PeopleView {
  readonly usage: Usage;
  readonly serverName: string;
  readonly listing: Listing;
  /** The people listing when it is held, else undefined. */
  readonly listed: readonly PersonListing[] | undefined;
}

/** What stands where the people listing would be — asking, the failure, or the older server's sentence. */
function listingMissing(view: View): string {
  const missing: Readonly<Record<Listing['kind'], () => string>> = {
    held: () => '',
    asking: () => `asking ${view.serverName}…`,
    failed: () => `the people listing could not be read: ${view.listing.kind === 'failed' ? view.listing.problem : ''}`,
    older: () => notYet(view.usage),
  };

  return missing[view.listing.kind]();
}

function summary(view: View): string {
  const usage = view.usage;
  const total = totalsOf(usage.vendors);
  const active = (usage.people ?? []).filter((person) => totalsOf(person.vendors).runs > 0).length;
  const cost = totalCost(usage.vendors.map((row) => vendorCost(row, view.price)));

  return `<div class="team-summary">${[
    tile('Launches', shortNumber(total.runs), total.failed > 0 ? `${shortNumber(total.failed)} failed` : 'none failed'),
    tile('Tokens', shortNumber(total.tokensIn + total.tokensOut),
      `${shortNumber(total.tokensIn)} in · ${shortNumber(total.tokensOut)} out`),
    tile('~ List price', costText(cost), pricedSub(usage)),
    tile('People', String(active), peopleSub(view)),
  ].join('')}</div>`;
}

function pricedSub(usage: Usage): string {
  return isOlderServer(usage) ? `an estimate, never a bill — ${escapeHtml(notYet(usage))}` : 'an estimate at list prices, never a bill';
}

function peopleSub(view: View): string {
  return view.listed === undefined
    ? `signed in with no runs: ${escapeHtml(listingMissing(view))}`
    : `+ ${joinPeople(view.usage.people ?? [], view.listed).idle.length} signed in with no runs`;
}

/** The people column: the cards, and the collapsed list of the signed-in who spent nothing. */
function people(view: View): { readonly cards: string; readonly idle: string } {
  const joined = joinPeople(view.usage.people ?? [], view.listed ?? []);
  const idle = view.listed === undefined
    ? `<details class="quiet" data-fold="${escapeHtml(`${view.serverId}|idle`)}"><summary>Signed in, no recorded runs in this window</summary>`
      + `<div class="hint">Who is signed in: ${escapeHtml(listingMissing(view))}.</div></details>`
    : idleList(view.serverId, joined.idle, view.now);

  return { cards: peopleCards(joined.cards, view), idle };
}

/** A vendor's models, each with its launches — or what stands where they would be on an older server. */
function models(row: VendorUsage, usage: Usage): string {
  return row.models === undefined
    ? `<span class="model hint">models: ${escapeHtml(notYet(usage))}</span>`
    : `<span class="model">${row.models.map((one) => `${escapeHtml(one.model.length > 0 ? one.model : 'unknown')} ${shortNumber(one.runs)}`).join(', ')}</span>`;
}

function vendors(view: View): string {
  const usage = view.usage;
  if (usage.vendors.length === 0) {
    return '<div class="quiet">No vendor ran in this window.</div>';
  }

  return usage.vendors.map((row) => `<div class="vendor" style="--vc:${escapeHtml(view.palette(row.vendor))}">`
    + `<div class="vhead"><b>${escapeHtml(row.vendor)}</b>${models(row, usage)}<span class="spacer"></span>`
    + `<span class="num">${escapeHtml(costText(vendorCost(row, view.price)))}</span></div>`
    + `<div class="vline num">${shortNumber(row.runs)} launches · ${shortNumber(row.failed)} failed · `
    + `${shortNumber(row.tokensIn)} in · ${shortNumber(row.tokensOut)} out</div></div>`).join('');
}

function chart(view: View): string {
  const daily = view.usage.daily;

  return '<div class="chart"><div class="colhead"><h2>Launches per day</h2><span class="spacer"></span>'
    + '<span class="hint">last 30 UTC days</span></div>'
    + (daily === undefined ? `<div class="hint">The chart ${escapeHtml(notYet(view.usage))}.</div>` : launchesChart(daily, view.palette, view.serverId))
    + '</div>';
}

const NOTES = '<div><b>Launches</b> are runs of a vendor\'s CLI, not review rounds: one round launches several reviewers, and '
  + 'a retry is another launch. Rounds are counted here once the server records which round a run belonged to.</div>'
  + '<div><b>~$</b> is an estimate at each model\'s public list price (OpenRouter, then LiteLLM), never a bill — the company '
  + 'pays flat subscriptions. A vendor\'s own reported cost is used where it gave one. A model no list prices is a dash, and a '
  + 'total that includes one is marked ≥. The server\'s lines carry no cached-token count yet, so every input token is priced '
  + 'at the full input rate and an estimate over cache-heavy input overstates.</div>'
  + '<div><b>Last seen</b> is when a session was last used, which the server records to the hour.</div>'
  + '<div>The windows are the server\'s own: trailing, and counted in UTC — <b>Today</b> here is not since your midnight.</div>';

/** What the page is told before anything is known — no tab, which is also what a non-admin is told. */
export const NO_TEAM_PUSH: TeamPush = { admin: false, servers: '', selected: '', window: 'week', readUtc: '', parts: EMPTY_PARTS };
