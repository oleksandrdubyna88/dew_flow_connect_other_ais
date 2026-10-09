/**
 * The vault's API keys as vendors in "Add a model" (PLAN_feature_review.md, story S3.6).
 *
 * <p>The operator's words, 2026-09-26: list every provider that is available, plus one entry per key
 * NAME in the vault's config entry, shown with a leading `!` because it is an API key — `!grok`,
 * `!qwen`. The server reads the vault and reports the names on `--providers`; the values stay in that
 * process. Nothing here can hold a key: the only vault-derived input is a list of names, and choosing
 * one writes a row that NAMES its key.</p>
 *
 * <p>Pure and `vscode`-free, so every decision is a unit test; the host in `panelProvider.ts` shows the
 * pick, asks for what is missing and saves the row.</p>
 */

import { DEFAULT_API_DIALECT } from './apiRuntime';
import { ProviderNotes } from './providers';
import { vaultKeyOf } from './vaultKey';
import { freeVendorId, normaliseId, Vendor } from './vendors';

/** The mark that says an entry is an API key from the vault, not a CLI. */
export const VAULT_KEY_MARK = '!';

/**
 * Where a key of a known name is sent, when the name alone says it — the two endpoints the operator's
 * keys were bought for (D9).
 *
 * <p>Data, and deliberately small: a name with no entry here is asked for its base URL, which is the
 * honest answer for an endpoint this build knows nothing about. The Qwen key is a Token Plan key, and
 * a Token Plan key has its OWN base URL, not the general DashScope one (Q5).</p>
 */
export const API_KEY_PRESETS: Readonly<Record<string, { readonly baseUrl: string; readonly vendor: string }>> = {
  grok: { baseUrl: 'https://api.x.ai/v1', vendor: 'xAI' },
  qwen: { baseUrl: 'https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1', vendor: 'Alibaba Model Studio (Token Plan)' },
};

/** The preset endpoint for a key's name, or empty when the name has none. */
export function presetEndpoint(keyName: string): string {
  return API_KEY_PRESETS[keyName.trim().toLowerCase()]?.baseUrl ?? '';
}

/** One row of the pick for a vault key — or, with an empty `keyName`, the one row that says why there are none. */
export interface VaultKeyItem {
  readonly label: string;
  readonly detail: string;
  readonly description: string;
  /** The vault key's name; empty for the explanatory row. */
  readonly keyName: string;
}

/** The three facts the pick needs from a providers answer. */
export type VaultNotes = Pick<ProviderNotes, 'vaultKeys' | 'vaultNote' | 'vault'>;

/**
 * The `!name` rows, in the vault's order — offered after everything else the pick already lists.
 *
 * <p><b>A key already used by a row is still listed</b>: a second model on one key is a thing a person
 * wants (qwen3.8-max and deepseek-v4-pro on one Token Plan key), and dropping the entry would be the
 * same one-way door the catalogue's own docblock records. It says which id the new row takes.</p>
 *
 * <p><b>No keys is said, not shown as nothing</b>, whenever there is a reason: the server did not
 * answer, is too old to name the vault's keys, or could not read the vault. A vault that was read and
 * holds no key adds nothing — that list is genuinely empty.</p>
 */
export function vaultKeyItems(notes: VaultNotes, answered: boolean, vendors: readonly Vendor[]): readonly VaultKeyItem[] {
  if (!answered || notes.vault !== 'read') {
    return [noKeysItem(answered ? notes : { ...notes, vault: 'not-reported' })];
  }
  const taken = new Set(vendors.map((vendor) => vendor.id));

  return notes.vaultKeys.map((keyName) => keyItem(keyName, vendors, taken));
}

/** One `!name` row, saying which id it would take when that is not the name itself. */
function keyItem(keyName: string, vendors: readonly Vendor[], taken: ReadonlySet<string>): VaultKeyItem {
  const users = vendors.filter((vendor) => vendor.runtime === 'api' && vaultKeyOf(vendor) === keyName).map((vendor) => vendor.id);
  const id = freeVendorId(normaliseId(keyName), taken);
  const second = users.length > 0 || id !== normaliseId(keyName);

  return {
    label: `${VAULT_KEY_MARK}${keyName}`,
    detail: keyDetail(keyName),
    description: second ? `${users.length > 0 ? `already used by ${users.join(', ')} — ` : ''}this adds ${id}` : '',
    keyName,
  };
}

