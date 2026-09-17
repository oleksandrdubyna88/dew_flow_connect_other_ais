# PLAN — the page tests RUN the page

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: the webview page test files under
> `src_vs_code/src/test/` — `roundsLogPage.test.ts` first, then `rolesPage.test.ts`,
> `chatPresetsPage.test.ts` and every other file that asserts over a generated page's SOURCE TEXT.
>
> Related docs: [research/module_extension.md](../research/module_extension.md),
> [.agents/conventions/common/generated-code-tests.md](../.agents/conventions/common/generated-code-tests.md)
> (the rule this exists to satisfy),
> [research/PLAN_a_round_leaves_as_a_file.md](../research/PLAN_a_round_leaves_as_a_file.md)
> (where it was raised, and which converted the two controls it added).
>
> **The boundary with [PLAN_the_tail_of_the_command_split.md](PLAN_the_tail_of_the_command_split.md),
> written here because a boundary legible from one direction only is how the same work gets built
> twice** (`planning-docs.md`). That plan fixes what the HOST does and owns the **extension-host**
> harness (its story 13). This one owns the **webview page** and the 224 source-text assertions over
> it. They are disjoint, and the harnesses are different: `bundledPage.test.ts` executes a bundled,
> minified page script against a stub DOM, which cannot reach a native `createQuickPick`.
>
> **Two rounds of that plan's gate sent its story 6 here, and both were wrong** — story 6's refusal is
> drawn by `vscode.window.createQuickPick` at `conversationPickerCommand.ts:197`, a native control,
> not a page script. Nothing in this plan blocks it and nothing in it blocks this. Recorded so the
> next reader does not repeat the routing.
>
> The status line also gained its date on 2026-09-17: `planning-docs.md:28` requires
> `plan only, nothing implemented yet, <YYYY-MM-DD>`, and `plan-lifecycle.mjs` checks that a status
> line EXISTS rather than that it carries a date — so this was green while being wrong.

## The symptom

A webview page in this extension is assembled as a template literal and handed to VS Code as text.
Thousands of tests assert over that text — `assert.match(html, /data-export=/)` — and the rule in
`common/generated-code-tests.md` says why that is not enough:

> *"No substring assertion can see that, because the string contained everything it was supposed to
> contain. The artefact was correct as text and wrong as a program."*

That rule was written after a measured failure in a sibling repository: four thousand green tests
over an assembled string while a user-visible bug shipped, because one of three worked examples was
painted without the CSS class every colour rule is scoped under.

**This repository has now hit the same class of defect twice in one plan**, and both are worth
reading before starting:

1. `roundsLog.ts`'s click handler needs an early `return` in the tick-box and Export branches, or a
   control also opens the row it sits in. A source assertion cannot see a missing `return`.
2. When the executing tests for those two controls were first written they observed the WRONG thing
   ("the host was not asked for findings") and stayed green with the `return` deleted. They only
   gained teeth once they observed the row actually opening. **An executing test is not automatically
   a test with teeth** — this plan must verify each converted assertion by breaking what it covers.

## Why it is its own plan

The operator's ruling, 2026-09-14, on
[PLAN_a_round_leaves_as_a_file.md](../research/PLAN_a_round_leaves_as_a_file.md):

> *Move the full conversion of the old tests to runtime checks into a separate tech-debt ticket (or
> finish it now if the deadline is not pressing), so the current PR is not inflated. Merging new
> tests with the old text assertions from here on is forbidden.*

So the conversion is this plan, and **the prohibition is already in force** — it is written into
`.coderabbit.yaml`'s path instructions for `src_vs_code/src/test/**` and into `.agents/PROJECT.md`,
so a new source-text assertion is flagged on review rather than waiting for this plan to land.

## What must be true when it is done

- Every behavioural claim about a page is asserted against the page's RESULT after running it: what
  the DOM holds, what was posted to the host, what a control did.
- Source-text assertions remain only where there is no program to run — a `<meta>` tag, a CSP
  header, a nonce being present, an escaped value appearing escaped.
- Each converted assertion is verified by breaking the behaviour it covers and seeing it go red with
  the real symptom. A conversion that stays green under its own break is not done.
- The harness is shared, not copied per file.

## The harness already exists — widen it rather than writing a second one

`src_vs_code/src/test/bundledPage.test.ts` already bundles a page module with esbuild, runs
`roundsLogHtml`, extracts the `<script>` body and executes it with a DOM shim and a
`acquireVsCodeApi` stub (`new Function('document', 'window', 'acquireVsCodeApi', body)`). Its
`runningPage()` helper records every element, every listener and every posted message, and
`clickInRow()` builds a synthetic event whose `closest` answers for a control AND the row it sits in
— which is what makes a missing `return` observable.

**Extract that into `src/test/pageHarness.ts`** and let every page test import it. Do NOT start from
`node:vm`: the rule's deny-by-default boundary is worth reaching for, but the existing `new Function`
+ explicit-context approach is what these tests already trust, and replacing it is a second change
competing with the conversion for review attention. A `node:vm` sandbox with a cleared environment,
an allowlist of globals, a hard timeout and an output bound is a good follow-up once the conversion
is done.

