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
> `.vsix` size measurement — **shipped 2026-09-17**) and **1.3** (the diff — **shipped 2026-09-18**).
> Four features and a measurement fork cannot pass one review-fix-doc-commit cycle as a unit.
>
> **Story 1.3's own measurement changed its algorithm.** Anonymisation INVENTS differences: the
> normaliser numbers placeholders in order of declaration, so a fix adding one line renumbers
> everything below it, and a plain line diff marked **7** lines where **1** had changed. The
> comparison therefore runs over masked names and the display keeps the real text — at the cost,
> stated in the module, that a difference consisting only of an index is reported as no difference,
> which cannot be told from a genuine change of variable because the skeleton no longer carries what
> would distinguish them. Measured on a constructed pair; no real corpus was on the machine.

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

### The measurement, run 2026-09-17 — and the choice is **Shiki**

Shiki 4.4.3, three grammars (`csharp`, `typescript`, `javascript`), one theme, the **JavaScript**
regex engine rather than the Oniguruma WASM one:

| | today | with Shiki |
|---|---|---|
| extension bundle, raw | 1,772,645 B | **+613,959 B** (+35 %) |
| the same, deflated — what a `.vsix` actually stores | 442,653 B | **+96,010 B** |
| `.vsix` on disk | 565,590 B | ≈ 661,600 B (**+17 %**) |

**The CSP admits it unchanged.** Shiki emits inline `style` attributes; the page sets no
`style-src-attr`, so they fall back to `style-src 'unsafe-inline'`, which is already there.

**It fits a pure, synchronous page module.** `createHighlighterCoreSync` + `createJavaScriptRegexEngine`
imports nothing from `node:`, needs no WASM file, and returns HTML without an `await` — so
`reviewPageHtml` stays the pure synchronous function the bundle test requires. This was the risk
that could have ruled Shiki out and it did not: the grammars raised **no engine warnings**, and on a
realistic skeleton `public`/`async`/`await` came back `token-keyword`, `// …` `token-comment`,
`"a default"` `token-string-expression`, and `Task<List<string>>` tokenised without the `<` breaking
anything.

**Why the size is worth paying, and it is not the reason you would guess.** *Neither option can use
VS Code's own colours.* A webview is handed the workbench theme variables and **no token colours** —
the only token-ish variable in either repository is `--vscode-debugTokenExpression-name`, and
`creds_for_devs` colours its own `tok-*` classes with `var(--vscode-charts-*)` and hard-coded
fallbacks. So "as close to VS Code's own as possible" reduces to **tokenisation**, which is exactly
what real TextMate grammars buy and a regex cannot.

Three things then decide it:

1. **The corpus is real code.** `Normalise` renames identifiers and leaves literals, comments,
   generics and interpolation verbatim — so the page renders `Task<List<string>>`, `$"{var_1} …"` and
   `// …`, which is precisely where a regex highlighter goes wrong.
2. **`scriptRender.ts` has no C# row at all**, and C# is the corpus's main language. The reuse saving
   is smaller than it looks: the port would mean hand-writing a grammar for the language that matters
   most, which is the thing being reused to avoid.
3. **`createCssVariablesTheme` keeps theme- and tone-following.** It emits `var(--coai-hl-token-…)`
   rather than baked colours, so the page maps them to `var(--vscode-charts-*)` exactly as the
   sibling does — and story 1.1's tone control still reaches the code. A default Shiki theme would
   have hard-coded `#1E1E1E`/`#D4D4D4` and broken both.

**What this costs if it is ever regretted:** one module, `highlight(code, language): string`. The page
calls that and nothing else, so the engine behind it can be swapped without touching `bugzReviewPage.ts`.

**The cost per draw, raised by three plan reviewers and then measured.** 200 pairs is 400
`codeToHtml` calls and a draw happens after every decision: **468 ms and 400 blocks on the first
draw, 2–3 ms and 0 blocks on every draw after**, once each block is memoised by its own text. Not the
multi-second freeze the round predicted, but a person deciding about ninety pairs paid the first
number ninety times.

