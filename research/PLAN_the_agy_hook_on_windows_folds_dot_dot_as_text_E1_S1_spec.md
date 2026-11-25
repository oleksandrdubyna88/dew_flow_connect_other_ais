# SPEC — E1.S1: the agy hook knows its own folder, and the payload's `cwd` names nothing (W3)

> Status: **spec written 2026-11-18; its story is IMPLEMENTED, 2026-11-24** — E1 (S1 W3, S2 W2, S3 W1) shipped in #1023 (merge 9721a7fe, 2026-11-24); released in mcp 0.66.1 (2026-11-24) and server 0.16.4 (#1042, 2026-11-24, release run 45412033457, 12 assets). The check with the release `coai-server` 0.16.4 binary on the host passed on 2026-11-24 (agy 1.5.13): the in-root read was allowed and its marker read; the other job's file by absolute path, by `..` and through a link inside the root was each denied with the contract sentence, and the secret was never read. **Not deployed:** on 2026-11-25 the owner chose to skip the deploy, so the Team server still serves 0.16.3. W3 does not hit today's server (its job roots sort after the hook folder), so 0.16.3 stays safe there. Parent:
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md) (story S1), of
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md) (W3, change 1.1).
> A security story: spec by Fable, code by Sonnet, review by Opus. Not the cornerstone.

The coder re-discovers nothing: this file is the brief. The decision and its evidence are in the stories document, section 1.

## 1. The defect, in one payload

agy sorts `workspacePaths` and sets `cwd` to the first entry. The shipped hook takes `cwd` for the coai folder (`Places.Allows`,
`AntigravityReadOnly.cs:301-306`). With the root `/home/u/.cache/r` and the coai folder `/home/u/.local/share/coai-agy/h1`, agy sends
`cwd` = the root. The hook then denies every read in the root, and it would allow a read of the coai folder's own files.

## 2. The rule

Three steps of the parent rule change. The rest stays
([the S1 spec of the parent plan](PLAN_the_agy_hook_checks_the_path_S1_spec.md), section 1).

| Step | What must hold | On failure |
|---|---|---|
| D3 payload `cwd` | As today: a JSON string, fully qualified, and it resolves. It is validated and used for nothing else. | deny, path reason |
| D6 not the coai folder | `RealPath.IsInside(coai, target, DocumentId.Comparison)` is FALSE. `coai` is the resolved `site.CoaiFolder`, never the payload's `cwd`. | deny, path reason |
| D8 the coai folder is known | `site.CoaiFolder` is not null, is fully qualified, and `RealPath.Resolve` of it is `Ok`. | deny, path reason |

- **No entry is removed from the roots.** An entry equal to the coai folder admits only paths inside it, and D6 denies those. D6 also
  holds under a wider root that contains the coai folder. The coai folder need not be listed at all.
- **`HookSite.CoaiFolderOf(workingDirectory)`** is text only, no disk:
  1. `Path.IsPathFullyQualified(workingDirectory)`, else null.
  2. Trim one trailing separator. The last name must equal `.agents` under `DocumentId.Comparison`, else null.
  3. The answer is `Path.GetDirectoryName` of the trimmed text. Null or empty: null.
- **`HookSite.OfThisProcess()`** never throws. It reads `Directory.GetCurrentDirectory()` under a catch of every exception (the
  process edge, as `Decide` has) and takes an empty string on failure. Its `Read` is `RealPath.Lstat`.
- **An unknown folder says so, once.** When `site.CoaiFolder` is null and the tool is a reader, one line goes to `log` (standard
  error when null): `coai --agy-hook: denied, this hook does not run from its own .agents folder`. A constant. No path. Reason: a
  hook that denies everything is otherwise silent, and that is how W3 itself went unseen. A folder that is named but does not resolve
  writes nothing, like every other failed resolve.
  **Deviation 2026-11-18 (review fix F13, PR #1023):** a folder that is named but does not resolve (the disk cannot look at it, or it is not
  local) now writes the same `UnknownFolderLine` once and denies; the "writes nothing" text above is the old rule and no longer holds.
- **The tool is judged first.** A tool that is not a reader gets `DenyReason`, whatever the site is, and writes no line.
- **Why `cwd` is still validated.** Every agy from 1.5.9 to 1.5.11 sends it, and it is one of `workspacePaths`. A payload without it
  is one this code does not understand, and it was denied before. Rows B19 and D4 keep that.

## 3. The API shape, and the move

```csharp
// new: src_mcp/runners/Reviewers/HookSite.cs
public sealed record HookSite(string? CoaiFolder, EntryReader Read)
{
    public static HookSite OfThisProcess();
    internal static string? CoaiFolderOf(string workingDirectory);
}

// src_mcp/runners/Reviewers/AntigravityReadOnly.cs
public static string Decide(string payload, HookSite site, TextWriter? log = null);
public static int RunHook(TextReader input, TextWriter output, HookSite? site = null);   // null: HookSite.OfThisProcess()

// new: src_mcp/runners/Reviewers/AgyHookPlaces.cs
internal static class AgyHookPlaces { internal static bool Allows(JsonObject call, HookSite site); }
```

