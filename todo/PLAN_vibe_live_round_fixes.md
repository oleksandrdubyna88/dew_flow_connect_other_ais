# PLAN — a Vibe home stays under MAX_PATH, survives its second turn, and a refusal says what it is

> Status: **plan only, nothing implemented yet, 2026-10-15.** Scope: every Vibe home site (`src_mcp/runners/Vibe/`,
> `src_mcp/src/Server/Rounds/`, `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs`,
> `src_mcp/src/Server/Consultation/`), the home validation's admitted names (`shared/vibe-home-manifest.json`,
> `scripts/vibe-home-manifest.mjs`), the consult failure kinds (`src_mcp/runners/Consultation/`,
> `shared/consult-failure-kinds.json`, `src_vs_code/src/consultantHealth.ts`), and one MEASUREMENT before any cap on a
> Vibe launch's spend. One pull request, branch `fix/vibe-home-short-path`.
>
> Parent: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (D17 — the owned home, D20 — the conversation
> home sweep). Evidence: [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §22, §24, §26, and the
> three live runs of 2026-10-15 summarised in §1 below (their folder is a `testsToDelete-` folder and will be swept, so
> the numbers this plan rests on are copied here).

## 1. Symptom — four defects, all found by live runs through the RELEASED coai-mcp 0.54.0 (Windows, `LongPathsEnabled = 0`)

**(1) A Vibe home deeper than ~102 characters loses the turn.** Vibe writes its session store under `VIBE_HOME`; the
deepest path measured below a home is the session chunk's temporary file,
`logs\session\unified\<36-char session id>\chunks\.<64 hex>.json.<16 hex>.tmp` — **156 characters**. Windows without long
paths stops a path at **259** characters (MAX_PATH 260 with its terminator), so a home longer than
`259 − 1 − 156` = **102** characters dies with exit 2, `[Errno 2] No such file or directory`, after the launch was
paid for. Measured 2026-10-15, three times:

| Run | Site | Home (length) | Died at | Total |
|---|---|---|---|---|
| live round | round reviewer | `%TEMP%\coai-answers-<12 hex>\vh-<10 hex>` (TEMP + 40) | the chunk `.tmp` | > 259 with a TEMP ~29 characters longer than `C:\Users\abcde\AppData\Local\Temp` |
| consult run1 | conversation, `COAI_CONSULTANT_HOMES` under TEMP | `…\testsToDelete-vibe-consult-a7f3\homes\coai-vibe\e9a7ff70…\vh-21f30f2142` (128) | `…\generations\.staging-0000000000000000-b9d869c1dcbfecdd\runtime-state.json` (131 below the home) | **260** |
| consult run2 | conversation, root under TEMP | `…\testsToDelete-vibe-consult-h1\coai-vibe\df1ed776…\vh-21f30f2142` (120) | `…\chunks\.00ef632a….json.7f4842daffb2dac5.tmp` (156 below) | **277** |

With the DEFAULT root (`%LOCALAPPDATA%`) the second scout's longest path was 242 for the 5-character profile name (`abcde` here), so
every site fails for a longer profile name — computed from today's layout (`C:\Users\<u>\AppData\Local` is 23 + u):

| Site | Where the home is today | Home length | Fails from a profile of |
|---|---|---|---|
| round reviewer | `%TEMP%\coai-answers-<12>\vh-<10>` | TEMP + 40 (73 at u = 5) | u ≥ 34, or any TEMP over 62 |
| question row | `%TEMP%\coai-question-<12>\vh-<10>` | TEMP + 41 | u ≥ 33 |
| consultant check | `%TEMP%\coai-check-<32>\own\vh-<10>` | TEMP + 62 (95 at u = 5) | **u ≥ 13** |
| conversation | `%LOCALAPPDATA%\coai-vibe\<32>\vh-<10>` | 80 + u | u ≥ 23 |
| `providers` probe | `%TEMP%\coai-answers-<random>` | TEMP + 26 | never in practice: a bare start writes only `vibehistory` (§24) |

D17 already said "a short coai-owned folder under LocalApplicationData/coai-vibe/ (short: a deep home broke on a
>260-character path, first probe §4)"; S2 then put the ROUND's homes in the round's TEMP scratch
(`RoundScratch.cs:42-43`, `RosterBuilder.cs:451`, `VibeRuntime.cs:89`), and S3 put the question row's and the check's in
theirs (`QuestionRowLaunch.cs:94,148-149`, `ConsultantCheck.cs:211,260`). Nothing measured the depth.

