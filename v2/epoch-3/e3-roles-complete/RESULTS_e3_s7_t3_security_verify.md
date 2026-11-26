# E3 s7 t3 "the instruction step" — security verify of `ca1acc05` (2026-11-26, epoch 3 E3)

Judge: Fable (security judgement role). Read-only for product code: no edit, no build, no test run, no model call, no CLI run.
Branch `feat/v2-ep3-e3-roles`, commit judged `ca1acc05` (diff `e8101102..ca1acc05`, 18 files). All paths and line numbers below are at `ca1acc05`
(read with `git show ca1acc05:<path>`; the worktree `/home/jinx/git/coai-v2-ep3e3` was not touched).

Read: the whole diff; `src_mcp/src/Server/RoleJobs/{CoderInstructions,CoderWork,CoderChecks,CoderEnvelope,RoleStops,RoleRunner}.cs`;
`src_mcp/core/Toolchain/{ToolchainParse,ToolchainModel,ToolchainTables,ToolchainFile,FailureDigest}.cs`; `src_mcp/core/Notices/Redaction.cs`;
`src_mcp/runners/Toolchain/{CheckBoundary,BoundaryUnit,BoundaryJob,ChecksAtBase,UnitControl}.cs`; `src_mcp/runners/Processes/{ProcessLauncher,BoundedText}.cs`;
`src_mcp/tests/{ACoderInstructionStepTests,AToolchainSchemaAgreesWithTheReaderTests,CoderRig}.cs`; `shared/toolchain.schema.json`; SPEC C9; the task file; `research/module_server.md`.
For context only (not judged): the next commit `3c101f27` (s7 t2), which writes the prompt block, and `BoundaryUnit.cs` / `ToolchainParse.cs` of `feat/v2-ep3-e2-coder`.

Run by the judge: one `systemd-run --user` unit on a made-up folder and a made-up listener (section 2); the rule resolver's `check` and `explain` (read-only, no model call).
The probe folder is removed and no probe unit is left. Not read: any key file, any environment.

Rules: `rules.mjs check` answered `resolved`; of the 33 selected rules only `common.security` was read (this file is written outside the repository).
The AI that commits this file loads the rest for its own change.

## 1. Verdict

**Must-change: 3 (M1, M2, M3), then READY — under condition C1.** Each fix is a few lines in a file of this task, with one test.

| # | What | Where |
|---|---|---|
| M1 | The step's output is kept and handed on for the prompt with no secret search | `CoderInstructions.cs:154-171` |
| M2 | A declared step is silently skipped, and a model is launched, when the toolchain file at the base is refused for any reason the parser does not count as a fault of the `instructions` key | `CoderInstructions.cs:103-112` |
| M3 | One new fault of the reader repeats a typed value | `ToolchainParse.cs:259` |
| C1 | Condition, not a change of this commit: the check unit of THIS branch is still the list from before the E2 follow-ups | `BoundaryUnit.cs:34-54` |

What this commit does and does not do: it runs the step, keeps the text and writes the envelope member. It does **not** put the text into a prompt: `CoderWork.cs:94` takes only the record,
and `CoderWork.cs:110` sends `CoderPrompt.Build(...)` as before. The block is written by `3c101f27` (s7 t2). Question (2) is therefore answered for what t3 hands over, with one look at t2's block.

## 2. What was measured

| # | Question | Answer |
|---|---|---|
| P1 | A unit with the check list (`ProtectSystem=strict`, `ProtectHome=tmpfs`, the three tmpfs, `PrivateUsers=yes`, the hardening, `PrivateNetwork=yes`), my folder bound read-only as a stand-in for "a path the unit sees". A made-up `nc -lU` listener outside the unit on a socket file in that folder | The step **reached it**: the listener outside received the line. `nc -U /run/dbus/system_bus_socket` from inside: **connected** (exit 0). TCP to the host loopback: closed. Interfaces: `lo` only. Whether the bus accepts a method call from a unit with `PrivateUsers=yes` was not measured |
| P2 | The resolver's payload for the files of this task, `--task implement` (`rules.mjs explain`) | 33 rules, 334,362 bytes: the number in `todo.md`, the YAML comment and `module_server.md` reproduces |
| P3 | `coder.md` | 2,628 bytes, sha256 `ee31db20…d7bf`: the plan's value (SPEC C7 item 1) |

Not measured, by reading only: everything about the .NET launcher (the decoder, the cap), since no build and no test run was allowed.

## 3. The six questions

### 3.1 The unit, the base commit, the network (question 1)

Holds:

- **Only declared argv.** The argv is `step.Argv` plus `step.PerFile` once for each file of the task (`ToolchainModel.cs:47-48`), read from the toolchain file AT THE BASE through the MAIN checkout
  (`CoderInstructions.cs:103`, `ChecksAtBase.cs:57-62`, `:66-90`), started with no shell through `/usr/bin/env -i` and the allow-list (`BoundaryUnit.cs:136-137`), with no extra environment (`CoderInstructions.cs:136`).
  A file name of the task is the one part that is not the repository's: it is refused when it is rooted, has `..`, starts with `-` or holds a control character (`:114-117`, `:197-198`), and it is one argv item, never split.
  A program name with `=` or a leading `-` is an `ArgumentException` and ends the job (`BoundaryUnit.cs:131-134`, `CoderInstructions.cs:138-141`). No source of this commit reads `AGENTS.md` or `CLAUDE.md` (grep of the three folders).
