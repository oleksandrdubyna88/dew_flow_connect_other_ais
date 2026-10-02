import { compareVersions } from './coaiInstall';
import { positive } from './consultSettings';

/**
 * The question consultant, as the panel holds it (todo/PLAN_question_consultant.md, S4).
 *
 * <p>The server owns the fan-out, the gate and every refusal (`QuestionConsultSettings`,
 * `QuestionConsultReader`, `QuestionRows.Parse`); this module is what a person chooses and the eight
 * `COAI_QCONSULT_*` keys it crosses the seam as. The `cadenceSettings.ts` shape: one reader the section and
 * the env block share, each key written only when it differs from the server's own default — so a pristine
 * panel sends nothing and the server's fallback is what runs, which is safe only while the defaults agree,
 * and `qconsultSettings.test.ts` reads the C# to keep them agreeing.</p>
 *
 * <p>Pure and `vscode`-free.</p>
 */

/** What the gate does about a question asked without the consultants. */
export type QconsultMode = 'off' | 'remind' | 'require';

/** The modes, in the order the panel offers them — the C# `QuestionConsultSettings.Modes`, checked by a test. */
export const QCONSULT_MODES: readonly QconsultMode[] = ['off', 'remind', 'require'];

/**
 * One row: a model and exactly ONE base prompt, on or off — the C# `QuestionRow`, field for field.
 *
 * <p>`acknowledged` is D13's tick: a pair whose runtime cannot be CONFINED (every codex pair; agy on disk)
 * runs only once the operator has said, on that row, that it may read this machine — and the server refuses
 * a flagged row without it (S3's deviation 1), so the tick is what this panel writes.</p>
 */
export interface QuestionRowSetting {
  readonly id: string;
  readonly vendor: string;
  readonly runtime: string;
  readonly model: string;
  readonly baseUrl: string;
  readonly executablePath: string;
  /** The vault entry an `api` row's key is filed under when it is not the vendor id — a NAME, never a value. */
  readonly key: string;
  readonly prompt: string;
  readonly enabled: boolean;
  readonly acknowledged: boolean;
}

/** A base prompt a person added — the C# `QuestionPromptSeedRow`. A shipped prompt is edited through its override file instead. */
export interface QuestionPromptSetting {
  readonly id: string;
  readonly title: string;
  readonly capability: string;
  readonly text: string;
}

export interface QconsultSettings {
  /** Off: `ask_consultants` answers `off`, and the gate stands down (D9). */
  readonly enabled: boolean;
  readonly mode: QconsultMode;
  readonly rows: readonly QuestionRowSetting[];
  readonly prompts: readonly QuestionPromptSetting[];
  /** The folders a `disk` row may read — absolute, existing, and none of the places D14 (c) refuses. */
  readonly roots: readonly string[];
  readonly rowMinutes: number;
  readonly questionsPerSession: number;
  readonly freeBatches: number;
}

/** The operator's accepted defaults — the server's `QuestionConsultSettings` initialisers, read from the C# by a test. */
export const DEFAULT_QCONSULT: QconsultSettings = {
  enabled: true,
  mode: 'require',
  rows: [],
  prompts: [],
  roots: [],
  rowMinutes: 5,
  questionsPerSession: 10,
  freeBatches: 2,
};

/** The most rows on at once — the C# `QuestionRows.MaxActive`, checked by a test. */
export const MAX_ACTIVE_ROWS = 6;

/** The stored keys this feature owns, so no list of them is written out twice. */
export const QCONSULT_SETTINGS: readonly string[] = [
  'qconsultEnabled', 'qconsultMode', 'qconsultRows', 'qconsultPrompts', 'qconsultRoots',
  'qconsultRowMinutes', 'qconsultQuestionsPerSession', 'qconsultFreeBatches',
];

/**
 * The first `coai-mcp` that reads any `COAI_QCONSULT_*` key — the release S5 cuts from this branch, the
 * minor after `mcp-v0.40.5`. If another release is cut before this branch merges, move this with it.
 *
 * <p>Below it the keys are KEPT OUT of the settings file (the `API_RUNTIME_SINCE` precedent): such a server
 * has no `ask_consultants`, so the rows would be keys nobody reads while the section suggested they run.</p>
 */
export const QCONSULT_SINCE = '0.41.0';

/** A raw configuration reader, as `settingsShape` defines it. */
type Read = (section: string) => unknown;

/**
 * The eight settings, parsed from whatever `settings.json` holds — read the way the SERVER reads them, so
 * the panel never shows a consultant the server does not run: a mode matched whatever its case, anything
 * else `require` (the server's catch-all); a count a positive whole number or the default (`IntVar`); the
 * seventh active row and after switched off (`QuestionRows.Capped`).
 */
