# SPEC — E1.S2 (CORNERSTONE): the agy hook looks only at local paths (W2, gate findings 1 and 2)

> Status: **spec written 2026-11-18; its story is IMPLEMENTED, 2026-11-24** — E1 (S1 W3, S2 W2, S3 W1) shipped in #1023 (merge 9721a7fe, 2026-11-24); released in mcp 0.66.1 (2026-11-24) and server 0.16.4 (#1042, 2026-11-24, release run 45412033457, 12 assets). The check with the release `coai-server` 0.16.4 binary on the host passed on 2026-11-24 (agy 1.5.13): the in-root read was allowed and its marker read; the other job's file by absolute path, by `..` and through a link inside the root was each denied with the contract sentence, and the secret was never read. **Not deployed:** on 2026-11-25 the owner chose to skip the deploy, so the Team server still serves 0.16.3. W3 does not hit today's server (its job roots sort after the hook folder), so 0.16.3 stays safe there. Parent:
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md) (story S2), of
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md) (W2, "Gate round 1").
> The cornerstone of the epic: spec by Fable, code by Sonnet, review by Opus. It builds on S1 (`HookSite`, `AgyHookPlaces`).

The coder re-discovers nothing: this file is the brief.

## 1. The defect

The hook resolves a path before it judges it. `RealPath.Resolve` looks at every entry on the way with `RealPath.Lstat`
(`RealPath.cs:93`, `File.GetAttributes`). For `\\attacker\share\x` that look is an SMB request, and Windows offers the user's NTLM
credentials to that host. The same look happens for a root, for the payload's `cwd`, and for the target of a junction inside a granted
local root. The decision is `deny` afterwards, so nothing shows that the look was made.

## 2. The rule (step D9)

**(a) One guarded reader.** Every `RealPath.Resolve` call of the hook gets `AgyHookPlaces.Guarded(site.Read, site.IsLocal)`.
For each path it is handed, the guard asks `isLocal(path)` FIRST. Not local: it throws `IOException` with a constant message that
holds no part of the path, and it does not call the inner reader. Local: it returns the inner reader's answer.
`RealPath` turns the `IOException` into a failed resolve (`RealPath.cs:122`), and the hook denies with the path reason.

**(b) Every resolved path is local.** A resolve that is `Ok` counts only when `site.IsLocal(result.Path)` is true. Otherwise it is
treated as not resolved. Reason: `RealPath` finishes a bare root with no look at all. A link whose text is `\\host\share` resolves to
that share and the reader is never asked (`Pushed`, `RealPath.cs:380`, then `Run` ends).

Both apply to all four things the hook resolves: `Path`, every `workspacePaths` entry, the payload's `cwd`, and the coai folder.
Through (a) they apply to every link target met on the way, because the walk hands each entry of a link's text to the same reader.

**What "local" is.** The test coai already has (`Junctions.IsLocal`, used by `GitScratch.Look`, `GitScratch.cs:149`), with the OS
given as an argument:

```csharp
// src_mcp/runners/Files/Junctions.cs — the two-argument overload (line 126) now calls this with OperatingSystem.IsWindows()
internal static bool IsLocal(string target, Func<string, DriveType> driveType, bool windows) =>
    windows ? IsLocalDrivePath(target, driveType) : IsLocalUnixPath(target);
```

- Windows (`IsLocalDrivePath`, `Junctions.cs:134`): a letter, a colon and a separator; not starting with `\\`; and the drive's
  type is not `Network`, `Unknown` or `NoRootDirectory`. So `\\host\share`, `//host/share`, `\\?\…`, `\\.\…`, `\\?\UNC\…`, `\??\…`
  and a mapped network drive are all refused. `\\?\C:\x` is refused too, although it is a local file: an honest call does not spell it.
- Unix (`IsLocalUnixPath`, `Junctions.cs:152`): rooted, not starting with `//`, not under `/net/`, `/misc/` or `/Network/`.

