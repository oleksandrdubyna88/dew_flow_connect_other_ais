# PLAN — Mistral Vibe and Kimi Code as coai runtimes, on the person's own subscription

> Status: **plan only, nothing implemented yet, 2026-10-12** — re-planned that day after M-V11 (D6: a sanitised snapshot
> of the tree replaces the empty working folder). Scope: `src_mcp` (two new reviewer runtimes and two
> consultants), `shared/*.json` seeds and their generated TypeScript, `src_vs_code` (runtime lists, presets, install,
> the catalog model card), docs. Chat, the Team server and the bench are out (§8). Kimi runs over ACP behind a coai shim (D15,
> re-planned 2026-10-12 after the owner signed in). Every probe number below is n=1 on Windows; nothing is claimed for WSL
> until the probe scripts have run there.
>
> Related docs: [RESULTS_vibe_and_kimi_first_probe.md](../research/RESULTS_vibe_and_kimi_first_probe.md),
> [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md),
> [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md),
> [module_runners.md](../research/module_runners.md), [module_extension.md](../research/module_extension.md),
> [RESULTS_agy_write_block.md](../research/RESULTS_agy_write_block.md),
> [PLAN_the_consultant_has_its_own_vendors.md](../research/PLAN_the_consultant_has_its_own_vendors.md).

## 1. Goal

The owner bought a Mistral subscription and signed in to the `vibe` CLI (2.26.1), and wants Kimi Code (`kimi`, 0.41.0 at the first probe,
2.1.1 once the owner signed in on 2026-10-12; kimi.com) as well. coai can reach Mistral today only as an API-key endpoint: a vendor row on the `codex` runtime with
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
- **D4 — superseded by D17 (2026-10-12).** It was a pre-launch read of the PERSON's `trusted_folders.toml`. The probe
  showed that list does load project hooks for the folder, a parent and a junction (M-V4, exact-case match), and that the
  sign-in is not in `VIBE_HOME` (M-V7) — so coai gives Vibe its OWN home, whose trust list coai creates empty and checks
  before every launch (D17). The person's list is never read.
- **D5 — superseded by D17 (2026-10-12).** It refused a launch while the person's own `~/.vibe/hooks.toml` declared a hook
  (which fires in an untrusted folder, M-V9 3 of 3). With D17 the person's home is never Vibe's home, so their hooks are
  not loaded at all; a `hooks.toml` appearing in coai's OWN home refuses the launch (D17's validation).
