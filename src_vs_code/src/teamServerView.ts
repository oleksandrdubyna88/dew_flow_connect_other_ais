import { Catalog, CatalogVendor, SlotSummary } from './teamServerApi';
import { TeamServer, canonicalTeamServerUrl } from './teamServers';

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
  /** The signed-in account, or empty when this machine has not signed in. */
  readonly email: string;
  /** The last catalog this machine managed to fetch, or undefined when it never has. */
  readonly catalog?: Catalog | undefined;
  /** Why the last attempt failed, or empty when it did not. */
  readonly problem: string;
  /** Whether {@link catalog} predates the last failed attempt. */
  readonly stale: boolean;
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
  if (state.email.length === 0) {
    return 'Not signed in.';
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

function escape(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/** One server's rows. */
export function teamServerRow(state: TeamServerState): string {
  const signedIn = state.email.length > 0;
  const id = escape(state.server.id);
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
      ? `<button class="run" data-command="signOutTeamServer" data-id="${id}">Sign out</button>`
      : `<button class="run" data-command="signInTeamServer" data-id="${id}">Sign in</button>`}
    <button class="run" data-command="removeTeamServer" data-id="${id}">Remove</button>
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
