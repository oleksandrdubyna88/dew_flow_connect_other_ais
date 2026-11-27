# Pass 5 re-check — agy M11–M14, head c7f2e259

Verifier: Fable (security verify role), 2026-11-26 and 2026-11-27. Read-only for the branch: no edit, no commit, no push.
Branch `feat/v2-ep3-e3-agy`, head `c7f2e259` (one commit on `ee1271b7`). Every path and line below is at `c7f2e259`.
The text that was checked: pass 5 in `RESULTS_e3_s7_t3_security_verify.md` (M11 to M14, S33 to S38). Pass 6 (codex, M15 to M17) is by another verifier and is only read here.

Threat model, as in pass 5: a model under injection inside its unit, and the unit of a later vendor of the same job.

**What was done.**
- Read: the product diff `ee1271b7..c7f2e259` (14 files) and the test diff; the whole of `AgyTurnCheck`, `CoderTurnCheck`, `AgyCoderVendor`, `AgyCoderUnit`, `AgyCoderPreflight`, `BoundaryPreflight`, `BoundaryJob`, `BoundaryFolders`, `SignInRefresh`; the roads in `CoderBoundary`, `CoderTurns`, `CoderOsLayer`, `RoleModelLauncher`, `RoleFailover`, `CoderFixLoop`, `RoleRunner`, `ReviewerExecutor`, `ProcessLauncher`; the judgement 14.4 and 14.7; SPEC C6; the saved agy streams in `v2/spikes/e3s5_measure/out_agy/`.
- Built the head in a temporary worktree (`/home/jinx/coai-t3/wt-pass5-recheck`, detached at `c7f2e259`) under the shared lock. Ran the new tests there. Then took each fix out of the product code of that worktree, built again, and ran the tests again (section 7).
- Added 9 probe cases of my own to that worktree only (section 8). Each probe states the SAFE behaviour, so a red probe shows a hole.
- Ran one pair of scratch units on made-up folders in the scratchpad (P13). No unit is left.
- Not run: agy, codex, a model call. No key file and no environment was read. The real HOME was not touched. The temporary worktree is removed.

**Owner decisions that this re-check follows:** no version stops anything; the risks agy A1 to A13 and codex C1 to C7, C9 are accepted; G15 stays literal and the 45-minute cap stays until the owner answers.

## 1. Verdict

| # | Verdict | In one line |
|---|---|---|
| M11 | **NOT CLOSED** (two legs are open) | Closed for a turn that ends on its OWN deadline or with an exit that is not 0. Open: (a) a turn that the JOB's budget cuts is not checked, and with the default numbers this is every turn that does not end; (b) on a cut stream, a tool outside the three that is still `ACTIVE` passes |
| M12 | **CLOSED** | The three rows are asked inside the launch unit, and each goes red when its property is dropped |
| M13 | **CLOSED**, and the fix opens **R-M15** | Each vendor has its own `/tmp` and `/var/tmp`. But the two folders are now bind sources INSIDE a folder that the unit can write |
| M14 | **CLOSED** (by reading, and with the fake) | The scratch copy is made stale, the real file is not written, the envelope counts. One real `agy models` on a stale copy that coai made is owed (0 paid calls) |

**New must-change opened by the fix: 1 (R-M15).** It comes from the M13 fix.

**Merge into E3:** see section 9.

## 2. M11 — the turn check for a turn that ended badly

