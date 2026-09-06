import { chmod, mkdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  Catalog,
  ServerResult,
  createSession,
  deleteSession,
  fetchCatalog,
  fetchClientConfig,
} from './teamServerApi';
import {
  TeamServer,
  applicationIdOf,
  canonicalTeamServerUrl,
  noProviderMessage,
  offerableProviders,
  tokenFileName,
} from './teamServers';

/**
 * Signing in and out of a Team server: the half that touches `vscode` and the disk.
 *
 * <p>The wire is `teamServerApi.ts` and the pure rules are `teamServers.ts`, both testable without an
 * extension host. What is here is what cannot be: minting a Microsoft token, writing a file only its
 * owner can read, and remembering who is signed in.</p>
 */

/** What this machine remembers about one server, beside the token file. */
export interface SignedIn {
  readonly email: string;
  readonly expiresUtc: string;
}

/** When to renew without asking anybody. Two days is enough slack for a weekend. */
export const RENEW_WITHIN_MS = 2 * 24 * 60 * 60 * 1000;

const STATE_PREFIX = 'teamServer:';

export function signedInKey(serverId: string): string {
  return `${STATE_PREFIX}${serverId}`;
}

/**
 * Whether a session is close enough to expiry to renew it quietly.
 *
 * <p>Pure and exported so the boundary is tested rather than trusted: an off-by-one here is a person
 * being signed out mid-review.</p>
 */
export function needsRenewal(expiresUtc: string, nowMs: number): boolean {
  const expiry = Date.parse(expiresUtc);
  if (Number.isNaN(expiry)) {
    // Unreadable is treated as due: renewing early costs one silent request, and being wrong the
    // other way costs a person their session in the middle of something.
    return true;
  }

  return expiry - nowMs < RENEW_WITHIN_MS;
}

/**
 * Write the token where the MCP shim will look for it, readable only by its owner.
 *
 * <p>The path is derived by the SAME rule the C# side uses — see
 * `shared/team-server-url-vectors.json`, which both suites assert.</p>
 */
export async function writeToken(dataDir: string, url: string, token: string): Promise<void> {
  const folder = join(dataDir, 'servers');
  await mkdir(folder, { recursive: true });
  const path = join(folder, tokenFileName(url));
  await writeFile(path, token, { mode: 0o600 });
  if (process.platform !== 'win32') {
    // `mode` on writeFile only applies when the file is CREATED, so a token replacing an older one
    // would otherwise keep whatever mode that one had.
    await chmod(folder, 0o700);
    await chmod(path, 0o600);
  }
}

export async function deleteToken(dataDir: string, url: string): Promise<void> {
  await rm(join(dataDir, 'servers', tokenFileName(url)), { force: true });
}

/**
 * What this machine remembers between windows — the slice of VS Code's `Memento` used here.
 *
 * <p>Declared rather than imported, so this module needs no `vscode` at runtime and the whole
 * sign-in flow is testable in plain Node. The extension passes the real thing.</p>
 */
export interface StateStore {
  get<T>(key: string): T | undefined;
  update(key: string, value: unknown): Promise<void>;
}

/** The pieces of the editor this needs, named so the flow can be read without them. */
export interface AuthHost {
  readonly dataDir: string;
  readonly state: StateStore;
  getSession(scope: string, interactive: boolean): Promise<string | undefined>;
  confirmApplication(server: TeamServer, applicationId: string): Promise<boolean>;
  say(message: string): void;
  /**
   * How to reach the network. The extension leaves it unset and gets the real `fetch`.
   *
   * <p>It is on the host rather than a parameter because the host is already the seam for everything
   * that cannot run in a test — and the sign-in flow is where most of this epic's security decisions
   * live, so all of them being reachable from a test is worth one field.</p>
   */
  readonly fetchImpl?: typeof fetch | undefined;
}

export type SignInOutcome =
  | { readonly ok: true; readonly signedIn: SignedIn }
  | { readonly ok: false; readonly message: string };

/**
 * Sign in to one server.
 *
 * <p><b>The order is the security.</b> The server is asked what it wants BEFORE any token exists, its
 * answer is refused unless it names `api://<app>/coai.access`, and the first time a given server asks
 * for a token the person is shown which application they are about to consent to. Only then is a
 * token minted, and it goes to that server and nowhere else.</p>
 *
 * <p><b>A half-finished sign-in is undone.</b> If the session is created but the token cannot be
 * written, the session is deleted again — otherwise a live session exists on the server that this
 * machine has no way to use, and no way to end. Raised on the plan round.</p>
 */
