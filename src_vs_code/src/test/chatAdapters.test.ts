import assert from 'node:assert/strict';
import { test } from 'node:test';
import { AGY_ARGS, agyAdapter } from '../agyAdapter';
import { CLAUDE_ARGS, claudeAdapter } from '../claudeAdapter';
import { CODEX_ARGS, codexAdapter } from '../codexAdapter';
import { ChatAdapter } from '../chatAdapter';

/**
 * Three vendors, three wire protocols, one seam.
 *
 * <p>Every line asserted here was OBSERVED — printed by the real binary on this machine — rather
 * than copied from a manual. That is the point of the file: an adapter is a promise about somebody
 * else's output, and the only way to keep it honest is to pin the bytes they actually sent.</p>
 */

const THREE: readonly { readonly name: string; readonly adapter: ChatAdapter }[] = [
  { name: 'agy', adapter: agyAdapter },
  { name: 'claude', adapter: claudeAdapter },
  { name: 'codex', adapter: codexAdapter },
];

test('every adapter says which shape it is, because the context rule depends on it', () => {
  for (const { name, adapter } of THREE) {
    assert.ok(['persistent', 'per-turn'].includes(adapter.shape), `${name} has no shape`);
  }
  assert.strictEqual(agyAdapter.shape, 'persistent');
  assert.strictEqual(claudeAdapter.shape, 'persistent');
  assert.strictEqual(codexAdapter.shape, 'per-turn');
});

test('a line no adapter recognises is nothing, never an answer and never a failure', () => {
  // All three log prose to stdout beside their events — a banner, a warning, a progress note — so
  // an unreadable line is the ordinary case. An adapter that failed a turn on one would fail most.
  for (const { name, adapter } of THREE) {
    for (const line of ['', 'Warming up…', '{ not json', '[]', 'null']) {
      assert.strictEqual(adapter.classify(line).kind, 'nothing', `${name} read meaning into: ${line}`);
    }
  }
});

test('agy: the turn is an event, and the schema is the one its own error message named', () => {
  // The binary refused `type:` with `stream input message is missing the "event" field`, which is
  // how this shape was discovered at all.
  const line = JSON.parse(agyAdapter.encode('explain this')) as Record<string, unknown>;

  assert.strictEqual(line['event'], 'user');
  assert.deepStrictEqual(line['message'], { role: 'user', content: 'explain this' });
});

test('agy: init is ready, a SUCCESS result is the answer, and any other status is a failure', () => {
  assert.deepStrictEqual(agyAdapter.classify('{"event":"init"}'), { kind: 'ready' });
  assert.deepStrictEqual(
    agyAdapter.classify('{"event":"result","result":{"status":"SUCCESS","response":"  it means this  "}}'),
    { kind: 'answer', text: 'it means this' },
  );
  assert.deepStrictEqual(
    agyAdapter.classify('{"event":"result","result":{"status":"ERROR","error":"the input was refused"}}'),
    { kind: 'failure', failure: 'the input was refused' },
  );
  assert.match(
    (agyAdapter.classify('{"event":"result","result":{"status":"WEIRD"}}') as { failure: string }).failure,
    /WEIRD/,
    'a status with no error text is reported as nothing at all',
  );
});

test('agy: the four flags are all there, each one bought with a measured failure', () => {
  assert.deepStrictEqual([...agyAdapter.argv('')], [...AGY_ARGS]);
  for (const flag of ['--mode', 'plan', '--disable-slash-commands', '--input-format', '--output-format']) {
    assert.ok(AGY_ARGS.includes(flag), `${flag} is gone`);
  }
});

test('claude: the content is a list of typed BLOCKS, which is what the CLI accepts', () => {
  const line = JSON.parse(claudeAdapter.encode('explain this')) as { message: { content: unknown } };

  assert.deepStrictEqual(line.message.content, [{ type: 'text', text: 'explain this' }]);
});

test('claude: --verbose is in the argv, because without it the CLI refuses outright', () => {
  // Measured: `--output-format=stream-json requires --verbose`. A flag that looks removable is a
  // flag somebody removes.
  assert.ok(CLAUDE_ARGS.includes('--verbose'), 'the CLI will refuse every launch');
  assert.ok(CLAUDE_ARGS.includes('-p'));
  assert.deepStrictEqual([...claudeAdapter.argv('anything')], [...CLAUDE_ARGS], 'claude took a resume id');
});