**What is closed.**
- `CoderTurnCheck.StopFirst` no longer asks for `ExitCode: 0, TimedOut: false, Cancelled: false` (`CoderTurnCheck.cs:22-25`). `Stop` asks the vendor for every launch that has a process (`:37`).
- The earlier conditions that skip the check each end the job, or have no process: an OS stop is `coder-boundary` (`RoleFailover.cs:582-585`), an exposed key is `key-in-output` (`:571-574`), a key stop and a refusal have no process. So none of them is a road around the check.
- Every finished launch carries its process result: a timeout, a cancel, a limit and an exit that is not 0 (`ReviewerExecutor.cs:987-1000`, `:1013`, `:1027`). A stream that the deadline cut is kept up to the cut.
- `AgyTurnCheck.Check` reads the mode signs first, for every end (`AgyTurnCheck.cs:46-49`). For a cut stream it calls `Cut` (`:52-55`, `:87-121`): a `DONE` event of another tool, a `DONE` event with no record or with a record that is not `allow`, and records that cannot be read beside a `DONE` event are each `Boundary`.
- `Boundary` wins over crash and limit: both callers read `StopFirst` before `StopOfLaunch` (`RoleFailover.cs:542-550`, `CoderFixLoop.cs:166`), and `coder-boundary` never fails over (`RoleStops.cs:62`).
- claude and codex answer `Fine` for every end at this head (`CoderVendors.cs:220`, `CodexTurnCheck.cs:11`), so the wider wiring changes nothing for them here. The codex half after the merge is M16 of pass 6.

**Leg (a), open: a turn that the job's budget cuts is never checked.**
- Where: `RoleFailover.cs:401-404` returns `Interrupted` when the job's token is cancelled, BEFORE `ReadAsync` (the place of the check, `:542`). `CoderFixLoop.cs:164-166` answers `stopped` for a cancelled token and does not call `StopFirst`. `CoderFixLoop.cs:184-187` replaces ANY stop of a fix turn, a `coder-boundary` too, with `timeout` when the budget is used up.
  The stream itself can also be lost on this road: the second snapshot runs with the cancelled token (`CoderTurns.cs:77`), and for a cancel that passes through `CoderTurnLauncher` answers an EMPTY result (`CoderOsLayer.cs:198-202`). The fix must keep the result.
- Why this is the usual case and not a corner: the deadline of a turn is the smallest of three numbers: what is left of the job's budget, the attempt timeout, and agy's cap. The job's budget is 30 minutes by default (`RoleJobModels.cs:222`). The coder's attempt timeout is 60 minutes (`CoderWork.cs:38`). The cap is 45 minutes (`AgyCoderVendor.cs:51`).
  So in a job with the default budget the deadline of EVERY turn is the rest of the job's budget. The job's own timer starts before the work (`RoleRunner.cs:96`) and the launch starts after the sign-in copy and the preflight, so the job's timer fires first.
  The scenario of M11 is "a model that got such a tool through has only to keep the turn going until its deadline". With the default numbers that turn ends by the job's budget, and nothing is compared. The fix is reached only when the budget is longer than the cap plus what the job used before.
- What is better than before: the job does not fail over and cannot end green. It ends `failed`, stop `timeout`. What is the same as before: nothing says that a boundary broke. G15 is the ONE check at the time of a job for a tool that the hook was not called for (A4, A10); a person who sees `timeout` starts the job again with a longer budget.
- Measured (section 8, probes 5 and 6): a job with a budget of 1 minute, a turn that holds after a `DONE` event with no record. Turn 0: `failed`, `timeout`. A fix turn: `failed`, `timeout`. Expected by M11: `coder-boundary`.
- Smallest fix: keep the result of a cut launch (do not replace it by an empty one), and ask `CoderTurnCheck.Stop` before the three places above. A `Boundary` ends the job `coder-boundary`; it wins over `timeout` and `stopped`. A cancel by the person can keep its word `cancelled`, with the boundary sentence as a notice.
  Tests through the rig: the two probes of section 8.

**Leg (b), open: a tool outside the three that did not end passes on a cut stream.**
- Where: `Cut` looks only at events whose last state is `DONE` (`AgyTurnCheck.cs:89`). An event of another tool whose last state is `ACTIVE` is not looked at. The test says it for `write_file` ("an event that did not end is what a cut explains", `AAgyCoderFixTurnsTests.cs:354`); there is no case for another tool.
- Measured shape (`out_agy/agy_t1.jsonl`, `agy_t5.jsonl` to `agy_t7.jsonl`): every tool event is first `ACTIVE`, then `DONE` or `ERROR`. A denied tool is `ACTIVE` for 0.05 to 0.16 seconds. `run_command` is `ACTIVE`, then `DONE`.
- Scenario: the model got a command tool through (the case of M11). The command itself does not return: a listener, a reverse shell, `sleep`. This is the usual shape of such a command; the model does not have to "keep the turn going".
  The event stays `ACTIVE`, the deadline cuts the turn, `Cut` answers nothing, the turn is a crash, the job fails over and can end green on the next model.