## The boundary with the stylesheet reader (MANDATORY, both sides)

A reviewer on 2026-09-17 read
[PLAN_one_reader_for_a_page_stylesheet.md](PLAN_one_reader_for_a_page_stylesheet.md) as working
against this plan. It does not, and the division is written on both sides so the objection does not
have to be re-made.

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| BEHAVIOURAL assertions over page source text — a control on the wrong branch, a missing `return` | **this plan**, by running the page against the shim | none | either |
| Reading a page's STYLESHEET — parsing rules, ranking by specificity, saying which one paints | [PLAN_one_reader_for_a_page_stylesheet.md](PLAN_one_reader_for_a_page_stylesheet.md), on `cssRules.ts` | none — a DOM shim does not implement the cascade | either |

**Disjoint**: [`PROJECT.md:110`](../.agents/PROJECT.md#L110) carves out exactly this case —
*"Source assertions stay legitimate where there is no program to run"* — and `cssRules.ts` was created
by issue #322's code round **after** the 2026-09-14 ruling this plan enforces. A stylesheet question is
never answered by running the page; a behavioural question is never answered by reading its text.
Neither plan blocks the other.

## Build order

1. **`src/test/pageHarness.ts`** — `runningPage(module, exported, state)`, the stub element with
   recorded listeners, `clickInRow`, and a `deliver(message)` for the host→page bridge. Moved out of
   `bundledPage.test.ts`, which then imports it; that file's tests must stay green unchanged, which
   is the proof the extraction changed nothing.
2. **`roundsLogPage.test.ts`** (13) — the file the reviewer named, and the smallest real one, so
   the harness is proved on it before the big one. Convert in place, assertion by assertion,
   breaking each behaviour to confirm the new assertion sees it.
3. **`rolesPage.test.ts`** (12) — the second page, which is what shows whether the harness
   generalises or was shaped around one page.
4. **`chatPresetsPage.test.ts`** (21).
5. **`chatPage.test.ts`** (173) — **three quarters of the work on its own.** Triage first, into
   behavioural and legitimately-textual, and record the split in the section above; then convert the
   behavioural ones. Expect this to be several sittings, and do not start it before the harness has
   survived steps 2–4.
6. **A guard** so this cannot regress: a test that reads the page test files and fails on a new
   behavioural assertion over page source. This is the hard part to get right — it must not fire on
   the legitimate cases above — so it comes last, informed by what the triage in step 5 learned.

## How much is left — counted 2026-09-14, not estimated

`grep -c 'assert\.\(match\|doesNotMatch\)' src_vs_code/src/test/*Page*.ts`:

| File | Assertions |
|---|---|
| `chatPage.test.ts` | **173** |
| `chatPresetsPage.test.ts` | 21 |
| `roundsLogPage.test.ts` | 13 |
| `rolesPage.test.ts` | 12 |
| `bundledPage.test.ts` | 5 *(already the executing harness; these are its non-executable ones)* |
| `rolesPageScript.test.ts` | 0 |
| **Total** | **224** |

Two things that number says. **`chatPage.test.ts` is three quarters of the work** and should be
sequenced accordingly — it is not "one more file". And not all 224 are defects: an unknown share are
the legitimately textual cases (a nonce, a CSP header, a value appearing escaped), so the first real
task after the harness is to triage that file's assertions into *behavioural* and *textual* and
record the split here. A conversion that treats all 224 as wrong will churn assertions that were
right.

## Test plan

The tests ARE the deliverable, so the test plan is the verification discipline:

- Each converted assertion: break the behaviour, watch it go red with the real symptom, restore.
- After each file: the whole extension suite, with the count before and after — a conversion that
  loses tests has dropped a claim, and a conversion that gains none has not converted anything.
- `bundledPage.test.ts` green throughout, unchanged, as the harness extraction's own proof.

## The boundary with the notifications plan

> Reciprocal of the *Who builds what* table in
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md), which is
> MANDATORY on both sides — a boundary named once is not a boundary.

**This plan owns the backlog of existing source-text assertions.** The notifications plan adds
nothing to that backlog: its new page (S5) is tested by RUNNING the page against the DOM shim from
its first commit, which is the shape this plan is converting the others to.

So the count in *How much is left* does not grow when that page lands.

## Definition of Done

- [ ] `src/test/pageHarness.ts` exists and every page test imports it; no page test builds its own.
- [ ] `roundsLogPage.test.ts` asserts behaviour after running the page; its remaining text
      assertions are only the non-executable ones, each with a comment saying why.
- [ ] Every other `*Page*.test.ts` converted, or listed here with the reason it cannot be.
- [ ] Each converted assertion verified by breaking what it covers.
- [ ] The guard in step 6 exists, and is itself verified by adding a forbidden assertion.
- [ ] The whole extension suite green, with counts before and after.
- [ ] `research/module_extension.md` records how a page is tested here.
- [ ] This plan promoted per [planning-docs.md](../.agents/conventions/common/planning-docs.md).
