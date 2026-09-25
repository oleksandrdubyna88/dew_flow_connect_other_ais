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
| D16 | `RoundEngine` is extracted from `PanelService` — as its own plan, [PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md), built after this plan's Epic 1 and **before** its Epic 2 (§7.1), so the stage's hooks land in the extracted engine rather than growing a 3056-line file. |

## 3. What exists today (verified 2026-09-25 against `origin/main` 21aba62e)

- `Stage` is an enum serialised by name — `src_mcp/core/Rounds/SessionState.cs:233`; `DocumentReview`
  was appended last. `rounds.stage` is `TEXT` (`src_mcp/src/Store/Schema.cs:49`) — a new stage needs no
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
- `PanelConfig.FeatureDefault = (1 round, threshold 5)`, routed at ALL THREE sites that pick a default
  today — `GateFor` (`PanelSettings.cs:1200-1202`, `isPlan ? "PLAN" : "CODE"`), `PanelConfig.ShippedFor`
  (`SessionState.cs:131-132`) and the `Defaults` dictionary (`:120`); `GateFor` gains
  `COAI_MAX_ROUNDS_FEATURE` / `COAI_THRESHOLD_FEATURE` — without them the stage would silently read the
  `_CODE` keys. The panel
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
  MCP handshake, as `consult` does. **The session is created under the engine's own claim**
  (`StageRun.CreateIfAbsent`): today `RunStageAsync` refuses without a session (`PanelService.cs:1260-1264`)
  and the document stage creates OUTSIDE the claim (`:921`, before `:1254`) and narrows the race by
  re-reading (`:871-874`) — two concurrent first calls on one plan would be two creators.
- After `Done`, the only door is the optional **`again: true`** (D14, mirrors `review_code`), refused
  when `head` equals the last feature round's SHA.
- **The session records the resolved `baseSha`** of its first round (`PersistedSession.FeatureBase`).
  A later call on the same plan with a DIFFERENT base — the same plan reviewed on a release branch, a
  backport, a caller who mistyped — is refused with a sentence naming both SHAs, rather than silently
  inheriting the other work's rejections and budget; `again: true` is the door, and it starts a fresh
  review against the new base (earlier rounds stay in the log). Same base, moved head is the normal
  fix-and-rerun path. (Plan round, 2026-09-25 — a path alone could not tell two release trains of one
  plan apart.)
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

