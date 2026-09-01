import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';

/**
 * Every command in the manifest, and every setting, is described somewhere in the help.
 *
 * <p>A one-off audit finds the gaps once. This test finds them on the commit that ADDS the next
 * command, which is the only moment it is cheap to write the article — and it is why the help
 * cannot quietly rot into a description of a product that no longer exists.</p>
 *
 * <p><b>The shape is a forced choice, not a pass mark.</b> A command clears this one of three
 * ways: the help names it verbatim, an ALIAS declares the words the help uses instead, or
 * SELF_EVIDENT declares — in a sentence, on the record — why it needs no article. There is no
 * fourth way and no silent default, so a new command cannot merely be forgotten.</p>
 */

interface Manifest {
  contributes: {
    commands: Array<{ command: string; title: string }>;
    configuration: { properties: Record<string, unknown> };
  };
}

const manifest = JSON.parse(
  fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'),
) as Manifest;

/** Every English word of every article, lowercased — the corpus a reader actually gets. */
const corpus = HELP_ARTICLES.map((a) => Object.values(a.en).join(' '))
  .join(' ')
  .toLowerCase();

/** A menu title as prose: no trailing ellipsis, no parenthetical aside. */
function asProse(title: string): string {
  return title
    .replace(/(\.\.\.|…)\s*$/u, '')
    .replace(/\s*\(.*?\)\s*$/u, '')
    .trim()
    .toLowerCase();
}

/**
 * The help says it in different words. The phrase IS the assertion: rewrite the article and drop
 * the phrase, and this goes red rather than quietly losing the coverage.
 */
const ALIAS: Record<string, string> = {
  'coai.copyConfigBlock': 'copy the mcp config block',
  'coai.copyClaudeSnippet': 'copy the claude.md snippet',
  'coai.answerQuestion': 'answer it in the panel',
  'coai.answerQuestionWaiting': 'the title-bar icon turns green',
  'coai.showRounds': 'show review rounds',
};

/** Why a command needs no article. A sentence, on the record — never an empty string. */
const SELF_EVIDENT: Record<string, string> = {
  'coai.help': 'It opens this help. An article explaining how to open the thing you are reading would be a joke at the reader’s expense.',
};

const SETTING_ALIAS: Record<string, string> = {
  'coai.vendors': 'each row is one vendor',
  'coai.maxRoundsPlan': 'rounds',
  'coai.maxRoundsCode': 'rounds',
  'coai.gateThresholdPlan': 'passes at or under',
  'coai.gateThresholdCode': 'passes at or under',
  'coai.onExhausted': 'when the rounds run out',
  'coai.maxConcurrency': 'reviewers at once',
  'coai.maxPerProvider': 'per vendor',
  'coai.reviewerTimeoutMinutes': 'reviewer timeout',
  'coai.escalationMinutes': 'wait for you',
  'coai.credsKey': 'credsfordevs config key',
  'coai.language': 'ask and answer in',
  'coai.translator.provider': 'translated by',
  'coai.translator.model': 'which small, fast model does it',
  'coai.promptsPerRound': 'one picker per round per role',
  'coai.rotatePrompts': 'rotate the lenses automatically',
  'coai.uiScale': 'text size',
  'coai.helpLanguage': 'language switch',
};

test('every command is described in the help, or declared self-evident with a reason', () => {
  for (const { command, title } of manifest.contributes.commands) {
    if (SELF_EVIDENT[command] !== undefined) {
      assert.ok(SELF_EVIDENT[command]!.length > 20, `${command}: the reason must be a sentence`);
      continue;
    }
    const alias = ALIAS[command];
    const needle = alias ?? asProse(title);
    assert.ok(
      corpus.includes(needle),
      `${command} ("${title}") is in no article. Write one, add an ALIAS for the words the help uses, or declare it SELF_EVIDENT with a reason.`,
    );
  }
});

test('every setting is described in the help', () => {
  for (const key of Object.keys(manifest.contributes.configuration.properties)) {
    const needle = SETTING_ALIAS[key];
    assert.ok(needle !== undefined, `${key} has no SETTING_ALIAS — add the words the help uses for it.`);
    assert.ok(corpus.includes(needle), `${key}: the help no longer says "${needle}".`);
  }
});

test('the aliases are honest: nothing declared covered by a phrase nobody wrote', () => {
  for (const [command, phrase] of Object.entries(ALIAS)) {
    assert.ok(corpus.includes(phrase), `${command}: ALIAS phrase "${phrase}" is in no article.`);
  }
});

test('the first four articles are what a person does in their first ten minutes', () => {
  // The order is the whole navigation: a reader who opens help after installing the extension
  // should find "install the server" at the top, not alphabetically somewhere in the middle.
  assert.deepEqual(
    HELP_ARTICLES.slice(0, 4).map((a) => a.id),
    ['install-the-server', 'choose-reviewers', 'teach-your-ai', 'the-gate'],
  );
});

test('every article carries all five sections in every language it claims', () => {
  for (const article of HELP_ARTICLES) {
    for (const language of HELP_LANGUAGES) {
      const { body } = bodyFor(article, language);
      for (const [section, text] of Object.entries(body)) {
        // A title is a title; the five ANSWERS are what have to be substantial. Holding a title
        // to the same floor was the first version of this test, and it failed on a good title.
        const floor = section === 'title' ? 10 : 60;
        assert.ok(
          text.trim().length > floor,
          `${article.id}/${language}: "${section}" is missing or too short to be an answer`,
        );
      }
    }
  }
});

test('article ids are unique, because a lookup returns the first match', () => {
  const ids = HELP_ARTICLES.map((a) => a.id);
  assert.equal(new Set(ids).size, ids.length);
});

/**
 * The fallback is honest, which is exactly why it needs a test of its own.
 *
 * <p>{@link bodyFor} answers an untranslated article with the English body and a visible note, so
 * every test above passes whether or not a translation exists. That is right for a reader and
 * wrong for a build: a new article would quietly become English-only in four languages and
 * nothing would say so.</p>
 */
test('every article exists in every language the switch offers', () => {
  const missing: string[] = [];
  for (const article of HELP_ARTICLES) {
    for (const language of HELP_LANGUAGES) {
      if (language !== 'en' && bodyFor(article, language).fallback) {
        missing.push(`${article.id}/${language}`);
      }
    }
  }
  assert.deepEqual(missing, [], `untranslated: ${missing.join(', ')}`);
});

test('a translation is a translation, not the English text pasted across', () => {
  for (const article of HELP_ARTICLES) {
    for (const language of HELP_LANGUAGES) {
      if (language === 'en') {
        continue;
      }
      const { body } = bodyFor(article, language);
      assert.notEqual(
        body.whatItIs.trim(),
        article.en.whatItIs.trim(),
        `${article.id}/${language}: the body is the English one, so the fallback would have been more honest`,
      );
    }
  }
});
