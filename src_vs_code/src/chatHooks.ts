import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { Thread, threads } from './chatThread';
import { pulse } from './chatHost';
import { chatLanguage, show } from './chatShow';
import { ask, oneReask, oneRetry } from './chatTurn';
import { switchModel } from './chatLaunch';
import { freshStart } from './chatArchive';
import { resolveAndPin } from './chatSessionJoin';
import { ModelPreset } from './chatPresets';
import { savedModels, savedPrompts, taskOf } from './chatConfig';
import { chatInstruction, openingTurn, reinstructed, reinstructedHead, stillOurs } from './chatPrompt';
import { chatSettingsFrom } from './chatSettings';
import { carriedFrom, carryMark } from './chatCarry';
import { imageFileName, imageRefusal, pastedImage } from './chatImage';
import { acknowledgement, answerToCopy, blockToCopy } from './answerCopy';
import { textCopier, type CopyDecision, type CopyReport } from './copyText';
import { coaiDataDir } from './dataDir';
import { insideReally, promptsFrom } from './claudeSessions';
import { createChatPanel, pushChatCopied, setChatDraft } from './chatPanel';
import { notify } from './notify';
import { withdraw } from './chatQueue';
import { pushChatDraft } from './chatPanel';

/**
 * Everything a chat page can ask of the host, and the three writers that put words in its composer.
 *
 * <p>Extracted from `chatCommand.ts` unchanged, and it is the biggest of the modules that came out
 * of it. ONE object built in ONE place: both paths that create a panel use it, and a second copy of
 * these callbacks would be as many chances for a restored tab to stop stopping turns, or to leak a
 * session on close, the day one of them changes.</p>
 *
 * <p>The composer writers came with it because nothing else calls them. A prompt chooses the TASK
 * and a model chooses the ROLE; they are the same gesture with different halves, so they write
 * through one function and cannot drift into behaving differently — which is what they had done.</p>
 *
 * <p><b>Over the 400 lines the style rule calls typical, and it says why here because the rule asks
 * it to.</b> `conversationHooks` is most of them and is one object literal: every callback a page can
 * invoke, built together so the two paths that create a panel cannot be handed different ones.
 * Splitting it by callback would produce files that exist only to be re-assembled at the call site,
 * and re-assembling it in two places is the defect the single object prevents. Under the 800
 * ceiling; the honest next move is to give whole callbacks pure halves a test can reach, which is a
 * behaviour question and belongs to a plan of its own rather than to a move.</p>
 */

/**
 * How many times each conversation has asked what was written in its session.
 *
 * <p>The number is the only thing that tells a late answer from a current one, and it lives here
 * rather than on the thread because it is about presses rather than about the conversation.</p>
 */
const asking = new WeakMap<object, number>();

/**
 * Put this conversation's instruction into its composer, around the passage it is about.
 *
 * <p>The two rows of buttons are the same gesture with different halves — a prompt chooses the TASK,
 * a model chooses the ROLE — so they write through one function and cannot drift into behaving
 * differently, which is what they had done: *"переключение промта все ок, а модели подвисает"*.</p>
 *
 * @param draft what the page says is in the box, or `undefined` when the caller did not ask for it —
 *   a prompt button IS the instruction "ask this instead", so it replaces without asking.
 * @returns whether it was written. A box somebody has typed their own question into is left alone,
 *   which two vendors refused to ship without on the plan round.
 */
function writeInstruction(
  entry: ChatEntry,
  thread: Thread,
  draft: string | undefined,
  was: string,
  config: vscode.WorkspaceConfiguration,
): boolean {
  const now = instructionOf(thread, config);
  // FIRST, AND ALMOST ALWAYS: swap the instruction where it stands. The box is the only place that
  // knows what is really in it, so keeping everything after the instruction byte for byte is both
  // the safest thing to do with somebody's words and the only version of this that cannot drift out
  // of step with the conversation's memory of the passage.
  // AND THE SAME SWAP when the instruction in the box is not the one this side wrote, because
  // somebody typed over it. That is ordinary — edit the role, press a preset, and the preset has to
  // win — and it used to fall through to the rebuild below, which refused and left the box alone
  // with a message about it. Cut at the service lines, everything below them kept byte for byte.
  const swapped = draft === undefined
    ? undefined
    : reinstructed(draft, was, now) ?? reinstructedHead(draft, now, chatLanguage());
  if (swapped !== undefined) {
    thread.ourDraft = swapped;
    setChatDraft(entry, swapped);

    return true;
  }
  // The instruction is not at the front any more — somebody wrote over it, or this is a box we have
  // never written to. Then the old test applies: a whole turn goes in when the box is empty or still
  // holds what this side built, and a question somebody typed is left alone.
  if (draft !== undefined && draft !== thread.ourDraft && !stillOurs(draft, thread.passage)) {
    return false;
  }
  const next = openingTurn(now, chatLanguage(), thread.passage);
  thread.ourDraft = next;
  setChatDraft(entry, next);

  return true;
}