1. **`lessons`** — `{"pitfalls":[…],"blockers":[…],"findings":[…]}`. **Each of the three arrays must
   be non-empty**; "nothing here" is written as an entry that says so and why (`"none — every epic
   merged without a blocker; the one risk was X and it did not happen"`), never as `[]`. Refused
   (phrased as an instruction, the `ReviewScope.Refusal` shape) when absent, not JSON, a key is
   unknown, ANY of the three arrays is empty, a "none" entry carries no reason, or the total is below
   `ReviewScope.Floor`. The refusal names which array was empty and lists the questions: what went
   wrong or nearly wrong and where; what blocked and how it resolved (or is still open); what a
   reviewer of the whole feature must know — a seam between epics, a workaround, something deliberately
   left undone; which rejected gate finding you are least sure of. Capped at 32 KB. (Plan round,
   2026-09-25: "all arrays empty" and "none only with a reason" contradicted each other for one empty
   array.)
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
  `HTTP 429 Too Many Requests … try again in Xs` (and `HTTP 503 Service Unavailable` for a 503), which
  `RateLimit.Hit` already recognises — it matches the TEXT with a non-zero exit
  (`ReviewerExecutor.cs:145-157`), never the exit code alone; 77 on
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
  `qwen`. **The operator's choice (2026-09-25): the MAX model of each vendor** — Qwen **3.8 Max**, and
  the top Grok the key offers (the operator recalled "3.7"; the exact id is read, not recalled). The
  model ids are whatever S0.5 finds the operator's keys can call (`GET /models`), not names copied
  from a web page — a Token Plan key in particular may expose a narrower list than the public one.
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
- `--log` gains `note`, read through `RoundsQuery.cs:812`'s `pragma_table_info` — an older database has
  no such column, and selecting it unconditionally is exactly how a new column once emptied the whole
  rounds list (`PLAN_consultant.md:610-613`); absent means `''` (the "empty string is the one
  spelling of no data" rule).
- **Volume and retention** (plan round, 2026-09-25). A feature review runs once per plan, plus one
  round per fix-and-rerun: at this repository's pace (about one plan a day at the busiest) that is a
  handful of `rounds` rows a day, each a few KB with its `plan_text` — the same order as a code round,
  which the log already keeps. Real rounds follow the existing rule: **every round is kept**, because
  the log is the product's record and its export is how a person keeps it elsewhere. The one shape that
  could grow without an owner is the **skipped** row — a caller retrying with no reviewer ticked, over
  and over. So **consecutive skips on one session coalesce**: a skip whose reason equals the previous
  round's skip reason updates that row (`completed_utc`, a repeat count in `note`) instead of appending
  one, and only a changed reason or a real round opens a new number. Test: ten identical skips → one
  row saying "×10".
- Session file: `SessionState.Feature`; `PlanText` = plan; `RoundRecord.Sha` = `headSha`; the last
  round's `FeatureInputs` (baseSha, epics, lessons) for `status`.

### 4.13 Both halves, old and new (measure before shipping — `.agents/PROJECT.md`)

| Seam | New → old | Old → new | Measure |
|---|---|---|---|
| vendor `feature` field | old server ignores it (verify no `UnmappedMemberHandling.Disallow`) | absent = false → skip "no vendor ticked" | phase 0 |
| `runtime: "api"` | **dangerous**: an old server's `RuntimeOf` (`PanelSettings.cs:1087`) turns an unknown runtime into codex + baseUrl — silently the wrong vendor | never written | panel gate `API_RUNTIME_SINCE`: rows disabled against an older installed server with "needs coai-mcp ≥ X" — and SUPPRESSED from the written settings, which needs the installed version threaded into the writer: `envBlock(settings, vendors)` (`settingsShape.ts:459-463`) → `vendorsEnv` (`vendors.ts:413-419`) has no such input today; `sameVendors(vendors, DEFAULT_VENDORS)` (`:461`) must still emit the rest |
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

Each spike is folded into the story whose code it measures (§7.1); its rows are that story's acceptance evidence.

## 7. Build order — three epics, nine stories, the coai gate once per epic

> Operator's order (2026-09-25), which outranks the defaults: 2–3 epics of 2–3 logically complete
> stories; the gate runs once per EPIC — its own branch from the previous epic's commit,
> `review_plan` with that epic's plan, every story built without gating it on its own, ONE commit,
> `review_code` over the whole diff with the previous epic's commit as `baseRef`, fixes folded into
> that one commit. The split was made on Fable; ordinary stories run on Opus; anything where being
> wrong is expensive — keys and credentials, data migration, architecture — runs on Fable. Every
> story names its model and the reason, and its summary says so (`subagent-models.md`).

### 7.0 How the gate runs, per epic

- Branches `feat/feature-review-e1`, `feat/feature-review-e2`, `feat/feature-review-e3`, each in a
  worktree of its own (`D:\rsd\_wt\coai-feature-e1` …), created from the commit the previous epic
  left on `main` (`task-lifecycle.md` §1).
- "The previous epic's commit" is read literally where nothing landed between, and as the epic's
  branch point otherwise: E2 starts from `main` AFTER the round-engine move (§7.1), so its `baseRef`
  is that commit — E1's commit is its ancestor, and reviewing the move's ~1 300 moved lines as part
  of E2's diff would be reviewing another plan's pull requests.
- Per epic, in this order: `open(repoPath, branch: feat/feature-review-eN, callerModel)` →
  `review_plan(planText = this document, prefaced by one paragraph naming the epic, its stories in
  scope and the other two epics out of scope)` → `resolve` until `proceed` → build every story
  (RED first; in parallel where §7.5 allows) → ONE commit → `review_code(baseRef = the epic's
  branch point)`, the scope reused from the session → `resolve` → fixes amended into the same commit
  → `review_code(again: true)` until `proceed` → pull request (`pull-requests.md`: gate before the
  PR; rebase merge; verdicts, reviewer counts and honest failures in the PR body). The commands the
  gate handed back (split, autonomy, model) are named in each epic's summary.

### 7.1 Phase 0 and the prerequisite — where they sit

**Phase 0 is not an epic.** An epic is a diff the gate reviews; three of the five spikes cannot run
without product code (S0.1 is the AOT publish loading grammars, S0.2 is `--outline`, S0.5 is the
product's own vault read — Q6), and the two that can (S0.3, S0.4) produce no diff. Each spike is
therefore folded into the story whose code it measures, and its rows in §6 are that story's
acceptance evidence — a story is not accepted until its numbers are in this document.

| Spike | Where | Why there |
|---|---|---|
| S0.3 turns (hand-driven, no code) | any time during E1/E2; **recorded in §6 before E3's `review_plan`** | it decides C3, and E3's plan round must see the decision |
| S0.1 grammars, S0.2 budget | inside S1.3 — the `--outline` built there IS the instrument | a throwaway outliner would be thrown away and rebuilt |
| S0.5 xAI / Qwen | inside S1.2, between its first half (generic `openai` dialect + `--probe-api`) and its second (the `xai`/`qwen` dialects and presets) | the dialects are written FROM the rows; the probe is the product's own key path |
| S0.4 old side | the last acceptance check of each epic, for the seams THAT epic introduces (§4.13) | E1: `runtime: "api"`, the api version gate. E2: the session file, `--log` with `note`/`skipped`/`FeatureReview`, `user_version`, the Team server catalog. E3: the vendor `feature` field, the snippet |

**The round-engine move ([PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md))
is not an epic either.** It lands on `main` through its own pull requests and its own gate.
Order: **E1 merges → steps 1–4 (records, prompt, roster, engine) → E2 branches.** E1 carries every
§9 fix that touches the moved members (`Finish`, `CommandStageOf`, round numbering), so the move
carries them, as that plan's §3 requires; E2 must NOT branch before step 4 is on `main`, because a
rebase across a move resolves delete-versus-modify in favour of the delete (that plan, §3). Steps
5–7 (sweeps, resolve path, document stage) run beside E2 or between E2 and E3 — never concurrently
with S2.2, which edits `resolve`/`status`/`ask_human`; whichever lands second rebases and re-proves
with `prove-move.mjs`. Both plans' boundary text says this.

### 7.2 Epic 1 — foundation: honest stages, the `api` runtime, the outliner

Everything here is useful without the feature stage. Branch `feat/feature-review-e1` from `main` at
this plan's merge commit.

- [ ] **S1.1 — every `Stage` answers by name, and a round's number comes from the journal.**
  Goal: retire the four silent defaults and fix the three document-stage defects and the
  round-overwrite, so a fourth stage can be added without a `_ =>` swallowing it.
  - Deliverables: `core/Rounds/Stages.cs` (§4.1 descriptor, exhaustive, `PhraseOf` never "done");
    routed through it: `PanelConfig.BucketFor` (`SessionState.cs:203`), `RoundSubject.StageName`
    (`RoundSubject.cs:88`), `RoundRefusals.ReviewKindOf` (`RoundRefusals.cs:83`), `CommandStageOf`
    (`PanelService.cs:2483`), `Finish`'s sentence (`:2778`), `AnswerFor`'s revise sentence,
    `RoundMachine.Resolve`'s next stage, the `Sha` write (`:1428`); `ProviderSettings.Reviews`
    (`PanelSettings.cs:99`) stays a throwing switch. §9 items **1, 2, 3** (`ask_human` gains the
    optional `document` and loads that session — `:2805` loads the branch session today), **4, 6**
    (a round's number = `max(numbers of this session and stage) + 1` under the claim; `LiveRound.Record`
    stops reading `RoundsRunThisStage + 1` at `LiveRound.cs:232`; the budget counter stays), **7**
    (`--ask-local` 65, `Program.cs:1494`), **9** (`CoaiMcp.Normalizer.csproj:9-11`,
    `TreeSitterNormalizer.cs:60-67`, `Tools.cs:7`).
  - RED first: an `Enum.GetValues<Stage>()` walk over `Stages.Of`; a document `call_human` notice
    names the document gate, not "done"; resolving a document review does not say "the code stage is
    complete"; `ask_human` for a document session files under that session's id;
    `ReviewKindOf`/`CommandStageOf` answer `DocumentReview`; **code round 1 → resolve → advance HEAD →
    `again` → BOTH rows in the log** (the §9.6 sequence, seen overwriting through the upsert at
    `RoundsDb.cs:777`); `--ask-local` with no arguments exits 65; every existing bucket and phrase
    pinned by literal.
  - Acceptance: each RED seen with its real symptom, then green; `CoaiMcp.Tests` whole and green
    through the executable; behaviour otherwise byte-identical (the literal pins).
  - Model: **Fable** — the descriptor is the architecture the stage hangs on; round numbering changes
    what a stored row means (data).

- [ ] **S1.2 — the `api` runtime: Grok and Qwen review plans and code the day it ships.**
  Goal: an OpenAI-compatible HTTP reviewer with a vault key, whose dialects are measured, not documented.
  - Deliverables, in this order inside the story: **(i)** `LocalAsk.RequestBody` (`LocalAsk.cs:186`)
    pinned by a golden byte test, then refactored to `ChatRequest.Body(dialect, …)` reading
    `shared/api-dialects.json` with the `openai` dialect only; `src/Api/AskApiMode.cs` (`--ask-api`;
    exit codes 0/65/69/70/75/77 per §4.10; `Authorization: Bearer` from `COAI_API_KEY` in the child's
    ENV, never argv); `runners/Reviewers/ApiRuntime.cs` (self-invocation like `LocalRuntime.SelfInvocation`,
    empty `SharedResource`, no `EngineLease`); `RuntimeResolution.NameOf` with `api` BEFORE the base-URL
    arm (`RuntimeResolution.cs:92`); `AuthOf` and the `VendorProbe` api branch;
    `ReviewerRuntimeSelector.MachineOnlyRuntimes = {local, api}` and the Team server's
    `KnownRuntimes = RuntimeNames − MachineOnlyRuntimes` (`VendorConfig.cs:38-41`);
    `ConsultantResolution.Consulting` unchanged, so `api` is refused by name; **`--probe-api`** — a
    one-shot mode that reads the vault as `KeyVault.ReadAsync` does (`COAI_CREDS_KEY` from ENV), runs
    `GET /models` and the §6 S0.5 matrix, prints results only, with a test that neither stdout nor
    stderr ever contains a key. **(ii) Run S0.5** through `--probe-api` with the operator's `grok`
    and `qwen` vault entries; write the rows into §6. **(iii)** the `xai` and `qwen` dialects and
    `shared/api-presets.json` written FROM those rows (model ids from `GET /models`; the Token-Plan
    base URL of Q5 as data), mirrored by a TS check. **(iv)** Extension: `models.ts` `RUNTIMES += 'api'`
    (not in the chat or consultant pickers), `vendors.ts` `dialect?`, the three presets, `vendorsFrom`
    survives, `API_RUNTIME_SINCE = <the mcp version this ships in>` — rows disabled in the card AND
    suppressed from the emitted `COAI_VENDORS` for an older installed server, which means the installed
    version is threaded into the writer (`envBlock(settings, vendors)`, `settingsShape.ts:459`, has no
    such input today); any new `help(...)` key ships with its five translations here, because the
    coverage test forces it. `.agents/PROJECT.md` lists `--ask-api` and `--probe-api`.
  - RED first: the local golden body (BEFORE the refactor); no argv element equals the key and the
    key is in ENV; an `HttpListener` stub: 429 → exit 75 with a sentence `RateLimit.Hit` matches
    (`ReviewerExecutor.cs:145-157` — the match is on the TEXT, "HTTP 429" / "429 Too Many Requests",
    with a non-zero exit; the code alone is not recognised), 401 → 77 naming the vendor and never
    echoing the body; `--ask-api` with no arguments exits 65; `NameOf` order (an api row with a base
    URL is `api`, not `codex`); `PanelSettings.RuntimeOf` keeps `api`; the Team server rejects `api`;
    the consultant refuses `api`; extension: `vendorsFrom` keeps `api`, the gate suppresses the row and
    `sameVendors` still emits the rest, `panelServerDefaultsAgreement`.
  - Acceptance: one `review_plan` round on this repository with Grok and Qwen as reviewers through
    `api` (vendor, model, cost recorded in §6); the S0.5 rows in §6 with the verbatim vendor answers;
    the S0.4 rows for `runtime: "api"` and the version gate measured against released `mcp-v0.35.0`
    and `extension-v0.53.1`.
  - Model: **Fable** — API keys, the 401/403 path, and a key that must never reach argv or a log line.

- [ ] **S1.3 — the outline for seven languages, `--outline`, and the feature finding schema.**
  Goal: a body-free AST outline of any supported file, measured for budget, and the schema the feature
  reviewer answers in — every other stage untouched.
  - Deliverables: `ISourceOutliner` + `OutlineLanguage` in core (separate from `SourceLanguage`, the
    corpus's trust boundary); `normalizer/Grammars.cs` (the one `(library, entry point)` table both
    classes read — `TreeSitterNormalizer.cs:32` holds it privately today); `normalizer/OutlineTables.cs`
    (node kinds as data, exhaustive, no `_ =>`); `normalizer/TreeSitterOutliner.cs` (signature = node
    start to `body` start, whitespace collapsed, ≤240 chars; the four special cases; the 20 % ERROR
    threshold); `shared/kept-grammars.txt` + `tree-sitter-tsx`, `-rust`, `-php`, `-python`;
    `--outline <file>` (listed in `.agents/PROJECT.md`); `FindingSchema.FeatureJson` derived from
    `FindingSchema.Json` (`sourceRequests`, strict), `SchemaFile.Ensure` one file per shape,
    `RawReview`/`NormalisedReview.SourceRequests` (empty, never null; an invalid request is a named
    rejection); `core/Feature/FeatureBudget.cs` — constants calibrated by S0.2, not typed.
  - RED first: `KeptGrammarsTests` over the union of both interfaces' languages (red the moment
    `OutlineLanguage` names Rust); per language a golden fixture and the **no-body property** (no line
    unique to a body appears in the outline); the parse-failure threshold; every grammar loads by its
    entry point (`tree_sitter_php` vs `_php_only` decided by loading, not by reading);
    `FindingSchema.Json` byte-identical; `FeatureJson` meets the OpenAI strict rules; the collector's
    `.tsx` behaviour unchanged.
  - Acceptance: S0.1 rows (≥95 % of named declarations against a regex baseline on 20 real files per
    language; ERROR share; publish size delta, win-x64 and linux-x64, in the AOT publish) and S0.2 rows
    (`--outline` over the consultant, PR #230 and the S8 notices: bytes, file counts, `git show` against
    `cat-file --batch`) written into §6; every `FeatureBudget` constant traces to a row.
  - Model: **Opus** — a decided design, per-language tables, and a schema pinned byte-for-byte.

### 7.3 Epic 2 — the stage

Branch `feat/feature-review-e2` from `main` after E1 and round-engine steps 1–4. Every line number in
§3–§4 is re-read against `RoundEngine.cs`, `StageRun.cs`, `RosterBuilder.cs` and `ReviewerPrompt.cs`
before the first edit.

- [ ] **S2.1 — the stage exists in the core, the engine and the store.**
  Goal: `Stage.FeatureReview` can be begun, skipped or run through `RunStageAsync` with its own budget,
  role, session key and skip record — before any tool reaches it.
  - Deliverables: `Stage.FeatureReview` (appended last) and its `Stages` row; `RoleStages.Feature`,
    `RoleStages.All` (read by `MustBeWellFormed`, `RoleCatalog.cs:273`, and `RoleComposition`),
    `RoleBuckets.FeatureCode`; `PanelConfig.FeatureDefault = (1, 5)` routed at ALL THREE sites that
    pick a default today (`GateFor`, `PanelSettings.cs:1200-1202`; `ShippedFor`, `SessionState.cs:131-132`;
    the `Defaults` dictionary, `:120`); `COAI_MAX_ROUNDS_FEATURE` / `COAI_THRESHOLD_FEATURE`; the
    built-in role `FeatureReview` in `shared/builtin-roles.json` + `src_mcp/src/prompts/feature-review.md`;
    **the minimum the seed forces on the extension**: `builtinRoles.generated.ts` regenerated,
    `roles.ts` `FEATURE_CODE` with an exhaustive `bucketOf` (`roles.ts:331`), **`enabledCodeRoles` as a
    bucket filter (§9.8, `settingsShape.ts:728-729`)**; `ProviderSettings.Serves(FeatureReview) =
    Feature && !IsRemote` with the `feature` field parsed (absent = false); `SessionState.Feature`,
    `IsFeatureSession`, `SessionKey.For(…, feature)` appending `#feature:<key>` (`SessionState.cs:406`),
    the `":feature"` branch constant, `SessionStore.Load/Exists/FileFor` + `SessionClaim` feature
    argument, the sweep re-saving under its own key; `PersistedSession.FeatureBase`;
    `RoundMachine.BeginFeatureRound` (+ `again`, the four refusal sentences, the different-base refusal);
    **the session is created under the engine's own claim** (a `StageRun.CreateIfAbsent` factory —
    the document stage creates outside it, `PanelService.cs:921` before `:1254`, and narrows the race
    by re-reading, `:871-874`); `StageRun.WhenNobody` (`Refuse` | `RecordSkip`) and the skip branch in
    `RoundEngine` replacing the refusal at pre-move `PanelService.cs:1333`, numbered from the journal
    (S1.1), `stage.Begin` kept BEFORE it (`:1270`), consecutive identical skips coalescing; schema step
    15 `rounds.note`; `RoundContext.HeadSha` supplied by the skip (`RoundsDb.cs:801`); `--log` gains
    `note`, read through `RoundsQuery.cs:812`'s `pragma_table_info` (an older database has no column;
    `''` is the one spelling of absence); Team server `AcceptedRoles` filters the built-ins by stage
    (`AcceptedRoles.cs:103-108` seeds every built-in id today; `AllowAny` at `:178`).
  - RED first: the `Stages` walk fails on the new value until its row exists; every existing session
    key pinned by literal and the feature key colliding with no branch or document key;
    `BeginFeatureRound` refusals (four sentences), `again` with the same head refused and with a new
    head fresh, a different base refused naming both SHAs; `enabledCodeRoles` counts the seeded
    `FeatureReview` role today; the skip record: verdict `skipped`, `head_sha` written, session state
    untouched, ten identical skips → one row "×10"; **all failed → disable every reviewer → call again
    → refused with the human-gate sentence, not `skipped`**; two concurrent first calls on one plan
    create one session; a database without `note` reads as `''`; `AcceptedRoles` with `AllowAny = true`
    and with `FeatureReview` in `Coai:ExtraRoles`; `builtinRoleCatalog` and `generatedFilesAreCurrent`
    green on both sides.
  - Acceptance: both suites green; the engine-level truth table (rows 1–3 and the human-gate row)
    pinned through the engine's internal test seam; the S0.4 rows for the session file (the old sweep
    skips it — `SessionStore.cs:455-463`), `user_version` 15 read by the released binary, and `--log`
    with `note`/`skipped`/`FeatureReview` read by the released extension, written into §4.13.
  - Model: **Fable** — session identity and a schema step are data migration; skip-versus-block is
    the gate's integrity.