- **Before any model, once.** `CoderWork.cs:83` is after the worktree is made (`:64`, a fresh worktree at the base: a taken name is refused) and before `failover.RunAsync` (`:110`). One call for the job; a failover is inside `RunAsync`, so no later attempt runs it again
  and it never runs on a tree a coder wrote. The text is held in memory; the kept file is never read back, so nothing a check writes later can change what a second attempt is sent.
- **The same boundary as the checks, by construction.** The same `CheckBoundary`, the same `BoundaryUnit.Properties`, the admission by the same preflight (`CoderInstructions.cs:126`, `:136`). There is no second property list.
- **The kept text is out of reach of every unit.** `run.Folder` (`:205`) is the role job's record folder in the data directory, not `coder-jobs/<id>`; no unit binds it.
- **Network: none is the right state.** The step's output becomes prompt text, so a network would let it bring in text that is not of the base commit and send out what it can read. `PrivateNetwork=yes`, `lo` only (P1); the test `D2_OnARealUnit…` shows the host loopback closed.

Does not hold yet, or holds by distance:

- **C1.** See section 4.
- The network state is **borrowed from the test step** (S1).
- A private network does not close a **path socket** (P1, S2).
- "At the base commit" is assumed, not asserted (S3); an ignored file the step writes stays in the tree (S11).

### 3.2 The output on its way to a prompt (question 2)

- **Size.** 64 KiB of UTF-8 of the decoded text (`:89`, `:182`); the launcher keeps at most 4 x 64 Ki characters of each stream (`:136`) and a cut stream fails the step (`:181`). Nothing is cut. Holds.
- **Secret search: none. M1.**
- **Fencing (t2, `3c101f27`, `CoderPrompt.cs` `RanBlock`).** The block is the last thing of the prompt; the output is inside a fence one longer than its longest run of backticks, so it cannot close the fence and forge a heading; the argv is JSON. A test holds the fence.
  The block does NOT say "data, not instructions", and by the plan it must not: the output is the repository's rule text and the coder is meant to follow it (SPEC C7 item 4, C9 "what the step is not"). That is the trust of `AGENTS.md`, and it is only as true as M1 and C1 make it.
- **Control characters.** Not refused; only U+FFFD is (`:183`). The prompt goes to every CLI on stdin after the key line (`CoderBoundary.cs:59` at `3c101f27`), so a control character cannot move the key line. S4.
- **Logged.** One line with exit, size, hash and time (`:169`); a warning with a path when the file could not be kept (`:212`). The failure reason holds the exit code, the sizes, path names of a changed tree, and for a unit that did not start 300 redacted characters of the client's stderr.
  The envelope of a failed step holds 8 stderr lines of 300 characters, each through `Redaction.SafeText` (`:190-194`). stdout of a FAILED step is kept nowhere. Holds.

### 3.3 Fail closed (question 3)

| Path | Ends | Launches |
|---|---|---|
| the unit is refused by the preflight (`:126-130`) | `instructions-failed` | 0 |
| the unit did not start (`:178`), could not be stopped (`:177`), the program name is refused (`:138-141`) | `instructions-failed` | 0 |
| past its time (`:179`), exit not 0 (`:180`), a cut stream (`:181`), over 64 KiB (`:182`), U+FFFD (`:183`), tree or HEAD moved (`:184`) | `instructions-failed` | 0 |
| git cannot read HEAD or the status before the step (`:121-124`) | `instructions-failed` | 0 |
| a task file name that is not plain (`:114-117`) | `instructions-failed` | 0 |
| a fault the parser counts as the key's (`:104-107`) | `toolchain-refused` | 0 |
| a cancel while the step runs (`:143-146`) | stopped | 0 |
| any other exception of the run (`ProcessTreeNotRecordedException`; `CheckBoundary.cs:129`) | `runner-error` (`RoleRunner.cs:66-70`) | 0 (S7) |
| **the file at the base is refused for another reason, or git could not hand it over** (`:109-112`) | **goes on as "no step declared"** | **1 or more. M2** |

### 3.4 The reader (question 4)

- One optional top-level key, in the root table only (`ToolchainTables.cs:72`), closed map, `argv` required, `timeoutSeconds` 1 to 300 (`:93-96`). `version` stays 1. The argv rule of the reader (no credential-like text, the fault names the class) covers `argv` and `perFile` (`ToolchainParse.cs:251`, `:262` through `Command`, `:201`).
- **E4.** The E4 folder at this commit does not hold the word `instructions`; SPEC line 512 records the boundary (E4 s3 t1 adds four names of a PROJECT). No overlap.
- **Echo of values.** On this branch `Shown` still repeats a scalar (`ToolchainParse.cs:57`), so `instructions: <text>` and `timeoutSeconds: "<text>"` repeat the text (`:245`, `:279`), and `Keys` repeats an unknown key. All three inherit E2's fix
  (`37c899e6`, `916bbf9c`: kind and length) when the branches meet, because they call the same helpers. **One fault does not: M3.**

### 3.5 claude's prompt bytes with no step (question 5)

