import { readFile } from 'node:fs/promises';
import * as vscode from 'vscode';
import { pastedSnippetStatus } from './snippetInWorkspace';
import { discoverEngine, LocalEngine, openAiBaseOf, probeEngine } from './localEngines';
import { EscalationWatcher } from './escalationWatcher';
import { ModelChoice, parseAgyModels, parseCodexModels } from './models';
import {
  isPanelCommand,
  liveRegions,
  OPEN_BY_DEFAULT,
  panelHtml,
  staticKey,
  VSCODE_COMMAND_FOR,
} from './panelView';
import { parseSession, SessionFile } from './rounds';
import { PriceOfModel, usageTabHtml } from './roundsLog';
import { parseUsage, priceOf, UsageEntry, Window } from './usage';
import {
  CliStatus,
  latestCliVersion,
  unquoted,
  versionProbeCandidates,
  versionSourceFor,
} from './cliVersions';
import { askVersion, capture } from './versionProbe';
import { readOverlay, seedIfEmpty, writeOverlay } from './sideSettings';
import { thisSide } from './installer';
import { latestServerVersion, latestTeamServerVersion, serverOnThisSide, serverPath } from './installer';
import { DbLog, EMPTY_LOG } from './roundsDb';
import { ProvidersAnswer } from './providers';
import { readProviders } from './providersProbe';
import { readLog } from './roundsDbRead';
import { sideKey, sideLabel } from './coaiInstall';
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
import {
  ConfigReader,
  OVERLAID_SETTINGS,
  overlaidReader,
  roleRecordUpdate,
  SettingMessage,
  settingsFrom,
  settingWrite,
} from './settingsShape';
import {
  mirroredLines,
  NetworkingMode,
  networkingModeOf,
  previewOf,
  windowsSideEngine,
  windowsWslconfigPath,
  writeWslconfig,
  wslconfigWith,
} from './wslNetwork';
import { normaliseId, Vendor, VENDOR_PRESETS, vendorsFrom } from './vendors';
import { Catalog, Usage, fetchClientConfig, fetchUsage } from './teamServerApi';

/**
 * The panel's window names, as the server's `/api/usage` spells them.
 *
 * <p>The server's windows are trailing and half-open rather than calendar ones, so `week` is the
 * last seven days. The names differ because the panel's own `day` predates the server entirely.</p>
 */
const WINDOW_ON_THE_WIRE: Readonly<Record<Window, string>> = {
  day: 'today',
  week: 'week',
  month: 'month',
  year: 'year',
};
import {
  AuthHost,
  SignedIn,
  TokenFact,
  catalogOf,
  intentScopeOf,
  plannedAction,
  readToken,
  reconcile,
  revokedKey,
  signIn,
  signOut,
  signedInKey,
  tokenFactKey,
} from './teamServerAuth';
import {
  TeamServer,
  canonicalTeamServerUrl,
  isUsableVendorId,
  newTeamServerId,
  remoteVendorRowId,
  teamServersFrom,
} from './teamServers';
import { TeamServerState, slotSentence } from './teamServerView';
import { coaiDataDir } from './dataDir';
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
/** How long a Team server's catalog stands before it is asked again. */
const TEAM_SERVER_FRESH_MS = 60 * 1000;

/**
 * How long a FAILED silent sign-in is left alone before it is tried again.
 *
 * <p>Ten minutes against the sixty seconds of the refresh above, because the two are answering
 * different questions. An identity provider that will not answer here — no cached Microsoft session
 * in this WSL distro, a network that is down — would otherwise be asked once a minute for as long as
 * the window is open. Pressing <i>Sign in</i> is always immediate; this only paces the attempts
 * nobody asked for. Raised on the plan round.</p>
 */
const MINT_BACKOFF_MS = 10 * 60 * 1000;

