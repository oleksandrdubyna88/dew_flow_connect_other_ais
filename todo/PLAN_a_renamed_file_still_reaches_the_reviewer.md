# PLAN — a renamed file still reaches the reviewer

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope:
> `src_mcp/runners/Context/ContextAssembler.cs`, `src_mcp/core/Context/` (`FileDiff`). Finding 4 of
> [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md).
>
> Related docs: [module_runners.md](../research/module_runners.md) — *What a code round is a diff OF*;
> [PLAN_the_gate_diffs_from_a_moving_base.md](../research/PLAN_the_gate_diffs_from_a_moving_base.md),
> whose open tail (one git process per file) is unchanged by this.

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
2. **A rename is diffed as one.** The per-file diff receives BOTH paths as pathspecs (`-- old new`),
   which is what lets git show the rename with its similarity and the edit under it, rather than a
   deletion and an unrelated addition. `FileDiff` carries the new path as `Path` and the old one as
   `RenamedFrom` (empty otherwise), so `DiffShaper` gates and elides by the name a reviewer will look
   for; `BlobSize` reads the old side by the old name.
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

- [ ] Tests 1–5 written, watched fail for the real symptom, passing.
- [ ] No pathspec is ever built from a quoted or `=>`-joined column; the only parser reads `-z`.
- [ ] `module_runners.md` — *What a code round is a diff OF* — records the rename shape and why both paths travel.
