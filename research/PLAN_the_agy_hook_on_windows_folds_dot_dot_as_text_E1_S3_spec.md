# SPEC — E1.S3: no dot segment where text folds, the documents, and the real Windows run (W1, gate findings 3 and 4)

> Status: **spec written 2026-11-18; its story is IMPLEMENTED, 2026-11-24** — E1 (S1 W3, S2 W2, S3 W1) shipped in #1023 (merge 9721a7fe, 2026-11-24); released in mcp 0.66.1 (2026-11-24) and server 0.16.4 (#1042, 2026-11-24, release run 45412033457, 12 assets). The check with the release `coai-server` 0.16.4 binary on the host passed on 2026-11-24 (agy 1.5.13): the in-root read was allowed and its marker read; the other job's file by absolute path, by `..` and through a link inside the root was each denied with the contract sentence, and the secret was never read. **Not deployed:** on 2026-11-25 the owner chose to skip the deploy, so the Team server still serves 0.16.3. W3 does not hit today's server (its job roots sort after the hook folder), so 0.16.3 stays safe there. Parent:
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md) (story S3), of
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md) (W1, option A).
> A security story: spec by Fable, code by Sonnet, review by Opus. It builds on S1 and S2. Not the cornerstone.

The coder re-discovers nothing: this file is the brief.

## 1. The defect

`RealPath` applies `..` to the path resolved so far (`Up`, `RealPath.cs:328`): the Unix rule. Win32 folds `.` and `..` as text
before it follows any link. With a junction `R\nm` → `R\deep\a\b`, the path `R\nm\..\..\outside\secret.txt` is judged as
`R\deep\outside\secret.txt`, inside `R`, and opened by Windows as `<parent of R>\outside\secret.txt`. The real hook allowed such a
path on 2026-11-18 (the plan, "The Windows measurement of step 1").

**What Windows folds, measured 2026-11-18** on this machine's Windows host (build 10.0.26300, `[IO.Path]::GetFullPath` in Windows
PowerShell 5.1, which calls `GetFullPathName`):