Holds at this commit by the absence of a change: no prompt file and no line of `CoderPrompt.cs` is in the diff, the bytes are the plan's (P3), and `CoderWork.cs:110` sends what it sent.
**No sha test exists at `ca1acc05`**; the tests that hold it are in `3c101f27` (`ACoderFixLoopThroughTheSeamTests`: `D5_TheShippedClaudePrompt_IsTheSameBytes`, `D5_TheThreeClaudePrompts_AreByteForByte…`, `D6_WithNoStep_ClaudeIsSentNoBlock…`). Read, not run.

### 3.6 The dod of t3, by reading the tests (question 6)

| dod | Verdict | Notes |
|---|---|---|
| d1 | **met, with two gaps** | The key, the three members, the default, `perFile`, the nine refusals with the line, the secret rule: `D1_*`. Read at the base: `D2_TheStepIsReadAtTheBaseCommit…`. Schema equals reader: keys, required, types, the range. Gaps: "coai never reads an instruction file" has no test (true by reading); the schema allows `argv: []` and `{file}` in `argv`, which the reader refuses (S10) |
| d2 | **met as worded** | Once, at the base, kept, hash only in the envelope, one run for two attempts, the real unit with a control. Caveats: the stand-in sign-in lies in a temp folder (`CoderRig.cs:38`, `:47-48`), so the row shows the `/tmp` cover and says nothing of a home outside the hidden folders (C1); "used again by a later attempt" is "not run again" here, the text reaches a prompt only in t2 |
| d3 | **met for the six causes named; not for "a declared step that cannot run", in full** | One test for each: exit, time, size, UTF-8, tree, unit. Each asserts zero launches, an empty ledger, `declared: true`, the argv, no `green`. Not covered: M2's path (a model IS launched), the arms `:177`, `:178`, `:138-141`, `:114-117` (S9) |
| d4 | **met** | The six members; `declared: false` with zeros and no `instructions/` folder; the canary is in no log line and not in the result. M2 makes `declared: false` untrue on its path |
| d5 | **met** | Measured first: 334,362 bytes for three tasks (reproduced, P2), in `stories/s7-vendor-seam-rows/todo.md`; the step is not declared, the file says why, a test reads the committed file; the lead decides |

## 4. Must-change

**M1. The output is kept on disk and handed on for the prompt of every vendor with no secret search.**

- Where: `CoderInstructions.cs:154-171` (`Judge`: `Keep(text, …)` at `:168`, `Ran(record, text, …)` at `:171`); `Why` (`:175-185`) has no arm for it. At `3c101f27` `CoderPrompt.RanBlock` sends `ran.Text` as it is.
- Why it is a defect and not a wish: the product already has the rule, twice. Text of a failed check goes back to a coder only through `Redaction.SafeSource` (`FailureDigest.cs:43`, `:120`, `:158`).
  Text that goes to a hosted model is REFUSED when the same passes find a secret (`Redaction.FirstSecretClass`, `Redaction.cs:336-359`: "the text goes to a hosted model and into the vendor's own store").
  The step's output is the one text of a repository-run program that reaches a vendor with neither. `common/security.md`: a measure applied at some of its sites.
  SPEC C9 calls the output "text of the repository at the base commit, as `AGENTS.md` is". Nothing makes that true: it is whatever the program prints, and the program reads everything the unit shows.
- Scenario: a step that prints context, with `git remote -v` or `git config -l` in it. The main checkout is bound read-only in the check unit (`BoundaryUnit.cs:42`), so `https://user:TOKEN@host/…` of its `.git/config` is printed.
  Or a tool that prints its request headers, or a rule file with a live token. Exit 0, under 64 KiB: the token is written to `instructions/stdout.txt` (kept until retention) and is sent to the vendor of EVERY model of the route,
  a codex and an agy coder included, whose own unit hides the main checkout. Nothing stops, nothing says so.
- Smallest fix: one arm in `Why`, before the tree arm, with the passes the product already has:
  `_ when Redaction.FirstSecretClass(text) is { Length: > 0 } kind => $"the step printed text that looks like a credential ({kind}); nothing of it is kept or sent"`. It ends `instructions-failed` before `Keep`, names the class and never the text.
  One test in the d3 group: a step that prints a made-up vendor-shaped token ends the job with zero launches, and the token is not in the reason, the envelope, the log or the job folder.
- The cost, said plainly: the `labelled` and `parameter` classes go by the NAME beside the value, so prose such as `password: must be twelve characters` in a rule text refuses the job. If the lead finds that too strict for rule text,
  the other measure the product has is `Redaction.SafeSource(text)` before the hash, the file and the block, with a `redacted` member in the envelope; that changes "nothing is cut" of C9 item 5 into "a credential is replaced and said", which is a plan change.
  Either is acceptable to this judgement. Sending the text unsearched is not.

**M2. A declared step is skipped and a model is launched when the file at the base is refused for a reason that is not a fault of the key.**

- Where: `CoderInstructions.cs:104` matches only `Refused { InstructionFaults.Count: > 0 }`; `:109-112` answers `None` for every other read that is not `Configured` with a declared step.
  `InstructionFaults` is filled only inside `ToolchainParse.Instructions` (`ToolchainParse.cs:220-236`). It is empty for: a refusal of the YAML reader (`ToolchainFile.cs:31`), a file over the size limit or not UTF-8 (`:21-22`),
  a base commit or a file git could not hand over (`ChecksAtBase.cs:61`), and a fault in another key (the test `D1_AFaultElsewhereInTheFile_IsNotAFaultOfTheInstructionsKey` asserts exactly that).