- [ ] **S2.2 — `review_feature`, outline only (follow-ups = 0).**
  Goal: the eleventh tool runs a real round over plan + epics + lessons + outline, and is useful by itself.
  - Deliverables: `src/Server/Stages/FeatureStage.cs` (≤300 lines) + `FeatureInputs` (§4.5, no I/O
    before the refusals pass, each through `Refusal.Answer`); git refs (`rev-parse --verify <ref>^{commit}`
    with `GitHistory.IsCommitish`, the ancestor check, a non-empty reviewable range); `FeatureSessions`
    (identity through `DocumentReader.IdentityOf`, `DocumentReader.cs:132`; caller from the handshake);
    `runners/Feature/FeatureOutlineBuilder.cs` (base..head through `ContextAssembler.ComparisonBase`,
    `NumstatReader`, `DiffExclusions`; `git show` at `headSha`; `*` marks through `DiffSplitter`;
    deterministic cutting naming every dropped file); `FeatureContext` (§4.6 order; 4 KB reserved for
    "what this context left out"); `StageRules.Feature`; `ReaderMaterial` replacing `bool hasCheckout`
    in `ReviewerPrompt`, `WhatYouHave`'s third truth; the stage's `StageRun` (`RolesPerVendor`,
    `NeedsWorktree: false`, `WhenNobody: RecordSkip`, `Head`); `feature` on `resolve`/`status`/`ask_human`;
    `Tools.cs` (eleven tools) with D11's tool description; `ScenarioCoverageTests.Covered["review_feature"]`;
    `McpContractTests`; `shared/refusal-sites.json` re-recorded.
  - RED first: every empty shape of `lessons` refused with all four questions in the text; `epics`
    bounds; a `planPath` outside the repository refused; base = head, base not an ancestor, an empty
    range — each its own sentence; `FeatureOutlineBuilder` on a real temporary repository (A/M/D/R,
    binary named, unsupported with size, `*` marks, reads `head` not the dirty tree); the budget property
    test (never exceeded, the elision list always whole); **`AFeatureIsReviewedEndToEndTests`** over the
    FakeCli: skip (no vendor ticked) → tick → the recorded stdin carries the outline and no body line →
    `resolve(feature)` → revise → `again` with a new head → proceed → done; the skip truth table row by
    row through the tool.
  - Acceptance: a live `review_feature` on this repository (E1's diff as the "feature"; Grok and Qwen
    through `api` plus one CLI reviewer) with `COAI_FEATURE_SOURCE_FOLLOWUPS=0`; the whole suite green;
    `research/module_tests.md` gains the flow row.
  - Model: **Opus** — the surface of a decided design; the two gate-integrity decisions were pinned in S2.1.

