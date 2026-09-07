import { Catalog, CatalogVendor, SlotSummary, Usage } from './teamServerApi';
import { TeamServer, canonicalTeamServerUrl } from './teamServers';
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
</div>`;
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
 * The Team server, in the *Server* section: the address, read-only, and what is answering on it.
 *
 * <p>The address is SHOWN and not editable on purpose — pointing a live session somewhere else is a
 * sign-out, not a text edit — and it is here because this is the section a person opens to ask what
 * this side is talking to. The other half of that question, `coai-mcp`, is already above it.</p>
 *
 * <p>One block per configured server rather than one for "the" server: the panel has always allowed
 * several, and choosing one of them to display would be right only by luck.</p>
 */
export function teamServerHere(states: readonly TeamServerState[], perSide: boolean): string {
  return states.map((state) => hereBlock(state, perSide)).join('\n');
}

function hereBlock(state: TeamServerState, perSide: boolean): string {
  const id = escape(state.server.id);
  // The sign-out hint only where there is something to sign out OF. Telling somebody who is signed
  // out to sign out first is an instruction they cannot follow, and this section has no buttons at
  // all — every one of them is in *Team servers*, which is why both sentences name it. Raised on the
  // code round.
  const change = state.email.length > 0
    ? `To point this side somewhere else, sign out under <b>Team servers</b> first.`
    : `<b>Team servers</b> is where you sign in and out.`;

  return `<div class="field">
  <label for="ts-here-${id}">Team server — ${escape(state.server.name)}</label>
  <input id="ts-here-${id}" type="text" data-server-url="${id}" readonly aria-readonly="true"
         value="${escape(canonicalTeamServerUrl(state.server.url))}">
  <div class="status">${escape(hereSentence(state))}</div>
  <div class="hint">${escape(sideSentence(perSide))} ${change}</div>
</div>`;
}

/** What is answering on this side, in one line — the version being the point of it. */
export function hereSentence(state: TeamServerState): string {
  if (state.email.length === 0) {
    // Not `notSignedInHere`: that one ends in "press Sign in", and there is no such button in THIS
    // section. The sentence has to name where the button actually is. Raised on the code round.
    return signedOutHere(state);
  }
  if (state.catalog === undefined) {
    return state.problem.length > 0
      ? `Signed in as ${state.email} — ${state.problem}`
      : `Signed in as ${state.email} — connecting…`;
  }

  // A version that goes silently stale is a number that makes a dead connection look healthy, so a
  // catalog older than the last failed attempt says so rather than standing there on its own.
  const stale = state.stale ? ' (last known — it is not answering now)' : '';

  return `coai-server ${state.catalog.serverVersion}${stale} — signed in as ${state.email}.`;
}

/** The same states as {@link notSignedInHere}, worded for a section that has no buttons. */
function signedOutHere(state: TeamServerState): string {
  const elsewhere = state.elsewhere ?? '';
  if (state.problem.length > 0) {
    return elsewhere.length === 0
      ? `Not signed in — ${state.problem}`
      : `${elsewhere} is signed in on another side, and this side could not sign itself in: `
        + state.problem;
  }

  return elsewhere.length === 0
    ? 'Not signed in on this side.'
    : `Not signed in on this side — ${elsewhere} is signed in on another one.`;
}

/** Which of the two arrangements this machine is in, said rather than implied. */
export function sideSentence(perSide: boolean): string {
  return perSide
    ? 'This side keeps its own sign-in — your Windows window and each WSL distro can be signed '
      + 'in to different accounts.'
    : 'Every side of this machine shares this sign-in — a WSL window signs itself in with the '
      + 'same account, without asking.';
}

/** One server's rows. */
export function teamServerRow(state: TeamServerState): string {
  const signedIn = state.email.length > 0;
  const id = escape(state.server.id);
  // A button that cannot help while something is already in flight is disabled rather than left
  // pressable — pressing it again is what a person does when nothing appears to be happening.
  const stop = (state.busy ?? '').length > 0 ? ' disabled' : '';
  const slots = (state.catalog?.vendors ?? []).map((v) => escape(slotSentence(v))).join('; ');

  return `<div class="ts-row" data-server="${id}">
  <div class="ts-head">
    <b>${escape(state.server.name)}</b>
    <span class="ts-url">${escape(canonicalTeamServerUrl(state.server.url))}</span>
  </div>
  <div class="ts-acct">${accountGlyph(signedIn)} ${escape(signedIn ? state.email : 'no account')}</div>
  <div class="hint">${escape(statusSentence(state))}</div>
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
export function teamServersBody(states: readonly TeamServerState[]): string {
  const add = `<button class="add" data-command="addTeamServer">＋&nbsp; Add a Team server</button>`;
  if (states.length === 0) {
    return `<div class="hint"><b>Nothing here yet.</b> A Team server runs the vendor CLIs on one `
      + `machine, on one company subscription, so nobody needs their own. Add one and sign in with `
      + `your work account to review through it.</div>
${add}`;
  }

  return `${states.map(teamServerRow).join('\n')}
${add}`;
}
