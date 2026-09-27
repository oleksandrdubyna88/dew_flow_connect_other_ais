import assert from 'node:assert/strict';
import { test } from 'node:test';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';

/**
 * The feature gate's person-facing half is in the help in all five languages (story S3.3 of
 * `todo/PLAN_feature_review.md`).
 *
 * <p>`bodyFor` marks a translation that is MISSING and says nothing about one that is BEHIND — this
 * repository's own recorded trap — so each new thing is asked of every language by a string all five
 * must carry: the control's English label (every control name in this catalogue is English in every
 * language), the tool's name, and the badge's words.</p>
 */

function said(id: string, language: (typeof HELP_LANGUAGES)[number]): string {
  const article = HELP_ARTICLES.find((one) => one.id === id);
  assert.ok(article !== undefined, `the ${id} article has been renamed, and this test is now asserting nothing`);
  const { body, fallback } = bodyFor(article, language);
  assert.equal(fallback, false, `the ${language} ${id} article is missing, so a reader of it gets English`);

  return Object.values(body).join(' ');
}

test('every language’s reviewers article names the fourth switch and who cannot have it', () => {
  for (const language of HELP_LANGUAGES) {
    const text = said('choose-reviewers', language);

    assert.ok(text.includes('reviews features'), `the ${language} reviewers article never names the "reviews features" switch`);
    assert.ok(text.includes('0.39.0'), `the ${language} reviewers article does not say which coai-mcp runs feature reviews`);
  }
});

test('every language’s snippet article says the paste now teaches review_feature, for three or more epics', () => {
  for (const language of HELP_LANGUAGES) {
    const text = said('teach-your-ai', language);

    assert.ok(text.includes('review_feature'), `the ${language} snippet article never mentions review_feature`);
    assert.ok(text.includes('3'), `the ${language} snippet article does not say the feature gate is for three or more epics`);
  }
});

test('every language’s rounds-log article says what a skipped round is', () => {
  for (const language of HELP_LANGUAGES) {
    const text = said('recent-rounds', language);

    assert.ok(text.includes('skipped — did not block'), `the ${language} rounds article never explains the skipped badge`);
  }
});
