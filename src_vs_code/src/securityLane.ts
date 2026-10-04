import { SECURITY_SEED } from './securityLane.generated';
import { compareVersions } from './coaiInstall';
import type { Vendor } from './vendors';

export interface SecurityPrompt { readonly id: string; readonly triggers: readonly string[]; readonly focus: readonly string[]; }
export interface SecurityRun { readonly vendor: string; readonly prompt: string; readonly context?: string; readonly contextTokens?: number; readonly stages?: readonly string[]; }
export interface SecurityLane {
  readonly invalidConfiguration?: unknown;
  readonly enabled: boolean; readonly threshold: number; readonly maxRounds: number;
  readonly prompts: readonly SecurityPrompt[]; readonly runs: readonly SecurityRun[];
}
/**
 * The shipped prompts as the wire carries them: `id`, `triggers`, `focus` and nothing else. The catalogue's
 * `always` flag is a catalogue fact, never a settings member — a 0.41/0.42 server refuses a prompt entry that
 * carries one, and a saved lane would keep it in settings.json for good (todo/PLAN_the_security_tab_reads_at_a_glance.md).
 */
const SEED_PROMPTS: readonly SecurityPrompt[] = SECURITY_SEED.prompts.map(({ id, triggers, focus }) => ({ id, triggers, focus }));
export const DEFAULT_SECURITY: SecurityLane = { enabled: false, threshold: 0, maxRounds: 2, prompts: SEED_PROMPTS, runs: [] };
/**
 * A shipped prompt that is only on or off: paired with a reviewer it runs on every code change, with no conditions.
 * Read from the RAW catalogue on purpose: `SEED_PROMPTS` above is the same catalogue projected for the wire, which
 * is exactly the projection that drops this flag.
 */
export const securityAlways = (id: string): boolean =>
  SECURITY_SEED.prompts.some(p => p.id === id && 'always' in p && p.always === true);
export const SECURITY_SINCE = '0.41.0';
export const securityPromptId = (id: string): boolean => /^redteam-[a-z0-9-]+$/.test(id) && id.length <= 80;
export const securitySupported = (version: string): boolean =>
  version === '' || version === '0.0.0' || compareVersions(version, SECURITY_SINCE) >= 0;
export function securityEnv(lane: SecurityLane, version: string): Record<string, string> {
  if (!securitySupported(version)) return {};
  return configured(lane) ? { COAI_SECURITY_LANE: JSON.stringify(lane) } : {};
}
const configured = (lane: SecurityLane): boolean => lane.enabled || lane.runs.length > 0 || 'invalidConfiguration' in lane;

/**
 * The lane a stored `coai.securityLane` describes. Unknown MEMBERS travel on the wire so the server can
 * refuse them instead of broadening a run; a known member holding a value the lane cannot mean keeps the
 * whole stored value aside as `invalidConfiguration`, and the lane off.
 *
 * <p>The setting has no scope, so a cloned repository's `.vscode/settings.json` can supply it: every
 * known member is checked here, on the one road in, before any of it reaches the Settings page or a
 * write.</p>
 */
export function securityLaneFrom(value: unknown): SecurityLane {
  if (value == null) return DEFAULT_SECURITY;
  return record(value) && securityLaneProblem(value) === ''
    ? fromRecord(value)
    : { ...DEFAULT_SECURITY, invalidConfiguration: value };
}

