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

## The extension's outward ssh calls

The ConnectOtherAIs extension (`src_vs_code`) makes **two kinds of ssh call**, both to a host that is the alias in the name of an open terminal called `Creds: <host> / <session>`, both through the local `ssh`, both read-only. (A third kind, the install of `jq` on a VM, is a candidate of the accounts plan and is not in the code.)

1. **A press.** **CoAI: take the question**, **CoAI: add the question** and *Asked* of that terminal's chat read the Claude session that runs in tmux session `<session>` on `<host>`.
2. **A timer.** A round of the AI accounts list, 30 seconds after the window has an eligible terminal and then 10 minutes after the END of each round, with **no press**, calls each host of the open `Creds:` terminals that is not refused, one at a time, and reads which Claude Code, Codex and Antigravity accounts are signed in there (and, in a build that turns it on, their usage).

This section says what the extension PASSES and does there. Where it says "we pass", it states what is sent, not what the other side does with it: the effect of each option on a real server is observed only where `research/module_extension.md` says so (M-O1…M-O7 are owed, below). **What a usage read writes and starts on a VM (W6 and W7 of the accounts plan) is not judged here**: it is measured in E2, and this section is completed from what was seen. E1 ships the Claude usage read OFF.

- **A host is contacted unless the person refused it.** (This replaces *"The first read for a host asks once"* and *"No ssh child is started before a "yes""*, which were true until the accounts plan.) One function, `consentOf` (`remoteHosts.ts`), says whether a host may be contacted, and the door of the round (`admit`) asks it just before every call. With **`coai.terminalAccountsAllowNewHosts`** on (the default), a host with no answer is allowed by the accounts round itself, with no question, and the call to it follows within 30 seconds to 10 minutes. Only **Not this host** stops a host: a "no" always wins, whatever the setting, whichever window or round comes next. The host is taken from the LIVE name of the terminal, and the name is checked again just before every spawn; a name is a request, never proof.
- **What a terminal's name can set off (the plan's finding F1, in plain words).** A host nobody was asked about is read by a terminal's name. Any program that can open a terminal in the window — another extension, or a task of an opened workspace — can call it `Creds: <host> / x`, and within 30 seconds to 10 minutes the extension logs in to that host as the person and runs the fixed script, and again every 10 minutes while the terminal is open. What limits it: the name chooses only the host, from an alphabet with no option and no shell character; we pass `StrictHostKeyChecking=yes`, `BatchMode=yes` and `ForwardAgent=no`, so ssh is asked to refuse a host whose key is not already in `known_hosts`, to ask for no password and to forward no agent (we pass the options; the effect on a real host is owed, M-O3); nothing of the reply goes back to whoever opened the terminal. What does not limit it: every host the person has ever logged in to with a key can be reached this way. A name never gives a person's "yes", a command, an option or an operand, or a way around a "no". The person is told afterwards (the daily notice and the list). **Turning the setting off restores ask-first.**
- **How a new host is allowed, stored and shown.** The answer of each host is a row in `globalState` (`coai.terminalHosts`), at most 100, one for each host: a person's "yes", a person's "no" or an automatic "yes" (written by the round only over a host that has no row). Every change is a commit of the change for ONE host: the stored value is read and the write is called in the same step, so a stale variable is harmless (a stale WINDOW is the file's business, below). A "no" outranks a person's "yes", which outranks an automatic one. At the cap an automatic row goes first and, for a person's own change, then the oldest person's "yes"; **a "no" is never dropped**, and when no row can go the person's answer is not kept and the person is told. A hundred invented terminal names fill the store with automatic rows and push each other out; they never push out a person's row. The list says how every host stands (*allowed automatically on <day>*, *not asked yet*, *turned off*), the hosts command *CoAI: hosts read over ssh* lists and changes the answers (*Read*, *Not this host*, *Forget*), and a notice names the hosts the round allowed by itself at most once a day, with the button **Open settings**. With the setting on, a *Forget* lets the host be allowed again by itself; with it off the host is asked about again, and an automatic "yes" counts as no answer until a person presses *Read*.
- **The setting, and who may set it.** We declare `coai.terminalAccountsAllowNewHosts` with scope `application`, and we READ it from the person's own user layer only, afresh at every call, through the one reader that does not go through a side's overlay; a `.vscode/settings.json` of a workspace or a folder that sets it, on or off, decides nothing, and the list says in its first row that one tried. What VS Code hands the extension for an `application` key set in a workspace was not observed (R2, R11). An imported config file is refused this key by name, and an export never carries it. Other code that runs in the editor can write the user setting itself, as it can start `ssh` itself: the setting is not a boundary against code that already runs there.
- **The refusals file.** A person's "no" and the Stop of the accounts round are kept in a FILE, one for each SIDE of the machine (Windows, a WSL distro, a remote machine), `terminal-hosts/refused.json` under the extension's `globalStorageUri`, not in `globalState`. **Why `globalState` alone cannot keep one:** a window sends the extension's WHOLE `globalState` object on any write and replaces its whole copy when another window's write arrives, so a window with a stale copy that writes ANY key puts old values back, and can erase a "no" or the Stop for good with nobody told; the lag was measured only as an upper bound of 20 seconds. The file holds at most 100 refusals and one Stop, never more than 64 KiB, and only a person's press shortens it: a *Read* or a *Forget* removes a refusal, a start removes the Stop; no round, reload, second window, full list or damaged list removes one. It has ONE writer, which **claims** an exclusive-create lock, **writes** the file by a rename beside it, and **reads back**: it never reports a change as kept unless it sees it in the file, it merges again into a file another writer changed, and it merges and writes again up to three rounds before it says the write did not stay. The decision to contact a host reads the file from the disk as the last thing before it answers; no copy kept between two decisions answers for it.
- **What a write that does not land does.** A refusal or a Stop that the file did not take is held in the copy in `globalState` at once and in a small marker file (`unsure-<token>.json`, at most 1 KiB each and 32 in the folder) that every window of the side reads, and **while a marker or a held change waits, no window of that side contacts any host**. That holds only when a marker was made: with no marker (a full disk, say) only the pressing window stops reading. A warning says so, with ONE action, **Save now**: a person's press that takes the lock over, lands what waits and removes a marker that cannot be read, and only when it removed one does it stop the round. If no marker could be made, the notice says that only this window knows the answer, must stay open and that other windows of the side may still read hosts.
- **The lock is never taken over by its age.** It is taken over when its owner is shown to be gone on this machine (another boot, or no process of that number) and by a person's press; every doubt waits for a person. A window that sees the file go back writes what it remembers again. A "no" seen in `globalState` — the copy of a window of an older build, or of another side — is copied into the file at every read. **We describe this as what the writer does — it claims, writes and reads back — and never as "a refusal cannot be lost":** that an exclusive create admits one writer and a rename replaces the file whole on the person's own disk, and what the system answers about a process that is gone, were not observed here.
- **A list or a file that cannot be read contacts nobody.** A list of hosts with a row that is not whole, or a refusals file that cannot be read, stops every accounts read of that side until a person's own answer for a host mends it; the mend of the list keeps the rows that can be read, and the mend of the file keeps only the refusals the list still holds (one kept only in the file is lost, and the question says so) and **leaves the round stopped**. No notice says that either is damaged: a person learns it from a host's row, the session door or the hosts list. A state row of the round that cannot be read is a stopped round.
- **The stop button.** *Stop the accounts round*, in the list, stops the reads in every window of the side until a person chooses the one row that then says *The accounts round is stopped. Choose this row to start it.* The Stop is written to the refusals file first and to a copy in `globalState` second, is read from the disk before every call, and is removed only by that person's start. A call that is already running when a refusal or the Stop is written runs to its end (30 s at most) and its result is dropped; a call that starts afterwards is not made.
- **Whose configuration, whose keys.** The host is an alias in the person's own `~/.ssh/config`, which is the authority for it (`ProxyJump`, `ProxyCommand`, `IdentityFile` and the rest). The keys and the agent are the person's, through that configuration. The extension handles no key, no password and no secret, and none is in an argv or in a log. We pass `BatchMode=yes`, which tells ssh not to ask for a password or a passphrase.
- **No forwarding, no terminal.** We pass `-T`, `RequestTTY=no`, `RemoteCommand=none`, `ClearAllForwardings=yes`, `ForwardAgent=no` and `ForwardX11=no`, `ConnectTimeout=5` and `LogLevel=ERROR`, before the person's own configuration (for each setting ssh keeps the first value it is given). The accounts call takes the same options from the same list.
- **Argv only.** The program is started with its words as an argument list, `shell` off, `--` right before the host. The host and every operand are validated by a rule whose alphabet has no space, tab, quote or shell character, checked again at the one road in; each operand is single-quoted for the REMOTE login shell, and the script travels on stdin, never in an argument. The accounts call sends ONE fixed text, `ACCOUNTS_SCRIPT` (no parameter can replace it), and its one operand is one of two closed words, `claude-usage` or `no-claude-usage`, checked by a membership test here and again by the script; no value from a terminal's name or from a VM is ever an operand. The argv node hands `ssh.exe` on Windows is the argv we build; that a real call from Windows delivers it unchanged is owed (M-O1).
- **Which `ssh`.** ONE absolute path for each platform (Windows `<SystemRoot>\System32\OpenSSH\ssh.exe`, else `/usr/bin/ssh`), never the `PATH`, and the child's working folder is that executable's folder — the extension host's own folder can be the opened workspace. The cost: a person whose terminal uses another ssh gets the system one here; the call then ends with ssh's own line, not with a wrong answer.
- **Bounded, and nothing left running.** A press: 15 seconds, then we kill the ssh child; at most 8 MiB of stdout, counted in bytes; one read per terminal at a time and at most three calls for one press. The timer: 30 seconds and 64 KiB for a call, one call at a time for each window, one timer for each window (cleared with the last eligible terminal and by the stop button). We kill on Windows with `taskkill /t /f` on its pid (to take its children too), elsewhere by signalling the ssh child only (`processLauncher.ts`, `killTree`) — on Linux and macOS a process that ssh itself started, such as a `ProxyCommand`, is ssh's to end, and that it does is not observed. The session script is one function that reads, writes and starts nothing in the background, and leaves nothing on the VM. **The accounts script writes no file and installs nothing** (it only NAMES a package manager for a host without `jq`, and never runs one); it starts the vendor programs in the foreground, each under `timeout`, and the text is sent as one function and a call in one brace group, so that a text cut on its way is a syntax error and starts nothing (seen on a shell in the spec's checks, not on every shell).
- **Road (a): no secret passes through our script, and what the VM then calls.** The accounts script opens no credential file, holds no token in a variable and gives none to any program: it never opens `~/.claude/.credentials.json` or `~/.claude.json`; `~/.codex/auth.json` and agy's sign-in file are only tested for existence; Codex session files (a transcript: private, not a credential) are read by their last megabyte, three files at most, through `jq` programs that print the plan, the values of up to two windows and the two times of ONE record, and nothing else. **Claude Code reads its own login inside its own process.** On a usage read (not in E1's build) our script starts `claude -p "/usage"` and Claude Code calls Anthropic with the VM's own login: that is a NEW edge from a VM to a vendor. We PASS `--no-session-persistence`, `--strict-mcp-config` and `--setting-sources ""` to it, and `TZ=UTC` in its environment (a request that the reset is printed in UTC; a reset that does not say `UTC` is shown as a dash). A flag is a request until its effect has been seen: **no sentence here says that Claude Code starts no hook, no MCP server and no plugin**, and a child that a read leaves behind, or one that left the process group, is not looked for and cannot be ended (W7, E2).
- **What Claude Code writes on a usage read — measured, and not ours to prevent.** Four cache files of its own, in each of the four reads whose changed files were listed: `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json`, `~/.claude/policy-limits.json.stamp.json`, `~/.claude/policy-limits.json.signature.json` and `~/.claude/remote-settings.json.signature.json`. They do not grow. The account and version commands wrote no file. `~/.claude.json` and a backup of it were written during ONE read; by whom is not proven. Growth without bound (a backup file for each read, with nobody to retire it) and a live session that is disturbed are what W6 and R7 of the plan measure in E2.
- **The start of `agy`.** Every round starts `agy` on the VM with its version flag, for its version only; of its output only digits and dots leave the VM, within 4 seconds. What else `agy` does on a start (a self-update, for one) was not measured (W8), and our timer decides when it starts.
- **The person's activity is dropped on the VM.** The text of a usage read has a section about the person's requests, top subagents and top MCP servers: it never leaves the VM. Only lines that fit three anchored patterns (the session line and the week lines) do, at most eight, plus a count of the others and never their text.
- **What comes back stays in memory.** What comes back is input from another machine: it is parsed by a strict frame, never run, never used as a local path, never logged and never part of an error's text. For a press, the one sentence that CAN carry text from the VM is the refusal a door shows when it finds no question or cannot settle the session: it may name the pane's working folder on the remote host, or a session id you chose. That sentence is shown to you and is KEPT in the notifications ledger like every notice. The log of a call holds the host, the session name (`accounts` for the timer), how it ended and ssh's own stderr tail, never stdout and never an operand. The question you take or add IS saved with the conversation, as every passage is. The prompts *Asked* shows are not saved: they are read again at each press. For the timer, the reply is a closed vocabulary of at most 64 lines of 512 bytes; text in it (an email, an organization, a plan) reaches a row only through one function that takes control and format characters out, writes every `$(` as `$ (` and cuts at 200 characters; a value that looks like a credential makes that vendor unreadable (a net, not the measure: the measure is that the script opens no credential file). **Nothing of a reading is stored** — not in `globalState`, not in a file, not in a log; the list shows an account's email and organization on the screen (the owner's decision O2) and keeps them in the window's memory, dropped with the host's last terminal, a refusal, the stop and the window. The only command a row ever shows is one of four fixed `jq` install commands (`sudo apt-get install -y jq` and its three kin); no text of a reply is part of one, and CoAI never runs it.

**Plan change 2.1 (host keys).** The read passes `-o StrictHostKeyChecking=yes`, before your own configuration. With it, ssh refuses a host whose key is not already in your `known_hosts`, and a host whose key changed, whatever your configuration says (`accept-new` or `no` included); the read then ends with ssh's own sentence. A host your terminal's ssh already trusts is read as usual. (We pass the option; that a real host not in `known_hosts` is refused and nothing is written is owed, M-O3.)

**The residual of change 2.1 (connection sharing).** The read does not turn off ssh connection sharing. On Windows, the OpenSSH client that comes with Windows has no released connection sharing, so a read cannot share a connection or leave one open. When the extension runs on Linux or macOS — or on the remote machine of a Remote window — and your ssh configuration shares connections (`ControlMaster`, `ControlPersist`), the read can go through a connection that is already open, or open one that stays open for the `ControlPersist` time, as your configuration says. With the timer this means a persisting master can be opened by a round with no press.

Why the read does not turn connection sharing off: `-o ControlPath=none` was proposed and NOT taken, because it is an independent cause and so a second change of epoch 2, and a further ssh option is a full stop.

**What a shared connection means for the host key.** The host-key check of change 2.1 happens when ssh opens a NEW connection. A read that goes through a connection that is already open (the residual above) does not check the host key again: ssh checked it when that connection was opened, under the settings in force then.

**Residuals of the accounts calls, named.** Every host of an open `Creds:` terminal gets a login every 10 minutes, whether an AI CLI is there or not, until the person refuses it, and a VM's ssh log shows it. The list shows an email on screen. Each usage read is a start of Claude Code, with what Claude Code does on a start: its caches, its own network calls, possibly its own update check. A VM that prints a reset in another zone than UTC shows the percent and a dash for the reset. These are the thirteen residuals of the refusals file and the store (the S1 spec, § S1.10), written as what the writer does and never as "a refusal cannot be lost":

1. **A call that is already running** when a refusal or the Stop is written runs to its end, 30 s at most, and its result is dropped. A call that starts afterwards is not made, in any window of that side: the file is read from the disk as the last thing before the door answers.
2. **Another side of the machine** learns of a refusal or of the Stop through its copy in `globalState` only. It copies a refusal into its own file at its next read and keeps it from then on. Inside the lag after the press (measured only as an upper bound of 20 s) a stale write can erase the copy before that side has read it; that side then goes on reading the host, and nobody is told. The Stop is not copied in: on another side it holds only while its copy stands. The side where the person pressed is not affected. To be sure of a host on two sides, the person presses *Not this host* on each; the owner said a second side is used for the chat only, not for the accounts list, so a record of what was lifted stays a candidate (C4b).
3. **A person's "yes" that was withdrawn can come back.** With the setting OFF, a *Forget* of a person's "yes" puts the host back to *not asked yet*; a stale write inside the lag can put the old "yes" row back, and the host is then read again with no question. *Not this host* is the press that holds. It is the behaviour of the session door since 0.87.0, now with a timer behind it. With the setting on a forgotten host is allowed again in any case.
4. **A lifted refusal can come back — as its copy, and since the copy-in also into the file.** After a *Read* or a *Forget*, another window still holds the old "no" row until its view catches up. If it makes a decision in that time it copies the row into the file of its side, and a stale write in that time puts the row itself back. Either way the host is refused again until the person presses once more, on each side that took it. It errs to not contacting, and the list says so. The same follows a file that went back. The same holds for an old stop: the round is stopped again, and the list says so.
5. **The lock, the marker and the writer.** The lock is never taken over by its age. (a) **A lock whose owner cannot be shown to be gone** — a window that hangs with it, a note that was torn, another machine's lock over a shared folder, a process number given to a later program where no boot id can be read — makes every write of the side `busy` until a person presses *Save now*. A "no" pressed then is in its copy and its marker, and no window of the side reads a host. (b) **After *Save now* the former holder can still wake up and rename its stale file over the new one.** A window that read the newer file sees a lower `gen`, or the same `gen` by another writer, and writes the union back. A refusal is lost from the file only when no such window is open; the copy still counts and is copied in again, unless a stale write erased it too. (c) **Two windows that both judge one owner gone** can both write; the one whose change is missing sees it at its read-back or at its next read, and what is left is a window that ends in between. (d) **A disk that takes neither the file nor the marker** keeps the refusal in the pressing window's memory and in the copy, and nowhere else. If THAT window is closed, the copy is all that is left, and a stale write can erase it. The notice says that the window must stay open.
6. **A damaged file, or a damaged list,** stops every accounts read of that side until a person mends it. The mend of a file keeps only the refusals the list still holds; one kept only in the file is lost, and the question says so. The mend of a list drops the entries that could not be read; a refusal of THIS side is not lost by it, because it is in the file, and a copy that came from another side can be. No notice says that either is damaged. It fails closed, and silently.
7. **A file that is deleted from outside** reads as a file that was never written. Every open window that had read it sees a file that went back and writes what it remembers; the copy-in copies the copies back at the next decision. With no window open, the refusals stand as their copies only until then. The same code can delete a marker, or edit the file; so it can edit `known_hosts`. Not a boundary of this software.
8. **A window still running an older build** reads an automatic row as a person's "yes", also with the setting off; drops the oldest row at its cap, a "no" copy among them; does not read the file, so ITS session door reads a host refused only there, on a press; and a "no" given in it is a row and no file entry until a window of this build on that side reads the store, which copies it in. Until such a read it is a copy only, and a stale write can erase it. Until the old window is reloaded.
9. **A hundred refusals.** The next person's "no" is not kept, in the file or in the list. It is said to the person; that host is not contacted by the round while the list is full.
10. **A press in flight** is not stopped by a "no" given during it: at most three calls, 15 s each.
11. **The setting can be written by other code in the editor**, and Settings Sync may carry it to the person's other machines. It is still the person's own layer.
12. **With the setting on, the first contact comes before any telling.** That is finding F1, above, and the owner's decision.
13. **A side with a refusal that is not written reads no host at all — when its marker was made.** Every window of the side stops while the file cannot be written: one hung window's lock stops the accounts list of the whole side until the write lands or a person presses *Save now*. When NO marker could be made (a disk that is full or read-only), only the pressing window stops reading, and its notice says that other windows of the side may still read hosts. It errs to not contacting, and the notice and the list say it.

**Four limits that the building of E1 recorded** (not residuals of the file):

- **A Forget given in ANOTHER window does not bump this window's generation token.** The change signal of the keeper fires for this window's commits only, so a result in flight in this window is not dropped by it. With the setting off, the round's admission check before a result is published drops the reading only when this window's view of the list caught up before the result is back; the code has no bound for that (how long a second window takes to see a `globalState` write was measured elsewhere, at about 20 s). A reading that is already shown stays in its group. With the setting on, the host is allowed again by itself anyway.
- **E1 ships `USAGE_POLICY` `off`.** The list says *usage not read: CoAI does not ask Claude Code for usage in this build*; that sentence, and the help sentence that says the same, must change when a build turns the usage read on.
- **Text from a VM is cut at 200 characters** by the one plain-text function, so an email longer than 200 characters is shown cut.
- **A VM whose `sh` exits non-zero on every round writes two log lines for every round**, because the log gate counts any answered run. It is a log volume, not a leak: a line holds the host, how the call ended and ssh's own last line.

**Owed measurements (M-O1…M-O7).** A real call from Windows (the argv reaches `ssh.exe` unchanged); the size of the residual on Linux; the refusal of an unknown or changed host key; no `ssh` process left after a read killed at the limit, `ProxyJump` included; the `comm` of the Claude CLI on the VMs; tmux's exact `-t "=name:"` match; whether the registry's `cwd` follows a `cd`. They are observed in the owner's R6 and R7 checks on a pre-release build, and until then this section says what is passed, not what is achieved. For the accounts calls E2 adds the two gates named above (W6, W7), a start of `agy` (W8), two real windows (R12) and a remote window (R11).

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
