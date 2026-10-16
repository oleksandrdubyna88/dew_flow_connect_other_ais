# PLAN — The plan gate reads the committed plan itself

> Status: **E1 merged as #837 on `main`, 2026-10-16; E2 coded and tested on the branch `feat/plan-gate-e2`, 2026-10-16, not merged — the plan gate `proceed`ed and the plan was split into two epics on 2026-10-15
> (§ Epics), after the question consultant was asked.** Scope: `review_plan` (`src_mcp/src/Tools.cs`,
> `PanelService.ReviewPlanAsync`), one widening of `GitHistory`, a new committed-plan reader, the round record (session
> file + one `rounds` column), the plan prompt's end line, and one display line in the extension's rounds log.
>
> Related: [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md) (§ *A field added to the round list*),
> [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md) (schema steps 22/23).

## 1. The symptom (measured 2026-10-15)

The plan gate reviews exactly one string. `ReviewPlanAsync` (`src_mcp/src/Server/PanelService.cs:612-699`) runs with
`NeedsWorktree: false … ReadsCheckout: false` (`PanelService.cs:635`), and the reviewer's whole view of the plan is
`$"## The plan under review\n\n{planText}"` (`PanelService.cs:677`) in an empty scratch directory. Epic documents here
are 60–100 KB. On one day:

1. A calling agent's verbatim copy of such a document into `planText` was cut by a safety classifier **three times**;
   each round went out truncated and burned `maxRounds: 1`. Nobody can send ~70 KB+ verbatim reliably.
2. A path-only `planText` produced a **hollow `proceed` with 0 findings** while both reviewers wrote that the body was
   missing. Nothing in the gate could tell a reviewer that saw nothing from one that saw a sound plan.
3. The `review_plan` description still says a reviewer reads `planText` "plus a read-only checkout"
   (`src_mcp/src/Tools.cs:90`). It does not, and has not since the plan stage lost its worktree.

## 2. The goal — what must be true when this is done

1. A caller can name a **committed** plan file instead of pasting it; the server reads it from git's object database at
   the one commit the round runs on, and every reviewer receives it whole.
2. The text never transits the calling agent, so no classifier on the caller's side can cut it.
3. Every way the read could be wrong — dirty file, wrong object type, partial capture, bad encoding, too large — is a
   **refusal before any round is consumed**, in a sentence that names the cure.
4. The round records what was reviewed: commit, canonical path, blob id, byte count, and the id of the text sent;
   `status` and `--log` show it.
