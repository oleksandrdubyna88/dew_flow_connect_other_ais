import type { Admission } from './capabilityAdmission';
import { RUNTIMES as QUESTION_RUNTIMES } from './capabilityAdmission';
import { optionHtml, rowPicks } from './catalogPicks';
import { type ProbeResult } from './claudeModels';
import { consultableVendors } from './consultSettings';
import { type VendorOption, catalogueOptions } from './consultantView';
import { escapeHtml } from './escapeHtml';
import type { LocalEngine } from './localEngines';
import { type ModelChoice, RUNTIMES, type Runtime, modelsFor } from './models';
import type { HelpKey } from './help';
import { help, segmentedRadio } from './panelControls';
import { MAX_ACTIVE_ROWS, type QconsultSettings, type QuestionRowSetting, qconsultSkewNote } from './qconsultSettings';
import {
  type QuestionPromptView,
  type RootPlaces,
  enableBlocker,
  questionPrompts,
  rootRefusal,
  rowAdmission,
  runtimeOfRow,
} from './qconsultWrite';
import type { Vendor } from './vendors';

/**
 * The *Question consultant* section (todo/PLAN_question_consultant.md, S4): the rows — a model and exactly one
 * base prompt each, on or off, at most six on — the prompts with their capability, the folders a disk row may
 * read, the mode and the limits.
 *
 * <p>The `consultantView.ts` split: {@link questionRowView} DECIDES what a row offers and whether its switch
 * may be turned on, and the markup only renders that value — so a test asserts the decision, and the page test
 * runs the page. An incompatible pair is DISABLED with the capability table's own reason (A3); a pair that can
 * read this machine (D13) is shown so — with a tick that is ON and cannot be taken off, because no setting of that
 * runtime confines what it reads (revised by the operator on 2026-10-03; there is no acknowledgement to give).</p>
 */

/** What the section needs beyond the settings. */
export interface QconsultViewState {
  readonly vendors: readonly Vendor[];
  readonly codexModels?: readonly ModelChoice[];
  readonly agyModels?: readonly ModelChoice[];
  readonly claudeProbe?: ProbeResult | undefined;
  readonly enginesByEndpoint?: Readonly<Record<string, LocalEngine>> | undefined;
  /** The installed server's version — the banner, when it is known to be too old. */
  readonly serverVersion: string;
  /** Each SHIPPED prompt's override file as it is on disk; absent or empty = the shipped words. */
  readonly promptOverrides?: Readonly<Record<string, string>> | undefined;
  /** Where a disk root may not be on this machine; absent draws no refusal beside a root. */
  readonly places?: RootPlaces | undefined;
  /**
   * The new Settings page's catalog rows (PLAN_one_model_catalog.md E4.2): given, each row PICKS one ticked "question
   * consultant" instead of drawing its own vendor, model, endpoint, key and CLI path; absent, the current page.
   */
  readonly pickFrom?: readonly Vendor[] | undefined;
}

/** One option of a row's prompt picker: the prompt, and — when this row's runtime cannot run it — why. */
export interface PromptOption {
  readonly value: string;
  readonly label: string;
  readonly disabled: boolean;
  readonly reason: string;
}

/** Everything one row DECIDES, before any of it is markup. */
export interface QuestionRowView {
  readonly id: string;
  readonly vendor: string;
  readonly vendors: readonly VendorOption[];
  readonly runtime: string;
  readonly model: string;
  readonly models: readonly ModelChoice[];
  readonly prompt: string;
  readonly prompts: readonly PromptOption[];
  readonly admission: Admission;
  readonly enabled: boolean;
  /** Why the switch of a row that is OFF may not be turned on — empty when it may. */
  readonly blocked: string;
  readonly takesBaseUrl: boolean;
  readonly takesKey: boolean;
  readonly takesExecutablePath: boolean;
  readonly baseUrl: string;
  readonly key: string;
  readonly executablePath: string;
}

/** The runtimes the question consultant launches, as the panel's `Runtime` — the capability table's list. */
function questionRuntimes(): readonly Runtime[] {
  return RUNTIMES.filter((runtime) => (QUESTION_RUNTIMES as readonly string[]).includes(runtime));
}

