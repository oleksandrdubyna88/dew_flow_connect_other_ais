import assert from 'node:assert/strict';
import { test } from 'node:test';
import { agyAdapter } from '../agyAdapter';
import { NEW_CONVERSATION } from '../chatAdapter';
import { chatModelsOf } from '../chatCatalogModels';
import { chatRunSpec } from '../chatPresets';
import { rowInstructed } from '../chatPrompt';
import type { ChatSession, TurnResult } from '../chatSession';
import { hearsRowInstruction, instructedAfter } from '../chatThread';
import { sourceOf } from './sourceReading';
import { remoteChatFor } from '../chatRemote';
import { CLAUDE_ARGS, claudeAdapter } from '../claudeAdapter';
import { chatLaunchFor } from '../cliChatLaunch';
import { codexAdapter } from '../codexAdapter';
import { requestBody } from '../remoteAsk';
import type { ServerResult } from '../teamServerApi';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';

/**
 * E4.6c of research/PLAN_one_model_catalog.md (D8): a row's effort and system prompt apply when it CHATS as they do when it
 * reviews — by the server's own rules (coai-mcp, E2.2). Effort: claude's `--effort`, a Team server's `effort` field, and
 * nothing for codex or agy. The system prompt: a section before the person's words, never in argv, on the first turn a
 * session hears and on every turn of a session that forgets.
 */

const row = (over: Partial<Vendor>): Vendor => ({ ...DEFAULT_VENDORS[0]!, uses: ['chat'], ...over });

test('a row\'s effort and system prompt reach the chat model and the run spec built from it', () => {
  const [model] = chatModelsOf([], [row({ id: 'chat-deep', runtime: 'claude', effort: 'high', systemPrompt: 'Be terse.' })], []);
  const spec = chatRunSpec(model!);

  assert.deepEqual([model!.effort, model!.systemPrompt], ['high', 'Be terse.']);
  assert.deepEqual([spec.effort, spec.systemPrompt], ['high', 'Be terse.']);
});

test('a claude launch carries the row\'s effort; an empty or refused one sends no flag at all', () => {
  const launch = (effort?: string) => chatLaunchFor(row({ runtime: 'claude', effort }), '', 'opus', 'text');

  assert.deepEqual(claudeAdapter.argv(launch('high')), [...CLAUDE_ARGS, '--model', 'opus', '--effort', 'high']);
  assert.deepEqual(claudeAdapter.argv(launch(undefined)), [...CLAUDE_ARGS, '--model', 'opus']);
  assert.deepEqual(claudeAdapter.argv(launch('turbo')), [...CLAUDE_ARGS, '--model', 'opus'], 'a level the CLI refuses was sent');
});

test('codex and agy are never handed an effort — the server sends them none either', () => {
  for (const runtime of ['codex', 'antigravity'] as const) {
    const launch = chatLaunchFor(row({ runtime, effort: 'high' }), '', '', 'text');
    assert.equal(launch.effort ?? '', '', `${runtime} was given an effort`);
  }
  const forced = { ...NEW_CONVERSATION, effort: 'high' };
  assert.ok(!codexAdapter.argv(forced).some((arg) => arg.includes('effort')), 'codex argv carries an effort');
  assert.ok(!agyAdapter.argv(forced).some((arg) => arg.includes('effort')), 'agy argv carries an effort');
});

test('a Team-server chat sends the row\'s effort in its request, and nothing when it has none', async () => {
  assert.equal(requestBody('claude', 'opus', 'hi', 60, '', 'high')['effort'], 'high');
  assert.ok(!('effort' in requestBody('claude', 'opus', 'hi', 60)), 'an empty effort crossed as a field');

  const bodies: unknown[] = [];
  const asked = async <T>(_url: string, _route: string, attempt: { body?: unknown } = {}): Promise<ServerResult<T>> => {
    if (attempt.body !== undefined) {
      bodies.push(attempt.body);

      return { ok: true, status: 202, contract: 2, value: { id: 'review-1' } as T };
    }

    return { ok: true, status: 200, contract: 2, value: { status: 'done', answer: 'yes' } as T };
  };
  const remote = row({ id: 'srv-claude', runtime: 'remote', model: 'opus', teamServerId: 'srv', remoteVendor: 'claude', effort: 'max' });
  const session = remoteChatFor(remote, { id: 'srv', name: 'Server', url: 'https://coai.example.test' }, 'a-token', asked);
  await session!.send('explain this');

  assert.equal((bodies[0] as Record<string, unknown>)['effort'], 'max');
});

