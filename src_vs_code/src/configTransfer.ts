import { promptFile } from './rolesPrompts';
import { sectionsOf } from './settingRefused';

/**
 * Export config / Import config — issue #467. The decisions, with nothing of VS Code in them.
 *
 * <p><b>What travels:</b> every setting whose BASE value differs from the default the manifest declares,
 * and the text of every prompt a person wrote (`<dataDir>/prompts/*.md`). <b>What never does:</b> a secret,
 * and this machine's layout — see {@link NEVER_TRANSFERRED}; and per-side overrides, which belong to one
 * side of one machine (an import writing them into the base layer would change every other side).</p>
 */

export const CONFIG_FORMAT = 'coai-config';
export const CONFIG_VERSION = 1;

/** Why a setting is never written to a config file, by key. The reason is what an import says. */
export const NEVER_TRANSFERRED: Readonly<Record<string, string>> = {
  credsKey: 'a secret: the access key to the credential vault',
  dataDirectory: 'a path on this machine',
  dataSide: 'which side of this machine this window is',
  alsoWatchDataDirectories: 'paths on this machine',
  perSideSettings: 'how this machine splits its settings between its sides',
};

/** The field inside a vendor or a consultant that is a path on this machine. */
const MACHINE_PATH_FIELD = 'executablePath';

/** What the manifest says about one setting: its default, and the JSON type(s) a value must have. */
export interface DeclaredSetting {
  readonly default?: unknown;
  readonly type?: string | readonly string[] | undefined;
}

/** The manifest's settings, by their key WITHOUT the `coai.` prefix. */
export type Declared = Readonly<Record<string, DeclaredSetting>>;

export interface ConfigFile {
  readonly format: typeof CONFIG_FORMAT;
  readonly version: number;
  readonly exportedAt: string;
  readonly note: string;
  readonly settings: Readonly<Record<string, unknown>>;
  readonly prompts: Readonly<Record<string, string>>;
}

export const TRANSFER_NOTE = 'Settings whose base value differs from the default, and prompt texts. '
  + 'Never a secret, a path on the exporting machine, or a per-side override.';

/**
 * The settings to export: every declared one whose base value differs from its default, none of the
 * never-transferred ones, and no machine path inside any value.
 *
 * @param baseValueOf the value in the BASE layer (`config.inspect(key).globalValue`), or undefined when
 *   the person never set it — undefined is the default, and is not exported
 */
export function exportedSettings(declared: Declared, baseValueOf: (key: string) => unknown): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const key of Object.keys(declared).sort(byName)) {
    // Stripped BEFORE the comparison: a value that differs from its default only by a path on this
    // machine is the default, and exporting it would overwrite the importer's own. (gemini, code round.)
    const value = withoutMachinePaths(baseValueOf(key));
    if (travels(key, value, defaultOf(declared, key))) {
      out[key] = value;
    }
  }

  return out;
}

/** Set, allowed to travel, and not the default. */
function travels(key: string, value: unknown, declaredDefault: unknown): boolean {
  return value !== undefined && neverWhy(key).length === 0 && canonical(value) !== canonical(declaredDefault);
}

/**
 * Why this key never travels, or empty. `Object.hasOwn`, never `in`: `toString in {}` is true, and the
 * reason then read back was a FUNCTION's source.
 */
function neverWhy(key: string): string {
  return Object.hasOwn(NEVER_TRANSFERRED, key) ? `never transferred: ${NEVER_TRANSFERRED[key] ?? ''}` : '';
}

function defaultOf(declared: Declared, key: string): unknown {
  return declared[key]?.default;
}

/** Every setting this build's manifest declares, by key without `coai.`, with its default. */
export function declaredSettings(manifest: unknown): Declared {
  const out: Record<string, DeclaredSetting> = {};
  for (const section of sectionsOf(manifest)) {
    for (const [full, schema] of Object.entries(recordOr(section['properties']))) {
      out[full.replace(/^coai\./, '')] = { default: recordOr(schema)['default'], type: declaredType(recordOr(schema)['type']) };
    }
  }

  return out;
}

function recordOr(value: unknown): Record<string, unknown> {
  return isRecord(value) ? value : {};
}

/** A schema's `type`: one name or a list of them; anything else declares nothing. */
function declaredType(value: unknown): string | readonly string[] | undefined {
  if (typeof value === 'string') {
    return value;
  }

  const names = Array.isArray(value) ? value.filter((one): one is string => typeof one === 'string') : [];

  return names.length > 0 ? names : undefined;
}

