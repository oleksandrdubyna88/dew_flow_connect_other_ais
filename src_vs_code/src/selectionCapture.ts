import { randomUUID } from 'node:crypto';
import { ProcessHandle } from './processLauncher';
import { WindowsReach } from './hostSide';

/**
 * Copying the selection out of somebody else's webview.
 *
 * <p>Claude Code's chat panel offers no way to read what is selected in it — measured, not assumed:
 * neither `data-vscode-context` nor `webviewSection` appears anywhere in the shipped extension, so
 * there is no DOM seam and a command is handed nothing. What DOES work, measured on 2026-09-08 with
 * a throwaway probe extension, is asking the operating system to press `Ctrl+C` for us: 638
 * characters of a real paragraph, in 1725 ms.</p>
 *
 * <p><b>Two things the probe learned that this file exists to keep.</b> `.NET`'s `SendKeys` delivers
 * NOTHING to an Electron window (readback: 0 characters) while `keybd_event` delivers reliably
 * (17 107) — so this is WinAPI, deliberately, and must not be "simplified" back. And when the
 * command is invoked from a KEYBINDING the person may still be physically holding `Ctrl` and `Alt`:
 * a synthetic `Ctrl+C` on top of a held `Alt` is `Ctrl+Alt+C`, which copies nothing.</p>
 *
 * <p><b>So the script WAITS for the chord to come up rather than forcing it up.</b> Two reviewers
 * raised the same objection from opposite directions: a key-up sent while a finger is down does not
 * stay up (the hardware repeat re-asserts it), and if it does stay up, the person's next keystroke
 * arrives with the modifier missing. Both are about the same mistake — changing global keyboard
 * state that belongs to somebody else. `GetAsyncKeyState` asks instead: in the ordinary case the
 * keypress is long over before PowerShell has started and NOTHING is released, and a chord still
 * held after 800 ms is released deliberately, key by key, only where it is actually down.</p>
 *
 * <p><b>The clipboard is borrowed, never taken.</b> It is saved, used as a channel, and put back —
 * but only if nothing else wrote to it while we were busy. The window is over a second wide, and a
 * person who copied something in another window during it would otherwise find their copy replaced
 * by a tidy-up. Raised on the code round; the check is one comparison and it is tested.</p>
 *
 * <p><b>What the borrow cannot promise.</b> `vscode.env.clipboard` is text and nothing else: an
 * image or a file on the clipboard is invisible to it, and the person's own `Ctrl+C` replaces it
 * whatever we do. What this file guarantees is narrower and worth stating — it never destroys what
 * it could not read. When the borrow comes back empty, nothing is written at all.</p>
 */

/**
 * What the clipboard holds while we watch to see whether the copy landed.
 *
 * <p>Exported for the test that pins the collision below, not because anybody else needs it.</p>
 */
export const CLIPBOARD_SENTINEL = 'COAI-CHAT-NOTHING-WAS-COPIED';

/**
 * The marker for ONE capture, which is not the same as the marker for the next.
 *
 * <p>A fixed sentinel is a string somebody can copy — this file's own source contains it, and
 * selecting that line would have been reported as "nothing was copied". A capture may fail for a
 * hundred reasons, but never for the CONTENT of what was selected. (local, the code round.)</p>
 *
 * <p>`randomUUID` rather than `Math.random`, and not because a clipboard marker needs to resist an
 * attacker: nothing here is a secret and the string lives for a second. It is that arguing the case
 * costs more than the import — SonarCloud reads `Math.random` as a security question (S2245) and
 * marked the whole change C for it, and a reviewer reading this line a year from now would have to
 * make the same argument again.</p>
 */
export function markerFor(): string {
  return `${CLIPBOARD_SENTINEL}-${randomUUID()}`;
}

/**
 * The whole helper, as one PowerShell program.
 *
 * <p>One exported constant rather than a file on disk: a `.ps1` beside the compiled output is one
 * more thing the packaging step has to remember, and it was forgotten once already in this
 * repository's history for the MCP shim. It travels to PowerShell base64-encoded — see `argvFor` —
 * so no layer of shell quoting can touch it.</p>
 */
