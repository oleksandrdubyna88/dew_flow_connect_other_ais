# PLAN — the feature review: a fourth gate, run once a whole plan is built, before release

> Status: **plan only, nothing implemented yet, 2026-09-25.** Scope: `src_mcp` (core, normalizer,
> runners, server, store), `src_vs_code` (panel, roles, rounds log, help, snippet), `src_server`
> (one exclusion only), `shared/`, `.agents/PROJECT.md`.
>
> Related docs: [architecture.md](../research/architecture.md) ("Three gates, not two"),
> [PLAN_review_document.md](../research/PLAN_review_document.md) (the precedent for adding a stage),
> [PLAN_consultant.md](../research/PLAN_consultant.md) (multi-turn conversations with a vendor),
> [PLAN_local_models.md](../research/PLAN_local_models.md) (the `--ask-local` HTTP shim this reuses),
> [module_core.md](../research/module_core.md), [module_server.md](../research/module_server.md),
> [module_runners.md](../research/module_runners.md), [module_extension.md](../research/module_extension.md).

## 1. The goal

The gate reviews a plan before it is built and each epic's diff after it is built. Nobody reviews
**the feature as a whole**: whether what shipped across all the epics is what the plan asked for,
whether the seams between epics hold, and what the implementer learned the hard way. By the time the
last epic lands, every reviewer has only ever seen a slice.

The feature review is a fourth stage, `review_feature`, called **once, at the very end of a plan** —
all epics implemented, pull requests possibly already merged, before the release. It sends the plan,
the epics, the implementer's own account of pitfalls/blockers/findings, the gate's own history for
this work, and an **AST outline** of every file the plan changed, to reviewers the person has ticked
for this stage — typically API-only models (xAI Grok, Qwen Max) that no CLI reaches and that took no
part in writing or reviewing the work. A reviewer that needs to see real code asks for it by name.

The idea is diversity of eyes, not policing: different models notice different things.

## 2. Decisions (confirmed by the operator, 2026-09-25 — do not reopen)

| # | Decision |
|---|---|
| D1 | **Blocking iff it can run.** Stage enabled AND at least one reviewer serves it → a full loop (rounds, `resolve`, `proceed` / `revise` / `call_human`; fixes land as NEW pull requests). No reviewer available → the round is **recorded as `skipped` with its reason and does not block**. Reviewers exist but **all fail** during the round → blocks (`call_human`, the existing `Answered == 0` path) — otherwise any network failure silently bypasses the gate. |
| D2 | Inputs: `repoPath`, `planPath`, `baseRef` (the commit before the first epic), `head` (branch or SHA, may be `origin/main`), `epics` (JSON), `lessons` (JSON). |
| D3 | **Only an AST outline is sent** — real names, signatures without bodies, line numbers at `head`. No raw code by default. |
| D4 | **Source on demand**: a reviewer may request the original code of **any file in the repository at `head`**, not only changed ones. |
| D5 | Outline languages: **C#, TypeScript, TSX, JavaScript, Rust, PHP, Python.** F# dropped (no grammar in `TreeSitter.DotNet` 1.3.0; building one ourselves was declined). |
| D6 | **No enforcement that the models differ** from those used earlier — a per-vendor `feature` tick is the whole mechanism. |
| D7 | `lessons` is **required and written by the calling AI** (pitfalls / blockers / findings). The server **automatically attaches the gate's history of this work**: rejected findings with their reasons, and consultations. |
| D8 | **One built-in role, `FeatureReview`**, editable in settings like every role. |
| D9 | A new runtime **`api`** (OpenAI-compatible HTTP, key from the vault). Presets are data. **The operator bought xAI (Grok) and Qwen keys on 2026-09-25**, so both presets and their dialects ship in v1 — each dialect written from a MEASURED answer (§6, S0.5), not from documentation. |
| D10 | **The Team server does not take part in v1.** |
| D11 | Caller instruction: a detailed **tool description** plus a **product-owned snippet half `coai-feature`**, like `consultantRule.md` — not a shared conventions rule. Hooking the stage into `/promote-plan` is a separate, later conventions plan. |
| D12 | **Fix the defects found on the way** (§9). |
| D13 | Session identity is the plan's **repository-relative path** (not its file name). |
| D14 | An optional **`again: true`** reopens a finished feature review (refused when `head` has not moved). |
| D15 | The source resolver **refuses credential-looking files** (`.env*`, `*.pem`, `*.key`, `*.pfx`, `*.p12`, `id_rsa*`/`id_ed25519*`/`id_ecdsa*`, and names in the existing `shared/credential-words.json`), with the reason, beyond D4's "any file". |
| D16 | `RoundEngine` is extracted from `PanelService` — as its own plan, [PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md), built **before** this plan's epic B so the stage's hooks land in the extracted engine rather than growing a 3056-line file. |

## 3. What exists today (verified 2026-09-25 against `origin/main` 21aba62e)

- `Stage` is an enum serialised by name — `src_mcp/core/Rounds/SessionState.cs:233`; `DocumentReview`
  was appended last. `rounds.stage` is `TEXT` (`src_mcp/src/Store/Schema.cs:46`) — a new stage needs no
  migration.
- Two exhaustive switches throw on an unknown stage (`PanelConfig.BucketFor`,
  `SessionState.cs:203`; `ProviderSettings.Reviews`, `src_mcp/src/Server/PanelSettings.cs:99`). Four
  silently default: `RoundSubject.StageName` `_ => "done"` (`core/Rounds/RoundSubject.cs:88`),
  `RoundRefusals.ReviewKindOf` `_ => "code"` (`src/Server/RoundRefusals.cs:83`), `CommandStageOf`
  `_ => Any` (`src/Server/PanelService.cs:2483`), `Finish` "The code stage is complete" for any `Done`
  (`PanelService.cs:2774-2780`).
