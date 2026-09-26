import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cadenceBlock, DEFAULT_CADENCE } from '../cadenceSettings';
import { consultantBody } from '../consultantView';
import { DEFAULT_CONSULT } from '../consultSettings';

// The census the other way round from helpTooltips.test.ts: there, every tooltip must sit on a control;
// here, every control of the Consultant section and the consultation cadence must carry its "?"
// (research/PLAN_consult_limits_kinds_and_help.md, story 3). The operator's words: a "?" on every new setting.

const HTML = consultantBody(DEFAULT_CONSULT, {}) + cadenceBlock(DEFAULT_CADENCE);

/** Every label a person reads a setting by — the options of a segmented radio are one control, not three. */
const LABELS = [...HTML.matchAll(/<label[^>]*>[\s\S]*?<\/label>(?:<span class="help"[^>]*>\?<\/span>)?/gu)]
  .map((match) => match[0])
  .filter((label) => !label.includes('type="radio"'));

/** A label that wraps its own checkbox: a click anywhere inside it — a "?" included — toggles the switch. */
const wrapsACheckbox = (label: string): boolean => label.includes('type="checkbox"');
const insideTheLabel = (label: string): string => label.slice(0, label.indexOf('</label>'));

test('every consultant and cadence setting has a "?" that explains it', () => {
  const bare = LABELS.filter((label) => !label.includes('class="help"'))
    // Named by its `for`, or by the start of its markup — never by stripping tags, which CodeQL reads as a
    // sanitizer that misses `<script` (js/incomplete-multi-character-sanitization).
    .map((label) => /for="([^"]+)"/u.exec(label)?.[1] ?? label.slice(0, 80));

  assert.deepEqual(bare, [], `settings with no "?": ${bare.join(', ')}`);
});

test('a checkbox’s "?" sits outside its label, so reading the help does not flip the switch', () => {
  const inside = LABELS.filter((label) => wrapsACheckbox(label) && insideTheLabel(label).includes('class="help"'))
    .map((label) => /for="([^"]+)"/u.exec(label)?.[1] ?? label);

  assert.deepEqual(inside, [], `a click on these "?" would toggle the setting: ${inside.join(', ')}`);
});

test('the census covers every control it is about, so an empty list of labels cannot pass it', () => {
  for (const id of ['consultEnabled', 'consultVendor-claude', 'consultBaseUrl-claude', 'consultExecutablePath-claude',
    'consultTurns', 'consultCallsPerSession', 'consultIdleMinutes', 'consultPrompt',
    'cadenceEvery', 'cadenceRiskThreshold', 'cadenceRiskMax']) {
    assert.ok(LABELS.some((label) => label.includes(`for="${id}"`)), `${id} has no label in the census`);
  }
});
