# PLAN — Mistral Vibe and Kimi Code as coai runtimes, on the person's own subscription

> Status: **plan only, nothing implemented yet, 2026-10-11.** Scope: `src_mcp` (two new reviewer runtimes and two
> consultants), `shared/*.json` seeds and their generated TypeScript, `src_vs_code` (runtime lists, presets, install,
> the catalog model card), docs. Chat, the Team server and the bench are out (§8). Kimi's measurement — and everything
> after it — is gated on the owner signing in (§6). Every probe number below is n=1 on Windows; nothing is claimed for WSL
> until the probe scripts have run there.
>
> Related docs: [RESULTS_vibe_and_kimi_first_probe.md](../research/RESULTS_vibe_and_kimi_first_probe.md),
> [module_runners.md](../research/module_runners.md), [module_extension.md](../research/module_extension.md),
> [RESULTS_agy_write_block.md](../research/RESULTS_agy_write_block.md),
> [PLAN_the_consultant_has_its_own_vendors.md](../research/PLAN_the_consultant_has_its_own_vendors.md).

## 1. Goal

The owner bought a Mistral subscription and signed in to the `vibe` CLI (2.26.1), and wants Kimi Code (`kimi` 0.41.0,
kimi.com) as well. coai can reach Mistral today only as an API-key endpoint: a vendor row on the `codex` runtime with
`baseUrl https://api.mistral.ai/v1`, its key in `MISTRAL_API_KEY` as derived from the id
(`src_mcp/runners/Reviewers/CustomRuntime.cs:10-17`). That never
touches the subscription, and nothing at all reaches Kimi Code. So, when this plan is done:

1. A person adds **Vibe** or **Kimi** from the catalog's "Add a reviewer…" presets, picks a model, and the row reviews plans
   and code with the CLI's own sign-in — no key in the vault.
2. The same rows can answer `consult` and a question-consultant row.
3. **Neither CLI can WRITE to the checkout or the machine through coai, and neither starts an MCP server** (the #514 rule,
   `src_mcp/runners/Reviewers/NoMcpServers.cs:8`) — to be measured 3 of 3 per side by the probe scripts before any code
   claims it; the first probe is n=1 on Windows. This is the hard requirement; that probe already found two ways it would
   have failed (§2 D2, D3). READS are bounded only as far as M-V6 / M-K6 measure, and each runtime's question-consultant
   `disk` standing says exactly that — a prompt-injected branch asking `read_file` for `~/.ssh` is a leak path this plan
   measures and states, not one it claims to close.

## 2. Decisions (with the evidence for each)

Settled with the consultant (consultation `82485649`, codex `gpt-6-astra`, two turns; every claim it made was checked
against the code or a run before it went in here — the question consultant had no row switched on).

- **D1 — one class per vendor behind `IReviewerRuntime` / `IConsultantRuntime`.** `VibeRuntime`, `KimiRuntime`,
  `VibeConsultant`, `KimiConsultant`, each registered in the one selector; no `if` on a vendor name (owner rule).
