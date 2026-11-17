# SPEC — story S1 (cornerstone): the agy hook allows a reader only inside the folders it was given

> Status: **IMPLEMENTED, 2026-11-17** (shipped in #1006, written by Fable before its code). Parent plan:
> [PLAN_the_agy_hook_checks_the_path.md](PLAN_the_agy_hook_checks_the_path.md) (`## Stories`, S1). Spec by Fable, code by
> Sonnet, review by Opus. Scope: `AntigravityReadOnly.Decide`, its tests in `src_mcp`, and the server binary test.
>
> **Deviation from this text.** `Decide` ships as `Decide(string payload, EntryReader? read = null, TextWriter? log = null)`, and the last-resort catch of section 1 ("Total") covers the WHOLE of `Decide`, not D2–D7 alone: a lone UTF-16 surrogate in `toolCall.name` threw outside it. That one case is denied with `DenyReason`, not `DenyOutsideReason`; D2–D7 keep `DenyOutsideReason`. Each catch writes one stderr line naming the exception type only. The parent plan's status line has the whole record.

The coder re-discovers nothing: this file is the brief. Where it and the plan differ, section 7 says so.

## 1. The decision rule

Input: the `PreToolUse` payload text. Output: `{"decision":"allow"}` or `{"decision":"deny","reason":…}`.
The call is allowed only when every step passes. Steps D2–D7 are one conjunction; their order does not change the
outcome, and every one of them denies with the SAME reason (section 2).

| Step | What must hold | On failure |
|---|---|---|
| D0 parse | The payload is one JSON object. Duplicate property names are refused at parse (`JsonDocumentOptions.AllowDuplicateProperties = false`, .NET 10). | deny, tool-name reason |
| D1 name | `toolCall.name` is a JSON string and is in `HookAllowedTools`, compared `Ordinal`. Unchanged. | deny, tool-name reason |
| D2 path text | `toolCall.args` is an object. Exactly ONE of its property names equals `Path` when case is ignored, and that one is spelled `Path`. Its value is a JSON string and `Path.IsPathFullyQualified(value)` is true. | deny, path reason |
| D3 cwd | `cwd` is a JSON string, fully qualified, and `RealPath.Resolve(cwd, read: read)` is `Ok`. | deny, path reason |
| D4 roots | `workspacePaths` is a JSON array. EVERY element is a JSON string, fully qualified, and `RealPath.Resolve(entry, read: read)` is `Ok`. One bad element denies the call, even beside a valid root. | deny, path reason |
| D5 target | `RealPath.Resolve(path, read: read)` is `Ok`. | deny, path reason |
| D6 not the coai folder | `RealPath.IsInside(cwd.Path, target.Path, Comparison)` is FALSE. | deny, path reason |
| D7 inside a root | `RealPath.IsInside(root.Path, target.Path, Comparison)` is true for at least one resolved entry of D4. | deny, path reason |

Why each choice:

- **`RealPath.Resolve` for all three (target, every root, cwd).** agy opens the file and follows the last name too, so the
  target needs the full resolve. `ResolveParent` keeps the last name unresolved: a link as the leaf would pass. `ResolveBelow`
  refuses a link leaf and the root itself: an honest link inside the root would fail. Roots and cwd are resolved the same way
  as the target, or a root spelled through a link never contains its own files (gate finding a).
- **No base directory.** D2–D4 refuse anything not fully qualified BEFORE the resolver runs. So the hook process's cwd
  (`<coai folder>/.agents`) takes no part. On Windows `\x` and `C:x` are not fully qualified and are denied.
- **D6 is the plan's "roots minus cwd", on resolved paths.** There is no separate removal step. An entry equal to cwd admits
  only paths inside cwd, and D6 denies those. D6 also holds when a granted root CONTAINS the coai folder. cwd missing or
  unresolvable denies everything (gate finding a).
- **Comparison: `DocumentId.Comparison`** (`src_mcp/core/Rounds/DocumentId.cs:32`): `Ordinal`, and `OrdinalIgnoreCase` on
  Windows. It is the one `WorkspaceLookup.Contained` uses for the same granted roots, so "listed by coai" and "readable by
  agy" agree. Do not write a new comparison. `IsInside` compares root plus a separator, never a bare prefix.
- **D2, one spelling of `Path`.** `args` is the only model-written object the rule reads. If agy matches argument names
  without case, `{"Path": inside, "path": outside}` would be judged on one value and read on the other. Two spellings is a
  payload this code does not understand: deny. An honest call has exactly one `Path`.
- **A missing last name inside a root is allowed.** `RealPath` resolves a missing suffix as written. The hook judges WHERE,
  not WHETHER: agy then reports the missing file itself (its wording is not measured), which is the true cause. A `..` after
  a missing name fails in `RealPath` and is denied.
- **A path equal to a root is allowed.** `IsInside` is true for equality, as in `WorkspaceLookup`. A directory holds no text
  for a file reader, and whatever agy does with it is inside the grant.
- **Total.** `Decide` never throws, for any text and any `read`. D0–D1 keep today's catch and add `ArgumentException`.
  D2–D7 run under one last-resort `catch (Exception)` that answers deny with the path reason. Reason: a handler that answers
  nothing let a write through on Windows (RESULTS_agy_write_block.md). This is the process edge, the one place a catch-all
  belongs.

## 2. The deny reasons

- `DenyReason` — unchanged: `coai: this consultation is read-only; only view_file or read_file runs`. For D0 and D1.
- `DenyOutsideReason` — new: `coai: this consultation reads only inside the folders it was given`. For D2–D7 and the
  last-resort catch.

The reason is a constant. It never carries the model's path, a root, the cwd, or a `RealPathResult.Why` (that text quotes
the path).

