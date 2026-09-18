# PLAN — a deleted role stays deleted, and an interrupted deletion finishes itself

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope:
> `src_vs_code/src/rolesPanel.ts`, `src_vs_code/src/rolesEdit.ts`, a new `roleDeletion.ts` and its
> store, one callback on `mirrorSchedule.ts`, the roles page, and their tests.
>
> Defect 2 of the four in
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md) (its S7).
>
> Related: [module_extension.md](../research/module_extension.md), and `PLAN_the_mirror_says_when_it_stood_down.md`
> — named rather than linked, because defect 1 is still in review and the document is not on `main` yet.
>
> **Through its plan round, 2026-09-18: two reviewers, nine findings, all nine accepted.** The round
> changed the shape of this plan rather than its details, and the two that changed it most are marked
> *(round)* where they land: the four role-keyed settings must be pruned BEFORE the mirror runs rather
> than after, and the deletion must not call `sync()` itself at all.

## Who builds what

| Slice | Owner | Order |
|---|---|---|
| The ledger, the funnel, the panel count, the notifications page, the write gap | parent plan, S1–S5 | shipped 2026-09-17 |
| The rounds log's derived sections and its search | `PLAN_the_rounds_log_in_line.md`, S6 | shipped 2026-09-17 |
| The Help tab's refused write — defect 3 | shipped 2026-09-18 through `settingWrite.ts`, from a direction nobody planned | done |
| The settings mirror's silence — defect 1 | `PLAN_the_mirror_says_when_it_stood_down.md` | in review 2026-09-18 |
| **A role deleted before the deletion has landed — defect 2** | **this plan** | **now** |
| The server half and defect 4 | the parent plan, S8 | needs a `coai-mcp` release |

**This plan depends on defect 1**, and that is not a scheduling note: the whole correctness argument
below rests on somebody being able to ask *did the row actually reach the server*. Before 2026-09-18
`sync()` answered that question and four of its five answers were thrown away.

## The symptom

[rolesPanel.ts:328-329](../src_vs_code/src/rolesPanel.ts) is two lines:

```ts
  await write(outcome.rows);
  await forget(outcome.forget);
```

`write` puts the rows into `coai.roles` ([rolesPanel.ts:82](../src_vs_code/src/rolesPanel.ts));
`forget` deletes the prompt override files ([rolesPanel.ts:383](../src_vs_code/src/rolesPanel.ts)).
Nothing between them asks whether the row reached the file the server reads.

**When it does not**, the result is a role that still exists on the server with a prompt that has no
text, complaining once per round for ever
([PanelService.cs:1876](../src_mcp/src/Server/PanelService.cs)). That is the 2026-09-16 incident's
own shape: `Role2` was deleted, the mirror had stood down, and eleven code rounds ran against it.

Three more holes in the same operation, and they are why this is a story rather than an `if`:

1. **The four role-keyed settings are never pruned.** `removed()`
   ([rolesEdit.ts:95](../src_vs_code/src/rolesEdit.ts)) filters the row and names the prompt files.
   It says nothing about `coai.rounds`, `coai.thresholds`, `coai.roleEnabled` or
   `coai.promptsPerRound` ([settingsShape.ts:66-67,79,122](../src_vs_code/src/settingsShape.ts)),
   each a `Record` keyed by role id.
2. **The id is free again immediately.** `added()` takes the ids in use from the CURRENT rows alone —
   `idFor('', new Set(current.map((r) => r.id.toLowerCase())))`
   ([rolesEdit.ts:78](../src_vs_code/src/rolesEdit.ts)). So the next role named the same thing gets
   the same id and inherits a stranger's round budget, threshold and enabled flag.
3. **A crash anywhere in the middle leaves no evidence.** Every step is a separate write and nothing
   records that a deletion was in progress, so the next start cannot tell a half-done deletion from a
   role somebody simply has not deleted.

## What this builds

