import { canonicalTeamServerUrl, isSafeAdvertisedScope, teamServerEndpoint } from './teamServers';

/**
 * Every request this extension makes to a Team server.
 *
 * <p>Pure in the sense that matters: it imports no `vscode` and touches no disk, so every branch is
 * testable with a stubbed `fetch`. The `vscode`-facing half — minting a Microsoft token, writing the
 * token file, telling the panel — is `teamServerAuth.ts`.</p>
 *
 * <p><b>Every call is bounded and none of them can hang the panel.</b> The panel renders from cache
 * and a fetch repaints when it lands; a server that never answers becomes a sentence, never a
 * spinner that outlives the person's patience. Raised on the plan round.</p>
 */

/** Why a URL was refused before anything was sent. */
export const INSECURE = 'a Team server must be https, or http on this machine — a token sent in the '
  + 'clear over a network is a token anybody on it can take';

/** How long any single request may take before it is abandoned. */
export const REQUEST_TIMEOUT_MS = 10_000;

/** The header the server judges BEFORE the token, answering 426 when this client is too old. */
export const CONTRACT_HEADER = 'X-Coai-Contract';

/** What this client speaks. Must match `RemoteAsk.ContractVersion` in C#. */
export const CONTRACT_VERSION = 1;

/**
 * The oldest server contract this panel can work with.
 *
 * <p>The mirror of the server's `Coai:MinimumClientContract`, and it carries the same rule: **raise
 * it only when an older server would be MISREAD**, never merely because a newer one exists. The
 * value of the mechanism is that it stays quiet until it matters, and a warning that appears because
 * somebody bumped a number is one people learn to scroll past before the day it is true.</p>
 *
 * <p>One, because nothing has moved yet. The server's own comment argues for building the mechanism
 * before the first breaking change — "the day a response shape moves, the old clients are already in
 * the field with no way to say what they speak" — and this half is subject to exactly that: the day
 * it matters, the panels that cannot read the header are already installed.</p>
 */
export const SERVER_CONTRACT_REQUIRED = 1;

/**
 * What the server said it speaks, from a response it already sent.
 *
 * <p>Three answers, and the difference between the last two is the whole point. A number is what it
 * speaks. `0` is a server that ANSWERED and named nothing — genuinely old, since every release since
 * the mechanism landed sets the header. `undefined` is "not known": no response arrived at all, or
 * what arrived was not a number, and neither of those is evidence about the server's age. Recording
 * a dropped connection as `0` would flash a skew warning on every network blip.</p>
 */
function contractOf(response: Response): number | undefined {
  const said = response.headers.get(CONTRACT_HEADER);
  if (said === null) {
    return 0;
  }

  // Canonical decimal digits or nothing. `Number()` alone would have accepted the whole zoo a proxy
  // can put on a wire — `Number('')` and `Number(' ')` are BOTH 0, which is the one wrong answer
  // available here: an empty header would have been read as "this server is ancient" and warned
  // about a healthy one. `0x10` is 16 and `1e2` is 100 for good measure. Anything that is not plain
  // digits is something other than this server answering, and a guess dressed as a fact is worse
  // than saying nothing. Raised by four reviewers on the code round.
  if (!/^\d+$/.test(said.trim())) {
    return undefined;
  }

  const parsed = Number.parseInt(said.trim(), 10);

  return Number.isSafeInteger(parsed) ? parsed : undefined;
}

/**
 * What a server said, or why it could not be asked.
 *
 * <p>`status` is on BOTH arms. A success used to discard it, which meant no caller could tell
 * `201 Created` from any other 2xx — and the server's own test asserts exactly `201` for a minted
 * session, so the two halves of the contract could disagree about the code while agreeing about
 * the body. CodeRabbit asked for it on the contract suite's pull request; nothing pays for it but
 * one field.</p>
 */
export type ServerResult<T> =
  | { readonly ok: true; readonly status: number; readonly contract: number | undefined; readonly value: T }
  | { readonly ok: false; readonly status: number; readonly contract: number | undefined; readonly message: string };

export interface ClientConfig {
  readonly microsoftScope: string;
  readonly providers: readonly string[];
}