- **D6 — Vibe's working folder is ALWAYS a coai-owned SANITISED SNAPSHOT of the tree, never the checkout** (Vibe's home is
  D17's). *Re-planned 2026-10-12.* The branch under review controls everything in the checkout, including
  `.vibe/hooks.toml`, `.vibe/agents` (which could redefine the built-in `plan` agent), `.vibe/tools`, skills and
  `AGENTS.md`. Vibe documents all of them as loading only in a trusted folder, and D3 never trusts one — but that is Vibe's
  handling again, and D4's guard is a check-then-launch that a folder trusted between the check and the child's start would
  lose (code gate, codex, 2026-10-11). The first answer — an empty coai-owned folder, the checkout read by absolute path —
  is impossible: **M-V11 measured 3 of 3 `tool_denied`** for a `read_file` outside the working folder
  ([RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §1), so Vibe's reads are bounded to its cwd.
  So the cwd is a fresh coai-owned COPY of the tree with every project-configuration path left out; **M-V14 measured 3 of
  3**: with that copy deliberately trusted, `read_file` inside it answered, and hook canaries in the source tree and in the
  copy's parent did not fire, while the positive control (the same hook inside the trusted copy) did (§2 of the same file).
  Whatever Vibe does or whoever trusts what, the files are not there: the second independent block beside D2, and no race
  for D4 to lose. Settled with the consultant (consultation `826d66e2`, codex `gpt-6-astra`); the snapshot's contracts:
  - **Source.** A code round copies its pinned worktree (`RoundEngine.cs:349` leases it; `WorktreeManager.cs` adds it,
    `SubmodulePopulator.cs` fills its submodules) — NOT `git archive`, whose `export-ignore` / `export-subst` attributes
    would drop or rewrite reviewed bytes. A plan round needs no snapshot (its cwd is already an empty `ScratchDirectory`).
    A `consult` turn copies the LIVE tree: tracked plus untracked-not-ignored paths, their current bytes, deleted paths
    absent (a rename is the old path absent and the new present); a file that changes while copying makes the copy retry
    once, then refuse.
  - **Exclusions, at every depth**, matched with the destination filesystem's case rules: `.git`, `.vibe/`, `.agents/skills/`,
    `AGENTS.md` (and the Kimi equivalents S4's probe names). An excluded path is named in the manifest, never silently lost.
  - **Links.** A symlink, junction or other reparse point is REFUSED (the snapshot is not made, the launch is refused with
    the path named), never dereferenced; a path that would escape the snapshot root, and two names that collide under the
    destination's case rules, refuse before anything is published.
  - **Submodules and LFS.** A populated submodule is copied at its recorded revision; an unpopulated one, or an LFS pointer
    file, is listed as unavailable in the prompt, never presented as reviewed source.
  - **Limits.** A ceiling on copied bytes, file count and elapsed time (config keys, defaults from S1's measurement of
    this repository: 2 737 files, a 38.8 MB tar, 5.7 s); over any of them the launch is refused with the number — never a
    partial copy. A Windows long-path failure is a refusal too. **And an aggregate cap** (plan gate, codex, 2026-10-12):
    the process keeps a count of the bytes held by its LIVE snapshots, and a new snapshot is refused — with both numbers —
    when that sum plus the new one would pass a configured ceiling (default 2 GB) or when the temp volume's free space would
    fall under a configured floor (default 5 GB). A refusal is the row's failure with a sentence, like any other; the round
    goes on with its other reviewers.
  - **Sharing.** One snapshot per round per pinned SHA, shared by every reviewer of a runtime that asks for one in that
    round; one per `consult` CONVERSATION, at one stable path, refreshed in place before each turn (the live tree moves; see
    the end of D6). A round's lives inside the round's `coai-answers-*` scratch directory and goes with it (§7).
  - **Citations.** A manifest of snapshot-relative paths; a finding's path that is absolute and inside the snapshot is
    mapped back to the repository-relative path only when the manifest holds it. A resumed consultation keeps its
    snapshot's path, so its history's absolute paths stay inside it; a path the refreshed manifest no longer holds is not
    mapped.
  - **One helper, several callers.** `ReviewSnapshot` is one shared unit called at the orchestration seams — the round's
    launch directory (`RosterBuilder.cs:370-375` passes it to `IReviewerRuntime.Build`), the consultant's launch
    (`ConsultationService.cs:648`) and the question row (`QuestionRowLaunch.cs`, `ConsultantLaunches.Cwd`
    `ConsultantLaunches.cs:53`). Which runtimes need it is a capability the ADAPTER declares (a default-false member on
    `IReviewerRuntime` / `IConsultantRuntime`), never an `if` on a vendor name. Created only for a launch granted the
    checkout; a repair and a prompt-only review keep their empty folder.
  The snapshot is NOT a write sandbox — if D2 failed, copied files would not stop a write elsewhere; it closes the path by
  which the reviewed branch supplies configuration. The person's own configuration is closed by D17. **A conversation
  keeps ONE snapshot path**: a resumed Vibe session stays bound to the folder it was created in — a read in a new
  `--workdir` was `tool_denied`, and the model re-asked turn 1's absolute path (RESULTS_vibe_confinement §22, B and E) —
  while the same path re-filled in place each turn was read afresh 3 of 3 (§22, D). So a `consult` conversation's
  snapshot is created once at a stable path and REFRESHED IN PLACE before each turn; a code round's snapshot is per round.
  **The refresh is transactional** (plan gate, codex, 2026-10-12): the replacement tree is built COMPLETE beside the
  snapshot, under every rule above (exclusions, links, ceilings, the one retry on a mid-copy change), and only then swapped
  into the stable path; any refusal leaves the previous snapshot and its manifest exactly as they were, so no turn ever
  reads a tree that never existed in the live repository. A swap that fails half-way puts the old tree back. **The whole
  inventory is checked, not only each file** (plan gate, codex, 2026-10-12: a file copied early can stay unchanged while a
  later one changes before its own copy, so per-file stamps alone can publish a mix of times): the live listing and every
  copied file's stamp are taken again AFTER the last file is copied; any path added, removed or changed anywhere in the
  inventory since the copy began retries the whole copy once, then refuses. This is detection by length and write time —
  **best effort**: a writer that keeps both unchanged is not seen, and a live tree is not frozen while it is copied (risk
  consultation `f1ec5d9a`). A hard link in a locally prepared live tree imports the linked file's bytes (git records no
  hard links), stated as a known limit. **Reads are contained by the opened handle** (risk consultation `f1ec5d9a`): a
  served file is opened first, its handle's final path checked inside the root, and read from that handle under a cap —
  never checked and then re-opened by path; the root itself is part of the link walk; only a file's unnamed stream is
  copied (Windows alternate data streams are not). Tests: a refused refresh (ceiling, link, a tree
  that keeps changing) leaves the old snapshot readable with its manifest unchanged; a file changed after its own copy but
  before the copy ends, and a file added during the copy, each make the copy retry, then refuse.
- **D7 — the prompt on stdin, the answer from `--output json`, usage from `--output-dir`.** Answer = the last
  `role: assistant` message; session id = `sessionId` on any element (no `ReadHandle` interface change needed). The export
  folder is a per-ATTEMPT folder (a retry never reads a stale export) **inside the launch's own `coai-answers-*` scratch
  directory**, and is **deleted right after `ReadUsage`/`ReadHandle` read it**. Not beside the consultant's answer files:
  `ConsultantArtefactSweep` enumerates files only (`src_mcp/src/Server/Consultation/ConsultantArtefactSweep.cs:22`) and
  would never remove the tree. A crash between launch and read is covered by the sweep that already exists for those
  directories (§7). `--output streaming` only if a measured event carries something the array does not. Stdin carried a
  33 KB prompt 3 of 3 (M-V8).
- **D8 — exit 3 (a limit) is a failure** even when an assistant message exists. `--time-limit` is set a few seconds under
  the row's timeout, on the documented promise that Vibe writes its export on every exit ("interrupt the run …, write the
  export and exit 3") — measured: exit 3 and an export 3 of 3 (M-V13), `outcome: deadline`, `stop_reason` null, usage null in
  2 of 3 — so coai reads `outcome`, and null usage is `Usage.Unknown`.
- **D8b — no MCP server starts** (the #514 rule; `NoMcpServers.cs:8`; Claude and Codex have launch switches at
  `ClaudeRuntime.cs:97` and `CodexConsultant.cs:102`; the CLIs with none are warned about at `src_mcp/src/Program.cs:2178-2187`).
  Measured: `VIBE_MCP_SERVERS=[]` does NOT stop a user server (M-V3b, 3 of 3 — Vibe merges `mcp_servers` by name across
  layers), and an MCP server supplied ONLY through the environment variable starts under an empty home (RESULTS §22 A,
  3 of 3). So Vibe's `NoMcpServers` arm is D17: coai's own home declares none, and the child's environment is built, not
  inherited, so no `VIBE_*` reaches it. ONE launch control every Vibe launch path goes through — the reviewer, `consult` and
  the question row — never a flag one path adds and another forgets (code gate, codex, 2026-10-11); a test per path. Kimi:
  the same question in M-K10 (the shim already sends `mcpServers: []`), answered under the same one-control rule.
- **D9 — cost is recorded as `null`, tokens as reported.** `cost_usd` is Vibe's list price even on a subscription, and
  `lineCost` treats any numeric `costUsd` as a bill (`estimated: false`, `src_vs_code/src/usage.ts:411`). A missing export
  is `Usage.Unknown`, never zero. Carrying Vibe's estimate as an estimate needs provenance through the ledger — not this plan.
- **D10 — models: a curated, version-labelled alias list, typed aliases allowed, the saved choice kept.** Vibe takes the
  alias through `VIBE_ACTIVE_MODEL` (no `--model` flag), set in the launch request's EXPLICIT environment so it reaches a
  question row too, whose child gets only `ProcessEnvironment.Minimal` (`ConsultantLaunches.ForQuestion`, pinned by
  `ChildEnvironmentTests.cs:33-39`) — M-V12 checks that Vibe finds its home and sign-in under that environment, and a test
  covers each of the two launch paths. First list `mistral-medium-3.5`, `glm-5-3`. **Checked twice** (M-V7 b: an invalid
  alias exits 0 and silently routes to the default): before the launch against the curated list (a typed alias outside it
  is refused with the list), and after it against the export's `active_model` — absent or different is a failure that
  keeps the reported usage. A match proves Vibe accepted the alias, not which backend answered. Kimi takes its model through the ACP
  `model` config option (D15), never the config default; its list comes from the Kimi measurement (M-K7). Discovery only once a CLI command lists the
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
- **D14 — a billing refusal is a SPENT allowance, named as one.** Today the captured line matches nothing in `RateLimit`
  (its `429` has no status label and no "too many"), so it reaches the person as a bare non-zero exit — not retried, but
  not named either (an earlier draft said it would be retried; the test showed otherwise). `insufficient balance` goes into BOTH
  `RateLimit.Phrases` (`ReviewerExecutor.cs:251`) and `RateLimit.Spent` (`:304`) — `Reason` filters through `Marked`
  first (`:341`), so `Spent` alone would lose the line. Generic, small, independent of either vendor's runtime.
- **D15 — Kimi runs over ACP behind a coai shim, never in prompt mode.** *Re-planned 2026-10-12, after the owner signed in
  (Kimi Code 2.1.1, `managed:kimi-code` OAuth); evidence [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md).*
  Prompt mode is unusable: `-p` takes the prompt only as an argument (`-p -` is a literal dash; no `-p` and `-p ""` are
  refused, §1), a review prompt is over Windows' command line, and prompt mode runs `auto` with no approval. `kimi acp` (the
  Agent Client Protocol on stdio) carries the prompt on stdin and makes coai the CLIENT, which gives coai three blocks it
  owns, not Kimi:
  - **Writes** — every write-kind tool (`Write`/`edit`, `Bash`/`execute`, `AskUserQuestion`) sent
    `session/request_permission` first; answered `reject_once`, nothing was written, 3 of 3 (§2.1). The shim refuses EVERY
    permission request — it never inspects which tool asked.
  - **`Read` only** — with `fs.readTextFile: true` offered, Kimi sent every `Read` through `fs/read_text_file`, and the
    shim served only paths inside the working folder; an outside file was refused and not quoted (§2.2). Without the offer,
    `Read` took an outside file and quoted it — so the offer is mandatory, and an excluded name is refused like an outside
    path. **But `Grep` and `Glob` read the disk directly** (auto-allowed ripgrep, no permission request, no client call): an
    outside canary's marker came back (§2.3, the consultant's hypothesis, consultation `f48a09c9`), and no documented key
    switches a tool off. So Kimi's READS are not bounded — the standing codex already has (it reads anywhere, and coai has
    never called it confined). The reviewer row accepts that, as for codex; the question consultant's `disk` standing for
    `kimi` is `unconfined`; and the plan says "tested write attempts were denied", never "cannot read". ACP `mode: plan` is no
    way out: the turn reads its own plan file from Kimi's session store, outside any snapshot, and ends empty when refused
    (§2.4).
  - **Project instructions** — the same channel carried Kimi's own search for `AGENTS.md`, `agents.md` and
    `.kimi-code/AGENTS.md`; the shim refuses those names at every depth.
  Writes stay the hard requirement, and they are version-specific evidence, not a sandbox: Kimi decides which operations
  ask, so S4 measures every announced tool — including `Agent` sub-agents — and Kimi's own session store under
  `~/.kimi-code/sessions` is a permitted vendor write, named as such. The working folder is the same sanitised snapshot as
  Vibe's (D6, with `.kimi-code/` and both spellings of `AGENTS.md`
  added to the exclusions), so whatever Kimi loads from DISK without the client — measured in M-K9 — is not the branch's.
  The model is set every session with `session/set_config_option {configId: "model"}`: a session came up on a model the plan
  does not include and failed `401 Your current subscription does not have access to kimi-for-coding-highspeed` (§2) — that
  sentence is a diagnosis row. `mode` stays `default` (manual approvals — the shim's refusals ARE the approvals).
  **The shim is `coai-mcp --ask-acp`**, the established self-invocation pattern of `--ask-api` / `--ask-local` /
  `--ask-remote` (`src_mcp/src/Program.cs:304-307`, `AskApiMode.cs`, `LocalRuntime.SelfInvocation` `LocalRuntime.cs:64`):
  `KimiRuntime.Build` returns a `ProcessRequest` for the shim, the prompt on its stdin, so `ReviewerExecutor` launches,
  times out and kills it like any CLI; the shim runs `kimi acp` as ITS child and prints the answer and usage on stdout. A new
  one-shot mode is written into the list in `.agents/PROJECT.md` and never exits 64. The ACP client half is one unit with an
  `IAcpProfile` per vendor (Kimi: the command, the model option id, the exclusion names) — one module per vendor; nothing
  else speaks ACP yet. Usage: `usage_update {used, size}` is CONTEXT tokens with no input/output split — recorded as
  unknown input/output until M-K2 shows a split (the `/usage` command is the candidate), cost `null` (D9).
- **D17 — every Vibe launch gets a coai-owned home and a built environment** (2026-10-12; consultation `e17eb7cd`,
  codex `gpt-6-astra`, every claim checked). On Windows the sign-in lives in the Credential Manager (`ai.mistral.vibe`), not
  in `VIBE_HOME`: empty and copied homes were signed in 24 of 24 (M-V7). So:
  - **The home.** `VIBE_HOME` is a short coai-owned folder under `LocalApplicationData/coai-vibe/` (short: a deep home broke on
    a >260-character path, first probe §4) — one per `consult` conversation, kept across its resumed turns (the session store
    `--resume` needs lives in it), and one per code round for reviewers, disposed with the round. Before every launch it is
    VALIDATED against the files Vibe itself writes (RESULTS §22's inventory: `connector_bootstrap_cache.json` + lock,
    `experiment_eval_cache.json`, `trusted_folders.toml` with both lists empty, `vibehistory`, `whoami_cache.json`,
    `logs/`); a `config.toml` declaring hooks, MCP servers or agents, a `hooks.toml`, an `agents/` or `tools/` folder, a
    non-empty trust list, or a link anywhere in it refuses the launch with the path named. Turns sharing a home are serialised.
  - **The environment.** Built, never inherited: `ProcessEnvironment.Minimal` for the platform, plus exactly
    `VIBE_HOME`, `VIBE_CLI=python` and `VIBE_ACTIVE_MODEL`. No other `VIBE_*` reaches the child (RESULTS §22 A).
  - **The CLI.** `vibe` is two CLIs, and a GrowthBook flag cached in the home flipped this account from the Python CLI to a
    Rust CLI between two launches; the Rust CLI refuses `--time-limit` and `--output-dir` (exit 2) and is unmeasured
    (RESULTS §3). `VIBE_CLI=python` pins the measured one. If a later Vibe drops the Python CLI, the launch fails with
    exit 2 and `VendorDiagnosis` names the cause — coai never falls through to the Rust CLI unmeasured.
  This replaces D4, D5 and D8b's fallback with one block coai owns. It closes the person's hooks, MCP servers, agents
  (a user `~/.vibe/agents/plan.toml` would override the built-in `plan` agent everywhere — Vibe's code, not measured) and
  trust list. It does NOT stop Vibe's own built-in skills, which it unpacks into the home (four, RESULTS §22); with
  `--enabled-tools read_file` no tool they could call is enabled. **The explicit tool restriction stays authoritative under
  Vibe's remote experiment variants** — smart approval (available and default), registry skills and managed shell tools all
  forced on, every write `tool_denied`, fresh and resumed, 3 of 3 (M-V15, RESULTS §23). The home's validation allows Vibe's
  bundled plugin artifacts only through a **version-labelled hash manifest** — never "any file under `logs/session/plugins/`".
  §22 recorded names and sizes, NOT hashes (cadence consultation `4e79f70e`), so S1 adds a reproducible step that starts
  `vibe` once on an empty home and writes `shared/vibe-home-manifest.json` (Vibe version → path → SHA-256). A Vibe version
  with no manifest is REFUSED with a sentence naming the version and the step that qualifies it; requalification is the
  cost of every Vibe update that changes those bytes. Tests: an upgraded CLI on a fresh home and on a resumed home — a
  manifest present admits both, an absent one refuses both.
- **D18 — Vibe and Kimi on Linux and WSL are refused until measured.** Every number above is Windows. On Linux Vibe's keyring may
  fall back to a file inside `VIBE_HOME`, which would break D17, and nothing was measured there. A launch whose CHILD runs on
  Linux (including WSL launched from Windows) is refused with a sentence naming this plan, for both runtimes (Kimi's WSL
  side is just as unmeasured); the probe scripts' WSL runs lift it in a later change.
- **D16 — official installers only.** Vibe: `uv tool install mistral-vibe` (what is installed here) or Mistral's own
  script; Kimi: Moonshot's own installer. Both go into `OFFICIAL_SOURCES` (`vendorTerminal.ts:346`), which a test pins.
  Neither is on npm, so the update light needs a version source other than npm (`cliVersions.ts:25` has npm and manifest
  only) — or no light, which `versionSourceFor` already allows.

## 3. What exists today (verified on `main` 722fc597, 2026-10-11; `usage.ts` re-checked on bb95c1c4, 2026-10-12)

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

The coai gate runs once for the whole plan — one plan round on this document and one code round over the whole
committed diff of every story below, with `again: true` for a checkpoint when a later story lands. No story is gated on
its own: each is built on one implementation branch, committed, and `review_code` then reads the whole diff. Five
stories (re-split 2026-10-12, when M-V11 turned the empty working folder into a snapshot helper big enough to stand on
its own). All five build now — the owner signed in to Kimi on 2026-10-12; S4 and S5 hold nothing in S1–S3 back.
Inside every story the measurement comes first, and no code that depends on a cell is written before that cell is
recorded.

**S1 — Vibe measured, and the sanitised snapshot every snapshot-needing launch uses.** Decisions D6, D14. Cells: every
Vibe cell — M-V1…M-V14 and M-V3b (§5) — is measured here in one probe run (M-V11 and M-V14 already by hand, 3 of 3 on
Windows, 2026-10-12). Depends on nothing. Complete on its own: the probe records what S2 and S3 build on, the snapshot is
a tested unit no runtime uses yet, and a billing refusal is no longer retried.
- `scripts/probe-vibe-confinement.mjs`, modelled on `scripts/probe-agy-write-block.mjs`: the write arms of the first
  probe's §2, the hook arms of its §3 and the cells M-V1…M-V14 with M-V3b (§5), 3 of 3 on WINDOWS, into
  `research/RESULTS_vibe_confinement.md`. The WSL run belongs to the later change that lifts D18 (plan gate, codex,
  2026-10-12: S1 must not wait on a side D18 refuses). Re-run on every Vibe version bump. No bullet of S2 or S3 is written before the
  cell it cites is recorded.
- `ReviewSnapshot` (D6): the copy, the exclusions, the link and collision refusals, submodules and LFS, the limits, the
  manifest and the path mapping, as one unit with its own tests (§5), plus the adapter capability member that asks for
  it (default false; nothing sets it true until S2).
- The Vibe home manifest step (D17): start `vibe --version` and one tiny turn on an empty owned home, hash what Vibe wrote,
  write `shared/vibe-home-manifest.json` for that version; re-run on every Vibe update.
- `RateLimit` learns the billing refusal (D14), with the captured Kimi line as the fixture. Generic and independent of
  either runtime, so it lands in the first story, where no sign-in holds it; the Kimi probe of S4 then meets it already
  fixed.

**S2 — the Vibe reviewer row.** Decisions D1, D2, D3, D6 (the snapshot wired at the round's launch directory),
D7, D8, D8b, D9, D10, D11, D12, D13, D16, D17 (per-round home, built environment), D18. Cells: builds on M-V1…M-V5, M-V3b, M-V7…M-V11, M-V13 and M-V14, all recorded by
S1. Depends on S1. Complete on its own: a Vibe row added from the preset reviews plans and code with the CLI's own
sign-in.
- The round's launch directory (`RosterBuilder.cs:370-375`) takes the round's shared snapshot for an adapter that asks
  for one; the reviewer's cited paths go through the snapshot's mapping.
- `VibeRuntime` (D2, D3, D6, D7, D8, D9; stdin; `VIBE_ACTIVE_MODEL`; the per-attempt export folder and its deletion),
  registered in `Default`, `RuntimeNames`, `Named`, and in `MachineOnlyRuntimes` (D12).
- The owned home and the built environment (D17): creation, validation before every launch (a refusal with the path and its
  cure), serialised turns, disposal with the round; the Linux/WSL refusal (D18); the model checked before and after
  (D10).
- `VendorDiagnosis`: Vibe's install cure per OS and the failure sentences M-V7 captured (not signed in, quota).
- Seeds: `feature-availability.json` (`runtimes`; `effort` `unmeasured` — no flag sets it; `thinking` `unmeasured` —
  the models carry levels in Vibe's own config but no launch flag was shown taking effect; `fastMode` `none`), regenerate.
- Extension: `RUNTIMES`, both `package.json` enums, `modelsFor` + `modelsProvenance` (curated list, D10), the preset
  (D11), `vendorTerminal` maps + `vibeInstall` + `OFFICIAL_SOURCES` (D16), `JUDGED_HERE`, both `CLI_RUNTIMES`,
  `KNOWS_ITS_OWN_ENDPOINT`, a colour slot, the `crossesTo` gate (D13). The model card is the catalog's (new UI only).
- Docs: `module_runners.md`, `module_extension.md`, `architecture.md` runtime list, README, help in five languages,
  `.agents/rules/common/vendor-routing.md` (Mistral models → `vibe`), CHANGELOG entries under `## Unreleased`.

**S3 — Vibe as a consultant and a question-consultant row.** Decisions D1, D6 (one stable snapshot path per conversation,
refreshed in place; cwd per capability), D7 (`sessionId`, resume), D10, D17 (one home per conversation, kept across its turns;
the built environment on the question-row path), D18.
Cells: builds on M-V5 (per-turn or cumulative), M-V6 and M-V12, all recorded by S1. Depends on S1 AND S2 — it uses S2's
reviewer adapter (a consultant reads answer and usage through it, `IConsultantRuntime.cs:107`) and S2's owned-home and
environment controls (cadence consultation `4e79f70e`). Complete on its own: a Vibe row answers `consult` and a
question-consultant row, and a resumed turn reports its usage right.
- `VibeConsultant` (cwd = the conversation's ONE snapshot path, refreshed in place from the live tree before each turn,
  D6 — a new path per turn is outside the resumed session's boundary; memory: the vendor remembers, `--resume <sessionId>`; `UsageIsCumulative` from M-V5), added to
  `features.consultant` and `OnACli`.
- `consultant-limitations.json` rows per side.
- Question consultant: `RuntimeCapabilities.Runtimes`, a `ConfinementPlanner` arm for `vibe` (cwd = a sanitised snapshot of
  the live tree for `disk`, D6 — Vibe cannot read outside its cwd, M-V11 — and an empty scratch folder for `none`; never
  `--trust`/`--add-dir`; the same
  owned home and built environment as the reviewer, D17), and the three `runtime-capabilities.json` rows — `none` /
  `disk` / `web` each from its own measured cells, because denied writes do not prove bounded READS (M-V6). A cell that is
  not measured is written `unmeasured`, and the planner refuses that capability.
- The question-row launch path: the built environment of D17 (`ProcessEnvironment.Minimal` + the three `VIBE_*`; `ForQuestion`
  keeps explicit values, `ConsultantLaunches.cs:65`), signed in under it (M-V12 3 of 3) — the second of the launch-path tests
  of §5. **A question row is one-shot**, so its home and snapshot belong to the ROW, not to a conversation: both live inside
  the row's own `coai-question-*` scratch folder (`QuestionRowLaunch.cs:68`, removed in `finally`), gone on success,
  refusal and cancellation alike; persistent ownership and refresh-in-place are `consult`'s only.
- **The row's disk grant, not "the live tree"** (cadence consultation `4e79f70e`): a `disk` row reads the operator's GRANTED
  ROOTS (`QuestionFanOut.cs:203`, `Grant.Roots` — several roots, a sub-folder of a repository, or a non-Git folder). Each
  granted root is copied by the snapshot's live mode, limited to that root, into its own sub-folder of the row's snapshot;
  the prompt names those SNAPSHOT paths, never the original roots (Vibe cannot read outside its cwd, M-V11). A root that is
  not inside a Git work tree is refused for a Vibe row with a sentence (the snapshot's live mode lists through Git) until
  a plain-folder mode exists. Tests: a sub-folder-only grant copies only that sub-folder; two roots both arrive; a non-Git
  root refuses; the prompt names only snapshot paths; the row's home and snapshot are gone after success, refusal and
  cancellation.
- Docs: `module_server.md` consultant sections, `module_core.md`.

**S4 — Kimi measured, then a Kimi reviewer row over ACP.** The owner signed in on 2026-10-12, so S4 is no longer blocked.
Decisions D1, D6 (the snapshot with Kimi's exclusions), D8b, D9, D10 (the ACP model option, the list from M-K7), D11, D12,
D13, D15, D16. Cells: every Kimi cell — M-K1…M-K10 (§5) — in one probe run, first (M-K1 and the ACP arms of M-K3/M-K6 already
by hand, 2026-10-12). Depends on S1 (the snapshot; D14) and S2 for the shapes it repeats (a `crossesTo` constant and arm per
runtime, the preset and installer maps); not on S3. Complete on its own: a Kimi row reviews plans and code.
- `scripts/probe-kimi-confinement.mjs` → `research/RESULTS_kimi_confinement.md`, cells M-K1…M-K10 (§5), 3 of 3 on Windows
  (Kimi on a Linux/WSL child is refused like Vibe until the same later change measures it),
  driving `kimi acp` through the SAME client unit the shim uses (so the probe measures the shipped code, not a copy). The
  code half of S4, and S5, are re-planned from its results if any cell contradicts D15.
- The ACP client unit and `IAcpProfile` (`KimiAcpProfile`): `initialize` with `fs.readTextFile: true`, `writeTextFile: false`,
  `terminal: false`; `session/new` with `mcpServers: []`; `set_config_option` for the model; every permission request
  refused; `fs/read_text_file` served only inside the working folder and never for an excluded name; every other client
  request answered with an error; a deadline of its own under the row's timeout.
- `coai-mcp --ask-acp` (D15): stdin prompt in, answer and usage out, exit codes of its own (never 64), named in
  `.agents/PROJECT.md`'s one-shot list. **The shim is infrastructure until M-K4, M-K9 and M-K10 pass THROUGH it** (risk
  consultation `ba5f661a`): ACP makes `session/request_permission` optional for the agent, so refusing every request does
  not prove every write asks — a sub-agent, a hook or a user MCP server must be measured with canaries and positive
  controls before `KimiRuntime` ships.
- `KimiRuntime` classifies the shim's outcome from its STRUCTURED report (`end`, `failure`, the exit code), never by
  scanning its stdout text through `RateLimit` — a read trace naming `quota.cs` would otherwise turn a sign-in refusal
  (77) into a retryable rate limit (risk consultation `ba5f661a`); and the report's `notCaptured` usage survives a failure
  (the executor's rate-limit branch drops usage today). Tests through the executor AND the scheduler: a spent balance, a
  clean sign-in refusal, and a sign-in refusal whose trace carries quota text — each launched once, usage kept unknown.
- `KimiRuntime` (the shim's `ProcessRequest`, the snapshot capability, answer and usage from the shim, cost `null`, D9),
  registered in `Default`, `RuntimeNames`, `Named`, and in `MachineOnlyRuntimes` (D12).
- Kimi session retirement (§7): the ownership record written by the shim, the round's and conversation's recorded folders
  deleted at their end, the 6-hour sweep over recorded folders only. Tests: an unrecorded folder named `wd_snapshot-x_y` is
  left alone; a recorded one is deleted at the end; a crash leftover goes after 6 hours; a shim killed between the pending
  line and `session/new`'s answer leaves a session the sweep still finds and retires.
- `VendorDiagnosis`: Kimi's install cure per OS (Moonshot's own installer, D16) and the failure sentences M-K7 captured —
  including the subscription-model `401` of D15 and the billing `429` of D14.
- Seeds: `feature-availability.json` rows for `kimi` (`effort` — Kimi's config lists `support_efforts` low/high/max per model,
  but no ACP option sets it in 2.1.1, so `unmeasured`; `thinking` `none` — the only ACP value is `on`; `fastMode` `none`),
  regenerate.
- Extension: `RUNTIMES`, both `package.json` enums, `modelsFor` + `modelsProvenance` (the M-K7 list, D10), the preset
  (D11), `vendorTerminal` maps + `kimiInstall` + `OFFICIAL_SOURCES` (D16), `JUDGED_HERE`, both `CLI_RUNTIMES`,
  `KNOWS_ITS_OWN_ENDPOINT`, a colour slot, the `crossesTo` gate (D13). The ▶ terminal runs plain `kimi` (interactive), never
  `acp`.
- Docs: the S2 set, for Kimi — `module_runners.md` (the ACP shim), `module_extension.md`, `architecture.md`, README, help in
  five languages, `vendor-routing.md`, CHANGELOG, and `.agents/PROJECT.md`.

**S5 — Kimi as a consultant and a question-consultant row.** Decisions D1, D6, D15 (the resumed-turn checks). Cells:
builds on M-K2 (session id), M-K5 and M-K9, all recorded by S4. Depends on S4; not on S3. Complete on its own: a Kimi row
answers `consult` and a question-consultant row, and a resumed turn keeps every refusal.
- `KimiConsultant` over the same shim: the conversation's ONE snapshot path refreshed in place, as for Vibe (D6), unless
  M-K5 shows a resumed ACP session follows a new `cwd`; memory per M-K5 (`session/load` /
  `resume` with the same refusals, else coai carries the transcript); added to `features.consultant` and `OnACli`.
- `consultant-limitations.json` rows per side.
- Question consultant: `RuntimeCapabilities.Runtimes`, a `ConfinementPlanner` arm for `kimi` (snapshot for `disk`, empty
  folder for `none`; `Read` bounded by the shim, `Grep`/`Glob` not — so `disk` is written `unconfined`, D15), and the three `runtime-capabilities.json` rows — `none` / `disk` / `web`
  each from its own measured cells; `unmeasured` where no cell says, and the planner refuses that capability.
- Docs: `module_server.md` consultant sections, `module_core.md`.

## 5. Test plan

Measured cells (probe scripts, 3 of 3 on Windows — WSL is the later change that lifts D18; every cell a write check where it
can write):

- **M-V1** the D2 flag set, write arms (file, shell, `run_typescript`) — 0 written. **M-V2** project hook under the shipped
  flags without `--trust` — no canary; positive control with `--trust` — canary. **M-V3** project MCP server in
  `.vibe/config.toml` — its process does NOT start (a startup canary, not a tool call). **M-V4** a persisted-trusted folder,
  a trusted parent, a junction to a trusted folder — what Vibe loads (measured: each loads project hooks, §8 of the RESULTS;
  D4, which would have refused them, is superseded by D17). **M-V5** resumed
  export: per-turn or cumulative usage; whether `cached_input_tokens` is inside `input_tokens`. **M-V6** reads outside the
  working folder (`read_file` on a sibling path) — the `disk`/`none` standing. **M-V7** wording of: not signed in (on a
  short `VIBE_HOME` copy), quota spent, an invalid alias; where the sign-in is stored. **M-V8** a 33 KB prompt on stdin.
  **M-V3b** a USER-level MCP server in `~/.vibe/config.toml` (on a short `VIBE_HOME` copy): does its process start under
  the shipped flags, and does `VIBE_MCP_SERVERS=[]` stop it (D8b). **M-V9** a user-level `hooks.toml` hook in an untrusted
  folder — canary (D5). **M-V10** the checkout carries `.vibe/agents/plan.*` (a `plan` agent with every tool),
  `.vibe/tools`, a project skill and an `AGENTS.md` that orders a write — each with a canary or write arm, cwd = the
  checkout, no `--trust` (D6). **M-V11** cwd = an empty coai-owned folder, no `--add-dir`: can `read_file` read a file in
  the checkout by absolute path — **measured 3 of 3 `tool_denied`, 2026-10-12**: reads are bounded to the cwd (D6 re-planned). **M-V12** a question row's minimal environment: Vibe finds its home and
  sign-in, and the model variable takes effect (D10). **M-V13** `--time-limit 5` on a longer task: exit 3, and the export
  exists (D8). **M-V14** a sanitised copy (no `.vibe/`) made trusted on purpose: `read_file` inside it answers, hook
  canaries in the source tree and in the copy's parent do not fire, the same hook inside the trusted copy does (positive
  control) — **measured 3 of 3, 2026-10-12** (D6); the script adds a junction and a symlink pointing out of the copy.
- **M-K1** `-p -` with piped stdin — **measured 2026-10-12: a literal dash; prompt mode cannot take stdin** (D15). **M-K2**
  ACP: the answer's `agent_message_chunk`s, `usage_update`, whether `/usage` gives an input/output split, the session id, a
  refused tool's shape. **M-K3** ACP write arms (file, shell, any discovered tool) with every permission refused — 0 written
  (3 of 3 by hand, 2026-10-12). **M-K4** a tool that writes WITHOUT a permission request (a negative search over every tool
  kind the session announces). **M-K5** a resumed session (`session/load` / `resume`) keeps the refusals and the client-fs
  routing. **M-K6** reads: outside the working folder refused when the shim serves them (by hand, 1 run), and read without the
  offer (1 run: quoted). **M-K7** signed-in model ids, ACP error codes, quota and not-signed-in wording (the subscription
  `401` captured). **M-K8** `KIMI_CODE_HOME` path-length behaviour. **M-K9** what Kimi reads or runs from DISK without the
  client — project skills, hooks and `.kimi-code/` config in the working folder, with canaries. **M-K10** a user-level MCP
  server in `~/.kimi-code/config.toml` with `mcpServers: []` — does its process start (D8b).

Unit tests (red first where they guard a defect):

- `AcpClientTests` (S4, against a scripted fake agent): every permission request refused whatever its options; a
  read inside the working folder served, outside or of an excluded name refused; `initialize` offers read and never write or
  terminal; the model set before the prompt; an unknown client request answered with an error; the deadline kills the
  child; usage read from `usage_update`. `--ask-acp` exit codes, and that it never exits 64. The shim tears its child down on
  every exit path — its own deadline, an ACP protocol failure, the agent's premature EOF and a normal end — with a bounded
  graceful cancel, then a tree-kill, then a wait; tested with a fake agent that spawns a heartbeat grandchild, checking that
  both pids are gone after an outer timeout, a shim deadline and a premature EOF (consultation `f48a09c9`). The shim's own
  stdin EOF means "prompt complete" (`ProcessLauncher` closes stdin after writing, `ProcessLauncher.cs:479`), never cancel;
  `kimi acp`'s stdin stays open for the replies, both its streams are drained concurrently, and reverse requests are
  answered while `session/prompt` is awaited. `fs/read_text_file` is served only for an absolute path whose lexically
  normalised form is inside the snapshot root by path components AND is a manifest entry — the bytes read from the
  manifest's stored path, never the supplied spelling; drive-relative, root-relative, UNC/device, ADS (`:`), trailing-dot or
  trailing-space components and unmatched 8.3 names are refused; ordinal matching. A test swaps a file for a junction after
  the snapshot is made and expects a refusal (the snapshot serves retained bytes or opens by handle refusing links).
- `VibeRuntimeTests` / `KimiRuntimeTests`: argv never holds `--trust`, `--add-dir`, the prompt or a newline; holds both D2
  flags; env carries the model; `ReadAnswer` takes the last assistant message from a captured array; `ReadUsage` reads a
  captured `export.json`; a missing export is unknown, not zero; `cost` is null; exit 3 with an assistant message is a failure;
  the export folder is gone after the read.
- Owned home (D17): created empty; a `hooks.toml`, a `config.toml` declaring hooks/MCP/agents, an `agents/` or `tools/` folder,
  a non-empty trust list, an unexpected file or a link in it → refusal naming the path; Vibe's own inventory (RESULTS §22)
  → go; two turns on one home are serialised; a round's home is gone with the round. The child environment holds exactly
  the platform's minimal set plus `VIBE_HOME`, `VIBE_CLI=python`, `VIBE_ACTIVE_MODEL` — a parent `VIBE_MCP_SERVERS` never
  reaches it (a test sets one in the parent and inspects the request). Linux/WSL child → refused (D18). Model: an alias outside
  the curated list refused before launch; an export whose `active_model` is absent or different → failure with usage kept.
- `crossesTo`: a `vibe` / `kimi` row is not sent to a server older than its constant; a test that a host whose `RUNTIMES`
  lacks `vibe` does not overwrite a `vibe` row in the server's settings file (D13). **And one live compatibility check**
  (plan gate, codex, 2026-10-12): the extension's REAL send path (the settings mirror the server reads) against the last
  RELEASED `coai-mcp` binary, asserting the rows are withheld and the older server serves its other rows unchanged. The
  Team server is not the counterpart here: these rows never reach it (D12 — machine-only; a Team-server row carries the
  runtime `remote`).
- Each launch path (reviewer, `consult`, question row) carries the model variable in the request's explicit environment,
  goes through the one MCP launch control (D8b), and starts in a sanitised snapshot (or an empty folder) — never the
  checkout (D6); an adapter that does not ask for a snapshot keeps today's directory, byte for byte.
- `ReviewSnapshotTests` (D6): `.vibe/`, `.agents/skills/`, `AGENTS.md` and `.git` absent at every depth (case-folded on
  Windows); a symlink and a junction refuse with the path; a colliding name and an escaping path refuse; byte, file and
  time ceilings refuse with the number and leave no partial copy; a populated submodule is copied, an unpopulated one and
  an LFS pointer are listed unavailable; an `export-ignore` / `export-subst` file keeps its reviewed bytes; the live-tree
  mode copies untracked-not-ignored files, omits deleted ones, and retries once then refuses on a change mid-copy; the
  manifest maps an absolute snapshot path back and refuses one outside it; the snapshot is gone with its scratch
  directory.
- `RateLimitTests`: the captured billing line → `Hit` true, `Hopeless(Reason)` true, one launch, zero retries.
- `MachineOnlyRuntimes` holds both; the Team server refuses a `vibe` row by name.
- `EveryAdapterRecordsWhatItLaunchedTests` gains both adapters; `ConfinementPlannerTests` gains each arm and refuses an
  `unmeasured` capability.
- Extension: the hard-coded lists above, a preset round-trip through `vendorsFrom`, `crossesTo` holding a row back from an
  older server, `OFFICIAL_SOURCES` admitting only the official installers, the ▶ launch never falling back to codex.
- Whole suites before each PR (`dotnet` test executables and `npm test` in `src_vs_code`), `plan-lifecycle.mjs`, the
  generators' `--check`.
- One live review round per runtime through the product (`review_plan` on a scratch branch) per side, recorded in the
  RESULTS file — and, for each runtime, one live `consult` (a two-turn conversation, so the resume runs) and one live
  question-consultant row through the product, per side (plan gate, codex, 2026-10-12: unit tests and scripted ACP can pass
  while the real orchestration fails to launch the shim or route the answer). Each flow, its runtime, and what it does not
  cover are listed in `research/module_tests.md`.
- Aggregate cap: a second snapshot that would pass the ceiling is refused with both numbers; a free-space floor below the
  volume's free space refuses; a finished launch gives its bytes back to the count (a test with a fake volume reader).

## 6. Owner-only prerequisites

- **Kimi sign-in** — done on Windows 2026-10-12 (`kimi login`, managed `kimi-code` OAuth). WSL still needs `kimi` installed and
  signed in for S4's WSL cells.
- **Vibe in WSL.** Only the Windows side is installed and signed in; until `vibe` is installed and signed in there and the probe
  has run, D18 refuses Vibe on a Linux/WSL child.
- Releases after each story follow the repository's release process with the owner's go.

## 7. Budget — what grows

- **coai's per-attempt export folders** (~90 KB each, the journal copy): zero retained — deleted right after the read (D7).
  A crash between launch and read leaves one inside its `coai-answers-*` directory, and that directory is removed by the
  sweep that already runs: `PanelService.PruneOldAnswerDirs` (`src_mcp/src/Server/PanelService.cs:1365`), at most once
  every 10 minutes per process, on the round's build path, removing the product's scratch directories in temp older than
  **6 hours** (`PruneOldScratchDirs`, `:1405`; the prefixes in `shared/temp-sweep.json`). No new sweep. Test: an export
  folder left in an old `coai-answers-*` directory is gone after `PruneOldScratchDirs`. If the consultant's launch has no
  such directory today, it gets one under the same prefix rather than a new prefix. **The round path is not the only
  caller** (plan gate, codex, 2026-10-12: a crashed `consult` followed by no round would leave its snapshot forever):
  `ConsultationSweeper`'s beat (`src_mcp/src/Server/Consultation/ConsultationSweeper.cs`, `Swept`), which runs every
  `Pace.SweepEvery` while the server serves whether or not a round runs, also calls the same throttled
  `PruneOldAnswerDirs` — one sweep, two callers, the 10-minute throttle unchanged. Test: an old `coai-answers-*`
  directory holding a snapshot is gone after one beat with no round built.
- **Vibe's session store** (`logs/session/unified/<id>`, ~130 KB for two short turns, estimated ~300 KB for a review):
  under D17 it lives in coai's OWN home, never the person's `~/.vibe`, so it goes when that home goes (next bullet). The
  person's `~/.vibe` gains nothing from coai.
- **Owned homes** (D17): ~85 KB of Vibe's own unpacked plugins per home plus its sessions (~130 KB for two short turns). A
  round's home lives in the round's `coai-answers-*` directory and goes with it (and with the 6-hour sweep after a crash);
  a conversation's home under `LocalApplicationData/coai-vibe/` is deleted when the conversation closes or lapses, by the
  consultation sweep that already closes it (`ConsultationSweeper`), with a test that a closed conversation's home is gone.
- **Snapshots** (D6): one per code round per runtime-that-asks (≈ the tree's size — 38.8 MB for this repository), inside
  the round's `coai-answers-*` scratch directory and gone with the round; and one per `consult` conversation, refreshed in
  place, deleted with the conversation's home when it closes or lapses. Both are covered by the 6-hour sweep after a crash. At 50 Vibe launches a day on a repository this size, worst case
  ≈ 2 GB of copying a day. On disk at once: one snapshot per concurrently running round or turn — a few hundred MB at
  the handful of rounds this machine runs together — and never more than D6's aggregate cap (2 GB by default), which
  refuses a new snapshot before the temp volume runs low (free-space floor, 5 GB by default).
- **Kimi's session store** (`~/.kimi-code/sessions/wd_<working-folder name>_<hash>/<session>`; plan gate, codex,
  2026-10-12: it needs a projection and an owner). Measured from the hand runs: **120–220 KB per session** (eight folders,
  1.6 MB). At 50 Kimi launches a day ≈ 10 MB a day, ≈ 3.6 GB a year if nothing retires it. **coai retires exactly what it
  created, by RECORD, never by name pattern** (code round, codex, 2026-10-12: a `wd_snapshot-*` wildcard would also match a
  person's own session in a folder they happened to call `snapshot-…`). BEFORE `session/new` the shim appends a PENDING
  line to an ownership record in coai's data folder (`kimi-sessions.jsonl`: the working folder it will hand Kimi, the round
  or conversation, the time, no session id yet); after `session/new` answers it appends the resolved line (the session id
  and its folder). A pending line with no resolved line is a shim killed in between (plan gate, codex, 2026-10-12): the
  sweep reconciles it by Kimi's own `wd_<working-folder name>_<hash>` folder for exactly that recorded working folder —
  the one session folder whose name Kimi derives from it, created after the pending line's time — and retires it like a
  resolved one. The round (or the closed or
  lapsed conversation) deletes the folders its record lines name and drops those lines; the consultation beat's sweep
  deletes recorded folders older than 6 hours whose owner is gone (a crash's leftovers). A folder no record line names is
  never touched, whatever its name. Whether Kimi's
  `session_index.jsonl` keeps stale lines for deleted folders, and whether that matters, is measured in S4 (M-K5).
- No new table, cache or long-lived process.

## 8. Not this plan's

- **Chat** with Vibe or Kimi (`cliChatLaunch.ts` `ADAPTERS`, the `chat` seed list).
- **The Team server** (slots, `VendorLogin`, `SlotEnvironment`, `CooldownParser`, the Dockerfile) — D12 keeps both runtimes
  machine-only until a plan for it exists.
- **The bench** (`src_bench/CoaiBench`).
- Vibe's `vibe-acp` / `vibe-app-server` as a transport (Vibe's CLI flags plus the snapshot hold, measured; moving Vibe onto the
  ACP shim later would give it the same client-owned blocks Kimi has).
- Effort / thinking controls for either runtime until a flag is measured taking effect.
- Carrying Vibe's `cost_usd` as an estimate (D9).

## 9. Definition of Done

- [ ] `research/RESULTS_vibe_confinement.md` and `research/RESULTS_kimi_confinement.md` hold every M-cell, 3 of 3 on Windows.
- [ ] A Vibe row and a Kimi row each pass one live plan round and one live code round through the product, on Windows.
- [ ] Both answer `consult` and a question-consultant row; a resumed turn is write-checked.
- [ ] No launch of either CLI carries `--trust`, `--add-dir` or the prompt in argv, and no Vibe launch starts in the checkout —
      pinned by tests on every launch path; every Vibe launch granted the tree starts in a sanitised snapshot.
- [ ] Every Vibe launch runs on a coai-owned, validated home with a built environment and `VIBE_CLI=python` (D17); a Linux/WSL
      child is refused (D18); the model is checked before and after (D10).
- [ ] A billing refusal is not retried.
- [ ] coai deletes only the Kimi session folders its ownership record names (§7).
- [ ] Both runtimes are machine-only; an older server never receives the rows.
- [ ] Seeds regenerated; every hard-coded runtime list in tests updated; both suites green.
- [ ] Docs, help in five languages, `vendor-routing.md`, READMEs and CHANGELOGs updated.
- [ ] This plan promoted to `research/` with its deviations.