5. A plan reviewer that says the plan did not reach it whole is **not counted as answering** — it cannot help a round
   pass (option A's mandate, verified with the question consultant).
6. `planText` keeps working exactly as today for every caller that sends it.

## 3. Decisions (each checked against the code)

| # | Decision | Why — and what was checked |
|---|---|---|
| D1 | New optional argument **`committedPlan`** (repo-relative path). Not `plan`, not `planPath`. | `plan` already exists on `review_plan` as the **cadence** key (`Tools.cs:80-83`, parsed at `Server/Cadence/CadenceDesk.cs:143-147`) and legitimately names a different file than the one reviewed — an epic's stories document is reviewed while `plan` names its parent. Giving it a second meaning silently would conflate the two. `planPath` is `review_feature`'s name for a **working-tree** read (`Server/Stages/FeaturePlan.cs:58`); one name with two sources is a trap. `committedPlan` says the source in its name. No mode string (`planFrom`) — a mode plus a path is two arguments for one fact. |
| D2 | `planText` and `committedPlan` are **mutually exclusive**; neither is refused. | The `DocumentReader` precedent refuses both (`Server/DocumentReader.cs:63-66`). Both checks live in `ReviewPlanAsync`; the engine's "planText is required" (`Server/Rounds/RoundEngine.cs:213-217`) stays as it is, because `review_code` and `review_document` share its sentence through `from`, and `committedPlan` means nothing to them. **Never** "read the file when `planText` looks like a pointer": a guess about intent is not an input. |
| D3 | **One commit, pinned once.** `ReviewPlanAsync` resolves the branch tip with `WorktreeManager.ResolveShaAsync` (`runners/Worktrees/WorktreeManager.cs:91-97`), reads the plan at that sha, and passes it as `StageRun.Head` (`Server/Rounds/StageRun.cs:99`). | The engine resolves `stage.Head` when set (`RoundEngine.cs:304`), and `rev-parse --verify <sha>^{commit}` returns the same sha — so the plan, the cadence outline read under the claim (`CadenceDesk.cs:355-362`) and the round record are one commit even if the branch moves mid-call. Precedent: the feature stage pins its head the same way (`PanelService.cs:1498`). `ResolveShaAsync` THROWS `WorktreeException`, and in `ReviewPlanAsync` that call sits outside the engine's `try`; it is caught there and answered through `Error(...)`, as `OpenAsync` does (`PanelService.cs:482-490`). `ReviewPlanAsync` (`:618`, today a synchronous `Task` return) becomes `async`. |
| D4 | Optional **`expectedCommit`** (40 hex). Malformed → refused; tip ≠ expected → refused, naming both. | A caller that committed and then had a peer commit land on the branch would otherwise review a plan it never saw. Shape check is `GitHistory.IsCommitish` (`runners/Collecting/GitHistory.cs:72`). |
| D5 | Read **by blob id**: `git --literal-pathspecs ls-tree -l -z --full-tree <sha> -- <path>`, then `git cat-file blob <oid>`. Not `git show sha:path`. | One `ls-tree` line gives mode, type, oid and size, so symlink (`120000`), submodule (`160000 commit`), directory (`040000 tree`) and the size ceiling are refused **without reading a byte**; the bytes read are then those of the oid that gets recorded. `--literal-pathspecs` stops `*`/`?` in a name from globbing; `-z` stops quoting. The `-l` size column is left-padded with spaces and is `-` for a non-blob (a submodule); the parser takes both, and its tests carry both. Path rules reuse `GitHistory.IsRepoRelative` (`GitHistory.cs:93`: empty, NUL, rooted, drive, `..` refused); `\` → `/` first, as `CadenceDesk.cs:143` does; a trailing `/` is refused. `CommittedFile.ReadAsync` (`runners/Collecting/CommittedFile.cs:31`) was checked and is not reused for the read: it answers "not in commit" vs "git failed" but cannot see the entry's type or size. |
| D6 | **A partial capture is refused.** `GitAnswer` gains an additive `Truncated` (default false); `GitHistory.RunAsync` sets it from `ProcessResult.Truncated`. | `RunAsync` (`GitHistory.cs:264-274`) checks `TimedOut`/`Cancelled` only (`:271`), while the launcher cuts a stream at its ceiling or drain grace and says so only in `ProcessResult.Truncated` (`runners/Processes/ProcessLauncher.cs:127-139`). The new reader refuses a truncated read. Existing callers keep today's behaviour — see §8. |
| D7 | **The capture is proved whole**: the reader recomputes git's blob id (SHA-1 of `blob <n>\0` + bytes) over the UTF-8 bytes of the decoded text and requires it to equal the oid. **The id comparison decides; nothing else does.** | stdout is decoded with lossy `Encoding.UTF8` (`ProcessLauncher.cs:227`), so invalid bytes become U+FFFD silently; the strict decoder the document reader uses (`DocumentReader.cs:46`) cannot be applied to a captured string. The id check catches a lossy decode, a truncation the flag missed, and a stripped BOM in one comparison — and it ACCEPTS a valid plan that contains a literal U+FFFD, because valid UTF-8 re-encodes to the same bytes (plan round 1, gemini). Order: id matches → accept; id matches over `EF BB BF` + bytes → accept (the BOM is dropped, recorded bytes are the blob's); otherwise refuse, worded "not UTF-8" when the text holds U+FFFD and "the capture differs from the blob" when it does not. SHA-1 only: `IsCommitish` already accepts 40 hex and nothing else. |
| D8 | **Uncommitted or staged changes to the path are refused**, via `git --no-optional-locks --literal-pathspecs status --porcelain=v1 -z --untracked-files=no -- <path>`, run from the toplevel — **only when the checkout's HEAD is the pinned sha**. | `git status` compares through the clean filters, so a CRLF working copy of an LF blob under `core.autocrlf` is clean — a byte comparison of the working file would refuse it wrongly. `--no-optional-locks` keeps it from taking `index.lock` in a tree other sessions share. One `git rev-parse --show-toplevel HEAD` answers both "where is the root" and "is HEAD the pinned sha". When HEAD is another commit the working tree says nothing about the branch, so nothing is compared and the reply says so in one line. A checked-out branch IS its HEAD, so "branch ahead of a dirty HEAD" cannot occur in one worktree; a caller reviewing a branch it has not checked out here, and wanting to assert what it reviews, passes `expectedCommit` (D4). Considered and rejected in plan round 1. Two known blind spots, said rather than fixed: a path marked `skip-worktree` / `assume-unchanged` (sparse checkouts) reads as clean, and a branch checked out in ANOTHER worktree than `repoPath` is "not compared" — true, since that tree is not this one. |
| D9 | **Refusals, all before `RunStageAsync`** (so before the claim, the begin and any round number): both/neither input; credential-shaped name (`CredentialFiles.WhichPattern`, `core/Feature/CredentialFiles.cs:31`, as `FeaturePlan.cs:37`); bad path shape; branch unresolvable; `expectedCommit` malformed or stale; path not in the tree; symlink / submodule / directory; empty or whitespace-only; over the ceiling; an LFS pointer (text starts `version https://git-lfs.github.com/spec/v1`); truncated; not UTF-8 / capture mismatch; dirty or staged. | Ceiling = `DocumentRules.MaxBytes` (256 KiB, `core/Rounds/DocumentRules.cs:31`), the existing bound; the reason it is the bound is `DocumentRules.cs:22-29`. Refusals go through `PanelService.Error` → `Refusal.Answer` (`PanelService.cs:1847`), the same recorded path the engine's use. |
| D10 | **What the round records.** A `PlanSource` record — `Origin` (`commit`/`text`), `Commit`, `Path`, `Blob`, `Bytes`, `TextId` — on `RoundRecord` in the session file, nullable and not normalised, like `RoundRecord.Caller` (`Server/SessionStore.cs:193`); `status` serialises `RoundRecord`s (`Server/ServerJsonContext.cs:143-151`), so it shows there with no new member. A text round records `Origin: text`, `Commit` (the sha), `Bytes`, `TextId` — "what was reviewed" for every plan round. `TextId` = `DocumentRules.ArtifactIdOf` (`DocumentRules.cs:117`). **How it reaches the record:** a committed read hands its `PlanSource` to the engine on a new `StageRun.PlanSource` member; the engine stamps it beside `Sha`/`CadenceNote` where the record is built (`RoundEngine.cs:483-490`), filling `Commit` from its own `sha` — so a text round, which `ReviewPlanAsync` never resolves, gets its commit there too. Plan rounds keep `RecordsSha: false` (`core/Rounds/Stages.cs:75`), so `RoundRecord.Sha` stays empty and `PlanSource.Commit` is the session-file copy; the DB's `head_sha` already gets `sha` (`RoundEngine.cs:615`). A SKIPPED round (`SkippedOrOwed`, `RoundEngine.cs:688`) reviewed nothing and gets no `PlanSource`. | Absent ≠ empty, exactly as `Caller`'s remark argues: a round written before the field said nothing. |
| D11 | **One schema step**: `ALTER TABLE rounds ADD COLUMN plan_source TEXT NOT NULL DEFAULT ''` (JSON of D10; `''` = not recorded). `LoggedRound` (`Store/RoundsQuery.cs:27`) gains a trailing `PlanSource? PlanSource = null`, read only when `HasColumn(db, "rounds", "plan_source")` (`RoundsQuery.cs:895`). | The session file + existing columns were checked and do **not** suffice for `--log`: `head_sha` holds the commit and `plan_text` the text (`Store/RoundsDb.cs:847-882`), so bytes, `TextId` and even the blob id are functions of `plan_text` — but **the path is in no column**, and nothing distinguishes a committed read from a pasted one (`subject` is a heading or a file name, `core/Rounds/RoundSubject.cs`). **Step number:** main is at step 21 (`TheRoundsKey`, `Store/Schema.cs:603-616`) — array index 20, since the migrator runs `steps[version]` and then sets `user_version = version + 1` (`storage/SqliteMigrator.cs:89-90`), so step *n* is index *n−1* and `user_version` *n*. The size epic claims step 22 and the authors epic step 23 (parent plan §11). Because position IS version, the number is fixed **at merge**: the next free step then — step 22 (index 21) if this lands first, step 24 (index 23) if both land first. Whichever PR merges second renumbers before merging; no build of an unmerged branch is pointed at a real data directory. |
| D12 | **The incomplete-plan sentinel — a detector, not a proof.** The composed plan section gains a source line and a final line `END OF PLAN <TextId>`; the instruction before it says: if that line is not visible, or the section holds no plan body (only a path or a pointer), answer with **exactly one** finding whose title is exactly `PLAN_INCOMPLETE <TextId>` and nothing else. For `Stage.PlanReview`, (original rule, superseded by the amendment further down in this row:) an `Ok` outcome whose findings are exactly that one finding (title trimmed, compared ignoring case) becomes `ReviewerOutcome.Unparseable(reason, usage)` (`runners/Reviewers/ReviewerExecutor.cs:201`). The fold **rebinds `results` itself**, right after `RunAllAsync` returns and before `ReviewerSummaryFactory.From` (`RoundEngine.cs:434`), because everything after it reads `results` — `SecurityRound.Merge` (`:445`, which keeps only `Ok`), `DecisionSummary` (`:451`), `Notice`, the warning clauses, `live.Finish` (`:483`) and `SpentPrompts` — so the dropped finding reaches none of them and the lens is not counted as spent. Accepted mismatch, written down: the progress callback (`:409-424`) has already shown that reviewer as finished in the live view, the audit line and the ledger. The ledger row is right as it stands (the tokens were spent); the live view ends on `live.Finish`'s folded state. **Amended 2026-10-16 (cadence consultation, owner's decision — fail closed):** ANY finding whose trimmed title equals the token for THIS round's id folds the reviewer, whatever else it answered — two identical tokens, or the token beside real findings, are a failed reviewer, never an answer. The original "exactly one" rule let two identical tokens stay an `Ok`, count as answered and, under the plan threshold, carry a round to `proceed` with no review; plan round 1's "a substantive review is never discarded over a title collision" is superseded because a reviewer that says the plan did not reach it did not review the plan. The reason names how many other findings were set aside (`…(PLAN_INCOMPLETE); 3 other findings set aside`); a token for ANOTHER round's id is an ordinary finding. The fold keeps the outcome's whole accounting (usage, `EarlierLaunches`, `EarlierTurns`, `Launches`, warning, CLI version), so a retried reviewer stays billed. The prompt also marks the START: a `BEGIN OF PLAN <TextId>` line right under the heading, and a one-line reminder just before the end line — the reviewer must see both markers, else answer the token. | What it detects is the two measured failures — a cut tail and a pointer-only body — and a reviewer's own statement that it saw no plan. It does not prove a reviewer read every middle section; nothing on this side can attest to what a model attended to (plan round 1, codex). When it fires it is a failure, never an answer: zero answered → `call_human` (`core/Rounds/RoundMachine.cs:404`); some answered → the reviewer line names it. `Unparseable` is reused rather than a new variant because it already means "an answer the gate cannot use" and keeps the usage; a new variant would touch every exhaustive switch for one sentence. The sentinel rides **in the prompt** with the existing finding schema (`core/Findings/FindingSchema.cs`), so an older Team server — which receives the prompt from this side — needs nothing. `TextId` cannot be forged by a plan (it would have to contain its own hash). It covers `planText` rounds too: that is where the hollow pass happened. |
| D13 | **No token preflight.** Per-model context capacity is not data here: `DocumentRules.cs:26-29` says so, and the one configured token number, the security lane's `ContextTokens` (`Server/SecurityLaneSetting.cs:7`), is an operator setting for that lane, not a model capability. The bound stays the 256 KiB ceiling; D12 is what happens when a reviewer still did not get it whole. | A check against a guessed number would refuse valid rounds and miss invalid ones (`DocumentRules.cs:27-29`). |
| D14 | Prompts keep travelling on stdin. | `StdIn = prompt` at `runners/Reviewers/ReviewerRuntime.cs:638` and `:821`, `ClaudeRuntime.cs:108` — unchanged. |
| D15 | The tool description is updated; the server instructions are not. | The 2 KiB budget binds `Program.Instructions` only (`tests/TheInstructionsFitWhatAClientKeepsTests.cs`, whose remark sends detail INTO tool descriptions because their budget is separate). The new text: `committedPlan` / `expectedCommit`, the refusals in one sentence, the end-line rule, and the false "plus a read-only checkout" (`Tools.cs:90`) corrected. |
| D16 | Extension: **display only**, no new action, so no new button. | Nothing in `src_vs_code` calls `review_plan` or shows `planText`; the rounds log shows `subject` (`src_vs_code/src/roundsLog.ts:501`) from the session file read in `rounds.ts`. It gains one detail line from `planSource` — `todo/PLAN_x.md @ 1a2b3c4 · blob 9f8e7d6 · 98 304 bytes` — and shows nothing when the field is absent (older server). |

