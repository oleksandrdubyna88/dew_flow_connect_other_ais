# PLAN — Mistral Vibe and Kimi Code as coai runtimes, on the person's own subscription

> Status: **in progress, 2026-10-13 — S1 complete: the billing refusal (D14), the sanitised snapshot (D6) and the
> `--ask-acp` Kimi shim shipped in coai-mcp 0.51.0 (#785), and the Vibe home manifest (D17) on branch
> `feat/vibe-kimi-runtimes` (`shared/vibe-home-manifest.json`, RESULTS_vibe_confinement.md §24). S2 is BUILT on the same
> branch — server (`VibeRuntime`, the launch control, the round's snapshot) and extension (the preset, the models, the
> buttons, the older-server gate) — unreleased, its live product rounds not yet recorded. S3 is BUILT on the stacked
> branch `feat/vibe-consultant` — `VibeConsultant`, the conversation's one snapshot and home under `coai-vibe/<id>`, the
> question row's copies of its granted roots, the D20 home sweep, the three `runtime-capabilities.json` rows (web
> `unmeasured`) — unreleased, its live `consult` and question row not yet run. S4's MEASUREMENT half is done
> (RESULTS_kimi_confinement.md §3–§15: 74 launches through the shipped shim, 2026-10-13); two cells contradicted the plan,
> so S4's code half and S5 were re-planned the same day around D19 — a coai-owned `KIMI_CODE_HOME` whose `credentials/` is a
> junction to the person's — and `KimiRuntime` is not written.** Open: S2's live rounds and release, S3's live consult and question row and release, S4's code half
> (gated on D19's cells, §5), S5. Re-planned 2026-10-12 after M-V11 (D6: a sanitised snapshot of the tree replaces the
> empty working folder). Scope: `src_mcp` (two new reviewer runtimes and two consultants), `shared/*.json` seeds and their
> generated TypeScript, `src_vs_code` (runtime lists, presets, install, the catalog model card), docs. Chat, the Team server
> and the bench are out (§8). Kimi runs over ACP behind a coai shim (D15, re-planned 2026-10-12 after the owner signed
> in). Each Vibe cell is 3 of 3 on Windows; each Kimi cell of §3–§15 is 3 of 3 on Windows through the released 0.51.0
> shim; nothing is claimed for WSL until the probe scripts have run there.
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
    `AGENTS.md` and `agents.md`, `.kimi-code/` — the set `SnapshotExclusions.Default` ships (`SnapshotOptions.cs:59`): each
    Kimi name in it either CONFIRMED loaded from disk untrusted with no client call (M-K9 (a), RESULTS_kimi_confinement.md
    §12, 3 of 3) or searched for by Kimi itself (`agents.md`, §2.2) — **plus `.agents/agents/`** (2026-10-13: Kimi's documented second sub-agent location; leaving it out costs nothing
    and M-K9 (e) measures whether the list is then COMPLETE against every project path Kimi documents). The shim's own
    list (`KimiAcpProfile.cs:34`) gains the same name. An excluded path is named in the manifest, never silently lost.
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
  **measured 2026-10-13, and the first answer FAILED** — M-K10: a stdio server declared in the person's own
  `~/.kimi-code/mcp.json` started 3 of 3 while the shim sent `mcpServers: []` (`AcpMessages.cs:62`;
  RESULTS_kimi_confinement.md §13 — ACP's list ADDS servers to the session, it does not replace the home's file, and Kimi
  documents no global switch). Kimi's arm of the one launch control is therefore D19's owned home: no `mcp.json` in it,
  an empty `workspace-trust/` (a project `mcp.json` starts only in a TRUSTED folder — 0 of 4 untrusted, 1 of 1 trusted,
  §12), both validated before every launch, with `mcpServers: []` kept as the belt to that brace. One control
  (`KimiLaunch`) for the reviewer, `consult` and the question row — a test per path, as for Vibe.
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
  keeps the reported usage. A match proves Vibe accepted the alias, not which backend answered. **Kimi takes its model
  through the ACP `model` config option (D15), never the config default — and is checked twice too, for reasons of its
  own** (M-K7, RESULTS_kimi_confinement.md §10, 2026-10-13): `--model nope` is a bare `-32603 Internal error` on
  `session/set_config_option` → exit 70 with no sentence to name, and two of the six ids a session announces
  (`moonshot-ai/kimi-k2.6`, `moonshot-ai/kimi-k2.7-code`) carry their own `api_key` in the person's `config.toml` and
  bill `api.moonshot.ai` — the person's API key, not the Kimi Code plan this row is for. So (1) BEFORE the launch the id must
  be on the curated list, which holds `kimi-code/*` ids only: `kimi-code/k3-256k` now (it answered the probe's 64 paid
  turns); `kimi-code/k3` and `kimi-code/kimi-for-coding` (K2.8 Preview) join when M-K7b records one answered turn each —
  Vibe's rule, seen, not listed (`VibeLaunch.cs:20-25`); `kimi-code/kimi-for-coding-highspeed` is on the list with its 401
  named (`KimiAcpProfile.cs:39-43` — the plan tier decides, not coai); an id outside `kimi-code/` is refused with the
  billing sentence, never sent. (2) INSIDE the shim, after `session/new`, the asked id must be among the session's
  announced `configOptions` for `model`, else exit 65 with the announced list — the sentence `-32603` never gives, and
  the one place the list is authoritative. `thinking` has `low`/`high`/`max` on the k3 models (§10) — an `effort`
  candidate, unmeasured, out (§8). Discovery only once a CLI command lists the models without a model turn. The terminal
  ▶ launch gets the same env/flag — today an unknown runtime falls back to `codex -m …`
  (`src_vs_code/src/vendorTerminal.ts:35`).
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
    permission request — it never inspects which tool asked. **Measured through the shipped shim, 2026-10-13 (§6, §7):
    0 of 30 write attempts landed; every one asked the client first — `Write`, `Edit`, foreground and background `Bash`,
    and a sub-agent's own `Write`/`Bash`, which came to the client through the SAME channel — and the positive controls in
    `kimi -p` wrote 7 of 7.** Of the 27 announced tools, `AgentSwarm`, the cron tools and the goal tools were not exercised
    (§14: approval-required by Kimi's reference, or session state); that is the stated edge of the evidence.
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
    `.kimi-code/AGENTS.md`; the shim refuses those names at every depth. **Narrowed 2026-10-13 (M-K9 (a), §12, 3 of 3):
    that search reaches the client only for files that are NOT there. When they exist in the working folder — `AGENTS.md`,
    `.kimi-code/AGENTS.md`, `.kimi-code/skills/*`, `.agents/skills/*`, `.kimi-code/agents/*.md` — Kimi loads them from
    DISK, untrusted, with or without `.git`, with no client call at all.** D6's snapshot exclusions are the block; the shim's
    list is the second check on the same names and is kept, never claimed as the first.
  Writes stay the hard requirement, and they are version-specific evidence, not a sandbox: Kimi decides which operations
  ask — Kimi 2.1.1 is what §6–§7 measured, and D19 WARNS, visibly, when another version runs (owner, 2026-10-13). Kimi's own session
  store (`sessions/`, `session_index.jsonl`) is a permitted vendor write — and under D19 it lands in coai's OWN home
  (M-K8, §11: the session landed in the copy's `session_index.jsonl`, the person's index unchanged, 6 of 6). The working
  folder is the same sanitised snapshot as Vibe's (D6, with `.kimi-code/`, both spellings of `AGENTS.md` and
  `.agents/agents/` in the exclusions), so whatever Kimi loads from DISK without the client is not the branch's.
  The model is set every session with `session/set_config_option {configId: "model"}`: a session came up on a model the plan
  does not include and failed `401 Your current subscription does not have access to kimi-for-coding-highspeed` (§2) — that
  sentence is a diagnosis row. `mode` stays `default` (manual approvals — the shim's refusals ARE the approvals).
  **The shim is `coai-mcp --ask-acp`**, the established self-invocation pattern of `--ask-api` / `--ask-local` /
  `--ask-remote` (`src_mcp/src/Program.cs:304-307`, `AskApiMode.cs`, `LocalRuntime.SelfInvocation` `LocalRuntime.cs:64`):
  `KimiRuntime.Build` returns a `ProcessRequest` for the shim, the prompt on its stdin, so `ReviewerExecutor` launches,
  times out and kills it like any CLI; the shim runs `kimi acp` as ITS child and prints the answer and usage on stdout. A new
  one-shot mode is written into the list in `.agents/PROJECT.md` and never exits 64. The ACP client half is one unit with an
  `IAcpProfile` per vendor (Kimi: the command, the model option id, the exclusion names) — one module per vendor; nothing
  else speaks ACP yet. Usage: `usage_update {used, size}` is CONTEXT tokens with no input/output split (`AcpSession.cs:460`).
  **M-K2 found the split (§5, 3 of 3):** `/usage` sent as a SECOND `session/prompt` on the same session after `end_turn`
  is answered by Kimi itself in ~2 s with no model call — `Context: N / 262144 tokens (…) Session total: N input, M output`.
  The shim sends it after an answered turn and parses that one line into the report's `tokensIn`/`tokensOut`; an answer
  that does not parse keeps `notCaptured: true`, never zero; a turn that did not end `end_turn` sends nothing more. The
  session log's `outputTokens=NN` lines are a second source, not read (a log's shape is nobody's contract). Whether the
  total is cumulative across a loaded session is M-K5b's (S5). Cost `null` (D9).
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
  `vibe` once on an empty home and writes `shared/vibe-home-manifest.json` (Vibe version → path → SHA-256). **The version
  rule is WARN AND RUN — the owner's decision of 2026-10-13 ("set it up once and it must work if it can"), replacing the
  refusal S1 shipped (`VibeLaunch.HomeRefusal`, `VibeLaunch.cs:242-248`):** a Vibe version with NO entry in the manifest
  LAUNCHES. Its home's SHAPE is still validated in full — the forbidden names (a `hooks.toml`, any `config.toml`, an
  `agents/` or `tools/` folder), a link anywhere, a trust list that trusts anything — which is exactly the shape check that
  already runs before the version is known (`VibeHome.Refusal(home, manifest)`, `VibeHome.cs:44-45`); the files under the
  pinned prefix are admitted by NAME for that unknown version; and the launch's result carries the SHARED version warning
  (one shape for both runtimes, D19: the running version, the measured one(s), that every confinement number AND the
  home's plugin hashes are the measured version's, and that re-running `scripts/vibe-home-manifest.mjs --write` and the
  probe is advised) — the reviewer line shows it, the row's card shows it. A version WITH an entry keeps its plugin hashes
  ENFORCED: a file under the pinned prefix whose bytes differ from the entry's is the tampered bytes of a KNOWN version —
  the one place nothing can be made safe — and is still a refusal with the path (`VibeHome.cs:153-158`). Tests: an
  upgraded CLI with no entry, on a fresh home and on a resumed home — both launch, both carry the warning, both still
  refuse a `hooks.toml` and a link; a known version with one changed plugin byte refuses with the path; a known version
  with its bytes carries no warning.
