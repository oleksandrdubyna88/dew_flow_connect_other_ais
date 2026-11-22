// The `chat` module (v2 E4 story s4-chat-carried): chat WITHOUT the legacy module.
// `../../measure` FIRST: this bundle's own copy of the recorder evaluates before the chat code does, and that moment is `moduleEval:chat`.
//
// The shell registers every chat command and the `coaiChat` serializer once (shell/entry.ts); their thunks load THIS module. The first call
// runs `registerChat` (./register.ts) exactly once — the data root, side-specific models, remote sessions, presets, the chat store with its migration,
// housekeeping and orphan sweep, the rename follower — and hands each registered handler to the shell through the bridge, as the legacy module does
// for its own commands. The legacy module never starts any of this (its activation had the chat block cut out), so a window that loads both starts it once.
import { mark } from '../../measure';
import * as vscode from 'vscode';
import { adoptRegistrationsFor } from '../../../src/shellBridge';
import { chooseFromBug as chooseFromBugIn } from '../../../src/chatCommand';
import { flushChatUsage } from '../../../src/chatUsageFile';
import { testDoorOf } from '../../../src/remoteSessionHost';
import { CHAT_COMMANDS, PANEL_CHAT } from '../../ids';
import type { ChatModule } from '../../lazy';
import { type ChatRegistration, registerChat } from './register';

type Handler = (...args: unknown[]) => unknown;

const owned = new Set<string>(CHAT_COMMANDS);
const handlers = new Map<string, Handler>();
let serializer: vscode.WebviewPanelSerializer | undefined;

/** The outcome of the one registration; a failure is kept, so a second call re-throws it rather than starting chat again. */
let outcome: { readonly started: ChatRegistration } | { readonly failed: unknown } | undefined;

/** Hands the chat commands and the serializer the registration makes to the shell, and refuses everything else (a view provider, another module's command). */
function adopt(): void {
  adoptRegistrationsFor({
    command: (id, run) => {
      if (!owned.has(id)) {
        return false;
      }
      handlers.set(id, run);

      return true;
    },
    viewProvider: () => false,
    serializer: (viewType, held) => {
      if (viewType !== PANEL_CHAT) {
        return false;
      }
      serializer = held;

      return true;
    },
  });
}

/** Starts chat once and answers what it made. */
function started(context: vscode.ExtensionContext): ChatRegistration {
  if (outcome === undefined) {
    mark('chatStart');
    adopt();
    try {
      outcome = { started: registerChat(context) };
    } catch (failed) {
      outcome = { failed };
    } finally {
      adoptRegistrationsFor(undefined);
      mark('chatEnd');
    }
  }
  if ('failed' in outcome) {
    throw outcome.failed;
  }

  return outcome.started;
}

export const command: ChatModule['command'] = (context, id, args) => {
  started(context);
  const handler = handlers.get(id);
  if (handler === undefined) {
    throw new Error(`ConnectOtherAIs: the command ${id} is registered by the shell for chat, but the chat module did not register it`);
  }

  return handler(...args);
};

export const restorePanel: ChatModule['restorePanel'] = (context, panel, state) => {
  started(context);

  return serializer?.deserializeWebviewPanel(panel, state);
};

/** The legacy sidebar's `Chat` on a bug: the bug goes into a chat through the one registry this module made. */
export const chooseFromBug: ChatModule['chooseFromBug'] = (context, chat) => {
  const { chatPanels } = started(context);

  return chooseFromBugIn(chatPanels, context.extensionUri, chat as Parameters<typeof chooseFromBugIn>[2]);
};

/** What a test host may read of the chat module: the hand-over's side, as `ShellTestDoor` for the legacy one, and the two old test doors that moved here. */
export interface ChatTestDoor {
  readonly adoptedCommands: () => readonly string[];
  readonly adoptedSerializer: () => boolean;
}

export const start: ChatModule['start'] = (context) => {
  const registered = started(context);

  return context.extensionMode === vscode.ExtensionMode.Test
    ? {
      remoteSessions: testDoorOf(registered.remote),
      skipShell: registered.skipShell,
      chat: { adoptedCommands: () => [...handlers.keys()], adoptedSerializer: () => serializer !== undefined } satisfies ChatTestDoor,
    }
    : {};
};

/** `deactivate` of the host: the queued usage records are drained (with the ceiling the old `deactivate` had) and the heartbeat's timer is stopped. */
export const deactivate: ChatModule['deactivate'] = () => {
  if (outcome === undefined || !('started' in outcome)) {
    return undefined;
  }
  outcome.started.stopHeartbeat();

  return drainUsage();
};

async function drainUsage(): Promise<void> {
  // The drain has a CEILING and can therefore give up (`flushChatUsage`); what it must not do is give up in silence: the console is the only surface
  // left at this point in the lifecycle. What it says is exactly what is known — the queued writes are not cancelled and normally still land.
  if (!await flushChatUsage()) {
    console.error(
      'ConnectOtherAIs: the ledgers were still writing when this window closed, and the drain gave '
      + 'up waiting. The queued records are not cancelled and normally still land; if the host was '
      + 'killed first, they did not. The usual cause is a data directory on a drive that stopped '
      + 'answering.',
    );
  }
}
