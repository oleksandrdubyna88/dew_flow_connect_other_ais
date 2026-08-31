import * as vscode from 'vscode';
import { EscalationWatcher } from './escalationWatcher';
import { ModelChoice, parseCodexModels } from './models';
import { liveRegions, OPEN_BY_DEFAULT, panelHtml } from './panelView';
import { parseSession, SessionFile } from './rounds';
import { serverSettingsJson } from './serverSettingsFile';
import { settingsFrom } from './settingsShape';
import { normaliseId, Vendor, VENDOR_PRESETS, vendorsFrom } from './vendors';
import { vendorTerminal } from './vendorTerminal';

/**
 * The sidebar panel: reviewers, language, the gate, the limits, and what is waiting on you.
 *
 * <p>The markup is next door in `panelView.ts`, pure and tested. This half is the wiring — reading
 * VS Code configuration, writing it back, and re-rendering when anything changes. The server's own
 * actions (install, copy the config block, copy the snippet) live in the view's ⋯ menu, where
 * VS Code puts commands, rather than as buttons competing with the settings.</p>
 *
 * <p><b>Settings are written to VS Code configuration</b>, not to a file of our own: a person who
 * prefers the Settings UI gets the same values, and this panel is a face on them rather than a
 * second source of truth.</p>
 */
