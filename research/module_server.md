# module: server — coai-mcp, the protocol holder

> `src_mcp/src` — the host. Identity `connect-other-ais` on the wire, `coai` as the client's
> config key (which is what prefixes the tools: `mcp__coai__review_plan`). Built by hand on the
> `ModelContextProtocol` SDK — the hosted default logs to stdout, and stdout carries JSON-RPC.

## The seven tools

| Tool | Backed by | Refuses when |
|---|---|---|
| `providers` | `PanelService.ProvidersAsync` — CLI probe + vault state | never; it reports |
| `open` | `OpenAsync` — resolve branch, prune worktrees, load-or-create session | repo/branch unresolvable |
| `review_plan` | `RunStageAsync` with one `PlanCritique` per provider | no session; round awaiting resolve |
| `review_code` | `RunStageAsync` with the three code roles per provider | **no plan round reached `proceed`** |
| `resolve` | `ResolveAsync` — reasoned decisions by finding index | bad index; reject without a reason |
| `status` | persisted session + round trail | no session |
| `ask_human` | `Escalations` — a question FILE the extension watches | only an empty question; otherwise it WAITS the budget, then answers `no_answer_yet` telling the model to ask in the chat |

## Flow of one stage

`RunStageAsync`: load session → `RoundMachine.Begin*` (refusal = the answer) → resolve SHA → ONE
worktree lease → build work (schema file, role prompt + contract + context; repair prompt = same +
"ONLY the JSON") → `BoundedScheduler` → merge → `GateRule` → `RoundMachine.CompleteRound` → persist
(`PersistedSession.Pending` = what `resolve` indices point into) → `ReviewAnswer` with an
`instruction` sentence for the main AI. The lease disposes in `finally` — a thrown stage leaves no
worktree.

## Escalation — reaching a person without a port

`ask_human` writes `escalations/<id>.json` into the data directory the extension already reads for
the rounds view, then polls for `<id>.answer.json` beside it. The round's still-gating findings ride
with the question, because a person deciding "ship anyway?" should not have to go looking for what
gates.

A malformed, half-written or empty answer file is **not** an answer — the wait continues; unblocking
a round on nothing is the failure this guards against. The budget (`COAI_ESCALATION_MINUTES`, 30 by
default; `COAI_ESCALATION_SECONDS` wins when set) ends in `no_answer_yet` with the instruction to ask
in the chat — the family's `remote-ask` fallback — and the question file **stays open**.

## Configuration and keys

Environment until the extension arrives: `COAI_PROVIDERS`, `COAI_MODEL_*`, `COAI_EXE_*`,
`COAI_MAX_ROUNDS`, `COAI_GATE_THRESHOLD`, `COAI_ON_EXHAUSTED`, `COAI_MAX_CONCURRENCY`,
`COAI_MAX_PER_PROVIDER`, `COAI_REVIEWER_TIMEOUT_MINUTES`, `COAI_DATA_DIR`, `COAI_LOG_LEVEL`, and
`COAI_CREDS_KEY` — the CredsForDevs config-entry key. `KeyVault` runs `creds config <key>` once at
startup; missing binary / no key / 401 / malformed body are named per-vendor unavailabilities in
`providers`, never crashes, never partial applies, never logged values.

## Persistence

`SessionStore`: one JSON file per session key (SHA-256-prefixed name) under
`COAI_DATA_DIR/sessions`; temp+move writes; a torn file reads as a fresh session rather than a
locked repo. Round trail (`RoundRecord`) and pending findings ride in the same file — `status`
survives a server restart, per the durable-status rule.

## Verification that matters

- `McpContractTests` speak real JSON-RPC over real stdio to the built binary — and via
  `COAI_CONTRACT_EXE` to the PUBLISHED one; the release workflow runs exactly that as its smoke.
- `PanelServiceTests` run the full loop (plan rounds → gate → code rounds → done) against the
  vendor-mode fake CLI: dedup across providers, the standing-rejection discount, restart survival,
  and the six-launch fan-out with three distinct role prompts, all observed.
- Stdout purity is a test: verbose logging on, every stdout line must parse as JSON.
