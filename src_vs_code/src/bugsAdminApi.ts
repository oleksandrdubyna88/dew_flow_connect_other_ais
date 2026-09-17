/**
 * The admin API of `coai-bugs`, as this extension asks it.
 *
 * <p><b>Every answer the server can give is a CASE, not an exception.</b> The plan round found five
 * states the plan had not listed — a server that is down, a 429, a 400, a 404 on revoke and a
 * revoke that changed nothing — and each of them sends a person somewhere different. A client that
 * throws on all of them makes the tab say one thing for five situations, and the one it says
 * ("something went wrong") is the one that helps least.</p>
 *
 * <p><b>The 401 carries no diagnosis, deliberately.</b> `coai-bugs` answers an absent
 * `COAI_BUGS_ADMIN_KEYS`, a wrong key and a revoked one with the same status and the same body, so
 * that the endpoint cannot be asked whether administration is enabled. This client therefore has
 * ONE rejected case and the tab must not invent a reason for it: telling somebody their key is
 * invalid when the server has none configured sends them to re-enter a key that was always
 * correct.</p>
 *
 * <p><b>A cursor is a TOKEN, never composed here.</b> `nextBefore` is opaque and forward-only; this
 * client passes back exactly what it was handed. Composing one is a 400 by design — the server
 * refuses a cursor a caller invented rather than answering it with an empty page, because an empty
 * page reads like the end of the list.</p>
 */

/** One key, as the listing gives it. No key value and no hash: the wire has no field for either. */
export interface KeyRow {
  readonly id: string;
  readonly note: string;
  readonly createdUtc: string;
  /** Absent while the key is in force. */
  readonly revokedUtc?: string;
  /** `yyyy-MM`, or absent for never used — never a date and never a clock time. */
  readonly lastSeenMonth?: string;
  readonly sent: number;
  readonly waiting: number;
}

/** A page of keys. `nextBefore` is present only while another page may exist. */
export interface KeysPage {
  readonly items: readonly KeyRow[];
  readonly limit: number;
  readonly total: number;
  readonly nextBefore?: string;
}

/** The one response in this product that carries a key, and it carries it once. */
export interface IssuedKey {
  readonly id: string;
  readonly key: string;
  readonly note: string;
  readonly createdUtc: string;
}

/** What revoking came to. `changed` false means somebody else had already done it. */
export interface Revocation {
  readonly id: string;
  readonly revokedUtc: string;
  readonly changed: boolean;
}

/**
 * What asking the server came to.
 *
 * <p>A closed union rather than a value plus a thrown error, for the reason the doctrine gives: the
 * refusals here are not exceptional, they are the ordinary answers of a server that is deliberately
 * uninformative, and each one has its own sentence to show.</p>
 */
export type Answer<T> =
  | { readonly kind: 'ok'; readonly value: T }
  /** 401. The server will not say which of three causes it is, and neither may we. */
  | { readonly kind: 'rejected'; readonly why: string }
  /** 429, with the seconds the server asked for. */
  | { readonly kind: 'limited'; readonly why: string; readonly retryAfterSeconds: number }
  /** 400, and a sentence written to be read by the person who typed the thing. */
  | { readonly kind: 'refused'; readonly why: string }
  /** 404 — on revoke, a key this server does not have. */
  | { readonly kind: 'missing'; readonly why: string }
  /** No answer at all: unreachable, DNS, a timeout, a 5xx. NOT a credential problem. */
  | { readonly kind: 'unreachable'; readonly why: string };

/** How long any one request may take before it is called unreachable. */
export const TIMEOUT_MS = 10_000;

/** What a page asks for when nobody says otherwise. */
export const PAGE_SIZE = 50;

/** Where and with what. */
export interface Admin {
  /** The server's base address, e.g. `https://bugs.remsoft.dev`. */
  readonly server: string;
  /** The admin key, from `SecretStorage` and nowhere else. */
  readonly key: string;
}

/** The newest page of keys, or the page after a cursor a previous page handed over. */
export async function keys(admin: Admin, before = ''): Promise<Answer<KeysPage>> {
  const query = before.length > 0
    ? `?limit=${PAGE_SIZE}&before=${encodeURIComponent(before)}`
    : `?limit=${PAGE_SIZE}`;

  return ask<KeysPage>(admin, `/admin/keys${query}`, 'GET');
}

