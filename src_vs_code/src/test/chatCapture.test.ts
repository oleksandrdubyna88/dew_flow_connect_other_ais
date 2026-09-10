import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CLIPBOARD_SENTINEL,
  COPY_SCRIPT,
  Clipboard,
  RunOutcome,
  argvFor,
  captureSelection,
  markerFor,
  ran,
  refusalFor,
  shouldRestore,
} from '../selectionCapture';
import { WindowsReach } from '../hostSide';

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

/**
 * The three sides a capture can be attempted from, named rather than spelled at every call.
 *
 * <p>They replace the `platform` string this function used to take, and the replacement is the point:
 * `'linux'` was two different situations wearing one word — a WSL window with a Windows session one
 * hop away, and a Linux box with none — and the old signature could not tell them apart.</p>
 */
const DIRECT: WindowsReach = { kind: 'direct' };
const INTEROP: WindowsReach = { kind: 'interop' };
const NONE: WindowsReach = { kind: 'none' };

/** A helper that started, ran and exited cleanly — whatever it did or did not put on the clipboard. */
const RAN: RunOutcome = { phase: 'ran', refusal: '' };

/** The commonest fake: the helper ran and copied nothing. */
const copied = async (): Promise<RunOutcome> => RAN;

test('the helper releases every modifier before it presses anything', () => {
  // A keybinding leaves Ctrl and Alt physically down. A synthetic Ctrl+C on top of a held Alt is
  // Ctrl+Alt+C, which copies nothing — measured, and the reason these three lines exist.
  const releases = COPY_SCRIPT.indexOf('KEYUP, [IntPtr]::Zero)');
  const press = COPY_SCRIPT.indexOf('keybd_event([CoaiKeys]::CTRL, 0, 0,');

  assert.ok(releases > 0, 'nothing is released');
  assert.ok(press > releases, 'the chord is built before the modifiers are let go');
});

test('the helper waits for the person to let go before it touches the keyboard state at all', () => {
  // Two reviewers, independently: a synthetic key-up on a key somebody is PHYSICALLY holding does
  // not stay up — the hardware repeat re-asserts it — and if it does stay up, the person's next
  // keystroke arrives unmodified. The fix is not to fight it: ask whether the keys are still down,
  // and in the ordinary case (a keypress is over long before this runs) release nothing whatever.
  const waits = COPY_SCRIPT.indexOf('if (-not $held) { break }');
  const releases = COPY_SCRIPT.indexOf('KEYUP, [IntPtr]::Zero)');

  assert.match(COPY_SCRIPT, /GetAsyncKeyState/, 'nothing asks whether the modifiers are still held');
  assert.ok(waits > 0, 'nothing waits for them to come up');
  assert.ok(waits < releases, 'the state is changed before anybody waited for it to sort itself out');
  assert.match(
    COPY_SCRIPT,
    /if \(\[CoaiKeys\]::Held\(\$vk\)\) \{ \[CoaiKeys\]::keybd_event/,
    'a key that is already up is released again — that is the state change nobody asked for',
  );
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

        return RAN;
      },
      clipboard,
      DIRECT,
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

  const capture = await captureSelection(copied, watching, DIRECT);

  assert.strictEqual(capture.text, 'the selected passage');
  assert.deepStrictEqual(
    clipboard.writes().slice(1),
    [],
    'the newer clipboard was overwritten by the restore',
  );
});

test('a copy that landed nothing says so, and still gives the clipboard back', async () => {
  const clipboard = fakeClipboard('untouched');

  const capture = await captureSelection(copied, clipboard, DIRECT);

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /nothing was copied/);
  assert.strictEqual(clipboard.held(), 'untouched', 'a failed capture kept the sentinel');
});

test('somewhere that is not Windows refuses in words, and names the way through', async () => {
  const clipboard = fakeClipboard('untouched');

  const capture = await captureSelection(copied, clipboard, NONE);

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /right-click menu/, 'the refusal does not say what to do instead');
  assert.deepStrictEqual(clipboard.writes(), [], 'a platform that cannot capture still touched the clipboard');
});