| Spelling | Windows makes it | So |
|---|---|---|
| `C:\Windows\..\System32` | `C:\System32` | an exact `..` leaves its folder |
| `C:\Windows\.. \System32` | unchanged | `.. ` in the middle is a name |
| `C:\Windows\.. ` at the end | `C:\Windows\` | trimmed to the folder itself |
| `C:\Windows\...\System32` | unchanged | a name |
| `C:\Windows\.. .\System32` | `C:\Windows\.. \System32` | one dot taken off; still a name |
| `C:\Windows\System32.\drivers`, `…\drivers.`, `…\drivers ` | `C:\Windows\System32\drivers` | another spelling of the same entry |

Only a segment that is exactly `..` leaves its folder. The other spellings stay in the folder they are written in, so they cannot
carry a path out of a root. That is why the plan's option A, as written, is enough.

## 2. The rule (step D10)

Where `site.FoldsDotsAsText` is true, a `Path` with a segment that is exactly `.` or `..` is denied with the path reason.

- **`AgyHookPlaces.HasDotSegment(text)`**: split the text on `\` and on `/`; true when any piece equals `.` or `..` (ordinal).
  Text only. It does not ask which OS it runs on: the site says whether it is consulted.
- **Where.** On the text of `Path`, after D2 (one `Path`, a string, fully qualified) and before anything is resolved. So gate
  finding 4 holds: a relative path keeps its own denial from D2, and this rule sees fully qualified paths only.
- **`.` too.** A lone `.` is harmless: Windows and the walk both skip it. It is denied with `..` because the plan's option A says
  so, and one rule is easier to state. The model sent neither in 11 measured runs.
- **`Path` only.** Roots, the payload's `cwd` and the coai folder are text from coai and agy, never from the model. Row T5 pins it.
- **Not a link's own text.** The kernel applies a relative link's `..` to the path it has already resolved, which is what the
  walk does. `RealPath`'s `..` rule does not change (the plan: other sites rely on it). *(Superseded 2026-11-18 by the Opus review of E1: on Windows NT reads a relative link's text with `..` folded, so this is a probable escape, not measured. Follow-up: [PLAN_the_agy_hook_on_windows_judges_a_links_dot_text.md](../todo/PLAN_the_agy_hook_on_windows_judges_a_links_dot_text.md); see `SECURITY.md` and `RESULTS_agy_read_file.md` §7.)*
- **`HookSite.OfThisProcess()`** passes `OperatingSystem.IsWindows()`.
- **Off Windows nothing changes.** There the kernel is the walk.

```csharp
public sealed record HookSite(string? CoaiFolder, EntryReader Read, Func<string, bool> IsLocal, bool FoldsDotsAsText)
internal static bool HasDotSegment(string text);   // AgyHookPlaces
```

## 3. Test scenarios

New class `src_mcp/tests/TheAgyHookRefusesDotSegmentsWhereTextFoldsTests.cs` on the shared fixture. It gains
`job-1/sub/a/b/x.txt`, `job-1/.hidden`, `job-1/..x/f.txt` and `job-1/x..y.txt`. `Site(…)` takes `folds`; its default is this OS.
Because the flag is an input, both arms run on every machine. "Today" is the branch after S2.

| # | Setup beyond the fixture | Path | Expect | Today |
|---|---|---|---|---|
| T1 | the reader answers `Entry.Link(W/job-1/sub/a/b)` for `W/job-1/jl`; `folds` true | `W/job-1/jl/../../outside/secret.txt` | deny. Control: `W/job-1/jl/x.txt` is allowed | FAIL (allow) |
| T2 | as T1; `folds` false | the same | allow: off Windows the walk's verdict stands (`W/job-1/sub/outside/secret.txt`, a missing name inside the root) | pass |
| T3 | `folds` true, then false | `W/job-1/sub/../inside.txt`; `W/job-1/./inside.txt` | deny when `folds`, allow when not. The plan's "pure-text case": stated, so it cannot flip unseen | FAIL (the deny) |
| T4 | `folds` true | `W/job-1/.hidden`; `W/job-1/..x/f.txt`; `W/job-1/x..y.txt` | allow: a name with dots is no dot segment | pass |
| T5 | `folds` true; entries [`W/home`, `W/job-1/sub/..`] | `W/job-1/inside.txt` | allow: the rule reads `Path` only | pass |
| T6 | theory, `HasDotSegment` | true: `C:\a\..\b`, `C:\a\.\b`, `C:/a/../b`, `C:\a\..`, `C:\a\.`, `C:\a\..\`, `/a/../b`. False: `C:\a\b.txt`, `C:\..a\b`, `C:\a..\b`, `C:\...\b`, `C:\.. \b`, `C:\a\.hidden`, `C:\a\b.` | as listed | FAIL |
| T7 | `HookSite.OfThisProcess()` | — | `FoldsDotsAsText` equals `OperatingSystem.IsWindows()` | pass |
| T8 [W] | Windows only (`Assert.SkipUnless`). A real junction `W\job-1\nm` → `W\job-1\sub\a\b` by `Junctions.Shell.MakeLink`; the real reader; the site of this OS | `W\job-1\nm\..\..\outside\secret.txt` | FIRST the premise: `File.ReadAllText` of that path gives the text of `outside/secret.txt`, so Windows does read outside the root. THEN deny. Control: `W\job-1\nm\x.txt` is allowed | FAIL (allow) |

In T1, folded as text the path is `W/outside/secret.txt`, which exists and is outside every root.

**Existing rows.** A3 keeps its deeper file; its `sub/../inside.txt` moves to T3. B3, B7 and B9 still deny on every OS; on
Windows the dot rule now denies them before the resolver. B14 is unchanged: D2 comes first.

**The built binaries.** One case each (M6 in the mcp test, S11 in the server test): the child in `home/.agents`, the default payload,
Path `job-1/sub/../inside.txt` → allow off Windows, deny with the path reason on Windows. It shows the real flag reaches the rule.

## 4. Red, green, red

1. **Stub.** Add the member (the product passes this OS; the rule ignores it) and `HasDotSegment` returning false.
2. **RED.** Rows marked FAIL fail with "expected deny, found allow" (T6: wrong answer). The others pass.
3. **GREEN.** Implement section 2. Both test projects in Release, on Linux and on the Windows job. The leftovers guard.
4. **RED again**, one change at a time, restored after each. Record each observation in the pull request.

| Change back | Must go red |
|---|---|
| D10 removed | T1, T3 (the deny); T8, M6 and S11 on the Windows job |
| D10 applied whatever `FoldsDotsAsText` says | T2, T3 (the allow); M6 and S11 off Windows |
| `HasDotSegment` splits on one separator only | T6 |
| `HasDotSegment` matches a name that starts or ends with dots | T4, T6 |
| D10 reads the roots too | T5 |
| `OfThisProcess()` passes a constant | T7 on one OS or the other |

## 5. The real Windows run (gate finding 3)

A step of the build, not a unit test. Whoever runs it writes the result into the RESULTS file (section 6). The team lead runs it on
the owner's Windows host, with agy signed in and the branch's Release `coai-mcp.exe`.

**Setup.**
- The hook folder: let the branch binary prepare its own (`%LOCALAPPDATA%\coai-agy\h…`) with one agy launch. agy runs from it.
- `R` = `%TEMP%\coai-winprobe\R`, holding `inside.txt` (a marker), `deep\a\b\x.txt` (a marker) and the junction `R\nm` → `R\deep\a\b`
  (`mklink /J`). `…\Local\Temp\…` sorts before `…\Local\coai-agy\…`, so agy sends `cwd` = `R`.
- `%TEMP%\coai-winprobe\outside\secret.txt` (another marker). agy reads the temp folder by itself (measured), so only the hook
  stands in the way. This is the case the plan lists as not measured.
- One agy run per probe: stream mode, `--mode plan --agent coai-reader --add-dir R`, the prompt naming the exact path.

| Probe | Path | Expect |
|---|---|---|
| W-a | `R\inside.txt` | allowed, the marker read. This is W3 on Windows, and it shows where agy starts the hook there |
| W-b | `<hook folder>\.agents\hooks.json` | the hook's sentence |
| W-c | `R\nm\..\..\outside\secret.txt` | first the premise: `cmd /c type` of that path prints the outside marker. Then: `tool call denied by pre-tool hook: coai: this consultation reads only inside the folders it was given` in agy's output, no entry in agy's own `denied_actions`, the outside marker nowhere |
| W-d | `R\nm\x.txt` | allowed, the marker read: an honest junction still works |
| W-e | `\\<an address on the LAN that is not this machine>\share\x` | the hook's sentence. With a packet capture on TCP 445: no packet to that address while the branch hook answers. Control: the same payload replayed to the 0.66.0 hook shows packets, so the capture sees |
| W-f | `R\deep\..\inside.txt` | the hook's sentence: a `..` that stays inside is denied on Windows. Note what the model does next |

- If the model cleans a path itself, replay the payload to `cmd /c .\coai-hook.cmd` in the hook folder and say agy was not in the loop.
- Record: the Windows build, the agy version, the binary's commit, each `Path` as the payload carried it, the decision, the marker.
- **If it cannot be run**, or only in part: write what was run and what was not, and why. The Windows limits then stay in
  `SECURITY.md` with a dated note, and the parent's Definition of Done box says so.

## 6. The documents

| File | What changes |
|---|---|
| `research/RESULTS_agy_read_file.md` | New `## 7. Windows, and the hook's own folder`: the plan's "Windows measurement of step 1" carried over; the table of section 1; W3 with both captured payloads; steps D3, D6, D8, D9, D10 in short; S1's Linux run and this spec's Windows run. In §6, dated notes, the old text kept: at lines 121-123 (`workspacePaths` "holds the agy cwd first" and "`cwd` is the folder coai owns" were true only while the coai folder sorted first), at "The rule, in short", and at `### Windows, not closed here` (closed by §7) |
| `SECURITY.md`, the agy hook item (lines 55-82) | The rule: not inside the coai folder, which the hook knows from where it runs; the payload's `cwd` names nothing; only local paths are looked at; on Windows no `.` or `..` segment in `Path`. "Other than `cwd`" becomes "other than the coai folder". "agy on Windows is not measured" becomes what was measured. The item "Windows, not closed here" is replaced by a dated note. New limits: the five of the stories document, section 6 |
| `research/architecture.md:443` | "the coai folder (`cwd`)" becomes the folder the hook runs from; one sentence each for D9 and D10 |
| `research/module_tests.md` (the row of line 207) | The three new test classes and `AgyHookFixture` |
| `src_mcp/runners/Files/RealPath.cs:61` | The remark says the rule is the Unix rule: the parent a Unix kernel uses. Win32 folds `.` and `..` as text before any link is followed, so a caller that authorises a spelling for another program to open on Windows must refuse dot segments itself, as the agy hook does (`research/RESULTS_agy_read_file.md` §7). A comment only |
| the changelog, through the release pull request | (1) Fixed, every OS, since mcp 0.65.4 and server 0.16.3: agy could read nothing in a granted folder that sorts before coai's own folder, such as one under `~/.cache`, `/data`, `C:\Projects` or the Windows temp folder. (2) Windows: a `Path` with `.` or `..` is denied; a network or device path is never looked up. (3) Known: a checkout on a network drive, a UNC path or `\\wsl$` has agy's reads denied. The two Windows gaps noted at `src_vs_code/CHANGELOG.md:72` are closed |

