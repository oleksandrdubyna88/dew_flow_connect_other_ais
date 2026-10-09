import { rowPicks } from './catalogPicks';
import { escapeHtml as esc } from './escapeHtml';
import { SECURITY_SEED } from './securityLane.generated';
import {
  DEFAULT_SECURITY, SECURITY_MOST_PROMPTS, SECURITY_MOST_RUNS, SECURITY_SINCE, SECURITY_STAGES, securityLaneProblem,
  securitySupported, securityTokenBudget, securityWrite, type SecurityLane, type SecurityRun,
} from './securityLane';
import { promptState, promptsInOrder } from './securityLaneState';
import { promptCard } from './securityPromptCard';
import type { SecurityTextState } from './securityPromptFiles';
import type { Vendor } from './vendors';

/**
 * The Security lane tab (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 3): the switches, the tag legend in two
 * columns, one card per prompt in the order the person reads them, and the reviewer / prompt pairs with real buttons.
 */

/** What the host read about the prompt files: each prompt's text state, and the folder they live in. */
export interface SecurityLaneFiles {
  readonly text: Readonly<Record<string, SecurityTextState>>;
  readonly promptsDir: string;
}

/** The pair's Prompt select ends in this: not a prompt, a request to make one (`securityLaneScript.ts`). */
export const NEW_PROMPT_SENTINEL = '__newSecurityPrompt__';

const at = (field: string): string => `data-setting="securityLane" data-security-field="${esc(field)}"`;
const check = (field: string, on: boolean, label: string, disabled = false): string => `<label><input type="checkbox" ${at(field)}${on ? ' checked' : ''}${disabled ? ' disabled' : ''}> ${esc(label)}</label>`;
const count = (field: string, value: number, min: number, max: number): string => `<input type="number" ${at(field)} min="${min}" max="${max}" value="${esc(String(value))}">`;
const option = (id: string, value: string): string => `<option value="${esc(id)}"${id === value ? ' selected' : ''}>${esc(id)}</option>`;
/** A dropdown that keeps an unknown current value as an option; `extra` adds attributes to the select itself. */
const select = (field: string, value: string, choices: readonly string[], extra = ''): string => {
  const options = [...new Set([value, ...choices])].map(id => option(id, value)).join('');
  return `<select ${at(field)}${extra}>${options}</select>`;
};
const legend = (): string => '<ul class="seclane-tags">' + SECURITY_SEED.signals.map(s =>
  `<li><code>${esc(s.id)}</code> ${esc(s.label)}${s.trigger ? '' : ' (focus only)'}</li>`).join('') + '</ul>';

/**
 * @param vendors the rows a pair may name — the rows ticked Security lane on Models
 * @param allRows every catalog row: the ordinary gate is judged on them, and a pair's row that is not in `vendors` is
 *   named from them (research/PLAN_one_model_catalog.md E4.2)
 */
export function securityLaneBody(lane: SecurityLane, vendors: readonly Vendor[], version: string, files: SecurityLaneFiles, allRows: readonly Vendor[]): string {
  if ('invalidConfiguration' in lane) return malformedNote(lane.invalidConfiguration);
  const old = !securitySupported(version);
  return versionNote(version)
    + (!allRows.some(v => v.enabled && (v.code || v.feature)) ? '<p class="stale">Enable an ordinary code or feature reviewer too: the security lane cannot replace the ordinary gate.</p>' : '')
    + `<p>Additional security reviews beside the ordinary gate. Prompt text stays in your prompt files.</p>
      <p>${check('enabled', lane.enabled, 'Enable security lane', old)}</p>
      <label>Allowed major/blocking findings ${count('threshold', lane.threshold, 0, 100)}</label>
      <label>Maximum rounds ${count('maxRounds', lane.maxRounds, 1, 10)}</label>
      <h3>Prompts</h3>
      <p>Pair a prompt with a reviewer by ticking the reviewer on its card. Its conditions decide whether it runs: a shipped
      preset runs when changed code matches one of them, and redteam-general runs on every code change. Your own prompt is
      made with + New custom prompt, and its text lives in the file its card names.</p>`
    + legend()
    + promptsInOrder(lane).map(p => promptCard({
      prompt: p, state: promptState(p, files.text[p.id] ?? 'none'), text: files.text[p.id] ?? 'none', vendors, old,
      paired: vendor => lane.runs.some(r => r.vendor === vendor && r.prompt === p.id), promptsDir: files.promptsDir,
    })).join('')
    + promptsFooter(lane)
    + '<h3>Reviewer / prompt pairs</h3>'
    + lane.runs.map((r, i) => runBody(r, i, lane, vendors, strandedNote(r.vendor, vendors, allRows))).join('')
    + runsFooter(lane, vendors);
}

/** The prompt count, the button that makes one, and the prompts an over-full legacy lane loses. */
function promptsFooter(lane: SecurityLane): string {
  const full = lane.prompts.length >= SECURITY_MOST_PROMPTS;
  const lost = lane.prompts.slice(SECURITY_MOST_PROMPTS).map(p => p.id);
  return '<p class="seclane-actions">'
    + `<button type="button" data-command="newSecurityPrompt" data-id=""${full ? ' disabled' : ''}>+ New custom prompt</button>`
    + `<span class="seclane-count">Prompts: ${lane.prompts.length} of ${SECURITY_MOST_PROMPTS} (${DEFAULT_SECURITY.prompts.length} shipped)${full ? ' — the library is full' : ''}</span></p>`
    + (lost.length === 0 ? '' : `<p class="stale">The server reads at most ${SECURITY_MOST_PROMPTS} prompts and drops ${esc(lost.join(', '))}.</p>`);
}

