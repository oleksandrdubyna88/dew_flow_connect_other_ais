import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_SECURITY, securityLaneFrom, securityLaneSave, type SecurityLane } from '../securityLane';
import { DEFAULT_VENDORS } from '../vendors';
import { type Page, panelState, runPanel } from './panelPageHarness';
import { paneIn } from './panelPages';

/**
 * A `coai.securityLane` value nobody in this panel wrote — a cloned repository's `.vscode/settings.json`
 * can carry one, because the setting has no scope — RUN through the Settings page the panel really
 * renders (`.agents/PROJECT.md`: a webview page is tested by running it).
 */

const SUPPORTED = { kind: 'known', version: '0.41.0', remembered: false, updateOffered: false } as const;
const VENDOR = DEFAULT_VENDORS.find((v) => v.enabled)!.id;

/** Markup that, unescaped inside an attribute, closes it and draws a lane switch of its own. */
const FORGED = '1"><input type="checkbox" data-setting="securityLane" data-security-field="enabled" checked><b a="';

/** The shipped reviewers ticked Security lane on Models — what the new page offers a pair (E4.2; E5.1 step 3). */
const TICKED = DEFAULT_VENDORS.map((v) => ({ ...v, uses: ['security' as const] }));

function pageWith(securityLane: SecurityLane): Page {
  return runPanel(panelState('securityLane', { vendors: TICKED, settings: { ...DEFAULTS, securityLane }, server: SUPPORTED }));
}

/** The Security lane tab's own pane, as the Settings page drew it — the new page's Security lane (E5.1 step 3). */
function pane(page: Page): string {
  return paneIn(page.html, 'security');
}

const switches = (page: Page) => page.controls.filter((c) => c.dataset['securityField'] === 'enabled');

