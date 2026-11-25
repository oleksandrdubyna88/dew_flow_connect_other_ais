# The codex and agy coder write boundary — security judgement (2026-11-25, epoch 3 E3 s4 t6)

Judge: Fable (security judgement role), read-only for product code, no model call, **no run of `codex` or `agy`** (not even `--version`).
Head `269a64d8` on `feat/v2-ep3-e3-roles`. Read: `SPEC.md` (C1 to C6, section J), `RESULTS_codex_coder.md`, `RESULTS_agy_coder.md`,
`RESULTS_model_lists.md`, the claude files (`RESULTS_coder_write_boundary.md`, `RESULTS_coder_write_boundary_judgement.md`), the three key-pool
judgements of E2 s7 (`origin/feat/v2-ep3-e2-coder`), and `src_mcp/runners/Toolchain/{CoderUnit,BoundaryUnit,BoundaryJob,BoundaryPreflight,CheckBoundary,CoderBoundary,CoderProgram}.cs`,
`src_mcp/runners/Reviewers/AntigravityReadOnly.cs`.

Run by the judge: 11 `systemd-run --user` probes on scratch folders with made-up files and two local listeners (section 2). Nothing else.
An earlier draft of this judgement was lost in a WSL restart; this file was written again from the sources, and each claim of the draft that it
keeps was checked again (section 2 says how).

## 1. Verdicts

| Vendor | Measured release | Verdict | The release enters `shared/coder-boundary-releases.json` |
|---|---|---|---|
| codex | `codex-cli 0.171.0` | **ship** | only with the addendum that follows the measure of s5 t6 (two tool paths were not tried: section 5, X1) |
| agy | `agy 1.5.17` | **ship — by the owner's decision, with the must-change list of section 6 and the accepted risks of section 7** | only with the addendum that follows the measure of s6 t7 AND the owner's word on section 7 |

Both stories stay: the row "ship / ship" of SPEC C5 applies. `validate_work` accepts the folder (section 10).

**What the evidence alone says about agy, and what the owner's decision changes.** By the rule of SPEC C1 and by d2 of this task ("a property that
was asked for and not observed counts as not held") agy 1.5.17 is NOT shippable on the evidence in hand: B13 was not measured on a resumed turn;
the unit does not hide the sign-in from a process inside it; there is no network limit; B6, B11 and B12 rest on agy's own agent file and hook,
and the hook is OPEN on "exit 0, no output". The owner decided that agy IS allowed as a coder (the decision reached this judge through the
orchestrator's brief, which dates it 2026-11-26; the judge did not see the owner's message). So this file does not refuse. It makes the agy
coder as defensible as it can be made, says which mitigations are not proved to be buildable, and names what is left as accepted risks **for
the owner**. Nothing is weakened in silence: until s6 t7 has seen B13 hold on a resumed turn, the table holds no agy release, and the product
refuses agy by data.

## 2. What the judge checked itself

| # | Claim | How | Answer |
|---|---|---|---|
| J1 | A check unit binds the whole job folder writable, and the coder folder is inside it | read `BoundaryUnit.cs:44` (`BindPaths={job.JobRoot}`) and `BoundaryJob.cs:86` (`CoderFolder => JobRoot/coder`); probe: a unit with the property list of `BoundaryUnit.Properties`, host network | **true.** A command read `<job>/coder/home/.codex/auth.json` and `<job>/coder/home/.gemini/antigravity-cli/antigravity-oauth-token` (made-up files) and WROTE `<job>/coder/guard/.agents/hooks.json`. Interfaces inside: `eth0 lo` |
| J2 | The build step has the host network and runs the coder's code | `BoundaryJob.cs:31` (`NetworkChoice.DefaultBuild` = host) | true by reading |
| J3 | Which property hides the coder folder from a check unit | 4 probes | `InaccessiblePaths=<job>/coder`: hidden. `TemporaryFileSystem=<job>/coder:ro`: hidden (empty folder). Binding the check folders one by one and nothing else: **still readable** when the job folder is not under `/home` (`ProtectSystem=strict` makes it read-only, not hidden). `TemporaryFileSystem=<job>:ro` plus the check folders one by one: hidden, and the check's own HOME still writable |
| J4 | Every bind site | `grep` for `BindPaths=`, `BindReadOnlyPaths=`, `TemporaryFileSystem=`, `InaccessiblePaths=` over `src_mcp/src` and `src_mcp/runners` | two sites only: `BoundaryUnit.Properties` and `CoderUnit.Properties` |
| J5 | `IPAddressDeny=any` does nothing in a user unit | probe with a listener on the host's `127.0.0.1` (no outside host was contacted) | **accepted and not enforced** (the unit connected). `PrivateNetwork=yes`: enforced (only `lo`, the host listener not reachable). `RestrictAddressFamilies=AF_UNIX`: enforced. systemd 259.5, WSL2 |
| J6 | A unit with the host network shares the HOST loopback | the same probe with `PrivateNetwork=no` | true: the unit read the answer of the listener on `127.0.0.1` |
| J7 | `127.0.0.1:9` refuses a connection | connect from outside and from inside a host-network unit | refused both times; `ip_unprivileged_port_start` is 1024, so only root can listen there |
| J8 | A unit with no network can still reach a program outside through a unix socket in a bound folder | probe with `PrivateNetwork=yes` | true (the outside listener answered) |
| J9 | Two codex tool paths were not measured | `RESULTS_codex_coder.md` section 4 (each step used "the apply_patch tool or a shell command") and U3 (`functions.exec` is a JavaScript tool; 6 `collaboration.*` tools no flag removed) | true by reading: no row tried the JavaScript itself or a command of a `spawn_agent` child |
| J10 | The refresh rule of codex | `RESULTS_codex_coder.md` 0.2, 0.4 | the 5-minute window was read from source at `rust-v0.160.0`; for 0.171.0 only the names in the binary were seen. The override was seen to take 4 of 4 refresh POSTs on 0.171.0. What the CLI does when the override address REFUSES the connection was not measured |

