import assert from 'node:assert/strict';
import { test } from 'node:test';

import { API_SETTINGS_SINCE, apiReportFrom, withoutApiSetting, type ApiReport } from '../apiSettings';
import { escapeHtml } from '../escapeHtml';
import { HELP } from '../help';
import { NO_NOTES, type ProviderHealth } from '../providers';
import { settingMessageFrom, settingWrite } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor } from '../vendors';
import { GENERIC_ANSWER, QWEN_ANSWER, XAI_ANSWER } from './apiReportFixtures';
import { click, lastWrite, panelState, runPanel, type Control, type Page } from './panelPageHarness';

/**
 * The per-model settings on an `api` card, RUN (story S3.8 of `todo/PLAN_feature_review.md`, the extension
 * half): the panel is rendered from a providers answer this branch's `coai-mcp` really printed, its own
 * script is run over its own markup, and the assertions are about what a person is offered and what the page
 * posts — never about the page's source text.
 */

const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });

function row(id: string, model: string, dialect: string, extra: Partial<Vendor> = {}): Vendor {
  return {
    id, runtime: 'api', model, enabled: true, plan: true, code: true, baseUrl: 'https://api.example/v1',
    executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0, dialect, ...extra,
  };
}

const QWEN = row('qwen', 'qwen3.8-max', 'dashscope');
const GROK = row('grok', 'grok-4.7', 'xai');
const GENERIC = row('other', 'some-model', 'openai');

function reportOf(answer: unknown): ApiReport {
  const parsed = apiReportFrom(answer);
  assert.ok(parsed !== undefined, 'the fixture is not a report the panel accepts');

  return parsed;
}

function health(provider: string, answer?: unknown): ProviderHealth {
  return { provider, auth: 'key', note: '', ...(answer === undefined ? {} : { api: reportOf(answer) }) };
}

/** The panel with these api rows and what the server reported for each — as the probe hands it over. */
function page(vendors: readonly Vendor[], reported: readonly ProviderHealth[], version = API_SETTINGS_SINCE, answered = true): Page {
  return runPanel(panelState('reviewers', {
    vendors: [...DEFAULT_VENDORS, ...vendors],
    server: KNOWN(version),
    providers: {
      reported: Object.fromEntries(reported.map((one) => [one.provider, one])),
      asked: true,
      answered,
      notes: NO_NOTES,
    },
  }));
}

function control(on: Page, setting: string, vendor: string): Control | undefined {
  return on.controls.find((one) => one.dataset['setting'] === setting && one.dataset['vendor'] === vendor);
}

// ---------------------------------------------------------------- the effort dropdown

test('the effort dropdown offers exactly the levels the module declared, in its order — and nothing else', () => {
  for (const [vendor, answer] of [[QWEN, QWEN_ANSWER], [GROK, XAI_ANSWER]] as const) {
    const on = page([vendor], [health(vendor.id, answer)]);
    const effort = control(on, 'effort', vendor.id);

    assert.ok(effort !== undefined, `the ${vendor.id} card has no effort dropdown`);
    assert.deepEqual(effort.options.map((one) => one.value), reportOf(answer).capabilities.effortLevels);
  }
});

test('the calibrated default is marked, and is what the dropdown shows while the row sets nothing', () => {
  const effort = control(page([QWEN], [health('qwen', QWEN_ANSWER)]), 'effort', 'qwen');
  const marked = effort?.options.filter((one) => one.text.includes('calibrated default')) ?? [];

  assert.deepEqual(marked.map((one) => one.value), [QWEN_ANSWER.defaults.effort]);
  assert.equal(effort?.value, QWEN_ANSWER.defaults.effort);
});

test('a row’s own effort is what the dropdown shows', () => {
  const effort = control(page([{ ...QWEN, effort: 'xhigh' }], [health('qwen', QWEN_ANSWER)]), 'effort', 'qwen');

  assert.equal(effort?.value, 'xhigh');
});

test('choosing a level writes the row’s effort', () => {
  const on = page([QWEN], [health('qwen', QWEN_ANSWER)]);
  const effort = control(on, 'effort', 'qwen');
  assert.ok(effort !== undefined);

  effort.value = 'low';
  effort.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(on))), { kind: 'vendor', key: 'effort', vendor: 'qwen', value: 'low', control: 'select' });
});

test('a module that declares no levels gets no dropdown, and the card says why', () => {
  const on = page([GENERIC], [health('other', GENERIC_ANSWER)]);

  assert.equal(control(on, 'effort', 'other'), undefined, 'a dropdown with a list typed on this side is what the operator ruled out');
  assert.match(on.html, /no effort levels are declared for this model/u);
});

// ---------------------------------------------------------------- the thinking switch

test('the thinking switch is there where the vendor has one, on unless the row switched it off', () => {
  const unset = control(page([QWEN], [health('qwen', QWEN_ANSWER)]), 'thinking', 'qwen');
  const off = control(page([{ ...QWEN, thinking: false }], [health('qwen', QWEN_ANSWER)]), 'thinking', 'qwen');

  assert.equal(unset?.type, 'checkbox');
  assert.equal(unset?.checked, true);
  assert.equal(off?.checked, false);
});

test('switching thinking off writes the row’s switch', () => {
  const on = page([QWEN], [health('qwen', QWEN_ANSWER)]);
  const box = control(on, 'thinking', 'qwen');
  assert.ok(box !== undefined);

  box.checked = false;
  box.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(on))), { kind: 'vendor', key: 'thinking', vendor: 'qwen', value: false });
});

