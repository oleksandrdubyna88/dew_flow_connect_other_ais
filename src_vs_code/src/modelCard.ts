import { apiSettingsFields } from './apiSettingsView';
import { type BinarySays, confirmButton, skew } from './catalogShell';
import { escapeHtml } from './escapeHtml';
import { effortField, systemPromptField, thinkingLine, timeoutField, usesBoxes } from './modelCardFields';
import {
  type CheckFacts, checkButton, cliBadge, contractNote, healthBadge, offMachineNote, verdictBadge, worldButtons,
} from './modelCardWorld';
import type { TeamServerState } from './teamServerView';
import { modelsFor, modelsProvenance } from './models';
import {
  type CardContext, cannotRun, dialectField, endpointField, featureBox, modelOptions, modelWords, priceFields, runtimeFields, stageBox,
} from './panelView';
import { vaultKeyOf } from './vaultKey';
import { executableFor } from './vendorTerminal';
import { reviewsDocuments, type Vendor } from './vendors';

/**
 * One card of the Models tab (todo/PLAN_one_model_catalog.md E3.2): what the row is, what it is used for, how it
 * answers, and how it is reached — drawn from the SAME facts and the same controls as the current page's card
 * (`cardContextFor`, `stageBox`, `priceFields` …), so a choice made on either page is stored the same way.
 *
 * <p>Four parts, each one row of the grid's subgrid, so two cards side by side line their parts up and end together.</p>
 */

/** What a card needs beyond its row and the shared context. */
export interface ModelCardFacts {
  readonly context: CardContext;
  /** What refers to this row — sentences the remove dialog lists, and the line under the card. */
  readonly references: readonly string[];
  /** The review stages this row is the only switched-on model for: it can then be neither switched off nor removed. */
  readonly lastFor: readonly string[];
  readonly binary: BinarySays;
  /** The row's last ✓ Check, from its durable record (`modelCardWorld.ts`). */
  readonly check: CheckFacts;
  /** The Team servers this side knows — what a remote row's contract note reads. */
  readonly teamServers: readonly TeamServerState[];
}

interface Access {
  readonly key: string;
  readonly label: string;
}

const CLI_ACCESS: Access = { key: 'cli', label: 'a CLI on this machine' };

const ACCESS: Partial<Readonly<Record<Vendor['runtime'], Access>>> = {
  local: { key: 'local', label: 'this machine’s GPU' },
  api: { key: 'api', label: 'an API key' },
  remote: { key: 'remote', label: 'a Team server' },
};

/** Where a row runs, as a person says it — the access badge, and the access filter's key. */
export function accessOf(runtime: Vendor['runtime']): Access {
  return ACCESS[runtime] ?? CLI_ACCESS;
}

/** The review stages a row takes, as the "Used for" filter names them. */
const STAGES: readonly (readonly [string, (vendor: Vendor) => boolean])[] = [
  ['plan', (vendor) => vendor.plan],
  ['code', (vendor) => vendor.code],
  ['document', reviewsDocuments],
];

/** The stages and uses a row has, as the "Used for" filter and its counts read them. */
export function usedFor(vendor: Vendor): readonly string[] {
  return [...STAGES.filter(([, takes]) => takes(vendor)).map(([stage]) => stage), ...(vendor.uses ?? [])];
}

function effortKey(vendor: Vendor): string {
  return (vendor.effort ?? '').length > 0 ? vendor.effort! : 'default';
}

/** The card's data — what the page's filters read, never what a write is keyed by. */
function filterAttributes(vendor: Vendor): string {
  const search = [vendor.id, vendor.name ?? '', vendor.model, vendor.runtime].join(' ').toLowerCase();

  return ` data-model-card="${escapeHtml(vendor.id)}" data-runtime="${escapeHtml(vendor.runtime)}"`
    + ` data-access="${accessOf(vendor.runtime).key}" data-uses="${escapeHtml(usedFor(vendor).join(' '))}"`
    + ` data-effort="${escapeHtml(effortKey(vendor))}" data-enabled="${String(vendor.enabled)}" data-search="${escapeHtml(search)}"`;
}