## 4. The read, in order (one `CommittedPlanReader.ReadAsync(git, repoPath, sha, path, ct)`)

1. Normalise the path (D5); credential shape (D9). No process yet.
2. `rev-parse --show-toplevel HEAD` → root, and whether HEAD is the pinned sha (D8).
3. `ls-tree -l -z --full-tree` → exactly one entry whose path equals the canonical path; mode `100644`/`100755`, type
   `blob`; `0 < size ≤ MaxBytes`. Otherwise the matching refusal.
4. `cat-file blob <oid>` → refuse `!Ran`, `Truncated`, non-zero exit.
5. U+FFFD / blob-id proof (D7); empty-after-trim; LFS pointer.
6. When HEAD is the sha: `status` (D8) → refuse a non-empty answer, naming staged (`X`) or unstaged (`Y`).
7. Return `Accepted(PlanSource, Text)`.

Five git processes, each bounded by `GitHistory`'s 30 s budget (`GitHistory.cs:44`); none writes. The reader lives in
`runners/Collecting/` beside `CommittedFile`; the pure parts — path normalisation, `ls-tree -z` parsing, the blob id,
the LFS test — live in `core/` so they are unit-tested without git.

## 5. The prompt (plan stage only)

```
## The plan under review
BEGIN OF PLAN <TextId>
(read by the server from <path> at commit <sha12>, blob <oid12>, <bytes> bytes)   ← committed reads only

<the text>

Reminder: if the first line under the heading was not `BEGIN OF PLAN <TextId>`, answer … `PLAN_INCOMPLETE <TextId>` …
END OF PLAN <TextId>
```

