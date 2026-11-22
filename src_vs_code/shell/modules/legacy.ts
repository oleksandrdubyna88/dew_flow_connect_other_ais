// The `legacy` module: for this story every command, the sidebar view and the chat serializer load here.
// On first use it runs the OLD `activate()` (src/extension.ts) exactly once, and forwards to what that call
// registered. Later stories move features into real modules and this file shrinks to nothing.
import * as vscode from 'vscode';
import { activate as activateOldCode, deactivate as deactivateOldCode, type CoaiExports } from '../../src/extension';
import { adoptRegistrationsFor } from '../../src/shellBridge';
import { recordMarksWith } from '../../src/measureMarks';
import { COMMANDS, PANEL_CHAT, VIEW_PANEL } from '../ids';
import type { ShellModule } from '../lazy';
import { mark } from '../measure';

type Handler = (...args: unknown[]) => unknown;

// The old code names two cold-start moments (the sidebar's `ready`, its first data); the shell's marks record them.
recordMarksWith(mark);

const owned = new Set<string>(COMMANDS);
const handlers = new Map<string, Handler>();
let viewProvider: vscode.WebviewViewProvider | undefined;
let panelSerializer: vscode.WebviewPanelSerializer | undefined;

/** The outcome of the one run of the old `activate()`; a failure is kept, so a second call re-throws it rather than running the old code again. */
let outcome: { readonly started: CoaiExports } | { readonly failed: unknown } | undefined;

function adopt(): void {
  adoptRegistrationsFor({
    command: (id, run) => {
      if (!owned.has(id)) {
        return false;
      }
      handlers.set(id, run);

      return true;
    },
    viewProvider: (viewType, provider) => {
      if (viewType !== VIEW_PANEL) {
        return false;
      }
      viewProvider = provider;

      return true;
    },
    serializer: (viewType, serializer) => {
      if (viewType !== PANEL_CHAT) {
        return false;
      }
      panelSerializer = serializer;

      return true;
    },
  });
}

/** Runs the old `activate()` once and answers what it returned. */
function started(context: vscode.ExtensionContext): CoaiExports {
  if (outcome === undefined) {
    adopt();
    try {
      outcome = { started: activateOldCode(context) };
    } catch (failed) {
      outcome = { failed };
    } finally {
      adoptRegistrationsFor(undefined);
    }
  }
  if ('failed' in outcome) {
    throw outcome.failed;
  }

  return outcome.started;
}

export const command: ShellModule['command'] = (context, id, args) => {
  started(context);
  const handler = handlers.get(id);
  if (handler === undefined) {
    throw new Error(`ConnectOtherAIs: the command ${id} is registered by the shell, but the old activation did not register it`);
  }

  return handler(...args);
};

export const resolveView: ShellModule['resolveView'] = (context, view, resolveContext, token) => {
  started(context);

  return viewProvider?.resolveWebviewView(view, resolveContext, token);
};

export const restorePanel: ShellModule['restorePanel'] = (context, panel, state) => {
  started(context);

  return panelSerializer?.deserializeWebviewPanel(panel, state);
};

// The old code has no `vscode://` handler: the link only starts it, which is what `onUri` is for.
export const handleUri: ShellModule['handleUri'] = (context) => {
  started(context);
};

/** What a test host may read of the shell's side of the hand-over; present only in `ExtensionMode.Test`, like the old exports' own test doors. */
export interface ShellTestDoor {
  /** The command ids the old activation handed to the shell, in no particular order. */
  readonly adoptedCommands: () => readonly string[];
  /** True once the old activation gave the shell a view provider and a panel serializer. */
  readonly adoptedViewAndSerializer: () => boolean;
}

export const start: ShellModule['start'] = (context) => {
  const old = started(context);

  return context.extensionMode === vscode.ExtensionMode.Test
    ? { ...old, shell: { adoptedCommands: () => [...handlers.keys()], adoptedViewAndSerializer: () => viewProvider !== undefined && panelSerializer !== undefined } satisfies ShellTestDoor }
    : old;
};

export const deactivate: ShellModule['deactivate'] = () => (outcome !== undefined && 'started' in outcome ? deactivateOldCode() : undefined);
