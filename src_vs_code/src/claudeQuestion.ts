/**
 * The question Claude Code is asking, taken as TEXT rather than as a screenshot.
 *
 * <p>Asked for in these words: *"если можно — перехватывать целиком что спрашивает Клод. потому что
 * сейчас приходится делать скриншоты"*. And the reason a screenshot was the only way is worth
 * writing down, because it decides this whole design: **the question widget cannot be selected**.
 * The operator proved it with a picture of a select-all that highlights the transcript above the
 * question and stops dead at the widget's edge. So a copy — even the synthetic one
 * `selectionCapture.ts` performs — would take everything EXCEPT the thing wanted.</p>
 *
 * <p>It is on disk, though. Claude Code appends every session to a JSON-lines file, and a question
 * is a `tool_use` block named `AskUserQuestion` whose input carries the questions, their options and
 * every option's description. That is worth more to a second model than a picture of the same
 * thing, and it costs no focus, no clipboard and no keystroke.</p>
 *
 * <p>Pure: lines in, decisions out. The caller finds the file and reads it — which is where the
 * uncertainty lives, and it is kept out of here on purpose.</p>
 */

/** One option offered by a question. */
export interface AskedOption {
  readonly label: string;
  readonly description: string;
}

/** One question. A single `AskUserQuestion` can carry several, and often does. */
export interface AskedQuestion {
  readonly header: string;
  readonly question: string;
  readonly multiSelect: boolean;
  readonly options: readonly AskedOption[];
}

/** A question found in a session, and what is known about it. */
export interface AskedSet {
  /** The `tool_use` id, which is how an answer names the question it answers. */
  readonly id: string;
  readonly questions: readonly AskedQuestion[];
  /** Whether an answer for it is already in the file. */
  readonly answered: boolean;
  /** The session the question was asked in, for a person who has two open. */
  readonly sessionId: string;
  /** When it was asked, as the file recorded it. */
  readonly at: string;
  /**
   * What this session is CALLED — and what its tab is called, which is the same string.
   *
   * <p>Claude Code writes `{"type":"ai-title","aiTitle":"…","sessionId":"…"}` into the session as it
   * names the conversation, and that title is what VS Code shows on the tab. Measured against a live
   * session: the tab reading *Подключение к scoreMeter DB* has a row saying exactly that.</p>
   *
   * <p>It is the only honest join between a TAB and a SESSION on this machine — there is no window
   * id to ask for — so it is what lets two sessions waiting in one folder be told apart instead of
   * refused. Empty when the session has no title row yet, which a short one does not.</p>
   */
  readonly title: string;
}

/** Anything, narrowed to an object, or nothing. */
function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** One option, or nothing when the shape is not one. */
function optionOf(value: unknown): AskedOption[] {
  const one = record(value);
  const label = text(one?.['label']);

  return label.length === 0 ? [] : [{ label, description: text(one?.['description']) }];
}

/** One question, or nothing. A question with no text is not one, however many options it carries. */
function questionOf(value: unknown): AskedQuestion[] {
  const one = record(value);
  const asked = text(one?.['question']);
  if (asked.length === 0) {
    return [];
  }
  const options = Array.isArray(one?.['options']) ? (one['options'] as unknown[]).flatMap(optionOf) : [];

  return [{
    header: text(one?.['header']),
    question: asked,
    multiSelect: one?.['multiSelect'] === true,
    options,
  }];
}

/**
 * The questions a `tool_use` block carries, or nothing when it is not one that asks.
 *
 * <p>EVERY question in the array, in order. `questions` holds four at a time often enough that
 * taking the first would drop most of what was asked — and silently, which is the worst way.</p>
 */
function askedIn(block: unknown): readonly AskedQuestion[] | undefined {
  const one = record(block);
  if (one?.['type'] !== 'tool_use' || one['name'] !== 'AskUserQuestion') {
    // Not a question at all — a Bash call, an answer, anything else. Nothing to say about it.
    return undefined;
  }
  const input = record(one['input']);

  return Array.isArray(input?.['questions']) ? (input['questions'] as unknown[]).flatMap(questionOf) : [];
}

/** The content blocks of a row, whatever kind of row it is. */
function blocksOf(row: Record<string, unknown>): unknown[] {
  const message = record(row['message']);

  return Array.isArray(message?.['content']) ? (message['content'] as unknown[]) : [];
}

