import * as vscode from 'vscode';
import { EscalationWatcher } from './escalationWatcher';
import { ModelChoice, parseCodexModels } from './models';
import {
  isPanelCommand,
  liveRegions,
  OPEN_BY_DEFAULT,
  panelHtml,
  staticKey,
  VSCODE_COMMAND_FOR,
} from './panelView';
import { parseSession, SessionFile } from './rounds';
import { parseUsage, UsageEntry, Window } from './usage';
import { latestServerVersion } from './installer';
import { serverSettingsJson } from './serverSettingsFile';
import { roleRecordUpdate, SettingMessage, settingsFrom, settingWrite } from './settingsShape';
import { normaliseId, Vendor, VENDOR_PRESETS, vendorsFrom } from './vendors';
import { Platform, vendorInstall, vendorTerminal } from './vendorTerminal';

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
  /** Which window the spending chart shows. A view preference, so it lives here, not in config. */
  private usageWindow: Window = 'week';
  /** The newest published server version, and when GitHub last answered. */
  private latestServer = '';
  private latestCheckedAt = 0;

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
      (m: { type: string; key?: string; value?: unknown; vendor?: string; command?: string; id?: string; open?: boolean; role?: string; round?: number }) => {
        if (m.type === 'section' && m.id !== undefined) {
          this.openSections = m.open === true
            ? [...new Set([...this.openSections, m.id])]
            : this.openSections.filter((s) => s !== m.id);
        } else if (m.type === 'prompt' && m.role !== undefined && m.round !== undefined) {
          void this.choosePrompt(m.role, m.round, String(m.value));
        } else if (m.type === 'setting') {
          void this.write({ key: m.key, value: m.value, vendor: m.vendor, role: m.role });
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
      usage: await this.readUsage(),
      usageWindow: this.usageWindow,
      latestServerVersion: await this.publishedVersion(),
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
   * The CLI a reviewer needs, in a terminal with the command typed and waiting.
   *
   * <p>Typed rather than sent, for the same reason the ▶ button does it: installing something
   * globally is the person's decision, and a command pushed into a shell that has not finished
   * starting is a line of stray text.</p>
   *
   * <p>The shell decides which prerequisite is shown, because that is the only part where
   * PowerShell and bash differ — `npm install -g` is identical in both. A CLI that npm does not
   * publish gets its documentation opened instead of a command that would fail.</p>
   */
  private async installVendorCli(id: string): Promise<void> {
    const vendor = vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors')).find((v) => v.id === id);
    if (vendor === undefined) {
      return;
    }

    // `process.platform` is the extension HOST's platform, which is the one that matters: in a
    // VS Code window connected to WSL it is 'linux', whatever the machine's badge says, and the
    // terminal this opens runs there too.
    const install = vendorInstall(vendor, platform());
    if (install.command.length === 0) {
      const open = 'Open the instructions';
      const choice = await vscode.window.showInformationMessage(install.note, open);
      if (choice === open) {
        await vscode.env.openExternal(vscode.Uri.parse(install.docs));
      }
      return;
    }

    const terminal = vscode.window.createTerminal({ name: `coai · install ${vendor.id}` });
    terminal.show();
    if (install.note.length > 0) {
      void vscode.window.showInformationMessage(install.note);
    }

    // Both lines are typed, newest last, so the prompt holds the install command itself: a machine
    // that already has node needs only that one, and a machine that does not can scroll up one.
    if (install.prerequisite.length > 0) {
      terminal.sendText(`# first time on this machine? ${install.prerequisite}`, false);
      terminal.sendText('', true);
    }

    terminal.sendText(install.command, false);
  }

  /**
   * One role's prompt for one round.
   *
   * <p>Stored as <code>role -&gt; [round1, round2, ...]</code> — the shape the server reads and
   * the shape a person reasons in. Rounds nobody has chosen stay EMPTY rather than being filled
   * with what they resolve to today: both reviewers of this change caught that padding them
   * freezes today’s default into a stored choice the moment anyone touches a later round, so a
   * later change to that default would never reach them again. Both halves read an empty entry
   * as "not chosen".</p>
   */
  private async choosePrompt(role: string, round: number, id: string): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    const settings = settingsFrom((section) => config.get(section));
    const rounds = [...(settings.promptsPerRound[role] ?? [])];
    while (rounds.length < round) {
      rounds.push('');
    }
    rounds[round - 1] = id;
    await config.update(
      'promptsPerRound',
      { ...settings.promptsPerRound, [role]: rounds },
      vscode.ConfigurationTarget.Global,
    );
    await this.render();
  }

  /**
   * One setting, written globally.
   * <p>Global rather than workspace: the vendors you review with and the language you read in are
   * properties of YOU, not of one checkout — and a workspace write would surprise anyone whose
   * `.vscode/settings.json` is in git.</p>
   */
  /**
   * One changed control, written where it is kept.
   *
   * <p>The routing is {@link settingWrite}, decided without `vscode` so it can be tested; this
   * method is the three writes it names. Nothing here may fall through: a role-keyed setting once
   * travelled in the vendor slot, so the provider hunted for a vendor called `Architecture`, wrote
   * the vendor list back unchanged, and `coai.rounds` was never written at all — which read, from
   * the panel, as a number that would not stick.</p>
   */
  private async write(message: SettingMessage): Promise<void> {
    const write = settingWrite(message);
    if (write === undefined) {
      return;
    }

    const config = vscode.workspace.getConfiguration('coai');
    switch (write.kind) {
      case 'vendor': {
        const vendors = vendorsFrom(config.get('vendors')).map((v) =>
          v.id === write.vendor ? { ...v, [write.key]: write.value } : v,
        );
        await config.update('vendors', vendors, vscode.ConfigurationTarget.Global);
        return;
      }
      case 'role': {
        // A record, merged rather than replaced: writing one role's number must not drop the other
        // three, and the stored object is what every other role reads on the next repaint.
        const current = config.get<Record<string, unknown>>(write.key) ?? {};
        await config.update(
          write.key,
          roleRecordUpdate(current, write.role, write.value),
          vscode.ConfigurationTarget.Global,
        );
        return;
      }
      case 'plain':
        await config.update(write.key, write.value, vscode.ConfigurationTarget.Global);
        return;
      default: {
        // Every kind is handled, and the compiler is what says so.
        const unhandled: never = write;
        return unhandled;
      }
    }
  }

  /**
   * One control's click. Unknown names are ignored; KNOWN ones must all be handled, and the
   * compiler is what enforces that — see {@link PANEL_COMMANDS}.
   */
  private async run(command: string | undefined, id: string | undefined): Promise<void> {
    if (!isPanelCommand(command)) {
      return;
    }

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
      case 'checkForUpdate':
        this.latestCheckedAt = 0;
        break;
      case 'usageWindow':
        if (id !== undefined) {
          this.usageWindow = id as Window;
        }
        break;
      case 'customModel':
        if (id !== undefined) {
          await this.customModel(id);
        }
        break;
      case 'installVendorCli':
        if (id !== undefined) {
          await this.installVendorCli(id);
        }
        break;
      case 'installServer':
        // The panel has no business downloading anything itself: the command that does it is
        // registered by the extension, is what the ⋯ menu invokes, and reports its own progress
        // and its own failure. The button's job is only to reach it.
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.installServer);
        break;
      default: {
        // A PanelCommand with no case above lands here and fails to compile. That is the whole
        // guard: the Update button was posting a command nobody handled, and nothing said so.
        const unhandled: never = command;
        void unhandled;
        return;
      }
    }
    await this.render();
  }

  /**
   * "another model…" from a picker: the list is a convenience, never a limit. `__translator__`
   * routes to the translator's model; anything else names a vendor.
   */
  private async customModel(id: string): Promise<void> {
    const model = await vscode.window.showInputBox({
      title: `Model for ${id}`,
      prompt: "The exact model id the CLI should be given. Empty keeps the CLI's default.",
      placeHolder: 'e.g. gemini-2.5-flash, gpt-5.4-mini, haiku',
    });
    if (model === undefined) {
      return; // dismissed — the picker snaps back to the saved value on re-render
    }

    await this.write({ key: 'model', value: model.trim(), vendor: id });
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

  /**
   * The server's append-only spending ledger.
   *
   * <p>Read whole: a year of rounds is a few hundred kilobytes, and streaming it would buy
   * nothing a person could notice while adding a second way for the chart to be wrong.</p>
   */
  /**
   * The newest published server version, asked of GitHub at most every half hour.
   *
   * <p>The panel repaints on every keystroke in a settings field; asking GitHub each time would
   * spend the anonymous rate limit in a minute and then answer nothing at all. "Check again" in
   * the Server section clears the clock for a person who wants an answer now.</p>
   */
  private async publishedVersion(): Promise<string> {
    const halfAnHour = 30 * 60 * 1000;
    if (Date.now() - this.latestCheckedAt < halfAnHour) {
      return this.latestServer;
    }
    this.latestCheckedAt = Date.now();
    this.latestServer = (await latestServerVersion()) ?? '';
    return this.latestServer;
  }

  private async readUsage(): Promise<UsageEntry[]> {
    try {
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(this.dataDir, 'usage.jsonl'));
      return parseUsage(new TextDecoder().decode(bytes));
    } catch {
      return []; // nothing has run yet, which the chart says in words
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

/** The extension host's platform, narrowed to the three the buttons can answer for. */
function platform(): Platform {
  return process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
}
