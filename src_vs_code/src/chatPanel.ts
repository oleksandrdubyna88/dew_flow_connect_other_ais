import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { PageMessage, chatCommandOf, offersPair } from './chatMessages';
import { ChatEntry, DisposableSession, RevealablePanel } from './chatPanels';
import {
  ChatMessage,
  ChatModelChoice,
  ChatPageState,
  TurnMarks,
  chatCappedHtml,
  chatMessagesHtml,
  chatPageHtml,
  chatPickerHtml,
  chatPresetRowsHtml,
  chatStatusHtml,
} from './chatPage';
import { ChatProvider } from './chatModels';
import { ModelPreset, PromptPreset } from './chatPresets';
import { chatTabIcon } from './chatIcon';
import { escapeHtml } from './webviewHtml';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';
import { applyToneDelta, currentTextTone, pushTextToneTo } from './textToneHost';

/**
 * The `vscode` half of a conversation tab, and deliberately the thin one.
 *
 * <p>Everything that can be decided without a host lives elsewhere: which tab a conversation belongs
 * to is `sessionKey.ts`, which conversations exist is `chatPanels.ts`, what a page message means is
 * `chatMessages.ts`, and what the page looks like is `chatPage.ts`. What is left here is the part
 * that can only be done against the real API — creating the panel, carrying its messages, and
 * pushing state into it — which is also the part no unit test can reach. Keeping it small is how the
 * untestable surface stays small.</p>
 *
 * <p><b>Hooks are called with the conversation's `id`, never with the tab key.</b> The first version
 * closed over the key it was created with, and a `rekey` — which happens when the host replaces a
 * live tab's object — left every message from that page looking itself up under a key nobody held:
 * sends silently dropped, and a close that could not find its entry leaving the vendor process
 * running. The id is created here, once, and outlives every key the tab wears. (gemini and codex,
 * the code round, independently.)</p>
 *
 * <p><b>Everything assigned to `innerHTML` in the page is escaped HERE, where it is built.</b> The
 * page never escapes anything it is handed; it renders what the host produced. That is the invariant
 * a reader should carry into `chatPage.ts`.</p>
 */

/** What the panel asks the extension to do. Every hook is passed the conversation's stable id. */
export interface ChatPanelHooks {
  /** The person pressed send. */
  readonly onSend: (id: object, text: string) => void;
  /**
   * The person chose a different PAIR — already checked against what this conversation offers.
   *
   * <p>`modelId` may be empty: the page sends no model when the provider moved, because the model
   * that was showing belonged to the provider being left.</p>
   */
  readonly onPick: (id: object, providerId: string, modelId: string, draft?: string) => void;
  /** A saved prompt was pressed. The host owns the list; the page only names which. */
  readonly onUsePrompt: (id: object, presetId: string, draft?: string) => void;
  /** A saved model was pressed. Its provider and model answer, and its starting prompt is offered. */
  readonly onUseModel: (id: object, presetId: string, draft: string) => void;
  /**
   * The page asked what the person wrote in the session this conversation came from.
   *
   * <p>It must ALWAYS answer, even to say it cannot: the page paints *Reading the session…* the
   * moment the region opens, and a hook that returns without posting leaves that on the screen for
   * as long as the tab is open. (gemini, the code round.)</p>
   */
  readonly onShowAsked: (id: object) => void;
  /**
   * The page asked that a handover start here instead of at the beginning.
   *
   * <p>The host records it and pushes the transcript back with the rule on it. The page does not
   * draw the rule because it was pressed — a press that failed to record would otherwise show a line
   * that is not durable, while the next Team turn quietly re-sent everything above it.</p>
   */
  readonly onCarryFrom: (id: object, at: number) => void;
  /**
   * The person stopped the answer they were waiting for.
   *
   * <p>`turn` is the turn the page believed was running — a whole number counted from 1, never 0.
   * The host stops that turn and no other, because this message can land after the turn it names has
   * already finished, by which time the next question may be in flight. A page that cannot say which
   * turn it means gets no stop at all: `chatCommandOf` refuses such a message before it reaches this
   * hook, so there is no wildcard to fall back on. (Whoever writes the button in `chatPage.ts`: send
   * the turn the state you rendered was carrying.)</p>
   */
  readonly onStop: (id: object, turn: number) => void;
  /** VS Code closed the tab — the registry must forget it and end its session. */
  readonly onClosed: (id: object) => void;
  /** Start again with the same passage. */
  readonly onRestart: (id: object) => void;
  /** Ask the model that is chosen NOW the question the last answer was given to. */
  readonly onReask: (id: object) => void;
  /** A picture was pasted into the composer, or taken off it again. */
  readonly onAttach: (id: object, dataUrl: string) => void;
  readonly onUnattach: (id: object) => void;
  /** Move the thread to a model that keeps a conversation. */
  readonly onUseLocal: (id: object) => void;
  /** The page trapped an error, or the host failed to handle one of its messages. */
  readonly onPageError: (id: object, message: string) => void;
  /**
   * Copy the answer at `index` as the MARKDOWN it arrived as.
   *
   * <p>By index into the thread's own messages, which is the array the page was rendered from —
   * turns are appended and never reordered, so the number identifies the same answer for as long as
   * the tab lives. What is copied is the source, because that is the one thing a selection cannot
   * give: selecting the page gives what the page shows.</p>
   */
  readonly onCopyAnswer: (id: object, index: number) => void;
  /**
   * Open a file an ANSWER named, at a line.
   *
   * <p>A hook rather than something this module does, for the reason every other action here is one:
   * `chatPanel.ts` is the `vscode` wiring and nothing else, and a function that reaches into the
   * workspace, the filesystem and the active editor is not wiring — it is the half a test cannot
   * reach. (gemini, the code round.)</p>
   */
  readonly onOpenFile: (id: object, path: string, line: number) => void;
}