/**
 * The person chose a model — from a preset button, or from the picker under them.
 *
 * <p>ONE implementation for both, because they are one decision. The picker used to switch the
 * session and leave the composer instructed by the model before it, so the next turn went to B
 * carrying A's role while the marking said A was answering. (codex, the second code round.)</p>
 *
 * <p>Everything about the SCREEN happens now; the process follows. A switch that is refused leaves
 * the old model answering, so the button goes back to it — and nothing else does: the box may have
 * been typed into while the switch resolved, and the refusal is said out loud instead.</p>
 */
function chooseModel(
  entry: ChatEntry,
  thread: Thread,
  preset: ModelPreset,
  draft: string | undefined,
  config: vscode.WorkspaceConfiguration,
): void {
  const wasChosen = thread.chosenId;
  const wasRole = thread.role;
  const was = instructionOf(thread, config);
  thread.presses += 1;
  const press = thread.presses;
  thread.chosenId = preset.id;
  thread.role = preset.startingPrompt ?? '';
  if (!writeInstruction(entry, thread, draft, was, config)) {
    // The box holds something the person wrote, so it keeps the instruction it has — and the
    // conversation keeps the role that MATCHES it, or the two would disagree about the same words
    // and the marking behind the box would stop finding them.
    thread.role = wasRole;
    if ((preset.startingPrompt ?? '').length > 0) {
      void notify({
        as: 'information',
        class: 'outcome',
        source: 'chat',
        code: 'preset-prompt-left-alone',
        subject: preset.id,
        title: `${preset.name} opens with its own prompt, and the box holds something you wrote — so it was left alone.`,
      });
    }
  }
  show(entry, thread.running, '');
  void switchModel(entry, preset.id, preset.model).then((switched) => {
    // ONLY IF THIS PRESS IS STILL THE LAST ONE — by count, not by id: pressing A, then B, then A
    // again would otherwise let the first A's refusal undo the second. (codex, the code round.)
    if (switched || thread.presses !== press) {
      return;
    }
    thread.chosenId = wasChosen;
    show(entry, thread.running, '');
  });
}

/** What this conversation is instructing with: the model's role, then the prompt's task. */
function instructionOf(thread: Thread, config: vscode.WorkspaceConfiguration): string {
  return chatInstruction(
    thread.role,
    taskOf(config, thread.promptId, chatSettingsFrom((key) => config.get(key)).prompt),
  );
}

/**
 * Everything a chat page can ask of the host, for a conversation that is opened OR restored.
 *
 * <p>One object built in one place. Both paths create a panel, and a second copy of these six
 * callbacks would be six chances for a restored tab to stop stopping turns, or to leak a session on
 * close, the day one of them changes.</p>
 */
