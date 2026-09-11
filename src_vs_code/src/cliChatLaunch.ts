import { needsShell } from './cliVersions';
import { Platform } from './hostSide';
import { ChatAdapter, ChatLaunch, NEW_CONVERSATION } from './chatAdapter';
import { agyAdapter } from './agyAdapter';
import { claudeAdapter } from './claudeAdapter';
import { codexAdapter } from './codexAdapter';
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

/**
 * Every runtime that can hold a chat, and the adapter that speaks to it.
 *
 * <p>It lives HERE, next to the launch that implements it, rather than in `chatModels.ts` where the
 * picker is built. A capability belongs with the code that provides it: the old direction had the
 * launch layer importing a constant out of a module that imports the PAGE, so a change to what the
 * chat can speak began in the presentation half. Adding a runtime is one entry in this map, and the
 * picker follows it. (codex, the second code round.)</p>
 *
 * <p><b>Three, since 2026-09-09.</b> It was one because the master plan's build split recorded a
 * limitation as fact — "claude's schema differs and codex exec has no multi-turn stdin". Measured,
 * half of that was wrong: `codex` holds a conversation through `resume`, and `claude` holds one
 * exactly as `agy` does, faster than either. `vendor-routing.md` still binds and this map is how:
 * a Claude model reaches the `claude` CLI and never `agy`, because the runtime chooses the adapter
 * AND the executable together.</p>
 */
const ADAPTERS: Readonly<Record<string, { readonly adapter: ChatAdapter; readonly executable: string }>> = {
  antigravity: { adapter: agyAdapter, executable: 'agy' },
  claude: { adapter: claudeAdapter, executable: 'claude' },
  codex: { adapter: codexAdapter, executable: 'codex' },
};

export const CHAT_RUNTIMES: readonly string[] = Object.keys(ADAPTERS);

/** What a runtime's CLI is called when the reviewer's settings do not say. */
export function defaultExecutableFor(runtime: string): string {
  return ADAPTERS[runtime]?.executable ?? '';
}

/** The adapter for a runtime, or nothing when the chat cannot speak to it. */
export function adapterFor(runtime: string): ChatAdapter | undefined {
  return ADAPTERS[runtime]?.adapter;
}

/** What the chat needs from a launch, decided without touching the world. */
export interface LaunchSpec {
  readonly executable: string;
  readonly args: readonly string[];
  /**
   * Whether this launch needs the platform shell.
   *
   * <p>True for exactly one case: a Windows `.cmd` or `.bat` shim, which node has refused to spawn
   * directly since the 2024 argument-injection fix. Both new vendors are npm shims on this machine,
   * so without this a chat with either of them died at its first turn on `spawn codex ENOENT` —
   * found by the live check rather than by any test, which is what live checks are for.</p>
   */
  readonly shell: boolean;
  /** Empty means "wherever the caller likes" — but for this feature it is always a temp directory. */
  readonly cwd: string;
  /** Empty when the row can be launched; otherwise why it cannot. */
  readonly refusal: string;
}

/**
 * The flags `agy` is launched with. Re-exported from its adapter, which is where they now live.
 *
 * <p>Kept as a name here because the argv and the protocol travelled together when they moved, and
 * a caller that still asks this module for them should get the same answer as the adapter gives.</p>
 */
export { AGY_ARGS } from './agyAdapter';


/**
 * Why this row cannot be chatted with, or an empty string.
 *
 * <p>Its own function because the answer is needed BEFORE anything is created — a tab, a process, a
 * directory. `launchSpecFor` asks it too, so there is one sentence and one rule rather than two.</p>
 */
