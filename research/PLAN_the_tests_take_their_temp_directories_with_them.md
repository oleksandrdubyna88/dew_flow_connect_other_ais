# PLAN — a run sweeps the temporary directories before it starts

> Status: **IMPLEMENTED, 2026-09-23.** Both runners sweep `coai-*` directories older than ten minutes
> before they start, on one rule in `shared/temp-sweep.json`. **Deviations:** (1) the rule is shared
> as a DATA file both halves read, not as one script the C# suite calls — the server suite already had
> a working sweep (`TempDirsAreSwept` over `PanelService.PruneOldScratchDirs`), so it was widened, not
> replaced; (2) the rule gained what this plan did not have: `neverSwept`, the product's own runtime
> working directories under the same prefix (a live chat's directory can sit an hour with nothing
> written), held against each program's own source by a test in each program; (3) test #4 — "a
> whole-suite run leaves the count where it found it" — is a MEASUREMENT, not an in-suite assertion: a
> suite cannot observe what it leaves after it exits. Measured 2026-09-23: a full extension run swept
> 855 leftovers and, on an already-swept temp, added 19. The trade is recorded in
> [module_tests.md](module_tests.md): test-made product-prefixed directories now wait for the
> product's own six-hour sweep. Scope: the test runners (`src_vs_code/scripts/run-tests.mjs` and the
> `src_mcp` suite's entry), and one shared sweep.
>
> Opened by the operator on 2026-09-18 after a sweep found **5 455 leftover directories**, all made
> the same day.
>
> Related: [module_tests.md](module_tests.md).

## The operator's ruling, which decides the design

> *«тесты их не убирают за собой — а должны. Причём не после себя, а перед запуском: всё, что
> старше 10 мин — очищать.»*

**Before the run, not after each test.** That is the whole shape of this plan, and it is the right
way round for a reason the measurement below makes plain: cleanup that hangs off each test only
works when the test ends. A run stopped with Ctrl-C, a runner killed by a failing build, a host that
crashes mid-scenario — none of those get to their `after`, and those are exactly the runs that leave
the most behind. One sweep at the start always runs, and it is one place to read instead of two
hundred.

**Ten minutes** is the protection: anything a concurrently running session is using was touched more
recently than that, so the sweep cannot reach into another agent's run. This repository has up to
three sessions against it at once.

## The symptom, measured

`C:\Users\<user>\AppData\Local\Temp` held **5 455** directories named `coai-*` on 2026-09-18. The
oldest was stamped 11:47 and the newest 14:06 — **every one made that day**, by eight runs of
`npm test` and a few `src_mcp` runs. 53.5 MB, which is nothing; 5 455 entries in one folder, which
is not.

It has cost time twice. `SubmissionOrderTests` has looked HUNG with nothing failing, and on
2026-09-18 `chatStoreFile`'s two-writer race — a test whose subject is a `.lock` file in that very
folder — failed once in eight suite runs and passed 40/40 on its own. Neither was a defect in the
code under test.

The count is the cost. Every `mkdtemp` walks the directory, every antivirus scan re-walks it, and a
test about a lock there is competing with five thousand siblings.

## Where they come from, for the record

Not to be fixed one by one — the sweep makes that unnecessary — but because the list says how fast
the folder fills and therefore what the sweep has to keep up with.

| Prefix | Count | Made by |
|---|---|---|
| `coai-order` | 645 | [RuleOrderTests.cs:22](../src_mcp/tests/RuleOrderTests.cs), [SubmissionOrderIsTheDispatchOrderTests.cs:29](../src_mcp/tests/SubmissionOrderIsTheDispatchOrderTests.cs), [SubmissionOrderTests.cs:31,46](../src_mcp/tests/SubmissionOrderTests.cs) — field initialisers, so one per test class instance |
| `coai-atomic` | 450 | [atomicFile.test.ts:23](../src_vs_code/src/test/atomicFile.test.ts) — the `aTempDir()` helper |
| `coai-discovery` | 132 | `testDiscovery.test.mjs` |
| `coai-chat-store` | 92 | `chatStoreFile.test.ts` |
| the tail | ~4 100 | `coai-couldnotask`, `coai-doors`, `coai-chat-lock`, `coai-chat-ledger`, `coai-discovery-bare`, `coai-custom`, `coai-host-ws`, `coai-stages` and others |

