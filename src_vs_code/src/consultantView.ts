/**
 * The *Consultant* section: who answers each kind of caller when it is stuck, and the caps.
 *
 * <p>Pure markup from a value, like every other section body — no `vscode`, so the whole section is
 * a unit test rather than something only a running window can show.</p>
 */

import { CALLER_KINDS, ConsultSettings, consultableVendors, sameVendorNote } from './consultSettings';
import { Vendor } from './vendors';
import { escapeHtml } from './escapeHtml';
import { ModelChoice, Runtime, modelsFor } from './models';

/** What the section needs beyond the settings themselves: the rows a person configured. */
export interface ConsultantViewState {
  readonly vendors: readonly Vendor[];
  /** Models the codex CLI reported, if it has been asked. Empty is normal and not an error. */
  readonly codexModels?: readonly ModelChoice[];
  readonly agyModels?: readonly ModelChoice[];
  /** The prompt override as it is ON DISK, or empty for "the one this build ships with". */
  readonly consultPrompt?: string | undefined;
}

/**
 * The section's body.
 *
 * <p>One row per CALLER rather than per vendor, and that is the shape of the feature: the question
 * is "when THIS kind of agent is stuck, who does it ask", and the answer is different for each
 * because a model cannot see its own blind spot.</p>
 */
export function consultantBody(consult: ConsultSettings, state: ConsultantViewState): string {
  const { offered, refused } = consultableVendors(state.vendors);

  return `<div class="field">
  <label for="consultEnabled"><input type="checkbox" id="consultEnabled" data-setting="consultEnabled"${consult.enabled ? ' checked' : ''}> Let a stuck AI consult another vendor</label>
  <div class="hint">The AI calls <code>consult</code> itself when it is stuck. The consultant reads this checkout READ-ONLY, with the uncommitted change, and answers advice the AI must verify.</div>
</div>
${offered.length === 0 ? noVendors() : CALLER_KINDS.map((caller) => row(caller, consult, offered, state)).join('\n')}
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

/** One caller's row: which vendor answers it, on which model, and what to think about the pair. */
function row(
  caller: { id: string; label: string },
  consult: ConsultSettings,
  offered: readonly Vendor[],
  state: ConsultantViewState,
): string {
  const chosen = consult.byCaller[caller.id];
  const vendor = offered.find((one) => one.id === chosen.vendor);
  // A saved vendor that no longer resolves is STRANDED in the list rather than replaced: falling
  // through to the first offered row would show a pair nobody chose and offer it as valid, which is
  // the defect `chatChoice` was fixed for.
  const stranded = vendor === undefined && chosen.vendor.length > 0;
  // The SAME list a reviewer card offers, from the same function — a consultant is an ordinary
  // vendor row, and a second curated list would be a second thing to keep in step with every CLI.
  // A local engine's models are not asked for here: the engine is only consulted where a reviewer
  // card already has one in hand, and passing none costs the saved value nothing (it is kept and
  // marked below).
  const models: readonly ModelChoice[] = vendor === undefined
    ? []
    : modelsFor(
      vendor.runtime as Runtime,
      state.codexModels ?? [],
      chosen.model,
      undefined,
      state.agyModels ?? [],
    );
  const note = vendor === undefined ? '' : sameVendorNote(caller.id, vendor.runtime);

  return `<div class="field consultant-row" data-caller="${escapeHtml(caller.id)}">
  <label for="consultVendor-${escapeHtml(caller.id)}">${escapeHtml(caller.label)} asks</label>
  <select id="consultVendor-${escapeHtml(caller.id)}" data-setting="consultVendor" data-caller="${escapeHtml(caller.id)}">
${offered.map((one) => option(one.id, `${one.id}${one.model.length > 0 ? ` · ${one.model}` : ''}`, chosen.vendor)).join('\n')}
${stranded ? option(chosen.vendor, `${chosen.vendor} — not configured any more`, chosen.vendor) : ''}
  </select>
  <select id="consultModel-${escapeHtml(caller.id)}" data-setting="consultModel" data-caller="${escapeHtml(caller.id)}">
${option('', vendor === undefined ? 'the row’s own model' : `the row’s own${vendor.model.length > 0 ? ` — ${vendor.model}` : ''}`, chosen.model)}
${models.map((model) => option(model.id, model.label, chosen.model)).join('\n')}
${chosen.model.length > 0 && !models.some((one) => one.id === chosen.model) ? option(chosen.model, `${chosen.model} — not offered by this vendor`, chosen.model) : ''}
  </select>
${note.length === 0 ? '' : `  <div class="hint">${escapeHtml(note)}</div>`}
</div>`;
}

function option(value: string, label: string, selected: string): string {
  return `    <option value="${escapeHtml(value)}"${value === selected ? ' selected' : ''}>${escapeHtml(label)}</option>`;
}

/**
 * A row that cannot consult, NAMED with its reason.
 *
 * <p>Never filtered away: a vendor a person can see configured, silently missing from a picker, is a
 * person hunting for something that is right in front of them.</p>
 */
function refusal(row: { vendor: Vendor; why: string }): string {
  return `<div class="hint stale">${escapeHtml(row.vendor.id)} cannot consult — ${escapeHtml(row.why)}.</div>`;
}

function noVendors(): string {
  return '<div class="hint stale">No configured vendor can hold a consultation yet. Add one in Reviewers first — a consultant is an ordinary vendor row.</div>';
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
