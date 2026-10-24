import { isMinutes } from './apiSettings';
import { isKib } from './promptCap';
import { saidText } from './saidText';
import { rowHasFastTier } from './fastTier';

/**
 * The fields a reviewer row gains as it becomes a catalog row (PLAN_one_model_catalog.md D1, E1.1).
 *
 * <p>Parsed here rather than in `vendorsFrom` because `vendors.ts` was at the 800-line limit, and kept
 * free of `vendors.ts` so the import graph gains no cycle. Every field is ABSENT unless it was said, as
 * every optional field of a row already is: `coai.vendors` is JSON a person reads, and a row written
 * before the catalog must parse to exactly what it parsed to before.</p>
 */

/** A non-review feature an instance may serve. The review stages stay the row's own flags. */
export type CatalogUse = 'security' | 'consultant' | 'qconsult' | 'chat' | 'bugz';

/** Every use, in the order the catalog stores and draws them. */
export const CATALOG_USES: readonly CatalogUse[] = ['security', 'consultant', 'qconsult', 'chat', 'bugz'];

export interface CatalogFields {
  name?: string;
  uses?: readonly CatalogUse[];
  systemPrompt?: string;
  timeoutMinutes?: number;
  maxPromptKib?: number;
  chatStartingPrompt?: string;
  stream?: boolean;
  fast?: FastSetting;
  safeMode?: 'off';
}

/** A fast mode that is not the default (research/PLAN_fast_mode.md): On, or As the CLI is set. Off is stored as nothing. */
export type FastSetting = 'on' | 'cli';

/** The catalog fields off a stored row, each only when it holds a value somebody could have meant. */
export function catalogFields(v: Record<string, unknown>): CatalogFields {
  const name = saidText(v['name']);
  const uses = usesFrom(v['uses']);

  return {
    ...(name === undefined ? {} : { name }),
    ...(uses.length === 0 ? {} : { uses }),
    ...promptField('systemPrompt', v['systemPrompt']),
    ...timeoutField(v['runtime'], v['timeoutMinutes']),
    ...maxPromptField(v['maxPromptKib']),
    ...promptField('chatStartingPrompt', v['chatStartingPrompt']),
    ...streamField(v['runtime'], v['stream']),
    ...fastField(v),
    ...safeModeField(v['runtime'], v['safeMode']),
  };
}

/** Known uses only, once each, in {@link CATALOG_USES} order — so two spellings of one list are one list. */
export function usesFrom(raw: unknown): readonly CatalogUse[] {
  const said: readonly unknown[] = Array.isArray(raw) ? raw : [];

  return CATALOG_USES.filter((use) => said.includes(use));
}

/**
 * A prompt as written — NOT trimmed, because its whitespace is the person's text — and absent when it
 * is not a string or holds nothing but spaces. Its length is judged by `catalogRefusal`, never cut here.
 */
function promptField(field: 'systemPrompt' | 'chatStartingPrompt', raw: unknown): CatalogFields {
  return typeof raw === 'string' && raw.trim().length > 0 ? { [field]: raw } : {};
}

/**
 * A row's fast mode (research/PLAN_fast_mode.md) — `on` or `cli` kept only on a row that has a tier; Off, the default, is
 * kept as nothing, so a row that never set it and one set to Off read alike.
 */
function fastField(v: Record<string, unknown>): CatalogFields {
  const said = FAST_SETTINGS.find((one) => one === v['fast']);

  return said !== undefined && rowHasFastTier(launchOf(v)) ? { fast: said } : {};
}

const FAST_SETTINGS: readonly FastSetting[] = ['on', 'cli'];

/** What decides a row's tier, off the stored row: its runtime, its model, and whether it is on somebody else's endpoint. */
function launchOf(v: Record<string, unknown>): { readonly runtime: string; readonly model: string; readonly baseUrl: string } {
  return { runtime: saidText(v['runtime']) ?? '', model: saidText(v['model']) ?? '', baseUrl: saidText(v['baseUrl']) ?? '' };
}

/**
 * A claude row's safe mode for its REVIEWER launches (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2) — kept
 * only as `off`, the one state a person chose; ON, the default, is kept as nothing, so a row that never set it and one set to on read
 * alike. Only a claude row has the switch: the other runtimes have no flag for it.
 */
function safeModeField(runtime: unknown, raw: unknown): CatalogFields {
  return runtime === 'claude' && raw === 'off' ? { safeMode: 'off' } : {};
}

/** An api row's stream switch (research/PLAN_api_streaming.md) — kept only when ON, so a switched-off row reads as one that never had it. */
function streamField(runtime: unknown, raw: unknown): CatalogFields {
  return runtime === 'api' && raw === true ? { stream: true } : {};
}

/** A CLI row's own limit; an api row has `reviewMinutes`, its module's calibrated value, instead. */
function timeoutField(runtime: unknown, raw: unknown): CatalogFields {
  return runtime !== 'api' && isMinutes(raw) ? { timeoutMinutes: raw } : {};
}

/** A row's prompt cap, kept on every runtime: the server leaves an over-cap reviewer out whatever the row runs on. */
function maxPromptField(raw: unknown): CatalogFields {
  return isKib(raw) ? { maxPromptKib: raw } : {};
}
