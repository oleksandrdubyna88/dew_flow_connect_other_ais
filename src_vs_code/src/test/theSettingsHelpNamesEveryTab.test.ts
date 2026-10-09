import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { CATALOG_TABS } from '../catalogPlaces';

/**
 * The help's tour of the Settings tab names every tab the page draws, in the order it draws them,
 * in every language. The tabs are read from `CATALOG_TABS` — the list the page itself is built
 * from — so a tab added there is a tab this test asks the help about, rather than one a hand-kept
 * copy of the list never heard of. (The Security lane tab shipped without a word here.)
 *
 * <p>It waited as a TODO through E5.1, whose step 5 removed the page the article toured; E5.2 of
 * research/PLAN_one_model_catalog.md rewrote the article in five languages around `CATALOG_TABS`, and it holds again.</p>
 */
for (const language of HELP_LANGUAGES) {
  test(`the ${language} Settings tab article names every Settings tab, in the order the page draws them`, () => {
    const article = HELP_ARTICLES.find((one) => one.id === 'the-settings-tab');
    assert.ok(article, 'there is no the-settings-tab article');
    const { body, fallback } = bodyFor(article, language);
    // The language's OWN article: a missing translation falls back to the English, which would pass for every language.
    assert.equal(fallback, false, `the ${language} article is missing and the English stands in for it`);
    const text = body.whatItIs;
    const titles = CATALOG_TABS.map((tab) => tab.label);
    assert.ok(titles.length > 4, 'the Settings page drew almost no tabs, so this compared nothing');

    let after = -1;
    for (const title of titles) {
      const at = text.indexOf(title, after + 1);
      assert.ok(at > after, `the ${language} article does not name "${title}" after the tab before it`);
      after = at;
    }
  });
}
