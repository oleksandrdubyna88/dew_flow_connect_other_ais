# ConnectOtherAIs: product audit, 2026-09-09

> Status: **the audit of 2026-09-09 — NOT confirmed against the current code. Re-check every finding against the code before anyone acts on it.** The ten plans that close this audit and their build order are in [PLAN_the_audit_of_2026_09_09_is_built.md](PLAN_the_audit_of_2026_09_09_is_built.md) (partly built on 2026-09-11, seven stories shipped and five cut by the operator), so some findings below are fixed or cut already; which ones is not established here. The six-direction backlog that was written from this audit is [PLAN_product_improvements.md](PLAN_product_improvements.md), which that build order does not mention. Byte-identical to `research/REVIEW_product_audit_2026-09-09.md` on main before this translation.
>
> Translated from Russian (2026-11-05). Every finding, number and path is kept; the translation changes no content.

The checkout `D:/rsd/dew_flow_connect_other_ais` was examined; the HEAD recorded during the audit
is `aa91a7a9e6b08aeaa58e68d937e47aaa77b66199`, extension `0.31.17`.
Scope: the shared Team server, the local MCP, the VS Code extension, process launching,
review-context collection, the queue, authorization and operational limits.

This is an audit of the sources plus local reproductions on synthetic data. A production
server, real credentials, employees' prompts and paid model calls were not
used. The product sources were not changed. The production state described in the
deploy documentation was not confirmed separately.

By the end of the audit, parallel edits to chatPage, its tests, the changelog and the
documentation appeared in the working tree. The author of the audit did not make them. The test
results given here refer to the state built during the check and do not
confirm those late UI changes.

Priorities: **P1** — fix in the nearest stabilization cycle, because an incorrect review,
a loss of availability or a violation of a trust boundary is possible; **P2** — the next
cycle. The presence of a P1 does not mean that an incident has already happened. There is no proven P0 in this
check; this is not a guarantee that there are no other vulnerabilities.

**1. P1, security: the shared server does not isolate file data between jobs.**

Basis: `src_server/src/Jobs/ReviewLauncher.cs:89` creates a temporary cwd, but
`src_mcp/runners/Processes/ProcessLauncher.cs:66` starts an ordinary child process
under the same user. On lines 93–95 the environment is extended, not replaced
by an allowed list. `src_server/src/Slots/SlotEnvironment.cs:29` changes HOME/XDG,
but that is a choice of the configuration directory, not a ban on reading the other directories.
`deploy/README.md:48` describes running the service as root.