J1 with J2 is a new finding: section 4.

## 3. Rows B1 to B15 — which layer holds each

Layers. codex: **V** the vendor sandbox with the permission profile `coai` in the job's `config.toml`; **F** the flag set CX1; **U** the unit;
**C** coai's own check. agy: **G** the guard hook; **A** the agent file's tool list; **M** `--mode accept-edits`; **U**; **C**.
"One layer" means: when that layer fails, nothing else stops it.

| Row | codex 0.171.0 | agy 1.5.17 |
|---|---|---|
| B1 edit in the worktree | V + U | G + U |
| B2 write to the main checkout | V, U (main hidden) | G, U (main hidden) |
| B3 beside the worktree, another job's worktree | V, U (coder root is a tmpfs) | G, U |
| B4 `.git` of the worktree and of main | V, U (read-only binds) | G, U |
| B5 the real home | U, V | G, U |
| B6 read outside the worktree | V, and U for main, the home and other worktrees. For the job's own folder: **V, one layer** | G, and U for main, the home and other worktrees. For the job's own folder (sign-in, guard): **G, one layer** |
| B7 list or search outside | V, U | A (no such tool), G |
| B8 write through a link out | V, U. A folder link was tried only by the model-free probe | G (resolves links), U |
| B9 no command leaves the limit | U, V (own PID namespace) | A + G + M: **there is no command at all**; U for what a command could reach if one ran |
| B10 no network tool | F (the `--disable` set, `web_search="disabled"`) for tools; **V, one layer**, for a command. The CLI process: the whole network | A + G. The CLI process: the whole network |
| B11 the sign-in and the launch's secret | **V, one layer** (with `shell_environment_policy.inherit="core"`); the unit alone does NOT hide the job's `auth.json` | **G + A**; the unit does NOT hide the sign-in file, and agy needs it |
| B12 the tools are the measured list | F; checked with no model (`codex debug prompt-input`). 6 `collaboration.*` tools stay | A; **the model's own answer only**. G (deny all but three names) is what enforces it |
| B13 repository files run no code | F (no `--trust`, `--disable hooks`), V (`.codex`, `.agents` protected); fresh and resumed | agy loads hooks and agents from the cwd only, and the cwd is never the worktree (C). **Fresh only: the resumed turn was not measured** |
| B14 a resumed turn has the same limit | F + `config.toml` on every turn; the handle is coai's (C) | G from the cwd, A from the conversation, the handle and the id compare are coai's (C) |
| B15 only the allow-list environment | U (`env -i`) | U |

## 4. A new finding: the checks can read and write the coder's folder (all vendors)

Measured (J1 to J3). The check unit binds the whole job folder writable. The coder's folder is inside it: the coder's home with the sign-in COPY
of codex or agy, the CLI's session files, and — for agy — the guard folder with `hooks.json`, the agent file and the hook script. The build step
has the host network and runs the code the coder wrote. So:

1. A build step can read the sign-in copy and send it anywhere. For agy that copy holds the Google refresh token, which is not rotated.
2. A check can rewrite the agy guard (`hooks.json`, the agent file) or plant files in the home of any vendor's CLI before the next fix turn.
   "Written and read back before every launch" (s6 t2 d2) repairs the guard files; it does not repair a planted file nobody lists.
3. The same holds for the claude coder of E2 today: a check can write `<job>/coder/home/.claude/`. The claude sign-in is a read-only bind made
   only in the coder's unit, so a check sees an empty file there — but whether claude 2.1.330 with `--restricted` runs a hook from a
   `settings.json` that a check planted in the coder's HOME was **not measured**. The fix below closes it for every vendor at the one place;
   the lead should carry this line to E2 as a follow-up.

Method of the sweep: J4 (every bind site, by `grep` of the four property names). No third site exists.

Must-change (S1, section 5): no check unit sees the coder folder. The job folder becomes an empty read-only tmpfs in a check unit and the check's
own folders (`BoundaryJob.Folders`) are bound one by one — a folder made later is hidden unless someone names it. The preflight of the CHECK unit
gets a row for it, in both network states.

## 5. Must-change — shared, and codex

Each item names the task file that now carries it.

**Shared (story s7, built whatever the verdicts are).**

- **S1. No check unit sees the coder folder** (section 4). `BoundaryUnit.Properties`; rows K1 and K2. → s7 t5.
- **S2. No unit sees another job's folder.** The default jobs folder is under the home, which every unit hides. `COAI_CODER_JOBS` can move it
  outside the home; then every job's folder is readable from every unit (`ProtectSystem=strict` hides nothing). Hide the jobs folder in the check
  unit and in the coder unit before the job's own folders are bound. → s7 t5.
