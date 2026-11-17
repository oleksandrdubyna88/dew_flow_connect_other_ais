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
  assert.match(text, /unless this side has its own value, which wins over both/u, 'the English article lost the side-wins precedence clause');
});

/**
 * One meaning-bearing phrase per language, in that file's own words, for each of three clauses an English-only check
 * would leave unguarded: (a) the one road left to a person includes the case where no consultant can be had, (b) the
 * value this side holds wins over a workspace's and the user's, (c) not_reviewed is never a pass.
 */
const CLAUSES: Record<Exclude<(typeof HELP_LANGUAGES)[number], 'en'>, { noConsultant: string; sideWins: string; neverAPass: string }> = {
  de: { noConsultant: 'oder wenn kein Konsultant zu haben ist', sideWins: 'außer diese Seite hat einen eigenen Wert, der über beiden steht', neverAPass: 'nie als bestanden' },
  es: { noConsultant: 'cuando no hay ningún consultor disponible', sideWins: 'salvo que este lado tenga un valor propio, que gana a ambos', neverAPass: 'nunca como aprobada' },
  ru: { noConsultant: 'или когда ни одного консультанта получить нельзя', sideWins: 'если только у этой стороны нет собственного значения: оно сильнее обоих', neverAPass: 'никогда не как «пройдено»' },
  uk: { noConsultant: 'або коли жодного консультанта не вдалося залучити', sideWins: 'хіба що ця сторона має власне значення: воно сильніше за обидва', neverAPass: 'ніколи не як «пройдено»' },
};

test('every translation keeps the no-consultant road, the side-wins clause and never-a-pass', () => {
  for (const language of HELP_LANGUAGES.filter((one): one is Exclude<typeof one, 'en'> => one !== 'en')) {
    const text = joined(language);
    const clauses = CLAUSES[language];
    assert.ok(text.includes(clauses.noConsultant), `the ${language} article lost the no-consultant road: ${clauses.noConsultant}`);
    assert.ok(text.includes(clauses.sideWins), `the ${language} article lost the side-wins clause: ${clauses.sideWins}`);
    assert.ok(text.includes(clauses.neverAPass), `the ${language} article lost not_reviewed is never a pass: ${clauses.neverAPass}`);
  }
});

test('no language promises that nothing ever asks the person', () => {
  const promises = [/never asks you\b/iu, /nie(mals)? etwas fragt/iu, /nunca (le )?pregunta (nada|algo)/iu, /никогда ничего не спрашива/iu, /ніколи нічого не питає/iu];
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
  assert.ok(HELP.onExhausted.includes('or when no consultant can be had'), 'the tooltip does not name the no-consultant road to a person');
});
