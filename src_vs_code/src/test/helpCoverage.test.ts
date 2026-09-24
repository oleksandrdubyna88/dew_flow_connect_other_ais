import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { COPY_ANSWER } from '../chatPage';
import { BLOCK_CONTROL_TERMS } from '../renderAnswer';

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

test('every language’s chat article names the header button, not just the article key', () => {
  // THIS TEST EXISTS BECAUSE THE ONE BELOW CANNOT SEE IT. `bodyFor` marks a translation that is
  // MISSING; it cannot mark one that is merely a release out of date, and a reader of the Russian
  // help is then told about a page that has moved. The control's own label is English in every
  // language — as every other control name in this catalogue is — so it is the one string all five
  // can be asked for. (codex, D2's plan round.)
  const article = HELP_ARTICLES.find((one) => one.id === 'chat-with-other-ai');
  assert.ok(article !== undefined, 'the chat article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} chat article is missing, so a reader of it gets English`);
    assert.match(
      Object.values(body).join(' '),
      /New chat/u,
      `the ${language} chat article does not mention the New chat button, so a reader of it never learns `
      + 'where the reset is — the article is a release out of date rather than absent, which nothing else here can see',
    );
  }
});

test('every language’s chat article names both copy controls and the reserved reply tag', () => {
  // The same blind spot as the test above, on the change that introduced a SECOND copy control. Three
  // English strings every language has to carry: the two labels, whose difference is the entire point
  // of renaming one of them, and the fence tag, which is the only thing the extension recognises — a
  // reader whose help omits it never learns how to make a model produce one, and the feature is
  // invisible to them. (codex, the plan round.)
  const article = HELP_ARTICLES.find((one) => one.id === 'chat-with-other-ai');
  assert.ok(article !== undefined, 'the chat article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    // DERIVED from the renderer, not retyped: a list a test repeats will not notice the next control
    // added beside it, and would stay green while a language stopped covering the page.
    for (const named of [COPY_ANSWER, ...BLOCK_CONTROL_TERMS]) {
      assert.ok(
        said.includes(named),
        `the ${language} chat article never says “${named}”, so a reader of it cannot use the control `
        + 'or cannot make a model produce the block it is for — stale rather than missing, which nothing else here can see',
      );
    }
  }
});

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
  'coai.editChatPresets': 'edit chat presets',
  'coai.editRoles': 'opens the roles page',
  'coai.editCommands': 'opens the commands page',
  'coai.copyConfigBlock': 'copy the mcp config block',
  'coai.copyClaudeSnippet': 'copy the claude.md snippet',
  'coai.answerQuestion': 'answer it in the panel',
  'coai.answerQuestionWaiting': 'the title-bar icon turns green',
  'coai.showRounds': 'show review rounds',
  // The panel section and the page are both called *Notifications*; nothing in the product says
  // "show notifications" to a person, and an article written to satisfy this test rather than to
  // be read is the thing an alias exists to avoid.
  'coai.showNotifications': 'every message this extension has shown you',
  // It is hidden from the palette (`when: false`), because from there it would act on a list that is
  // not open. The help therefore describes what the KEYBINDING does rather than naming a command a
  // reader can never type — which is exactly what an alias is for.
  'coai.forgetPickedConversation': 'forgets the conversation under the cursor',
};

/** Why a command needs no article. A sentence, on the record — never an empty string. */
const SELF_EVIDENT: Record<string, string> = {
  'coai.help': 'It opens this help. An article explaining how to open the thing you are reading would be a joke at the reader’s expense.',
};

