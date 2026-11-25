# coai v2 — plan

> Status: **plan, nothing built, 2026-11-22.** Branch `feat/v2-plan`. Built with the v2 work method itself: a feature
> folder → epochs (≤ 5 epics each, MVP first) → epics → stories → tasks, each node with a YAML file. Stories and tasks
> are written for an epic when it starts (now: epoch 1). The split was re-decided by Fable on 2026-11-22: 5 epochs, MVP epics with measurable pass criteria, 10 cornerstone stories (feature.yaml `cornerstones`; 12 since 2026-11-25, when the E3 plan gate added E3 s3 and s4).

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