A job comes from an ordinary authorized employee and contains an arbitrary
prompt. A CLI with read tools works in the same file space where the
accounts' credentials, the CLI history and the data of other jobs are kept. With the described
root deployment the consequences are wider than CoAI itself. The plan/read-only mode by itself
does not establish a confidentiality boundary between users: this is consistent with the
[description of Claude permissions](https://code.claude.com/docs/en/permissions) and the
[description of Codex read-only](https://github.com/openai/codex/blob/main/codex-rs/prompts/templates/permissions/sandbox_mode/read_only.md).

Status: the absence of such a boundary in the launcher is confirmed; the access of a specific
production CLI to a specific file was not checked. An additional system
restriction on the live host may change the assessment.

Fix: a separate unprivileged service user, then isolation of the
worker/job with explicitly allowed mounts, network and environment variables. For a job
that needs only the text it was given, disable the file/command tools. Separate the
credentials store from the context available to the model; do not give the shared CLI history
to all jobs. Check the boundary with a test using a synthetic file of a neighboring job.

**2. P1, availability/security: cancelled jobs allow prompts to accumulate in RAM.**

`src_server/src/Jobs/JobStore.cs:124` limits only the number of Queued. Cancel
on lines 294–308 leaves the JobRecord, including the Prompt; the removal of terminal jobs
on lines 338–349 happens after an hour. There is no limit on the total bytes or on terminal records.
`deploy/nginx/coai:51` allows a body of up to 4 MB.

Reproduction: queue limit 20, 40 sequential Submit+Cancel calls with different
prompts of 1 MiB each. After Sweep, 40 records and 40 MiB of prompt characters remained.
ASCII text in a .NET string takes about twice as much memory, not counting other
objects. All the requests can be made sequentially without exceeding the queued-cap.
The HTTP rate limit slows the accumulation down, but does not limit its size within an hour.

Consequences: an authorized client, including a faulty one, can exhaust the RAM
of the shared server. MemoryMax limits the damage to neighboring services, but does not keep
CoAI available.

Fix: limit the size of a prompt and the total bytes of accepted jobs; release the
Prompt immediately after completion/cancellation; keep the necessary history outside RAM;
keep compact idempotency records separately. Introduce a common admission limit.
Also limit the volume of stdout/stderr: the launcher currently accumulates them with no ceiling
in a StringBuilder (`ProcessLauncher.cs:99`).

**3. P1, reliability: timeout and cancellation do not cover sending stdin.**

`src_mcp/runners/Processes/ProcessLauncher.cs:138` first awaits WriteStdInAsync,
and only on lines 140–144 starts the timeout/wait with cancellation. WriteAsync itself
on line 184 does not receive a CancellationToken.

Reproduction: `/bin/sleep 3`, which does not read stdin, a prompt of 1 MiB, a timeout of
100 ms and a cancellation after 100 ms. The call finished after **3021 ms**, and
`TimedOut=False`. A CLI that lives forever and does not read would hold the call and the slot.

Fix: one deadline from the start of the operation, the stdin write and the wait for exit
under the same cancellation; a common finally guarantees that the process tree is ended
on any error. A regression test with a filled pipe is needed, not only sleep
with an empty stdin.

**4. P1, correctness: review loses the diff of renames and of Unicode names.**

`src_mcp/runners/Context/ContextAssembler.cs:53` parses the human-readable
`git diff --numstat` through Split, then on line 71 passes the third column as an
ordinary pathspec. Git prints a rename as `old.cs => new.cs`, and Unicode
with the standard core.quotePath as a quoted string with escape sequences.
These are not the real file names.

Reproduction on a separate git repository: a file renamed with a change of
behavior; a second changed file is called `файл.cs`. An ordinary git diff contains the
changes, but ContextAssembler returns **Text="" for both**. For Fast mode
this directly deprives the reviewer of the content of the change, without an explicit error.

Fix: the machine format `-z` with correct parsing of the old/new path, or one
structurally parsed patch. Checks: rename+edit, Unicode, quotes, tabs,
newlines in a name, and delete. Additionally remove the separate git process for
each file: now N files take on the order of N git calls.

**5. P1, performance: the additional account slots sit idle.**

`src_server/src/Jobs/JobRunner.cs:54` selects only one least-recently-used slot.
On lines 63–68 a failed Acquire ends the attempt; the other slots are not tried.
`AccountSlot.IsReady` does not take being busy into account, and LastUsed is updated on release
(`SlotRegistry.cs:311`). While the chosen slot is busy, it stays the first candidate.

Reproduction of the real JobRunner/SlotRegistry, without a vendor CLI: two ready
accounts, two queued jobs; the first job is blocked by a fake launcher. The second
PumpAsync returns false: **launched=1, queued=1 with two slots**.

Fix: atomically select and acquire the first available slot, trying the
candidates when a lock is busy; test two truly simultaneous launches
through JobRunner, not only two independent TryClaim calls.

**6. P1, availability: an error before the launch can stop JobPump forever.**

`src_server/src/Jobs/JobPump.cs:111` gets the launch task, but then on line 118
awaits only started.Task. If PumpAsync throws an exception before TrySetResult,
the continuation writes the error to the log, but started never completes. The main loop
then does not even reach Sweep and the other vendors.

Reproduction: instead of the file `.lock` of a synthetic slot there is a directory.
Acquire throws UnauthorizedAccessException; the launch task is faulted,
**startSignalCompleted=False**. A similar class is a file error before the signal.
`/api/health` meanwhile checks only that the HTTP endpoint works.

Fix: a single awaitable launch outcome, or a mandatory completion of
started on every exit, including exception/cancellation. A separate test:
a faulty slot of one vendor does not stop the next vendor. Add
observation of the pump's progress to readiness/metrics.

**7. P1, functionality: the model selected for local chat does not reach the launch.**

`src_vs_code/src/cliChatLaunch.ts:176` passes only resume to the adapter. The field
vendor.model is used neither in argv nor in any other launch parameter.
`agyAdapter.ts`, `claudeAdapter.ts`, `codexAdapter.ts` do not add the selected model.

Reproduction through the compiled launchSpecFor: for each of the three runtimes,
replacing `selected-model-A` with `selected-model-B` gives a **completely identical launch**.
The choice in the UI does not control the model — it is determined by the CLI's own default/config.
Remote chat passes model, and this finding does not apply to it.

Fix: a typed launch context with model, runtime, resume, permissions;
vendor-specific model flags and a check of the model actually selected from init/usage,
if the vendor reports it. A test "changed the model → the real argv changed".

**8. P2, performance/reliability: closing a chat leaves child processes on Unix.**

`src_vs_code/src/processLauncher.ts:373` on Linux/macOS goes to child.kill(),
which ends only the immediate process. The same applies to the native Windows
branch without a shell: a tree-kill does not happen there either.

Reproduction in WSL: our own node child starts `/bin/sleep 3`; after
handle.kill the parent ends, but the **grandchild stays alive after 100 ms**.
All the synthetic processes ended by themselves at the three-second limit. The behavior
of the native Windows branch was not reproduced separately.

Fix: a managed process group on Unix and a correct tree/job-object
model on Windows; wait for the actual termination. A test with a nested process instead of
a mock that confirms only the fact of a kill call.

**9. P1 when Google is enabled, security: authorization without an audience is allowed.**

`src_server/src/Auth.cs:71` sets ValidateAudience=false when the
Google Audiences are empty. `Startup.Guard` requires an audience for Microsoft, but not for Google.
A local check of the settings confirmed: Google enabled + [] passes startup,
and the JWT bearer options have **ValidateAudience=False**.

Then a valid Google token of an employee, issued to another application, may be
accepted by this service. The signature, the expiry and the allowed email domain remain checked;
this does not mean a login by an arbitrary anonymous user. The finding does not apply to a
Microsoft-only deployment.

Fix: a mandatory non-empty allowlist of audiences for every external IdP
and a test with a token of another application. The requirement to check aud is described in
[Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect).

**10. P2, performance: the cost of the statistics grows with the whole history of the company.**

`src_server/src/Usage/UsageReader.cs:75` rereads and deserializes the entire JSONL
before filtering by date. `/api/usage?scope=me` filters by employee even later.
The extension refreshes the Team server about once a minute; the local readUsage
(`panelProvider.ts:1993`) also reads the whole file every time.

Measurement on a synthetic history, after warm-up: 1000 old records —
4–5 ms; 100 000 — **284–303 ms**, although both "for today" selections return 0.
This is a local microbenchmark, not a measurement of production latency. The complexity of one
request is O(the whole history), and the total load also depends on the number of open clients.

Fix: an indexable store/aggregates by day, employee, vendor and model;
incremental reading by offset and caching where JSONL is enough. Introduce
retention for prompts, CLI history and evidence separately from the long-term aggregates.

**What to improve in the product after stabilization.**

1. **Show the completeness of a review.** Now RoundMachine.cs:117 forbids proceed
   only when zero reviewers answered; one answer is enough to check the gate by
   findings. This is the existing policy, not a new regression bug. For a company
   it is worth introducing mandatory roles/a minimum number of independent vendors that answered and a separate
   status "review incomplete". Separately decide whether Blocking may fit within the
   ordinary numeric threshold or always requires a human decision.
2. **Make the review result verifiable.** Keep the SHA, the base SHA, the hash of the scope/diff,
   the configuration, the actual models, the completeness of the checks and the decisions. Use this
   for a PR check/CI and invalidate the result when the reviewed content changes.
   The MCP protocol by itself does not forbid a user/agent to bypass a gate call.
3. **A shared operations panel.** A visible queue and running jobs, the age of the oldest
   task, busy/ready/cooldown of each account, the ability to cancel,
   queue/run latency p50/p95, the number of timeouts and parse errors by vendor.
   This immediately exposes defects 5 and 6, which an ordinary health endpoint hides.
4. **Survive a deploy/restart.** Now the jobs and their idempotency keys are kept in
   memory, and a lost job is recognized through an epoch. A shared service needs
   durable jobs/results/idempotency, recovery with an explicit interrupted state
   and a careful retry policy: an external call that has already been spent cannot be considered
   as never having existed after a restart.
5. **Company policies and costs.** Centralized allowlists of models and endpoints,
   daily budgets/queue fairness, settings for data retention and deletion,
   managed blocking of an employee and revocation of all of their sessions. The local choice of
   reviewers can stay convenient, but the mandatory checks must be set
   by the administrator on the server.
6. **Measure the usefulness of a reviewer.** Accepted/rejected with a reason, repeated findings,
   the share of unique useful findings, the cost and the time to resolution; show
   the denominator and the sample size. This is a more useful criterion for adding one more
   model than the total number of findings.
7. **Improve chat continuity.** Restoring a tab/draft, clear behavior
   when the model is changed, retry with the context kept. Limit a conversation by a budget of
   tokens/cost and an explicit summary instead of a single hard number of turns.

**What to improve in the code and the checks.**

- Keep the split into pure core/runners/hosts: it has already made it possible to reproduce
  the problems without paid vendors. Align the C# and TypeScript launch policy through
  a common set of contract vectors, including model, permissions, timeout and cancel.
- Separate the management of Team servers, usage and settings from the large panelProvider;
  use the existing shared URL-vector contract as a model for checking the seams.
- Move the long history of earlier fixes out of the comments into research;
  keep the invariants in force next to the code. There are outdated comments
  — for example, chatModels describes a refusal for the Team server which the code already supports.
- Complement the strong unit base with scenarios of real boundaries: busy slots, a file system
  failure before the start-signal, pipe backpressure, a grandchild process, rename/Unicode,
  model selection, a restart with an unfinished external request, session revocation at the same time
  as Validate. The last one deserves a separate check: Validate may write
  last-used after reading, while Revoke deletes the file without common synchronization.
- Do not substitute these unit tests for the extension host and the installed artifacts.
  Short canary scenarios on the packaged extension and the published AOT binary are needed,
  plus limited real vendor checks in a separate environment.

Order of work: first shared-server isolation, the memory limit, the stdin timeout and
the pump; then the lost diffs, the use of slots and model selection; then the process
tree, statistics and durable jobs. Raise the Google audience to the first stage if that
IdP is used. Redis/Kubernetes/a split into microservices are not needed to
eliminate the listed defects.

**Checks performed.**

| Check | Result |
|---|---|
| .NET solution build, Debug, WSL Ubuntu | 0 warnings, 0 errors |
| MCP test executable | 1113 passed, 0 failed |
| Team-server test executable | 226 passed, 0 failed |
| Extension npm test, Windows | 1020 passed, 0 failed, 1 skipped |
| Real TS client ↔ local Team server contract | 3 passed |
| Extension settings ↔ fresh MCP binary seam | passed |
| NuGet vulnerable packages, including transitive | Not detected by the current nuget.org advisory feed |
| Isolated probes | Confirmed findings 2–10; 9 — through startup/options |

The first npm test run in WSL did not reach the tests: the installed node_modules
contained a Windows-native TypeScript, without a linux-x64 compiler. The extension tests were then
built and run on Windows; the compiled JS was used for the local WSL
contract/probes. A green unit suite does not refute the reproductions shown.
The dependency audit does not cover the versions of the vendor CLIs, the OS and nginx
installed in production; an npm dev-toolchain advisory scan was not performed.

The local materials of the check are in the ignored directory
`artifacts/audit-20260909/`: `Program.cs`, `Audit.csproj`, `chat.cjs`, logs.
Reproduction: in WSL from the repository root run
`dotnet run --project artifacts/audit-20260909/Audit.csproj`, then
`node artifacts/audit-20260909/chat.cjs` after compiling the extension.
These are diagnostic programs that record the current behavior; they make no fixes
and do not replace the regression tests in the main suite.