test('a clipboard we could not read as text is never written to', async () => {
  // Somebody had an IMAGE on the clipboard. `vscode.env.clipboard` is text-only, so the borrow reads
  // an empty string and the old code wrote a sentinel over the image and then "restored" the empty
  // string it had read — destroying, in the FAILURE case, something it never held. When there is
  // nothing to give back, the empty clipboard is its own sentinel and we touch nothing.
  const clipboard = fakeClipboard('');

  const capture = await captureSelection(copied, clipboard, DIRECT);

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /nothing was copied/);
  assert.deepStrictEqual(clipboard.writes(), [], 'an unreadable clipboard was overwritten anyway');
});

test('a copy onto an unreadable clipboard is kept, not replaced by the emptiness we read', async () => {
  const clipboard = fakeClipboard('');

  const capture = await captureSelection(
    async () => {
      await clipboard.write('the selected passage');

      return RAN;
    },
    clipboard,
    DIRECT,
  );

  assert.strictEqual(capture.text, 'the selected passage');
  assert.strictEqual(clipboard.held(), 'the selected passage', 'the restore blanked the clipboard');
});

test('a passage that happens to BE the marker is still a passage', async () => {
  // A fixed sentinel is a string somebody can copy. Whoever selects the line that names it in this
  // repository's own source would be told nothing was copied — for content, which is the one thing
  // a capture must never judge. The marker is unique per call instead. (local, the code round.)
  const clipboard = fakeClipboard('what the person had copied');

  const capture = await captureSelection(
    async () => {
      await clipboard.write(CLIPBOARD_SENTINEL);

      return RAN;
    },
    clipboard,
    DIRECT,
  );

  assert.strictEqual(capture.text, CLIPBOARD_SENTINEL);
  assert.strictEqual(capture.failure, '');
});

test('a WSL window captures the selection exactly as a Windows one does', async () => {
  // The defect this branch exists for. The extension host is linux in a Remote-WSL window, but the
  // editor window is a Windows one and the helper is one interop hop away — measured on the
  // operator's machine at 1.07 s, against a 6 s cap.
  const clipboard = fakeClipboard('what the person had copied');

  const capture = await captureSelection(
    async () => {
      await clipboard.write('the selected passage');

      return RAN;
    },
    clipboard,
    INTEROP,
  );

  assert.deepStrictEqual(capture, { text: 'the selected passage', failure: '' });
});

test('when the Windows side cannot be reached through interop, the refusal says so — not that nothing was copied', async () => {
  // The lie this change removes. The helper never started — interop switched off in /etc/wsl.conf,
  // a docker-desktop distro with no /mnt/c, a PATH without System32 — and the person was told to
  // select some text, which they had already done.
  const clipboard = fakeClipboard('what the person had copied');

  const capture = await captureSelection(
    async () => ({ phase: 'neverStarted', refusal: 'spawn powershell.exe ENOENT' }),
    clipboard,
    INTEROP,
  );

  assert.strictEqual(capture.text, '');
  assert.match(capture.failure, /could not be reached/);
  assert.match(capture.failure, /ENOENT/, 'what the system actually said was thrown away');
  assert.doesNotMatch(capture.failure, /select the text first/);
  assert.strictEqual(clipboard.held(), 'what the person had copied', 'a helper that never ran kept the borrow');
});

test('a direct Windows host whose helper itself fails also says so, not that nothing was copied', async () => {
  const clipboard = fakeClipboard('what the person had copied');

  const capture = await captureSelection(
    async () => ({ phase: 'neverStarted', refusal: 'spawn powershell.exe EACCES' }),
    clipboard,
    DIRECT,
  );

  assert.match(capture.failure, /could not be started/);
  assert.doesNotMatch(capture.failure, /interop|Windows side/, 'a Windows host was told about a hop it does not make');
});

test('a helper that started and then hung is never reported as a Windows side that could not be reached', async () => {
  // Three reviewers raised this from three directions on the plan round. A machine under load and a
  // machine with interop switched off need two different things done about them.
  const clipboard = fakeClipboard('what the person had copied');

  const capture = await captureSelection(
    async () => ({ phase: 'timedOut', refusal: 'it was still running after 6000 ms' }),
    clipboard,
    INTEROP,
  );

  assert.match(capture.failure, /did not finish in time/);
  assert.doesNotMatch(capture.failure, /could not be reached/, 'a slow machine was reported as an unreachable one');
});

