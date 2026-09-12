import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { humanSaid } from '../claudeQuestion';
import {
  Asked,
  Found,
  MOST_PER_PROMPT,
  MOST_PROMPTS,
  namesTheSame,
  oneAnswerFrom,
  pinnable,
  promptsFrom,
  sessionFileIn,
} from '../claudeSessions';
import { chatCommandOf } from '../chatMessages';

/**
 * What the PERSON wrote, read back off the session file.
 *
 * <p>A chat window four hours old has lost the thing it is about — the question scrolled away and
 * the operator ends up asking Claude what it is working on. The words never went anywhere: Claude
 * Code writes every one of them to its own session file. These tests fix the two halves of getting
 * them back — which rows count as a person speaking, and which session belongs to this tab.</p>
 */

/** The two halves, composed exactly as the host composes them: find the file, then read it. */
const promptsInSession = async (
  home: string,
  cwd: string,
  caseBlind: boolean,
  looking: string,
): Promise<Asked> => {
  const found = await sessionFileIn(home, cwd, caseBlind, looking);

  return found.kind === 'one' ? await promptsFrom(found.file) : found;
};

/** What the shipping reader would take from these lines: it is fed one at a time, off a stream. */
const humanPrompts = (lines: readonly string[]): readonly string[] =>
  lines.map(humanSaid).filter((one) => one.length > 0);

const said = (text: string, extra: Record<string, unknown> = {}): string => JSON.stringify({
  type: 'user',
  origin: { kind: 'human' },
  message: { content: [{ type: 'text', text }] },
  ...extra,
});

const titled = (name: string): string => JSON.stringify({ type: 'ai-title', aiTitle: name, sessionId: 's' });

test('every human turn comes back, oldest first', () => {
  // ALL of them, because the first line is not reliably the one that says what the work became —
  // the page steps through them. Asked for as "первое плюс стрелки «следующее»".
  const found = humanPrompts([said('make the task text green'), titled('Colours'), said('and now the стрелки')]);

  assert.deepStrictEqual(found, ['make the task text green', 'and now the стрелки']);
});

test('a turn an extension prefilled is still the person speaking', () => {
  // CredsForDevs writes its preamble into the composer and the operator types their question at the
  // end of it, so the two arrive as ONE message. Checked with them rather than guessed: *"то что ты
  // посчитал впрыском был мой вопрос. все ок."*
  const both = 'You have CredsForDevs available.\n\nа если в папке 2 сессии и обе задали вопрос?';

  assert.deepStrictEqual(humanPrompts([said(both)]), [both]);
});

test('what the machinery says is not what a person said', () => {
  const lines = [
    said('mine'),
    // A block carrying a `text` field that is not a text BLOCK: the machinery's words, in the
    // person's row. Taking the first thing that looked like prose showed them as theirs.
    JSON.stringify({
      type: 'user',
      origin: { kind: 'human' },
      message: { content: [{ type: 'tool_use', text: 'not typed by anyone', name: 'Read' }] },
    }),
    // A tool result: a user row, but nobody typed it.
    JSON.stringify({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'a' }] } }),
    // A sidechain is a subagent's own conversation, and a meta row is Claude Code talking to itself.
    said('a subagent', { isSidechain: true }),
    said('a reminder', { isMeta: true }),
    // An assistant turn is the other half of the conversation.
    JSON.stringify({ type: 'assistant', message: { content: [{ type: 'text', text: 'not yours' }] } }),
    // And a local command's own output arrives wearing a human's clothes.
    said('<local-command-stdout>ok</local-command-stdout>'),
  ];

  assert.deepStrictEqual(humanPrompts(lines), ['mine']);
});

test('a slash command is unwrapped to what was typed, not to its envelope', () => {
  const wrapped = said('<command-name>/feature-dev</command-name><command-args>покажи вопрос</command-args>');

  assert.deepStrictEqual(humanPrompts([wrapped]), ['/feature-dev покажи вопрос']);
});

test('a half-written line is skipped rather than throwing the whole read away', () => {
  // The file is being appended to while it is read — the last line is regularly a fragment.
  assert.deepStrictEqual(humanPrompts([said('first'), '{"type":"user", "origin', '', said('second')]), [
    'first',
    'second',
  ]);
});

