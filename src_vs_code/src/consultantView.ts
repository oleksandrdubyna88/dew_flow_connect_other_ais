/**
 * The *Consultant* section: who answers each kind of caller when it is stuck, and the caps.
 *
 * <p>Pure markup from a value, like every other section body — no `vscode`, so the whole section is
 * a unit test rather than something only a running window can show.</p>
 *
 * <p><b>The decisions live in {@link consultantRowView}, and the markup only renders one.</b> That
 * split is this story's, and it is what lets the section be tested without parsing HTML: which
 * options a row offers, which is selected, whether the entry is one the catalogue knows, and which
 * fields the runtime even has are a VALUE a test asserts. `.agents/PROJECT.md` refuses a new
 * behavioural assertion over page source text, and the honest alternative to one is not a cleverer
 * regular expression — it is a decision that was never markup in the first place.</p>
 */

import {
  CALLER_KINDS,
  CONSULTING_RUNTIMES,
  CUSTOM_ENDPOINT,
  ConsultSettings,
  consultableVendors,
  sameVendorNote,
} from './consultSettings';
import { ProbeResult, claudeNote } from './claudeModels';
import { escapeHtml } from './escapeHtml';
import { LOOKING } from './lookingSpinner';
import type { LocalEngine } from './localEngines';
import { ModelChoice, Runtime, modelsFor } from './models';
import { VendorPalette } from './vendorColour';

/** What the section needs beyond the settings themselves. The reviewer rows are NOT among them. */
export interface ConsultantViewState {
  /** Models the codex CLI reported, if it has been asked. Empty is normal and not an error. */
  readonly codexModels?: readonly ModelChoice[];
  readonly agyModels?: readonly ModelChoice[];
  /** What the Claude CLI answered when last asked which families it reaches. */
  readonly claudeProbe?: ProbeResult | undefined;
  /** True while that answer is being refreshed \u2014 seconds of real requests, so a row says so. */
  readonly askingClaude?: boolean | undefined;
  /**
   * The local engines the panel has probed, by the endpoint they answer on.
   *
   * <p>Keyed by endpoint rather than by reviewer row, and that is forced rather than chosen: since
   * story C5 the consultant section holds no reviewer rows at all, so there is no row to borrow an
   * engine from. Absent is a real state and NOT the same as an engine that refused — a dropdown
   * that has never asked must not call a saved model gone.</p>
   */
  readonly localEngines?: Readonly<Record<string, LocalEngine>> | undefined;
  /** The prompt override as it is ON DISK, or empty for "the one this build ships with". */
  readonly consultPrompt?: string | undefined;
  /**
   * The REVIEWERS' own palette, so a caller wears the colour its vendor has everywhere else.
   *
   * <p>Handed in rather than built here, because the promise is made over a LIST: every view asks
   * for the palette of the CONFIGURED reviewers, never of whoever happens to appear in the thing it
   * is drawing. Building one from the four caller kinds would be a second list and a second
   * assignment, which is the defect `vendorColour.ts` exists to prevent.</p>
   *
   * <p>Optional because a caller with no palette is a legitimate state — the panel always passes
   * one, including the empty-vendor case, where the anchored ids still answer.</p>
   */
  readonly palette?: VendorPalette;
}

/**
 * The edge a caller that is not a vendor gets.
 *
 * <p>The same token `.role-code` uses, and for the same job: this marks a container rather than an
 * entity. "Another client" is not a vendor and must not look like one — and nothing drawn from the
 * palette can then collide with it, which a hashed colour for an unanchored id could.</p>
 */
export const CALLER_NEUTRAL = 'var(--vscode-widget-border)';

/**
 * The stripe for a caller: its vendor's own colour, or a neutral edge.
 *
 * <p>`claude`, `codex` and `gemini` are ANCHORED in the palette, so they answer with the colour they
 * already have on their reviewer card and in a running round whether or not a reviewer of that name
 * is configured. That is the whole of the cross-view promise, and it costs nothing here.</p>
 *
 * <p>Pure, and exported, so the decision is tested by calling it rather than by reading the page it
 * ends up in.</p>
 */
export function callerColour(callerId: string, colour: VendorPalette | undefined): string {
  if (callerId === OTHER_CALLER || colour === undefined) {
    return CALLER_NEUTRAL;
  }

  return colour(callerId);
}

/** The caller kind that is not a vendor: anything this product does not recognise by name. */
const OTHER_CALLER = 'other';

