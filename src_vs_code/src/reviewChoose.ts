import { ChatEntry } from './chatPanels';
import { aboutText } from './reviewAbout';
import { ReviewPair } from './reviewPair';
import { text } from './reviewText';

/**
 * What *CoAI: choose* on one bug hands a chat (issue #487).
 *
 * <p>The operator's words: *"при нажатии — она забирает Where, Why, Fix, Complexity, Before, After,
 * Your comment on this pair, ложит это в чат и дает человеку уже выбрать модель и вопрос"*. So this is
 * the PASSAGE, and only the passage: the instruction, the model and the question are the chat's, chosen
 * in its composer the way the right-click *CoAI: choose* leaves them.</p>
 *
 * <p>Pure, and free of `node:` and `vscode`: the host decides where the conversation goes, this decides
 * what it is about. Where and Complexity come from the same sentences the row shows
 * ({@link aboutText}), so the chat and the page can never say a line or a count differently.</p>
 */

/** One bug, ready to become a conversation. */
export interface BugChat {
  /** Which bug this is — the same checkout and finding is the same conversation. */
  readonly key: string;
  /** What the chat tab is called: the method's name, which is what the row leads with. */
  readonly label: string;
  /** The passage the composer is filled with. */
  readonly text: string;
  /** The checkout and the path inside it, so the chat can say where it came from. */
  readonly repoPath: string;
  readonly file: string;
}

/**
 * A code block that nothing inside it can close.
 *
 * <p>A skeleton is somebody's code, and code holds backticks — a template literal, a Markdown string, a
 * doc comment quoting a fence. A fixed ```` ``` ```` would end at the first one inside and turn the rest
 * of the method into prose. The fence is one backtick longer than the longest run in the code, which is
 * the rule CommonMark gives for exactly this.</p>
 */
export function fenced(code: string, language: string): string {
  const longest = Math.max(0, ...[...code.matchAll(/`+/gu)].map((run) => run[0].length));
  const fence = '`'.repeat(Math.max(3, longest + 1));

  // The info string is the pair's language as the server recorded it, and CommonMark lets it hold no
  // backtick; a line break would end it early. So it keeps only what a language name is made of.
  const info = language.toLowerCase().replace(/[^a-z0-9#+.-]/gu, '');

  return `${fence}${info}\n${code}\n${fence}`;
}

/** The words about this pair: the ones being typed if there are any, else the ones saved, else none. */
function commentOf(pair: ReviewPair, drafts: ReadonlyMap<number, string>): string {
  const said = text(drafts.get(pair.findingId) ?? pair.comment);

  return said.length > 0 ? said : '(none)';
}

/**
 * The seven parts, labelled and in the issue's order.
 *
 * @param drafts what the panel holds from keystrokes — a comment is typed long before it is saved,
 *   and the person pressing the button means the words on screen, not the last ones written down
 */
export function bugChat(pair: ReviewPair, drafts: ReadonlyMap<number, string>): BugChat {
  const about = aboutText(pair);
  const passage = [
    `**Where:** ${about.where}`,
    `**Why:** ${about.why}`,
    `**Fix:** ${about.fix}`,
    `**Complexity:** ${about.complexity}`,
    '**Before:**',
    fenced(pair.skeletonBefore, pair.language),
    '**After:**',
    fenced(pair.skeletonAfter, pair.language),
    `**Your comment on this pair:** ${commentOf(pair, drafts)}`,
  ].join('\n\n');

  return {
    key: `${text(pair.repoPath)}#${pair.findingId}`,
    label: text(pair.symbolName),
    text: passage,
    repoPath: text(pair.repoPath),
    file: text(pair.file),
  };
}

/** The two lookups a bug's conversation needs from the chat registry — `ChatPanels` has both. */
export interface Conversations {
  get(key: object): ChatEntry | undefined;
  entryOf(id: object): ChatEntry | undefined;
}

/**
 * Each bug's conversation, for as long as the window lives.
 *
 * <p>The chat registry keys conversations by object IDENTITY — a Claude tab or a file tab is its own
 * key. A bug has no tab, so it is given one object, minted on the first press. After a reload a press
 * opens a new conversation, the rule every non-Claude source already has (`sessionKey.rekeysByLabel`);
 * the old one is kept, under the method's name, in *CoAI: switch conversations*.</p>
 */
export class BugChats {
  private readonly keys = new Map<string, object>();
  private readonly ids = new Map<string, object>();

  keyFor(bug: string): object {
    const known = this.keys.get(bug);
    if (known !== undefined) {
      return known;
    }
    const minted = Object.freeze({ bug });
    this.keys.set(bug, minted);

    return minted;
  }

  /** The conversation a first press made, written down by its id — nothing, when the press was refused. */
  remember(bug: string, panels: Conversations): void {
    const made = panels.get(this.keyFor(bug));
    if (made !== undefined) {
      this.ids.set(bug, made.id);
    }
  }

  /**
   * The bug's open conversation, wherever its key has got to — or nothing.
   *
   * <p>By the conversation's ID, not by the key it was opened under. *Go to conversation* and the picker
   * bind a conversation to its file's tab by re-keying it, and the key this class minted then names
   * nothing: looked up by key, the next press opened a second conversation about the same bug. (Our own
   * code reviewer.) A closed one is gone from the registry, so the next press opens a new one.</p>
   */
  openFor(bug: string, panels: Conversations): ChatEntry | undefined {
    const id = this.ids.get(bug);

    return id === undefined ? undefined : panels.entryOf(id);
  }
}
