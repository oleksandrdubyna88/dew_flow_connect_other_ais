import type { Admission } from './capabilityAdmission';
import { offeredLine, optionHtml, rowPicks } from './catalogPicks';
import { escapeHtml } from './escapeHtml';
import type { HelpKey } from './help';
import { help, segmentedRadio } from './panelControls';
import { familyLabel, otherSideHere, otherSideNote, type Existence, type PathFamily } from './pathFamily';
import { NOTHING_ASKED, groupedPaths } from './pathGroups';
import { groupCaptionId, pathGroupsHtml } from './pathGroupsView';
import { keepsItsOwnLine, type PathSides } from './pathSides';
import { TYPED_ROOT_KEY } from './typedRoots';
import { refusalAttributes, refusalNote as typedRefusalNote, rootBoxKey, type TypedRefusal, type TypedRefusals } from './typedRefusals';
import { type PathFinding, findingFor } from './pathFindings';
import { markAttributes, markClass, markNote } from './pathMark';
import { QCONSULT_ROOTS } from './pathSettings';
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
 * <p>{@link questionRowView} DECIDES which prompts a row offers and whether its switch may be turned on, and the
 * markup only renders that value — so a test asserts the decision, and the page test runs the page. Who a row asks
 * is a pick of a catalog row ticked "question consultant" (research/PLAN_one_model_catalog.md E4.2); its model is edited
 * on Models. An incompatible pair is DISABLED with the capability table's own reason (A3); a pair that can
 * read this machine (D13) is shown so — with a tick that is ON and cannot be taken off, because no setting of that
 * runtime confines what it reads (revised by the operator on 2026-10-03; there is no acknowledgement to give).</p>
 */

/** What the section needs beyond the settings. */
export interface QconsultViewState {
  readonly vendors: readonly Vendor[];
  /** The installed server's version — the banner, when it is known to be too old. */
  readonly serverVersion: string;
  /** Each SHIPPED prompt's override file as it is on disk; absent or empty = the shipped words. */
  readonly promptOverrides?: Readonly<Record<string, string>> | undefined;
  /** Where a disk root may not be on this machine; absent draws no refusal beside a root. */
  readonly places?: RootPlaces | undefined;
  /** The paths of this side the watch found missing or unanswerable (E5.4); absent draws no mark beside a root. */
  readonly findings?: readonly PathFinding[] | undefined;
  /** The catalog rows a question row picks from — those ticked "question consultant" among them (PLAN_one_model_catalog.md E4.2). */
  readonly pickFrom: readonly Vendor[];
  /** Both sides, when the folders are drawn in groups (todo/PLAN_paths_per_side.md E4.2); undefined draws one list. */
  readonly sides?: PathSides | undefined;
  /** What was typed into the other side's folder box and refused — drawn beside the box. */
  readonly refusals?: TypedRefusals | undefined;
  /**
   * Whether `qconsultRoots` is saved into THIS side's own settings (`coai.perSideSettings`, `settingsShape.keptPerSide`):
   * the other side's typed box is then not drawn — a sentence stands in its place (E4's own review, item 1).
   */
  readonly ownSettings?: boolean | undefined;
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
  readonly runtime: string;
  readonly prompt: string;
  readonly prompts: readonly PromptOption[];
  readonly admission: Admission;
  readonly enabled: boolean;
  /** Why the switch of a row that is OFF may not be turned on — empty when it may. */
  readonly blocked: string;
}

