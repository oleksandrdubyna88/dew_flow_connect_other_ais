import { spawn } from 'node:child_process';
import * as vscode from 'vscode';
import { snippetStatus, SnippetStatus } from './claudeSnippet';
import { discoverEngine, LocalEngine, openAiBaseOf, probeEngine } from './localEngines';
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
import {
  CliStatus,
  latestCliVersion,
  parseCliVersion,
  versionProbeCandidates,
  versionSourceFor,
} from './cliVersions';
import { latestServerVersion } from './installer';
import {
  fetchTable,
  LITELLM_PRICES,
  liteLlmTable,
  ModelPrice,
  OPENROUTER_MODELS,
  openRouterTable,
  PriceTable,
  priceFor,
} from './modelPrices';
import { roleRecordUpdate, SettingMessage, settingsFrom, settingWrite } from './settingsShape';
import { normaliseId, Vendor, VENDOR_PRESETS, vendorsFrom } from './vendors';
import {
  executableFor,
  Platform,
  VendorInstall,
  vendorInstall,
  vendorTerminal,
  vendorUpdate,
} from './vendorTerminal';

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
  /** Each vendor's installed and published CLI version, and when they were last read. */
  private cliStatus: Record<string, CliStatus> = {};
  private cliCheckedAt = 0;
  /**
   * The local engine as last probed, for WHICH endpoint, and when.
   *
   * <p>The endpoint is part of the key, not a detail. Without it a probe still in flight for
   * endpoint A can land after the person has typed endpoint B and populate B's row with A's models
   * — raised by this product's own gate on the plan for this feature.</p>
   *
   * <p><b>And so is the vendor id.</b> These were three scalars, one engine for the whole panel,
   * looked up with `vendors.find(v => v.runtime === 'local')` — so a second local reviewer showed
   * the first one's models. Found by Claude Sonnet 5, 2026-09-02.</p>
   */
  private localEngines: Record<string, LocalEngine> = {};
  private localProbedEndpoints: Record<string, string> = {};
  private localCheckedAt: Record<string, number> = {};
  /** The two public price lists, and when they were last fetched. */
  private openRouterPrices: PriceTable = {};
  private liteLlmPrices: PriceTable = {};
  private pricesCheckedAt = 0;

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

    // The configuration listener used to be registered HERE, and that was the defect: VS Code
     // resolves a webview view lazily, so in a window where nobody opened this panel there was
     // nothing watching the settings and nothing mirroring them to the server. It now lives in
     // `activate`, which runs whether or not anyone looks at a webview. Registering it here also
     // added a second listener every time the view was disposed and resolved again.
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
    const state = {
      settings,
      vendors,
      codexModels: this.codexModels,
      serverInstalled: this.context.globalState.get<string>('coai.installedVersion') !== undefined,
      serverVersion: this.context.globalState.get<string>('coai.installedVersion') ?? '',
      questions: this.watcher.openQuestions,
      openSections: this.openSections,
      sessions: await this.readSessions(),
      usage: this.remembered(await this.readUsage()),
      usageWindow: this.usageWindow,
      latestServerVersion: await this.publishedVersion(),
      cliStatus: await this.vendorCliStatus(vendors),
      modelPrices: await this.modelPrices(vendors),
      snippetStatus: await this.pastedSnippet(),
      localEngines: await this.probeLocalEngines(vendors),
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
   * The local model engine, probed only when a local reviewer is actually configured.
   *
   * <p>Only then, and that is deliberate: probing two ports on every repaint of every panel would
   * be this extension knocking on a developer's own machine for a feature they are not using.</p>
   *
   * <p>Cached for a minute rather than half an hour, unlike the CLI versions: somebody who has just
   * started Ollama, or just pulled a model, expects the list to notice. A minute is short enough to
   * feel live and long enough that a repaint storm costs one probe.</p>
   *
   * <p>An endpoint somebody TYPED is asked directly and nothing else is probed — they have said
   * where it is, and looking elsewhere would be second-guessing them.</p>
   */
  private async probeLocalEngines(vendors: readonly Vendor[]): Promise<Record<string, LocalEngine>> {
    const probed: Record<string, LocalEngine> = {};
    for (const vendor of vendors.filter((v) => v.runtime === 'local')) {
      const engine = await this.probeLocalEngine(vendor);
      if (engine !== undefined) {
        probed[vendor.id] = engine;
      }
    }

    return probed;
  }

  private async probeLocalEngine(vendor: Vendor): Promise<LocalEngine | undefined> {
    const A_MINUTE = 60 * 1000;
    const wanted = vendor.baseUrl.length > 0 ? openAiBaseOf(vendor.baseUrl) : '';
    const cached = this.localEngines[vendor.id];
    // A probe that found NOTHING is not cached, and that is the fix for the other half of the
    // finding: somebody opens the panel, sees "no local engine answered", starts Ollama, and would
    // otherwise wait out the TTL staring at a stale sentence. Two connection refusals cost
    // nothing, so the empty answer is simply re-asked. A successful probe is cached, because
    // listing models on every repaint is what the cache is for.
    const fresh = Date.now() - (this.localCheckedAt[vendor.id] ?? 0) < A_MINUTE && (cached?.reachable ?? false);
    if (cached !== undefined && fresh && this.localProbedEndpoints[vendor.id] === wanted) {
      return cached;
    }

    this.localCheckedAt[vendor.id] = Date.now();
    this.localProbedEndpoints[vendor.id] = wanted;
    const engine = wanted.length > 0 ? await probeEngine(wanted) : await discoverEngine();
    // The endpoint may have changed WHILE this probe was in flight; the answer then belongs to a
    // configuration nobody is looking at any more, and showing it would be worse than showing
    // nothing. The next repaint probes the current one.
    const current = vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors'))
      .find((v) => v.id === vendor.id);
    const currentWanted = current === undefined ? ''
      : current.baseUrl.length > 0 ? openAiBaseOf(current.baseUrl) : '';
    if (currentWanted !== wanted) {
      this.localCheckedAt[vendor.id] = 0;

      return cached;
    }
    this.localEngines[vendor.id] = engine;

    return engine;
  }

  /**
   * How old the snippet pasted into this workspace is.
   *
   * <p>The same four instruction files the SERVER reads for its conventions pass — a person pastes
   * into whichever one their AI reads, and there is no reason for the two halves of this product to
   * disagree about which those are.</p>
   *
   * <p>Only the workspace ROOT, and only files that exist. Walking a repository for a pasted block
   * would be a filesystem crawl on every repaint to answer a question about one paragraph.</p>
   */
  private async pastedSnippet(): Promise<SnippetStatus> {
    const root = vscode.workspace.workspaceFolders?.[0]?.uri;
    if (root === undefined) {
      return snippetStatus(undefined);
    }

    for (const name of ['CLAUDE.md', 'AGENTS.md', 'GEMINI.md', '.github/copilot-instructions.md']) {
      const text = await this.readIfPresent(vscode.Uri.joinPath(root, name));
      // The FIRST file that carries it wins. A repository with the block in two files has a
      // problem this panel cannot fix, and reporting the older of the two would be arbitrary.
      if (text.includes('Multi-model review gate (ConnectOtherAIs)')) {
        return snippetStatus(text);
      }
    }

    return snippetStatus(undefined);
  }

  private async readIfPresent(uri: vscode.Uri): Promise<string> {
    try {
      return new TextDecoder().decode(await vscode.workspace.fs.readFile(uri));
    } catch {
      return ''; // an absent instruction file is the normal case, not a failure
    }
  }

  /**
   * The published list price of every model the vendors are currently set to.
   *
   * <p>Once a day: a price list does not move faster than that, and the two files are large —
   * OpenRouter answered with 419 models and LiteLLM with 3408 entries when this was written. Only
   * the models actually in use are kept, so the panel carries a handful of numbers rather than a
   * catalogue.</p>
   *
   * <p>Both fail silently to an empty table. A machine with no network shows the same panel it
   * always showed, with dashes where the prices would be — which is exactly what it showed before
   * this existed.</p>
   */
  private async modelPrices(vendors: readonly Vendor[]): Promise<Record<string, ModelPrice>> {
    const A_DAY = 24 * 60 * 60 * 1000;
    if (Date.now() - this.pricesCheckedAt > A_DAY) {
      this.pricesCheckedAt = Date.now();
      [this.openRouterPrices, this.liteLlmPrices] = await Promise.all([
        fetchTable(OPENROUTER_MODELS, openRouterTable),
        fetchTable(LITELLM_PRICES, liteLlmTable),
      ]);
    }

    const prices: Record<string, ModelPrice> = {};
    for (const vendor of vendors) {
      if (vendor.model.length === 0) {
        continue; // "the CLI's default" — we do not know which model that is, so we do not guess
      }
      const price = priceFor(vendor.model, this.openRouterPrices, this.liteLlmPrices);
      if (price !== undefined) {
        prices[vendor.model] = price;
      }
    }

    return prices;
  }

  /**
   * Clear one vendor's recorded runs from the spending chart, after asking.
   *
   * <p><b>A watermark, never a rewrite of the ledger.</b> The server appends to `usage.jsonl` while
   * this panel is open, so filtering that file and writing it back would race a round finishing
   * mid-write — and a spending record is exactly the kind of file that must not lose rows to a UI
   * action. What is stored here is "ignore anything this vendor recorded at or before this instant".
   * Nothing is destroyed, the ledger stays the server's, and the row returns the next time the
   * vendor runs because that entry's timestamp is later.</p>
   *
   * <p>Modal, because it is not reversible from the panel and the number it clears is the only
   * record of what a month cost.</p>
   */
  private async forgetUsage(provider: string): Promise<void> {
    const forget = 'Forget';
    const answer = await vscode.window.showWarningMessage(
      `Clear ${provider}'s recorded runs from the spending chart?`,
      {
        modal: true,
        detail:
          'The chart stops counting what this vendor has recorded so far. Nothing is deleted from '
          + 'the ledger on disk, and the row comes back the next time this vendor runs.',
      },
      forget,
    );
    if (answer !== forget) {
      return;
    }

    const marks = { ...this.forgottenBefore(), [provider]: new Date().toISOString() };
    await this.context.globalState.update('coai.usageForgottenBefore', marks);
    await this.render();
  }

  /**
   * The ledger minus what has been forgotten.
   *
   * <p>Applied on READ so the file is never touched: an entry recorded at or before a vendor's
   * watermark is not counted, and everything after it is. That is what makes the row come back on
   * its own — there is no state to reset, only a timestamp the next run is later than.</p>
   */
  private remembered(entries: readonly UsageEntry[]): UsageEntry[] {
    const marks = this.forgottenBefore();

    return entries.filter((e) => {
      const mark = marks[e.provider];

      return mark === undefined || e.utc > mark;
    });
  }

  /** Per vendor, the instant before which its recorded runs are not counted. */
  private forgottenBefore(): Record<string, string> {
    return this.context.globalState.get<Record<string, string>>('coai.usageForgottenBefore') ?? {};
  }

  /**
   * What each reviewer's CLI is, and what its vendor publishes.
   *
   * <p>Cached for half an hour, like the server's own update check and for the same reason: the
   * panel repaints whenever anything changes, and an uncached read would spawn one process and open
   * one connection PER VENDOR every time. Pressing the button clears the cache, so "I just
   * updated it" is answered immediately rather than in twenty minutes.</p>
   *
   * <p>Nothing here can fail loudly. A CLI that is not installed, a machine with no network, a
   * vendor with no official version source — each leaves its entry empty, and an empty entry is a
   * grey button. Guessing would be worse: a button that lights up because a fetch failed is a
   * button that lies.</p>
   */
  private async vendorCliStatus(vendors: readonly Vendor[]): Promise<Record<string, CliStatus>> {
    const HALF_AN_HOUR = 30 * 60 * 1000;
    if (Date.now() - this.cliCheckedAt < HALF_AN_HOUR) {
      return this.cliStatus;
    }
    this.cliCheckedAt = Date.now();

    const entries = await Promise.all(vendors.map(async (vendor) => [vendor.id, await this.oneCliStatus(vendor)] as const));
    this.cliStatus = Object.fromEntries(entries);

    return this.cliStatus;
  }

  private async oneCliStatus(vendor: Vendor): Promise<CliStatus> {
    const source = versionSourceFor(vendor.runtime, platform(), process.arch === 'arm64' ? 'arm64' : 'x64');

    const [installed, latest] = await Promise.all([
      this.installedCliVersion(vendor),
      source === undefined ? Promise.resolve('') : latestCliVersion(source),
    ]);

    return { installed, latest };
  }

  /**
   * What the binary on this machine says when asked.
   *
   * <p>The vendor's CLI path wins over the bare name, exactly as the ▶ and ⤓ buttons do: the whole
   * point of that field is that PATH could not answer, and asking the wrong binary its version
   * would report a number for software the reviews do not run.</p>
   */
  private async installedCliVersion(vendor: Vendor): Promise<string> {
    const executable = executableFor(vendor);
    if (executable.length === 0) {
      return '';
    }

    // On Windows the answer is usually `codex.cmd`, so the candidates are tried in order and the
    // first that ANSWERS wins. A name that does not exist fails immediately with ENOENT, so this
    // costs nothing when the first one is right.
    for (const candidate of versionProbeCandidates(executable, platform())) {
      const version = await askVersion(candidate);
      if (version.length > 0) {
        return version;
      }
    }

    return '';
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
    await this.openCliTerminal(id, 'install', vendorInstall);
  }

  /**
   * The vendor's own update command, in a terminal, typed and waiting.
   *
   * <p>Not always the install command: `claude update` and `agy update` update themselves, while
   * codex and gemini are updated by installing again. Which is which is in {@link vendorUpdate},
   * verified per vendor — `agy update` was written down as not existing because `agy --help` does
   * not list it, and it exists.</p>
   */
  private async updateVendorCli(id: string): Promise<void> {
    await this.openCliTerminal(id, 'update', vendorUpdate);
  }

  private async openCliTerminal(
    id: string,
    verb: 'install' | 'update',
    commandFor: (vendor: Vendor, platform: Platform) => VendorInstall,
  ): Promise<void> {
    const vendor = vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors')).find((v) => v.id === id);
    if (vendor === undefined) {
      return;
    }

    // `process.platform` is the extension HOST's platform, which is the one that matters: in a
    // VS Code window connected to WSL it is 'linux', whatever the machine's badge says, and the
    // terminal this opens runs there too.
    const install = commandFor(vendor, platform());
    if (install.command.length === 0) {
      const open = 'Open the instructions';
      const choice = await vscode.window.showInformationMessage(install.note, open);
      if (choice === open) {
        await vscode.env.openExternal(vscode.Uri.parse(install.docs));
      }
      return;
    }

    const terminal = vscode.window.createTerminal({ name: `coai · ${verb} ${vendor.id}` });
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
      case 'updateVendorCli':
        if (id !== undefined) {
          this.cliCheckedAt = 0; // the button stops being green as soon as the version moves
          await this.updateVendorCli(id);
        }
        break;
      case 'reprobeLocal':
        // Clearing the cache is the whole action: the next render probes, because a probe that is
        // not fresh is not reused. Nothing else to do and nothing to await beyond the repaint.
        this.localCheckedAt = {};
        await this.render();
        break;
      case 'forgetUsage':
        if (id !== undefined) {
          await this.forgetUsage(id);
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

/**
 * One `--version` call, answered with the version or with nothing.
 *
 * <p>A CLI that hangs must not hold up a repaint, so the wait is short and a timeout produces the
 * same "could not tell" every other failure here does. Nothing throws: an absent binary is an
 * ordinary state of a machine, not an error to show somebody.</p>
 */
function askVersion(executable: string): Promise<string> {
  return new Promise((resolve) => {
    const child = spawn(executable, ['--version'], { shell: false });
    const timer = setTimeout(() => {
      child.kill();
      resolve('');
    }, 8000);
    let output = '';
    child.stdout?.on('data', (chunk: Buffer) => {
      output += chunk.toString();
    });
    child.on('error', () => {
      clearTimeout(timer);
      resolve('');
    });
    child.on('close', () => {
      clearTimeout(timer);
      resolve(parseCliVersion(output));
    });
  });
}

/** The extension host's platform, narrowed to the three the buttons can answer for. */
function platform(): Platform {
  return process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
}