export function qconsultSettingsFrom(read: Read): QconsultSettings {
  return {
    enabled: read('qconsultEnabled') !== false,
    mode: modeOf(read('qconsultMode')),
    rows: capped(listOf(read('qconsultRows')).flatMap(rowFrom)),
    prompts: listOf(read('qconsultPrompts')).flatMap(promptFrom),
    roots: listOf(read('qconsultRoots')).filter((one): one is string => typeof one === 'string' && one.trim().length > 0).map((one) => one.trim()),
    rowMinutes: positive(read('qconsultRowMinutes'), DEFAULT_QCONSULT.rowMinutes),
    questionsPerSession: positive(read('qconsultQuestionsPerSession'), DEFAULT_QCONSULT.questionsPerSession),
    freeBatches: positive(read('qconsultFreeBatches'), DEFAULT_QCONSULT.freeBatches),
  };
}

function modeOf(value: unknown): QconsultMode {
  const said = typeof value === 'string' ? value.trim().toLowerCase() : '';

  return QCONSULT_MODES.find((mode) => mode === said) ?? DEFAULT_QCONSULT.mode;
}

function listOf(value: unknown): readonly unknown[] {
  return Array.isArray(value) ? value : [];
}

/** An object's fields, or none — a stored entry that is not an object has no id and is dropped by the caller. */
function recordOf(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

/** A stored row, or none when it has no id to be named by. A row with no prompt is KEPT, so the section can say so. */
function rowFrom(value: unknown): readonly QuestionRowSetting[] {
  const row = recordOf(value);
  const id = text(row['id']);

  return id.length === 0 ? [] : [{
    id,
    vendor: text(row['vendor']),
    runtime: text(row['runtime']).toLowerCase(),
    model: text(row['model']),
    baseUrl: text(row['baseUrl']),
    executablePath: text(row['executablePath']),
    key: text(row['key']).toLowerCase(),
    prompt: text(row['prompt']),
    enabled: row['enabled'] !== false,
    acknowledged: row['acknowledged'] === true,
  }];
}

function promptFrom(value: unknown): readonly QuestionPromptSetting[] {
  const prompt = recordOf(value);
  const id = text(prompt['id']);

  return id.length === 0 ? [] : [{ id, title: text(prompt['title']), capability: text(prompt['capability']), text: text(prompt['text']) }];
}

/** The server's cap, mirrored: the seventh row that is on, and every one after it, is off. */
function capped(rows: readonly QuestionRowSetting[]): readonly QuestionRowSetting[] {
  let active = 0;

  return rows.map((row) => {
    active += row.enabled ? 1 : 0;

    return row.enabled && active > MAX_ACTIVE_ROWS ? { ...row, enabled: false } : row;
  });
}

/** Whether the installed server reads these keys — true for an UNKNOWN version, which is not an old one. */
export function qconsultOnServer(installedServerVersion: string): boolean {
  return installedServerVersion.length === 0 || compareVersions(QCONSULT_SINCE, installedServerVersion) <= 0;
}

/** The banner the section carries while the installed server is KNOWN to be older — or nothing. */
export function qconsultSkewNote(installedServerVersion: string): string {
  return qconsultOnServer(installedServerVersion)
    ? ''
    : `The coai-mcp you have installed (${installedServerVersion}) has no question consultant — `
      + `ask_consultants is not in it — so nothing set here is written to its settings file until you update it to ${QCONSULT_SINCE} `
      + 'or later — the MCP server tab.';
}

/**
 * The keys this feature adds to the server's environment — each only when it differs from the default, and
 * none at all for a server known to be too old to read them.
 */
export function qconsultEnv(settings: QconsultSettings, installedServerVersion = ''): Record<string, string> {
  if (!qconsultOnServer(installedServerVersion)) {
    return {};
  }

  return Object.fromEntries(QCONSULT_KEYS
    .filter(([, of]) => of(settings) !== of(DEFAULT_QCONSULT))
    .map(([key, of]) => [key, of(settings)]));
}

/** Each env key and the value it carries — one row per setting, so a ninth cannot be half-wired. */
const QCONSULT_KEYS: readonly (readonly [string, (settings: QconsultSettings) => string])[] = [
  ['COAI_QCONSULT_ENABLED', (s) => String(s.enabled)],
  ['COAI_QCONSULT_MODE', (s) => s.mode],
  // The rows, the prompts and the roots go out as they are stored: these settings ARE the wire format, as
  // `COAI_ROLES` is — so there is no shape to translate and no second schema to keep level.
  ['COAI_QCONSULT_ROWS', (s) => JSON.stringify(s.rows)],
  ['COAI_QCONSULT_PROMPTS', (s) => JSON.stringify(s.prompts)],
  ['COAI_QCONSULT_ROOTS', (s) => JSON.stringify(s.roots)],
  ['COAI_QCONSULT_ROW_MINUTES', (s) => String(s.rowMinutes)],
  ['COAI_QCONSULT_QUESTIONS_PER_SESSION', (s) => String(s.questionsPerSession)],
  ['COAI_QCONSULT_FREE_BATCHES', (s) => String(s.freeBatches)],
];
