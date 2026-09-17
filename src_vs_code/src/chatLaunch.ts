import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { ChatEntry } from './chatPanels';
import { threads } from './chatThread';
import { asText, show } from './chatShow';
import { ChatSession } from './chatSession';
import { CliChatSession, REAL_TIMERS } from './cliChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import { isRemote, memoryOf, modelToRun } from './chatModels';
import { presetInForce } from './chatPresets';
import { savedModels, vendorFor } from './chatConfig';
import { carriedFrom } from './chatCarry';
import { remoteChatFor } from './chatRemote';
import { TeamServer, rowBelongsTo, teamServersFrom } from './teamServers';
import { readToken } from './teamServerAuth';
import { coaiDataDir } from './dataDir';
import { ChatHome, adapterFor, chatHome, chatRuntimeRefusal, defaultExecutableFor } from './cliChatLaunch';
import { chatProcessFor } from './chatProcess';
import { resolvedExecutable } from './versionProbe';
import { Vendor } from './vendors';
import { notify } from './notify';

/**
 * Where a conversation's process comes from, and what happens when somebody changes their mind.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. A launch is built per TURN rather than once, because
 * a vendor that keeps no process needs a different command line for its second question than for its
 * first; a switch disposes the old process, resolves the new CLI and starts another, and NOTHING is
 * switched until the old conversation is provably finished.</p>
 *
 * <p>`emptyTempDir` came with it. It reads like housekeeping and is not: it is where a vendor process
 * runs, it is released with the session it was made for, and its other caller — the command that
 * opens a tab — is a caller of this module rather than the other way round.</p>
 */

/** A directory with nothing in it, and the way to take it away. See `cliChatLaunch` for both. */
export function emptyTempDir(): ChatHome {
  return chatHome(
    () => fs.mkdtempSync(path.join(os.tmpdir(), 'coai-chat-')),
    (dir) => fs.rmSync(dir, { recursive: true, force: true }),
    // Not a message box: nothing a person can do about a locked temp directory, and a modal for it
    // would be worse than the leak. The extension host's own log is where this belongs.
    (failure) => console.warn(`[coai] ${failure}`),
  );
}

/**
 * A vendor process in an empty directory of its own, and the directory, held together.
 *
 * <p>The launch is built per TURN rather than once, because a vendor that keeps no process needs a
 * different command line for its second question than for its first: `codex` resumes a thread by
 * id, and the id is not known until the first turn has been answered. A persistent vendor ignores
 * the argument entirely and gets the same argv every time.</p>
 */
export function started(
  vendor: Vendor,
  resolved: string,
  /**
   * The model this conversation is on — `modelId`, never `vendor.model`. The row's model is the
   * default a person is choosing away from, and handing it here would relabel every answer with a
   * model the CLI was never told about. (Audit finding 7 of 2026-09-09.)
   */
  model: string,
  remote?: ChatSession,
): { session: ChatSession; home: ChatHome } {
  if (remote !== undefined) {
    // A Team server needs no process and no directory: the home is a stub whose release does
    // nothing, so the rest of this file does not have to know which kind it holds.
    return { session: remote, home: { dir: '', release: () => undefined } };
  }
  const home: ChatHome = emptyTempDir();
  const adapter = adapterFor(vendor.runtime);

  return {
    session: new CliChatSession(
      chatProcessFor(vendor, home.dir, resolved, model),
      DEFAULT_BUDGETS,
      REAL_TIMERS,
      adapter,
    ),
    home,
  };
}

/**
 * The FILE this vendor's CLI is, or the sentence saying it could not be found.
 *
 * <p>`spawn` searches neither PATHEXT nor the shell's own rules, so a bare name that every terminal
 * resolves fails here with `ENOENT`. Asked once, before anything is created, so a missing CLI is a
 * message about a missing CLI rather than a conversation that dies at its first turn.</p>
 */
/**
 * The conversation for a REMOTE row, or the sentence saying why there is none.
 *
 * <p>Two things can be missing and they are different sentences: the server the row belongs to may
 * have been removed from the settings, and this side may not be signed in to it. A person can act
 * on either, and neither is "the model did not answer".</p>
 */
