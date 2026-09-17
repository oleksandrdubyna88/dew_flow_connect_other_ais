# PLAN — the review page can be read

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: `src_vs_code`
> (`bugzReviewPage.ts`, `bugzReviewPanel.ts`, `bugzView.ts`), `src_mcp` (the projection `--bugs-json`
> and `--pairs-json` return), and — for one story only — the ingest contract in `src_bugs`.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_server.md](../research/module_server.md),
> [PLAN_a_corpus_of_real_defects.md](../research/PLAN_a_corpus_of_real_defects.md) (the corpus this
> page decides for), [PLAN_who_holds_a_key.md](../research/PLAN_who_holds_a_key.md) (the keys and the
> send), [PLAN_the_corpus_tail.md](PLAN_the_corpus_tail.md) (the ranking pass that has no transport).

## The symptom

The **Review bugs** page is where a person decides what leaves this machine, and it is the hardest
page in the product to read. Ninety rows, every one expanded, `BEFORE` and `AFTER` side by side with
no highlighting and no indication of what actually differs between them, in code whose identifiers
have already been replaced with `var_1`, `method_2`, `type_1`. There is no way to tell which project
a row came from, no way to reach the real file, and nothing that says why the finding was raised or
what would fix it.

The operator listed fifteen things on 2026-09-17, in the order they hit them while trying to use it.
This plan is those fifteen, grouped by what they actually require, because they are five different
kinds of work wearing one coat.

## What is already here, and must be reused rather than rebuilt

| Need | What exists | Where |
|---|---|---|
| Zoom and tone controls | `zoomControlHtml` / `zoomStyle` / `zoomScript` / `ZOOM_CSS`, and `toneControlHtml` / `toneStyle` / `TONE_CSS` — already used by **five** pages | `src_vs_code/src/zoomControl.ts`, `textTone.ts` |
| A highlighter | A dependency-free regex highlighter, nine languages, escapes before it marks | `dew_flow_creds_for_devs/src_vs_code/src/scriptRender.ts` |
| The columns a row needs | `why`, `fix`, `head_sha`, `repo_path`, `file`, `line`, `language` are all in the schema already | `src_mcp/src/Store/Schema.cs` |
| The row the page gets | `RoundsDb.Pairs()` selects nine columns and none of the above | `src_mcp/src/Store/RoundsDb.cs:382` |
| The row the page holds | `ReviewPair` — and it ALREADY carries `symbolName` and `title`, the real method name and the real finding title | `src_vs_code/src/bugzReviewPage.ts:17` |
| The upload's own projection | `UploadRun.Wire` builds `UploadedPair` from the STORED pair, independently of anything the page holds | `src_mcp/src/Collecting/UploadRun.cs` |

**That last row is the safety property this whole plan rests on.** Everything below adds local
material to the page — real paths, real class names, a call graph, un-anonymised source — and none of
it can reach the server, because the page never supplies the payload: it sends decision IDs, and the
send independently reads the stored pairs and projects exactly three fields. `OnlyThreeFieldsLeave`
is the test that says so, and it must stay green through every story here.

## Story 1 — the page can be read at all *(presentation only)*

> Split into **1.1** (collapse, zoom, tone — **shipped 2026-09-17**), **1.2** (highlighting, after the
> `.vsix` size measurement) and **1.3** (the diff). Four features and a measurement fork cannot pass
> one review-fix-doc-commit cycle as a unit.

Nothing new is fetched; this is rendering.

- **Every row collapsed by default**, expandable, with **Collapse all** and **Expand all** at the top.
- **Zoom and tone controls**, by adopting `zoomControl.ts` and `textTone.ts` — the five pages that
  already use them are the specification.
- **Syntax highlighting** of `BEFORE` and `AFTER`.
- **The difference highlighted separately**, the way git shows it — added, removed, changed — ON TOP
  of the syntax highlighting.

### The one fork in this story, and it needs a measurement

The operator said two things that pull apart: *reuse what `creds_for_devs` already solved*, and
*get as close to VS Code's own highlighting as possible*. The sibling's `scriptRender.ts` is a regex
highlighter whose own docblock says it *"will misread exotic nesting and that is fine; it is a
credential manager, not an IDE"*. Closest-to-VS-Code means TextMate grammars — Shiki uses the same
grammars and themes VS Code does — and that is a real dependency in a bundled `.vsix`.

