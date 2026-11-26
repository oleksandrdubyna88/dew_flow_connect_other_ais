import type { DoorAnswer } from './remotePermission';

/**
 * What a `wsl.exe` call came to (E3.S1, § W10) — the type and the one rule about it, in a module with no node import, so the round and the table that
 * the tab's page bundles never pull the process launcher in.
 */

/** How a call that was not answered ended. `door` carries the door's own answer. */
export type WslEnd = 'door' | 'platform' | 'missing' | 'argv' | 'not-started' | 'timeout' | 'capped' | 'wsaetimedout' | 'exit';

export type WslRun =
  | { readonly kind: 'answered'; readonly code: number; readonly stdout: string }
  | { readonly kind: 'refused'; readonly refusal: string; readonly end: WslEnd; readonly door?: DoorAnswer };

/** A strike: the call ended at a 30 s limit, ours or WSL's own (§ W10). Neither is a fault of a script. */
export const isStrike = (run: WslRun): boolean => run.kind === 'refused' && (run.end === 'timeout' || run.end === 'wsaetimedout');