- [ ] **S2.3 — the gate's history of this work.**
  Goal: the reviewer sees what earlier rounds rejected and why, labelled as evidence, never as proof.
  - Deliverables: `GateHistoryQuery` (§4.8 rules a/b/c; `T0`; `rev-list` ≤5000 else switched off and
    said; `epics[].branch` ranked first, the rest labelled candidate by rule; consultations by branch or
    SHA; rejected findings only, `TextSimilarity` de-duplication, plan before code, newest first, ≤24 KB;
    the feature session's own rejections excluded; the honest counts sentence; a database failure → one
    sentence and the round continues; nothing seeded into `session.Rejections`); the §4.6 section-4 slot
    in `FeatureContext`; the look at real `PlanReview` subjects (`SELECT subject, substr(plan_text,1,120) …`)
    recorded in this plan BEFORE rule (c) is written.
  - RED first: a squash-merged epic found by `epics.branch`; a merged one by `rev-list`; an unrelated
    branch in the window NOT attached and counted; a plan round before `T0` found by its heading; a
    consultation by branch; no database → the sentence and the round runs; `session.Rejections` unchanged.
  - Acceptance: the history section appears in S2.2's live round with its counts sentence; suite green.
  - Model: **Opus** — a read-only query over existing tables; a mistake is a poorer context, not a
    wrong verdict, and the round catches it.

