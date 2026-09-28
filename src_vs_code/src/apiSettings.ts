/**
 * Per-model settings of an `api` row on the panel's side (story S3.8 of `todo/PLAN_feature_review.md`, the
 * extension half): what `coai-mcp` reports a row's vendor module can be told, and the three settings a
 * person may set over what calibration settled — the thinking switch, the reasoning effort, the whole-review
 * limit in minutes.
 *
 * <p><b>The panel decides nothing about a vendor.</b> The levels a dropdown offers, whether a switch exists,
 * the calibrated defaults and the refusal of a row's own settings are all the MODULE's (`IApiVendor` in
 * `src_mcp/core/Api`), read off `providers` — never a list typed on this side (the operator's requirement,
 * 2026-09-27). What lives here is how that report is read, how the three values are stored on the row, what
 * crosses to the server, and the one rule about a default: a value equal to it is not stored, so the row
 * keeps following the next calibration.</p>
 *
 * <p>Pure and `vscode`-free, and the page bundle imports it through `providers.ts`; the row is a structural
 * type because `vendors.ts` imports from here (`importCycles.test.mjs`).</p>
 */

import { compareVersions } from './coaiInstall';

/**
 * The first `coai-mcp` that reads `effort` / `thinking` / `reviewMinutes` off a row and reports `api` on
 * `providers`: the release after `mcp-v0.39.0`, which was cut on 2026-09-26 without the vendor modules —
 * checked by ancestry (`git merge-base --is-ancestor 09aa3d33 mcp-v0.39.0` is false), not assumed from the
 * date. If another release is cut before this branch merges, move this with it.
 */
export const API_SETTINGS_SINCE = '0.40.0';

/** The three settings, by the names the row, the wire and `PanelSettings.ParseVendors` share. */
export const API_SETTING_KEYS = ['effort', 'thinking', 'reviewMinutes'] as const;

export type ApiSettingKey = (typeof API_SETTING_KEYS)[number];

/** A whole-review limit longer than a day is not a limit anybody meant; the box refuses it rather than guess. */
export const MAX_REVIEW_MINUTES = 1440;

/** What the vendor can be told — `ApiCapabilities` as the panel needs it. */
export interface ApiCapabilities {
  readonly thinkingSwitchable: boolean;
  /** The vendor's own effort words, in its own order — exactly the dropdown's options. */
  readonly effortLevels: readonly string[];
  /** The effort word that switches thinking off (`none` on qwen3.8-max), or empty. */
  readonly thinkingOffLevel: string;
}

/** Three of `ApiDefaults` / `ApiEffective` — the ones a person can set. */
export interface ApiSettingValues {
  readonly effort: string;
  readonly thinkingOn: boolean;
  readonly reviewMinutes: number;
}

/** `ApiRowReport`: one api row through its module's eyes, as `providers` reports it. */
export interface ApiReport {
  readonly module: string;
  /** The one model the module was measured on — empty for the generic module, whose defaults nobody calibrated. */
  readonly measuredModel: string;
  readonly capabilities: ApiCapabilities;
  readonly defaults: ApiSettingValues;
  /** What the row RUNS with: its own settings over the environment over the defaults — reported, not configured. */
  readonly effective: ApiSettingValues;
  /** Why the row's own settings cannot be sent, or empty. */
  readonly refusal: string;
  /** Why the row's named module was set aside for its model, or empty. */
  readonly note: string;
}

/** What this file asks of a row — structural, because `vendors.ts` imports from here. */
export interface ApiSettingsRow {
  readonly runtime: string;
  readonly effort?: string | undefined;
  readonly thinking?: boolean | undefined;
  readonly reviewMinutes?: number | undefined;
}

/** An effort word as a vendor spells one: lower-case letters, digits, `-` and `_`. Anything else is not a level. */
const LEVEL = /^[a-z0-9][a-z0-9_-]{0,31}$/u;

/**
 * The report `providers` carried for a row, read defensively — or nothing, when there is no report or it
 * lacks what a control is drawn from. Nothing is the older server's answer too, which is what hides the
 * controls rather than drawing them from a guess.
 */
export function apiReportFrom(raw: unknown): ApiReport | undefined {
  const fields = recordOf(raw);
  const capabilities = capabilitiesFrom(fields['capabilities']);
  const defaults = valuesFrom(fields['defaults']);
  if (capabilities === undefined || defaults === undefined) {
    return undefined;
  }

  return {
    module: text(fields['module']),
    measuredModel: text(fields['measuredModel']),
    capabilities,
    defaults,
    effective: valuesFrom(fields['effective']) ?? defaults,
    refusal: text(fields['refusal']),
    note: text(fields['note']),
  };
}

