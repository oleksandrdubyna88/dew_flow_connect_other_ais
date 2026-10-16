# PLAN — Kimi Code and Mistral Vibe on Linux and WSL

> Status: **plan only, nothing implemented yet, 2026-10-16.** Scope: lifting D18 of
> [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) for a coai-mcp that runs ON a Linux host (Remote-WSL first) —
> Vibe first, because it is measured; Kimi after its WSL cells. Touches `src_mcp/runners/{Vibe,Kimi,Files,Processes,Platform}`,
> `src_mcp/src/Acp`, `shared/vibe-home-manifest.json`, `shared/consultant-limitations.json`, `scripts/` (the probes),
> `src_vs_code/src` (hints, install lines, help in five languages, `officialSources.ts`, `runtimeSince.ts`), and the RESULTS documents.
>
> Related docs: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (D17–D20, §4 S4),
> [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md) (E4.5's Linux notes),
> [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md), [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md),
> [PLAN_vibe_live_round_fixes.md](../research/PLAN_vibe_live_round_fixes.md) (D2's path budget, D7's `PrivateFolder` Unix leg),
> [PLAN_a_wsl_consultant_is_checked_from_windows.md](PLAN_a_wsl_consultant_is_checked_from_windows.md) (the Windows→WSL bridge, not this plan).

## 1. The symptom

On 2026-10-16 the owner works in VS Code Remote-WSL, so `coai-mcp` runs as `linux-x64` inside WSL. He installed and signed in to
both CLIs there (`~/.local/bin/vibe` 2.26.1, `~/.kimi-code/bin/kimi` 2.2.0) and asked for "a release where I can use them".
Today every Vibe and Kimi row on that side is refused before anything starts:

- Vibe: `VibeLaunch.HostRefusal` (`src_mcp/runners/Vibe/VibeLaunch.cs:77-82`) answers *"Vibe is refused on a wsl host until it is
  measured there (D18 …)"* for every host that is not Windows, and the reviewer, consultant and question row all pass through it
  (`VibeLaunch.cs:149`).
- Kimi: `KimiLaunch.HostRefusal` (`src_mcp/runners/Kimi/KimiLaunch.cs:28-33`) the same, through `KimiLaunch.cs:158`; behind it five
  more Windows-only mechanisms (§4 DL6–DL11) would refuse or silently do nothing if the first refusal were simply removed.
- The extension says so in the card hints (`src_vs_code/src/vendors.ts:294`, `:310`), the install notes
  (`src_vs_code/src/vendorTerminal.ts:488-493`, `:568-572`) and the help in five languages (`helpContent.ts:115`, `helpDe.ts:87`,
  `helpEs.ts:87`, `helpRu.ts:87`, `helpUk.ts:31`).

D18 was right when it was written: every confinement number was Windows. Since then a scout measured Vibe on WSL2 (n = 1 per arm,
2026-10-16) and the generic Linux lock and process semantics; Kimi on WSL was NOT run, because the owner's own `kimi` TUI was open
and Kimi's refresh token rotates (a second launch could sign him out).

## 2. The goal — what is true when this plan is done

1. On a Linux host (`HostKind.Linux` or `HostKind.Wsl`, `src_mcp/runners/Platform/HostKind.cs:17-24`) a Vibe row reviews plans and
   code, answers `consult` and a question-consultant row, with the same confinement as on Windows: read only the snapshot, no
   write, no shell, no MCP server, a coai-owned validated home, a built environment — **on the person's own sign-in, never copied.**
2. The same for a Kimi row, after its WSL cells are recorded: the owned `KIMI_CODE_HOME`, the sign-in by link, the refresher lock,
   the tree hold, the teardown that leaves no grandchild, the wait for the person's own `kimi`.
3. Every action is a panel button: the install button on Linux provisions what the launch needs (Kimi's `rg`/`fd`), and the card,
   the hint and the help say what the side's `coai-mcp` actually does.
4. What stays refused, with a sentence: a Windows `coai-mcp` that would start the Linux side (`HostKinds.RunsInWsl`,
   `HostKind.cs:39-47`), and macOS.
5. The owner rules hold: an unmeasured Vibe or Kimi VERSION warns and runs; only what cannot be made safe is refused; set it up
   once and it works.

## 3. The evidence, cell by cell

The scout's draft (session scratch, not in git; redacted) is carried into the RESULTS documents by E1 — never linked.

| Cell | What | State |
|---|---|---|
| L-V1 | A fresh `VIBE_HOME` on WSL is NOT signed in: no keyring; exit 1, free, `Error: Missing MISTRAL_API_KEY environment variable for mistral provider…` — the exact wording `VendorDiagnosis.cs:109` already matches. With `$VIBE_HOME/.env` a SYMLINK to `~/.vibe/.env` it answered 4 of 4; Vibe did not replace the link; the target kept its size and mtime. | measured, n = 1 |
| L-V2 / L-V3 | `--agent plan --enabled-tools read_file`: read inside the cwd served; read outside DENIED; `write_file` and `bash` DENIED; the real `~/.vibe` byte-identical before and after. | measured, n = 1 |
| L-V4 | `--resume` on the same owned home: the same session, the code word back. | measured, n = 1 |
| L-V5 | A 640-character `VIBE_HOME`, deepest path 775 characters: answered. The Windows 102/259 budget does not apply. | measured, n = 1 |
| L-V-home | `HOME` redirected to an empty folder: answered, nothing written there. | measured, n = 1 |
| L-V-manifest | The home holds the Windows inventory's names; of 12 pinned plugin files 11 hash differently (Linux plugin package id `908d84…`, Windows `6bb638…`); two unknown names: `.env` (the link) and `logs/session/unified/.session-index.json` (on resume). | measured, n = 1 |
| L-K5 | Generic Linux `flock`: the lock stays HELD after the parent's SIGKILL while the direct child that inherited the fd lives; a `close_fds` or `setsid` grandchild does NOT hold it; a grandchild handed the fd holds it. | measured (no Kimi) |
| L-K4 harness | With a FAKE ACP agent: killing the agent by pid leaves both children; killing its process group kills the same-group child, NOT a `setsid` child. | measured (fake agent, not Kimi) |
| L-K1 static | `kimi` 2.2.0 is a 191 MB ELF; `fd` is in `~/.kimi-code/bin`, `rg` is NOT; Kimi looks for `rg` on `PATH`, then `<share>/bin/rg`; a lazy download exists in the strings, unproven. | strings only |
| L-K1 live, L-K2, L-K3, L-K4 (Kimi) | — | **NOT RUN** (§5) |

The owner's OAuth slot on that machine is `oauth/kimi-code-env-<hex>`, not the default — the D19 rule (the slot READ from the person's
`config.toml`) already covers it. The scout found the owner's TUI by `comm` = `kimi` (`ps -eo comm=`).

## 4. Decisions

Numbered DL1–DL14 so they never collide with the parent plan's D-numbers. Every reference was opened on `03ec1c1a`.

- **DL1 — Scope: a Linux HOST, not a Linux CHILD of Windows.** Admitted: a `coai-mcp` whose own host is `HostKind.Linux` or
  `HostKind.Wsl` (`HostKind.cs:30-31`, `:50-56`). Still refused: the second arm of both host refusals, a Windows host starting
  `wsl.exe` or a `\\wsl$` path (`HostKinds.RunsInWsl`, `HostKind.cs:39-47`) — a Windows process cannot take a Linux lock (`HeldFile`
  states that no lock crosses the 9P boundary, `src_mcp/runners/Files/HeldFile.cs:17-22`), cannot list or end a Linux process tree,
  and the Windows→WSL bridge is another plan's. macOS stays refused (nothing measured there). **Native Linux runs the same code
  as WSL**: every mechanism below is the kernel's or the filesystem's, and WSL2 is a real Linux kernel; what differs on a desktop
  Linux is a keyring Vibe may use instead of `.env` — handled by DL2's "no `.env` → no link" branch. The `linux` limitation rows say
  "measured on WSL2" (owner question Q1). The host refusal becomes per runtime and per stage: Vibe admits Linux/WSL from E2, Kimi
  from E6.

- **DL2 — Vibe's sign-in on Linux is a SYMLINK `$VIBE_HOME/.env → <person's Vibe home>/.env`, the one link an owned Vibe home
  may hold.** *Security / authentication.* The alternatives, each rejected with its reason:
  - *the key in the child's environment* — coai would have to READ and hold the secret, and every process Vibe starts inherits it
    (`/proc/<pid>/environ`, crash dumps); the key would pass through coai's own code, which today never touches a vendor secret;
  - *a copy of `.env` in each owned home* — one copy of the key per round and per conversation, each a file the sweeps must scrub,
    and a key the person rotates leaves stale copies working; D19 refused copies of a sign-in for the same reason;
  - *the keyring* — WSL has none (L-V1).

  So the link, with every condition the D17 "a link anywhere refuses" rule (`src_mcp/runners/Vibe/VibeHome.cs:97-100`) needs to
  admit exactly one exception:
  - **where**: the person's Vibe home is `VIBE_HOME` of coai's OWN environment when set, else `~/.vibe` (the `KIMI_CODE_HOME` rule
    of D19); the target is exactly `<that>/.env`, absolute;
  - **the target**: a regular file by `lstat` (never a link — coai never chains links, the `SnapshotLinks.IsLink` rule), owned by the
    user coai runs as, not group- or other-writable (whoever can write it configures Vibe), at most 64 KiB;
  - **its variable NAMES only**: each non-empty, non-`#` line, an optional `export `, the text before the first `=`; the names must be
    exactly `{MISTRAL_API_KEY}`. A `VIBE_*` name in that file would reach Vibe as configuration — the channel D17 closes for the
    environment (`VibeLaunch.cs:90-93`; RESULTS §22 A). The VALUE bytes are never stored, compared, logged or returned. Any other
    name refuses with the names listed (never a value) and the cure (owner question Q2);
  - **who makes it**: the launch control, inside the home's turn, after the shape check and before `--version`
    (`VibeLaunch.TakeAsync`, `VibeLaunch.cs:146-172`), on a non-Windows host only — so the reviewer, retry, consult, question row,
    consultant check and `providers` probe get it from one place;
  - **what is admitted**: in `VibeHome.Judge` (`VibeHome.cs:94-103`) a link named `.env` at the home's top level, on a non-Windows
    host, whose ONE-level target (`Junctions.Target`, `src_mcp/runners/Files/Junctions.cs:122-134`) string-equals the expected path.
    Any other link, a `.env` that is a file, a `.env` on Windows, a `.env` one level down — refused as today;
  - **no `.env`** at the person's home: no link is made and Vibe runs; it exits 1 with L-V1's wording, free, and
    `VendorDiagnosis.cs:109-112` names it — its cure text becomes per host (Linux: *run `vibe --setup` in a terminal on this side;
    the sign-in is `~/.vibe/.env`, which coai links into its own homes and never copies*). No pre-check: a desktop Linux keyring may
    sign Vibe in without a `.env`;
  - **deletion**: `GitScratch.Unlink` already removes a FILE link by its own name and checks the target after
    (`src_mcp/runners/Files/GitScratch.cs:55-71`, `:116-133`); E2 adds the red-first Linux test that every deleter (round home,
    one-shot sweep `VibeHomes.SweepOneShot`, the D20 consult sweep, the test sweeps) leaves the target's bytes, mode and mtime as they
    were. `Writable` (`GitScratch.cs:85-92`) is never asked of a link — the test pins that a `chmod` never reaches the target.

  Why linking the key file into a coai-owned home is acceptable: the home is `0700` (`PrivateFolder`'s Unix leg,
  `src_mcp/runners/Files/PrivateFolder.cs:80`), so no other user can follow the link; the link is outside the snapshot Vibe reads,
  and a read outside the cwd is denied (L-V2) — a prompt-injected branch asking `read_file` for `$VIBE_HOME/.env` meets the same
  refusal as `~/.ssh`; on Windows the same key sits in the Credential Manager, which every Vibe launch reads anyway (RESULTS_vibe §12) — the
  exposure is the same, only its place differs. **One unknown, measured before E2 ships (L-V8):** whether Vibe also loads a `.env`
  from its WORKING folder — the reviewed branch's own `.env` in the snapshot. If it does, `.env` joins Vibe's snapshot exclusions
  on every host (the reviewed branch would otherwise set `VIBE_*` through it).

- **DL3 — The Vibe home manifest is per platform.** The JSON already carries `"platform": "win32"`
  (`shared/vibe-home-manifest.json:33`, written by `scripts/vibe-home-manifest.mjs:176`), and the C# drops it (`ManifestVersion`,
  `src_mcp/runners/Vibe/VibeHomeManifest.cs:115-120`; `For(version)` at `:46`). So: `VibeHomeEntry` gains `Platform` (`win32` |
  `linux`, as node spells it; required by `Complete`, `:70-72`); `For(version, platform)`; `VibeLaunch.Verdict` and `VersionWarning`
  (`VibeLaunch.cs:306-317`) pass THIS process's platform; `Known` names both (`2.26.1 (win32), 2.26.1 (linux)`). A version measured on
  another platform only is an UNMEASURED version here: it warns and runs with the shape check (owner rule), and the warning says on
  which platform it was measured. Today, without this, 2.26.1 on Linux is a KNOWN version whose plugin bytes differ — the one hard
  refusal (`VibeHome.cs:173`, `ByHash` at `:188`) — so every Linux Vibe launch would be refused. `logs/session/unified/.session-index.json` joins
  `varying` on every platform (session data, L-V-manifest). `VibeHome.Unhashed`'s "measured anywhere" admission of a starter file
  (`VibeHome.cs:176-183`) stays across all entries: those are bytes Vibe itself wrote. The `linux` entry is WRITTEN BY THE SCRIPT in
  WSL (E1), never by hand. **If two fresh-home runs disagree on the hashes (L-V7)**, there is no `linux` entry and Linux is always
  warn-and-run; the plan records it as the deviation.

