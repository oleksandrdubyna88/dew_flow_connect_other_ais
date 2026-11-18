# Security — what this product guarantees about paths, and where it stops

This file states the path guarantees of `coai-mcp` (`src_mcp`), and — in its own section — the one call over ssh that the VS Code extension makes. It is not a vulnerability-reporting policy; report a
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
- **The agy hook's path check** (`AntigravityReadOnly.Decide`, answered by `coai-mcp --agy-hook` and `coai-server --agy-hook`; measured in
  `research/RESULTS_agy_read_file.md` §6 and §7): a `read_file` or `view_file` call is allowed only when its `Path` is fully qualified
  and its real path (`RealPath.Resolve`, links and `..` followed) is inside the real path of a granted root from the payload's
  `workspacePaths`, and not inside the real path of the coai folder. A relative path, a missing or
  malformed field, a second spelling of `Path`, a duplicate JSON property and any resolution failure deny. It fails closed and never
  falls back to the spelling. The reason does not echo the path. On the Team server this keeps one job's reviewer out of another
  job's folder under `/tmp`, which agy's own boundary allows. Since 2026-11-18 (the plan `todo/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md`):
  - **The hook knows its own folder.** The coai folder is the parent of the hook process's own working folder, which agy sets to the
    `.agents` folder that holds the hook script. The payload's `cwd` names nothing: agy sorts `workspacePaths` and sends its first entry as
    `cwd`, so `cwd` can be a granted root. A hook that does not run from a folder named `.agents`, or whose folder does not resolve,
    denies every read and writes one constant line to standard error.
  - **No network or device path is ever looked up.** Every look at the disk goes through one guarded reader that refuses a path that is
    not local before any I/O, and every resolved path must be local. That covers `Path`, every root, `cwd`, the coai folder and the target
    of any link met on the way, so a junction inside a granted root that points at a share is not followed to the network. A `Path`
    that starts with two separators is denied by its spelling.
  - **On Windows a `Path` with a `.` or `..` segment is denied**, because Win32 folds those as text before it follows a link. The rule
    covers `Path` only. Roots, `cwd` and the coai folder are text from coai and agy.

  Limits:
  - A link swapped between the hook's check and agy's open is not closed. The check is at one moment.
  - Hard links and bind mounts are not seen by any path check.
  - Inside a granted root nothing is narrowed: `.git` and credential-looking files stay readable by agy's reader, though coai's own
    list and search hide them.
  - Only `Path` is read. Whether any agy reader takes a second file argument is not measured, nor is `view_file`'s argument name on
    agy before 1.5.9. A call without `Path` is denied.
  - An agy older than 1.5.9, whose payload lacks `Path`, `cwd` or `workspacePaths`, has every read denied.
  - Every `workspacePaths` entry is a root, and the coai folder is kept out of them by the rule above. agy builds the entries from coai's
    `--add-dir` arguments. If agy added a folder of its own, it would be a root.
  - It trusts where agy starts the hook (the hooks file's folder). If a later agy starts it elsewhere, every read is denied, and the line on
    standard error says why. That is the safe side.
  - **A checkout on a network place loses agy's reads.** A root on a UNC path, a mapped network drive or `\\wsl$\…`, and on Unix a root
    under `/net/`, `/misc/` or `/Network/`, is not supported: every agy read under it is denied. A consult grants the person's own
    checkout, so one kept in such a place gets no agy reads.
  - A network file system mounted into the Unix tree at an ordinary folder (nfs, cifs) is spelled like a local path and is not detected.
    Its credentials are the mount's own. A custom autofs map on another root is not detected either.
  - The dot rule covers `Path` only, and only on Windows. A harmless `..` in a Windows `Path` is denied too, and the model can resend the
    path without it.
  - **A dot segment in a link's own text is not judged, and on Windows that is a probable escape (found by the Opus review of epic E1,
    2026-11-18; not measured on a real machine).** On Windows a relative SYMBOLIC link inside a granted root whose stored text contains `..`
    is walked by coai's resolver with the Unix rule: `..` goes to the parent of the path resolved so far, so the hook judges the target
    inside the root. NT folds the same link text as text, so it reads outside. Example: `R\a` is a symlink to `b\..\..\outside\secret.txt`
    and `R\b` is another symlink, to `deep\er`. The resolver walks `R\b` to `R\deep\er`, applies the two `..` to that, and lands inside `R`;
    NT applies them to the text `b\..\..` and lands above `R`. Preconditions: symbolic links (not junctions: a junction cannot hold a
    relative or dotted text) inside the root, which need Developer Mode or administrator rights, or a git checkout with
    `core.symlinks=true` that holds such a link. The follow-up plan is
    `todo/PLAN_the_agy_hook_on_windows_judges_a_links_dot_text.md`. History, kept: until 2026-11-18 this limit read "A dot segment in a
    link's own text is not judged. What NT does with an absolute link target that holds `..` is not measured." The absolute-target case is
    still not measured.
  - **Another spelling of one entry** (an 8.3 short name, a trailing dot or space, an NTFS stream name) is a different string, and it
    fails in one of two directions. An 8.3 alias of a ROOT is refused, which is a false denial. An alias of the COAI FOLDER under a wide
    root (for example `home.`, an 8.3 name, or `::$INDEX_ALLOCATION`) is not seen as inside the coai folder, so that folder's files can be
    read. The folder holds no secret. Neither spelling leaves a root.
  - Case is ignored, as `DocumentId.Comparison` does, so on Windows in a directory made case-sensitive a sibling that differs only by case
    would be accepted. On macOS a different case is refused wrongly.
  - Windows measured 2026-11-18 on one build (10.0.26300) with agy 1.5.11: the payload has the same fields as on Linux, agy denies a read
    in the user profile by itself and allows a read under the temp folder, and the model sent no `.` or `..` in 11 ordinary runs. Whether
    agy cleans a path before it opens it is not measured. That Windows sends no NTLM handshake is shown by a recording reader in the tests;
    no packet capture was taken (`pktmon` needs administrator rights; the real run in `research/RESULTS_agy_read_file.md` §7 says so).
  - **Windows, closed by the plan `todo/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md` (2026-11-18), see `research/RESULTS_agy_read_file.md` §7.**
    Before it, this item read (kept as history): "Windows, not closed here: (a) `..` after a link or junction. `RealPath` applies `..` to the
    path resolved so far, the Unix rule. Win32 folds `..` as text before it follows links. A path such as
    `<root>\node_modules\junction\..\..\..\..\Users\me\.ssh\id_rsa` can pass the hook and be read outside the root, if agy itself allows
    that read (not measured on Windows). (b) A UNC or device `Path` (`\\host\share\x`, `//host/share/x`) is resolved before it is judged,
    so the hook looks it up over SMB. That can send the user's NTLM credentials to that host or run past the hook's 10 s timeout. Neither
    applies on Linux, the Team server or WSL." Also in that text: the rule said "not inside the real path of the payload's `cwd`, the folder coai owns" (see the first bullet above for why that was wrong), "agy on Windows is not measured" (now measured, above) and "Every
    `workspacePaths` entry other than `cwd` counts as a root" (the payload's `cwd` is no longer trusted, see above). W1 was confirmed in
    the real 0.66.0 hook and was exploitable end to end under the temp folder (RESULTS §7: the 0.66.0 control read the outside marker); the
    branch hook denies it on real Windows. W2 is closed by the guarded reader: a direct UNC, `//` or `\\?\` `Path` was denied on real
    Windows. A junction to a UNC target was not run there (`mklink` to a UNC target needs administrator rights) and rests on the
    reader-seam tests and the Windows CI job.
  - Hook denials are not in agy's `denied_actions`, so coai's read follow-up does not fire for them.
  - agy's own checks still run after an allow. The hook only narrows.
  - The hook always answers and exits 0. What agy does when a hook times out is not ours.

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

## The extension's one outward ssh call

The ConnectOtherAIs extension (`src_vs_code`) makes one ssh call: when a terminal is called `Creds: <host> / <session>`, **CoAI: take the question**, **CoAI: add the question** and *Asked* of that terminal's chat read the Claude session that runs in tmux session `<session>` on `<host>`, over the local `ssh`. This section says what the extension PASSES and does there. Where it says "we pass", it states what is sent, not what the other side does with it: the effect of each option on a real server is observed only where `research/module_extension.md` says so (M-O1…M-O7 are owed, below).

- **Read-only, to a host the person agreed to.** The first read for a host asks once (a modal naming the host); the answer is kept, and a "no" keeps that host clipboard-only until the person changes it in **CoAI: hosts read over ssh**. No ssh child is started before a "yes". The host is taken from the LIVE name of the terminal, and the name is checked again just before every spawn; a name is a request, never proof.
- **Whose configuration, whose keys.** The host is an alias in the person's own `~/.ssh/config`, which is the authority for it (`ProxyJump`, `ProxyCommand`, `IdentityFile` and the rest). The keys and the agent are the person's, through that configuration. The extension handles no key, no password and no secret, and none is in an argv or in a log. We pass `BatchMode=yes`, which tells ssh not to ask for a password or a passphrase.
- **No forwarding, no terminal.** We pass `-T`, `RequestTTY=no`, `RemoteCommand=none`, `ClearAllForwardings=yes`, `ForwardAgent=no` and `ForwardX11=no`, `ConnectTimeout=5` and `LogLevel=ERROR`, before the person's own configuration (for each setting ssh keeps the first value it is given).
- **Argv only.** The program is started with its words as an argument list, `shell` off, `--` right before the host. The host and every operand are validated by a rule whose alphabet has no space, tab, quote or shell character, checked again at the one road in; each operand is single-quoted for the REMOTE login shell, and the script travels on stdin, never in an argument. The argv node hands `ssh.exe` on Windows is the argv we build; that a real call from Windows delivers it unchanged is owed (M-O1).
- **Which `ssh`.** ONE absolute path for each platform (Windows `<SystemRoot>\System32\OpenSSH\ssh.exe`, else `/usr/bin/ssh`), never the `PATH`, and the child's working folder is that executable's folder — the extension host's own folder can be the opened workspace. The cost: a person whose terminal uses another ssh gets the system one here; the call then ends with ssh's own line, not with a wrong answer.
- **Bounded, and nothing left running.** 15 seconds, then we kill the ssh child: on Windows with `taskkill /t /f` on its pid (to take its children too), elsewhere by signalling the ssh child only (`processLauncher.ts`, `killTree`) — on Linux and macOS a process that ssh itself started, such as a `ProxyCommand`, is ssh's to end, and that it does is not observed; at most 8 MiB of stdout, counted in bytes; one read per terminal at a time and at most three calls for one press. The remote script is one function that reads, writes and starts nothing in the background, and leaves nothing on the VM.
- **The transcript stays in memory.** What comes back is input from another machine: it is parsed by a strict frame, never run, never used as a local path, never logged and never part of an error's text. The one sentence that CAN carry text from the VM is the refusal a door shows when it finds no question or cannot settle the session: it may name the pane's working folder on the remote host, or a session id you chose. That sentence is shown to you and is KEPT in the notifications ledger like every notice. The log of a call holds the host, the session name, how it ended and ssh's own stderr tail, never stdout and never an operand. The question you take or add IS saved with the conversation, as every passage is. The prompts *Asked* shows are not saved: they are read again at each press. The rest of the transcript is dropped after the parse.

**Plan change 2.1 (host keys).** The read passes `-o StrictHostKeyChecking=yes`, before your own configuration. With it, ssh refuses a host whose key is not already in your `known_hosts`, and a host whose key changed, whatever your configuration says (`accept-new` or `no` included); the read then ends with ssh's own sentence. A host your terminal's ssh already trusts is read as usual. (We pass the option; that a real host not in `known_hosts` is refused and nothing is written is owed, M-O3.)

**The residual of change 2.1 (connection sharing).** The read does not turn off ssh connection sharing. On Windows, the OpenSSH client that comes with Windows has no released connection sharing, so a read cannot share a connection or leave one open. When the extension runs on Linux or macOS — or on the remote machine of a Remote window — and your ssh configuration shares connections (`ControlMaster`, `ControlPersist`), the read can go through a connection that is already open, or open one that stays open for the `ControlPersist` time, as your configuration says.

Why the read does not turn connection sharing off: `-o ControlPath=none` was proposed and NOT taken, because it is an independent cause and so a second change of epoch 2, and a further ssh option is a full stop.

**What a shared connection means for the host key.** The host-key check of change 2.1 happens when ssh opens a NEW connection. A read that goes through a connection that is already open (the residual above) does not check the host key again: ssh checked it when that connection was opened, under the settings in force then.

**Owed measurements (M-O1…M-O7).** A real call from Windows (the argv reaches `ssh.exe` unchanged); the size of the residual on Linux; the refusal of an unknown or changed host key; no `ssh` process left after a read killed at the limit, `ProxyJump` included; the `comm` of the Claude CLI on the VMs; tmux's exact `-t "=name:"` match; whether the registry's `cwd` follows a `cd`. They are observed in the owner's R6 and R7 checks on a pre-release build, and until then this section says what is passed, not what is achieved.

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
