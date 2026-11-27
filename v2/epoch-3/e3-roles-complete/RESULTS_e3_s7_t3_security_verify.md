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


---

# Pass 3 — the pass-2 fixes in `2eb40e31`, and the security-relevant code of s8 t1 (`a7358b69`) (2026-11-26)

Judge: Fable (security judgement role). Read-only for product code: no edit, no build, no test run, no model call.
Branch `feat/v2-ep3-e3-roles` at `2eb40e31`. Every path and line below is at `2eb40e31` (`git show 2eb40e31:<path>`, `git diff 893382a3 2eb40e31`); the working files were not read.

Read: the product diff of the range (14 files); `src_mcp/runners/Toolchain/{CoderBoundary,CoderUnit,CoderTurns,CheckBoundary,BoundaryPreflight,BoundaryFolders,UnitControl,BoundaryHost}.cs`; `src_mcp/runners/Consultation/CoderVendors.cs`;
`src_mcp/runners/Files/RealPath.cs`; `src_mcp/src/Server/RoleJobs/{CoderOsLayer,RoleFailover,CoderWork,CoderRoute,RoleRoute,RoleJobService,RoleModelLauncher,RoleKeyRoad,CoderJobFolders,CoderInstructions}.cs`;
`src_mcp/core/Work/StrictYamlReader.cs`; `src_mcp/tests_fakecli/{Program,CoderVerbs,ModelMap}.cs` and the project files; `src_mcp/tests/ACoderFailsOverAcrossVendorsTests.cs`; SPEC section E; S3 and S4 of the judgement; the s8 todo.

Run by the judge: one scratch unit of a made-up process (P8; killed and reset, none is left). No test was run. Not read: any key file, any environment.

## P3.1 Verdict

**The pass-2 must-changes are closed**: M4, M5, M6 (both parts) and S13 are fixed as asked.

**s8 t1: must-change 1 (M7), then ready.** It is in code E2 wrote; s8 t1 is the change that makes it matter between vendors, and its new class states the opposite as a fact.

| # | What | Where |
|---|---|---|
| M7 | A coder unit that could NOT be stopped does not end the job. The failover goes on to the next model, of another vendor too, beside a unit that is still alive, and `LeaveVendor` runs with a live writer in the home | `CoderTurns.cs:93-114`, `CoderOsLayer.cs:89-94`, `:172-173`, `RoleFailover.cs:362` |

**"A vendor other than claude gets none of claude's credentials" is the right reading of SPEC E7** ("a unit holds only its own vendor's sign-in"), and it does not stand in the way of s7 t6: a copy for the job lies in the coder's home and needs no bind from the host.
It is half of E7. The other half, and one thing t6 must decide before it makes a copy, are C2 below.

## P3.2 What was measured

| # | Question | Answer |
|---|---|---|
| P8 | A scratch user unit whose one process ignores the stop signal; then `systemctl --user stop` with the product's wait of 20 s (`UnitControl.cs:19`) | The unit's `TimeoutStopUSec` is 1 min 30 s (the manager's default; the product sets none). After the 20 s the stop client was ended, the unit was **`deactivating`** and its process **alive**. `EnsureGoneAsync` answers "not gone" for that state. A kill signal ended it |

## P3.3 Must-change

**M7. A unit that could not be stopped is read as a model that crashed.**

- Where: `CoderTurns.Classify` (`CoderTurns.cs:93-114`) asks `!run.Gone` only for a process that exited 0, and even then answers `CoderTurnOutcome.Failed`. For an exit that is not 0 the sentence is "the coder exited N" and `Gone` is not looked at.
  `CoderOsLayer.StopOf` (`CoderOsLayer.cs:89-94`) makes a job-ending stop of `WriteStopped` and `Refused` only; `CoderTurnLauncher.Failure` (`:172-173`) hands the failover a plain failed process, which it classifies as a crash and answers with the next model.
  `RoleFailover.AttemptAsync` then calls `VendorLeft` (`RoleFailover.cs:362`) and starts the next unit. The remark of `CoderSignInCopy` (`CoderOsLayer.cs:181`) says "the vendor's unit has ended and been stopped before this runs, so nothing races the resolution". Nothing makes that true. No test holds the path.
- How a unit stays: P8. Any process that ignores the stop signal lives up to 90 s; coai waits 20. The `systemd-run` client must also end early for the result to be a crash. It ends by itself when the manager's connection drops, and a process in a unit can end it:
  a unit may signal the person's processes (P3 and S1 of the E2 verify; `PrivatePIDs` was taken out again, `6786502c`). A claude coder has no shell. A codex coder has one.
- Scenario: a job on codex, then agy. A process the codex model started ignores the stop signal and ends its own client. coai reads a crash, waits 20 s, deletes codex's sign-in copy and starts agy's unit. For up to 70 s more the old unit runs with the coder's home, its temp folders and the worktree writable:
  1. it writes the sign-in back (a CLI that refreshes its token rewrites the file by itself; S4 of the judgement says agy does), and agy's unit reads it: E7 is broken;
  2. it swaps the copy's folder for a link between `RealPath.Resolve` (`:196-197`) and `File.Delete` (`:229`), and the delete lands in the folder the link names, the person's real one among them. One try for each failover, a narrow window, a real one;
  3. it edits the worktree under the next coder and under coai's `git add`, after `Restore` proved the tree clean.
  The same road was there in E2 between two claude models (point 3 only). s8 t1 adds another vendor's sign-in to it.
- Smallest fix: in `Classify`, `!run.Gone` FIRST and for every result, as an outcome of its own that `StopOf` turns into `coder-boundary` (kind `unit-not-stopped`, started: true): the job ends, nothing of the turn is committed, no next attempt.
  To make it rare as well: `TimeoutStopSec=` on every unit of the family, shorter than the stop wait (10 s against 20), so the manager kills what ignores the signal while coai still waits.
  One test on a real unit: a turn whose process ignores the stop signal and whose client is ended finishes the job `coder-boundary` with no later launch; and the remark of `CoderSignInCopy` then names the stop that makes it true.

## P3.4 Should

- **C2. Before s7 t6 makes a copy: one home for each vendor, or the delete is not enough.** `LeaveVendor` removes ONE file at ONE path. Every vendor's unit binds the same coder folder (`CoderUnit.cs:61-63`: home, `/tmp`, `/var/tmp`), and nothing between two vendors empties it (`BoundaryFolders.Tidy` removes three markers).
  So what the left vendor's CLI or model put anywhere else in the home or the temp folders, a second name of the copy (a hard link, a plain copy), a refresh file, a note, is read by the next vendor's unit. An ignored file in the worktree goes the same way.
  The copy at its own path, untouched, is handled; a copy that moved is not, and after the delete coai no longer knows the token texts S4 wants searched in a commit.
  The structural answer: `<job>/coder/<vendor>/…`, bound only into that vendor's unit; the delete is then hygiene and no path a model could prepare is walked. Else: remember the token texts of every copy the job made, for the commit search of every later turn.
  Also for t6, from S16: the REAL sign-in files of codex and agy must be out of claude's units and the check units where the home is not under a hidden folder; the coder's preflight has no other-job row, and a claude coder's unit binds the whole coder root (S20).
- **S21. By reading, not measured: a left vendor whose folder was never made ends the job instead of failing over.** `RealPath.Resolve` answers OK for a path whose last names are missing (`RealPath.cs:345`), so the branch at `CoderOsLayer.cs:204-208` ("the folder is not there") is not reached for a missing folder.
  The code goes on to `File.Delete` (`:229`), which on Linux throws `DirectoryNotFoundException` when the file's folder is not there; that is an `IOException`, so the answer is "could not be deleted" and the job ends `coder-boundary`.
  It is reached when the first vendor never got as far as its folders: its CLI is missing or its probe refused, so `_lastVendor` is set (`RoleFailover.cs:373`) and `BoundaryFolders.Prepare` never ran. A job that should go from a missing claude CLI to the next vendor would stop instead. The closed side, but the wrong stop.
  Ask `Lstat(folder)` for `Missing` first and answer empty; one test with a first vendor that was never started.
- **S22. `VendorLeft` answers "nothing left" for a vendor it cannot resolve** (`CoderWork.cs:127`). Not reachable today (the vendor was launched through the same `Resolve`); make it the sentence, not the empty string.
- **S23. The branch is on the vendor's id** (`CoderBoundary.cs:71`: `Vendor.Id != CoderBoundaryTable.Claude`). SPEC E5 wants one table and no second place that decides by vendor. The seam already holds the fact as data: bind the host's file only for a vendor whose recipe is `BindRealFile`.
  And the words: such a launch is recorded as `keyed` and its rows speak of "a launch that has a pool key" (`BoundaryPreflight.cs`, `SignInAbsentRow`, `KeysHiddenRow`, `SignInRow`). With t6 it is a third state, a copy of its own, with rows of its own (0600 in 0700, its own file there, no other vendor's).
- **S24. The coder's preflight asks for the key FOLDER only** (`BoundaryPreflight.cs:257`); the property hides the folder and the keys file (`BoundaryUnit.KeysHidden`), and the check's row asks both. Give the coder's row the same list. It is the row that now speaks for every unit of another vendor.
- **S25.** The fake's `readFiles` writes the first 80 characters of every file it is told to read into the record folder (`CoderVerbs.TryRead`). The tests name stand-ins only (the rig's home is a temp folder). Say in the field's remark that a real path must never be named there; SPEC E3.
- **Open from the earlier passes:** S2 (path sockets), S5 to S8, S11, S12, S14 to S20 as far as not named above. S14 is closed by M5's fix (a file or a link at the stand-in's name is not touched).

## P3.5 Confirmed safe

The pass-2 fixes:

- **M4.** `--expand-environment=no` is in `DetachedLauncher.UnitPrefix` (`DetachedLauncher.cs:37`), and `BoundaryUnit.Wrap` (`BoundaryUnit.cs:176`) builds every unit's line from it; a grep finds no other place that builds a `systemd-run` line. Measured in pass 2 (P6). An old client's refusal names the version. Two tests on a real unit.
- **M5.** The stand-in is made in `Path.GetDirectoryName(JobRoot)` whatever the cover (`CheckBoundary.cs:144`); a leftover folder of that name is replaced, a file or a link there is never touched and gives no stand-in (`:151-160`); no stand-in is the row `bad … nothing was observed` (`BoundaryPreflight.cs:174`).
  The job folder is refused when it is not its own real path before any of this runs. Three tests: the own-cover layout is asked, the one-folder layout that the unit shows goes red and refuses the job, a stand-in that cannot be made is a bad row.
- **M6.** The YAML reader repeats no text of the file: the five messages that did are fixed sentences now and `Shorten` is gone; what it still puts into a message is a size, a line number and a named indicator. `CoderInstructions.cs:110` passes the whole refusal through `Redaction.SafeText`.
- **S13.** `CoderJobFolders.RemoveOf` removes the stand-in by its one name (`:52`), by the same `Remove` that leaves a link alone; the record stays while any of the three names stays.

s8 t1:

- **No claude credential in another vendor's unit.** `HostFor` and `KeyedLaunch` (`CoderBoundary.cs:65`, `:71`) give every launch of another vendor a host with `BindSignIn` false, in the admission and in the run alike, so `BoundaryHost.SignInFile` is empty and no property names the file.
  The preflight asks from inside in the keyed mode: the sign-in is at neither its place in the job's home nor its real path, and the key folder cannot be read. A vendor's own property list that forgot the hiding is refused by those rows.
  Only a claude model draws a pool key (`RoleKeyRoad.Fresh`, `:91-93`), so no claude key is on another vendor's stdin. In the product today only claude has a type (`CoderVendors.Shipped`), so none of this runs outside a test yet.
- **The delete does not leave the home.** `CoderSignInCopy.Remove` resolves the home and the copy's FOLDER and refuses a folder that resolves outside the home (`CoderOsLayer.cs:210-214`); the delete is of the last name under the resolved folder, and `File.Delete` removes a link, never what it points at.
  A folder at the copy's name is refused. After the delete the name is looked at again. Every refusal ends the job `coder-boundary` before the next unit (`RoleFailover.cs:362-365`), and it runs before `Restore` and before anything of the next attempt. With no live writer (M7) there is no window.
- **The empty place of claude's bind.** Deleted only when it is a regular file of length 0 (`:224-227`): a link, a folder or a file with something in it is refused and ends the job.
  A planted link cannot be followed (the kind is read without following, and the delete is of the name). A hard link there is one more name of an empty file; removing it changes nothing else. The real file cannot be hard-linked from inside the unit (another mount).
  A model that put a COPY of the real sign-in there makes the job stop, which is right; a copy under another name is C2.
- **A refused vendor is never launched; a refused model makes nothing.** The start routes first (`RoleJobService.cs:80`), before the key check, the store, the job id and the runner; `CoderRoute.Narrow` drops entries of a vendor that does not resolve and refuses a model the caller named on one. The worktree is made later still, by the runner.
  The runner asks the set again at every launch (`RoleModelLauncher.cs:65`). `CoderSet` and `Vendors` are `internal` properties with the product's set as their value; no setting, no environment name and no argument reaches them (grep).
- **The fake cannot be reached in a product build.** `FakeCli` is a project of its own; no product project references it, only the two test projects; the product reads no `FAKECLI_` name; CI builds it and never publishes it. `readFiles` is read from a test's model map, inside the fake only.
- **The reading of E7** (the question): right, for the reasons in the verdict. What it leaves for t6 is C2 and S23.


---

# Pass 4 — s7 t6, the sign-in copies (`514aa1c1`, diff `6ebdbf42..514aa1c1`) (2026-11-26)

Judge: Fable (security judgement role). Read-only for product code: no edit, no build, no test run, no model call.
Every path and line below is at `514aa1c1` (`git show 514aa1c1:<path>`); the working files were not read, two coders work in the E3 trees.

Threat model given for this pass: a coder model under prompt injection inside its unit, and the unit of a later vendor of the same job.

Read: the diff of the range (20 files); `src_mcp/runners/Toolchain/{SignInCopy,SignInRefresh,CoderBoundary,CoderTurns,CoderUnit,BoundaryPreflight}.cs`; `src_mcp/runners/Consultation/CoderVendors.cs`;
`src_mcp/src/Server/RoleJobs/{CoderOsLayer,CoderCommits,CoderEnvelope,CoderWork,CoderFixLoop,CoderRestore,CoderJobFolders,RoleModelLauncher,RoleFailover,RoleJobNotices}.cs`; `src_mcp/src/Server/Keys/ClaudeKeySecret.cs`;
`src_mcp/tests/ASignInCopyTests.cs`; the fake CLI's three files; the rows A11, A12, G10, CX-R1, CX-R4, AG-R1, AG-R2 of the judgement; the paragraph of `research/module_server.md`.

Run by the judge: one scratch unit on made-up folders (P9; folders removed, no unit left). No test was run, so every "holds" about a test is by reading. Not read: any key file, any sign-in file, any environment.

State of the product: no codex or agy type exists yet and both rows are marked not launchable (`6ebdbf42`), so nothing of t6 runs outside a test today. The findings are about the mechanism that those two vendors will run on.

## P4.1 Verdict

**Must-change: 3 (M8, M9, M10), then ready as a mechanism.** One condition before `launchable` is switched on for a vendor with a copy (C3).

