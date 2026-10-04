import { DEFAULT_SECURITY, conditionsDiffer, type SecurityLane } from './securityLane';
import {
  holdsContent, newPromptProblem, repointWrite, restoreSteps, securityCommandWrite, type CommandWrite, type RestoreSteps,
} from './securityLaneState';
import type { SecurityTextState } from './securityPromptFiles';

/**
 * What the Security lane tab's buttons do (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 3, D) — with every
 * effect handed in, so the paths that delete a person's text are tested without an editor (`securityFlows.test.ts`).
 * `securityCommands.ts` binds the effects to VS Code.
 *
 * <p>Two rules hold for every command. A question to the person is asked OUTSIDE the provider's write queue: the render
 * awaits that queue, so a dialog inside it would freeze every write and every repaint. And the write is enqueued and
 * RE-DECIDED there, against the setting and the files as they are then: another window, or this window's own earlier
 * writes still in the queue, may have changed them since the press.</p>
 */
export interface SecurityFlowHost {
  /** The stored lane, read now. */
  readonly lane: () => SecurityLane;
  /** The provider's write queue — the one every setting write goes through. */
  readonly enqueue: (work: () => Promise<void>) => void;
  /** A lane field written as a page control writes it. */
  readonly write: (field: string, value: unknown) => Promise<void>;
  /** The folder the server reads overrides from, for telling a person where a new prompt's text goes. */
  readonly promptsDir: string;
  /** A prompt's override file, or nothing for an id that may not become a path (`promptFile`). */
  readonly fileOf: (id: string) => string | undefined;
  readonly textState: (id: string) => Promise<SecurityTextState>;
  /** Whether that file is open in an editor with unsaved changes — background tabs included. */
  readonly unsaved: (file: string) => boolean;
  /** Deletes a file; throws when it cannot, and an absent file is not an error. */
  readonly remove: (file: string) => Promise<void>;
  /** Asks for a new prompt's name, checking each keystroke with `problem`; nothing when the person cancels. */
  readonly askName: (problem: (name: string) => string) => Promise<string | undefined>;
  readonly confirm: (question: string, action: string) => Promise<boolean>;
  /** Says why a press did nothing, or less than asked. */
  readonly tell: (message: string) => void;
  /** Opens a prompt's text in an editor. */
  readonly open: (id: string) => Promise<void>;
}

export async function runSecurityCommand(command: string, id: string | undefined, host: SecurityFlowHost): Promise<void> {
  const flows: Readonly<Record<string, () => Promise<void>>> = {
    newSecurityPrompt: () => newPrompt(id, host),
    restoreSecurityPrompt: () => restore(id ?? '', host),
    removeSecurityPrompt: () => removePrompt(id, host),
  };
  await (Object.hasOwn(flows, command) ? flows[command]!() : Promise.resolve(written(command, id, host)));
}

/** A command that needs no question: decided inside the queue, against the setting as it is then. */
function written(command: string, id: string | undefined, host: SecurityFlowHost): void {
  host.enqueue(() => apply(securityCommandWrite(host.lane(), command, id), host));
}
async function apply(write: CommandWrite, host: SecurityFlowHost): Promise<void> {
  if ('refusal' in write) host.tell(`Security lane: ${write.refusal}.`);
  else await host.write(write.field, write.value);
}

/** + New custom prompt: a name first, then the prompt, then — when a pair asked for it — that pair, then its file. */
async function newPrompt(identity: string | undefined, host: SecurityFlowHost): Promise<void> {
  const name = await host.askName(typed => newPromptProblem(host.lane(), typed));
  if (name === undefined) return;
  host.enqueue(async () => {
    const problem = newPromptProblem(host.lane(), name);
    if (problem !== '') { host.tell(`Security lane: ${problem}.`); return; }
    await host.write('addPrompt', name);
    await afterAdding(name, identity, host);
  });
}
/** Only a prompt the re-read setting actually holds is paired and opened: a workspace setting can shadow the write. */
async function afterAdding(name: string, identity: string | undefined, host: SecurityFlowHost): Promise<void> {
  const lane = host.lane();
  if (!lane.prompts.some(p => p.id === name)) {
    host.tell(`Security lane: ${name} was not added — a workspace setting may override coai.securityLane.`);
    return;
  }
  if (identity !== undefined && identity !== '') await repoint(lane, identity, name, host);
  await host.open(name);
}
async function repoint(lane: SecurityLane, identity: string, name: string, host: SecurityFlowHost): Promise<void> {
  const write = repointWrite(lane, identity, name);
  if ('refusal' in write) host.tell(`Prompt ${name} created; the pair changed while you typed — pair it from its card.`);
  else await host.write(write.field, write.value);
}

