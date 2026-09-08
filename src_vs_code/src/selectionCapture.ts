import { ProcessHandle } from './processLauncher';

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
 * command is invoked from a KEYBINDING the person is still physically holding `Ctrl` and `Alt`: a
 * synthetic `Ctrl+C` on top of a held `Alt` is `Ctrl+Alt+C`, which copies nothing. Every modifier is
 * released first.</p>
 *
 * <p><b>The clipboard is borrowed, never taken.</b> It is saved, used as a channel, and put back —
 * but only if nothing else wrote to it while we were busy. The window is over a second wide, and a
 * person who copied something in another window during it would otherwise find their copy replaced
 * by a tidy-up. Raised on the code round; the check is one comparison and it is tested.</p>
 */

/** What the clipboard holds while we watch to see whether the copy landed. */
const SENTINEL = 'COAI-CHAT-NOTHING-WAS-COPIED';

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
  public const uint KEYUP = 0x0002;
  public const byte CTRL = 0x11, ALT = 0x12, SHIFT = 0x10, KC = 0x43;
}
"@
Start-Sleep -Milliseconds 180
foreach ($vk in @([CoaiKeys]::ALT, [CoaiKeys]::CTRL, [CoaiKeys]::SHIFT)) {
  [CoaiKeys]::keybd_event($vk, 0, [CoaiKeys]::KEYUP, [IntPtr]::Zero)
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
  run: () => Promise<void>,
  clipboard: Clipboard,
  platform: string = process.platform,
): Promise<Capture> {
  if (platform !== 'win32') {
    // Not a silent no-op: a refusal that names the way through is the difference between a broken
    // feature and one that works differently here. The menu path needs no synthetic keystroke.
    return {
      text: '',
      failure: 'copying the selection needs Windows — copy it yourself, then use “Chat with other AI” from the right-click menu',
    };
  }

  const borrowed = await clipboard.read();
  await clipboard.write(SENTINEL);
  await run();
  const captured = await clipboard.read();

  if (captured === SENTINEL) {
    await clipboard.write(borrowed);

    return {
      text: '',
      failure: 'nothing was copied — select the text first, or copy it yourself and use the right-click menu',
    };
  }

  if (shouldRestore(await clipboard.read(), captured)) {
    await clipboard.write(borrowed);
  }

  return { text: captured, failure: '' };
}

/** Wait for a helper process to finish, however it finishes. Never rejects. */
export function ran(child: ProcessHandle, after: (ms: number, run: () => void) => () => void, capMs = 6000): Promise<void> {
  return new Promise((resolve) => {
    let done = false;
    const finish = (): void => {
      if (done) {
        return;
      }
      done = true;
      cancel();
      resolve();
    };
    const cancel = after(capMs, () => {
      child.kill();
      finish();
    });
    child.onExit(() => finish());
    child.onError(() => finish());
  });
}
