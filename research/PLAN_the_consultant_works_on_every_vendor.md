# PLAN — the consultant works on every vendor, and says why when it cannot

> Status: **IMPLEMENTED, 2026-10-03 — all five epics, with two parts of the live acceptance NOT run and one review
> finding partial by decision.** Not run (E5.5): the forced-`run_command` consultation through the real consult path
> (the antigravity weekly quota was spent; requirement 1 rests on the direct agy measurement, 6/6, and the scenario
> tests on the recorded stream — owed once the quota resets) and the codex Check (rate-limited until 2026-10-08); see
> [RESULTS_consultant_live_acceptance.md](RESULTS_consultant_live_acceptance.md). Partial by decision: the whole-branch
> review's finding F left `ConsultantsReport`'s antigravity branch in place (its reason is in that bullet). Gate: epics
> 1–4 passed coai plan and code rounds; epic 5's plan round passed, its code round could not run (every coai vendor out
> of quota) and the operator proceeded on own reviewers, as for the whole-branch review. Deviations: *E1–E5 as built*,
> *E5.5 as run* and *Whole-branch review* below. Still open, each its own plan: the WSL Check from a Windows window,
> the reviewer's #504 double billing, the oversized files, the claude reviewer's allowlist, a turn that waits by the
> clock.
>
> Plan gate round 1 `proceed` (gemini, 7 findings: 5 accepted, 2 rejected with reasons) and an own Opus
> review (8 Major, 8 Minor, all accepted), both folded in; split into 5 epics per the gate's order. Scope:
> `src_mcp/runners/Consultation/*` (epic 3 added `ConsultantLimitations.cs`),
> `src_mcp/runners/Reviewers/{AntigravityRuntime,VendorDiagnosis,ClaudeCapability,ReviewerRuntime}.cs`,
> `src_mcp/runners/Platform/HostKind.cs`, `src_mcp/runners/Processes/ExecutableResolver.cs`,
> `src_mcp/src/Server/Consultation/*`, two new one-shot modes, one schema step, the Consultant tab of the extension
> (`src_vs_code/src/consultant*.ts`), `shared/*.json` (epic 3 added `consultant-limitations.json`). Windows and WSL
> alike.
>
> Related docs: [module_runners.md](module_runners.md), [module_server.md](module_server.md),
> [module_extension.md](module_extension.md), [architecture.md](architecture.md),
> [PLAN_consultant.md](PLAN_consultant.md), [PLAN_empty_vendor_answer.md](PLAN_empty_vendor_answer.md),
> [RESULTS_agy_allow_rule.md](RESULTS_agy_allow_rule.md).

## 1. The symptom

The operator configured the consultant as **antigravity · gemini-3.1-pro-high** and asked it two real
questions about a plan on 2026-10-02. Both came back **empty**, and the caller was told:

> the consultant (antigravity) exited cleanly but answered nothing; its transcript is kept at … — try once
> more with a sharper problem statement

Both transcripts are on disk (`%LOCALAPPDATA%\coai-mcp\unparseable\consult-antigravity-20261002-154011.txt`,
`-154044.txt`; a third from 2026-09-30). What they show, read line by line:

1. `init` reports `"permission_mode":"request-review"` and declares, among others, `view_file`,
   `grep_search`, `find_by_name`, `list_dir` and `run_command`. The model reads the tree (`view_file` on
   `BackupRunner.cs`, `BackupStore.cs`) and then calls `run_command` — `git grep -i sweep`, `ls …`.