- **Commit 1, a move with no change of behaviour.** `Places`, `Placed`, `PathText`, `Qualified`, `Real` and `Roots`
  (`AntigravityReadOnly.cs:300-339`) move to `AgyHookPlaces.cs`. `ReadsInside` stays and keeps its catch. `Text` has one copy.
  In the tests, the fixture and helpers of `TheAgyHookReadsOnlyInsideItsRootsTests` (lines 24-142) move to
  `src_mcp/tests/AgyHookFixture.cs`, used by the old class and the new ones. The whole suite is green before and after.
  Why now: all three stories edit the place check, and S2 scans the new file for file-system types.
- The old `Decide(string, EntryReader?, TextWriter?)` is removed, not kept beside the new one. `site.Read` goes to every
  `RealPath.Resolve` call and nowhere else.
- `AgyHookPlaces.cs` and `HookSite.cs` hold no `.LinkTarget`, no `ResolveLinkTarget`, and no `GetFullPath(` beside a `StartsWith(`.
  Methods stay at cyclomatic complexity 4 or less. `Program.cs` of both binaries is not touched.
- Update the class remarks and the `Decide` summary of `AntigravityReadOnly`: the coai folder is where the hook runs.

## 4. Test scenarios

New class `src_mcp/tests/TheAgyHookKnowsItsOwnFolderTests.cs` on the shared fixture. The fixture gains `aaa/inside.txt`: a root whose
name sorts before `home`. The test sets `cwd` and the order itself; it does not sort.

**Helpers** (in `AgyHookFixture`):
- `Site(string? coaiFolder = W/home, EntryReader? read = null)` builds the `HookSite`. Every existing row runs with it.
- `Sent(path, cwd, entries, tool = "read_file")` builds the payload with `workspacePaths` exactly as listed, in that order.
- `Allowed` and `Denied` take the site. `Denied` still proves its control first.

"Today" is the result against the stub of section 5, which decides as the shipped hook does.

| # | Site and payload beyond the default | Path | Expect | Today |
|---|---|---|---|---|
| F1 | `cwd` `W/aaa`, entries [`W/aaa`, `W/home`], as agy sends a root that sorts first; each reader name | `W/aaa/inside.txt` | allow | FAIL (deny) |
| F2 | as F1 | `W/home/.agents/hooks.json` | deny; control: F1 | FAIL (allow) |
| F3 | entries = each of the 6 orders of {`W/aaa`, `W/home`, `W/job-1`}, `cwd` = the first entry | `aaa/inside.txt` and `job-1/inside.txt` allow; `home/.agents/hooks.json` deny | all three, in every order | FAIL in the 4 orders where `home` is not first |
| F4 | entries [`W/aaa`], `cwd` `W/aaa`: the coai folder is not listed | `aaa/inside.txt` allow; `home/.agents/hooks.json` deny | both | FAIL (the allow) |
| F5 | entries [`W`, `W/home`], `cwd` `W`: a wide root that holds the coai folder and sorts first | `home/.agents/hooks.json` deny; `job-10/secret.txt` allow | both | FAIL (the allow) |
| F6 | `cwd` `W/job-1`, entries [`W/home`, `W/job-1`]: `cwd` is a root that does not sort first | `job-1/inside.txt` | allow | FAIL (deny) |
| F7 | site folder: null; `""`; `home` (relative); `W/home` with a NUL. Default payload | `W/job-1/inside.txt` | deny, path reason; control: the default site | FAIL (allow) |
| F8 | site folder `W/home`, `read` throws `IOException` for the entry `W/home`, matched by its whole path (a temp folder may sit under `/home`); `cwd` `W/job-1`, entries [`W/job-1`, `W/second`] | `W/second/b.txt` | deny; control: the same with the real reader | FAIL (allow) |
| F9 | site folder null, `log` given: a reader call, then `write_file` | `W/job-1/inside.txt` | reader: deny, and `log` is exactly the line of section 2 plus a newline. `write_file`: `DenyReason`, `log` empty | FAIL |
| F10 [L] | `W/via` → `W`. Site `W/via/home`, entries [`W/home`, `W/job-1`], `cwd` `W/home`. Then site `W/home`, entries [`W/via/home`, `W/job-1`], `cwd` `W/via/home` | `W/home/.agents/hooks.json` deny; `W/job-1/inside.txt` allow | both, both ways | pass (it replaces B13) |
| F11 | theory, `HookSite.CoaiFolderOf`, `X` = the fixture's `W`, nothing created | `X/h1/.agents` and `X/h1/.agents/` → `X/h1`. Null for: `X/h1`, `X/h1/.agents/agents`, `X/h1/.agentsx`, `.agents`, `""`. `X/h1/.AGENTS` → `X/h1` only where `DocumentId.Comparison` ignores case | as listed | FAIL |
| F12 | `HookSite.OfThisProcess()` in the test host | — | no throw; `CoaiFolder` is null (the host does not run from a folder named `.agents`) | pass |