/** Why a stored `coai.securityLane` cannot be used, naming the part that is wrong — empty when it can. */
export function securityLaneProblem(value: unknown): string {
  if (value == null) return '';
  return record(value) ? recordProblem(value) : 'it must be a JSON object';
}
function recordProblem(value: Record<string, unknown>): string {
  if ('invalidConfiguration' in value)
    return 'it holds invalidConfiguration, which is not a setting: once the object inside it is correct, make that object the whole value';
  const runs: readonly unknown[] = Array.isArray(value['runs']) ? value['runs'] : [];
  const checks = [
    () => fieldProblem(value, ROOT_FIELDS),
    () => listProblem('prompts', promptsFrom(value['prompts']), promptProblem),
    () => listProblem('runs', runs, runProblem),
  ];
  return checks.map(check => check()).find(problem => problem !== '') ?? '';
}
function fromRecord(value: Record<string, unknown>): SecurityLane {
  const prompts = promptsFrom(value['prompts']).filter(isPrompt);
  return {
    ...value, enabled: value['enabled'] === true,
    threshold: number(value['threshold'], 0, 0, 100), maxRounds: number(value['maxRounds'], 2, 1, 10),
    prompts: [...prompts, ...DEFAULT_SECURITY.prompts.filter(seed => !prompts.some(p => p.id === seed.id))],
    runs: Array.isArray(value['runs']) ? value['runs'].filter(isRun) : [],
  };
}
const promptsFrom = (value: unknown): readonly unknown[] => Array.isArray(value) ? value.map(promptDefaults) : DEFAULT_SECURITY.prompts;
function promptDefaults(value: unknown): unknown {
  if (!record(value)) return value;
  const seed = SECURITY_SEED.prompts.find(p => p.id === value['id']) ?? { triggers: [], focus: [] };
  return { triggers: seed.triggers, focus: seed.focus, ...value };
}

const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const strings = (v: unknown): v is readonly string[] => Array.isArray(v) && v.every(x => typeof x === 'string');
/** A member's check and what to say when it fails. */
type Rule = readonly [ok: (v: unknown) => boolean, problem: string];
const text: Rule[0] = v => typeof v === 'string';
const ROOT_FIELDS: Readonly<Record<string, Rule>> = {
  enabled: [v => typeof v === 'boolean', 'enabled must be true or false'],
  threshold: [v => within(v, 0, 100), 'threshold must be a whole number from 0 to 100'],
  maxRounds: [v => within(v, 1, 10), 'maxRounds must be a whole number from 1 to 10'],
  prompts: [Array.isArray, 'prompts must be a list'],
  runs: [Array.isArray, 'runs must be a list'],
};
const PROMPT_FIELDS: Readonly<Record<string, Rule>> = {
  id: [text, 'id must be text'],
  triggers: [strings, 'triggers must be a list of text'],
  focus: [strings, 'focus must be a list of text'],
};
/** The stages a pair can serve — the server's `SecurityRun.Serves` knows these two and refuses any other. */
export const SECURITY_STAGES: readonly string[] = ['code', 'feature'];
const RUN_FIELDS: Readonly<Record<string, Rule>> = {
  vendor: [text, 'vendor must be text'],
  prompt: [text, 'prompt must be text'],
  context: [v => v === 'slice' || v === 'diff', 'context must be slice or diff'],
  contextTokens: [v => within(v, 1024, 200000), 'contextTokens must be a whole number from 1024 to 200000'],
  stages: [v => strings(v) && v.every(s => SECURITY_STAGES.includes(s)), 'stages must be a list holding only code and feature'],
};
const promptProblem = (v: unknown): string => record(v) ? fieldProblem(v, PROMPT_FIELDS, Object.keys(PROMPT_FIELDS)) : 'must be an object';
const runProblem = (v: unknown): string => record(v) ? fieldProblem(v, RUN_FIELDS, ['vendor', 'prompt']) : 'must be an object';
const isPrompt = (v: unknown): v is SecurityPrompt => promptProblem(v) === '';
const isRun = (v: unknown): v is SecurityRun => runProblem(v) === '';
function fieldProblem(value: Record<string, unknown>, rules: Readonly<Record<string, Rule>>, required: readonly string[] = []): string {
  return Object.entries(rules).map(([key, rule]) => memberProblem(value, key, rule, required.includes(key))).find(p => p !== '') ?? '';
}
function memberProblem(value: Record<string, unknown>, key: string, [ok, problem]: Rule, required: boolean): string {
  if (!(key in value)) return required ? `${key} is missing` : '';
  return ok(value[key]) ? '' : problem;
}
function listProblem(name: string, items: readonly unknown[], problem: (v: unknown) => string): string {
  const index = items.findIndex(item => problem(item) !== '');
  return index < 0 ? '' : `${name}[${index}]: ${problem(items[index])}`;
}
const number = (v: unknown, fallback: number, min: number, max: number): number =>
  within(v, min, max) ? v : fallback;