export interface SlotSummary {
  readonly total: number;
  readonly ready: number;
  readonly coolingDown: number;
  readonly needsSignIn: number;
}

export interface CatalogVendor {
  readonly id: string;
  readonly runtime: string;
  readonly models: readonly string[];
  readonly slots: SlotSummary;
}

export interface Catalog {
  readonly serverVersion: string;
  readonly isAdmin: boolean;
  readonly vendors: readonly CatalogVendor[];
  readonly error: string;
}

export interface Session {
  readonly token: string;
  readonly expiresUtc: string;
  readonly email: string;
}

export interface VendorUsage {
  readonly vendor: string;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly runs: number;
  readonly failed: number;
  readonly seconds: number;
  readonly costUsd?: number;
}

export interface Usage {
  readonly window: string;
  readonly vendors: readonly VendorUsage[];
}

/** The bearer this machine holds for one server, or empty when it has never signed in. */
export type TokenOf = (url: string) => string;

interface Attempt {
  readonly method?: string | undefined;
  readonly token?: string | undefined;
  readonly body?: unknown;
  readonly fetchImpl?: typeof fetch | undefined;
}

/**
 * One request, with a deadline, translated into an answer rather than an exception.
 *
 * <p>A network failure and a refusal are both ordinary here: the panel must render either as a
 * sentence beside the server it is about. The one thing it may never do is throw into a render.</p>
 */
