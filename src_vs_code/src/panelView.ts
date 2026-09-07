import {
  TeamServerState,
  teamServersBody,
  teamUsageBlock,
  usageScopeControl,
} from './teamServerView';
import { canonicalTeamServerUrl } from './teamServers';
import { escapeHtml } from './escapeHtml';
import { availabilityOf, ProviderHealth, ProvidersAnswer } from './providers';
import { CoaiSettings } from './settingsShape';
import { Escalation } from './escalations';
import { HELP, HelpKey } from './help';
import { ModelChoice, modelsFor, modelsProvenance, RemoteProvenance } from './models';
import { ROLES, promptsFor, selectedFor } from './prompts';
import { barWidth, estimated, money, shortDuration, shortNumber, totalsByVendor, UsageEntry, Window, within } from './usage';
import { costPhrase, elapsed, isRunning, reviewerRows, RoundRecord, SessionFile, stageName } from './rounds';
import { vendorColour } from './vendorColour';
import { CliStatus, cliStatusNote, updateAvailable, UNKNOWN_CLI } from './cliVersions';
import { SnippetStatus, snippetNote } from './claudeSnippet';
import { LocalEngine, remoteWarning } from './localEngines';
import { ServerStatus } from './coaiInstall';
import { ModelPrice } from './modelPrices';
import { Vendor } from './vendors';

/**
 * The panel's HTML, as a pure function of what it shows.
 *
 * <p>Pure and `vscode`-free so the markup is a test rather than something checked by opening a
 * sidebar. Three rules the tests hold: every colour comes from a theme variable, every value that
 * came from a file or a person is escaped — a question is DATA, never markup — and nothing can
 * make the view scroll sideways.</p>
 *
 * <p><b>Labels sit above their controls, never beside them.</b> A sidebar is narrow and its width
 * is the person's choice; a label-and-field row collides the moment they drag it in, and then the
 * whole view scrolls sideways. Stacked fields cannot do that.</p>
 *
 * <p><b>One section per job, and most of them closed.</b> Almost everything here is configured
 * once and never touched again; a wall of it hides the two things that do change — who reviews,
 * and what came back.</p>
 */

export interface PanelState {
  readonly settings: CoaiSettings;
  /**
   * The Team servers this machine knows, with whatever their catalogs last said.
   *
   * <p>It is IN {@link staticKey} rather than a live region: `liveRegions` returns exactly two
   * (`questions` and `rounds`), so everything else reaches the screen by the repaint. A section that
   * was patched instead would be a section that stops updating the day somebody reorders the DOM.</p>
   */
  /**
   * <p><b>Optional deliberately.</b> The provider always supplies it; a TEST fixture does not, and
   * making it required would have meant editing sixteen of them. That is not a style preference
   * here: these files are stored with CRLF under `core.autocrlf=true`, so any commit that touches
   * one rewrites every line of it — sixteen unreviewable whole-file diffs to add one field nobody's
   * assertions care about. Absent means none, which is what a panel with no Team servers has.</p>
   */
  readonly teamServers?: readonly TeamServerState[] | undefined;
  /**
   * What the SERVER says about each configured reviewer, from `coai-mcp --providers`.
   *
   * <p>Optional, and absent means the server was not asked or could not answer — which badges
   * NOTHING. The panel displays this decision; it does not make it.</p>
   */
  readonly providers?: ProvidersAnswer | undefined;
  /**
   * Whose spending the usage section is showing. `company` is offered only to an admin.
   *
   * <p>Optional, like {@link teamServers}, and absent means the same as `me`.</p>
   */
  readonly usageScope?: 'me' | 'company' | undefined;
  readonly vendors: readonly Vendor[];
  readonly codexModels: readonly ModelChoice[];
  /** What `agy models` lists on this machine, or none when it could not be asked. */
  readonly agyModels: readonly ModelChoice[];
  /**
   * The server binary on the side this panel is running on — the disk, then the binary's own
   * `--version`, then this side's record. Never a record made on another side.
   */
  readonly server: ServerStatus;
  /**
   * Which side that is, as VS Code's remote indicator names it (`WSL: Ubuntu`), or empty for a
   * local window — where there is one side and no need for a word for it.
   */
  readonly side: string;
  /**
   * Whether this side keeps its own settings.
   *
   * <p>Its own field rather than part of {@link CoaiSettings}, because it is the one setting that
   * must NOT be per side: it is the switch, shared by every side, and a switch that could differ
   * per side would let one side hold values another side cannot see.</p>
   */
  readonly perSide: boolean;
  readonly questions: readonly Escalation[];
  readonly sessions: readonly SessionFile[];
  /** Which collapsible sections are open. Empty falls back to {@link OPEN_BY_DEFAULT}. */
  readonly openSections: readonly string[];
  /**
   * Which ROUNDS are expanded, by {@link roundKey}.
   *
   * <p>Carried in the state for the same reason the sections are: this list is patched into the
   * page every five seconds while a round runs, and a `<details>` whose open state lived only in
   * the DOM would snap shut under the person mid-read. Raised as Blocking by this change's own
   * gate, which is the failure it would have shipped with.</p>
   */
  /** Every reviewer run the server has recorded, newest last. */
  readonly usage: readonly UsageEntry[];
  /** Which window the spending chart is showing. */
  readonly usageWindow: Window;
  /** The newest published server version, or empty while it is unknown or unreachable. */
  readonly latestServerVersion: string;
  /**
   * The local model engine on this machine, probed at repaint.
   *
   * <p>A local reviewer's model list is the only one that cannot be shipped: what is installed is a
   * fact about the machine the panel is running on. A vendor missing from this map has not been
   * probed yet, which is a different sentence from "nothing answered".</p>
   *
   * <p><b>Keyed by VENDOR id, not one for the panel.</b> It was a single engine, probed from
   * `vendors.find(v => v.runtime === 'local')` and handed to every card, so a second local reviewer
   * on another port displayed the first one's models and picking one sent a model that engine does
   * not have. Found by Claude Sonnet 5, 2026-09-02.</p>
   */
  readonly localEngines: Readonly<Record<string, LocalEngine>>;
  /**
   * What the CLAUDE.md snippet pasted into this workspace is, next to what this build hands out.
   *
   * <p>Only `older`, `unversioned` and `ahead` produce a line. A workspace that never adopted the
   * gate is not a problem to report, and one that is current has nothing to say.</p>
   */
  readonly snippetStatus: SnippetStatus;
  /**
   * The published list price per MODEL id, for the models the vendors are set to.
   *
   * <p>Shown as the rate fields' placeholder and used for the money when they are empty. It is a
   * LIST price, not a bill: reviews here run on a subscription, so this is what the tokens would
   * have cost through an API. Anything typed wins over it.</p>
   */
  readonly modelPrices: Readonly<Record<string, ModelPrice>>;
  /**
   * Each vendor's installed and published CLI version, by vendor id.
   *
   * <p>Absent, or both fields empty, is a legitimate answer — an offline machine, a CLI that is not
   * installed, a vendor this build has no official version source for — and it renders as a grey
   * button rather than as an error.</p>
   */
  readonly cliStatus: Readonly<Record<string, CliStatus>>;
}

/**
 * Nothing is open before anyone touches anything.
 *
 * <p>The panel opens as a list of headings that fits in one glance, and you expand what you came
 * for. Two sections open was still a wall on a narrow sidebar — and this is a panel you configure
 * once and afterwards only visit when something is waiting on you.</p>
 */
export const OPEN_BY_DEFAULT: readonly string[] = [];

