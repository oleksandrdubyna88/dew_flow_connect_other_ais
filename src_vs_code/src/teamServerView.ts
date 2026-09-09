import { Catalog, CatalogVendor, KindUsage, SlotSummary, Usage } from './teamServerApi';
import { TeamServer, canonicalTeamServerUrl } from './teamServers';
// The same comparator the coai-mcp update check uses. Two version comparisons in one panel that
// disagreed about what "newer" means is a defect waiting for a version like 0.10.0.
import { compareVersions } from './coaiInstall';
import { SERVER_CONTRACT_REQUIRED } from './teamServerApi';
// The ONE escaper, imported rather than copied. A private second copy is the anti-pattern the
// security rule names by example — "three byte-identical private copies | hardening one left the
// other two behind" — and byte-identical is exactly what this one was. Caught on the code round.
import { escapeHtml as escape } from './escapeHtml';

/**
 * The *Team servers* section, as markup.
 *
 * <p>Pure: it takes what is known and returns a string, so every sentence a person will read is a
 * unit test rather than something only reachable by signing in to a real server. The panel builds one
 * HTML string and has no `asWebviewUri` plumbing, which is why the account glyph is an inline SVG
 * rather than a file reference.</p>
 */

/** What the panel knows about one server right now. */
export interface TeamServerState {
  readonly server: TeamServer;
  /**
   * The account THIS SIDE holds a token for, or empty when it holds none.
   *
   * <p>The token file, never a shared record: `coaiDataDir()` is a path on the extension host that
   * is running, so a WSL window and a Windows one hold different files. A record that described the
   * machine is what let this row read "Signed in" in a distro where no token existed at all.</p>
   */
  readonly email: string;
  /**
   * The account signed in on ANOTHER side of this machine, when this side has none.
   *
   * <p>Empty whenever {@link email} is set — the two never both apply. It exists so the row can
   * offer the one button that fixes the situation instead of reading like nobody ever signed in.</p>
   */
  readonly elsewhere?: string | undefined;
  /** The last catalog this machine managed to fetch, or undefined when it never has. */
  readonly catalog?: Catalog | undefined;
  /** Why the last attempt failed, or empty when it did not. */
  readonly problem: string;
  /** Whether {@link catalog} predates the last failed attempt. */
  readonly stale: boolean;
  /**
   * The HTTP contract this server last said it speaks, or undefined when nothing is known.
   *
   * <p>Three states and the last two are not the same. A number is what it answered with. `0` is a
   * server that ANSWERED and named nothing, which is a real fact about its age. `undefined` is a
   * server that has not been reached, or whose answer was unreadable — recorded as nothing, because
   * a dropped connection is not evidence that a server is old, and treating it as such would flash a
   * warning on every network blip.</p>
   */
  readonly contract?: number | undefined;
  /** What this server says has been spent on it, in the window the panel is showing. */
  readonly usage?: Usage | undefined;
  /**
   * What this server is in the middle of right now, or empty.
   *
   * <p>Sign-in awaits four requests and a Microsoft prompt. Without this the row read "Not signed
   * in" the whole time and its button stayed pressable, so a person could not tell a slow sign-in
   * from one that did nothing — and pressed it again. Caught on the code round.</p>
   */
  readonly busy?: string | undefined;
}

/** Whether anybody signed in here is an admin — the only people offered *Company*. */
export function anyAdmin(states: readonly TeamServerState[]): boolean {
  return states.some((s) => s.catalog?.isAdmin === true);
}

/**
 * What one Team server says has been spent on it.
 *
 * <p>The server has already done the arithmetic — it keeps the ledger — so this renders what it
 * said rather than recomputing anything. Failed runs are counted, never filtered: a review that
 * burned ninety seconds and answered nothing spent the same as one that answered.</p>
 */
