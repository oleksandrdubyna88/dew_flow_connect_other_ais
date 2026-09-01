# PLAN — a gate per ROLE, prompts dealt across vendors, and no translator

> Status: **IMPLEMENTED, 2026-09-01.** Scope: `src_mcp/core/Rounds` (the gate, the
> catalog, the machine), `src_mcp/src/Server/PanelService.cs` (the fan-out), the panel's Prompts and
> Gate sections merged into one, and the removal of `src_mcp/runners/Translation`.
>
> Related docs: [module_server.md](module_server.md),
> [module_runners.md](module_runners.md),
> [module_extension.md](module_extension.md),
> [PLAN_conventions_pass.md](PLAN_conventions_pass.md).

## Three requests, one subsystem

The operator asked for three things on 2026-09-01, all of them about how a round is composed:

1. **Rounds and threshold per ROLE**, not per stage. Architecture 2 rounds, Security 3,
   Performance 1, each with its own threshold — and the Prompts and Gate sections merged, because
   they are then two halves of one setting.
2. **Prompts dealt across vendors instead of duplicated.** Every prompt runs, but each vendor runs a
   DIFFERENT one: if codex takes the universal plan prompt, antigravity does not. Assigned randomly.
   Two pools — the plan stage's prompts, and the code stage's — divided the same way. **One vendor
   means one vendor does all of it; there is no alternative there.**
3. **No translator.** The escalation is three buttons now, so there is no prose to translate.
   English only.

## What (2) trades away, stated before it is built

Today every vendor runs every role's prompt, so two vendors answer the same question and
`FindingDedup` merges what they agree on. **That agreement is the strongest signal this product
produces** — it is why the help says a finding raised by two vendors is worth more than either, and
why the threshold counts it once.

Dealing the prompts out removes it. In exchange: every lens gets used instead of one lens being
asked twice, and a round costs half as many launches. That is a real trade and the operator has made
it; this section exists so nobody later reads the change as an oversight. `FindingDedup` stays —
duplicate findings can still arrive from different lenses — but it will rarely have anything to
merge, and the help text must stop promising cross-vendor agreement.

## Build order

### 1. The gate becomes per role

`PanelConfig` currently holds `Plan` and `Code` as `StageGate`. Replace with a gate per role:

```csharp
public sealed record RoleGate(int MaxRounds, int Threshold);
public sealed record PanelConfig(IReadOnlyDictionary<string, RoleGate> Roles, StagePolicy OnExhausted)
{
    public RoleGate For(string role);          // the role's own numbers
    public RoleGate For(Stage stage);          // the widest budget of the stage's roles
}
```

- Defaults: plan 3/2; Architecture 2/3, Security 3/3, Performance 1/3 are the operator's example, so
  the shipped defaults stay 2/3 for all three code roles and the panel is where they diverge.
- **A round runs the roles that still have budget.** Round 3 of the code stage runs only the roles
  whose `MaxRounds >= 3`. This is the part that changes `RoundMachine`: the stage is finished when no
  role has both open findings over ITS threshold and rounds left.
- Per-role thresholds mean per-role gating counts: `GateRule.Evaluate` must group findings by the
  ROLE that raised them. `Finding` does not carry its role today — `ReviewerOutcome.Ok` knows it, so
  the merge must keep it. That is the one real data change.
- Env/settings: `COAI_ROUNDS_<ROLE>` and `COAI_THRESHOLD_<ROLE>`; the per-stage keys stay as the
  default for every role of that stage, and the legacy single keys stay under those.

### 2. Prompts dealt, not duplicated

A round's work list is built as `(role, prompt)` ITEMS first, then assigned:

- **Plan stage.** The items are the plan role's prompts that this session has not used yet. Deal one
  to each enabled vendor. Three prompts and two vendors: round 1 runs two, round 2 runs the third.
- **Code stage.** The items are one per role with budget this round — three at most. Deal them
  across the vendors, balanced: two vendors and three items means one vendor takes two.
