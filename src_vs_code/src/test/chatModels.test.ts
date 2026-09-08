import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatChoice, chatModelsFrom, chosenModel } from '../chatModels';
import { AGY_ARGS, CHAT_RUNTIMES, chatHome, launchSpecFor } from '../cliChatLaunch';
import { Vendor } from '../vendors';

/**
 * Which models may be asked, and what asking one looks like on a command line.
 *
 * <p>The two halves are tested together because they answer one question between them: this row can
 * be offered, and this is what offering it means. Splitting them would leave the interesting failure
 * — a row that appears in the picker and cannot be launched — with no test that sees both sides.</p>
 */

/**
 * A vendor row, with no `as` in sight.
 *
 * <p>The cast that used to close this function is exactly what `doctrine.md` §3 forbids: a promise
 * to keep a shape by hand, which comes due silently the day `Vendor` gains a required field and
 * every fixture in the suite goes on compiling against a type it no longer satisfies.</p>
 */
function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'antigravity',
    runtime: 'antigravity',
    model: 'gemini-3.7-flash-high',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

test('a row on a runtime the chat speaks is offered, with its model in the label', () => {
  const list = chatModelsFrom([vendor()]);

  assert.strictEqual(list.offered.length, 1);
  assert.match(list.offered[0]!.label, /gemini-3\.7-flash-high/);
  assert.strictEqual(list.refused.length, 0);
});

test('a disabled row is not offered at all, and is not reported as refused either', () => {
  // It is not a capability problem; the person turned it off.
  const list = chatModelsFrom([vendor({ enabled: false })]);

  assert.deepStrictEqual(list.offered, []);
  assert.deepStrictEqual(list.refused, []);
});

test('a row on another runtime is REFUSED BY NAME rather than silently missing', () => {
  // A person who configured `codex` and finds the picker quietly missing it cannot tell a bug from a
  // policy. `vendor-routing.md` also forbids routing it through agy, which is what silence invites.
  const list = chatModelsFrom([vendor({ id: 'codex', runtime: 'codex' })]);

  assert.deepStrictEqual(list.offered, []);
  assert.strictEqual(list.refused.length, 1);
  assert.match(list.refused[0]!.reason, /codex runs on codex/);
  assert.match(list.refused[0]!.reason, new RegExp(CHAT_RUNTIMES[0]!));
});

test('offered and refused are reported side by side', () => {
  const list = chatModelsFrom([vendor(), vendor({ id: 'claude', runtime: 'claude' })]);

  assert.strictEqual(list.offered.length, 1);
  assert.strictEqual(list.refused.length, 1);
});

test('the model asked for wins when it is on offer', () => {
  const list = chatModelsFrom([vendor(), vendor({ id: 'second' })]);

  assert.strictEqual(chosenModel(list, 'second'), 'second');
});

test('a model that is not on offer falls back to the first that is', () => {
  const list = chatModelsFrom([vendor()]);

  assert.strictEqual(chosenModel(list, 'codex'), 'antigravity');
  assert.strictEqual(chosenModel(list, ''), 'antigravity');
});

test('nothing on offer chooses nothing, rather than inventing a vendor to bill', () => {
  assert.strictEqual(chosenModel({ offered: [], refused: [] }, 'antigravity'), '');
});

test('a model the person NAMED and which cannot answer is refused by name, never substituted', () => {
  // The quiet substitution is the defect: `coai.chatModel` says `codex`, the chat cannot speak to
  // codex, and the passage went to a different vendor's model — billed, and answered by somebody
  // they did not choose. An empty setting is a different thing entirely: it is not a choice.
  const list = chatModelsFrom([vendor(), vendor({ id: 'codex', runtime: 'codex' })]);

  const choice = chatChoice(list, 'codex');

  assert.strictEqual(choice.modelId, '');
  assert.match(choice.refusal, /codex runs on codex/);
});

test('a model the person named and which is simply gone says that, rather than picking a stranger', () => {
  const choice = chatChoice(chatModelsFrom([vendor()]), 'a-row-they-deleted');

  assert.strictEqual(choice.modelId, '');
  assert.match(choice.refusal, /a-row-they-deleted/);
});

