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

export const start: InstallModule['start'] = (context) => {
  mark('installStart');

  return workOf(context).start().finally(() => { mark('installEnd'); });
};

export const deactivate: InstallModule['deactivate'] = () => undefined;