Worth noting because it shows the per-test approach does not hold: `atomicFile.test.ts` contains
**both** shapes. Line 23 takes a directory with no cleanup; lines 204–205 and 220–221, in the same
file, write `t.after(() => fs.rmSync(dir, { recursive: true, force: true }))`. Somebody already
knew the rule and it still leaked, four lines away.

## What this builds

**1. One sweep, run before anything else.** `src_vs_code/scripts/run-tests.mjs` and the `src_mcp`
suite's entry both call it first. It removes every `coai-*` directory in the system temp directory
whose last write was more than ten minutes ago, and it says how many it took, so a run that starts
by removing four thousand directories tells somebody that.

**2. It is pure where it can be.** Which entries to remove is a decision over `(name, lastWrite,
now)` and belongs in a function a test can run against a list, with no filesystem at all. The part
that actually unlinks is the thin half.

**3. It never fails a run.** A directory it cannot remove — a handle held open, a permission, a
racing session — is counted and stepped over. A test suite that refuses to start because of
housekeeping is worse than the housekeeping.

**4. It is shared, not written twice.** Two runners in two languages, one rule. The JavaScript half
is the implementation and the C# suite calls the same script, or the rule moves to
`.agents/conventions/tools/` if a third repository turns out to want it.

## Test plan

```bash
cd src_vs_code
npm test
```

| # | Test | Why it has teeth |
|---|---|---|
| 1 | The decision keeps what is younger than ten minutes and takes what is older — both directions, against a fixed `now`. | "Removes old ones" passes a build that removes everything, including the run another session has in flight. That is the one outcome this must never have. |
| 2 | Only `coai-*` is considered. A neighbouring directory with another name is not offered for removal whatever its age. | The sweep runs against the shared system temp directory, where other software lives. |
| 3 | A directory it cannot remove is counted and the sweep carries on. | A suite that will not start because housekeeping failed has made things worse. |
| 4 | A whole-suite run leaves the count where it found it, give or take the run's own directories. | The measurement, as a test \u2014 the only one that would have caught this, and it catches every future shape. |
| 5 | Every new assertion watched failing first. | `testing.md`, mandatory. |

## Growth surface

**Negative, and that is the point.** The steady state becomes "what the last ten minutes made".

## Definition of Done

- [x] One sweep, called before anything else by both runners, removing `coai-*` older than ten
      minutes and saying how many.
- [x] The decision is a pure function with its own tests; only the unlinking touches a disk.
- [x] A removal that fails is counted and stepped over, never fatal.
- [ ] A whole-suite run leaves the count where it found it, asserted. **Measured instead** — see the
      status line; a suite cannot assert what it leaves after it exits.
- [x] Each assertion watched failing first, and both observations reported.
- [x] `research/module_tests.md` records the sweep and the ten-minute rule with its reason.
- [x] Promotion per `common/planning-docs.md`, then `plan-lifecycle.mjs`.
- [x] Through `review_plan` and `review_code`.

## What this deliberately does NOT do

**It does not add an `after` to every test.** That was the first draft and the operator ruled against
it, correctly: per-test cleanup does not run for the runs that leave the most behind, and it is two
hundred places to keep right instead of one. Tests are free to clean up after themselves where it is
natural — `atomicFile.test.ts` already does in places — but nothing depends on it.

**It does not touch directories the extension makes at runtime.** Those have owners and lifetimes of
their own, and a person's data directory is not housekeeping.

**And it does not clean anybody's machine retroactively** — that was done by hand on 2026-09-18:
5 363 directories older than ten minutes removed, ninety-two younger ones left alone in case another
session was mid-run.