| # | What | Where |
|---|---|---|
| M8 | coai writes the sign-in copy THROUGH a link a coder left: a folder that is not there yet is not checked, and the home itself can be a link | `SignInCopy.cs:187-196`, `:230-236`; `CoderOsLayer.cs:251-264` |
| M9 | The copy's tokens are searched for in the commit only. What the coder PRINTS (its answer, its report, both streams) is not searched, and goes into the turn files, the envelope and the main AI | `RoleModelLauncher.cs:154` |
| M10 | The scratch home with the FULL copy (the owner's refresh token) is outside the names the retention removes, and in one layout it is under no cover | `CoderBoundary.cs:127`, `CoderJobFolders.cs:49-52`, `SignInRefresh.cs:27`, `:133` |

The four questions of the brief, short:

- **Can a token leave?** Through a commit: no, for the exact text (K3 holds). Through the envelope, the turn files and the job's reason: **yes (M9)**. Through a notice or a log line of coai: no. Through a file the checks read: **yes (M8)**.
- **Can the job steer the refresh unit?** No. Its argv, environment and home are constants of the recipe and of coai; the worktree, the task and the toolchain file reach none of them.
- **Can a coder make coai read or write through a link?** **Write: yes (M8).** Read: yes, harmless (S29).
- **Can the gate be passed with a crafted expiry?** A coder can pass it for its OWN later turns of a job-long copy (S29); it gains no credential by it. The real file is the person's own, and an expiry that cannot be read refuses.

## P4.2 What was measured

| # | Question | Answer |
|---|---|---|
| P9 | A unit with the coder's binds (`TemporaryFileSystem=<jobs>:ro`, `BindPaths=<job>/coder`, a stand-in worktree writable, `PrivateUsers=yes`). From inside: rename `<job>/coder/home`, put a link at its name, and make a link in the old home to a path the unit cannot see | All three **done**. On the host `<job>/coder/home` was a link to the worktree's folder, and the second link resolved to `<job>/home`, the home of the CHECKS. The home is not a mount point; only the folder above it is |

## P4.3 Must-change

**M8. The copy is written through a link.**

- Where, two holes:
  1. `SignInCopy.Write` asks `IsInside` only when the copy's folder IS there (`SignInCopy.cs:193`). For a folder that is not there yet it asks nothing and `Directory.CreateDirectory` makes it (`:200`), through every link in the path above it.
     `RealPath.Lstat` looks at the last name only. The test (`ALinkThatTheCoderMadeWhereTheFolderBelongs…`) has the link AT the folder, one name deep. agy's file is two names deep: `.gemini/antigravity-cli/antigravity-oauth-token` (judgement, J1).
  2. `IsInside` (`:230-236`) and `CoderSignInCopy.Remove` (`CoderOsLayer.cs:251-264`) compare the RESOLVED folder with the RESOLVED home. A home that is a link passes both. A coder with a shell can make it one (P9); nothing asks whether `job.CoderHomeFolder` is a real folder, and the preflight's home row does not (`BoundaryPreflight.cs:107`).
- Scenario (a codex coder under injection, then agy; codex has a shell, SPEC C7): in its turn 0 codex commits a folder `.h` with one file, an ignore line for the rest of `.h`, and a build target that reads `.h`. It makes `~/.gemini` a link to `<worktree>/.h` (or its whole home, hole 2).
  A fix turn of codex ends on a limit; the job goes to agy. `LeaveVendor` deletes codex's copy; the restore keeps `.h` (it is committed). coai then writes AGY's copy into `<worktree>/.h/antigravity-cli/`.
  The file is ignored: `git add` does not stage it, `ls-files --exclude-standard` does not list it, so K3 does not see it. The next round of checks binds the worktree; the build step has the host network and runs the target codex committed. agy's access token leaves.
  That is the one thing the cover of s7 t5 is for (a check sees no coder home) and the second half of SPEC E7. With the link aimed at `<job>/home` instead, the copy lands in the checks' home; no later check reads it there, and the delete at the job's end refuses it and says nothing (S27).
- Smallest fix: one question, asked before `Make` and in `Remove`: the home is a real folder and its own real path (`Lstat(home)` is a directory and `Resolve(home).Path == home`, the shape of `BoundaryJob.JobRootOnDiskProblem`), and the copy's folder RESOLVES to a path inside it, there or not (`RealPath.Resolve` answers for a missing end).
  A wrong answer refuses the launch (`coder-boundary`), as the other refusals of `Write` do. Add `[ ! -L "$HOME" ]` to the home row, so a unit is never started on a linked home either. Two tests: a link one name above a folder that is not there; a home that is a link.

**M9. A token of the copy in what the coder prints is not searched for.**

- Where: `RoleModelLauncher.LaunchCoderAsync` scrubs a launch's answer, evidence and both streams only when the launch has a POOL key (`RoleModelLauncher.cs:154`: `secret is null ? new Scrubbed(finished, false, [])`), and `RoleFailover.cs:567` ends the job only for that key. The token texts of the copies exist as the same type (`CoderOsLayer.SignInTokens`, `:48`) and are handed to the commit search alone (`CoderFixLoop.cs:221`).
  For the pool key the product searches three places: the output, the home and the commit. For the copy it searches one. `common/security.md`: a measure applied at some of its sites.
- Scenario: text in the repository tells the coder to "include your auth file in the notes for debugging". codex has a shell, reads `auth.json` and puts the access token (10 days at most, by the judgement) and the id token into `notes` of its report.
  The report is valid and the checks are green. The tokens are in `attempts/<n>/turn-<k>.txt`, in the envelope's `report`, in the result file, and in the answer of `role_status` that the main AI reads: another vendor. The same through a stream that becomes a stop's reason; `Redaction.SafeText` knows vendor key shapes, not a JWT.
- Smallest fix: `LaunchCoderAsync` takes `Layer.SignInTokens()` and passes every one through the scrub that the pool key gets (the text replaced by a mark that names the copy's id). A hit ends the job `coder-boundary` before anything of the turn is read, kept or committed.
  One test beside `ATokenOfTheCopy_InAFileTheCoderWrote…`: a turn whose answer holds a token of the copy; the token is in no file of the job's record and not in the result. An encoded token passes this search as it passes K3 (S30).

**M10. The full copy for the refresh can stay for ever, and is not always hidden.**

- Where: the scratch home is `<job folder>.refresh`, a SIBLING of the job folder (`CoderBoundary.cs:127`). It holds the whole real file: for agy the owner's Google refresh token, which does not expire by itself (judgement, A12).
  1. `CoderJobFolders.RemoveOf` removes three names: `<id>`, `<id>.mounts` and the preflight's stand-in (`CoderJobFolders.cs:49-52`). `<id>.refresh` is not one of them. The `finally` of `MakeCopyAsync` (`CoderBoundary.cs:140-147`) does not run when the runner is killed, the machine loses power or WSL restarts during the up to 180 s of the unit.
     The folder then stays, with the token, and nothing ever removes it. `SignInRefresh.cs:133` says "what stays is under the folder the E2 sweep removes with the job". It is beside that folder. This is S13 of pass 2 again, with a real secret.
  2. `SignInRefresh.cs:27` says the folder is "under the folder the units cover with an empty tmpfs". That is true when the cover is the holding folder. When the job folder is its own cover (`BoundaryJob.JobCover`), the sibling is under no cover of this job or of any other.
     Where the holding folder is not under a hidden path, a coder's unit of another job reads it while the refresh runs (a claude coder's unit binds the whole coder root, S20; the coder's preflight has no other-job row). Mode 0700 does not help: it is the same user.
- Smallest fix: the scratch home INSIDE the job folder (`<job>/refresh`). It is then under the cover in every unit of this job in both layouts, and the retention removes it with the job by the existing name. Also remove a leftover `refresh` folder at the next start of any job (the sweep that runs there), so the token does not wait for the 30 days.
  For point 2: no refresh when the job folder is its own cover, with a sentence, or a recorded risk with its own id. Correct the two remarks. One test: a refresh unit killed with its client; the full copy is gone after the next start.

## P4.4 Condition before `launchable` is on (C3)

Not a change of this commit. The owner of s5 and s6 needs it before a route can hold two vendors that sign in with a file.

1. **One home for all vendors lets the first steer the CLI of the next.** C2 of pass 3 gave two answers; t6 took the second (remember the tokens). That closes the commit. It does not close this: every vendor's unit binds the same `<job>/coder` (`CoderUnit.cs:61`), so a codex model can leave, in a REAL folder inside the home, whatever agy's CLI reads at its start: settings, hooks, an agent file, the guard folder (`<job>/coder/guard`, written from outside in J1).
   agy's CLI then runs it in agy's unit, with agy's token and the host network. No link is needed and M8's fix does not see it. Under the threat model of this pass it is the largest thing left.
   The structural answer stays `<job>/coder/<vendor>/`, bound only into that vendor's unit; else coai must make the next vendor's home EMPTY before its first launch and write its guard and its config itself, each time.
2. **The real files of codex and agy at their real paths** (S16, open since pass 2): `BoundaryUnit.SignInHidden` hides claude's file only, and no row asks for the others. They are the product's credentials now.
3. **The rows of a copy vendor** (S23): AG-R1 wants "the job's copy, mode 0600, the only file of the sign-in there; the real `~/.gemini` cannot be seen". The preflight of such a launch still asks claude's two rows in the `keyed` mode.

## P4.5 Should

- **S26. The refresh unit is a new unit of the family with a list of its own** (`SignInRefresh.cs:37-51`). It has no `KeysHidden` and no `SignInHidden` (KEY_POOL_SPEC: the key pool is in NO boundary unit), no preflight and no record of its properties.
  Its remark says "no repository, worktree, main checkout": true only where those lie under a hidden folder; elsewhere `ProtectSystem=strict` leaves them readable. It runs the vendor's own CLI with no model, so this is not a road for the coder. Add the two shared lists, and say what is hidden by what.
- **S27. The delete at the job's end says nothing when it fails** (`CoderOsLayer.cs:68`: the answer of `Remove` is dropped). A copy behind a link or under a folder stays until the retention, in silence. Log it and raise a notice.
  Under `UnitNotStopped` the copy stays on purpose; the remark says the sweep removes it "when the unit is gone", and the retention removes it after 30 days or 200 jobs. Say that, or remove it at the next start.
  An exception of a turn goes `catch` → `EnsureGoneAsync` (answer dropped) → `throw` (`CoderBoundary.cs:280-284`), so `_unitMayBeAlive` is not set and the `finally` of the work deletes beside a unit that may live.
- **S28. The record of the real file is made on one end only.** `SignInRecords` and the notice are in `EnvelopeAsync` (`CoderWork.cs:237`). A job that ends on a token hit, a boundary stop or the last failover has no `signIn` in its envelope and no notice, and those are the jobs where a person wants it. Make the record in the `finally`, for every end.
- **S29. The gate and the token list read the copy as the coder left it.** For a job-long copy (codex) `PrepareSignInAsync` reads the expiry from the file in the coder's home before every later launch (`CoderBoundary.cs:108`). A coder can write any `exp` there; the launch then starts on a token with no margin and a due refresh fails on the placeholder: a lost turn, no credential.
  `File.Exists(state.CopyFile)` (`:97`) follows a link, so a link at the copy's name makes `ExpiryOfFile` and `TokensOf` read a host file of the coder's choice (texts to memory only, for the search). Keep the expiry coai wrote at `Make` in the job's state and gate on that; ask `Lstat` for a plain file.
  The agy time is read by `DateTime.TryParse`, which takes many shapes that are not RFC 3339; use the exact formats.
- **S30. What K3 does not find, said once:** a token that is encoded or cut in two; a file git ignores (it is not committed, and a check reads it: M8); a file in the coder's home or temp folders. The same limits as the pool key's search. They belong in the doc beside "the tokens are searched".
- **S31.** `Unsearchable` says "for the turn's claude key" when the search was for a copy's token (`CoderCommits.cs:133`), and `TokenHit` names neither the vendor nor the id (`:129`); the brief of t6 says a hit names an id.
- **S32.** `Snapshot` answers "not there" for a file it could not read (`SignInCopy.cs:59-62`): the person is told to sign in when the trouble is a permission. `ExpiryOfFile` reads the REAL file with `File.ReadAllBytes` (`CoderBoundary.cs:117`), which is not the shared handle the three other reads take; read-only, but the doc says "shared read handle" for all.
- **Open from the earlier passes:** S2 (path sockets), S5 to S8, S11, S12, S15, S17 to S25 as far as not named above. C2 of pass 3 is answered for the commit; its other half is C3 item 1.

## P4.6 Confirmed safe

- **The copy's shape.** `Make` builds a NEW object from the recipe's kept fields, then puts the placeholder where the recipe says and the source has the field (`SignInCopy.cs:91-106`); nothing else of the source is carried. The placeholder is a fixed text and is never in the token list (`:172`).
  The file is made with `CreateNew` and mode 0600 after a file or a link at its name is removed (the link itself, not its target); the folder is 0700; a folder at the copy's name refuses. A link AT the folder's name refuses. What is missing is M8.
- **No text of a sign-in in a sentence.** Every refusal of `Make`, `Write`, `Gate`, `Snapshot` and the refresh names a field kind, an exception TYPE, an exit code, minutes or a vendor; none takes an exception's message or a stream of the CLI. `ClaudeKeySecret` prints its id. The state objects print the vendor.
- **The gate.** Asked before every launch, after the copy is made and before the admission (`CoderTurns.cs:48-53`); the need is the unit's whole life (timeout and backstop) and the recipe's margin. No expiry, a JWT with fewer than two parts, a payload that is not base64 or not JSON, an `exp` that is no whole number or out of range: each is `null`, and `null` refuses (`SignInCopy.cs:150-153`, `:274-294`).
  A refused launch is a boundary refusal, so the job ends and nothing is started on less. codex is never refreshed by coai (no `RefreshArgv`), so its refresh token is in no unit.
- **The refresh unit cannot be steered by the job.** The argv is the recipe's, the environment is five fixed names with `HOME` the scratch home, the working folder is the scratch home, the program is the one the launch was configured with (`CoderBoundary.cs:121-130`). No path of the worktree, no value of the task or the toolchain file is on its line, and the line is not expanded (M4).
  It binds no worktree, no coder folder and no check folder; the coder's unit is not started until the unit is gone and the scratch home is deleted (`:140-147`); a refresh that fails, runs out of time or cannot be stopped refuses the launch and makes no copy. The full copy is cut by the same `Make`, so the coder's copy of a refreshed file has no refresh token either.
- **The real file is never written.** Every open of it is `FileAccess.Read`; the refresh works on a byte copy. Its sha256 and time before and after are in the envelope as a hash and two times, and the notice holds the vendor and the times only.
- **K3 and C2, for the commit.** Every copy's token texts, as the file is now and as it ever was in the job (a set that only grows; read again before a left vendor's copy is deleted, `CoderOsLayer.cs:113-118`), are searched in the files before `git add`, in the staged names and in the staged blobs after it (`CoderCommits.cs:37-43`, `:76-85`).
  A hit resets the index, commits nothing and ends the job `coder-boundary`; a search that cannot be made is a failure, not a pass. There is one commit site (`CoderFixLoop.cs:221`), for turn 0 and every fix turn. The texts live in the runner's memory only, and the runner dumps no core.
- **After the job.** The copies are deleted in the `finally` of the work for every end that reaches it; `Remove` does not follow a link out of the (resolved) home and refuses a folder; under `UnitNotStopped` nothing is deleted beside a live writer.
- **The fake.** `set-json-field` is a verb of the fake's own program and `#folder` a suffix of its model map; no product project references `FakeCli`, the product reads no `FAKECLI_` name, and CI builds the fake and never publishes it (as in pass 3).

## P4.7 `research/module_server.md` against the code (the t6 paragraph)

1. "refused when the folder is a link or leads out of the home": true for a folder that is there. A folder that is not there yet is not asked, and a home that is a link is not seen (M8).
2. "`TemporaryFileSystem=<job cover>:ro` (no other job, no worktree, no coder home)": the coder home, yes. Other jobs and the worktree only when they lie under the cover or a hidden folder (M10 point 2, S26).
3. "The full copy … is deleted before the coder starts, whatever the unit came to": not when the runner dies while the unit runs; and no sweep removes it (M10 point 1).
4. "shared read handle": three of the four reads (S32).
5. "then the sweep of E2 removes them": the retention does, with the job folder, after 30 days or 200 jobs (S27).
6. Missing: the tokens are not searched for in what a launch prints (M9), and what K3 does not find (S30).


---

# Pass 5 — the agy coder, s6 t1 to t5 (`ee1271b7` on `feat/v2-ep3-e3-agy`, diff `367ed703..ee1271b7`) (2026-11-26)

Judge: Fable (security judgement role). Read-only: no edit, no build, no test run, no model call, no vendor CLI, no probe unit in this pass.
Every path and line below is at `ee1271b7` (`git show ee1271b7:<path>`). This section is in the briefs copy only; nothing was written into a tree.

Threat model, as before: a model under injection inside its unit, and the unit of a later vendor of the same job.

Read: the product diff of the range; `src_mcp/runners/Reviewers/AntigravityCoderGuard.cs`; `src_mcp/runners/Consultation/{AgyCoderVendor,AgyTurnCheck,AntigravityConsultant,CoderVendors}.cs`;
`src_mcp/runners/Toolchain/{AgyCoderUnit,AgyCoderPreflight,CoderBoundary,CoderTurns,BoundaryUnit,BoundaryJob,SignInRefresh}.cs`; `src_mcp/src/Server/RoleJobs/{CoderTurnCheck,CoderFixLoop,RoleFailover,RoleModelLauncher,RoleStops,CoderOsLayer}.cs`;
`src_mcp/src/Program.cs` (the hook mode); the test names of `AAgyCoderFixTurnsTests`; the judgement, section 14 whole (G15, A1 to A13); `RESULTS_agy_coder.md` sections 0 and 10; the saved streams of the measure (`out_agy/`) for the shape of a denied event.
Every "holds" is by reading. agy stays not launchable in the product's table (`4bfde22a`).

## P5.1 Verdict

**Must-change: 4 (M11 to M14), then ready as built.** The guard, the unit and the preflight are sound in themselves; the four are about where a check is NOT asked.

| # | What | Where |
|---|---|---|
| M11 | The turn check is asked only for a turn that ended clean. A turn that timed out, exited non-zero or was cancelled is a crash and fails over: its mode signs and its events are never looked at | `CoderTurnCheck.cs:21-24`, `AgyTurnCheck.cs:52-55` |
| M12 | agy's own preflight replaces the claude coder's rows and drops two of them: the claude key pool cannot be read, the person's claude sign-in is not in the unit. The properties are asked for; nothing observes them | `AgyCoderPreflight.cs:23-44`, `AgyCoderUnit.cs:60-62` |
| M13 | `/tmp` and `/var/tmp` are still ONE folder for every vendor of a job | `BoundaryJob.cs:147-150`, `AgyCoderUnit.cs:58-59` |
| M14 | The refresh is not forced, so with the 45-minute cap most launches are refused (question (b)). Closed side; by reading, to be measured | `CoderBoundary.cs:139-140`, `AgyCoderVendor.cs:51` |

**(a) G15:** a safe relaxation exists, in one exact form (P5.3). It changes a must-change the owner named, so it needs the owner's word and one measured turn first.
**(b) 45 minutes:** right as an upper bound (45 + 1 + 5 = 51 of about 60 minutes). It does not work as built (M14), and it makes almost every turn run a refresh (P5.4).

## P5.2 Must-change

**M11. A turn that ended badly is never checked.**

- Where: `CoderTurnCheck.StopFirst` calls the vendor's check only when the process ended `ExitCode: 0, TimedOut: false, Cancelled: false` (`CoderTurnCheck.cs:21-24`); it is the one caller (`RoleFailover.cs:542`, `CoderFixLoop.cs:166`). For any other end `StopOfLaunch` answers a crash or a limit, and the job goes to the next model.
  `AgyTurnCheck.Check` looks at the mode signs first, "for every result" (`AgyTurnCheck.cs:46`), and the test `TheCheck_AFailedLaunchIsNotTiedToTheGuard_ButItsModeSignsStillEndTheJob` holds that for the pure function. The wiring never reaches it. And `:52-55` answers `Fine` for every such turn without a look at its events.
- Why it matters: G15 is, by the judgement, the ONE check at the time of a job that notices a tool the hook was not called for (A4, A10). It notices after the tool ran. A model that got such a tool through has only to keep the turn going until its deadline: the turn is `TimedOut`, nothing is compared, the worktree is put back and the next model starts. The job can end green, and nothing says a boundary broke.
  The same for a turn with `warning: --mode` on stderr or a `denied_actions` result that then exits non-zero.
- Smallest fix: the check is asked for every coder launch that has a process. On a cut stream it answers `Boundary` for what one side alone proves: a mode sign; an event that ended `DONE` with no record or with a `deny` record; a `DONE` event of another tool. It does not ask for equal counts or for an event that did not end (those are what a cut explains). A `Boundary` wins over crash and limit.
  One test through the rig: a turn that times out after a `DONE` event with no record ends the job `coder-boundary`, no failover.

**M12. Two rows of every other-vendor launch are gone for agy.**

- Where: `CoderBoundary.PreflightAsync` takes `plan.Items` in place of `BoundaryPreflight.CoderItemsFor` (`CoderBoundary.cs:243-256`). agy's items (`AgyCoderPreflight.cs:23-44`) have no `keys-folder-hidden` and no `sign-in-not-in-unit`. `AgyCoderUnit.Properties` asks for both (`BoundaryUnit.KeysHidden`, `SignInHidden`, `:60-62`).
  Pass 3 called the keyless launch safe BECAUSE those two rows ask from inside ("a vendor's own property list that forgot the hiding is refused by those rows"). KEY_POOL_SPEC: the pool is in no boundary unit, and a row reads it from inside. `BoundaryUnit.cs` itself: a property is a request until the preflight has seen the answer.
- Scenario: the data folder is outside the hidden folders (`/var/lib/coai`, `/srv`); a release of systemd or a path spelling makes `InaccessiblePaths=-` a no-op (the dash hides a wrong path in silence). agy's unit reads the claude pool keys and has the whole network. No row says so. For the real `~/.gemini` the agy row does ask (`AG-R1`), so this half is only claude's material.
- Smallest fix: the two shared fragments (`CheckKeysRow` with the folder AND the keys file, `CheckSignInRow`) in agy's script and items; they are `internal` now. Their degrade rows in the theory. Add one row for E7 by observation: `<job>/coder/<another vendor>` is not there.

**M13. One temp folder for all vendors.**

- Where: `BoundaryJob.CoderTempFolder` is `<job>/coder/tmp` and `CoderVarTempFolder` `<job>/coder/vartmp` (`BoundaryJob.cs:147-150`); the claude, the codex and the agy unit each bind them as `/tmp` and `/var/tmp` (`CoderUnit.cs:63-64`, `CodexCoderVendor.cs:134`, `AgyCoderUnit.cs:58-59`). Nothing empties them between two vendors (`LeaveVendor` removes one file of the home).
  C3 of pass 4 was answered for the home (`<job>/coder/<vendor>/home`). The temp folders are the same C3.
- Scenario, both directions: a codex model (it has a shell) leaves files under `/tmp`; agy's CLI, coai's handler and `sh` run in agy's unit with `TMPDIR=/tmp` and agy's token. And what agy's CLI leaves there is read by a later codex model.
  The product already treats these folders as a place where a CLI may leave its credential: after a keyed launch it searches them for the pool key (`RoleModelLauncher.cs:169`). What agy writes under `/tmp` was not measured.
- Smallest fix: `<job>/coder/<vendor>/tmp` and `vartmp`, in the three units and in the folders each vendor makes. The `tmp-is-the-jobs` marker row follows the path.

**M14. The refresh does not give a new token for a token that is not expired.** See P5.4.

## P5.3 Question (a): can G15 item 4 be relaxed?

What the literal rule buys. The check runs AFTER the turn, so it stops nothing inside the turn. It keeps the turn out of a commit, ends the job, and it is the only sign at the time of a job that the agent file no longer limits the tools (AG-A1 is class `release`; A10: no version gate).
What it costs: the model's OWN list holds `manage_task` (AG-A1, measured), which the guard denies. One such call in a healthy turn ends the job. Today a denied `write_file` outside the worktree passes and a denied `manage_task` does not, though nothing happened in either.

**A relaxation is safe in this form, and not wider:**

1. The tie stays exact for EVERY event: one record, the same conversation, step and tool name; the counts are equal.
2. An event of a tool outside the three is tolerated only when all three hold: its record says `deny`; its state is `ERROR`; and its `tool_info.error.message` is agy's own line for a hook denial with coai's reason in it (measured shape, `out_agy/agy_t1.jsonl`: `"error":{"type":"TOOL_ERROR","message":"tool call denied by pre-tool hook: <the hook's reason>"}`).
   The third condition is the important one. `ERROR` alone is also what a tool that RAN and failed looks like. The message ties the error to the denial, and coai's reason texts are three constants.
3. Everything else ends the job as today: an event of another tool that ended `DONE`; an `allow` record for another tool; `ERROR` without the hook's line; no record; two records; a record with no event.
4. The mode signs stay literal (`warning: --mode`, the command-permission line, `denied_actions`): they mean a call reached agy's permission layer, past the agent file AND the guard.
5. The sign is kept, not dropped: the envelope names the tolerated tools and their counts for each turn. A name that is not in the measured own list of the release in the table (today `manage_task`) also raises a notice: on that day the agent file stopped limiting the tools, and the job runs on the hook and the mode alone.

Stricter and also fine: tolerate only names of the measured own list, and end the job for any other name. It keeps item 4 as a stop for the case it was written for, at the price of refused jobs on the day agy adds a tool of its own. With no version gate (A10) I would take 1 to 5.

Conditions: (i) the owner's word and a ledger line, because G15 is in the judgement as a must-change the owner named; (ii) one measured turn with coai's OWN handler (14.9: it has not met agy yet): a denied `manage_task` and a denied `run_command` each give `ERROR`, the hook line with coai's reason, one record, and the turn goes on; (iii) M11 first, or the relaxed rule is also skipped on every bad end.

## P5.4 Question (b): the 45-minute cap

- **The arithmetic holds.** `RoleModelLauncher.cs:69` cuts every agy turn to 45 minutes, fix turns too. The gate needs the unit's end (45 + 1 minute of backstop) and 5 minutes: 51 minutes. A new token has 3599 s. The gate and the row `AG-R2` read the real `expiry`, not the 3599, so a shorter life than measured refuses and does not slip.
- **It does not work as built (M14).** `MakeCopyAsync` runs the refresh unit when the real file has less than the 51 minutes (`CoderBoundary.cs:139-140`), on a byte copy of the real file. agy refreshes "at start-up when it is expired" (`RESULTS_agy_coder.md:16`), and both measured refreshes were of an expired file (the real one, and a copy with `expiry` set 2 hours back, section 10.5).
  For a token with 1 to 50 minutes left, `agy models` has nothing to refresh, the copy is as short as the real file, and the gate refuses the launch; a refused launch ends the job. So a launch is possible in the first 8 or 9 minutes of a token's hour, or after it expired. A fix turn almost always falls in the other 50. Each refusal still ran a refresh unit with the owner's refresh token (A12), for nothing, and its sentence ("start agy once yourself") does not help: the person's own agy will not refresh either.
  By reading, not measured: agy may refresh a token that is near its end. One `agy models` on a copy with 30 minutes left settles it.
- **Smallest fix:** coai sets `token/expiry` of its OWN scratch copy to a past time before the unit runs. That is the measured row. The real file is not touched, and the coder's copy is cut from the refreshed file as today.
- **What the number costs, for the owner.** With a need of 51 minutes of 60, nine turns of ten run a refresh. Each one puts the refresh token into the refresh unit (A12) and is one more refresh on coai's copy (A5: the day the vendor rotates, the owner's own file dies).
  A cap of 30 minutes needs 36, and a real token younger than 24 minutes needs no refresh. So: 45 as the upper bound is sound; the default should be as short as agy turns allow, and the envelope should count the refreshes of a job.
