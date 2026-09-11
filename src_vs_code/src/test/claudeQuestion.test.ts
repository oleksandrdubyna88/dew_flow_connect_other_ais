import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync, utimesSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { AskedSet, askedAsText, lastAsked } from '../claudeQuestion';
import {
  WaitingSession,
  projectDirIn,
  projectDirName,
  waitingIn,
  waitingQuestion,
} from '../claudeSessions';
import { CLAUDE_PANEL_VIEW_TYPE, isOrdinaryEditorTab, sourceSession } from '../sessionKey';

/**
 * Taking the question Claude Code is asking, off its own session file.
 *
 * <p>The fixtures are the shape a real file has, measured against one on this machine rather than
 * imagined: an assistant row whose `message.content` carries a `tool_use` named `AskUserQuestion`,
 * and a later row whose `tool_result` names that `tool_use_id`.</p>
 */

const asks = (id: string, questions: unknown, extra: Record<string, unknown> = {}): string => JSON.stringify({
  type: 'assistant',
  sessionId: 'session-1',
  timestamp: '2026-09-11T18:00:00.000Z',
  message: { content: [{ type: 'tool_use', id, name: 'AskUserQuestion', input: { questions } }] },
  ...extra,
});

const answers = (id: string): string => JSON.stringify({
  type: 'user',
  message: { content: [{ type: 'tool_result', tool_use_id: id, content: 'the answer' }] },
});

/** The question a file holds, for the tests that are about the question rather than the answer shape. */
const asked = (lines: readonly string[]): AskedSet | undefined => {
  const found = lastAsked(lines);

  return found.kind === 'asked' ? found.set : undefined;
};

const ONE = [{
  header: 'Scope',
  question: 'How far should this go?',
  multiSelect: false,
  options: [
    { label: 'All of it', description: 'every part in one pass' },
    { label: 'Just the first', description: 'and stop' },
  ],
}];

// ---------------------------------------------------------------------------------------------
// Reading the file.
// ---------------------------------------------------------------------------------------------

test('the last question in the file is the one taken', () => {
  const found = asked([asks('a', ONE), asks('b', [{ ...ONE[0], question: 'And then?' }])]);

  assert.strictEqual(found?.id, 'b', 'an older question won');
  assert.strictEqual(found?.questions[0]?.question, 'And then?');
  assert.strictEqual(found?.answered, false);
});

test('a question with an answer after it is reported ANSWERED, not handed over as live', () => {
  // Handing a second model a question that is already settled is the worst outcome this command
  // has: the answer comes back about a decision that was made ten minutes ago.
  const found = asked([asks('a', ONE), answers('a')]);

  assert.strictEqual(found?.answered, true, 'an answered question looked like one still waiting');
});

test('EVERY question in the block is read, not the first', () => {
  // `questions` holds four at a time often enough that taking the first would drop most of what
  // was asked — and silently. (codex, the plan round.)
  const four = [0, 1, 2, 3].map((n) => ({ ...ONE[0], question: `Question ${n}` }));
  const found = asked([asks('a', four)]);

  assert.strictEqual(found?.questions.length, 4, 'questions were dropped');
  assert.deepStrictEqual(found?.questions.map((one) => one.question), [
    'Question 0', 'Question 1', 'Question 2', 'Question 3',
  ], 'the order of the questions moved');
});

test('a half-written line costs that line and nothing else', () => {
  // The file is being appended to by another process while this reads it, so a truncated last line
  // is the ordinary case rather than a corrupt file.
  const found = asked([asks('a', ONE), '{"type":"assistant","message":{"cont', '', 'not json at all']);

  assert.strictEqual(found?.id, 'a', 'one bad line took the whole file with it');
});

test('a file with nothing asked in it is nothing, not an exception', () => {
  assert.strictEqual(lastAsked([]).kind, 'nothing');
  assert.strictEqual(lastAsked(['{"type":"user","message":{"content":[]}}']).kind, 'nothing');
});

test('a block that is not a question is not read as one', () => {
  const other = JSON.stringify({
    type: 'assistant',
    message: { content: [{ type: 'tool_use', id: 'x', name: 'Bash', input: { command: 'ls' } }] },
  });

  assert.strictEqual(lastAsked([other]).kind, 'nothing', 'another tool was taken for a question');
});

