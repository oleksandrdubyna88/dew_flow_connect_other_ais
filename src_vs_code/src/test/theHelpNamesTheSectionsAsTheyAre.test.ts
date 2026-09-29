import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';

/**
 * The help calls the sidebar's sections what they are called now, in every language
 * (`research/PLAN_every_page_reads_alike.md`, S4): **Active rounds** became **Active gates** and **Active
 * consultations** on 2026-09-29. A test that only forbade the old name would pass on a translation that
 * simply deleted the sentences, so each language must also NAME both new sections — in English, as every
 * UI name is in every translation here.
 */

/** Everything one language's help says, every article and every part of it. */
function everythingIn(language: (typeof HELP_LANGUAGES)[number]): string {
  return HELP_ARTICLES.map((article) => Object.values(bodyFor(article, language).body).join('\n')).join('\n');
}

// The two articles that walk through the sidebar's sections. Scoped to them, because Consultation cadence
// is also the name of a Settings control, which the help named long before it was a section too.
const SIDEBAR_ARTICLES = ['recent-rounds', 'the-settings-tab'];

for (const language of HELP_LANGUAGES) {
  test(`the ${language} help's sidebar articles name Consultation cadence, the section the cadence lines moved to`, () => {
    for (const id of SIDEBAR_ARTICLES) {
      const article = HELP_ARTICLES.find((one) => one.id === id);
      assert.ok(article !== undefined, `there is no ${id} article`);
      const text = Object.values(bodyFor(article, language).body).join('\n');

      assert.ok(text.includes('**Consultation cadence**'), `the ${language} ${id} article does not name the Consultation cadence section`);
    }
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