- The round engine is one method, `RunStageAsync` (`PanelService.cs:1237`), parameterised by
  `StageRun` (`:3013`); an empty roster refuses at `:1333-1341`; `Answered == 0` → `CallHuman` at
  `core/Rounds/RoundMachine.cs:209-221`.
- Tree-sitter already ships inside the Native AOT binary (`src_mcp/normalizer`, `TreeSitter.DotNet`
  1.3.0, loaded by `new Language(library, entryPoint)` — `normalizer/TreeSitterNormalizer.cs:32`).
  Only C#/TS/JS are kept (`shared/kept-grammars.txt`); the package ships `tree-sitter-tsx` (1.5 MB),
  `-rust` (1.2 MB), `-php` (1.1 MB), `-python` (0.56 MB) for all nine RIDs. **No outline exists** —
  the only extraction is the anonymising skeleton of one method for the defect corpus.
- An HTTP reviewer exists: `LocalRuntime` + `coai-mcp --ask-local` (`runners/Reviewers/LocalRuntime.cs`,
  `LocalAsk.cs`, `src/Program.cs:1477-1634`). It sends **no `Authorization` header**, always sends
  `frequency_penalty` (an error on xAI reasoning models), and queues through `EngineLease` as if the
  endpoint were a GPU. It also answers **64** for missing arguments (`Program.cs:1491-1494`) — a
  violation of `.agents/PROJECT.md` ("a binary that KNOWS a one-shot mode must never exit 64").
- The runtime list is one set, `ReviewerRuntimeSelector.RuntimeNames` (`ReviewerRuntime.cs:493`); the
  Team server accepts `RuntimeNames − local` (`src_server/src/Vendors/VendorConfig.cs:38-41`) — so
  adding `api` there would make the Team server accept it unless excluded.
- Vendor keys come from one CredsForDevs `config` entry, keyed by vendor row id
  (`src/Server/KeyVault.cs`), and reach a child process through ENV, never argv
  (`CodexRuntime.KeyEnv`, `ReviewerRuntime.cs:303`).
- There is **no durable id for "a plan's work"**: sessions are repo+branch (+document), epics live on
  their own branches, and squash merges make epic SHAs unreachable from `main`. `rounds` carries
  `head_sha`, `base_ref`, `plan_text`, `subject`; `findings` carries `resolution` and `reason`;
  `consultations` carries `branch`, `head_sha`, `outcome`.

## 4. Architecture (chosen)

Three designs were costed — minimal (maximum reuse, strings for new meaning), clean (a descriptor per
stage, a session-subject union, extraction of the document stage) and pragmatic (reuse the engine,
new abstractions only where the minimal path would cause a defect). **This plan takes the pragmatic
one plus the clean design's stage descriptor**, because the descriptor is the thing that retires four
silent defaults at once and costs little. The session-subject union and the document-stage
extraction are deferred: they cost a second risky move inside this feature and buy nothing it needs.

### 4.1 A stage descriptor (`core/Rounds/Stages.cs`, new)

One exhaustive table replaces the scattered switches:

```csharp
public sealed record StageDescriptor(
    Stage Stage, RoleBucket Bucket, string Phrase, string Kind, CommandStage Commands,
    Stage AdvancesTo, string CompletedSentence, string ReviseSentence, bool RecordsSha,
    bool RunsOnTeamServer, NobodyPolicy WhenNobody);

public static class Stages
{
    public static StageDescriptor Of(Stage stage); // exhaustive; unknown => throw
    public static string PhraseOf(string persisted); // unknown persisted text => itself, never "done"
}
```

Routed through it: `BucketFor`, `RoundSubject.StageName`, `RoundRefusals.ReviewKindOf`,
`CommandStageOf`, `Finish`'s sentence, `revise`'s sentence in `AnswerFor`, `RoundMachine.Resolve`'s
next stage, the `Sha` write at `PanelService.cs:1428`. `ProviderSettings.Reviews` stays a throwing
switch (server layer) with a new arm. A test walks `Enum.GetValues<Stage>()`; the existing buckets are
pinned by literal.

### 4.2 The stage and its role

- `Stage.FeatureReview`, appended **last**. `RoleStages.Feature = "feature"`, bucket
  `RoleBuckets.FeatureCode = ("feature", programmingTask: true)`. `RoleCatalog.MustBeWellFormed`
  (`RoleCatalog.cs:273`) and `RoleComposition` (`:279`) accept it through one shared `RoleStages.All`.
- Built-in role `FeatureReview` in `shared/builtin-roles.json`, prompt `src_mcp/src/prompts/feature-review.md`,
  overridable in the data dir like every role (D8).
- `PanelConfig.FeatureDefault = (1 round, threshold 5)`; `GateFor` gains `COAI_MAX_ROUNDS_FEATURE` /
  `COAI_THRESHOLD_FEATURE` — without them the stage would silently read the `_CODE` keys. The panel
  mirrors the default (`panelServerDefaultsAgreement.test.ts`).
- **"The feature gate is enabled" is the role's own switch** (`COAI_ENABLED_FeatureReview` / the
  role tick). No second global setting.

### 4.3 The session

- `SessionState.Feature` (string, normalised at declaration like `Document`), `IsFeatureSession`.
  `SessionKey.For(repo, branch, document = "", feature = "")` appends `#feature:<key>` only when set;
  every existing key stays byte-identical (pinned by literal).
- The session's branch segment is the constant `":feature"` — a colon cannot appear in a git ref, so
  it can collide with no branch, and `head` (which moves as fix PRs land) is not part of the key.
- Identity = the plan's **repository-relative path** (D13), through `DocumentReader.IdentityOf` (repo
  confinement, symlink walk). A file-name identity would make `a/PLAN.md` and `b/PLAN.md` share
  findings, completion state and budget; the path's one cost — a session orphaned when `/promote-plan`
  moves the file — is rare, because the feature review runs before release and promotion after.
- `open` is not required: `review_feature` creates its own session and records the caller from the
  MCP handshake, as `consult` does.
