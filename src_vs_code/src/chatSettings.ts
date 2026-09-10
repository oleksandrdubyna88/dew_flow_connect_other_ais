import { DEFAULT_CHAT_PROMPT } from './chatPrompt';
import { PromptPreset, chatPromptPresetsFrom, mainPrompt } from './chatPresets';
import { LANGUAGES, LanguageCode } from './settingsShape';

/**
 * The four settings the chat feature owns, parsed from whatever `settings.json` actually holds.
 *
 * <p>Pure: it takes a reader, not a `vscode` handle, so every fallback below is a unit test rather
 * than a claim. That matters more here than in most settings modules, because three of these four
 * reach a MODEL — a junk value does not draw a wrong pixel, it asks a wrong question and bills for
 * the answer.</p>
 *
 * <p><b>These are person-level settings and do not cross to the server.</b> They are deliberately
 * absent from `envBlock` and from the settings mirrored into `coai-mcp`: the review gate has no use
 * for the language somebody wants their explanations in, and a setting that travels where it is not
 * read is a setting that will one day be read by accident.</p>
 */

/** What happens after the passage is captured. See the master plan's table. */
export type ChatAutoSend = 'always' | 'keyboard' | 'never';

export const CHAT_AUTO_SEND: readonly ChatAutoSend[] = ['always', 'keyboard', 'never'];

/**
 * The default, and the only one of the three that can be defended without knowing the person.
 *
 * <p>`keyboard` spends a vendor turn automatically ONLY where the text is certainly the passage just
 * selected — the keybinding captured it itself. The menu path takes whatever is in the clipboard and
 * cannot know how old it is, so it fills the composer and waits. The owner may still choose
 * `always`; what he should not do is get it by default.</p>
 */
export const DEFAULT_AUTO_SEND: ChatAutoSend = 'keyboard';

export interface ChatSettings {
  /** The instruction the captured passage travels with. Never empty — falls back to `Explain`. */
  readonly prompt: string;
  /**
   * WHICH saved prompt {@link prompt} came from — the preset id the panel's picker holds.
   *
   * <p>An id rather than the words themselves, and that is the whole reason this setting exists: the
   * text is edited in the presets tab, so a sidebar holding a COPY of it would go stale the first
   * time somebody rewrote the prompt they had chosen. Empty means the main one.</p>
   */
  readonly promptChoice: string;
  /** The saved prompts, carrying the legacy migration — what the picker offers, by name. */
  readonly prompts: readonly PromptPreset[];
  /** The language answers are asked in. Its own setting, NOT `coai.helpLanguage` — see below. */
  readonly language: LanguageCode;
  readonly autoSend: ChatAutoSend;
  /**
   * Which configured row answers — the PROVIDER half of the pair. Empty means "the first offered".
   *
   * <p>The name predates the pair and is kept because the VALUE never changed meaning: it has always
   * held a vendor row id, which is what `legacyPick` reads it as. Renaming the setting would have
   * been a migration of every installation for a word.</p>
   */
  readonly model: string;
  /** Which of that row's models. Empty means the model the row itself is configured to. */
  readonly modelName: string;
}

/**
 * The codes, taken from the catalog rather than written out again.
 *
 * <p>There were two lists: this one, and `LANGUAGES` in `settingsShape.ts` with the native labels
 * the menus are built from. Two lists of the same five things drift the day a sixth is added — the
 * menu would offer it and this reader would refuse it, silently, back to English.</p>
 */
const CODES: readonly LanguageCode[] = LANGUAGES.map((language) => language.code);

/** A string, or the fallback — `settings.json` is a file a person edits by hand. */
function text(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

/**
 * Read the four, with every fallback stated.
 *
 * @param read the setting reader — `vscode.workspace.getConfiguration('coai').get` in the host
 */
export function chatSettingsFrom(read: (key: string) => unknown): ChatSettings {
  const language = read('chatLanguage');
  const autoSend = read('chatAutoSend');
  // The list carries its own migration: somebody who has been editing `coai.chatPrompt` since the
  // chat shipped owns exactly one prompt, and it is theirs. Read through the same function the
  // presets tab and the chat command read it through — a second reader here would be a second place
  // to forget the second argument, and forgetting it silently sends `Explain` instead of their words.
  const prompts = chatPromptPresetsFrom(read('chatPromptPresets'), text(read('chatPrompt'), ''));
  const choice = text(read('chatPromptChoice'), '');
  // A choice can name a prompt that was deleted in the other tab — the ordinary case, not an exotic
  // one, because the list is edited somewhere else. The main one answers then, never nothing: an
  // empty question is one that still gets asked, and billed.
  const chosen = prompts.find((preset) => preset.id === choice) ?? mainPrompt(prompts);

  return {
    // A blank prompt box is a cleared field, not a request for an empty question. `chatPrompt.ts`
    // makes the same decision at the other end; both are cheap and neither is the only guard.
    prompt: chosen?.text ?? DEFAULT_CHAT_PROMPT,
    promptChoice: choice,
    prompts,
    modelName: text(read('chatModelName'), ''),
    // English by default, and NOT `coai.helpLanguage`: that one is set to English on the owner's
    // machine, so borrowing it would have delivered English explanations — exactly what the feature
    // exists to avoid. One setting cannot answer two questions that disagree.
    language: CODES.includes(language as LanguageCode) ? (language as LanguageCode) : 'en',
    autoSend: CHAT_AUTO_SEND.includes(autoSend as ChatAutoSend)
      ? (autoSend as ChatAutoSend)
      : DEFAULT_AUTO_SEND,
    model: text(read('chatModel'), ''),
  };
}

/**
 * The settings a write to `key` invalidates, and which must be cleared with it.
 *
 * <p>One case, and it is the pair: `coai.chatModelName` names one of `coai.chatModel`'s models, so
 * choosing a different provider leaves it holding the PREVIOUS provider's model. Left alone, the
 * panel strands that value in its select while `openingModel` quietly opens the conversation on the
 * row's own model — the section describing a state that is not the one the chat is in. Raised on the
 * plan round by gemini as Blocking, and by codex from the other end.</p>
 *
 * <p>A pure rule rather than an `if` in the write path, because that is where it would be forgotten
 * the day a third setting joins the pair.</p>
 */
export function clearedByWriting(key: string): readonly string[] {
  return key === 'chatModel' ? ['chatModelName'] : [];
}

/**
 * Whether this trigger sends by itself.
 *
 * <p>Its own function because it is the one decision in this module that spends money, and a caller
 * reading `settings.autoSend === 'always' || (fromMenu === false && …)` at the call site is a caller
 * that will get it subtly wrong the second time it is written.</p>
 */
export function sendsImmediately(autoSend: ChatAutoSend, fromMenu: boolean): boolean {
  if (autoSend === 'always') {
    return true;
  }
  if (autoSend === 'never') {
    return false;
  }

  return !fromMenu;
}
