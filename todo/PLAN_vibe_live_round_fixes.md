# PLAN — a Vibe home stays under MAX_PATH, survives its second turn, and a refusal says what it is

> Status: **code implemented on branch `fix/vibe-home-short-path`, 2026-10-15 (D1–D8 and scenarios A–F, K built red-first; D7 per the
> security review). Open: the DoD's live Vibe round, two-turn consult and question row through the built server, and the plan's promotion
> to `research/` once they are recorded.** The plan was revised the same day after the plan gate, an independent review and the M-V17
> measurement — see §11. Scope: every Vibe home site (`src_mcp/runners/Vibe/`,
> `src_mcp/src/Server/Rounds/`, `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs`,
> `src_mcp/src/Server/Consultation/`), the home validation's admitted names (`shared/vibe-home-manifest.json`,
> `scripts/vibe-home-manifest.mjs`), the consult failure kinds (`src_mcp/runners/Consultation/`,
> `shared/consult-failure-kinds.json`, `src_vs_code/src/consultantHealth.ts`), one extension setting for the homes root,
> and a turn cap on Vibe consultant launches (measured, RESULTS §28). One more measurement (M-V18, §6) is a follow-up
> nothing here waits for; the cure of the loop's cause is extracted
> ([PLAN_a_vibe_row_is_told_what_its_folders_hold.md](PLAN_a_vibe_row_is_told_what_its_folders_hold.md)). One pull
> request, branch `fix/vibe-home-short-path`.
>
> Parent: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (D17 — the owned home, D20 — the conversation
> home sweep). Evidence: [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §22, §24, §26, and the
> three live runs of 2026-10-15 summarised in §1 below (their folder is a `testsToDelete-` folder and will be swept, so
> the numbers this plan rests on are copied here).

## 1. Symptom — four defects, all found by live runs through the RELEASED coai-mcp 0.54.0 (Windows, `LongPathsEnabled = 0`)

**(1) A Vibe home deeper than 102 characters loses the turn.** Vibe writes its session store under `VIBE_HOME`; the
deepest path measured below a home is the session chunk's temporary file,
`logs\session\unified\<36-char session id>\chunks\.<64 hex>.json.<16 hex>.tmp` — **156 characters**: the persisted
chunk, 134 characters below the home, wrapped by Vibe's atomic write (`.` + name + `.<16 hex>.tmp`, 22 characters). A
transient file: it exists only while it is written, so no inventory taken after a run shows it — it is known from the
failing path of run2 below and from the wrapper's shape. Windows without long paths stops a FILE path at **259**
characters and a FOLDER path at **247**, counted in UTF-16 units — probed 2026-10-15 with Vibe's own interpreter
(`%APPDATA%\uv\tools\mistral-vibe\Scripts\python.exe`, CPython 3.14.6, this machine, `LongPathsEnabled = 0`): files of
258 and 259 characters written and `os.replace`d, 260 → `FileNotFoundError errno 2`; folders of 247 made, 248 and 249 →
errno 2; a 259-character file path holding one `é` (260 UTF-8 bytes) written. So a home longer than
`259 − 1 − 156` = **102** characters dies with exit 2, `[Errno 2] No such file or directory`, after the launch was
paid for. The folder limit is not the binding one: the deepest folder Vibe makes is
`logs\session\plugins\packages\6b\<64 hex>\skills\skill-creator`, 118 characters below the home, which allows a home of
128. Measured 2026-10-15, three times:

| Run | Site | Home (length) | Died at | Total |
|---|---|---|---|---|
| live round | round reviewer | `%TEMP%\coai-answers-<12 hex>\vh-<10 hex>` (TEMP + 40) | the chunk `.tmp` | > 259 with a TEMP ~29 characters longer than `C:\Users\abcde\AppData\Local\Temp` |
| consult run1 | conversation, `COAI_CONSULTANT_HOMES` under TEMP | `…\testsToDelete-vibe-consult-a7f3\homes\coai-vibe\e9a7ff70…\vh-21f30f2142` (128) | `…\generations\.staging-0000000000000000-b9d869c1dcbfecdd\runtime-state.json` (131 below the home) | **260** |
| consult run2 | conversation, root under TEMP | `…\testsToDelete-vibe-consult-h1\coai-vibe\df1ed776…\vh-21f30f2142` (120) | `…\chunks\.00ef632a….json.7f4842daffb2dac5.tmp` (156 below) | **277** |

With the DEFAULT root (`%LOCALAPPDATA%`) the second scout's longest path was 242 for the 5-character profile name (`abcde` here), so
every site fails for a longer profile name — computed from today's layout (`C:\Users\<u>\AppData\Local` is 23 + u, the
default TEMP 28 + u):

| Site | Where the home is today | Home length | Fails from a profile of |
|---|---|---|---|
| round reviewer | `%TEMP%\coai-answers-<12>\vh-<10>` | TEMP + 40 (73 at u = 5) | u ≥ 35, or any TEMP over 62 |
| question row | `%TEMP%\coai-question-<12>\vh-<10>` | TEMP + 41 | u ≥ 34 |
| consultant check | `%TEMP%\coai-check-<32>\own\vh-<10>` | TEMP + 62 (95 at u = 5) | **u ≥ 13** |
| conversation | `%LOCALAPPDATA%\coai-vibe\<32>\vh-<10>` | 80 + u | u ≥ 23 |
| `providers` probe | `%TEMP%\coai-answers-<random>` | TEMP + 26 | only a TEMP over 221: a bare start writes only `vibehistory` (§24) |

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

What the two folders are, from Vibe 2.26.1's own source (`%APPDATA%\uv\tools\mistral-vibe\Lib\site-packages`):

- **`plugins/tmp/`** is the store's staging area (`mistralai_vibe_local_harness/vibe/plugins/_store.py:204`). A package is
  rebuilt there under a fresh random name (`_temporary_path`, `:349-351`; `mkdir` without `exist_ok`, `:271`), re-digested
  and refused unless it digests to its key (`:293-296`), and only then renamed into `packages/` (`:302`); a blob and a
  manifest are written there and renamed the same way (`:400`, `:420`). Stale entries are swept by Vibe itself, and the
  folder is "never load-bearing" (`:456-471`). Nothing under it is read as a package.
- **`plugins/data/<session id>/<plugin>/`** is that plugin's `${PLUGIN_DATA}`: per-session durable state, made writable for
  the plugin's own servers and hooks (`vibe/app_server/_plugins.py:541-567`, `:525`). It CAN hold code Vibe runs: a
  plugin's "knowledge" runtime is copied into the data root only `if not definition.runtime_root.exists()` — an existing
  one is adopted as it is (`vibe/core/plugins/_materialize.py:220-245`); a `.runtime/node-loader-v1/register.mjs` beside
  the data roots is loaded into every node child through `NODE_OPTIONS --import` (`:345-356`); and a declared server's cwd
  may be `${PLUGIN_DATA}` (`vibe/core/plugins/_native.py:2060-2062`). After turn 1 of the built-in package it held two
  EMPTY folders.

**(3) That refusal was reported as a missing CLI.** The consult record said `failureKind: cli-not-found` with the cure
"install it with uv: uv tool install mistral-vibe". The launch control's refusal becomes
`ReviewerOutcome.NotStarted(turn.Refusal)` (`ReviewerExecutor.cs:891`), and `ConsultFailures.Ended` maps EVERY
`NotStarted` to `CliNotFound` with the install cure (`ConsultFailures.cs:81`). The person is told to reinstall a CLI that
is installed — and the real cure is cut off besides: `ConsultFailures.Safe` caps what it keeps at 240 characters
(`ConsultFailures.cs:57`, `SaidCap` `:119`), the home refusal's "delete {home}" sits ~330 characters in, and D2's
sentence below is ~550.