- After `Done`, the only door is the optional **`again: true`** (D14, mirrors `review_code`), refused
  when `head` equals the last feature round's SHA.
- `SessionStore.Load/Exists/FileFor` and `SessionClaim` gain the optional `feature` argument; `Save`
  reads it from `State.Feature`, so the orphan sweep re-saves a feature session under its own key.

### 4.4 Skip against block — the truth table (D1)

| Situation | Decided at | Result |
|---|---|---|
| `FeatureReview` role disabled | roster empty → `BuildWork` empty | **skipped** — "the feature gate is switched off" |
| No vendor ticked `feature` (incl. an old extension that never writes the field) | `Serves(FeatureReview)` → nobody eligible | **skipped** — "no vendor is ticked to review features" |
| Vendors ticked, none can run (no key, no CLI, bad URL) | `CanRun` → empty; reason from `ExcludedFrom` | **skipped**, naming each |
| Reviewers ran, **all failed** (timeout, 401, 429 exhausted, unparseable — on any turn) | `CompleteRound`, `Answered == 0` | **`call_human`** — blocks |
| Some failed | normal path | proceed / revise / … with an honest summary |
| A Team server vendor ticked | `Serves(FeatureReview) = Feature && !IsRemote` in v1 | not asked, named in `ExcludedFrom` |

`skipped` lives **outside `RoundMachine`**: `StageRun.WhenNobody = RecordSkip` replaces the refusal at
`PanelService.cs:1333`. It writes a `RoundRecord { Verdict = "skipped", Sha = headSha }`, projects it to
the database with the reason in a new `rounds.note`, and **leaves the session state untouched** (no
`AwaitingResolve`, no budget spent, not `Done`) — so the next call, once a reviewer exists, runs a real
round. The answer is `verdict: "skipped"` with an instruction: *this does NOT block the release; tell
the person the feature review did not run, and why*.

Three things the skip path must get right (consultant, 2026-09-25, each verified against the code):

- **Its round number** comes from the durable journal (§9 item 6's fix: `max(numbers for this stage) + 1`
  under the session claim), never from the budget counter — or the next real round overwrites it.
- **`rounds.head_sha` is written from `RoundContext.HeadSha`** (`RoundsDb.cs:801`), not from
  `RoundRecord.Sha`; the skip path supplies both.
- **`stage.Begin` runs before the skip branch** (`PanelService.cs:1270`). That order is kept ON
  PURPOSE: a standing `call_human` from an earlier all-failed round is not dissolved by switching the
  reviewers off — otherwise un-ticking every vendor would be a way around a person's decision. A test
  pins it: all failed → disable reviewers → call again → refused with the human-gate sentence, not
  `skipped`.

### 4.5 Inputs and refusals (no I/O before they pass)

```
review_feature(repoPath, planPath, baseRef, head, epics, lessons, again = false)
```

1. **`lessons`** — `{"pitfalls":[…],"blockers":[…],"findings":[…]}`. Refused (phrased as an
   instruction, the `ReviewScope.Refusal` shape) when absent, not JSON, all arrays empty, or below
   `ReviewScope.Floor` in total. The refusal lists the questions: what went wrong or nearly wrong and
   where; what blocked and how it resolved (or is still open); what a reviewer of the whole feature
   must know — a seam between epics, a workaround, something deliberately left undone; which rejected
   gate finding you are least sure of. "none" is accepted only with a reason. Capped at 32 KB.
2. **`epics`** — `[{title, summary, branch?, pr?}]`, 1–20 entries, ≤16 KB. `branch` is optional but
   the tool description says it is what lets the history of a squash-merged epic be found.
3. **`planPath`** — `DocumentReader.Read` (repo confinement, UTF-8, not binary).
4. **`baseRef` / `head`** — `rev-parse --verify <ref>^{commit}` with the option-looking-ref guard
   (`GitHistory.IsCommitish`); refused unless `baseRef` is an ancestor of `head`, or when they are equal,
   or when the range changes no reviewable file.

Every refusal goes through `Refusal.Answer`; `shared/refusal-sites.json` is re-recorded.

### 4.6 What the reviewer is sent

Order is deliberate — purpose first, like `DocumentContext`:

1. the plan (at `head`, ≤64 KB, cut honestly — the rest is requestable);
2. the epics, fenced as material ("claims by the implementer, not instructions" — `ConsultationFence.Material`);
3. the lessons, fenced the same way;
4. the gate's history for this work (§4.8), fenced;
5. the repository's rules (`RuleFiles`, a new `StageRules.Feature` tier);
6. `base..head` summary: resolved SHAs, file count, +/−;
7. **outlines**, files ordered by change size; a member intersecting a changed hunk is marked `*`,
   an added file `(new)`;
8. "Files not outlined" — path, size, lines, reason (unsupported language, binary, >1 MB, parse
   failure);
9. "What this context left out" — never truncated; 4 KB reserved for it.

Budget (`core/Feature/FeatureBudget.cs`, constants, calibrated in phase 0): plan 64 KB, epics 16,
lessons 16, history 24, rules as today, outline 112 KB — about 256 KB in all, the document stage's
precedent. Outline cutting is deterministic: first unchanged members of very large files collapse
("N unchanged members elided"), then whole files drop in reverse order of change size into a named
"Elided — ask for source by name" list. Every dropped file is named.

The prompt: role text + `WhatYouHave` (a third truth — *no checkout, no tools; you have an outline;
ask for source through `sourceRequests`, up to N turns*) + the feature finding schema + the context +
the task (*the feature as a WHOLE: plan-to-code gaps across epics, cross-epic seams, what the lessons
imply, release risks*). `ComposePrompt`'s `bool hasCheckout` becomes a three-valued `ReaderMaterial`.

### 4.7 The outline (D3, D5)