const within = (v: unknown, min: number, max: number): v is number =>
  typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max;

/** The token budget a pair runs with: its own when it holds a usable one, its source mode's default otherwise. */
export const securityTokenBudget = (r: SecurityRun, context: string): number =>
  number(r.contextTokens, context === 'slice' ? 24000 : 200000, 1024, 200000);

/**
 * What the host saves for one control's write against the STORED setting — nothing when that setting is
 * malformed. Saving the panel's stand-in would overwrite what the person wrote in settings JSON, which is
 * exactly where the Security lane tab tells them to correct it. What is saved is COMPACTED: see
 * {@link compactPrompts}.
 */
export function securityLaneSave(stored: unknown, field: string, value: unknown, vendors: readonly Vendor[]): SecurityLane | undefined {
  const lane = securityLaneFrom(stored);
  return 'invalidConfiguration' in lane ? undefined : compactPrompts(securityWrite(lane, field, value, vendors));
}

/** The library's size, shipped prompts included — the server's `SecurityCatalog.MostPrompts`. */
export const SECURITY_MOST_PROMPTS: number = SECURITY_SEED.limits.mostPrompts;
/** The pairs one lane holds — the server's `SecurityCatalog.MostRuns`, from the same shared catalogue. */
export const SECURITY_MOST_RUNS: number = SECURITY_SEED.limits.mostRuns;

/**
 * The lane as it should be STORED: a shipped prompt the person never changed is left out. The merged lane — what
 * `securityEnv` sends and the tab draws — is the same either way, because {@link securityLaneFrom} merges every
 * missing shipped prompt back in. Kept is everything else: a custom prompt, an edited preset, and any entry with a
 * member this version does not know, which travels on for the server to refuse.
 *
 * <p>Why: every save used to write the whole merged list back, freezing a snapshot of every preset's conditions
 * into settings.json, so the day the catalogue changed an untouched preset would read as "edited" and never receive
 * the shipped update (todo/PLAN_the_security_tab_reads_at_a_glance.md, D2).</p>
 */
export function compactPrompts(lane: SecurityLane): SecurityLane {
  if ('invalidConfiguration' in lane) return lane;
  const once = (id: string): boolean => lane.prompts.filter(p => p.id === id).length === 1;
  return { ...lane, prompts: lane.prompts.filter(p => !(once(p.id) && untouchedShipped(p))) };
}
/**
 * A shipped prompt exactly as it ships, with nothing beside id, triggers and focus. Only an id stored ONCE is dropped:
 * the server reads the first entry of an id and complains about the rest, so dropping an untouched first entry would
 * quietly promote the second (own review, epic 2).
 */
function untouchedShipped(prompt: SecurityPrompt): boolean {
  const seed = SEED_PROMPTS.find(s => s.id === prompt.id);
  return seed !== undefined && Object.keys(prompt).every(key => PROMPT_MEMBERS.has(key)) && !conditionsDiffer(prompt, seed);
}
const PROMPT_MEMBERS: ReadonlySet<string> = new Set(['id', 'triggers', 'focus']);

/**
 * Whether a shipped prompt's conditions are no longer the shipped ones, compared AS SETS. For an "always" prompt only
 * stored triggers count: its focus is replaced on both sides, and every hand-added general was stored with `focus: []`.
 */
