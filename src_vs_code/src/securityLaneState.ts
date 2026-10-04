import { SECURITY_SEED } from './securityLane.generated';
import {
  DEFAULT_SECURITY, SECURITY_MOST_PROMPTS, conditionsDiffer, securityAlways, securityPromptId,
  type SecurityLane, type SecurityPrompt, type SecurityRun,
} from './securityLane';
import type { SecurityTextState } from './securityPromptFiles';

/**
 * What the Security lane tab draws each prompt card from (todo/PLAN_the_security_tab_reads_at_a_glance.md, epic 2):
 * which state a card is in, the order cards come in, what a collapsed block says, whether a new name may be used,
 * and the ONE mapping every button's write goes through. Pure, so each of them is tested without a page or a host.
 */

/** Green, orange or purple: shipped as it ships, a shipped prompt the person changed, or one the person added. */
export type PromptState = 'shipped' | 'edited' | 'custom';

const seedOf = (id: string): SecurityPrompt | undefined => DEFAULT_SECURITY.prompts.find(p => p.id === id);

/**
 * A shipped prompt is edited when its override has usable text, or its conditions are not the shipped ones as sets.
 * An empty or whitespace file is not an edit (Edit creates one); nor is the bare placeholder or an oversized file —
 * the server sends no such text, and the card names that as a problem instead. Unknown members never count.
 */
export function promptState(prompt: SecurityPrompt, text: SecurityTextState): PromptState {
  const seed = seedOf(prompt.id);
  if (seed === undefined) return 'custom';
  return text === 'written' || conditionsDiffer(prompt, seed) ? 'edited' : 'shipped';
}

/** Shipped prompts in catalogue order (general first), then the person's own in the order they are stored. */
export function promptsInOrder(lane: SecurityLane): readonly SecurityPrompt[] {
  const shipped = DEFAULT_SECURITY.prompts.flatMap(seed => lane.prompts.filter(p => p.id === seed.id));
  return [...shipped, ...lane.prompts.filter(p => seedOf(p.id) === undefined)];
}

const TRIGGERS: readonly string[] = SECURITY_SEED.signals.filter(s => s.trigger).map(s => s.id);
const labelOf = (id: string): string => SECURITY_SEED.signals.find(s => s.id === id)?.label ?? id;
const labels = (ids: readonly string[]): string => ids.map(labelOf).join(' · ');

/** The one line a collapsed conditions block shows, so collapsed never means hidden (the plan's D8). */
export function conditionsSummary(prompt: SecurityPrompt): string {
  if (securityAlways(prompt.id)) return 'Runs on every code change';
  return `Runs on: ${triggerText(prompt.triggers)}; focus: ${labels(prompt.focus) || 'none'}`;
}
function triggerText(triggers: readonly string[]): string {
  if (TRIGGERS.every(t => triggers.includes(t))) return `any security signal (${TRIGGERS.length})`;
  return labels(triggers) || 'nothing — a shipped preset with no condition does not run';
}

/** Why a name cannot become a new custom prompt, or empty when it can. Checked again inside the queued write. */
export function newPromptProblem(lane: SecurityLane, name: string): string {
  const shipped = DEFAULT_SECURITY.prompts.length;
  const checks: readonly (readonly [ok: boolean, problem: string])[] = [
    [/^redteam-/.test(name), 'a prompt name starts with redteam-'],
    [/^redteam-[a-z0-9-]+$/.test(name), 'after redteam-, use lower-case letters, digits and hyphens only'],
    [securityPromptId(name), 'a prompt name is at most 80 characters'],
    [!lane.prompts.some(p => p.id === name), `${name} is already a prompt`],
    [lane.prompts.length < SECURITY_MOST_PROMPTS, `the lane holds at most ${SECURITY_MOST_PROMPTS} prompts, ${shipped} of them shipped`],
  ];
  return checks.find(([ok]) => !ok)?.[1] ?? '';
}

/** What one button writes: a `securityWrite` field and its value, or why nothing is written. */
export type CommandWrite = { readonly field: string; readonly value: unknown } | { readonly refusal: string };

/**
 * The single mapping from a button to the lane write it makes — the old-server test, the unit tests and the host
 * all route a press through this, so a button can never write something no test has seen.
 */
export function securityCommandWrite(lane: SecurityLane, command: string, id: string | undefined): CommandWrite {
  const writes: Readonly<Record<string, () => CommandWrite>> = {
    addSecurityRun: () => ({ field: 'addRun', value: true }),
    removeSecurityRun: () => removeRunWrite(lane.runs, id),
    restoreSecurityPrompt: () => promptCommand('restore', id, shipped),
    clearSecurityConditions: () => promptCommand('restore', id, securityAlways),
    removeSecurityPrompt: () => promptCommand('remove', id, p => ownPrompt(lane, p)),
  };
  return Object.hasOwn(writes, command) ? writes[command]!() : { refusal: `${command} is not a Security lane command` };
}

/**
 * A prompt command, refused where the write would mean nothing: restore needs a shipped prompt, clear needs an "always"
 * one, remove needs the person's own prompt in this lane. Refused rather than passed through, so no caller can take a
 * no-op write as done — a restore on a custom card would go on to delete the person's own text (own review, epic 2).
 */
function promptCommand(key: string, id: string | undefined, applies: (id: string) => boolean): CommandWrite {
  if (id === undefined || !securityPromptId(id)) return { refusal: 'no prompt was named' };
  return applies(id) ? { field: `prompt:${id}:${key}`, value: true } : { refusal: `${id} has nothing to ${key}` };
}
const shipped = (id: string): boolean => seedOf(id) !== undefined;
const ownPrompt = (lane: SecurityLane, id: string): boolean => !shipped(id) && lane.prompts.some(p => p.id === id);

/**
 * Remove pair names its pair by identity as well as by row: `[index, vendor, prompt]`. The row is removed only while
 * it still holds that pair — an index shifts when another window edits the setting or a pair above is removed, and
 * removing the wrong pair would be a silent wrong write (the plan's D5).
 */
function removeRunWrite(runs: readonly SecurityRun[], id: string | undefined): CommandWrite {
  const identity = runIdentity(id);
  if (identity === undefined) return { refusal: 'the pair could not be identified' };
  const [index, vendor, prompt] = identity;
  return holds(runs[index], vendor, prompt) ? { field: `run:${index}:remove`, value: true } : { refusal: 'that pair moved; nothing was removed' };
}
function runIdentity(id: string | undefined): readonly [number, string, string] | undefined {
  const parsed = parsedJson(id);
  return Array.isArray(parsed) && parsed.length === 3 && isIdentity(parsed) ? [parsed[0], parsed[1], parsed[2]] : undefined;
}
const isIdentity = (v: readonly unknown[]): v is readonly [number, string, string] =>
  Number.isInteger(v[0]) && (v[0] as number) >= 0 && typeof v[1] === 'string' && typeof v[2] === 'string';
function parsedJson(text: string | undefined): unknown {
  try { return text === undefined ? undefined : JSON.parse(text); } catch { return undefined; }
}
/** Vendors compare without case, as the server and `uniqueRuns` compare them; prompt ids are exact. */
const holds = (run: SecurityRun | undefined, vendor: string, prompt: string): boolean =>
  run !== undefined && run.vendor.toLowerCase() === vendor.toLowerCase() && run.prompt === prompt;
