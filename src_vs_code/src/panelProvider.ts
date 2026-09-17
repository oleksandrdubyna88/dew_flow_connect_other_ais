import { readFile } from 'node:fs/promises';
import * as vscode from 'vscode';
import { DISCOVERY_KEY } from './chatDiscovery';
import { chatSettingsFrom, clearedByWriting } from './chatSettings';
import { phrasesFrom, type Phrase } from './phrases';
import { chatForgetKey, rememberedChat } from './chatSpendRows';
import { phraseCopier } from './phraseCopy';
import { chatModelPresetsFrom, vendorOfPreset } from './chatPresets';
import { ViewHandle, isDisposedRejection } from './viewHandle';
import { pastedSnippetStatus } from './snippetInWorkspace';
import { discoverEngine, LocalEngine, openAiBaseOf, probeEngine } from './localEngines';
import { EscalationWatcher } from './escalationWatcher';
import { ConsultationWatcher } from './consultationWatcher';
import { ModelChoice, parseAgyModels, parseCodexModels } from './models';
import {
  isPanelCommand,
  PanelState,
  liveRegions,
  OPEN_BY_DEFAULT,
  panelHtml,
  staticKey,
  withholdsRepaint,
  VSCODE_COMMAND_FOR,
  type ChatLedgers,
} from './panelView';
import { parseSession, SessionFile } from './rounds';
import { PriceOfModel, usageTabHtml } from './roundsLog';
import { parseUsage, priceOfLine, UsageEntry, Window } from './usage';
import { stat } from 'node:fs/promises';
import { ChatTurnRecord } from './chatUsage';
import { chatUsagePath, readChatUsage } from './chatUsageFile';
import {
  badEndpoint,
  consultantEndpointWrite,
  consultantRecordUpdate,
  endpointAnswer,
  endpointConflict,
} from './consultantWrite';
import { ChatDoorRecord } from './chatDoors';
import { chatDoorsPath, readChatDoors } from './chatDoorsFile';
import {
  CliStatus,
  latestCliVersion,
  unquoted,
  versionProbeCandidates,
  versionSourceFor,
} from './cliVersions';
import { askVersion, capture } from './versionProbe';
import { PROBE_FILE, parseProbe, writeProbe } from './claudeProbeFile';
import { PROBE_CAP_MS, probeClaudeModels, probeSucceeded, probeToKeep } from './claudeProbe';
import { ProbeResult, stillGood } from './claudeModels';
import { writeFileAtomically } from './atomicFile';
import { seedIfEmpty } from './sideSettings';
import { readerFor, reportRefusal, saveSetting } from './sideConfig';
import { hostPlatform, Platform } from './hostSide';
import { thisSide } from './installer';
import { latestServerVersion, latestTeamServerVersion, serverOnThisSide, serverPath } from './installer';
import { DbLog, EMPTY_LOG } from './roundsDb';
import { NO_NOTES, ProvidersAnswer } from './providers';
import { readProviders } from './providersProbe';
import { Found, FoundRound, keysFileIn, MAX_LIMIT, readBugs, readFindings, readLog, readManyFindings, readPairs, RoundKey, serverRun, uploadRun, writeKeep } from './roundsDbRead';
import { contributorKey, setContributorKey } from './bugsAdminKey';
import { mayStart, outcomeOf } from './bugsSend';
import { BugCorpus, EMPTY_CORPUS } from './roundsDb';
import { usersPanel } from './bugsKeysPanel';
import { BugzReviewPanel } from './bugzReviewPanel';
import { ServerStatus, sideKey, sideLabel } from './coaiInstall';
import { rolesKnowTheServer } from './rolesPanel';
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
  roleRecordUpdate,
  SettingMessage,
  CoaiSettings,
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
import {
  normaliseId,
  pinnedDocument,
  presetsOffered,
  reviewerPickItems,
  Vendor,
  VENDOR_PRESETS,
  vendorsFrom,
} from './vendors';
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
  rowBelongsTo,
} from './teamServers';
import { TeamServerState, slotSentence } from './teamServerView';
import { access } from 'node:fs/promises';
import { join } from 'node:path';
import { asText } from './asText';
import { notify, notifyAndAsk, notifyOnce } from './notify';
import { chosenRoot, coaiDataDir, dataSideName, whereData, type DataLocation } from './dataDir';
import { alsoWatchDataDirectories } from './escalationWatcher';
import { watchedDirs, type WatchedDir } from './escalationDirs';
import { CONSULT_PROMPT_PATH, consultPromptWrite } from './consultPrompt';
import { consultationsHtml } from './roundsLog';
import { CLOSE_CHOICES, refusalIn, SERVER_TOO_OLD } from './consultations';
import { CALLER_KINDS, ConsultSettings, ResolvedConsultant } from './consultSettings';
import { claudeExecutableFor, claudeIsWanted, mayAsk } from './claudeCli';
import {
  executableFor,
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

/** Is this caller's consultant a DEFINITION on that runtime? An unplaceable entry is on none. */
function onRuntime(one: ResolvedConsultant | undefined, runtime: string): boolean {
  return one !== undefined && one.kind === 'definition' && one.runtime === runtime;
}


export class PanelProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'coai.panel';

  /**
   * The live view, held through a handle that knows about disposal.
   *
   * <p>It used to be a bare `vscode.WebviewView?` assigned in `resolveWebviewView` and never
   * cleared, while both sibling panels (`helpPanel`, `roundsLogPanel`) subscribed to
   * `onDidDispose` and nulled theirs. VS Code disposes a view when it is HIDDEN, so on
   * 2026-09-08 a person who opened the rounds log to answer a question the gate had asked them
   * got `Webview is disposed` — the escalation watcher had gone on painting into it every five
   * seconds.</p>
   */
  private readonly held = new ViewHandle<vscode.WebviewView>();


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
  /**
   * Which Claude families this machine can actually reach, as the CLI itself last answered.
   *
   * <p>Undefined is "nobody has asked", which is a different sentence from "none of them" — the
   * dropdown draws the whole curated list either way and only the LABELS differ, because a
   * discovery that could not run must never subtract from what a person could already choose.</p>
   */
  private claudeProbe: ProbeResult | undefined = undefined;
  /** Whether the answer on disk has been read yet. Read once, then this process owns it. */
  private claudeProbeRead = false;
  /** One probe at a time. Four billed requests do not need a second copy racing them. */
  private claudeProbeInFlight = false;
  /** True from the moment a probe starts until it lands, so the sections can say so. */
  private askingClaude = false;
  /**
   * Which Claude binary the last refresh was started FOR, or empty for none yet.
   *
   * <p>The trigger, and the reason it is the executable rather than a boolean: a render must not
   * start a refresh that has already been started for the same CLI, and repointing that CLI
   * must start one. A boolean would have answered the first and lost the second.</p>
   */
  private claudeAskedFor = '';
  /** The version the last refresh read, so a cached answer from another binary is not shown. */
  private claudeCliVersion = '';
  /**
   * When the last probe FAILED, or 0 when none has.
   *
   * <p>The trigger above is an edge, which is what stopped a render from spawning a process on
   * every paint — but an edge alone has no way back: a probe that timed out or met a spent
   * allowance left the panel saying "not asked yet" until the editor restarted. A failure is
   * dated so the edge can be crossed again once the backoff has passed.</p>
   */
  private claudeProbeFailedAt = 0;
  /** Engines probed for CONSULTANT endpoints, keyed by the endpoint the row stores. */
  private consultEngines: Record<string, LocalEngine> = {};
  private consultEngineAt: Record<string, number> = {};
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
    contract?: number | undefined;
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
  /**
   * When a control in the page gained focus, and which one. 0 means none has it.
   *
   * <p>What `withholdsRepaint` reads. The page reports both edges, and a transition between two
   * controls reports neither — the focusout of the one being left arrives before the focusin of the
   * one being entered.</p>
   */
  /** The discovery payload last written, so an unchanged one is not written again. */
  private discoveryWritten = '';
  /** Whether the failure below has already been said. Once a session, never once a render. */
  private discoveryWarned = false;
  private editingSince = 0;
  private editingId = '';
  private editingCaret: readonly [number, number] = [0, 0];
  /**
   * Every write, in the order the page asked for it.
   *
   * <p>`onDidReceiveMessage` used to fire `void this.write(...)` per message, so two writes raced
   * and a render could read a configuration a write had not finished applying. They queue now, and
   * `render` awaits the queue.</p>
   */
  private queued: Promise<void> = Promise.resolve();

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly watcher: EscalationWatcher,
    private readonly dataDir: vscode.Uri,
    private readonly answer: (id: string) => Promise<void>,
    /**
     * What is being consulted right now, or nothing.
     *
     * <p>Optional and defaulted, so every test that builds a provider is unchanged and a build with
     * no watcher paints an empty region rather than crashing. It is a WATCHER rather than a list
     * because the files change without anybody touching the panel — the same reason the escalation
     * watcher is one.</p>
     */
    private readonly consultations?: ConsultationWatcher,
  ) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    this.held.hold(view);
    // A new document, so nothing in it is focused yet — whatever the last one left behind.
    this.forgetEditing();
    // Only ever clears the handle when THIS view is still the one held: VS Code re-creates a hidden
    // view when it is shown again, so a late callback from a replaced view would otherwise blank
    // the live one and stop the sidebar updating until something else resolved it.
    view.onDidDispose(() => {
      this.held.release(view);
      this.forgetEditing();
    });
    view.webview.options = { enableScripts: true };
    view.webview.onDidReceiveMessage(
      (m: { type: string; key?: string; value?: unknown; vendor?: string; command?: string; id?: string; open?: boolean; role?: string; caller?: string; round?: number; editing?: boolean; start?: number; end?: number }) => {
        if (m.type === 'section' && m.id !== undefined) {
          this.openSections = m.open === true
            ? [...new Set([...this.openSections, m.id])]
            : this.openSections.filter((s) => s !== m.id);
        } else if (m.type === 'prompt' && m.role !== undefined && m.round !== undefined) {
          void this.choosePrompt(m.role, m.round, String(m.value));
        } else if (m.type === 'setting') {
          this.enqueue(() => this.write({ key: m.key, value: m.value, vendor: m.vendor, role: m.role, caller: m.caller }));
        } else if (m.type === 'focus') {
          this.editing(m.editing === true, m.id ?? '', Number(m.start), Number(m.end));
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
        // The row's typed rate, or the list price of the model that ANSWERED when the row is gone.
        // The rule and the reason are in `priceOfLine`; it is pure and tested there rather than
        // written out here, where nothing could reach it. (codex, the second code round.)
        const rate = priceOfLine(provider, model, vendors, (id) => published(id, open, lite));
        seen.set(key, rate === undefined ? undefined : { inPerMillion: rate.in, outPerMillion: rate.out });
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
  /**
   * Forget the cached log, because something it describes has just moved.
   *
   * <p>The cache exists so a page refreshing every tick does not spawn a process every tick. That is
   * right for a page ticking on its own and wrong for a real change: a consultation ending is the
   * last watcher event there will be, so a cache entry written a second earlier would have kept the
   * old rows on screen until somebody clicked something. The watcher clears it and asks again.
   * (CodeRabbit, on the pull request.)</p>
   */
  forgetRoundsLog(): void {
    this.roundsLogAt = 0;
  }

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
    this.roundsLogCache = server === undefined
      ? EMPTY_LOG
      // The whole window rather than one page: this list is what gives every row its decision
      // counts, and the page paginates the rows it already holds. See MAX_LIMIT for the arithmetic.
      : await readLog(server.fsPath, { limit: MAX_LIMIT });

    return this.roundsLogCache;
  }

  /**
   * What ONE round found, read when somebody opens its row.
   *
   * <p>Not cached, and not on the tick: this is a read a person asked for by clicking, and it is
   * small — one round's findings against the 3.78 MB the list used to carry for every round whether
   * or not anybody looked. A machine with no server installed answers `failed`, which the row draws
   * as a retry rather than as "this round found nothing".</p>
   */
  async roundFindings(sessionId: string, stage: string, number: number): Promise<Found> {
    const server = serverPath(this.context.globalStorageUri);

    return server === undefined
      ? { state: 'failed', findings: [] }
      : readFindings(server.fsPath, { sessionId, stage, number });
  }

  /**
   * The findings of a whole SELECTION, in one spawn.
   *
   * <p>What a bulk export asks. The per-round call above is what a person opening one row asks, and
   * the two stay separate because they answer different questions: one row wants the answer now, a
   * selection wants five hundred answers without five hundred processes.</p>
   */
  async roundFindingsMany(keys: readonly RoundKey[], stop?: () => boolean): Promise<readonly FoundRound[]> {
    const server = serverPath(this.context.globalStorageUri);
    if (server === undefined) {
      return keys.map((key) => ({ key, found: { state: 'failed' as const, findings: [] } }));
    }

    // `stop` reaches the CHILD, which is the whole point: one process answers for the whole
    // selection, so a cancel that only stopped listening would leave it reading the database while
    // the person who cancelled watched nothing happen. It is handed to the per-round fallback too.
    return readManyFindings(
      server.fsPath,
      keys,
      keysFileIn(this.context.globalStorageUri.fsPath),
      // Through `serverRun`, not `capture` directly: that is the door that carries the data
      // directory this window chose, and a batch read that skipped it would export the rounds of a
      // different directory from the one the list beside it is showing.
      serverRun(server.fsPath, stop),
      readFindings,
      stop);
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

  /**
   * The chat ledger — what conversations cost, as opposed to review rounds.
   *
   * <p>A second file and a second read, because the two ledgers have two different writers: the
   * server appends to `usage.jsonl` while it runs a round, and the extension appends to
   * `chat-usage.jsonl` when a turn ends. They are merged into one table on the page rather than into
   * one file on disk, so neither half has to know the other's format or ship on the other's day.</p>
   *
   * <p>Whole, like {@link usageLines} and for the same reason: forgetting a vendor's spending is a
   * decision about the spending VIEW, not a reason to empty a row that really did cost money.</p>
   */
  /**
   * The parsed ledger, and what the file looked like when it was parsed.
   *
   * <p>An empty stamp is "not known", which never matches and therefore never serves a stale answer.
   * The records are held rather than the text: parsing is the expensive half.</p>
   */
  private chatLedger: { stamp: string; records: readonly ChatTurnRecord[] } = { stamp: '', records: [] };

  /** The same, for the ledger of INVOCATIONS - how often the chat was reached for. */
  private doorLedger: { stamp: string; records: readonly ChatDoorRecord[] } = { stamp: '', records: [] };

  /**
   * Every door ever opened, on the same stamped cache as the turns.
   *
   * <p>Its own file, and the reason is measured against the other one's parser: `parseChatUsageLine`
   * requires a non-empty `utc` and nothing else, so a door line living in `chat-usage.jsonl` reads as
   * a turn that cost nothing to every caller that already reads it - a phantom conversation row in
   * the table on this very page. (codex and gemini, the plan round, independently.)</p>
   */
  async doorLines(): Promise<readonly ChatDoorRecord[]> {
    const stamp = await this.ledgerStamp(chatDoorsPath(this.dataDir.fsPath));
    if (stamp !== '' && stamp === this.doorLedger.stamp) {
      return this.doorLedger.records;
    }
    const records = await readChatDoors(this.dataDir.fsPath);
    this.doorLedger = { stamp, records };

    return records;
  }

  async chatLines(): Promise<readonly ChatTurnRecord[]> {
    const path = chatUsagePath(this.dataDir.fsPath);
    // The log page ticks every five seconds while it is open, and it used to re-read and re-parse the
    // WHOLE ledger on each one — thousands of allocations on the extension host's main thread for a
    // file that had not changed. `stat` is one syscall; a parse of a year's records is not. The size
    // AND the mtime together, because an append-only file always grows but a file replaced by hand
    // may not. (gemini and codex, the code round, independently.)
    const stamp = await this.ledgerStamp(path);
    if (stamp !== '' && stamp === this.chatLedger.stamp) {
      return this.chatLedger.records;
    }
    const records = await readChatUsage(this.dataDir.fsPath);
    this.chatLedger = { stamp, records };

    return records;
  }

  /** The file's identity as a string, or empty when it cannot be stat-ed — which never caches. */
  private async ledgerStamp(path: string): Promise<string> {
    try {
      const found = await stat(path);

      return `${found.size}:${found.mtimeMs}`;
    } catch {
      return '';
    }
  }

  /** The spending window the page shows. Today by default — since midnight, by the operator's ruling. */
  setUsageWindow(window: string): void {
    if ((['day', 'week', 'month', 'year'] as readonly string[]).includes(window)) {
      this.usageWindow = window as Window;
    }
  }

  /**
   * The consultations tab of the rounds log page, priced.
   *
   * <p>Beside {@link usageTab} and for the same reason: the rates live HERE — a person's typed
   * per-vendor price and the published lists this panel fetches — so the page asks for the rendered
   * tab rather than being handed a table and a bag of inputs. The first version of this change let
   * the page build the table itself with no rates at all, and the column went on showing the dash
   * the change exists to remove while every unit test passed. (issue #309.)</p>
   */
  async consultationsTab(log: DbLog): Promise<string> {
    const vendors = this.vendorsHere();
    const prices = await this.modelPrices(vendors);

    return consultationsHtml(log, vendors, (model: string) => prices[model]);
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
      // THE SECOND LEDGER, which this page has never counted: what the chat cost, and how often it
      // was reached for. Read on the same stamped caches as everything else here.
      //
      // `vendorOf` turns the id a chat recorded - the model PRESET in force - into the vendor whose
      // row it belongs in, so both halves of the page name the same three vendors the same way.
      // Resolved on the way OUT rather than on the way in, which names every line already on disk.
      await this.chatLedgers(),
    );
  }

  /** The last answer from `--providers`, when it was taken, and which binary gave it. */
  private providersCache: ProvidersAnswer = { reported: {}, asked: false, answered: false, notes: NO_NOTES };

  private providersAt = 0;

  /**
   * Write down what the server said about ITSELF, which nothing has ever read.
   *
   * <p>`--providers` has always answered `unrecognised` — the settings it could not understand,
   * each already a sentence meant to be acted on — and a `vaultNote`. The panel's parser took
   * `{provider, auth, note}` off each row and dropped the rest, so a malformed `COAI_ROLES` or a
   * role row the server refused was visible only in a log file nobody opens. Three lines of parser
   * and this; the notifications plan calls it the cheapest honest improvement in the inventory.</p>
   *
   * <p>It would NOT have caught the 2026-09-16 incident — that role was accepted rather than
   * dropped, and its complaint came at round time — and saying so is the point: this closes a real
   * gap, not the one that prompted the work.</p>
   */
  private sayWhatTheServerSaid(answer: ProvidersAnswer): void {
    const said = answer.notes.unrecognised;
    if (said.length === 0) {
      return;
    }
    // ONE notice for the whole answer, not one per sentence. The first version looped, and the code
    // round was right about where that ends: `unrecognised` is free text the SERVER composes, with
    // no bound on how many entries a malformed settings file produces, so each sentence being its
    // own subject meant each was its own key — ten thousand toasts and ten thousand serialised
    // appends, on a chain a later record then waits behind. An unbounded input reaching an unbounded
    // number of notifications is the shape this whole feature exists to bound. (codex.)
    //
    // The subject is the whole set joined, so a DIFFERENT complaint is still news — which was the
    // reason the loop keyed on the sentence — while the same set arriving on every ten-second probe
    // stays one key. `notifyOnce` shows it once per run and records every arrival.
    void notifyOnce({
      as: 'warning',
      class: 'failure',
      source: 'coai-mcp',
      code: 'server-did-not-understand-a-setting',
      subject: said.join(' · '),
      title: said.length === 1
        ? (said[0] ?? '')
        : `The server could not understand ${said.length} of its settings, starting with: ${said[0] ?? ''}`,
      // Every sentence, on the record, bounded by DETAIL_LIMIT at the serialiser rather than by a
      // number chosen here — so the ledger keeps what the toast has no room for.
      detail: said.join('\n'),
      cure: 'The server is running without those settings. Fix them in the panel and reload the window.',
    });
  }

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

  /**
   * What the Claude CLI last answered about the families it reaches — the last answer, never a wait.
   *
   * <p>Shaped exactly like {@link providerHealth} above, and for the same reason: this costs four
   * billed requests and tens of seconds, so a render starts it and never awaits it. The freshness
   * check is what stops the loop — the repaint this triggers finds the answer good and starts
   * nothing.</p>
   *
   * <p>Only when something on this machine would actually USE a Claude model. Probing for a runtime
   * nobody has configured would be this extension spending a person's allowance on a dropdown they
   * are never going to open.</p>
   */
  private claudeProbeAnswer(vendors: readonly Vendor[], consult: ConsultSettings): ProbeResult | undefined {
    const wanted = claudeIsWanted(vendors, consult);
    // EDGE-TRIGGERED. It used to start a refresh on every render, and the refresh repainted from a
    // `finally` whatever it had found — so a fresh cache still caused a repaint, which started
    // another refresh, which spawned another `--version`, for ever. A render now asks at most once
    // per SETTLING: the flag is cleared only when a probe genuinely could not be judged fresh, and
    // the executable is part of the key so repointing the CLI is still noticed. (Blocking, codex.)
    const executable = claudeExecutableFor(vendors, consult);
    if (wanted && mayAsk(this.claudeAskedFor, executable, Date.now(), this.claudeProbeFailedAt)) {
      this.claudeAskedFor = executable;
      this.claudeProbeFailedAt = 0;
      this.refreshClaudeProbe(vendors, consult).then(undefined, (error: unknown) => {
        // A catch-all at the detached edge, per the try/catch rule: this promise is deliberately
        // not awaited, so without one a failure here is an unhandled rejection and nothing else.
        console.error('ConnectOtherAIs: the Claude model probe failed', error);
      });
    }

    return this.claudeProbeToShow();
  }

  /**
   * The answer a dropdown may draw on — which is not always the one on disk.
   *
   * <p>An answer whose CLI version no longer matches the binary this machine runs is EVIDENCE ABOUT
   * ANOTHER BINARY. Keeping it in memory is right (it is what a failed probe falls back to), but
   * presenting it as confirmed would label a family verified from a record a different CLI wrote —
   * and a person choosing that alias can then silently get the default. It is withheld until a probe
   * confirms it against the version actually installed. (codex SecurityReliability, this round.)</p>
   */
  private claudeProbeToShow(): ProbeResult | undefined {
    const probe = this.claudeProbe;
    if (probe === undefined || probe.cliVersion !== this.claudeCliVersion) {
      return undefined;
    }

    // And the BINARY, not only the version. A reviewer row and a consultant can point at two
    // different installations of one version, signed into two different accounts — so a record that
    // names another executable is evidence about another account's models. A record written before
    // that field existed names none, and is trusted, because nothing else about it says otherwise.
    return (probe.executable ?? '') === '' || probe.executable === this.claudeAskedFor ? probe : undefined;
  }

  /**
   * One probe at a time, a repaint when it lands, and a *looking* state for as long as it runs.
   *
   * <p>The freshness question is asked against the CLI's OWN version, which is itself a process
   * spawn — so it is asked here rather than in the render, and a machine with no Claude CLI answers
   * an empty version and is never probed at all.</p>
   */
  private async refreshClaudeProbe(vendors: readonly Vendor[], consult: ConsultSettings): Promise<void> {
    if (this.claudeProbeInFlight) {
      return;
    }
    this.claudeProbeInFlight = true;
    // Whether anything CHANGED, so a no-op run repaints nothing. A repaint that changed nothing was
    // what closed the loop above into an endless one.
    let moved = false;
    // Both read in the `finally`, which is the only place that sees a THROW as well as a return.
    // Dating the failure inside the try meant an exception left nothing dated, so the edge trigger
    // never asked again for the rest of the session — the very hole the dating exists to close.
    let cliVersion = '';
    let succeeded = false;
    try {
      if (!this.claudeProbeRead) {
        this.claudeProbeRead = true;
        this.claudeProbe = await this.readClaudeProbe();
        moved = this.claudeProbe !== undefined;
      }
      const executable = claudeExecutableFor(vendors, consult);
      cliVersion = await askVersion(executable);
      // A CLI that will not say its version is not a transient failure — it is not installed at
      // this path — so it is NOT dated for a retry. Re-asking a binary that is not there every ten
      // minutes would be a process spawn a person never asked for, for ever.
      moved = moved || cliVersion !== this.claudeCliVersion;
      this.claudeCliVersion = cliVersion;
      if (cliVersion.length === 0 || stillGood(this.claudeProbe, cliVersion, Date.now())) {
        // Nothing to do, which is not a failure: an absent CLI must not be re-asked every ten
        // minutes for ever, and a fresh answer is the answer.
        succeeded = true;

        return;
      }
      moved = true;

      // SAID before it is started. The alternative is tens of seconds of a dropdown that looks
      // finished, which is exactly how a person chooses from a list that was about to change.
      this.askingClaude = true;
      await this.render();
      const found = await probeClaudeModels(
        {
          run: (args) => capture(unquoted(executable), args, false, PROBE_CAP_MS, () => this.held.view === undefined),
          cliVersion: async () => cliVersion,
          executable,
          now: () => Date.now(),
        },
        undefined,
        // Asked before each candidate. Four of them is up to a hundred seconds of BILLED requests,
        // and a window that has gone will not read the answer. (gemini, this round.)
        () => this.held.view === undefined,
      );
      // A run that learned nothing keeps the previous answer: an account whose allowance is spent
      // is a state this installation is really in, and it must not empty anybody's dropdown.
      this.claudeProbe = probeToKeep(found, this.claudeProbe);
      succeeded = probeSucceeded(found);
      if (this.claudeProbe !== undefined) {
        await this.keepClaudeProbe(this.claudeProbe);
      }
    } finally {
      this.claudeProbeInFlight = false;
      // A run that did not succeed is dated so it can be tried again shortly — including one that
      // THREW, which is what a `finally` sees and the try did not. A CLI that could not even say its
      // version is left alone: it is not installed at that path, and re-asking a binary that is not
      // there every ten minutes is a process spawn nobody asked for.
      if (!succeeded && cliVersion.length > 0) {
        this.claudeProbeFailedAt = Date.now();
      }
      const wasLooking = this.askingClaude;
      this.askingClaude = false;
      // Whatever happened, the panel stops saying it is looking. Cleared here rather than at the
      // end, because a throw would otherwise leave that sentence on screen for good — and repainted
      // ONLY when something a person can see actually changed, because an unconditional repaint
      // here is what made every render start another probe.
      if (moved || wasLooking) {
        await this.render();
      }
    }
  }

  /** The answer this machine kept, or nothing at all — an unreadable file is not an answer. */
  private async readClaudeProbe(): Promise<ProbeResult | undefined> {
    try {
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(this.dataDir, PROBE_FILE));

      return parseProbe(new TextDecoder().decode(bytes));
    } catch {
      return undefined; // never asked here, or the data directory has moved
    }
  }

  /**
   * Keep it for the week.
   *
   * <p>Atomically, like every other writer here: a window killed mid-write would otherwise leave a
   * truncated file that the next launch reads as no answer, which costs four requests in silence.
   * A disk that refuses is not worth a message — the answer is still live in this process, and the
   * only cost is asking again next week.</p>
   */
  private async keepClaudeProbe(probe: ProbeResult): Promise<void> {
    try {
      await vscode.workspace.fs.createDirectory(this.dataDir);
      await writeFileAtomically(vscode.Uri.joinPath(this.dataDir, PROBE_FILE).fsPath, writeProbe(probe));
    } catch (error) {
      // The list on screen is correct either way — this costs a re-probe next week, not an answer.
      // But a persistence failure that says nothing is one nobody can act on, and this product's
      // own store already names the path it could not write. (codex Conventions, this round.)
      console.error(
        `ConnectOtherAIs: the Claude model probe could not be kept at ${vscode.Uri.joinPath(this.dataDir, PROBE_FILE).fsPath}`,
        error,
      );
    }
  }

  /**
   * The engines behind the CONSULTANT rows, keyed by the endpoint each row stores.
   *
   * <p>Not `localEngines` above, and the key is the difference: that map is per reviewer ROW, and
   * since story C5 the consultant section holds no reviewer rows at all. Two callers pointing at one
   * engine share its answer; one pointing elsewhere gets its own, which is the defect the reviewer
   * map was itself split to fix one surface over.</p>
   *
   * <p>No in-flight endpoint check is needed here, unlike the reviewer path: the key IS the
   * endpoint, so an answer for one a person has since retyped is simply never looked up.</p>
   */
  private consultEnginesAnswer(consult: ConsultSettings): Record<string, LocalEngine> {
    if (!this.consultEnginesInFlight) {
      this.consultEnginesInFlight = true;
      this.probeConsultEngines(consult).then(
        (probed) => {
          const changed = JSON.stringify(probed) !== JSON.stringify(this.consultEnginesShown);
          this.consultEnginesShown = probed;
          this.consultEnginesInFlight = false;

          return changed ? this.render() : undefined;
        },
        (error: unknown) => {
          this.consultEnginesInFlight = false;
          console.error('ConnectOtherAIs: a consultant endpoint could not be probed', error);
        },
      );
    }

    return this.consultEnginesShown;
  }

  /** What the last consultant probe found, so a render draws it without waiting for the next one. */
  private consultEnginesShown: Record<string, LocalEngine> = {};
  private consultEnginesInFlight = false;

  private async probeConsultEngines(consult: ConsultSettings): Promise<Record<string, LocalEngine>> {
    const A_MINUTE = 60 * 1000;
    const wanted = [...new Set(
      CALLER_KINDS
        .map(({ id }) => consult.byCaller[id])
        .filter((one) => onRuntime(one, 'local'))
        .map((one) => (one as Extract<ResolvedConsultant, { kind: 'definition' }>).baseUrl),
    )];
    // ITS OWN memo, not the reviewer pass's. This pass is detached — a render starts it and does
    // not await it — so clearing one shared field at its start and end reaches into a reviewer pass
    // sitting between two rows, and the next `windowsSideOnce()` there launches the interop again.
    // (CodeRabbit, PR #335.)
    let windowsSide: Promise<string> | undefined = undefined;
    const windowsSideHere = (): Promise<string> => (windowsSide ??= windowsSideEngine());
    for (const endpoint of wanted) {
      const cached = this.consultEngines[endpoint];
      // An answer that found NOTHING is not kept, exactly as the reviewer probe does not keep one:
      // somebody who starts Ollama after opening the panel must not wait out a TTL to be believed.
      const fresh = Date.now() - (this.consultEngineAt[endpoint] ?? 0) < A_MINUTE && (cached?.reachable ?? false);
      if (!fresh) {
        this.consultEngineAt[endpoint] = Date.now();
        // eslint-disable-next-line no-await-in-loop -- one engine at a time, like the reviewer pass.
        this.consultEngines[endpoint] = endpoint.length > 0
          ? await probeEngine(openAiBaseOf(endpoint))
          : await discoverEngine(undefined, undefined, windowsSideHere);
      }
    }

    return Object.fromEntries(wanted.flatMap((e) => {
      const engine = this.consultEngines[e];

      return engine === undefined ? [] : [[e, engine] as const];
    }));
  }

  /** One probe at a time, and a repaint when it lands rather than a wait while it runs. */
  private async refreshProviders(executable: string): Promise<void> {
    if (this.providersInFlight) {
      return;
    }
    this.providersInFlight = true;
    try {
      const answer: ProvidersAnswer = executable.length === 0
        ? { reported: {}, asked: false, answered: false, notes: NO_NOTES }
        : await readProviders(executable);
      this.sayWhatTheServerSaid(answer);
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
    if (this.held.view === undefined) {
      return;
    }
    // Never render from a configuration this panel has not finished writing. Blur fires `change`
    // and then `focusout`, and the render the release asks for would otherwise overtake the write
    // it followed and re-stamp the box with the value being replaced — the symptom, reintroduced by
    // the fix. `queued` never stays rejected; see `enqueue`.
    //
    // Awaited until it is STABLE, because a write appended while this render was suspended would
    // otherwise be read a moment too late. Bounded: under continuous typing the queue never settles,
    // and a render that waits for silence is a render that never happens.
    for (let round = 0; round < 5; round += 1) {
      const seen = this.queued;
      // eslint-disable-next-line no-await-in-loop -- the point is to wait for each one in turn.
      await seen;
      if (seen === this.queued) {
        break;
      }
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
      // Never awaited. The probe is four real requests to a real CLI; a render that waited for one
      // would be a panel that hangs for half a minute the first time it is opened on a new machine.
      claudeProbe: this.claudeProbeAnswer(vendors, settings.consult),
      askingClaude: this.askingClaude,
      server: this.told(await serverOnThisSide(this.context.globalStorageUri, this.context.globalState, published)),
      side: sideLabel(vscode.env.remoteName, process.env['WSL_DISTRO_NAME']),
      perSide: this.perSide(config),
      questions: this.watcher.openQuestions,
      openSections: this.openSections,
      sessions,
      usage: this.remembered(await this.readUsage()),
      usageWindow: this.usageWindow,
      latestServerVersion: published,
      latestTeamServerVersion: this.latestTeamServer,
      storage: await whereThisWindowKeepsItsData(),
      cliStatus: await this.vendorCliStatus(vendors),
      modelPrices: await this.modelPrices(vendors),
      snippetStatus: await pastedSnippetStatus(),
      consultPrompt: await this.readConsultPrompt(),
      consultations: this.consultations?.running ?? [],
      localEngines: await this.probeLocalEngines(vendors),
      // The consultant's own engines, keyed by ENDPOINT: since story C5 the section holds no
      // reviewer rows, so there is none to borrow one from, and a row whose endpoint nobody probed
      // had its saved model labelled gone by something that had never looked.
      //
      // NOT awaited, unlike the reviewer rows' probe beside it. Ten consultant endpoints that refuse
      // are ten sequential connection waits, and a refused endpoint is deliberately re-asked rather
      // than cached — so awaiting this would put that whole cost on every repaint. It repaints when
      // it lands. (codex and gemini UxDxPerformance, this round.)
      enginesByEndpoint: this.consultEnginesAnswer(settings.consult),
      // The corpus and the last run, cached for a few seconds: this is a process spawn and a
      // repaint is frequent. It is what puts a running collect on the screen without a poller.
      bugz: await this.bugz(),
      teamServers: this.teamServerStates(config),
      providers: this.providerHealth(),
      usageScope: this.usageScope,
      // Straight from the configuration, exactly as the COMMAND reads it — not through the
      // per-side reader beside it. These four are person-level by design (`chatSettings.ts` says
      // so, and none of them is in `OVERLAID_SETTINGS`), so routing them through an overlay that
      // will never hold them would only invite somebody to add them to it one day and split the
      // one reader in two.
      chat: chatSettingsFrom((key: string) => config.get(key)),
      // Straight from the configuration for the same reason, and read HERE rather than inside the
      // section so that `staticKey` can see it change.
      phrases: this.phrases(),
      // Only while something IS focused — and by the time a paint gets past the hold below, that
      // means the hold ran out under it. An ordinary paint carries nothing and steals nobody's
      // focus.
      focus: this.editingSince > 0
        ? { id: this.editingId, start: this.editingCaret[0], end: this.editingCaret[1] }
        : undefined,
    };

    // What this panel DISCOVERED, left where the chat command can read it. Three of the four model
    // sources are fetched — the codex and agy CLIs' own lists and a Team server's allowlist — and
    // every fetch happens here. The command cannot repeat them to open a tab, so without this it
    // builds its catalog empty and a model chosen in the section above resolves to nothing, opening
    // the conversation on the row's own model instead. Written on every render, so it is as fresh as
    // the panel is.
    await this.rememberDiscovery(state);

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
    // Re-read AFTER the awaits above, and never through `this.held.view` again below. The check at the
    // top of this method proves nothing by the time we get here: every `await` is a place the event
    // loop can run `onDidDispose`, and `this.held.view` then becomes undefined — so `this.held.view.webview`
    // throws a TypeError, which is NOT the disposal error and would be rethrown. Raised on the code
    // round, by two reviewers, on both write paths.
    const live = this.held.view;
    if (live === undefined) {
      return;
    }

    const key = staticKey(state);
    // A third answer, between the two: WITHHOLD. Assigning the html rebuilds the document, and a
    // document rebuilt under a focused control takes the caret and anything typed since the last
    // write with it — which is how `поясни` was lost, five times a minute, to probes and version
    // checks nobody asked for. The key is deliberately NOT recorded while a paint is withheld, so
    // the next render after focus leaves paints what this one could not.
    if (key !== this.paintedKey && !withholdsRepaint(this.editingSince, Date.now())) {
      // Guarded as well as null-checked, because disposal can still land between the line above and
      // this one — and recorded only AFTER the paint succeeded. Setting it first was a defect of
      // its own: a disposal here left `paintedKey` claiming this state was painted, so the view VS
      // Code creates when the sidebar is shown again matched the key, skipped the html write
      // entirely, and posted live regions into an empty document — a blank sidebar with no controls
      // and no way back. Found by gemini on the code round, twice.
      try {
        live.webview.html = panelHtml(state, this.nonce);
      } catch (error) {
        if (!isDisposedRejection(error)) {
          throw error;
        }

        return;
      }
      this.paintedKey = key;
      void this.refreshTeamServers();

      return;
    }

    // Never awaited: the section draws from what is already known, and this repaints when it
    // lands. A render that waited on a Team server would be a panel that hangs when one is slow.
    void this.refreshTeamServers();

    // The disposal is expected and dropped; anything else keeps its reporter, because a LIVE view
    // refusing a message — a payload that cannot be cloned, a host channel that fell over — leaves
    // a stale sidebar and has nothing else to say so.
    live.webview.postMessage({ type: 'live', ...liveRegions(state) }).then(undefined, (error: unknown) => {
      if (!isDisposedRejection(error)) {
        console.error('ConnectOtherAIs: the panel could not be updated', error);
      }
    });
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
    // THE MODELS OF BOTH HALVES OF THE PAGE. A reviewer row selects one; a chat is switched between
    // model PRESETS, which select their own - and this map used to hold only the first kind, so a
    // chat card read "no rate set for this model" for a model the published table prices perfectly
    // well. The two lists are asked the same question and answered from the same table.
    // (CodeRabbit, PR #209.)
    const presets = chatModelPresetsFrom(vscode.workspace.getConfiguration('coai').get('chatModelPresets'));
    const wanted = [...vendors.map((one) => one.model), ...presets.map((one) => one.model)];
    for (const model of wanted) {
      if (model.length === 0) {
        continue; // "the CLI's default" — we do not know which model that is, so we do not guess
      }
      const price = priceFor(model, this.openRouterPrices, this.liteLlmPrices);
      if (price !== undefined) {
        prices[model] = price;
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
    const answer = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'spending',
      code: 'forget-vendor-spending',
      subject: provider,
      modal: true,
      title: `Clear ${provider}'s recorded runs from the spending chart?`,
      detail: 'The chart stops counting what this vendor has recorded so far. Nothing is deleted from '
        + 'the ledger on disk, and the row comes back the next time this vendor runs.',
      action: forget,
    });
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
   * Clear one CHAT row from the spending chart, after asking.
   *
   * <p>The reviewers' watermark, keyed differently because the row is: a chat row is a vendor AND a
   * model (`ChatSpendRow`), so forgetting <em>codex · gpt-5.4</em> must leave <em>codex · gpt-5.5</em>
   * alone — they are two rows on the page and a person pressing ✕ is pointing at one of them.</p>
   *
   * <p>A SECOND map beside `coai.usageForgottenBefore`, never a change to it: the two halves of the
   * page are two ledgers, and nothing about one makes a state of the other wrong.</p>
   */
  async forgetChatUsage(provider: string, model: string): Promise<void> {
    const named = model.length > 0 ? `${provider} · ${model}` : provider;
    const forget = 'Forget';
    const answer = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'spending',
      code: 'forget-chat-spending',
      subject: named,
      modal: true,
      title: `Clear ${named}'s recorded chat from the spending chart?`,
      detail: 'The chart stops counting what this model has recorded so far. Nothing is deleted from '
        + 'the ledger on disk, and the row comes back the next time this model answers.',
      action: forget,
    });
    if (answer !== forget) {
      return;
    }

    const marks = { ...this.chatForgottenBefore(), [chatForgetKey(provider, model)]: new Date().toISOString() };
    await this.context.globalState.update('coai.chatUsageForgottenBefore', marks);
    await this.render();
  }

  /** Per vendor-and-model pair, the instant before which its recorded chat is not counted. */
  private chatForgottenBefore(): Record<string, string> {
    return this.context.globalState.get<Record<string, string>>('coai.chatUsageForgottenBefore') ?? {};
  }

  /**
   * Both chat ledgers, minus what has been forgotten.
   *
   * <p>The filter is applied HERE, on the way out, for the reason `remembered()` gives about the
   * reviewers' one: the writer appends while this panel is open, so filtering a file and writing it
   * back would race a turn finishing mid-write. The caches below hold what the FILE said; what is
   * forgotten is a question about the marks, and it is asked on every render — so a mark written a
   * moment ago takes effect on the next paint whether or not the lines came from a cache.</p>
   */
  private async chatLedgers(): Promise<ChatLedgers> {
    const marks = this.chatForgottenBefore();
    const vendorOf = vendorOfPreset(chatModelPresetsFrom(
      vscode.workspace.getConfiguration('coai').get('chatModelPresets'),
    ));

    return {
      turns: rememberedChat(await this.chatLines(), marks, vendorOf),
      doors: rememberedChat(await this.doorLines(), marks, vendorOf),
      vendorOf,
    };
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
    const source = versionSourceFor(vendor.runtime, hostPlatform(), process.arch === 'arm64' ? 'arm64' : 'x64');

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
    for (const candidate of versionProbeCandidates(executable, hostPlatform())) {
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
      void notify({
        as: 'information',
        class: 'outcome',
        source: 'vendorTerminal',
        code: 'vendor-terminal-note',
        subject: vendor.id,
        title: note,
      });
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

    // The extension HOST's platform, which is the one that matters here: the terminal this opens
    // runs on this side, so a WSL window wants the linux instructions. `hostSide.ts` is where that
    // doctrine and its narrowing now live, in one place — this used to be one of three copies.
    const install = commandFor(vendor, hostPlatform());
    if (install.command.length === 0) {
      const open = 'Open the instructions';
      const choice = await notifyAndAsk({
        as: 'information',
        class: 'offer',
        source: 'vendorInstall',
        code: 'no-install-command-for-this-vendor',
        subject: vendor.id,
        title: install.note,
        action: open,
      });
      if (choice === open) {
        await vscode.env.openExternal(vscode.Uri.parse(install.docs));
      }
      return;
    }

    const terminal = vscode.window.createTerminal({ name: `coai · ${verb} ${vendor.id}` });
    terminal.show();
    if (install.note.length > 0) {
      void notify({
        as: 'information',
        class: 'outcome',
        source: 'vendorInstall',
        code: 'vendor-install-note',
        subject: vendor.id,
        title: install.note,
      });
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
  /**
   * Put one write on the queue, and keep the queue usable whatever it does.
   *
   * <p>The work runs on both settle paths of the promise before it, and its own rejection is
   * swallowed here — a chain left rejected would never write another setting and would take
   * `render`'s `await` down with it, so a single failed update would freeze the panel for the life
   * of the window. A failure has already been reported to the person by {@link save}, which is the
   * only place that knows what could not be written.</p>
   */
  private enqueue(work: () => Promise<void>): void {
    this.queued = this.queued.then(work, work).catch(() => undefined);
  }

  /**
   * The page reporting that a control gained or lost focus.
   *
   * <p>Losing it renders at once: a paint withheld while the person was typing has been waiting for
   * exactly this, and `render` awaits the write queue first, so the paint it produces carries what
   * was typed rather than what it replaced.</p>
   */
  /**
   * What this panel DISCOVERED, left where the chat command can read it.
   *
   * <p>Three of the four model sources are fetched — the `codex` and `agy` CLIs' own lists and a
   * Team server's allowlist — and every fetch happens here. The command cannot repeat them to open a
   * tab, so without this it builds its catalog empty and a model chosen in the chat section resolves
   * to nothing, opening the conversation on the row's own model instead.</p>
   *
   * <p><b>Written only when it CHANGED.</b> A render happens for reasons that have nothing to do
   * with discovery — a usage tick, a question arriving, any setting written in any window — and
   * awaiting a serialisation of every discovered model and every Team catalog before each repaint is
   * disk churn in front of the paint. Compared as text, which is what is stored anyway. (gemini and
   * codex, the code round.)</p>
   *
   * <p>And it never takes the panel down with it: a store that cannot be written is a chat that
   * resolves models the older way, not a sidebar that fails to render.</p>
   */
  private async rememberDiscovery(state: PanelState): Promise<void> {
    const discovered = {
      codex: state.codexModels,
      agy: state.agyModels,
      // The chat resolves its Claude list from this snapshot, so the probe rides in it. Without it
      // the picker offered every curated alias as though each had been confirmed, one surface away
      // from the panel saying which had.
      claude: state.claudeProbe,
      catalogs: Object.fromEntries(
        (state.teamServers ?? []).flatMap((one) =>
          (one.catalog === undefined ? [] : [[one.server.id, { ...one.catalog, url: one.server.url }]])),
      ),
    };
    const written = JSON.stringify(discovered);
    if (written === this.discoveryWritten) {
      return;
    }
    try {
      await this.context.globalState.update(DISCOVERY_KEY, discovered);
      this.discoveryWritten = written;
    } catch (error) {
      // SAID once, not swallowed and not repeated. Nobody can act on the write itself, but the
      // consequence is worth a sentence: the section goes on offering models the chat command will
      // not find, so a conversation can open on a different model from the one on screen. Once per
      // session, because a render happens every few seconds and a warning per render is a warning
      // nobody reads. Retrying continues — the next render tries again. (CodeRabbit, PR #196.)
      console.error('coai: the discovered model lists could not be stored', error);
      if (!this.discoveryWarned) {
        this.discoveryWarned = true;
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'modelDiscovery',
          code: 'discovered-models-not-stored',
          title: 'ConnectOtherAIs could not store the discovered model lists, so a chat may open on the model'
            + ' its reviewer row is set to rather than the one chosen in the panel.',
          detail: asText(error),
        });
      }
    }
  }

  private editing(editing: boolean, id: string, start: number, end: number): void {
    if (editing) {
      // Only the FIRST focus of a session starts the clock. Tabbing from one control to the next
      // must not renew it: a cap a focus change renews is a cap with no bound, which is the same
      // defect as a cap a keystroke renews. The id and the caret are refreshed every time, because
      // the paint that eventually lands must find the control the person is in NOW.
      if (this.editingSince === 0) {
        this.editingSince = Date.now();
      }
      this.editingId = id;
      this.editingCaret = [start, end];

      return;
    }
    this.forgetEditing();
    void this.render();
  }

  /**
   * Nothing is being edited.
   *
   * <p>Also on DISPOSAL, and that is not tidiness: closing the sidebar mid-sentence fires no
   * `focusout`, so a view resolved again within the cap would refuse to paint and would refocus a
   * control from a page that no longer exists.</p>
   */
  private forgetEditing(): void {
    this.editingSince = 0;
    this.editingId = '';
    this.editingCaret = [0, 0];
  }

  private async write(message: SettingMessage): Promise<void> {
    const write = settingWrite(message);
    if (write === undefined) {
      return;
    }

    // The consultant's prompt is a FILE, not a setting — see `saveConsultPrompt`. Intercepted here
    // rather than inside the plain case, because everything below this line is about configuration.
    if (write.kind === 'plain' && write.key === 'consultPrompt') {
      await this.saveConsultPrompt(write.value);
      return;
    }

    const config = vscode.workspace.getConfiguration('coai');
    switch (write.kind) {
      case 'vendor': {
        // `pinnedDocument` FIRST, so the spread below can still override it: changing the document
        // box writes that box, and changing the PLAN box also records what the document switch was
        // silently meaning until now. Otherwise unticking `plan` on a local reviewer would take its
        // document rounds away as an invisible side effect — and ticking it would hand them over.
        // (Ux finding, plan 5's plan round.)
        const vendors = vendorsFrom(this.read(config)('vendors')).map((v) =>
          v.id === write.vendor
            ? { ...v, ...pinnedDocument(v, write.key), [write.key]: write.value }
            : v,
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
      case 'caller': {
        // Merged the same way a role record is, and into `consultants` rather than the control's own
        // key: the four rows are one map, so the key a control carries says which HALF of a row
        // changed — the vendor or its model — and is never a setting of its own. The caps beside
        // them are ordinary settings and take the plain path below.
        // Read through the SIDE-AWARE reader, because `consultants` is one of the overlaid
        // settings: reading the shared value and then saving the merge into the side overlay would
        // drop whatever that side had already chosen. Written and read by the same rule.
        // (CodeRabbit, on the pull request.)
        const current = (this.read(config)('consultants') as Record<string, unknown> | undefined) ?? {};
        // The rows go in because the WRITE resolves too: what is stored stops being a reference to a
        // reviewer row the moment a person edits the section, so the definition being written has to
        // be worked out from the rows this side can see — the same reader, on the same config, as the
        // line above.
        const rows = vendorsFrom(this.read(config)('vendors'));
        await this.save(config, 'consultants', consultantRecordUpdate(current, write.caller, write.key, write.value, rows));
        return;
      }
      case 'plain':
        // A setting that INVALIDATES another is cleared BEFORE it, not after. One case today, and it
        // is the chat pair: `chatModelName` names one of `chatModel`'s models, so choosing a
        // different provider leaves it holding the previous one's — a value the panel would strand
        // in its select while the conversation quietly opened on the row's own model instead. The
        // order matters for the crash in between: cleared first, an extension host killed mid-write
        // leaves a provider with no model, which is a state the pair has a meaning for. Written
        // first, it would leave the NEW provider paired with the OLD provider's model. (codex.)
        for (const stale of clearedByWriting(write.key)) {
          await this.save(config, stale, '');
        }
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

  /**
   * The server status, passed through — and told to the roles tab on the way.
   *
   * <p>That tab draws its own version-skew banner and has no way to ask for the answer: it is a
   * panel of its own, opened by a command, with no reference to this provider. Told here, where the
   * answer is already in hand and every repaint passes through.</p>
   */
  private told(server: ServerStatus): ServerStatus {
    rolesKnowTheServer(server.kind === 'absent' ? '' : server.version);

    return server;
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
   *
   * <p>The rule was right and the ACCESSOR was private, which is not the same thing: the chat and the
   * settings file handed to `coai-mcp` went around it because they could not reach it. The decision
   * now lives in `sideSettings.sideConfigReader`, where all three call it, and this method is the
   * panel's way in rather than the only implementation.</p>
   */
  private read(config: vscode.WorkspaceConfiguration): ConfigReader {
    return readerFor(this.context, config);
  }

  /**
   * The configured reviewer ids — the ONE list every view colours from.
   *
   * <p>Public because the rounds-log page is rendered outside this class and must not infer its own
   * list from the rounds it happens to hold: two lists differing by a single name are two different
   * colour assignments for the same vendor. See `vendorColour.ts`.</p>
   */
  vendorIds(): readonly string[] {
    return this.vendorsHere().map((v) => v.id);
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
   *
   * <p>The NEXT one arrived on 2026-09-14 and was not a missing declaration at all: the keys were
   * declared, and the WINDOW had not caught up with the update that declared them. That is why the
   * catch is here rather than inside `saveSetting` — and why what it reports is
   * {@link reportRefusal}, which offers the reload instead of describing the failure.</p>
   */
  private async save(config: vscode.WorkspaceConfiguration, key: string, value: unknown): Promise<void> {
    // The per-side branch moved to `sideConfig.saveSetting` when the roles page needed it too. A
    // second copy of "which layer does this belong in" is how the roles page came to write a per-side
    // setting globally. The REPORT stayed with the callers, because only a caller knows whether a
    // notification or a banner is the right surface — this panel has no banner, so it is the toast.
    try {
      await saveSetting(this.context, config, key, value);
    } catch (error: unknown) {
      reportRefusal(this.context, key, error);
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
      case 'restoreConsultPrompt':
        await this.saveConsultPrompt('');
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
      case 'customConsultant':
        if (id !== undefined) {
          await this.customConsultant(id);
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
      // Posted by the rounds-log page, never by the sidebar — the sidebar's spending region has no
      // chat half. It is in the shared vocabulary because that is where every `data-command` this
      // product emits is declared, and the exhaustiveness check below then demands a case here.
      case 'forgetChat':
        break;
      case 'installServer':
        // The panel has no business downloading anything itself: the command that does it is
        // registered by the extension, is what the ⋯ menu invokes, and reports its own progress
        // and its own failure. The button's job is only to reach it.
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.installServer);
        break;
      case 'editChatPresets':
        // The same shape, and for the same reason: the tab is opened by a registered command that
        // owns its own panel. Without this the section could name the presets it picks from and
        // offer no way to reach them — which is the state it shipped in.
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.editChatPresets);
        break;
      case 'editRoles':
        // The same shape again: the roles tab owns its own panel and is opened by a registered
        // command, so the Prompts section can point at the roles it draws.
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.editRoles);
        break;
      case 'editPhrases':
        // Once more for the phrases tab, which the Phrases section points at. A tab reachable only
        // from the command palette is a tab the operator could not find — the deviation the presets
        // tab shipped with, and which two tests now guard.
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.editPhrases);
        break;
      case 'changeDataDirectory':
        // Delegated for the same reason `installServer` is: the flow it runs owns dialogs, a folder
        // picker and a setting write, and the panel has no business holding any of them. It is the
        // SAME flow the first install asks, so there is one question with one answer rather than two
        // that drift.
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.changeDataDirectory);
        break;
      case 'moveDataDirectory':
        await vscode.commands.executeCommand(VSCODE_COMMAND_FOR.moveDataDirectory);
        break;
      case 'copyPhrase':
        await this.copyPhrase(id);
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
      case 'closeConsultation':
        await this.closeConsultation(id ?? '');
        break;
      case 'collectBugs':
        await this.collectBugs();
        break;
      case 'reviewBugs':
        await this.reviewBugs();
        break;
      case 'bugsKeys':
        await this.bugsKeys();
        break;
      case 'setBugsServer':
        await this.setBugsServer();
        break;
      case 'sendBugs':
        await this.sendBugs();
        break;
      case 'setBugsKey':
        await this.setBugsKey();
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
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'wslNetwork',
        code: 'windows-side-unreachable',
        title: 'The Windows side of this machine could not be reached through interop, so nothing was '
          + 'written. Put these two lines in %USERPROFILE%\\.wslconfig by hand, then run '
          + `\`wsl --shutdown\` from Windows:\n\n${mirroredLines('mirrored')}`,
        cure: 'Put the two lines in %USERPROFILE%\\.wslconfig by hand and run `wsl --shutdown`.',
      });

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
    const choice = await notifyAndAsk({
      as: 'information',
      class: 'confirmation',
      source: 'wslNetwork',
      code: 'mirrored-is-already-written',
      subject: path,
      modal: true,
      title: `${path} already says networkingMode=mirrored.`,
      detail: 'It takes effect when WSL next starts cold: run `wsl --shutdown` from Windows, then reopen '
        + 'this window. Nothing here can run it — it would terminate the distro this window is '
        + 'attached to, mid-call.',
      actions: [restart, revert],
    });
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
      void notify({
        as: 'warning',
        class: 'refusal',
        source: 'wslNetwork',
        code: 'wslconfig-merge-refused',
        subject: path,
        title: `${merged.refused}. Set it by hand instead:\n\n${mirroredLines(mode)}`,
        cure: 'Set networkingMode by hand, then run `wsl --shutdown`.',
      });

      return;
    }
    if (!merged.changed) {
      void notify({
        as: 'information',
        class: 'outcome',
        source: 'wslNetwork',
        code: 'wslconfig-already-says-this',
        subject: path,
        title: `${path} already says networkingMode=${mode}.`,
      });

      return;
    }

    const write = 'Write it';
    const confirmed = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'wslNetwork',
      code: 'write-the-wslconfig',
      subject: `${path}:${mode}`,
      modal: true,
      title: `Set networkingMode=${mode} in ${path}?`,
      // The WHOLE file, not the two lines this adds: the question a person needs answered before
      // approving a global change is whether their other settings survive it, and a preview that
      // shows only the addition cannot answer it.
      detail: `${previewOf(merged.text)}\n\nThis file is global: every WSL distro on this machine reads `
        + 'it, docker-desktop included. Nothing changes until WSL is restarted, which this cannot '
        + 'do for you — it would terminate the distro this window is attached to.',
      action: write,
    });
    if (confirmed !== write) {
      return;
    }

    const outcome = await writeWslconfig(path, merged.text);
    if (!outcome.written) {
      void notify({
        as: 'error',
        class: 'failure',
        source: 'wslNetwork',
        code: 'wslconfig-not-written',
        subject: path,
        title: outcome.message,
      });

      return;
    }
    if (outcome.message.length > 0) {
      // Written, but something about confirming it did not go to plan. Saying "it failed" here
      // would be a lie about a file that HAS changed, and the next press would offer to undo it.
      // A different CODE from the failure above for exactly that reason.
      void notify({
        as: 'warning',
        class: 'outcome',
        source: 'wslNetwork',
        code: 'wslconfig-written-but-unconfirmed',
        subject: path,
        title: outcome.message,
      });
    }

    const copy = 'Copy the command';
    const next = await notifyAndAsk({
      as: 'information',
      class: 'offer',
      source: 'wslNetwork',
      code: 'wslconfig-written-restart-wsl',
      subject: `${path}:${mode}`,
      title: `${path} now says networkingMode=${mode}. Run \`wsl --shutdown\` from Windows (not from here), `
        + 'then reopen this window — the setting is read when WSL next starts cold.',
      action: copy,
    });
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
        contract: known?.contract,
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
        const answer = await notifyAndAsk({
          as: 'warning',
          class: 'confirmation',
          source: 'teamServerAuth',
          code: 'approve-the-microsoft-application',
          subject: `${server.name}:${applicationId}`,
          modal: true,
          title: `Sign in to ${server.name}?`,
          detail: `${canonicalTeamServerUrl(server.url)} is asking for a token for Microsoft `
            + `application ${applicationId}. Only continue if that is your company's `
            + `ConnectOtherAIs server — a token minted here can be used by whoever runs it.`,
          action: go,
        });

        return answer === go;
      },
      say: (message) => void notify({
        as: 'information', class: 'outcome', source: 'teamServerAuth', code: 'team-server-said', title: message,
      }),
    };
  }

  /**
   * Add a server: verify it BEFORE saving, so a mistake is caught now rather than at the first review.
   *
   * <p>Raised on the plan round — a URL that is merely syntactically valid buys nothing, and the
   * failure would otherwise surface as a broken reviewer days later. A server that is simply DOWN is
   * not a mistake, so saving is still offered after the warning.</p>
   */
  /**
   * One copier for the life of the panel, so the status bar keeps ONE line.
   *
   * <p>It holds the handle to the message it last put there and disposes it before writing the next;
   * a copier made per press would leave five lines competing after five presses.</p>
   */
  private readonly copier = phraseCopier({
    writeText: (text: string) => Promise.resolve(vscode.env.clipboard.writeText(text)),
    say: (message: string, forMs: number) => vscode.window.setStatusBarMessage(message, forMs),
  });

  /**
   * A phrase onto the clipboard, and the button told whether it landed.
   *
   * <p>The deciding is `phraseCopy.ts`, where a test can reach it; this is the half that needs a
   * `vscode`. The page is told <b>afterwards</b>, and only when the write RESOLVED — a button that
   * said *Copied* the moment it was pressed would say it just as confidently while the clipboard was
   * held by something else, and the person would paste whatever was there before. (Plan round,
   * gemini, Blocking.)</p>
   */
  private async copyPhrase(id: string | undefined): Promise<void> {
    const report = await this.copier.copy(this.phrases(), id);
    if (!report.copied || id === undefined) {
      return;
    }
    // A disposed view is expected and dropped; anything else keeps its reporter, exactly as the live
    // region's post does. Swallowing every rejection would hide a webview that had stopped accepting
    // messages, and the button would then never confirm with nothing anywhere saying why.
    this.held.view?.webview.postMessage({ type: 'copied', id }).then(undefined, (error: unknown) => {
      if (!isDisposedRejection(error)) {
        console.error('ConnectOtherAIs: the panel could not be told that a phrase was copied', error);
      }
    });
  }

  /**
   * The phrase catalog, read in ONE place.
   *
   * <p>The render and the copy each used to read the configuration for themselves. Two readers of
   * one list is how the panel comes to show a button whose id the copy then refuses — the moment a
   * second source is added (a workspace list, a team list) only one of them would learn about it.
   * (Code round, codex.)</p>
   */
  private phrases(): readonly Phrase[] {
    return phrasesFrom(vscode.workspace.getConfiguration('coai').get('phrases'));
  }

  /**
   * Runs the collector over this machine's own accepted findings.
   *
   * <p><b>Nothing here holds the state.</b> The run writes itself to the database before it starts
   * a candidate and again when it ends, and the section reads that row — so closing the window,
   * pressing F5 or hiding the panel changes what is DISPLAYED and never what is true. A flag on
   * this class would die with the view, which is the failure the durable-status rule names.</p>
   *
   * <p>The model is passed for the record; the collector refuses a non-local one itself, before it
   * reads a finding, and the refusal arrives here as a non-zero exit with its own sentence.</p>
   */
  /**
   * How long a collect may run before the spawn is given up on.
   *
   * <p>Generous: a default run is two hundred candidates and each is several git subprocesses with
   * a thirty-second budget of its own. Giving up here does not stop the run — the child keeps
   * going and keeps writing its own row — it only stops this window waiting, and the section then
   * shows the run's real state from the database like any other.</p>
   */
  /** The settings as configuration has them now — the same read the render does. */
  private settings(): CoaiSettings {
    const config = vscode.workspace.getConfiguration('coai');

    return settingsFrom((section) => config.get(section));
  }

  private static readonly COLLECT_CAP_MS = 30 * 60_000;

  /**
   * How long a send may take.
   *
   * <p>Shorter than a collection because it is bounded by a network request rather than by a model,
   * and longer than one request because it is as many batches as it takes: two thousand pairs is ten
   * of them, each with the CLI's own two-minute timeout. A cap is not a promise that anything is
   * wrong at the end of it — the pairs are marked on acknowledgement, so a killed send is simply
   * offered again.</p>
   */
  private static readonly SEND_CAP_MS = 30 * 60_000;

  /**
   * How often the section is repainted while a collection runs.
   *
   * <p>Three seconds. Each tick is a process spawn reading a database, so this is not free — but a
   * run is minutes long and a progress line that moves twice is not progress. The same order as
   * the ten-second rounds-log cache and the five-second escalation watcher beside it.</p>
   */
  private static readonly COLLECT_TICK_MS = 3_000;


  /** The corpus as last read, and when — the section repaints far more often than this changes. */
  private bugzCache: BugCorpus = EMPTY_CORPUS;

  private bugzAt = 0;

  /** The review window, held so a second press returns to it rather than opening another. */
  private review: BugzReviewPanel | undefined;

  /**
   * What the corpus and the last run look like now.
   *
   * <p>Cached for a few seconds because a repaint is frequent and this is a process spawn. Setting
   * {@link bugzAt} to zero is how a press of Collect says "ask again now" without a second path.</p>
   */
  private async bugz(): Promise<BugCorpus> {
    const AGE_MS = 5_000;
    if (Date.now() - this.bugzAt < AGE_MS) {
      return this.bugzCache;
    }

    const server = serverPath(this.context.globalStorageUri);
    this.bugzAt = Date.now();
    this.bugzCache = server === undefined ? EMPTY_CORPUS : await readBugs(server.fsPath);

    return this.bugzCache;
  }

  /**
   * Ends a consultation by hand, because the AI that asked may never come back to say.
   *
   * <p><b>VS Code's own picker is the selector</b>, rather than a modal built in the webview: it
   * brings the confirmation, the cancellation (Escape), the keyboard and the screen-reader behaviour
   * with it, and a hand-made one in a panel this size would be a second, worse copy of all four. The
   * note is optional and is what a person reads months later when deciding whether consulting this
   * vendor was worth the money.</p>
   *
   * <p><b>The server is reached through its one-shot mode</b>, not through MCP — the extension does
   * not speak it. A refusal comes back on stdout with exit 65 and is SHOWN: a close that failed
   * silently would leave the card exactly as it was, which reads as a button that does nothing.
   * (issue #309.)</p>
   *
   * <p><b>And exit 64 is a sentence of its own.</b> The two halves of this product update
   * separately, so a person whose extension is newer than their server presses a button the binary
   * has never heard of. 64 is exactly what that binary answers — <c>.agents/PROJECT.md</c> reserves
   * the code for it — and showing its argument-parser's complaint instead would send somebody to
   * debug a request that was never read. (codex, the code round.)</p>
   *
   * <p><b>Under a progress notification</b>, because it is not instant: the run waits on the
   * consultation's own repository lock, which a turn in that checkout can hold for the length of a
   * vendor call. Without one the picker closes, nothing moves, and the only feedback for up to
   * twenty seconds is a table that has not changed. (codex UX, the code round.)</p>
   */
  async closeConsultation(id: string): Promise<void> {
    if (id.length === 0) {
      return;
    }
    const server = serverPath(this.context.globalStorageUri);
    if (server === undefined) {
      await notify({
        as: 'warning',
        class: 'refusal',
        source: 'consultations',
        code: 'server-not-installed-for-closing',
        title: 'The MCP server is not installed yet, so there is nothing to record this with.',
        cure: 'Install it from the ConnectOtherAIs panel.',
      });

      return;
    }

    const chosen = await vscode.window.showQuickPick(
      CLOSE_CHOICES.map((one) => ({ label: one.label, detail: one.detail, outcome: one.outcome })),
      { title: 'How did this consultation end?', placeHolder: 'Escape leaves it open' },
    );
    if (chosen === undefined) {
      return; // cancelled, and a cancelled close changes nothing
    }

    const note = await vscode.window.showInputBox({
      title: `Recording '${chosen.label}'`,
      prompt: 'One sentence for the log — what you did, or why it was dropped. Optional.',
      placeHolder: 'leave empty to record the outcome alone',
    });
    if (note === undefined) {
      return; // Escape on the note is Escape on the whole thing
    }

    // THE CONSULTATION'S OWN checkout, read from the row, not this window's first folder. The log
    // lists consultations from every repository a person has reviewed, so a close made from it must
    // take the repository lock on the one it belongs to — a lock on whatever happens to be open
    // would guard nothing and could block something unrelated. (The review pass, 2026-09-17.)
    const repo = (await this.roundsLog()).consultations.find((one) => one.id === id)?.repoPath ?? '';
    if (repo.length === 0) {
      await notify({
        as: 'warning',
        class: 'refusal',
        source: 'consultations',
        code: 'consultation-gone-from-the-log',
        // The consultation, because this code is about one of them and a shared counter would let
        // recovering one reset the other.
        subject: id,
        title: 'That consultation is not in the log any more.',
        detail: `${id} — a record is swept some days after it ends.`,
      });

      return;
    }
    const { code, output } = await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: `Recording '${chosen.label}'…`,
        cancellable: false,
      },
      // The WHOLE wait: the run takes the consultation's repository lock, and a turn running in
      // that checkout holds it for as long as a vendor takes to answer.
      async () => serverRun(server.fsPath)(
        ['--close-consult', '--repo', repo, '--id', id, '--outcome', chosen.outcome,
          ...(note.trim().length > 0 ? ['--note', note.trim()] : [])],
        PanelProvider.CLOSE_CONSULT_CAP_MS));

    if (code === SERVER_TOO_OLD) {
      // Reserved, and for this only: a binary that knows the mode answers 65 however wrong the
      // request was. So this is not "something went wrong" — it is the one failure whose cure is a
      // version rather than a different click, and saying it is the whole value of the code.
      await notify({
        as: 'warning',
        class: 'refusal',
        source: 'consultations',
        code: 'server-too-old-to-close-a-consultation',
        title: 'This MCP server is too old to record how a consultation ended.',
        detail: 'Nothing was changed.',
        cure: 'Update the server from the ConnectOtherAIs panel, then try again.',
      });
    } else if (code !== 0) {
      // The SENTENCE the server wrote, not a code: it names what is already on the record, or the
      // word it would have taken. `refusalIn` reads the JSON and falls back to the raw text.
      await notify({
        as: 'error',
        class: 'failure',
        source: 'consultations',
        code: 'consultation-not-closed',
        subject: id,
        title: 'The consultation was not closed.',
        detail: refusalIn(output),
      });
    }

    await this.render();
  }

  /** How long a close may take. It is one small write behind a repository lock, not a vendor turn. */
  private static readonly CLOSE_CONSULT_CAP_MS = 20_000;

  private async collectBugs(): Promise<void> {
    const server = serverPath(this.context.globalStorageUri);
    if (server === undefined) {
      await notifyAndAsk({
        as: 'warning',
        class: 'refusal',
        source: 'bugz',
        code: 'server-not-installed-for-collecting',
        title: 'The MCP server is not installed yet, so there is nothing to collect with.',
      });

      return;
    }

    // Through `serverRun`, which is the door that carries THIS window's data directory. A spawn
    // that skipped it would collect against a different database from the one the section shows.
    const model = this.settings().bugzModel;
    const run = serverRun(server.fsPath);
    const collecting = run(
      ['--collect-bugs', ...(model.length > 0 ? ['--model', model] : [])],
      PanelProvider.COLLECT_CAP_MS);

    // NOT awaited before the repaint, and this is the point. The run writes `running` to the
    // database before its first candidate, so the section can show it — but only if something
    // asks. Awaiting the whole process first meant the button stayed enabled and un-progressed
    // for the length of a multi-minute run, and a second click started a second collection.
    // (Code round, gemini and codex, four findings between them.)
    await this.watchCollect(collecting);
  }

  /**
   * Repaints while a collection runs, and once more when it stops.
   *
   * <p>A poll rather than a subscription, because the writer is another PROCESS and the only
   * channel between them is the database. Every tick is one spawn of `--bugs-json`, which is why
   * it is seconds rather than milliseconds; the run writes a beat per candidate, so the numbers
   * move whether or not a tick lands on one.</p>
   *
   * <p>It stops when the process exits, and repaints a final time from the row the run's own
   * `finally` wrote — so a crash shows as `failed` and an abandoned run as `interrupted`, rather
   * than as a button that waits for ever.</p>
   */
  private async watchCollect(collecting: Promise<{ code: number; output: string }>): Promise<void> {
    let over = false;
    const finished = collecting.then((answer) => {
      over = true;

      return answer;
    });

    while (!over) {
      this.bugzAt = 0;
      await this.render();
      await Promise.race([
        finished,
        new Promise((wake) => setTimeout(wake, PanelProvider.COLLECT_TICK_MS)),
      ]);
    }

    const { code, output } = await finished;
    if (code !== 0) {
      // Its own words: the collector says why it refused, and paraphrasing them here would be a
      // second copy of a rule that lives in the core.
      // The collector's own words when it gave any, and a sentence of ours when it gave none — the
      // plan round named this as one of the six that say nothing at all when the output is empty.
      await notifyAndAsk({
        as: 'warning',
        class: 'failure',
        source: 'bugz',
        code: 'collector-refused',
        title: output.trim() || 'The collector could not run.',
        detail: output.trim(),
      });
    }

    this.bugzAt = 0;
    await this.render();
  }
  /**
   * Asks for the ingest server's address.
   *
   * <p>A dialog rather than a box in the section, as {@link addTeamServer} and the consultant's
   * custom endpoint already are. The section is in `staticKey` because the Collect button has to
   * repaint while a run happens, and a free-text control inside a repainting section is rebuilt
   * under the caret on every keystroke. There IS a third way — the consultant prompt is a textarea
   * held by `focusin` — but a dialog is what the two nearest neighbours do, it validates before it
   * closes, and it keeps this section free of the trap entirely.</p>
   */
  /**
   * Opens the review page over what has been collected.
   *
   * <p>One panel, held, so pressing the button twice brings the same window back rather than
   * opening a second one over the same rows — two windows writing decisions about the same pairs
   * would each redraw from a database the other had just changed.</p>
   */
  private async reviewBugs(): Promise<void> {
    const server = serverPath(this.context.globalStorageUri);
    if (server === undefined) {
      await notifyAndAsk({
        as: 'warning',
        class: 'refusal',
        source: 'bugz',
        code: 'server-not-installed-for-reviewing',
        title: 'The MCP server is not installed yet, so there is nothing to review.',
      });

      return;
    }

    this.review ??= new BugzReviewPanel({
      read: () => readPairs(server.fsPath),
      decide: (ids, keep) => writeKeep(
        server.fsPath, ids, keep, keysFileIn(this.context.globalStorageUri.fsPath)),
      // A decision changes how many pairs the Bugz section says are waiting, and that section is a
      // different window onto the same database. Without this the count sat stale until something
      // unrelated repainted the panel. (Code round, gemini.)
      changed: async () => {
        this.bugzAt = 0;
        await this.render();
      },
    });

    await this.review.show();
    // The Bugz section shows how many are waiting, and a decision changes that.
    this.bugzAt = 0;
    await this.render();
  }

  /**
   * Opens the Users tab.
   *
   * <p>The server address comes from the same setting the ingest side uses, read at each call
   * rather than captured: it is changed by the button right below this one, and a panel holding a
   * stale copy would ask the wrong host and blame the key.</p>
   */
  private async bugsKeys(): Promise<void> {
    await usersPanel(
      this.context.secrets,
      () => vscode.workspace.getConfiguration('coai').get<string>('bugzServer', '').trim(),
    ).show();
  }

  /**
   * Sends what this machine has kept, through the real one-shot.
   *
   * <p><b>Everything is decided before the child exists.</b> `mayStart` reads the address, the key
   * and the database's own send row, so an address a credential must not cross never has one put
   * into a process environment — the CLI refuses it too, and that belt-and-braces is deliberate, but
   * only this check runs before the key moves. (Plan round, codex.)</p>
   *
   * <p><b>And the key reaches the child through `uploadRun`</b>, the one door that carries a
   * credential: never an argument, which is in `ps` and in a shell history, and never a file, which
   * is a cleanup a crash skips.</p>
   */
  private async sendBugs(): Promise<void> {
    const server = serverPath(this.context.globalStorageUri);
    if (server === undefined) {
      await notify({
        as: 'warning',
        class: 'refusal',
        source: 'bugz',
        code: 'server-not-installed-for-sending',
        title: 'The MCP server is not installed yet, so there is nothing to send with.',
      });

      return;
    }

    const where = this.settings().bugzServer.trim();
    const key = await contributorKey(this.context.secrets);
    const refusal = mayStart({ server: where, key, corpus: this.bugzCache });
    if (refusal !== undefined) {
      await notify({
        as: 'warning',
        class: 'refusal',
        source: 'bugz',
        code: `send-refused-${refusal.kind}`,
        title: refusal.why,
      });

      return;
    }

    await this.watchSend(uploadRun(server.fsPath, key)(
      ['--upload-pairs', '--server', where], PanelProvider.SEND_CAP_MS));
  }

  /**
   * Repaints while a send runs, and says what it came to when it stops.
   *
   * <p>The same poll `watchCollect` uses, and for the same reason: the writer is another PROCESS and
   * the only channel between them is the database. What differs is the ending — a send has an
   * outcome a person has to read, and every exit the CLI can answer has its own sentence, including
   * the two that are about this machine rather than about the pairs.</p>
   */
  private async watchSend(sending: Promise<{ code: number; output: string }>): Promise<void> {
    const answer = await sending;
    await this.render();

    // A send that WORKED is an outcome and a send that did not is a failure, which is the
    // difference the funnel of notification classes is for: one is a thing that happened and the
    // other is a thing to act on.
    const outcome = outcomeOf(answer.code, answer.output);
    const went = outcome.kind === 'sent' || outcome.kind === 'partly';
    await notify({
      as: went ? 'information' : 'warning',
      class: went ? 'outcome' : 'failure',
      source: 'bugz',
      code: `send-${outcome.kind}`,
      title: outcome.said,
    });
  }

  /**
   * Asks for the contributor key and stores it in the editor's secret storage.
   *
   * <p>Never `settings.json`: settings sync, and a credential that follows somebody to another
   * machine is one nobody can account for. The same rule and the same storage as the admin key,
   * which is why they share a module.</p>
   */
  private async setBugsKey(): Promise<void> {
    const typed = await vscode.window.showInputBox({
      title: 'The contributor key for the ingest server',
      prompt: 'Kept in the editor\'s secret storage on this machine only — never in settings, which sync.',
      password: true,
      ignoreFocusOut: true,
    });
    if (typed === undefined) {
      return;
    }

    await setContributorKey(this.context.secrets, typed);
    await this.render();
  }

  private async setBugsServer(): Promise<void> {
    const typed = await vscode.window.showInputBox({
      title: 'Where collected pairs are sent',
      value: this.settings().bugzServer,
      prompt: 'The address of the bug ingest server. Leave empty to send nowhere.',
      validateInput: (value) => {
        const trimmed = value.trim();
        if (trimmed.length === 0) {
          return undefined;
        }

        // Refuses while the box is still open, which is the reason for a dialog over a box: the
        // person fixes it where they typed it instead of finding out later from a failed send.
        try {
          const url = new URL(trimmed);

          return url.protocol === 'https:' || url.protocol === 'http:'
            ? undefined
            : 'An http or https address.';
        } catch {
          return 'That is not an address.';
        }
      },
    });

    if (typed === undefined) {
      return;
    }

    // To configuration, like every other control here: a value kept on this object would be lost
    // on reload and invisible to the Settings UI.
    await vscode.workspace.getConfiguration('coai').update(
      'bugzServer', typed.trim(), vscode.ConfigurationTarget.Global);
    await this.render();
  }

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
    const answer = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'teamServer',
      code: 'add-a-server-that-could-not-be-checked',
      subject: server.name,
      modal: true,
      title: `${server.name} could not be checked: ${config.message}`,
      detail: 'A server that is only down right now is not a mistake — but a wrong address is, '
        + 'and it would otherwise surface as a broken reviewer days from now.',
      action: anyway,
    });

    return answer === anyway;
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
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'teamServer',
        code: 'sign-in-failed',
        subject: server.name,
        title: result.message,
      });
    }
  }

  private async signOutTeamServer(id: string): Promise<void> {
    const server = this.serverNamed(id);
    if (server === undefined) {
      return;
    }

    const result = await signOut(server, this.authHost(), await readToken(coaiDataDir(), server.url));
    if (!result.ok) {
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'teamServer',
        code: 'sign-out-failed',
        subject: server.name,
        title: result.message,
      });
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
      (v) => v.runtime === 'remote' && rowBelongsTo(v, server),
    );
    const both = rows.length === 1 ? 'Remove it and 1 reviewer' : `Remove it and ${rows.length} reviewers`;
    const answer = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'teamServer',
      code: 'remove-a-team-server',
      subject: server.name,
      modal: true,
      title: `Remove ${server.name}?`,
      detail: rows.length === 0
        ? 'You will be signed out of it and its token deleted from this machine.'
        : `You will be signed out of it and its token deleted. These reviewers point at it and `
          + `cannot work without it: ${rows.map((r) => r.id).join(', ')}. Their spending history `
          + `is kept either way.`,
      actions: rows.length === 0 ? ['Remove'] : [both, 'Remove the server only'],
    });
    if (answer === undefined) {
      return;
    }

    // Signed out FIRST, so the session is ended ON the server rather than left running there. Its
    // failure is SHOWN: a token file that survived removal is a credential nobody is watching for.
    const gone = await signOut(server, this.authHost(), await readToken(coaiDataDir(), server.url));
    if (!gone.ok) {
      // The same code as the ordinary sign-out failure: it is the same condition, and the removal
      // carries on either way.
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'teamServer',
        code: 'sign-out-failed',
        subject: server.name,
        title: gone.message,
      });
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
      // From WHICHEVER call reached the server: the header rides every response, so a catalog call
      // that failed to connect while the usage call got through still leaves the row knowing what it
      // is talking to. Only when an HTTP response actually arrived — `undefined` means the call never
      // reached a server (a timeout, a refused connection, a URL that is not https), and recording
      // that as "this server named nothing" would report every network blip as a server too old.
      contract: answer.contract ?? spent.contract ?? known?.contract,
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
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'teamServer',
        code: 'catalog-could-not-be-read',
        subject: server.name,
        title: `${server.name}: ${answer.message}`,
      });

      return undefined;
    }

    // A catalog is a STRANGER'S answer. An id like `../../other` would otherwise be copied verbatim
    // into a reviewer row id — which names that row's spending history and its vault key — and onto
    // a command line as `--vendor`. Entries that cannot be an id are dropped and said out loud
    // rather than silently, because a vendor going missing needs a reason. Caught on the code round.
    const usable = (answer.value.vendors ?? []).filter((v) => isUsableVendorId(v.id));
    const refused = (answer.value.vendors ?? []).length - usable.length;
    if (refused > 0) {
      void notify({
        as: 'warning',
        class: 'refusal',
        source: 'teamServer',
        code: 'catalog-offered-unusable-ids',
        subject: server.name,
        title: `${server.name} offered ${refused} vendor(s) whose name this extension will not use as an `
          + 'id. They are not listed.',
      });
    }

    const offered = usable;
    if (offered.length === 0) {
      void notify({
        as: 'warning',
        class: 'refusal',
        source: 'teamServer',
        code: 'catalog-is-empty',
        subject: server.name,
        title: `${server.name} offers no vendors yet — the operator has not added any accounts to it.`,
      });

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
    // The catalogue is offered WHOLE. It used to drop any preset already in the panel, which made a
    // second row of anything impossible and said nothing about why the entry had gone — the same
    // one-way door VENDOR_PRESETS' own docblock records for gemini. A preset whose id is taken now
    // comes with the next free one and an item that says so.
    const items = reviewerPickItems(presetsOffered(VENDOR_PRESETS, existing));
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
        ...items.map((p) => ({
          label: p.label,
          detail: p.detail,
          description: p.description,
          offered: p.offered,
          server: undefined,
        })),
        ...teamServers.map((s) => ({
          label: `Team server ${s.name}`,
          detail: `on ${canonicalTeamServerUrl(s.url)} — the company's subscription, nothing to install`,
          description: '',
          offered: undefined,
          server: s,
        })),
      ],
      {
        title: 'Add a reviewer',
        placeHolder: 'Which vendor should review as well?',
        // Both, and neither is decoration. Without matchOnDetail a person typing words from an
        // entry's hint gets an empty list; without matchOnDescription the same happens to anyone
        // typing the id a second row will take, which is only ever written in the description.
        matchOnDetail: true,
        matchOnDescription: true,
      },
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

    // `server` above and `offered` here are the two arms of the list, and the branch above returns,
    // so this is only ever reached for a catalogue entry. It is written as a guard rather than a
    // non-null assertion because four reviewers in one round read the assertion as a crash waiting
    // for anyone who picked a Team server — it was not, and a guard means nobody has to prove that
    // again from the control flow.
    const chosen = picked.offered;
    if (chosen === undefined) {
      return;
    }

    // The id comes from the OFFERING, not from the preset: a second row of a configured vendor was
    // allocated its own free name there. The blank preset keeps its empty id, so the branch below
    // still asks for a name and a URL rather than writing a row nobody named.
    let vendor: Vendor = { ...chosen.preset, id: chosen.id };
    if (vendor.id.length === 0) {
      const own = await this.askCustomEndpoint('Add a reviewer');
      if (own === undefined) {
        return;
      }
      vendor = { ...vendor, id: own.id, baseUrl: own.baseUrl };
    }

    await this.saveVendor(vendor);
  }

  /**
   * A name and a base URL, asked once for both the reviewer list and the consultant.
   *
   * <p>ONE function because the two flows must mint the same id from the same words: the id keys the
   * vault entry, and two spellings of one name are two keys and a credential that is only there
   * half the time. It lived inline in {@link addVendor} until story C6 needed the same two boxes for
   * the consultant, and a second copy would have drifted on validation first.</p>
   *
   * <p>The name box refuses a name that normalises to nothing, and refuses one another row already
   * holds AT A DIFFERENT ENDPOINT — said while the box is open rather than after it closes, which is
   * why the check is a validator and `endpointConflict` returns a sentence. The same endpoint under
   * the same name is not a clash: it is one service named once. Dismissing either box returns
   * `undefined` and nothing anywhere is written. (gemini, C6's plan round.)</p>
   */
  private async askCustomEndpoint(title: string, caller = ''): Promise<{ id: string; baseUrl: string } | undefined> {
    const config = vscode.workspace.getConfiguration('coai');
    const rows = vendorsFrom(this.read(config)('vendors'));
    const consultants = (this.read(config)('consultants') as Record<string, unknown> | undefined) ?? {};
    const name = await vscode.window.showInputBox({
      title,
      prompt: 'A short name — it identifies the vendor and names its key in the vault entry',
      placeHolder: 'mistral',
      validateInput: (v) => (normaliseId(v).length === 0 ? 'A name is needed' : undefined),
    });
    if (name === undefined) {
      return undefined;
    }

    const baseUrl = await vscode.window.showInputBox({
      title: `${title}: ${normaliseId(name)}`,
      prompt: 'Its OpenAI-compatible base URL',
      placeHolder: 'https://api.example.com/v1',
      validateInput: (v) => (badEndpoint(v) ?? (endpointConflict(name, v, rows, consultants, caller) || undefined)),
    });

    return endpointAnswer(name, baseUrl);
  }

  /**
   * "Another OpenAI-compatible endpoint", chosen in one caller's consultant row.
   *
   * <p>The picker posted a COMMAND rather than a setting, because the option it carries has no id
   * and an id is what keys the vault entry — so the name is asked for FIRST and the write happens
   * once, with everything it needs. The definition lands in that caller's consultant record and
   * nowhere else: it is not appended to the reviewer rows, because three independent sets of
   * settings is the whole ruling, and a consultant that added itself to somebody's reviewers would
   * be the coupling this plan removed coming back through the last open door. (gemini, C6's plan
   * round, on where the definition lands.)</p>
   */
  private async customConsultant(caller: string): Promise<void> {
    const own = await this.askCustomEndpoint('A consultant of your own', caller);
    if (own === undefined) {
      return; // dismissed at either box — the row keeps the vendor it had
    }

    const config = vscode.workspace.getConfiguration('coai');
    const current = (this.read(config)('consultants') as Record<string, unknown> | undefined) ?? {};
    await this.save(config, 'consultants', consultantEndpointWrite(current, caller, own.id, own.baseUrl));
  }

  /** One place a new reviewer is written, so both routes refuse a duplicate the same way. */
  private async saveVendor(vendor: Vendor): Promise<void> {
    const config = vscode.workspace.getConfiguration('coai');
    const vendors = vendorsFrom(this.read(config)('vendors'));
    if (vendors.some((v) => v.id === vendor.id)) {
      void notify({
        as: 'warning',
        class: 'refusal',
        source: 'reviewers',
        code: 'reviewer-already-added',
        subject: vendor.id,
        title: `${vendor.id} is already a reviewer.`,
      });

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
      void notify({
        as: 'warning',
        class: 'refusal',
        source: 'reviewers',
        code: 'the-last-reviewer-stays',
        title: 'A review panel needs at least one reviewer.',
      });
      return;
    }

    const confirmed = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'reviewers',
      code: 'remove-a-reviewer',
      subject: id,
      modal: true,
      title: `Remove ${id} from the review panel?`,
      detail: 'Its model and endpoint settings go with it. Every vendor can be added back from the presets.',
      action: 'Remove',
    });
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

  /**
   * The consultant's prompt override, or empty when there is none.
   *
   * <p>Read at paint rather than cached, exactly like the pasted snippet above it: the file is small,
   * it can be edited by hand or by another window, and a cache would show a person their own edit
   * from two minutes ago with nothing saying so.</p>
   */
  /** The last reason a prompt write failed, so one unwritable disk is one message. */
  private promptWriteFailed = '';

  private async readConsultPrompt(): Promise<string> {
    try {
      return new TextDecoder().decode(
        await vscode.workspace.fs.readFile(vscode.Uri.joinPath(this.dataDir, ...CONSULT_PROMPT_PATH)),
      );
    } catch {
      return ''; // no override, which is the ordinary state and means the shipped prompt
    }
  }

  /**
   * Writes what is in the box to the server's prompt override, or takes the override away.
   *
   * <p>Not `config.update`: the server reads its prompts from its own data directory, override-first,
   * so the file IS the setting. Writing a `coai.*` key beside it would have given one prompt two
   * homes, and the hand-edit the server has always supported would have been reverted by whichever
   * window mirrored next.</p>
   *
   * <p><b>Written beside it and renamed over it</b>, the way the settings file and an answered
   * escalation already are. `writeFile` truncates before it fills, so a host killed between the two
   * leaves the SERVER reading a half-written prompt — and the server reads its prompts override-first
   * without a second opinion, so a truncated one is simply what the consultant is asked. Raised by two
   * reviewers on this story's plan round.</p>
   *
   * <p>A failure is swallowed the way the settings write's is, and for the same reason: this runs
   * from a keystroke pause, and a disk that will not take a file is not something a panel can fix by
   * interrupting somebody about it. Nothing claims the prompt was saved — the box is repainted from
   * the FILE, so a write that did not land shows as the words coming back on the next paint.</p>
   */
  private async saveConsultPrompt(value: unknown): Promise<void> {
    const write = consultPromptWrite(value);
    const target = vscode.Uri.joinPath(this.dataDir, ...CONSULT_PROMPT_PATH);
    try {
      if (write.kind === 'remove') {
        // Removing an override that was never written is the ORDINARY case, not an error — and it is
        // the ONLY one this swallows. It used to swallow every rejection, so a permission failure or
        // a provider error left the old prompt in force while the panel cleared its warning and the
        // restore looked as though it had worked. (CodeRabbit, on the pull request.)
        try {
          await vscode.workspace.fs.delete(target);
        } catch (error) {
          if (!(error instanceof vscode.FileSystemError) || error.code !== 'FileNotFound') {
            throw error;
          }
        }
        this.promptWriteFailed = '';
        return;
      }
      const directory = vscode.Uri.joinPath(this.dataDir, CONSULT_PROMPT_PATH[0]!);
      await vscode.workspace.fs.createDirectory(directory);
      const temp = vscode.Uri.joinPath(directory, `${CONSULT_PROMPT_PATH[1]}.${process.pid}.tmp`);
      await vscode.workspace.fs.writeFile(temp, new TextEncoder().encode(write.text));
      try {
        await vscode.workspace.fs.rename(temp, target, { overwrite: true });
      } catch (error) {
        // The temp name carries the pid, so a rename that keeps failing leaves one more file beside
        // the one the SERVER reads out of this directory. The settings writer already cleans up on
        // its own failure path for the same reason. (CodeRabbit, on the pull request.)
        await vscode.workspace.fs.delete(temp).then(undefined, () => undefined);
        throw error;
      }
      // The situation is over. A failure after this is news rather than a repeat.
      this.promptWriteFailed = '';
    } catch (e) {
      this.reportPromptFailure(e instanceof Error ? e.message : String(e));
    }
  }

  /**
   * Says, ONCE per distinct reason, that the consultant's prompt is not on disk.
   *
   * <p>The durable-status rule pointed at a text box: an action that failed must not look like one
   * that succeeded, and this one used to be swallowed entirely — a read-only data directory left a
   * person typing into a box whose words no consultation would ever read. Once per reason, and
   * cleared by the next successful write, because this runs from a keystroke PAUSE: a message per
   * pause over one unwritable disk is the other way to make it unusable. Raised twice on this
   * story's code round, in two roles.</p>
   */
  private reportPromptFailure(why: string): void {
    if (this.promptWriteFailed === why) {
      return;
    }
    this.promptWriteFailed = why;
    void notify({
      as: 'warning',
      class: 'failure',
      source: 'consultant',
      code: 'consultant-prompt-not-saved',
      title: `The consultant's prompt could not be saved, so consultations still use the previous one: ${why}`,
      detail: why,
    });
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
 * Where this window keeps its data — and never an exception, whatever the environment says.
 *
 * <p>`coaiDataDir()` THROWS on an unusable `COAI_DATA_SIDE`, which is right for every other caller:
 * the server refuses to start on it, and a token written to a guessed path would be worse than
 * none. It is wrong for the panel. That value is exactly when a person opens this section to find
 * out what is wrong, and an exception here would empty the whole panel and take the sentence that
 * explains it with them.</p>
 *
 * <p>`whereData` already returns the refusal as a state rather than throwing. This wrapper exists
 * for the other half of the same finding (codex, plan round): anything ELSE the environment or the
 * filesystem can throw — a permission error on the existence check, a path the platform rejects —
 * must also reach the page as a sentence instead of a blank section.</p>
 */
async function whereThisWindowKeepsItsData(): Promise<DataLocation> {
  try {
    // The two paths are probed BEFORE the pure rule runs, off the event loop. The primary use case
    // for a chosen directory is a NAS, and a synchronous existsSync against a disconnected SMB share
    // blocks the extension host for as long as the share takes to time out — every panel
    // interaction hung, with nothing on screen to say why. Raised on the code round by codex and
    // gemini. A probe that throws is a probe that answered "not there", which is the honest reading
    // of a permission error on a path we are only describing.
    const present = new Set<string>();
    // The chosen ROOT, from whichever layer named it — never the variable, which stopped being the
    // whole answer when a directory could be chosen in a setting.
    const configured = chosenRoot();
    for (const path of configured.length === 0 ? [] : probePaths()) {
      if (await reachable(path)) {
        present.add(path);
      }
    }

    // WITH the other installations' directories. `whereData` is pure and cannot read a setting, and
    // the panel is the one surface where a mistyped path can be seen at all — a directory that cannot
    // be read contributes no questions and throws nothing, which is the silence this feature exists
    // to end, and would be the silence again one level up.
    const resolved = whereData((path) => present.has(path));

    return {
      ...resolved,
      alsoWatched: await probeWatched(resolved.directory.length > 0 ? resolved.directory : coaiDataDir()),
    };
  } catch (error) {
    return {
      directory: '',
      side: dataSideName(),
      ignoredSide: '',
      refusal: `This window could not work out where its data lives: ${asText(error)}`,
      notes: [],
      alsoWatched: [],
      env: {},
      // Nothing resolved, so no layer answered. Naming one here would be this surface guessing about
      // the very question it has just failed to answer.
      source: 'default',
    };
  }
}

/** The two paths the notes ask about — the shared root's database, and this side's directory. */
function probePaths(): readonly string[] {
  const root = chosenRoot();

  return [join(root, 'coai.db'), coaiDataDir()];
}

/**
 * The watched directories, each PROBED, so the panel says what is really being read.
 *
 * <p>`watchedDirs` refuses what cannot work on this host — a POSIX path named from a Windows window.
 * A path that is merely absent, unmounted or permission-refused passes that and then contributes no
 * questions and throws nothing, which is indistinguishable from an installation that has asked
 * nothing. That is the exact silence `coai.alsoWatchDataDirectories` exists to end, so leaving it off
 * this surface would be the same bug one level up. (codex and gemini, the code round.)</p>
 *
 * <p>Probed off the event loop, for the reason every other probe in this file is: the use case is a
 * NAS, and a synchronous check against a disconnected share hangs the extension host. The window's
 * OWN directory is not probed here — `whereData` has already answered for it.</p>
 */
async function probeWatched(own: string): Promise<readonly WatchedDir[]> {
  const asked = watchedDirs(own, alsoWatchDataDirectories(), process.platform);

  return Promise.all(asked.map(async (dir, at) => (
    at === 0 || dir.refusal.length > 0 || await reachable(dir.path)
      ? dir
      : {
        ...dir,
        refusal: 'this window cannot read that folder — check the path, and that the machine or share holding it is up',
      })));
}

/** Whether a path is there, without ever throwing and without blocking the host. */
async function reachable(path: string): Promise<boolean> {
  try {
    await access(path);
    return true;
  } catch {
    return false;
  }
}