/** The whole file, ready to be written. */
export function configFile(
  settings: Readonly<Record<string, unknown>>,
  prompts: Readonly<Record<string, string>>,
  exportedAt: string,
): ConfigFile {
  return { format: CONFIG_FORMAT, version: CONFIG_VERSION, exportedAt, note: TRANSFER_NOTE, settings, prompts };
}

/** Whether a name is one a prompt file may carry — the SAME guard the roles page writes through. */
export function isPromptName(id: string): boolean {
  return promptFile('.', id) !== undefined;
}

export interface Refused {
  readonly name: string;
  readonly why: string;
}

export type Imported =
  | {
    readonly ok: true;
    readonly settings: Readonly<Record<string, unknown>>;
    readonly prompts: Readonly<Record<string, string>>;
    readonly refused: readonly Refused[];
  }
  | { readonly ok: false; readonly why: string };

/** A config file read back: what will be applied, and what is refused with its reason. */
export function importedConfig(text: string, declared: Declared): Imported {
  const file = parsed(text);
  const wrong = file === undefined ? 'The file is not JSON.' : whatIsWrongWith(file);
  if (file === undefined || wrong.length > 0) {
    return { ok: false, why: wrong };
  }

  const refused: Refused[] = [];
  const kept = entriesOf(recordOr(file['settings']), (key, value) => settingRefusal(key, value, declared), refused);
  refused.push(...pathRefusals(kept));
  const settings = Object.fromEntries(Object.entries(kept).map(([key, value]) => [key, withoutMachinePaths(value)]));
  const prompts = textsOf(entriesOf(recordOr(file['prompts']), promptRefusal, refused));

  return { ok: true, settings, prompts, refused };
}

/**
 * Every setting that carried a CLI path, which is never applied — issue #467, the code round (gemini).
 *
 * <p>A path in a file is a path on the machine that WROTE it, and one naming a binary for a vendor this
 * machine does not have yet would be run by the next round. The path is dropped and the confirmation
 * says so; the rest of the entry applies.</p>
 */
function pathRefusals(kept: Readonly<Record<string, unknown>>): Refused[] {
  return Object.keys(kept).sort(byName)
    .filter((key) => canonical(kept[key]) !== canonical(withoutMachinePaths(kept[key])))
    .map((key) => ({ name: `${key} → ${MACHINE_PATH_FIELD}`, why: 'a path on the machine that wrote the file' }));
}

function textsOf(kept: Readonly<Record<string, unknown>>): Record<string, string> {
  return Object.fromEntries(Object.entries(kept).flatMap(([id, text]) => (typeof text === 'string' ? [[id, text]] : [])));
}

function parsed(text: string): Record<string, unknown> | undefined {
  try {
    const value: unknown = JSON.parse(text);

    return isRecord(value) ? value : undefined;
  } catch {
    return undefined;
  }
}

function whatIsWrongWith(file: Record<string, unknown>): string {
  if (file['format'] !== CONFIG_FORMAT) {
    return 'This is not a ConnectOtherAIs config file.';
  }
  if (file['version'] !== CONFIG_VERSION) {
    return `This config file is version ${String(file['version'])}, and this build reads version ${CONFIG_VERSION}. `
      + 'Export it again from a build of the same version, or update this one.';
  }

  return hasBothSections(file) ? '' : 'The file has no settings or no prompts section.';
}

function hasBothSections(file: Record<string, unknown>): boolean {
  return isRecord(file['settings']) && isRecord(file['prompts']);
}

function entriesOf(
  from: Record<string, unknown>,
  refusal: (name: string, value: unknown) => string,
  refused: Refused[],
): Record<string, unknown> {
  const kept: Record<string, unknown> = {};
  for (const [name, value] of Object.entries(from)) {
    const why = refusal(name, value);
    if (why.length > 0) {
      refused.push({ name, why });
    } else {
      kept[name] = value;
    }
  }

  return kept;
}

function settingRefusal(key: string, value: unknown, declared: Declared): string {
  const never = neverWhy(key);
  if (never.length > 0) {
    return never;
  }

  return Object.hasOwn(declared, key) ? typeRefusal(value, declared[key]?.type) : 'unknown to this build';
}

/** Which JSON value each schema type name admits. A name not listed here admits anything. */
const FITS: ReadonlyMap<string, (value: unknown) => boolean> = new Map([
  ['string', (value: unknown) => typeof value === 'string'],
  ['number', (value: unknown) => typeof value === 'number'],
  ['integer', (value: unknown) => Number.isInteger(value)],
  ['boolean', (value: unknown) => typeof value === 'boolean'],
  ['array', (value: unknown) => Array.isArray(value)],
  ['object', (value: unknown) => isRecord(value)],
  ['null', (value: unknown) => value === null],
]);