### 7.4 Epic 3 — source on demand, and the person's side

Branch `feat/feature-review-e3` from E2's commit. S0.3's rows must be in §6 before this epic's `review_plan`.

- [ ] **S3.1 — `SourceResolver`: git objects at `headSha`, nothing else, and never a credential.**
  Goal: a reviewer may ask for any file at `head` and gets exactly that — never the working tree, never
  a path outside the repository, never a credential-looking file, never more than the caps.
  - Deliverables: `RepoPaths.IsRelative` in core, moved from `GitHistory.IsRepoRelative`
    (`GitHistory.cs:91`; both callers re-pointed, one road); `runners/Feature/SourceResolver.cs`
    (`git show <sha>:<path>` only; one `git show` per file per round; `symbol` → outline lookup with up
    to 3 overloads and a 20-name refusal; `lines` clamped ≤400; whole file ≤16 KB else head + "ask for
    lines"; ≤8 requests/turn, 48 KB/turn, 128 KB/reviewer); D15's credential refusal (`.env*`, `*.pem`,
    `*.key`, `*.pfx`, `*.p12`, `id_rsa*`/`id_ed25519*`/`id_ecdsa*`, and the embedded `CredentialWords`
    from `shared/credential-words.json`) with its reason; lock files and build output refused through
    `DiffExclusions`; output fenced with path, lines and SHA.
  - RED first: traversal (`../`, an absolute path, a symlinked parent) refused **without starting a
    process** (the fake launcher records nothing); an uncommitted edit is not served; each credential
    shape refused with the D15 sentence — and one positive: an ordinary file IS served (the
    fixture-rejected-in-green trap); symbol/overloads/lines/caps; one `git show` per file across two requests.
  - Acceptance: the credential-shape scan has its companion test (the pattern still matches a known
    instance); suite green.
  - Model: **Fable** — it serves repository contents to third-party models; path confinement and
    credential refusal are the security boundary.

