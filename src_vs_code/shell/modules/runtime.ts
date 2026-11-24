// The `runtime` module (v2 E4 story s4-chat-carried, task t1): the state that must exist ONCE per extension host, made once.
// The loader (`shell/lazy.ts`) memoises this module and hands its `runtime` to every bundle by reference (`src/hostRuntime.ts`); no other bundle may carry
// `src/runtimeState.ts` or this file (`forbidInputs` in `scripts/bundle-entries.json`, checked by the entry firewall). What the object holds, and why it is
// not module-level state in each bundle, is said in `src/runtimeState.ts`.
import { createRuntime } from '../../src/runtimeState';

export const runtime = createRuntime();
