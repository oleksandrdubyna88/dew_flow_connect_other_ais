import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  API_SETTINGS_SINCE,
  API_SETTING_KEYS,
  apiReportFrom,
  apiSettingsOnServer,
  resetTarget,
  withApiSetting,
  withoutApiSetting,
  type ApiReport,
} from '../apiSettings';
import { parseProviders } from '../providers';
import { QWEN_ANSWER, XAI_ANSWER } from './apiReportFixtures';
import { serverSettingsJson } from '../serverSettingsFile';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor, vendorsEnv, vendorsFrom } from '../vendors';

/**
 * Per-model settings on an `api` row (story S3.8 of `todo/PLAN_feature_review.md`, the extension half):
 * what the row stores, what crosses to the server, what an older server is spared, and what a write does
 * to a value equal to the calibrated default.
 *
 * <p>The reports are `apiReportFixtures.ts`: what this branch's `coai-mcp --providers` printed, each put
 * through the parser the panel uses before any test relies on it.</p>
 */

function report(answer: unknown): ApiReport {
  const parsed = apiReportFrom(answer);
  assert.ok(parsed !== undefined, 'the fixture is not a report the panel accepts, so every test below would be about nothing');

  return parsed;
}

const QWEN_ROW: Vendor = {
  id: 'qwen', runtime: 'api', model: 'qwen3.8-max', enabled: true, plan: true, code: true,
  baseUrl: 'https://dashscope.example/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0, dialect: 'dashscope',
};

function rowsOf(json: string): readonly Record<string, unknown>[] {
  return JSON.parse(json) as Record<string, unknown>[];
}

const wireRow = (vendors: readonly Vendor[], version: string, id = 'qwen'): Record<string, unknown> | undefined =>
  rowsOf(vendorsEnv(vendors, version)).find((row) => row['id'] === id);

// ---------------------------------------------------------------- the report

test('the providers answer carries each api row’s report, and a CLI row carries none', () => {
  const reported = parseProviders(JSON.stringify({
    providers: [
      { provider: 'qwen', auth: 'unavailable', note: '', api: QWEN_ANSWER },
      { provider: 'codex', auth: 'subscription', note: '' },
    ],
  }));

  assert.deepEqual(reported?.['qwen']?.api?.capabilities.effortLevels, ['low', 'medium', 'xhigh']);
  assert.equal(reported?.['qwen']?.api?.defaults.effort, 'medium');
  assert.equal(reported?.['codex']?.api, undefined);
});

test('a report is read defensively: a level that is not a word is dropped, and a body that is not a report is none', () => {
  const forged = report({ ...QWEN_ANSWER, capabilities: { ...QWEN_ANSWER.capabilities, effortLevels: ['low', 7, '<b>', ' high '] } });

  assert.deepEqual(forged.capabilities.effortLevels, ['low', 'high']);
  assert.equal(apiReportFrom(undefined), undefined);
  assert.equal(apiReportFrom('qwen'), undefined);
  assert.equal(apiReportFrom({ module: 'qwen' }), undefined, 'a report with no capabilities or defaults cannot draw a control');
});

// ---------------------------------------------------------------- the stored row

test('the three settings survive the round trip when said, lower-cased, and are absent when not', () => {
  const [said] = vendorsFrom([{ ...QWEN_ROW, effort: ' XHigh ', thinking: false, reviewMinutes: 35 }]);
  const [silent] = vendorsFrom([{ ...QWEN_ROW }]);

  assert.equal(said?.effort, 'xhigh');
  assert.equal(said?.thinking, false);
  assert.equal(said?.reviewMinutes, 35);
  for (const key of API_SETTING_KEYS) {
    assert.ok(!(key in (silent as object)), `${key} is written on a row that never said it — coai.vendors is JSON a person reads`);
  }
});

test('a stored value that is not a setting is not kept: a blank effort, a non-boolean switch, a minute count that is no count', () => {
  const [row] = vendorsFrom([{ ...QWEN_ROW, effort: '  ', thinking: 'no', reviewMinutes: -5 }]);
  const [fraction] = vendorsFrom([{ ...QWEN_ROW, reviewMinutes: 2.5 }]);

  for (const key of API_SETTING_KEYS) {
    assert.ok(!(key in (row as object)), `${key} survived as a value nobody could have meant`);
  }
  assert.ok(!('reviewMinutes' in (fraction as object)), 'a fraction of a minute is not a limit the server reads');
});

// ---------------------------------------------------------------- the wire

test('an api row carries its settings to the server, only the ones it said', () => {
  const set = wireRow([...DEFAULT_VENDORS, { ...QWEN_ROW, effort: 'xhigh', thinking: false, reviewMinutes: 35 }], API_SETTINGS_SINCE);
  const unset = wireRow([...DEFAULT_VENDORS, QWEN_ROW], API_SETTINGS_SINCE);

  assert.equal(set?.['effort'], 'xhigh');
  assert.equal(set?.['thinking'], false);
  assert.equal(set?.['reviewMinutes'], 35);
  for (const key of API_SETTING_KEYS) {
    assert.ok(!(key in (unset ?? {})), `${key} crossed for a row that set nothing, so the default would stop following calibration`);
  }
});