export function conditionsDiffer(prompt: SecurityPrompt, seed: SecurityPrompt): boolean {
  if (securityAlways(prompt.id)) return prompt.triggers.length > 0;
  return !sameSet(prompt.triggers, seed.triggers) || !sameSet(prompt.focus, seed.focus);
}
function sameSet(a: readonly string[], b: readonly string[]): boolean {
  const left = new Set(a);
  const right = new Set(b);
  return left.size === right.size && [...left].every(x => right.has(x));
}
/** One control edits one field; all other pairs and forward-compatible metadata survive. */
export function securityWrite(lane: SecurityLane, field: string, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  if ('invalidConfiguration' in lane || refusedForAlways(field)) return lane;
  const roots: Record<string, () => SecurityLane> = {
    enabled: () => enableLane(lane, value, vendors),
    threshold: () => ({ ...lane, threshold: number(value, lane.threshold, 0, 100) }),
    maxRounds: () => ({ ...lane, maxRounds: number(value, lane.maxRounds, 1, 10) }),
    addPrompt: () => addPrompt(lane, value), addRun: () => addRun(lane, value, vendors),
  };
  if (Object.hasOwn(roots, field)) return roots[field]!();
  return detailWrite(lane, field, value, vendors);
}
function enableLane(lane: SecurityLane, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  const enabled = { ...lane, enabled: value === true };
  if (!enabled.enabled || enabled.runs.length > 0) return enabled;
  return addSelected(enabled, vendors.find(v => v.enabled), lane.prompts.find(p => p.id === 'redteam-authz'));
}
function addPrompt(lane: SecurityLane, value: unknown): SecurityLane {
  if (typeof value !== 'string' || !securityPromptId(value)) return lane;
  if (!hasPromptRoom(lane, value)) return lane;
  return { ...lane, prompts: [...lane.prompts, { id: value, triggers: [], focus: [] }] };
}
const hasPromptRoom = (lane: SecurityLane, id: string): boolean =>
  lane.prompts.length < SECURITY_MOST_PROMPTS && !lane.prompts.some(p => p.id === id);
/**
 * A pair for the first enabled reviewer and the first prompt it is not paired with yet. Never an "always"
 * prompt: it costs a reviewer on every change, so it is switched on only from its own card.
 */
function addRun(lane: SecurityLane, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  if (value !== true || lane.runs.length >= SECURITY_MOST_RUNS) return lane;
  const candidates = lane.prompts.filter(p => !securityAlways(p.id));
  const vendor = vendors.find(v => v.enabled && candidates.some(p => !lane.runs.some(r => r.vendor === v.id && r.prompt === p.id)));
  const p = candidates.find(p => !lane.runs.some(r => r.vendor === vendor?.id && r.prompt === p.id));
  return addSelected(lane, vendor, p);
}
function addSelected(lane: SecurityLane, vendor: Vendor | undefined, p: SecurityPrompt | undefined): SecurityLane {
  return vendor && p ? { ...lane, runs: [...lane.runs, { vendor: vendor.id, prompt: p.id }] } : lane;
}
function detailWrite(lane: SecurityLane, field: string, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  const [kind = '', id = '', key = ''] = field.split(':');
  const edits: Record<string, () => SecurityLane> = {
    pair: () => pairWrite(lane, id, key, value, vendors),
    trigger: () => tagWrite(lane, 'trigger', id, key, value), focus: () => tagWrite(lane, 'focus', id, key, value),
    prompt: () => promptWrite(lane, id, key, value), run: () => indexedRunWrite(lane, id, key, value, vendors),
  };
  return invokeEdit(edits, kind, lane);
}
const invokeEdit = (edits: Record<string, () => SecurityLane>, kind: string, fallback: SecurityLane): SecurityLane =>
  Object.hasOwn(edits, kind) ? edits[kind]!() : fallback;
/**
 * The writes that SET a prompt's conditions — a tag box, or an "All … tags" field. An "always" prompt has none, so
 * these are refused for it by field, never by prompt: `prompt:<id>:restore` stays open, because it is how a
 * leftover from a hand-registered general is cleared.
 */
