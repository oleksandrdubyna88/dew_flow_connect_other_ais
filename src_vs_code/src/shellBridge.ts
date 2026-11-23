import * as vscode from 'vscode';

/**
 * How the old activation hands its registrations to the thin shell (v2 epoch 1, E2 S1).
 *
 * <p>The shell (`shell/entry.ts`) registers every command, the sidebar view provider and the chat panel
 * serializer itself, so that activation does nothing else. The old `activate()` still runs, on first use
 * (`shell/modules/legacy.ts`), and VS Code refuses a second registration of the same id. So the old code
 * registers through the three functions below: while an adopter is installed and says it owns the id, the
 * handler is given to the adopter instead of VS Code; with no adopter — the unit tests, and any caller that
 * does not run under the shell — every call is the plain `vscode` one.</p>
 */
export interface Adopter {
  /** True when the shell owns this command id and now holds the handler. */
  readonly command: (id: string, run: (...args: unknown[]) => unknown) => boolean;
  /** True when the shell owns this view type and now holds the provider. */
  readonly viewProvider: (viewType: string, provider: vscode.WebviewViewProvider) => boolean;
  /** True when the shell owns this panel type and now holds the serializer. */
  readonly serializer: (viewType: string, serializer: vscode.WebviewPanelSerializer) => boolean;
}

let adopter: Adopter | undefined;

/** Installs the adopter for the length of the old `activate()` call; `undefined` removes it. */
export function adoptRegistrationsFor(next: Adopter | undefined): void {
  adopter = next;
}

/** A registration the shell holds has nothing to dispose: the shell's own subscription ends it. */
const HELD: vscode.Disposable = { dispose: () => undefined };

export function registerCommand(id: string, run: (...args: unknown[]) => unknown): vscode.Disposable {
  return adopter?.command(id, run) === true ? HELD : vscode.commands.registerCommand(id, run);
}

export function registerWebviewViewProvider(viewType: string, provider: vscode.WebviewViewProvider): vscode.Disposable {
  return adopter?.viewProvider(viewType, provider) === true ? HELD : vscode.window.registerWebviewViewProvider(viewType, provider);
}

export function registerWebviewPanelSerializer(viewType: string, serializer: vscode.WebviewPanelSerializer): vscode.Disposable {
  return adopter?.serializer(viewType, serializer) === true ? HELD : vscode.window.registerWebviewPanelSerializer(viewType, serializer);
}