**Measure before choosing**: the `.vsix` size today, the size with Shiki and only the corpus's
languages, and whether the webview's CSP admits it. Ship the regex port if the cost is not worth it,
and say so in the plan; the shape (one highlighter module the page calls) is the same either way, so
the choice can be revisited without touching the page.

**`BugzReviewPanel.draw()` replaces the entire HTML**, so collapse state, zoom and tone must live
somewhere a repaint does not destroy — the panel's state, not the DOM.

**And the key that state is held by is `findingId`**, which already exists on `ReviewPair` and is
already rendered as `data-row="${pair.findingId}"` (`bugzReviewPage.ts:57`). Three reviewers asked
whether Story 1 secretly needs new data for this; it does not, and naming the key is what makes that
answer checkable. What it DOES need is tests for the ways a positional key would have passed anyway:
a redraw that **reorders** rows, one that **removes** the expanded row, and two rows with the **same
title**. A key that is an array index survives none of those and every same-order test.

**Zoom and tone are asserted WITH a collapsed row**, not beside one: the two modules style the body
and the rows are what collapse, so the interaction is the thing to check rather than either half.

## Story 2 — which project, and which language *(a projection change)*

- Top-level tabs per **project / repository**.
- Sub-tabs per **programming language** inside each.

`p.language` is already on the pair and `s.repo_path` is already on the session, through
`collect_pairs → findings → rounds → sessions`. `Pairs()` selects neither. This is a wider SELECT and
a grouping in the page, not new collection.

### The identity rule, decided here rather than by whoever implements it

Three reviewers refused to let this stay as "decide the rule", and they were right: a test written
after the fact passes whichever arbitrary rule the implementer chose. `PLAN_a_corpus_of_real_defects.md`
measured the material — differently spelled paths for one repository, **30 % of raw candidates in
scratch directories**, 104 worktrees — and a tab per distinct `repo_path` would split one project
across a dozen tabs.

**There is no remote URL in this database.** `sessions.repo_path` is the only repository identity the
schema holds (`Schema.cs:41`), so a precedence beginning with a git remote is not available without
collecting one first. The rule is therefore, in order:

1. **The git remote, when the checkout is reachable and has one** — resolved locally at render time,
   never stored, because it is the only identity that survives a move. Unreachable checkout: next rule.
2. **The normalised root**: resolve the path, strip a worktree suffix (`…/_wt/<name>` and the
   `…/scratchpad/wt-*` shape this session itself uses), and take the repository directory name.
3. **An explicit `unknown` bucket** for a path that resolves to nothing — scratch directories that no
   longer exist, which is 30 % of raw candidates. Named, and never silently merged into a real project.

**Never the basename alone**, which merges unrelated repositories that happen to share a folder name.

The test is over the paths ACTUALLY in the database — a worktree path, a scratch path, a missing one,
and two spellings of one repository — with the expected mapping written down for each.

## Story 3 — what the finding says, and where it lives *(a projection change, plus a revision rule)*

- **Cause and proposed fix** in the description: `findings.why` and `findings.fix` exist and are not
  projected. Show what is recorded, and say plainly when a finding has none rather than inventing one.
- **The file path and class name, as a link.**
- **Cyclomatic complexity** of the method.
- **The commit hash**, and four ways to reach the code.

### The revision rule, which is what makes the links honest

The pairs describe HISTORICAL code — the round's `head_sha`. A link, a complexity number and a call
graph describe **today's checkout**. If the method moved, renamed or gained an overload, all three are
convincing and wrong. The operator's answer, accepted on 2026-09-17: show the hash, and offer the ways
through explicitly. Measured in `PLAN_a_corpus_of_real_defects.md` §2–3, over the real corpus:

- `head_sha` is **orphaned 55.7 %** of the time — squash-merge plus branch deletion. Per repository:
  `dew_flow_connect_other_ais` **72 %**, `creds_for_devs` 39 %, every `_wt/*` worktree **100 %**,
  `creds_e4` / `creds_e5` / `conventions` **0 %**.