const CONDITION_FIELDS: ReadonlySet<string> = new Set(['trigger', 'focus', 'prompt:triggers', 'prompt:focus']);
function refusedForAlways(field: string): boolean {
  const [kind, id, key] = field.split(':');
  return (CONDITION_FIELDS.has(`${kind}`) || CONDITION_FIELDS.has(`${kind}:${key}`)) && securityAlways(`${id}`);
}
function indexedRunWrite(lane: SecurityLane, id: string, key: string, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  if (!/^\d+$/.test(id)) return lane;
  const index = Number(id);
  if (key === 'remove' && value === true) return { ...lane, runs: lane.runs.filter((_, i) => i !== index) };
  const runs = lane.runs.map((r, i) => i === index ? runWrite(r, key, value, vendors, lane.prompts) : r);
  return uniqueRuns(lane, runs);
}
function uniqueRuns(lane: SecurityLane, runs: readonly SecurityRun[]): SecurityLane {
  const pairs = runs.map(r => r.vendor.toLowerCase() + '/' + r.prompt);
  return new Set(pairs).size === pairs.length ? { ...lane, runs } : lane;
}
function pairWrite(lane: SecurityLane, vendor: string, prompt: string, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  if (value === false) return { ...lane, runs: lane.runs.filter(r => r.vendor !== vendor || r.prompt !== prompt) };
  if (value !== true || lane.runs.length >= SECURITY_MOST_RUNS) return lane;
  return addKnownPair(lane, vendor, prompt, vendors);
}
function addKnownPair(lane: SecurityLane, vendor: string, prompt: string, vendors: readonly Vendor[]): SecurityLane {
  if (lane.runs.some(r => r.vendor === vendor && r.prompt === prompt)) return lane;
  const row = vendors.find(v => v.id === vendor && v.enabled);
  return addSelected(lane, row, lane.prompts.find(p => p.id === prompt));
}
function tagWrite(lane: SecurityLane, kind: string, id: string, tag: string, value: unknown): SecurityLane {
  if (!SECURITY_SEED.signals.some(s => s.id === tag && (kind === 'focus' || s.trigger))) return lane;
  const key = kind === 'trigger' ? 'triggers' : 'focus';
  return { ...lane, prompts: lane.prompts.map(p => p.id === id ? { ...p,
    [key]: value === true ? [...new Set([...p[key], tag])] : p[key].filter(t => t !== tag),
  } : p) };
}
function promptWrite(lane: SecurityLane, id: string, key: string, value: unknown): SecurityLane {
  const actions: Record<string, () => SecurityLane> = {
    remove: () => removePrompt(lane, id, value), restore: () => restorePrompt(lane, id, value),
  };
  return Object.hasOwn(actions, key) ? actions[key]!() : tagListWrite(lane, id, key, value);
}
function tagListWrite(lane: SecurityLane, id: string, key: string, value: unknown): SecurityLane {
  if (!['triggers', 'focus'].includes(key) || typeof value !== 'string') return lane;
  const tags = [...new Set(value.split(',').map(s => s.trim()).filter(Boolean))];
  return { ...lane, prompts: lane.prompts.map(p => p.id === id ? { ...p, [key]: tags } : p) };
}
/**
 * A shipped prompt's conditions put back to the shipped ones. Every other member it carries stays — unknown
 * members travel on the wire for the server to refuse, and restoring must not quietly launder them away.
 */
function restorePrompt(lane: SecurityLane, id: string, value: unknown): SecurityLane {
  const seed = SEED_PROMPTS.find(p => p.id === id);
  if (value !== true || seed === undefined) return lane;
  return { ...lane, prompts: lane.prompts.map(p => p.id === id ? { ...p, triggers: seed.triggers, focus: seed.focus } : p) };
}
function removePrompt(lane: SecurityLane, id: string, value: unknown): SecurityLane {
  if (value !== true || SECURITY_SEED.prompts.some(p => p.id === id)) return lane;
  return { ...lane, prompts: lane.prompts.filter(p => p.id !== id), runs: lane.runs.filter(r => r.prompt !== id) };
}
function runWrite(r: SecurityRun, key: string, value: unknown, vendors: readonly Vendor[], prompts: readonly SecurityPrompt[]): SecurityRun {
  const edits: Record<string, () => SecurityRun> = {
    vendor: () => vendors.some(v => v.id === value) ? { ...r, vendor: String(value) } : r,
    prompt: () => prompts.some(p => p.id === value) ? { ...r, prompt: String(value) } : r,
    context: () => value === 'slice' || value === 'diff' ? { ...r, context: value } : r,
    contextTokens: () => ({ ...r, contextTokens: number(value, r.contextTokens ?? 24000, 1024, 200000) }),
    code: () => stageWrite(r, 'code', value), feature: () => stageWrite(r, 'feature', value),
  };
  return Object.hasOwn(edits, key) ? edits[key]!() : r;
}
function stageWrite(r: SecurityRun, stage: string, value: unknown): SecurityRun {
  const stages = r.stages ?? SECURITY_STAGES;
  return { ...r, stages: value === true ? [...new Set([...stages, stage])] : stages.filter(s => s !== stage) };
}
