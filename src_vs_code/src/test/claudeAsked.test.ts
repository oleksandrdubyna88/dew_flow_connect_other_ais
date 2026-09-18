import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, symlinkSync, writeFileSync, utimesSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { humanSaid } from '../claudeQuestion';
import {
  Asked,
  Found,
  MOST_PER_PROMPT,
  MOST_PROMPTS,
  foldersToSearch,
  namesTheSame,
  oneAnswerFrom,
  pinnable,
  promptsFrom,
  projectDirName,
  projectDirNames,
  SCAN_BUDGET,
  sessionFileIn,
  sessionFileOf,
  severalMatch,
  staysInside,
  waitingQuestion,
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

      // Four hours later, Claude has renamed the conversation, and the pinned file still answers.
      writeFileSync(file, `${titled('The name it had')}\n${said('over what was this window')}\n${titled('What it became')}\n`, 'utf8');

      // This line used to assert `none` — searching by the old name found nothing at all, and the
      // pin was the only thing standing between the person and that refusal. It is `one` now: a
      // session answers to every name it has worn, so the name the tab captured still reaches it.
      // The pin is what makes the answer independent of ALL of them, which is why both are asserted.
      assert.strictEqual((await promptsInSession(home, 'D:\\work\\app', true, 'The name it had')).kind, 'said',
        'a name the session used to wear no longer reaches it');
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
  const one = (file: string): Found => ({ kind: 'one', file, complete: true });
  const nothing: Found = { kind: 'none', why: 'unmatched', refusal: 'nothing in this one' };
  const ambiguous: Found = { kind: 'several', refusal: '2 sessions here share the name' };

  assert.strictEqual(pinnable([nothing, one('mine.jsonl')]), true, 'a single match anywhere was not pinnable');
  assert.strictEqual(pinnable([one('a.jsonl'), ambiguous]), false, 'a namesake elsewhere was pinned over');
  assert.strictEqual(pinnable([one('a.jsonl'), one('b.jsonl')]), false, 'two roots both matched and one was pinned');
  assert.strictEqual(pinnable([nothing]), false, 'nothing was pinned as something');

  // AND ITS OTHER HALF: not pinnable is TWO situations — nothing was found, and too much was — and
  // only the second is evidence that a saved conversation of this name belongs to a session in front
  // of somebody. `chatGoto`'s name fallback leans on that and on nothing else.
  //
  // COUNTED OVER THE ANSWERS, never over the array: `findSession` returns one outcome per FOLDER and
  // a single folder can answer `several`, so a length test reads false in a one-root workspace
  // however many sessions share the name — which is where nearly everybody is, and where the
  // fallback was consequently dead. (CodeRabbit, on the pull request.)
  assert.strictEqual(severalMatch([ambiguous]), true,
    'ONE folder holding several sessions of this name reads as though the name were unique — the case that shipped');
  assert.strictEqual(severalMatch([nothing, ambiguous]), true, 'several in the second folder went unnoticed');
  assert.strictEqual(severalMatch([one('a.jsonl'), one('b.jsonl')]), true, 'two roots matching one name each is still two');
  assert.strictEqual(severalMatch([nothing, one('mine.jsonl')]), false, 'a single match anywhere was read as several');
  assert.strictEqual(severalMatch([nothing]), false, 'nothing found was read as too much found');
  assert.strictEqual(severalMatch([]), false, 'a walk that answered nothing at all was read as several');
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

test('a window with no folder open looks in the home directory, not nowhere', () => {
  // Both readers used to answer that there was nowhere to look, and Take the question said it in a
  // sentence confident enough that nobody went behind it: "Open a folder first — a Claude Code
  // session belongs to one." It does not. For an operator who works without a folder — which is how
  // the one who found this works — that command had never once worked.
  assert.deepStrictEqual(foldersToSearch([], 'C:\\Users\\strug'), ['C:\\Users\\strug']);

  // And a window that HAS folders is scoped to them: two projects may legitimately hold a
  // conversation by the same name, and the folder is what tells them apart.
  assert.deepStrictEqual(
    foldersToSearch(['D:\\rsd\\app'], 'C:\\Users\\strug'), ['D:\\rsd\\app']);
  assert.deepStrictEqual(foldersToSearch(['a', 'b'], 'home'), ['a', 'b']);
});

test('the page can ask a handover to start somewhere, and cannot ask for a position that is not one', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'carryFrom', at: 4 }), { kind: 'carryFrom', at: 4 });
  assert.deepStrictEqual(chatCommandOf({ type: 'carryFrom', at: 0 }), { kind: 'carryFrom', at: 0 });

  // A NEGATIVE would reach `slice` as an offset from the END and carry the last message instead of
  // the suffix — this feature as its own inverse — so it is not a position and nothing happens.
  for (const bad of [-1, 1.5, Number.NaN, Number.POSITIVE_INFINITY, '2', null, undefined]) {
    assert.deepStrictEqual(
      chatCommandOf({ type: 'carryFrom', at: bad }),
      { kind: 'ignore' },
      `${String(bad)} was accepted as a place to start a handover`,
    );
  }
});

