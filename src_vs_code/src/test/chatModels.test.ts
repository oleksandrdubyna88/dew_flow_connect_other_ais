import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHAT_RUNTIMES, chatModelsFrom, chosenModel } from '../chatModels';
import { AGY_ARGS, launchSpecFor } from '../cliChatLaunch';
import { Vendor } from '../vendors';

/**
 * Which models may be asked, and what asking one looks like on a command line.
 *
 * <p>The two halves are tested together because they answer one question between them: this row can
 * be offered, and this is what offering it means. Splitting them would leave the interesting failure
 * — a row that appears in the picker and cannot be launched — with no test that sees both sides.</p>
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
  } as Vendor;
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

test('a runtime with no adapter is refused rather than launched through the wrong protocol', () => {
  const spec = launchSpecFor(vendor({ id: 'claude', runtime: 'claude' }), 't');

  assert.match(spec.refusal, /claude runs on claude/);
  assert.strictEqual(spec.executable, '', 'a refused row still produced something to run');
  assert.deepStrictEqual([...spec.args], []);
});
