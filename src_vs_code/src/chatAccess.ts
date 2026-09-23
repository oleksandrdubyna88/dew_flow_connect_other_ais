import { ChatEntry } from './chatPanels';
import { Thread, threads } from './chatThread';
import { asText, onATeamServer, show } from './chatShow';
import { cliFor, install } from './chatLaunch';
import { vendorFor } from './chatConfig';
import { ChatAccess } from './chatAdapter';
import { isFolder } from './cliChatLaunch';
import { TURN_AGENT_ON, accessNotice, agentFolderGone, agentQuestion, agentRefusal } from './chatAccessRules';
import { notify, notifyAndAsk } from './notify';

/**
 * What the person asked for LAST, per conversation.
 *
 * <p>A change waits in the turn queue, and a person can change their mind while it waits: tick, confirm,
 * untick. Compared against `thread.access` alone, the untick was a no-op (nothing had changed yet) and
 * the queued tick then turned agent mode on anyway — the last intent lost. (Our own code review.)</p>
 */
const wantedBy = new WeakMap<Thread, ChatAccess>();

/**
 * The person ticked or unticked agent mode in an open tab (issue #289).
 *
 * <p>The same move as a model switch, because it is one: a vendor's flags are fixed when it starts, so
 * a new access is a new session with the transcript carried (`install`). It joins the turn queue, as a
 * switch does — the page disables the box while a turn runs, and the queue is what makes that hold
 * even for a message that crossed a turn starting.</p>
 *
 * <p>Turning it ON asks first, in a modal the person has to answer: this hands a model the computer
 * (local, the plan round). Turning it off asks nothing. The page never ticks its own box — it draws
 * what the host pushes — so a cancelled question leaves nothing to put back.</p>
 */
export async function switchAccess(entry: ChatEntry, agent: boolean): Promise<boolean> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return false;
  }
  const wanted: ChatAccess = agent ? 'agent' : 'text';
  wantedBy.set(thread, wanted);

  return thread.access === wanted ? true : changed(entry, thread, wanted);
}

async function changed(entry: ChatEntry, thread: Thread, wanted: ChatAccess): Promise<boolean> {
  const refusal = refusalFor(thread, wanted);
  if (refusal.length > 0) {
    return refused(entry, thread, refusal);
  }
  if (!(await allowed(thread, wanted))) {
    return false;
  }
  const done = thread.turns
    .then(() => applied(entry, thread, wanted))
    .catch((reason: unknown) => refused(entry, thread, `The chat could not change agent mode: ${asText(reason)}`));
  thread.turns = done;

  return done;
}

/**
 * Why this conversation cannot be put in agent mode now, or empty. Asked when the box is ticked AND
 * again when the queue reaches the change: a model switch queued ahead of it may have moved the
 * conversation to a Team server, and the folder may have gone. (Our own code review, and codex on the
 * code round: the box must refuse a folder the launch would refuse.)
 */
function refusalFor(thread: Thread, wanted: ChatAccess): string {
  if (wanted !== 'agent') {
    return '';
  }
  const refusal = agentRefusal(onATeamServer(thread), thread.workspace);

  return refusal.length > 0 ? refusal : agentFolderGone(thread.workspace, isFolder(thread.workspace));
}

/** Turning agent mode OFF needs nobody's say-so; turning it on asks first. */
function allowed(thread: Thread, wanted: ChatAccess): Promise<boolean> {
  return wanted === 'agent' ? confirmed(thread) : Promise.resolve(true);
}

async function confirmed(thread: Thread): Promise<boolean> {
  const answer = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'chat',
    code: 'turn-agent-mode-on',
    modal: true,
    title: agentQuestion(nameOf(thread), thread.workspace),
    action: TURN_AGENT_ON,
  });

  return answer === TURN_AGENT_ON;
}

/**
 * Who the question and the notice name. A row left on its CLI's default model has an empty model id,
 * and a consent question that begins " will be able to…" names nobody. (Our own code review.)
 */
function nameOf(thread: Thread): string {
  return thread.modelId.length > 0 ? thread.modelId : thread.providerId;
}

/** The change, once the queue reaches it — if it is still wanted and still allowed. */
async function applied(entry: ChatEntry, thread: Thread, access: ChatAccess): Promise<boolean> {
  if (!stillWanted(entry, thread, access)) {
    return false;
  }
  const refusal = refusalFor(thread, access);

  return refusal.length > 0 ? refused(entry, thread, refusal) : inForce(entry, thread, access);
}

/**
 * Whether the queued change still means anything: the tab was not closed while it waited (a new process
 * for a closed tab would outlive it — gemini and local, the code round), the person has not asked for
 * the opposite since, and it is not already in force.
 */
function stillWanted(entry: ChatEntry, thread: Thread, access: ChatAccess): boolean {
  return threads.get(entry.id) === thread && wantedBy.get(thread) === access && thread.access !== access;
}

/**
 * The new access, in force. A tab restored from a reload has no process yet: its access is simply
 * recorded, and the first question opens the session in it (`reopened`).
 */
async function inForce(entry: ChatEntry, thread: Thread, access: ChatAccess): Promise<boolean> {
  if (!thread.reopen && !(await relaunched(entry, thread, access))) {
    return false;
  }
  thread.access = access;
  show(entry, thread.running, '');
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'chat',
    code: 'chat-access-changed',
    subject: nameOf(thread),
    title: accessNotice(access, nameOf(thread)),
  });

  return true;
}

async function relaunched(entry: ChatEntry, thread: Thread, access: ChatAccess): Promise<boolean> {
  const vendor = vendorFor(thread.providerId);
  if (vendor === undefined) {
    return refused(entry, thread, `The reviewer ${thread.providerId} is no longer configured.`);
  }
  const cli = await cliFor(vendor);
  if (cli.refusal.length > 0) {
    return refused(entry, thread, cli.refusal);
  }
  install(thread, vendor, cli.resolved, thread.modelId, undefined, access);

  return true;
}

/** Said, and drawn — with the thread's OWN running state, never a hard-coded false (chatTurn.ts). */
function refused(entry: ChatEntry, thread: Thread, refusal: string): boolean {
  void notify({
    as: 'warning',
    class: 'refusal',
    source: 'chat',
    code: 'chat-access-refused',
    subject: 'agent mode',
    title: refusal,
  });
  show(entry, thread.running, refusal);

  return false;
}