- [ ] **S3.2 — the turn loop: one reviewer, one conversation, one terminal outcome.**
  Goal: a reviewer that asks for source gets it and answers again — caching-friendly resends, honest
  usage, and a rollback switch.
  - Deliverables: `IReviewerContinuation` + `ReviewerWork.Continue` (`BoundedScheduler.cs:24`; `None`
    everywhere else); the loop inside `BoundedScheduler.LaunchAsync` (`:390`) around `RunWithLadderAsync`
    — the slot held for every turn, the ladder per turn, exactly one terminal outcome (the stand-down
    count at `:346` moves once); `EarlierTurns` usage on the outcome base, read by `UsageLedger`
    (`runners/Reviewers/UsageLedger.cs:123`) and `LiveRound.Finish` (`LiveRound.cs:187-194`); one ledger
    entry per turn; the repair rebuilt per turn (composed once today, pre-move `PanelService.cs:2115`);
    the tail prompt (previous findings compact, the requests, served code fenced, "not served", FINAL on
    the last turn — the prefix byte-identical); `COAI_FEATURE_SOURCE_FOLLOWUPS` (default 3; 0 = off);
    the scaled deadline (`reviewerTimeout × (1 + follow-ups)` into `RoundBudget.For`, `RoundBudget.cs:28`;
    the explicit override at pre-move `:2389` kept); `reviewers.note` "N turns; source: …";
    `Ok.Turns/Served` for the audit; FakeCli turn switching (a `FAKECLI_TURN_*` family beside
    `FAKECLI_STDOUT` in `src_mcp/tests_fakecli/Program.cs`). **C3 (CLI resume)** is built ONLY if S0.3
    measured turn 2 above 40 % of turn 1 — and then as its own follow-up plan, not a fourth story: the
    continuation is the seam it plugs into, and follow-ups = 0 keeps stateless resend shippable meanwhile.
  - RED first: turn N+1's prefix equals the base prompt byte for byte; stops on no request, on the
    cap, on the budget; a malformed turn-2 answer is repaired against turn 2's prompt; a source request
    with non-zero usage then a failed turn 2 keeps turn 1's cost (ledger AND round total); a
    cancellation during turn 2 keeps it too; a failed later turn is a failed reviewer (no fallback to
    turn-1 findings); the per-provider peak does not rise; S2.2's scenario extended: turn 2 serves a
    symbol and only the last turn's findings count.
  - Acceptance: **the DoD's live run** — `review_feature` on this repository against Grok and Qwen
    through `api` and one CLI reviewer, with a source request served, cost recorded; the suite green in
    both configurations (the change touches concurrency).
  - Model: **Fable** — the scheduler's outcome, usage and slot accounting is architecture every stage runs through.