test('a CLI row never carries them, even when a hand-edited file put them there', () => {
  const codex = wireRow([{ ...DEFAULT_VENDORS[0]!, effort: 'high', thinking: false, reviewMinutes: 9 } as Vendor], API_SETTINGS_SINCE, 'codex');

  for (const key of API_SETTING_KEYS) {
    assert.ok(!(key in (codex ?? {})), `${key} reached the server on a codex row`);
  }
});

test('against a server known to be older the settings are held back, the row still crosses — and unknown is not old', () => {
  const row = { ...QWEN_ROW, effort: 'xhigh', thinking: false, reviewMinutes: 35 };
  const older = wireRow([...DEFAULT_VENDORS, row], '0.39.0');
  const unknown = wireRow([...DEFAULT_VENDORS, row], '');

  assert.ok(older !== undefined, 'the row itself must still reach an older server that knows the api runtime');
  for (const key of API_SETTING_KEYS) {
    assert.ok(!(key in older), `${key} was written for a server that predates it`);
  }
  assert.equal(unknown?.['effort'], 'xhigh');
  assert.equal(apiSettingsOnServer('0.39.0'), false);
  assert.equal(apiSettingsOnServer(API_SETTINGS_SINCE), true);
  assert.equal(apiSettingsOnServer(''), true);
});

test('the settings file the server reads carries them the same way', () => {
  const stored = vendorsFrom([...DEFAULT_VENDORS, { ...QWEN_ROW, effort: 'low' }]);
  const file = JSON.parse(serverSettingsJson(DEFAULTS, stored, 'test', API_SETTINGS_SINCE)) as { COAI_VENDORS?: string };

  assert.equal(rowsOf(file.COAI_VENDORS ?? '[]').find((row) => row['id'] === 'qwen')?.['effort'], 'low');
});

test('the settings ship in the release after 0.39.0, which was cut without the vendor modules', () => {
  assert.equal(API_SETTINGS_SINCE, '0.40.0');
});

// ---------------------------------------------------------------- a write, and the default

test('choosing a level other than the default stores it; choosing the default stores nothing', () => {
  const qwen = report(QWEN_ANSWER);

  assert.equal(withApiSetting(QWEN_ROW, 'effort', 'xhigh', qwen)?.effort, 'xhigh');
  const back = withApiSetting({ ...QWEN_ROW, effort: 'xhigh' }, 'effort', 'medium', qwen);
  assert.ok(back !== undefined && !('effort' in back), 'the default was stored, so it would stop following future calibrations');
});

test('an effort the module does not declare is refused, never stored', () => {
  assert.equal(withApiSetting(QWEN_ROW, 'effort', 'ultra', report(QWEN_ANSWER)), undefined);
});

test('the thinking switch stores only OFF, and only where the vendor has a switch', () => {
  const off = withApiSetting(QWEN_ROW, 'thinking', false, report(QWEN_ANSWER));
  const on = withApiSetting({ ...QWEN_ROW, thinking: false }, 'thinking', true, report(QWEN_ANSWER));

  assert.equal(off?.thinking, false);
  assert.ok(on !== undefined && !('thinking' in on), 'thinking on is the calibrated default and must not be stored');
  assert.equal(withApiSetting(QWEN_ROW, 'thinking', false, report(XAI_ANSWER)), undefined, 'xai has no switch to turn off');
});

test('the review limit stores a whole number of minutes other than the default; an empty box is the default', () => {
  const qwen = report(QWEN_ANSWER);

  assert.equal(withApiSetting(QWEN_ROW, 'reviewMinutes', 35, qwen)?.reviewMinutes, 35);
  for (const blank of [0, 20, '', Number.NaN]) {
    const row = withApiSetting({ ...QWEN_ROW, reviewMinutes: 35 }, 'reviewMinutes', blank, qwen);
    assert.ok(row !== undefined && !('reviewMinutes' in row), `${String(blank)} left a limit on the row`);
  }
  assert.equal(withApiSetting(QWEN_ROW, 'reviewMinutes', 2.5, qwen), undefined);
  assert.equal(withApiSetting(QWEN_ROW, 'reviewMinutes', -1, qwen), undefined);
  assert.equal(withApiSetting(QWEN_ROW, 'reviewMinutes', 100_000, qwen), undefined);
});

test('reset takes the row’s own value away, and only that one', () => {
  const row = { ...QWEN_ROW, effort: 'xhigh', thinking: false, reviewMinutes: 35 };
  const reset = withoutApiSetting(row, 'effort');

  assert.ok(!Object.keys(reset).includes('effort'));
  assert.equal(reset.thinking, false);
  assert.equal(reset.reviewMinutes, 35);
});

test('a reset button’s id names the row and the setting, and anything else names nothing', () => {
  assert.deepEqual(resetTarget('qwen-2:reviewMinutes'), { vendor: 'qwen-2', key: 'reviewMinutes' });
  assert.equal(resetTarget('qwen:model'), undefined);
  assert.equal(resetTarget('qwen'), undefined);
  assert.equal(resetTarget(undefined), undefined);
});