/** Restore default: ask when the file holds anything, refuse over unsaved edits; then, in the queue, decide again. */
async function restore(id: string, host: SecurityFlowHost): Promise<void> {
  const refused = securityCommandWrite(host.lane(), 'restoreSecurityPrompt', id);
  if ('refusal' in refused) { host.tell(`Security lane: ${refused.refusal}.`); return; }
  const file = host.fileOf(id) ?? '';
  const seen = await host.textState(id);
  await restoreBy(restoreSteps(seen, changed(host.lane(), id), host.unsaved(file)), id, file, seen, host);
}
async function restoreBy(steps: RestoreSteps, id: string, file: string, seen: SecurityTextState, host: SecurityFlowHost): Promise<void> {
  if (steps.refusal !== '') { host.tell(`Security lane: ${steps.refusal} (${file}).`); return; }
  if (steps.confirm && !(await host.confirm(`Restore ${id} to its shipped text and conditions? ${file} — your text in it — is deleted.`, 'Restore default'))) return;
  host.enqueue(() => restoreNow(id, file, seen, host));
}
/**
 * Re-decided now, not at the press: the file may have gained text or unsaved edits since, and a write this window
 * queued earlier may have landed. The conditions are written every time (restoring is idempotent), and the file goes
 * only once they are back — a write a workspace setting shadows leaves the text where it is, and says so.
 */
async function restoreNow(id: string, file: string, seen: SecurityTextState, host: SecurityFlowHost): Promise<void> {
  const now = await host.textState(id);
  if (changedSincePress(now, seen, file, host)) {
    host.tell(`Security lane: ${file} changed after you pressed Restore default; nothing was deleted — press it again.`);
    return;
  }
  await host.write(`prompt:${id}:restore`, true);
  if (changed(host.lane(), id)) {
    host.tell(`Security lane: ${id}'s conditions could not be restored — a workspace setting may override coai.securityLane; ${file} was kept.`);
    return;
  }
  if (now !== 'none') await deleteOverride(file, host);
}
/** Unsaved edits now, or text where there was none when the person decided — either way, not what they agreed to delete. */
const changedSincePress = (now: SecurityTextState, seen: SecurityTextState, file: string, host: SecurityFlowHost): boolean =>
  host.unsaved(file) || (holdsContent(now) && !holdsContent(seen));
const changed = (lane: SecurityLane, id: string): boolean => {
  const seed = DEFAULT_SECURITY.prompts.find(p => p.id === id);
  const stored = lane.prompts.find(p => p.id === id);
  return seed !== undefined && stored !== undefined && conditionsDiffer(stored, seed);
};
async function deleteOverride(file: string, host: SecurityFlowHost): Promise<void> {
  try {
    await host.remove(file);
  } catch (error: unknown) {
    host.tell(`Security lane: the conditions are restored, but ${file} could not be deleted (${error instanceof Error ? error.message : String(error)}). Press Restore default again to retry.`);
  }
}

/** Remove custom prompt: the file stays on disk, and the person is told so and how many pairs go with it. */
async function removePrompt(id: string | undefined, host: SecurityFlowHost): Promise<void> {
  const write = securityCommandWrite(host.lane(), 'removeSecurityPrompt', id);
  if ('refusal' in write) { host.tell(`Security lane: ${write.refusal}.`); return; }
  const name = id ?? '';
  const pairs = host.lane().runs.filter(r => r.prompt === name).length;
  if (!(await host.confirm(removeQuestion(name, pairs, host.fileOf(name)), 'Remove custom prompt'))) return;
  host.enqueue(() => apply(securityCommandWrite(host.lane(), 'removeSecurityPrompt', id), host));
}

/** The removal question: what goes, and that the text — when there can be one — stays where it is. */
function removeQuestion(id: string, pairs: number, file: string | undefined): string {
  const going = pairs === 0 ? '' : ` and its ${pairs} ${pairs === 1 ? 'pair' : 'pairs'}`;
  return `Remove ${id}${going}?` + (file === undefined ? '' : ` Its text stays in ${file}.`);
}
