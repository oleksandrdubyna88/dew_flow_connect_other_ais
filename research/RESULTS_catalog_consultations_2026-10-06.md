# RESULTS — the catalog plan's owed consultations (2026-10-06)

> Status: **record, 2026-10-06; verification in progress on `feat/catalog-e4` (PR #688).** Two of the three
> consultations the cadence of [PLAN_one_model_catalog.md](../todo/PLAN_one_model_catalog.md) owed, run once the
> consultant was available again (codex `gpt-6-astra`, read-only over the checkout and its uncommitted diff). Every
> finding is advice until a test reproduces it; the column says which are verified.

## Cadence, epics 1–3 (consultation `62786772`, merged epics, asked after the fact)

| # | Finding (consultant) | Verified | Status |
|---|---|---|---|
| C1 | A side that keeps its own `vendors` but inherits the shared `consultants` loses the consultant: the user layer's migration makes `consult-other` a row in ITS vendors; the side's overlay has no such row, so the inherited reference names nothing there (`catalogMigrationHost.ts` side layer reads `sharedVendors`, not the shared definitions). | by code reading | open |
| C2 | A row's E2 options (effort, system prompt, timeout, key name) never reach a consultation: `COAI_CONSULTANTS` carries `{vendor, model, runtime, baseUrl, executablePath}` only (`settingsShape.ts` `wireEntry`), `ConsultantResolver.FromRow`/`AsProvider` rebuild only those, and a consultant-only row is not in `COAI_VENDORS` (E1.4). Contradicts E2.2's "review, consult, question consult". | by code reading | open |
| C3 | An explicitly empty `COAI_VENDORS="[]"` ran Codex and Antigravity — read as "not configured" (`PanelSettings.WithProvidersFrom`). | RED test `AnEmptyReviewerListIsEmptyTests` | fixed |
| C4 | Duplicate copied an ABSENT `vaultKeyName` and changed the id, so a copy of `grok` looked for key `grok-2` (`catalogCommands.duplicated`). | RED test in `catalogCommands.test.ts` | fixed |

## Risk, epic 4 — the chat move (consultation `1af5f386`)

| # | Finding (consultant) | Verified | Status |
|---|---|---|---|
| R1 | A conversation whose recorded row was removed resumes on another row offering the same model, unasked (`resumedPick` falls back to `legacyPick`). Should keep the unavailable identity and ask for a replacement. | RED test in `aResumedChatKeepsItsModel.test.ts` | fixed — the recorded row is kept and refused by name |
| R2 | Side id collision: the user preset moves to `chat-a`; the side already owns an unrelated `chat-a`, so its move makes `chat-a-2` — but the side inherits `chatModel`, which still selects the unrelated `chat-a`. | — | open |
| R3 | `adoptedRow` adopts a row with matching runtime/model/name but a different executable or endpoint; and after a collision made `chat-a-2`, an interrupted retry makes `chat-a-3`. | — | open |
| R4 | An interrupted MAIN move leaves a stale `chatModelName` (the remap repairs `chatModel` only). | RED test in `catalogChatStep.test.ts` | fixed — a remapped MAIN preset writes its model too |
| R5 | A legacy conversation whose `modelId` is a preset id resolves to `chat-a/sonnet`, but the restored thread keeps `modelId: 'a'` and the first turn asks for `chat-a/a` and is refused (`chatConversationRestore.ts`). | RED wiring test in `chatSourceWiring.test.ts` | fixed — the thread keeps the resolved model |
| R6 | Downgrade then upgrade loses a mixed-case preset from the chat: its row carries no `vaultKeyName` (not `normaliseId`-clean), so `lostItsUses` (which needs a foreign key name) never repairs its dropped `uses`. | RED test in `catalogChatStep.test.ts` | fixed — the record proves the row, no key name needed |
| R7 | On the open fingerprint question: a preset edited in an OLDER build after the move is a **conflicting revision of the same preset**, not a new model — keep the edited revision and surface the conflict; widening the fingerprint alone duplicates rows while `movedTo` still picks the first record. | — | design answer |

The consultant also checked a plain backup/restore round trip of the chat keys and found it sound.

## Seen on the way

`AReviewerThatAsksForSourceIsAskedAgainTests.AConversationThatOutlivesItsCap_IsOneTerminalTimeout_WithEveryTurnsUsageKept`
is flaky on its own: 1 of 4 runs failed on a `main`-based build, 1 of 3 on this branch — not caused by these fixes.

## Still owed

The cadence consultation for epics 4–5 (before epic 5 is built).