- **DL4 — No Linux path budget.** `VibePathBudget.ForThisProcess` is already `None` off Windows (`src_mcp/runners/Vibe/VibePathBudget.cs:33`);
  L-V5 wrote 775 characters. No sanity bound is added: a path past `PATH_MAX` fails inside Vibe with its own error, which the
  generic diagnosis reports, and no measured case needs more.

- **DL5 — The CLIs are found where their installers put them, not only on the editor's `PATH`.** The VS Code server's `PATH`
  often lacks `~/.local/bin` (seen on the owner's machine under Remote-SSH), and `~/.kimi-code/bin` reaches only shells started after
  Moonshot's installer edited an rc file.
  - Vibe: `VibeRuntime.InstalledExecutableFor` (`src_mcp/runners/Vibe/VibeRuntime.cs:80-85`) is Windows-only. On Linux:
    `$UV_TOOL_BIN_DIR/vibe`, else `~/.local/bin/vibe` (uv's tool folder, where Mistral's script puts it — the scout found it there),
    when it exists and is executable; else bare `vibe` (the `PATH` walk), else the install cure. uv's other documented bin-folder
    variables are re-read from uv's storage docs when the story is written, not guessed here.
  - Kimi: `KimiAcpProfile.InstalledExecutableFor` (`src_mcp/runners/Acp/KimiAcpProfile.cs:135-140`) and `KimiPlaces.ExecutableOf`
    (`src_mcp/runners/Kimi/KimiPlaces.cs:71-83`, the Windows-only `inBin` at `:74`) gain the Linux arm
    `<KIMI_INSTALL_DIR or ~/.kimi-code>/bin/kimi`.
  - Kimi's tools: `KimiTools.FoldersFor` (`src_mcp/runners/Kimi/KimiTools.cs:37-48`) searches the person's bin, then `PATH`; on Linux
    `~/.local/bin` is a third place (where DL12's `fd` link goes), so a found tool's folder joins the built `PATH` through the existing
    `ToolFolders` (`KimiPlaces.cs:24-27`).

- **DL6 — Kimi's credentials link on Linux is a directory symlink.** *Security / authentication.* `Junctions.Refusal` refuses
  first with `WindowsOnly` (`Junctions.cs:60`, `:141-143`). On Linux the same `Make`/`Refusal`/read-back runs over a product Unix
  shell of `IJunctionShell` (`src_mcp/runners/Files/JunctionShell.cs:9-22`) — the shape the test stand-in already has
  (`src_mcp/tests/WindowsStandIn.cs:52-65`), promoted: `Directory.CreateSymbolicLink` at a path that does NOT exist (no
  delete-then-create window, unlike the stand-in's `:60-61`), read back by `Junctions.Target`. The locality rule
  `IsLocalUnixPath` (`Junctions.cs:97-101`) gains, on WSL, `/mnt/<letter>/`: a sign-in folder on the Windows drives is shared with a
  Windows Kimi that no Linux lock can see (`HeldFile.cs:17-22`) — two refreshers of one rotating token. It is judged on the real path
  (`Resolved`, `Junctions.cs:179`). coai never `chmod`s the target (Kimi keeps it `0700`, RESULTS_kimi §11). **Waits for L-K2.**

- **DL7 — The Linux lock key is the folder's identity: `(st_dev, st_ino)` from `statx`.** *Security / authentication.*
  `KimiCredentialsLock.KeyFor` (`src_mcp/runners/Kimi/KimiCredentialsLock.cs:116-120`) hashes `HandleRead.FinalDirectory`, which is
  `""` off Windows (`src_mcp/runners/Files/HandleRead.cs:226-238`), so the key is empty and the lock `Unusable` — fail-closed by
  design (E4.2 decision 9; the remark at `KimiCredentialsLock.cs:53-56`). A Linux PATH is a spelling — two bind mounts or two
  symlinked spellings give two keys, two refreshers, a signed-out person; the kernel path from `/proc/self/fd`
  (`HandleRead.cs:244-265`) is the same spelling problem. Device and inode name the folder itself. So `KeyFor` asks a per-platform
  IDENTITY: Windows keeps the upper-cased final path; Linux hashes `dev_major:dev_minor:ino` from `statx(AT_FDCWD, path, 0,
  STATX_INO)` — `statx`'s struct is the same on every Linux architecture — through a `LibraryImport("libc")`, the precedent
  `src_mcp/service_defaults/AppendOnlyFile.cs:374-383`. A failed `statx` is an empty key: still `Unusable`, never the spelling.
  `HandleRead.FinalDirectory` stays as it is for the key's Windows arm; `Junctions`' locality check gets the Linux real path
  (`WorktreePaths.Real`, which `CanonicalDirectory` already falls back to, `HandleRead.cs:211-212`). Needs no Kimi cell.

- **DL8 — The tree hold on Linux is the same inheritable file, IF .NET takes it exclusively and lets it be inherited.**
  *Security / authentication.* `HeldFile.Take(path, inheritable: true)` opens with `FileShare.None | FileShare.Inheritable`
  (`HeldFile.cs:47-51`); on Unix .NET turns a share mode into an advisory `flock`. Two facts are unproven on Linux, because every
  test that would take it skips there (`src_mcp/tests/KimiCredentialsLockTests.cs:247`, `:296`, `:314`): that the combined share
  mode still asks for an EXCLUSIVE lock (a shared one would let two refreshers in), and that the descriptor reaches the child (no
  close-on-exec). That is cell **L-N1**, a test, not a Kimi run. If either fails, the Linux take is a native one: `open` without
  `O_CLOEXEC` and `flock(LOCK_EX | LOCK_NB)` through `libc` (the `AppendOnlyFile` precedent), behind the same `Hold` union. What the
  hold covers on Linux (L-K5): the shim and `kimi`, the direct child — not a grandchild that `kimi` starts the ordinary way. That is
  enough for its purpose, one refresher at a time, IF the refresher is `kimi` itself: **L-K4** checks that no descendant of `kimi acp`
  is another `kimi`. If one is, `ForeignKimiWatch`'s orphan rule (a `kimi` whose parent ended counts as the person's,
  `src_mcp/runners/Kimi/ForeignKimi.cs:32-36`) is the only net, and the plan goes back to the owner before E5 ships.

- **DL9 — The teardown on Linux: the shim is a child SUBREAPER and ends its own descendants by identity.** The parent plan's S4
  condition (`PLAN_vibe_and_kimi_runtimes.md` §4, "Before the shim launches Kimi on any host but Windows") asks the transport to own
  the tree independently of the agent, "a Unix process group (or session) killed as a group after the leader is gone", with a test
  that the grandchild of an agent that crashed first is gone. The test stays; the mechanism changes, for two measured reasons: a group
  kill misses a child that opened its own session (L-K4 harness, last row: survives), and .NET's `Process.Start` has no pre-exec hook
  to put the agent in a new group. So: at the start of `AskAcpMode.RunAsync` (`src_mcp/src/Acp/AskAcpMode.cs:73`), on Linux, the shim
  calls `prctl(PR_SET_CHILD_SUBREAPER, 1)` — every orphan of its tree, `setsid` or not, is reparented to the SHIM instead of init.
  After `ProcessLauncher.EndTreeFirstAsync` (`src_mcp/runners/Processes/ProcessLauncher.cs:645-650`, called from
  `AcpTransport.EndAsync`, `src_mcp/runners/Acp/AcpTransport.cs:216-228`) the transport lists the shim's OWN descendants
  (`ProcessTree.Descendants`, `src_mcp/runners/Processes/ProcessTree.cs:59`, the `/proc` walk at `:169-196`) — the shim starts nothing
  but the agent — ends each by pid and start time (`ProcessTree.Terminate`, `:124`) and confirms (`ConfirmGoneAsync`, `:151`; a
  zombie counts as exited, `:198-211`). A survivor is reported in the shim's structured report. Recorded as a deviation from S4's
  letter; its test is S4's own.

- **DL10 — The foreign-Kimi wait reads `/proc` on Linux.** `HostProcessList.Current` returns `[]` off Windows
  (`src_mcp/runners/Kimi/HostProcessList.cs:29`, `:53`), so on Linux the wait for the person's own `kimi` never waits — silently. The
  Linux listing reads `/proc/<pid>/stat` (pid, parent, the bracketed `comm`); its reader is EXTRACTED from `ProcessTree.LinuxParents`
  (`ProcessTree.cs:169-196`), which already parses that line for the parent — one reader, not two. `KimiNames`/`CoaiNames`
  (`ForeignKimi.cs:46-48`) keep `kimi` and `coai-mcp` (both under `comm`'s 15 characters). **Waits for L-K6** (the `comm` of a
  running `kimi acp`, and whether the parent chain reaches `coai-mcp`).

- **DL11 — Kimi's OS-home redirect on Linux is `HOME` alone.** D19 already says so (parent plan, "On Unix the redirect is `HOME`
  alone, when D18 lifts"). `KimiEnvironment.Built` sets `HOME` → `os/` on Unix and not the three Windows names; its refusal checks
  `HOME` only there (`src_mcp/runners/Kimi/KimiEnvironment.cs:138`). The Unix minimal set has no `XDG_*`
  (`src_mcp/runners/Processes/ProcessEnvironment.cs:90-91`), so Kimi derives its XDG folders from the redirected `HOME` — into `os/`,
  which `KimiHome.OsProblem` refuses the moment it holds anything (`src_mcp/runners/Kimi/KimiHome.cs:185-195`). **Waits for L-K3**: if
  Kimi writes under `os/` on Linux, the `os/` rule is re-decided from that inventory with the owner — never silently widened.

- **DL12 — `rg` and `fd` on Linux are provisioned by the install button.** The owner rules (set it up once; every action is a
  button) exclude "ask the owner to install them by hand". The line, one press, each step guarded and idempotent:
  - `rg`: skipped when `rg` resolves or `~/.kimi-code/bin/rg` exists; else `sudo apt-get install -y ripgrep` where `apt-get` exists,
    `sudo dnf install -y ripgrep` where `dnf` does — the projects' own README instructions, chosen by `command -v` inside the line;
  - `fd`: skipped when `fd` resolves or `~/.kimi-code/bin/fd` exists (the owner's case: it does); else the `fd-find` package and,
    because Debian names the binary `fdfind` and Kimi looks for `fd`, `ln -s "$(command -v fdfind)" ~/.local/bin/fd` (fd's README);
  - any other distribution: the line stops with a sentence naming both projects' READMEs;
  - then Moonshot's script, as today (`vendorTerminal.ts:528-532`).

  `OFFICIAL_SOURCES` (`src_vs_code/src/officialSources.ts:17-37`) gains `distroPackages: ['ripgrep', 'fd-find']` and the `VERBS`
  table (`:52-75`) gains `apt-get install` and `dnf install`, `-y` only, packages from that list only, `sudo` admitted as the first
  word of THOSE steps only. *Security (supply chain): reviewed as such.* `KimiTools.Refusal` (`KimiTools.cs:51-61`) names the cure
  for the HOST — today it quotes `winget` everywhere (`RipgrepInstall`/`FdInstall`, `:24-27`). Rejected: a release binary dropped
  into `~/.kimi-code/bin` — Kimi's own folder, where no package manager would update it. The `sudo` password prompt in the install
  terminal is owner question Q4.

- **DL13 — Not adopted: trimming `/mnt/<letter>/` entries from the built `PATH` on WSL, and a `HOME` redirect for Vibe.** Both were
  weighed. The first would close WSL interop (a Linux child starting a Windows `.exe`) — but every tool that could start a process is
  denied (Vibe: `read_file` only, L-V3; Kimi: the shim refuses every permission request, D15), and no cell shows a read it closes.
  The second was measured harmless (L-V-home), but Vibe's configuration lives under `VIBE_HOME`, which D17 already owns; Windows has
  no such redirect and the two sides stay one shape.

- **DL14 — Release order and versions.** Vibe ships first: E1–E3 in the next `src_mcp` minor, **0.58.0**, with an extension minor.
  Kimi follows in the first minor after E4–E6 (0.59.0 at the earliest). The per-runtime gate `RUNTIME_SINCE`
  (`src_vs_code/src/runtimeSince.ts:46-50`) answers "does this server know the runtime", not "does it run it on THIS host", so a
  second table joins it: `VIBE_ON_LINUX_SINCE = '0.58.0'`, `KIMI_ON_LINUX_SINCE` set by E6 — a Linux/WSL side whose server is older
  shows "update coai-mcp to run Vibe here" instead of a row that is refused at launch. **The unreleased Kimi-consultant branch
  (`feat/kimi-consultant`) sets `KIMI_CONSULTANT_SINCE` to 0.58.0. Whichever of the two merges SECOND bumps its constant to the next
  minor** (`runtimeSinceIsAhead.test.ts` guards the table, not unreleased commits — the reason `KIMI_RUNTIME_SINCE` once said 0.56.0,
  `runtimeSince.ts:30-42`).

## 5. The measurement cells, and who waits for them

**Port the probes first** (E1): today they are Windows-bound — `scripts/lib/kimi.mjs:20` (`kimi.exe`), `:99` (`rg.exe`, `fd.exe`),
`:204` (`mklink /J` through `cmd.exe`), `:406` (`taskkill`); `scripts/probe-kimi-tree-hold.mjs:61` (exits off `win32`), `:86`
(PowerShell for the process list); `scripts/lib/kimi-home.mjs:98`; `scripts/lib/vibe.mjs:18` (`MINIMAL_WINDOWS`), `:57-58`
(`taskkill`); `scripts/probe-vibe-confinement.mjs:86` (`python.exe` under `APPDATA`), `:158` (`mklink /J`). The Linux arms use
`ln -s`, `MinimalUnix`'s names, `/proc` and `kill -- -<pgid>`; every launch from a script FILE in an owned `testsToDelete-` folder,
a built environment, nothing written into `~/.vibe` or `~/.kimi-code` by the probe (checked by a listing before and after).

| Cell | Question | Cost | Gates |
|---|---|---|---|
| L-V6 | Which variable NAMES the owner's `~/.vibe/.env` holds (names only, values never read out). | free | E2 (DL2's admitted set) |
| L-V7 | `scripts/vibe-home-manifest.mjs --write` in WSL: the `linux` entry; a second fresh home agrees on every hash. | 2 tiny turns | E2 (DL3) |
| L-V8 | Does Vibe load a `.env` from its WORKING folder? (A bogus `MISTRAL_API_KEY` in the cwd, no home `.env`: still L-V1's "Missing" → it does not.) Code reading of Vibe's package first. | free | E2 (snapshot exclusion) |
| L-V9 | L-V1, L-V2/3, L-V4 re-run through the ported probe, 3 of 3. | ~10 turns | E3 (release) |
| L-N1 | .NET on Linux: `HeldFile.Take(…, inheritable: true)` excludes a second take (inheritable or not), and a child started after it holds the lock after the parent's SIGKILL. A test in the Linux CI job, in a child process (no test holds an inheritable handle in the host). | free | E5 (DL8) |
| L-N2 | A subreaper shim and a FAKE agent that starts a same-group child and a `setsid` child, then exits first: after the teardown both are gone. A test in the Linux CI job. | free | E5 (DL9) |
| L-K1 | Live: `kimi --version` and `kimi acp --help` in the built environment; `Grep`/`Glob` with `rg` on the built `PATH` → no `bin/` in the owned home (M-K12 (c) on Linux); without `rg` → where the download lands. | free + 1 turn | E6 (DL5, DL12) |
| L-K2 | The sign-in through a SYMLINK into the `0700` folder: signed in; a refresh lands in the person's folder; Kimi neither replaces the link nor `chmod`s the target; the person's own `kimi` works after. | 3 turns | E5 (DL6) |
| L-K3 | `HOME` → `os/` alone: `~/.agents/skills` and `AGENTS.md` closed (the canary only with the owner's yes, as on Windows); `os/` stays empty; what lands in the owned home. | 3 turns | E6 (DL11) |
| L-K4 | `kimi acp`'s tree during a `Grep` turn: children, their group and session, which holds the lock fd (`/proc/<pid>/fd`), whether any descendant is another `kimi`; the kill arms (parent, agent pid, group). The scout's 4-turn script is the start. | 4 turns | E5 (DL8, DL9) |
| L-K6 | `/proc/<pid>/stat` `comm` of `kimi acp` under the shim and of the person's TUI; the parent chain to `coai-mcp`. | free (inside L-K4) | E5 (DL10) |
| L-K7 | The write arms and `session/load` through the shim on Linux. | 3 turns | E6 |
| M-K14 | The refresh race — unchanged: later, it gates only lifting the serialisation (D19). | — | none here |

**Every Kimi cell runs only with the owner's yes AND no other `kimi` process on that side** (`ps -eo comm=`): the refresh token
rotates (D19); a second refresher can sign the owner out. The scout's prepared run is the template.

## 6. Build order

1. **E1** — port the probes; write the Vibe RESULTS section from the scout's draft; run L-V6, L-V7, L-V8; commit the `linux` manifest
   entry the script wrote. Nothing in `src_mcp` changes except the embedded JSON.
2. **E2** — Vibe on a Linux host (DL1 for Vibe, DL2, DL3, DL5's Vibe half, the diagnosis text). Red-first tests flip the D18 pins.
3. **E3** — the extension's Vibe texts, `VIBE_ON_LINUX_SINCE`, the limitation rows, L-V9, the live WSL checks through the released
   binary. **Release 0.58.0.**
4. **E4** — the Kimi WSL measurements (the cells above), the owner present.
5. **E5** — the Kimi platform mechanisms (DL6–DL10). L-N1/L-N2 and DL7 can start right after E1; DL6, DL8's last clause, DL9's
   mechanism choice and DL10 wait for E4.
6. **E6** — Kimi on a Linux host: DL1 for Kimi, DL5's Kimi half, DL11, DL12, the extension, `KIMI_ON_LINUX_SINCE`, the live checks.
   **Release the next minor.**

Each epic is one branch, one PR, the coai gate once per epic.

## 7. Scenarios

Each red is the assertion message the test fails with before the fix.

**Vibe (E2)**

- S-V1 *Given* a WSL host, a Vibe row and `~/.vibe/.env` naming only `MISTRAL_API_KEY`, *when* a reviewer launches, *then* it starts, and
  its home holds `.env` → that file. Red: `"a Vibe launch on a wsl host should start, but was refused: Vibe is refused on a wsl host…"`.
- S-V2 *Given* the same home after the turn, *when* the next launch validates it, *then* the `.env` link is admitted.
  Red: `"the .env link to the person's Vibe sign-in should be admitted, but the home was refused: … .env, which is a link…"`.
- S-V3 *Given* a home whose `.env` links ANYWHERE else (another file, a relative target, a chain), *then* refused with the path.
  Red: `"a .env link to <path> should refuse the launch, but it was admitted"`.
- S-V4 *Given* a Windows host and a home holding a `.env` link, *then* refused as today. Red: `"a link in a Windows Vibe home should still refuse"`.
- S-V5 *Given* `~/.vibe/.env` naming `VIBE_MCP_SERVERS` as well, *then* refused, the sentence lists the names and holds NO value.
  Red: `"a .env naming VIBE_MCP_SERVERS should refuse"` and `"the refusal must not contain the value 'sk-test-…'"`.
- S-V6 *Given* `~/.vibe/.env` is group-writable, or is itself a link, *then* refused with the cure.
- S-V7 *Given* no `~/.vibe/.env`, *when* Vibe exits 1 with L-V1's wording, *then* the row says *not signed in*, cure `vibe --setup` on
  this side. Red: `"the Linux cure should name ~/.vibe/.env, but said: …keyring…"`.
- S-V8 *Given* a Linux host and Vibe 2.26.1 with the `linux` entry's bytes, *then* no warning; with one plugin byte changed, refused with
  the path. Red: `"a Linux 2.26.1 home with its own bytes was refused against the win32 hashes"`.
- S-V9 *Given* a version with only a `win32` entry, on Linux, *then* it runs with the shared warning naming `win32`.
- S-V10 *Given* a home with a `.env` link to a `0600` file, *when* each deleter runs, *then* the link is gone and the file's bytes, mode
  and mtime are unchanged. Red: `"the deleter changed the person's ~/.vibe/.env (mode 600 → …)"`.
- S-V11 *Given* `vibe` only in `~/.local/bin` and a `PATH` without it, *then* `DefaultExecutable` is that path. Red: `"expected ~/.local/bin/vibe, got vibe"`.
- S-V12 *Given* a Windows host whose row names `wsl.exe`, *then* still refused with the D18 sentence (DL1).

**Kimi (E5, E6)**

- S-K1 *Given* a Linux host and a person's `~/.kimi-code/credentials`, *when* the home is made, *then* `credentials` is a symlink whose
  read-back names that folder. Red: `"a directory junction is made on Windows only…"` must no longer be the answer on linux.
- S-K2 *Given* a target under `/mnt/c/` on WSL, *then* refused, nothing made. Red: `"a sign-in folder on the Windows drives should be refused on wsl"`.
- S-K3 *Given* two spellings of one folder (a symlinked parent), *then* `KeyFor` returns ONE key; a deleted folder → empty key.
  Red: `"two spellings of one sign-in folder gave two lock keys"`.
- S-K4 *Given* the tree hold taken inheritable by a child process, *when* a second take is tried from another process, *then* `Busy`
  (L-N1). Red: `"a second take of an inheritable hold succeeded — the lock is shared, not exclusive"`.
- S-K5 *Given* the shim's parent killed with SIGKILL while the agent lives, *then* the hold stays held until the agent ends.
- S-K6 *Given* a fake agent that starts a same-group child and a `setsid` child and then exits FIRST, *when* the transport ends, *then*
  both children are gone (S4's test). Red: `"the setsid grandchild of an agent that crashed first is still alive (pid …)"`.
- S-K7 *Given* a fake `/proc` with a `kimi` whose parent chain reaches no `coai-mcp`, *then* the wait waits and then answers busy with
  the person's sentence. Red: `"the Linux listing found no process — the foreign-Kimi wait never waits"`.
- S-K8 *Given* a Linux host, *then* the built environment sets `HOME` → `os/` and no `USERPROFILE`/`HOMEDRIVE`/`HOMEPATH`, and the
  refusal checks `HOME` alone.
- S-K9 *Given* `rg` absent everywhere on Linux, *then* the refusal names the apt/dnf lines and NOT `winget`. Red: `"the Linux refusal quotes winget"`.
- S-K10 *Given* the Linux install line, *then* `fromOfficialSources` admits it; *given* `sudo apt-get install -y curl`, or
  `sudo rm -rf`, or `apt-get install ./x.deb`, *then* refused.

## 8. Test plan

- **Flip the pins, red first.** The tests that hold the refusals today are rewritten for the new rule in the same commit as the
  change, and each is observed red against the old code: `src_mcp/tests/VibeLaunchControlTests.cs:257-268` (Linux and WSL refused →
  admitted; a `wsl.exe` row on Windows still refused), `KimiLaunchChecksTests.cs:184-195`, `KimiLaunchControlTests.cs:81-84`,
  `JunctionsTests.cs:275-290` (S1.4 becomes: Linux and WSL make a symlink, macOS and Other still refused),
  `HostProcessListTests.cs:75-81` (the empty listing becomes a `/proc` listing), `KimiAcpProfileTests.cs:47-56` (Linux finds
  `~/.kimi-code/bin/kimi`; macOS still guesses nothing); the extension's hint and help pins; the `TheOwnersKimi` skips are re-read
  and each kept or lifted with its reason.
- **Linux CI runs the real paths.** S-K3, S-K4/5 (L-N1) and S-K6 (L-N2) run WITHOUT the `WindowsStandIn` stand-in on the ubuntu job;
  the stand-in stays for the Windows-only decisions. The skips at `KimiCredentialsLockTests.cs:247`, `:296`, `:314` are lifted on
  Linux.
- **Deleters around a file link** (S-V10): `GitScratch.DeleteEvenIfReadOnly`, `VibeHomes.SweepOneShot`, the D20 sweep, the test sweeps.
- **Extension:** `officialSources` verbs (S-K10); the Linux install line passes `bash -n` and runs against stand-ins for `apt-get`,
  `dnf`, `sudo` (the `kimiWindowsInstallRuns.test.ts` method); the per-host hints and the five help languages (`helpCoverage.test.ts`);
  `VIBE_ON_LINUX_SINCE` / `KIMI_ON_LINUX_SINCE` against an older server.
- **Probe scripts:** their own `node --test` files gain Linux cases (`scripts/lib/vibe.test.mjs`, `kimi.test.mjs`, `kimi-home.test.mjs`).
- **Live, after each release:** in WSL through the released binary — a plan round, a code round, a two-turn consult and a question
  row with a Vibe row (E3), the same with a Kimi row (E6); the real `~/.vibe` and `~/.kimi-code` compared before and after.
- Run as the repository runs tests: the MTP executables and `node --test`, never `dotnet test`.

## 9. Growth

| Surface | Size | Who retires it |
|---|---|---|
| `shared/vibe-home-manifest.json` entries | one entry per Vibe version per platform, ~12 hashes, ~2 KB | git; replaced when a version is re-qualified |
| The `.env` link | one per Vibe home | gone with its home (round disposal, one-shot sweep, D20) |
| Kimi lock files on Linux (`~/.local/share/coai-mcp/kimi-locks/`) | two empty files per sign-in folder identity — one or two on a machine | kept, reused (as on Windows, `KimiCredentialsLock.cs:57-59`) |
| Zombies of reaped orphans under the shim | at most the agent's tree, for one turn | reaped by init when the shim exits |
| Probe scratch (`/tmp/testsToDelete-*`) | a few MB per run | the test-leftovers rule; the scout's four folders are the owner's to remove |

Nothing here has an in-flight state of its own; the homes keep D20's sweep.

## 10. Boundaries with other plans

| Item | Built by | The other plan's part |
|---|---|---|
| D18 lifted for a Linux HOST | this plan | [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) D18 states the refusal; this plan amends it (a note there points here) |
| Kimi's Linux install line | this plan (DL12) | [PLAN_vibe_and_kimi_runtimes_E4.md](PLAN_vibe_and_kimi_runtimes_E4.md) E4.5 wrote it down "for the day D18 lifts" |
| Kimi as a consultant / question row on Linux | `feat/kimi-consultant` (its own limitation rows) | this plan's E5/E6 give it the Linux launch; whichever merges second bumps its `*_SINCE` (DL14) |
| A Windows `coai-mcp` running the WSL side | [PLAN_a_wsl_consultant_is_checked_from_windows.md](PLAN_a_wsl_consultant_is_checked_from_windows.md) | this plan keeps it refused (DL1) |
| M-K14, the refresh race | the parent plan (later) | unchanged here |
| macOS | nobody yet | stays refused |

Disjoint otherwise: chat, the Team server and the bench are out, as in the parent plan.

## 11. Owner prerequisites and open questions

Prerequisites: Vibe paid turns for E1 and L-V9 (~12); Kimi paid turns for E4 (~16) with his own `kimi` closed while they run;
`sudo` once, by the install button (E6), if he wants `rg` from `apt-get`.

- **Q1 — Native Linux too, or WSL only?** Default: both (same kernel; the `linux` rows say "measured on WSL2"; a desktop keyring
  that holds the Vibe key without a `.env` makes coai's launch — no D-Bus in the built environment — signed out, and the diagnosis
  says so).
- **Q2 — A `~/.vibe/.env` with more than `MISTRAL_API_KEY`.** Default: refuse, list the names, cure "keep only the key there". The
  alternative — coai reads the key and passes it in the environment — is DL2's rejected option.
- **Q3 — His own `kimi` TUI open in WSL makes every coai Kimi turn wait, then answer busy** (decision 74 of the E4 plan, the same on
  Windows) until M-K14 is measured. Keep it, or run M-K14 first?
- **Q4 — `sudo apt-get` in the install terminal** (he types his password there). Default: yes.
- **Q5 — Release order** if `feat/kimi-consultant` is ready first: it takes 0.58.0 and Vibe-on-Linux 0.59.0 (DL14).

## Definition of Done

- [ ] E1–E6 done-when met; every cell of §5 that gates a shipped epic recorded in `research/RESULTS_vibe_confinement.md` /
      `RESULTS_kimi_confinement.md` (Linux sections), with predictions written before each run.
- [ ] A Vibe row reviews, consults and answers a question row in WSL through the released 0.58.0; the real `~/.vibe` unchanged.
- [ ] A Kimi row the same through its release; the real `~/.kimi-code` unchanged but for Kimi's own token refresh.
- [ ] Every D18 pin flipped red-first; a `wsl.exe` row on Windows and a macOS host still refused.
- [ ] The `.env` link: exactly one admitted link, names-only check, no value ever in a sentence or log, deleters proven around it.
- [ ] The lock key is identity-based on Linux, the tree hold proven exclusive and inherited (L-N1), the subreaper teardown proven (L-N2).
- [ ] The install button provisions `rg`/`fd` on Debian/Ubuntu and Fedora; `OFFICIAL_SOURCES` admits exactly those verbs and packages.
- [ ] Hints, notes and help in five languages say what each side's server does; `*_ON_LINUX_SINCE` level with the tags.
- [ ] `research/module_runners.md` and `research/module_extension.md` updated; the parent plan's D18 amended; this plan promoted.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## Epics

Six epics, Vibe first; quota ⌈2·6/5⌉ = 3. Stories go to `todo/PLAN_kimi_and_vibe_on_linux_E<n>.md`, one per epic, written when the
epic starts and linked under it in the same commit. The cornerstone AREAS below are proposed by the judge for Fable to confirm
([subagent-models.md](../.agents/conventions/common/subagent-models.md)); no story is marked yet.

Cornerstones: 0 of 3 — none marked yet

### E1 — Linux probes, and Vibe's Linux cells recorded · M · ordinary (measurement) · depends on nothing

- **Goal.** The probe scripts run on Linux; the scout's Vibe findings become RESULTS with predictions; L-V6, L-V7, L-V8 recorded;
  the `linux` manifest entry written by the script.
- **Scope.** `scripts/lib/{vibe,kimi,kimi-home}.mjs`, `probe-vibe-confinement.mjs`, `probe-kimi-confinement.mjs`,
  `probe-kimi-tree-hold.mjs`, `vibe-home-manifest.mjs` (§5's lines); their tests; `shared/vibe-home-manifest.json`;
  `research/RESULTS_vibe_confinement.md` (a Linux section). No product C#.
- **Done when.** The ported scripts pass their tests on both OSes; the three cells recorded; the manifest's `linux` entry committed
  (or, if L-V7 disagrees, its absence recorded with both hash sets).

### E2 — Vibe on a Linux host · M · **authentication / security** (the key-file link) · depends on E1

- **Goal.** §2 item 1 in the server.
- **Scope.** DL1 for Vibe (`VibeLaunch.cs:77-82`; `VibeRuntime.cs:80-85`); DL2 (the link maker, the names check, `VibeHome.Judge`, the
  deleter tests, `VendorDiagnosis.cs:109-112`'s per-host cure); DL3 (`VibeHomeManifest`, `VibeLaunch.Verdict`/`VersionWarning`, the
  `.session-index.json` name); DL5's Vibe half; L-V8's outcome (a `.env` snapshot exclusion if needed); `research/module_runners.md`.
- **Done when.** S-V1–S-V12 green as MTP executables on Windows and Linux CI; every D18 pin for Vibe flipped red-first.
- **Cornerstone AREA (proposed).** The one admitted link: DL2's admission rule in `VibeHome` plus the names check — the only place an
  owned home may point at a secret, and a wrong rule there has no visible symptom.

### E3 — Vibe on Linux in the extension, and the 0.58.0 release · S · ordinary · depends on E2

- **Goal.** The panel says what the WSL side does, and the release ships.
- **Scope.** `vendors.ts:294`; `vendorTerminal.ts:478`, `:488-493`; the five help files; `runtimeSince.ts` (`VIBE_ON_LINUX_SINCE`);
  `shared/consultant-limitations.json:252-263` (the Vibe `linux` and `wsl` rows: measured on WSL2, the cells named);
  `runtime-capabilities.json` if its Vibe rows name a host; L-V9; `research/module_extension.md`.
- **Done when.** Extension tests green; L-V9 3 of 3; the live WSL checks of §8 answered through the released binary.

### E4 — Kimi measured on WSL · M · ordinary (measurement), **runs on the owner's live, rotating sign-in** · depends on E1

- **Goal.** L-K1, L-K2, L-K3, L-K4, L-K6, L-K7 recorded, each 3 of 3 where it is a confinement claim.
- **Scope.** The ported Kimi probes; `research/RESULTS_kimi_confinement.md` (a Linux section). No product code.
- **Done when.** Every cell recorded with its prediction; DL6, DL8, DL9, DL10, DL11 confirmed or re-decided in this plan (a
  re-decision goes to the owner before E5).

### E5 — Kimi's Linux mechanisms · L · **authentication / security** (the sign-in link and the refresher lock) · depends on E1 (L-N1, L-N2, DL7) and E4 (the rest)

- **Goal.** Every Windows-only mechanism has its Linux counterpart, proven.
- **Scope.** DL6 (`Junctions`' Unix shell and locality rule); DL7 (`KimiCredentialsLock.KeyFor`'s identity, the `statx` import);
  DL8 (L-N1, and the native take if needed); DL9 (`AskAcpMode`'s subreaper, `AcpTransport.EndAsync`'s descendant end); DL10 (the
  extracted `/proc` reader, `HostProcessList` on Linux).
- **Done when.** S-K1–S-K7 green on the Linux CI job without the stand-in; the `KimiCredentialsLockTests` skips lifted on Linux.
- **Cornerstone AREAS (proposed).** (1) The refresher lock on Linux — DL7's identity key and DL8's exclusive inheritable hold as one
  unit: a lock that is shared or keyed by spelling lets two refreshers in and signs the person out, with no symptom until it does.
  (2) DL9's teardown — the subreaper and the descendant end, S4's own condition.

### E6 — Kimi on a Linux host, and its release · M · ordinary; the `OFFICIAL_SOURCES` verbs reviewed as security (supply chain) · depends on E5

- **Goal.** §2 item 2 end to end, and item 3 for Kimi.
- **Scope.** DL1 for Kimi (`KimiLaunch.cs:28-33`); DL5's Kimi half (`KimiAcpProfile.cs:135-140`, `KimiPlaces.cs:74`, `KimiTools`);
  DL11 (`KimiEnvironment`); DL12 (`vendorTerminal.ts:528-532`, `:568-572`, `officialSources.ts`, `KimiTools.cs:24-27`, `:51-61`);
  `vendors.ts:310`, the help, `KIMI_ON_LINUX_SINCE`; `research/module_runners.md`, `module_extension.md`.
- **Done when.** S-K8–S-K10 green; the install button run in WSL on a machine (or a `testsToDelete-` prefix) without `rg`; the live
  Kimi checks of §8 through the release; this plan promoted.
