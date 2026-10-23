# PLAN — the codex, agy and claude reviewers get what Kimi got: a failure is classified from the CLI's own words, and what the person reads is whole, cured and redacted

> Status: **E5.S1 and E5.S2 are built on branch `docs/cli-reviewer-gaps` (2026-10-23), nothing is released, and E5.S4's paid canary run was made by the lead on claude 2.1.309 (the branch's hook did not run under `--safe-mode`); the other stories are plan only.** Scope: `src_mcp/runners/Reviewers/ReviewerExecutor.cs`
> (`RateLimit`, the stderr tail, the kept evidence), `ReviewerRuntime.cs` (`CodexRuntime`), `ClaudeRuntime.cs`,
> `AntigravityRuntime.cs`, `BoundedScheduler.cs` (the summary line), `VendorDiagnosis.cs`, `VendorProbe.cs`,
> `src_mcp/runners/Files/EvidenceFile.cs`, `src_mcp/core/Notices/Redaction.cs` with its mirror
> `src_vs_code/src/notifications.ts` (one story, ordered AFTER the redaction plan on `docs/redaction-across-lines`),
> their tests, `research/module_runners.md` and `research/module_core.md`. Five epics: E1–E3 are the six scout items
> (R1, R3, R4, R5, R6, R8); E4 (R2: the three reviewers start from an allow-list environment, with a visible switch back)
> and E5 (R7: the Claude reviewer runs `--safe-mode` so the reviewed branch's `CLAUDE.md`, hooks and MCP servers do not
> steer it; a card note for codex and claude) were added the same day after the owner answered §9 and the question
> consultant's two precedents were verified. **This plan adds NO new refusal of any kind** (owner decision 2026-10-23:
> nothing may be refused only because it is unmeasured — a note, never a refusal): E4 and E5 are safety DEFAULTS with
> notes, each with a visible way back.
>
> Related docs: [module_runners.md](../research/module_runners.md) (§*What counts as hopeless* :1193, §*A stated reset is
> never waited out locally* :1229, §*WSL, measured* :874), [module_core.md](../research/module_core.md) (`Redaction`, :58–60),
> [PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md](PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md) (the Kimi work this
> plan mirrors), [PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md](PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md)
> (R2/R7 touch the same reviewer), `todo/PLAN_redaction_reaches_a_value_on_the_next_line.md` (on branch
> `docs/redaction-across-lines`, not on `main` yet — E3.S5 lands after it; see §4).

## 1. The symptom, and the goal

The owner asked: Codex and Antigravity (agy) were added much faster than Kimi and Vibe — do they need any of the work
Kimi and Vibe got? A read-only scout read `origin/main` (151cc652) and ranked eight gaps; every line below was then
re-read in the code before it was cited (§2 records where the scout was off).

**The headline (R1) breaks work and spends money.** `RateLimit.Hit` (`ReviewerExecutor.cs:333-334`) says a launch
was rate limited when the exit is non-zero AND a phrase or a status code appears **anywhere in stdout or stderr**
(`:336-337`; the phrases `:301-302`: *rate limit*, *usage limit*, *quota*, *unavailable*, *high demand*, …). The
executor asks the adapter first (`:1010`) — but only Kimi overrides it (`KimiRuntime.cs:277-285`); codex, claude and
agy fall to the default `RateLimitVerdict.ByText` (`ReviewerRuntime.cs:535`, `ReviewerExecutor.cs:275-276`).

For those three, stdout is not an error channel:

| CLI | what stdout holds on a failed launch | where its real failure is |
|---|---|---|
| codex (`--json`, `ReviewerRuntime.cs:660`) | the whole event stream — agent messages, command output, up to megabytes (`:712-713`) | `{"type":"error"}` / `{"type":"turn.failed"}` events, already parsed by `CodexRuntime.WhyItFailed` (`:707-729`); stderr |
| claude (`--output-format json`, `ClaudeRuntime.cs:93`) | the envelope, whose `result` is the review's own text | `is_error: true` with `result` as the sentence, already parsed by `ClaudeRuntime.WhyItFailed` (`:153-174`); stderr |
| agy (`--output-format stream-json`, `AntigravityRuntime.cs:54`) | NDJSON events including the model's text | stderr (`AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429) … Resets in 88h49m9s"}`, fixture `src_mcp/tests/fixtures/antigravity/consult-quota.stderr.txt`) |

A review of coai's own code says *quota*, *rate limit* and *usage limit* often — this repository is about them. So any
non-zero exit of a codex, claude or agy reviewer whose stdout mentions one of these words is reported as **rate
limited**; the ladder (`BoundedScheduler.cs:617-669`, `RetryLadder.Default` `:28-34`: 5 s, 30 s, 60 s, 120 s) relaunches
it **up to four more times**, each a paid launch, each with its own repair launch possible; and `RateLimit.Reason`
(`:394-402`) quotes the first marked line of stdout — for codex a whole JSON event line, for claude the whole envelope
— as *the reason*, cut at 160 characters for the cell (`:804`) and 4 000 for the ledger. If that stdout line happens to
hold *exhausted* or *daily*, `Hopeless` (`:373-374`) stops the ladder and the consultation path files it as a
**Quota** (`ConsultFailures.cs:88`) — a wrong class said with confidence. Kimi got exactly this fix on 2026-10-20
(E4.4 of the Kimi runtime plan: a path named `quota.cs` in the trace is not a rate limit, `ReviewerRuntime.cs:530-532`).

**The goal.** For codex, claude and agy, as for Kimi:

1. a launch is rate limited only when the CLI's **own error channel** says so (E1);
2. the words that are known to be a limit are in the lists — codex's *session limit* and claude's *monthly spend
   limit*, both already recorded in this repository, are not (E1);
3. what the person reads is whole where it matters (agy's stated reset is cut mid-number in the cell), carries a
   cure for the one agy failure that has none, and warns about an unmeasured agy version the way Vibe's does (E2);
4. every vendor line that reaches a note, a file or a card is redacted before it is cut, on every road (E3);
5. a local codex, claude or agy reviewer no longer starts with the developer's whole environment — an allow-list the
   three CLIs are measured to sign in under, a visible switch back, the mode on the round's record (E4);
6. the Claude reviewer is not steered by the reviewed branch's own `CLAUDE.md`, hooks and MCP servers (`--safe-mode`,
   declared by both recorded `claude --help` texts), and the codex and claude cards say the branch's configuration
   reaches the reviewer (E5).

§9 records the owner's answers and the two verified precedents E4 and E5 are built on.

This plan creates no growth surface: no table, directory, cache or spawned process is added.

## 2. What was verified, and where the scout was off

Every reference below was read in the worktree at 151cc652. Three scout claims needed correction:

