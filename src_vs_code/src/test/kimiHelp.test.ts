import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { KIMI_RUNTIME_SINCE } from '../runtimeSince';

/**
 * The help for the Kimi Code row, in five languages (E4.5 of todo/PLAN_vibe_and_kimi_runtimes_E4.md; brief item 11) — the
 * Vibe row's shape: what the row is, Windows-only, the sign-in, the update, the version warning. It claims NOTHING about the
 * owned home never receiving code: M-K12 (c) is unrecorded (parent D19).
 */

test('every language’s choose-reviewers article names Kimi Code, where it runs (Windows, WSL2), and how it is signed in and updated', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} article is missing`);
    assert.ok(body.setup.includes('**Kimi Code**'), `the ${language} article does not name the Kimi preset`);
    assert.ok(body.setup.includes('kimi login'), `the ${language} article does not say how Kimi is signed in`);
    assert.ok(body.setup.includes('winget'), `the ${language} article does not say the Windows line installs rg and fd`);
    assert.ok(body.setup.includes(KIMI_RUNTIME_SINCE), `the ${language} article does not name coai-mcp ${KIMI_RUNTIME_SINCE}`);
    assert.ok(body.usage.includes('@moonshot-ai/kimi-code'), `the ${language} article does not say where the newest Kimi is read from`);
    assert.ok(body.usage.includes('kimi-code/install'), `the ${language} article does not say how Kimi is updated`);
    assert.doesNotMatch(Object.values(body).join(' '), /owned home never receives code/u, 'M-K12 (c) is unrecorded — the help claims nothing about it');
  }
});

test('every language’s choose-reviewers article lists Kimi among the rows that run with a warning', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers')!;

  for (const language of HELP_LANGUAGES) {
    assert.ok(bodyFor(article, language).body.whatCanGoWrong.includes('Kimi'), `the ${language} article names no Kimi version warning`);
  }
});
