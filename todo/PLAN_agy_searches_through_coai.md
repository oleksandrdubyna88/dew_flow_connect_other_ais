# PLAN — an antigravity consultant searches through coai: list and search served read-only, agy stays in plan mode

> Status: **plan only, nothing implemented yet, 2026-10-08.** Scope: a new bounded read-only workspace reader
> (`src_mcp/src/Server/WorkspaceLookup.cs` + `src_mcp/core/Feature/LookupBudget.cs`), the agy request format and its parser
> (`src_mcp/core/Consultation/LookupRequests.cs`), `AntigravityConsultant` as an answering-follow-up runtime for question rows,
> a bounded request loop inside one `consult` call (`ConsultationService`), the agy prompt text (`AntigravityFollowUps`),
> tests and docs. The reviewers are not touched.
>
> Related docs: [RESULTS_agy_question_row_follow_up.md](../research/RESULTS_agy_question_row_follow_up.md),
> [RESULTS_agy_consult_follow_up.md](../research/RESULTS_agy_consult_follow_up.md),
> [module_runners.md](../research/module_runners.md), [module_server.md](../research/module_server.md).

## 1. The symptom

An antigravity consultant cannot look for anything. coai runs agy in `--mode plan` (read-only); there the only read tool
that works is `view_file` with an exact FILE path — a folder is refused (`invalid_args cannot view <dir>`), and
`list_dir` / `find_by_name` / `grep_search` are declared but unavailable (RESULTS_agy_consult_follow_up.md §1) — and the
shell is auto-denied headless. So a disk question gets "run this command yourself" on Windows (3 of 3 through the
product) and, in WSL, often a failed row (answered 1 of 3): the model reaches outside the root looking for paths and agy
refuses. The codex row, which can search, found the real file in WSL 3 of 3 (RESULTS §4).

## 2. The operator's decision (2026-10-08) and the shape it rejected