// ---------------------------------------------------------------------------------------------
// Rendering it.
// ---------------------------------------------------------------------------------------------

test('every option and every description reaches the passage', () => {
  const found = asked([asks('a', ONE)]);
  const text = askedAsText(found!);

  assert.match(text, /\[Scope\] How far should this go\?/, 'the header or the question is missing');
  assert.match(text, /All of it — every part in one pass/, 'an option lost its description');
  assert.match(text, /Just the first — and stop/, 'the second option is missing');
});

test('a question that takes more than one answer says so', () => {
  const many = asked([asks('a', [{ ...ONE[0], multiSelect: true }])]);
  const one = asked([asks('a', ONE)]);

  assert.match(askedAsText(many!), /more than one answer/, 'a multi-select question did not say so');
  assert.doesNotMatch(askedAsText(one!), /more than one answer/, 'a single-answer question said it was not');
});

test('an option with no description is still an option', () => {
  const bare = asked([asks('a', [{ ...ONE[0], options: [{ label: 'Yes' }] }])]);

  assert.match(askedAsText(bare!), /- Yes$/m, 'an option without a description was dropped or left a dangling dash');
});

// ---------------------------------------------------------------------------------------------
// WHICH session, and the refusal to guess.
// ---------------------------------------------------------------------------------------------

test('the project directory is the path with every separator replaced', () => {
  // Measured against a real ~/.claude/projects rather than assumed.
  assert.strictEqual(projectDirName('D:\\rsd\\ClaudeRag'), 'D--rsd-ClaudeRag');
  assert.strictEqual(projectDirName('/home/me/work/app'), '-home-me-work-app');
});

test('the directory is matched case-insensitively, because its case is not ours to predict', () => {
  const root = '/root';

  assert.strictEqual(projectDirIn(root, 'D:\\rsd\\ClaudeRag', ['D--rsd-ClaudeRag'], true), join(root, 'D--rsd-ClaudeRag'));
  assert.strictEqual(projectDirIn(root, 'D:\\rsd\\ClaudeRag', ['d--rsd-clauderag'], true), join(root, 'd--rsd-clauderag'));
  assert.strictEqual(projectDirIn(root, 'D:\\rsd\\ClaudeRag', ['something-else'], true), '', 'a stranger directory matched');
});

const session = (file: string, answered: boolean, at = '2026-09-11T18:00:00.000Z', title = ''): WaitingSession => ({
  file,
  asked: { id: file, questions: ONE, answered, sessionId: file, at, title },
});

test('one session waiting is the answer; two is a refusal, never a pick', () => {
  assert.deepStrictEqual(waitingIn([session('a', false)]).kind, 'one');
  // The host cannot see into Claude Code's webview, so it cannot tell which tab is being looked at.
  // A plausible guess here delivers somebody else's question, silently.
  assert.deepStrictEqual(waitingIn([session('a', false), session('b', false)]).kind, 'several');
});

test('nothing waiting is told apart from nothing asked', () => {
  assert.strictEqual(waitingIn([]).kind, 'none');
  assert.strictEqual(waitingIn([session('a', true)]).kind, 'answered');
});

test('the newest answered one is the one reported answered', () => {
  const found = waitingIn([
    session('old', true, '2026-09-11T10:00:00.000Z'),
    session('new', true, '2026-09-11T18:00:00.000Z'),
  ]);

  assert.strictEqual(found.kind === 'answered' ? found.session.file : '', 'new');
});

// ---------------------------------------------------------------------------------------------
// Against a real directory.
// ---------------------------------------------------------------------------------------------

