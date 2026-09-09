import { engineNote, LocalEngine } from './localEngines';
import { canonicalTeamServerUrl } from './teamServers';
import { TeamServerState } from './teamServerView';
import { Vendor } from './vendors';
/**
 * Which models a vendor can be pointed at, and where that list comes from.
 *
 * <p>Two different honesties. **Codex publishes its list**: the CLI keeps
 * `~/.codex/models_cache.json`, refreshed against the service, so the dropdown shows what this
 * machine can actually reach today. **Gemini publishes nothing** — its CLI has no models command
 * and writes no cache — so the list here is curated, and says so.</p>
 *
 * <p>Either way the field stays a COMBOBOX, never a closed dropdown: a list that cannot be
 * overridden is a list that goes stale in front of someone who knows better.</p>
 */

export interface ModelChoice {
  readonly id: string;
  readonly label: string;
}

/** The shape of a vendor's CLI — what argv to build, not who the vendor is. */
/**
 * Every reviewer runtime this build knows, as a VALUE — and the type is derived from it.
 *
 * <p>One declaration, deliberately. There used to be two: this union, and a `RUNTIMES` array in
 * `vendors.ts` that `vendorsFrom` validated against. Adding `local` to the type and not to the
 * array made every saved local reviewer come back as a CODEX one — the row kept its name, listed
 * codex's models, offered codex's buttons, and a round would have gone through the Codex CLI:
 * the one thing the local runtime exists to avoid. The comment beside that check already said the
 * two had to be kept in step, which is the argument for there being only one of them.</p>
 */
export const RUNTIMES = ['codex', 'gemini', 'claude', 'antigravity', 'local', 'remote'] as const;

export type Runtime = (typeof RUNTIMES)[number];

/**
 * `agy models` → the ids it lists, in its order.
 *
 * <p>Two columns separated by a tab: the id, then the label a person reads. Anything else on the
 * line — the "Fetching available models..." it prints first, a blank line, a warning — is not two
 * columns and is skipped, so a CLI that greets you does not become a model called "Fetching".</p>
 */
export function parseAgyModels(text: string): ModelChoice[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.split('\t'))
    .filter((columns) => columns.length >= 2 && columns[0]!.trim().length > 0 && columns[1]!.trim().length > 0)
    .map((columns) => ({ id: columns[0]!.trim(), label: columns[1]!.trim() }));
}

/** `~/.codex/models_cache.json` → the slugs it lists. A missing or broken cache is simply none. */
export function parseCodexModels(text: string): ModelChoice[] {
  try {
    const parsed = JSON.parse(text) as { models?: { slug?: unknown; display_name?: unknown }[] };
    if (!Array.isArray(parsed.models)) {
      return [];
    }
    return parsed.models
      .filter((m): m is { slug: string; display_name?: string } => typeof m.slug === 'string' && m.slug.length > 0)
      .map((m) => ({
        id: m.slug,
        label: typeof m.display_name === 'string' && m.display_name.length > 0 ? m.display_name : m.slug,
      }));
  } catch {
    return [];
  }
}

/**
 * Gemini's list is curated because there is nothing to read it from.
 *
 * <p>Kept short and generic on purpose: `-latest` aliases survive a model generation, which a
 * hard-coded version number does not. Anything absent is still typeable.</p>
 */
export const CURATED_GEMINI_MODELS: readonly ModelChoice[] = [
  { id: 'gemini-flash-latest', label: 'Gemini Flash (latest)' },
  { id: 'gemini-flash-lite-latest', label: 'Gemini Flash Lite (latest)' },
  { id: 'gemini-pro-latest', label: 'Gemini Pro (latest)' },
];

/**
 * The choices to offer for one runtime — discovered first, curated when there is no discovery,
 * and always with whatever the person already typed, so a saved value never vanishes from its
 * own dropdown.
 */
/**
 * Claude's models, curated: the CLI resolves an alias to the latest of that family, which is what
 * anyone picking from a list actually wants.
 */
export const CURATED_CLAUDE_MODELS: readonly ModelChoice[] = [
  { id: 'haiku', label: 'Haiku — fastest, cheapest' },
  { id: 'sonnet', label: 'Sonnet — the balanced one' },
  { id: 'opus', label: 'Opus — the strongest' },
];