- Scenario 1, the likely one: a person declares the step as `argv: [node, tool.mjs]`. The strict subset has no flow list, so the YAML reader refuses the FILE before the parser runs. Scenario 2: the step is right and `adapter: cobol` is wrong.
  Scenario 3: git times out on `show`. In each the job runs nothing, launches the coder with write access to the worktree and without the repository's own command, commits its turn 0, and only then ends `toolchain-refused` at the checks (`CheckRunner.cs:55`).
  The envelope says `declared: false`, and a codex or an agy coder is told "this repository declares no `instructions` step" (`3c101f27`). Both are false. C9 item 5 and d3 say zero launches.
- Smallest fix: at `:104` every `ToolchainRead.Refused` ends the job `toolchain-refused` with `refused.Sentence`, before any launch. Nothing is lost: such a job cannot be green, the first check round refuses the same file.
  It also saves the paid turn. One test: a base file with a right `instructions` key and a fault elsewhere, and one with a flow list in `argv`: zero launches.
  (If an E2 test expects a turn 0 before `toolchain-refused`, it changes with this. A narrower fix, a `Declared`-or-unknown flag on `Refused`, keeps that turn and needs the raw text searched for the key when the YAML reader refused; the wide one is simpler and safer.)

**M3. The `{file}` fault repeats the argv item.**

- Where: `ToolchainParse.cs:259`: `… holds {file} ('{misplaced}'); …`.
- Scenario: `- "https://user:TOKEN@host/{file}"` or `- "--header=Authorization: Bearer TOKEN{file}"` in `argv`. `NoSecret` (`:207-213`) names the class and not the text, as E2 decided; the next fault prints the whole item.
  It goes into `refused.Sentence` and `InstructionFaults`, so into the job's reason (`CoderInstructions.cs:106`), `job.json`, the check note and the answer the main AI reads: a typed credential on its way to a vendor through an error message.
  The test `D1_ASecretInTheStepsArgv…` promises the opposite for the same key. This fault is new text, so the merge with E2's `Shown` does not repair it.
- Smallest fix: drop `('{misplaced}')`; the line number is already there. The theory row that asks for `{file}` in the sentence still passes. Add the secret-with-placeholder item to `D1_ASecretInTheStepsArgv…`.

## 5. Condition C1 — the unit on this branch is the old one

`0dc0f94c`, `35e6f584`, `964f037a` and the rest of the E2 follow-ups are not ancestors of `ca1acc05`. So here the step's unit binds the WHOLE job folder writable (`BoundaryUnit.cs:45`: the coder's home too, before the coder starts),
has no `InaccessiblePaths` for the key folder or the keys file and no sign-in row. With a data folder or a keys file outside `/home`, `/root`, `/mnt` and `/run/user`, a step can print a pool key, and its stdout is now a road to a vendor that needs no network.
The step calls the one property list, so it inherits `JobView`, `KeysHidden` and `SignInHidden` on the day the branches meet (s7 t5); nothing in `CoderInstructions.cs` has to change for that.

What must be true before a job with a step runs outside a test:

1. s7 t5 is in, and the step is shown on a real unit NOT to see `<job>/coder` and the key folder (one row beside `D2_OnARealUnit…`, with a stand-in outside the temp folder).
2. `SignInHidden` of E2 hides claude's file only. A codex and an agy sign-in at their real paths, and the per-job COPY of s7 t6, need the same before those vendors ship. The copy must be made after the step or lie under the cover: the step runs for every vendor.

## 6. Should

- **S1. The step borrows the test step's network** (`CoderInstructions.cs:126`, `:136`: `layer.Job.NetworkOf(CheckStep.Test)`). Today that is private. The day a repository may give its tests the host network, the step gets it in silence, and the record carries the test's reason.
  Give the step its own `NetworkChoice.None("the output of the step becomes prompt text")` and admit `CheckNetwork.Private`.
- **S2. A private network does not close path sockets (measured, P1).** Any socket file outside the hidden folders is reachable: the system bus here, a container daemon's socket on a machine where the person may use it. Inherited from E2's unit, not made by this commit;
  it matters more for a step whose output is a prompt. For the unit's owner: `InaccessiblePaths=-` for `/run/dbus`, `/run/docker.sock`, `/run/containerd`, `/run/podman`, and a preflight row that connects and must fail.
- **S3. "At the base" is not asserted.** `:120-124` only asks that git answered. `CoderChecks.cs:74-78` compares its head (`:75`); do the same: `before.Head == baseSha` and an empty status, else `instructions-failed`.
- **S4. Control characters pass.** Refuse every character under 32 but tab, LF and CR, and 127, as "not text" in `Why`. A person reads `stdout.txt` in a terminal; and if M1 uses `FirstSecretClass`, that search drops those characters first while the text sent keeps them.
- **S5. The decoder (by reading the runtime, not measured).** The launcher's reader is made by `Process` with byte-order-mark detection, so a UTF-16 or UTF-32 mark switches the decoder and a UTF-8 mark is dropped: such output is not "UTF-8" and passes.
  `bytes` and `sha256` are of the decoded text encoded again, not of the bytes printed. The text sent is the text hashed, so the vendor gets nothing unrecorded. Say it in the doc, or measure it in a test.
