# PLAN — two equal deadlines race, and the security lane's deadline test loses on macOS

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/SecuritySources.cs`,
> `src_mcp/core/Feature/SourceBudget.cs`, `src_mcp/tests/SecuritySourcesCharacterizationTests.cs`.
>
> Related docs: [module_server.md](../research/module_server.md),
> [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (where it was
> found, releasing mcp 0.42.1).

## 1. The symptom

`SecuritySourcesCharacterizationTests.The_collection_deadline_ends_the_file_it_interrupts_and_every_later_one` failed in
three recent CI runs, all of them on the **macOS** leg (`macOS · mcp · server · bench`): runs 37150422769, 37152106782
and 37191162285 (2026-10-03/04). It takes about 30 s each time and fails with:

```
Expected string to be the same string, but they differ on line 18 and column 1 (index 295):
    "…c/B.cs =>\nnot served:…"     (actual)
    "…c/B.cs =>\nSource read…"     (expected)
```

The macOS check is not required, so auto-merge goes past it. A red check that nobody has to read is how a real
regression on that leg gets missed.

## 2. What the code does (verified 2026-10-04)

- `SecuritySources.cs:14`: `CollectionBudget = TimeSpan.FromSeconds(30)`. `:31-33` links a token that cancels after
  it, and `:84/:87` add `"Source read deadline reached; remaining source omitted."` when that deadline ends the reads.
- `SourceBudget.cs:57`: `ReadDeadline = TimeSpan.FromSeconds(30)`. `SourceResolver.cs:197-210` gives EACH file's git
  read its own linked token with that deadline. If it fires first, the read becomes `Reading.Not(TimedOutReason)`,
  which renders as `not served: … timed out: git did not answer within 30 s` (`:229`).
- The test (`SecuritySourcesCharacterizationTests.cs:146-156`) makes `src/B.cs`'s read hang forever. `A.cs` is read in
  milliseconds, so B's per-file 30 s deadline starts only milliseconds after the collection's 30 s deadline. **Two
  timers of the same length, a few milliseconds apart:** on a loaded macOS runner the later one can fire first, and B
  is reported as a git timeout instead of the collection deadline.

That also happens in production: if a slow first file leaves the per-file deadline the earlier of the two, the
reviewer is told "git timed out on B, ask again next turn" when the truth is "the collection ran out of time".

## 3. Build order

1. **RED, deterministically.** Construct the resolver with a `readDeadline` a hair SHORTER than the collection budget.
   The constructor already takes one (`SourceResolver.cs:42`), and the collection budget may need the same seam.
   Assert the sentence the operator should see. Today that fails on every machine, not just on macOS.
2. **Decide the rule:** when the per-file read times out AND the collection deadline is at or past its end, the file
   is the collection deadline's (`DeadlineReached`), not a git timeout. Probably one check in
   `SecuritySources.ReadFileAsync` (`:70-87`) after a refused read. Alternatively, make the per-file deadline strictly
   shorter than the collection budget, so the two never coincide (state which, and why).
3. **GREEN,** then make the existing characterization test independent of timer order: inject both budgets small
   (e.g. 200 ms / 300 ms) instead of waiting 30 s, which also takes 30 s off the suite.

## 4. Test plan

- The RED test above, failing with the real sentence (`not served: … timed out`) before the fix.
- The characterization test run 20× in a loop on Windows; one macOS CI run green.
- Whole C# suite green.

## 5. Definition of Done

- [ ] RED observed with the real symptom, then GREEN.
- [ ] The rule (which deadline names an interrupted file) is stated in `SecuritySources.cs` and `module_server.md`.
- [ ] The characterization test no longer depends on two equal timers; the suite is ~30 s faster.
- [ ] The macOS leg green on the PR.