/** What one row shows, decided — exported because it is what the tests assert. */
export function questionRowView(row: QuestionRowSetting, settings: QconsultSettings, state: QconsultViewState): QuestionRowView {
  const prompts = questionPrompts(settings.prompts);
  const runtime = runtimeOfRow(row, state.vendors);

  return {
    id: row.id,
    vendor: row.vendor,
    vendors: catalogueOptions(consultableVendors(questionRuntimes())),
    runtime,
    model: row.model,
    models: modelsOf(row, runtime, state),
    prompt: row.prompt,
    prompts: prompts.map((p) => promptOption(p, runtime, row, state.vendors)),
    admission: rowAdmission(row, prompts, state.vendors),
    enabled: row.enabled,
    blocked: row.enabled ? '' : enableBlocker(row, settings.rows, prompts, state.vendors),
    ...fieldsOf(row, runtime),
  };
}

function fieldsOf(row: QuestionRowSetting, runtime: string): Pick<QuestionRowView, 'takesBaseUrl' | 'takesKey' | 'takesExecutablePath' | 'baseUrl' | 'key' | 'executablePath'> {
  return {
    takesBaseUrl: ['codex', 'local', 'api'].includes(runtime),
    takesKey: runtime === 'api',
    takesExecutablePath: ['claude', 'codex', 'antigravity'].includes(runtime),
    baseUrl: row.baseUrl,
    key: row.key,
    executablePath: row.executablePath,
  };
}

/** The models the runtime offers — through the same `modelsFor` the reviewer and consultant rows use. */
function modelsOf(row: QuestionRowSetting, runtime: string, state: QconsultViewState): readonly ModelChoice[] {
  return (RUNTIMES as readonly string[]).includes(runtime) ? listed(row, runtime as Runtime, state) : [];
}

function listed(row: QuestionRowSetting, runtime: Runtime, state: QconsultViewState): readonly ModelChoice[] {
  return modelsFor(runtime, state.codexModels ?? [], row.model, engineOf(row, runtime, state), state.agyModels ?? [], [], state.claudeProbe,
    row.executablePath, { baseUrl: row.baseUrl, keyName: keyNameOf(row) });
}

/** The local engine probed at a local row's endpoint, when one has been. */
function engineOf(row: QuestionRowSetting, runtime: Runtime, state: QconsultViewState): LocalEngine | undefined {
  return runtime === 'local' ? (state.enginesByEndpoint ?? {})[row.baseUrl] : undefined;
}

/** The vault entry an api row's key is filed under: its own key name, or its vendor id. */
function keyNameOf(row: QuestionRowSetting): string {
  return row.key.length > 0 ? row.key : row.vendor;
}

/** A prompt as this row's picker offers it: disabled, with the table's reason, when the row's runtime cannot run it (A3). */
function promptOption(prompt: QuestionPromptView, runtime: string, row: QuestionRowSetting, vendors: readonly Vendor[]): PromptOption {
  const admission = rowAdmission({ ...row, runtime, prompt: prompt.id }, [prompt], vendors);

  return {
    value: prompt.id,
    label: `${prompt.title} — ${prompt.capability}`,
    disabled: !admission.admitted,
    reason: admission.reason,
  };
}

/** The section's body. */
export function qconsultBody(settings: QconsultSettings, state: QconsultViewState): string {
  return [
    banner(state.serverVersion),
    switchAndMode(settings),
    rowsBlock(settings, state),
    promptsBlock(settings, state),
    rootsBlock(settings, state),
    limitsBlock(settings),
  ].join('\n');
}

function banner(serverVersion: string): string {
  const note = qconsultSkewNote(serverVersion);

  return note.length === 0 ? '' : `<div class="hint stale" data-qconsult-banner>${escapeHtml(note)}</div>`;
}

const MODE_WORDS: readonly (readonly [string, string])[] = [['off', 'Off'], ['remind', 'Remind'], ['require', 'Require']];