test('a helper that copied the passage and then exited badly is still a capture', async () => {
  // The phase explains a failure; it never overrules the evidence. Refusing a passage the person can
  // see was copied, on the strength of an exit code, would throw away work that plainly happened.
  const clipboard = fakeClipboard('what the person had copied');

  const capture = await captureSelection(
    async () => {
      await clipboard.write('the selected passage');

      return { phase: 'failed', refusal: 'it exited 1' };
    },
    clipboard,
    DIRECT,
  );

  assert.strictEqual(capture.text, 'the selected passage');
  assert.strictEqual(capture.failure, '');
});

test('every refusal but one names the way through, and only the true one mentions the selection', () => {
  const ranCleanly = refusalFor(INTEROP, RAN);

  assert.match(ranCleanly, /select the text first/, 'the one case where the selection IS the problem');
  for (const outcome of [
    { phase: 'neverStarted', refusal: 'ENOENT' },
    { phase: 'timedOut', refusal: 'still running' },
    { phase: 'failed', refusal: 'exited 1' },
  ] as const) {
    const said = refusalFor(INTEROP, outcome);

    assert.match(said, /right-click menu/, `${outcome.phase} does not say what to do instead`);
    assert.doesNotMatch(said, /select the text first/, `${outcome.phase} blames the person's selection`);
  }
});

/** A process handle that does nothing until a test makes it do something. */
function fakeChild(): {
  handle: Parameters<typeof ran>[0];
  exit(code: number): void;
  fail(reason: string): void;
  killed(): boolean;
} {
  let onExit: (code: number) => void = () => undefined;
  let onError: (reason: string) => void = () => undefined;
  let wasKilled = false;

  return {
    handle: {
      pid: 1,
      writeLine: () => true,
      writeAndEnd: () => true,
      onStdout: () => () => undefined,
      onLine: () => () => undefined,
      onExit: (listener) => {
        onExit = listener;
      },
      onError: (listener) => {
        onError = listener;
      },
      stderrTail: () => 'the helper said this',
      kill: () => {
        wasKilled = true;
      },
    },
    exit: (code) => onExit(code),
    fail: (reason) => onError(reason),
    killed: () => wasKilled,
  };
}

test('ran names the phase it stopped in, and settles exactly once', async () => {
  const clean = fakeChild();
  const cleanly = ran(clean.handle, () => () => undefined);
  clean.exit(0);
  assert.deepStrictEqual(await cleanly, { phase: 'ran', refusal: '' });

  const broken = fakeChild();
  const badly = ran(broken.handle, () => () => undefined);
  broken.exit(1);
  const failed = await badly;
  assert.strictEqual(failed.phase, 'failed');
  assert.match(failed.refusal, /exited 1: the helper said this/, 'what the child said was dropped');

  const absent = fakeChild();
  const never = ran(absent.handle, () => () => undefined);
  absent.fail('spawn powershell.exe ENOENT');
  assert.deepStrictEqual(await never, { phase: 'neverStarted', refusal: 'spawn powershell.exe ENOENT' });
});

test('ran kills a helper that outlives the cap, and calls it a timeout rather than a failure', async () => {
  const hung = fakeChild();
  let fire: () => void = () => undefined;

  const outcome = ran(hung.handle, (_ms, run) => {
    fire = run;

    return () => undefined;
  }, 6000);
  fire();
  const said = await outcome;

  assert.strictEqual(said.phase, 'timedOut');
  assert.match(said.refusal, /6000 ms/);
  assert.ok(hung.killed(), 'a helper past its cap was left running');
  // The exit that follows the kill must not overwrite the answer already given.
  hung.exit(0);
  assert.strictEqual((await outcome).phase, 'timedOut');
});

test('two captures never borrow under the same marker', () => {
  assert.notStrictEqual(markerFor(), markerFor(), 'the marker is fixed, so its collision is too');
});

test('the restore rule is one comparison, and it is the whole guard', () => {
  assert.strictEqual(shouldRestore('what we captured', 'what we captured'), true);
  assert.strictEqual(shouldRestore('somebody else wrote this', 'what we captured'), false);
});