export class PanelProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'coai.panel';

  private view?: vscode.WebviewView;
  private codexModels: ModelChoice[] = [];
  /**
   * What `agy models` last listed, and when it was asked.
   *
   * <p>Asked rather than remembered, because the remembered list went a model generation stale in
   * silence — the operator found Gemini 3.8 Flash missing from the dropdown while the CLI had been
   * listing it. Asked at most once an hour, because it is a process and a network call, and a
   * subscription does not gain models between two repaints.</p>
   */
  private agyModels: ModelChoice[] = [];
  private agyCheckedAt = 0;
  /** What the last repaint was drawn from, so only a real change to the controls repaints. */
  private paintedKey = '';
  /** One nonce per panel instance: the CSP admits our one script, and a repaint reuses it. */
  private readonly nonce = nonce();
  /** Which sections the person has open — kept HERE because the panel repaints on every
      change, and a section that snapped shut mid-edit would be worse than none. */
  private openSections: string[] = [...OPEN_BY_DEFAULT];
  /** Which window the spending chart shows. A view preference, so it lives here, not in config. */
  private usageWindow: Window = 'day';

  private usageScope: 'me' | 'company' = 'me';

  /** The last catalog each server managed to answer with, and what has gone wrong since. */
  /** When each server was last asked what it offers. */
  private teamCheckedAt = 0;

  /** One refresh at a time: a second would race the first for the same `catalogs` entries. */
  private refreshing = false;

  /** What each server is in the middle of, so the row can say so and its buttons can stop. */
  private busy: Readonly<Record<string, string>> = {};

  /**
   * The last silent sign-in that failed, per server: when, and what it said.
   *
   * <p>In memory rather than in `globalState`, deliberately — reloading the window is a person
   * saying "try again", and a backoff that survived that would be a backoff they cannot clear.</p>
   */
  private mintFailure: Readonly<Record<string, { readonly at: number; readonly message: string }>> = {};

  private catalogs: Record<string, {
    catalog?: Catalog | undefined;
    usage?: Usage | undefined;
    problem: string;
    stale: boolean;
  }> = {};
  /** The newest published server version, and when GitHub last answered. */
  private latestServer = '';
  /** The newest published Team-server version, shown to admins only. Read-only: it is deployed. */
  private latestTeamServer = '';
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
  /** The rounds database as last read, and when — a read is a process spawn. */
  private roundsLogCache: DbLog = EMPTY_LOG;
  private roundsLogAt = 0;

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




  /**
   * What a MODEL costs per million tokens, for the log page's Cost column.
   *
   * <p>By model id rather than by vendor: a round is priced from the usage ledger, and every line
   * there names the model that actually answered — so a finished round keeps the cost it had when
   * it ran, however the vendor is configured today. The same two public price lists the spending
   * section reads; nothing new is fetched.</p>
   */
  async modelPrice(): Promise<PriceOfModel> {
    await this.refreshPriceTables();
    const [open, lite] = [this.openRouterPrices, this.liteLlmPrices];
    // What the OPERATOR typed, per vendor, wins over any list — through the same priceOf the
    // spending tab uses rather than a second copy of the rule. It is the only thing that can price
    // a local engine at all: no public list has ever heard of one, so its rounds read as a floor
    // until somebody says what it costs them.
    const vendors = this.vendorsHere();
    // Looked up once per DISTINCT model and remembered, rather than re-derived inside the loop that
    // prices a hundred rounds — the gate's performance finding, and it costs nothing to honour.
    // The whole price list is searched, not only the models the vendors are set to now, because a
    // finished round is priced by the model that answered it.
    const seen = new Map<string, { inPerMillion: number; outPerMillion: number } | undefined>();

    return (model, provider) => {
      const key = provider + '|' + model;
      if (!seen.has(key)) {
        const typed = priceOf(provider, vendors, (id) => published(id, open, lite));
        seen.set(key, typed === undefined ? undefined : { inPerMillion: typed.in, outPerMillion: typed.out });
      }

      return seen.get(key);
    };

    function published(id: string, openRouter: PriceTable, liteLlm: PriceTable) {
      const price = priceFor(id, openRouter, liteLlm);

      return price === undefined ? undefined : { inPerMillion: price.inPerMillion, outPerMillion: price.outPerMillion };
    }
  }

  /**
   * The rounds database, as the installed server reads it.
   *
   * <p>Through the server rather than through SQLite of our own: it owns the schema, it already has
   * the file, and the alternative was a WebAssembly build in the VSIX. A server too old for the flag
   * answers nothing, which is the same to the page as a machine that has run no rounds — it goes on
   * showing everything it builds from the session files.</p>
   */
  async roundsLog(): Promise<DbLog> {
    // Cached for a few seconds, because the log page refreshes every tick while a round runs and
    // each read is a process spawn plus a walk of the whole findings table. The gate called that
    // out twice: a hot path is not where a child process belongs. A few seconds is shorter than any
    // round and longer than any burst of ticks.
    const AGE_MS = 10_000;
    if (Date.now() - this.roundsLogAt < AGE_MS) {
      return this.roundsLogCache;
    }
    const server = serverPath(this.context.globalStorageUri);
    this.roundsLogAt = Date.now();
    this.roundsLogCache = server === undefined ? EMPTY_LOG : await readLog(server.fsPath);

    return this.roundsLogCache;
  }

  /**
   * The ledger, whole, for pricing rounds.
   *
   * <p>Not the `remembered` subset the spending tab shows: forgetting a vendor's spending is a
   * decision about the SPENDING VIEW, and it must not silently empty the Cost column of rounds that
   * really did cost money.</p>
   */
  async usageLines(): Promise<readonly UsageEntry[]> {
    return this.readUsage();
  }

  /** The spending window the page shows. Today by default — since midnight, by the operator's ruling. */
  setUsageWindow(window: string): void {
    if ((['day', 'week', 'month', 'year'] as readonly string[]).includes(window)) {
      this.usageWindow = window as Window;
    }
  }

  /**
   * The spending tab of the rounds log page: the ledger, the forget marks, the prices and the window
   * — everything the sidebar section used to show, rendered by the same function, for the page.
   */
  async usageTab(): Promise<string> {
    const vendors = this.vendorsHere();

    return usageTabHtml(
      this.remembered(await this.readUsage()),
      this.usageWindow,
      vendors,
      await this.modelPrices(vendors),
      // What each Team server says was spent ON IT. This machine's ledger has no line for a
      // review that ran there, so the two are shown beside each other rather than summed into
      // a number neither of them holds.
      this.teamServerStates(vscode.workspace.getConfiguration('coai')),
      this.usageScope,
    );
  }

  /** The last answer from `--providers`, when it was taken, and which binary gave it. */
  private providersCache: ProvidersAnswer = { reported: {}, asked: false, answered: false };

  private providersAt = 0;

  /** Keyed on the executable too: a reinstall inside the window must not serve the old one's answer. */
  private providersFrom = '';

  private providersInFlight = false;

  /**
   * What the SERVER says it can run — the last answer, never a wait.
   *
   * <p><b>Started by a render and never awaited by one.</b> `refreshTeamServers` already has this
   * shape and it is here for the same reason: this is a process spawn with an 8 s cap, and awaiting
   * it inside `render` holds the whole panel for as long as a cold or hanging binary takes. The
   * probe repaints when it lands, and the freshness check is what stops the loop — the render it
   * triggers finds the answer fresh and starts nothing.</p>
   *
   * <p>An empty answer is not "everything is fine": `availabilityOf` reads a missing row as UNKNOWN,
   * and the card then shows what it always showed.</p>
   */
  private providerHealth(): ProvidersAnswer {
    const AGE_MS = 10_000;
    const server = serverPath(this.context.globalStorageUri);
    const executable = server?.fsPath ?? '';

    // The path is part of the key. Reinstalling or repointing the server inside the window would
    // otherwise serve the previous binary's verdict — which can badge a reviewer the new one runs.
    if (executable !== this.providersFrom || Date.now() - this.providersAt >= AGE_MS) {
      void this.refreshProviders(executable);
    }

    return this.providersCache;
  }

  /** One probe at a time, and a repaint when it lands rather than a wait while it runs. */
  private async refreshProviders(executable: string): Promise<void> {
    if (this.providersInFlight) {
      return;
    }
    this.providersInFlight = true;
    try {
      const answer = executable.length === 0
        ? { reported: {}, asked: false, answered: false }
        : await readProviders(executable);
      const changed = JSON.stringify(answer) !== JSON.stringify(this.providersCache);
      this.providersCache = answer;
      this.providersAt = Date.now();
      this.providersFrom = executable;
      if (changed) {
        await this.render();
      }
    } finally {
      this.providersInFlight = false;
    }
  }

  /** Re-read everything and repaint: the configuration, the sessions, the ledger and the probes. */
  async render(): Promise<void> {
    if (this.view === undefined) {
      return;
    }
    const config = vscode.workspace.getConfiguration('coai');
    const settings = settingsFrom((section) => config.get(section));
    const vendors = vendorsFrom(this.read(config)('vendors'));
    this.codexModels = await this.readCodexModels();
    this.agyModels = await this.readAgyModels(vendors);
    // The published version is read FIRST because the server's status is stated against it — and
    // both belong to the side this extension host is running on, never to "the machine".
    const published = await this.publishedVersion();
    const sessions = await this.readSessions();
    const state = {
      settings,
      vendors,
      codexModels: this.codexModels,
      agyModels: this.agyModels,
      server: await serverOnThisSide(this.context.globalStorageUri, this.context.globalState, published),
      side: sideLabel(vscode.env.remoteName, process.env['WSL_DISTRO_NAME']),
      perSide: this.perSide(config),
      questions: this.watcher.openQuestions,
      openSections: this.openSections,
      sessions,
      usage: this.remembered(await this.readUsage()),
      usageWindow: this.usageWindow,
      latestServerVersion: published,
      latestTeamServerVersion: this.latestTeamServer,
      cliStatus: await this.vendorCliStatus(vendors),
      modelPrices: await this.modelPrices(vendors),
      snippetStatus: await pastedSnippetStatus(),
      localEngines: await this.probeLocalEngines(vendors),
      teamServers: this.teamServerStates(config),
      providers: this.providerHealth(),
      usageScope: this.usageScope,
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
      void this.refreshTeamServers();

      return;
    }

    // Never awaited: the section draws from what is already known, and this repaints when it
    // lands. A render that waited on a Team server would be a panel that hangs when one is slow.
    void this.refreshTeamServers();

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
    // One Windows-side question per PASS, shared by every local row. It used to be asked once per
    // row: ten local reviewers on a machine where nothing answers meant ten interop launches, all
    // asking the same box the same thing. Held for the pass and dropped at the end of it, because
    // caching a failed answer is what the row-level cache deliberately does not do either.
    this.windowsSideThisPass = undefined;
    const probed: Record<string, LocalEngine> = {};
    for (const vendor of vendors.filter((v) => v.runtime === 'local')) {
      const engine = await this.probeLocalEngine(vendor);
      if (engine !== undefined) {
        probed[vendor.id] = engine;
      }
    }
    this.windowsSideThisPass = undefined;

    return probed;
  }

  /** The Windows-side answer for THIS pass, asked at most once and never kept beyond it. */
  private windowsSideThisPass: Promise<string> | undefined = undefined;

  private windowsSideOnce(): Promise<string> {
    this.windowsSideThisPass ??= windowsSideEngine();

    return this.windowsSideThisPass;
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
    const engine = wanted.length > 0
      ? await probeEngine(wanted)
      : await discoverEngine(undefined, undefined, () => this.windowsSideOnce());
    // The endpoint may have changed WHILE this probe was in flight; the answer then belongs to a
    // configuration nobody is looking at any more, and showing it would be worse than showing
    // nothing. The next repaint probes the current one.
    const current = this.vendorsHere()
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
  /** The two public lists, fetched at most once a day and kept in memory. */
  private async refreshPriceTables(): Promise<void> {
    const A_DAY = 24 * 60 * 60 * 1000;
    if (Date.now() - this.pricesCheckedAt > A_DAY) {
      this.pricesCheckedAt = Date.now();
      [this.openRouterPrices, this.liteLlmPrices] = await Promise.all([
        fetchTable(OPENROUTER_MODELS, openRouterTable),
        fetchTable(LITELLM_PRICES, liteLlmTable),
      ]);
    }
  }

  private async modelPrices(vendors: readonly Vendor[]): Promise<Record<string, ModelPrice>> {
    await this.refreshPriceTables();

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
  async forgetUsage(provider: string): Promise<void> {
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
    const vendor = this.vendorsHere().find((v) => v.id === id);
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
    const vendor = this.vendorsHere().find((v) => v.id === id);
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
        const vendors = vendorsFrom(this.read(config)('vendors')).map((v) =>
          v.id === write.vendor ? { ...v, [write.key]: write.value } : v,
        );
        await this.save(config, 'vendors', vendors);
        return;
      }
      case 'role': {
        // A record, merged rather than replaced: writing one role's number must not drop the other
        // three, and the stored object is what every other role reads on the next repaint.
        const current = config.get<Record<string, unknown>>(write.key) ?? {};
        await this.save(config, write.key, roleRecordUpdate(current, write.role, write.value));
        return;
      }
      case 'plain':
        await this.save(config, write.key, write.value);
        // Turning the per-side switch ON seeds this side with what it reads today, so nothing
        // changes until something is edited. An empty overlay looks identical - until the first
        // shared edit on another side silently changes this one, which is the surprise this feature
        // exists to remove. Idempotent, so switching off and on again keeps what was configured.
        if (write.key === 'perSideSettings' && write.value === true) {
          await seedIfEmpty(
            this.context.globalState,
            thisSide(this.context.globalStorageUri),
            (section) => config.get(section));
        }
        if (write.key === 'perSideSettings') {
          await this.carryTeamLogins(write.value === true);
        }
        return;
      default: {
        // Every kind is handled, and the compiler is what says so.
        const unhandled: never = write;
        return unhandled;
      }
    }
  }

  /**
   * Carry the Team-server sign-ins across the switch, so neither direction signs anybody out.
   *
   * <p>Symmetric, and the same idea as {@link seedIfEmpty} one layer over: turning the switch ON
   * copies the shared record into this side's, so this side keeps the session it had and only
   * DIVERGES when somebody signs in again; turning it OFF promotes this side's record to the shared
   * one when there is none, so the side that merged the sides is the one whose account they share.
   * Never overwrites — a record that already exists is a decision somebody made.</p>
   *
   * <p>The other sides need nothing here. Their tokens now disagree with the intent that applies to
   * them, and `reconcile` re-mints them as the shared account on their next refresh — which is what
   * stops a WSL window from quietly going on reviewing as the account it happened to hold. Both
   * directions were raised on the plan round.</p>
   */
  private async carryTeamLogins(perSide: boolean): Promise<void> {
    const side = this.sideKeyHere();
    const state = this.context.globalState;
    for (const server of this.teamServers(vscode.workspace.getConfiguration('coai'))) {
      const from = signedInKey(server.id, perSide ? '' : side);
      const to = signedInKey(server.id, perSide ? side : '');
      const carried = state.get<SignedIn>(from);
      // Turning sharing ON (perSide false) OVERWRITES the shared record. Guarding it the way the
      // other direction is guarded was a defect: the shared record is never cleared, so it almost
      // always holds an older account — the promotion would be skipped, that stale account would
      // become everybody's intent, and this side would then have its live session replaced by one
      // it had left behind. Caught on the code round. The other direction keeps its guard: seeding
      // a side that already has a record would discard a decision somebody made.
      if (carried !== undefined && (!perSide || state.get<SignedIn>(to) === undefined)) {
        await state.update(to, carried);
      }

      // The SIGN-OUT travels with the sign-in, and forgetting it was a hole: sign out with the sides
      // shared, separate them before another side has refreshed, and that side finds a token, no
      // intent — and no revocation in its new scope. So it keeps a session the person ended. An
      // intent that was NOT carried is exactly the case where this matters. Raised on the second
      // code round.
      const stamped = state.get<number>(revokedKey(server.id, perSide ? '' : side));
      if (stamped !== undefined && state.get<number>(revokedKey(server.id, perSide ? side : '')) === undefined) {
        await state.update(revokedKey(server.id, perSide ? side : ''), stamped);
      }
    }
  }

  /** Whether this side keeps its own settings. Shared by every side, deliberately: one switch. */
  private perSide(config: vscode.WorkspaceConfiguration): boolean {
    return config.get<boolean>('perSideSettings') === true;
  }

  /**
   * How this side reads a setting: its own value first, the shared one otherwise.
   *
   * <p>One accessor, because a read that goes around it is a setting that silently stays shared —
   * and the person who set a different proxy on one side would find out when a review ran against
   * the wrong company's server.</p>
   */
  private read(config: vscode.WorkspaceConfiguration): ConfigReader {
    const shared: ConfigReader = (section) => config.get(section);

    return this.perSide(config)
      ? overlaidReader(shared, readOverlay(this.context.globalState, thisSide(this.context.globalStorageUri)))
      : shared;
  }

  /** The vendors as THIS side has them. */
  private vendorsHere(): readonly Vendor[] {
    return vendorsFrom(this.read(vscode.workspace.getConfiguration('coai'))('vendors'));
  }

  /**
   * Writes one setting, and SAYS SO when VS Code refuses.
   *
   * <p>Every write used to be a bare `await config.update(...)` reached through `void this.write(…)`,
   * so a rejection went nowhere at all. That is half of the bug the operator reported as "the
   * checkboxes come unticked": the three gate switches were never declared in
   * `contributes.configuration`, VS Code will not persist a key it does not know, and the refusal
   * was swallowed — the box lit up, nothing was saved, and nothing said a word. The declaration is
   * the fix; this is what makes the NEXT one loud instead of silent.</p>
   */
  private async save(config: vscode.WorkspaceConfiguration, key: string, value: unknown): Promise<void> {
    // A setting this side keeps to itself never reaches settings.json: that file is the CLIENT's, and
    // VS Code hands it to every extension host, which is the whole reason the per-side switch exists.
    if (this.perSide(config) && OVERLAID_SETTINGS.includes(key)) {
      await writeOverlay(this.context.globalState, thisSide(this.context.globalStorageUri), key, value);

      return;
    }

    try {
      await config.update(key, value, vscode.ConfigurationTarget.Global);
    } catch (error) {
      const detail = error instanceof Error ? error.message : String(error);
      void vscode.window.showErrorMessage(`ConnectOtherAIs could not save "coai.${key}": ${detail}`);
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
        // The cached Team-server totals are the OTHER window's — the same staleness the scope toggle
        // had, one control along. Caught on the code round.
        this.teamCheckedAt = 0;
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
      case 'fixWslNetwork':
        await this.fixWslNetwork();
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
      case 'addTeamServer':
        await this.addTeamServer();
        break;
      case 'signInTeamServer':
        if (id !== undefined) {
          await this.signInTeamServer(id);
        }
        break;
      case 'signOutTeamServer':
        if (id !== undefined) {
          await this.signOutTeamServer(id);
        }
        break;
      case 'removeTeamServer':
        if (id !== undefined) {
          await this.removeTeamServer(id);
        }
        break;
      case 'teamUsageScope':
        // Only an admin is ever shown the control, and the SERVER refuses `company` for anybody
        // else — so this is a display preference, not a permission.
        this.usageScope = this.usageScope === 'me' ? 'company' : 'me';
        // The cached totals are the OTHER scope's. Without this the button flips, the label says
        // "the whole company", and the figures underneath are still that one person's — for up to a
        // minute, with nothing saying so. Caught on the code round.
        this.teamCheckedAt = 0;
        await this.render();
        break;
      default: {
        // A PanelCommand with no case above lands here and fails to compile. That is the whole
        // guard: the Update button was posting a command nobody handled, and nothing said so.
        const unhandled: never = command;
        void unhandled;
        return;
      }
    }

    // Choosing Today/Week/Month/Year changes ONE region and nothing else, so it patches that region
    // rather than repainting the panel. It used to fall through to the full render below — which
    // stats the server binary, probes every vendor CLI, asks GitHub what is published and fetches
    // two price tables — and switching a window took seconds for an arithmetic change over rows the
    // extension already had in hand.
    await this.render();
  }

  /**
   * Points this WSL distro's `127.0.0.1` at the Windows host, or puts it back.
   *
   * <p><b>What it cannot do, and why the button stops where it does.</b> `.wslconfig` is read at
   * cold start, so applying it means `wsl --shutdown` — which terminates the distro this extension
   * host is running in, mid-call. So the file is written here and the one command is handed to the
   * person. Nothing is written before they press this, and nothing is written before they have seen
   * the exact text.</p>
   *
   * <p>It is a TOGGLE because the setting is global to every distro, `docker-desktop` included, and
   * mirrored mode is known to conflict with some VPN clients: a switch with no way back is not a
   * cure. Raised on the plan by the local reviewer, 2026-09-03.</p>
   */
  private async fixWslNetwork(): Promise<void> {
    const path = await windowsWslconfigPath();
    if (path.length === 0) {
      void vscode.window.showWarningMessage(
        'The Windows side of this machine could not be reached through interop, so nothing was '
        + 'written. Put these two lines in %USERPROFILE%\\.wslconfig by hand, then run '
        + `\`wsl --shutdown\` from Windows:\n\n${mirroredLines('mirrored')}`,
      );

      return;
    }

    const existing = await readFile(path, 'utf8').catch(() => '');
    if (networkingModeOf(existing) === 'mirrored') {
      await this.mirroredIsAlreadyWritten(path, existing);

      return;
    }

    await this.setNetworkingMode(path, existing, 'mirrored');
  }

  /**
   * The second press, when the file already says what the first press wrote.
   *
   * <p>It used to be a blind toggle, and that was a trap: writing mirrored does not make the engine
   * reachable until WSL restarts, so the button and its note stayed exactly as they were — and
   * pressing again, which is what a person does when nothing appears to have happened, silently
   * undid the fix. Found by Gemini 3.7 Flash in the code round. Now the second press explains the
   * restart, and reverting is a deliberate choice with its own button.</p>
   */
  private async mirroredIsAlreadyWritten(path: string, existing: string): Promise<void> {
    const restart = 'Copy `wsl --shutdown`';
    const revert = 'Put it back to nat';
    const choice = await vscode.window.showInformationMessage(
      `${path} already says networkingMode=mirrored.`,
      {
        modal: true,
        detail:
          'It takes effect when WSL next starts cold: run `wsl --shutdown` from Windows, then reopen '
          + 'this window. Nothing here can run it — it would terminate the distro this window is '
          + 'attached to, mid-call.',
      },
      restart,
      revert,
    );
    if (choice === restart) {
      await vscode.env.clipboard.writeText('wsl --shutdown');
    }
    if (choice === revert) {
      await this.setNetworkingMode(path, existing, 'nat');
    }
  }

  /** Shows the whole merged file, writes it when they say so, and reports what actually happened. */
  private async setNetworkingMode(path: string, existing: string, mode: NetworkingMode): Promise<void> {
    const merged = wslconfigWith(existing, mode);
    if (merged.refused.length > 0) {
      void vscode.window.showWarningMessage(
        `${merged.refused}. Set it by hand instead:\n\n${mirroredLines(mode)}`,
      );

      return;
    }
    if (!merged.changed) {
      void vscode.window.showInformationMessage(`${path} already says networkingMode=${mode}.`);

      return;
    }

    const write = 'Write it';
    const confirmed = await vscode.window.showWarningMessage(
      `Set networkingMode=${mode} in ${path}?`,
      {
        modal: true,
        // The WHOLE file, not the two lines this adds: the question a person needs answered before
        // approving a global change is whether their other settings survive it, and a preview that
        // shows only the addition cannot answer it.
        detail:
          `${previewOf(merged.text)}\n\nThis file is global: every WSL distro on this machine reads `
          + 'it, docker-desktop included. Nothing changes until WSL is restarted, which this cannot '
          + 'do for you — it would terminate the distro this window is attached to.',
      },
      write,
    );
    if (confirmed !== write) {
      return;
    }

    const outcome = await writeWslconfig(path, merged.text);
    if (!outcome.written) {
      void vscode.window.showErrorMessage(outcome.message);

      return;
    }
    if (outcome.message.length > 0) {
      // Written, but something about confirming it did not go to plan. Saying "it failed" here
      // would be a lie about a file that HAS changed, and the next press would offer to undo it.
      void vscode.window.showWarningMessage(outcome.message);
    }

    const copy = 'Copy the command';
    const next = await vscode.window.showInformationMessage(
      `${path} now says networkingMode=${mode}. Run \`wsl --shutdown\` from Windows (not from here), `
      + 'then reopen this window — the setting is read when WSL next starts cold.',
      copy,
    );
    if (next === copy) {
      await vscode.env.clipboard.writeText('wsl --shutdown');
    }
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
  // ---------- Team servers ----------

  private teamServers(config: vscode.WorkspaceConfiguration): TeamServer[] {
    return teamServersFrom(this.read(config)('teamServers'));
  }

  /**
   * What the panel draws each server from.
   *
   * <p>Never awaits a network call: the section renders from what was last learned, and a fetch
   * started elsewhere repaints when it lands. A panel that waited would be a panel that hangs
   * whenever a server is slow.</p>
   */
  private teamServerStates(config: vscode.WorkspaceConfiguration): TeamServerState[] {
    const side = this.sideKeyHere();
    const scope = this.intentScope(config);

    return this.teamServers(config).map((server) => {
      const known = this.catalogs[server.id];
      // The FACT, not the intent. `email` is whose token file THIS side holds, so a WSL window that
      // has not minted one yet reads "not signed in here" instead of claiming the Windows session —
      // which is the whole defect this change is about. `elsewhere` is the intent, and only when
      // there is no token here, so the row can offer the one button that fixes it.
      const here = this.context.globalState.get<TokenFact>(tokenFactKey(server.id, side));
      const intent = this.context.globalState.get<SignedIn>(signedInKey(server.id, scope));
      const failed = this.mintFailure[server.id];

      return {
        server,
        email: here?.email ?? '',
        elsewhere: here === undefined ? (intent?.email ?? '') : '',
        catalog: known?.catalog,
        usage: known?.usage,
        // A failed silent sign-in is shown whether or not this side still holds SOMETHING. It used
        // to be suppressed while a token was present, which is the one case where it matters most:
        // the token belongs to an account the intent no longer names, so the row read "signed in as
        // <the wrong person>" with no sign that anything had gone wrong. Caught on the code round.
        problem: known?.problem ?? (failed?.message ?? ''),
        stale: known?.stale ?? false,
        busy: this.busy[server.id] ?? '',
      };
    });
  }

  /** This side of the machine, as a key component. Always this side's own — see `TokenFact`. */
  private sideKeyHere(): string {
    return sideKey(thisSide(this.context.globalStorageUri));
  }

  /**
   * Which sign-in record applies: the shared one, or this side's own.
   *
   * <p>Derived from the SAME switch that decides whether this side keeps its own settings, so
   * "does this side keep its own things" has one answer rather than two that can disagree.</p>
   */
  private intentScope(config: vscode.WorkspaceConfiguration): string {
    return intentScopeOf(this.sideKeyHere(), this.perSide(config));
  }

  private authHost(): AuthHost {
    return {
      dataDir: coaiDataDir(),
      side: this.sideKeyHere(),
      perSide: this.perSide(vscode.workspace.getConfiguration('coai')),
      // Injected, because both stamps it writes are persisted and then compared to each other —
      // `.claude/rules/shared/common/utc-timestamps.md` rule 2. Raised on the code round.
      now: () => Date.now(),
      state: {
        get: <T,>(key: string) => this.context.globalState.get<T>(key),
        update: async (key: string, value: unknown) => {
          await this.context.globalState.update(key, value);
        },
      },
      getSession: async (scope, interactive) => {
        try {
          // `clearSessionPreference` ONLY when a person asked for this. Paired with
          // `createIfNone: false` it forces a prompt that is simultaneously forbidden, so a renewal
          // could only ever return nothing — and the extension would read that as an expired
          // identity session and sign them out. Weekly. See `renewIfDue`.
          const session = await vscode.authentication.getSession(
            'microsoft',
            [scope],
            interactive ? { createIfNone: true, clearSessionPreference: true } : { createIfNone: false },
          );

          return session?.accessToken;
        } catch {
          // A cancelled or failed sign-in is an answer, not a crash in a command handler.
          return undefined;
        }
      },
      confirmApplication: async (server, applicationId) => {
        const go = 'Sign in';
        const answer = await vscode.window.showWarningMessage(
          `Sign in to ${server.name}?`,
          {
            modal: true,
            detail: `${canonicalTeamServerUrl(server.url)} is asking for a token for Microsoft `
              + `application ${applicationId}. Only continue if that is your company's `
              + `ConnectOtherAIs server — a token minted here can be used by whoever runs it.`,
          },
          go,
        );

        return answer === go;
      },
      say: (message) => void vscode.window.showInformationMessage(message),
    };
  }

  /**
   * Add a server: verify it BEFORE saving, so a mistake is caught now rather than at the first review.
   *
   * <p>Raised on the plan round — a URL that is merely syntactically valid buys nothing, and the
   * failure would otherwise surface as a broken reviewer days later. A server that is simply DOWN is
   * not a mistake, so saving is still offered after the warning.</p>
   */
  private async addTeamServer(): Promise<void> {
    const name = await vscode.window.showInputBox({
      title: 'Add a Team server',
      prompt: 'A short name for it — yours, and only for display',
      placeHolder: 'RemSoft Dev',
      validateInput: (v) => (v.trim().length === 0 ? 'A name is needed' : undefined),
    });
    if (name === undefined) {
      return;
    }

    const url = await vscode.window.showInputBox({
      title: `Add ${name.trim()}`,
      prompt: 'Its address',
      placeHolder: 'https://coai.example.com',
      validateInput: (v) => (v.trim().startsWith('http') ? undefined : 'An https address is needed'),
    });
    if (url === undefined) {
      return;
    }

    const config = vscode.workspace.getConfiguration('coai');
    const existing = this.teamServers(config);
    const server: TeamServer = {
      id: newTeamServerId(name, existing.map((s) => s.id)),
      name: name.trim(),
      url: url.trim(),
    };

    if (!(await this.reachable(server))) {
      return;
    }

    await config.update('teamServers', [...existing, server], vscode.ConfigurationTarget.Global);
    await this.render();
  }

  /** Ask the server what it wants, and let the person decide what to do when it will not say. */
  private async reachable(server: TeamServer): Promise<boolean> {
    const config = await fetchClientConfig(server.url);
    if (config.ok) {
      return true;
    }

    const anyway = 'Add it anyway';
    const answer = await vscode.window.showWarningMessage(
      `${server.name} could not be checked: ${config.message}`,
      {
        modal: true,
        detail: 'A server that is only down right now is not a mistake — but a wrong address is, '
          + 'and it would otherwise surface as a broken reviewer days from now.',
      },
      anyway,
    );

    return answer === anyway;
  }

  /**
   * Whether a reviewer row belongs to this server.
   *
   * <p>By id when the row records one, because an address is correctable and an id is not: fixing a
   * typo in a hostname would otherwise leave rows nothing would ever match again — their model list
   * frozen, and removal quietly finding none of them. Caught on the code round.</p>
   */
  private static rowBelongsTo(vendor: Vendor, server: TeamServer): boolean {
    return (vendor.teamServerId ?? '').length > 0
      ? vendor.teamServerId === server.id
      : canonicalTeamServerUrl(vendor.baseUrl) === canonicalTeamServerUrl(server.url);
  }

  private serverNamed(id: string): TeamServer | undefined {
    return this.teamServers(vscode.workspace.getConfiguration('coai')).find((s) => s.id === id);
  }

  private async signInTeamServer(id: string): Promise<void> {
    const server = this.serverNamed(id);
    if (server === undefined) {
      return;
    }

    // Sign-in awaits four requests and a Microsoft prompt. Without a visible in-progress state the
    // row still reads "Not signed in" throughout, the button stays pressable, and a person cannot
    // tell a slow sign-in from one that did nothing — so they press it again. Caught on the code
    // round.
    // A person pressing the button is a person saying "try again now": whatever the last silent
    // attempt decided, it must not delay this one or keep its sentence on the row afterwards.
    this.mintFailure = PanelProvider.without(this.mintFailure, server.id);
    this.busy = { ...this.busy, [server.id]: 'Signing in…' };
    await this.render();
    try {
      await this.signInAndTell(server);
    } finally {
      const { [server.id]: _done, ...rest } = this.busy;
      this.busy = rest;
      await this.render();
    }
  }

  private async signInAndTell(server: TeamServer): Promise<void> {
    const result = await signIn(server, this.authHost());
    if (result.ok) {
      await this.refreshCatalog(server);
    } else {
      void vscode.window.showWarningMessage(result.message);
    }
  }

  private async signOutTeamServer(id: string): Promise<void> {
    const server = this.serverNamed(id);
    if (server === undefined) {
      return;
    }

    const result = await signOut(server, this.authHost(), await readToken(coaiDataDir(), server.url));
    if (!result.ok) {
      void vscode.window.showWarningMessage(result.message);
    }

    delete this.catalogs[server.id];
    await this.render();
  }

  /**
   * Remove a server, and deal with everything that pointed at it.
   *
   * <p>Raised twice on the plan round: removing a server used to leave its token file on disk and
   * its reviewer rows in the settings, so a credential stayed behind and the panel showed reviewers
   * that could only fail. The rows are NAMED and the person decides — they carry spending history,
   * so deleting them silently is not reversible.</p>
   */
  private async removeTeamServer(id: string): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    const servers = this.teamServers(config);
    const server = servers.find((s) => s.id === id);
    if (server === undefined) {
      return;
    }

    const rows = vendorsFrom(this.read(config)('vendors')).filter(
      (v) => v.runtime === 'remote' && PanelProvider.rowBelongsTo(v, server),
    );
    const both = rows.length === 1 ? 'Remove it and 1 reviewer' : `Remove it and ${rows.length} reviewers`;
    const answer = await vscode.window.showWarningMessage(
      `Remove ${server.name}?`,
      {
        modal: true,
        detail: rows.length === 0
          ? 'You will be signed out of it and its token deleted from this machine.'
          : `You will be signed out of it and its token deleted. These reviewers point at it and `
            + `cannot work without it: ${rows.map((r) => r.id).join(', ')}. Their spending history `
            + `is kept either way.`,
      },
      ...(rows.length === 0 ? ['Remove'] : [both, 'Remove the server only']),
    );
    if (answer === undefined) {
      return;
    }

    // Signed out FIRST, so the session is ended ON the server rather than left running there. Its
    // failure is SHOWN: a token file that survived removal is a credential nobody is watching for.
    const gone = await signOut(server, this.authHost(), await readToken(coaiDataDir(), server.url));
    if (!gone.ok) {
      void vscode.window.showWarningMessage(gone.message);
    }

    delete this.catalogs[server.id];

    await config.update(
      'teamServers',
      servers.filter((s) => s.id !== server.id),
      vscode.ConfigurationTarget.Global,
    );
    if (answer === both) {
      const ids = new Set(rows.map((r) => r.id));
      const kept = vendorsFrom(this.read(config)('vendors')).filter((v) => !ids.has(v.id));
      await config.update('vendors', kept, vscode.ConfigurationTarget.Global);
    }

    await this.render();
  }

  /** Ask one server what it offers and what has been spent on it, and remember both. */
  private async refreshCatalog(server: TeamServer, renewalProblem = ''): Promise<void> {
    const token = await readToken(coaiDataDir(), server.url);
    if (token.length === 0) {
      return;
    }

    const answer = await catalogOf(server, token);
    const known = this.catalogs[server.id];
    // `company` is asked for ONLY where THIS server's catalog said this account is an admin. The
    // control is global but the permission is not: an admin on one server and an ordinary user on
    // another would otherwise have the second refuse every request and go stale. Caught on the code
    // round.
    const scope = this.usageScope === 'company' && (known?.catalog?.isAdmin === true) ? 'company' : 'me';
    const spent = await fetchUsage(
      server.url,
      token,
      WINDOW_ON_THE_WIRE[this.usageWindow],
      scope,
    );
    // A renewal that failed is reported even when the catalog call SUCCEEDED, because the old token
    // stays valid for up to two days: without this the panel looked healthy right until reviews
    // started failing with a 401 nobody had been warned about. Caught on the code round.
    const problem = answer.ok ? renewalProblem : answer.message;
    this.catalogs[server.id] = {
      catalog: answer.ok ? answer.value : known?.catalog,
      usage: spent.ok ? spent.value : known?.usage,
      // A usage call that failed is not silence either — it would otherwise render as "nothing
      // recorded on this server", which is a measurement, not an absence of one.
      problem: problem.length > 0 ? problem : (spent.ok ? '' : spent.message),
      stale: answer.ok ? false : known?.catalog !== undefined,
    };
  }

