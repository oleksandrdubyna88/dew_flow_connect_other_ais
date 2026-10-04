import { canonicalTeamServerUrl } from './teamServers';

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

/**
 * The PREFIX of every admin key's name — the key itself is filed under `<prefix>:<server>` ({@link keyName}). The old
 * fixed name that held one key for every server is {@link LEGACY} below, a separate constant on purpose: renaming the
 * prefix must never move what the legacy check reads.
 */
const ADMIN_KEY = 'coai.bugs.adminKey';

/**
 * The CONTRIBUTOR key — what this machine sends its own pairs with.
 *
 * <p>A different credential from the admin key and kept apart from it on purpose: one issues and
 * revokes, the other uploads, and a person who holds both should be able to remove either. Same
 * storage for the same reason — `settings.json` syncs, and a key that follows somebody to another
 * machine is a key nobody can account for.</p>
 */
const CONTRIBUTOR_KEY = 'coai.bugs.contributorKey';

/**
 * The names the two keys were stored under before they were filed per server — literals, not the prefixes above, and
 * not exported: nothing outside this module may read a key that no server has been vouched for.
 */
const LEGACY_ADMIN_KEY = 'coai.bugs.adminKey';
const LEGACY_CONTRIBUTOR_KEY = 'coai.bugs.contributorKey';
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

/**
 * The name a key is filed under: its kind and the server that issued it (research/PLAN_bugz_keys_per_server.md).
 *
 * <p>`coai.bugzServer` is a per-side setting, and a key filed under one fixed name was sent to whichever server a side
 * named — so a host that never issued it received it, and could replay it against the one that did. The server is
 * written the way `canonicalTeamServerUrl` writes it, so one server spelt two ways is one name. No server, no name:
 * `''`, which every reader below treats as "no key".</p>
 */
export function keyName(kind: 'admin' | 'contributor', server: string): string {
  const where = canonicalTeamServerUrl(server);

  return where.length === 0 ? '' : `${kind === 'admin' ? ADMIN_KEY : CONTRIBUTOR_KEY}:${where}`;
}

/** What a request to one Bugz server is sent with: that server, and ITS key — read from one value, never two. */
export interface Credentials {
  readonly server: string;
  readonly key: string;
}

/** What a setter did. `no-server` stored nothing, and the caller says so rather than reporting success. */
export type Stored = 'stored' | 'cleared' | 'no-server';

/** The admin key for this server, or empty when none has been set for it. */
export async function adminKey(secrets: Secrets, server: string): Promise<string> {
  return held(secrets, keyName('admin', server));
}

/**
 * The ADMIN credentials for a request to `server`: the server and its own key from the ONE value given, so the key
 * looked up and the address the request goes to can never be two different reads of a setting that changed between
 * them (plan round, session d5cdb1b2). Named for its kind: an admin key sent as a contributor's is a key in the wrong
 * server's logs.
 */
export async function adminCredentialsFor(secrets: Secrets, server: string): Promise<Credentials> {
  return { server, key: await adminKey(secrets, server) };
}

/** The CONTRIBUTOR credentials for a request to `server` — what the pair upload sends, by the same one-value rule. */
export async function contributorCredentialsFor(secrets: Secrets, server: string): Promise<Credentials> {
  return { server, key: await contributorKey(secrets, server) };
}

/** Whether a server is one a key can be filed under — the key module's own test, for every caller that must decide it. */
export function hasServer(server: string): boolean {
  return keyName('admin', server).length > 0;
}

/** Sets it for this server. An empty value CLEARS it; with no server nothing is stored, and that is the answer. */
export async function setAdminKey(secrets: Secrets, server: string, key: string): Promise<Stored> {
  return put(secrets, keyName('admin', server), key);
}

/** The contributor key for this server, or empty when none has been set for it. */
export async function contributorKey(secrets: Secrets, server: string): Promise<string> {
  return held(secrets, keyName('contributor', server));
}