export function modelsFor(
  runtime: Runtime,
  discoveredCodex: readonly ModelChoice[],
  current: string,
  localEngine?: LocalEngine,
  discoveredAgy: readonly ModelChoice[] = [],
  allowedRemote: readonly string[] = [],
): ModelChoice[] {
  // A Team server's allowlist is as authoritative as an installed-model list, and for the same
  // reason: it is what this server will actually accept THIS minute. So the dropdown is discovered,
  // never curated — and a saved model the server no longer allows is kept and MARKED, exactly as a
  // local engine's is. Dropping it would silently switch the reviewer to another model; showing it
  // plainly would let a round be sent for one the server will refuse.
  if (runtime === 'remote') {
    const offered = allowedRemote.map((id) => ({ id, label: id }));

    return current.length > 0 && !offered.some((m) => m.id === current)
      ? [{ id: current, label: `${current} — this server does not offer it any more` }, ...offered]
      : offered;
  }

  // A local engine's list is DISCOVERED, and that is the whole difference from the others: what can
  // be picked is what is installed on this machine this minute. A list compiled when this extension
  // was built would be wrong on every machine, including the one it was built on. An engine that did
  // not answer contributes nothing rather than a plausible-looking default.
  if (runtime === 'local') {
    const found = (localEngine?.models ?? []).map((m) => ({
      id: m.id,
      label: m.detail.length > 0 ? `${m.id} — ${m.detail}` : m.id,
    }));

    // A selection the engine no longer lists is kept and MARKED, never quietly dropped and never
    // shown as though it were still installed. Dropping it would silently switch the reviewer to
    // another model; showing it plainly would let a round be sent for a model that will 404. Raised
    // by this product's own gate on the plan for this feature.
    return current.length > 0 && !found.some((m) => m.id === current)
      ? [{ id: current, label: `${current} — NOT on this engine any more` }, ...found]
      : found;
  }
  const base =
    runtime === 'codex'
      ? [...discoveredCodex]
      : runtime === 'claude'
        ? [...CURATED_CLAUDE_MODELS]
        : runtime === 'antigravity'
          ? [...(discoveredAgy.length > 0 ? discoveredAgy : ANTIGRAVITY_MODELS)]
          : [...CURATED_GEMINI_MODELS];
  if (current.length > 0 && !base.some((m) => m.id === current)) {
    base.unshift({ id: current, label: `${current} (yours)` });
  }
  return base;
}

/**
 * What `agy models` listed on a Google AI Pro subscription WHEN THIS WAS WRITTEN.
 *
 * <p>One subscription, three families: the effort level is part of the model id rather than a
 * separate setting, which is why `-high` and `-low` are listed as distinct choices.</p>
 *
 * <p><b>A fallback, not the answer.</b> It is what the panel offers when the CLI cannot be asked,
 * and it goes stale in silence — the operator found Gemini 3.8 Flash missing from this list on
 * 2026-09-05 while `agy models` had listed it, along with 3.6 and a Pro (Low), for some time. The
 * discovered list comes first now.</p>
 */