## 3. The API shape

```csharp
public const string DenyOutsideReason = "coai: this consultation reads only inside the folders it was given";

/// read: how one entry is looked at; the real file system when null (the seam RealPath.Resolve has).
public static string Decide(string payload, EntryReader? read = null)
```

- `Decide(string payload)` stays the entry point: `RunHook` calls it with no reader. `RunHook`, `HookAllowedTools`,
  `DenyReason`, the four files and both `Program.cs` routings do not change.
- The reader is passed to every `RealPath.Resolve` call and used for nothing else.
- Constraints: `RealPath` is the only resolver. No `ResolveLinkTarget`, no `.LinkTarget`, no `GetFullPath(` with
  `StartsWith(` in the file (`TheLinkResolverIsOneRoadTests`). Methods stay at cyclomatic complexity 4 or less: one small
  `private static` method per step. Failures inside are values (`RealPathResult`, a bool), not exceptions.
- Update the class remarks and the `Decide` summary: the hook now checks the place too. No other production file changes.

## 4. Test scenarios

New file `src_mcp/tests/TheAgyHookReadsOnlyInsideItsRootsTests.cs`, one class, `IDisposable`.

**Fixture** (`TempDir.For("coai-agy-hook-paths-")`, called `W`; real folders and files):

```
W/home/.agents/hooks.json   the payload's cwd is W/home (it stands for the coai folder)
W/job-1/inside.txt          root A      W/job-1/secret.txt   an inside decoy      W/job-1/sub/deep.txt
W/job-10/secret.txt         a sibling job, never a root (prefix sibling of job-1)
W/second/b.txt              root B
W/outside/secret.txt        W/outside/deeper/x.txt
```

**Helpers.**
- `Call(path, tool = "read_file", cwd = W/home, roots = [W/job-1, W/second])` builds the payload with `JsonObject`, never
  by string interpolation (a Windows path has backslashes). Shape as captured on agy 1.5.9: `cwd`, `hookEventName`,
  `toolCall { args { Path }, id, name }`, `workspacePaths = [cwd, ..roots]`.
- `Allowed(payload)`: the decision is `allow`.
- `Denied(payload, control)`: FIRST the control payload is `allow`, THEN the payload is `deny` and its reason equals
  `DenyOutsideReason`. The control is the same call with the one thing under test repaired (default: `W/job-1/inside.txt`
  with the same cwd and roots). It proves the fixture is one the rule accepts, so a deny is not a rejected fixture.