function switchAndMode(settings: QconsultSettings): string {
  return `<div class="field">
  <div class="check-row"><label for="qconsultEnabled"><input type="checkbox" id="qconsultEnabled" data-setting="qconsultEnabled"${settings.enabled ? ' checked' : ''}> Let an AI ask the question consultant first</label>${help('qconsultEnabled')}</div>
  <div class="hint">Before an AI asks you a question it calls <code>ask_consultants</code>: every row below that is on answers it at once, each with its one prompt, and the answers come back to the AI separately — advice, never orders.</div>
</div>
<div class="field">
  <label>${help('qconsultMode')}Before it asks you</label>
  ${segmentedRadio('qconsultMode', settings.mode, 'What the gate in front of ask_human does about a question asked without the consultants', MODE_WORDS)}
  <div class="hint">Require: once a plan is being built — after its first two question batches, until its stage release — a question that was not put to the consultants first is refused. A production risk with a reason always reaches you at once, with the consultants' answers folded under it. Remind only says so; Off says nothing.</div>
</div>`;
}

function rowsBlock(settings: QconsultSettings, state: QconsultViewState): string {
  const on = settings.rows.filter((r) => r.enabled).length;

  return `<h3>${help('qconsultRows')}Who answers</h3>
${settings.rows.map((row) => rowHtml(questionRowView(row, settings, state), state.pickFrom)).join('\n')}
<button type="button" class="link" data-command="qconsultAddRow" data-id="">Add a row</button>
<div class="hint">${on} of at most ${MAX_ACTIVE_ROWS} on. One row is a model and exactly one prompt; the same prompt may sit on several rows. A row is added switched off.</div>`;
}

/** One row: its switch, who answers, its prompt, the caveat and its tick, and Remove. */
function rowHtml(view: QuestionRowView, pickFrom: readonly Vendor[] | undefined): string {
  const id = escapeHtml(view.id);

  return `<div class="field qconsult-row" data-row="${id}">
  ${rowSwitch(view)}
${pickFrom === undefined ? definitionFields(view) : pickField(view, pickFrom)}
  <label for="qconsultRowPrompt-${id}">${help('qconsultRowPrompt')}Prompt</label>
  <select id="qconsultRowPrompt-${id}" data-setting="qconsultRowPrompt" data-caller="${id}">
${promptOptions(view)}
  </select>
${flagBlock(view)}
${rowHints(view)}
  <button type="button" class="link" data-command="qconsultRemoveRow" data-id="${id}">Remove</button>
</div>`;
}

/** The current page: the row's own vendor, model, endpoint, key name and CLI path. */
function definitionFields(view: QuestionRowView): string {
  const id = escapeHtml(view.id);

  return `  <select id="qconsultRowVendor-${id}" data-setting="qconsultRowVendor" data-caller="${id}">
${optionsWithKept(view.vendors, view.vendor)}
  </select>
  ${modelControl(view)}
${view.takesBaseUrl ? textField(view.id, 'qconsultRowBaseUrl', 'Endpoint', view.baseUrl, 'https://api.example.com/v1') : ''}
${view.takesKey ? textField(view.id, 'qconsultRowKey', 'Vault key name', view.key, 'empty = the vendor id') : ''}
${view.takesExecutablePath ? textField(view.id, 'qconsultRowExecutablePath', 'Where its CLI is', view.executablePath, 'leave empty to look it up on PATH') : ''}`;
}

/** The new page: the catalog row this row asks, picked from the rows ticked "question consultant" (E4.2). */
function pickField(view: QuestionRowView, rows: readonly Vendor[]): string {
  const id = escapeHtml(view.id);
  const picks = rowPicks('qconsult', view.vendor, rows, 'This row');
  const note = picks.note.length === 0 ? '' : `\n  <div class="hint stranded">${escapeHtml(picks.note)}</div>`;

  return `  <label for="qconsultRowPick-${id}">Model</label>
  <select id="qconsultRowPick-${id}" data-setting="qconsultRowPick" data-caller="${id}">${picks.options.map((one) => optionHtml(one, view.vendor)).join('')}</select>${note}`;
}

function rowSwitch(view: QuestionRowView): string {
  const id = escapeHtml(view.id);
  const disabled = view.blocked.length > 0 ? ` disabled title="${escapeHtml(view.blocked)}"` : '';

  return `<div class="check-row"><label for="qconsultRowEnabled-${id}"><input type="checkbox" id="qconsultRowEnabled-${id}" data-setting="qconsultRowEnabled" data-caller="${id}"${view.enabled ? ' checked' : ''}${disabled}> ${id} runs</label></div>`;
}

