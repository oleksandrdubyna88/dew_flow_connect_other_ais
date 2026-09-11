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
function askedIn(block: unknown): readonly AskedQuestion[] {
  const one = record(block);
  if (one?.['type'] !== 'tool_use' || one['name'] !== 'AskUserQuestion') {
    return [];
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
 * The last question asked in this session, and whether it has been answered.
 *
 * <p><b>A malformed line is skipped, never thrown over.</b> The file is being appended to by another
 * process, so its last line is routinely half-written — that is the ordinary case, not a corrupt
 * file. Each line is parsed on its own and a failure costs that line alone.</p>
 */
export function lastAsked(lines: readonly string[]): AskedSet | undefined {
  let found: AskedSet | undefined;
  const answered = new Set<string>();

  for (const line of lines) {
    if (line.trim().length === 0) {
      continue;
    }
    let row: Record<string, unknown> | undefined;
    try {
      row = record(JSON.parse(line));
    } catch {
      continue;
    }
    if (row === undefined) {
      continue;
    }
    for (const block of blocksOf(row)) {
      const one = record(block);
      if (one?.['type'] === 'tool_result') {
        answered.add(text(one['tool_use_id']));
        continue;
      }
      const questions = askedIn(block);
      if (questions.length > 0) {
        found = {
          id: text(one?.['id']),
          questions,
          answered: false,
          sessionId: text(row['sessionId']),
          at: text(row['timestamp']),
        };
      }
    }
  }

  return found === undefined ? undefined : { ...found, answered: answered.has(found.id) };
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