- **S3. A sign-in reaches a codex or agy coder unit only as a COPY that coai makes for the job.** Never a bind of the real file (the CLI may
  rewrite it; a codex refresh on a read-only bind would rotate the token and lose the new one). The copy is 0600 in a 0700 folder, is deleted
  when the job ends (and by the E2 sweep after a kill), and the real file's bytes and modification time are compared before and after the job
  and written into the envelope. claude does not change. → s7 t6.
- **S4. The commit search also looks for the tokens of the sign-in copy.** The search of E2 s7 (staged blobs and names, before and after
  `git add`, fail closed) gets the token texts of the job's copy as they are after the turn (agy rewrites its file). A hit ends the job
  `coder-boundary` and nothing is committed. This is the second layer for B11 where the first is one layer. → s7 t6.
- **S5. The table says where each id is asked.** Section 8 defines four classes (`pre`, `launch`, `turn`, `release`). The table holds the class
  of each id and, per measured release, the sha256 of the measured program. Tests: the `pre` ids and the preflight rows are equal; every
  `launch` and `turn` id names its check; every `release` id has a results heading for each release. → s7 t1.
- **S6. The seam carries the unit's hide set, the working directory, the environment allow-list and the sign-in recipe per vendor.**
  `CoderBoundary.RunAsync` forces the worktree as the working directory (`CoderBoundary.cs:151`) and `CoderUnit.Properties` binds the main checkout
  and the coder root read-only (`CoderUnit.cs:47-48`); codex and agy need them HIDDEN, and agy needs another working directory. The claude unit
  stays byte for byte. → s7 t1.
- **S7. The seam carries the check of a turn's own stream** (section 8, class `turn`). A wrong answer ends the job `coder-boundary`, never green;
  a lost session ends the attempt as a crash. → s7 t2.

**codex.**

- **X1. Measure before the argv is frozen** (s5 t6, before any code of s5; 8 paid calls at most):
  (a) a command of a `spawn_agent` child, fresh and resumed: the rows B2, B6, B10, B11 and its tool list; whether the stream names the call, so
  that coai can refuse a turn that used it; any switch that removes the six tools;
  (b) the JavaScript of `functions.exec` itself: file, environment, process and network reach without `exec_command`, and where it runs;
  (c) the shipping place: a `CODEX_HOME` that is not under `/tmp` (the measure met `Refusing to create helper binaries under temporary dir`; a job
  folder is not under `/tmp`, so the helper binaries of a real job were never seen);
  (d) `.codex` files that appear in the worktree AFTER turn 0, as a check can write them;
  (e) the refresh: no refresh with `exp` far away and `last_refresh` 9 days old; the text and exit code when the override address refuses the
  connection; a copy with the real access token and a PLACEHOLDER refresh token answers `exec` and `exec resume`.
  If (a) or (b) does not hold and no flag closes it, the addendum says `refuse` for 0.171.0 and the row "refuse / ship" of SPEC C5 applies.
- **X2. The release is pinned by its bytes.** Exact release, and the sha256 of the program file that was measured; both read before every
  launch from the file the unit runs; the program is bound read-only. → s5 t1.
- **X3. The refresh can never happen in a job** — section 9. → s5 t1 (the margin), s5 t2 (the override), s5 t3 (the row), s7 t6 (the copy).
- **X4. The argv is CX1 and the profile, on every turn.** `config.toml` is written and read back before EVERY launch (a resumed turn takes the
  limit from it: `exec resume` has no `-s`). A resumed argv has every flag of the fresh one that `exec resume` accepts. → s5 t2.
- **X5. The unit is the measured one**: main and the coder root hidden (not read-only), the `/tmp` shape under which the vendor sandbox starts,
  the same in the preflight and in the launch. Never `RestrictNamespaces`, never `ReadWritePaths=/mnt`. → s5 t2.
- **X6. The preflight probes V inside the unit with `codex sandbox -P coai`**, the tool list with `codex debug prompt-input`, and the rest of
  section 8. → s5 t3.
- **X7. After every turn**: the thread id is the handle's; no item of the stream is a tool outside the measured list. → s5 t4.
- **X8. The output schema is the flat variant** (the API refuses the `allOf` of `shared/coder-report.schema.json`). coai's strict reader stays
  the check; a test holds the flat file and the reader equal. → s5 t2.

Not a boundary matter, to know: a tool can exit 0 for a write that never reached the disk (B3, B5) — coai reads the tree, never a tool's exit
code; the vendor sandbox makes empty `.agents`, `.aws`, `.codex` folders in the worktree and protects those names, so a codex coder cannot edit
a file under `.agents/` or `.codex/` of a repository that tracks them (this one does): such a task ends with its report and the route goes on.

## 6. Must-change — agy

- **G1. Measure before any code of s6** (s6 t7; 12 paid turns at most): B13 on a resumed turn, also with hook and agent files that appear in
  the worktree after turn 0; the guard folder read-only in the unit; the tool events of the stream against the hook's payloads; where the stream
  names the mode; a sign-in copy with no refresh token; the program run from a read-only copy; agy behind a proxy in a unit with no network.
  When the installed release is no longer 1.5.17, the task stops: the rows of s4 t5 must be measured again on the new release first.