export class PanelProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'coai.panel';

  private view?: vscode.WebviewView;
  private codexModels: ModelChoice[] = [];
  /** What the last repaint was drawn from, so only a real change to the controls repaints. */
  private paintedKey = '';
  /** One nonce per panel instance: the CSP admits our one script, and a repaint reuses it. */
  private readonly nonce = nonce();
  /** Which sections the person has open — kept HERE because the panel repaints on every
      change, and a section that snapped shut mid-edit would be worse than none. */
  private openSections: string[] = [...OPEN_BY_DEFAULT];

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly watcher: EscalationWatcher,
    private readonly dataDir: vscode.Uri,
    private readonly answer: (id: string) => Promise<void>,
  ) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    this.view = view;
    view.webview.options = { enableScripts: true };
    view.webview.onDidReceiveMessage(
      (m: { type: string; key?: string; value?: unknown; vendor?: string; command?: string; id?: string; open?: boolean }) => {
        if (m.type === 'section' && m.id !== undefined) {
          this.openSections = m.open === true
            ? [...new Set([...this.openSections, m.id])]
            : this.openSections.filter((s) => s !== m.id);
        } else if (m.type === 'setting' && m.key !== undefined) {
          void this.write(m.key, m.value, m.vendor);
        } else if (m.type === 'command') {
          void this.run(m.command, m.id);
        }
      },
    );

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
    const settings = settingsFrom((section) => config.get(section));
    const vendors = vendorsFrom(config.get('vendors'));
    this.codexModels = await this.readCodexModels();
    await this.writeServerSettings(settings, vendors);
    const state = {
      settings,
      vendors,
      codexModels: this.codexModels,
      serverInstalled: this.context.globalState.get<string>('coai.installedVersion') !== undefined,
      serverVersion: this.context.globalState.get<string>('coai.installedVersion') ?? '',
      questions: this.watcher.openQuestions,
      openSections: this.openSections,
      sessions: await this.readSessions(),
    };

    // Two update paths, and which one runs is the whole fix for the pickers.
    //
    // Assigning `webview.html` RELOADS the webview, and a reload closes any open dropdown. The
    // escalation watcher ticks every five seconds, and a round in flight rewrites its session
    // file constantly — so the old unconditional assignment shut every `<select>` in this panel
    // two or three seconds after it was opened.
    //
    // So a change to the CONTROLS repaints (rare, and always the person's own doing), while the
    // live regions — the round in flight, a question waiting on an answer — are posted as HTML
    // and patched into place, touching nothing else.
    const key = staticKey(state);
    if (key !== this.paintedKey) {
      this.paintedKey = key;
      this.view.webview.html = panelHtml(state, this.nonce);
      return;
    }

    void this.view.webview.postMessage({ type: 'live', ...liveRegions(state) });
  }

  /**
   * A vendor's own CLI, in a terminal, with its usage command typed and waiting.
   *
   * <p>Typed rather than sent: pressing Enter is the person's decision, and a slash command
   * pushed into a TUI that has not finished starting is a line of stray text. This is also where
   * a CLI gets signed in — the gemini reviewer that failed every round on this machine failed
   * because its CLI had never authenticated headlessly, and the panel offered nowhere to fix
   * that.</p>
   */
  private async runVendor(id: string): Promise<void> {
    const vendor = vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors')).find((v) => v.id === id);
    if (vendor === undefined) {
      return;
    }

    const { command, usageCommand, note } = vendorTerminal(vendor);
    const terminal = vscode.window.createTerminal({ name: `coai · ${vendor.id}` });
    terminal.show();
    if (note.length > 0) {
      void vscode.window.showInformationMessage(note);
    }
    terminal.sendText(command, true);
    if (usageCommand.length > 0) {
      terminal.sendText(usageCommand, false);
    }
  }

  /**
   * One setting, written globally.
   * <p>Global rather than workspace: the vendors you review with and the language you read in are
   * properties of YOU, not of one checkout — and a workspace write would surprise anyone whose
   * `.vscode/settings.json` is in git.</p>
   */
  private async write(key: string, value: unknown, vendorId?: string): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    if (vendorId !== undefined) {
      const vendors = vendorsFrom(config.get('vendors')).map((v) =>
        v.id === vendorId ? { ...v, [key]: value } : v,
      );
      await config.update('vendors', vendors, vscode.ConfigurationTarget.Global);
      return;
    }

    await config.update(key, value, vscode.ConfigurationTarget.Global);
  }

  private async run(command: string | undefined, id: string | undefined): Promise<void> {
    switch (command) {
      case 'answer':
        if (id !== undefined) {
          await this.answer(id);
        }
        break;
      case 'addVendor':
        await this.addVendor();
        break;
      case 'removeVendor':
        if (id !== undefined) {
          await this.removeVendor(id);
        }
        break;
      case 'runVendor':
        if (id !== undefined) {
          await this.runVendor(id);
        }
        break;
      case 'customModel':
        if (id !== undefined) {
          await this.customModel(id);
        }
        break;
      default:
        return;
    }
    await this.render();
  }

  /**
   * "another model…" from a picker: the list is a convenience, never a limit. `__translator__`
   * routes to the translator's model; anything else names a vendor.
   */
  private async customModel(id: string): Promise<void> {
    const model = await vscode.window.showInputBox({
      title: id === '__translator__' ? 'Translator model' : `Model for ${id}`,
      prompt: "The exact model id the CLI should be given. Empty keeps the CLI's default.",
      placeHolder: 'e.g. gemini-2.5-flash, gpt-5.4-mini, haiku',
    });
    if (model === undefined) {
      return; // dismissed — the picker snaps back to the saved value on re-render
    }

    if (id === '__translator__') {
      await vscode.workspace
        .getConfiguration('coai')
        .update('translator.model', model.trim(), vscode.ConfigurationTarget.Global);
      return;
    }
    await this.write('model', model.trim(), id);
  }

  /** A preset, or a name and an endpoint typed in — the list is not meant to stay at two. */
  private async addVendor(): Promise<void> {
    const existing = new Set(vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors')).map((v) => v.id));
    // A preset already in the panel is not offered twice; the blank one (empty id) always is.
    const offered = VENDOR_PRESETS.filter((p) => p.id.length === 0 || !existing.has(p.id));
    const picked = await vscode.window.showQuickPick(
      offered.map((p) => ({ label: p.label, detail: p.hint, preset: p })),
      { title: 'Add a reviewer', placeHolder: 'Which vendor should review as well?' },
    );
    if (picked === undefined) {
      return;
    }

    let vendor: Vendor = { ...picked.preset };
    if (vendor.id.length === 0) {
      const name = await vscode.window.showInputBox({
        title: 'Add a reviewer',
        prompt: 'A short name — it identifies the vendor and names its key in the vault entry',
        placeHolder: 'mistral',
        validateInput: (v) => (normaliseId(v).length === 0 ? 'A name is needed' : undefined),
      });
      if (name === undefined) {
        return;
      }
      const baseUrl = await vscode.window.showInputBox({
        title: `Add ${normaliseId(name)}`,
        prompt: 'Its OpenAI-compatible base URL',
        placeHolder: 'https://api.example.com/v1',
        validateInput: (v) => (v.trim().startsWith('http') ? undefined : 'A base URL is needed'),
      });
      if (baseUrl === undefined) {
        return;
      }
      vendor = { ...vendor, id: normaliseId(name), baseUrl: baseUrl.trim() };
    }

    const config = vscode.workspace.getConfiguration('coai');
    const vendors = vendorsFrom(config.get('vendors'));
    if (vendors.some((v) => v.id === vendor.id)) {
      void vscode.window.showWarningMessage(`${vendor.id} is already a reviewer.`);
      return;
    }
    await config.update('vendors', [...vendors, vendor], vscode.ConfigurationTarget.Global);
  }

  /**
   * Removing the last reviewer would leave a panel with nobody in it, so it is refused — and
   * removing ANY reviewer is confirmed first: the link sits one line above the model picker, it
   * takes that vendor's model and endpoint with it, and there is no undo.
   */
  private async removeVendor(id: string): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    const vendors = vendorsFrom(config.get('vendors'));
    if (vendors.length <= 1) {
      void vscode.window.showWarningMessage('A review panel needs at least one reviewer.');
      return;
    }

    const confirmed = await vscode.window.showWarningMessage(
      `Remove ${id} from the review panel?`,
      {
        modal: true,
        detail: "Its model and endpoint settings go with it. Every vendor can be added back from the presets.",
      },
      'Remove',
    );
    if (confirmed !== 'Remove') {
      return;
    }

    await config.update(
      'vendors',
      vendors.filter((v) => v.id !== id),
      vscode.ConfigurationTarget.Global,
    );
  }

  /**
   * Saves the settings where the server reads them.
   *
   * <p>This is what ended the chore: settings used to reach the server only inside the pasted
   * `mcpServers` env block, so every change to a threshold meant copying the block again and
   * re-pasting it into a client's config. Both halves already share this directory.</p>
   *
   * <p>A failure here is not worth interrupting anyone over — the env block still works, and a
   * settings panel is the wrong place to report a disk problem.</p>
   */
  private async writeServerSettings(
    settings: ReturnType<typeof settingsFrom>,
    vendors: readonly Vendor[],
  ): Promise<void> {
    try {
      await vscode.workspace.fs.createDirectory(this.dataDir);
      await vscode.workspace.fs.writeFile(
        vscode.Uri.joinPath(this.dataDir, 'settings.json'),
        new TextEncoder().encode(serverSettingsJson(settings, vendors)),
      );
    } catch {
      // Not writable; the pasted env block remains the way in.
    }
  }

  /**
   * The Codex CLI's own model cache — what this machine can actually reach today, rather than a
   * list we would have to keep up to date by hand.
   */
  private async readCodexModels(): Promise<ModelChoice[]> {
    const home = process.env['USERPROFILE'] ?? process.env['HOME'];
    const codexHome = process.env['CODEX_HOME'] ?? (home === undefined ? undefined : `${home}/.codex`);
    if (codexHome === undefined) {
      return [];
    }
    try {
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.file(`${codexHome}/models_cache.json`));
      return parseCodexModels(new TextDecoder().decode(bytes));
    } catch {
      return []; // codex has never run here, or keeps its cache elsewhere
    }
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

/** A nonce per panel instance: the content security policy admits exactly our one script. */
function nonce(): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  return Array.from({ length: 32 }, () => alphabet[Math.floor(Math.random() * alphabet.length)]).join('');
}

/**
 * Everything a repaint is needed for - the CONTROLS. Deliberately excludes the live regions
 * (rounds, questions), which are patched in place instead so an open dropdown survives.
 */
function staticKey(state: Parameters<typeof panelHtml>[0]): string {
  return JSON.stringify([
    state.settings,
    state.vendors,
    state.codexModels,
    state.serverInstalled,
    state.serverVersion,
    state.openSections,
  ]);
}