export function conversationHooks(panels: ChatPanels): Parameters<typeof createChatPanel>[2] {
  // ONE sentence-teller for this whole object. Three reviewers found the acknowledgement path's
  // empty catch, and `coding-style.md` is explicit that an error is never silently swallowed; a
  // second inline `showWarningMessage` beside the first is how one of them ends up without the
  // other's wording.
  //
  // It goes through the funnel rather than to `vscode.window` directly, because this is the one
  // sentence in the object that appears while the person is looking somewhere ELSE by definition:
  // what they were waiting for is a tick on a control that simply never came. A toast nobody was
  // facing is the case the notifications ledger exists for.
  const warn = (message: string): void => {
    void notify({
      as: 'warning',
      class: 'failure',
      source: 'chatPage',
      code: 'copy-not-acknowledged-on-the-page',
      title: message,
    });
  };
  // ONE copier for both controls on an answer, so the whole and the part cannot disagree about what
  // happens when the clipboard refuses — and so two quick presses land in the order they were made.
  // Everything it knows about `vscode` is these two functions; the decisions are in `answerCopy.ts`,
  // where a test can reach them.
  const answerCopier = textCopier({
    writeText: (text) => Promise.resolve(vscode.env.clipboard.writeText(text)),
    say: (message, forMs) => vscode.window.setStatusBarMessage(message, forMs),
  });

  return {
      onSend: (id, text) => {
        const found = panels.entryOf(id);
        if (found !== undefined) {
          void ask(found, text);
        }
      },
      // TAKE ONE BACK. The row leaves the page and the words go into the composer; what actually
      // stops the question being asked is `oneTurn` finding it gone, because the callback that
      // would run it cannot be un-chained. An id naming nothing is an ordinary race — a press that
      // landed a tick after its turn began — and changes nothing rather than being an error, and
      // redraws nothing either, since repainting would wipe the sentence explaining the last turn.
      onWithdraw: (id, questionId) => {
        const found = panels.entryOf(id);
        const mine = found === undefined ? undefined : threads.get(found.id);
        if (found === undefined || mine === undefined) {
          return;
        }
        const taken = withdraw(mine.waiting, questionId);
        if (taken.returned.length === 0) {
          return;
        }
        mine.waiting = taken.waiting;
        pushChatDraft(found, taken.returned);
        show(found, mine.running, '');
      },
      onPick: (id, providerId, modelId, draft) => {
        const config = vscode.workspace.getConfiguration('coai');
        const found = panels.entryOf(id);
        const mine = threads.get(id);
        const preset = savedModels(config).find((one) => one.id === providerId);
        if (found === undefined) {
          return;
        }
        // A PRESET is what the first select names, so choosing one there is choosing a model in
        // every sense the buttons above mean it. The bare switch is what is left for a pick this
        // side cannot match to a preset — a page one write behind, or the model half alone.
        if (mine !== undefined && preset !== undefined && modelId.length === 0) {
          chooseModel(found, mine, preset, draft, config);

          return;
        }
        void switchModel(found, providerId, modelId);
      },
      onUsePrompt: (id, presetId, draft) => {
        const found = panels.entryOf(id);
        const preset = savedPrompts(vscode.workspace.getConfiguration('coai'))
          .find((one) => one.id === presetId);
        const thread = threads.get(id);
        if (found === undefined || thread === undefined) {
          return;
        }
        // Said out loud, like the model button beside it: a press can outlive the row it names.
        if (preset === undefined) {
          const gone = 'That saved prompt is no longer in your presets — it was removed or renamed.';
          void notify({
            as: 'warning',
            class: 'refusal',
            source: 'chat',
            code: 'prompt-preset-gone',
            title: gone,
          });
          show(found, thread.running, gone);

          return;
        }
        // The INSTRUCTION changes and the captured passage stays. The composer holds both — the
        // prompt, the language line, the fence and the text that was selected — so replacing the
        // whole box threw away the passage the conversation is about, which is what a person watched
        // happen every time they pressed a second button. Rebuilt from the same three parts instead.
        const config = vscode.workspace.getConfiguration('coai');
        const was = instructionOf(thread, config);
        const wasPrompt = thread.promptId;
        // The TASK changes; the role the model carries is untouched, because pressing "explain" does
        // not stop it being an architect. The two buttons run the same three lines with different
        // halves, which is the only way they can go on behaving the same.
        thread.promptId = presetId;
        if (!writeInstruction(found, thread, draft, was, config)) {
          // The box was not rewritten, so the conversation keeps the task the words in it were
          // written with — otherwise the marking, and the pair recorded with the question when it is
          // sent, would both name a prompt nothing on screen used. (gemini, the second code round.)
          thread.promptId = wasPrompt;
          void notify({
            as: 'information',
            class: 'outcome',
            source: 'chat',
            code: 'preset-instruction-left-alone',
            subject: preset.id,
            title: `${preset.name} replaces the instruction, and the box holds something you wrote — so it was left alone.`,
          });
        }
        show(found, thread.running, '');
      },
      onUseModel: (id, presetId, draft) => {
        const config = vscode.workspace.getConfiguration('coai');
        const found = panels.entryOf(id);
        const preset = savedModels(config).find((one) => one.id === presetId);
        const mine = threads.get(id);
        if (found === undefined || mine === undefined) {
          return;
        }
        // A BUTTON THAT NAMES NOTHING SAYS SO. The rows are redrawn on every state push, so a preset
        // deleted in the other tab normally takes its button with it — but a press can outlive the
        // row it names, and a button that does nothing and explains nothing is the defect this whole
        // change started from. (CodeRabbit, PR #200.)
        if (preset === undefined) {
          const gone = 'That saved model is no longer in your presets — it was removed or renamed.';
          void notify({
            as: 'warning',
            class: 'refusal',
            source: 'chat',
            code: 'model-preset-gone',
            title: gone,
          });
          show(found, mine.running, gone);

          return;
        }
        chooseModel(found, mine, preset, draft, config);
      },
      onMarkGone: (id, which) => {
        const mine = threads.get(id);
        const found = panels.entryOf(id);
        if (mine === undefined || found === undefined) {
          return;
        }
        // The button stops looking pressed. It was lit because its words were the instruction in
        // force; they are not, and a button claiming something nothing is using lies about what the
        // next question will carry. Two halves, two buttons: a PROMPT preset owns the task, and a
        // MODEL preset owns the role it put there — edit either away and that one goes dark.
        const lit = which === 'task' ? mine.promptId : mine.chosenId;
        if (lit.length === 0) {
          return;
        }
        if (which === 'task') {
          mine.promptId = '';
        } else {
          mine.chosenId = '';
        }
        show(found, mine.running, '');
      },
      onCarryFrom: (id, at) => {
        const mine = threads.get(id);
        const found = panels.entryOf(id);
        if (mine === undefined || found === undefined) {
          return;
        }
        // CLAMPED HERE TOO, and FORWARD ONLY. The page's number is checked for being a position at
        // all; this one is checked against the conversation it is a position IN, which the page
        // cannot do — the transcript it rendered may be a turn behind the one the host holds.
        //
        // And never backwards. The button is offered on the last answer alone, so a real press can
        // never name a position above the mark already set; a lower one is a stale page or a forged
        // message, and taking it would put back a conversation somebody deliberately excluded — on
        // a Team server, at a price. (codex, the code round, as a security finding.)
        mine.carryFrom = Math.max(
          carryMark(at, mine.messages.length),
          carryMark(mine.carryFrom, mine.messages.length),
        );
        // AND THE CARRY ALREADY STAGED. A switch, a restore and a lost context all fill `carry`
        // ahead of the next question — so pressing the button after switching and before asking
        // moved the rule and sent the whole conversation anyway, which is the one case this feature
        // was built for. (gemini, the code round.)
        if (mine.carry.length > 0) {
          mine.carry = carriedFrom(mine.messages, mine.carryFrom);
        }
        // `show` writes the tab down and pushes it back, in that order, so the rule the person ends
        // up looking at is the rule that will survive a reload rather than one the store never
        // heard about. (codex, the plan round.)
        show(found, mine.running, '');
      },
      onShowAsked: (id) => {
        const found = panels.entryOf(id);
        const mine = threads.get(id);
        if (found === undefined) {
          // The tab is gone; there is nobody to answer.
          return;
        }
        if (mine === undefined) {
          // SAID, not left silent. The page paints "Reading the session…" the moment the region
          // opens, and a hook that returns without posting leaves that there for as long as the tab
          // is open. (gemini, the code round.)
          found.panel.post({
            type: 'asked',
            at: Number.MAX_SAFE_INTEGER,
            asked: [],
            refusal: 'This conversation is no longer held by the extension, so its session cannot be found.',
          });

          return;
        }
        // WHICH PRESS THIS IS. Opening, folding and opening again starts a second read while the
        // first is still going, and the slower one landing last would replace what the person just
        // asked for with what they asked for before. The page keeps the highest it has seen and
        // ignores anything older. (codex and gemini, the code round, on both halves of it.)
        const at = (asking.get(id) ?? 0) + 1;
        asking.set(id, at);
        void (async () => {
          // THE FILE FIRST, when this tab has one. It was resolved as the tab opened, while its name
          // still matched — and a name is what goes stale here, never a path.
          //
          // A RESTORED tab has none: the reload lost it, so the first press resolves by name and
          // KEEPS what it found. Without that it resolved afresh every time, by a name that is
          // exactly as stale on the second press as on the first — so a conversation Claude renamed
          // after the reload would never be found again. (CodeRabbit, PR #207.)
          const answer = mine.sessionFile.length > 0
            ? await promptsFrom(mine.sessionFile)
            : await resolveAndPin(found, mine);
          // A REASON, never a blank region. Four situations look identical from an empty box — no
          // session file, no folder, a namesake it refuses to pick between, and a conversation the
          // person has not spoken in yet — and the box is the only place they are looking.
          found.panel.post(answer.kind === 'said'
            ? { type: 'asked', at, asked: answer.said, refusal: '' }
            : { type: 'asked', at, asked: [], refusal: answer.refusal });
        })();
      },
      onStop: (id, turn) => {
        const found = panels.entryOf(id);
        const thread = threads.get(id);
        if (found === undefined || thread?.running !== true) {
          // Nothing is running, or the tab is already gone. A stop is a message about a turn, and
          // there is no turn — killing the process for it would cost the conversation for a keypress
          // that arrived too late to mean anything.
          return;
        }
        if (turn !== thread.turn) {
          // The page named a turn that is no longer the running one, which is what a late or a
          // repeated press looks like from here. Refused rather than applied to whatever happens to
          // be in flight now — that turn is a different question the person has not asked to stop.
          // There is no wildcard to fall back to: the bridge already refuses a stop that names no
          // turn, so `turn` here is always a real number a page chose. (codex and gemini, the code
          // round, on both halves of this rule.)
          return;
        }
        thread.session.stop();
      },
      onClosed: (id) => {
        // The registry disposes the session the ENTRY was created with, which after a model switch
        // is no longer the one that is running. So the thread's own current session is ended here
        // too — disposal is idempotent, and the alternative is an authenticated child nobody owns.
        const thread = threads.get(id);
        panels.closeById(id);
        // Forgotten, not merely disposed. A remote turn can be answered a poll after the tab went
        // away, and `show` would then post state into a webview VS Code has torn down — a throw out
        // of a callback nobody catches. Every reader of a thread starts by looking it up, so
        // removing it turns all of them into no-ops at once. (codex and gemini, the code round.)
        threads.delete(id);
        pulse?.();
        thread?.session.dispose();
        thread?.home.release();
      },
      onAttach: (id, dataUrl) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          void attachPicture(entry, thread, dataUrl);
        }
      },
      onUnattach: (id) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          forgetPicture(thread);
          show(entry, false, '');
        }
      },
      onReask: (id) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          void oneReask(entry, thread);
        }
      },
      onRetry: (id, at) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          void oneRetry(entry, thread, at);
        }
      },
      onRestart: (id) => {
        // *New chat*: the capped notice's button since the cap existed, and D2's header button. One
        // implementation for both, because they are the same gesture with the same words.
        const entry = panels.entryOf(id);
        if (entry !== undefined) {
          void freshStart(entry);
        }
      },
      onUseLocal: () => undefined,
      onPageError: (_id, message) => {
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'chatPage',
          code: 'chat-page-reported-an-error',
          title: `The chat page reported: ${message}`,
          detail: message,
        });
      },
      onOpenFile: (id, requested, line) => {
        void openWorkspaceFile(id, requested, line, (message) => {
          void notify({
            as: 'warning',
            class: 'failure',
            source: 'chatPage',
            code: 'file-from-an-answer-not-opened',
            subject: requested,
            title: message,
          });
        });
      },
      onCopyAnswer: (id, index, sig) => {
        // The SOURCE, out of the thread the page was rendered from. A person copying an answer wants
        // the markdown they can paste into a plan or an issue, and that is the one thing selecting
        // the page cannot give them - a selection gives what the page shows.
        // RESOLVED at press time, not when its turn in the queue comes. A block control carries a
        // signature and is refused if the answer changed under it; this one carries nothing, so a
        // press queued behind a slow write could otherwise copy whatever had replaced the message at
        // that index by the time it ran. (codex, the code round.)
        const said = threads.get(id)?.messages[index];
        const decision: CopyDecision = said === undefined || said.role !== 'model'
          ? { kind: 'refused', said: 'That answer is not on this page any more.' }
          : answerToCopy(said.text);
        tellThePage(warn, panels, id, index, undefined, sig, answerCopier.copy(() => decision));
      },
      onCopyBlock: (id, index, block, sig) => {
        // The SAME markdown the page was drawn from, walked by the SAME function that numbered the
        // control. Nothing the page sent becomes text: it named a position and echoed a signature,
        // and both are checked here against what this host holds.
        tellThePage(warn, panels, id, index, block, sig, answerCopier.copy(() => {
          const said = threads.get(id)?.messages[index];

          return said === undefined || said.role !== 'model'
            ? { kind: 'refused', said: 'That answer is not on this page any more.' }
            : blockToCopy(said.text, block, sig);
        }));
      },
  };
}