- **G2. The guard is closed by construction.** The handler is coai's own binary (`--agy-hook`), started by the hook's command with `exec` and
  nothing else. It writes an explicit decision for EVERY input: an empty input, text that is not JSON, a tool it does not know and a payload with
  no `Path` are each `deny`. It has a deadline of its own (5 s, under the hook's 10 s) that also ends in `deny`. A failure to write the decision
  is a non-zero exit. So "exit 0, no output" — the one answer agy treats as allow (U9) — cannot come from this handler. A preflight row gives it
  an EMPTY input, through the exact command line of `hooks.json`, and needs the explicit `deny`. → s6 t2, s6 t4.
- **G3. The guard's rule.** Allow only `read_file`, `write_file` and `edit_file` (the names the hook sees), and only when `Path`, resolved
  through links, is inside the job's worktree — the worktree coai wrote into the guard's own file, and also a `workspacePaths` entry of the
  payload — and not under `.git`, `.agents` or `.codex` of it. Everything else is `deny`: `run_command`, every network tool, `manage_task`,
  the guard folder, the home, the sign-in file. The deny of `.agents` and `.codex` is what was measured; it is dropped only by an addendum after
  G1. → s6 t2.
- **G4. The guard writes one record for each decision** (tool id, tool name, decision), outside the worktree. → s6 t2; read by s6 t5.
- **G5. The agent file is the measured bytes, and `--agent coai-coder` is on EVERY argv, fresh and resumed.** A lost conversation starts a new
  one in silence; without `--agent` that new one is agy's default agent, which has a command tool. → s6 t2, s6 t3.
- **G6. `--mode accept-edits` is on every argv and is asserted.** The help declares it (each launch); after every turn the stderr holds no
  mode fall-back line (`--mode auto` fell back to the default mode with only a warning; in the default mode a command RUNS). → s6 t3, s6 t5.
- **G7. The working directory is the job's guard folder, never the worktree.** The worktree is only an `--add-dir` root. This repository has an
  `.agents` folder: with the worktree as cwd agy would load the repository's own hooks and agents. The guard folder is written and read back
  before every launch, is read-only inside the coder's unit when G1 shows agy runs so, and is hidden from the checks (S1). → s6 t3.
- **G8. The conversation id is compared on every resumed turn.** Exit code 0 with another id, or the stderr line `conversation "<id>" not
  found`, is a lost session: a `crash`, the worktree goes back to the last coai commit, the next model starts there. → s6 t5.
- **G9. The release is pinned by its bytes, and a self-update is seen before every launch.** Exact release and sha256, read from the file the
  unit runs. agy updates itself (1.5.16 became 1.5.17 inside one day): when G1 shows that agy runs from a read-only copy, coai keeps the measured
  program in a store of its own and launches only from there, so an update of the person's `agy` neither changes a running job nor refuses the
  next one. Without the store, an update refuses the agy coder until the new release is measured and judged. → s6 t1.
- **G10. The refresh token stays out of the coder's unit when that can be built.** When G1 shows that agy runs a turn with a copy that has no
  refresh token: the refresh is done in a unit of its own with no model, no repository and no worktree (`agy models`), and the coder's unit gets
  a copy with the access token only (valid about one hour; a turn is capped at 45 minutes and gets a fresh token). When G1 shows it does not
  work, the full copy stays and accepted risk A1 is at its largest. → s7 t6 (the mechanism), s6 t3 (the recipe).
- **G11. The network limit.** As far as measured there is NO property of a user unit that limits the hosts of a process that needs its vendor
  (U13; J5). The only limit that is possible is a unit with no network whose one way out is a forwarder of coai's with a list of hosts (J8 shows
  the plumbing). Its other half — agy working behind a proxy — is not measured; G1 measures it. **It is not built in E3**: it cannot be designed
  before G1, and the device limits (6 tasks in a story, 8 stories in an epic) leave no task for it. It is named for E4 ("coder hardening");
  until it is built it is accepted risk A2. → s6 t7 d6; the line for E4's `doneWhen` is in section 10.
- **G12. The preflight rows of section 8**, one for each `pre` id. → s6 t4.
- **G13. The owner's word before the table.** The agy release enters the table only with an addendum of this file after G1, and with the
  owner's answer on section 7 as it stands then. → s6 t7 d8.

## 7. Accepted risks — for the owner

**agy (the owner confirms these before the release enters the table).**

- **A1. Inside the agy coder's unit nothing of the OS stops a process from reading the sign-in copy.** agy reads it as a file with the job's
  user; a mount cannot show a file to agy and hide it from agy's child. That no other process runs there rests on agy itself: the agent file's
  tool list, `commandExecutionPolicy: "off"`, the hook being called for every tool, and the mode. The pin fixes the program's bytes; it does not
  fix what the vendor's server offers this account (the mode already depends on the account). If agy ever runs a command or a network tool
  without calling the hook, the token can leave before coai reads the turn's stream. What it then loses: with G10 an access token of at most
  one hour; without G10 **the owner's Google refresh token, which does not expire by itself**.
- **A2. The agy process has the whole network and the host's loopback** until the forwarder of G11 exists — if it can exist.
- **A3. B12 cannot be asked before a job.** The tool list is the model's own answer in the measure. Before a job coai can only check its own
  files and its own handler. After every turn it checks the stream; that is after the fact.
- **A4. agy is open on "exit 0, no output".** coai's handler never answers so. A later agy that reads another answer as allow is stopped only
  by the pin.
