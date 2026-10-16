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

1. On a WSL host (`HostKind.Wsl`, `src_mcp/runners/Platform/HostKind.cs:17-24`) a Vibe row reviews plans and
   code, answers `consult` and a question-consultant row, with the same confinement as on Windows: read only the snapshot, no
   write, no shell, no MCP server, a coai-owned validated home, a built environment — **on the person's own sign-in, never copied.**
   A native Linux host (`HostKind.Linux`) stays refused, with a sentence saying it is measured on WSL2 only, until one native-Linux
   run of L-V9 is recorded (DL1; the plan gate, F10).
2. The same for a Kimi row, after its WSL cells are recorded: the owned `KIMI_CODE_HOME`, the sign-in by link, the refresher lock,
   the tree hold, the teardown that leaves no grandchild, the wait for the person's own `kimi`.
3. Every action is a panel button: on Debian/Ubuntu (`apt-get`) and Fedora (`dnf`) the install button provisions what the launch
   needs (Kimi's `rg`/`fd`); on any other distribution it prints one generic instruction — *install ripgrep and fd with your
   distribution's package manager, or place them on PATH or in `~/.local/bin`* — and the card, the hint and the help say what the
   side's `coai-mcp` actually does.
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
| L-K1 live, L-K2, L-K3, L-K4, L-K6, L-K7 (Kimi 2.2.0) | Signed in 8 of 8 through the symlink; one refresh landed in the person's folder (tmp + rename, mode kept, link kept); no `bin/` in the owned home; `os/` gains Kimi's native cache (→ DL11); `rg` in its own session survives a pid or group kill briefly (→ DL9); `comm` of `kimi acp` is `MainThread` (→ DL10); 3 of 3 write arms refused; `session/load` works, the model in `configOptions` (already what the shipped parser reads). | **measured 2026-10-16**, 8 paid turns — RESULTS_kimi_confinement §29 |

The owner's OAuth slot on that machine is `oauth/kimi-code-env-<hex>`, not the default — the D19 rule (the slot READ from the person's
`config.toml`) already covers it. The scout found the owner's TUI by `comm` = `kimi` (`ps -eo comm=`).

## 4. Decisions

Numbered DL1–DL14 so they never collide with the parent plan's D-numbers. Every reference was opened on `03ec1c1a`.

