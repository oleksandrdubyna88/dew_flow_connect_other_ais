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
  assert.match(text, /declares a production risk, with a reason, reaches you at once in the Planning or Released phase, where no consultant is asked first/u, 'the English article lost the production-risk exception to the only-road sentence');
  assert.match(text, /an unanswered FIRST round is retried, and only an unanswered round at its cap proceeds as `not_reviewed`/u, 'the English article lost the feature-review retry');
  assert.match(text, /nobody has answered is released — when the gate next loads that session/u, 'the English article lost when an older build\u2019s hold is released');
  assert.match(text, /not at the moment you change the setting, and a release that fails is tried again by the next call/u);
  assert.match(text, /a hold whose notice you already answered, for example with \*discuss\*, stays held and yours to answer/u);
  assert.match(text, /reset to Auto once/u);
  assert.match(text, /unless this side has its own value, which wins over both/u, 'the English article lost the side-wins precedence clause');
});

/**
 * One meaning-bearing phrase per language, in that file's own words, for each of three clauses an English-only check
 * would leave unguarded: (a) the one road left to a person includes the case where no consultant can be had, (b) the
 * value this side holds wins over a workspace's and the user's, (c) not_reviewed is never a pass; and three that say where the
 * release promise stops: (d) an unanswered FIRST feature round is retried, (e) a hold the person already answered stays theirs,
 * (f) the release comes with the next load of the session and not with the setting; and one more that keeps the road-to-a-person
 * sentence from being wider than the server: (g) a question declared a production risk reaches the person at once in the Planning
 * or Released phase, without a consultant first.
 */
interface Clauses {
  noConsultant: string;
  sideWins: string;
  neverAPass: string;
  featureRetry: string;
  answeredStays: string;
  notAtTheSetting: string;
  productionRisk: string;
}

const CLAUSES: Record<Exclude<(typeof HELP_LANGUAGES)[number], 'en'>, Clauses> = {
  de: { noConsultant: 'oder wenn kein Konsultant zu haben ist', sideWins: 'außer diese Seite hat einen eigenen Wert, der über beiden steht', neverAPass: 'nie als bestanden', featureRetry: 'wird eine unbeantwortete ERSTE Runde wiederholt', answeredStays: 'bleibt gesperrt und gehört weiter Ihnen', notAtTheSetting: 'nicht in dem Moment, in dem Sie die Einstellung ändern', productionRisk: 'als Produktionsrisiko kennzeichnet, erreicht Sie in der Planungs- oder der Freigabephase sofort' },
  es: { noConsultant: 'cuando no hay ningún consultor disponible', sideWins: 'salvo que este lado tenga un valor propio, que gana a ambos', neverAPass: 'nunca como aprobada', featureRetry: 'una PRIMERA ronda sin respuesta se repite', answeredStays: 'sigue retenida y le toca a usted', notAtTheSetting: 'no en el momento en que usted cambia el ajuste', productionRisk: 'declara riesgo de producción, con un motivo, le llega de inmediato' },
  ru: { noConsultant: 'или когда ни одного консультанта получить нельзя', sideWins: 'если только у этой стороны нет собственного значения: оно сильнее обоих', neverAPass: 'никогда не как «пройдено»', featureRetry: 'первый раунд без ответов повторяется', answeredStays: 'остаётся наложенной и за вами', notAtTheSetting: 'а не в момент смены настройки', productionRisk: 'объявляет риском для продакшена, с причиной, доходит до вас сразу' },
  uk: { noConsultant: 'або коли жодного консультанта не вдалося залучити', sideWins: 'хіба що ця сторона має власне значення: воно сильніше за обидва', neverAPass: 'ніколи не як «пройдено»', featureRetry: 'перший раунд без відповідей повторюється', answeredStays: 'лишається накладеним і за вами', notAtTheSetting: 'а не в момент зміни налаштування', productionRisk: 'оголошує ризиком для продакшену, з причиною, доходить до вас одразу' },
};

test('every translation keeps the no-consultant road, the side-wins clause, never-a-pass and the three limits of the release promise', () => {
  for (const language of HELP_LANGUAGES.filter((one): one is Exclude<typeof one, 'en'> => one !== 'en')) {
    const text = joined(language);
    const clauses = CLAUSES[language];
    assert.ok(text.includes(clauses.noConsultant), `the ${language} article lost the no-consultant road: ${clauses.noConsultant}`);
    assert.ok(text.includes(clauses.sideWins), `the ${language} article lost the side-wins clause: ${clauses.sideWins}`);
    assert.ok(text.includes(clauses.neverAPass), `the ${language} article lost not_reviewed is never a pass: ${clauses.neverAPass}`);
    assert.ok(text.includes(clauses.featureRetry), `the ${language} article lost the feature review's retry: ${clauses.featureRetry}`);
    assert.ok(text.includes(clauses.answeredStays), `the ${language} article lost that an answered hold stays the person's: ${clauses.answeredStays}`);
    assert.ok(text.includes(clauses.notAtTheSetting), `the ${language} article lost that the release is not at the setting: ${clauses.notAtTheSetting}`);
    assert.ok(text.includes(clauses.productionRisk), `the ${language} article lost that a production-risk question keeps its own path to the person: ${clauses.productionRisk}`);
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
  assert.ok(HELP.onExhausted.includes('a feature review’s first round, when it has a second to spend, is retried instead'), 'the tooltip does not qualify not_reviewed for a feature review');
  assert.ok(HELP.onExhausted.includes('could not settle'), 'the tooltip does not say the AI may still ask');
  assert.ok(HELP.onExhausted.includes('or when no consultant can be had'), 'the tooltip does not name the no-consultant road to a person');
});