export function teamUsageBlock(
  state: TeamServerState,
  shortNumber: (n: number) => string,
): string {
  const rows = state.usage?.vendors ?? [];
  if (rows.length === 0) {
    return `<div class="ts-usage"><b>${escape(state.server.name)}</b>`
      + `<div class="empty">Nothing recorded on this server in this window.</div></div>`;
  }

  const busiest = Math.max(...rows.map((r) => r.tokensIn + r.tokensOut), 1);
  const cards = rows.map((r) => {
    const total = r.tokensIn + r.tokensOut;
    const failed = r.failed === 0 ? '' : ` · <span class="warn">${r.failed} failed</span>`;
    const width = Math.max(2, Math.round((total / busiest) * 100));

    return `<div class="spend">
  <div class="head"><span class="name">${escape(r.vendor)}</span></div>
  <div class="bar"><span style="width:${width}%"></span></div>
  <div class="figures">${shortNumber(r.tokensIn)} in · ${shortNumber(r.tokensOut)} out · ${r.runs} run(s)${failed}</div>
</div>`;
  }).join('\n');

  return `<div class="ts-usage"><b>${escape(state.server.name)}</b>
${cards}
${kindLine(state.usage?.kinds ?? [], shortNumber)}
</div>`;
}

/**
 * What the gate cost, and what asking cost — beside each other, under the vendor rows.
 *
 * <p>The owner's ruling of 2026-09-08, in the words he used: *"счиатть, отделять"*. The vendors are
 * the same vendors and the money is the same money, so one total answers neither question; and a
 * second table of the same vendors split two ways would leave a reader adding pairs of rows to get
 * back the number they already had. One line, under the rows it decomposes.</p>
 *
 * <p><b>Nothing at all when there is only one kind.</b> A window with no conversations in it is not
 * a conversation total of zero — a zero is a measurement, and this would be an absence wearing one.
 * Servers older than this field send no kinds and get the same silence, which is what makes this
 * safe to ship before every server has it.</p>
 */
export function kindLine(
  kinds: readonly KindUsage[],
  shortNumber: (n: number) => string,
): string {
  if (kinds.length < 2) {
    return '';
  }

  const parts = kinds.map((k) => {
    const tokens = shortNumber(k.tokensIn + k.tokensOut);

    return `${escape(named(k.kind))}: ${k.runs} · ${tokens} tokens`;
  });

  return `<div class="hint">${parts.join(' &middot; ')}</div>`;
}

/** What a kind is called on a page a person reads, rather than on the wire. */
function named(kind: string): string {
  return kind === 'chat' ? 'conversations' : kind === 'review' ? 'reviews' : kind;
}

/**
 * The Me / Company control, and why it is only sometimes there.
 *
 * <p>The SERVER refuses `scope=company` for anybody who is not an admin, so rendering the control
 * for everybody would be offering a button that answers 403. It is shown when a server this machine
 * is signed in to says this account is an admin.</p>
 */
export function usageScopeControl(states: readonly TeamServerState[], scope: 'me' | 'company'): string {
  if (!anyAdmin(states)) {
    return '';
  }

  return `<button class="link" data-command="teamUsageScope">`
    + `${scope === 'company' ? 'Showing the whole company — show just me' : 'Show the whole company'}`
    + `</button>`;
}

/**
 * The account glyph, inline.
 *
 * <p>An SVG rather than a codicon for the reason the CredsForDevs panel found: a `ThemeIcon` is
 * repainted in the selection colour, so a green "signed in" dot turns the same colour as everything
 * else the moment the row is selected — which is precisely when somebody is looking at it.</p>
 */
function accountGlyph(signedIn: boolean): string {
  const colour = signedIn ? '#3fb950' : '#8b949e';

  return `<svg width="12" height="12" viewBox="0 0 16 16" aria-hidden="true" focusable="false">`
    + `<circle cx="8" cy="5" r="3" fill="${colour}"/>`
    + `<path d="M2 14c0-3.3 2.7-5 6-5s6 1.7 6 5" fill="${colour}"/></svg>`;
}