- **DL1 — Scope: a Linux HOST, not a Linux CHILD of Windows.** Admitted: a `coai-mcp` whose own host is `HostKind.Linux` or
  `HostKind.Wsl` (`HostKind.cs:30-31`, `:50-56`). Still refused: the second arm of both host refusals, a Windows host starting
  `wsl.exe` or a `\\wsl$` path (`HostKinds.RunsInWsl`, `HostKind.cs:39-47`) — a Windows process cannot take a Linux lock (`HeldFile`
  states that no lock crosses the 9P boundary, `src_mcp/runners/Files/HeldFile.cs:17-22`), cannot list or end a Linux process tree,
  and the Windows→WSL bridge is another plan's. macOS stays refused (nothing measured there). **Native Linux is refused for now —
  decided by the plan gate (F10), which closes Q1.** The code is the same, and WSL2 is a real Linux kernel, but every cell is WSL2's,
  and a desktop Linux differs where it matters (a keyring Vibe may use instead of `.env`, other filesystems, other distributions'
  packaging). So 0.58.0 admits `HostKind.Wsl` ONLY; `HostKind.Linux` keeps a refusal whose sentence is new: *"Vibe runs on WSL2 only
  for now — it is measured on WSL2 and not yet on a native Linux host (DL1 of todo/PLAN_kimi_and_vibe_on_linux.md)"*. It lifts when
  at least one native-Linux run of L-V9 is recorded (§5, L-V10). The `linux` limitation rows say exactly that; the `wsl` rows name the
  WSL2 cells. The host refusal becomes per runtime and per stage: Vibe admits WSL from E2, Kimi from E6 (WSL only, by the same rule).
  **WSL1 is not WSL2** (cadence consult a3933362, verified against the code): `VendorDiagnosis.NamesWsl`
  (`src_mcp/runners/Reviewers/VendorDiagnosis.cs:256-258`) answers yes for ANY banner holding `microsoft` or `WSL`, so a WSL1
  kernel (`4.4.0-…-Microsoft`, a syscall translation layer with no real `/proc` process tree or `flock` semantics of its own) reads
  as `HostKind.Wsl` today. "Measured on WSL2" must mean WSL2: the runtime admission asks a second, narrower predicate —
  `NamesWsl2` (the banner holds `WSL2`, or `microsoft-standard`, case-insensitive) — and a WSL1 kernel is refused with the
  native-Linux sentence's WSL1 variant (*"…measured on WSL2, and this is WSL1"*). `NamesWsl` itself is NOT narrowed: its other
  callers (the networking cure) are right for both generations.

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
  - **the target**: judged through ONE open descriptor, never through the path (consultation c6e42fd3, verified against the
    precedent): `open(O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC)` — a link answers `ELOOP` and is refused (coai never chains
    links, the `SnapshotLinks.IsLink` rule; a FIFO or a device cannot block the open) — then `statx(fd, "", AT_EMPTY_PATH)` for
    type, owner and mode on THAT descriptor: a regular file, owned by the user coai runs as (`geteuid`), not group- or
    other-writable (whoever can write it configures Vibe), and the bytes read from the same descriptor, at most 64 KiB, one byte more
    refusing. **Ported, not rewritten**: `RegularFiles.ReadOwned` and its Linux leg (`ReadLinuxOwned` → `ReadOpened` → `Judged` →
    `Capped`, wsl_care's `src_daemon/src/WslCare.Core/Files/RegularFiles.cs:68`, `:132-175`, `:253`; `OwnershipProblem` at
    `:121`) — the same owner's MIT code, measured there, `LibraryImport` and so AOT-safe like `AppendOnlyFile.cs:374-383`; it comes
    into `src_mcp/runners/Files/` with a header naming its origin, with one `libc` spelling (coai's). `BeneathFiles.cs` (the
    folder-chain opens) is NOT ported: the chain above `~/.vibe/.env` is the person's own home, which coai admits for Kimi's sign-in
    too (`Junctions.Resolved`, `Junctions.cs:179`); only the final component is coai's to judge. **The residual, stated where the code
    reads it**: coai's check and Vibe's read are two opens; the mode rule leaves only the person's own account (or root) able to
    swap the file between them, and coai cannot close that window and does not claim to;
  - **its variable NAMES only, through a small grammar that matches python-dotenv's NAME part** (the plan gate, F8): Vibe reads the
    file with python-dotenv, so a line python-dotenv binds to a variable must never be refused as unreadable. As python-dotenv's
    parser (`dotenv/parser.py`) reads a binding: leading whitespace skipped; an optional `export` followed by whitespace; the NAME —
    `'…'` (single-quoted, no `'` inside) or unquoted, a run of characters other than `=`, `#` and whitespace; optional whitespace;
    `=`. Everything after that `=` — quotes, escapes, inline `#` comments, the value itself — is the VALUE, and coai never parses,
    inspects, stores, compares, logs or returns it. So `KEY=v`, `KEY = "v"`, `export KEY='v'`, `  KEY=v  # note` and `'KEY'=v` all
    name `KEY`. Empty lines and `#` comment lines are admitted. The names must be exactly `{MISTRAL_API_KEY}`. A `VIBE_*` name in
    that file would reach Vibe as configuration — the channel D17 closes for the environment (`VibeLaunch.cs:90-93`; RESULTS §22 A).
    **The grammar is checked against python-dotenv, not against this paragraph:** E1 runs python-dotenv — the version Vibe 2.26.1
    bundles in its own tool environment — over a fixed set of lines (each form above, the `export` and whitespace variants, a bare
    word, a line starting with `=`, a quoted value that continues on the next line) and writes the names it binds as test vectors
    (`shared/dotenv-name-vectors.json`, the shared-vectors pattern of `shared/capability-matrix-vectors.json`); the C# grammar's test
    reads them. Three refusals, never one:
    - a line that does not bind a name — a bare word with no `=` (python-dotenv keeps it with NO value and sets nothing from it, and
      it is how a key pasted on its own line looks), a line starting with `=`, a line python-dotenv itself cannot parse — refuses
      with a GENERIC sentence that quotes nothing from the file but the line number;
    - a bound name other than the admitted one refuses by its LINE NUMBER only, never by the name (cadence consult a3933362,
      verified with python-dotenv 1.2.2 from Vibe 2.26.1's own environment): coai reads physical lines with no quote state, so a
      line it reads as `NAME=…` may be the CONTINUATION of a quoted value — `MISTRAL_API_KEY="dummy` + `LEAK_CANARY=ending"` binds
      ONE name, `MISTRAL_API_KEY`, and echoing `LEAK_CANARY` would print value text. The cure names the forbidden-prefix rule
      (*a `VIBE_*` or any other variable in that file is refused*) without naming what was found;
    - **multi-line quoted values — what coai can and cannot say** (corrected by the same consult; the gate text claimed a refusal
      it could not deliver): a continuation line shaped like a binding refuses (above — the safe direction); a continuation shaped
      like a comment or blank (`MISTRAL_API_KEY="dummy` + `#ending"`) is ADMITTED, and that is sound for admission, not only
      tolerated: every binding python-dotenv makes starts on a physical line, coai reads every physical line, so no name can be
      bound that coai did not see (`MISTRAL_API_KEY="a` + `"` + `VIBE_X=1` binds `VIBE_X` on its own line, and coai refuses it).
      What coai cannot do is SAY "keep each variable on one line" reliably, so that sentence is not promised; the
      whole-file vectors (L-V6b) pin all three shapes;
    - **what a "line" is** (cadence consult a3933362, turn 2, verified): python-dotenv's line break is `
`, `
` OR a bare ``
      (`_newline` in `dotenv/parser.py`) — `MISTRAL_API_KEY=x` + bare CR + `VIBE_X=1` binds `VIBE_X`. coai's reader splits on exactly
      those three and nothing else (a vertical tab, form feed or NEL binds no second name — measured, and splitting on them would
      only refuse more). A reader that splits on `
` alone would admit that file: this is the one place the soundness argument
      above depends on the implementation, and S-V13 pins it;
    - the cure for both (owner question Q2): keep only `MISTRAL_API_KEY=…` in that file.

    A test pins that no sentence, log line or exception message holds a byte of any value;
  - **who makes it**: the launch control, inside the home's turn, after the shape check and before `--version`
    (`VibeLaunch.TakeAsync`, `VibeLaunch.cs:146-172`), on a non-Windows host only — so the reviewer, retry, consult, question row,
    consultant check and `providers` probe get it from one place;
  - **what is admitted**: in `VibeHome.Judge` (`VibeHome.cs:94-103`) a link named `.env` at the home's top level, on a non-Windows
    host, whose ONE-level target (`Junctions.Target`, `src_mcp/runners/Files/Junctions.cs:122-134`) string-equals the expected path.
    Any other link, a `.env` that is a file, a `.env` on Windows, a `.env` one level down — refused as today;
  - **no `.env`** at the person's home: no link is made and Vibe runs; it exits 1 with L-V1's wording, free, and
    `VendorDiagnosis.cs:109-112` names it — its cure text becomes per host (Linux and WSL: *run `vibe --setup` in a terminal on this
    side; the sign-in is `~/.vibe/.env`, which coai links into its own homes and never copies*). **The sentence names the keyring
    case** (the plan gate, F10): *a sign-in kept in a desktop keyring is invisible to coai's launches — their built environment
    carries no D-Bus session (`ProcessEnvironment.cs:90-91`) — so coai needs `~/.vibe/.env`*. No pre-check: the run is free and its
    wording is measured;
  - **the link cannot be made** (the plan gate, F3) — a filesystem without symbolic links, `~/.vibe` missing, permission denied:
    nothing is left at `$VIBE_HOME/.env` (a half-made link is removed by its own name), and the launch falls through to the "no
    `.env`" path above: Vibe runs and exits 1 with L-V1's wording, and the cure states BOTH the setup command and the reason the link
    could not be made (the system's message, which names a path and never the file's content). Never a copy as a fallback;
  - **deletion**: `GitScratch.Unlink` already removes a FILE link by its own name and checks the target after
    (`src_mcp/runners/Files/GitScratch.cs:55-71`, `:116-133`); E2 adds the red-first Linux test that every deleter (round home,
    one-shot sweep `VibeHomes.SweepOneShot`, the D20 consult sweep, the test sweeps) leaves the target's bytes, mode and mtime as they
    were. `Writable` (`GitScratch.cs:85-92`) is never asked of a link — the test pins that a `chmod` never reaches the target.

  Why linking the key file into a coai-owned home is acceptable: the home is `0700` (`PrivateFolder`'s Unix leg,
  `src_mcp/runners/Files/PrivateFolder.cs:80`), so no other user can follow the link; the link is outside the snapshot Vibe reads,
  and a read outside the cwd is denied (L-V2) — a prompt-injected branch asking `read_file` for `$VIBE_HOME/.env` meets the same
  refusal as `~/.ssh`; on Windows the same key sits in the Credential Manager, which every Vibe launch reads anyway (RESULTS_vibe §12) — the
  exposure is the same, only its place differs. **One unknown, and it is a RELEASE GATE (L-V8):** whether Vibe also loads a `.env`
  from its WORKING folder — the reviewed branch's own `.env` in the snapshot. If it does, `.env` joins Vibe's snapshot exclusions
  on every host (the reviewed branch would otherwise set `VIBE_*` through it). The link is a configuration BOUNDARY, not only a
  filesystem exception: `VibeHome` exists because hooks, MCP servers and agents in a home change what runs (`VibeHome.cs:12-24`), and
  the home is judged before every launch, the version probe included and again after it (`VibeLaunch.cs:200-210`, `:213-225`).
  A second dotenv the reviewed repository can supply would be that boundary with a hole in it — so 0.58.0 is not tagged until L-V8 is
  recorded (E3's done-when), whatever E2's tests say.

- **DL3 — The Vibe home manifest is per platform.** The JSON already carries `"platform": "win32"`
  (`shared/vibe-home-manifest.json:33`, written by `scripts/vibe-home-manifest.mjs:176`), and the C# drops it (`ManifestVersion`,
  `src_mcp/runners/Vibe/VibeHomeManifest.cs:115-120`; `For(version)` at `:46`). So: `VibeHomeEntry` gains `Platform` (`win32` |
  `linux`, as node spells it). **An entry with no `platform` reads as `win32`** (the plan gate, F6): today's only entry is the
  Windows one, and an older manifest or a hand-made test entry must keep meaning what it meant — so `Complete` (`:70-72`) does NOT
  require the field; `For(version, platform)`; `VibeLaunch.Verdict` and `VersionWarning`
  (`VibeLaunch.cs:306-317`) pass THIS process's platform; `Known` names both (`2.26.1 (win32), 2.26.1 (linux)`). A version measured on
  another platform only is an UNMEASURED version here: it warns and runs with the shape check (owner rule), and the warning says on
  which platform it was measured. Today, without this, 2.26.1 on Linux is a KNOWN version whose plugin bytes differ — the one hard
  refusal (`VibeHome.cs:173`, `ByHash` at `:188`) — so every Linux Vibe launch would be refused. `logs/session/unified/.session-index.json` joins
  `varying` on every platform (session data, L-V-manifest). `VibeHome.Unhashed`'s "measured anywhere" admission of a starter file
  (`VibeHome.cs:176-183`) stays across all entries: those are bytes Vibe itself wrote. The `linux` entry is WRITTEN BY THE SCRIPT in
  WSL (E1), never by hand. **If two fresh-home runs disagree on the hashes (L-V7)**, there is no `linux` entry and Linux is always
  warn-and-run; the plan records it as the deviation. **Rollback:** going back to 0.57.x restores today's behaviour exactly — it
  reads only `version` and the pins, so on Windows the same entry is enforced, and on WSL D18 refuses before any home is judged. A
  WSL home 0.58.0 left behind (with its `.env` link) is never launched on by 0.57.x, and its sweeps unlink a file link by its own
  name already (`GitScratch.Unlink`, `GitScratch.cs:116-133`, shipped in 0.54.0).

- **DL4 — No Linux path budget.** `VibePathBudget.ForThisProcess` is already `None` off Windows (`src_mcp/runners/Vibe/VibePathBudget.cs:33`);
  L-V5 wrote 775 characters. No sanity bound is added: a path past `PATH_MAX` fails inside Vibe with its own error, which the
  generic diagnosis reports, and no measured case needs more.

- **DL5 — The CLIs are found where their installers put them, not only on the editor's `PATH`.** The VS Code server's `PATH`
  often lacks `~/.local/bin` (seen on the owner's machine under Remote-SSH), and `~/.kimi-code/bin` reaches only shells started after
  Moonshot's installer edited an rc file.
  - Vibe: `VibeRuntime.InstalledExecutableFor` (`src_mcp/runners/Vibe/VibeRuntime.cs:80-85`) is Windows-only. **The precedence on
    WSL, written out (the plan gate, F9):** (1) the row's own CLI path when set — already first today
    (`settings.ExecutablePath`, `VibeRuntime.cs:92`); (2) `$UV_TOOL_BIN_DIR/vibe`; (3) `~/.local/bin/vibe` (uv's default tool folder,
    where Mistral's script puts it — the scout found it there); (4) the `PATH` walk (bare `vibe`); (5) the install cure. (2) and (3)
    count only when the file exists and is executable. **Why the installer's folder comes BEFORE `PATH`:** the server's `PATH` often
    lacks `~/.local/bin`, which is the case this exists for; when both hold a `vibe`, the one in uv's folder is the one the panel's
    install and ⟳ buttons maintain (`uv tool install` / `uv tool upgrade`), while a `PATH` hit may be an older or foreign install —
    and it is the order Windows already uses (the installer's `vibe.exe` before the bare name, `VibeRuntime.cs:80-85`) and Kimi will
    use (DL5's next bullet). A person who wants another `vibe` sets the row's CLI path, which wins. uv's other documented bin-folder
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
  `IsLocalUnixPath` (`Junctions.cs:97-101`) is a PREFIX list (`/net/`, `/misc/`, `/Network/`), and a `/mnt/<letter>/` prefix added to it
  would be weaker than the guarantee it stands for (consultation c6e42fd3, verified): a sign-in folder on a Windows filesystem is
  shared with a Windows Kimi that no Linux lock can see (`HeldFile.cs:17-22`) — two refreshers of one rotating token — and a Windows
  filesystem reaches Linux through a custom `mount -t drvfs` point, a bind mount or `virtiofs` as well as through `/mnt/c`. So the
  rule is the FILESYSTEM that holds the real path, read from `/proc/self/mountinfo`: the deepest mount point at or above the path,
  refused when its type is `9p` with `aname=drvfs`, `drvfs` or `virtiofs`. **Ported**: `MountTable.Parse` / `Holding` and
  `MountEntry.IsDrvfs` (wsl_care's `src_daemon/src/WslCare.Core/Files/MountTable.cs:18`, `:29`, `:34` — 108 lines, a pure
  parser with its tests, the kernel's octal escapes decoded and the super-options kept raw as the kernel prints them). The sentence
  still names `/mnt/<letter>/` as the example; the rule is the mount type. On a native Linux without drvfs the rule is inert (Q1).
  It is judged on the real path (`Resolved`, `Junctions.cs:179`). coai never `chmod`s the target (Kimi keeps it `0700`, RESULTS_kimi
  §11). **Waits for L-K2.**

- **DL7 — The Linux lock key is the folder's identity: `(st_dev, st_ino)` from `statx`.** *Security / authentication.*
  `KimiCredentialsLock.KeyFor` (`src_mcp/runners/Kimi/KimiCredentialsLock.cs:116-120`) hashes `HandleRead.FinalDirectory`, which is
  `""` off Windows (`src_mcp/runners/Files/HandleRead.cs:226-238`), so the key is empty and the lock `Unusable` — fail-closed by
  design (E4.2 decision 9; the remark at `KimiCredentialsLock.cs:53-56`). A Linux PATH is a spelling — two bind mounts or two
  symlinked spellings give two keys, two refreshers, a signed-out person; the kernel path from `/proc/self/fd`
  (`HandleRead.cs:244-265`) is the same spelling problem. Device and inode name the folder itself. So `KeyFor` asks a per-platform
  IDENTITY: Windows keeps the upper-cased final path; Linux hashes `dev_major:dev_minor:ino` from `statx(AT_FDCWD, path, 0,
  STATX_INO)` — `statx`'s struct is the same on every Linux architecture — through a `LibraryImport("libc")`, the precedent
  `src_mcp/service_defaults/AppendOnlyFile.cs:374-383`. The record and the read are **ported** with DL2's file reader:
  `FileStatus` and `SameFileAs` (one device, one inode — `RegularFiles.cs:12-21` in wsl_care) and `Status`'s offsets (`:240`); the
  key is that record's three numbers, nothing of the path. A failed `statx` is an empty key: still `Unusable`, never the spelling.
  The alias set a test must cover (consultation c6e42fd3): a symlinked parent, a bind mount of the same folder, `/proc/self/cwd`-style
  spellings — one key each; a deleted folder — no key.
  `HandleRead.FinalDirectory` stays as it is for the key's Windows arm; `Junctions`' locality check gets the Linux real path
  (`WorktreePaths.Real`, which `CanonicalDirectory` already falls back to, `HandleRead.cs:211-212`). Needs no Kimi cell.
  **The lock files are retired, not kept for ever** (the plan gate, F15). An identity key changes whenever the sign-in folder is
  made again (a new inode), so on Linux the files can accumulate where on Windows they could not. Assumed maximum: **8 sign-in
  identities per machine** in a lock root's life (one `~/.kimi-code/credentials`, a few re-creations, a second `KIMI_CODE_HOME` at
  most) — 16 empty files. Retirement: the gate file records, once, under its own hold, the identity it was keyed by and the folder
  path it came from (`dev:ino` and the path — no secret; the folder is the person's own). The sweep that already runs on every beat
  and at server start (`ConsultationSweeper`, `src_mcp/src/Server/Consultation/ConsultationSweeper.cs`, D20's host) removes a key's
  two files when it can take BOTH exclusively at once without waiting (nobody holds them) AND the recorded folder no longer exists
  or no longer has that identity; it unlinks them while it still holds them. Because removing a lock file under a flock is the
  classic two-holders race, every taker verifies after its take that the path still names the file it locked (`fstat` of its
  descriptor against `statx` of the path; a mismatch releases and takes again). A file the sweep cannot read or decide about is
  left, logged, never the reason the sweep fails (D20's rule).

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
  but the agent — ends each by identity and confirms. Recorded as a deviation from S4's letter; its test is S4's own. **Four
  things the teardown must be, each verified against today's code (consultation c6e42fd3):**
  - *It re-lists.* `ProcessTree.ConfirmGoneAsync` (`ProcessTree.cs:151-167`) re-checks the FIXED list it was handed
    (`alive = alive.Where(IsAlive)`) and never discovers a child born after the first listing. Under a subreaper every orphan of the
    tree is reparented to the shim, so the confirmation loop re-lists `Descendants(shim pid)` on every step until the listing is
    empty or the bound passes — a grandchild born late is found by its new parent, not missed.
  - *It is bounded.* `ProcessLauncher.EndTreeFirstAsync` (`ProcessLauncher.cs:645-650`) waits for the agent's exit with
    `CancellationToken.None`, on purpose (`:628-630`: a cancelled wait returns before the handle is free). The agent's wait stays
    as it is — SIGKILL is not ignored — but the DESCENDANT confirmation has its own bound (`ConfirmationBound`, `:39`), and a member
    still there at the bound is a SURVIVOR, never waited on for ever.
  - *It signals by identity, not by number.* `ProcessTree.Terminate` (`:124-144`) checks `IsAlive` and then `Process.Kill` — two
    steps, and a pid reused between them is somebody else's process. On Linux the end goes through a `pidfd`: `pidfd_open` pins
    the process, its start is compared with the identity, and the signal goes through the descriptor, so the number cannot be
    reused under the signal. **Ported in part**: the native layer and the pin → check → signal → poll order
    (wsl_care's `src_daemon/src/WslCare.Core/Processes/ProcessSignals.cs:192-213`, `LibcPidfdCalls` at `:281-332`) come
    over; the shell around it is REWRITTEN on coai's own `ProcessIdentity(Pid, StartedUtc)` and `OrphanSweep.Same` slack
    (`ProcessTree.cs:28-29`: one identity rule for every record here), not wsl_care's start ticks, `Tuning` or `IFileSystem`.
    glibc's wrappers need 2.36 (Ubuntu 24.04 has 2.39); a missing symbol is answered by the story (raw `syscall` numbers 434/424,
    which are the same on x86-64 and arm64, or a stated refusal) — never by falling back to a kill by number in silence.
  - *A survivor changes ELIGIBILITY, not only a report.* Today's text said "reported in the shim's structured report". That is
    not enough: the tree hold is disposed when `AcpClient.AskAsync` returns (`AskAcpMode.cs:127-131`), it is held only by processes
    that INHERITED the descriptor (`HeldFile.cs:25-30`), and L-K5 measured that a `close_fds`/`setsid` grandchild holds nothing — so a
    survivor can be alive, refreshing, and invisible to the next launch's gate. The policy is the one `ReaderMarker`/`ReaderReconcile`
    already applies to a vendor slot (`src_server/src/Slots/ReaderMarker.cs:65-68`, `:114-136`): the shim writes the members it
    listed (pid and start) to a marker under the lock root, keyed by the sign-in, BEFORE it signals; a clean confirmation clears it;
    the NEXT launch's gate (`KimiLaunch.TakeAsync`, after the gate file is held) reconciles the marker first — terminate by identity,
    confirm, else the turn is Busy with the survivor named — and `KimiHome`'s disposal (`KimiHome.Alive`, `KimiHome.cs:338-346`) keeps
    the home while a marker stands. A marker that names nothing it can match (the shim died between the write and the listing) blocks
    too, until a person clears it or the process it would have named is provably gone — the price that precedent accepted (Q6).
    `ReaderReconcile` MOVES from `src_server` to `src_mcp/runners/Processes` (reuse-first step 2.4: both projects need it and the server
    already references the runners), it is not copied.
    **The way out is a panel button** (the plan gate, F11; the owner's rule that every action is a button). The Busy sentence names
    the marker FILE by its full path, the pid(s) it names, and says that it is safe to clear once those processes are gone — and that
    the Kimi card's *Clear the stuck Kimi marker* button does it. The button runs a one-shot mode of the server
    (`coai-mcp --kimi-clear-marker <path>`), which re-checks every member by identity first: it clears the marker only when none of
    them lives, and otherwise answers with the pid that still does — the button can never clear a marker over a living survivor.
    The mode joins the one-shot list in `.agents/PROJECT.md:51-68` ("adding a one-shot mode means adding it here"), with its own exit
    codes, never 64. The card shows the button only while the server's providers answer reports a standing marker for that row.
  **The supervisor's own failure is in scope.** A subreaper that is killed cleans nothing up. Verified: the `--ask-acp` mode is
  started with no cancellation token and registers no signal (`Program.cs:430`; `AskAcpMode.RunAsync`'s `ct = default`,
  `AskAcpMode.cs:73-79`), so on a `SIGTERM` the runtime exits without running the `finally` that ends the agent
  (`AcpClient.cs:106-110`); what covers the children then is the SERVER's `Process.Kill(entireProcessTree: true)` on the shim
  (`ProcessLauncher.cs:653-663`), which lists the shim's descendants by parent pid while the shim still lives — and under the
  subreaper that listing holds the reparented orphans too. So: the shim registers `SIGTERM`/`SIGINT` into its token the way the
  stdio server already does (`Program.cs:1988-1997`, reused), the L-N2 test gains the arms "the shim is `SIGTERM`ed while the agent
  lives" (the finally runs; the tree is ended) and "the shim is `SIGKILL`ed" (nothing of the shim runs; the marker written before
  the start names the agent; the next launch reconciles it), and the cancellation of the outer launcher is the same arm seen from
  the server.
  **Measured (L-K4, RESULTS_kimi_confinement §29): confirmed.** The real `rg` of a Grep turn runs in its OWN group and session; a
  group kill misses it and a pid kill leaves it, alive 0.1–0.5 s. Without a subreaper its orphan is reparented to WSL's
  `Relay(<pid>)`, not to pid 1 — so no teardown may look for `ppid == 1`; under `PR_SET_CHILD_SUBREAPER` the orphan comes to the shim
  and `Descendants(shim pid)` finds it. Only the agent held the inherited lock descriptor (DL8's premise holds). Not run: Kimi's
  sub-agents, background shell and MCP servers — S4's test keeps its own grandchild arm.

- **DL10 — The foreign-Kimi wait reads `/proc` on Linux.** `HostProcessList.Current` returns `[]` off Windows
  (`src_mcp/runners/Kimi/HostProcessList.cs:29`, `:53`), so on Linux the wait for the person's own `kimi` never waits — silently. The
  Linux listing reads `/proc/<pid>/stat` (pid, parent, the bracketed `comm`); its reader is EXTRACTED from `ProcessTree.LinuxParents`
  (`ProcessTree.cs:169-196`), which already parses that line for the parent — one reader, not two. `KimiNames`/`CoaiNames`
  (`ForeignKimi.cs:46-48`) keep `kimi` and `coai-mcp` (both under `comm`'s 15 characters). **An unreadable or incomplete `/proc` is
  never evidence that nobody is running** (consultation c6e42fd3, verified): `IProcessList.Snapshot` says "empty when the host
  cannot say" (`ForeignKimi.cs:9-10`) and `Wait` reads an empty listing as the way being clear (`ForeignKimi.cs:62-74`) — fail-open,
  and exactly what the `[]` off Windows does today (`HostProcessList.cs:53`). On Linux the listing answers a closed union — the
  processes, or *unreadable* with the reason — and *unreadable* ends the wait as Busy with the reason, never as clear; a listing that
  does not contain the shim's own pid is unreadable by definition. The limit stays worded as `ForeignKimi.cs:37-39` already words it:
  a check at the START, which cannot stop a `kimi` the person opens afterwards.
  **Re-decided by L-K6 (RESULTS_kimi_confinement §29): match the EXECUTABLE, not `comm`.** A running `kimi acp` has `comm` =
  `MainThread` (every Node process on that machine does; it read `kimi-code` once, during shutdown), so `KimiNames` matched against
  `/proc/<pid>/stat` would never see an ACP agent — the wait would be fail-open again by another road. `/proc/<pid>/exe`'s basename
  is `kimi`, stable for the whole run. So the Linux listing reads, per process: the parent from `stat` (the extracted reader), the
  owner uid (`/proc/<pid>` owner), and the `exe` link's basename; a process is Kimi when that basename is `kimi` OR its `comm` is
  `kimi` (the TUI, as the first scout saw it). **Only processes of THIS uid are judged**: another account's process answers `EACCES`
  on `exe` (`/proc/1` does, for everyone), and it cannot refresh this person's sign-in — its folder is `0700` — so it is skipped,
  not counted unreadable (otherwise every launch is Busy). A same-uid process whose `exe` cannot be read is *unreadable* → Busy with
  the reason. `coai-mcp` in the parent chain is matched the same way, by the `exe` basename (`coai-mcp`); its `comm` was not
  measured and is not relied on. The parent-chain rule itself held: the agent's chain reached the driver.

- **DL11 — Kimi's OS-home redirect on Linux is `HOME` alone.** D19 already says so (parent plan, "On Unix the redirect is `HOME`
  alone, when D18 lifts"). `KimiEnvironment.Built` sets `HOME` → `os/` on Unix and not the three Windows names; its refusal checks
  `HOME` only there (`src_mcp/runners/Kimi/KimiEnvironment.cs:138`). The Unix minimal set has no `XDG_*`
  (`src_mcp/runners/Processes/ProcessEnvironment.cs:90-91`), so Kimi derives its XDG folders from the redirected `HOME` — into `os/`,
  which `KimiHome.OsProblem` refuses the moment it holds anything (`src_mcp/runners/Kimi/KimiHome.cs:185-195`).
  **Decided by L-K3 (RESULTS_kimi_confinement §29): `os/` stays EMPTY; the built environment gains `XDG_CACHE_HOME`.** Every launch,
  `--version` included, extracts nine native files (about 2 MB) into `$HOME/.cache/kimi-code/native/<version>/linux-x64/<sha256>/` —
  the same in three fresh folders, and nothing else lands in `os/`. A free arm measured that `XDG_CACHE_HOME` moves exactly that
  extraction and leaves `HOME` empty. Of the two measured cures this is the one that does NOT widen the `os/` rule (the other,
  admitting that cache path inside `os/`, would), so it needs no owner decision: `KimiEnvironment.Built` sets `XDG_CACHE_HOME` to a
  `cache/` folder inside the round's owned tree, beside `os/` and NOT inside `KIMI_CODE_HOME` (whose shape `KimiHome` judges), created
  `0700` with the home and disposed with it; `OsProblem` is unchanged and its test gains the Linux arm *a launch leaves `os/` empty*.
  The cost is 2 MB and one extraction per owned home. Also measured: Kimi reads `$HOME/.agents/AGENTS.md` and `skills/*/SKILL.md` of
  the REDIRECTED home (a canary in an owned fake home came back in the answer) — so `os/` staying empty is also what keeps a skills
  folder out of the review; the person's real `~/.agents` was not touched.

- **DL12 — `rg` and `fd` on Linux are provisioned by the install button.** The owner rules (set it up once; every action is a
  button) exclude "ask the owner to install them by hand". The line, one press, each step guarded and idempotent:
  - `rg`: skipped when `rg` resolves or `~/.kimi-code/bin/rg` exists; else `sudo apt-get install -y ripgrep` where `apt-get` exists,
    `sudo dnf install -y ripgrep` where `dnf` does — the projects' own README instructions, chosen by `command -v` inside the line;
  - `fd`: skipped when `fd` resolves or `~/.kimi-code/bin/fd` exists (the owner's case: it does); else the `fd-find` package and,
    because Debian names the binary `fdfind` and Kimi looks for `fd`, `ln -s "$(command -v fdfind)" ~/.local/bin/fd` (fd's README);
  - any other distribution — neither `apt-get` nor `dnf` resolves — (the plan gate, F2 + F13): the line installs nothing for the
    tools and prints ONE generic instruction, *"install ripgrep and fd with your distribution's package manager, or place them on
    PATH or in ~/.local/bin"*, then continues with Moonshot's script; `KimiTools.Refusal` says the same sentence when it finds
    neither tool and neither package manager. coai does not guess a third package manager's spelling;
  - then Moonshot's script, as today (`vendorTerminal.ts:528-532`).

  `OFFICIAL_SOURCES` (`src_vs_code/src/officialSources.ts:17-37`) gains `distroPackages: ['ripgrep', 'fd-find']` and the `VERBS`
  table (`:52-75`) gains `apt-get install` and `dnf install`, `-y` only, packages from that list only. **`sudo` is a NEW rule, not a
  consequence** (consultation c6e42fd3, verified): `fromOfficialSources` refuses a step only when it carries an install word
  (`unknownInstall`, `officialSources.ts:137-140`; the words at `:77`), so a step `sudo rm -rf …` or `sudo chmod …` beside one admitted
  URL is admitted TODAY — S-K10's `sudo rm -rf` is red against the current parser for that reason. The rule added: a step whose first
  word is `sudo` is refused unless the rest of it is one of the two admitted package steps; `ln -s "$(command -v fdfind)" …` and
  `command -v` carry no `sudo` and no install word and stay as the parser reads them. *Security (supply chain): Fable's security
  judgement, outside the cornerstone quota.* `KimiTools.Refusal` (`KimiTools.cs:51-61`) names the cure
  for the HOST — today it quotes `winget` everywhere (`RipgrepInstall`/`FdInstall`, `:24-27`): on WSL the `apt-get` or `dnf` line
  when that manager resolves, else the generic instruction above. Rejected: a release binary dropped
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
| L-V8 | Does Vibe load a `.env` from its WORKING folder? (A bogus `MISTRAL_API_KEY` in the cwd, no home `.env`: still L-V1's "Missing" → it does not.) Code reading of Vibe's package first. | free | E2 (snapshot exclusion) **and E3's release** — 0.58.0 is not tagged before it is recorded |
| L-V6b | python-dotenv — the version in Vibe 2.26.1's own tool environment — over DL2's fixed set of lines AND whole files (the three multi-line shapes of DL2), written as `shared/dotenv-name-vectors.json`: per vector the names python-dotenv binds, and coai's expected verdict (admitted, or refused at line N with no file text). No `.env` of the owner's is read. | free | E2 (DL2's grammar) |
| L-V9 | L-V1, L-V2/3, L-V4 re-run through the ported probe, 3 of 3. | ~10 turns | E3 (release) |
| L-V10 | L-V9 once on a NATIVE Linux host (not WSL). | ~10 turns | lifting DL1's native-Linux refusal — not this release |
| L-N1 | .NET on Linux: `HeldFile.Take(…, inheritable: true)` excludes a second take (inheritable or not), and a child started after it holds the lock after the parent's SIGKILL. A test in the Linux CI job, in a child process (no test holds an inheritable handle in the host). | free | E5 (DL8) |
| L-N2 | A subreaper shim and a FAKE agent that starts a same-group child and a `setsid` child, then exits first: after the teardown both are gone. Three more arms (DL9): a grandchild born AFTER the first listing is found by the re-listing confirmation; the shim `SIGTERM`ed while the agent lives ends its tree; the shim `SIGKILL`ed leaves a marker the next launch reconciles. A test in the Linux CI job. | free | E5 (DL9) |
| L-N3 | A survivor marker (a fake agent's `setsid` child that ignores `SIGTERM`, recorded by identity) makes the next launch on that sign-in Busy with the pid named, and the home disposal keeps the home; once the child is gone the next launch clears it. A test in the Linux CI job. | free | E5 (DL9's eligibility rule) |
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
4. **E4** — the Kimi WSL measurements (the cells above), the owner present. It depends on E1's ported Kimi probes only, so it may
   run beside E2–E3; what serialises it is the owner's presence and his own `kimi` closed.
5. **E5** — the Kimi platform mechanisms (DL6–DL10), in two halves. The Kimi-INDEPENDENT half — DL7's identity key, L-N1, L-N2,
   L-N3, the `/proc` reader's extraction, the survivor marker's shape, the ported file reader and mount table — may start right
   after E1. The Kimi-DEPENDENT half — DL6 (the symlink into the `0700` folder), DL8's last clause (no descendant of `kimi acp` is a
   second `kimi`), DL9's mechanism choice (what `kimi`'s tree actually looks like) and DL10's names — **does not merge before E4 has
   recorded L-K2, L-K4 and L-K6 and DL6/DL8/DL9/DL10 are each confirmed or re-decided in this plan**: generic Linux cells (L-K5,
   the fake-agent harness) prove the kernel's behaviour, not Kimi's own descriptor and refresh behaviour, and a re-decision goes to
   the owner first. E4 is a decision gate, not a formality.
6. **E6** — Kimi on a Linux host: DL1 for Kimi, DL5's Kimi half, DL11, DL12, the extension, `KIMI_ON_LINUX_SINCE`, the live checks.
   Not before E5's survivor rule (DL9) is in: a release in which a survivor is only a report would ship the seam the consultation
   named. **Release the next minor.**

Each epic is one branch, one PR, the coai gate once per epic. **A cadence consultation for epics E1–E3 is owed before their code
round** (the plan gate's command, 2026-10-16) — asked once the three epics' code exists and before `review_code` is called, its
answer recorded in the E1–E3 stories documents.

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
  this side, naming `~/.vibe/.env` AND the keyring sentence (a desktop keyring is invisible to coai's launches, no D-Bus).
  Red: `"the WSL cure should name ~/.vibe/.env and say a keyring sign-in is invisible to coai, but said: …"`. (No pin AGAINST the
  word "keyring": the sentence must contain it.)
- S-V8 *Given* a Linux host and Vibe 2.26.1 with the `linux` entry's bytes, *then* no warning; with one plugin byte changed, refused with
  the path. Red: `"a Linux 2.26.1 home with its own bytes was refused against the win32 hashes"`.
- S-V9 *Given* a version with only a `win32` entry, on Linux, *then* it runs with the shared warning naming `win32`.
- S-V10 *Given* a home with a `.env` link to a `0600` file, *when* each deleter runs, *then* the link is gone and the file's bytes, mode
  and mtime are unchanged. Red: `"the deleter changed the person's ~/.vibe/.env (mode 600 → …)"`.
- S-V11 (DL5's precedence, one row per step) *Given* `vibe` only in `~/.local/bin` and a `PATH` without it, *then*
  `DefaultExecutable` is that path. Red: `"expected ~/.local/bin/vibe, got vibe"`. *Given* the row's own CLI path is set, *then* it
  wins over all. *Given* `$UV_TOOL_BIN_DIR/vibe` and `~/.local/bin/vibe` both exist, *then* the first. *Given* `~/.local/bin/vibe` and
  another `vibe` on `PATH`, *then* `~/.local/bin/vibe`. *Given* none, *then* bare `vibe` (the `PATH` walk), and a failed start carries
  the install cure.
- S-V12 *Given* a Windows host whose row names `wsl.exe`, *then* still refused with the D18 sentence (DL1).
- S-V13 (DL2's grammar, a Theory over `shared/dotenv-name-vectors.json`) *Given* each vector line, *then* the names coai reads equal
  the names python-dotenv bound. Admitted rows include `MISTRAL_API_KEY=v`, `MISTRAL_API_KEY = "v"`, `export MISTRAL_API_KEY='v'`,
  `  MISTRAL_API_KEY=v  # note` and `'MISTRAL_API_KEY'=v`. Red: `"python-dotenv binds MISTRAL_API_KEY from 'MISTRAL_API_KEY = "v"'
  but coai refused the line as unreadable"`. *Given* a bare key on its own line, a line beginning with `=`, or a quoted value that
  continues on the next line, *then* refused with the generic sentence naming the line number and nothing from the file. Red:
  `"the refusal echoed text from the .env: 'sk-test-…'"`. *Given* the whole files `MISTRAL_API_KEY="dummy` + `LEAK_CANARY=ending"`, *then*
  refused at line 2 and the sentence holds neither `LEAK_CANARY` nor `ending`; `MISTRAL_API_KEY="dummy` + `#ending"`, *then* admitted
  (python-dotenv binds one name); `MISTRAL_API_KEY="a` + `"` + `VIBE_X=1`, *then* refused at line 3. Red: `"the refusal named
  LEAK_CANARY, which is value text"`. *Given* `MISTRAL_API_KEY=x` + bare CR + `VIBE_X=1`, *then* refused at line 2. Red: `"a bare CR
  hid VIBE_X from coai, which python-dotenv binds"`. The Theory asserts the IMPLICATION over every vector, not name equality:
  *coai admits the file ⇒ every name python-dotenv binds is `MISTRAL_API_KEY`*, run through the product's own line reader.
- S-V14 *Given* `~/.vibe/.env` is a FIFO, a socket, or a file whose owner is another uid, *when* the link maker judges it, *then*
  refused through the descriptor without blocking (the test's FIFO has no writer). Red: `"the judgement blocked on a FIFO"` /
  `"a file owned by uid … was admitted"`.
- S-V15 (F3) *Given* the link cannot be made — the owned home on a filesystem that refuses symbolic links (a test seam), the person's
  `~/.vibe` missing, or the home not writable — *when* a reviewer launches, *then* nothing is left at `$VIBE_HOME/.env`, Vibe runs and
  exits 1 with L-V1's wording, and the row's cure names `vibe --setup` AND the reason the link was not made. Red: `"the cure did not
  say why the .env link could not be made"` / `"a half-made .env was left in the home"` / `"a copy of .env was made as a fallback"`.
- S-V16 (F6) *Given* a manifest entry with no `platform` field, *then* it reads as `win32`; *given* a Windows host and Vibe 2.26.1,
  *then* the launch, its verdict and its warning are exactly today's (no warning; one changed plugin byte refused with the path).
  Red: `"an entry without a platform was not read as win32"` / `"a Windows 2.26.1 launch changed: …"`.
- S-V17 (F10) *Given* a NATIVE Linux host (`HostKind.Linux`) and a Vibe row, *then* refused before anything starts, the sentence
  saying Vibe is measured on WSL2 only; *given* `HostKind.Wsl`, *then* it starts. Red: `"a native Linux host should still be refused
  with the WSL2-only sentence, but Vibe started"`. A Theory over three `/proc/version` banners — WSL2 (`…-microsoft-standard-WSL2`),
  WSL1 (`4.4.0-19041-Microsoft`), native (`…-generic`) — through `NamesWsl2` and the admission: only the first starts. Red: `"a WSL1
  banner was admitted as WSL2"`.

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
  `sudo rm -rf`, or `apt-get install ./x.deb`, *then* refused. Red for `sudo rm -rf` against today's parser: `"a step 'sudo rm -rf …'
  beside an admitted URL was admitted"` (`unknownInstall` reads no install word in it).
- S-K11 *Given* a sign-in folder reached through a BIND MOUNT of its parent, *then* `KeyFor` of both spellings is ONE key (DL7).
  Red: `"a bind-mounted spelling of one sign-in folder gave a second lock key"`.
- S-K12 *Given* a mountinfo table with a `drvfs`-typed mount at `/data/win` (not under `/mnt/`) and a target below it, *then*
  refused as a Windows filesystem; *given* the same table and a target on the ext4 root, *then* admitted (DL6). Red: `"a sign-in
  folder on a drvfs mount outside /mnt/ was admitted"`.
- S-K13 *Given* a Linux listing that cannot be read (a `/proc` the fake cannot open, or one without the shim's own pid), *then*
  the foreign-Kimi wait answers Busy with the reason, and nothing starts (DL10). Red: `"an unreadable process listing was read as
  'nobody is running'"`.
- S-K14 *Given* a survivor marker from the previous turn naming a process that is still alive and the same (pid and start), *when*
  the next launch takes the gate, *then* it reconciles first: terminate by identity, confirm, and — the process still there at the
  bound — Busy with the pid; the home disposal keeps the home. *Given* the marker names a process that is gone, *then* cleared and the
  launch proceeds (DL9). Red: `"a launch proceeded on a sign-in whose previous tree still has a survivor (pid …)"`.
- S-K15 *Given* a fake agent that starts a `setsid` grandchild AFTER the shim's first listing and then exits, *when* the teardown
  confirms, *then* the re-listing finds the grandchild under the subreaper and ends it (DL9). Red: `"a grandchild born after the
  first listing survived the confirmation"`.
- S-K16 *Given* the shim is `SIGTERM`ed while its agent lives, *then* the finally runs and the agent's tree is ended; *given* it is
  `SIGKILL`ed, *then* the marker written before the start still names the agent (DL9). Red: `"after SIGTERM the agent (pid …) is
  still alive"`.
- S-K17 (F2 + F13) *Given* a WSL side where neither `apt-get` nor `dnf` resolves (stand-ins), *when* the install line runs, *then*
  it installs no tool, prints the generic instruction and still runs Moonshot's script; *given* `rg` and `fd` missing on such a host,
  *then* `KimiTools.Refusal` holds the same generic sentence and neither `winget` nor `apt-get`. Red: `"on a host without apt-get or
  dnf the line tried 'sudo apt-get …'"` / `"the refusal named a package manager this host does not have"`.
- S-K18 (F11) *Given* a standing survivor marker, *when* the next launch is Busy, *then* its sentence holds the marker's full path,
  the pid(s), "safe to clear once … gone" and the button's name; *when* the button's one-shot mode runs while a named member still
  lives, *then* the marker stays and the answer names that pid; once none lives, the marker is gone. Red: `"the Busy sentence gave
  no way out: no marker path"` / `"the clear mode removed a marker over a living survivor (pid …)"`.
- S-K19 (F15) *Given* a lock root with a key whose recorded folder no longer exists and nobody holds its files, *when* the sweep runs,
  *then* both files are gone; *given* the same with the gate file held, *then* both stay; *given* a taker whose locked file was
  unlinked by the sweep between its open and its lock, *then* it re-takes on the new file. Red: `"a lock file of a vanished sign-in
  folder outlived the sweep"` / `"two holders of one sign-in lock after a sweep"`.

## 8. Test plan

- **Flip the pins, red first.** The tests that hold the refusals today are rewritten for the new rule in the same commit as the
  change, and each is observed red against the old code: `src_mcp/tests/VibeLaunchControlTests.cs:257-268` (Linux and WSL refused →
  admitted; a `wsl.exe` row on Windows still refused), `KimiLaunchChecksTests.cs:184-195`, `KimiLaunchControlTests.cs:81-84`,
  `JunctionsTests.cs:275-290` (S1.4 becomes: Linux and WSL make a symlink, macOS and Other still refused),
  `HostProcessListTests.cs:75-81` (the empty listing becomes a `/proc` listing), `KimiAcpProfileTests.cs:47-56` (Linux finds
  `~/.kimi-code/bin/kimi`; macOS still guesses nothing); the extension's hint and help pins; the `TheOwnersKimi` skips are re-read
  and each kept or lifted with its reason.
- **Linux CI runs the real paths.** S-K3, S-K4/5 (L-N1), S-K6/S-K15/S-K16 (L-N2), S-K11 and S-K14 (L-N3) run WITHOUT the
  `WindowsStandIn` stand-in on the ubuntu job; the stand-in stays for the Windows-only decisions. The skips at
  `KimiCredentialsLockTests.cs:247`, `:296`, `:314` are lifted on Linux. S-K12 and S-K13 run everywhere over a fake table / fake
  listing (pure functions), as `ForeignKimiTests` already does.
- **The ported code keeps its tests.** `RegularFiles`' Linux leg, `MountTable` and the pidfd layer come with the tests wsl_care
  runs on them, re-homed under `src_mcp/tests/`; a port without its tests is a copy.
- **Deleters around a file link** (S-V10): `GitScratch.DeleteEvenIfReadOnly`, `VibeHomes.SweepOneShot`, the D20 sweep, the test sweeps.
- **Extension:** `officialSources` verbs (S-K10); the Linux install line passes `bash -n` and runs against stand-ins for `apt-get`,
  `dnf`, `sudo` (the `kimiWindowsInstallRuns.test.ts` method); the per-host hints and the five help languages (`helpCoverage.test.ts`);
  `VIBE_ON_LINUX_SINCE` / `KIMI_ON_LINUX_SINCE` against an older server.
- **Probe scripts:** their own `node --test` files gain Linux cases (`scripts/lib/vibe.test.mjs`, `kimi.test.mjs`, `kimi-home.test.mjs`).
- **The scenario harness drives the shipped Linux flows** (the plan gate, F14; `scenario-tests.md`). The harness is the in-repo
  scenario suite under `src_mcp/tests`, catalogued in `research/module_tests.md`: the real server, the real launcher and the fake CLI
  (`FakeCli`) standing in for the vendor. Its Vibe flows exist and are Windows-only today — `AVibeReviewerRunsThroughAFakeVibeTests`
  (a code round, and a plan round on an unmeasured Vibe; both `Assert.Skip` off Windows at `:189-192`, `:232-235`, "D18"),
  `VibeConsultantTests` and `AVibeConsultationKeepsOneSnapshotTests` (consult), `AVibeQuestionRowReadsACopyOfItsRootsTests` (a
  question row). E2 runs all four flows — **a plan round, a code round, a consult, a question row** — on the Linux CI job with the
  runtime's host given as WSL (`VibeRuntime(id, host)`, the primary constructor at `VibeRuntime.cs:41`), the `.env` link made against a fake person's
  `~/.vibe/.env`, and one arm with `HostKind.Linux` refused (S-V17). E6 does the same for a Kimi row. Each flow goes into
  `research/module_tests.md` with what the harness does NOT prove: the real Vibe or Kimi (the fake CLI answers), the real
  keyring/D-Bus behaviour, a real WSL kernel's drvfs mount (CI is ubuntu, not WSL), the person's real `~/.vibe/.env`.
- **Live, after each release:** in WSL through the released binary — a plan round, a code round, a two-turn consult and a question
  row with a Vibe row (E3), the same with a Kimi row (E6); the real `~/.vibe` and `~/.kimi-code` compared before and after.
- Run as the repository runs tests: the MTP executables and `node --test`, never `dotnet test`.

## 9. Growth

| Surface | Size | Who retires it |
|---|---|---|
| `shared/vibe-home-manifest.json` entries | one entry per Vibe version per platform, ~12 hashes, ~2 KB | git; replaced when a version is re-qualified |
| The `.env` link | one per Vibe home | gone with its home (round disposal, one-shot sweep, D20) |
| Kimi lock files on Linux (`~/.local/share/coai-mcp/kimi-locks/`) | two small files per sign-in folder identity; assumed at most 8 identities in a lock root's life — 16 files | the beat and start sweep removes a key's files when nobody holds them and the folder they record is gone or has another identity (DL7, F15); a held or undecidable file is left |
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

- **Q1 — Native Linux too, or WSL only? DECIDED by the plan gate (F10), 2026-10-16: WSL only** until a native-Linux run of L-V9
  (L-V10) is recorded; `HostKind.Linux` keeps a WSL2-only refusal (DL1).
- **Q2 — A `~/.vibe/.env` with more than `MISTRAL_API_KEY`, or a line the grammar cannot read.** Default: refuse; for a validated
  name list the names, cure "keep only the key there"; for an unreadable line say only its number, cure "each line is `NAME=value`
  or a `#` comment" (DL2's grammar — the refusal never quotes the file). The alternative — coai reads the key and passes it in the
  environment — is DL2's rejected option.
- **Q3 — His own `kimi` TUI open in WSL makes every coai Kimi turn wait, then answer busy** (decision 74 of the E4 plan, the same on
  Windows) until M-K14 is measured. Keep it, or run M-K14 first?
- **Q4 — `sudo apt-get` in the install terminal** (he types his password there). Default: yes.
- **Q5 — Release order** if `feat/kimi-consultant` is ready first: it takes 0.58.0 and Vibe-on-Linux 0.59.0 (DL14).
- **Q6 — A survivor blocks the sign-in for coai** (DL9): while a marker names a process coai could not end — or names nothing it can
  match, because the shim died between the write and the listing — every coai Kimi turn on that sign-in is Busy with the pid (or the
  reason), re-tried on every launch, until the process is gone or a person clears the marker. The person's own `kimi` is never touched.
  Default: accept the availability cost, as the vendor-slot precedent did (`ReaderMarker.cs:89-93`); the alternative — a survivor is
  only reported — leaves two refreshers possible. The way out is the card's *Clear the stuck Kimi marker* button, which never clears
  over a living survivor (DL9, F11).

## Definition of Done

- [ ] E1–E6 done-when met; every cell of §5 that gates a shipped epic recorded in `research/RESULTS_vibe_confinement.md` /
      `RESULTS_kimi_confinement.md` (Linux sections), with predictions written before each run.
- [ ] A Vibe row reviews, consults and answers a question row in WSL through the released 0.58.0; the real `~/.vibe` unchanged.
- [ ] 0.58.0 admits `HostKind.Wsl` only; a native Linux host is refused with the WSL2-only sentence (S-V17) until L-V10 is recorded.
- [ ] The scenario harness drives a plan round, a code round, a consult and a question row with a Vibe row on the Linux CI job
      (and the same with a Kimi row in E6); each flow and what it does not prove is in `research/module_tests.md`.
- [ ] A failed link falls through to the signed-out cure with its reason (S-V15); a manifest entry without `platform` is `win32` and a
      Windows launch is unchanged (S-V16); the names grammar matches python-dotenv's vectors (S-V13).
- [ ] A Kimi row the same through its release; the real `~/.kimi-code` unchanged but for Kimi's own token refresh.
- [ ] Every D18 pin flipped red-first; a `wsl.exe` row on Windows and a macOS host still refused.
- [ ] The `.env` link: exactly one admitted link, the target judged through one descriptor, the names grammar with its two refusals,
      no value or unread line ever in a sentence or log, deleters proven around it; L-V8 recorded before 0.58.0 is tagged.
- [ ] The lock key is identity-based on Linux (aliases and bind mounts one key), the locality rule by mount type, the tree hold proven
      exclusive and inherited (L-N1), an unreadable `/proc` never read as clear, the subreaper teardown proven with its re-listing,
      its bound, its identity signalling and its supervisor-failure arms (L-N2), a survivor blocking the next launch and the home
      disposal (L-N3).
- [ ] The install button provisions `rg`/`fd` on Debian/Ubuntu and Fedora and prints the generic instruction elsewhere (S-K17);
      `OFFICIAL_SOURCES` admits exactly those verbs and packages.
- [ ] A survivor marker can be cleared from the panel, never over a living survivor (S-K18); the Kimi lock files are retired (S-K19).
- [ ] Hints, notes and help in five languages say what each side's server does; `*_ON_LINUX_SINCE` level with the tags.
- [ ] `research/module_runners.md` and `research/module_extension.md` updated; the parent plan's D18 amended; this plan promoted.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## Epics

Six epics, Vibe first; quota ⌈2·6/5⌉ = 3. Stories go to `todo/PLAN_kimi_and_vibe_on_linux_E<n>.md`, one per epic, written when the
epic starts and linked under it in the same commit ([subagent-models.md](../.agents/conventions/common/subagent-models.md)).

**Split confirmed / changed by Fable, 2026-10-16.** The six epics and the Vibe-first order stand; four things change in how they
hang together. *(1) E4 is a decision gate for E5, not a parallel measurement.* E5 was written as one epic "depending on E1 and E4",
which lets its Kimi-dependent half be coded on the fake-agent harness and L-K5's generic `flock` cells; those prove the kernel, not
`kimi`'s own descriptor inheritance, refresh behaviour or tree shape (DL8's last clause, DL9's mechanism choice). §6 now splits E5
into a Kimi-independent half that starts after E1 and a Kimi-dependent half that does not merge before L-K2, L-K4 and L-K6 are
recorded and DL6/DL8/DL9/DL10 re-confirmed — a re-decision goes to the owner first. E5 stays ONE epic (one branch, one PR, one gate)
because the two halves land in the same files (`KimiCredentialsLock`, `Junctions`, `AcpTransport`) and a second PR over the same
lines would be a merge exercise, not a boundary. *(2) E4 may run beside E2–E3.* Its only dependency is E1's ported Kimi probes;
what serialises it is the owner's presence and his own `kimi` closed — so the Kimi measurements need not wait for the Vibe release.
*(3) L-V8 is E3's release gate.* It gated E2's snapshot exclusion only; a second dotenv the reviewed repository can supply is a hole
in the configuration boundary `VibeHome` exists for, and 0.58.0 is not tagged until the cell is recorded. *(4) E6 does not ship a
survivor that is only a report.* DL9's eligibility rule (the marker reconciled by the next launch and the home disposal) is E5's,
and E6 depends on it. The release points stand: 0.58.0 after E3, the next minor after E6, DL14's second-merger rule unchanged.
Not changed, and weighed: splitting E5 into two epics (seven epics keep Q = 3 and would make the gate structural — rejected for the
same-files reason above; the §6 wording and E5's done-when carry the gate instead), and moving the installer line (DL12) out of E6
into its own epic (it is one button and its review is Fable's outside the quota either way).

**Marks.** Per the gate's operator command, authentication / security work is BUILT on Fable and everything else on Opus (Sonnet
codes under either; the mark says who writes the scenarios and the brief and who reviews). E1 ordinary · E2 **authentication /
security** · E3 ordinary · E4 ordinary (measurement on the owner's live sign-in) · E5 **authentication / security** · E6 ordinary,
with DL12's `officialSources.ts` verbs and the new `sudo` rule as a security (supply-chain) judgement on Fable, outside the
cornerstone quota.

Cornerstones: 0 of 3 — no story marked yet; reserved: A1 (E2), A2 (E5), A3 (E5)

The three AREAS reserve the whole quota, so E1, E3, E4 and E6 carry none and a fourth mark goes to the operator, not into a stories
document. Each area becomes ONE story when its epic's stories document is written (Opus marks it `model: fable` and moves the line
above to `k of 3`); Fable then writes that story's spec.

**The cornerstone areas**, in the consultation's terms after verification — three edges of one thing, the person's credentials in
a coai-owned process: who may be given them (admission), who holds them for how long (ownership), and the proof that nobody holds
them after (teardown). The highest risk is the seam between A2 and A3: a launch returns and its hold is disposed
(`AskAcpMode.cs:127-131`) while a descendant that inherited no descriptor (`HeldFile.cs:25-30`; L-K5) still holds the sign-in, and
the next launch's gate sees nothing. A1 has no such seam, and is a cornerstone because a wrong rule there has no symptom at all.

- **A1 — credential ADMISSION: the one admitted `.env` link (E2).** *Boundary:* everything between "the person's sign-in file
  exists" and "Vibe reads it from an owned home" — the expected-path rule; the descriptor-based judgement of the target (type, owner,
  mode and the capped read on one `O_NOFOLLOW` descriptor, the ported `RegularFiles` leg); the names grammar with its two refusals;
  the admission of exactly one link in `VibeHome.Judge` (`VibeHome.cs:94-103`); the link maker's place inside the home's turn, before
  `--version` (`VibeLaunch.cs:200-210`); and every deleter's behaviour around the link (`GitScratch.Unlink`, `GitScratch.cs:116-133`;
  `VibeHomes.SweepOneShot`; the D20 sweep). Cleanup is INSIDE the area: a deleter that follows the link is the same defect as a
  maker that admits the wrong one. *Minimum scenarios:* S-V1–S-V6, S-V10, S-V13, S-V14, S-V15, and L-V6b and L-V8 recorded before the release.
  *Review focus:* no byte of a value or of an unread line in any sentence, log or exception; nothing ever follows a link (the
  maker, the judge, the deleters); the target's bytes, mode and mtime unchanged on every cleanup route; the residual (two opens, the
  person's own account) stated in the code where the check is.
- **A2 — credential OWNERSHIP for the tree's whole lifetime: the Kimi lock as one lifecycle (E5).** *Boundary:* the three duties
  `KimiCredentialsLock` already names as one (`KimiCredentialsLock.cs:46-52`) — the server's gate, the inherited sign-in hold, the
  per-home hold — plus what decides whether they may be taken: the identity key (DL7, `dev:ino`; symlink and bind-mount aliases one
  key, a deleted folder no key), the exclusive inheritable take (DL8, L-N1), the mount-type locality rule (DL6, the ported
  `MountTable`), and the foreign-Kimi wait's honesty about `/proc` (DL10). *Minimum scenarios:* S-K2, S-K3, S-K4, S-K5, S-K7, S-K11,
  S-K12, S-K13, with L-K2, L-K4 and L-K6 recorded before the Kimi-dependent half merges. *Review focus:* no path on which "cannot
  tell" reads as "free" (an empty key, an unreadable listing, an unnamed handle each refuse); real shim-to-agent descriptor
  inheritance (not the test host's non-inheriting take); the parent's death with a living child; two spellings of one folder;
  independent server processes on one sign-in; a disposal's `HomeProbe` racing a surviving child (`HomeWait`,
  `KimiCredentialsLock.cs:69-72`).
- **A3 — VERIFIED teardown, and what a survivor does to the next launch (E5).** *Boundary:* DL9 whole — the subreaper, the
  re-listing confirmation (today's `ConfirmGoneAsync` re-checks a fixed list, `ProcessTree.cs:151-167`), the bound (today's agent
  wait has none, `ProcessLauncher.cs:645-650`), signalling by identity through a `pidfd` (the ported native layer), the shim's own
  failure arms (`SIGTERM` registered into the token — today none, `Program.cs:430`; `SIGKILL` covered by the marker written before
  the start), and the survivor marker reconciled by the next launch's gate and by the home disposal (`ReaderReconcile` MOVED to the
  runners, `ReaderMarker.cs:114-136`). *Minimum scenarios:* S-K6, S-K14, S-K15, S-K16, L-N2's arms, L-N3. *Review focus:* the A2∩A3
  seam above — a survivor must change the eligibility of the next launch and of `KimiHome.Alive` (`KimiHome.cs:338-346`), never
  only a report line; the marker is written BEFORE the signal and cleared only on a confirmed-empty re-listing; a marker that names
  nothing it can match blocks; nothing is ever ended by name.

**Consultation c6e42fd3 — what was taken and what was not.** One line per point; each verified before it was used.
- Three areas as admission / ownership / teardown, the seam between (2) and (3) the highest risk — **taken**; the seam is real:
  the hold is disposed when `AskAsync` returns (`AskAcpMode.cs:127-131`), only inheritors hold it (`HeldFile.cs:25-30`), a
  `close_fds`/`setsid` grandchild inherits nothing (L-K5).
- The `.env` link is a configuration boundary; `VibeHome` validates before every launch, the version probe included — **taken**;
  `VibeHome.cs:12-24`, `VibeLaunch.cs:200-210` (shape before `--version`) and `:213-225` (again after).
- L-V8 as a release gate — **taken**; it gated E2 only; now E3's done-when and §5.
- Descriptor-based admission (open without following the final link, FIFOs blocked, type/owner/mode on the same descriptor, the
  bytes capped), precedents `RegularFiles.cs` / `BeneathFiles.cs` — **taken for `RegularFiles`** (`ReadOwned` `:68`, `ReadOpened`
  `:146`, `Judged` `:161`, `Capped` `:253` — ported with its tests); **not taken for `BeneathFiles`**: the folder chain above the
  sign-in is the person's own, which coai admits for Kimi too (`Junctions.Resolved`, `Junctions.cs:179`). The residual (a swap
  between coai's check and Vibe's read) is stated in DL2.
- A restricted names grammar, a generic refusal for malformed syntax, never an echo of secret text — **taken**; DL2's old rule
  ("the text before the first `=`") would have echoed a key pasted on its own line; S-V13 pins it.
- Cleanup belongs in the admission area; `GitScratch`'s link-first deletion is the precedent — **taken**; `GitScratch.cs:55-71`,
  `:116-133`; A1 owns S-V10.
- The Kimi lock is one lifecycle with three responsibilities — **taken**; `KimiCredentialsLock.cs:46-52` names exactly those three
  files and duties; the test set (aliases, independent servers, parent death, real fd inheritance, a sweep racing a child) is A2's.
- The device/inode key, `FileStatus.SameFileAs` — **taken**; `RegularFiles.cs:12-21` in wsl_care; the record comes with the port.
- The `/mnt/<letter>/` refusal is weaker than its guarantee; `MountTable.cs` finds the filesystem holding a path — **taken**;
  `IsLocalUnixPath` is a prefix list (`Junctions.cs:97-101`), `MountTable.Holding` `:34` and `IsDrvfs` `:18` are ported; S-K12.
- Word the foreign-Kimi check honestly; an unreadable `/proc` is not evidence that nobody runs — **taken**; the limit is already
  worded at `ForeignKimi.cs:37-39`; the fail-open is real: "empty when the host cannot say" (`:9-10`) is read as clear by `Wait`
  (`:62-74`), and `HostProcessList.cs:53` answers `[]` off Windows; S-K13.
- The subreaper must cover the supervisor's own failure — **taken**; verified that the shim mode gets no token and no signal
  registration (`Program.cs:430`, `AskAcpMode.cs:73-79`), so the ending `finally` (`AcpClient.cs:106-110`) does not run on `SIGTERM`;
  the server's tree kill (`ProcessLauncher.cs:653-663`) is what covers the children, while the shim lives.
- Two traps: `EndTreeFirstAsync` waits unbounded (`ProcessLauncher.cs:645-650`) and `ConfirmGoneAsync` never discovers later children
  (`ProcessTree.cs:151-167`) — **both verified true, taken in modified form**: the agent's wait stays unbounded on purpose
  (`:628-630`); the DESCENDANT confirmation is what gets the bound and the re-listing.
- The precedents `ProcessSignals.cs` (pidfd + start, signal through the descriptor) and `ReaderMarker.cs` (unidentified readers or
  survivors leave the account blocked) — **taken**; `ProcessSignals.cs:192-213` is ported as the native layer and the order, its
  shell rewritten on coai's `ProcessIdentity`/`OrphanSweep.Same` (**not** wsl_care's start ticks — one identity rule here,
  `ProcessTree.cs:28-29`); `ReaderReconcile` (`ReaderMarker.cs:114-136`) is MOVED to the runners, not copied.
- Keep Vibe first; make E4 a genuine gate for E5 — **taken**; §6 and the split paragraph above.
- The installer: `officialSources.ts` ignores steps it does not read as an install, so apt/dnf verbs do not restrict arbitrary
  `sudo` — **taken**; verified at `officialSources.ts:137-140` (`unknownInstall` answers 0 for a step without an install word); the
  `sudo` rule is written as a NEW rule in DL12, reviewed by Fable outside the quota.

**Cross-repo ports, the rule applied.** Both repositories are MIT with one copyright holder, and wsl_care is public, so porting is
a reuse-first move, not a licence question; every ported file carries a header naming its origin and ships with its tests
(`RegularFiles`' Linux leg + `FileStatus`; `MountTable`; the pidfd native layer). Rewritten rather than ported: the signalling
shell (coai's identity type), and nothing of `BeneathFiles`.

**Risk items named to the gate** (at most two; passed on the next gate call):
1. **E2 — A1, credential admission**: the one `.env` link, its descriptor judgement, the names grammar and the deleters around it.
2. **E5 — A2 + A3, credential ownership and verified teardown**: the Kimi lock as one lifecycle, and a survivor that changes the
   next launch's eligibility.

**Plan gate 2026-10-16 (epics 1–3).** The gate reviewed the Vibe extract (E1–E3) and answered `good_enough`; the Kimi epics get
a round of their own. Accepted, and where each landed:
- **F2 + F13** — a distribution with neither `apt-get` nor `dnf`: one generic instruction (install with the distribution's package
  manager, or place the tools on PATH or in `~/.local/bin`), the same sentence in the `rg` refusal. §2 item 3 narrowed; DL12; S-K17.
- **F3** — the `.env` link cannot be made: fall through to the "no `.env`" path, the cure naming the setup command and the reason.
  DL2's new bullet; S-V15.
- **F6** — a manifest entry with no `platform` reads as `win32`; the rollback line (0.57.x restores today's behaviour). DL3; S-V16.
- **F8** — the names grammar follows python-dotenv's NAME part (whitespace, `export`, a quoted name), the value never parsed, and is
  checked against python-dotenv's own output as vectors. DL2's grammar bullet; §5 L-V6b; S-V13 rewritten with the `KEY = "v"` and
  `export KEY='v'` rows.
- **F9** — DL5's precedence written out (the row's path, `$UV_TOOL_BIN_DIR`, `~/.local/bin`, `PATH`, the cure) with why the
  installer's folder precedes `PATH`. DL5; S-V11.
- **F10** — 0.58.0 admits WSL only; native Linux refused with a WSL2-only sentence until L-V10; the signed-out cure names the keyring
  case. §2 item 1; DL1 (Q1 closed); DL2's "no `.env`" bullet; §5 L-V10; S-V7 (now asserting the keyring sentence), S-V17; E2, E3,
  the DoD.
- **F11** — the survivor marker's way out: a panel button over a one-shot mode that never clears over a living survivor, and a Busy
  sentence naming the marker's path. DL9; Q6; S-K18.
- **F14** — the scenario harness drives the four shipped Vibe flows on Linux CI, each catalogued with what it does not prove in
  `research/module_tests.md`. §8; E2's and E3's done-when; the DoD.
- **F15** — the Kimi lock files: at most 8 identities assumed, retired by the beat/start sweep under the two-holders guard. DL7;
  §9; S-K19.

Rejected, with the reasons recorded:
- **F0** (a swap window on `.env` between coai's check and Vibe's read) — only the person's own uid or root can swap it, because of
  the mode rule; `chattr +i` needs root; passing the key in the environment is DL2's rejected option; the residual is already
  stated in DL2.
- **F1** — the L-V8 contingency is already in DL2 and in E2's scope.
- **F4** — E5's Kimi-dependent half is already gated on E4, and any re-decision goes to the owner (§6).
- **F5** — "his own `kimi` closed" is already a prerequisite (§5, §11).
- **F7** — the extract was deliberate (its size); the Kimi epics get their own round.
- **F12** — the owner's standing command is to proceed on the safe defaults and list the questions at the end; the defaults only
  ever refuse more.

Also owed from the gate's commands: the cadence consultation for E1–E3 before their code round (§6).

### E1 — Linux probes, and Vibe's Linux cells recorded · M · ordinary (measurement) — built on Opus · depends on nothing

- **Goal.** The probe scripts run on Linux; the scout's Vibe findings become RESULTS with predictions; L-V6, L-V7, L-V8 recorded;
  the `linux` manifest entry written by the script.
- **Scope.** `scripts/lib/{vibe,kimi,kimi-home}.mjs`, `probe-vibe-confinement.mjs`, `probe-kimi-confinement.mjs`,
  `probe-kimi-tree-hold.mjs`, `vibe-home-manifest.mjs` (§5's lines); their tests; `shared/vibe-home-manifest.json`;
  `research/RESULTS_vibe_confinement.md` (a Linux section). No product C#.
- **Done when.** The ported scripts pass their tests on both OSes; the three cells recorded; L-V6b's vectors committed (lines
  AND the three whole-file multi-line shapes, each with python-dotenv's names and coai's expected verdict) — DL2's grammar contract
  is settled here, before E2 starts; the manifest's `linux` entry committed
  (or, if L-V7 disagrees, its absence recorded with both hash sets).

### E2 — Vibe on a Linux host · M · **authentication / security** (the key-file link) — built on Fable · depends on E1

- **Goal.** §2 item 1 in the server.
- **Scope.** DL1 for Vibe (`VibeLaunch.cs:77-82`; `VibeRuntime.cs:80-85`); DL2 (the link maker, the ported descriptor-based reader
  — `RegularFiles`' Linux leg and `FileStatus` into `src_mcp/runners/Files/` with their tests — the names grammar and its two
  refusals, `VibeHome.Judge`, the deleter tests, `VendorDiagnosis.cs:109-112`'s per-host cure); DL3 (`VibeHomeManifest`,
  `VibeLaunch.Verdict`/`VersionWarning`, the `.session-index.json` name); DL5's Vibe half; L-V8's outcome (a `.env` snapshot
  exclusion if needed); `research/module_runners.md`.
- **Done when.** S-V1–S-V17 green as MTP executables on Windows and Linux CI; every D18 pin for Vibe flipped red-first; `HostKind.Wsl`
  admitted and `HostKind.Linux` refused with the WSL2-only sentence; the scenario harness runs a plan round, a code round, a consult
  and a question row with a Vibe row on the Linux CI job (§8), each flow and its limits written into `research/module_tests.md`.
- **Cornerstone AREA A1 (confirmed by Fable, 2026-10-16).** Credential admission: the one admitted link — see *The cornerstone
  areas* above for its boundary, minimum scenarios and review focus. One story of this epic is marked when its stories document is
  written.

### E3 — Vibe on Linux in the extension, and the 0.58.0 release · S · ordinary — built on Opus · depends on E2, and on L-V8 recorded

- **Goal.** The panel says what the WSL side does, and the release ships.
- **Scope.** `vendors.ts:294`; `vendorTerminal.ts:478`, `:488-493`; the five help files; `runtimeSince.ts` (`VIBE_ON_LINUX_SINCE`);
  `shared/consultant-limitations.json:252-263` (the Vibe `linux` and `wsl` rows: measured on WSL2, the cells named);
  `runtime-capabilities.json` if its Vibe rows name a host; L-V9; `research/module_extension.md`.
- **Done when.** Extension tests green; L-V9 3 of 3; **L-V8 recorded in `research/RESULTS_vibe_confinement.md` before the tag**;
  the `linux` limitation rows say "measured on WSL2 only" and the `wsl` rows name the cells; the four harness flows of E2 still green
  on the release commit; the live WSL checks of §8 answered through the released binary.

### E4 — Kimi measured on WSL · M · ordinary (measurement) — built on Opus, **runs on the owner's live, rotating sign-in** · depends on E1 (its Kimi probes); may run beside E2–E3; gates E5's Kimi-dependent half

- **Goal.** L-K1, L-K2, L-K3, L-K4, L-K6, L-K7 recorded, each 3 of 3 where it is a confinement claim.
- **Scope.** The ported Kimi probes; `research/RESULTS_kimi_confinement.md` (a Linux section). No product code.
- **Done when.** Every cell recorded with its prediction; DL6, DL8, DL9, DL10, DL11 confirmed or re-decided in this plan (a
  re-decision goes to the owner before E5's Kimi-dependent half merges). This is a decision gate: E5's done-when names it.

### E5 — Kimi's Linux mechanisms · L · **authentication / security** (the sign-in link, the refresher lock, the verified teardown) — built on Fable · depends on E1 (the Kimi-independent half) and E4 (the Kimi-dependent half, §6 item 5)

- **Goal.** Every Windows-only mechanism has its Linux counterpart, proven — and a survivor changes what the next launch may do.
- **Scope.** DL6 (`Junctions`' Unix shell; the locality rule by mount type over the ported `MountTable`); DL7
  (`KimiCredentialsLock.KeyFor`'s identity from the ported `FileStatus`, the `statx` import); DL8 (L-N1, and the native take if
  needed); DL9 (`AskAcpMode`'s subreaper and `SIGTERM`/`SIGINT` registration, `AcpTransport.EndAsync`'s re-listing bounded descendant
  end through the ported pidfd layer, the survivor marker, `ReaderReconcile` moved to `src_mcp/runners/Processes` and asked by
  `KimiLaunch.TakeAsync` and `KimiHome.Alive`); DL10 (the extracted `/proc` reader, `HostProcessList` on Linux answering a closed
  union).
- **Done when.** S-K1–S-K7 and S-K11–S-K16 green on the Linux CI job without the stand-in; L-N1, L-N2 (all arms) and L-N3 recorded;
  the `KimiCredentialsLockTests` skips lifted on Linux; **and E4's gate met** — L-K2, L-K4, L-K6 recorded and DL6/DL8/DL9/DL10
  confirmed in this plan — before the Kimi-dependent half merges.
- **Cornerstone AREAS A2 and A3 (confirmed by Fable, 2026-10-16, re-bounded).** A2 credential ownership for the tree's lifetime —
  the lock as one lifecycle, DL6 + DL7 + DL8 + DL10; A3 the verified teardown and the survivor's effect on eligibility — DL9 whole.
  Boundaries, minimum scenarios and review focus under *The cornerstone areas* above. Two stories of this epic are marked when its
  stories document is written; the A2∩A3 seam is named in both specs.

### E6 — Kimi on a Linux host, and its release · M · ordinary — built on Opus; DL12's `OFFICIAL_SOURCES` verbs and the `sudo` rule are a security (supply-chain) judgement on Fable, outside the quota · depends on E5 (DL9's survivor rule included)

- **Goal.** §2 item 2 end to end, and item 3 for Kimi.
- **Scope.** DL1 for Kimi (`KimiLaunch.cs:28-33`); DL5's Kimi half (`KimiAcpProfile.cs:135-140`, `KimiPlaces.cs:74`, `KimiTools`);
  DL11 (`KimiEnvironment`); DL12 (`vendorTerminal.ts:528-532`, `:568-572`, `officialSources.ts` — the two verbs, `distroPackages`,
  and the `sudo` first-word rule — `KimiTools.cs:24-27`, `:51-61`); `vendors.ts:310`, the help, `KIMI_ON_LINUX_SINCE`;
  `research/module_runners.md`, `module_extension.md`.
- **Done when.** S-K8–S-K10 green (S-K10's `sudo rm -rf` observed red against the old parser first); the install button run in WSL
  on a machine (or a `testsToDelete-` prefix) without `rg`; the live Kimi checks of §8 through the release; this plan promoted.