- **but 99.6 % of orphaned blobs are still readable.**

So the four actions are not one button:

1. **Show the hash**, short, beside the path — and say when it cannot be resolved.
2. **Open the file at that revision** (`git show <sha>:<path>`, read-only). Works ~99.6 % of the time.
3. **Switch the working tree to that commit** — the most attacked item in the round, by four findings
   across three providers, and they are right. It ships only with all of: the commit **reachable**,
   the tree **clean** (tracked AND untracked), the checkout not shared with another live session, the
   prior `HEAD` **snapshotted** and **restored on any failure**, and the result reported explicitly.
   If any of those cannot be satisfied cheaply, the action is **a dedicated review worktree** instead
   of this checkout — or it does not ship. A button that can eat uncommitted work is worse than no
   button.
4. **Open the file as it is now** — the current working tree, which is where a fix would be made.

**Every one of these runs against stored data, so the data is validated before it reaches git.** A
stored pair can carry `file = ../../.ssh/config`, an absolute path, or a malformed sha:

- the sha must match `^[0-9a-f]{7,40}$`;
- the path must be **relative** and its canonical resolution must stay under the repository root, or
  the action is refused;
- git is invoked through the repository's existing **exe-plus-argv launcher** (`processLauncher.ts`
  already spawns `spawn(target, [...args])` with no shell) and with a timeout that kills the process
  tree — never a shell string, where a metacharacter in stored data becomes execution.

**And existence is checked before the action is offered, not after it is clicked.** `head_sha` being
reachable does not mean the file existed at that path then — a move or a rename makes
`git show <sha>:<path>` exit non-zero. `git cat-file -e <sha>:<path>` decides whether action (2) is
offered at all; when it is not, the row says the file was not at that path at that revision, which is
itself the most useful thing it could say.

**Every one of them says which revision it means, in different words.** Number 4 is the one that can
mislead, so it is labelled CURRENT rather than presented as "the" file.

## Story 4 — who calls this, and what it calls *(measure before building)*

- How many methods **call** this one, expandable, each entry a clickable path.
- How many methods **it calls**, the same.

**Delegate, do not compute.** VS Code answers this itself through `vscode.prepareCallHierarchy`,
`vscode.provideIncomingCalls` and `vscode.provideOutgoingCalls`. References are **not** the same as
callers and must not be substituted for them.

**What to measure first, before a line is written**: open a representative method of each language in
the corpus and invoke Show Call Hierarchy by hand; time a cold and a repeated expansion; record which
languages answer at all. Provider coverage per language is unknown and cannot be established from the
checkout.

**The measurement is a GATE, not a step**: no delegation logic is written until it has run, because a
language whose provider answers nothing turns "load on demand" into a feature that silently shows
zero for a method with fifty callers.

Rules that follow from the measurement: load on demand only, count distinct method identities rather
than call sites, distinguish **unavailable** from **zero**, and keep no persistent cache at first — a
caller can change in another file while the target document's version is unchanged.

**And every request is cancellable, bounded and tagged.** A provider can hang, or answer after the
person has collapsed the row, filtered the list or triggered a redraw. So: a cancellation token, a
bounded timeout, a provider error or timeout rendered as **unavailable** rather than as zero, and each
response carried back with the row and draw generation that asked for it — a late answer applied to a
different row is a number that is confidently wrong about somebody else's method.

## Story 5 — the anonymisation can be turned off, locally *(a view, never a payload)*

A control that shows the methods un-anonymised, so the person can read what they are deciding about.

**Local only, and by construction rather than by care.** What is sent is always anonymised: the toggle
changes the VIEW. The property that makes this safe already holds — the page supplies decision IDs and
the send projects the stored pair itself — and the story's job is to add the control without weakening
it.

**The test is byte-level, not visual.** With the view un-anonymised and with it anonymised, the
serialised `UploadedPair` a send transmits must be **byte-identical** — whitespace, field order, null
against absent, all of it. A test that compared the rendered page, or the `ReviewPair` object, would
pass over a serialisation difference, which is the only kind of difference that could actually leak.

## Story 6 — a comment, which DOES go to the server *(a contract change)*

A place to type a comment per pair, sent with it.