test('a contextTokens value from a cloned repository cannot draw a control into the Security lane tab', () => {
  const stored = { ...DEFAULT_SECURITY, runs: [{ vendor: VENDOR, prompt: 'redteam-authz', contextTokens: FORGED }] };
  const page = pageWith(securityLaneFrom(stored));

  assert.ok(switches(page).length <= 1, 'the stored value drew a second lane switch onto the page');
  assert.match(pane(page), /coai\.securityLane in your settings JSON is malformed \(runs\[0\]: contextTokens/,
    'the tab does not say which part of the setting is malformed');
});

test('whatever reaches the token budget box stays inside its value', () => {
  // Built by hand, past the reader, ON PURPOSE: this is the sink's own guarantee, for a lane that
  // reached the view by some road other than `securityLaneFrom`. The cast is the point of the test.
  const lane: SecurityLane = {
    ...DEFAULT_SECURITY,
    runs: [{ vendor: VENDOR, prompt: 'redteam-authz', contextTokens: FORGED as unknown as number }],
  };
  const page = pageWith(lane);

  assert.equal(switches(page).length, 1, 'the token budget box let its value draw a lane switch');
  const budget = page.controls.find((c) => c.dataset['securityField'] === 'run:0:contextTokens');
  assert.ok(budget, 'the pair has no token budget box at all');
});

test('a stages value that is not a list leaves the Settings page rendering, with the lane shown as malformed', () => {
  const stored = { ...DEFAULT_SECURITY, runs: [{ vendor: VENDOR, prompt: 'redteam-authz', stages: 1 }] };
  const page = pageWith(securityLaneFrom(stored));

  assert.match(pane(page), /malformed \(runs\[0\]: stages/);
  assert.equal(page.controls.filter((c) => c.dataset['setting'] === 'securityLane').length, 0,
    'a malformed lane still offered controls that would write over it');
});

test('a well-formed pair keeps its source, budget and stages, and draws its controls', () => {
  // The positive control for the refusals around it: the same reader, a run it must accept.
  const stored = { ...DEFAULT_SECURITY, enabled: true,
    runs: [{ vendor: VENDOR, prompt: 'redteam-authz', context: 'slice', contextTokens: 4096, stages: ['code'], futureSource: 'v2' }] };
  const lane = securityLaneFrom(stored);
  assert.equal('invalidConfiguration' in lane, false, 'a well-formed pair was read as malformed');
  const page = pageWith(lane);

  assert.equal(page.controls.find((c) => c.dataset['securityField'] === 'run:0:contextTokens')?.value, '4096');
  assert.equal(page.controls.find((c) => c.dataset['securityField'] === 'run:0:code')?.checked, true);
  assert.equal(page.controls.find((c) => c.dataset['securityField'] === 'run:0:feature')?.checked, false);
  assert.equal(page.controls.find((c) => c.dataset['securityField'] === 'run:0:context')?.value, 'slice');
});

const MALFORMED_RUNS: readonly [string, Record<string, unknown>][] = [
  ['context', { context: 'everything' }],
  ['contextTokens', { contextTokens: 12 }],
  ['contextTokens', { contextTokens: 1500.5 }],
  ['contextTokens', { contextTokens: '4096' }],
  ['contextTokens', { contextTokens: 300000 }],
  ['stages', { stages: 'code' }],
  ['stages', { stages: [1] }],
  ['stages', { stages: ['plan'] }],
];

for (const [field, run] of MALFORMED_RUNS) {
  test(`a pair whose ${field} is ${JSON.stringify(run[field])} turns the lane off and says it is malformed`, () => {
    const stored = { ...DEFAULT_SECURITY, enabled: true, runs: [{ vendor: VENDOR, prompt: 'redteam-authz', ...run }] };
    const lane = securityLaneFrom(stored);

    assert.equal(lane.enabled, false);
    assert.ok('invalidConfiguration' in lane, `${JSON.stringify(run)} was accepted as a pair`);
  });
}

test('the malformed-setting notice sends the person to coai.securityLane, the setting that exists', () => {
  const page = pageWith(securityLaneFrom({ ...DEFAULT_SECURITY, enabled: 'yes' }));
  const text = pane(page);

  assert.match(text, /Security lane is off: coai\.securityLane in your settings JSON is malformed \(enabled must be true or false\)\. Correct it there\./);
  assert.ok(!text.includes('invalidConfiguration'), 'the notice names a field that exists only inside the panel');
});

for (const language of HELP_LANGUAGES) {
  test(`the ${language} security lane help does not send anybody to invalidConfiguration, which no settings file holds`, () => {
    const article = HELP_ARTICLES.find((one) => one.id === 'security-lane');
    assert.ok(article, 'there is no security lane article');
    const text = Object.values(bodyFor(article, language).body).join('\n');

    assert.ok(text.includes('coai.securityLane'), `the ${language} article does not name the setting to correct`);
    assert.ok(!text.includes('invalidConfiguration'), `the ${language} article names invalidConfiguration`);
  });
}

test('a write against a malformed setting leaves what the person wrote in settings JSON alone', () => {
  const stored = { ...DEFAULT_SECURITY, runs: [{ vendor: VENDOR, prompt: 'redteam-authz', stages: 1 }] };

  for (const [field, value] of [['enabled', true], ['addRun', true], ['threshold', 3]] as const) {
    assert.equal(securityLaneSave(stored, field, value, DEFAULT_VENDORS), undefined,
      `a ${field} write would replace the malformed setting with the panel's stand-in`);
  }
  // The positive half: the same write against a well-formed setting is saved.
  assert.equal(securityLaneSave(DEFAULT_SECURITY, 'enabled', true, DEFAULT_VENDORS)?.enabled, true);
});

test('a malformed lane still reaches the server switched off, so it can say the setting was refused', () => {
  const securityLane = securityLaneFrom({ ...DEFAULT_SECURITY, enabled: true, runs: 'all' });
  const wire = JSON.parse(envBlock({ ...DEFAULTS, securityLane }, DEFAULT_VENDORS, '0.41.0')['COAI_SECURITY_LANE']!);

  assert.equal(wire.enabled, false);
  assert.deepEqual(wire.runs, []);
  assert.equal(wire.invalidConfiguration.runs, 'all');
});