**(4) A question row spent 2 233 164 input tokens in 299 s.** A `disk` row (`vibe-disk`, `mistral-medium-3.5`) asked
for two markers — one inside the granted root, one in a file OUTSIDE it — ran to Vibe's own `--time-limit 300` (exit 3,
`outcome: deadline`) with 2 233 164 tokens in / 13 804 out (record `fc27ef18…`); a narrower question on the same root
answered in 33 s and ~15 000 tokens. Something looped at the owner's expense. M-V17 measured what
([RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §28, 2026-10-15): not the outside path (0 of 7
runs named it) — **the row cannot LIST its granted folder** (`bash` is denied, `read_file` on a folder fails with
`[Errno 13]`, no list tool is enabled), so it guesses file names with `read_file` (`tool_failed`, "File not found"),
from request 29 eleven guesses a request through `run_typescript`; every request resends the whole history (96 % of it
served from the provider's cache, so the money is far below the token count — 0.46 USD for arm A's 288 k). Vibe has
flags that end such a run with an export: `--max-turns N` (assistant turns, one model request each) and `--max-tokens N`
(prompt + completion across the session), each 3 of 3 with exit 3, `export.json` written and `outcome: turn_limit` /
`token_limit`. Side finding: `--enabled-tools read_file` still left `bash`, `run_typescript` and `search_tool_functions`
callable by the model (denied or inert, but each costs a request).

## 2. Decisions

**D1 — every home that Vibe writes a session into lives under ONE short root, the consultant homes root, and the
round, the row and the check own and delete theirs.** The root is the existing `PanelSettings.ConsultantHomesRoot`
(`PanelSettings.cs:385-389`: `COAI_CONSULTANT_HOMES`, else `%LOCALAPPDATA%`) — already local, already the place D17
named, and now settable from the panel (D8). No home lives under TEMP any more, so a custom TEMP cannot break Vibe.
Layout:

| Site | Home | Length at the default root | Fits a profile up to |
|---|---|---|---|
| round reviewer | `<root>\coai-vh\<8 hex round>\<6 hex of provider/role>` | 47 + u | **55** |
| question row | `<root>\coai-vh\<8 hex>\h` | 42 + u | **60** |
| consultant check | `<root>\coai-vh\<8 hex>\h` | 42 + u | **60** |
| conversation | `<root>\coai-vibe\<32 hex id>\h` (was `\vh-<10>`) | 68 + u | **34** (was 22) |
| `providers` probe | unchanged, `%TEMP%\coai-answers-*` | — | its deepest write is `vibehistory` (§24): fails only for a TEMP over 221 characters |

- `coai-vh` is a sibling of `coai-vibe`/`coai-kimi`, **outside `ConsultantHomes.All`** (`ConsultantHomes.cs:24`), so the
  D20 sweep, which walks only those two (`ConsultantHomeSweep.cs:65`), never sees a round's or a row's home — it cannot
  judge one as a foreign or idle conversation folder.
- Each `<8 hex>` folder is OWNED for its life: the owner (the round's `RoundScratch`, the row's or the check's
  `OneShotHome`) creates it and holds its lock file `coai-turn.lock` (`HeldFile.Take`, the D20 pattern of
  `ConversationFolder.TakeTurn`, `ConversationFolder.cs:82`) until it deletes the folder in `Dispose`/`finally`, whatever
  ended it. A crash leftover is removed by the product's six-hour scratch sweep, extended to `<root>\coai-vh\*` (D3).
- The conversation keeps its 32-hex folder name: the D20 sweep reads the record by that name
  (`ConsultantHomeSweep.cs:142`, `ConsultationService.cs:62`), and a 12-hex name (which would fit u ≤ 54) means a prefix
  lookup in the store and a rewrite of `ConsultantHomeSweepTests` — rejected for this fix as a risk to D20 for 20
  characters. Its home is `h` (one Vibe per conversation folder, so the provider/role hash bought nothing there).
- **Settings built by hand** (tests, tools — `ConsultantHomesRoot` empty, `PanelSettings.cs:379-382`): every site keeps
  its home in its own scratch exactly as today, so no test ever writes the machine's `%LOCALAPPDATA%`. (A gate finding asked
  for this fallback to go; rejected — it is reachable only from settings a test builds, every server builds its settings
  from the environment.)
- **A root that changes orphans the old one's folders.** Neither sweep walks a root that is no longer configured: coai
  keeps no record of earlier roots, and walking a folder the person stopped naming — and may have reused — is the riskier
  error. What is left is bounded (a conversation folder is a few MB, `coai-vh` is empty between runs); the setting's help
  line (D8) says the old `coai-vibe` and `coai-vh` folders can be deleted once no consultation is open.
