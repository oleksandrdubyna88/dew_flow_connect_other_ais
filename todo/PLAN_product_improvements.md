# PLAN — ConnectOtherAIs improvements for company-wide use

> Status: **the audit and plan of 2026-09-09 — NOT confirmed against the current code. Re-check each item against the code, and against the related plans that were carried out, before anyone acts on it.** The related build order for the audit's ten findings is [PLAN_the_audit_of_2026_09_09_is_built.md](PLAN_the_audit_of_2026_09_09_is_built.md) (partly built on 2026-09-11, seven stories shipped and five cut); it covers the findings of the audit, but it does not mention this six-direction backlog, so which items here are already built or superseded is not established.
>
> Original status line: **plan only, nothing implemented yet, 2026-09-09.** Scope: gate, Team server, VS Code extension, operations and checks.
>
> Translated from Russian (2026-11-05). Every item and every number is kept; the translation changes no content.

Goal: make review results trustworthy, make the shared service observable and
resilient to restarts, and make costs and policies manageable at the company level.

Source: [the product audit of 2026-09-09](REVIEW_product_audit_2026-09-09.md)
and the six directions chosen by the user. This is a separate backlog of improvements;
the status of each item has to be confirmed by an implementation and by checks. Fixes
for the specific bugs of the audit are accounted for in the matching direction.

## 1. Gate trustworthiness

Problem: `src_mcp/core/Rounds/RoundMachine.cs:117` forbids a gate verdict when zero
reviewers answered, but does not guarantee that every required role is covered.

- [ ] Define the required roles and the minimum number of independent vendors that answered.
- [ ] Show the requested models and the models that actually answered, with the reasons for any skips.
- [ ] Introduce an explicit "review incomplete" status in the MCP response and in the interface.
- [ ] Separate an incomplete review from the absence of findings; define the conditions
  for a re-run and for an explicit human decision.

Done when the failure of all reviewers of a required role does not look like a successful
complete review, and the user and the calling AI see the same coverage.
Checks: a partial timeout, an unavailable vendor, a disabled optional role,
a fully successful round, state recovery after reopening.