- **A5. "A refresh does not rotate the Google token" is three observations, not a contract.** If the vendor changes it, a job's refresh can end
  the owner's sign-in; the cure is to sign in again.
- **A6. The cost of the pin.** Without the store of G9 the agy coder is refused after every self-update until about 22 paid turns measure the
  new release; with the store it runs an old release, which the vendor may one day refuse. agy's quota is not unlimited.
- **A7. Rule text of the repository steers the agy model** (measured: it was followed). The guard and the unit are what hold, as for CLAUDE.md.
- **A8. An agy coder cannot edit `.git`, `.agents/**` or `.codex/**` of the worktree.** A task that needs it fails with its report.

**codex.**

- **C1. For a command of the model, B6 (inside the job folder), B10 and B11 are one layer: the vendor's sandbox.** It was seen to refuse to run
  when it cannot start. If it has a hole on 0.171.0, a command can read the job's `auth.json`. S4 and the placeholder of X1 (e) bound that.
- **C2. The codex process itself has the whole network and the host's loopback**; only the model's commands lose it (as claude in E2).
- **C3. A real refresh was never run.** Section 9 keeps it from happening. If every guard fails, the owner signs in to codex again.
- **C4. `AGENTS.md` of the repository reaches the model** (as CLAUDE.md for claude).

**Both.** Measured on one machine (WSL2, kernel 6.18, systemd 259.5, bubblewrap 0.11.1). Another Linux is decided by the preflight before each
job. Native Windows and macOS stay refused.

## 8. The properties the verdicts rely on (the ids of the table, and where each is asked)

Classes: **pre** — a preflight row: no model, inside the EXACT launch unit (the same property list), before every job. **launch** — asked by coai
before every launch, turn 0 and every fix turn, on the files the unit will get. **turn** — read by coai from the CLI's own stream after every
turn. **release** — cannot be asked without a model turn: measured per release, fresh and resumed, and tied to the exact release and the
program's sha256.

SPEC C2 item 3 says "one row for each id". That holds for the `pre` ids. A `release` id has no row that would be true: a row can only check
coai's half (its files, its handler). This file says so instead of writing a row that proves nothing; the lead folds the sentence into the SPEC
with the next plan round (section 10).

**Shared by both vendors — the coder's unit (U) and the check unit (K).**

| Id | Property | Class | Rows |
|---|---|---|---|
| U1 | the worktree is the job's and can be written | pre | B1 |
| U2 | the main checkout is HIDDEN: no file of it can be read or listed; only `<main>/.git` is there, read-only | pre | B2, B4, B6, B7 |
| U3 | the coder root is HIDDEN: nothing beside the worktree is there; a write beside it does not reach the disk | pre | B3 |
| U4 | the `.git` pointer of the worktree and the main `.git` cannot be written | pre | B4 |
| U5 | no user manager and no bus inside; `/run/user` is empty; `/mnt` holds the resolver file at most; `/run/WSL` is closed; a Windows `.exe` does not run | pre | B9 |
| U6 | HOME, `/tmp` and `/var/tmp` are the job's own; the real home cannot be read or written | pre | B5 |
| U7 | the environment is exactly the vendor's allow-list (compared name by name, not only searched for bad names) | pre | B15 |
| U8 | the folder of another job cannot be read | pre | B3, B11 |
| U9 | the program of the launch is there, cannot be written from inside, and its sha256 is the measured one | pre | — |
| K1 | from a CHECK unit, in each network state, a canary in the job's coder folder cannot be read, listed or written | pre (the check preflight) | B11 |
| K2 | from a CHECK unit a canary in another job's folder cannot be read | pre (the check preflight) | B11 |
| K3 | the tokens of the job's sign-in copy are in no staged blob and in no staged name | turn (before every commit) | B11 |

**codex.**