**The size ceiling this page has, named rather than solved.** At 200 pairs the page is **1.3 MB of
HTML** and its first draw costs 468 ms; both scale linearly, so 2000 pairs would be ~13 MB and ~4.7 s.
Highlighting is not what makes that true — the markup is — so the answer is virtualisation or
paging, not a lazier highlighter. The code round proposed highlighting only the open rows; measured,
that trade is worse, because the page paints a disclosure locally so a click costs no process, and
moving it to the panel would put a **209 ms full rebuild on every row somebody opens** against 468 ms
once per panel. **This belongs to epic 2**, where tabs per project and per language arrive and a
corpus is split for other reasons anyway.

**Shipped, and the prediction checked against the real package.** The estimate above was made by
deflating a probe bundle; the `.vsix` actually built afterwards came to **664,353 B — +98,763,
+17.5 %** against the predicted +96,010, +17.0 %. Close enough that the estimate was worth making,
and reported here because a prediction nobody checks is a number, not a measurement.

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

> **Story 2.1 built the cheap half of this on 2026-09-18**, and three things in its brief turned out
> wrong when the code was read, each of which changed the design:
>
> 1. **The AFTER skeleton is not the method at `head_sha`.** `Collector.LocateThenWalkAsync`
>    normalises the before side from `head_sha` and the after side from `touched.Sha` — the commit
>    the walk found the fix in, stored as `fix_sha`. A complexity "of the method at `head_sha`" would
>    have been confidently wrong about the after side, so `ReviewPair` carries `fixSha` as a seventh
>    field beyond the brief's six, and the page labels each side with its own commit.
> 2. **The AOT context needed no new `[JsonSerializable]` line.** The source generator emits an
>    accessor for every type reachable from a registered root; `Default.StoredPair` already existed
>    that way, and a compile probe proved it before `ReviewPair` was written.
> 3. **`--pairs-json`'s inner join to `findings` was fine; the new joins to `rounds` and `sessions`
>    are LEFT.** A pair whose session row is gone must stay on the page. The product cannot orphan
>    one — Microsoft.Data.Sqlite turns foreign keys on — but the `sqlite3` shell leaves them off, and
>    that is the hand that prunes old sessions.
>
> What shipped: `RoundsDb.Pairs()` → `ReviewPair` (`Sendable()`, `StoredPair` and `UploadRun.Wire`
> byte-identical, `OnlyThreeFieldsLeaveTests` unmodified); `why`/`fix` with "none recorded"; the
> short hash as TEXT beside `file:line` (links are epic 3); cyclomatic complexity computed from the
> skeleton in `cyclomatic.ts`, comments and literals blanked first, labelled `2 at aaaa111 → 4 at
> bbbb222`. The class name is story 2.3's (it is not stored, and the un-anonymised locate is where it
> comes from). Records: `research/module_server.md`, `module_extension.md`, `module_tests.md`.

> **Story 2.2 built the two tabs on 2026-09-18, and this plan's identity rule did not survive the
> measurement.** The rule written above — the git remote, else the normalised root with worktree
> suffixes stripped, else `unknown` — was checked against `sessions.repo_path` in the live store
> (106 distinct values) before anything was built, and both halves were wrong:
>
> 1. **Stripping a worktree-looking suffix merges unrelated repositories.** Not hypothetically:
>    removing the last segment puts **22** directories under `d:/rsd/_wt` into one bucket —
>    `coai-*`, `creds-*` and `conv-gate`, three different products — and **20** under `d:/rsd`,
>    which is every project on the machine in a single tab. Two plan reviewers said so
>    independently (findings 0 and 3) and the table proved them right. **No suffix is stripped.**
> 2. **The git spawn is unnecessary.** A linked worktree's `.git` is a FILE naming its parent, so
>    one `readFileSync` does what a process was going to: 54 live paths → **10** identities, and
>    the two `_wt` siblings above land in their two correct products. The extension still runs no
>    git at all.
> 3. **A trap neither this plan nor any reviewer named.** A submodule INSIDE a worktree writes
>    `gitdir: .../repo/.git/worktrees/wt-rp/modules/...`, so cutting at `/.git/worktrees/` files
>    **dew_flow_conventions under connect_other_ais**. The parent is recovered only when exactly
>    one segment follows `/worktrees/`.
> 4. **41 % of the corpus is gone**, and 33 sessions record `repo_path` as `.`. Both are explicit
>    buckets rather than errors — an unreachable project's tab says *not on disk any more*.
>
> What shipped: `projectIdentity.ts` (the rule), `tabStrip.ts` (**the one strip**, extracted from
> `rolesPage`, which was converted to it in the same change so the extraction did not merely add a
> fourth copy — finding 4; byte-identical output proved over all five tab values), `reviewTabs.ts`
> (project then language, each strip drawn only when it offers a choice, language tabs rebuilt from
> the chosen project so the blank table findings 1 and 2 describe cannot occur), and the panel
> holding both selections with `draw` split so a filter press repaints without a server process.
> `roundsLog`'s tab strip is still missing `role="tablist"`/`aria-selected`/`aria-controls`: named
> as a defect, left as a question for the operator rather than repaired as a side effect.
> Records: `research/module_extension.md`, `module_tests.md`.

