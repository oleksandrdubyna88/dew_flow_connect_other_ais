import * as vscode from 'vscode';
import { EscalationWatcher } from './escalationWatcher';
import { panelHtml } from './panelView';
import { parseSession, SessionFile } from './rounds';
import { settingsFrom } from './settingsShape';

/**
 * The sidebar panel: settings, the server, open questions and recent rounds in one place.
 *
 * <p>The markup is next door in `panelView.ts`, pure and tested. This half is the wiring — reading
 * VS Code configuration, writing it back, and re-rendering when anything changes.</p>
 *
 * <p><b>Settings are written to VS Code configuration</b>, not to a file of our own. A person who
 * prefers the Settings UI, or a repository that commits `.vscode/settings.json`, gets the same
 * values; this panel is a convenient face on them, never a second source of truth.</p>
 */
export class PanelProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'coai.panel';

  private view?: vscode.WebviewView;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly watcher: EscalationWatcher,
    private readonly dataDir: vscode.Uri,
    private readonly commands: {
      install: () => Promise<void>;
      copyConfig: () => Promise<void>;
      copySnippet: () => Promise<void>;
      answer: (id: string) => Promise<void>;
    },
  ) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    this.view = view;
    view.webview.options = { enableScripts: true };
    view.webview.onDidReceiveMessage((message: { type: string; key?: string; value?: unknown; command?: string; id?: string }) => {
      if (message.type === 'setting' && message.key !== undefined) {
        void this.write(message.key, message.value);
      } else if (message.type === 'command') {
        void this.run(message.command, message.id);
      }
    });

    // The panel is a view of state that four other things change: settings, the server's
    // escalations, its sessions, and the installer. Re-render on each rather than on a timer.
    this.context.subscriptions.push(
      vscode.workspace.onDidChangeConfiguration((e) => {
        if (e.affectsConfiguration('coai')) {
          void this.render();
        }
      }),
    );
    void this.render();
  }

  /** Re-read everything and repaint. Cheap: a few small files and one configuration read. */
  async render(): Promise<void> {
    if (this.view === undefined) {
      return;
    }
    const config = vscode.workspace.getConfiguration('coai');
    this.view.webview.html = panelHtml(
      {
        settings: settingsFrom((section) => config.get(section)),
        serverInstalled: this.context.globalState.get<string>('coai.installedVersion') !== undefined,
        serverVersion: this.context.globalState.get<string>('coai.installedVersion') ?? '',
        questions: this.watcher.openQuestions,
        sessions: await this.readSessions(),
      },
      nonce(),
    );
  }

  /**
   * One setting, written where the person is most likely to want it.
   * <p>Global, not workspace: the vendors you review with and the language you read in are
   * properties of YOU, not of one checkout — and a workspace write would surprise anyone whose
   * `.vscode/settings.json` is in git.</p>
   */
  private async write(key: string, value: unknown): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    if (key.startsWith('provider.')) {
      const provider = key.slice('provider.'.length);
      const current = new Set(config.get<string[]>('providers') ?? []);
      if (value === true) {
        current.add(provider);
      } else {
        current.delete(provider);
      }
      // Keep the panel's own order, so the list never shuffles under the person.
      const ordered = ['codex', 'gemini', 'deepseek'].filter((p) => current.has(p));
      await config.update('providers', ordered, vscode.ConfigurationTarget.Global);
      return;
    }

    await config.update(key, value, vscode.ConfigurationTarget.Global);
  }

  private async run(command: string | undefined, id: string | undefined): Promise<void> {
    switch (command) {
      case 'install':
        await this.commands.install();
        break;
      case 'copyConfig':
        await this.commands.copyConfig();
        break;
      case 'copySnippet':
        await this.commands.copySnippet();
        break;
      case 'answer':
        if (id !== undefined) {
          await this.commands.answer(id);
        }
        break;
      default:
        return;
    }
    await this.render();
  }

  private async readSessions(): Promise<SessionFile[]> {
    const dir = vscode.Uri.joinPath(this.dataDir, 'sessions');
    const sessions: SessionFile[] = [];
    try {
      for (const [name, kind] of await vscode.workspace.fs.readDirectory(dir)) {
        if (kind !== vscode.FileType.File || !name.endsWith('.json')) {
          continue;
        }
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(dir, name));
        const session = parseSession(new TextDecoder().decode(bytes));
        if (session !== undefined) {
          sessions.push(session);
        }
      }
    } catch {
      // No data dir yet — the panel says "no rounds yet", which is true.
    }
    return sessions;
  }
}

/** A fresh nonce per render: the content security policy admits exactly our one script. */
function nonce(): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  return Array.from({ length: 32 }, () => alphabet[Math.floor(Math.random() * alphabet.length)]).join('');
}