test('no model named means no choice was made, so the first on offer answers', () => {
  const choice = chatChoice(chatModelsFrom([vendor(), vendor({ id: 'second' })]), '');

  assert.strictEqual(choice.modelId, 'antigravity');
  assert.strictEqual(choice.refusal, '');
});

test('the model named and on offer is the one that answers', () => {
  const choice = chatChoice(chatModelsFrom([vendor(), vendor({ id: 'second' })]), 'second');

  assert.deepStrictEqual(choice, { modelId: 'second', refusal: '' });
});

test('nothing on offer at all reports every reason it has, not one of them', () => {
  const choice = chatChoice(
    chatModelsFrom([vendor({ id: 'codex', runtime: 'codex' }), vendor({ id: 'claude', runtime: 'claude' })]),
    '',
  );

  assert.strictEqual(choice.modelId, '');
  assert.match(choice.refusal, /codex/);
  assert.match(choice.refusal, /claude/);
});

test('the launch carries the four flags, each of which was chosen against a failure', () => {
  const spec = launchSpecFor(vendor(), 'C:/temp/empty');

  assert.strictEqual(spec.refusal, '');
  assert.deepStrictEqual([...spec.args], [...AGY_ARGS]);
  // stdin, because `-p` was MEASURED not to read it - the model answered "you did not attach the
  // fragment" - and argv on Windows is this family's known truncation trap.
  assert.ok(spec.args.includes('--input-format'));
  assert.ok(spec.args.includes('stream-json'));
  // Read-only, and a passage beginning with a slash is not a command.
  assert.ok(spec.args.includes('--mode'));
  assert.ok(spec.args.includes('--disable-slash-commands'));
});

test('the child runs in the directory it was given, never the workspace', () => {
  const spec = launchSpecFor(vendor(), 'C:/temp/empty');

  assert.strictEqual(spec.cwd, 'C:/temp/empty');
});

test('a configured path is used, and a bare name is the fallback', () => {
  assert.strictEqual(launchSpecFor(vendor({ executablePath: 'D:/tools/agy.exe' }), 't').executable, 'D:/tools/agy.exe');
  assert.strictEqual(launchSpecFor(vendor(), 't').executable, 'agy');
});

test('the directory a conversation runs in is made once and taken away once', () => {
  // It used to be made on every press of the keybinding, including the presses that only reveal a
  // tab that is already open, and it was never removed. (codex, the plan round.)
  const made: string[] = [];
  const removed: string[] = [];
  const home = chatHome(
    () => {
      made.push('made');

      return 'C:/temp/coai-chat-abc';
    },
    (dir) => removed.push(dir),
    () => assert.fail('a clean removal reported a failure'),
  );

  assert.strictEqual(home.dir, 'C:/temp/coai-chat-abc');
  home.release();
  home.release();

  assert.deepStrictEqual(made, ['made'], 'the directory was made more than once');
  assert.deepStrictEqual(removed, ['C:/temp/coai-chat-abc'], 'removed twice, or never');
});

test('a directory that will not delete is SAID, and does not take the closing tab down with it', () => {
  // `reliability.md`: the outer edge of a detached call ends in a catch that logs. Swallowing this
  // one leaves a directory per conversation in %TEMP% and nothing anywhere that could explain it.
  const said: string[] = [];
  const home = chatHome(
    () => 'C:/temp/coai-chat-locked',
    () => {
      throw new Error('the directory is in use by another process');
    },
    (failure) => said.push(failure),
  );

  assert.doesNotThrow(() => home.release());
  assert.strictEqual(said.length, 1, 'the failure was swallowed');
  assert.match(said[0]!, /C:\/temp\/coai-chat-locked/, 'the report does not say which directory');
  assert.match(said[0]!, /in use by another process/, 'the report does not say what went wrong');
});

test('a runtime with no adapter is refused rather than launched through the wrong protocol', () => {
  const spec = launchSpecFor(vendor({ id: 'claude', runtime: 'claude' }), 't');

  assert.match(spec.refusal, /claude runs on claude/);
  assert.strictEqual(spec.executable, '', 'a refused row still produced something to run');
  assert.deepStrictEqual([...spec.args], []);
});