| Id | Property | Class | Rows |
|---|---|---|---|
| CX-P1 | the installed release is exactly a release of the table, the sha256 of the program file is the measured one, and the help of `exec` and of `exec resume` declares every flag of CX1 | launch | — |
| CX-V1 | the vendor sandbox STARTS inside the unit with the profile `coai` (`codex sandbox -P coai`) | pre | all V rows |
| CX-V2 | inside it: a write in the worktree works; a write into the job folder and beside the worktree does not reach the disk | pre | B1, B2, B3, B5 |
| CX-V3 | inside it: the job's `auth.json`, the job folder and the main checkout cannot be read | pre | B6, B7, B11 |
| CX-V4 | inside it: no network — a listener that coai opens on the host loopback is reached from the unit and NOT from the sandbox | pre | B10 |
| CX-V5 | inside it: no process but the sandbox's own is visible, and no environment in `/proc` holds the canary name of the unit | pre | B9, B11 |
| CX-V6 | inside it: `.git`, `.agents` and `.codex` of the worktree cannot be written | pre | B4, B13 |
| CX-T1 | the tool names of `codex debug prompt-input` with the exact flags are the measured list: no `mcp__` name, no web search, no image tool | pre | B12 |
| CX-T2 | a command of a `spawn_agent` child has the same sandbox and no other tool | release — **not measured yet (X1 a)** | B9 to B12 |
| CX-T3 | the JavaScript of `functions.exec` reaches no file, environment, process or network by itself | release — **not measured yet (X1 b)** | B6, B10, B11 |
| CX-T4 | no item of a turn's stream is a tool outside the measured list (an MCP call, a web search; an agent call when CX-T2 says the stream shows it) | turn | B12 |
| CX-C1 | the job's `config.toml` is byte for byte what coai wrote, read back inside the unit | pre, and launch (written before every turn) | B14 |
| CX-C2 | the argv is CX1, fresh and resumed, and holds no never-flag of SPEC C3 | launch | B12, B14 |
| CX-C3 | a `.codex/config.toml` with an MCP server in a stand-in project does not change the tool list of `codex debug prompt-input` (the project is not trusted) | pre | B13 |
| CX-C4 | `exec` and `exec resume` apply the profile of `config.toml` to every command of the model | release (X1 looks for a model-free way to ask it) | B2 to B11 |
| CX-R1 | the copy's access token has a readable `exp`, and `exp` ≥ now + the unit's `RuntimeMaxSec` + 60 min (section 9) | launch | — |
| CX-R2 | `CODEX_REFRESH_TOKEN_URL_OVERRIDE` is in the unit's environment with the dead local address, and a connection to that address from inside the unit is REFUSED | pre | — |
| CX-R3 | the sign-in in the unit is the job's copy, mode 0600; the real `~/.codex` cannot be seen | pre | B11 |
| CX-R4 | the copy holds a placeholder in place of the refresh token | pre — **only when X1 (e) shows it works** | B11 |
| CX-S1 | the thread id of every turn is the handle's; the recorded "no rollout found" text is a crash | turn | B14 |
| CX-S2 | the handle is bound by coai to the vendor, the job, the worktree and the path of `CODEX_HOME` (codex binds a thread only to that path) | launch | B14 |
| CX-S3 | the flat output schema and coai's reader are equal; the report is checked by the reader | turn | — |

**agy.**

| Id | Property | Class | Rows |
|---|---|---|---|
| AG-P1 | the release is exactly a release of the table; the sha256 of the program file is the measured one; the help declares `--mode accept-edits`, `--agent`, `--add-dir`, `--conversation` and the stream formats | launch | — |
| AG-P2 | the program runs from coai's store copy (**when G1 shows it runs so**); the person's `agy` is not in the unit | pre | — |
| AG-G1 | the handler allows a measured tool on a path inside the worktree, and denies: a path outside, `.git`, `.agents`, `.codex`, a file link out, a folder link out, the guard folder, the sign-in file, `run_command`, a network tool, `manage_task`, an unknown tool, a payload with no `Path` | pre (one answer for each case, in the unit) | B1 to B11 |
| AG-G2 | the handler answers an EMPTY input and text that is not JSON with an explicit `deny`, through the exact command line of `hooks.json`, run from the guard folder | pre | all G rows |
| AG-G3 | `hooks.json` (matcher `*`, timeout 10), the agent file, the hook script and the guard's own file are the expected bytes, read back inside the unit | pre, and launch (written before every turn) | B12, B14 |
| AG-G4 | agy calls the hook for EVERY tool call, and takes `deny`, a non-zero exit and a timeout as "do not run" | release (1.5.17: fresh and resumed) | B2 to B11 |
| AG-G5 | every tool event of a turn's stream has a guard record with the same id and the decision `allow` | turn — **when G1 shows the ids match** | B12 |
| AG-A1 | with the measured agent file the tools are `read_file`, `write_file`, `edit_file`, `manage_task`; no command, list, search or network tool | release (the model's own answer) | B7, B9, B10, B12 |
| AG-A2 | no tool event of a turn that was not denied names a tool outside `read_file`, `write_file`, `edit_file` | turn | B12 |
| AG-M1 | the argv has `--mode accept-edits` and `--agent coai-coder`, fresh and resumed, and no never-flag of SPEC C3 | launch | B9, B14 |
| AG-M2 | the stderr of a turn holds no mode fall-back line (and the stream names `accept-edits`, when G1 finds where) | turn | B9 |
| AG-B1 | the working directory of the unit is the guard folder; the worktree is only an `--add-dir` root | pre, and launch | B13 |
| AG-B2 | hooks and agents in `.agents` of the worktree do not run, also on a resumed turn and also when they appear after turn 0 | release — **the resumed half is not measured yet (G1)** | B13 |
| AG-B3 | the guard folder cannot be written from inside the unit (**when G1 shows agy runs so**) | pre | B13, B14 |
| AG-R1 | the sign-in in the unit is the job's copy, mode 0600, the only file of the sign-in there; the real `~/.gemini` cannot be seen | pre | B11 |
| AG-R2 | the copy in the coder's unit holds no refresh token, and its access token outlives the turn by 5 min (**when G1 shows it works**) | pre, and launch | B11 |
| AG-S1 | the conversation id of every resumed turn is the handle's, and the stderr has no "not found" line; else a crash | turn | B14 |
| AG-S2 | the handle is bound by coai to the vendor, the job, the worktree and the home | launch | B14 |

Counts: 12 shared, 22 codex, 18 agy. An id marked "when … shows" enters the table only with the addendum that follows the measure.

## 9. The refresh margin (codex)

The copy holds the owner's real tokens. A refresh on the copy rotates the refresh token, and the owner's own file then holds a dead one.
codex refreshes when the access token's `exp` ≤ now + 5 min (source at 0.160.0; on 0.171.0 only the names were seen in the binary). The access
token lives 10 days.

- **The margin.** Before EVERY launch (turn 0 and every fix turn) coai reads the copy and starts the launch only when `exp` can be read and
  **`exp` ≥ now + the unit's `RuntimeMaxSec` + 60 minutes**. `RuntimeMaxSec` is the hard end of the unit (the turn's deadline plus one minute,
  61 minutes at most today), so at every moment of the launch at least 60 minutes are left: 12 times the CLI's own window. That room is for a
  window that changed between 0.160.0 and 0.171.0 and for a wrong clock. Too little time left is a refusal that tells the person to run `codex`
  once themselves (their own CLI refreshes their own file) — never a launch with less.