test('a folder Claude Code has never run in is a refusal that names where it looked', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    const answer = await waitingQuestion(home, 'D:\\nowhere', true);

    assert.strictEqual(answer.kind, 'failed');
    assert.match(answer.kind === 'failed' ? answer.refusal : '', /projects/, 'the refusal does not say where it looked');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a real session file is found, read, and its question taken', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${asks('a', ONE)}\n`, 'utf8');
    const answer = await waitingQuestion(home, 'D:\\work\\app', true);

    assert.strictEqual(answer.kind, 'one', 'the question in the only session was not found');
    assert.match(
      answer.kind === 'one' ? askedAsText(answer.session.asked) : '',
      /How far should this go/,
      'the question did not survive the round trip',
    );
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('two sessions both waiting are refused by count, not resolved by mtime', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${asks('a', ONE)}\n`, 'utf8');
    writeFileSync(join(dir, 'two.jsonl'), `${asks('b', ONE)}\n`, 'utf8');
    // Make one clearly newer, so a mtime-based pick would have something to prefer.
    const later = Date.now() / 1000 + 60;
    utimesSync(join(dir, 'two.jsonl'), later, later);

    assert.strictEqual(
      (await waitingQuestion(home, 'D:\\work\\app', true)).kind,
      'several',
      'a session was picked for the person',
    );
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a case-sensitive filesystem does not take a namesake project for this one', () => {
  // On a case-sensitive host `/work/App` and `/work/app` are two different projects, and a loose
  // match there hands one project's question to the other. (codex, the code round, as a security
  // finding.)
  const root = '/root';

  assert.strictEqual(projectDirIn(root, '/work/App', ['-work-app'], false), '', 'a namesake project matched');
  assert.strictEqual(projectDirIn(root, '/work/App', ['-work-app'], true), join(root, '-work-app'));
  // And two names that differ only in case are an ambiguity, not a pick.
  assert.strictEqual(projectDirIn(root, '/work/App', ['-work-app', '-WORK-APP'], true), '');
});

test('a question still waiting wins over a later one that was answered', () => {
  // A question can be left open while the conversation goes on around it. Taking only the LAST one
  // reported "already answered" while the first still sat on screen. (codex, the code round.)
  const found = asked([asks('a', ONE), asks('b', ONE), answers('b')]);

  assert.strictEqual(found?.id, 'a', 'the question still waiting was passed over');
  assert.strictEqual(found?.answered, false);
});

test('when every question has been answered, the newest of them is the one reported', () => {
  const found = asked([asks('a', ONE), answers('a'), asks('b', ONE), answers('b')]);

  assert.strictEqual(found?.id, 'b');
  assert.strictEqual(found?.answered, true);
});

test('a tool_result with no id names nothing, and answers nothing', () => {
  const nameless = JSON.stringify({
    type: 'user',
    message: { content: [{ type: 'tool_result', content: 'x' }] },
  });
  const found = asked([asks('a', ONE), nameless]);

  assert.strictEqual(found?.answered, false, 'an answer that names no question answered one');
});

test('a question this build cannot read is NAMED, not counted as silence', () => {
  // If Anthropic moves a field, the command must say the format changed rather than report that
  // Claude is asking nothing while a question sits on screen. (codex, the code round.)
  const strange = JSON.stringify({
    type: 'assistant',
    message: { content: [{ type: 'tool_use', id: 'a', name: 'AskUserQuestion', input: { prompts: [] } }] },
  });

  assert.strictEqual(lastAsked([strange]).kind, 'unreadable', 'an unrecognised question looked like an empty file');
  assert.strictEqual(lastAsked([]).kind, 'nothing', 'an empty file looked like a format change');
});

test('two files of the same NAME do not inherit the other one\'s conversation', () => {
  // /a/README.md and /b/README.md share a label and are not the same document. The label fallback
  // belongs to Claude's panels, where a label is a session name somebody chose. (codex.)
  const gone = { key: {}, label: 'README.md' };
  const now = { key: {}, label: 'README.md', viewType: '', scheme: 'file' };

  assert.deepStrictEqual(
    sourceSession(now, [now], [gone], isOrdinaryEditorTab),
    { kind: 'new', key: now.key, label: 'README.md' },
    'a namesake file took over a conversation that belonged to another file',
  );
  // And the panel keeps the fallback it was given it for.
  const panel = { key: {}, label: 'main', viewType: CLAUDE_PANEL_VIEW_TYPE, scheme: '' };

  assert.strictEqual(
    sourceSession(panel, [panel], [{ key: {}, label: 'main' }])?.kind,
    'rekey',
    'a renamed Claude tab lost its conversation',
  );
});

test('a folder with no session directory of its own is refused BY NAME', async () => {
  // Each refusal sentence is the only signal a person gets, and every one is reachable from an
  // ordinary state of the world. (CodeRabbit, PR #206.)
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    mkdirSync(join(home, '.claude', 'projects'), { recursive: true });

    await waitingQuestion(home, 'D:\\work\\app', true).then((answer) => {
      assert.strictEqual(answer.kind, 'failed');
      assert.match(
        answer.kind === 'failed' ? answer.refusal : '',
        /D--work-app/,
        'the refusal does not name the directory it looked for',
      );
    });
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a session whose shape this build cannot read says the format changed', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    const strange = JSON.stringify({
      type: 'assistant',
      message: { content: [{ type: 'tool_use', id: 'a', name: 'AskUserQuestion', input: { prompts: [] } }] },
    });
    writeFileSync(join(dir, 'one.jsonl'), `${strange}\n`, 'utf8');

    await waitingQuestion(home, 'D:\\work\\app', true).then((answer) => {
      assert.strictEqual(answer.kind, 'failed', 'an unreadable shape was reported as silence');
      assert.match(
        answer.kind === 'failed' ? answer.refusal : '',
        /format has changed/,
        'the refusal does not say what is wrong',
      );
      assert.match(answer.kind === 'failed' ? answer.refusal : '', /one\.jsonl/, 'the refusal does not name the file');
    });
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a folder Claude has run in but never asked anything in is nothing, not a failure', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), '{"type":"user","message":{"content":[]}}\n', 'utf8');

    await waitingQuestion(home, 'D:\\work\\app', true).then((answer) => {
      assert.strictEqual(answer.kind, 'none', 'an empty session was reported as a failure');
    });
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('an answered session is reported answered, from a real directory', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-home-'));
  try {
    const dir = join(home, '.claude', 'projects', 'D--work-app');
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'one.jsonl'), `${asks('a', ONE)}\n${answers('a')}\n`, 'utf8');

    await waitingQuestion(home, 'D:\\work\\app', true).then((answer) => {
      assert.strictEqual(answer.kind, 'answered', 'a settled question was offered as one still waiting');
    });
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('the TAB decides between two sessions that are both waiting', () => {
  // There is no window id to ask for — but Claude Code writes the conversation's title into its
  // session file, and the tab shows that same title. Measured against a live session before this
  // was built: a tab reading 'Подключение к scoreMeter DB' has a row saying exactly that.
  const one = session('a', false, '2026-09-11T10:00:00.000Z', 'Подключение к scoreMeter DB');
  const two = session('b', false, '2026-09-11T18:00:00.000Z', 'Пул с несколькими PR');

  const found = waitingIn([one, two], 'Пул с несколькими PR');

  assert.strictEqual(found.kind, 'one', 'the tab could not name its own session');
  assert.strictEqual(found.kind === 'one' ? found.session.file : '', 'b');
});

test('a tab that names NEITHER of them still refuses, rather than picking the newer', () => {
  const one = session('a', false, '2026-09-11T10:00:00.000Z', 'One conversation');
  const two = session('b', false, '2026-09-11T18:00:00.000Z', 'Another conversation');

  assert.strictEqual(waitingIn([one, two], 'Something else entirely').kind, 'several');
  // And with no tab to ask — the command invoked from a file rather than from the panel — the
  // refusal is the same one it was before any of this.
  assert.strictEqual(waitingIn([one, two]).kind, 'several');
});

test('two sessions that share a title are still an ambiguity', () => {
  const one = session('a', false, '2026-09-11T10:00:00.000Z', 'Same name');
  const two = session('b', false, '2026-09-11T18:00:00.000Z', 'Same name');

  assert.strictEqual(waitingIn([one, two], 'Same name').kind, 'several', 'a namesake session was picked');
});

test('the title is read off the session, and the newest one wins', () => {
  // Claude Code refines the title as the conversation goes on, and the tab shows the newest.
  const titled = (name: string): string => JSON.stringify({ type: 'ai-title', aiTitle: name, sessionId: 's' });
  const found = asked([titled('First guess'), asks('a', ONE), titled('What it is really about')]);

  assert.strictEqual(found?.title, 'What it is really about', 'an older title named the tab');
  assert.strictEqual(asked([asks('a', ONE)])?.title, '', 'a session with no title row invented one');
});
