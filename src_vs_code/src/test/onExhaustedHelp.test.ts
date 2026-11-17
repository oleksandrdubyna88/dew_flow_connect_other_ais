import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP } from '../help';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';

/**
 * The help for When the rounds run out under policy Auto, in five languages (E2.S3 of
 * todo/PLAN_auto_never_asks_a_person_E2.md, section 9.2). The guarantee is requirement 5's, exactly: the gate raises no
 * new hold, never waits and never writes a card; the AI's own question after its consultants can still reach a person.
 * No text may promise that nothing ever asks.
 */

const GATE = HELP_ARTICLES.find((one) => one.id === 'the-gate');

function joined(language: (typeof HELP_LANGUAGES)[number]): string {
  assert.ok(GATE !== undefined, 'the-gate article has been renamed, and this test is now asserting nothing');
  const { body, fallback } = bodyFor(GATE, language);
  assert.equal(fallback, false, `the ${language} the-gate article is missing`);
  return Object.values(body).join('\n');
}

test('every language’s the-gate article names Auto, not_reviewed, the workspace file and the first server that knows Auto', () => {
  for (const language of HELP_LANGUAGES) {
    const text = joined(language);
    for (const needle of ['*Auto*', 'not_reviewed', '.vscode/settings.json', '0.66.0', 'COAI_ON_EXHAUSTED', 'coai.onExhaustedResetToAuto']) {
      assert.ok(text.includes(needle), `the ${language} the-gate article does not contain ${needle}`);
    }
  }
});

test('every language’s the-gate article is not the English text in disguise', () => {
  const english = joined('en');
  for (const language of HELP_LANGUAGES.filter((one) => one !== 'en')) {
    const text = joined(language);
    assert.notEqual(text, english, `the ${language} article is the English one`);
    assert.doesNotMatch(text, /raises no new hold and never waits/u, `the ${language} article still carries the English guarantee`);
  }
});

test('the English article states the guarantee in requirement 5’s words', () => {
  const text = joined('en');
  assert.match(text, /raises no new hold and never waits for you, and it never writes a card/u);
  assert.match(text, /its consultants could not settle, or when no consultant can be had/u);
  assert.match(text, /predates Auto/u);
  assert.match(text, /reset to Auto once/u);
});

test('no language promises that nothing ever asks the person', () => {
  const promises = [/never asks you anything/iu, /nie(mals)? etwas fragt/iu, /nunca (le )?pregunta (nada|algo)/iu, /никогда ничего не спрашива/iu, /ніколи нічого не питає/iu];
  for (const language of HELP_LANGUAGES) {
    const text = `${joined(language)}\n${HELP.onExhausted}`;
    for (const promise of promises) assert.doesNotMatch(text, promise, `the ${language} help promises that nothing asks the person`);
  }
});

test('the When the rounds run out tooltip puts Auto first and says not_reviewed is never a pass', () => {
  assert.match(HELP.onExhausted, /^What happens when the rounds run out and findings still gate, or nobody answered\. Auto \(the default\)/u);
  assert.ok(HELP.onExhausted.includes('not_reviewed'), 'the tooltip does not name not_reviewed');
  assert.ok(HELP.onExhausted.includes('never a pass'), 'the tooltip does not say not_reviewed is never a pass');
  assert.ok(HELP.onExhausted.indexOf('Auto') < HELP.onExhausted.indexOf('Ask a human'), 'Auto is not first');
  assert.ok(HELP.onExhausted.includes('could not settle'), 'the tooltip does not say the AI may still ask');
});
