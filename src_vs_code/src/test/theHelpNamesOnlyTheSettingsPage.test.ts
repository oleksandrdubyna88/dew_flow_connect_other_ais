import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP } from '../help';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor, type HelpLanguage } from '../helpContent';

/**
 * The help sends nobody to UI the Settings page no longer draws (todo/PLAN_one_model_catalog.md, E5.2).
 *
 * <p>E5.1 removed the old Settings page: its Reviewers tab and reviewer cards, **Add a reviewer**, the Review roles, Gate
 * commands and Chat presets tabs, the pages **Edit roles…** and **Edit commands…** opened, **Chat other AIs**, and the
 * preview switch between the two pages. The page that is left is `CATALOG_TABS` — Models first. An article that still
 * says "press Add a reviewer on the Reviewers tab" describes a button nobody can find.</p>
 *
 * <p><b>The list is per language</b> (the plan round's finding 1). A control's label stays English in every language, so
 * the labels are checked everywhere; but the words AROUND a label are translated — de *Reviewer-Karte*, ru *карточка
 * ревьюера* — and an English-only list would pass a German article that still tours the old page. So each language has
 * its own words, each with the reason it is no longer true.</p>
 *
 * <p><b>Tooltips</b> (`help.ts`) are English only — the language files hold articles, not tooltips — and are read with
 * the English list. The sidebar's articles say "section", and are right: the sidebar keeps its sections, so nothing
 * here names that word.</p>
 */

/** One thing the Settings page no longer draws, how it is said, why it is wrong now, and a phrase it must flag. */
interface Removed {
  readonly pattern: RegExp;
  readonly why: string;
  /** A phrase the pattern must flag — what the canary runs, so a pattern that matches nothing is a red test. */
  readonly sample: string;
}

/** The English labels, the same in every language — a button's name is never translated here. */
const EVERY_LANGUAGE: readonly Removed[] = [
  { pattern: /Add a reviewer/u, why: 'the old page\'s button; a model is added with ＋ Add a model on Models', sample: 'press **＋ Add a reviewer**' },
  {
    pattern: /Try the new Settings page|Use the current page|settingsPreview/u,
    why: 'the preview switch and its setting went with the old page — there is one Settings page',
    sample: '**Try the new Settings page** at the top',
  },
  { pattern: /Chat other AIs/u, why: 'the old tab\'s name; the place is Chat', sample: 'in Settings → Chat other AIs' },
  { pattern: /Edit presets…/u, why: 'opened the Chat presets tab, which is gone; the prompt presets are drawn on Chat itself', sample: '**Edit presets…** beside it' },
  { pattern: /Which model answers/u, why: 'the old Chat tab\'s two-box model setting; Chat draws Which model a chat opens on', sample: '**Which model answers**' },
  { pattern: /Settings → Reviewers/u, why: 'the old Reviewers tab; every model is a card on Models', sample: 'in Settings → Reviewers' },
  // The three tabs E5.1 step 4 deleted, by their labels. Case-sensitive on purpose: the palette commands kept as redirects
  // are titled "Edit review roles" and "Edit chat presets", in lower case, and they are true (the code round, finding 5).
  // A question row that can read this machine is marked by a tick that is ON and cannot be taken off (D13, revised
  // 2026-10-03, `qconsultView.flagBlock`) — there is no tick a person gives to accept it (own review of E5.2).
  { pattern: /I accept that this row/u, why: 'no such tick: Can read this machine is drawn on and cannot be taken off', sample: 'the tick **I accept that this row can read this machine**' },
  { pattern: /Review roles|Gate commands|Chat presets/u, why: 'a tab E5.1 deleted; its editor is Reviews › Roles & prompts, Reviews › Commands or Chat', sample: 'the **Review roles** tab' },
];

