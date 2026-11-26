# coai v2 — plan

> Status: **plan, nothing built, 2026-11-22.** Branch `feat/v2-plan`. Built with the v2 work method itself: a feature
> folder → epochs (≤ 5 epics each, MVP first) → epics → stories → tasks, each node with a YAML file. Stories and tasks
> are written for an epic when it starts (now: epoch 1). The split was re-decided by Fable on 2026-11-22: 5 epochs, MVP epics with measurable pass criteria, 10 cornerstone stories (feature.yaml `cornerstones`; 12 since 2026-11-25, when the E3 plan gate added E3 s3 and s4).
> **Stopped for re-review, 2026-11-26** (the ledger below): the owner ordered epoch 3 revised and reviewed whole.

## Plan changes

> Epoch 1 · approved 2026-11-22 · by reviewers · state: stopped

- 1.1 · 2026-11-25 · owner · contract, approach — decision 40: the coder prompt has a version for each vendor, the claude text unchanged (epoch 3 E3 SPEC C7)
- 1.2 · 2026-11-26 · owner · dod — the paid caps of epoch 3 E3: s8 t3 and s8 t4 together at most 30 USD, s5 t6 at most 30 codex calls, s6 t7 at most 30 agy turns; they replace 8 calls, 12 turns, 0.10 USD and 0.50 USD
- 1.3 · 2026-11-26 · owner · scope, contract — the risks agy A1 to A9 and codex C1 to C4 are accepted; claude, codex and agy each ship as coder, reviewer and consultant, so the codex and agy main-AI rows of E3 s7 t4 are both filled
- 1.4 · 2026-11-26 · owner · scope — the instruction step of E3 s7 t3 keeps its 64 KiB limit; a short form of the rule resolver's output is a follow-up outside epoch 3
- 1.5 · 2026-11-26 · owner · contract, epics, dod — a secret in the output of the instruction step stops nothing: the job goes on and a notice `instructions-secret` names the class and the line, never the value; its tab in the extension's notifications is a new doneWhen line of epoch 4 e4-local-pages
- 1.6 · 2026-11-26 · owner · contract — WorkPack renders an outside dependency in the pack text for every scope, epoch and epic included; whole-epic text ids move, approval ids stay
- 1.7 · 2026-11-26 · owner · contract, approach — a coder's checks (epoch 3 E4, A4, D12, D13): the head's own definitions run, nothing the head changed is refused, every difference from the base is reported; restore takes packages from the internet; E3's text agrees
  (The second entry stopped the plan. The owner on counting these and reviewing epoch 3 whole, 2026-11-26: «да, пересматриваем».)
  (Epoch 1 is dated at the plan gate of the whole plan: round 2, `good_enough`, 2026-11-22, commit c24e5141. The ledger was
  opened on 2026-11-26 with these seven entries. Changes before that day are not entries: they are in the SPEC files of the
  epics and in their deviations. For epoch 3 E3 they are sections J, K and L of
  [SPEC_CHANGES.md](epoch-3/e3-roles-complete/SPEC_CHANGES.md): the judgement of s4 t6, the coder comparison and the two
  findings of the second plan round are named there as plan changes. Whether they become entries is the lead's decision.)
  (What each entry changed in E3: section M of the same file. The re-review record for epoch 2 is written by the re-review.)
- 1.8 · 2026-11-26 · owner · contract, approach, dod — a new version of a CLI never stops anything, for every vendor (claude, codex and agy alike): no version, release, hash or pin of any vendor's CLI gates, refuses, stops a job or forces a new measure, a new judgement or a doc rewrite; what was measured is history ("measured on X"); a vendor ships by its verdict
- 1.9 · 2026-11-26 · reviewer · contract, dod — re-review round of epoch 3 E3 (session 1089eaa8, proceed), finding F0: the failure record of the instruction step redacts the argv and the last lines of standard error before they are recorded or shown; a rule of records, not of the prompt
- 1.10 · 2026-11-26 · reviewer · contract, dod — the same round, finding F1: the approval id covers a pack format version, raised with the outside-dependency change of 1.6; an approval of an older format binds no coder job and the refusal asks for a new plan round; so the approval id of every scope moves once
- 1.11 · 2026-11-26 · reviewer · contract, dod — the same round, finding F3: the 30 USD cap of E3 s8 t3 and s8 t4 is enforced before each call by a reserved priced worst case, not from the ledger afterwards; the reservation is recorded beside the actual cost
  (Entry 1.8: the owner's words as the lead gave them — "a new version must never stop anything, or rewrite docs, pins and so
  on. It came out, it came out. We move on. We can write down for history which version the check was on." — and that it
  holds for every AI. It takes "only a measured release", the pins and the claude version floor out of the plan.)
  (Entries 1.9 to 1.11 are the three findings accepted in the re-review round of E3, which read the plan at c48b3a5c. One
  finding, F2, was rejected: it is the owner's decision of entry 1.4. They are recorded as entries by the lead's order. The
  rule of plan changes also says that what a re-review changes is the baseline of the next epoch; whoever opens epoch 2
  decides which reading stands. The plan stays stopped: epoch 2 and its re-review record are not written here.)
  (What these four changed in E3: sections M and N of the same file.)

## Goal

Make coai one product the owner can run on every machine from one Windows VS Code, that never waits for a person, starts
in ≤ 2 s, and does the deterministic half of AI development itself (build, test, merge, PR) so the AI does only the
judgement half — faster, with quality kept.

## Owner decisions (2026-11-22, fixed)

- Shape C: the C# core (Native AOT) is simplified in place with its tests; the extension is a new thin shell on the
  shared UI kit, released as the same extension 2.0. Chat and phrases are carried as they are.
- Auto mode only: the gate never waits for a person; failures become notifications. Gate meaning kept: plan → resolve →
  code → resolve; document and feature reviews; outcomes proceed / revise / good_enough / not_reviewed.
- Names: `cons` (stuck consultant), `consask` (question consultant).
- Hub = a mode of the Team server on the ionos host, personal tokens; settings AND logs of every device; one-click update
  everywhere; nothing manual except on Windows (paste-ready bash/pwsh lines).
- VM: one shared instance for all OS users, everything shared including CLI logins.
- Zero-config install: finds claude / codex / agy, writes MCP config and hooks; vendor-neutral main AI.
- Main AI page: main AI, roles → models (orchestrator, coder, scout, important, BA), complexity 0–10 → model bands,
  Haiku task kinds, and the work method itself (MVP epics, max epics per epoch, min consults per epoch/epic) —
  configurable per device.
- AskUserQuestion hook: the first question in a session is answered by consask; later ones within 30 min reach the person.
- Coder delegation to another AI with a strict JSON contract; coai runs build/tests itself after each coder task and
  keeps the coder's CLI session until green; coai runs everything parallel that can be parallel.
- Toolchain adapters: C#, Node/JS/TS, PHP, Rust, Python, Java.
- tmux Claude sessions through the existing creds MCP; Claude mod with phrases, active consults, notifications.
- Studio (video) review mode with picture attachments; frames may go to Claude, Gemini (agy), Codex.
- Security lane deferred. Conventions optional: coai no longer ships or enforces them; a repo that mounts them still has them read (reworked later).
- Order: foundation → hub/devices/update → new features.

## Architecture

- Server half: [architecture/server.md](architecture/server.md) (incl. consultant review 1, § 16).
- Extension half: [architecture/extension.md](architecture/extension.md) (incl. consultant review 1, § 10).

## Epochs

| Epoch | Epics | Proves |
|---|---|---|
| [1 Foundation](epoch-1/epoch.yaml) | E1 gate core + live proof (MVP), E2 thin shell + joint smoke (MVP), E3 server cleanup + install + roles, E4 shell modules, E5 2.0 release | the simplified gate and the fast shell work end to end with real data |
| [2 Hub](epoch-2/epoch.yaml) | E1 hub slice (MVP), E2 VM shared instance (MVP), E3 device agent + sync, E4 update + rollout, E5 kit 0.3.0 + Devices page | one Windows window sees and controls every device |
| [3 Deterministic work](epoch-3/epoch.yaml) | E1 external-role jobs + router + failover + BA (MVP), E2 coder one task to green (MVP), E3 roles complete, E4 adapters + coder hardening, E5 work scheduler | an external role does a task that coai routes, checks and keeps green |
| [4 Operator](epoch-4/epoch.yaml) | E1 AskUserQuestion hook (MVP), E2 tmux sessions via creds (MVP), E3 Claude mod, E4 local pages, E5 Features page | the person works through coai |
| [5 Reach](epoch-5/epoch.yaml) | E1 studio attachments (MVP), E2 studio roles, E3 php/rust/python/java adapters, E4 git/PR/CI takeovers | video review through frames and every listed language |

## Growth surfaces (named in the architecture)

Hub logs (50 MB per device+stream, 2 GB total, sweeper), coder worktrees (24 h after terminal, ≤ 20 live per repo),
`coder/*` branches (merged: 14 days), runner logs (20 MB each, 30 days), hub settings revisions (last 20), slot files
(one host-level directory, released on exit).

## Test plan and Definition of Done

Per epic in its `epic.yaml` (`doneWhen`) and per task (`dod`). The v2 folder itself is the first fixture of the YAML
validator (epoch 1 E3): `validate_work` must accept it.
