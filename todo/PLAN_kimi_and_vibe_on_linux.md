# PLAN — Kimi Code and Mistral Vibe on Linux and WSL

> Status: **E1–E3 released in 0.59.0, E4–E5 built, E6 built and in review for 0.61.0 — 2026-10-18.** (Earlier: E1 done 2026-10-16 — the probes run on Linux; L-V6b, L-V7, L-V8 recorded; E4 measured 2026-10-16.) Scope: lifting D18 of
> [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) for a coai-mcp that runs ON a Linux host (Remote-WSL first) —
> Vibe first, because it is measured; Kimi after its WSL cells. Touches `src_mcp/runners/{Vibe,Kimi,Files,Processes,Platform}`,
> `src_mcp/src/Acp`, `src_mcp/src/Program.cs` (the `--kimi-clear-marker` one-shot mode), `src_server/src/Slots`
> (`ReaderReconcile` moves out of it, DL9 — the plan gate for epics 4–6, 2026-10-16, F9), `shared/vibe-home-manifest.json`, `shared/consultant-limitations.json`, `scripts/` (the probes),
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
   the tree hold, the teardown that leaves no grandchild, the wait for the person's own `kimi`. One limit, stated (cadence consult
   8df043b8): if the shim itself is `SIGKILL`ed, a descendant born after its last listing is not in the marker and may outlive the
   turn — accepted because only `kimi` refreshes and an orphan `kimi` is caught by DL10's wait; a surviving non-`kimi` grandchild is
   a leak, not a credential risk.
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
| L-V6b, L-V7, L-V8 (Vibe 2.26.1, E1) | 48 python-dotenv vectors from Vibe's own environment, Windows and WSL agreeing; the admission implication holds on all 48 (DL2's corrections are in DL2). The `linux` manifest entry written by the script: two fresh runs agree on all 12 hashes, 11 differ from `win32` — committed STAGED (DL3). Vibe does NOT load a `.env` from its working folder, its `--workdir` or the parent (DL2). L-V6 not run: E1 never reads the owner's `.env`. | **measured 2026-10-16**, 2 paid tiny turns — RESULTS_vibe_confinement §30 |
| L-K1 live, L-K2, L-K3, L-K4, L-K6, L-K7 (Kimi 2.2.0) | Signed in 8 of 8 through the symlink; one refresh landed in the person's folder (tmp + rename, mode kept, link kept); no `bin/` in the owned home; `os/` gains Kimi's native cache (→ DL11); `rg` in its own session survives a pid or group kill briefly (→ DL9); `comm` of `kimi acp` is `MainThread` (→ DL10); 3 of 3 write arms refused; `session/load` works, the model in `configOptions` (already what the shipped parser reads). | **measured 2026-10-16**, 8 paid turns — RESULTS_kimi_confinement §29 |

The owner's OAuth slot on that machine is `oauth/kimi-code-env-<hex>`, not the default — the D19 rule (the slot READ from the person's
`config.toml`) already covers it. The scout found the owner's TUI by `comm` = `kimi` (`ps -eo comm=`) — enough for a TUI, not for
an ACP agent, whose `comm` is `MainThread` (L-K6); §5's guard matches the executable too.

## 4. Decisions

Numbered DL1–DL14 so they never collide with the parent plan's D-numbers. Every reference was opened on `03ec1c1a`.

- **DL1 — Scope: a Linux HOST, not a Linux CHILD of Windows.** Admitted: a `coai-mcp` whose own host is `HostKind.Linux` or
  `HostKind.Wsl` (`HostKind.cs:30-31`, `:50-56`). Still refused: the second arm of both host refusals, a Windows host starting
  `wsl.exe` or a `\\wsl$` path (`HostKinds.RunsInWsl`, `HostKind.cs:39-47`) — a Windows process cannot take a Linux lock (`HeldFile`
  states that no lock crosses the 9P boundary, `src_mcp/runners/Files/HeldFile.cs:17-22`), cannot list or end a Linux process tree,
  and the Windows→WSL bridge is another plan's. macOS stays refused (nothing measured there). **Native Linux is refused for now —
  decided by the plan gate (F10), which closes Q1.** The code is the same, and WSL2 is a real Linux kernel, but every cell is WSL2's,
  and a desktop Linux differs where it matters (a keyring Vibe may use instead of `.env`, other filesystems, other distributions'
  packaging). So 0.59.0 admits `HostKind.Wsl` ONLY; `HostKind.Linux` keeps a refusal whose sentence is new: *"Vibe runs on WSL2 only
  for now — it is measured on WSL2 and not yet on a native Linux host (DL1 of todo/PLAN_kimi_and_vibe_on_linux.md)"*. It lifts when
  at least one native-Linux run of L-V9 is recorded (§5, L-V10). The `linux` limitation rows say exactly that; the `wsl` rows name the
  WSL2 cells. The host refusal becomes per runtime and per stage: Vibe admits WSL from E2, Kimi from E6 (WSL only, by the same rule — the same
  `NamesWsl2` predicate below, so a WSL1 banner is refused for a Kimi row too: the plan gate for epics 4–6, 2026-10-16, F0; S-K20).
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
    refusing. **A FIFO is a refusal Vibe itself would not make** (A1 spec, 2026-10-16): Vibe 2.26.1 reads a FIFO sign-in on
    purpose (`vibe/core/config/vibe_schema.py:104-106`, for 1Password-style environment files), and coai still refuses it — it
    never blocks, and it links only a regular file it can judge — so the sentence says exactly that: Vibe could read this file,
    coai will not link it. **Ported, not rewritten**: `RegularFiles.ReadOwned` and its Linux leg (`ReadLinuxOwned` → `ReadOpened` → `Judged` →
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
    reads them. Until E1 merges, that file is not at HEAD, so S-V13 reads a test-local copy of the same vectors, and on any
    disagreement between the copy and the shared file python-dotenv's output wins and the copy is corrected (A1 spec, 2026-10-16).
    **Four refusal kinds, never one** (A1 spec, 2026-10-16 — the gate text said three, the DoD two), each refusing by LINE NUMBER
    only: `NoBinding` and `OtherName` below; `NotText` — invalid UTF-8 or a NUL byte on that line (python-dotenv cannot decode it
    either, and Vibe would not start — L-V6b, 2026-10-16: true of invalid UTF-8; a NUL python-dotenv DOES read and bind, and it is
    Vibe's environment that cannot take the value, so the refusal stands with Vibe's reason); `ByteOrderMark` — line 1 starts with `EF BB BF`, which python-dotenv's `utf-8` codec (not
    `utf-8-sig`) keeps as part of the first NAME, so Vibe never finds `MISTRAL_API_KEY` and the sentence says to save the file
    without one. In detail:
    - a line that does not bind a name — a bare word with no `=` (python-dotenv keeps it with NO value and sets nothing from it, and
      it is how a key pasted on its own line looks), a line starting with `=`, a line python-dotenv itself cannot parse IN ITS NAME PART — refuses
      (L-V6b, 2026-10-16: a VALUE python-dotenv cannot parse — `KEY="v" junk`, an unterminated quote — is ADMITTED by a grammar
      that never parses values, and python-dotenv binds nothing there, so Vibe meets L-V1's wording; sound) — refuses
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
    - **what a "line" is** (cadence consult a3933362, turn 2, verified): python-dotenv's line break is `\n`, `\r\n` OR a bare `\r`
      (`_newline` in `dotenv/parser.py`) — `MISTRAL_API_KEY=x` + bare CR + `VIBE_X=1` binds `VIBE_X`. coai's reader splits on exactly
      those three and nothing else (a vertical tab, form feed or NEL binds no second name — measured, and splitting on them would
      only refuse more). A reader that splits on `\n` alone would admit that file: this is the one place the soundness argument
      above depends on the implementation, and S-V13 pins it;
    - the cure for both (owner question Q2): keep only `MISTRAL_API_KEY=…` in that file.

    A test pins that no sentence, log line or exception message holds a byte of any value;
  - **who makes it**: the launch control, inside the home's turn, after the shape check and before `--version`
    (`VibeLaunch.TakeAsync`, `VibeLaunch.cs:146-172`), on a non-Windows host only — so the reviewer, retry, consult, question row
    and consultant check get it from one place. **Not the `providers` probe** (A1 spec, 2026-10-16): `VibeRuntime.ProbeVersionAsync`
    (`src_mcp/runners/Vibe/VibeRuntime.cs:118-130`) runs `--version` straight through `launcher.RunAsync` on a temp home of its own
    and never passes `TakeAsync`; `vibe --version` needs no key, so the probe makes no link and needs none;
  - **what is admitted**: in `VibeHome.Judge` (`VibeHome.cs:94-103`) a link named `.env` at the home's top level, on a non-Windows
    host, whose ONE-level RAW target — `FileInfo.LinkTarget` as the link stores it, compared ordinally — equals the expected absolute
    path. **Not `Junctions.Target`** (A1 spec, 2026-10-16): it normalises through `GetFullPath` (`src_mcp/runners/Files/Junctions.cs:128`,
    `:301-303`), so a RELATIVE spelling that resolves to the same file would pass, and S-V3 refuses a relative target; the maker
    writes the absolute spelling and the judge demands exactly it. Any other link, a `.env` that is a file (already refused,
    `src_mcp/tests/VibeHomeTests.cs:206`), a `.env` on Windows, a `.env` one level down — refused as today;
  - **no `.env`** at the person's home: no link is made and Vibe runs; it exits 1 with L-V1's wording, free, and
    `VendorDiagnosis.cs:109-112` names it — its cure text becomes per host, which needs a `Classify(string, HostKind)` overload:
    today's `Classify(string text, bool? wsl = null)` (`VendorDiagnosis.cs:173`) knows only WSL-or-not, and its sentence ("kept in
    the operating system's keyring") is the Windows truth only; the old signature routes through the new one (A1 spec,
    2026-10-16). Linux and WSL: *run `vibe --setup` in a terminal on this
    side; the sign-in is `~/.vibe/.env`, which coai links into its own homes and never copies*). **The sentence names the keyring
    case** (the plan gate, F10): *a sign-in kept in a desktop keyring is invisible to coai's launches — their built environment
    carries no D-Bus session (`ProcessEnvironment.cs:90-91`) — so coai needs `~/.vibe/.env`*. No pre-check: the run is free and its
    wording is measured;
  - **the link cannot be made** (the plan gate, F3) — a filesystem without symbolic links, `~/.vibe` missing, permission denied:
    nothing is left at `$VIBE_HOME/.env` (a half-made link is removed by its own name), and the launch falls through to the "no
    `.env`" path above: Vibe runs and exits 1 with L-V1's wording, and the person is told BOTH the setup command and the reason the
    link could not be made (the system's message, which names a path and never the file's content). Never a copy as a fallback.
    **How the reason travels — a recorded deviation from F3's letter** (A1 spec, 2026-10-16): the cure is
    `VendorDiagnosis.For(stdErrTail)` inside `Because` (`src_mcp/runners/Reviewers/BoundedScheduler.cs:802`), keyed on the vendor's
    text with no access to the launch control, and threading a note into it would touch `ReviewerLaunch`/`ReviewerOutcome`/`Because`.
    So the reason rides as the launch's WARNING (`LaunchTurn.Warning`, `src_mcp/runners/Reviewers/LaunchTurn.cs:64` →
    `ReviewerOutcome.Warning`, carried however the reviewer ended, `src_mcp/runners/Reviewers/ReviewerExecutor.cs:586-601`), on the
    same reviewer line and card as the cure;
  - **deletion**: `GitScratch.Unlink` already removes a FILE link by its own name and checks the target after
    (`src_mcp/runners/Files/GitScratch.cs:55-71`, `:116-133`), and `ConsultantHomeSweep.DeleteAllBut`
    (`src_mcp/src/Server/Consultation/ConsultantHomeSweep.cs:261-277`) routes links to its delete seam. E2 adds the Linux test that
    every deleter (round home, one-shot sweep `VibeHomes.SweepOneShot`, the D20 consult sweep, the test sweeps) leaves the target's
    bytes, mode and mtime as they were. `Writable` (`GitScratch.cs:85-92`) is never asked of a link — the test pins that a `chmod`
    never reaches the target. **That test is GREEN first, not red** (A1 spec, 2026-10-16): today's deleters already unlink by name,
    so there is no product defect to observe. Its teeth are proven the testing rule's way for a fix that already landed — a MUTANT
    deleter in the test (every entry through `File.SetAttributes` then `File.Delete`, no link check) turns the `0400`-target arm red
    with the real symptom, because a Unix `SetAttributes` through the link `chmod`s the TARGET; both observations go in the summary.

  Why linking the key file into a coai-owned home is acceptable: the home is `0700` (`PrivateFolder`'s Unix leg — created `0700` at
  `src_mcp/runners/Files/PrivateFolder.cs:127`, judged at `:160-183`), so no other user can follow the link; the link is outside the snapshot Vibe reads,
  and a read outside the cwd is denied (L-V2) — a prompt-injected branch asking `read_file` for `$VIBE_HOME/.env` meets the same
  refusal as `~/.ssh`; on Windows the same key sits in the Credential Manager, which every Vibe launch reads anyway (RESULTS_vibe §12) — the
  exposure is the same, only its place differs. **One unknown, and it is a RELEASE GATE (L-V8):** whether Vibe also loads a `.env`
  from its WORKING folder — the reviewed branch's own `.env` in the snapshot. If it does, `.env` joins Vibe's snapshot exclusions
  on every host (the reviewed branch would otherwise set `VIBE_*` through it). **Code reading predicts "no"** (A1 spec, 2026-10-16;
  re-checked in Vibe 2.26.1's installed package, python-dotenv 1.2.2): every `load_dotenv_values()` call site (`vibe/cli/cli.py:576`,
  `vibe/cli/entrypoint.py:436`, `vibe/app_server/stdio.py:126`, `vibe/acp/entrypoint.py:76`, `vibe/acp/auth.py:159`) reads the default
  `GLOBAL_ENV_FILE`, which is `VIBE_HOME/.env` (`vibe/core/paths/_vibe_home.py:14`), and `find_dotenv` is never called. The cell
  still runs (E1); a prediction is not a measurement. **Recorded 2026-10-16 (E1, RESULTS_vibe_confinement §30.8): NO** — a bogus
  key and a `VIBE_SESSION_LOGGING__SAVE_DIR` canary in the working folder (3 of 3), in the `--workdir` folder with another cwd, and
  in the working folder's parent all ended in L-V1's *Missing* wording with no canary folder, while the same file as
  `$VIBE_HOME/.env` (the control) gave *Invalid API key* and made the canary. So `.env` does not join the snapshot exclusions for
  2.26.1, and E3's release gate on L-V8 is met. The link is a configuration BOUNDARY, not only a
  filesystem exception: `VibeHome` exists because hooks, MCP servers and agents in a home change what runs (`VibeHome.cs:12-24`), and
  the home is judged before every launch, the version probe included and again after it (`AgainstAsync`, `VibeLaunch.cs:201-211`;
  `AfterTheVersion`, `:214-224`).
  A second dotenv the reviewed repository can supply would be that boundary with a hole in it — so 0.59.0 is not tagged until L-V8 is
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
  WSL (E1), never by hand. **Written 2026-10-16 (L-V7) and STAGED** (E1's deviation): it sits under a top-level `staged` key the
  shipped C# does not read, because `VibeLaunch.Verdict` matches exactly one entry (`[var entry]`, `VibeLaunch.cs:306-310`) and a
  second 2.26.1 in `versions` turns a WINDOWS 2.26.1 launch into an unmeasured one — measured: 4 `*Vibe*` tests red, a tampered
  Windows plugin file admitted among them (RESULTS_vibe_confinement §30.6). E2 moves it with `node scripts/vibe-home-manifest.mjs
  --unstage linux` in the same change that makes `VibeHomeEntry` read `platform`. **If two fresh-home runs disagree on the hashes (L-V7)**, there is no `linux` entry and Linux is always
  warn-and-run; the plan records it as the deviation. **Rollback:** going back to 0.58.x restores today's behaviour exactly — it
  reads only `version` and the pins, so on Windows the same entry is enforced, and on WSL D18 refuses before any home is judged. A
  WSL home 0.59.0 left behind (with its `.env` link) is never launched on by 0.58.x, and its sweeps unlink a file link by its own
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
    `ToolFolders` (`KimiPlaces.cs:24-27`). **Why the third place is load-bearing** (the plan gate for epics 4–6, 2026-10-16, F10): the
    built `PATH` is `[person's bin, ..ToolFolders]` and then the parent's `PATH` (`KimiEnvironment.Built`,
    `src_mcp/runners/Kimi/KimiEnvironment.cs:65`; `BuiltPath`, `:74-80`), and `FoldersFor` looks only in the person's bin and that
    parent `PATH` (`KimiTools.Directories`, `KimiTools.cs:84-87`). The VS Code server's `PATH` often lacks `~/.local/bin` (above), and
    E4's built `PATH=~/.kimi-code/bin:/usr/bin:/bin` (RESULTS_kimi_confinement §29, "How it ran") is the narrow case: there a
    `~/.local/bin/fd` is found by nobody and joins nothing, and Kimi downloads into the owned home. So `KimiTools` records the
    DIRECTORY where it found each of `rg` and `fd` — the existing `FoldersFor` answer — with `~/.local/bin` in its search, and that
    directory joins the built `PATH` as a `ToolFolders` entry.

