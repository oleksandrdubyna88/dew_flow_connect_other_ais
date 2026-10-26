# PLAN — S5's stories: Kimi Code as a consultant and as a question-consultant row

> Status: **in progress — E5.1–E5.4 and E5.7 implemented 2026-10-16; E5.5–E5.6 implemented 2026-10-26 (rows, vectors, generated table, planner arm, the flags refusal, 12 scenario tests green; deviations 47–58) — not yet reviewed; the live checks (J2) and M-K17 are not run.** E5.1: M-K16 recorded as RESULTS §29
> (every fact took the first branch of E5.1's table) and the probe client's `loadUpdates` / `endLikeTheShim`. E5.2: the shim's `--session`
> with `session_lost` (exit 70); deviations B1–B11, *E5.1–E5.2 as built*. E5.3 (Fable): the owned homes; deviations 13–20. Opus reviewed
> each, and its fixes are in. E5.4 (Opus): `KimiConsultant`, registered and seeded, and the service arms E5.3 owed; deviations 21–30, not yet reviewed. M-K17 (§30) not run. The judge (Opus) wrote the stories; Fable confirmed the split (§0.2) and gave E5.3
> its security judgement. The plan gate passed (proceed), and its findings are folded in (§"Plan gate 2026-10-16").
> The coder of each story follows the gate's command (§0.2): E5.3 on Fable, every other story on Opus.
> **E5.7 implemented 2026-10-16 (Opus)**, built on its own branch beside E5.4 and merged into this one: the consultant-runtime gate
> (`CONSULTANT_SINCE`, a note, never a withheld entry), the guard over both tables, the homes folder naming both CLIs, and the help in
> five languages; deviations 40–46, *E5.7 as built*. With E5.4's seed (`features.consultant` gains `kimi`) merged, S5.7.5 is green.
>
> Parent plan: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md), §4 **S5** (decisions D1, D6, D15, D19, D20).
> Built on S4, which is merged and released as coai-mcp 0.57.0: [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md)
> (its 76 deviations are binding here). Evidence: [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md) §5, §8,
> §11, §12, §19, §23, §25–§28. The private-folder rule: D1, D3 and D7 of
> [PLAN_vibe_live_round_fixes.md](../research/PLAN_vibe_live_round_fixes.md). The parent's decisions are cited by number (D6,
> D15, D19, D20); this document does not repeat them. It says who builds what, in which order, and how each part is proved.

## 0. How to read this

**The epic.** A Kimi row answers `consult` (a conversation of several turns) and a question-consultant row (one shot). It
also passes `coai-mcp --check-consultant`. Every launch goes through S4's one control: `KimiLaunch` for the home, the
environment, the model and the sign-in gate, and the `--ask-acp` shim for the refusals, the read boundary, the tree hold and
the foreign-`kimi` wait. **New in S5:** the shim's `--session` load path; the conversation's owned home kept across turns; a
junction-first disposal for every ONE-SHOT Kimi home; `KimiConsultant`; three capability rows and a planner arm; and the
extension's half. **Not in S5:** M-K14 (the refresh race — the owner put it later); Linux, WSL and macOS (D18 still refuses
them); the Kimi REVIEWER's homes, which still live under TEMP (deviation 68 of E4, left open there); Team server, chat, bench.

**Who does what** ([subagent-models.md](../.agents/conventions/common/subagent-models.md)). For each story: the scenarios and the
brief are by Opus (this document), or by **Fable for a cornerstone**. The review is by Opus. The measurement of E5.1 is run by a
scout (Sonnet), and only with the owner's yes for its paid turns. **The coder follows the plan gate's command (2026-10-16):** a
story that touches authentication or security is built on **Fable**, an ordinary story on **Opus** — the marking per story is
§0.2's table (E5.3 on Fable; E5.1's client code, E5.2, E5.4, E5.5, E5.6 and E5.7 on Opus). The fixes after a review are by the
same model that built the story.

