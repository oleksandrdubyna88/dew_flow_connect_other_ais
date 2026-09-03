import { engineNote, LocalEngine } from './localEngines';
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
export const RUNTIMES = ['codex', 'gemini', 'claude', 'antigravity', 'local'] as const;

export type Runtime = (typeof RUNTIMES)[number];

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
): ModelChoice[] {
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
          ? [...ANTIGRAVITY_MODELS]
          : [...CURATED_GEMINI_MODELS];
  if (current.length > 0 && !base.some((m) => m.id === current)) {
    base.unshift({ id: current, label: `${current} (yours)` });
  }
  return base;
}

/**
 * What `agy models` lists on a Google AI Pro subscription.
 *
 * <p>One subscription, three families: the effort level is part of the model id rather than a
 * separate setting, which is why `-high` and `-low` are listed as distinct choices.</p>
 */
const ANTIGRAVITY_MODELS: readonly ModelChoice[] = [
  { id: 'gemini-3.7-flash-high', label: 'Gemini 3.7 Flash (High)' },
  { id: 'gemini-3.7-flash-medium', label: 'Gemini 3.7 Flash (Medium)' },
  { id: 'gemini-3.7-flash-low', label: 'Gemini 3.7 Flash (Low)' },
  { id: 'gemini-3.1-pro-high', label: 'Gemini 3.1 Pro (High)' },
  { id: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 (Thinking)' },
  { id: 'claude-opus-4-6-thinking', label: 'Claude Opus 4.6 (Thinking)' },
  { id: 'gpt-oss-120b-medium', label: 'GPT-OSS 120B (Medium)' },
];

/** Where the list came from, said in the panel so nobody mistakes curation for discovery. */
export function modelsProvenance(
  runtime: Runtime,
  discoveredCodex: readonly ModelChoice[],
  localEngine?: LocalEngine,
): string {
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
    return 'what `agy models` lists for this subscription — Gemini, Claude and GPT-OSS through one CLI.';
  }
  return discoveredCodex.length > 0
    ? `${discoveredCodex.length} models the Codex CLI has cached for this machine.`
    : 'the Codex CLI has cached no model list yet — type a model, or run codex once.';
}

// `hostPlatform` lived here for exactly one message — the WSL advice in `engineNote` — and it was
// the wrong question: `process.platform` is 'linux' both in a WSL distro and on a native Linux box,
// and only one of them has a `.wslconfig` to edit. The engine now carries whether the probe ran
// under WSL, which is the fact the message actually needed.