- `Link(at, text)`: `File.CreateSymbolicLink`, and `Assert.Skip("this machine cannot create a symbolic link: …")` on
  `IOException` / `UnauthorizedAccessException`, as in `ALinkCannotCarryTheReaderOutsideTests.cs:50-60`. Rows marked [L]
  use it: they run on Unix and on a Windows machine that may create links, and skip elsewhere. A link to a folder uses
  `Directory.CreateSymbolicLink`. Each [L] row is its own `[Fact]`, so a skip hides nothing else.
- A stand-in reader (B22, B24, B25) wraps `RealPath.Lstat` and changes ONE entry, matched by its last name: the resolver
  hands it the path resolved so far, which is not always the spelling the test wrote (macOS: `/var` is a link).
- Tool names come from `AntigravityReadOnly.HookAllowedTools`, not a typed list. No test changes the process directory.

"Today" is the result against today's `Decide` (it allows every reader call).

| # | Setup beyond the fixture | Path (or payload change) | Expect | Today |
|---|---|---|---|---|
| A1 | — , for each allowed tool name | `W/job-1/inside.txt` | allow | pass |
| A2 | — , for each allowed tool name | `W/second/b.txt` (the second root) | allow | pass |
| A3 | — | `W/job-1/sub/deep.txt`; `W/job-1/sub/../inside.txt` | allow | pass |
| A4 [L] | `job-1/in-link` → `inside.txt`; `job-1/abs-link` → `W/job-1/sub/deep.txt` | each link | allow | pass |
| A5 [L] | `W/via` → `W`; roots = [`W/via/job-1`] | `W/via/job-1/inside.txt`; `W/job-1/inside.txt` | allow | pass |
| A6 | — | `W/job-1/not-there.txt`; `W/job-1/no-dir/x.txt` | allow (decided) | pass |
| A7 | — | `W/job-1`; `W/job-1/` | allow (decided) | pass |
| B1 | — | `W/outside/secret.txt` | deny | FAIL |
| B2 | — | `W/job-10/secret.txt` (sibling prefix) | deny | FAIL |
| B3 | — | `W/job-1/../outside/secret.txt`; `W/job-1/sub/../../job-10/secret.txt` | deny | FAIL |
| B4 [L] | `job-1/leak` → `W/outside/secret.txt` | `W/job-1/leak` | deny | FAIL |
| B5 [L] | `job-1/dir` → `W/outside` | `W/job-1/dir/secret.txt` | deny | FAIL |
| B6 [L] | `job-1/l2` → `W/outside`; `job-1/alias` → `W/job-1/l2/secret.txt` | `W/job-1/alias` | deny | FAIL |
| B7 [L] | `job-1/l3` → `W/outside/deeper` | `W/job-1/l3/../secret.txt` (the decoy `job-1/secret.txt` exists) | deny | FAIL |
| B8 [L] | `job-1/gone` → `W/outside/nothing.txt` (dangling) | `W/job-1/gone` | deny | FAIL |
| B9 | — | `W/job-1/inside.txt/x`; `W/job-1/inside.txt/../secret.txt`; `W/job-1/nope/../inside.txt` | deny | FAIL |
| B10 | — | `W/home/.agents/hooks.json` | deny | FAIL |
| B11 | roots = [] (cwd is the only entry) | `W/home/.agents/hooks.json`; `W/job-1/inside.txt`. Control: default roots | deny | FAIL |
| B12 | roots = [`W`] (a root that contains the coai folder) | `W/home/.agents/hooks.json` deny; `W/job-10/secret.txt` ALLOW | both | FAIL |
| B13 [L] | `W/via` → `W`; cwd = `W/via/home`, entries = [`W/home`, `W/job-1`]; then the reverse spelling | `W/home/.agents/hooks.json` | deny | FAIL |
| B14 | — | `inside.txt`; `job-1/inside.txt`; `./inside.txt`; `../job-1/inside.txt`; on Windows also `\x` and `C:x` | deny | FAIL |
| B15 | — | `args` missing, null, a string; `Path` missing, null, a number, an array, an object, `""` | deny | FAIL |
| B16 | — | `Path` holds a NUL (`…/job-1/inside.txt\u0000`); `Path` longer than `RealPath.MaxPathChars` | deny, no throw | FAIL |
| B17 | `args` holds the keys shown | `{"Path": inside, "path": outside}`; `{"path": inside}`; `{"PATH": inside}` | deny | FAIL |
| B18 | raw JSON text | two `Path` properties (inside, then outside); two `workspacePaths` properties | deny (reason not asserted), no throw | FAIL |
| B19 | — | `cwd` missing, null, a number, `""`, relative, with a NUL — the Path is `W/job-1/inside.txt` | deny | FAIL |
| B20 | — | `workspacePaths` missing, null, a string, an object; an element that is a number, null, `""`, relative, with a NUL, beside `W/job-1` | deny | FAIL |
| B21 [L] | `W/loop-a` ↔ `W/loop-b`; entries = [`W/home`, `W/job-1`, `W/loop-a`] | `W/job-1/inside.txt` | deny | FAIL |
| B22 | `read` throws `IOException` for `W/second` only | `W/job-1/inside.txt` | deny | FAIL |
| B23 [L] | `job-1/loop-a` ↔ `job-1/loop-b`; a chain of 45 links ending at `inside.txt` | `W/job-1/loop-a`; the chain's first link | deny in under 5 s, no throw | FAIL |
| B24 | `read` answers `Entry.Link(W/outside/secret.txt)` for `W/job-1/stood-in`, real disk for the rest | `W/job-1/stood-in` | deny (a link on every machine) | FAIL |
| B25 | `read` throws `InvalidOperationException` | `W/job-1/inside.txt` | deny, `Decide` does not throw | FAIL |
| B26 | — | `W/JOB-1/inside.txt` | allow where `DocumentId.Comparison` ignores case, deny elsewhere | FAIL off Windows |
| B27 | a reader name, today's old payload: `{"toolCall":{"name":"read_file","args":{}}}` | — | deny, path reason (an older agy reads nothing) | FAIL |
| C1 | a perfectly valid inside call, tool = `write_file`, `write_to_file`, `run_command`, `Read_File`, `""` | `W/job-1/inside.txt` | deny, reason = `DenyReason` | pass |
| C2 | — | B1's reason | equals the literal in section 2; contains neither `W` nor `secret` | FAIL |
| C3 | `RunHook` with A1's payload, then B1's | — | exit 0 twice; `allow`, then `deny` | FAIL |