Related plan: [a skipped role reaches the AI's answer](../research/PLAN_a_skipped_role_reaches_the_ai.md).

## 2. Queue management

Problem: for a shared service it is not enough to know that the HTTP endpoint answers;
one must see why jobs are waiting and which accounts are actually working.
Entry point: `src_server/src/Jobs/JobPump.cs:14`.

- [ ] Show busy/ready/cooldown and the need to sign in again for accounts.
- [ ] Show waiting and running jobs, wait time and run time.
- [ ] Add cancellation with confirmation that the work actually stopped.
- [ ] Show vendor errors, queue/run latency and p95 with a clear measurement window.
- [ ] Separate personal access from administrative access; do not reveal other people's prompts.
- [ ] Signal when the queue handler stops making progress while HTTP keeps working.

Done when an operator can explain the delay of a specific job and stop
it, and free accounts are used. Checks: a busy first slot with a free
second one, cooldown, an error of one vendor, cancellation, the rights of an ordinary user.

## 3. Keeping jobs across a restart

Problem: `src_server/src/Jobs/JobStore.cs:41` keeps jobs and idempotency keys in memory.

- [ ] Keep accepted jobs, results and idempotency keys in durable storage.
- [ ] Persist the acceptance of a job before the reply to the client, and its state transitions.
- [ ] Restore the queue after a restart; explicitly finish or restore orphaned
  running jobs under an agreed policy.
- [ ] Rule out an uncontrolled repeat of an already paid external call on retry.
- [ ] Keep the ability to get a result by the earlier job id after a deploy.
- [ ] Define migrations, backup/restore, retention and a limit on the volume of data.

Done when a restart does not lose an accepted job and its known result, and the
undetermined outcome of an external call is explicitly visible. Checks: a stop after accept,
during the call, after the vendor's answer but before the result is saved, a repeat with the same
idempotency key, recovery from a backup.

## 4. Company policies

Problem: mandatory requirements and restrictions must be applied on the server,
independently of an employee's local settings.
Entry points: `src_server/src/CallerFilter.cs:25`, `src_server/src/Sessions.cs:117`.

- [ ] Centralize the mandatory checks and the allowed models/endpoints.
- [ ] Introduce budgets and fair-share rules for the shared queue.
- [ ] Define retention periods for prompts, results, evidence and usage aggregates.
- [ ] Add blocking of an employee and revocation of all of their sessions.
- [ ] Version the policy and keep its version with the review result.
- [ ] Show the reason for a refusal and the policy in force in the interface.

Done when a direct API call is subject to the same restrictions as the extension,
and a revoked session is not restored by a concurrent request.
Checks: a bypass through local settings/API, exceeding a budget, revocation during
parallel requests, a policy change during a job, deletion by retention.

## 5. Measuring the usefulness of models

Problem: the number of findings by itself does not show how useful a reviewer is.
Entry point of the statistics: `src_server/src/Usage/UsageEndpoints.cs:21`.

- [ ] Count unique accepted findings after de-duplication.
- [ ] Take rejected findings into account with their reasons; do not count every rejection as a
  proven false positive without classifying the reason.
- [ ] Show the cost of a useful finding and the review time by model and by role.
- [ ] Show the overlap of findings between vendors and repeated findings.
- [ ] State the sample size, the period and the incompleteness of the cost data.
- [ ] Separate review costs from chat and align the usage units between vendors.

Done when models can be compared on comparable samples, and an unknown
cost does not turn into a zero one. Checks: duplicates, several authors of one
finding, a changed decision, an unknown price, small samples, chat/review.

Related plans: [comparable usage](PLAN_usage_that_compares.md),
[usage by employee](PLAN_team_usage_by_person.md).

## 6. Code and tests

Goal: keep the split between core/runners/hosts and test the real boundaries of the system.
UI split point: `src_vs_code/src/panelProvider.ts:141`.

- [ ] Split the large panelProvider by responsibility: settings, Team servers,
  usage and presentation; keep the external behavior and the users' settings.
- [ ] Add a check of a full stdin pipe with timeout/cancellation.
- [ ] Check the stopping of a process tree with a real child process.
- [ ] Check simultaneously busy/free slots through a real JobRunner.
- [ ] Check rename+edit, Unicode and special characters in file names.
- [ ] Check that the selected model reaches the actual argv/request.
- [ ] Check restart/crash and the recovery of jobs, results and idempotency.
- [ ] Keep the contract/seam tests and add the necessary scenarios on the packaged
  extension and the published AOT binary.

Done when the tests reproduce the matching defects before the fix and pass
after it; a refactoring does not change the protocol or the users' behavior.

## Order and constraints

1. First close the confirmed risks of the audit; run the regression checks of item 6
   together with each fix, not at the end.
2. Gate trustworthiness and queue observability — items 1–2.
3. Durable storage and server policies — items 3–4.
4. Usefulness analytics — item 5; split panelProvider gradually.

Before implementing each direction, prepare a concrete technical plan,
re-check the current state and take into account the related plans that are already done.
For new stores/metrics, define in advance the expected number of users and jobs,
the volume estimate in bytes, the maximum size, the owner of cleanup and the behavior on a crash.
Numeric limits are not approved here: this is a backlog, not a finished storage design.

## Definition of Done

- [ ] Each direction is implemented, or its remaining scope is explicitly moved
  to a separate open plan; unfinished items are not marked as done.
- [ ] The listed behavioral and cross-component checks are done.
- [ ] The module documents and the descriptions of external contracts are updated.
- [ ] For changes on both sides, compatibility under separate updates is checked.
- [ ] After completion the plan is moved to research with the results and deviations.