**(2) No Vibe conversation can have a second turn.** Turn 1 makes Vibe create two EMPTY folders the manifest never saw —
`logs\session\plugins\data\<session uuid>\vibe\` and `logs\session\plugins\tmp\` (measured listing below; the manifest
script lists FILES only, `scripts/lib/vibe.mjs:67-79`, so an empty folder was invisible to it). Turn 2's hash check
refuses: *"the coai-owned Vibe home … holds logs/session/plugins/data/, which is not a folder Vibe writes into its home —
coai refuses to start Vibe on it (D17 …)"* — 2 of 2 conversations. By reading, the same refusal hits a ROUND reviewer's
retry and repair, which reuse the reviewer's home (`VibeRuntime.HomeFor` is deterministic per provider and role,
`VibeRuntime.cs:157-158`). Why the hash check and not the shape check: under the pinned prefix `logs/session/plugins/`
the shape check admits any folder by place (`VibeHome.cs:148-149`, `byHash: false`), the hash check only folders that
lead to a pinned file (`VibeHome.cs:150-151`), and no pinned file is under `data/` or `tmp/`.

The measured turn-1 home (`work\c4-inv-after-turn1.json`, vibe 2.26.1, 77 entries) is §24's inventory plus: the three
`chunks/<64 hex>.json` files, `CURRENT`, `generations/0000000000000001/` and `…/0000000000000021/` (each `checkpoint.json`,
`manifest.json`, `projection-state.json`, `runtime-state.json`), `journal/0000000000000001.jsonl` and
`…/0000000000000022.jsonl`, `meta.json`, `scheduled-loops.json` — all under `unified/*/**`, admitted — and the two empty
folders above, which are not.

**(3) That refusal was reported as a missing CLI.** The consult record said `failureKind: cli-not-found` with the cure
"install it with uv: uv tool install mistral-vibe". The launch control's refusal becomes
`ReviewerOutcome.NotStarted(turn.Refusal)` (`ReviewerExecutor.cs:891`), and `ConsultFailures.Ended` maps EVERY
`NotStarted` to `CliNotFound` with the install cure (`ConsultFailures.cs:81`). The person is told to reinstall a CLI that
is installed, and the real cure (the sentence's own: delete the home) is buried.

**(4) A question row spent 2 233 164 input tokens in 299 s.** A `disk` row (`vibe-disk`, `mistral-medium-3.5`) asked
for two markers — one inside the granted root, one in a file OUTSIDE it — ran to Vibe's own `--time-limit 300` (exit 3,
`outcome: deadline`) with 2 233 164 tokens in / 13 804 out (record `fc27ef18…`); a narrower question on the same root
answered in 33 s and ~15 000 tokens. Something looped at the owner's expense. What, is not known: the export (D7) and the
row's home (in its `coai-question-*` folder) were deleted when the row ended, so the tool calls are gone.

## 2. Decisions

**D1 — every home that Vibe writes a session into lives under ONE short root, the consultant homes root, and the
round, the row and the check own and delete theirs.** The root is the existing `PanelSettings.ConsultantHomesRoot`
(`PanelSettings.cs:385-389`: `COAI_CONSULTANT_HOMES`, else `%LOCALAPPDATA%`) — already local, already the place D17
named, already the knob a person with a long profile can point at `C:\coai`. No home lives under TEMP any more, so a
custom TEMP cannot break Vibe. Layout:

| Site | Home | Length at the default root | Fits a profile up to |
|---|---|---|---|
| round reviewer | `<root>\coai-vh\<8 hex round>\<6 hex of provider/role>` | 47 + u | **55** |
| question row | `<root>\coai-vh\<8 hex>\h` | 42 + u | **60** |
| consultant check | `<root>\coai-vh\<8 hex>\h` | 42 + u | **60** |
| conversation | `<root>\coai-vibe\<32 hex id>\h` (was `\vh-<10>`) | 68 + u | **34** (was 22) |
| `providers` probe | unchanged, `%TEMP%\coai-answers-*` | — | its deepest write is `vibehistory` (§24): fails only for a TEMP over 230 characters |

- `coai-vh` is a sibling of `coai-vibe`/`coai-kimi`, **outside `ConsultantHomes.All`** (`ConsultantHomes.cs:24`), so the
  D20 sweep, which walks only those two (`ConsultantHomeSweep.cs:65`), never sees a round's or a row's home — it cannot
  judge one as a foreign or idle conversation folder.
- The round's homes folder is owned by `RoundScratch` beside its TEMP folder and deleted in its `Dispose`
  (`RoundScratch.cs:54-82`), whatever ended the round; the row's and the check's by a disposable `OneShotHome` in their
  `finally`. A crash leftover is removed by the product's six-hour scratch sweep, extended to `<root>\coai-vh\*` (D3).
- The conversation keeps its 32-hex folder name: the D20 sweep reads the record by that name
  (`ConsultantHomeSweep.cs:142`, `ConsultationService.cs:62`), and a 12-hex name (which would fit u ≤ 54) means a prefix
  lookup in the store and a rewrite of `ConsultantHomeSweepTests` — rejected for this fix as a risk to D20 for 20
  characters. Its home is `h` (one Vibe per conversation folder, so the provider/role hash bought nothing there).
- **Settings built by hand** (tests, tools — `ConsultantHomesRoot` empty, `PanelSettings.cs:379-382`): every site keeps
  its home in its own scratch exactly as today, so no test ever writes the machine's `%LOCALAPPDATA%`.
- Rejected: `%SystemDrive%\coai` or `%ProgramData%` (short whatever the profile, but shared by every local user: a home
  holds the session transcript of the reviewed code, so it would need an owner-only ACL and squatting checks — a
  security design of its own); a junction to a short path (D17 refuses links in a home, R2); a `\\?\`-prefixed
  `VIBE_HOME` (would remove the limit for any length, but unmeasured — M-V18 in §6 measures it for a follow-up).

**D2 — and the launch control refuses, before the home is made or anything of Vibe's starts, a home whose deepest
write would pass the limit.** `VibeHomes.BudgetRefusal(home, longPathsEnabled)`: when `LongPathsEnabled` is not `1`
(`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`; read once, `false` off Windows) and
`home.Length + 1 + 156 > 259`, the sentence:

> the coai-owned Vibe home {home} is {n} characters long, and Vibe writes 156 characters below it (its session chunks,
> research/RESULTS_vibe_confinement.md §27) — {n + 157} is past Windows' 259-character path limit, which this machine
> keeps (LongPathsEnabled is not 1), so coai does not start Vibe on it; set COAI_CONSULTANT_HOMES to a short local
> folder such as C:\coai (coai keeps its Vibe homes under it), or enable Win32 long paths (LongPathsEnabled = 1 under
> HKLM\SYSTEM\CurrentControlSet\Control\FileSystem, an administrator's change)

It is the first check after the host and the environment in `VibeLaunch.TakeAsync` (`VibeLaunch.cs:163-167`), before
`HomeMade` (`VibeLaunch.cs:239-251`). With long paths enabled no refusal: CPython is long-path aware (its `python.exe`
manifest, the Python documentation's "Removing the MAX_PATH Limitation") — documented, NOT measured here (this machine has
`0`), and said so in the code comment. No margin over 156: the number is measured, a margin would cost every user the
same characters, and the manifest script prints the deepest path so a new Vibe version's requalification sees a deeper
one (D4). This is what makes (1) impossible to PAY for: a home that would fail is never started on — for the conversation
at a profile over 34, the round over 55, the row and check over 60, the person reads the sentence before any token.

**D3 — the six-hour scratch sweep also takes `<root>\coai-vh\*`.** `VibeHomes.SweepOneShot(homesRoot, cutoffUtc, warn)`:
for each folder directly under `<root>\coai-vh`: a link is said and left alone (never entered); a folder whose
in-process turn (`VibeHomeTurns.TryTake`) or any sub-folder's is held is skipped; one whose newest write — the folder,
its entries, and their entries (a reviewer home's top level) — is older than the cutoff is removed with
`GitScratch.DeleteEvenIfReadOnly` (which clears read-only FOLDERS, §26 defect 2); an error is warned and the next folder
goes on. Called from `PanelService.PruneOldAnswerDirs` (`PanelService.cs:1376-1390`) — the one throttled sweep the round's
build and the consultation beat both reach (`PanelService.cs:122,1758`) — with `settings.ConsultantHomesRoot`, so the
method takes the root as a parameter. Six hours is today's rule for `coai-answers-*`, which held the round homes until
now; nothing runs that long (a reviewer's timeout is minutes, `--time-limit` under it).

**D4 — Vibe's per-session plugin data and its unpack staging are admitted by NAME, with every starter still refused.**
Two globs join the manifest's `varying` (`scripts/vibe-home-manifest.mjs:63-73` and the same two lines in
`shared/vibe-home-manifest.json`'s `varying` — the field is a verbatim copy of the script's constant, so this is exactly
what `--write` would produce; `versions` is untouched and no paid run is needed):
`logs/session/plugins/data/*/**` and `logs/session/plugins/tmp/**`. In `VibeHome.Rules.FileProblem`
(`VibeHome.cs:131-135`) a path that matches a varying glob is judged as varying BEFORE the pinned-prefix branch, and a
varying file under the pinned prefix is still put through `VibePluginStarters.Problem` (`VibePluginStarters.cs:39-42`)
on BOTH checks — a `hooks.json`, `hooks/hooks.json`, `ai.mistral.vibe/hooks.toml`, or an `mcp.json`/`.mcp.json`/foreign
manifest that declares a server or a hook is refused there with the starter's clause; anything else under those two
folders is data. Folders already lead correctly (`VibeHome.Leads`, `VibeHome.cs:194-195`). The manifest script's
inventory also lists FOLDERS (an empty folder as `path/`), classified like files, so the next Vibe version's new empty
folder stops the script instead of reaching a user; its summary prints the longest relative path below the home.

**D5 — a launch-control refusal is its own failure kind, `launch-refused`.** `ReviewerOutcome.NotStarted` gains
`bool ByLaunchControl = false` (`ReviewerExecutor.cs:234`); `LaunchBuiltAsync` sets it from the turn
(`ReviewerExecutor.cs:891`); `LaunchTurn.Refused(string reason, bool cliMissing = false)` (`LaunchTurn.cs:34`) carries
the one case that IS a missing CLI — Vibe's `--version` that could not be started (`VibeLaunch.cs:265-268`, through
`HomeVerdict`), which stays `cli-not-found` with the install cure. `ConsultFailures.Ended` (`ConsultFailures.cs:81`):
`NotStarted { ByLaunchControl: true }` → new `ConsultFailure.LaunchRefused(Said)`, kind `launch-refused`,
`What` = "coai did not start the consultant ({vendor}): {Said}", `Cure` = "the sentence names what coai's launch check
found and its cure — the CLI itself is installed"; every other `NotStarted` stays `CliNotFound`. The catalogue
(`ConsultFailure.cs:17`, `shared/consult-failure-kinds.json` with label "Refused by coai's launch check",
`givesTheCallBack: true` — not the caller's doing) and the extension's label map (`consultantHealth.ts:343`) gain the word
together; their parity tests (`ConsultFailureTests`, `consultantHealth.test.ts`) hold both halves. A round's reviewer line
already prints the sentence (`BoundedScheduler.cs:747`, "not started: …") and needs nothing.

**D6 — no cap on a Vibe launch's spend is coded before it is measured** (§6, M-V17). The decision table for the cap is
in §6; its code joins this pull request only if the measurement lands before the code round, else it is extracted into a
fresh `todo/` plan when this one is promoted.

## 3. Files, and the change in each

| File | Change |
|---|---|
| `src_mcp/runners/Vibe/VibeHomes.cs` (new) | `OneShotFolder = "coai-vh"`, `DeepestChild = 156`, `PathLimit = 259`; `OneShotIn(root, id)` (pure: `<root>\coai-vh\<id>`), `ReserveOneShot(root)` (`""` when root is empty; else a fresh 8-hex name not present), `BudgetRefusal(home, longPaths)` (pure), `SweepOneShot(root, cutoff, warn)` (D3); `OneShotHome(folder, note) : IDisposable` (deletes the folder best-effort, says a failure; nothing for `""`). Every method CC ≤ 4. |
| `src_mcp/runners/Platform/LongPaths.cs` (new) | `LongPaths.Enabled` — the registry value read once under `OperatingSystem.IsWindows()`, `false` elsewhere or on any read failure. |
| `src_mcp/runners/Vibe/VibeRuntime.cs:157-165` | `HomeFor(owned, provider, role)` → `owned\<6 hex of provider/role>` (was `vh-<10>`); doc comment cites this plan. `:24-30` remarks updated (homes under the homes root). `:103` probe unchanged, comment says why (D1 table). |
| `src_mcp/runners/Vibe/VibeConsultant.cs:65` | the home is `Path.Combine(owned, "h")` — a new `VibeRuntime.ConsultantHome(owned)`; remarks `:20-24` updated. |
| `src_mcp/runners/Vibe/VibeLaunch.cs:161-171` | `TakeAsync(…, bool? longPaths = null)`; the `before` chain gains `() => VibeHomes.BudgetRefusal(Value(request, HomeVariable), longPaths ?? LongPaths.Enabled)` after the environment check. `:265-268` the Win32 path marks the verdict `CliMissing`; `HomeVerdict` (`:189-195`) gains `bool CliMissing { get; init; }`, `Given` passes it to `LaunchTurn.Refused`. |
| `src_mcp/runners/Reviewers/LaunchTurn.cs:22-34` | `Refused(string reason, bool cliMissing = false)`, property `CliMissing`. |
| `src_mcp/runners/Reviewers/ReviewerExecutor.cs:234,891` | `NotStarted(string Reason, bool ByLaunchControl = false)`; `new NotStarted(turn.Refusal, ByLaunchControl: !turn.CliMissing)`. |
| `src_mcp/runners/Consultation/ConsultFailure.cs:17,188-195` | kind `LaunchRefused = "launch-refused"`; record `LaunchRefused(string Said)` beside `CliNotFound`. |
| `src_mcp/runners/Consultation/ConsultFailures.cs:81` | `NotStarted { ByLaunchControl: true } n => new ConsultFailure.LaunchRefused(Safe(n.Reason))` before the existing arm. |
| `shared/consult-failure-kinds.json`, `src_vs_code/src/consultantHealth.ts:343` | the word and its label (D5). |
| `src_mcp/runners/Vibe/VibeHome.cs:131-151` | D4: varying before pinned; starters applied to a varying file under the pinned prefix on both checks. Remarks `:17-24` updated. |
| `shared/vibe-home-manifest.json`, `scripts/vibe-home-manifest.mjs:63-78` | the two globs (D4); the script's inventory lists folders, prints the deepest path. |
| `scripts/lib/vibe.mjs:67-79` | `files(dir, { folders: true })` lists every folder as `rel/` too (default unchanged for the probe script). |
| `src_mcp/src/Server/Rounds/RoundScratch.cs:25-82` | `Reserve(note, homesRoot)`: also `Homes = VibeHomes.ReserveOneShot(homesRoot)`; `Dispose` deletes `Homes` after `Directory`, the same best-effort sentence. `None.Homes` is `""`. |
| `src_mcp/src/Server/Rounds/RosterBuilder.cs:106,451` | `Reserve(…, _settings.ConsultantHomesRoot)`; `OwnedScratch = scratch.Homes.Length > 0 ? scratch.Homes : scratch.Directory`. |
| `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:75,94-121,148-149` | ctor gains `string homesRoot = ""`; for a home-keeping row a `using var home = new OneShotHome(VibeHomes.ReserveOneShot(homesRoot), …)`, and `Owning` passes `home.Folder` when non-empty (else the scratch, today's). The copies and `cwd` stay in the TEMP scratch. |
| `src_mcp/src/Server/QuestionConsult/QuestionFanOut.cs:64` | `new(executor, ledger, log, launcher, panel.ConsultantHomesRoot)`. |
| `src_mcp/src/Server/Consultation/ConsultantCheck.cs:200-212,258-260` | the turn holds a `OneShotHome` from `settings.ConsultantHomesRoot` and passes it as `OwnedScratch` (else `place.Folder`, today's); disposed with the turn. |
| `src_mcp/src/Server/PanelService.cs:122,1376-1390,1758` | `PruneOldAnswerDirs(string homesRoot)` also calls `VibeHomes.SweepOneShot(homesRoot, now − 6 h, warn)` inside the same throttle; both callers pass `settings.ConsultantHomesRoot`. |
| `src_mcp/src/Server/PanelSettings.cs:374-389` | the doc says the root now holds every Vibe home (`coai-vh` and `coai-vibe`) and why it must be short; the variable keeps its name. |
| docs | `PLAN_vibe_and_kimi_runtimes.md` D17 amended (dated 2026-10-15: the layout, the budget, the two globs) and D20 (the sweep never sees `coai-vh`); `research/module_runners.md` / `module_server.md` Vibe sections; `research/RESULTS_vibe_confinement.md` §27 (the three live runs of §1 — the scout's write-up, if not already written). |

## 4. Scenarios — each a test written FIRST and watched red for the stated reason

New test folders are `TempDir.For("testsToDelete-vh-<what>-")` (`.agents/conventions/common/test-leftovers.md`). A new
member is first written as a stub (`=> string.Empty`, `=> 0`, a parameter that is ignored) so the test compiles; the red
below is then the assertion, never a compile error.

### A. The path budget and the layout (defect 1)

| # | Given / When / Then | Expected red on today's code (or the stub) |
|---|---|---|
| A1 | **Given** a home of 103 characters and long paths off, **when** `VibeHomes.BudgetRefusal(home, false)`, **then** the sentence names the home, `103`, `259`, `COAI_CONSULTANT_HOMES` and `LongPathsEnabled`. | stub: `Expected string to contain "COAI_CONSULTANT_HOMES" … but found ""` |
| A2 | Boundary: 102 characters → empty; 103 → refused. | 103 case red as A1 |
| A3 | A 300-character home with long paths ON → empty. | green from the stub — kept as the guard of the other branch |
| A4 | **Given** a Windows host, the built request and a home under `testsToDelete-vh-budget-*` padded to 120 characters, long paths off, **when** `VibeLaunch.TakeAsync(…, longPaths: false)`, **then** it is `Refused` with A1's sentence, the home folder does NOT exist and the fake launcher was called **0** times. | `Expected launcher calls to be empty, but found 1` (`--version` ran) and the folder was created |
| A5 | **Given** the default-root shape `C:\Users\<u>\AppData\Local` (a string, nothing on disk), **when** each site's home is built — `HomeFor(OneShotIn(root, "a1b2c3d4"), "vibe", "PlanCritique")`, `ConsultantHome(OneShotIn(root, …))`, `ConsultantHome(ConversationFolder(root, Vibe, <32 hex>))` — **then** `BudgetRefusal(home, false)` is empty at u = 55 / 60 / 34 and non-empty at 56 / 61 / 35. | today's `vh-<10>` names: `Expected "" but found "the coai-owned Vibe home … is 107 characters long …"` (round at u = 55) |
| A6 | **Given** `TEMP` pointed at a 150-character `testsToDelete-vh-longtemp-*` folder (process env, restored in `finally`; the class is a non-parallel collection), **when** a round, a question row and a check reserve their homes with a homes root of `testsToDelete-vh-root-*`, **then** no home starts with `Path.GetTempPath()`. | round: `Expected … not to start with "<temp>" but … coai-answers-…\vh-…` |
| A7 | Round: **given** `RoundScratch.Reserve(note, root)`, **then** `Homes` is `<root>\coai-vh\<8 hex>`; a reviewer home made in it holding a READ-ONLY folder (`logs\session\plugins\packages\6b\x\skills\vibe`, attribute set) is gone after `Dispose`, and so is `Directory`. | stub ignoring the root: `Expected Homes to start with "<root>\coai-vh" but found ""` |
| A8 | Round end to end (`AVibeReviewerRunsThroughAFakeVibeTests` shape, `PlayedVibe`): the `VIBE_HOME` the fake Vibe received is under `<root>\coai-vh\`, at most 102 characters, and the folder is gone when the round has ended. | `VIBE_HOME` is `…\coai-answers-…\vh-…` |
| A9 | Question row (`QuestionRowLaunch` with `homesRoot`): `VIBE_HOME` is `<root>\coai-vh\<8>\h`; after an answer, after a launch-control refusal and after a cancellation the `<8>` folder is gone. | `VIBE_HOME` under `coai-question-*` |
| A10 | Consultant check: the same as A9 for one check. | `VIBE_HOME` under `coai-check-*\own\vh-…` |
| A11 | Conversation: `VIBE_HOME` is `<root>\coai-vibe\<id>\h`, the same on turn 2. | `…\vh-21f30f2142` |
| A12 | Settings built by hand (`ConsultantHomesRoot` empty): round, row and check homes stay inside their own scratch, and nothing is created under `%LOCALAPPDATA%\coai-vh`. | green from the start — the guard that no test writes the machine's folder |

### B. The sweeps (crash leftovers, and D20 leaving a round home alone)

| # | Given / When / Then | Expected red |
|---|---|---|
| B1 | **Given** `<root>\coai-vh\<8>\<6>` holding a read-only plugin folder, every write time set seven hours back, **when** `SweepOneShot(root, now − 6 h, warn)`, **then** the folder is gone and the count is 1. | stub `=> 0`: `Expected directory not to exist` |
| B2 | The same home written one hour ago → kept. | green from the stub; red if B1's fix ignores the age |
| B3 | An old home whose `VibeHomeTurns` turn this test holds → kept this pass, gone on the next once the turn is given back. | removed while held, once B1 is naive |
| B4 | An old entry under `coai-vh` that is a LINK (a junction on Windows, a symlink elsewhere) to a folder holding a file → the link is left, warned, the target and its file untouched. | target emptied, once B1 is naive |
| B5 | **Given** a homes root with `coai-vh\<8>\<6>` (no stamp, written eight days ago) and no records, **when** the D20 sweep `ConsultantHomeSweep.Sweep` runs, **then** the round home exists and the count is 0. | green from the start; break-it: add `VibeHomes.OneShotFolder` to `ConsultantHomes.All` → red (`ForeignAge` passed, the folder is removed) — record that observation |
| B6 | `PanelService.ForgetTheLastSweep()`, then `PruneOldAnswerDirs(root)` → B1's leftover is gone (the wiring, through the one throttle). | the leftover stays |

### C. A second turn starts on the home its first turn left (defect 2)

| # | Given / When / Then | Expected red |
|---|---|---|
| C1 | **Given** `VibeHomeTests.TheInventoryVibeWrites()` plus the measured turn-1 extras — the EMPTY folders `logs/session/plugins/data/193149ad-d8e5-069e-8f34-aece24b2623d/vibe/` and `logs/session/plugins/tmp/`, and the `chunks/`, `generations/0000000000000001/…`, `journal/…`, `meta.json`, `scheduled-loops.json` files of §1 — **when** the hash check `VibeHome.Refusal(home, Manifest, Entry)` runs, **then** it is empty. | `Expected string to be empty, but found "the coai-owned Vibe home … holds logs/session/plugins/data/, which is not a folder Vibe writes into its home — …"` |
| C2 | The same home through the shape check `VibeHome.Refusal(home, Manifest)` → empty. | green today — kept so both checks are pinned |
| C3 | Under `plugins/data/<uuid>/vibe/`: a `hooks/hooks.json` → both checks refuse with "hook"; an `.mcp.json` holding `{"mcpServers":{"x":{"command":"y"}}}` → both refuse with "start"; a `state.json` holding `{}` → admitted. | after a naive D4 (admit by name only): the two starters are admitted — `Expected string to contain "hook" but found ""` |
| C4 | Under `plugins/tmp/`: `pkg/x.partial` admitted; `pkg/hooks.json` refused with "hook". | as C3 |
| C5 | The shipped manifest's `varying` contains both globs and equals `scripts/vibe-home-manifest.mjs`'s `VARYING` (read from the script's text, as `TheProbeScriptsMinimalEnvironmentIsTheProductsTests` reads its script). | `Expected varying to contain "logs/session/plugins/data/*/**"` |
| C6 | Repair on the same home: **given** a home holding C1's inventory and a fake launcher answering `--version` with `vibe 9.9.9` (the test's manifest entry), **when** `VibeLaunch.TakeAsync`, **then** the turn is `Holding`, not refused. | refused with the `plugins/data/` sentence |
| C7 | Conversation (`AVibeConsultationKeepsOneSnapshotTests` shape): a fake Vibe whose turn 1 also makes the two empty folders; turn 2 starts with `--resume <id>` and answers. | turn 2 fails: "holds logs/session/plugins/data/" |
| C8 | `node --test scripts/lib/vibe.test.mjs`: `files(dir, { folders: true })` lists an empty folder as `a/b/`, and `files(dir)` does not. | the empty folder is missing |