test('claude: system/init is ready and result/success is the answer', () => {
  assert.deepStrictEqual(claudeAdapter.classify('{"type":"system","subtype":"init","session_id":"a"}'), { kind: 'ready' });
  assert.deepStrictEqual(
    claudeAdapter.classify('{"type":"result","subtype":"success","result":" the answer "}'),
    { kind: 'answer', text: 'the answer' },
  );
});

test('claude: any other result subtype is a failure that says which one', () => {
  const event = claudeAdapter.classify('{"type":"result","subtype":"error_max_turns"}');

  assert.strictEqual(event.kind, 'failure');
  assert.match((event as { failure: string }).failure, /error_max_turns/);
});

test('claude: an assistant message mid-turn is nothing — the result is the answer', () => {
  // Otherwise the page would show the model thinking out loud as though it had finished.
  assert.strictEqual(
    claudeAdapter.classify('{"type":"assistant","message":{"content":[{"type":"text","text":"hm"}]}}').kind,
    'nothing',
  );
});

test('codex: the first turn opens a thread and a later one RESUMES it by id', () => {
  assert.deepStrictEqual([...codexAdapter.argv('')], ['exec', ...CODEX_ARGS]);
  assert.deepStrictEqual(
    [...codexAdapter.argv('01a0851c-488c-7be0-9e51-e35c5fb2ce5c')],
    ['exec', 'resume', '01a0851c-488c-7be0-9e51-e35c5fb2ce5c', ...CODEX_ARGS],
  );
});

test('codex: it resumes by ID and never by --last, which is the machine’s last session', () => {
  // `--last` is the most recent codex session on the whole machine, so two chat tabs — or one chat
  // and one review round — would answer each other's questions.
  for (const argv of [codexAdapter.argv(''), codexAdapter.argv('some-id')]) {
    assert.ok(!argv.includes('--last'), 'the adapter resumes whatever ran last on this machine');
  }
});

test('codex: the prompt travels on stdin, because argv cannot carry a conversation', () => {
  // Measured: 76 059 bytes answered in five seconds through stdin. Windows caps a command line at
  // 32 767 characters and a carried conversation is bounded at 60 000 — argv would have truncated.
  assert.ok(CODEX_ARGS.includes('-'), 'the prompt would go in argv');
  assert.strictEqual(codexAdapter.encode('explain this'), 'explain this', 'the prompt was wrapped in an envelope');
});

test('codex: it runs outside a repository, and that was measured rather than assumed', () => {
  // The flag's own help says it runs "without persisting session files to disk"; the session store
  // survives it anyway, which is why the resume above works from an empty temp directory.
  assert.ok(CODEX_ARGS.includes('--skip-git-repo-check'), 'codex will refuse to start outside a repo');
});

test('codex: the thread id, the answer and a failure each come from their own event', () => {
  assert.deepStrictEqual(
    codexAdapter.classify('{"type":"thread.started","thread_id":"01a0851c-032e-7f51-ad06-8e20a4a65929"}'),
    { kind: 'session', id: '01a0851c-032e-7f51-ad06-8e20a4a65929' },
  );
  assert.deepStrictEqual(
    codexAdapter.classify('{"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"BANANA"}}'),
    { kind: 'answer', text: 'BANANA' },
  );
  assert.strictEqual(
    codexAdapter.classify('{"type":"turn.completed","usage":{"input_tokens":15466,"output_tokens":6}}').kind,
    'nothing',
    'the usage block was read as an answer',
  );
  assert.strictEqual(codexAdapter.classify('{"type":"turn.failed","message":"the model gave up"}').kind, 'failure');
});

test('codex: an item that is not an agent message is not the answer', () => {
  // A run of commands, a file edit, a reasoning summary: all `item.completed`, none of them the
  // thing to show. Taking the last line of stdout would have shown whichever came last.
  assert.strictEqual(
    codexAdapter.classify('{"type":"item.completed","item":{"type":"command_execution","text":"ls"}}').kind,
    'nothing',
  );
});