- A cut does not explain this event. On a clean stream ANY event of another tool ends the job, whatever its state (`AgyTurnCheck.cs:171-175`, G15 item 4). A cut explains a missing END of an event. It does not explain that a tool outside the three was started at all.
  An honest cut can leave a denied tool `ACTIVE` only inside the 0.05 to 0.16 seconds of the hook call; that case then ends `coder-boundary`, which is the closed side.
- Note: pass 5 wrote "it does not ask for an event that did not end". The coder built exactly that. The words were too wide: they are right for the three tools and wrong for another tool.
- Measured (section 8, probes 7 to 9): the same `ACTIVE` `run_command` event is `Boundary` on a clean stream and `Fine` on a cut one (exit 1, and a timeout).
- Smallest fix: in `Cut`, an event of a tool outside the three is `Boundary` in every state. One line in the pure test (`ACTIVE`, `run_command`, exit 1) and one case in the rig theory (a held turn after an `ACTIVE` event of another tool).

**"Nothing proved → Fine" on a cut stream, the rest.** Safe for: a record with no event, an event of the three tools that did not end, unequal counts. Not safe for leg (b). See also R-S2 (a stream cut by the size ceiling).

## 3. M12 — the rows of agy's preflight

**CLOSED.**
- `AgyCoderPreflight.ExtraItems` holds `keys-folder-hidden`, `sign-in-not-in-unit` and `other-vendor-folder-hidden` (`AgyCoderPreflight.cs:50`); `Items` is the table's rows and these three (`:53`). The script asks the two shared fragments and the new row (`:189`; `BoundaryPreflight.cs:144-159`; `AgyCoderPreflight.cs:138-145`).
- The keys row reads the folder AND the keys file (`AgyCoderPreflight.cs:279`). The sign-in row asks a READ (`-r`), which is the right question for `InaccessiblePaths` (P12 of pass 6).
- The other-vendor row is an observation: coai makes the folder of each other vendor when it is not there, with a canary, and removes only what it made (`:216-239`, `:244-250`). No folder named, or one that cannot be made: the row is bad ("nothing was observed"). No model runs between the plant and the tidy; only the preflight's own script does.
- The arguments are in the order the header reads them (`:67`, `:275-281`); the submodule row follows with `shift 0`.
- A row that is missing refuses (the shared reader), so the three cannot drop out in silence.

Remarks, not must-change: R-S3 (the AG-R2 row is now narrower), R-S4 (the real codex file is not asked), R-S5 (the sentences say "the check's unit").

## 4. M13 — a temp folder for each vendor

**CLOSED for what M13 named.**
- `BoundaryJob.CoderTempOf(vendor)` is `<job>/coder/<vendor>/tmp` and `CoderVarTempOf(vendor)` is `<job>/coder/<vendor>/vartmp` (`BoundaryJob.cs:150`, `:153`). The old names are claude's (`:147`, `:156`).
- Every product caller uses the vendor's own: the three units (`CoderUnit.cs:63-64`, `AgyCoderUnit.cs:58-59`, `CodexCoderVendor.cs:134`), the folders each vendor makes (`BoundaryJob.cs:205`, `AgyCoderUnit.cs:38`, `CodexCoderVendor.cs:162`), the markers and their tidy (`CoderBoundary.cs:201`, `:209`, `:220`), and the key search after a launch (`RoleModelLauncher.cs:169`). A search of the product code finds no other user and no spelled `coder/tmp`.
- **The claude path** is `{J}/coder/claude/tmp` and `{J}/coder/claude/vartmp` in the unit, the folders, the markers and the key search. It is right everywhere I looked. The check unit uses `<job>/tmp` (`BoundaryJob.cs:75`), which did not change.
- **Hidden from the other units:** each unit covers the folder of the jobs with a read-only tmpfs and binds only `<job>/coder/<its own vendor>`. So the temp folders of the other vendors are under the cover. A row observes it for agy only (`other-vendor-folder-hidden`); for claude and codex it is by the property list and one test (R-S6).

