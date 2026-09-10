# PLAN — a translation that is BEHIND is marked the way a missing one already is

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/helpContent.ts`,
> `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts`, `helpPage.ts`, and one new test.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md).

## The symptom

The help catalog already has one honest failure mode and one silent one, and they look the same from
the outside.

**Honest.** A translation that is MISSING falls back to English with a visible note. That is
`bodyFor` in [helpContent.ts](../src_vs_code/src/helpContent.ts), line 65 — the key is absent,
the English body is returned, `fallback: true`, and the page says so. Nobody is shown a blank article and
nobody is shown a stale one, because there is nothing to be stale.

**Silent.** A translation that EXISTS but was written against an older English body is returned with
`fallback: false` and no note at all. Nothing compares the two. A reader in their own language is
shown a confident, well-formed article describing a product that changed underneath it.

That is not hypothetical. It was found by an audit on 2026-09-10, and it had been true for a day
across four languages at once:

| | what the article said |
|---|---|
| English | the picker, the presets, the re-ask, the picture, the running total, the Stop |
| Русский, Українська, Deutsch, Español | *"which model answers"* is a setting — and nothing else |

Five features had shipped into that tab. A person reading the help in Russian was told about none of
them, and was told nothing was missing. The English article had itself been a version behind for
part of that day, so the same hole caught the floor language too — but there at least the next
writer could see it, because English is where the change lands.

## Why the existing guards do not catch it

`helpCoverage.test.ts` is a good test and it is aimed elsewhere:

- *every command is described in the help* — checks the ENGLISH corpus. A translated file could be
  empty of a command's name and pass.
- *every article exists in every language the switch offers* — checks the KEY, not the text. This is
  exactly the check that says a stale translation is fine.
- *a translation is a translation, not the English text pasted across* — checks that the text
  DIFFERS from English, which a stale translation does, emphatically.

So the suite currently rewards the failure: the more out of date a translation is, the more
confidently it passes the one test that looks at its words.

## The goal

**A translation that is behind must be as visible as one that is missing** — to the reader, and to
the commit that made it behind.

Both halves matter, and the second is the one that actually keeps the help alive. A note on the page
tells a reader to distrust what they are reading; a red test tells the person CHANGING the English
article that four other files now need a pass, on the commit where they still remember what they
changed and why.

## The approach

Each translated body records the English body it was made from, as a short digest. Nothing records a
date, a version or a revision number: those are all promises a person has to keep, and the record
here should be derived from the text itself so it cannot drift by inattention.

1. **`digestOf(body: HelpBody): string`** in `helpContent.ts` — a stable short hash (8 hex from
   `node:crypto`) over the five fields of an English body, joined by a separator that cannot occur
   in the text. Pure, exported, tested for stability.

2. **A stamp map per language module.** Beside `RU`, a `RU_FROM: Readonly<Record<string, string>>`
   mapping article id → the digest of the English body that translation was made from. It lives in
   the SAME file as the text it describes, on purpose: re-stamping without translating then shows up
   in a diff as a changed hash beside unchanged prose, which is a thing a reviewer can see.

3. **`bodyFor` gains a third outcome.** `{ body, fallback, stale }` — `stale: true` when the article
   has a translation whose stamp does not match `digestOf(article.en)`. The body returned is still
   the TRANSLATION, not English: an article a version behind is more useful to somebody who does not
   read English than a correct article they cannot read. What changes is that they are told.

4. **The page says so**, next to where it already says an article fell back to English. Same
   treatment, different sentence: *this translation was written for an earlier version of this
   article — the English one is current.*

5. **The test that makes it a rule.** For every language and every article, the recorded stamp must
   equal the current English digest. The failure names the language and the article id, and says the
   two ways out: translate the article again, or — when the English change genuinely did not alter
   meaning — re-stamp deliberately.

6. **`npm run help:stamp`** — a script that rewrites every stamp map from the current English. It is
   the tool that makes step 5 cheap, and it is also the tool that can defeat it, which is why the
   stamps sit beside the prose rather than in a generated file nobody reads.

### What was considered and rejected

- **A revision integer per article, bumped by hand.** It is one more thing to remember at exactly
  the moment everyone forgets: the day the English text changes. The whole point is a check that
  does not depend on the person who changed the text noticing.
- **Translating in CI with a model.** The extension already has a translator for questions, so this
  is technically reachable — and it is a different plan. Machine-translating the help without a
  person reading it would replace a visible staleness with an invisible one.
- **Failing the build only for the floor language.** English is the one language that cannot go
  stale unnoticed, because it is where a change lands.

## Build order

1. `digestOf` + its test (stability, and that a changed field changes the digest).
2. The stamp maps, generated once by the script, from the current English — the article this audit
   brought up to date is already current in all five languages, so the first stamping starts clean.
3. `bodyFor`'s third outcome + its tests, including that a stale body is still the TRANSLATION.
4. The page's note.
5. The parity test, and `npm run help:stamp`.

## Test plan

- `digestOf` is stable across runs and changes when any field changes.
- A stamp that matches → `stale: false`; a stamp that does not → `stale: true`, and the body is the
  translated one.
- A missing translation still returns English with `fallback: true` and NOT `stale` — the two
  outcomes must not be conflated, because the advice to the reader differs.
- The parity test fails, with the language and the article named, when an English body is edited and
  nothing else is.
- The page renders one note, not two, when an article is both fallback and (impossibly) stale.

## Definition of Done

- [ ] Editing an English article and running the suite fails, naming every language now behind.
- [ ] A reader of a stale article sees a note saying so, and still sees their own language.
- [ ] `npm run help:stamp` re-stamps, and the diff it produces is readable as *stamped, not
      translated*.
- [ ] `helpCoverage.test.ts` keeps every check it has — this adds a check, it replaces none.
- [ ] The module doc records the mechanism, and this plan is promoted with what shipped differently.