### D. A refusal is named as what it is (defect 3)

| # | Given / When / Then | Expected red |
|---|---|---|
| D1 | `ConsultFailures.Classify(vibe, Ended(new NotStarted("the coai-owned Vibe home … holds logs/session/plugins/data/ …", ByLaunchControl: true)), "vibe")` → kind `launch-refused`, `What` carries the sentence, `Cure` does NOT contain `uv tool install`. | stub flag ignored: `Expected Kind to be "launch-refused", but found "cli-not-found"` |
| D2 | `NotStarted("'agy' could not be started")` (flag false) → still `cli-not-found` with the vendor's install command (the existing `ACliNotStarted_OrNotOnThePath_IsCliNotFound_WithTheVendorsOwnInstallCommand` stays green). | — (guard) |
| D3 | `ReviewerExecutor` with an adapter whose `TakeTurnAsync` answers `LaunchTurn.Refused("x")` → `NotStarted { ByLaunchControl: true }`; with `LaunchTurn.Refused("x", cliMissing: true)` → `false`. | `Expected ByLaunchControl to be true, but found false` |
| D4 | `VibeLaunch.TakeAsync` with a launcher that throws `Win32Exception` on `--version` → `CliMissing` true, and classified `cli-not-found` with "uv tool install mistral-vibe". | `Expected CliMissing to be true` |
| D5 | The catalogue: `ConsultFailureTests`' reflection test and `src_vs_code/src/test/consultantHealth.test.ts` pass with `launch-refused` on both sides. | the reflection test names the word missing from the JSON |
| D6 | End to end: a Vibe conversation turn on a home holding a `hooks.toml` → the record's `failureKind` is `launch-refused`, the answer quotes the home sentence and never "install it with uv". | `failureKind` `cli-not-found` |

