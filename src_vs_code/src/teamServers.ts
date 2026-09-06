import { createHash } from 'node:crypto';

/**
 * A Team server this machine knows about.
 *
 * <p>One company subscription per vendor, installed on one Linux VM, shared by everybody who can
 * sign in with a company account. This is the client's record of one such server.</p>
 */
export interface TeamServer {
  /**
   * The identity, generated once and never rewritten.
   *
   * <p><b>Not the name, and not the URL.</b> The name is editable, so using it would orphan every
   * reviewer row and its usage history the first time somebody renamed a server; the URL can be
   * corrected after a typo, which would do the same. Raised twice on the plan round of epic 3.</p>
   */
  readonly id: string;
  /** What a person calls it. Free text, editable, used for nothing but display. */
  readonly name: string;
  readonly url: string;
}

/**
 * One canonical spelling of a server URL.
 *
 * <p><b>This must agree with `TeamServerAuth.Normalise` in C# exactly.</b> The extension writes the
 * token file and the MCP shim reads it, each deriving the path independently — so a divergence shows
 * up as a person signing in successfully and being told they are not signed in one second later.
 * `shared/team-server-url-vectors.json` holds the vectors and BOTH suites assert them.</p>
 *
 * <p>Scheme and host lower-cased, a DEFAULT port dropped and a non-default one kept, trailing
 * slashes removed, the path preserved because a server may legitimately live under one. Something
 * that is not a URL comes back trimmed and lower-cased rather than throwing: this runs while
 * building a command line, and a bad URL should fail at the request with a sentence.</p>
 */
export function canonicalTeamServerUrl(url: string): string {
  const text = (url ?? '').trim();
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    return text.replace(/\/+$/, '').toLowerCase();
  }

  const scheme = parsed.protocol.replace(/:$/, '').toLowerCase();
  // WHATWG URL already drops a default port, which is the same rule `Uri.IsDefaultPort` applies.
  const port = parsed.port.length > 0 ? `:${parsed.port}` : '';
  const path = parsed.pathname.replace(/\/+$/, '');

  return `${scheme}://${parsed.hostname.toLowerCase()}${port}${path}`;
}

/** How much of the hash names the file. 16 hex characters is 64 bits — see the C# constant. */
export const FINGERPRINT_LENGTH = 16;

/** The short hash that names a server's token file. Shared with C#, vector for vector. */
export function teamServerFingerprint(url: string): string {
  return createHash('sha256')
    .update(canonicalTeamServerUrl(url), 'utf8')
    .digest('hex')
    .slice(0, FINGERPRINT_LENGTH);
}

/** Where this machine keeps that server's token. `<dataDir>/servers/<fingerprint>.token`. */
export function tokenFileName(url: string): string {
  return `${teamServerFingerprint(url)}.token`;
}

/** An absolute URL under this server, whatever slashes the caller wrote. */
export function teamServerEndpoint(url: string, route: string): string {
  return `${canonicalTeamServerUrl(url)}/${route.replace(/^\/+/, '')}`;
}

/**
 * A stable id for a new server, derived once from its name and never rewritten.
 *
 * <p>Readable rather than a UUID, because this id is half of every reviewer row's name — the thing a
 * person reads in the panel, in their spending page and in a vault key. A collision with an existing
 * server gets a numeric suffix; the id it produces is then permanent for that server.</p>
 */
export function newTeamServerId(name: string, existing: readonly string[]): string {
  const base = name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 24);
  const stem = base.length > 0 ? base : 'team';
  if (!existing.includes(stem)) {
    return stem;
  }

  for (let n = 2; ; n += 1) {
    const candidate = `${stem}-${n}`;
    if (!existing.includes(candidate)) {
      return candidate;
    }
  }
}

/** The reviewer row id for one vendor on one server. Both halves matter — see {@link TeamServer.id}. */
export function remoteVendorRowId(serverId: string, remoteVendor: string): string {
  return `${serverId}-${remoteVendor}`.toLowerCase();
}