/** Each language's own words around the labels. */
const OWN_WORDS: Readonly<Record<HelpLanguage, readonly Removed[]>> = {
  en: [
    { pattern: /Reviewers\*{0,2} tab/iu, why: 'the old Reviewers tab; it is Models', sample: 'The **Reviewers** tab of Settings' },
    { pattern: /reviewer cards?\b/iu, why: 'the old page\'s card; a model has a card on Models', sample: 'no reviewer card can say' },
    { pattern: /reviewer rows?\b/iu, why: 'a row of the old Reviewers tab; a model is a card on Models', sample: 'if a reviewer row says so' },
    { pattern: /reviewer list/iu, why: 'the old page\'s list of reviewers; the models are one catalog, drawn on Models', sample: 'into the reviewer list' },
    {
      pattern: /(roles|commands) page/iu,
      why: 'Edit roles… and Edit commands… open no page of their own — they jump to Reviews › Roles & prompts and Reviews › Commands',
      sample: 'opens the roles page',
    },
    { pattern: /presets tab|tab with both lists/iu, why: 'the Chat presets tab is gone; its lists are Models and Chat', sample: 'opens a tab with both lists in it' },
    { pattern: /new Settings page|current page/iu, why: 'there is one Settings page, so nothing is new or current', sample: '**The new Settings page** is a preview' },
  ],
  ru: [
    { pattern: /вкладк\p{L}* \*{0,2}Reviewers/iu, why: 'the old Reviewers tab; it is Models', sample: 'Вкладка **Reviewers** в Settings' },
    { pattern: /карточк\p{L}* (каждого )?ревьюер/iu, why: 'a reviewer card of the old page; a model has a card on Models', sample: 'ни одна карточка ревьюера' },
    { pattern: /строк\p{L}* ревьюер/iu, why: 'a row of the old Reviewers tab; a model is a card on Models', sample: 'если строка ревьюера об этом говорит' },
    { pattern: /список ревьюеров|списке ревьюеров/iu, why: 'the old page\'s list of reviewers; the models are one catalog on Models', sample: 'переносит их в список ревьюеров' },
    { pattern: /страниц\p{L}* (ролей|команд)/iu, why: 'Edit roles… / Edit commands… jump to a place of the Settings page, never a page of their own', sample: 'открывает страницу ролей' },
    { pattern: /вкладку с обоими списками/iu, why: 'the Chat presets tab is gone', sample: 'открывает вкладку с обоими списками' },
    { pattern: /нов\p{L}* страниц\p{L}* Settings|текущ\p{L}* страниц/iu, why: 'there is one Settings page', sample: '**Новая страница Settings** — предварительная версия' },
  ],
  uk: [
    { pattern: /вклад[кц]\p{L}* \*{0,2}Reviewers/iu, why: 'the old Reviewers tab; it is Models', sample: 'на вкладці Reviewers' },
    { pattern: /карт[кц]\p{L}* рецензент/iu, why: 'a reviewer card of the old page; a model has a card on Models', sample: 'жодна картка рецензента' },
    { pattern: /ряд(ок|к\p{L}*) (цього )?рецензент/iu, why: 'a row of the old Reviewers tab; a model is a card on Models', sample: 'рядки рецензентів' },
    { pattern: /спис(ок|ку) рецензентів/iu, why: 'the old page\'s list of reviewers; the models are one catalog on Models', sample: 'до списку рецензентів' },
    { pattern: /сторінк\p{L}* (ролей|команд)/iu, why: 'Edit roles… / Edit commands… jump to a place of the Settings page, never a page of their own', sample: 'відкриває сторінку команд' },
    { pattern: /вкладку з обома списками/iu, why: 'the Chat presets tab is gone', sample: 'відкриває вкладку з обома списками' },
    { pattern: /нов\p{L}* сторінк\p{L}* Settings|поточн\p{L}* сторінк|замість поточної/iu, why: 'there is one Settings page', sample: '**Нова сторінка Settings**' },
  ],
  de: [
    { pattern: /Tab \*{0,2}Reviewers|Reviewers-Tab/iu, why: 'the old Reviewers tab; it is Models', sample: 'Der Tab **Reviewers** der Settings' },
    { pattern: /Reviewer-Karte/iu, why: 'a reviewer card of the old page; a model has a card on Models', sample: 'keine Reviewer-Karte' },
    { pattern: /Reviewer-Zeile/iu, why: 'a row of the old Reviewers tab; a model is a card on Models', sample: 'wenn die Reviewer-Zeile es sagt' },
    { pattern: /Reviewer-Liste/iu, why: 'the old page\'s list of reviewers; the models are one catalog on Models', sample: 'in die Reviewer-Liste' },
    { pattern: /Seite der Befehle|Rollenseite/iu, why: 'Edit roles… / Edit commands… jump to a place of the Settings page, never a page of their own', sample: 'öffnet die Rollenseite' },
    { pattern: /Tab mit beiden Listen/iu, why: 'the Chat presets tab is gone', sample: 'öffnet einen Tab mit beiden Listen' },
    { pattern: /neue\p{L}* Settings-Seite|aktuelle\p{L}* Seite/iu, why: 'there is one Settings page', sample: '**Die neue Settings-Seite** ist eine Vorschau' },
  ],
  es: [
    { pattern: /pestaña \*{0,2}Reviewers/iu, why: 'the old Reviewers tab; it is Models', sample: 'La pestaña **Reviewers** de Settings' },
    { pattern: /tarjetas? de revisor/iu, why: 'a reviewer card of the old page; a model has a card on Models', sample: 'ninguna tarjeta de revisor' },
    { pattern: /filas? del? revisor/iu, why: 'a row of the old Reviewers tab; a model is a card on Models', sample: 'si la fila del revisor lo dice' },
    { pattern: /lista de revisores/iu, why: 'the old page\'s list of reviewers; the models are one catalog on Models', sample: 'a la lista de revisores' },
    { pattern: /página de (roles|órdenes)/iu, why: 'Edit roles… / Edit commands… jump to a place of the Settings page, never a page of their own', sample: 'abre la página de roles' },
    { pattern: /pestaña con ambas listas/iu, why: 'the Chat presets tab is gone', sample: 'abre una pestaña con ambas listas' },
    { pattern: /nueva página de Settings|página actual/iu, why: 'there is one Settings page', sample: '**La nueva página de Settings** es una vista previa' },
  ],
};