| # | scout said | verified |
|---|---|---|
| R1 | `RateLimit.Hit` ~:333-336, `Reason` ~:394 scan all stdout; Kimi override ~:277; codex parser ~:707; claude `is_error` ~:153 | all as stated: `Hit` `:333-334`, `Marked` `:336-337`, `Reason` `:394-402`, `KimiRuntime.RateLimited` `:277-285`, `CodexRuntime.WhyItFailed` `:707-729` (`CodexRuntime` is declared at `ReviewerRuntime.cs:587`; `DeepseekRuntime` `:805` inherits it, so it gets the fix for free), `ClaudeRuntime.WhyItFailed` `:153-164`, `SaysItFailed` `:173-174`. The seam exists: `IReviewerRuntime.RateLimited` `:535`, asked at `ReviewerExecutor.cs:1010` |
| R3 | 160-char cut `BoundedScheduler.cs` ~:804, loss recorded `VendorReason.cs` ~:11 | `ReasonLength = 160` `:804`, used by `Describe` `:747`; the ledger/log road `DescribeWhole` `:763-764` is already whole (4 000). `VendorReason.cs:9-12` records *…Resets in 110h12m4…*. The cell cut is in `Because` → `Quote` `:914-915` → `Cut` `:881` |
| R4 | *session limit · resets 9:30pm* ~:204, claude *monthly spend limit* ~:143 not in `Phrases`/`Spent` | `CooldownParser.cs:203-204` (codex, captured 2026-09-05 on the VM) and `ClaudeRuntime.cs:142-145` (measured 2026-09-29, 44 benchmark cells lost). Neither phrase is in `Phrases` `:301-302` nor `Spent` `:357`. The claude envelope's `"api_error_status":429` does NOT match `StatusCode` `:325-331` either (`\bstatus\b` needs a boundary and `_status` has none) — so today that failure is an `exit 1` with the sentence as `FailureReason`, never a limit, never a quota |
| R5 | no not-signed-in row for agy; observed *authentication timed out* `module_runners.md` ~:931; install command `VendorDiagnosis.cs` ~:347 | `module_runners.md:928-935`; `VendorDiagnosis.InstallCure` `:347-348`; the `Known` table `:55-125` has no agy row at all. **Nuance the scout missed:** `AntigravityOnLinuxTests.cs:85-99` pins that no vendor-table string contains `agy.exe` (refuted advice is offered nowhere) — the new cure must respect that |
| R6 | redact before the cut in `OpeningOfLastLine` ~:474-480; `VendorProbe.cs` ~:272-274; evidence `:521` + `EvidenceFile.cs` ~:43-48; `e.Message` ~:534; shapes `Redaction.cs` ~:83 | `OpeningOfLastLine` `:474-480` cuts with `VendorReason.Capped` and redacts nothing (the redaction happens later, in `Quote` `:914-915`, on the already-cut text — the opposite of the rule `:909` states); `VendorProbe.Refused` `:264-275` puts `said` (uncapped, unredacted) into the health note; `KeepIn` `:516-525` hands raw text to `EvidenceFile.Keep` `:43-61`, whose `Bounded` `:64-67` only cuts; the empty-answer transcript `Transcript` `:729-730` is whole stdout+stderr (`:1072`). **The `e.Message` note is at `:525`, not ~:534.** `VendorPattern` `:83-84`. **The scout named only the C# side: `Redaction.cs:9-14` is a byte-for-byte PORT of `src_vs_code/src/notifications.ts` (`:183` holds the same vendor list), held by a parity corpus — a new shape lands in both halves or in neither** |
| R8 | agy has no `VersionWarning` (~:373 default empty); model pinned ~:37; nothing checks which model ran | `ReviewerRuntime.cs:373` default; `AntigravityRuntime.cs` overrides neither `VersionWarning`, `WhyItFailed`, `RateLimited` nor `Disowned`; `DefaultModel` `:37`; the probe applies the warning `VendorProbe.cs:234`; the card draws it `modelCard.ts:202` (`versionWarningNote` reads `providers[].warning`, `module_extension.md:13023`). `VersionWarnings.Unmeasured` `:23-24` is the one builder. The research names agy 1.3.2 and 1.3.3 as measured (`RESULTS_vendor_quota_gauges.md:222,238`) |
| R2 | codex/agy/claude inherit the whole server environment (`ProcessLauncher.cs` ~:99, comment ~:86-98) | `InheritsEnvironment = true` `:99`, comment `:85-98`; none of the three `Build` methods sets it (`ReviewerRuntime.cs:646-677`, `ClaudeRuntime.cs:89-113`, `AntigravityRuntime.cs:47-71`). Kimi (`KimiEnvironment.cs:67-68`), Vibe (`VibeLaunch.cs:99-100`) and question consultants (`ConsultantLaunches.cs:79`) start from `ProcessEnvironment.Minimal`. **The Team server already confines its reviewers** (`src_server/src/Jobs/Confinement.cs:38`: `InheritsEnvironment = !Confined`) — R2 is about the LOCAL `coai-mcp` only, and the server's `Confinement` is the shape to reuse |
| R7 | codex and claude run inside the reviewed branch | codex: `-C worktreePath` and cwd `worktreePath` (`ReviewerRuntime.cs:654,672`); claude: `--add-dir` and cwd `worktreePath` (`ClaudeRuntime.cs:98,104`). **agy does NOT**: it runs from `AntigravityReadOnly.Home()` with `--add-dir` only when the tree is non-empty (`AntigravityRuntime.cs:62-64,125-128`). The sanitised snapshot is opt-in per adapter (`NeedsASanitisedSnapshot => false`, `ReviewerRuntime.cs:410`; `ReviewSnapshots.cs:13-17` says what it removes). Whether headless `claude -p` runs project hooks is NOT recorded anywhere (`RESULTS_claude_consultant_confinement.md` has no "hook") — a fact to establish |

## 3. What this plan does NOT do (the owner's decision of 2026-10-23)

- **No `HostAdmission` / `MeasuredOnNativeLinux` rows for codex or agy** (`src_mcp/runners/Platform/HostAdmission.cs:25`).
  An unmeasured host is a note on the card, never a refusal.
- **No `WslBridge`-style refusal for a Windows `agy.exe` reached from WSL.** It is measured not to finish its sign-in
  (`module_runners.md:928-935`) — that is a cure row and an optional note (E2.S2), not a door.
- **No waiting for a "captured real sentence".** R4 and R5 use sentences this repository already recorded
  (`CooldownParser.cs:204`, `ClaudeRuntime.cs:143`, `module_runners.md:931`).
- **No copy of Kimi's survivor marker, subreaper or survivor gate** (`KimiSurvivorMarker.cs`, `LinuxSubreaper.cs`,
  `KimiSurvivorGate.cs`), **no owned-home sign-in copies**, **no sibling-lens stand-down** — those cure Kimi's ACP shim's
  shape, which these three CLIs do not have.
- **No "never wait even when a reset is stated".** `RetryLadder.ResetOutlasts` (`:90-91`) is right: a stated reset beyond
  the budget stops the ladder; one inside it is waited out.
- **Never `--bare` for the Claude reviewer.** Both recorded help texts say it skips *keychain reads*
  (`help-2.1.258-windows.txt:40-44`, `help-2.1.197-wsl.txt:33`), and the lead's 2.1.309 text says *OAuth and keychain are
  never read* — the owner signs in to every CLI through the browser, so `--bare` is a launch that cannot sign in.
  `--safe-mode` is the flag that keeps *Auth … work[ing] normally* (`help-2.1.258-windows.txt:198-208`).
- **No automatic retry with the full environment** when an allow-listed launch (E4) fails. A failure is reported with the
  mode it ran in; the person flips the visible setting. A silent second launch would hide the one thing E4 exists to show.
- **No full sanitised snapshot for codex and claude in this plan** — it is E5's open tail (§*Open tail* under E5), because
  for CLIs that can read any path they are told a snapshot alone is not a block, and the owner has not asked for it.

## 4. The boundary with the redaction plan (both sides)

`todo/PLAN_redaction_reaches_a_value_on_the_next_line.md` (branch `docs/redaction-across-lines`, plan only, 2026-10-23)
changes `Redaction.cs` heavily: a new procedural `LabelContinuations` pass, a widened joiner, the pass ORDER of both roads,
its mirror and the parity corpus. Two plans editing the same file in the same month need the table the planning
convention asks for — written here now, and **to be written into that plan's own file when it is on `main`** (its author
is told through this plan's gate; a boundary named on one side is not a boundary):

| item | which plan builds it | the other's part |
|---|---|---|
| a new pass, the pass order, the joiner, the parity corpus's shape | the redaction plan | this plan does not touch them |
| three new one-line vendor shapes in `VendorPattern` and `notifications.ts:183` (`AIza…`, `ya29.…`, JWT) | **this plan, E3.S5 — after the redaction plan merges** | the redaction plan adds no shape |
| callers that redact before they cut (`OpeningOfLastLine`, the probe note, kept evidence, the keep-failure note) | this plan, E3.S1–S4 — they call the redactor and do not edit it, so they can land **before** the redaction plan | none |

Order: the redaction plan first for `Redaction.cs` itself; E3.S1–S4 of this plan are independent of it. What is disjoint:
the redaction plan changes no caller that E3 names (its S3/Q6 does edit two other callers, `AskApiMode.cs` and
`ProbeApiMode.cs` — named here as the exceptions; they do not overlap E3.S1–S4); this plan never changes a pass. The
reciprocal table is written into the redaction plan's own file on its branch before either side builds the shared file
(cadence consult 74decbae).

## 5. Build order