2. Headless agy cannot ask a person, so it **auto-denies** the command and **ends the turn itself**. The
   model is never called again (the conversation's own `transcript.jsonl` ends on the denied step).
3. The stream's last event: `{"event":"result","result":{"status":"SUCCESS","response":"","denied_actions":[{"action":"command","display_name":"RunCommand"}]}}`, exit **0**. Stderr:
   `jetski: no output produced — a tool required the "command" permission that headless mode cannot prompt for, so it was auto-denied.`
4. A prompt saying "do not run commands" does not help: ONE attempt is enough to end the turn.

So "a sharper problem statement" is the wrong advice, nothing told the operator what was wrong, and the
settings page shows nothing about it.

**This exact defect was already fixed for REVIEWERS** (issue #504): `AntigravityRuntime.FollowUp`
(`src_mcp/runners/Reviewers/AntigravityRuntime.cs:84`) continues the same conversation with "commands will
stay denied, answer now". It is called only from `ReviewerExecutor.RunAsync` → `SecondLaunch`
(`ReviewerExecutor.cs:547`, `:583`). A consultation calls `executor.LaunchAsync`
(`ConsultationService.cs:634`), which has no second launch — the reviewer fix never reached it.

### The same question asked of every vendor

| vendor | works as a consultant today? | what is wrong | evidence |
|---|---|---|---|
| antigravity | **no** — empty on any shell attempt | above | the transcripts |
| claude | answers | its deny-list (`ClaudeConsultant.cs:43`, `--disallowedTools Edit Write NotebookEdit Bash WebFetch WebSearch Task Agent`) does not name PowerShell, so on Windows it reads outside the repository and is offered the user's whole tool set | measured 2026-10-01, 6/6 leaks, `research/RESULTS_question_consultant_capabilities.md` on branch `feat/question-consultant` |
| codex | answers | `-s read-only` does not confine READS on Windows (`pwsh Get-Content` outside the tree, 3/3); no flag fixes it | same document |
| local / api | answers | no tools — reads only what coai sends | by construction |

And for all of them: a failed consultation is classified nowhere, its reason is persisted nowhere the panel
can read, and the Consultant tab has no health information at all (`consultantRowView`,
`src_vs_code/src/consultantView.ts:212`, renders static hints only). Reviewers have `VendorProbe` /
`--providers`; consultant definitions are probed nowhere (`PanelService.cs:245` loops over
`_settings.Providers` only).

**WSL.** A Claude Code or Codex session inside WSL spawns its own Linux `coai-mcp` with its own data
directory (`~/.local/share/coai-mcp`) and its own `agy`/`claude`/`codex` (present here:
`~/.local/bin/agy`, `/usr/bin/claude`, `/usr/bin/codex`). Everything below must work there too.

## 2. What must be true when this is done

1. **An antigravity consultation whose COMMAND is denied returns advice.** coai continues the same
   conversation once (`--conversation <id>`, exactly one flag even on a resumed turn), in prose, naming the
   denied action and the read tools observed to work headless; the second launch's timeout is CAPPED to
   what the turn has left. Proven by a scenario test on the real stream AND by a live run that forces
   `run_command` through the consult path, on Windows and WSL.
2. **The antigravity prompt names only read tools OBSERVED to run headless without a prompt** — measured
   2026-10-02 ([RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md)): only
   `view_file`. `grep_search`/`find_by_name`/`list_dir` are declared by `init` but the model does not have
   them in `--mode plan`. (The plan round's gemini finding said exactly this and was rejected on the `init`
   declaration; the measurement proved the reviewer right — recorded as a deviation.) The follow-up
   answered 6/6 (3 per side).
3. **If the follow-up is denied or empty too, there is no third launch**; the turn ends classified with a
   cure naming what the person can do.
4. **Every failed consultation turn carries a closed classification and a cure** — `command-denied`,
   `read-denied` (another denied action: `read_file`, `read_url`), `empty`, `quota`, `rate-limited`,
   `vendor-refused`, `cli-not-found`, `timeout`, `deadline`, `exit`, `tree-changed`,
   `conversation-dropped`, `record-failed`, `cancelled` — on the record, in the database, and in the
   answer to the calling AI. The "sharper problem statement" sentence is gone. A turn that later succeeds
   clears the failure.
5. **A turn that failed for the vendor's reason (`command-denied`, `read-denied`, `empty`) does not spend
   the caller's consult budget — up to a bounded number of give-backs per window**, against the exact
   take it refunds, so the cap still cannot fail open. Tokens are still recorded in the ledger.
6. **The ledger bills a two-launch turn once and never under-bills**: for a cumulative vendor the
   field-wise maximum of the two reports; otherwise the sum.
7. **The claude consultant is confined by an allowlist** — `--restricted --tools Read,Glob,Grep` replaces
   the deny-list on every platform, and the EXACT shipped argv is measured (fresh and `--resume`d, Windows
   and WSL, a canary outside the repository) before the code changes and before any text calls it
   "confined".
8. **`coai-mcp --consultants`** (one-shot, no model calls): per caller kind, the vendor/runtime/model it
   resolves to; the CLI's presence, version and AUTH SOURCE (`VendorProbe` on the RESOLVED definition —
   not a sign-in check, which only Check and the last failure can show); the vendor's limitations on THIS
   platform; the agy settings path for this side (and on Linux/WSL the prefix snippet with its warning);
   the last answer and last failure on that row.
9. **`coai-mcp --check-consultant --caller <kind>`** (one-shot): ONE real paid turn of that row in a scratch
   git repository with a marker word and a canary file OUTSIDE it — never the person's code — reporting
   `answered` + marker read + canary not read, or the classified failure. Not counted against the budget,
   no consultation record, billed in the ledger, its deadline printed, exit 0 for every classified
   outcome. Its in-flight state survives a reload and never sticks; two concurrent checks never both launch.
10. **The Consultant tab shows under every row**: CLI and auth source; this platform's limitations; the
    last failure with cure and time (hidden when the row now resolves to another vendor/model); a Check
    button with a paid-call confirmation, a durable *Checking…* state and the result. An older server is
    named as "update the MCP server", never shown as empty.
11. **WSL**: everything works with a WSL-side `coai-mcp`; a Remote-WSL window gets the whole tab for its side.
    A plain Windows window shows the WSL side READ-ONLY from `coai.alsoWatchDataDirectories`: the last
    outcome and the last check (with the CLI facts that check recorded), labelled with the side, no Check
    button, a sentence pointing at a Remote-WSL window, and staleness judged only from the check's own
    state, never by comparing a WSL clock with the Windows one. (As built after the whole-branch review: the panel judges
    by its own elapsed time since it last saw the heartbeat change. Residual: a server one-shot mode reading the other
    side's record once cannot see a beat move, so it still compares the two clocks.)
12. **Limitation texts never claim more than was measured.** Each row names where it was measured or says
    it was not (codex on Linux/WSL: documented upstream, openai/codex#7657, not measured here).
13. **The agy allow-rule snippet appears only on Linux/WSL**, where a prefix rule was measured to work
    (P0), with the warning that prefix rules are not read-only. On Windows the tab says agy allows only
    exact commands there. coai never writes agy's settings file.

## 3. Constraints

- Reuse first: the #504 mechanics, `RetryLadder.WithinRemaining`, `ReviewerOutcome`/`RateLimit`/
  `VendorDiagnosis`, `ReviewerExecutor.Complaint`, `VendorProbe`, `coai.alsoWatchDataDirectories`,
  `textCopier`, `serverRun`, `runPanel`. Extend; do not copy.
- No new code in files over 800 lines beyond wiring: `ConsultationService.cs` (1031), `Program.cs` (2168),
  `panelView.ts` (3341), `panelProvider.ts` (4149). Splitting them is its own plan (§9).
- The reviewer's behaviour stays byte-identical (`ADeniedCommandIsAskedAgainTests` unchanged); the
  reviewer's own claude deny-list (`ClaudeRuntime.ReachTools`) is NOT in scope.
- Both new modes in `.agents/PROJECT.md`; a bad request answers 65, never 64.
- A webview page is tested by running it; no new assertion over page source text; no
  `${JSON.stringify(...)}` in a script template; no backticks in template literals.
- Durable status: persisted in-flight state, fenced, swept; never a pid as liveness. UTC everywhere stored.
- The schema step is append-only and numbered at merge time (branch `feat/question-consultant`, unmerged,
  uses 17). At the rebase onto main it became **19** — main had taken 17 (the questions) and 18 (the security
  evidence); see *Rebased onto main* below.
- Every lock names its atomic operation, carries an acquisition id, breaks by rename, releases fenced, and
  states its residual race in its own header (`reliability.md`).
- Out of scope: Check of the WSL side from a plain Windows window via `wsl.exe` (§9).

## 4. P0 — measured before promising

**2026-10-02, agy 1.2.15** ([RESULTS_agy_allow_rule.md](RESULTS_agy_allow_rule.md), harness
`scripts/probe-agy-allow-rule.mjs`): a GLOBAL `~/.gemini/antigravity-cli/settings.json` IS read by a
headless turn on both sides; on **WSL** the prefix rule `command(git grep)` ran 2/2; on **Windows** only the
exact command line ran (`command(git grep)` denied 2/2, `command(git)` 1/1). So the snippet is Linux/WSL
only. A denied step can report `state: DONE` — denial is read from `result.denied_actions` (by its
`action`) and the stderr line. The agy quota stream (`AGY_ERROR … RESOURCE_EXHAUSTED (code 429) … Individual
quota reached`, exit 3) already classifies as hopeless `quota` (`RateLimit.Phrases` has "quota", `Spent`
has "exhausted").

## 5. Epics and stories

The gate runs once per EPIC: each epic gets its own branch from the previous epic's commit
(`feat/consultant-works-everywhere-e<k>`), its own `review_plan` (`plan` + `epic: "k/5"`), all of its stories,
one commit, one `review_code` with the previous epic's commit as `baseRef`. The consultation cadence groups
E1–E3 and E4–E5. Models: the split was done on Opus 5.5 (the operator's order says Fable; this account's
Fable monthly limit was hit on 2026-10-02, so Opus is the recorded fallback); ordinary stories on Opus,
security-sensitive stories (E3.1, E4.2) on Fable if it is available again, else Opus — named per story in
the summary.

### Epic 1 — the antigravity turn answers (server)

- **E1.1 Measure the read tools, build the fixture, write RED.** One live agy turn per side asking it to use
  `grep_search`, `find_by_name` and `list_dir` in a scratch repo: which run headless without a prompt (same
  harness shape as P0, settings untouched). And the follow-up itself, measured before it is built: a
  forced `run_command` turn, then the prose follow-up on the same conversation, **3 repeats per side** —
  does the model answer, or reach for the shell again (cadence consultation fad9944d, 2026-10-02: a declared
  tool is a stronger pull than prose, and agy has no flag to undeclare it). A real successful follow-up
  stream becomes the companion fixture `antigravity-consult-advice.ndjson` (plan round of epic 1).
  Fixture `src_mcp/tests/Fixtures/antigravity-consult-denied.ndjson`
  + `.stderr.txt` from the 2026-10-02 transcript (paths redacted). RED, observed failing for the real
  symptom: `ADeniedCommand_IsContinuedInTheSameConversation_AndTheAdviceArrives` (today: "answered nothing
  … sharper problem statement") and `AResumedTurnsFollowUp_CarriesOneConversationFlag`.
- **E1.2 `AntigravityStream.Continue` + a precise `WasDenied`.** `Continue(first, conversation, message)`
  extracted from `AntigravityRuntime.FollowUp`: REPLACES an existing `--conversation` value or appends it,
  swaps stdin; the reviewer's `FollowUp` calls it with its unchanged `NoCommands` (byte-identical — its
  argv has no `--conversation`). `DeniedActions(transcript)` parses `result.denied_actions` as JSON and
  returns the action words; the stderr `auto-denied` line is matched in STDERR only (as `Complaint` does,
  `ReviewerExecutor.cs:701`), never in stdout, which carries the model's own tool parameters.
- **E1.3 `ConsultantTurn` — one or two launches.** `IConsultantRuntime` gains defaulted `FollowUp(first,
  launched)`, `DeniedActions(launched)`, `Toolbox`. `AntigravityConsultant` continues only for a denied
  `command` (a `read_file`/`read_url` denial gets its own follow-up wording naming that action).
  `ConsultantTurn.RunAsync` launches the second only when the first answered nothing AND a follow-up is
  offered AND the tree is still clean (`treeStillClean` re-snapshots through the filesystem invariant),
  with its timeout capped by `RetryLadder.WithinRemaining` (`ReviewerExecutor.cs:593` is the precedent),
  so the launcher — not the turn's `ConsultationDeadline` (`T + T/4`) — kills a hung second launch. Never a
  third. `ConsultantTurnResult` carries `SurvivingHandle` (the second launch's, else the first's — a killed
  second launch must not lose the conversation) and `Breached` (the tree changed before a follow-up could
  run); the service keeps its own final snapshot comparison too (plan round of epic 1).
- **E1.4 Billing and the prompt.** `ConsultationUsage.OfTwoLaunches(cumulative, first, second)`: cumulative
  → field-wise max (a timed-out second launch returns `Usage.None`, `ReviewerExecutor.cs:829`, and must not
  erase the first's tokens); otherwise the sum. `ThisTurnsShare` applied on every outcome path, not only
  success. `ConsultantPromptInput.Toolbox` under "## What you have", from the adapter of the record's FROZEN
  runtime, naming only the tools E1.1 observed. `ConsultationTurn` gains `FollowedUp` (defaulted).

### Epic 2 — failures are classified, persisted, explained, and not over-charged (server)

- **E2.1 `ConsultFailure`.** Closed record hierarchy with `Kind`, `What(vendor)`, `Cure`, `Evidence`;
  `ConsultFailures.Classify` maps `ReviewerOutcome` (`NotStarted`→`cli-not-found` with `InstallCure`,
  `TimedOut`, `RateLimited`→`quota`/`rate-limited` by `RateLimit.Hopeless`, `NonZeroExit`→ diagnosis) and an
  empty answer → `command-denied` / `read-denied` by the denied action, else `empty` with `Complaint`.
  `VendorDiagnosis` gains a `DiagnosisKind` column and `Classify(text)`; `For(text)` unchanged.
  `shared/consult-failure-kinds.json` is the vocabulary (C# reflection parity test; the extension's labels
  tested against it in E5).
- **E2.2 Evidence and the answer.** `EvidenceFile.Keep` extracted from `ReviewerExecutor.KeepIn/Bounded`
  (atomic, 64 KB cap) and used by the consultation's `KeepEvidence` (today unbounded and non-atomic,
  `ConsultationService.cs:906`). The empty/`Breach`/`Terminal`/`AfterTheLaunch` branches move into
  `ConsultationFailing.cs` as pure builders; `shared/refusal-sites.json` follows. The caller's answer on a
  vendor failure: the cure + "do not retry this consultation; carry on, or ask the person".
- **E2.3 A bounded, fenced give-back.** A counted stuck turn that produced NO advice for a reason outside the
  caller's control gives its call back — every failure kind except `cancelled` (the caller's own choice)
  and `tree-changed` (the consultant ran and broke the invariant); widened from denied/empty by epic 2's
  plan round. `TryTake` returns a `CounterTake` (window start + store: file or
  memory); `GiveBack(take, now)` refunds only that take — not after the window rolled over, not into the
  other store — and at most `GivesBackPerWindow` (3) per caller per window, the count kept in the counter
  file under the same lock. Ledger outcome words `command-denied` / `read-denied` added; the turn's tokens
  stay in the ledger. Two billing gaps found while building epic 1 and fixed here: `ThisTurnsShare` rebuilds
  `new Usage(in, out, cost)` and drops `TokensCached`, `TokensReasoning`, `NoPriceSet` and `NotCaptured` —
  carry them through; and it subtracts only ANSWERED turns (`record.Turns`), so a failed turn's tokens on a
  cumulative vendor come back inside the next answered turn's share — keep a running total of what the
  conversation has already been billed (on the record) and subtract that — an INTERRUPTED turn (Terminal
  keeps the handle, adds no turn) is the case epic 1's review named. And a pre-existing misclassification
  the `cancelled` / `deadline` kinds depend on: `ProcessLauncher` never throws on cancellation, it returns
  `TimedOut: true, Cancelled: true`, and `LaunchAsync` turns that into a plain `TimedOut`; the follow-up
  snapshot (`FilesystemInvariant.SnapshotAsync`) then throws a `ContextException` from a killed `git status`
  rather than an `OperationCanceledException` — so the service's cancellation arm is effectively
  unreachable and `AfterTheLaunch`'s `deadline` is never true. Pass `ProcessResult.Cancelled` through, and
  make the snapshot throw `OperationCanceledException` when its token is cancelled.
- **E2.4 Record, schema, query.** `ConsultationRecord` gains `FailureKind`, `FailureCure`, `Evidence`
  (null-normalised); `Answered` clears them. Schema step `WhyAConsultationFailed` (three
  `TEXT NOT NULL DEFAULT ''` columns on `consultations`), `ConsultationRow`, `ConsultationRows.From`,
  `RoundsDb.RecordConsultation`, a fifth `RoundsQuery` shape chosen by `HasColumn`. Numbered last, after
  rebasing on main.
- **E2.5 Health files.** `ConsultHealthStore` writes two files per caller kind under
  `<dataDir>/consultations/health/`: `<kind>.answer.json` (last answer: utc, consultation id, vendor,
  runtime, model) and `<kind>.failure.json` (last failure: + kind, what, cure, evidence, side), each written
  atomically (temp in the same directory, then move) and ONLY when its stored time is older than the new
  one, so two `coai-mcp` processes cannot drop a newer outcome. ONE rule says whether the failure is current
  — its time is later than the last answer's (`ConsultHealth.Current`) — and every reader (`--consultants`,
  the panel) goes through it; nothing is deleted. Orphaned `*.tmp` files in `health/` older than an hour
  and consultation evidence (`unparseable/consult-*`) older than 30 days are removed by the
  `ConsultationSweeper` tick (epic 2's plan round: a growth surface needs its retention).
- **E2 review fixes (2026-10-02, the code round + three own reviewers).** Decisions narrowed or widened:
  *resumable* is now only a TRANSIENT failure (`timeout`, `rate-limited`, `deadline`, and `record-failed` —
  the vendor answered, only our write was lost) whose conversation THIS turn's launches named; every other
  failure ends the consultation, and the record's older handle never makes a turn resumable. *DoNotRetry*
  widened from `command-denied`/`empty` to every failure that is neither transient nor `tree-changed` /
  `cancelled`. Caller versus deadline is decided by the tokens' state and passed into the classification (the
  launcher's `Cancelled` flag reports the linked turn token). A turn decides its failure ONCE and refunds at
  most once, after the record is written. The counter fails closed on an existing file it cannot read or
  parse. Consultation evidence moved to `unparseable/consultations/`, which the 30-day retention prunes; the
  retention runs after the record sweep and never breaks it. Vendor text is redacted before it is persisted.

### Epic 3 — claude is confined, and every limitation says where it was measured (server)

- **E3.1 Measure the exact claude argv** (security; Fable if available). The P0 harness shape for claude:
  `-p --output-format stream-json --verbose --permission-mode plan --restricted --tools Read,Glob,Grep
  --strict-mcp-config --add-dir <repo>`, fresh and `--resume`d (the resumed session opened under the OLD
  deny-list argv), Windows and WSL, 3 repeats each of: read inside the repo (must work), read a canary
  outside it (must not), any shell (must not be offered — `init.tools` read back). Also whether
  `--restricted` breaks an auth configured through `settings.json`. Recorded in
  `research/RESULTS_claude_consultant_confinement.md`. If a cell leaks, the argv is changed and re-measured
  before E3.2. **If `--restricted` breaks a working sign-in** (it ignores user settings), the fallback is
  `--tools Read,Glob,Grep` without it — measured 2026-10-01 to stop the shell and the network but NOT reads
  outside the working directories — and the claude rows of the limitations then say exactly that
  (`standing: unconfined`, "reads outside the repository through Read"), rather than "confined".
  **Measured 2026-10-02** ([RESULTS_claude_consultant_confinement.md](RESULTS_claude_consultant_confinement.md)):
  Windows 2.1.258 confined 9/9 fresh and resumed, sign-in intact; **claude 2.1.197 (WSL) has no
  `--restricted` and refuses the launch** (`error: unknown option '--restricted'`, exit 1); `--tools` alone
  held 9/9 there, but leaked on Windows 2.1.258 on 2026-10-01, so it is not claimed as confinement.
- **E3.2 The argv, chosen by the installed CLI.** `ClaudeCapability.ProbeAsync(launcher, executable, cwd, ct)`
  reads the CLI's `--help` on EVERY launch — NO cache (risk consultation 264fbcf2, 2026-10-03: a long-lived server's
  cached answer survives an in-place upgrade or downgrade of the CLI while the panel's one-shot probe shows
  the fresh one; measured cost 0.26–0.86 s on Windows, 0.35–0.49 s in WSL, against 5–30+ s turns; and WSL's
  `claude` is a symlink into an npm package, so an mtime-keyed cache would watch the wrong file) — bounded
  by a 10 s timeout per ask through the shared launcher. **Three answers** (epic 3's code round, security —
  the first build read every failure as "not supported" and so launched UNCONFINED on a hung help):
  `Declared` (the help lists `--restricted` as a flag), `NotDeclared` (the help came back without it, or with
  it taking a required value), `Unknown` (timeout, non-zero exit, empty help, a CLI that would not start) — asked
  once more, and a second `Unknown` REFUSES the turn before launch (`ConsultantPreparation.Refused`,
  `vendor-refused`, cure "claude --help did not answer, so coai cannot tell whether --restricted is supported
  and will not launch it unconfined — check the CLI (`claude --help`) or choose another consultant"); never a
  downgrade. The probe runs in the defaulted `IConsultantRuntime.PrepareAsync`, which the service calls for
  every consultant; `ClaudeConsultant.Build` stays pure and takes the fact from `ReviewerSettings.ClaudeCli`,
  which defaults to `Unprobed` — a value `Build` refuses with an `ArgumentException`, so a call site that forgets
  `PrepareAsync` fails loudly. Declared → `--restricted --tools Read,Glob,Grep`; NotDeclared → `--tools
  Read,Glob,Grep` and the row's limitation says this claude cannot be confined to the repository — update it.
  The turn RECORDS what it was sent (`ConsultationRecord.Confinement`, `ConsultationTurn.Confinement`, both health
  files), and every turn's capability is logged at Information, a refusal at Warning. A launch that still fails
  with `unknown option` classifies as `vendor-refused` with the cure "update the CLI". `Denied` deleted; the
  golden argv tests (supported/unsupported × fresh, resumed, model) own the whole argv — documented as asserting
  what is SENT, with E3.1 as the evidence of effect — and the two older claude consultant tests
  (`ConsultantsTests.ClaudeIsReadOnly_AndCanSTILLReadTheTree`,
  `ConsultStoryTwoGateTests.AConsultantMayREADTheTree_AndMayNotRunAShellInIt`) were folded into them.
- **E3.3 Limitations and the Linux agy path.** `shared/consultant-limitations.json`, embedded in C#: rows of
  `runtime`, `platform` (`windows|linux|wsl|macos` — every runtime has a row of its own per platform; there is
  no `any`), `capability` for claude (`restricted|no-restricted`), `standing`
  (`confined|unconfined|default-deny|unmeasured`), `text`, and `measured` (date, version, cells, document) or
  `source` saying it is not measured. claude rows are written from E3.1 only. `HostKind` (namespace
  `CoaiMcp.Runners.Platform`) from `OperatingSystem` + the WSL detection `VendorDiagnosis` already has.
  `AntigravityStream.InstalledExecutable` on Linux/WSL prefers an `agy` on the PATH and falls back to
  `$HOME/.local/bin/agy` only when the PATH has none. Epic 3's plan round: the lookup takes the capability as well
  as the host and runtime — since the code round as a plain qualifier word, `Lookup(host, runtime, capability = "")`,
  with a claude overload passing `ClaudeCapability.Qualifier` — so a claude without `--restricted` gets
  the not-confined row on ANY platform instead of the platform's measured one; explicit `macos` rows exist
  for every runtime (unmeasured where nothing was run); the behavioural evidence for claude is the
  committed live probe (`scripts/probe-claude-consultant-confinement.mjs`), re-run per platform in E5.5,
  with the golden argv tests beside it labelled as what is SENT. The claude REVIEWER's deny-list stays out
  of scope by the operator's decision and is tracked in
  [PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md](../todo/PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md).

### Epic 4 — the panel's source of truth (server)

- **E4.1 `--consultants`.** `ConsultantsReadMode.cs`: settings read as `--providers` does; per caller kind
  `ConsultantResolver.Resolve` → `VendorProbe.RunAsync` on the resolved definition (deduplicated by
  runtime + executable, in parallel) → `cliFound`, `version`, `authSource`; + limitations for this host;
  + both health files; + the check state (E4.3); + the agy settings path for this side and, on
  Linux/WSL, the snippet and its warning. Exit 0 with data (unavailable is data), 74 when the data
  directory is unreadable, never 64. Listed in `.agents/PROJECT.md` and the help text. The same answer is
  WRITTEN to `<dataDir>/consultations/health/consultants.json` (atomically, overwritten, with its time) by
  the mode and once in the background after the stdio server starts, so the OTHER side (a plain Windows
  window reading a WSL store through `coai.alsoWatchDataDirectories`) gets the resolved vendor, CLI facts
  and limitations too, labelled with when they were taken (cadence consultation 435b1b25, 2026-10-03: the
  read-only view otherwise had only outcome files).
- **E4.2 `--check-consultant --caller <kind>`** (security; Fable if available). Validate (65 otherwise);
  `Preflight(kind)` (unavailable → data, exit 0). Scratch: `coai-check-<guid>` under the temp directory,
  `git -c user.name=coai -c user.email=coai@invalid -c commit.gpgsign=false -c core.hooksPath= …` for init
  and commit, each with a timeout, `CHECK.md` with a random word, one uncommitted edit, and a canary file in
  a SIBLING directory with another random word. The turn goes through the same path as a consultation —
  `PrepareAsync` (a claude gets its `--help` probe; Unknown refuses), `Build`, `ConsultantTurn`, the
  invariant snapshots, `ConsultFailures` — with the consult prompt + `Toolbox` and a problem that asks for
  the word in CHECK.md AND explicitly asks the model to try reading the canary by its absolute path,
  replying "CANNOT" if it cannot (epic 4's plan round: a canary the prompt never names is a vacuous
  confinement test — even an unconfined runtime would pass); budget `min(ReviewerTimeout, 4 min)`; ledger
  `role consult-check`; delete the scratch. State in `<kind>.check.json` (per caller kind, never one shared
  file); stdout carries exactly ONE JSON document at the end (the deadline is in the state file and that
  document, never printed earlier). Result: `answered` + `markerRead` + `canaryRead` + the confinement it
  ran with, or the failure. The canary is reported as ONE of three, never as a bare boolean (cadence
  consultation 435b1b25: a model that declines on its own proves compliance, not confinement): `read` (its
  word is in the answer — a leak), `denied-by-cli` (the CLI's own record shows the read was refused —
  claude's `permission_denials`, agy's `denied_actions`, through the adapter's `DeniedActions`, which the
  claude consultant now implements too), or `not-attempted` (no word, no denial). Only `denied-by-cli` is
  observed confinement. Exit 0 for every classified outcome, `already-checking` included.
- **E4.3 The check lock and its state** — **redesigned by the risk consultation (faa596bf, 2026-10-03):**
  the heartbeat-fenced lock had a check-then-act gap (a process paused between verifying its id and the
  paid launch would wake, launch a second time and overwrite the newer result; a file cannot fence a
  process's memory). The check instead HOLDS `<kind>.check.lock` open with `FileShare.None` for its whole
  duration — the repository's own `SessionTurn` / `EngineLease` shape: the atomic operation is the
  exclusive open, the kernel releases it when the process dies, and a paused holder simply keeps holding
  it, so nobody "breaks" a lock and there is no rename. A second check whose exclusive open fails answers
  `already-checking` (no second paid launch). A reader decides liveness by trying the exclusive open
  itself (non-blocking): held → `checking`; free while `<kind>.check.json` says `checking` → `abandoned`
  (`ConsultCheckState.Settled(state, lockHeld)` pure). Only the holder writes `<kind>.check.json`
  (through `AtomicFile`), and only while it holds the lock. The residual is stated in the type's header:
  an advisory lock on Linux (`flock`) is honoured between .NET processes but not across the WSL/Windows
  9P boundary — so the OTHER side only reads the state file and judges a `checking` state by the
  `heartbeatUtc` the holder refreshes (`ConsultCheckState.SettledAcross`, four silent 15 s beats →
  `abandoned`), and a sweep never settles another side's record. (The first build probed the lock across
  the seam and so called a live WSL check abandoned from Windows; caught by the coai code round of epic 4
  and fixed with RED tests.)
- **E4.4 Sweeps.** Scratch `coai-check-*` older than a day removed at the start of every check AND on each
  `ConsultationSweeper` tick; `shared/temp-sweep.json` `neverSwept` gains the prefix (the test sweep must
  not race a live check); a `checking` state whose lock is free is rewritten `abandoned` by the sweep; an
  unreadable directory never stops the sweep.
- **E4 as built (2026-10-03) — deviations.** (1) The held-lock shape was EXTRACTED rather than copied:
  `runners/Files/HeldFile.cs` (`TryHold`, `IsHeld`, `TryHoldWithin`) now carries the exclusive open for
  `EngineLease`, `SessionTurn`, `RepositoryLock` and the new `ConsultCheckLock`; and `GitScratch.DeleteEvenIfReadOnly`
  moved out of `PanelService`. (2) The check's acquisition waits up to half a second, so a reader's momentary
  liveness probe is never read as a holder. (3) A scratch repository git could not make is `unavailable` with git's
  reason (no consult-failure kind fits a failure before any consultant ran). (4) The agy snippet's warning is a FIELD,
  `snippetWarning`, on the linux/wsl rows of `shared/consultant-limitations.json` — the loader refuses one without the
  other, or either off linux/wsl. (5) The sweeper's temp walk for stale `coai-check-*` is paced to once per ten minutes
  per sweeper (every check also sweeps at its start, unpaced); the abandoned-state rewrite runs on every beat and takes
  the lock to write. (6) `--consultants` probes only AVAILABLE rows, always as enabled, deduplicated by runtime +
  executable + model (the model since the code round) + the two facts the auth answer reads; a claude row nobody could probe reads `capability: unknown` and the
  `unmeasured` limitation. (7) The check's `deadlineUtc` is `ConsultationDeadline.For(min(ReviewerTimeout, 4 min))` + 2
  minutes of setup (the scratch's git, a claude's two `--help` asks), all under one cancellation — and, since the code
  round, `deadlineUtc` is that PLUS a teardown allowance (drain, scratch removal, the final write), computed once.
  (8) The canary has FOUR readings since the code round: `read` (its word in the answer or any launch's stream),
  `denied-by-cli` only for a refusal whose input names the canary or its directory (claude's `tool_input`, through the
  new `IConsultantRuntime.Denials`), `denied-by-cli-unattributed` for a refusal that names nothing (agy), and
  `not-attempted`. (9) The code round's other fixes: the probe carries the resolved model; a lock file that cannot be
  opened is 74, not `already-checking` (`HeldFile.Take` → `Hold`); a reader re-reads a free lock's state and falls back
  to the heartbeat when it cannot open the lock; liveness is the lock on this side and the heartbeat across the seam
  (the coordinator's fix), and a check refuses to start beside a fresh other-side `checking` — one data directory
  shared by both sides is otherwise unsupported for checks; the turn's bookkeeping (answer test, kept transcript,
  ledger row) is one copy with the consultation's (`ConsultantTurnBooks`); the canary is fingerprinted and a changed
  one is `tree-changed`; a cancellation while the scratch is made is `cancelled`/`deadline`; the sweeper's first temp
  walk waits a pace; a background survey cut short by shutdown is not written.

### Epic 5 — the Consultant tab (extension)

- **E5.0 The contract epic 4 built (read before building).** THIS side: `coai-mcp --consultants` — per caller kind
  the resolved vendor/runtime/model, `cli` (found/version/authSource), the limitation row (standing, text,
  measured-or-sourced, `settingsPath`, `snippet` + `snippetWarning` only on Linux/WSL agy rows), the last answer, the
  CURRENT failure (`ConsultHealth.Current` already applied) with the confinement it ran with, and the check state
  already settled by the server (`checking`, `answered`, `failed`, `unavailable`, `already-checking`, `abandoned`,
  `unreadable`; canary `read` / `denied-by-cli` / `denied-by-cli-unattributed` / `not-attempted`). OTHER side (a WSL
  store read through `coai.alsoWatchDataDirectories`): `consultations/health/consultants.json` (the same answer, with
  its time and side) plus `<kind>.check.json` — a `checking` there is judged by its `heartbeatUtc` against the
  staleness the server publishes in that file (`heartbeatStaleAfterSeconds`; epic 5 adds the field if it is not
  there), never by a hard-coded number in TypeScript and never by a lock probe. `--check-consultant` prints ONE JSON
  document; its `deadlineUtc` already includes the teardown allowance. Epic 5's plan round: the server writes
  `heartbeatStaleAfterSeconds` into `<kind>.check.json` and `consultants.json` in this epic (a small `src_mcp`
  change); a file WITHOUT it (a server from before) is shown honestly as "checking, as of <heartbeat time> — whether it
  is still running cannot be told from here", never as an endless spinner and never judged by a TypeScript constant.
- **E5.T Tests (epic 5's plan round made them explicit here).** Pure: answer and file parsing (absent field = old
  server; `[]` = answered), `checkStateOf` this side / other side (fresh, stale, no staleness field), every failure
  kind and canary reading worded, the sides parser. Host on ports: a refused confirmation runs nothing, a second
  click while checking runs once, a throwing run clears the optimistic flag, 64 → "update the MCP server", the kill
  cap starts static and tightens only from a fresher state file, copy of an unknown id touches no clipboard.
  Page by `runPanel` (bundled, its own script run): one Check per row with its id, the message on click, disabled
  while checking, none in another side's block, the failure for another vendor/model hidden, the old-server sentence,
  `copied` per command with the old `copyPhrase` path intact. Contract (`npm run test:contract`, the built binary):
  `--consultants` returns the four kinds; an old binary answers 64 to `--check-consultant`.
- **E5.1 Pure modules.** `consultantHealth.ts` (types; `parseConsultantsAnswer` — absent field = old server,
  `[]` = answered; `parseHealthFiles`; `checkStateOf`: this side → the server's settled state as given; other side
  → the heartbeat rule above; failure wording covering every kind of `shared/consult-failure-kinds.json`; canary
  wording that says plainly only `denied-by-cli` is observed confinement), `consultantSides.ts` (this
  side via `sideLabel`; other sides from `alsoWatchDataDirectories`: `\\wsl.localhost\<d>\…`,
  `\\wsl$\<d>\…`, `/mnt/<x>/…`), `consultantHealthRead.ts` (a directory's `consultations/health/*.json`
  through ports; `undefined` = keep the last snapshot).
- **E5.2 The row's health block.** `consultantHealthView.ts` (markup + CSS: CLI + auth source,
  limitations, last failure — hidden when the row now resolves to another vendor/model — check line and
  button; escaped; semantic; another side's block read-only with its label and a Remote-WSL pointer, the
  evidence path shown as that side's path). `consultantView.ts` widens `ConsultantRowView` with `health`
  and renders the block after the hints.
- **E5.3 Check and copy.** `consultantCheckRun.ts` (`serverRun`; exit 0 + JSON → reported, 64 → too-old, 65 →
  refused, else crashed; epic 5's plan round: the process STARTS with a static generous kill cap (no file is read
  at launch — a previous check's `<kind>.check.json` carries an expired deadline, and on a first run there is none),
  and the cap is tightened to `deadlineUtc` + 30 s only from a state file whose `startedUtc` is newer than the
  spawn; an early exit (64, 65) ends the run at once whatever the cap);
  `consultantHealthHost.ts` on ports (modal paid-call confirmation naming the vendor, the scratch folder and
  the vendor-side session it leaves → optimistic flag → `void` run → refresh; copy of the Linux/WSL snippet
  through `textCopier`). `panelView.ts`: `checkConsultant`, `copyConsultantSnippet` in `PANEL_COMMANDS`; the
  `copied` handler generalised by `command` (old `copyPhrase` path kept).
- **E5.4 Probe, watcher, wiring.** `consultantsProbe.ts` (spawns `--consultants`, cached like
  `providerHealth`, never awaited by render); `consultantHealthWatcher.ts` (own side fs-watch + 5 s poll of
  every side); `panelProvider.ts` wiring only.
- **E5.5 Docs and the live acceptance.** `research/module_runners.md`, `module_server.md`,
  `module_extension.md`, `module_tests.md`, `architecture.md`; `POST_DEPLOY.md` question asked. Live, paid,
  on Windows AND WSL: a forced-`run_command` consultation through the real consult path (requirement 1);
  Check for antigravity and claude (codex if its quota is back — it is out until 2026-10-08); results in
  the plan's deviations.
- **E5 as built (2026-10-03) — deviations.** (1) `heartbeatStaleAfterSeconds` is a COMPUTED property on
  `ConsultCheckRecord` and `ConsultantsAnswer` (`ConsultCheckState.HeartbeatStaleAfter`, 60), so a record rewritten by a
  newer build carries that build's margin; `HeartbeatStaleAfterIsPublishedTests`. (2) Two pure modules beyond the four the
  plan named: `consultantHealthState.ts` (what each row shows, decided — the `consultantRowView` split) and
  `consultantHealthPanel.ts` (the `vscode` composition, so `panelProvider.ts` holds wiring only; in the Sonar coverage
  exclusions). (3) The paid-call confirmation goes through `notifyAndAsk` (`paid-consultant-check`, modal), not a direct
  `showWarningMessage` — the notification census refuses a direct call; its population moved 141 → 142. (4) A 65's
  sentence is shown only when stdout carries one: the server writes it to stderr, which `capture` does not collect, so the
  panel otherwise names the exit code. (5) The contract half runs in `npm test` against the BUILT `coai-mcp`
  (`consultantsLiveContract.test.ts`, skipping when unbuilt, the `bugzLiveContract` shape) — `npm run test:contract`
  drives the Team server, not `coai-mcp`. There is no OLD binary to ask, so "an old binary answers 64 to
  `--check-consultant`" is proved by its two runnable halves: an unknown mode exits 64 → too old, and the mode refuses a
  bad request with 65 → refused. (6) The page harness learned a button's drawn text (`textContent`) and the
  `[data-command="name"]` selector, each with its own test. (7) The probe re-asks a too-old server at the TTL rather than
  never again (`CadenceProbes` stops): a server updated inside the window is then seen. (8) Own review fixes (the coai
  gate's reviewers were out of quota; the operator proceeded on the coordinator's reviewers): THIS side's watcher
  signature is built from the CHECK files only — every `--consultants` rewrites `consultants.json`, so counting it
  re-asked the probe in an endless loop (Blocking); a check of the consultant a row used to name is labelled; a
  `checking` on disk counts only until a later server answer or this window's run of it ending, else it is judged by
  its published heartbeat; an `already-checking` press is a note, never a candidate; the watcher pauses when the
  Settings tab closes.
- **E5.5 as run (2026-10-03) — deviations.** Recorded in
  [RESULTS_consultant_live_acceptance.md](RESULTS_consultant_live_acceptance.md). `--consultants` ran
  on Windows and WSL (the allow snippet on WSL rows only). Check: Windows claude 2.1.258 `restricted` and WSL claude
  2.1.197 `no-restricted` both answered with the canary refused by the CLI (`denied-by-cli`); Windows antigravity was
  classified `quota` with its cure. NOT run: the forced-`run_command` consultation through the real consult path —
  the account's antigravity weekly quota was spent (≈ 87 h to reset), so requirement 1 rests on the direct agy
  measurement (6/6, `RESULTS_agy_consult_follow_up.md`) and the scenario tests on the recorded stream; it is owed
  once the quota resets. codex Check not run (rate-limited until 2026-10-08). A WSL antigravity Check was not run for
  the same quota.
- **Whole-branch review (2026-10-03) — deviations.** Four own reviewers stood in for the coai gate, whose vendors were
  out of quota; every accepted finding was fixed with a RED test first (or a test proved to have teeth). (A) Health
  reaches the tab live: every `ConsultHealthStore` write also rewrites THAT caller kind's `lastAnswer` / `lastFailure` /
  `failureCurrent` / `healthUnreadable` in `consultants.json` (`ConsultantsFile.Refresh`, no re-probe, nothing done when
  the file is absent or unreadable), and a survey re-reads every row's health files immediately before its own atomic
  write (`ConsultantsFile.Written`) — the residual is the milliseconds between that read and the move, stated in the
  type's header; THIS side's watcher signature now carries the `<kind>.answer.json` / `<kind>.failure.json` bytes (the
  probe never writes them, so no loop); `failureCurrent` is decided PER CONSULTANT — the failure's vendor + model must
  be what the row names, and only a later answer by that same consultant clears it (`ConsultHealth.Current`), with the
  panel's own vendor comparison removed (a row whose facts are about another consultant than the panel names is still
  hidden behind its notice); `LastAnswer` / `LastFailure` are a `HealthOnDisk` union (none / found / unreadable, read
  through `SharedRead`), and an unreadable file reaches the row as `healthUnreadable` and the tab as a notice, never as
  healthy. Residual: the answer file keeps the LAST answer only, so a recovery followed by another consultant's answer
  is not seen until the failing one answers again. (B) `--consultants` probes every consultant with no vault key — a
  consultation sends none — and no longer reads the vault. (C) `UsageLedger.RecordJob(…, Usage, …)` carries cached,
  reasoning, no-price and not-captured into consult and consult-check rows. (D) A failed turn's notice subject is
  `consult:<kind>`, not the compiler-filled `Applied`. (E) A refusal before the launch leaves an existing consultation's
  record as it was and writes none for a new one (answer, health file and give-back kept). (F) Each adapter maps its own
  denials (`IConsultantRuntime.SilentFailure`): a claude envelope refusing `Read`/`Glob`/`Grep` is `read-denied`; the
  survey asks the adapter which executable decides the argv (`CapabilityExecutable`), removing the survey's copy of
  `ClaudeConsultant.Executable`. The `runtime == antigravity` branch in `ConsultantsReport` stayed: the limitations
  file cannot express it (agy's macos row carries no settings path, yet the row must still say where agy reads). (G)
  The failure application moved to `ConsultationFailedTurns.cs`; `ConsultationService.cs` is 928 lines (merge base 1031).
  (H) `ConsultHealthPaths` is the one copy of the health and answers directories and the 500 ms sharing retry. (I)
  `ConsultantFacts` became `SurveyedConsultant` (unresolved / refused / probed) + `CliCapability` + `ConsultantOutcomes`;
  the JSON shape only gained `healthUnreadable`. (J) `shared/consult-check-words.json` holds the check states and canary
  readings, held by a C# reflection test and a TS test. (K) The `--check-consultant` help names all four readings and
  exit 74. (L) A `--help` cut short is `Unknown` (fail closed); a claude that cannot be started is refused as
  `cli-not-found` with the install cure. (M) The timeout cure names *Reviewer timeout, minutes* on the Limits tab; a
  denied command or an empty answer tells the caller a NEW consult naming the files is the move, never "do not retry".
  (N) The probe ignores a moved `utc` when deciding to repaint; the health is computed and watched only while the
  Settings tab is visible AND holds the Consultant tab (paused on hide, `onDidChangeViewState`); the watcher's first read
  does not re-ask `--consultants`. (O) Another side's `checking` is judged by THIS window's elapsed time since it first
  saw the heartbeat's current value (`withBeatsSeen`), never this clock minus the WSL stamp; the server's one-shot modes
  cannot observe a beat move, so `ConsultCheckState.SettledAcross` still compares clocks — the residual skew, stated in
  its remarks. (P) The side label is an `h4`; the false `aria-live` region is gone, and a landed Check is announced
  through the notification funnel (`consultant-check-landed`; census 142 → 143). (Q) §6's check bullets describe the
  held lock that was built and tested.
- **Rebased onto main (2026-10-03) — deviations.** Main gained 86 commits while this branch was open, the question
  consultant and the security lane among them. (1) **Schema step 19, not 17**: main's 17 (`TheQuestionsAsked`) and 18
  (`SecurityEvidence`) stay; `WhyAConsultationFailed` is appended as 19, pinned by position
  (`ConsultationFailureColumnsTests.TheFailureColumns_AreStep19`). (2) **`ConsultantPreviewFork`**, the sibling of
  main's `SecurityPreviewFork`: this branch's preview builds stamped 17 for the failure columns, so such a file — at 17,
  or at 18 after a released main build added its evidence over it — has `failure_kind` and no `question_consults`;
  before the migrator, in one IMMEDIATE transaction, it gains the question tables (`IF NOT EXISTS`), the evidence column
  only if absent, and is stamped 19. It runs before the security fork (their shapes are disjoint, and a repaired file
  stands at 19). `AConsultantPreviewDatabaseKeepsEveryStepTests`: red without the fork (`Expected opened not to be
  <null> because a database the consultant preview wrote must still open, not fail step 19 on a duplicate column`),
  green with it; its control (main's database at 18 gains 19) passed throughout. The branch only ever numbered this
  step 17. The machine's real stores were read read-only at the rebase: neither carries the preview shape. (3) §7's
  boundary row done: `ConfinementPlannerTests.TheShippedConsultants_StillBuildTodaysArgv_ByteForByte` takes the new
  claude argv (prepared, `--restricted --tools Read,Glob,Grep`), and `ReadTools` is now
  `ConfinementPlanner.ClaudeDiskTools` — one list. (4) `IConsultantRuntime` now derives from main's
  `IAnsweringRuntime`; `PrepareAsync` and everything after `UsageIsCumulative` stay on the conversation interface, and
  only the AS-SHIPPED claude launch needs the prepared capability — a question row's planned launch is unchanged.
  (5) Main's `ConsultCallCounter.Peek` (the question consultant's gate) read an unreadable counter as an empty window;
  it now holds the caller at its cap as `TryTake` does, and the question gate says the counter's sentence. (6) This
  branch's `RowOutcomes` record in `ConsultantsReport.cs` shadowed the core's `RowOutcomes` word list inside
  `CoaiMcp.Server`; renamed `ConsultantRowOutcomes`. (7) The notice census is main's 142 plus this branch's two: 144.

### Growth surfaces

| surface | size | retired by | interrupted |
|---|---|---|---|
| `consultations/health/<kind>.{answer,failure}.json` | 4 caller kinds × 2 × ~2 KB | overwritten (newer only) | atomic write |
| `consultations/health/<kind>.check.{json,lock}` | 4 × ~2 KB, 4 empty locks | overwritten; locks reused | on this side a free lock → `abandoned` (every reader, and the sweep rewrites it); across the Windows/WSL seam a heartbeat silent past four beats → `abandoned` (readers only — the sweep never settles another side's record) |
| `consultations/health/consultants.json` | one, ~2–6 KB | overwritten by every `--consultants` and every server start, never by a start cut short | atomic write |
| consultation evidence (`unparseable/consultations/*`, a check's as `check-<vendor>-*`) | capped 64 KB each (was unbounded) | the sweeper removes everything in `unparseable/consultations/` older than 30 days (the rest of `unparseable/` still has no retention, `module_runners.md:615`) | atomic |
| `consultations/health/*.tmp` | transient | the sweeper removes any older than an hour | — |
| scratch `coai-check-*` | one small repo + canary per check | deleted at the end; swept > 1 day at each check and sweeper tick | swept |
| three columns on `consultations` | ≤ ~300 B per failed row | **kept forever with the table** (it has no stated retention); projected < 1 MB per 3 000 failed turns | — |
| ledger | one row per check | the ledger's existing retention | — |
| vendor's own session store | one session per check | the vendor's own store (named in the confirmation) | — |

## 6. Test plan

**Server (xUnit v3, `src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`)**
- `ConsultAntigravityDenialScenarioTests` (fake CLI, real stream fixture): continued-and-answers (RED
  first); denied-again → `command-denied`, exactly two launches; a `read_file` denial → `read-denied` with
  its own wording; tree changed by the first launch → breach, no follow-up; second launch hangs → killed by
  the launcher within the remaining budget, handle kept, not a `deadline`; billed once when cumulative and
  the first's tokens kept when the second times out; a vendor-failed turn gives the call back, and the
  fourth such turn in a window does not; the turn's tokens are in the ledger after a give-back.
- `AntigravityStreamContinueTests` (one conversation flag on a resumed argv); `DeniedActions` (spaced JSON;
  `auto-denied` in stdout ignored, in stderr matched).
- `ConsultFailureTests` (outcome table; agy quota stream → `quota`); parity with
  `shared/consult-failure-kinds.json`; `VendorDiagnosis.Classify`; `ConsultationUsageTests` (max, sum,
  `Usage.None` second).
- `ConsultCallCounterTests`: give-back against its own take, not after rollover, not across stores, capped.
- `ConsultantPromptTests` (agy `Toolbox` names exactly the observed tools and not the follow-up marker).
- Claude golden argv + the two rewritten tests; `AReviewerStartsNoMcpServersTests` green.
- `ConsultantLimitationsTests` (by `HostKind`; every row measured or says not; claude rows cite E3.1).
- `AntigravityOnLinuxTests` (`~/.local/bin/agy` fallback).
- Schema round-trip of the three columns; `RoundsQuery` reads all five shapes; `Answered` clears the
  failure; health files: newer-only write, cleared failure after a later answer.
- One-shot modes as real processes: `--consultants` (4 rows, a definition row probed through the fake CLI,
  auth source not "signed in", agy path per platform, 74 on an unreadable dir); `--check-consultant`
  (answered + marker read + canary not read; no `consultations/*.json`; counter untouched; ledger row;
  scratch deleted; an empty HOME with no git identity still commits; two concurrent checks → exactly one launch,
  the other `already-checking` because its exclusive open of the held `<kind>.check.lock` failed; a holder killed
  mid-check → the kernel released its lock, the next reader settles its `checking` as `abandoned` and a new check
  runs; a lock file nobody can open → 74, never `already-checking`; a fresh `checking` from the other side → `already-checking`
  and nothing launches; timeout → `timeout` with the tree killed; missing `--caller` → 65);
  `ARequestFaultIsNotAnOldBinaryTests` green.

**Extension (`npm test`, hermetic; page tests RUN the page)**
- Pure: answer and health parsing (absent = old server), `checkStateOf`, sides, wording for every kind.
- Host on ports: refused confirmation runs nothing; a second click while checking runs once; a throwing run
  clears the optimistic flag; 64 → "update the MCP server"; copy of an unknown id touches no clipboard.
- Page (`runPanel`): one Check per row with its id; the message on click; disabled while checking; none in
  another side's block; a failure for another vendor/model hidden; `copied` per command; the old
  `copyPhrase` path still works.
- Contract (`npm run test:contract`, the built binary): an old binary answers 64 to `--check-consultant`;
  `--consultants` returns the four caller kinds.

**Live (manual, paid)** — E1.1, E3.1 and E5.5 above, each recorded in `research/`.

## 7. Boundaries with other plans

| item | this plan | other plan |
|---|---|---|
| moving the EXISTING consultant's claude launch off the deny-list | **builds it** (E3) | `PLAN_question_consultant.md` §9 (branch `feat/question-consultant`, unmerged, another session's worktree) lists it as a tails item — DONE here; that branch's `ConfinementPlannerTests.TheShippedConsultants_StillBuildTodaysArgv_ByteForByte` must take the new argv when it merges second, and `ReadTools` → `ConfinementPlanner.ClaudeDiskTools`. The mirror row is to be written into that plan when it is next touched (it cannot be edited from here) |
| the reviewer's claude deny-list (`ClaudeRuntime.ReachTools`) | not built (operator decision) | [PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md](../todo/PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md); also listed in the question-consultant plan's tails |
| a capability matrix (`shared/runtime-capabilities.json`) | not built; `consultant-limitations.json` shares its `standing` words and adds a platform | that branch; unify when both are on main |
| schema step number | appended after whatever main has — **19** at the rebase (`ConsultantPreviewFork` for the preview's 17) | that branch uses 17; the security lane 18 |
| Check of the WSL side from a Windows window | not built | [PLAN_a_wsl_consultant_is_checked_from_windows.md](../todo/PLAN_a_wsl_consultant_is_checked_from_windows.md) |
| the reviewer's #504 follow-up billing the first launch twice | not built | [PLAN_the_reviewer_follow_up_is_billed_once.md](../todo/PLAN_the_reviewer_follow_up_is_billed_once.md) |
| splitting `ConsultationService.cs` / `Program.cs` | not built | [PLAN_consultation_service_and_program_under_800_lines.md](../todo/PLAN_consultation_service_and_program_under_800_lines.md) |
| a live liveness probe for REVIEWERS | not built; the consultant Check is the first live probe | [PLAN_provider_liveness.md](../todo/PLAN_provider_liveness.md) — may reuse `ConsultantTurn` |
| retention for `unparseable/` | not built | recorded at `module_runners.md:615` |

The order: this plan's E3 should land before the question-consultant branch merges, because that branch's
byte-for-byte argv test is the one that has to move.

## 8. Definition of Done

- [x] P0 measured on Windows and WSL, `research/RESULTS_agy_allow_rule.md`.
- [x] E1.1 and E3.1 measured and recorded before the code they govern.
- [x] The RED scenario test observed failing with the real symptom, then passing.
- [x] Items 1–13 of §2 each have a test or a recorded live observation (item 1 by the direct agy measurement and the
      scenario tests — its product-path live run is owed, see the status line).
- [x] Reviewer behaviour unchanged (#504 tests green, byte-identical reviewer argv).
- [x] Both one-shot modes in `.agents/PROJECT.md`; a bad request answers 65, never 64.
- [x] Whole C# suite and all extension suites green in the shipping configuration; a clean `tsc`.
- [x] Module docs, `architecture.md`, `module_tests.md` updated; Mermaid renders.
- [x] Coai gate per epic: plan round and code round `proceed`; own reviewers in parallel; cadence
      consultations taken or their failure stated — except epic 5's code round and the whole-branch review, run by own
      reviewers on the operator's decision while every coai vendor was out of quota.
- [ ] PR merged with CI green and every thread resolved; release cut after the operator's OK;
      `POST_DEPLOY.md` run against the installation.
- [x] Plan promoted with its deviations.

## 9. What this plan does not do

- Check the WSL side from a plain Windows window (`wsl.exe`) — its own plan.
- Fix the reviewer's probable double billing on the #504 path — its own plan, measure first.
- Split the oversized files — its own plan.
- Give the rest of `unparseable/` a retention (only `consult-*` evidence gets one here).
- Write agy's settings file. Ever.
