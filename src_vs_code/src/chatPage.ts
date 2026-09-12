import { escapeHtml, jsonForScript } from './webviewHtml';
import { renderAnswer } from './renderAnswer';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';
import { TONE_CSS, toneControlHtml, toneScript, toneStyle } from './textTone';
import { vendorPalette } from './vendorColour';
import { ChatProvider, ChatProviderList } from './chatModels';
import { ModelPreset, PromptPreset } from './chatPresets';
import { pastedImage } from './chatImage';

/**
 * The conversation tab: the passage that started it, what has been said, and a box to say more.
 *
 * <p>PURE — no `vscode`, no `node:`. `bundledPage.test.ts` bundles this module the way the shipped
 * page is bundled and refuses a `node:` import in it, which is what makes "a page module imports
 * nothing from the host" a check rather than a sentence. Everything the page needs arrives as a
 * value; everything it wants done goes back as a message.</p>
 *
 * <p><b>The page escapes nothing it is handed at render time.</b> Every string that reaches
 * `innerHTML` — the messages, the failure, the capped region, the picker — is built by a function in
 * THIS file or by `chatPanel.ts`, and escaped there, at the point it is built. That is the invariant:
 * escaping happens where the string is made, never where it is written. A reviewer looking at the
 * `innerHTML` assignments in the script below should follow the value back to its builder rather
 * than conclude the page is unsafe. (Raised on the code round; it was true, and it was implicit.)</p>
 *
 * <p><b>The passage is at the top and it is not decoration.</b> The menu path takes whatever is in
 * the clipboard and cannot know whether it is the passage just selected or something copied an hour
 * ago — the gate raised that three times. Showing the text the conversation is ABOUT is how a person
 * sees a stale clipboard instead of discovering it in the answer.</p>
 *
 * <p><b>It used to be capped in height with a scrollbar of its own, and is not any more.</b> The cap
 * existed because "a fifty-line selection would otherwise push the composer off the screen on open"
 * — and the composer is pinned now, so nothing can push it anywhere. What a long selection pushes
 * down is the conversation, which is what the page scrolls to anyway. Two scrollbars on one page was
 * the price of the old arrangement and the operator named it.</p>
 *
 * <p><b>The composer is disabled while a turn runs, and that is a feature.</b> A real explanation
 * took 9.4 s when it was measured, and eight of those seconds are silent. Two turns down one NDJSON
 * pipe would interleave; the page makes that impossible rather than the session refusing it late.</p>
 */

/** One thing said, by one of the two parties. */
/** Which model gave an answer — recorded when it ARRIVED, never looked up afterwards. */
export interface AnsweredBy {
  readonly id: string;
  readonly label: string;
}

export interface ChatMessage {
  readonly role: 'you' | 'model';
  readonly text: string;
  /**
   * WHAT THIS QUESTION WAS ASKED WITH — the role and the task in force when it was sent.
   *
   * <p>Carried by the message rather than read from the conversation, because the conversation moves
   * on: switch model after asking and the current role no longer matches the words above, so a
   * question sent a minute ago would quietly lose its colours. (codex and local, the plan round.)</p>
   *
   * <p>Optional: an answer has none, and neither has a question from a build before this field —
   * a restored tab, whose transcript falls back to the conversation's current marks.</p>
   */
  readonly marks?: { readonly role: string; readonly task: string };
  /**
   * The model that gave this answer, for a `model` message.
   *
   * <p>Optional because two kinds of message legitimately have none: what the PERSON said, and an
   * answer from before this field existed — a conversation restored from a tab that predates it.
   * Both fall back to the old caption rather than rendering an empty line where a name should be.</p>
   *
   * <p>It is what ANSWERED, not what is configured. Switching the model mid-conversation is a
   * shipped feature and the thread is carried across, so a tab routinely holds answers from two
   * models; reading the current setting would relabel every one of them.</p>
   */
  readonly model?: AnsweredBy | undefined;
}

/** A model id as a class name. Ids come from a Team server's catalog, so they are not trusted. */
export function modelClass(id: string): string {
  return `model-${id.toLowerCase().replace(/[^a-z0-9-]/g, '-').slice(0, 48)}`;
}

/** A model the picker may offer. `remote` models say what they cannot do. */
export interface ChatModelChoice {
  readonly id: string;
  readonly label: string;
  readonly caption: string;
}

export interface ChatPageState {
  /**
   * This conversation's own id, minted where the tab is made and never shown to anybody.
   *
   * <p>The page hands it straight back through `setState`, which is the only thing VS Code preserves
   * about a webview across a window reload — so it is the whole of how a restored tab knows WHICH
   * conversation it is. A title cannot do that job: two Claude Code sessions can both be called
   * `main`, which is why `chatPanels.ts` is keyed by object identity rather than by name.</p>
   */
  readonly id: string;
  /** The Claude Code session this tab belongs to — its label, shown as the heading. */
  readonly title: string;
  readonly passage: string;
  readonly messages: readonly ChatMessage[];
  readonly models: readonly ChatModelChoice[];
  /** Every provider this conversation may put a question to, each with its own models. */
  readonly providers: readonly ChatProvider[];
  /**
   * Who would answer a re-ask, or empty when there is nothing to re-ask.
   *
   * <p>The gesture is an EMPTY box and Enter — free to take, because an empty box has always been
   * refused — and a feature whose only trigger is pressing Enter on nothing is a feature nobody
   * discovers. So the Send button reads *Re-ask · <model>* whenever this is set, which is both the
   * second way in and the only way to know the first exists.</p>
   *
   * <p>The HOST decides: it knows which model gave the last answer and which one is chosen now.</p>
   */
  readonly reask: string;
  /**
   * The picture waiting to go with the next question, as a data URL — or empty when there is none.
   *
   * <p>A data URL because the page cannot read a file: `localResourceRoots` is empty and the CSP
   * loads nothing from disk. The HOST holds the real file; this is only what a person sees.</p>
   */
  readonly attached: string;
  /**
   * What this conversation has cost so far, as a line, or empty when it has cost nothing.
   *
   * <p>A line rather than a number, because what it says varies: a bill, an estimate wearing the
   * tilde, or a count of turns nobody priced. The HOST composes it — `chatSpend.ts` holds the rule
   * — and the page shows what it was handed.</p>
   */
  readonly spend: string;
  /** The prompts a person saved, as buttons above the composer. */
  readonly promptPresets: readonly PromptPreset[];
  /** The models a person saved, likewise. */
  readonly modelPresets: readonly ModelPreset[];
  /** The vendor ROW that answers — the identity a saved choice stores. */
  readonly providerId: string;
  /** Which of that row's models. Empty means "whatever the row is set to". */
  readonly modelId: string;
  /** Which saved PROMPT this conversation is using — the button that looks pressed. */
  readonly promptId: string;
  /**
   * Which turn is in flight, counted from 1 — and 0 when the page cannot say.
   *
   * <p>It is in the STATE rather than kept in the script because the thinking line is replaced
   * wholesale on every push: the control that stops a turn is rendered with that turn's number in
   * it, so a control on screen can only ever name the turn it was drawn for. The seam refuses a stop
   * that names no turn at all, deliberately — a wildcard stop ends whatever is running, which by the
   * time a late message lands can be the turn AFTER the one somebody pressed for.</p>
   */
  readonly turn: number;
  /** A turn is in flight: the composer is locked and the thinking line is shown. */
  readonly running: boolean;
  /**
   * The conversation has reached its limit and cannot take another turn.
   *
   * <p>Only a remote model reaches this: it holds no conversation, so turn four re-sends everything
   * said so far for the fourth time, and the thread's cost grows while its usefulness does not. The
   * owner capped it at three on 2026-09-08. A capped page must OFFER something — a locked box with
   * no way out is the failure the gate named — so it shows the two honest actions, start again or
   * move the thread to a local model, which does have memory.</p>
   */
  readonly capped: boolean;
  /** Empty when nothing failed. A sentence when something did. */
  readonly failure: string;
  /**
   * What the composer opens with, unsent.
   *
   * <p>The menu path fills it rather than sending: it took whatever was on the clipboard and
   * cannot know how old that is, so the person presses send once and the wrong-content vendor
   * call becomes impossible. A draft is therefore a first-class part of the page state, not a
   * decoration — it is the whole difference between the two doors.</p>
   */
  readonly draft: string;
  /**
   * WHICH WORDS IN THE TURN ARE WHICH — who is answering, what is asked, and the machinery.
   *
   * <p>The page is told because it cannot work it out: a textarea holds one string, and a role and a
   * task read alike. Marking "the first block" painted whichever of the two happened to be there —
   * so a conversation with no role painted its task in the role's colour, and one with both left the
   * task unmarked, which is what the operator saw.</p>
   */
  readonly marks: TurnMarks;
  /**
   * WHICH MODEL BUTTON is pressed — the preset the person chose.
   *
   * <p>Not `providerId`, which is the model actually answering. A switch disposes one process and
   * starts another, and while that runs the choice has been made but the session has not moved: a
   * button drawn from `providerId` stayed unpressed for as long as it took, which is what the
   * operator felt as the model switch hanging.</p>
   */
  readonly chosenModelId: string;
  /**
   * Whether this conversation came from a Claude Code session — the only case where there is
   * anything to read back. A chat opened from a file has no session behind it, so it gets no button
   * rather than a button that apologises.
   */
  readonly fromSession: boolean;
  /** What the person wrote in that session, once they have asked for it. Empty until then. */
  readonly asked: readonly string[];
  readonly uiScale: number;
  /**
   * How far the text is from the theme's own colour: 0 is the theme, up is brighter, down is
   * dimmer and warmer. Beside `uiScale` because it is the same kind of thing — a preference about
   * this person's eyes, kept in a setting so it follows them.
   */
  readonly textTone: number;
}

