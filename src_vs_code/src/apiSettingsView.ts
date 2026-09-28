import { API_SETTINGS_SINCE, MAX_REVIEW_MINUTES, type ApiReport, type ApiSettingKey, type ApiSettingsRow } from './apiSettings';
import { escapeHtml } from './escapeHtml';
import { help } from './panelControls';
import type { ProviderHealth } from './providers';

/**
 * The per-model settings on an `api` card (story S3.8 of `todo/PLAN_feature_review.md`, the extension half):
 * the thinking switch, the effort dropdown and the review time limit, each with its `?` and — while the row
 * sets a value of its own — a "reset to calibrated default".
 *
 * <p>Everything a control offers is what `coai-mcp` REPORTED for the row (`ProviderHealth.api`): the levels
 * are the module's, the switch is there only when the module says it is, the defaults are what calibration
 * settled, and the refusal and the set-aside note are the server's own sentences. With no report the
 * controls are not drawn at all — never drawn from a list typed here — and the card says why.</p>
 *
 * <p>Its own module because `panelView.ts` is several times the size the coding rule allows; it imports
 * `help` from `panelControls`, which is how `helpTooltips.test.ts` finds the tooltips it attaches.</p>
 */

/** The row as this block reads it. */
type Row = ApiSettingsRow & { readonly id: string };

/** The words beside a value the row did not set: calibrated for a measured module, plain for the generic one. */
function defaultWord(report: ApiReport): string {
  return report.measuredModel.length > 0 ? 'calibrated default' : 'default';
}

/**
 * The block for one card — or nothing, for a row that is not `api`. A card switched off because the installed
 * server does not know the runtime asks for no block at all (that card already says why, and none of its
 * controls work) — the caller decides that, so this function has one job. `id` is the row's id, already
 * escaped by the card.
 */
export function apiSettingsFields(row: Row, id: string, health: ProviderHealth | undefined, serverVersion: string): string {
  return row.runtime === 'api' ? forApiRow(row, id, health, serverVersion) : '';
}

/** The controls from the server's report, or — with no report — the sentence saying why there are none. */
function forApiRow(row: Row, id: string, health: ProviderHealth | undefined, serverVersion: string): string {
  const report = health?.api;

  return report === undefined ? withheld(health, serverVersion) : controls(row, id, report);
}

/**
 * Why the controls are not there. Two reasons, and a person can act on only one of them: an installed server
 * that answered for this row without reporting what the module takes is too old; a row nobody reported on
 * — no probe answer yet, or a switched-off reviewer the settings file does not send — is simply not known.
 */
function withheld(health: ProviderHealth | undefined, serverVersion: string): string {
  const installed = serverVersion.length > 0 ? ` (${serverVersion})` : '';
  const sentence = health === undefined
    ? 'Thinking, effort and the review time limit are set here once coai-mcp reports what this model accepts — '
      + 'it has not reported this row (a reviewer that is switched off is not sent to it).'
    : `The coai-mcp you have installed${installed} does not report what this model accepts, so its thinking, effort `
      + `and review time limit cannot be set here. They arrive with coai-mcp ${API_SETTINGS_SINCE} — update it in the `
      + 'MCP server section below.';

  return `\n  <div class="field api-settings"><div class="hint">${escapeHtml(sentence)}</div></div>`;
}

function controls(row: Row, id: string, report: ApiReport): string {
  return `
  <div class="field api-settings">
    ${thinkingControl(row, id, report)}
    ${effortControl(row, id, report)}
    ${minutesControl(row, id, report)}
    ${runsWith(report)}${serverSaid(report)}
  </div>`;
}

/**
 * The switch, where the vendor documents one — drawn from what the row asked for, else what it spells as an
 * effort (`none` on qwen3.8-max is the switch written as a level), else the default.
 */