and, immediately before the heading, the D12 instruction. The `context for review` log line (`PanelService.cs:649-656`)
gains the source and `TextId`. The session keeps the text as `PlanText` (`RoundEngine.cs:597`) — the whole committed
plan — so `review_code`'s reuse of it as scope and the split order's measurement (`RoundEngine.cs:506`) see the real
plan rather than a pointer.

## 6. Build order (one PR, five commits, each green on its own)

1. **`GitHistory` widened** — `GitAnswer.Truncated`; `EntryAtAsync` (ls-tree), `BlobAsync` (cat-file blob),
   `RootAndHeadAsync`, `PathStatusAsync`. Every argument an array element; revision arguments behind `IsCommitish`.
2. **Core helpers + `CommittedPlanReader`** with its refusals (§4).
3. **Wiring**: `Tools.cs` (`planText` becomes optional — `string? planText = null`; `committedPlan`, `expectedCommit`),
   `ReviewPlanAsync` (D2–D4, D9, `Head = sha`), the prompt (§5), the description (D15).
4. **Recording**: `PlanSource` on `RoundRecord` + `ServerJsonContext` registration; the schema step (D11) with the
   projection write and the `HasColumn`-guarded read; the D12 fold.
5. **Extension + docs**: `rounds.ts` type + the rounds-log line; `research/module_server.md`,
   `research/module_extension.md`, and `research/architecture.md` § the `--log` seam (one more field, same rule).