export async function signIn(
  server: TeamServer,
  host: AuthHost,
  interactive = true,
): Promise<SignInOutcome> {
  const config = await fetchClientConfig(server.url, host.fetchImpl);
  if (!config.ok) {
    return { ok: false, message: `${server.name}: ${config.message}` };
  }

  const providers = offerableProviders(config.value.providers ?? []);
  if (providers.length === 0) {
    return { ok: false, message: noProviderMessage(server.name, config.value.providers ?? []) };
  }

  const scope = config.value.microsoftScope;
  if (interactive && !(await confirmedOnce(server, scope, host))) {
    return { ok: false, message: `${server.name}: sign-in cancelled.` };
  }

  const idpToken = await host.getSession(scope, interactive);
  if (idpToken === undefined) {
    return {
      ok: false,
      message: interactive
        ? `${server.name}: no Microsoft account was chosen, so nothing was signed in.`
        : `${server.name}: your Microsoft session has expired — sign in again.`,
    };
  }

  const session = await createSession(server.url, idpToken, host.fetchImpl);
  if (!session.ok) {
    return { ok: false, message: `${server.name}: ${refusal(session)}` };
  }

  try {
    await writeToken(host.dataDir, server.url, session.value.token);
  } catch (e) {
    // The session exists on the server and this machine cannot use it. Ending it is the only way
    // not to leave a credential nobody can reach and nobody can revoke.
    await deleteSession(server.url, session.value.token, host.fetchImpl);
    const why = e instanceof Error ? e.message : String(e);

    return {
      ok: false,
      message: `${server.name}: signed in, but the token could not be saved (${why}), so the `
        + 'session was ended again rather than left open. Check that '
        + `${join(host.dataDir, 'servers')} is writable.`,
    };
  }

  const signedIn: SignedIn = { email: session.value.email, expiresUtc: session.value.expiresUtc };
  await host.state.update(signedInKey(server.id), signedIn);

  return { ok: true, signedIn };
}

/**
 * Ask once, per server, before a token is ever minted for it.
 *
 * <p>The application id is the server's to choose, and the scope check cannot say whether that
 * application is one the company owns — only a person can. Asked once and remembered, because asking
 * on every sign-in trains people to click through it.</p>
 */
async function confirmedOnce(server: TeamServer, scope: string, host: AuthHost): Promise<boolean> {
  const key = `${signedInKey(server.id)}:trusted`;
  const application = applicationIdOf(scope);
  if (host.state.get<string>(key) === application) {
    return true;
  }

  if (!(await host.confirmApplication(server, application))) {
    return false;
  }

  await host.state.update(key, application);

  return true;
}

function refusal(result: ServerResult<unknown>): string {
  if (result.ok) {
    return '';
  }

  return result.status === 403
    ? 'that account is outside the company domain this server allows.'
    : result.message;
}

/**
 * Give up a session: on the server, then on this machine.
 *
 * <p>Best effort against the SERVER — one that is down must not be able to keep somebody signed in
 * locally for ever. Not best effort against the DISK: a token left behind is a usable credential, so
 * a failure to delete it is said out loud with the path in it.</p>
 */
export async function signOut(
  server: TeamServer,
  host: AuthHost,
  token: string,
): Promise<{ readonly ok: boolean; readonly message: string }> {
  if (token.length > 0) {
    await deleteSession(server.url, token, host.fetchImpl);
  }

  await host.state.update(signedInKey(server.id), undefined);

  try {
    await deleteToken(host.dataDir, server.url);
  } catch (e) {
    const why = e instanceof Error ? e.message : String(e);

    return {
      ok: false,
      message: `${server.name}: signed out on the server, but its token file could not be deleted `
        + `(${why}). Delete ${join(host.dataDir, 'servers', tokenFileName(server.url))} by hand — `
        + 'until then it is a credential sitting on this machine.',
    };
  }

  return { ok: true, message: `${server.name}: signed out.` };
}

/**
 * Renew quietly when a session is nearly out, without ever showing a prompt.
 *
 * <p><b>`clearSessionPreference` must be off here.</b> It forces the provider to ignore the cached
 * account and ask, which `createIfNone: false` forbids — so the pair can only ever return nothing,
 * and the extension would conclude the identity session was gone and sign the person out. Weekly.
 * That is exactly what silent renewal exists to prevent, and the plan round caught it before it was
 * written.</p>
 */
export async function renewIfDue(
  server: TeamServer,
  host: AuthHost,
  nowMs: number = Date.now(),
): Promise<SignInOutcome | undefined> {
  const current = host.state.get<SignedIn>(signedInKey(server.id));
  if (current === undefined || !needsRenewal(current.expiresUtc, nowMs)) {
    return undefined;
  }

  return signIn(server, host, false);
}

/** What one server's catalog says right now, or why it could not be asked. */
export async function catalogOf(
  server: TeamServer,
  token: string,
  fetchImpl?: typeof fetch,
): Promise<ServerResult<Catalog>> {
  return fetchCatalog(canonicalTeamServerUrl(server.url), token, fetchImpl);
}