test('the system prompt goes before the words on a session\'s first turn, and is never in argv', () => {
  const sent = rowInstructed('explain this', 'Be terse.', true);

  assert.match(sent, /^## What the person asked of this model\n\nBe terse\./u);
  assert.ok(sent.endsWith('explain this'), 'the person\'s words are not what the turn ends with');
  const argv = claudeAdapter.argv(chatLaunchFor(row({ runtime: 'claude', systemPrompt: 'Be terse.' }), '', '', 'text'));
  assert.ok(!argv.some((arg) => arg.includes('Be terse')), 'the system prompt is in argv, where any process listing reads it');
});

test('a later turn of the same session, or a row with no system prompt, sends the words alone', () => {
  assert.equal(rowInstructed('and this?', 'Be terse.', false), 'and this?');
  assert.equal(rowInstructed('explain this', '', true), 'explain this');
  assert.equal(rowInstructed('explain this', '   ', true), 'explain this');
});

test('a session hears the instruction once; another session, or one that forgets, hears it again', () => {
  const session = (): ChatSession => ({ send: () => Promise.resolve({ ok: true, answer: '' }), stop: () => undefined, dispose: () => undefined });
  const a = session();
  const b = session();

  assert.equal(hearsRowInstruction({ forgetful: false, instructed: undefined, session: a }), true, 'the first turn went without it');
  assert.equal(hearsRowInstruction({ forgetful: false, instructed: a, session: a }), false, 'a session that remembers was told twice');
  assert.equal(hearsRowInstruction({ forgetful: false, instructed: a, session: b }), true, 'a switched-to session never heard it');
  assert.equal(hearsRowInstruction({ forgetful: true, instructed: a, session: a }), true, 'a Team server forgets each turn');
});

test('a stopped, failed or restarted turn leaves the session un-instructed, so the next turn carries it again', () => {
  // One session object outlives its process: a stop or a crash starts a new child on the SAME object. Marking it as
  // instructed before the send dropped the row's system prompt for the rest of the conversation. (our own reviewer, E4.6.)
  const a: ChatSession = { send: () => Promise.resolve({ ok: true, answer: '' }), stop: () => undefined, dispose: () => undefined };
  const next = (result: TurnResult): boolean => hearsRowInstruction({ forgetful: false, instructed: instructedAfter(a, result), session: a });

  assert.equal(next({ ok: true, answer: 'fine' }), false, 'a session that answered was told again');
  assert.equal(next({ ok: false, failure: 'the person stopped it', stopped: true }), true, 'after a stop the next process never heard it');
  assert.equal(next({ ok: false, failure: 'the CLI exited' }), true, 'after a failed turn the next process never heard it');
  assert.equal(next({ ok: true, answer: 'fine', contextLost: true }), true, 'a turn answered by a new process left it unheard');
});

test('the send path instructs what it SENDS and marks the session only by the turn\'s result — the transcript is not touched', () => {
  // chatTurn.ts imports vscode, so its wiring is read; the decisions it calls are tested above.
  const turn = sourceOf('chatTurn.ts');

  assert.match(turn, /const instructing = hearsRowInstruction\(thread\);/u);
  assert.match(turn, /\.send\(rowInstructed\(sent, answering\?\.systemPrompt \?\? '', instructing\)/u);
  assert.match(turn, /thread\.instructed = instructedAfter\(heard, result\);/u);
  assert.doesNotMatch(turn, /thread\.instructed = thread\.session;/u, 'the session is marked before anything has heard it');
  assert.doesNotMatch(turn, /messages[^\n]*rowInstructed/u, 'the instruction was written into the transcript');
});