**Changes in `src_mcp/tests/AntigravityReadOnlyTests.cs`** (its `Payload(name)` has empty `args`, so a reader is now denied):
- Lines 43 and 45: the two `allow` assertions move to A1. Keep the non-reader loop, the unparsable payloads and the
  `DenyReason` text assertion. Add B27 here or in the new class, once.
- `TheBuiltBinary_AnswersTheHook_AsTheChildProcessAgyStarts` (lines 84-98) gets real payloads, built as `Call` builds them,
  over a small fixture under `_base`: `write_to_file` → deny; `view_file` and `read_file` inside → allow; `read_file` on the
  sibling by absolute path → deny with the path reason. One more: the child's working directory is the ROOT and the Path is
  `inside.txt` → deny. Only a child process can show that the process directory is not a base.

**Red, green, red.**
1. Stub: add `DenyOutsideReason` and the `read` parameter; the body stays today's. This only lets the tests compile.
2. RED: write every scenario, build, run the MTP executable (never `dotnet test`) with
   `--filter-class "*TheAgyHookReadsOnlyInsideItsRootsTests"`. Rows marked FAIL must fail with "expected deny, found allow"
   (C2: no path reason). Rows marked pass must pass: they are the positives that fail an over-strict rule. A row that fails
   for another cause (a setup error, an exception) is a broken test: fix the test first.
3. GREEN: implement section 1. All rows pass. Then the whole `CoaiMcp.Tests` and `CoaiServer.Tests`, in Release (CI's
   configuration), and the leftovers guard as CI runs it.
4. RED again, one step at a time, restoring after each. Record each observation in the PR:

| Remove | Must go red |
|---|---|
| D7 (allow every resolved target) | B1, B2, B3 |
| the separator in containment (bare prefix) | B2 |
| the resolver (compare the spelling) | B4, B5, B6, B7, B9, B24 |
| D6 | B10, B12 |
| D3's failure (treat an unreadable cwd as "no cwd") | B19 |
| D4's "every element" (skip a bad one) | B20, B21, B22 |
| D2's one-spelling rule | B17 |
| the fully-qualified test | the child-process case (working directory = the root, Path `inside.txt`), and B19, B20 for cwd and roots. B14 alone cannot: in-process a relative path lands under the test folder |
| the last-resort catch | B25 |

## 5. `src_server/tests/TheServerBinaryAnswersTheAgyHookTests.cs`

Today its payload is `{"toolCall":{"name":…,"args":{}}}`; `read_file` and `view_file` would become denials. The test keeps
its purpose: the BUILT `coai-server`, started as agy starts the hook, gives the same answers as `Decide`.

- Fixture under `_dir`: `home/.agents/`, `job-1/inside.txt`, `job-10/secret.txt`, `outside/secret.txt`.
- Payloads built with `JsonObject`: `cwd` = `home`, `workspacePaths` = [`home`, `job-1`], `toolCall.args.Path`.
- The child's working directory is `home/.agents`, as agy runs it. Keep the literal `--agy-hook`, the `ProcessLauncher`,
  the 60 s bound, and the exit code 0 and no-timeout assertions.

| Case | Tool | Path | Expect |
|---|---|---|---|
| S1 | `read_file`, `view_file` | `job-1/inside.txt` (absolute) | allow |
| S2 | `read_file` | `job-10/secret.txt` (absolute, a sibling job) | deny, reason = the path sentence as a LITERAL |
| S3 | `read_file` | `job-1/../outside/secret.txt` | deny, path sentence |
| S4 [L], its own `[Fact]` | `read_file` | `job-1/leak` → `outside/secret.txt` | deny, path sentence |
| S5 | `write_file`, `run_command` | `job-1/inside.txt` | deny, reason = the tool-name sentence as a literal |
| S6 | `read_file` | today's old payload, empty `args`, no roots | deny |

The reasons are literals here on purpose: the release step (plan, `## Stories`) looks for these sentences in agy's output.
Rename the test for the guarantee and update the class summary. `research/module_tests.md` names the old method: story S2.

## 6. Out of scope, and stated limits

- **A race.** A link swapped between the hook's check and agy's open is not closed. The check is at one moment.
- **Hard links and bind mounts.** No path check sees them (`RealPath` remarks, SECURITY.md).
- **Inside a root nothing is narrowed.** `.git` and credential-looking files in a granted root stay readable by agy's
  reader. `WorkspaceLookup` hides them from coai's own list and search; this hook does not.
- **Another argument that names a file.** Only `Path` is read. Whether any agy reader takes a second file argument is not
  measured. `view_file`'s argument name on agy before 1.5.9 is not measured: a call without `Path` is denied.
- **Older agy.** A payload without `Path`, `cwd` or `workspacePaths` fails closed: every read is denied (B27).
- **The roots are trusted.** Every entry of `workspacePaths` other than cwd is a root. agy builds it from coai's argv; if
  agy ever added a folder of its own, it would be a root.
- **Windows.** An 8.3 short name is a different string: a false refusal. Case follows `DocumentId.Comparison`: ignored. In
  a directory made case-sensitive, a sibling whose name differs only by case would be accepted. agy on Windows is not
  measured at all. macOS: a different case is a false refusal.
- **Hook denials are not in agy's `denied_actions`.** coai's read follow-up does not fire for them, as today.
- **agy's own checks still run after an allow.** The hook only narrows.
- **No answer is agy's to handle.** The hook always answers and exits 0. What agy does when a hook times out is not ours.

## 7. Where this spec differs from the plan's text

- D6 replaces "remove cwd from the roots" and adds: a path inside the coai folder is denied even under a wider root.
- D2's one-spelling rule and D0's duplicate refusal are additions. They deny only payloads no honest call produces.
- The scenarios live in a new test class, not in `AntigravityReadOnlyTests.cs` (file size, one guarantee per class).
- The plan's Windows limit says a different case is denied. With the comparison the plan itself names, it is accepted.
- The plan's test list has no missing-leaf and no root-itself case. Both are decided here (A6, A7).