**But the fix opens R-M15 (section 6).**

## 5. M14 — the forced refresh

**CLOSED, by reading and with the fake.**
- `SignInRefresh.RunAsync` copies the real file into the scratch home and then calls `MakeStale` on the COPY (`SignInRefresh.cs:76-88`): it sets `token/expiry` to two hours in the past and writes the copy again (`:109-145`). The caller passes the field only for a vendor whose expiry is a field (`CoderBoundary.cs:159`); codex (a JWT) is not touched.
- **The real file is never written.** It is opened once, for reading, with a shared handle (`SignInRefresh.cs:76`). Every write goes to `Path.Combine(scratch, relative)`; `scratch` is `<job>/refresh` and `relative` is a constant of the recipe. The test compares the bytes and the time of the real file before and after.
- **The scratch copy is edited safely.** coai deletes a leftover scratch folder first (`CoderBoundary.cs:106`), makes the folders with mode 0700, and makes the file with `CreateNew` and mode 0600 (`SignInRefresh.cs:62-80`). `MakeStale` then opens the same path again and truncates it (`:136`). It does not ask if the name is a link.
  That is safe here: `<job>/refresh` is under the read-only cover of every unit of the job and is bound into none; no unit of the job is alive at that time (a unit that cannot be stopped ends the job); and the file was made by coai some lines before.
- **No token in a log or an argv.** The sentences of `MakeStale` are fixed; an exception gives only its type name (`:143`). The argv of the refresh unit is the program and `models`; the environment is five fixed names.
- **Fail closed.** No expiry field, a file that is not a JSON object, a write that fails: no refresh, the scratch is deleted, no launch. A refresh that does nothing leaves a copy that is two hours expired, and the gate before every launch refuses it (`CoderBoundary.cs:124`). So a stale copy never reaches a start (A11).
- **The count.** `Refreshes` is counted for each refresh unit (`CoderBoundary.cs:157`) and is in the envelope (`CoderOsLayer.cs:55`, `CoderEnvelope.cs:59`, `:79`).
- **Not proved here:** that the real agy takes the copy as coai writes it. coai writes the whole JSON again in the compact form and the time in the form `2026-11-26T21:33:18.1234567Z`. The measure (10.5) used its own edit. One `agy models` on a stale copy made by `MakeStale` settles it; it costs 0 paid calls. A refusal by agy is on the closed side (exit not 0: no launch).
- The 45-minute cap is not changed. The number is the owner's.

Remarks: R-S7 (the refresh unit has no `KeysHidden` and no `SignInHidden`, S26; it now runs for about nine turns of ten), R-S8 (`MakeStale` does not look at the name before it truncates).

## 6. New must-change

**R-M15. A unit can swap its temp folder for a link, and its next unit gets the target of the link as `/tmp` or `/var/tmp`, writable.** Opened by the M13 fix.

- Where: the bind sources `<job>/coder/<vendor>/tmp` and `vartmp` (`BoundaryJob.cs:150`, `:153`) now lie INSIDE `<job>/coder/<vendor>`, which the same unit binds writable (`CoderUnit.cs:62-64`, `AgyCoderUnit.cs:56-59`, `CodexCoderVendor.cs:132-134`).
  Before the fix the sources were `<job>/coder/tmp` and `<job>/coder/vartmp`. Their names were under the read-only cover; a unit saw only their content, as `/tmp` and `/var/tmp`, and could not replace the folder itself.
  The code knows this rule for the submodule mounts: "a unit may bind a folder of the job writable, and a source reachable through it could be changed from inside" (`BoundaryJob.cs:67`). The fix did not apply it to the temp folders.
- Nothing looks at the two names before the next launch. `BoundaryFolders.Prepare` calls `Directory.CreateDirectory` (a link to a folder passes) and writes the marker through the link (`BoundaryFolders.cs:21-28`). `JobRootOnDiskProblem` asks the job folder only (`BoundaryJob.cs:110-117`).
  The row `tmp-is-the-jobs` looks for the marker, and coai wrote the marker through the link, so the row is green (`BoundaryPreflight.cs:115-117`). systemd follows a link in the source of a bind, from the host's root.