- **S6. The size sentence is said for three different facts.** `Truncated` is stdout or stderr or a pipe a background child held open (`ProcessLauncher.cs:588`, `BoundedText.cs:20`). A 300 KB stderr reads "printed more than 65536 bytes". Fail closed; the sentence misleads.
- **S7.** An exception of the run that is not `ArgumentException` ends the job `runner-error` with no envelope. Zero launches, so safe; catch it and answer `instructions-failed`.
- **S8. Plan against code.** C9 item 4: "standard error goes to the step log". There is no step log: stderr of a step that passed is dropped. The unit's record (`BoundaryRun.Record`: properties, preflight rows, network) is dropped too,
  while E2 (`424cf25f`) keeps it for the check units. Keep both under `instructions/`.
- **S9. Tests.** No test for M2's path; for the arms `:177`, `:178`, `:138-141`, `:114-117`; for "coai never reads an instruction file". `D2_TheStepIsReadAtTheBaseCommit…` ends with `ended.Should().NotBeNull()`.
- **S10. Schema against reader.** `shared/toolchain.schema.json:48` lets `instructions.argv` be `[]`; the reader refuses it (`ToolchainParse.cs:252-255`, the fault at `:254`). Add `"minItems": 1` beside the `$ref` and one line to the equality test.
- **S11. Ignored files.** The tree is compared by `git status` without ignored files, so a step may leave a gitignored file in the worktree and the coder starts on the base plus that file. The rule of the checks; record it for the step.
- **S12.** `File.WriteAllText` of the kept text takes the process's default mode, and a write that fails is only a warning: the envelope then names a hash of a text that is on no disk.

## 7. `research/module_server.md` against the code

The paragraph "The instruction step (epoch 3 E3 s7 t3; SPEC C9)" is true to the code in every sentence but these:

1. "a key the reader cannot read ends it `toolchain-refused`" is true only for a fault the parser counts as the key's. A file refused for any other reason launches a model (M2). After M2 the sentence is true as written.
2. "(no network, the job's own home, no credential)": the environment holds none. On this branch the unit still shows the key folder, a keys file and a home that lie outside the hidden folders, and the whole job folder is writable (C1).
3. "the worktree at the base commit": the file is read at the base (true); the worktree's HEAD is assumed to be the base, not compared (S3).
4. "kept in `instructions/stdout.txt` of the job folder": it is the role job's record folder in the data directory, not `coder-jobs/<id>`, which the same document calls the job folder. "serves every later attempt": from memory; the file is never read.
5. Missing: a task file name that is not plain also ends `instructions-failed`; the envelope of a failed step has a seventh member, `stderrTail`; "more than 64 KiB" is also said for a cut stderr and a held pipe (S6); "a U+FFFD in the text" is right and narrower than the plan's "not UTF-8" (S5).
6. Missing: the text is not searched for a secret (M1) and the paragraph does not say that this commit sends nothing to a model (the block is s7 t2).
7. The size line (334 KB, 33 rules, 32 KiB a read) is right (P2).


---

# Pass 2 — the fixes of t3 (`c905ed1d`), C1 after the E2 merge (`43652fdc`) and t5 (`f5033a58`), the K2 row, and the `${NAME}` question (2026-11-26)

Judge: Fable (security judgement role). Read-only for product code: no edit, no build, no test run, no model call.
Branch `feat/v2-ep3-e3-roles` at `893382a3`. Every path and line below is at `893382a3` (`git show 893382a3:<path>`); the working files were not read, another coder works in them.

Read: the diffs of `c905ed1d` and `f5033a58`; `src_mcp/src/Server/RoleJobs/{CoderInstructions,CoderJobFolders,RoleJobIds}.cs`; `src_mcp/runners/Toolchain/{CheckBoundary,BoundaryPreflight,BoundaryUnit,BoundaryJob,CoderUnit}.cs`;
`src_mcp/runners/Detach/DetachedLauncher.cs`; `src_mcp/core/Toolchain/{ToolchainParse,ToolchainFile}.cs`; `src_mcp/core/Work/StrictYamlReader.cs`;
`src_mcp/tests/{ACoderInstructionStepTests,ACheckUnitHidesTheCoderFolderTests,ACheckBoundaryPreflightTests,BoundaryBed}.cs`; the t5 task file; K2 in `v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`.

Run by the judge: three `systemd-run --user` units with made-up arguments (P4 below; `--collect`, none is left, no file was made). No test was run by the judge, and no run record that holds the new tests was found, so every "holds" about a test below is by reading.
Not read: any key file. Of the user manager's environment only two COUNTS of names were taken; no value was read or printed.

## P2.1 Verdict

**t3: READY.** M1, M2 and M3 of the first pass are fixed as asked, and the two d1 gaps, the step's own network state and the base-and-clean check are in.
**C1: met for claude.** The E2 follow-ups are ancestors of `893382a3`, the step's unit is the one property list, and a test on a real unit shows the step does not see its job's coder folder. Still open, as the story's todo says: the sign-in files of codex and agy.

**Must-change in this pass: 3 (M4, M5, M6).** None is a regression of the t3 fix commit.