export const COPY_SCRIPT = `
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CoaiKeys {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
  public const uint KEYUP = 0x0002;
  public const byte CTRL = 0x11, ALT = 0x12, SHIFT = 0x10, KC = 0x43;
  public static bool Held(byte vk) { return (GetAsyncKeyState((int)vk) & 0x8000) != 0; }
}
"@
$mods = @([CoaiKeys]::ALT, [CoaiKeys]::CTRL, [CoaiKeys]::SHIFT)
Start-Sleep -Milliseconds 120
for ($i = 0; $i -lt 40; $i++) {
  $held = $false
  foreach ($vk in $mods) { if ([CoaiKeys]::Held($vk)) { $held = $true } }
  if (-not $held) { break }
  Start-Sleep -Milliseconds 20
}
foreach ($vk in $mods) {
  if ([CoaiKeys]::Held($vk)) { [CoaiKeys]::keybd_event($vk, 0, [CoaiKeys]::KEYUP, [IntPtr]::Zero) }
}
Start-Sleep -Milliseconds 120
[CoaiKeys]::keybd_event([CoaiKeys]::CTRL, 0, 0, [IntPtr]::Zero)
[CoaiKeys]::keybd_event([CoaiKeys]::KC, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 40
[CoaiKeys]::keybd_event([CoaiKeys]::KC, 0, [CoaiKeys]::KEYUP, [IntPtr]::Zero)
[CoaiKeys]::keybd_event([CoaiKeys]::CTRL, 0, [CoaiKeys]::KEYUP, [IntPtr]::Zero)
Start-Sleep -Milliseconds 350
`;

/**
 * PowerShell's own escape hatch: UTF-16LE, base64.
 *
 * <p>`-Command` with a multi-line script means quoting the thing twice — once for the shell that
 * spawns PowerShell and once for PowerShell — and every layer has its own idea about backticks and
 * `@"`. `-EncodedCommand` has no quoting at all.</p>
 */
export function argvFor(script: string): readonly string[] {
  const encoded = Buffer.from(script, 'utf16le').toString('base64');

  return ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', encoded];
}

/** What a capture came to. `text` is empty whenever `failure` is not. */
export interface Capture {
  readonly text: string;
  readonly failure: string;
}

/**
 * WHERE a helper run stopped — which is a different question from whether anything was copied.
 *
 * <p>It exists because one sentence was doing four jobs. `ran` used to resolve `void`, so a
 * PowerShell that never started, one that hung, one that exited badly and one that ran perfectly over
 * an empty selection all arrived here identically — and the person was told, in every case, to select
 * the text first. Three reviewers raised that independently on the plan round, from three directions:
 * a helper that started and then hung must never be reported as a Windows side that could not be
 * reached, because the two send somebody to two different places.</p>
 */
export type RunPhase =
  /** It started, finished, and exited cleanly. Whether it COPIED anything is the clipboard's answer. */
  | 'ran'
  /** It never started — no such executable, interop switched off, a permission. */
  | 'neverStarted'
  /** It was still running when the cap fired, and was killed. */
  | 'timedOut'
  /** It ran and ended badly. */
  | 'failed';

export interface RunOutcome {
  readonly phase: RunPhase;
  /** Empty when it ran cleanly; otherwise what the system itself said. */
  readonly refusal: string;
}

/** The one instruction every refusal ends with, written once so the four cannot drift apart. */
const THE_WAY_THROUGH = ' — copy it yourself, then use “Chat with other AI” from the right-click menu';

/**
 * The sentence for a capture that landed nothing, chosen by WHERE it stopped and by what could have
 * been reached from here.
 *
 * <p>Pure, and separate from the capture itself, because the wording is the part reviewers argued
 * about and a test can hold it still. The `ran` case is the ONLY one that mentions the selection —
 * it is the only one where the helper actually pressed the keys and the clipboard still came back
 * empty, which is the one situation in which "select the text first" is true.</p>
 *
 * <p>The interop wording is deliberately the sentence this product already shipped for the same fact
 * in `panelProvider`'s `.wslconfig` button, not a fourth phrasing of it. What the system said travels
 * in the brackets rather than being interpreted: `ENOENT` for a missing `powershell.exe` and a
 * permission for interop switched off in `/etc/wsl.conf` are both named by their own words, which is
 * more use than a guess between them and is the reason the plan round's request for a pre-flight
 * probe was refused.</p>
 */
export function refusalFor(reach: WindowsReach, outcome: RunOutcome): string {
  const said: Record<RunPhase, string> = {
    ran: 'nothing was copied — select the text first, or copy it yourself and use the right-click menu',
    // The bracket carries the cap it passed and whatever the helper had printed before it hung — a
    // PowerShell that warned about something on its way to hanging said the useful half there, and
    // dropping it left a sentence that could not be acted on. (local and gemini, the code round.)
    timedOut: `the copy helper did not finish in time (${outcome.refusal})${THE_WAY_THROUGH}`,
    neverStarted: reach.kind === 'interop'
      ? `the Windows side of this machine could not be reached (${outcome.refusal})${THE_WAY_THROUGH}`
      : `the copy helper could not be started (${outcome.refusal})${THE_WAY_THROUGH}`,
    failed: `the copy helper failed (${outcome.refusal})${THE_WAY_THROUGH}`,
  };

  return said[outcome.phase];
}