export async function ask<T>(
  url: string,
  route: string,
  attempt: Attempt = {},
): Promise<ServerResult<T>> {
  // Checked HERE rather than at the call sites, which is the whole point: it used to live only in
  // `fetchClientConfig`, so a server URL edited to plain http in settings.json after signing in
  // would have had the catalog and usage calls carry the bearer token in the clear. Two reviewers
  // found it. The measure belongs on the road in, not at each door.
  if (!isHttpsOrLoopback(url)) {
    return { ok: false, status: 0, contract: undefined, message: INSECURE };
  }

  const controller = new AbortController();
  const deadline = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  // What the server said about itself, if it got as far as saying anything. Held outside the `try`
  // so a failure AFTER the response arrived still carries it.
  let spoken: number | undefined;
  const headers: Record<string, string> = { [CONTRACT_HEADER]: String(CONTRACT_VERSION) };
  if (attempt.token !== undefined && attempt.token.length > 0) {
    headers['Authorization'] = `Bearer ${attempt.token}`;
  }
  if (attempt.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  try {
    const send = attempt.fetchImpl ?? fetch;
    const request: RequestInit = {
      method: attempt.method ?? 'GET',
      headers,
      signal: controller.signal,
      // A configured server could otherwise redirect `/api/catalog` to an origin it does not own and
      // `fetch` would forward the Authorization header there — or, from `/api/session`, the Microsoft
      // token in the body. There is no legitimate redirect on this API, so any redirect is refused
      // rather than followed to a host nobody confirmed.
      redirect: 'error',
    };
    if (attempt.body !== undefined) {
      request.body = JSON.stringify(attempt.body);
    }
    const response = await send(teamServerEndpoint(url, route), request);
    // Read BEFORE the body. `response.text()` can throw on a truncated or mis-encoded payload, and
    // the catch arm below would then answer `undefined` for a server that had already said what it
    // speaks — losing the number on exactly the call that most wants it.
    spoken = contractOf(response);
    const text = await response.text();
    if (!response.ok) {
      return { ok: false, status: response.status, contract: spoken, message: said(text, response.status) };
    }

    return {
      ok: true,
      status: response.status,
      contract: spoken,
      value: (text.length > 0 ? JSON.parse(text) : {}) as T,
    };
  } catch (e) {
    // Includes the deadline: an aborted request is a server that did not answer, which is exactly
    // what a person needs told, and is not different in kind from one that refused the connection.
    return { ok: false, status: 0, contract: spoken, message: reason(e) };
  } finally {
    clearTimeout(deadline);
  }
}

/** The server's own sentence when it sent one, else something short and true. */
function said(text: string, status: number): string {
  try {
    const parsed = JSON.parse(text) as { error?: unknown };
    if (typeof parsed.error === 'string' && parsed.error.length > 0) {
      return parsed.error;
    }
  } catch {
    // Not JSON: something in front of the server can answer with an HTML error page.
  }

  return text.trim().length > 0 ? cut(text.trim(), 200) : `the server answered ${status}`;
}

function reason(e: unknown): string {
  if (e instanceof Error && e.name === 'AbortError') {
    return `it did not answer within ${REQUEST_TIMEOUT_MS / 1000}s`;
  }

  return e instanceof Error ? e.message : String(e);
}

function cut(text: string, max: number): string {
  const single = text.replace(/[\r\n]+/g, ' ');

  return single.length <= max ? single : `${single.slice(0, max)}…`;
}

/**
 * What a server says about signing in — the FIRST thing asked of any server, and unauthenticated.
 *
 * <p>The advertised scope is checked here rather than at the call site, because this is the boundary
 * where a stranger's answer becomes something this extension acts on. A scope that is not
 * `api://<guid>/coai.access` is refused outright: without that, a server a person added could name
 * any resource and have the extension mint a Microsoft token for it.</p>
 */
export async function fetchClientConfig(
  url: string,
  fetchImpl?: typeof fetch,
): Promise<ServerResult<ClientConfig>> {
  const answer = await ask<ClientConfig>(url, 'api/client-config', { fetchImpl });
  if (!answer.ok) {
    return answer;
  }

  if (!isSafeAdvertisedScope(answer.value.microsoftScope)) {
    return {
      ok: false,
      status: 0,
      contract: answer.contract,
      message: `the Team server at ${canonicalTeamServerUrl(url)} asked for a sign-in permission this `
        + `extension will not request (${cut(String(answer.value.microsoftScope), 80)}). It must be `
        + 'api://<application id>/coai.access.',
    };
  }

  return answer;
}

/**
 * Whether a URL may carry a bearer token.
 *
 * <p>https anywhere, http only on this machine. The exception exists because a developer running the
 * server locally has no certificate and no exposure; everything else does.</p>
 */
export function isHttpsOrLoopback(url: string): boolean {
  try {
    const parsed = new URL(canonicalTeamServerUrl(url));
    if (parsed.protocol === 'https:') {
      return true;
    }

    return parsed.protocol === 'http:'
      && ['localhost', '127.0.0.1', '::1', '[::1]'].includes(parsed.hostname);
  } catch {
    return false;
  }
}

/**
 * Trade an identity-provider token for a session on this server.
 *
 * <p><b>The token travels in the Authorization header, and only there.</b> This route is
 * authorised like every other one: the server resolves the caller from
 * `Request.Headers.Authorization` before the handler runs, and reads nothing out of the request
 * body. Posting the token as `{ token }` instead — which is what 0.31.1 shipped — is answered
 * `401` with an empty body before any JWT scheme is even attempted, so the failure names neither
 * the token nor the reason.</p>
 */
export function createSession(
  url: string,
  idpToken: string,
  fetchImpl?: typeof fetch,
): Promise<ServerResult<Session>> {
  return ask<Session>(url, 'api/session', {
    method: 'POST',
    token: idpToken,
    fetchImpl,
  });
}

/** Give up this session. Best effort: a server that is down must not keep somebody signed in here. */
export function deleteSession(
  url: string,
  token: string,
  fetchImpl?: typeof fetch,
): Promise<ServerResult<unknown>> {
  return ask<unknown>(url, 'api/session', { method: 'DELETE', token, fetchImpl });
}

/**
 * What this server offers, and how healthy it is.
 *
 * <p>This is the call the whole panel section is drawn from, and the plan for epic 3 forgot to
 * specify it at all — two stories depended on a "cached catalog" that nothing ever filled. The plan
 * round caught it as blocking.</p>
 */
export function fetchCatalog(
  url: string,
  token: string,
  fetchImpl?: typeof fetch,
): Promise<ServerResult<Catalog>> {
  return ask<Catalog>(url, 'api/catalog', { token, fetchImpl });
}

export function fetchUsage(
  url: string,
  token: string,
  window: string,
  scope: 'me' | 'company',
  fetchImpl?: typeof fetch,
): Promise<ServerResult<Usage>> {
  return ask<Usage>(url, `api/usage?window=${encodeURIComponent(window)}&scope=${scope}`, {
    token,
    fetchImpl,
  });
}