The parent plan's Definition of Done is ticked box by box. Promotion to `research/` is the team lead's, with the deviations recorded.

## 7. Limits

- **A harmless `..` is lost on Windows.** The model can send the path again without it.
- **Other spellings of one entry** (a trailing dot or space, an 8.3 short name, a stream name) do not leave a root. Under a wide
  root they can name the coai folder's own files without being seen as inside it. Not closed here; the folder holds no secret.
- **A dot segment in a link's own text** is not judged (section 2). What NT does with an absolute link target that holds `..` is not
  measured; `mklink /J` does not write one.
- **Whether agy cleans a path before it opens it** is not measured. The rule does not depend on it.
- **One Windows build** was measured for section 1. Microsoft's own description of path normalisation says the same.

## Amendments from the cadence consultation (2026-11-18, consultation 54f49926, codex)

This repairs the spec so that it does what the plan says. It is not a plan change.

1. **The fallback replay runs from `.agents`.** The direct replay of a payload through the hook (`cmd /c .\coai-hook.cmd`) must run with
   its working folder set to `<hook folder>\.agents`, where the script lives. Run from the hook folder itself, the command is missing.
   Run by its full path from elsewhere, it gets S1's unknown-folder denial, which would look like a W1 denial. Replay an ALLOWED control
   first (an in-root `Path`), so that a later denial is known to be the W1 rule.