| # | What | Where | From |
|---|---|---|---|
| M4 | The user manager replaces `${NAME}` and a whole-word `$NAME` in EVERY argument of every unit. Job values are on that line: a toolchain argv, the task's file names, the paths | `BoundaryUnit.cs:170-183`, `:163` | E2 s2; item 4 of this pass |
| M5 | The K2 row answers "ok" without looking when it has no stand-in: when the job folder is its own cover, which is the one layout where another job's folder is NOT covered, and when the stand-in could not be made | `CheckBoundary.cs:136-155`, `BoundaryPreflight.cs:174` | t5 |
| M6 | A refusal of the YAML reader repeats 40 characters of the file's line, and M2's new sentence carries every refusal into the job's reason | `StrictYamlReader.cs:275`, `ToolchainFile.cs:31`, `CoderInstructions.cs:110` | E2's reader, reached by M2 |

## P2.2 What was measured

| # | Question | Answer |
|---|---|---|
| P4 | systemd 259.5, a unit started as the product starts one (`systemd-run --user --wait --pipe --collect --quiet -- <program> <arguments>`), six made-up arguments naming a variable the user manager has (`XDG_RUNTIME_DIR`, which is no secret) and one it has not. The script printed only "literal" with the length, "changed" or "empty", never a value | `${NAME}` alone: **replaced**. `$NAME` as a whole argument: **replaced**. `pre${NAME}post`: **replaced**. `pre$NAME`: literal. `$$NAME`: one dollar removed (17 characters became 16). `${NO_SUCH_NAME}`: an **empty** argument |
| P5 | The same through `/usr/bin/env -i PATH=… <program>`, as a step is started | The same: the arguments are replaced before `env` runs |
| P6 | The same with `--expand-environment=no` | All six **literal**, lengths unchanged |
| P7 | The user manager's environment on this machine | 12 names; 0 with `token`, `secret`, `api_key`, `password` or a `_key` ending in the name |

## P2.3 Must-change

**M4. A job value on a unit's command line is rewritten by the user manager from its own environment.**

- Where: `BoundaryUnit.Wrap` (`BoundaryUnit.cs:170-183`) builds the `systemd-run` argument list for every unit of this family, a check, a preflight and a coder's, and passes no `--expand-environment=no` and escapes no `$`.
  `BoundaryUnit.cs:163` puts the step's argv after `/usr/bin/env -i`. `DetachedLauncher.cs:32-33`, `:39-47` is the same for the runner's unit.
- What is on that line: every argv item of the toolchain file for a check step; the instruction step's `argv` and `perFile` with the task's file names in them (`CoderInstructions.cs:140`; a name with `$` passes `IsPlainPath`);
  the preflight's arguments, which are paths and the job id (`BoundaryPreflight.cs:250`, `:257`); the `env` assignments `HOME=<path>` and the others. `IsPlain` (`BoundaryJob.cs`) refuses a space, a colon, a backslash and a control character, not `$`, `{` or `}`. The reader refuses no `$` in an argv.
- So the answer to "can a job value reach the systemd command line": yes, all of them do, and the manager reads them (P4, P5). The `shift 9` is right for what it was made for, the script's own text (no script spells `${`; grep). It does nothing for the arguments.
- Scenario 1, a road around `env -i`. A machine whose session puts the login environment into the user manager (`systemctl --user import-environment`, `dbus-update-activation-environment --systemd --all`; usual for a graphical session) and a person who exports a token in the profile.
  A toolchain file at the base with the item `${GITHUB_TOKEN}` (or the whole word `$GITHUB_TOKEN`) in a build argv: the build step, which has the host network, is started with the token's VALUE as an argument and can send it.
  In the instruction step the value is printed into the prompt of every vendor when it is not one of the shapes M1 knows. The same through a task whose `files` holds `${NAME}`.
  `/proc/<manager>/environ` is refused to a unit (P3 of the E2 verify), so this is the only road to those values; the `clean-environment` row and the remark "and nothing else" (`BoundaryUnit.cs`, `Environment`) do not see it. Not on this machine today (P7).
- Scenario 2, the declared command is not the command run. An item `sh -c 'for f in ${FILES}; do …'`, a `${VERSION}` meant for the program itself, a path with `${…}`: each is replaced, an unset name by nothing (P4). The step's record and the envelope show the argv as written.
  A check can pass having run something else than the base declared.
- Smallest fix: `"--expand-environment=no"` in `Wrap`, beside `--wait --pipe`, and in `DetachedLauncher.UnitPrefix`. Measured: P6. It needs systemd 254; an older client refuses the option, the unit does not start and the preflight refuses the job, which is the closed side, so the sentence of that refusal should name the version.
  For an older manager the other way is to write every `$` of the inner arguments as `$$` in `Wrap` (P4 shows the pair is taken back to one); it must then be shown on a real unit that the scripts still read their `$1`.
  One test on a real unit: a step whose argv holds `${XDG_RUNTIME_DIR}`, `$XDG_RUNTIME_DIR` and `${NO_SUCH_NAME}` gets the three texts as written; and one preflight row beside `clean-environment`, with its degrade (the option dropped) in the theory.
- Not measured: whether a `-p` property value is read for `%` or `$` the same way. The paths that go there are refused only for the four characters above.

**M5. `other-job-folder-hidden` says "ok" where nothing was observed.**

- Where: `CheckBoundary.PlantAnotherJob` returns an empty string when `job.JobCover == job.JobRoot` (`CheckBoundary.cs:138-141`) and when the folder or the canary could not be made (`:151-154`).
  The row then prints `ok "the job folder itself is the cover, so no other job's folder lies beside it inside the unit"` (`BoundaryPreflight.cs:174`). The test `K2_AJobFolderThatIsItsOwnCover_…` asserts that "ok".
