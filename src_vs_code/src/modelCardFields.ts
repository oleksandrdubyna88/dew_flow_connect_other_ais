import { CATALOG_USES, type CatalogUse } from './catalogFields';
import { MAX_PROMPT_BYTES } from './catalogRules';
import { escapeHtml } from './escapeHtml';
import { help } from './panelControls';
import { CHAT, CONSULTING, EFFORT, type EffortRow, THINKING } from './featureAvailability.generated';
import type { Runtime } from './models';
import type { Vendor } from './vendors';

/**
 * The fields of a Models card that the old page's reviewer card never had (todo/PLAN_one_model_catalog.md E3.2): what else a row is
 * used for, its effort and thinking as its RUNTIME takes them (D4, D12), its own system prompt and its own time limit.
 * Each control writes the row through the panel's one vendor write (`data-setting` + `data-vendor`), where
 * `catalogWriteRules.ts` refuses what may not be stored.
 */

/** What each catalog use is called on a card. */
export const USE_LABELS: Readonly<Record<CatalogUse, string>> = {
  security: 'security lane',
  consultant: 'consultant',
  qconsult: 'question consultant',
  chat: 'chat',
  bugz: 'Bugz ranking',
};

/** Why a use is not offered on a row of this runtime — '' where it is (D4: one file decides for both halves). */
export function useRefusal(use: CatalogUse, runtime: Runtime): string {
  return (USE_RULES[use] ?? (() => ''))(runtime);
}

const USE_RULES: Partial<Readonly<Record<CatalogUse, (runtime: Runtime) => string>>> = {
  consultant: (runtime) => (CONSULTING.includes(runtime) ? '' : `a consultation runs on ${CONSULTING.join(', ')}`),
  qconsult: (runtime) => (CONSULTING.includes(runtime) ? '' : `a consultation runs on ${CONSULTING.join(', ')}`),
  chat: (runtime) => (CHAT.includes(runtime) ? '' : `a chat opens a CLI here: ${CHAT.join(', ')}`),
  bugz: (runtime) => (runtime === 'local' ? '' : 'Bugz ranks with a model on this machine’s GPU'),
};

/** The uses ticks: one per feature beyond the review stages, each disabled with its reason where it is not offered. */
export function usesBoxes(vendor: Vendor, id: string): string {
  const held = new Set(vendor.uses ?? []);
  const boxes = CATALOG_USES.map((use) => useBox(id, use, useRefusal(use, vendor.runtime), held.has(use)));

  return `<span class="boxes">${boxes.join('')}</span>${notOffered(vendor.runtime)}`;
}

/** One use tick: a command, not a setting — a use is one entry of the row's `uses` list, toggled by the host. */
function useBox(id: string, use: CatalogUse, why: string, held: boolean): string {
  return why.length > 0
    ? `<label class="feat off" title="${escapeHtml(why)}"><input type="checkbox" disabled> ${USE_LABELS[use]}</label>`
    : `<label class="feat"><input type="checkbox" data-command="toggleUse" data-id="${id}|${use}"${held ? ' checked' : ''}> ${USE_LABELS[use]}</label>`;
}

/** One line for every use a row of this runtime cannot take, grouped by reason — not a tooltip per box alone. */
function notOffered(runtime: Runtime): string {
  const lines = [...reasonsOf(runtime)].map(([why, uses]) => `<li>Not offered: ${escapeHtml(uses.join(', '))} — ${escapeHtml(why)}.</li>`);

  return lines.length === 0 ? '' : `<ul class="notes-list">${lines.join('')}</ul>`;
}

/** The uses a runtime cannot take, by the reason it cannot — two uses refused for one reason share a line. */
function reasonsOf(runtime: Runtime): ReadonlyMap<string, readonly string[]> {
  const refused = CATALOG_USES.map((use) => [useRefusal(use, runtime), USE_LABELS[use]] as const).filter(([why]) => why.length > 0);

  return new Map([...new Set(refused.map(([why]) => why))].map((why) => [why, refused.filter(([one]) => one === why).map(([, label]) => label)]));
}

/** The effort control of a row that is not an `api` row (an api row's comes from its probe report, `apiSettingsView`). */
/** @param mark the control's "new" mark, or '' (`catalogShell.newTag`) */
export function effortField(vendor: Vendor, id: string, probed: readonly string[], mark = ''): string {
  const row = EFFORT.find((one) => one.runtime === vendor.runtime);
  const draw = row === undefined ? NO_EFFORT : EFFORT_FIELDS[vendor.runtime === 'remote' ? 'remote' : row.source];

  return `<div class="field"><label for="effort-${id}">Effort${mark}</label>${draw(vendor, id, row, probed)}</div>`;
}

type EffortDraw = (vendor: Vendor, id: string, row: EffortRow | undefined, probed: readonly string[]) => string;

const NO_EFFORT: EffortDraw = () => '<div class="hint">This runtime takes no effort.</div>';