/**
 * What a file had to say, which is three things and not two.
 *
 * <p>`unreadable` is a block that IS an `AskUserQuestion` and whose questions this build could not
 * read. Collapsing it into "nothing was asked" is what would happen if Anthropic moved a field: the
 * command would report that Claude is asking nothing while a question sat on screen, and the next
 * person to look would have no way to tell format drift from an empty session. The plan promised a
 * sentence naming the shape, so there is a third answer to give one. (codex, the code round.)</p>
 */
export type FileAsked =
  | { readonly kind: 'asked'; readonly set: AskedSet }
  | { readonly kind: 'unreadable' }
  | { readonly kind: 'nothing' };

/** Every question in the file, in the order they were asked, each with its answered state. */
function everyAsked(lines: readonly string[]): { asked: AskedSet[]; unreadable: boolean } {
  const asked: { id: string; questions: readonly AskedQuestion[]; sessionId: string; at: string }[] = [];
  const answered = new Set<string>();
  let unreadable = false;
  // The LAST one: Claude Code refines the title as the conversation goes on, and the tab shows the
  // newest. An older row would name the tab as it was called an hour ago.
  let title = '';

  for (const line of lines) {
    if (line.trim().length === 0) {
      continue;
    }
    let row: Record<string, unknown> | undefined;
    try {
      row = record(JSON.parse(line));
    } catch {
      // A half-written last line is the ordinary case for a file another process is appending to,
      // not a corrupt file. It costs that line and nothing else.
      continue;
    }
    if (row === undefined) {
      continue;
    }
    if (row['type'] === 'ai-title' && text(row['aiTitle']).length > 0) {
      title = text(row['aiTitle']);
      continue;
    }
    for (const block of blocksOf(row)) {
      const one = record(block);
      if (one?.['type'] === 'tool_result') {
        const names = text(one['tool_use_id']);
        if (names.length > 0) {
          answered.add(names);
        }
        continue;
      }
      const questions = askedIn(block);
      if (questions === undefined) {
        continue;
      }
      const id = text(one?.['id']);
      if (questions.length > 0 && id.length > 0) {
        asked.push({ id, questions, sessionId: text(row['sessionId']), at: text(row['timestamp']) });
      } else {
        // A question-shaped block this build could not read. Said out loud rather than counted as
        // silence, because the two want opposite reactions from whoever reads the sentence.
        unreadable = true;
      }
    }
  }

  return {
    asked: asked.map((one) => ({ ...one, answered: answered.has(one.id), title })),
    unreadable,
  };
}

/**
 * The question this session is waiting on — or the newest one, when none is waiting.
 *
 * <p><b>The newest UNANSWERED, not simply the newest.</b> A file where an unanswered question is
 * followed by an answered one is ordinary — a question can be left open while the conversation goes
 * on around it — and taking only the last one reported "already answered" while the first still sat
 * on screen waiting. (codex, the code round.)</p>
 *
 * <p>When nothing is waiting, the newest ANSWERED question comes back marked as such, so the caller
 * can say "the last question was already answered", which is a different sentence from "nothing was
 * ever asked here".</p>
 */
export function lastAsked(lines: readonly string[]): FileAsked {
  const { asked, unreadable } = everyAsked(lines);
  const waiting = asked.filter((one) => !one.answered);
  const found = waiting.length > 0 ? waiting[waiting.length - 1] : asked[asked.length - 1];
  if (found !== undefined) {
    return { kind: 'asked', set: found };
  }

  return unreadable ? { kind: 'unreadable' } : { kind: 'nothing' };
}

/**
 * The question as a passage: every question, every option, every description.
 *
 * <p>Plain text with the shape a reader expects, because this goes to a model as MATERIAL — it is
 * fenced downstream like any captured passage, so nothing in it is an instruction to anybody.</p>
 */
export function askedAsText(set: AskedSet): string {
  const parts = set.questions.map((question) => {
    const head = question.header.length > 0 ? `[${question.header}] ` : '';
    const pick = question.multiSelect ? ' (more than one answer may be chosen)' : '';
    const options = question.options.map((option) => (option.description.length > 0
      ? `  - ${option.label} — ${option.description}`
      : `  - ${option.label}`));

    return [`${head}${question.question}${pick}`, ...options].join('\n');
  });

  return parts.join('\n\n');
}