/** The catalogue, and the row's own vendor kept when the catalogue does not list it. */
function optionsWithKept(options: readonly VendorOption[], current: string): string {
  const kept = options.some((o) => o.value === current) ? [] : [{ value: current, label: `${current} — not in the catalogue`, hint: '' }];

  return [...kept, ...options].map((o) => option(o.value, o.label, current, o.hint)).join('\n');
}

/** A select when the runtime offers models, a box otherwise — an api row's model is whatever its endpoint calls it. */
function modelControl(view: QuestionRowView): string {
  const id = escapeHtml(view.id);
  if (view.models.length === 0) {
    return `<input type="text" id="qconsultRowModel-${id}" data-setting="qconsultRowModel" data-caller="${id}" value="${escapeHtml(view.model)}" placeholder="model — empty is the runtime's default">`;
  }
  const kept = view.models.some((m) => m.id === view.model) || view.model.length === 0 ? '' : option(view.model, `${view.model} — not offered here`, view.model);

  return `<select id="qconsultRowModel-${id}" data-setting="qconsultRowModel" data-caller="${id}">
${option('', "the runtime's own default", view.model)}
${view.models.map((m) => option(m.id, m.label, view.model)).join('\n')}
${kept}
  </select>`;
}

/** The prompts, each refused pair DISABLED with its reason; a row with no prompt shows one it cannot keep. */
function promptOptions(view: QuestionRowView): string {
  const missing = view.prompts.some((p) => p.value === view.prompt) ? '' : `    <option value="" selected disabled>${escapeHtml(view.prompt.length === 0 ? 'no prompt — pick one' : `${view.prompt} — not a prompt here`)}</option>\n`;

  return missing + view.prompts.map((p) => promptOptionHtml(p, view.prompt)).join('\n');
}

function promptOptionHtml(p: PromptOption, current: string): string {
  const refused = p.disabled ? ` disabled title="${escapeHtml(p.reason)}"` : '';
  const label = p.disabled ? `${p.label} — cannot run here` : p.label;

  return `    <option value="${escapeHtml(p.value)}"${p.value === current ? ' selected' : ''}${refused}>${escapeHtml(label)}</option>`;
}

/**
 * D13, revised 2026-10-03: a pair that can read this machine says so, with a tick that is ON and cannot be taken off.
 * It is not a setting — nothing is stored, nothing posted — because no setting of the runtime would confine it.
 */
function flagBlock(view: QuestionRowView): string {
  if (!view.admission.admitted || view.admission.flag.length === 0) {
    return '';
  }
  const id = escapeHtml(view.id);

  return `  <div class="hint stale" data-flag="${escapeHtml(view.admission.flag)}">Can read this machine — ${escapeHtml(view.admission.caveat)}.</div>
  <div class="check-row"><label for="qconsultRowCanRead-${id}"><input type="checkbox" id="qconsultRowCanRead-${id}" checked disabled> Can read this machine — ${escapeHtml(whyItStaysOn(view.runtime, view.admission.flag))}</label>${help('qconsultRowCanRead')}</div>`;
}

/** Why the tick cannot be taken off, per flag — the measured fact, in the runtime's own name. */
function whyItStaysOn(runtime: string, flag: string): string {
  const name = runtime.charAt(0).toUpperCase() + runtime.slice(1);

  return flag === 'default-deny'
    ? `${name} has no setting that holds it to these folders; only its own default refuses a read outside them`
    : `${name} has no setting that limits what it reads, so this cannot be switched off`;
}

/** A row that is ON with a pair the table refuses — a hand edit — says why the server will not run it. */
function storedButRefused(view: QuestionRowView): string {
  return view.enabled && !view.admission.admitted ? view.admission.reason : '';
}

/** Why the switch is off-limits, or why a row that is on cannot run as stored. */
function rowHints(view: QuestionRowView): string {
  const said = view.blocked.length > 0 ? view.blocked : storedButRefused(view);

  return said.length === 0 ? '' : `  <div class="hint" data-blocked>${escapeHtml(said)}</div>`;
}

function promptsBlock(settings: QconsultSettings, state: QconsultViewState): string {
  return `<h3>${help('qconsultPrompts')}Base prompts</h3>
${questionPrompts(settings.prompts).map((p) => promptHtml(p, state.promptOverrides?.[p.id] ?? '')).join('\n')}
<button type="button" class="link" data-command="qconsultAddPrompt" data-id="">Add a prompt…</button>`;
}