/** The pair count and + Add pair — disabled, with the reason as text, when pressing it could add nothing. */
function runsFooter(lane: SecurityLane, vendors: readonly Vendor[]): string {
  const canAdd = securityWrite(lane, 'addRun', true, vendors) !== lane;
  const why = canAdd ? '' : whyNoPair(lane, vendors);
  return '<p class="seclane-actions">'
    + `<button type="button" data-command="addSecurityRun" data-id=""${canAdd ? '' : ' disabled'}>+ Add reviewer / prompt pair</button>`
    + `<span class="seclane-count">Pairs: ${lane.runs.length} of ${SECURITY_MOST_RUNS}${why}</span></p>`;
}

/** Why + Add pair can add nothing — said as text beside it, never only as a disabled look. */
function whyNoPair(lane: SecurityLane, vendors: readonly Vendor[]): string {
  if (lane.runs.length >= SECURITY_MOST_RUNS) return ' — the most a lane holds';
  return vendors.some(v => v.enabled) ? ' — every enabled reviewer is already paired with every prompt it can take'
    : ' — no model ticked security lane is switched on; tick or switch one on, on Models';
}

/** Where the malformed value lives and what is wrong with it — the setting a person can open, never the panel's stand-in. */
function malformedNote(stored: unknown): string {
  const problem = securityLaneProblem(stored);
  return '<p class="stale">Security lane is off: coai.securityLane in your settings JSON is malformed'
    + (problem === '' ? '' : ` (${esc(problem)})`) + '. Correct it there.</p>';
}

function versionNote(version: string): string {
  if (!securitySupported(version)) return `<p class="stale">The installed MCP server ${esc(version)} does not run this lane. Update to ${SECURITY_SINCE} or later.</p>`;
  if (version === '' || version === '0.0.0') return '<p>Server version is unverified; security settings will be sent, but lane support has not been confirmed.</p>';
  return '';
}

/**
 * One pair. Its Remove button and its Prompt select's "+ New custom prompt…" carry the pair's IDENTITY — row, reviewer,
 * prompt — so a pair that moved between the paint and the press is refused rather than the wrong one written (D5).
 */
function runBody(r: SecurityRun, i: number, lane: SecurityLane, vendors: readonly Vendor[], stranded: string): string {
  const v = vendors.find(v => v.id === r.vendor);
  const context = r.context ?? defaultContext(v);
  const field = (key: string): string => 'run:' + i + ':' + key;
  const stages = r.stages ?? SECURITY_STAGES;
  const identity = JSON.stringify([i, r.vendor, r.prompt]);
  const prompts = promptsInOrder(lane).map(p => p.id);
  const promptSelect = select(field('prompt'), r.prompt, prompts, ` data-seclane-run="${esc(identity)}"`)
    .replace('</select>', `<option value="${NEW_PROMPT_SENTINEL}">+ New custom prompt…</option></select>`);
  return `<fieldset><legend>Pair ${i + 1}${securityOnly(v) ? ' — security only' : ''}</legend>
        ${runWarnings(r, lane, v, stranded)}
        <label>Reviewer ${select(field('vendor'), r.vendor, vendors.map(v => v.id))}</label>
        <label>Prompt ${promptSelect}</label>
        <label>Source ${select(field('context'), context, ['slice', 'diff'])}</label>
        <label>Context token budget ${count(field('contextTokens'), securityTokenBudget(r, context), 1024, 200000)}</label>
        ${check(field('code'), stages.includes('code'), 'Code')}
        ${check(field('feature'), stages.includes('feature'), 'Feature')}
        <p class="seclane-actions"><button type="button" data-command="removeSecurityRun" data-id="${esc(identity)}" data-seclane-then="addSecurityRun" data-seclane-then-id="">Remove pair</button></p></fieldset>`;
}
const defaultContext = (v: Vendor | undefined): string => v?.runtime === 'local' ? 'slice' : 'diff';
const securityOnly = (v: Vendor | undefined): boolean => v !== undefined && ![v.code, v.plan, v.document, v.feature].some(Boolean);

/**
 * A pair's row the page does not offer, named (research/PLAN_one_model_catalog.md E4.2, D3): a row that is gone or not
 * ticked Security lane — '' when the page offers it.
 */
function strandedNote(vendor: string, offered: readonly Vendor[], allRows: readonly Vendor[]): string {
  return offered.some(v => v.id === vendor) ? '' : rowPicks('security', vendor, allRows, 'This pair').note;
}

/** What to say about a pair's reviewer: stranded, switched off or gone — or ''. */
function reviewerWarning(run: SecurityRun, vendor: Vendor | undefined, stranded: string): string {
  if (stranded.length > 0) return stranded;
  return vendor?.enabled ? '' : `Reviewer ${run.vendor} is unavailable; select an enabled reviewer.`;
}

function runWarnings(run: SecurityRun, lane: SecurityLane, vendor: Vendor | undefined, stranded: string): string {
  const reviewer = reviewerWarning(run, vendor, stranded);
  const warnings: string[] = reviewer.length > 0 ? [reviewer] : [];
  if (!lane.prompts.some(p => p.id === run.prompt)) warnings.push(`Prompt ${run.prompt} is missing; select a registered prompt.`);
  return warnings.map(message => `<p class="stale">${esc(message)}</p>`).join('');
}
