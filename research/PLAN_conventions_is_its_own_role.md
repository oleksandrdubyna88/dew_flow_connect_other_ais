# PLAN — Conventions is a role, not a prompt the other roles can be given

> Status: **IMPLEMENTED, 2026-09-08.** Scope: `src_vs_code/src/prompts.ts`,
> `panelView.ts`, `settingsShape.ts`, `package.json` and their tests; `src_mcp/core/Rounds`
> (`PromptCatalog`, the role list) and `src_mcp/src/Server/PanelService.cs`.
>
> Related docs: [PLAN_conventions_pass.md](PLAN_conventions_pass.md) — this supersedes
> its shape, [module_extension.md](module_extension.md),
> [module_server.md](module_server.md).

## What the operator asked for

> вот тут я хочу добавить еще одну секцию. назовем конвеншинс. там будет только проверка
> конвеншинс. а выбор конвеншинс в других дропдаунах убрать.

A fourth box in the code stage, called **Conventions**, that does the conventions check and nothing
else — and `Conventions` disappears from the other three roles' dropdowns.

## Why this is better than what it replaces, in one sentence

The conventions pass has been a PROMPT that a code role could be given, plus a rule that made it
round 1's default — first for all three roles, then (2026-09-07) for Architecture alone. Both
shapes had the same flaw: the pass had no budget of its own. It competed for a role's rounds, so
giving it a round meant taking one away from architecture or security, and its findings were counted
against that role's threshold. As a role it has its own rounds, its own threshold and its own place
in the fan-out, which is what it always behaved like.

## The design

**1. `Conventions` becomes a role**, `stage: 'code'`, listed FIRST among the code roles — a broken
written rule is the cheapest finding to act on and the least arguable, which is the reason it was
given round 1 in the first place.

**2. Its prompt list is the conventions prompt alone**, marked `universal: true` for that role so
`universalFor` answers with it. One prompt means the picker has nothing to choose; render it, but
there is nothing to decide.

**3. `conventions` is removed from the other three roles' prompt lists.** That is the operator's
second sentence, and it is what makes the new role meaningful rather than a duplicate.

**4. The round-1 special case DIES in both halves.** `selectedFor` (TS) and
`PromptCatalog.ForRound` (C#) each carry an `if (hasRules && round == 1 && role == Architecture)`
branch. With a role of its own there is nothing to substitute, and both functions go back to "what
was chosen, else this role's universal prompt". Two programs get simpler at once, and
`panelServerPromptAgreement.test.ts` — the hand transcription that holds them level — shrinks with
them.

**5. A repository with NO written rules must not run this role.** The old design refused the pass
when there was nothing to judge, on the grounds that it would invent a standard, and that reasoning
survives the reshaping. `PanelService.cs:374` already receives `rules.HasRules`; when it is false
the Conventions reviewers are not launched and are NAMED as skipped, with the reason, through the
same machinery 0.31.5 added for a reviewer that could not run. A silently absent role is a round
that quietly reviewed less than it said.

**6. Architecture drops to ONE round.** This one changes a shipped default and is worth objecting
to if it is wrong: Architecture has two rounds today *because* round 1 was the conventions pass and
round 2 was its own question. Take the conventions round away and the second is the only real one it
had. Its threshold stays at 5.

**7. Defaults for the new role:** 1 round, threshold 5 — the shape the other code roles now have.

## Build order

1. `prompts.ts`: the role, its prompt list, the removals, and `selectedFor` without the branch.
2. `PromptCatalog.cs`: the same, in the same words — role constant, prompt rows, `ForRound`.
3. `settingsShape.ts` + `package.json`: defaults for four code roles, Architecture at 1.
4. `panelView.ts`: `ROLE_TONE` and a colour for the new role; the fan-out sentence counts four
   roles; the code-stage hint no longer explains a round-1 default that no longer exists.
5. `PanelService.cs`: no rules → the Conventions reviewers are skipped and named.
6. Tests on both sides, then the docs.

## Test plan

- `panelServerPromptAgreement.test.ts` over all FIVE roles: an unset round is that role's universal
  prompt, with no round-1 exception anywhere.
- The other three roles do not offer `conventions` — asserted per role, since a leftover row in one
  of them is exactly the failure this change is about.
- The Conventions role offers exactly one prompt.
- C#: `ForRound` has no `hasRules` behaviour left; the conventions prompt belongs to the Conventions
  role only.
- A round in a repository with no rules names the Conventions reviewers as skipped rather than
  running them or omitting them silently.
- `settingsShape.test.ts`: the four code roles' defaults, and the manifest agreeing.

## What shipped differently

**The skipped role is logged, not yet named to the calling AI.** The plan asked for the reason to
reach both the log and what is handed back, reusing the machinery 0.31.5 added for a reviewer that
could not run. What is built writes a warning naming the round and the four file names it looked
for. Reaching the AI's own answer is the honest remaining half, and it is small.

**The fan-out sentence had to be derived, not edited.** It multiplied by a literal `3` beside a
hardcoded list of the same three roles; adding a fourth made it say "3 roles" under four boxes. Both
numbers come from `ROLES` now, so the next role changes nothing.

**Two tests were asserting a default where they meant an invariant** and went red for the wrong
reason: the panel's role-id check spelled the four role names, and the arithmetic test spelled the
count. Both read the role list now.

**`Conventions` took yellow** in the tone palette. Four code roles crowd blue-orange-green
otherwise, and yellow reads as "check this first", which is what the role is for.

## Definition of Done

- [x] A **Conventions** box in the code stage, with its own rounds and threshold.
- [x] `Conventions` appears in no other role's dropdown.
- [x] Neither half has a round-1 special case left.
- [~] A repository with no written rules gets a SKIP with the reason in the log. Naming it in what
      the calling AI receives is the open half.
- [x] `research/PLAN_conventions_pass.md` records that its shape was superseded, and the module docs
      say what exists now.
- [ ] Released as one pair: the panel and `coai-mcp` decide roles together.