test('the TAB decides which session is read back', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Пул с несколькими PR')}\n${said('the wrong one')}\n`, 'utf8');
    writeFileSync(join(dir, 'two.jsonl'), `${titled('Подключение к БД')}\n${said('the right one')}\n`, 'utf8');

    const found = await promptsInSession(home, 'D:\\work\\app', true, 'Подключение к БД');

    assert.strictEqual(found.kind, 'said');
    assert.deepStrictEqual(
      found.kind === 'said' ? found.said : [],
      ['the right one'],
      'the tab was shown somebody else’s conversation',
    );
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a tab whose title names no session reads nothing, rather than the newest', async () => {
  // The same rule the waiting question follows: delivering the wrong conversation silently is worse
  // than saying there is nothing here.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('One conversation')}\n${said('not for you')}\n`, 'utf8');

    const missed = await promptsInSession(home, 'D:\\work\\app', true, 'Something else');
    assert.strictEqual(missed.kind, 'none');
    assert.match(
      missed.kind === 'none' ? missed.refusal : '',
      /Something else/,
      'the refusal does not name the title it looked for',
    );

    // And a tab with no name at all — a chat opened from a file — never even looks.
    const unnamed = await promptsInSession(home, 'D:\\work\\app', true, '');
    assert.strictEqual(unnamed.kind, 'none');
    assert.match(unnamed.kind === 'none' ? unnamed.refusal : '', /not named after/);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a folder Claude has never run in says so BY NAME, rather than throwing or going quiet', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const answer = await promptsInSession(home, 'D:\\work\\app', true, 'Anything');

    assert.strictEqual(answer.kind, 'none');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /nothing there to read|no sessions for this folder/);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('two sessions sharing a title are REFUSED, never picked between', async () => {
  // It picked the first the directory listed until the plan round said so, from two vendors at once.
  // The whole reason the title join exists is that handing over somebody else's conversation
  // silently is the worst thing this can do — and choosing between namesakes is that, with steps.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Same name')}\n${said('the first')}\n`, 'utf8');
    writeFileSync(join(dir, 'two.jsonl'), `${titled('Same name')}\n${said('the second')}\n`, 'utf8');

    const answer = await promptsInSession(home, 'D:\\work\\app', true, 'Same name');

    assert.strictEqual(answer.kind, 'several', 'a namesake session was shown as though it were this one');
    assert.match(answer.kind === 'several' ? answer.refusal : '', /2 sessions/, 'the refusal does not say how many');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session this tab owns but has never been spoken in is told apart from one that is missing', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Fresh')}\n`, 'utf8');

    const answer = await promptsInSession(home, 'D:\\work\\app', true, 'Fresh');

    assert.strictEqual(answer.kind, 'none');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /Nothing of yours is in that session yet/);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('what crosses to the page is bounded, in both directions', async () => {
  // A day-long session is hundreds of turns and some of them are whole files. All of it in one
  // postMessage is megabytes over a bridge that has to stay responsive. (gemini, the plan round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    const many = [titled('Long day'), said('x'.repeat(MOST_PER_PROMPT + 500))];
    for (let turn = 0; turn < MOST_PROMPTS + 20; turn += 1) {
      many.push(said(`turn ${turn}`));
    }
    writeFileSync(join(dir, 'one.jsonl'), `${many.join('\n')}\n`, 'utf8');

    const answer = await promptsInSession(home, 'D:\\work\\app', true, 'Long day');
    const got = answer.kind === 'said' ? answer.said : [];

    assert.strictEqual(got.length, MOST_PROMPTS, 'the whole day crossed the bridge');
    // The EARLIEST are kept: the question this button answers is what the window was FOR.
    assert.match(got[1] ?? '', /^turn 0$/, 'the earliest turns were the ones dropped');
    assert.ok((got[0] ?? '').length < MOST_PER_PROMPT + 200, 'a pasted file crossed whole');
    assert.match(got[0] ?? '', /cut here/, 'a turn was cut without saying so');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('the page can ask for it, and cannot ask for anything else by that name', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'showAsked' }), { kind: 'showAsked' });
  // And a page from an older build, asking for it the way nothing asks for it, is a press of nothing.
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'showAsked' }), { kind: 'ignore' });
});

test('a turn written in several blocks comes back whole', () => {
  // A prefilled preamble and the person's own question can be two blocks of one turn. Returning at
  // the first cut their words in half. (gemini, the code round.)
  const split = JSON.stringify({
    type: 'user',
    origin: { kind: 'human' },
    message: {
      content: [
        { type: 'text', text: 'You have CredsForDevs available.' },
        { type: 'tool_result', tool_use_id: 'x' },
        { type: 'text', text: 'а если в папке 2 сессии?' },
      ],
    },
  });

  assert.deepStrictEqual(humanPrompts([split]), ['You have CredsForDevs available.\nа если в папке 2 сессии?']);
});

test('the envelope is unwrapped in the shape a real session file has', () => {
  // Measured on this machine rather than imagined: the tags are separated by a newline, the args run
  // to several lines, and the name already carries its own slash.
  const real = said('<command-name>/feature-dev:feature-dev</command-name>\n<command-args>проект стал '
    + 'сильно большой.\nхочу структурировать.</command-args>');

  assert.deepStrictEqual(humanPrompts([real]), [
    '/feature-dev:feature-dev проект стал сильно большой.\nхочу структурировать.',
  ]);
});

test('command arguments may contain the angle brackets a person typed', () => {
  // `[^<]*` refused the whole envelope over a `<` the PERSON wrote, and their prompt came back
  // wearing its tags. The closing tag delimits the arguments; nothing else does. (CodeRabbit, #207.)
  const code = said('<command-name>/explain</command-name>\n<command-args>why is Array<T> not '
    + 'assignable when x < limit?</command-args>');

  assert.deepStrictEqual(humanPrompts([code]), ['/explain why is Array<T> not assignable when x < limit?']);
});

test('a message that merely OPENS with the tags is not an envelope', () => {
  // Matching the two halves separately left a hole in the middle: a message opening with a command
  // name and quoting a command-args tag further down came back as neither half of what was written.
  // (codex, the second code round.)
  const mixed = said('<command-name>/compact</command-name>please keep this text <command-args>quoted</command-args>');

  assert.deepStrictEqual(humanPrompts([mixed]), [
    '<command-name>/compact</command-name>please keep this text <command-args>quoted</command-args>',
  ]);
});

test('a command envelope is unwrapped only where Claude Code puts one — at the start', () => {
  // Quoting the tags mid-sentence replaced everything the person actually wrote with the quotation.
  const quoted = said('is <command-name>/compact</command-name> the tag you meant?');

  assert.deepStrictEqual(humanPrompts([quoted]), ['is <command-name>/compact</command-name> the tag you meant?']);
  // And the real envelope still unwraps.
  assert.deepStrictEqual(humanPrompts([said('<command-name>/compact</command-name>')]), ['/compact']);
});

test('one folder of several answers; two of them is a refusal, never a pick', () => {
  // FIRST-MATCH-WINS was the bug: a workspace with two roots, each holding a session called Build,
  // showed whichever root VS Code listed first. Five reviewers across two vendors, one round.
  const found = (said: string): Asked => ({ kind: 'said', said: [said] });
  const nothing: Asked = { kind: 'none', refusal: 'nothing in this one' };

  assert.deepStrictEqual(oneAnswerFrom([nothing, found('mine'), nothing]), found('mine'));

  const both = oneAnswerFrom([found('one root'), found('the other root')]);
  assert.strictEqual(both.kind, 'several', 'a tab was handed one of two folders at random');
  assert.match(both.kind === 'several' ? both.refusal : '', /2 of the folders/);
});

test('with nothing found anywhere, the reason given is the first one, and never silence', () => {
  const first: Asked = { kind: 'none', refusal: 'the nearest miss' };
  const second: Asked = { kind: 'none', refusal: 'a folder they were not asking about' };

  assert.deepStrictEqual(oneAnswerFrom([first, second]), first);

  // A window with no folder open at all still gets a sentence rather than an empty region.
  const none = oneAnswerFrom([]);
  assert.strictEqual(none.kind, 'none');
  assert.match(none.kind === 'none' ? none.refusal : '', /no folder open/);
});

test('a namesake refusal outranks a plain miss, because it is the one worth acting on', () => {
  const ambiguous: Asked = { kind: 'several', refusal: '2 sessions here share this name' };
  const nothing: Asked = { kind: 'none', refusal: 'nothing in this one' };

  assert.deepStrictEqual(oneAnswerFrom([nothing, ambiguous]), ambiguous);
});

test('an ambiguity in ONE root is an ambiguity, even when another root has a single match', () => {
  // One match here and two namesakes there is three candidates, not one — and answering with the
  // single match picks between three while looking as though it picked between none. (codex, the
  // second code round.)
  const found: Asked = { kind: 'said', said: ['from the tidy root'] };
  const ambiguous: Asked = { kind: 'several', refusal: '2 sessions in this folder share the name' };

  assert.deepStrictEqual(oneAnswerFrom([found, ambiguous]), ambiguous);
  assert.deepStrictEqual(oneAnswerFrom([ambiguous, found]), ambiguous);
});

test('a tab that found its file keeps reading it after Claude renames the conversation', () => {
  // THE POINT OF PINNING. Claude Code refines a conversation's ai-title as it goes on and the tab
  // follows it, so the name captured when this chat opened stops matching hours later — which is
  // exactly the window the button exists for. A file does not move. (codex, the second code round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));

  return (async () => {
    try {
      const dir = join(home, '.claude', 'projects', 'D--work-app');
      mkdirSync(dir, { recursive: true });
      const file = join(dir, 'one.jsonl');
      writeFileSync(file, `${titled('The name it had')}\n${said('over what was this window')}\n`, 'utf8');

      const found = await sessionFileIn(home, 'D:\\work\\app', true, 'The name it had');
      assert.strictEqual(found.kind, 'one', 'the tab could not find its own session while its name matched');

      // Four hours later, Claude has renamed the conversation. Searching by the old name finds
      // nothing at all — and the pinned file still answers.
      writeFileSync(file, `${titled('The name it had')}\n${said('over what was this window')}\n${titled('What it became')}\n`, 'utf8');

      assert.strictEqual((await promptsInSession(home, 'D:\\work\\app', true, 'The name it had')).kind, 'none');
      const still = await promptsFrom(found.kind === 'one' ? found.file : '');
      assert.deepStrictEqual(still.kind === 'said' ? still.said : [], ['over what was this window']);
    } finally {
      rmSync(home, { recursive: true, force: true });
    }
  })();
});

test('a pinned file that is gone says so, rather than looking like an empty conversation', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const gone = join(home, 'never-existed.jsonl');

    const answer = await promptsFrom(gone);

    assert.strictEqual(answer.kind, 'none');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /no longer on disk/);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a file that IS there but holds nothing of yours is told apart from one that is gone', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const file = join(home, 'empty.jsonl');
    writeFileSync(file, `${titled('Fresh')}\n`, 'utf8');

    const answer = await promptsFrom(file);

    assert.strictEqual(answer.kind, 'none');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /Nothing of yours is in that session yet/);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('what may be PINNED is exactly what the button would answer', () => {
  // The pin is that refusal made once and kept, so the two must agree: a pin the button would have
  // refused makes the refusal permanent and invisible, on a tab nobody will think to suspect. They
  // disagreed when they were first written — one root with a single match and another with two
  // namesakes was three candidates to the button and one to the pin.
  const one = (file: string): Found => ({ kind: 'one', file });
  const nothing: Found = { kind: 'none', refusal: 'nothing in this one' };
  const ambiguous: Found = { kind: 'several', refusal: '2 sessions here share the name' };

  assert.strictEqual(pinnable([nothing, one('mine.jsonl')]), true, 'a single match anywhere was not pinnable');
  assert.strictEqual(pinnable([one('a.jsonl'), ambiguous]), false, 'a namesake elsewhere was pinned over');
  assert.strictEqual(pinnable([one('a.jsonl'), one('b.jsonl')]), false, 'two roots both matched and one was pinned');
  assert.strictEqual(pinnable([nothing]), false, 'nothing was pinned as something');
  assert.strictEqual(pinnable([]), false, 'a window with no folder open pinned a file');
});

test('a window with NO FOLDER open looks where Claude Code actually ran — the home directory', async () => {
  // Measured on the operator's machine, with the button open beside a live conversation it could not
  // see: the tab read "Подключение к scoreMeter DB" and `workspaceFolders` was undefined, so the
  // search was scoped to a workspace that did not exist. Claude Code does not need a folder — a VS
  // Code terminal with none starts in the home directory, and the session is filed under it.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    // The project directory home itself would have: every separator becomes a dash.
    const dir = join(home, '.claude', 'projects', home.replace(/[\\/:]/g, '-'));
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Подключение к scoreMeter DB')}\n${said('мой вопрос')}\n`, 'utf8');
    // And another project entirely — 77 of them on the machine this was found on, a gigabyte in all.
    // Reading them to compare titles was the first attempt and was worse than the bug it fixed.
    const other = join(home, '.claude', 'projects', 'D--rsd-something');
    mkdirSync(other, { recursive: true });
    writeFileSync(join(other, 'two.jsonl'), `${titled('Something else')}\n${said('not yours')}\n`, 'utf8');

    const found = await sessionFileIn(home, home, true, 'Подключение к scoreMeter DB');

    assert.strictEqual(found.kind, 'one', 'a window with no folder open could not find a session that exists');
    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['мой вопрос']);

    // And a name that is in ANOTHER project is not this window's, however real it is.
    assert.strictEqual((await sessionFileIn(home, home, true, 'Something else')).kind, 'none');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a refusal names the directory it looked in, so a window can tell where it went wrong', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('One conversation')}\n`, 'utf8');

    const missed = await sessionFileIn(home, 'D:\\work\\app', true, 'Another');

    assert.strictEqual(missed.kind, 'none');
    assert.match(missed.kind === 'none' ? missed.refusal : '', /D--work-app/, 'the refusal does not say where it looked');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a tab wearing a SHORTENED title still finds its session', () => {
  // The defect the operator found on the shipped build, in the two strings that caused it: Claude
  // Code truncates the title on its own panel and writes the whole thing to the session file. An
  // exact comparison matched short conversations and never long ones — and said so, correctly and
  // uselessly, with the ellipsis still in the refusal.
  assert.strictEqual(namesTheSame('Подключение к scoreMeter DB', 'Подключение к scoreMeter...'), true);
  assert.strictEqual(namesTheSame('Подключение к scoreMeter DB', 'Подключение к scoreMeter…'), true,
    'the single-character ellipsis is the same truncation');

  // A whole name is still matched whole. Nothing is loosened where nothing was truncated.
  assert.strictEqual(namesTheSame('Пул с несколькими PR', 'Пул с несколькими PR'), true);
  assert.strictEqual(namesTheSame('Пул с несколькими PR', 'Пул с несколькими'), false,
    'a name that was not truncated became a prefix anyway');

  // A prefix is a prefix, not a substring, and an ellipsis alone names everything — so it names nothing.
  assert.strictEqual(namesTheSame('Подключение к scoreMeter DB', 'scoreMeter...'), false);
  assert.strictEqual(namesTheSame('Anything at all', '...'), false, 'an ellipsis alone matched a session');
  assert.strictEqual(namesTheSame('Anything at all', ''), false);
});

test('a shortened title that fits TWO sessions is still a refusal', async () => {
  // What keeps the prefix from becoming a licence to guess: the ambiguity rule does not care why two
  // names matched.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Подключение к scoreMeter DB')}\n${said('the first')}\n`, 'utf8');
    writeFileSync(join(dir, 'two.jsonl'), `${titled('Подключение к scoreMeter API')}\n${said('the second')}\n`, 'utf8');

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'Подключение к scoreMeter...');

    assert.strictEqual(answer.kind, 'several', 'a shortened title picked between two conversations');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('the shortened title reaches the whole session, not just its name', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Подключение к scoreMeter DB')}\n${said('мой вопрос')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\work\\app', true, 'Подключение к scoreMeter...');
    assert.strictEqual(found.kind, 'one');

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['мой вопрос']);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});