/**
 * WHICH WORDS IN A TURN ARE WHICH: who is answering, what is being asked, and the machinery.
 *
 * <p>Three kinds of text and three ways of showing them, because a turn is not one thing: a role is
 * a standing fact, a task is this question, and the language line, the material note and the fence
 * are addressed to the model and identical every time.</p>
 */
export interface TurnMarks {
  /** WHO is answering — the model preset's own prompt. Empty marks nothing. */
  readonly role: string;
  /** WHAT is being asked — the prompt preset in force. Empty marks nothing. */
  readonly task: string;
  /** The lines that are nobody's question, marked to be skipped rather than read. */
  readonly service: readonly string[];
}

/** Nothing known about a turn, which marks none of it. */
export const NO_MARKS: TurnMarks = { role: '', task: '', service: [] };

/** One stretch of a turn, and what it IS. */
export interface TurnPart {
  readonly kind: 'role' | 'task' | 'service' | 'plain';
  readonly text: string;
}

/**
 * One turn, cut into the parts it is made of — the same function behind the composer and inside the
 * transcript.
 *
 * <p>Embedded into the page by `toString()`, like `shouldFollow` beside it, so what the tests
 * exercise is what the page runs. A second implementation would be a second set of rules about the
 * same words, and the box and the message it becomes a second later would drift.</p>
 *
 * <p><b>It returns parts rather than HTML, and calls nothing at all.</b> The first version built the
 * markup here and escaped it with a helper of its own — and the bundler hoisted that helper out of
 * the function, so the source `toString()` handed the page called a name the page did not have. A
 * function that is going to be read out of itself may use nothing but its own arguments.</p>
 *
 * <p>The two halves of the instruction are matched at the FRONT and nowhere else: text that no
 * longer begins with them has been written over by the person, and marking a prefix that does not
 * match colours half of a sentence they wrote. The service lines are matched wherever they stand,
 * because they always stand in the same place and never mean anything else.</p>
 */
export function turnParts(text: string, marks: TurnMarks): TurnPart[] {
  const parts: TurnPart[] = [];
  const role = marks === undefined || marks.role === undefined ? '' : marks.role;
  const task = marks === undefined || marks.task === undefined ? '' : marks.task;
  const service = marks === undefined || marks.service === undefined ? [] : marks.service;
  let at = 0;
  if (role.length > 0 && text.slice(0, role.length) === role) {
    parts.push({ kind: 'role', text: role });
    at = role.length;
  }
  const gap = at > 0 ? '\n\n' : '';
  if (task.length > 0 && text.slice(at, at + gap.length + task.length) === gap + task) {
    if (gap.length > 0) {
      parts.push({ kind: 'plain', text: gap });
    }
    parts.push({ kind: 'task', text: task });
    at += gap.length + task.length;
  }
  const rest = text.slice(at);
  let from = 0;
  while (from < rest.length) {
    let best = -1;
    let hit = '';
    for (let index = 0; index < service.length; index += 1) {
      const line = service[index] ?? '';
      const found = line.length === 0 ? -1 : rest.indexOf(line, from);
      if (found >= 0 && (best < 0 || found < best)) {
        best = found;
        hit = line;
      }
    }
    if (best < 0) {
      break;
    }
    if (best > from) {
      parts.push({ kind: 'plain', text: rest.slice(from, best) });
    }
    parts.push({ kind: 'service', text: hit });
    from = best + hit.length;
  }
  if (from < rest.length) {
    parts.push({ kind: 'plain', text: rest.slice(from) });
  }

  return parts;
}

/** The parts as HTML, escaped — the host's half of the drawing. */
export function markedTurn(text: string, marks: TurnMarks): string {
  return turnParts(text, marks)
    .map((part) => {
      const body = escapeHtml(part.text);
      if (part.kind === 'plain') {
        return body;
      }

      return part.kind === 'service'
        ? `<u class="service">${body}</u>`
        : `<mark class="${part.kind}">${body}</mark>`;
    })
    .join('');
}

/** The messages region on its own, so the host can push it without re-rendering the page. */
export function chatMessagesHtml(messages: readonly ChatMessage[], marks: TurnMarks = NO_MARKS): string {
  if (messages.length === 0) {
    return '<p class="empty">Nothing asked yet.</p>';
  }

  return messages
    .map((message, index) => {
      const mine = message.role === 'you';
      // What the PERSON typed is not markdown until they say so: a question that begins with a hash
      // is a question, not a heading, and rendering it would silently eat what they wrote.
      // MARKED, not merely escaped: a question that has been sent is the same words it was in the
      // box a moment earlier, and they stop being readable if the colours go when it moves. Asked
      // for looking at a sent turn — the role, the task and three lines of machinery, all one grey.
      const body = mine
        ? markedTurn(message.text, message.marks === undefined ? marks : { ...marks, ...message.marks })
        : renderAnswer(message.text);
      // The copy control carries the INDEX, and the host reads the message out of the same array
      // this was rendered from - so what is copied is the markdown that arrived, which is the one
      // thing a selection cannot give: selecting the page gives what the page shows.
      // Its OWN class. It used to be a `who` span inside the `who` row, so every rule written for
      // the row — flex, gap, margin, opacity — landed on the label too, and the next person to change
      // the row's layout would have moved the text with it. (gemini, the code round.)
      const said = !mine && message.model !== undefined && message.model.label.length > 0
        ? `<span class="author ${modelClass(message.model.id)}">${escapeHtml(message.model.label)}</span>`
        : `<span class="author">${mine ? 'You' : 'The other AI'}</span>`;
      const copy = mine
        ? ''
        : `<button type="button" class="copy" data-copy="${index}" title="Copy this answer as Markdown">Copy</button>`;
      // A rule after an answer, not after a question. The operator asked for a row of asterisks and
      // then settled on the rule, which is also what a row of asterisks BECOMES once markdown is
      // rendered - a thematic break.
      const end = mine ? '' : '<hr class="end">';

      return `<div class="msg ${mine ? 'you' : 'model'}">`
        + `<div class="who">${said}${copy}</div>`
        + `<div class="what">${body}</div>${end}</div>`;
    })
    .join('');
}

/** The picker, or nothing at all when there is only one model to pick. */
/** One `<option>`, with everything in it escaped — labels come from a Team server's catalog. */
function option(id: string, label: string, chosen: string): string {
  return `<option value="${escapeHtml(id)}"${id === chosen ? ' selected' : ''}>${escapeHtml(label)}</option>`;
}

/**
 * Two steps: WHICH PROVIDER, and then which of its models.
 *
 * <p>It used to be one flat list of the configured reviewer ROWS, each labelled with the single
 * model it happened to be set to — so `gemini · gemini-3.8-flash` was a row, not a choice, and
 * picking a different model meant leaving the conversation and reconfiguring a reviewer. The
 * operator's words for it were that the list read as an arbitrary handful.</p>
 *
 * <p><b>A provider is a vendor ROW, not a runtime</b>, and that was settled by measurement rather
 * than by preference: three vendors' reviewers independently overturned the plan's recommendation on
 * the pure half's round. The row is the identity a saved choice stores and the identity resolution
 * looks up, because the row carries the runtime, the executable, the base URL, the price and — for a
 * Team server — the server and the vendor name on it.</p>
 *
 * <p><b>The model list is the CHOSEN provider's and nobody else's.</b> A list holding another row's
 * models is a list somebody can pick a combination from that has no adapter — a Claude model through
 * `agy`, which `vendor-routing.md` forbids.</p>
 *
 * <p>A provider with ONE model still shows it. Hiding a list of one would leave a person unable to
 * see what will answer, which is the complaint the two steps exist for.</p>
 */
export function chatPickerHtml(list: ChatProviderList, providerId: string, modelId: string): string {
  if (list.providers.length === 0 && list.refused.length === 0) {
    return '';
  }
  const chosen = list.providers.find((provider) => provider.id === providerId) ?? list.providers[0];
  // WHICH VENDOR, and then which of its models. One entry per vendor among the saved models, valued
  // by the first saved model of that vendor — so choosing a vendor switches to it and the second
  // select fills with what it can be pointed at. Two selects that both named the saved model were
  // two dropdowns saying one thing, which is what the operator asked about.
  const seen = new Set<string>();
  const providers = list.providers
    .filter((provider) => !seen.has(provider.vendor) && seen.add(provider.vendor) !== undefined)
    .map((provider) => option(provider.id, provider.vendor, chosen?.vendor === provider.vendor ? provider.id : ''))
    .join('');
  const models = (chosen?.models ?? []).map((model) => option(model.id, model.label, modelId)).join('');
  // Refused rows are SHOWN. A person who configured a reviewer and finds the picker silently missing
  // it has no way to tell a bug from a policy — the rule the flat list already followed.
  const refused = list.refused
    .map((row) => `<div class="refused">${escapeHtml(row.reason)}</div>`)
    .join('');

  return `<div class="picker">`
    + `<select id="provider" aria-label="Which provider answers">${providers}</select>`
    + `<select id="model" aria-label="Which model answers">${models}</select>`
    + `<span class="caption" id="caption">${escapeHtml(chosen?.caption ?? '')}</span></div>${refused}`;
}

/**
 * The two rows of buttons above the composer: what to ASK, and what shall ANSWER.
 *
 * <p><b>Two rows and not one merged list</b>, and that was the operator's decision rather than a
 * layout preference: they are two kinds of decision, and one button that silently sets both is a
 * button whose effect cannot be predicted from its name. A preset that named a model AND a prompt
 * would be a third thing to explain.</p>
 *
 * <p><b>The colour is an EDGE, and it says WHICH HALF of the instruction the button changes.</b>
 * A model button wears the colour the ROLE is written in behind the composer; a prompt button wears
 * the TASK's. Press one and you can see, in the box below, exactly which words moved — which is the
 * whole question a person has about a row of buttons that all look alike.</p>
 *
 * <p>It used to be the VENDOR's colour from `vendorPalette`, one vendor one colour, as the rounds
 * list and the reviewer cards keep it. That rule belongs to the review gate, where the question
 * being answered is "whose model is this"; here the question is "what does this button do to my
 * question", and the two colours that already answer it are the ones in the text. An edge rather
 * than a filled box, for the reason the reviewer cards gave: a wall of filled blocks is harder to
 * read than the text in it.</p>
 */