## 5. Commands

```powershell
cd D:\rsd\_wt\coai-vibe-maxpath
dotnet build src_mcp/tests/CoaiMcp.Tests.csproj
# red first, per group, then green:
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*VibeHomesTests" --filter-class "*VibeHomeTests" --filter-class "*VibeLaunchControlTests"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ConsultFailureTests" --filter-class "*ConsultantHomeSweepTests" --filter-class "*AVibeReviewerRunsThroughAFakeVibeTests" --filter-class "*AVibeConsultationKeepsOneSnapshotTests" --filter-class "*AVibeQuestionRowReadsACopyOfItsRootsTests"
node --test scripts/lib/vibe.test.mjs
# the whole suites before the code round (never `dotnet test`):
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
npm --prefix src_vs_code test
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore
node .agents/conventions/tools/plan-lifecycle.mjs
```

A test that sets `TEMP` (A6) runs in a non-parallel collection and restores the variable in `finally`. No test may write
`%LOCALAPPDATA%`: every homes root in a test is a `testsToDelete-` folder.

## 6. Measurement before any cap — M-V17, the question row that spent 2.2 M tokens (a scout's brief)

**Question.** What did Vibe do for 299 s and 2 233 164 input tokens on the `disk` row of record `fc27ef18…`, and does
Vibe offer a cap that ends such a run with an export? Nothing in this plan's code depends on the answer.