- Scenario (a model with a shell in its unit: codex; the same standard as M8 of pass 4 and M15 of pass 6): in one turn it runs `mv <job>/coder/codex/vartmp x; ln -s <target> <job>/coder/codex/vartmp`. The target does not have to be visible in the unit.
  The next turn of the same vendor (a repair turn, a fix turn) starts a unit with the target bound at `/var/tmp`, writable. The target can be the main checkout, the person's real home (`~/.ssh`, `~/.codex`, `~/.gemini` with the refresh tokens), the claude key folder, or another job's folder.
  `ProtectHome`, the tmpfs over the main checkout, the cover of the jobs and `InaccessiblePaths` hide those folders at THEIR paths. They do not hide the same folder at `/var/tmp`. Every row is green.
  For agy the same road needs a tool that the guard did not stop (the case of M11). claude has no shell and no tool that makes a link or moves a folder, so a claude job is not open by this.
- This widens the accepted risk C1. C1 says: for a command of the model, a write INSIDE the job folder is held by one layer (the vendor sandbox). With this layout, one write inside the job folder gives the next unit a write OUTSIDE it, where the unit was the second layer.
- Measured: P13 (two scratch units, systemd 259), and the probes 1 to 4 of section 8 through the product's own road.
- Smallest fix, one of two:
  1. Put the sources where no unit can reach their names: for example `<job>/coder-temp/<vendor>/tmp` and `vartmp`, under the cover, bound only as `/tmp` and `/var/tmp` (the shape before the fix, with a folder for each vendor).
  2. Or keep the layout and ask on the host before EVERY admission and launch: each of the two names is a real folder by `Lstat`, and `<job>/coder/<vendor>` is its own real path. A wrong answer refuses the launch (`coder-boundary`). No unit of the job is alive between the question and the bind, so nothing races it.
  Way 1 is the better one: there is no question to forget. Tests: the probes of section 8 (the file must NOT be at the target), for claude's unit, agy's and codex's.

## 7. Red-first: do the new tests go red when the fix is taken out?

At the unchanged head, in the temporary worktree: the new tests of M11, M12 and M14 with the whole row theory of the preflight are **33 of 33 green**; the class `AVendorsHaveTheirOwnTempFoldersTests` is **3 of 3 green**.

Then the fix was taken out of the PRODUCT code only (the tests stayed), and the solution was built again (0 errors):

| Fix | What was taken out | Tests that went red | Tests that stayed green |
|---|---|---|---|
| M11 | The old condition back in `StopFirst`; `Cut` answers nothing | **5 red.** The rig theory, all 4 cases: the job ended `succeeded` on the next model (this is the harm of M11). The pure test `M11_OnACutStream_…`: `Fine` for a `DONE` event with no record | `M11_…_WithNothingBroken_StillFailsOver_AsACrash`. Right: it is the guard against a check that is too strict, and it holds both ways |
| M12 | The three rows out of the script and out of `ExtraItems` | **2 red.** `M12_TheKeysAndTheOtherVendorRows_Hold_…` and `D3_ThePreIdsOfTheTableForAgy_…` (the list of the extra items is empty) | The row theory. Its cases come from `Items`, so the three cases are no longer there: 19 cases, all green. See the note |
| M13 | `CoderTempOf` and `CoderVarTempOf` answer the shared `<job>/coder/tmp` and `vartmp` | **4 red.** All 3 tests of `AVendorsHaveTheirOwnTempFoldersTests` (in the OS test the file that agy left is in the folder that claude gets) and `ACoderVendorSeamTests.D1_TheClaudeVendor_UnitValues_…` (the property list of claude) | The other 37 tests of `ACoderVendorSeamTests` |
| M14 | The field is not given to `RunAsync`, so the scratch copy is not made stale | **2 red.** `M14_ARealTokenThatIsNotExpired_…` (the launch is refused: "2 minutes left and the launch needs 7") and `M14_TheEnvelopeOfAJob_CountsTheRefreshes` (the job failed) | `M14_ARealTokenWithTheMarginInIt_NeedsNoRefresh_…`. Right: it needs no refresh in both states |