**This is the only item in the list that changes a shipped contract**, and it is sequenced last for
that reason. `UploadedPair` carries exactly three properties and an architecture test asserts the
count; the client asserts a blacklist because it holds the original, and the server asserts a
whitelist because it never will. A fourth field means the server must accept, whitelist and store it,
the test's expectation changes, and — the part that needs a decision rather than an implementation —
**the anonymisation promise now covers free text a person typed, which no normaliser can scrub.**

Before any code, five things the round insisted on and I had left as one sentence:

1. **What may a comment contain** — a maximum size, an allowed character set, and what happens to one
   that exceeds either. "An operator pastes a megabyte into each of ninety rows" is the reviewer's
   example and it is not far-fetched.
2. **PII.** Either the comment is scanned before it is serialised, or comments stay LOCAL and never
   enter the wire contract. There is no third option that keeps the anonymisation promise, because no
   normaliser can scrub free text a person typed.
3. **The server's half**: the whitelist must admit the field, the store must have a column, and both
   ship BEFORE the client sends one.
4. **Version negotiation.** The client and the deployed server are upgraded separately and have
   shipped out of step before. A four-field payload to a three-field server may be rejected whole or
   silently truncated, and independent client and server tests both stay green either way. A LIVE
   contract test against both an old and a new server decides this before the wire shape changes.
5. **Retention.** How long a comment is kept, who sweeps it, and what the projected storage is.

If the answer to (2) is "scan it", the scanner is a story of its own and this one waits for it.

## Story 7 — the ranking model picker *(NOT a UI task — a disclosure decision)*

The Bugz section's **Ranking model** control is one flat dropdown of local engines. The operator asked
for provider-first-then-model with every provider available: the team server, the local engines and
the CLI vendors.

**Verified in the code, and it changes what this story is.** `RankingModels.Local` is literally
`["local"]` and `IsAllowed` refuses any model that was NAMED and is not local. The remark above it
says why: *"The vendor half is what decides whether the text leaves the machine, and it is the half
this is allowed to care about."* The ranking pass reads finding text **before** anonymisation.

And `PLAN_the_corpus_tail.md` §1 — *"The ranking pass has no transport"* — records that
`Ranking.Order` decides the order of a reply nothing produces. **The picker today chooses a model that
is never called.**

So this story is two questions for the operator before it is any code at all:

1. May un-anonymised finding text be sent to a remote vendor for ranking? If no, the picker stays
   local-only and the work is to make that legible rather than to widen it.
2. If yes: which vendors, and does the person get told, per run, where the text went?

Widening the dropdown without answering those offers choices the collector refuses — which is the
failure this page already has too much of.

## Build order — four epics, ten stories

The split was made separately, by a model asked to do nothing else, because deciding what the epics
and stories ARE is the judgement that shapes everything after it. It re-arranged the seven stories
above rather than accepting them, and three of its findings changed the plan:

- **There is no un-anonymised text stored anywhere.** `collect_pairs` holds
  `symbol_name, language, skeleton_before, skeleton_after, written_utc, keep` (`Schema.cs:308-319`)
  and the corpus plan records that the raw text is computed and thrown away. So story 5 is **not a
  view toggle** — it is a new one-shot mode that re-runs the collector's locate path without
  `Normalise` and stores nothing. The class name has the same gap and the same answer, so it moves
  there from story 3.
- **Widening `StoredPair` would drag the new columns through the UPLOAD's own type** and break
  `OnlyThreeFieldsLeaveTests`' fixture. The wider SELECT returns a NEW page-facing record;
  `StoredPair`, `Sendable()` and `Wire()` are untouched until the comment story.
- **The worktree is a growth surface with no size, owner or sweep** — `planning-docs.md` requires the
  budget, and the plan did not have it. The in-place `git checkout` of somebody's own tree is **sized
  out**: if it is ever wanted it is a new story carrying all five preconditions, never a widening.