/**
 * Tick the control that was pressed — but only once the clipboard actually took the text.
 *
 * <p>Both copy hooks discarded their report with a bare `void`, which is why neither control could
 * ever move: the one fact worth showing was thrown away at the point it became known. It is a
 * function rather than two copies of four lines because the two hooks differ in one argument, and
 * two sites that each need a catch is how one of them ends up without one.</p>
 *
 * <p><b>Nothing is shown for a copy that did not land.</b> `CopyReport.copied` is true only when the
 * write RESOLVED; a refusal and a clipboard held by another program both come back false, and the
 * sentence `copyText.ts` has already put in the status bar is then the only thing the person sees,
 * which is right — a tick there would be a lie about where their paste is coming from.</p>
 *
 * <p>The rejection is caught rather than left to float. `textCopier.copy` answers a report on every
 * path it knows, but an unowned rejection from a boundary like this one surfaces as an extension-host
 * error rather than as anything the tab can say, and the panel's own message dispatch already guards
 * itself the same way.</p>
 */
function tellThePage(
  said: (message: string) => void,
  panels: ChatPanels,
  id: object,
  index: number,
  block: number | undefined,
  sig: string,
  copying: Promise<CopyReport>,
): void {
  void copying.then((report) => {
    // THE RULE IS IN `answerCopy.ts`, where a test can reach it. Nothing in this file can be
    // imported by the suite, so a condition written here is a condition nothing checks.
    const landed = acknowledgement(report, { index, ...(block === undefined ? {} : { block }), sig });
    if (landed === undefined) {
      return;
    }
    const entry = panels.entryOf(id);
    if (entry !== undefined) {
      pushChatCopied(entry, landed.index, landed.block, landed.sig);
    }
  }).catch((reason: unknown) => {
    // NAMED, not swallowed. The person already has the sentence `copyText.ts` put in the status bar,
    // so this is not a second thing to tell them about the copy — it is the extension saying that its
    // own acknowledgement path failed, which is otherwise invisible: the control simply never ticks
    // and nothing anywhere says why. (Three reviewers, the code round; `coding-style.md` forbids a
    // silently swallowed error.)
    said(`The copy could not be acknowledged on the page: ${reason instanceof Error ? reason.message : String(reason)}`);
  });
}

