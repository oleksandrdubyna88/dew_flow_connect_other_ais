import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';

/**
 * The help calls the sidebar's sections what they are called now, in every language
 * (`research/PLAN_every_page_reads_alike.md`, S4): **Active rounds** became **Active gates** and **Active
 * consultations** on 2026-09-29. A test that only forbade the old name would pass on a translation that
 * simply deleted the sentences, so each language must also NAME both new sections — in English, as every
 * UI name is in every translation here. **Consultation cadence** was a third section until v2 (E4, story s6-dead-v1-out): it is gone, and so is every
 * sentence that calls it a section.
 */

/** Everything one language's help says, every article and every part of it. */
function everythingIn(language: (typeof HELP_LANGUAGES)[number]): string {
  return HELP_ARTICLES.map((article) => Object.values(bodyFor(article, language).body).join('\n')).join('\n');
}

// The two articles that walk through the sidebar's sections.
const SIDEBAR_ARTICLES = ['recent-rounds', 'the-settings-tab'];

for (const language of HELP_LANGUAGES) {
  test(`the ${language} help's sidebar articles do not say Consultation cadence is a section of the sidebar (removed in v2), and no article names it at all`, () => {
    for (const id of SIDEBAR_ARTICLES) {
      const article = HELP_ARTICLES.find((one) => one.id === id);
      assert.ok(article !== undefined, `there is no ${id} article`);
      const text = Object.values(bodyFor(article, language).body).join('\n');

      assert.ok(!/Consultation cadence/u.test(text), `the ${language} ${id} article still names Consultation cadence`);
      assert.ok(text.includes('**Active gates**') && text.includes('**Active consultations**'), `the ${language} ${id} article lost the two sections that are still there`);
    }
    assert.ok(!/Consultation cadence/u.test(everythingIn(language)), `the ${language} help still names Consultation cadence somewhere`);
  });
}

for (const language of HELP_LANGUAGES) {
  test(`the ${language} help no longer sends anybody to Active rounds, and names both sections that replaced it`, () => {
    const text = everythingIn(language);

    assert.ok(!text.includes('Active rounds'), `the ${language} help still names Active rounds`);
    assert.ok(text.includes('**Active gates**'), `the ${language} help never names Active gates`);
    assert.ok(text.includes('**Active consultations**'), `the ${language} help never names Active consultations`);
  });
}
