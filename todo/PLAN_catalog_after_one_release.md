# PLAN — what the model catalog keeps for one release, removed after it

> Status: **plan only, nothing implemented yet (2026-10-09).** Scope: `src_vs_code` — the three redirect commands, the
> restore command and the migration backup the catalog kept "for one more release" (T5). Extracted from
> [PLAN_one_model_catalog.md](../research/PLAN_one_model_catalog.md) when it was promoted after extension 0.65.0.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## Goal

Extension 0.65.0 (2026-10-09) made the catalog page the only Settings page and kept, for one release, what an upgrading
person might still reach for. After the NEXT extension release has shipped, these go:

1. **The redirect commands** `coai.editRoles`, `coai.editCommands`, `coai.editChatPresets` (`editorRedirects.ts`, their
   manifest entries and help aliases) — a keybinding or a habit has had one release to move to the Settings page.
2. **The restore command** *ConnectOtherAIs: Restore settings from before the catalog* and the backup it reads
   (`coai.migratedFrom`), per the plan's T5 — only once no supported extension version can need to go back through it.
3. **Decide, with the operator, about the frozen chat preset key** (`coai.chatModelPresets`): it is kept unrewritten so an
   older extension still reads the chat models it knew. Removing it ends that downgrade path.

## Not this plan's

- The feature review of the catalog plan did not run (no model ticked for features); it runs when the operator ticks a
  model — the planned pair is Fugu Max and Grok, waiting with the xAI work.
- The Models card's prices for non-reviewer rows: [PLAN_models_card_prices_every_row.md](PLAN_models_card_prices_every_row.md).

## Build order

1. Confirm the next extension release after 0.65.0 has shipped (the gate for starting).
2. RED: a test that the manifest declares none of the three commands, and that the help names none of them.
3. Remove, with every test that held them moved or deleted (was → now), docs and CHANGELOG.

## Test plan

- The guard tests above; `npm test`, eslint, the family checks, `test:host` (the activation scenario lists the commands).

## Definition of Done

- [ ] The next release after 0.65.0 has shipped before any removal.
- [ ] The three redirects, the restore command and `coai.migratedFrom` are gone, or the operator decided otherwise.
- [ ] Docs, help and CHANGELOG updated; this plan promoted.