test('the page can say WHICH half of the instruction left the box, and nothing else', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'markGone', which: 'task' }), { kind: 'markGone', which: 'task' });
  assert.deepStrictEqual(chatCommandOf({ type: 'markGone', which: 'role' }), { kind: 'markGone', which: 'role' });

  // There are two buttons and two halves. Anything else names neither, so it is a press of nothing.
  for (const bad of ['prompt', 'model', '', 1, null, undefined]) {
    assert.deepStrictEqual(
      chatCommandOf({ type: 'markGone', which: bad }),
      { kind: 'ignore' },
      `${String(bad)} was accepted as half of an instruction`,
    );
  }
});

/**
 * A conversation Claude Code names TWICE — and this build could only read one of the names.
 *
 * <p>The operator, with the session open in the editor group beside the refusal: *«сесия точно
 * есть»*. It was. Claude Code writes `{"type":"custom-title","customTitle":"…"}` as well as
 * `ai-title`, and `custom-title` is what a hand-renamed tab wears; the word appeared nowhere in this
 * repository. Measured on their own machine the same evening, over 101 sessions and 1.2 GB: four
 * carry a custom title and no ai-title at all, five carry both and the last of each disagree, and
 * not one of 162 distinct titles is claimed by two sessions.</p>
 *
 * <p>The second half is theirs too — *«я переименовал вручную, оно изменило, а потом через пару
 * минут вернуло старые названия»*. Their file says so: the rename at line 1281 spells `sessionId`
 * before `customTitle` and the rows around it spell it after, so a second writer re-asserts the old
 * title minutes later. A conversation must therefore answer to the name it wears now AND the ones it
 * used to — with the CURRENT name winning, or a session that used to be called something outranks
 * the one called that today. (gemini, the plan round.)</p>
 */

/** A row that carries the working directory a session was written in — what a folder is asked for. */
const saidIn = (cwd: string): string => JSON.stringify({ type: 'user', cwd, message: { content: [] } });

const renamed = (name: string): string =>
  JSON.stringify({ type: 'custom-title', sessionId: 's', customTitle: name });

