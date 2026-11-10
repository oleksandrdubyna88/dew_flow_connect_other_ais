import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Clipboard, borrowClipboard } from '../selectionCapture';
import {
  NO_TERMINAL_SELECTION,
  TERMINAL_COPY,
  captureTerminalSelection,
  terminalPassage,
} from '../terminalSource';
import { blanked } from './blankedSource';
import { bodyOf, sourceOf } from './sourceReading';

/**
 * Copying a terminal's selection: the borrow, and the sentence when there is none.
 *
 * <p>A terminal is never sent a keystroke. A synthetic Ctrl+C reaches the shell as SIGINT — in `ssh`,
 * `tmux` or the Claude CLI that is an interrupt, not a copy — so the terminal copy is VS Code's own
 * `copySelection` command, run inside the same clipboard borrow the keyboard capture uses. The
 * borrow is tested here on its own (S1-T1…T5), then the terminal capture built on it (T6…T9).
 * `chatCapture.test.ts` is the regression net for the extraction and is not edited.</p>
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

test('S1-T6 the terminal capture runs exactly one command, the copy', async () => {
  const seen: string[] = [];

  await captureTerminalSelection(async (command) => {
    seen.push(command);
  }, fakeClipboard(ORIGINAL));

  assert.deepStrictEqual(seen, ['workbench.action.terminal.copySelection']);
  assert.strictEqual(TERMINAL_COPY, 'workbench.action.terminal.copySelection');
});

test('S1-T7 no selection gives the owner’s sentence, and so does a selection of nothing but space', async () => {
  const none = await captureTerminalSelection(async () => undefined, fakeClipboard(ORIGINAL));

  assert.deepStrictEqual(none, { text: '', failure: 'Select text in the terminal first.' });
  assert.strictEqual(NO_TERMINAL_SELECTION, 'Select text in the terminal first.');

  const clipboard = fakeClipboard(ORIGINAL);
  const blank = await captureTerminalSelection(async () => clipboard.write('  \n'), clipboard);

  assert.deepStrictEqual(blank, { text: '', failure: NO_TERMINAL_SELECTION }, 'a passage of white space was returned');
  assert.strictEqual(clipboard.held(), ORIGINAL);

  const kept = await captureTerminalSelection(async () => clipboard.write('  ls -la\n'), clipboard);

  assert.deepStrictEqual(kept, { text: '  ls -la\n', failure: '' }, 'the selection is the passage and is not trimmed');
  assert.deepStrictEqual(
    terminalPassage({ landed: false, text: '', outcome: undefined }),
    { text: '', failure: NO_TERMINAL_SELECTION },
  );
});

test('S1-T8 a copy command that rejects says so, and not "select text"', async () => {
  const clipboard = fakeClipboard(ORIGINAL);

  const capture = await captureTerminalSelection(async () => {
    throw new Error('no terminal');
  }, clipboard);

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /^The terminal selection could not be copied: no terminal$/);
  assert.notStrictEqual(capture.failure, NO_TERMINAL_SELECTION, 'the sentence lies about the cause');
  assert.strictEqual(clipboard.held(), ORIGINAL, 'a rejected copy kept the marker');

  const odd = await captureTerminalSelection(async () => {
    throw 'a string, not an Error';
  }, fakeClipboard(ORIGINAL));

  assert.match(odd.failure, /could not be copied: a string, not an Error$/);
});

test('S1-T9 the terminal path presses no key and shares the keyboard latch', () => {
  const raw = sourceOf('chatCapture.ts');
  const code = blanked(raw);
  const terminal = bodyOf(code, 'export async function fromTheTerminal(');
  const rawTerminal = bodyOf(raw, 'export async function fromTheTerminal(');
  const keyboard = bodyOf(code, 'export async function passageFor(');

  assert.match(terminal, /oneCapture\(/, 'the terminal door has a latch of its own, or none');
  assert.match(terminal, /captureTerminalSelection\(/, 'the terminal door does not use the terminal capture');
  // IDENTIFIERS, in the body with its strings and comments blanked: a call that starts a helper or types into the terminal.
  // `sendText('\u0003')` is a real Ctrl+C into a shell — the very thing this door exists not to do.
  for (const forbidden of ['pressCopy', 'COPY_SCRIPT', 'launch', 'sendText', 'sendSequence']) {
    assert.ok(!new RegExp(`\\b${forbidden}\\b`).test(terminal), `the terminal door reaches for ${forbidden}, which is a keystroke`);
  }
  // WORDS, in the raw body: a helper's name only ever appears inside a string, which the blanking removes.
  for (const forbidden of ['powershell', 'keybd_event']) {
    assert.ok(!rawTerminal.toLowerCase().includes(forbidden), `the terminal door names ${forbidden}, which is a keystroke`);
  }
  // Companion: the scan is alive — the keyboard door still holds the key press it forbids here.
  assert.match(keyboard, /pressCopy/, 'the scan reads the wrong function: the keyboard door no longer presses the key');
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
