# PLAN — E1 stories: the agy hook knows its own folder, looks only at local paths, and refuses a spelling Windows reads elsewhere

> Status: **plan only, nothing implemented yet, 2026-11-18.** Parent:
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md) (its `## Epics`, E1; epoch 1,
> one change recorded: 1.1). The only epic of the plan. Three stories, all three security stories, each with a spec written before
> its code. **Cornerstone: E1.S2.** The parent's ledger line reads `Cornerstones: 1 of 1 — E1.S2`.
> Written by Fable on the gate operator's command (the rule's default writer of a stories document is Opus).
> A stories document keeps no change ledger: a change to this epic is an entry in the parent.
>
> Scope: `src_mcp/runners/Reviewers/AntigravityReadOnly.cs`, two new files beside it, one widened method in
> `src_mcp/runners/Files/Junctions.cs`, a remark in `src_mcp/runners/Files/RealPath.cs`, their tests in `src_mcp` and `src_server`,
> and the documents that state the hook's rule and limits. One branch, one pull request, the coai code gate once.

## 1. The W3 decision: the coai folder is the parent of the hook's own working folder

The parent's change 1.1 says the hook identifies the coai folder by itself. Three ways were weighed.

| Way | What it is | Verdict |
|---|---|---|
| (a) compute it | `Path.Combine(AntigravityReadOnly.DefaultBase, HookHandler.OfThisProcess().Key())`, as the launcher does | **rejected**: the hook process does not see the launcher's environment on two of four launch paths |
| (b) where the hook runs | the parent of the hook process's working folder, which is `<coai folder>/.agents` | **chosen** |
| (c) both must agree | (a) and (b), deny on a difference | **rejected**: it denies every read wherever (a) is wrong |

**Why (a) is wrong.** `DefaultBase` is `LocalApplicationData` + `coai-agy` (`AntigravityReadOnly.cs:63`). The hook is a child of agy and
gets agy's environment (measured: `research/RESULTS_agy_read_file.md` §6, "An environment variable set on agy reaches the hook process").

| Launch path | Launcher's `DefaultBase` | What the agy child is given | Hook's `DefaultBase` | Agree |
|---|---|---|---|---|
| reviewer, local `coai-mcp` (`AntigravityRuntime.cs:69`) | `$XDG_DATA_HOME` or `~/.local/share`, plus `coai-agy`; Windows `%LOCALAPPDATA%\coai-agy` | the whole environment (`ProcessLauncher.cs:99`, `InheritsEnvironment` is true) | the same | yes |
| consult (`AntigravityConsultant.cs:204`) | the same | the whole environment | the same | yes |
| question row (`AntigravityConsultant.cs:217`, then `ConsultantLaunches.cs:79`) | the same | `ProcessEnvironment.Minimal`: Unix keeps `HOME` and drops `XDG_DATA_HOME` (`ProcessEnvironment.cs:91`); Windows keeps `LOCALAPPDATA` (`:84`) | Unix: `~/.local/share/coai-agy` | Unix: no, when the operator has an `XDG_DATA_HOME` of their own. Windows: yes |
| Team server (`ReviewLauncher.cs:396`, `SlotEnvironment.cs:33-35`) | the server's own: `/root/.local/share/coai-agy` (`POST_DEPLOY.md:45` shows `/root/.local/share/coai-agy/hab3a05e932d9`) | not inherited; `HOME` = the slot folder, `XDG_DATA_HOME` = `<slot>/.local/share` | `<slot>/.local/share/coai-agy`, or the relative `coai-agy` when that folder is missing | **never** |

Measured 2026-11-18, .NET 10.0.114 on Linux, `Environment.GetFolderPath(LocalApplicationData)` in a one-line script:
- `XDG_DATA_HOME` set and the folder there: it answers `$XDG_DATA_HOME`.
- only `HOME` set to a slot-like folder: it answers `<that folder>/.local/share`.
- the folder not there: it answers the EMPTY string, so `DefaultBase` is the relative `coai-agy`.

So with (a) the Team server's hook would name a folder that is not the coai folder. The real one would count as a root, and its files
would be readable. With (c) every agy read on the Team server would be denied.

**Why (b) holds.** The hook's command is relative: `sh ./coai-hook.sh` and `.\coai-hook.cmd` (`AntigravityReadOnly.cs:207`). It
starts only when the shell's working folder is the folder that holds the script, and the handler inherits that folder. agy's own guide
says the handler runs "with the hooks file's folder as its cwd" (`research/PLAN_agy_cannot_write_its_roots.md:75`). Measured on Linux:
"its working directory is `<coai folder>/.agents`" (`research/RESULTS_agy_read_file.md:122`). On Windows the bare name was not found
and `.\coai-hook.cmd` was (live, 2026-10-09, the comment at `AntigravityReadOnly.cs:204-206`). Both built-binary tests already start
the child there. The environment plays no part, so the Team server and a question row get the same answer as a local review.

