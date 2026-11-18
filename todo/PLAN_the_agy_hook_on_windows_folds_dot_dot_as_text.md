# PLAN — the agy read-only hook on Windows folds `..` as text and never opens a network path

> Status: **plan only, nothing implemented yet, 2026-11-16; plan gate `proceed` 2026-11-18 (session 6830eaba, 2 reviewers, 4 findings accepted and folded in: §2 "Gate round 1"); allowed over the PR cap by the owner on 2026-11-18.** Scope: `src_mcp/runners/Files/RealPath.cs` (or the hook's call of it),
> `src_mcp/runners/Reviewers/AntigravityReadOnly.cs`, their tests, and the documents that list the hook's limits. Windows only; Linux,
> the Team server and WSL are not affected.
>
> Found by the Opus review of the parent plan's branch, 2026-11-16; parent:
> [PLAN_the_agy_hook_checks_the_path.md](../research/PLAN_the_agy_hook_checks_the_path.md). The parent's path check ships with these two gaps stated as
> limits. They are moved here so the parent plan does not change a second time after its approval.

## Plan changes
> Epoch 1 · approved 2026-11-18 · by reviewers · state: open
- 1.1 · 2026-11-18 · agent · scope, stories — step 1's measurement found W3, so the payload's `cwd` is no longer taken as the coai folder (§1 W3)
  agy sorts `workspacePaths` and sets `cwd` to its first entry, not to its own working folder: measured on agy 1.5.11 on Windows and
  on agy 1.5.9 on Linux. A granted root that sorts before the coai folder is therefore taken for the coai folder, and every read in it
  is denied. The shipped hook (mcp 0.65.4, 0.66.0; server 0.16.3) has this defect.

## 1. Problem

The parent's hook allows `read_file` / `view_file` only when the real path of `Path` is inside a granted root. It resolves the path with
`RealPath.Resolve`. Two things go wrong on Windows. Both are read from the code and the Win32 rules. Neither was run on a Windows machine, and
agy on Windows is not measured.

### W1. `..` after a link or junction

`RealPath` applies `..` to the path resolved so far: `Up()` removes the last resolved segment (`src_mcp/runners/Files/RealPath.cs` ~327-336,
called from the walk at ~324). That is the Unix rule, where the kernel resolves a link first and then takes its parent. The remark at
`RealPath.cs` ~61, "which is the parent the disk would use", holds on Unix only.

Win32 does the opposite. `GetFullPathName`, which `CreateFileW` uses, folds `.` and `..` as TEXT before any link is followed.

Example. The root is `C:\repo`. It holds a pnpm junction `node_modules\foo`, which points to `C:\repo\node_modules\.pnpm\foo@1\node_modules\foo`.
The model sends:

`C:\repo\node_modules\foo\..\..\..\..\..\Users\me\.ssh\id_rsa`

1. The hook walks the junction, then pops five times from the resolved junction target. It lands on `C:\repo`, and the entry
   `C:\repo\Users\me\.ssh\id_rsa` is missing. A missing entry ends the walk, and the rest is appended as written.
2. The hook judges `C:\repo\Users\me\.ssh\id_rsa` as inside the root and allows.
3. Windows folds the text to `C:\Users\me\.ssh\id_rsa` and reads outside the root.

The read goes through only if agy itself allows it. agy on Windows is not measured. On Linux agy denies `$HOME` by itself. Scenario B7 of
[the S1 spec](../research/PLAN_the_agy_hook_checks_the_path_S1_spec.md) (`job-1/l3/../secret.txt`) passes on Windows for the wrong reason.

### W2. A network path is looked up before it is judged

The hook resolves the target before it judges it (`AntigravityReadOnly.cs` ~292-295, `Qualified` then `Real`). The old hook never touched the
disk. `Path.IsPathFullyQualified` admits `\\?\C:\...`, `\\.\...`, `\\server\share\...` and `//server/share/...`.

With `Path = \\attacker\share\x`, `RealPath.Resolve` looks the entry up over SMB. That can send the user's NTLM credentials to that host. It can
also stall past the hook's 10 s timeout. The hook would deny the path afterwards, but the lookup has already happened.

### W3. The payload's `cwd` is not the coai folder (found 2026-11-18 by step 1's measurement; plan change 1.1)

The parent rule's D3 and D6 take the payload's `cwd` to be the coai-owned hook folder. They deny a path inside it, and they treat every
other `workspacePaths` entry as a root. That reading came from Linux runs in which the coai folder happened to sort first.

What agy actually does: it SORTS `workspacePaths` and sets `cwd` to its first entry. Its process working folder and its `init` event's
`cwd` stay the coai folder.

- **Windows, agy 1.5.11.** The coai-like folder `coaip` and one root `aaa` gave `cwd` = `aaa`. Root `R` gave `cwd` = `R`. Roots `rootp`
  and `zzz` gave `cwd` = `coaip`. The order is ordinal, uppercase before lowercase.
- **Linux, agy 1.5.9.** A probe folder under `/tmp/…` and one root `/home/jinx/.cache/coai-sortprobe-root` gave
  `"cwd":"/home/jinx/.cache/coai-sortprobe-root"`, `"workspacePaths":["/home/jinx/.cache/coai-sortprobe-root","/tmp/…/sortprobe"]`.

So when a granted root sorts before the coai folder, the hook takes that root for the coai folder and denies every read in it. Replayed
through the real hook on Windows, such a payload was denied with `coai: this consultation reads only inside the folders it was given`.

No deployment of today is hit:
- Here, the coai folder `/home/jinx/.local/share/coai-agy/…` sorts before `/home/jinx/git/…` and `/tmp/…`.
- On the Team server, `/root/.local/share/…` sorts before `/tmp/coai-server-job-…`.

A root at `/data/…`, `/code/…` or `~/.cache/…`, or one at `C:\Projects\…` or `…\AppData\Local\Temp\…` on Windows, is hit.

The fix direction: the hook identifies the coai folder by itself, not from the payload. It is the folder the launcher prepared for this
binary (`AntigravityReadOnly.DefaultBase` and `HookHandler.Key()`). The hook process also runs in its `.agents` subfolder. The roots are
every `workspacePaths` entry except that folder, resolved. A path inside that folder is denied. The payload's `cwd` is no longer
trusted for this.

### The Windows measurement of step 1 (2026-11-18, agy 1.5.11 and coai-mcp 0.66.0 on this machine's Windows host)

These go into `research/RESULTS_agy_read_file.md` (S3).
- **Payload.** The same fields as on Linux. `Path` is absolute, with backslashes and an uppercase `C:`, and is the model's text. The
  reader tool is `read_file`.
- **agy's own boundary.** It denied a read of `C:\Users\strug\…` outside its roots (`denied_actions`). It allowed and returned a read
  under the temp folder outside its roots, as `/tmp` on Linux.
- **The model's `..`.** It sent no `.` or `..` in any ordinary path over 11 runs.
- **W1 in the real hook.** It is confirmed: a junction `R\nm\foo` → `R\deep\a\b\c\d\e\f\g` and 7 `..` segments are folded by Win32 as text
  to `C:\Users\strug\coai-winprobe-outside\secret.txt`, outside R. `cmd /c type` printed its marker. The hook allowed that path. agy then
  denied it by itself, because the target was in the user profile, so W1 was not exploitable end to end in that run. A W1 target under
  the temp folder, which agy reads by itself, was not measured.
- **W2.** The hook denied, each within about 60 ms with process start-up:
  - `\\127.0.0.1\…`, `//127.0.0.1/…`, `\\?\C:\…`, `\\.\C:\…` and `\\?\UNC\127.0.0.1\…`;
  - `D:\…`, another local drive.

  Timing cannot show whether an SMB lookup happened. That proof is the reader guard and its tests.

## 2. Options

**W1.**
- A. On Windows, deny any `Path` that has a `.` or `..` segment. Simple. agy's own reader may then lose a harmless `..` on Windows; the model
  can resend the path without it.
- B. Fold the text first, as Win32 does, and require BOTH the folded spelling and the walked path to pass the inside checks. More permissive
  than A. It needs a second resolve, and the folded spelling must be built the way `GetFullPathName` builds it.
- Recommended: A, unless a measurement on a Windows machine shows agy sends `..` in ordinary paths.
- Not chosen: changing `RealPath`'s `..` rule. Other sites rely on it, and `TheLinkResolverIsOneRoadTests` pins one walk for all of them.

**W2.**
- A. Resolve `cwd` and the roots first. Then, before resolving `Path`, deny when `Path.GetPathRoot(Path)` equals no resolved root's root
  (case ignored).
- B. Deny a `Path` that starts with `\\` or `//` unless a root uses that prefix.
- Recommended: A. It also covers `\\?\` and `\\.\` forms and a path on another drive. B is a cheaper cut if A turns out hard to test.

### Gate round 1 (2026-11-18, session 6830eaba) — four findings, all accepted, folded in here

The plan was approved with these four. They are part of the approved plan, not changes after it.

1. **A junction inside a granted local root can point at a UNC share.** A root-of-path check on the SPELLING passes, and the resolver then
   looks the share up while it walks the junction. So the guard sits where the disk is touched, not on the spelling: the hook hands
   `RealPath` an `EntryReader` that refuses, without any I/O, to look at a path that is not on a local drive. That covers a UNC,
   `//`, `\\?\UNC\`, `\\.\` or mapped-network path. The test for "local" is the one coai already has for links in a scratch folder
   (`Junctions.IsLocal`, used by `GitScratch.Look`). A refused look fails the resolve, and the hook denies. This applies to the target,
   to every root, to `cwd`, and to every link target met on the way. **It replaces W2's option A as the mechanism.** A root-of-path
   comparison may stay as a cheap first cut, but the reader guard is what the Definition of Done relies on.
2. **Network and device roots.** The policy is that a UNC, device or mapped-network root or `cwd` is not supported. It is refused by the
   same reader guard before any lookup, so every read under it is denied. coai never grants one today. Tests cover a matching UNC root and
   path (deny, no lookup) and a non-matching one.
3. **The real Windows filesystem.** The reader-seam test alone does not prove what Win32 does. On a Windows machine, the real hook binary
   and a real junction with the escape spelling must show the read denied. That means the hook's decision, and agy's read where agy runs.
   If that cannot be run, the Windows limit stays in `SECURITY.md` and is not removed (Definition of Done).
4. **Relative paths.** The hook already denies any `Path` that is not fully qualified (the parent's rule D2) before anything here runs.
   Every rule in this plan therefore applies to fully qualified paths only, and a relative path keeps its existing denial.

## 3. Build order

1. Measure first, on a Windows machine: does agy 1.5.9 allow a read of `C:\Users\<name>\...` outside its roots? Does it send `..` in ordinary
   paths? Record the result in `research/RESULTS_agy_read_file.md` (a new dated paragraph). This decides whether W1 is exploitable or only
   a gap in depth. It does not decide whether to fix it.
2. Write the reader-seam tests below. They fail on a Windows build first (W1) and, for W2, they fail on any build.
3. W2: add the root-of-path check before the resolve in `AntigravityReadOnly.Decide`.
4. W1: add the `.` / `..` rule (option A) or the folded-spelling check (option B).
5. Run the whole `AntigravityReadOnly` and `TheAgyHookReadsOnlyInsideItsRootsTests` groups on Windows and on Linux.
6. Update the limits in `SECURITY.md`, `research/RESULTS_agy_read_file.md` §6 and `src_vs_code/CHANGELOG.md`: remove the two Windows limits the
   parent added, with a dated note (the old text stays in the history).
7. Fix the remark at `RealPath.cs` ~61 so it says the rule is the Unix rule, and what Win32 does instead.

## 4. Test plan

- **W1, on every machine.** Stand a link in through the reader seam (`EntryReader`, like scenario B24) so the test needs no real junction and
  runs on Linux and Windows alike. The spelling is `root/<link>/../../..`-style text that, folded as text, leaves the root, and that the walk
  alone would judge inside. Today: allow (red). After the fix: deny. A control row first shows the same payload without `..` is allowed.
- **W1, a pure-text case.** A `Path` with a `.` or `..` segment that stays inside the root. Under option A it is denied on Windows. Under
  option B it is allowed. The test states which, so a later change cannot flip it unseen.
- **W2, on every machine.** A recording reader: the hook is handed `\\host\share\x` and `//host/share/x` with a root on `C:\`. The reader must
  see no call for any entry of that path, and the decision is deny. Where the OS treats the string as a plain relative name (Linux), the
  test uses a `Path` the same rule applies to, or is a Windows-only `[Fact]` with a skip elsewhere.
- **W2, Windows only.** A `[Fact]` that skips off Windows: `\\127.0.0.1\nonexistent\x` is denied and returns in under 1 s, with no SMB
  lookup (the reader seam records none).
- **A junction to a UNC target (gate finding 1), on every machine.** Through the reader seam: a local-looking `Path` under the root whose
  link entry's target is `\\host\share\x`. The guarded reader must make no look at the UNC target, and the decision is deny.
- **A UNC root or `cwd` (gate finding 2).** Payloads whose root, or whose `cwd`, is UNC or device-form, one matching the `Path` and one not:
  deny, and no look of any network path.
- **The real Windows run (gate finding 3).** This is a step of the build, recorded in the RESULTS file, not a unit test. The real
  `coai-mcp` hook and a real junction with the escape spelling: the hook's decision is deny, and so is agy's read where agy runs.
- **Not proven by tests:** that Windows sends no NTLM handshake. The recording reader shows the hook made no lookup. A packet capture is the
  evidence, once, on a Windows machine, and goes into the RESULTS file.

## Definition of Done

- [ ] W3 (change 1.1). The coai folder is identified by the hook itself, not by the payload's `cwd`, on every OS. Tests cover:
  - a root that sorts BEFORE the coai folder, in the order agy sends it (`cwd` = that root): its reads are allowed;
  - the coai folder in any position in `workspacePaths`: its reads are denied;
  - the server binary, through its own hook.
  Linux and the Team server are fixed too, in the next mcp and server release.

- [ ] Step 1 measured on a Windows machine and written to `research/RESULTS_agy_read_file.md`, or the plan states it was not possible and why.
- [ ] A `Path` with a `..` after a link or junction that leaves the root by Win32's text rule is denied. The reader-seam test runs on every OS.
- [ ] A UNC, `\\?\` or `\\.\` `Path` is denied with no lookup of it. The recording-reader test is green. The Windows `[Fact]` is green.
- [ ] No network or device path is ever looked up by the hook. That holds for a junction met on the way to a UNC target, and for a UNC or
  device root or `cwd` (gate findings 1 and 2). The reader-guard tests are green on every OS.
- [ ] On a real Windows machine, the real hook binary and a real junction with the escape spelling deny the read (gate finding 3). If this
  could not be run, the Windows limits stay in `SECURITY.md` with a dated note saying so.
- [ ] The existing scenarios A1-A7, B1-B27, C1-C3 of the S1 spec still pass.
- [ ] `RealPath.cs` ~61's remark is correct.
- [ ] The Windows limits in `SECURITY.md`, `RESULTS_agy_read_file.md` §6 and the changelog are replaced by a dated note, history kept.
- [ ] The plan is promoted to `research/` with its deviations recorded.