## 7. Test plan

Real git in a temp repo via `tests/TempGitRepo.cs`; reviewers through the existing fake-launcher round tests
(`tests/FakeCliRoundTests.cs`, `tests/EndToEndTests.cs` patterns). Run as the MTP executable, never `dotnet test`.

| # | Scenario | Asserts |
|---|---|---|
| T1 | 100 KB committed plan whose last line is unique | **every** reviewer's prompt contains that last line and `END OF PLAN <TextId>` |
| T2 | committed plan edited in the working tree | refused "uncommitted", no round recorded, session unchanged |
| T3 | edit staged, not committed | refused "staged" |
| T4 | CRLF working copy of an LF blob under `core.autocrlf=true` | **accepted** (the eol case D8 exists for) |
| T5 | `../outside.md`, `/etc/x`, `C:\x`, `a\0b`, `todo/` | refused, no git process started for the shape cases |
| T6 | path is a committed symlink / a submodule / a directory | refused with its own word, before `cat-file` |
| T7 | fake launcher returns exit 0 with `Truncated: true` on `cat-file` | refused "incomplete read" (precedent `tests/ClaudeCapabilityTests.cs:234`) |
| T8 | blob with invalid UTF-8; blob with a BOM | refused "not UTF-8"; BOM accepted, recorded bytes = blob size |
| T9 | empty file; 257 KiB file; an LFS pointer | three refusals; the size one starts no `cat-file` |
| T10 | both `planText` and `committedPlan`; neither | refused, no round |
| T11 | `expectedCommit` malformed; stale | refused, naming tip and expected |
| T12 | branch moves between the read and the engine (fake resolve) | the round's `head_sha` = the read's commit (D3) |
| T13 | a completed committed round | session file `PlanSource` = commit, path, blob, bytes, `TextId`; `status` returns it; `--log` row has it; a DB without the column reads `null` |
| T14 | `planText` round | `PlanSource.Origin = text`, behaviour otherwise byte-identical to today's prompt + the two new lines |
| T15 | one reviewer answers `PLAN_INCOMPLETE`, one answers normally | 1 of 2 answered, the sentinel's finding not merged and its lens not counted as spent; both answer it → `call_human` |
| T16 | checkout HEAD ≠ the branch's sha, file dirty | accepted with the "not compared" line |
| T17 | extension: a round with and without `planSource` | the page, run per `bundledPage.test.ts`, shows the line / shows nothing |
| T18 | a committed `.env` / `secrets/id_rsa`-shaped path (real git, file committed) | refused before any git process and before any round; no reviewer launched (plan round 1, codex) |
| T19 | a valid plan containing a literal U+FFFD; one reviewer answers the token plus two real findings | the plan is accepted (D7) with its literal U+FFFD; the reviewer FAILS (amended D12 — any token for this round's id), its two real findings are set aside and the reason says "2 other findings set aside"; none of the three is merged; a lone reviewer → `call_human` |
| T20 | branch name that does not resolve | refused through `Error(...)`, not a tool exception; no round (D3) |
| T21 | path not in the tree at the pinned commit (exists only in the working tree) | refused "not in commit <sha12>", before `cat-file` |

**Break-its** (mutate production, name the test that must go red, restore, re-run green — reported with the red text):
B1 drop the `Truncated` refusal → T7. B2 read the working file instead of the blob → T2 and T8. B3 accept both
inputs → T10. B4 skip the blob-id proof → T8. B5 drop the `Head = sha` pin → T12. B6 drop the D12 fold → T15.
B7 drop the credential-shape refusal → T18. B8 refuse on U+FFFD before the id check → T19.
B9 compare the working tree even when HEAD ≠ the pinned sha → T16.

## 8. Out of scope — named, not silently left

- `review_code` gains no `committedPlan`: its scope is the session's kept `PlanText`, which is now whole.
- `review_feature`'s `planPath` stays a working-tree read (`FeaturePlan.cs:58`); `review_document` is untouched.
- Existing `GitHistory` readers (`CommittedFile`, the cadence outline, `--file-at`) still ignore `Truncated`; making
  them refuse changes what the review page shows for files past 16 MiB — a separate, smaller plan.
- The family snippet that tells callers "verbatim as `planText`" lives in `dew_flow_conventions`
  (`common/coai-review-gate.md`, step 2) and the extension's `claudeSnippet.ts`; offering `committedPlan` there is a
  conventions change after this ships, and this plan does not depend on it.
- No refusal of a pointer-shaped `planText`: D12 makes such a round fail closed instead of guessing.

## 9. Growth

| Surface | Size at stated volumes | Retired by | Interrupted |
|---|---|---|---|
| `rounds.plan_source` | ≈ 200 B per plan round; at 50 plan rounds/day ≈ 3.6 MB/year | kept forever, as every round row (`Schema.cs:311`: a retention window "would delete the product") | written with the round row; no in-flight state |
| `RoundRecord.PlanSource` | ≈ 250 B per plan round in a session file | the session file's existing owner | written with the round |
| `plan_text` / `PersistedSession.PlanText` | ≤ 256 KiB per plan round — **unchanged class**: a verbatim `planText` is the same text | unchanged | unchanged |

No directory, cache or long-lived process is added; the reader's five git processes end with the call.

## Definition of Done

- [ ] `review_plan` takes `committedPlan` (+ optional `expectedCommit`), exclusive with `planText`; old callers unchanged.
- [ ] The text is read from the blob at the one pinned commit, proved whole by its blob id, and refused on every §3-D9 case before any round is consumed.
- [ ] Each round records `PlanSource`; `status`, `--log` and the rounds log show it; the schema step's number is taken at merge and the other two plans' steps are respected.
- [ ] An answer carrying ANY `PLAN_INCOMPLETE <TextId>` finding for this round's id — alone, repeated, beside real findings, or wrapped in markdown or quotes — counts as a failure and its other findings are set aside (the reason says how many); all-failed → `call_human`; a token for another id is an ordinary finding (amended D12).
- [ ] The tool description is corrected (no checkout) and says how to use the new argument; server instructions still pass their budget test.
- [ ] T1–T21 green; B1–B9 each observed red with the named test, then green.
- [ ] `module_server.md`, `module_extension.md`, `architecture.md` updated; `node .agents/conventions/tools/plan-lifecycle.mjs` clean; this plan promoted.

## Epics

Two epics, each on its own branch and ONE pull request, the coai gate once per epic; cornerstone quota ⌈2·2/5⌉ = 1.
Stories live in `todo/PLAN_the_plan_gate_reads_the_committed_plan_E<n>.md`, one document per epic, written by Opus when
the epic starts (`subagent-models.md`, pipeline step 3) and linked under its epic here in the same commit (the both-sides
rule). Fable named the cornerstone AREA; Opus marks the story within the quota and keeps the ledger. **This split amends
§6:** "one PR, five commits" becomes one PR per epic — E1 is §6's commits 1–3 and the session-file half of commit 4; E2 is
the schema half of commit 4 and commit 5. Every commit still lands green on its own.

*Consultant (gpt-6-astra), 2026-10-15, used for this split, each claim checked against the code:* two epics, not three —
the reader, the refusals, the pin, the sentinel's prompt marker AND its fold, the session-file record, `status`, the tool
description and the server docs are one unit of value (a caller can review a committed plan safely, and the round says what
it reviewed), useful on its own; the column, `--log` and the extension line are exposure of a record that already exists
and ship second. No docs-only epic: the description fix (D15) is part of E1's usability, since a caller learns
`committedPlan` from it. The consultant's fallback (sentinel first, committed file second, reporting third) is kept as the
cut if E1's stories document finds E1 too large: D12 stands alone on `planText` rounds. **One correction to the
consultant's grouping:** the extension's rounds log reads the SESSION FILE, not the database (`roundsLog.ts:20,501` maps
`SessionFile` records), so its display line depends on E1's record only and never on E2's column; it stays in E2 because
it is display, and so that E1 ships nothing in `src_vs_code`. Riskiest: a read that is wrong with no symptom — a lossy
decode, a truncation the flag missed, a BOM — recorded under a blob id it does not match; the area holds exactly that.

Cornerstones: 1 of 1 — E1.S2

### E1 — Review a committed plan safely, and record what was reviewed (§6 steps 1–4) · L · depends on nothing

- **Stories.** [PLAN_the_plan_gate_reads_the_committed_plan_E1.md](PLAN_the_plan_gate_reads_the_committed_plan_E1.md) — six
  stories, written by Opus 2026-10-15; **E1.S2 is the cornerstone** (the reader's integrity proof, spec by Fable).
- **Goal.** §2 items 1–3, 5 and 6, and the session-file half of 4: a caller names a committed plan, the server reads it
  from the object database at the one pinned commit, proves the capture whole, refuses every wrong read before a round is
  consumed, every reviewer gets it whole, a reviewer that says it did not is not counted as answering, and the round's
  record in the session file says what was reviewed. `planText` callers see only the two new prompt lines.
- **Scope.** `GitHistory` widened (§6 step 1: `GitAnswer.Truncated` set from `ProcessResult.Truncated` at
  `GitHistory.cs:264-274`; `EntryAtAsync`, `BlobAsync`, `RootAndHeadAsync`, `PathStatusAsync`, revisions behind
  `IsCommitish`); the pure helpers in `core/` and `CommittedPlanReader.ReadAsync` in `runners/Collecting/` with §4's order
  (D5–D9); `Tools.cs` — `string? planText = null`, `committedPlan`, `expectedCommit` (D1, D4), the corrected description
  (D15, "plus a read-only checkout" removed at `Tools.cs:90`); `ReviewPlanAsync` becomes `async`, refuses both/neither
  (D2), resolves the tip once through `ResolveShaAsync` inside the `Error(...)` catch (D3, precedent `PanelService.cs:484`),
  checks `expectedCommit`, and passes `Head = sha` on `StageRun` (`StageRun.cs:99`); the prompt's source line, the D12
  instruction and `END OF PLAN <TextId>` (§5) with the `context for review` log line; the D12 fold rebinding `results`
  between `RunAllAsync` and `ReviewerSummaryFactory.From` (`RoundEngine.cs:434`) into `ReviewerOutcome.Unparseable`
  (`ReviewerExecutor.cs:201`); `PlanSource` as a nullable, non-normalised `RoundRecord` member (D10, the `Caller`
  precedent `SessionStore.cs:193`), `StageRun.PlanSource`, the stamp beside `Sha`/`CadenceNote` (`RoundEngine.cs:483-490`),
  none on a `SkippedOrOwed` round, and the `ServerJsonContext` registration — so `status` (`SessionAnswer.Rounds`,
  `ServerJsonContext.cs:143-151`) shows it with no new member; `research/module_server.md` for the tool and the record.
  Not in E1: any `rounds` column, `--log`, anything in `src_vs_code`.
- **Stories list (T/B).** The reader and its refusals: T2, T3, T4, T5, T6, T7, T8, T9, T16, T18, T21 · the arguments and
  the pin: T10, T11, T12, T20 · the prompt and the fold: T1, T14, T15, T19 · the record: T13's session-file and `status`
  assertions. Break-its B1–B9, all of them — every one mutates E1 code.
- **Done when.** T1–T12, T14–T16, T18–T21 and T13's session/`status` half green as the MTP executable; B1–B9 each
  observed red with its named test, then green; `TheInstructionsFitWhatAClientKeepsTests` still green (D15 touched the
  description only); a `planText` round's prompt is byte-identical to today's plus the two new lines; the server's
  `review_plan` schema publishes `planText`, `committedPlan` and `expectedCommit` as optional; `module_server.md` updated.
- **Cornerstone AREA.** The reader's integrity proof — §4 steps 3–5 as one unit: the `ls-tree -z` entry rules (mode, type,
  size, exactly one entry), the `Truncated` refusal, and the blob-id recomputation with its BOM and U+FFFD order (D5–D7).
  Every other failure in this plan has a symptom a person can see — a refusal sentence, a reviewer line, a visible
  `PLAN_INCOMPLETE` finding when the fold is missing (B6 goes red on a verdict, not on silence). This one has none: a
  capture that is not the blob, accepted and recorded under the blob's id, is reviewed as the plan and reads as the plan
  for ever. The fold was considered and not chosen: its two failure modes are both visible (a blocking finding, or an
  answered count that T15/T19 pin), and it rides on the finding schema the gate already parses.

### E2 — Expose plan provenance in history and the rounds log (§6 steps 4–5) · S · depends on E1 (the record it projects and shows)

- **Stories.** [PLAN_the_plan_gate_reads_the_committed_plan_E2.md](PLAN_the_plan_gate_reads_the_committed_plan_E2.md) — three
  stories, written by Opus 2026-10-15 together with E1's and revised 2026-10-16 against `main` once E1 had merged (#837);
  no cornerstone.
- **Goal.** §2 item 4's second half: `--log` and the extension's rounds log show what each plan round reviewed; a database
  from before the step and a session file from before E1 read as *not recorded*, never as empty.
- **Scope.** The schema step (D11) — `ALTER TABLE rounds ADD COLUMN plan_source TEXT NOT NULL DEFAULT ''` appended after
  `TheRoundsSize` (`Schema.cs:37`) as **step 23** (array index 22): the round-size plan's step 22 merged first (#829), and the
  authors plan's schema step moved to 24. The number is still checked against `main` at merge — if another step landed
  first, this PR renumbers; the projection write in `RecordRoundRow` (`RoundsDb.cs:842-922`) from `record.PlanSource`,
  `''` when null — the `round_key` precedent, rewritten from the record on every re-record; `LoggedRound` gains an **init
  property** `PlanSource? PlanSource { get; init; }` (the `Size` precedent, `RoundsQuery.cs:113` — not a trailing positional
  parameter, which amends D11), read by `RoundPlanSources.Attach` chained after `RoundChangesLog.Attach` and only under
  `HasColumn(db, "rounds", "plan_source")` (`RoundsQuery.cs:909`), the ids bound through a helper shared with
  `RoundChangesLog`; `--log` emits it; `rounds.ts` gains the optional `planSource` type and `roundsLog.ts` one detail line (D16), nothing when the
  field is absent; `research/module_extension.md` and `architecture.md` § the `--log` seam (one more field, same rule).
  **No backfill, decided:** a plan round completed between E1's merge and E2's carries `PlanSource` in its session file
  and in `status`, and reads `''` in the database — *not recorded*, by the absent ≠ empty rule D10 already states. Rounds
  have no startup re-projection today (consultations do, `PanelService.cs:159`); building the first one for the rounds
  of one dev window between two merges is machinery larger than the gap, and the session file keeps the fact.
- **Stories list (T/B).** T13's `--log` row and "a DB without the column reads `null`" assertions; T17; three break-its of
  its own, named here because none of B1–B9 touches E2 code: **B10** drop the `HasColumn` guard → T13's step-22 database
  read goes red; **B11** render the line for an absent `planSource` → T17's "shows nothing" case goes red; **B16** bind
  `''` always in the projection write → the round trip of a committed plan round goes red (story E2.S1).
- **Done when.** A step-22 database opens at step 23 and a read-only `--log` on a step-22 file still answers; a round
  recorded before the step reads *not recorded* in `--log`; a round recorded after it shows commit, path, blob and bytes;
  `bundledPage.test.ts` shows the line for a round with `planSource` and nothing without; B10 and B11 observed red then
  green; `CoaiMcp.Tests.exe` and `npm test` green; the step number checked against `main` at merge; both documents of this
  plan promoted together.