/**
 * What the server's own slot counts mean for a person.
 *
 * <p>The server already computed these for its own queue, so the panel can say WHY a vendor is
 * unusable rather than that it is — and the two reasons have different owners. Accounts that are
 * signed out never come back on their own; rate-limited ones always do.</p>
 */
export function slotSentence(vendor: CatalogVendor): string {
  const slots: SlotSummary = vendor.slots ?? { total: 0, ready: 0, coolingDown: 0, needsSignIn: 0 };
  if (slots.total === 0) {
    return `${vendor.id}: no accounts — ask the operator`;
  }
  if (slots.ready > 0) {
    const cooling = slots.coolingDown > 0 ? `, ${slots.coolingDown} cooling down` : '';

    return `${vendor.id}: ${slots.ready} of ${slots.total} ready${cooling}`;
  }
  if (slots.needsSignIn > 0) {
    return `${vendor.id}: all ${slots.total} signed out — the operator must sign them in`;
  }

  return `${vendor.id}: all ${slots.total} rate-limited, they come back by themselves`;
}

/**
 * The line that says where a review actually goes.
 *
 * <p>The same disclosure a non-loopback local engine gets, for the same reason: "an external endpoint
 * is configured" is a sentence somebody skims, and "your diffs are sent to coai.example.com" is one
 * they read. A Team server ALWAYS gets it — being the company's own server makes it no less true that
 * the code leaves this machine.</p>
 */
export function disclosure(url: string): string {
  let host: string;
  try {
    host = new URL(canonicalTeamServerUrl(url)).host;
  } catch {
    host = url;
  }

  return `Every review sent here — your plan, your diffs and the file contents around them — goes to `
    + `${host}.`;
}

/** What to say about a server nobody has signed into, or one that would not answer. */
export function statusSentence(state: TeamServerState): string {
  if ((state.busy ?? '').length > 0) {
    return state.busy!;
  }
  if (state.email.length === 0) {
    return notSignedInHere(state);
  }
  if (state.problem.length > 0) {
    return state.stale
      ? `${state.problem} — showing what it last said.`
      : state.problem;
  }
  if (state.catalog === undefined) {
    return 'Signed in. Asking what it offers…';
  }

  return `Signed in. Server ${state.catalog.serverVersion}.`;
}

/**
 * Not signed in HERE — and whether that is because nobody has, or because it was somebody else's
 * window.
 *
 * <p>The middle case is the one that used to be invisible: a session signed in on Windows, a WSL
 * window that could not mint its own, and a row that said nothing about either. Saying which account
 * is waiting, and why this side could not take it, is the difference between a button somebody
 * presses and a panel somebody gives up on.</p>
 */
function notSignedInHere(state: TeamServerState): string {
  const elsewhere = state.elsewhere ?? '';
  // The PROBLEM first, whoever is or is not signed in elsewhere. It used to be reachable only when
  // another side was signed in, so with the sides separated — or simply with nobody else signed in —
  // a refused sign-in, a timeout and a server that is down all rendered as a bare "Not signed in."
  // Caught on the code round.
  if (state.problem.length === 0) {
    return elsewhere.length === 0
      ? 'Not signed in.'
      : `${elsewhere} is signed in on another side of this machine — press Sign in to use it here.`;
  }

  return elsewhere.length === 0
    ? `Not signed in — ${state.problem}`
    : `${elsewhere} is signed in on another side of this machine, and this side could not sign `
      + `itself in: ${state.problem}`;
}

/**
 * What the release line has, for the person who could act on it.
 *
 * <p><b>Admins only, and read-only on purpose.</b> A Team server is DEPLOYED, not downloaded —
 * there is no Update button here and there could not be one, because updating it means touching a
 * machine this panel has no business touching. The request was to SHOW it, and that is all this
 * does.</p>
 *
 * <p>Silent in three states, each of which would otherwise be a sentence that teaches nothing: the
 * caller is not an admin; the server has not said its version yet; or no `server-v*` release has
 * been published (the Team server is deployed by hand, so that is the ordinary state today).</p>
 */