/**
 * One option in a row's vendor picker: what is stored, and what a person reads.
 *
 * <p>The `hint` is the catalogue's own sentence — the one *Add a reviewer* shows under each vendor,
 * saying what it IS and how it runs. One catalogue read the same way twice means both halves of the
 * entry travel, not just the name; a picker showing bare names leaves a person choosing between
 * `DeepSeek` and `OpenRouter` with nothing to choose ON. Empty for a row's own stored entry, whose
 * label already says what it is. (This story's code round, `codex`.)</p>
 */
export interface VendorOption {
  readonly value: string;
  readonly label: string;
  readonly hint: string;
}

/**
 * Everything one caller's row DECIDES, before any of it is markup.
 *
 * <p>`state` is the three the row can be in, and they are genuinely different things rather than
 * degrees of the same one. OFFERED: the catalogue knows this consultant. STRANDED: it does not, but
 * the entry is a definition that says what it runs — a custom endpoint, or a preset this build
 * retired — so it stays selected and labelled from its own fields rather than being replaced by
 * whatever happens to be first. UNAVAILABLE: the rule could not place it at all, and the reason it
 * gives is the one thing a person can act on.</p>
 */
export interface ConsultantRowView {
  readonly caller: { readonly id: string; readonly label: string };
  readonly state: 'offered' | 'stranded' | 'unavailable';
  readonly vendor: string;
  readonly runtime: Runtime | '';
  readonly model: string;
  readonly baseUrl: string;
  readonly executablePath: string;
  readonly options: readonly VendorOption[];
  /**
   * Which option the picker shows as chosen — usually {@link vendor}, and deliberately NOT for an
   * entry the rule could not place.
   *
   * <p>An unplaceable entry can name something the catalogue still offers: `deepseek` names no
   * runtime and needs no reviewer row, so without one it is unavailable while the picker lists it as
   * a working choice. Marking THAT option selected makes choosing it fire no `change` event at all,
   * so the single click a person would try does nothing and the row stays broken — the repair has to
   * be reachable, not merely present. So an unplaceable row selects the option that stores nothing,
   * and every catalogue entry beneath it is one change event away. (codex, C6's code round; the
   * regression came from C5's de-duplication, which removed the other option there was to pick.)</p>
   */
  readonly selected: string;
  readonly models: readonly ModelChoice[];
  /** What the empty model option means HERE — a runtime's default, or that there will be no call. */
  readonly modelPlaceholder: string;
  /**
   * The row's own model when the list above does not hold it — kept, and never dropped under the person.
   *
   * <p>A decision rather than markup, like the rest of this value: emptying a list must not take a
   * saved value off the screen with it, and whether it has to be carried is a question about the
   * model and the list, which a template literal is the wrong place to ask. Empty when the list
   * already offers it, so nothing is ever shown twice.</p>
   */
  readonly keptModel: string;
  /** What the row has to say about its own list right now, or nothing when it has nothing. */
  readonly modelNote: string;
  /** Which of the consultant's own settings this runtime actually has. */
  readonly takesBaseUrl: boolean;
  readonly takesExecutablePath: boolean;
  /** What to think about this pair — said, never decided for the person. */
  readonly hints: readonly string[];
}

/** The empty model option, in the two cases it means genuinely different things. */
const RUNTIME_DEFAULT = 'the runtime’s own default';
const NO_MODEL = 'no model until this consultant is one the build can place';

/**
 * The section's body.
 *
 * <p>One row per CALLER rather than per vendor, and that is the shape of the feature: the question
 * is "when THIS kind of agent is stuck, who does it ask", and the answer is different for each
 * because a model cannot see its own blind spot.</p>
 */
export function consultantBody(consult: ConsultSettings, state: ConsultantViewState): string {
  const { refused } = consultableVendors();

  return `<div class="field">
  <label for="consultEnabled"><input type="checkbox" id="consultEnabled" data-setting="consultEnabled"${consult.enabled ? ' checked' : ''}> Let a stuck AI consult another vendor</label>
  <div class="hint">The AI calls <code>consult</code> itself when it is stuck. The consultant reads this checkout READ-ONLY, with the uncommitted change, and answers advice the AI must verify.</div>
</div>
${CALLER_KINDS.map((caller) => row(consultantRowView(caller, consult, state), callerColour(caller.id, state.palette))).join('\n')}
<div class="hint">These are the CONSULTANT’s own settings. A vendor here shares its name — and so its key in the vault — with the reviewer row of the same name, and nothing else: change a reviewer's model or endpoint and the consultant stays where you put it.</div>
${refused.map(refusal).join('\n')}
<div class="field inline">
  <label for="consultTurns">Turns per consultation</label>
  <input type="number" id="consultTurns" min="1" max="20" data-setting="consultTurns" value="${consult.turns}">
</div>
<div class="field inline">
  <label for="consultCallsPerSession">Calls per session</label>
  <input type="number" id="consultCallsPerSession" min="1" max="100" data-setting="consultCallsPerSession" value="${consult.callsPerSession}">
</div>
<div class="field inline">
  <label for="consultIdleMinutes">Close an idle consultation after, minutes</label>
  <input type="number" id="consultIdleMinutes" min="1" max="240" data-setting="consultIdleMinutes" value="${consult.idleMinutes}">
</div>
${promptField(state.consultPrompt ?? '')}
<div class="hint">A consultation leaves a thread in the vendor's own store holding this repository's uncommitted change — that is what makes a follow-up possible, and it is not ours to delete.</div>`;
}

