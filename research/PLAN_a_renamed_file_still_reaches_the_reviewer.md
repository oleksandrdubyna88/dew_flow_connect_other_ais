# PLAN — a renamed file still reaches the reviewer

> Status: **IMPLEMENTED, 2026-09-11.** Story 1.3. Scope as built:
> `src_mcp/runners/Context/NumstatReader.cs` (new), `ContextAssembler.cs`.
>
> Related docs: [module_runners.md](module_runners.md) — *And it is a diff of files git NAMED*; and
> [PLAN_the_gate_diffs_from_a_moving_base.md](PLAN_the_gate_diffs_from_a_moving_base.md), the same class
> of defect one layer up.

## What shipped differently

1. **`BlobSize` needed no separate fix.** The plan called it a second defect; it already tries the new
   side first, and `{sha}:{newPath}` resolves the moment the path is a real one.
2. **`FileDiff` gained no `RenamedFrom`.** Nothing downstream needs it — the diff text carries the rename
   header itself — and a field on a core record used in a dozen places is not worth adding for nothing.
3. **The tab was the thing this plan nearly got wrong.** `-z` removes the need to escape the SEPARATOR,
   which is NUL; it says nothing about the two tabs dividing the counts from the path. Splitting on every
   tab truncated a legal Linux filename — and **the first version of the test asserted the truncated
   value as correct**, written from the parser instead of from the guarantee. Six reviewers across two
   vendors found it in one round, and codex framed it correctly as a way to hide a file from review.
4. **A truncated stream is refused, not trimmed.** Returning the records read so far hands the reviewer a
   diff shorter than the change and calls it the change.

## The symptom

`ContextAssembler.CollectAsync` runs `git diff --numstat` (`ContextAssembler.cs:84`), splits each line
on tabs (`:89-95`) and reuses the third column as a PATHSPEC for the per-file diff (`:102`). Two shapes
that column takes are not paths:

- a rename, which git prints as `src/{old.cs => new.cs}` (or `old => new` with no common prefix);
- any path with a byte outside ASCII, a quote, a backslash or a control character, which git prints
  C-QUOTED under the default `core.quotePath` — `"src/\321\204\320\260\320\271\320\273.cs"`.

Given either as a pathspec, `git diff` matches nothing and answers nothing, so `FileDiff.Text` is
empty: the reviewer sees the file NAMED in the list and no change under it, with no error anywhere.
In Fast mode there is nothing else to read. Reproduced on 2026-09-10 on a fixture repository: the
numstat carried `1 0 src/{old.cs => new.cs}` and `2 1 "src/\321\204…"`, the plain diff had 22
lines, and the per-file diff for each of the two columns had **0**. A refactor renames files; this is
the common case, not the exotic one.

The same site has a second latent defect the audit did not name: the OLD side of a renamed binary is
sized with `cat-file -s {base}:{path}` (`BlobSize`), and for a rename that path did not exist on the
base.

## The change

1. **`--numstat -z`.** Fields NUL-separated, paths never quoted, and a rename carried as an EMPTY path
   field followed by the old and the new path as two more fields. The parser reads that shape and
   nothing else; the human-readable form is gone from the code.
2. **A rename is diffed as one.** The per-file diff receives BOTH paths as pathspecs
   (`-- :(literal)old :(literal)new`), which is what lets git show the rename with its similarity and
   the edit under it, rather than a deletion and an unrelated addition.

   *As built (CodeRabbit, on the pull request — the plan described a shape that never shipped):* the
   old name lives on **`NumstatChange.RenamedFrom`**, not on `FileDiff`, which carries only `Path`,
   `Text`, `IsBinary` and `BinaryBytes`. `DiffShaper` therefore gates and elides by the name a
   reviewer will look for because that is the ONLY name it is given. And `BlobSize` never reads the
   old side: it asks `{sha}:{path}` and then `{baseRef}:{path}`, the NEW path both times, and answers
   `0` when neither resolves — a binary deleted on both sides of a rename chain still reaches the
   reviewer named.
3. Tests on a fixture repository, beside the ones `ContextAssemblerTests` already builds.

No growth surface.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `ARenamedAndEditedFileStillCarriesItsDiff` — rename plus an edit; the collected text names both paths and contains the edited line | RED today: `Text` is empty |
| 2 | `AFileNamedOutsideAsciiStillCarriesItsDiff` — a Cyrillic file name | RED today: empty |
| 3 | `APathWithASpaceAndAQuoteStillCarriesItsDiff` — the quote half runs where the filesystem allows the name (not Windows) and says so | the other quoting triggers |
| 4 | `ARenamedBinaryIsSizedByItsOldName` | the second defect |
| 5 | `ADeletedFileIsStillADeletion`, and the existing assembler tests | nothing else moved, including the merge-base resolution |

## Definition of Done

- [x] Tests 1–5 written, watched fail for the real symptom, passing — with two of them shipping under
      other names, for reasons in *What shipped differently*: test 3's quote half became
      `APathWithATabOrANewlineSurvivesWholeBecauseTheSeparatorIsNeither`, a parser test rather than a
      repository one, because the defect it caught is in the SPLIT and no filesystem is needed to show
      it; and test 4 is `ARenamedBinaryFileIsStillABinaryFile`, sized by the NEW name, since the old
      one was never what `BlobSize` asks for.
- [x] No pathspec is ever built from a quoted or `=>`-joined column; the only parser reads `-z`.
- [x] Every pathspec is additionally `:(literal)`-prefixed, so a filename that IS pathspec magic
      cannot decide which file's diff comes back. Found by CodeRabbit on this plan's own pull request
      — the third shape of the same defect, and the only one that returns somebody ELSE'S file rather
      than nothing.
- [x] `module_runners.md` — *And it is a diff of files git NAMED* — records the rename shape and why
      both paths travel.
