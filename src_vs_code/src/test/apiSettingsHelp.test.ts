import assert from 'node:assert/strict';
import { test } from 'node:test';

import { API_SETTINGS_SINCE } from '../apiSettings';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';

/**
 * An api card's per-model settings are in the help in all five languages (story S3.8 of
 * `todo/PLAN_feature_review.md`).
 *
 * <p>`bodyFor` marks a translation that is MISSING and says nothing about one that is BEHIND, so each new
 * thing is asked of every language by a string all five must carry: the controls' English labels (every
 * control name in this catalogue is English in every language) and the release that has them.</p>
 */

/** The labels the card draws — English in every language, as every other control name in the catalogue. */
const LABELS = ['thinking', 'effort', 'max review time (minutes)', 'reset to calibrated default'] as const;

test('every language’s reviewers article names the three settings, the reset, and the coai-mcp that has them', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} reviewers article is missing, so a reader of it gets English`);
    const said = Object.values(body).join(' ');

    for (const label of LABELS) {
      assert.ok(said.includes(`**${label}**`), `the ${language} reviewers article never names the "${label}" control`);
    }
    assert.ok(said.includes(API_SETTINGS_SINCE), `the ${language} reviewers article does not say which coai-mcp has the settings`);
  }
});