- **The backstop.** `CODEX_REFRESH_TOKEN_URL_OVERRIDE=http://127.0.0.1:9/oauth/token` in the unit's environment: a refresh that the margin did
  not foresee cannot reach the vendor (4 of 4 POSTs went to the override in the measure). Port 9 refuses a connection on this machine and only
  root can listen below 1024 (J7); the unit shares the host's loopback (J6), so a preflight row asks it inside the unit before every job. The
  text `Your access token could not be refreshed` is a lost sign-in for the failover: the vendor's other models are skipped.
- **By construction, when X1 (e) shows it works**: the copy holds a placeholder in place of the refresh token. Then no refresh on the copy can
  rotate anything, whatever the CLI does, and the long-lived secret is never in the job folder.

agy needs no margin: its refresh did not rotate the token (3 observations) and it refreshes at every start with an expired token. With the
copy of G10 the launch needs `expiry` ≥ now + `RuntimeMaxSec` + 5 minutes, because that copy cannot be refreshed.

## 10. What this commit changed, and what the lead does next

Task files changed from the lists above:

- s5: new `t6-codex-open-rows-measure` (first; t1 depends on it); t1 to t5 amended.
- s6: new `t7-agy-open-rows-measure` (first; t1 and t2 depend on it); t1 to t5 amended; `t6-agy-boundary-scenarios` moved to s8 (s6 is at the
  device limit of 6 tasks).
- s7: t1 and t2 amended; new `t5-check-units-hide-the-coder-folder` and `t6-sign-in-copies`.
- s8: `t6-agy-boundary-scenarios` (from s6); t1 and t4 amended. `story.yaml` of s8 is unchanged: both vendors stay in its `dependsOn`.
- Both boundary-scenario tasks (s5 t5, s8 t6) had a line that could not be true: "a child command cannot read the stand-in sign-in file".
  Inside the unit the job's own sign-in copy CAN be read; the file that cannot be read is the one of the real home. Both now say so, and a test
  states the readable copy as a fact.
- `security: true` is new on s5 t4, s6 t5 and s7 t2: each now holds a check that ends a job `coder-boundary`.
- s4 t6: `files` names the s7 and s8 folders too.

Counts after the commit: E3 has 8 stories and 43 tasks (it had 39); no task is above complexity 6; s5, s6, s7 and s8 each hold 6 tasks.
New paid calls: 8 (codex, `gpt-6-luna` low) and 12 (agy, `gemini-3.8-flash-low`) at most — the caps are the owner's (SPEC H, question 5).

For the lead (this judge did not edit them):

1. **A new plan round for the scope.** Task files of s5 to s8 changed, so the approval id of the scope changed (SPEC B8).
2. **The plan-change count.** Four tasks were added and one moved for one cause (this judgement, which SPEC C5 orders). Whether that is an entry
   of the parent plan's ledger is the lead's decision; in doubt it is a change.
3. **Sentences of the SPEC to fold in:** the header's "39 tasks"; C2 item 3 ("one row for each id" → "one row for each `pre` id; the other
   classes of section 8 of the judgement"); C6 ("the hook … is not `.git`" → also `.agents` and `.codex`, until an addendum drops it; the agy
   working directory is the guard folder); section F (the program store of G9: one copy of about 280 MB per measured release, removed when its
   release leaves the table); section H (the accepted risks of section 7).
4. **E4.** A `doneWhen` line: "the coder of a vendor whose measure showed it works behind a proxy runs in a unit with no network but coai's
   forwarder with the vendor's hosts (the judgement of E3 s4 t6, G11)".