/**
 * Open a file an ANSWER named, if it is really inside this workspace.
 *
 * <p>The renderer checked the shape and `chatCommandOf` checked it again, and neither is enough: a
 * string can look confined and still leave through a folder that merely starts with the same
 * letters. So the path is resolved against each workspace root and the RESULT is what is judged.</p>
 *
 * <p><b>Judged by where it LEADS, and that is a correction.</b> Until 2026-09-17 this compared the
 * path AS WRITTEN — `isInside(folder.uri.path, target.path)` — and then called `stat` and
 * `showTextDocument`, both of which follow links. A workspace holding `docs -> /home/me/.ssh`
 * therefore passed the check and opened the file outside it, from a link in a model's answer.
 * `leadsInside` requires containment of the written pair AND the canonical pair, and fails closed
 * when either cannot be canonicalised. It is `claudeSessions`' own check, which had been through
 * two gate rounds over exactly this shape — not a second implementation of one.</p>
 *
 * <p><b>`fsPath`, not `path`.</b> A URI path (`/d:/rsd/x`) compared against what `realpath` returns
 * (`d:\rsd\x`) refuses everything on Windows, which is a security fix that looks like it works
 * because nothing opens.</p>
 *
 * <p><b>The race this does NOT close, stated rather than implied.</b> `showTextDocument` re-opens by
 * PATH, so between the check and the open a local writer can replace the file or one of its parent
 * components. VS Code's API takes a `Uri` rather than a file descriptor, so there is no no-follow
 * handle to hand it and the race cannot be closed from here. What it IS bounded by: the attacker
 * must already be able to write inside the workspace — a far smaller set than "anything a model can
 * put in a link", which is the set this removes. The check runs immediately before the open.</p>
 *
 * <p>A reference that resolves nowhere is SAID rather than swallowed: a link that quietly does
 * nothing is a link a person presses twice.</p>
 */