- Rejected: `%SystemDrive%\coai` or `%ProgramData%` as the DEFAULT (short whatever the profile, but shared by every local
  user — see D7); a junction to a short path (D17 refuses links in a home, R2); a `\\?\`-prefixed `VIBE_HOME` and the
  long-paths registry flag (both unmeasured — M-V18 in §6 measures them for a follow-up).

**D2 — on Windows the launch control refuses, before the home is made or anything of Vibe's starts, a home whose
deepest write would pass the limit; off Windows there is no budget.** One adapter owns the numbers
(`.agents/conventions/common/platform-limits.md` §1): `VibePathBudget` (new, `src_mcp/runners/Vibe/VibePathBudget.cs`), an
explicit platform → probed-value mapping resolved ONCE where `VibeRuntime` is constructed —
`Windows → (FileLimit 259, DeepestChild 156)`, and `None` for every other platform with no catch-all value: Vibe is refused
off Windows by D18's host check, which runs first (`VibeLaunch.cs:164`), so no budget there is ever the deciding check, and
a number for a platform nobody probed is not written down. The platform is the PROCESS's (`OperatingSystem.IsWindows()`),
not the `HostKind` a test injects, so a test that builds `new VibeRuntime("vibe", HostKind.Windows)` on ubuntu or macOS CI
is not judged by a Windows limit. `VibeRuntime(string id, HostKind host, VibePathBudget? budget = null)` — null is
`VibePathBudget.ForThisProcess`; a test passes `VibePathBudget.None` (§4, R4's audit) or a budget of its own.

`budget.Refusal(home)` measures `Path.GetFullPath(home).Length` — UTF-16 units, the unit the probe of §1 showed the limit
is counted in — and when `length + 1 + DeepestChild > FileLimit` says:

> the coai-owned Vibe home {home} is {n} characters long, and Vibe writes {DeepestChild} characters below it (its session
> chunks, research/RESULTS_vibe_confinement.md §27) — {n + 1 + DeepestChild} is past Windows' {FileLimit}-character path
> limit, so coai does not start Vibe on it; set "Vibe homes folder" in ConnectOtherAIs (COAI_CONSULTANT_HOMES) to a
> shorter local folder of your own — coai keeps its Vibe homes under it, private to you

Every number in it comes from the budget, none is a literal (platform-limits §1.4). It is the first check after the host
and the environment in `VibeLaunch.TakeAsync` (`VibeLaunch.cs:163-167`), before `HomeMade` (`VibeLaunch.cs:239-251`).
There is NO long-paths exemption (gate G1): the registry flag says what Windows allows a long-path-AWARE process, not that
Vibe's interpreter is one, and that branch is unmeasured; M-V18 (§6) measures it, and a follow-up may relax the budget on
its evidence. No margin over 156: the number is measured, a margin would cost every user the same characters, and a
deeper write in a later Vibe version is caught when that version is requalified (D4's `deepestChild`). This is what makes
(1) impossible to PAY for: a home that would fail is never started on — for the conversation at a profile over 34, the
round over 55, the row and check over 60, the person reads the sentence before any token.

**D3 — the six-hour scratch sweep also takes `<root>\coai-vh\*`, and never a folder somebody owns.**
`VibeHomes.SweepOneShot(homesRoot, cutoffUtc, warn)`:
- `<root>\coai-vh` itself a link (or not a folder) → said, nothing swept (the D20 rule, `ConsultantHomeSweep.cs:91`).
- For each entry: a link is said and left alone, never entered; a folder whose lock file this sweep cannot take with no
  wait (`HeldFile.Take`) is skipped — its owner is alive, in this process or ANOTHER server's on the same root; a folder
  whose newest write — the folder, its entries, and their entries — is older than the cutoff is removed under the lock
  with `GitScratch.DeleteEvenIfReadOnly` (which clears read-only FOLDERS, §26 defect 2), the lock file last; an error is
  warned and the next folder goes on.
- **Two servers, one root** (the root is per user, every server of that user shares it): `VibeHomeTurns` is in-process
  and cannot see another server's round, so the LOCK is what protects a live home across processes — the owner takes it
  the moment it makes the folder and holds it to the delete; a server killed mid-round loses it to the OS, and its folder
  then goes by age. The age rule is the second guard, for the instant between the folder's creation and its lock.
- Called from `PanelService.PruneOldAnswerDirs` (`PanelService.cs:1376-1390`), today a parameterless `private static`
  method passed to `RosterBuilder` as an `Action` (`PanelService.cs:122`) and called by `SweepScratch` (`:1758`): it becomes
  `internal static void PruneOldAnswerDirs(string homesRoot)`, the constructor passes the lambda
  `() => PruneOldAnswerDirs(settings.ConsultantHomesRoot)`, and `SweepScratch()` calls it with `_settings.ConsultantHomesRoot`.
  Six hours is today's rule for `coai-answers-*`, which held the round homes until now; a home lives for one launch's
  `--time-limit`, minutes. (A gate finding asked the sweep to look deeper than two levels for the newest write; rejected
  for the same reason — the lock, not the age, protects a live home.)

**D4 — Vibe's unpack staging is admitted by name; its per-session plugin data only as EMPTY folders; every starter still
refused.** From the source cited in §1(2):
- `logs/session/plugins/tmp/**` joins the manifest's `varying` (files admitted by name): nothing there is ever loaded as a
  package (a staged tree is re-digested before it is renamed, under a random name a planted folder cannot take), Vibe
  sweeps it itself, and a reviewer killed at its timeout mid-unpack leaves files there that its retry must not be refused
  over. A file under it is still put through `VibePluginStarters.Problem` (`VibePluginStarters.cs:39-42`) on both checks.
- `logs/session/plugins/data/**` joins a NEW manifest list, `varyingFolders`: the FOLDERS that match it are admitted, and
  any FILE under `plugins/data/` is refused on both checks — *"…holds {path}, which is a file in a Vibe plugin's
  per-session data, where Vibe adopts a plugin's runtime code without re-checking it (vibe/core/plugins/_materialize.py) —
  coai admits only its empty folders"*. Turn 1 of the built-in package wrote none; the first Vibe that does is refused
  with that path named, and measured then.
- In `VibeHome.Rules` (`VibeHome.cs:126-178`): a path matching `varying` is judged as varying BEFORE the pinned-prefix
  branch, with the starter check for one under the pinned prefix — extracted into `VaryingProblem` so `FileProblem`
  (`:131-135`, at CC 4 today) stays within the limit; `FolderAdmitted` (`:141-146`) also admits a folder that matches or
  leads to a `varyingFolders` glob (`VibeHome.Leads`/`Matches`, `:190-195`); a file whose path matches a `varyingFolders`
  glob is the refusal above. `VibeHomeManifest` (`VibeHomeManifest.cs`) parses `varyingFolders` (absent → empty, so an
  older embedded manifest still loads).
- **The manifest script is fixed to see what it missed** (`scripts/vibe-home-manifest.mjs`): `classOf` (`:78`) checks
  `varying` and `varyingFolders` FIRST, as the C# does; a FILE under the pinned prefix is `pinned`; a FOLDER under it is
  admitted only when it leads to a pinned file, else `unknown`; `inventory()` (`:81`) lists folders as `path/` and never
  hashes one (today `readFileSync` on a folder would throw `EISDIR`). The listing moves into `scripts/lib/vibe.mjs` as
  `files(dir, { folders: true })` and the classifier into an exported function, so both have a node test.
- **`deepestChild` is recorded per version** (review R3): the script cannot SEE the 156-character path (a transient
  `.tmp`); it records `deepestChild = longest persisted file path + 22` (Vibe's atomic-write wrapper, §1) with the path
  it came from, and the summary prints it. The 2.26.1 entry gets `"deepestChild": 156` and
  `"deepestPersisted": "logs/session/unified/<id>/chunks/<64 hex>.json"` (134) once, by hand — the one hand edit of a
  generated file, recorded as a deviation with run2's failing path as its evidence — and every later `--write` writes
  both. A parity test holds `VibePathBudget`'s Windows `DeepestChild` ≥ every entry's `deepestChild`, so requalifying a
  Vibe that writes deeper turns the suite red until the budget is raised.
- The JSON's `varying`/`varyingFolders` are the script's constants verbatim (a parity test reads the script's text); its
  `versions` keep every pinned hash; no paid run is needed.

**D5 — a launch-control refusal is its own failure kind, `launch-refused`, and its cure reaches the person whole.**
- `ReviewerOutcome.NotStarted` gains `bool ByLaunchControl = false` (`ReviewerExecutor.cs:234`); `LaunchBuiltAsync` sets it
  from the turn (`ReviewerExecutor.cs:891`); `LaunchTurn.Refused(string reason, bool cliMissing = false)`
  (`LaunchTurn.cs:34`) carries the one case that IS a missing CLI.
- **Which Vibe failures are a missing CLI** (gate G2): in `VibeLaunch.VersionAsync` (`VibeLaunch.cs:254-269`), a
  `Win32Exception` whose `NativeErrorCode` is **2** (`ERROR_FILE_NOT_FOUND`) or **3** (`ERROR_PATH_NOT_FOUND`) →
  `CliMissing`, `cli-not-found` with the install cure; any other code (5 access denied, 193 not a valid application, 216,
  1260 blocked by policy — Smart App Control) → `launch-refused` with the OS's own message and "the file is there but
  Windows would not start it". `--version` that STARTED and answered no version (`:261-263`) stays `launch-refused`, NOT
  `CliMissing`: a CLI that started is installed, so "CLI not found" would be false; its sentence is reworded to carry both
  cures — *"run `{exe} --version` by hand: if it fails, reinstall with `uv tool install --force mistral-vibe`; if it prints
  a version coai cannot read, this coai is older than that Vibe"*. `HomeVerdict` (`VibeLaunch.cs:189-195`) gains
  `bool CliMissing { get; init; }`, and `Given` (`:227-232`) passes it on.
- `ConsultFailures.Ended` (`ConsultFailures.cs:81`): `NotStarted { ByLaunchControl: true }` → new
  `ConsultFailure.LaunchRefused(Said)`; every other `NotStarted` stays `CliNotFound`.
- **The cure is not cut off** (review R1). Decided: ONE sentence, kept whole under a larger cap, not a finding/cure split.
  Every launch-control refusal is already one sentence naming what was found and its cure, cure last (the house rule,
  `VibeLaunch.cs:37-40`); splitting all of them into two fields is a refactor of the whole control for a display concern,
  and the only defect is the 240-character cap meant for a VENDOR's words. `LaunchRefused.Said` is
  `Redaction.SafeText(FirstLine(reason), Redaction.TitleLimit)` — the 1 000-character cap coai already uses for its own
  cures (`ConsultFailures.cs:105`, `Redaction.cs:40`); the longest launch-control sentence is D2's at ~550 (the budget
  refuses any longer home before the home refusal can be said), and the `--version` one carries Vibe's output inside the
  same cap. `What` = "coai did not start the consultant ({vendor}): {Said}"; `Cure` = "do what the sentence ends with — coai's
  launch check found this in its own setup, and the CLI itself is installed". D1/D6 assert the text reaching `What`
  contains `delete {home}` and `COAI_CONSULTANT_HOMES`.
- The catalogue (`ConsultFailure.cs:17`, `shared/consult-failure-kinds.json` with label "Refused by coai's launch check",
  `givesTheCallBack: true` — not the caller's doing) and the extension's label map (`consultantHealth.ts:343`) gain the
  word together; their parity tests (`ConsultFailureTests`, `consultantHealth.test.ts`) hold both halves. A round's
  reviewer line already prints the whole sentence (`BoundedScheduler.cs:747`, "not started: …").

**D6 — a Vibe CONSULTANT launch is capped at 20 assistant turns (`--max-turns 20`); a review launch is not capped.**
M-V17 is measured (RESULTS §28) and resolves §6's decision table to its FIRST row, so the cap joins this pull request, in
the flag's own unit (gate G3):
- **The flag: `--max-turns`, not `--max-tokens`.** The loop's unit is the model request, and `--max-turns` counts exactly
  that (§28.3: `steps` = the journal's completions), independent of how large the prompt is. `--max-tokens` counts every
  request's whole history, cached or not: a question row or a consult turn that carries a large context (a document, a
  feature, a long problem statement — easily several times arm A's 7.2 k first request) would be cut after one or two
  legitimate requests, and its overshoot is a request (≤ 5.7 k past the value). `--max-price` is unmeasured and a
  subscription's export price is not what the person pays. Both measured flags end with exit 3, an export and a readable
  `outcome` (`turn_limit` / `token_limit`) — and their `stop_reason` differs (`limit` / `interrupted`), so coai reads
  `outcome`, as §18 already said.
- **The value: 20.** Arm B (the answer in one named file) took 2 requests; arm C at 10 cut every looping run at 79–84 k
  input tokens. A legitimate question that names several files reads them one request each (H4: there is no listing, so a
  row reads only what it is told or guesses), plus the answer — 10 would cut a question naming ten files; 20 leaves room for
  ~15 files and a retry or two. The worst case at 20, at arm A's measured growth (7.2 k + ~170 a request), is ≈ 180 k input
  tokens — 8 % of the 2.2 M this plan started from, mostly cached. The number is one named constant
  (`VibeLimits.ConsultantTurns`), cited from §28 in its doc comment; a setting for it is not added until somebody needs a
  different one.
- **Where:** every launch `VibeConsultant.Build` makes — the question row, the stuck consultant's conversation turn (the cap
  is per launch, so per turn), the consultant check. NOT a review (`VibeRuntime.Build`, its retry and repair): a review reads
  a snapshot whose files its prompt names (the diff, the plan), it was never seen looping, and a cut review is paid for and
  lost; the time limit (D8 of the parent) still bounds it.
- **The outcome is coai's sentence.** `VibeRuntime.WhyItFailed` (`VibeRuntime.cs:210-216`) gains
  `{ Outcome: "turn_limit" }` → *"Vibe stopped at coai's cap of {N} assistant turns (--max-turns) — it did not find what
  it was asked for in its granted folders; Vibe cannot list a folder (research/RESULTS_vibe_confinement.md §28), so name
  the files the question is about"*, and `{ Outcome: "token_limit" }` → the same for a token cap coai does not set today
  (read, never set, so a later flag change is already worded). The row's and the turn's reason is then
  "exit 3: <that sentence>", as the deadline's is today; the usage is still read from the export (D8, D9).
- **The cause is NOT cured here.** A cap bounds the loss; it does not stop a row guessing. The cure — telling the row what
  its folders hold (a listing of the snapshot coai made) — and the side finding (`bash`, `run_typescript`,
  `search_tool_functions` callable despite `--enabled-tools read_file`) are unmeasured, each needs arms of its own, and the
  second is a confinement question: both are extracted into
  [PLAN_a_vibe_row_is_told_what_its_folders_hold.md](PLAN_a_vibe_row_is_told_what_its_folders_hold.md).

**D7 — SECURITY — coai's folders under the homes root are private to the person, through ONE type** (implemented per the security review
of 2026-10-15, ACCEPT-WITH-CHANGES). The root may be a folder the person picks (D8), and a folder at a drive's root inherits
`Authenticated Users: Modify`; another local user could read a home's transcript (the reviewed code) or coai's copy of the tree. So every
entry into `<root>\coai-*` goes through `PrivateFolder` (`src_mcp/runners/Files/PrivateFolder.cs`; a structural test fails on any other
`Path.Combine` with the root or a `coai-*` name): `VibeHomes.ReserveOneShot` and `SweepOneShot`, `ConsultantPlaces.OwnedAsync`,
`ConsultantHomeSweep`'s pass and `close_consult`.
- **The root** must be absolute, not UNC, not `\\?\`, and must EXIST — coai never creates it ("the Vibe homes folder {root} does not
  exist — create a short local folder of your own and name it in ConnectOtherAIs (COAI_CONSULTANT_HOMES)"). The root's own DACL and everything
  above it are not judged. The server validates `COAI_CONSULTANT_HOMES` against this, however the value came in.
- **Create** atomically with the DACL (`new DirectoryInfo(path).Create(security)`): protected, three `FullControl` Allow entries
  (container+object inherit) for the current user, LocalSystem and BuiltinAdministrators — SIDs, never names; the owner is not set.
  `CreateDirectory` returns silently on an existing folder, so the create proves nothing and step 3 runs every time.
- **Verify by HANDLE** (`CreateFileW` with backup semantics, share read+write): `GetFinalPathNameByHandleW` must equal `<canonical root>\<name>`
  component by component (refuses a junction, a symlink, a swap); `GetSecurityInfo` reads owner and DACL; a failed open or read is a refusal.
  The owner must be the token user or Administrators (an elevated session creates folders owned by Administrators). When the owner passes
  but the DACL is not exactly the three entries (another SID, a Deny, inherited entries, Everyone), it is RE-ACLed through the handle
  (`SetSecurityInfo`, which propagates to existing children and migrates a 0.53/0.54 `coai-vibe` tree), re-read, and one line says
  "made {path} private; {who} could {read/modify} it until now". A foreign owner is refused: "{path} is not private to you (owner {owner};
  {who} may {rights}) — … delete it, or choose another Vibe homes folder in ConnectOtherAIs (COAI_CONSULTANT_HOMES)".
- **The handle is held** for the reservation's life, share read+write without delete. Measured (E7): a handle that asks only for attributes
  and security records no share mode and blocks nothing; asking for `FILE_LIST_DIRECTORY` as well makes `Directory.Move` throw
  `IOException`, so the folder cannot be renamed or swapped while it is held.
- **Below `coai-*`** no ACL checks: `<8>` is created fresh; the link checks stay; `VibeLaunch.HomeMade` refuses a home whose parent does not
  exist instead of creating the chain. A Vibe export (the whole conversation) lives beside the home in the owned folder, not in TEMP.
- **Off Windows** the same type: `0700` on create; an existing folder must not be a symlink and must have no group or other bits (refused
  naming the mode); the owner uid is not checked (a stated limit).
- **Limits:** a local administrator and SYSTEM are outside the threat model; the root and everything above it belong to the person;
  re-ACLing does not undo contents planted while the folder was wide (the per-launch home check judges those; the log says the
  transcripts were readable until now); a `coai-vh` pre-created by another user on a C:\-style root cannot be deleted by the victim — choose another folder.
- **CI:** a `windows-latest` job runs the MCP suite; the runner is elevated, so E1 accepts owner in {user, Administrators} and E5 skips
  without `SeRestorePrivilege`.

**D8 — the homes root is a panel setting** (the owner's rule: every action is a panel control). The extension has none
today: nothing in `src_vs_code/src` writes `COAI_CONSULTANT_HOMES`. A setting `coai.vibeHomesDirectory` (a folder;
default empty = the server's own default) joins the path-settings registry (`src_vs_code/src/pathSettings.ts`, the
census test fails on a `*Directory` setting not registered there) with wire key `COAI_CONSULTANT_HOMES`, side-aware like
`coai.qconsultRoots` (`pathSettings.ts:79`); it is drawn on the Consultants page beside the Vibe rows; its help line is
written in the five help languages (`helpContent.ts:32`: en, ru, uk, de, es) and says what lives there, that it must be
short and local, that coai makes its folders there private (D7), and that the previous folder's `coai-vibe`/`coai-vh` can
be deleted after a change (D1). The server reads it through the settings file like every other wire key; the environment
still wins.

## 3. Files, and the change in each

| File | Change |
|---|---|
| `src_mcp/runners/Vibe/VibePathBudget.cs` (new) | D2: `record VibePathBudget(int FileLimit, int DeepestChild)`, `None`, `ForThisProcess` (explicit mapping, Windows only), `Refusal(home)`. |
| `src_mcp/runners/Vibe/VibeHomes.cs` (new) | `OneShotFolder = "coai-vh"`; `OneShotIn(root, id)` (pure); `ReserveOneShot(root)` → a reservation union: `None` (root empty), `Refused(sentence)` (D7: a link, a foreign owner, an open DACL), `Reserved(folder)` (made private, its lock taken); `SweepOneShot(root, cutoff, warn)` (D3); `OneShotHome : IDisposable` (holds the lock, deletes the folder best-effort, says a failure). Every method CC ≤ 4. |
| `src_mcp/runners/Vibe/VibeHomeAcl.cs` (new, Windows) | D7: create private, check owner and DACL; one sentence per refusal. |
| `src_mcp/runners/Vibe/VibeRuntime.cs:41-47,157-165` | the constructor takes the budget (D2); `HomeFor(owned, provider, role)` → `owned\<6 hex of provider/role>` (was `vh-<10>`); `ConsultantHome(owned)` → `owned\h`; `:24-30` remarks updated. `:103` probe unchanged, comment says why (D1 table). `TakeTurnAsync` (`:190-191`) passes the budget. |
| `src_mcp/runners/Vibe/VibeConsultant.cs:57-65` | the home is `VibeRuntime.ConsultantHome(owned)`; the argv passes `maxTurns: VibeLimits.ConsultantTurns` (D6); remarks `:20-24` updated. |
| `src_mcp/runners/Vibe/VibeRuntime.cs:132-147,210-216` | `Arguments(…, int maxTurns = 0)` adds `--max-turns N` after `--time-limit` only when `maxTurns > 0` (`Build`, the review, passes nothing); `WhyItFailed` gains the `turn_limit` and `token_limit` arms (D6). `VibeLimits` (new, beside `VibeModels` in `VibeLaunch.cs:15-29` or its own file): `ConsultantTurns = 20`, its doc citing RESULTS §28. |
| `src_mcp/runners/Vibe/VibeLaunch.cs:161-171,227-269` | `TakeAsync(…, VibePathBudget budget)`; the `before` chain gains the budget and D7's link check after the environment check; G2's error codes and reworded `--version` sentence; `HomeVerdict.CliMissing`. |
| `src_mcp/runners/Reviewers/LaunchTurn.cs:22-34` | `Refused(string reason, bool cliMissing = false)`, property `CliMissing`. |
| `src_mcp/runners/Reviewers/ReviewerExecutor.cs:234,891` | `NotStarted(string Reason, bool ByLaunchControl = false)`; `new NotStarted(turn.Refusal, ByLaunchControl: !turn.CliMissing)`. |
| `src_mcp/runners/Consultation/ConsultFailure.cs:17,188-195` | kind `LaunchRefused = "launch-refused"`; record `LaunchRefused(string Said)`. |
| `src_mcp/runners/Consultation/ConsultFailures.cs:81` | the `ByLaunchControl` arm first, `Said` under `Redaction.TitleLimit` (D5). |
| `shared/consult-failure-kinds.json`, `src_vs_code/src/consultantHealth.ts:343` | the word and its label (D5). |
| `src_mcp/runners/Vibe/VibeHome.cs:126-195`, `VibeHomeManifest.cs` | D4: varying first, `VaryingProblem`, `varyingFolders` (folders admitted, files refused), starters on both checks; remarks `:17-24`. |
| `shared/vibe-home-manifest.json`, `scripts/vibe-home-manifest.mjs`, `scripts/lib/vibe.mjs` | D4: `tmp/**` in `varying`, `data/*/**` in `varyingFolders`, the 2.26.1 entry's `deepestChild`/`deepestPersisted`; the script's classifier, folder listing and `deepestChild`. |
| `src_mcp/src/Server/Rounds/RoundScratch.cs:25-82` | `Reserve(note, homesRoot)`: also the round's `OneShotHome`; a `Refused` reservation is kept like `RoundTree.Refused` — the Vibe rows are left out of the round with its sentence (as `RosterBuilder` does for a refused snapshot), the others run; `Dispose` disposes the home after `Directory`. |
| `src_mcp/src/Server/Rounds/RosterBuilder.cs:106,451` | `Reserve(…, _settings.ConsultantHomesRoot)`; `OwnedScratch` = the home's folder when reserved, else `scratch.Directory`. |
| `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:75,94-121,148-149` | ctor gains `string homesRoot = ""`; a home-keeping row holds a `OneShotHome` for its run (a `Refused` one ends the row `refused` with the sentence); `Owning` passes its folder (else the scratch, today's). The copies and `cwd` stay in the TEMP scratch. |
| `src_mcp/src/Server/QuestionConsult/QuestionFanOut.cs:64` | `new(executor, ledger, log, launcher, panel.ConsultantHomesRoot)`. |
| `src_mcp/src/Server/Consultation/ConsultantCheck.cs:200-212,258-260` | the turn holds a `OneShotHome` from `settings.ConsultantHomesRoot` and passes it as `OwnedScratch` (else `place.Folder`, today's); a refused reservation fails the check with `launch-refused`. |
| `src_mcp/src/Server/Consultation/ConsultantPlaces.cs:143-173` | D7: `coai-vibe` created private, a foreign or open one refused. |
| `src_mcp/src/Server/PanelService.cs:122,1376-1390,1758` | D3's signature, lambda and `SweepScratch` call. |
| `src_mcp/src/Server/PanelSettings.cs:374-389` | the doc says the root holds every Vibe home and why it must be short and private. |
| `src_vs_code/src/pathSettings.ts`, `package.json`, the Consultants page, `helpContent.ts` | D8. |
| docs | `PLAN_vibe_and_kimi_runtimes.md` D17 and D20 amended (dated 2026-10-15); `research/module_runners.md`, `module_server.md`, `module_extension.md`. (`research/RESULTS_vibe_confinement.md` §27 and §28 are already written.) |

## 4. Scenarios — each a test written FIRST and watched red for the stated reason

New test folders are `TempDir.For("testsToDelete-vh-<what>-")` (`.agents/conventions/common/test-leftovers.md`). A new
member is first written as a stub (`=> string.Empty`, `=> 0`, a parameter that is ignored) so the test compiles; the red
below is then the assertion, never a compile error.

### A. The path budget and the layout (defect 1)

| # | Given / When / Then | Expected red on today's code (or the stub) |
|---|---|---|
| A1 | **Given** the Windows budget and a home of 103 characters, **when** `Refusal(home)`, **then** the sentence names the home, `103`, `260`, `259`, `156` and `COAI_CONSULTANT_HOMES`, and names no example folder. | stub: `Expected string to contain "COAI_CONSULTANT_HOMES" … but found ""` |
| A2 | Boundary (platform-limits §1, three fixtures): 102 characters → empty (the largest accepted); 103 → refused (the first refused); and **102 characters holding one `é`** (103 UTF-8 bytes, asserted to differ from its `Length`) → empty — the limit is UTF-16 units, so a guard that counted bytes would refuse it. | the 103 case as A1; the `é` case goes red under a byte-counting guard (break-it, recorded) |
| A3 | `VibePathBudget.None.Refusal(<300 characters>)` → empty; `ForThisProcess` is `None` when `OperatingSystem.IsWindows()` is false (asserted on the CI leg it runs on), and the Windows values are 259 and 156 there. | stub: `ForThisProcess` returns the Windows values everywhere — red on ubuntu/macOS |
| A4 | **Given** the Windows budget, the built request and a home under `testsToDelete-vh-budget-*` padded to 120 characters, **when** `VibeLaunch.TakeAsync(…, budget)`, **then** it is `Refused` with A1's sentence, the home folder does NOT exist and the fake launcher was called **0** times. | `Expected launcher calls to be empty, but found 1` (`--version` ran) and the folder was created |
| A5 | **Given** the default-root shape `C:\Users\<u>\AppData\Local` (a string, nothing on disk), **when** each site's home is built — `HomeFor(OneShotIn(root, "a1b2c3d4"), "vibe", "PlanCritique")`, `ConsultantHome(OneShotIn(root, …))`, `ConsultantHome(ConversationFolder(root, Vibe, <32 hex>))` — **then** the Windows budget's `Refusal` is empty at u = 55 / 60 / 34 and non-empty at 56 / 61 / 35. | today's `vh-<10>` names: `Expected "" but found "the coai-owned Vibe home … is 109 characters long …"` (round at u = 55) |
| A6 | **Given** `TEMP`, `TMP` and `TMPDIR` all pointed at a 150-character `testsToDelete-vh-longtemp-*` folder (`Path.GetTempPath` reads `TMP` first on Windows, `TMPDIR` elsewhere; restored in `finally`; the class is a non-parallel collection), **when** a round, a question row and a check reserve their homes with a homes root of `testsToDelete-vh-root-*`, **then** no home starts with `Path.GetTempPath()`. | round: `Expected … not to start with "<temp>" but … coai-answers-…\vh-…` |
| A7 | Round: **given** `RoundScratch.Reserve(note, root)`, **then** its home folder is `<root>\coai-vh\<8 hex>` holding a taken `coai-turn.lock`; a reviewer home made in it holding a READ-ONLY folder (`logs\session\plugins\packages\6b\x\skills\vibe`, attribute set) is gone after `Dispose`, and so is `Directory`. | stub ignoring the root: `Expected the home folder to start with "<root>\coai-vh" but found ""` |
| A8 | Round end to end (`AVibeReviewerRunsThroughAFakeVibeTests` shape, `PlayedVibe`): the `VIBE_HOME` the fake Vibe received is under `<root>\coai-vh\`, at most 102 characters, and the folder is gone when the round has ended. | `VIBE_HOME` is `…\coai-answers-…\vh-…` |
| A9 | Question row (`QuestionRowLaunch` with `homesRoot`): `VIBE_HOME` is `<root>\coai-vh\<8>\h`; after an answer, after a launch-control refusal and after a cancellation the `<8>` folder is gone. | `VIBE_HOME` under `coai-question-*` |
| A10 | Consultant check: the same as A9 for one check. | `VIBE_HOME` under `coai-check-*\own\vh-…` |
| A11 | Conversation: `VIBE_HOME` is `<root>\coai-vibe\<id>\h`, the same on turn 2. | `…\vh-21f30f2142` |
| A12 | Settings built by hand: `VibeHomes.ReserveOneShot("")` is `None`, and a round, a row and a check built with an empty `ConsultantHomesRoot` keep their homes inside their own scratch (asserted on the `VIBE_HOME` they launched with — the test never looks at the machine's `%LOCALAPPDATA%`). | green from the start — the guard that no test writes the machine's folder |

### B. The sweeps (crash leftovers, two servers, and D20 leaving a round home alone)

| # | Given / When / Then | Expected red |
|---|---|---|
| B1 | **Given** `<root>\coai-vh\<8>\<6>` holding a read-only plugin folder and no lock holder, every write time set seven hours back, **when** `SweepOneShot(root, now − 6 h, warn)`, **then** the folder is gone and the count is 1. | stub `=> 0`: `Expected directory not to exist` |
| B2 | The same home written one hour ago → kept. | green from the stub; red if B1's fix ignores the age |
| B3 | **Two servers:** an old folder whose `coai-turn.lock` is held by a SECOND PROCESS (a child the test starts that opens the lock with no sharing and waits) → kept, the count 0; the child exits → the next pass removes it. | removed while held, once B1 is naive |
| B4 | Links (a junction on Windows, a symlink elsewhere): (a) an old entry under `coai-vh` that is a link to a folder holding a file → the link left, warned, the target and its file untouched; (b) `coai-vh` ITSELF a link to such a folder → `SweepOneShot` sweeps nothing and warns, and `ReserveOneShot` is `Refused` naming it, nothing made under it. | (a) target emptied once B1 is naive; (b) a folder reserved through the link |
| B5 | **Given** a homes root with `coai-vh\<8>\<6>` (no stamp, written eight days ago) and no records, **when** the D20 sweep `ConsultantHomeSweep.Sweep` runs, **then** the round home exists and the count is 0. | green from the start; break-it: add `VibeHomes.OneShotFolder` to `ConsultantHomes.All` → red (`ForeignAge` passed, the folder is removed) — record that observation |
| B6 | `PanelService.ForgetTheLastSweep()`, then `PanelService.PruneOldAnswerDirs(root)` (now `internal static`; the tests see internals already, as `ForgetTheLastSweep` shows) → B1's leftover is gone. | the leftover stays |

### C. A second turn starts on the home its first turn left (defect 2)

| # | Given / When / Then | Expected red |
|---|---|---|
| C1 | **Given** `VibeHomeTests.TheInventoryVibeWrites()` plus the measured turn-1 extras — the EMPTY folders `logs/session/plugins/data/193149ad-d8e5-069e-8f34-aece24b2623d/vibe/` and `logs/session/plugins/tmp/`, and the `chunks/`, `generations/0000000000000001/…`, `journal/…`, `meta.json`, `scheduled-loops.json` files of §1 — **when** the hash check `VibeHome.Refusal(home, Manifest, Entry)` runs, **then** it is empty. | `Expected string to be empty, but found "the coai-owned Vibe home … holds logs/session/plugins/data/, which is not a folder Vibe writes into its home — …"` |
| C2 | The same home through the shape check `VibeHome.Refusal(home, Manifest)` → empty. | green today — kept so both checks are pinned |
| C3 | Under `plugins/data/<uuid>/vibe/`: a `state.json` holding `{}`, a `hooks/hooks.json`, and an `.mcp.json` holding `{"mcpServers":{"x":{"command":"y"}}}` → EACH refused on BOTH checks, the sentence naming the file and "plugin's per-session data". | hash check: red today only by wording (`…is not one of the files Vibe 9.9.9 unpacks` — assert the new clause); shape check: `state.json` is ADMITTED today by place (`VibeHome.cs:159-167`) → `Expected … to contain "per-session data" but found ""`; the two starters are refused on the shape check today already (green there) |
| C4 | Under `plugins/tmp/`: `package-0123456789abcdef-0123456789abcdef/x.partial` admitted on both checks; `…/hooks.json` refused with "hook" on both. | hash check refuses `x.partial` today |
| C5 | Parity: the shipped manifest's `varying` contains `logs/session/plugins/tmp/**`, its `varyingFolders` is `["logs/session/plugins/data/*/**"]`, and both equal the script's constants (read from the script's text, as `TheProbeScriptsMinimalEnvironmentIsTheProductsTests` reads its script); every entry's `deepestChild` ≤ the Windows `VibePathBudget.DeepestChild`, and 2.26.1's is 156. | `Expected varying to contain "logs/session/plugins/tmp/**"` |
| C6 | Repair on the same home: **given** a home holding C1's inventory and a fake launcher answering `--version` with `vibe 9.9.9` (the test's manifest entry), **when** `VibeLaunch.TakeAsync`, **then** the turn is `Holding`, not refused. | refused with the `plugins/data/` sentence |
| C7 | Conversation (`AVibeConsultationKeepsOneSnapshotTests` shape): a fake Vibe whose turn 1 also makes the two empty folders; turn 2 starts with `--resume <id>` and answers. | turn 2 fails: "holds logs/session/plugins/data/" |
| C8 | `node --test scripts/lib/vibe.test.mjs` (CI runs `scripts/**/*.test.mjs`, `ci.yml:196`): `files(dir, { folders: true })` lists an empty folder as `a/b/` and `files(dir)` does not; the classifier calls `logs/session/plugins/data/x/vibe/` admitted-folder, `logs/session/plugins/newthing/` `unknown`, `logs/session/plugins/blobs/60/` pinned-lead (it leads to a pinned file), `logs/session/plugins/tmp/a` varying; `deepestChild` of C1's listing is 156. | `newthing/` classified `pinned` (prefix checked first, `:78`); folders absent from the listing |

### D. A refusal is named as what it is, whole (defect 3)

| # | Given / When / Then | Expected red |
|---|---|---|
| D1 | `ConsultFailures.Classify(vibe, Ended(new NotStarted(<the real VibeHome refusal for a 100-character home>, ByLaunchControl: true)), "vibe")` → kind `launch-refused`; `What` contains `delete <home>` (the sentence's end, ~330 characters in); `Cure` does NOT contain `uv tool install`. | stub flag ignored: `Expected Kind to be "launch-refused", but found "cli-not-found"`; and with the kind fixed but `Safe`'s cap kept: `Expected What to contain "delete C:\…" ` |
| D2 | `NotStarted("'agy' could not be started")` (flag false) → still `cli-not-found` with the vendor's install command (`ACliNotStarted_OrNotOnThePath_IsCliNotFound_WithTheVendorsOwnInstallCommand` stays green). | — (guard) |
| D3 | `ReviewerExecutor` with an adapter whose `TakeTurnAsync` answers `LaunchTurn.Refused("x")` → `NotStarted { ByLaunchControl: true }`; with `LaunchTurn.Refused("x", cliMissing: true)` → `false`. | `Expected ByLaunchControl to be true, but found false` |
| D4 | G2, `VibeLaunch.TakeAsync` with a launcher whose `--version` throws: `Win32Exception(2)` and `Win32Exception(3)` → `CliMissing`, classified `cli-not-found` with "uv tool install mistral-vibe"; `Win32Exception(5)` and `(1260)` → not `CliMissing`, `launch-refused`, the OS message in the sentence; `--version` exiting 0 with `no version here` → `launch-refused`, the sentence carrying both cures. | `Expected CliMissing to be true` (2, 3); the others are `cli-not-found` today |
| D5 | The catalogue: `ConsultFailureTests`' reflection test and `src_vs_code/src/test/consultantHealth.test.ts` pass with `launch-refused` on both sides. | the reflection test names the word missing from the JSON |
| D6 | End to end: a Vibe conversation turn on a home holding a `hooks.toml`, and one whose home would pass the budget (a homes root of 90 characters, Windows budget) → the record's `failureKind` is `launch-refused`, the answer quotes the whole sentence — `delete <home>` in the first, `COAI_CONSULTANT_HOMES` in the second — and never "install it with uv". | `failureKind` `cli-not-found`, the cure cut at 240 |

### E. The private root (D7, as the security review left it)

| # | Given / When / Then | Expected red |
|---|---|---|
| E1 | A fresh root: `Open` gives a protected DACL with exactly the three FullControl CI/OI Allow ACEs; owner in {user, Administrators}. | stub (plain `CreateDirectory`): `Expected security.AreAccessRulesProtected to be True … but found False` |
| E2 | A user-owned `coai-vh` with `Everyone: Read` is Opened, the DACL is now exactly the three, and the log names Everyone. | `AreAccessRulesProtected` False |
| E2b | A Deny ACE for Users is gone after `Open`. | as E2 |
| E3 | The `%LOCALAPPDATA%` shape `coai-vibe\<id>\h\file`: the child file's ACL is the three (propagation). | as E2 |
| E4 | `coai-vh` a junction to a private folder: Refused, the sentence names the path, nothing made. | `Expected … Refused, but found … Opened` |
| E5 | Owner set to SYSTEM under `SeRestorePrivilege` (else skipped, with the reason): Refused naming `NT AUTHORITY\SYSTEM` and `COAI_CONSULTANT_HOMES`. | Opened |
| E6 | `ForConversationAsync` and the sweep on a root whose `coai-vibe` is a junction: Refused, 0 swept, a warning. | the conversation started |
| E7 | With an Opened handle held, `Directory.Move` throws `IOException`. | `Expected a <IOException> to be thrown … but no exception was thrown` (until the handle asked for a data right) |
| E8 | A missing root: Refused with the "does not exist" sentence, nothing created. | Opened (the folder was created) |
| E9 | Unix legs: `Open` creates 0700; a pre-existing 0755 is Refused naming 755. | — |

### K. A Vibe consultant launch stops at 20 turns and says why (defect 4, D6)

| # | Given / When / Then | Expected red |
|---|---|---|
| K1 | **Given** a question-row `ConsultantLaunch` (`LaunchConfinement.Planned`), **when** `VibeConsultant.Build`, **then** the argv holds `--max-turns` exactly once, followed by `20` (`VibeLimits.ConsultantTurns`), after `--time-limit`. | `Expected argv to contain "--max-turns"` |
| K2 | The same for a conversation turn (with and without `--resume <id>`) and for a consultant check's launch. | as K1 |
| K3 | `VibeRuntime.Build` (a review) and `ForLaunch` of it (a retry/repair) → the argv holds no `--max-turns`. | green from the start; break-it: give `Arguments`' `maxTurns` a default of 20 → red — record it |
| K4 | `WhyItFailed` on an export with `outcome: turn_limit` (and `stop_reason: limit`) → the sentence contains `20`, `--max-turns`, `list a folder` and `§28`; with `outcome: token_limit` (and `stop_reason: interrupted`) → the token-cap sentence; `deadline` unchanged. | `Expected … to contain "--max-turns" but found "Vibe ended with outcome: turn_limit"` |
| K5 | End to end through `QuestionRowLaunch` with a played Vibe (`PlayedVibe`) that exits 3, prints `Stopped: turn_limit` on stderr and writes an export with `outcome: turn_limit` and a usage of 84 186 / 1 395: the row ends `failed`, its reason is `exit 3: Vibe stopped at coai's cap of 20 assistant turns …`, and its usage is 84 186 / 1 395 (read from the export, never zero). | reason `exit 3: Vibe ended with outcome: turn_limit` |
| K6 | The same played Vibe as a stuck consultant's turn: the record's `failureKind` is `exit` with K4's sentence in `What`, never `cli-not-found` or `launch-refused`. | the sentence absent |

