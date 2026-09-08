import assert from 'node:assert/strict';
import { test } from 'node:test';
import { COPY_SCRIPT, Clipboard, argvFor, captureSelection, shouldRestore } from '../selectionCapture';

/**
 * Borrowing the clipboard to copy somebody else's selection.
 *
 * <p>Every rule here was learned by measurement with the phase-0 probe rather than reasoned out, and
 * two of them are counter-intuitive enough to be worth a test each: `.NET`'s `SendKeys` delivers
 * NOTHING to an Electron window while `keybd_event` delivers reliably, and a keybinding leaves the
 * person's modifiers physically held, so a synthetic `Ctrl+C` becomes `Ctrl+Alt+C` unless they are
 * released first.</p>
 */

/** A clipboard that records what happened to it, in order. */
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

test('the helper releases every modifier before it presses anything', () => {
  // A keybinding leaves Ctrl and Alt physically down. A synthetic Ctrl+C on top of a held Alt is
  // Ctrl+Alt+C, which copies nothing — measured, and the reason these three lines exist.
  const releases = COPY_SCRIPT.indexOf('KEYUP, [IntPtr]::Zero)');
  const press = COPY_SCRIPT.indexOf('keybd_event([CoaiKeys]::CTRL, 0, 0,');

  assert.ok(releases > 0, 'nothing is released');
  assert.ok(press > releases, 'the chord is built before the modifiers are let go');
});

test('the helper uses WinAPI, not the .NET convenience that delivers nothing', () => {
  // SendKeys readback: 0 characters. keybd_event readback: 17107. Same window, same second.
  assert.match(COPY_SCRIPT, /keybd_event/);
  assert.doesNotMatch(COPY_SCRIPT, /SendKeys/);
});

test('the script reaches PowerShell with no quoting at all', () => {
  const argv = argvFor('Write-Output "it has quotes, `backticks` and @\\" here"');

  assert.ok(argv.includes('-EncodedCommand'), 'the script was passed as text somebody has to quote');
  const encoded = argv[argv.length - 1]!;
  assert.strictEqual(
    Buffer.from(encoded, 'base64').toString('utf16le'),
    'Write-Output "it has quotes, `backticks` and @\\" here"',
    'the round trip changed the script',
  );
});

test('a captured selection comes back, and the clipboard is put where it was', () => {
  return (async () => {
    const clipboard = fakeClipboard('what the person had copied');
    const capture = await captureSelection(
      async () => {
        await clipboard.write('the selected passage');
      },
      clipboard,
      'win32',
    );

    assert.deepStrictEqual(capture, { text: 'the selected passage', failure: '' });
    assert.strictEqual(clipboard.held(), 'what the person had copied', 'the clipboard was taken, not borrowed');
  })();
});

test('a clipboard somebody else wrote to during the window is left alone', async () => {
  // The window is over a second wide. Restoring over a copy made in another app during it would be
  // a tidy-up that destroys somebody's work. (Raised on the code round; this is the check.)
  const clipboard = fakeClipboard('what the person had copied');
  let step = 0;
  const watching: Clipboard = {
    read: async () => {
      step += 1;
      // 1: the borrow. 2: what our copy landed. 3: another app got there first.
      return step === 1 ? 'what the person had copied' : step === 2 ? 'the selected passage' : 'something else entirely';
    },
    write: clipboard.write,
  };

  const capture = await captureSelection(async () => undefined, watching, 'win32');

  assert.strictEqual(capture.text, 'the selected passage');
  assert.deepStrictEqual(
    clipboard.writes().slice(1),
    [],
    'the newer clipboard was overwritten by the restore',
  );
});

test('a copy that landed nothing says so, and still gives the clipboard back', async () => {
  const clipboard = fakeClipboard('untouched');

  const capture = await captureSelection(async () => undefined, clipboard, 'win32');

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /nothing was copied/);
  assert.strictEqual(clipboard.held(), 'untouched', 'a failed capture kept the sentinel');
});

test('somewhere that is not Windows refuses in words, and names the way through', async () => {
  const clipboard = fakeClipboard('untouched');

  const capture = await captureSelection(async () => undefined, clipboard, 'darwin');

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /right-click menu/, 'the refusal does not say what to do instead');
  assert.deepStrictEqual(clipboard.writes(), [], 'a platform that cannot capture still touched the clipboard');
});

test('the restore rule is one comparison, and it is the whole guard', () => {
  assert.strictEqual(shouldRestore('what we captured', 'what we captured'), true);
  assert.strictEqual(shouldRestore('somebody else wrote this', 'what we captured'), false);
});