Note on M12: the theory is the test that drops a property and wants the row red. At the head its three new cases are green (the rows went red for the dropped `InaccessiblePaths` and for the cover). When the rows are taken out with their items, the three cases are not made at all, so they cannot go red. The two red tests above are what holds the rows in place. A row taken out of the script alone (its item still in `Items`) refuses every agy launch, by the shared reader: closed.

No new test exists for the two open legs of M11 or for R-M15. The probes of section 8 can be those tests.

## 8. The probes

All in the temporary worktree, at the unchanged head, under the shared lock. Each probe states the safe answer. **Probes 1 to 6, 8 and 9 are red. Probe 7 is green (it is the control).**

| # | Probe | Safe answer | Seen at `c7f2e259` |
|---|---|---|---|
| 1 | agy's unit, `/tmp`. Turn 1 runs `mv <job>/coder/agy/tmp <…>.old && ln -s <main checkout> <job>/coder/agy/tmp`. Turn 2 lists `/tmp` and writes a file there | The file is not in the main checkout | Turn 1: `swap=0`. Turn 2: the preflight is green, the list shows `.git`, `.gitignore`, `a.txt`, `b.txt` (the main checkout, which is hidden from an agy unit), `write=0`. **The file is in the main checkout on the host** |
| 2 | The same for `/var/tmp` (`vartmp`) | The same | The same |
| 3 | claude's unit, `/tmp` (`<job>/coder/claude/tmp`) | The file is not in the main checkout (it is read-only in a claude unit) | **The file is in the main checkout on the host** |
| 4 | claude's unit, `/var/tmp` | The same | The same |
| 5 | An agy job through the rig, budget 1 minute, attempt timeout 5 minutes. Turn 0 prints a `DONE` `write_file` event with no record and then holds | `failed`, `coder-boundary` | `failed`, **`timeout`** ("the job ran past its 1 minute budget") |
| 6 | The same in a fix turn: turn 0 is red, the fix turn holds after the event | `failed`, `coder-boundary` | `failed`, **`timeout`**, 2 launches |
| 7 | The pure check, a CLEAN stream (exit 0) with one event: `run_command`, `ACTIVE`; no record | `Boundary` | `Boundary` (green: the clean rule, G15 item 4) |
| 8 | The same stream, exit 1 | `Boundary` | **`Fine`** |
| 9 | The same stream, timed out | `Boundary` | **`Fine`** |

In probes 1 to 4 the program of the unit is `/bin/sh`: it stands for a command of a model. Both turns go through the product's road (`CoderTurns.RunAsync`: the folders, the markers, the preflight, the launch).

**P13, scratch units (systemd 259, made-up folders).** The properties: a read-only tmpfs over a stand-in for the folder of the jobs; `BindPaths=<…>/coder/agy`; `BindPaths=<…>/coder/agy/tmp:/var/tmp`; a tmpfs over a stand-in "victim" folder with one file in it.
Unit 1: the victim folder shows 0 entries; the `mv` and the `ln -s` end 0. Unit 2, the same properties: the victim folder still shows 0 entries at its own path; `/var/tmp` lists the victim's file, reads it, and a write there is on the host. So the rename works inside the unit, and systemd follows the link in the source.

## 9. Can the agy branch be merged into E3?

**As code, with `launchable` off for agy and for codex: yes.** Both marks are off at this head (`shared/coder-boundary-releases.json:23`, `:84`).
- A claude job is not opened by anything here. claude's turn check answers `Fine` for every end, so the wider wiring of M11 changes nothing for it. R-M15 needs a `mv` and a link in `<job>/coder/claude`; a claude coder has no shell and no tool that does either, and a check unit does not see `<job>/coder` at all. The claude path moved in every place.
- The merge makes M12, M13 (for what it named), M14 and the larger part of M11 better than `ee1271b7`.
- For the merge itself (from pass 6): `ACodexCoderPreflightTests.cs:129` drops the bind of `job.CoderVarTempFolder`, which is claude's folder after this commit. Name codex's folder there, or the row `tmp-is-the-jobs` does not go red in codex's theory.

