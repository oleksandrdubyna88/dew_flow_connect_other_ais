import { type CliStatus, cliStatusNote, updateAvailable } from './cliVersions';
import { MODEL_CHECK } from './consultantCheckRun';
import { checkStateOnThisDisk, type CheckRecord, type HealthFile } from './consultantHealth';
import type { ConsultantHealthState } from './consultantHealthState';
import { escapeHtml } from './escapeHtml';
import { remoteWarning } from './localEngines';
import { type CardContext, endpointButton, headButtons } from './panelView';
import { availabilityOf } from './providers';
import type { TeamServerState } from './teamServerView';
import type { Vendor } from './vendors';

/**
 * The parts of a Models card that face the world (todo/PLAN_one_model_catalog.md E3.3) — each one what the current
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

function judged(record: CheckRecord, nowMs: number): CheckFacts {
  const check = checkStateOnThisDisk(record, nowMs);

  return (JUDGED[check.state] ?? ((reason) => ({ checking: false, said: `checked: ${check.state}${reason.length > 0 ? ` — ${reason}` : ''}`, tone: 'warn' })))(check.reason);
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

/** ✓ Check — one paid turn of this row, asked first by the host (D10); held while one runs. */
export function checkButton(id: string, check: CheckFacts): string {
  return `<button type="button" class="link" data-command="checkModel" data-id="${id}"`
    + ` title="One real, paid turn of this model in a scratch folder — you are asked first"${check.checking ? ' disabled' : ''}>✓ Check</button>`;
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

const CLI_RUNTIMES: ReadonlySet<Vendor['runtime']> = new Set(['codex', 'claude', 'antigravity', 'gemini']);

/** The CLI this row runs, and whether a newer one is published — for a row that runs a CLI on this machine. */
export function cliBadge(vendor: Vendor, cli: CliStatus): string {
  if (!CLI_RUNTIMES.has(vendor.runtime)) {
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