/** The levels a Team server can apply — the runtimes that LIST theirs; the server drops what its runtime does not take. */
const LISTED_LEVELS: readonly string[] = [...new Set(EFFORT.filter((row) => row.source === 'list').flatMap((row) => row.levels))];

const EFFORT_FIELDS: Readonly<Record<EffortRow['source'] | 'remote', EffortDraw>> = {
  list: (vendor, id, row) => effortSelect(vendor, id, row?.levels ?? [], 'Default — the vendor decides'),
  probe: (vendor, id, _row, probed) => (probed.length > 0
    ? effortSelect(vendor, id, probed, 'Default — the engine decides')
    : '<div class="hint">Its levels come from probing the engine — ⟳ look again, then choose one.</div>'),
  unmeasured: (vendor, _id, row) => `<div class="hint">${escapeHtml(stored(vendor))}${escapeHtml(row?.note ?? '')}</div>`,
  none: (_vendor, _id, row) => `<div class="hint">${escapeHtml(row?.note ?? '')}</div>`,
  remote: (vendor, id) => effortSelect(vendor, id, LISTED_LEVELS, 'Default — the server decides')
    + '<div class="hint">The Team server applies it where its runtime takes the level, and says on the reviewer when it does not.</div>',
};

function stored(vendor: Vendor): string {
  return (vendor.effort ?? '').length > 0 ? `'${vendor.effort}' is kept and not applied. ` : '';
}

function effortSelect(vendor: Vendor, id: string, levels: readonly string[], fallback: string): string {
  const current = vendor.effort ?? '';
  const options = [['', fallback], ...levels.map((level) => [level, level])]
    .map(([value, label]) => `<option value="${escapeHtml(value!)}"${value === current ? ' selected' : ''}>${escapeHtml(label!)}</option>`);

  return `<select id="effort-${id}" data-setting="effort" data-vendor="${id}">${options.join('')}</select>`;
}

/**
 * Thinking on a row that is not an `api` row (D12): no runtime but `api` has a switch today, so the card says why in one
 * line — the api card draws the switch itself where its report says the model has one (`apiSettingsView`).
 */
export function thinkingLine(vendor: Vendor): string {
  const row = THINKING.find((one) => one.runtime === vendor.runtime);

  return row === undefined || row.source === 'probe' ? '' : `<div class="field hint">Thinking: no switch — ${escapeHtml(row.note)}</div>`;
}

const ENCODER = new TextEncoder();

/** The row's own system prompt (D1): empty sends the feature's own prompt unchanged; at most 8 KiB, counted in bytes. */
export function systemPromptField(vendor: Vendor, id: string, mark = ''): string {
  const text = vendor.systemPrompt ?? '';

  return `<div class="field wide"><label for="sp-${id}">System prompt${mark}</label>`
    + `<textarea id="sp-${id}" rows="3" data-setting="systemPrompt" data-vendor="${id}"`
    + ` placeholder="Empty: the feature’s own prompt is sent unchanged">${escapeHtml(text)}</textarea>`
    + `<div class="hint"><span data-bytes-for="sp-${id}">${ENCODER.encode(text).length}</span> of ${MAX_PROMPT_BYTES} bytes</div></div>`;
}

/** The three states of a row's fast mode (research/PLAN_fast_mode.md), Off — the default — first. */
const FAST_STATES: readonly (readonly [string, string])[] = [['', 'Off — the standard tier'], ['on', 'On — the fast tier'], ['cli', 'As the CLI is set']];

/** A row's fast mode — drawn by the Settings page's card only, and only for a row that has a tier. */
export function fastField(vendor: Vendor, id: string, mark = ''): string {
  const chosen = vendor.fast ?? '';
  const options = FAST_STATES.map(([value, label]) => `<option value="${value}"${value === chosen ? ' selected' : ''}>${escapeHtml(label)}</option>`).join('');

  return `<div class="field"><label for="fast-${id}">Fast mode${mark}${help('fastMode')}</label>`
    + `<select id="fast-${id}" data-setting="fast" data-vendor="${id}">${options}</select></div>`;
}

/** An api row's stream switch (research/PLAN_api_streaming.md) — drawn by the Settings page's card only. */
export function streamField(vendor: Vendor, id: string, mark = ''): string {
  return `<div class="check-row"><label class="check"><input type="checkbox" data-setting="stream" data-vendor="${id}"${vendor.stream === true ? ' checked' : ''}>`
    + ` stream the answer${mark}</label>${help('apiStream')}</div>`;
}

/** The row's own time limit for one answer — empty follows Limits › Reviewer timeout (D1; never a stand-in number). */
export function timeoutField(vendor: Vendor, id: string): string {
  return `<div class="field"><label for="timeout-${id}">Give up on one answer after, minutes</label>`
    + `<input type="number" id="timeout-${id}" min="1" max="1440" data-setting="timeoutMinutes" data-vendor="${id}"`
    + ` value="${vendor.timeoutMinutes ?? ''}" placeholder="the round’s (Limits)"></div>`;
}
