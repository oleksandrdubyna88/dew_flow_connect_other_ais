# Security — what this product guarantees about paths, and where it stops

This file states the path guarantees of `coai-mcp` (`src_mcp`). It is not a vulnerability-reporting policy; report a
problem to the repository owners privately.

## A path is resolved by one resolver — `RealPath`

The decisions in "What the sites guarantee" below — what a document reader may read, which root a consultant may be granted, which tree a
delete may touch — go through `src_mcp/runners/Files/RealPath.cs`. A few other sites still compare paths lexically; each is listed under
"Lexical sites that remain, and why each is safe". `RealPath` does what `realpath(3)` does, root-down:

- A link is read as the text it stores (never `ResolveLinkTarget(...).FullName`, which folds `..` as a string and never
  resolves the directories inside the target). Its text is pushed in front of the rest of the path.
- **One budget of 40 link expansions** for a whole resolution (the kernel's own `MAXSYMLINKS` on Linux). Plain folders cost
  nothing: a 60-folder path resolves. Over the budget, or a loop, is a refusal. The links in a repository root's OWN spelling
  (macOS: `/var` is `/private/var`) are the root's: the document reader and the feature plan resolve the root once and walk a
  relative request from what it resolved to, so a chain of exactly 40 links is not refused for a link the caller never wrote. An
  absolute request is walked as written. macOS's kernel bound is 32 and Linux's 40: a chain of 33 to 40 links is accepted by the
  resolver on macOS, and a later open of the ORIGINAL spelling would fail with ELOOP — a failure, never a read. Every guard acts on
  the resolved path, which has no link left, so the higher budget cannot widen what is read.
- `..` goes to the parent of the real path resolved so far, never above its root.
- A name that is not there ends the walk and the rest is kept as written. A `..` after such a name FAILS: the disk
  answers "no such file", and a lexical pop would answer a folder nobody looked at. A DANGLING link is a link, not a
  missing name: it costs a hop and its target is walked.
- **Nothing is below a file.** `file/.`, `file/..` and `file/name` fail (the disk answers ENOTDIR); a skip or a pop to the file's
  neighbour would read a file nobody named.
- A NUL, an empty link text, a drive-relative `C:x` (also as the whole of a path given to `ResolveParent`), a path or link text over 32 KiB (`RealPath.MaxPathChars`), a resolved
  path over 32 KiB, and any IO error are a failure. Callers turn the failure into refuse, not ours or cannot compare — never
  into the spelling, with one named exception (`ConsultationService.RepoIdentity`, below). A Windows target `\x` (a root with no drive) resolves against the LINK's volume.
- Containment compares the resolved path with the equally resolved root: equal, or the root plus a separator. Never a bare
  prefix.

`TheLinkResolverIsOneRoadTests` fails for any other `ResolveLinkTarget` in production code, and for a new reader of
`.LinkTarget` that is not listed there with its reason.

## What the sites guarantee

- **The document reader** (`review_document`, `FeaturePlan`, and the list and search a consultant is served by
  `WorkspaceLookup`): a path whose real path is not inside the repository (or a granted root) is refused, and so is a
  path the resolver cannot follow to its end. A path that resolves to nothing — a dangling link, a file removed since — is
  refused as "no file", never an unhandled exception. The text of a document is sent to three other vendors, so this is the
  boundary that keeps an outside file out of that. Refusals name the resolution as the reason and never echo more than
  200 characters of a path, with control characters made visible.
- **The question consultant's roots** (`QuestionConsultSettings`): a root is judged at its real path against the profile,
  the data directory, system directories and credential folders; a root that cannot be resolved is refused.
- **The `coai-wt-*` worktree deletion guard** (`WorktreeManager.OurTree`, which stands before
  `git worktree remove -f -f` and a recursive delete): a tree is ours only when its REAL path is strictly under the REAL
  storage root and the last name is not a link. There is no lexical alternative, and a resolution failure means NOT ours.
  The guard returns the real path and every command (unlock, remove, delete, move aside, owner marker) is aimed at THAT path,
  never at the spelling it was handed.
- **The review-tree root's recursive delete** (`ReviewTreeRoot.EnsureGoneAsync`): the same rule, through `RealPath.ResolveBelow`,
  and the delete runs at the judged path. Its first form compared `GetFullPath` strings.
- **`WorktreePaths.Real` and `Same`** (git's `realpath` against a spelled path): `Real` throws `IOException` for a path
  that cannot be resolved; `Same`, a predicate, answers `false`. Neither falls back to the spelling.

- **A read root** (`HandleRead.Trust`, used by `AcpReadBoundary` and the private-folder check): the root's canonical path is fixed
  when the reader is made, and the opened handle's own name is compared with it. A root the resolver cannot resolve has an EMPTY canonical
  path — never its spelling — and a read under it is refused, saying the root is the reason.
- **Containment before existence.** The document reader decides containment on the RESOLVED path first and only then asks what is at it:
  the existence checks follow links, so asked first they would say "is a directory" for a link to an outside folder and tell the caller
  what an outside path is.
- **One place keeps the spelling, on purpose:** `ConsultationService.RepoIdentity`, the owner label that filters a caller's own consultations. It
  is not an access decision, and both sides compute it the same way, so they agree whether or not either could resolve the path. Every
  other resolver failure is a refusal, "not ours" or "cannot compare".

## Lexical sites that remain, and why each is safe

`TheLinkResolverIsOneRoadTests` lists every production file that folds a path with `GetFullPath(` and tests it with `StartsWith(`, and
fails for a new one. The ones that remain, each with its reason:

- `AcpReadBoundary` — a request is placed lexically, then every component is lstat-ed (any link on the way is refused) and the bytes are read
  by handle, whose own name is verified.
- `FilesystemInvariant` — compares two paths git itself printed (absolute, already real) to fingerprint a repository; decides no access.
- `AtomicFile`, `Junctions` — a temp-file name, and a local-or-network classification by spelling; neither reads or deletes by the comparison.
  (`HandleRead`, which reads by handle, no longer folds a path at all: see "A read root" above.)
- `SubmoduleMounts` — chooses which declared mounts of a tree the product made are inspected (read-only `git status`); nothing is deleted by it.
- `SubmodulePopulator` — the clone source IS the folded path it checked, and `NoLinksBetween` lstat-s every directory from the checkout down
  to it, so no link can sit on the way.
- `KimiClearMarkerMode` — deletes one file whose direct parent string equals the product's own lock root and whose name is 16 hex digits plus
  `.survivors.json`; a link there is unlinked, never followed.
- `QuestionConsultSettings` — a deny-list of places; every root is compared as written AND at its real path, and an unresolvable root is refused.

## Limits, said plainly

- **Hard links and bind mounts are out of scope.** A hard link is one file with two names — there is no path to resolve —
  and a bind mount shows one tree at two places with no `readlink` to say so. No path check sees either.
- **Case.** A path is kept as spelled. Windows and macOS compare `OrdinalIgnoreCase` where the product always did
  (the worktree guard); the document reader compares `Ordinal` except on Windows, as `DocumentId.Comparison` says. On a
  case-insensitive disk a comparison that differs only by case is therefore a FALSE REFUSAL (fail closed), never a false
  accept. Open limit on macOS for the document reader; not widened here.
- **A check at one moment.** A folder swapped for a link between the check and the read or delete that follows is not seen. The two
  recursive deletes (`WorktreeManager`, `ReviewTreeRoot`) act on the real path they judged, which narrows the window and does not close it; a
  handle-pinned `openat`/`unlinkat` delete would ([todo/PLAN_a_delete_is_pinned_to_the_folder_it_judged.md](todo/PLAN_a_delete_is_pinned_to_the_folder_it_judged.md)).
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
