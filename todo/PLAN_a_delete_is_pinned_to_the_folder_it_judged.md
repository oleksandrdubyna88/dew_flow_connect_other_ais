# PLAN — a recursive delete is pinned to the folder it judged (an `openat`/`unlinkat` walk), closing the check-to-use race

> Status: **plan only, nothing implemented yet, 2026-11-01 — a follow-up; nothing waits for it.** Scope: `WorktreeManager.EraseAsync`
> (`src_mcp/runners/Worktrees/WorktreeManager.cs`) and `ReviewTreeRoot.EnsureGoneAsync` (`src_mcp/runners/Worktrees/ReviewTreeRoot.cs`) — the two
> recursive deletes that stand behind `RealPath.ResolveBelow`.
>
> Extracted from the security fix of 2026-11-01 ([SECURITY.md](../SECURITY.md), "Past defects" and "Limits, said plainly"): the resolver
> change made both deletes act on the REAL path they judged, and recorded that the check and the delete are still two steps.

## 1. Goal

Both deletes now decide, by `RealPath.ResolveBelow`, that a tree's real path is strictly under the real root and that its last name is not a
link — and then act on THAT path. Between the decision and the delete (`git worktree remove -f -f`, `Directory.Delete(recursive)`, the move-aside,
the owner marker) a component of that path can be replaced by a link by anyone who can write the parent folder. The check does not see it:
it is "a check at one moment", a documented limit today.

Who can do it: a process of the same user, or a person with write access to the storage root. Nobody is hurt in the ordinary run; the storage
roots are machine-local and private to the user. The plan closes the window instead of leaving it as a sentence.

## 2. The idea

Walk the path down from the root by HANDLE, never by name twice:

- Open the root directory once (`open(O_DIRECTORY | O_NOFOLLOW)`; on Windows `CreateFileW` with `FILE_FLAG_OPEN_REPARSE_POINT`), then open
  each component with `openat(dirfd, name, O_NOFOLLOW | O_DIRECTORY)`. A link on the way is an error (`ELOOP`), not a detour.
- Delete from the handle: `unlinkat(dirfd, name, 0 / AT_REMOVEDIR)` for each entry, depth first, never by a path string.
- Verify the opened directory's identity (device and inode, `FolderIdentity` already reads them) against the one the check saw, when the check
  and the walk cannot be one step.

`git worktree remove` takes a path, so the git step stays by path; the plan decides whether the delete that follows git's (and its fallback) is
the only one that has to be pinned, because git has already removed the registered tree by then.

## 3. Open questions (measure first)

1. .NET has no `openat`/`unlinkat`: P/Invoke on Unix (a variadic-free `openat` is fine; `fcntl` is not) and `NtCreateFile`/`SetFileInformationByHandle`
   on Windows. `FolderIdentity`, `LinuxStatx` and `HandleRead` show the repository's pattern; macOS has the `F_GETPATH` problem recorded in
   `HandleRead`'s remarks.
2. Cost: the two deletes run on trees of a few thousand files; an `unlinkat` walk in managed code must not be slower than `Directory.Delete`
   by a factor that matters under the 2-minute `Erasing` budget.
3. Windows: a held file refuses the delete (the existing retry and move-aside stay); does a handle-pinned delete change which files hold?

## 4. What grows, who retires it

Nothing: no file, table or process is added. The walk opens and closes handles inside one call.

## 5. Build order

1. A scenario test, RED first on Linux: a stand-in seam swaps a component for a link between the check and the delete; today the delete follows it.
2. The handle-pinned walk behind a seam, Linux first (`openat`/`unlinkat`), then Windows.
3. `WorktreeManager.EraseAsync` and `ReviewTreeRoot.EnsureGoneAsync` use it; the lexical and by-name deletes go away.
4. SECURITY.md: the "check at one moment" limit is narrowed to what remains (a hard link, a bind mount), and a dated line says when it closed.

## 6. Definition of Done

- [ ] A swapped component between check and delete does not make the delete leave the judged tree (a test that was RED first).
- [ ] No `Directory.Delete(recursive)` by name remains behind either guard; `TheLinkResolverIsOneRoadTests` or a sibling scan names any that do.
- [ ] The Windows move-aside and the held-file retry behave as before (the existing tests stay green).
- [ ] SECURITY.md and the module docs say what remains open.
