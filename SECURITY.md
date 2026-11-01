# Security — what this product guarantees about paths, and where it stops

This file states the path guarantees of `coai-mcp` (`src_mcp`). It is not a vulnerability-reporting policy; report a
problem to the repository owners privately.

## A path is resolved by one resolver — `RealPath`

Every decision that depends on where a path REALLY leads goes through `src_mcp/runners/Files/RealPath.cs`. It does what
`realpath(3)` does, root-down:

- A link is read as the text it stores (never `ResolveLinkTarget(...).FullName`, which folds `..` as a string and never
  resolves the directories inside the target). Its text is pushed in front of the rest of the path.
- **One budget of 40 link expansions** for a whole resolution (the kernel's own `MAXSYMLINKS` on Linux). Plain folders cost
  nothing: a 60-folder path resolves. Over the budget, or a loop, is a refusal.
- `..` goes to the parent of the real path resolved so far, never above its root.
- A name that is not there ends the walk and the rest is kept as written. A `..` after such a name FAILS: the disk
  answers "no such file", and a lexical pop would answer a folder nobody looked at. A DANGLING link is a link, not a
  missing name: it costs a hop and its target is walked.
- A NUL, an empty link text, a drive-relative `C:x`, a path or link text over 32 KiB (`RealPath.MaxPathChars`), a resolved
  path over 32 KiB, and any IO error are a failure. Callers turn the failure into refuse, not ours or cannot compare — never
  into the spelling. A Windows target `\x` (a root with no drive) resolves against the LINK's volume.
- Containment compares the resolved path with the equally resolved root: equal, or the root plus a separator. Never a bare
  prefix.

`TheLinkResolverIsOneRoadTests` fails for any other `ResolveLinkTarget` in production code, and for a new reader of
`.LinkTarget` that is not listed there with its reason.

## What the sites guarantee

- **The document reader** (`review_document`, `FeaturePlan`, and the list and search a consultant is served by
  `WorkspaceLookup`): a path whose real path is not inside the repository (or a granted root) is refused, and so is a
  path the resolver cannot follow to its end. The text of a document is sent to three other vendors, so this is the
  boundary that keeps an outside file out of that. Refusals name the resolution as the reason and never echo more than
  200 characters of a path, with control characters made visible.
- **The question consultant's roots** (`QuestionConsultSettings`): a root is judged at its real path against the profile,
  the data directory, system directories and credential folders; a root that cannot be resolved is refused.
- **The `coai-wt-*` worktree deletion guard** (`WorktreeManager.IsUnderOurRoot`, which stands before
  `git worktree remove -f -f` and a recursive delete): a tree is ours only when its REAL path is strictly under the REAL
  storage root and the last name is not a link. There is no lexical alternative, and a resolution failure means NOT ours.
- **`WorktreePaths.Real` and `Same`** (git's `realpath` against a spelled path): `Real` throws `IOException` for a path
  that cannot be resolved; `Same`, a predicate, answers `false`. Neither falls back to the spelling.

## Limits, said plainly

- **Hard links and bind mounts are out of scope.** A hard link is one file with two names — there is no path to resolve —
  and a bind mount shows one tree at two places with no `readlink` to say so. No path check sees either.
- **Case.** A path is kept as spelled. Windows and macOS compare `OrdinalIgnoreCase` where the product always did
  (the worktree guard); the document reader compares `Ordinal` except on Windows, as `DocumentId.Comparison` says. On a
  case-insensitive disk a comparison that differs only by case is therefore a FALSE REFUSAL (fail closed), never a false
  accept. Open limit on macOS for the document reader; not widened here.
- **A check at one moment.** A folder swapped for a link between the check and the read or delete that follows is not seen.
- **Length.** A path over 32 KiB is refused at once. A 200,000-segment path took 36 s in a quadratic walk in the sibling
  `dew_flow_mcp`; this resolver does not walk after a missing name and refuses long input first.
- A Windows reparse point with no link target (a cloud-file placeholder) is read as a plain entry; it redirects no path.

## Past defects

### 2026-11-01 — a link's target path was never resolved

The document reader took `ResolveLinkTarget(returnFinalTarget: true).FullName` for a link. With `root/l2` a link to an outside
folder and `root/alias` a link to `root/l2/secret.txt`, the check said "inside" and the read followed `l2` outside; the text went
to three vendors. Reproduced on `origin/main`: `DocumentReader.Read` returned the outside text for `alias`, and for
`alias3/secret2.md` through a directory link; a `..` in link text read an inside decoy instead of the file the disk opens;
`nonexistent/../evil` read an outside file that was never inspected. The same walk made `WorktreeManager` delete a folder
OUTSIDE the storage root for a name lexically under it (the guard OR-ed the lexical spelling, and gave up after 16 links with
the place it had reached), and made `WorktreePaths.Real` answer `root/x` where `realpath` answers `outside/x`.
Fixed by one resolver and by a scan that keeps it one (`TheLinkResolverIsOneRoadTests`). Found by a scout who reproduced the
code verbatim; the same defect was fixed the same day in `dew_flow_mcp`'s `SandboxedFileReader`, and a security review of
that resolver supplied the `..`-after-a-missing-name, length, NUL and Windows-root rules above.