function capabilitiesFrom(raw: unknown): ApiCapabilities | undefined {
  const fields = recordOf(raw);
  if (typeof fields['thinkingSwitchable'] !== 'boolean' || !Array.isArray(fields['effortLevels'])) {
    return undefined;
  }

  return {
    thinkingSwitchable: fields['thinkingSwitchable'],
    effortLevels: levelsFrom(fields['effortLevels']),
    thinkingOffLevel: text(fields['thinkingOffLevel']).toLowerCase(),
  };
}

/** The levels a vendor declared, trimmed and de-duplicated in its order; a non-word is not a level. */
function levelsFrom(raw: readonly unknown[]): readonly string[] {
  const words = raw
    .filter((one): one is string => typeof one === 'string')
    .map((one) => one.trim().toLowerCase())
    .filter((one) => LEVEL.test(one));

  return [...new Set(words)];
}

function valuesFrom(raw: unknown): ApiSettingValues | undefined {
  const fields = recordOf(raw);
  if (typeof fields['thinkingOn'] !== 'boolean' || !isWholeMinutes(fields['reviewMinutes'])) {
    return undefined;
  }

  return { effort: text(fields['effort']).toLowerCase(), thinkingOn: fields['thinkingOn'], reviewMinutes: fields['reviewMinutes'] };
}

function recordOf(raw: unknown): Record<string, unknown> {
  return typeof raw === 'object' && raw !== null && !Array.isArray(raw) ? (raw as Record<string, unknown>) : {};
}

function text(raw: unknown): string {
  return typeof raw === 'string' ? raw.trim() : '';
}

/** A whole, positive number of minutes — what the server reports; the environment may set more than the box allows. */
function isWholeMinutes(raw: unknown): raw is number {
  return typeof raw === 'number' && Number.isInteger(raw) && raw >= 1;
}

/**
 * Whether the installed server reads the three settings — true for an UNKNOWN version too, as in every
 * other skew gate here: holding a person's settings back on a guess would run a server able to honour them
 * on the defaults instead.
 */
export function apiSettingsOnServer(installedServerVersion: string): boolean {
  return installedServerVersion.length === 0 || compareVersions(API_SETTINGS_SINCE, installedServerVersion) <= 0;
}

// ---------------------------------------------------------------- the stored row

/** The three settings as a row stores them: each only when it was SAID, as a value the server can read. */
export type StoredApiSettings = { effort?: string; thinking?: boolean; reviewMinutes?: number };

/**
 * The three settings off a stored row — each absent unless it is a value somebody could have meant: an
 * effort word, a boolean switch, a whole number of minutes the box would accept. `coai.vendors` is JSON a
 * person edits, and a value kept because it merely exists would cross to the server as a setting.
 */
export function storedApiSettings(v: Record<string, unknown>): StoredApiSettings {
  return { ...storedEffort(v['effort']), ...storedThinking(v['thinking']), ...storedMinutes(v['reviewMinutes']) };
}

function storedEffort(raw: unknown): { effort?: string } {
  const effort = wordOf(raw);

  return LEVEL.test(effort) ? { effort } : {};
}

function storedThinking(raw: unknown): { thinking?: boolean } {
  return typeof raw === 'boolean' ? { thinking: raw } : {};
}

function storedMinutes(raw: unknown): { reviewMinutes?: number } {
  return isMinutes(raw) ? { reviewMinutes: raw } : {};
}

/** A stored or typed word, trimmed and lower-cased like every name the server normalises; not a string is empty. */
function wordOf(raw: unknown): string {
  return typeof raw === 'string' ? raw.trim().toLowerCase() : '';
}

/** A whole number of minutes the box accepts: at least one, at most a day. */
function isMinutes(raw: unknown): raw is number {
  return isWholeMinutes(raw) && raw <= MAX_REVIEW_MINUTES;
}

// ---------------------------------------------------------------- the wire

/**
 * What an `api` row tells the server about its settings: what it said, and nothing it did not — absent is
 * the module's default, which is how a default keeps following calibration. Nothing for any other runtime,
 * and nothing for a server KNOWN to be older than {@link API_SETTINGS_SINCE}: that one ignores the three
 * names (measured, `live-api-settings-compat.mjs`), and a file carrying them would claim settings the
 * running server does not honour while the card says they cannot be set. The row itself still crosses.
 */
