# PLAN — a fresh install starts with seven useful phrases

> Status: **IMPLEMENTED, 2026-10-08** (branch `feat/default-phrases`; no release yet — a release needs the operator's
> OK). Scope: the `coai.phrases` default in `src_vs_code/package.json`, one test file, the Phrases help section in five
> languages, docs.
>
> Deviations: the plan round's two findings were accepted — the docs say the defaults reach anyone WITHOUT a saved
> value (existing users who never saved phrases too, which is the operator's rule), and an extension-host scenario
> (`host/defaultPhrasesScenario.ts`) does the round trip through real configuration layers instead of the planned
> render test; the README's Phrases bullet was updated too. The code round's English-only finding was rejected: the
> help is localized by design. Open tail: none. Seen on the way, not caused by this change: the host scenario *a
> deletion the mirror carries takes the prompt text with it* failed once in four runs (19/19 twice after, 18/18 without
> the change) — a timing flake in that scenario.
>
> Related docs: [module_extension.md](module_extension.md).

## 1. The symptom

A person who just installed the extension opens the panel and the **Phrases** section is empty: one *Edit phrases…*
button and a sentence. Nothing shows what a phrase is for, so people do not understand how to use it (the operator,
2026-10-08). The operator's own seven phrases are the best example of the feature there is — the sentences a person
types into Claude Code again and again.

## 2. The operator's decision (2026-10-08)

- A new install gets the operator's seven phrases, **translated to English**.
- **If a person already has something, it is not touched** ("если у людей что-то есть — тогда не трогаем").

## 3. The design

**The declared default, not a write.** `coai.phrases` is declared in `package.json` with `"default": []`
(`src_vs_code/package.json:676-678`), and every reader goes through `getConfiguration('coai').get('phrases')`
(`panelProvider.ts:2956`, `phrasesPanel.ts:60`). VS Code returns the declared default only when no scope holds a value.
So changing the default to the seven phrases gives exactly the operator's rule, with no code that writes:

| The person's `settings.json` | What they see |
|---|---|
| no `coai.phrases` at all (a new install, or never used) | the seven phrases |
| their own phrases | their own phrases, unchanged |
| `[]` — they removed every phrase | nothing; the defaults do not come back |

The third row holds because the phrases tab saves through `saveSetting` → `config.update(key, rows, Global)`
(`sideConfig.ts:184`), which writes the empty array as a value. Editing a default phrase writes the whole list (the
six or seven rows) to the person's settings for the first time; from then on it is theirs.

Rejected: seeding on activation (write the seven rows when the setting is empty). It writes into every user's
settings, cannot tell "never had any" from "removed them all" once `[]` is the default, and races between two windows.

**The seven phrases**, each with a stable id (`phrase-default-…`) so a colour and a click stay with it:

| Name (button) | Text |
|---|---|
| PR, CodeRabbit, deploy | Create the PR, read the PR comments and fix them, then deploy and release (if there is something to release), autonomously. |
| What problem are we solving? | Remind me what problem we are solving, where we are now, and what is left to do. |
| Questions, suggestions, plan | Review it again. If you have questions, ask them. If you find something that can be improved or fixed, say so. The result must be a plan of the highest quality. |
| Continue | Continue, following all the development rules, the coai gates and best practices, autonomously. |
| All done? | Is everything I asked you to do implemented, are the PRs created and merged, and is the deploy (release) done? |
| Consultant | Discuss it with the consultant. |
| Progress? | Tell me about the progress, measured against what I asked you to do: what is done and what is left, in percent and in approximate hours. (newline) If there are open questions, highlight them. (newline) After the answer, continue what you were doing, autonomously. |

**The help.** The Phrases help section gets one sentence: a new install starts with seven example phrases; edit or
remove them, and a list you emptied stays empty. In all five languages (en, de, es, ru, uk), so no translation is
left behind the English.

## 4. Build order

1. RED: `defaultPhrases.test.ts` reads the manifest's default and fails on today's `[]`.
2. GREEN: the default in `package.json`; the setting's `markdownDescription` says new installs start with examples.
3. The help sentence in five languages.
4. Docs: `research/module_extension.md` (the Phrases section), `research/module_tests.md`.
5. The whole extension suite, lint, the code round, PR. A release only with the operator's OK.

## 5. Test plan

- The manifest default, read through `phrasesFrom`, keeps all seven rows — every text kept verbatim, every name within
  `NAME_LIMIT`, every id unique and starting `phrase-default-`.
- The texts are the table above, word for word (a test pins them, so a change is a decision).
- Removing the last phrase from a list yields `rows: []` (not "unchanged"), so the tab writes an empty list and the
  defaults cannot come back.
- The panel's Phrases section renders the default list as seven buttons when the setting is unset.

## 6. Definition of Done

- [x] RED → GREEN for the default, with the test seen failing on `[]`.
- [x] A person with phrases, or with an emptied list, sees no change (by the test of the removal rule and the design).
- [x] Help in five languages; `module_extension.md`, `module_tests.md` updated; this plan promoted.
- [x] Whole extension suite (5758 passed, 0 failed) and lint green; host scenarios 19/19; gate rounds resolved.
- [x] PR merged — #710, 2026-10-08 17:10Z (`9f87dade`).
