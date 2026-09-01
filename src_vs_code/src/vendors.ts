import { Runtime } from './models';

/** Every CLI shape this build can drive. Kept beside the parser that has to recognise them. */
const RUNTIMES: readonly Runtime[] = ['codex', 'gemini', 'claude', 'antigravity'];

/**
 * The reviewers, as an editable LIST rather than a fixed three.
 *
 * <p>The panel can add and remove them because the panel is not the limit: a new vendor with an
 * OpenAI-compatible endpoint is a base URL and a key, and the Codex CLI already knows how to be
 * pointed at one. Hard-coding three would have made every future vendor a release.</p>
 */
export interface Vendor {
  readonly id: string;
  readonly runtime: Runtime;
  readonly model: string;
  readonly enabled: boolean;
  /** OpenAI-compatible endpoint, for a vendor riding the Codex runtime. Empty = the CLI's own. */
  readonly baseUrl: string;
}

/** What a fresh install reviews with: the two vendors whose CLIs authenticate themselves. */
export const DEFAULT_VENDORS: readonly Vendor[] = [
  { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '' },
  { id: 'gemini', runtime: 'gemini', model: '', enabled: true, baseUrl: '' },
];

/**
 * Offered by "Add a reviewer…" — presets, not a closed set; the last is a blank to fill in.
 *
 * <p><b>Every default vendor is listed here too</b>, which is not redundancy: remove gemini and
 * the list it came from was the only place it existed, so it could never be added back. A default
 * that cannot be restored is a one-way door, and the operator walked through it.</p>
 */
export const VENDOR_PRESETS: readonly (Vendor & { label: string; hint: string })[] = [
  {
    label: 'Codex (OpenAI)',
    hint: 'The Codex CLI, signed in as itself — the panel’s default first reviewer.',
    id: 'codex',
    runtime: 'codex',
    model: '',
    enabled: true,
    baseUrl: '',
  },
  {
    label: 'Gemini (Google)',
    hint: 'The Gemini CLI, signed in as itself. Needs an interactive login or a key in the vault.',
    id: 'gemini',
    runtime: 'gemini',
    model: '',
    enabled: true,
    baseUrl: '',
  },
  {
    label: 'Claude (a second one)',
    hint: 'A separate claude -p process: it sees the plan and the diff, never the conversation that produced them.',
    id: 'claude',
    runtime: 'claude',
    model: 'haiku',
    enabled: true,
    baseUrl: '',
  },
  {
    label: 'DeepSeek',
    hint: 'Rides the Codex CLI against api.deepseek.com — needs a key in the vault entry.',
    id: 'deepseek',
    runtime: 'codex',
    model: 'deepseek-chat',
    enabled: true,
    baseUrl: 'https://api.deepseek.com/v1',
  },
  {
    label: 'OpenRouter',
    hint: 'One key, many models, through the Codex CLI.',
    id: 'openrouter',
    runtime: 'codex',
    model: '',
    enabled: true,
    baseUrl: 'https://openrouter.ai/api/v1',
  },
  {
    label: 'Another OpenAI-compatible endpoint',
    hint: 'Give it a name and a base URL; the key goes in the vault entry under that name.',
    id: '',
    runtime: 'codex',
    model: '',
    enabled: true,
    baseUrl: '',
  },
];

/** Read whatever is stored, keeping only entries that could actually be run. */
export function vendorsFrom(value: unknown): Vendor[] {
  if (!Array.isArray(value)) {
    return [...DEFAULT_VENDORS];
  }
  const vendors = value
    .filter((v): v is Record<string, unknown> => typeof v === 'object' && v !== null)
    .map((v) => ({
      id: typeof v['id'] === 'string' ? v['id'].trim().toLowerCase() : '',
      // An unknown runtime becomes `codex` because that is the one that takes a base URL — but
      // every runtime this build KNOWS must be listed in RUNTIMES. A missing name silently runs
      // the wrong vendor's model and reports the answer under the right vendor's name, which is
      // exactly the defect the server side had until a reviewer named it.
      runtime: RUNTIMES.includes(v['runtime'] as Runtime) ? (v['runtime'] as Runtime) : ('codex' as const),
      model: typeof v['model'] === 'string' ? v['model'].trim() : '',
      enabled: v['enabled'] !== false,
      baseUrl: typeof v['baseUrl'] === 'string' ? v['baseUrl'].trim() : '',
    }))
    .filter((v) => v.id.length > 0);

  // A stored list that names nothing runnable is not a configuration, it is an accident.
  return vendors.length > 0 ? dedupe(vendors) : [...DEFAULT_VENDORS];
}

/** One id, one vendor: two rows with the same name would fight over the same key and env. */
function dedupe(vendors: Vendor[]): Vendor[] {
  const seen = new Set<string>();
  return vendors.filter((v) => (seen.has(v.id) ? false : (seen.add(v.id), true)));
}

/** A name a person typed → something usable as an id, an env-var suffix and a vault key. */
export function normaliseId(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/**
 * The environment the server reads: the vendor list as JSON, because a comma-separated string
 * cannot carry a runtime and a base URL, and inventing a second encoding for them would be a
 * format nobody could read in a config file.
 */
export function vendorsEnv(vendors: readonly Vendor[]): string {
  return JSON.stringify(
    vendors
      .filter((v) => v.enabled)
      .map((v) => ({ id: v.id, runtime: v.runtime, model: v.model, baseUrl: v.baseUrl })),
  );
}