async function openWorkspaceFile(
  _id: object,
  requested: string,
  line: number,
  refuse: (message: string) => void,
): Promise<void> {
  for (const folder of vscode.workspace.workspaceFolders ?? []) {
    const target = vscode.Uri.joinPath(folder.uri, requested);
    if ((await insideReally(folder.uri.fsPath, target.fsPath)) === undefined) {
      continue;
    }
    try {
      await vscode.workspace.fs.stat(target);
    } catch {
      continue;
    }
    const editor = await vscode.window.showTextDocument(target);
    if (line > 0) {
      const at = new vscode.Position(Math.max(0, line - 1), 0);
      editor.revealRange(new vscode.Range(at, at), vscode.TextEditorRevealType.InCenter);
      editor.selection = new vscode.Selection(at, at);
    }

    return;
  }

  refuse(`There is no ${requested} in this workspace.`);
}

/**
 * Where a conversation's pictures live: one directory per conversation, under the extension's own.
 *
 * <p>The vendor process OPENS these files — that is the mechanism phase 0 measured — so they are
 * real files with a real path, and their lifetime is a contract rather than a detail. One directory
 * per conversation is what makes forgetting them possible: the tab closing removes it whole, the
 * way `chatOrphans.ts` ends the processes.</p>
 */
function pictureDir(id: string): string {
  return path.join(coaiDataDir(), 'pictures', id.replace(/[^\w-]/g, ''));
}