- [ ] **S3.3 — the extension, the docs, the promotion.**
  Goal: a person can tick a vendor for features, see a skipped round for what it is, read the help,
  paste the snippet — and the record says four gates.
  - Deliverables: `vendors.ts` `feature?` (absent = false; forced false with a sentence for a Team
    server row), the vendor card's fourth switch; roles grouped Plan / Code / Document / Feature
    (`rolesPage.ts` offers the stage); the stage's budget and switch (`panelServerDefaultsAgreement`);
    `FEATURE_SINCE`; `rounds.ts` `stageName` covers `DocumentReview` (**§9.5**, `rounds.ts:261-263`)
    and `FeatureReview` (unknown → raw); `roundsLog.ts` renders `skipped` as its own neutral state and
    shows `note` (the page RUN — `bundledPage.test.ts`); help in all five languages; the snippet half
    `src_vs_code/src/featureRule.md` (`<!-- coai-feature v1 -->`), `FEATURE_VERSION = 1`, the
    `KNOWN_HALVES` row (`claudeSnippet.ts:176`), `prepare-gate.mjs` emission, `ARTEFACT_VERSION` 11 → 12
    (`:66`), the "(v12)" menu title, the hash re-pinned; docs: `research/module_core|server|runners|extension|tests.md`,
    `architecture.md` ("Four gates, not three"), `src_vs_code/CHANGELOG.md` (the ONE narrative changelog;
    `changelog-names-the-release.mjs` refuses an mcp release it does not name); the S0.4 rows for the
    `feature` field and the snippet against the released halves; this plan's DoD ticked; **promotion**
    per `/promote-plan` — every edit first, `git mv` last, the `research/README.md` and `todo/README.md`
    rows in the same commit, both round-engine boundary texts updated.
  - RED first: `stageName('DocumentReview')` (red today); `feature` absent = false and a Team server
    row forced false; the log page RUN with a skipped row (ask what the assertion would see with the
    branch deleted); help coverage ×5; the snippet: halves, v12, hash; `generatedFilesAreCurrent`.
  - Acceptance: `npm test` green; `test:seam` and `test:parity` green; the plan promoted with its
    deviations; `plan-lifecycle.mjs` and `pin-check.mjs` green.
  - Model: **Opus** — UI, translations and documentation of work already shaped.

