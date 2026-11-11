import * as assert from 'node:assert/strict';

import { ChatPanels } from '../../chatPanels';
import { threads, type Thread } from '../../chatThread';

/** Helpers that more than one host scenario file needs, kept out of `scenarios.ts` so no scenario file imports the list that imports it. */

/**
 * Wait for something to become true, or say what did not happen.
 *
 * <p>Bounded, and the sentence is the caller's: a scenario that hangs until the launcher's own
 * ten-minute budget expires reports "the extension host did not finish", which names the harness
 * rather than the defect.</p>
 */
export async function until(ready: () => boolean, whatDidNotHappen: string, ms = 15_000): Promise<void> {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (ready()) {
      return;
    }
    // Polling on purpose — each probe must finish before the next one starts (S9382).
    await new Promise((wake) => setTimeout(wake, 100)); // NOSONAR
  }
  assert.fail(`${whatDidNotHappen} (waited ${Math.round(ms / 1000)}s)`);
}

/**
 * A chat registered under `key` the way a real one is, carrying only what the way back reads: its store
 * id and its source. Cast for the reason `conversationHolding` gives — and if the command ever reads a
 * third field, it throws here rather than passing.
 */
export function registeredChat(panels: ChatPanels, key: object, saveId: string, source: Thread['source'], active = false): object {
  const id = {};
  threads.set(id, { saveId, source } as unknown as Thread);
  panels.open(key, saveId, () => ({
    id,
    panel: { reveal() {}, dispose() {}, post() {}, isActive: () => active },
    session: { dispose() {} },
  }));

  return id;
}
