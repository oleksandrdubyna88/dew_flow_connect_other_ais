import { escapeHtml } from './escapeHtml';
import { costText, Estimate, ListPrice, totalCost, vendorCost } from './teamCost';
import { PersonListing, PersonUsage, VendorUsage } from './teamServerApi';
import { shortNumber } from './usage';
import { VendorPalette } from './vendorColour';

/**
 * The people on the Team server tab (todo/PLAN_team_usage_by_person.md, stories 1.3 and 3.1): who spent what, joined
 * with who is signed in.
 *
 * <p><b>Every usage person is kept.</b> The company answer is the record of spending; the people listing (`GET
 * /api/people`) only adds a display name and when the person was last seen. Joined by case-insensitive email — the
 * server already groups one person's casings (D2), and the client never regroups the spending, it only looks a name up.
 * Somebody who spent and holds no session now (a raw identity-provider token, or a session since expired) is a card
 * with no name; somebody signed in who spent nothing in this window is not a card, but a line in the collapsed list
 * (D3).</p>
 *
 * <p><b>"Last seen within the hour".</b> The server stamps a session's last use at ONE-HOUR resolution, so anything
 * finer would be a precision it does not have.</p>
 *
 * <p>Every name and email is the server's text — a display name is identity-provider text — and is escaped; a fold key
 * is compared as an attribute value on the page, never built into a selector.</p>
 */

export interface Totals {
  readonly runs: number;
  readonly failed: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
}

export function totalsOf(rows: readonly VendorUsage[]): Totals {
  return rows.reduce((sum, row) => ({
    runs: sum.runs + row.runs,
    failed: sum.failed + row.failed,
    tokensIn: sum.tokensIn + row.tokensIn,
    tokensOut: sum.tokensOut + row.tokensOut,
  }), { runs: 0, failed: 0, tokensIn: 0, tokensOut: 0 });
}

/** One card: the spending, and — when the person is signed in — their name and when they were last seen. */
export interface JoinedPerson {
  readonly usage: PersonUsage;
  readonly name: string;
  readonly lastUsedUtc: string;
}

/** What the cards are drawn with. */
export interface PeopleView {
  readonly serverId: string;
  readonly price: ListPrice;
  readonly palette: VendorPalette;
  /** Now, UTC milliseconds — what "last seen" is measured from. */
  readonly now: number;
}

const HOUR_MS = 60 * 60 * 1000;

function keyOf(email: string): string {
  return email.trim().toLowerCase();
}

/** A spender the listing does not name: no session now, so no name and no last use to show. */
const UNLISTED: Pick<PersonListing, 'displayName' | 'lastUsedUtc'> = { displayName: '', lastUsedUtc: '' };

/** Every usage person, with a name where the listing has one; and the listed people who spent nothing in the window. */
export function joinPeople(
  spent: readonly PersonUsage[],
  listed: readonly PersonListing[],
): { readonly cards: readonly JoinedPerson[]; readonly idle: readonly PersonListing[] } {
  const byEmail = new Map(listed.map((one) => [keyOf(one.email), one]));
  const spenders = new Set(spent.map((one) => keyOf(one.email)));

  return {
    cards: spent.map((usage) => {
      const found = byEmail.get(keyOf(usage.email)) ?? UNLISTED;

      return { usage, name: found.displayName, lastUsedUtc: found.lastUsedUtc };
    }),
    idle: listed.filter((one) => !spenders.has(keyOf(one.email))),
  };
}

/** When a person was last seen, at the server's one-hour resolution; a stamp nobody sent is a dash. */
export function lastSeen(lastUsedUtc: string, now: number): string {
  const at = Date.parse(lastUsedUtc);
  if (Number.isNaN(at)) {
    return 'last seen —';
  }
  const hours = Math.floor((now - at) / HOUR_MS);

  return hours < 1 ? 'last seen within the hour' : (hours < 48 ? `last seen ${hours} h ago` : `last seen ${Math.floor(hours / 24)} days ago`);
}