/** What one row shows, decided — exported because it is what the tests assert. */
export function questionRowView(row: QuestionRowSetting, settings: QconsultSettings, state: QconsultViewState): QuestionRowView {
  const prompts = questionPrompts(settings.prompts);
  const runtime = runtimeOfRow(row, state.vendors);

  return {
    id: row.id,
    vendor: row.vendor,
    runtime,
    prompt: row.prompt,
    prompts: prompts.map((p) => promptOption(p, runtime, row, state.vendors)),
    admission: rowAdmission(row, prompts, state.vendors),
    enabled: row.enabled,
    blocked: row.enabled ? '' : enableBlocker(row, settings.rows, prompts, state.vendors),
  };
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
${offeredLine('qconsult')}
${settings.rows.map((row) => rowHtml(questionRowView(row, settings, state), state.pickFrom)).join('\n')}
<button type="button" class="link" data-command="qconsultAddRow" data-id="">Add a row</button>
<div class="hint">${on} of at most ${MAX_ACTIVE_ROWS} on. One row is a model and exactly one prompt; the same prompt may sit on several rows. A row is added switched off.</div>`;
}

/** One row: its switch, who answers, its prompt, the caveat and its tick, and Remove. */
function rowHtml(view: QuestionRowView, pickFrom: readonly Vendor[]): string {
  const id = escapeHtml(view.id);

  return `<div class="field qconsult-row" data-row="${id}">
  ${rowSwitch(view)}
${pickField(view, pickFrom)}
  <label for="qconsultRowPrompt-${id}">${help('qconsultRowPrompt')}Prompt</label>
  <select id="qconsultRowPrompt-${id}" data-setting="qconsultRowPrompt" data-caller="${id}">
${promptOptions(view)}
  </select>
${flagBlock(view)}
${rowHints(view)}
  <button type="button" class="link" data-command="qconsultRemoveRow" data-id="${id}">Remove</button>
</div>`;
}

/** The catalog row this row asks, picked from the rows ticked "question consultant" (E4.2). */
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
  return `<h3>${help('qconsultRoots')}Folders a disk row may read</h3>
${state.sides === undefined ? oneRootList(settings, state) : rootGroups(settings, state, state.sides)}
<div class="hint">Read-only, and never a drive root, your profile folder itself, a system folder or the data folder — the server refuses those too.</div>`;
}

/** The folders as one list — every place before E4, and one side known (or *Always one*). */
function oneRootList(settings: QconsultSettings, state: QconsultViewState): string {
  const listed = settings.roots.length === 0
    ? '<div class="hint">No folder yet — a disk row reads nothing until one is here.</div>'
    : settings.roots.map((root) => rootLine(root, state)).join('\n');

  return `${listed}${noneOnThisSide(settings.roots, state.places)}
${ADD_A_FOLDER}`;
}

const ADD_A_FOLDER = '<button type="button" class="link" data-command="qconsultAddRoot" data-id="">Add a folder…</button>';

function rootLine(root: string, state: QconsultViewState): string {
  return rootHtml(root, state.places, findingFor(state.findings ?? [], QCONSULT_ROOTS.id, root));
}

/**
 * The folders in the three groups of `pathGroups.ts` once both sides are known (todo/PLAN_paths_per_side.md E4.2): Add a
 * folder… adds to this side's; the other side's takes TYPED text (`typedRoots.ts` decides, the host refuses beside the
 * box); Remove works in every group. Grouped by THIS window's verdict, the disk asked only at this machine's own spelling.
 */
function rootGroups(settings: QconsultSettings, state: QconsultViewState, sides: PathSides): string {
  const grouped = groupedPaths(settings.roots, sides.here === 'windows', existenceOf(state.places));

  return pathGroupsHtml(QCONSULT_ROOTS_LIST, grouped, sides, (root) => rootLine(root, state), {
    thisSide: `${noneOnThisSide(settings.roots, state.places)}\n${ADD_A_FOLDER}`,
    otherSide: otherSideAdds(sides, state),
  });
}

/**
 * What the other side's group adds after its folders: the typed box — or, while this side keeps its own folders, the
 * sentence that says why there is none, with a refusal of a write a page drawn before the switch moved still posted.
 */
function otherSideAdds(sides: PathSides, state: QconsultViewState): string {
  const box = rootBoxKey(QCONSULT_ROOTS_LIST, 'otherSide');
  const refusal = state.refusals?.[box];

  return state.ownSettings === true ? keepsItsOwnLine() + typedRefusalNote(refusal, box) : typedRootBox(sides, refusal);
}

/** The list the typed box appends to — the one shared setting (design (b) O1: the spelling IS the side). */
const QCONSULT_ROOTS_LIST = 'qconsultRoots';

/** What this host's disk said about the stored roots spelled for the other OS, as the grouping reads it. */
function existenceOf(places: RootPlaces | undefined): Existence {
  return places === undefined ? NOTHING_ASKED : { existing: places.existingHere, unknown: places.unknownHere };
}

/** The box that types a folder of the other side — text only, described by its group's caption and by a refusal. */
function typedRootBox(sides: PathSides, refusal: TypedRefusal | undefined): string {
  const box = rootBoxKey(QCONSULT_ROOTS_LIST, 'otherSide');
  const attributes = refusalAttributes(refusal, box);

  return `<div class="field"><label for="qconsult-typed-root">Add a folder on ${familyLabel(sides.other)} — type its path, then press Enter</label>`
    + `<input type="text" id="qconsult-typed-root" data-setting="${TYPED_ROOT_KEY}" data-caller="otherSide" value="${escapeHtml(refusal?.typed ?? '')}"`
    + ` placeholder="${escapeHtml(TYPED_EXAMPLE[sides.other])}" aria-describedby="${escapeHtml(groupCaptionId(QCONSULT_ROOTS_LIST))}${attributes.describedBy}"${attributes.invalid}>`
    + `${typedRefusalNote(refusal, box)}</div>`;
}

/** What the typed box suggests, by the family it types for. */
const TYPED_EXAMPLE: Readonly<Record<PathFamily, string>> = { windows: 'D:\\work\\project or \\\\server\\share\\project', posix: '/home/you/project' };

/**
 * A stored root's line: the folder, its Remove, and what this side makes of it — the other side's folder SAID (the
 * server skips it, operator 2026-10-09), a refused one drawn as broken, a MISSING one of this side marked bright
 * (E5.4: the line takes the mark's class and `aria-invalid`, and the sentence under it).
 */
function rootHtml(root: string, places: RootPlaces | undefined, missing: PathFinding | undefined): string {
  return `<div class="field qconsult-root${markClass(missing)}"${markAttributes(missing)}><code>${escapeHtml(root)}</code> <button type="button" class="link" data-command="qconsultRemoveRoot" data-id="${escapeHtml(root)}">Remove</button>${markNote(missing)}${rootNote(root, places)}</div>`;
}

function rootNote(root: string, places: RootPlaces | undefined): string {
  return places === undefined ? '' : sideNote(root, places) || refusalNote(root, places);
}

/** Which side a root is — said only when the page KNOWS: an unknown answer from the disk is said as one, never guessed. */
function sideNote(root: string, places: RootPlaces): string {
  if (places.unknownHere.includes(root)) {
    return '<div class="hint">This window could not tell whether this folder exists on this machine, so it calls it neither this side\'s nor the other side\'s.</div>';
  }

  return theOtherSides(root, places) ? `<div class="hint">${escapeHtml(otherSideNote(places.windows))}</div>` : '';
}

function refusalNote(root: string, places: RootPlaces): string {
  const refusal = rootRefusal(root, places);

  return refusal.length === 0 ? '' : `<div class="hint stale">${escapeHtml(refusal)}</div>`;
}

/** The server's decision: spelled for the other OS and no folder here — `/work` that exists on a Windows drive is this side's. */
function theOtherSides(root: string, places: RootPlaces): boolean {
  return otherSideHere(root, places.windows, places.existingHere.includes(root), places.unknownHere.includes(root));
}

/** Every stored folder is the other side's: on this side a disk row has nothing to read, and the server does not ask it. */
function noneOnThisSide(roots: readonly string[], places: RootPlaces | undefined): string {
  const allTheOtherSides = places !== undefined && roots.length > 0 && roots.every((root) => theOtherSides(root, places));

  return allTheOtherSides
    ? '\n<div class="hint">Every folder here is the other side\'s, so a disk row is not asked on this side — add a folder of this machine to ask it here too.</div>'
    : '';
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
