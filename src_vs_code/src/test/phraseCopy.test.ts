import assert from 'node:assert/strict';
import { test } from 'node:test';
import { phraseCopier, phraseToCopy } from '../phraseCopy';
import { phrasesFrom } from '../phrases';

/**
 * Putting a phrase on the clipboard, and the three sentences that say what happened.
 *
 * <p>The paths worth testing are the ones that FAIL, which is why the clipboard and the status bar
 * are injected: a plan reviewer pointed out that every test listed in the story resolved phrase data
 * and none of them touched the write, so an implementation could report success before the write
 * landed and pass the lot.</p>
 */

const PHRASES = phrasesFrom([
  { id: 'a', name: 'Ship it', text: 'make a pr, accept it, deploy' },
  { id: 'b', name: 'Check', text: '  indented\nand two lines\n' },
]);

/** A clipboard and a status bar, recorded. */
function ports(options: { readonly fail?: boolean } = {}) {
  const written: string[] = [];
  const said: string[] = [];
  let livingNow = 0;
  const copier = phraseCopier({
    writeText: async (text) => {
      if (options.fail === true) {
        throw new Error('the clipboard is held by another program');
      }
      written.push(text);
    },
    say: (message) => {
      said.push(message);
      livingNow += 1;

      return { dispose: () => { livingNow -= 1; } };
    },
  });

  return { copier, written, said, living: () => livingNow };
}

test('a button names a phrase, and its words go to the clipboard exactly as stored', async () => {
  const { copier, written } = ports();
  const report = await copier.copy(PHRASES, 'b');

  assert.equal(report.copied, true, 'a copy that worked was not reported as one');
  assert.deepStrictEqual(written, ['  indented\nand two lines\n'],
    'the phrase was trimmed or reshaped on its way to the clipboard');
});

test('the confirmation names the phrase, so it is obvious which one was taken', async () => {
  const { copier, said } = ports();
  await copier.copy(PHRASES, 'a');

  assert.match(said[0] ?? '', /Ship it/, 'the confirmation does not say what was copied');
});

test('a write that is refused says so, and never claims success', async () => {
  const { copier, written, said } = ports({ fail: true });
  const report = await copier.copy(PHRASES, 'a');

  assert.equal(report.copied, false, 'a failed clipboard write was reported as a copy');
  assert.deepStrictEqual(written, [], 'something was written despite the failure');
  assert.match(said[0] ?? '', /could not be copied/, 'a failed copy said nothing to the person');
  assert.ok(!/Copied/.test(said[0] ?? ''), 'a failed copy printed the success sentence');
});

test('an id naming no phrase writes nothing and refuses in words', async () => {
  const { copier, written, said } = ports();
  const report = await copier.copy(PHRASES, 'gone');

  assert.equal(report.copied, false, 'a stale button reported a copy');
  assert.deepStrictEqual(written, [], 'a stale button put something on the clipboard');
  assert.match(said[0] ?? '', /not in the list any more/, 'a stale button said nothing');
});

test('pressing a phrase when there are none at all points at the way to make one', async () => {
  const { copier, said } = ports();
  await copier.copy([], 'a');

  assert.match(said[0] ?? '', /Edit phrases/, 'an empty list refuses without saying what to do about it');
});

test('five copies in a row leave one line on the status bar, not five', async () => {
  const { copier, living } = ports();

  for (const id of ['a', 'b', 'a', 'b', 'a']) {
    await copier.copy(PHRASES, id);
  }

  assert.equal(living(), 1, `${living()} status messages are competing over the same strip`);
});

test('a refusal replaces the line a success left, and the other way round', async () => {
  const { copier, living } = ports();
  await copier.copy(PHRASES, 'a');
  await copier.copy(PHRASES, 'gone');

  assert.equal(living(), 1, 'the confirmation of the copy before it is still on screen beside the refusal');
});

test('the decision is separable: which phrase, or the sentence instead', () => {
  assert.deepStrictEqual(phraseToCopy(PHRASES, 'a').kind, 'copy');
  assert.deepStrictEqual(phraseToCopy(PHRASES, 'gone').kind, 'refused');
  assert.deepStrictEqual(phraseToCopy(PHRASES, undefined).kind, 'refused',
    'a message carrying no id at all was treated as naming one');
});

test('two presses land in the order they were pressed, so the last one is on the clipboard', async () => {
  // Two asynchronous writes started together end with whichever RESOLVES last on the clipboard, so
  // pressing A then B could leave A on it. (Code round 2026-09-14, codex, Major.)
  const written: string[] = [];
  const slow = new Map<string, () => void>();
  const copier = phraseCopier({
    writeText: async (text) => {
      await new Promise<void>((resolve) => { slow.set(text, resolve); });
      written.push(text);
    },
    say: () => ({ dispose: () => undefined }),
  });

  const first = copier.copy(PHRASES, 'a');
  const second = copier.copy(PHRASES, 'b');
  await new Promise<void>((resolve) => { setImmediate(resolve); });

  // Only the FIRST write has been started: the second is queued behind it rather than racing it.
  assert.equal(slow.size, 1, 'both writes were in flight at once, so whichever finished last wins');
  slow.get('make a pr, accept it, deploy')?.();
  await first;
  await new Promise<void>((resolve) => { setImmediate(resolve); });
  slow.get('  indented\nand two lines\n')?.();
  await second;

  assert.deepStrictEqual(written, ['make a pr, accept it, deploy', '  indented\nand two lines\n'],
    'the presses landed out of order, so the clipboard holds the phrase pressed first');
});
