import { Runtime, RUNTIMES } from './models';

/** Every CLI shape this build can drive. Kept beside the parser that has to recognise them. */

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
  /**
   * Where this vendor's CLI is. Empty = look it up on PATH.
   *
   * <p>PATH is not always able to answer, and WSL is the case that proves it: `codex` resolves
   * there to the Windows npm shim on the interop PATH, which runs Linux node against a Windows
   * install and dies on a missing native dependency. The native Linux one sits in
   * `~/.npm-global/bin` and until this field existed nothing could point at it — so a WSL round
   * failed every time, whatever anybody configured.</p>
   */
  readonly executablePath: string;
  /**
   * What this vendor bills, per million tokens. Zero means "not set".
   *
   * <p>From the PERSON, never from a table we ship. A shipped price list is wrong for anyone on a
   * flat subscription, wrong the first time a vendor changes a price, and wrong silently in both
   * cases. Only Claude reports its own cost; codex and antigravity report tokens and nothing else,
   * so every row in the spending section read a dash — true, and useless against the question a
   * person actually has.</p>
   */
  readonly pricePerMillionIn: number;
  readonly pricePerMillionOut: number;
}

/** The model an Antigravity row starts on: flash at high effort, the CLI's own active model. */
export const ANTIGRAVITY_DEFAULT_MODEL = 'gemini-3.7-flash-high';

/**
 * What a fresh install reviews with: the two vendors whose CLIs authenticate themselves.
 *
 * <p><b>Antigravity, not Gemini, since 2026-09-01.</b> Google retired Code Assist for individual
 * accounts and its CLI now refuses before it reaches a model. The adapter for the replacement had
 * shipped the day before and nothing used it: no preset offered it, every default still named
 * gemini, and a saved list therefore went on pointing at a closed door. Supporting a vendor and
 * DEFAULTING to it are different changes, and only the first one had been made.</p>
 */
export const DEFAULT_VENDORS: readonly Vendor[] = [
  { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  { id: 'antigravity', runtime: 'antigravity', model: ANTIGRAVITY_DEFAULT_MODEL, enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
];

/**
 * Offered by "Add a reviewer…" — presets, not a closed set; the last is a blank to fill in.
 *
 * <p><b>Every default vendor is listed here too</b>, which is not redundancy: remove gemini and
 * the list it came from was the only place it existed, so it could never be added back. A default
 * that cannot be restored is a one-way door, and the operator walked through it.</p>
 */
/**
 * A model served on this machine, or on a box you can reach.
 *
 * <p>Exported on its own because it is the one preset whose MODEL cannot be defaulted here: what is
 * installed is a fact about the machine the panel is running on, discovered at repaint. An empty
 * model means "the first one the engine reports", decided there rather than guessed here.</p>
 */
export const LOCAL_PRESET: Vendor & { label: string; hint: string } = {
  label: 'Local model (Ollama / vLLM)',
  hint: 'A model on this machine, through its OpenAI-compatible endpoint — no CLI, no key, no bill.',
  id: 'local',
  runtime: 'local',
  model: '',
  enabled: true,
  baseUrl: '',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

export const VENDOR_PRESETS: readonly (Vendor & { label: string; hint: string })[] = [
  {
    label: 'Codex (OpenAI)',
    hint: 'The Codex CLI, signed in as itself — the panel’s default first reviewer.',
    id: 'codex',
    runtime: 'codex',
    model: '',
    enabled: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Antigravity (Google)',
    hint: 'Google’s replacement for Code Assist — one subscription reaching Gemini, Claude and GPT-OSS.',
    id: 'antigravity',
    runtime: 'antigravity',
    model: ANTIGRAVITY_DEFAULT_MODEL,
    enabled: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Gemini (Google) — retired',
    hint: 'RETIRED by Google for individual accounts: it refuses before reaching a model. Kept only for a Workspace account that still has Code Assist.',
    id: 'gemini',
    runtime: 'gemini',
    model: '',
    enabled: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Claude (a second one)',
    hint: 'A separate claude -p process: it sees the plan and the diff, never the conversation that produced them.',
    id: 'claude',
    runtime: 'claude',
    model: 'haiku',
    enabled: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'DeepSeek',
    hint: 'Rides the Codex CLI against api.deepseek.com — needs a key in the vault entry.',
    id: 'deepseek',
    runtime: 'codex',
    model: 'deepseek-chat',
    enabled: true,
    baseUrl: 'https://api.deepseek.com/v1',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'OpenRouter',
    hint: 'One key, many models, through the Codex CLI.',
    id: 'openrouter',
    runtime: 'codex',
    model: '',
    enabled: true,
    baseUrl: 'https://openrouter.ai/api/v1',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Another OpenAI-compatible endpoint',
    hint: 'Give it a name and a base URL; the key goes in the vault entry under that name.',
    id: '',
    runtime: 'codex',
    model: '',
    enabled: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  LOCAL_PRESET,
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
      // An unknown runtime becomes `codex` because that is the one that takes a base URL, so a
      // name written by a NEWER extension still leaves a row that launches something.
      //
      // The list is imported rather than repeated, and that is not tidiness. It WAS repeated,
      // `local` was added to the type and not to the copy here, and every saved local reviewer
      // came back as a codex one — silently, under its own name. The comment that used to sit
      // here said the two must be kept in step; they are now one declaration instead.
      runtime: (RUNTIMES as readonly string[]).includes(v['runtime'] as string)
        ? (v['runtime'] as Runtime)
        : ('codex' as const),
      model: typeof v['model'] === 'string' ? v['model'].trim() : '',
      enabled: v['enabled'] !== false,
      baseUrl: typeof v['baseUrl'] === 'string' ? v['baseUrl'].trim() : '',
      executablePath: typeof v['executablePath'] === 'string' ? v['executablePath'].trim() : '',
      pricePerMillionIn: rate(v['pricePerMillionIn']),
      pricePerMillionOut: rate(v['pricePerMillionOut']),
    }))
    .filter((v) => v.id.length > 0)
    .map(migrateRetired);

  // A stored list that names nothing runnable is not a configuration, it is an accident.
  return vendors.length > 0 ? dedupe(vendors) : [...DEFAULT_VENDORS];
}

/**
 * A reviewer saved before the retirement, moved to the CLI Google pointed at.
 *
 * <p>The RUNTIME moves and the id stays: the id names the row, its usage history and its vault
 * key, so renaming it would orphan all three. The model goes with the runtime, because a model id
 * from the old CLI is not one the new CLI lists — <c>gemini-flash-latest</c> is not an `agy`
 * model, and leaving it would trade a dead CLI for a refused one.</p>
 *
 * <p>A vendor with its own base URL is never touched: that is not Google's CLI at all.</p>
 */
function migrateRetired(vendor: Vendor): Vendor {
  return vendor.runtime === 'gemini' && vendor.baseUrl.length === 0
    ? { ...vendor, runtime: 'antigravity', model: ANTIGRAVITY_DEFAULT_MODEL }
    : vendor;
}

/** One id, one vendor: two rows with the same name would fight over the same key and env. */
function dedupe(vendors: Vendor[]): Vendor[] {
  const seen = new Set<string>();
  return vendors.filter((v) => (seen.has(v.id) ? false : (seen.add(v.id), true)));
}

/** A rate is a non-negative number or it is unset; a negative price would credit the person. */
function rate(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : 0;
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
      .map((v) => ({
        id: v.id,
        runtime: v.runtime,
        model: v.model,
        baseUrl: v.baseUrl,
        executablePath: v.executablePath,
      })),
  );
}