/** Sets it for this server, with the same rules the admin key has. */
export async function setContributorKey(secrets: Secrets, server: string, key: string): Promise<Stored> {
  return put(secrets, keyName('contributor', server), key);
}

async function held(secrets: Secrets, name: string): Promise<string> {
  return name.length === 0 ? '' : (await secrets.get(name)) ?? '';
}

// ---------------------------------------------------------------- the keys from before they were filed per server

/** The old fixed names, and the kind each one was. */
const LEGACY: readonly (readonly [string, 'admin' | 'contributor'])[] = [[LEGACY_ADMIN_KEY, 'admin'], [LEGACY_CONTRIBUTOR_KEY, 'contributor']];

/**
 * Whether keys from before the per-server names are still held. They are NEVER filed automatically: nothing proves
 * which server issued one — a side may have named its own server, a workspace may have overridden it, the shared value
 * may have changed since — and filing a key under a server that did not issue it is the replay this exists to end
 * (plan round, session d5cdb1b2). Until the person adopts or discards them, they are sent nowhere.
 */
export async function legacyKeysHeld(secrets: Secrets): Promise<boolean> {
  const found = await Promise.all(LEGACY.map(async ([old]) => (await secrets.get(old)) !== undefined));

  return found.some(Boolean);
}

/**
 * The old keys filed under `server` — the one the PERSON says issued them. A key already filed there is newer and
 * wins. Each old name is deleted only once its key is proven held under the new one (written, then read back), so a
 * store that loses the write leaves the old key where it was: `not-kept`.
 */
export async function adoptLegacyKeys(secrets: Secrets, server: string): Promise<'adopted' | 'none' | 'no-server' | 'not-kept'> {
  if (!(await legacyKeysHeld(secrets))) {
    return 'none';
  }
  if (!hasServer(server)) {
    return 'no-server';
  }
  const kept = await Promise.all(LEGACY.map(([old, kind]) => adoptOne(secrets, old, keyName(kind, server))));

  return kept.every(Boolean) ? 'adopted' : 'not-kept';
}

/** One old key under its new name — unless that name holds one already — and the old name gone once that is proven. */
async function adoptOne(secrets: Secrets, old: string, now: string): Promise<boolean> {
  const value = await secrets.get(old);
  const proven = value === undefined || (await filed(secrets, now, value));
  if (proven) {
    await secrets.delete(old);
  }

  return proven;
}

/**
 * `value` held under `now` — written unless a real newer key is there already — and proven by reading back THAT value.
 * An empty string under the new name is no key at all; a read-back that differs from what was written (a lossy store,
 * a writer racing this one) is not proof, so the old key stays where it was.
 */
async function filed(secrets: Secrets, now: string, value: string): Promise<boolean> {
  const newer = (await secrets.get(now)) ?? '';
  if (newer.length > 0) {
    return true;
  }
  await secrets.store(now, value);

  return (await secrets.get(now)) === value;
}

/** The old keys removed, filed nowhere — what a person chooses when they cannot vouch for where they came from. */
export async function discardLegacyKeys(secrets: Secrets): Promise<void> {
  for (const [old] of LEGACY) {
    await secrets.delete(old);
  }
}

/**
 * Stores a credential, or removes it when what arrived is nothing — and stores nothing at all without a name.
 *
 * <p>The clear-on-empty rule in ONE place: storing `''` leaves a key that reads as SET and refuses
 * every request, which is the worst of both — and it is the kind of rule that gets remembered at the
 * first call site and forgotten at the second.</p>
 */
async function put(secrets: Secrets, name: string, key: string): Promise<Stored> {
  if (name.length === 0) {
    return 'no-server';
  }
  const trimmed = key.trim();
  if (trimmed.length === 0) {
    await secrets.delete(name);

    return 'cleared';
  }
  await secrets.store(name, trimmed);

  return 'stored';
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