export function chatPresetRowsHtml(
  prompts: readonly PromptPreset[],
  models: readonly ModelPreset[],
  chosenPrompt = '',
  chosenModel = '',
): string {
  if (prompts.length === 0 && models.length === 0) {
    return '';
  }
  // One vendor, one colour — and the vendor is the RUNTIME now, which is what that rule always
  // meant. It used to key on the reviewer row, so two rows of one vendor wore two colours.
  const promptRow = prompts.length === 0
    ? ''
    : `<div class="presets prompts">${prompts
      .map((preset) =>
        `<button type="button" class="preset prompt${preset.id === chosenPrompt ? ' on' : ''}"`
        + ` data-prompt-preset="${escapeHtml(preset.id)}"`
        + `${preset.id === chosenPrompt ? ' aria-pressed="true"' : ''}>`
        + `${escapeHtml(preset.name)}</button>`)
      .join('')}</div>`;
  const modelRow = models.length === 0
    ? ''
    : `<div class="presets models">${models
      .map((preset) =>
        `<button type="button" class="preset model${preset.id === chosenModel ? ' on' : ''}"`
        + ` data-model-preset="${escapeHtml(preset.id)}"`
        + `${preset.id === chosenModel ? ' aria-pressed="true"' : ''}`
        + `>${escapeHtml(preset.name)}</button>`)
      .join('')}</div>`;

  return `<div id="presets">${modelRow}${promptRow}</div>`;
}

/**
 * The picture waiting to go with the next question.
 *
 * <p>Rendered only when it really is an image this page may show. The host built this value from
 * what the page sent it, so it is not arbitrary — but a page that renders a `src` it has not looked
 * at is a page that would render `javascript:` the day something else fills that field, and the
 * check costs one call.</p>
 */
function attachedHtml(attached: string): string {
  if (pastedImage(attached) === undefined) {
    return '';
  }

  return `<div class="attachment"><img class="attached" src="${escapeHtml(attached)}" alt="the picture that will go with the next question">`
    + '<button type="button" id="unattach" title="Take the picture off">Remove</button></div>';
}

/** What a capped conversation offers instead of a composer nobody can use. */
export function chatCappedHtml(capped: boolean): string {
  if (!capped) {
    return '';
  }

  return '<div class="capped"><p>This model keeps no conversation, so every follow-up re-sends the whole '
    + 'thread. Three turns is the limit.</p>'
    + '<button type="button" id="restart">Start a new conversation</button> '
    + '<button type="button" id="useLocal">Continue with a local model</button></div>';
}

/** The page's own styles. Its own function so the document below stays readable. */
/**
 * A colour rule per model the page knows about, from the palette every other surface uses.
 *
 * <p>One call to `vendorPalette` answers here, in the rounds list and on the reviewer cards, so a
 * vendor a person has learned is the same colour wherever they meet it. A model that answered once
 * and is no longer offered gets no rule and falls back to the ordinary caption colour — which is
 * honest: the page cannot say what colour a vendor it has never been told about would have.</p>
 */
function modelColours(
  models: readonly ChatModelChoice[],
  messages: readonly ChatMessage[] = [],
): string {
  // Every model this page will SHOW, not only the ones it can still offer. Switching models carries
  // the whole thread across — which is the feature this caption exists for — so a conversation
  // routinely displays a model the picker has moved on from, and building the rules from the picker
  // alone left those answers with a class and no rule. The ids are in the messages already.
  const shown = [...new Set([
    ...models.map((model) => model.id),
    ...messages.flatMap((message) => (message.model === undefined ? [] : [message.model.id])),
  ])].filter((id) => id.length > 0);
  const colour = vendorPalette(shown);

  return shown
    .map((id) => `  .msg .author.${modelClass(id)} { color: ${colour(id)}; opacity: 1; }`)
    .join('\n');
}