/**
 * Read the saved list, keeping only entries that name something.
 *
 * <p>An entry saved before ids existed gets one derived from its name, so an upgrade does not orphan
 * anybody's servers — and the derived id is then written back the next time the list is saved.</p>
 */
export function teamServersFrom(value: unknown): TeamServer[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const seen: string[] = [];
  const servers: TeamServer[] = [];
  for (const entry of value) {
    if (typeof entry !== 'object' || entry === null) {
      continue;
    }
    const row = entry as Record<string, unknown>;
    const url = typeof row['url'] === 'string' ? row['url'].trim() : '';
    if (url.length === 0) {
      continue;
    }
    const name = typeof row['name'] === 'string' && row['name'].trim().length > 0
      ? row['name'].trim()
      : new URL(canonicalTeamServerUrl(url) || 'https://invalid').hostname;
    const saved = typeof row['id'] === 'string' ? row['id'].trim().toLowerCase() : '';
    const id = saved.length > 0 && !seen.includes(saved) ? saved : newTeamServerId(name, seen);
    seen.push(id);
    servers.push({ id, name, url });
  }

  return servers;
}

/**
 * Whether a scope a SERVER asked for may be requested from the identity provider.
 *
 * <p><b>This is a trust boundary, not a formatting check.</b> A person adds a server by typing its
 * URL; a hostile one could then advertise any scope it liked, and the extension would mint a
 * Microsoft token for that resource and post it straight back — the plan round's most serious
 * finding. So two things are pinned rather than one:</p>
 *
 * <ul>
 *   <li>the RESOURCE must be <c>api://&lt;guid&gt;</c> — an application id, never a Microsoft Graph
 *       scope and never a bare host, so no server can ask for permission to read anyone's mail;</li>
 *   <li>the PERMISSION must be exactly {@link REQUIRED_SCOPE_NAME}, so a server cannot name a
 *       different one on the same application.</li>
 * </ul>
 *
 * <p>The application id is still the server's to choose, which is why signing into a server for the
 * first time asks the person to confirm it. Entra's own consent screen is a second backstop.</p>
 */
export const REQUIRED_SCOPE_NAME = 'coai.access';

const APP_ID_SCOPE = new RegExp(
  `^api://[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/${REQUIRED_SCOPE_NAME}$`,
  'i',
);

export function isSafeAdvertisedScope(scope: unknown): scope is string {
  return typeof scope === 'string' && APP_ID_SCOPE.test(scope.trim());
}

/** The application id inside an accepted scope — what a person is asked to confirm. */
export function applicationIdOf(scope: string): string {
  return scope.trim().slice('api://'.length, -(REQUIRED_SCOPE_NAME.length + 1));
}

/**
 * Which sign-in providers this build can actually complete.
 *
 * <p>v1 is Microsoft only, by the operator's decision: the company is on Entra and the domain
 * allow-list is the point of the product. The server keeps its Google scheme wired but off, so
 * turning it on later is a `.env` line rather than a client release — which only works if the client
 * offers what the SERVER advertises, intersected with what it can do. Offering a provider with no
 * implementation is a dead button, which the plan round caught.</p>
 */
export const IMPLEMENTED_PROVIDERS: readonly string[] = ['microsoft'];

export function offerableProviders(advertised: readonly string[]): string[] {
  return advertised.filter((p) => IMPLEMENTED_PROVIDERS.includes(p.trim().toLowerCase()));
}

/** What to say when a server offers only sign-in methods this build cannot do. */
export function noProviderMessage(server: string, advertised: readonly string[]): string {
  const offered = advertised.length > 0 ? advertised.join(', ') : 'none';

  return `The Team server at ${server} offers sign-in with ${offered}, and this version of the `
    + `extension can do ${IMPLEMENTED_PROVIDERS.join(', ')}. Ask the operator to enable Microsoft, `
    + `or update the extension.`;
}
