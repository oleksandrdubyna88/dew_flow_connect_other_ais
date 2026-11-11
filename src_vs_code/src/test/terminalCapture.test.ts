import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { oneAtATime } from '../oneAtATime';
import { Capture, Clipboard, borrowClipboard } from '../selectionCapture';
import { NOTHING_COPIED, readCopiedText, terminalPassage } from '../terminalSource';
import { blanked } from './blankedSource';
import { bodyOf, sourceOf } from './sourceReading';

/**
 * The terminal door reads the clipboard; the borrow is the Claude-tab door's.
 *
 * <p>The person copies, and the door takes what is on the clipboard (epoch 2, change 1.2): it runs no copy command, no borrow
 * and writes nothing, so a terminal is never sent a keystroke either. The borrow is tested here on its own (S1-T1…T5, T11);
 * the terminal read built on the clipboard is S1r-T1…T6. `chatCapture.test.ts` is the regression net for the borrow's
 * extraction and is not edited.</p>
 */

/**
 * A clipboard that records what happened to it, in order.
 *
 * <p>The same fake as `chatCapture.test.ts` — not shared, because that file is the unedited
 * regression net for this extraction and a second consumer of a three-line fake is not worth
 * touching it.</p>
 */
function fakeClipboard(initial: string): Clipboard & { held(): string; writes(): readonly string[] } {
  let value = initial;
  const writes: string[] = [];

  return {
    read: async () => value,
    write: async (text: string) => {
      writes.push(text);
      value = text;
    },
    held: () => value,
    writes: () => writes,
  };
}

const ORIGINAL = 'what the person had copied';

test('S1-T1 a rejected copy still gives the clipboard back', async () => {
  const clipboard = fakeClipboard(ORIGINAL);
  const reason = new Error('the command was refused');

  await assert.rejects(
    borrowClipboard(clipboard, async () => {
      throw reason;
    }),
    (thrown: unknown) => thrown === reason,
    'the borrow swallowed or replaced the reason the copy failed',
  );

  assert.strictEqual(clipboard.held(), ORIGINAL, 'a rejected copy left the marker on the clipboard');
  assert.strictEqual(clipboard.writes().length, 2, 'the writes are the marker, then the original');
  assert.strictEqual(clipboard.writes()[1], ORIGINAL);
});

test('S1-T2 a copy that wrote nothing is "nothing landed", and the clipboard comes back', async () => {
  const clipboard = fakeClipboard(ORIGINAL);

  const borrowed = await borrowClipboard(clipboard, async () => 'ran');

  assert.strictEqual(borrowed.landed, false, 'the marker was taken for a copied passage');
  assert.strictEqual(borrowed.text, '');
  assert.strictEqual(borrowed.outcome, 'ran');
  assert.strictEqual(clipboard.held(), ORIGINAL);
});

test('S1-T3 a copied selection comes back, and the clipboard is restored', async () => {
  const clipboard = fakeClipboard(ORIGINAL);

  const borrowed = await borrowClipboard(clipboard, async () => {
    await clipboard.write('the selected passage');

    return 7;
  });

  assert.deepStrictEqual(borrowed, { landed: true, text: 'the selected passage', outcome: 7 });
  assert.strictEqual(clipboard.held(), ORIGINAL, 'the clipboard was taken, not borrowed');
});

test('S1-T4 a clipboard somebody changed during the copy is left alone', async () => {
  const inner = fakeClipboard(ORIGINAL);
  let step = 0;
  const watching: Clipboard = {
    read: async () => {
      step += 1;
      // 1: the borrow. 2: what our copy landed. 3: another app got there first.
      return step === 1 ? ORIGINAL : step === 2 ? 'the selected passage' : 'something else entirely';
    },
    write: inner.write,
  };

  const borrowed = await borrowClipboard(watching, async () => undefined);

  assert.strictEqual(borrowed.text, 'the selected passage');
  assert.deepStrictEqual(inner.writes().slice(1), [], 'the newer clipboard was overwritten by the restore');
});

test('S1-T5 an empty clipboard is never written', async () => {
  const unreadable = fakeClipboard('');

  const nothing = await borrowClipboard(unreadable, async () => undefined);

  assert.strictEqual(nothing.landed, false);
  assert.deepStrictEqual(unreadable.writes(), [], 'an unreadable clipboard was overwritten');

  const copying = fakeClipboard('');
  const copied = await borrowClipboard(copying, async () => {
    await copying.write('the selected passage');
  });

  assert.strictEqual(copied.text, 'the selected passage');
  assert.strictEqual(copying.held(), 'the selected passage', 'the restore blanked the clipboard');
});

test('S1r-T1 the copied text is the passage, as it is, and the clipboard is not written', async () => {
  const clip = fakeClipboard('  ls -la\n');

  const capture = await readCopiedText(() => clip.read());

  assert.deepStrictEqual(capture, { text: '  ls -la\n', failure: '' }, 'the copied text was trimmed');
  assert.deepStrictEqual(clip.writes(), [], 'the terminal door wrote to the clipboard');
  assert.strictEqual(clip.held(), '  ls -la\n');
});