export function chatRuntimeRefusal(vendor: Vendor): string {
  // A Team server needs no adapter and no executable: it takes a prompt over HTTP and answers it.
  if (CHAT_RUNTIMES.includes(vendor.runtime) || vendor.runtime === 'remote') {
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
 * <p>Injected `make`, `remove` and `report` so the three guarantees — made once, removed once, and
 * a failure SAID rather than swallowed — are tests rather than claims. `dispose` and `closeAll` can
 * both arrive for the same tab, and a directory a virus scanner is holding open can refuse to go.</p>
 *
 * <p>`report` is required, not defaulted. A closing tab is the outer edge of a detached call, and
 * `reliability.md` is explicit that such an edge ends in a catch that LOGS; a default no-op would
 * have made the silence the easy path again, which is how it got here. (codex, the code round.)</p>
 */
export function chatHome(make: () => string, remove: (dir: string) => void, report: (failure: string) => void): ChatHome {
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
      } catch (reason) {
        // Not rethrown: a tab must still close. Said, because an empty directory per conversation
        // that never goes away is exactly the kind of thing nobody notices for a year.
        report(`the chat's temporary directory could not be removed: ${dir} — ${asText(reason)}`);
      }
    },
  };
}

/** A thrown thing, as a sentence. */
function asText(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

/**
 * What a model id may look like before it is put in a command line.
 *
 * <p>The same reason `codexAdapter`'s `THREAD_ID` exists, written down there: this is untrusted input
 * by POSITION rather than by provenance. A model id arrives from settings a person typed, from a Team
 * server's catalog, or from a restored tab, and it leaves in an argv that on Windows goes through
 * `cmd.exe`. **A name beginning with a dash is an OPTION to the CLI, not a model** — and the options
 * these three accept include ones that remove their own restrictions.</p>
 *
 * <p>Deliberately permissive about the middle and strict about the first character and the alphabet:
 * real ids carry dots, colons and slashes (`Qwen3.5-35B-A3B-Q5_vk128:latest`, `openai/gpt-oss-120b`)
 * and a pattern that refused those would break every chat while passing the test above it.</p>
 */
const MODEL_NAME = /^[A-Za-z0-9][A-Za-z0-9._:/-]{0,99}$/;

/**
 * Why this model may not be put on a command line, or an empty string.
 *
 * <p>REFUSED rather than dropped. Dropping it would re-create the defect the model parameter exists to
 * fix — the tab labels an answer from the pick, so a silently discarded model means an answer
 * attributed to a model the CLI was never told about, which is exactly what nobody could see before.</p>
 */
function modelRefusal(model: string): string {
  return model.length === 0 || MODEL_NAME.test(model)
    ? ''
    : `${JSON.stringify(model)} is not a model name this chat will put on a command line`;
}

/**
 * The command line for one vendor row, or the reason there is none.
 *
 * @param tempDir a directory with nothing in it — see the note below
 */
export function launchSpecFor(
  vendor: Vendor,
  tempDir: string,
  /**
   * What this turn is asking for: the thread to continue, and the model the person picked.
   *
   * <p>It used to be the resume id alone, which is why the picker decided nothing — see
   * `ChatAdapter.argv`. The model here is the CONVERSATION's, not the row's: `readyToChat` keeps
   * `modelId` and `vendor.model` apart deliberately, and a row's default is what the person is
   * choosing away from.</p>
   */
  launch: ChatLaunch = NEW_CONVERSATION,
  /** The file the vendor's name resolved to, from `resolvedExecutable`. Empty falls back to the name. */
  resolved = '',
  platform: Platform = process.platform === 'darwin' ? 'darwin' : process.platform === 'win32' ? 'win32' : 'linux',
): LaunchSpec {
  const refusal = chatRuntimeRefusal(vendor) || modelRefusal(launch.model);
  const known = ADAPTERS[vendor.runtime];
  if (refusal.length > 0 || known === undefined) {
    return { executable: '', args: [], cwd: '', shell: false, refusal };
  }
  const executable = [resolved, vendor.executablePath, known.executable].find((name) => name.length > 0) ?? '';

  return {
    executable,
    shell: needsShell(executable, platform),
    // From the adapter, because the command line and the wire protocol are one decision: a vendor
    // launched with another's flags answers in a shape nobody here can read.
    args: known.adapter.argv(launch),
    // An empty temp directory, never the workspace. The task is to explain a paragraph: handing a
    // third-party agent the source tree buys nothing but startup time, and on Windows a working
    // directory is also something `cmd.exe` searches before the PATH.
    cwd: tempDir,
    refusal: '',
  };
}
