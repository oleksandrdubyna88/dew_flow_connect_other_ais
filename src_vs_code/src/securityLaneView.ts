import { escapeHtml as esc } from './escapeHtml';
import { SECURITY_SEED } from './securityLane.generated';
import {
  SECURITY_SINCE, SECURITY_STAGES, securityLaneProblem, securitySupported, securityTokenBudget,
  type SecurityLane, type SecurityPrompt, type SecurityRun,
} from './securityLane';
import type { Vendor } from './vendors';

const at = (field: string): string => `data-setting="securityLane" data-security-field="${esc(field)}"`;
const check = (field: string, on: boolean, label: string, disabled = false): string => `<label><input type="checkbox" ${at(field)}${on ? ' checked' : ''}${disabled ? ' disabled' : ''}> ${esc(label)}</label>`;
const count = (field: string, value: number, min: number, max: number): string => `<input type="number" ${at(field)} min="${min}" max="${max}" value="${esc(String(value))}">`;
const option = (id: string, value: string): string => `<option value="${esc(id)}"${id === value ? ' selected' : ''}>${esc(id)}</option>`;
const select = (field: string, value: string, choices: readonly string[]): string => {
  const options = [...new Set([value, ...choices])].map(id => option(id, value)).join('');
  return `<select ${at(field)}>${options}</select>`;
};
const signalLabels = (): string => SECURITY_SEED.signals.map(s =>
  `<code>${esc(s.id)}</code> (${esc(s.label)}${s.trigger ? '' : '; focus only'})`).join(', ');

export function securityLaneBody(lane: SecurityLane, vendors: readonly Vendor[], version: string): string {
  if ('invalidConfiguration' in lane) return malformedNote(lane.invalidConfiguration);
  const old = !securitySupported(version);
  return versionNote(version)
    + (!vendors.some(v => v.enabled && (v.code || v.feature)) ? '<p class="stale">Enable an ordinary code or feature reviewer too: the security lane cannot replace the ordinary gate.</p>' : '')
    + `<p>Additional security reviews beside the ordinary gate. Prompt text stays in your prompt files.</p>
      <p>${check('enabled', lane.enabled, 'Enable security lane', old)}</p>
      <label>Allowed major/blocking findings ${count('threshold', lane.threshold, 0, 100)}</label>
      <label>Maximum rounds ${count('maxRounds', lane.maxRounds, 1, 10)}</label>
      <h3>Prompts</h3><p>Tick each check for the reviewer that should run it. Matching code conditions are required for the twelve presets; removing every trigger disables their execution. Custom prompts with no triggers run every time. Focus prioritizes source. At most 16 reviewer / prompt pairs.</p>
      <p>${signalLabels()}</p>`
    + lane.prompts.map(p => promptBody(p, lane, vendors, old)).join('')
    + `<label>Add prompt (redteam-name) <input type="text" ${at('addPrompt')} value=""></label><h3>Reviewer / prompt pairs</h3>`
    + lane.runs.map((r, i) => runBody(r, i, lane, vendors)).join('')
    + `<p>${check('addRun', false, 'Add reviewer / prompt pair')}</p>`;
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

function runBody(r: SecurityRun, i: number, lane: SecurityLane, vendors: readonly Vendor[]): string {
  const v = vendors.find(v => v.id === r.vendor);
  const context = r.context ?? defaultContext(v);
  const field = (key: string): string => 'run:' + i + ':' + key;
  const stages = r.stages ?? SECURITY_STAGES;
  return `<fieldset><legend>Pair ${i + 1}${securityOnly(v) ? ' — security only' : ''}</legend>
        ${runWarnings(r, lane, v)}
        <label>Reviewer ${select(field('vendor'), r.vendor, vendors.map(v => v.id))}</label>
        <label>Prompt ${select(field('prompt'), r.prompt, lane.prompts.map(p => p.id))}</label>
        <label>Source ${select(field('context'), context, ['slice', 'diff'])}</label>
        <label>Context token budget ${count(field('contextTokens'), securityTokenBudget(r, context), 1024, 200000)}</label>
        ${check(field('code'), stages.includes('code'), 'Code')}
        ${check(field('feature'), stages.includes('feature'), 'Feature')}
        ${check(field('remove'), false, 'Remove pair')}</fieldset>`;
}
const defaultContext = (v: Vendor | undefined): string => v?.runtime === 'local' ? 'slice' : 'diff';
const securityOnly = (v: Vendor | undefined): boolean => v !== undefined && ![v.code, v.plan, v.document, v.feature].some(Boolean);

function runWarnings(run: SecurityRun, lane: SecurityLane, vendor: Vendor | undefined): string {
  const warnings: string[] = [];
  if (!vendor?.enabled) warnings.push(`Reviewer ${run.vendor} is unavailable; select an enabled reviewer.`);
  if (!lane.prompts.some(p => p.id === run.prompt)) warnings.push(`Prompt ${run.prompt} is missing; select a registered prompt.`);
  return warnings.map(message => `<p class="stale">${esc(message)}</p>`).join('');
}

const triggerWarning = (p: SecurityPrompt, preset: boolean): string => preset && p.triggers.length === 0
  ? `<p class="stale">${esc(p.id)} has no triggers; select at least one condition to run this preset.</p>` : '';

function promptBody(p: SecurityPrompt, lane: SecurityLane, vendors: readonly Vendor[], old: boolean): string {
  const preset = SECURITY_SEED.prompts.some(seed => seed.id === p.id);
  return `<fieldset><legend>${esc(p.id)}</legend>
    ${triggerWarning(p, preset)}
    <p>${vendors.filter(v => v.enabled).map(v => check('pair:' + v.id + ':' + p.id,
      lane.runs.some(r => r.vendor === v.id && r.prompt === p.id), v.id, old)).join(' ')}</p>
    <p>Repository prompt: <code>src_mcp/src/prompts/${esc(p.id)}.md</code></p>
    <button data-command="editSecurityPrompt" data-id="${esc(p.id)}">Edit local prompt override</button>
    <p>Run when code matches: ${SECURITY_SEED.signals.filter(s => s.trigger).map(s =>
      check('trigger:' + p.id + ':' + s.id, p.triggers.includes(s.id), s.label)).join(' ')}</p>
    <p>Prioritize source: ${SECURITY_SEED.signals.map(s =>
      check('focus:' + p.id + ':' + s.id, p.focus.includes(s.id), s.label)).join(' ')}</p>
    <label>All trigger tags <input type="text" ${at('prompt:' + p.id + ':triggers')} value="${esc(p.triggers.join(', '))}"></label>
    <label>All focus tags <input type="text" ${at('prompt:' + p.id + ':focus')} value="${esc(p.focus.join(', '))}"></label>
    ${preset ? '' : check('prompt:' + p.id + ':remove', false, 'Remove custom prompt')}</fieldset>`;
}