- The sentence is not true. `JobCover` is the job folder itself exactly when the folder that holds the job folders cannot be covered (`BoundaryJob.cs:94-103`: it is the root, or holds or is the worktree, the main checkout or the coder root, or a temp folder).
  Then the tmpfs lies over this job's folder only, and the holding folder is in the unit as the host has it, read-only: every other job's folder is beside it. It is hidden only when it happens to lie under `/home`, `/root`, `/mnt` or `/run/user`.
- Scenario: `COAI_CODER_JOBS` and the coder root are one folder outside those four (`/srv/coai`; the retention code allows one folder for both). Every check unit of every job reads `/srv/coai/<other job>/coder/home`: transcripts, CLI config and, after s7 t6, the sign-in COPY of a codex or agy job.
  The build step has the host network. K2 is the row made for this, and it is green. The second case: a stand-in that could not be written (a full disk, a file or a dangling link at the name) is also "ok", with the first case's sentence.
  This is the shape S2 of the E2 verify named for K1, which now says "bad … nothing was observed" for an empty name (`BoundaryPreflight.cs:164`).
- Smallest fix: plant the stand-in ALWAYS, in the real holding folder (`Path.GetDirectoryName(job.JobRoot)`, not `JobCover`), and ask always; a stand-in that could not be made is the row `bad "no stand-in could be made, so nothing was observed"`.
  Under a hidden folder the row is then "ok" by observation, in both cover cases. In the one-folder layout outside the hidden folders it is "bad" and the job is refused before any check, which is what d3 of t5 says ("a wrong answer refuses the job").
  If the lead wants that layout to run, it is an accepted risk with its own id and a record that says so, not an "ok" row. Tests: the own-cover bed with another job's folder beside it outside the temp folder goes red; a name that cannot be made goes red.
  `research/module_runners.md:4471` ("are not there, wherever `COAI_CODER_JOBS` puts them") is true only after this.

**M6. A YAML refusal repeats the line, and the job's reason now carries it.**

- Where: `StrictYamlReader.cs:275`: `expected 'key: value', found '{Shorten(text)}'`, 40 characters of the line (`:559`). `ToolchainFile.cs:31` puts the message into the refusal; `CoderInstructions.cs:110` (M2's sentence) puts every refusal into the reason of the job, unredacted.
- Scenario: an argv item of the toolchain file written without its dash, or any line that is only a value: a line that is a 40-character token is repeated whole. It goes to `job.json` and to the answer the main AI reads, which is a vendor.
  `37c899e6` ("the toolchain file's refusals never echo a typed value") covers the parser's faults and not the reader's. It was reachable before through the check note; M2 made it the first thing a job says.
- Smallest fix, at the one consumer that must not repeat: in `ToolchainFile.FromText` the reader's message goes through `Redaction.SafeText` (the product's passes) or, simpler and closer to E2's rule, the toolchain file's refusal says the line number and the code and not the text.
  One row in the theory of `D1_…NeverRepeats…`: a file with a bare token line.

## P2.4 Should

- **S13. A stand-in left by a kill is never removed, and the comment says it is.** `RemoveAnotherJob` runs in a `finally` (`CheckBoundary.cs:126-129`); a killed runner, a power loss or a WSL restart leaves `<coder-jobs>/coai-preflight-other-<job id>`.
  `CoderJobFolders.RemoveOf` removes two names, `<id>` and `<id>.mounts` (`:41-50`), and searches nothing, so "the retention of the folder takes it with the job" (`CheckBoundary.cs:168`) is not what the code does. Add the third name to `RemoveOf` and correct the comment. Harmless content (one fixed sentence); it is litter, and this family counts leftovers.
- **S14. The stand-in is made through whatever is at its name.** `Directory.CreateDirectory` and `File.WriteAllText` (`:145-147`) follow a link: a link at that name would get `canary.txt` written into its target. Only a process outside every unit can put one there (no unit can write the holding folder), so it is hygiene: make a fresh folder, refuse an existing name, mode 0700.
- **S15. The C1 test shows the effect, not the property.** `C1_OnARealUnit_TheStep_CannotSeeTheCodersFolder…` has the canary on the host as its control and no red (the cover dropped). The property is shown red by E2's K1 tests and t5's K2 tests on the same list, so this is enough for C1; say in the test which tests hold the mechanism.
- **S16. C1, the part that is still open.** `BoundaryUnit.SignInHidden` hides claude's `.claude/.credentials.json` at its real path and nothing of codex or agy. Before s7 t6 and before either vendor ships: their sign-in files at their real paths, with a row; and the copy made after the step or under the cover. The todo records it.
- **S17.** The envelope of a file refused for another reason says `declared: false` (`CoderInstructions.cs:110`: `InstructionFaults.Count > 0`) while the reason says "not known". Leave the member out there, or give it a third value.
- **S18.** `IsControlButNotSpacing` (`:195`) lets U+0080 to U+009F pass (U+009B is a control sequence introducer for some terminals). Add the range.
- **S19.** `D1_AnInstructionFileOfTheRepository_IsNeverReadByCoai…` asserts `declared: false` only; assert too that `ran-by-agents-md` is not in the worktree.
- **S20. By reading, not measured, and not of these commits:** a claude coder's unit binds the whole coder root read-only (`CoderUnit.cs:56`) and covers `JobCover` (`:60`). In the one-folder layout of M5 the coder root holds the other jobs' folders, so a claude coder reads them even under `/home`. To be looked at with M5.
- **Open from the first pass, recorded in the story's todo:** S2 (path sockets, measured P1), S5, S6, S7, S8, S11, S12.