/** Everything the page shows that can change after it is open. */
export interface ChatPushState {
  readonly messages: readonly ChatMessage[];
  readonly running: boolean;
  readonly capped: boolean;
  readonly failure: string;
  readonly models: readonly ChatModelChoice[];
  /** Every row that can answer, each with its own models — what the picker offers. */
  readonly providers: readonly ChatProvider[];
  /** Who would answer a re-ask, or empty when there is nothing to re-ask. */
  readonly reask: string;
  /** The picture waiting to go with the next question, as a data URL, or empty. */
  readonly attached: string;
  /** What this conversation has cost so far, as a line, or empty. */
  readonly spend: string;
  readonly providerId: string;
  readonly modelId: string;
  /** Which saved PROMPT is in force — the button that looks pressed. */
  readonly promptId: string;
  /** The two lists, so the rows of buttons can be redrawn with the right one pressed. */
  readonly promptPresets: readonly PromptPreset[];
  readonly modelPresets: readonly ModelPreset[];
  /**
   * Which words in the turn are which — who is answering, what is asked, and the machinery.
   *
   * <p>Pushed because the page cannot tell them apart: it holds one string, and it draws the box's
   * own text transparent so a layer behind it can colour part of it. Empty marks nothing.</p>
   */
  readonly marks: TurnMarks;
  /** Which model button is pressed: the preset chosen, which is ahead of the session mid-switch. */
  readonly chosenModelId: string;
  /** Where a handover starts — the index of the first message carried. Zero carries everything. */
  readonly carryFrom: number;
  /**
   * How many turns are ahead of this one on a Team server, or 0 for none and for a local model.
   *
   * <p>Not optional. A push that says nothing about the queue would leave the last number on screen
   * while the turn is being answered — and this state is deduplicated by its serialisation, so a
   * stale position would be pushed exactly once and then stick.</p>
   */
  readonly queued: number;
  /**
   * Which turn is in flight, counted from 1 — 0 when none is, or when it cannot be named.
   *
   * <p>The control that stops a turn is rendered INTO the thinking line with this number in it, so a
   * control on screen names the turn it was drawn for and no other. That is the whole reason it
   * travels with the state rather than being remembered by the page.</p>
   */
  readonly turn: number;
}