### 1. A tombstone, written and durable BEFORE the row is touched

The order is the whole of it. A tombstone written *after* the row buys nothing: a process that dies
between the row write and the tombstone leaves the next start with no evidence, the prompt files
orphaned and the id free — which is precisely the state this defect is about.

**One file per tombstone, `<dataDir>/deletions/<roleId>.json`**, written to a neighbour and renamed
over, cleared by unlink.

- **Not a VS Code setting.** A setting is mirrored to the server, so the tombstone would itself be
  waiting on the mirror it exists to survive. That is the regress, exactly.
- **Not the append-only ledger.** A tombstone must CLEAR, and nothing in that design is ever
  rewritten. A directory whose entries are created and removed matches the lifetime.
- **One file per id, not one index.** Two windows deleting two roles do not contend, and a file that
  fails to parse strands one deletion rather than all of them.

**The id is checked before it reaches a path** *(round)*. `promptFile` already refuses anything that
is not a slug, which is what keeps it inside the prompts directory; the same check guards this
directory, so a role id carrying `..`, a separator or a character Windows refuses cannot be written
at all. A role id that fails it is a bug elsewhere and is reported rather than sanitised into
something else — silently rewriting an id would make the tombstone about a different role.

**Contents**: the role id, its display name (so the page can name it without the row it just
deleted), the prompt ids, the instant it was asked for, the last reason it could not finish, and a
**nonce** *(round)* minted when it is written.

**The nonce is not decoration.** Two windows can be working the same deletion; one finishes and
clears the tombstone; the person then creates a new role that takes the freed id; the second window
resumes and prunes the NEW role's settings and files. Idempotence protects a repeated operation on
unchanged state — it does not protect a new thing that reuses an identity. So every destructive step
re-reads the tombstone first and stops if it is gone or carries a different nonce.

### 2. The order, and every step idempotent

| # | Step | Idempotent because |
|---|---|---|
| 1 | Write the tombstone | the same content rewritten is the same tombstone |
| 2 | Write the row AND prune the four role-keyed settings | both are already absent on a second pass |
| 3 | Wait until the mirror has carried it | the condition is read, never remembered |
| 4 | Delete the prompt override files | `rm(..., { force: true })`, already |
| 5 | Clear the tombstone | unlink of an absent file is a no-op |

**Step 2 prunes the four settings WITH the row, not after the mirror** *(round)*. They are part of
the payload the mirror writes, so pruning them after a successful sync leaves the server holding
orphaned keys until some unrelated setting changes — possibly for ever. They belong to the row and
they go with it.

**What waits for the mirror is only the TEXT.** The row and its four keyed records can be rewritten
by hand in a minute; the paragraphs of prose in a prompt cannot. That asymmetry is the whole reason
step 4 is on the far side of step 3.

### 3. Step 3: the deletion does NOT call `sync()` *(round — the largest change)*

The first draft had the deletion call `sync()` itself. That is wrong, and the way it is wrong is the
normal path rather than an edge case: writing `coai.roles` fires VS Code's configuration listener,
which starts the mirror's own schedule, so a direct call moments later answers **`busy`** — the
schedule holds the lock. The listener's sync then succeeds, the row really does reach the server, and
nothing resumes the cleanup. The person is left with a completed deletion that looks stranded, and
told to reload or to force it. Calling `busy` "another attempt" does not schedule that attempt.

So the deletion **subscribes** instead. `MirrorSchedule` gains one callback — told on every TERMINAL
outcome, whichever it is — and the deletion's coordinator resumes on each one.

**And the condition it resumes on is not "a write landed".** It is:

> a write landed, **and** the role is absent from the configuration as it reads right now.

That is what makes it correct in the face of five separate `config.update` calls in step 2, each of
which fires the listener: a sync that landed between the first and the last carried an incomplete
removal. Since `sync()` writes what the settings say at the moment it runs, a landed sync whose
configuration no longer mentions the role is proof the server has the whole of it. The condition is
re-read every time rather than remembered, which is also why step 3 is idempotent.