test('a vendor without a switch has no toggle, and the card says thinking cannot be switched off', () => {
  const on = page([GROK], [health('grok', XAI_ANSWER)]);

  assert.equal(control(on, 'thinking', 'grok'), undefined);
  assert.match(on.html, /thinking cannot be switched off for this model/u);
});

// ---------------------------------------------------------------- the time limit

test('the review limit shows the calibrated default while unset, and the row’s own number when set', () => {
  const unset = control(page([QWEN], [health('qwen', QWEN_ANSWER)]), 'reviewMinutes', 'qwen');
  const set = control(page([{ ...QWEN, reviewMinutes: 35 }], [health('qwen', QWEN_ANSWER)]), 'reviewMinutes', 'qwen');

  assert.equal(unset?.type, 'number');
  assert.equal(unset?.value, '');
  assert.match(unset?.placeholder ?? '', new RegExp(`^${QWEN_ANSWER.defaults.reviewMinutes}\\b`, 'u'), 'the empty box does not say what it defaults to');
  assert.equal(set?.value, '35');
});

test('typing a limit writes the row’s minutes, as a number', () => {
  const panel = page([QWEN], [health('qwen', QWEN_ANSWER)]);
  const box = control(panel, 'reviewMinutes', 'qwen');
  assert.ok(box !== undefined);

  box.value = '45';
  box.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(panel))), { kind: 'vendor', key: 'reviewMinutes', vendor: 'qwen', value: 45 });
});

// ---------------------------------------------------------------- reset

test('each value the row set has a reset, and a click asks the host to clear exactly that one', () => {
  const panel = page([{ ...QWEN, effort: 'xhigh', reviewMinutes: 35 }], [health('qwen', QWEN_ANSWER)]);
  const resets = panel.commands.filter((one) => one.dataset['command'] === 'resetApiSetting').map((one) => one.dataset['id']);

  assert.deepEqual(resets, ['qwen:effort', 'qwen:reviewMinutes'], 'a reset is offered for a value the row never set, or missing for one it did');
  click(panel, 'resetApiSetting', 'qwen:effort');
  assert.deepEqual(panel.posted.at(-1), { type: 'command', command: 'resetApiSetting', id: 'qwen:effort' });
});

test('after a reset the row holds nothing and the dropdown shows the calibrated default again', () => {
  const cleared = withoutApiSetting({ ...QWEN, effort: 'xhigh' }, 'effort');
  const panel = page([cleared], [health('qwen', QWEN_ANSWER)]);

  assert.equal(control(panel, 'effort', 'qwen')?.value, QWEN_ANSWER.defaults.effort);
  assert.ok(!panel.commands.some((one) => one.dataset['id'] === 'qwen:effort'), 'nothing is left to reset, and a reset is still offered');
});

// ---------------------------------------------------------------- what the server said

test('the refusal coai-mcp gave for the row’s own settings is shown on its card', () => {
  const panel = page([{ ...GROK, thinking: false }], [health('grok', XAI_ANSWER)]);

  assert.ok(panel.html.includes(escapeHtml(XAI_ANSWER.refusal)), 'the server refused this row and the card does not say so');
});

test('the set-aside note is shown on the card', () => {
  const note = "the 'glm' module was measured on glm-5.3, not glm-5.2 — this row runs on the generic module";
  const panel = page([row('glm', 'glm-5.2', 'glm')], [health('glm', { ...GENERIC_ANSWER, note })]);

  assert.ok(panel.html.includes('this row runs on the generic module'), 'the downgrade the server named is not said on the card');
});

test('what the row RUNS with is the server’s report, said as such', () => {
  const panel = page([QWEN], [health('qwen', { ...QWEN_ANSWER, effective: { ...QWEN_ANSWER.effective, effort: 'low', reviewMinutes: 7 } })]);

  assert.match(panel.html, /coai-mcp runs it at effort low[^<]*7 minutes/u);
});

// ---------------------------------------------------------------- an older or silent server

test('a server that reports no settings for the row hides the controls and names the release that has them', () => {
  const panel = page([QWEN], [health('qwen')], '0.39.0');

  for (const setting of ['effort', 'thinking', 'reviewMinutes']) {
    assert.equal(control(panel, setting, 'qwen'), undefined, `${setting} is drawn with nothing to draw it from`);
  }
  assert.ok(panel.html.includes(API_SETTINGS_SINCE), 'the card does not say which coai-mcp has the settings');
  assert.ok(panel.html.includes('0.39.0'), 'the card does not name the installed server');
});

test('a server that has not answered for the row hides the controls and says so', () => {
  const panel = page([QWEN], [], API_SETTINGS_SINCE, false);

  assert.equal(control(panel, 'effort', 'qwen'), undefined);
  assert.match(panel.html, /once coai-mcp reports what this model accepts/u);
});

test('a CLI card has none of it', () => {
  const panel = page([], [health('codex')]);

  assert.equal(control(panel, 'effort', 'codex'), undefined);
  assert.ok(!panel.commands.some((one) => one.dataset['command'] === 'resetApiSetting'));
});

// ---------------------------------------------------------------- the help

test('every control carries a "?" of its own', () => {
  const panel = page([QWEN], [health('qwen', QWEN_ANSWER)]);

  for (const key of ['apiThinking', 'apiEffort', 'apiReviewMinutes'] as const) {
    assert.ok(panel.html.includes(`title="${escapeHtml(HELP[key])}"`), `the ${key} control has no help`);
  }
});
