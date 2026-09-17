/**
 * Where the bugs admin key lives, and where a key that was issued but never copied waits.
 *
 * <p><b>`SecretStorage`, never `settings.json`.</b> This is the repository's first use of it, so
 * the pattern is set here. Settings SYNC — across machines, into a profile, sometimes into a
 * repository — and an admin credential that follows somebody to another machine is a credential
 * nobody can account for. `SecretStorage` is per machine and encrypted by the OS keychain.</p>
 *
 * <p><b>A pending issuance is the reason this file holds two things rather than one.</b> Three plan
 * reviewers found the same defect independently: `POST /admin/keys` COMMITS the key before the
 * panel can display it, and a webview cannot veto its own disposal — the editor's tab control, a
 * window close and an extension-host reload all take it away. So "the panel will not dismiss until
 * the key is copied" is a promise enforced by nothing, and the key is unrecoverable because the
 * server returns it exactly once.</p>
 *
 * <p>The guarantee therefore lives HERE and not in a modal. The moment the server answers, the key
 * is written as pending; it is cleared only by an explicit copy or an explicit discard, and a
 * discard REVOKES it rather than forgetting it. Whatever happens to the window, the next open finds
 * the pending issuance and offers both.</p>
 */

/** The two things this extension keeps for the bugs admin surface. */
export const ADMIN_KEY = 'coai.bugs.adminKey';
export const PENDING_ISSUANCE = 'coai.bugs.pendingIssuance';

/** A key the server has issued and nobody has confirmed holding. */
export interface Pending {
  readonly id: string;
  readonly key: string;
  readonly note: string;
  readonly createdUtc: string;
}

/**
 * The half of `vscode.SecretStorage` this needs.
 *
 * <p>An interface rather than the type itself so the tests drive it without a VS Code host — the
 * same reason every other seam in this extension is one. The three members are exactly what
 * `SecretStorage` offers; nothing here reaches past them.</p>
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
 * than one that has lost track of a key — which the listing can still show, newest first.</p>
 */
export async function pendingIssuance(secrets: Secrets): Promise<Pending | undefined> {
  const stored = await secrets.get(PENDING_ISSUANCE);
  if (stored === undefined || stored.length === 0) {
    return undefined;
  }

  try {
    const held = JSON.parse(stored) as Partial<Pending>;

    return typeof held.id === 'string' && typeof held.key === 'string'
      ? {
        id: held.id,
        key: held.key,
        note: typeof held.note === 'string' ? held.note : '',
        createdUtc: typeof held.createdUtc === 'string' ? held.createdUtc : '',
      }
      : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Records a key as issued and not yet held by anybody.
 *
 * <p>Called the instant the server answers `201` and BEFORE the panel paints, because the window
 * between those two is exactly the one that loses a key.</p>
 */
export async function holdIssuance(secrets: Secrets, issued: Pending): Promise<void> {
  await secrets.store(PENDING_ISSUANCE, JSON.stringify(issued));
}

/** Forgets the pending issuance — after a copy, or after the revoke that a discard performs. */
export async function releaseIssuance(secrets: Secrets): Promise<void> {
  await secrets.delete(PENDING_ISSUANCE);
}
