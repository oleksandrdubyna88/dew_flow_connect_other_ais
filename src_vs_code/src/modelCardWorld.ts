import { type CliStatus, cliStatusNote, updateAvailable } from './cliVersions';
import { MODEL_CHECK } from './consultantCheckRun';
import { checkStateOnThisDisk, type CheckRecord, type HealthFile } from './consultantHealth';
import type { ConsultantHealthState } from './consultantHealthState';
import { escapeHtml } from './escapeHtml';
import { CONSULTING } from './featureAvailability.generated';
import { markerLine } from './kimiMarker';
import { remoteWarning } from './localEngines';
import { CLI_RUNTIMES, type Runtime } from './models';
import { help } from './panelControls';
import { type CardContext, endpointButton, headButtons } from './panelView';
import { availabilityOf } from './providers';
import { consultantOnServer, consultantRuntimeNote } from './runtimeSince';
import type { TeamServerState } from './teamServerView';
import type { Vendor } from './vendors';

/**
 * The parts of a Models card that face the world (research/PLAN_one_model_catalog.md E3.3) — each one what the current
 * card, the Consultant tab or the Setup tab already calls: the CLI's buttons and version, coai-mcp's verdict, the
 * ✓ Check's durable state, an endpoint off this machine, a Team server that cannot take what the row sets.
 */

/** What the card says about the row's last ✓ Check — read from the durable record coai-mcp keeps as `model-<id>`. */
export interface CheckFacts {
  readonly checking: boolean;
  /** The badge's words; '' draws no badge (the health is not being read, the tab is not showing). */
  readonly said: string;
  readonly tone: 'ok' | 'warn' | 'err' | 'busy';
}

const NOT_READ: CheckFacts = { checking: false, said: '', tone: 'warn' };

/** How a judged check reads on a card. */
const JUDGED: Readonly<Record<string, (reason: string) => CheckFacts>> = {
  answered: () => ({ checking: false, said: 'checked: it answered', tone: 'ok' }),
  checking: () => ({ checking: true, said: 'checking…', tone: 'busy' }),
  failed: (reason) => ({ checking: false, said: `checked: no answer${reason.length > 0 ? ` — ${reason}` : ''}`, tone: 'err' }),
};

/**
 * An answer from a row that asked to stream says whether it came as one (research/PLAN_api_streaming.md, Story C): a gateway
 * that ignored the request, or a coai-mcp too old to read the switch, answers all the same — so "answered" alone would
 * hide that the setting did nothing.
 */
const STREAMED: Readonly<Record<string, CheckFacts>> = {
  'streamed': { checking: false, said: 'checked: it answered · streamed', tone: 'ok' },
  'not-streamed': { checking: false, said: 'checked: it answered, but NOT streamed — the endpoint or this coai-mcp ignored the switch', tone: 'warn' },
};

function judged(record: CheckRecord, nowMs: number): CheckFacts {
  const check = checkStateOnThisDisk(record, nowMs);
  const streamed = check.state === 'answered' ? STREAMED[record.streamed] : undefined;
  if (streamed !== undefined) {
    return streamed;
  }

  return (JUDGED[check.state] ?? ((reason) => ({ checking: false, said: `checked: ${check.state}${reason.length > 0 ? ` — ${reason}` : ''}`, tone: 'warn' })))(reasonOf(check.reason, record));
}

/**
 * Why the check ended as it did: its stated reason, else — for a failed check, which records its classification and no
 * reason — what coai-mcp classified it as. Without it the card said a bare "checked: no answer" while the record said why
 * (research/PLAN_local_effort_reaches_every_local_launch.md, S1.3).
 */
function reasonOf(reason: string, record: CheckRecord): string {
  return reason.length > 0 ? reason : record.failureWhat;
}

function fromFile(file: HealthFile<CheckRecord> | undefined, nowMs: number): CheckFacts {
  if (file === undefined) {
    return { checking: false, said: 'not checked yet', tone: 'warn' };
  }

  return file.kind === 'found' ? judged(file.value, nowMs) : { checking: false, said: 'the last check could not be read', tone: 'warn' };
}

/**
 * The row's ✓ Check, as it stands: a run this window started is "checking…" at once; otherwise the durable record —
 * which a reload reads back, and which the binary's own sweep settles after a crash (CLAUDE.md §8).
 */
