# s7 key pool part 1 — security judgement (2026-11-25)

Judge: Fable (security judgement role), read-only, at commit cca029ee (base 85d4921e). Method: code reading, black-box runs with made-up tokens in scratch folders, and mutations in a scratch worktree.
`K/` = `src_mcp/src/Server/Keys/`, `R/` = `src_mcp/runners/`, `T/` = `src_mcp/tests/`.

**Verdict: not ready as it is; ready to merge into E2 after the 5 must-change items.**

## Must-change

1. **Env-source keys reach child processes.** `R/Processes/ProcessLauncher.cs:99` defaults `InheritsEnvironment = true`; `:425-436` filters only when false. `COAI_CLAUDE_KEY_*` therefore reaches gate reviewers (`R/Reviewers/ReviewerRuntime.cs:646` codex, `:836` gemini, `ClaudeRuntime.cs:89`), `cons` (`R/Consultation/CodexConsultant.cs:95`), git, and the role runner on the setsid/nohup roads (`R/Detach/DetachedLauncher.cs:89-90`). Breaks the owner's rule "never reach any child process". `T/AKeySourcesTests.cs:309-318` checks only the allow-lists.
   Fix: strip every `COAI_CLAUDE_*` name in `ApplyEnvironment` for ALL launches, and remove them from the process environment after `KeyStartup.Sync` (`src_mcp/src/Program.cs:2257`). Test with a real child and `InheritsEnvironment = true`.
2. **Shared mode starts from an inherited setgid bit.** `K/KeyFolder.cs:52-56`: a data folder made by plain `mkdir` under a 2755 parent gave `keys/` 2770 and `.key` 0660, wider than the data folder. `import`/`sync` print no warning (only `add`, `K/KeyVerbs.cs:75-78`).
   Fix: shared mode only with an explicit device setting (written by `--install --shared`) plus the bit; otherwise 0700/0600. Warn on import, sync and start too.
3. **`pool.lock` follows a link.** `K/KeySession.cs:99-111`, `K/KeyFolder.cs:143-149`: with `pool.lock` linked to a private 0600 file, `--keys list` changed it to 0660; a dangling link created a file at its target.
   Fix: refuse a lock path that is a link or not a regular file; set the mode through the open handle; test.
4. **A token in `account` is stored and printed.** Account pattern = any text without spaces (`K/KeySourceReader.cs:31-32`); the `lines` import does not check it (`K/KeyVerbs.cs:327`); `AddOne` has no account check (`K/KeyAdmin.cs:99-120`). The token appeared in `k-….json` and `list --json`.
   Fix: check in `AddOne`: must hold `@` and `Redaction.FirstSecretClass` must be empty. Sentinel tests for add, lines, env and creds-json.
5. **Flaky committed test.** `T/AKeyPoolTests.cs:247-262` failed 1 of 4 runs of the five key classes (line 255, an add refused as busy).
   Fix: move the synced write out of the lock, or a longer wait for admin verbs; must pass 20 of 20.

## Should-change

- Three mutations stayed green (145/145) — no real test for: the temp file made with the umask mode (`K/KeyFolder.cs:192` removed); the exact-text scrub removed (`K/KeyPool.cs:202`); the shared warning not printed (`K/KeyVerbs.cs:77`).
- `KeyResult<string>` is a record that holds the token and prints `Value` (`K/KeySession.cs:14-34`; used `K/KeyVerbs.cs:62,126,173`). Override `ToString`.
- The JSON check asks only the two key contexts (`T/AKeyStoreTests.cs:334`); ask every context of the assembly.
- Keys file (`K/KeySourceReader.cs:63-81`): follows a link, no owner check, check-then-open by path, a FIFO can hang the start. Use one handle, regular file, owner = this user.
- A start or `sync` without the variables disables every `env` key (`K/KeySourceSync.cs:26`, `K/KeyStartup.cs:18`). Sweep `env` only when at least one variable is present.
- The `.json` is not synced; after a power loss the sweep deletes the synced `.key` (`K/KeySession.cs:133-140`). Sync the add's `.json`, or keep the orphan.
- Core dumps hold env keys: set the process non-dumpable, or `LimitCORE=0` in the AMI unit rule.
- Windows: no key test runs there; `ModeProblem` always empty (`K/KeyFolder.cs:154-157`). One Windows CI leg before t5 ships.
- The "keys is a link" refusal (`K/KeyFolder.cs:101-104`) has no test; read verbs skip it.

## Leak paths

| Path | Closed in code | Closed by a test |
|---|---|---|
| Exceptions (`Program.cs:471-474`, `K/KeyStartup.cs:28-32`) | yes | yes |
| `ToString` of the secret and the input | yes | yes |
| Logs, notices, argv | yes | yes |
| Label on stdout/stderr | yes | yes |
| Scanner | yes | yes |
| Temp files | yes | no |
| Account | no (must 4) | no |
| Child environment | no (must 1) | no |
| Core dumps | no | no |

`/proc/<pid>/environ` of the server holds the env keys for the same user; the spec accepts that.

## Deviations (from research/module_server.md)

1. `_` in source labels: accept.
2. `sync` verb + sync at start: accept with a change (env sweep above, must 1).
3. Shared mode from setgid: **reject as built** (must 2); the AMI is one user.
4. Own writer instead of AtomicJson: accept (O_EXCL, 0600 from creation, rename); add the missing test.
5. Keys file refused on Windows: accept (fails closed).
6. Two notice codes deferred to t5: accept.
7. K5 split into threads + processes: accept with a change (must 5).
Also: unfair lock — accept after must 5; only `.key` synced — accept with the should-item; stub-made red runs — accept given the mutation result.

## Mutations planted by the judge (all went red, 13 tests)

Secret `ToString` gives the text; keys-file mode check off; `ModeProblem` off; label redaction off; `COAI_CLAUDE_` allowed for the runner; scanner follows links.

## Follow-ups (written todos, not code; added by the s7 part 2 session, 2026-11-25)

- Core dumps hold the env keys: set the process non-dumpable, or `LimitCORE=0` in the AMI unit rule. Not done.
- Windows: no key test runs there and `ModeProblem` is always empty; one Windows CI leg before t5 ships. Not done.
- The keys file has no owner check (the BCL offers none); a read-only FIFO named as the keys file is still opened for reading.
- The shared-instance setting is `COAI_KEYS_SHARED=1`; the `--install --shared` that should write it does not exist in this tree.