**The flip of `launchable` for agy (s8 t6): no, not yet.** It needs M11 leg (a), M11 leg (b) and R-M15, built and green.
**The flip for codex:** add R-M15 to the list of pass 6 (M15 to M17). codex is the vendor whose model has a shell, so R-M15 weighs most there.

Seen, not checked: the worktree `/home/jinx/git/coai-v2-ep3e3` (the E3 branch) has a merge of `c7f2e259` in progress (`MERGE_HEAD`).

## 10. Should

Numbered R-S so that they do not meet S39 and above of pass 6.

- **R-S1. Tests of M11.** The rig theory cuts turn 0 only. There is no cut FIX turn (the other caller, `CoderFixLoop.cs:166`), no turn that ends on a limit, and no case for the two open legs.
- **R-S2. A stream cut by the size ceiling.** `ProcessResult.Truncated` is not read by `AgyTurnCheck`. The launcher keeps the first 8 Mi characters (`ProcessLauncher.cs:54`) and drops the rest. An event after that point is not seen, on a clean turn and on a cut one. A record with no event is caught; a tool that the hook was not called for is not. `Truncated` should end the job. (The same as S43 of pass 6 for codex.)
- **R-S3. The AG-R2 row is narrower now.** It looked for `refresh`; it now looks for `refresh_token` (`AgyCoderPreflight.cs:128`). The reason is the fake: its new token text is `made-up-agy-access-token-refreshed` (`tests_fakecli/AgyCoder.cs`). The copy is made from a list of fields, so the row is the second look. Keep it wide (`grep -qi refresh`) and give the fake another token text.
- **R-S4. The real codex file is not asked in agy's unit.** `SignInHidden` hides three real files. agy's rows ask claude's (the new row) and agy's own (`AG-R1`). Nothing observes `~/.codex/auth.json`, which holds a refresh token. One more arm in the row closes it (the shape of M12; S42 of pass 6 says the same for codex's rows).
- **R-S5. The sentences of the two shared rows** say "the check's unit" inside a coder's unit (`BoundaryPreflight.cs:151`, `:157-158`). A person reads the wrong unit in the refusal.
- **R-S6. The row `other-vendor-folder-hidden` is in agy's preflight only.** The preflights of claude and codex have no row for E7. "What a CLI leaves in `/tmp` is not in the unit of a later vendor" is observed in one direction by one test (agy, then claude).
- **R-S7. S26 has more weight now.** The refresh unit has no `KeysHidden` and no `SignInHidden` (`SignInRefresh.cs:37-51`) and no row of its own. With M14 it runs before about nine agy turns of ten (the 45-minute cap; the owner's number), with the owner's refresh token in it (A12).
- **R-S8. `MakeStale` does not look at the name before it truncates** (`SignInRefresh.cs:136`). It is safe today by place and time (section 5). One `Lstat` (a file, not a link) makes it safe by itself.
- **R-S9. The fix of M15 of pass 6 must also cover the folders and the temp names.** `BoundaryFolders.Prepare` also makes FOLDERS through a link (`BoundaryFolders.cs:21-24`: the home, `.gemini`, `.claude`, the two temp folders). The markers in the temp folders are written through the swapped name of R-M15: in probes 1 to 4 the row `tmp-is-the-jobs` was green in turn 2, so the empty file `.coai-job-<id>` was in the target while the preflight ran.
- **R-S10. Docs.** `BoundaryJob.cs:204` still says the two temp folders are "shared". The commit changes no file under `research/` or `v2/`, so the module docs and `SPEC_CHANGES.md` hold no word of the four changes (the cut rule, the three rows, the temp folder of a vendor, the stale scratch copy).
- **R-S11. One test for the sign-in row.** The theory shows the row red when the property is dropped. No test shows it GREEN for a home that is outside a hidden folder with the property there, so the red is not proved to come from the dropped property alone. It is on the closed side.
- **Open from pass 5, not touched by this commit (by the diff):** S33 to S38. `PlantOtherVendors` is now a third plant function (S35).