const SETTING_ALIAS: Record<string, string> = {
  'coai.bugzModel': 'ranking model',
  'coai.bugzServer': 'review bugs',
  'coai.chatPrompt': 'the prompt the passage travels with',
  'coai.chatPromptChoice': 'which of your saved prompts a chat opens with',
  'coai.chatModelName': 'which of its models answers',
  'coai.phrases': 'a list of sentences you keep',
  'coai.commands': 'commands of your own',
  'coai.chatPromptPresets': 'your saved prompts, each with a name',
  'coai.chatModelPresets': 'your saved models, each with a name',
  'coai.chatLanguage': 'the language the other ai answers in',
  'coai.chatAutoSend': 'whether it sends at once or waits',
  'coai.chatModel': 'which reviewer answers a chat',
  'coai.vendors': 'each row is one vendor',
  'coai.teamServers': 'a team server is the other way to get a reviewer',
  'coai.onExhausted': 'when the rounds run out',
  'coai.maxConcurrency': 'reviewers at once',
  'coai.maxPerProvider': 'per vendor',
  'coai.reviewerTimeoutMinutes': 'reviewer timeout',
  'coai.roundTimeoutMinutes': 'a whole round',
  'coai.escalationMinutes': 'wait for you',
  'coai.rounds': 'rounds',
  'coai.thresholds': 'passes at or under',
  'coai.roleEnabled': 'tick box on its own heading',
  'coai.dealPlanLenses': 'deal the lenses across vendors',
  'coai.dealCodeLenses': 'deal the lenses across vendors',
  'coai.codeWorkspace': 'what a reviewer gets',
  'coai.credsKey': 'credsfordevs config key',
  'coai.promptsPerRound': 'one picker per round per role',
  'coai.roles': 'the roles you add are your own',
  'coai.uiScale': 'text size',
  'coai.textTone': 'text tone',
  'coai.helpLanguage': 'language switch',
  // The three gate switches. They shipped undeclared, so this guard never saw them: a setting that
  // VS Code does not know about is a setting nothing checks the help for either.
  'coai.autonomous': 'work autonomously',
  'coai.splitPlan': 'split the plan into epics and stories',
  // Issue #117: the switch names the caller's own strongest model now, and the two models are chosen
  // per kind of assistant — the key keeps its historical name.
  'coai.splitWithFable': 'split with the strongest model',
  'coai.commandModels': 'chosen per kind of assistant',
  // Issue #131: the article names both choices and says what neither of them is.
  'coai.gatePer': 'never once per story',
  'coai.perSideSettings': 'separate settings for each side',
  // The consultant's five. The caller map and each cap is a setting of its own, so each one is
  // covered by the words the article actually uses for it.
  'coai.consultants': 'who a stuck ai asks',
  'coai.consultTurns': 'turns per consultation',
  'coai.consultCallsPerSession': 'calls per session',
  'coai.consultIdleMinutes': 'close an idle consultation after, minutes',
  'coai.consultEnabled': 'let a stuck ai consult another vendor',
  // The storage pair (issue #115). Their article is the one a person reaches for after reinstalling
  // an operating system, which is the moment these two settings exist for.
  'coai.dataDirectory': 'where your data lives',
  'coai.dataSide': 'a name for this installation',
  // The one that only bites when two installations share a person: a round running in WSL writes its
  // question where a Windows window is not looking, and the round blocks on a modal nobody sees.
  'coai.alsoWatchDataDirectories': 'questions from another installation',
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

test('every language says the logs move, not only English', () => {
  // `bodyFor` marks a translation that is MISSING and says nothing about one that is BEHIND, which
  // is this repository's own recorded trap — the audit-log article was a release out of date in four
  // languages and nothing caught it. The five were edited together when the logs started moving
  // (2026-09-15); this is what catches the next change that forgets one. (codex, plan round.)
  const article = HELP_ARTICLES.find((one) => one.id === 'where-your-data-lives');
  assert.ok(article !== undefined, 'the storage article has been renamed, and this asserts nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} storage article is missing, so a reader gets English`);
    assert.match(
      Object.values(body).join(' '),
      // Two alphabets. The Russian and Ukrainian articles say "логи", which a Latin-only pattern
      // reads as an absence — the first version of this test did exactly that and accused a
      // translation that was perfectly correct.
      /log|лог/iu,
      `the ${language} storage article never mentions the logs, so a reader of it moves their data `
      + 'believing the logs stayed behind — or does not know to look for them',
    );
  }
});