export function publishedNote(state: TeamServerState, published: string): string {
  const running = state.catalog?.serverVersion ?? '';
  if (state.catalog?.isAdmin !== true || running.length === 0 || published.length === 0) {
    return '';
  }

  return compareVersions(published, running) > 0
    ? `⬆ ${published} is published — this one runs ${running}.`
    : `${published} is the newest published — this one is up to date.`;
}

/**
 * Said out loud when this panel needs a newer server than the one answering.
 *
 * <p>The mirror of the server's 426: it refuses a client it can no longer serve, and this reports a
 * server this panel can no longer read. Reports rather than refuses, deliberately — the panel that
 * stopped talking to a server it merely suspects would turn a warning into an outage, which is the
 * failure the server's own contract comment refuses for the other direction.</p>
 *
 * <p>Silent in the two states that are not evidence: a server that has never answered, and one that
 * is new enough. A mechanism that talks when nothing is wrong is one people learn to scroll past
 * before the day it is true.</p>
 */
export function contractNote(state: TeamServerState): string {
  const said = state.contract;
  if (said === undefined || said >= SERVER_CONTRACT_REQUIRED) {
    return '';
  }

  const speaks = said === 0
    ? 'does not say which version of the API it speaks'
    : `speaks version ${said} of the API`;

  return `⚠ This server ${speaks}, and this extension needs ${SERVER_CONTRACT_REQUIRED} or later. `
    + 'Reviews sent here may be read wrongly by one half or the other — update the Team server.';
}

/** One server's rows. */
export function teamServerRow(state: TeamServerState, published = ''): string {
  const signedIn = state.email.length > 0;
  const id = escape(state.server.id);
  // A button that cannot help while something is already in flight is disabled rather than left
  // pressable — pressing it again is what a person does when nothing appears to be happening.
  const stop = (state.busy ?? '').length > 0 ? ' disabled' : '';
  const slots = (state.catalog?.vendors ?? []).map((v) => escape(slotSentence(v))).join('; ');
  const release = publishedNote(state, published);
  const contract = contractNote(state);

  return `<div class="ts-row" data-server="${id}">
  <div class="ts-head">
    <b>${escape(state.server.name)}</b>
    <span class="ts-url">${escape(canonicalTeamServerUrl(state.server.url))}</span>
  </div>
  <div class="ts-acct">${accountGlyph(signedIn)} ${escape(signedIn ? state.email : 'no account')}</div>
  <div class="hint">${escape(statusSentence(state))}</div>
  ${contract.length > 0 ? `<div class="hint ts-warn">${escape(contract)}</div>` : ''}
  ${release.length > 0 ? `<div class="hint ts-release">${escape(release)}</div>` : ''}
  ${slots.length > 0 ? `<div class="ts-slots">${slots}</div>` : ''}
  <div class="hint ts-warn">${escape(disclosure(state.server.url))}</div>
  <div class="ts-buttons">
    ${signedIn
      ? `<button class="run" data-command="signOutTeamServer" data-id="${id}"${stop}>Sign out</button>`
      : `<button class="run" data-command="signInTeamServer" data-id="${id}"${stop}>Sign in</button>`}
    <button class="run" data-command="removeTeamServer" data-id="${id}"${stop}>Remove</button>
  </div>
</div>`;
}

/** The whole section body. */
export function teamServersBody(states: readonly TeamServerState[], published = ''): string {
  const add = `<button class="add" data-command="addTeamServer">＋&nbsp; Add a Team server</button>`;
  if (states.length === 0) {
    return `<div class="hint"><b>Nothing here yet.</b> A Team server runs the vendor CLIs on one `
      + `machine, on one company subscription, so nobody needs their own. Add one and sign in with `
      + `your work account to review through it.</div>
${add}`;
  }

  return `${states.map((s) => teamServerRow(s, published)).join('\n')}
${add}`;
}
