# ConnectOtherAIs — architecture, protocol and measurements

> The engineering behind the gate: the protocol as it is enforced, the prompt shapes and what
> measuring them changed, the local-model path and why it bypasses the vendor CLIs, the token
> accounting, and the build. If you arrived looking for what this is and how to run it, that is
> [README.md](README.md) — this is the deep dive it links to.
>
> The measurement write-ups themselves live in [research/](research/): every `RESULTS_*.md` is one
> campaign with its data beside it under `research/data/`.

A multi-model review gate. Your main AI writes the plan and the code; **other vendors' models** —
Codex, Antigravity (Gemini/Claude/GPT-OSS), a second Claude, DeepSeek — review both in rounds,
until the findings that matter drop under a
threshold, or the rounds are spent and the agent takes what is true (`good_enough`).

The value is not "more review". It is **review by a model that cannot see the author's reasoning**,
which is the only kind that catches the author's assumptions.

Two halves, the shape CredsForDevs proved — and, since the Team server, an optional third:

| | What it is |
|---|---|
| `coai-mcp` | A Native-AOT MCP server over stdio. An MCP client starts it; it runs the rounds. |
| ConnectOtherAIs | A VS Code extension: settings, the install button, the rounds view and its log. |
| `coai-server` | **Optional.** The Team server: one box running the vendor CLIs on one company subscription, behind your own sign-in. Nothing needs it; it has its own `server-v*` release line and its deploy is manual. |

## The protocol

```
open → review_plan → resolve → (revise, repeat) → proceed
     → implement
     → review_code → resolve → (fix, repeat) → proceed
```

`review_code` **refuses** until a plan round has reached `proceed`. Skipped stages are impossible,
not discouraged — the honest limit of a design with no hooks: the server cannot make a model call
it, but it can make a skipped stage impossible to fake.

