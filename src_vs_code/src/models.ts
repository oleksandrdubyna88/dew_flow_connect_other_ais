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
export type Runtime = 'codex' | 'gemini' | 'claude' | 'antigravity';

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
): ModelChoice[] {
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
export function modelsProvenance(runtime: Runtime, discoveredCodex: readonly ModelChoice[]): string {
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
