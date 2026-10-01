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
export const DEFAULT_SECURITY: SecurityLane = { enabled: false, threshold: 0, maxRounds: 2, prompts: SECURITY_SEED.prompts, runs: [] };
export const SECURITY_SINCE = '0.41.0';
export const securityPromptId = (id: string): boolean => /^redteam-[a-z0-9-]+$/.test(id) && id.length <= 80;
export const securitySupported = (version: string): boolean =>
  version === '' || version === '0.0.0' || compareVersions(version, SECURITY_SINCE) >= 0;
export function securityEnv(lane: SecurityLane, version: string): Record<string, string> {
  if (!securitySupported(version)) return {};
  return configured(lane) ? { COAI_SECURITY_LANE: JSON.stringify(lane) } : {};
}
const configured = (lane: SecurityLane): boolean => lane.enabled || lane.runs.length > 0 || 'invalidConfiguration' in lane;

/** Preserve unknown fields/tags on the wire so the server can refuse them instead of broadening a run. */
export function securityLaneFrom(value: unknown): SecurityLane {
  if (value == null) return DEFAULT_SECURITY;
  if (!record(value)) return { ...DEFAULT_SECURITY, invalidConfiguration: value };
  if ('invalidConfiguration' in value) return { ...DEFAULT_SECURITY, invalidConfiguration: value['invalidConfiguration'] };
  return fromRecord(value);
}
function fromRecord(value: Record<string, unknown>): SecurityLane {
  const prompts = promptsFrom(value['prompts']);
  if (!validRoot(value) || !prompts.every(prompt))
    return { ...DEFAULT_SECURITY, invalidConfiguration: value };
  return {
    ...value, enabled: value['enabled'] === true,
    threshold: number(value['threshold'], 0, 0, 100), maxRounds: number(value['maxRounds'], 2, 1, 10),
    prompts: [...prompts, ...DEFAULT_SECURITY.prompts.filter(seed => !prompts.some(p => p.id === seed.id))],
    runs: Array.isArray(value['runs']) ? value['runs'] : [],
  };
}
const promptsFrom = (value: unknown): readonly unknown[] => Array.isArray(value) ? value.map(promptDefaults) : DEFAULT_SECURITY.prompts;
function promptDefaults(value: unknown): unknown {
  if (!record(value)) return value;
  const seed = SECURITY_SEED.prompts.find(p => p.id === value['id']) ?? { triggers: [], focus: [] };
  return { triggers: seed.triggers, focus: seed.focus, ...value };
}
function validRoot(value: Record<string, unknown>): boolean {
  const validators: Record<string, (v: unknown) => boolean> = {
    enabled: v => typeof v === 'boolean', threshold: v => number(v, -1, 0, 100) !== -1,
    maxRounds: v => number(v, -1, 1, 10) !== -1, prompts: Array.isArray,
    runs: v => Array.isArray(v) && v.every(run),
  };
  return Object.entries(validators).every(([key, validate]) => !(key in value) || validate(value[key]));
}
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const strings = (v: unknown): v is readonly string[] => Array.isArray(v) && v.every(x => typeof x === 'string');
const prompt = (v: unknown): v is SecurityPrompt => record(v) && typeof v['id'] === 'string' && strings(v['triggers']) && strings(v['focus']);
const run = (v: unknown): v is SecurityRun => record(v) && typeof v['vendor'] === 'string' && typeof v['prompt'] === 'string';
const number = (v: unknown, fallback: number, min: number, max: number): number =>
  within(v, min, max) ? v : fallback;
const within = (v: unknown, min: number, max: number): v is number =>
  typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max;

/** One control edits one field; all other pairs and forward-compatible metadata survive. */
export function securityWrite(lane: SecurityLane, field: string, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  if ('invalidConfiguration' in lane) return lane;
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
  lane.prompts.length < 32 && !lane.prompts.some(p => p.id === id);
function addRun(lane: SecurityLane, value: unknown, vendors: readonly Vendor[]): SecurityLane {
  if (value !== true || lane.runs.length >= 16) return lane;
  const vendor = vendors.find(v => v.enabled && lane.prompts.some(p => !lane.runs.some(r => r.vendor === v.id && r.prompt === p.id)));
  const p = lane.prompts.find(p => !lane.runs.some(r => r.vendor === vendor?.id && r.prompt === p.id));
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
  if (value !== true || lane.runs.length >= 16) return lane;
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
  if (key === 'remove') return removePrompt(lane, id, value);
  if (!['triggers', 'focus'].includes(key) || typeof value !== 'string') return lane;
  const tags = [...new Set(value.split(',').map(s => s.trim()).filter(Boolean))];
  return { ...lane, prompts: lane.prompts.map(p => p.id === id ? { ...p, [key]: tags } : p) };
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
  const stages = r.stages ?? ['code', 'feature'];
  return { ...r, stages: value === true ? [...new Set([...stages, stage])] : stages.filter(s => s !== stage) };
}