**Predictions, written before the run.** H1 (most likely): the model retried `read_file` on the path outside the granted
root in a loop — every retry `tool_denied`, every request resending the whole history (~20 000 tokens × ~100 requests).
H2: it re-read the granted files repeatedly. H3: Vibe's own compaction or planning looped. The scout writes which held.

**Method** (Windows, the RELEASED server, nothing of the owner's installation; every folder `testsToDelete-vibe-qloop-<id>`):
1. Free: `vibe --help` and `vibe -p --help` — record every flag that bounds a run (a turn, step, request or price limit:
   `--max-turns`, `--max-price` or whatever 2.26.1 calls them), verbatim.
2. Free: capture the product's exact launch — a scratch `COAI_DATA_DIR` whose one `vibe` row's `executablePath` is a
   recording stand-in (`vibe-recorder.cmd` → a node script writing argv, the environment names, cwd and stdin to a file
   and printing nothing), then `ask_consultants` with run3's question and context VERBATIM (both are in the record above).
3. Paid, arm A, **n = 1**: the real `vibe` with the recorded argv/stdin/cwd (the cwd re-made as the product makes it — a
   copy of the granted root), the BUILT environment (minimal set + `VIBE_HOME` = a fresh SHORT folder such as
   `C:\t\vq1`, `VIBE_CLI=python`, `VIBE_ACTIVE_MODEL=mistral-medium-3.5`, `PYTHONUTF8=1`), `--time-limit 90` instead of
   300, `--output-dir` kept. Keep the export AND the home. From `export.json` and
   `logs/session/unified/<id>/journal/*.jsonl` record: the number of model requests, each tool call (name, argument,
   `tool_denied` / `tool_failed` / ok), the input tokens per request where the journal has them, and the first repeated
   call. If the loop is visible, stop here (n = 1 is enough to DIAGNOSE, as §26 did).
4. Paid, arm B, n = 1: the same with the question narrowed to the granted root only (the control: ~15 000 tokens).
5. Paid, arm C, **n = 3, only if step 1 found a bounding flag**: arm A with that flag at a small value — the exit code,
   whether the export is written, its `outcome`, the tokens. This is the cell a cap would rest on.
6. Never `--trust`, `--add-dir`, a home under TEMP, or the owner's `~/.vibe`. Budget: stop when the arms together pass
   1 000 000 input tokens, and say so.

**Output.** `research/RESULTS_vibe_confinement.md` §28 (method, predictions against outcomes, the tool-call table, the
flags verbatim) and the evidence folder named in it.

**Decision table for the cap (D6) — the code is written only after §28 exists:**

| What §28 shows | What the fix does |
|---|---|
| a bounding flag, arm C 3 of 3 ending with an export and a readable outcome | every Vibe launch whose prompt is not a review (question row, consult turn) gets the flag at max(10, 3 × arm B's request count); the outcome is read as a failure with its own sentence ("Vibe stopped at coai's turn cap (N) — the question may name a file outside the granted folders"); scenarios red-first like §4 |
| no flag, H1 holds | not decided here: the question goes to the owner with the three options and their costs — a lower question-row `--time-limit`, a question that names a path outside the row's granted roots refused or flagged before launch, or a prompt line telling the row a denied read is final (each a measured arm before it ships) |
| neither H1 nor a flag | §28 is the deliverable; a fresh `todo/` plan carries what it found |

## 7. What grows, who retires it, what an interruption leaves

- **`<root>\coai-vh\`** — per Vibe reviewer ~85 KB of unpacked plugins plus its session (~130 KB for two short turns,
  §22, §24), so ≈ 0.25 MB × the round's Vibe reviewers, one folder per round, row or check. Retired by its owner when the
  round, row or check ends (A7–A10); a crash leftover by the six-hour sweep (B1, D3), which runs at most every ten minutes
  on a round's build and on the consultation beat. A server killed with a Vibe child alive leaves the child writing until
  its `--time-limit`, minutes — far inside six hours.
- **`<root>\coai-vibe\<id>\h`** — unchanged in size and owner (D20). A 0.54.0 conversation's `vh-<10>` home is left inside
  its folder and goes with it (D20); its next turn starts on an empty `h` and fails its `--resume` as the exit it is —
  no regression: under 0.54.0 no second turn could run at all (defect 2).
- Nothing new in TEMP; the probe's home is unchanged.

## 8. Build order

1. **D** (failure kind) — smallest, independent: D1–D6, then the JSON and the extension label.
2. **C** (the two globs, the starters on varying, the script's folder listing) — C1–C8.
3. **A** (`VibeHomes`, `LongPaths`, the budget in the launch control, then each site: round, row, check, conversation) —
   A1–A12, in that order.
4. **B** (the one-shot sweep and its wiring) — B1–B6, with B5's break-it observation recorded in the summary.
5. Docs (§3's last row), `plan-lifecycle.mjs`, the whole suites.
6. M-V17 (§6) is a scout's, in parallel with 1–5; the cap only after §28, per D6.

## 9. Not in scope

- The conversation folder's 32-hex name (D1: kept for D20; u ≤ 34, then the budget refusal).
- The round's `--output-dir` export folder (`%TEMP%\coai-answers-*\vx-<10>\export.json`, TEMP + 52) and a reviewer's
  `read_file` of a deep file in a snapshot under a long TEMP — the first fails only for a TEMP over 207 characters, the
  second fails ONE read (`tool_failed`), not the run.
- Kimi's homes (`coai-kimi`): a 280-character session path was written and read back (D19), so its depth is not Vibe's.
- WSL and Linux (D18 refuses Vibe there).

## 10. Definition of Done

- [ ] Every scenario of §4 was watched red for the stated reason before its fix, then green; the summary quotes each
      group's first red message and the green run.
- [ ] No Vibe home that writes a session is under TEMP; with the default root every site fits the budget up to the
      profile lengths in D1's table, and past them the launch is refused BEFORE the home is made, with D2's sentence.
- [ ] A round, a question row and a check delete their homes on every way out; a crash leftover under `coai-vh` older
      than six hours is swept; the D20 sweep never touches `coai-vh` (B5, with its break-it).
- [ ] A conversation's second turn and a reviewer's repair start on the home the first launch left (C6, C7); a starter
      under `plugins/data` or `plugins/tmp` is still refused.
- [ ] A launch-control refusal is `launch-refused` everywhere a failure kind is written; a CLI that cannot be started is
      still `cli-not-found` with its install cure.
- [ ] `CoaiMcp.Tests.exe` (whole), `npm --prefix src_vs_code test`, `node --test scripts/lib/vibe.test.mjs`,
      `dotnet format --verify-no-changes` and `node .agents/conventions/tools/plan-lifecycle.mjs` all clean.
- [ ] D17 and D20 of the parent plan amended (dated 2026-10-15), the module docs updated, RESULTS §27 records the three
      live runs of §1.
- [ ] M-V17 either recorded as RESULTS §28 with the cap built per D6's table, or its open half extracted into a fresh
      `todo/` plan when this plan is promoted.
- [ ] One live Vibe round, one two-turn `consult` and one question row through the built server on Windows, each
      answered, recorded in RESULTS (the parent plan's DoD row this fix unblocks).