test('a conversation renamed by hand is found under the name it now wears', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(
      join(dir, 'one.jsonl'),
      `${titled('Bug detection pipeline')}\n${renamed('coai 7 issues')}\n${said('мой вопрос')}\n`,
      'utf8',
    );

    const found = await sessionFileIn(home, 'D:\\work\\app', true, 'coai 7 issues');

    assert.strictEqual(found.kind, 'one',
      'the session the person renamed by hand was reported as not existing');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a conversation renamed and then reverted is found under BOTH names', async () => {
  // The three rows off the operator's own file, in their order. Whichever name is on the tab at the
  // moment the button is pressed, it is one of these two — and "the last title" answers neither
  // reliably, because the writer that re-asserts the old one runs on every turn.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(
      join(dir, 'one.jsonl'),
      `${renamed('/feature-dev:feature-dev выбери 7 самых простых')}\n`
      + `${renamed('coai 7 issues')}\n`
      + `${renamed('/feature-dev:feature-dev выбери 7 самых простых')}\n${said('мой вопрос')}\n`,
      'utf8',
    );

    const byTheNew = await sessionFileIn(home, 'D:\\work\\app', true, 'coai 7 issues');
    const byTheOld = await sessionFileIn(home, 'D:\\work\\app', true, '/feature-dev:feature-dev выбери 7 самых простых');

    assert.strictEqual(byTheNew.kind, 'one', 'the name the person gave it found nothing');
    assert.strictEqual(byTheOld.kind, 'one', 'the name Claude Code put back found nothing');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session named only by hand — no ai-title anywhere in it — is found', async () => {
  // Four of the operator's 101 sessions are this, and they were invisible: `titleOf` answered with
  // an empty string and the refusal said no session was called that.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${renamed('роль 2 еррор')}\n${said('что не так')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\work\\app', true, 'роль 2 еррор');
    assert.strictEqual(found.kind, 'one', 'a session with no ai-title could not be found at all');

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['что не так']);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session CURRENTLY called this beats one that was once called this', async () => {
  // The cost of matching every name a session ever had, and the rule that pays it: a conversation
  // the person has moved on from must not outrank the one they are looking at. Refusing both would
  // be worse — it would make a reused name permanently unanswerable.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'past.jsonl'), `${renamed('Refactor X')}\n${renamed('Something else')}\n${said('the old one')}\n`, 'utf8');
    writeFileSync(join(dir, 'now.jsonl'), `${renamed('Refactor X')}\n${said('the one in front of them')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\work\\app', true, 'Refactor X');
    assert.strictEqual(found.kind, 'one', 'a name worn by one session today was reported as ambiguous');

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['the one in front of them'],
      'the tab was handed the session that USED to wear this name');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('two sessions that BOTH once wore this name are still a refusal, never a pick', async () => {
  // The rule this loosening could have broken. Neither wears it now, both wore it once, and there is
  // nothing to choose between them — so nothing is chosen.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${renamed('Shared')}\n${renamed('One')}\n${said('the first')}\n`, 'utf8');
    writeFileSync(join(dir, 'two.jsonl'), `${renamed('Shared')}\n${renamed('Two')}\n${said('the second')}\n`, 'utf8');

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'Shared');

    assert.strictEqual(answer.kind, 'several', 'a name two sessions once wore was handed to one of them');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a stale title re-asserted over a rename leaves a refusal, never the wrong conversation', async () => {
  // codex, the code round, on the version of this that called the last row the CURRENT name: the
  // writer that puts an old title back runs on every turn, so a session's real name can sit in the
  // second tier while a namesake sits there too. What must NOT happen is a pick. The safe direction
  // is the one this module has always taken — and the session id and the picker are what recover
  // from it, which is stories A2 and B.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    // Renamed to New, and then Claude Code put Old back under it.
    writeFileSync(join(dir, 'theirs.jsonl'), `${renamed('New')}\n${renamed('Old')}\n${said('the one they mean')}\n`, 'utf8');
    // Another conversation that wore New once, long ago.
    writeFileSync(join(dir, 'other.jsonl'), `${renamed('New')}\n${renamed('Something else')}\n${said('not this one')}\n`, 'utf8');

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'New');

    assert.strictEqual(answer.kind, 'several',
      'a name sitting in the second tier for two sessions was handed to one of them');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a WHOLE name answers before a name that merely starts the same way', async () => {
  // A truncated tab is a prefix of everything that begins with it, and one of those is the session
  // called exactly what the tab could show. Refusing that pair as ambiguous is an ambiguity nobody
  // has. (gemini, the code round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'whole.jsonl'), `${titled('Bug fix')}\n${said('the exact one')}\n`, 'utf8');
    writeFileSync(join(dir, 'longer.jsonl'), `${titled('Bug fix pipeline')}\n${said('the longer one')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\work\\app', true, 'Bug fix…');
    assert.strictEqual(found.kind, 'one', 'a session named exactly what the tab shows was refused as ambiguous');

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['the exact one']);

    // And a real ambiguity is still refused: two names that only START the same, and no whole name.
    const both = await sessionFileIn(home, 'D:\\work\\app', true, 'Bug f…');
    assert.strictEqual(both.kind, 'several', 'two prefixes and no whole name were picked between');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a title row carrying nothing but spaces is not a name, and does not demote a real one', async () => {
  // A blank row counted as a name takes the LATEST place and pushes the name the person gave the
  // conversation down into the earlier tier — where a namesake can reach it and the pair is refused.
  // (gemini, the code round.) The harm needs the namesake to be visible at all, which is why this
  // fixture has one.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${renamed('A real name')}\n${renamed('   ')}\n${said('hello')}\n`, 'utf8');
    writeFileSync(join(dir, 'two.jsonl'), `${renamed('A real name')}\n${renamed('Moved on')}\n${said('not this')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\work\\app', true, 'A real name');
    assert.strictEqual(found.kind, 'one', 'a blank title row displaced the name the person gave it');

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['hello']);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

/**
 * THE ID, not the name — what a conversation that was pinned once already knows about its session.
 *
 * <p>A conversation keeps `source: {kind:'claude', sessionId}`, and that id IS the file's name. The
 * store has held it since the day the pin was built, and nothing used it to find the file: a reload
 * empties the remembered path and the next press walks the folder by a title another program is
 * rewriting underneath it. So the tab was hunted by a string while its identity sat on the record.</p>
 */

test('a conversation with a stored id finds its file though every title differs', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    const id = '942e84d6-1eff-4c68-ad29-642ac4b90e9a';
    writeFileSync(join(dir, `${id}.jsonl`), `${titled('Nothing like the tab')}\n${said('мой вопрос')}\n`, 'utf8');

    const found = await sessionFileOf(home, 'D:\\work\\app', true, id);
    assert.strictEqual(found.kind, 'one', 'a conversation whose file we can NAME was not found');
    assert.strictEqual(found.kind === 'one' ? found.file : '', join(dir, `${id}.jsonl`));

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['мой вопрос']);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('resolving by id reads no transcript at all', async () => {
  // The point of the id path is that it costs one look at the directory entry. A file whose contents
  // are not lines of JSON — and would produce nothing at all if they were read — still resolves.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    const id = '11111111-2222-3333-4444-555555555555';
    writeFileSync(join(dir, `${id}.jsonl`), '\u0000\u0001 not a transcript at all \u0000', 'utf8');

    const found = await sessionFileOf(home, 'D:\\work\\app', true, id);

    assert.strictEqual(found.kind, 'one', 'the id path read the file instead of naming it');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a stored id that does not look like one never becomes a path', async () => {
  // A record is a file a person or another program can edit, and this turns a stored string back
  // into a path. `..\outside.jsonl` joined to the project directory is a file OUTSIDE it, and an
  // access check would confirm it exists quite happily. (codex, the plan round, as a security
  // finding.) The shape is asked before the disk is touched, and containment after.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    const elsewhere = join(home, '.claude', 'projects', 'D--somebody-else');
    mkdirSync(dir, { recursive: true });
    mkdirSync(elsewhere, { recursive: true });
    writeFileSync(join(elsewhere, 'outside.jsonl'), `${said('another person’s conversation')}\n`, 'utf8');

    const refused = [
      '../D--somebody-else/outside',
      '..\\D--somebody-else\\outside',
      'D--somebody-else/outside',
      '11111111-2222-3333-4444-555555555555/../../D--somebody-else/outside',
      '',
      'outside',
    ];
    for (const id of refused) {
      const answer = await sessionFileOf(home, 'D:\\work\\app', true, id);

      assert.strictEqual(answer.kind, 'none', `an id of another shape became a path: ${id}`);
      assert.doesNotMatch(
        answer.kind === 'none' ? answer.refusal : '',
        /somebody-else/,
        'the refusal repeated a path outside the project directory back at the caller',
      );
    }
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('an id whose file is gone says so, and the name is still there to fall back on', async () => {
  // The two halves of the fallback, as the host composes them: the id misses because the session was
  // deleted, and the tab's name still reaches the one session that answers to it. Without this, a
  // conversation whose id went stale would sit on the id's refusal for ever. (codex, the plan round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jsonl'), `${titled('The tab')}\n${said('here')}\n`, 'utf8');

    const stale = await sessionFileOf(home, 'D:\\work\\app', true, '942e84d6-1eff-4c68-ad29-642ac4b90e9a');
    assert.strictEqual(stale.kind, 'none', 'a session id whose file is gone answered as though it were there');
    assert.match(stale.kind === 'none' ? stale.refusal : '', /D--work-app/, 'the refusal does not say where it looked');

    const byName = await sessionFileIn(home, 'D:\\work\\app', true, 'The tab');
    assert.strictEqual(byName.kind, 'one', 'the name could not answer after the id missed');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

/**
 * THE BUDGET. What a folder ten times this one costs, and what is said when it is not all read.
 *
 * <p>The operator's own folder is 101 sessions and 1.2 GB, and reading every title in it takes 5.2 s
 * warm. Nothing bounded that. A folder ten times the size is a minute behind a button, and the
 * sentence at the end of it would have been "no session is called that" — which would not be true,
 * because most of them were never looked at. (local and gemini, the plan round, on a plan that
 * created something that grows and did not name its budget.)</p>
 *
 * <p>The clock is INJECTED. A test that waits ten seconds to prove a ten-second deadline is a test
 * nobody runs twice.</p>
 */

const manySessions = (dir: string, howMany: number, named: (n: number) => string): void => {
  for (let n = 0; n < howMany; n += 1) {
    writeFileSync(join(dir, `s${n}.jsonl`), `${titled(named(n))}\n${said(`in ${n}`)}\n`, 'utf8');
    // Newest first is what the reader trusts: give each file a distinct, decreasing age.
    const when = new Date(Date.now() - n * 60_000);
    utimesSync(join(dir, `s${n}.jsonl`), when, when);
  }
};

test('a folder larger than the budget is read newest-first, and the cut is NAMED', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    manySessions(dir, 5, (n) => (n === 4 ? 'The oldest' : `Session ${n}`));

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'The oldest', { most: 3, withinMs: 10_000, mostBytes: SCAN_BUDGET.mostBytes });

    assert.strictEqual(answer.kind, 'none', 'a session outside the budget was found, so nothing was bounded');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /newest 3 of 5/u,
      'the refusal claims no session is called that, without saying that most were never read');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a match INSIDE the budget is still a match', async () => {
  // The cap must not turn into a refusal of everything. Newest first is what makes this safe: the
  // conversation somebody is looking at is the one that was written to most recently.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    manySessions(dir, 5, (n) => `Session ${n}`);

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'Session 1', { most: 3, withinMs: 10_000, mostBytes: SCAN_BUDGET.mostBytes });

    assert.strictEqual(answer.kind, 'one', 'a session well inside the budget was refused');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a walk that runs out of TIME stops, and says that is why', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    manySessions(dir, 5, (n) => (n === 4 ? 'The oldest' : `Session ${n}`));

    // Four seconds per file against a ten-second deadline: three files, then it stops.
    let clock = 0;
    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'The oldest', {
      most: 1_000,
      withinMs: 10_000,
      mostBytes: SCAN_BUDGET.mostBytes,
      now: () => {
        clock += 4_000;

        return clock;
      },
    });

    assert.strictEqual(answer.kind, 'none');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /in 10 s|within 10 s/u,
      'a walk that ran out of time did not say so');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});


test('a session file that LEADS outside the folder is refused, however it is spelled', async () => {
  // Lexical containment says nothing about a link. Anything on this machine can drop a file named
  // like a session id into the project directory pointing at something else, and both `resolve` and
  // `access` are perfectly happy with it. (codex, the plan round, as a security finding.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    const elsewhere = join(home, 'secrets');
    mkdirSync(dir, { recursive: true });
    mkdirSync(elsewhere, { recursive: true });
    const secret = join(elsewhere, 'notes.jsonl');
    writeFileSync(secret, `${said('somebody else’s words')}\n`, 'utf8');
    const id = '11111111-2222-3333-4444-555555555555';
    let linked = true;
    try {
      symlinkSync(secret, join(dir, `${id}.jsonl`), 'file');
    } catch {
      // Windows without developer mode refuses a symlink to an unprivileged process (EPERM,
      // measured here). This leg cannot run, and the test below covers the same decision as data.
      linked = false;
    }
    if (linked) {
      const answer = await sessionFileOf(home, 'D:\\work\\app', true, id);

      assert.strictEqual(answer.kind, 'none', 'a link out of the project directory was followed');
      assert.doesNotMatch(answer.kind === 'none' ? answer.refusal : '', /secrets/u,
        'the refusal repeated the path the link led to');
    }
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a refusal about a stored id repeats neither the id nor a path built from it', async () => {
  // A refusal that echoes what it was given is a refusal somebody can probe with. (codex, the plan
  // round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });

    const answer = await sessionFileOf(home, 'D:\\work\\app', true, '../../etc/passwd');

    assert.strictEqual(answer.kind, 'none');
    const refusal = answer.kind === 'none' ? answer.refusal : '';
    assert.doesNotMatch(refusal, /passwd|\.\./u, 'the refusal says back the string it was asked to refuse');
    assert.match(refusal, /shape this build knows/u, 'the refusal does not say what was wrong');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session too big to read under the budget is SKIPPED and counted, never silently dropped', async () => {
  // The hole three reviewers found in the first budget: it bounded how many files were opened and
  // said nothing about how big one of them could be, so a single pathological session could overrun
  // the whole deadline inside one read — and the sentence at the end would have claimed the folder
  // held nothing by that name.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'huge.jsonl'), `${titled('The one being looked for')}\n${'x'.repeat(4_096)}\n`, 'utf8');

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'The one being looked for', {
      most: 250,
      withinMs: 10_000,
      mostBytes: 1_024,
    });

    assert.strictEqual(answer.kind, 'none', 'a file past the byte budget was read anyway');
    assert.match(answer.kind === 'none' ? answer.refusal : '', /small enough to read/u,
      'a skipped file was reported as a session that is not called that');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session file is inside the folder only if it LEADS there too', () => {
  // The decision as data, because the case it exists for cannot be built on this machine: Windows
  // refuses a symlink to an unprivileged process (EPERM, measured), so the filesystem leg above
  // passes by doing nothing here. A skip is not a pass.
  const dir = join('C:', 'Users', 'me', '.claude', 'projects', 'D--work-app');
  const file = join(dir, '11111111-2222-3333-4444-555555555555.jsonl');

  assert.strictEqual(staysInside(dir, file, { dir, file }), true, 'an ordinary session file was refused');

  // Spelled inside, leads out: the link case, which the lexical half cannot see.
  const secret = join('C:', 'Users', 'me', 'secrets', 'notes.jsonl');
  assert.strictEqual(staysInside(dir, file, { dir, file: secret }), false, 'a link out of the folder was followed');

  // Spelled out, however it leads: refused by the lexical half, which is asked first and costs nothing.
  const outside = join('C:', 'Users', 'me', '.claude', 'projects', 'D--somebody-else', 'x.jsonl');
  assert.strictEqual(staysInside(dir, outside, { dir, file: outside }), false, 'a path outside the folder was accepted');

  // And the directory itself is not a file inside itself.
  assert.strictEqual(staysInside(dir, dir, { dir, file: dir }), false, 'the folder was accepted as a session in it');
});

test('a match found inside a CUT scan is answered but never called complete', async () => {
  // The cut's real cost, and the reason `Found.one` carries a flag: with 251 sessions and the name
  // in two of them, the first match would be adopted for ever and the namesake refusal defeated by
  // a budget nobody saw. The answer still comes back — the person gets their conversation — and the
  // caller is told it was not proved unique. (codex, the code round, twice.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    manySessions(dir, 4, (n) => (n === 0 ? 'The one wanted' : `Session ${n}`));

    const cut = await sessionFileIn(home, 'D:\\work\\app', true, 'The one wanted', {
      most: 2,
      withinMs: 10_000,
      mostBytes: SCAN_BUDGET.mostBytes,
    });
    assert.strictEqual(cut.kind, 'one', 'a match inside the budget was refused');
    assert.strictEqual(cut.kind === 'one' ? cut.complete : true, false,
      'a match from a scan that read two of four sessions was reported as proven unique');

    const whole = await sessionFileIn(home, 'D:\\work\\app', true, 'The one wanted');
    assert.strictEqual(whole.kind === 'one' ? whole.complete : false, true,
      'a scan that read the whole folder was reported as incomplete');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session file that will not open is a FAILURE, not a session with nothing in it', async () => {
  // The waiting question used to read each file with `readFile` and had this answer for free; it
  // became a stream in this story and silently lost it, which would have reported a locked or
  // unreadable session as one holding no question. A directory wearing a session's name is the
  // shape that reproduces it on every platform. (codex, the code round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    mkdirSync(join(dir, 'not-a-file.jsonl'));

    const answer = await waitingQuestion(home, 'D:\\work\\app', true, '');

    assert.strictEqual(answer.kind, 'failed', 'a session that could not be read was reported as silence');
    assert.match(answer.kind === 'failed' ? answer.refusal : '', /could not be read/u, 'the refusal does not say what happened');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('the waiting question’s deadline covers the READING, not only the choosing', async () => {
  // The regression this story introduced and the round caught: the walk picked files under the
  // clock and a second loop read them with no clock in it, so the time limit bounded the choosing
  // and none of the work. (gemini, the code round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    manySessions(dir, 4, (n) => `Session ${n}`);

    let clock = 0;
    const answer = await waitingQuestion(home, 'D:\\work\\app', true, '', {
      most: 1_000,
      withinMs: 10_000,
      mostBytes: SCAN_BUDGET.mostBytes,
      now: () => {
        clock += 4_000;

        return clock;
      },
    });

    assert.strictEqual(answer.kind, 'failed', 'a walk that ran out of time reported that nothing was waiting');
    assert.match(answer.kind === 'failed' ? answer.refusal : '', /within 10 s/u, 'the refusal does not say the time ran out');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a folder that would not LIST says so as a fact, not as an absence', async () => {
  // The fact `walkFailed` rests on, at the boundary that produces it. A thrown walk is not the only
  // way a walk fails: a directory that would not list comes back as an ordinary `none`, so a caller
  // asking only "did it throw" would report "no session is called that" about a folder nobody could
  // read. A FILE where the project directory should be is the shape that reproduces it on every
  // platform — `readdir` answers ENOTDIR. (codex, the plan round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const root = join(home, '.claude', 'projects');
    mkdirSync(root, { recursive: true });
    writeFileSync(join(root, projectDirName('D:\\work\\app')), 'not a directory', 'utf8');

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'Anything');

    assert.strictEqual(answer.kind, 'none');
    assert.strictEqual(answer.kind === 'none' ? answer.why : '', 'unreadable',
      'a folder that could not be listed was reported as one holding no session of that name');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a folder that answered and holds nothing of that name says THAT, as a fact', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', projectDirName('D:\\work\\app'));
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${titled('Something else')}\n`, 'utf8');

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'Anything');

    assert.strictEqual(answer.kind === 'none' ? answer.why : '', 'unmatched',
      'a folder that answered perfectly well is carried as one that would not');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a walk cut short by its budget is carried as UNREADABLE, not as an absence', async () => {
  // A cut folder was not finished, which is nearer to "it would not say" than to "nothing is called
  // that" — and *go to* must not offer to start a second conversation on the strength of sessions it
  // never opened.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', projectDirName('D:\\work\\app'));
    mkdirSync(dir, { recursive: true });
    manySessions(dir, 4, (n) => `Session ${n}`);

    const answer = await sessionFileIn(home, 'D:\\work\\app', true, 'Nothing here', {
      most: 2,
      withinMs: 10_000,
      mostBytes: SCAN_BUDGET.mostBytes,
    });

    assert.strictEqual(answer.kind === 'none' ? answer.why : '', 'unreadable',
      'a folder that was only half read was reported as one that holds no such session');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