**Existing rows.**
- `Answer` passes `Site()`. A1 to A7, B1 to B12, B14 to B27, C1, C2 and D1 to D8 keep their text and their result.
- B13 goes: it compared the payload's `cwd` with an entry. F10 asks the same of the site folder.
- B19 and D4 stay: a `cwd` that is missing, not a string, relative, with a NUL or a lone surrogate still denies.
- C3 passes `Site()` to `RunHook`. No test changes the process directory.
- `AntigravityReadOnlyTests.Decision` passes a site whose folder is null: its payloads never reach the place check.

**The built `coai-mcp`** (`TheBuiltBinary_AnswersTheHook_AsTheChildProcessAgyStarts`; add `aaa/inside.txt` and `home/.agents/hooks.json` under `_base`):

| # | The child's working folder, and the payload | Path | Expect | Today |
|---|---|---|---|---|
| M1 | `home/.agents`; `cwd` `aaa`, entries [`aaa`, `home`] | `aaa/inside.txt` | allow | FAIL |
| M2 | as M1 | `home/.agents/hooks.json` | deny, path reason | FAIL |
| M3 | `home`, which is not an `.agents` folder; the default payload | `job-1/inside.txt` | deny, path reason; standard error holds the line of section 2 | FAIL |
| M4 | `home/.agents`; the default payload | `../../job-1/inside.txt` | deny, path reason | pass |

M4 replaces the case "the working directory is the root, the Path is `inside.txt`". A hook outside `.agents` now denies for another
reason, so that case no longer shows that the process directory is no base. From `home/.agents`, `../../job-1/inside.txt` would
complete to the inside file. The other cases of the test stay.

**The built `coai-server`** (`TheServerBinaryAnswersTheAgyHookTests`): S7, S8 and S9 are M1, M2 and M3 with the server binary.
`Payload` takes the `cwd` and the entries; `Answer` takes the working folder. The reasons stay literals. S1 to S6 stay.

## 5. Red, green, red

1. **Stub.** Add `HookSite`, the new `Decide` and `RunHook` signatures and `CoaiFolderOf` returning null. The body ignores
   `site.CoaiFolder` and decides as today with `site.Read`. This only lets the tests compile.
2. **RED.** Write every row. Build, then run the MTP executable with `--filter-class "*TheAgyHookKnowsItsOwnFolderTests"`, and
   the two binary tests. Rows marked FAIL must fail for the stated cause. Rows marked pass must pass. A row that fails for another
   cause is a broken test: fix the test first.
3. **GREEN.** Implement section 2. Then the whole `CoaiMcp.Tests` and `CoaiServer.Tests` in Release, and the leftovers guard.
4. **RED again**, one change at a time, restored after each. Record each observation in the pull request.

| Change back | Must go red |
|---|---|
| D6 reads the payload's `cwd`, not the site folder | F1 to F6, M1, M2, S7, S8 |
| D8: a null or unresolvable site folder means "no coai folder" and D6 is skipped | F7, F8, M3, S9 |
| the line for an unknown folder | F9 |
| the resolve of the site folder (its spelling is compared) | F10, first half |
| `CoaiFolderOf` without the `.agents` test | F11 only. M3 stays green: the parent of `home` holds every root, so D6 still denies |
| D3: the payload's `cwd` is not validated | B19, D4 |
| the fully-qualified test of `Path` | M4, off Windows. After S3 the dot rule denies it on Windows too |

## 6. The real run (Linux; the Windows half is in the S3 spec)

Recorded in `research/RESULTS_agy_read_file.md` by S3. The branch's Release `coai-mcp`, agy as installed, its version noted.

1. Let the branch binary prepare its folder: one agy launch through it, then find `<LocalApplicationData>/coai-agy/h…`.
2. Make `~/.cache/coai-sortprobe-root/inside.txt` with a marker. That root sorts before `~/.local/share/…`.
3. From the prepared folder run agy as the probe of 2026-11-18 did: stream mode, `--mode plan --agent coai-reader`,
   `--add-dir ~/.cache/coai-sortprobe-root`.

| Probe | Expect |
|---|---|
| read `…/coai-sortprobe-root/inside.txt` | allowed, the marker read. With mcp 0.66.0 the same call was denied (the plan, W3) |
| read `<prepared folder>/.agents/hooks.json` | `tool call denied by pre-tool hook: coai: this consultation reads only inside the folders it was given` |

The Team server cannot show W3 today: its job roots sort after `/root/.local/share`. Its release runs the usual hook probes of
`POST_DEPLOY.md`, which must stay as they are.

## 7. Limits

- **It trusts where agy starts the hook.** agy's guide says the hooks file's folder. If a later agy starts it elsewhere, every
  read is denied and the line of section 2 says why. That is loud, and it is the safe side.
- **Another spelling of the hook's own folder.** If the working folder arrives as an 8.3 short name, it is a different string from
  the entry in `workspacePaths`. Reads in the real roots still work. The coai folder's own files are then not excluded under an
  entry that names it the long way. The folder holds no secret.
- **A second agy on the same folder** changes nothing: the folder is per handler binary, and the site is read per hook process.
- Everything in section 6 of the parent rule's spec still holds, with "the coai folder" in place of "`cwd`".