- **D2 — Vibe's read-only launch is `--agent plan --enabled-tools read_file`, both, always.** `--agent plan` alone wrote into
  Vibe's private scratchpad; `--enabled-tools read_file` alone denied every write; both together denied 39 of 39 write
  attempts, including `run_typescript` reaching `tools.file_system.*` (RESULTS §2, n=1, Windows). Both, because the next
  Vibe version may change one of them — but they share ONE failure mode (Vibe's own tool handling), so they are not the
  two independent blocks agy has (`AntigravityReadOnly.cs:14-18`). The second, independent block is D6's working folder.
- **D3 — Vibe is NEVER launched with `--trust` or `--add-dir`.** A project `.vibe/hooks.toml` runs its shell command under
  the full flag set when the folder is trusted, and `--add-dir` trusts its folder (RESULTS §3). A branch under review can
  carry that file. Without `--trust` the headless run still works and `read_file` still reads the checkout.
- **D4 — a pre-launch trust guard.** A person who once trusted their repository in interactive Vibe reopens D3 for a
  consultant running in the live checkout. Before every Vibe launch, coai reads `trusted_folders.toml` from the child's
  effective `VIBE_HOME` — resolved from the SAME environment the child is given, which differs between a reviewer launch
  (inherited) and a question row (`ProcessEnvironment.Minimal`, `ConsultantLaunches.ForQuestion`) — resolves junctions and
  aliases, and **refuses with a named reason** when the working folder or any ancestor is trusted, or when the file exists
  but cannot be read or parsed. An ABSENT file means go (a fresh install has none). Preferred over an env override because
  none is documented. It is a pre-check, not a sandbox, and it cannot be written before M-V4 records the file's entry format,
  Windows case handling, ancestor semantics and the WSL `/mnt/c` versus `C:\` spelling — the probe saw only an empty file.
- **D5 — a launch is refused while the person's own hooks are configured** (plan gate, codex, 2026-10-11: a notice would
  leave the hard requirement of §1 unmet). `~/.vibe/hooks.toml` (in the child's effective `VIBE_HOME`) loads in an untrusted
  folder too — documented, not measured; M-V9 measures it. A reviewed branch cannot write it (D2), so it is not an attack path,
  but any hook in it can change the checkout during a coai launch. So when that file declares a hook (or cannot be read or
  parsed), the trust guard of D4 refuses the launch with a sentence naming the file, the row's health line and the
  `providers` probe say why, and the person chooses: remove the hooks, or not use Vibe through coai. No switch to override it
  in this plan.
- **D6 — Vibe's working folder is ALWAYS a coai-owned empty folder, never the checkout; coai never moves `VIBE_HOME`.**
  The branch under review controls everything in the checkout, including `.vibe/hooks.toml`, `.vibe/agents` (which could
  redefine the built-in `plan` agent), `.vibe/tools`, skills and `AGENTS.md`. Vibe documents all of them as loading only in
  a trusted folder, and D3 never trusts one — but that is Vibe's handling again, so M-V10 measures each with a canary. And
  D4's guard is a check-then-launch: a folder trusted between coai's read of `trusted_folders.toml` and the child's start
  would load the checkout's hooks (code gate, codex, 2026-10-11). A working folder coai owns — created fresh per launch, as
  `AntigravityReadOnly.Home()` gives agy — holds none of those files whatever Vibe does or whoever trusts what, which
  closes both: the second independent block beside D2, and no race left for D4 to lose. It rests on `read_file` reading the
  checkout by absolute path without `--add-dir`, which only M-V11 can say. **If M-V11 says it cannot, S1 and S2 stop there
  and are re-planned** — there is no fallback to the checkout as the working folder, because that would leave the hard
  requirement of §1 resting on a check that can lose a race. D4 stays as defence in depth for the checkout's ancestors and
  the person's own folders. `VIBE_HOME` stays where the person
  has it: the sign-in is assumed to live there (M-V7 confirms), and a deep `VIBE_HOME` broke on a >260-character path
  (RESULTS §4).
- **D7 — the prompt on stdin, the answer from `--output json`, usage from `--output-dir`.** Answer = the last
  `role: assistant` message; session id = `sessionId` on any element (no `ReadHandle` interface change needed). The export
  folder is a per-ATTEMPT folder (a retry never reads a stale export) **inside the launch's own `coai-answers-*` scratch
  directory**, and is **deleted right after `ReadUsage`/`ReadHandle` read it**. Not beside the consultant's answer files:
  `ConsultantArtefactSweep` enumerates files only (`src_mcp/src/Server/Consultation/ConsultantArtefactSweep.cs:22`) and
  would never remove the tree. A crash between launch and read is covered by the sweep that already exists for those
  directories (§7). `--output streaming` only if a measured event carries something the array does not. Stdin is settled
  for 260 characters only; a review prompt is ~33 KB, so D7 stands pending M-V8.
- **D8 — exit 3 (a limit) is a failure** even when an assistant message exists. `--time-limit` is set a few seconds under
  the row's timeout, on the documented promise that Vibe writes its export on every exit ("interrupt the run …, write the
  export and exit 3") — not yet measured; M-V13 stops a run on a limit and checks the export exists.
- **D8b — no MCP server starts** (the #514 rule; `NoMcpServers.cs:8`; Claude and Codex have launch switches at
  `ClaudeRuntime.cs:97` and `CodexConsultant.cs:102`; the CLIs with none are warned about at `src_mcp/src/Program.cs:2178-2187`).
  Vibe reads `mcp_servers` from its config and says `VIBE_*` overrides any config field, so the first candidate is
  `VIBE_MCP_SERVERS=[]` in the child's environment (M-V3b: a user-level server with a startup canary, with and without it).
  If that holds, it is Vibe's `NoMcpServers` arm; if it does not, the trust guard refuses a launch while the person's config
  declares a server, as D5 does for hooks. Either way it is ONE launch control that every Vibe launch path goes through —
  the reviewer, `consult`, and the question row's `ConfinementPlanner` arm with its minimal environment — never a flag one
  path adds and another forgets (code gate, codex, 2026-10-11); a test per path. Kimi: the same question in M-K6, answered
  the same way, under the same one-control rule.
- **D9 — cost is recorded as `null`, tokens as reported.** `cost_usd` is Vibe's list price even on a subscription, and
  `lineCost` treats any numeric `costUsd` as a bill (`estimated: false`, `src_vs_code/src/usage.ts:395`). A missing export
  is `Usage.Unknown`, never zero. Carrying Vibe's estimate as an estimate needs provenance through the ledger — not this plan.
- **D10 — models: a curated, version-labelled alias list, typed aliases allowed, the saved choice kept.** Vibe takes the
  alias through `VIBE_ACTIVE_MODEL` (no `--model` flag), set in the launch request's EXPLICIT environment so it reaches a
  question row too, whose child gets only `ProcessEnvironment.Minimal` (`ConsultantLaunches.ForQuestion`, pinned by
  `ChildEnvironmentTests.cs:33-39`) — M-V12 checks that Vibe finds its home and sign-in under that environment, and a test
  covers each of the two launch paths. First list `mistral-medium-3.5`, `glm-5-3`. Kimi takes `-m <alias>`; its list comes
  from the Kimi measurement (M-K7). Discovery only once a CLI command lists the
  models without a model turn. The terminal ▶ launch gets the same env/flag — today an unknown runtime falls back to
  `codex -m …` (`src_vs_code/src/vendorTerminal.ts:35`).
- **D11 — presets only.** Neither runtime enters `DEFAULT_VENDORS` or the server's default `Providers`.
- **D12 — machine-only until the Team server is planned.** `VendorConfig.KnownRuntimes` is every runtime minus
  `MachineOnlyRuntimes` (`src_server/src/Vendors/VendorConfig.cs:45`, `src_mcp/runners/Reviewers/ReviewerRuntime.cs:788`),
  so both names go into `MachineOnlyRuntimes`, or the Team server would accept rows it has no CLI for.
- **D13 — mixed versions: what is guarded, and what is not.** An unknown runtime becomes `codex` on read, for RENDERING
  (`src_vs_code/src/vendors.ts:376`). Three paths, narrowed by the plan gate (codex, 2026-10-11):
  1. *An older server* — held back by `crossesTo` (`src_vs_code/src/vendorsWire.ts:112-114`), which today tests only
     `api` against `API_RUNTIME_SINCE`. NOT a plain reuse: a server-version constant per new runtime and an arm for each
     in `crossesTo`, with a test that a `vibe` row never reaches a server older than that version.
  2. *An older extension window writing the server's settings file* — already guarded: the file records which build wrote
     it and an older build stands down (`src_vs_code/src/serverSettingsSync.ts`, the 2026-09-07 `remote` incident). A test
     that a host whose `RUNTIMES` lacks `vibe` does not overwrite a `vibe` row in that file.
  3. *An older extension's own save of `coai.vendors`* — NOT guarded: it rewrites the row's runtime to `codex`, and the
     newer host then mirrors that. This is the mixed-version limit `module_extension.md:3626` already records for
     `remote` and `api`; the extension cannot change code that already shipped. The CHANGELOG and the help say to reload
     every window after updating, and the limit is written into `module_extension.md` beside the existing one.
- **D14 — a billing refusal is hopeless, not a rate limit.** `suspended due to insufficient balance` goes into BOTH
  `RateLimit.Phrases` (`ReviewerExecutor.cs:251`) and `RateLimit.Spent` (`:304`) — `Reason` filters through `Marked`
  first (`:341`), so `Spent` alone would lose the line. Generic, small, independent of either vendor's runtime.
- **D15 — Kimi's read-only story is unknown and must be measured before any code.** In `-p` mode Kimi asks for no approval
  and runs under its `auto` policy; only static deny rules hold (`kimi` command reference), and `--plan` is refused with
  `-p` (RESULTS §5) — documentation and one refusal, nothing more. The candidate is an `--agent-file` whose tool list holds
  only a read tool, plus static deny rules;
  `--agent-file` cannot be combined with `--session`, and a resumed session restores the agent bound at creation — so the
  resumed turn needs its own write check.
- **D16 — official installers only.** Vibe: `uv tool install mistral-vibe` (what is installed here) or Mistral's own
  script; Kimi: Moonshot's own installer. Both go into `OFFICIAL_SOURCES` (`vendorTerminal.ts:346`), which a test pins.
  Neither is on npm, so the update light needs a version source other than npm (`cliVersions.ts:25` has npm and manifest
  only) — or no light, which `versionSourceFor` already allows.

## 3. What exists today (verified on `main` 722fc597, 2026-10-11)

Server — `ReviewerRuntimeSelector.Default` `ReviewerRuntime.cs:756`, `RuntimeNames` `:771`, `MachineOnlyRuntimes` `:788`,
`Named` `:800`; `RuntimeResolution.cs:94-145` needs no edit; `PanelSettings.RuntimeOf` (`PanelSettings.cs:1411`) keeps a
name it knows from `RuntimeNames` — checked, no edit; `VendorDiagnosis.cs:47-112` (known failures) and
`InstallCure` `:260`; `VendorProbe.cs` runs `--version` through `DefaultExecutable`; consultants `ConsultantResolution.cs:34`
`OnACli` (the `Consulting` list is read from the seed); question consultant `RuntimeCapabilities.cs:84` (a hand-written
list, NOT the seed) and `ConfinementPlanner.cs:126-133`, whose default arm (`:132`) gives an unknown runtime **no confinement
and empty flags** — a runtime may never enter `runtime-capabilities.json` without its own arm there.

Seeds — `shared/feature-availability.json` (`runtimes` `:23`, `features.consultant` `:25`, one `effort`, `thinking` and
`fastMode` row per runtime, enforced by `FeatureAvailability.cs:187-225`); `shared/runtime-capabilities.json` (`runtimes`
`:22`, three rows per runtime — `none`, `disk`, `web` — each citing measured cells); `shared/capability-matrix-vectors.json`;
`shared/consultant-limitations.json` (one row per consulting runtime × side). Generated by
`src_vs_code/scripts/generate-feature-availability.mjs` and `generate-runtime-capabilities.mjs`; held by
`generatedFilesAreCurrent.test.ts`, `capabilityAdmission.test.ts`, `featureAvailability.test.ts` and their C# twins.

Extension — `RUNTIMES` `models.ts:35` (an unknown runtime in `modelsFor` `:184` falls to the Gemini list), `modelsProvenance`
`:322`; `VENDOR_PRESETS` `vendors.ts:234`; `vendorTerminal.ts` maps (`EXECUTABLE` `:35`, `SELF_UPDATE` `:112`, model flag
`:187`, `DOCS` `:267`, `vendorInstall` `:288`, `OFFICIAL_SOURCES` `:346`); catalog `catalogWriteRules.ts:50` `JUDGED_HERE`,
`modelCardWorld.ts:97` and `setupTab.ts:18` `CLI_RUNTIMES`; `panelView.ts:1186` `KNOWS_ITS_OWN_ENDPOINT`;
`vendorColour.ts:92` `ANCHORED`; `package.json` runtime enums (vendor rows and consultants); help in five languages
(`helpContent.ts`, `help.ts`, `helpRu/Uk/De/Es.ts`).

Tests that hard-code runtime lists: TS `runtimeSurvives.test.ts:89`, `vendorTerminal.test.ts:116`, `cliVersions.test.ts:103`,
`addModelGroups.test.ts:8`, `vendorColour.test.ts`, `featureAvailability.test.ts:64`, `capabilityAdmission.test.ts:143`,
`consultant.test.ts:616`, and the refusal text at `capabilityAdmission.test.ts:157`; C# `FeatureAvailabilityTests.cs:71`,
`CapabilityMatrixTests.cs` (lists, and the refusal text at `:211`), `ConfinementPlannerTests.cs:172`, `:352`, `:363`
(`Adapters()`) and the refusal text at `:141`, `ConsultantLimitationsTests.cs`, `ApiConsultantTests.cs:110`
(`QuestionResolution.Answering`), `EveryAdapterRecordsWhatItLaunchedTests.cs:73`, `ChildEnvironmentTests.cs:33-39`. The loops over
`RuntimeNames` (`VendorRuntimeSurvivesParsingTests.cs`, `AnUnknownRuntimeIsRefusedByNameTests.cs`) pick the new names up.

## 4. Build order

The coai gate runs once for the whole plan — one plan round on this document (done) and one code round over the whole
committed diff of every story below, with `again: true` for a checkpoint when a later story lands. No story is gated on
its own: each is built on one implementation branch (this plan merges on its own, first), committed, and `review_code` then reads the whole diff. Four stories. S1 and S2
build now; S3 and S4 wait for the owner's Kimi sign-in (§6) and hold nothing in S1 or S2 back. Inside every story the
measurement comes first, and no code that depends on a cell is written before that cell is recorded.

**S1 — Vibe measured, then a Vibe reviewer row.** Decisions D1, D2, D3, D4, D5, D6, D7, D8, D8b, D9, D10, D11, D12, D13,
D14, D16. Cells: every Vibe cell — M-V1…M-V13 and M-V3b (§5) — is measured here in one probe run; the code builds on
M-V1…M-V4, M-V3b, M-V5 (where `cached_input_tokens` sits), M-V7…M-V11 and M-V13, and records M-V5's
per-turn-or-cumulative answer, M-V6 and M-V12 for S2. Depends on nothing. Complete on its own: a Vibe row added from the
preset reviews plans and code with the CLI's own sign-in.
- `scripts/probe-vibe-confinement.mjs`, modelled on `scripts/probe-agy-write-block.mjs`: the write arms of RESULTS §2,
  the hook arms of §3 and the cells M-V1…M-V13 with M-V3b (§5), 3 of 3 per side (Windows and WSL), into
  `research/RESULTS_vibe_confinement.md`. Re-run on every Vibe version bump. No bullet below is written before the
  cell it cites is recorded.
- `RateLimit` learns the billing refusal (D14), with the captured Kimi line as the fixture. Generic and independent of
  either runtime, so it lands here in the first story, where no sign-in holds it; the Kimi probe of S3 then meets it
  already fixed.
- `VibeRuntime` (D2, D3, D6, D7, D8, D9; stdin; `VIBE_ACTIVE_MODEL`; the per-attempt export folder and its deletion),
  registered in `Default`, `RuntimeNames`, `Named`, and in `MachineOnlyRuntimes` (D12).
- The trust guard (D4) and the user-hooks refusal (D5) — a refusal before launch, with its cure text — and Vibe's
  `NoMcpServers` arm (`VIBE_MCP_SERVERS=[]`), or the user-MCP-server refusal when M-V3b says the variable does not hold
  (D8b).
- `VendorDiagnosis`: Vibe's install cure per OS and the failure sentences M-V7 captured (not signed in, quota).
- Seeds: `feature-availability.json` (`runtimes`; `effort` `unmeasured` — no flag sets it; `thinking` `unmeasured` —
  the models carry levels in Vibe's own config but no launch flag was shown taking effect; `fastMode` `none`), regenerate.
- Extension: `RUNTIMES`, both `package.json` enums, `modelsFor` + `modelsProvenance` (curated list, D10), the preset
  (D11), `vendorTerminal` maps + `vibeInstall` + `OFFICIAL_SOURCES` (D16), `JUDGED_HERE`, both `CLI_RUNTIMES`,
  `KNOWS_ITS_OWN_ENDPOINT`, a colour slot, the `crossesTo` gate (D13). The model card is the catalog's (new UI only).
- Docs: `module_runners.md`, `module_extension.md`, `architecture.md` runtime list, README, help in five languages,
  `.agents/rules/common/vendor-routing.md` (Mistral models → `vibe`), CHANGELOG entries under `## Unreleased`.

**S2 — Vibe as a consultant and a question-consultant row.** Decisions D1, D4 (the guard on the question row's own
environment), D6 (cwd per capability), D7 (`sessionId`, resume), D10 (the model variable on the question-row path).
Cells: builds on M-V5 (per-turn or cumulative), M-V6 and M-V12, all recorded by S1. Depends on S1. Complete on its own:
a Vibe row answers `consult` and a question-consultant row, and a resumed turn reports its usage right.
- `VibeConsultant` (memory: the vendor remembers, `--resume <sessionId>`; `UsageIsCumulative` from M-V5), added to
  `features.consultant` and `OnACli`.
- `consultant-limitations.json` rows per side.
- Question consultant: `RuntimeCapabilities.Runtimes`, a `ConfinementPlanner` arm for `vibe` (cwd = a coai-owned scratch
  folder for every capability, D6; the checkout reached by absolute path for `disk`; never `--trust`/`--add-dir`; the same
  MCP launch control and trust guard as the reviewer, D8b and D4), and the three `runtime-capabilities.json` rows — `none` /
  `disk` / `web` each from its own measured cells, because denied writes do not prove bounded READS (M-V6). A cell that is
  not measured is written `unmeasured`, and the planner refuses that capability.
- The question-row launch path: `VIBE_ACTIVE_MODEL` in the request's explicit environment, Vibe's home and sign-in found
  under `ProcessEnvironment.Minimal` (M-V12, D10), and the trust guard resolving `VIBE_HOME` from that same environment
  (D4) — the second of the two launch-path tests of §5.
- Docs: `module_server.md` consultant sections, `module_core.md`.

**S3 — Kimi measured, then a Kimi reviewer row.** Waits for the owner's `kimi login` (§6) and blocks nothing in S1 or
S2. Decisions D1, D8b, D9, D10 (`-m <alias>`, the list from M-K7), D11, D12, D13, D15, D16. Cells: every Kimi cell —
M-K1…M-K8 (§5) — is measured here in one probe run, first; the code builds on M-K1, M-K2, M-K3, M-K4, M-K6, M-K7 and
M-K8, and records M-K5 and M-K2's session id for S4. Depends on S1 for the shapes it repeats (a `crossesTo` constant and
arm per runtime, the preset and installer maps, D14 already landed); not on S2. Complete on its own: a Kimi row reviews
plans and code.
- `scripts/probe-kimi-confinement.mjs` → `research/RESULTS_kimi_confinement.md`, cells M-K1…M-K8 (§5), 3 of 3 per side.
  The code half of S3, and S4, are re-planned from its results if any cell contradicts D15. If M-K1 says the piped
  prompt is read as a literal, S3 stops here, because a review prompt is over the Windows command-line limit.