export function checkFactsOf(health: ConsultantHealthState | undefined, id: string): CheckFacts {
  const key = `${MODEL_CHECK}${id}`;
  if (health === undefined) {
    return NOT_READ;
  }

  return health.thisSide.checking.includes(key)
    ? JUDGED['checking']!('')
    : fromFile(health.thisSide.files?.checks[key], health.nowMs);
}

/** The ✓ Check is a consultant check: offered only on a runtime that can hold a consultation (shared/feature-availability.json). */
export function offersCheck(runtime: Runtime): boolean {
  return CONSULTING.includes(runtime);
}

/** Why a row's ✓ Check is not offered — the disabled button's title (D7). The one place the wording lives. */
export function checkNotOffered(runtime: Runtime): string {
  return `the consultant check is not offered for ${runtime} — this runtime cannot hold a consultation`;
}

/**
 * ✓ Check — on a runtime that can hold a consultation: one paid turn of this row, asked first by the host (D10), held while
 * one runs. On any other row: drawn disabled with the reason, and bound to nothing — no command, no row, so a press posts
 * nothing and no paid-turn prompt can open (D7). The same, with the update note as the reason, on a runtime this extension
 * consults on but the INSTALLED server is known to be older for (`CONSULTANT_SINCE`; deviation 37 of
 * todo/PLAN_vibe_and_kimi_runtimes_E5.md) — an unknown server is never called old. The server's refusal by name stays the
 * backstop for a stale page.
 */
export function checkButton(id: string, runtime: Runtime, check: CheckFacts, serverVersion: string): string {
  if (!offersCheck(runtime)) {
    return refusedCheck(checkNotOffered(runtime));
  }

  return consultantOnServer(runtime, serverVersion) ? offeredCheck(id, check) : refusedCheck(consultantRuntimeNote(serverVersion, runtime));
}

function offeredCheck(id: string, check: CheckFacts): string {
  return `<button type="button" class="link" data-command="checkModel" data-id="${id}"`
    + ` title="One real, paid turn of this model in a scratch folder — you are asked first"${check.checking ? ' disabled' : ''}>✓ Check</button>`;
}

function refusedCheck(why: string): string {
  return `<button type="button" class="link" disabled title="${escapeHtml(why)}">✓ Check</button>`;
}

export function healthBadge(check: CheckFacts): string {
  return check.said.length === 0
    ? ''
    : `<span class="badge health ${check.tone}" title="The last ✓ Check — a test call, not coai-mcp's verdict">${escapeHtml(check.said)}</span>`;
}

/** coai-mcp's own verdict, kept apart from the last check; "cannot review" is the current card's `cannotRun`. */
export function verdictBadge(id: string, context: CardContext): string {
  return availabilityOf(id, context.reported) === 'fine'
    ? '<span class="badge verdict ok" title="As coai-mcp reads your settings file here — not a test call">coai-mcp will run it</span>'
    : '';
}

/** The CLI this row runs, and whether a newer one is published — for a row that runs a CLI on this machine. */
export function cliBadge(vendor: Vendor, cli: CliStatus): string {
  if (!CLI_RUNTIMES.includes(vendor.runtime)) {
    return '';
  }
  return `<span class="badge cli" title="${escapeHtml(cliStatusNote(vendor.runtime, cli))}">${escapeHtml(cliSaid(vendor.runtime, cli))}</span>`;
}

function cliSaid(runtime: string, cli: CliStatus): string {
  return cli.installed.length > 0 ? `${runtime} ${cli.installed}${newerOf(cli)}` : `${runtime} CLI: version not read`;
}

function newerOf(cli: CliStatus): string {
  return cli.latest.length > 0 && updateAvailable(cli.installed, cli.latest) ? ` · ${cli.latest} available` : '';
}

/** ▶ open, ⤓ install, ⟳ update for a CLI; ⟳ look again and ⇄ fix WSL for a local engine; ≡ ask an endpoint. */
export function worldButtons(vendor: Vendor, id: string, context: CardContext): string {
  return headButtons(vendor, id, vendor.runtime === 'local', vendor.runtime === 'api', context.localEngine, context.cli)
    + endpointButton(vendor, id, context.askingEndpoint ?? false);
}