function chatStyle(
  uiScale: number,
  textTone: number,
  models: readonly ChatModelChoice[] = [],
  messages: readonly ChatMessage[] = [],
): string {
  // The zoom goes INSIDE the body rule, and that is not a tidiness preference. It used to sit above
  // it, where CSS has no such thing as a declaration: a parser consuming a qualified rule appends
  // every token to the prelude until it meets `{`, and `;` does not end one — so the selector became
  // `font-size: 13px; body`, and the whole body rule was dropped. The page had no margin, no
  // padding, no font and no background of its own for as long as that stood. The help page always
  // did it this way (`helpPage.ts`); this one did not.
  return `  html, body { height: 100%; }
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 0; display: flex; flex-direction: column; overflow: hidden; ${zoomStyle(uiScale)} ${toneStyle(textTone)} }
  /* The conversation scrolls; the page does not. The min-height of 0 is what makes that true: a flex
     child refuses to shrink below its content without it, so the region would never scroll, the
     body would instead, and the composer would leave the screen — the symptom this layout exists
     to end. (The gate raised it, and it is invisible to every test but a reading of this rule.) */
  #scroll { flex: 1 1 auto; min-height: 0; overflow-y: auto; padding: 0 20px; }
  /* Positioned, because the jump control hangs above it. */
  #composer { flex: 0 0 auto; padding: 8px 20px 12px; position: relative; }
  /* OUT OF FLOW, and that is the point rather than the styling. In the flow it would take room in
     the footer, which shrinks the scrolling region, which changes clientHeight - one of the three
     numbers the follow decision is made from. A control that appears BECAUSE a reader was not
     followed must not alter what "at the bottom" means. No display property either, so the hidden
     attribute keeps working. */
  .jump { position: absolute; bottom: calc(100% + 6px); left: 50%; transform: translateX(-50%); font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 12px; padding: 4px 12px; cursor: pointer; box-shadow: 0 2px 6px rgba(0, 0, 0, .35); }
  .jump:hover { background: var(--vscode-button-hoverBackground); }
  .compose { display: flex; gap: 8px; align-items: flex-end; }
  .attachment { display: flex; gap: 8px; align-items: center; margin: 0 0 6px; }
  .attached { max-height: 84px; max-width: 40%; border: 1px solid var(--vscode-panel-border); border-radius: 4px; }
  #unattach { font: inherit; font-size: .9em; color: var(--vscode-foreground); background: none; border: 1px solid var(--vscode-panel-border); border-radius: 3px; padding: 2px 8px; cursor: pointer; }
  /* OUTSIDE the scrolling region, both of them: a question you are reading must not slide away
     while you look for the line it is about, and the button that shows it has to stay reachable.
     Asked for exactly so — "неважно где я, в начале или в конце". */
  header { display: flex; align-items: baseline; gap: 12px; margin: 0; padding: 16px 20px 12px; flex: 0 0 auto; }
  .asked { margin-left: auto; font: inherit; font-size: .9em; color: var(--vscode-foreground); background: var(--vscode-button-secondaryBackground, transparent); border: 1px solid var(--vscode-panel-border); border-radius: 3px; padding: 2px 10px; cursor: pointer; }
  .asked.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border-color: var(--vscode-focusBorder); }
  /* It OPENS: a height and an opacity that take half a second, because a block of text appearing
     under your eyes with no warning is a jolt. Asked for in those words. */
  /* VISIBILITY, not only height and opacity. Those three hide a region from the eye and leave its
     buttons in the tab order and its text on a screen reader — folded away and still reachable, which
     is the worst of both. The visibility property animates discretely: visible the instant it opens, hidden only
     once the fold has finished, which is exactly the behaviour wanted. (CodeRabbit, PR #207.) */
  .asking { flex: 0 0 auto; max-height: 0; opacity: 0; overflow: hidden; padding: 0 20px; visibility: hidden;
            transition: max-height .5s ease, opacity .5s ease, padding .5s ease, visibility .5s ease;
            border-bottom: 2px solid transparent; }
  /* ORANGE, and a colour no other line on this page wears. The panel border it used to use is the
     colour every rule in the editor is, so the boundary between what you asked and what was answered
     read as one more division among many — asked for as *"а то сливается ответами"*. A charts token
     with a hex fallback, like every other colour here, so a theme that redefines the palette moves
     this with it. */
  .asking.open { max-height: 40vh; opacity: 1; padding: 0 20px 10px; visibility: visible;
                 border-bottom-color: var(--vscode-charts-orange, var(--vscode-editorWarning-foreground, #d18616)); }
  .askingHead { display: flex; align-items: center; gap: 6px; opacity: .75; font-size: .85em; }
  /* AFTER the rule above and no weaker than it. The browser's own [hidden] is a bare attribute
     selector, so a class that sets display beats it and the element stays on screen with its hidden
     property set to true — which is exactly what the operator photographed: two dead boxes above a
     sentence explaining there was nothing to step through. */
  .askingHead[hidden] { display: none; }
  .askingHead button { min-width: 24px; padding: 2px 6px; font: inherit; }
  .askedAt { min-width: 4em; text-align: center; }
  .askedText { max-height: calc(40vh - 2.5em); overflow-y: auto; white-space: pre-wrap; overflow-wrap: break-word; }
  h1 { font-size: 1.2em; margin: 0; }
  .passage { border-left: 3px solid var(--vscode-panel-border); padding: 6px 0 6px 12px; margin: 0 0 16px; white-space: pre-wrap; opacity: .85; }
  /* A reading measure. A line that spans a wide tab is a line whose side says nothing, and the
     sides are how the two speakers are told apart at a glance. */
  .msg { margin: 0 0 18px; max-width: 46rem; }
  /* The EDGE is the colour, not a filled box - the decision already shipped for the reviewer cards,
     and for the same reason: a wall of filled blocks is harder to read than the text in it. */
  .msg.you { margin-left: auto; border-right: 3px solid var(--vscode-textLink-foreground); padding-right: 10px; text-align: right; }
  .msg.model { border-left: 3px solid var(--vscode-charts-green, var(--vscode-textLink-foreground)); padding-left: 10px; }
  .msg .who { font-size: .85em; opacity: .7; margin-bottom: 4px; display: flex; align-items: center; gap: 8px; }
  .msg.you .who { justify-content: flex-end; }
  /* The PROSE takes the editor's foreground, and only the prose: the chrome around it - the hint,
     the picker, the captions - belongs to --vscode-foreground, and overriding that on body produces
     a seam between the two rather than a brighter page. (gemini, the plan round.) */
  /* By NAME, not by inheritance: this rule sets a colour of its own, so the tone has to reach it
     through the property or the answers — the text somebody is actually reading — ignore it. */
  .msg .what { color: var(--coai-read); line-height: 1.55; }
  .msg.you .what { white-space: pre-wrap; }
  .msg .what > :first-child { margin-top: 0; }
  .msg .what > :last-child { margin-bottom: 0; }
  .msg .what h1, .msg .what h2, .msg .what h3, .msg .what h4, .msg .what h5, .msg .what h6 { font-size: 1.05em; margin: 1.2em 0 .4em; }
  .msg .what p { margin: 0 0 .7em; }
  .msg .what ul, .msg .what ol { margin: 0 0 .7em; padding-left: 1.6em; }
  .msg .what li { margin: .15em 0; }
  .msg .what code { font-family: var(--vscode-editor-font-family, monospace); font-size: .92em; background: var(--vscode-textCodeBlock-background, rgba(127,127,127,.18)); border-radius: 3px; padding: 0 .3em; }
  /* Its own box, and it scrolls inside it: a long line of code must not widen the page. */
  .msg .what pre { margin: 0 0 .7em; padding: 8px 10px; overflow-x: auto; background: var(--vscode-textCodeBlock-background, rgba(127,127,127,.14)); border-radius: 4px; }
  .msg .what pre code { background: none; padding: 0; }
  .msg .what blockquote { margin: 0 0 .7em; padding-left: 10px; border-left: 2px solid var(--vscode-panel-border); opacity: .9; }
  .msg .what table { border-collapse: collapse; margin: 0 0 .7em; display: block; overflow-x: auto; }
  .msg .what th, .msg .what td { border: 1px solid var(--vscode-panel-border); padding: 3px 8px; text-align: left; }
  .msg .what a.link { color: var(--vscode-textLink-foreground); cursor: pointer; text-decoration: none; }
  .msg .what a.link:hover { color: var(--vscode-textLink-activeForeground); text-decoration: underline; }
  /* Dimmed, not erased. A zero opacity leaves an invisible target in the tab order and hides the
     control entirely from a keyboard, a touch screen and anyone reading with one. Arriving anywhere
     in the answer reveals it, which is what :focus-within is for. (gemini, the code round.) */
  .msg .copy { font: inherit; font-size: .9em; color: var(--vscode-textLink-foreground); background: none; border: none; padding: 0; cursor: pointer; opacity: .55; }
  .msg:hover .copy, .msg:focus-within .copy, .msg .copy:focus { opacity: 1; }
  hr.end { border: none; border-top: 1px solid var(--vscode-panel-border); margin: 14px 0 0; opacity: .55; }
  .empty { opacity: .6; }
  .thinking { opacity: .75; margin: 0 0 12px; }
  #stop { font: inherit; font-size: .9em; color: var(--vscode-textLink-foreground); background: none; border: none; padding: 0 0 0 4px; cursor: pointer; text-decoration: underline; }
  #stop[disabled] { opacity: .5; cursor: default; text-decoration: none; }
  .queued { opacity: .8; font-size: .9em; }
  .failure { border: 1px solid var(--vscode-inputValidation-errorBorder, var(--vscode-panel-border)); border-radius: 4px; padding: 8px 10px; margin: 0 0 12px; }
  .capped { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .capped p { margin: 0 0 8px; }
  #presets { display: flex; flex-direction: column; gap: 4px; margin: 0 0 8px; }
  .presets { display: flex; gap: 6px; flex-wrap: wrap; }
  /* The EDGE is the colour and the background is the theme's own: unobtrusive is the edge, legible
     is the label in the ordinary foreground. A filled box per button would be a wall. */
  .preset { font: inherit; font-size: .9em; color: var(--vscode-foreground); background: var(--vscode-button-secondaryBackground, transparent); border: 1px solid var(--vscode-panel-border); border-left-width: 3px; border-radius: 3px; padding: 2px 8px; cursor: pointer; max-width: 18rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .preset:hover { background: var(--vscode-button-secondaryHoverBackground, var(--vscode-list-hoverBackground)); }
  /* WHICH one is answering, and which words will be sent. A row of buttons that all look alike is a
     row where the one in force is the one you have to remember. */
  .preset.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border-color: var(--vscode-focusBorder); }
  /* The instruction, marked by the COLOUR OF ITS TEXT.
     A textarea cannot colour part of its own text, so the words are drawn by a layer behind it in
     the same metrics and the box gives up drawing them: its own text is transparent and only its
     caret shows. A background block was the first attempt and it made the words unreadable, which
     is the opposite of the point — you mark the instruction so it can be READ apart from the
     passage. Selected text is painted back in, because a selection over invisible text is a
     coloured rectangle with nothing in it. */
  /* The layer is positioned against the box, so it does not stretch across the Send button. */
  .compose { position: relative; }
  .composeBox { position: relative; flex: 1 1 auto; min-width: 0; display: flex; }
  /* The same wrapping as the layer behind it: a word the box breaks in another place is a word
     drawn in another place. */
  #say { overflow-wrap: break-word; color: transparent; caret-color: var(--vscode-input-foreground, var(--vscode-foreground)); background: transparent; position: relative; z-index: 1; }
  #say::selection { color: var(--vscode-input-foreground, var(--vscode-foreground)); background: var(--vscode-editor-selectionBackground); }
  #backdrop {
    position: absolute; inset: 0; box-sizing: border-box; margin: 0; padding: 8px; overflow: hidden;
    pointer-events: none; white-space: pre-wrap; overflow-wrap: break-word;
    color: var(--vscode-input-foreground); font: inherit;
    border: 1px solid transparent; border-radius: 4px;
  }
  /* WHO is answering and WHAT they are being asked: two decisions, made with two different
     buttons, so two colours rather than one undifferentiated block of "instruction". */
  /* WHO is answering and WHAT is being asked. Two colours, named once and used wherever either of
     them appears: the words behind the composer, and the stripe on the button that changes them. */
  body { --coai-role: var(--vscode-charts-purple, #b180d7); --coai-task: var(--vscode-charts-green, #89d185); }
  mark { background: none; font-weight: 600; }
  mark.role { color: var(--coai-role); }
  mark.task { color: var(--coai-task); }
  /* The machinery: underlined and dimmed, so an eye looking for the question steps over it. */
  u.service { text-decoration: underline; text-decoration-style: solid; opacity: .55; }
  /* The stripe names the half. After the pressed-state rule, whose border shorthand would otherwise take the
     left edge with it — a pressed button must still say what it changes. */
  .preset.model { border-left-color: var(--coai-role); }
  .preset.prompt { border-left-color: var(--coai-task); }
  .picker { display: flex; gap: 8px; align-items: center; margin: 0; flex-wrap: wrap; }
  /* Send sits at the end of the line that names the model, rather than beside the box. Asked for:
     the composer is then the full width of the panel, which is where the long text goes. The button
     is OUTSIDE the pickerBox element, whose innerHTML a push replaces - inside it, every state push would
     destroy the control and the listener bound to it once at load. */
  .pickerRow { display: flex; gap: 8px; align-items: center; margin: 0 0 8px; flex-wrap: wrap; }
  .pickerRow #pickerBox { flex: 1 1 auto; min-width: 0; }
  .pickerRow #send { margin-left: auto; }
  .picker select { max-width: 45%; }
  .refused { font-size: .85em; opacity: .75; margin: 0 0 8px; }
  .caption { font-size: .85em; opacity: .7; }
  .spend { font-size: .85em; opacity: .7; margin-left: auto; padding-left: 8px; white-space: nowrap; }
  /* The 30 % lives HERE and only here, so the CSS path and the JavaScript fallback below cannot
     disagree about where the ceiling is: the fallback sets a height and this caps it. field-sizing
     is Chromium-only, which is not a limitation in a page that renders nowhere but VS Code's own
     webview - but the manifest declares support back to 1.85, whose engine has never heard of it,
     and there the fallback does the growing. */
  textarea { flex: 1 1 auto; min-width: 0; box-sizing: border-box; min-height: 64px; max-height: 30vh; field-sizing: content; overflow-y: auto; resize: none; font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 4px; padding: 8px; }
  textarea[disabled] { opacity: .6; }
  #send { flex: 0 0 auto; font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 4px; padding: 8px 14px; cursor: pointer; }
  #send:hover:not([disabled]) { background: var(--vscode-button-hoverBackground); }
  #send[disabled] { opacity: .6; cursor: default; }
  .hint { font-size: .85em; opacity: .6; margin-top: 4px; }
${modelColours(models, messages)}
${ZOOM_CSS}
${TONE_CSS}`;
}

/**
 * The line shown while a turn is in flight, and where in a queue it is.
 *
 * <p>"Thinking…" is the whole truth for a local CLI: the process is running and it is thinking. It
 * is a guess for a Team server, where the honest answer for most of the wait is that somebody else's
 * round has the vendor. A shared server queues twenty deep per person by design, so an unchanging
 * spinner for minutes is the one shape a busy server and a broken tab look identical in — and the
 * position was already on the wire. (gemini, the code round.)</p>
 *
 * <p>`position` is 0 when the server did not say, or when the turn has left the queue and is being
 * answered. Both are "no number to show", and neither is position zero.</p>
 */
