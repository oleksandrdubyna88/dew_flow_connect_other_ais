# PLAN — the agy read-only hook on Windows folds `..` as text and never opens a network path

> Status: **plan only, nothing implemented yet, 2026-11-16.** Scope: `src_mcp/runners/Files/RealPath.cs` (or the hook's call of it),
> `src_mcp/runners/Reviewers/AntigravityReadOnly.cs`, their tests, and the documents that list the hook's limits. Windows only; Linux,
> the Team server and WSL are not affected.
>
> Found by the Opus review of the parent plan's branch, 2026-11-16; parent:
> [PLAN_the_agy_hook_checks_the_path.md](PLAN_the_agy_hook_checks_the_path.md). The parent's path check ships with these two gaps stated as
> limits. They are moved here so the parent plan does not change a second time after its approval.

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
[the S1 spec](PLAN_the_agy_hook_checks_the_path_S1_spec.md) (`job-1/l3/../secret.txt`) passes on Windows for the wrong reason.

### W2. A network path is looked up before it is judged

The hook resolves the target before it judges it (`AntigravityReadOnly.cs` ~292-295, `Qualified` then `Real`). The old hook never touched the
disk. `Path.IsPathFullyQualified` admits `\\?\C:\...`, `\\.\...`, `\\server\share\...` and `//server/share/...`.

With `Path = \\attacker\share\x`, `RealPath.Resolve` looks the entry up over SMB. That can send the user's NTLM credentials to that host. It can
also stall past the hook's 10 s timeout. The hook would deny the path afterwards, but the lookup has already happened.

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
- **Not proven by tests:** that Windows sends no NTLM handshake. The recording reader shows the hook made no lookup. A packet capture is the
  evidence, once, on a Windows machine, and goes into the RESULTS file.

## Definition of Done

- [ ] Step 1 measured on a Windows machine and written to `research/RESULTS_agy_read_file.md`, or the plan states it was not possible and why.
- [ ] A `Path` with a `..` after a link or junction that leaves the root by Win32's text rule is denied. The reader-seam test runs on every OS.
- [ ] A UNC, `\\?\` or `\\.\` `Path` is denied with no lookup of it. The recording-reader test is green. The Windows `[Fact]` is green.
- [ ] The existing scenarios A1-A7, B1-B27, C1-C3 of the S1 spec still pass.
- [ ] `RealPath.cs` ~61's remark is correct.
- [ ] The Windows limits in `SECURITY.md`, `RESULTS_agy_read_file.md` §6 and the changelog are replaced by a dated note, history kept.
- [ ] The plan is promoted to `research/` with its deviations recorded.
