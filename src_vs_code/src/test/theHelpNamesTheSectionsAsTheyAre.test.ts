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

for (const language of HELP_LANGUAGES) {
  test(`the ${language} help no longer sends anybody to Active rounds, and names both sections that replaced it`, () => {
    const text = everythingIn(language);

    assert.ok(!text.includes('Active rounds'), `the ${language} help still names Active rounds`);
    assert.ok(text.includes('**Active gates**'), `the ${language} help never names Active gates`);
    assert.ok(text.includes('**Active consultations**'), `the ${language} help never names Active consultations`);
  });
}