/**
 * Open a tab for one conversation.
 *
 * <p>Returns the registry's entry rather than the panel: the caller's next act is to put it in the
 * map, and handing back a `WebviewPanel` would invite a second place that knows how to dispose one.</p>
 *
 * @param known whether a model id the page asks for is one this conversation was actually offered
 * @param extensionUri where this extension was installed — the only way to name a file it ships
 * @param restored the panel VS Code handed back after a window reload, for the serializer to fill.
 *   Absent for a tab somebody opened, which is the ordinary case. It exists so that a RESTORED tab
 *   is built here and nowhere else: the icon, the message wiring, the disposal and the zoom hook are
 *   all set up in this one function, and a second place that made a chat panel would have to
 *   remember every one of them, forever, on every change to any of them.
 */
export function createChatPanel(
  state: ChatPageState,
  session: DisposableSession,
  hooks: ChatPanelHooks,
  extensionUri: vscode.Uri,
  restored?: vscode.WebviewPanel,
): ChatEntry {
  // The conversation's own identity, created once and never replaced. See the module comment.
  const id = { conversation: state.title };
  // What this conversation is allowed to be answered by, kept from the state it was opened with and
  // replaced by every push. A separate predicate handed in at creation was the alternative, and it
  // would have drifted the moment a Team server's catalog arrived: two places deciding what a valid
  // model is, one of them frozen. (gemini, the second code round.)
  offered.set(id, pairsOf(state.providers));

  const panel = restored ?? vscode.window.createWebviewPanel(
    'coaiChat',
    state.title,
    vscode.ViewColumn.Active,
    // `enableFindWidget` is the whole of Ctrl+F: the editor's find bar never reaches a webview panel
    // without it, and an answer long enough to be worth searching is the ordinary case here.
    {
      enableScripts: true,
      retainContextWhenHidden: true,
      enableFindWidget: true,
      localResourceRoots: [],
    },
  );
  if (restored !== undefined) {
    // A panel VS Code rebuilt runs the page again, so the script has to be allowed again — the
    // webview half of the options is settable and this is where it is set. The PANEL half is not:
    // `retainContextWhenHidden` and `enableFindWidget` are fixed at creation and there is no API to
    // change them afterwards, so whether a restored tab keeps its find bar is the workbench's call
    // and not ours. Named here rather than pretended about.
    restored.webview.options = { enableScripts: true, localResourceRoots: [] };
    restored.title = state.title;
  }
  // A pair, because a tab icon is workbench chrome and no theme reaches it — `var(--vscode-…)` is
  // unavailable there, and the colour has to be baked into the file. The paths are resolved by
  // `chatTabIcon` rather than spelled out here: a path written twice is a path that goes stale on the
  // first rename, and nothing type-checks a `Uri`.
  //
  // An assignment onto a live object, which the immutability rule would ordinarily refuse. The rule
  // governs OUR data; this handle is VS Code's, `iconPath` is settable only after creation, and every
  // webview in this extension is configured the same way (`panel.webview.html = …`,
  // `view.webview.options = …`). Raised by two reviewers on the code round; recorded rather than
  // worked around, because the alternative is a creation API the API does not have.
  panel.iconPath = chatTabIcon((...segments) => vscode.Uri.joinPath(extensionUri, ...segments));
  panel.webview.html = chatPageHtml(state, crypto.randomBytes(16).toString('hex'));

  const scale = pushUiScaleTo(panel.webview);
  const tone = pushTextToneTo(panel.webview);
  panel.webview.onDidReceiveMessage((message: PageMessage) => {
    // A detached boundary: nothing awaits this, so a rejection here would have no owner and the
    // extension host would report it as unhandled instead of the tab saying anything. (codex.)
    void handle(id, message, hooks).catch((reason: unknown) => {
      hooks.onPageError(id, reason instanceof Error ? reason.message : String(reason));
    });
  });
  panel.onDidDispose(() => {
    scale.dispose();
    tone.dispose();
    try {
      hooks.onClosed(id);
    } catch {
      // A throw here escapes into VS Code's disposal loop, which is not ours to break. The session
      // it failed to close is the caller's problem to notice; the editor's is not. (local.)
    }
  });

  const revealable: RevealablePanel = {
    reveal: () => panel.reveal(),
    dispose: () => panel.dispose(),
    post: (message) => {
      void panel.webview.postMessage(message);
    },
  };

  return { id, panel: revealable, session };
}