export const ANTIGRAVITY_MODELS: readonly ModelChoice[] = [
  { id: 'gemini-3.7-flash-high', label: 'Gemini 3.7 Flash (High)' },
  { id: 'gemini-3.7-flash-medium', label: 'Gemini 3.7 Flash (Medium)' },
  { id: 'gemini-3.7-flash-low', label: 'Gemini 3.7 Flash (Low)' },
  { id: 'gemini-3.1-pro-high', label: 'Gemini 3.1 Pro (High)' },
  { id: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 (Thinking)' },
  { id: 'claude-opus-4-6-thinking', label: 'Claude Opus 4.6 (Thinking)' },
  { id: 'gpt-oss-120b-medium', label: 'GPT-OSS 120B (Medium)' },
];

/** Where the list came from, said in the panel so nobody mistakes curation for discovery. */
/**
 * What a Team server allows this vendor, as the caption needs it.
 *
 * <p>Declared here rather than imported from the panel so this module keeps knowing nothing about
 * how a card is drawn: this is the smallest shape the sentence needs, and the panel's own record
 * satisfies it structurally.</p>
 */
export type CatalogState =
  /** The server answered and this is its allowlist. */
  | 'here'
  /** A server entry exists and its catalog is not in the panel's state yet. */
  | 'waiting'
  /** No Team server on this side matches the row — usually one removed with its reviewers left behind. */
  | 'no-server';

export interface RemoteProvenance {
  readonly models: readonly string[];
  readonly named: string;
  readonly catalog: CatalogState;
}

/**
 * What a vendor that is not a Team-server row knows about a catalog: nothing.
 *
 * <p>A module constant rather than an object literal in a parameter default. A literal there is
 * allocated afresh on every call and is a default nobody can point at — which is why the analyser
 * flags it, and why two files had written the same three fields out by hand.</p>
 */
export const NO_REMOTE_CATALOG: RemoteProvenance = { models: [], named: '', catalog: 'no-server' };

/**
 * What the caption says when there is no allowlist to count.
 *
 * <p>Neither of these says "not asked yet". Nothing at this call site can tell a request still in
 * flight from one that failed — the fetch, its error and its retry belong to the Team servers
 * section, which already renders the server's own message and marks a stale answer. Claiming the
 * first when it may be the second is the kind of confident wrong sentence this file has had to
 * remove twice; pointing at the one place that knows costs a clause.</p>
 *
 * <p>And the two are kept apart, because they have different cures. A row whose server entry is
 * GONE — removed while its reviewers were left behind — must not be sent to a section that no
 * longer lists it. Accepted findings, this story's plan and code rounds.</p>
 */
const CATALOG_NOT_HERE: Readonly<Record<Exclude<CatalogState, 'here'>, string>> = {
  waiting: "this Team server's catalog has not arrived — the Team servers section says why.",
  'no-server': 'no Team server on this side matches this reviewer — add it under Team servers, or remove the row.',
};

/**
 * The count, in a sentence rather than a template with a number bolted to it.
 *
 * <p>It does NOT substitute a friendly word for an empty `named`. That guard was written here first
 * and removed: it made the caption read plausibly while the LOOKUP behind it searched the catalog
 * for a vendor called nothing, and it silently disarmed the test for exactly that defect — the test
 * passed with the bug in place, which is the one failure mode a test cannot have. The caller
 * guarantees a name (`vendorsFrom` refuses a row with an empty id), so a blank one here should look
 * as wrong as it is.</p>
 */
function allowedNote(remote: RemoteProvenance): string {
  // Zero is a real state, not an error: a server can offer a vendor and allow it no models at all,
  // and "0 models this Team server allows" is a sentence nobody writes.
  if (remote.models.length === 0) {
    return `this Team server allows '${remote.named}' no models at all.`;
  }

  return remote.models.length === 1
    ? `the one model this Team server allows for '${remote.named}'.`
    : `the ${remote.models.length} models this Team server allows for '${remote.named}'.`;
}

export function modelsProvenance(
  runtime: Runtime,
  discoveredCodex: readonly ModelChoice[],
  localEngine?: LocalEngine,
  discoveredAgy: readonly ModelChoice[] = [],
  remote: RemoteProvenance = NO_REMOTE_CATALOG,
): string {
  // A Team-server row runs no CLI on this machine and has no cache here: its list came over HTTP
  // from a server's catalog. Without this arm the function fell through to the codex sentence, so a
  // Team-server reviewer was captioned "8 models the Codex CLI has cached for this machine" — a
  // claim about software that has nothing to do with it, under a dropdown filled from an HTTP
  // response. Reported from a screenshot, 2026-09-07.
  //
  // A state rather than a length test, because the not-yet-answered case deliberately returns the
  // row's own model rather than an empty list, so a count of one cannot tell the two apart.
  if (runtime === 'remote') {
    return remote.catalog === 'here' ? allowedNote(remote) : CATALOG_NOT_HERE[remote.catalog];
  }

  if (runtime === 'local') {
    // The engine's own note carries the reason when nothing answered, which is the case this line
    // exists for: an empty dropdown with no explanation reads as "you have no models".
    return localEngine === undefined ? 'no engine probed yet.' : engineNote(localEngine);
  }
  if (runtime === 'gemini') {
    return 'a curated list — the Gemini CLI publishes none. Any other model can be typed in.';
  }
  if (runtime === 'claude') {
    return 'aliases the Claude CLI resolves to the latest of each family. Any exact id can be typed in.';
  }
  if (runtime === 'antigravity') {
    // The truth about where the list came from, which this line used to state wrongly: it claimed
    // the CLI's own answer while offering a snapshot taken when the file was written — and the
    // snapshot was a model generation behind before anybody noticed.
    return discoveredAgy.length > 0
      ? `${discoveredAgy.length} models \`agy models\` lists for this subscription.`
      : 'a list from when this was written — `agy` did not answer, so it may be behind. Any id can be typed in.';
  }
  return discoveredCodex.length > 0
    ? `${discoveredCodex.length} models the Codex CLI has cached for this machine.`
    : 'the Codex CLI has cached no model list yet — type a model, or run codex once.';
}

// `hostPlatform` lived here for exactly one message — the WSL advice in `engineNote` — and it was
// the wrong question: `process.platform` is 'linux' both in a WSL distro and on a native Linux box,
// and only one of them has a `.wslconfig` to edit. The engine now carries whether the probe ran
// under WSL, which is the fact the message actually needed.


/**
 * MOVED HERE from `panelView.ts` (2026-09-09), unchanged.
 *
 * <p>It was always pure — it reads a row and a list of server states and returns three fields — but
 * it lived in the file that renders the panel, so anything else that needed the same answer had to
 * import the view to get it. The chat picker needs exactly this answer, and a pure module importing
 * a webview renderer to ask what a Team server allows is the coupling that makes a lane boundary
 * meaningless. It sits beside `modelsFor`, which consumes what it returns, and beside
 * `RemoteProvenance`, which was already declared here. (gemini, the plan round.)</p>
 */
/**
 * What a Team server allows, and whether the server has actually said so.
 *
 * <p>The two cannot be collapsed into a list length. When the catalog has not arrived this returns
 * the row's OWN model — never an empty list, because an empty one means "your server withdrew this
 * model" and `modelsFor` would mark it so, on every reload, for as long as a server stayed
 * unreachable. So a one-item list is ambiguous by construction, and the caption has to be told which
 * of the two it is looking at rather than counting.</p>
 *
 * <p>The shape is `RemoteProvenance`, declared beside the caption that reads it. It was declared
 * twice for one release — the same three fields, once here and once there — which is the beginning
 * of the drift this plan spent two stories closing on the other seam.</p>
 */
export function allowedModelsFor(vendor: Vendor, servers: readonly TeamServerState[]): RemoteProvenance {
  if (vendor.runtime !== 'remote') {
    return { models: [], named: vendor.id, catalog: 'no-server' };
  }

  // By ID first, because an address is CORRECTABLE and an id is not: fixing a typo in a hostname
  // would otherwise orphan every row that matched the old string. The address is the fallback, for
  // rows written before the id was recorded — and it is compared CANONICALLY, never as typed, since
  // the row stores one spelling and the server entry holds whatever the person entered.
  const url = canonicalTeamServerUrl(vendor.baseUrl);
  const server = servers.find((s) => (vendor.teamServerId !== undefined && vendor.teamServerId.length > 0)
    ? s.server.id === vendor.teamServerId
    : canonicalTeamServerUrl(s.server.url) === url);
  // Guarded by `typeof`, not by `!== undefined`: a JSON null passes the second and then has
  // `.length` read off it, and this is a field a person can hand-write. Length-checked rather than
  // `??`, because nullish coalescing keeps an EMPTY string — a row carrying `remoteVendor: ''` would
  // be looked up in the catalog under a blank name. The C# side of this seam asks the same question
  // the same way (`VendorIdentity.Recorded`), and two halves of one contract disagreeing about what
  // counts as absent is how this whole plan started. Both raised on this story's rounds.
  const named = typeof vendor.remoteVendor === 'string' && vendor.remoteVendor.length > 0
    ? vendor.remoteVendor
    : vendor.id;

  // Three states, not two. "No server entry matches this row" is not "its catalog has not arrived
  // yet" — it is what a person is left with after removing a Team server and keeping its reviewers,
  // and sending them to a section that no longer lists their server is worse than saying nothing.
  if (server === undefined) {
    return { models: [vendor.model], named, catalog: 'no-server' };
  }

  if (server.catalog === undefined) {
    // NOT an empty allowlist. An empty one means "this server no longer offers your model", and
    // `modelsFor` would then mark every remote row's saved model as withdrawn — on every reload,
    // before the first fetch has landed, and for as long as a server stays unreachable. A person
    // would read that as their configuration having been dropped. Caught on the code round.
    return { models: [vendor.model], named, catalog: 'waiting' };
  }

  const wanted = named.toLowerCase();

  return {
    // Case-insensitively, because `RemoteProbe.Read` on the other side of this seam compares with
    // `OrdinalIgnoreCase`. A server whose catalog says `DeepSeek` answers a row that recorded
    // `deepseek` perfectly well, and this comparison showed that same row an empty dropdown and a
    // caption saying the server allows it nothing. Neither side may lower-case the name it SENDS —
    // that is the server's own spelling — but both must agree about which names are the same one.
    // Found by the automated reviewer on this change's pull request.
    // `named` is lower-cased ONCE rather than inside the predicate. It was a fresh string allocation
    // per catalog entry, per remote row, per rebuild of the picker — invisible at three rows and not
    // at thirty. (gemini, the code round.)
    models: (server.catalog.vendors ?? [])
      .find((v) => v.id.toLowerCase() === wanted)?.models ?? [],
    named,
    catalog: 'here',
  };
}