### F. The audit of the existing Vibe tests (review R4) — a build step, not a new behaviour

With D2 the budget applies to every Vibe launch a test drives on a Windows machine, whose TEMP is 33 characters here
and ~38 on the Windows runner. Before the first budget commit, the coder lists the longest `VIBE_HOME` each class
below produces on this machine (a one-off print, not committed) and, per class, either shortens its folders or passes
`VibePathBudget.None` to the `VibeRuntime` it builds — the summary names each class and which it got. Known to pass 102:
`ConsultantCheckTests` (`coai-checktest-temp-*` + `coai-check-<32>` + `own` + `vh-<10>` ≈ TEMP + 95). To audit:
`AConversationPlaceTests`, `AVibeConsultationKeepsOneSnapshotTests`, `AVibeQuestionRowReadsACopyOfItsRootsTests`,
`AVibeReviewerRunsThroughAFakeVibeTests`, `AVibeRowReviewsInTheRoundsSnapshotTests`,
`AnUnmeasuredVibeIsWarnedAboutNotRefusedTests`, `ConsultantHomeSweepTests`, `EveryAdapterRecordsWhatItLaunchedTests`,
`OnlyARuntimeThatAsksGetsASanitisedSnapshotTests`, `TheVibeProbeLeavesThePersonsHomeAloneTests`, `VibeConsultantTests`,
`VibeIsDiagnosedInItsOwnWordsTests`, `VibeLaunchControlTests`, `VibeLedgerNamesTheVersionTests`,
`VibePrintsUtf8OnEveryPathTests`, `VibeRuntimeTests`, `PlayedVibe`. On ubuntu and macOS CI the budget is `None` by A3.
Passing `None` is right only where the home's length is not what the test is about; A1–A12 use the real budget.