/**
 * Issues one key.
 *
 * <p><b>Never retried automatically, and the reason is in the plan.</b> If this request fails
 * without an answer the key may exist: the server commits it before it replies, and a retry that
 * succeeded would leave two live keys, one of which nobody knows about. The caller refreshes the
 * newest-first listing instead and lets a person see it — which is what that ordering is for.</p>
 */
export async function issue(admin: Admin, note: string): Promise<Answer<IssuedKey>> {
  return ask<IssuedKey>(admin, '/admin/keys', 'POST', { note });
}

/** Ends one key. Idempotent: a second call answers 200 with `changed: false`. */
export async function revoke(admin: Admin, id: string): Promise<Answer<Revocation>> {
  return ask<Revocation>(admin, `/admin/keys/${encodeURIComponent(id)}/revoke`, 'POST');
}

/**
 * One request, and every way it can come back.
 *
 * <p>The body of a refusal is `{ "why": "..." }` — camel-cased, from routes and gates alike, since
 * story 2 put the naming policy on the serializer context rather than on each call site. When a
 * body cannot be read at all the status still decides the case, because a server that answered 401
 * with an empty body is still refusing a credential.</p>
 */
async function ask<T>(
  admin: Admin,
  path: string,
  method: 'GET' | 'POST',
  body?: unknown,
): Promise<Answer<T>> {
  // Built in two pieces rather than with `body: undefined`: this repository compiles with
  // `exactOptionalPropertyTypes`, under which an explicit `undefined` is NOT the same as an absent
  // property — and `fetch` declares `body` as present-or-absent.
  const sending: RequestInit = {
    method,
    headers: headers(admin, body !== undefined),
    signal: AbortSignal.timeout(TIMEOUT_MS),
  };
  if (body !== undefined) {
    sending.body = JSON.stringify(body);
  }

  let response: Response;
  try {
    response = await fetch(`${trimmed(admin.server)}${path}`, sending);
  } catch (error_: unknown) {
    // Unreachable, refused, DNS, or the timeout above. Emphatically not a credential problem: that
    // misreading is what sends somebody to rotate a key that was working.
    return { kind: 'unreachable', why: because(error_) };
  }

  if (response.ok) {
    return read<T>(response);
  }

  return refusal(response, await why(response));
}

/** Which case a failing status is. */
function refusal<T>(response: Response, sentence: string): Answer<T> {
  if (response.status === 401) {
    return { kind: 'rejected', why: sentence };
  }

  if (response.status === 429) {
    return { kind: 'limited', why: sentence, retryAfterSeconds: seconds(response) };
  }

  if (response.status === 400) {
    return { kind: 'refused', why: sentence };
  }

  if (response.status === 404) {
    return { kind: 'missing', why: sentence };
  }

  // A 5xx is the server failing rather than refusing, which is the same thing to a reader as not
  // being able to reach it at all — and the status is carried so it is not mistaken for a network
  // fault when somebody reads the sentence.
  return { kind: 'unreachable', why: `the server answered ${response.status}: ${sentence}` };
}

/** The body of a success, or the one failure that is not the server's fault. */
async function read<T>(response: Response): Promise<Answer<T>> {
  try {
    return { kind: 'ok', value: (await response.json()) as T };
  } catch (error_: unknown) {
    return { kind: 'unreachable', why: `the server's answer could not be read: ${because(error_)}` };
  }
}

/** The server's own sentence, or a stand-in when it sent none. */
async function why(response: Response): Promise<string> {
  try {
    const said = (await response.json()) as { why?: unknown };

    return typeof said.why === 'string' && said.why.length > 0
      ? said.why
      : `the server answered ${response.status} and said nothing`;
  } catch {
    return `the server answered ${response.status} and said nothing`;
  }
}

/** What `Retry-After` asked for, or the window the server documents. */
function seconds(response: Response): number {
  const asked = Number(response.headers.get('retry-after'));

  return Number.isFinite(asked) && asked > 0 ? asked : 60;
}

function headers(admin: Admin, sending: boolean): Record<string, string> {
  const carried: Record<string, string> = {
    authorization: `Bearer ${admin.key}`,
    accept: 'application/json',
  };
  if (sending) {
    carried['content-type'] = 'application/json';
  }

  return carried;
}

/** A trailing slash on the setting and a leading one on the path would make `//admin`. */
function trimmed(server: string): string {
  return server.replace(/\/+$/u, '');
}

function because(error_: unknown): string {
  return error_ instanceof Error ? error_.message : String(error_);
}