## P2.5 Confirmed safe

The t3 fixes (by reading the code and the tests):

- **M1.** `Why` searches the whole stdout with `Redaction.FirstSecretClass` after the size, U+FFFD and control arms and before the tree arm (`CoderInstructions.cs:182-191`). A hit is `instructions-failed`, the reason names the class only, and `Keep` is not reached, so nothing is on disk and `Ran` is not made.
  The search reads the same characters the text has, because a control character is refused one arm earlier (`:188`, `:195`). The test asserts zero launches and the token absent from the reason, the result, the log and every file of the job's record folder.
  The lead's decision (refuse, no redaction, prose with a credential NAME refuses the job) is recorded in the todo. It is the product's own rule for text that goes to a hosted model.
- **M2.** Every `ToolchainRead.Refused` ends `toolchain-refused` before the worktree is looked at and before any unit (`:108-111`). The test covers a flow list in `argv` and a fault in another key, zero launches each; the E2 test that expected a turn 0 now asserts none.
- **M3.** The fault says the item's place and length (`ToolchainParse.cs:269`); the test holds a credential with the placeholder in it out of `Sentence` and `InstructionFaults`. With the E2 merge `Shown` and `KeyShown` say kind and length for the other faults of the key.
- **The d1 gaps.** `minItems: 1` beside the `$ref` (`shared/toolchain.schema.json:48`) with a line in the equality test; a source scan of the three files and a run with an `AGENTS.md` that orders a command.
- **The step's own network.** `StepNetwork` is a constant, none (`:96`); the admission asks `CheckNetwork.Private` (`:130`); nothing of the test step's choice is read any more.
- **At the base and clean.** `before.Head != baseSha || before.Status.Length > 0` ends the job (`:125`). The base is a full 40-character id by `CoderWorktree.cs:120`, so the two are compared like with like. The state is read before the admission, so the preflight's own marker files are not in it.

C1 after the merge:

- `0dc0f94c`, `35e6f584`, `964f037a`, `424cf25f`, `37c899e6` are ancestors of `893382a3`.
- The step's unit is `CheckBoundary.RunAsync` over `BoundaryUnit.Properties`: the read-only tmpfs over `JobCover` with only the check folders bound (`BoundaryUnit.cs:82-86`), the key folder and keys file inaccessible at their real paths, claude's sign-in file inaccessible, the job folder refused when it is not its own real path (`CheckBoundary.cs`, `JobRootOnDiskProblem`).
  There is no second list. The step is admitted by the same preflight, so K1 and K2 are asked before it runs.
- On a real unit (the rig skips with no user manager) the step prints `hidden-coder-folder` and `does-not-list-coder-folder` while a stand-in sign-in copy lies in `<job>/coder/home` on the host. Before turn 0 no unit of the job can write the coder's home any more.

The K2 stand-in (item 3), apart from M5, S13 and S14:

- **Where and what.** `<holding folder>/coai-preflight-other-<job id>` with one file of one fixed sentence; default modes; made before each preflight unit and removed after it, for each network state in turn.
- **The name cannot be a job's.** A job id is `r`, 14 digits, `-`, 8 hex characters (`RoleJobIds.cs`, 24 characters); the stand-in's name is 45 characters, over the 40 a job id may have, and it is neither `<id>` nor `<id>.mounts`. So it is never another job's folder, and no retention of any job removes or enters it. The job id in it is checked before any path is made (`BoundaryJob.Problem`).
- **Can a running job see it.** Not when the holding folder is the cover: both kinds of unit put the tmpfs over it (`BoundaryUnit.cs:84`, `CoderUnit.cs:60`), and a unit started earlier keeps its own empty view. When the job folder is its own cover nothing is planted (that is M5's gap, not a leak: there is nothing in the stand-in).
- **Can a running job make it mean something.** No unit of this family can write the holding folder: it is under the tmpfs, or hidden, or read-only by `ProtectSystem=strict`. So no step and no coder can make, replace, link or remove the stand-in, turn the row green or red, or aim the delete somewhere. The delete is of one name built from a checked job id.
- **Races.** Two jobs have two names. One job asks its network states one after the other, and its admissions (the step, then each round of checks) one after the other. The unit is asked after and stopped before the stand-in is removed.
- **Crash.** Return, refusal, exception and cancel all pass the `finally`. A kill does not (S13).

The `shift 9` (item 4), apart from M4:

- `Command` always passes ten arguments before the mounts, the tenth empty when there is no stand-in, and an empty argument stays an argument. `Header` reads `$1` to `$7` and `CheckHeader` `$8` and `$9` before the shift; every row after it uses names. The `$1` to `$3` of `row()` are the function's own.
  `SubmodulesRow(1)` shifts past the stand-in to the mounts; the coder's script is unchanged (`SubmodulesRow(4)`). No script spells `${` (grep of `BoundaryPreflight.cs`), so the manager changes nothing in the script's text.
- A row the script did not print is a row that did not hold (`BoundaryPreflight.Parse`), so a script that died at the shift refuses the job.
- With `--expand-environment=no` every shape of P4 arrives as written (P6), so M4's fix needs no change of any script.