/**
 * What one caller's row shows, decided — the pure half of this section.
 *
 * <p>Exported because it is what the tests assert. Every question the markup below asks has already
 * been answered here, so a change to the layout cannot change what the section MEANS, and a change
 * to what it means fails a test that never looked at a tag.</p>
 */
export function consultantRowView(
  caller: { id: string; label: string },
  consult: ConsultSettings,
  state: ConsultantViewState,
): ConsultantRowView {
  const resolved = consult.byCaller[caller.id];
  // Read ONCE per row and passed down, rather than asked for again inside each helper.
  const picker = consultableVendors();

  return resolved === undefined || resolved.kind === 'unavailable'
    ? unplaceable(caller, resolved?.vendor ?? '', resolved?.model ?? '', resolved?.why ?? '', picker)
    : placed(caller, resolved, picker, state);
}

/** What a row may offer: the catalogue it can store, and the door to one of your own. */
type Picker = ReturnType<typeof consultableVendors>;

/** A definition: the catalogue knows it, or it stays selected under its own name. */
function placed(
  caller: { id: string; label: string },
  one: { vendor: string; runtime: Runtime; model: string; baseUrl: string; executablePath: string },
  picker: Picker,
  state: ConsultantViewState,
): ConsultantRowView {
  const known = picker.offered.some((preset) => preset.id === one.vendor);
  const placeable = CONSULTING_RUNTIMES.includes(one.runtime);
  // THE ENGINE, when one has been probed for this endpoint. Nothing was passed here at all, so a
  // local consultant had an empty dropdown and its saved model was labelled gone by something that
  // had never asked an engine. (issue #301.)
  const engine = one.runtime === 'local' ? (state.localEngines ?? {})[one.baseUrl] : undefined;
  const models = placeable
    ? modelsFor(one.runtime, state.codexModels ?? [], one.model, engine, state.agyModels ?? [], [], state.claudeProbe)
    : [];

  return {
    caller,
    state: known ? 'offered' : 'stranded',
    vendor: one.vendor,
    runtime: one.runtime,
    model: one.model,
    baseUrl: one.baseUrl,
    executablePath: one.executablePath,
    options: known ? offeredOptions(picker) : [ownOption(one), ...offeredOptions(picker)],
    selected: one.vendor,
    models,
    modelPlaceholder: placeable ? RUNTIME_DEFAULT : NO_MODEL,
    keptModel: kept(one.model, models),
    modelNote: claudeNote(one.runtime, state.askingClaude ?? false),
    takesBaseUrl: placeable && (one.runtime === 'codex' || one.runtime === 'local'),
    takesExecutablePath: placeable && one.runtime !== 'local',
    hints: hintsFor(caller.id, one),
  };
}

/** A saved model the list above does not hold — carried by the row, or empty when it is already there. */
function kept(model: string, models: readonly ModelChoice[]): string {
  return model.length > 0 && !models.some((one) => one.id === model) ? model : '';
}

/**
 * An entry the rule could not place: kept, named, and asked about.
 *
 * <p>No model list, because there is no runtime to ask for one — offering the models of whatever the
 * catalogue lists first would be a choice nobody made, presented as this caller's. The reason comes
 * from the rule that refused it rather than being invented here: it is the one sentence that says
 * what to do, and re-deriving it would be a second answer to a question already answered.</p>
 */