Eleven tools, unprefixed (the client's `coai` id is the namespace): `providers`, `open`,
`review_plan`, `review_code`, `review_document`, `review_feature`, `resolve`, `status`, `ask_human` —
and `consult` with `close_consult`, the pair that gates nothing: a stuck assistant asking another
vendor's model about the LIVE working tree, uncommitted edits included, getting an answer rather than
a verdict, and recording afterwards whether it helped. `review_feature` is the fourth gate, called
once at the end of a plan of three or more epics over the whole feature; it needs no `open`.

## Who decides at the end of a round (v2)

A round ends in one of **four outcomes**, and none of them waits for a person:

| Outcome | Meaning | What the AI does |
|---|---|---|
| `proceed` | the findings that gate are at or under the role's threshold | `resolve`, then the next stage |
| `revise` | findings still gate and the role has rounds left | fix, `resolve`, review again |
| `good_enough` | rounds are spent and findings still gate (or a feature review is at its cap) | read the findings, apply the ones that are true, reject the rest with reasons, `resolve`, move on |
| `not_reviewed` | no reviewer answered | the stage proceeds WITHOUT a review — said out loud, never a pass |

There is no held gate, no escalation card, no `call_human` verdict, no `continue_anyway`, no 15-minute wait
and no per-stage "on exhausted" policy. The old ladder is gone, and with it the dialog, the status-bar
item and the open-questions section that served it. The `ask_human` tool stays registered so an old
caller does not break, and only answers `ask_in_conversation` with a note: the AI asks you in its own
chat. `ask_consultants` is unchanged — it asks other vendors' models, never a person.

A data folder written by an older build can hold sessions that were held, cards that were open and
settings keys the v2 server no longer reads. The server converts them itself — the first start of a v2
build runs the migration, a load of a session that is still held converts it, and `coai-mcp
--migrate-v2` shows or runs the same work by hand. See `research/module_server.md`, *The v2 data
migration*.

An answer a person had already given to a held gate, and v1 never applied, is overridden by the
conversion (the gate becomes `good_enough`); the person is told once, by a server notice, before the
session is converted.

## What makes "fewer than 2 remarks" mean something

Without a rule, three verbose reviewers guarantee escalation forever. So:

1. Only `blocking` and `major` count; minors and nits are reported and never gate.
2. **De-duplication happens first** — same file, lines within ±5, same category, same remark → one
   finding listing every provider that raised it. Two vendors agreeing is stronger evidence, not
   twice the work.
3. A finding you rejected **with a reason**, re-raised with the same argument, does not count again.
   Re-raised with a genuinely new argument, it counts in full.
4. **Each ROLE has its own rounds and its own threshold**, and a finding is counted against the
   threshold of the role that raised it. Architecture may be worth two passes with different lenses
   while performance is worth one; a shared budget forces the cheapest role to pay for the most
   expensive. A stage passes when every role is at or under its own number — not when one total is
   small enough — and it revises only for roles that still have rounds to spend.

Defaults (`DEFAULTS` in `settingsShape.ts`): one round for every role — two for the feature review —
and a threshold of 6 for the plan role and 5 for every other role. When the rounds run out the answer is
always `good_enough` (a round nobody answered is `not_reviewed`; a feature review's first round of two is
retried first): the gate never waits and never writes a card. Only the AI's own question can still reach a
person, and it does so in the AI's own chat, after its consultants.

## Role jobs and the BA (v2, epoch 3)

A role is a model that is not yours, run as a **detached job**. Four tools and one one-shot mode carry it:

| Surface | What it does |
|---|---|
| `start_role(role, repoPath, taskFile \| brief, model?, timeoutMinutes?, workItem?)` | Starts the job and answers at once with `{jobId, state, route[], launcher}`. The job is run by its own process (`coai-mcp --role-run <jobId>`): it outlives the call, this server and your session. Roles today: `ba`, `coder`, `scout`, `judge` and `probe` (a diagnostic that asks no model). The scout and the judge need `brief` and may add `taskFile` ([below](#the-scout-and-the-judge-v2-epoch-3)). Refused, never queued, past `COAI_ROLE_JOBS_MAX` (3; 2 on WSL). |
| `role_status(jobId, maxSeconds?)` | `{state, generation, attempts[{n, model, vendor, stop, reason, evidenceRef, keyId}], stalled, result}`. `keyId` is the claude key the attempt ran on (the id of a pool key, `person-sign-in` for the device switch, empty for another vendor). `maxSeconds` (0–50) waits for the end. States: `queued`, `starting`, `running`, `succeeded`, `failed`, `cancelled`, `lost`. `lost` = the runner died without a result; `stalled` = alive but silent for two minutes (never taken over). |
| `role_cancel(jobId)` | Asks the runner to stop, waits 10 s, then kills the recorded runner and CLI child by pid and start time. The generation is raised first, so a paused runner publishes nothing. |
| `validate_work(repoPath, featureFolder)` and `coai-mcp --validate-work <dir>` | Checks a work-plan tree against `shared/work-plan.schema.json` and the rules of `shared/work-method.md`, with this device's limits. Writes nothing. |
| `coai-mcp --role-delta <jobId> --at <commit>` | The measure of the BA: `changedShare` = (changed + deleted + added task files) / the task files of the BA's manifest, read at `<commit>`. Passes below 0.20; take it at the commit after the plan gate. Exit 0 measured, 65 wrong arguments or no such job, 74 unreadable. |

**Job files are the authority.** `<dataDir>/roles/jobs/<jobId>/` holds `request.json`, `job.json`, the lease, the heartbeat,
`attempts/<n>/` (the answers), `result.json` and, for the BA, `ba-manifest.json`. RoundsDb (`role_jobs`, `role_attempts`) is a projection.
A job is `lost` only when its OS lock is free.

**Router and failover.** The route comes from the role's list in `COAI_ROLES_MODELS` (the default `ba` list is `fable`, then `opus`); a security, kind-`security` or
complexity-10 task takes the `important` list. A model name no CLI is known to accept is refused before any launch (`claude` bills an unknown name). A model that
stops with a **limit**, a **refusal** (the output arm `status: "refused"`, never prose) or a **crash** sends the job to the next model at once, from turn 0 in a new
session; only the last model waits. Every switch is one Information line (`role job … switched A -> B after <stop>: <reason>`) and one `role-model-switched` notice that names the vendor
the repository text now goes to.

**The BA** (`start_role role: "ba"`, `taskFile:` an `epic.yaml` that has no stories) reads the plan and answers ONE JSON object (`shared/ba-output.schema.json`, no paths). coai checks it
with `validate_work`'s rules over an overlay, sends the errors back to the same session (at most two repair turns), and only then writes `stories/**/story.yaml`, `task.yaml` and
`ba-manifest.json` atomically (a staging folder, one rename). An invalid plan writes nothing (`failed: output-invalid`).

## The scout and the judge (v2, epoch 3)

`start_role role: "scout"` and `role: "judge"` answer a QUESTION about a repository, read-only, on the road the BA takes (`ReadOnlyRoleWork`): the consultant adapters' read-only launch (Read, Glob, Grep; never the coder's write case), the tree compared around every attempt (`tree-changed` never fails over), at most two repair turns in the SAME session, a declared refusal goes to the next model. There is no new tool. `brief` is required (at most 64 KiB); `taskFile` is optional and must be a regular file inside the repository (not a link, not under `.git`, not named like a credential file).

| | scout | judge |
|---|---|---|
| model list | `scout` | `important` (a `model` the caller names outside it is refused before any folder, naming `COAI_ROLES_MODELS`) |
| answer (`shared/…schema.json`) | `facts[{claim, evidence[1..5]}]`, `files[]`, `unknown[]` | `verdict`, `summary`, `findings[{id, severity, title, detail, evidence[0..5], fix}]`, `decision` only when asked |
| reader | `core/Roles/ScoutOutput.cs` | `core/Roles/JudgeOutput.cs` (approve with a blocking finding, revise or reject with no finding and a repeated id are errors) |

**Evidence is checked by coai** (`core/Roles/RoleEvidence.cs`, pure; `RoleEvidenceFiles` reads through `RealPath` only). An item `{file, line, text}` is `verified` when that line holds the text once white space is collapsed. A blank `text` (empty, white space, or only characters that draw as nothing) is an ERROR of the answer, decided before any line is read, so it can never be verified. A path that is absolute, has a drive or `..`, a `.git` segment, runs through a link or is named like a credential file is an error and the file is never opened. A file over 2 MiB or not UTF-8 gives `verified: false`. A shape error and an unverified item go back as a repair turn; after the last turn a shape error ends `failed: output-invalid`, while an unverified item stays in the envelope with `verified: false` (never shown as verified, never dropped).

**The envelope** (`result.json`, built by coai from the answer and its checks, never copied from the model): the answer with `verified` and `why` on every evidence item, `exists` on every listed file, `counts`, `repairTurns` and the `commit` HEAD was at when the job began. Every text passes `Redaction.SafeText`.

## review_plan over a feature folder (v2, epoch 3)

`review_plan(repoPath, branch, featureFolder, workItem?)` reviews a work plan that is COMMITTED. Give exactly one of `planText`, `committedPlan` or `featureFolder`. No new tool.

- **Read from git.** The server resolves the tip of the branch once. That commit is the commit of the round. `GitWorkFolder` reads the folder from it: one `ls-tree`, each blob checked against its id. It follows no link and no submodule. The working tree is never read for the text.
- **Validate first.** The server checks the whole feature with the rules of `validate_work` and the limits of this device. A folder that is not valid is refused with at most 10 errors (code, node, message), their total and the source of the limits. No reviewer is launched, no round row is written, the stage does not move and no worktree is made.
- **The scope.** `workItem` names an epoch, an epic or a story of the folder. With no `workItem` the scope is the whole feature. A `workItem` that names no node of the folder is refused.
- **The pack** (`core/Work/WorkPack.cs`, pure code). The order is: `feature.yaml`; the `plan` file of the feature; the ancestors of the scope (`epoch.yaml`, `epic.yaml`, the `spec` file of the epic); then each story after the stories it depends on and, in a story, each task after the tasks it depends on. Ties go by the number in the id (`s2` before `s10`). The same commit gives the same bytes. The pack of every scope has one line (node and title) for each node outside the scope that a node of the scope depends on. Each file is one section, `## <path>`, in a fence one backtick longer than the longest run of backticks in the file. The reviewers also read a line **Not sent**: the files the pack does not hold.
- **The limits.** A YAML file, a `plan` file and a `spec` file: 64 KiB each. The folder: 2000 entries. The pack: 256 KiB. Over a limit the round is refused, with the size and the three largest sections. Nothing is cut.
- **What is approved.** The round records an approval id. It is the SHA-256 of one line for the pack format, one line (blob id and path) for each file in the pack, and one line for each outside dependency. A file that is not in the pack, such as a `RESULTS_*.md`, is not in the id. A new pack format changes every id once.
- **The working tree guard.** When HEAD is the commit of the round, every file of the pack is compared with the working tree. An edited, a staged or a deleted file is a refusal that names the files. So is a new `.yaml` file under the scope. When HEAD is another commit, the note says that the working tree was not compared.
- **The coder binding.** A coder job started under a folder round is bound when its task file is inside the scope, its base descends from the commit of the round and the approval id computed at the base is the approved one. After the approval, a change of a file the reviewers read refuses the start; the sentence names the first path or node that differs. The cure is a new plan round. An approval made under an older pack format binds no coder job.

## The coder (v2, epoch 3)

`start_role role: "coder"` runs ONE task of a work plan in its own git worktree, on a model of claude, codex or agy, until the repository's own checks are green. The `taskFile` is a `task.yaml` inside the repository that passes `validate_work`'s task rules.
There is no `start_coder` tool: the coder is one role case of `start_role`, with the same `role_status` and `role_cancel`. A request that fails the check (no `taskFile`, a file not called `task.yaml`, an invalid task) is refused with a sentence before any folder is made.

**The route.** The task's `complexity` picks the model (the band model, then the `coder` list of `COAI_ROLES_MODELS`); `security: true` or `complexity: 10` takes the `important` list. A coder route keeps the models of the vendors that are launchable for the coder (claude always; codex and agy as the next part says). A model of another vendor is dropped, with the reason. A route with none left is refused, naming `COAI_ROLES_MODELS`. A model the caller names in `model` on a vendor that is not launchable is refused, naming the model and the vendor.

**The worktree and who commits.** coai makes `coder/<jobId>` at `baseSha` (the `HEAD` of `repoPath` at the start, unless `baseSha` is given) under a machine-local folder (`COAI_CODER_ROOT` moves it), at most 20 live per repository (the next is refused, none is evicted). The coder edits files. coai commits the work (one commit per turn), builds and tests; the coder never does. A coder that turns out invalid gets ONE repair turn in the same session (`shared/coder-report.schema.json`: `{status: "ok", dod: [{id, met, evidence}], notes}` or `{status: "refused", reason}`).

**The result** (`result.json`, the same JSON as `role_status`'s `result`): `jobId`, `baseSha`, `headSha`, `branch`, `schemaValid`, `fixTurns`, `filesChanged` (`[{status, path}]`, taken from `git diff --name-status <baseSha> <headSha>` and from nothing else), `report` (the coder's claim, or null), `mainCheckout` (`{taken, changed, changes, note}`: evidence about the folder the job was started in, never a verdict) and, once the checks ran, `green`, `checksState`, `checksNote`, `format`, `checks` (the last round's steps: `{project, step, argv, exitCode, durationMs, logRef}`), `rounds` (`{turn, headSha, state}` of every round), `fixRounds` (`{cap, note}`) and `lastDigest`. `green` is the checks' word. A report with every `met: true` never makes a job green.

**`.coai/toolchain.yaml`** is the repository's checks (`shared/toolchain.schema.json`; this repository's own file is the example). Keys: `version: 1`; `projects[]` with `path`, `adapter` (`dotnet`, `node`, `php`, `rust`, `python`, `java`), `build` and `test` (an argv, one item to a line), `memoryMb`; `skip` (`restore`, `lint`, `format`, `formatCheck`, `audit`, `coverage`; never `build` or `test`); `timeouts` (`build`, `test`, a whole number and `s`, `m` or `h`; defaults 15m and 30m). It is read by the strict YAML subset reader, which refuses a flow list other than `[]` and every flow map. An argv starts with no shell, from the repository root, with a minimal environment; `{jobs}` is the number of build workers. The file is read at the BASE commit, so a coder cannot change its own checks: a change it makes is reported in `checksNote` and does not run for that job. No file is `not_configured`; a file that is there and wrong is `toolchain_refused`. Neither is ever green.

**The fix loop.** After each turn coai commits, then runs the base toolchain's build and test on that exact commit. A red step sends a digest of the failure (at most 40 items and 16 KB: MSBuild errors, one item per failed test of the test executable, else the last raw lines) to the SAME session of the same vendor: claude `--resume <session of turn 0>`, codex `exec resume <thread of turn 0>`, agy `--conversation <id of turn 0>`. A handle of another vendor, job or worktree is refused before a process starts. A fix turn that finds its session lost is a `crash`. At most `COAI_FIX_ROUNDS` fix turns (default 3; a whole number from 0 to 10, anything else is 3 and `fixRounds.note` says so). The whole job stays inside `timeoutMinutes` (default 30). A model that stops with a limit, a refusal or a crash in any turn hands the worktree, put back to the last coai commit, to the next model of the route, from turn 0 in a new session; the next model may be of another vendor. The fix-turn count belongs to the job, not to the model. When the job leaves a vendor, what that vendor's sign-in left in the job's home is deleted before the next vendor starts, and a copy that cannot be deleted ends the job `coder-boundary`.
**Stop words** (`role_status` `attempts[].stop`, and the job's `stop`): `not-green` (red after the last fix turn), `not-configured`, `toolchain-refused`, `instructions-failed` (a step that the repository declares could not run, see *The instruction step*; no model was launched), `checks-changed-tree` (the tree is not what coai committed and expected, so what the checks said is not trusted: the checks changed it, or the worktree's `.coai/toolchain.yaml` — read by coai every round — is a FIFO or cannot be read; never sent to the coder as a fix and never fails over), `coder-boundary` (the OS layer stopped a write or refused to start, a preflight row was bad, a launch check failed or a turn check found a stream that is not what was measured), `timeout`, `output-invalid`, `cancelled`; `limit`, `refusal` and `crash` fail over. Not built here: a re-run of a test that failed once, a stop on a repeated failure and test selection from the changed files (every round runs the whole suite).

**Which vendors can be a coder.** One table decides: `shared/coder-boundary-releases.json`. Only `CoderBoundaryTable` and `CoderVendorSet` read it.

- claude is not in the table. claude is always launchable.
- Another vendor is launchable only when its verdict is `ship` AND its row has the mark `launchable: true`.
- The owner set this rule on 2026-11-26. The mark stays off until the must-changes of the judgement are built and green. ONE commit turns the mark on. Both marks are on for codex and for agy.
- For each vendor the table holds the ids of the properties that its verdict relies on. Each id has a class: `pre`, `launch`, `turn` or `release`.
- The table also holds the history of the measures, in the key `measured`. A record has the release, the date, the headings of the results and of the judgement, and the sha256 of the programs.
- The history is a record only. Nothing compares an installed release or hash with it.

**No version of a CLI gates anything.** The owner decided this on 2026-11-26, for claude, codex and agy alike.

- An older, a newer and an unnamed release of a CLI is launched like the measured one.
- No release refuses a job, stops it, asks for a new measure or a new judgement, or needs a doc to change.
- A person sees the installed release beside the last measured one in two places. One is `cli {vendor, installed, lastMeasured}` in the coder envelope. The other is `cliVersion` and `lastMeasuredVersion` of an attempt in `role_status`.
- What still refuses a coder asks the installed CLI what it DOES. It never asks which release it is. There are five checks:
  1. The verdict and the mark of the table.
  2. A flag of the argv that the help of the installed CLI does not declare. The refusal names the flag.
  3. A preflight row. It is a probe with no model, in the exact launch unit, with one row for each id of class `pre`. A bad row ends the job `coder-boundary` and names the row.
  4. A launch check, made before every launch. It looks at the sign-in copy and its margin, the files of the vendor in its home and, for agy, the guard folder.
  5. A turn check, made after every turn on the stream of the CLI itself. It runs before anything of the turn is committed.
- One of these checks may stop a job after a CLI has updated itself. The owner accepted that as protection. It is not a version gate.

**The codex coder.** The verdict is `ship`. It was measured on `codex-cli 0.171.0` (the rows B1 to B15 on 2026-11-25, the open rows on 2026-11-26). History: `v2/spikes/RESULTS_codex_coder.md`. Judgement: section 13 of `v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`.

- *Argv.* The same flags on every turn: `codex exec --json -m <model>`, the measured `-c` keys and `--disable` switches, `-c approval_policy="never"`, `-c web_search="disabled"`, `-c agents.enabled=false` and `-o <file>`. A fix turn is `codex exec resume <thread of turn 0>` with the same flags.
- *Unit.* The unit hides the main checkout and the coder root with an empty tmpfs. The worktree is bound back. Besides the worktree, only `<job>/coder/codex` and a `/var/tmp` of the job are writable. `/tmp` is an empty tmpfs.
- *Sign-in.* The job's home holds a COPY of `~/.codex/auth.json`. A placeholder takes the place of the refresh token. Before every launch the access token must live at least as long as the unit plus 60 minutes. The refresh address is set to a local address that refuses connections. A preflight row asks that.
- *Commands of the model.* They run in the vendor's own sandbox (`codex sandbox`, profile `coai`). A preflight row tries it before every job.
- *Turn check.* After every turn coai reads the stream and the rollout file of the thread. These end the job `coder-boundary`: an item type that nobody measured, a web search, a call of an agent, and a thread that is not the handle's.
- *Measured limits.*
  - For a command of the model, the vendor's sandbox is the ONE layer between the command and the sign-in copy (C1).
  - The codex process itself has the whole network and the loopback of the host (C2).
  - The JavaScript that the model writes runs outside that sandbox. It runs in a process that can read the copy and has the network (C5).
  - What an agent does is not visible in the stream (C7).
  - The `AGENTS.md` of the repository reaches the model (C4).

**The agy coder.** The verdict is `ship`, by the owner's decision. It was measured on `agy 1.5.18` on 2026-11-26. The rows of 1.5.17 are of 2026-11-25 and of a program that is no longer installed. History: `v2/spikes/RESULTS_agy_coder.md`. Judgement: section 14 of the file named above.

- *Argv.* The flag set AG1 on every turn: `--print=`, `--input-format stream-json`, `--output-format stream-json`, `--mode accept-edits`, `--agent coai-coder`, `--model` and `--add-dir <worktree>`. A fix turn adds `--conversation <id of turn 0>`.
- *Unit.* The unit starts in a GUARD folder of the job. That folder is bound read-only. The unit never starts in the worktree. It hides the main checkout and the coder root. Besides the worktree, only `<job>/coder/agy` is writable.
- *Guard.* The agent file lists three tools: `read_file`, `write_file` and `edit_file`. It lists no command tool. The hook of the guard is coai's own program (`coai-mcp --agy-hook`). For every tool call it answers with an explicit decision. It allows a write only inside the worktree, and not under `.git`, `.agents` or `.codex` of it. It records each decision with the conversation id and the step index.
- *Turn check.* After every turn coai compares the records with the tool events of the stream. A tool that is not one of the three ends the job `coder-boundary`. So does an event with no record. So do the signs that the mode did not hold.
- *Sign-in.* The job's home holds a COPY with the access token only. Before every launch the access token must live at least as long as the unit plus 5 minutes. When the real file has no such margin, a refresh runs. It runs in a unit of coai's own, never in the coder's.
- *Measured limits.*
  - For agy the OS layer does not keep a command from the sign-in copy (A1). Only the agent file, the guard and the mode keep a command from existing. They rest on the behaviour of agy.
  - The agy process has the whole network and the loopback of the host (A2).
  - The real tool list of a turn cannot be asked before a job (A3).
  - The hook of agy is open on "exit 0, no output". The handler of coai never answers so (A4).
  - The mode that a turn ran in cannot be read from the stream. Only the signs after the turn show it (A9).
  - An agy coder cannot edit `.git`, `.agents` or `.codex` of its worktree (A8).
  - The `.agents` rules of the repository steer the model (A7).

**The instruction step.** A repository may declare ONE command that coai runs before any model.

- *Declare.* Use the key `instructions` of `.coai/toolchain.yaml`: `argv`, `perFile` with `{file}`, and `timeoutSeconds` from 1 to 300 (60 when absent).
- *Run.* coai reads the key at the base commit. It runs the command once for the job, in a check unit: no network, the home of the job, no credential, the worktree at the base.
- *Output.* The standard output (UTF-8, at most 64 KiB, exit 0) goes into the prompt of turn 0 of EVERY vendor, claude too. It is one block under the heading "Run by coai before your turn". A fix turn does not repeat it.
- *Failure.* A step that cannot run ends the job `instructions-failed`. The causes are: a non-zero exit, a time out, more than 64 KiB, bytes that are not UTF-8 and a changed tree. No model is launched, and no vendor is tried. The envelope names the argv, the exit code, the size and the last lines of standard error.
- *No step.* A claude coder is sent no block. A codex or agy coder is sent a block that says that nothing was run.
- *Secrets.* A secret-shaped text in the output stops nothing. coai records the class and the line in the envelope and in one `instructions-secret` notice. It never records the value.
- *Envelope.* `instructions {declared, argv, exitCode, bytes, sha256, ms}` and `prompt {id, sha256}`. Each vendor has its own prompt: `coder` for claude, `coder.codex` and `coder-agy`. They differ in the lines about tools and about the instruction files.
- *This repository* declares no step. The output of its rule resolver for a task is larger than 64 KiB.

**The write boundary of the claude coder** has three layers, and its limits are measured, not assumed. (The first two layers below are claude's; the codex and agy coders have their own, described above. The other items below are common to every vendor.)

- *The launch flags* (`v2/spikes/RESULTS_coder_write_boundary.md`, set R11, claude 2.1.330): `--restricted`, `--tools Read,Edit,Write,Glob,Grep` (no shell), `--permission-mode acceptEdits`, `--permission-prompts none`, no MCP server, `--add-dir` and the cwd = the worktree. They refuse an edit or a read outside the worktree, a link out of it and `.git`. A shell is not in the tool list; with one, the flags would not confine it.
- *The OS layer.* The claude process itself runs in a named `systemd-run --user` unit (`CoderBoundary`): `ProtectSystem=strict`, `ProtectHome=tmpfs`, `TemporaryFileSystem=/run/user`, `/mnt` and `/dev/shm`, `InaccessiblePaths=-/run/WSL` (that stops WSL interop; hiding `/mnt` did not), `BindPaths` of the worktree only, `BindReadOnlyPaths` of the main checkout and of the worktree's `.git`, `NoNewPrivileges`, `PrivateUsers`, `MemoryMax` with `MemorySwapMax=0`, and a home of the coder's own. One file of the real home is bound read-only, and only when the job has no pool key: `~/.claude/.credentials.json` (with a pool key it is not bound; see *The claude key pool*). A write the OS refuses ends the job as `coder-boundary` (never green, never failed over). The layer stops WRITES: a read of any file the unit can see is not stopped. For the real CLI the outcome does not say "stopped", because the CLI reports a failed tool in its transcript, not on stderr; the checkout is still unwritten.
- *What a unit can write, the host reads once, by one road* (`UnitWrittenFile`): the sign-in copy, the agy guard's records, a codex rollout, `report.txt`, the worktree's toolchain file. A command in a unit can replace any of them by a FIFO (a plain open waits for ever), a link or a huge file, so the host opens them without waiting, never through a final link, only as a regular file, within a size limit and a time bound; one that is not a regular file ends the job (`coder-boundary`, or `checks-changed-tree` for the toolchain file, which a check can also make). Every vendor's turn check has a fixed 30-second limit and answers `coder-boundary` when it does not finish.
- *What the unit of agy and of codex can read of the person's home.* The program of the vendor, and coai's own program as agy's hook handler, are bound read-only by their WHOLE folder. Where that folder is wide (`~/bin`, `~/.local/bin`), everything in it is shown to the unit — and for codex its shell can read it. A program that lies directly in the home is refused; keep the program in a narrow folder of its own (the measured codex install is `~/.codex/packages/standalone/releases/<release>/bin`).
- *The check boundary* (`CheckBoundary`): every build and test step runs in its own unit with the same properties, plus `Nice=10`, the job's own `HOME`, `TMPDIR`, NuGet and npm folders under the job folder, and a clean environment (`env -i` with an allow-list). The test step has `PrivateNetwork=yes`; the build step has the host network, because this repository's build restores and audits NuGet online and failed in 8 s with only a loopback device. Both choices and their reasons are in every step's record. A preflight (a probe of each property: a write to the worktree works; a write to the main checkout, to `.git` and to the real home is refused; no user manager and no sockets inside; a Windows `.exe` does not run) must pass before a step starts, else the job ends `coder-boundary`.

**Submodules.** git leaves an empty folder where a submodule is, so a repository whose rules or tests live in one (this one: `.agents/conventions`) could not be green in a coder worktree. coai provides each submodule's content AT THE BASE COMMIT, offline: it reads `.gitmodules` and the gitlinks of the base, finds the submodule's own git folder in the main checkout (`.git/modules`, never its working copy), writes the pinned tree into a folder of its own beside the job folder (`<job folder>.mounts/m<n>`; no fetch, no URL read, global and system git config off), and binds it READ-ONLY onto the worktree's empty folder in every check unit and in the coder's unit, one more `BindReadOnlyPaths=<source>:<target>` and nothing else. The worktree itself is never written, a submodule that cannot be provided (never initialised in the main checkout, an unsafe path, a link in its tree) is named in the job's log and record and the job goes on without it, the preflight has a `submodules-readonly` row only for a job with a mount, and a coder's diff that touches `.gitmodules` or a submodule's path is refused (`coder-commit`, nothing committed). The properties of every coder launch's unit are kept in `attempts/<n>/unit-<part>.json` (names and non-secret values; an environment or credential property by name only).

**Platforms.** The boundary exists on Linux and WSL only (it needs a systemd user manager and `systemd-run --user`). Native Windows and macOS have no OS write boundary and no write probe: a coder job is refused there before any process starts, with a sentence that says to run it in WSL (Windows) or on Linux with a systemd user manager (macOS). Not measured: a Linux host that is not WSL, a cold NuGet cache, what the CLI does when the sign-in token expires inside the unit, and the peak memory of a check step.

**The plan gate.** A coder job is bound to the approved plan revision of the feature it was started from. `review_code` on `coder/<jobId>` is accepted without its own plan round when the job's `job.json` (the authority, never the branch name) names a plan that proceeded, the task belongs to that revision and the base descends from its source commit. A branch called `coder/x` that no job of this data folder made is refused as before.

## The claude key pool (v2, epoch 3)

Role jobs do not run claude on the person’s own sign-in. They run it on a **pool of `claude setup-token` tokens** kept per device in `<data>/keys/claude/` (the coder first, then the BA and every role after them). The consultants, the gate reviewers and the Team server slots are unchanged. Spec: `v2/epoch-3/e2-coder-one-task/KEY_POOL_SPEC.md`; code notes: `research/module_server.md`.

- **Entry.** `coai-mcp --keys list|add|import|relabel|disable|enable|remove|scan|sync`. A token is ONE line on stdin, never an argument. Keys also come from `COAI_CLAUDE_KEY_<label>` variables and a keys file (`COAI_CLAUDE_KEYS_FILE`). The store is write-only: no verb gives a token back.
- **Round robin.** A fresh session takes the next usable key after a cursor kept in `pool.json`; a resumed turn keeps the key of its session. A limit puts the key in cooldown (the stated reset, else 30 minutes doubled, 5 hours at most) and the SAME model runs again on the next key, at most 3 switches per job. A refused key is `dead`. A key whose text appears in a model’s output or in the worktree is `dead (exposed)`, for good, and the job ends `key-in-output`; the mark waits up to a minute for the pool’s lock, and when it still cannot be written one `claude-key-exposed` notice says the key is still enabled and how to disable it. With no usable key nothing waits: the next vendor of the route runs, or the job fails `limit`; `start_role` refuses a claude-only route at once. `role_status` names the key of each attempt (`keyId`).
- **How a key reaches the child.** ONLY as the environment variable `CLAUDE_CODE_OAUTH_TOKEN`: never an argument, never a file the child can list, never a log line. For a read-only role it is in the child’s environment block. For the coder it is the FIRST LINE of the stdin of the `systemd-run --user` unit (see *The write boundary* above): the unit’s own shell reads and exports it, so the key is not a unit property, not in `systemctl --user show` and not in the transient unit file. One road for every secret: every other explicit variable of the launch (for example a row’s own `ANTHROPIC_API_KEY`) is one more line at the head of the same stdin, after the key, and the `systemd-run` client is started with an empty environment; only the proxy, certificate and time-zone names of coai’s own environment still cross by name. With a key the person’s `~/.claude/.credentials.json` is NOT bound into the unit, and the key folder is hidden in it.
- **No key at all.** `COAI_ROLE_CLAUDE_PERSON_SIGN_IN=1` lets a device whose pool holds no key (a disabled or dead key counts as a key) use the person’s own sign-in; the ledger then says `person-sign-in`.

## Install

1. **The server**: *ConnectOtherAIs: Install the MCP Server…* downloads the release asset for your
   platform into the extension's storage (never onto `PATH`), verifies its `sha256`, and puts the
   `mcpServers` block on your clipboard. Paste it into `~/.claude.json`, a project `.mcp.json`, or
   `.vscode/mcp.json`, and restart the client.
2. **The instructions**: *ConnectOtherAIs: Copy the CLAUDE.md snippet* gives the text that teaches a
   repository's main AI when to call the tools.

Codex, Antigravity and Claude authenticate themselves — if their CLIs are signed in, no key is
needed. DeepSeek
rides the Codex CLI's custom-provider config and needs a key, which comes from **one CredsForDevs
`config` entry** read once at startup. That is the only key path: a `credential` entry cannot serve
it, because nothing in the vault's read routes returns a secret. A **local** reviewer needs neither:
there is no CLI to sign in and no account to bill.

## A model on your own machine

*＋ Add a reviewer → Local model (Ollama / vLLM)* adds a row called `local` whose model dropdown is
what THIS machine has installed — each with its parameter size, quantisation and disk size, read from
the engine rather than from a list shipped here. Nothing found says where it looked and why, because
an empty dropdown with no reason is indistinguishable from "you have no models".

It is **not** the Codex CLI pointed at a local endpoint. That was tried first and it answers, but
codex's own system prompt is 21k tokens before any review content — measured — so a small-context
model is refused outright and a large one pays for a prompt unrelated to the review. A local reviewer
is a direct call: `coai-mcp --ask-local` POSTs to the engine's OpenAI-compatible endpoint with the
finding schema, `temperature` and `seed` pinned in the request, and prints the answer where the
executor already looks. It is a process like every other reviewer, so the timeouts, the kill, the
usage parsing and the unparseable handling are the ones that were already there.

Three things worth knowing before using one:

- **Two structured-output modes exist and only one works.** `response_format: {"type":"json_schema"}`
  returns well-formed findings; the weaker `json_object` answers with a shape it invented. There is
  no fallback to it, because a fallback would buy a full generation and an unusable round.
- **An endpoint that is not on this machine is announced in the row**, naming the host and saying
  that the plan, the diffs and the file contents around them are sent to it. `localhost`, `::1` and
  the whole 127.0.0.0/8 block are this machine, decided by PARSING the host — `127.0.0.1.evil.test`
  is somebody else's.
- **Tokens are counted, money is a dash.** The engine reports `prompt_tokens` and
  `completion_tokens`, so a local round appears in the spending chart with real numbers. Cost stays
  null rather than 0, because free and unpriced are different facts: what a local run costs is
  electricity and a busy card, and this product can see neither.

## Fast or Full: what a reviewer is given

Every reviewer runs in an **empty directory** by default — plan stage and code stage alike. The
diff, the plan and this project's written rules are assembled **by the server** and handed over in
the prompt; what changes between the two positions is only whether there is a repository to explore.

| | what the reviewer gets | when |
|---|---|---|
| **Fast** *(default)* | the composed prompt, in an empty directory | almost always |
| **Full** | the same prompt, plus one read-only checkout of the commit | when the meaning of a change depends on callers the diff does not show |

**Fast is the default because it was measured, not preferred.** On one commit, taking the checkout
away made every hosted model find MORE useful defects — Gemini 3.7 Flash 4→8, GPT-5.6-Luna
6→10, Claude Sonnet 5 6→7 — at a half to a third of the input tokens, with no wrong
finding from any of the three. Three real defects surfaced that no run WITH a checkout had reached.
A reviewer given a repository spends its attention deciding where to look; a reviewer given a diff
reads the diff. The evidence is in
[RESULTS_findings_that_are_worth_something.md](research/RESULTS_findings_that_are_worth_something.md).

The prompt is told which of the two it got, and this is not decoration. Eighteen of the twenty-five
prompt files used to open by claiming a read-only checkout was present — in Fast, the default, it was
not, and an agentic CLI that goes looking for one runs headless with nobody to grant it permission:
it refuses itself and returns nothing at all. Measured 2026-09-06 over 71 antigravity reviewer runs,
15 of them — 21 % — died exactly there. The one repair launch a reviewer is allowed is composed the
same way but always as no-checkout, because it always runs in an empty temp directory whatever the
review was given — promising it a tree was the same defect one layer down. And a prompt somebody
overrode in the catalog before this changed has the old sentence stripped as it is composed: their own
copy cannot know the mode either, and two opposite sentences in one prompt is worse than either.

The sentence comes from `PanelService`, which is the only
code that knows the mode — and the plan stage counts as no-checkout too, because its working
directory is an empty scratch. Both no-checkout cases say outright that there is no tool to call.

Full creates **one** detached `git worktree` pinned to a resolved SHA, outside your repository,
shared by every reviewer in that round: the main AI keeps editing while a review runs, and six
checkouts of a moving branch would be six different inputs to one comparison. Codex runs
`-s read-only --ephemeral`, Gemini `--approval-mode plan`. The tree is removed in a `finally`, and
an orphan from a killed session is pruned by the next `open`.

## Build and test

```bash
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe     # never `dotnet test` — MTP, no VSTest host
(cd src_vs_code && npm ci && npm test)   # subshell: the next line runs from the repo root
node .agents/conventions/tools/plan-lifecycle.mjs
```

Local runs are capped and lowered (the family rule `common.local-cpu-budget`):

- `npm test` and `test:contract` give `node --test` at most 8 workers (`min(8, CPUs - 1)`, so a small machine keeps
  node's own default) and lower themselves through
  `scripts/lower-priority.mjs`, a copy of the mount's template;
- each xUnit project reads `maxParallelThreads: 8` from its copied `xunit.runner.json`. The copy is skipped when
  `CI=true`, because an exact 8 would RAISE a 4-vCPU runner;
- the executables run through the mount's `low-priority.mjs`.

CI is exempt and unchanged. `localCpuBudget.test.mjs` pins all of it.

The process-level suite drives a scriptable **fake CLI**, so CI touches no vendor and needs no
network. The wire contract is checked against the built binary and, in the release workflow,
against the **published** one.

## Documentation

`research/architecture.md` is the entry point; `module_core`, `module_runners`, `module_server` and
`module_extension` deep-dive each half. Plans live in `todo/` while open and move to `research/`
with an `IMPLEMENTED` status when they ship — a rule this repository's CI enforces.

## Prompts: a universal question, and twenty narrow lenses

Each reviewer role ships a **universal** prompt and five narrow ones, and the panel can pick which
prompt each ROUND uses.

**Round 1 of every code role is the conventions pass**: it judges the diff against the rules this
project has written down — `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.claude/rules`, read from the
commit under review **by the server** and put in the prompt, so the pass works in Fast too — and
nothing else. A finding there must quote the sentence it
breaks; a convention the reviewer believes in but the project never wrote is not a finding. Pick
something else for round 1 and that wins.

**Dealing the lenses** is a switch per stage, off by default. Off, every vendor answers every
question and two vendors agreeing on a finding is a fact the gate can use. On, the round's prompts
are dealt out one per vendor: every lens gets asked once at half the launches, and that agreement is
gone. It is a real trade and the default is the conservative half of it.

| Role | Universal | Lenses |
|---|---|---|
| Plan | the whole plan | assumptions & verification · the human path · data loss & recovery · operability · scope & budget |
| Architecture | boundaries + evolution | boundaries & duplication · cost of the next change · coupling & knowledge · names & the shape they imply · testability of the seams |
| Security & reliability | the whole surface | what it holds and leaves · attack surface · blast radius · two at once · what this change trusts |
| Performance & UX-DX | both | cost at scale · ergonomics & waiting · the first run and the empty case · work done twice · what cannot be taken back |

**The last twelve were measured before they shipped**, and the measurement found something better
than a winning sentence. Each was drafted three times, and the three drafts turned out to be three
SHAPES held constant across all twelve — a question list, a task to enact, a rule with exceptions.
Seventy-two runs later ([RESULTS_focused_prompts.md](research/RESULTS_focused_prompts.md)): the
shapes find the same AMOUNT (6.6–6.9 findings, 79–82 % gating, flat) and differ in whether they find
the same thing TWICE — **42 % against 32 %**. So a lens here is written as a task to perform wherever
its subject has a sequence to enact, and as a question list only where it does not. Five of the
twelve picks were decided by that measurement; seven were inside its noise and took the shape result
as a prior, which the document says rather than presenting twelve winners.

**What is still not claimed.** Whether a lens finds what the universal prompt MISSES is a different
question, and the campaign that asks it compares the two arms over one real change. Over three plans
the union of all lenses found roughly twice what any single one did — and that result does not
survive its own control: the SAME prompt on the SAME text three times produced 6, 4 and 5 findings
whose overlaps were 3, 1 and **0**. Run-to-run variance alone explains the spread, which is exactly
why repeatability, not yield, is what the shape measurement was scored on.

The measurement that matters for a gate is a different one: a finding raised by two vendors
independently is stronger evidence than one raised twice by the same prompt, and every finding
carries the `providers` that raised it.

## What each AI has used

The server appends one line per reviewer to `usage.jsonl` — vendor, model, role, stage, seconds,
tokens, cost, outcome — and the panel charts it per vendor over a day, week, month or year.
Failed reviewers are recorded too: a run that burned ninety seconds and answered nothing is
exactly what a spending record must not hide. A vendor that does not price its own runs shows a
dash rather than `$0.00`, because free and unreported are different facts.

Token accounting is each vendor's own, because a shared rule is wrong for at least one of them:
codex folds cached tokens INTO its input count, claude reports them BESIDE it, and antigravity's
thinking tokens sit inside its output count. Claude reports one run twice and the two disagree —
`usage` is the last message, `modelUsage` is the session — and the ledger reads the second.

## The help, in the panel

The yellow **?** in the panel's title bar opens a searchable help page. It carries one article per
control and per setting, plus the machinery you cannot see from the panel — where a reviewer
actually runs, what happens when one fails, how a setting reaches the server, and what the audit
trail holds.

The first four articles are the first four things a person does: install the server, choose the
reviewers, tell your AI to use the gate, and set the gate itself. Search runs over the full text,
the language switch and the ± text size are real settings so they sync, and **The prompts, in
full** prints every prompt verbatim — held byte-for-byte against the server's own files by a test,
so the page cannot describe a question the product no longer asks.

Two tests keep it honest. One fails the build when a command or a setting has nothing written
about it — a new button either gets an article, or an alias naming the words the help uses, or a
written reason why it needs none; there is no fourth way and no silent default. The other holds the
printed prompts against the shipped ones.