function thinkingControl(row: Row, id: string, report: ApiReport): string {
  if (!report.capabilities.thinkingSwitchable) {
    return `<div class="check-row"><span class="hint">thinking cannot be switched off for this model</span>${help('apiThinking')}${reset(row, id, 'thinking', report)}</div>`;
  }
  const on = thinkingShown(row, report);

  return `<div class="check-row"><label class="check"><input type="checkbox" data-setting="thinking" data-vendor="${id}"${on ? ' checked' : ''}> thinking</label>`
    + `${help('apiThinking')}${reset(row, id, 'thinking', report)}</div>`;
}

function thinkingShown(row: Row, report: ApiReport): boolean {
  const offLevel = report.capabilities.thinkingOffLevel;
  const spelledOff = offLevel.length > 0 && row.effort === offLevel;

  return row.thinking ?? (spelledOff ? false : report.defaults.thinkingOn);
}

/** Exactly the module's levels, the default marked — or, when it declares none, a sentence instead of a list. */
function effortControl(row: Row, id: string, report: ApiReport): string {
  const levels = report.capabilities.effortLevels;
  if (levels.length === 0) {
    return `<div class="check-row"><span class="hint">effort · no effort levels are declared for this model, so none is offered — `
      + `the endpoint decides${sentVerbatim(row)}</span>${help('apiEffort')}${reset(row, id, 'effort', report)}</div>`;
  }
  const chosen = row.effort !== undefined && levels.includes(row.effort) ? row.effort : report.defaults.effort;
  const marked = ' (' + defaultWord(report) + ')';
  const option = (level: string): string =>
    `<option value="${escapeHtml(level)}"${level === chosen ? ' selected' : ''}>${escapeHtml(level)}${level === report.defaults.effort ? marked : ''}</option>`;

  return `<label for="effort-${id}">${help('apiEffort')}effort</label>${reset(row, id, 'effort', report)}
    <select id="effort-${id}" data-setting="effort" data-vendor="${id}">
      ${levels.map(option).join('\n      ')}
    </select>`;
}

function sentVerbatim(row: Row): string {
  return row.effort === undefined ? '.' : `; this row sends ‘${escapeHtml(row.effort)}’ as it is.`;
}

/** The whole-review limit: the row's own number, or empty with the default said in the box. */
function minutesControl(row: Row, id: string, report: ApiReport): string {
  return `<label for="minutes-${id}">${help('apiReviewMinutes')}max review time (minutes)</label>${reset(row, id, 'reviewMinutes', report)}
    <input type="number" id="minutes-${id}" min="1" max="${MAX_REVIEW_MINUTES}" step="1" data-setting="reviewMinutes" data-vendor="${id}"
           value="${row.reviewMinutes ?? ''}" placeholder="${report.defaults.reviewMinutes} — ${defaultWord(report)}">`;
}

/** A reset for a value the row set — nothing for one it did not, because there is nothing to put back. */
function reset(row: Row, id: string, key: ApiSettingKey, report: ApiReport): string {
  return row[key] === undefined
    ? ''
    : ` <button type="button" class="link" data-command="resetApiSetting" data-id="${id}:${key}">reset to ${defaultWord(report)}</button>`;
}

/** What the row RUNS with — the server's report, said as the server's, because the environment can outrank the row. */
function runsWith(report: ApiReport): string {
  const { effort, thinkingOn, reviewMinutes } = report.effective;
  const words = `coai-mcp runs it at effort ${effort.length > 0 ? effort : 'the endpoint’s own'}, thinking ${thinkingOn ? 'on' : 'off'}, `
    + `for at most ${reviewMinutes} minutes — ${moduleWords(report)}.`;

  return `<div class="hint">${escapeHtml(words)}</div>`;
}

function moduleWords(report: ApiReport): string {
  return report.measuredModel.length > 0
    ? `the ${report.module} module, calibrated on ${report.measuredModel}`
    : 'the generic module, nothing calibrated for this model';
}

/** The server's own sentences: why the row's settings were refused, and why its named module was set aside. */
function serverSaid(report: ApiReport): string {
  const refusal = report.refusal.length > 0 ? `\n    <div class="stale">${escapeHtml(report.refusal)}</div>` : '';
  const note = report.note.length > 0 ? `\n    <div class="hint">${escapeHtml(report.note)}</div>` : '';

  return refusal + note;
}
