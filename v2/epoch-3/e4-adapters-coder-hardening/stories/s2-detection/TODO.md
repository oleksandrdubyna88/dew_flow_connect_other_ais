# s2-detection — what was built tonight (2026-11-26) and what each dod item still owes

Built and tested: t1 (`shared/toolchains.json`), t2 (`ManifestsAtCommit`), t3 (`DotnetDetector`), t4 (`NodeDetector`), t5 (`ToolchainResolve`), t6 (golden trees). The full MCP suite was green at each push. Nothing of it is wired into `ChecksAtBase` yet (that is s3).

Items that do not read exactly as the dod says, or that a reviewer must look at:

1. **t6 d3 / D10: this repository's own HEAD gives TWO node projects, not one.** The dod says "one dotnet unit ... and the node project `src_vs_code` with npm". Detection forced on this repository's HEAD gives that unit and `src_vs_code/package.json` (typecheck and test steps), and also `http/package.json` (the `.http` contract suite: a lock file, no `build`, `typecheck` or `test` script), which is listed as `unchecked` with no step, as SPEC §A3 says. The test (`ADetectionIsGoldenTests.D10_…`) asserts the unit and `src_vs_code`, and that every node project is npm; it does not assert that `http` is absent.
2. **t5 d4 and the plan gate's first finding: the compare of the CHECK DEFINITIONS is not built** (it is s3 t5, as the dod of t5 says). `ToolchainResolve` lists a changed fact in `Differences` and judges none; a lost step is refused. Until s3 t5 lands, a head that replaces `scripts.test` with `echo passed` is accepted by `ToolchainResolve` (the step still exists). D12 and D13 of the SPEC are therefore NOT covered by a test yet.
3. **t2: the path rule (control character, absolute, drive, `..`) is applied to the paths of the files the search selects, not to every path of the tree.** A repository with one odd file name elsewhere would otherwise be refused for something no manifest reads. A manifest path that git lists is never `..` or absolute (git refuses to store it), so the `..` and absolute cases are tested on the pure `ManifestSearch.PathProblem` and `Select`, not through git.
4. **t3: only the root `global.json` counts for the MTP opt-in.** Story s1 measured that the SDK, and with it the runner, follows the working folder, and every .NET step runs from the repository root. A `global.json` in a unit's own folder is read as a definition file (the compare of s3 t5 will see it) but does not opt the unit in.
5. **A renamed folder that holds a test project is refused as a lost step** (the step's subject is the project path): `renames-a-folder` of the vstest and MTP trees. The sentence names the cure; the refusal is conservative by construction.
6. **Workspace members** are the packages the patterns name (a folder, one `*` in a segment, `**`, a negated pattern). A segment with two stars does not match, so such a package is a project of its own rather than hidden.
7. **The goldens were generated from the detector's output and then read** against SPEC §A and the measured facts; they are a regression net. `COAI_GOLDEN_OUT` regenerates them.

## The shoulds of the Fable security verify of s2 (2026-11-26, f7fd401b) — state after the must-changes

M1-M5 and S1 are fixed (commits 7d0a83e7, 1bff1660; tests `ADetectionHardeningTests`, `AManifestsAtACommitTests`). The others, by the report's numbers:

- **S1** met: `GitHistory.Hardened` and `ForChecks()`; `ManifestsAtCommit` uses them. `ChecksAtBase` still reads the base toolchain with the plain reader: s3 t3 switches it.
- **S2** (XML depth, `element.Value` recursion on a 1 MiB stack): not met. A depth limit of 64 while loading is a small change; not made.
- **S3** met for the parts that touch the detector: a path that holds U+FFFD or another unusual character is refused, and a path listed twice is refused (`ManifestSearch.Select`).
- **S4** met: the inert `MaxCharactersFromEntities = 0` line is gone.
- **S5** not met: a submodule or a note that only the head has is named nowhere (`ToolchainResolve` takes the base's notes).
- **S6** met: names are matched without case and an odd spelling of a manifest or definition name is refused (`S6_*`).
- **S7** not met (memory of a 32 MiB listing: one string per record). **S8** not met (no deadline for one tree: up to 502 git processes, each with 30 s).
- **S9** not met: the BOM and `encoding=` questions are not measured; the s3 t5 compare uses object ids for the files that are whole definitions (`global.json`, the configuration files).
- **S10** open for the plan owner: see the limits in the todo of s3.