## 5. Commands

```powershell
cd D:\rsd\_wt\coai-vibe-maxpath
dotnet build src_mcp/tests/CoaiMcp.Tests.csproj
# red first, per group, then green:
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*VibePathBudgetTests" --filter-class "*VibeHomesTests" --filter-class "*VibeHomeTests" --filter-class "*VibeLaunchControlTests"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ConsultFailureTests" --filter-class "*ConsultantHomeSweepTests" --filter-class "*AVibeReviewerRunsThroughAFakeVibeTests" --filter-class "*AVibeConsultationKeepsOneSnapshotTests" --filter-class "*AVibeQuestionRowReadsACopyOfItsRootsTests" --filter-class "*ConsultantCheckTests"
node --test "scripts/**/*.test.mjs"
# the whole suites before the code round (never `dotnet test`):
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
npm --prefix src_vs_code test
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore
node .agents/conventions/tools/plan-lifecycle.mjs
```

A test that sets `TEMP`/`TMP`/`TMPDIR` (A6) runs in a non-parallel collection and restores them in `finally`. No test may
write `%LOCALAPPDATA%`: every homes root in a test is a `testsToDelete-` folder.

## 6. Measurements — scouts' briefs

### M-V17 — the question row that spent 2.2 M tokens, before any cap