- **D18 — Vibe and Kimi on Linux and WSL are refused until measured.** Every number above is Windows. On Linux Vibe's keyring may
  fall back to a file inside `VIBE_HOME`, which would break D17, and nothing was measured there. A launch whose CHILD runs on
  Linux (including WSL launched from Windows) is refused with a sentence naming this plan, for both runtimes (Kimi's WSL
  side is just as unmeasured); the probe scripts' WSL runs lift it in a later change. **Kimi's first Linux questions are
  D19's** (2026-10-13): there the credentials junction is a SYMLINK into a folder Kimi keeps at `0700` (RESULTS_kimi §11)
  — whether Kimi opens its credentials through a symlink, whether it refuses or re-`chmod`s a linked folder, and whether a
  refresh through the link lands in the person's folder, are unmeasured; the WSL/Linux run of the Kimi probe answers them
  before D18 lifts for Kimi.
- **D19 — every Kimi launch gets a coai-owned `KIMI_CODE_HOME`, a built environment, and the person's sign-in by JUNCTION —
  never by copy** (2026-10-13; evidence RESULTS_kimi_confinement.md §11–§15, every cell 3 of 3 on Windows through the
  shipped shim). Two cells the plan had no block for: the person's own `config.toml` `[[hooks]]` fired under coai's launch
  (M-K9 (d), §12) and the person's own `mcp.json` server started under `mcpServers: []` (M-K10, §13). Both are the PERSON's
  configuration, read from Kimi's home; `KIMI_CODE_HOME` moves that home and is honoured 6 of 6 (M-K8, §11; a 280-character
  session path inside a 205-character home was written and read back, so Vibe's short-path rule is not Kimi's). D17's
  shape, then — but Vibe's sign-in was in the OS keyring, and Kimi's is not:
  - **The sign-in, and why a copy is not an owned home.** Kimi's OAuth is FILE-based (`credentials/kimi-code-env-<id>.json`,
    `storage = "file"`), the access token lives 900 s, so nearly every launch refreshes it — and **the refresh token ROTATES
    on every refresh (4 of 4, §11)**. A copied `credentials/` that refreshes holds the only live pair and the person's
    `~/.kimi-code` is signed out; the probe survived by re-syncing the freshest pair into every copy inside the 900 s
    window, which a product cannot do. **M-K8b** (§11, 3 of 3; `scripts/probe-kimi-confinement.mjs:287-293`): an owned
    home whose `credentials/` is a JUNCTION to the person's `~/.kimi-code/credentials` answered 3 of 3, run 1 refreshed the
    token THROUGH the junction into the person's folder, and the person's own home answered afterwards — the sign-in
    survived. The alternatives were weighed and rejected: *copy and write back* (a crash between the refresh and the
    write-back loses the rotation; a person's own `kimi` refreshing in between is clobbered by the write-back); *hard-link
    the credential files* (a rewrite by rename splits the link, and one side is signed out); *a directory symlink* (needs
    a privilege or Developer Mode on Windows, which a junction does not); *an undocumented credentials-path override*
    (none is documented, and a closed-source CLI's undocumented variable is not a foundation). **So the junction is the
    only sign-in route, and this is what it costs, stated rather than hidden:** (i) Kimi runs on the person's LIVE
    credentials, and a refresh under coai's launch rotates the person's token — a permitted vendor write into the person's
    folder, named as such like the session store; (ii) two refreshes of one rotating token at the same moment — two coai
    launches, or a coai launch and the person's own `kimi` — are UNMEASURED (M-K14): whether the server keeps a grace window
    or revokes the grant on reuse decides whether a race signs the person out, so until M-K14 is recorded coai SERIALISES
    its own Kimi launches per credentials folder (below) and the person's own interactive `kimi` is a stated race; (iii) a
    disposal that walked the junction would delete the person's sign-in — closed below, with a red-first test; (iv) on
    Linux and macOS the junction is a symlink into a `0700` folder — D18 refuses those hosts until the probe runs there.
  - **Making the junction.** The probe used `mklink /J` (`scripts/lib/kimi.mjs:121-124`) — a mount-point reparse point. The
    product makes the same reparse point itself (`DeviceIoControl` with `FSCTL_SET_REPARSE_POINT`, a P/Invoke the AOT build
    takes; no shell, no quoting of the person's profile path, no privilege) — or, if that proves awkward, `cmd.exe` by its
    absolute `System32` path with `/d /c mklink /J`, the probe's exact form. Either way the link is read back after creation
    (`DirectoryInfo.LinkTarget`, which the repository already reads for worktrees, `WorktreePaths.cs:64-82`) and must name
    the person's folder, or the launch is refused. Before it is made: the person's home is `KIMI_CODE_HOME` of coai's OWN
    environment when set, else `~/.kimi-code`; its `credentials/` must exist (absent → refused as *not signed in — run
    `kimi login`*, before any process starts — the §10 sentence, earlier and cheaper), must not itself be a link
    (`SnapshotLinks.IsLink`, `SnapshotLinks.cs:37` — coai never chains links), and must be a local path (a junction cannot
    point at a UNC target; a roaming profile is refused with the sentence). coai never creates, renames or deletes anything
    INSIDE the target: the only writes into it are Kimi's own.
  - **The home.** One per reviewer per code round, a short folder in the round's own scratch (the `vh-` shape of
    `VibeRuntime.HomeFor`, `VibeRuntime.cs:149-150`, as `kh-`; short by habit, not need — M-K8), gone with the round; one
    per `consult` conversation under `LocalApplicationData/coai-kimi/`, kept across its turns because the session store
    `session/load` needs lives in it (M-K8: sessions land in the owned home, 6 of 6), deleted when the conversation closes
    or lapses (§7). What it holds: the credentials junction, and NOTHING copied from the person's home — never
    `config.toml` (it carries the Moonshot `api_key` of the `moonshot-ai/*` rows and the `[[hooks]]` of M-K9 (d)), never
    `tui.toml` (`[upgrade] auto_install = true`), never `mcp.json`, `workspace-trust/`, `skills/`, `agents/` or `AGENTS.md`.
    **What ELSE it must hold is unmeasured, and M-K11 decides it before the home is coded:** M-K8b's "owned home" was a
    COPY of the person's `config.toml`, `tui.toml`, `device_id`, `region`, `migrations-effort.json` and the bundled
    `bin/rg.exe`/`bin/fd.exe`, minus `credentials/`, plus the junction (`scripts/lib/kimi.mjs:73-94`) — not an empty home.
    So M-K11 (a) starts from the junction alone and adds, one at a time, only what a 3-of-3 sign-in or a working
    `Grep`/`Glob` turns out to need: a coai-WRITTEN minimal `config.toml` naming the `managed:kimi-code` provider and nothing
    else (if Kimi's built-in default does not already pick it when credentials exist); `device_id` (is the token
    device-bound? does a fresh id per round register a new device on the person's account? — copied only if a fresh one
    fails, and then stated); `region` and `migrations-effort.json` (does an absent marker run a migration that writes?);
    `bin/rg.exe`/`bin/fd.exe` (without them, does `Grep` fail, fall back to the PATH, or DOWNLOAD into which home?). Each
    candidate's concern is written into the cell's row; what the row admits is what the home gets.
  - **Validation before every launch** (the `VibeHome` shape, `VibeHome.cs:36-45`, run inside the home's turn by one launch
    control, `KimiLaunch`, the `VibeLaunch.TakeAsync` order, `VibeLaunch.cs:143-168`): the link rule is inverted at exactly
    one path — `credentials` MUST be a junction whose read-back target is the person's folder; a link anywhere else refuses.
    `config.toml` absent, or byte-identical to coai's own template (per M-K11 (a)); `tui.toml` absent or coai's own;
    `mcp.json` present → refused; `workspace-trust/` with any entry → refused (Kimi's trust store is the home's, §12 — a
    trusted working folder starts a project `mcp.json`); `skills/`, `agents/`, `AGENTS.md`, any `hooks*` → refused. The
    rest — `sessions/`, `session_index.jsonl`, `workspaces.json`, `cache/`, `logs/`, `user-history/`, `file-history/`,
    `telemetry/`, `updates/` — admitted by NAME from the inventory M-K11 (c) records of an owned home after `kimi --version`,
    one turn, and a second start. **A hash manifest (`shared/kimi-home-manifest.json`, the Vibe step's twin) only if
    M-K11 (c) finds Kimi writing files it later READS as configuration or code** — Vibe's unpacked plugins were that
    (D17, RESULTS_vibe §24); data is admitted by name. Every refusal names the path and the cure (delete the home).
    Turns sharing a home are serialised (`VibeHomeTurns.cs:21`, whose logic is vendor-free — shared, not copied).
  - **The environment.** Built, never inherited: `ProcessEnvironment.Minimal` (`ProcessEnvironment.cs:94`) plus exactly
    `KIMI_CODE_HOME` and `KIMI_CODE_NO_AUTO_UPDATE=1` (set from the first launch; its effect CLAIMED only after M-K13, which
    gates nothing). Today the shim
    starts `kimi acp` with the launcher's default, which inherits (`AcpTransport.cs:87-88`, `ProcessLauncher.cs:100`) —
    the probe reached Kimi by inheritance from the shim's own environment (`scripts/probe-kimi-confinement.mjs:366`). The
    product does not rely on two hops of inheritance: the shim takes `--home <path>` and REBUILDS the agent's environment
    from it through the one builder `KimiLaunch.Built` that the runtime also uses for the shim's own request; an
    `EnvironmentRefusal` (the `VibeLaunch.cs:91-94` shape) stands on both hops, and a test inspects each. `USERPROFILE`
    stays in the minimal set (`ProcessEnvironment.cs:81-83`), which is why the `~/.agents/*` standing below exists.
  - **The version: WARN AND RUN — the owner's decision, 2026-10-13.** Every Kimi number is 2.1.1, and Kimi updates itself:
    `tui.toml [upgrade] auto_install = true`, and `updates/` was rewritten during the probe (§3, §14). `kimi --version` is
    asked before every launch in the built environment on the owned home (the Vibe shape) and compared with
    `KimiAcpProfile.MeasuredVersions` (`["2.1.1"]` today — the versions the whole probe has run on). A version outside the
    list STILL LAUNCHES; the launch's result carries a visible WARNING naming the running version, the measured one(s), and
    that every confinement number coai has is the measured version's; the reviewer line shows it, and the panel shows it on
    the row's card; re-running `scripts/probe-kimi-confinement.mjs` on the new version is ADVISED by the sentence, never
    required. The owner decided the same for Vibe on 2026-10-13 (D17): **one shared version warning for both runtimes — one
    shape, never two** — a record on the launch result built by one builder (the runtime, the running version, the measured
    versions, what the numbers cover, the advised step), carried by `LaunchTurn` (`LaunchTurn.cs:17-50`, today a refusal
    only) onto `ReviewerLaunch` (`ReviewerExecutor.cs:1047-1053`) and from there to the reviewer line and the card; Vibe
    keeps the one refusal that cannot be made safe, a KNOWN version's plugin bytes that differ from its entry (D17).
    `KIMI_CODE_NO_AUTO_UPDATE=1` stays in the
    built environment and M-K13 stays an informational cell, not a gate: the pin only makes the warning rarer, and whether
    it works is worth one measured row, not a blocked story.
  - **`~/.agents/skills` and `~/.agents/AGENTS.md` — a stated limit, not a refusal.** Kimi documents reading them from the
    OS home whatever `KIMI_CODE_HOME` says (unmeasured, §14). They are the PERSON's own prompts, not the branch's; every
    write still asks (M-K4), and Kimi's reads are `unconfined` already (D15, the codex standing) — so a refusal while they
    exist would bar every person who uses Kimi skills interactively, for no write it would stop. M-K12 measures (a) that
    they load under an owned home at all (a canary skill in the real `~/.agents/skills/` — the owner allowed that write on
    2026-10-13; the probe still takes `--allow-agents-canary` so it never happens by accident — removed in a `finally`,
    verified absent), and (b) whether redirecting `USERPROFILE`/`HOME` in the built environment closes them
    without breaking the launch (the `kimi.exe` path and the junction target are absolute, so it may). If (b) is clean 3 of
    3, the built environment takes it and the limit goes; else the standing is written into `runtime-capabilities.json`'s
    Kimi rows, the help, and the question consultant's `disk` row — exactly as codex's is.
  - **Disposal — the junction first, by its own name, never through it.** `GitScratch.DeleteEvenIfReadOnly`
    (`GitScratch.cs:19-31`) enumerates `SearchOption.AllDirectories` — .NET's enumeration ENTERS a directory reparse point —
    and only then `Directory.Delete(recursive)`; the probe's own note is that a recursive remove followed a junction on a
    throwaway (`scripts/lib/kimi.mjs:103-104`). On a home holding the junction that walk is the person's credentials
    folder. Three rules: (1) the home's disposal removes `credentials` FIRST with a non-recursive delete of the link itself
    (the probe's `rmdirSync`), verifies the link is gone AND the person's folder still holds the files it held (names and
    count read before), and only then deletes the rest; (2) `DeleteEvenIfReadOnly` learns reparse points — the walk is
    done by hand, never entering a directory `SnapshotLinks.IsLink` names, and unlinks it in place — so the 6-hour scratch
    sweep (`PanelService.cs:1385`, `PruneOldScratchDirs` `:1412`), the snapshot's `TryDeleteRoot` (`ReviewSnapshot.cs:185-197`)
    and a lapsed conversation's home (§7) are safe for a crash-leftover home; (3) the TEST sweeps (`TempDirsAreSwept.cs:132-173`
    is raw `Directory.Delete`; `src_vs_code/scripts/run-tests.mjs`) get the same test, or a stated reason. The red-first test:
    a scratch tree with a junction to a folder holding a read-only file — after every deleter the junction is gone and the
    file, bytes and attribute, is untouched.
  - **Serialisation, until M-K14 says otherwise.** In-process: the scheduler's per-provider cap (`BoundedScheduler.cs:91`,
    `perProviderCap`, 2 today) is 1 for an adapter that declares `LaunchesOneAtATime` — a capability member on
    `IReviewerRuntime` like `NeedsASanitisedSnapshot` (`VibeRuntime.cs:61`), never an `if` on a vendor name — so the wait
    is the scheduler's, outside the row's timeout. Cross-process (two editor windows, two servers): a named mutex keyed by
    the hash of the credentials target, taken in `KimiLaunch.TakeAsync` — inside the row's timeout, stated. The cost: a
    round's three Kimi reviewers run one after another, up to three times the wall time — accepted by the owner on
    2026-10-13, with M-K14 to run LATER. When it does: a server with a grace window lifts the cap to the default and drops
    the mutex, a server that revokes on reuse keeps both and writes the person's own interactive `kimi` into the help as
    the remaining race.
  This replaces, for Kimi, what D17 is for Vibe, and D8b's Kimi arm. It does NOT bound Kimi's reads (`Grep`/`Glob`, D15) and
  does not close `~/.agents/*` until M-K12 says it can.
- **D20 — a persistent consultant home has a BOUNDED life** (2026-10-13; the plan gate's finding, codex, accepted). A
  `consult` conversation's home — `LocalApplicationData/coai-vibe/<id>` (D17) or `coai-kimi/<id>` with its junction (D19),
  named by the consultation's id — is deleted when the conversation closes or lapses. That is the only cleanup the plan had,
  and it runs INSIDE the closing: a host killed before the conversation closes leaves the home — junction and all — for ever,
  and nothing reads `coai-vibe/` or `coai-kimi/` again. So a third pass joins the sweep that already runs on every beat and
  at server start: `ConsultationService.Sweep` (`ConsultationService.cs:263-264`) sweeps the records first and then runs
  `ConsultationRetention.Sweep` (`ConsultationRetention.cs:31-34`), on the `ConsultationSweeper` beat
  (`ConsultationSweeper.cs:76-110`, every `ServerPace.SweepEvery` — `COAI_SWEEP_SECONDS`, 60 s unless set, clamped 10–3 600,
  `ServerPace.cs:8,32`) and once in the `PanelService` constructor at start (`ConsultationSweeper.cs:7-10`). The home sweep
  runs AFTER the records (the retention pass's order), over every folder of both roots:
  - **Removed** when (a) the store has no record for the folder's id (`ConsultationStore.cs:17,32`: `consultations/<id>.json`)
    AND the home's last write is older than the window — never a just-made home whose record write is in flight; (b) the
    record is over — closed, failed, lapsed (`ConsultationStore.cs:160-170`, the record's `IsOver`); (c) the record is still
    open but the home's last write is older than the window — in which case the record pass of the same beat has already
    lapsed it (`ConsultationStore.cs:176`), so (b) catches it next beat at the latest.
  - **The window is the conversation's own idle budget**, not a new number: `settings.ConsultIdle`
    (`PanelSettings.cs:606`, `COAI_CONSULT_IDLE_MINUTES`, `:941`, 15 minutes unless set) — the same window after which the
    record sweep closes a conversation as lapsed and a follow-up is refused (`ConsultationRules.cs:36`). A home that sat idle
    longer than its conversation may belongs to a conversation that is closed or about to be.
  - **Kept** when the record is open and the home was written inside the window; and SKIPPED this beat when the home's turn
    is held (a zero-wait take on `VibeHomeTurns` / Kimi's twin, `VibeHomeTurns.cs:21`) — a live launch, judged by the lock
    and never by a stamp, the store's own rule for a running turn (`ConsultationStore.cs:181-199`).
  - **Junction-first for Kimi** (D19's disposal order): the `credentials` link removed by its own name and verified before
    the recursive delete, which never enters a reparse point. **Never the reason a sweep fails** (`ConsultationRetention.cs:20-24`):
    a folder that cannot be listed or removed is logged and skipped, and the next beat tries again.
  - **The bound, stated.** While a server runs, no home outlives its conversation by more than `ConsultIdle` plus one beat
    (15 min + 60 s by default); a host killed mid-conversation leaves its homes until the next server start, when the start
    sweep removes them. What this does not do: run while no coai-mcp runs — a machine that never starts the server again
    keeps the leftovers, which is the bound the record store itself has.
  Tests (§5): an orphaned `coai-kimi/<id>` with no record, older than the window, whose junction points at a folder holding
  a file → the home and the junction gone, the file untouched; the same home younger than the window → kept; a live
  conversation's home (record open, written now) → kept; a closed record's home → gone on the beat; a home whose turn is held
  → skipped, then gone once the turn is given back; a service built with a stale home present → gone at start; a Vibe home
  the same without the junction; an unreadable folder → logged, the pass goes on. The disk projection is §7's.
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
  (D10). **The version rule, re-planned 2026-10-13 (owner: warn and run):** `VibeLaunch.HomeRefusal`'s no-entry refusal
  (`VibeLaunch.cs:242-248`) becomes the SHARED version warning on the launch turn — for a version with no manifest entry the
  shape check (`VibeHome.cs:44-45`) stands alone, the pinned-prefix files are admitted by name, and `LaunchTurn` carries the
  warning (`LaunchTurn.cs:17-50`) onto `ReviewerLaunch` (`ReviewerExecutor.cs:1047-1053`), the reviewer line and the card;
  a known version's plugin hashes stay enforced (`VibeHome.cs:153-158`, a mismatch still refuses). One builder for the
  warning's text and fields, used by Vibe here and by Kimi in S4 (D19) — one shape, never two.
- `VendorDiagnosis`: Vibe's install cure per OS and the failure sentences M-V7 captured (not signed in, quota).
- Seeds: `feature-availability.json` (`runtimes`; `effort` `unmeasured` — no flag sets it; `thinking` `unmeasured` —
  the models carry levels in Vibe's own config but no launch flag was shown taking effect; `fastMode` `none`), regenerate.
- Extension: `RUNTIMES`, both `package.json` enums, `modelsFor` + `modelsProvenance` (curated list, D10), the preset
  (D11), `vendorTerminal` maps + `vibeInstall` + `OFFICIAL_SOURCES` (D16), `JUDGED_HERE`, both `CLI_RUNTIMES`,
  `KNOWS_ITS_OWN_ENDPOINT`, a colour slot, the `crossesTo` gate (D13). The model card is the catalog's (new UI only); the
  row's card shows the shared version warning whenever a launch's result carries one (owner, 2026-10-13).
- Docs: `module_runners.md`, `module_extension.md`, `architecture.md` runtime list, README, help in five languages,
  `.agents/rules/common/vendor-routing.md` (Mistral models → `vibe`), CHANGELOG entries under `## Unreleased`.

**S3 — Vibe as a consultant and a question-consultant row.** Decisions D1, D6 (one stable snapshot path per conversation,
refreshed in place; cwd per capability), D7 (`sessionId`, resume), D10, D17 (one home per conversation, kept across its turns;
the built environment on the question-row path), D18, D20 (the conversation's home swept on the beat and at start when its
conversation is closed, unknown to the store, or idle past `ConsultIdle`).
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
- *Built 2026-10-13 on `feat/vibe-consultant` (stacked on S1+S2), unreleased; the live `consult` and question row are owed
  after the release.* What shipped differently from the bullets above, and why:
  - **The snapshot's stable path is a NAME, not only an object.** A conversation's snapshot is `coai-vibe/<id>/tree`
    beside its home `coai-vibe/<id>/vh-…` (the home is not the conversation folder itself, because the home's shape check
    refuses anything Vibe does not write — the copy cannot live inside it). A process-wide registry
    (`ConversationSnapshots`) refreshes it in place; a turn after a restart (or a settings reload's new service) uses the
    new `ReviewSnapshots.MakeAtAsync`, which builds beside and swaps in at the same path — so §22's "one stable path"
    holds across servers, not only within one.
  - **A refused refresh refuses the TURN** (before the launch, the conversation still open), rather than launching on the
    previous tree: the consultant is promised the live tree.
  - **The adapter declares its homes folder** (`IConsultantRuntime.HomesFolder`, `ConsultantHomes.Vibe`) — the bullets named
    only the snapshot capability; a runtime that keeps a home on every consultant path needed a second declared fact, never
    a vendor name. The homes root is `PanelSettings.ConsultantHomesRoot` (`COAI_CONSULTANT_HOMES`, else local application
    data), EMPTY in hand-built settings, so no test makes or sweeps a home in the machine's own folder.
  - **The question row's copies are a new `CwdKind.Snapshot`** the planner decides (not a branch in the row's launch), and
    the prompt naming the copies is composed by the fan-out and called by the launch (`RowLaunchInput.PromptNaming`) once
    the copies exist. A home-keeping runtime's `none` row stands in an empty `cwd` folder BESIDE its home in the row's
    folder, so a cwd never holds its own home.
  - **Also wired, not in the bullets:** the consultant CHECK (`--check-consultant`) gets the same one-shot copy and home —
    without it a Vibe check would have failed on the missing `Workdir`. `close_consult` removes the conversation's folder at
    once (`ConsultantHomeSweep.RemoveNow`), the beat being the backstop.
  - **D20's deleter landed early:** `GitScratch.DeleteEvenIfReadOnly` unlinks every link in place and never enters one (red
    first: it cleared the target's read-only attribute through a junction and then failed on it). D19's Kimi-specific
    disposal — the junction removed FIRST by name and the person's folder VERIFIED after — stays S4's, in `KimiHome`; the
    sweep is runtime-neutral and already safe for a Kimi home's shape (its test plants one).
  - **The sweep keeps an `asking` record's home** whatever its stamps (a turn in flight; the record pass settles an orphan
    first), beside the plan's "skipped while its turn is held".
  - **Measurement-led:** `vibe × web` is `unmeasured` (no Vibe cell exercises the web), so the planner refuses it; the
    shared "unmeasured" refusal still names research/RESULTS_question_consultant_capabilities.md as the record that would
    admit it, which is the wrong file for Vibe — open.

**S4 — Kimi measured, then a Kimi reviewer row over ACP.** The owner signed in on 2026-10-12, so S4 is no longer blocked.
Decisions D1, D6 (the snapshot with Kimi's exclusions), D8b (the Kimi arm = D19), D9, D10 (the ACP model option, the curated
`kimi-code/*` list, the pre-check and the in-shim check), D11, D12, D13, D15, D16, D18, **D19**. Depends on S1 (the snapshot;
D14) and S2 for the shapes it repeats (a `crossesTo` constant and arm per runtime, the preset and installer maps, the
owned-home validation and the one launch control); not on S3. Complete on its own: a Kimi row reviews plans and code.

*The measurement half — DONE 2026-10-13.* `scripts/probe-kimi-confinement.mjs` → `research/RESULTS_kimi_confinement.md`
§3–§15: cells M-K1…M-K10 and M-K8b, 74 launches, each ACP arm through the shipped `coai-mcp --ask-acp` (so the probe
measured the shipped client, not a copy) but two — M-K5's `session/load` turn and M-K7's bare `session/new`, which the
shim has no path for yet and the probe's small client mirrors (§3) — each write arm with a canary and a `kimi -p` positive
control, 3 of 3 on Windows.
What it recorded, and where each finding now lives: writes held everywhere, sub-agent and background shell included (D15);
`Read` bounded, `Grep`/`Glob` not (D15, unchanged); the branch's instruction files and skills load from disk with no client
call (D15 narrowed; D6's exclusions are the block); **a user `mcp.json` server starts under `mcpServers: []` and a user
`[[hooks]]` fires — D8b's Kimi arm failed, and the plan had no D17 for Kimi → D19**; `KIMI_CODE_HOME` honoured and long
paths fine; the sign-in is file-based with a rotating refresh token, and a junctioned `credentials/` shares it (D19); an
unknown model is a bare `-32603`, `moonshot-ai/*` bills the person's key (D10); `session/load` keeps the refusals and does
not rebind its cwd (S5); `/usage` gives the input/output split (D15). The shim is no longer "infrastructure until M-K4, M-K9
and M-K10 pass through it" (risk consultation `ba5f661a`) — they did, and the sub-agent's writes asked through the same
channel.

*The code half — re-planned 2026-10-13, and gated.* The plan's rule holds: no code below is written before the cell it
cites is recorded. **Cells before code, in this order** (all in `scripts/probe-kimi-confinement.mjs`, 3 of 3, Windows, through
the shipped shim; §5 defines each): **M-K11** (what the owned home must hold; its inventory) comes first — nothing of
`KimiHome` is coded before it; **M-K9 (e)** (the exclusion list is complete) before the snapshot's and the shim's lists are
called complete; **M-K7b** (one answered turn per curated id) before an id joins the list; **M-K12** (`~/.agents/*`) before
the `runtime-capabilities.json` rows and the help are written; **M-K14** (the refresh race) runs LATER — the owner accepted
the serialised rows on 2026-10-13; its absence costs wall time, not correctness — and lifts the cap when it is recorded;
**M-K13** (the auto-update pin) is informational and gates nothing. A new Kimi version launches with the version warning;
re-running the whole script on it is what makes it a measured one (owner, 2026-10-13: warn and run, never refuse).
- `KimiHome` + `KimiLaunch` (D19), the Vibe shapes with the inversions D19 names: the home made in the round's scratch (or the
  conversation's folder) with its `credentials` junction, the person's folder located and checked first (exists, not a
  link, local), the junction read back; the validation before every launch (the junction's target, no other link, no
  `mcp.json`, an empty `workspace-trust/`, no `config.toml` but coai's own, no `tui.toml`/`skills/`/`agents/`/`AGENTS.md`/
  `hooks*`, the rest by M-K11 (c)'s names — and a hash manifest ONLY if M-K11 (c) finds configuration or code written);
  the home's turn; the host (D18); the model pre-check against the curated `kimi-code/*` list (D10); `kimi --version` in the
  built environment on the owned home against `MeasuredVersions` — a version outside it launches, and the warning rides on
  the launch's result to the reviewer line and the card, never a refusal (owner, 2026-10-13); the built environment
  (`Minimal` + `KIMI_CODE_HOME` + `KIMI_CODE_NO_AUTO_UPDATE=1`) with `EnvironmentRefusal` on the shim's request. The `providers` probe's
  `--version` runs the same way on a throwaway home, never in the inherited environment against the person's home (the
  Vibe precedent, `VibeRuntime.cs:99-111`).
- The disposal (D19): the junction unlinked first by its own name and verified, then the home; `DeleteEvenIfReadOnly`
  made reparse-point-safe for every caller (the 6-hour sweep, the snapshot root, the conversation sweep) — the red-first
  junction test of §5 BEFORE the first home is ever made on a developer's machine, because the first bug here deletes a
  sign-in.
- The shim (`coai-mcp --ask-acp`, D15, D19): `--home <path>` and the agent's environment REBUILT from it through
  `KimiLaunch.Built` (never two hops of inheritance); after `session/new`, the asked model checked against the announced
  options (exit 65 with the list, D10); after an `end_turn`, `/usage` as a second prompt and the one line parsed into
  `tokensIn`/`tokensOut` (`notCaptured` when it does not parse, nothing sent after a non-answer); exit codes of its own
  (never 64), named in `.agents/PROJECT.md`'s one-shot list. `.agents/agents` added to `KimiAcpProfile.Exclusions`
  (`KimiAcpProfile.cs:34`) and to `SnapshotExclusions.Default` (`SnapshotOptions.cs:59`).
- Serialisation (D19, until M-K14): `LaunchesOneAtATime` on `IReviewerRuntime` (default false), read by the scheduler's
  per-provider limiter (`BoundedScheduler.cs:91`); the cross-process named mutex in `KimiLaunch.TakeAsync`. A test that two
  Kimi launches of one round never overlap, and that a Vibe row's cap is untouched.
- **Before the shim launches Kimi on any host but Windows** (the code gate of epic 4, 2026-10-12, codex): an agent that
  exits before the teardown loses its children on Linux and macOS — they are reparented and the tree kill finds none
  (`research/module_runners.md`, the ACP teardown's known limit). The transport must own the tree independently of the
  agent — a Unix process group (or session) killed as a group after the leader is gone — with a test that the grandchild
  of an agent that crashed first is gone. Windows keeps the exited parent's handle and finds the children; a Job Object
  there is the stronger form. Today `KimiAcpProfile` names an install only for Windows, so nothing launches Kimi
  elsewhere yet — and D18/D19 add the symlink-into-`0700` question to the Linux list.
- `KimiRuntime` classifies the shim's outcome from its STRUCTURED report (`end`, `failure`, the exit code), never by
  scanning its stdout text through `RateLimit` — a read trace naming `quota.cs` would otherwise turn a sign-in refusal
  (77) into a retryable rate limit (risk consultation `ba5f661a`); and the report's `notCaptured` usage survives a failure
  (the executor's rate-limit branch drops usage today). Tests through the executor AND the scheduler: a spent balance, a
  clean sign-in refusal, and a sign-in refusal whose trace carries quota text — each launched once, usage kept unknown.
- `KimiRuntime` (the shim's `ProcessRequest` with `--home`, `--cwd` the round's snapshot, `--model` the checked id; the
  snapshot capability; `LaunchesOneAtATime`; `TakeTurnAsync` → `KimiLaunch.TakeAsync`; answer and usage from the shim's
  report, cost `null`, D9), registered in `Default`, `RuntimeNames`, `Named`, and in `MachineOnlyRuntimes` (D12).
- Kimi's sessions need no retirement of their own any more (§7, rewritten): they live in the owned home (M-K8, 6 of 6) and
  go with it — the round's scratch, the conversation's folder, the 6-hour sweep after a crash. The ownership record and
  the `wd_*` reconciliation of the 2026-10-12 draft are dropped, and the deviation is recorded at promotion. What M-K11 (b)
  must still show is that an owned-home launch leaves the person's `~/.kimi-code` byte-identical but for `credentials/`.
- `VendorDiagnosis`: Kimi's install cure per OS (Moonshot's own installer, D16) and the failure sentences M-K7 captured —
  the subscription-model `401` of D15, `Authentication required` (exit 77), the billing `429` of D14 — plus coai's own
  pre-launch sentences: not signed in (no `credentials/` to junction), a junction that could not be made or reads back
  wrong, an id outside `kimi-code/` — and the version WARNING (a sentence on the result, not a refusal) for a Kimi outside
  `MeasuredVersions`, which the extension shows on the row's card (owner, 2026-10-13).
- Seeds: `feature-availability.json` rows for `kimi` (`effort` — `thinking` has `low`/`high`/`max` on the k3 models (M-K7)
  but nothing measured it taking effect, so `unmeasured`; `thinking` `unmeasured` for the same reason; `fastMode` `none`),
  regenerate.
- Extension: `RUNTIMES`, both `package.json` enums, `modelsFor` + `modelsProvenance` (the M-K7 list, D10), the preset
  (D11), `vendorTerminal` maps + `kimiInstall` + `OFFICIAL_SOURCES` (D16), `JUDGED_HERE`, both `CLI_RUNTIMES`,
  `KNOWS_ITS_OWN_ENDPOINT`, a colour slot, the `crossesTo` gate (D13). The ▶ terminal runs plain `kimi` (interactive), never
  `acp`. The row's card shows D19's version warning whenever a launch's result carries one (owner, 2026-10-13).
- Docs: the S2 set, for Kimi — `module_runners.md` (the ACP shim), `module_extension.md`, `architecture.md`, README, help in
  five languages, `vendor-routing.md`, CHANGELOG, and `.agents/PROJECT.md`.

**S5 — Kimi as a consultant and a question-consultant row.** Decisions D1, D6 (one stable snapshot path per conversation),
D15, D19 (one home per conversation, kept across its turns; the junction's target checked before every turn). Cells:
builds on M-K2 (the session id), M-K5 and M-K9 — recorded by S4, 2026-10-13 — and on **M-K5b** (whether `/usage`'s session
total is cumulative across a loaded session) and **M-K12** (the `~/.agents/*` standing), which S4's gate runs. Depends on
S4; not on S3. Complete on its own: a Kimi row answers `consult` and a question-consultant row, and a resumed turn keeps
every refusal.
- `KimiConsultant` over the same shim, with a `--session <id>` load path: `session/load {sessionId, cwd, mcpServers: []}`
  then the model set again, then the prompt. **M-K5 decided the shape (RESULTS_kimi §8, 3 of 3):** a loaded session keeps
  the refusals and the client-fs routing, and resolves relative paths against the folder it was CREATED in — its `cwd` on
  load does not rebind (Vibe did the same, RESULTS_vibe §22 B, E). So the conversation keeps ONE snapshot path, refreshed
  in place before each turn (D6), and `session/load` is sent with that same path; memory is the vendor's (`session/load`),
  never a transcript coai carries. Usage per turn: the session total's DELTA from the previous turn's recorded total if
  M-K5b shows it cumulative, else the line as read; `UsageIsCumulative` from that cell. Added to `features.consultant` and
  `OnACli`.
- The conversation's owned home (D19) under `LocalApplicationData/coai-kimi/`: made with its junction at the first turn,
  validated before every turn (the junction's target still the person's folder — a sign-out and sign-in between turns
  replaces the files, not the folder, and is Kimi's to notice), deleted — junction first — when the conversation closes or
  lapses, and by D20's home sweep on the beat and at server start when its conversation is closed, unknown to the store or
  idle past `ConsultIdle` (§7). The conversation's turns on one home are serialised; its launches share the per-credentials
  gate with the reviewers (D19) until M-K14.
- `consultant-limitations.json` rows per side.
- Question consultant: `RuntimeCapabilities.Runtimes`, a `ConfinementPlanner` arm for `kimi` (snapshot for `disk`, empty
  folder for `none`; `Read` bounded by the shim, `Grep`/`Glob` not — so `disk` is written `unconfined`, D15; the `~/.agents/*`
  standing from M-K12 in the same row), and the three `runtime-capabilities.json` rows — `none` / `disk` / `web` each from
  its own measured cells; `unmeasured` where no cell says, and the planner refuses that capability. **A question row is
  one-shot**, so its home — junction and all — and its snapshot live inside the row's own `coai-question-*` scratch folder
  (`QuestionRowLaunch.cs:68`, removed in `finally`), gone — junction first — on success, refusal and cancellation alike; the
  S3 rule, with D19's disposal order.
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
- **The Kimi cells recorded 2026-10-13** (RESULTS_kimi_confinement.md §4–§13, each 3 of 3 through the shipped shim — M-K5's
  second turn and M-K7's model list through the probe's mirroring small client, §3):
  **M-K1** `-p -` is a literal dash; prompt mode cannot take stdin (D15). **M-K2** the shim's report; `/usage` as a second
  prompt gives `Session total: N input, M output` (D15). **M-K3** write arms through the shim — 0 of 9 wrote, controls 3 of
  3. **M-K4** nothing writes without a permission request: a background shell and a sub-agent's `Write`/`Bash` both asked;
  27 tools announced, the unexercised ones named (D15). **M-K5** `session/load` keeps the refusals and the routing; its
  `cwd` does not rebind (S5). **M-K6** `Read` bounded, `Grep`/`Glob` unbounded (D15). **M-K7** empty home → exit 77; `--model
  nope` → `-32603`, exit 70, no sentence; the highspeed `401` through the shim; the six ids, two of them `moonshot-ai/*` on
  the person's API key (D10). **M-K8** `KIMI_CODE_HOME` honoured, a 280-character session path written and read back;
  the sign-in is file-based, the refresh token rotates on every refresh (D19). **M-K8b** an owned home with `credentials/`
  junctioned to the person's: answered, a refresh went through the junction, the person's home kept working (D19).
  **M-K9 (a)** the branch's `AGENTS.md`, `.kimi-code/AGENTS.md`, skills and sub-agent files load from disk untrusted with no
  client call; a project `mcp.json` starts only trusted (D6, D15, D8b); **(d)** a user `[[hooks]]` fires through the shim
  (D19). **M-K10** a user `mcp.json` server starts under `mcpServers: []` (D8b → D19).
- **The cells D19 adds (2026-10-13), each in `scripts/probe-kimi-confinement.mjs`, 3 of 3 on Windows through the shipped shim
  unless the row says why not — and the code each one gates (§4 S4):**
  **M-K11 (a) the owned home's contents** — gates `KimiHome`: starting from a home holding ONLY the credentials junction, is
  the launch signed in (`Reply with exactly: OK`), and do `Grep`/`Glob` work? Add one candidate at a time only when the
  previous arm fails, in this order, and record each arm's own result: a coai-written minimal `config.toml` naming
  `managed:kimi-code` and nothing else; `device_id` (fresh per home first — does a fresh id fail the refresh, or register a
  device on the person's account?); `region` and `migrations-effort.json`; `bin/rg.exe`/`bin/fd.exe` (without them, does
  `Grep` fail, use the PATH, or download — and into which home). **(b) the person's home is untouched** — gates §7's
  rewrite: every file of `~/.kimi-code` hashed before and after an owned-home launch; only `credentials/` (the refresh)
  may differ, 3 of 3. **(c) the inventory** — gates the validation's admitted names and the manifest question: what an
  owned home holds after `kimi --version`, after one turn, and after a second start; which of those files Kimi later READS
  as configuration or code (a skill, an agent, a hook, a plugin — the Vibe §24 question) against which are data (sessions,
  caches, logs); whether any file's bytes change between starts. A configuration-or-code file → `shared/kimi-home-manifest.json`
  and the Vibe step's twin; none → admission by name.
  **M-K12 `~/.agents/*`** — gates the `runtime-capabilities.json` Kimi rows and the help: (a) a canary skill in the real
  `~/.agents/skills/<unique>/` and a canary line in `~/.agents/AGENTS.md` (the owner allowed that write on 2026-10-13; the
  probe still takes `--allow-agents-canary` so it never happens by accident; removed in a `finally`, verified absent) —
  are they in the session's wire under an owned `KIMI_CODE_HOME`? (b) the same with
  `USERPROFILE`/`HOME` redirected to an empty folder in the built environment — do they stop loading, and does the launch
  still sign in and answer (the `kimi.exe` path and the junction target are absolute)? Clean 3 of 3 → the built environment
  takes the redirect; else the stated limit.
  **M-K13 the auto-update pin** — informational, gates nothing (owner, 2026-10-13: the version is warn-and-run): with
  `KIMI_CODE_NO_AUTO_UPDATE=1` in the built environment and no `tui.toml` in the owned home, across three launches:
  `updates/` absent in the owned home, the person's `updates/` and `bin/kimi.exe` hashes unchanged, `kimi --version` the
  same before and after. A control run without the variable, if `updates/` appears with it: the variable does nothing and
  the version warning is the only net.
  **M-K14 the refresh race** — gates only the LIFTING of D19's serialisation; runs LATER (owner, 2026-10-13: the serialised
  rows are accepted until then): with the person's access token
  past its 900 s (the probe waits; it reads the credential file's expiry, never holds a token — the guard's existing rule,
  §11), three shims on three owned homes junctioned to the ONE credentials folder, started within the same second: all three
  answered? any `Authentication required`? how many rotations did the guard see? and the person's own `kimi -p` afterwards
  — answered 3 of 3? Then the person-race arm: a `kimi -p` control started in the same second as one shim. A server that
  tolerates the race (every launch answered, the person signed in after) lifts the cap; one that revokes on reuse keeps it
  and the help names the interactive race.
  **M-K7b one answered turn per curated id** — gates each id's place on the list: `kimi-code/k3` and `kimi-code/kimi-for-coding`,
  `Reply with exactly: OK`, the answer and the session's `current` model recorded; `kimi-code/kimi-for-coding-highspeed`
  re-run once to keep its `401` row current. Two paid turns.
  **M-K9 (e) the exclusion list is complete** — gates calling it complete: the M-K9 (a) kit plus `.agents/agents/<name>.md` (a
  sub-agent with a canary word) and every other project-level path Kimi's documentation names for instructions, skills,
  sub-agents, hooks or MCP, each with a canary; the arm's row lists the documented paths it tried. Any path that loads and is
  not in `SnapshotExclusions.Default` joins it (and the shim's list) in the same change.
  **M-K5b `/usage` across a loaded session** — gates S5's usage arithmetic: turn 1 through the shim, `/usage`; turn 2 by
  `session/load` on the same cwd, `/usage` again — is `Session total` cumulative (turn 2's numbers include turn 1's) or
  per turn? Three runs; the small client's load path, as M-K5 used (the shim's `--session` is S5's to add).

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
  Version (owner, 2026-10-13: warn and run): a `vibe --version` with NO manifest entry LAUNCHES — the shape check still
  refuses a `hooks.toml`, a link and a trust entry on that home, the pinned-prefix files are admitted by name, and the launch
  turn carries the SHARED version warning (the running version, the measured one(s), that the numbers and the plugin hashes
  are the measured version's, the advised `vibe-home-manifest.mjs --write` and probe) to the reviewer line (C#) and the row's
  card (TS); a known version with one changed plugin byte → refused with the path; a known version with its bytes → no
  warning. The warning's shape: Vibe's and Kimi's are ONE record type with the same fields from one builder — a test
  constructs both and compares their shape, so a second shape cannot appear.
- Owned Kimi home (D19, `KimiHomeTests` / `KimiLaunchControlTests`, against a FAKE person's home in a temp folder — never the
  developer's `~/.kimi-code`): the person's `credentials/` absent → refused *not signed in* before any process; the person's
  `credentials/` itself a link → refused; made: `credentials` is a reparse point whose read-back target is the fake folder,
  nothing was copied, and the home holds nothing else (or exactly coai's `config.toml` template, per M-K11 (a)); the
  junction's target swapped for another folder → refused with the path; a second link anywhere, an `mcp.json`, a
  `workspace-trust/<entry>`, a `tui.toml`, a `skills/`, `agents/`, `AGENTS.md` or `hooks*`, a `config.toml` that is not
  coai's bytes, a name outside M-K11 (c)'s inventory → each a refusal naming the path and the cure; the inventory itself →
  go; two turns on one home serialised. The child environment on BOTH hops — the shim's request from `KimiRuntime`, the
  agent's request inside `AskAcpMode` from `--home` — holds exactly the platform's minimal set plus `KIMI_CODE_HOME` and
  `KIMI_CODE_NO_AUTO_UPDATE=1`, and a `KIMI_CODE_HOME` set in the parent never reaches the agent by inheritance (a test sets
  one in the parent and inspects the agent's request). Linux/WSL child → refused (D18). Model: an id outside `kimi-code/`
  refused with the billing sentence; one on the list but not among the session's announced options → the shim exits 65
  with the list (fake agent); `--model nope` never reaches `set_config_option`. Version (owner, 2026-10-13: warn and run):
  a `kimi --version` outside `MeasuredVersions` LAUNCHES, and the launch's result carries the warning naming the running
  version, the measured one(s), and that every confinement number is the measured version's; the warning reaches the
  reviewer line through the executor (C#), and a TS test shows it on the row's card; a measured version carries no
  warning; the probe re-run is advice in the sentence, never a refusal. Usage: `/usage` sent only after `end_turn`; the
  `Session total` line parsed; an unparseable answer → `notCaptured`, tokens 0, never a number. Serialisation: two Kimi
  launches in one round never overlap (the scheduler's per-provider cap at 1 for `LaunchesOneAtATime`); two servers'
  `KimiLaunch.TakeAsync` on one credentials target wait on each other (the named mutex); a Vibe row's cap unchanged.
- **The junction disposal (D19) — red first, before any owned home is made on a developer's machine:** a scratch tree
  holding `credentials` as a junction (a symlink on Unix) to a folder with a read-only file. After the home's disposal, after
  `GitScratch.DeleteEvenIfReadOnly` on the tree, after `PruneOldScratchDirs` over its parent, after the snapshot's
  `TryDeleteRoot`, and after each TEST sweep (`TempDirsAreSwept`, `run-tests.mjs`): the junction is gone, the tree is gone,
  the target folder still exists and its file keeps its bytes AND its read-only attribute. Expected red on the unfixed
  `DeleteEvenIfReadOnly`: the attribute cleared through the junction (`GitScratch.cs:21-27`), or the file deleted. A second
  test: the disposal of a home whose junction was already removed, and of one whose `credentials` is a real folder (a
  tampered home), each ends with the home gone and nothing outside it touched.
- **Consultant home sweep (D20)**, against fake `coai-vibe/` and `coai-kimi/` roots and a fake record store in a temp
  folder: an orphaned `coai-kimi/<id>` with no record, written longer ago than `ConsultIdle`, whose `credentials` junction
  points at a folder holding a file → the home and the junction gone, the target folder and its file untouched; the same
  home written inside the window → kept; a live conversation's home (record open, written now) → kept; a closed, failed or
  lapsed record's home → gone on the beat; a home whose turn is held → skipped this beat, gone on the next once the turn is
  given back; the start sweep on a service built with a stale home present → gone; a Vibe home the same, no junction; an
  unreadable folder → logged, the rest of the pass still runs; the records are swept BEFORE the homes on one beat (an open
  record idle past the window is lapsed, and its home goes by rule (b)).
- `crossesTo`: a `vibe` / `kimi` row is not sent to a server older than its constant; a test that a host whose `RUNTIMES`
  lacks `vibe` does not overwrite a `vibe` row in the server's settings file (D13). **And one live compatibility check**
  (plan gate, codex, 2026-10-12): the extension's REAL send path (the settings mirror the server reads) against the last
  RELEASED `coai-mcp` binary, asserting the rows are withheld and the older server serves its other rows unchanged. The
  Team server is not the counterpart here: these rows never reach it (D12 — machine-only; a Team-server row carries the
  runtime `remote`).
- Each launch path (reviewer, `consult`, question row) carries the model variable in the request's explicit environment,
  goes through the one MCP launch control (D8b — `VibeLaunch` for Vibe, `KimiLaunch` for Kimi, each on every path), and
  starts in a sanitised snapshot (or an empty folder) — never the checkout (D6); an adapter that does not ask for a snapshot
  keeps today's directory, byte for byte.
- `ReviewSnapshotTests` (D6): `.vibe/`, `.agents/skills/`, `.agents/agents/`, `.kimi-code/`, `AGENTS.md`/`agents.md` and
  `.git` absent at every depth (case-folded on
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

- **Installing is the product's job, not the owner's** (owner, 2026-10-13: "the install buttons must work, per OS"). The
  extension's Install / Update button for `vibe` and `kimi` runs the vendor's official installer for the side it acts on
  (D16) — Windows, Linux/WSL and macOS — so a missing CLI in WSL is cured by that button, never by a hand install. What stays
  the owner's is the SIGN-IN, which only a person can do: the button opens the vendor's own terminal for it afterwards.
- **Kimi sign-in** — done on Windows 2026-10-12 (`kimi login`, managed `kimi-code` OAuth); WSL needs it once `kimi` is
  installed there by the button, for S4's WSL cells.
- **The owner's answers to D19's three questions, 2026-10-13** — nothing of D19 is open to the owner any more: (1) the
  version is WARN AND RUN — a Kimi outside `MeasuredVersions` launches with the visible warning (D19), never a refusal; the
  probe re-run is advised in the sentence, not required; (2) M-K12 MAY write its canary into the owner's real `~/.agents/`
  — removed in a `finally`, verified absent, and still behind the probe's explicit `--allow-agents-canary` so it never
  happens by accident; (3) M-K14 runs LATER, and a Kimi row's launches stay serialised until it is recorded (D19) —
  accepted. What M-K14 will need when it runs: the access token expired before each of its three runs (the probe waits out
  the 900 s itself, ~45 minutes of wall time with the owner's own `kimi` idle) and, for the person-race arm, a `kimi -p`
  started on cue.
- **Vibe in WSL.** Only the Windows side is installed and signed in; until `vibe` is installed (by the button) and signed in
  there and the probe has run, D18 refuses Vibe on a Linux/WSL child.
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
- **Owned homes** (D17): ~85 KB of Vibe's own unpacked plugins per home plus its sessions (~130 KB for two short turns —
  call a conversation's home ~300 KB). A round's home lives in the round's `coai-answers-*` directory and goes with it
  (and with the 6-hour sweep after a crash); a conversation's home under `LocalApplicationData/coai-vibe/` is deleted when
  the conversation closes or lapses, by the consultation sweep that already closes it (`ConsultationSweeper`), with a test
  that a closed conversation's home is gone — **and by D20's home sweep** on the same beat and at server start, for a home
  whose conversation was closed while the host was down, is unknown to the store, or sat idle past `ConsultIdle`. So while a
  server runs a home outlives its conversation by at most `ConsultIdle` + one beat (15 min + 60 s by default), and a killed
  host's homes go at the next start. **Projected:** resident at once = the open conversations' homes plus those of the last
  ~16 minutes — a handful, a few MB; passing through at 50 Vibe conversations a day ≈ 50 × 300 KB = 15 MB a day, none of it
  kept past the window. A home is removed with its snapshot (next bullet), so the snapshot's size, not the home's, is the
  number that matters there.
- **Snapshots** (D6): one per code round per runtime-that-asks (≈ the tree's size — 38.8 MB for this repository), inside
  the round's `coai-answers-*` scratch directory and gone with the round; and one per `consult` conversation, refreshed in
  place, deleted with the conversation's home when it closes or lapses. Both are covered by the 6-hour sweep after a crash. At 50 Vibe launches a day on a repository this size, worst case
  ≈ 2 GB of copying a day. On disk at once: one snapshot per concurrently running round or turn — a few hundred MB at
  the handful of rounds this machine runs together — and never more than D6's aggregate cap (2 GB by default), which
  refuses a new snapshot before the temp volume runs low (free-space floor, 5 GB by default).
- **Kimi's session store and owned homes** (rewritten 2026-10-13, D19). Measured: **~91–112 KB per session** (`wd_*` folder;
  55 of them, ~6 MB, from the probe's 74 launches, RESULTS_kimi §14), and under `KIMI_CODE_HOME` the session lands in the
  OWNED home — the person's `session_index.jsonl` unchanged, 6 of 6 (§11). So the 2026-10-12 draft's ownership record
  (`kimi-sessions.jsonl`, the pending/resolved lines, the `wd_*` reconciliation — plan gate and code round, codex) is
  DROPPED: a session has an owner because its home has one. A reviewer's home (`kh-*`, junction and sessions) lives in the
  round's `coai-answers-*` directory and goes with it — and with the 6-hour sweep after a crash; a conversation's home
  under `LocalApplicationData/coai-kimi/` is deleted when the conversation closes or lapses, by the consultation sweep
  that already closes it (`ConsultationSweeper`), with a test that a closed conversation's home is gone; a question row's
  lives in its `coai-question-*` folder, gone in `finally`. **Every one of those deletions is junction-first** (D19): the
  `credentials` link removed by its own name and verified before anything recursive runs, and the recursive deleter itself
  never entering a reparse point — the one test of §5 that must be green before the first home exists. What stays
  unmeasured until M-K11 (b): that an owned-home launch leaves the person's `~/.kimi-code` byte-identical but for
  `credentials/` — Kimi's `workspaces.json`, `user-history/` and `updates/` are home-relative in the probe's hashing (§3),
  not yet shown home-relative under `KIMI_CODE_HOME` arm by arm. At 50 Kimi launches a day: ~5 MB of sessions a day inside
  homes that are gone within the day. **A conversation's `coai-kimi/<id>` home is also D20's:** swept junction-first on the
  beat and at start when its conversation is closed, unknown to the store, or idle past `ConsultIdle` — so, as for Vibe, a
  home outlives its conversation by at most 15 min + 60 s while a server runs, a killed host's homes go at the next start,
  and the junction (0 bytes, the person's folder by reference) never outlives the home. Projected: resident = the open
  conversations' homes plus the last ~16 minutes' (~100 KB each — under a MB); passing through at 50 Kimi conversations a
  day ≈ 5 MB. The person's `~/.kimi-code` gains nothing from coai but its own token refreshes.
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
      child is refused (D18); the model is checked before and after (D10); a Vibe with no manifest entry launches WITH the
      shared version warning on the result and the card, its home's shape still validated, while a known version's tampered
      plugin bytes still refuse (owner, 2026-10-13).
- [ ] The version warning is ONE shape for Vibe and Kimi, built by one builder and carried on the launch turn to the reviewer
      line and the card — pinned by a test that constructs both.
- [ ] A persistent consultant home (`coai-vibe/<id>`, `coai-kimi/<id>`) never outlives its conversation by more than
      `ConsultIdle` plus one beat while a server runs, and a killed host's leftovers go at the next start — junction-first,
      the target untouched (D20); pinned by the sweep tests of §5, and the projection is in §7.
- [ ] A billing refusal is not retried.
- [ ] Every Kimi launch runs on a coai-owned, validated `KIMI_CODE_HOME` whose `credentials/` is a junction to the person's
      folder, in a built environment, through one launch control on every path (D19); the person's `credentials/` is never
      copied, and every disposal and sweep removes the junction by its own name first — pinned by the red-first junction
      test of §5; the person's `~/.kimi-code` is byte-identical after a launch but for its own token refresh (M-K11 (b)).
- [ ] D19's cells are recorded before the code they gate (M-K11, M-K9 (e), M-K7b, M-K12, M-K5b; M-K14 before the
      serialisation is lifted; M-K13 informational), `MeasuredVersions` names only versions the whole probe has run on, and
      a Kimi outside it launches WITH the visible warning on the result and the card — never a refusal (owner, 2026-10-13).
- [ ] The Kimi model id is checked before the launch (the curated `kimi-code/*` list) and inside the shim (the announced
      options); an id outside `kimi-code/` never reaches Kimi (D10).
- [ ] Both runtimes are machine-only; an older server never receives the rows.
- [ ] Seeds regenerated; every hard-coded runtime list in tests updated; both suites green.
- [ ] Docs, help in five languages, `vendor-routing.md`, READMEs and CHANGELOGs updated.
- [ ] This plan promoted to `research/` with its deviations.
