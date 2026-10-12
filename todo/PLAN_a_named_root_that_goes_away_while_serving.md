# PLAN — a named data folder that goes away while the server runs is never made again by a write

> Status: **plan only, nothing implemented yet (2026-10-12).** Scope: `src_mcp` — every place the server makes a folder
> under the data directory (29 `Directory.CreateDirectory` calls in `src_mcp/src` at `origin/main` 2540b973, plus the
> ledger's `JsonlLedger.CreateDirectoryOf`).
>
> Related docs: [PLAN_names_and_a_missing_root.md](../research/PLAN_names_and_a_missing_root.md) (where this was left
> open), [PLAN_paths_per_side.md](../research/PLAN_paths_per_side.md) (D3), [module_server.md](../research/module_server.md).

## The symptom, and the goal

**Today.** Since PR #777 (coai-mcp 0.50.0) no coai-mcp mode creates a NAMED data folder (`COAI_DATA_DIR`) that is
confirmed absent **when the mode starts**: writer modes exit 74, and a server an MCP client starts while the folder is
missing serves only refusals. But the server checks once, at start. If the folder is THERE at start and goes away later —
a network drive unmounted, a USB disk pulled, a share asleep and answering "not found" — the next write makes it again
with a recursive `Directory.CreateDirectory`, for example:

- `RoundsDb.Open` → `Directory.CreateDirectory(dataDir)` (`src_mcp/src/Store/RoundsDb.cs:66`);
- `SessionStore` → `Directory.CreateDirectory(SessionsDir)` (`src_mcp/src/Server/SessionStore.cs:480`);
- `ConsultationStore` → `System.IO.Directory.CreateDirectory(Directory)` (`src_mcp/src/Server/Consultation/ConsultationStore.cs:48`);
- every ledger line → `JsonlLedger.CreateDirectoryOf(path)` (`src_mcp/service_defaults/JsonlLedger.cs:157`, `:172`).

On Linux that writes into the bare mount point (`/mnt/z/coai` with nothing mounted), hides the disconnect, and leaves the
person's later data split between two places — the very thing the owner's D3 forbids. Found by the coai gate's fourth
code round on #777 (finding 3), rejected there as outside that plan's start-time scope, and recorded here.

**When this is done.** While the server runs, no write makes a NAMED root that has gone away: a write under a confirmed-
absent named root is refused with the same sentence the start-time check gives, the tool call that needed it answers an
`McpException` naming the folder, and nothing is created. When the folder comes back, writes resume without a restart.
The default location and a side folder inside an existing root are still made lazily, as today.

## Design (to be decided in the plan round)

The extension already solved this for itself: `src_vs_code/src/dataDirMaker.ts` (`makeDataDirectorySync`, `:63`;
`planFor`, `:81`) makes ONE folder level at a time below a named root and never the root itself, and every writer goes
through it (the inventory test `src_vs_code/src/test/dataDirWriters.ts`). The server's twin:

- **One maker** in `src_mcp/service_defaults` (beside `ResolvedDataDir`), e.g. `DataDirMaker.Make(string dir)`: with a
  named root (`PanelSettings.NamedDataRoot`), it creates only the levels BELOW that root and refuses when the root itself
  is confirmed absent (`QuestionRoots.PresenceOf`, three states — an unknown answer is not refused); with no named root,
  it creates as today.
- **Every writer goes through it** — a scan test over `src_mcp/src` and `service_defaults` that fails on a
  `Directory.CreateDirectory` reaching under the data directory anywhere but the maker (the extension's inventory test,
  ported), with a named allow-list for folders that are not under the data directory (review worktrees, temp files).
- **What a refusal looks like** per caller: a tool call → `McpException` naming the folder (the refusal-only server's
  sentence, already measured to reach the client); a background sweep → skipped and logged once, never thrown; a ledger
  line → counted as lost, as the extension's `recordNotification` does (`landed | held | lost`).

## Stories (to be finalised in the plan round)

1. `DataDirMaker` with its tests: below a named root it makes levels; the root itself confirmed absent → refused; an
   unknown answer → made; no named root → made as today. RED first.
2. The inventory scan test and the allow-list; move every writer under the data directory to the maker.
3. The refusal per caller (tool, sweep, ledger), and a stdio test: start serving with the root present, remove it, call a
   tool that writes → `isError` naming the folder, the root still absent; put it back → the next call succeeds.
4. Docs: `module_server.md`, `module_tests.md`, CHANGELOG under Unreleased.

### The check-to-create race (the code round of the PR that recorded this plan, 2026-10-12, accepted)

A presence check followed by `Directory.CreateDirectory` still races an unmount: the root answers present, the drive
goes away, and the RECURSIVE create makes the root again at the bare mount point. So the maker must not use a recursive
create at all. It creates each level below the named root with a NON-recursive primitive — the operating system's own
`mkdir` / `CreateDirectoryW`, which fails (`ENOENT` / `ERROR_PATH_NOT_FOUND`) when the parent is not there — exactly what
the extension's `dataDirMaker.ts` does with a non-recursive `mkdirSync`. .NET has no non-recursive create, so the maker
needs its own (a small P/Invoke per OS, or creating each level only after opening its parent and treating "parent
missing" as the refusal). A race is then a FAILED write — refused with the sentence — never a recreated root. The
stated guarantee is exactly that; a test plants the race (the root removed between the presence answer and the create,
through an injected file-system seam) and asserts the root stays absent.

### A named root that IS a mount point (CodeRabbit on the PR that recorded this plan, 2026-10-12, accepted)

When `COAI_DATA_DIR` names the mount point itself (`/mnt/z`, `Z:\` on a drive that stays lettered), an unmount leaves
the EMPTY mount-point directory behind: every presence check — the extension's and the server's — answers "present", and
writes land in the bare mount point. Neither half catches it today. Options for the plan round of this work: (a) a
**marker file** (`.coai-data-root`, written when the folder is chosen or first adopted) — a named root without its
marker is "not our folder", refused like an absent one, with a sentence that names the case; (b) comparing the root's
device id with its parent's on POSIX (equal → nothing mounted); (c) refusing a root that is itself a mount point and
asking for a folder inside it. (a) works on every OS and for both halves; it needs a one-time adoption for existing
data folders (one that already holds `coai.db` or `sessions/` is ours). A test: a named root that exists but is empty
and unmarked (the unmounted mount point) is refused by the server and the extension alike, and a marked one is used.

### Added by the plan round of the PR that recorded this plan (2026-10-12, all accepted)

- **The scan cannot pass vacuously.** A companion assertion that the scan FINDS a known writer (for example
  `RoundsDb.Open`'s folder creation routed through the maker, and a planted `Directory.CreateDirectory` in a fixture
  string), as the repository's other scans do (`everyServerModeTheExtensionLaunches.test.ts`'s "the scan reads the
  server's modes").
- **The stdio test goes RED first against today's behaviour**: start serving with the root present, remove it, call a
  writing tool, and see the root made again — recorded — before the maker exists; then GREEN; then a compiling break-it.
- **The scenario is recorded with its coverage** in `research/module_tests.md` (what the stdio test proves, and what it
  does not — a real unmounted drive, a share that sleeps).

## Build order

1 → 2 → 3 → 4, one branch, one pull request, one coai-mcp release.

## Test plan

Every story RED first with its failure message recorded, then GREEN, then a compiling break-it on the deciding line.
`dotnet build dew_flow_connect_other_ais.slnx` and `CoaiMcp.Tests.exe` (never `dotnet test`), `npm run test:seam`, the
family checks.

## Risks

1. **A sleeping share** answers `unknown` — never refused (the start-time rule), so a write there may still fail on I/O,
   as it does today.
2. **Many writers** — the scan test is what keeps a new one from going around the maker.
3. **The extension's own refusal** of its launches already holds; this is the server's half only.

## Definition of Done

- [ ] One server-side maker; no write under the data directory goes around it (scan test).
- [ ] A named root that goes away mid-session is never made again by a write; tool calls refuse with the folder named; writes resume when it is back (stdio test).
- [ ] The default location and side folders still made lazily.
- [ ] `module_server.md`, `module_tests.md`, CHANGELOG; this plan promoted when done.