test('S1r-T2 nothing copied gives the owner’s sentence, an empty clipboard and white space alike', async () => {
  for (const held of ['', '  \n\t']) {
    const clip = fakeClipboard(held);

    const capture = await readCopiedText(() => clip.read());

    assert.deepStrictEqual(
      capture,
      { text: '', failure: 'Copy the text first, then press Ask other AI.' },
      `a passage of ${JSON.stringify(held)} was returned`,
    );
    assert.deepStrictEqual(clip.writes(), []);
  }

  assert.strictEqual(NOTHING_COPIED, 'Copy the text first, then press Ask other AI.');
  assert.deepStrictEqual(terminalPassage('  \n\t'), { text: '', failure: NOTHING_COPIED });
});

test('S1r-T3 a clipboard that cannot be read says so, and the read never rejects', async () => {
  const busy = await readCopiedText(async () => {
    throw new Error('clipboard busy');
  });

  assert.strictEqual(busy.text, '');
  assert.notStrictEqual(busy.failure, NOTHING_COPIED, 'the sentence lies about the cause');
  assert.strictEqual(busy.failure, 'The clipboard could not be read: clipboard busy');

  const odd = await readCopiedText(async () => {
    throw 'odd';
  });

  assert.strictEqual(odd.failure, 'The clipboard could not be read: odd');
});

test('S1r-T4 the same text twice is sent twice', async () => {
  const clip = fakeClipboard('same');

  const first = await readCopiedText(() => clip.read());
  const second = await readCopiedText(() => clip.read());

  assert.deepStrictEqual(first, { text: 'same', failure: '' });
  assert.deepStrictEqual(second, { text: 'same', failure: '' }, 'an unchanged clipboard was refused; decision 3 sends it');
});

test('S1r-T5 a press while the keyboard path holds the latch is refused, never the marker', async () => {
  const clip = fakeClipboard(ORIGINAL);
  const latch = oneAtATime<Capture>({ text: '', failure: 'busy' });
  let open: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => {
    open = resolve;
  });
  let started: () => void = () => undefined;
  const running = new Promise<void>((resolve) => {
    started = resolve;
  });

  const keyboard = latch(async () => {
    await clip.write('coai-marker-1234');
    started();
    await gate;
    await clip.write(ORIGINAL);

    return { text: 'the keyboard passage', failure: '' };
  });
  await running;

  const pressed = await latch(() => readCopiedText(() => clip.read()));

  assert.deepStrictEqual(pressed, { text: '', failure: 'busy' }, 'a terminal press during the keyboard capture sent the marker');

  open();
  assert.deepStrictEqual(await keyboard, { text: 'the keyboard passage', failure: '' });
  assert.deepStrictEqual(await latch(() => readCopiedText(() => clip.read())), { text: ORIGINAL, failure: '' });
});

test('S1r-T6 the terminal door reads under the one latch and runs nothing', () => {
  const raw = sourceOf('chatCapture.ts');
  const code = blanked(raw);
  const terminal = bodyOf(code, 'export async function fromTheTerminal(');
  const rawTerminal = bodyOf(raw, 'export async function fromTheTerminal(');
  const keyboard = bodyOf(code, 'export async function passageFor(');

  assert.match(terminal, /oneCapture\(/, 'the terminal door has a latch of its own, or none');
  assert.match(terminal, /readCopiedText\(/, 'the terminal door does not read the copied text');
  // IDENTIFIERS, in the body with its strings and comments blanked: a call that starts a helper, types into the terminal,
  // runs a command, or borrows the clipboard. `sendText('\u0003')` is a real Ctrl+C into a shell.
  const forbidden = [
    'pressCopy', 'COPY_SCRIPT', 'launch', 'sendText', 'sendSequence', 'executeCommand',
    'borrowClipboard', 'captureSelection', 'write', 'writeText',
  ];
  for (const name of forbidden) {
    assert.ok(!new RegExp(`\\b${name}\\b`).test(terminal), `the terminal door reaches for ${name}, which is not a read`);
  }
  // WORDS, in the raw body: a helper's or a command's name only ever appears inside a string, which the blanking removes.
  for (const word of ['copySelection', 'powershell', 'keybd_event']) {
    assert.ok(!rawTerminal.toLowerCase().includes(word.toLowerCase()), `the terminal door names ${word}`);
  }
  const source = sourceOf('terminalSource.ts');
  assert.ok(!/\bexecuteCommand\b/.test(blanked(source)), 'terminalSource.ts runs a command');
  assert.ok(!source.includes('copySelection'), 'terminalSource.ts still names the copy command');
  // Companion: the scan is alive — the keyboard door still holds the key press it forbids here, under the same latch.
  assert.match(keyboard, /pressCopy/, 'the scan reads the wrong function: the keyboard door no longer presses the key');
  assert.match(keyboard, /oneCapture\(/, 'the scan reads the wrong function: the keyboard door has no latch');
});

test('S1-T11 a restore that fails after a rejected copy does not replace the copy\'s reason', async () => {
  const inner = fakeClipboard(ORIGINAL);
  let reads = 0;
  const failing: Clipboard = {
    read: async () => {
      reads += 1;
      if (reads > 1) {
        throw new Error('the clipboard is gone');
      }

      return inner.read();
    },
    write: inner.write,
  };
  const reason = new Error('the command was refused');

  await assert.rejects(
    borrowClipboard(failing, async () => {
      throw reason;
    }),
    (thrown: unknown) => thrown === reason,
    'the failed restore masked the reason the copy failed',
  );
});