export function chatStatusHtml(running: boolean, position: number, turn: number): string {
  if (!running) {
    return '';
  }
  const where = Number.isInteger(position) && position > 0
    ? ` <span class="queued">· waiting in the queue, ${position} ahead</span>`
    : '';
  // No number, no button. The alternative is a control that posts a message the host will drop,
  // which looks to a person exactly like a stop that did not work.
  const stop = Number.isSafeInteger(turn) && turn > 0
    ? ` <button type="button" id="stop" data-turn="${turn}">Stop</button>`
    : '';

  return `<p class="thinking">Thinking…${where}${stop}</p>`;
}

/**
 * What each pushed region holds, for the page as it is first rendered.
 *
 * <p>One function because two callers need the SAME four strings: the markup writes them, and the
 * script remembers them as "what is already on screen" so that the first pushed state can tell a
 * change from a repeat. Two copies of these expressions would drift on the day one of them gains a
 * condition, and the symptom would be a page that scrolls on a push that changed nothing.</p>
 */
type Regions = Record<'messages' | 'thinking' | 'capped' | 'failure', string>;

function regionsOf(state: ChatPageState): Regions {
  return {
    messages: chatMessagesHtml(state.messages, state.marks),
    thinking: chatStatusHtml(state.running, 0, state.turn),
    capped: chatCappedHtml(state.capped),
    failure: state.failure.length === 0 ? '' : `<div class="failure">${escapeHtml(state.failure)}</div>`,
  };
}

/** The document, without its head or its script. */
function chatBody(state: ChatPageState, regions: Regions): string {
  const locked = state.running || state.capped;

  return `<header>
<h1>${escapeHtml(state.title)}</h1>${zoomControlHtml(state.uiScale)}${toneControlHtml(state.textTone)}
${state.fromSession ? '<button type="button" id="asked" class="asked" aria-expanded="false" aria-controls="asking" title="What you asked in this session, read back from disk">Asked</button>' : ''}
</header>
${state.fromSession ? `<section id="asking" class="asking" aria-live="polite" aria-hidden="true">
<div class="askingHead" id="askingHead">
  <button type="button" id="askedBack" aria-label="The one before">&lsaquo;</button>
  <span id="askedAt" class="askedAt"></span>
  <button type="button" id="askedNext" aria-label="The next one">&rsaquo;</button>
</div>
<div id="askedText" class="askedText"></div>
</section>` : ''}
<main id="scroll">
<div class="passage" id="passage">${escapeHtml(state.passage)}</div>
<div id="failure">${regions.failure}</div>
<div id="messages">${regions.messages}</div>
<div id="thinking">${regions.thinking}</div>
<div id="capped">${regions.capped}</div>
</main>
<footer id="composer">
<button type="button" id="jump" class="jump" hidden>Jump to newest ↓</button>
${chatPresetRowsHtml(state.promptPresets, state.modelPresets, state.promptId, state.chosenModelId)}
<div class="pickerRow">
<div id="pickerBox">${chatPickerHtml({ providers: state.providers, refused: [] }, state.providerId, state.modelId)}</div>
<span id="spend" class="spend" title="What this conversation has cost so far. A turn carries the whole conversation, so each question is billed for the ones before it.">${escapeHtml(state.spend)}</span>
<button type="button" id="send"${locked ? ' disabled' : ''}>${state.reask.length > 0 ? `Re-ask · ${escapeHtml(state.reask)}` : 'Send'}</button>
</div>
${attachedHtml(state.attached)}
<div class="compose">
<div class="composeBox">
<div id="backdrop" aria-hidden="true"></div>
<textarea id="say" rows="3" placeholder="Ask about the text above…"${locked ? ' disabled' : ''}>${escapeHtml(state.draft)}</textarea>
</div>
</div>
<div class="hint">Enter sends · Shift+Enter for a new line</div>
</footer>`;
}

/**
 * How close to the bottom still counts as being AT the bottom, in pixels.
 *
 * <p>The operator's rule says "a line or two of slack"; the gate asked for a number, and it is right
 * to — a boundary a reader can land on is a boundary a test has to name. 48 is about two lines at
 * the base size and three at the largest zoom step down. It is a constant rather than something
 * derived from the font size on purpose: a reader a hair short of the bottom considers themselves at
 * the bottom whatever size they are reading at, and a slack that shrinks with the text would make
 * the rule behave differently for the people most likely to have scrolled.</p>
 */
export const FOLLOW_SLACK_PX = 48;

/**
 * Was the reader at the bottom — and therefore should a new answer be scrolled to?
 *
 * <p>Rule 2 of the decided scroll behaviour (entry 23, settled 2026-09-09). Pure, and taking its
 * slack as a parameter rather than reading the constant, because its SOURCE is embedded into the
 * page: it must reference nothing outside itself or the page gets a function whose free variable
 * does not exist there.</p>
 *
 * <p>Written as `!(distance > slack)` rather than `distance <= slack` for the case that reaches it
 * most rarely and matters anyway: a page that cannot measure itself yields NaN, every comparison
 * with NaN is false, and the negation turns that into "follow". A page whose numbers are unreadable
 * is a page whose reader has not scrolled away from anything.</p>
 */
export function shouldFollow(
  scrollTop: number,
  clientHeight: number,
  scrollHeight: number,
  slack: number,
): boolean {
  return !(scrollHeight - (scrollTop + clientHeight) > slack);
}