/** The clipboard, narrowed — `vscode.env.clipboard` in the host, two functions in a test. */
export interface Clipboard {
  read(): Promise<string>;
  write(text: string): Promise<void>;
}

/**
 * Should the borrowed clipboard be put back?
 *
 * <p>Its own function so the rule is tested rather than trusted. Only when what is on the clipboard
 * now is exactly what our own copy put there: anything else means somebody wrote to it during the
 * window, and their content wins.</p>
 */
export function shouldRestore(current: string, captured: string): boolean {
  return current === captured;
}

/**
 * Press `Ctrl+C` for the person, and read what landed.
 *
 * @param run starts the helper and resolves when it has finished
 * @param clipboard the clipboard, injected
 * @param platform `process.platform` in the host
 */
export async function captureSelection(
  run: () => Promise<RunOutcome>,
  clipboard: Clipboard,
  reach: WindowsReach,
): Promise<Capture> {
  if (reach.kind === 'none') {
    // Not a silent no-op: a refusal that names the way through is the difference between a broken
    // feature and one that works differently here. The menu path needs no synthetic keystroke.
    return {
      text: '',
      failure: 'copying the selection needs Windows — copy it yourself, then use “Chat with other AI” from the right-click menu',
    };
  }

  const borrowed = await clipboard.read();
  // Nothing readable as TEXT — an image, a file, a copied cell — reads back as an empty string, and
  // `vscode.env.clipboard` offers no way to see it, let alone put it back. So it is not taken: the
  // emptiness is its own sentinel, and a capture that lands nothing leaves what the person had
  // exactly where it was. Writing a sentinel over an image and then "restoring" the empty string we
  // had read is a tidy-up that destroys something it never held. (gemini, the plan round.)
  const restorable = borrowed.length > 0;
  const marker = restorable ? markerFor() : '';
  if (restorable) {
    await clipboard.write(marker);
  }
  const outcome = await run();
  const captured = await clipboard.read();

  // The CLIPBOARD is asked first and the phase only explains a failure, which is deliberate and not
  // an ordering accident: a helper that copied the passage and then exited badly has still copied the
  // passage, and refusing it on the strength of its exit code would throw away work the person can
  // see happened. So the phase never overrules evidence — it is what turns "nothing landed" from one
  // sentence into four.
  if (captured === marker) {
    if (restorable) {
      await clipboard.write(borrowed);
    }

    return { text: '', failure: refusalFor(reach, outcome) };
  }

  if (restorable && shouldRestore(await clipboard.read(), captured)) {
    await clipboard.write(borrowed);
  }

  return { text: captured, failure: '' };
}

/** How much of a failing helper's stderr is worth putting in a notification. */
const TAIL_IN_A_SENTENCE = 200;

/**
 * Wait for a helper process to finish, and say HOW it finished. Never rejects.
 *
 * <p>It used to resolve `void`, and that was the whole of the second defect in this file: four
 * different endings arrived at the caller as one, so the person was told to select some text
 * whatever had actually gone wrong. Nothing about the waiting changed — the cap still kills, the
 * error still ends it, and it still settles exactly once.</p>
 */
export function ran(
  child: ProcessHandle,
  after: (ms: number, run: () => void) => () => void,
  capMs = 6000,
): Promise<RunOutcome> {
  return new Promise((resolve) => {
    let done = false;
    const finish = (outcome: RunOutcome): void => {
      if (done) {
        return;
      }
      done = true;
      cancel();
      resolve(outcome);
    };
    const cancel = after(capMs, () => {
      child.kill();
      finish({ phase: 'timedOut', refusal: `it was still running after ${capMs} ms${detail(child.stderrTail())}` });
    });
    child.onExit((code) => finish(outcomeOfExit(code, child.stderrTail())));
    // A missing binary does NOT throw synchronously on Windows and does not on Linux either: it
    // arrives here, and it is the ordinary shape of both "no powershell.exe on this PATH" and
    // "interop is switched off". Its own words are worth more than any guess between them.
    child.onError((reason) => finish({ phase: 'neverStarted', refusal: reason }));
  });
}

/**
 * A clean exit ran; anything else failed, carrying what the child said about it.
 *
 * <p>Exported because it is a DECISION and not plumbing — which of the four phases an ending belongs
 * to, and what the sentence for it says. Reached through `ran` it could only be exercised by driving
 * a fake process; on its own it is two lines of test. (codex, the code round.)</p>
 */
export function outcomeOfExit(code: number, tail: string): RunOutcome {
  return code === 0
    ? { phase: 'ran', refusal: '' }
    : { phase: 'failed', refusal: `it exited ${code}${detail(tail)}` };
}

function detail(tail: string): string {
  const said = tail.trim().slice(-TAIL_IN_A_SENTENCE);

  return said.length === 0 ? '' : `: ${said}`;
}
