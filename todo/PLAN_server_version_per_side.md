# PLAN — the Server section states what is on THIS side's disk, not what somebody installed anywhere

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/{installer,coaiInstall,panelProvider,panelView,extension}.ts`,
> `src_mcp/src/Program.cs` (a `--version` mode), `src_mcp/src/CoaiMcp.csproj` and the `mcp-binaries`
> job of `.github/workflows/release.yml` (stamping and asserting that version).
>
> Related docs: [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md).

## The symptom, measured 2026-09-03 on the machine it was reported from

One person, one machine, two VS Code windows of the same profile: a Windows window and a window
attached to `WSL: Ubuntu`. The WSL window's Server section says

> **coai-mcp 0.12.2 is installed.** 0.12.2 is the newest published — you are up to date.

and there is no button. Nobody ever installed 0.12.2 in WSL. What is actually there:

| measured | value |
|---|---|
| `coai.installedVersion` | present in **one** place only: `%APPDATA%\Code\User\globalStorage\state.vscdb` → `0.12.2` |
| the same key in WSL (`~/.vscode-server/data/User/globalStorage/state.vscdb`) | the file is **0 bytes**; the key is not in it |
| the binary on the Windows side | `coai-mcp.exe`, written 11:52 — one minute after `mcp-v0.12.2` was published (11:51:02Z) |
| the binary on the WSL side | `coai-mcp`, written 10:55, sha256 `2db675ae…` — byte-identical to the **published 0.12.1** (both tarballs downloaded and hashed) |
| what WSL's Claude Code actually launches | `/home/jinx/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais/coai-mcp` (`~/.claude.json:1374`) — i.e. **0.12.1** |

**The cause is a split neither half of the code accounts for.** The version is remembered in
`globalState` ([installer.ts:30-38](../src_vs_code/src/installer.ts#L30-L38)); the binary is written under
`globalStorageUri` ([extension.ts:160](../src_vs_code/src/extension.ts#L160)). In VS Code those two have
**different scopes**: `globalState` is the client's storage, shared by every window of the profile
including remote ones, while `globalStorageUri` is a path on the extension host that is actually
running — `~/.vscode-server/…` in a WSL window. So the install at 10:55 (from WSL, correctly fetching
linux-x64 0.12.1) wrote its record into the Windows-side database, and the press at 11:52 (from
Windows, win-x64 0.12.2) overwrote that same single record. One record, two disks.

**And the panel never looks at the disk.** `serverInstalled` / `serverVersion` are read straight from
that record ([panelProvider.ts:155-156](../src_vs_code/src/panelProvider.ts#L155-L156)), so the claim
"is installed" is a claim about memory. Three consequences, in the order they hurt:

1. **The Update button is unreachable on the WSL side.** It renders only when the published version
   differs from the remembered one ([panelView.ts:406-411](../src_vs_code/src/panelView.ts#L406-L411)),
   and `offerUpdate` is gated the same way ([extension.ts:297-300](../src_vs_code/src/extension.ts#L297-L300)).
   With the record already at 0.12.2 there is no way, from inside the product, to update the 0.12.1
   binary that WSL actually runs. The reviewers keep running last release's server for ever.
2. **A side where nothing was ever installed reports "installed".** A fresh WSL attached from a
   machine that has installed on Windows shows the same sentence and no button, and *Copy the MCP
   config block* ([extension.ts:174-188](../src_vs_code/src/extension.ts#L174-L188)) hands out a path
   that does not exist. What comes back then is an MCP client that cannot start its server.
3. **Nothing in the panel says which side it is talking about**, on a product whose own docs
   ([module_extension.md](../research/module_extension.md), the WSL section) are about a machine with
   two sides.

This is not a regression in 0.26.2: the key has been side-blind since `df25020` (epic 05). It became
visible the day the two sides ended up on different versions. What made it *reportable* is separate
and needs no code: the WSL side is running extension **0.25.2** while Windows has 0.26.2, because a
remote extension host installs its own copy — that one is a VS Code "Install in WSL" press.

## What the gate changed about this plan (round 1, 2026-09-03)

Two of three reviewers answered — codex was rate limited (404 from its backend, after one retry) —
and the round produced nine findings, six accepted. Verdict `proceed` at exactly the threshold.

The one that would have shipped a *second* version of this same bug: **`vscode.env.remoteName` is
generic.** It answers `wsl`, `ssh-remote`, `dev-container` — never `wsl+ubuntu`. Two distros, or two
SSH hosts, would have computed one key again, and the plan's own panel wording quoted a value the API
does not return. The key and the label come from **`remoteAuthority`** instead, and the local key is
spelled so that no authority can ever collide with it.

Also accepted: `updateOffered` must be false when nothing is installed (`offerUpdate` runs at
activation, and a fresh machine must not be nagged to "update" something it does not have); the probe
cache must store **failures** too, or a pre-0.12.3 binary re-spawns a process on every five-second
tick; `stat` is never cached, only the probe result is; and the version is cut at the FIRST `+`,
whatever follows it.

Rejected, with reasons recorded in the session: a retry and a taxonomy of probe failures (no
user-visible action differs between "timed out" and "printed rubbish", and an install re-downloads
the published asset under its own checksum, so pressing Update on an already-current binary rewrites
the same bytes); a spinner for this one probe (that is
[PLAN_panel_probing_state.md](PLAN_panel_probing_state.md), which owns it for every probe in the
panel, and double presses are already joined by `SingleFlight`); and a content hash instead of
`mtime`+`size` (a same-size, same-timestamp replacement by a different version is not a state the
product can produce, and hashing 15 MB on a schedule to defend it is worse than the risk).

## What must be true when this is done

1. **The Server section describes the side the panel is running on.** In a remote window it reports
   the binary under that host's `globalStorageUri`; in a local window, the local one. A record made
   on one side never satisfies the other.
2. **"Installed" means the file is there.** The claim is made from `stat` of the target path, never
   from a remembered value alone. No file → *Install*, whatever any record says.
3. **The version shown is the binary's own answer.** `coai-mcp --version` prints `coai-mcp <version>`
   on stdout and exits 0; the panel asks the file it is about to describe.
4. **A binary that cannot answer is reported as unknown, not as up to date.** Every release up to
   and including 0.12.2 exits 64 on `--version`; that state reads "installed, version unknown" and
   **offers Update** rather than claiming currency.
5. **The published number a release stamps is the number its binary reports.** The release job
   passes the tag's version to `dotnet publish` and the smoke step fails the release when
   `--version` disagrees with the tag.
6. **The update path works from the WSL side.** On the machine in the symptom, after this ships, the
   WSL panel offers Install/Update, and pressing it puts the current linux-x64 build in
   `~/.vscode-server/…` and says so.
7. **The remembered version becomes a per-side fallback, not the truth.** It is keyed by the side it
   was installed on and is used only when the file exists but cannot be asked (a binary the OS
   refuses to spawn — Smart App Control on a freshly written file does exactly this).

## Constraints

- **No stdout in the server except where it is already sanctioned.** `--version` is a person running
  the binary by hand, like `--help`, so stdout is correct for it and for nothing else
  ([Program.cs:11-18](../src_mcp/src/Program.cs#L11)). It must not touch the protocol path.
- **`Classify` stays pure**, and the new mode is a case in it — the existing test covers the shape.
- **The legacy `coai.installedVersion` key is not read.** Its value cannot be attributed to a side
  (that is the whole defect), so adopting it would re-introduce the lie in a quieter form. It is
  left in place, unread; one honest "version unknown → Update" heals it on the first press.
- **No extra process per repaint.** The panel renders on every settings keystroke and every
  five-second watcher tick. The `--version` probe is cached and re-run only when `stat` shows a
  different `mtime`/`size`, or after an install.
- **Reuse, do not re-write, the probe.** `askVersion` already exists
  ([panelProvider.ts:1006-1032](../src_vs_code/src/panelProvider.ts#L1006-L1032)) with the 8-second cap and the
  "nothing rather than an error" contract, and `parseCliVersion`
  ([cliVersions.ts:84](../src_vs_code/src/cliVersions.ts#L84)) already extracts a version from a CLI banner.
  Extract the former to a module both callers use; do not add a second spawn helper.
- **The extension keeps zero runtime dependencies** and the decisions stay in pure, tested functions
  (`coaiInstall.ts`), with the disk and the process in `installer.ts` — the existing split.
- **Native AOT.** Whatever reads the version at runtime must work in an AOT binary; the CI smoke step
  is what proves it, not a local `dotnet run`.

## Build order

1. **RED first, extension side.** A test that states the symptom: given a record made on another
   side and no file on this one, the Server section says *not installed* and renders Install. It
   fails against today's code, which reports the record.
2. **`serverStatus` — the pure decision** in `coaiInstall.ts`:
   `{ fileExists, reported, remembered, published }` → `{ kind: 'absent' | 'known' | 'unknown', version, updateOffered }`,
   with the ordering of rule 3 → 7 → 4. **`updateOffered` is false for `absent`** — an install is
   not an update, and `offerUpdate` runs at activation on machines that have nothing.
3. **Per-side key**: `installedKey(remoteAuthority)` in `coaiInstall.ts` —
   `coai.installedVersion@local` for a local window, `coai.installedVersion@remote:wsl+ubuntu` for a
   remote one, fed from **`vscode.env.remoteAuthority`** (`remoteName` answers a generic `wsl` and
   would put two distros back on one key). The two shapes cannot collide whatever the authority is,
   including empty. `installer.ts` takes the key rather than owning it.
4. **Extract the probe** to `versionProbe.ts` (`askVersion`, unchanged behaviour), used by
   `panelProvider` for the vendor CLIs and by the new server probe.
5. **`installedServer(storage, rid, state, authority)`** in `installer.ts`: `stat` **on every call,
   never cached** → probe, whose OUTCOME is cached against `mtime`+`size` **including a failure**, so
   a pre-0.12.3 binary is asked once rather than on every five-second tick → fallback to the per-side
   record. `panelProvider.render` and `extension.offerUpdate` / `copyConfigBlock` all read this one
   function.
6. **The panel's wording** ([panelView.ts:394-415](../src_vs_code/src/panelView.ts#L394)): three states, and
   the side named from the authority when there is one — *"coai-mcp 0.12.1 is installed in WSL:
   Ubuntu."* / *"…is installed in WSL: Ubuntu but cannot report its version — press Update."* /
   *"coai-mcp is not installed in WSL: Ubuntu."* A local window keeps today's sentence. The label is
   a pure function of the authority: `wsl+ubuntu` → `WSL: Ubuntu`, `ssh-remote+box` → `SSH: box`,
   anything else verbatim.
7. **Server: `--version`.** `Startup.Version` in `Classify`, one stdout line, exit 0. The number
   comes from the assembly's informational version cut at the FIRST `+` (whatever follows it), and
   `<Version>0.0.0</Version>` in `CoaiMcp.csproj` so an unstamped local build says `0.0.0` — never a
   default `1.0.0`, which would read as newer than every published release and suppress the button.
8. **Release: stamp and assert.** `dotnet publish -p:Version=$VERSION` in the `Publish` step, with
   `VERSION` lifted out of the tag before it (it is derived in `Package` today), and the smoke step
   asserts `"$EXE" --version` equals `coai-mcp $VERSION`.
9. **Docs**: `module_extension.md` (the Server section's two-sided reality and the three states),
   `module_server.md` (the new startup mode and where the number comes from).

## Test plan

`src_vs_code`: `npm test` (node:test over the pure modules) —

- the RED case of step 1, kept as the regression test;
- `serverStatus` truth table: no file + record → absent; file + reported → known, no update at
  parity, update when published is newer; file + no answer + record → known-remembered; file + no
  answer + no record → unknown + update offered; `0.0.0` reported → update offered;
- `installedKey`: two different `remoteName`s never collide, and `undefined` (local) is its own key;
- `parseCliVersion('coai-mcp 0.12.3')` → `0.12.3` (the banner shape the server will print);
- the config-block message follows the file, not the record.

`src_mcp`: the MTP executable `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` —

- `Classify(["--version"]) == Startup.Version`, and the existing cases unchanged;
- the version string is non-empty, matches `^\d+\.\d+\.\d+`, and carries no `+` suffix.

By hand, on the machine that produced the symptom, after packaging: the WSL panel offers Install,
the press lands a linux-x64 binary in `~/.vscode-server/…`, `--version` on it prints the release's
number, and the Windows panel is unaffected by any of it.

## Definition of Done

- [ ] The WSL window offers Install/Update for a side it has nothing on, and names the side.
- [ ] `serverInstalled` is derived from `stat`, and `serverVersion` from the binary's own `--version`.
- [ ] A binary that cannot report its version reads "unknown" and offers Update.
- [ ] The remembered version is per-side and used only as a fallback; the legacy key is never read.
- [ ] `coai-mcp --version` exists, writes one line to stdout, exits 0, and is stamped by the release.
- [ ] The release smoke fails when the binary's `--version` disagrees with its tag.
- [ ] `npm test` and `CoaiMcp.Tests.exe` pass, including the RED test from step 1.
- [ ] `module_extension.md` and `module_server.md` record the new behaviour; `todo/README.md` lists
      this plan while it is open.