export function apiSettingsOnTheWire(v: ApiSettingsRow, installedServerVersion: string): StoredApiSettings {
  return v.runtime === 'api' && apiSettingsOnServer(installedServerVersion)
    ? storedApiSettings(v as unknown as Record<string, unknown>)
    : {};
}

// ---------------------------------------------------------------- a write

/**
 * The row after a person set one of the three — or `undefined` when the value is refused and nothing is to
 * be written. A value equal to the calibrated default is stored as NOTHING, so the row keeps following the
 * next calibration; an empty box is the default. Refused: an effort the module does not declare, a thinking
 * switch the vendor does not have, a limit that is not a whole number of minutes from one to a day.
 *
 * <p>Without a report (the server has not said what the module takes) the value is kept as long as it is a
 * well-formed setting: nothing here can compare it with a default it was never told. The card draws no
 * control in that state, so this is a hand-typed message's path, not a person's.</p>
 */
export function withApiSetting<T extends ApiSettingsRow>(row: T, key: ApiSettingKey, value: unknown, report: ApiReport | undefined): T | undefined {
  const wanted = SETTINGS[key].read(value, report);
  if (wanted === REFUSED) {
    return undefined;
  }

  return wanted === DEFAULT || SETTINGS[key].isDefault(wanted, report) ? withoutApiSetting(row, key) : { ...row, [key]: wanted };
}

/** The row without its own value for one setting — what "reset to calibrated default" does. */
export function withoutApiSetting<T extends ApiSettingsRow>(row: T, key: ApiSettingKey): T {
  const { [key]: _dropped, ...rest } = row;

  return rest as T;
}

/** A value that must not be written, and a value that means "the default" — two answers a parse can give. */
const REFUSED = Symbol('refused');
const DEFAULT = Symbol('default');

type Parsed = string | boolean | number | typeof REFUSED | typeof DEFAULT;

interface SettingRule {
  readonly read: (value: unknown, report: ApiReport | undefined) => Parsed;
  readonly isDefault: (value: string | boolean | number, report: ApiReport | undefined) => boolean;
}

/** One rule per setting — the table is what keeps each decision below four branches. */
const SETTINGS: Readonly<Record<ApiSettingKey, SettingRule>> = {
  effort: { read: effortOf, isDefault: (value, report) => value === report?.defaults.effort },
  thinking: { read: thinkingOf, isDefault: (value, report) => value === report?.defaults.thinkingOn },
  reviewMinutes: { read: minutesOf, isDefault: (value, report) => value === report?.defaults.reviewMinutes },
};

function effortOf(value: unknown, report: ApiReport | undefined): Parsed {
  const word = wordOf(value);
  if (word.length === 0) {
    return DEFAULT;
  }

  return takes(report, word) ? word : REFUSED;
}

/** Whether the module takes this word: one of its levels — or any well-formed word, when nobody reported what it takes. */
function takes(report: ApiReport | undefined, word: string): boolean {
  return LEVEL.test(word) && (report === undefined || report.capabilities.effortLevels.includes(word));
}

function thinkingOf(value: unknown, report: ApiReport | undefined): Parsed {
  if (typeof value !== 'boolean') {
    return REFUSED;
  }

  return value || canSwitchOff(report) ? value : REFUSED;
}

/** Whether thinking may be switched off: the module says so — or nobody reported, and the server will judge. */
function canSwitchOff(report: ApiReport | undefined): boolean {
  return report === undefined || report.capabilities.thinkingSwitchable;
}

function minutesOf(value: unknown): Parsed {
  if (isBlank(value)) {
    return DEFAULT;
  }

  return isMinutes(value) ? value : REFUSED;
}

/** An emptied box, as the page posts one: `''`, `0` (`Number('')`), or not a number at all. */
function isBlank(value: unknown): boolean {
  return value === '' || value === 0 || Number.isNaN(value);
}

// ---------------------------------------------------------------- reset

/**
 * The row and the setting a reset button names — `<vendor id>:<setting>`, the `customCommandModel` shape —
 * or nothing, when the id names no setting of the three. The LAST colon splits, so no vendor id is cut.
 */
export function resetTarget(id = ''): { vendor: string; key: ApiSettingKey } | undefined {
  const at = id.lastIndexOf(':');

  return at > 0 ? targetOf(id.slice(0, at), id.slice(at + 1)) : undefined;
}

function targetOf(vendor: string, key: string): { vendor: string; key: ApiSettingKey } | undefined {
  return isApiSettingKey(key) ? { vendor, key } : undefined;
}

export function isApiSettingKey(key: string): key is ApiSettingKey {
  return (API_SETTING_KEYS as readonly string[]).includes(key);
}