**No prefix is stripped.** A local junction's text reaches the reader as a plain drive path: .NET takes the `\??\` of the stored
target off. Evidence: `Junctions.ReparseBuffer` writes `\??\` + target (`Junctions.cs:315`) and the read-back compares equal to the
target (`Verified`, `Junctions.cs:339`); and on 2026-11-18 the real hook walked a real junction on Windows. Row N24 pins it.

**No separate check of the spelling.** Gate finding 1 allows a root-of-path comparison as a first cut. It is not built. Every
spelling it would refuse is refused by (a) at the first look or by (b) at the result. A third check that no test can see fail would
be a comment.

**Policy (gate finding 2).** A root, a `cwd` or a coai folder that is not local is not supported: the payload is denied whole, as any
unresolvable entry is (the parent rule's D4). No new deny sentence.

## 3. The API shape

```csharp
// src_mcp/runners/Reviewers/HookSite.cs
public sealed record HookSite(string? CoaiFolder, EntryReader Read, Func<string, bool> IsLocal)
// OfThisProcess(): IsLocal = text => Junctions.IsLocal(text, Junctions.Shell.DriveOf, OperatingSystem.IsWindows())

// src_mcp/runners/Reviewers/AgyHookPlaces.cs
internal static EntryReader Guarded(EntryReader inner, Func<string, bool> isLocal);
```

- `AgyHookPlaces` has ONE method that calls `RealPath.Resolve`, and it passes the guarded reader and applies (b). Everything the
  hook resolves goes through it.
- `AgyHookPlaces.cs` names no file-system type: no `File`, `Directory`, `FileInfo`, `DirectoryInfo`, `DriveInfo`, `Lstat`,
  `.LinkTarget` or `GetFullPath(`. `System.IO.Path`'s text functions are fine. The disk is reached only through `site.Read`, and the
  drive table only through `site.IsLocal`. `HookSite.OfThisProcess()` is the one place that names the real ones.
- If `site.IsLocal` or the reader throws anything else, the catch in `ReadsInside` denies, as today (row B25).

## 4. Test scenarios

New class `src_mcp/tests/TheAgyHookLooksOnlyAtLocalPathsTests.cs` on the shared fixture (`W/home` is the coai folder; the roots are
`W/job-1` and `W/second`).

**Helpers.**
- `Remote`: a place that is not local on THIS OS. Windows: `\\coai-no-such-host\share`. Unix: `/net/coai-no-such-host/share`.
- `Local(text)`: `Junctions.IsLocal(text, _ => DriveType.Fixed, OperatingSystem.IsWindows())`. The fixture's `Site(…)` takes it as
  the default `IsLocal`.
- `Recorder`: an `EntryReader` that appends every path it is handed to `Seen`. It NEVER touches the disk for a path that is not
  local: it answers `Entry.Missing`. On Unix it answers `Entry.Directory` for exactly `/net`, so the walk goes on to the host name
  (without that, a machine with no `/net` ends the walk early and the test sees nothing). For every other path it asks
  `RealPath.Lstat`, or the row's stand-in.
- `NoRemoteLook()`: `Seen` holds no path for which `Local` is false. Every row below asserts the decision AND `NoRemoteLook()`.
- The first test proves the recorder sees: for `W/job-1/inside.txt` the answer is allow and `Seen` is not empty.

"Today" is the result on the branch after S1.

| # | Setup beyond the fixture | Path | Expect | Today |
|---|---|---|---|---|
| N1 | — | `Remote/x.txt` | deny; no remote look | FAIL on the look (already deny) |
| N2 | entries gain `Remote` | `Remote/x.txt`: a network root that matches | deny; no remote look | FAIL (allow) |
| N3 | entries gain `Remote` | `W/job-1/inside.txt`: a network root that does not match | deny; no remote look | FAIL (allow) |
| N4 | `cwd` = `Remote`, entries [`Remote`, `W/home`, `W/job-1`] | `W/job-1/inside.txt`; then `Remote/x.txt` | deny; no remote look | FAIL |
| N5 | site folder = `Remote/h1` | `W/job-1/inside.txt` | deny; no remote look | FAIL (allow) |
| N6 | the reader answers `Entry.Link(Remote/x.txt)` for `W/job-1/jl` | `W/job-1/jl`; `W/job-1/jl/deeper.txt` | deny; no remote look | FAIL on the look |
| N7 | the reader answers `Entry.Link(Remote)` for `W/linked-root`; entries gain `W/linked-root` | `W/job-1/inside.txt`; then `W/linked-root` itself | deny; no remote look | FAIL (allow) |
| N8 | `W/job-1/a` answers `Entry.Link(W/job-1/b)`, `W/job-1/b` answers `Entry.Link(Remote/x.txt)` | `W/job-1/a` | deny; no remote look | FAIL on the look |
| N9 | a real link inside the root to a file inside the root [L]; and B24's stand-in | as A4 and B24 | allow, deny: the guard changes neither | pass |
| N10 | `HookSite.OfThisProcess()` | `IsLocal(Remote/x.txt)` is false; `IsLocal(W)` is true | the product's wiring | FAIL |
| N11 | theory, `AgyHookPlaces.Guarded(inner, isLocal)`, `isLocal` built with `windows` given | Not local: `\\h\s\x`, `//h/s/x`, `\\?\C:\x`, `\\.\C:\x`, `\\?\UNC\h\s\x`, `\??\C:\x` (windows true); `Z:\x` with `Z` typed `Network`, `Unknown`, `NoRootDirectory`; `/net/h/x`, `/misc/x`, `//h/s` (windows false). Local: `C:\x` typed `Fixed`; `/tmp/x` | not local: `IOException`, the inner reader called 0 times, the message is the constant. Local: the inner answer, 1 call | FAIL |
| N12 | theory, `Junctions.IsLocal(text, driveType, windows)` | the spellings of N11, plus `C:x`, `\x`, `x`, `""` (false both ways), `c:/x` (true on windows), `/net` (true off windows) | as listed | FAIL (no such overload) |
| N13 | scan of `src_mcp/runners/Reviewers/AgyHookPlaces.cs` with `ProductionSources` | — | the file is found; it names none of the types of section 3; `RealPath.Resolve(` occurs exactly once | may pass after S1; its red-again row gives it meaning |

N11 and N12 run on every OS: the rules are text. The Windows spellings are judged on Linux and the Unix ones on Windows.

**Windows only.** Each is its own `[Fact]` starting with `Assert.SkipUnless(OperatingSystem.IsWindows(), "…")`, as
`JunctionsTests.cs:219` does. They run through `Decide`, where the paths mean what Windows means by them.

| # | Setup | Path | Expect | Today |
|---|---|---|---|---|
| N20 | the recorder | each of `\\h\s\x`, `//h/s/x`, `\\?\C:\Windows\win.ini`, `\\.\C:\Windows\win.ini`, `\\?\UNC\h\s\x`, `\\.\pipe\x`, `\??\C:\x` | deny; no remote look | FAIL on the look, for each spelling that reaches the reader |
| N21 | the recorder around the REAL `RealPath.Lstat`; `IsLocal` from `HookSite.OfThisProcess()` | `\\127.0.0.1\coai-nonexistent\x` | deny, in under 1 s; `Seen` holds no such path | FAIL |
| N22 | `IsLocal` built with a drive type that answers `Network` for `Z:\` (then `Unknown`, then `NoRootDirectory`); entries gain `Z:\repo` | `Z:\repo\x.txt`; then `W\job-1\inside.txt` | deny; no look at any `Z:` path | FAIL |
| N23 | the reader answers `Entry.Link(@"\\h\s")` — a BARE share — for `W\linked-root`; entries gain it | `W\linked-root`; then `W\job-1\inside.txt` | deny, and `Seen` holds nothing remote: only (b) can deny this | FAIL (allow) |
| N24 | a real junction `W\job-1\jn` → `W\job-1\sub`, made by `Junctions.Shell.MakeLink` (no privilege needed); the real reader; `IsLocal` from `OfThisProcess()` | `W\job-1\jn\deep.txt` | allow: a local junction still works | pass |
| N25 | as N20, but the spelling is a root entry, then the `cwd` | `W\job-1\inside.txt` | deny; no remote look | FAIL |

**The built binaries.** One case each, in `TheBuiltBinary_AnswersTheHook_AsTheChildProcessAgyStarts` (M5) and in the server
binary test (S10): the child in `home/.agents`, the default payload, Path = `Remote/x.txt` of this OS → deny with the path reason.
A child process cannot show that no look was made, so these two only keep the routing honest. Today: pass.

## 5. Red, green, red

1. **Stub.** Add the `IsLocal` member (the product and the fixture pass their real predicates), `Guarded` returning the inner
   reader unchanged, and the three-argument `Junctions.IsLocal` returning true. This only lets the tests compile.
2. **RED.** Rows marked FAIL fail for the stated cause: a remote path in `Seen`, or `allow`. N9 and N24 pass.
3. **GREEN.** Implement section 2. Then both test projects in Release, and the leftovers guard.
4. **RED again**, one change at a time, restored after each. Record each observation in the pull request.

| Change back | Must go red |
|---|---|
| (a): `site.Read` goes to `RealPath.Resolve` unguarded | N1 to N6 and N8 on the look, on every OS; N7 on Unix; N20, N21, N22, N25 on the Windows job |
| (a): the guard calls the inner reader and judges afterwards | N11, and the same rows on the look |
| (b): an `Ok` resolve is taken without the local test | N23, and N7 on the Windows job. Nothing on a Unix run: there `RealPath` reaches no network place without a look. Say so in the pull request, with the Windows job's result |
| `OfThisProcess()` gives an `IsLocal` that is always true | N10, N21 |
| the Unix arm of the new overload answers true | N12; N1 to N8 on Unix |
| a second `RealPath.Resolve(` call in `AgyHookPlaces.cs` that passes `site.Read` | N13 |

## 6. Limits

- **Not proven here: the wire.** The recorder shows the hook asked for no remote entry. That Windows sends no NTLM handshake is
  shown by a packet capture, once, in S3's Windows run.
- **A network checkout is not supported.** A root on a UNC path, a mapped network drive or `\\wsl$\…` has every agy read denied.
  On Unix the same holds under `/net/`, `/misc/` and `/Network/`. The sentence is the path sentence; the changelog says why.
- **A network file system mounted into the Unix tree** (nfs, cifs at an ordinary folder) is spelled like a local path and is not
  detected. Its credentials are the mount's own. A custom autofs map on another root is not detected either.
- **The drive type** comes from the local drive table (`DriveInfo`). `LocalPaths.cs` says it answers without contacting the share;
  that is not measured again here. What Windows answers for a `subst` drive is not measured.
- **A volume mount point** with no drive letter has a link text that is no drive path. It is resolved as before this story.
- **agy's own read** still follows links after an allow. The hook has refused every path whose resolution leaves the local drives,
  so an allowed path was local at the moment of the check. A link swapped after it is the parent rule's stated race.
- **Two tests of locality exist** in this repository: `Junctions.IsLocal` (runners) and `LocalPaths.IsLocalRooted`
  (`src_mcp/src/Store`). Their Unix lists differ. The hook must use the first: the server binary has no access to the second.
  Making them one is not this story.

## Amendments from the cadence consultation (2026-11-18, consultation 54f49926, codex)

These repair this spec so that it does what the plan says. They are not plan changes.

1. **The Unix arm of the locality test is TEXT only.** Today `IsLocalUnixPath` uses `Path.IsPathRooted`, which depends on the host: on
   Windows it answers true for `C:x` and `\x`. With `windows: false`, a path is rooted only when it starts with `/`. Fix the helper so
   N12 holds when run on Windows (`C:x` and `\x` give false with `windows: false`).
2. **A `Path` that starts with two separators is denied on every OS, by its spelling, before it is resolved.** That means `//` or `\\`,
   in either order or mixed. `RealPath` folds `//tmp/x` to `/tmp/x` before the reader sees it, so neither the guard nor the result check
   would refuse it on Linux. Add a test through `Decide` on every OS: `//<fixture>/job-1/inside.txt` is denied, with the control
   `/<fixture>/job-1/inside.txt` allowed. A link's TARGET text that starts with `//` on Linux is left to the kernel's own reading (`//x`
   is `/x` there). On Windows the guard refuses it. This closes a contract gap: the policy says `//` is non-local. It is not a measured
   Linux leak.