/**
 * WHAT CLAUDE CODE REALLY CALLS THE FOLDER — measured, not read off three separators.
 *
 * <p>The rule here was `[\\/:]` and it was verified, honestly, against a real `~/.claude/projects`
 * — on a machine whose paths happen to contain nothing else. A person on a Mac found the rest of it:
 * a repository called `dew_flow_payroll` has its sessions under `…-dew-flow-payroll`, and the
 * extension looked for `…-dew_flow_payroll` and said no session existed.</p>
 *
 * <p>Measured on 84 pairs of (the `cwd` a transcript records, the folder that transcript is in) from
 * this machine on 2026-09-17: the shipped rule was right for 51 of them, and replacing everything
 * that is not a letter or a digit is right for all 84. The characters actually seen changing were
 * `\` `:` `.` and `_` — the last two are what the shipped rule missed, and between them they cover
 * every `dew_flow_*` repository here and every temp folder with a dotted suffix.</p>
 */
test('every character that is not a letter or a digit becomes a dash', () => {
  // The pair that started it, from the report: a repo with underscores in its name.
  assert.strictEqual(projectDirName('/Users/mark/Desktop/Development/RSDPay/dew_flow_payroll'),
    '-Users-mark-Desktop-Development-RSDPay-dew-flow-payroll',
    'a repository with underscores in its path is looked for under a name Claude Code never uses');

  // And on this machine, measured: D:\rsd\dew_flow_benchmark really is D--rsd-dew-flow-benchmark.
  assert.strictEqual(projectDirName('D:\\rsd\\dew_flow_benchmark'), 'D--rsd-dew-flow-benchmark');

  // A DOT too, which the report did not mention and the measurement found: 33 of the 84 folders on
  // this machine are temp directories whose dotted suffix the shipped rule kept.
  assert.strictEqual(projectDirName('C:\\Users\\me\\AppData\\Local\\Temp\\coai-noworkspace-4lxvwi4g.1op'),
    'C--Users-me-AppData-Local-Temp-coai-noworkspace-4lxvwi4g-1op');

  // The three the rule always had are unchanged, so the old cases stay covered.
  assert.strictEqual(projectDirName('D:\\rsd\\ClaudeRag'), 'D--rsd-ClaudeRag');
  assert.strictEqual(projectDirName('/home/me/work/app'), '-home-me-work-app');
});