export function panelHtml(state: PanelState, nonce: string, nowMs: number = Date.now()): string {
  const open = state.openSections.length === 0 ? OPEN_BY_DEFAULT : state.openSections;
  const body = [
    `<div id="live-questions">${questionsSection(state.questions)}</div>`,
    section('reviewers', 'Reviewers', open, reviewersBody(state)),
    section('prompts', 'Prompts per round', open, promptsBody(state)),
    section('gate', 'The gate', open, gateBody(state.settings)),
    section('limits', 'Limits', open, limitsBody(state.settings)),
    section('keys', 'Vendor keys', open, keysBody(state)),
    section('teamServers', 'Team servers', open, teamServersBody(state.teamServers ?? [])),
    section('side', 'This side', open, sideBody(state)),
    section('server', 'MCP server', open, serverBody(state)),
    section('rounds', 'Active rounds', open, `<div id="live-rounds">${roundsBody(state.sessions, nowMs)}</div>`),
  ].join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>${CSS}</style>
</head>
<body>
${body}
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  for (const el of document.querySelectorAll('[data-setting]')) {
    el.addEventListener('change', () => {
      const value = el.type === 'checkbox' ? el.checked : el.type === 'number' ? Number(el.value) : el.value;
      if (value === '__other__') {
        // Not a model — a request to type one; the input box comes from the provider side.
        vscode.postMessage({ type: 'command', command: 'customModel', id: el.dataset.vendor });
        return;
      }
      vscode.postMessage({ type: 'setting', key: el.dataset.setting, value,
                           vendor: el.dataset.vendor, role: el.dataset.role });
    });
  }
  for (const el of document.querySelectorAll('[data-prompt]')) {
    el.addEventListener('change', () =>
      vscode.postMessage({ type: 'prompt', role: el.dataset.prompt, round: Number(el.dataset.round), value: el.value }));
  }
  for (const el of document.querySelectorAll('.section')) {
    el.addEventListener('toggle', () =>
      vscode.postMessage({ type: 'section', id: el.dataset.section, open: el.open }));
  }
  for (const el of document.querySelectorAll('[data-command]')) {
    el.addEventListener('click', () =>
      vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
  }

  // Live updates arrive as HTML for the two regions that change on their own — the round in
  // flight, and any open escalation. Patching them leaves every other control ALONE,
  // which is the whole point: assigning the panel's html reloads the webview, and a reload
  // closes any open dropdown. This is what stopped the pickers snapping shut mid-choice.
  // What each region showed last. Identical markup is not re-applied: replacing it recreates
  // every element and drops the scroll position, and "nothing changed" is the common case on a
  // five-second tick.
  let lastQuestions = '';
  let lastRounds = '';
  window.addEventListener('message', (event) => {
    const message = event.data;
    if (message?.type !== 'live') {
      return;
    }
    const questions = document.getElementById('live-questions');
    const rounds = document.getElementById('live-rounds');
    if (questions !== null && typeof message.questions === 'string' && message.questions !== lastQuestions) {
      lastQuestions = message.questions;
      questions.innerHTML = message.questions;
    }
    if (rounds !== null && typeof message.rounds === 'string' && message.rounds !== lastRounds) {
      lastRounds = message.rounds;
      rounds.innerHTML = message.rounds;
    }
    // The answer buttons live inside the patched HTML, so they are re-bound here.
    for (const el of document.querySelectorAll('#live-questions [data-command]')) {
      el.addEventListener('click', () =>
        vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
    }
  });
</script>
</body>
</html>`;
}

/**
 * One collapsible section.
 *
 * <p><code>&lt;details&gt;</code> rather than a scripted accordion: keyboard-operable and
 * screen-reader-correct for free, and its arrow cannot get out of step with its own state. The
 * open set is carried in {@link PanelState} because the panel repaints on every change — a
 * section that snapped shut while somebody was typing in it would be worse than no collapsing.</p>
 */
function section(id: string, title: string, open: readonly string[], body: string): string {
  return `<details class="section sec-${id}" data-section="${id}"${open.includes(id) ? ' open' : ''}>
  <summary>${escapeHtml(title)}</summary>
${body}
</details>`;
}

/**
 * A visible line when a local reviewer points somewhere that is not this machine.
 *
 * <p>Visible, not a tooltip: the thing being warned about is that source code leaves the machine,
 * and a warning nobody hovers over is a warning nobody reads. Found by this product's own gate
 * reviewing the plan for this feature — the field was advertised for "a box on the network" with
 * nothing anywhere saying what that costs.</p>
 */
function remoteNotice(endpoint: string): string {
  const warning = remoteWarning(endpoint);

  return warning.length === 0 ? '' : `    <div class="stale remote">${escapeHtml(warning)}</div>`;
}

/** A published rate in the box, greyed as a placeholder — visibly not somebody's own number. */
function ratePlaceholder(perMillion: number | undefined): string {
  return perMillion === undefined || perMillion <= 0 ? '—' : String(perMillion);
}

/**
 * Where the number in the box came from, said out loud.
 *
 * <p>The distinction matters more than the number: a rate somebody typed is a fact about their
 * account, and a public list price is what these tokens would have cost through an API. The panel
 * must never let the second be mistaken for the first — which is the same reason the money it
 * produces keeps its tilde.</p>
 */
function rateNote(model: string, price: ModelPrice | undefined): string {
  if (price === undefined) {
    return `What this vendor bills per million tokens, in and out. No public list carries a price for ${model.length === 0 ? 'this model' : model}, so this one has to come from you.`;
  }
  const list = price.source === 'openrouter' ? "OpenRouter's model list" : "LiteLLM's public price file";

  return `Empty uses the published list price for ${model} — $${price.inPerMillion} in / $${price.outPerMillion} out per million, from ${list}. That is what these tokens would cost through an API, not what you were billed: reviews here run on your subscription. Type a rate to use your own, per field.`;
}

/**
 * What a screen reader says, which cannot be a colour.
 *
 * <p>The green is the fast signal and it is never the only one: the label and the tooltip both
 * carry the same fact in words.</p>
 */
function updateLabel(id: string, cli: CliStatus): string {
  return updateAvailable(cli.installed, cli.latest)
    ? `Update the ${id} CLI to ${cli.latest}`
    : `The ${id} CLI is up to date`;
}

function reviewersBody(state: PanelState): string {
  return `${state.vendors.map((v) => vendorCard(v, {
    codexModels: state.codexModels,
    agyModels: state.agyModels,
    cli: state.cliStatus[v.id] ?? UNKNOWN_CLI,
    price: state.modelPrices[v.model],
    localEngine: state.localEngines[v.id],
    allowedRemote: allowedModelsFor(v, state.teamServers ?? []),
    reported: state.providers?.reported ?? {},
  })).join('\n')}
<button class="add" data-command="addVendor" title="${escapeHtml(HELP.addVendor)}">＋&nbsp; Add a reviewer</button>`;
}

/**
 * The shipped vendors that reach their own service and need no base URL.
 *
 * <p>By ID rather than by runtime: `deepseek` and `openrouter` are `codex` too, and a vendor
 * somebody named themselves is `codex` by default — those all need the field. A vendor that is not
 * on this list is asked for an endpoint, which is the safe direction: an unnecessary empty box is
 * a smaller defect than a reviewer that cannot be configured.</p>
 */
const KNOWS_ITS_OWN_ENDPOINT: ReadonlySet<string> = new Set(['codex', 'claude', 'gemini', 'antigravity']);

/**
 * What a Team server still allows for one of its vendors.
 *
 * <p>Empty for every other runtime, and empty for a remote row whose server has not been asked yet
 * — a guessed list would let somebody pick a model that was never going to be accepted. The row's
 * own saved model is added back by `modelsFor`, marked, so a selection never silently vanishes.</p>
 */
/**
 * What a Team server allows, and whether the server has actually said so.
 *
 * <p>The two cannot be collapsed into a list length. When the catalog has not arrived this returns
 * the row's OWN model — never an empty list, because an empty one means "your server withdrew this
 * model" and `modelsFor` would mark it so, on every reload, for as long as a server stayed
 * unreachable. So a one-item list is ambiguous by construction, and the caption has to be told which
 * of the two it is looking at rather than counting.</p>
 *
 * <p>The shape is `RemoteProvenance`, declared beside the caption that reads it. It was declared
 * twice for one release — the same three fields, once here and once there — which is the beginning
 * of the drift this plan spent two stories closing on the other seam.</p>
 */
function allowedModelsFor(vendor: Vendor, servers: readonly TeamServerState[]): RemoteProvenance {
  if (vendor.runtime !== 'remote') {
    return { models: [], named: vendor.id, catalog: 'no-server' };
  }

  // By ID first, because an address is CORRECTABLE and an id is not: fixing a typo in a hostname
  // would otherwise orphan every row that matched the old string. The address is the fallback, for
  // rows written before the id was recorded — and it is compared CANONICALLY, never as typed, since
  // the row stores one spelling and the server entry holds whatever the person entered.
  const url = canonicalTeamServerUrl(vendor.baseUrl);
  const server = servers.find((s) => (vendor.teamServerId !== undefined && vendor.teamServerId.length > 0)
    ? s.server.id === vendor.teamServerId
    : canonicalTeamServerUrl(s.server.url) === url);
  // Guarded by `typeof`, not by `!== undefined`: a JSON null passes the second and then has
  // `.length` read off it, and this is a field a person can hand-write. Length-checked rather than
  // `??`, because nullish coalescing keeps an EMPTY string — a row carrying `remoteVendor: ''` would
  // be looked up in the catalog under a blank name. The C# side of this seam asks the same question
  // the same way (`VendorIdentity.Recorded`), and two halves of one contract disagreeing about what
  // counts as absent is how this whole plan started. Both raised on this story's rounds.
  const named = typeof vendor.remoteVendor === 'string' && vendor.remoteVendor.length > 0
    ? vendor.remoteVendor
    : vendor.id;

  // Three states, not two. "No server entry matches this row" is not "its catalog has not arrived
  // yet" — it is what a person is left with after removing a Team server and keeping its reviewers,
  // and sending them to a section that no longer lists their server is worse than saying nothing.
  if (server === undefined) {
    return { models: [vendor.model], named, catalog: 'no-server' };
  }

  if (server.catalog === undefined) {
    // NOT an empty allowlist. An empty one means "this server no longer offers your model", and
    // `modelsFor` would then mark every remote row's saved model as withdrawn — on every reload,
    // before the first fetch has landed, and for as long as a server stays unreachable. A person
    // would read that as their configuration having been dropped. Caught on the code round.
    return { models: [vendor.model], named, catalog: 'waiting' };
  }

  return {
    // Case-insensitively, because `RemoteProbe.Read` on the other side of this seam compares with
    // `OrdinalIgnoreCase`. A server whose catalog says `DeepSeek` answers a row that recorded
    // `deepseek` perfectly well, and this comparison showed that same row an empty dropdown and a
    // caption saying the server allows it nothing. Neither side may lower-case the name it SENDS —
    // that is the server's own spelling — but both must agree about which names are the same one.
    // Found by the automated reviewer on this change's pull request.
    models: (server.catalog.vendors ?? [])
      .find((v) => v.id.toLowerCase() === named.toLowerCase())?.models ?? [],
    named,
    catalog: 'here',
  };
}

/**
 * A reviewer the SERVER says it cannot run, named on its own card.
 *
 * <p>The whole point of this plan: a row could be enabled, ticked for both stages, and silently
 * absent from every round. The round now says so when one runs — this says so before one does.</p>
 *
 * <p><b>Three states, and only one of them draws anything.</b> `unknown` — the server was not asked,
 * could not answer, or did not mention this row — is silent, because a badge that lights up on a
 * failed probe is a badge that lies. The reason travels as the title, because "unavailable" is not
 * something a person can act on and "not signed in to the Team server at …" is.</p>
 */
function cannotRun(id: string, reported: Readonly<Record<string, ProviderHealth>>): string {
  if (availabilityOf(id, reported) !== 'unavailable') {
    return '';
  }
  const why = reported[id]?.note ?? '';

  // Where the verdict came from, said in the same breath. `coai-mcp --providers` reads the settings
  // file plus the environment of the process that asked it, and an MCP client's own `env` block
  // outranks that file key by key — so somebody whose client passes one can see that this reading is
  // not necessarily the running server's. Accepted finding, epic 3's code round; the alternative,
  // sharing the live server's environment, needs a channel into it that does not exist.
  const said = `${why} (as coai-mcp reads your settings file here)`;

  const label = escapeHtml(`${id} cannot review: ${said}`);

  return `<span class="badge cannot-run" title="${escapeHtml(said)}"`
    + ` aria-label="${label}">cannot review</span>`;
}

/**
 * Everything a card needs that is not the row itself: what was discovered, probed and priced.
 *
 * <p>One argument instead of seven. The list had reached eight parameters, at which point a reader
 * has to count commas to know which `readonly ModelChoice[]` is which — and every one of them is the
 * same kind of thing, a fact this panel went and found. Naming them is what makes the call site
 * legible, and an analyser was right to say so.</p>
 */
interface CardContext {
  readonly codexModels: readonly ModelChoice[];
  readonly agyModels: readonly ModelChoice[];
  readonly cli: CliStatus;
  readonly price: ModelPrice | undefined;
  readonly localEngine: LocalEngine | undefined;
  readonly allowedRemote: RemoteProvenance;
  /** What the SERVER says it can run — displayed, never re-decided here. */
  readonly reported: Readonly<Record<string, ProviderHealth>>;
}

/**
 * The endpoint field, and the CLI-path and price fields.
 *
 * <p>Lifted out of `vendorCard` because that function had reached a cognitive complexity of 32
 * against an allowed 15 — one function deciding what a card shows for six runtimes, three of which
 * hide different halves of it. These two are whole answers on their own and read better named.</p>
 *
 * <p>Who is asked for an endpoint: everybody except the shipped vendors that already know where
 * they go. It used to be "everybody with a baseUrl already set, plus local" — which hid the field
 * from the one preset whose entire purpose is to be given a base URL ("Another OpenAI-compatible
 * endpoint" ships with an empty one), so it could never be filled in. Found by Gemma4 26B,
 * 2026-09-02, and it is the only defect in that campaign no hosted model found.</p>
 */
function endpointField(vendor: Vendor, id: string, local: boolean, remote: boolean): string {
  return remote || (KNOWS_ITS_OWN_ENDPOINT.has(vendor.id) && vendor.baseUrl.length === 0)
      ? ''
      : `
  <div class="field">
    <input type="url" data-setting="baseUrl" data-vendor="${id}" title="${escapeHtml(local ? HELP.localEndpoint : HELP.vendorBaseUrl)}"
           placeholder="${local ? 'http://127.0.0.1:11434/v1 — empty uses whatever was found' : ''}"
           value="${escapeHtml(vendor.baseUrl)}">
    <div class="hint">${local
      ? 'Its OpenAI-compatible base, ending in <code>/v1</code>. Empty means the engine the probe found.'
      : `Its OpenAI-compatible endpoint. The key for it lives in the vault entry under <code>${id}</code>.`}</div>
${local ? remoteNotice(vendor.baseUrl) : ''}
  </div>`;

  // Shown for EVERY vendor, not only a custom endpoint. PATH is not always able to answer: in WSL
  // `codex` and `gemini` resolve to the WINDOWS npm shims through the interop PATH and die on a
  // missing Linux binary, and until this field existed nothing could point at the native one.
}

function runtimeFields(vendor: Vendor, id: string, local: boolean, remote: boolean, price: ModelPrice | undefined): string {
  return remote ? '' : `
  <div class="field">
    <input type="text" data-setting="executablePath" data-vendor="${id}" title="${escapeHtml(HELP.vendorExecutablePath)}"
           placeholder="CLI path — empty means look it up on PATH" value="${escapeHtml(vendor.executablePath)}">
  </div>
  <div class="field inline">
    ${labelled(`price-in-${id}`, '$ / 1M in', 'vendorPrice')}
    <input type="number" id="price-in-${id}" min="0" step="0.01" data-setting="pricePerMillionIn" data-vendor="${id}"
           value="${vendor.pricePerMillionIn === 0 ? '' : vendor.pricePerMillionIn}"
           placeholder="${ratePlaceholder(price?.inPerMillion)}" title="${escapeHtml(local ? HELP.localPrice : rateNote(vendor.model, price))}">
  </div>
  <div class="field inline">
    ${labelled(`price-out-${id}`, '$ / 1M out', 'vendorPrice')}
    <input type="number" id="price-out-${id}" min="0" step="0.01" data-setting="pricePerMillionOut" data-vendor="${id}"
           value="${vendor.pricePerMillionOut === 0 ? '' : vendor.pricePerMillionOut}"
           placeholder="${ratePlaceholder(price?.outPerMillion)}" title="${escapeHtml(local ? HELP.localPrice : rateNote(vendor.model, price))}">
  </div>`;
}

function vendorCard(vendor: Vendor, context: CardContext): string {
  const { codexModels, cli, price, localEngine, agyModels, allowedRemote, reported } = context;
  const id = escapeHtml(vendor.id);
  const local = vendor.runtime === 'local';
  // A Team server row is configured ON THE SERVER, not here: its endpoint is the server's address,
  // its CLI runs there, and its price is the company's subscription rather than this person's. Three
  // fields that could only be filled in wrongly.
  const remote = vendor.runtime === 'remote';
  const models = modelsFor(vendor.runtime, codexModels, vendor.model, localEngine, agyModels, allowedRemote.models);
  const endpoint = endpointField(vendor, id, local, remote);
  const executable = runtimeFields(vendor, id, local, remote, price);

  return `<div class="vendor">
  <div class="head">
    <input type="checkbox" id="v-${id}" data-setting="enabled" data-vendor="${id}"${vendor.enabled ? ' checked' : ''}
           title="${escapeHtml(HELP.vendorEnabled)}">
    <label class="name" for="v-${id}">${id}</label>${cannotRun(vendor.id, reported)}
    ${local ? `${(localEngine?.elsewhere ?? '').length > 0 ? `<button class="run get" data-command="fixWslNetwork" data-id="${id}"
            title="${escapeHtml(HELP.fixWslNetwork)}"
            aria-label="Switch WSL to mirrored networking">⇄</button>` : ''}<button class="run upd" data-command="reprobeLocal" data-id="${id}"
            title="${escapeHtml(HELP.reprobeLocal)}"
            aria-label="Look for local models again">⟳</button>` : `<button class="run" data-command="runVendor" data-id="${id}" title="${escapeHtml(HELP.runVendor)}"
            aria-label="Open ${id} in a terminal">▶</button>
    <button class="run get" data-command="installVendorCli" data-id="${id}" title="${escapeHtml(HELP.installVendorCli)}"
            aria-label="Install the ${id} CLI">⤓</button>
    <button class="run upd${updateAvailable(cli.installed, cli.latest) ? ' has-update' : ''}"
            data-command="updateVendorCli" data-id="${id}" title="${escapeHtml(cliStatusNote(vendor.id, cli))}"
            aria-label="${escapeHtml(updateLabel(vendor.id, cli))}">⟳</button>`}
    <button class="link" data-command="removeVendor" data-id="${id}">remove</button>
  </div>
  <div class="field">
    <select data-setting="model" data-vendor="${id}" title="${escapeHtml(local ? HELP.localModel : HELP.vendorModel)}">
      ${modelOptions(models, vendor.model, local ? 'whatever the engine answers with' : "the CLI's default")}
    </select>
    <div class="hint">${escapeHtml(vendor.runtime)} · ${escapeHtml(modelsProvenance(vendor.runtime, codexModels, localEngine, agyModels, allowedRemote))}</div>
  </div>
  <div class="field stages${vendor.enabled ? '' : ' off'}">
    <label class="check"><input type="checkbox" data-setting="plan" data-vendor="${id}"${vendor.plan ? ' checked' : ''}${vendor.enabled ? '' : ' disabled'}> reviews plans${help('vendorStages')}</label>
    <label class="check"><input type="checkbox" data-setting="code" data-vendor="${id}"${vendor.code ? ' checked' : ''}${vendor.enabled ? '' : ' disabled'}> reviews code</label>
  </div>${endpoint}${executable}
</div>`;
}


/**
 * One switch, and the words for the side it applies to.
 *
 * <p>The side is NAMED rather than implied. Somebody with a Windows window and two WSL distros is
 * about to keep three sets of settings, and the only way to be sure which one is being edited is to
 * read it off the panel that is editing it.</p>
 */
function sideBody(state: PanelState): string {
  const here = state.side.length === 0 ? 'this machine' : state.side;

  return `<div class="field">
  <div class="hint">One machine can hold several working environments — a local window, and each WSL
  distro or remote host. VS Code hands the same settings file to all of them, so this is what keeps
  them apart. Off, every window shares one set of settings, exactly as before.</div>
  <label class="check"><input type="checkbox" data-setting="perSideSettings"${state.perSide ? ' checked' : ''}> Separate settings for each side${help('perSideSettings')}</label>
  <div class="hint">This side is <b>${escapeHtml(here)}</b>. ${state.perSide
    ? 'It keeps its own vendors, models, proxies, CLI paths and vault key; your text size and help language stay shared.'
    : 'It shares its settings with every other side.'}</div>
</div>`;
}

function gateBody(s: CoaiSettings): string {
  // Rounds and threshold moved INTO each role's box, beside that role's prompts: they were two
  // sections describing one thing. What is left here is the one decision that belongs to neither
  // role nor stage \u2014 what to do when the rounds run out.
  return `<div class="field">
  ${labelled('onExhausted', 'When the rounds run out', 'onExhausted')}
  <select id="onExhausted" data-setting="onExhausted">
    <option value="human"${s.onExhausted === 'human' ? ' selected' : ''}>Ask a human</option>
    <option value="continue"${s.onExhausted === 'continue' ? ' selected' : ''}>Continue, and say so</option>
    <option value="good_enough"${s.onExhausted === 'good_enough' ? ' selected' : ''}>Good enough \u2014 take what\u2019s true and move on</option>
    <option value="escalate"${s.onExhausted === 'escalate' ? ' selected' : ''}>Climb the ladder</option>
  </select>
</div>
<div class="field">
  <div class="hint">These three do not change what the gate DECIDES. They are orders it hands back to
  whichever AI called it — how the work is broken up, when you are interrupted, which model does the
  expensive half. All three are off unless you turn them on.</div>
  <label class="check"><input type="checkbox" data-setting="autonomous"${s.autonomous ? ' checked' : ''}> Work autonomously${help('autonomous')}</label>
  <label class="check"><input type="checkbox" data-setting="splitPlan"${s.splitPlan ? ' checked' : ''}> Split the plan into epics and stories${help('splitPlan')}</label>
  <label class="check"><input type="checkbox" data-setting="splitWithFable"${s.splitWithFable ? ' checked' : ''}> Split with Fable, and give it the risky stories${help('splitWithFable')}</label>
</div>`;
}

function limitsBody(s: CoaiSettings): string {
  return `<div class="field inline">
  ${labelled('maxConcurrency', 'Reviewers at once', 'maxConcurrency')}
  <input type="number" id="maxConcurrency" min="1" data-setting="maxConcurrency" value="${s.maxConcurrency}">
</div>
<div class="field inline">
  ${labelled('maxPerProvider', 'Per vendor', 'maxPerProvider')}
  <input type="number" id="maxPerProvider" min="1" data-setting="maxPerProvider" value="${s.maxPerProvider}">
</div>
<div class="field inline">
  ${labelled('reviewerTimeoutMinutes', 'Reviewer timeout, minutes', 'reviewerTimeout')}
  <input type="number" id="reviewerTimeoutMinutes" min="1" data-setting="reviewerTimeoutMinutes" value="${s.reviewerTimeoutMinutes}">
</div>
<div class="field inline">
  ${labelled('escalationMinutes', 'Wait for you, minutes', 'escalationMinutes')}
  <input type="number" id="escalationMinutes" min="1" data-setting="escalationMinutes" value="${s.escalationMinutes}">
</div>`;
}

/**
 * The keys, and — first — whether they are needed at all.
 *
 * <p>Somebody reading "CredsForDevs config key" with codex and gemini configured has no way to
 * know the answer is "not yet". A field that cannot say whether it applies to you is a field that
 * gets filled in wrongly, so this one says it.</p>
 */
function keysBody(state: PanelState): string {
  const needy = state.vendors.filter((v) => v.enabled && v.baseUrl.length > 0);
  const field = `<div class="field">
  ${labelled('credsKey', 'CredsForDevs config key', 'credsKey')}
  <input type="text" id="credsKey" data-setting="credsKey" value="${escapeHtml(state.settings.credsKey)}"
         placeholder="${needy.length === 0 ? 'not needed yet' : 'the key from Enable Code Access…'}">
</div>`;

  if (needy.length === 0) {
    return `<div class="hint"><b>Nothing to fill in yet.</b> Every reviewer you have signs in through its own CLI, so none of them needs an API key. This becomes necessary when you add a vendor that has no CLI of its own — DeepSeek, OpenRouter, any endpoint you give a URL to.</div>
${field}`;
  }

  const names = needy.map((v) => escapeHtml(v.id)).join(', ');
  const one = needy.length === 1;
  return `<div class="hint">${names} ${one ? 'reaches an endpoint of its own, so it needs' : 'reach endpoints of their own, so they need'} an API key. Put the keys in ONE CredsForDevs entry of kind <code>config</code> — a JSON object keyed by vendor name — turn on <i>Enable Code Access…</i> for it, and paste the key it mints here.</div>
${field}`;
}

/**
 * The one sentence at the top of the Server section, for the side this panel is running on.
 *
 * <p><b>It names the side whenever there is one</b>, because a machine with a Windows window and a
 * WSL window has two servers and used to be described by one sentence that belonged to whichever
 * side pressed the button last. A local window says nothing about sides: there is only one.</p>
 *
 * <p>The `unknown` case is the pre-0.12.3 binaries, which answer `--version` with a refusal on
 * stderr. Saying so is the honest sentence — the alternative was calling them up to date.</p>
 */
export function serverSentence(server: ServerStatus, side: string): string {
  const here = side.length === 0 ? '' : ` in ${side}`;
  if (server.kind === 'absent') {
    return `coai-mcp is not installed${here.length === 0 ? ' yet' : here}.`;
  }
  if (server.kind === 'unknown') {
    // It says what IS, and leaves the action to the button. It used to end "— press Update", which
    // was a promise the section could not keep offline: with no published version there is no
    // button, and a sentence naming an action nobody can take is worse than a plain statement.
    return `A coai-mcp is installed${here}, but it cannot report its version.`;
  }

  return server.remembered
    ? `coai-mcp ${server.version} is installed${here} (from this side's own record — the binary could not be asked).`
    : `coai-mcp ${server.version} is installed${here}.`;
}

/**
 * What is installed, what is published, and a button when those differ.
 *
 * <p>The published version is shown even when it MATCHES, because "you are up to date" and "the
 * check never ran" look identical when only a mismatch is displayed — and this check silently
 * never ran at all for weeks, asking GitHub for the newest release of any kind and being handed
 * an extension tag.</p>
 */
function serverBody(state: PanelState): string {
  const installed = `<div class="status">${escapeHtml(serverSentence(state.server, state.side))}</div>`;
  const present = state.server.kind !== 'absent';

  // Three states, and the button appears in exactly one of them: something newer is published over
  // what is here, or nothing is here at all. `updateOffered` is false whenever the published version
  // could not be read, so no sentence in this section promises an action the section cannot offer —
  // which is what the "press Update" wording did offline, with no button under it.
  const published = state.latestServerVersion.length === 0
    ? '<div class="hint">The published version could not be read from GitHub just now.</div>'
    : present && !state.server.updateOffered
      ? `<div class="hint">${escapeHtml(state.latestServerVersion)} is the newest published — you are up to date.</div>`
      : `<div class="hint">${escapeHtml(state.latestServerVersion)} is published.</div>
<button class="add" data-command="installServer">⬇&nbsp; ${present ? 'Update' : 'Install'} coai-mcp ${escapeHtml(state.latestServerVersion)}</button>`;

  // The pasted snippet is the other half of this section: the server is installed here, and
  // the instruction that makes an AI USE it lives in somebody's CLAUDE.md, where it goes stale
  // silently. One line, and only when there is something to do about it.
  // The check itself, when it could not be made. Here rather than on a card, because it says
  // nothing about any one reviewer — a badge fed by a failed probe would be a badge that lies — and
  // because "asked and could not answer" is a fact about this binary. `asked: false` is silent: the
  // line above already says the server is absent. Four reviewers raised this on the plan round.
  const probe = state.providers !== undefined && state.providers.asked && !state.providers.answered
    ? '<div class="stale">The installed coai-mcp could not report its reviewers, so no card can say '
      + 'whether the server would run it. An update usually fixes it.</div>'
    : '';

  const snippet = snippetNote(state.snippetStatus);
  const stale = snippet.length === 0 ? '' : `<div class="stale">${escapeHtml(snippet)}</div>`;

  // This section is about coai-mcp and nothing else. It carried the Team server's address and
  // version too for a day (0.31.1–0.31.3), on the reasoning that "what am I talking to" is one
  // question with two answers — and in front of the operator it read as two subjects sharing a
  // box. A Team server is described where it is managed, under *Team servers*; a section titled
  // for one thing describes that thing.
  return `${installed}${stale}${probe}
${published}
<div class="hint">Changes here are saved for the server straight away; it reads them when your MCP client next starts it. The config block in the ⋯ menu is pasted once, when you first set it up.</div>
<button class="link" data-command="checkForUpdate">Check again</button>`;
}

function questionsSection(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const cards = questions
    .map((q) => {
      const findings = q.openFindings
        .map(
          (f) =>
            `<div class="finding">• ${escapeHtml(f.severity)} ${escapeHtml(f.category)} — ${escapeHtml(f.title)}</div>`,
        )
        .join('\n      ');
      return `<div class="question">
      <div>${escapeHtml(q.question)}</div>
      ${findings}
      <div class="meta">${escapeHtml(q.branch)}${q.translationNote ? ` · shown untranslated: ${escapeHtml(q.translationNote)}` : ''}</div>
      <button data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
    </div>`;
    })
    .join('\n');
  // Never collapsible: a blocked round is the one thing that must not be tidied away.
  return `<h2>A review is waiting on you</h2>\n${cards}`;
}

/**
 * What has run — and what is running RIGHT NOW, which is the question people actually have
 * during a ten-minute code gate.
 *
 * <p>A round in flight sorts first and lists its reviewers one by one, because "four of six
 * answered, two running" is the difference between waiting and being stuck. Until the server
 * began writing the round at its START, this panel could not tell those apart at all.</p>
 */
export 
/**
 * Which prompt each role uses on each round, beside that role’s rounds and threshold.
 *
 * <p>One row per round because that is the unit a person actually reasons about — "the second
 * round should look for something else" — and a single prompt per role could not express it.
 * The universal prompt is the default everywhere; a narrow lens is always a deliberate pick.</p>
 */
/** Which tone wraps each role. The colour is never the only signal — the name is always written. */
const ROLE_TONE: Record<string, string> = {
  PlanCritique: 'plan',
  Architecture: 'arch',
  SecurityReliability: 'sec',
  UxDxPerformance: 'uxdx',
};

/**
 * The fan-out, multiplied out loud.
 *
 * <p>"three reviewers per vendor, every round" made a reader ask whether each reviewer runs six
 * times. It does not: six is the number of REVIEWERS in a round — vendors times roles — each run
 * once. What looks like six runs is the panel itself: three roles, each with a picker per round.
 * So the sentence does the arithmetic in the numbers actually configured, and a disabled vendor is
 * not counted, because a reviewer that will not run is not one.</p>
 */
function fanOut(state: PanelState): string {
  const vendors = state.vendors.filter((v) => v.enabled).length;
  const reviewers = vendors * 3;
  // Derived, not stored: it is the widest CODE role's budget, and a stored copy would be a
  // second source of truth for a number that already exists.
  const rounds = Math.max(
    1,
    ...['Architecture', 'SecurityReliability', 'UxDxPerformance'].map((r) => state.settings.rounds[r] ?? 2),
  );
  return (
    `${vendors} vendor${vendors === 1 ? '' : 's'} × 3 roles = ${reviewers} reviewer${reviewers === 1 ? '' : 's'} ` +
    `per round, each runs once per round, up to ${rounds} round${rounds === 1 ? '' : 's'}`
  );
}

function promptsBody(state: PanelState): string {
  const s = state.settings;
  const roleRow = (role: (typeof ROLES)[number]): string => {
    // A role's own budget decides how many rounds it shows: a picker for a round that role will
    // never reach is a control that cannot do anything, which the spending tabs already taught.
    const budget = Math.max(1, Math.min(s.rounds[role.id] ?? 2, 6));
    const pickers = Array.from({ length: budget }, (_, i) => {
      const round = i + 1;
      const current = selectedFor(role.id, round, s.promptsPerRound);
      const options = promptsFor(role.id)
        .map(
          (p) =>
            `<option value="${escapeHtml(p.id)}"${p.id === current ? ' selected' : ''} title="${escapeHtml(p.purpose)}">${escapeHtml(p.label)}</option>`,
        )
        .join('');
      return `  <div class="field">
    <label for="pr-${escapeHtml(role.id)}-${round}">Round ${round}</label>
    <select id="pr-${escapeHtml(role.id)}-${round}" data-prompt="${escapeHtml(role.id)}" data-round="${round}">${options}</select>
  </div>`;
    }).join('\n');

    // The gate and the prompts were two sections describing one thing: how many times this role
    // asks, how much it may still find, and what it asks each time. One box now.
    return `<div class="role role-${ROLE_TONE[role.id] ?? 'plan'}">
  <div class="head"><span class="name">${escapeHtml(role.label)}</span></div>
  <div class="field inline">
    ${labelled(`rounds-${role.id}`, 'Rounds', 'maxRounds')}
    <input type="number" id="rounds-${role.id}" min="1" max="6" data-setting="rounds" data-role="${role.id}"
           value="${s.rounds[role.id] ?? 2}">
  </div>
  <div class="field inline">
    ${labelled(`threshold-${role.id}`, 'Passes at or under', 'gateThreshold')}
    <input type="number" id="threshold-${role.id}" min="0" data-setting="thresholds" data-role="${role.id}"
           value="${s.thresholds[role.id] ?? 3}">
  </div>
${pickers}
</div>`;
  };

  const plan = ROLES.filter((r) => r.stage === 'plan').map(roleRow).join('\n');
  const code = ROLES.filter((r) => r.stage !== 'plan').map(roleRow).join('\n');

  return `<div class="role-group">
  <div class="group-head">Plan stage</div>
  <div class="field">
    <label class="check"><input type="checkbox" data-setting="dealPlanLenses"${s.dealPlanLenses ? ' checked' : ''}> Deal the lenses across vendors</label>
    <div class="hint">Off: every vendor answers the same question, and two vendors agreeing on a finding is a fact the gate can use. On: every lens gets asked once instead, at half the launches \u2014 and that agreement is gone. Anything you pick below wins either way.</div>
  </div>
${plan}
</div>
<div class="role-group">
  <div class="group-head">Code stage \u2014 ${fanOut(state)}</div>
  <div class="field">
    <label class="check"><input type="checkbox" data-setting="dealCodeLenses"${s.dealCodeLenses ? ' checked' : ''}> Deal the roles across vendors</label>
    <div class="hint">Off: each of the three roles is asked of every vendor. On: the three roles are dealt out, one vendor each.</div>
  </div>
  <div class="field">
    ${labelled('codeWorkspace', 'What a reviewer gets', 'codeWorkspace')}
    <div class="seg" role="radiogroup" aria-label="What a reviewer gets">
      <label class="${s.codeWorkspace === 'none' ? 'on' : ''}"><input type="radio" name="codeWorkspace" data-setting="codeWorkspace" value="none"${s.codeWorkspace === 'none' ? ' checked' : ''}> Fast — diffs only</label>
      <label class="${s.codeWorkspace === 'worktree' ? 'on' : ''}"><input type="radio" name="codeWorkspace" data-setting="codeWorkspace" value="worktree"${s.codeWorkspace === 'worktree' ? ' checked' : ''}> Full — with the code</label>
    </div>
    <div class="hint">Fast sends the diff, the plan and this project’s rules — and nothing to explore. Measured on one commit: every hosted model found MORE that way, at a half to a third of the tokens. Full also hands them the checkout, for a review that needs the surrounding code.</div>
  </div>
  <div class="hint"><b>Architecture</b> round 1 defaults to <b>Conventions</b>: it judges the diff against the rules this project has written down \u2014 <code>CLAUDE.md</code>, <code>AGENTS.md</code>, <code>GEMINI.md</code>, <code>.claude/rules</code> \u2014 and nothing else. The other two roles spend their round on their own subject; pick <b>Conventions</b> for them if you want the rules read again. Anything you pick wins.</div>
${code}
</div>`;
}


/**
 * The spending itself, which advances while a round runs, so it travels as a patch rather than a
 * repaint: a repaint reloads the webview, and a reload closes whatever dropdown was open.
 */
export /**
 * What one vendor cost: what it billed, or what its rates say, or nothing.
 *
 * <p>Never both. A reported cost is the fact and an estimate beside it is noise; an estimate with
 * no rate behind it is a guess dressed as a number, so that stays a dash. The tilde is load-bearing
 * — it is the difference between what somebody charged and what we worked out.</p>
 */
function spend(row: { costUsd: number | null; estimatedUsd: number | null }): string {
  return row.costUsd !== null
    ? money(row.costUsd)
    : row.estimatedUsd !== null
      ? estimated(row.estimatedUsd)
      : '—';
}


/** The total, saying which half is billed and which is worked out. */
function total(billed: number | null, guessed: number | null): string {
  if (billed === null && guessed === null) {
    return '—';
  }
  if (guessed === null) {
    return money(billed);
  }

  return billed === null ? estimated(guessed) : `${money(billed)} + ${estimated(guessed)}`;
}

/**
 * The spending region on its own — what the provider patches when the window changes.
 *
 * <p>Today/Week/Month/Year is arithmetic over rows the extension already holds. It used to go
 * through the full repaint — which stats the server binary, probes every vendor CLI, asks GitHub
 * what is published and fetches two price tables — so choosing a window took seconds for a sum.</p>
 */
export function usageRegion(
  usage: readonly UsageEntry[],
  window: Window,
  vendors: readonly Vendor[],
  prices: Readonly<Record<string, ModelPrice>>,
  teamServers: readonly TeamServerState[] = [],
  usageScope: 'me' | 'company' = 'me',
): string {
  // What each Team server says was spent ON IT, under this machine's own totals. The server keeps
  // that ledger — a review that ran there left no line in this machine's — so the two are shown
  // beside each other rather than summed into a number neither of them holds.
  const team = teamServers
    .filter((s) => s.email.length > 0)
    .map((s) => teamUsageBlock(s, shortNumber))
    .join('\n');
  const scope = usageScopeControl(teamServers, usageScope);
  const rows = totalsByVendor(within(usage, window, new Date()), vendors, (modelId) => prices[modelId]);
  if (rows.length === 0) {
    return `<div class="empty">Nothing recorded on this machine in this window yet.</div>
${team}${scope}`;
  }

  const busiest = Math.max(...rows.map((r) => r.tokensIn + r.tokensOut));
  const cards = rows
    .map((r) => {
      const total = r.tokensIn + r.tokensOut;
      const failed = r.failed === 0 ? '' : ` · <span class="warn">${r.failed} failed</span>`;
      return `<div class="spend">
  <div class="head"><span class="name" style="color:${vendorColour(r.provider)}">${escapeHtml(r.provider)}</span><span class="cost">${spend(r)}</span>
    <button class="link forget" data-command="forgetUsage" data-id="${escapeHtml(r.provider)}"
            title="Clear ${escapeHtml(r.provider)}'s recorded runs from this chart. Nothing is deleted from the ledger — the row simply stops counting what is already there, and comes back the next time this vendor runs."
            aria-label="Forget ${escapeHtml(r.provider)}'s recorded spending">✕</button></div>
  <div class="bar"><span style="width:${barWidth(total, busiest)}%"></span></div>
  <div class="figures">${shortNumber(r.tokensIn)} in · ${shortNumber(r.tokensOut)} out · ${r.runs} run(s)${failed}</div>
  <div class="hint">${shortDuration(r.seconds)} total · ${shortDuration(r.averageSeconds)} average</div>
</div>`;
    })
    .join('\n');

  const all = rows.reduce(
    (t, r) => ({
      tokens: t.tokens + r.tokensIn + r.tokensOut,
      cost: r.costUsd === null ? t.cost : (t.cost ?? 0) + r.costUsd,
      // Kept apart from `cost` all the way to the end: a total that mixes what a vendor billed with
      // what we worked out from a rate somebody typed is a number nobody can check.
      guess: r.estimatedUsd === null ? t.guess : (t.guess ?? 0) + r.estimatedUsd,
      seconds: t.seconds + r.seconds,
    }),
    { tokens: 0, cost: null as number | null, guess: null as number | null, seconds: 0 },
  );

  return `${cards}
<div class="hint total">All vendors: ${shortNumber(all.tokens)} tokens · ${total(all.cost, all.guess)} · ${shortDuration(all.seconds)}</div>`;
}

/**
 * The rounds that are RUNNING, newest first — and nothing else.
 *
 * <p>This section used to be a 72-hour history of finished rounds, each a disclosure that opened
 * to its reviewers, with a policy for who had opened what and a document-level toggle listener to
 * report it. That is where the flicker lived: a list replaced through innerHTML every five seconds
 * fires `toggle` for every open card exactly as a click does, and the provider answered each with
 * another patch. Ruled on 2026-09-05: the sidebar answers "what is happening now"; everything that
 * has happened is a log, and a log is a page with a table. A running round is shown whole, because
 * its reviewers are what somebody is waiting on; a finished one is not shown at all.</p>
 */
export function roundsBody(sessions: readonly SessionFile[], nowMs: number = Date.now()): string {
  const running = sessions
    .flatMap((s) => s.rounds.map((r) => ({ branch: s.state.branch, ...r })))
    .filter(isRunning)
    .sort((a, b) => (b.startedUtc ?? '').localeCompare(a.startedUtc ?? ''));
  if (running.length === 0) {
    return '<div class="empty">Nothing is running. Every round, finished or not, is in <b>Show review rounds</b>.</div>';
  }

  return running.map((r) => roundCard(r, nowMs)).join('\n');
}

/**
 * A round's identity across repaints — branch, stage, number and the instant it started.
 *
 * <p>Four fields rather than the round number: two rounds of the same number on two branches are
 * two rounds, and a re-run is a new one rather than the old one back.</p>
 */
export function roundKey(round: RoundRecord & { branch: string }): string {
  return `${round.branch}|${round.stage}|${round.number}|${round.startedUtc}`;
}

/** One running round, whole: what it is, how far it has got, and every reviewer's line. */
function roundCard(round: RoundRecord & { branch: string }, nowMs: number): string {
  // Only the vendor's WORD carries the colour; the rest of the row is exactly as it was. Both
  // halves come out of a session file somebody else wrote, so both are escaped.
  const reviewers = reviewerRows(round)
    .map((row) =>
      `<div class="reviewer"><span class="who" style="color:${vendorColour(row.provider)}">`
      + `${escapeHtml(row.provider)}</span>${escapeHtml(row.rest)}</div>`)
    .join('\n');
  const took = elapsed(round, nowMs);
  // WHAT is being reviewed leads the line; the branch and the round number follow it.
  const subject = (round.subject ?? '').length > 0
    ? `<div class="subject">${escapeHtml(round.subject!)}</div>`
    : '';
  // Three lines, not one: the sidebar is narrow, and one line with the stage, the branch and the
  // badge was cut off with an ellipsis exactly where the branch name got interesting.
  const head = `${subject}<div class="line">${escapeHtml(stageName(round.stage))} ${round.number}</div>`
    + `<div class="line branch">${escapeHtml(round.branch)}</div>`
    + `<div class="line"><span class="badge running">running</span> · ${round.gatingCount} gating</div>`
    + `<div class="usage">${took.length > 0 ? `${escapeHtml(took)} · ` : ''}${escapeHtml(costPhrase(round))}</div>`;

  return `<div class="round" data-round="${escapeHtml(roundKey(round))}">${head}
${reviewers}</div>`;
}


/**
 * The two regions the provider may patch without reloading the webview — the round in flight and
 * a waiting question. Everything else on the panel is a control, and a control only changes when
 * the person changes it.
 */
export function liveRegions(state: PanelState, nowMs: number = Date.now()): { questions: string; rounds: string } {
  return {
    questions: questionsSection(state.questions), rounds: roundsBody(state.sessions, nowMs) };
}

/**
 * A model list as SELECT options: the default first, every known model, the saved value
 * kept even when unknown, and "another model…" as the way out of the list.
 *
 * <p>A select rather than a datalist, learned the hard way: a datalist FILTERS its options by the
 * field's current value, so the moment a model was chosen every other option vanished and the
 * picker read as broken.</p>
 */
function modelOptions(models: readonly ModelChoice[], current: string, emptyLabel: string): string {
  const known = models
    .map((m) => `<option value="${escapeHtml(m.id)}"${m.id === current ? ' selected' : ''}>${escapeHtml(m.label)}</option>`)
    .join('\n      ');
  return `<option value=""${current === '' ? ' selected' : ''}>${emptyLabel}</option>
      ${known}
      <option value="__other__">another model…</option>`;
}

/**
 * The little "?" that explains a setting on hover.
 *
 * <p>A native `title` rather than a scripted popup: it works with the keyboard, it cannot escape
 * the webview, and it needs no state of its own.</p>
 */
export function help(key: HelpKey): string {
  return `<span class="help" title="${escapeHtml(HELP[key])}" role="img" aria-label="What this means">?</span>`;
}

/** A label with its explanation beside it. */
function labelled(forId: string, text: string, key: HelpKey): string {
  return `<label for="${forId}">${help(key)}${escapeHtml(text)}</label>`;
}

/** Every value here came from a file or a person; none of it may become markup. */
// Re-exported so every existing importer keeps working; the function itself lives in its own module
// now, because `teamServerView` also builds markup and importing it from here would be a cycle —
// which is exactly why a private fourth copy appeared. See `escapeHtml.ts`.
export { escapeHtml } from './escapeHtml';

const CSS = `
  /* Everything measures its own border and padding: without this a 100% field plus padding is
     wider than its parent, and the whole view gains a horizontal scrollbar. */
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: transparent;
    /* Air down both sides: nothing should touch the edge of the view, and the right-hand gap is
       a shade wider so a field never sits under the scrollbar's track. */
    margin: 0; padding: 4px 14px 20px 12px; overflow-x: hidden;
  }
  h2 {
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .75;
    margin: 10px 0 6px; font-weight: 600;
  }
  .section { border-top: 1px solid var(--vscode-panel-border); padding: 0 0 8px; }
  .section > summary {
    /* .9 rather than .75: coloured text at .75 on a dark ground is muddy, and the colour is
       doing the separating now. */
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .9;
    font-weight: 600; padding: 10px 0 6px; cursor: pointer; list-style: none;
    display: flex; align-items: center; gap: 6px; user-select: none;
  }
  .section > summary:hover { opacity: 1; }
  /* Each header that opens carries its own tone, from the same palette the role boxes use, so a
     column of eight identical grey words becomes something you can aim at. The chevron follows for
     free: it is drawn from currentColor.

     Colour is never the only signal — every heading is also its own word — and each tone is a
     --vscode-charts-* token with a hex fallback, so a theme that redefines the charts palette
     moves these with it. */
  .sec-reviewers > summary { color: var(--tone-arch); }
  .sec-prompts   > summary { color: var(--tone-plan); }
  .sec-gate      > summary { color: var(--tone-sec); }
  .sec-limits    > summary { color: var(--tone-limits); }
  .sec-keys      > summary { color: var(--tone-keys); }
  .sec-side      > summary { color: var(--tone-keys); }
  /* The two stage boxes sit on one line under the model: they are one decision about this vendor. */
  .vendor .stages { display: flex; gap: 12px; flex-wrap: wrap; }
  /* A vendor that is off everywhere: the boxes stay visible, so their state is readable, and go
     inert, so nobody ticks one expecting it to mean something. The gate said the contradiction was
     the defect - not the boxes themselves. */
  .vendor .stages.off { opacity: .55; }
  .sec-server    > summary { color: var(--tone-uxdx); }
  .sec-usage     > summary { color: var(--tone-arch); }
  .sec-rounds    > summary { color: var(--tone-plan); }
  .section > summary::-webkit-details-marker { display: none; }
  /* A real chevron, drawn from two borders rather than borrowed from punctuation: it matches the
     Explorer's weight, scales with the text, and points the right way in both states with no
     script. (A '›' glyph rendered a third of this size — a disclosure arrow nobody can hit.) */
  .section > summary::before {
    content: ''; flex: 0 0 auto; width: 6px; height: 6px; margin: 0 4px 0 2px;
    border-right: 1.5px solid currentColor; border-bottom: 1.5px solid currentColor;
    transform: rotate(-45deg); transition: transform .12s ease;
  }
  .section[open] > summary::before { transform: rotate(45deg); margin-top: -3px; }
  .field { margin: 8px 0; }
  .field > label { display: block; margin-bottom: 3px; }
  .inline { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  /* A two-position switch: Fast on the left, Full on the right, the chosen half lit. */
  .seg { display: flex; border: 1px solid var(--vscode-widget-border, #3c3c3c); border-radius: 4px; overflow: hidden; }
  .seg label { flex: 1; display: flex; align-items: center; justify-content: center; gap: 6px;
               padding: 4px 8px; cursor: pointer; font-size: 12px; }
  .seg label.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
  .seg input { margin: 0; }
  .inline > label { flex: 1 1 auto; min-width: 0; margin-bottom: 0; }
  .inline > input[type="number"] { flex: 0 0 64px; width: 64px; }
  .hint { opacity: .65; font-size: 11px; margin: 3px 0 0; line-height: 1.45; }
  input[type="text"], input[type="url"], input[type="number"], select {
    background: var(--vscode-input-background); color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px;
    padding: 3px 6px; font-family: inherit; font-size: inherit;
    width: 100%; max-width: 100%; min-width: 0;
  }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 10px; cursor: pointer;
    font-family: inherit; font-size: inherit; width: 100%; margin: 6px 0 0;
  }
  button:hover { background: var(--vscode-button-hoverBackground); }
  button.add { margin-top: 10px; padding: 7px 10px; font-weight: 600; }
  button.link {
    background: none; color: var(--vscode-textLink-foreground); padding: 0; width: auto;
    margin: 0; text-decoration: underline; font-size: 11px;
  }
  button.link:hover { background: none; color: var(--vscode-textLink-activeForeground); }
  .help {
    display: inline-block; width: 14px; height: 14px; line-height: 14px; margin-right: 6px;
    text-align: center; border-radius: 50%; font-size: 10px; font-weight: 700; cursor: help;
    background: var(--vscode-badge-background); color: var(--vscode-badge-foreground);
    opacity: .8; flex: 0 0 auto;
  }
  .help:hover { opacity: 1; }
  .vendor {
    border: 1px solid var(--vscode-panel-border);
    border-radius: 3px; padding: 8px; margin: 8px 0;
  }
  .vendor .head { display: flex; align-items: center; gap: 6px; }
  .vendor .name { font-weight: 600; flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
  .vendor input[type="checkbox"] { flex: 0 0 auto; margin: 0; }
  /* The play button sits between the name and remove, centred in the gap they leave. */
  /* The install button sits beside ▶ and is deliberately quieter: it is the thing you press
     once, on a machine that does not have the CLI yet. */
  .vendor .head .get { font-size: 11px; }
  .vendor .head .run {
    flex: 0 0 auto; width: auto; margin: 0 auto; padding: 1px 8px; line-height: 1.2;
    background: none; color: var(--vscode-charts-green); font-size: 13px;
    border: 1px solid transparent; border-radius: 3px;
  }
  .vendor .head .run:hover {
    background: var(--vscode-toolbar-hoverBackground); border-color: var(--vscode-charts-green);
  }
  /* The update button says, by its colour, whether there is anything to do — which is the question
     somebody actually has, and the one they used to answer by leaving the panel. Grey is the
     resting state AND the "could not tell" state: a button that lights up because a fetch failed
     would be worse than one that never lights up.

     These sit AFTER the .run:hover rule on purpose. They have the same specificity as it, so
     earlier they lost: .run:hover paints a green border, hovering is how a tooltip gets read, and every
     up-to-date button therefore turned green the moment anybody looked at it. Reported against
     0.20.0 within the hour. */
  .vendor .head .upd { font-size: 12px; color: var(--vscode-descriptionForeground); }
  .vendor .head .upd:hover { border-color: var(--vscode-descriptionForeground); background: none; }
  .vendor .head .upd.has-update { color: var(--vscode-charts-green); font-weight: 600; }
  .vendor .head .upd.has-update:hover {
    border-color: var(--vscode-charts-green); background: var(--vscode-toolbar-hoverBackground);
  }
  .question {
    border-left: 3px solid var(--vscode-inputValidation-warningBorder);
    background: var(--vscode-inputValidation-warningBackground);
    padding: 8px 10px; margin: 8px 0; border-radius: 0 3px 3px 0;
  }
  .question .meta { opacity: .7; font-size: 11px; margin-top: 4px; }
  .finding { font-size: 11px; opacity: .85; margin: 3px 0 0 8px; }
  .verdict {
    font-family: var(--vscode-editor-font-family); font-size: 11px; margin: 2px 0;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }
  .usage { font-size: 11px; opacity: .7; margin: 0 0 4px; }
  /* Three days of rounds is a list of unknown length inside a sidebar section, so it scrolls in
     place rather than pushing every section below it off the bottom of the panel. */
  /* Twice what it was. A sidebar is usually far taller than 320px, so five rounds filled the list
     and everything else sat behind a scrollbar in a panel with room to spare. */
  #live-rounds { max-height: 640px; overflow-y: auto; }
  /* A running round is a block: nothing to open, so nothing to point at. */
  .round { margin: 0 0 4px; }
  .round .subject { font-weight: 600; }
  .round .line { white-space: normal; overflow-wrap: anywhere; }
  .round .branch { font-family: var(--vscode-editor-font-family); font-size: .92em; opacity: .85; }
  /* A hand promises something opens. Only the cards that DO open show one — the flat ones are a
     line, not a control, and offering a hand over them is the panel telling a small lie. */
  /* Shown for the moment between the click and the reviewers arriving. The body is built by the
     provider, so there is always a gap; an empty card during it reads as a card with nothing in it. */
  .reviewer { font-size: 11px; opacity: .85; margin: 1px 0 1px 8px; }
  .badge { padding: 0 5px; border-radius: 8px; font-size: 10px; font-weight: 600; }
  .badge.running { background: var(--vscode-charts-green); color: var(--vscode-editor-background); }
  /* The editor's own error colour, so it reads as a problem in every theme rather than in one. */
  .badge.cannot-run {
    background: var(--vscode-inputValidation-errorBackground);
    color: var(--vscode-inputValidation-errorForeground, var(--vscode-foreground));
    border: 1px solid var(--vscode-inputValidation-errorBorder);
    margin-left: 6px;
  }
  .badge.stopped {
    background: var(--vscode-inputValidation-warningBorder);
    color: var(--vscode-editor-background);
  }
  /* The role palette, taken from the sibling product's own token set
     (creds/src_vs_code/src/entityFormStyles.ts): a charts token with the hex it falls back to, so a
     theme that defines them wins and one that does not still gets the intended colour. */
  :root {
    --tone-plan: var(--vscode-charts-purple, #c586c0);
    --tone-arch: var(--vscode-charts-blue, #569cd6);
    --tone-sec: var(--vscode-charts-orange, #ce9178);
    --tone-uxdx: var(--vscode-charts-green, #b5cea8);
    --tone-limits: var(--vscode-charts-yellow, #d7ba7d);
    --tone-keys: var(--vscode-charts-red, #f14c4c);
    --tone-code: var(--vscode-widget-border, #454545);
  }
  .role-group { border: 1px solid var(--vscode-widget-border); border-radius: 4px; padding: 6px 8px 2px; margin: 0 0 10px; }
  .group-head { font-size: 11px; font-weight: 600; opacity: .8; margin: 0 0 6px; }
  /* A left edge rather than a filled box: it marks the role at a glance without turning the
     settings panel into four coloured slabs, and it survives a light theme unchanged. */
  .role { border: 1px solid var(--vscode-widget-border); border-left: 3px solid var(--tone-plan);
          border-radius: 3px; padding: 6px 8px 2px; margin: 0 0 8px; }
  .role .head { margin: 0 0 4px; }
  .role .name { font-weight: 600; }
  .role-plan { border-left-color: var(--tone-plan); }
  .role-arch { border-left-color: var(--tone-arch); }
  .role-sec { border-left-color: var(--tone-sec); }
  .role-uxdx { border-left-color: var(--tone-uxdx); }
  .role-code { border-left-color: var(--tone-code); }
  .tabs { display: flex; gap: 4px; margin: 0 0 8px; }
  .tab { flex: 1; padding: 3px 6px; font: inherit; color: var(--vscode-foreground);
         background: var(--vscode-editorWidget-background); border: 1px solid var(--vscode-widget-border);
         border-radius: 3px; cursor: pointer; }
  .tab:hover { background: var(--vscode-toolbar-hoverBackground); }
  .tab.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground);
            border-color: var(--vscode-button-background); }
  .tab.on:hover { background: var(--vscode-button-hoverBackground); }
  /* The spending card. It was called .usage, which the rounds section had already claimed for its
     own line - so opacity .7 from a rule written for something else dimmed every card, and the
     .hint inside it to .7 x .65. Nothing was broken; the whole section just read as disabled.
     A name each. */
  .spend { margin: 0 0 12px; }
  .spend .head { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
  .spend .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; }
  /* The money is the quiet half of the row: a dash where a vendor does not price its own runs. */
  .spend .cost { font-size: 11px; opacity: .75; flex: 0 0 auto; }
  .spend .forget { flex: 0 0 auto; padding: 0 2px; font-size: 11px; opacity: .55; }
  /* A stale pasted snippet is worth noticing and not worth alarming about: the gate still
     works, the AI reading it is just being told an older story. */
  .stale {
    border-left: 3px solid var(--tone-limits); padding: 6px 8px; margin: 6px 0;
    font-size: 11px; background: var(--vscode-textBlockQuote-background);
  }
  .spend .forget:hover { opacity: 1; color: var(--tone-keys); }
  /* The tokens are what the section is FOR, so they are read at full strength; the durations
     underneath stay a .hint, because they are context rather than the answer. */
  .spend .figures { font-size: 11px; margin: 3px 0 0; line-height: 1.45; }
  .bar { height: 6px; background: var(--vscode-editorWidget-background); border-radius: 3px;
         overflow: hidden; margin: 4px 0 2px; }
  .bar span { display: block; height: 100%; background: var(--vscode-button-background); }
  .warn { color: var(--vscode-editorWarning-foreground); }
  .total { margin-top: 8px; border-top: 1px solid var(--vscode-widget-border); padding-top: 6px; }
  .subject { font-weight: 600; margin: 6px 0 1px; }
  .empty { opacity: .6; font-style: italic; margin: 6px 0; }
  .status { margin: 2px 0 0; }
`;

/**
 * What the panel paints on: a key over the state that a REPAINT would change.
 *
 * <p>A repaint reloads the webview, which closes any dropdown that was open, so it is reserved
 * for the person's own doing. Everything that moves by itself travels through
 * {@link liveRegions} instead.</p>
 *
 * <p><b>Anything left out of this key is a control that can never change.</b> The spending
 * window was: clicking Today, Month or Year recorded the choice, produced an identical key, and
 * repainted nothing — so the section sat on Week for good, and the buttons read as broken
 * because they were.</p>
 */
/**
 * Every command a control in this panel can post.
 *
 * <p>It exists because the Update button did nothing for a day. The markup emitted
 * `data-command="installServer"`, the provider's switch had no case for it, and the click fell
 * into `default: return` — no error, no notification, no log. A button wired to nothing looks
 * exactly like a button whose work failed silently.</p>
 *
 * <p>The list is declared HERE, beside the markup that emits it, and the provider switches over
 * this type with an exhaustiveness check: a command added here without a case is a COMPILE error,
 * not a dead button. The test below adds the other half — a `data-command` in the markup that is
 * not in this list.</p>
 */
export const PANEL_COMMANDS = [
  'answer',
  'addVendor',
  'removeVendor',
  'runVendor',
  'installServer',
  'checkForUpdate',
  'usageWindow',
  'installVendorCli',
  'updateVendorCli',
  'forgetUsage',
  'reprobeLocal',
  // Only rendered when the probe SAW an engine on the Windows side that this WSL distro cannot
  // reach; on every other machine the button does not exist, because there is nothing to fix.
  'fixWslNetwork',
  // Posted by the model picker rather than by a button: "another model…" is a request to type one.
  'customModel',
  // Team servers. Each is a button in the section above, and the provider's switch is checked for
  // exhaustiveness — a command added here without a case is a COMPILE error, not a dead button.
  'addTeamServer',
  'signInTeamServer',
  'signOutTeamServer',
  'removeTeamServer',
  // The Company/Me control on the spending section, which only an admin is shown. Without it that
  // control would be a button wired to nothing, which is the exact trap this list exists to prevent.
  'teamUsageScope',
] as const;

export type PanelCommand = (typeof PANEL_COMMANDS)[number];

/**
 * Panel commands that are only a request to run a command the extension registered, by id.
 *
 * <p>Named here rather than typed at the call site because the exhaustiveness check proves a
 * `case` EXISTS, not that it invokes the right thing: a typo in the id would compile, pass every
 * guard, and reproduce the original silence exactly. A test holds these ids against the manifest's
 * own contributed commands, which is the only check that can catch that.</p>
 */
export const VSCODE_COMMAND_FOR = {
  installServer: 'coai.installServer',
} as const satisfies Partial<Record<PanelCommand, string>>;

export function isPanelCommand(value: string | undefined): value is PanelCommand {
  return value !== undefined && (PANEL_COMMANDS as readonly string[]).includes(value);
}

export function staticKey(state: PanelState): string {
  return JSON.stringify([
    state.settings,
    state.vendors,
    state.codexModels,
    // The local model list belongs here for the same reason every other list does: it CHANGES —
    // somebody starts Ollama, pulls a model, presses the reprobe button. Left out, the picker was
    // frozen for the life of the panel while the probe underneath it worked perfectly.
    state.localEngines,
    state.server,
    state.side,
    // Rare, and both are a person's doing or an answer they asked for.
    state.latestServerVersion,
    state.usageWindow,
    state.openSections,
    // The Team-server rows, their slot lines and the usage scope reach the screen by being HERE.
    // Left out, the section would be frozen for the life of the panel while the fetch underneath it
    // worked perfectly — which is exactly what happened to the local model list.
    state.teamServers,
    state.usageScope,
  ]);
}