/**
 * A value of a kind the manifest does not declare — a string where `vendors` is a list — refused by name.
 *
 * <p>The TYPE only, not the whole schema: a value of the right kind with a wrong field is what the
 * settings readers already tolerate and name, and a whole-schema validator is a dependency for a case
 * nobody has met. A value of the wrong KIND is what breaks a reader outright. (codex, the code round.)</p>
 */
function typeRefusal(value: unknown, type: string | readonly string[] | undefined): string {
  const allowed = typeNames(type);
  const fits = allowed.length === 0 || allowed.some((one) => FITS.get(one)?.(value) ?? true);

  return fits ? '' : `its value is not ${allowed.map(withArticle).join(' or ')}, which this build declares`;
}

function typeNames(type: string | readonly string[] | undefined): string[] {
  return typeof type === 'string' ? [type] : [...(type ?? [])];
}

function withArticle(type: string): string {
  return `${/^[aeiou]/.test(type) ? 'an' : 'a'} ${type}`;
}

function promptRefusal(id: string, value: unknown): string {
  if (!isPromptName(id)) {
    return 'not a valid prompt name';
  }

  return typeof value === 'string' ? '' : 'its text is not text';
}

/**
 * An imported value with this machine's own paths put back — issue #467's plan round.
 *
 * <p>Export leaves every `executablePath` out, so a whole-value import would wipe each local CLI path. The
 * same ENTRY — by `id` in a list, by key in a map — takes the path it already has here; an entry this
 * machine does not have yet gets none, which is what a new vendor has anyway.</p>
 */
export function withLocalPaths(imported: unknown, current: unknown): unknown {
  if (Array.isArray(imported)) {
    return imported.map((entry) => withPathOf(entry, sameId(current, entry)));
  }
  if (isRecord(imported) && isRecord(current)) {
    return Object.fromEntries(Object.entries(imported).map(([key, entry]) => [key, withPathOf(entry, current[key])]));
  }

  return imported;
}

function sameId(current: unknown, entry: unknown): unknown {
  const id = isRecord(entry) ? entry['id'] : undefined;

  return Array.isArray(current) && id !== undefined ? current.find((one) => isRecord(one) && one['id'] === id) : undefined;
}

function withPathOf(entry: unknown, local: unknown): unknown {
  const path = localPath(local);

  return isRecord(entry) && path.length > 0 ? { ...entry, [MACHINE_PATH_FIELD]: path } : entry;
}

/** The path this machine's entry carries, or empty. */
function localPath(local: unknown): string {
  const path = recordOr(local)[MACHINE_PATH_FIELD];

  return typeof path === 'string' ? path : '';
}

/** A value with every `executablePath` removed, at any depth. */
export function withoutMachinePaths(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(withoutMachinePaths);
  }

  return isRecord(value)
    ? Object.fromEntries(Object.entries(value)
      .filter(([key]) => key !== MACHINE_PATH_FIELD)
      .map(([key, inner]) => [key, withoutMachinePaths(inner)]))
    : value;
}

/** The order every list of names here is sorted in — `localeCompare`, as every other ordering in this extension. */
export function byName(left: string, right: string): number {
  return left.localeCompare(right);
}

/** A value's JSON with object keys sorted — equal values give equal text. */
export function canonical(value: unknown): string {
  return JSON.stringify(sortedKeys(value)) ?? 'undefined';
}

function sortedKeys(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(sortedKeys);
  }

  return isRecord(value)
    ? Object.fromEntries(Object.keys(value).sort(byName).map((key) => [key, sortedKeys(value[key])]))
    : value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * The confirmation an import asks — counts, what replaces text the person already has, and every refusal
 * with its reason.
 */
export function importQuestion(
  imported: Extract<Imported, { ok: true }>,
  file: string,
  promptsThatExist: readonly string[],
): string {
  const settings = Object.keys(imported.settings).length;
  const prompts = Object.keys(imported.prompts).length;
  const replaced = Object.keys(imported.prompts).filter((id) => promptsThatExist.includes(id)).length;
  const refused = imported.refused.map((one) => `${one.name} (${one.why})`).join('; ');

  return `Apply ${settings} setting(s) and ${prompts} prompt text(s) from ${file}?`
    + (replaced > 0 ? ` ${replaced} of the prompt texts replace text you already have.` : '')
    + (refused.length > 0 ? ` Not applied: ${refused}.` : '')
    + ' Settings the file does not name are left as they are; per-side overrides are not changed.';
}
