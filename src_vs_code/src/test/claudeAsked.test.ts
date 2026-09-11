import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { humanPrompts } from '../claudeQuestion';
import { promptsInSession } from '../claudeSessions';
import { chatCommandOf } from '../chatMessages';

/**
 * What the PERSON wrote, read back off the session file.
 *
 * <p>A chat window four hours old has lost the thing it is about — the question scrolled away and
 * the operator ends up asking Claude what it is working on. The words never went anywhere: Claude
 * Code writes every one of them to its own session file. These tests fix the two halves of getting
 * them back — which rows count as a person speaking, and which session belongs to this tab.</p>
 */

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

    assert.deepStrictEqual(found, ['the right one'], 'the tab was shown somebody else’s conversation');
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

    assert.deepStrictEqual(await promptsInSession(home, 'D:\\work\\app', true, 'Something else'), []);
    // And a tab with no name at all — a chat opened from a file — never even looks.
    assert.deepStrictEqual(await promptsInSession(home, 'D:\\work\\app', true, ''), []);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('a folder Claude has never run in is empty, not a throw', async () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-asked-'));
  try {
    assert.deepStrictEqual(await promptsInSession(home, 'D:\\work\\app', true, 'Anything'), []);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('the page can ask for it, and cannot ask for anything else by that name', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'showAsked' }), { kind: 'showAsked' });
  // And a page from an older build, asking for it the way nothing asks for it, is a press of nothing.
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'showAsked' }), { kind: 'ignore' });
});
