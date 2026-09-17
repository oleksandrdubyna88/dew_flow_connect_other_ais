/**
 * Where the bugs admin key lives, and where a key that was issued but never copied waits.
 *
 * <p><b>`SecretStorage`, never `settings.json`.</b> This is the repository's first use of it, so
 * the pattern is set here. Settings SYNC — across machines, into a profile, sometimes into a
 * repository — and an admin credential that follows somebody to another machine is a credential
 * nobody can account for. `SecretStorage` is per machine and encrypted by the OS keychain.</p>
 *
 * <p><b>A pending issuance is the reason this file holds three things rather than one.</b> Three
 * plan reviewers found the same defect: `POST /admin/keys` COMMITS the key before the panel can
 * display it, and a webview cannot veto its own disposal. So "the panel will not dismiss until the
 * key is copied" is enforced by nothing, and the key is unrecoverable because the server returns it
 * exactly once.</p>
 *
 * <p><b>The code round then found the window that remained</b>, three times over: the key is
 * committed by the SERVER before this process can write anything at all, so a host death between
 * the answer and the keychain write leaves a live key with no local record. Two commits cannot be
 * made atomic from one side, and this is said rather than papered over — but the window is narrowed
 * to as close to nothing as a client can get:</p>
 *
 * <ul>
 *   <li>an ATTEMPT is recorded BEFORE the request leaves, so a crash at any point afterwards leaves
 *       a trace that the next open can act on — it cannot name the key, but it can say one may
 *       exist and point at the newest row, which is what story 2 ordered the listing for;</li>
 *   <li>the issuance itself is written the instant the answer arrives, with the ORIGIN it came
 *       from, so a discard can never be sent to a server that did not issue it;</li>
 *   <li>and nothing is forgotten except by an explicit copy, or by a discard the issuing server
 *       confirmed.</li>
 * </ul>
 */

/** The three things this extension keeps for the bugs admin surface. */
export const ADMIN_KEY = 'coai.bugs.adminKey';
export const PENDING_ISSUANCE = 'coai.bugs.pendingIssuance';
export const ISSUANCE_ATTEMPT = 'coai.bugs.issuanceAttempt';

/** A key the server has issued and nobody has confirmed holding. */
export interface Pending {
  readonly id: string;
  readonly key: string;
  readonly note: string;
  readonly createdUtc: string;
  /**
   * The server that issued it.
   *
   * <p>Carried because the setting it came from is editable while this record is held: a key issued
   * against server A and discarded after the address was changed to B would send A's id to B, and
   * B's 404 — "no key of that id here" — would be read as proof that it was gone. The key would
   * stay alive on A with nobody holding it, which is the exact outcome the pending record exists to
   * prevent. (Code round, codex.)</p>
   */
  readonly server: string;
}

/** That an issuance was attempted, and against whom. Written before the request leaves. */
export interface Attempt {
  readonly server: string;
  readonly note: string;
}

/**
 * The half of `vscode.SecretStorage` this needs.
 *
 * <p>An interface rather than the type itself so the tests drive it without a VS Code host — the
 * same reason every other seam in this extension is one.</p>
 */
export interface Secrets {
  get(key: string): Thenable<string | undefined>;
  store(key: string, value: string): Thenable<void>;
  delete(key: string): Thenable<void>;
}

/** The admin key, or empty when none has been set. */
export async function adminKey(secrets: Secrets): Promise<string> {
  return (await secrets.get(ADMIN_KEY)) ?? '';
}

/** Sets it. An empty value CLEARS it rather than storing nothing under a key that then reads as set. */
export async function setAdminKey(secrets: Secrets, key: string): Promise<void> {
  const trimmed = key.trim();
  if (trimmed.length === 0) {
    await secrets.delete(ADMIN_KEY);

    return;
  }

  await secrets.store(ADMIN_KEY, trimmed);
}

/**
 * The key that was issued and never confirmed, if there is one.
 *
 * <p>A stored value that cannot be parsed is treated as none rather than thrown: this is read on
 * every open of the tab, and a panel that refuses to draw because of a malformed secret is worse
 * than one that has lost track of a key — the listing can still show it, newest first.</p>
 */
export async function pendingIssuance(secrets: Secrets): Promise<Pending | undefined> {
  const held = await parsed(secrets, PENDING_ISSUANCE);
  if (held === undefined) {
    return undefined;
  }

  return typeof held.id === 'string' && typeof held.key === 'string' && held.id.length > 0
    ? {
      id: held.id,
      key: held.key,
      note: text(held.note),
      createdUtc: text(held.createdUtc),
      server: text(held.server),
    }
    : undefined;
}

/**
 * Records a key as issued and not yet held by anybody, and forgets the attempt that led to it.
 *
 * <p>Called the instant the server answers `201` and BEFORE the panel paints.</p>
 */
export async function holdIssuance(secrets: Secrets, issued: Pending): Promise<void> {
  await secrets.store(PENDING_ISSUANCE, JSON.stringify(issued));
  await secrets.delete(ISSUANCE_ATTEMPT);
}

/** Forgets the pending issuance — after a copy, or after the revoke a discard performed. */
export async function releaseIssuance(secrets: Secrets): Promise<void> {
  await secrets.delete(PENDING_ISSUANCE);
}

/**
 * Records that an issuance is about to be asked for.
 *
 * <p>Written BEFORE the request, because the server commits the key before it answers: everything
 * after this point can die — the network, this process, the machine — and the next open still
 * knows that a key may exist. It cannot know the key itself; nothing can, because the server says
 * it once. What it can do is refuse to pretend nothing happened.</p>
 */
export async function beginIssuance(secrets: Secrets, attempt: Attempt): Promise<void> {
  await secrets.store(ISSUANCE_ATTEMPT, JSON.stringify(attempt));
}

/** An issuance that was asked for and never accounted for, if there is one. */
export async function issuanceAttempt(secrets: Secrets): Promise<Attempt | undefined> {
  const held = await parsed(secrets, ISSUANCE_ATTEMPT);

  return held === undefined ? undefined : { server: text(held.server), note: text(held.note) };
}

/** Forgets the attempt — once it has an answer, or once a person has been told about it. */
export async function endIssuance(secrets: Secrets): Promise<void> {
  await secrets.delete(ISSUANCE_ATTEMPT);
}

/** A stored record, or nothing when there is none or it cannot be read. */
async function parsed(secrets: Secrets, name: string): Promise<Record<string, unknown> | undefined> {
  const stored = await secrets.get(name);
  if (stored === undefined || stored.length === 0) {
    return undefined;
  }

  try {
    const held: unknown = JSON.parse(stored);

    return typeof held === 'object' && held !== null ? (held as Record<string, unknown>) : undefined;
  } catch {
    return undefined;
  }
}

/** A field that must be a string, or empty — a stored record is data, not a promise. */
function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}