function unplaceable(
  caller: { id: string; label: string },
  vendor: string,
  model: string,
  why: string,
  picker: Picker,
): ConsultantRowView {
  // The stored entry is shown, and it STORES NOTHING. The catalogue can already hold this id —
  // `deepseek` names no runtime and needs no reviewer row — and the two ways of getting that wrong
  // are both real: give this option the entry's own id and one select carries two
  // `<option value="deepseek">`, so a person reaches whichever the browser resolves to; give the
  // catalogue's option the selection instead and choosing it fires no `change` at all, so the row
  // cannot be repaired by the one click anybody would try. An empty value is neither. It cannot be
  // stored — `vendorChosen` refuses an empty id — and it leaves every catalogue entry below it one
  // change event away. (C5's code round found the first; C6's found the second.)
  const own: VendorOption = { value: '', label: `${vendor} — not a consultant this build can place`, hint: '' };

  return {
    caller,
    state: 'unavailable',
    vendor,
    runtime: '',
    model,
    baseUrl: '',
    executablePath: '',
    options: [own, ...offeredOptions(picker)],
    selected: '',
    models: [],
    modelPlaceholder: NO_MODEL,
    keptModel: model,
    modelNote: '',
    takesBaseUrl: false,
    takesExecutablePath: false,
    hints: why.length === 0 ? [] : [why],
  };
}

/** The catalogue as a person reads it — the labels *Add a reviewer* offers, not internal ids. */
function offeredOptions(picker: Picker): readonly VendorOption[] {
  return [
    ...picker.offered.map((preset) => ({ value: preset.id, label: preset.label, hint: preset.hint })),
    ...customOption(picker.custom),
  ];
}

/**
 * The way to an endpoint of your own — last, because it is a door rather than a vendor.
 *
 * <p>The catalogue's blank preset, under the catalogue's own words. Its value is
 * {@link CUSTOM_ENDPOINT}, which the page's script turns into a command instead of a setting write:
 * an id keys the vault entry, so nothing may be stored until a person has given one. C5 left this
 * entry out because it had no flow to mint an id; C6 is that flow, and the ruling — one list of what
 * can be picked — is what puts it back.</p>
 */
function customOption(custom: { label: string; hint: string } | undefined): readonly VendorOption[] {
  return custom === undefined ? [] : [{ value: CUSTOM_ENDPOINT, label: custom.label, hint: custom.hint }];
}

/**
 * A consultant the catalogue does not list, labelled from what it IS.
 *
 * <p>Every custom endpoint is one of these, and so is a preset this build has since retired. Falling
 * through to the first offered entry would show a pair nobody chose and offer it as valid, which is
 * the defect `chatChoice` was fixed for; and an id alone would not say why it is not in the list, so
 * the runtime it runs on goes beside it.</p>
 */
function ownOption(one: { vendor: string; runtime: Runtime; baseUrl: string }): VendorOption {
  return {
    value: one.vendor,
    label: `${one.vendor} — your own, on ${one.runtime}${one.baseUrl.length > 0 ? ` at ${one.baseUrl}` : ''}`,
    hint: '',
  };
}

/**
 * What to think about this row — said beside it, never acted on for the person.
 *
 * <p>The second one is this build telling the truth about itself. A consultant on the codex runtime
 * that carries a base URL is refused BY THE SERVER — `ConsultantResolution` matches
 * `"codex" when vendor.BaseUrl.Length == 0` and answers `CannotConsult` for everything else, which
 * `ConsultantsTests` pins — so DeepSeek, OpenRouter and any custom endpoint are storable here and
 * not runnable. Teaching that runtime custom endpoints is a separate, measured change the file
 * defers deliberately. Until then the section says so, because offering a choice that fails silently
 * is worse than offering one that explains itself.</p>
 */
function hintsFor(
  callerKind: string,
  one: { runtime: Runtime; baseUrl: string },
): readonly string[] {
  const own = sameVendorNote(callerKind, one.runtime);
  const endpoint = one.runtime === 'codex' && one.baseUrl.length > 0
    ? 'this build cannot hold a consultation through a custom endpoint on the Codex CLI — the server refuses it by name. The setting is kept; it will run when that runtime learns to.'
    : '';

  return [own, foreignRuntime(one.runtime), endpoint].filter((hint) => hint.length > 0);
}

/**
 * The other half of `CannotConsult`, said where the entry is chosen rather than when it fails.
 *
 * <p>The picker cannot offer one of these — `consultableVendors` filters the catalogue by
 * {@link CONSULTING_RUNTIMES} — but a stored entry can BE one, because rule (a) materialises
 * whatever runtime the reviewer row it names is on, `remote` included. Materialising is not
 * permitting and never was: `ConsultationService.OnTheVendorAsync` asks `ConsultantResolution.For`
 * before anything is launched and refuses by name, so a consultation is not routed at a Team server
 * by this or any other path. What was missing is that the SECTION said nothing — the row offered a
 * CLI path and a model list, and a person read three sentences about settings that will never be
 * read and none about the refusal already waiting for them. (This story's plan round, `codex`.)</p>
 *
 * <p>The entry itself is untouched, as rule (c) is untouched: a stored choice is never rewritten
 * under the person, because the id keys their vault entry. It is named, and the way out is the
 * catalogue sitting in the same select.</p>
 */