- `KimiRuntime` (`-m <alias>`; the read-only mechanism the probe found — D15's `--agent-file` with a read-only tool list
  plus static deny rules, M-K3 and M-K4; the prompt on stdin, M-K1; answer, usage and a denied tool's shape from
  stream-json, M-K2; cost `null`, D9), registered in `Default`, `RuntimeNames`, `Named`, and in `MachineOnlyRuntimes`
  (D12).
- `VendorDiagnosis`: Kimi's install cure per OS and the failure sentences M-K7 captured (not signed in, quota).
- Seeds: `feature-availability.json` rows for `kimi` (`effort` and `thinking` `unmeasured` until a flag is measured
  taking effect, §8; a `fastMode` row), regenerate.
- Extension: `RUNTIMES`, both `package.json` enums, `modelsFor` + `modelsProvenance` (the M-K7 list, D10), the preset
  (D11), `vendorTerminal` maps + `kimiInstall` + `OFFICIAL_SOURCES` (D16), `JUDGED_HERE`, both `CLI_RUNTIMES`,
  `KNOWS_ITS_OWN_ENDPOINT`, a colour slot, the `crossesTo` gate (D13).
- Docs: the S1 set, for Kimi — `module_runners.md`, `module_extension.md`, `architecture.md`, README, help in five
  languages, `vendor-routing.md`, CHANGELOG.

