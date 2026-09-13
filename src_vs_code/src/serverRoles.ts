import type { Catalog } from './teamServerApi';
import { isBuiltIn } from './roles';

/**
 * Which of a person's own roles a configured Team server will run — said BEFORE a round, not after.
 *
 * <p>A role a Team server will not carry is already named in the round's result, once per (vendor,
 * role). By then somebody has waited for a review that was never going to include it. The panel
 * holds every configured server's catalog to draw its health, so it can say the same thing where the
 * roles are configured instead.</p>
 *
 * <p><b>The absent-field rule, which this family has paid for twice.</b> A server older than plan 3
 * sends no `roles` property at all. That means <b>the five this product ships</b> — the behaviour
 * that predates the field — never "none", which would silently empty every round, and never "any",
 * which would send custom roles to a server certain to refuse them. An EMPTY list means the same as
 * absent: a server that HAS the field always accepts at least five, so `[]` can only be a bug.</p>
 */

/** What a server's catalog told us about roles, once the absent-field rule has been applied. */
export type ServerRoles =
  /** It named them, and that list is the whole truth about it. */
  | { readonly kind: 'answered'; readonly names: readonly string[]; readonly allowAny: boolean }
  /** It answered without the field — a server older than this, running the five that ship. */
  | { readonly kind: 'shipped' }
  /** There is no catalog for it: never fetched, or the last fetch failed. */
  | { readonly kind: 'unknown' };

/**
 * Read one server's catalog as an answer about roles.
 *
 * <p>`undefined` is NOT an answer. A server whose catalog could not be read has no opinion, and
 * reading that as "the five it ships" would be inventing one — the same distinction `RemoteRoles`
 * draws in `coai-mcp`, for the same reason: only one of the two can honestly say why a role did not
 * run.</p>
 */
export function serverRolesFrom(catalog: Catalog | undefined): ServerRoles {
  if (catalog === undefined) {
    return { kind: 'unknown' };
  }

  const named = catalog.roles ?? [];

  return named.length === 0
    ? { kind: 'shipped' }
    : { kind: 'answered', names: named, allowAny: catalog.allowAnyRole === true };
}

/** Whether this server will run a role by this id. */
export function serverRuns(roles: ServerRoles, roleId: string): boolean {
  if (roles.kind !== 'answered') {
    // Every server has always run the five, and nothing about a missing field or a failed fetch
    // makes that less true. A role somebody ADDED is the one in question.
    return isBuiltIn(roleId);
  }

  return roles.allowAny || roles.names.some((name) => name.toLowerCase() === roleId.toLowerCase());
}

/**
 * The sentence for one role and one server, or nothing when it will run.
 *
 * <p>Named by the id a server matches on, but SHOWN by the name the person gave it: somebody who
 * called a role "Requirements we wrote" should read that back rather than the `Requirements` the
 * wire uses.</p>
 */
export function whyNotOnServer(
  roles: ServerRoles,
  roleId: string,
  shown: string,
  server: string,
): string {
  if (serverRuns(roles, roleId)) {
    return '';
  }
  if (roles.kind === 'answered') {
    return `${server} runs ${roles.names.join(', ')} — not ${shown}.`;
  }

  return roles.kind === 'shipped'
    ? `${server} is older than the setting that carries roles you added, so it runs the five this product ships — not ${shown}.`
    : `${server} could not be asked which roles it runs, so ${shown} was left out rather than sent and refused.`;
}

/** One configured Team server, as much of it as this decision needs. */
export interface ServerOfRoles {
  readonly name: string;
  readonly catalog: Catalog | undefined;
}

/**
 * Every sentence worth showing about one role, across every configured Team server.
 *
 * <p>Empty when every server will run it — including when there are no Team servers at all, which is
 * the ordinary case and must stay silent.</p>
 */
export function roleOnServers(
  servers: readonly ServerOfRoles[],
  roleId: string,
  shown: string,
): readonly string[] {
  return servers
    .map((one) => whyNotOnServer(serverRolesFrom(one.catalog), roleId, shown, one.name))
    .filter((line) => line.length > 0);
}