/** Take the picture off, and take the FILE with it — a file nobody will open is a file left behind. */
function forgetPicture(thread: Thread): void {
  if (thread.attachedPath.length > 0) {
    try {
      fs.rmSync(thread.attachedPath, { force: true });
    } catch {
      // A file that cannot be removed is not worth a sentence to the person: it is in a temp
      // directory the tab's own close sweeps, and saying so would explain nothing they can act on.
    }
  }
  thread.attached = '';
  thread.attachedPath = '';
}

/**
 * Keep a pasted picture, or say why it cannot be kept.
 *
 * <p>Refused BY NAME where the chosen provider cannot take one. The worst outcome this feature has
 * is a picture that silently does not arrive: somebody pastes a screenshot, asks about it, and is
 * answered about the text alone with nothing anywhere saying the image was dropped.</p>
 */
async function attachPicture(entry: ChatEntry, thread: Thread, dataUrl: string): Promise<void> {
  const refusal = imageRefusal(thread.providerId);
  if (refusal.length > 0) {
    show(entry, thread.running, refusal);

    return;
  }
  const picture = pastedImage(dataUrl);
  if (picture === undefined) {
    show(entry, thread.running, 'That is not a picture this can send — PNG, JPEG, WebP and GIF only.');

    return;
  }
  forgetPicture(thread);
  const dir = pictureDir(entry.id.toString());
  try {
    await fs.promises.mkdir(dir, { recursive: true });
    const file = path.join(dir, imageFileName(picture.type, thread.turn + 1));
    await fs.promises.writeFile(file, Buffer.from(picture.base64, 'base64'));
    thread.attached = dataUrl;
    thread.attachedPath = file;
  } catch {
    show(entry, thread.running, 'The picture could not be written to disk, so nothing was attached.');

    return;
  }
  show(entry, thread.running, '');
}