/** Two letters for the round mark beside a card — decoration, hidden from readers. */
function initials(person: JoinedPerson): string {
  const source = person.name.trim().length > 0 ? person.name : (person.usage.email.split('@')[0] ?? '');
  const parts = source.split(/[\s._-]+/).filter((part) => part.length > 0);

  return (parts.length > 1 ? `${parts[0]![0]}${parts[1]![0]}` : source.slice(0, 2)).toUpperCase();
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

function vendorTable(rows: readonly VendorUsage[], view: PeopleView): string {
  const body = rows.map((row) => `<tr><td><i class="dot" style="background:${escapeHtml(view.palette(row.vendor))}"></i>`
    + `${escapeHtml(row.vendor)}</td><td class="num">${shortNumber(row.runs)}</td><td class="num">${shortNumber(row.failed)}</td>`
    + `<td class="num">${shortNumber(row.tokensIn)}</td><td class="num">${shortNumber(row.tokensOut)}</td>`
    + `<td class="num">${escapeHtml(costText(vendorCost(row, view.price)))}</td></tr>`).join('');

  return '<div class="tablewrap"><table class="t"><thead><tr><th scope="col">Vendor</th><th scope="col">Launches</th>'
    + '<th scope="col">Failed</th><th scope="col">In</th><th scope="col">Out</th><th scope="col">~$</th></tr></thead>'
    + `<tbody>${body}</tbody></table></div>`;
}

/** Who the card is: the name in bold and the email beside it, or the email alone when nobody listed a name. */
function who(person: JoinedPerson): string {
  return person.name.trim().length > 0
    ? `<b>${escapeHtml(person.name)}</b><span class="mail">${escapeHtml(person.usage.email)}</span>`
    : `<b>${escapeHtml(person.usage.email)}</b>`;
}

/** What the page sorts and searches a card by, as attributes — the page never builds a selector from them. */
function facts(person: JoinedPerson, total: Totals, cost: Estimate): string {
  const seen = Date.parse(person.lastUsedUtc);

  return `data-search="${escapeHtml(`${keyOf(person.usage.email)} ${person.name.toLowerCase()}`.trim())}" `
    + `data-launches="${total.runs}" data-tokens="${total.tokensIn + total.tokensOut}" `
    + `data-cost="${cost.known ? cost.usd : -1}" data-seen="${Number.isNaN(seen) ? 0 : seen}"`;
}

/**
 * One person, as a card that opens. Its fold key is the SERVER and the email, lower-cased: it keeps an open card open
 * across a push, and keeps the same email on another server a different card.
 */
export function personCard(person: JoinedPerson, view: PeopleView): string {
  const total = totalsOf(person.usage.vendors);
  const cost = totalCost(person.usage.vendors.map((row) => vendorCost(row, view.price)));
  const failed = total.failed > 0 ? `<span class="warn">${shortNumber(total.failed)} failed</span>` : '<span>no failures</span>';

  return `<details class="person" data-fold="${escapeHtml(`${view.serverId}|${keyOf(person.usage.email)}`)}" ${facts(person, total, cost)}>`
    + `<summary><span class="avatar" aria-hidden="true">${escapeHtml(initials(person))}</span>`
    + `<span class="who">${who(person)}</span>`
    + `<span class="cost num">${escapeHtml(costText(cost))}</span>`
    + `<span class="line2 num"><span>${shortNumber(total.runs)} launches</span>`
    + `<span>${shortNumber(total.tokensIn)} in · ${shortNumber(total.tokensOut)} out</span>`
    + `<span>${escapeHtml(lastSeen(person.lastUsedUtc, view.now))}</span></span>`
    + `<span class="stack" aria-hidden="true">${stack(person.usage.vendors, view.palette)}</span></summary>`
    + `<div class="detail">${vendorTable(person.usage.vendors, view)}<div class="fails">${failed}</div></div></details>`;
}

export function peopleCards(cards: readonly JoinedPerson[], view: PeopleView): string {
  return cards.length === 0
    ? '<div class="quiet">Nobody ran anything on this server in this window.</div>'
    : cards.map((person) => personCard(person, view)).join('');
}

/** The collapsed list of the signed-in who spent nothing in this window (D3, D5). */
export function idleList(serverId: string, idle: readonly PersonListing[], now: number): string {
  const rows = idle.map((one) => `<li>${escapeHtml(one.displayName.length > 0 ? one.displayName : one.email)} `
    + `<span class="hint">${escapeHtml(one.email)} · ${escapeHtml(lastSeen(one.lastUsedUtc, now))}</span></li>`).join('');

  return `<details class="quiet" data-fold="${escapeHtml(`${serverId}|idle`)}">`
    + `<summary>Signed in, no recorded runs in this window: ${idle.length}</summary>`
    + (rows.length > 0 ? `<ul>${rows}</ul>` : '<div class="hint">Everybody signed in ran something.</div>')
    + '<div class="hint">People with an unexpired session — somebody on a raw identity-provider token holds none.</div></details>';
}