- A turn that reaches the 45 minutes is `TimedOut`: with M11 open it is also a turn nobody checks.

## P5.5 Should

- **S33. A stream that names no tool event passes with a changed worktree.** `Events` skips an update with no `step_index` (`AgyTurnCheck.cs:221`), and a release that renames the event gives zero events. With zero records too (the hook was not called) the turn is `Fine`. A13 accepts "a release whose stream leaves out a tool event".
  A cheap sign closes most of it: the worktree changed in the turn and the stream has no `DONE` event of `write_file` or `edit_file` is `coder-boundary`.
- **S34. The login address** is kept out of the sentence, the evidence and the notice for the three recorded texts (`RoleStops.cs:146-148`). For any other agy stop the evidence is the raw stdout and stderr (`ConsultFailures.EvidenceOf`). A new wording of the login line would carry the address there. For agy, never keep raw stderr as evidence.
- **S35. A second plant function.** `AgyCoderPreflight.PlantOtherJob` (`:178-198`) is `CheckBoundary.PlantAnotherJob` written again; it does not replace a leftover folder. One function for both (`common/security.md`).
- **S36.** `HandlerFolders` binds the WHOLE folder of coai's program read-only into agy's unit (`AgyCoderUnit.cs:75-80`). Where that is `~/bin` or `~/.local/bin`, everything in it is shown. Say it, or bind the file.
- **S37.** The records file and the home lie in `<job>/coder/agy`, writable in the unit. That is right (the handler appends from inside), and it means the tie and the records rest on "the model has no command", as the guard does. Say it beside G15: the stream is the witness the model cannot write.
- **S38.** The guard's own deadline (`DecideWithin`, `:248-262`) answers `deny` while the work may still append an `allow` record. The process ends at once, so in practice no record; either way the tie breaks and the job ends. Closed; a remark is enough.
- **Open from the earlier passes:** S2 (path sockets), S26 (the refresh unit's own list: check that `KeysHidden` is there now), S28 to S32, and S33 to S38 above.

## P5.6 Confirmed safe

- **The guard.** With a guard file in the handler's folder every path of `RunHook` writes an explicit decision: no file to read, text that is not JSON, no `Path`, a `Path` that is not a full path or is given twice in another case of the letters, no `cwd`, no `workspacePaths`, an exception, the deadline: each `deny`. An `allow` whose record cannot be appended becomes `deny` (`:294`). A decision that cannot be written is exit 1, and a handler that cannot start is `exec`'s own non-zero exit; "exit 0, no output" (A4) is on no path.
  The case is chosen by coai's file in the read-only guard folder, never by the payload; and the worktree must be named by BOTH that file and the payload's `workspacePaths`, so a guard file planted elsewhere allows nothing. The target is resolved through every link before the place rule, so a link in the worktree leads nowhere; `.git`, `.agents` and `.codex` of the worktree and everything under the guard folder are denied, for reads too. So the model cannot read its own sign-in copy with a tool (row 10 of `AG-G1` asks exactly that).
  The guard folder is written and read back before every launch, hashed again inside the unit (AG-G3) and bound read-only (AG-B3).
- **The launch.** The flag set AG1 with `--mode accept-edits` and `--agent coai-coder` on every argv, fresh and resumed; the working folder is the guard folder, and a launch without one, or with the worktree, throws before a process (`AntigravityConsultant.Coder`); the row `AG-B1` asks it inside.
  The unit hides the main checkout and the coder root (tmpfs, only the main `.git` read-only and the worktree bound back), covers the job folders and binds only `<job>/coder/agy`; so another vendor's home is not there. The environment is seven fixed names, compared name by name inside (U7). The program and the handler's folder are read-only (U9).
- **The sign-in.** A copy with the access token, its type and its expiry and no refresh token, fresh for every turn, written by `SignInCopy` (M8 to M10 of pass 4 are in this branch), gated before every launch and asked again inside the unit (`AG-R2`: no `refresh` in the file, the expiry past the unit's end and the margin). The real `~/.gemini` visible in the unit refuses the launch (`AG-R1`). `SignInHidden` now lists the codex and the agy real files beside claude's.
- **The preflight** runs in the exact launch unit: the same property list, working folder and environment as the launch, with the vendor's own rows, and its 16 probes of the guard go through the exact command of `hooks.json`. Its records are put away before the turn (`BeforeStart`), and a file that cannot be put away refuses the launch. U8 is new for a coder's unit: another job's folder is asked (S20 of pass 2, for agy).
- **The turn check, for a clean turn.** Unreadable records refuse; a line that is no record refuses; a record with no event, an event with no record, two records for a step, `DONE` without `allow`, `deny` without `ERROR`, an event that did not end, unequal counts: each ends the job `coder-boundary` before anything of the turn is read or committed (`StopFirst` runs before the launch's own stop). The sentences name a step and a tool, never a path or an argument.
  A resumed turn in another conversation, or with the not-found line, is a crash and never continued. The init line is not compared (A9, AG-M3).
- **The failover texts.** A lost agy sign-in is read from the three recorded texts and the result's error; the stop's sentence is fixed and its evidence is the sentence (`NoEvidence`), so the address of that start is in no envelope, evidence file or notice.
- **The seam.** `CheckTurn` with the job fails closed without it (`AgyCoderVendor.cs:60`); `TurnDeadline` only shortens; `NotProvedByPreflight` puts the four `release` ids into the envelope by name, so a reader is not told the rows proved them; `PreflightPlan` tidies in a `finally`. The `BoundaryPreflight` fragments are `internal` to the assembly, no wider.


---

## Pass 6 — codex coder (s5 t3–t5), head 424222fc

Judge: Fable (security judgement role), 2026-11-26. Read-only for product code: no edit, no commit, no push.
Branch `feat/v2-ep3-e3-roles`, range `367ed703..424222fc`: `46b78dcc` (t3), `20fb7b6f` (t4), `424222fc` (t5). Every path and line below is at `424222fc`.
The agy part of the range (to `ee1271b7`) was pass 5. It is read here only where codex uses the seam.

Threat model, as before: a model under injection inside its unit, and the unit of a later vendor of the same job. New for codex: the model has a shell.

Read: the product diff `ee1271b7..424222fc` (28 files); `CodexCoderVendor`, `CodexPreflight`, `CodexPreflightRows`, `CodexPreflightScript`, `CodexDoctorReport`, `CodexToolList`, `CodexSessions`, `CodexTurnCheck`, `LoopbackListener`;
`CoderBoundary`, `CoderTurns`, `CoderUnit`, `CoderProgram`, `BoundaryUnit`, `BoundaryFolders`, `BoundaryPreflight`, `SignInCopy`, `SignInRefresh`, `GitScratch`; `CoderTurnCheck`, `RoleFailover` (the read of a launch), `CoderFixLoop`, `RoleStops`;
the four codex test files and `CodexVerbs.cs` of the fake; the judgement sections 8, 13 and 13.11; `RESULTS_codex_coder.md` sections 1 to 3, 9.7 and 10; the saved streams and doctor reports in `v2/spikes/e3s5_measure/out_codex/`; `shared/coder-boundary-releases.json`.
Also read, on the coordinator's word: `c7f2e259` on `feat/v2-ep3-e3-agy` (the pass-5 fixes; parent `ee1271b7`, not merged into this branch).

Run by the judge: three scratch units on made-up folders (P10, P12), and one small .NET program on made-up files, under the shared lock (P11). All scratch files are removed and no unit is left.
Not run: no build of the repository and no test of it (the test binary is older than the head), so every "holds" about a test is by reading. No codex, no agy, no model call. No key file and no environment was read. The real HOME was not touched.

Owner decisions that this pass follows: no version, hash or pin gates anything (so U9 compares no sha256: correct); the risks C1 to C7 and C9 are accepted; a secret in program output does not stop a job.

### P6.1 Verdict

**Must-change: 3 (M15, M16, M17).** The preflight rows, the turn rule and the sign-in are sound in themselves. The three are about what stands around them.

| # | What | Where |
|---|---|---|
| M15 | coai itself writes outside the worktree. On the host it writes three marker files and one program copy THROUGH a link that a coder with a shell left at their names | `BoundaryFolders.cs:26-28`, `:45-49` |
| M16 | The codex turn check is made only for a turn that ended clean. At this head a bad end is not checked at all, and the next fresh launch deletes the rollout files unseen. After the merge of `c7f2e259` the check is asked for every end and then answers wrong in both directions | `CoderTurnCheck.cs:22`, `CodexTurnCheck.cs:30-50`, `CodexCoderVendor.cs:224` |
| M17 | The folder of the program is bound into the unit and is opened for reads to the model's commands. A program that lies in the person's home brings the whole home back. No row asks if the real home can be READ | `CoderProgram.cs:35-43`, `CodexCoderVendor.cs:143`, `:195`, `BoundaryPreflight.cs:107` |

**Is it safe to turn on `launchable` for codex (s5 t5 d4)? Not at `424222fc`.** It is safe when all of this is true:

1. M15, M16 and M17 are built and green.
2. `c7f2e259` is merged (the M11 wiring and M13). The second verifier decides if it closes M11 to M14; this pass only says what codex needs from it.
3. The product's own preflight ran ONE time on the real codex, in the real unit, with no model, and every row was green (P6.5, point 3). This costs 0 paid calls.

The test `D4_EveryMustChangeOfTheJudgementsAddendum_IsBuilt…` holds the list of the addendum only. Add M15 to M17 to what the flip rests on.

### P6.2 What was measured

| # | Question | Answer |
|---|---|---|
| P10 | A unit with a tmpfs over a stand-in for `/home`. In it a stand-in home with a made-up key file and a made-up sign-in file. The sign-in file is `InaccessiblePaths`. Case A: a narrow sub-folder of the home is bound read-only (the program folder). Case B: the home itself is bound read-only (the program lies directly in it) | A: the key file is **not readable**. B: the key file is **READ**, the sign-in file is not, a write is refused. So in B every row of today is green |
| P11 | .NET 10, made-up files: the three host calls of `BoundaryFolders.Prepare` when a link stands at the name | `File.WriteAllText(link, "")`: the link's target went from 21 bytes to **0**. A link to a name that is not there: a **new file was made** at that name. `File.Copy(source, link, overwrite: true)` and `File.SetUnixFileMode`: the target now holds the **bytes of the source** and has mode **0700**. The link itself stays in each case |
| P12 | A unit with `InaccessiblePaths=-<file>` on a file that the unit can see | `[ -e file ]` is **true** (an empty file of mode 0 stands there). `[ -r file ]` is false and a read is refused |

Also seen: on this machine `/tmp` is a tmpfs. So every test bed is on a tmpfs, and the unit's own tmpfs over `/tmp` hides the stand-in home in every codex test.

### P6.3 Must-change

**M15. coai writes through a link in the worktree, on the host.**

- Where: `BoundaryFolders.Prepare`. `:26-28` is `File.WriteAllText` of the marker `.coai-job-<job id>` in the unit's two temp folders and in the WORKTREE. `:45-49` is `File.Copy(…, overwrite: true)` and `File.SetUnixFileMode` of `<worktree>/.coai-preflight.exe` (a machine with WSL).
  It runs on the host, outside every unit, before every coder launch (`CoderBoundary.cs:201`) and before every round of checks (`CheckBoundary.cs:62`, `:113`). No name is looked at before the write. The job id is in the name of the worktree folder (`coai-coder-<job id>`).
- This is E2 code, not code of the three commits. codex makes it reachable in a turn: it is the first coder with a shell. On the real codex a command of the model made a link in the worktree (results section 4, row B8).
- Scenario: text in the repository tells the model to run `ln -s <main checkout>/src/Program.cs .coai-job-<job id>`. The vendor sandbox permits it: the link is in the worktree, and its target does not have to be visible. The turn ends clean and the link is committed.
  The round of checks starts, `Prepare` writes the marker, and the file in the MAIN checkout is empty (P11). The same for the person's real `~/.codex/auth.json`, `~/.ssh/authorized_keys`, or the keys file of the pool.
  A link to a name that is not there makes a new file, for example `<main>/.git/index.lock`: git then stops for the person. On a WSL machine the name `.coai-preflight.exe` gets the bytes of a Windows program and mode 0700.
  `Tidy` then removes the link (`:58-61`), so nothing shows it. No row, no stop. "The main checkout is byte-equal" (U2, B2) is broken by coai's own write.
- Without codex too: a check step runs code that the coder wrote, with the worktree and its `/tmp` and `/var/tmp` writable. A test file of a claude coder can leave the same links. So E2 has this today.
- Smallest fix: one function for "a file of coai's own in a folder that a unit can write". Look at the name without following it; delete a file or a link by its own name; refuse a folder; then make the file with `FileMode.CreateNew`.
  `CodexCoderVendor.WriteOwnFile` (`:311`) already has this shape. Copy the program through that handle and give the mode when the file is made. Use it for the four names.
  Tests: a link at each of the four names; the target is byte-equal after `Prepare`, and the unit still finds its marker.

**M16. The codex turn check is not made for a turn that ended badly.**

- At `424222fc`: `CoderTurnCheck.StopFirst` asks the vendor only for `ExitCode: 0, TimedOut: false, Cancelled: false` (`CoderTurnCheck.cs:22`). This is M11 of pass 5, and it holds for codex in the same way.
  Scenario: a release drops the effect of a switch (C6: nothing gates a release). A turn uses the web search, or starts a child (a second rollout file). The model then keeps a command running to the deadline, or the CLI exits 1.
  The turn is a crash and the job fails over. CX-T4 and CX-T5 are never asked. The next attempt is a fresh launch, and `PrepareLaunch` DELETES `sessions` (`CodexCoderVendor.cs:224`): the only record of the child is removed before any check read it. The job can end green on the next model.
- After the merge of `c7f2e259` (the check is asked for every launch with a process): `CodexTurnCheck.Check` has no rule for a cut stream, and its answer is taken before the launch's own stop. By reading:
  1. **A crash wins over a violation.** A stream with no `thread.started`, or with another thread, is a `Crash` at `:35-43`, before the items are read at `:45`. A `web_search` item in that stream fails over.
  2. **What a cut explains ends the job.** A last line that was cut is "cannot be read" (`:30`). No rollout file of the thread is `Boundary` (`:50`). So a turn that the deadline killed in the middle of a line ends the job `coder-boundary`.
     A lost sign-in is the same: the saved stream `cx_x4b_exp60_host.jsonl` has a thread and exit 1. If the CLI wrote no rollout for it (not measured), the job ends `coder-boundary`, the other models are not skipped, and the person is not told to run codex. The same for a limit.
- Smallest fix, in `CodexTurnCheck`: read the items first, for every end. For a turn that ended badly answer `Boundary` only for what the turn itself proves: a bad item (rule 13.11, items 2 to 4), a rollout file of another thread, a call named `collaboration.*`.
  For such a turn leave "no thread", "no rollout file of the thread" and "a last line that cannot be read" to the launch's own stop (crash, limit, lost sign-in). For a clean end keep all of them as they are.
  Tests through the rig: a turn that times out after a `web_search` item ends `coder-boundary` with no next launch; a turn that exits 1 with a second rollout file ends `coder-boundary`; the two lost-sign-in fixtures still skip the vendor.

**M17. The program's folder can show the person's home, to the unit and to the vendor sandbox.**

- Where: `CoderProgram.Resolve` (`CoderProgram.cs:35-43`) refuses a program inside the worktree or the job folder, and a folder that is the root. It takes every other folder. The codex unit binds that folder read-only (`CodexCoderVendor.cs:143`).
  For codex only, the job's `config.toml` also gives the model's COMMANDS read access to it (`:195`: `"<program folder>" = "read"`). So both layers open the same folder.
- The rows: U6 says "the real home cannot be read or written". `HomeRow` asks the write only (`BoundaryPreflight.cs:107-113`). The two sign-in rows ask two files. Nothing asks if the home is there.
- Scenario: the person keeps the binary in the home folder (`~/codex`), or in a folder with private files (`~/Downloads`, `~/bin`). The real path is `/home/<user>/codex`, so the folder is `/home/<user>`, and `BindReadOnlyPaths=/home/<user>` puts the home back over the tmpfs of `ProtectHome` (P10, case B).
  A command of the model reads `~/.ssh/id_ed25519`, `~/.config/gh/hosts.yml`, `~/.netrc`. What a command prints is the model's input, so it is at the vendor. Every row is green.
  One row goes red by accident: when the person has a claude sign-in file, `sign-in-not-in-unit` sees the placeholder of P12 and says "visible". A person without that file gets no red row. The measured install is narrow (`~/.codex/packages/standalone/releases/<release>/bin`), so this is not the usual case. It is silent when it happens.
- Smallest fix: (1) refuse a program whose folder is the real home or a folder above it, with a sentence that names the folder. (2) One arm in the home row: the real home is not there, or it is a tmpfs of the unit; else "the real home can be read".
  (3) Put the folder that is shown into the job's record, so a wide folder (`~/.local/bin`) is seen by the person (S36 of pass 5, now for a vendor whose model has a shell). Tests: a program in a stand-in home is refused; the row goes red when the home is bound back.

### P6.4 Not a new must-change: the state of pass 5 for codex

- **M13 (one temp folder for all vendors), point 8.** At `424222fc` the codex unit has its own tmpfs as `/tmp` (`CodexCoderVendor.cs:139`): nothing is shared there. Its `/var/tmp` is `<job>/coder/vartmp` (`:140`), the folder of every vendor: this half of M13 holds for codex.
  `c7f2e259` makes it `<job>/coder/codex/vartmp` and moves the markers with it. **After the merge the codex part of M13 is closed by that commit.** No new must-change.
  For the merge: `ACodexCoderPreflightTests.cs:129` drops the bind of `job.CoderVarTempFolder`, which is claude's folder after that commit. The row `tmp-is-the-jobs` then does not go red in the theory until the test names codex's folder.
- **M11.** The wiring is in `c7f2e259`. The codex half is M16.
- **S26.** The refresh unit of agy still has no `SignInHidden` (`SignInRefresh.cs:37-51`). So "the real `~/.codex/auth.json` is hidden in ALL units" is true by a property in four units and by `ProtectHome` only in that one.

### P6.5 The nine points

| # | Verdict | Short answer |
|---|---|---|
| 1 Preflight rows | **OK, with M17 and S39, S40, S42** | Every `pre` id has a row (22 ids; K1 and K2 by the check preflight; a test holds the two sets equal). The rows run in the launch's unit: the same properties, working folder and environment; only `RuntimeMaxSec` differs. `Budget` is 4 minutes, the sum of the inner deadlines is about 3. `Rows` first asks the shared reader, so a unit that did not end clean, or a row that is missing, refuses. No row carries a word of a program: the sentences are fixed, the three computed rows say counts. No token is on the command line or in a row. A row can pass with its property absent in four places: U6 (M17), the three sandbox rows (S39), U2 and U3 on a tmpfs disk (S40) |
| 2 Model source | **SHOULD (S41)** | A changed model line passes: the expected text is built from the line it checks. It does not open a hole today: no unit of the job is alive between the write and the read, a command of the model cannot write the home (CX-V2), and the launch's `-m` wins over the file. The cost is that CX-T1 is then asked for another model than the launch's |
| 3 Real codex | **A model-free run is needed. 0 paid calls** | See below |
| 4 Turn check | **MUST (M16); the rule itself OK, with S43 to S45** | The rule is 13.11 item by item, with one place more open (S43). Against the 23 saved real streams: only the item types of the rule occur, the two real list answers parse as empty, the real `web_search` and `collab_tool_call` items end the job. The delete: the home is a real folder at its own real path, `.codex` and `sessions` are real folders (no link is followed), and the deleter removes a link by its name. It touches nothing outside `<job>/coder/codex/home/.codex/sessions`. It does hide another thread when the turn before was not checked (M16) |
| 5 Sign-in | **OK** | The copy has the kept fields and the placeholder, mode 0600, one name; the gate is asked before every launch with 60 minutes of margin; codex has no refresh step, so its refresh token is in no unit; rows ask the copy and the real file from inside. d3 and the emulation: acceptable, see below |
| 6 Boundary | **OK for the unit; MUST for the host (M15)** | A write at the main checkout or beside the worktree goes into the unit's tmpfs and is gone with the unit; its pages count against `MemoryMax`. `<main>/.git`, the `.git` pointer, the submodule mounts and the program folder are read-only. Such a write gives no stop: only a read-only bind answers, so the name of test D1 ("each is stopped by the OS") says more than the unit does, and the remark of the class says it right. The road to the disk is coai's own write (M15) |
| 7 Exemptions | **OK** | Two rows, each tied to the file, the line and the sha256 of that line (both hashes checked: lines 119 and 137), class `raw-temp`. Both lines are arguments of `bwrap` for the sandbox's own `/tmp`, never a path of the host |
| 8 `/tmp` | **Closed by `c7f2e259` after the merge** | P6.4 |
| 9 Other | **MUST (M15); else OK** | No secret reaches an argv or a log in the range. The stop sentences are fixed or go through `Redaction.SafeText`; an item type is at most 40 plain characters. The listener is on 127.0.0.1 only, accepts, closes and reads nothing. The real sign-in file is opened for reading only |

**Point 3 in full.** The parsers are sound against real material: the doctor reader finds its five fields in the three saved real reports, and the tool reader is the logic of the measure's own `toolnames.mjs`. What was never run is the SCRIPT against the real CLI, in the unit, with a copy. One run with no model must show:

1. Every row is green on a healthy machine. `codex sandbox -P coai -- /bin/sh -c …` prints both `sb-started` and `sb-done`.
2. The real sandbox does not list `<main>`. The unit shows `<main>/.git`; if the sandbox shows it too (git needs it), the row CX-V3 is red for every job.
3. `debug prompt-input` and `doctor` take `-c model="…"` and answer in 40 seconds inside the unit. The measure ran the tool list outside the unit, with no `-c model`, for one model (J15).
4. The three probes leave no rollout file in `sessions` and do not rewrite `auth.json` or `config.toml`. A rollout of a probe would end every job at CX-T5.
5. No daemon of the CLI keeps the pipe of the script open. If one does, the unit runs to its deadline.

A red result is possible in each of the five; all are on the closed side. A false green on the real codex is possible in S39 and S41.
**Paid calls: 0 are needed for this verdict.** `sandbox`, `debug prompt-input` and `doctor` make no model request, and the count of 30 is of `exec` calls. **2 are recommended** before the flip, so that it does not give refused jobs only: one turn 0 and one fix turn through the product.
They show the item types at effort `medium` (the measure used `low`; a `reasoning` item ends every job by rule 4 until an addendum adds it), the rollout names after a resume, and the report file. 23 of 30 are used, so 7 are left.

**Point 5, the emulation.** In a job the rows CX-V1 to CX-V6 run the REAL `codex sandbox` of the installed CLI. The `bwrap` of the fake is only how a test makes a row go red. What stands for the real sandbox is the measure of two days on 0.171.0 and the owner's risk C1.
So the emulation is acceptable for the flip, with the model-free run above. Test d3 is right to say it out loud: in the unit alone the copy can be read.

### P6.6 Should

- **S39. The three sandbox rows have a control outside the sandbox only.** CX-V3, CX-V4 and CX-V5 say "ok" when the probe printed no token (`CodexPreflightScript.cs:103-109`, `:142-149`). A probe whose tools do not run inside the sandbox (`head`, `ls`, `bash`, `timeout`, `grep` not on its `PATH`) prints none, and the rows are green.
  The remark of the class says such a probe is a red row. Add one positive token for each: a read of a file in the worktree, a list of the worktree, `/proc/self` there, the tools found. A row without its token is "nothing was observed".
- **S40. U2 and U3 judge "covered" by the type of the file system** (`:30-44`). Where the folder itself is on a tmpfs (here: everything under `/tmp`) and holds nothing else, the row is green with no cover. Let the script leave a marker in `<main>` and beside the worktree, and let `CodexPreflightRows.Build` look on the HOST after the unit: a marker on the disk is a bad row.
- **S41. The model of the probes is read from the file that the row checks** (`CodexPreflight.cs:61`, `CodexCoderVendor.cs:287`). Give the launch's model to the plan through the seam. One test: a `config.toml` with only the model line changed refuses.
- **S42. `sign-in-not-in-unit` asks `[ -e … ]`** (`CodexPreflightScript.cs:53-56`). Where the home is not under a hidden folder, the property works and the row still says "visible" (P12): every codex job is refused there, with a wrong sentence. Ask a read, as `sign-in-is-the-copy` does.
  Ask agy's real file too: `SignInHidden` hides three files and the rows ask two (the shape of M12).
- **S43. CX-T4, three places.** A COMPLETED list call with `result: null` passes (`CodexTurnCheck.cs:123`); 13.11 wants one text block. An `item.*` event with no readable item is skipped (`:84`). `process.Truncated` is not read: a stream cut at 8 Mi characters is checked as far as it was kept. Each is "cannot be read", so each should end the job.
- **S44. CX-T5.** Only names `rollout-*.jsonl` are looked at (`:170`); a file of another name in `sessions` should end the job too. The call name is read as `payload.name` that starts with `collaboration.` (`:197`): no real rollout line is saved, and the test line is made up.
  Match also a `namespace` of `collaboration` and the six bare names; then no paid call is needed to prove the shape. `Directory.Exists` and the listing follow a link (not measured): ask `Lstat` first, as the delete does.
  Say in the doc that the rollout is the CLI's own file in a folder its process can write: the check rests on CX-V2, as the guard's records rest on "no command" for agy (S37).
- **S45.** `CodexSessions.Clear` deletes with `DeleteEvenIfReadOnly` (`CodexSessions.cs:27`), which lists the target of a link before and after. `sessions` is written by code that is not coai's: use `DeleteLeavingLinkTargetsUnread`, as the job folder's sweep does.
- **S46. The vendor's programs run although a unit row is already bad.** The script prints U1 to U9 and goes on to `codex sandbox`, the two tool lists and `doctor` (which contacts the vendor) in a unit that did not hold. Stop before the first `$prog` when a unit row is bad, and answer the other rows "not asked".
- **S47.** `CollectLaunch` runs before the question "is the unit gone" (`CoderTurns.cs:76`; `CodexCoderVendor.cs:268-273`). With a live unit a link can be put at `report.txt` between the look and the copy. Call it only for a unit that is gone, and bound the size of the copy.
- **S48. Tests.** No red for the third arm of `sign-in-is-the-copy` (the real codex file can be read); none for M16's roads; the break of `config-read-back` changes the first line, so it does not show S41.
- **S49. Small.** `IFS` stays a newline after the probes (`CodexPreflightScript.cs:163-168`; the rows after them do not split, so no effect today). The plan is made outside the `try` (`CoderBoundary.cs:243`): a throw after `LoopbackListener.Start` leaves the listener.
  U7 is asked in a unit without the names that cross by name (proxy, certificate, time zone; `CoderUnit.cs:27-28`): say in the doc that a launch can hold up to ten names more than the row saw.
- **Open from the earlier passes:** S2 (path sockets), S26, S29 (the gate reads the copy as the CLI left it; the codex copy lives as long as the job), S36.

### P6.7 Confirmed safe

- **The rows and the ids.** `CodexPreflight.Rows` answers U1 to U9 and the 13 codex ids of class `pre`; the test compares the table with the rows in both directions. CX-T1 wants the 11 measured names as a set, so one name more or less is red. CX-C3 and CX-C4 are red for a field that is missing; the exit code of `doctor` is not read.
- **The exact unit.** One function gives the properties to the preflight and to the launch (`UnitProperties`); a test compares the two argument lists but for the deadline.
- **Hostile text.** Every program's output is taken by the script (`$(…)`, stderr dropped) and reaches the host only as base64 in a `data` line; the host parses it as JSON and writes its own sentence. An item answered two times is a bad row. The fake's `HOSTILE` text and the two tokens of the bed are in no row and no sentence (by reading the test).
- **Controls.** CX-R2 first connects to coai's own listener, so "refused" is not "cannot connect". CX-V2 is judged by what the unit then sees, not by an exit code. U8 uses the shared stand-in of pass 2 (M5), not a copy of it.
- **The launch half.** Before every launch: the home is a real folder at its own real path, `CODEX_HOME` is a real folder, no `environments.toml`, `config.toml` and the schema are made new with `CreateNew` after a file or a link at the name is removed, and read back. The preflight then runs, on every launch.
- **The rule of the turn** for a clean end: 13.11 items 1 to 4, the started and the completed form; an unknown type ends the job and the reason names it; CX-S1 is a crash; CX-T5 refuses with no job, no file, an unreadable file, a file of another thread and an agent call.
- **The lost sign-in.** The three signs of 13.6 and the text that is none of them; only for an exit that is not 0; the cure is a fixed sentence.
- **`launchable`** is off in the table and a test holds it off.


## Pass 7 — fixes of pass 6 and of the pass 5 re-check, head 0c9538e7

Verifier: Fable (security verify role), 2026-11-27. Read-only for the branch: no edit, no commit, no push.
Branch `feat/v2-ep3-e3-roles`, head `0c9538e7` (fetched; equal to the remote head). Range `424222fc..0c9538e7`: `209ec300`, `9852b959` (M15 from E2, `39229214`), `62617190` (M16, M17, M11 leg (a), S-items), `c438f290` (agy `2bb2fbe4`: M11 leg (b), R-M15), `0c9538e7` (two test-only fixes).
Every path and line below is at `0c9538e7`.

Threat model, as before: a model under injection inside its unit, and the unit of a later vendor of the same job.

**What was done.**
- Read: the two finding texts (pass 6 here; `RESULTS_e3_pass5_recheck.md`); the product diff of the range (25 product files) and the new tests; the roads in `RoleFailover`, `CoderFixLoop`, `CoderWork`, `RoleRunner`, `CoderTurns`, `CoderOsLayer`, `CoderBoundary`.
- Built the head in a temporary worktree (`/home/jinx/coai-t3/wt-pass7`, detached at `0c9538e7`) under the shared lock. Ran six test classes there: **188 of 188 green** at the unchanged head.
- Took each fix out of the PRODUCT code of that worktree only (the tests stayed), built again (0 errors each time), and ran the tests again. Seven builds (P7.3).
- Added 7 probe cases of my own to that worktree only (P7.5). Each probe states the SAFE behaviour, so a red probe shows a gap.
- The temporary worktree is removed. No unit is left. The branch worktree is clean at `0c9538e7`.
- Not run: codex, agy, a model call. No key file and no environment was read. The real HOME was not touched. No Python.

**Owner decisions that this pass follows:** no version stops anything; the risks agy A1 to A13 and codex C1 to C7, C9 are accepted; G15 stays literal and the 45-minute cap stays until the owner answers.

### P7.1 Verdict

| Item | Verdict | In one line |
|---|---|---|
| M15 | **CLOSED** | The three markers and the program copy are made by one function: look without following, delete a file or a link by its name, a folder is a stop, then `CreateNew` |
| M16 | **CLOSED** | The codex check reads the items first, for every end. A bad end answers a boundary only for what is proved. A fresh launch does not delete a rollout that holds an agent call. Three small gaps are should (S50, S51, S52) |
| M17 | **CLOSED** (parts 1 and 2; part 3 is S36) | A program whose folder is the home or above it is refused, for every vendor. The home row asks a read |
| M11 (a) | **CLOSED** for the road. One gap in the same family is **M18** | A turn that the job's token cut is checked on turn 0, the repair turn and a fix turn. `coder-boundary` wins over `timeout` and `stopped` in all three places and in the envelope |
| M11 (b) | **CLOSED** | On a cut stream, an event of a tool outside the three is a boundary in every state |
| R-M15 | **CLOSED** | The temp sources are in `<job>/coder-temp/<vendor>/`, outside every writable bind. The names are also asked with `Lstat` before every coder admission. Each half holds alone |

**New must-change: 1 (M18).** It is for agy. It does not let a job end green.

**Left items:** S41, S48 and S36 may wait (P7.6).

### P7.2 The evidence, item by item

**M15.**
- `BoundaryFolders.WriteOwnFile` (`BoundaryFolders.cs:59-90`): `Lstat` (`:61`); a file or a link is deleted by its own name (`:63-64`); a folder is an `IOException`, so the job is refused (`:66-67`); the file is made with `FileMode.CreateNew` (`:70`); the mode is set on the open handle (`:88`).
- The four names use it: the markers in the two temp folders and in the worktree (`:26-28`) and the program copy (`:45-46`).
- Both callers go through it: a coder launch (`CoderBoundary.cs:209`) and a round of checks (`CheckBoundary.cs:113`). `Tidy` deletes by name (`:93-99`).
- A model can put a folder at the marker name. Then every later launch and check round is refused. This ends its own job; it is on the closed side.

**M16.**
- The check is asked for every end: `CoderTurnCheck.StopFirst` has no condition on the exit (`CoderTurnCheck.cs:22-25`).
- `CodexTurnCheck.Check` reads the items first (`CodexTurnCheck.cs:41-44`). Then a clean end goes to `Whole` and a bad end goes to `Cut` (`:46`).
- `Cut` (`:72-73`) answers a boundary for a rollout file of another thread and for an agent call (`Rollout(…, whole: false)`, `:191-225`). No thread, no rollout, a cut last line, a rollout line that cannot be read: not a boundary. The launch's own stop is then read (crash, limit, lost sign-in).
- A fresh launch asks `HoldsAnAgentCall` before it deletes `sessions` (`CodexSessions.cs:25-29`, `CodexTurnCheck.cs:297-302`; called at `CodexCoderVendor.cs:227`). A refusal there is `boundary-refused`, so the job ends `coder-boundary`.

**M17.**
- `CoderProgram.HomeProblem` (`CoderProgram.cs:51-65`): the folder of the program is the home, or the home is inside it.
- Asked in `CoderBoundary.ProgramOf` (`CoderBoundary.cs:34-39`) for the properties (`:55`), the admission (`:203`) and the run (`:298`), and in the codex `PrepareLaunch` (`CodexCoderVendor.cs:232-236`).
- The home row has a read arm (`BoundaryPreflight.cs:111`). It is the shared fragment: the check preflight (`:236`), claude's (`:242`), codex's (`CodexPreflightScript.cs:229`), agy's (`AgyCoderPreflight.cs:189`).
- Not asked in one place: the refresh unit (`CoderBoundary.cs:151` calls `Vendor.Program`, not `ProgramOf`). See S54.

**M11 (a).**
- `CoderTurnCheck.StopOfACutTurnAsync` (`CoderTurnCheck.cs:34-46`) runs `StopFirst` with a time of its own (`CutCheckBudget`, 10 seconds, `:28`) and keeps only a boundary.
- Turn 0 and the repair turn: `RoleFailover.cs:401-411`. A boundary goes to `Finished`, then `Failed(coder-boundary)` (`:257-262`).
- A fix turn: `CoderFixLoop.cs:164-166`. The throw for a cancel is skipped for a boundary (`:181-184`). The budget's word does not replace a boundary (`:189`).
- The envelope: `CoderWork.cs:225-236`. A boundary of a cut turn is written with a token of its own (30 seconds).
- The stream is kept: the second snapshot of a cancelled launch has a token of its own (`CoderTurns.cs:82-85`, `:113-117`).
- The runner: a `Failed` outcome keeps its own word. The budget's word is only for a `Stopped` outcome (`RoleRunner.cs:126-130`, `:149-153`).
- A cancel by the person with a broken boundary ends `failed`, `coder-boundary`, not `cancelled`. That is the closed side.

**M11 (b).** `AgyTurnCheck.Cut` answers a boundary for the first event of a tool outside the three, before it looks at the state (`AgyTurnCheck.cs:89-93`).

**R-M15.**
- The place: `BoundaryJob.CoderTempRoot` is `<job>/coder-temp` (`BoundaryJob.cs:153`); the two sources are `<job>/coder-temp/<vendor>/tmp` and `vartmp` (`:159`, `:162`).
- I listed every writable bind of every unit (check, claude, codex, agy, refresh). None is `<job>`, `<job>/coder-temp` or a folder above them. A unit sees the content of its two sources at `/tmp` and `/var/tmp` only.
- The question: `CoderTempOnDiskProblem` (`:168-180`) asks four names with `Lstat`. It is in the early part of the admission (`CoderBoundary.cs:197`), before `BoundaryFolders.Prepare` (`:209`) and before the preflight unit.
- Every coder launch goes through this admission. It has one caller (`CoderTurns.cs:63`).

### P7.3 Red-first: do the tests go red when the fix is taken out?

At the unchanged head: `ACodexTurnCheckTests` 36, `ABoundaryFoldersNeverWriteThroughALinkTests` 13, `AVendorsHaveTheirOwnTempFoldersTests` 9, `ACodexCoderFixTurnsTests` 36, `ACodexCoderPreflightTests` 47, `AAgyCoderFixTurnsTests` 47: all green.

| Fix | What was taken out | Tests that went red | Tests that stayed green |
|---|---|---|---|
| M15 | The old writes back in `WriteOwnFile` (`WriteAllText`, `Copy` with overwrite, the mode by path) | **8 of 13.** A link to a file: the 4 places. A link to a name that is not there: the 4 places | The control and the 4 folder cases. Right: the old code also stops at a folder |
| M16, the order and the cut rule (the every-end wiring stays) | `Check` reads the thread before the items and uses `Whole` for every end | **9 pure** (three theories, each for exit 1, deadline, cancel). **3 through the rig:** `D2_ALostSignInOnTurnZero…` (a lost sign-in ended as a boundary), `M16_ABadItemInAStreamThatNamesNoThread…`, `M11a_ACalmTurn…` (a calm cut turn ended as a boundary) | The rig cases with a thread and a bad item: the old order finds them too |
| M16, the read before the delete | The refusal out of `CodexSessions.Clear` | **1 pure:** `M16_AFreshLaunch_ReadsTheRolloutsBeforeItDeletesThem…` | The rig case `M16_ATurnThatCrashedWithAnAgentCall…`: the cut rule finds the call first |
| M16 with the wiring of `424222fc` | The old condition back in `StopFirst` (clean ends only) | **3 through the rig:** web search then exit 1; a second rollout then exit 1; a bad item with no thread. Each job ended `succeeded` on the next model. This is the harm of M16 | The agent-call rig case: `Clear` refuses the next launch |
| The same, and the refusal out of `Clear` | Both | **1:** the agent-call rig case. The job ended `succeeded` | — |
| M17 | `HomeProblem` answers nothing; the read arm out of the home row | **2 of 47:** both M17 tests | The other 45 |
| M11 (a) | `StopOfACutTurnAsync` keeps no boundary | **2 of 3:** turn 0 and the fix turn end `failed`, `timeout` | The calm control. Right: it is green both ways |
| M11 (b) | The rule for another tool out of `Cut` | **4 of 47:** the pure tests `M11b_…` and `M11_OnACutStream…`; the rig cases `exit-while-another-tool-is-active` and `timeout-while-another-tool-is-active` | The other 43 |
| R-M15, the question | `CoderTempOnDiskProblem` asks no name (the place stays) | **4:** the theory `ATempSourceThatIsALink_…`, all cases | The OS test `AUnitThatSwaps…`: the place alone holds |
| R-M15, the place | The sources back in `<job>/coder/<vendor>` (the question stays) | **2:** `TheTempSources_LieInNoFolderThatAUnitBindsWritable…`, `TheTempFolders_AreTheVendors…` | The OS test: the question alone refuses the next launch |
| R-M15, both | Both | **5:** the two above, two theory cases, and the OS test (the file is in the victim folder) | — |

The coder's counts for M16, M17 and M11 (a) agree with mine where they can be compared (M11 (a): 2 of 3).
Note: no test of agy cuts a turn by the JOB's budget. The road is the shared one (`RoleFailover`, `CoderFixLoop`), and the codex rig tests hold it. The agy half is the pure rule for a cancelled process.

### P7.4 The four points: is there a new hole?

**1. M11 (a): can a model make the check run out of time? What is the answer then?**
- The answer for a check that does not finish in 10 seconds is **nothing** (`CoderTurnCheck.cs:42-45`). The job then ends with its own word: `timeout` or `stopped`. It cannot end green. But no boundary is reported.
- In the usual case the check is a parse of at most 8 Mi characters and some file reads. That is far below 10 seconds.
- A model with only the tools of its vendor cannot make it slow. A model with a COMMAND in the agy unit can: one FIFO at the name of the guard's records. This is **M18** (P7.5).
- For codex the files of the check are in `CODEX_HOME/sessions`. A command of the model cannot write there (CX-V2, the one layer of risk C1). So for codex this needs the vendor sandbox to fail first.
- `coder-boundary` outranks `timeout` and `stopped` in `RoleFailover`, `CoderFixLoop` and `CoderWork.EnvelopeAsync`. I found no fourth place (P7.2).

**2. `CodexSessions.Clear` refuses to delete a folder that holds an agent call. Can a model use it?**
- No new power. A turn with an agent call in its rollout already ends the job: `Cut` and `Whole` both read the rollout. The refusal of `Clear` is reached only when a check did not see the call. Then the job ends `coder-boundary` at the next codex launch. That is the right end.
- It does not block a later job: `sessions` is in the job's own folder.
- It does not confuse a later vendor: the unit of another vendor does not see `<job>/coder/codex`.
- False positive: a call is a payload object with a name. Text of a command is inside a JSON string, so it is not read as a call. A model can write a call with such a name; it then ends its own job.
- One gap, should (S51): when the NEXT model is of another vendor, nothing reads the folder.

**3. R-M15: the time between the question and the bind.**
- No unit can reach the four names: they are under the read-only cover, and no unit binds a parent of them writable. So a live unit has nothing to use, at any time.
- Also, no unit of the job is alive then: a unit that cannot be stopped ends the job, and the preflight unit is asked to be gone before the launch.
- A round of checks does not use `coder-temp`. Its `/tmp` and `/var/tmp` are `<job>/tmp` and `<job>/vartmp`, which have the same property by place. No `Lstat` is asked for them; none is needed.

**4. M17: a program in a subfolder of the home, and a link to a program.**
- A link: `CoderProgram.Resolve` takes the real path first (`CoderProgram.cs:29`). A link in a system folder to a file directly in the home is refused. A link in the home to a file in a narrow folder is allowed, and the unit binds the real folder. Right.
- A subfolder: allowed, by design (the measured install is `~/.codex/packages/standalone/releases/<release>/bin`). The home row stays green, because it looks for a regular file directly in the home.
  A WIDE subfolder (`~/bin`, `~/.local/bin`, `~/Downloads`) is then shown read-only, and for codex the model's commands can read it. This is part 3 of M17 = S36. The property list of the unit is written into the attempt's folder, so the folder is in the record; no sentence tells the person.
- A home outside `/home` is not hidden by `ProtectHome`. The new read arm then refuses every job on that machine. Closed side.

### P7.5 New must-change, and the probes

**M18. A FIFO at the name of the guard's records stops the agy turn check. The job then hangs, or ends `timeout`; no boundary is reported.**

- Where: `AgyCoderVendor.CheckTurn` reads the records BEFORE the stream is judged (`AgyCoderVendor.cs:62-63`: the read is an argument of the call). `AgyGuardRecords.Read` opens every entry that is not a folder and not a link (`:130-145`); `RealPath.Lstat` says `File` for a FIFO (`RealPath.cs:93-103`). The open of a FIFO with no writer does not return.
  The file is `<job>/coder/agy/guard-records.jsonl` (`AgyCoderUnit.cs:31`), in the folder that the unit binds writable (`:56`). No property of the unit stops `mkfifo`.
- Scenario: the case of M11. The model got a command tool through (risks A4, A10). The command runs `rm guard-records.jsonl; mkfifo guard-records.jsonl` and then waits.
  The stream holds the `run_command` event, and since leg (b) that event alone is a boundary. But the check never reaches the stream.
  - The job's budget cuts the turn (the usual case, re-check section 2): `StopOfACutTurnAsync` waits 10 seconds and answers nothing. The job ends `failed`, `timeout`. This is the result that leg (a) was to remove.
  - The turn ends in another way (its own deadline, an exit, a clean end): `StopFirst` is called with no time limit (`RoleFailover.cs:550`, `CoderFixLoop.cs:166`). The runner waits in the open for ever. The job stays `running`; the heartbeat goes on; `role_cancel` does not end it.
- What holds: the job cannot end green, and nothing of the turn is committed. What is lost: the word `coder-boundary`. G15 is the one check at the time of a job for a tool that the hook was not called for.
- codex: the same wait is possible with a FIFO named `rollout-…jsonl` in `sessions`, or at `report.txt` (`CodexCoderVendor.cs:269-276`). Both need a write that CX-V2 stops (C1). So for codex it is a should; the fix is the same code.
- Smallest fix, three parts:
  1. Judge what the stream alone proves BEFORE the records are read (the mode signs, a tool outside the three). Give the check a function that reads the records, not the text.
  2. Never open an entry that is not a regular file (a FIFO, a socket, a device): it is "cannot be read".
  3. Give every turn check a time limit, the clean road too, and answer `coder-boundary` when the check does not finish. Never answer nothing.
- Tests: probes P1, P2 and P6 below.

**The probes.** All in the temporary worktree, at the unchanged head, under the shared lock. **P0 and P3 are green (controls). P1, P2, P4, P5, P6 are red.**

| # | Probe | Safe answer | Seen at `0c9538e7` |
|---|---|---|---|
| P0 | agy check, a cut stream with a `run_command` event `ACTIVE`, no records file | `Boundary` | `Boundary` (control) |
| P1 | The same, with a FIFO at the records name | `Boundary` in 3 seconds | **No answer in 3 seconds.** After a writer opened the FIFO: `Boundary` |
| P2 | `StopOfACutTurnAsync` for that turn (cancelled), with the FIFO | `coder-boundary` | **Nothing, after 10.0 seconds** |
| P3 | The same as P2, no FIFO | `coder-boundary` | `coder-boundary` (control) |
| P6 | The product's own road, a real agy unit, `/bin/sh` as the program (it stands for a command of a model): `rm` and `mkfifo` at the records name. Then the vendor's check for a stream with a `run_command` event | The check answers in 3 seconds | The unit printed `fifo-made`. **The check did not answer in 3 seconds** |
| P4 | codex check, a CLEAN end, the stream names no thread, the rollout holds `collaboration.spawn_agent` | `Boundary` | **`Crash`** (S51) |
| P5 | codex check, a CLEAN resumed turn in another thread, an agent call in that thread's rollout | `Boundary` | **`Crash`** (S51) |

### P7.6 The left items

- **S41 may wait.** The model of the probes is read from `config.toml` (`CodexPreflight.cs:61`). `PrepareLaunch` writes that file new and reads it back (`CodexCoderVendor.cs:239`) directly before the one admission (`CoderTurns.cs:56`, `:63`). No unit of the job runs between the two. So the model of the probes is the model of the launch. The coder's reason (the plan carries no argv) is right.
- **S48 may wait.** Two of its three parts are built: a red for the real codex file (`S42_…`, the `.codex/auth.json` case) and the tests of M16's roads. The third part is the test of S41 and waits with it.
- **S36 may wait**, with one line for the person. The hole of M17 is closed by parts 1 and 2. What is left is a wide subfolder of the home, shown read-only (P7.4, point 4). For agy the model has no command. For codex the model's commands can read that folder; the measured install is narrow.
  It should not wait long after the flip: `launchable` is for every machine, not only for the measured one. A sentence in the result ("the unit shows the folder X") is enough.

### P7.7 Should

Numbered from S50.

- **S50. A stream that reached its ceiling passes on a bad end (codex).** `Whole` ends the job for `Truncated` (`CodexTurnCheck.cs:51`); `Cut` does not look at it. The flag is set by the ceiling AND by the drain grace of a killed process (`ProcessLauncher.cs:151-153`), so the coder put it with "what a cut explains".
  But the ceiling is not a cut: a model can fill 8 Mi characters first, then use a bad item, then keep the turn going. The item is not in the kept stream; the rollout check looks for agent calls only. This is the open half of S43.
  With the default budget the job then ends `timeout`, not green. Give the launcher two flags (ceiling, drain), and let the ceiling end the job for every end. The same for agy (R-S2: `AgyTurnCheck` does not read `Truncated` at all).
- **S51. On a clean end, a crash still wins over the rollout** (`CodexTurnCheck.cs:58-66` are before `:68`). P4 and P5. The next codex launch reads the folder (`Clear`), so the job ends there. When the next model is of ANOTHER vendor, `LeaveVendor` (`CoderOsLayer.cs:113-121`) does not read `sessions`, and the job can end green with an agent call that nothing read.
  A model cannot choose the thread line of the stream, so this needs a fault of the CLI. Read the rollout for agent calls before the two crash answers.
- **S52. A cut resumed turn in another thread is a boundary; a clean one is a crash.** `Cut` compares the files with the thread of the stream (`:73`, `:210`), so the file of the handle's own thread is "another thread". Closed side, but the two ends do not agree. Take the handle's thread as known too.
- **S53. The cover marker has a fixed name** (`.coai-preflight-cover`, `CodexPreflightScript.cs:56`). The host deletes a file or a link of that name in the MAIN checkout and makes the row red (`CodexPreflightRows.cs:35-41`, `:59-77`). It cannot tell its own marker from a file of the repository.
  A repository that tracks that name loses the file in the person's checkout at every codex preflight, and every codex job is refused. Use a name with the job id and a random part, as the job marker has.
- **S54. The refresh unit does not ask the home rule** (`CoderBoundary.cs:151`). For agy with the program directly in the home, one refresh unit runs with the home bound back, and with the owner's refresh token (A12, A5), before the admission refuses the launch. No model is in that unit. Use `ProgramOf` there.
- **S55. Host reads of entries in a folder that a unit can write** should all refuse an entry that is not a regular file: the records (M18), the rollouts, `report.txt`, the sign-in copy as the CLI left it. One helper.
- **S56. The read arm of the home row** says "ok" when `find` is not there or the home cannot be listed (`BoundaryPreflight.cs:111`). Add a control, as S39 did for the sandbox rows. Tests: there is no case for a LINK to a program (a link in a system folder to a file in the home; a link in the home to a file in a narrow folder).
- **S57. The list that the flip rests on** (`ACodexCoderBoundaryTests.cs:181`) still holds the addendum only. Pass 6 asked for M15 to M17 in it. Add M15, M16, M17, M11 (a), R-M15 and M18.
- **S58. An exception inside a turn check** ends the job `runner-error` (`RoleRunner.cs:66-76`). Closed side. `AgyTurnCheck` calls `GetString` on a value that may not be a string (`AgyTurnCheck.cs:258`, `:260`, `:294`). Read with the kind first.
- **Open from before, not touched by this range:** R-S2 (see S50), R-S7 and S26 (the refresh unit has no `KeysHidden`, no `SignInHidden`, no row), R-S9 for the FOLDERS that `Prepare` makes (`BoundaryFolders.cs:21-24`; the home is asked elsewhere, M8), R-S10 (no word of the range in `research/` or `v2/`), S2, S29.

### P7.8 Confirmed safe

- **claude.** Its turn check answers `Fine` for every end, so the wider wiring changes nothing. Its temp sources moved with the others. The read arm of the home row is new for claude and for the check unit; no bind of today puts a regular file directly into the home (the caches are folders).
- **The item rule of codex on a bad end.** A lost sign-in (an `error` item, exit 1) passes the items and the cut rule, and the launch's own stop skips the vendor (`D2_…` is red when the cut rule is out).
- **`HoldsAnAgentCall`** looks at `sessions` without following it, reads leniently, and deletes nothing.
- **S39, S40, S42 to S47, S49** are built as the commit says (by reading, and their tests are green; S43 has the note S50 and S40 has the note S53).
- **`launchable`** is off for codex and for agy (`shared/coder-boundary-releases.json:23`, `:84`), and tests hold both off (`ACodexCoderBoundaryTests.cs:176`, `ACoderVendorSeamTests.cs:255`).

### P7.9 Flip verdict

**codex — owed before `launchable` turns on:**
1. **One run of the product's own preflight on the real codex, in the real unit, with no model. 0 paid calls.** Every row green. It must show the five points of pass 6 (P6.5, point 3) and what this range added:
   the four new tokens inside the REAL sandbox (`sb-saw-wt-file`, `sb-list-wt`, `sb-proc-self`, `sb-tools`): if the real sandbox has no `/proc`, or hides `.git`, three rows are red for every job;
   the read arm of the home row on the real home;
   no file `.coai-preflight-cover` in the main checkout after the run.
2. **S57**: the must-changes of passes 6 and 7 in the list that the flip rests on. Small.
3. The flip commit itself: the mark at `shared/coder-boundary-releases.json:23` and the two tests that hold it off.

Not owed for codex: M18 (for codex it needs CX-V2 to fail; the fix is the same code, so take it with agy's).
**Recommended, 2 paid calls (7 of 30 left):** one turn 0 and one fix turn through the product, at effort `medium`. They show the item types (a `reasoning` item ends every job by rule 4, on every end now), the rollout names after a resume, and the report file. If a `reasoning` item is there, the owner needs an addendum before the flip; else the flip gives refused jobs only. These calls are not needed for safety.

**agy — owed before `launchable` turns on:**
1. **M18** built and green.
2. **One real `agy models` on a stale copy that `MakeStale` made. 0 paid calls** (from the re-check, M14). A refusal by agy is on the closed side.
3. The flip commit itself: the mark at `shared/coder-boundary-releases.json:84` and the test that holds it off.

**Recommended for agy:**
- One run of agy's own preflight on the real machine with no model (0 paid calls), if none was run after `c7f2e259`: the three rows of M12, the read arm of the home row, the new temp folders.
- One turn 0 and one fix turn through the product (paid; the number is the owner's). By the judgement 14.9, coai's own guard as a CODER guard has not met the real agy. With G15 literal, one `manage_task` call of the model ends the job (the owner's decision, not a defect). Without these turns the flip can give refused jobs only. Not needed for safety.

**Waiting on the owner, not on code:** G15 literal or relaxed; the 45-minute cap; the addendum for `reasoning` if the paid codex turns show it.

---

## Pass 8 — agy M18 and the real-run fix, head aa177f5d

Verifier: Fable (security verify role), 2026-11-27. Read-only for the branch: no edit, no commit, no push.
Branch `feat/v2-ep3-e3-agy`, head `aa177f5d` (fetched; equal to the remote head). Range `0c9538e7..aa177f5d`: `f9b72f8a` (M18), `aa177f5d` (the expiry row, the gated real-run test, the results file).
Every path and line below is at `aa177f5d`.

Threat model, as before: a model under injection inside its unit, and the unit of a later vendor of the same job.

**What was done.**
- Read: pass 7 here; the product diff of the range (4 product files) and the new tests; `RESULTS_e3_agy_real_runs.md`; the roads after a launch in `RoleModelLauncher`, `CoderOsLayer`, `CoderWork`, `CoderFixLoop`, `RoleFailover`, `RoleRunner`, `SignInCopy`, `SignInRefresh`, `CodexCoderVendor`, `CodexSessions`.
- Built the head in a temporary worktree (`/home/jinx/coai-t3/wt-pass8`, detached at `aa177f5d`) under the shared lock. Ran four test classes there: **120 of 120 green** at the unchanged head (`AAgyTurnCheckDoesNotWaitTests` 12, `AAgyCoderLaunchTests` 25, `AAgyCoderFixTurnsTests` 47, `ACodexTurnCheckTests` 36).
- Took each part of the fix out of the PRODUCT code of that worktree only (the tests stayed), built again (0 errors each time), and ran the tests again. Six more builds (P8.3).
- Added 25 probe cases of my own to that worktree only (P8.4 to P8.6). Each probe states the SAFE behaviour, so a red probe shows a gap. One probe goes through the product's own road with a real unit.
- Ran the two lines of the expiry row in plain `sh` with 15 made-up values (P8.7).
- The temporary worktree and its logs are removed. No unit is left. The branch worktree is clean at `aa177f5d`.
- Not run: codex, agy, a model call. No key file and no environment was read. The real HOME was not touched. No Python. The two real runs of the results file were read, not repeated.

**Owner decisions that this pass follows:** no version stops anything; the risks agy A1 to A13 and codex C1 to C7, C9 are accepted; G15 stays literal and the 45-minute cap stays until the owner answers.

### P8.1 Verdict

| Item | Verdict | In one line |
|---|---|---|
| M18 | **CLOSED** | The stream is judged first. The records are opened with no wait and only a regular file is read. Every vendor's check has a limit of 5 seconds, and a check that does not finish answers `coder-boundary` on the clean road and on the cut road |
| The same wait in another file | **NOT CLOSED: M19** | The host reads the agy sign-in copy, as the unit left it, with a plain open. It does this after EVERY launch, before the turn check. A FIFO there stops the job for ever. The 5-second limit is not on that road |
| The 5-second limit | Safe. Tight, but right for the flip | Measured: 4 ms to 330 ms for the usual sizes. Do not scale it with the size. Raise it to 30 seconds and bound the work (S59 to S61) |
| The expiry row (`CopyRow`) | **Safe** | No command can be injected. `date` alone accepts words such as `next year`, but the host gate reads the same field strictly and refuses first |
| The real-run test | **Safe** for the real sign-in file | Read only. The copies are deleted in a `finally`. One should: it deletes the whole folder that the person named (S63) |

**New must-change: 1 (M19).** It is for agy. It does not let a job end green.

### P8.2 M18: the evidence

The three parts that pass 7 asked for are all there.

1. **The stream first.** `AgyTurnCheck.Check` judges the mode signs (`AgyTurnCheck.cs:51-54`) and a tool outside the three (`:56-60`) before it calls the read of the records (`:62`). The vendor gives the read as a function (`AgyCoderVendor.cs:65-71`).
2. **No wait on the open, and only a regular file.** `AgyGuardRecords.Read` looks without following (`AgyCoderVendor.cs:151`). A missing entry is "no records". A folder or a link is "cannot be read". For the rest, `ReadRegular` opens with `O_RDONLY | O_NONBLOCK | O_CLOEXEC` (`:164`, `:173`) and reads only when the stream can seek (`:182`). A FIFO opens at once and cannot seek, so it is "cannot be read".
3. **Records that cannot be read are a boundary of their own**, for every end, also with no tool event at all (`AgyCoderVendor.cs:74-76`).
4. **A limit for every vendor.** `CoderTurnCheck.Stop` runs the vendor's check in a task and waits 5 seconds (`CoderTurnCheck.cs:31`, `:73-78`). No answer in that time is `coder-boundary` (`:55-56`). The cut road no longer answers nothing (`:49-52`).
   All three callers use it with the default limit: `RoleFailover.cs:404` and `:550`, `CoderFixLoop.cs:165-166`.

**Can the open be raced?** No. The open does not use `O_NOFOLLOW`, but no process of the unit is alive at that time: a unit that is not gone gives `UnitNotStopped` (`CoderTurns.cs:131-134`), and then the check is not asked (`CoderTurnCheck.cs:23`).

**The pattern `LibraryImport("libc")`** is already in the product (`LinuxHeldFile.cs:107`, `LinuxStatx.cs:107`), so the new import is not a new risk for the native build.

### P8.3 Red-first: do the tests go red when the fix is taken out?

Class `AAgyTurnCheckDoesNotWaitTests`, 12 cases. At the unchanged head: 12 green.

| What was taken out | Red | Which |
|---|---|---|
| Part 1 only: the records are read before the stream is judged | **0 of 12** | None. Part 2 hides it: the read cannot wait any more. See S62 |
| Part 2 only: a plain `File.ReadAllText` | **4 of 12** | `AnEntryThatIsNotARegularFile_…` (3 ends), `AFifoAtTheRecordsName_IsABoundary_EvenForATurnWithNoToolEventAtAll`. Each: no answer in 3 seconds |
| Part 3 only: records that cannot be read are no boundary of their own | **1 of 12** | `AFifoAtTheRecordsName_IsABoundary_EvenForATurnWithNoToolEventAtAll` |
| Part 4 only: no limit in `Stop`, and the cut road answers nothing | **2 of 12** | `P2_…OnTheCleanRoad…` (came back after 30 s with nothing), `P2_…OnTheCutRoad_Too` |
| All four (the code of pass 7) | **11 of 12** | All but the folder-and-link case, which the old code also held. Both `P6_…ThroughTheRig…` cases: the job did not end in 2 minutes |
| The decode in the expiry row (`aa177f5d`) | **1 of 25** in `AAgyCoderLaunchTests` | `TheSignInInTheShapeOfTheRealFile_…`. The same count as the coder's |

The probes P1, P2 and P6 of pass 7 are in the class and are green at the head.

### P8.4 Other host reads of an entry in a folder that a unit can write

I listed every read that the host makes after a unit ran. "Limit" means: the read is inside the vendor's check, so a wait ends in `coder-boundary` after 5 seconds.

| Entry | Who can write it | Where the host reads it | Probe at `aa177f5d` | Result |
|---|---|---|---|---|
| agy: the guard's records | A command in the agy unit | The turn check | P1, P6 of the coder | **Closed** (M18) |
| agy: the sign-in copy, as the unit left it | A command in the agy unit (`AgyCoderUnit.cs:56`, `:34`) | `SignInCopy.TokensOf` (`SignInCopy.cs:163-178`), from four places. None is inside the check | P8_1 to P8_4, P8_R1: **no answer** | **M19** |
| agy: the guard folder | Nobody (read-only in the unit, `AgyCoderUnit.cs:57`) | Before a launch | — | Safe |
| The coder's home and temp folders, the key search | A command in the unit | `KeyScanner.Read` (`KeyScanner.cs:85-89`) | — | Safe: an entry with no length is not opened, a link is not followed, the read is in pieces |
| codex: a rollout file, in the turn check | A command only if CX-V2 fails (risk C1) | `CodexTurnCheck.AgentCallIn` (`CodexTurnCheck.cs:261`) | P8_6, clean end and exit 1: **`coder-boundary` in 2 s** (limit set to 2 s in the probe) | Covered by the limit. Stays S55 |
| codex: a rollout file, before a fresh launch | The same | `CodexSessions.Clear` → `HoldsAnAgentCall` (`CodexSessions.cs:26`, `CodexCoderVendor.cs:227`) | P8_7: **no answer in 3 s** | NOT covered by the limit. S55 |
| codex: `report.txt` | The same | `CollectLaunch` (`CodexCoderVendor.cs:269-276`, called at `CoderTurns.cs:79`) | P8_8: **no answer in 3 s** | NOT covered by the limit. S55 |

**For codex, your rule ("a should only if the wait ends in `coder-boundary`") holds for the rollout read in the check, and does not hold for `Clear` and for `report.txt`.** I keep both as should for one reason only: a command of a codex model cannot write those files while the vendor sandbox holds (CX-V2), and that layer is the accepted risk C1. If you do not accept that reason, they are must-changes for the codex flip. The fix is the helper of M19, so take them with it.

Not checked again in this pass: the worktree (git reads it; passes of E2).

### P8.5 New must-change

**M19. A FIFO at the name of the agy sign-in copy stops the job for ever. No boundary is reported. The 5-second limit is not on this road.**

- Where: `SignInCopy.TokensOf` reads the copy with `File.ReadAllBytes` (`SignInCopy.cs:167`). It does not look at the kind of the entry first. `SignInJobState.Remember` and `Tokens` call it (`:360`, `:372-377`).
  The file is `<job>/coder/agy/home/.gemini/antigravity-cli/antigravity-oauth-token` (`AgyCoderUnit.cs:34`), inside the folder that the unit binds writable (`:56`).
- The four roads:
  1. After EVERY coder launch, to scrub the tokens from the output: `RoleModelLauncher.cs:162` → `CoderOsLayer.SignInTokens` (`CoderOsLayer.cs:48-49`). This is BEFORE the turn check.
  2. Before every commit: `CoderFixLoop.cs:243`.
  3. When the job leaves the vendor: `CoderOsLayer.cs:117`.
  4. At the end of every job, in a `finally`: `CoderWork.cs:137` → `RemoveSignInCopies` (`CoderOsLayer.cs:62`).
- Scenario: the case of M18. The model got a command tool through (risks A4, A10). The command runs `rm` and `mkfifo` at the name of the copy.
  The launch ends. Road 1 opens the FIFO and waits. The turn check is never asked, so the `run_command` event of the stream is never read.
  The runner awaits the work with no limit (`RoleRunner.cs:97`), and its heartbeat goes on beside the work (`:288-298`). The job stays `running`. `role_cancel` and the job's budget cancel a token that the blocked read does not see.
- What holds: the job cannot end green, and nothing of the turn is committed. What is lost: the end of the job and the word `coder-boundary`. This is the harm of M18, in the next file.
- Probes, all red at `aa177f5d`:
  - P8_1 `SignInJobState.Tokens`, P8_2 `CoderOsLayer.SignInTokens`, P8_3 `RemoveSignInCopies`, P8_4 `LeaveVendor`: each gave no answer in 3 seconds. P8_0 (a regular copy) is the green control.
  - **P8_R1, the product's own road with a real unit:** the coder's rig case P6, with one change: the fake agy puts the FIFO at the sign-in copy and not at the records. Attempt time 4 seconds. **The job did not end in 60 seconds.** The same case with the FIFO at the records ends `coder-boundary` in seconds (P6, green).
  - P8_5: a LINK at the name of the copy is read through. The tokens of the target file came back. The texts go only to the scrub and to the commit search, in memory, so nothing leaks. But a link to a file with no end makes the read fill memory (not probed).
- codex: the copy is in `CODEX_HOME`, which a command of the model cannot write (CX-V2, risk C1). The same code, a should.
- Smallest fix:
  1. ONE helper for a host read of an entry that a unit can write: look without following; open with `O_NONBLOCK` and `O_NOFOLLOW`; read only a regular file (ask the open descriptor); read at most a set number of bytes. `AgyGuardRecords.ReadRegular` is most of it.
  2. Use it in `SignInCopy.TokensOf`, in the records read, in `CodexTurnCheck.AgentCallIn` and in `CodexCoderVendor.CollectLaunch`. This is S55.
  3. A sign-in copy that is not a regular file after a turn ends the job `coder-boundary`, as the records do now. At the least it must never wait.
- Tests: P8_1 to P8_5 as pure tests, and P8_R1 as a rig case for an exit and for a turn that holds (the two forms of P6).

### P8.6 The shared 5-second limit

**Can a turn that did nothing wrong get a false `coder-boundary`?** Yes, but only far from the usual sizes. Measured in the temporary worktree, load average about 3 to 4:

| Check | Input | Time |
|---|---|---|
| agy | 300 tool calls, stream 0.1 Mi characters | 4 ms |
| agy | 300 tool calls, stream 8.0 Mi characters (the ceiling) | 61 ms |
| agy | 3,000 tool calls, stream 1.0 to 7.9 Mi characters | 167 to 213 ms |
| agy | **20,000 tool calls**, stream 6.7 Mi characters | **6,277 ms** |
| codex | stream 8.0 Mi characters, rollout 16 MB | 53 ms |
| codex | stream 8.0 Mi characters, rollout 64 MB | 116 ms |
| codex | stream 8.0 Mi characters, rollout 256 MB | 328 ms |
| agy | a records file of 256 MB with nothing in it | 1,284 ms, and **1.8 GB of memory allocated** |

- The stream cannot make the check slow: the launcher cuts it at 8 Mi characters, and that parses in about 60 ms.
- A codex rollout of 256 MB takes a third of a second. A false answer needs a host that is about 15 times slower than this one was.
- The one input that passes 5 seconds is an agy turn with about 18,000 tool calls. The tie of events and records is quadratic (`AgyTurnCheck.cs:200-237`). A real turn of 45 minutes does not make that many calls. A model that reads files in a loop could.
- The limit also covers claude, whose check answers at once. For claude a false answer needs the process to stand still for 5 seconds (memory pressure, no free pool thread).

**Is 5 seconds the right number? Should it scale with the size?**
- **Do not scale it.** The limit is a stop for a wait that never ends, not a budget for work. A limit that grows with a file lets the unit choose the limit.
- **5 seconds is safe, and it can stay for the flip.** A wrong answer is on the closed side.
- **It is tighter than it needs to be.** A false answer costs a whole paid job and tells the person that the boundary broke. A longer limit costs nothing but waiting time in the case of an attack. So: one fixed limit of 30 seconds (S61), and bound the work so that an honest check stays far below it (S59, S60).
- After the limit, the check's task goes on in the background. For agy it cannot wait now. For codex one thread stays in the open. The runner is one process for one job (`--role-run`), so the thread ends with the job.

### P8.7 The expiry row (`CopyRow`)

The row (`AgyCoderPreflight.cs:127-137`) reads the field with `sed`, changes `+` or `+` to `+` (`:131`), and gives the text to `date -d` (`:132`).

- **No command injection.** The text is used only inside double quotes, as the argument of `-d` and in two tests. Nothing evaluates it a second time. Probe in `sh`: `$(touch X)`, a backtick form, `x; touch X; #`, `-f /etc/passwd`, `--file=…`, `…Z&touch X`: no file was made, and `date` refused each one, so the row is red.
- **The decode is narrow and right.** In a time of this shape, the plus is the only character that the JSON writer of the copy escapes. An escaped backslash before `u002B` gives `\+`, which `date` refuses.
- **The field is not the model's.** The host makes the copy new before every turn (`FreshForEveryTurn`, `SignInCopy.cs:237-248`: delete, then `CreateNew`), from the person's real file or from the scratch copy of the refresh unit. No unit of the job is alive between that and the preflight. The copy holds one `expiry` key (`AgyCoderVendor.cs:34`).
- **Can a bad time pass the row?** The row alone: yes. `date -d` accepts `next year`, `@9999999999`, `now + 10 years` (probe). But the host gate reads the same field as JSON, with a strict parse, BEFORE the preflight (`CoderTurns.cs:49` → `CoderBoundary.cs:132`; the admission is at `CoderTurns.cs:63`). Probe P8_9: the host gate refuses all four words. Both must pass, so a launch does not pass with such a field.
- One small difference (S64): a time with NO offset is read as UTC by the host and as local time by `date`. agy writes an offset, so this is not seen today.

### P8.8 The real-run test (`TheRealAgyRunsTests.cs`)

- **It runs only when asked**: `COAI_REAL_AGY=1`, a results file and a jobs folder, each named by the person (`:47-58`). In a normal run it is a skip.
- **The real sign-in file is opened for reading only.** `HashOf` (`:28`), `ExpiryOfFile` (`:76`), the byte copy of `SignInRefresh` through a read handle (`SignInRefresh.cs:76`), the snapshot and the copy of `PrepareSignInAsync` (`:102`). The one write with `Truncate` (`SignInRefresh.cs:136`) is on the scratch copy, which was made with `CreateNew` directly before it.
- **No unit of the test can write it.** The refresh unit and the preflight unit run with `ProtectHome=tmpfs` (the argv in the results file), and the scratch home is the one writable folder of the refresh unit.
- **It checks itself**: the sha256 before, after (a), after (b), and as an assert at the end (`:142`). Only "equal" or "changed" is written.
- **Nothing is left** in the usual case and after a throw: the refresh folder is deleted by the product, and the whole jobs folder is deleted in a `finally` (`:127-137`). I looked: `/home/jinx/coai-t3/runs/real-agy-jobs` is not there now.
- **The results file holds no secret**: long token-like texts are replaced, each argv is cut, and the units get their values through `env -i` with names of paths only.
- Two notes, both S63:
  - The `finally` deletes the folder that `COAI_REAL_AGY_JOBS` names, WHOLE. A person who names a folder with other files loses them. Make a folder of the test's own inside it, and delete only that.
  - A run that is killed (not a throw) while the refresh unit runs leaves the FULL copy, with the refresh token, in `<jobs>/<job>/refresh` (mode 0600). The next run with the same folder deletes it. Else a person must.
- One fact for the flip, not a defect: in run (b) the hook handler was the test's stand-in program, which holds the product's hook code (`AgyCoderBed.cs:60`, `tests_fakecli/Program.cs:153`). The shipped `coai-mcp` as the handler inside the real unit was not seen. If it cannot run there, the row `guard-answers-each-case` refuses every launch: closed side.

### P8.9 Should

Numbered from S59.

- **S59. The records are read whole, with no size limit** (`AgyCoderVendor.cs:182`). A records file of 256 MB made the check allocate 1.8 GB. A command in the unit can make a file of any size with one `truncate`. On a machine with little memory the runner can be killed. Ask the size of the open file, and call a file over a few MiB "cannot be read".
- **S60. The tie of events and records is quadratic** (`AgyTurnCheck.cs:200-237`): 20,000 tool calls took 6.3 seconds, which is over the limit, so an honest turn of that size ends `coder-boundary`. Index the records by conversation and step.
- **S61. Raise the limit of the check from 5 to 30 seconds** (`CoderTurnCheck.cs:31`). See P8.6. Do not scale it.
- **S62. Part 1 of M18 has no red of its own**: with the old order, 12 of 12 stay green. One pure test: the read function is not called when the stream alone proves a boundary.
- **S63. The real-run test** deletes the whole folder the person named, and a killed run leaves the full copy (P8.8).
- **S64. The expiry row accepts words that are not a time**, and reads a time with no offset as local time (P8.7). The host gate holds. Let the row ask for the shape of a time first.
- **S55, wider now.** The two codex reads that the limit does not cover (`Clear`, `report.txt`) and the read through a link at the sign-in copy. One helper (M19, fix part 1).
- **S58, still open.** An exception inside a check now arrives as an `AggregateException`. It ends the job `runner-error` as before (`RoleRunner.cs:66-76`).
- **Open from before, not touched by this range:** S50 to S54, S56, S57, R-S2, R-S7, S26, R-S9, R-S10, S2, S29, S36, S41, S48.

### P8.10 Confirmed safe

- **claude.** Its check answers `Fine` at once, inside the same limit. Its sign-in is a bind of the real file, not a copy, so M19 does not reach it.
- **The cut road.** The check of a cut turn waits 10 seconds more than the limit as a backstop (`CoderTurnCheck.cs:45`), and answers `coder-boundary` when even that runs out.
- **`coder-boundary` from the limit** goes the same way as any other boundary: it outranks `timeout` and `stopped` in `RoleFailover`, `CoderFixLoop` and the envelope (pass 7, P7.2).
- **The records put away before a launch** (`AgyGuardRecords.PutAway`) rename or delete by name and open nothing.
- **`launchable`** is off for codex and for agy (`shared/coder-boundary-releases.json:23`, `:84`), and tests hold agy off (`ACoderVendorSeamTests.cs:255`, `AAgyCoderLaunchTests.cs:84`).

### P8.11 Flip verdict for agy

**Not yet. One item of code is owed.**

Owed before `launchable` turns on:
1. **M19** built and green, with the tests of P8.5.
2. **The flip commit itself**: the mark at `shared/coder-boundary-releases.json:84`, and the two tests that hold it off (`ACoderVendorSeamTests.cs:255`, `AAgyCoderLaunchTests.cs:84`).

Done, and not owed again:
- M18 (this pass).
- One real `agy models` on a stale copy that `MakeStale` made: it refreshed, the real file stayed equal (results file, run (a)).
- One real preflight: 23 of 23 rows, with the three rows of M12 and the read arm of the home row (run (b)).
- The fix of M19 is host code that runs after a unit ended. It changes no unit property and no row. So the two real runs need no repeat, unless the fix touches the preflight script or the unit.

**Recommended, not owed: paid agy turns through the product.** What they prove that nothing else proves:
1. The real agy reads the product's own guard folder and calls the product's hook for every tool. Until now the real agy met the files of the measure, and the product's files met the fake agy.
2. The tie of G15 on a real stream: the conversation id, the step numbers and the tool names of the real stream are equal to those in the records that the product's hook wrote. Until now one program made both sides.
3. The real agy starts on the product's sign-in copy (no refresh token, the expiry spelled with `+`). The real preflight did not start agy on it. The first real run found one bug of this kind in 1 of 23 rows, and the turn road has had no real run at all.
4. A fix turn: `--conversation` continues the same conversation, and the session rule reads the real id.
5. How often the real model asks for a tool outside the three (for example `manage_task`). With G15 literal each such call ends the job. This is the number that the owner needs for the G15 decision.

Every failure of 1 to 4 ends in `coder-boundary` or `crash`, so the turns are **not needed for safety**. Without them the flip can give refused jobs only.

**How many:** **2 turns are enough**: one job with a turn 0 that reads one file and writes one file, and one fix turn (make the first check red on purpose). If one of them ends `coder-boundary` because of a tool outside the three, that is the result; do not repeat it to get a green. For a first number for point 5, use 3 jobs (about 6 turns).
The turns need a build with the mark on. So the order is: M19, the flip commit on the branch, the paid turns, then the merge. A gated test with a table of its own gives the same without the commit.

**Waiting on the owner, not on code:** G15 literal or relaxed; the 45-minute cap; the number of paid agy turns.

---

## Pass 9 — M19 and the unit-written read helper, commit 7d118804

Verifier: Fable (security verify role), 2026-11-27. Read-only for the branch: no edit, no commit, no push.
Branch `feat/v2-ep3-e3-agy`, commit `7d118804` (fetched). Range `aa177f5d..7d118804`: `e0b07cd2` (the other branch), the merge `0cef12ec`, and `7d118804` (the fix).
Every path and line below is at `7d118804`. Later commits and the uncommitted files of the agy coder in the branch worktree were not read.

Threat model, as before: a model under injection inside its unit, and the unit of a later vendor of the same job.

**What was done.**
- Read: pass 8 here; the diff of the range (9 product files, 2 shared JSON files, the tests); the helper and `RegularFiles.cs` below it; every host read on the coder road (P9.4).
- Built the commit in a temporary worktree (`/home/jinx/coai-t3/wt-pass9`, detached at `7d118804`) under the shared lock. Ran six test classes there: **135 of 135 green** at the unchanged commit (`AUnitWrittenEntriesTests` 18, `AAgyTurnCheckDoesNotWaitTests` 12, `ACodexCoderBoundaryTests` 7, `ACodexTurnCheckTests` 36, `ACodexCoderLaunchTests` 26, `ACodexCoderFixTurnsTests` 36).
- Took nine parts of the fix out of the PRODUCT code of that worktree only (the tests stayed), in three builds (0 errors each), and ran the class of the fix again (P9.3).
- Added 16 probe cases of my own to that worktree only (class `P9ProbeTests`, and one small steering of the fake CLI). Each probe states the SAFE behaviour, so a red probe shows a gap. Eight of them go through the product's own road with a real unit. Result: 8 probes red, 8 green (P9.4 to P9.6).
- Made a scratch git repository (plain git) to see what git does with a FIFO in a work tree.
- The temporary worktree, its logs and the scratch files are removed. No unit is left.
- Not run: codex, agy, a model call. No key file and no environment was read. The real HOME was not touched. No Python.

**Owner decisions that this pass follows:** no version stops anything; the risks agy A1 to A13 and codex C1 to C7, C9 are accepted; G15 stays literal and the 45-minute cap stays until the owner answers.

### P9.1 Verdict

| Item | Verdict | In one line |
|---|---|---|
| M19 | **CLOSED** | The sign-in copy is read through the helper on all four roads. A FIFO gives no tokens and no wait. After the vendor's own check, an entry that is not a plain file ends the job `coder-boundary` |
| S55 (codex reads) | **Closed** | The rollouts, `CodexSessions.Clear` and `report.txt` go through the helper. P9_9: a FIFO at the codex sign-in copy ends `coder-boundary` |
| S59, S61, S62 | **Closed** | Records: at most 16 MiB. The check limit is a fixed 30 seconds. The order test goes red with the old order |
| The helper | Right for the LAST name of the path | No race: the type is asked of the open descriptor, and the same descriptor is read. A link in a PARENT folder is followed (S65, no harm found). Two limits are weaker than they read (S67, S68) |
| Reads still outside the helper | **1 must-change: M20.** 2 should (S66, S70) | The host reads `.coai/toolchain.yaml` of the WORKTREE with a plain open in every check round. A FIFO there stops the job for ever. Older than this commit. codex can reach it today with its own tools; claude and agy only when a command got through |
| codex | **No new real codex run is needed** | The fix changes no unit, no argv, no preflight row and no file that the CLI reads. An honest turn goes the same road |
| The merge `0cef12ec` | Safe | No conflict. It brings tests, two JSON files and a results file. It meets the fix in one way: codex is launchable, so the codex half of the fix is on a live road |

**New must-change: 1 (M20).** It is not a fault of this commit, and it is not agy-only. The wait itself makes no job green.

### P9.2 M19: the evidence

1. **The read.** `SignInCopy.TokensOf` reads the copy with `UnitWrittenFile.Read`, at most 256 KiB (`SignInCopy.cs:168`). What cannot be read gives no tokens.
2. **The four roads** all go through `SignInJobState.Remember` → `TokensOf` (`SignInCopy.cs:361`, `:373-378`): after every launch (`RoleModelLauncher.cs:162` → `CoderOsLayer.cs:48-49`), before every commit (`CoderFixLoop.cs:243`), when the job leaves the vendor (`CoderOsLayer.cs:117`), in the job's `finally` (`CoderWork.cs:137` → `CoderOsLayer.cs:62`).
3. **The open.** `open` with `O_NONBLOCK | O_CLOEXEC | O_NOCTTY | O_NOFOLLOW` (`RegularFiles.UnitWritten.cs:59`; the flags at `RegularFiles.cs:241`, `:260`). The type is asked of the OPEN descriptor with `statx` (`:66`; `RegularFiles.cs:174-185`). The read is of the same descriptor and counts the bytes that were read, not the length the file says (`:73`; `RegularFiles.cs:202-221`).
4. **The boundary.** `CoderTurnCheck.Stop` runs the vendor's check and then `EntriesChecked` (`CoderTurnCheck.cs:82`, `:58-61`). agy names its sign-in copy (`AgyCoderVendor.cs:63`). codex names its `auth.json` copy and `report.txt` (`CodexCoderVendor.cs:291`). A FIFO, a link at the last name, a folder, a file with no read permission or a file over the limit gives a sentence (`UnitWrittenFile.cs:32-51`), and the sentence is `coder-boundary`. A verdict that is already a boundary stays, so the stream is still judged first.
5. **No road goes round the check and then launches the same vendor again.** The check is not asked only when the launch already has a stop of the layer, a refusal or a key stop (`CoderTurnCheck.cs:23`). A stop of the layer ends the job `coder-boundary` (`RoleFailover.cs:590-593`). A refusal means that no unit ran. A key stop is for claude only, and claude names no entry.
6. **The limit.** `CheckBudget` is 30 seconds, fixed (`CoderTurnCheck.cs:31`). The backstop of a cut turn is 10 seconds more (`:45`).

### P9.3 Red-first: do the tests go red when the fix is taken out?

Class `AUnitWrittenEntriesTests`, 18 cases. At the unchanged commit: 18 green. The parts in one build touch different tests.

| What was taken out | Red | Which |
|---|---|---|
| A. `TokensOf` reads with a plain open (the code of pass 8) | **3 of 18** | `TheSignInCopy_ReadAsAFifo_…` (no answer in 3 s); `P8_R1_…` exit and hold (the job did not end in 2 minutes). My P9_9 (codex) also: the job did not end in 2 minutes |
| B. The entries are not checked in `Stop` | **3 of 18** | `P8_R1_…` exit and hold; the `report` case. My P9_9: the codex job ended GREEN with a FIFO at its sign-in copy |
| D. `report.txt` is copied with the old code | **1 of 18** | The `report` case (the job did not end in 2 minutes) |
| E. A rollout is opened with a plain open | **1 of 18** | `AFreshCodexLaunch_WithAFifoInTheSessions_…` (no answer in 3 s). `rollout-clean` and `rollout-cut` stay GREEN: the 30-second limit ends them `coder-boundary`. See S69 |
| G. `CodexSessions.Clear` does not read the problem | **2 of 18** | `AFreshCodexLaunch_WithAFifoInTheSessions_…`, `AFreshCodexLaunch_WithALinkInTheSessions_…` |
| H1. The open has no `O_NOFOLLOW` | **1 of 18** | `TheOneRoad_…(link)` |
| H2. The open has no `O_NONBLOCK` | **3 of 18** | `TheSignInCopy_ReadAsAFifo_…`, `TheOneRoad_…(fifo)`, `AFreshCodexLaunch_WithAFifoInTheSessions_…` (each: no answer in time). The five rig cases stay green: the 20-second and 30-second bounds end them with the same word |
| I. The look does not ask the size | **1 of 18** | `TheOneRoad_…(too-big)` |
| J. The records are read before the stream is judged (S62) | **1 of 18** | `TheRecordsAreNotRead_WhenTheStreamAloneProvesABoundary_…` |

Builds: A + J + G + D gave 7 red. B + E + H1 gave 5 red. H2 + I gave 4 red.
The test of S61 (`TheCheckBudget_IsOneFixedThirtySeconds`) compares the number with 30 seconds; I did not build that change.

### P9.4 Every host read of an entry in a folder that a unit can write

Who can write: the coder's unit of a vendor binds the worktree and that vendor's own folder of the job (`CoderUnit.cs:57`, `:62-64`; `CodexCoderVendor.cs:137`, `:141`; `AgyCoderUnit.cs:52`, `:56`). A check unit binds the worktree and the check folders (`BoundaryUnit.cs:41`, `:46-47`, `:99`). The refresh unit binds its scratch home (`SignInRefresh.cs:42`). No unit sees the folder of another vendor or the data folder.

| Entry | Where the host reads it | Through the helper | Result |
|---|---|---|---|
| agy and codex: the sign-in copy, the tokens | `SignInCopy.cs:168` | Yes | **Closed** (M19) |
| agy: the guard's records | `AgyCoderVendor.cs:151` | Yes, at most 16 MiB | Closed |
| codex: `report.txt` | `CodexCoderVendor.cs:275` | Yes, at most 1 MiB | Closed |
| codex: the rollouts, in the turn check and before a fresh launch | `CodexTurnCheck.cs:246` (walk), `:259-277` (read), `:325-337` | Yes: the open and the walk. The read is by line (S68) | Closed |
| **Every vendor: `.coai/toolchain.yaml` of the worktree** | `ChecksAtBase.cs:47` → `DiskWorkFolder.cs:28-40`, from `CoderChecks.cs:82`, in every check round | **No. A plain open** | **M20** |
| codex: the sign-in copy, the expiry | `SignInCopy.cs:130`, from `CoderBoundary.cs:132`, before every launch | No. A plain open | Safe by the order of today. S66 |
| claude with a pool key: the coder's home and temp folders | `RoleModelLauncher.cs:169`, `:179` → `KeyScanner.cs:60-63`, `:85-95` | No | A FIFO is not opened (length 0). No limit of size, time or entries. S70 |
| Every vendor: the files that git lists in the worktree, for the key and token search | `CoderCommits.cs:140-146`, `:206-210` → `KeyScanner` | No | The same. S70 |
| The refresh unit's scratch copy | `SignInCopy.cs:74`, from `CoderBoundary.cs:173-176` | No. A plain open | Safe in the threat model: that unit runs the vendor's CLI with `models`, no model and no file of the job. No coder unit sees the folder |
| codex: `config.toml`, the read-back and the model line | `CodexCoderVendor.cs:339`, `:303` (from `CodexPreflight.cs:61`) | No | Safe: the host made the file new (`CreateNew`) just before, and no unit ran between |
| agy: the guard folder, the read-back | `AntigravityCoderGuard.cs:170` | No | Safe: read-only in the unit, and made new just before |
| The worktree's `.git` entry | `CoderTree.cs:82-93` | No | Safe: bound read-only in every unit |
| Protected submodule paths | `CoderCommits.cs:108-114` | No | Safe: it looks and lists, it opens no file |
| The logs of the checks, the job's own files | `CoderChecks.cs:152`, `RoleJobFolder.cs:162` | No | Safe: in the data folder, written by the host, in no unit |

git's own reads of the worktree are not in this list (passes of E2). One fact from the scratch repository: git does not see a FIFO at a name that it does not track (`status` is empty, `add -A` is exit 0). A FIFO at a TRACKED name makes `git add -A` fail.

### P9.5 New must-change

**M20. A FIFO at `.coai/toolchain.yaml` in the worktree stops the job for ever in the check round. No stop is reported.**

- Where: `ChecksAtBase.RunAsync` compares the toolchain file of the base with the file in the worktree (`ChecksAtBase.cs:47`). The worktree is read by `DiskWorkFolder` (`CoderChecks.cs:82`). `DiskWorkFolder.Read` looks with `FileInfo` and then opens with `info.OpenRead()` (`DiskWorkFolder.cs:28-40`). A FIFO is there, is not a link and has length 0, so the plain open is made, and it waits.
- The read is made in EVERY check round, for EVERY vendor, before any step runs, also when the base has no toolchain file. Its one use is the note "the worktree added, removed or changed the file".
- Who can make the FIFO:
  - Code that the coder wrote and that a CHECK runs (a build step, a test). The worktree is writable in a check unit (`BoundaryUnit.cs:41`), and a check unit can make a FIFO (P9_3 `fifo-by-check`). No command tool of the model is needed.
  - A command of the model in the coder's unit. codex: the shell of the vendor sandbox, where the worktree is the write root (`CodexCoderVendor.cs:199`). I did not measure that the vendor sandbox lets `mkfifo` through; the fake CLI has no vendor sandbox. claude: it has no shell tool (`CoderVendors.cs:259-271`), so only when a command got through. agy: only when a command tool got through (A4, A10), as in M18 and M19.
- What git stops: a FIFO at a TRACKED name. `git add` fails and the job ends `coder-commit` in 1 second (P9_3 `fifo`). A FIFO that a check makes at a tracked name ends `checks-changed-tree` (P9_3 `fifo-by-check`).
- What git does not stop: a FIFO at a name that is not tracked. Three ways, each probed through the product's road with real units (the codex rig; the fake CLI stands for the model's tools):
  1. **The base has no toolchain file.** A command in the coder's unit makes the FIFO in turn 0. Without the FIFO the job ends `not-configured` in 1 second (P9_3 `none-control`). With it: **the job did not end in 90 seconds.** State `running`, heartbeat phase `checks`, one commit (P9_3 `none-fifo`).
  2. **The base has a toolchain file, and a command makes the FIFO.** Turn 0 deletes the file and leaves the check red. coai commits the deletion; it is only a note in the result. The fix turn makes the FIFO at the name, which git does not track any more. Two commits. **The job did not end in 90 seconds** (P9_3 `delete-then-fifo`, run twice).
  3. **The base has a toolchain file, and a CHECK makes the FIFO. No command of the model.** Turn 0 deletes the file (a patch tool can) and leaves the check red. The build step of the first check round makes the FIFO; in the probe the base's build step stands for a test that the coder wrote. git does not see it, so the round is not `checks-changed-tree`. The fix turn is committed. **The second check round did not end in 90 seconds** (P9_3 `delete-then-check-fifo`).
  In ways 2 and 3 the job went on and ended `succeeded` when I opened and closed the write end of the FIFO.
- Who is exposed today: **codex**, which is launchable. Way 3 needs only its patch tool and a test that the model wrote; ways 1 and 2 need its shell to make a FIFO in the worktree. **claude**: its five tools (`ConfinementPlanner.cs:81`) have no shell and cannot delete a file, so I found no way without a command that got through. **agy**: the same, with A4 or A10.
- The pure probe P9_2: `DiskWorkFolder.Read` of a FIFO gave no answer in 3 seconds.
- What holds: the wait itself makes nothing green and commits nothing more. What is lost: the end of the job. The runner awaits the work with no limit, the heartbeat goes on, and `role_cancel` and the budget cancel a token that the blocked read does not see. This is the harm of M18 and M19, in the next file.
- Smallest fix: read the worktree's toolchain file with `UnitWrittenFile.Read(path, ToolchainTables.MaxBytes)`. Missing is "not there". Content is the text. Anything else is "changed", with the reason in the note. It must never wait. A FIFO there does not prove a command of the model (a check can make it), so it is not a boundary by itself.
- Tests: P9_2 as a pure test. P9_3 `none-fifo`, `delete-then-fifo` and `delete-then-check-fifo` as rig cases.

### P9.6 The helper itself

**Can a race between the type check and the read be used?** No.
- `Read`: one `open`, then `statx` of that descriptor, then the read of that descriptor (`RegularFiles.UnitWritten.cs:59-73`). Nothing is asked of the path a second time. A file that grows during the read is cut at the limit, because the bytes are counted.
- `Problem` opens and closes. A later read opens again, and is safe by itself.
- `Walk` looks at an entry and enters it later (`UnitWrittenFile.cs:70-80`). A swap between the two needs a live process of the unit. A unit that is not gone ends the job before any check or launch (`CoderTurns.cs:131-134`, `CoderTurnCheck.cs:23`).
- On macOS and Windows the helper looks and then opens plainly (`RegularFiles.UnitWritten.cs:76-82`). Its comment says macOS has no FIFO of this kind; it has. The coder road is refused on those systems (`CoderBoundary.cs:197`), so this cannot be reached.

**Does a link in a PARENT folder get followed?** Yes. `O_NOFOLLOW` guards the last name only.
- P9_1a: `home/.gemini` is a link to a folder outside. `TokensOf` gave the token of the file outside. P9_1b: `Problem` gave no sentence, so the entries check does not see it.
- Which parents can a unit replace? agy: `home`, `.gemini`, `antigravity-cli` (the unit binds `<job>/coder/agy` writable, `AgyCoderUnit.cs:56`). codex: `home`, `.codex`, and the folders below `sessions`. Not replaceable: the vendor folder itself, so `report.txt` and `guard-records.jsonl` have no parent that a unit can change.
- What I looked for and did not find:
  - A leak. The tokens go to the scrub and to the commit search, in memory. A sentence names an id, never a text. The one read whose bytes reach a person is `report.txt`, and it has no such parent.
  - A wait. The file below the linked parent is opened the same way.
  - A delete or a write through the link. The delete of the copy resolves the real folder and refuses a path that leaves the home (`CoderOsLayer.cs:257-274`). The next copy and the next codex launch refuse a link at any level (`SignInCopy.cs:204-236`, `CodexCoderVendor.cs:210-218`), and that ends the job `coder-boundary`.
- So it is a should (S65), not a must. The words "no link" of the commit are true for the last name only.

**Can `Walk` be made to hang or to use too much memory?** No hang and no large memory. Two limits are weaker than they read.
- It opens no file. A FIFO is listed, not opened. A link ends the walk with a sentence.
- Memory: the list holds at most 20,000 paths.
- The limit of 20,000 counts the files found plus the folders that wait. It does not count folders that were entered. P9_4: 30,150 empty folders were walked in 386 ms with no problem said. The depth limit holds, and the 20-second bound holds. At this speed 20 seconds are about 1.5 million folders; after the bound the answer is "did not finish" (closed side), and the walk goes on in the background until the tree ends (S67).

**Does `Bounded` leave a blocked thread or a descriptor behind on timeout?**
- The task is not stopped; it ends by itself. A read of this helper cannot wait (no FIFO, no link, a regular file on the local disk), so the thread comes back and the `using` closes the descriptor.
- P9_6: 1,800 reads and looks of a FIFO, a link and a regular file. Open descriptors before 225, after 181. Nothing was left open.
- Each call holds two pool threads for its time: the caller waits and the work runs. Inside `Stop` this is three deep. That is fine for one job.
- **The server cannot be starved.** Every call of the helper is in the runner, and the runner is one process for one job (`--role-run`, `RoleRunMode.cs:84`). The MCP server never calls it. What a timeout leaves ends with the job.
- What can still run long after a timeout: the walk of a very large tree (S67), and the read of many large rollouts (S68). Both use CPU and memory in the runner of that job only.

### P9.7 codex: does this fix change behaviour, and is a new real run needed?

**No new real codex run is needed.**

What changes for codex (it is launchable since the merge):

| Change | For an honest turn |
|---|---|
| The check limit is 30 seconds, not 5 | More time. No other change |
| The rollouts are listed by `Walk`: at most 20,000 entries, 12 levels, no link | The CLI writes `sessions/YYYY/MM/DD/rollout-….jsonl`: 3 levels, one file a thread, no link |
| A rollout is read through the helper, at most 512 MiB | The same lines are read. Measured: 64 MiB of 1 KiB lines in 194 ms (P9_5), so 256 MiB is under 1 second |
| New `coder-boundary` causes: a rollout, `auth.json` or `report.txt` that is not a plain file or is over its limit; a link or an odd node in `sessions`, also on a cut turn | The CLI makes plain files. `auth.json` is a few KiB; the limit is 256 KiB. The report is the model's last message; the limit is 1 MiB, as before. A report over the limit was "no answer" before and is `coder-boundary` now |
| `report.txt` is copied as bytes that were read, not with `File.Copy` | The same bytes. The host file gets the default mode, not the mode of the unit's file. It is in the data folder |
| A fresh launch is refused when `sessions` holds a FIFO, a link or a file over the limit | Not met by an honest job |

Not changed: the unit's properties, the argv, the environment, `config.toml`, the schema, the 28 preflight rows (`git diff` of the range shows no change in `CodexPreflight.cs`, `CodexPreflightRows.cs`, `BoundaryUnit.cs`).

What only a real turn can show is that the real CLI leaves these entries as plain files.
- `report.txt`: the real run of pass 7 already showed it. The old code refused a link and a file over 1 MiB, and both real turns gave an answer.
- The rollouts: the same run showed that `sessions` holds only `rollout-….jsonl` files and that they can be read. It did not show that none is a link; the CLI has no reason to make one.
- `auth.json`: not shown by a run. The host makes that file itself, and the copy has no refresh token, so the CLI has no reason to write it again.

A wrong guess here gives a refused job (closed side), never a green one. The codex test classes are green at the commit (105 cases in four classes).

### P9.8 The merge `0cef12ec`

- No conflict: the combined diff of the merge is empty.
- It brings: `launchable: true` for codex (`shared/coder-boundary-releases.json:23`), the name `gpt-6.1-terra`, seven test files, the gated real-codex harness and its results file. No product code.
- It meets the fix in one way. codex is launchable in the shipped table, so from this commit the codex half of the fix (the rollouts, `Clear`, `report.txt`, the entries check) is on a road that a person can start. Before the merge it was behind the mark.
- agy stays off: `shared/coder-boundary-releases.json:84`, and the tests that hold it (`ACoderVendorSeamTests.cs:219`, `AAgyCoderLaunchTests.cs:85`).
- At the unchanged commit the codex classes are green: `ACodexCoderLaunchTests` 26, `ACodexCoderFixTurnsTests` 36, `ACodexCoderBoundaryTests` 7, `ACodexTurnCheckTests` 36. I did not run the route tests that the merge changed; they do not touch the fix.

### P9.9 Should

Numbered from S65.

- **S65. A link in a parent folder is followed, and the entries check does not see it** (P9.6; P9_1a, P9_1b red). No harm found. Make "no link" true for the whole path below the vendor's folder: resolve the folder of the entry and compare it with the path as spelled, inside the helper.
- **S66. The expiry of the sign-in copy is still a plain read** (`SignInCopy.cs:130`, from `CoderBoundary.cs:132`). For codex the copy is not made new for every turn (`CoderBoundary.cs:121`), so this reads the copy as the unit left it. P9_8: a FIFO there gives no answer in 3 seconds. Today the entries check of the turn before comes first and no unit is alive between, so it is safe. A runner that takes a job over after a kill meets the copy with no check before. Read it through the helper.
- **S67. The entry limit of `Walk` does not count folders that were entered** (`UnitWrittenFile.cs:72`; P9_4 red). Count every entry that is seen.
- **S68. A rollout is read by line, and a line has no limit** (`CodexTurnCheck.cs:275-277`). "Read as a stream" is true only when the file has line ends. P9_5: a file of 64 MiB with no line end (one `truncate`) made the look allocate 576 MiB in 393 ms. By the same ratio, a file at the limit of 512 MiB allocates about 4.6 GiB (not run: RAM is tight). Also, every rollout file is read, and there can be thousands. The 30-second limit ends the wait, not the work. Read in pieces and call a line over a few MiB "not a regular rollout", or lower the limit to 64 MiB. For codex this needs a command outside the vendor sandbox (risk C1).
- **S69. Three roads have no red of their own.**
  - A rollout that is a FIFO, in the turn check: with a plain open the two rig cases stay green, because the 30-second limit ends the job with the same word. Assert the sentence ("not a regular file"), or a time under 10 seconds.
  - A FIFO at the CODEX sign-in copy: no test. P9_9 is that test; without the entries check the job ends green.
  - A link at the sign-in copy through `TokensOf` (P8_5 of pass 8): only the helper is tested with a link.
- **S70. `KeyScanner` on folders that a unit can write** (`KeyScanner.cs:60-63`, `:85-95`): no limit of size, time or entries, and a plain open after a look. A FIFO is safe (length 0, not opened). A sparse file of many GiB is read to its end. The search of the coder's home runs also when the unit could not be stopped (`RoleModelLauncher.cs:169`), where a swap after the look is possible. Use the helper's open, and a time bound.
- **S71. The helper and `HostReadEntries` are in no `research/module_*.md`.**
- **S58, still open.** An exception inside a check, and now inside `Bounded`, arrives as an `AggregateException`.
- **S60, still open, and less urgent.** With the 30-second limit the quadratic tie cannot give a false boundary inside the 8 Mi stream limit (20,000 calls took 6.3 seconds in pass 8).
- **Open from before, not touched by this range:** S50 to S54, S56, S57, S63, S64, R-S2, R-S7, S26, R-S9, R-S10, S2, S29, S36, S41, S48.

### P9.10 Confirmed safe

- **The order.** The stream is judged first, then the records, then the entries. An entry problem never replaces a boundary that the stream proved.
- **A false boundary from the new limits.** Records 16 MiB are about 110,000 tool calls. A sign-in copy is a few KiB. The time bounds (20 and 30 seconds) are fixed and far above the measured times.
- **The sentence of an entry problem** names the last name of the file and the reason. It holds no content, and it goes through the redaction (`CoderTurnCheck.cs:90`).
- **The `finally` of the job with a FIFO at the copy**: the tokens are read through the helper, and the FIFO is deleted by its own name (`CoderOsLayer.cs:284-300`).
- **Before a launch**, a FIFO at the copy, at `report.txt`, at `config.toml` or at the records is deleted or moved by name and the file is made new with `CreateNew` (`SignInCopy.cs:238-249`, `CodexCoderVendor.cs:347-353`, `AgyCoderVendor.cs:159-182`). `EntryKind.File` means "not a folder and not a link", so it covers a FIFO.
- **A hard link** to a file of the person cannot be made in a unit: the home is hidden, and a link across two mounts is refused.
- **claude.** It names no entry (`CoderVendors.cs:146`). Its sign-in is a bind of the real file. Its check answers at once.
- **`launchable`** is on for codex and off for agy.

### P9.11 The flip list for agy

Owed before `launchable` turns on:
1. **M20**, by the rule that made M18 and M19 must-changes for agy: when a command of the model gets through (A4, A10), a FIFO stops the job for ever. It is one call site. It is not agy-only, and agy is not the vendor that is most open to it: codex is, and codex is launchable now. The coordinator decides if M20 holds the agy flip; I recommend to fix it first, for codex.
2. **The flip commit itself**: the mark at `shared/coder-boundary-releases.json:84` and the two tests that hold it off (`ACoderVendorSeamTests.cs:219`, `AAgyCoderLaunchTests.cs:85`).

Done, and not owed again:
- M18 (pass 8) and M19 (this pass).
- The two real runs of pass 8 need no repeat: this commit changes no unit property, no preflight row and no argv of agy.

Recommended, not owed: the paid agy turns of P8.11. They now show one more thing: the real agy leaves its sign-in copy a plain file under 256 KiB after a turn. If it does not, every real turn ends `coder-boundary` (closed side).

**Waiting on the owner, not on code:** G15 literal or relaxed; the 45-minute cap; the number of paid agy turns.

## Pass 10 — M20, S66–S69, the agy flip, end state at 5c6cfbe1

Verifier: Fable (security verify role), 2026-11-27. Read-only for the branch: no edit, no commit, no push.
Branch `feat/v2-ep3-e3-roles`, head `5c6cfbe1` (the same commit as `feat/v2-ep3-e3-agy`). Range `7d118804..5c6cfbe1`: `b9df00dc` (the agy flip), `4466732e` (the results of the real agy jobs), `5c6cfbe1` (the fix).
Every path and line below is at `5c6cfbe1`.

Threat model, as before: a model under injection inside its unit, and the unit of a later vendor of the same job.

**What was done.**
- Read: pass 9 here; the diff of the range (10 product files, of which one is the flip JSON; the tests); `RESULTS_e3_agy_real_jobs.md`; the road of a check round from `CoderFixLoop` to `RoleRunner`.
- Built the head in a temporary worktree (`/home/jinx/coai-t3/wt-pass10`, detached at `5c6cfbe1`) under the shared lock. The class of the fix, `AToolchainFileOfTheWorktreeTests`: **9 of 9 green** at the unchanged commit.
- Took parts of the fix out of the PRODUCT code of that worktree only (the tests stayed), in four builds (0 errors each), and ran the class again (P10.3).
- Added 5 probe cases of my own to that worktree only (class `P10ProbeTests`). Each probe states the SAFE behaviour, so a red probe shows a gap. Three of them go through the product's own road with real units. Result at the unchanged commit: 4 green, 1 red (P10.4).
- The temporary worktree, its logs and the scratch files are removed. No unit and no test process is left. The branch worktree is clean at `5c6cfbe1`.
- Not run: codex, agy, a model call. No key file and no environment was read. The real HOME was not touched. No Python.

**Owner decisions that this pass follows:** no version stops anything; the risks agy A1 to A13 and codex C1 to C7, C9 are accepted; G15 stays literal and the 45-minute cap stays until the owner answers; the codex name `terra` is an open owner question and is not a defect here.

### P10.1 Verdict

| Item | Verdict | In one line |
|---|---|---|
| M20 | **CLOSED** | The three reads of the worktree's `.coai/toolchain.yaml` go through the helper. A FIFO answers in 1 to 6 ms. The job ends `failed`, `checks-changed-tree`. The tests go red when the fix is taken out |
| S66 | **Closed** | The expiry of the job's copy is read with no wait and no link. The real file is read with no wait, and a link is followed |
| S67 | **Closed** | The walk counts every entry. The test goes red with the old count |
| S68 | **Closed** | A line has a limit of 4 Mi characters. A file of 64 MiB with no line end: 28 to 41 ms and 8 MiB (pass 9: 576 MiB). The reader is about 4 times slower than before (S76) |
| S69 | **2 of 3 parts built** | Built: the FIFO at the codex sign-in copy, and the link at the copy through `TokensOf`. Both go red. Not built: a red for a rollout that is a FIFO in the turn check |
| The word `checks-changed-tree` | **Right. Keep it** | `coder-boundary` would say more than is proved. Neither word can end green and neither fails over. One case reads weaker than the evidence (S72) |
| The flip commit `b9df00dc` | **Safe** | No product code: one JSON mark and tests. The rule "ship AND the mark" is still held by a test with a table of its own. No test can make a paid call |
| The real agy jobs `4466732e` | Read, not repeated | 5 paid turns, 3 jobs green, 0 tool events outside the three. They ran on `b9df00dc`. No new paid run is needed for `5c6cfbe1` |
| **New must-change** | **None** | No M21 |
| **End state** | **No must-change is open** | codex and agy are launchable. 7 groups of should-items are worth doing before the merge (P10.10) |

### P10.2 M20: the evidence

1. **The read.** `DiskWorkFolder.Read` looks with `FileInfo` (a link and a name that is not there are `Missing`, a length over the limit is `TooLarge`) and then reads with `UnitWrittenFile.Read` (`DiskWorkFolder.cs:29-41`). That is `open` with `O_NONBLOCK | O_NOFOLLOW`, the type asked of the open descriptor, a counted read, and a bound of 20 seconds (`UnitWrittenFile.cs:82-83`, `RegularFiles.UnitWritten.cs:57-74`).
2. **The answers.** A FIFO, a socket, a link that appeared after the look, and a read that did not finish are the new case `WorkFile.NotRegular` (`DiskWorkFolder.cs:57-58`, `IWorkFolder.cs:24-25`). A read that the disk refuses still throws (`:59-63`).
3. **Every read of that file on the coder road goes this way.** There are three, and no other: before the round (`CoderChecks.cs:81`), inside the round before any step (`ChecksAtBase.cs:47`), after the round (`CoderChecks.cs:91`).
4. **The end.** `NotRegular` in a look makes the round `tree_changed` (`CoderChecks.cs:139-145`). The round is not red, so the loop ends at once (`CoderFixLoop.cs:109-112`). The job is `failed` with the word `checks-changed-tree` and its envelope (`CoderChecks.cs:47`, `CoderWork.cs:215`, `:248`). No digest goes to the coder.
5. **The same fix helps two more readers.** `CoderTask.cs:99` (the start check, in the server) and `BaPlanCheck.cs:40` read plan files through the same class. A FIFO in a plan folder is now an error line, not a wait.

### P10.3 Red-first: do the tests go red when the fix is taken out?

Class `AToolchainFileOfTheWorktreeTests`, 9 cases. At the unchanged commit: 9 green in 10 seconds.

| Build | What was taken out | Red | Which, and what the job did |
|---|---|---|---|
| 1 | `DiskWorkFolder.Read` is the old plain open | **4 of 9** | `P9_2` (no answer in 3 s). The three ways of `M20_…`: the job did not end in 90 seconds |
| 1 | The walk counts as before (S67) | **1 of 9** | `S67_…` |
| 1 | The rollout is read with the plain `ReadLine` (S68) | **1 of 9** | `S68_…` |
| 1 | `TokensOf` follows a link (S69) | **1 of 9** | `S69_ALinkAtTheSignInCopy…`: it gave the token of the file outside |
| 2 | Both looks answer nothing (the read without a wait stays) | **3 of 9** | Way 1 ends `not-configured`. **Ways 2 and 3 end `succeeded`** |
| 2 | `ExpiryOfFile` is the old plain read (S66) | **1 of 9** | `S66_…` (no answer in 3 s) |
| 2 | The entries check asks no entry (S69) | **1 of 9** | `S69_AFifoMadeAtTheCodexSignInCopy…`: the job ended `succeeded` |
| 3 | Only the look BEFORE the round answers nothing | **0 of 9** | The look after the round gives the same word. Only the sentence differs |
| 3 | `CoderBoundary.cs:132` follows a link (`noFollow: false`) | **0** | Also 0 of 37 in `ASignInCopyTests` and 0 of 18 in `AUnitWrittenEntriesTests` |
| 4 | Only the look AFTER the round answers nothing | **0 of 9** | My probe P10_2 goes red: the job ends `succeeded` |

Build 1 gave 7 red of 9. Build 2 gave 5 red of 9.

What this shows:
- The fix of the wait has its red (build 1).
- The two looks have a red together (build 2). Without them a job with a FIFO at that name ends **green** in two of the three ways.
- **Neither look has a red of its own.** The look after the round is the one line that stops a green end when a check makes the FIFO in a round that passes. No test of the commit holds it (S74).

### P10.4 The probes

| Probe | The safe behaviour | At `5c6cfbe1` |
|---|---|---|
| P10_1, pure | Every kind of entry at the name gives an answer at once | Green. FIFO: `NotRegular`, 1 to 6 ms. Socket: `NotRegular`. Link to a file: `Missing`. Link to a FIFO: `Missing`. `.coai` is a link to a folder that holds a FIFO: `NotRegular`, 0 ms. Folder: `Missing`. Over the limit: `TooLarge`. Mode 000: **throws** `UnauthorizedAccessException` |
| P10_2, rig | A check makes the FIFO in a round that is GREEN: the job is not green | Green: `failed`, `checks-changed-tree`, "what the round said is not trusted" |
| P10_3, rig | A check leaves a file with mode 000 at the name: the job ends with a word of the checks | **Red: `failed`, `runner-error`**, "the runner failed: UnauthorizedAccessException: no read permission (errno 13)". No envelope, no attempt in the record (S73) |
| P10_4, rig | Way 1 with a second model in the route: no failover | Green: `checks-changed-tree`, 1 attempt, 1 launch. The second model was not started |
| P10_5, pure | The rollout read is fast and small | Green. 64 MiB of 1 KiB lines: 532 to 1029 ms in four runs (pass 9, old reader: 194 ms). 64 MiB with no line end: 28 to 41 ms, 8 MiB |

### P10.5 The word: `checks-changed-tree` or `coder-boundary`?

**Keep `checks-changed-tree`.**

- **The coder's reason is right for the look after the round.** A check runs code that the coder wrote, in a unit that can write the worktree. A test can make the FIFO with no command of the model (way 3, P10_2). So a FIFO there proves no broken boundary.
- **It is also right for codex in every case.** The shell is a tool of codex, and the worktree is its write root. `mkfifo` in the worktree is inside what codex may do.
- **The word has this meaning already.** The same state is used when the worktree is not at the commit that coai made, before any step runs (`CoderChecks.cs:75-78`). So "the tree is not what coai committed and expected" is its meaning, not only "a check wrote a file".

**Can the job end green?** No. The round is `tree_changed`, never `green`. P10_2 shows the hardest case: every step passes, and the job is `failed`.
**Can the job fail over?** No. The loop returns `Done` with a stop word, and that is the end of the job (`CoderFixLoop.cs:111`, `CoderWork.cs:215`). P10_4: one launch.
**Is the word sent to the coder as a fix?** No. The digest is empty and the round is not red.

**Can a breach look like an ordinary failure?** In one case the word says less than the evidence. It is a should, not a must (S72).
- The look BEFORE the round sees what the coder's TURN left. No check ran between the end of the turn and that look: the look after the round before it saw no FIFO, and a check unit is gone when its client returns. (Before turn 0 only the instruction step ran, when the base declares one. That is the person's own command, not the model's.)
- claude has five tools and no shell. agy has three tools. None of them can make a FIFO. So for claude and agy, a FIFO at the look before the round shows that a command ran in the coder's unit. That is the evidence that M19 ends with `coder-boundary` for an entry in the vendor's folder.
- The job ends `checks-changed-tree`, and the sentence says "a command of the coder or a check made it, and nothing shows which". For this look that is not true: a check did not make it.
- Why it is not a must: the job fails, nothing more is committed, no model is started after it, and the sentence names a command of the coder as a cause. The commit of that turn is on the branch with either word, because the look comes after the commit.

**The round's own note can be lost.** The look after the round answers before the result of the round is read (`CoderChecks.cs:91-94`). A round that was `boundary_refused` in a later step, with a FIFO at the name, ends `checks-changed-tree` and not `coder-boundary`. Both end the job. The older rule for a tree that moved does the same (`:96-100`).

**The word `coder-boundary` for `terra`.** In that case the word means: the layer refused the launch before any unit of the coder ran. No model ran and nothing was written. It is not a sign of a breach. One effect to know: `coder-boundary` never fails over, so a route entry behind `terra` is not tried. In the default list that entry is the agy model. (I took the refusal of `terra` from the brief. I did not measure it.)

### P10.6 Is the look before the round free of a race?

Yes, because nothing rests on it.

- A check step runs after the look. A FIFO that a check makes is seen by the look after the round, or by the look before the next round.
- No read can wait, whatever the order. Each of the three reads opens the name again and is safe alone. A swap between the `FileInfo` look and the open changes only the answer (`Missing`, `NotRegular` or text), never the time.
- No process of a unit is alive between two looks. The coder's unit is checked gone before the commit, and a check unit is gone when its client returns.
- So a race can change only the sentence ("nothing was run" or "what the round said is not trusted"). It cannot bring a wait back, and it cannot make a round green.

### P10.7 `ReadFollowing` on the person's real file

Safe.

- The one caller is the read of the expiry of the REAL file (`CoderBoundary.cs:147`). The path is `<the person's home>/<the vendor's file>`.
- No unit of the job sees that path. The home is hidden in every unit, and the job gets a copy. So only the person can put a link there, and it is the person's own link. The old code followed it too.
- The other reads of the same file follow a link too: the hash before and after (`SignInCopy.cs:45-56`) and the read for the copy (`:69-76`). So the three reads agree.
- The open cannot wait, the type is asked of the open descriptor, and the read stops at 256 KiB (`UnitWrittenFile.cs:58-79`, `RegularFiles.cs:110-124`).
- One limit of the claim: "never waited on" is true for the expiry read only. The hash and the copy still open the real file plainly. That is the person's own file and it is outside the threat model.
- The job's copy is read with `noFollow: true` (`CoderBoundary.cs:132`). The default of the parameter is `false`, which is the less safe side for a new caller (S76).

### P10.8 The flip commit `b9df00dc`

- **What it changes in the product:** one mark, `"launchable": true` for agy (`shared/coder-boundary-releases.json:84`), and the note of the table. No code.
- **The tests that hold the mark on** are enough: the row and the resolution (`AAgyCoderLaunchTests.cs:81`), the table and the order of the vendors (`ACoderVendorSeamTests.cs:212`, `:227`), the text of the JSON file (`:253`), the route of the product's table (`ACoderRouteByVendorTests.cs:89`, `ACoderFailsOverAcrossVendorsTests.cs:377`).
- **The rule itself is still held.** "Launchable only with the verdict ship AND the mark" is tested with a table of the test's own, in all six cases (`ACoderVendorSeamTests.cs:238`). The refusal of a named model on a vendor that is off is tested with a table where agy is off (`ACoderTaskCheckTests.cs`, `Narrow_RefusesTheModelTheCallerNamed…`).
- **The vendor `nonesuch`** is in tests only. It stands for "a vendor with no row", which agy cannot stand for any more. `CoderRoute.Narrow` drops it from a list and refuses a route that has nothing else (`CoderRoute.cs:29-45`). The service test still shows the refusal and no job folder (`ACoderTaskCheckTests.cs:280`). A route in a job file with such a vendor is refused before any launch (`RoleFailover.cs:193`).
- **Does a test now start a real route?**
  - `ACoderTaskCheckTests` starts nothing: its launcher is a stand-in (`NothingStarted`).
  - `TheRealAgyJobsTests` runs only with `COAI_REAL_AGY_JOBS_RUN=1`. It counts the turns and refuses the seventh before any process starts.
  - `TheCoderEpicAcceptanceTests.cs:228` is the one to know. It is a real server and a real runner, and its route is now the fake claude model and then `agy/gemini-4-argon-medium`. The world sets no `COAI_ROLE_AGY_CLI`, so agy means the program on the PATH. agy is reached only when the fake claude turn stops with a limit, a refusal or a crash. Then the probe of the real program can run on the host, with the temporary HOME of the test (by reading, `RoleModelLauncher.cs:117`). After that the sign-in gate refuses, because that HOME holds no agy sign-in file (`CoderBoundary.cs:115-119`). **No paid call is possible.** Set the agy CLI to the fake in that world (S77).
- **The list that the flip rests on** holds M11 to M19 (`AAgyCoderLaunchTests.cs:453`). M20 is in neither list (S75).
- **The order.** Pass 9 asked for M20 before the flip. The flip came first (08:05) and the fix after it (09:46). Both are in the head, and nothing was released between them.
- **The default route.** With an agy reviewer row, the default coder list now ends with the agy model (`shared/role-defaults.json`, owner decision 35). No new setting is needed for a job to fail over to agy.
- **The real agy jobs** (`4466732e`) ran on `b9df00dc`: 3 jobs, 5 paid turns, all green, 0 tool events outside the three, every guard record `allow`, every turn check `Fine`, the real sign-in file equal before and after. One fact for S26: every turn ran a refresh (5 of 5).
- **No new paid run is needed for `5c6cfbe1`.** For an honest agy turn the fix changes two reads of files that are plain and small. A wrong guess gives a refused launch (closed side).

### P10.9 Should

Numbered from S72.

- **S72. The look before the round says "or a check", and for claude and agy that hides what is known** (P10.5). Smallest change: a sentence of its own for that look ("the coder's turn left it; no check ran after the turn"). The coordinator decides if the word becomes `coder-boundary` for a vendor with no command tool.
- **S73. An entry that the disk refuses ends the job `runner-error` with no envelope** (P10_3). A check needs only `chmod 000 .coai/toolchain.yaml`. The throw is right for `validate_work` (exit 74). In `CoderChecks` catch it in the two looks and at `ChecksAtBase.cs:47`, and make the round `tree_changed`. Closed side. Before this commit the read inside the next round threw the same way; the look after the round is a new place for it.
- **S74. Tests of the two looks.**
  - The look after the round has no red (build 4). Add P10_2: turn 0 deletes the file and writes green, the build step makes the FIFO, the job must be `failed`.
  - The look before the round has no red (build 3). Assert the sentence "nothing was run" and no step in the round.
  - `noFollow: true` at `CoderBoundary.cs:132` has no red (build 3). The S66 test reads through the following road only.
  - S69, part 1 (a rollout that is a FIFO in the turn check) is still not built.
- **S75. M20 is not in the lists that the flips rest on** (`ACodexCoderBoundaryTests.cs:210`, `AAgyCoderLaunchTests.cs:453`), and the documents do not have the new meaning of the word: `ARCHITECTURE.md:149` says "(the checks changed the tree)". S71 is still open.
- **S76. Small points of the helper.**
  - The line reader takes one character at a time: about 4 times slower (P10_5). A rollout at the limit of 512 MiB is about 6 seconds here, inside the bounds of 20 and 30 seconds, with less room on a loaded machine.
  - `ReadLine` ends a line at `\n` only and trims every `\r` at the end. The old reader also ended a line at a lone `\r`.
  - `ExpiryOfFile` has `noFollow = false` as its default. Make the safe side the default, or use two names.
  - `DiskWorkFolder` sorts the answers of the helper by the text of the reason (`:55-61`). A changed sentence in the helper changes the sort with no compile error.
- **S77. `TheCoderEpicAcceptanceTests`**: set `COAI_ROLE_AGY_CLI` to the fake, and correct the comment at `:88` ("the mark of agy is off").

### P10.10 End state at `5c6cfbe1`

**No must-change is open.** codex and agy are launchable.
- M15 to M20 are closed by passes 7 to 10 of this file. M1 to M6 are closed by passes 2 and 3. M8 to M10 are in the branch (said in pass 5).
- M11 to M14 were given to the second verifier (pass 6, line 685). Their smallest proofs are in the flip list (`AAgyCoderLaunchTests.cs:453`). I did not check M12 to M14 again in this pass.

**Should be done before E3 is merged toward main.** All are small.

| Item | Why now |
|---|---|
| S74, the test of the look after the round | It is the one line that stops a green end, and it has no red |
| S73 | One `chmod` in a test ends a job `runner-error` with no envelope, for every vendor |
| S72, the sentence only | The stop sentence must not say "or a check" where no check ran |
| S26 and R-S7, with S54 | The refresh unit has no `KeysHidden`, no `SignInHidden` and no home rule. It was a rare unit. With agy on and the 45-minute cap it runs before nearly every agy turn (5 of 5 real turns). The key pool must be in no unit |
| S36, one line for the person | Pass 7: "it should not wait long after the flip". Both vendors are on now |
| S75, S71, R-S10 | The documents and the flip lists must hold what the merge brings |
| S77 | A test world should not name a real CLI |

**Can wait for a later epoch.**
- S65 and S70. The coder's reasons are right: no harm was found for S65, and S70 is cost, not a wait.
- S51 (a crash wins over the rollout when the next model is of another vendor). It needs a fault of the CLI, the work of that turn is thrown away by the restore, and a child runs in the same unit. Routes with two vendors are now usual, so take it early in the next epoch: read the rollouts in `LeaveVendor`.
- S50 and R-S2 (a stream at its ceiling). For agy the guard still denies a tool outside the three.
- S53 (the cover marker has a fixed name). It needs a repository that tracks that name.
- S69 part 1, S76, S52, S56, S58, S60, S63, S64, S41, S48, S29, S2, R-S9.
- The word for a vendor with no command tool (the second half of S72).

**Waiting on the owner, not on code:** G15 literal or relaxed; the 45-minute cap; the codex name `terra`.