export async function remoteFor(vendor: Vendor): Promise<{ session: ChatSession | undefined; refusal: string }> {
  const servers = teamServersFrom(vscode.workspace.getConfiguration('coai').get('teamServers'));
  const server: TeamServer | undefined = servers.find((one) => rowBelongsTo(vendor, one));
  if (server === undefined) {
    return { session: undefined, refusal: `${vendor.id} belongs to a Team server this side no longer has.` };
  }
  const token = await readToken(coaiDataDir(), server.url);
  // The whole server, not just its URL: its id is half of this row's own name, so it is what tells
  // the row's id apart from the vendor name the server actually knows. See `serverVendorOf`.
  const session = remoteChatFor(vendor, server, token);

  return session === undefined
    ? { session: undefined, refusal: `Sign in to ${server.name} to chat with ${vendor.id}.` }
    : { session, refusal: '' };
}

export async function cliFor(vendor: Vendor): Promise<{ resolved: string; refusal: string }> {
  if (isRemote(vendor)) {
    // A Team server has no executable to find. Saying so here keeps the caller's shape: one
    // question, one refusal, before anything is created.
    return { resolved: '', refusal: '' };
  }
  const asked = vendor.executablePath.length > 0 ? vendor.executablePath : defaultExecutableFor(vendor.runtime);
  const resolved = await resolvedExecutable(asked);

  return resolved.length > 0
    ? { resolved, refusal: '' }
    : {
      resolved: '',
      refusal: `${asked} could not be found. Install the ${vendor.runtime} CLI, or put its full path in that reviewer's settings.`,
    };
}

/** The vendor row behind a model id, read fresh — the person may have edited settings since. */
/**
 * The person chose a different model in the open tab.
 *
 * <p>A conversation is a process, so this is a new process — but not a new conversation. The whole
 * transcript, questions and answers both, is handed to the next turn (`carriedTurn`), because that
 * is the only way context crosses a process boundary here. Asked for directly by the owner, and the
 * alternative shipped for about an hour: a switch that started again and said so.</p>
 *
 * <p>It waits for a turn in flight instead of killing it. The page disables its composer while the
 * model is thinking but not its picker, and disposing the session under a running turn would fail
 * that turn with "the conversation was closed" — an error about something the person did on
 * purpose. The switch simply joins the queue the turns already run in.</p>
 */
export function switchModel(entry: ChatEntry, providerId: string, asked: string): Promise<boolean> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return Promise.resolve(false);
  }
  const provider = thread.providers.find((one) => one.id === providerId);
  if (provider === undefined) {
    // A saved preset can name a row that has since been removed or switched off. Said out loud, like
    // every other refusal on this path — a button that does nothing and explains nothing is the
    // defect this whole change started from. (CodeRabbit, PR #200.)
    const refusal = `${providerId} is not a reviewer this conversation can be sent to any more.`;
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'chat-vendor-gone',
      subject: providerId,
      title: refusal,
    });
    show(entry, false, refusal);

    return Promise.resolve(false);
  }
  // An EMPTY model is the page saying the provider moved: which of the new row's models answers is
  // decided here, because this side holds the catalog. The row's own configured model, which is what
  // every other entry point into this feature falls back to.
  // A model that was NAMED and is not offered is REFUSED, never swapped for another — four reviewers
  // across three vendors on one round, and they were right: a person presses a button labelled
  // `gpt-5.6-luna`, a catalog moves under them, and the turns go somewhere else. The fallback belongs
  // to the EMPTY ask alone, which is the page saying the provider moved.
  const running = modelToRun(provider.models, asked, vendorFor(providerId)?.model ?? '');
  if (!running.ok) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'chat-model-gone',
      subject: providerId,
      title: running.refusal,
    });
    // The page has already moved its own select; put the state back so it stops claiming otherwise.
    show(entry, false, running.refusal);

    return Promise.resolve(false);
  }
  const modelId = running.model;
  if (thread.providerId === providerId && thread.modelId === modelId) {
    // ALREADY the one answering, which is not a refusal: there is nothing to switch, and everything
    // else pressing that button means still applies. It used to answer `false` like a refusal, so a
    // model preset pressed while its own model was already chosen did nothing at all.
    return Promise.resolve(true);
  }
  if (thread.running) {
    void notify({
      as: 'information',
      class: 'outcome',
      source: 'chat',
      code: 'model-switch-queued',
      subject: modelId,
      title: `Switching to ${modelId} as soon as the current answer arrives.`,
    });
  }
  // The ANSWER is what the queued switch did, not that it was queued. A caller that acts on the
  // switch — a model preset putting its own prompt in the composer — would otherwise act while
  // `switchNow` was still to find that the CLI is not installed, and offer those words to the model
  // that is still answering. (CodeRabbit, PR #200.)
  const done = thread.turns
    .then(() => switchNow(entry, providerId, modelId))
    .catch((reason: unknown) => {
      // Not swallowed: a switch that failed leaves the thread on the OLD model, and a person who
      // believes otherwise reads the next answer as the new model's. (codex, the code round.)
      const failure = `The chat could not switch to ${modelId}: ${asText(reason)}`;
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'chat',
        code: 'model-switch-failed',
        subject: modelId,
        title: failure,
        detail: asText(reason),
      });
      show(entry, false, failure);

      return false;
    });
  thread.turns = done;

  return done;
}

