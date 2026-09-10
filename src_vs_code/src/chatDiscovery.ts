import { Catalog } from './teamServerApi';
import { ChatCatalog } from './chatModels';
import { ModelChoice } from './models';
import { TeamServerState } from './teamServerView';
import { TeamServer, canonicalTeamServerUrl } from './teamServers';

/**
 * What the PANEL discovered, kept where the surface that cannot discover it can read it.
 *
 * <p>Three of the four sources a model list comes from are FETCHED rather than read — the `codex`
 * and `agy` CLIs' own lists, and a Team server's allowlist — and all three fetches live in the
 * panel. `chatCatalogFrom` in `chatCommand.ts` passed them empty, so the command's idea of what a
 * row can be pointed at was "the model it is configured to" and nothing else.</p>
 *
 * <p><b>That was harmless until the panel gained a two-step picker, and then it was not.</b> The
 * person picks `gemini-3.8-flash-low` in the sidebar — a real model, discovered by `agy models`,
 * present in no curated list — and the command builds a catalog that has never heard of it, so
 * `openingModel` falls back and the conversation opens on the row's own model instead. A different
 * model, billed, in a voice nobody chose. Found by this product's own plan gate before it shipped.</p>
 *
 * <p>Pure over a plain key-value store, which is what a `Memento` amounts to. The store OUTLIVES the
 * version that wrote it, so everything read back is validated: a value written by an older build is
 * the ordinary case on every update, and it must not take the chat down with it.</p>
 */

/** Where the panel leaves it. One key, one shape, read by one function. */
export const DISCOVERY_KEY = 'coai.chatDiscoveredModels';

export interface Discovery {
  /** What `codex` listed on this machine. */
  readonly codex: readonly ModelChoice[];
  /** What `agy models` listed on this machine. */
  readonly agy: readonly ModelChoice[];
  /** The last catalog each Team server answered with, by server id, WITH the address it came from. */
  readonly catalogs: Readonly<Record<string, StoredCatalog>>;
}

/**
 * A cached catalog, and the endpoint it was fetched from.
 *
 * <p>The address is stored because a server ID alone does not identify a catalog over time: a URL
 * that was corrected, or an ID reused in another workspace, would otherwise hand this window an
 * allowlist fetched from somewhere else — and a model withdrawn there would read as offered here.
 * An ABSENT address means a build that predates this field wrote the entry, and is trusted; only a
 * PRESENT one that disagrees is refused. (codex, the code round.)</p>
 */
export type StoredCatalog = Catalog & {
  readonly url: string;
  /**
   * Whether this catalog was KEPT from a refresh that failed.
   *
   * <p>The panel holds the previous answer when a server cannot be reached and marks it, so the
   * section can say so. Storing it without the mark would launder it: the command would rebuild a
   * state claiming it was current, and a model the server has since withdrawn would read as offered.
   * (CodeRabbit, PR #196.)</p>
   */
  readonly stale: boolean;
};

export const EMPTY_DISCOVERY: Discovery = { codex: [], agy: [], catalogs: {} };

/** A record, or nothing — a stored value can be a string, a number or a null. */
function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * The model choices in a stored list.
 *
 * <p>A half-valid entry is dropped and the rest survives it, which is the rule `chatPresets.ts`
 * already follows for a list a person edits: refusing the whole list because one entry was written
 * by a build that shaped it differently would take away every model somebody can pick.</p>
 */
function choices(value: unknown): readonly ModelChoice[] {
  return (Array.isArray(value) ? value : []).flatMap((row): ModelChoice[] => {
    const one = record(row);
    const id = text(one?.['id']);

    return id.length === 0 ? [] : [{ id, label: text(one?.['label']).length > 0 ? text(one?.['label']) : id }];
  });
}

/** One Team server's catalog, shaped enough for `allowedModelsFor` to read its allowlist. */
function catalog(value: unknown): StoredCatalog | undefined {
  const one = record(value);
  if (one === undefined) {
    return undefined;
  }
  const vendors = (Array.isArray(one['vendors']) ? one['vendors'] : []).flatMap((row): Catalog['vendors'][number][] => {
    const entry = record(row);
    const id = text(entry?.['id']);

    return id.length === 0 ? [] : [{
      id,
      runtime: text(entry?.['runtime']),
      models: (Array.isArray(entry?.['models']) ? entry['models'] : []).filter((m): m is string => typeof m === 'string'),
      slots: { total: 0, ready: 0, coolingDown: 0, needsSignIn: 0 },
    }];
  });

  return {
    serverVersion: text(one['serverVersion']),
    isAdmin: one['isAdmin'] === true,
    error: text(one['error']),
    vendors,
    url: text(one['url']),
    stale: one['stale'] === true,
  };
}

/** What the store holds, validated. Anything unrecognisable is an empty discovery, never a throw. */
export function discoveryFrom(saved: unknown): Discovery {
  const one = record(saved);
  if (one === undefined) {
    return EMPTY_DISCOVERY;
  }
  // Built in one expression rather than assigned into: `catalogs[id] = …` on a plain object is both
  // the mutation the style rule forbids and the way a stored `__proto__` key stops being data.
  const catalogs = Object.fromEntries(
    Object.entries(record(one['catalogs']) ?? {}).flatMap(([id, value]): [string, StoredCatalog][] => {
      const parsed = catalog(value);

      return parsed === undefined ? [] : [[id, parsed]];
    }),
  );

  return { codex: choices(one['codex']), agy: choices(one['agy']), catalogs };
}

/**
 * The catalog the chat command builds, with the panel's discoveries in it.
 *
 * <p>The Team servers come from the settings — they are configuration, and the command reads them
 * for itself — and each is given back the catalog the panel last fetched for it, which is where the
 * allowlist lives. `localEngine` stays absent on purpose: a `local` row cannot chat at all
 * (`canChat`), so it would be a value nothing on this path can read.</p>
 */
/**
 * The cached catalog, but only if it belongs to the address this server is configured at.
 *
 * <p>An entry with no address predates the field and is trusted — refusing it would drop every
 * allowlist on the update that added the check, for a case that has not happened. Compared
 * canonically, because the row stores one spelling and the server entry holds whatever was typed.</p>
 */
function fetchedFrom(cached: StoredCatalog | undefined, url: string): Catalog | undefined {
  if (cached === undefined) {
    return undefined;
  }

  return cached.url.length === 0 || canonicalTeamServerUrl(cached.url) === canonicalTeamServerUrl(url)
    ? cached
    : undefined;
}

export function catalogUsing(discovery: Discovery, servers: readonly TeamServer[]): ChatCatalog {
  return {
    discoveredCodex: discovery.codex,
    discoveredAgy: discovery.agy,
    localEngine: undefined,
    teamServers: servers.map((server): TeamServerState => ({
      server,
      email: '',
      problem: '',
      // `hasOwnProperty`, because a server id is a string somebody can write and `catalogs` is a
      // plain object: `catalogs['__proto__']` answers with Object.prototype — an INHERITED value,
      // not a missing one — and reading a field off it throws where a lookup should have missed.
      catalog: fetchedFrom(
        Object.prototype.hasOwnProperty.call(discovery.catalogs, server.id) ? discovery.catalogs[server.id] : undefined,
        server.url,
      ),
      stale: discovery.catalogs[server.id]?.stale === true
        && Object.prototype.hasOwnProperty.call(discovery.catalogs, server.id),
    })),
  };
}