**When the hook cannot tell.** The working folder cannot be read, is not fully qualified, or its last name is not `.agents`: the coai
folder is unknown, and every reader call is denied with the path reason. One constant line goes to standard error.

**The payload's `cwd`.** It no longer names the coai folder. It is still validated as before (a string, fully qualified, resolvable):
agy always sends it, it is one of `workspacePaths`, and a payload without it is one this code does not understand.

## 2. The rule after E1

D0, D1, D2, D4, D5 and D7 are the parent rule's steps, unchanged
([the S1 spec of the parent plan](../research/PLAN_the_agy_hook_checks_the_path_S1_spec.md), section 1). D2 to D10 deny with the path reason.

| Step | What must hold | Story |
|---|---|---|
| D3 payload `cwd` | A string, fully qualified, resolves. Validated only: it names nothing. | S1 |
| D6 not the coai folder | The target is not inside the resolved coai folder of the hook's site. | S1 |
| D8 the coai folder is known | The site names it, fully qualified, and it resolves. | S1 |
| D9 local only | Every look at the disk goes through one guarded reader, which refuses a path that is not local before any I/O. Every resolved path must be local too. | S2 |
| D10 no dot segment where text folds | Where the OS folds `.` and `..` as text (Windows), `Path` has no segment that is exactly `.` or `..`. | S3 |

## 3. The API shape after E1

```csharp
// src_mcp/runners/Reviewers/HookSite.cs — what one hook process knows by itself, never from the payload
public sealed record HookSite(string? CoaiFolder, EntryReader Read, Func<string, bool> IsLocal, bool FoldsDotsAsText)
{
    public static HookSite OfThisProcess();                         // never throws
    internal static string? CoaiFolderOf(string workingDirectory);  // text only
}

// src_mcp/runners/Reviewers/AntigravityReadOnly.cs
public static string Decide(string payload, HookSite site, TextWriter? log = null);       // pure but for site.Read
public static int RunHook(TextReader input, TextWriter output, HookSite? site = null);    // null: OfThisProcess()

// src_mcp/runners/Reviewers/AgyHookPlaces.cs — the place check; it names no file-system type
internal static class AgyHookPlaces
{
    internal static bool Allows(JsonObject call, HookSite site);
    internal static EntryReader Guarded(EntryReader inner, Func<string, bool> isLocal);
    internal static bool HasDotSegment(string text);
}

// src_mcp/runners/Files/Junctions.cs — widened; the two-argument overload passes this OS
internal static bool IsLocal(string target, Func<string, DriveType> driveType, bool windows);
```

S1 makes `HookSite` with its first two members. S2 adds `IsLocal`. S3 adds `FoldsDotsAsText`. The old
`Decide(string, EntryReader?, TextWriter?)` goes: one road in. Both `Program.cs` routings stay as they are.

## 4. The stories

Order: S1, S2, S3. Each story is red tests, then code, then the red-again table of its spec, in its own commits.

### S1 — the hook knows its own folder (W3, change 1.1)

- **Goal.** A root that sorts before the coai folder is readable. The coai folder is denied wherever it stands in
  `workspacePaths`. A hook that cannot tell where it runs denies. Fixes Linux, the Team server and Windows alike.
- **Spec.** [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S1_spec.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S1_spec.md).
- **Files.** New `HookSite.cs` and `AgyHookPlaces.cs` (the place check moves there first, with no change of behaviour);
  `AntigravityReadOnly.cs`; new `src_mcp/tests/AgyHookFixture.cs` (the fixture of the existing class, extracted) and
  `TheAgyHookKnowsItsOwnFolderTests.cs`; `TheAgyHookReadsOnlyInsideItsRootsTests.cs`, `AntigravityReadOnlyTests.cs`,
  `src_server/tests/TheServerBinaryAnswersTheAgyHookTests.cs`.
- **Done when.** Rows F1 to F12, M1 to M4 and S7 to S9 of the spec are green. A1 to D8 of the existing class are green. The red-again
  table is observed and written into the pull request. The real Linux run of the spec's section 6 is recorded.
- **Who.** Security story: spec Fable, code Sonnet, review Opus. Not the cornerstone: its failure is loud (reads are denied), a test
  and a real run show it, and a release repairs it.

### S2 — CORNERSTONE: the hook looks only at local paths (W2, gate findings 1 and 2)

- **Goal.** The hook never looks at a network or device path: not for `Path`, a root, the payload's `cwd`, the coai folder, or a
  link target met on the way. A network root is not supported: every read is denied.