/** Why the row may not be switched off or removed now — '' when it may. */
function lockOf(facts: ModelCardFacts): string {
  return facts.lastFor.length > 0 ? `the only model switched on for ${facts.lastFor.join(' and ')} review` : '';
}

function onSwitch(vendor: Vendor, id: string, locked: string): string {
  const refused = locked.length > 0 && vendor.enabled ? ` disabled title="${escapeHtml(locked)}"` : '';

  return `<input type="checkbox" class="switch" id="on-${id}" data-setting="enabled" data-vendor="${id}"${vendor.enabled ? ' checked' : ''}`
    + `${refused} aria-label="${escapeHtml(`${vendor.id} switched on`)}">`;
}

function nameBlock(vendor: Vendor, id: string): string {
  return `<div><input class="name-edit" data-setting="name" data-vendor="${id}" value="${escapeHtml(vendor.name ?? '')}" placeholder="${id}" aria-label="Display name">`
    + `<div class="sub">${escapeHtml(accessOf(vendor.runtime).label)} · ${escapeHtml(vendor.runtime)} · id <code>${id}</code></div></div>`;
}

function removeBody(vendor: Vendor, references: readonly string[]): string {
  const left = references.length > 0 ? `\nIt is ${references.join('; ')} — each is left with nobody until you pick another.` : '';

  return `Its spending history stays in the log under ${vendor.id}.${left}`;
}

function actions(vendor: Vendor, id: string, facts: ModelCardFacts, locked: string): string {
  const remove = confirmButton({
    label: '✕', command: 'removeModel', id: vendor.id, title: `Remove ${vendor.id}?`, action: 'Remove', danger: true,
    body: removeBody(vendor, facts.references), ...(locked.length > 0 ? { refused: locked } : {}),
  });

  return `<div class="actions">${checkButton(id, facts.check)}<button type="button" class="link" data-command="duplicateModel" data-id="${id}" title="Add a copy with its own id">⧉ Duplicate</button>${remove}</div>`;
}

/** What this side's coai-mcp ignores on a row, asked through the one `skew` road — by capability (E3's plan round). */
const IGNORED: readonly { readonly feature: string; readonly what: string; readonly set: (vendor: Vendor) => boolean }[] = [
  { feature: 'systemPrompt', what: 'its system prompt', set: (vendor) => (vendor.systemPrompt ?? '').length > 0 },
  { feature: 'timeoutMinutes', what: 'its own time limit', set: (vendor) => vendor.runtime !== 'api' && vendor.timeoutMinutes !== undefined },
  { feature: 'cliEffort', what: 'its effort', set: (vendor) => vendor.runtime !== 'api' && (vendor.effort ?? '').length > 0 },
];

function ignoredNote(vendor: Vendor, binary: BinarySays): string {
  const said = [...new Set(IGNORED.filter((one) => one.set(vendor)).map((one) => skew(one.feature, one.what, binary)).filter((one) => one.length > 0))];

  return said.length === 0 ? '' : `<p class="skew">${escapeHtml(said.join(' '))}</p>`;
}

function top(vendor: Vendor, id: string, facts: ModelCardFacts): string {
  const locked = lockOf(facts);

  return `<div class="card-top"><div class="card-head">${onSwitch(vendor, id, locked)}${nameBlock(vendor, id)}${actions(vendor, id, facts, locked)}</div>`
    + `<div class="badges"><span class="badge access">${escapeHtml(accessOf(vendor.runtime).label)}</span>${verdictBadge(vendor.id, facts.context)}`
    + `${cannotRun(vendor.id, facts.context.reported)}${cliBadge(vendor, facts.context.cli)}${healthBadge(facts.check)}</div>`
    + `<div class="world">${worldButtons(vendor, id, facts.context)}</div>`
    + `${offMachineNote(vendor)}${contractNote(vendor, facts.teamServers)}${ignoredNote(vendor, facts.binary)}</div>`;
}