**S4 — Kimi as a consultant and a question-consultant row.** Waits with S3. Decisions D1, D15 (the resumed-turn write
check). Cells: builds on M-K2 (session id), M-K5 and M-K6, all recorded by S3. Depends on S3; not on S2. Complete on its
own: a Kimi row answers `consult` and a question-consultant row, and a resumed turn is write-checked.
- `KimiConsultant` (`-S <id>`, with the resumed-turn write check of D15 — M-K5, also after the agent file is gone), added
  to `features.consultant` and `OnACli`.
- `consultant-limitations.json` rows per side.
- Question consultant: `RuntimeCapabilities.Runtimes`, a `ConfinementPlanner` arm for `kimi`, and the three
  `runtime-capabilities.json` rows — `none` / `disk` / `web` each from its own measured cells (M-K3, M-K4, M-K6);
  `unmeasured` where no cell says, and the planner refuses that capability.
- Docs: `module_server.md` consultant sections, `module_core.md`.

## 5. Test plan

Measured cells (probe scripts, 3 of 3 per side; every cell a write check where it can write):

- **M-V1** the D2 flag set, write arms (file, shell, `run_typescript`) — 0 written. **M-V2** project hook under the shipped
  flags without `--trust` — no canary; positive control with `--trust` — canary. **M-V3** project MCP server in
  `.vibe/config.toml` — its process does NOT start (a startup canary, not a tool call). **M-V4** a persisted-trusted folder,
  a trusted parent, a junction to a trusted folder — what Vibe loads, and that the D4 guard refuses each. **M-V5** resumed
  export: per-turn or cumulative usage; whether `cached_input_tokens` is inside `input_tokens`. **M-V6** reads outside the
  working folder (`read_file` on a sibling path) — the `disk`/`none` standing. **M-V7** wording of: not signed in (on a
  short `VIBE_HOME` copy), quota spent, an invalid alias; where the sign-in is stored. **M-V8** a 33 KB prompt on stdin.
  **M-V3b** a USER-level MCP server in `~/.vibe/config.toml` (on a short `VIBE_HOME` copy): does its process start under
  the shipped flags, and does `VIBE_MCP_SERVERS=[]` stop it (D8b). **M-V9** a user-level `hooks.toml` hook in an untrusted
  folder — canary (D5). **M-V10** the checkout carries `.vibe/agents/plan.*` (a `plan` agent with every tool),
  `.vibe/tools`, a project skill and an `AGENTS.md` that orders a write — each with a canary or write arm, cwd = the
  checkout, no `--trust` (D6). **M-V11** cwd = an empty coai-owned folder, no `--add-dir`: can `read_file` read a file in
  the checkout by absolute path (D6's second block). **M-V12** a question row's minimal environment: Vibe finds its home and
  sign-in, and the model variable takes effect (D10). **M-V13** `--time-limit 5` on a longer task: exit 3, and the export
  exists (D8).
- **M-K1** `-p -` with piped stdin — read or literal. **M-K2** stream-json: answer, usage, session id, a denied tool's shape.
  **M-K3** an `--agent-file` with a read-only tool list — write arms 0 written. **M-K4** static deny rules alone. **M-K5**
  resumed turn (`-S <id>`) keeps the restricted tools, also after the agent file is gone. **M-K6** project config / hooks /
  `AGENTS.md` in the checkout — what loads headless. **M-K7** signed-in model ids (`kimi-code/kimi-for-coding`?), exit codes,
  quota and not-signed-in wording. **M-K8** `KIMI_CODE_HOME` path-length behaviour.

Unit tests (red first where they guard a defect):

- `VibeRuntimeTests` / `KimiRuntimeTests`: argv never holds `--trust`, `--add-dir`, the prompt or a newline; holds both D2
  flags; env carries the model; `ReadAnswer` takes the last assistant message from a captured array; `ReadUsage` reads a
  captured `export.json`; a missing export is unknown, not zero; `cost` is null; exit 3 with an assistant message is a failure;
  the export folder is gone after the read.
- Trust guard (written from M-V4's recorded format): trusted folder, trusted ancestor, a junction into a trusted folder,
  unreadable file, malformed file, a declared user hook, a declared user MCP server (if D8b needs it) → refusal naming the
  file; empty lists → go; NO file → go; `VIBE_HOME` resolved from the child's own environment on both launch paths.
- `crossesTo`: a `vibe` / `kimi` row is not sent to a server older than its constant; a test that a host whose `RUNTIMES`
  lacks `vibe` does not overwrite a `vibe` row in the server's settings file (D13).
- Each launch path (reviewer, `consult`, question row) carries the model variable in the request's explicit environment,
  goes through the one MCP launch control (D8b), and starts in a fresh coai-owned working folder — never the checkout (D6).
- `RateLimitTests`: the captured billing line → `Hit` true, `Hopeless(Reason)` true, one launch, zero retries.
- `MachineOnlyRuntimes` holds both; the Team server refuses a `vibe` row by name.
- `EveryAdapterRecordsWhatItLaunchedTests` gains both adapters; `ConfinementPlannerTests` gains each arm and refuses an
  `unmeasured` capability.
- Extension: the hard-coded lists above, a preset round-trip through `vendorsFrom`, `crossesTo` holding a row back from an
  older server, `OFFICIAL_SOURCES` admitting only the official installers, the ▶ launch never falling back to codex.
- Whole suites before each PR (`dotnet` test executables and `npm test` in `src_vs_code`), `plan-lifecycle.mjs`, the
  generators' `--check`.
- One live review round per runtime through the product (`review_plan` on a scratch branch) per side, recorded in the
  RESULTS file.

## 6. Owner-only prerequisites

- **Kimi sign-in.** This machine's `kimi` is configured with a Moonshot API key whose account answered `429 … suspended due to
  insufficient balance`. S3 needs `kimi login` (device code) with the kimi.com subscription — on Windows and in WSL.
- **Vibe in WSL.** Only the Windows side is installed and signed in; S1's WSL cells need `vibe` installed and signed in there.
- Releases after each story follow the repository's release process with the owner's go.

## 7. Budget — what grows

- **coai's per-attempt export folders** (~90 KB each, the journal copy): zero retained — deleted right after the read (D7).
  A crash between launch and read leaves one inside its `coai-answers-*` directory, and that directory is removed by the
  sweep that already runs: `PanelService.PruneOldAnswerDirs` (`src_mcp/src/Server/PanelService.cs:1365`), at most once
  every 10 minutes per process, on the round's build path, removing the product's scratch directories in temp older than
  **6 hours** (`PruneOldScratchDirs`, `:1405`; the prefixes in `shared/temp-sweep.json`). No new sweep. Test: an export
  folder left in an old `coai-answers-*` directory is gone after `PruneOldScratchDirs`. If the consultant's launch has no
  such directory today, it gets one under the same prefix rather than a new prefix.
- **Vibe's own session store** (`~/.vibe/logs/session/unified/<id>`, ~100 KB for a two-word turn, estimated ~300 KB for a
  review): owned by Vibe, never deleted by coai — the consultant's resume needs it. At 50 launches a day ≈ 15 MB a day,
  ≈ 5 GB a year; stated in `module_runners.md` and on the model card's help so the person can clear it.
- **Kimi's session store** (`~/.kimi-code/sessions`): the same rule; size measured in S3.
- No new table, cache or long-lived process.

## 8. Not this plan's

- **Chat** with Vibe or Kimi (`cliChatLaunch.ts` `ADAPTERS`, the `chat` seed list).
- **The Team server** (slots, `VendorLogin`, `SlotEnvironment`, `CooldownParser`, the Dockerfile) — D12 keeps both runtimes
  machine-only until a plan for it exists.
- **The bench** (`src_bench/CoaiBench`).
- Vibe's `vibe-acp` / `vibe-app-server` and Kimi's `acp` as transports.
- Effort / thinking controls for either runtime until a flag is measured taking effect.
- Carrying Vibe's `cost_usd` as an estimate (D9).

## 9. Definition of Done

- [ ] `research/RESULTS_vibe_confinement.md` and `research/RESULTS_kimi_confinement.md` hold every M-cell, 3 of 3 per side.
- [ ] A Vibe row and a Kimi row each pass one live plan round and one live code round through the product, per side.
- [ ] Both answer `consult` and a question-consultant row; a resumed turn is write-checked.
- [ ] No launch of either CLI carries `--trust`, `--add-dir` or the prompt in argv, and no Vibe launch starts in the checkout —
      pinned by tests on every launch path.
- [ ] The trust guard refuses a trusted folder or ancestor; a declared user hook in `~/.vibe/hooks.toml` (and a user MCP server, if D8b needs it) refuses the launch.
- [ ] A billing refusal is not retried.
- [ ] Both runtimes are machine-only; an older server never receives the rows.
- [ ] Seeds regenerated; every hard-coded runtime list in tests updated; both suites green.
- [ ] Docs, help in five languages, `vendor-routing.md`, READMEs and CHANGELOGs updated.
- [ ] This plan promoted to `research/` with its deviations.
