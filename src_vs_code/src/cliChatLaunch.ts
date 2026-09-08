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
 * The command line for one vendor row, or the reason there is none.
 *
 * @param tempDir a directory with nothing in it — see the note below
 */
export function launchSpecFor(vendor: Vendor, tempDir: string): LaunchSpec {
  if (!CHAT_RUNTIMES.includes(vendor.runtime)) {
    // Refused BY NAME rather than routed through a protocol it does not speak. `vendor-routing.md`
    // is explicit that a Claude model never goes through `agy`, and the failure would be invisible
    // in the output — which is exactly why that rule exists.
    return {
      executable: '',
      args: [],
      cwd: '',
      refusal: `${vendor.id} runs on ${vendor.runtime}, and the chat can only speak to ${CHAT_RUNTIMES.join(', ')} so far`,
    };
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
