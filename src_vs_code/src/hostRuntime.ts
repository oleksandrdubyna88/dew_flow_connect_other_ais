import { join } from 'node:path';
import type { HostRuntime } from './runtimeState';

/**
 * How a bundle reaches the state that exists once per host (v2 E4 s4-chat-carried t1; `runtimeState.ts` says what it holds and why).
 *
 * <p>The shell's loader (`shell/lazy.ts`) puts a handle on `globalThis` under {@link SHELL_KEY} the first time it loads a module; the handle's
 * `runtime()` answers the exports of the one `runtime` module the loader memoises. So every bundle — chat, legacy, any later one — holds the SAME
 * object by reference, and none carries `runtimeState.ts`. `shell/ids.ts` spells the key too (it imports nothing, so it cannot share this
 * constant); `hostRuntime.test.ts` fails when the two differ.</p>
 *
 * <p>With no shell — a unit test, a script run from `out/` — there is no handle: the runtime is made once per process from the compiled
 * `runtimeState.js` beside this file. The require is not a literal, so a bundle never carries it (a bundle always has the shell).</p>
 */
export const SHELL_KEY = Symbol.for('coai.shell');

/** What the shell's loader publishes. */
interface ShellHandle {
  readonly runtime: () => HostRuntime;
  readonly module: <M>(id: string) => Promise<M>;
}

let own: HostRuntime | undefined;

/** The runtime of this host: the shell's when there is one, else this process's own. */
export function hostRuntime(): HostRuntime {
  const shell = (globalThis as unknown as Record<symbol, ShellHandle | undefined>)[SHELL_KEY];
  if (shell !== undefined) {
    return shell.runtime();
  }
  if (own === undefined) {
    // Not a literal: esbuild must not bundle runtimeState into a module that has the shell.
    const load = require as (path: string) => { readonly createRuntime: () => HostRuntime };
    own = load(join(__dirname, 'runtimeState.js')).createRuntime();
  }

  return own;
}

/**
 * Another shell module, loaded through the shell's own loader — the one memoised copy, never a `require` of this bundle's own. The legacy module's
 * review page hands a bug to the chat this way (`chat`: `ChatModuleDoor`). Rejects when there is no shell: nothing here may start a second copy.
 */
export function loadShellModule<M>(id: string): Promise<M> {
  const shell = (globalThis as unknown as Record<symbol, ShellHandle | undefined>)[SHELL_KEY];

  return shell === undefined ? Promise.reject(new Error(`ConnectOtherAIs: the module ${id} is reached through the shell, and this code runs without one`)) : shell.module<M>(id);
}

/** The id the shell loads the chat module under (`shell/ids.ts`'s `MODULE_CHAT`; `hostRuntime.test.ts` holds the two together). */
export const MODULE_CHAT = 'chat';

/** What the chat module offers the old code: the review page's `Chat` on a bug. */
export interface ChatModuleDoor {
  readonly chooseFromBug: (context: import('vscode').ExtensionContext, chat: unknown) => Promise<void>;
}
