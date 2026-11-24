import * as assert from 'node:assert/strict';
import type * as vscode from 'vscode';

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

/**
 * Activates the extension under test AND starts its old code and its chat module, then answers what those returned, as one object.
 *
 * <p>The shell's `activate()` only registers (v2 E2 S1), so it returns two doors: `legacy()` loads the `legacy` module and runs the old `activate()` once,
 * and `chat()` (v2 E4) loads the `chat` module, which owns the chat commands, the serializer, the remote-sessions road and the skip-list check. The scenarios
 * below were written against the one old call's result and its start-up work (watchers, the settings mirror, the migrations, the chat store); asking for both
 * here is what a command, the sidebar or a restored chat tab does in the field, on first use. The two `shell` test doors are joined, so the
 * hand-over scenario reads one list: the commands the two adopters hold between them, and a view provider AND a serializer.</p>
 */
export async function activated<T>(extension: vscode.Extension<T>): Promise<T> {
  const shell = await extension.activate() as unknown as { readonly legacy: () => Promise<T>; readonly chat: () => Promise<ChatDoors>; readonly install: () => Promise<InstallDoors> };
  const old = await shell.legacy() as T & { readonly shell?: LegacyDoor };
  const chat = await shell.chat();
  const { chat: chatDoor, ...doors } = chat;
  // The install module (v2 E4 S5) holds `coai.installServer`; its work is not awaited here (it asks the network), only its door is read.
  const installDoor = (await shell.install()).install;

  return {
    ...old,
    ...doors,
    ...(old.shell === undefined || chatDoor === undefined ? {} : {
      shell: {
        adoptedCommands: () => [...old.shell?.adoptedCommands() ?? [], ...chatDoor.adoptedCommands(), ...installDoor?.adoptedCommands() ?? []],
        adoptedViewAndSerializer: () => old.shell?.adoptedView() === true && chatDoor.adoptedSerializer(),
      },
    }),
  };
}

interface LegacyDoor {
  readonly adoptedCommands: () => readonly string[];
  readonly adoptedView: () => boolean;
}

interface InstallDoors {
  readonly install?: { readonly adoptedCommands: () => readonly string[] };
}

interface ChatDoors {
  readonly remoteSessions?: unknown;
  readonly terminalAccounts?: unknown;
  readonly skipShell?: unknown;
  readonly chat?: { readonly adoptedCommands: () => readonly string[]; readonly adoptedSerializer: () => boolean };
}
