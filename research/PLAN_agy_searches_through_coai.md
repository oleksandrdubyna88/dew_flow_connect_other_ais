# PLAN — an antigravity consultant searches through coai: list and search served read-only, agy stays in plan mode

> Status: **IMPLEMENTED, 2026-10-08** (branch `feat/agy-searches-through-coai`; no release — the operator's decision).
> Live record: [RESULTS_agy_searches_through_coai.md](RESULTS_agy_searches_through_coai.md) — 6 of 6 asks answered on
> the final LIVE-TESTED build (WSL went from 1 of 3 to 3 of 3, naming the real file), both consults answered using
> lookups; the code-round-2 changes made after it are listed in the record's §7 and are covered by scenario tests only.
>
> Deviations: (1) **the plan's premise "agy keeps `--mode plan` — it can write nothing" is FALSE** — the live write check
> found agy writes inside its `--add-dir` root on both sides; this branch does not change what agy may write, and the
> defect is open as [PLAN_agy_cannot_write_its_roots.md](../todo/PLAN_agy_cannot_write_its_roots.md); (2) a fence agy
> glued to a sentence was read as no block — found live, fixed before the PR; (3) the code round added a 20 s per-block
> time limit, a turn that cannot hold a result reads nothing, a path the file system cannot hold is refused by name,
> lock files are hidden when named directly, and the caps no longer follow `SourceBudget`; (4) the refused-shell
> follow-up keeps its measured text and adds one sentence naming the block when a reader is attached; (5) one answer in
> six opened with the model's working notes — agy's prose, passed on as written, not fixed here.
>
> Scope: a new bounded read-only workspace reader
> (`src_mcp/src/Server/WorkspaceLookup.cs` + `src_mcp/core/Feature/LookupBudget.cs`), the agy request format and its parser
> (`src_mcp/core/Consultation/LookupRequests.cs`), `AntigravityConsultant` as an answering-follow-up runtime for question rows,
> a bounded request loop inside one `consult` call (`ConsultationService`), the agy prompt text (`AntigravityFollowUps`),
> tests and docs. The reviewers are not touched.
>
> Related docs: [RESULTS_agy_question_row_follow_up.md](RESULTS_agy_question_row_follow_up.md),
> [RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md),
> [module_runners.md](module_runners.md), [module_server.md](module_server.md).

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
**coai searches**: agy keeps `--mode plan` — it can write nothing *(refuted live: plan mode does not make an `--add-dir`
root read-only; agy CAN write there — deviation 1)* — and asks coai to list or search; coai does it itself,
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

- `<path>` is absolute, or relative to THE granted root when there is exactly one (a consultation's checkout; a question
  row with one root). With several roots a relative path is refused by name, asking for the absolute one — the prompt
  lists every granted root's absolute path, so agy always knows them (plan round, gemini). `search` is a LITERAL,
  case-insensitive substring — never a regex (no pattern a model writes can make coai's scan slow). At most 8 lines per
  block; other lines are named as refused.
- A turn whose answer carries a block is a request turn; any prose beside it is kept as the draft. A turn with no block
  is the answer. The last allowed turn is told it is the last (the api rows' `FINAL` rule, `QuestionTail`). **If the last
  allowed turn still carries a block** (plan round, gemini): the block is stripped, its prose is the answer with a note
  that lookups were capped and which were not served; with no prose left, the row ends `failed` saying exactly that.

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
consultation's tree check still runs before every continuation (`ConsultantTurn`'s `changesSoFar`), and the turn is billed
by the same N-launch cumulative rule as a question row (plan round, gemini).

**The prompt.** `AntigravityFollowUps.Toolbox` stops saying only "your only tool is view_file": it adds the `coai-lookup`
format and its limits, for both features. The silent-launch follow-up (`NoCommands`) also names the format, because a
model refused a shell command is exactly the one that needs a listing.

## 4. Build order — three stories, one gate round each way

1. **S1 — `WorkspaceLookup` + `LookupBudget` + `LookupRequests`** (pure reader and parser), RED first.
2. **S2 — question rows**: the seam, `AntigravityConsultant.AfterAsync`, cumulative billing, the prompt; RED first.
3. **S3 — the consultant**: the bounded loop in `ConsultationService`, the prompt; RED first.

Then docs, the whole C# suite, the extension's suites if a shared file changed, one code round over the branch; the LIVE
CHECKS on the branch build, recorded, BEFORE the PR (plan round, gemini: a fake CLI cannot prove the real agy turn); then
PR, merge. No release in this plan: a release needs the operator's OK.

The containment re-check runs on the resolved path immediately before each read. A link swapped in by another process
between that check and the read is out of scope (plan round, codex, rejected): it needs a writer inside the root, agy
cannot write there, and such a writer could read the outside file itself. *(Refuted live: agy CAN write files there. What
still holds: a LINK needs a shell, which agy is denied — not separately measured — and the reader opens the resolved path
by name, not through a handle bound to the check; see module_server.md, Threat model of the reader.)*

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
- **Live, on the branch build, before the PR:** three `ask_consultants` per side and one `consult` per side; the answers
  must name a real file or say none exists from what coai served. Plus a **write check** (plan round, codex): agy with the
  exact flags coai launches, in a disposable scratch tree under the temp directory, asked to create a sentinel file —
  the check fails if the file exists afterwards. Nothing is deleted by the check (the owner's rule); its trees are named
  for the person. All recorded in `research/RESULTS_agy_searches_through_coai.md`.

## 6. Definition of Done

- [x] S1–S3 each RED → GREEN, with a teeth check on the containment tests.
- [x] agy keeps `--mode plan`; no flag gives it write access; reviewers untouched. *(But plan mode itself writes — deviation 1; open in todo.)*
- [x] Every lookup is bounded, contained, redacted, and says when a cap cut it.
- [x] Question rows and the consultant both serve lookups and continue the same conversation; billed once per turn.
- [x] Live checks per side and the write check recorded in `research/` before the PR; `module_runners.md`,
      `module_server.md`, `module_tests.md` (both flows, what the scenarios do not prove), `research/README.md` updated;
      plan promoted; `todo/README.md` row removed.
- [x] Whole suites green; gate rounds resolved.
- [ ] PR merged — the pull request that carries this record.
