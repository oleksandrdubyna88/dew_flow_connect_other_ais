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
 * Everything the PERSON wrote in this session, in the order they wrote it.
 *
 * <p>Asked for because a window four hours old has lost the thing it is about: *"начальный вопрос
 * исчезает, и мне приходится спрашивать Клод над чем ты работаешь"*. It is on disk the whole time.</p>
 *
 * <p><b>A human prompt is one Claude Code attributed to a person</b> — `origin.kind === 'human'`.
 * A prompt an extension prefilled is still theirs: CredsForDevs writes its preamble into the box and
 * the person types their question at the end of it, so the two arrive as ONE message and the operator
 * reads it as their own. Checked with them rather than guessed.</p>
 *
 * <p>ALL of them, oldest first, because the page offers to step through: after four hours the first
 * line is not always the one that says what the work turned into.</p>
 *
 * <p>A slash command is kept and unwrapped: `&lt;command-name&gt;` tags are Claude Code's own
 * envelope around something a person really did type.</p>
 */
/**
 * What the person said in ONE line of a session file, or empty when nobody did.
 *
 * <p>A line at a time, so the file can be streamed rather than held. A day-long session is tens of
 * megabytes and a project folder holds weeks of them; reading each one into a string and splitting
 * it into an array of lines allocates both, on the extension host's own thread. (The code round, on
 * this point from five reviewers across two vendors.)</p>
 */
export function humanSaid(line: string): string {
  if (line.trim().length === 0) {
    return '';
  }
  let row: Record<string, unknown> | undefined;
  try {
    row = record(JSON.parse(line));
  } catch {
    // A half-written last line, which is what the end of a live session always looks like.
    return '';
  }
  if (row?.['type'] !== 'user' || row['isSidechain'] === true || row['isMeta'] === true) {
    return '';
  }
  const origin = record(row['origin']);
  if (origin?.['kind'] !== 'human') {
    return '';
  }

  return plainly(saidIn(row));
}

/**
 * The title a session gives itself in ONE line, or empty. The caller keeps the last one.
 *
 * <p>Named for what it reads out of the line rather than for who said it — the neighbouring
 * {@link humanSaid} returns a PERSON's words, and a matching name here read as though this did too.
 * (The second code round, as a nit worth taking.)</p>
 */
export function titleFrom(line: string): string {
  if (!line.includes('"ai-title"')) {
    return '';
  }
  try {
    const row = JSON.parse(line) as { type?: unknown; aiTitle?: unknown };

    return row.type === 'ai-title' && typeof row.aiTitle === 'string' ? row.aiTitle : '';
  } catch {
    // A half-written line names nothing.
    return '';
  }
}

/** The text of a user row, whichever shape its content takes. */
function saidIn(row: Record<string, unknown>): string {
  const message = record(row['message']);
  const content = message?.['content'];
  if (typeof content === 'string') {
    return content;
  }
  if (!Array.isArray(content)) {
    return '';
  }
  // EVERY text block, not the first. A turn can arrive in several of them — a prefilled preamble
  // and the person's own question are two — and returning at the first cut their words in half.
  // (gemini, the code round.)
  const spoken: string[] = [];
  for (const block of content as unknown[]) {
    const one = record(block);
    // The block's KIND, not merely the presence of a `text` field. A `tool_use` block carries one
    // too, and taking the first thing that looked like prose would have shown the machinery's words
    // as the person's. (The plan round, on a row whose blocks are mixed.)
    if (one?.['type'] === 'text' && typeof one['text'] === 'string' && one['text'].trim().length > 0) {
      spoken.push(one['text']);
    }
  }

  return spoken.join('\n');
}

/**
 * A prompt as a person would recognise it: the slash-command envelope unwrapped, the tooling gone.
 */
function plainly(said: string): string {
  // ONE CONTIGUOUS ENVELOPE, anchored at the start. Claude Code writes the tags as a block that
  // opens the message; the same tags elsewhere in one are a person quoting them, and unwrapping
  // there replaced everything they actually wrote with the quotation. Matching the two halves
  // separately had the same hole in the middle: a message that OPENED with a command name and went
  // on to quote a command-args tag further down came back as neither. (gemini, then codex.)
  //
  // The CLOSING TAG delimits the arguments, not "anything but a left angle bracket". A person whose
  // command arguments mention `Array<T>` or `x < limit` wrote those, and `[^<]*` refused the whole
  // envelope over them — so their prompt came back wearing its tags. (CodeRabbit, PR #207.)
  const envelope = /^\s*<command-name>([\s\S]*?)<\/command-name>\s*(?:<command-message>[\s\S]*?<\/command-message>\s*)?(?:<command-args>([\s\S]*?)<\/command-args>)?\s*$/
    .exec(said);
  if (envelope !== null) {
    // The tag already carries its slash — measured on this machine's own session files, where every
    // one of them reads <command-name>/compact</command-name>. Adding another made it //compact.
    return [(envelope[1] ?? '').trim(), (envelope[2] ?? '').trim()].filter((part) => part.length > 0).join(' ');
  }
  // A local command's own output is not something anybody wrote.
  if (said.startsWith('<local-command-stdout>')) {
    return '';
  }

  return said.trim();
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
