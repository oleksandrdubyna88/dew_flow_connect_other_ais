# PLAN — a role is not switched on without a question to ask

> Status: **plan only, nothing implemented yet.** Scope: the Edit-roles rules and page in `src_vs_code`
> (`roles.ts`, `rolesEdit.ts`, `rolesPanel.ts`, `rolesPage.ts`) and one sentence in `src_mcp`
> (`PanelService.cs`). Issue #338.
>
> Related docs: [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md),
> [architecture.md](../research/architecture.md).

## The symptom

Issue #338: on 2026-09-16 six gate rounds each ended *"Role2 was not asked: its prompt 'role2-general' has
no text — write it at …\prompts\role2-general.md"*. The role was enabled, counted in the catalogue and in
every session snapshot, and never produced a reviewer. The only signal was one line in a round reply,
naming a generated id and a file path rather than the role the person had named.

## Why it happens (read from the code)

- **Every new role is created with a prompt that has no text, and switched on when there is room.**
  `added()` (`src_vs_code/src/rolesEdit.ts:87-100`) stores `{…, active: room, prompts: [{id: promptId,
  label: 'General', purpose: ''}]}`; no prompt file is written. The text is a FILE,
  `<dataDir>/prompts/<id>.md` (`rolesPrompts.ts:38-54`), written only by `writeText`
  (`rolesPanel.ts:472-497`), which deletes the file for an empty body.
- **The id is `Role2` for every first role**, not because of the script of the name: `added` calls
  `idFor('', …)` before any name exists (`rolesEdit.ts:93`, `roles.ts:246-273`), and a rename keeps the id.
- **The server skips it, and says so by id.** `PanelService.cs:1912-1934`: `!_prompts.Has(choice)` →
  `Skip(role, "its prompt '…' has no text — write it at …")`, rendered `"{role} was not asked: {reason}"`
  (`PanelService.cs:199`, `SessionState.cs:330`). `RolePrompts.Has` is true for a shipped prompt and for
  an override file with non-blank text (`RolePrompts.cs:47-53`). The prompt a round asks is the FIRST of
  the role's prompts unless a per-round choice says otherwise (`RoleCatalog.cs:194-221`).
- **Nothing on the page says any of it.** The page has no Save button — everything is saved as it is
  typed (`rolesPage.ts:509`) — so "refuse to save" in its literal form has nowhere to happen.

## The design — the issue's three options, as this code can take them

1. **A new role is created switched OFF**, always. It has no question to ask yet, so it cannot be a
   reviewer yet. (The comment at `rolesEdit.ts:80-86` already argues that a role created switched off is
   one a person can see is switched off.)
2. **Switching a role ON is refused while the prompt it would be asked has no text** — the issue's option
   (1), at the only moment the page has for it. A pure `whyNotAskable(row, texts)` in `roles.ts` answers
   `''` for a role this product ships (its prompts are embedded) and, for a person's own role, names it BY
   ITS NAME: *"“{name}” cannot be switched on yet: its prompt “{label}” has no text. Write the question it
   asks in the box under it first."* `rowsAfter` gains a parameter carrying that answer, in the shape of
   its existing `reserved` (`rolesEdit.ts:50-58`); `switched()` refuses with it. The host
   (`rolesPanel.ts` `store`) reads the texts the way `texts()` already does, only for a `set active on`.
   Blank text counts as none, as it does on the server (`RolePrompts.cs:47-51`). A role of one's own
   with NO prompt at all is unaskable too, and says so rather than reading a label that is not there
   (gemini, the plan round).
3. **The page marks an active role that cannot be asked** — the issue's option (3), for the ways (2)
   cannot see: a text erased after the role was switched on, a settings file edited by hand, a prompt file
   deleted. A hint in the role's block (`rolesPage.ts` `roleBlock`, beside the existing hints), by name:
   *"“{name}” is switched on but will not be asked: its prompt “{label}” has no text."* Both sentences are
   about the FIRST prompt — the one every round without a per-round choice asks — and say so; a per-round
   choice of another empty prompt is the server's sentence to give, and is out of scope (codex, the plan
   round).
4. **The server names the role too.** The skip reason gains the role's name when it has one
   (`catalog.ById(role)?.Name`): *"Role2 was not asked: “My role” has a prompt 'role2-general' with no text
   — write it at …"* — the id stays first because it is the key a person may search for.

The issue's option (2), writing a template file on save, is **not** taken: a template becomes a real
question a reviewer is paid to answer, and `RolePrompts.Has` treats blank text as absent precisely so
that a reviewer is never launched with nothing to ask. An honest refusal beats a reviewer asked a
placeholder.

## What is deliberately NOT here

- **`lastStanding` counting an unaskable role as a reviewer** (`rolesEdit.ts:217-219`) — a stage could be
  left with only an unaskable role. Real, and a separate rule change; recorded as the open tail.
- **Removing the last prompt of a person's role** is not refused today either (`promptRemoved`); the
  server drops such a row. Out of scope.
- **A per-round prompt choice pointing at a different, empty prompt** (`promptsPerRound`) — the check here
  is on the first prompt, which is what every round without a choice asks.

## Build order

1. `whyNotAskable` in `roles.ts` + tests (a shipped role, a role of one's own with text, blank text, no
   text, no prompts; the sentence names the role and the prompt by name).
2. `added()` switched off; `switched()` refuses via the new `rowsAfter` parameter + tests in
   `rolesEdit.test.ts` (the existing *"adding into a stage with room stores it switched on"* is the
   guarantee this change reverses, rewritten to the new one).
3. `rolesPanel.ts` `store` passes the answer for an activation; `texts()` reuse, no second reader.
4. `roleBlock` hint, tested by RUNNING the page (`rolesPageHarness.ts` `runRolesPage`), not by reading
   its source (gemini, the plan round): shown for an active unaskable role, absent when it has text or is
   off.
5. Server: the reason names the role + `TheRoundSaysWhatItCouldNotAskTests` (red first).
6. Docs: `research/module_extension.md`, `research/module_server.md`, `research/module_tests.md` (the flow
   and what it does NOT prove: no scenario runs a real round with a role a person added — the extension half
   and the server half are each tested, the seam between them is the settings mirror, already covered),
   CHANGELOG `## Unreleased`.

## Test plan

- `cd src_vs_code && npm test`, `npm run lint`; `dotnet build`, `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`.
- Every changed behaviour red first, then green, then proved by breaking it.

## Definition of Done

- [ ] A new role is created switched off.
- [ ] Switching on a role whose asked prompt has no text is refused, naming the role by its name.
- [ ] An active role that cannot be asked is marked on the page, by name.
- [ ] The server's skip sentence names the role.
- [ ] Tests for every step; both suites and lint green.
- [ ] Docs updated; this plan promoted to `research/`.