`stood-down`, and an exhausted `busy` or `failed`, record the reason on the tombstone and leave it
standing. Nothing is deleted, and the funnel says why — once per condition, per the ledger's own
rule.

### 4. A startup sweep

On activation, list `<dataDir>/deletions` and run steps 2–5 for each tombstone, then leave the
coordinator subscribed. A deletion interrupted by a crash, a reload or a closed laptop finishes
itself, and the person sees nothing because there is nothing to see.

**The sweep never removes a tombstone because its role is missing** *(round)*. That rule was in the
first draft's growth section and it contradicts step 2: by the time a deletion is interrupted, the
role is already out of the rows, so "the role no longer exists" describes every tombstone worth
keeping. A tombstone is unlinked by step 5 and by nothing else.

### 5. The id is not reusable while a tombstone stands

`added()` gains a second argument: the ids taken for reasons other than a row. The caller supplies
`rows ∪ tombstones`. Pure, and the test is one line.

### 6. A tombstone that cannot clear must not become a life sentence

In this plan's own scenario the mirror stays stood down until somebody reloads a window, so a
tombstone can outlive every reload and bar the person from ever recreating a role of that name.

The roles page therefore **shows** stranded tombstones — the name, when it was asked for, and the
reason the last attempt gave — with two actions:

- **Reload Window**, offered first when the reason was a stand-down, because that is the thing that
  actually ends one: a newer build owns the settings file, and reloading is how this window becomes
  that build.
- **Finish the deletion anyway**, which runs steps 4–5 and releases the id.

**And that second action costs something, which it must say** *(round)*. While the server still
carries the row, deleting the prompt text is the incident: rounds run the role, find no text, and
complain each time. The confirmation says exactly that — *the server may go on running this role
without its text until its settings catch up, and rounds against it will complain* — rather than the
softer "the server may still carry the row".

Keeping the files instead was considered and refused: it releases the id while leaving text on disk,
so the next role of that name opens with a stranger's prose, which is the defect `removed()`'s own
comment exists for. One of the two costs has to be paid, and the person choosing is the one who
should choose which.

**Stranded means: a tombstone that has failed at least once and is not being worked on now.** A
tombstone one second old is not stranded; showing it would be showing a person the inside of a write
that is about to succeed. **The clock is a parameter** *(round)*, like every other clock in this
repository, so the boundary is tested rather than slept through.

### 7. What is deliberately NOT touched

The per-role `RoleGate` inside session files. Sessions are history, `PanelConfig.Catalog` is
`[JsonIgnore]`, and the live catalog is reattached on load. **Verified on the incident's own data**:
the gate for `Role2` is still there and is not what kept it running.

## The shape

A new `roleDeletion.ts` that takes its world by parameter — read, write, remove, list, the clock, and
the mirror's terminal signal — so every step above is RUN by tests rather than read. `rolesPanel.ts`
supplies the host's own functions, the way `serverSettingsSync` is handed its reader and writer.

That is also what makes the crash cases testable at all: "kill the process between step 2 and step 3"
is, in a module whose steps are functions, "call step 2, throw the object away, call the sweep".

## Test plan

```bash
cd src_vs_code
npm ci        # first time, or after a rebase onto a main with new dependencies
npm test
```

