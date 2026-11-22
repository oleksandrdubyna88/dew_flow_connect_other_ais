// The `legacy` module: every command that has not gone to a module of its own, and the sidebar view, load here. Chat has gone (`./chat/index.ts`, v2 E4).
// On first use it runs the OLD `activate()` (src/extension.ts) exactly once, and forwards to what that call
// registered. Later stories move features into real modules and this file shrinks to nothing.
// `../measure` FIRST: this bundle's own copy of the recorder evaluates before the old code does, and that moment is the `moduleEval` mark.
import { mark } from '../measure';
import * as vscode from 'vscode';
import { activate as activateOldCode, deactivate as deactivateOldCode, type CoaiExports } from '../../src/extension';
import { adoptRegistrationsFor } from '../../src/shellBridge';
import { recordMarksWith } from '../../src/measureMarks';
import { COMMANDS, MODULE_LEGACY, moduleOf, VIEW_PANEL } from '../ids';
import type { ShellModule } from '../lazy';

type Handler = (...args: unknown[]) => unknown;

// The old code names its own cold-start moments (its activation's phases, the sidebar's render, `ready`, first data); the shell's marks record them.
recordMarksWith(mark);
// Every import above has been evaluated: the time from `moduleEval` to here is the old tree's module-level code (the 4 MB of it).
mark('legacyBody');

// The chat commands belong to the chat module: its own adopter holds them, and the old activation no longer registers them.
const owned = new Set<string>(COMMANDS.filter((id) => moduleOf(id) === MODULE_LEGACY));
const handlers = new Map<string, Handler>();
let viewProvider: vscode.WebviewViewProvider | undefined;

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
    // The `coaiChat` serializer is the chat module's; the old activation registers none.
    serializer: () => false,
  });
}

/** Runs the old `activate()` once and answers what it returned. */
function started(context: vscode.ExtensionContext): CoaiExports {
  if (outcome === undefined) {
    mark('legacyActivateStart');
    adopt();
    try {
      outcome = { started: activateOldCode(context) };
    } catch (failed) {
      outcome = { failed };
    } finally {
      adoptRegistrationsFor(undefined);
      mark('legacyActivateEnd');
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

// The old code has no `vscode://` handler: the link only starts it, which is what `onUri` is for.
export const handleUri: ShellModule['handleUri'] = (context) => {
  started(context);
};

/** What a test host may read of the shell's side of the hand-over; present only in `ExtensionMode.Test`, like the old exports' own test doors. */
export interface ShellTestDoor {
  /** The command ids the old activation handed to the shell, in no particular order. */
  readonly adoptedCommands: () => readonly string[];
  /** True once the old activation gave the shell a view provider. */
  readonly adoptedView: () => boolean;
}

export const start: ShellModule['start'] = (context) => {
  const old = started(context);

  return context.extensionMode === vscode.ExtensionMode.Test
    ? { ...old, shell: { adoptedCommands: () => [...handlers.keys()], adoptedView: () => viewProvider !== undefined } satisfies ShellTestDoor }
    : old;
};

export const deactivate: ShellModule['deactivate'] = () => (outcome !== undefined && 'started' in outcome ? deactivateOldCode() : undefined);