/** A local engine whose endpoint is NOT this machine: the diff and the rules would be sent there. */
export function offMachineNote(vendor: Vendor): string {
  const warning = vendor.runtime === 'local' ? remoteWarning(vendor.baseUrl) : '';

  return warning.length === 0 ? '' : `<p class="skew">${escapeHtml(warning)}</p>`;
}

/**
 * What coai-mcp WARNED about the CLI this row will run — today a Vibe version coai has not measured, which runs anyway
 * (the owner's decision of 2026-10-13; `providers[].warning`). Said on the card, in the warning tone, for an AVAILABLE
 * row too: `cannotRun` speaks only for an unavailable one, and a warned row is not that. Nothing when the server sent
 * no warning, which is every other row and every server older than the field.
 */
export function versionWarningNote(id: string, reported: CardContext['reported']): string {
  const warning = reported[id]?.warning ?? '';

  return warning.length === 0 ? '' : `<p class="skew warned"><b>Runs with a warning:</b> ${escapeHtml(warning)}</p>`;
}

/**
 * A Kimi row's STANDING survivor marker, when the server reports one: the line that says what stands and the *Clear the stuck Kimi
 * marker* button (E6 of research/PLAN_kimi_and_vibe_on_linux.md, DL9). Nothing for any other row, when no marker stands, and from a
 * server older than the field — the Busy sentence carries the way out by hand either way. The button's id is the ROW; the host asks
 * the server again at the press and takes the path from that answer (`kimiMarker.ts`).
 */
export function kimiMarkerNote(vendor: Vendor, reported: CardContext['reported']): string {
  const marker = vendor.runtime === 'kimi' ? reported[vendor.id]?.survivorMarker : undefined;

  return marker === undefined
    ? ''
    : `<p class="skew warned"><b>A Kimi marker stands.</b> ${escapeHtml(markerLine(marker))} `
      + `<button type="button" class="link" data-command="clearKimiMarker" data-id="${escapeHtml(vendor.id)}" data-pending-label="Clearing…" `
      + 'title="Checks every process the marker names; clears it only when all of them are gone. Ends nothing.">Clear the stuck Kimi marker</button></p>';
}

/**
 * A Kimi row's tick-box for `coai.kimiAllowNetworkSignIn` (DL6, Q7): admit a sign-in folder on network storage. Drawn only on a Kimi card
 * on a WSL2 or Linux window (`CardContext.kimiAllowNetworkSignIn` is undefined elsewhere) and written as a PLAIN setting — never a row's
 * field, so no `data-vendor`. It says the risk beside the box, because the refusal it lifts exists to keep the person signed in.
 */
export function kimiNetworkSignInField(vendor: Vendor, context: CardContext): string {
  const on = context.kimiAllowNetworkSignIn;

  return vendor.runtime !== 'kimi' || on === undefined
    ? ''
    : `<div class="field"><div class="check-row"><label class="check"><input type="checkbox" data-setting="kimiAllowNetworkSignIn"${on ? ' checked' : ''}> `
      + `Allow a Kimi sign-in on network storage</label>${help('kimiAllowNetworkSignIn')}</div>`
      + '<div class="hint">Off: coai refuses to start Kimi when its sign-in folder is on network storage (NFS, CIFS, sshfs). A Kimi on another machine that shares '
      + 'this home can refresh the sign-in first and leave you signed out. Tick only if you never run Kimi elsewhere with this home.</div></div>';
}

/** The contract the row's own Team server last said it speaks — undefined for any other row, or one not heard from. */
function contractOf(vendor: Vendor, servers: readonly TeamServerState[]): number | undefined {
  return vendor.runtime === 'remote' ? servers.find((one) => one.server.id === vendor.teamServerId)?.contract : undefined;
}

/** A Team server that speaks contract 1 applies neither a row's effort nor its system prompt (E2.5) — said on the card. */
export function contractNote(vendor: Vendor, servers: readonly TeamServerState[]): string {
  const contract = contractOf(vendor, servers);

  return contract === undefined || contract >= 2
    ? ''
    : `<p class="skew">This Team server speaks contract ${contract}, so it applies neither this row's effort nor its system prompt — update the Team server.</p>`;
}