### 7.5 Dependencies and parallelism

```
E1:  S1.1 ∥ S1.2 ∥ S1.3          all three touch Program.cs's args[0] switch and .agents/PROJECT.md — merged by hand inside the branch
     └─ merge ─ round-engine steps 1–4 (their own PRs) ─┐
E2:  S2.1 → { S2.2 ∥ S2.3 }      S2.3 builds against FeatureContext's section slot; S2.2 owns the tool
     └─ merge ─ round-engine 5–7 anywhere here, never beside S2.2 ─┐
E3:  { S3.1 ∥ S3.3 } → S3.2 → the live DoD run → promotion (S3.3's last act)
```

- S1.2's long pole is S0.5: it needs `COAI_CREDS_KEY` configured and the `grok`/`qwen` entries in the
  vault (Q6 — new on 2026-09-25). Start it first.
- S2.1 needs S1.1's descriptor and numbering; S2.2 needs S1.3's `FeatureJson` and outliner and S2.1.
- S3.2 needs S3.1 (the resolver), S1.3 (`SourceRequests`) and S2.2 (the tool to drive end to end).
- S3.3's rounds-log and vendor work needs only E2 (already on `main`) and may start the day E3 opens.

### 7.6 Deliberately not a story

- C3 CLI resume — a follow-up plan if S0.3 demands it (§7.4).
- Round-engine steps 5–7 — that plan's own pull requests.
- The releases after E3 — `mcp-v*`, `extension-v*` and `server-v*` (the Team server's `KnownRuntimes`
  and `AcceptedRoles` changed) — cut on `main` after the merge, one tag per push (`task-lifecycle.md` §3).
  Not a story because a release is not a diff the gate reviews; it IS on the Definition of Done.

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

All four were answered on 2026-09-25 and are now D13–D16. The two asked after them are answered too:

- **Q5 — answered.** The Qwen key is an Alibaba Model Studio **Token Plan (Solo)** key (`sk-sp-…`),
  workspace region **Singapore, International**. A Token Plan key has its OWN base URL —
  `https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1` — not the general
  `dashscope-intl` one, and the console warns that `dashscope.aliyuncs.com` enters maintenance mode in
  favour of per-workspace hosts (`ws-….ap-southeast-1.maas.aliyuncs.com`). The Qwen preset therefore
  stores the base URL as data the person can change, and S0.5 measures the Token Plan URL first.
- **Q6 — answered.** Keys go into coai's own vault `config` entry under the vendor row ids `grok` and
  `qwen` (the product's path). This machine had no `COAI_CREDS_KEY` configured on 2026-09-25
  (`providers`: `vaultReadUtc: never`), so the entry is new. S0.5 therefore measures THROUGH the
  product's own vault read, never with a key an agent has seen: its probe is a one-shot mode that reads
  the vault the way `KeyVault` does and prints results only, with a test asserting its output never
  contains a key.

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
- [ ] `.agents/PROJECT.md` lists `--ask-api`, `--probe-api` and `--outline`.
- [ ] Released after E3: `mcp-v*`, `extension-v*` and `server-v*` (the Team server's `KnownRuntimes`
      and `AcceptedRoles` changed), one tag per push.
- [ ] All three suites green through their executables / `npm test`; plan-lifecycle and pin checks
      green.
- [ ] `research/architecture.md` and the module docs describe four gates; `src_vs_code/CHANGELOG.md`
      (the one narrative changelog, which the mcp release guard reads) written; this plan promoted with
      its deviations recorded and its row moved from `todo/README.md` to `research/README.md`.