/** Everything one language's text is checked for. */
function removedIn(language: HelpLanguage): readonly Removed[] {
  return [...EVERY_LANGUAGE, ...OWN_WORDS[language]];
}

/** What a text names that the page no longer draws: the words found, and why each is wrong — empty when nothing. */
function namedRemoved(text: string, language: HelpLanguage): readonly string[] {
  return removedIn(language).flatMap((one) => {
    const found = one.pattern.exec(text);

    return found === null ? [] : [`«${found[0]}» — ${one.why}`];
  });
}

/** Every part of every article in one language that names removed UI, as `article.field: what — why`. */
function offendingArticles(language: HelpLanguage): readonly string[] {
  return HELP_ARTICLES.flatMap((article) => Object.entries(bodyFor(article, language).body)
    .flatMap(([field, text]) => namedRemoved(text, language).map((said) => `${article.id}.${field}: ${said}`)));
}

for (const language of HELP_LANGUAGES) {
  test(`the ${language} help names no control the Settings page no longer draws`, () => {
    const offending = offendingArticles(language);

    assert.ok(offending.length === 0, `the ${language} help still names the old Settings page:\n${offending.join('\n')}`);
  });
}

test('no tooltip names a control the Settings page no longer draws', () => {
  const offending = Object.entries(HELP).flatMap(([key, text]) => namedRemoved(text, 'en').map((said) => `${key}: ${said}`));

  assert.ok(offending.length === 0, `these tooltips still describe the old Settings page:\n${offending.join('\n')}`);
});

for (const language of HELP_LANGUAGES) {
  test(`the canary: the ${language} scan flags a body that names removed UI, so a clean pass is not a vacuous one`, () => {
    for (const one of removedIn(language)) {
      const fake = `Open Settings, then ${one.sample}, and carry on.`;

      // THIS entry flags its own sample — another entry matching it would hide a pattern that matches nothing (code round).
      assert.ok(one.pattern.test(fake), `the ${language} entry ${one.pattern.source} does not flag its own sample "${one.sample}"`);
      assert.ok(namedRemoved(fake, language).length > 0, `the ${language} scan did not flag "${one.sample}" (${one.pattern.source})`);
    }
  });
}

test('the canary: a sidebar section, and the Settings page as it is, are not flagged', () => {
  const asItIs = 'The **Phrases** section of the panel; **＋ Add a model** on Models; Reviews › Roles & prompts; **Edit roles…** jumps there; '
    + 'the palette\'s **Edit review roles** and **Edit chat presets** open their places.';

  for (const language of HELP_LANGUAGES) {
    assert.deepEqual(namedRemoved(asItIs, language), [], `the ${language} scan flags the page as it is`);
  }
});