0. **E5.S2 first — the Claude reviewer's `--safe-mode`.** Out of numeric order on purpose, and here is why: it is a
   security item with an observed mechanism, not a theoretical one. A headless claude session in a worktree ran that
   worktree's project `SessionStart` hook (`dew_flow_conventions · research/RESULTS_subagent_model_tiers.md` §*E3 — the
   self-install observed (2026-10-14)*), and coai's Claude reviewer runs with the reviewed worktree as its working
   directory (`ClaudeRuntime.cs:104`) — so a branch under review can run a command inside its own reviewer today. The fix
   is one flag the detector already in `ClaudeCapability` can confirm from `--help`, declared by both recorded help texts
   (2.1.258 and 2.1.197). E1–E3 cost money and truth; this one costs trust. One PR, one release, before the rest — and
   that PR carries **E5.S1** (the fact's record, which S2 needs first) and **E5.S4** (the canary and behaviour check),
   plus S2's new reviewer-path probe preparation and its `SafeMode` setting (cadence consult 5119c081).
1. **E1** — classification. S1 codex, S2 claude, S3 agy (the three overrides), then S4 the phrases. Ships in one coai-mcp
   release; the Team server's reviewer path uses the same executor (`src_server/src/Jobs/ReviewLauncher.cs`), so it ships
   there with the next deploy.
2. **E2** — what the person reads. S1 the stated reset in the cell, S2 the agy cure row (+ optional note), S3 the agy
   version warning (+ the model fact).
3. **E3.S1–S4** — redact-before-cut on the four roads. Independent of `Redaction.cs`.
4. **E3.S5** — the three shapes, both halves, after `docs/redaction-across-lines` is merged. Rebase, then build.
5. **E4** — the allow-list environment for the three local reviewers, after E1–E3: its last step is a manual, paid
   sign-in smoke of the three real CLIs by the owner or the lead after the release (§6), and that step should not wait
   on the cheaper epics.
6. **E5.S3** — the card note; **E5's `--restricted` verification** is the lead's one-turn launch,
   after S2 ships, and decides whether a second flag follows in its own story.

Each epic goes through the coai plan gate before its code and the code gate after it, as the Kimi quota plan did.

## 6. Test plan

Tests are xUnit v3 on the Microsoft Testing Platform — **run the executable, never `dotnet test`**:

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ACodexReviewerIsRateLimitedOnlyByItsOwnErrorEvent*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AClaudeReviewerIsRateLimitedOnlyWhenItsEnvelopeSaysError*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AnAntigravityReviewerIsRateLimitedByItsStderrOnly*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ASessionLimitAndASpendLimitAreLimits*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AStatedResetSurvivesTheSummaryCut*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AnAntigravityThatTimedOutSigningInIsToldTheCure*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AnAntigravityVersionIsWarnedNotRefused*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*IsRedactedBeforeItIsCut*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ALocalReviewerStartsFromTheAllowlist*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ASentinelNeverReachesAReviewer*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*TheClaudeReviewerRunsInSafeMode*"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AReviewerSteeredByTheBranchSaysSo*"
# the whole suite, through the family wrapper (what CI runs)
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
# E3.S5 only: the extension's half and the parity corpus
cd src_vs_code && npm ci && npm test
node .agents/conventions/tools/plan-lifecycle.mjs
```

Every story follows RED → GREEN → break-it: the test is written first and watched fail with the real symptom (not a
setup error); after the fix it passes; then the fix is reverted once to prove the test has teeth, and restored. Both
observations go into the story's commit message. No story runs a vendor CLI: every test feeds a `ProcessResult` or a
fixture file. Three steps are NOT suite tests and are said so here: E2.S3's model fact (a recorded run under the real
agy, by the story's builder, in a scratch repository, written to `research/RESULTS_*.md` before code depends on it);
E4's **sign-in smoke** — after the release, the owner or the lead runs one round with each of codex, claude and agy on the
allow-list default and records that all three found their browser sign-in (paid, so never in the suite; the bench's
2026-10-01 measurement is the evidence it starts from, not a substitute for it); and E5's **`--restricted` check** — the
lead's one-turn launch of coai's exact Claude argv plus `--restricted --strict-mcp-config`, recording whether it conflicts
with `--permission-mode plan` / `--disallowedTools` (`ClaudeRuntime.cs:94-95`) and whether the file tools are confined to
`--add-dir`. The E4 child-process test uses the suite's fake CLI (a script that prints its environment), never a vendor's.

## 7. Definition of Done

- [ ] A codex, claude or agy reviewer whose stdout mentions *quota*/*rate limit*/*usage limit* but whose own error
      channel says nothing is an `exit n` with its real reason — never `rate limited`, never a second launch
      (`ACodexReviewerIsRateLimitedOnlyByItsOwnErrorEventTests` and its two siblings green; break-it recorded).
- [ ] Codex's *session limit* and claude's *monthly spend limit* are limits; the spend limit is hopeless (one launch);
      the session limit's stated clock reaches `CooldownParser.StatedReset` and the ledger's `statedReset`.
- [ ] agy's stated reset is whole in the round summary's cell, redacted before it is cut.
- [ ] *authentication timed out* has a cure row; on WSL/Linux it names the Linux install; no vendor-table string says
      `agy.exe` (the existing pin test extended, green).
- [ ] An unmeasured agy version is warned about in the shared sentence and run; the measured one gets no warning.
- [ ] The four redaction roads redact before they cut; the keep-failure note is redacted.
- [ ] `AIza…`, `ya29.…` and JWT shapes are redacted in both halves with the parity corpus green — landed after the
      redaction plan, not beside it.
- [ ] No new refusal of any kind was added for codex, agy or claude; every unmeasured condition is a note.
- [ ] `research/module_runners.md` (the codex/claude/agy failure reading, the hopeless words, the WSL section, the
      stated-reset section) and `research/module_core.md` (the shapes) match the code; Mermaid renders.
- [ ] The boundary table of §4 is also in the redaction plan's file once it is on `main`.
- [ ] A local codex, claude or agy reviewer starts from the allow-list by default: a sentinel variable in the server's
      environment is absent from the child's, the profile and proxy names are present, the ledger line says which mode
      ran, the visible setting switches back, and no launch is silently retried with the full environment.
- [ ] The post-release sign-in smoke of the three real CLIs on the allow-list is recorded in `research/` (owner/lead).
- [ ] The Claude reviewer's argv carries `--safe-mode` when the installed CLI declares it; when it does not, the launch
      goes without it and the card and the round carry a NOTE — never a refusal; `--bare` appears nowhere.
- [ ] The codex and claude cards say the reviewed branch's own configuration reaches the reviewer; the Claude row says
      what `--safe-mode` switches off.
- [ ] The `--restricted` one-turn check is recorded; a follow-up story exists or a recorded non-item says why not.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean; the whole `CoaiMcp.Tests.exe` suite green; this plan
      promoted with its deviations when the last built epic ships.

## 8. Risks

- **A structured override hides a limit the CLI says only on stdout as plain text.** Codex's error event carries the
  limit sentence (measured 2026-09-09, `ReviewerExecutor.cs:365-368`) and claude's envelope carries it (2026-09-29); agy's
  is on stderr (fixture). If a CLI release moves it, the reviewer is reported as `exit n: <reason>` with the sentence
  visible — a worse class but a true sentence, and the ledger line keeps the whole text. Mitigation: E1 keeps the
  stderr scan for all three; only the stdout scan is replaced by the adapter's parser.
- **`session limit` as a phrase could match a reviewer's own text** — but after E1 the phrase is matched only against the
  parsed error sentence and stderr, which is the whole point of doing E1 before E1.S4.
- **A JWT shape on the `SafeSource` road** redacts a sample token inside a served test fixture. Accepted: three dotted
  base64url segments starting `eyJ` are unmistakable, and a reviewer does not need a token's body to review the code
  around it. Named in the module docs.
- **Redacting kept evidence costs time on a 64 KB transcript.** The redactor is measured at ≤130 ms over 200 KB
  (`Redaction.cs:110-112`); the whole-call ceiling is about four seconds in the adversarial worst case (`:152-158`). Evidence
  is written on the failure path only.
- **The agy "measured version" constant goes stale** the next time agy is measured. Same risk Vibe and Kimi carry; the
  constant's doc names the RESULTS record it came from, and the warning says *re-run the probe*, never *refused*.
- **E4's allow-list breaks a sign-in that lives in an env var nobody listed.** The owner signs in through the browser
  on every CLI (no env-var sign-in), and the three CLIs are measured to find that sign-in under exactly the bench's list
  (2026-10-01, Windows). Linux and macOS are NOT measured under it — the post-release smoke covers them, the card shows a
  broken sign-in before a round (`providers` probes with the same mode), and the visible setting is the way back.
- **The pass-through is not secret-free.** `ProcessEnvironment.Passthrough` carries the proxy names (`:51`), and a proxy
  URL can hold `user:password@`. The allow-list narrows what a reviewer can print; it does not make the environment
  clean. Say so in the setting's description and in the docs; never call it "secret-free".
- **`--safe-mode` on a Claude that does not declare it** would refuse the launch. The detector reads `--help` first
  (the `ClaudeCapability` probe, `:111`); an undeclared flag is left out with a NOTE. A declared flag the CLI then
  mis-handles is caught by the first live round and is a plain failure with its sentence, never a silent loss.
- **`--restricted` may conflict with the shipped argv** (`--permission-mode plan` + `--disallowedTools`,
  `ClaudeRuntime.cs:94-95`) or confine `--add-dir` reads in a way a review needs. It is NOT sent until the lead's
  one-turn check says what it does; E5.S2 ships `--safe-mode` alone.

## 9. The two owner questions — asked, answered the same day, and what the answers rest on

Both were first written here as open questions with options; the owner answered on 2026-10-23 and asked for the question
consultant (consultId `fc43c9c96ce244cdb5c087f793ffcd4f`, gpt-6-astra). Its two precedents were verified by the lead
before this section was rewritten. The options are kept so the record shows what was chosen against.

**R2 — the LOCAL codex, claude and agy reviewers inherit the whole environment.** A local `coai-mcp` reviewer is started
with the developer's entire environment (`ProcessLauncher.cs:99`; the comment `:85-98` says this is deliberate: that is
where sign-ins, proxies and PATH live). Kimi, Vibe and every question consultant start from `ProcessEnvironment.Minimal`;
the Team server confines every reviewer (`src_server/src/Jobs/Confinement.cs:38`). A read-anywhere reviewer (claude
unconfined keeps `Bash`; codex `exec -s read-only` can still run commands) handed a prompt-injected diff could print
`env` into its answer, which the person then reads and the ledger keeps (redacted by shape only — a secret with no
recognisable shape passes).

| option | what it is | cost |
|---|---|---|
| A — keep as is | nothing changes; the comment's reasoning stands | the exposure above stays on local runs |
| B — a visible setting, off by default | an allow-list behind a switch the person turns on | an env-var sign-in not on the list breaks when switched on — visible at once on the card |
| **C — the allow-list by default, a visible switch back (chosen)** | `InheritsEnvironment = false` + `ProcessEnvironment.Passthrough` + the OS profile names + each CLI's own config names; the round's record says which mode ran; `providers` probes in the same mode | the same break on the first round after the release — bounded by the owner's answer and the precedent below, and by the post-release smoke |

**The owner's answer:** they sign in to every CLI through the **browser** — there is no env-var sign-in to lose — so the
one cost that made B the first recommendation is not there. **The precedent (verified):**
`dew_flow_benchmark · src/Bench.Domain/Probes/ProbeChildEnvironment.cs` (worktree `bench-probe-optout`) is an allow-list
(`PATH, PATHEXT, SystemRoot, windir, ComSpec, OS, NUMBER_OF_PROCESSORS, ProgramData, USERPROFILE, HOME, HOMEDRIVE,
HOMEPATH, APPDATA, LOCALAPPDATA, USERNAME, TEMP, TMP, TMPDIR`, plus the `ProgramFiles`/`ProgramW6432`/`PROCESSOR_`
prefixes) whose doc comment records that **claude 2.1.258, codex-cli 0.156.1 and agy 1.2.14 each started, found their
browser login and answered under exactly this set (2026-10-01, Windows)** — claude keeps its OAuth under `~/.claude`,
codex under `~/.codex`, agy under `%LOCALAPPDATA%`. It is another repository: evidence to cite, never a reference from
coai code (the list is copied into coai with its source named). → **E4.**

**R7 — the reviewed branch configures two of its own reviewers.** codex and claude run with the worktree as their
working directory (`ReviewerRuntime.cs:654,672`; `ClaudeRuntime.cs:98,104`), so the branch's `AGENTS.md`, `CLAUDE.md`,
`.codex/` and `.claude/` apply to the review of that branch — the thing being reviewed can steer its reviewer. agy does
not run inside it (`AntigravityRuntime.cs:62-64`). Vibe and Kimi get a sanitised snapshot (`ReviewSnapshots.cs`), but that
snapshot is a block for them only because their reads are bounded to the working folder — codex and claude can read any
path they are told, so a snapshot alone is not a block for them.

**The fact, established three ways.** (1) The precedent (verified): `dew_flow_conventions ·
research/RESULTS_subagent_model_tiers.md` §*E3 — the self-install observed (2026-10-14)* — a HEADLESS claude session in a
worktree ran that worktree's project `SessionStart` hook, which installed a hook into `~/.claude`. So a reviewed branch's
`.claude/settings*.json` can plausibly run commands inside a Claude reviewer; not yet verified under coai's exact
invocation (`--permission-mode plan` + `--disallowedTools`). (2) The repository's own recorded help texts: `--safe-mode`
is declared by 2.1.258 (`help-2.1.258-windows.txt:198-208`) AND 2.1.197 (`help-2.1.197-wsl.txt:166`) — *all customizations
(CLAUDE.md, skills, plugins, hooks, MCP servers, custom commands and agents …) disabled … Admin-managed (policy)
settings still apply. Auth, model selection, built-in tools, and permissions work normally. Sets `CLAUDE_CODE_SAFE_MODE=1`*;
`--setting-sources <user,project,local>` by both (`:211-212`, `:179`); `--restricted` by 2.1.258 only (`:180-194`: removes
the command-running tools and `WebFetch` unless `--tools` names them, *ignores user, project and local settings files
(managed settings and `--settings` still apply; add `--strict-mcp-config` to skip MCP servers too)*, and *confines the file
tools to the working directories (`--add-dir` included)*); `--bare` by both — and it skips **keychain reads**, which is the
owner's sign-in (§3). (3) The lead ran `claude --help` on 2.1.309 (free): the same four flags, the same wording, and for
`--bare` the sharper *OAuth and keychain are never read*.

| option | what it is | decision |
|---|---|---|
| a — a card note | the codex and claude rows say the branch's own configuration reaches the reviewer; agy's says it reads the branch through `--add-dir` and a `.gemini/` there is not measured (`runtimeNote`, `modelCard.ts:188-203`) | **E5.S3, built** |
| b′ — the CLI's own flag for Claude | `--safe-mode` on every Claude reviewer launch where `--help` declares it; a NOTE where it does not | **E5.S2, built first (§5)** |
| b″ — `--restricted --strict-mcp-config` as a second flag | also confines file reads to `--add-dir` — closes part of the unconfined-read finding of `PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md` | **a lead's one-turn check, then its own story or a recorded non-item** |
| b — the sanitised snapshot for codex and claude (`NeedsASanitisedSnapshot => true`, `.codex/`, `.claude/`, `CLAUDE.md`, `AGENTS.md`, `.gemini/` excluded; citations through the snapshot's `MapBack`) | removes the configuration from the tree they are pointed at, not from a path they can still be told | **E5's open tail — not built now** |
| codex | no flag that skips `AGENTS.md`/`.codex/` is known on codex-cli | the card note only; the snapshot stays the tail |

## Epics

### E1 — a limit is read from the CLI's own error channel (R1 + R4)

Goal: codex, claude and agy classify a rate limit the way Kimi does — from the structured failure and stderr, never from
the whole of stdout — and the two recorded limit sentences are in the lists. Depends on nothing. Done when the four
stories' tests are green with their break-it recorded and `module_runners.md` §*What counts as hopeless* says how each
CLI is read.

**S1 — codex.** RED: `ACodexReviewerIsRateLimitedOnlyByItsOwnErrorEventTests` —
`AQuotaWordInsideAnAgentMessage_WithExitOne_IsAnExitNotALimit` (stdout: an `item.completed` agent message whose text
says *the rate limit check in quota.cs is exhausted*, exit 1 → `NonZeroExit`, `Attempts == 1`, the ladder never waits),
`AnErrorEventSayingUsageLimit_IsALimit_AndHopeless` (`{"type":"error","message":"You've hit your usage limit…"}` →
`RateLimited`, `Hopeless`), `AStderrLineSayingHighDemand_StillCounts`, `TheReasonIsTheEventsSentence_NotAStdoutLine`.
Also RED (cadence consult 74decbae, verified): `AMixedErrorStream_TheHopelessEventWins` — exit 1, empty stderr, stdout
`{"type":"error","message":"HTTP 429 Too Many Requests"}` then `{"type":"turn.failed","error":{"message":"You've hit your
usage limit."}}` → `RateLimited`, `Hopeless`, `Attempts == 1` (a constructed input, not a captured sequence). Today's
`WhyItFailed` returns at the FIRST conclusive event (`ReviewerRuntime.cs:721`), so feeding only its result would keep the
transient 429 and relaunch.
Fix: `CodexRuntime.RateLimited` override = `RateLimitVerdict.ByText` over the messages of EVERY `error`/`turn.failed`
event (one new private reader beside `WhyItFailed`, sharing `CouldBeAFailure` and the event parsing, joined so `Spent`
sees each; a hopeless message wins over a transient one) plus stderr — stdout's other lines are never read. `WhyItFailed`
keeps its display role unchanged. No new API on `RateLimit`. `DeepseekRuntime` inherits it.
Break-it: delete the override → the first test reads `rate limited (after 5 attempts)`. Docs: `module_runners.md` :1193.

**S2 — claude.** RED: `AClaudeReviewerIsRateLimitedOnlyWhenItsEnvelopeSaysErrorTests` —
`AReviewTextMentioningQuota_WithExitOne_IsAnExitNotALimit` (envelope `is_error:false`, `result` is a review that says
*quota*), `AnIsErrorEnvelopeSayingRateLimit_IsALimit`, `TheReasonIsTheEnvelopesResult_WithItsHttpStatus`.
Fix: `ClaudeRuntime.RateLimited` = `ByText(result with { StdOut = WhyItFailed(invocation, result) })` (`:153-164`,
`SaysItFailed` `:173-174` decides). Break-it as S1. Docs: the same section; `AClaudeReasonInItsEnvelopeTests` gains the
cross-reference.

**S3 — agy.** RED: `AnAntigravityReviewerIsRateLimitedByItsStderrOnlyTests` —
`AModelLineSayingQuota_OnStdout_IsAnExitNotALimit` (a stream-json `assistant` line whose text says *quota*, exit 1),
`TheAgyErrorOnStderr_IsALimit_WithItsStatedReset` (fixture `consult-quota.stderr.txt` → `RateLimited`,
`CooldownParser.StatedReset` non-null), `AnEmptyStderr_WithAQuotaWordOnStdout_IsAnExit`.
Also RED: `TheStructuredResultEventOnStdout_WithEmptyStderr_IsALimit` — the recorded refusal of
`research/RESULTS_vendor_quota_gauges.md` §3.1 (a stream-json `{"event":"result","result":{"status":"ERROR","error":
"Individual quota reached. … Resets in 88h49m9s."}}` on stdout) with stderr EMPTY → `RateLimited`, `Hopeless`.
Fix: `AntigravityRuntime.RateLimited` = `ByText` over stderr plus the `error` field of a stdout `result` event whose
`status` is `ERROR` (the fact the plan first left open is established by that record — cadence consult 74decbae); every
other stdout line (model text, `step_update`) is never read. Break-it as S1. Docs: the same section.

**S4 — the two recorded sentences.** RED: `ASessionLimitAndASpendLimitAreLimitsTests` —
`CodexsSessionLimit_IsALimit_WhoseClockIsRead` (`You've hit your session limit · resets 9:30pm (UTC)` as a codex error
event → `RateLimited`; `RetryLadder.ResetOutlasts` true when 9:30pm lies past the budget, false inside it),
`ClaudesMonthlySpendLimit_IsALimit_AndHopeless` (the `is_error` envelope with `You've hit your monthly spend limit`,
`api_error_status` 429 → `RateLimited`, `Hopeless`, `Attempts == 1`), `AConsultationOnASpendLimit_IsAQuota`
(`ConsultFailures.cs:88` → `ConsultFailure.Quota`), `TheLedgerLineCarriesTheStatedReset` (`UsageLedger.StatedResetOf`
`:438-439`). Fix: `Phrases` += `session limit`, `spend limit`; `Spent` += `monthly spend limit`; the doc comment names both
sources (`CooldownParser.cs:204`, `ClaudeRuntime.cs:143`) as the rule at `:346` requires. The Team server parks a slot on the
same parser (`CooldownParser.cs:73-81`), so a parked session limit follows. Break-it: remove one phrase → `expected
RateLimited, got NonZeroExit`. Docs: `module_runners.md` :1208 (the `Spent` list is quoted there and must change with it).

Cornerstones: 0 of 2 — E1.S1, E1.S4.

### E2 — what the person reads is whole, cured and warned (R3 + R5 + R8)

Goal: the cell, the card and the diagnosis table say for agy what they say for Kimi. Depends on E1.S3 for the agy reason
to be the stderr line. Done when the three stories are green and the WSL and stated-reset sections of
`module_runners.md` are updated.

**S1 — the stated reset leads the cell (R3).** RED: `AStatedResetSurvivesTheSummaryCutTests` —
`AnAgyRefusal_ShowsItsResetClauseWhole_InTheCell` (`ReviewerSummaryFactory.Describe` of a `RateLimited` whose reason is
the 290-character agy line → the cell contains `Resets in 110h12m4s` whole), `TheClauseIsRedactedBeforeItIsCut` (a
`Bearer …` inside the clause → `[redacted]`, never a head), `AReasonWithNoStatedClause_IsCutAsBefore`,
`TheLedgerLineIsUnchanged` (`DescribeWhole` byte for byte as today), and (cadence consult 74decbae)
`ACredentialRightBeforeTheClause_IsNeverExposedByExtraction` — the synthetic reason `rate limit; Bearer
FAKECANARYavailable again at 9:30pm`: `ClockTime` has no opening word boundary, so extracting first and redacting the
clause after would show `available again at 9:30pm` cut out of the credential. ORDER, therefore: redact the WHOLE reason
(uncut) first, extract the clause from the redacted text, then cap the cell. The new lead is applied in `Describe` only;
the shared `Described` path that `DescribeWhole` uses is not changed.
Fix: in `Described` for `RateLimited` (`BoundedScheduler.cs:774-775`) quote the vendor's reset clause FIRST — the
quotation rule `KimiQuotaWords` set (`:10`: a quotation, never a parsed time, in a cell) — through one new pure reader
`CooldownParser.StatedClause(reason)` that returns the text the existing `Duration`/`ClockTime`/`OnDate` patterns
(`:206-224`) matched, so the clause list is the parser's and nothing copies it; then `Because(...)` as today. The parsed
instant stays where it is (the ledger's `statedReset`). Break-it: revert the lead → the cell ends `110h12m4…`. Docs:
`module_runners.md` :1229; `VendorReason.cs:9-12` remark updated to say the cell now leads with the clause.

**S2 — the agy sign-in cure, per host (R5).** RED: `AnAntigravityThatTimedOutSigningInIsToldTheCureTests` —
`AuthenticationTimedOut_OnStderr_HasACure` (`VendorDiagnosis.Classify("Error: authentication timed out")` is `Refused`
with a cure), `OnWindows_TheCureIsToRunAgyOnceAsTheUser`, `OnWslAndLinux_TheCureNamesTheLinuxInstall` (the cure contains
`antigravity.google/cli/install.sh` and says a Windows Antigravity binary reached through interop cannot finish its
sign-in), `TheRefutedAdviceIsStillOfferedNowhere` — `AntigravityOnLinuxTests.cs:85-99` extended with the new row's cure on
both hosts: no string contains `agy.exe`. Fix: a `Known` row (`"authentication timed out"`, `DiagnosisKind.Refused`) with a
`CureOn` arm by host (`:220-225`, the Mistral precedent). **Optional note, never a refusal:** when the configured
antigravity `executablePath` ends in `.exe` and the host is WSL, `VendorHealth.Note` says so with the Linux install
(`VendorProbe.cs:231-234`, a pure `AntigravityNotes.WindowsBinaryFromWsl(exe, host)`); RED
`AWindowsAgyPathOnWsl_GetsANote_AndStaysEnabled` asserts `Enabled` and `CliFound` unchanged — break-it: flip `Enabled` →
red, which is the owner's rule pinned. Docs: `module_runners.md` :925-935.

**S3 — the agy version is warned about, not refused (R8).** RED: `AnAntigravityVersionIsWarnedNotRefusedTests` —
`AnUnmeasuredVersion_GetsTheSharedSentence` (`VersionWarnings.Unmeasured` opening), `TheMeasuredVersion_GetsNoWarning`,
`NoVersionPrinted_GetsNoWarning`, `TheWarningReachesTheProbesHealth` (`VendorProbe.cs:234`). Fix:
`AntigravityRuntime.VersionWarning` through `VersionWarnings.Unmeasured("antigravity", running, measured, …)`; `measured`
is the newest version a `research/RESULTS_*.md` names as run (1.3.3 at `RESULTS_vendor_quota_gauges.md:238` today — read at
build time, cited in the constant's doc). The card needs no code (`module_extension.md:13023`). **Fact to establish:** does
agy's `result` event name the model that answered? If yes, reuse `IReviewerRuntime.Disowned` (`ReviewerRuntime.cs:444`) as
Vibe does — a launch answered by another model than the row's is disowned with the sentence; if no, the adapter's doc
says *checked: the result event's fields on 1.3.3 — no model name; not checked: later versions*. Break-it: return
`string.Empty` → red. Docs: `module_runners.md` (the agy adapter section) and the research record.

Cornerstones: 0 of 1 — E2.S1.

### E3 — redact before you cut, on every road (R6)

Goal: no vendor text reaches a note, a file, a card or a summary cut before it is redacted, and three more key shapes are
known to both halves. S1–S4 depend on nothing; S5 depends on the redaction plan being on `main` (§4). Done when the
roads' tests are green and `module_core.md` :58-60 lists the new shapes with their parity.

**S1 — the long last line.** RED: `AStderrLineIsRedactedBeforeItIsCutTests` —
`ASecretStraddlingTheSafetyCap_LeavesNoHead` (a 4 100-character single stderr line with `sk-` + 40 characters placed at
offset 3 995 → `TailOf` holds no `sk-`), `AShortTail_IsUnchanged`. Fix: `OpeningOfLastLine` (`:474-480`) returns
`VendorReason.Written(line)` (redact, then cap) instead of `VendorReason.Capped(line)`. Break-it: swap back → the head
`sk-ab` survives. Docs: `module_runners.md` :779 (*a reason is never half a line*).

**S2 — the probe note.** RED: `AProbeNoteIsRedactedBeforeItIsCutTests` — `AVersionRefusalCarryingABearer_IsRedactedInTheNote`,
`AKilobyteRefusal_IsCappedAtTheSafetyCap`. Fix: `VendorProbe.Refused` (`:264-275`) passes `said` through
`VendorReason.Written`. Break-it as S1.

**S3 — kept evidence.** RED: `KeptEvidenceIsRedactedBeforeItIsBoundedTests` — `ATranscriptWithAnAuthorizationHeader_IsKeptRedacted`,
`AnEmptyAnswersTranscript_IsKeptRedacted` (`Transcript` `:729-730` via `:1072`), `ALayoutIsKept` (line count unchanged —
the `SafeSource` road, `module_core.md:59`), `ATranscriptPastTheCap_IsRedactedThenCut`. Fix: `EvidenceFile.Keep` writes
`Bounded(Redaction.SafeSource(text))` — one change covers the reviewer's evidence and the consultation's (`:519`). Break-it:
drop the pass → the header is on disk.

**S4 — the keep-failure note.** RED: `AKeepFailureNoteIsRedactedTests` — `AnIoExceptionMessageInTheNote_IsRedacted` (an
`IOException` whose message carries a `token …` → the note says `[redacted]`). Fix: `ReviewerExecutor.cs:525` wraps
`e.Message` in `Redaction.SafeText(…, Redaction.DetailLimit)`. Break-it as S1.

**S5 — three shapes, both halves (after the redaction plan).** RED, C#: `AGoogleKeyAJwtAndAnOauthTokenAreRedactedTests`
— `AnAizaKey_IsRedacted` (`AIza` + 35 of `[0-9A-Za-z_-]`), `AYa29Token_IsRedacted` (`ya29.` + 20–512), `AJwt_IsRedacted`
(three dotted base64url segments, the first `eyJ`, each 8+, the last ≤512), `AWordStartingEyJ_IsKept` (`eyJust` with no
dots), `TheShapesAreOnTheSafeSourceRoadToo`. RED, TS: the same four in `notifications.test.ts`, and the parity corpus
`noticeShapes.ts` carries all three so `run-parity.mjs` proves the halves agree. Fix: `VendorPattern` (`Redaction.cs:83-84`)
and `notifications.ts:183` gain the three alternations, every quantifier bounded, no `IgnoreCase` (the file's rule `:25-27`);
`module_core.md:58` lists them as the fifth–seventh secret shapes in `Patterns`, `Secrets` and `Shapes`. Break-it: add the
shape to one half only → parity red. Lands by rebase onto the merged redaction plan; its status line is the gate.

Cornerstones: 0 of 1 — E3.S3.

### E4 — a local codex, claude or agy reviewer starts from an allow-list, with a visible way back (R2)

Goal: the three local reviewers no longer start with the developer's whole environment. The child gets
`ProcessEnvironment.Passthrough` (the proxy and certificate names, `ProcessEnvironment.cs:50-52`), the OS profile names the
bench measured the three CLIs signing in under (§9: `PATH, PATHEXT, SystemRoot, windir, ComSpec, OS, NUMBER_OF_PROCESSORS,
ProgramData, USERPROFILE, HOME, HOMEDRIVE, HOMEPATH, APPDATA, LOCALAPPDATA, USERNAME, TEMP, TMP, TMPDIR`, the
`ProgramFiles`/`ProgramW6432`/`PROCESSOR_` prefixes; on Linux and macOS also `USER, LOGNAME, SHELL, LANG, LC_ALL, LC_CTYPE,
TERM, XDG_CONFIG_HOME, XDG_CACHE_HOME, XDG_DATA_HOME, XDG_RUNTIME_DIR, DOTNET_ROOT` — the names `MinimalUnix` `:90-91` already
needs, plus the XDG trio a CLI's config dir can be moved by), and each CLI's own config names (`CODEX_HOME`,
`CLAUDE_CONFIG_DIR`, `ANTHROPIC_API_KEY`/`OPENAI_API_KEY`/`ANTIGRAVITY_API_KEY` for a row whose key is set — the adapters
set those three on the request already, `ReviewerRuntime.cs:691-692`, `ClaudeRuntime.cs:109-111`, `AntigravityRuntime.cs:67-69`;
agy names no config variable in its adapter, which is what was checked). A visible operator setting switches back to
inherit; the round's record says which mode ran; a failure under the allow-list is NEVER retried with the full environment.
Depends on E1 (a sign-in failure must be classified as what it is before this changes how often it happens). Done when
the stories are green, `module_runners.md` §*The launcher's two environments* (:599) describes the third environment, and
the post-release smoke is recorded.

**S1 — the list and the switch.** RED: `ALocalReviewerStartsFromTheAllowlistTests` —
`ByDefault_TheRequestDoesNotInherit_AndNamesTheReviewerAllowlist` (each of the three `Build` results carries
`InheritsEnvironment == false` and `Passthrough` set-equal to the new `ProcessEnvironment.Reviewer` list for the platform),
`WithTheSettingOnInherit_TheRequestInheritsAsToday`, `TheRowsApiKeyIsStillAppliedOnTop` (`Environment` applied last,
`ProcessLauncher.cs:82-83`), `TheReviewerListIsASupersetOfPassthrough_AndNeverNamesACredentialWord` (no name containing
`KEY`, `TOKEN`, `SECRET`, `PASSWORD` except the three per-row key names the adapters set themselves). Fix: one
`ProcessEnvironment.ReviewerFor(isWindows)` beside `MinimalFor` (`:97-98`), its doc naming the bench record as its source;
the three adapters' `Build` return `request with { InheritsEnvironment = !settings.AllowlistedEnvironment, Passthrough =
ProcessEnvironment.Reviewer }` through ONE helper (`ReviewerEnvironment.Apply(request, settings)` — the server's
`Confinement.Apply` shape, `src_server/src/Jobs/Confinement.cs:38`, which stays the server's: there is no unconfined launch
there); `ReviewerSettings` gains `EnvironmentMode` (`allowlist` default, `inherit`) read from the vendor's settings beside
`Confined` (`:140`), surfaced in the panel's settings with a one-line description that says the pass-through still carries
the proxy URL. Break-it: default to `inherit` → the first test red. Docs: `module_runners.md` :599.

**S2 — the proof in a child process (the RED test the lead asked for).** RED: `ASentinelNeverReachesAReviewerTests` —
`AnUnrelatedVariableInTheServersEnvironment_IsAbsentFromTheChilds` (the test process sets
`COAI_TEST_SENTINEL_SECRET=…`, launches the suite's fake CLI — a script that prints its environment — through the real
`ProcessLauncher` with a request built by each of the three adapters, and asserts the printed environment has no
`COAI_TEST_SENTINEL_SECRET` while `PATH` and the profile names are present), `WithInheritOn_TheSentinelIsPresent` (the way
back works), `AProxyNameIsPresentEitherWay`. Three vendors × two modes through one `[Theory]`. Break-it: set
`InheritsEnvironment = true` in the helper → the sentinel appears. No vendor CLI is run.

**S3 — the record says which mode ran, and nothing retries.** RED: `ALaunchLineSaysItsEnvironmentModeTests` —
`TheLedgerLineCarriesAllowlistOrInherit` (`LaunchOptions`, `LaunchOptions.cs:133-141`, gains `Environment = "allowlist" |
"inherit"`, written by the same path that writes `Cli`/`CliVersion` — story 3.4 of the round counter, `module_runners.md`
:1740), `TheProbeRunsInTheSameMode` (`VendorProbe` builds its `--version` request through the same helper, so the probe
and the round never differ in mode — it does NOT prove a sign-in: `VendorProbe.cs:250` returns the configured auth label
on a version success without validating credentials, so a sign-in the allow-list breaks shows first as the round's own
classified failure, which is why E1 lands first and S4 exists — cadence consult 5119c081), `AFailureUnderTheAllowlist_IsReportedInThatMode_AndNotRelaunched` (the executor
makes one launch; the outcome's sentence ends *(allow-listed environment — switch the vendor's environment setting to
inherit to compare)*). Break-it: add a second launch on failure → red. Docs: `module_runners.md` :1740 and the extension's
usage view if it lists launch options (`module_extension.md`).

**S4 — the post-release sign-in smoke (manual, paid, owner/lead).** One AUTHENTICATED review round per vendor on the
allow-list default, Windows first, then WSL/Linux and macOS (all unmeasured by the bench; names such as a D-Bus/keyring
or npm-prefix variable are measurement questions, not added blind; `SSL_CERT_FILE` already rides `Passthrough`). This is
recovery evidence, not pre-release compatibility evidence — the `inherit` switch is the way back meanwhile, recorded in `research/RESULTS_reviewer_allowlist_sign_in.md`
with versions; a vendor that fails is switched to `inherit` by the setting and the missing name is added to the list in a
follow-up story — never by widening the default blind.

**S5 — the setting reaches the launch, end to end (plan gate b2b81e47, finding 1).** The S1–S3 tests hold the helper and a
fake child; a wiring error could still leave the panel showing `inherit` while launches stay allow-listed, or the probe and
the round run in different modes. RED: a scenario-harness flow `TheEnvironmentSettingReachesTheProbeAndTheRound` — change
the vendor's `EnvironmentMode` through the same settings path the panel writes, run a `providers` probe and a reviewer
launch against the fake CLI that prints its environment, and assert for BOTH modes that the effective environment (the
sentinel absent/present) and the recorded mode (ledger line, probe note) agree. Break-it: read the mode from a default
instead of the stored setting → red. Docs: the flow is catalogued in `research/module_tests.md`.

Cornerstones: 0 of 2 — E4.S1, E4.S2.

### E5 — the Claude reviewer is not steered by the branch it reviews, and the cards say who is (R7)

Goal: `--safe-mode` on every Claude reviewer launch whose CLI declares it (both recorded versions do), detected from
`--help` through the probe that already reads it, a NOTE where it is absent; the codex and claude cards say the reviewed
branch's own configuration reaches the reviewer. S2 is built FIRST of the whole plan (§5). Depends on nothing. Done when
the stories are green and `module_runners.md` §*The claude consultant is confined by an allowlist* (:1448) gains the
reviewer's paragraph.

**S1 — the fact, recorded.** No code. `research/RESULTS_claude_reviewer_safe_mode.md`: the three sources of §9 (the
conventions E3 observation, the two recorded help fixtures with line numbers, the lead's 2.1.309 run), the `--bare`
exclusion and why, and the open `--restricted` question with the exact one-turn command the lead will run. Written before
S2's code so the plan gate reads the fact and not a claim.

**S2 — `--safe-mode`, detected, never refused.** RED: `TheClaudeReviewerRunsInSafeModeTests` —
`WhenHelpDeclaresSafeMode_TheArgvCarriesIt` (fixture `help-2.1.258-windows.txt` → `--safe-mode` present, before
`--permission-mode`), `WhenHelpDeclaresIt_OnTheOlderCli_Too` (fixture `help-2.1.197-wsl.txt:166`),
`WhenHelpDoesNotDeclareIt_TheArgvGoesWithout_AndTheLaunchCarriesANote` (a fake help text without the flag → no
`--safe-mode`, `LaunchTurn.Warning`/the invocation's note says *the installed claude does not declare --safe-mode; the
reviewed branch's CLAUDE.md, hooks and MCP servers reach this reviewer*), `ADeclarationWithARequiredValue_IsNotSupport`
(the `RestrictedIn` rule, `ClaudeCapability.cs:130-137`, applied to the new flag), `BareIsNeverSent`,
`TheConsultantsArgvIsUnchanged` (the question consultant and the stuck consultant keep theirs — this story is the
reviewer's). Fix: `ClaudeCapability` grows a second declaration (`SafeModeIn(helpText)`, same ANSI-stripping line scan,
same *flag, not flag-with-value* rule) read by the SAME `ProbeAsync` (`:111`, one `--help` per launch, never a second
process); `ClaudeRuntime.Build` takes the capability (as the consultant's planner does) and emits `--safe-mode` when
declared. **The reviewer paths do not probe today** (verified, cadence consult 5119c081: `ProbeAsync` is called only by
`ClaudeConsultant.cs:76` and `ConsultantsReadMode.cs:137`; `RosterBuilder.SettingsFor` `:524` and the Team server's
`ReviewLauncher.cs:375` leave `ClaudeCli` at `Unprobed`), so S2 adds an asynchronous preparation step on BOTH reviewer
paths that fills `ClaudeCli` before `Build`, plus RED `ARealReviewFromNormalSettingsCarriesSafeMode` — a scenario test
that starts from ordinary settings, runs the local round path against the fake CLI whose `--help` declares the flag, and
observes `--safe-mode` in the eventual argv (and the Team-server twin in its job test). A help that timed out, was cut
or failed is UNKNOWN, not "does not declare": the launch goes without the flag and its note says the capability could
not be read (`AnUnreadableHelp_IsUnknown_AndSaysSo`). A visible per-vendor setting `SafeMode` (`on` default, `off`) is
the way back the §1 promise needs — E4's environment switch cannot remove an argv flag (`TheSafeModeSettingOff_DropsTheFlag_AndSaysSo`;
`off` also removes an inherited `CLAUDE_CODE_SAFE_MODE` from the child's environment, or the way back is not one).
Risk consult 958e430d (verified against `ClaudeRuntime.cs:85-110`): the probe is FRESH per round — no executable+version
cache (a cache still needs a version spawn to see an in-place update; budget the existing two 10 s probe attempts), test
`ASwappedCliBetweenRounds_IsReadAgain`; an unsupported-flag failure is NEVER retried without `--safe-mode`; on the Team
server the probe request goes through the same `Confinement`, slot `HOME`/config, temp folder and cancellation budget as
the launch (`ClaudeCapability.AskAsync` `:139` inherits today — test `TheTeamProbeRunsUnderTheJobsConfinement`); the
consultants' argv and their Unknown → refusal for `--restricted` stay exactly as they are (`TheConsultantsUnknownStaysARefusal`,
fresh, resumed and question consultations). The absent-flag note does not say MCP servers reach the reviewer — it already
sends `--strict-mcp-config`; it names the branch's CLAUDE.md and hooks only; the card's existing `versionWarningNote`/`runtimeNote` carries the absent-flag note through `providers`. The
`--restricted --strict-mcp-config` pair is NOT added here: after S2 ships, the lead runs one turn of coai's exact argv
plus those two flags and records whether they conflict with `--permission-mode plan`/`--disallowedTools` and whether
`--add-dir` reads survive; a *yes, compatible* opens its own story (it would also close part of
`PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md`), a *no* is a recorded non-item. Break-it: drop the flag from
the argv → the first test red; send `--bare` → `BareIsNeverSent` red. Docs: `module_runners.md` :1448; `POST_DEPLOY.md`
(the Team server's installed claude declares the flag, or its rows carry the note).

**S3 — the card says who steers the reviewer.** RED: `AReviewerSteeredByTheBranchSaysSoTests` —
`TheCodexRowSaysTheBranchsAgentsMdAndCodexConfigReachIt`, `TheClaudeRowSaysWhatSafeModeSwitchesOff` (and, when the flag
is absent, what reaches it), `TheAgyRowSaysItReadsTheBranchThroughAddDir_AndAGeminiFolderIsNotMeasured`,
`NoRowsEnabledChanges`. Fix: a per-runtime `ConfigurationNote` on `IReviewerRuntime` (default empty, like
`VersionWarning` `:373`) folded into `VendorHealth.Note` by the probe; the card draws it as it draws `runtimeNote`
(`modelCard.ts:188-203`) — a sentence, never a held switch. Break-it: return empty for codex → red. Docs:
`module_runners.md` and `module_extension.md` (the card's notes).

**S4 — the effect observed, not only the flag (plan gate b2b81e47, finding 0).** The S2 tests prove `--safe-mode` is in
the argv; they do not prove a branch's hook stops running under coai's exact invocation. The lead runs ONE real Claude turn
(paid, cheap) in a scratch `testsToDelete-` worktree whose `.claude/settings.json` registers a harmless canary
`SessionStart` hook that writes a marker file inside that same folder: once with coai's shipped reviewer argv (with
`--permission-mode plan`, `--disallowedTools` and `--safe-mode`) → the marker must be ABSENT; once as a control without
`--safe-mode` → the marker present (a control that does not fire makes the run prove nothing). The same pair of runs also
observes that the review is not quietly worse (cadence consult 5119c081; the recorded help says permissions and built-in
tools work normally, which is a claim, not an observation): an allowed file read in the worktree, a read through the
`--add-dir` directory, a user permission denial still enforced, a prohibited write still refused, and the fast-mode
`--settings` file still applied. And the one case that could make safe mode LESS safe (risk consult 958e430d): safe mode
also switches off the person's USER hooks (managed hooks stay), so a canary read denied ONLY by a user `PreToolUse` hook
is tried with and without the flag — denied before and readable after is recorded as the trade, the card note says that
user hooks do not apply to a safe-mode reviewer, and coai's own confinement (plan mode + `--disallowedTools`) is what
the claim rests on, never a user hook. S4 is built and released WITH S2 (S1's evidence and this check travel in S2's PR), not
after it. Recorded in
`research/RESULTS_claude_reviewer_safe_mode.md` with the CLI version. The card's and the docs' claim is limited to the
versions where the effect was observed; on any other version the S2 note says the effect is not measured there (a note,
never a refusal). Only the canary's own `testsToDelete-` folder is removed afterwards.

**Open tail (not built now):** the sanitised snapshot for codex and claude — `NeedsASanitisedSnapshot => true` on both
adapters (`ReviewerRuntime.cs:410`), `.codex/`, `.claude/`, `CLAUDE.md`, `AGENTS.md`, `.gemini/` excluded, every finding's
path mapped back through the snapshot's `MapBack` as Vibe's and Kimi's are (`src_mcp/runners/Snapshots/*`). For CLIs that
read any path they are told it narrows what they are pointed at, not what they can reach — which is why it waits for the
`--restricted` answer (which does confine reads) and for the owner to ask.

Cornerstones: 0 of 1 — E5.S2.

### E5.S1/S2 — status: built 2026-10-23; S4's paid run made the same day

Built on branch `docs/cli-reviewer-gaps`; nothing is released. **E5.S3 (the card note) is NOT built.** E5.S4's paid run was made by the lead on claude 2.1.309 and is recorded in
[RESULTS_claude_reviewer_safe_mode.md](../research/RESULTS_claude_reviewer_safe_mode.md) §5: the branch's project hook did not run under `--safe-mode` and did run
without it; reads, sign-in and permission rules held; a hook handed in by `--settings` did NOT run under `--safe-mode` (the trade). Rows 5 and 8 used a scratch
`--settings` file, not the owner's user settings. The effect claim is limited to 2.1.309. No other epic was started.

**What shipped.**

- **S1** — `research/RESULTS_claude_reviewer_safe_mode.md` (the three sources with line numbers, why never `--bare`, the one trade, the pending canary and `--restricted`
  runs), with its row in `research/README.md`.
- **S2, the decision** — `ClaudeSafeMode.For(ReviewerSettings)` (`runners/Reviewers/ClaudeSafeMode.cs`): `--safe-mode` (placed before `--permission-mode`) when the row's
  `SafeMode` is on and `ClaudeCli.SafeMode == Declared`; else no flag and a note naming the branch's `CLAUDE.md` and hooks (never MCP) — one sentence each for
  not-declared, unknown (*could not be read*), never-asked and switched-off. `ClaudeCapability` grew `SafeModeSupport SafeMode` and `SafeModeIn(helpText)` (the
  `RestrictedIn` rule, generalised to one `Declares(help, flag)`), read from the SAME `--help`; `Unknown(reason)` sets both. Never `--bare`; never a retry without the flag.
- **S2, both reviewer paths ask.** Local: `RosterBuilder.ClaudeCapabilitiesAsync` → `ClaudeCapabilities` (beside `CodexTiers`), once per distinct executable whose row is ON,
  every round, no cache, in the coai data folder — passed through the plan, code, document and feature stages into `BuildWork(…, claude:)` → `RosterFrame` → `SettingsFor`
  (security lane included). Team server: `ReviewLauncher` probes per job through the same `Shaped(request, slot, work)` that shapes the launch (confinement, slot `HOME`,
  the job's folder as temp, the job's cancellation).
- **S2, the note and the way back.** `ReviewerInvocation.Note` (made by the pure `Build`) → `ClaudeRuntime.TakeTurnAsync` → `LaunchTurn.Noting` → the outcome's `Warning` → the
  round's reviewer line. The row setting `safeMode` (`on` default, `off`; `VendorDto`/`ProviderSettings.SafeMode`; `--features` lists `safeMode`; the extension's model card
  select, stored as `off` only, sent only to a binary that lists the feature) removes the flag AND an inherited `CLAUDE_CODE_SAFE_MODE` from the child's environment.
- **The card note of E5.S4's recorded trade** (*safe mode also switches off your own user hooks for this reviewer; coai's own limits keep it read-only*) is the hint under the
  select and in its help — the only place it is said. The consultants' argv and their `Unknown` → refusal are untouched.

**RED observed first** (against the code before the fix, not a setup error):

| test | failure message |
|---|---|
| `WhenHelpDeclaresSafeMode_TheArgvCarriesIt` | `Expected argv {"-p", "--output-format", "json", "--permission-mode", "plan", "--disallowedTools", …} to contain "--safe-mode".` |
| `WhenHelpDoesNotDeclareIt_…AndTheLaunchCarriesANote` | `Expected launch.Note "" to contain "the installed claude does not declare --safe-mode".` |
| `AnUnreadableHelp_IsUnknown_AndSaysSo` / `ALaunchNobodyProbed_…` | `Expected launch.Note "" to contain "could not be read"` / `"was not asked"` |
| `TheSafeModeSettingOff_DropsTheFlag_AndSaysSo` | `Expected off.Note "" to contain "switched off".` |
| `ADeclarationWithARequiredValue_IsNotSupport` (with `SafeModeIn` stubbed to false) | `Expected ClaudeCapability.SafeModeIn("Options:\n  --safe-mode [mode] …") to be True because an OPTIONAL value is not taken from a following option, but found False.` |
| `ARealReviewFromNormalSettingsCarriesSafeModeTests` (the reviewer path never reads what it asked: `ClaudeCapabilities.For` stubbed to `Unprobed`) | 6 of 15 red: `Expected launches to contain only items matching … because every claude review launch carries --safe-mode, but {{"-p", "--output-format", "json", "--permission-mode", "plan", …` and `reviewers: "all 1 reviewers answered; warned — claude/PlanCritique: the installed claude was not asked about --safe-mode for this launch, so it was not sent; …"` where `does not declare --safe-mode` was expected |
| `TheTeamClaudeReviewerRunsInSafeModeTests` (the server never probes) | 4 of 6 red: `Expected claude.Probes to contain a single item because the job asks the box's claude for its help before it builds the launch, but the collection is empty.` |

**Break-its** (each by editing the code, running, then WRITING the file back and rebuilding — a copy would keep its mtime):

| break | red |
|---|---|
| the flag dropped from `Build`'s argv | `WhenHelpDeclaresSafeMode_TheArgvCarriesIt`, `WhenHelpDeclaresIt_OnTheOlderCli_Too`, and the plan, code, document and swapped-CLI scenarios |
| `SafeModeFlag` set to `--bare` | `BareIsNeverSent`, `WhenHelpDeclares…` |
| `SafeModeIn` as a substring test | `AMentionInProse_IsNotADeclaration`, both `ADeclarationWithARequiredValue_IsNotSupport` cases |
| `Unknown(reason)` no longer sets `SafeMode` | `AnUnreadableHelp_IsUnknown_AndSaysSo` |
| the env removal dropped | `TheSafeModeSettingOff_DropsTheFlag_AndSaysSo`, `TheRowsApiKeyIsStillApplied_…`, `ARowThatSwitchedSafeModeOff_IsLaunchedWithoutIt_AndWithoutTheInheritedVariable` (the child's own environment record) |
| an Off row still probed | `ARowThatSwitchedSafeModeOff_IsNeverAsked` |
| the consultant treats `Unknown` as ready | `TheConsultantsUnknownStaysARefusal` (and `AClaudeThatCannotBeStarted_IsRefusedAsCliNotFound_WithTheInstallCure`) |
| a static cache of the answer | `ASwappedCliBetweenRounds_IsReadAgain` (the others it reds are the cache outliving the test) |
| the document stage not passing `claude:` | `ADocumentReview_FromNormalSettings_CarriesSafeMode` |
| the feature stage not passing `claude:` | `AFeatureRound_AsksItsClaudeBeforeItBuilds_AndSendsSafeMode` |
| the note not carried by `TakeTurnAsync` | `WhenHelpDoesNotDeclareIt_…`, `AClaudeWithoutTheFlag_RunsWithoutIt_AndTheRoundSaysSo`, `AHelpThatNeverAnswers_…` |
| the Team probe not shaped | `TheTeamProbeRunsUnderTheJobsConfinement`: `Expected probe.InheritsEnvironment to be False because the probe is confined like the review …, but found True.` |
| the extension's feature gate, the claude-only rule and the runtime check removed | three of six in `aClaudeRowHasASafeModeSwitch.test.ts` |

**Deviations from the plan, and why.**

1. **`claude:` is an OPTIONAL parameter of `BuildWork`**, unlike `codexTiers`. A required one would have changed about a hundred test call sites for a fault that is visible
   (no flag, a note) instead of fatal; the four stages' scenario tests are what hold each stage to asking.
2. **The local probe runs in the coai data folder, not the worktree** — the one place a help must not be started from is the tree under review.
3. **The absent-flag note does not travel on the Team server path.** `ReviewAttempt` has no per-launch warning channel, so there the note is the box's own `claude --help` and
   `POST_DEPLOY.md` (*A third thing item 11 can be*) says how to read it. The Team server has no per-row `safeMode`; it keeps the default.
4. **The setting is a per-row field** (like `fast`), not a panel-wide key like `coai.kimiAllowNetworkSignIn`, because a second claude row may differ. It is drawn on claude cards only.
5. **The user-hooks sentence is the setting's hint and help, not a launch note**; E5.S3's `ConfigurationNote` stays unbuilt.
6. **The extension's tests were written after its code** (RED first is not observed for `aClaudeRowHasASafeModeSwitch.test.ts`); the break-it row above is their teeth.
7. **Existing server test helpers now ignore a `--help` request** (`ReviewLauncherTests.Scripted`, `EachServerLineSaysWhatItRanWithTests.Received`): a claude job asks it first, and those tests count launches.

**Test runs** (executables only): `the whole `CoaiMcp.Tests.exe` suite through `low-priority.mjs`: 13686 total, 0 failed; `CoaiServer.Tests.exe`: 620 total, 0 failed, 1 skipped; the extension's `npm test`: 6515 tests, 0 failed (one unrelated `chatStoreFile` race test failed once under load and passed alone three times); the new classes alone: `TheClaudeReviewerRunsInSafeModeTests` 14, `ARealReviewFromNormalSettingsCarriesSafeModeTests` 15, `TheTeamClaudeReviewerRunsInSafeModeTests` 6, `aClaudeRowHasASafeModeSwitch.test.ts` 6, plus the feature twin in `AFeatureIsReviewedEndToEndTests`; `plan-lifecycle.mjs` clean.`
