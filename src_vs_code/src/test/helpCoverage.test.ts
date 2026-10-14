import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { COPY_ANSWER } from '../chatPage';
import { BLOCK_CONTROL_TERMS } from '../renderAnswer';
import { TEAM_TAB_LABEL } from '../teamServerTabPage';
import { TEAM_SERVER_WITH_PEOPLE } from '../teamServerTab';

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

test('every language describes the sides seen and the path-fields switch, by the controls\' own English labels (paths per side E2.3)', () => {
  // The operator's ruling: the help is in FIVE languages. bodyFor marks a MISSING translation, not a stale one, so each
  // language is asked for the labels a person meets on Setup › This side — English in every language, as every control
  // name in this catalogue is.
  const article = HELP_ARTICLES.find((one) => one.id === 'settings-and-the-server');
  assert.ok(article !== undefined, 'the settings article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    const said = Object.values(body).join(' ');
    assert.equal(fallback, false, `the ${language} settings article is missing`);
    for (const label of ['**Path fields per side**', '**Forget**', '*Automatic*', '*Always two*', '*Always one*', '90']) {
      assert.ok(said.includes(label), `the ${language} settings article never says ${label} — the side registry is undocumented there`);
    }
  }
});

test('every language says the split order names Briefs and review, Code and search and Sonnet (model tiers)', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'the-gate');
  assert.ok(article !== undefined, 'the gate article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    const said = Object.values(body).join(' ');
    assert.equal(fallback, false, `the ${language} gate article is missing`);
    assert.ok(said.includes('**Split with the strongest model**'), `the ${language} gate article lost the split paragraph`);
    for (const label of ['**Briefs and review**', '**Code and search**', 'Sonnet']) {
      assert.ok(said.includes(label), `the ${language} gate article never says ${label} — the third model slot is undocumented there`);
    }
  }
});

test('every language says a CLI path is kept per side, and names the older-extension limit (paths per side E3)', () => {
  // Design (c) of research/PLAN_paths_per_side.md: "Said in the help and in the CHANGELOG when E3 ships." The two example
  // paths are the same in every language, so they are what all five are asked for; `PATH` is the lookup an emptied
  // field — and a side whose own value an older extension's save dropped — falls back to.
  const article = HELP_ARTICLES.find((one) => one.id === 'settings-and-the-server');
  assert.ok(article !== undefined, 'the settings article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const marker of ['`C:\\tools\\codex.cmd`', '`/usr/local/bin/codex`', 'PATH']) {
      assert.ok(said.includes(marker), `the ${language} settings article never says ${marker} — the CLI path per side is undocumented there`);
    }
    // E3 as built (2026-10-11): a value an older save dropped is said once, with **Restore** — the button the notice offers.
    assert.ok(said.includes('**Restore**'), `the ${language} settings article never says **Restore** — the way back to a dropped CLI path is undocumented there`);
  }
});

test('every language describes the two fields the pages draw, by the labels the pages carry (paths per side E4)', () => {
  // The labels and the caption are English on the page in every language, as every control name here is — so they are what
  // all five are asked for: this side's field, the other side's caption, the third group of folders, and the read-only
  // data directory's "from a window on that side".
  const article = HELP_ARTICLES.find((one) => one.id === 'settings-and-the-server');
  assert.ok(article !== undefined, 'the settings article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const label of ['**This side — Windows**', '*Used by the server on that side. This window does not check it.*', '*Both sides / unclear*', 'Setup › MCP server']) {
      assert.ok(said.includes(label), `the ${language} settings article never says ${label} — the two fields are undocumented there`);
    }
    assert.equal(said.includes('comes in a following release'), false, `the ${language} settings article still promises the other side's field for a later release`);
  }
});

test('every language says why the other side\'s boxes are gone while each side keeps its own settings (paths per side E4, own review)', () => {
  // The sentence the page draws in their place is English in every language, as every control text here is.
  const article = HELP_ARTICLES.find((one) => one.id === 'settings-and-the-server');
  assert.ok(article !== undefined, 'the settings article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    assert.ok(said.includes('*This side keeps its own settings — set the other side\'s value from a window on that side.*'),
      `the ${language} settings article never says why the other side's CLI path and folder box are not shown while each side keeps its own settings`);
  }
});

