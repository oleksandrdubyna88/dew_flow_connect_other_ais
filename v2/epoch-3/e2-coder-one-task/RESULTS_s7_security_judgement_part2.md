# s7 key pool part 2 — security judgement (2026-11-25, cca029ee..cb82b7ee)

Judge: Fable (security judgement role), read-only. Method: code read at cb82b7ee; 18 mutations in a scratch worktree (baseline 207/207 green): 15 red, 3 green; three probes with made-up values (systemd 259, git). No real CLI call and no real key.
`R/` = `src_mcp/runners/`, `K/` = `src_mcp/src/Server/Keys/`, `J/` = `src_mcp/src/Server/RoleJobs/`.

**Verdict: not ready as it is; ready to merge into E2 after the 5 must-change items.**

## 1. Part-1 items

- 1 (child environment): closed (strip removed → 2 red; start removal removed → 1 red). Gap: the real default unset (`K/KeyStartup.cs:40`) made a no-op stays green. On Windows the runner inherits the server environment (`R/Detach/WindowsDetach.cs:87`), so that line is the only guard there.
- 2, 3, 4: closed (mutations red: 2, 2, 3 tests).
- 5: not proven. The crowd test passed 19 of 20; one run failed at `tests/AKeyPoolTests.cs:255` (four adds answered 75 after the 10 s wait), machine load 21, series under nice.

## Must-change

1. **The key is in the unit's properties and in a 0644 file.** `R/Toolchain/CoderUnit.cs:136` passes `--setenv=CLAUDE_CODE_OAUTH_TOKEN`. Measured with a made-up value: not in an argv, not in the journal, but in `systemctl --user show <unit> -p Environment` and in `/run/user/<uid>/systemd/transient/<unit>.service` (mode 0644, folder 0700) while the unit lives. Breaks "no file other than its .key". Comments at `CoderUnit.cs:16` and `R/Toolchain/BoundaryUnit.cs:132` are true for argv only.
   Fix: send the key as the first line of stdin; the inner shell reads and exports it before the `exec`. Measured: property empty, transient file 0 hits, the child has the variable, the rest of stdin arrives. Extend K12 to read the property and the transient file.
2. **`ANTHROPIC_BASE_URL` stays in a keyed launch.** `R/Consultation/ClaudeConsultant.cs:242` removes two names only; a read-only launch inherits the whole environment (`R/Processes/ProcessLauncher.cs:99,427`); the CLI would send its token to that host (from knowledge of the CLI, not measured).
   Fix: remove `ANTHROPIC_BASE_URL` and the Bedrock and Vertex switches; assert in K16.
3. **The commit search fails open.** `J/CoderFixLoop.cs:205-206` asks `J/RoleKeyRoad.cs:258`, which gives null when the pool is busy for 2 s or the key is dead/removed (`K/KeyPool.cs:197-212`); `J/CoderCommits.cs:38` then commits with no search.
   Fix: keep the session's `ClaudeKeySecret` in memory, or refuse the commit.
4. **The search reads the working tree, not what git commits.** `J/CoderCommits.cs:59-63`. Measured: a `.gitattributes` line `working-tree-encoding=UTF-16LE` gives 0 hits in the file while the committed blob holds the exact text.
   Fix: search the staged blobs and the name list.
5. **The crowd test still fails under load.** Fix: a blocking lock, or a longer wait in the test's own adds.

## Should-change

- Three mutations stayed green: the read-only role's scrub (`J/RoleModelLauncher.cs:99`), `WithoutCredential` (`:116-119`), the real unset (`K/KeyStartup.cs:40`).
- Dead (exposed) is not final: `K/KeyAdmin.cs:171-173` `enable` revives it; remove + add or a start sync makes a new enabled record. Refuse `enable` for an exposed key and keep a list of exposed ids.
- Residue after key-in-output: the file stays in the worktree; `git add -A` (`J/CoderCommits.cs:26`) has already written the blob; the CLI's own transcripts in `coder/home` and `attempts/<n>/claude-home` (`J/RoleModelLauncher.cs:81`) are never searched. Search before `git add`; search each keyed launch's home and temp folder after it ends.
- The scrub is exact text of one key (`J/RoleModelLauncher.cs:141-171`): partial, base64 or split-line copies pass, and so does another pool key. It does run before anything is stored. Scrub with every pool secret and the `sk-ant-oat` pattern.
- An unreadable pool reads as empty: with every `k-*.json` unreadable (`K/KeySession.cs:179-190`, `K/KeyPool.cs:137-140`) and the switch set, the launch uses the person's sign-in although key files exist. Answer `Failed`.
- `Resume(claude, "")` gives `None` (`J/RoleKeyRoad.cs:116-118`) = the person's sign-in. No caller does this today. Answer `Gone`.
- A keys file outside `/home` can be read inside the coder's unit; only `<data>/keys` is hidden (`R/Toolchain/CoderUnit.cs:44-45`).
- Core dumps: add `LimitCORE=0` to `R/Toolchain/BoundaryUnit.cs:70-79` and to the runner unit.

## Deviations

1. Mixed route with an empty pool starts and skips claude: accept (without the switch there is no sign-in launch, `J/RoleKeyRoad.cs:132-145`).
2. Auth-failed = the "Not logged in" envelope (`J/RoleStops.cs:123-126`): accept with a change — on a keyed launch that text more probably means the CLI did not get the key, so one good key would die per job; a revoked key probably answers 401, which is not read. Do not mark the key dead on this signal until the real fixture exists.
3. Test rigs set the switch: accept (three K15 tests cover the default; the mutation went red).

The switch is safe: `Fresh` never gives `None` for claude; cooling keys and a failed pool both give `Skip`; both callers of `LaunchAsync` pass a key (`J/RoleFailover.cs:409`, `J/CoderFixLoop.cs:154`).

## Key switch

Each attempt has its own folder; a unit cannot read the environment of a host process (measured: permission denied); one job's coder home is shared across its key switches (matters only with the residue item); dead (exposed) is not final (should list).

## Follow-ups

- Not before merge: keys-file owner and FIFO; `--install --shared` (fails closed).
- Core dumps: two properties now are cheap (should list).
- Windows: before any release, not before E2. Until the CI leg exists, only the untested `K/KeyStartup.cs:40` keeps the env keys from the runner there.
- New, before the first real key: one run with the real CLI and a throwaway key, then `--keys scan` over the job folder and both homes (FakeCli cannot show whether the CLI writes the token to its config folder). Needs the owner.