> **Story 3.1 built actions 2 and 4 of the revision rule on 2026-09-18** — *Open at &lt;sha&gt;* and
> *Open CURRENT (may differ)* — and three things in this plan and its brief turned out different
> when the code was read:
>
> 1. **Git does not run where this plan says it does.** The paragraph below names "the repository's
>    existing exe-plus-argv launcher (`processLauncher.ts`)" — the EXTENSION. Story 2.2 verified and
>    kept that the extension spawns no git at all, and 2.3 put the whole real-method read behind a
>    server one-shot; this story does the same. `coai-mcp --file-at --id <findingId>` is the read,
>    through `GitHistory.FileAtAsync`, which IS `git show <sha>:<path>` and already goes through the
>    one launcher with the tested timeout and tree-kill. The sha regex written below, `^[0-9a-f]{7,40}$`,
>    was not added: `GitHistory.ObjectId` (exactly forty) already gates every git read, and a second
>    pattern would have been weaker in one direction and stricter in another.
> 2. **"Its canonical resolution must stay under the repository root" is the wrong guard for the
>    historical path, and the right one for the current file.** A file deleted or renamed since the
>    recorded revision has no canonical current path, so canonicalising it would refuse exactly the
>    blob action 2 exists to read — 99.6 % of orphaned blobs still read. The stored path is validated
>    LEXICALLY (traversal, absolute, drive-qualified, NUL), server-side, on the one road into
>    `sha:path`; the symlink question is git's, whose object spec resolves nothing outside the object
>    database. Action 4 opens the live filesystem, so it takes the canonical guard — `insideReally`,
>    twice: the recorded checkout inside an open workspace folder, and the file inside the checkout.
> 3. **"Existence is checked before the action is offered" became "probe once per repository, on the
>    first press, and remember."** A `cat-file -e` per row at paint is a process per row for a page a
>    person may never scroll; an offer derived from nothing is a press that fails for the 41 % of
>    checkouts that no longer exist. The first press in a repository is the probe, its answer is
>    remembered for every row of that repository, and a row whose commit is gone, or whose file was
>    not at that path then, says so instead of offering — the latter naming the path and offering the
>    current file, which the plan round made Blocking.
>
> What shipped: `--file-at` (server: `FileAtRevision`, `FileAtReader`, `CommittedFile` extracted
> from `RealMethodReader`, `GitHistory.IsRepoRelative` on the road in; every domain outcome a reason
> at exit 0, 65 for a bad id; the repository is the row's, never argv's), and on the page a
> read-only `coai-revision:` document named `Totals@aaaa111.cs` for action 2, the guarded live open
> for action 4, `RevisionMemory` per repository, `reviewAbout.ts` extracted so the page stays under
> its line ceiling. `StoredPair`, `Sendable()`, `UploadRun.Wire`, `OnlyThreeFieldsLeaveTests` and
> `NormalizeAnswer` byte-identical. A SHA-256 repository stays unsupported everywhere (`ObjectId` is
> forty characters); the one-line fix is recorded in `module_server.md`, not made. Action 3 is story
> 3.2. Records: `research/module_server.md`, `module_extension.md`, `module_tests.md`;
> `.agents/PROJECT.md` names the mode.