function useFor(vendor: Vendor, id: string, facts: ModelCardFacts): string {
  const on = vendor.enabled && facts.context.apiNote.length === 0;
  const stage = (kind: 'plan' | 'code', text: string): string => stageBox(kind, id, vendor[kind], on && !facts.lastFor.includes(kind), text);

  return `<div class="block"><h4 class="block-title">Use for</h4>`
    + `<div class="field stages">${stage('plan', 'reviews plans')}${stage('code', 'reviews code')}`
    + `${stageBox('document', id, reviewsDocuments(vendor), on, 'reviews documents')}${featureBox(vendor, id, on, facts.context.featureNote)}</div>`
    + `<div class="field">${usesBoxes(vendor, id)}</div></div>`;
}

function modelField(vendor: Vendor, id: string, context: CardContext): string {
  const endpoint = { baseUrl: vendor.baseUrl, keyName: vaultKeyOf(vendor), listed: context.endpointListing, asking: context.askingEndpoint ?? false };
  const models = modelsFor(vendor.runtime, context.codexModels, vendor.model, context.localEngine, context.agyModels,
    context.allowedRemote.models, context.claudeProbe, executableFor(vendor), endpoint);
  const words = modelWords(vendor.runtime, vendor.baseUrl);
  const whence = modelsProvenance(vendor.runtime, context.codexModels, {
    localEngine: context.localEngine, discoveredAgy: context.agyModels, remote: context.allowedRemote,
    claudeProbe: context.claudeProbe, askingClaude: context.askingClaude, endpoint,
  });

  return `<div class="field"><label for="model-${id}">Model</label>`
    + `<select id="model-${id}" data-setting="model" data-vendor="${id}" title="${escapeHtml(words.title)}">${modelOptions(models, vendor.model, words.empty)}</select>`
    + `<div class="hint">${escapeHtml(whence)}</div></div>`;
}

/** The efforts the row's own probe reported — what a local engine is judged by. */
function probedOf(context: CardContext, id: string): readonly string[] {
  return context.reported[id]?.api?.capabilities.effortLevels ?? [];
}

/** By runtime: an api row's effort, thinking and limit come from its probe report; any other row's from its runtime. */
function tuning(vendor: Vendor, id: string, context: CardContext): string {
  return vendor.runtime === 'api'
    ? apiSettingsFields(vendor, id, context.reported[vendor.id], context.serverVersion)
    : effortField(vendor, id, probedOf(context, vendor.id)) + thinkingLine(vendor) + timeoutField(vendor, id);
}

function answers(vendor: Vendor, id: string, facts: ModelCardFacts): string {
  return `<div class="block"><h4 class="block-title">How it answers</h4><div class="settings-grid">`
    + `${modelField(vendor, id, facts.context)}${tuning(vendor, id, facts.context)}${systemPromptField(vendor, id)}</div></div>`;
}

/** How the row is reached: its endpoint, an api row's dialect, the CLI's path where there is a CLI here. */
function connectionOf(vendor: Vendor, id: string): string {
  const remote = vendor.runtime === 'remote';
  const api = vendor.runtime === 'api';

  return endpointField(vendor, id, vendor.runtime === 'local', remote, false) + (api ? dialectField(vendor, id, false) : '')
    + runtimeFields(vendor, id, remote || api);
}

function usedLine(references: readonly string[]): string {
  return references.length > 0 ? `<div class="used-by">Used as ${escapeHtml(references.join('; '))}.</div>` : '';
}

function foot(vendor: Vendor, id: string, facts: ModelCardFacts): string {
  const prices = priceFields(vendor, id, vendor.runtime === 'local', vendor.runtime === 'remote', facts.context.price, '', '', false);

  return `<div class="card-foot">${details('Connection', connectionOf(vendor, id))}${details('Price', prices)}${usedLine(facts.references)}</div>`;
}

function details(title: string, body: string): string {
  return body.trim().length === 0 ? '' : `<details class="more"><summary><span class="what">${escapeHtml(title)}</span></summary>${body}</details>`;
}

/** One card. */
export function modelCard(vendor: Vendor, facts: ModelCardFacts): string {
  const id = escapeHtml(vendor.id);

  return `<article class="card${vendor.enabled ? '' : ' disabled'}" style="--vc:${escapeHtml(facts.context.colour)}"${filterAttributes(vendor)}>`
    + `${top(vendor, id, facts)}${useFor(vendor, id, facts)}${answers(vendor, id, facts)}${foot(vendor, id, facts)}</article>`;
}
