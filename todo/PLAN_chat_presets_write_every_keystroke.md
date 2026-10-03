# PLAN — the chat presets tab writes the settings on every keystroke

> Status: **plan only, nothing implemented yet, 2026-10-03.** Scope: `src_vs_code/src/chatPresetsPage.ts`,
> `chatPresetsPanel.ts`, tests. Found while surveying the webviews for
> [PLAN_busy_marks_on_every_webview.md](PLAN_busy_marks_on_every_webview.md) (its §3).
>
> Related docs: [module_extension.md](../research/module_extension.md).

## 1. Symptom

The chat presets page posts `edit` on every `input` event (`chatPresetsPage.ts:216-226`), and its host applies each
one straight to `config().update` (`chatPresetsPanel.ts:225`, `apply` at `:115-163`) with no settling and no
serialisation. Typing a twenty-character preset name is twenty settings writes, and two of them can race. The roles,
phrases and commands tabs all settle typing for 300 ms through `settledWrites`.

## 2. Goal

Typing in a preset field is stored once, when the person stops, and writes land one at a time and in order — the same
two rules the other tabs keep, through the same module.

## 3. Design

Route the chat presets host through `settledWrites` (`fieldOf` keys a typed field by preset and field; a structural
command goes straight through), exactly as `phrasesPanel.ts:154` does. No new queue.

## 4. Build order

1. RED: a host-shaped value test — twenty `edit`s for one field apply once after the settle; an `add` drains the typing
   first; writes do not overlap.
2. GREEN: the panel through `settledWrites`.
3. Docs; `npm test`; lint.

## 5. Test plan

| Test | Proves |
|---|---|
| a new `chatPresetsWrites.test.ts` | one write per settled field, in order, typing drained before a structural change |

## 6. Definition of Done

- [ ] Plan and code gate passed; tests RED first; `npm test` green; docs updated; this plan promoted.