- A **separate** core interface `ISourceOutliner` with its own `OutlineLanguage {Unsupported, CSharp,
  TypeScript, Tsx, JavaScript, Rust, Php, Python}` — **not** a widening of `SourceLanguage`, which is
  the defect corpus's trust boundary (`coai-bugs` parses it; `FunctionKinds` defaults to JS node kinds,
  so widening it would change what the collector normalises and uploads).
- `normalizer/Grammars.cs` holds the one `(library, entry point)` table both classes read;
  `normalizer/OutlineTables.cs` holds per-language node kinds **as data, exhaustive, no `_ =>`**;
  `normalizer/TreeSitterOutliner.cs` is the one algorithm. Signature = source from the node's start to
  its `body` field's start, whitespace collapsed, ≤240 chars — **no body by construction**. Special
  cases: Python `decorated_definition`, TS/JS `const f = () =>`, Rust `impl_item`, PHP's `php` grammar.
- A file whose ERROR-node share exceeds 20 % is reported "unsupported (parse failed)" with its size.
- `runners/Feature/FeatureOutlineBuilder.cs`: changed files from `base..head` (reusing
  `ContextAssembler.ComparisonBase`, `NumstatReader`, `DiffExclusions`), each read at `head` with
  `git show` (no worktree), hunks for the `*` marks from `git diff -U0 -M` through the existing
  `DiffSplitter`.
- `shared/kept-grammars.txt` gains `tree-sitter-tsx`, `-rust`, `-php`, `-python`; `KeptGrammarsTests`
  covers the union of both interfaces' languages.
- A one-shot **`--outline <file>`** mode for measurement and debugging — listed in `.agents/PROJECT.md`.

### 4.8 The gate's history "of this work" (D7)

There is no work id, so membership is proven by evidence, in a bounded window. `T0` = committer time
of `baseSha`; `S` = `git rev-list baseSha..headSha` (≤5000, else this proof is switched off and said);
`B` = `epics[].branch`. A round of this repository with `started_utc ≥ T0` belongs when **any** of:

- (a) `rounds.head_sha ∈ S` — merged or rebased epics;
- (b) `sessions.branch ∈ B` — **squash-merged** epics, whose SHAs are not in `main`;
- (c) a plan round whose `subject` matches the plan's **H1 heading** as `RoundSubject.From` would
  shorten it (`core/Rounds/RoundSubject.cs:29` — a subject is the file name only when a caller passed a
  path, and a shortened heading otherwise), or whose `plan_text` opens with that heading, with the
  window widened to `T0 − 90 days` (a plan is reviewed before `main` reaches `baseRef`).

**None of these is proof, and the context says so.** No stored field proves membership: a rebased
epic does not keep the SHA its code round reviewed, `rev-list base..head` includes unrelated work merged
in the same range, and a heading match is a coincidence waiting to happen. The caller's explicit
`epics[].branch` is the strongest association and is ranked first; everything else is attached as
**candidate** evidence, labelled with which rule admitted it. Before building (c), run
`SELECT subject, substr(plan_text,1,120) FROM rounds WHERE stage='PlanReview' LIMIT 20` on a real
database to see what subjects actually look like.

Consultations: same repository, `started_utc ≥ T0`, and `branch ∈ B` or `head_sha ∈ S`. Only
**rejected** findings (`resolution = 'reject'`) with reasons are attached, de-duplicated with the
existing `TextSimilarity`, plan before code, newest first, ≤24 KB. The feature session's own
rejections are excluded (the gate already counts them). The context carries honest counts: *"14
rejections from 6 rounds (branches feat/a, feat/b; plan round PLAN_x.md), 3 consultations; NOT
attached: 9 rounds in the same window with nothing tying them to this work"*. The history is context,
not a discount — external rejections are NOT seeded into `session.Rejections`. A database failure puts
"gate history unavailable: …" in the context and the round continues.

### 4.9 Source on demand (D4)

- **Schema.** `FindingSchema.FeatureJson` is derived from `FindingSchema.Json` by adding
  `sourceRequests: [{file, symbol|null, startLine|null, endLine|null, why}] | null` (in `required`,
  `additionalProperties: false`). `FindingSchema.Json` stays **byte-identical**, so no other stage
  changes — a code reviewer is never offered a field nobody would serve. `SchemaFile.Ensure` writes one
  file per shape. `RawReview` / `NormalisedReview.SourceRequests` (empty, never null); an invalid
  request is a named rejection, never a crash.
- **Where the loop lives.** Inside `BoundedScheduler.LaunchAsync`, around `RunWithLadderAsync`,
  through `ReviewerWork.Continue : IReviewerContinuation` (`None` for every other stage). Slots already
  surround `LaunchAsync` and its cancellation catch keeps sibling results (`BoundedScheduler.cs:244`,
  `:390`), so the reviewer's slot is **held for all its turns** — one reviewer, one conversation; the
  rate-limit ladder runs per turn; 429 on turn 2 retries turn 2. The loop emits **exactly one terminal
  outcome** per reviewer — an intermediate outcome would update stand-down accounting (`:346`) early.
  No new outcome subtypes.
- **The repair is rebuilt every turn.** Today it is composed once, before the first attempt
  (`PanelService.cs:2115`); a turn's repair must carry that turn's tail, or a malformed turn-2 answer is
  repaired against turn 1's prompt. The continuation therefore returns a whole `ReviewerWork`
  (invocation AND repair) per turn.