> **MEASURED 2026-10-15 — [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §28.** H1 did not hold as
> worded (0 of 7 runs named the outside path); a new H4 held — the row cannot list its folder and guesses names. Arms C
> (`--max-turns 10`) and D (`--max-tokens 60000`) were each 3 of 3 with exit 3, an export and `outcome: turn_limit` /
> `token_limit`; arm B needed 2 requests; spent 740 616 input tokens (96 % cached), 1.19 USD. The decision table below
> resolved to its first row → D6 and §4 K. Deviations the scout recorded (§28.2): arm D added, the first homes named
> `C:\t\vq<n>`, `--time-limit 90` in every arm. The brief is kept below as it was run.

**Question.** What did Vibe do for 299 s and 2 233 164 input tokens on the `disk` row of record `fc27ef18…`, and does
Vibe offer a flag that bounds a run and ends it with an export? Nothing in this plan's code depends on the answer.

**Predictions, written before the run.** H1 (most likely): the model retried `read_file` on the path outside the granted
root in a loop — every retry `tool_denied`, every request resending the whole history (~20 000 tokens × ~100 requests).
H2: it re-read the granted files repeatedly. H3: Vibe's own compaction or planning looped. The scout writes which held.

**Method** (Windows, the RELEASED server, nothing of the owner's installation):
1. Free: `vibe --help` and `vibe -p --help` — record every flag that bounds a run, verbatim, with the UNIT its help states
   (turns, steps, requests, tokens, price).
2. Free: capture the product's exact launch — a scratch `COAI_DATA_DIR` whose one `vibe` row's `executablePath` is a
   recording stand-in (`vibe-recorder.cmd` → a node script) that appends EVERY invocation (argv, the environment's names
   and the four built values, cwd, stdin) to one file and answers `--version` with `vibe 2.26.1` and exit 0 — the launch
   control asks `--version` before every launch, and a stand-in that does not answer it is refused before the review
   launch is ever recorded. Then `ask_consultants` with run3's question shape: a SYNTHETIC out-of-root marker file the
   scout creates for this run (never a real file of the machine), the same wording otherwise.
3. Paid, arm A, **n = 1**: the real `vibe` with the recorded argv/stdin/cwd (the cwd re-made as the product makes it — a
   copy of the granted root), the BUILT environment (minimal set + `VIBE_HOME` = a fresh folder `C:\testsToDelete-vq<n>`,
   `VIBE_CLI=python`, `VIBE_ACTIVE_MODEL=mistral-medium-3.5`, `PYTHONUTF8=1`), `--time-limit 90` instead of 300,
   `--output-dir` kept. Keep the export AND the home. From `export.json` and
   `logs/session/unified/<id>/journal/*.jsonl` record: the number of model requests, each tool call (name, argument,
   `tool_denied` / `tool_failed` / ok), the input tokens per request where the journal has them, and the first repeated
   call. If the loop is visible, stop here (n = 1 is enough to DIAGNOSE, as §26 did).
4. Paid, arm B, n = 1: the same with the question narrowed to the granted root only (the control: ~15 000 tokens).
5. Paid, arm C, **n = 3, only if step 1 found a bounding flag**: arm A with the flag at a value IN ITS OWN UNIT chosen from
   arm B (B's count of that unit × 3) — the exit code, whether the export is written, its `outcome`, the tokens, and the
   count of that unit actually consumed (does a "turn" mean a model request, a user turn, or a tool step? — read it off the
   journal). This is the cell a cap would rest on.
6. Never `--trust`, `--add-dir`, a home under TEMP, or the owner's `~/.vibe`. Budget: stop starting arms once the arms
   together pass 1 000 000 input tokens; the worst case is therefore ~1 M plus one arm A (~7 500 tokens a second × 90 s
   ≈ 0.7 M), ≈ 1.7 M — said in advance, so the owner can refuse it.

**Output.** `research/RESULTS_vibe_confinement.md` §28: method, predictions against outcomes, the tool-call table, the
flags verbatim. **Redacted before commit:** the question and context are paraphrased, every path is replaced by its role
(`<granted root>`, `<outside marker>`, `<home>`), the profile name never appears; the evidence folder
(`testsToDelete-vibe-qloop-<id>`) stays local.

**Decision table for the cap (D6, gate G3) — the code is written only after §28 exists** (it does; row 1 applied, with
`--max-turns` as the flag and 20 as the value for the reasons in D6):

| What §28 shows | What the fix does |
|---|---|
| a bounding flag whose unit, its behaviour at the limit (ends with an export, a readable `outcome`) and its relation to what the loop consumed are all VERIFIED 3 of 3 in arm C | the flag is set for a non-review Vibe launch (question row, consult turn) at a value in THAT unit — `max(<unit's floor>, 3 × arm B's count of that unit)`, the floor named in §28; the outcome read as a failure with its own sentence ("Vibe stopped at coai's {unit} cap ({N}) — the question may name a file outside the granted folders"); scenarios red-first like §4 |
| a flag, but its unit does not map to what the loop consumed (a price flag on a subscription that reports no price; a "turn" that counts user turns while the loop is tool steps), or arm C is not 3 of 3 | no cap from it; recorded in §28 as why |
| no usable flag, H1 holds | not decided here: the question goes to the owner with the options and their costs — a lower question-row `--time-limit`, a question naming a path outside the row's granted roots refused or flagged before launch, or a prompt line telling the row a denied read is final (each a measured arm before it ships) |
| neither H1 nor a flag | §28 is the deliverable; a fresh `todo/` plan carries what it found |

### M-V18 — long paths ON, and a `\\?\` home (a follow-up; nothing in this plan waits for it; it must also measure 8.3 short names — the budget counts `GetFullPath` as written)

**Question.** Can the budget be relaxed — does Vibe 2.26.1 write past 259 characters (a) with `LongPathsEnabled = 1`, or
(b) given `VIBE_HOME=\\?\<full path>` with the flag at 0? Today D2 refuses on Windows whatever the flag says, because the
flag is a promise to long-path-AWARE processes and nobody checked that Vibe's interpreter is one.

**Method.** Free first: Vibe's own `python.exe` (`%APPDATA%\uv\tools\mistral-vibe\Scripts\python.exe -I`) writes and
`os.replace`s files at 258 / 259 / 260 / 300 characters under `C:\testsToDelete-mp<n>` — §1's probe, re-run in each arm.
Arm (a) needs an administrator to set the flag and a sign-out: ON A SCRATCH MACHINE OR VM only, never the owner's without
their explicit yes. Arm (b) needs no admin. Then, per arm that passes the free probe, one paid tiny turn (`Reply with
exactly: OK`) and one `--resume`d turn on a home of 130 characters (so the chunk lands at ~287), 3 of 3: exit, export,
the session written and resumed, the home validation still passing (does `\\?\` survive into every path Vibe builds?).

**Output and use.** RESULTS §29. A follow-up plan may then give `VibePathBudget` a measured exemption (a) or have
`VibeLaunch.Built` hand Vibe a `\\?\` home (b) — each only with its cell 3 of 3.

## 7. What grows, who retires it, what an interruption leaves

- **`<root>\coai-vh\`** — per Vibe reviewer ~85 KB of unpacked plugins plus its session (~130 KB for two short turns,
  §22, §24), so ≈ 0.25 MB × the round's Vibe reviewers, one folder per round, row or check. Retired by its owner when the
  round, row or check ends (A7–A10); a crash leftover by the six-hour sweep (B1, D3), which runs at most every ten minutes
  on a round's build and on the consultation beat. A server killed with a Vibe child alive leaves the child writing until
  its `--time-limit`, minutes — far inside six hours; the OS frees the dead server's lock.
- **`<root>\coai-vibe\<id>\h`** — unchanged in size and owner (D20). A 0.54.0 conversation's `vh-<10>` home is left inside
  its folder and goes with it (D20); its next turn starts on an empty `h` and fails its `--resume` as the exit it is —
  no regression: under 0.54.0 no second turn could run at all (defect 2).
- **An abandoned root** after the setting changes — not swept (D1); bounded by what one root held.
- Nothing new in TEMP; the probe's home is unchanged.

## 8. Build order

1. **D** (failure kind and the whole cure) — smallest, independent: D1–D6, then the JSON and the extension label.
2. **C** (tmp by name, data as folders only, the starters, the script's classifier, listing and `deepestChild`) — C1–C8.
3. **F** (the audit of the existing tests) — before the first budget commit, so the suite stays green on this machine.
4. **A** (`VibePathBudget`, `VibeHomes`, the budget in the launch control, then each site: round, row, check,
   conversation) — A1–A12, in that order.
5. **B** (the one-shot sweep, its lock and its wiring) — B1–B6, with B5's break-it observation recorded in the summary.
6. **E and D7** — only after Fable's review of D7 (the coordinator sends it); if D7 changes, E changes with it.
7. **K** (the turn cap and its sentence, D6) — K1–K6, with K3's break-it recorded.
8. **D8** (the extension setting, its census row, its page field, its five help lines).
9. Docs (§3's last row), `plan-lifecycle.mjs`, the whole suites.
10. M-V17 is done (RESULTS §28). M-V18 is a follow-up; the loop's cause is
    [PLAN_a_vibe_row_is_told_what_its_folders_hold.md](PLAN_a_vibe_row_is_told_what_its_folders_hold.md).

## 9. Not in scope

- The conversation folder's 32-hex name (D1: kept for D20; u ≤ 34, then the budget refusal).
- The round's `--output-dir` export folder (`%TEMP%\coai-answers-*\vx-<10>\export.json`, TEMP + 52) and a reviewer's
  `read_file` of a deep file in a snapshot under a long TEMP — the first fails only for a TEMP over 207 characters, the
  second fails ONE read (`tool_failed`), not the run.
- Kimi's homes (`coai-kimi`): a 280-character session path was written and read back (D19), so its depth is not Vibe's.
  D7's private-folder rule applies to Kimi's root when its adapter lands — noted for D19, not built here.
- WSL and Linux (D18 refuses Vibe there).

## 10. Definition of Done

- [ ] Every scenario of §4 was watched red for the stated reason before its fix, then green; the summary quotes each
      group's first red message and the green run; A2's and B5's break-it observations are recorded.
- [ ] No Vibe home that writes a session is under TEMP; on Windows with the default root every site fits the budget up to
      the profile lengths in D1's table, and past them the launch is refused BEFORE the home is made, with D2's sentence;
      off Windows no budget is applied.
- [ ] A round, a question row and a check delete their homes on every way out; a crash leftover under `coai-vh` older
      than six hours is swept and a locked one never is, from either process; the D20 sweep never touches `coai-vh`.
- [ ] A conversation's second turn and a reviewer's repair start on the home the first launch left (C6, C7); a file under
      `plugins/data` and a starter under `plugins/tmp` are still refused.
- [ ] A launch-control refusal is `launch-refused` everywhere a failure kind is written and its whole sentence reaches the
      person; only a `--version` that could not start for error 2 or 3 is `cli-not-found`.
- [ ] D7 as Fable's review left it; D8's setting on the panel in five languages.
- [ ] The F audit's per-class outcome is in the summary.
- [ ] `CoaiMcp.Tests.exe` (whole), `npm --prefix src_vs_code test`, `node --test "scripts/**/*.test.mjs"`,
      `dotnet format --verify-no-changes` and `node .agents/conventions/tools/plan-lifecycle.mjs` all clean.
- [ ] D17 and D20 of the parent plan amended (dated 2026-10-15), the module docs updated. (RESULTS §27 — the three live
      runs and the MAX_PATH probe — and §28 — M-V17 — were written with this plan's revision.)
- [ ] Every Vibe consultant launch (question row, conversation turn, consultant check) carries `--max-turns 20` and no
      review does; a `turn_limit` outcome reaches the person as coai's sentence with the usage read (K1–K6).
- [ ] M-V18 extracted into a fresh `todo/` plan when this plan is promoted (it is a follow-up, nothing here waits for it).
- [ ] One live Vibe round, one two-turn `consult` and one question row through the built server on Windows, each
      answered, recorded in RESULTS (the parent plan's DoD row this fix unblocks).

## 11. Gate and review — what changed (2026-10-15)

- **Code gate 2026-10-15: 4 findings accepted, 1 rejected — partial is LibraryImport's.** Accepted: `LaunchTurn.Refused` takes a required `RefusalCause` (no default); the descendant walk refuses a child whose OWNER is not the person or Administrators (the limit sentence it replaced said a planted child keeps its owner); `turn_limit` / `token_limit` say only that the cap stopped the launch before it answered, and the file-search guidance is the question row's alone (`IConsultantRuntime.QuestionRowAdvice`); the manifest script builds one classifier per inventory. Deviation: the owner test on a real SYSTEM-owned child (E5b) skips unless elevated, like E5; its rule is tested on a bare descriptor.
- **Review round, 2026-10-15 (an Opus judge and a cadence consultant): FIX.** D7: ONE handle is opened (with a data right and, when it can,
  WRITE_DAC), verified, re-ACLed and HELD (the first version verified one handle and held another, opened by path); after a re-ACL every
  descendant is walked — links never entered — and reset to inherit (propagation leaves explicit entries and protected DACLs), only when the
  parent needed the re-ACL; a child's planted OWNER keeps an implicit WRITE_DAC (stated). E5's token-privilege struct was laid out wrong, so it
  never ran; fixed and restored after. `COAI_CONSULTANT_HOMES` is judged at startup (a `SettingProblem`) and drawn as a mark on the page.
  `Exited` follows the scheduler's precedence (an announcing stderr line first); the cap wording is neutral; the Vibe end-to-end tests no longer
  depend on TEMP's length (a 55-character TEMP reproduced 8 failures); `plugins/data/**`; three exception-path leaks; the structural test widened;
  a dangling `coai-vh` is said. Deviation: B3's "second process" is a second open in the same process. M-V18 must also measure 8.3 names (the
  budget counts `GetFullPath`, which keeps them as written).
- **D7 per the security review, 2026-10-15** (ACCEPT-WITH-CHANGES): one `PrivateFolder` type is the only entry into `<root>\coai-*`; the
  root must exist and is never created; folders are made with a protected DACL and verified by handle (path, owner, DACL), re-ACLed when
  only the DACL is wrong and refused when the owner is another; the handle is held; the export moves beside the home; a `windows` CI job.

The coai plan gate returned `proceed` with three accepted findings; an independent Opus review returned REVISE with six
blocking findings and a set of minor ones. Each, and where it landed:

- **G1** — the long-paths exemption dropped (the flag is no proof Vibe's interpreter is long-path aware, and that branch
  was unmeasured); on Windows the budget always applies, off Windows none; `LongPaths.cs` is gone; M-V18 written as its own
  brief → D2, §6 M-V18, A3.
- **G2** — only `NativeErrorCode` 2/3 is a missing CLI; other codes and a `--version` with no version are `launch-refused`
  with reworded cures (justified: a CLI that started is installed) → D5, D4 scenario.
- **G3** — the cap's value is defined and measured in the flag's own unit, and added only when that mapping is verified →
  §6 M-V17 step 5 and its decision table.
- **Gate, rejected** — dropping the empty-root fallback (test-only) → D1's last-but-two bullet; a deeper newest-write scan
  in the sweep (the lock protects a live home, not the age) → D3.
- **R1** — the cure was cut at 240 characters: one sentence kept whole under `Redaction.TitleLimit`, decided over a
  finding/cure split, with D1/D6 asserting the cure's words → D5, D1, D6.
- **R2** — the script's classifier checked the pinned prefix first and would hash a folder: varying first, a folder under
  the prefix admitted only when it leads to a pinned file, folders listed and never hashed, a node test; D4's earlier
  claim that the script "classifies folders like files" corrected → D4, C8.
- **R3** — the script cannot see the transient 156-character path: `deepestChild` (persisted + 22) recorded per version,
  with a parity test against the budget; D2's earlier claim corrected → D4, C5.
- **R4** — existing tests build a Windows `VibeRuntime` on ubuntu/macOS and use deep TEMP homes on this machine: the
  budget is the PROCESS's platform, a `VibePathBudget` seam on `VibeRuntime`, and an audit step listing every class → D2,
  §4 F, build order 3.
- **R5** — link safety on `coai-vh` itself (sweep and reservation), and the `C:\coai` example dropped; private folders with
  a protected DACL and a refusal of a foreign or open one, marked for Fable → D3, D7, B4, §4 E, build order 6.
- **R6** — the recorder answers `--version` with `vibe 2.26.1` and records every invocation → §6 M-V17 step 2.
- **Minor** — Vibe's source cited for `plugins/tmp` (staging, re-digested) and `plugins/data` (adopted runtime code, so
  FOLDERS only) → §1(2), D4; the 259/247 limits and the UTF-16 unit probed with Vibe's own Python (free) and the 247
  folder limit shown not binding → §1(1), A2's `é` fixture, D2's `GetFullPath`; two servers on one root → the lock in D1/D3,
  B3; `PruneOldAnswerDirs`' lambda and B6's access → D3, B6; A6 sets `TMP` and `TMPDIR`; A12 asserts `ReserveOneShot("")`
  instead of looking at the machine; C3's shape-check half is green today and said so; `FileProblem` at CC 4 →
  `VaryingProblem`; the env-only cure → a panel setting in five languages, and the orphaned old root stated → D8, D1; the
  arithmetic (round u ≥ 35, row u ≥ 34, A5's 109, the probe's 221) → §1, A5; M-V17's hygiene (redaction, a synthetic
  marker, the worst-case spend, `C:\testsToDelete-vq<n>`) → §6.
- **M-V17 measured** (the scout's redacted draft folded into RESULTS §28, its subheadings demoted, one paragraph's
  PowerShell-escape damage repaired; the draft file removed) — H1 not as worded, H4 held; `--max-turns` and `--max-tokens`
  each 3 of 3 with an export and an outcome → §1(4); D6 rewritten from "no cap before a measurement" to the cap itself
  (`--max-turns 20` on consultant launches only, `outcome` read, coai's sentence), with the flag and the value argued;
  §4 K; §6 marked measured; build order 7; DoD. The loop's CAUSE (no listing) and the side finding (`bash`,
  `run_typescript`, `search_tool_functions` callable despite `--enabled-tools read_file`) are NOT in this pull request:
  both are unmeasured cures, the second a confinement question → extracted to
  [PLAN_a_vibe_row_is_told_what_its_folders_hold.md](PLAN_a_vibe_row_is_told_what_its_folders_hold.md).
- **RESULTS §27** written now as well (the three live runs and the free MAX_PATH probe), so the evidence outlives its
  `testsToDelete-` folders.
