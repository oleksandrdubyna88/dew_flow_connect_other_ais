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

// Where a "?" sits relative to a checkbox is BEHAVIOUR — a "?" inside the label flips the setting when
// clicked — so it is measured in a real browser (scripts/measure-help-clicks.mjs), not asserted over this
// text: a new behavioural assertion over page text is refused (.agents/PROJECT.md).

test('every consultant and cadence setting has a "?" that explains it', () => {
  const bare = LABELS.filter((label) => !label.includes('class="help"'))
    // Named by its `for`, or by the start of its markup — never by stripping tags, which CodeQL reads as a
    // sanitizer that misses `<script` (js/incomplete-multi-character-sanitization).
    .map((label) => /for="([^"]+)"/u.exec(label)?.[1] ?? label.slice(0, 80));

  assert.deepEqual(bare, [], `settings with no "?": ${bare.join(', ')}`);
});

test('the census covers every control it is about, so an empty list of labels cannot pass it', () => {
  for (const id of ['consultEnabled', 'consultVendor-claude', 'consultBaseUrl-claude', 'consultExecutablePath-claude',
    'consultTurns', 'consultCallsPerSession', 'consultIdleMinutes', 'consultPrompt',
    'cadenceEvery', 'cadenceRiskThreshold', 'cadenceRiskMax']) {
    assert.ok(LABELS.some((label) => label.includes(`for="${id}"`)), `${id} has no label in the census`);
  }
});