test('every language says how a Security lane pair names its reviewer, by the page\'s own English words (names and a missing root, story 1)', () => {
  // research/PLAN_names_and_a_missing_root.md: the pair's Reviewer now reads like the question consultant's picker — a
  // row by its card's name, `(switched off)`, `(stranded)`, "no reviewer — pick one", and a removed row whose pair the
  // server DROPS. The marks are English in every language, as every control text here is.
  const article = HELP_ARTICLES.find((one) => one.id === 'security-lane');
  assert.ok(article !== undefined, 'the security lane article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    const said = Object.values(body).join(' ');
    assert.equal(fallback, false, `the ${language} security lane article is missing`);
    for (const words of ['*(switched off)*', '*(stranded)*', '*no reviewer — pick one*', '**Change on Models**']) {
      assert.ok(said.includes(words), `the ${language} security lane article never says ${words} — how a pair names its reviewer is undocumented there`);
    }
  }
});

test('every language says the MCP server does not create a missing named data folder either (names and a missing root, story 3)', () => {
  // research/PLAN_names_and_a_missing_root.md: the paid **Check** is not started, the server's own modes stop, and a client
  // that starts the server gets one that refuses every tool naming the folder. `COAI_DATA_DIR` and **Check** are English
  // in every language — a variable and a control — so they are what all five are asked for.
  const article = HELP_ARTICLES.find((one) => one.id === 'where-your-data-lives');
  assert.ok(article !== undefined, 'the data article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const words of ['**Check**', '`COAI_DATA_DIR`']) {
      assert.ok(said.includes(words), `the ${language} data article never says ${words} — that the MCP server makes no missing named folder is undocumented there`);
    }
  }
});

test('every language names the Team server tab where the help lists the tabs of the log and the spending tab points at it', () => {
  // The same blind spot as the chat test above: bodyFor marks a MISSING translation, not a stale one, and the old
  // help promised a "Show the whole company" button that no longer exists (todo/PLAN_team_usage_by_person.md, D6).
  // The tab's label is English in every language, as every control name here is, so it is the string all five carry.
  const named = `**${TEAM_TAB_LABEL}**`;
  for (const id of ['the-rounds-log', 'what-each-ai-has-used', 'team-servers']) {
    const article = HELP_ARTICLES.find((one) => one.id === id);
    assert.ok(article !== undefined, `the ${id} article has been renamed, and this test is now asserting nothing`);
    for (const language of HELP_LANGUAGES) {
      const said = Object.values(bodyFor(article, language).body).join(' ');
      assert.ok(said.includes(named), `the ${language} ${id} article never names the ${TEAM_TAB_LABEL} tab — stale rather than missing`);
      assert.ok(!said.includes('Show the whole company'), `the ${language} ${id} article still promises the removed Company toggle`);
    }
  }
});

test('every language says what the Team server tab shows from a newer server — names, last seen, ~$, the chart — and what an older one says', () => {
  // Epic 3 of todo/PLAN_team_usage_by_person.md. The control names are English in every language, as everywhere here,
  // and the version is read from the one constant the tab prints, so a release that moves it moves the help's check too.
  const article = HELP_ARTICLES.find((one) => one.id === 'the-rounds-log');
  assert.ok(article !== undefined, 'the rounds-log article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const named of ['**last seen**', '**~$**', '**≥**', '**launches per day**', '**The numbers**', `needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE}`]) {
      assert.ok(said.includes(named), `the ${language} rounds-log article never says “${named}” — stale rather than missing`);
    }
  }
});

test('every language says the Team server tab shows the server\'s own rounds, the runs without a round apart, and count unavailable for an older server', () => {
  // Story 5.2 of todo/PLAN_every_round_is_counted.md: the tab's Vendors column gains a Rounds block counted by the SERVER
  // over 5 h / 24 h / 7 d. The block's words are English in every language, as every control name here is.
  const article = HELP_ARTICLES.find((one) => one.id === 'the-rounds-log');
  assert.ok(article !== undefined, 'the rounds-log article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const named of ['**Rounds**', '**5 h**', '**24 h**', '**7 d**', '**runs without a round**', '**budget refusals**', '*count unavailable*']) {
      assert.ok(said.includes(named), `the ${language} rounds-log article never says “${named}” — stale rather than missing`);
    }
  }
});