test('a folder spelled the OLD way is still found, because the rule is somebody else’s', async () => {
  // The rule is Anthropic's, undocumented, and measured on one machine — so a folder written by a
  // version that spelled it the old way is looked for as well, rather than the person being told
  // their sessions do not exist. Exact first, old spelling second, and the case-blind pass after
  // both; two names derived from one path cannot collide.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const root = join(home, '.claude', 'projects');
    const oldWay = join(root, 'D--rsd-dew_flow_benchmark');
    mkdirSync(oldWay, { recursive: true });
    writeFileSync(join(oldWay, 'one.jsonl'), `${titled('An older folder')}\n${said('still here')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\rsd\\dew_flow_benchmark', true, 'An older folder');

    assert.strictEqual(found.kind, 'one',
      'a folder written under the older spelling was reported as no sessions at all');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('each character the rule touches is pinned on its own, not as one happy path', () => {
  // A table rather than a sentence, because "84 of 84 pairs" is an inspection that happened once and
  // this is the thing that runs. Every character the measurement saw changing gets its own line, and
  // the three the old rule already handled get theirs, so a later edit that fixes one and breaks
  // another cannot pass. (codex, the plan round.)
  const cases: readonly (readonly [string, string])[] = [
    ['a_b', 'a-b'],
    ['a.b', 'a-b'],
    ['a\\b', 'a-b'],
    ['a/b', 'a-b'],
    ['a:b', 'a-b'],
    ['a b', 'a-b'],
    ['a-b', 'a-b'],
    ['abc123', 'abc123'],
    // No collapsing: a drive letter really does become TWO dashes, measured on this machine.
    ['D:\\rsd', 'D--rsd'],
  ];
  for (const [path, expected] of cases) {
    assert.strictEqual(projectDirName(path), expected, `“${path}” is not spelled the way Claude Code spells it`);
  }
});

test('a folder named in another alphabet is looked for BOTH ways', () => {
  // The one thing the measurement cannot settle: no path on this machine has a letter outside A–Z,
  // so whether Claude Code keeps a Cyrillic letter or dashes it is unknown. Both spellings are
  // looked for, which is why the unknown costs nothing. (gemini, the plan round.)
  const spellings = projectDirNames('D:\\rsd\\проект_один');

  assert.ok(spellings.includes('D--rsd-проект-один'), 'a folder that keeps its letters is not looked for');
  // Every letter of 'проект_один' dashed: eleven characters, eleven dashes.
  assert.ok(spellings.includes(`D--rsd-${'-'.repeat(11)}`),
    'a folder whose letters were dashed is not looked for');
  assert.ok(spellings.includes('D--rsd-проект_один'), 'the spelling this extension used until today is not looked for');
});

test('an EMPTY folder under the new name does not shadow the old one that holds the sessions', async () => {
  // An upgrade leaves the old folder where it was and Claude Code starts writing the new one, so both
  // exist and the preferred one can be empty. Answering with it would keep the very refusal this
  // change is about. (gemini and codex, the plan round, independently.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const root = join(home, '.claude', 'projects');
    mkdirSync(join(root, 'D--rsd-dew-flow-benchmark'), { recursive: true });
    const older = join(root, 'D--rsd-dew_flow_benchmark');
    mkdirSync(older, { recursive: true });
    writeFileSync(join(older, 'one.jsonl'), `${titled('Where the sessions really are')}\n${said('here')}\n`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\rsd\\dew_flow_benchmark', true, 'Where the sessions really are');

    assert.strictEqual(found.kind, 'one',
      'an empty folder under the newer spelling hid the transcripts in the older one');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('Take the question reads the same folder rule as the Asked button', async () => {
  // Both go through one resolver, and this is what says so from the outside: the second flow, on a
  // path with an underscore, which is the shape that was broken.
  //
  // It asserts the QUESTION, not merely "did not fail". The first version wrote a title and accepted
  // anything but a failure — so a folder holding no question would have passed it, which is a fixture
  // the code rejects proving nothing. (codex, the code round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--rsd-dew-flow-benchmark');
    mkdirSync(dir, { recursive: true });
    const asking = JSON.stringify({
      type: 'assistant',
      sessionId: 'session-1',
      timestamp: '2026-09-17T09:00:00.000Z',
      message: {
        content: [{
          type: 'tool_use',
          id: 'q1',
          name: 'AskUserQuestion',
          input: {
            questions: [{
              header: 'Scope',
              question: 'Which folder is this?',
              multiSelect: false,
              options: [{ label: 'This one', description: 'the underscore path' }],
            }],
          },
        }],
      },
    });
    writeFileSync(join(dir, 'one.jsonl'), `${saidIn('D:\\rsd\\dew_flow_benchmark')}
${titled('A waiting one')}
${asking}
`, 'utf8');

    const answer = await waitingQuestion(home, 'D:\\rsd\\dew_flow_benchmark', true, '');

    assert.strictEqual(answer.kind, 'one',
      'Take the question still cannot find the folder for a path with an underscore');
    assert.strictEqual(
      answer.kind === 'one' ? answer.session.asked.questions[0]?.question : '',
      'Which folder is this?',
      'the question that was found is not the one in that folder',
    );
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a folder is this project’s because a transcript in it SAYS so, not because it has one', async () => {
  // `D:\rsd\foo_bar` and `D:\rsd\foo-bar` are two checkouts with ONE folder name between them.
  // Taking the first candidate that holds any transcript hands over the other project's
  // conversations — the silent cross-project hand-over this module exists to refuse. (codex, the
  // code round.)
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const root = join(home, '.claude', 'projects');

    // The preferred spelling for BOTH paths, holding the OTHER checkout's session.
    const shared = join(root, 'D--rsd-foo-bar');
    mkdirSync(shared, { recursive: true });
    writeFileSync(join(shared, 'theirs.jsonl'),
      `${saidIn('D:\\rsd\\foo-bar')}
${titled('Shared name')}
${said('not your conversation')}
`, 'utf8');

    // The older spelling, holding the one actually asked for.
    const mine = join(root, 'D--rsd-foo_bar');
    mkdirSync(mine, { recursive: true });
    writeFileSync(join(mine, 'mine.jsonl'),
      `${saidIn('D:\\rsd\\foo_bar')}
${titled('Shared name')}
${said('your conversation')}
`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\rsd\\foo_bar', true, 'Shared name');
    assert.strictEqual(found.kind, 'one', 'the session of this checkout was not found at all');

    const read = await promptsFrom(found.kind === 'one' ? found.file : '');
    assert.deepStrictEqual(read.kind === 'said' ? read.said : [], ['your conversation'],
      'another checkout’s conversation was handed over, because its folder was listed first');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('an empty file, and a directory wearing the extension, are not sessions', async () => {
  // Two more shapes the "has a .jsonl in it" test accepted: a half-written file left by a killed run,
  // and a folder somebody called `archive.jsonl`. Neither names a cwd, so neither is evidence.
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    const root = join(home, '.claude', 'projects');
    const preferred = join(root, 'D--rsd-dew-flow-bench');
    mkdirSync(join(preferred, 'archive.jsonl'), { recursive: true });
    writeFileSync(join(preferred, 'half-written.jsonl'), '{"type":"user","mes', 'utf8');

    const older = join(root, 'D--rsd-dew_flow_bench');
    mkdirSync(older, { recursive: true });
    writeFileSync(join(older, 'one.jsonl'),
      `${saidIn('D:\\rsd\\dew_flow_bench')}
${titled('The real one')}
${said('here')}
`, 'utf8');

    const found = await sessionFileIn(home, 'D:\\rsd\\dew_flow_bench', true, 'The real one');

    assert.strictEqual(found.kind, 'one',
      'an empty file or a directory named like one shadowed the folder that holds the sessions');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});
