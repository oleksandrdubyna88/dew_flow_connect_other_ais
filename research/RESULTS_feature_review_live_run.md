# RESULTS — the feature gate's live acceptance run: coai reviews its own feature through the product

> Status: **run complete, 2026-09-28.** One `review_feature` call through the PRODUCT — `coai-mcp` built from `main` at
> `37920d78` (the calibration PR #594 merged), driven over MCP stdio — with three reviewers ticked for features and nothing
> else: **grok-4.7** (`api`, xAI, the `xai` module's calibrated defaults), **glm-5.3** (`api`, the Alibaba Model Studio
> Token Plan route with the `qwen` vault key, the `glm` module's defaults) and **Codex CLI** (`gpt-6-luna`). Content: coai's
> own public repository and its feature plan, nothing else.
>
> The plan this proves: [PLAN_feature_review.md](../todo/PLAN_feature_review.md) — D26 (b), and the Definition of Done's
> end-to-end item. Related: [RESULTS_feature_reviewer_models.md](RESULTS_feature_reviewer_models.md) (the calibration
> that chose the two api reviewers and their settings, D26 (a)), [RESULTS_reviewer_input_sizes.md](RESULTS_reviewer_input_sizes.md)
> (why a CLI's token column is not comparable with an api one).
>
> Raw data — the request, the reply, the server's log, the ledger — stays on the operator's machine.

## What ran

| | |
|---|---|
| Binary | `coai-mcp` Debug build of `37920d78`; before the run every suite passed through its executable: CoaiMcp 6,060 passed / 8 skipped, CoaiServer 334, CoaiBugs 317 / 2 skipped, CoaiBench 117, extension `npm test` 4,687 / 2 skipped |
| Data | a fresh, isolated `COAI_DATA_DIR`; its `settings.json` held the three vendor rows (each `feature: true`, every other stage off), `COAI_REVIEWER_TIMEOUT_MINUTES=20` (the calibration's per-turn deadline) and `COAI_CONSULT_ENABLED=false`. Effort, token ceiling and the 20-minute api cap were NOT set: the modules' defaults applied (`providers` before the run: grok `medium`, 8,192, 20 min; glm `high`, 65,536, 20 min). The vault key reached the child through its environment only; the product found the vault CLI itself (§9.10) and loaded 2 keys |
| Call | `repoPath` = the checkout at `37920d78`, `planPath` = `todo/PLAN_feature_review.md`, `baseRef` = `05269852` (the commit before epic 1), `epics` = epic 1 (`d8c0bcaa`), epic 2 (#575), epic 3 (#591) and the calibration (#594), `lessons` = the calibration's causes, xAI's transient 500s, the macOS `/private/var` top-level refusal, the consultation that hung, and what was left undone; `callerModel` = `claude-opus-5-5`. Four epics, so no `COAI_FEATURE_MIN_EPICS` override |
| Pack | 326,686 bytes of feature pack (336,361 bytes of prompt): 565 files changed, 41 outlined, 166 named as not outlined, 358 elided, 4,058 member hunks cut by the budget; rules 73,932 bytes (7 of 12 tier rules, 5 omitted). The gate's history section was empty — the data directory was new, so the rounds and consultations of epics 1–3 were not in its database (the lessons said so) |

## Per reviewer

Every turn's line is the product's ledger (`usage.jsonl`); "served" and "refused" are the reviewer's audit line in the
server's log. A source request made in turn *n* is served into turn *n + 1*.

| reviewer | turns | source requested · served · refused | tokens in | out | cached | reasoning | seconds | cost | findings | outcome |
|---|---|---|---|---|---|---|---|---|---|---|
| grok-4.7 (api) | 4 | turns 1–3 · 11 slices · 11 (5 budget, 5 no such declaration, 1 no such path) | 454,961 | 43,432 | 285,312 (62.7 %) | 39,760 | 664.2 (11.1 min) | $0.7425 | 1 | ok, every turn |
| glm-5.3 (api) | 4 | turns 1–3 · 9 slices · 1 (no such declaration) | 418,164 | 48,609 | 270,336 (64.6 %) | 40,181 | 681.3 (11.4 min) | $0.4911 | 5 | ok, every turn |
| codex CLI | 4 | turns 1–3 · 14 slices · 7 (6 budget, 1 no such path) | 828,384 | 5,431 | 355,328 (42.9 %) | 0 reported | 91.5 (1.5 min) | not metered (subscription; no price row) | 1 | ok, every turn |
| **round** | | | 1,701,509 | 97,472 | | | wall 687.6 s (11.5 min) | **$1.2337** | 7 | `proceed` |

Per turn, in / out / cached / reasoning / seconds:

| turn | grok-4.7 | glm-5.3 | codex CLI |
|---|---|---|---|
| 1 | 94,764 / 14,613 / **1,152** / 13,452 / 220.5 | 90,317 / 16,502 / **0** / 14,663 / 222.0 | 209,430 / 2,243 / 103,168 / – / 34.6 |
| 2 | 110,253 / 8,073 / **94,720** / 6,976 / 120.7 | 99,177 / 16,942 / **90,112** / 14,695 / 237.3 | 228,544 / 972 / 113,408 / – / 17.8 |
| 3 | 123,089 / 14,383 / **94,720** / 13,519 / 225.6 | 106,630 / 10,098 / **90,112** / 7,834 / 143.4 | 255,825 / 1,363 / 126,720 / – / 22.0 |
| 4 | 126,855 / 6,363 / **94,720** / 5,813 / 97.4 | 122,040 / 5,067 / **90,112** / 2,989 / 78.6 | 134,585 / 853 / 12,032 / – / 17.1 |

What the numbers say:

- **The follow-up turns hit the vendors' prompt caches.** grok's turn 1 cached 1,152 tokens (cold), turns 2–4 cached
  94,720 each — 75–86 % of each follow-up's prompt — so `x-grok-conv-id` (§9.27) routes a real conversation to its cache
  at this pack size, which the calibration had measured only on smaller packs. glm cached 0 on turn 1 and 90,112 on each
  follow-up. D25's byte-identical prefix works on both vendors; C3 (CLI resume) is not needed for the api reviewers.
- **The 20-minute cap held with margin.** Both api reviewers finished four turns in 11.1 and 11.4 minutes; the whole call
  took 11.5 minutes. No turn was cut (`finish_reason` never `length` — every turn's outcome is `ok`), no repair ran.
- **xAI answered no 500 this time**, so §9.29's retry of "Auth context expired" is still unobserved live.
- **Cost is the ledger's.** grok's is the list price with its reasoning tokens billed as output (§9.25); glm's is Token
  Plan credits at the proxy list price — an equivalent, not the bill. The CLI's token columns include the CLI's own
  context and are not comparable with the api rows.
- **Refusals were the resolver working, not failing.** The "budget" refusals are the 64 KB-a-turn / 128 KB-a-reviewer
  caps (`SourceBudget`), each naming the lines it dropped; "no such declaration" answered names the file does not
  declare — a tool's name where the tool is a lambda (`ReviewFeature`, `review_feature` in `Tools.cs`), a function asked of
  the wrong file (`serverSettingsJson` lives in `serverSettingsFile.ts`, not `settingsShape.ts`) — each listing the file's
  real declarations;
  "no such path" answered guessed paths (`src_mcp/src/Server/Rounds/TurnLoop.cs`, which lives under `runners/`).

## Verdict and findings

`proceed` — "all 3 reviewers answered", 3 gating findings against a threshold of 5, no `blocking` finding, so D23 ran no
second round. 7 findings: 3 major, 3 minor, 1 nit. Each was read against the code at `37920d78` and resolved through
`resolve` with `feature` = the plan path: **3 accepted, 4 rejected with reasons**; the session is `Done`.

| # | severity | by | finding | decision |
|---|---|---|---|---|
| 0 | major | codex | `review_feature` takes no `head`, though D2 named one | **rejected** — a recorded deviation: S2.2 says "the head is the checkout's HEAD, not a `head` argument (D2 named one)", and the tool description says so |
| 1 | major | grok | the product-owned caller instruction (`src_vs_code/src/featureRule.md`, the snippet's `coai-feature` half) tells the caller to pass `head` — `origin/main` included — and to call again "with the new `head`" after `revise` | **accepted** — true: the tool declares no `head`, and the MCP SDK (ModelContextProtocol 2.2.0) ignores an undeclared argument without an error (checked on `status` with an extra `head`), so the call SUCCEEDS over the checkout's current HEAD — a caller following the snippet on a checkout that is not the feature's gets a clean review of the wrong tree |
| 2 | major | glm | the same missing `head`, with the squash-merged partial-review scenario | **rejected** — the same recorded deviation (in the plan section glm was not served); the text that leads callers into the scenario is #1 |
| 3 | minor | glm | `TurnLoop.Within` bounds a turn's launch and its repair each to the remaining cap, so a turn could take twice it | **rejected, refuted** — `ReviewerExecutor.RunAsync` gives the repair only what the first launch left of the already-bounded timeout, and the rate-limit ladder does the same (`WithinRemaining`) |
| 4 | minor | glm | a turn serves 64 KB of source where §4.9 says 48 KB, unrecorded | **rejected** — recorded beside the constant (`SourceBudget` remarks, the pack trial) and in the plan's S3.1 entry ("64 KB, not 48") |
| 5 | minor | glm | the pack listed the live submodule `.agents/conventions` as "deleted at head" | **accepted** — confirmed: `git cat-file --batch-check` answers a gitlink with two fields (`<oid> submodule`), `CatFile.Object` accepts only three, so the object reads as missing and `FeatureOutlineBuilder.Changed` classifies the path `Deleted`; `ReadPlan`'s "a submodule at head" reason is unreachable for it |
| 6 | nit | glm | `RoundEngine.cs` is 920 lines, over the shared coding-style's 800-line cap | **accepted** — true at head (the D16 extraction itself) |

### Accepted — real defects, NOT fixed in this run

1. **The `coai-feature` snippet half tells the caller to pass a `head` the tool does not declare** (`src_vs_code/src/featureRule.md`,
   "What to pass" and the `revise` / `again` bullets). The SDK drops the undeclared argument silently, so the call succeeds
   and reviews the checkout's current HEAD, whatever `head` named. Fix: say the reviewed commit is the checkout's HEAD — check out the
   commit that holds the feature first — and that a later call needs a moved checkout. The plan's §4.5 signature and §5
   sequence diagram carry the same stale `head` and should be corrected with it. (D24 moves this half to the conventions
   mount in S3.5; the text there must not inherit it.)
2. **A submodule in the range is reported as deleted at head** (`src_mcp/runners/Feature/CatFile.cs` `Object`,
   `FeatureOutlineBuilder.Changed`). Fix, RED first: parse the two-field `submodule` answer as an existing object of type
   `commit`, so the path is named "a submodule at head; not read"; pin it with a range that moves a submodule pin.
3. **`RoundEngine.cs` is over the 800-line cap.** Split the answer-composition half into its own file, or record the
   exemption beside the declaration.

## D26's two conditions

| condition | met? | evidence |
|---|---|---|
| (a) the vendor-calibration PR's acceptance cases: a `finish_reason: length` answer that parses is a failure and the D23 retry ground; a failed call's billed usage crosses shim → executor → ledger on turn 1 and turn 2; per-vendor token limits and reasoning switches; the xAI cache key | **yes** | merged in #594 as §9.23, §9.24, §9.28 and the vendor modules, §9.27, each seen RED then GREEN ([RESULTS_feature_reviewer_models.md](RESULTS_feature_reviewer_models.md)); their tests are in the CoaiMcp suite that passed before this run |
| (b) a live product-path run recording Grok, Qwen and a CLI reviewer with a served follow-up and per-turn usage and cache counts | **yes** | this run: grok-4.7, glm-5.3 on the Qwen (Alibaba Token Plan) key — the calibration recommended glm-5.3 over qwen3.8-max for this route — and Codex CLI; every reviewer was served source in three follow-up turns; per-turn tokens, cached tokens, reasoning, seconds and cost are on the ledger above |

**D26 is satisfied: the mcp release that carries epic 3 is no longer held by it.** What this run did NOT exercise: a
billed failure live (that is (a)'s ground, proven by tests), xAI's 500 retry, a second round, and the gate-history section
over a real database.