- **Usage survives a failed later turn.** The ledger and the round total read usage only from `Ok` and
  `Unparseable` (`UsageLedger.cs:123`, `LiveRound.cs:187`); a turn-2 timeout would make turn 1's spend
  vanish. Accumulated usage from earlier turns is carried on EVERY terminal outcome (an
  `EarlierTurns` usage on the outcome's base, read by both), and the ledger writes one entry per turn.
  Tests: a source request with non-zero usage → a malformed turn 2 → repair; separately, a cancellation
  during turn 2 — both keep turn 1's cost.
- **One continuation for every runtime: stateless resend.** Turn N+1's prompt = turn 1's prompt
  **byte-for-byte** + a tail (compact previous findings, the requests, the served code fenced with its
  path, lines and SHA, "not served: …", *only this turn's answer counts — repeat what you still stand
  by*; on the last turn *FINAL: sourceRequests will be ignored*). The identical prefix is what lets
  prompt caching work on Claude, OpenAI/codex and xAI; "turn k of N" appears only in the tail. CLI
  resume (`codex exec resume`, `claude --resume`, `agy --conversation`) is **not** built — codex review
  runs are `--ephemeral`, which kills resume — unless phase 0 shows resending is too expensive.
- **Only the final turn's findings count.** A failed later turn is a failed reviewer — falling back
  to turn 1's findings, made while waiting for source, could give a false `proceed`.
- **`SourceResolver`** (runners) serves only git objects at the pinned `headSha` — never the working
  tree, never an uncommitted edit, no path escape (`IsRepoRelative`, moved to core as
  `RepoPaths.IsRelative`). `symbol` → outline lookup (up to 3 overloads; unknown symbol → refusal
  listing up to 20 names in the file); `lines` → clamped, ≤400 lines; neither → whole file if ≤16 KB,
  else its head plus "ask for lines". Limits: ≤8 requests/turn, 48 KB/turn, 128 KB/reviewer, 3
  follow-up turns. One `git show` per file per round (cache). Credential-looking files are refused
  with the reason "not served: looks like a credential file" (D15) — and so are lock files and build
  output (`DiffExclusions`).
- `COAI_FEATURE_SOURCE_FOLLOWUPS` counts **follow-up** turns only (default 3, so at most 4 turns in
  all; **0 = loop off, outline only**) — the rollback switch. Every limit in this plan is stated in
  follow-ups, never in total turns.
- **The round deadline scales the PER-REVIEWER duration, not the reviewer count.** `RoundBudget.For`
  divides reviewers by concurrency into waves (`core/Rounds/RoundBudget.cs:28`), so passing
  `roles × turns` as the reviewer count buys nothing when they fit in one wave — one reviewer, cap 3,
  four turns at ten minutes would still get ten minutes, though its turns run sequentially. The
  derivation passes `reviewerTimeout × (1 + follow-ups)` as the reviewer timeout instead, and an
  explicit round-timeout override (`PanelService.cs:2389`) is kept as it is.

### 4.10 The `api` runtime (D9)

- `runners/Reviewers/ApiRuntime.cs` — self-invocation of `coai-mcp --ask-api --endpoint --model
  --prompt-file --schema-file --out --timeout-seconds --max-tokens --dialect [--reasoning-effort]`.
  The key travels **only** as `COAI_API_KEY` in the child's ENV. `SharedResource` empty — machine lane,
  per-provider cap, **no `EngineLease`**, not subject to the GPU stand-down.
- `src/Api/AskApiMode.cs` — `POST {base}/chat/completions` with `Authorization: Bearer`. Exit codes:
  0 ok; 65 bad arguments or no key (**never 64** — the mode is known); 75 on 429/503 with the phrase
  `HTTP 429 Too Many Requests … try again in Xs`, which `RateLimit.Hit` already recognises; 77 on
  401/403 ("the API refused the key for vendor '<id>'", body not echoed); 69 network; 70 other. Listed
  in `.agents/PROJECT.md`.
- **Dialects are data** — `shared/api-dialects.json`, embedded in core and mirrored by a TS check:
  which of `temperature`/`seed`/`frequency_penalty` to send, `max_tokens` vs `max_completion_tokens`,
  `json_schema` vs `json_object`, the reasoning-effort map. `LocalAsk.RequestBody` becomes
  `ChatRequest.Body(dialect, …)`; the **local body is pinned by a golden byte test before the
  refactor**. v1 ships three dialects — `openai` (generic), `xai` and `qwen` — and the last two are
  written **from the answers measured in S0.5**, never from documentation alone: the precedent is
  `ReviewerExecutor.cs:166-172` ("nothing goes in this list that has not been read off a real vendor
  answer"). Documentation says, and S0.5 must confirm or refute: xAI reasoning models reject
  `frequency_penalty`/`presence_penalty`/`stop` and take their own `reasoning_effort` values; DashScope
  offers strict `json_schema` only on some Qwen models and `json_object` on others.
- **Presets** (`shared/api-presets.json`, mirrored in the panel): **xAI** — `https://api.x.ai/v1`,
  dialect `xai`; **Qwen** — the DashScope OpenAI-compatible endpoint of the operator's region, dialect
  `qwen`. The model ids are whatever S0.5 finds the operator's keys can call (`GET /models`), not
  names copied from a web page.
- `RuntimeResolution.NameOf`: `api` **before** the `baseUrl → codex` branch (`RuntimeResolution.cs:85-94`)
  — otherwise an api row silently runs through codex. `AuthOf`: no key → unavailable "needs a key under
  '<id>' in the vault"; no base URL → unavailable. `VendorProbe`: `api` branch — key present, URL
  well-formed, "endpoint not contacted" (a live `GET /models` is later).
- `ReviewerRuntimeSelector.MachineOnlyRuntimes = {"local", "api"}`; the Team server's `KnownRuntimes`
  becomes `RuntimeNames − MachineOnlyRuntimes` (D10). The consultant refuses `api` by name in v1.
- The `api` runtime is useful on its own at every stage — it ships as its own epic, before the stage.

### 4.11 Where the code lives

`PanelService.cs` is ~3056 lines against the 800 the coding rule allows. D16 moves the round engine
(`RunStageAsync`, `BuildWork`, `ComposePrompt`, `WhatYouHave`, `AnswerFor`, `StageRun`) out first, in
its own plan; this feature's hooks (the `StageRun` fields, the skip branch, `ReaderMaterial`) then land
in `RoundEngine`, the stage's entry point is a small `src/Server/Stages/FeatureStage.cs` (≤300 lines),
and the rest lives in dedicated classes (`FeatureSessions`, `FeatureOutlineBuilder`, `SourceResolver`,
`GateHistoryQuery`, `FeatureInputs`, `FeatureContext`). Line numbers in this plan are against
`origin/main` 21aba62e — BEFORE that move — and are re-read after it.

### 4.12 Storage

- One schema step appended to `Schema.Steps`: `ALTER TABLE rounds ADD COLUMN note TEXT NOT NULL
  DEFAULT ''` — the skip reason. Turns and served source go into the existing `reviewers.note`
  ("3 turns; source: 5 file(s), 38 KB; refused 1").
- `--log` gains `note`; absent means `''` (the "empty string is the one spelling of no data" rule).
- Session file: `SessionState.Feature`; `PlanText` = plan; `RoundRecord.Sha` = `headSha`; the last
  round's `FeatureInputs` (baseSha, epics, lessons) for `status`.

### 4.13 Both halves, old and new (measure before shipping — `.agents/PROJECT.md`)

| Seam | New → old | Old → new | Measure |
|---|---|---|---|
| vendor `feature` field | old server ignores it (verify no `UnmappedMemberHandling.Disallow`) | absent = false → skip "no vendor ticked" | phase 0 |
| `runtime: "api"` | **dangerous**: an old server's `RuntimeOf` (`PanelSettings.cs:1087`) turns an unknown runtime into codex + baseUrl — silently the wrong vendor | never written | panel gate `API_RUNTIME_SINCE`: rows disabled against an older installed server with "needs coai-mcp ≥ X" |
| extension rollback | an old `vendorsFrom` rewrites `api` as codex on next save | — | release note; the old code cannot be fixed |
| session file with `Stage: "FeatureReview"` | old sweep must skip it (`JsonException`, `SessionStore.cs:455-463`) | — | phase 0; fallback `sessions/feature/` |
| database `user_version` | old binary as reader/migrator | — | against the released artefact |
| `--log` verdict `skipped`, stage `FeatureReview`, `note` | old extension shows raw strings | — | phase 0 |
| Team server `/api/catalog` | `FeatureReview` would enter `AcceptedRoles` on rebuild — which holds bare role ids, and whose `AllowAny` bypasses the catalog altogether (`src_server/src/Jobs/AcceptedRoles.cs:103`, `:178`) | — | `AcceptedRoles` filters the BUILT-INS by stage; tests with `AllowAny = true` and with `FeatureReview` named in `Coai:ExtraRoles`. The client never sends a feature role to a Team server (`Serves` excludes remote rows), so this is the second line, not the first |
| api version gate | — | — | must **suppress `api` rows from the emitted `COAI_VENDORS`** for an older installed server, not merely disable them in the UI — the old server reads the settings file, not the panel |

### 4.14 The extension

- `models.ts` `RUNTIMES += 'api'` (not in chat or consultant pickers). `vendors.ts`: `feature?`
  (absent = false; forced false for a Team server row with "Team servers do not run feature reviews
  yet"), `dialect?`; presets "xAI (Grok)", "Qwen (DashScope)" and a generic "API (OpenAI-compatible)",
  read from `shared/api-presets.json`.
- The vendor card's fourth switch "reviews features"; roles grouped Plan / Code / Document /
  **Feature**; the stage's budget and switch.
- `roles.ts` `FEATURE_CODE`, exhaustive `bucketOf`; `rolesPage.ts` offers the stage;
  **`settingsShape.ts:728` `enabledCodeRoles` becomes a bucket filter** (today it would count a feature
  role as code).
- `rounds.ts` `stageName` covers Document and Feature review (unknown → raw); `roundsLog.ts` renders
  `skipped` as its own neutral state ("skipped — did not block") and shows `note`.
- Help in all five languages in one commit.
- Snippet half: `src_vs_code/src/featureRule.md` (`<!-- coai-feature v1 -->`), `FEATURE_VERSION = 1`, a
  `KNOWN_HALVES` row, `prepare-gate.mjs` emission, `ARTEFACT_VERSION` 11 → 12, the "(v12)" menu title.

## 5. One call, end to end

```mermaid
sequenceDiagram
  participant AI as Caller AI
  participant T as Tools.review_feature
  participant F as FeatureStage
  participant R as RunStageAsync
  participant O as FeatureOutlineBuilder
  participant H as GateHistoryQuery
  participant S as BoundedScheduler
  participant V as Reviewer (api / CLI)
  participant SR as SourceResolver
  AI->>T: repoPath, planPath, baseRef, head, epics, lessons
  T->>F: ReviewFeatureAsync
  F->>F: FeatureInputs.Parse (refusals, no I/O)
  F->>F: plan via DocumentReader; rev-parse base/head; is-ancestor
  F->>F: FeatureSessions.OpenOrContinue (repo#:feature#feature:plan)
  F->>R: StageRun{BeginFeatureRound, Feature, Head, WhenNobody=RecordSkip, Turns}
  R->>O: outline of base..head at headSha, budget
  R->>H: history (T0, rev-list, epic branches, plan subject)
  R->>R: FeatureContext -> BuildWork(FeatureJson, continuation)
  alt nobody can review
    R-->>AI: skipped + reason (recorded, does not block)
  else reviewers
    R->>S: RunAllAsync
    loop per reviewer, turn <= 1 + follow-ups
      S->>V: base prompt (+ tail)
      V-->>S: findings + sourceRequests
      S->>SR: serve (git show headSha:path, caps)
      SR-->>S: fenced source
    end
    S-->>R: last-turn outcomes, usage summed
    R->>R: dedup, GateRule, CompleteRound (Answered==0 -> call_human)
    R-->>AI: proceed / revise / good_enough / call_human
  end
  AI->>T: resolve(..., feature=planPath); fixes as new PRs; review_feature(head=new)
```

## 6. Phase 0 — measure before building (no product code)

| Spike | Question | Pass/fail |
|---|---|---|
| S0.1 grammars | Do `tree-sitter-tsx/rust/php/python` load by their entry points (`tree_sitter_php` vs `_php_only`) on win-x64 and linux-x64 **in the AOT publish**? Publish size delta? Outline quality on 20 real files per language | ≥95 % of named declarations against a regex baseline; ERROR share per language recorded |
| S0.2 budget | A throwaway `--outline` over three shipped features of this repo (the consultant, `review_document` PR #230, the S8 notices) | bytes, file counts, `git show` vs `cat-file --batch` time; calibrate the 112 KB / 400-file limits |
| S0.3 turns | Two hand-driven resend turns on codex, claude, agy and one OpenAI-compatible API | share of useful requests; does the model stop on FINAL; cache hit on turn 2 (`cache_read` / `cached_tokens`); resume (C3) only if turn 2 costs >40 % of turn 1 |
| S0.5 xAI and Qwen | With the operator's keys (through the vault, never on argv): `GET /models`; one `chat/completions` per candidate model with the review schema as strict `json_schema`, then as `json_object`; with and without `frequency_penalty`, `seed`, `temperature`; each `reasoning_effort` value; a 429 and a 401 provoked on purpose; a second turn with the same prefix | per vendor: the model ids the key can call, which fields are refused and with what verbatim error, whether strict schema holds, the effort values accepted, the exact 429/401 text (for `RateLimit.Hit`), cached-token counts on turn 2, cost of one review-sized call. These rows ARE the `xai` / `qwen` dialects |
| S0.4 old side | Previous RELEASED `coai-mcp` against a data dir with a feature session, `COAI_VENDORS` with `api`+`feature`, the new DB; previous released extension against the new `--log` | each row of §4.13 recorded with versions |

Results are written into this plan before epic A's code.

## 7. Build order (epics; the coai gate once per epic)

- [ ] **Epic 0 — phase 0 spikes** (§6).
- [ ] **Prerequisite — [PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md)**
  (D16), before epic B. Epic A may run in parallel: A1–A5 touch the engine's call sites only where A1
  fixes a defect, and those fixes are landed first so the move carries them.
- [ ] **Epic A — foundation**
  - [ ] A1 the defects of §9 (each RED first), shipped on their own — item 6 (round numbering) first,
    because the skip path depends on it.
  - [ ] A2 the stage descriptor (§4.1) — behaviour-preserving apart from A1's fixes.
  - [ ] A3 the `api` runtime (§4.10), C# + `--ask-api` + the `openai`/`xai`/`qwen` dialects and the
    xAI and Qwen presets from S0.5 + Team server exclusion + panel UI and version gate. Useful on every
    stage by itself — Grok and Qwen become reviewers of plans and code the day it ships.
  - [ ] A4a outliner for C#/TS/TSX/JS + `--outline`; A4b Rust/PHP/Python; `kept-grammars.txt`.
  - [ ] A5 feature schema + parser (`FeatureJson`, `SourceRequests`) — other stages untouched.
- [ ] **Epic B — the stage**
  - [ ] B1 core: `Stage.FeatureReview`, bucket, defaults and ENV keys, role validators, the built-in
    role and prompt, `Serves(Feature)`, `SessionState.Feature` and key, `BeginFeatureRound`, the
    `AcceptedRoles` exclusion.
  - [ ] B2 `review_feature`, **outline only** (turns = 1): inputs and refusals, git refs,
    `FeatureSessions`, `FeatureOutlineBuilder`, `FeatureContext`, `RunStageAsync` hooks, skip +
    `rounds.note`, `feature` on `resolve`/`status`/`ask_human`, the tool description, contract and
    scenario tests. **Useful by itself.**
  - [ ] B3 the gate history (§4.8).
- [ ] **Epic C — source on demand**
  - [ ] C1 `SourceResolver`.
  - [ ] C2 the turn loop: `IReviewerContinuation`, scheduler, `Ok.Turns/Served`, the tail prompt, the
    scaled deadline, FakeCli turn switching, end-to-end.
  - [ ] C3 (conditional on S0.3) CLI resume.
- [ ] **Epic D — the extension**: D1 vendor switch, role groups, `enabledCodeRoles`, budgets,
  `FEATURE_SINCE`; D2 rounds log and sidebar (`skipped`, `note`, stage names); D3 help ×5 and the
  `coai-feature` snippet half, artefact v12.
- [ ] **Epic E — documentation and promotion**: `research/module_core|server|runners|extension|tests.md`,
  `architecture.md` ("Four gates"), CHANGELOG, `/promote-plan`.

## 8. Test plan

xUnit v3 through the MTP executables (never `dotnet test`); extension pages tested by RUNNING them
(bundled page harness), never by asserting on page source text.

- **Defects (§9)** — RED first, each named after the guarantee: a document `call_human` names the
  document gate, not "done"; resolving a document review does not say "the code stage is complete";
  `ask_human` for a document session carries that session's id; `ReviewKindOf` / `CommandStageOf` /
  `StageName` answer every `Stage` value; `--ask-local` without arguments never exits 64;
  `stageName('DocumentReview')` in the extension.
- **Core** — `Stages` exhaustive; existing buckets and session keys pinned by literal; the feature
  key collides with no branch or document key; `BeginFeatureRound` refusals (four sentences), `again`
  with the same head refused and with a new head fresh; `FeatureInputs` — every empty shape of
  `lessons` refused **with all four questions in the text**; `FeatureBudget` never exceeded and the
  elision list always whole (property test); the schema: `Json` byte-identical, `FeatureJson` meets
  the OpenAI strict rules; invalid source requests named.
- **Normalizer** — per language: golden fixtures, the **no-body property** (no line unique to a body
  appears in the outline), parse-failure threshold; every grammar loads; `KeptGrammarsTests`; the
  collector's `.tsx` behaviour unchanged.
- **Runners** — `FeatureOutlineBuilder` on a real temporary repo (A/M/D/R, binary named, unsupported
  with size, `*` marks, deterministic cutting naming every dropped file, reads `head` not the dirty
  tree); `SourceResolver` (traversal refused **without starting a process**, uncommitted edit not
  served, symbol/overloads/lines, caps, one `git show` per file); the turn loop (stops on no request,
  on the cap, on the budget; usage summed; a failed turn 2 fails the reviewer; per-provider peak does
  not rise; cancellation reported); **turn N+1's prefix equals the base prompt byte for byte**;
  `api`: no argv element equals the key, the key is in ENV, dialect bodies, the local golden body, a
  `HttpListener` stub (429 → rate-limit ladder, 401 → named sentence without the key), `NameOf` order,
  `AuthOf`, the Team server rejects `api`, the consultant refuses `api`.
- **Server** — `AFeatureIsReviewedEndToEndTests` (a `ScenarioCoverageTests` scenario): skip → tick a
  vendor → the outline arrives (recorded stdin) with no body text → turn 2 serves a symbol → only the
  last turn's findings count → `resolve(feature)` → revise → again with a new head → proceed → done.
  The skip truth table, row by row. History: squash branch via `epics.branch`, merge via rev-list, an
  unrelated branch in the window NOT attached and counted, a plan round before `T0` found by subject, a
  consultation by branch, no database → a sentence and the round runs. `McpContractTests`,
  `refusal-sites.json`.
- **Extension** — vendors (`feature` absent = false, Team server forced false, `api` survives
  `vendorsFrom`, version gates); `enabledCodeRoles` (**RED: it counts a feature role today**); run
  `rolesPage` / `roundsLog` (a feature group, a skipped row); the snippet (halves, v12, hash);
  `generatedFilesAreCurrent`, `builtinRoleCatalog`, `panelServerDefaultsAgreement`, help coverage ×5.

## 9. Defects found on the way (D12 — fix, RED first)

1. `RoundSubject.StageName` `_ => "done"`: a document review's `call_human` notice reads "The **done**
   gate needs your decision" (`RoundSubject.cs:88-93`, used at `PanelService.cs:1852`).
2. `Finish` says "The code stage is complete" when ANY session reaches `Done`, a document review
   included (`PanelService.cs:2774-2780`).
3. `ask_human` always loads the branch session (`PanelService.cs:2805`): for a document session it
   shows the wrong pending findings and files the question under the branch session's id, so the
   person's answer (`Escalations.DecisionFor`) never reaches the document session.
4. `RoundRefusals.ReviewKindOf` `_ => "code"` and `CommandStageOf` `_ => Any` — silent defaults
   (retired by §4.1).
5. `src_vs_code/src/rounds.ts` `stageName` does not know `DocumentReview` — the log shows the raw enum.
6. **A later round overwrites an earlier one in the log.** `LiveRound` numbers a round
   `RoundsRunThisStage + 1` (`src/Server/LiveRound.cs:232`); `BeginCodeRoundAgain` resets that counter
   to 0 (`core/Rounds/RoundMachine.cs:176`), and so does a human `Continue`/`Fix`; the database upserts
   on `(session_id, stage, number)` (`src/Store/RoundsDb.cs:777`). So: finish code round 1, resolve,
   advance HEAD, run `again` — the second round REPLACES the first round's row. Confirmed from the code
   path by the consultant and re-read here; the RED test is exactly that sequence. Fix: a round's
   number is `max(existing numbers for this session and stage) + 1`, allocated under the session claim
   and independent of the budget counter, which keeps counting rounds for the budget only. **A
   prerequisite of the skip path** (§4.4).
7. `--ask-local` exits **64** on missing arguments (`Program.cs:1491-1494`) — must be 65.
8. `settingsShape.ts:728` `enabledCodeRoles` treats "not plan" as code — becomes wrong the moment a
   third result bucket exists.
9. Stale text: `normalizer/CoaiMcp.Normalizer.csproj:9-11` says it is not referenced by `CoaiMcp.csproj`
   (it is, `:35`); `TreeSitterNormalizer.cs:60-67` says TS and TSX share one grammar file (the package
   ships a separate `tree-sitter-tsx`); `Tools.cs:7` says nine tools (there are ten).

## 10. Open questions for the operator

All four were answered on 2026-09-25 and are now D13–D16. Still open, both needed before S0.5:

- **Q5** The Qwen key's DashScope region — international (`dashscope-intl.aliyuncs.com`), US, or
  mainland (`dashscope.aliyuncs.com`)? A key works only on its own region's endpoint.
- **Q6** How the keys reach the measurement: in coai's own vault `config` entry under the vendor row
  ids (the product's path, preferred), or as CredsForDevs entries opened for `use`?

## 11. Not in this plan

- The Team server as a feature reviewer (D10) — a separate plan, as plan 5 was for documents.
- Hooking the stage into `/promote-plan` (conventions) — after the gate has run in practice.
- CLI resume for source turns — only if phase 0 says so (C3).
- A per-model context-window check; budgets are constants in v1.

## Definition of Done

- [ ] Phase 0 results recorded in this plan, and every budget constant traced to a measurement.
- [ ] Every §9 defect has a test that was seen RED with the real symptom, then GREEN.
- [ ] `review_feature` runs end to end on this repository against **Grok and Qwen through `api`** and
      one CLI reviewer, with a source request served.
- [ ] The `xai` and `qwen` dialects each trace to an S0.5 row with the verbatim vendor answer.
- [ ] The skip truth table (§4.4) is a test, row by row; "all failed" blocks.
- [ ] No argv element and no log line ever contains an API key (a test scans both).
- [ ] Every row of §4.13 was measured against the previous RELEASED other half.
- [ ] `.agents/PROJECT.md` lists `--ask-api` and `--outline`.
- [ ] All three suites green through their executables / `npm test`; plan-lifecycle and pin checks
      green.
- [ ] `research/architecture.md` and the module docs describe four gates; CHANGELOG written; this
      plan promoted with its deviations recorded.