test('every language says the spending tab prices each line at its own model and marks a floor ≥', () => {
  // Story 2.5 of todo/PLAN_every_round_is_counted.md: the tab now prices line by line and shows `≥` when a line could not
  // be priced. Same blind spot as above — a translation a release out of date is not a MISSING one — so the one string
  // every language carries, the mark and its example figure, is asked for in all five.
  const article = HELP_ARTICLES.find((one) => one.id === 'what-each-ai-has-used');
  assert.ok(article !== undefined, 'the spending article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const named of ['**≥**', '`≥ ~$2.40`']) {
      assert.ok(said.includes(named), `the ${language} spending article never says “${named}” — stale rather than missing`);
    }
  }
});

test('every language says the Rounds block comes first on the spending tab, and that a count nobody has is words, never 0', () => {
  // Stories 2.2–2.4 of todo/PLAN_every_round_is_counted.md: the tab's FIRST block counts rounds per vendor row over
  // 5 h / 24 h / 7 d, and a read that failed (an older coai-mcp, a database that would not open) says *count unavailable*
  // rather than drawing a zero. The block's own words are English in every language, as every control name here is.
  const article = HELP_ARTICLES.find((one) => one.id === 'what-each-ai-has-used');
  assert.ok(article !== undefined, 'the spending article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const named of ['**Rounds**', '*count unavailable*', '**5 h**', '**7 d**', '--round-counts']) {
      assert.ok(said.includes(named), `the ${language} spending article never says “${named}” — stale rather than missing`);
    }
  }
});

test('every language says a probe sits beside the rounds as its own kind, and that every paid launch is its own ledger line', () => {
  // Story 3.3 of todo/PLAN_every_round_is_counted.md: `coai-mcp --probe-api --model` writes a line per paid completion,
  // shown on the Rounds block as **probe** and never as a round; and a repair, a follow-up, a retry and a rotated
  // Team-server attempt each write a line of their own, so a vendor's runs count them. The command and the kind's name
  // are English in every language, as every control name here is.
  const article = HELP_ARTICLES.find((one) => one.id === 'what-each-ai-has-used');
  assert.ok(article !== undefined, 'the spending article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    for (const named of ['**probe**', '--probe-api --model', '`turn`']) {
      assert.ok(said.includes(named), `the ${language} spending article never says “${named}” — stale rather than missing`);
    }
  }
});

