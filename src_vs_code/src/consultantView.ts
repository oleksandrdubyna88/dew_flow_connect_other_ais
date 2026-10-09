/**
 * The *Consultant* section: who answers each kind of caller when it is stuck, and the caps.
 *
 * <p>Pure markup from a value, like every other section body — no `vscode`, so the whole section is
 * a unit test rather than something only a running window can show.</p>
 *
 * <p>Who each caller asks is not drawn here: the Settings page hands in its picks from the catalog
 * (`consultantPicks.ts`, todo/PLAN_one_model_catalog.md E4.2), and this body draws the switch, the caps and the
 * prompt around them.</p>
 */

import { ConsultSettings } from './consultSettings';
import { escapeHtml } from './escapeHtml';
import { help } from './panelControls';

/** What the section needs beyond the settings themselves. */
export interface ConsultantViewState {
  /** The prompt override as it is ON DISK, or empty for "the one this build ships with". */
  readonly consultPrompt?: string | undefined;
}

/**
 * The section's body.
 *
 * <p>One row per CALLER rather than per vendor, and that is the shape of the feature: the question
 * is "when THIS kind of agent is stuck, who does it ask", and the answer is different for each
 * because a model cannot see its own blind spot.</p>
 *
 * @param callerRows each caller's pick from the catalog (`consultantPicksHtml`), drawn under the switch
 */
export function consultantBody(consult: ConsultSettings, state: ConsultantViewState, callerRows: string): string {
  return `<div class="field">
  <div class="check-row"><label for="consultEnabled"><input type="checkbox" id="consultEnabled" data-setting="consultEnabled"${consult.enabled ? ' checked' : ''}> Let an AI consult another vendor</label>${help('consultEnabled')}</div>
  <div class="hint">The AI calls <code>consult</code> itself when it is stuck, and the gate orders one for a group of epics or a risky piece when the consultation cadence is on. The consultant reads this checkout READ-ONLY, with the uncommitted change, and answers advice the AI must verify.</div>
</div>
${callerRows}
<div class="field inline">
  <label for="consultTurns">${help('consultTurns')}Turns per consultation</label>
  <input type="number" id="consultTurns" min="1" max="20" data-setting="consultTurns" value="${consult.turns}">
</div>
<div class="field inline">
  <label for="consultCallsPerSession">${help('consultCallsPerSession')}Calls per session</label>
  <input type="number" id="consultCallsPerSession" min="1" max="100" data-setting="consultCallsPerSession" value="${consult.callsPerSession}">
</div>
<div class="field inline">
  <label for="consultIdleMinutes">${help('consultIdleMinutes')}Close an idle consultation after, minutes</label>
  <input type="number" id="consultIdleMinutes" min="1" max="240" data-setting="consultIdleMinutes" value="${consult.idleMinutes}">
</div>
${promptField(state.consultPrompt ?? '')}
<div class="hint">A consultation leaves a thread in the vendor's own store holding this repository's uncommitted change — that is what makes a follow-up possible, and it is not ours to delete.</div>`;
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
  <label for="consultPrompt">${help('consultPrompt')}What the consultant is asked to do</label>
  <textarea id="consultPrompt" rows="6" data-setting="consultPrompt" data-file="consult.md" placeholder="The prompt this build ships with.">${escapeHtml(prompt)}</textarea>
  <div class="hint">Empty is the prompt this build ships with — the panel cannot show you those words, because they are compiled into the server. What you type here is written to its prompt override and read on the next consultation. <b>Emptying the box is the same as pressing Restore default</b>: there is no such thing as a prompt that says nothing.</div>
  <button type="button" class="link" data-command="restoreConsultPrompt">Restore default</button>
</div>`;
}
