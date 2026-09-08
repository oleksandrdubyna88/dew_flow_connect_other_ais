import { CHAT_RUNTIMES } from './chatModels';
import { Vendor } from './vendors';

/**
 * How a vendor row becomes a command line.
 *
 * <p>Pure, so the flags are a test rather than a claim — and they are worth a test, because three of
 * the four were chosen against a measured failure rather than from a manual.</p>
 *
 * <p><b>The orphan ledger is NOT here.</b> Story 1.4 of the master plan owns recording a started
 * child so a crashed extension host cannot leave an authenticated vendor process running. This file
 * is the argv half of that story, brought forward because the trigger needs it; the ledger half
 * stays open, and the plan still says so.</p>
 */

/** What the chat needs from a launch, decided without touching the world. */
export interface LaunchSpec {
  readonly executable: string;
  readonly args: readonly string[];
  /** Empty means "wherever the caller likes" — but for this feature it is always a temp directory. */
  readonly cwd: string;
  /** Empty when the row can be launched; otherwise why it cannot. */
  readonly refusal: string;
}

/**
 * The flags, and why each one is there.
 *
 * <ul>
 *   <li><b>`--input-format stream-json`</b> — the captured passage travels on STDIN. Measured: `-p`
 *       does not read stdin at all (the model answered "you did not attach the fragment"), and argv
 *       on Windows is the truncation trap this family has already been bitten by.</li>
 *   <li><b>`--output-format stream-json`</b> — one NDJSON event per line, which is what makes a turn
 *       distinguishable from a log line.</li>
 *   <li><b>`--mode plan`</b> — read-only. The task is "explain this paragraph"; nothing should be
 *       written by a model answering it. Measured to survive multi-turn, against a reviewer who
 *       called it a single-prompt batch.</li>
 *   <li><b>`--disable-slash-commands`</b> — the passage is another AI's text and can begin with a
 *       slash. This is the first of two guards; the fence in `chatPrompt.ts` is the second.</li>
 * </ul>
 */
export const AGY_ARGS: readonly string[] = [
  '--mode',
  'plan',
  '--disable-slash-commands',
  '--input-format',
  'stream-json',
  '--output-format',
  'stream-json',
];

/**
 * Why this row cannot be chatted with, or an empty string.
 *
 * <p>Its own function because the answer is needed BEFORE anything is created — a tab, a process, a
 * directory. `launchSpecFor` asks it too, so there is one sentence and one rule rather than two.</p>
 */
export function chatRuntimeRefusal(vendor: Vendor): string {
  if (CHAT_RUNTIMES.includes(vendor.runtime)) {
    return '';
  }

  // Refused BY NAME rather than routed through a protocol it does not speak. `vendor-routing.md`
  // is explicit that a Claude model never goes through `agy`, and the failure would be invisible
  // in the output — which is exactly why that rule exists.
  return `${vendor.id} runs on ${vendor.runtime}, and the chat can only speak to ${CHAT_RUNTIMES.join(', ')} so far`;
}

/** A directory of its own for one conversation, and the way to take it away again. */
export interface ChatHome {
  readonly dir: string;
  /** Remove it. Idempotent, and never throws — a tab closing must not fail on a locked file. */
  release(): void;
}

/**
 * Make the directory a conversation runs in, and hold the way to remove it.
 *
 * <p>The gate asked what cleans up after a launch, and the honest answer was: nothing. Worse, the
 * directory was made on every INVOCATION — pressing `Ctrl+Alt+A` against a tab that is already open
 * starts no process at all, and left an empty directory in `%TEMP%` each time. It is made where it
 * is used now, and released when the conversation closes.</p>
 *
 * <p>Injected `make` and `remove` so the two guarantees — made once, removed once, whatever happens
 * — are a test rather than a claim. `dispose` and `closeAll` can both arrive for the same tab.</p>
 */
export function chatHome(make: () => string, remove: (dir: string) => void): ChatHome {
  const dir = make();
  let released = false;

  return {
    dir,
    release: () => {
      if (released) {
        return;
      }
      released = true;
      try {
        remove(dir);
      } catch {
        // An empty directory nobody can delete is not worth a message, let alone a failed close.
      }
    },
  };
}

/**
 * The command line for one vendor row, or the reason there is none.
 *
 * @param tempDir a directory with nothing in it — see the note below
 */
export function launchSpecFor(vendor: Vendor, tempDir: string): LaunchSpec {
  const refusal = chatRuntimeRefusal(vendor);
  if (refusal.length > 0) {
    return { executable: '', args: [], cwd: '', refusal };
  }

  return {
    executable: vendor.executablePath.length > 0 ? vendor.executablePath : 'agy',
    args: AGY_ARGS,
    // An empty temp directory, never the workspace. The task is to explain a paragraph: handing a
    // third-party agent the source tree buys nothing but startup time, and on Windows a working
    // directory is also something `cmd.exe` searches before the PATH.
    cwd: tempDir,
    refusal: '',
  };
}