/**
   * Renew quietly, then re-ask, for every server this machine is signed into.
   *
   * <p>Started by a render and never awaited by one — the panel draws from what is already known and
   * this repaints when it lands. The freshness check is what stops a repaint loop: a render kicks
   * this off, it renders once at the end, and that render finds the answer fresh and starts
   * nothing.</p>
   */
  private async refreshTeamServers(): Promise<void> {
    if (Date.now() - this.teamCheckedAt < TEAM_SERVER_FRESH_MS) {
      return;
    }

    const servers = this.teamServers(vscode.workspace.getConfiguration('coai'));
    if (servers.length === 0 || this.refreshing) {
      return;
    }

    // Servers are independent, and asked CONCURRENTLY. Serially, one unreachable server spent its
    // full deadline before the next was even tried, so five of them could keep every healthy one
    // showing "asking what it offers…" for a minute. `allSettled`, because one server failing is
    // that server's problem and not the others'.
    this.refreshing = true;
    try {
      await Promise.allSettled(servers.map(async (server) => {
        await this.refreshCatalog(server, await this.reconcileHere(server));
      }));
    } finally {
      // Stamped when the work FINISHED, not when it started. Stamping at entry meant a run that
      // took longer than the freshness window was immediately fresh-expired by its own closing
      // render — a refresh loop that never idles. Caught on the code round.
      this.teamCheckedAt = Date.now();
      this.refreshing = false;
    }

    await this.render();
  }

  /**
   * Bring this side's session into line, and remember a failure instead of repeating it.
   *
   * <p>Returns the sentence the row should show, or empty. A silent sign-in that failed is not
   * retried for ten minutes: the refresh runs every sixty seconds, and an identity provider that
   * cannot answer in this window would otherwise be asked sixty times an hour. The remembered
   * message keeps the row explaining itself while nothing is being tried.</p>
   */
  private async reconcileHere(server: TeamServer): Promise<string> {
    const failed = this.mintFailure[server.id];
    if (failed !== undefined && Date.now() - failed.at < MINT_BACKOFF_MS) {
      return failed.message;
    }

    // ASKED before it is DONE, so the row can say what is happening while it happens — and so that
    // it says nothing on the ordinary refresh, where the answer is `nothing` and a spinner once a
    // minute would be worse than the silence it replaced.
    const host = this.authHost();
    if (await plannedAction(server, host) === 'nothing') {
      this.mintFailure = PanelProvider.without(this.mintFailure, server.id);

      return '';
    }

    // SAYS SO while it runs. A silent sign-in is four requests and can take seconds, and without
    // this the row read "<somebody> is signed in on another side — press Sign in to use it here"
    // with an enabled button, throughout: the person is invited to start a second, interactive
    // sign-in against the one already in flight. The same finding the interactive path had on
    // epic 3's code round, one path over. Raised on this one's.
    this.busy = { ...this.busy, [server.id]: 'Signing in…' };
    await this.render();
    try {
      const done = await reconcile(server, host);
      this.mintFailure = done.problem.length > 0
        ? { ...this.mintFailure, [server.id]: { at: Date.now(), message: done.problem } }
        : PanelProvider.without(this.mintFailure, server.id);

      return done.problem;
    } finally {
      this.busy = PanelProvider.without(this.busy, server.id);
    }
  }

  /** One entry dropped, the rest kept — a copy, because nothing here is mutated in place. */
  private static without<T>(
    map: Readonly<Record<string, T>>,
    key: string,
  ): Readonly<Record<string, T>> {
    const { [key]: _gone, ...rest } = map;

    return rest;
  }

  /**
   * A reviewer from a Team server: pick the server, then pick from what it OFFERS.
   *
   * <p>The catalog is fetched at the moment of the choice rather than read from the cache. A stale
   * cache would mint a row naming a vendor the server has since dropped, and the failure would then
   * arrive at the first review reading exactly like a typo. Raised as blocking on the plan round.</p>
   *
   * <p>The vendor's id comes from the catalog VERBATIM — never from the label shown to the person —
   * because that string is what `--vendor` sends and what the server matches.</p>
   */
  private async addFromTeamServer(server: TeamServer): Promise<Vendor | undefined> {
    const token = await readToken(coaiDataDir(), server.url);
    const answer = await catalogOf(server, token);
    if (!answer.ok) {
      void vscode.window.showWarningMessage(`${server.name}: ${answer.message}`);

      return undefined;
    }

    // A catalog is a STRANGER'S answer. An id like `../../other` would otherwise be copied verbatim
    // into a reviewer row id — which names that row's spending history and its vault key — and onto
    // a command line as `--vendor`. Entries that cannot be an id are dropped and said out loud
    // rather than silently, because a vendor going missing needs a reason. Caught on the code round.
    const usable = (answer.value.vendors ?? []).filter((v) => isUsableVendorId(v.id));
    const refused = (answer.value.vendors ?? []).length - usable.length;
    if (refused > 0) {
      void vscode.window.showWarningMessage(
        `${server.name} offered ${refused} vendor(s) whose name this extension will not use as an `
          + 'id. They are not listed.',
      );
    }

    const offered = usable;
    if (offered.length === 0) {
      void vscode.window.showWarningMessage(
        `${server.name} offers no vendors yet — the operator has not added any accounts to it.`,
      );

      return undefined;
    }

    const picked = await vscode.window.showQuickPick(
      offered.map((v) => ({ label: v.id, detail: slotSentence(v), vendor: v })),
      { title: `Add a reviewer from ${server.name}`, placeHolder: 'Which vendor should review?' },
    );
    if (picked === undefined) {
      return undefined;
    }

    return {
      id: remoteVendorRowId(server.id, picked.vendor.id),
      runtime: 'remote',
      // Which server, by its permanent id — see `Vendor.teamServerId`.
      teamServerId: server.id,
      // Verbatim from the catalog: this is what `--vendor` sends and what the server matches.
      remoteVendor: picked.vendor.id,
      model: picked.vendor.models[0] ?? '',
      enabled: true,
      plan: true,
      code: true,
      baseUrl: canonicalTeamServerUrl(server.url),
      executablePath: '',
      pricePerMillionIn: 0,
      pricePerMillionOut: 0,
    };
  }

  private async addVendor(): Promise<void> {
    const existing = new Set(this.vendorsHere().map((v) => v.id));
    // A preset already in the panel is not offered twice; the blank one (empty id) always is.
    const offered = VENDOR_PRESETS.filter((p) => p.id.length === 0 || !existing.has(p.id));
    // Only servers THIS SIDE holds a token for. One that is merely configured — or one signed in on
    // another side of this machine — can neither be asked what it offers nor run a review here, so
    // offering it would be a dead entry.
    const config0 = vscode.workspace.getConfiguration('coai');
    const side = this.sideKeyHere();
    const teamServers = this.teamServers(config0).filter(
      (s) => this.context.globalState.get<TokenFact>(tokenFactKey(s.id, side)) !== undefined,
    );
    const picked = await vscode.window.showQuickPick(
      [
        ...offered.map((p) => ({ label: p.label, detail: p.hint, preset: p, server: undefined })),
        ...teamServers.map((s) => ({
          label: `Team server ${s.name}`,
          detail: `on ${canonicalTeamServerUrl(s.url)} — the company's subscription, nothing to install`,
          preset: undefined,
          server: s,
        })),
      ],
      { title: 'Add a reviewer', placeHolder: 'Which vendor should review as well?' },
    );
    if (picked === undefined) {
      return;
    }

    if (picked.server !== undefined) {
      const fromServer = await this.addFromTeamServer(picked.server);
      if (fromServer !== undefined) {
        await this.saveVendor(fromServer);
      }

      return;
    }

    let vendor: Vendor = { ...picked.preset! };
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

    await this.saveVendor(vendor);
  }

  /** One place a new reviewer is written, so both routes refuse a duplicate the same way. */
  private async saveVendor(vendor: Vendor): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    const vendors = vendorsFrom(this.read(config)('vendors'));
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
    const vendors = vendorsFrom(this.read(config)('vendors'));
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
  /**
   * The models this subscription actually has, from the CLI that knows.
   *
   * <p>Only when a vendor is set to that runtime — an installed `agy` nobody uses is not worth a
   * process on every repaint — and only once an hour. Anything at all going wrong leaves the list
   * empty, which the dropdown reads as "use the fallback and say so".</p>
   */
  private async readAgyModels(vendors: readonly Vendor[]): Promise<ModelChoice[]> {
    const AN_HOUR = 60 * 60 * 1000;
    if (!vendors.some((v) => v.runtime === 'antigravity')) {
      return [];
    }
    if (Date.now() - this.agyCheckedAt < AN_HOUR && this.agyModels.length > 0) {
      return this.agyModels;
    }
    this.agyCheckedAt = Date.now();
    const agy = vendors.find((v) => v.runtime === 'antigravity')?.executablePath || 'agy';
    const { code, output } = await capture(unquoted(agy), ['models'], false, 20_000);

    return code === 0 ? parseAgyModels(output) : [];
  }

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
    // The Team server's line, on the same clock and in the same breath: two release lines read from
    // one tag list, and a second timer would mean two rate limits and two answers about one moment.
    this.latestTeamServer = (await latestTeamServerVersion()) ?? '';
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