/** The one row a pick shows when it can list no keys, carrying the reason in words. */
function noKeysItem(notes: VaultNotes): VaultKeyItem {
  const why = notes.vault === 'not-reported'
    ? 'the installed coai-mcp did not name them — update it (the MCP server tab) to list the vault’s API keys here'
    : `the vault could not be read: ${notes.vaultNote.length > 0 ? notes.vaultNote : 'no reason was given'}`;

  return { label: 'No API keys from the vault', detail: why, description: '', keyName: '' };
}

/** What a `!name` entry says it will do. */
function keyDetail(keyName: string): string {
  const preset = API_KEY_PRESETS[keyName];
  const where = preset === undefined
    ? 'you give its OpenAI-compatible base URL'
    : `${preset.vendor} at ${preset.baseUrl}`;

  return `An API key in your vault under “${keyName}” — ${where}; the model list is asked from the endpoint.`;
}


/**
 * The row a chosen `!name` becomes: an `api` reviewer that reads the key filed under that name.
 *
 * <p>The id is the name made into an id, or the next free `<name>-N` when one is taken — the same
 * allocation a second preset row gets. The KEY is the name as the vault spells it (lower-cased, as the
 * vault matches it), which may differ from the id: `qwen-2` reads `qwen`. It is written even when it
 * equals the id, so the row says which key it was made from.</p>
 */
export function vaultKeyRow(keyName: string, baseUrl: string, model: string, taken: ReadonlySet<string>): Vendor {
  const vaultKeyName = keyName.trim().toLowerCase();

  return {
    id: freeVendorId(normaliseId(vaultKeyName), taken),
    runtime: 'api',
    model,
    enabled: true,
    plan: true,
    code: true,
    baseUrl,
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    dialect: DEFAULT_API_DIALECT,
    vaultKeyName,
  };
}

/** What the endpoint's own `GET /models` answered, through `coai-mcp --probe-api` — the ids, or why there are none. */
export interface ProbedModels {
  readonly ids: readonly string[];
  readonly reason: string;
}

/**
 * The model ids out of the probe's report (`ProbeApiMode.Render`: `models.status`, `models.ids`,
 * `models.error`) — the probe's own models call, so there is one HTTP client for it and it runs where
 * the key is.
 *
 * <p>An endpoint that refused or answered nothing is a REASON, never an empty list pretending to be
 * the endpoint's answer: the host then asks for a model id by hand and says why.</p>
 */
export function probeModels(stdout: string): ProbedModels {
  const models = modelsSection(stdout);
  if (models === undefined) {
    return { ids: [], reason: 'coai-mcp --probe-api did not answer with a report' };
  }
  const ids = stringsOf(models.ids);

  return models.status === 200 && ids.length > 0 ? { ids, reason: '' } : { ids: [], reason: refusal(models) };
}

/** The report's `models` object, when the text is a report at all. */
type ModelsSection = { readonly status?: unknown; readonly ids?: unknown; readonly error?: unknown };

function modelsSection(stdout: string): ModelsSection | undefined {
  try {
    return sectionOf(JSON.parse(stdout));
  } catch {
    return undefined;
  }
}

function sectionOf(report: unknown): ModelsSection | undefined {
  const models = (report as { models?: unknown } | null)?.models;

  return typeof models === 'object' && models !== null ? models as ModelsSection : undefined;
}

/** The non-empty strings of a list; anything else is not a model id. */
function stringsOf(value: unknown): readonly string[] {
  return Array.isArray(value) ? value.filter((id): id is string => typeof id === 'string' && id.length > 0) : [];
}

/** Why the endpoint gave no list, in the probe's own terms: its status, and its redacted words. */
function refusal(models: ModelsSection): string {
  const said = typeof models.error === 'string' && models.error.length > 0 ? `: ${models.error}` : '';

  return `GET /models answered ${typeof models.status === 'number' ? models.status : 'nothing'}${said}`;
}