/**
 * One message from the page.
 *
 * <p>The READING of the message is `chatMessages.ts`, which needs no host and is tested; what is
 * left here is the dispatch. That split exists because a wrong message type would otherwise have
 * shipped with every test green — a person's send quietly ignored. (codex, the plan round.)</p>
 */
async function handle(id: object, message: PageMessage, hooks: ChatPanelHooks): Promise<void> {
  const command = chatCommandOf(message);
  switch (command.kind) {
    case 'zoom':
      await applyZoomDelta(command.delta);

      return;
    case 'tone':
      await applyToneDelta(command.delta);

      return;
    case 'send':
      hooks.onSend(id, command.text);

      return;
    case 'pick':
      // A page can post any pair it likes — a stale retained webview certainly will, and a tampered
      // one might. Choosing a model is choosing who gets paid, so the host checks the name against
      // what this conversation was actually offered rather than trusting the page. (codex.)
      // The PAIR is checked as a pair, the rule this feature keeps everywhere else: a model offered
      // by somebody is not a model offered by THIS provider, and `vendor-routing.md` is the reason.
      if (offersPair(offered.get(id), command.provider, command.model)) {
        hooks.onPick(id, command.provider, command.model, command.draft);
      } else {
        hooks.onPageError(id, `that pair is not one this conversation offers: ${command.provider} · ${command.model}`);
      }

      return;
    case 'usePrompt':
      hooks.onUsePrompt(id, command.id, command.draft);

      return;
    case 'useModel':
      hooks.onUseModel(id, command.id, command.draft);

      return;
    case 'showAsked':
      hooks.onShowAsked(id);

      return;
    case 'carryFrom':
      hooks.onCarryFrom(id, command.at);

      return;
    case 'stop':
      hooks.onStop(id, command.turn);

      return;
    case 'openLink':
      // Handed to the editor, not navigated in the page: the webview has `localResourceRoots: []`
      // and a CSP that loads nothing, so this is the only way out — and it is the way a person can
      // see where they are going before they arrive.
      await vscode.env.openExternal(vscode.Uri.parse(command.url));

      return;
    case 'openFile':
      hooks.onOpenFile(id, command.path, command.line);

      return;
    case 'copyAnswer':
      hooks.onCopyAnswer(id, command.index);

      return;
    case 'reask':
      hooks.onReask(id);

      return;
    case 'attach':
      hooks.onAttach(id, command.dataUrl);

      return;
    case 'unattach':
      hooks.onUnattach(id);

      return;
    case 'restart':
      hooks.onRestart(id);

      return;
    case 'useLocal':
      hooks.onUseLocal(id);

      return;
    case 'pageError':
      hooks.onPageError(id, command.message);

      return;
    default:
      // Ignored on purpose — see the exception documented in `chatMessages.ts`.
      return;
  }
}

/**
 * What was last pushed to a conversation, so an unchanged state is not pushed again.
 *
 * <p>Keyed by the conversation's id and weak, so a closed tab's record goes with it. The page
 * replaces its whole message region on every push, and pushing an identical one costs a re-render
 * that drops the reader's text selection for nothing. The same shape `roundsLogPanel.ts` uses.</p>
 */
const lastPushed = new WeakMap<object, string>();

/** Which PAIRS each conversation currently offers — the one source the pick check reads. */
const offered = new WeakMap<object, ReadonlyMap<string, ReadonlySet<string>>>();