test('no language still says the product ships no price table — every one names the list the default price comes from', () => {
  // Untrue since the list prices (research/PLAN_models_card_prices_every_row.md): a model is priced at its public list
  // price unless a rate is typed, so "leave it empty and it stays a dash" sent a reader to type a rate the tab no
  // longer needs. The stale sentence is a different sentence in each language, so each is named; the list's own name is
  // the one word all five share.
  const stale: Readonly<Record<string, string>> = {
    en: 'ships no price table', de: 'keine Preistabelle', es: 'no incluye tabla de precios',
    ru: 'Прайс-лист продукт не поставляет', uk: 'Прайс-листа цей продукт навмисно не постачає',
  };
  const article = HELP_ARTICLES.find((one) => one.id === 'what-each-ai-has-used');
  assert.ok(article !== undefined, 'the spending article has been renamed, and this test is now asserting nothing');
  for (const language of HELP_LANGUAGES) {
    const said = Object.values(bodyFor(article, language).body).join(' ');
    assert.ok(stale[language] !== undefined, `${language} has no stale phrase on record — add the one its article used`);
    assert.ok(!said.includes(stale[language]!), `the ${language} spending article still says “${stale[language]}”, which has been untrue since the list prices`);
    assert.ok(said.includes('OpenRouter'), `the ${language} spending article never names the list the default price comes from`);
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
  // `coai.editRoles`, `coai.editCommands` and `coai.editChatPresets` need no alias since E5.2: the pages they opened are
  // gone, and the Settings tab's article names the three by their titles as the redirects they are for one release.
  'coai.copyConfigBlock': 'copy the mcp config block',
  'coai.copyClaudeSnippet': 'copy the claude.md snippet',
  'coai.answerQuestion': 'answer it in the panel',
  'coai.answerQuestionWaiting': 'the title-bar icon turns green',
  'coai.showRounds': 'show review rounds',
  // Its title is one word that every article uses, so the title alone would pass whether or not the gear
  // was ever explained — the phrase is the article's own.
  'coai.openSettings': 'the gear beside the help button',
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
  'coai.securityLane': 'security lane',
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
  'coai.chatModel': 'which model a chat opens on',
  'coai.vendors': 'every model this side can use',
  'coai.teamServers': 'a team server is the other way to get a reviewer',
  'coai.planPrices': 'subscription prices',
  'coai.onExhausted': 'when the rounds run out',
  'coai.maxConcurrency': 'reviewers at once',
  'coai.maxPerProvider': 'per vendor',
  'coai.reviewerTimeoutMinutes': 'reviewer timeout',
  'coai.roundTimeoutMinutes': 'a whole round',
  'coai.escalationMinutes': 'wait for you',
  'coai.rounds': 'rounds',
  'coai.thresholds': 'passes at or under',
  'coai.roleEnabled': 'its **active** tick in **reviews › roles & prompts**',
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
  'coai.stopLocalWhenQuiet': 'stop the local reviewer when the cloud reviewers found almost nothing',
  // Issue #117: the switch names the caller's own strongest model now, and the three models are chosen
  // per kind of assistant — the key keeps its historical name.
  'coai.splitWithFable': 'split with the strongest model',
  'coai.commandModels': 'chosen per kind of assistant',
  // Issue #131: the article names both choices and says what neither of them is.
  'coai.gatePer': 'never once per story',
  'coai.perSideSettings': 'separate settings for each side',
  // research/PLAN_paths_per_side.md E2.3: the switch, by the label Setup › This side gives it.
  'coai.pathFieldsPerSide': 'path fields per side',
  // The consultant's five. The caller map and each cap is a setting of its own, so each one is
  // covered by the words the article actually uses for it.
  'coai.consultants': 'who a stuck ai asks',
  'coai.consultTurns': 'turns per consultation',
  'coai.consultCallsPerSession': 'calls per session',
  'coai.consultIdleMinutes': 'close an idle consultation after, minutes',
  'coai.consultEnabled': 'let an ai consult another vendor',
  // The consultation cadence's four (research/PLAN_consult_on_a_cadence.md), by the panel's own labels.
  'coai.cadenceMode': 'consultation cadence',
  'coai.cadenceEvery': 'one consultation per this many epics',
  'coai.cadenceRiskThreshold': 'ask for the riskiest pieces from this many epics',
  'coai.cadenceRiskMax': 'riskiest pieces per plan, at most',
  // The question consultant's eight (todo/PLAN_question_consultant.md, S4), by the tab's own labels.
  'coai.qconsultEnabled': 'let an ai ask the question consultant first',
  'coai.qconsultMode': 'before it asks you',
  'coai.qconsultRows': 'who answers',
  'coai.qconsultPrompts': 'base prompts',
  'coai.qconsultRoots': 'folders a disk row may read',
  'coai.qconsultRowMinutes': 'minutes one row may run',
  'coai.qconsultQuestionsPerSession': 'questions per session',
  'coai.qconsultFreeBatches': 'question batches that reach you first',
  // The storage pair (issue #115). Their article is the one a person reaches for after reinstalling
  // an operating system, which is the moment these two settings exist for.
  'coai.dataDirectory': 'where your data lives',
  'coai.dataSide': 'a name for this installation',
  // The one that only bites when two installations share a person: a round running in WSL writes its
  // question where a Windows window is not looking, and the round blocks on a modal nobody sees.
  'coai.alsoWatchDataDirectories': 'questions from another installation',
  // The model catalog's one-time move (PLAN_one_model_catalog.md E1.3): the marker and the backup, both written by
  // the extension, by the words the article uses for them.
  'coai.catalogMigration': 'moved into the catalog',
  'coai.migratedFrom': 'a copy of them as they were',
  // The chat presets' move (E4.6a): its record.
  'coai.chatPresetsMoved': 'remembers which chat presets moved',
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
