# PLAN — a structural check parses rather than greps

> Status: **plan only, nothing implemented yet. Tech debt, opened by operator ruling on 2026-09-17.**
> Scope: the two C# tests that assert on SOURCE TEXT — `src_bugs/tests/TheAdminKeysTests.cs:174` and
> `src_bugs/tests/TheDocblocksAreAttachedTests.cs:40` — and the rule that governs any future one.
>
> Related docs: [module_tests.md](../research/module_tests.md),
> [PLAN_who_holds_a_key.md](PLAN_who_holds_a_key.md) (the story that produced both tests),
> [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) (the TypeScript
> equivalent, which is a DIFFERENT problem — see *What this is not* below).

## The symptom

A test that reads source and asserts on the text reads the **comments** as well as the code. The
more carefully a guarantee is explained, the more likely a search for its violation matches the
explanation — so this fires hardest in exactly the files that are written well.

It happened twice on 2026-09-17, in one story, in the same test:

1. `source.Should().NotContain("FrozenSet")` guarded *"the credential store is an array, never a
   set"*. It went red immediately, because `src_bugs/src/AdminKeys.cs` contains a paragraph of
   `<remarks>` explaining **why it is not a `FrozenSet`** — a set's `Contains` is a hash and a probe,
   and both are data-dependent.
2. `body.Should().NotContain("return")` guarded the constant-time loop's *no early return*. It went
   red because the loop's own comment reads *"Accumulated, never returned from inside"*.

Both were patched at the time — the first by deleting the assertion and pinning the field
declaration instead, the second by stripping `//` lines before asserting
(`src_bugs/tests/TheAdminKeysTests.cs:191-196`). **Both patches are the same admission**: the test
wants to ask a question about code, and it is asking it of a character stream.

## Why a patch is not the fix

Stripping `//` lines is a lexer written in one line of LINQ, and it is wrong in the ways a one-line
lexer is always wrong:

- it does not strip `/* … */`, so a block comment still counts;
- it does not strip a trailing comment on a line of code (`found |= …; // never return here`), only
  whole comment lines;
- it cannot tell a `//` inside a string literal from a comment, so a URL in a string is treated as
  the start of one;
- and it does the opposite of what is wanted for `<c>return</c>` inside a doc comment, which is
  prose that LOOKS like code.

None of those is hypothetical for this codebase in particular — it carries more doc comment than
code in the files these tests read (`Corpus.cs` is 39 % comment lines) — and every one of them fails
in the same direction: a **false red** that the next person silences by weakening the assertion.
That is how a guard becomes decoration.

## The goal

A structural assertion asks a question about the **syntax tree**: does this method contain a
`ReturnStatementSyntax` inside its loop body; is this field's declared type an array; does this
member carry two documentation trivia blocks. Comments and doc comments are *trivia* to a parser,
and trivia is exactly what these tests must not see.

## What this is NOT

**It is not [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md).** That plan's
224 assertions are about a webview page that should be EXECUTED rather than read at all, because no
amount of parsing sees a control wired to the wrong branch. This plan is about the small number of
properties that genuinely cannot be observed by running anything — *this loop has no early exit*,
*this field is an array* — where reading the code is the only option and the only question is
whether you read it with a parser or with `IndexOf`.

**It does not add new structural tests.** The prohibition on new source-TEXT assertions
(`.coderabbit.yaml`, `.agents/PROJECT.md`) stands; this converts the two that exist.

## Build order

1. **Take the dependency.** `Microsoft.CodeAnalysis.CSharp` in the test project only, through
   `Directory.Packages.props` as central package management requires. Publisher is Microsoft and it
   releases continuously, so the NuGet rule allows it without asking — but **confirm the licence at
   the pin** (MIT at the time of writing) and record it beside the entry, per the licence-before-
   version rule. It must not reach `CoaiBugs.csproj`: the server is Native AOT and has no business
   carrying a compiler.
2. **One helper, `Parsed.cs` in `src_bugs/tests`.** Parses a file once into a `SyntaxTree` and
   answers the two questions the existing tests ask:
   - `Parsed.Method(file, name)` → the `MethodDeclarationSyntax`, so a test can walk it;
   - `Parsed.Members(file)` → each member with its leading documentation trivia, for the docblock
     guard.
   Cache per file per run; these tests read the same handful of files.
3. **Convert `TheComparisonWalksEveryHashAndLeavesOnlyAtTheEnd`.** It becomes: the method contains
   exactly one `ForEachStatementSyntax`; that statement's body contains no `ReturnStatementSyntax`,
   `BreakStatementSyntax` or `ContinueStatementSyntax`; it invokes
   `CryptographicOperations.FixedTimeEquals`; and `_hashes` is declared as an array type. The
   anchors-must-exist rule survives translation — a method that no longer parses to that shape fails
   loudly rather than passing vacuously.
4. **Convert `TheDocblocksAreAttachedTests`.** It becomes: no member carries two
   `DocumentationCommentTriviaSyntax` in its leading trivia. This is strictly stronger than the line
   scan, which cannot see a member whose two blocks are separated by an attribute.
5. **Delete the comment-stripping helper** and the `//`-aware reasoning around it, and say in
   `research/module_tests.md` what replaced it and why.

## Test plan

The break-provers are the point of this work, not a formality. Every one must be re-run after the
conversion, because a rewritten assertion is an unproven assertion:

- **Early return** — put `return new Presented.Administrator(found);` inside the loop. Red, naming
  the early exit. (This is the break that proved the original; it must still work.)
- **Early break and early continue** — each separately. Red.
- **The store becomes a set** — change `_hashes` to `FrozenSet<string>` and adjust the loop. Red on
  the declared type, not on the word.
- **A comparison that is not fixed-time** — `hash.SequenceEqual(wanted)`. Red.
- **PROSE IMMUNITY, which is the whole reason for the change**: write `return`, `break`, `continue`
  and `FrozenSet` into a comment and a doc comment inside the method, change no code, and the test
  stays **green**. Both of today's failures are this case; if it is not asserted, the next one is
  not caught either.
- **Two doc blocks separated by an attribute** — `/// <summary>a</summary>` `[Obsolete]`
  `/// <summary>b</summary>` on one member. Red, which the line scan cannot manage.
- The whole `CoaiBugs.Tests` suite, Debug and Release, since the test project gains a dependency.

## Definition of done

- [ ] `Microsoft.CodeAnalysis.CSharp` pinned in `Directory.Packages.props` with its licence recorded,
      referenced by the TEST project only, and absent from the AOT server's dependency graph.
- [ ] Both source-text tests parse instead of matching, and neither strips comments by hand.
- [ ] Every break in the test plan re-run and observed, including the prose-immunity case that stays
      green.
- [ ] `research/module_tests.md` says what these tests read and why it is a tree rather than a
      string, replacing the paragraph that currently explains the comment stripping.
- [ ] The two source-text tests that read NON-C# artefacts are left alone and the plan says so:
      `TheEdgeIsWatchedTests` reads an nginx vhost and `TheSchemaIsFrozenTests` reads a SQL fixture.
      Neither has a C# parser to reach for, and both are asserting on a file that is genuinely text.