/** The pairs a state offers, by provider. */
function pairsOf(providers: readonly { readonly id: string; readonly models: readonly { readonly id: string }[] }[]):
ReadonlyMap<string, ReadonlySet<string>> {
  return new Map(providers.map((provider) => [provider.id, new Set(provider.models.map((model) => model.id))]));
}

/**
 * Push what changed into an open page.
 *
 * <p>Regions, not a re-render. Re-assigning `webview.html` would rebuild the page and throw away the
 * half-typed follow-up in the composer — which is exactly what a person does while waiting nine
 * seconds for an answer.</p>
 *
 * <p>It carries the MODEL as well as the messages. After a capped thread takes "continue with a
 * local model" the host switches models, and a page that was never told would keep showing the
 * remote one as selected while the answers came from somewhere else — misleading about behaviour,
 * latency and cost, all three. (gemini and codex, the code round.)</p>
 *
 * @returns whether anything was actually sent
 */
export function pushChatState(entry: ChatEntry, state: ChatPushState): boolean {
  const payload = {
    type: 'state',
    // MARKED like the composer below it: a question that has been sent is the same words, and they
    // stop being readable if the colours go when it moves.
    // RUNNING too. The full-page render was given it and this one was not, so every push during a
    // turn redrew the button the full render had just withheld. (CodeRabbit, PR #208.)
    messagesHtml: chatMessagesHtml(state.messages, state.marks, state.carryFrom, state.running),
    running: state.running,
    capped: state.capped,
    thinkingHtml: chatStatusHtml(state.running, state.queued, state.turn),
    cappedHtml: chatCappedHtml(state.capped),
    failureHtml: state.failure.length === 0 ? '' : `<div class="failure">${escapeHtml(state.failure)}</div>`,
    pickerHtml: chatPickerHtml({ providers: state.providers, refused: [] }, state.providerId, state.modelId),
    // The ROWS as well as the picker. They were drawn once, when the page was built, so pressing a
    // prompt changed the words in the box and left every button looking exactly as it had — and a row
    // of buttons where the one in force looks like the others is a row you have to remember.
    presetsHtml: chatPresetRowsHtml(state.promptPresets, state.modelPresets, state.promptId, state.chosenModelId),
    // Trimmed, because the instruction they were built into is: an untrimmed mark would not match
    // the text in the box and the page would silently colour nothing.
    marks: { ...state.marks, role: state.marks.role.trim(), task: state.marks.task.trim() },
    modelId: state.modelId,
  };

  offered.set(entry.id, pairsOf(state.providers));

  const serialised = JSON.stringify(payload);
  if (lastPushed.get(entry.id) === serialised) {
    return false;
  }
  lastPushed.set(entry.id, serialised);
  entry.panel.post(payload);

  return true;
}

/**
 * Put text into an open tab’s composer, unsent.
 *
 * <p>The menu path’s whole shape: the passage arrives, the person reads it, and one keypress
 * sends it. A separate message from the state push because it must NOT be idempotent — pushing
 * the same state twice should change nothing, while inviting a second passage into the composer
 * is exactly what a second invocation means.</p>
 */
export function pushChatDraft(entry: ChatEntry, draft: string): void {
  entry.panel.post({ type: 'state', draft });
}

/**
 * REPLACE what is in the composer, rather than adding to it.
 *
 * <p>Two operations because they are two instructions. A captured passage is ADDED to whatever was
 * being written — throwing that away is the one thing this feature has been careful about since the
 * prompt box lost what was typed into it. A prompt preset is the opposite: it IS "ask this instead",
 * and joining it onto a half-written question produces one neither of them wrote. They had been
 * sharing the appending one. (CodeRabbit, PR #200.)</p>
 */
export function setChatDraft(entry: ChatEntry, draft: string): void {
  entry.panel.post({ type: 'state', setDraft: draft });
}

/** The scale the page opens at, so a new tab matches the ones already open. */
export function chatUiScale(): number {
  return currentUiScale();
}

/** The tone the page opens at, for the same reason: a new tab matches the ones already open. */
export function chatTextTone(): number {
  return currentTextTone();
}
