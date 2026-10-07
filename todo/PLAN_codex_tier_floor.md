# PLAN — codex 0.110–0.130 are never told a tier they refuse

> Status: **plan only, nothing implemented yet (2026-10-07).** A regression fix for coai-mcp 0.44.0 (fast mode, PR
> #693). Scope: `CodexRuntime.TierArgs` and what it reads, `VendorDiagnosis`, the codex row of the `fastMode` block in
> `shared/feature-availability.json` (and the extension's generator, which checks that block), the docs.
>
> Related docs: [RESULTS_codex_service_tier_versions_2026-10-07.md](../research/RESULTS_codex_service_tier_versions_2026-10-07.md)
> (the measurement), `PLAN_fast_mode.md` and its extracted tail `PLAN_fast_mode_api_tier.md` (this replaces that tail's
> "codex version floor" item).

## The symptom

Since coai-mcp **0.44.0** every codex launch whose row is Off — the DEFAULT — carries `-c service_tier=default`
(`ReviewerRuntime.cs:422` `TierArgs`, read by the reviewer argv at `:459` and by all three `CodexConsultant` branches,
`CodexConsultant.cs:75`, `:77`, `:83`). Measured today: **codex 0.110.0 through 0.130.0 refuse it at config load**
(`Error loading config.toml: unknown variant \`default\`, expected \`fast\` or \`flex\` in \`service_tier\``, exit 1,
before any request); 0.107.0 and older ignore the key; 0.131.0 and newer accept it. So on those releases EVERY codex
review, consultation and question row fails, with a sentence that names a config file the person never edited.
`fast` is accepted everywhere measured.

## What changes

1. **The refusal is said with its cure** (`VendorDiagnosis.cs`, the `(Marker, Sentence, Kind)` table at `:47-101`, the
   specific-first order the `--effort` row at `:92-96` follows): marker ``unknown variant `default`, expected `fast` or
   `flex` `` → *"the installed codex (0.110–0.130) cannot be told the standard tier — update codex, or set this row's
   fast mode to 'As the CLI is set'."*, kind `UnknownOption`. Lands first and alone: it helps a person on 0.44.0 the
   moment the next build ships, whatever else changes.
2. **A release that refuses Off is not told Off.** The refusing range is DATA, beside the measurement that found it: the
   codex row of `fastMode` gains `"refusesStandard": { "from": "0.110.0", "through": "0.130.0" }`, read by
   `FeatureAvailability` (and allowed by the extension generator's `FAST_FIELDS`, which refuses unknown fields). For a
   codex in that range Off sends **nothing** (it has no way to say "standard"); On still sends `fast`; As the CLI is set
   is unchanged. A version that cannot be read, or does not parse, is sent what it is sent today.
3. **How `TierArgs` learns the version — decided by the plan round: a probed field, asked asynchronously, never
   cached** (the round's findings 0, 1, 2, 5; the synchronous cached reader this section first recommended is dropped).
   - `CodexTierSupport.ProbeAsync(launcher, executable, workingDirectory, ct)` — the shape of
     `ClaudeCapability.ProbeAsync` (`ClaudeCapability.cs:111`): `codex --version` through the shared
     `IProcessLauncher`, which bounds it with a timeout and kills the whole process tree, parsed as `codex-cli X.Y.Z`,
     answered as `Accepts`, `RefusesStandard` (in the data's range) or `Unknown` (it did not start, timed out, or did not
     parse). Asked on every launch path, never cached — the reason `ClaudeCapability.cs:44-47` gives (in-place upgrades,
     a symlink or version-manager shim switching under the same path) holds for codex, and the cost is the same order:
     **measured 442–601 ms** for `codex --version` on this machine (five runs).
   - The answer lives on `ReviewerSettings.CodexTier` (beside `ClaudeCli`, `ReviewerRuntime.cs:159`), default
     `Unprobed`, which `TierArgs` treats exactly as today (it sends the tier) — so nothing changes until a site probes,
     and a test per site proves each one does.
   - The sites: the reviewer roster asks once per distinct codex executable per round, before `RosterBuilder.BuildWork`
     (`PanelService.cs:636`, `:809`, `:1158`; the security lane through `Security()`, `:805`), and the roster's
     `SettingsFor` (`RosterBuilder.cs:373`) copies the answer onto each codex row's settings; a consultation and the
     consultant check through a `CodexConsultant.PrepareAsync` override (`IConsultantRuntime.cs:167`, as
     `ClaudeConsultant.cs:74-87` does); a question row in its own async turn (`QuestionRowLaunch.cs:135-140`), for codex
     rows only — its claude rows are a separate open plan (`PLAN_a_question_row_on_an_old_claude.md`).
   - `Unknown` sends what is sent today. Rejected alternative (the round's finding 4): omitting the tier when the version
     cannot be read would let a person's own `service_tier = "priority"` make every review fast and 2–2.5× the cost
     unasked — the reason Off exists. `Unknown` arises only when `--version` cannot run (the launch then cannot run
     either) or prints something new (only a future release, which accepts `default`); and change 1 names the refusal
     with its cure if it is met anyway.
4. **`--providers`** already reports the requested `fast`; a codex in the refusing range reports `off` with a note that
   the release cannot be told it, so the card does not claim a state the launch did not send.

## Build order

1. The diagnosis row (RED: `VendorDiagnosis.Classify` on the measured sentence names it).
2. The data field and both readers (RED: a seed with the range is read; the generator accepts it and refuses a
   malformed one; `FastModeIsDataTests`).
3. `CodexTierSupport` — probe and parse (RED: `codex-cli 0.130.0` → `RefusesStandard`; `0.131.0`, `0.107.0` →
   `Accepts`; garbage, a CLI that does not start, and one that hangs past the timeout → `Unknown`, the hung child
   killed; a second launch asks again — the fake CLI counts its calls).
4. `TierArgs` reads `CodexTier` (RED: Off on `RefusesStandard` sends no `service_tier`, On sends `fast`, `Unprobed` and
   `Unknown` and `Accepts` send `default`), then each site fills it (RED per site, against a fake codex answering
   `codex-cli 0.120.0`: a review round's codex reviewer, the security lane's codex, a consultation, the consultant
   check, and a question row — each launch's argv carries no `service_tier` for Off; with `0.160.0` it keeps `default`).
5. `--providers` (RED: a codex row on a fake `0.120.0` reports `fast: "off"` with the note; on `0.160.0`, `off` and no
   note; On reports `on` either way); docs: `research/module_runners.md`, `module_server.md`, the fast-mode plan's tail.

## Test plan

Every step above starts RED. The existing argv pins (`AFastModeReachesEveryCodexLaunchTests`,
`AReviewerStartsNoMcpServersTests`, `ConfinementPlannerTests`) build settings directly, so they stay on `Unprobed` and
keep `default` unchanged — the per-site tests are what prove the probe is wired. The seam's fast-tier leg is unchanged
(no extension behaviour changes). Full C# and extension suites, the seam, the family checks.

## Definition of Done

- [ ] A codex 0.110.0–0.130.0 is never sent `service_tier=default`; one outside the range is, exactly as today.
- [ ] The refusal, if met anyway, is named with its cure.
- [ ] The range is data with the measurement linked; both halves read it.
- [ ] RED first for every step; all suites green; module docs updated; the coai plan and code gates passed; PR merged.
- [ ] Released only when the owner says so (mcp 0.44.0 is out with the defect — the owner is told).