**Cornerstones — the ledger.** The plan's quota is Q = ⌈2·5/5⌉ = 2 (five epics, S1–S5). The parent's ledger line (§4 S4)
reads **`Cornerstones: 2 of 2 — E4.1, E4.2`**, so the quota is spent. **E5.3 is the cornerstone this epic needs** (decision J1
below). By [subagent-models.md](../.agents/conventions/common/subagent-models.md) ("a mark that would make k exceed Q goes to
the operator, not into the document"), this document does NOT change the ledger line. **Decided 2026-10-16, after
the question consultant was asked (consult 95f08e0b, option B): the quota stands at 2 of 2.** E5.3 is NOT a cornerstone.
Opus writes its spec from the scenario set below. Fable gives E5.3 a **security judgement**, since it handles authentication
and that role is outside any quota. The judgement covers the owner/sweep races, the lock order, how every disposal stop
propagates, the row/check fallback paths and the separate `kh-*` root. It is given before any code. No coder starts E5.3
before its spec and that judgement are final — **both are in this document now** (E5.3, decisions j–k and the judgement R1–R11,
2026-10-16).

**Build order** (strict where an arrow is shown):

```
E5.1 (measure M-K16) ──► E5.2 (shim --session) ──► E5.4 (KimiConsultant, consult) ──► E5.5 (the check)
                                                        │                            └► E5.6 (question row)
E5.3 (homes, cornerstone) ──────────────────────────────┘                                     │
E5.7 (extension) — written beside E5.4 and E5.6, merged after both ◄───────────────────────────┘
```

E5.3 does not depend on E5.1 or E5.2. Its primitives (the one-shot disposal, the conversation root) can be built first.
E5.4 needs E5.2 (`--session`) and E5.3 (the root). E5.5 and E5.6 need E5.4. E5.7 needs the seeds of E5.4 and E5.6.

**What S4 left that S5 reuses, not rewrites** (each anchor verified on `origin/main` 02ffd326, 2026-10-16):

| Need | Already there |
|---|---|
| A conversation folder made private, held, stamped, with ONE snapshot path refreshed in place | `ConsultantPlaces.ForConversationAsync` (`src_mcp/src/Server/Consultation/ConsultantPlaces.cs:126-136`), `OwnedAsync` through `PrivateFolder.Open` (`:143-174`, the open at `:146`), the folder's in-process turn and its `coai-turn.lock` (`:203-245`), `ConversationSnapshots.FreshAsync` (`ConversationSnapshots.cs:31`, `TreeName = "tree"` `:22`) |
| The consultant's home folder handed to the adapter | `ConsultationService.cs:824` (`OwnedScratch = place.Folder`), `:827` (`Workdir = place.Workdir`) |
| The Kimi home made, validated before EVERY launch, the sign-in gate | `KimiLaunch.TakeAsync` (`src_mcp/runners/Kimi/KimiLaunch.cs:73-91`: the home's turn `:81`, the gate `:98`), asked by the executor through `KimiRuntime.TakeTurnAsync` (`KimiRuntime.cs:139-149`) |
| The reviewer's shim request, its home under an owned folder | `KimiRuntime.Build` (`KimiRuntime.cs:76-90`; the root `KimiHome.HomeFor(OwnedScratch, provider, role)` at `:79`; the argv `:96-108`) |
| Junction-first disposal with the live-tree probe | `KimiHome.Dispose` / `DisposeAllUnder` (`KimiHome.cs:125-144`), `KimiCredentialsLock.HomeProbe` (`KimiCredentialsLock.cs:195`) |
| The conversation sweep (D20) and `close_consult`, already junction-first | `ConsultantHomeSweep.RemoveNow` (`:77`), `Removed` (`:202`), `KimiHomesDisposed` (`:243-254`), `AllTaken` over the folder and every sub-folder (`:280`) |
| One-shot homes under `<root>\coai-vh\<8>` | `VibeHomes.ReserveOneShot` (`VibeHomes.cs:36-55`), chosen by `HomesFolder` in the question row (`QuestionRowLaunch.cs:101`) and the check (`ConsultantCheck.cs:211`) |
| Cumulative usage → a per-turn share | `ConsultationBilling.ThisTurnsShare` (`ConsultationBilling.cs:33-34`) over `ConsultationUsage.Less` (`src_mcp/core/Consultation/ConsultationUsage.cs:32-40`, floored at 0); the question row's twin (`QuestionRowLaunch.cs:284`) |
| A question row in a copy of its roots, the prompt naming the copies | `CwdKind.Snapshot` (`src_mcp/core/QuestionConsult/ConfinementPlanner.cs:10-22`), `ConsultantPlaces.OneShotAsync` (`:293-314`), `QuestionFanOut.cs:214-217` |
| The Vibe consultant, the model to follow | `src_mcp/runners/Vibe/VibeConsultant.cs` (whole file, 132 lines) |

**Commands, for every story** (from the worktree root; never `dotnet test`):

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<TheClass>"      # the story's classes first
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe                                    # then the whole suite
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes
cd src_vs_code && npm run typecheck && npm run lint && npm test                        # E5.4, E5.6, E5.7
node src_vs_code/scripts/generate-feature-availability.mjs --check                     # E5.4
node src_vs_code/scripts/generate-runtime-capabilities.mjs --check                     # E5.6
node --test scripts/lib/*.test.mjs                                                     # E5.1
node .agents/conventions/tools/plan-lifecycle.mjs                                      # every story that edits a doc
```

**For every story.** Cyclomatic complexity ≤ 4 per method. Red first for every behaviour, and the red message goes into
the summary. A class that does not exist yet is a COMPILE red: stub it so that it returns empty or does nothing, then watch
the behavioural red named in the table. Every expected red below is an ASSERTION message, never a compile error. Anything a
test makes outside its process is named `testsToDelete-<what>-<short id>`
([test-leftovers.md](../.agents/conventions/common/test-leftovers.md)): `TempDir.For("testsToDelete-<what>-")`.
**No test makes a junction to anything outside its own `testsToDelete-` folder** — never to the developer's real
`~/.kimi-code`. A fake person's home is a folder inside the test's own temp folder (`KimiFixture`, `src_mcp/tests/KimiFixture.cs`).
No test holds an inheritable handle in the test host (`NoTestHoldsAnInheritableHandleTests`, E4 deviation 33). This is a
PUBLIC repository: no path, name or token of a real machine goes into a test, a fixture or a RESULTS row.

## 0.1 The three questions the caller asked, decided

**J1 — the cornerstone is E5.3, the owned homes' lifecycle.** Reasons, in order of weight:
1. **The first bug there signs the person out or hands Kimi a stale home.** The conversation home keeps a junction to the
   person's ROTATING sign-in (D19) across turns, across server restarts, and into two sweeps (D20 and the six-hour scratch
   sweep). A disposal that unlinks under a live `kimi` turns its next refresh into a sign-out (E4.2 decision 11). A one-shot
   home that is deleted by the bare deleter does not run the live-tree probe.
2. **It holds the one defect this split found** (decision a of E5.3): if the conversation folder is itself the Kimi root, as
   E4.2 decision 8 assumed, a turn waits for its own turn and never starts. That is a lock-order question, which is a
   cornerstone's kind of judgement.
3. **It touches shared primitives that Vibe uses too:** `OneShotHome.Dispose` and `VibeHomes.SweepOneShot`. A change there
   moves Vibe's one-shot homes as well.

Not cornerstones: E5.2 (a protocol branch; the refusals and the read boundary do not change, and M-K5 measured them on a
loaded session); E5.6 (the planner arm is a table row; the standing is decided here, in E5.6 decision 2); E5.4, E5.5 and E5.7
(wiring of shapes that exist).

**J2 — one measurement is needed before code, and one before a sentence.**
- **M-K16 — before E5.2's code** (E5.1). M-K5 and M-K5b recorded that `session/load` keeps the refusals, does not rebind the
  cwd, finds its session on an owned home 3 of 3, and that `/usage` is cumulative across ONE load. They did not record three
  things that decide E5.2's code: (a) whether the RESULT of `session/load` announces the `model` option (the in-shim model check,
  D10 (2), reads that announcement — `AcpClient.cs:141-148`); (b) whether Kimi REPLAYS the history as `session/update`
  notifications during the load (the probe's small client dropped every chunk before its prompt, `scripts/lib/kimi.mjs:453`, so
  nobody looked; the shim's `AcpSession.Answer` keeps EVERY chunk, `AcpSession.cs:83-92`, `:455-460`); (c) what Kimi answers for
  a session id it does not hold (the "conversation dropped" signal). It also confirms (d) a THREE-turn conversation whose turns
  each end the way the shim ends them — `session/cancel`, a short quiet wait, then the tree kill (`AcpClient.cs:192-201`,
  E4.2 decision 10) — keeps a monotone cumulative total, so the per-turn delta never needs the floor. Cost: about 9 paid turns
  of a few seconds each, plus unpaid arms. The brief is E5.1.
- **M-K17 — before any help or limitation sentence about the web for a `none` or `disk` row** (optional for code). Kimi
  announces `WebSearch` and `FetchURL` on this plan (RESULTS §7) and no cell exercised them (§14). If either runs without a
  `session/request_permission`, a `none` or `disk` row can send what it read to the network. The rows are `unconfined` either
  way, and the `web` capability stays `unmeasured` (refused) either way, so no code waits. Only the words wait.
- **Owed AFTER the code** (the parent's §5, not new): one live `consult` of THREE turns through the released binary (so two
  loads run), one live question row per capability, and one live `--check-consultant`, each recorded in RESULTS §31.

**J3 — where the home lives, and Kimi's path budget.**
- **The conversation's home lives at `<homes root>\coai-kimi\<consultation id>\kh-<10 hex>`.** The homes root is
  `PanelSettings.ConsultantHomesRoot` (`PanelSettings.cs:385`, `COAI_CONSULTANT_HOMES`, else the local application data —
  D1 of the Vibe fixes). `coai-kimi` is already in `ConsultantHomes.All` (`src_mcp/runners/Consultation/ConsultantHomes.cs:21`,
  `:24`), so D20's sweep already walks it. It is entered only through `PrivateFolder` (`ConsultantPlaces.cs:146`; D7 of the
  Vibe fixes). Not a bare `%LOCALAPPDATA%` path: the person may set the root, and the root is the one place D7's privacy rule
  is enforced. The `kh-<10 hex>` sub-folder, and not the conversation folder itself, is decision a of E5.3.
- **The one-shot homes (a question row, a check) live at `<homes root>\coai-vh\<8 hex>\kh-<10 hex>`**, through
  `VibeHomes.ReserveOneShot`, which both sites already call for any runtime that declares a `HomesFolder`. That is the lesson of
  PR #823: one short, private, owned and locked folder per one-shot launch, outside `ConsultantHomes.All`, so D20 never judges it
  as a conversation. What #823 did NOT need, and Kimi does, is a junction-first disposal of that folder (E5.3 decision g).
- **Kimi's path budget: none is needed, and the numbers say why.** D19 says "a 280-character session path inside a
  205-character home was written and read back". **Verified.** The probe's deep arm builds `KIMI_CODE_HOME` at about 205
  characters (`scripts/probe-kimi-confinement.mjs:363-368`; its label at `:358` says "~250", which is stale — the code makes 205).
  Kimi's session folder below it is `sessions\wd_<cwd name>_<12 hex>\session_<36>` — 75 characters for the probe's cwd named
  `work` — so the folder is at 280. The file `agents\main\wire.jsonl` is 23 more, at **303**. Both were written and read back with
  `LongPathsEnabled = 0` (RESULTS §11, the table row of M-K8). **Kimi's deepest measured write is therefore 98 characters below
  its home** for a 4-character cwd name. A sub-agent's folder `agents\agent-<n>` (RESULTS §7) is 3 characters longer by name; its
  file names were not recorded. At the default root `C:\Users\<u>\AppData\Local` (23 + u characters) the conversation's home is
  85 + u, so its deepest write is about 183 + u (186 + u for a sub-agent). A one-shot home is 59 + u, so about 157 + u. Both are
  under 259 for any profile up to 73 characters. **They do not need to be:** Kimi wrote past 260, which Vibe's interpreter
  cannot (`VibePathBudget`). So S5 adds no `KimiPathBudget`. The one thing that could still fail on a long path is COAI's
  own walk of the home (`KimiHome.Refusal`, `GitScratch`). E5.3 pins it with a test (S5.3.11), not with a refusal.

## 0.2 Split confirmed / changed by Fable, 2026-10-16

**Verdict: the split stands — seven stories, the order and the arrows as drawn; two decisions changed, three things added, nothing
removed.** Read against the parent's S5 (`PLAN_vibe_and_kimi_runtimes.md:817-854`), E4's record and the code on `origin/main` 02ffd326.

- **The boundaries are right.** E5.2 is the only story that touches the shim, and the only one with a protocol branch. E5.3 is the only
  one that touches a junction or a lock. E5.4 composes and registers; E5.5 and E5.6 prove two existing paths with a new adapter; E5.7 is
  the extension. No story edits another story's files, so different coders build them without a merge of logic.
- **The order is right, and E5.3 first is right.** E5.3 depends on nothing of E5.1 or E5.2, and its one code change (decision g) moves
  Vibe's one-shot homes too — it should land, and be released, before any Kimi consultant can make a one-shot home. E5.4's dependence
  on E5.2 is real (`--session` IS the second turn); on E5.3 it is nominal, because the conversation root is already `kh-*`
  (`KimiRuntime.cs:79`) — E5.4 may START beside E5.3 and merge after it.
- **What was missing — added:** (1) the per-turn re-resolution of where the sign-in's ancestors lead (E5.3 decision j, S5.3.15): a
  make-time check that a conversation home, living for hours, never repeated; (2) explicit `session_lost` arms in the shim's two switches,
  whose DEFAULT arms print `cancelled` and exit 69 (E5.2 decision 9, S5.2.12) — without them E5.2 decision 4's "70" is silently 69;
  (3) the plan gate's three accepted findings (§"Plan gate 2026-10-16": E5.2 decision 9, E5.3 decision k, E5.4 decisions 11–12).
- **What changed:** E5.3 decision g, step 3 — the deletion goes UNDER the lock, the lock file and the folder last (judgement R5); E5.3
  decision d gains the one rule that makes its order safe (R2: every disposer takes with NO wait).
- **Considered and not changed:** splitting E5.3 into "the conversation home" (tests only) and "the one-shot disposal" (code). Both halves
  hold a junction to the sign-in and share one lock order; two stories would need the same judgement twice. Kept as one.

**Who builds what — the gate's command, applied.** A story that touches authentication or security is built on Fable, an ordinary one on
Opus; Opus reviews every story.

| Story | Kind | Built on | Why |
|---|---|---|---|
| E5.1 | ordinary — a measurement; the client gains two features | Opus for the client's code; the paid cells by a scout (Sonnet) with the owner's yes | no product code; the client is the probe's own |
| E5.2 | ordinary | Opus | a protocol branch; the three blocks (refusals, read boundary, tree hold) are PINNED unchanged (S5.2.9, S5.2.10), and S5.2.4 keeps `Diagnosed`'s existing order (`AskAcpMode.cs:292-293`) — nothing about the sign-in changes |
| E5.3 | **authentication / security** | **Fable** | holds the sign-in's junction across turns; the lock order; a disposal under a live tree signs the person out |
| E5.4 | ordinary | Opus | composition over `KimiRuntime` — the gate is `inner`'s by construction (decision 1) |
| E5.5 | ordinary | Opus | an existing path proved with a new adapter |
| E5.6 | ordinary | Opus | table rows and a planner arm; the STANDINGS are decided in this document (decision 2), not by the coder |
| E5.7 | ordinary | Opus | the extension |

## E5.1 — Measured first: M-K16, `session/load` as the shim will use it (and M-K17, the web tools)

`model: sonnet` (a scout runs the paid cells; Opus reviews the RESULTS rows) · the client's two features (decision 1) are built on
Opus (ordinary, §0.2). No product code.

**Goal.** Record the facts E5.2's code depends on, 3 of 3 on Windows, Kimi 2.1.1, through the SHIPPED shim for every turn 1
and through the probe's small client for every load (the shim has no load path yet — the M-K5 arrangement, RESULTS §3).

**Files and anchors (verified).**
- `scripts/probe-kimi-confinement.mjs` — the cells table (M-K5 at `:643-667`, M-K5b in `scripts/lib/kimi-d19.mjs:236-262`),
  re-plumbed to the shim's flags by E4.5 (E4 deviation 62): turn 1 already runs through the shim on a product-shaped
  owned root (`<root>/home` beside an empty `<root>/os`).
- `scripts/lib/kimi.mjs:415-512` — `acpTurn`, the small client: `session/load` at `:491-493`; it adds a chunk to the answer only
  while `state.prompting` (`:453`); it ends by closing stdin, waiting 1.5 s and then killing the tree (`finish`, `:425-436`).
- `scripts/lib/kimi.test.mjs` — the node unit tests of the client.
- `research/RESULTS_kimi_confinement.md` — §29 (M-K16) and §30 (M-K17) are new; §25's "not measured" list is updated.

**Decisions.**
1. **The small client learns two things, and both are tested before any paid run.** (a) `loadUpdates`: every
   `session/update` that arrives between the `session/load` request and its response is recorded — its `sessionUpdate`
   kind, and for a text chunk the first 120 characters (no more, so the row stays small). (b) `endLikeTheShim`: when set, the
   turn ends as `AcpClient` ends one — `session/cancel`, at most 1 s of quiet, the tree kill, and only then stdin closed. This
   replaces the 1.5 s stdin-first grace for the M-K16 arms, so the measured ending is the product's.
2. **One cell, three arms, 3 runs each** (`--cells M-K16`):
   - **`announce` (1 paid turn per run):** turn 1 through the shim (`Reply with exactly: OK`); then the client loads that id on
     the SAME cwd path and the SAME owned root, sends no prompt, and ends like the shim. Record: the load result's
     `configOptions` (the `model` option's values, or "none announced"), and `loadUpdates`.
   - **`three-turns` (3 paid turns per run):** turn 1 through the shim; turns 2 and 3 by the client: load, `set_config_option`
     `model`, the prompt `Reply with exactly: OK<n>`, then `/usage`, then end like the shim. Between the turns: rewrite
     `notes.txt` in the cwd IN PLACE (the refresh of D6), and run the probe's home check (`scripts/lib/kimi-home.mjs`: the
     junction's target, `config.toml`'s bytes, `os/` empty). Record per turn: the session id; `Session total` in and out; the sum
     of `outputTokens` in the session log; the answer counted only after the prompt; whether a `loadUpdates` chunk holds
     `OK<n−1>`; `os/` empty; the person's credentials folder's file NAMES (never their content).
   - **`unknown` (unpaid):** a fresh owned root, the client loads a well-formed random UUID. Record the JSON-RPC answer (code
     and message) and the time it took.
3. **M-K17 (`--cells M-K17`, 3 paid turns), run before E5.6's help sentence:** through the shim, the prompt asks for
   `FetchURL` on `https://example.com/` and a `WebSearch` for `coai probe`. Record the report's `refusals` (a
   `session/request_permission` the shim refused) and `toolCalls` (title, kind, status). No local listener is used: the
   managed provider supplies these tools (RESULTS §7), so the fetch may not leave this machine at all, and only the permission
   channel is observable.
4. **The owner's yes before any paid run**, with the count: M-K16 = 12 paid turns (3 + 9), M-K17 = 3.

**What each result decides** (written now, so the result cannot be bent to fit the code):

| Fact | If … | Then E5.2 … |
|---|---|---|
| (a) the load result | announces the `model` option with values | runs the same in-session check as a new session (`NotOffered`) on the LOAD's announcement |
| | announces no `model` option | sets the conversation's model with `set_config_option` and runs no in-session check on a load. The model was checked against the curated list before the launch (`KimiLaunch`), and against turn 1's announcement in the same session. A refusal by the agent stays `agent_failed`, 70 |
| (b) the replay | replays chunks during the load | marks the answer AFTER the load returns — the answer is only what arrives after the prompt. The trace lists are marked the same way if `tool_call` updates are replayed |
| | replays nothing | marks it all the same (harmless). S5.2.2 pins the mark with a fake agent that replays |
| (c) an unknown id | any JSON-RPC error | maps every error answer to `session/load` that no known failure names to the new end `session_lost`. The RESULTS row records Kimi's words for the help |
| (d) three turns | the same id 3 of 3, totals monotone, delta_out = that turn's `outputTokens` | keeps `UsageIsCumulative = true` (E5.4) as M-K5b found it |
| | a total FALLS after a turn | stops: the delta floors at 0 (`ConsultationUsage.Less`) and would under-bill. The plan records it and the owner decides before E5.4 |
| | the session is not found on turn 2 or 3 | stops: the conversation cannot rest on `session/load`. Re-plan S5 with the owner |

**Result (RESULTS §29, 2026-10-16): the FIRST branch of every row.** (a) the load announces the `model` option (the home's one model, 9 of 9 loads) → the in-session check on the load's announcement; (b) Kimi replays the history before the load's response (9 of 9) → the answer is what follows the load; (c) an unknown id is `-32602` `Invalid params: Unknown sessionId: <id>` (3 of 3) → `session_lost` for any refused load no known failure names; (d) the same id, totals that only rise, the delta equal to the turn's own output (6 of 6) → `UsageIsCumulative = true`. Not measured: whether `tool_call` updates are replayed — S5.2.2's fake agent replays one.

**SCENARIOS** (the client's unit tests are red first; the cells are the measurement).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.1.1 `acpTurn records what arrives during session/load` (`kimi.test.mjs`) | a fake agent script (node) that answers `session/load` after sending two `agent_message_chunk` updates `OLD` and one `tool_call` | `acpTurn({ load: 's-1' })` | `state.loadUpdates` lists `agent_message_chunk` ×2 (text `OLD`) and `tool_call`; `state.answer` is empty | `AssertionError [ERR_ASSERTION]: Expected values to be strictly deep-equal: + actual undefined - expected [ { kind: 'agent_message_chunk', … } ]` |
| S5.1.2 `endLikeTheShim cancels, waits, kills, then closes stdin` | the fake agent records the order of `session/cancel`, its own kill and its stdin EOF | `acpTurn({ endLikeTheShim: true })` | the order is cancel → kill → (no EOF seen, the process was gone first) | `AssertionError: Expected values to be strictly deep-equal: + actual [ 'eof', 'killed' ] - expected [ 'cancel', 'killed' ]` |
| S5.1.3 M-K16 `announce` | the shipped shim, a product-shaped owned root, the owner's sign-in through the junction | 3 runs | RESULTS §29 row: what the load announced, what it replayed | UNKNOWN — the measurement is the scenario |
| S5.1.4 M-K16 `three-turns` | the same, cwd refreshed in place between turns | 3 runs × 3 turns | RESULTS §29: same id 3/3 per turn; totals and deltas; `os/` empty; credentials names kept | UNKNOWN |
| S5.1.5 M-K16 `unknown` | a fresh owned root | 3 runs | RESULTS §29: Kimi's code and words | UNKNOWN |
| S5.1.6 M-K17 | the shipped shim | 3 runs | RESULTS §30: asked (refused) or not asked, per tool | UNKNOWN |

**Done when** S5.1.1–S5.1.2 are green, the owner said yes to the spend, §29 (and §30 when run) is written with the scratch's
raw rows kept outside the repository, every owned root was disposed junction-first and the person's credentials folder kept
its names (the probe's existing guard), and the table above names which branch each E5.2 item takes.

## E5.2 — The shim's load path: `--session <id>`, `session/load`, the answer marked, `session_lost`

`model: opus` spec (this document) · built on Opus (ordinary, §0.2) · Opus reviews. **Starts only after E5.1's §29 is written.**

**Goal.** `coai-mcp --ask-acp --profile kimi … --session <id>` continues the session `<id>`: `initialize` →
`session/load {sessionId, cwd, mcpServers: []}` → the model (checked when the load announces it, E5.1 (a)) →
`set_config_option` → `session/prompt` → (answered) `/usage`. The cwd is the conversation's ONE stable snapshot path, the
same path as turn 1 (M-K5: a loaded session resolves paths against the folder it was created in). Without `--session` the
shim does exactly what it does today, byte for byte.

**Files and anchors (verified).**
- `src_mcp/src/Acp/AskAcpMode.cs` — `TurnAsync` `:95-121` (the `AcpAsk` is built at `:115-117`); `Refusal` `:204-205`;
  `Exit` `:272-289`; `Diagnosed` `:292-293`; `EndExit` `:299-305`; `Report` `:308-332` (it already prints `sessionId`,
  `:316`); `EndWord` `:354-363`; the type's remarks `:8-43` (the exit contract).
- `src_mcp/runners/Acp/AcpOutcome.cs:11` (`AcpAsk`), `:14-36` (`AcpEnd`).
- `src_mcp/runners/Acp/AcpClient.cs` — `ConverseAsync` `:121-134` (`session/new` `:124`, the id `:125`); `NotOffered`
  `:141-148`; `PromptedAsync` `:151-159`; `UsageAsync` `:167-181` (the mark that splits the answer from the `/usage` reply,
  `:169-170`, is the pattern to repeat); `Cut` `:185`; the outcome `:112-117`.
- `src_mcp/runners/Acp/AcpSession.cs:78` (`SessionId`), `:83-92` (`Answer`), `:455-469` (`Update`).
- `src_mcp/runners/Acp/AcpMessages.cs:62` (`NewSession` — `LoadSession` joins beside it), `:87` (`AnnouncedValues`).
- `src_mcp/runners/Acp/AcpWords.cs:10-37` (the flag and end words shared with their readers, E4 deviation 63).
- `src_mcp/src/Program.cs:2441` (the shim's help text).
- `src_mcp/core/Consultation/ConsultantHandle.cs:17-20` (`IsWellFormed`: `^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$`).
- Test harness: `src_mcp/tests_fakecli/AcpAgent.cs` — the script fields `:25-26`, the method switch `:121-142` (an unknown
  method is answered with an error, so `session/load` is refused today); `src_mcp/tests/FakeAcpAgent.cs:64-67` (`Models`,
  `UsageReply`). Classes to extend: `AskAcpModeTests`, `AKimiShimScenarioTests`, `AcpClientTests`.

**Decisions.**
1. **The flag is `--session`, the shim's own.** `AcpWords.SessionFlag = "--session"`. A value that `ConsultantHandle.IsWellFormed`
   refuses is 65 before anything starts (`--ask-acp: --session must be the id an earlier turn's report named`). The adapter
   validates the handle too (`ConsultantLaunches.MustBeLaunchable`), but the shim is a process boundary and checks its own argv.
   The name `--session` is also used by the `--findings` mode (`Program.cs:1621`); the shim's flags are parsed apart
   (`Program.Flags(args)` from the mode on, `AskAcpMode.cs:81`), so the two never meet.
2. **`AcpAsk` gains `Session`** (default empty). `AcpClient.ConverseAsync` chooses `session/new` or `session/load` on it.
   On a load, `session.SessionId` is `ask.Session` (the result may not repeat it), and the report's `sessionId` is the same id.
3. **The answer is what arrives after the prompt.** After `session/load` returns, the client marks `session.Answer.Length`
   (and the counts of the three trace lists, if E5.1 (b) saw `tool_call` replays). The outcome's answer is
   `Answer[from..to]`, where `to` is the existing `/usage` mark. `Ended` gains `AnswerFrom` (0 for a new session — unchanged).
4. **A load the agent refuses is `session_lost`, unless a known failure names it.** A new `AcpEnd.SessionLost`, the word
   `AcpWords.SessionLostWord = "session_lost"`, exit 70 (`EndExit`, "the agent refused in its own words" — no new exit code,
   never 64). `Diagnosed` keeps its order, so a load refused for the sign-in is still `not_signed_in`, 77. Nothing is
   recognised by the agent's TEXT. The adapter reads only the end word (E5.4 decision 5).
5. **The model on a load follows E5.1 (a)** — exactly one of the two branches of E5.1's table. The other branch is not written.
6. **Nothing else moves.** The hold is taken before the load as before the new session (`AskedAsync`, `AskAcpMode.cs:132-149`).
   The read boundary is the cwd, and the cwd is the same path. Every permission request is refused (M-K5: 3 of 3 on a
   loaded session). `/usage` is sent only after `end_turn`.
7. **CC ≤ 4:** `ConverseAsync` becomes `OpenedAsync(session, ask)` (new or load, answering the announcement to check) plus the
   existing tail. `TurnAsync` does not grow.
8. **Docs:** `research/module_runners.md` (the shim: `--session`, the mark, `session_lost`); the type's remarks in
   `AskAcpMode.cs`; the `--help` text. NOT `.agents/PROJECT.md` (its bundle is at its byte budget — E4 deviation 42).
9. **(Plan gate 2026-10-16, finding (a); Fable) The new end has EXPLICIT arms in both switches, and the contract names it.**
   `EndExit`'s default arm is `_ => Unavailable` (69) and `EndWord`'s is `_ => "cancelled"` (`AskAcpMode.cs:299-305`, `:354-363`): an
   `AcpEnd` value added without its arms exits 69 and prints `cancelled`, and decision 4's "70" is silently false. So `EndExit` gains
   `AcpEnd.SessionLost => Failed` and `EndWord` gains `AcpEnd.SessionLost => AcpWords.SessionLostWord`. The type's remarks (`:8-43`) list
   `session_lost` under 70 as "the agent no longer holds the session `--session` named"; `KimiReport.End`'s remark (`KimiReport.cs:15`)
   lists the word; `research/module_runners.md` the same. S5.2.12 pins that NO `AcpEnd` falls to a default arm. The host's side of the
   contract — classified from the report's `end`, never from text, and what the consultant then does — is E5.4 decision 11.

**BRIEF.**
1. `tests_fakecli/AcpAgent.cs` learns `session/load` from three script fields, so that every existing script keeps its
   behaviour: `loadable` (absent: `true` — the load is answered `{}`; `false`: answered with the error of `loadError`, default
   `-32602 "session not found"`); `replay` (chunks sent as `session/update` BEFORE the load's answer); `loadModels` (the
   `model` values announced in the LOAD's result; absent: none). The agent logs the method and its params, as for `session/new`.
2. `AcpMessages.LoadSession(string sessionId, string cwd)` → `{ sessionId, cwd, mcpServers: [] }`.
3. `AcpOutcome.cs`: `AcpAsk(…, string Session = "")`; `AcpEnd.SessionLost`.
4. `AcpClient`: `OpenedAsync` → for a load, `CallAsync("session/load", …)` inside a `try` that turns an
   `AcpAgentException` into `Ended.Of((AcpEnd.SessionLost, $"the agent could not load session {id}: {message}"))`; then the
   mark; then the model step per decision 5; then `PromptedAsync` unchanged.
5. `AskAcpMode`: read `--session` into the `AcpAsk`; `SessionRefusal` joins `Refusal`'s list (`:204-205`); `EndWord` and
   `EndExit` gain the new end.
6. `Program.cs:2441`: `[--session <id>]` in the `--ask-acp` line.

**SCENARIOS** (classes `AskAcpModeTests`, `AcpClientTests`, `AKimiShimScenarioTests`; a stub returning today's behaviour first).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.2.1 `ALoadTurn_SendsSessionLoad_WithTheSameCwd_AndNoMcpServer` | a fake agent; `--session s-1`, `--cwd <tree>` | one turn | the agent's log holds `session/load {sessionId: s-1, cwd: <full path of tree>, mcpServers: []}` and NO `session/new`; then `set_config_option`, `session/prompt`; report `sessionId` is `s-1`, exit 0 | *Expected the agent's log to contain a "session/load" request, but found "initialize", "session/new", "session/set_config_option", "session/prompt", "session/prompt".* (the flag is ignored today) |
| S5.2.2 `AReplayedHistory_IsNeverTheAnswer` | `replay: ["OLD ANSWER"]`, the prompt answered `NEW` | one turn with `--session` | report `answer` is exactly `NEW` | *Expected report answer to be "NEW" with a length of 3, but "OLD ANSWERNEW" has a length of 13.* (against a stub that loads but does not mark) |
| S5.2.3 `AnUnknownSession_IsSessionLost_Exit70_NothingPrompted` | `loadable: false` | one turn with `--session s-9` | exit 70; `end: session_lost`; the reason names `s-9`; the log has no `set_config_option` and no `session/prompt` | *Expected report end to be "session_lost", but found "agent_failed".* |
| S5.2.4 `ALoadRefusedForTheSignIn_IsStillNotSignedIn` | `loadable: false`, `loadError: -32000 "Authentication required"` | one turn with `--session` | exit 77, `failure: not_signed_in` — the known failure wins | *Expected exit code 77, but found 70.* (against an implementation that ends `session_lost` before the diagnosis) |
| S5.2.5 `AMalformedSessionId_Is65_BeforeAnyProcess` (Theory: `a b`; `../x`; a 101-character id; an id with a newline) | — | `AskAcpMode.RunAsync` | exit 65, the sentence names `--session`; the fake agent's log file does not exist | *Expected exit code 65, but found 70.* |
| S5.2.6 `TheModelOnALoad_FollowsWhatTheLoadAnnounced` (ONE row survives E5.1 (a); delete the other) | (i) `loadModels: ["kimi-code/k3-256k"]`, asked `kimi-code/k3`; (ii) no `loadModels`, asked `kimi-code/k3` | one turn with `--session` | (i) exit 65, `end: model_not_offered`, no `set_config_option`; (ii) `set_config_option` sent with `kimi-code/k3`, exit 0 | (i) *Expected exit code 65, but found 0.* (ii) *Expected exit code 0, but found 65.* (against the other branch) |
| S5.2.7 `ALoadedTurnsUsage_IsTheSessionTotal_AsKimiSaidIt` | `usageReply: "Session total: 40004 input, 72 output"` | one turn with `--session` | `usage.tokensIn 40004`, `tokensOut 72`, `notCaptured false` — the shim reports the total; the delta is the parent's (E5.4) | (green once S5.2.1 is green — pins that a load path does not lose `/usage`) |
| S5.2.8 `WithoutSession_TheTurnIsByteForByteToday` | a script used by an existing scenario | one turn, no `--session` | the agent's log is identical to the log recorded before this story (a golden file under the test's folder) | (green — pins decision 6's "nothing else moves") |
| S5.2.9 `ALoadedTurn_RefusesEveryPermission_AndServesOnlyItsCwd` | after the load the agent asks `Write` permission and reads an absolute path outside the cwd and `AGENTS.md` inside it | one turn with `--session` | the permission refused (`reject_once`), the outside read and the excluded name refused, not quoted | (green — pins M-K5 on the shim's own load path) |
| S5.2.10 `TheTreeHold_IsTakenForALoadTurnToo` (`AKimiShimScenarioTests`, the S3.14 shape) | a Kimi-shaped profile on an owned root in the test's folder, `--locks` the test's folder | one turn with `--session` | the agent's `envFile` was written while both tree-hold files probed `Held`; both `Free` within 5 s after the exit | (green — pins that the load path is under the hold) |
| S5.2.11 `TheHelpNamesSession` | — | `coai-mcp --help` | the `--ask-acp` line contains `[--session <id>]` | *Expected help to contain "[--session <id>]".* |
| S5.2.12 `EveryEnd_HasItsOwnWordAndExit_NoneFallsToADefaultArm` (decision 9) | every value of `AcpEnd` (`Enum.GetValues`) | `EndWord`, `EndExit` | the words are pairwise distinct; `cancelled` only for `Cancelled`; `SessionLost` → `session_lost` and 70; 69 only for `NotStarted`, `Deadline` and `Cancelled` | *Expected EndWord(SessionLost) to be "session_lost", but found "cancelled".* then *Expected EndExit(SessionLost) to be 70, but found 69.* (against the enum value added without its arms) |

**Done when** every scenario is green, the whole C# suite is green, `dotnet format --verify-no-changes` is clean, and no exit
path returns 64.

## E5.3 — The owned homes: a conversation's home kept across turns, and every one-shot Kimi home disposed junction-first

`model: opus` spec plus a `fable` **security judgement** (given 2026-10-16, below) · **built on Fable** (authentication/security,
§0.2) · Opus reviews. It is not a cornerstone; the quota stands, see §0 and J1. Below are the judge's decisions (a–i), Fable's two
additions (j–k), Fable's judgement (R1–R11, which amends g and d and confirms the rest), and the scenario set — the judge's minimum
(S5.3.1–S5.3.14) and the judgement's rows (S5.3.15–S5.3.20). Anchors verified on `origin/main` 02ffd326.

**Goal.** A Kimi conversation keeps ONE owned home across all its turns, in its own private folder. The home is made with its
junction at the first turn and validated before every turn. It never waits for itself. Its turns run one at a time and
share the sign-in gate, the tree hold and the foreign-`kimi` wait with every other Kimi launch. It is deleted junction-first,
never under a live tree: by `close_consult`, by the D20 sweep, and by nothing else. Every ONE-SHOT Kimi home (a question row, a
consultant check) is disposed the same way by its owner, and by the six-hour sweep after a crash.

**Files and anchors (verified).**
- `src_mcp/src/Server/Consultation/ConsultantPlaces.cs:126-136`, `:143-174` (`PrivateFolder.Open` `:146`), `:203-222`
  (`VibeHomeTurns.TakeAsync(turned.Folder)` `:205` — the folder's in-process turn, held until the place is disposed),
  `:225-245` (the `coai-turn.lock`), `:264-280` (`Placed`: `Folder` = the parent of `tree`).
- `src_mcp/src/Server/Consultation/ConsultationService.cs:667-668` (`using var place` across the whole turn), `:824`.
- `src_mcp/runners/Vibe/VibeHomeTurns.cs:21-36` (`TakeAsync`: keyed by the full path), `:115` (`SemaphoreSlim(1, 1)` — not
  re-entrant), `:46-61` (`TryTake`).
- `src_mcp/runners/Kimi/KimiLaunch.cs:73-91` (the home's turn on `places.OwnedRoot`, `:81`; the gate `:98`).
- `src_mcp/runners/Kimi/KimiRuntime.cs:79` (the root is `KimiHome.HomeFor(OwnedScratch, provider, role)`).
- `src_mcp/runners/Kimi/KimiHome.cs:46` (`Prefix = "kh-"`), `:73` (`Made`: writes only on a fresh home), `:97` (`Refusal`),
  `:117-118` (`HomeFor`), `:125-130` (`Dispose`), `:137-144` (`DisposeAllUnder`: the folder ITSELF when it holds `home/`, then
  every `kh-*` child; steps (1)–(3) only, E4 deviation 21).
- `src_mcp/src/Server/Consultation/ConsultantHomeSweep.cs:77` (`RemoveNow`), `:202-232` (`Removed`), `:243-254`
  (`KimiHomesDisposed`), `:280` (`AllTaken`: the folder and every sub-folder, no wait).
- `src_mcp/runners/Vibe/VibeHomes.cs:36-93` (`ReserveOneShot`), `:113-149` (`SweepOneShot`), `:187-204` (`RemovedUnderItsLock`:
  the bare deleter per entry), `:229-233` (`HomeIo.Real`), `:269-298` (`OneShotHome`; `Dispose` `:279-297` gives the lock back
  FIRST, then deletes with `GitScratch.DeleteEvenIfReadOnly`).
- `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:101` (the reservation), `:132-136` (`finally`: `Remove(scratch)`,
  `Release(homes)`), `:140-150`, `:390-416` (`RemoveScratch` → the bare deleter).
- `src_mcp/src/Server/Consultation/ConsultantCheck.cs:211` (the reservation), `:221-227` (`finally`),
  `src_mcp/src/Server/Consultation/ConsultCheckScratch.cs:107` (`Delete` → the bare deleter).
- `src_mcp/src/Server/PanelService.cs:1467-1489` (`DeleteIfDue` and its `KimiHomesDisposed` — the six-hour sweep's model for
  the one-shot sweep).
- Tests to mirror: `ConsultantHomeSweepTests` (S2.39's Kimi arm), `TheScratchSweepNeverDeletesAKimiHomeUnderALiveTreeTests`,
  `TheRoundsScratchDisposesItsKimiHomeJunctionFirstTests`, `VibeHomesTests`, `KimiHomeTests`, `KimiLaunchControlTests` (`FakeKimi`
  `:40`).

**Decisions (the judge's; Fable may amend with its reason).**
- **a. The conversation's Kimi root is `<folder>\kh-<10 hex>`, never the conversation folder itself.** The evidence: the place
  holds the FOLDER's in-process turn for the whole turn (`ConsultantPlaces.cs:205`, released only when the place is disposed after
  the launch has been read, `ConsultationService.cs:667`). `KimiLaunch.TakeAsync` then takes the turn of `places.OwnedRoot`
  (`KimiLaunch.cs:81`). The turns are one `SemaphoreSlim(1, 1)` per full path (`VibeHomeTurns.cs:115`). If the root were the
  folder — what E4.2 decision 8 assumed for "S5's conversation folder" — the launch would wait for a turn its own caller holds,
  until the turn's deadline cancels it. Vibe never met this, because its home is `<folder>\h` (`VibeRuntime.cs:184`). The cure
  costs nothing: `KimiRuntime.Build` already roots the home at `KimiHome.HomeFor(OwnedScratch, …)` (`KimiRuntime.cs:79`), and
  `OwnedScratch` is the folder (`ConsultationService.cs:824`). `DisposeAllUnder` already visits `kh-*` children (`KimiHome.cs:137-144`),
  and the sweep's `AllTaken` already takes every sub-folder's turn (`ConsultantHomeSweep.cs:280`). E4.2 decision 8 is superseded
  for the conversation; `DisposeAllUnder`'s "the folder itself" branch stays, harmless.
- **b. Made once, through the private folder.** `ConsultantPlaces.OwnedAsync` opens `coai-kimi` through `PrivateFolder`
  (`:146`) and makes the conversation folder. `KimiLaunch` makes `kh-…\home` (the junction, coai's `config.toml`) and `os\` at the
  FIRST turn, because the home is fresh (`KimiHome.Made`, `:73`). From the second turn on, `Made` writes nothing, and the
  validation inside the turn judges what is there.
- **c. Validated before every turn by the one control, and nothing new.** The executor asks `KimiRuntime.TakeTurnAsync` before
  every launch (`KimiRuntime.cs:139-149`). The junction must still read back as the person's folder, `config.toml` must be byte for
  byte coai's render, and `os\` must be empty (D19). **A re-login between two turns** can change the person's OAuth slot. Then
  turn 2 is refused with D19's sentence ("…your Kimi sign-in changed since the home was made (a re-login)") and its cure. For a
  conversation the cure is `close_consult` and a new consultation (the old session is lost with its home). The record stays open
  until then, and the turn's failure is `launch-refused` (D5 of the Vibe fixes). Re-rendering the config instead is an open
  question for the owner (§"Open questions" 2), not this story's change.
- **d. The lock order, written down.** A conversation turn takes: the folder's in-process turn → the folder's `coai-turn.lock`
  (`ConsultantPlaces`) → (the snapshot refreshed in place) → the `kh-…` root's in-process turn → the sign-in GATE (`KimiLaunch`)
  → (in the shim) the foreign-`kimi` wait → the tree holds. A one-shot launch takes `coai-vh\<8>\coai-turn.lock`
  (`ReserveOneShot`), then from the root's turn on, the same. The sweeps take the folder lock and every turn with NO wait and never
  the gate. No cycle exists, because nothing that holds the gate ever asks for a folder lock or a folder turn.
- **e. Shared with the reviewers, by construction.** The consultant's invocation carries `KimiRuntime` as its adapter, so its
  launches take the SAME gate file (`KimiCredentialsLock.PathFor(<person's credentials>)`) as a round's Kimi reviewers. The shim
  that the consultant starts is the same shim, with the same tree hold and foreign wait (E4 decision 74). The gate's wait is
  inside the turn's timeout (D19). A consult turn that waits behind a three-reviewer Kimi round can time out. That turn is then
  `interrupted` and resumable, as any killed turn is. This is stated in the help (E5.7), not hidden.
- **f. Deleted only by `close_consult` and D20's sweep, and both are junction-first already.** `RemoveNow` goes through
  `Removed` (`ConsultantHomeSweep.cs:77`, `:202`), which runs `KimiHome.DisposeAllUnder` under the folder lock and every turn, and
  KEEPS the folder on a `Stopped` (`:243-254`). This story adds no code there. It adds the CONVERSATION-shaped tests (a `kh-*`
  child beside `tree\`, the stamp and the lock). The tests that exist plant the reviewer shape or the folder-as-root shape.
- **g. A one-shot home is disposed junction-first by its owner, and by the one-shot sweep.**
  `OneShotHome.Dispose` (`VibeHomes.cs:279-297`) changes its order: (1) `KimiHome.DisposeAllUnder(folder, warn)` WHILE the lock is
  still held; (2) on any `Stopped` (a live tree, or a target that lost a name), give the lock back, KEEP the folder, and warn
  `"the Kimi home in {folder} is kept: {reason}; the scratch sweep takes it once its tree has ended"`; (3) otherwise give the lock
  back and delete as today. `VibeHomes.SweepOneShot`'s `RemovedUnderItsLock` (`:187-204`) runs `DisposeAllUnder` under the lock it
  took and skips the folder this pass on any `Stopped`, warned — the rule of `PanelService.DeleteIfDue` (`:1467-1489`).
  **The root-less fallback** (settings built by hand, so the home is in the row's or the check's own scratch):
  `QuestionRowLaunch.RemoveScratch` (`:393-399`) and `ConsultCheckScratch.Delete` (`:107`) call `KimiHome.DisposeAllUnder` on the
  folder THEY handed to the adapter as `OwnedScratch`, before the bare deleter. This is E4.1 decision 4: the callers that KNOW
  they hold a Kimi home dispose it through `KimiHome` first. E4.1 named "S5's question row and consultant check"; since #823 their
  homes live in a `OneShotHome`, so that is where the order goes. The in-deleter check (E4.1) stays the second belt.
- **h. No path budget for Kimi** (J3). One test proves that coai's own walk handles a home with a data path past 260 characters
  (S5.3.11).
- **i. What is NOT changed:** `KimiHome`'s validation and its admitted names, `KimiLaunch`'s order, the gate, the tree hold,
  D20's rules. **Renames are proposed, not done:** `VibeHomes`/`OneShotHome`/`coai-vh` and `VibeHomeTurns` now serve both CLIs
  (`OneShotHomes`, `OwnedHomeTurns` would say so). A rename touches the sweep, `ConsultantPlaces` and the D7 structural test for a
  name alone (E4 deviation 31). The operator decides.
- **j. (Fable, 2026-10-16) Where the sign-in REALLY is, re-asked before every turn.** `Junctions.Refusal`'s `Resolved` — the person's
  `credentials` must resolve, through every ancestor link, to a local drive (`Junctions.cs:179-189`) — runs only when the junction is
  MADE (`Make`, `:114-118`): on turn 1, never again, because `KimiHome.Made` writes nothing on a home that has its junction (`:85`,
  `:164-165`). Per turn the code re-checks the SPELLING of the person's home (`KimiLaunch.LocalHome`, `:149-152`) and the junction's
  one-level target (`KimiHome.Credentials`, `:221-233`), and then KEYS A LOCK on the final path (`KimiCredentialsLock.KeyFor`,
  `:116-121`) — a UNC final path gets a key, not a refusal. `KimiLaunch.Prepare` adds, right after `LocalHome`, the resolved check on
  `places.Credentials`, refused with `Resolved`'s own sentence BEFORE any lock (reuse-first, move 1: `Junctions.ResolvedRefusal(target)`
  becomes the public face of `:179-189` — one sentence, two callers). Judgement R8 says why it is worth one handle open. S5.3.15.
- **k. (Fable, 2026-10-16; the plan gate's finding (c)) How turns never renew the sign-in concurrently — stated.** Every coai Kimi
  launch on one sign-in — this conversation's turn, another conversation's, a round's reviewer, a question row, a check, on THIS server
  or on another server of the same machine — passes `KimiLaunch.TakeAsync` and takes the sign-in GATE `<key>.lock` (`:82`, `:98`;
  `KimiCredentialsLock.TakeAsync` `:134-156`): a FILE under `kimi-locks`, keyed by the credentials folder's identity (`:116-121`), held
  from before the version probe until the launch has been read (`Attempt`, `:264-284`; `ReviewerExecutor.cs:921`). Inside it the shim
  takes the sign-in's TREE HOLD, inheritable (`KimiShimHold.cs:40-46`; `KimiCredentialsLock.cs:186-191`), so a tree that outlives its
  server still holds the sign-in; and before both, the shim waits for the person's OWN `kimi` (`KimiShimHold.cs:29-35`,
  `ForeignKimi.cs:62-74`). So at most ONE `kimi` that coai started runs on one sign-in at any moment, across conversations, rounds and
  servers: the peak is 1, and a second turn waits inside its own deadline (decision e). S5 adds nothing to this; S5.3.16 pins it at
  the service level, and pins that a gate another process holds is waited for.

**Fable's security judgement (2026-10-16).** Verified on `origin/main` 02ffd326; every anchor below was read, not quoted. The
consultant's pointers (consult 95f08e0b, option B) — `ConsultantHomeSweep.cs:202`, `KimiHome.cs:312`, `VibeHomes.cs:279` (the owner
releases its lock BEFORE deleting) and `GitScratch.cs:57` — are confirmed and are R2, R4, R5 and R4 below.

- **R1 — The owned root is never a folder whose turn the caller holds (decision a confirmed; E4.2 decision 8 superseded).**
  `ConsultantPlaces.HeldAsync` takes `VibeHomeTurns.TakeAsync(turned.Folder)` (`ConsultantPlaces.cs:205`) and the place keeps it until
  the launch has been read (`ConsultationService.cs:667-668`); `KimiLaunch.TakeAsync` takes `VibeHomeTurns.TakeAsync(places.OwnedRoot)`
  (`KimiLaunch.cs:81`); the turns are one non-re-entrant `SemaphoreSlim(1, 1)` per full path (`VibeHomeTurns.cs:18`, `:115`). Root ==
  folder → the launch waits for its own caller until the token cancels, and the person sees a timeout, not a sentence. The cure is in
  the code: `KimiRuntime.Build` roots at `KimiHome.HomeFor(OwnedScratch, …)` = `<folder>\kh-<10 hex>` (`KimiRuntime.cs:79`), and
  `OwnedScratch` is the folder (`ConsultationService.cs:824`). Review item (3) is the guard; S5.3.1 the pin.
- **R2 — The lock order, and the ONE rule that makes it safe: every DISPOSER takes with no wait; every ACQUIRER waits only on something
  no disposer holds (decision d amended with this rule).** The acquirer's chain for one conversation turn: (A) the folder's in-process
  turn (`:205`, waited under the token) → (B) the folder's `coai-turn.lock` (`:227`, at most `LockWait` 10 s) → the snapshot → (C) the
  `kh` root's in-process turn (`KimiLaunch.cs:81`) → (D) the sign-in GATE (`:82`, `:98`; one-second slices under the token,
  `KimiCredentialsLock.cs:147-155`) → the launch; in the shim: (E) the foreign-`kimi` wait (`KimiShimHold.cs:29`) → (F) the sign-in's
  tree hold (`KimiCredentialsLock.cs:186-191`, within the deadline) → (G) the per-home `coai-tree.lock` (`:198-210`, `HomeWait` 500 ms,
  `:72`). D, then C, are given back when the launch has been read (`Attempt`, `KimiLaunch.cs:264-284`; `ReviewerExecutor.cs:921`); B
  and A when the place is disposed (`:667`). The disposers: `ConsultantHomeSweep.Removed` takes B with `HeldFile.Take` — no wait
  (`:207`) — then A and every sub-folder's turn with `TryTake` — no wait (`:214`, `:280-293`) — then PROBES G (`KimiHome.Alive`
  `:339-346` → `HeldFile.Probe` `:85-86`: one exclusive open, let go at once); it never takes D, E or F. `VibeHomes.RemovedUnderItsLock`
  takes the one-shot folder's lock with no wait (`:189`). So the sweep's order B → A is the REVERSE of the acquirer's A → B — safe only
  because the sweep never waits: it holds B for the length of one failed `TryTake` and lets go (`using (locked.Stream)`, `:212-226`).
  Nothing that holds D asks for A, B or any folder lock; nothing that holds F or G asks for anything but the agent's start. No cycle. Two
  meetings the order leaves, and how each ends: (i) a shim under D that waits for F held by ANOTHER server's tree waits within its own
  deadline and exits 69 (`KimiShimHold.cs:53-54`), and its server then gives D back — bounded; (ii) a disposal's probe of G and a shim's
  take of G can meet — the shim retries for 500 ms (`:72`, `:201`), longer than one open; a disposal that meets a shim's take sees `Held`
  and keeps the home — the safe direction. **Rule for every coder of this story:** a new wait is added only on the acquirer's side, and
  only on something no disposer ever holds; a disposer that WAITS turns a reversed order into a deadlock.
- **R3 — PrivateFolder's held handle is not a lock in this order, and must not become one.** `PrivateFolder.Open` holds the `coai-kimi`
  (or `coai-vh`) folder's handle sharing read and write but never delete (`PrivateFolderWindowsNative.cs:45-53`, `:172`): two holders
  coexist — the place (`ConsultantPlaces.cs:146-152`), every sweep pass (`ConsultantHomeSweep.cs:105-115`, `:140-150`), every
  reservation (`VibeHomes.cs:60-66`) and the one-shot sweep (`:137-147`) — and what it denies is a RENAME or DELETE of the homes folder
  itself while anyone has a place or a pass in it. It never waits and never blocks a child's deletion. Rule: it is held by exactly two
  owners (the place's held list, the reservation's `Pins` `:96-105`) and by a pass for the pass's length; never across an `await` outside
  those.
- **R4 — A stop propagates by VALUE, the walk goes on, and every caller turns ONE stop into "keep the whole folder".** `KimiHome.Dispose`
  returns the `Stopped` and deletes nothing more (`:127-129`); `DisposeAllUnder` warns each stop and continues with the next `kh-*`
  (`:143`, `:152-160`) — in a folder with a live `kh-a` and a dead `kh-b`, `kh-b` is unlinked and `kh-a` kept, and the caller sees one
  stop and keeps the folder: `ConsultantHomeSweep.KimiHomesDisposed` (`:243-253`) returns false before `DeleteAllBut` (`:220-225`);
  `RoundScratch` (`:142-151`); `PanelService.DeleteIfDue` (`:1467-1472`). The stops, each a sentence: the root or `home/` a link
  (`:333-336`); a live tree — `Held` AND `Unknown` (`:339-346`); an unlink that threw (`:356-359`); a link still there after its unlink
  (`:368-371`); a target that lost a name after the 200 ms re-list (`:373-377`; `GitScratch.cs:184-196`); the deleter's
  `LinkTargetLostException` or IO failure (`:394-403`). What `DisposeAllUnder` leaves to the caller's deleter (steps (1)–(3) only, E4
  deviation 21) holds NO link any more, and the deleter is still the second belt: its own `Unlink` verifies the target and throws
  (`GitScratch.cs:57-62`, `:116-133`, `:164-177`). **Rule for g:** the owner's `Dispose` and `SweepOneShot` give the same two-valued
  answer — a stop keeps the folder AND its lock file (never delete the lock file of a kept folder: `DeleteAllBut` keeps it `:225`,
  `RemovedUnderItsLock` deletes it last `:196-202`), and the warn carries the stop's own sentence, unparaphrased.
- **R5 — Decision g, step 3, OVERRULED in part: delete UNDER the lock; give the lock back; THEN the lock file and the folder — the shape
  `RemovedUnderItsLock` already has (`VibeHomes.cs:187-205`), extracted and shared, never copied.** As written, g gives the lock back
  and then deletes the whole folder, as today's `Dispose` does (`:286-291`). Between the release and the delete, a `SweepOneShot` pass —
  this server's or another's on the same root — can take the lock (`:189`) and run its own `DisposeAllUnder` and deleter on the same
  folder: two deleters on one tree. Not a sign-in risk — in both orders the junction is gone before the release, and both deleters verify
  the target by name — but a race that ends in an IOException the owner catches as "could not be deleted; the scratch sweep takes it"
  (`:294-297`) for a folder the sweep just took: noise that WILL be read as a loss once. The order: under the lock, `DisposeAllUnder`; on
  a stop, release and keep (R4); otherwise every entry but the lock file, under the lock; then the lock and the pin together (`Pins`); then
  the lock file and the folder — and "the folder is already gone" is silent, only a failure is said. ONE helper for both callers
  (reuse-first, move 2: the shared half of `RemovedUnderItsLock`). Cost: none — same entries, their order. S5.3.20.
- **R6 — The row and the check fallbacks: the check's home is one level DOWN.** `QuestionRowLaunch` hands `OwnedScratch = scratch` — the
  row's own folder when no root is configured (`HomeFolderOf`, `:140`) — so `kh-*` is a direct child and `DisposeAllUnder(scratch)` finds
  it. `ConsultantCheck` hands `OwnedScratch = place.Folder` = `<scratch>\own` (`:230-233`, `:262`, `:285`), but `ConsultCheckScratch.Delete`
  is called with the scratch ROOT (`ConsultantCheckMode.cs:307`): `DisposeAllUnder(root)` would look for `kh-*` among the root's direct
  children, find none, and leave the junction to the bare deleter — which runs NO live-tree probe. **The brief disposes
  `Path.Combine(root, ConsultantCheck.OwnName)` and the root (the second costs nothing).** S5.3.10's check arm plants exactly
  `coai-check-…\own\kh-x`, so a `DisposeAllUnder(root)` fails it — keep that shape. Two more facts: `ConsultCheckScratch.Delete` catches
  `LinkTargetLostException` as a plain IO failure (`:116-119`; it IS one, `LinkTargetLostException.cs:9`) — the sentence reaches the
  warn, enough; `QuestionRowLaunch.TryRemove` names it apart (`:408`). And in both fallbacks the home's `coai-tree.lock` lives inside the
  scratch under TEMP, so a scratch kept under a live tree is finished by the six-hour scratch sweep, which asks the same probe
  (`PanelService.cs:1467-1489`). Nothing else is needed.
- **R7 — The separate `kh-*` root (confirmed), and what it buys beyond R1.** The conversation folder holds `tree\` (a snapshot: no links,
  refreshed in place), the stamp and the lock — all coai's; the `kh` root holds the ONLY junction. `DisposeAllUnder`'s "itself" branch is
  dead for this shape (`Directory.Exists(folder\home)` is false, `:139`) — harmless, as decision a says. `AllTaken` takes `tree`'s turn too
  (`:282`): nobody holds it, it costs nothing. `LastWrite` reads the folder's DIRECT entries (`:300-308`): the stamp moves on every
  `Begin`/`End` (`ConsultantPlaces.cs:250`, `:258-260`), so an open conversation is judged by its turns, never by a Kimi write deep in
  `kh-x` that moves nothing above it — right. The reviewer's `kh-*` under a round's scratch is the same shape (`RoundScratch.cs:142-151`),
  so one `DisposeAllUnder` serves three callers.
- **R8 — Re-validating between turns: what is re-checked, what is not, and the one addition (decision j).** Before any lock, per turn:
  the person's home by SPELLING (`KimiLaunch.LocalHome`, `:149-152`), the credentials folder's existence (`:176-179`), the slot re-read and
  the config re-rendered (`:168-170`, `:181-185`). Inside the turn, `KimiHome.Refusal`: the junction is a link whose ONE-level reparse
  target spells the person's `credentials` (`:221-233`; `Junctions.Target` never chases, `:122-134`); `config.toml` byte-equal to the
  fresh render (`:235-239`); `os\` empty; every other name admitted or refused; no link inside (`:281-301`). The three cases: **(1) a
  sign-out and sign-in that keeps the folder** — the junction's path is unchanged and the gate's key is the folder's final path
  (`KimiCredentialsLock.cs:116-121`), also unchanged; the slot's VALUES decide: same values → the turn runs on the new token (Kimi's own
  refresh, under the gate); changed values → refused with the re-login sentence (decision c, S5.3.4). **Refusing is right:** a re-render
  would make coai WRITE into a home between turns, which `Made`'s fresh-only rule (`:85`, `:164-165`) exists to forbid; open question 2
  stays the owner's. **(2) a sign-out that removes the folder** — refused before any lock (`:176-179`); the junction dangles; its
  disposal reads the target as `Unread` → unlinks → nothing missing → `Clean` (`GitScratch.cs:142-143`, `:207-208`) — nothing is said
  about a folder that is not there, right. **(3) the target SWAPPED — an ancestor of `~\.kimi-code\credentials` replaced by a link** —
  the resolved check ran at make time only (decision j); `KeyFor` keys a lock on wherever the chain leads, a network path included. A
  same-user process is the only one that can do this (E4.2 decision 11's reasoning holds), but a conversation home lives for hours where
  a reviewer home lives minutes, every turn reads the target's entry NAMES (`Look`, `GitScratch.cs:142-143`) and a network target would be
  listed on every disposal; the check costs ONE handle open that `KeyFor` already pays. Hence decision j, before the gate, so a refused
  turn makes no file under `kimi-locks`. S5.3.15.
- **R9 — PrivateFolder's DACL over a junction INSIDE it: three facts and one pin.** (1) The walk after a re-ACL never enters or touches a
  reparse point (`PrivateFolderWindows.cs:175-179`), so coai's walk never re-ACLs the person's `credentials`, and a junction's OWN entry
  is left as it is (an explicit ACE on the junction object is not reset — a limit, harmless: the object carries no data). (2) The protected
  DACL is applied through the folder's handle with `SetSecurityInfo` (`PrivateFolderWindowsNative.cs:79-91`), its entries inheritable
  (`PrivateAcl.cs:62`, `:74-75`): Windows propagates to descendants itself, and the code's claim that propagation "does not follow one
  either" (`PrivateFolderWindows.cs:152`) is a claim about Windows pinned by no test here. If it were false, a re-ACL of `coai-kimi` would
  replace the DACL on the person's sign-in folder and its files with coai's three entries — narrower, not a leak, but a write into the
  person's folder that coai promised never to make. (3) The re-ACL runs only when the folder's DACL is wrong (`:122-123`); `coai-kimi` is
  created atomically WITH the DACL (`:68-71`), so this is the migration path and the wide-root path, not every open — and it runs INSIDE
  `PrivateFolder.Open`, before any place or turn of this process exists; another server's live home in the same `coai-kimi` is a real folder
  with a junction — walked, never entered. **The pin is S5.3.17** (Windows-only, beside `PrivateFolderWindowsTests`).
- **R10 — Owner/sweep races, enumerated: what each side holds, who wins, what is never lost.** (i) `close_consult` during a live turn:
  `RemoveNow` → `TakeTurn` is `Busy` → false → left to the beat (`:74-75`, `:207-210`); the record is closed; the beat removes the folder
  once free (record over → due). S5.3.19. (ii) The D20 sweep against a turn starting: the sweep holds B for microseconds; a place waits
  for B at most 10 s (`:227`); the sweep's `TryTake` of A fails while the place holds it → skip. (iii) Two servers' sweeps on one folder: B
  is cross-process, one wins, the other skips; a folder of ANOTHER store is judged by age alone (`:185-187`) — never removed inside
  `ForeignAge` while its owner's conversation is open. (iv) A one-shot owner against `SweepOneShot`: the owner holds the lock for its life
  (`:85`, `:92`); the sweep takes with no wait (`:189`) → skip; after R5 the only window is between the owner's release and its delete of
  the lock file and folder, where a sweep may take the lock and delete the (now empty) folder first — the owner's last delete then finds
  it gone and says nothing. (v) A server crash mid-turn: the kernel releases A (process memory), B and D (non-inheritable handles,
  `HeldFile.cs:47-61`); F and G stay HELD by the orphan `kimi` (inheritable, E4.2 decision 10) — so every disposer's probe of G says `Held`
  and the home is kept until `kimi` ends. That is the whole reason G exists; S5.3.8 and S5.3.18 pin it in both shapes. (vi) The sweep at a
  new server's start is (v) again — after the records, so a lapsed record takes its home with it.
- **R11 — Nothing of the sign-in in what this story writes.** S5.3.14 (E4.2 decision 12) stands. The collectors this story adds: the
  one-shot owner's and the one-shot sweep's warns carry `Stopped` sentences, which carry PATHS of the owned tree and the entry NAMES the
  target lost (`:377`) — names of the person's credential files, never bytes, the bound E4.1's `Look` already set. The sentence of decision
  j names the resolved path of the sign-in — a path, acceptable under the same bound. No new collector reads a file of the person's.

**SCENARIOS — the judge's minimum set (S5.3.1–S5.3.14) and the judgement's rows (S5.3.15–S5.3.20).** Every fake person's home is
`KimiFixture`'s, inside the test's `testsToDelete-kimi-…` folder; the homes root is a second `testsToDelete-` folder. No test makes a
junction to anything outside its own folders; a network path is a SEAM's answer, never a real target.

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.3.1 `AConversationTurn_StartsOnItsKhRoot_AndNeverWaitsForItself` | a place from `ConsultantPlaces.ForConversationAsync` for a Kimi consultant (held); the launch built with `OwnedScratch = place.Folder`; `FakeKimi` | `KimiRuntime.TakeTurnAsync` with a 5 s token | `Holding` within 5 s; `--home` is `<folder>\kh-<10 hex>`; `VibeHomeTurns.Held(<folder>\kh-…)` true while held | against a root of the folder itself: *Expected the launch turn to be taken within 5s, but TakeTurnAsync was cancelled waiting for the turn of …\coai-kimi\<id>.* |
| S5.3.2 `TheFirstTurnMakesTheHome_TheSecondWritesNothing` | turn 1 taken and given back | turn 2 on the same conversation | after turn 1: `home\credentials` a junction to the fake `credentials\`, `config.toml` coai's bytes, `os\` empty; the home's file list and bytes (with no junction entered) are the same before and after `Made` of turn 2 | (green — pins `Made`'s "fresh only" on the conversation path) |
| S5.3.3 `AJunctionRetargetedBetweenTurns_RefusesTurnTwo_BeforeAnyProcess` | after turn 1, `credentials` retargeted to the test's `persons2\credentials` | turn 2 | refused, naming `credentials points at …persons2…`; `FakeKimi` started nothing; both fake person's folders keep their files | (green — pins D19's per-launch check where S5 relies on it) |
| S5.3.4 `AReLoginBetweenTurns_RefusesWithTheCure_AndTheConversationStaysOpen` | the fake person's slot `key` changed after turn 1 | turn 2 through `ConsultationService` | the turn fails `launch-refused` with the re-login sentence; the record is still open; `close_consult` then removes the folder junction-first | (green on the refusal; the record's state is pinned) |
| S5.3.5 `CloseConsult_RemovesAConversationShapedKimiHome_JunctionFirst` | `coai-kimi\<id>\` holding `tree\`, the stamp, `coai-turn.lock` and `kh-x\home\credentials` (a junction to the test's `persons\`); `GitScratch.AfterUnlink` a recorder | `ConsultantHomeSweep.RemoveNow` | the folder gone; the recorder NEVER invoked (`KimiHome` unlinked the junction first); `persons\` keeps its files, bytes and `ReadOnly` | (green on HEAD — pins the `kh-*` shape no test has planted yet) |
| S5.3.6 `TheSweepSkipsALiveConversationsKimiHome` | the same folder past the idle window; the `kh-x` root's in-process turn held | `ConsultantHomeSweep.Sweep` | kept; nothing unlinked; removed on the next beat once the turn is given back | (green — pins `AllTaken` over `kh-*`) |
| S5.3.7 `AOneShotHome_IsDisposedJunctionFirst_UnderItsLock` | a `OneShotHome` (reserved under the test's root) holding `kh-x\home\credentials` → the test's `persons\`; `GitScratch.AfterUnlink` a recorder | `Dispose()` | the folder gone; the recorder never invoked; `persons\` intact | *Expected the deleter's unlink recorder not to be invoked, because KimiHome unlinks the junction first, but it was invoked with "…\kh-x\home\credentials".* |
| S5.3.8 `AOneShotHomeUnderALiveTree_IsKept_AndTheSweepTakesItLater` | as S5.3.7, with `kh-x\coai-tree.lock` held by the test (a plain `HeldFile.Take`, not inheritable) | `Dispose()`; then the hold released; then `SweepOneShot` with a cutoff in the future | after `Dispose`: the folder exists, `credentials` is still a link, the warn holds "kept" and "still running"; after the sweep: gone junction-first, `persons\` intact | *Expected Directory.Exists("…\coai-vh\<8>") to be true because a live tree keeps its home, but found False.* |
| S5.3.9 `SweepOneShot_StopsOnAKimiStop_AndSaysWhy` | an old `coai-vh\<8>` whose `kh-x` loses a target file through `KimiHome.AfterUnlink` | `SweepOneShot` | 0 removed; the warn holds the `Stopped` sentence; the next pass (seam off) removes it | *Expected swept to be 0, but found 1.* |
| S5.3.10 `TheRootlessFallback_DisposesJunctionFirst` (Theory: `QuestionRowLaunch.RemoveScratch`; `ConsultCheckScratch.Delete`) | a scratch (row: `coai-question-…`; check: `coai-check-…\own`) holding `kh-x\home\credentials` → the test's `persons\`; the recorder | the remover | gone; recorder never invoked; `persons\` intact | *Expected the deleter's unlink recorder not to be invoked …, but it was invoked with "…\kh-x\home\credentials".* |
| S5.3.11 `AHomeWhoseDataPathPasses260_IsCheckedAndDisposed` | a made `kh-x` home in the test's folder, plus `home\sessions\wd_tree_<12>\session_<36>\agents\main\wire.jsonl` placed so that the file's full path is over 300 characters | `KimiHome.Refusal`, then `KimiHome.Dispose` | `Refusal` empty; `Dispose` is `Clean(LinkWasThere: true)`; the root gone; `persons\` intact | if .NET's walk failed on the long path: *Expected Refusal to be empty, but found "… could not be read (Could not find a part of the path …)".* |
| S5.3.12 `AConsultTurnAndAReviewerLaunch_OnOneSignIn_NeverOverlap` | a conversation's `kh-a` and a round's `kh-b`, both junctioned to ONE fake `credentials\` | two `KimiLaunch.TakeAsync` in parallel (the S2.27 shape) | the second completes only after the first is disposed; both used `KimiCredentialsLock.PathFor(<fake credentials>)` | (green — pins decision e) |
| S5.3.13 `ThePathArithmetic_IsTheNumbersTheDocumentStates` (pure) | the homes root `C:\Users\u\AppData\Local`, a 32-hex id, a provider `kimi`, the role `consult` | `KimiHome.HomeFor(ConsultantHomes.ConversationFolder(root, ConsultantHomes.Kimi, id), …)` + `\home` | the length is `root.Length + 62`; the one-shot home is `root.Length + 36` (J3's 85 + u and 59 + u at that root) | (green — keeps J3's numbers honest when a name changes) |
| S5.3.14 `NothingOfTheSignIn_AppearsInAConversationsRecords` (the `KimiCanaryTests` shape, E4.2 decision 12) | the two canaries of S2.45; a two-turn conversation through `ConsultationService` with a played shim; one `close_consult` | every collector with its liveness check: the record, the ledger lines, the reply, the warn lines, the folder names | no collector holds either canary; the control arm sees a served canary | (a pin — its red, if ever: *Expected the record not to contain "CANARY-REFRESH-7f3a0c", but found it in …*) |
| S5.3.15 `AnAncestorOfTheSignIn_ResolvingOffTheLocalDrives_RefusesTurnTwo_BeforeAnyLock` (decision j, R8) | turn 1 taken and given back (`FakeKimi`); then the final-path seam answers `\\server\share\.kimi-code\credentials` for the fake person's folder (a seam — no test names a network path for real) | turn 2, `KimiLaunch.TakeAsync` | refused; the sentence holds "resolves to", "not a local drive path" and "D19"; `FakeKimi` started nothing; the test's `kimi-locks` folder gained no file; `VibeHomeTurns.Held(<kh root>)` is false | *Expected the launch to be refused because the sign-in resolves off the local drives, but it was Holding.* |
| S5.3.16 `TwoConversationsOnOneSignIn_RunOneKimiAtATime_AndWaitForAGateAnotherProcessHolds` (Theory; decision k, the gate's finding (c)) | (i) two consultations on ONE fake person; `PlayedKimi` whose shim records its start and end and holds 300 ms; (ii) one consultation; the gate file `KimiCredentialsLock.PathFor(<fake credentials>)` pre-held by the test (a plain `HeldFile.Take`, not inheritable), released after 1 s | (i) both turns started together through `ConsultationService`; (ii) one turn | (i) the peak of live played shims is 1; the second's start is not before the first's end; both answered; both used ONE gate path; (ii) the shim started only after the release, and within 2 s of it | (a pin of decision k — its red if the gate were skipped: *Expected the peak of live Kimi launches on one sign-in to be 1, but found 2.*; (ii): *Expected the launch to start after the gate was released, but it started while the gate was held.*) |
| S5.3.17 `AReAclOfCoaiKimi_NeverCrossesTheJunction_IntoTheSignIn` (R9; Windows-only, beside `PrivateFolderWindowsTests`; the fake shell records every path it is asked about) | a `coai-kimi` under the test's root whose DACL was widened (one extra Allow for Authenticated Users), holding `<id>\kh-x\home\credentials` → the test's `persons\credentials` with one file | `PrivateFolder.Open(root, "coai-kimi")` | `Opened`; the one note says "made … private"; the target folder's and its file's security descriptors are byte-equal to before; no `AclOf` or `ResetToInherit` call names a path under `credentials`; the junction itself was skipped | *Expected the DACL of …\persons\credentials to be unchanged after the re-ACL of coai-kimi, but it now holds the three protected entries.* (if Windows' propagation crossed the junction — the code's unpinned claim, `PrivateFolderWindows.cs:152`) |
| S5.3.18 `TheConversationSweep_KeepsAKhHomeWhoseTreeIsAlive_AndTakesItOnceItEnds` (R10 (v)) | the S5.3.5 folder, its record closed, past the window; `kh-x\coai-tree.lock` held by the test (a plain `HeldFile.Take`) | `ConsultantHomeSweep.Sweep`; the hold released; `Sweep` again | first pass: 0 removed, the junction still a link, the warn holds "still running" and "kept this beat"; second pass: gone junction-first, the recorder never invoked, `persons\` intact | (green on HEAD — pins `KimiHomesDisposed` on the `kh-*` shape; its red if the probe were skipped: *Expected …\kh-x\home\credentials to still be a link because a live tree keeps its home, but it was unlinked.*) |
| S5.3.19 `CloseConsultDuringALiveTurn_LeavesTheHomeToTheBeat` (R10 (i)) | a conversation folder shaped as S5.3.5 with its `coai-turn.lock` held by the test (the turn "runs" in another process) | `close_consult` (`RemoveNow`); then the lock released; then `Sweep` | the record is `closed`; after `RemoveNow` the folder and the junction are untouched and no warn names a loss; after the sweep the folder is gone junction-first | (green on HEAD — pins `:74-75`, `:207-210`; its red: *Expected the credentials junction to be untouched while the turn lock is held by another process, but it was unlinked.*) |
| S5.3.20 `AOneShotOwner_DeletesEveryEntryUnderItsLock_TheLockFileAndTheFolderLast` (R5) | a `OneShotHome` holding `kh-x\home\credentials` → the test's `persons\` and a `vh-…` folder; the per-entry delete seam records, for every entry, `HeldFile.Probe(<folder>\coai-turn.lock)` at the moment it is deleted | `Dispose()` | every entry but the lock file was deleted while the probe said `Held`; the lock file and the folder went last, after the release; the recorder never invoked; `persons\` intact | *Expected every entry of …\coai-vh\<8> to be deleted while its lock was still held, but "kh-x" was deleted after the lock was released.* (today's order, `VibeHomes.cs:286-291`) |

**Review focus (Opus, after Fable's judgement).** (1) `OneShotHome.Dispose`'s new order: `DisposeAllUnder` while the lock is held; on
a `Stopped`, NOTHING is deleted and the lock is given back. (2) `SweepOneShot` never deletes past a `Stopped`. (3) No caller
roots a Kimi home at a folder whose turn it holds (grep the diff for `OwnedScratch` and `HomeFor`). (4) The tests' junctions are
inside their own `testsToDelete-` folders (grep for `Junctions.Make(` and `MakeAsync(`). (5) CC ≤ 4 in every changed method.
(6) `ConsultCheckScratch.Delete` disposes `<root>\own` — not the root alone (R6). (7) The owner's entries go UNDER the lock, the lock
file and the folder last, through ONE helper shared with `RemovedUnderItsLock` — no second copy of the loop (R5). (8) `KimiLaunch.Prepare`
runs the resolved check BEFORE `KimiCredentialsLock.TakeAsync`, so a refused turn leaves no file under `kimi-locks` (decision j). (9) No
new wait on a disposer's side; every new sentence carries paths and names, never a file's content (R2, R11).

**Docs.** `research/module_server.md` (the conversation's Kimi home, the lock order, the one-shot disposal);
`research/module_runners.md` (`OneShotHome`, `SweepOneShot`); `research/module_tests.md` (the rows of this table and the reds seen).

## E5.4 — `KimiConsultant`: the conversation, registered, seeded, and its limitations per side

`model: opus` spec · built on Opus (ordinary, §0.2) · Opus reviews. Needs E5.2 and E5.3 (may start beside E5.3, merges after it).

**Goal.** A Kimi row answers `consult`. Turn 1 creates a session in the conversation's snapshot, on the conversation's home.
Every later turn loads that session with `--session`. Each turn is billed its share of Kimi's cumulative total. A session that
Kimi no longer holds is a dropped conversation. Kimi is a consultant in the shared seed, and the consultant tab says what it
can be kept from reading on each side.

**Files and anchors (verified).**
- New `src_mcp/runners/Kimi/KimiConsultant.cs`, modelled on `src_mcp/runners/Vibe/VibeConsultant.cs` (`Memory` `:35`,
  `UsageIsCumulative` `:38`, `NeedsASanitisedSnapshot` `:50`, `HomesFolder` `:53`, `Toolbox` `:56-58`, `Build` `:60-80`,
  `Cwd` `:83-84`, `SnapshotOf` `:86-91`, `OwnedFolder` `:97-102`, `ReadHandle` `:108-122`, `DroppedTheConversation` `:131`).
- `src_mcp/runners/Consultation/IConsultantRuntime.cs:158` (`Memory`), `:180` (`ReadHandle`), `:183`, `:195`, `:215`, `:227`, `:297`.
- `src_mcp/runners/Kimi/KimiReport.cs:20` (the record — it has no session id), `:38-60` (`Parse`/`From`); the shim prints
  `sessionId` (`AskAcpMode.cs:316`).
- `src_mcp/runners/Consultation/ConsultantResolution.cs:34-47` (`OnACli`; the `vibe` arm `:45`), `:50-55` (`VibeOn`, the shape for
  `KimiOn`); `src_mcp/runners/Consultation/QuestionResolution.cs:25` (`Answering` = `Consulting` ∪ `api`).
- `src_mcp/src/Server/Consultation/ConsultationBilling.cs:33-34`; `src_mcp/core/Consultation/ConsultationUsage.cs:32-40`.
- Seeds: `shared/feature-availability.json:25` (`features.consultant`); generated `src_vs_code/src/featureAvailability.generated.ts:22`
  (`CONSULTING`); `shared/consultant-limitations.json:239-272` (the four `vibe` rows — the shape).
- Tests that pin today's lists: `src_mcp/tests/FeatureAvailabilityTests.cs:78` (`Consultant.Should().NotContain("kimi", …S5's")`
  — it flips), `src_mcp/tests/ApiConsultantTests.cs:110`, `:122`; `src_mcp/tests/ConsultantLimitationsTests.cs:91`
  (`EveryConsultingRuntime_HasItsOwnRowOnEveryPlatform…`); `src_vs_code/src/test/consultant.test.ts:617`,
  `src_vs_code/src/test/featureAvailability.test.ts:45`. Helpers: `src_mcp/tests/KimiShimScripts.cs` (reports made by the shim's
  own `AskAcpMode.Report`), `src_mcp/tests/PlayedVibe.cs` (the shape for a `PlayedKimi`).

**Decisions.**
1. **Composed from `KimiRuntime`, never a second request builder.** `Build(launch)` calls
   `inner.Build(ConsultantLaunches.RoleOf(launch), launch.Prompt, Cwd(launch), string.Empty, launch.OutputDir, settings)` with
   `settings.Model` resolved (`KimiModels.Default` when empty), then appends `--session <handle>` when `launch.Handle` is not
   empty, and passes a planned launch through `ConsultantLaunches.ForQuestion` (idempotent on Kimi's built environment:
   `Passthrough` is already `Minimal`, `ConsultantLaunches.cs:78-79`). The returned invocation's adapter is `inner`, so the
   executor asks `KimiLaunch` (E5.3 decision e). `ConsultantLaunches.MustBeLaunchable` and `MustCarryNoLineBreak` first, as Vibe.
2. **The cwd is the snapshot, the home is the owned folder, and both are required** — the Vibe sentences with "Kimi". The cwd is
   `launch.Workdir` for a conversation (the ONE stable path, D6) and `ConsultantLaunches.Cwd(launch, plan)` for a planned row. A
   missing `Workdir` or `OwnedScratch` throws `ArgumentException`: never the checkout (D6), and never TEMP or the answers folder
   for a conversation's home (D19).
3. **`Memory = VendorRemembers`; the handle is the report's `sessionId`.** `KimiReport` gains `SessionId`, read in `From` (widened,
   not copied — reuse-first, move 1). `ReadHandle` answers it when `ConsultantHandle.IsWellFormed`, else empty.
4. **`UsageIsCumulative = true`** (M-K5b, RESULTS §23; confirmed for three turns by M-K16 (d)). The service subtracts what the
   conversation was already billed (`ConsultationBilling.ThisTurnsShare`). **Stated limit:** a turn whose `/usage` was not
   captured is billed `Unknown` (0 tokens, `NotCaptured` set), so the NEXT captured turn's share carries that turn's spend as
   well. The conversation's total stays right, and the turn it is filed under is the next one. Neither turn is counted twice.
5. **`DroppedTheConversation` = the report's `end` is `session_lost`** (E5.2 decision 4) — the shim's word, never Kimi's text.
6. **`NeedsASanitisedSnapshot = true`, `HomesFolder = ConsultantHomes.Kimi`** — the two declarations S3 made seams for
   (`ConsultantPlaces.cs:121-125`).
7. **`Toolbox` names only what was observed working** (`IConsultantRuntime.cs:286-297`): `Read`, by a path inside the working
   folder — a copy coai made of the checkout without its `.git`, `.kimi-code/`, `.agents/` skills and sub-agents, `.mcp.json` and
   `AGENTS.md` (served 3 of 3, M-K6); `Grep` and `Glob` (worked 3 of 3, RESULTS §17). It says that writes and shell commands are
   refused. It never says that `Grep` or `Glob` are bounded.
8. **`consultant-limitations.json` — four rows** (one per side, `ConsultantLimitationsTests.cs:91`). Windows: `standing:
   unconfined`. Its text says that Kimi reads coai's copy of the live tree through `Read`, that `Grep` and `Glob` search the disk
   directly and can read any file this user can, and that every write and shell command was refused — measured on kimi 2.1.1.
   `measured`: date 2026-10-13, `cliVersion` `kimi 2.1.1`, cells `M-K6` (`Read` bounded 3/3, `Grep`/`Glob` outside 3/3), `M-K3`
   and `M-K4` (0 of 9 and 0 of 6 writes), `M-K5` (a loaded session kept every refusal 3/3), `M-K5b` (cumulative), document
   `research/RESULTS_kimi_confinement.md`. Linux, WSL and macOS: `standing: unmeasured`, a `source` naming D18, as the Vibe rows do.
9. **Seeds:** `features.consultant` gains `kimi`, the TypeScript is regenerated in the SAME commit
   (`featureAvailability.test.ts:45` holds them equal), and `FeatureAvailabilityTests.cs:78` becomes `Contain("kimi")`.
10. **Registration:** `OnACli` gains `"kimi" => KimiOn(vendor)`, which is `VibeOn`'s shape: the runtime its row resolves to, else
    a new `KimiRuntime(vendor.Provider)`, never another runtime. `Answering` follows `Consulting` by itself.
11. **(Plan gate 2026-10-16, finding (a)) A dropped session is REFUSED with its cure — coai never starts a fresh session in its
    place.** `DroppedTheConversation` is `KimiReport.Parse(stdout).End == AcpWords.SessionLostWord` (decision 5) — the report's END word,
    never its `Reason` text or the agent's. The service has the shape already: `ConsultFailures.cs:43` → `ConsultFailure.ConversationDropped`
    → `ConsultationFailing.Terminal` ends the record with its handle cleared, not resumable (`ConsultationFailing.cs:73-77`); the reply
    carries `What` and `Cure` (`ConsultFailure.cs:266-274`). The cure's sentence is widened for Kimi: "start a new consultation
    (`close_consult` this one — its home goes with it)". **Why refuse, not restart:** (1) a silent `session/new` answers a FOLLOW-UP without
    its premise — a wrong answer in the right shape, which the caller cannot tell from a right one; (2) the vendor's cumulative total would
    restart under the same record, which is exactly the reset decision 12 guards — a restart would trip it on every dropped session;
    (3) a `Handle` that changes mid-conversation has no status to say so: `interrupted` means "resumable", and this is not. The old home is
    left to `close_consult` and D20 — its junction is still valid, nothing leaks. S5.4.9 and S5.4.15.
12. **(Plan gate 2026-10-16, finding (b)) The cumulative delta guards a counter RESET.** `ConsultationUsage.Less` floors every field at 0
    (`ConsultationUsage.cs:32-42`): a report whose total is BELOW what the conversation was already billed reads as "0 this turn" — a flat
    row that under-bills the whole turn. New rule, in the core beside the floor: a CAPTURED report (`!NotCaptured`) whose `TokensIn` OR
    `TokensOut` is below `record.Billed`'s is a reset — the vendor's counter restarted (a session that was not the one loaded, a vendor-side
    restart) — and this turn's share is the report WHOLE, every field, flags as they are. Both totals are judged together, never per field:
    a per-field mix would subtract one counter and restart the other. An uncaptured report is `Unknown` — zeros that are not a counter — and
    is never a reset (decision 4's stated limit stands). The rule is shared: antigravity, the other cumulative vendor, gets the same reading,
    and a flat row was never right for it either. M-K16 (d) decides whether Kimi can ever trip it; the guard is right whether or not it does.
    S5.4.14.

**SCENARIOS** (classes `KimiConsultantTests`, `AKimiConsultationScenarioTests` with a `PlayedKimi` launcher that answers each
shim request with `KimiShimScripts.Report(…)` widened with a session id and a cumulative total; `ConsultantLimitationsTests`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.4.1 `AKimiRow_IsAConsultant_AndAnAnsweringRuntime` | a vendor identity `{id: my-kimi, runtime: kimi}` | `ConsultantResolution.For`, `Consulting`, `QuestionResolution.Answering` | a `KimiConsultant` whose `Vendor` is `my-kimi`; both lists hold `kimi` | *Expected ConsultantResolution.For(my-kimi) not to be <null>.* |
| S5.4.2 `TheFirstTurn_IsTheShimRequest_InTheSnapshot_OnTheConversationsHome` | a launch with `Workdir = <folder>\tree`, `OwnedScratch = <folder>`, no handle | `Build` | argv holds `--ask-acp --profile kimi --model kimi-code/k3-256k --cwd <folder>\tree --home <folder>\kh-<10 hex>`, no `--session`; `WorkingDirectory` is the tree; `StdIn` is the prompt; no argument holds a newline; `KimiEnvironment.Refusal(request, places)` is empty; `Role` is `consult` | (the class is a compile red; against a stub that returns the bare reviewer request: *Expected Request.WorkingDirectory to be "…\tree", but found "<the answers folder>".*) |
| S5.4.3 `ALaterTurn_LoadsItsSession` | the same launch with `Handle = "0d6a…-uuid"` | `Build` | argv ends `… --session 0d6a…-uuid` (the pair in order); a malformed handle → `ArgumentException` from `MustBeLaunchable` | *Expected Request.Arguments to contain {"--session", "0d6a…"} in consecutive order, but it did not.* |
| S5.4.4 `NoSnapshot_OrNoHomeFolder_IsRefusedByName` (Theory) | `Workdir` empty; `OwnedScratch` empty | `Build` | `ArgumentException` with `*Workdir*` / `*OwnedScratch*` | *Expected a <System.ArgumentException> to be thrown, but no exception was thrown.* |
| S5.4.5 `TheHandle_IsTheReportsSessionId` (Theory) | a report with `sessionId: "0d6a…"`; with `"a b"`; stdout not JSON | `ReadHandle` | the id; empty; empty | *Expected handle to be "0d6a…", but found "".* |
| S5.4.6 `ADroppedConversation_IsTheReportsSessionLost` (Theory) | end `session_lost`; end `agent_failed` | `DroppedTheConversation` | true; false | *Expected DroppedTheConversation to be True, but found False.* |
| S5.4.7 `AThreeTurnConversation_IsBilledEachTurnsShare` | `PlayedKimi` reporting totals 20 000/30, 40 004/72, 60 010/101 | `consult`, then two follow-ups through `ConsultationService` | the ledger's three lines say 20 000/30, 20 004/42, 20 006/29; `record.Billed` is 60 010/101; turn 2's and turn 3's shim argv carry `--session` with turn 1's id | against `UsageIsCumulative = false`: *Expected the second turn's billed TokensIn to be 20004L, but found 40004L.* |
| S5.4.8 `AnUncapturedTurn_IsUnknown_AndTheNextCarriesItsShare` | totals 20 000/30, then `notCaptured`, then 60 010/101 | three turns | line 2 is `NotCaptured` with 0 tokens; line 3 is 40 010/71; the record's total is 60 010/101 | (green once S5.4.7 is green — pins decision 4's stated limit) |
| S5.4.9 `ADroppedSession_ClosesTheConversationWithItsCure` | turn 2's report `end: session_lost`, exit 70 | the follow-up | the consult fails as `ConsultFailure.ConversationDropped`; the reply names it and its cure | *Expected the failure to be ConversationDropped, but found NonZeroExit.* |
| S5.4.10 `TheTurnIsBilledUnderTheModelThatRan` (E4 decision 75's rule) | a Kimi row with no model | one turn | the ledger's `model` is `kimi-code/k3-256k` | (green — pins `ModelRan.Of` on the new adapter) |
| S5.4.11 `KimiHasALimitationRowOnEverySide` (`ConsultantLimitationsTests.cs:91` and `:112`) | `kimi` in `Consulting` | the existing tests | a row per side; Windows `unconfined` with its `measured` block; the others `unmeasured` with a `source` | *Expected a row of shared/consultant-limitations.json to describe 'kimi' on windows, but … No row … describes 'kimi' on Windows* (the loader's own sentence) |
| S5.4.12 `TheSeedNamesKimiAsAConsultant` | — | `FeatureAvailabilityTests` (`:78` flipped), `featureAvailability.test.ts:45`, the generator's `--check` | `kimi` in `features.consultant` and in `CONSULTING`; the generated file current | *Expected FeatureAvailability.Builtin.Consultant to contain "kimi".* |
| S5.4.13 `EveryAdapterRecordsWhatItLaunched` / `ConfinementPlannerTests` consult row | `Adapters()` gains `("kimi", new KimiConsultant(…))` (`ConfinementPlannerTests.cs:459-466`); the consult test's exclusion list (`:446`) gains `kimi` | the existing tests | a Kimi consult launch stands in the snapshot (asserted beside Vibe's, `:451-457`) | *Expected …WorkingDirectory to be "<RowTree>", but found "<Repo>".* |
| S5.4.14 `ATotalBelowWhatWasBilled_IsAReset_AndTheTurnIsBilledWhatItSaid` (decision 12; pure in `ConsultationUsageTests`, then through the service with `PlayedKimi`; Theory) | (i) billed 20 000/30, the next report 5 000/9 captured; (ii) billed 20 000/30, the next report 25 000/20 (out below, in above); (iii) billed 20 000/30, the next report `notCaptured` | `ConsultationUsage.Less`; the service's second turn | (i) 5 000/9 — not 0/0; (ii) 25 000/20 whole — one counter restarted means the pair did; (iii) `Unknown`, 0 tokens, `NotCaptured`, as decision 4 says — never a reset; the record's `Billed` advances by the share each time | *Expected the second turn's billed TokensIn to be 5000L, but found 0L.* |
| S5.4.15 `ADroppedSession_NeverStartsAFreshOne` (decision 11) | turn 2's report `end: session_lost`, exit 70 | the follow-up | the played shim was launched exactly twice (turn 1, the follow-up) — no third launch and no `session/new` after the loss; the record is no longer `open` and its `Handle` is empty; the reply's cure names `close_consult` and a new consultation | *Expected the played shim to have been launched 2 times, but it was launched 3 times.* (against an implementation that restarts the session) |
| S5.4.16 `AReLoginBetweenTurns_IsLaunchRefused_TheRecordStaysOpen_AndCloseConsultRemovesTheHomeJunctionFirst` (E5.3's S5.3.4 through the service; E5.3 deviation 16) | turn 1 answered by `PlayedKimi`; the fake person's slot `key` changed | turn 2 through `ConsultationService`; then `close_consult` | the turn fails `launch-refused` with the re-login sentence (D19's `config.toml is not what coai writes … (a re-login)`); the record is still `open`; `close_consult` removes `coai-kimi\<id>` with `GitScratch.AfterUnlink` never invoked and the person's folder whole | *Expected the record's status to be "open" because a refused launch is the turn's failure, not the conversation's, but found "failed".* |
| S5.4.17 `NothingOfTheSignIn_AppearsInATwoTurnConversationsRecords` (E5.3's S5.3.14 in full; `KimiCanaryTests` shape, E4.2 decision 12) | the two canaries of S2.45; a two-turn conversation through `ConsultationService` with a played shim; one `close_consult` | every collector with its liveness check: the record, the ledger lines, the reply, the warn lines, the folder names | no collector holds either canary; the control arm sees a served canary | (a pin — its red, if ever: *Expected the record not to contain "CANARY-REFRESH-7f3a0c", but found it in …*) |
| S5.4.18 `TwoConversationsOnOneSignIn_RunOneShimAtATime_ThroughTheService` (E5.3's S5.3.16 (i); decision k) | two consultations on ONE fake person; `PlayedKimi` whose shim records its start and end and holds 300 ms | both turns started together through `ConsultationService` | the peak of live played shims is 1; the second's start is not before the first's end; both answered; both used ONE gate path (`KimiCredentialsLock.PathFor(<fake credentials>)`) | *Expected the peak of live Kimi launches on one sign-in to be 1, but found 2.* (if the gate were skipped) |
| S5.4.19 `AGateAnotherProcessHolds_DelaysTheShim_UntilItsRelease` (E5.3's S5.3.16 (ii)) | one consultation; the gate file pre-held by the test (a plain `HeldFile.Take`, not inheritable), released after 1 s | one turn through the service | the played shim started only after the release, and within 2 s of it | *Expected the shim to start after the gate was released, but it started while the gate was held.* |
| S5.4.20 `TheConsultant_HandsTheService_ItsPlaceFolderAsTheOwnedScratch` (E5.3's root, R1) | a Kimi consultation through the service | turn 1 | the launch the service builds carries `OwnedScratch = place.Folder` (`coai-kimi\<id>`), and the shim's `--home` is `<folder>\kh-<10 hex>` — never the folder itself, never TEMP or the answers folder | *Expected the shim's --home to be a child of …\coai-kimi\<id>, but found "…\coai-answers\kh-…".* |

**Done when** every scenario is green through the service where the table says so, both suites are green, and the generator's
`--check` is clean. The live three-turn consult is the parent's DoD (J2).

## E5.5 — The consultant check (`--check-consultant`) for Kimi

`model: opus` spec · built on Opus (ordinary, §0.2) · Opus reviews. Needs E5.4 (and E5.3's disposal).

**Goal.** `coai-mcp --check-consultant` runs one paid Kimi turn as a consultation makes it: in a COPY of the check's scratch
repository, on a ONE-SHOT owned home (junction and all) under `<root>\coai-vh\<8>`, disposed junction-first whatever ended
the check. The check's own invariant (the scratch repository unchanged, the canary untouched) holds.

**Files and anchors (verified).** `src_mcp/src/Server/Consultation/ConsultantCheck.cs:199-227` (`TurnAsync`: the place, the
reservation `:211`, `finally` `:221-227`), `:230-233` (`PlaceAsync`: a copy for a runtime that asks), `:235-259` (`InPlaceAsync`;
the model rebound `:252`), `:265-286` (`LaunchAsync`: `OwnedScratch = homeFolder` `:285`), `:262` (`OwnName = "own"`);
`src_mcp/src/Server/Consultation/ConsultCheckScratch.cs:107-120`; `ConsultantCheck`'s existing Vibe tests as the model.

**Decisions.**
1. **No new branch in the check.** It already gives a copy to a runtime that asks (`NeedsASanitisedSnapshot`) and a one-shot
   home to a runtime that declares a `HomesFolder` (`:211`), which `KimiConsultant` does (E5.4 decision 6). What makes it safe for
   Kimi is E5.3 decision g (`OneShotHome.Dispose` and the root-less fallback). This story proves the path with its own tests, as the
   parent's S5 bullet asks.
2. **The home is `<root>\coai-vh\<8>\kh-<10 hex>`** (J3). With settings built by hand (no root) it is `<scratch>\own\kh-<10 hex>`, and
   `ConsultCheckScratch.Delete` disposes it junction-first (E5.3 g).
3. **The confinement recorded on the check** is empty (Kimi's confinement does not depend on its CLI's version), as Vibe's.

**SCENARIOS** (class `AKimiConsultantCheckTests`, `PlayedKimi`, a `testsToDelete-` homes root, `KimiFixture`'s fake person).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.5.1 `AKimiCheck_RunsInACopy_OnAOneShotHome` | a Kimi row; the homes root a test folder | `ConsultantCheck.RunAsync` | passed; the shim request's `--cwd` is under `<scratch>\own\tree`; `--home` is `<root>\coai-vh\<8>\kh-<10 hex>`; the scratch repository and its canary unchanged | before E5.4: *Expected the check of my-kimi to pass, but it failed: the vendor 'my-kimi' runs on 'kimi', which cannot hold a consultation in this build …* |
| S5.5.2 `AfterTheCheck_TheHomeIsGone_JunctionFirst` | S5.5.1, `GitScratch.AfterUnlink` a recorder | the check ends (passed; and a second arm where the turn fails exit 70) | `coai-vh\<8>` gone; the recorder never invoked; the fake person's folder intact, bytes and `ReadOnly` | *Expected the deleter's unlink recorder not to be invoked …* (before E5.3 g) |
| S5.5.3 `ACheckCancelledMidTurn_StillDisposesItsHome` | the played shim blocks; the token cancelled | the check | the record says cancelled; `coai-vh\<8>` gone junction-first | (green after E5.3 g — pins `finally`) |
| S5.5.4 `ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst` | settings built by hand (empty root) | the check | `--home` under `<scratch>\own\kh-…`; after it, the scratch gone and the recorder never invoked | *Expected the deleter's unlink recorder not to be invoked …* (before E5.3 g) |

**Done when** the four scenarios and the whole suite are green. The live check (J2) is recorded in RESULTS §31.

## E5.6 — The question consultant's Kimi row: the matrix, the planner arm, the one-shot row

`model: opus` spec · built on Opus (ordinary, §0.2) · Opus reviews. Needs E5.4 (and E5.3's disposal).

**Goal.** A question-consultant row can run on Kimi with a `none` or a `disk` prompt. Each row is flagged **Can read this
machine**, because it can. A `web` prompt is refused until a cell measures it. The row's home and its copies are one-shot and gone
junction-first on success, refusal and cancellation.

**Files and anchors (verified).**
- `src_mcp/core/QuestionConsult/RuntimeCapabilities.cs:84` (`Runtimes`, hand-written — NOT read from the seed),
  `:125-127` (a row outside it breaks the file).
- `src_mcp/core/QuestionConsult/ConfinementPlanner.cs:151-159` (`Fragments`; the `vibe` arm `:156`; the default arm gives an
  unknown runtime NO confinement and no flags, `:158`), `:197-202` (`Vibe`), `:91-100` (`SandboxVocabulary`), `:10-22` (`CwdKind`).
- `src_mcp/core/QuestionConsult/CapabilityMatrix.cs:85-92` (confined admits; unconfined admits FLAGGED; unmeasured refuses),
  `:105-106` (the unmeasured sentence names the record from `resultRef`).
- `shared/runtime-capabilities.json:19-20` (the `why` naming whose cells the rows cite), `:23` (`runtimes`), `:291-338` (the three
  `vibe` rows — the shape); `shared/capability-matrix-vectors.json:30-32` (the `vibe` vectors);
  `src_vs_code/src/runtimeCapabilities.generated.ts:35`; `src_vs_code/scripts/generate-runtime-capabilities.mjs`.
- `src_mcp/runners/Consultation/ConsultantLaunches.cs:62-67` (`Cwd` by plan), `:78-79` (`ForQuestion`).
- `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:101-136` (the reservation, the place, `finally`), `:168-182` (the launch:
  an EMPTY `cwd` folder beside the home for a home-keeping `none` row, `:172`), `:188` (the copies); `QuestionFanOut.cs:214-217`
  (the prompt names the copies).
- Tests: `src_mcp/tests/CapabilityMatrixTests.cs:80` (`TheVectorsActuallyLoaded_AndCoverEveryPairOfTheTable`), `:187-196`
  (the Vibe rows' shape), `src_mcp/tests/ConfinementPlannerTests.cs:129-154` (the Vibe arm and its `web` row), `:316-337` (the Vibe
  question launch — the model for Kimi's), `src_vs_code/src/test/capabilityAdmission.test.ts:143`, `:154-175`.

**Decisions.**
1. **The planner arm:** `"kimi" => Kimi(grant, flag)`. `disk` → `new("kimi", grant, [], [], CwdKind.Snapshot, [], flag) {
   SnapshotSources = grant.Roots }`. `none` → `new("kimi", grant, [], [], CwdKind.Scratch, [], flag)`. `web` never reaches it (the
   matrix refuses `unmeasured`). **No flags, ever:** Kimi's confinement is the shim's (D15), not its argv, so the plan carries
   none and `SandboxVocabulary` gains nothing.
2. **The standings, each from its own cells:**
   - `disk` — **`unconfined`**: `Read` is bounded by the shim, but `Grep` and `Glob` read the disk directly (M-K6, 3 of 3; D15).
     Cells `M-K6`, `M-K3`, `M-K4` (writes refused), `M-K9` and `M-K9 (e)` (why the roots are COPIED, not granted: a branch's
     `AGENTS.md`, skills, sub-agents and `.mcp.json` load from disk).
   - `none` — **`unconfined` too, not `confined` as Vibe's is.** An empty working folder bounds `Read`, but `Grep` and `Glob` took
     an OUTSIDE file's marker by absolute path 3 of 3 (M-K6), and the cwd plays no part in that. So the empty folder confines
     nothing, and the row says **Can read this machine**, as codex's `none` does. Cells `M-K6`, `M-K3`, `M-K4`. (The parent's S5
     bullet named only `disk`; this is the same evidence applied to the other row.)
   - `web` — **`unmeasured`**: `WebSearch` and `FetchURL` are announced (RESULTS §7) and no cell exercised them (§14). The planner
     refuses, naming `research/RESULTS_kimi_confinement.md` (the record `RecordOf` reads off `resultRef`). M-K17 does NOT admit
     `web`: what it can find is whether those tools ask. Admitting `web` needs its own cell.
   - Each row's `note` carries the version (2.1.1) and, for `none` and `disk`, the sentence that `WebSearch` and `FetchURL` are
     announced and unmeasured. M-K17 decides whether that sentence stays, changes or goes.
   - Nothing about `~/.agents`: the OS-home redirect closes it (M-K12, RESULTS §19).
3. **`KimiConsultant` on a planned launch** (E5.4 decision 2): the cwd from the plan (`Scratch` → the row's `cwd` folder, `Snapshot`
   → the copy), the home in the row's one-shot folder, no `--session` (one shot), and a plan that carries ANY flag is refused with
   `ArgumentException` ("a Kimi row's confinement is the shim's; a plan with flags is not one coai made for kimi").
4. **The vectors and the generated table** gain the three `kimi` pairs; `RuntimeCapabilities.Runtimes` gains `kimi` (the
   hand-written list, `:84`).
5. **One-shot, junction-first:** nothing new in `QuestionRowLaunch`. Its reservation, place and `finally` are generic; the
   disposal is E5.3 decision g.

**SCENARIOS** (classes `CapabilityMatrixTests`, `ConfinementPlannerTests`, `AKimiQuestionRowTests` with `PlayedKimi`; TS
`capabilityAdmission.test.ts`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.6.1 `KimiHasThreeRows_EachCitingItsCells` | — | `RuntimeCapabilities.Builtin.Of("kimi", none/disk/web)` | `unconfined` (cells include `M-K6`), `unconfined` (cells include `M-K6`, `M-K9`), `unmeasured`; version `2.1.1` | *Expected RuntimeCapabilities.Builtin.Of("kimi", Capability.None) not to be <null>.* |
| S5.6.2 `TheVectorsCoverKimi` (`CapabilityMatrixTests.cs:80`) | the table holds `kimi` | the existing test | every pair covered | the test's own message naming the missing `kimi × none`, `kimi × disk`, `kimi × web` pairs |
| S5.6.3 `ThePlannerArm_IsACopyForDisk_AnEmptyFolderForNone_AndRefusesWeb` | grants `None`, `Disk(RootA, RootB)`, `Web` | `ConfinementPlanner.Plan("kimi", …)` | `Planned` `Scratch`, no flags; `Planned` `Snapshot`, `SnapshotSources` = both roots, no flags, `AddDirs` empty; `Refused` containing "not been measured" and `RESULTS_kimi_confinement.md` | *Expected plan.Cwd to be CwdKind.Snapshot, but found CwdKind.Scratch.* (the default arm, `:158`) |
| S5.6.4 `AdmissionFlagsBothRows` | — | `CapabilityMatrix.Admit("kimi", none)`, `(…, disk)` | `Admitted` with `AdmissionFlag.Unconfined` both | *Expected Admission to be Admitted, but found Refused "'kimi' is not a runtime the question consultant can launch …".* |
| S5.6.5 `AKimiQuestionLaunch_IsTheShimRequest_InItsFolderOrItsCopy_OnTheRowsHome` (the `ConfinementPlannerTests.cs:316-337` shape) | `Planned("kimi", None)` and `Planned("kimi", Disk(RootA, RootB))` with `OwnedScratch = RowFolder` | `KimiConsultant.Build` | `none`: `WorkingDirectory` is the row's empty folder; `disk`: the row's tree; `--home` under `RowFolder`; no `--session`; neither root named; `InheritsEnvironment` false; `KimiEnvironment.Refusal` empty | *Expected Request.WorkingDirectory to be "<Scratch>", but found …* (against a stub ignoring the plan) |
| S5.6.6 `APlanWithFlags_IsRefusedForKimi` | a hand-made `Planned("kimi", …, Flags: ["--yolo"])` | `Build` | `ArgumentException` naming the shim | *Expected a <System.ArgumentException> to be thrown, but no exception was thrown.* |
| S5.6.7 `AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind` (`QuestionRowLaunch` with `PlayedKimi`; Theory: answered; refused before the launch; cancelled mid-turn) | a `disk` row on a git root in the test's folder | the row runs | the prompt names the copies' paths and never the root; after each ending the row's scratch and `coai-vh\<8>` are gone, the recorder never invoked, the fake person's folder intact | *Expected Directory.Exists("…\coai-vh\<8>") to be False, but found True.* / the recorder red (before E5.3 g) |
| S5.6.8 TS `kimi rows admit flagged, web refuses naming its record` (`capabilityAdmission.test.ts`; `:143`'s list gains `kimi`) | the regenerated table | `admit('kimi', 'none' / 'disk' / 'web')` | `none`/`disk` admitted with the unconfined caveat; `web` refused with `/not been measured/` and `RESULTS_kimi_confinement.md` | `AssertionError [ERR_ASSERTION]: Expected values to be strictly deep-equal` on the runtimes list; then *'kimi' is not a runtime the question consultant can launch* for `admit` |

**Done when** both suites and both generators' `--check` are green, and the live question rows (J2) are recorded in RESULTS §31.

## E5.7 — The extension: the consultant picker, the consultant-runtime gate, and the help in five languages

`model: opus` spec · built on Opus (ordinary, §0.2) · Opus reviews. Written beside E5.4 and E5.6, merged after both (it needs their seeds).

**Goal.** In the extension a Kimi row can be picked as a consultant and as a question-consultant row. An installed `coai-mcp`
that cannot consult on Kimi is NAMED, never silently routed elsewhere. The help says what a Kimi consultant reads and where its
homes live, in all five languages.

**Files and anchors (verified).**
- `src_vs_code/src/runtimeSince.ts:28` (`VIBE_RUNTIME_SINCE`), `:43` (`KIMI_RUNTIME_SINCE = '0.57.0'`), `:46-50` (`RUNTIME_SINCE`),
  `:58-62` (`runtimeOnServer`), `:80-85` (`heldNote`); `src_vs_code/src/test/runtimeSinceIsAhead.test.ts:33-35`
  (`DOCUMENTED_ELSEWHERE`), `:50-60` (`refusal`).
- `src_vs_code/src/consultSettings.ts:122` (`CONSULTING_RUNTIMES` = the generated `CONSULTING`), `:441`
  (`CONSULTANT_DEFINITION_SINCE` and its skew note — the sibling rule: say it only when the server is KNOWN and strictly older).
- `src_vs_code/src/modelCardFields.ts:31-32` (the model card's consultant and question-consultant fields read `CONSULTING`).
- `.release-please-manifest.json` (`src_mcp` `0.57.0`; `origin/main` 02ffd326 is that release's commit).
- Help: `src_vs_code/src/helpContent.ts:335` (the question consultant article, English), `src_vs_code/src/helpRu.ts:63`,
  `helpDe.ts:63`, `helpEs.ts:63`, `helpUk.ts:127`; the homes folder: `src_vs_code/src/help.ts:45` (`vibeHomesDirectory`),
  `src_vs_code/src/consultantView.ts:64-67` (the field, its label "Vibe homes folder"), `src_vs_code/src/pathFindings.ts:228`,
  `:237`, `:245`.

**Decisions.**
1. **A consultant-runtime gate, in the runtime gate's own pattern.** `runtimeSince.ts` gains
   `KIMI_CONSULTANT_SINCE = '0.58.0'` and `CONSULTANT_SINCE: Readonly<Record<string, string>> = { vibe: '0.54.0', kimi:
   KIMI_CONSULTANT_SINCE }` with `consultantOnServer(runtime, installed)` — `runtimeOnServer`'s rule (an unknown server is not old).
   **Why 0.58.0:** the newest released `src_mcp` is 0.57.0 (the manifest; `origin/main` is that release's own commit, so no `feat`
   waits after it). Release-please bumps the minor for a `feat` below 1.0, so the first release that can carry S5 is 0.58.0. The
   remarks say it, and say "whoever cuts the release keeps it level with the tag — if another minor ships first, raise it" (the
   `KIMI_RUNTIME_SINCE` rule). **Why `vibe: '0.54.0'` too:** S3 shipped the Vibe consultant in 0.54.0 (#811; the changelog's
   `## Server 0.54.0` section names Vibe) one release after the Vibe runtime (0.53.0), with no gate. One table that tells the truth
   for both costs one line. The operator may drop it (§"Open questions" 3).
2. **A note, never a withheld entry.** An older server refuses a consultant on a runtime it cannot consult with, by name
   (`ConsultantResolution.CannotConsult`). Withholding the `COAI_CONSULTANTS` entry instead would make that server consult with the
   shipped default pair, silently — worse than the refusal. So the Consultant section, and a question-consultant row on `kimi`,
   show: "The coai-mcp you have installed ({version}) cannot consult on {runtime}; update it to {since} or later — the MCP server
   tab." Shown only for a KNOWN server that is strictly older.
3. **The guard covers the new table.** `runtimeSinceIsAhead.test.ts` iterates `CONSULTANT_SINCE` beside `RUNTIME_SINCE`, with the
   same three ways to pass.
4. **No picker code.** `CONSULTING` is generated from the seed (E5.4), and `modelCardFields.ts:31-32` enables the consultant and
   question-consultant fields from it. The question consultant's `capabilityAdmission` is generated from the table (E5.6), and its
   **Can read this machine** tick follows the `unconfined` flag with no new code.
5. **Help, five languages** (the Vibe sentence's place in each article): a Kimi row reads a copy of each granted folder (`disk`)
   or stands in an empty folder (`none`), and **can read this machine on both** through its search tools — so the tick is on and
   cannot be taken off. A `web` prompt is disabled, because nothing has measured Kimi on the web. It runs on Windows only (D18). A
   folder outside a Git repository is refused. Its launches wait for one another and for a Kimi review on the same sign-in (D19).
   NOT "the home never receives code" (M-K12 (c) is still unrecorded), and nothing about the web for `none`/`disk` until M-K17
   is recorded.
6. **The homes folder says Kimi too.** The setting key `coai.vibeHomesDirectory` is unchanged (a rename is a migration and not
   this epic). The label becomes "Vibe and Kimi homes folder". Its help (`help.ts:45`) adds that Kimi's conversation homes
   (`coai-kimi`) and one-shot homes (`coai-vh`) live there too, each holding a LINK to your Kimi sign-in that coai removes first
   when it deletes the home, and that Kimi writes long paths without trouble, so the 156-character rule is Vibe's. The three
   `pathFindings.ts` texts name both CLIs.

**SCENARIOS** (`src_vs_code/src/test/`: `runtimeSince.test.ts` — or the closest existing file for that module — then
`runtimeSinceIsAhead.test.ts`, `consultant.test.ts`, `capabilityAdmission.test.ts`, `helpCoverage.test.ts`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.7.1 `consultantSinceNamesKimiAtTheNextMinor` | — | `CONSULTANT_SINCE` | `{ vibe: '0.54.0', kimi: '0.58.0' }` | `AssertionError [ERR_ASSERTION]: Expected values to be strictly deep-equal: + actual undefined - expected { vibe: '0.54.0', kimi: '0.58.0' }` |
| S5.7.2 `anOlderServerIsNamedOnTheConsultantSection` | installed 0.57.0, a `kimi` consultant picked | the section's note | names 0.57.0, `kimi` and 0.58.0; at 0.58.0 → no note; an unknown version → no note; a `codex` pick on 0.57.0 → no note | `AssertionError: Expected values to be strictly equal: + actual '' - expected 'The coai-mcp you have installed (0.57.0) cannot consult on kimi; …'` |
| S5.7.3 `theEntryStillCrosses` | the same old server | `consultantsEnv` | the `kimi` entry is IN `COAI_CONSULTANTS` (decision 2) | (green — pins "a note, never a withheld entry") |
| S5.7.4 `theGuardReadsTheConsultantTable` (`runtimeSinceIsAhead.test.ts`) | `kimi` 0.58.0 ahead of 0.57.0; `vibe` 0.54.0 named in its section; a planted `kimi: '0.57.0'` in the pure `refusal` | the guard | the first two pass; the planted one is refused with the existing sentence shape | the planted row: *kimi: SINCE 0.57.0 is not ahead of the newest released coai-mcp (0.57.0) and the changelog's "## Server 0.57.0" section does not name kimi* is NOT produced today because the table is not read — `AssertionError: Expected values to be strictly equal: + actual '' - expected 'kimi: SINCE 0.57.0 …'` |
| S5.7.5 `kimiIsOfferedAsAConsultant` | the regenerated `CONSULTING` | `modelCardFields.consultant('kimi')`, `.qconsult('kimi')` | `''` (enabled) both; `consultant.test.ts:617`'s list holds `kimi` | `AssertionError: Expected values to be strictly equal: + actual 'a consultation runs on codex, claude, antigravity, local, api, vibe' - expected ''` |
| S5.7.6 `theQuestionConsultantHelpNamesKimiInFiveLanguages` | the five articles | the help coverage test | each names Kimi, the tick, the disabled web and Windows only | `AssertionError: the ru question-consultant article does not mention Kimi` |
| S5.7.7 `theHomesFolderNamesBothCLIs` | — | the field, its help, the path findings | the label says "Vibe and Kimi"; the help names `coai-kimi`, `coai-vh` and the sign-in link; the setting key is unchanged | `AssertionError: Expected values to be strictly equal: + actual 'Vibe homes folder' - expected 'Vibe and Kimi homes folder'` |

**Done when** `npm run typecheck`, `npm run lint` and `npm test` are green, the generators' `--check` is clean, and
`research/module_extension.md` records the consultant gate and the help.

## Where the parent plan and the code disagree (found while splitting, 2026-10-16)

1. **The conversation folder is not the Kimi root.** E4.2 decision 8 said "S5's conversation folder (`coai-kimi/<id>`) is a root
   that also holds `tree/`, `coai-conversation.txt` and `coai-turn.lock`". Taken literally, the turn would wait for itself (E5.3
   decision a). The root is `<folder>\kh-<10 hex>`, which `KimiRuntime.Build` already makes. To be recorded in D19 at promotion.
2. **The conversation home is under the homes ROOT, not `LocalApplicationData/coai-kimi/`.** The parent's S5 bullet predates
   #823's D1. The root is `COAI_CONSULTANT_HOMES` else the local application data, entered through `PrivateFolder` (D7).
3. **A question row's Kimi home is under `<root>\coai-vh\<8>`, not "inside the row's own `coai-question-*` scratch folder
   (`QuestionRowLaunch.cs:68`)".** The parent's S5 bullet predates #823. The anchor moved too: the reservation is
   `QuestionRowLaunch.cs:101`. The copies are still made in the row's scratch.
4. **`none` is `unconfined` for Kimi**, where the parent's S5 bullet names only `disk` (E5.6 decision 2; the same evidence, M-K6).
5. **"Nothing of S5 waits on a cell" is not quite true.** Three facts that E5.2's code depends on were not recorded by M-K5 or
   M-K5b (J2): M-K16 is E5.1.
6. **`KimiReport` carries no session id** (`KimiReport.cs:20`) although the shim prints one (`AskAcpMode.cs:316`). E5.4 widens it.
7. **The one-shot homes' disposal is not junction-first.** E4.1 decision 4 listed "S5's question row and consultant check" among
   the callers that know they hold a Kimi home. Since #823 those homes are owned by `OneShotHome` and swept by `SweepOneShot`, both
   on the bare deleter (`VibeHomes.cs:187-204`, `:279-297`). E5.3 decision g moves D19's order there.
8. **The M-K8 deep arm's label says "~250 characters"** (`scripts/probe-kimi-confinement.mjs:358`) where its code makes a
   205-character home (`:363-368`) and RESULTS §11 says 205. RESULTS is right; the label is stale (E5.1 may fix it when it edits
   the script).
9. **D16 / E4.5 left no consultant gate**, and S3 shipped the Vibe consultant one release after the Vibe runtime with none. E5.7
   adds the table (with `vibe` in it, unless the operator says no).
10. **(Fable, 2026-10-16) An end the shim's switches do not name is printed `cancelled` and exits 69** — the default arms of
    `AskAcpMode.EndWord` (`:354-363`) and `EndExit` (`:299-305`). So `session_lost` needs explicit arms (E5.2 decision 9, S5.2.12), or
    E5.2 decision 4's "70" is 69 in the binary and the host reads a dropped session as "ended before an answer".
11. **(Fable, 2026-10-16) Where the sign-in's ancestors lead is resolved at MAKE time only.** `Junctions.Refusal`'s `Resolved`
    (`Junctions.cs:179-189`) runs inside `Make` (`:114-118`), i.e. on the first turn; the per-turn checks re-read the spellings
    (`KimiLaunch.LocalHome` `:149-152`, `KimiHome.Credentials` `:221-233`) and then key a lock on the final path
    (`KimiCredentialsLock.KeyFor` `:116-121`) without judging it. D19 says "the junction's target checked before every turn" — the
    target's spelling is, where it leads is not. E5.3 decision j closes it; to be recorded in D19 at promotion.
12. **(Fable, 2026-10-16) The check's `ConsultCheckScratch.Delete` is handed the scratch ROOT** (`ConsultantCheckMode.cs:307`) while
    the Kimi home sits one level down at `<root>\own\kh-*` (`ConsultantCheck.cs:262`, `:285`) — a `DisposeAllUnder(root)` would find no
    home. E5.3 judgement R6 names the folder to dispose.

**E5.3 as built (Fable, 2026-10-16) — where it deviates from the spec above:**

13. **Decision j runs TWICE per turn, and neither run is "right after `LocalHome`" as written.** (a) The person's HOME (`~\.kimi-code`) is
    resolved as the LAST check of `KimiLaunch.Prepare`'s pre-read list — before `KimiPersonsConfig.ReadFile` reads their `config.toml` from it
    (the review's M1: a swapped ancestor must get no read from the share, not one) — and only when that folder exists, so a sign-out that
    removed it keeps `ReadFile`'s own "not signed in" sentence. (b) The `credentials` folder is resolved in `Rendered`, right after its
    existence check — `Junctions.ResolvedRefusal` on a missing folder answers "cannot be named by its handle", which placed earlier would
    have replaced "has no credentials folder; run `kimi login`". Both run before the slot is rendered and before any lock (S5.3.15 and
    `ThePersonsHomeResolvingOffTheLocalDrives_IsRefusedBeforeTheirConfigIsRead`: no file under `kimi-locks`, the config never read).
14. **`OneShotHome.Remove` (a whole-folder seam) became `OneShotHome.Io : HomeIo` (per entry).** S5.3.20 observes the lock at the moment
    EACH entry goes, which a whole-folder seam cannot show. `HomeIo.Real.Delete` is `GitScratch.DeleteEntry`, made public (reuse move 1):
    a link by its own name, a folder whole, a FILE as a file — the old `io.Delete(entry)` = `DeleteEvenIfReadOnly(entry)` threw on a file
    beside the home (a Vibe export), a latent fault of `SweepOneShot` fixed on the way.
15. **`ConsultantCheck.OwnName` did not exist where the spec named it.** The const was private on `ConsultantCheckTurn`; it now lives on the
    public static `ConsultantCheck` (the turn reads it from there), so `ConsultCheckScratch` can dispose `<root>\own` by name.
16. **The `ConsultationService` arms wait for E5.4's `KimiConsultant`:** S5.3.4's "the record is still open; `close_consult` then removes the
    folder", S5.3.14's two-turn conversation with a played shim, and S5.3.16 (i) "through `ConsultationService`". Each is pinned at the one
    control instead — `KimiRuntime.TakeTurnAsync` over a place from `ConsultantPlaces.ForConversationAsync` (the HOME-ONLY shape, no snapshot
    copy: the lock order A → B → C → D is the same) — and S5.3.14's arm covers the collectors THIS story adds (the owner's and the sweep's
    warns, and the decision-j sentence PRODUCED through the `ResolvingJunctionShell` seam — a local folder resolves to itself and says
    nothing, so the arm asserts the sentence is among what the detector reads). E5.4 owes the three service arms as its own rows
    (S5.4.16–S5.4.20 below); the shapes are in `AConversationsKimiHomeTests`.
17. **S5.3.10 gained the live-tree arm of both fallbacks** (`TheRootlessFallback_KeepsTheScratchUnderALiveTree`): the row's scratch is kept with a
    WARNING (`a question row's scratch directory at {Path} is kept for the scratch sweep: {Reason}`), the check's with its warn. The brief's
    "the in-deleter check stays the second belt" implied it; nothing listed it.
18. **S5.3.16 (ii) is measured at the launch control:** the gate pre-held in the host (a plain `HeldFile.Take`) and released after 1 s — the launch
    starts after the release and within 2 s of it (the gate waits in one-second slices), where the row said "the shim started".
19. **S5.3.11 is a pin with no teeth of its own:** .NET's walk read and disposed the 300-character path on Windows (and on Linux in CI); the red the
    row names would be the platform's, so no deciding line was broken for it. S5.3.17's teeth were the reparse-point skip of
    `PrivateFolderWindows.SettledEntry` removed: the walk then entered the junction and listed the person's files (red as the row says), while
    Windows' own propagation left both descriptors byte-equal — the code's claim at `PrivateFolderWindows.cs:152` holds on this machine.
20. **The sweep's and the owner's kept-home sentences, fixed here:** the one-shot sweep says `one-shot Vibe homes: {folder} is kept this pass: {said}`
    (the `DeleteIfDue` shape), the owner `the Kimi home in {folder} is kept: {reason}; the scratch sweep takes it once its tree has ended` (decision g's
    words). Both carry the `Stopped` sentence unparaphrased (R4) — paths and entry names, never a byte (R11, pinned).

**E5.4 as built (Opus, 2026-10-16) — where it deviates from the spec above:**

21. **A dropped session's cure names the sweep, not `close_consult`** (decision 11, S5.4.15). The record of a dropped conversation is `failed`, and
    `ConsultationClosing.Refusal` refuses to close a failed record ("produced no advice — there is no outcome to record"), so the widened sentence the spec
    gave would send the caller into a refusal. `ConsultFailure.ConversationDropped.Cure` now reads "start a new consultation — this one has ended, and whatever
    coai kept for it (a consultant's home) goes with the next sweep"; D20's sweep takes a failed record's folder on its next beat, junction-first (S5.4.15
    asserts it through `SweepConsultations`). Shared by every vendor's dropped conversation; the sentence is true for one that keeps no home too.
22. **S5.4.16 needed a code change, shared by every vendor.** A `launch-refused` turn (`KimiLaunch` — or Vibe's control — said no; nothing was launched)
    ENDED the record (`failed`). `ConsultationFailing.Terminal` now returns a conversation that had already gone on to the status it had before `asking`
    (`TurnFailure.StatusBefore`, passed from the service's pre-`asking` record through `ConsultationFailedTurns.Decided`) — `open` or `interrupted`, the handle
    kept, the failure kind and cure recorded beside it; a FIRST turn refused so is ended as before (no conversation to keep). The rule
    `RefusedBeforeTheLaunch` already applied to an adapter's own refusal. The call is given back and the ledger line written as for any failure, and the answer
    still says "do not retry this consultation" — right for the re-login, whose cure is a new consultation.
23. **A product seam for the service arms: `KimiSeams.Using(host, seams)`**, per async flow (the `VibePathBudget.Using` precedent). The service builds
    `new KimiRuntime(id)` by name, from the real profile and `HostKinds.Current`; the seam lets a test hand it `KimiFixture`'s person and the measured host, so
    the real launch control, a real junction and a real gate file run under `ConsultationService`. `null` in the product. With it the service arms run on every
    OS through `WindowsStandIn` (Vibe's service scenarios run on Windows only); only Windows was run here.
24. **Decision 12 overturned an existing pin.** `CumulativeUsageTests.AVendorReportingLESSThanBeforeFloorsAtZero_NeverNegative` asserted a report below what
    was billed bills 0/0 — the very flat row decision 12 forbids. It became `ACountBelowWhatWasBilled_FloorsAtZero_NeverNegative` (the totals moved on, the
    cached and reasoning counts did not: those are floored). The rule is public, `ConsultationUsage.IsAReset`, beside the floor.
25. **S5.4.14's pure arm is in `CumulativeUsageTests`** (`ConsultStoryTwoGateTests.cs`, the existing class for `Less`), not a new `ConsultationUsageTests`.
26. **The reds.** The pure and adapter rows were watched red against stubs (`KimiConsultant` returning the bare reviewer request; `Less` unchanged). The
    service rows S5.4.7–S5.4.10, S5.4.14 (service), S5.4.18–S5.4.20 went green on arrival — the code landed with the adapter rows — and were given teeth by
    a temporary break of the deciding line (`UsageIsCumulative = false`; `DroppedTheConversation` false; the invocation's `Model` emptied; `OwnedScratch`
    emptied before `inner.Build`; `IsAReset` off; the gate keyed apart through `KimiCredentialsLock.Final`), each run red and reverted. S5.4.15's predicted red
    ("launched 3 times") could not be produced: there is no restart path to break (`ConsultantTurn` follows up only a launch that answered nothing, never a
    terminal one), so its teeth are the handle (*… to be empty …, but found "0d6a…"*) and the cure (by nature, deviation 21). S5.4.16 was red by nature.
27. **S5.4.17's collectors** are every Serilog line of the service (all levels, not only warnings) and every name under the data directory (the homes root
    included, never entering a link), taken before and after `close_consult`, beside the replies, the record files and the ledger. The control arm is a played
    answer that carries the canary — seen in the reply and the record.
28. **S5.4.18 runs in two checkouts.** Two consultations in ONE checkout serialise on the service's `RepositoryLock` before any gate, so the peak would be 1 for
    the wrong reason. The teeth run shows nothing but the gate serialises the two: keyed apart, the peak is 2.
29. **Decision 9 in two halves.** The seed and the generated `src_vs_code/src/featureAvailability.generated.ts` (and `featureAvailability.test.ts`'s frozen
    list) changed here, at the coordinator's word; `consultant.test.ts`'s `CONSULTING` list and `kimiRuntime.test.ts` ("neither a consultant") are E5.7's, and
    stay red on this branch until E5.7 merges.
30. **`KimiReport.SessionId` is a trailing, defaulted field** (one construction site, `From`), so nothing else changed shape (decision 3).

### E5.1–E5.2 as built (Opus, 2026-10-16)

Numbered apart from the list above, so the stories built in parallel do not collide on a number.

- **B1. The replay is kept out by a WINDOW, not a mark** (decision 3). `AcpSession.ReplayedAsync` drops every `session/update` that arrives while
  `session/load` is open; `Ended` gains no `AnswerFrom` and the three trace lists are not sliced. Same answer, and two things the mark could not give: the
  replayed tool calls never spend the trace caps (`MaxTraceEntries` 1000, `MaxTraceChars` 1 MiB) that a long conversation's history would otherwise fill
  before the turn's own trace, and the exclusion survives a turn that throws after the load (`Ended.Of(Failed(…))` would drop a mark held in `Ended`). A
  `usage_update` during the replay is dropped too. Sound because the pump handles messages in order: every replayed update is handled before the load's
  response completes the request.
- **B2. A refused load that a known failure names reports end `agent_failed`, not `session_lost`** (decision 4, made explicit). `AskAcpMode.Named` turns the
  end with the diagnosis, so a load refused for the sign-in is `not_signed_in`, 77, end `agent_failed` (S5.2.4 asserts it). Without it the report would say
  `session_lost` with exit 77, and E5.4 decision 5 reads the END word — it would drop a conversation that only needed `kimi login`.
- **B3. A bare `--session` (no value) is 65 too** — a fifth row of S5.2.5. `Program.Flags` drops a flag whose value is missing, so `--session` last on the
  argv, or followed by another flag, would have run a NEW session in place of the one asked for.
- **B4. The `session_lost` reason names the id in coai's words** (`AcpClient.LostLead`), then the agent's code and message, with only Kimi's echo
  `Unknown sessionId: <the id>` written `Unknown sessionId: <id>` (M-K16 (c): do not quote the id from the error; the review: a short id written over every
  occurrence garbled `Authentication` into `Authentic<id>tion`). The agent's message stays in the reason, because the known-failure diagnosis reads it (S5.2.4);
  the diagnosis reads the reason WITHOUT the lead (`AskAcpMode.Diagnosable`), so an id cannot spell a failure.
- **B5. The fake agent's script** (brief item 1): `loadModels` absent falls back to `models`, not "none" — Kimi announces the model on a load as on
  `session/new` (E5.1 (a)), and "none" would make every load scenario 65 under the first branch. `loadError` is an object `{code, message}`. `replay` is a list of
  the existing steps, so it replays a `tool_call` too (S5.2.2 does).
- **B6. S5.2.6 keeps branch (i) only**, with `models` offering the asked model and `loadModels` not, so it proves the LOAD's announcement is the one checked.
- **B7. Where the tests live.** S5.2.1–S5.2.9, S5.2.11 and S5.2.12 are a new class, `AskAcpSessionTests`, rather than additions to `AskAcpModeTests` and
  `AcpClientTests`; S5.2.10 is in `AKimiShimScenarioTests` as planned. S5.2.8's golden is a literal in the test (the client's lines, the cwd spelled `<cwd>`),
  recorded on the unchanged client, not a file. `AskAcpMode.EndExit` became `internal` for S5.2.12.
- **B8. The reds** were watched against a stub that parses `--session` into `AcpAsk.Session` and never loads, so most of them read "found 0" / "answered" /
  "session/new" rather than the table's predicted wording; S5.2.2's red is exactly the predicted one (against a load without the window); S5.2.4's predicted
  "77, found 70" was replaced by a teeth run (with `Named` removed: *Expected … "end" … not to be "session_lost"*).
- **B9. E5.1's cell is not a `--cells M-K16` arm of the probe.** The scout ran M-K16 with a scratch driver (outside the repository) before the client had the
  two features; `acpTurn` gained them afterwards, unit-tested (S5.1.1–S5.1.2), for the next run. `endLikeTheShim` waits for 250 ms of no output after
  `session/cancel`, at most 1 s — what the driver measured (251–282 ms) — where the shim waits for no PENDING request (at most 1.5 s). The client's tree
  kill is by pid on POSIX too (`SIGKILL`; only the unit tests' fake agent runs there). The probe script is not edited, so item 8's stale "~250" label stays.
  M-K17 (§30) was not run.
- **B10. `KimiReport` gains no `SessionId`** — E5.4 decision 3 owns that. The shim prints `sessionId`; on a `--session` turn it is the asked id on every exit
  that prints a report (fixed in the review: `AcpOutcome.NotStarted(why, session)` at all four sites, and the client reports the asked id when the agent ended
  before the load — `EveryExitOfALoadTurn_NamesTheAskedSession`).
- **B11. The review's fixes** (Opus review, 2026-10-16): `ConsultantHandle`'s pattern ends `\z` (`$` let `abc\n` through; the TypeScript twin's `$` has no such
  hole and is unchanged); a `tool_call_update` after the load for an id the replay announced is dropped (it used to add an entry with an empty title and kind);
  the probe's tree kill is sent only while the child is `stillRunning`.
- **Open tail: no `--cells M-K16` arm in the probe.** M-K16 ran on the scout's scratch driver, outside the repository, so the measurement cannot be re-run from the
  repository. The probe library has the two features it needs (`loadUpdates`, `endLikeTheShim`); the cell itself is owed — before M-K16 is re-run on a new
  Kimi version, or with the live checks of RESULTS §31.

### E5.7 as built (Opus, 2026-10-16)

Numbered from 40, apart from the lists above, so the stories built in parallel do not collide on a number.

40. **The guard's consultant rule is stricter than the runtime rule, and its sentence says so.** S5.7.4's row predicted the refusal
    "… section does not name kimi". Against the real changelog that sentence cannot come: the `## Server 0.57.0` section names Kimi in
    almost every paragraph — as a REVIEWER — so the runtime rule (the name anywhere) lets a planted `kimi: '0.57.0'` through (watched:
    `actual ''`). `runtimeSinceIsAhead.test.ts` now reads each table through a `Gate`: `KNOWS` (as before; `api` exempt with its reason) and
    `CONSULTS`, whose section must name the runtime AND a consultant in ONE sentence (split after a full stop, also one inside a bold run),
    with no exemptions. The refusal ends "… does not name kimi as a consultant …". `vibe: '0.54.0'` passes on "Added: Mistral Vibe as a
    consultant and a question consultant."
41. **The reds read as the plan's, with two differences of wording.** S5.7.1's red is `+ {}` against the expected table (the stub the plan's
    compile-red rule asks for — an empty table, not `undefined`). S5.7.6's red names `en`, not `ru`: `HELP_LANGUAGES` starts with English.
42. **The note covers every runtime of the table, not only `kimi`**, and it is drawn in two places: the Consultant section
    (`consultSettings.consultantRuntimeSkewNote`, each runtime of the RESOLVED caller map named once, beside the definition skew note) and
    beside a question row (`QuestionRowView.serverNote`). So a `vibe` consultant on a server below 0.54.0 is named too — the table's purpose.
43. **S5.7.5 waits on E5.4's seed, and is its own commit.** `features.consultant` is the server's `ConsultantResolution.Consulting` too, so
    seeding `kimi` here would break the server's suite (`FeatureAvailabilityTests.cs:78`, `ConsultantLimitationsTests`) on this branch. The
    commit holds `kimiIsOfferedAsAConsultant`, the `CONSULTING` list in `consultant.test.ts` and the flipped `kimiRuntime.test.ts` pin
    ("neither a consultant" → "a consultant (S5)"). Watched red here (`actual 'a consultation runs on codex, claude, antigravity, local, api,
    vibe'`) and green with E5.4's seed applied in the working tree only (`kimi` appended after `vibe`, the generator run), then reverted.
    `featureAvailability.test.ts`'s frozen list is E5.4's (S5.4.12) and goes red under the seed until E5.4 changes it.
44. **The homes folder: more texts than the three `pathFindings.ts` lines, and one of the three left.** The owner and the consequence name both
    CLIs; the cure (`:245`) names no CLI, so there is nothing to widen. The registry's `noun`/`there` (`pathSettings.ts`), the box's own hint,
    the manifest's `markdownDescription` and *The consultant* article's setup paragraph in five languages say Kimi too, because each names
    the label. The help-coverage alias is `vibe and kimi homes folder`.
45. **Decision (e)'s sentence lives in *The consultant* article** (*What can go wrong*, five languages), because it is about a `consult` turn;
    the question-consultant article says only that a Kimi row's launches wait for one another and for a Kimi review on the same sign-in.
46. **`CONSULTANT_SINCE` gates a note, so `consultantsWire.ts` is unchanged.** S5.7.3 was green before any code, as the row said.

### E5.5–E5.6 skeleton (Sonnet, 2026-10-26) — deviations 47–52

The skeleton is written: the three `kimi` rows and vectors, `runtimeCapabilities.generated.ts` regenerated, `RuntimeCapabilities.Runtimes` and the
planner's `"kimi"` arm, the 12 scenario tests with their exact names, and a refusal of a plan that carries flags. Method bodies of the new tests and of
two production methods are fill regions (`QWEN-FILL-BEGIN … END`); no test was run, red or green.

47. **S5.6.2 and S5.6.4's neighbours are existing tests.** `TheVectorsActuallyLoaded_AndCoverEveryPairOfTheTable` needs no new method: the three vectors make it cover `kimi`. S5.6.4 `AdmissionFlagsBothRows` is a new test in `CapabilityMatrixTests`. The test's `Records` list gains `("RESULTS_kimi_confinement.md", "2026-10-13", "M-K")`, which `EveryRow_CitesTheRecord_AndACellOrSaysWhyThereIsNone` needs.
48. **The `disk` row's cells are `M-K6`, `M-K3`, `M-K4`, `M-K9`.** `M-K9 (e)` is named in the `resultRef` and the `note`, not as a cell id, because a cell id must start with the record's prefix `M-K` and the table's tests take it whole. The row's date is `2026-10-13` (the day the probe ran), its version `2.1.1`.
49. **`resultRef` section numbers** cite RESULTS by section and cell: `§9 M-K6`, `§6 M-K3`, `§7 M-K4`, `§12 M-K9`, `§21 M-K9 (e)`, and `§14` for the web row (read from `research/RESULTS_kimi_confinement.md`, not edited). The wording may need to follow RESULTS §31 once J2 is recorded.
50. **A new helper `KimiStandIn.Of(KimiFixture, params IDisposable[])`** (`src_mcp/tests/KimiStandIn.cs`) is the person, host and lock-root stand-in. It is shared by `AKimiConsultantCheckTests`, `AKimiQuestionRowTests` and `AKimiConsultationScenarioTests`, which adds its caller scope (`CallingAs`) through the `params` and no longer has its own copy.
51. **`KimiConsultant.MustCarryNoPlanFlags`** refuses a plan whose `Flags` OR `Leading` is not empty (decision 3 names "ANY flag"). It is a private method called first in `Build`, so S5.6.6 is a pure test.
52. **The two theory classes keep the plan's names.** S5.5.2 is a `[Theory]` over the turn's exit (0 and 70, the table's "second arm"); S5.6.7 is a `[Theory]` over `AKimiQuestionRowTests.Ending` (answered, refused before the launch, cancelled mid-turn). Both classes are in the `fakecli-env` collection because `GitScratch.AfterUnlink` is a static recorder.

### E5.5–E5.6 polish (Sonnet, 2026-10-26) — deviations 53–58

The 12 scenarios compile and pass on this host (Linux, the Windows host stood in by `WindowsStandIn`).

53. **S5.5.1–S5.5.4 are green on the code before E5.5–E5.6.** E5.3 g and E5.4 are in that code, so the tables' "expected red" (*before E5.4*, *before E5.3 g*) is history. Their reds were shown by mutation: with `VibeHomes.EmptiedUnderItsLock` not calling `KimiHome.DisposeAllUnder`, S5.5.2 (both exits), S5.5.3 and S5.6.7 (answered, cancelled) fail with *Expected `_unlinkedByTheDeleter` to be empty …*; with `ConsultCheckScratch.KimiHomesDisposed` not disposing, S5.5.4 fails the same way. Both mutations are reverted.
54. **S5.5.3 reads a record, not an exception.** A cancelled check does not throw: it writes the record with `failureKind` `cancelled` (`ConsultantCheck.cs:186`), and the test asserts that, then the home gone and the recorder silent.
55. **The `kimi` rows' reds differ from the tables.** Against the code at `b4e2ea96` the planner tests fail with *Expected type to be Planned, but found Refused* (the matrix refuses an unknown runtime first), and S5.6.7 with the row `blocked` — *'kimi' is not a runtime the question consultant can launch*. With the table back and only the planner arm and `KimiConsultant` at `b4e2ea96`, S5.6.3 fails as the table says (*Expected disk.Cwd to be Snapshot … found Scratch*), S5.6.6 as the table says (*no exception was thrown*), and S5.6.5 on the working directory.
56. **S5.6.7's refused and cancelled arms pin `finally`.** The refused arm (a plain folder, refused at its copy) and the cancelled arm already clean up in `QuestionRowLaunch`; their value is that they stay so. The answered arm is the one the recorder turns red.
57. **S5.6.7 names the row's scratch as the parent of the shim's `--cwd`** (`<scratch>\tree`, `ConversationSnapshots.TreeName`), and the one-shot folder as the parent of `--home`; both must be gone. The test does not sweep the machine's temp folder for `coai-question-*`.
58. **The `KimiStandIn` scope class is a private nested class of the helper**; `AKimiConsultationScenarioTests` uses it through `KimiStandIn.Of` (deviation 50).
59. **S5.6.7's refused arm does not assert the row's scratch is gone.** The shim never starts on that arm, so no launch names the scratch, and `PanelService` has no seam for `QuestionRowLaunch.MakeScratch` (an `internal` init property); the temp folder of the machine is not swept for `coai-question-*` (deviation 57). The arm asserts the one-shot folder, the recorder and the person's folder. The scratch's removal in `finally` on a refusal at the copy is pinned at the `QuestionRowLaunch` level by `AVibeQuestionRowReadsACopyOfItsRootsTests.ARowRefusedAtItsCopy_GivesItsOneShotFolderBack`.
60. **S5.6.6 has a second test for `Leading`.** `APlanWithLeadingArguments_IsRefusedForKimi` sets `Leading` and no `Flags` (deviation 51), so `MustCarryNoPlanFlags` cannot lose its `Leading` half unseen.

## Plan gate 2026-10-16 — the accepted findings folded, the rejected ones recorded

The coai plan gate on this document passed (verdict `proceed`). Its operator commands: the SPLIT is Fable's judgement (§0.2); a story
that touches authentication or security is built on Fable, an ordinary one on Opus (§0.2's table).

**Accepted, and where each went:**
- **(a)** `session_lost` (exit 70) mapped EXPLICITLY in the shim's contract and in both switches → E5.2 decision 9, S5.2.12. The host
  classifies it from the report's `end`, never from text, and the consultant REFUSES with a cure — never a fresh session in its place →
  E5.4 decision 11, S5.4.15.
- **(b)** the cumulative-usage delta guards a reset: a captured total below what was billed bills the report WHOLE — never a negative,
  never a flat 0 → E5.4 decision 12, S5.4.14.
- **(c)** how turns avoid a concurrent renewal, stated: the per-sign-in gate and the tree hold serialise every coai Kimi launch on one
  sign-in across conversations, rounds and servers, plus the foreign-`kimi` wait → E5.3 decision k, S5.3.16 (two conversations' turns
  started together on one sign-in: a peak of 1, the second waiting for the first).

**Rejected, with the reasons given to the gate:**
- **"Unlink the junction between turns."** Rejected: a junction renews nothing — only a RUNNING `kimi` refreshes the token — and every coai
  launch is already serialised on the sign-in (decision k; the gate, the tree hold and the foreign wait). Unlinking would add a make and a
  verify per turn, a write into the home that `Made`'s fresh-only rule (`KimiHome.cs:85`) exists to forbid, and a window in which the home is
  not what the sweep and the next turn expect — for no renewal avoided.
- **"Keep the check's home under `coai-kimi`."** Rejected: decision g makes every `coai-vh` owner junction-first, so the check's home is as
  safe there as a question row's; and `coai-kimi` is D20's RECORD-judged namespace (`ConsultantHomes.All`; the sweep reads a consultation
  id off each folder's name, `ConsultantHomeSweep.cs:187`) — a check has no record, so its folder would be judged foreign and kept for
  `ForeignAge` where the one-shot owner deletes it at once.

## Open questions

1. **The cornerstone mark — DECIDED 2026-10-16 (§0).** The quota stands at 2 of 2; E5.3 is not a cornerstone. Opus wrote its
   decisions (a–i); Fable gave its security judgement (R1–R11), added decisions j–k and the rows S5.3.15–S5.3.20, and E5.3 is built on
   Fable by the plan gate's command (§0.2). Closed.
2. **A re-login in the middle of a conversation (owner).** As built (D19), turn 2 is refused because `config.toml` no longer
   matches a fresh render, and the cure loses the vendor session. Alternative: when the ONLY difference is the person's slot
   values, coai re-renders `config.toml` before the turn (coai's render is authoritative either way, so a tampered file would be
   overwritten, not obeyed) and the session survives. It changes a D19 rule, so it is not decided here.
3. **`vibe: '0.54.0'` in the new consultant table (operator).** Proposed for the truth of one table. It can be dropped without
   changing Kimi's half.
4. **M-K17 (owner, for the spend).** 3 paid turns decide one sentence. Run it with M-K16, or leave the `none`/`disk` notes
   saying "announced and unmeasured".
5. **Renames (operator).** `VibeHomes`/`OneShotHome`/`coai-vh`, `VibeHomeTurns` and `coai.vibeHomesDirectory` now serve both CLIs.
   Not renamed here (E5.3 decision i, E5.7 decision 6).
6. **Kimi reviewer homes under TEMP** (E4 deviation 68) stay open. Moving them into `coai-vh` would reuse E5.3 decision g's
   disposal as is. That is a follow-up, not S5.

## Definition of Done (this epic)

- [ ] E5.1: §29 (M-K16) written before any E5.2 code, the branch of each E5.2 item named; §30 (M-K17) before any web sentence.
- [ ] E5.3's spec is final — Opus's decisions a–i, Fable's j–k and judgement R1–R11 (2026-10-16) — and its coder is Fable, every
      other story's Opus (§0.2, the plan gate's command).
- [ ] Every scenario of E5.2–E5.7 is a test that was watched red with the message in its table, then green; both suites green;
      `dotnet format --verify-no-changes` clean; both generators' `--check` clean.
- [ ] No test made a junction outside its own `testsToDelete-` folder, and none holds an inheritable handle in the test host.
- [ ] A Kimi conversation of three turns, a Kimi question row per admitted capability and one `--check-consultant` ran live
      through the released binary on Windows, recorded in RESULTS §31 — with the person's credentials folder keeping its names
      and every owned home disposed junction-first.
- [ ] `research/module_server.md`, `module_runners.md`, `module_core.md`, `module_extension.md` and `module_tests.md` updated.
- [ ] The parent's S5 block records this document's deviations (§"Where the parent plan and the code disagree") when the
      stories land, and the parent, E4 and this document promote together (planning-docs.md, *A plan split into epics and
      stories*).
