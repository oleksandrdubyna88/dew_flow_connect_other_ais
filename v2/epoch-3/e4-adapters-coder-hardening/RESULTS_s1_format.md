# Epic e4-adapters-coder-hardening, story s1-measure-first, task t3 — the formatters, measured inside the check unit

Recorded 2026-11-26 on the WSL2 box of E2 (see `RESULTS_s1_dotnet.md` for the machine and for what "inside the unit" means: the product's own `CheckBoundary` and `BoundaryJob`, no property changed, one unit per command, the preflight held). Harness:
`src_mcp/tests/AFormattersAreMeasuredTests.cs` on `src_mcp/tests/MeasureBed.cs`; run as `COAI_RECORD_FORMAT=/home/jinx/coai-t3/e4/run-format ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class '*AFormattersAreMeasuredTests'`. Subject: a clone of this repository at `436943f3` for
`dotnet format`; a scratch project (prettier, npm 11.20.0, node v26.12.1, a COPY of node in the stand-in home) for prettier. Every command ran with ONLY a loopback device (`PrivateNetwork=yes`), after a restore on the host network (`dotnet restore` of the solution; `npm ci --ignore-scripts`). No model.

## Setup (so that the numbers mean something)

- .NET: three files of `src_mcp/core/Toolchain/` (`CheckResult.cs`, `MsBuildLines.cs`, `ToolchainTables.cs`) and a fourth, `FailureDigest.cs`, were made badly formatted by the harness OUTSIDE the unit (trailing white space on line 3, a tab for the indentation of line 5) and committed; the three are given to
  the formatter with `--include`, the fourth is not. The repository's `.editorconfig` has `trim_trailing_whitespace = true` and `end_of_line = lf`, and its style preferences at `suggestion` severity (its own comment says so).
- Node: `app/` has `a.js`, `b.js` (badly formatted), `c.json`, `broken.js` (a syntax error), `notes.xyz` (no language), `--help` (a file with that name, no extension), `--help.js`, and `untouched.js`; prettier `*` resolved to the current release (see the lock of the scratch run); `.prettierrc` is `{"singleQuote": true}`.
- Prediction (SPEC §A3, before the run): `dotnet format whitespace` in a `--folder` form changes only the included files and is quick; the `style` form needs the solution and may change nothing in this repository; `npx --no-install prettier --write --ignore-unknown {file}` with `./` before a file name keeps a name that looks like an option a file.

## M7 — `dotnet format`

Each form is run from the repository root, with `--include` of the three files, in a unit with only a loopback device, then a SECOND time on its own result.

| Form | Exit | Time (1st / 2nd run) | Files changed | Second run |
|---|---|---|---|---|
| `dotnet format whitespace . --folder --include <3 files>` | 0 | **1.8 s / 1.7 s** | exactly the three included files; `FailureDigest.cs` untouched | the diff against the commit is byte-identical to the first run's (hash `9622B312B708E581`) — nothing more changed |
| `dotnet format whitespace dew_flow_connect_other_ais.slnx --no-restore --include <3 files>` | 0 | 7.1 s / 6.9 s | the same three | identical (same hash) |
| `dotnet format style dew_flow_connect_other_ais.slnx --no-restore --include <3 files>` | 0 | 10.2 s / 10.0 s | **none** (the style preferences of this repository are `suggestion`) | nothing |
| the same with `--severity info` | 0 | 11.2 s / 10.8 s | **none** | nothing |
| `dotnet format whitespace . --folder --include ./<3 files>` (each name written `./<path>`) | 0 | **1.7 s / 1.6 s** | exactly the three; the same bytes as the form without `./` (the diff hash is the same, `9622B312B708E581`) | identical |
| `dotnet format whitespace . --folder --verify-no-changes --include <3 files>` on the badly formatted state | **2** | 1.6 s | none (a check, not a fix); stderr: `<path>(5,1): error WHITESPACE: Fix whitespace formatting. Delete 1 characters. [.]` one line per fault | — |

- `dotnet format --version`: `10.0.114-servicing.26522.126+0427b9abbead3fa64c6655eb4290c537397131fa`. The `--folder` form needs no restore and no solution: it read the files and the `.editorconfig` and nothing else. It worked with only a loopback device and wrote nothing outside the worktree
  (`git status` after each form shows only ` M` for the files above; the stand-in home is unchanged; the unit has no other writable place).
- `--include` takes the paths RELATIVE to the working folder, several after one flag; a file that is not named is not read (the fourth file stayed byte-identical in every form).
- The whitespace form ignores the style rules: it changed 3 files and no `using` order, no naming, no `var` preference. That is the formatter a person means by "format", and it is the one that cannot change what the code does.

**Default for the `dotnet` adapter: `dotnet format whitespace . --folder --include {file}`** (the working folder is the repository root, an item of `{file}` is `./<path>`, as SPEC §D2 says: measured with both spellings, same files and same bytes).
Only with an `.editorconfig` at the root (the repository has the rules the formatter reads). Longest time measured for the default form with three files: **1.8 s**. `style` is not a default: it changes nothing here and takes 10 s to 11 s. The solution form is not a default: slower and no different.

## M8 — prettier through `npx --no-install`

Inside the unit, `app/` as the working folder, `npx --no-install prettier --write --ignore-unknown <files>`, private network.

| Case | Exit | Time | Seen |
|---|---|---|---|
| `./a.js ./b.js ./c.json` | 0 | **0.5 s** | `a.js 34ms`, `b.js 4ms`, `c.json 3ms` on stdout; exactly those three changed (` M` in `git status`; `untouched.js` and the others not) |
| the same, second run | 0 | 0.4 s | `(unchanged)` for each; the diff is byte-identical (hash `85EF0EB93793D01B`) |
| `./broken.js` | **2** | 0.4 s | stderr: `[error] broken.js: SyntaxError: Unexpected token (1:7)` + a code frame |
| `./a.js ./b.js ./c.json ./broken.js` | **2** | 0.5 s | the three are processed (`(unchanged)` here), the broken one reported: **one broken file does not stop the others and the exit code is 2** |
| `./notes.xyz` with `--ignore-unknown` | 0 | 0.4 s | silently skipped |
| `./notes.xyz` without it | 2 | 0.4 s | `[error] No parser could be inferred for file "<path>/notes.xyz".` |
| `./--help` (a file of that name, no extension) | 0 | 0.4 s | **read as a file, not as an option** — and skipped, because it has no extension and `--ignore-unknown` is on; the file is unchanged |
| `./--help.js` | 0 | 0.4 s | read as a file and formatted (`const helpjs = { x: 1 };`) |
| for contrast: `--help` bare | 0 | 0.4 s | the usage text: the name IS an option without `./` |
| for contrast: `--help.js` bare | 1 | 0.4 s | `[error] Invalid --help value. Expected a flag, but received { js: true }.` — the bare name is parsed as `--help` with a value |
| for contrast: `-- --help` | 0 | 0.4 s | exit 0 (what it did with the name was not read) |

**Default for the `node` adapter: `npx --no-install prettier --write --ignore-unknown {file}`**, `{file}` as `./<path>` — only when prettier is a devDependency and has a configuration (the SPEC's rule), after `npm ci`. Longest time for three files: **0.5 s**. The `./` form is what makes a file named `--help.js` a file; it is measured here, and a file with no extension is skipped by the tool itself, so the list of extensions of the adapter is a second guard.

## What this does not settle

- Only whitespace faults were injected for .NET (trailing white space, a tab): `dotnet format whitespace` also fixes line endings, final newlines and brace layout per `.editorconfig`; not measured one by one. A formatted file that does not compile (a syntax error in a `.cs` file given to `dotnet format`) was NOT measured — prettier's behaviour on a syntax error was.
- One prettier version and one configuration (`singleQuote`); plugins, `.prettierignore`, `overrides` and the `package.json` `prettier` member were not run.
- A file name with a space, a control character or a line break was not given to either tool (the SPEC leaves them out before the argv, §D2).

## Files

Recorded consoles: `src_mcp/tests/fixtures/toolchain/dotnet-format-f1*.txt` and `prettier-*.txt`. Raw logs and `RESULT.txt` of both beds: `/home/jinx/coai-t3/e4/run-format/` (not committed).