The operator first chose agy's own `--dangerously-skip-permissions --sandbox`, if a measurement showed the sandbox blocks
writes and deletes. The vendor's documentation settled it before any run (verified on
<https://antigravity.google/docs/sandbox?tab=cli>): "Workspace folders … are mounted read-write" — the `--add-dir` roots,
the operator's ORIGINAL clones, would stay writable; the sandbox covers commands only; "On Windows, Antigravity continues
to use the previous behavior" (no sandbox); and requests to run outside the sandbox need an approval that
skip-permissions grants. That plan round (session `0b81ee77`) and its consultation (`f706070d`, codex: "choose isolation
or have coai perform bounded list/search") are recorded in the git history of this file. Asked again, the operator chose
**coai searches**: agy keeps `--mode plan` — it can write nothing — and asks coai to list or search; coai does it itself,
read-only, inside the granted roots, and continues the SAME agy conversation with the result. For **both** features: a
question row (`ask_consultants`) and the stuck consultant (`consult`).

## 3. The design

**The request format** (agy answers prose, not a schema, so a fenced block it can write in prose):

````
```coai-lookup
list <path>
search "<text>" in <path>
```
````

- `<path>` is absolute or relative to a granted root; `search` is a LITERAL, case-insensitive substring — never a regex
  (no pattern a model writes can make coai's scan slow). At most 8 lines per block; other lines are named as refused.
- A turn whose answer carries a block is a request turn; any prose beside it is kept as the draft. A turn with no block
  is the answer. The last allowed turn is told it is the last (the api rows' `FINAL` rule, `QuestionTail`).

**The reader, `WorkspaceLookup`** — coai's own code, in coai's process, no shell, no vendor tool:
- **Containment:** every path is canonicalised component by component with links resolved (`DocumentReader.FollowLink` /
  `Canonical`) and must land inside one granted root; `..`, a link or junction leading out, and a path naming a root that
  was not granted are refused by name. Roots are the question row's `Plan.Grant.Roots` (already validated by
  `QuestionRoots.Validate`) or the consultation's checkout.
- **What it never shows:** credential-like files (`CredentialFiles`), `.git/`, build and dependency folders
  (`node_modules`, `bin`, `obj`, `dist`, `target`, the `DiffExclusions` list), binaries, reparse points it would have to
  follow out. Every line it returns passes `Redaction.SafeSource`.
- **Caps** (`LookupBudget`, modelled on `SourceBudget`): `list` ≤ 200 entries, one level, directories marked; `search` ≤ 50
  hits (path:line:text, the line cut at 300 chars), files over 1 MB skipped, a walk of ≤ 20 000 files and ≤ 10 s per
  request; ≤ 32 KB of results per turn; ≤ 3 lookup turns per answer (the api rows' default follow-ups). A cap reached is
  said in the result ("stopped at 50 hits — narrow the path"), never silent.
- It reads the WORKING TREE (a root here is usually not a git checkout), unlike `SourceResolver`, which reads `HEAD`.

**Question rows.** `AntigravityConsultant` implements `IAnsweringFollowUps` for a planned (question) launch: `AfterAsync`
parses the block, serves it through `WorkspaceLookup`, and returns `AnsweringTurn.Next` carrying a continuation of the
SAME conversation — `AntigravityStream.Continue(first, conversation, results)` — instead of a rebuilt launch. Seam
changes this needs (found 2026-10-08): `AfterAsync` gets the finished launch (for the conversation id) and `Next` may
carry a ready invocation; `ConsultantLaunches.MustBePlannable` keeps refusing a handle on a FIRST planned launch only.
agy's usage is cumulative, so `QuestionRowLaunch.TurnsAsync` bills a continued conversation by its latest report, not the
sum (`ConsultationUsage` gains the N-launch rule; one ledger line per TURN stays).

**The consultant.** One `consult` call keeps its single `ConsultationTurn` record but may make up to 3 lookup
continuations inside it, before `Settle` (`ConsultationService.cs:699`), under the turn's existing deadline; the
consultation's tree check still runs before every continuation (`ConsultantTurn`'s `changesSoFar`).

**The prompt.** `AntigravityFollowUps.Toolbox` stops saying only "your only tool is view_file": it adds the `coai-lookup`
format and its limits, for both features. The silent-launch follow-up (`NoCommands`) also names the format, because a
model refused a shell command is exactly the one that needs a listing.

## 4. Build order — three stories, one gate round each way

1. **S1 — `WorkspaceLookup` + `LookupBudget` + `LookupRequests`** (pure reader and parser), RED first.
2. **S2 — question rows**: the seam, `AntigravityConsultant.AfterAsync`, cumulative billing, the prompt; RED first.
3. **S3 — the consultant**: the bounded loop in `ConsultationService`, the prompt; RED first.

Then docs, the whole C# suite, the extension's suites if a shared file changed, one code round over the branch, PR, merge.
Live checks after the build. No release in this plan: a release needs the operator's OK.

## 5. Test plan

- **S1:** containment — `..`, an absolute path in no root, a symlink and (Windows) a junction leading out, a root not
  granted: each refused by name; credential files, `.git`, `node_modules`, binaries never listed or searched; every cap
  hit and SAID; a literal with regex characters matches literally; redaction applied; the walk stops at the time cap.
- **S2:** `QuestionRowOnAgyScenarioTests` (fake CLI as a real child): a first answer with a `coai-lookup` block → the second
  launch carries `--conversation <id>` and the results on stdin → the final answer is the row's advice; a request outside
  the root → the refusal line, no read; the turn cap ends it with the unserved requests named; ONE ledger line at the
  latest cumulative report; existing denial scenarios still pass.
- **S3:** a consult scenario on agy: the lookup turn inside one call, the record keeps one turn, the tree check runs before
  the continuation, billed once.
- **Live, after the build:** three `ask_consultants` per side and one `consult` per side through the branch build; the
  answers must name a real file or say none exists from what coai served — recorded in
  `research/RESULTS_agy_searches_through_coai.md`.

## 6. Definition of Done

- [ ] S1–S3 each RED → GREEN, with a teeth check on the containment tests.
- [ ] agy keeps `--mode plan`; no flag gives it write access; reviewers untouched.
- [ ] Every lookup is bounded, contained, redacted, and says when a cap cut it.
- [ ] Question rows and the consultant both serve lookups and continue the same conversation; billed once per turn.
- [ ] Live checks per side recorded in `research/`; `module_runners.md`, `module_server.md`, `research/README.md` updated;
      plan promoted; `todo/README.md` row removed.
- [ ] Whole suites green; gate rounds resolved; PR merged.