- **Assignment is random, and recorded.** `RoundAudit` already logs each reviewer's prompt at
  Information; with a deal it must also log the SEED, or a round cannot be reproduced. A seed per
  round, stored in the round record.
- **One vendor takes everything.** With a single enabled vendor the deal is the identity, and the
  work list is exactly what it is today.
- An explicit per-round prompt choice still wins: a chosen prompt is an item like any other, it just
  cannot be dealt to two vendors.

### 3. The translator goes

- Delete `runners/Translation`, `ITranslator`, `CliTranslator`, `TranslationPrompt`, the
  `Translator` settings, `COAI_TRANSLATOR_*`, and the Language section's translator rows.
- `Language` itself stays only if something still reads it; the escalation question is now built
  from three fixed English labels, so it probably does not. Check before deleting.
- The help's Language article becomes a paragraph saying the questions are English, and the five-
  language HELP catalog is untouched — that is the help's own language, not the reviewers'.

## Test plan

- `RoleGate`: each role reads its own numbers; a round runs only roles with budget; a stage ends when
  every role is either under its threshold or out of rounds; the legacy and per-stage keys still fill
  in. RED first.
- `GateRule` grouping: two findings from Architecture and one from Security, thresholds 1 and 0 —
  Architecture passes and Security does not, and the stage does not pass.
- The deal: no prompt is assigned to two vendors in one round; every prompt in the pool is used
  across the stage's rounds; one vendor gets everything; the same seed produces the same assignment.
- The removal: no reference to `COAI_TRANSLATOR_*` or `ITranslator` survives, and an escalation
  question is still delivered with its findings.
- Panel: one merged section per role showing its rounds, its threshold and its per-round prompts; the
  arithmetic sentence recomputed for a DEALT round (three items over two vendors is three launches,
  not six).

## Definition of Done

- [ ] Rounds and threshold are per role, with the panel showing them beside that role's prompts.
- [ ] A code round runs only the roles that still have budget.
- [ ] Findings are counted against the threshold of the role that raised them.
- [ ] Prompts are dealt across vendors, no prompt twice in a round, seed recorded, one vendor takes
      everything.
- [ ] The translator is gone, root and branch, and nothing reads its settings.
- [ ] The help stops promising cross-vendor agreement and says what replaced it.
- [ ] Every rule above has a test that was watched fail first; both suites green.
- [ ] `research/module_*.md` updated; this plan promoted.

## What shipped differently

Four things the plan did not foresee, each found by building it:

1. **`CountVar` had to exist.** `IntVar` requires a positive number, which is right for rounds and
   wrong for a threshold — the panel had always accepted zero and had a test saying so, while the
   server silently substituted its default. A number a person deliberately set to nothing was the one
   value the two halves could not agree on.
2. **`Finding.Role` is stamped in `PanelService`, not carried by the reviewer.** It is the only place
   holding both the invocation and its answer, and a role a reviewer reports about itself is a value
   the gate would be trusting the reviewed party to supply.
3. **`StableSeed` is FNV, not `string.GetHashCode`.** The latter is randomised per process, so a
   restart would deal a different hand while the log named a seed nobody could reproduce.
4. **Dealing is opt-in behind two switches, not the new default.** The first build made it
   unconditional; that was wrong, and the correction came from the person who asked for the feature:
   *"это только если стоит галочка ротейт. без нее все работает как было."* Off, every vendor answers
   every question and agreement between two vendors stays available to the gate — which is the
   strongest signal this product produces, and not something to spend by default.

A fifth thing was found AFTER the plan was complete, by the pre-delivery campaign: the panel's prompt
picker was passing the dealing switch into the server mirror's rotation slot, so it displayed prompts
the server would not run. Rotation is now removed from both halves — see
[RESULTS_predelivery_campaign.md](RESULTS_predelivery_campaign.md) §4 and
[module_server.md](module_server.md).