> **Story 3.2 was SPLIT into three on 2026-09-21, and 3.2a shipped the same day.** Its plan round
> came back `good_enough` with 17 findings, 15 gating and none of them false, converging on one
> thing: the LIFETIME of a durable checkout is the whole risk, and it must be decided before any code.
> The split (`story32-split.md`, done by Fable at max as the gate's commands required) is **3.2a** a
> tree exists and opens in a new window · **3.2b** a tree can be removed, one at a time, never with
> work in it · **3.2c** the new window lands on the method. The dangerous half is 3.2b and it gets its
> own branch and its own review round, because anything that can REMOVE a checkout is where somebody's
> work is at risk.
>
> Four decisions the round left open, made in the split rather than left to the implementer: the tree
> lives until a person removes it (**no** LRU, no disposal sweep, no stale rule — every automatic
> remover can take a tree somebody is reading); the cap **refuses** at creation and names what is
> held, never evicts; identity is `(git common dir, full sha)` because a worktree is registered in
> exactly one object store; submodules **are** populated, and an empty mount is named on the row.
>
> Three things measured against real git 2.55 before any of it was built, two of which changed the
> design:
>
> 1. **"Never pass `--force` to a tree somebody is reading" is impossible as written** — a tree with a
>    POPULATED submodule refuses a plain `worktree remove` (exit 128, *working trees containing
>    submodules cannot be moved or removed*); unpopulated, it exits 0. So 3.2b's safety rests on our
>    own inspection of the tree, never on withholding the flag. This was a finding I had ACCEPTED in
>    the plan round, and it was not buildable as phrased.
> 2. **`--lock` defeats a single `--force`** (exit 128, *cannot remove a locked working tree*) — which
>    is exactly the call `WorktreeManager.RemoveAsync` makes — but git's own message names the
>    override, `remove -f -f`. A guard, not a vault, and the docblock says so.
> 3. **A worktree HEAD is a gc root**: after `reflog expire --expire=now --all` and `gc --prune=now`
>    an orphaned commit checked out in a tree is still there. A review tree PRESERVES the 55.7 %.
>
> And one the code settled: `PruneOursAsync`'s second half is a `Directory.Delete` by prefix that asks
> git no permission, so **no lock can stop it** — only a different prefix, and the machine-local root
> is the second, independent guard. Both are asserted separately, and the prefix one was shown red by
> putting the prefix back: *Expected Directory.Exists(made.Path) to be True ... but found False*.
>
> **What 3.2a shipped:** `--tree-at` through the same `AnswerOnePairAsync` door (`ReviewTree`,
> `ReviewWorktrees`, `ReviewTreeRecords`; record-written-last as the readiness signal, git's own
> `mkdir` as the mutex, cap 10 refusing with every held tree named), and on the page a third button
> *Check out &lt;sha&gt; in a new window* — `forceNewWindow: true` pinned as the WHOLE option object,
> the in-flight state said before the first await, the reason union asserted against the C# constants.
> `StoredPair`, `Sendable()`, `UploadRun.Wire`, `OnlyThreeFieldsLeaveTests` and `NormalizeAnswer`
> untouched; `WorktreeManager` and `PruneOursAsync` untouched. Records: `research/module_server.md`,
> `module_extension.md`, `module_tests.md`; `.agents/PROJECT.md` names the mode.
>
> **One thing 3.2a's PROJECT.md edit caught, which is worth more than the story:** a seven-line
> paragraph pushed the EIGHTH tier rule out of every reviewer's prompt — `StageRulesTests`
> `TheRotatedTail_CurrentlyFitsAtMostOneRule` went red naming seven. The canary's own docblock claims
> 1 145 bytes of headroom on CRLF; empirically fewer than ~320 bytes of PROJECT.md prose fit today.
> The paragraph was cut to two lines. **The stale headroom figure is not re-baselined here** and is
> the first thing the next PROJECT.md edit will hit.

> **Story 3.2b shipped 2026-09-21** — a person can see every review checkout this machine holds and
> give one back, one at a time, and nothing they typed into one can be deleted by pressing anything in
> this product. Its plan round returned `good_enough` with 14 gating findings and **not one of them was
> false**; all 16 were accepted. Three changed the design outright:
>
> 1. **Ignored files are not reproducible.** The plan called them build output and swept them along
>    with the tree. A `.env`, a local config and a globally ignored `notes.md` are all somebody's work
>    and all invisible to a plain status. They are counted, sampled and refused, and `--with-ignored`
>    is the person's second ask — which is also the confirmation another reviewer asked for, so the
>    two findings collapsed into one design.
> 2. **`git worktree prune` has no path filter.** The plan inherited 3.2a's gated prune for a vanished
>    tree; the gate checks only OUR path, and the prune still clears every unreachable registration in
>    that repository — including a person's own worktree on an unmounted drive. A vanished tree is now
>    FORGOTTEN: the record is dropped, git's registration is left, and the next checkout at that
>    identity clears it under its own narrow guard. A test asserts an unrelated unreachable worktree
>    survives.
> 3. **Empty output is not a clean tree.** A permission failure or an inaccessible submodule produces
>    no lines while git exits non-zero. Exit 0 is required for the parent and for every submodule
>    descended into.
>
> Measured before any of it was written, and it is why the refusals can NAME things: one
> `git status --ignore-submodules=none` in the parent sees all four dirty states — including an
> untracked file inside a populated submodule — and a clean tree answers empty. But it reports the
> submodule cases as the MOUNT and never the file, so naming what is in the way takes one more status
> per submodule the parent's own `S.M.`/`S..U` flags already pointed at.
>
> **The interface is a COMMAND and a picker, not the block this plan described**, and the reason is
> worth the deviation: `bugzReviewPage.ts` is at 791 of its 800-line cap; the Bugz section repaints on
> a poll and a list costs a process to build; and a picker returns ONE item, so *one at a time* becomes
> true by construction rather than by an assertion about a button that does not exist — which the plan
> round itself called unobservable.
>
> `ReviewTreeRoot` was extracted so the root, the prefix and the identity have one home: a second copy
> of the prefix is a second place for the one thing that must never be wrong to be wrong. Records:
> `research/module_server.md`, `module_extension.md`, `module_tests.md`, `architecture.md`;
> `.agents/PROJECT.md` names both modes and **adds no prose**, because fewer than ~320 bytes fit there
> before a tier rule leaves every reviewer's prompt.
>
> **Still open after it:** story 3.2c (the new window landing on the method) and story 3.3 (callers and
> callees, after its measurement).

> **Story 3.3's GATE ran on 2026-09-21, and it passes.** This plan says in so many words that no
> delegation logic is written until the measurement has run, because *"a language whose provider
> answers nothing turns 'load on demand' into a feature that silently shows zero for a method with
> fifty callers"*. It has run, as a scenario in a real VS Code rather than as something somebody does
> by hand — which makes it repeatable and makes it a test rather than a memory.
>
> | asked | answered |
> |---|---|
> | a language WITH a provider, method called twice | `prepareCallHierarchy` → 1 item, `provideIncomingCalls` → **2** |
> | the same language, method called by nobody | 1 item, **0** |
> | a language with NO provider installed | `prepareCallHierarchy` → **0 items**, so incoming cannot be asked at all |
>
> **So "unavailable" IS distinguishable from "zero"**, and it is distinguishable at the FIRST call:
> an empty `prepareCallHierarchy` means nobody could be asked, and a prepared item with zero incoming
> means nobody calls it. That is the rule the plan demanded, and it is a property of the API rather
> than of our code — which is why the scenario stays: the day it stops being true is the day the
> feature starts lying, and nothing else would notice.
>
> **Timing: cold 0.8–2.0 s, warm ~35 ms** — a 25–58× difference, measured twice. Two things follow.
> A first expansion costs SECONDS, so it needs the in-flight state CLAUDE.md §8 asks for, exactly as
> 3.1's press and 3.2a's checkout do. And nothing may be asked at PAINT: two hundred rows would be two
> hundred cold preparations, which is the same arithmetic story 3.1 measured and refused for its own
> probe.
>
> **What the measurement could NOT establish, and why it is not a gap this can close.** The harness
> launches with `--disable-extensions`, deliberately, so a developer's own extensions cannot change
> the answer. Only VS Code's BUILT-IN providers therefore answer — TypeScript and JavaScript do, C#
> does not, because C# needs an extension. So the table above measures the API's CONTRACT, which is
> what the gate is about, and says nothing about which languages a given person's editor can answer
> for. That question has no answer this repository can compute: it depends on what each person has
> installed. It is also, after this measurement, the question that no longer matters to the design —
> a language whose provider is absent is `unavailable` and says so, which is the whole point.
>
> **3.3 is therefore unblocked**, with three rules it inherits from the numbers: load on demand only,
> an in-flight state on the first expansion, and `unavailable` rendered as itself and never as zero.

> **Three more of story 3.3's facts, measured 2026-09-21 because its plan round asked for them.** The
> round returned 15 findings, 13 gating; 14 were accepted. Two were questions a measurement could
> settle, and settling them produced a third nobody had asked:
>
> ```
> [3.3] indented method: column0=1 ('Totals') atSymbol=1 ('counted')
> [3.3] file never opened: prepared=1 ('neverOpened') incoming=1
> [3.3] a file that does not exist: threw=true
> ```
>
> 1. **Asking at column 0 does NOT return empty — it returns the ENCLOSING symbol.** A reviewer
>    predicted that an indented method would resolve to nothing and read as `unavailable`. What it
>    actually does is worse: `prepareCallHierarchy` at character 0 of `    public counted()` prepares
>    **`Totals`**, the class, and would have counted the CLASS's callers under the method's name — a
>    number that is precise, plausible and about something else. So the column must be found from the
>    line's text, AND the prepared item's name must be checked against the row's `symbolName`: here
>    the two guards agree, because `Totals` ≠ `counted`.
> 2. **A provider answers for a file nobody opened**, and finds its caller in another file
>    (`prepared=1, incoming=1`). The concern was that a language service might index only open
>    documents, which would have left this feature blank for almost every review row. It does not.
> 3. **A file that no longer exists makes the call THROW**, rather than answer empty. That separates
>    *the file is gone* from *there is no provider* with no guessing at all — which is exactly the
>    distinction another reviewer said the plan had collapsed.
>
> Both scenarios stay as tests, for the same reason the first one does: they measure VS Code rather
> than us, and the day any of the three changes is the day a feature built on them starts lying.

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

> **Story 2.3 built this on 2026-09-18 — as a toggle over the view, with the class name — and four
> things in its brief turned out different when the code and real git were read:**
>
> 1. **The rename recovery the reader was going to have is unreachable, and the collector is why.**
>    The brief said the fetch "runs the same rule over the same commits" and the collector reads the
>    path AT the fix commit through `touched.Path`. Measured on real git: `git log --reverse
>    --name-only --follow head..fix -- <old name>` lists the rename commit under the OLD name and
>    nothing after it, so `Collector.WalkAsync` reads `fix:<old name>`, fails, continues and records
>    `symbol_gone` — **a pair whose fix renamed the file is never stored.** The reader therefore
>    follows no rename; its fixture for that shape now asserts the honest `file_not_in_commit`.
>    `GitHistory.CommitsTouchingAsync`'s docblock claims the opposite for the walk, and that is the
>    collector's to correct, not this story's.
> 2. **The two sides are independent, which the brief did not say.** The after side is located by
>    the NAME the pair already stores (`symbol_name`), not by the name the before side read — so a
>    head commit pruned since (55.7 % are orphaned) still leaves the after side readable, and
>    `commit_unreachable` is a fact about EITHER commit rather than the collector's head-only word.
> 3. **The class is a new normaliser method, `IAstNormalizer.EnclosingType`, not a field.** As the
>    brief asked — but it is asked at the FUNCTION'S first line so the innermost type answers, which
>    is what makes a method in a nested class name the inner class; the brief's four cases are the
>    suite's four tests, plus a TypeScript/JavaScript class pair.
> 4. **The plan's byte-level test is one test with three assertions, not two tests.** The database
>    file, the serialised `UploadRequest` (through the run's own `Wire`, by reflection as
>    `OnlyThreeFieldsLeaveTests` reaches it) and the wire's contents are asserted after a read that
>    provably put `_items` on the screen — and the mutation that makes the mode write one row turns
>    it red at byte 101998. The extension's half is the page posting a decision with exactly `type`,
>    `keep`, `ids` while real text shows.
>
> What shipped: `--real-method --id <findingId>` (stdout; 65 for a bad id, 74 for a database that
> will not read, every domain outcome a reason on the document at exit 0), `RealMethod`/`MethodSide`
> in the core with `RealMethodReason` reusing the collector's words, `RealMethodReader` in the
> runners, `RoundsDb.Pair(id)`; on the page a **Real code** toggle, both texts on every row with one
> render reading the current toggle, a generation (`draw/seq`) on every fetch with stale answers
> discarded, and the panel's cache keyed by id and validated by both shas, emptied with the window.
> `StoredPair`, `Sendable()`, `UploadRun.Wire`, `OnlyThreeFieldsLeaveTests` and `NormalizeAnswer`
> byte-identical. **Panel-held, not a setting** — the assumption stated above, made. Records:
> `research/module_server.md`, `module_extension.md`, `module_tests.md`; `.agents/PROJECT.md` names
> the mode.

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

### ANSWERED 2026-09-18 — and it leaves this plan

The operator decided: **allowed.** *"Whoever does not want it will choose a local LLM."* Both routes —
the Team server on to a provider's LLM, and a vendor CLI on to that vendor's cloud — are permitted.

So this is no longer a question and no longer a story here. It is its own plan, because the work it
unlocks is a transport that does not exist plus a boundary that has to be rewritten rather than
deleted. The five pieces are listed under **Build order** above, and the one addition beyond the
operator's words is there too: the picker has to MARK what leaves the machine at the point of
choosing, because "whoever does not want it will choose a local LLM" is only a choice if the person
can see which is which.

**Nothing in epics 1–3 depends on it**, and the review page's own promise is untouched: the page
supplies decision ids, the send projects the stored pair, and `OnlyThreeFieldsLeave` stays green.

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
| **1 — the page can be read** (no server change, no new data) | 1.1 collapse + zoom + tone · 1.2 highlighting, after the measurement · 1.3 the diff | Opus | **all three shipped** — `codeHighlight.ts` and `lineDiff.ts` are on `main`. The row said *1.1 shipped* until 2026-09-18, which is a status line that stopped matching the repository rather than work that stopped. |
| **2 — the page says what it is showing** (one wider SELECT, then the renders) | 2.1 the projection + cause, fix, hash, path, complexity · 2.2 project and language tabs · 2.3 the real method, un-anonymised, and its class | 2.1 and 2.3 **Fable max**, 2.2 Opus | **2.1 built 2026-09-18** (through both gate rounds; the populated live contract closed after the code round) · **2.2 built 2026-09-18** (the identity rule rewritten against the live table; `tabStrip` extracted and `rolesPage` converted) · **2.3 built 2026-09-18** (`--real-method`, a toggle over what the rows already hold; the rename recovery removed after real git showed the collector cannot store the row it would serve) |
| **3 — reaching the code, honestly about which revision** | 3.1 open at revision / open current · 3.2 a review worktree, SPLIT into 3.2a/3.2b/3.2c · 3.3 callers and callees, after the measurement | 3.1 and 3.2 **Fable max**, 3.3 Opus | **3.1 built 2026-09-18** ; **3.2a built 2026-09-21** (Opus, not the Fable the split assigned — Fable was rate-limited and a four-hour wait was worse than the substitution; said here rather than implied). **3.2b built 2026-09-21** (`--trees` / `--tree-remove`, a picker rather than a page block, ignored files refused until asked for per checkout). **3.2c not started**; **3.3 built 2026-09-21** — its gate measurement ran first and passed, its plan round returned 15 findings (13 gating, 14 accepted), and two of those were measured rather than argued: asking at column 0 prepares the enclosing CLASS, and a provider does answer for a file nobody opened. Earlier note: (`--file-at` server-side, a read-only document of the product's own scheme, the current file behind the workspace guard, one probe per repository remembered; the plan's sha regex and canonical-path guard for the historical read both dropped after reading the code — see the story note above) · 3.2, 3.3 not started |
| **4 — moving the anonymisation boundary** | 4.1 the server accepts a comment · 4.2 the client sends one | **Fable max** | **unblocked 2026-09-18, not started.** Both decisions were answered by the operator and both are recorded in this document: a person's comment is PUBLIC, so no PII scanner and no local-only fallback; and the ranking pass MAY use a remote model. This row still read *blocked on two decisions* while the sections below already carried the answers. |

### Carried out of story 1.1's code round, rejected there and owed somewhere

Two reviewers, independently, said the decision path has **no visible in-flight state**: pressing
*Keep selected* clears the selection, disables the buttons and then runs the server binary with
nothing on screen saying so. I rejected both for this story — the decide path is shipped code a
presentation-only story does not touch — but the observation is right, and it is bigger than a
spinner: CLAUDE.md §8 says a status-changing action must reflect its real state across a reload, and
a decision that is written by a child process is exactly that shape. It belongs with **epic 2.1**,
which is already opening `Pairs()`, or in a story of its own. It is not a story 1.1 omission.

### Both of epic 4's preconditions were answered on 2026-09-18

**Comments are PUBLIC.** Asked whether a person's comment is scanned for PII before it is sent or
stays local, the operator decided: *a comment a person wrote is public; it goes everywhere, including
to the server, for storage and later processing.*

So **epic 4 needs no scanner and no local-only fallback**, and the story that would have written one
does not exist. Four ordinary decisions remain for 4.1: size, character set, version negotiation,
retention. One thing follows that is NOT a scanner: the page must SAY, beside the box, that a
comment leaves the machine. Not a confirmation and not a gate — the policy is decided — but somebody
choosing what to put in a public field can only choose it knowingly if the page tells them, and every
other surface in this product says where things go.

**The ranking pass MAY use a remote model.** Asked whether un-anonymised finding text may reach a
remote vendor — through the Team server to a provider's LLM, or through a vendor CLI to that vendor's
cloud — the operator decided: *allowed; whoever does not want it will choose a local LLM.*

That settles the policy and does **not** shrink the work, so story 7 becomes a plan of its own rather
than a widened dropdown:

1. `RankingModels.Local` / `IsAllowed` (`RankingModels.cs:41,49`) stop being a boundary and become a
   default — rewritten as an explicit *allowed* list rather than a deleted check, so what is
   permitted stays written down in one place.
2. `ThePinThatMustNotDriftTests` reads that file and moves with it, deliberately, in one commit — the
   way `OnlyThreeFieldsLeave` is treated.
3. The transport, which does not exist: `Ranking.Order` orders a reply nothing produces
   ([PLAN_the_corpus_tail.md](PLAN_the_corpus_tail.md) §1).
4. The picker: provider first, then model, every provider — the operator's item 11.
5. **The picker marks what leaves the machine, at the point of choosing.** "Whoever does not want it
   will choose a local LLM" only works if the person can see which choice is which.

**And what leaves is not what the upload sends.** The ranking pass runs BEFORE the pairs are
collected, so before `Normalise` exists: it sees the reviewers' text, the real path and the real
method name, not `var_1`/`method_2`. The upload's promise is three anonymised fields guarded by
`OnlyThreeFieldsLeave`; nothing equivalent can guard this one, because ranking placeholders would
have nothing to rank. Wherever this is documented, it is documented in those words.

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