export async function switchNow(entry: ChatEntry, providerId: string, modelId: string): Promise<boolean> {
  const thread = threads.get(entry.id);
  if (thread === undefined || (thread.providerId === providerId && thread.modelId === modelId)) {
    return false;
  }
  // The PROVIDER is the row — it carries the runtime, the executable, the base URL, the price and,
  // for a Team server, the server and the vendor name on it. This used to look the row up by the
  // MODEL id, which is what it meant before a provider and a model were two questions.
  const vendor = vendorFor(providerId);
  const refusal = vendor === undefined
    ? `The reviewer ${providerId} is no longer configured.`
    : chatRuntimeRefusal(vendor);
  if (vendor === undefined || refusal.length > 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'chat-vendor-unusable',
      subject: providerId,
      title: refusal,
    });
    // The page has already moved its own select; put the state back so it stops claiming otherwise.
    show(entry, false, refusal);

    return false;
  }

  // The same resolution the first launch did, because the model being switched TO may be a vendor
  // whose CLI is not installed — and finding that out by spawning it would kill a conversation that
  // was working a moment ago.
  const cli = await cliFor(vendor);
  const remote = isRemote(vendor) ? await remoteFor(vendor) : { session: undefined, refusal: '' };
  const cannot = cli.refusal.length > 0 ? cli.refusal : remote.refusal;
  if (cannot.length > 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'switch-target-cannot-run',
      subject: providerId,
      title: cannot,
    });
    show(entry, false, cannot);

    return false;
  }

  thread.session.dispose();
  thread.home.release();
  const replacement = started(vendor, cli.resolved, modelId, remote.session);
  thread.session = replacement.session;
  thread.home = replacement.home;
  thread.providerId = providerId;
  thread.modelId = modelId;
  // The ROW as well as the session: a model chosen in the dropdown below the buttons is still a
  // model chosen, and the button naming it has to look it — but ONLY when it is that button's model.
  // The two selects move independently, so picking a vendor in the first and a different model in
  // the second leaves a conversation matching no preset at all, and recording the vendor's id lit a
  // button for a model that was not answering. Photographed by the operator: GPT-5.6-Terra pressed
  // while GPT-5.6-Sol was in the dropdown underneath it.
  thread.chosenId = presetInForce(savedModels(vscode.workspace.getConfiguration('coai')), providerId, modelId);
  // The memory rules move WITH the model. Left behind, they described the one just thrown away:
  // switching to a Team server kept `forgetful` false, so the server — which remembers nothing —
  // was asked turn two with no transcript behind it and the three-turn cap never applied; switching
  // back kept it true, so a CLI that HAS a memory was refused a fourth question. Spread from the one
  // function both callers use, so a field added there cannot be applied here and forgotten there.
  // (codex, the code round.)
  Object.assign(thread, memoryOf(vendor));
  // Everything said so far travels with the next question. Not sent now: nobody should be billed
  // for a conversation they moved and then never continued. Taken HERE rather than when the switch
  // was asked for, because this runs after the turn queue has drained — so an answer that was still
  // arriving when the person changed their mind is in the transcript by now. (codex, the plan round.)
  thread.carry = carriedFrom(thread.messages, thread.carryFrom);
  show(entry, false, '');
  // Said out loud: the next question costs more than the last one, because it carries everything
  // above it. A person who is not told reads the first answer as a model that mysteriously knows.
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'chat',
    code: 'model-switched',
    subject: modelId,
    title: `Now asking ${modelId}. Your next question carries this conversation across to it.`,
  });

  return true;
}