- **DL6 — Kimi's credentials link on Linux is a directory symlink.** *Security / authentication.* `Junctions.Refusal` refuses
  first with `WindowsOnly` (`Junctions.cs:60`, `:141-143`). On Linux the same `Make`/`Refusal`/read-back runs over a product Unix
  shell of `IJunctionShell` (`src_mcp/runners/Files/JunctionShell.cs:9-22`) — the shape the test stand-in already has
  (`src_mcp/tests/WindowsStandIn.cs:52-65`), promoted: `Directory.CreateSymbolicLink` at a path that does NOT exist (no
  delete-then-create window, unlike the stand-in's `:60-61`), read back by `Junctions.Target`. **The window is in the shared code
  too** (A2 spec, 2026-10-16): `Junctions.Reparsed` (`Junctions.cs:210-224`) creates the empty folder at the link BEFORE it asks the
  shell, whose verb turns an EMPTY folder into a link (`SetReparsePoint`, `JunctionShell.cs:17-21`) — which is why the stand-in
  deletes it first. So the verb becomes `MakeLink(link, target)`, made at a path that does not exist; the `Directory.CreateDirectory`
  moves into `JunctionsNative.MakeLink` (Windows: the folder, then the reparse point); the Unix shell is one `symlink(2)`, whose
  `EEXIST` is a refusal; the stand-in drops its `Directory.Delete`. The locality rule
  `IsLocalUnixPath` (`Junctions.cs:97-101`) is a PREFIX list (`/net/`, `/misc/`, `/Network/`), and a `/mnt/<letter>/` prefix added to it
  would be weaker than the guarantee it stands for (consultation c6e42fd3, verified): a sign-in folder on a Windows filesystem is
  shared with a Windows Kimi that no Linux lock can see (`HeldFile.cs:17-22`) — two refreshers of one rotating token — and a Windows
  filesystem reaches Linux through a custom `mount -t drvfs` point, a bind mount or `virtiofs` as well as through `/mnt/c`. So the
  rule is the FILESYSTEM that holds the real path, read from `/proc/self/mountinfo`: the deepest mount point at or above the path,
  refused when its type is `9p` with `aname=drvfs`, `drvfs` or `virtiofs`. **Ported**: `MountTable.Parse` / `Holding` and
  `MountEntry.IsDrvfs` (wsl_care's `src_daemon/src/WslCare.Core/Files/MountTable.cs:18`, `:29`, `:34` — 108 lines, a pure
  parser, the kernel's octal escapes decoded and the super-options kept raw as the kernel prints them). **wsl_care has no
  `MountTableTests`** (A2 spec, 2026-10-16; checked: only `ArchitectureTests.Numbers.cs:287` names the file, and the parser is
  exercised only indirectly, through `BaseFolderRulesTests` and `WindowsSystemDriveTests`), so its tests are written fresh in
  `src_mcp/tests/MountTableTests.cs`, over kernel-format lines: an ext4 `/`, a `9p` line with `aname=drvfs` at `/mnt/c`, a `drvfs`
  line at `/data/win`, a `virtiofs` line, an octal-escaped mount point, a super-option NOT decoded, stacked mounts (the last line
  wins), a short line skipped, a table without `/`. The sentence
  still names `/mnt/<letter>/` as the example; the rule is the mount type. On a native Linux without drvfs the rule is inert (Q1).
  **Network filesystems — decided by the owner 2026-10-19 (Q7, option A; first taken as a stated assumption 2026-10-17)** (E5's Fable security judgement,
  finding 3; question consultant 3607576…, which split: one row "warn and admit", the other "refuse with a cure"). The DL6 argument
  holds word for word for a sign-in folder on `nfs`/`nfs4`/`cifs`/`smb3`/`fuse.sshfs`/`ceph`/`glusterfs`: a `kimi` on ANOTHER
  machine sharing that home refreshes the same rotating token, and no lock on this machine can see it. Windows already refuses
  network and roaming locations for the same folder (D19). The repository's own precedent for the same choice is CredsForDevs'
  `DataDirCheck` (refuse a network data folder by default, with an explicit override). So, in E6: `MountEntry.IsNetwork` (the
  types above) refused with its own sentence — *"your Kimi sign-in folder is on network storage (<type>); a Kimi on another machine
  that shares this home can refresh the same sign-in and sign you out, and coai cannot see it. Point `KIMI_CODE_HOME` at a folder on
  this machine's own disk (not under the network home) and sign in there once with `kimi login`, or set
  `coai.kimiAllowNetworkSignIn` if you never run Kimi on another machine"* — and an explicit, default-off override setting that
  turns the refusal into a one-line warning on the card. The cure is a SEPARATE local sign-in, never a copy of the rotating token
  (D19). The owner's own WSL2 home is ext4 and unaffected either way.
  It is judged on the real path (`Resolved`, `Junctions.cs:179`). coai never `chmod`s the target (Kimi keeps it `0700`, RESULTS_kimi
  §11). **Confirmed by L-K2 (RESULTS_kimi_confinement §29):** signed in 8 of 8 through the symlink; the one refresh wrote
  `<slot>.json.tmp.<pid>.<rand>` INSIDE the person's folder and renamed it over the slot (new inode, same name, size and mode `600`, no
  leftover); the link kept its inode, the folder kept `700`, the rest of `~/.kimi-code` was untouched. One consequence: the folder
  must stay writable by its owner (Kimi's temp-and-rename needs it), and a `*.tmp.*` name there is Kimi's, never coai's to judge.

- **DL7 — The Linux lock key is the folder's identity: `(st_dev, st_ino)` from `statx`.** *Security / authentication.*
  `KimiCredentialsLock.KeyFor` (`src_mcp/runners/Kimi/KimiCredentialsLock.cs:116-120`) hashes `HandleRead.FinalDirectory`, which is
  `""` off Windows (`src_mcp/runners/Files/HandleRead.cs:226-238`), so the key is empty and the lock `Unusable` — fail-closed by
  design (E4.2 decision 9; the remark at `KimiCredentialsLock.cs:53-56`). A Linux PATH is a spelling — two bind mounts or two
  symlinked spellings give two keys, two refreshers, a signed-out person; the kernel path from `/proc/self/fd`
  (`HandleRead.cs:244-265`) is the same spelling problem. Device and inode name the folder itself. So `KeyFor` asks a per-platform
  IDENTITY: Windows keeps the upper-cased final path; Linux hashes `dev_major:dev_minor:ino` from `statx(AT_FDCWD, path, 0,
  STATX_TYPE | STATX_INO)`, and only when that status says a DIRECTORY (A2 spec, 2026-10-16: with `STATX_INO` alone a FILE at the
  path would get a key — `Junctions.Missing` refuses a file on the junction maker's path only, not on the shim's
  `KeyFor(Junctions.Target(link))`, `KimiShimHold.cs:38-47`; a file is an empty key, so `Unusable`) — `statx`'s struct is the same
  on every Linux architecture — through a `LibraryImport("libc")`, the precedent
  `src_mcp/service_defaults/AppendOnlyFile.cs:374-383`. The record and the read are **ported** with DL2's file reader:
  `FileStatus` and `SameFileAs` (one device, one inode — `RegularFiles.cs:12-21` in wsl_care) and `Status`'s offsets (`:240`); the
  key is that record's three numbers, nothing of the path. A failed `statx` is an empty key: still `Unusable`, never the spelling.
  The alias set a test must cover (consultation c6e42fd3): a symlinked parent, a bind mount of the same folder, `/proc/self/cwd`-style
  spellings — one key each; a deleted folder, or a file at the path — no key.
  `HandleRead.FinalDirectory` stays as it is for the key's Windows arm; `Junctions`' locality check gets the Linux real path
  (`WorktreePaths.Real`, which `CanonicalDirectory` already falls back to, `HandleRead.cs:211-212`). Its remark at
  `HandleRead.cs:217-218` ("D18 never reaches a non-Windows host here") goes stale with this change and is rewritten: a Linux
  folder is named by its identity (the key's Linux arm), not by this call (A2 spec, 2026-10-16). Needs no Kimi cell.
  **The lock files are retired, not kept for ever** (the plan gate, F15). An identity key changes whenever the sign-in folder is
  made again (a new inode), so on Linux the files can accumulate where on Windows they could not. Assumed maximum: **8 sign-in
  identities per machine** in a lock root's life (one `~/.kimi-code/credentials`, a few re-creations, a second `KIMI_CODE_HOME` at
  most) — 16 empty files. Retirement: the gate file records, once, under its own hold, the identity it was keyed by and the folder
  path it came from (`dev:ino` and the path — no secret; the folder is the person's own). The sweep that already runs on every beat
  and at server start (`ConsultationSweeper`, `src_mcp/src/Server/Consultation/ConsultationSweeper.cs`, D20's host) removes a key's
  two files when it can take BOTH exclusively at once without waiting (nobody holds them) AND the recorded folder no longer exists
  or no longer has that identity; it unlinks them while it still holds them. **The sweep is Linux only** (A2 spec, 2026-10-16):
  on Windows a held file cannot be unlinked — the hold is a `FileShare.None` stream with no `FileShare.Delete` (`HeldFile.cs:51`),
  so `File.Delete` is a sharing violation — and nothing accumulates there, because the Windows key is the upper-cased final PATH,
  which a re-made folder keeps; off Linux the sweep returns at once. Because removing a lock file under a flock is the
  classic two-holders race, every taker verifies after its take that the path still names the file it locked (`fstat` of its
  descriptor against `statx` of the path; a mismatch releases and takes again). A file the sweep cannot read or decide about is
  left, logged, never the reason the sweep fails (D20's rule).

- **DL8 — The tree hold on Linux is the same inheritable file, IF .NET takes it exclusively and lets it be inherited.**
  *Security / authentication.* `HeldFile.Take(path, inheritable: true)` opens with `FileShare.None | FileShare.Inheritable`
  (`HeldFile.cs:47-51`); on Unix .NET turns a share mode into an advisory `flock`. Two facts are unproven on Linux, because every
  test that would take it skips there (`src_mcp/tests/KimiCredentialsLockTests.cs:247`, `:296`, `:314`): that the combined share
  mode still asks for an EXCLUSIVE lock (a shared one would let two refreshers in), and that the descriptor reaches the child (no
  close-on-exec). That is cell **L-N1**, a test, not a Kimi run. If either fails, the Linux take is a native one: `open` without
  `O_CLOEXEC` and `flock(LOCK_EX | LOCK_NB)` through `libc` (the `AppendOnlyFile` precedent), behind the same `Hold` union.
  **The prediction, written before the run** (A2 spec, 2026-10-16 — Fable's reading of .NET's `SafeFileHandle.Unix.cs`, not
  re-checked against the source in this plan): .NET asks for `LOCK_EX` only when the share mode EQUALS `FileShare.None`, so
  `FileShare.None | FileShare.Inheritable` takes a SHARED `flock` — the first fact fails, and two shims would both hold the tree —
  while the second holds (`O_CLOEXEC` is added only when `Inheritable` is absent). So the native take (`LinuxHeldFile`, for
  `inheritable: true` on Linux only; every other take stays .NET's) is the EXPECTED outcome — and it ships only once L-N1's red run
  against today's `HeldFile.Take` CONFIRMS it, the red text and the green run both recorded in RESULTS_kimi_confinement (a new
  L-N1 section). If the run is green, `HeldFile` does not change and the test pins .NET's behaviour. What the
  hold covers on Linux (L-K5): the shim and `kimi`, the direct child — not a grandchild that `kimi` starts the ordinary way. That is
  enough for its purpose, one refresher at a time, IF the refresher is `kimi` itself: **L-K4** checks that no descendant of `kimi acp`
  is another `kimi`. If one is, `ForeignKimiWatch`'s orphan rule (a `kimi` whose parent ended counts as the person's,
  `src_mcp/runners/Kimi/ForeignKimi.cs:32-36`) is the only net, and the plan goes back to the owner before E5 ships.
  **L-K4 (RESULTS_kimi_confinement §29): in the measured tree (Grep turns; Kimi's sub-agents, background shell and MCP servers not
  run), no descendant of `kimi acp` was another `kimi`; only the agent held the inherited lock
  descriptor** (`rg` and a transient `env` child held nothing). The premise holds; L-N1 (.NET's share mode on Linux) is still owed.

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
  - *It reaps, and a zombie is never a survivor* (the plan gate for epics 4–6, 2026-10-16, F11 + F15). A subreaper inherits the
    orphans' EXIT too: an orphan that ends is a zombie of the shim until the shim waits for it, and `/proc` keeps its entry, start
    time included. Two rules. (1) The shim REAPS, **per pid, never `waitpid(-1)`** (cadence consult 8df043b8, epics 4–6, verified
    against .NET 10's `ProcessWaitState.Unix.cs`): .NET reaps the children it started through its own SIGCHLD handling, and a
    `waitpid(-1)` that takes the status of a child the runtime still tracks can end in the runtime's `FailFast`, not only in a wrong
    exit code — and "the shim starts nothing but the agent" is a fact about today's code, not a guarantee. So every step of the
    confirmation loop reaps exactly the descendants it LISTED in state `Z` whose pid is not one the shim STARTED (the launcher keeps
    the set of pids it started through `Process.Start` — today only the agent — and the drain excludes all of them, so a future probe
    child stays .NET's to reap; the L-N2 test starts a second managed child that exits with 17 during the drain and asserts its
    managed wait still sees 17) (`waitid(P_PID, pid, …,
    WEXITED | WNOHANG)` each): a reparented orphan is never a `Process` object, and a pid coai did not list is never touched. The
    L-N2 test asserts the agent's own exit code survives the drain. (2) `ProcessTree.Descendants`, when used for the confirmation, drops an entry that has exited — the rule `IsAlive` already
    applies (`Exited` / `ExitedEntry`, `src_mcp/runners/Processes/ProcessTree.cs:202-211`: state `Z` or `X` with no thread but the
    leader), reused, not a second rule. `Descendants` itself (`ProcessTree.cs:59-67`) lists zombies today, because `LinuxParents`
    reads only the parent. A grandchild that exits by itself before the teardown is therefore neither listed nor recorded as a
    survivor, and leaves no marker behind.
  - *It is bounded.* `ProcessLauncher.EndTreeFirstAsync` (`ProcessLauncher.cs:645-650`) waits for the agent's exit with
    `CancellationToken.None`, on purpose (`:628-630`: a cancelled wait returns before the handle is free). The agent's wait stays
    as it is — SIGKILL is not ignored — but the DESCENDANT confirmation has its own bound (`ConfirmationBound`,
    `ProcessTree.cs:39` — A3 spec, 2026-10-16: this cited `:39` after `ProcessLauncher.cs`), and a member
    still there at the bound is a SURVIVOR, never waited on for ever.
  - *It signals by identity, not by number.* `ProcessTree.Terminate` (`:124-144`) checks `IsAlive` and then `Process.Kill` — two
    steps, and a pid reused between them is somebody else's process. On Linux the end goes through a `pidfd`: `pidfd_open` pins
    the process, its start is compared with the identity, and the signal goes through the descriptor, so the number cannot be
    reused under the signal. **Ported in part**: the native layer and the pin → check → signal → poll order
    (wsl_care's `src_daemon/src/WslCare.Core/Processes/ProcessSignals.cs:192-213`, `LibcPidfdCalls` at `:281-332`) come
    over; the shell around it is REWRITTEN on coai's own `ProcessIdentity(Pid, StartedUtc)` and `OrphanSweep.Same` slack
    (`ProcessTree.cs:28-29`: one identity rule for every record here), not wsl_care's start ticks, `Tuning` or `IFileSystem`.
    Two details of the port (A3 spec, 2026-10-16): the import is spelled `"libc"` (coai's precedent, `AppendOnlyFile.cs:374-383`),
    not wsl_care's `"libc.so.6"` (`ProcessSignals.cs:284`); and wsl_care's `ProcessIdentity(Pid, long StartTicks)`
    (`ProcessSignals.cs:13`) is NOT ported — it clashes by name with coai's `ProcessIdentity(Pid, DateTime StartedUtc)`
    (`ProcessTree.cs:12`), which stays the one identity.
    **The missing-wrapper path, decided** (the plan gate for epics 4–6, 2026-10-16, F4): glibc's `pidfd_open` and
    `pidfd_send_signal` wrappers need glibc 2.36 (Ubuntu 24.04 has 2.39; Ubuntu 22.04 has 2.35, and has no wrappers). Where the
    wrapper symbol is missing, the layer calls the raw `syscall` with numbers **434** (`pidfd_open`) and **424**
    (`pidfd_send_signal`) — the same on x86-64 and arm64, present in every kernel from 5.3. Where the KERNEL has no pidfd (the raw call
    answers `ENOSYS`), the Linux Kimi launch is REFUSED before anything starts, with a stated sentence (*"this kernel has no pidfd
    (`pidfd_open` answered ENOSYS; Linux 5.3 or later is needed), so coai cannot end Kimi's process tree by identity and does not
    launch it"*) — never a silent fallback to a kill by number. The choice is behind a seam, so a test forces "no libc wrapper" (the
    raw path is the one used) and "ENOSYS" (refused with the sentence) on any kernel — S-K21.
  - *A survivor changes ELIGIBILITY, not only a report.* Today's text said "reported in the shim's structured report". That is
    not enough: the tree hold is disposed when `AcpClient.AskAsync` returns (`using (held.Hold)`, `AskAcpMode.cs:166-169`; the doc
    comment at `:127-131` says so — A3 spec, 2026-10-16), it is held only by processes that INHERITED the descriptor
    (`HeldFile.cs:23-28`), and L-K5 measured that a `close_fds`/`setsid` grandchild holds nothing — so a
    survivor can be alive, refreshing, and invisible to the next launch's gate. The policy is the one `ReaderMarker`/`ReaderReconcile`
    already applies to a vendor slot (`src_server/src/Slots/ReaderMarker.cs:65-68`, `:114-136`): the shim writes the members it
    listed (pid and start) to a marker under the lock root, keyed by the sign-in, BEFORE it signals; a clean confirmation clears it.
    **The marker is written in phases, as the precedent writes its own** (A3 spec, 2026-10-16: before `Process.Start` there is no
    pid, so "a marker written before the start names the agent" cannot be one write): `spawning`, naming no member, once the tree
    hold is held and before the agent starts — a failed write starts nothing; `running` naming the agent, right after
    `Process.Start` — what a `SIGKILL`ed shim leaves behind; `running` naming every listed member, before any signal (`ReaderPhases`,
    `ReaderMarker.cs:22-26`; the slot's two writes, `SlotRegistry.cs:270`, `:287-291`).
    The NEXT launch's gate (`KimiLaunch.TakeAsync`, after the gate file is held) reconciles the marker first — terminate by identity,
    confirm, else the turn is Busy with the survivor named — and `KimiHome`'s disposal (`KimiHome.Alive`, `KimiHome.cs:338-346`) keeps
    the home while a marker stands. **The shim's own tree hold is a THIRD place** (A2 spec, 2026-10-16): the gate is
    non-inheritable and dies with the server (`KimiCredentialsLock.cs:46-48`; held only while the launch is read,
    `ReviewerExecutor.cs:921`), so a server killed mid-turn frees it while its shim still runs, another server's `TakeAsync` passes
    BEFORE the first shim's teardown has written its marker, and that server's shim would start on a survivor. The shim's tree hold,
    taken only after the first shim let go, is the last point that can see that marker: `KimiShimHold.Taken` reconciles it after
    both tree files are held and before the agent starts, and a standing survivor gives the hold back and answers Busy (exit 69).
    A marker that names nothing it can match (a `spawning` marker: the shim died between that write and the start) blocks too,
    until a person clears it or the process it would have named is PROVABLY gone — made concrete (A3 spec, 2026-10-16): it clears
    only when DL10's listing is READABLE and shows no `kimi` of this uid (no foreign Kimi, and no entry named `kimi` at all — the
    orphaned `kimi acp` such a marker stands for has no `coai-mcp` in its chain, and exits within about 0.1 s of its stdin's EOF,
    L-K4); a `kimi` present keeps it Busy with the pids, an unreadable listing keeps it Busy with the reason. **A recorded deviation
    from the precedent**, which blocks a nameless marker for ever — the price it accepted (`ReaderMarker.cs:89-93`; Q6).
    `ReaderReconcile` MOVES from `src_server` to `src_mcp/runners/Processes` (reuse-first step 2.4: both projects need it and the server
    already references the runners), it is not copied. **It does not move unchanged** (cadence consult 8df043b8, verified):
    `ProcessTracking.StartedAt` (`src_mcp/runners/Processes/ProcessTracking.cs:159-172`) catches every exception and answers `null`
    — "no such process, or one this account may not look at" — and `ProcessTree.IsAlive` (`ProcessTree.cs:115-116`) reads `null` as
    not alive. That is the safe answer for a KILL (its comment says so) and the unsafe one for CLEARING a marker: the next launch's
    reconcile would drop an unreadable member that the clear mode below correctly refuses to drop. So both paths ask one tri-state
    question — `Liveness { Alive, Gone, Unknown }`: `Gone` only for `ENOENT`, another start time, or an exited entry; `Unknown` for
    any other failure — and `Unknown` keeps the marker and makes the launch Busy, with the errno named. `StartedAt` keeps its
    two-state answer for its kill-side callers. **The move touches `src_server`** (the plan gate for epics 4–6, 2026-10-16,
    F9): `ReaderReconcile` (`src_server/src/Slots/ReaderMarker.cs:97-137`, with the marker record and reasons its signature names)
    leaves that file; its one product caller, `SlotRegistry` (`src_server/src/Slots/SlotRegistry.cs:99`), and `ServerJsonContext.cs`
    then reference the moved types. The server's own tests must still pass unchanged: `ReaderBarrierTests`
    (`src_server/tests/ReaderBarrierTests.cs`, which calls `ReaderReconcile.RunAsync` directly at `:154`, `:172`) and
    `GaugeReaderCrashHarnessTests` (`src_server/tests/GaugeReaderCrashHarnessTests.cs`, which plants markers the registry reconciles).
    **The manual way out is ALWAYS in the sentence; the panel button is an addition** (the plan gate, F11; the plan gate for epics
    4–6, 2026-10-16, F12). The Busy sentence names the marker FILE by its full path, the pid(s) it names, and says that the file is
    safe to delete once those processes are gone — a sentence a person can act on with no button at all, so an older extension
    without the button still gets a usable answer. It then adds that the Kimi card's *Clear the stuck Kimi marker* button does it
    (the owner's rule that every action is a button). The button runs a one-shot mode of the server
    (`coai-mcp --kimi-clear-marker <path>`), which re-checks every member first and clears the marker ONLY when it can POSITIVELY
    verify that every named member is gone (the plan gate for epics 4–6, 2026-10-16, F2). "Gone" means `/proc/<pid>` answers
    `ENOENT`, or the process there has a different start time (a reused number) — or, by DL9's zombie rule above, the entry has
    exited (`ExitedEntry`). Any other failure — `EACCES`, `EIO`, a `stat` that cannot be read or parsed — is not evidence of
    anything: the marker stays, and the mode exits non-zero with an exit code of its own, naming the error and the pid. Exit codes,
    **fixed** (A3 spec, 2026-10-16; this text used to leave them to E5's stories): **0** cleared, or no marker at that path; **65**
    the arguments are wrong (no path, or a path that is not a marker under the Kimi lock root — the mode deletes nothing else);
    **75** a named member still lives (its pid named); **74** a member could not be verified (the errno named) — and a marker FILE
    that cannot be read or parsed is **74** too, not 65: 65 is an argument fault, a content fault is "cannot verify". Every member
    is checked first, then the verdict: `Alive` wins over `Unknown` (75 before 74), a living survivor being the stronger fact. A
    `spawning` marker answers by the listing rule above: 0 no `kimi`, 75 a `kimi` (its pids), 74 the listing unreadable. The mode
    signals nothing, ever. Never 64: a binary that knows the mode never answers 64
    (`.agents/PROJECT.md:63`, `:71-73`). The button can never clear a marker over a living — or an unverifiable — survivor.
    **Budgeted** (the plan gate for epics 4–6, 2026-10-16, F16): E5 owns the mode — its `args[0]` arm in `Classify`
    (`src_mcp/src/Program.cs:323-366`) and its dispatch beside `--ask-acp`'s (`:427-430`) — and its entry, with those exit codes, in
    the one-shot list of `.agents/PROJECT.md:51-68` ("adding a one-shot mode means adding it here"). E6 owns the rest: the server's
    providers answer reports a standing marker per Kimi row, and the Kimi card shows the button, which runs the mode, only while
    that report stands.
  **The supervisor's own failure is in scope.** A subreaper that is killed cleans nothing up. Verified: the `--ask-acp` mode is
  started with no cancellation token and registers no signal (`Program.cs:430`; `AskAcpMode.RunAsync`'s `ct = default`,
  `AskAcpMode.cs:73-79`), so on a `SIGTERM` the runtime exits without running the `finally` that ends the agent
  (`AcpClient.cs:106-110`); what covers the children then is the SERVER's `Process.Kill(entireProcessTree: true)` on the shim
  (`ProcessLauncher.cs:653-663`), which lists the shim's descendants by parent pid while the shim still lives — and under the
  subreaper that listing holds the reparented orphans too. So: the shim registers `SIGTERM`/`SIGINT` into its token. **Three copies
  of that registration exist already** (A3 spec, 2026-10-16; this text used to cite `Program.cs:1988-1997` as "the stdio server",
  but that is `RefusingServeAsync`, the server for a MISSING data root; the serving server uses `ServeStop.Register`,
  `ServeStop.cs:60-75` — four signals (`:32-33`), with a grace; the third is the consultant check,
  `ConsultantCheckMode.cs:61-65`, beside its `CancelKeyPress` handler at `:55-60`). So the two-signal registration is EXTRACTED once,
  `StopSignals.Into(CancellationTokenSource)` (`src_mcp/src/Server/StopSignals.cs`), and the `--ask-acp` mode uses it. Routing the
  existing copies through it is NOT done in E5 — it is asked (§11 Q7): a neighbour is named and asked about, never rewritten as a
  side effect. The L-N2 test gains the arms "the shim is `SIGTERM`ed while the agent
  lives" (the finally runs; the tree is ended) and "the shim is `SIGKILL`ed" (nothing of the shim runs; the `running` marker
  written right after the start names the agent; the next launch reconciles it), and the cancellation of the outer launcher is
  the same arm seen from the server.
  **Measured (L-K4, RESULTS_kimi_confinement §29): confirmed.** The real `rg` of a Grep turn runs in its OWN group and session; a
  group kill misses it and a pid kill leaves it, alive 0.1–0.5 s. Without a subreaper its orphan is reparented to WSL's
  `Relay(<pid>)`, not to pid 1 — so no teardown may look for `ppid == 1`; under `PR_SET_CHILD_SUBREAPER` the orphan comes to the shim
  and `Descendants(shim pid)` finds it. Only the agent held the inherited lock descriptor (DL8's premise holds). Not run: Kimi's
  sub-agents, background shell and MCP servers — S4's test keeps its own grandchild arm.

- **DL10 — The foreign-Kimi wait reads `/proc` on Linux.** `HostProcessList.Current` returns `[]` off Windows
  (`src_mcp/runners/Kimi/HostProcessList.cs:29`, `:53`), so on Linux the wait for the person's own `kimi` never waits — silently. The
  Linux listing reads `/proc/<pid>/stat` (pid, parent, the bracketed `comm`); its reader is EXTRACTED from `ProcessTree.LinuxParents`
  (`ProcessTree.cs:169-196`), which already parses that line for the parent — one reader, not two. **An unreadable or incomplete `/proc` is
  never evidence that nobody is running** (consultation c6e42fd3, verified): `IProcessList.Snapshot` says "empty when the host
  cannot say" (`ForeignKimi.cs:9-10`) and `Wait` reads an empty listing as the way being clear (`ForeignKimi.cs:62-74`) — fail-open,
  and exactly what the `[]` off Windows does today (`HostProcessList.cs:53`). **Windows is fail-open too** (A2 spec, 2026-10-16):
  a failed Toolhelp snapshot, or a walk that ends on an error, answers `[]` (`HostProcessListNative.cs:17-20`, `:42`). So the
  listing answers a closed union on EVERY platform — the processes, or *unreadable* with the reason: Windows names the Win32 error
  of the failed snapshot or walk, Linux the `/proc` read that failed, and macOS or a 32-bit process answers *unreadable* ("no
  process listing on this host") — and *unreadable* ends the wait at once as Busy with the reason, never as clear; a listing that
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
  the reason. **Two refinements** (cadence consult 8df043b8, verified against `proc_pid_exe(5)`): the kernel appends ` (deleted)`
  to the `exe` target once the file is unlinked — which is what a Kimi reinstall or update does to a RUNNING `kimi` — so the basename
  is compared after that suffix is stripped (a running old `kimi` is still Kimi, and still a refresher); and a same-uid entry that
  has EXITED (state `Z`/`X`, the `ExitedEntry` rule of DL9) is skipped before its `exe` is read — a zombie's `exe` cannot be read,
  and an unrelated zombie of any program must not make every Kimi launch Busy. The detection boundary, stated: a `kimi` started
  under ANOTHER file name, or hosted by an interpreter whose `exe` is not `kimi`, is not recognised — a shell wrapper that ends in
  the normal `kimi` binary is. `coai-mcp` in the parent chain is matched the same way, by the `exe` basename (`coai-mcp`); its `comm` was not
  measured and is not relied on. The parent-chain rule itself held: the agent's chain reached the driver.
  **Re-decided 2026-10-17 — a hidden `exe` falls back to `argv[0]`, measured on the owner's WSL** (question consultant a2fa7b43; A2's
  report). Linux reads `/proc/<pid>/exe` under PTRACE_MODE_READ_FSCREDS, so a SAME-uid process can still hide it: one with extra
  capabilities, or one that is non-dumpable. The ubuntu CI runner has `systemd --user` hiding it (CapPrm 0x800000000), and so does
  the owner's WSL2 (read-only check 2026-10-17): `systemd` (CapPrm 0x800000000) AND `(sd-pam)` (CapPrm 0 — non-dumpable, so a
  capability exception alone would not admit it). Under the rule above every Kimi launch on that machine would be Busy — not a
  usable product, and "refuse only what cannot be made safe" (owner) says the check must be precise, not blanket. Both processes'
  `/proc/<pid>/cmdline` stay readable (same check). So the identity is read in this order, per same-uid process: (1) the `exe`
  basename (with the ` (deleted)` strip); (2) when `exe` is denied, the basename of `argv[0]` from `cmdline` (and `comm` — `kimi`
  in either is Kimi); (3) only when BOTH `exe` and `cmdline` are unreadable is the process *unreadable* → Busy, naming the pid and
  what could not be read (the consultant's actionable diagnostic). A process gone between the listing and the read (ESRCH/ENOENT) is
  skipped. Residual, stated in the code: a non-dumpable `kimi` started under another `argv[0]` is not recognised — the same boundary
  as "a `kimi` started under another file name" above; `argv[0]` is the process's own claim, and the threat here is the person's own
  `kimi`, not a process hiding from coai. Tests: the three `/proc` shapes measured (hidden `exe` + readable `cmdline` naming
  `systemd` → skipped; the same naming `kimi` → Running; both unreadable → Unreadable with the pid), on the fake `IProcReads`.

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
  - `fd`: skipped when `fd` resolves or `~/.kimi-code/bin/fd` exists (the owner's case: it does); else the `fd-find` package;
  - the `fd` link, written out (the plan gate for epics 4–6, 2026-10-16, F7 + F17): Debian and Ubuntu name the binary `fdfind` and
    Kimi looks for `fd` (fd's README), Fedora's `fd-find` installs `/usr/bin/fd` and needs no link. So, after the packages:
    `mkdir -p ~/.local/bin` first (a fresh account has no such folder, and `ln` into a missing folder fails); then the link ONLY when
    `command -v fdfind` resolves AND `command -v fd` does not; the step is idempotent — it is skipped when `~/.local/bin/fd` is
    already a link to that `fdfind`, and otherwise made with `ln -sf "$(command -v fdfind)" ~/.local/bin/fd`, on that exact path
    only, so a second press neither fails on an existing link nor touches anything else. *As built (the review of 2026-10-18, G):* the
    link is judged by what is at the path — a REAL file, or a link of the person's own that still resolves to a file, is left alone; only
    an absent path, a dangling link or a link to a folder gets the link — `mkdir -p ~/.local/bin` moved INSIDE that branch (a host that needs
    no link gets no empty `~/.local`), it is `ln -sfn`, and the target is absolute: `/usr/bin/fdfind` when it exists, else `command -v`'s
    answer made absolute;
  - the line ENDS by checking its own work (the plan gate for epics 4–6, 2026-10-16, F10): it resolves `rg` and `fd` the way the SERVER will look
    (`KimiTools.Directories` — the person's Kimi `bin`, then `PATH`, then `~/.local/bin`; DL5 says the same, and the line follows it since the review of
    2026-10-18, H — it had listed `~/.local/bin` before `PATH`) — and prints which path each one resolved to, or that it did not, so a press that
    installed nothing usable says so in the terminal rather than at the next launch;
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
  word is `sudo` is refused unless the rest of it is one of the two admitted package steps; `mkdir -p ~/.local/bin`,
  `ln -sf "$(command -v fdfind)" ~/.local/bin/fd` and `command -v` carry no `sudo` and no install word and stay as the parser reads them. *Security (supply chain): Fable's security
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

- **DL14 — Release order and versions.** Vibe ships first: E1–E3 in the next `src_mcp` minor, **0.59.0**, with an extension minor.
  Kimi follows in the first minor after E4–E6 (0.60.0 at the earliest — and see the 2026-10-18 note at the end of this decision: main released 0.60.0 without it, so it is **0.61.0**). The per-runtime gate `RUNTIME_SINCE`
  (`src_vs_code/src/runtimeSince.ts:46-50`) answers "does this server know the runtime", not "does it run it on THIS host", so a
  second table joins it: `VIBE_ON_LINUX_SINCE = '0.59.0'`, `KIMI_ON_LINUX_SINCE` set by E6 — a Linux/WSL side whose server is older
  shows "update coai-mcp to run Vibe here" instead of a row that is refused at launch. **The unreleased Kimi-consultant branch
  (`feat/kimi-consultant`) sets `KIMI_CONSULTANT_SINCE` to 0.58.0. Whichever of the two merges SECOND bumps its constant to the next
  minor** (`runtimeSinceIsAhead.test.ts` guards the table, not unreleased commits — the reason `KIMI_RUNTIME_SINCE` once said 0.56.0,
  `runtimeSince.ts:30-42`). 2026-10-17: mcp 0.58.0 shipped first from main (the plan-gate round history, #844), so Vibe on WSL2 takes 0.59.0 and Kimi on Linux 0.60.0 at the earliest. **2026-10-18:** main released mcp 0.60.0 (#857, the prompt-cap work) without Kimi on Linux, so by the second-merger rule Kimi on Linux ships in **0.61.0**: `KIMI_ON_LINUX_SINCE` is `'0.61.0'`, the help says 0.61.0 in five languages, and the release PR — not this branch — moves the manifests and the changelog.

## 5. The measurement cells, and who waits for them

**Port the probes first** (E1): today they are Windows-bound — `scripts/lib/kimi.mjs:20` (`kimi.exe`), `:99` (`rg.exe`, `fd.exe`),
`:204` (`mklink /J` through `cmd.exe`), `:406` (`taskkill`); `scripts/probe-kimi-tree-hold.mjs:61` (exits off `win32`), `:86`
(PowerShell for the process list); `scripts/lib/kimi-home.mjs:98`; `scripts/lib/vibe.mjs:18` (`MINIMAL_WINDOWS`), `:57-58`
(`taskkill`); `scripts/probe-vibe-confinement.mjs:86` (`python.exe` under `APPDATA`), `:158` (`mklink /J`). The Linux arms use
`ln -s`, `MinimalUnix`'s names, `/proc` and `kill -- -<pgid>`; every launch from a script FILE in an owned `testsToDelete-` folder,
a built environment, nothing written into `~/.vibe` or `~/.kimi-code` by the probe (checked by a listing before and after).

| Cell | Question | Cost | Gates |
|---|---|---|---|
| L-V6 | Which variable NAMES the owner's `~/.vibe/.env` holds (names only, values never read out). **Not run in E1** (E1 never reads the owner's `.env`): E2 keeps `{MISTRAL_API_KEY}`, and a refusal by line number answers it on his first WSL launch. | free | E2 (DL2's admitted set) |
| L-V7 | `scripts/vibe-home-manifest.mjs --write` in WSL: the `linux` entry; a second fresh home agrees on every hash. | 2 tiny turns | E2 (DL3) |
| L-V8 | Does Vibe load a `.env` from its WORKING folder? (A bogus `MISTRAL_API_KEY` in the cwd, no home `.env`: still L-V1's "Missing" → it does not.) Code reading of Vibe's package first — done, and it predicts "no": every call site reads `VIBE_HOME/.env` only (DL2; A1 spec, 2026-10-16). | free | E2 (snapshot exclusion) **and E3's release** — 0.59.0 is not tagged before it is recorded |
| L-V6b | python-dotenv — the version in Vibe 2.26.1's own tool environment — over DL2's fixed set of lines AND whole files (the three multi-line shapes of DL2), written as `shared/dotenv-name-vectors.json`: per vector the names python-dotenv binds, and coai's expected verdict (admitted, or refused at line N with no file text). No `.env` of the owner's is read. | free | E2 (DL2's grammar) |
| L-V9 | L-V1, L-V2/3, L-V4 re-run through the ported probe, 3 of 3. **Measured 2026-10-16: 3 of 3 on every cell** — signed out → "Missing MISTRAL_API_KEY", free; signed in through the `.env` link; read inside served, outside denied; write_file, bash and run_typescript all denied (9 runs, the model's retries included); `--resume` the same session; `~/.vibe` byte-identical (RESULTS_vibe_confinement §31). 21 paid turns: `VIBE_ACTIVE_MODEL=nope` is PAID on Linux (free on Windows). The run used `env -i` with the MinimalUnix names and a `→` in three prompts — the committed probe inherits its whole environment and its prompts are ASCII; both fixed in the probe after the run. | ~10 turns (21 spent) | E3 (release) |
| L-V10 | L-V9 once on a NATIVE Linux host (not WSL). | ~10 turns | lifting DL1's native-Linux refusal — not this release |
| L-N1 | .NET on Linux: `HeldFile.Take(…, inheritable: true)` excludes a second take (inheritable or not), and a child started after it holds the lock after the parent's SIGKILL. A test in the Linux CI job, in a child process (no test holds an inheritable handle in the host). Predicted (A2 spec, 2026-10-16): the exclusivity FAILS — the combined share mode takes a shared `flock` — so DL8's native take is expected; it ships only once this red run confirms it. | free | E5 (DL8) |
| L-N2 | A subreaper shim and a FAKE agent that starts a same-group child and a `setsid` child, then exits first: after the teardown both are gone. Three more arms (DL9): a grandchild born AFTER the first listing is found by the re-listing confirmation; the shim `SIGTERM`ed while the agent lives ends its tree; the shim `SIGKILL`ed leaves its `running` marker naming the agent (written right after the start — DL9's phases; A3 spec, 2026-10-16), which the next launch reconciles. One more arm (the plan gate for epics 4–6, 2026-10-16, F11 + F15): a grandchild that exits by itself BEFORE the teardown is reaped by the shim's drain, is not recorded as a survivor, and no marker remains. A test in the Linux CI job. | free | E5 (DL9) |
| L-N3 | A survivor marker makes the next launch on that sign-in Busy with the pid named, and the home disposal keeps the home; once the member is gone the next launch clears it. **No real process can stage the survivor** (A3 spec, 2026-10-16): the teardown ends members with `SIGKILL` (`ProcessTree.cs:134`, `ProcessLauncher.cs:657`, kept by DL9), which nothing ignores, so the old "`setsid` child that ignores `SIGTERM`" cannot outlive it. The still-there-at-the-bound arm runs through the reconcile seam (`terminate` refusing, `confirmGone` answering the members — the `ReaderBarrierTests.cs:172` precedent); the real-process arms prove gone → cleared, and alive → ended by identity → cleared. The seam arm everywhere, the real-process arms in the Linux CI job. | free | E5 (DL9's eligibility rule) |
| L-K1 | Live: `kimi --version` and `kimi acp --help` in the built environment; `Grep`/`Glob` with `rg` on the built `PATH` → no `bin/` in the owned home (M-K12 (c) on Linux); without `rg` → where the download lands. | free + 1 turn | E6 (DL5, DL12) |
| L-K2 | The sign-in through a SYMLINK into the `0700` folder: signed in; a refresh lands in the person's folder; Kimi neither replaces the link nor `chmod`s the target; the person's own `kimi` works after. | 3 turns | E5 (DL6) |
| L-K3 | `HOME` → `os/` alone: `~/.agents/skills` and `AGENTS.md` closed (the canary only with the owner's yes, as on Windows); `os/` stays empty; what lands in the owned home. | 3 turns | E6 (DL11) |
| L-K4 | `kimi acp`'s tree during a `Grep` turn: children, their group and session, which holds the lock fd (`/proc/<pid>/fd`), whether any descendant is another `kimi`; the kill arms (parent, agent pid, group). The scout's 4-turn script is the start. | 4 turns | E5 (DL8, DL9) |
| L-K6 | `/proc/<pid>/stat` `comm` of `kimi acp` under the shim and of the person's TUI; the parent chain to `coai-mcp`. | free (inside L-K4) | E5 (DL10) |
| L-K7 | The write arms and `session/load` through the shim on Linux. | 3 turns | E6 |
| M-K14 | The refresh race — unchanged: later, it gates only lifting the serialisation (D19). | — | none here |

**Every Kimi cell runs only with the owner's yes AND no other `kimi` process on that side**: the refresh token rotates (D19); a
second refresher can sign the owner out. "No other `kimi`" is checked by matching, per process, the basename of `/proc/<pid>/exe`
= `kimi` OR `comm` = `kimi` — what the E4 run did, both (the plan gate for epics 4–6, 2026-10-16, F8). `ps -eo comm=` alone is not
enough: L-K6 measured `comm` = `MainThread` for a running `kimi acp`, so a `comm`-only guard misses every ACP agent (DL10). The
scout's prepared run is the template.

## 6. Build order

1. **E1** — port the probes; write the Vibe RESULTS section from the scout's draft; run L-V6, L-V7, L-V8; commit the `linux` manifest
   entry the script wrote. Nothing in `src_mcp` changes except the embedded JSON. **Done 2026-10-16** — L-V6 not run, the entry
   STAGED (DL3); see E1's outcome below.
2. **E2** — Vibe on a Linux host (DL1 for Vibe, DL2, DL3, DL5's Vibe half, the diagnosis text). Red-first tests flip the D18 pins.
3. **E3** — the extension's Vibe texts, `VIBE_ON_LINUX_SINCE`, the limitation rows, L-V9, the live WSL checks through the released
   binary. **Release 0.59.0.**
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
- S-V5 *Given* `~/.vibe/.env` naming `VIBE_MCP_SERVERS` as well, *then* refused by LINE NUMBER only, and the sentence holds
  neither the name nor any value (DL2's post-consult rule; A1 spec, 2026-10-16 — this scenario used to say the names are listed).
  Red: `"a .env naming VIBE_MCP_SERVERS should refuse"`, `"the refusal named VIBE_MCP_SERVERS — a found name may be value text"`
  and `"the refusal must not contain the value 'sk-test-…'"`.
- S-V6 *Given* `~/.vibe/.env` is group-writable, or is itself a link, *then* refused with the cure.
- S-V7 *Given* no `~/.vibe/.env`, *when* Vibe exits 1 with L-V1's wording, *then* the row says *not signed in*, cure `vibe --setup` on
  this side, naming `~/.vibe/.env` AND the keyring sentence (a desktop keyring is invisible to coai's launches, no D-Bus).
  Red: `"the WSL cure should name ~/.vibe/.env and say a keyring sign-in is invisible to coai, but said: …"`. (No pin AGAINST the
  word "keyring": the sentence must contain it.)
- S-V8 *Given* a Linux host and Vibe 2.26.1 with the `linux` entry's bytes, *then* no warning; with one plugin byte changed, refused with
  the path. Red: `"a Linux 2.26.1 home with its own bytes was refused against the win32 hashes"`.
- S-V9 *Given* a version with only a `win32` entry, on Linux, *then* it runs with the shared warning naming `win32`.
- S-V10 *Given* a home with a `.env` link to a `0600` or a `0400` file, *when* each deleter runs, *then* the link is gone and the
  file's bytes, mode and mtime are unchanged. **Green first, not red** (A1 spec, 2026-10-16): today's deleters already unlink a link
  by its name (DL2's deletion bullet), so there is no product defect to observe red. Teeth: a mutant deleter in the test (no link
  check, `File.SetAttributes` then `File.Delete`) goes red on the `0400` arm with `"the deleter changed the person's ~/.vibe/.env
  (mode 400 → 600)"`; both observations are recorded.
- S-V11 (DL5's precedence, one row per step) *Given* `vibe` only in `~/.local/bin` and a `PATH` without it, *then*
  `DefaultExecutable` is that path. Red: `"expected ~/.local/bin/vibe, got vibe"`. *Given* the row's own CLI path is set, *then* it
  wins over all. *Given* `$UV_TOOL_BIN_DIR/vibe` and `~/.local/bin/vibe` both exist, *then* the first. *Given* `~/.local/bin/vibe` and
  another `vibe` on `PATH`, *then* `~/.local/bin/vibe`. *Given* none, *then* bare `vibe` (the `PATH` walk), and a failed start carries
  the install cure.
- S-V12 *Given* a Windows host whose row names `wsl.exe`, *then* still refused with the D18 sentence (DL1).
- S-V13 (DL2's grammar, a Theory over `shared/dotenv-name-vectors.json` — over a test-local copy of the same vectors until E1
  merges, python-dotenv's output winning any disagreement; A1 spec, 2026-10-16) *Given* each vector line, *then* the names coai reads equal
  the names python-dotenv bound. Admitted rows include `MISTRAL_API_KEY=v`, `MISTRAL_API_KEY = "v"`, `export MISTRAL_API_KEY='v'`,
  `  MISTRAL_API_KEY=v  # note` and `'MISTRAL_API_KEY'=v`. Red: `"python-dotenv binds MISTRAL_API_KEY from 'MISTRAL_API_KEY = "v"'
  but coai refused the line as unreadable"`. *Given* a bare key on its own line, a line beginning with `=`, or a quoted value that
  continues on the next line, *then* refused with the generic sentence naming the line number and nothing from the file. Red:
  `"the refusal echoed text from the .env: 'sk-test-…'"`. *Given* the whole files `MISTRAL_API_KEY="dummy` + `LEAK_CANARY=ending"`, *then*
  refused at line 2 and the sentence holds neither `LEAK_CANARY` nor `ending`; `MISTRAL_API_KEY="dummy` + `#ending"`, *then* admitted
  (python-dotenv binds one name); `MISTRAL_API_KEY="a` + `"` + `VIBE_X=1`, *then* refused at line **2**, `NoBinding` — the lone `"`
  is a bare word to the name grammar (an unquoted name is any run without `=`, `#` and whitespace), so coai stops there, before the
  `VIBE_X` line (A1 spec, 2026-10-16; this scenario used to say line 3; the implication holds either way). *Given* a file whose
  first bytes are a UTF-8 byte-order mark, *then* refused at line 1, `ByteOrderMark`; *given* an invalid UTF-8 sequence or a NUL on
  line N, *then* refused at line N, `NotText`. Red: `"the refusal named
  LEAK_CANARY, which is value text"`. *Given* `MISTRAL_API_KEY=x` + bare CR + `VIBE_X=1`, *then* refused at line 2. Red: `"a bare CR
  hid VIBE_X from coai, which python-dotenv binds"`. The Theory asserts the IMPLICATION over every vector, not name equality:
  *coai admits the file ⇒ every name python-dotenv binds is `MISTRAL_API_KEY`*, run through the product's own line reader.
- S-V14 *Given* `~/.vibe/.env` is a FIFO, a socket, or a file whose owner is another uid, *when* the link maker judges it, *then*
  refused through the descriptor without blocking (the test's FIFO has no writer); the FIFO's sentence says Vibe itself could read
  a FIFO sign-in and coai links only a regular file it can judge (A1 spec, 2026-10-16). Red: `"the judgement blocked on a FIFO"` /
  `"a file owned by uid … was admitted"`.
- S-V15 (F3) *Given* the link cannot be made — the owned home on a filesystem that refuses symbolic links (a test seam), the person's
  `~/.vibe` missing, or the home not writable — *when* a reviewer launches, *then* nothing is left at `$VIBE_HOME/.env`, Vibe runs and
  exits 1 with L-V1's wording, the row's cure names `vibe --setup`, and the reason the link was not made arrives as the launch's
  WARNING (`LaunchTurn.Warning` → `ReviewerOutcome.Warning`) on the same reviewer line — a recorded deviation from F3's letter, which
  put the reason in the cure (DL2; A1 spec, 2026-10-16). Red: `"the warning did not say why the .env link could not be made"` /
  `"a half-made .env was left in the home"` / `"a copy of .env was made as a fallback"`.
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
- S-K3 *Given* two spellings of one folder (a symlinked parent), *then* `KeyFor` returns ONE key; a deleted folder → empty key;
  a FILE at the path → empty key (DL7's directory-only rule; A2 spec, 2026-10-16).
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
  Red: `"a bind-mounted spelling of one sign-in folder gave a second lock key"`. **A bind mount needs `CAP_SYS_ADMIN`** (A2 spec,
  2026-10-16): the test mounts through `sudo -n mount --bind` and unmounts in `finally`; without passwordless `sudo` it FAILS on CI
  (`CI` set) with `"bind-mount test needs passwordless sudo on the runner"`, and locally it is skipped with that reason — a
  silently skipped check is a deleted one.
- S-K12 *Given* a mountinfo table with a `drvfs`-typed mount at `/data/win` (not under `/mnt/`) and a target below it, *then*
  refused as a Windows filesystem; *given* the same table and a target on the ext4 root, *then* admitted (DL6). Red: `"a sign-in
  folder on a drvfs mount outside /mnt/ was admitted"`.
- S-K13b (cadence consult 8df043b8) *Given* a same-uid fake process whose `exe` reads `/…/kimi (deleted)` and `comm` `MainThread`,
  with no `coai-mcp` in its parents, *then* the wait answers Busy; *given* an unrelated same-uid ZOMBIE whose `exe` cannot be read,
  *then* it is skipped and the launch proceeds. Red: `"a running kimi whose binary was replaced was not seen"` / `"an unrelated
  zombie made the Kimi launch Busy"`.
- S-K13 *Given* a Linux listing that cannot be read (a `/proc` the fake cannot open, or one without the shim's own pid), *then*
  the foreign-Kimi wait answers Busy with the reason, and nothing starts (DL10). Red: `"an unreadable process listing was read as
  'nobody is running'"`. *Given* a failed Windows snapshot (a seam over the native shell's answer), *then* the same: Busy with the
  Win32 error, never clear (A2 spec, 2026-10-16). Red: `"a failed Toolhelp snapshot was read as 'nobody is running'"`.
- S-K14 *Given* a survivor marker from the previous turn naming a process that is still alive and the same (pid and start), *when*
  the next launch takes the gate, *then* it reconciles first: terminate by identity, confirm, and — the process still there at the
  bound — Busy with the pid; the home disposal keeps the home. *Given* the marker names a process that is gone, *then* cleared and the
  launch proceeds (DL9). Red: `"a launch proceeded on a sign-in whose previous tree still has a survivor (pid …)"`. *Given* a
  standing marker when the SHIM takes its tree hold (the third place, DL9; A2 spec, 2026-10-16), *then* the hold is given back and
  the shim answers Busy (exit 69) with the marker's sentence. Red: `"the shim's hold was Held over a standing survivor marker"`.
  The "still there at the bound" arm runs through the reconcile seam, not a real process (L-N3; A3 spec, 2026-10-16).
- S-K15 *Given* a fake agent that starts a `setsid` grandchild AFTER the shim's first listing and then exits, *when* the teardown
  confirms, *then* the re-listing finds the grandchild under the subreaper and ends it (DL9). Red: `"a grandchild born after the
  first listing survived the confirmation"`.
- S-K16 *Given* the shim is `SIGTERM`ed while its agent lives, *then* the finally runs and the agent's tree is ended; *given* it is
  `SIGKILL`ed, *then* the `running` marker written right after the start names the agent, and the next launch's reconcile clears
  it once the agent has died on its stdin's EOF (DL9's phases; A3 spec, 2026-10-16). Red: `"after SIGTERM the agent (pid …) is
  still alive"` / `"the marker written before the start does not name the agent"`.
- S-K17 (F2 + F13) *Given* a WSL side where neither `apt-get` nor `dnf` resolves (stand-ins), *when* the install line runs, *then*
  it installs no tool, prints the generic instruction and still runs Moonshot's script; *given* `rg` and `fd` missing on such a host,
  *then* `KimiTools.Refusal` holds the same generic sentence and neither `winget` nor `apt-get`. Red: `"on a host without apt-get or
  dnf the line tried 'sudo apt-get …'"` / `"the refusal named a package manager this host does not have"`. Three more arms (the
  plan gate for epics 4–6, 2026-10-16, F7 + F17 + F10), against stand-ins: *given* the line has already run once, *when* it is
  pressed again, *then* it succeeds and changes nothing (`~/.local/bin/fd` the same link, no error from `ln`); *given* a fresh
  account with no `~/.local/bin`, *then* the folder is made and the `fd` link lands in it; *given* a Fedora stand-in where `fd`
  already resolves (`/usr/bin/fd`), *then* no link is made. Every arm ends with the line printing where `rg` and `fd` resolve. Red:
  `"the second press failed: ln: … File exists"` / `"ln into a missing ~/.local/bin"` / `"a link was made although fd already
  resolved"`.
- S-K18 (F11; the plan gate for epics 4–6, 2026-10-16, F2 + F12) *Given* a standing survivor marker, *when* the next launch is Busy,
  *then* its sentence holds the marker's full path, the pid(s) and "safe to delete once those processes are gone" — asserted with
  NO button in play, as an older extension would show it — and names the button as an addition; *when* the button's one-shot mode
  runs while a named member still lives, *then* the marker stays, the mode exits 75 and names that pid; *given* a member whose
  `/proc` entry cannot be read (a seam answering `EACCES`, then `EIO`, then an unparseable `stat`), *then* the marker stays and the
  mode exits 74 naming the error; once every member answers `ENOENT` or another start time, the marker is gone, exit 0. Red: `"the
  Busy sentence gave no way out: no marker path"` / `"the clear mode removed a marker over a living survivor (pid …)"` / `"a member
  whose /proc cannot be read was taken as gone, and the marker was cleared"`. **The same three seam answers through the NEXT LAUNCH's
  reconcile** (cadence consult 8df043b8): the marker stays and the launch is Busy with the errno named, never cleared by the
  automatic path where the button would refuse. Red: `"the next launch's reconcile cleared a marker whose member it could not read"`.
  **The fixed exit codes' remaining arms** (A3 spec, 2026-10-16): *given* a marker file that cannot be read or parsed, *then* it
  stays and the mode exits 74; *given* no path, or a path outside the Kimi lock root, *then* exit 65 and nothing deleted (a decoy
  file outside the root survives), never 64. Red: `"the clear mode deleted a file outside the Kimi lock root"` / `"exit was 64 — a
  known mode never answers 64"`. **A `spawning` marker** (it names no process — DL9's listing rule): *given* a readable listing with
  no `kimi` of this uid, *then* cleared, exit 0; *given* a `kimi`, *then* kept, exit 75 with its pids; *given* an unreadable
  listing, *then* kept, exit 74 — and the next launch's reconcile answers the same three ways (cleared, Busy, Busy). Red: `"a
  spawning marker was cleared while a kimi runs"`.
- S-K19 (F15) *Given* a lock root with a key whose recorded folder no longer exists and nobody holds its files, *when* the sweep runs,
  *then* both files are gone; *given* the same with the gate file held, *then* both stay; *given* a taker whose locked file was
  unlinked by the sweep between its open and its lock, *then* it re-takes on the new file. Red: `"a lock file of a vanished sign-in
  folder outlived the sweep"` / `"two holders of one sign-in lock after a sweep"`. *Given* a Windows host, *then* the sweep is inert
  — it returns at once and deletes nothing (DL7: Linux only; A2 spec, 2026-10-16). Red: a delete attempted on Windows
  (`IOException: The process cannot access the file …`).
- S-K20 (the plan gate for epics 4–6, 2026-10-16, F0) *Given* a Kimi row, *then* the host admission is the same `NamesWsl2`
  predicate as Vibe's (DL1): a Theory over the same three `/proc/version` banners as S-V17 — WSL2 (`…-microsoft-standard-WSL2`),
  WSL1 (`4.4.0-19041-Microsoft`), native (`…-generic`) — of which only the first starts a Kimi row; WSL1 is refused with the WSL1
  sentence. Red: `"a WSL1 banner was admitted as WSL2 for a Kimi row"`.
- S-K21 (the plan gate for epics 4–6, 2026-10-16, F4) *Given* the pidfd layer's seam answering "no libc wrapper" (glibc < 2.36),
  *when* the teardown ends a member, *then* the raw `syscall` path (434 / 424) is the one used and the member is ended by identity;
  *given* the seam answering `ENOSYS` from the raw call, *then* the Linux Kimi launch is refused before anything starts, with DL9's
  no-pidfd sentence, and nothing is signalled by number. Red: `"with no libc wrapper the layer fell back to a kill by number"` /
  `"a kernel without pidfd launched Kimi"`. **How "nothing by number" is asserted** (A3 spec, 2026-10-16): `Process.Kill` cannot be
  spied, so the seam hands the layer a RECORDING fake `IPidfdCalls` that records every call and forwards the signal itself, and the
  target is a sleeper child that stays alive until the fake forwards it — a kill by number would end it before any recorded
  `Signal`. That arm runs everywhere; the arm through the REAL raw syscalls against a sleeper needs a Linux kernel and runs on the
  Linux CI job only.
- S-K22 (the plan gate for epics 4–6, 2026-10-16, F11 + F15) *Given* a fake agent whose `setsid` grandchild exits by itself before
  the teardown starts, *when* the transport ends, *then* the grandchild's zombie is reaped by the shim's drain, it is not listed by
  the confirmation, not recorded as a survivor, and no marker remains. Red: `"an exited grandchild (state Z, pid …) was recorded as
  a survivor"` / `"a marker remained after a clean teardown"`.

## 8. Test plan

- **Flip the pins, red first.** The tests that hold the refusals today are rewritten for the new rule in the same commit as the
  change, and each is observed red against the old code: `src_mcp/tests/VibeLaunchControlTests.cs:257-268` (Linux and WSL refused →
  admitted; a `wsl.exe` row on Windows still refused), `KimiLaunchChecksTests.cs:184-195`, `KimiLaunchControlTests.cs:81-84`,
  `JunctionsTests.cs:275-290` (S1.4 becomes: Linux and WSL make a symlink, macOS and Other still refused),
  `HostProcessListTests.cs:75-81` (the empty listing becomes a `/proc` listing; macOS and a 32-bit process answer *unreadable*,
  not `[]` — A2 spec, 2026-10-16), `KimiAcpProfileTests.cs:47-56` (Linux finds
  `~/.kimi-code/bin/kimi`; macOS still guesses nothing); the extension's hint and help pins; the `TheOwnersKimi` skips are re-read
  and each kept or lifted with its reason.
- **Linux CI runs the real paths.** S-K3, S-K4/5 (L-N1), S-K6/S-K15/S-K16/S-K22 (L-N2), S-K11 and S-K14 (L-N3) run WITHOUT the
  `WindowsStandIn` stand-in on the ubuntu job; the stand-in stays for the Windows-only decisions, but **off Windows it no longer
  sets `KimiCredentialsLock.Final`** (A2 spec, 2026-10-16; today `WindowsStandIn.cs:42` does): once the product names a Linux
  folder by its identity (DL7), the stand-in's spelling would hide the very identity under test; it keeps its symbolic-link shell,
  whose verb becomes `MakeLink` with no `Directory.Delete` (DL6). The skips at
  `KimiCredentialsLockTests.cs:247`, `:296`, `:314` are lifted on Linux. S-K12 and S-K13 run everywhere over a fake table / fake
  listing (pure functions), as `ForeignKimiTests` already does; S-K20 (a banner Theory) and S-K21's recording-fake arm run
  everywhere; S-K21's arm through the real raw syscalls runs on the Linux CI job only (A3 spec, 2026-10-16).
- **The server's tests survive the move** (the plan gate for epics 4–6, 2026-10-16, F9): `ReaderBarrierTests` and
  `GaugeReaderCrashHarnessTests` (`src_server/tests/`) pass unchanged after `ReaderReconcile` moves to the runners.
- **The ported code keeps its tests.** `RegularFiles`' Linux leg and the pidfd layer come with the tests wsl_care runs on them,
  re-homed under `src_mcp/tests/`; a port without its tests is a copy. `MountTable` has no tests of its own in wsl_care, so
  `MountTableTests` is written fresh here (DL6; A2 spec, 2026-10-16).
- **Deleters around a file link** (S-V10): `GitScratch.DeleteEvenIfReadOnly`, `VibeHomes.SweepOneShot`, the D20 sweep, the test sweeps
  — green first, its teeth shown by the mutant deleter (A1 spec, 2026-10-16).
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
  runtime's host given as WSL, the `.env` link made against a fake person's `~/.vibe/.env`, and one arm with `HostKind.Linux` refused (S-V17). E6 does the same for a Kimi row. Each flow goes into
  `research/module_tests.md` with what the harness does NOT prove: the real Vibe or Kimi (the fake CLI answers), the real
  keyring/D-Bus behaviour, a real WSL kernel's drvfs mount (CI is ubuntu, not WSL), the person's real `~/.vibe/.env`.
  **E5's Linux teardown flows get a row too** (A3 spec, 2026-10-16): L-N2's arms run through the REAL `coai-mcp --ask-acp` shim
  over the FakeCli agent, and their `research/module_tests.md` row says what they do NOT prove — the real `kimi`'s sub-agents,
  background shell and MCP servers, which E4 did not run either (RESULTS_kimi_confinement §29.8).
  **How the host is given — a seam the product path does not have yet** (A1 spec, 2026-10-16). The primary constructor
  `VibeRuntime(id, host)` (`src_mcp/runners/Vibe/VibeRuntime.cs:41`) is NOT what the service calls: the real service builds
  `new VibeRuntime(vendorId)` (`src_mcp/runners/Reviewers/ReviewerRuntime.cs:922`, the default list at `:867`;
  `src_mcp/runners/Consultation/ConsultantResolution.cs:53`), whose constructor takes `HostKinds.Current` (`VibeRuntime.cs:51-54`)
  — `Linux` on the ubuntu job, so every flow would be refused (S-V17). The harness story therefore adds an `AsyncLocal` host seam
  that this constructor consults, in the shape of `VibePathBudget.Using` (`src_mcp/runners/Vibe/VibePathBudget.cs:35`, `:41-47`:
  an `internal` per-flow override, disposed to restore the process's value).
- **Live, after each release:** in WSL through the released binary — a plan round, a code round, a two-turn consult and a question
  row with a Vibe row (E3), the same with a Kimi row (E6); the real `~/.vibe` and `~/.kimi-code` compared before and after.
- Run as the repository runs tests: the MTP executables and `node --test`, never `dotnet test`.

## 9. Growth

| Surface | Size | Who retires it |
|---|---|---|
| `shared/vibe-home-manifest.json` entries | one entry per Vibe version per platform, ~12 hashes, ~2 KB | git; replaced when a version is re-qualified |
| The `.env` link | one per Vibe home | gone with its home (round disposal, one-shot sweep, D20) |
| Kimi lock files on Linux (`~/.local/share/coai-mcp/kimi-locks/`) | two small files per sign-in folder identity; assumed at most 8 identities in a lock root's life — 16 files | the beat and start sweep removes a key's files when nobody holds them and the folder they record is gone or has another identity (DL7, F15); a held or undecidable file is left. The sweep is Linux only: on Windows the key is the path, which a re-made folder keeps, and a held file cannot be unlinked (A2 spec, 2026-10-16) |
| Zombies of orphans reparented to the shim | at most the agent's tree, and only between two drain steps | the shim reaps them itself: a per-pid `waitid(P_PID, pid, …, WEXITED \| WNOHANG)` drain on every step of the confirmation loop, never `waitpid(-1)`, sparing every pid the shim started (DL9; A2 and A3 specs, 2026-10-16 — this row used to say `waitpid(-1, WNOHANG)`, which DL9 forbids); one that exits after the last drain is reaped by whoever inherits it when the shim exits. Never counted a survivor — the confirmation drops exited entries (`ExitedEntry`) — so a zombie never leaves a marker (the plan gate for epics 4–6, 2026-10-16, F11 + F15) |
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
| `ReaderReconcile` moved from `src_server/src/Slots/ReaderMarker.cs` to `src_mcp/runners/Processes` | this plan (E5, DL9) | the vendor-slot reader barrier ([PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) epic 6) keeps its behaviour: `SlotRegistry` calls the moved code, and `ReaderBarrierTests` / `GaugeReaderCrashHarnessTests` pass unchanged (the plan gate for epics 4–6, 2026-10-16, F9) |

Disjoint otherwise: chat, the Team server and the bench are out, as in the parent plan.

## 11. Owner prerequisites and open questions

Prerequisites: Vibe paid turns for E1 and L-V9 (~12); Kimi paid turns for E4 (~16) with his own `kimi` closed while they run;
`sudo` once, by the install button (E6), if he wants `rg` from `apt-get`.

- **Q1 — Native Linux too, or WSL only? DECIDED by the plan gate (F10), 2026-10-16: WSL only** until a native-Linux run of L-V9
  (L-V10) is recorded; `HostKind.Linux` keeps a WSL2-only refusal (DL1).
- **Q2 — A `~/.vibe/.env` with more than `MISTRAL_API_KEY`, or a line the grammar cannot read.** Default: refuse, by LINE NUMBER
  only in both cases — never the name, which may be the continuation of a quoted value (DL2's post-consult rule; A1 spec,
  2026-10-16: this question used to say the names are listed). Another name: the sentence names the rule (*a `VIBE_*` or any other
  variable in that file is refused*), cure "keep only the key there"; an unreadable line: cure "each line is `NAME=value` or a `#`
  comment" (DL2's grammar — the refusal never quotes the file). The alternative — coai reads the key and passes it in the
  environment — is DL2's rejected option.
- **Q3 — His own `kimi` TUI open in WSL makes every coai Kimi turn wait, then answer busy** (decision 74 of the E4 plan, the same on
  Windows) until M-K14 is measured. Keep it, or run M-K14 first? **Default: keep the wait, as on Windows (decision 74)** (the plan
  gate for epics 4–6, 2026-10-16, F14): the Busy sentence already names the person's own `kimi` as the reason, so the wait is never
  a mystery. Revisited only if the owner meets it in daily use.
- **Q4 — `sudo apt-get` in the install terminal** (he types his password there). Default: yes.
- **Q5 — Release order** if `feat/kimi-consultant` is ready first: it takes 0.58.0 and Vibe-on-Linux 0.59.0 (DL14).
- **Q7 — A Kimi sign-in on a network filesystem (DL6).** **ANSWERED by the owner 2026-10-19: option A.** A Kimi sign-in folder on
  network storage is refused by default, with the cure (a local `KIMI_CODE_HOME`, signed in separately), and the machine-scoped
  override `coai.kimiAllowNetworkSignIn` admits it with a warning. (Taken as the default 2026-10-17; the alternative — admit with a
  warning and no refusal — was one consultant's choice and is not taken.)
- **Q6 — A survivor blocks the sign-in for coai** (DL9): while a marker names a process coai could not end — or names nothing it can
  match, because the shim died between its `spawning` write and the agent's start — every coai Kimi turn on that sign-in is Busy with
  the pid (or the reason), re-tried on every launch, until the process is gone or a person clears the marker. The person's own `kimi`
  is never touched. Default: accept the availability cost, as the vendor-slot precedent did (`ReaderMarker.cs:89-93`); the
  alternative — a survivor is only reported — leaves two refreshers possible. "Provably gone" for a marker that names nothing is
  concrete (A3 spec, 2026-10-16): it clears by itself only when DL10's listing is readable and shows no `kimi` of this uid — a
  recorded deviation from the precedent, which blocks such a marker for ever. The way out is in the Busy sentence itself — the
  marker's path, the pid(s), safe to delete once those are gone — and, in addition, the card's *Clear the stuck Kimi marker*
  button, which never clears over a living or an unverifiable survivor (DL9, F11; the plan gate for epics 4–6, 2026-10-16, F2 + F12).
- **Q7 — Route the two existing signal registrations through `StopSignals`?** (A3 spec, 2026-10-16) E5 extracts
  `StopSignals.Into` for the `--ask-acp` shim (DL9). `RefusingServeAsync` (`Program.cs:1988-1997`) and the consultant check
  (`ConsultantCheckMode.cs:55-65`) keep their own copies until the owner answers; `ServeStop.Register` (`ServeStop.cs:60-75`) is a
  different shape — four signals, a grace — and is not in the question. Default: not done in E5.

## Definition of Done

- [ ] E1–E6 done-when met; every cell of §5 that gates a shipped epic recorded in `research/RESULTS_vibe_confinement.md` /
      `RESULTS_kimi_confinement.md` (Linux sections), with predictions written before each run.
- [ ] A Vibe row reviews, consults and answers a question row in WSL through the released 0.59.0; the real `~/.vibe` unchanged.
- [ ] 0.59.0 admits `HostKind.Wsl` only; a native Linux host is refused with the WSL2-only sentence (S-V17) until L-V10 is recorded.
- [ ] The scenario harness drives a plan round, a code round, a consult and a question row with a Vibe row on the Linux CI job
      (and the same with a Kimi row in E6); each flow and what it does not prove is in `research/module_tests.md` — E5's Linux
      teardown flows included (A3 spec, 2026-10-16).
- [ ] A failed link falls through to the signed-out cure, its reason carried as the launch's warning (S-V15); a manifest entry
      without `platform` is `win32` and a Windows launch is unchanged (S-V16); the names grammar matches python-dotenv's vectors
      (S-V13).
- [ ] A Kimi row the same through its release; the real `~/.kimi-code` unchanged but for Kimi's own token refresh.
- [ ] Every D18 pin flipped red-first; a `wsl.exe` row on Windows and a macOS host still refused; a WSL1 banner refused for a Vibe
      row (S-V17) and for a Kimi row (S-K20).
- [ ] The `.env` link: exactly one admitted link (its raw target compared), the target judged through one descriptor, the names
      grammar with its four refusal kinds (`NoBinding`, `OtherName`, `NotText`, `ByteOrderMark` — each by line number only), no
      value or unread line ever in a sentence or log, deleters proven around it (green first, teeth by a mutant); L-V8 recorded
      before 0.59.0 is tagged.
- [ ] The lock key is identity-based on Linux (aliases and bind mounts one key, a file no key), the locality rule by mount type
      (`MountTable` with tests of its own), the tree hold proven exclusive and inherited (L-N1 — the native take only if its red run
      confirms the predicted shared lock), an unreadable process listing never read as clear, on Windows as on Linux (A2 spec,
      2026-10-16), the subreaper teardown proven with its re-listing,
      its bound, its identity signalling and its supervisor-failure arms (L-N2), a survivor blocking the next launch, the next shim's
      tree hold and the home disposal (L-N3; the third place, A2 spec, 2026-10-16), the marker written in its phases (`spawning`,
      then `running` naming the agent); a self-exited grandchild reaped and never a survivor (S-K22); no pidfd wrapper → the raw
      syscalls, no pidfd in the kernel → the launch refused with its sentence (S-K21).
- [ ] The install button provisions `rg`/`fd` on Debian/Ubuntu and Fedora and prints the generic instruction elsewhere, makes
      `~/.local/bin` first, links `fdfind` only where `fd` does not resolve, is idempotent, and ends by printing where `rg` and `fd`
      resolve (S-K17); `OFFICIAL_SOURCES` admits exactly those verbs and packages.
- [ ] The Busy sentence always carries the manual way out; a survivor marker can also be cleared from the panel, never over a
      living or unverifiable survivor, with the fixed exit codes 0 / 65 / 74 / 75 (a corrupt marker 74), and a marker that names
      nothing clears only when a readable listing shows no `kimi` (S-K18); the Kimi lock files are retired on Linux, the sweep
      inert on Windows (S-K19).
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
in the configuration boundary `VibeHome` exists for, and 0.59.0 is not tagged until the cell is recorded. *(4) E6 does not ship a
survivor that is only a report.* DL9's eligibility rule (the marker reconciled by the next launch and the home disposal) is E5's,
and E6 depends on it. The release points stand: 0.59.0 after E3, the next minor after E6, DL14's second-merger rule unchanged.
Not changed, and weighed: splitting E5 into two epics (seven epics keep Q = 3 and would make the gate structural — rejected for the
same-files reason above; the §6 wording and E5's done-when carry the gate instead), and moving the installer line (DL12) out of E6
into its own epic (it is one button and its review is Fable's outside the quota either way).

**Marks.** Per the gate's operator command, authentication / security work is BUILT on Fable and everything else on Opus (Sonnet
codes under either; the mark says who writes the scenarios and the brief and who reviews). E1 ordinary · E2 **authentication /
security** · E3 ordinary · E4 ordinary (measurement on the owner's live sign-in) · E5 **authentication / security** · E6 ordinary,
with DL12's `officialSources.ts` verbs and the new `sudo` rule as a security (supply-chain) judgement on Fable, outside the
cornerstone quota.

Cornerstones: 3 of 3 — A1 (E2), A2 (E5), A3 (E5) specified by Fable, 2026-10-16

The three AREAS take the whole quota, so E1, E3, E4 and E6 carry none and a fourth mark goes to the operator, not into a stories
document. All three specs are written (Fable, 2026-10-16), so the line above counts them; each area becomes ONE story `model: fable`
when its epic's stories document is written, carrying its spec, and that commit names the story in the line above in place of the
area.

**The cornerstone areas**, in the consultation's terms after verification — three edges of one thing, the person's credentials in
a coai-owned process: who may be given them (admission), who holds them for how long (ownership), and the proof that nobody holds
them after (teardown). The highest risk is the seam between A2 and A3: a launch returns and its hold is disposed
(`AskAcpMode.cs:166-169`) while a descendant that inherited no descriptor (`HeldFile.cs:23-28`; L-K5) still holds the sign-in, and
the next launch's gate sees nothing. A1 has no such seam, and is a cornerstone because a wrong rule there has no symptom at all.

- **A1 — credential ADMISSION: the one admitted `.env` link (E2).** *Boundary:* everything between "the person's sign-in file
  exists" and "Vibe reads it from an owned home" — the expected-path rule; the descriptor-based judgement of the target (type, owner,
  mode and the capped read on one `O_NOFOLLOW` descriptor, the ported `RegularFiles` leg); the names grammar with its four refusal
  kinds (DL2); the admission of exactly one link in `VibeHome.Judge` (`VibeHome.cs:94-103`), by its RAW target; the link maker's
  place inside the home's turn, before `--version` (`AgainstAsync`, `VibeLaunch.cs:201-211`); and every deleter's behaviour around
  the link (`GitScratch.Unlink`, `GitScratch.cs:116-133`; `VibeHomes.SweepOneShot`; the D20 sweep). Cleanup is INSIDE the area: a
  deleter that follows the link is the same defect as a maker that admits the wrong one. *Minimum scenarios:* S-V1–S-V6, S-V10,
  S-V13, S-V14, S-V15, and L-V6b and L-V8 recorded before the release.
  **Specified 2026-10-16** — A1 spec (session scratch, carried into `todo/PLAN_kimi_and_vibe_on_linux_E2.md` when E2's stories are
  written). Its plan-versus-code corrections are folded into this plan, each tagged "(A1 spec, 2026-10-16)": the probe makes no
  link (DL2), the raw link target (DL2), four refusal kinds (DL2, S-V13, the DoD), line numbers only (S-V5, Q2), S-V13's lone `"`
  refused at line 2, S-V10 green first with a mutant for teeth, the cure's reason as a warning (DL2, S-V15), the `HostKind`
  overload of `VendorDiagnosis.Classify` (DL2), the FIFO sentence (DL2, S-V14), L-V8 predicted "no" by code reading (DL2, §5),
  the harness's host seam (§8), the citations, and the test-local vectors until E1 merges (DL2, S-V13). Checked and unchanged: WSL1
  reads as `HostKind.Wsl` today (`HostKind.cs:53`, through `NamesWsl`), which DL1 already answers; a `.env` FILE is already refused
  (`VibeHomeTests.cs:206`).
  *Review focus:* no byte of a value or of an unread line in any sentence, log or exception; nothing ever follows a link (the
  maker, the judge, the deleters); the target's bytes, mode and mtime unchanged on every cleanup route; the residual (two opens, the
  person's own account) stated in the code where the check is.
- **A2 — credential OWNERSHIP for the tree's whole lifetime: the Kimi lock as one lifecycle (E5).** *Boundary:* the three duties
  `KimiCredentialsLock` already names as one (`KimiCredentialsLock.cs:46-52`) — the server's gate, the inherited sign-in hold, the
  per-home hold — plus what decides whether they may be taken: the identity key (DL7, `dev:ino`; symlink and bind-mount aliases one
  key, a deleted folder no key), the exclusive inheritable take (DL8, L-N1), the mount-type locality rule (DL6, the ported
  `MountTable`), and the foreign-Kimi wait's honesty about `/proc` (DL10). *Minimum scenarios:* S-K2, S-K3, S-K4, S-K5, S-K7, S-K11,
  S-K12, S-K13, with L-K2, L-K4 and L-K6 recorded before the Kimi-dependent half merges.
  **Specified 2026-10-16** — A2/A3 specs (session scratch, carried into todo/PLAN_kimi_and_vibe_on_linux_E5.md when E5's stories
  are written; the two share one seam, named first in both). The A2 spec's plan-versus-code corrections are folded into this plan, each
  tagged "(A2 spec, 2026-10-16)": the shim's tree hold as a THIRD place that reconciles the marker (DL9, S-K14, the DoD); the
  retirement sweep Linux only (DL7, S-K19, §9, the DoD); an unreadable listing on Windows too (DL10, S-K13, §8, the DoD); the
  `statx` mask `STATX_TYPE | STATX_INO`, a directory only (DL7, S-K3); `MountTableTests` written fresh (DL6, §8); the shell's
  `MakeLink` at a path that does not exist (DL6); the stand-in no longer setting `Final` off Windows (§8); the stale `HandleRead`
  remark (DL7); S-K11's `sudo -n` bind mount; the per-pid drain in §9; L-N1's prediction, a shared lock, to be CONFIRMED by its red
  run before the native take ships (DL8, §5). One sentence deleted: DL10's claim that `KimiNames`/`CoaiNames` fit under `comm`'s
  15 characters — irrelevant since L-K6 (the names are matched on the `exe` basename). Checked and unchanged: the
  `HostProcessListTests.cs:75-81` pin and the `HomeWait` lines (`KimiCredentialsLock.cs:69-72`).
  *Review focus:* no path on which "cannot
  tell" reads as "free" (an empty key, an unreadable listing, an unnamed handle each refuse); real shim-to-agent descriptor
  inheritance (not the test host's non-inheriting take); the parent's death with a living child; two spellings of one folder;
  independent server processes on one sign-in; a disposal's `HomeProbe` racing a surviving child (`HomeWait`,
  `KimiCredentialsLock.cs:69-72`).
- **A3 — VERIFIED teardown, and what a survivor does to the next launch (E5).** *Boundary:* DL9 whole — the subreaper, the
  re-listing confirmation (today's `ConfirmGoneAsync` re-checks a fixed list, `ProcessTree.cs:151-167`), the bound (today's agent
  wait has none, `ProcessLauncher.cs:645-650`), signalling by identity through a `pidfd` (the ported native layer), the shim's own
  failure arms (`SIGTERM` registered into the token — today none, `Program.cs:430`; `SIGKILL` covered by the `running` marker
  written right after the start), and the survivor marker reconciled by the next launch's gate, by the next shim's tree hold and
  by the home disposal (`ReaderReconcile` MOVED to the
  runners, `ReaderMarker.cs:114-136`). *Minimum scenarios:* S-K6, S-K14, S-K15, S-K16, S-K18, S-K21, S-K22 (the last three added by the plan gate for epics 4–6,
  2026-10-16), L-N2's arms, L-N3.
  **Specified 2026-10-16** — A2/A3 specs (session scratch, carried into todo/PLAN_kimi_and_vibe_on_linux_E5.md when E5's stories
  are written). The A3 spec's plan-versus-code corrections are folded into this plan, each tagged "(A3 spec, 2026-10-16)": the marker's two
  writes, `spawning` then `running` (DL9, §5 L-N2, S-K16, the DoD); "provably gone" made concrete for a marker that names nothing —
  a readable listing with no `kimi` of this uid, a recorded deviation from the `ReaderMarker` precedent (DL9, Q6, S-K18, the DoD);
  the clear mode's exit codes fixed, a corrupt marker 74 (DL9, S-K18, the DoD); L-N3's survivor through the reconcile seam, since
  nothing ignores `SIGKILL` (§5, S-K14); `StopSignals` extracted from three copies, replacing the old ones asked (DL9, §11 Q7);
  the pidfd port's `"libc"` spelling and wsl_care's `ProcessIdentity` not ported (DL9); S-K21 through a recording fake, its raw
  arm on Linux CI only (S-K21, §8); the Linux teardown flows' `research/module_tests.md` row (§8, the DoD); the per-pid drain in §9;
  the citations `ProcessTree.cs:39` and `AskAcpMode.cs:166-169` (with `HeldFile.cs:23-28` beside it). Checked and unchanged: the
  `ReaderReconcile` move — both server test files already use `CoaiMcp.Runners.Processes`; `Descendants` keeps listing zombies and
  the confirmation filters them.
  *Review focus:* the A2∩A3
  seam above — a survivor must change the eligibility of the next launch, of the next shim's tree hold (`KimiShimHold.Taken`) and
  of `KimiHome.Alive` (`KimiHome.cs:338-346`), never
  only a report line; the marker is written BEFORE the signal and cleared only on a confirmed-empty re-listing; a marker that names
  nothing it can match blocks until a readable listing shows no `kimi`; nothing is ever ended by name.

**Consultation c6e42fd3 — what was taken and what was not.** One line per point; each verified before it was used.
- Three areas as admission / ownership / teardown, the seam between (2) and (3) the highest risk — **taken**; the seam is real:
  the hold is disposed when `AskAsync` returns (`AskAcpMode.cs:166-169`), only inheritors hold it (`HeldFile.cs:23-28`), a
  `close_fds`/`setsid` grandchild inherits nothing (L-K5).
- The `.env` link is a configuration boundary; `VibeHome` validates before every launch, the version probe included — **taken**;
  `VibeHome.cs:12-24`, `VibeLaunch.cs:201-211` (shape before `--version`) and `:214-224` (again after) — lines corrected by the A1
  spec, 2026-10-16.
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
(`RegularFiles`' Linux leg + `FileStatus`; the pidfd native layer) — `MountTable` has none in wsl_care, so its tests are written
here (A2 spec, 2026-10-16). Not ported at all: wsl_care's `ProcessIdentity` record (A3 spec, 2026-10-16). Rewritten rather than ported: the signalling
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
- **F6** — a manifest entry with no `platform` reads as `win32`; the rollback line (0.58.x restores today's behaviour). DL3; S-V16.
- **F8** — the names grammar follows python-dotenv's NAME part (whitespace, `export`, a quoted name), the value never parsed, and is
  checked against python-dotenv's own output as vectors. DL2's grammar bullet; §5 L-V6b; S-V13 rewritten with the `KEY = "v"` and
  `export KEY='v'` rows.
- **F9** — DL5's precedence written out (the row's path, `$UV_TOOL_BIN_DIR`, `~/.local/bin`, `PATH`, the cure) with why the
  installer's folder precedes `PATH`. DL5; S-V11.
- **F10** — 0.59.0 admits WSL only; native Linux refused with a WSL2-only sentence until L-V10; the signed-out cure names the keyring
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

**Plan gate 2026-10-16 (epics 4–6).** coai session 1721dcd4 (branch `feat/wsl-kimi-linux`) reviewed the Kimi epics, E4 measured.
Accepted, and where each landed:
- **F0** — Kimi's admission uses Vibe's `NamesWsl2` predicate; a WSL1 banner is refused for a Kimi row. DL1; S-K20; E6; the DoD.
- **F2** — `--kimi-clear-marker` clears only on POSITIVE proof that every member is gone (`ENOENT`, another start time); any other
  failure keeps the marker and exits with its own code, naming the error. DL9; S-K18; Q6.
- **F4** — the pidfd layer without glibc's wrappers: raw syscalls 434/424; `ENOSYS` refuses the Linux Kimi launch with a sentence;
  both behind a seam. DL9; S-K21; the DoD.
- **F7 + F17 + F10** — the install line: `mkdir -p ~/.local/bin`, the `fdfind` link only where `fd` does not resolve, idempotent,
  and a closing check that prints where `rg` and `fd` resolve; `KimiTools` searches `~/.local/bin` and its folder joins the built
  `PATH`. DL5; DL12; S-K17's three new arms; the DoD.
- **F8** — §5's "no other `kimi`" guard matches the `exe` basename OR `comm`, not `ps -eo comm=`. §3; §5.
- **F9** — the `ReaderReconcile` move touches `src_server`; its tests named. Header scope; DL9; §8; §10; E5.
- **F11 + F15** — the shim reaps its reparented orphans, and the confirmation drops exited entries: a zombie is never a survivor.
  DL9; §5 L-N2; S-K22; §9.
- **F12** — the Busy sentence always carries the manual way out; the button is an addition. DL9; S-K18; Q6; the DoD.
- **F14** — Q3's default: keep the wait, as on Windows (decision 74). §11.
- **F16** — the button budgeted: the one-shot mode and its `.agents/PROJECT.md` entry in E5, the marker report and the card button
  in E6. DL9; E5; E6.

Rejected, with the reasons recorded:
- **F1** — exclusivity is already pinned: L-N1 and S-K4 assert that a second take of the inheritable hold is refused.
- **F3** — the link path is unique per owned home, and `symlink(2)` at a path that does not exist yet is atomic; there is no window.
- **F5** — each gating cell already has its branch in §5 and §6 (recorded, or a re-decision to the owner).
- **F6** — only `kimi` refreshes (L-K4: no descendant of `kimi acp` is another `kimi`); an orphan `kimi` is caught by the
  foreign-Kimi orphan rule (`ForeignKimi.cs:32-36`); a surviving `rg` is a leak, not a credential risk.
- **F13** — the merge order is a fact about merges, not about code; it is executed at release (DL14).

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
- **Outcome (2026-10-16).** Done, with three deviations. The probes and their libraries run on Linux (`351fe208`; new
  `scripts/lib/proc.mjs`, `scripts/lib/vibe-manifest.mjs`; the POSIX cases seen red in WSL first: ENOTDIR from `rmdir` of a
  symlink, the `cmd.exe` junction maker on Linux, a grandchild outliving a timed-out run); `node --test "scripts/**/*.test.mjs"`
  153 tests, Windows 141 passed / 12 skipped, WSL 150 passed / 3 skipped. L-V6b: `scripts/dotenv-name-vectors.mjs` and
  `shared/dotenv-name-vectors.json` (`cd7d56c7`, `e471bf8f`), 48 vectors by DL2's four kinds, Windows and WSL agreeing, the
  implication holding on all 48. L-V7: the `linux` entry written by the script, both runs agreeing (`3582132d`). L-V8: no
  (RESULTS_vibe_confinement §30.8). Deviations: (1) the `linux` entry is STAGED, not in `versions` (DL3 says why, measured);
  (2) L-V6 not run — E1 was told never to read the owner's `.env`, names included; (3) two of DL2's sentences were wider than the
  measurement — a VALUE python-dotenv cannot parse is admitted (it binds nothing), and a NUL is read by python-dotenv (the NotText
  reason is Vibe's) — both folded into DL2. The E1 stories document was not written (E1 ran from this section and §5).

### E2 — Vibe on a Linux host · M · **authentication / security** (the key-file link) — built on Fable · depends on E1

- **Goal.** §2 item 1 in the server.
- **Scope.** DL1 for Vibe (`VibeLaunch.cs:77-82`; `VibeRuntime.cs:80-85`); DL2 (the link maker, the ported descriptor-based reader
  — `RegularFiles`' Linux leg and `FileStatus` into `src_mcp/runners/Files/` with their tests — the names grammar and its four
  refusal kinds, `VibeHome.Judge` by the raw link target, the deleter tests, `VendorDiagnosis.cs:109-112`'s per-host cure through a
  `Classify(string, HostKind)` overload); DL3 (`VibeHomeManifest`, `VibeLaunch.Verdict`/`VersionWarning`, the `.session-index.json`
  name); DL5's Vibe half; L-V8's outcome (a `.env` snapshot exclusion if needed); the harness's `AsyncLocal` host seam for
  `VibeRuntime` (§8); `research/module_runners.md` (A1 spec, 2026-10-16, for the four kinds, the raw target, the overload and the
  seam).
- **Done when.** S-V1–S-V17 green as MTP executables on Windows and Linux CI; every D18 pin for Vibe flipped red-first; `HostKind.Wsl`
  admitted and `HostKind.Linux` refused with the WSL2-only sentence; the scenario harness runs a plan round, a code round, a consult
  and a question row with a Vibe row on the Linux CI job (§8), each flow and its limits written into `research/module_tests.md`.
- **Cornerstone AREA A1 (confirmed by Fable, 2026-10-16).** Credential admission: the one admitted link — see *The cornerstone
  areas* above for its boundary, minimum scenarios and review focus. One story of this epic is marked when its stories document is
  written.
- **Outcome (2026-10-17).** Built on `feat/wsl-vibe-host`, merged into PR #842; coai code gate epic 2 `proceed` (17 findings: 7
  accepted and fixed, 10 rejected with reasons); a Fable security judgement found A1 MET after three fixes; the epic-2 risk consult
  (aa853ecc) closed solved. Linux CI green on every job. **Deviations from the plan's letter, recorded:**
  - **A1 spec §3.2 step 2 was wrong** (found independently by Fable and the risk consultant): the same home's second turn did not
    re-judge the target. Now the target is judged through the one descriptor and its names scanned on EVERY turn; a target changed
    between turns is refused before `--version`; a rotated key stays admitted.
  - The read array is zeroed in a `finally` (the copy `buffer[..total]` had left the original uncleared); a relative or unexpanded
    `VIBE_HOME` is ignored (only an absolute one is the person's Vibe home); a libc without `statx` is a refusal sentence, not an
    exception; a failed open is named by what a person can do (no read permission / path too long / a symbolic link).
  - `VibeLaunch.TakeAsync` takes a `VibeHost(Kind, Wsl, SignIn)`; the `(id, HostKind)` constructor is internal (test-only — it
    carries no sign-in); the manifest platform is normalised where it is set (null or blank → `win32`).
  - DL5 follows uv's own folder order (`UV_TOOL_BIN_DIR`, `XDG_BIN_HOME`, `XDG_DATA_HOME/../bin`, `~/.local/bin`), absolute
    values only, a candidate counts only if it is an executable file.
  - One WSL-banner rule for the server and the extension: `shared/wsl-banner-vectors.json`, read by both test suites.
  - The WSL1 cure says to run `wsl --set-version <distro> 2` from Windows; the legacy `Classify(text, wsl: false)` names the
    process's own host, never Windows.

### E3 — Vibe on Linux in the extension, and the 0.59.0 release · S · ordinary — built on Opus · depends on E2, and on L-V8 recorded

- **Goal.** The panel says what the WSL side does, and the release ships.
- **Scope.** `vendors.ts:294`; `vendorTerminal.ts:478`, `:488-493`; the five help files; `runtimeSince.ts` (`VIBE_ON_LINUX_SINCE`);
  `shared/consultant-limitations.json:252-263` (the Vibe `linux` and `wsl` rows: measured on WSL2, the cells named);
  `runtime-capabilities.json` if its Vibe rows name a host; L-V9; `research/module_extension.md`.
- **Done when.** Extension tests green; L-V9 3 of 3; **L-V8 recorded in `research/RESULTS_vibe_confinement.md` before the tag**;
  the `linux` limitation rows say "measured on WSL2 only" and the `wsl` rows name the cells; the four harness flows of E2 still green
  on the release commit; the live WSL checks of §8 answered through the released binary.
- **Notes from E3's report (2026-10-16).** The extension half shipped on its own branch, `feat/wsl-vibe-extension`. The WSL Vibe
  limitation row says `confined` on n = 1 evidence (the scout's cells, §3) until L-V9 re-records it 3 of 3 before the release.
  Release clean-up owed by E2/E3: three texts still describe Vibe as Windows-only — `research/architecture.md:39` ("the Python
  CLI, Windows only"), `research/README.md` (its Vibe RESULTS rows, "Windows only"), and the `coai.vibeHomesDirectory` help text in
  `src_vs_code/package.json` (it speaks only of the Windows 259-character path budget).
- **Outcome (2026-10-17).** The extension half on `feat/wsl-vibe-extension`, merged into PR #842; an Opus judge (8 findings, one
  BLOCKING) and the coai code gate epic 3 `proceed` (12 findings: 5 accepted and fixed, 7 rejected with reasons). The card knows FIVE
  host kinds (windows | wsl2 | wsl1 | linux | darwin), read once from the kernel banner and required in `PanelState`: the "update
  coai-mcp" note shows on WSL2 only; native Linux and WSL1 get the server's refusal at any version; a host note never greys the stage
  boxes; the card note is proven escaped. L-V9 is recorded 3 of 3 (RESULTS_vibe §31), so the WSL row is `confined` on that record,
  not on n = 1. The release itself (tag, post-deploy, live WSL check) is the open tail of this epic.

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
  end through the ported pidfd layer — with its raw-syscall path and its `ENOSYS` refusal — the shim's per-pid `waitid` drain and the
  confirmation's exited-entry filter, the survivor marker in its `spawning`/`running` phases and its Busy sentence, `ReaderReconcile` moved to
  `src_mcp/runners/Processes` and asked by `KimiLaunch.TakeAsync`, `KimiShimHold.Taken` and `KimiHome.Alive`, `StopSignals`
  extracted — A2/A3 specs, 2026-10-16); DL6's `IJunctionShell.MakeLink` and `WindowsStandIn` (no `Final` off Windows), the
  `MountTableTests` written fresh, `HostProcessList` answering *unreadable* on Windows too (A2 spec, 2026-10-16); the Linux
  teardown flows' row in `research/module_tests.md` (A3 spec, 2026-10-16); **`src_server`** — `ReaderMarker.cs`,
  `SlotRegistry.cs`, `ServerJsonContext.cs` follow the move (the plan gate for epics 4–6, 2026-10-16, F9); the
  `--kimi-clear-marker` one-shot mode — its `Classify` arm and dispatch in `src_mcp/src/Program.cs` and its entry, with its exit
  codes, in `.agents/PROJECT.md`'s one-shot list (F16); DL10 (the extracted `/proc` reader, `HostProcessList` on Linux answering a
  closed union).
- **Done when.** S-K1–S-K7, S-K11–S-K16, S-K19 (DL7's lock-file retirement), S-K21 and S-K22 green on the Linux CI job without the stand-in, and S-K18's Busy-sentence
  and clear-mode arms (exit 0 / 65 / 74 / 75, a corrupt marker 74, the marker kept over a living or unverifiable member, a
  `spawning` marker cleared only by a readable listing with no `kimi` — A3 spec, 2026-10-16); S-K19's Windows arm inert (A2 spec,
  2026-10-16); L-N1 (its predicted shared lock confirmed or refuted by the red run before any native take ships), L-N2 (all arms) and L-N3
  recorded; the `KimiCredentialsLockTests` skips lifted on Linux; `ReaderBarrierTests` and `GaugeReaderCrashHarnessTests` green
  unchanged after the move; `--kimi-clear-marker` listed in `.agents/PROJECT.md` with its exit codes; **and E4's gate met** —
  L-K2, L-K4, L-K6 recorded and DL6/DL8/DL9/DL10 confirmed in this plan — before the Kimi-dependent half merges.
- **Cornerstone AREAS A2 and A3 (confirmed by Fable, 2026-10-16, re-bounded).** A2 credential ownership for the tree's lifetime —
  the lock as one lifecycle, DL6 + DL7 + DL8 + DL10; A3 the verified teardown and the survivor's effect on eligibility — DL9 whole.
  Boundaries, minimum scenarios and review focus under *The cornerstone areas* above. Two stories of this epic are marked when its
  stories document is written; the A2∩A3 seam is named in both specs — specified 2026-10-16 (A2/A3 specs, session scratch,
  carried into todo/PLAN_kimi_and_vibe_on_linux_E5.md when E5's stories are written).

### E6 — Kimi on a Linux host, and its release · M · ordinary — built on Opus; DL12's `OFFICIAL_SOURCES` verbs and the `sudo` rule are a security (supply-chain) judgement on Fable, outside the quota · depends on E5 (DL9's survivor rule included)

- **Goal.** §2 item 2 end to end, and item 3 for Kimi.
- **Scope.** DL1 for Kimi (`KimiLaunch.cs:28-33`, through the same `NamesWsl2` predicate as Vibe — F0); DL5's Kimi half
  (`KimiAcpProfile.cs:135-140`, `KimiPlaces.cs:74`, `KimiTools` with `~/.local/bin` as a searched place — F10);
  DL11 (`KimiEnvironment`); DL12 (`vendorTerminal.ts:528-532`, `:568-572` — with the `~/.local/bin` folder, the guarded idempotent
  `fd` link and the closing check, F7 + F17 + F10 — `officialSources.ts` — the two verbs, `distroPackages`, and the `sudo`
  first-word rule — `KimiTools.cs:24-27`, `:51-61`); the standing-marker report per Kimi row in the server's providers answer and
  the Kimi card's *Clear the stuck Kimi marker* button that runs E5's one-shot mode (F16); `vendors.ts:310`, the help,
  `KIMI_ON_LINUX_SINCE`; `research/module_runners.md`, `module_extension.md` (F-numbers here: the plan gate for epics 4–6,
  2026-10-16).
- **Done when.** S-K8–S-K10 green (S-K10's `sudo rm -rf` observed red against the old parser first), S-K17 with its three new
  arms, and S-K20; the providers answer reports a standing marker for its row and the card's button appears only then and runs
  the mode; the install button run in WSL on a machine (or a `testsToDelete-` prefix) without `rg`; the live Kimi checks of §8
  through the release; this plan promoted.

- **Outcome of the integration (2026-10-18).** The server half (`feat/wsl-kimi-e6-server`), the extension half (`feat/wsl-kimi-extension`) and main (mcp 0.60.0) are merged on `feat/wsl-kimi-linux`; the only conflict was main's `MaxPromptKib` field beside E6's `SurvivorMarker` field on the providers row (both kept). Three things the two halves left open are closed here. (1) **The setting `coai.kimiAllowNetworkSignIn`** (DL6, Q7) had a server reader (`COAI_KIMI_ALLOW_NETWORK_SIGNIN`) and no writer: it is now a boolean, default off, in `package.json`, written to the data directory's `settings.json` only when on, per side, never carried by a config export, with a tick-box on the Kimi card (*Connection*) in a WSL2 or Linux window and a help sentence in five languages. (2) **The card's two server fields were already covered** by the extension half: the survivor marker (`kimiMarkerNote`, the *Clear the stuck Kimi marker* button) and the row `warning` (`versionWarningNote`, which draws the network-storage line); a Kimi-row test for the warning was added. (3) **The version**: `KIMI_ON_LINUX_SINCE` 0.60.0 → 0.61.0 (DL14).

- **Outcome of the review (2026-10-18).** The lead's review of the integrated branch found twelve defects, all verified against the code; each was fixed red-first on `feat/wsl-kimi-linux` (the reds are in `research/module_tests.md`, *Kimi on Linux — the review of 2026-10-18*).
  **Security.** (A) `coai.kimiAllowNetworkSignIn` could be switched on by a workspace's `.vscode/settings.json` and copied by Settings Sync: it is now `"scope": "machine"` AND read through the user-layer door
  (`modelKeys.ts` `SAFETY_KEYS`, beside `MODEL_KEYS`, no second copy of the reading). (B) Turning the override OFF did not re-judge an existing home — the only mount check ran when the home was fresh: `Junctions.Locality` is asked on EVERY
  launch, before any process starts. (C) The D19 spelling check (`/net/`, `/misc/`) refused a home the override had admitted: `Junctions.IsLocalSpelling` follows the override on a Linux or WSL host. (D) `--kimi-clear-marker`
  read, checked and deleted without the sign-in's gate: it takes the gate (exit 74 when it cannot, in 20 s) and deletes only a marker that is still the one judged. (E) The supply-chain guard's observed bypasses (an escalator behind
  `$(`, a backtick, `&`, `<(`; a fetch with no readable admitted URL): the splitter breaks at `& ( )` and the backtick, an escalator is a TOKEN of the raw step text, the one admitted `sudo apt-get|dnf install` is a whole
  construct, a fetcher step must carry an admitted URL, and the line itself mutated at every `$(` is refused. **Correctness.** (F) the `KimiTools` cure said `ln -sf`: it says `ln -sfn … unless ~/.local/bin/fd is already your own file`.
  (G) The `fd` link: a real file or a resolving link of the person's own is left alone, `mkdir -p` moved inside the link branch, the target is absolute and prefers `/usr/bin/fdfind`. *Deviation from the brief:* a link that resolves to a
  FOLDER is still replaced (only one that resolves to a FILE is left alone) — the S2 guard against a planted directory link keeps its meaning. (H) The closing check follows the server's order (Kimi's bin, PATH, `~/.local/bin`);
  DL12's text above now says so. (I) The Kimi card's Linux note and the help in five languages say `sudo apt-get update` once if apt cannot find the packages, and that the tools must be on the PATH the VS Code server sees. (J) The
  override chain is tested link by link (roster, providers row, question fan-out) and was broken once on purpose and restored. (K) `AKimiRowRunsThroughTheServiceOnWsl2Tests` uses a `testsToDelete-` folder. (L) The Clear button says
  each way a run can end without an exit code, every outcome has its own notice code, and the button is disabled and reads *Clearing…* while the run is in flight. *Not done here, on purpose:* the `AsyncLocal` + `Restore` seam copies
  (`KimiHost.cs`, `KimiRuntime.cs` and five older ones) and the twin `{Path, Pids}` records are a separate refactor; 76 older `TempDir.For("coai-…")` prefixes in `src_mcp/tests` still lack `testsToDelete-`.