| Epic | Stories | Model | State |
|---|---|---|---|
| **1 — the page can be read** (no server change, no new data) | 1.1 collapse + zoom + tone · 1.2 highlighting, after the measurement · 1.3 the diff | Opus | **1.1 shipped** |
| **2 — the page says what it is showing** (one wider SELECT, then the renders) | 2.1 the projection + cause, fix, hash, path, complexity · 2.2 project and language tabs · 2.3 the real method, un-anonymised, and its class | 2.1 and 2.3 **Fable max**, 2.2 Opus | not started |
| **3 — reaching the code, honestly about which revision** | 3.1 open at revision / open current · 3.2 a review worktree · 3.3 callers and callees, after the measurement | 3.1 and 3.2 **Fable max**, 3.3 Opus | not started |
| **4 — moving the anonymisation boundary** | 4.1 the server accepts a comment · 4.2 the client sends one | **Fable max** | blocked on two decisions |

### Carried out of story 1.1's code round, rejected there and owed somewhere

Two reviewers, independently, said the decision path has **no visible in-flight state**: pressing
*Keep selected* clears the selection, disables the buttons and then runs the server binary with
nothing on screen saying so. I rejected both for this story — the decide path is shipped code a
presentation-only story does not touch — but the observation is right, and it is bigger than a
spinner: CLAUDE.md §8 says a status-changing action must reflect its real state across a reload, and
a decision that is written by a child process is exactly that shape. It belongs with **epic 2.1**,
which is already opening `Pairs()`, or in a story of its own. It is not a story 1.1 omission.

**Epic 4's preconditions are decisions, not code**: the five comment questions (size, charset, PII
— *scan* or *local only*, server-first, version negotiation, retention) and the ranking-picker
disclosure answer. Story 7 is therefore neither an epic nor a story: it is the same question asked of
another pass. If the answer is **no**, the picker stays `["local"]` and "make that legible" is a
one-line hint change folded into 4.2; if **yes**, it is a NEW plan, because the ranking pass has no
transport and `RankingModels.IsAllowed` refuses anything named and non-local — widening the dropdown
alone offers a choice the collector refuses.

## Test plan

- The page is tested by **RUNNING it** (`.agents/PROJECT.md`): every control below is exercised by
  executing the page's own script against the DOM shim, never by matching markup.
- Collapse/expand: a row starts collapsed, expands, collapses all, expands all — and **survives a
  `draw()`**, which replaces the whole HTML.
- Zoom and tone: the same assertions the five existing pages already make.
- Highlighting: the escaping property first — a pair whose code contains `</script>` or `<img onerror>`
  must render as text. Then a token or two per language, as cosmetics.
- Diff: a line added, a line removed and a line changed are each marked, and an identical pair is
  marked nowhere.
- Grouping: the tab rule over the paths **actually in the database**, including a worktree path and a
  scratch-directory path.
- Revision: a reachable hash offers all four actions; an orphaned one offers three and says why.
- Anonymisation toggle: with the view un-anonymised, what a send transmits is byte-identical to what
  it transmits with the view anonymised.
- `OnlyThreeFieldsLeave` stays green through stories 1–5, and its expectation changes **once**, in
  story 6, deliberately.
- **The wiring, which a page test cannot see.** A DOM test can prove a button POSTS the message it
  should while `bugzReviewPanel` or `bugzView` never handles it — the suite stays green and clicking
  *Open at revision*, *Show callers* or *Send comment* does nothing at all. Every new action therefore
  also gets the source-pinned wiring assertion this repository uses for exactly this
  (`bugsSendWiring.test.ts`, `bugsKeysWiring.test.ts`), and `research/module_tests.md` records which
  paths are covered that way and which remain unreachable without an extension-host harness.

## Definition of Done

- [ ] Story 1 ships alone, and the page is collapsed, highlighted, diffed, zoomable and tonable.
- [ ] The highlighter choice is recorded WITH its measurement, not with a preference.
- [ ] Project and language tabs group by a rule tested against real paths.
- [ ] Cause, fix, complexity, path, class and hash are shown, and each says which revision it means.
- [ ] Callers and callees are delegated to VS Code and gated on a measurement.
- [ ] The un-anonymised view cannot change what is sent, and a test proves it.
- [ ] A comment field ships only after the contract decision, and the wire test changes deliberately.
- [ ] The ranking picker is answered as a disclosure question before any control is widened.
- [ ] `research/module_extension.md` and `module_tests.md` updated; this plan promoted when it ends.
