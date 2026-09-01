# RESULTS — the pre-delivery campaign: two plans, two commits, eleven runs

> Status: **run 2026-09-01**, against the Release build of `coai-mcp` in WSL (Ubuntu 26.04), driven
> over stdio by a harness in the session scratchpad. Vendors: `codex` (native Linux, `~/.npm-global`)
> and `antigravity` (`agy`, Google's own `install.sh`). Every number below is from a real vendor run,
> not a stub.
>
> Related: [module_server.md](module_server.md), [module_extension.md](module_extension.md),
> [RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md).

## Why it exists

Before delivery: two plans reviewed under different settings, then two real commits reviewed under
five settings combinations. The point was not to grade the reviewers — it was to find out whether the
per-role gate, the dealt prompts and the fourth exhausted-rounds answer behave the way the panel says
they do, with real CLIs on a real branch.

It found one defect nothing else had (§4). It also failed to exercise two paths it was designed for,
and §3 says so plainly rather than counting them as passes.

## 1. The plan stage

| cell | rounds / threshold | dealing | verdict | gating | reviewers | wall |
|---|---|---|---|---|---|---|
| `plan-a-strict` | 1 / 0 | off | **`call_human`** | 6 | 2 of 2 | 42.1 s |
| `plan-b-dealt` | 1 / 9 | **on** | **`proceed`** | 9 | 2 of 2 | 35.8 s |

Both behaved as specified. The strict cell exhausted its single round with findings over a threshold
of zero and escalated, which is the shipped default policy. The dealt cell is the one worth reading
the log for: the two vendors were given **different lenses** (`plan-critique` and `plan-assumptions`)
where the strict cell gave both the same one. That is the dealing feature, observed rather than
inferred.

## 2. The code stage

Two branches, both real: `review/pr-money` (base `34152dc`, the per-vendor money work) and
`review/pr-wsl` (base `0b43097`, the OS-aware install work). Every cell passed a plan round first,
because `review_code` refuses without one.

| cell | what it varies | verdict | gating | threshold | reviewers | wall | tokens in/out |
|---|---|---|---|---|---|---|---|
| `code-1-default` | nothing — the shipped defaults | `proceed` | 0 | 3 | **6 of 6** | 107 s | 416 k / 23 k |
| `code-2-dealt` | `COAI_DEAL_CODE=true` | `proceed` | 1 | 3 | **3 of 3** | 63 s | 163 k / 8 k |
| `code-3-perrole` | arch 2 rounds, sec 1, uxdx 1 | `proceed` | 3 | 3 | 6 of 6 | 140 s | 579 k / 34 k |
| `code-4-zero-sec` | security threshold 0, others 99 | `proceed` | 0 | 99 | 6 of 6 | 98 s | 318 k / 27 k |
| `code-5-goodenough` | all thresholds 0, policy `good_enough` | **`good_enough`** | 1 | 0 | 6 of 6 | 117 s | 500 k / 30 k |

**Dealing halves a round, exactly as documented.** `code-2` launched three reviewers where
`code-1` launched six, on the same commit, and cost 39 % of the tokens for 59 % of the wall clock.
This is the trade the switch is off by default for: with six launches, two vendors filing the same
finding is a fact `FindingDedup` can use; with three, nobody agrees with anybody.

**The fourth answer works end to end.** `code-5` is the first observed `good_enough` verdict: one
gating finding, a threshold of zero it could never clear, one round, and the policy answering
"read what is open, apply what is true, move on" instead of stopping.

## 3. What the campaign did NOT establish

Two cells were designed to exercise a path and did not reach it. Recording that is the point of
recording anything.

- **`code-4-zero-sec` never tested its zero threshold.** The intent was "security gates on anything";
  the security reviewers found nothing that round, so the gate passed on an empty set. The answer
  (`proceed`, `threshold: 99` — the widest role's) is correct, and it is not evidence about a
  threshold of zero. The zero-threshold path is covered by `RoleGateTests` and, as it happens, by
  `code-5` and `code-7`, which both gated at zero.
- **`code-6-round2-onerole` never reached round 2.** Architecture had the only second round and a
  threshold of zero; it raised no findings, so round 1 passed and there was nothing to revise. Two
  further attempts (`code-8`, `code-9`) chased the same observation through the resolve-and-review-
  again loop; `code-9` did reach round 2, but with all three roles budgeted for it — so what it shows
  is that a second round RUNS, not that a spent role is left out of one. **The narrowing itself is
  asserted only by unit test** (`RolesForRound`, `RoleGateTests`), because which role a live reviewer
  gates on is not something a harness can choose.

Both gaps have the same cause: a reviewer's findings are the input, and on a small clean diff there
often are none. That is a good property of the reviewers and an inconvenient one for a campaign.

## 4. The defect it found

`code-9` ran two rounds on `review/pr-money` with dealing off and every threshold at zero:

| round | verdict | gating | roles that raised | reviewers |
|---|---|---|---|---|
| 1 | `revise` | 1 | UxDxPerformance | 6 of 6 |
| 2 | **`call_human`** | 5 | all three | 3 of 6 |

Two things came out of it.

**The panel was showing a prompt the server would not run.** Reading the round-2 configuration to
explain the reviewer count exposed that `selectedFor` — the panel's mirror of
`PromptCatalog.ForRound` — took the DEAL switch in its ROTATING slot. With dealing on, round 2 of
Architecture displayed `arch-boundaries` while the server ran `architecture`; the server's rotation
read only `COAI_ROTATE_PROMPTS`, which the extension stopped writing when the Prompts and Gate
sections were merged. So rotation had no way in from the product at all, and its only surviving
effect was to make a dropdown name a prompt nobody would run.

Both halves lost the branch. `panelServerPromptAgreement.test.ts` now compares the panel's answer
against the server's resolution for every role, round and `hasRules` value; `COAI_ROTATE_PROMPTS`
stays as the legacy alias for the two dealing switches. Rotation was measured worse than asking the
universal question twice — 17 distinct findings against 25 over two code rounds — so nothing was
lost with it.

**A vendor can go quiet mid-campaign, and the round says so by name.** Three of round 2's six
reviewers came back empty from `antigravity` after a repair attempt, having answered five clean
rounds earlier in the same hour. The round did not fail: it reported `3 of 6 reviewers answered`,
named each failure with its role, kept every raw answer on disk, and reached a verdict on what did
come back. That is the designed behaviour for a partial round, observed under a real vendor fault
rather than a stub.

## 5. Cost

Eleven runs, ~4.0 M input tokens and ~0.22 M output across the two vendors, about twenty minutes of
wall clock in total. Neither vendor prices its own runs, so the money column is what the panel now
computes from rates a person supplies — the campaign spent tokens, not a figure this file can honestly
put a dollar sign in front of.
