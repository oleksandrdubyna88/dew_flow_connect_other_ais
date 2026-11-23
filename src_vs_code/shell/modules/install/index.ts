// The `install` module (v2 E4 story s5-install-module): the binary install and update, and `coai-mcp --setup-clients`.
// `../../measure` FIRST: this bundle's own copy of the recorder evaluates before anything else here, and that moment is `moduleEval:install`.
//
// The shell never loads this module while the sidebar draws its menu: the sidebar module asks for `start` after its first `menuReady` (or its fallback), and the
// entry asks after the first command has answered — so the install work is outside the cold start's M2 window by construction. `start` is once per activation
// (`src/installWork.ts`): the binary check, then `--setup-clients` with this extension's own binary named, then the offer of a newer server.
// `coai.installServer` is this module's one command; after it a (re)installed server is announced to the legacy panel, which repaints, and the mode runs again.
import { mark } from '../../measure';
import * as vscode from 'vscode';
import { InstallWork, type InstallPorts } from '../../../src/installWork';
import { serverExists, serverPath } from '../../../src/installer';
import { announceServerInstalled } from '../../../src/serverInstalled';
import { notify } from '../../../src/notify';
import { spawnServer } from '../../../src/setupClients';
import { INSTALL_COMMANDS } from '../../ids';
import type { InstallModule } from '../../lazy';
import { offerUpdate, installServer } from './flow';

mark('installBody');

/** The seams of one window: the install code of the old activation, the server's own spawn, the notification funnel and the console. */
function realPorts(context: vscode.ExtensionContext): InstallPorts {
  return {
    binary: () => serverPath(context.globalStorageUri)?.fsPath,
    exists: () => serverExists(context.globalStorageUri),
    offerUpdate: (installed) => offerUpdate(context, installed),
    spawn: spawnServer,
    say: (notice) => notify(notice),
    log: (message, error) => { console.error(`ConnectOtherAIs: ${message}`, error ?? ''); },
  };
}

let work: InstallWork | undefined;

/** The window's one install work, made on first use (the context is the host's, one per activation). */
const workOf = (context: vscode.ExtensionContext): InstallWork => {
  work ??= new InstallWork(realPorts(context));

  return work;
};

export const command: InstallModule['command'] = async (context, id) => {
  if (id !== 'coai.installServer') {
    throw new Error(`ConnectOtherAIs: the command ${id} is registered by the shell for install, but the install module does not run it`);
  }
  const installed = await installServer(context);
  // A (re)installed server reads the roots afresh on its start; the panel asks the disk afresh too — whether or not the install worked, as before.
  announceServerInstalled();
  if (installed) {
    await workOf(context).serverInstalled();
  }
};

/** What a test host may read of the install module; present only in `ExtensionMode.Test`, like the other modules' doors. */
export interface InstallTestDoor {
  /** The commands the shell routes to this module. */
  readonly adoptedCommands: () => readonly string[];
  /** Settles when the one run of the install work has finished — a host awaits it where a person never would. */
  readonly finished: Promise<void>;
}

export const start: InstallModule['start'] = (context) => {
  mark('installStart');
  const finished = workOf(context).start().finally(() => { mark('installEnd'); });

  // In a test host the door is handed back at once, NOT the work: the host must not wait for a version check against the network, as no caller of this module does.
  return context.extensionMode === vscode.ExtensionMode.Test
    ? { install: { adoptedCommands: () => INSTALL_COMMANDS, finished } satisfies InstallTestDoor }
    : finished;
};

export const deactivate: InstallModule['deactivate'] = () => undefined;