function foreignRuntime(runtime: Runtime): string {
  return CONSULTING_RUNTIMES.includes(runtime)
    ? ''
    : `this build cannot hold a consultation on '${runtime}' — the server refuses it by name. `
      + `Consultants run on: ${CONSULTING_RUNTIMES.join(', ')}. The setting is kept; pick one of those above to use it.`;
}

/** One caller's row: which vendor answers it, on which model, and with what of its own. */
function row(view: ConsultantRowView, colour: string): string {
  const caller = escapeHtml(view.caller.id);

  // The colour is INLINE and the frame is a class, exactly as a reviewer card does it: the width and
  // the fallback belong in the stylesheet, the hue is computed per caller and cannot be named by a
  // class. CSP-legal — this page declares style-src 'unsafe-inline'.
  return `<div class="field consultant-row" data-caller="${caller}" style="border-left-color:${colour}">
  <label for="consultVendor-${caller}">${escapeHtml(view.caller.label)} asks</label>
  <select id="consultVendor-${caller}" data-setting="consultVendor" data-caller="${caller}">
${view.options.map((one) => option(one.value, one.label, view.selected, one.hint)).join('\n')}
  </select>
  <select id="consultModel-${caller}" data-setting="consultModel" data-caller="${caller}">
${option('', view.modelPlaceholder, view.model)}
${view.models.map((model) => option(model.id, model.label, view.model)).join('\n')}
${view.keptModel.length === 0 ? '' : option(view.keptModel, `${view.keptModel} — not offered by this vendor`, view.model)}
  </select>
${view.takesBaseUrl ? field(caller, 'consultBaseUrl', 'Endpoint', view.baseUrl, 'https://api.example.com/v1') : ''}
${view.takesExecutablePath ? field(caller, 'consultExecutablePath', 'Where its CLI is', view.executablePath, 'leave empty to look it up on PATH') : ''}
${view.modelNote.length === 0 ? '' : `  <div class="hint">${LOOKING}${escapeHtml(view.modelNote)}</div>`}
${view.hints.map((hint) => `  <div class="hint">${escapeHtml(hint)}</div>`).join('\n')}
</div>`;
}

/** One of the consultant's OWN settings, keyed by caller so the write path knows whose it is. */
function field(caller: string, setting: string, label: string, value: string, placeholder: string): string {
  return `  <label for="${setting}-${caller}">${escapeHtml(label)}</label>
  <input type="text" id="${setting}-${caller}" data-setting="${setting}" data-caller="${caller}" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder)}">`;
}

function option(value: string, label: string, selected: string, hint = ''): string {
  const title = hint.length === 0 ? '' : ` title="${escapeHtml(hint)}"`;

  return `    <option value="${escapeHtml(value)}"${value === selected ? ' selected' : ''}${title}>${escapeHtml(label)}</option>`;
}

/**
 * A catalogue entry that cannot consult, NAMED with its reason.
 *
 * <p>Never filtered away: a vendor a person can see offered one section above, silently missing from
 * this picker, is a person hunting for something that is right in front of them.</p>
 */
function refusal(entry: { id: string; label: string; why: string }): string {
  return `<div class="hint stale">${escapeHtml(entry.label)} cannot consult — ${escapeHtml(entry.why)}.</div>`;
}

/**
 * What the consultant is asked to do, and the way back to the shipped words.
 *
 * <p>`data-file` rather than a bare `data-setting`, because this box is not a setting: it is written
 * to the server's own prompt override, which is where the server reads it from. The attribute is
 * what tells the declared-settings test that, so the exemption is a fact in the markup rather than a
 * name on a list somewhere else.</p>
 *
 * <p>*Restore default* is a BUTTON rather than an empty box, so taking the override away is
 * something a person did on purpose. Emptying the box does the same thing — there is no such thing
 * as a prompt that says nothing — but nobody has to discover that to get back.</p>
 */
function promptField(prompt: string): string {
  return `<div class="field">
  <label for="consultPrompt">What the consultant is asked to do</label>
  <textarea id="consultPrompt" rows="6" data-setting="consultPrompt" data-file="consult.md" placeholder="The prompt this build ships with.">${escapeHtml(prompt)}</textarea>
  <div class="hint">Empty is the prompt this build ships with — the panel cannot show you those words, because they are compiled into the server. What you type here is written to its prompt override and read on the next consultation. <b>Emptying the box is the same as pressing Restore default</b>: there is no such thing as a prompt that says nothing.</div>
  <button type="button" class="link" data-command="restoreConsultPrompt">Restore default</button>
</div>`;
}