| # | Test | Why it has teeth |
|---|---|---|
| 1 | A delete whose mirror answers `stood-down` leaves the prompt files ON DISK and the tombstone standing, and says why. | The defect itself. Asserting only "it reports" passes a build that deletes the text and then complains. |
| 2 | A delete the mirror carries prunes the row, all four role-keyed settings, the prompt files and the tombstone — and `unchanged` counts as carried. | A test using only `written` passes a build that strands every second deletion. |
| 3 | **The listener owns the sync and the deletion is told `busy`; the cleanup still completes** with no reactivation and no force. | The normal path, and the round's sharpest finding. A build that calls `sync()` itself passes every other test here and fails this one. |
| 4 | A landed sync BETWEEN two of step 2's writes does not complete the deletion; the next one does. | Pins the condition as *the role is absent from the configuration now*, not *a write landed*. |
| 5 | Interrupted between EVERY pair of steps, the sweep finishes it — four cases, one per gap. | One case passes a build that only resumes from the gap it was written for. |
| 6 | A second worker whose tombstone was cleared and whose id was recreated deletes NOTHING. | Two coordinators and controlled pauses — no second host needed. Without the nonce, the new role loses its settings and its text. |
| 7 | A tombstoned id is not handed to a new role; once the tombstone clears, it is. | Both directions, or "never reuse" passes a build that never releases an id at all. |
| 8 | A tombstone one second old is not stranded; one that has failed is — against an injected clock. | Both directions again, and no sleeping. |
| 9 | *Finish the deletion anyway* releases the id and states the cost in the words above. | The sentence IS the feature: a silent local cleanup is how somebody concludes the server agrees. |
| 10 | A role id that is not a slug is refused before any path is built, and reported. | The scan that forbids it must still find the sanctioned path, or it is checking nothing. |
| 11 | Every new assertion watched failing first, with its message reported. | `testing.md`, mandatory. |

**And a scenario in a real editor**, on the harness defect 1 added: delete a role and assert the row
leaves `settings.json`. The failure path stays out of the host for the reason recorded there.

## Growth surface

**No cap, a bound that is stated, and a surface that shows it** *(round)*.

The first draft claimed tombstones are bounded by the number of roles, which is false: while the
mirror is down a person can create and delete differently named roles all day, and each deletion
leaves another tombstone behind while removing its row. A workspace with zero roles can hold many
pending deletions.

The real numbers: a tombstone is one JSON object of about 300 bytes. A thousand outstanding deletions
is **300 KB** and a thousand small reads at startup — noticeable, not dangerous. A cap was considered
and refused: the only thing a cap can do is refuse a deletion, and refusing to delete a role because
the mirror is down is a worse outcome than a directory of small files.

What replaces it is visibility. Every outstanding deletion past its stranding boundary is ON the
roles page with its reason, so the accumulation is something a person can see and act on rather than
something that grows quietly. The steady state is empty.

## Definition of Done

- [ ] The prompt files are deleted only after the mirror has carried the removal; the row and the
      four role-keyed settings go together, before it.
- [ ] The tombstone is durable before the row is touched, carries a nonce checked before every
      destructive step, and a startup sweep finishes what a crash interrupted.
- [ ] The deletion never calls `sync()` itself; it resumes on the mirror's terminal signal, and its
      condition is that the role is absent from the configuration as it reads then.
- [ ] A tombstoned id cannot be handed to a new role.
- [ ] Stranded tombstones are shown with their reason, and can be finished with the cost stated in
      the words this plan uses.
- [ ] Each assertion watched failing first, and both observations reported.
- [ ] The whole suite green, and `notificationSites.test.mjs`'s population moves only with a reason
      written beside the constant.
- [ ] `research/module_extension.md` and `research/module_tests.md` record the deletion's steps and
      the new flow.
- [ ] **The parent plan's boundary table and its defect-2 section are updated in the same task**,
      per `common/planning-docs.md`.
- [ ] Promotion per `common/planning-docs.md`, `git mv` last, then `plan-lifecycle.mjs`.
- [ ] Through `review_plan` (done, nine of nine accepted) and `review_code`.

## What this will NOT prove

That the SERVER drops the role when its settings catch up — that is `module_server.md`, and the
extension's side of the contract ends at the file. And the concurrency test drives two coordinators
in one process; two real windows racing on one data directory is not exercised, only the interleaving
that makes the nonce necessary.