/** The page's behaviour. Its own function for the same reason the styles are. */
function chatScript(state: ChatPageState, regions: Regions): string {
  return `(function () {
  const vscode = acquireVsCodeApi();
  // The one thing VS Code keeps about this page across a window reload, and it hands it back to
  // deserializeWebviewPanel. It is how a restored tab says which conversation it is — a title could
  // not, because two Claude Code sessions can share one.
  vscode.setState(${jsonForScript({ id: state.id })});
  let captions = ${jsonForScript(Object.fromEntries(state.models.map((model) => [model.id, model.caption])))};
  // Which words in the turn are which, so the layer behind the box can draw them apart.
  let marks = ${jsonForScript(state.marks)};
  // What the person wrote in the session this conversation came from, once the host has read it.
  let asked = ${jsonForScript(state.asked ?? [])};
  let askedAt = 0;
  // Why there is nothing, when there is nothing. Empty until the host has answered at all.
  let askedWhy = '';
  // The newest answer this page has taken. A read started by an earlier press can finish after one
  // started by a later press, and landing second it would replace what was just asked for.
  let askedSeen = 0;
  // The frame a paint is waiting for, declared HERE because the first paint runs at load - before
  // the painter's own place in this script - and a let in the temporal dead zone throws.
  let painting = 0;
  // The webview swallows a thrown error silently, and a page that stops responding to Enter with no
  // sign of why is the defect this trap exists to name. Same shape as the rounds log's.
  window.onerror = function (message) {
    const box = document.getElementById('failure');
    if (box) { box.textContent = 'The page hit an error: ' + message; }
    // And TELL the host. A trap that only writes into the page leaves the extension believing the
    // conversation is fine while the tab has stopped answering Enter.
    vscode.postMessage({ type: 'pageError', message: String(message) });
  };
  ${zoomScript()}
  ${toneScript()}
  // Bound by ASSIGNMENT, not declared: the minifier renames a declaration and leaves the name in the
  // template string that calls it, which is how the rounds log shipped a dead page twice. Embedding
  // the source is also what makes the function the tests exercise the function the page runs.
  var shouldFollow = ${shouldFollow.toString()};
  // The SAME marking the transcript above is rendered with. Embedded rather than written twice: two
  // sets of rules about the same words would show one thing in the box and another in the message
  // it becomes a second later.
  var turnParts = ${turnParts.toString()};
  var SLACK = ${FOLLOW_SLACK_PX};
  var pendingFollow = 0;
  // The ONE turn a stop has been asked for and not yet answered. The thinking line is replaced
  // wholesale on every push - a Team server pushes its queue position while a turn waits - and the
  // replacement carried a fresh, pressable control for a turn the person had already stopped. They
  // would press it again and watch it come back. It is one number, not a set, because there is one
  // turn in flight; the next turn's control is live, and inheriting a stop nobody asked for would be
  // the opposite defect. (gemini, the plan round.)
  var stopAsked = 0;
  // Whether the reader was at the bottom BEFORE the last thing that moved the layout under them.
  // Recomputed on every scroll, which is the only event that means the READER moved; a resize or a
  // growing composer must ask what this remembers, because after one the numbers no longer describe
  // where the person put themselves.
  var wasAtBottom = true;
  // A follow is out and has not run yet. If the reader cancels it, they are left scrolled up with an
  // answer nobody took them to - so the cancel hands them the jump control instead.
  var followOutstanding = false;
  // What was last PUT into each region, so a repeat can be told from a change without asking the
  // browser to serialise the DOM back to us on every push. Seeded with what the MARKUP holds, or the
  // first push would compare against nothing, read as a change, and scroll a reader for content that
  // was already on their screen.
  var lastWritten = ${jsonForScript(regions)};
  // The ONE scroll event the page owes itself, armed by its own write and consumed by the event it
  // causes. Two shapes were tried and both were wrong: a flag cleared on a later frame stays raised
  // until that frame comes, so every scroll in between reads as the page's own; and a position kept
  // indefinitely turns the bottom into a magic pixel - a reader who scrolls away and comes back to
  // it, which is what "scroll back down" IS, was still being read as the page for the rest of the
  // tab's life. Armed only when the write actually moved anything, or nothing would consume it.
  var selfScrollTo = null;
  // A scroll set in the same tick as the write scrolls to a height the browser has not laid out yet
  // and lands short. The next frame has the real height, and setTimeout is the fallback for a host
  // that has no requestAnimationFrame - the bundled page runs in exactly such a stub.
  function afterLayout(fn) {
    if (typeof requestAnimationFrame === 'function') { requestAnimationFrame(fn); }
    else if (typeof setTimeout === 'function') { setTimeout(fn, 0); }
  }
  // THE TWO ENTRY POINTS for moving this page, and the contract stories 3 and 4 are held to: the
  // jump control and the composer's resize go through scheduleFollow or landOnNewest, never through
  // scrollTop of their own. One place decides, or the rule is four rules that agree by accident.
  function landOnNewest() {
    const scroll = document.getElementById('scroll');
    if (!scroll) { return; }
    // Our own write fires a scroll event. Without this flag the page would cancel its own next
    // follow, and the rule would work exactly once per tab.
    const was = scroll.scrollTop;
    scroll.scrollTop = scroll.scrollHeight;
    // Whoever brought them here, they are here: the control has nothing left to offer. Retiring it
    // in the click handler alone left it standing for every other path that lands on the newest.
    offerJump(false);
    // What it CLAMPED to, not what we asked for: a browser answers scrollHeight - clientHeight. And
    // null when nothing moved, because then no scroll event is coming to consume the arming.
    selfScrollTo = scroll.scrollTop === was ? null : scroll.scrollTop;
    // Known NOW rather than sampled next frame. A run of keystrokes calls the re-pin several times
    // before any frame arrives, and each one was reading a flag that had not been updated yet.
    wasAtBottom = true;
  }
  // Deferred to the next frame, and CANCELLABLE. The reader can move between the frame being asked
  // for and the frame arriving - a drag, a wheel, Page Up - and a scroll that was right when it was
  // scheduled is a hijack by the time it runs. Re-measuring in the callback cannot tell: the content
  // is already in, so a reader who WAS at the bottom now measures short of it. Only the reader knows
  // they moved, so the cancel is their scroll event. (Two vendors raised this on the plan round.)
  // ASKED, not assumed. The gate's blocking finding on the plan: a page that takes field-sizing on
  // faith works on the machine it was written on and silently never grows anywhere else, which is
  // the one failure mode a person cannot report because nothing happens.
  var sizesItself = typeof CSS !== 'undefined' && typeof CSS.supports === 'function'
    && CSS.supports('field-sizing', 'content');
  // Height from content, ceiling from the CSS. Called on input, after a send has emptied the box and
  // after a draft is pushed into it - shrinking back is a call, not a hope.
  function fitComposer() {
    if (sizesItself) { return; }
    const box = document.getElementById('say');
    if (!box || !box.style) { return; }
    box.style.height = 'auto';
    box.style.height = box.scrollHeight + 'px';
    // Only where nothing else is watching. On a host WITH ResizeObserver this same height change
    // reaches the observer too, and both firing means two re-pins and two queued samplings racing
    // over one event. The engines without field-sizing are the ones this fallback exists for, and
    // requirement 8 was promised to them as well. (gemini, the code round.)
    if (typeof ResizeObserver !== 'function') { repinIfTheyWereAtTheBottom(); }
  }
  // A height change nobody asked for must not move the conversation out from under a reader. The
  // remembered flag is right here where re-measuring is not: they did NOT scroll, the region shrank
  // underneath them, so measuring now would call them scrolled away and leave the last answer behind
  // the box they are typing into.
  function repinIfTheyWereAtTheBottom() {
    if (wasAtBottom) {
      landOnNewest();

      return;
    }
    // Not at the bottom, so nothing moves - but the resize changed the numbers under them, and the
    // next frame is when they can be read again.
    afterLayout(rememberWhereTheyAre);
  }
  function rememberWhereTheyAre() {
    const scroll = document.getElementById('scroll');
    if (scroll) {
      wasAtBottom = shouldFollow(scroll.scrollTop, scroll.clientHeight, scroll.scrollHeight, SLACK);
    }
  }
  function offerJump(show) {
    const control = document.getElementById('jump');
    if (control) { control.hidden = !show; }
  }
  function scheduleFollow() {
    const token = ++pendingFollow;
    followOutstanding = true;
    afterLayout(function () {
      if (token === pendingFollow) {
        landOnNewest();
        followOutstanding = false;
      }
    });
  }
  // Set by the host with every state, and read by send() below: an empty box means "ask the other
  // model the same thing" only while there IS another model to ask.
  var canReask = ${jsonForScript(state.reask.length > 0)};
  function send() {
    const box = document.getElementById('say');
    if (!box || box.disabled) { return; }
    const text = box.value.trim();
    if (text.length === 0) {
      // The whole of entry 24. Text in the box is a question and must never be swallowed by this;
      // an empty box was refused before this existed and is refused still when there is nothing to
      // re-ask, so nothing that used to work has changed meaning.
      if (canReask) {
        vscode.postMessage({ type: 'command', command: 'reask' });
        lock(true);
      }

      return;
    }
    box.value = '';
    vscode.postMessage({ type: 'command', command: 'send', text: text });
    fitComposer();
    paintBackdrop();
    // Locked HERE, not when the host gets round to saying so. Between the post and the state that
    // comes back there was a window - small, and the width of a second Enter - in which a second
    // turn went down a pipe that carries one. Two vendors found it independently; the page did not
    // need telling that it had just sent something.
    lock(true);
    // And no focus call: focusing a control you have just disabled is how a caret ends up in a box
    // nobody can type in. The unlock below already hands focus back when the turn ends, whichever
    // way it went, and one place deciding that is the point.
  }
  // The one place either control's lock is written. Two controls deciding the same thing from the
  // same inputs is two chances to disagree, and the one that disagrees is the one that sends.
  function lock(locked) {
    const box = document.getElementById('say');
    const button = document.getElementById('send');
    if (box) { box.disabled = locked; }
    if (button) { button.disabled = locked; }
  }
  const say = document.getElementById('say');
  if (say) {
    say.addEventListener('keydown', function (event) {
      if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); send(); }
    });
    // PAINTED, not only fitted. The box draws its own text transparent and the layer behind it does
    // the drawing, so anything that changes the text and does not repaint makes those words VANISH —
    // which is what typing did, and what a tab opened from a capture did with the whole opening turn
    // in it. The operator sent a screenshot of an empty-looking composer that was not empty.
    say.addEventListener('input', function () { fitComposer(); paintBackdrop(); });
    // The two layers scroll as one or the words drift apart from the caret that is on them.
    say.addEventListener('scroll', function () {
      const layer = document.getElementById('backdrop');
      if (layer) { layer.scrollTop = say.scrollTop; layer.scrollLeft = say.scrollLeft; }
    });
    // THE FIRST PAINT. The draft is rendered into the box by the HTML, which fires no event.
    paintBackdrop();
    // A picture from the clipboard. The page reads the bytes because only the page has a clipboard
    // event; it writes nothing to disk, because it cannot - the HOST holds the file, which is what
    // the vendor process will open. Anything that is not an image pastes as the text it is.
    say.addEventListener('paste', function (event) {
      const items = event.clipboardData && event.clipboardData.items;
      if (!items) { return; }
      for (let index = 0; index < items.length; index += 1) {
        const item = items[index];
        if (!item || item.kind !== 'file' || typeof item.type !== 'string' || item.type.indexOf('image/') !== 0) {
          continue;
        }
        const file = typeof item.getAsFile === 'function' ? item.getAsFile() : null;
        if (!file) { continue; }
        event.preventDefault();
        if (typeof FileReader !== 'function') {
          vscode.postMessage({ type: 'command', command: 'pageError', message: 'this editor cannot read a pasted picture' });
          return;
        }
        const reader = new FileReader();
        reader.onload = function () {
          vscode.postMessage({ type: 'command', command: 'attach', data: String(reader.result || '') });
        };
        reader.readAsDataURL(file);

        return;
      }
    });
  }
  // The second caller of the ONE send. Attached here rather than as an onclick attribute: the page's
  // CSP is script-src 'nonce-...', so an inline handler is not merely untidy, it is a dead button.
  const sendButton = document.getElementById('send');
  if (sendButton) {
    sendButton.addEventListener('click', function () { send(); });
  }
  // The words the model is being instructed with, marked behind the box by the COLOUR OF THEIR
  // TEXT: the role in one colour, the task in another. Which is which comes from the host - the page
  // holds one string, and a role and a task read alike - and a question typed from scratch has
  // neither, which is why nothing is marked then.
  // ONE PAINT PER FRAME. Every keystroke asks for one, and a keystroke can arrive faster than the
  // browser draws: coalesced here rather than run per event, because the work is a full rebuild of a
  // layer that can hold a whole captured passage. (gemini and local, the code round.)
  function paintBackdrop() {
    if (painting !== 0) { return; }
    const frame = typeof requestAnimationFrame === 'function'
      ? requestAnimationFrame
      : function (run) { return setTimeout(run, 0); };
    painting = frame(function () { painting = 0; paintNow(); }) || 1;
  }
  function paintNow() {
    const box = document.getElementById('say');
    const behind = document.getElementById('backdrop');
    if (!box || !behind) { return; }
    const escape = function (t) {
      return t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    };
    // The tag is CHOSEN from what the part is, never built out of it. Nothing here can reach an
    // attribute even if a future part kind arrives from somewhere less trustworthy than this page's
    // own function. (gemini, the code round, as a security finding.)
    const opens = { role: '<mark class="role">', task: '<mark class="task">', service: '<u class="service">' };
    const closes = { role: '</mark>', task: '</mark>', service: '</u>' };
    const parts = turnParts(box.value || '', marks);
    let html = '';
    for (let index = 0; index < parts.length; index += 1) {
      const part = parts[index];
      const body = escape(part.text);
      html += opens[part.kind] === undefined ? body : opens[part.kind] + body + closes[part.kind];
    }
    behind.innerHTML = html;
    // BOTH AXES. A word longer than the box scrolls the textarea sideways, and a layer that only
    // follows the vertical scroll then draws the words in the wrong place. (gemini, the code round.)
    behind.scrollTop = box.scrollTop;
    behind.scrollLeft = box.scrollLeft;
  }
  // WHAT YOU ASKED, from the session file this conversation came from. The page holds no history
  // of its own — that window may be four hours old and its first line long gone from the screen.
  function paintAsked() {
    const box = document.getElementById('askedText');
    const at = document.getElementById('askedAt');
    if (!box || !at) { return; }
    const count = asked.length;
    const back = document.getElementById('askedBack');
    const next = document.getElementById('askedNext');
    const head = document.getElementById('askingHead');
    // ONE turn needs no arrows, and none needs no row at all. Disabled is not absent: on the
    // operator's own screenshot the two of them sat above a reason as a pair of empty boxes, which
    // reads as something broken rather than as the answer it was.
    if (head) { head.hidden = count < 2; }
    if (count === 0) {
      box.textContent = askedWhy.length > 0 ? askedWhy : 'Reading the session…';
      at.textContent = '';
      if (back) { back.disabled = true; }
      if (next) { next.disabled = true; }

      return;
    }
    if (askedAt >= count) { askedAt = count - 1; }
    if (askedAt < 0) { askedAt = 0; }
    box.textContent = asked[askedAt];
    at.textContent = (askedAt + 1) + ' / ' + count;
    if (back) { back.disabled = askedAt === 0; }
    if (next) { next.disabled = askedAt >= count - 1; }
  }
  function showAsked(open) {
    const region = document.getElementById('asking');
    const button = document.getElementById('asked');
    if (!region) { return; }
    region.classList.toggle('open', open);
    // Said out loud, not merely drawn: a person on a screen reader is told the button is expanded
    // and the region is there, and while it is folded the region is not announced at all.
    region.setAttribute('aria-hidden', open ? 'false' : 'true');
    if (button) {
      button.classList.toggle('on', open);
      button.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
  }
  const askedButton = document.getElementById('asked');
  if (askedButton) {
    askedButton.addEventListener('click', () => {
      const region = document.getElementById('asking');
      const open = region ? !region.classList.contains('open') : true;
      showAsked(open);
      // EVERY time it is opened, not only the first. The window this exists for is four hours old
      // and still being typed into: a list read once and kept would be missing everything said since,
      // which is most of what a person wants when they press it a second time. (gemini, the plan
      // round.) Never on LOAD, though — a session file is somebody else's and megabytes long, and
      // opening a tab is not a reason to read one.
      if (open) {
        vscode.postMessage({ type: 'showAsked' });
      }
      paintAsked();
    });
  }
  for (const step of [['askedBack', -1], ['askedNext', 1]]) {
    const button = document.getElementById(step[0]);
    if (button) {
      button.addEventListener('click', () => {
        askedAt += step[1];
        paintAsked();
      });
    }
  }

  function wirePicker() {
    const provider = document.getElementById('provider');
    const model = document.getElementById('model');
    // BOTH halves, whichever one moved. A message carrying only what changed would leave the host
    // pairing it with whatever it last heard, and the two can disagree — the model list belongs to a
    // provider, so changing the provider invalidates the model that was showing.
    function pick(providerId, modelId) {
      const caption = document.getElementById('caption');
      if (caption) { caption.textContent = captions[providerId] || ''; }
      // WITH the composer, like the buttons above. Choosing a model in the dropdown is the same
      // decision the button makes, so the role in the box follows it the same way - and the host can
      // only swap a half it can see. (codex, the second code round.)
      const box = document.getElementById('say');
      vscode.postMessage({
        type: 'command', command: 'pick', provider: providerId, model: modelId,
        text: box ? box.value : '',
      });
    }
    if (provider) {
      // A provider changed: the model is not carried across. Which of the new row's models answers is
      // the host's to decide - it holds the catalog - and sending the old one would name a model that
      // belongs to somebody else.
      provider.addEventListener('change', function () { pick(provider.value, ''); });
    }
    if (model) {
      model.addEventListener('change', function () { pick(provider ? provider.value : '', model.value); });
    }
  }
  function wireCapped() {
    const restart = document.getElementById('restart');
    if (restart) {
      restart.addEventListener('click', function () {
        vscode.postMessage({ type: 'command', command: 'restart' });
      });
    }
    const useLocal = document.getElementById('useLocal');
    if (useLocal) {
      useLocal.addEventListener('click', function () {
        vscode.postMessage({ type: 'command', command: 'useLocal' });
      });
    }
  }
  wirePicker();
  wireCapped();
  // Rule 1: the tab opens looking at the last thing said, not at the passage above it. Three times,
  // because the height is not final until the fonts are: now (the first paint is already close),
  // after layout, and after the fonts settle where the host reports them. Idempotent, so the two
  // extra calls cost nothing on a page that was already there.
  landOnNewest();
  scheduleFollow();
  if (document.fonts && document.fonts.ready && typeof document.fonts.ready.then === 'function') {
    // The correction belongs to the OPENING follow and dies with it. Scheduling unconditionally
    // here made a fresh token, so a cancelled open came back to life whenever the fonts happened to
    // settle - a reader who opened the tab and started reading upward was dragged down by a font.
    // Three findings from one vendor said this, and they were right.
    const openedAt = pendingFollow;
    document.fonts.ready.then(function () {
      if (pendingFollow === openedAt) { scheduleFollow(); }
    });
  }
  const scrollRegion = document.getElementById('scroll');
  if (scrollRegion) {
    scrollRegion.addEventListener('scroll', function () {
      if (selfScrollTo !== null && scrollRegion.scrollTop === selfScrollTo) {
        selfScrollTo = null;
      } else {
        pendingFollow++;
        // They cancelled a follow that had not run. An answer arrived, they were not taken to it,
        // and without this nothing on the page would say so. (codex, the plan round.)
        if (followOutstanding) {
          followOutstanding = false;
          offerJump(true);
        }
      }
      rememberWhereTheyAre();
      // A reader who came back under their own steam has answered the question the control was
      // asking. One that stays up after that is one nobody trusts.
      if (shouldFollow(scrollRegion.scrollTop, scrollRegion.clientHeight, scrollRegion.scrollHeight, SLACK)) {
        offerJump(false);
      }
    });
  }
  // A footer whose height changes takes the room from the conversation above it. A reader who was at
  // the bottom did NOT scroll - the region shrank underneath them - so re-measuring would call them
  // scrolled away and leave the last answer behind the box they are typing into: rule 2 broken from
  // the other side. What they WERE is the only measurement that survives the resize, so it is taken
  // before the layout changes and acted on after. A host without ResizeObserver keeps today's
  // behaviour rather than breaking.
  // Where the host has one, it catches every way the composer's height can change, including the
  // CSS path where no script of ours runs at all. Where it has none, fitComposer says so instead -
  // and those are the same hosts, since an engine without field-sizing is an old engine.
  if (typeof ResizeObserver === 'function') {
    const composer = document.getElementById('composer');
    if (composer) { new ResizeObserver(repinIfTheyWereAtTheBottom).observe(composer); }
  }
  // Delegated, because the answers are replaced wholesale on every push: a listener bound to each
  // link would have to be re-bound after every one, and the one that was missed is the one that
  // silently does nothing. The page never navigates and never reads a file - it names what it wants
  // and the HOST decides, which is where the workspace root and the scheme are actually known.
  // Delegated on the region that HOLDS the line rather than bound to the button, because the line is
  // replaced wholesale on every push and a listener bound to the old button dies with it.
  const thinkingRegion = document.getElementById('thinking');
  if (thinkingRegion) {
    thinkingRegion.addEventListener('click', function (event) {
      const target = event.target;
      if (!target || typeof target.closest !== 'function') { return; }
      const control = target.closest('#stop');
      if (!control || !control.dataset || control.disabled) { return; }
      const turn = Number(control.dataset.turn);
      if (!Number.isSafeInteger(turn) || turn <= 0) { return; }
      // Disabled the instant it is pressed. A second press a moment later would name the same turn,
      // and by the time it landed the host could have moved on to the next one.
      control.disabled = true;
      // Said, not merely greyed. Killing a vendor process and resolving the turn takes a moment, and
      // a dimmed control still reading Stop cannot be told from one that did nothing at all.
      control.textContent = 'Stopping…';
      stopAsked = turn;
      vscode.postMessage({ type: 'command', command: 'stop', turn: turn });
    });
  }
  const messagesRegion = document.getElementById('messages');
  if (messagesRegion) {
    messagesRegion.addEventListener('click', function (event) {
      const target = event.target;
      if (!target || typeof target.closest !== 'function') { return; }
      const acted = target.closest('[data-open], [data-file], [data-copy]');
      if (!acted || !acted.dataset) { return; }
      if (typeof acted.dataset.open === 'string') {
        vscode.postMessage({ type: 'command', command: 'openLink', url: acted.dataset.open });
      } else if (typeof acted.dataset.file === 'string') {
        vscode.postMessage({
          type: 'command', command: 'openLink',
          file: acted.dataset.file, line: Number(acted.dataset.line || 0),
        });
      } else if (typeof acted.dataset.copy === 'string') {
        vscode.postMessage({ type: 'command', command: 'copyAnswer', index: Number(acted.dataset.copy) });
      }
    });
  }
  // Delegated on the row, because a push replaces both rows whenever the saved lists change.
  const presetRows = document.getElementById('presets');
  if (presetRows) {
    presetRows.addEventListener('click', function (event) {
      const target = event.target;
      if (!target || typeof target.closest !== 'function') { return; }
      const pressed = target.closest('[data-prompt-preset], [data-model-preset]');
      if (!pressed || !pressed.dataset) { return; }
      // The page NAMES what was chosen and does nothing else. What a prompt preset does to the
      // composer, and what a model preset does to the conversation, are the host's to decide - it
      // holds the lists, and a page acting on its own copy would be a second place they can drift.
      // WITH WHAT IS IN THE COMPOSER, whichever button it was. Each preset changes one half of the
      // instruction at the front of that text, and the host can only swap a half it can see - the
      // box is the only place that knows what is really in it.
      const box = document.getElementById('say');
      const text = box ? box.value : '';
      if (typeof pressed.dataset.promptPreset === 'string') {
        vscode.postMessage({
          type: 'command', command: 'usePromptPreset', id: pressed.dataset.promptPreset, text: text,
        });
      } else if (typeof pressed.dataset.modelPreset === 'string') {
        vscode.postMessage({
          type: 'command', command: 'useModelPreset', id: pressed.dataset.modelPreset, text: text,
        });
      }
    });
  }
  const unattach = document.getElementById('unattach');
  if (unattach) {
    unattach.addEventListener('click', function () {
      vscode.postMessage({ type: 'command', command: 'unattach' });
    });
  }
  const jump = document.getElementById('jump');
  if (jump) {
    jump.addEventListener('click', function () {
      landOnNewest();
      offerJump(false);
      // The box, unless a turn is running - focusing a disabled control puts the caret where
      // nobody can type, which is the same rule send() follows.
      const box = document.getElementById('say');
      if (box && !box.disabled && typeof box.focus === 'function') { box.focus(); }
    });
  }
  window.addEventListener('message', function (event) {
    const data = event.data || {};
    // Its OWN message, not a thin state push. This handler treats a capped notice and a failure line
    // as gone when a state message does not mention them, so answering "what did I ask" through the
    // state channel would silently clear both.
    if (data.type === 'asked') {
      const at = typeof data.at === 'number' ? data.at : 0;
      if (at < askedSeen) {
        return;
      }
      askedSeen = at;
      const before = askedAt;
      const had = asked.length;
      asked = Array.isArray(data.asked) ? data.asked : [];
      askedWhy = typeof data.refusal === 'string' ? data.refusal : '';
      // WHERE THEY WERE, when a re-read simply found more. Sending them back to the first turn every
      // time the region is opened would undo the arrows they just pressed; a list that changed under
      // them is a different matter and starts again.
      askedAt = had > 0 && asked.length >= had ? before : 0;
      paintAsked();

      return;
    }
    if (data.type !== 'state') { return; }
    // BEFORE any write. Read after one, scrollHeight already includes what just arrived, so a reader
    // who was at the bottom measures as a screen short of it and is never followed - rule 2 turns
    // silently into "never follow", and the only place that shows is the real webview. One snapshot
    // at the head of the one handler every region arrives through is what makes it true by
    // construction rather than by remembering to do it in five places.
    const scroll = document.getElementById('scroll');
    const follow = !scroll || shouldFollow(scroll.scrollTop, scroll.clientHeight, scroll.scrollHeight, SLACK);
    let wrote = false;
    const messages = document.getElementById('messages');
    const thinking = document.getElementById('thinking');
    const capped = document.getElementById('capped');
    const failure = document.getElementById('failure');
    // A CHANGE, not an assignment. A retry, or a poll that pushes the state again, assigns the same
    // html - and counting that as an insertion scrolls a reader for content already in front of
    // them. Compared against what we LAST WROTE rather than against the element: reading innerHTML
    // back makes the browser serialise the whole subtree on every push, and what comes back is
    // normalised - attributes reordered, entities decoded - so an unchanged push can read as
    // different and a changed one as the same.
    if (messages && typeof data.messagesHtml === 'string' && lastWritten.messages !== data.messagesHtml) {
      messages.innerHTML = data.messagesHtml;
      lastWritten.messages = data.messagesHtml;
      wrote = true;
    }
    if (thinking && typeof data.thinkingHtml === 'string' && lastWritten.thinking !== data.thinkingHtml) {
      // Whether the keyboard was on the control this is about to destroy. Replacing the line while a
      // remote turn's queue position moves would otherwise take the focus away repeatedly, silently,
      // from somebody waiting - which is exactly when they are most likely to want it.
      const hadFocus = document.activeElement === document.getElementById('stop');
      thinking.innerHTML = data.thinkingHtml;
      lastWritten.thinking = data.thinkingHtml;
      wrote = true;
      const redrawn = document.getElementById('stop');
      if (redrawn) {
        if (Number(redrawn.dataset && redrawn.dataset.turn) === stopAsked) {
          redrawn.disabled = true;
          redrawn.textContent = 'Stopping…';
        }
        // Only onto a control that still has something to do. Putting a keyboard on a disabled
        // button leaves it somewhere with no action left, which is worse than leaving it be.
        if (hadFocus && !redrawn.disabled && typeof redrawn.focus === 'function') { redrawn.focus(); }
      }
    }
    // These two clear when they are NOT mentioned - the protocol has always meant that, and a
    // capped notice or a failure left on screen after it stopped being true is one a person acts on.
    // Requiring a string here was this branch's own regression; the gate caught it.
    const nextCapped = typeof data.cappedHtml === 'string' ? data.cappedHtml : '';
    if (capped && lastWritten.capped !== nextCapped) {
      capped.innerHTML = nextCapped;
      lastWritten.capped = nextCapped;
      wireCapped();
      wrote = true;
    }
    // The picker is re-rendered rather than nudged: after "continue with a local model" the whole
    // list can be different, and a select that only had its value set would show a model it no
    // longer offers.
    const pickerBox = document.getElementById('pickerBox');
    if (pickerBox && typeof data.pickerHtml === 'string') { pickerBox.innerHTML = data.pickerHtml; wirePicker(); }
    // The container stays, so the delegated click listener on it survives the swap.
    const rows = document.getElementById('presets');
    if (rows && typeof data.presetsHtml === 'string') { rows.innerHTML = data.presetsHtml; }
    // WHICH words are the instruction, before any draft below is applied - so the paint that follows
    // colours the new halves rather than the ones a previous model put there.
    if (data.marks && typeof data.marks === 'object') {
      marks = data.marks;
      paintBackdrop();
    }
    const nextFailure = typeof data.failureHtml === 'string' ? data.failureHtml : '';
    if (failure && lastWritten.failure !== nextFailure) {
      failure.innerHTML = nextFailure;
      lastWritten.failure = nextFailure;
      wrote = true;
    }
    const passage = document.getElementById('passage');
    if (passage && typeof data.passage === 'string' && passage.textContent !== data.passage) {
      passage.textContent = data.passage;
      wrote = true;
    }
    // A draft pushed into an OPEN tab. Appended rather than assigned, so a half-typed follow-up
    // is never thrown away by a second invocation - losing what somebody typed is the one thing
    // the queue in the session was also built to avoid.
    if (typeof data.draft === 'string' && data.draft.length > 0) {
      const box2 = document.getElementById('say');
      if (box2) {
        box2.value = box2.value.length > 0 ? box2.value + '\\n\\n' + data.draft : data.draft;
        fitComposer();
        paintBackdrop();
        if (typeof box2.focus === 'function') { box2.focus(); }
      }
    }
    // And the OTHER operation: replace. A prompt preset is the instruction "ask this instead", which
    // is the opposite of a capture - sharing the appending branch above made it join a half-written
    // question into one neither of them wrote. (CodeRabbit, PR #200.)
    if (typeof data.setDraft === 'string') {
      const box3 = document.getElementById('say');
      if (box3) {
        box3.value = data.setDraft;
        fitComposer();
        paintBackdrop();
        if (typeof box3.focus === 'function') { box3.focus(); }
      }
    }
    const box = document.getElementById('say');
    // Only a real boolean moves the lock. A state that says nothing about running - a partial push,
    // or a null across the bridge - must leave the composer as it is rather than quietly unlocking
    // it while a turn is still in flight. (local, the second code round.)
    // Who a re-ask would go to, which is also the button's caption: the gesture is an empty box and
    // Enter, and a feature whose only trigger is pressing Enter on nothing is one nobody discovers.
    if (typeof data.spend === 'string') {
      const total = document.getElementById('spend');
      if (total) { total.textContent = data.spend; }
    }
    if (typeof data.reask === 'string') {
      canReask = data.reask.length > 0;
      const reaskButton = document.getElementById('send');
      if (reaskButton) { reaskButton.textContent = canReask ? 'Re-ask · ' + data.reask : 'Send'; }
    }
    // A stop is about the turn in flight. When nothing is in flight it is about nothing — and
    // holding on to the number would disable the same-numbered turn of the NEXT conversation, since
    // restarting keeps this page and begins counting again. (gemini and local, the code round, from
    // two directions.) The phrase the restart button uses is deliberately not written here: comments
    // in this template ship to the browser, and a test that looks for that text found this one.
    if (data.running === false) {
      stopAsked = 0;
    }
    if (box && typeof data.running === 'boolean' && typeof data.capped === 'boolean') {
      const wasLocked = box.disabled;
      lock(data.running || data.capped);
      // Back to the box when the turn ends. Without this every single follow-up costs a mouse click,
      // nine seconds after the last one — which is the whole conversation, one click at a time.
      if (wasLocked && !box.disabled && typeof box.focus === 'function') { box.focus(); }
    }
    // Rules 2 and 3, applied once for whatever this push contained - an answer, a failure, a
    // thinking line or a capped notice. There is one rule, not one per outcome, and the two halves
    // are exclusive by construction: something arrived, and either the reader was taken to it or
    // they are told it is there.
    if (wrote) {
      if (follow) { scheduleFollow(); } else { offerJump(true); }
    }
  });
}());`;
}

export function chatPageHtml(state: ChatPageState, nonce: string): string {
  // Built once and handed to both. The markup writes these four strings and the script remembers
  // them; serialising a long conversation twice per render is a cost that grows with the thing a
  // person is most likely to have a lot of. (local, the code round.)
  const regions = regionsOf(state);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(state.title)}</title>
<style>
${chatStyle(state.uiScale, state.textTone, state.models, state.messages)}
</style>
</head>
<body>
${chatBody(state, regions)}
<script nonce="${nonce}">
${chatScript(state, regions)}
</script>
</body>
</html>`;
}