5. **E2.** The follow-up of section 4, point 3 (the claude coder's home and the checks).
6. **The owner.** Section 7 for agy; the two caps.

## 11. Not measured, and not checked by this judge

- Everything marked "not measured yet" in section 8, and the items of X1 and G1.
- No row of either results file was run again: the judge ran no vendor CLI. The rows are taken as the measure wrote them, with their hashes.
- What claude does with a file a check planted in the coder's home (section 4, point 3).
- The probes of section 2 ran on scratch folders under `/tmp` without the `/tmp` and `/var/tmp` binds of a real unit; they show what a bind and
  a hide do, not that the whole build of this repository passes under shape J3 — s7 t5 runs the suite under it.
- Whether a listener can appear on `127.0.0.1:9` between the preflight row and a refresh: only root can make one; the margin is the first guard.

## 12. Addendum 1 (2026-11-26): after the coder comparison of E2, and the owner's decision on the coder prompt

Judge: Fable, the same role. Read: `v2/epoch-3/e2-coder-one-task/RESULTS_coder_comparison.md` and its `comparison/` folder (merged into
this branch with `feat/v2-ep3-e2-coder` `ec24ee4c`). Run by the judge: nothing — no vendor CLI, no model call, no probe. **No verdict
changes, and no release enters the table by this addendum.** Sections 1 to 11 stand as they were written.

### 12.1 What the comparison is evidence for

One task, one run for each vendor (codex: three), started by a harness in the unit shape of s4 t3 and t5: the main checkout and the
coder root hidden, the sign-in as a copy, for agy the SHELL guard of s4 made fail-closed (not coai's handler, G2). It shows that
both CLIs did the task inside that shape, that the real sign-in files did not change, and that 17 paths could not be seen from the
replica of the check unit. That last point does not close S1: the harness kept the copies in a scratch folder under `/tmp`, which a
check unit replaces with its own; in the product the copies are in the job folder, which the check unit binds. S1 stands.

It is not a measure of any id of section 8: the product ran no row, nothing went red, no turn was resumed, and the guard was not
coai's binary.

### 12.2 The agy mode (G6, AG-M1, AG-M2)

Observed: the init line of the agy turn named `permission_mode: request-review` while `--mode accept-edits` was on the argv; the
edits went through. G6 says the mode "is asserted", and AG-M2 has a second half, "the stream names `accept-edits`, when G1 finds
where". In the one place where the stream names a mode it does NOT name `accept-edits`.

Three readings are open, and one line cannot choose between them: (a) `request-review` is how 1.5.17 names `accept-edits` in a
headless run (an edit is accepted; a command would be asked, and so is denied — the measure saw `denied_actions: command`); (b) the
field is another setting, of the account or of the agent, and does not follow `--mode`; (c) the flag did not take, without the
stderr warning that `--mode auto` gave. Under (c) the turn ran in a mode nobody asked for, and in the default mode a command RAN in
the measure. With the coder agent there is no command tool and the guard denies `run_command`, so row B9 keeps the layers A and G.
The layer in doubt is M.

Must-change **G14** (it replaces the stream half of AG-M2):

- The flag is a request. AG-M1 stays as coai's own half; it proves no mode.
- s6 t7 (d9) measures the `permission_mode` of the init line for no `--mode`, `--mode plan`, `--mode accept-edits` and the
  `--mode auto` fall-back, fresh and resumed, with no model turn where agy prints the line before one.
- **AG-M3** — class `turn`. It enters the table only when the value for `accept-edits` differs from the value of the default mode
  and of the fall-back on every start that was measured. The property: the init line of every turn names the value the table holds
  for the release. Another value, or a stream with no init line, ends the job `coder-boundary` before anything of that turn is
  committed. Rows: B9.
- **AG-M4** — class `release`. It enters the table when AG-M3 cannot. The property: with `--mode accept-edits` an edit inside the
  worktree is done and a command is denied, fresh and resumed (for 1.5.17 the fresh half was seen in the calls of s4 t5 for U10;
  the resumed half is not measured, and s6 t7 d9 measures it when AG-M4 is needed). Nothing then asks the mode at the time of a
  job, and for a job row B9 rests on A and G alone. Rows: B9.
- Accepted risk **A9**, for the owner, only with AG-M4: the mode a turn ran in cannot be read; a silent fall-back to the default
  mode is stopped only by the agent file and the guard.

The count of section 8 changes only with the addendum that follows s6 t7: 18 agy ids, and then AG-M3 or AG-M4.

### 12.3 The coder prompt of each vendor (the owner's decision 40, 2026-11-25)

The decision: each vendor gets its own version of the coder prompt — its own tool names, and a line that commands which the
repository's instruction files order first are not run in the job, because coai runs the checks; the claude text stays as it is.
It follows from the comparison: codex refused the shipped prompt twice (the names of claude's tools; then `AGENTS.md` of this
repository, which orders a command before any edit).

It is not a boundary matter, and the judge has no objection: a prompt is a request to a model, and no id of section 8 relies on
one. Three conditions for the task files, each now a dod line (s5 t4 d4, s6 t3 d6, s7 t2 d5):

1. The added line is narrow. It lifts only "run this command before you work". A line that told the model to disregard the
   instruction files of the repository would also drop their rules about secrets and paths.
2. It gives the model no command and names no tool that the vendor's measured list does not hold.
3. No vendor gets the prompt of another by default, and an override a person wrote for claude is never sent to another vendor.

For codex the line makes accepted risk C4 neither larger nor smaller: `AGENTS.md` still reaches the model, and the vendor sandbox is
what holds. For agy the guard denies every path under `.agents` (2 denied reads in the comparison), so the model could not read
those files; there the line only keeps a model from refusing.

### 12.4 X8, seen

codex wrote `"reason":null,"notes":null` on a report with `status: "ok"`: the flat schema makes every member required. coai's
strict reader is not to be loosened in general. Only a null of a member that the status does not use is read as absent; a null of
any other member and an unknown member stay errors (s5 t2 d2).

### 12.5 For the lead

- Accepted risks A1 and A2 are not changed by the comparison: the sign-in copies were visible to the coder units, by design.
- Task files changed with this addendum: s5 t2, s5 t4, s6 t3, s6 t4, s6 t5, s6 t7, s7 t1, s7 t2, s8 t2, s8 t4. No task was added.
- Not measured, and not checked by this judge: everything of this section. The judge read the comparison file as it was written.
