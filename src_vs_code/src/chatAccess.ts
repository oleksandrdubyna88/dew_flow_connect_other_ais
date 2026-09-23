import { ChatEntry } from './chatPanels';
import { Thread, threads } from './chatThread';
import { asText, onATeamServer, show } from './chatShow';
import { cliFor, install } from './chatLaunch';
import { vendorFor } from './chatConfig';
import { ChatAccess } from './chatAdapter';
import { TURN_AGENT_ON, accessNotice, agentQuestion, agentRefusal } from './chatAccessRules';
import { notify, notifyAndAsk } from './notify';

/**
 * The person ticked or unticked agent mode in an open tab (issue #289).
 *
 * <p>The same move as a model switch, because it is one: a vendor's flags are fixed when it starts, so
 * a new access is a new session with the transcript carried (`install`). It joins the turn queue, as a
 * switch does — the page disables the box while a turn runs, and the queue is what makes that hold
 * even for a message that crossed a turn starting.</p>
 *
 * <p>Turning it ON asks first, in a modal the person has to answer: this hands a model the computer
 * (local, the plan round). Turning it off asks nothing.</p>
 */
export async function switchAccess(entry: ChatEntry, agent: boolean): Promise<boolean> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return false;
  }
  const wanted: ChatAccess = agent ? 'agent' : 'text';

  return thread.access === wanted ? true : changed(entry, thread, wanted);
}

async function changed(entry: ChatEntry, thread: Thread, wanted: ChatAccess): Promise<boolean> {
  const refusal = wanted === 'agent' ? agentRefusal(onATeamServer(thread), thread.workspace) : '';
  if (refusal.length > 0) {
    return refused(entry, refusal);
  }
  if (!(await allowed(thread, wanted))) {
    // The page ticked its own box; drawing the state again puts it back.
    show(entry, false, '');

    return false;
  }
  const done = thread.turns
    .then(() => applied(entry, thread, wanted))
    .catch((reason: unknown) => refused(entry, `The chat could not change agent mode: ${asText(reason)}`));
  thread.turns = done;

  return done;
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
    title: agentQuestion(thread.modelId, thread.workspace),
    action: TURN_AGENT_ON,
  });

  return answer === TURN_AGENT_ON;
}

/**
 * The new access, in force. A tab restored from a reload has no process yet: its access is simply
 * recorded, and the first question opens the session in it (`reopened`).
 */
async function applied(entry: ChatEntry, thread: Thread, access: ChatAccess): Promise<boolean> {
  if (!thread.reopen) {
    const vendor = vendorFor(thread.providerId);
    if (vendor === undefined) {
      return refused(entry, `The reviewer ${thread.providerId} is no longer configured.`);
    }
    const cli = await cliFor(vendor);
    if (cli.refusal.length > 0) {
      return refused(entry, cli.refusal);
    }
    install(thread, vendor, cli.resolved, thread.modelId, undefined, access);
  }
  thread.access = access;
  show(entry, false, '');
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'chat',
    code: 'chat-access-changed',
    subject: thread.modelId,
    title: accessNotice(access, thread.modelId),
  });

  return true;
}

function refused(entry: ChatEntry, refusal: string): boolean {
  void notify({
    as: 'warning',
    class: 'refusal',
    source: 'chat',
    code: 'chat-access-refused',
    subject: 'agent mode',
    title: refusal,
  });
  show(entry, false, refusal);

  return false;
}
