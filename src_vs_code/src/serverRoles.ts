import type { Catalog } from './teamServerApi';
import { isRoleId } from './roles';

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

  // `Catalog` is a TypeScript INTERFACE, which is a promise about a value this code did not
  // produce: it came over HTTP from a server somebody else configured. `{"roles":[null]}` would
  // reach `name.toLowerCase()` and throw while the Prompts section was being built — one malformed
  // response taking the whole panel down. (codex, story 4's code round.)
  //
  // And it is the ID RULE that decides what is usable, not merely "a non-empty string": `[' ']`
  // would otherwise make a catalog look ANSWERED, and an answered catalog is the whole truth about
  // its server — so one space would report every shipped role as unsupported. Trimmed first,
  // because the server trims what it is sent. (CodeRabbit, plan 3.)
  const named = Array.isArray(catalog.roles)
    ? catalog.roles
      .map((name) => (typeof name === 'string' ? name.trim() : ''))
      .filter(isRoleId)
    : [];

  return named.length === 0
    ? { kind: 'shipped' }
    : { kind: 'answered', names: named, allowAny: catalog.allowAnyRole === true };
}

/** Whether this server will run a role by this id. */
export function serverRuns(roles: ServerRoles, roleId: string): boolean {
  if (roles.kind !== 'answered') {
    // Every deployed server runs THESE, and nothing about a missing field or a failed fetch makes
    // that less true. A role somebody ADDED is one question; a role this PRODUCT added after that
    // server was built is another, and both answer no.
    return BEFORE_THE_CATALOG.has(roleId.toLowerCase());
  }

  return roles.allowAny || roles.names.some((name) => name.toLowerCase() === roleId.toLowerCase());
}

/**
 * The roles EVERY deployed Team server runs, whatever it has said.
 *
 * <p><b>A literal list, and that is the point.</b> This was `isBuiltIn` — a proxy for "one of the
 * five this product ships", true for exactly as long as the product shipped five. Plan 4 put two
 * DOCUMENT roles in the same seed and the proxy silently started meaning seven, so this page told a
 * person that a Team server deployed months ago would run `DocumentReview`. It will not: that box's
 * `AcceptedRoles` was compiled before those roles existed, and the round comes back a 400 naming the
 * ones it does run. The deploy is manual, so a tag does not put new roles on a box.</p>
 *
 * <p>It is not a predicate. It is a fact about the PAST — what this product shipped before
 * `/api/catalog` ever named roles — so it is frozen by definition and no role added later can join
 * it. A server that has not SAID its roles predates that field; therefore it runs exactly these.</p>
 *
 * <p>`coai-mcp` holds the same list as `RemoteRoles.BeforeTheCatalog`, where it decides whether a
 * round is sent. This copy decides what a PERSON is told before they start one, and the two going
 * out of step is how a panel comes to promise a round the gate will not run — which is exactly what
 * happened here: PR #235 fixed that half and missed this one.</p>
 */
export const BEFORE_THE_CATALOG: ReadonlySet<string> = new Set([
  'plancritique',
  'conventions',
  'architecture',
  'securityreliability',
  'uxdxperformance',
]);

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