/** One prompt: its title and capability, and the words it is asked with — a shipped one's override, or its own. */
function promptHtml(prompt: QuestionPromptView, override: string): string {
  const id = escapeHtml(prompt.id);
  const overridden = prompt.shipped && override.trim().length > 0;
  const shown = overridden ? override : prompt.text;

  return `<div class="field qconsult-prompt" data-prompt-id="${id}">
  <label for="qconsultPromptText-${id}">${escapeHtml(prompt.title)} <span class="badge">${escapeHtml(prompt.capability)}</span>${prompt.shipped ? '' : ' <span class="badge">yours</span>'}</label>
  <textarea id="qconsultPromptText-${id}" rows="4" data-setting="qconsultPromptText" data-caller="${id}"${fileOf(prompt)}>${escapeHtml(shown)}</textarea>
${promptAction(prompt, overridden)}
</div>`;
}

/** A shipped prompt's box is written to its override FILE, which `data-file` says — a custom prompt's is a setting. */
function fileOf(prompt: QuestionPromptView): string {
  return prompt.shipped ? ` data-file="${escapeHtml(prompt.id)}.md"` : '';
}

function promptAction(prompt: QuestionPromptView, overridden: boolean): string {
  const id = escapeHtml(prompt.id);
  if (!prompt.shipped) {
    return `  <button type="button" class="link" data-command="qconsultRemovePrompt" data-id="${id}">Remove</button>`;
  }

  return overridden
    ? `  <button type="button" class="link" data-command="qconsultRestorePrompt" data-id="${id}">Restore default</button>`
    : '  <div class="hint">The words this build ships with.</div>';
}

function rootsBlock(settings: QconsultSettings, state: QconsultViewState): string {
  const listed = settings.roots.length === 0
    ? '<div class="hint">No folder yet — a disk row reads nothing until one is here.</div>'
    : settings.roots.map((root) => rootHtml(root, state.places)).join('\n');

  return `<h3>${help('qconsultRoots')}Folders a disk row may read</h3>
${listed}
<button type="button" class="link" data-command="qconsultAddRoot" data-id="">Add a folder…</button>
<div class="hint">Read-only, and never a drive root, your profile folder itself, a system folder or the data folder — the server refuses those too.</div>`;
}

function rootHtml(root: string, places: RootPlaces | undefined): string {
  const refusal = places === undefined ? '' : rootRefusal(root, places);

  return `<div class="field qconsult-root"><code>${escapeHtml(root)}</code> <button type="button" class="link" data-command="qconsultRemoveRoot" data-id="${escapeHtml(root)}">Remove</button>${refusal.length === 0 ? '' : `<div class="hint stale">${escapeHtml(refusal)}</div>`}</div>`;
}

function limitsBlock(settings: QconsultSettings): string {
  return [
    numberField('qconsultRowMinutes', 'Minutes one row may run', settings.rowMinutes, 60),
    numberField('qconsultQuestionsPerSession', 'Questions per session', settings.questionsPerSession, 100),
    numberField('qconsultFreeBatches', 'Question batches that reach you first', settings.freeBatches, 20),
  ].join('\n');
}

function numberField(setting: HelpKey, label: string, value: number, max: number): string {
  return `<div class="field inline">
  <label for="${setting}">${help(setting)}${escapeHtml(label)}</label>
  <input type="number" id="${setting}" min="1" max="${max}" data-setting="${setting}" value="${value}">
</div>`;
}

function textField(row: string, setting: string, label: string, value: string, placeholder: string): string {
  const id = escapeHtml(row);

  return `  <label for="${setting}-${id}">${escapeHtml(label)}</label>
  <input type="text" id="${setting}-${id}" data-setting="${setting}" data-caller="${id}" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder)}">`;
}

function option(value: string, label: string, selected: string, hint = ''): string {
  const title = hint.length === 0 ? '' : ` title="${escapeHtml(hint)}"`;

  return `    <option value="${escapeHtml(value)}"${value === selected ? ' selected' : ''}${title}>${escapeHtml(label)}</option>`;
}
