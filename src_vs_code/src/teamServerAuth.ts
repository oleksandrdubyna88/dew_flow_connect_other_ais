import { chmod, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
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

/**
 * The INTENT: which account this machine — or this side of it — should be signed in as.
 *
 * <p>An intention, never evidence. What a review can actually use is a token FILE, and the two
 * disagree whenever a mint failed, the machine was offline, or another side holds another account's
 * token. {@link TokenFact} is the other half, and the panel renders THAT.</p>
 */
export interface SignedIn {
  readonly email: string;
  readonly expiresUtc: string;
}

/**
 * The FACT: whose token file THIS side is holding, and since when.
 *
 * <p>Always per side, in every mode, because a token file always is: `coaiDataDir()` is a path on
 * the extension host that is running, which for a WSL window is inside the distro. A record that
 * described "this machine" is what let the panel claim a session the shim had no token for.</p>
 */
export interface TokenFact {
  readonly email: string;
  readonly expiresUtc: string;
  /** When it was minted — compared against a sign-out elsewhere, which is why it is a number. */
  readonly mintedAtMs: number;
}

/** When to renew without asking anybody. Two days is enough slack for a weekend. */
export const RENEW_WITHIN_MS = 2 * 24 * 60 * 60 * 1000;

const STATE_PREFIX = 'teamServer:';

/**
 * Where the intent lives: shared by every side, or this side's own.
 *
 * <p>`side` empty is the SHARED record — the mode where one login serves every window of the
 * machine, which is what *Separate settings for each side* being off means for a Team server too.</p>
 */
export function signedInKey(serverId: string, side = ''): string {
  return side.length === 0 ? `${STATE_PREFIX}${serverId}` : `${STATE_PREFIX}${serverId}@${side}`;
}

/** Where the fact lives. Side-scoped in EVERY mode — see {@link TokenFact}. */
export function tokenFactKey(serverId: string, side: string): string {
  return `${STATE_PREFIX}${serverId}#${side}`;
}

/**
 * When this scope was last signed out, in ms.
 *
 * <p>Scoped with the intent, so a sign-out reaches exactly the sides that shared that login: with
 * sharing on it reaches all of them, and with each side on its own account it reaches only the one
 * that pressed the button. Without it, signing out on Windows left a live, usable token inside the
 * WSL distro and a session still running on the server.</p>
 */
export function revokedKey(serverId: string, side = ''): string {
  return `${signedInKey(serverId, side)}:revoked`;
}

/**
 * Which Microsoft application this machine has approved for one server.
 *
 * <p>SHARED in every mode, deliberately: approving an application is a fact about the SERVER, not
 * about a side, and one person approving it twice for the same server teaches them to click through
 * the dialog. It is also what makes an unattended mint acceptable — a server that changes its
 * advertised application cannot get a token minted for it silently on any side.</p>
 */
export function trustedKey(serverId: string): string {
  return `${STATE_PREFIX}${serverId}:trusted`;
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
  // The DIRECTORY is closed before the token is written into it, not after: restricting it
  // afterwards leaves a window in which the file exists under a directory anybody could list.
  if (process.platform !== 'win32') {
    await chmod(folder, 0o700).catch(() => undefined);
  }

  const path = join(folder, tokenFileName(url));
  await writeFile(path, token, { mode: 0o600 });
  if (process.platform !== 'win32') {
    // `mode` on writeFile only applies when the file is CREATED, so a token replacing an older one
    // would otherwise keep whatever mode that one had.
    await chmod(path, 0o600);
  }
}

/**
 * The token this machine holds for one server, or empty when it has never signed in.
 *
 * <p>Empty rather than an exception: "not signed in" is an ordinary state with its own sentence and
 * its own exit code, not a fault — the same rule the C# side follows.</p>
 */
export async function readToken(dataDir: string, url: string): Promise<string> {
  try {
    return (await readFile(join(dataDir, 'servers', tokenFileName(url)), 'utf8')).trim();
  } catch {
    return '';
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
  /**
   * This side of the machine, as a key component — `sideKey(thisSide(...))`.
   *
   * <p>Always this side's own identity, whatever the settings say. The token file is per side in
   * every mode, so the record that DESCRIBES it has to be too.</p>
   */
  readonly side?: string | undefined;
  /**
   * Whether each side keeps its own sign-in — the *Separate settings for each side* switch.
   *
   * <p>Off (the default), one intent is shared by every side and a side without a token mints its
   * own from it, so Windows and WSL end up on one account without anybody signing in twice.</p>
   */
  readonly perSide?: boolean | undefined;
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
 *
 * <p><b>`expectedEmail` is the guard on a SILENT mint.</b> `getSession` is asked for an account, not
 * for a particular one, so a machine signed in to both a personal and a work Microsoft account can
 * hand back whichever is active on this side. Reconciling a shared intent therefore says whose
 * session it is minting, and a session that comes back as somebody else is ended again before any
 * token is written. Raised on the plan round.</p>
 */
export async function signIn(
  server: TeamServer,
  host: AuthHost,
  interactive = true,
  expectedEmail = '',
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
  const trusted = host.state.get<string>(trustedKey(server.id));
  if (!interactive && trusted !== applicationIdOf(scope)) {
    // A background renewal must never mint a token for an application the person has not seen. A
    // server that changed its advertised scope would otherwise have had one minted and posted to it
    // silently — the confirmation was guarded on `interactive`, so renewal skipped it entirely.
    // Caught on the code round.
    return {
      ok: false,
      message: `${server.name} is now asking for a different Microsoft application than the one you `
        + 'approved. Sign in again to review and approve it.',
    };
  }

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

  const wrongAccount = await refusedForWrongAccount(server, host, session.value, expectedEmail);
  if (wrongAccount.length > 0) {
    return { ok: false, message: wrongAccount };
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
  await host.state.update(intentKey(host, server.id), signedIn);
  // Both halves, in the same breath: what this scope INTENDS, and what this side now actually holds.
  // The panel reads the second one, so a mint that never happened cannot render as a session.
  const fact: TokenFact = { ...signedIn, mintedAtMs: Date.now() };
  await host.state.update(tokenFactKey(server.id, host.side ?? ''), fact);

  return { ok: true, signedIn };
}

/**
 * Where THIS host reads and writes the intent: the shared record, or this side's own.
 *
 * <p>One place decides it, so that a sign-in, a sign-out and a repaint cannot disagree about which
 * record they are talking about.</p>
 */
function intentKey(host: AuthHost, serverId: string): string {
  return signedInKey(serverId, host.perSide === true ? (host.side ?? '') : '');
}

/** The sign-out stamp for the same scope the intent lives in. */
function revocationKey(host: AuthHost, serverId: string): string {
  return revokedKey(serverId, host.perSide === true ? (host.side ?? '') : '');
}

/**
 * A session that came back as somebody else, ended again before it can be used.
 *
 * <p>Empty when there is nothing to check or the account matches — the interactive flow passes no
 * expectation, because there the person chose the account themselves and choosing a different one is
 * the point.</p>
 */
async function refusedForWrongAccount(
  server: TeamServer,
  host: AuthHost,
  session: { readonly token: string; readonly email: string },
  expectedEmail: string,
): Promise<string> {
  if (expectedEmail.length === 0 || session.email === expectedEmail) {
    return '';
  }

  await deleteSession(server.url, session.token, host.fetchImpl);

  return `${server.name}: the Microsoft account active on this side is ${session.email}, not `
    + `${expectedEmail} — nothing was signed in here. Sign in explicitly to use ${session.email} on `
    + 'this side, or separate the sides in the panel to keep an account per side.';
}

/**
 * Ask once, per server, before a token is ever minted for it.
 *
 * <p>The application id is the server's to choose, and the scope check cannot say whether that
 * application is one the company owns — only a person can. Asked once and remembered, because asking
 * on every sign-in trains people to click through it.</p>
 */
async function confirmedOnce(server: TeamServer, scope: string, host: AuthHost): Promise<boolean> {
  const key = trustedKey(server.id);
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
 *
 * <p><b>It reaches the other sides too.</b> Only this side's token file can be deleted from here —
 * the others are on filesystems this extension host cannot write. So the moment is STAMPED, and each
 * of them signs itself out on its next refresh (see {@link sessionAction}). Without that, signing
 * out on Windows left a usable token inside the WSL distro and a live session on the server. Raised
 * on the plan round.</p>
 */
export async function signOut(
  server: TeamServer,
  host: AuthHost,
  token: string,
): Promise<{ readonly ok: boolean; readonly message: string }> {
  // Best effort, and the local half runs regardless: a server that is down must not be able to keep
  // somebody signed in on this machine. What is NOT swallowed is whether it worked — the caller says
  // so, because a session left live on the server is a fact the person should hear even though the
  // remedy is the server's expiry rather than anything they can press.
  const revoked = token.length === 0 || (await deleteSession(server.url, token, host.fetchImpl)).ok;
  await host.state.update(intentKey(host, server.id), undefined);
  await host.state.update(revocationKey(host, server.id), Date.now());
  await host.state.update(tokenFactKey(server.id, host.side ?? ''), undefined);

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

  return revoked
    ? { ok: true, message: `${server.name}: signed out.` }
    : {
      ok: false,
      message: `${server.name}: signed out on this machine, but the server could not be reached to `
        + 'end the session there. It will expire on its own; nothing on this machine can use it.',
    };
}

/** What this side should do about its session, given the intent and what it actually holds. */
export type SessionAction = 'mint' | 'signOut' | 'nothing';

/**
 * The whole rule, as one pure table.
 *
 * <p>Pure so it is TESTED rather than trusted: every state below is one a person can reach by
 * opening a second window, and the version of this that lived scattered across the provider is what
 * let the panel report a session the shim had no token for.</p>
 *
 * <ul>
 *   <li>no intent, a fact minted BEFORE a sign-out → sign this side out too;</li>
 *   <li>an intent, no fact → mint (this is a WSL window opened after a Windows sign-in);</li>
 *   <li>an intent, a fact for a DIFFERENT account → mint (the sides were just merged);</li>
 *   <li>an intent, a fact nearly expired → mint, which is the old silent renewal;</li>
 *   <li>anything else → nothing.</li>
 * </ul>
 */
export function sessionAction(
  intent: SignedIn | undefined,
  fact: TokenFact | undefined,
  revokedAtMs: number,
  nowMs: number,
): SessionAction {
  if (intent === undefined) {
    return fact !== undefined && revokedAtMs > fact.mintedAtMs ? 'signOut' : 'nothing';
  }

  return mintDue(intent, fact, nowMs) ? 'mint' : 'nothing';
}

function mintDue(intent: SignedIn, fact: TokenFact | undefined, nowMs: number): boolean {
  return fact === undefined || fact.email !== intent.email || needsRenewal(fact.expiresUtc, nowMs);
}

/** What a reconciliation did, for the row that has to explain itself. */
export interface Reconciled {
  /** Why this side is not usable, in a sentence, or empty. */
  readonly problem: string;
  /** Whether anything on disk or in the records changed — the panel repaints on true. */
  readonly changed: boolean;
}

/**
 * Bring this side's session into line with what it is supposed to be, without ever asking anybody.
 *
 * <p>Replaces the old `renewIfDue`, which asked one question — is the session nearly out — of a
 * record that described the machine rather than the side. The mint here is the same silent
 * `signIn(…, interactive: false)`: it shows no prompt, refuses unless the person has already
 * approved this server's Microsoft application, and now also refuses a session that comes back as a
 * different account than the intent names.</p>
 *
 * <p><b>`clearSessionPreference` must stay off in that path.</b> It forces the provider to ignore
 * the cached account and ask, which `createIfNone: false` forbids — so the pair can only ever return
 * nothing, and the extension would conclude the identity session was gone and sign the person out.
 * Weekly. The plan round caught that before it was written.</p>
 */
export async function reconcile(
  server: TeamServer,
  host: AuthHost,
  nowMs: number = Date.now(),
): Promise<Reconciled> {
  const intent = host.state.get<SignedIn>(intentKey(host, server.id));
  const fact = await factHere(server, host);
  const revokedAtMs = host.state.get<number>(revocationKey(host, server.id)) ?? 0;
  const action = sessionAction(intent, fact, revokedAtMs, nowMs);

  if (action === 'mint' && intent !== undefined) {
    const outcome = await signIn(server, host, false, intent.email);

    return outcome.ok ? { problem: '', changed: true } : { problem: outcome.message, changed: false };
  }

  if (action === 'signOut') {
    const gone = await signOut(server, host, await readToken(host.dataDir, server.url));

    return { problem: gone.ok ? '' : gone.message, changed: true };
  }

  return { problem: '', changed: false };
}

/**
 * What this side holds, with the FILE as the arbiter.
 *
 * <p>A record describing a token somebody deleted by hand is discarded here rather than believed —
 * otherwise the panel keeps offering a session that cannot be used, and nothing ever re-mints it.</p>
 */
async function factHere(server: TeamServer, host: AuthHost): Promise<TokenFact | undefined> {
  const key = tokenFactKey(server.id, host.side ?? '');
  const recorded = host.state.get<TokenFact>(key);
  if (recorded === undefined) {
    return undefined;
  }

  if ((await readToken(host.dataDir, server.url)).length > 0) {
    return recorded;
  }

  await host.state.update(key, undefined);

  return undefined;
}

/** What one server's catalog says right now, or why it could not be asked. */
export async function catalogOf(
  server: TeamServer,
  token: string,
  fetchImpl?: typeof fetch,
): Promise<ServerResult<Catalog>> {
  return fetchCatalog(canonicalTeamServerUrl(server.url), token, fetchImpl);
}