- **Spec.** [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S2_spec.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S2_spec.md).
- **Files.** `AgyHookPlaces.cs`, `HookSite.cs`, `Junctions.cs` (one overload); new `TheAgyHookLooksOnlyAtLocalPathsTests.cs`;
  one case each in `AntigravityReadOnlyTests.cs` and the server binary test; `JunctionsTests.cs` (the new overload's table).
- **Done when.** Rows N1 to N12 are green on every OS and N20 to N25 on the Windows job. The scan of `AgyHookPlaces.cs` is green.
  The red-again table is observed and written into the pull request.
- **Who.** Security story and the cornerstone: spec Fable, code Sonnet, review Opus.
- **Why the cornerstone.** Its failure is silent and cannot be taken back. A lookup that should not happen leaves no symptom:
  the decision is `deny` either way, and the plan's own measurement says timing cannot show it. Credentials offered to a host
  stay offered. The proof rests on where the recording reader sits, which is a design choice of the spec.

### S3 — no dot segment where text folds, the documents, and the real Windows run (W1, gate findings 3 and 4)

- **Goal.** On Windows a `Path` with a `.` or `..` segment is denied, so the spelling the hook judges is the spelling Windows opens.
  The limits in the documents say what is true. A real Windows machine shows the hook's own denial.
- **Spec.** [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S3_spec.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S3_spec.md).
- **Files.** `AgyHookPlaces.cs`, `HookSite.cs`; the remark at `RealPath.cs:61`; new `TheAgyHookRefusesDotSegmentsWhereTextFoldsTests.cs`;
  `TheAgyHookReadsOnlyInsideItsRootsTests.cs` (A3); one case each in the two binary tests; `SECURITY.md`,
  `research/RESULTS_agy_read_file.md`, `research/architecture.md`, `research/module_tests.md`; the changelog text for the release
  pull request.
- **Done when.** Rows T1 to T7 are green on every OS and T8 on the Windows job. The Windows run of the spec's section 6 is recorded
  in the RESULTS file, or the documents say it was not run and the Windows limits stay. The parent's Definition of Done is ticked box by box.
- **Who.** Security story: spec Fable, code Sonnet, review Opus. The documents are written by the coder from the spec's list.
  The Windows run is the team lead's, on the owner's Windows host.

## 5. What every story keeps

- The existing rows A1 to A7, B1 to B27, C1 to C3 and D1 to D8 stay green. Three rows change shape, and each spec names its own: B13 and C3 (S1), A3 (S3).
- `RealPath` is the only resolver, and its `..` rule does not change. `TheLinkResolverIsOneRoadTests` stays green: the new files hold no
  `.LinkTarget`, no `ResolveLinkTarget`, and no `GetFullPath(` beside a `StartsWith(`.
- Both reader names, the two deny sentences as they are, exit 0 always, no path in a reason or a log line.
- Methods at cyclomatic complexity 4 or less (`src_mcp/CodeMetricsConfig.txt`). No test changes the process directory.
- Tests run through the MTP executable, never `dotnet test`. Run the leftovers guard as CI runs it before a push: moving the fixture
  can shift a line-keyed exemption.

## 6. Limits this epic states and does not close

- **A network checkout.** A root on a UNC path, a mapped network drive, `\\wsl$\…` or, on Unix, under `/net/`, `/misc/` or
  `/Network/` is not supported: every agy read under it is denied. The consult launch grants the person's own checkout, so a
  checkout kept in such a place loses agy's reads. The changelog says so.
- **Another name for the coai folder.** An 8.3 short name, a trailing dot, or an NTFS stream name is a different string. Under a
  granted root that contains the coai folder, such a spelling of its files is not seen as inside it. The folder holds no secret.
- **A dot segment in a link's own text.** Not judged. The kernel applies a relative link's `..` to the path it has already
  resolved, which is the walk's rule. An absolute target with `..` is not what `mklink /J` writes. Not measured on Windows.
- **A volume mount point** (a folder that mounts a volume with no drive letter) is resolved as before. Not changed here.
- **No packet capture yet.** That Windows sends no NTLM handshake is shown by the recording reader, not by the wire, until S3's run.

## 7. Where this differs from the plan's text, the gate findings and the consultants

- The plan's W3 paragraph names `DefaultBase` and `HookHandler.Key()` as the folder. Section 1 shows they do not agree across
  processes, so the hook uses where it runs. The Definition of Done is met as written.
- Gate finding 1 allows a root-of-path check on the spelling as a first cut. It is not built: the guarded reader and the check of
  every resolved path cover every case it would, and a rule no test can see fail is a comment (`common/security.md`).
- Gate finding 2 says coai never grants a network root today. A consult grants the person's checkout wherever it is (section 6).
- The plan's status line says Linux is not affected. W3 is on every OS, and the locality rule (`Junctions.IsLocal`) has a Unix arm.
- The consultants asked to consider `.` and `..` in roots, `cwd` and link text. The rule stays on `Path`, as the plan's option A says:
  roots and `cwd` are coai's and agy's text, never the model's. Link text is section 6.
- Measured 2026-11-18 on this machine's Windows host (build 26300, `[IO.Path]::GetFullPath`): only a segment that is exactly `..`
  leaves its folder. `.. ` in the middle of a path is kept as a name; at the end it is trimmed to the folder itself; `...` is a name.
  So the exact rule of option A is enough against the escape.
