# s7 key pool part 3 — security judgement (2026-11-25, head 9392d443)

Judge: Fable (security judgement role), read-only, short pass over cb82b7ee..9392d443. Method: code read; 10 mutations in a scratch worktree (baseline 223/223 green in the `AKey*` classes): 8 red, 2 green; one hand probe of the inner shell with a made-up line. No real CLI, no real key.
`R/` = `src_mcp/runners/`, `K/` = `src_mcp/src/Server/Keys/`, `J/` = `src_mcp/src/Server/RoleJobs/`.

**Verdict: ready to merge s7 into E2.** All 5 part-2 must-change items are closed. No new must-change.

## Items

1. Key not in the unit: closed. `R/Toolchain/CoderBoundary.cs:44-50,161-162` moves the key from the environment to stdin line 1; `R/Toolchain/CoderUnit.cs:125` reads and exports it. Mutation (key stays in the environment): K11 and K12 red; K12 printed the live `Environment=` property and the transient file text, so it reads the real unit. Rest of stdin intact (probe with dash; empty stdin or no newline gives exit 70). Nothing reads stdin before the shell (`ExecStart` is `/bin/sh` directly; `env | sed` does not read fd 0). The transient file has no `StandardInput=` line; `--pipe` passes the pipe itself; the journal does not get stdin. `StdIn` is used only at `R/Processes/ProcessLauncher.cs:516` (plus a length at `src/Server/RoundAudit.cs:67`). A key text cannot hold a newline (`K/KeyIdentity.cs:40`).
2. Foreign names: closed. `R/Consultation/ClaudeConsultant.cs:242-252` sets 29 names to null; mutation (`ANTHROPIC_BASE_URL` dropped) → K16 red. Coder road: closed by construction — only non-null explicit names and 10 proxy/certificate names cross (`CoderUnit.cs:27-28,95-96`); the shell unsets the rest.
3. Search never fails open: closed. `J/CoderFixLoop.cs:207-213`; mutation (in-memory secret not used) → M3 red.
4. Search before and after `git add`: closed. `J/CoderCommits.cs:29-32,46,69-131`; mutation (no search before add) → `S_TheWorkingTreeIsSearchedBeforeGitAdd` red; mutation (no staged search) → M4 red. Fail-closed branches correct by reading; `--text` also overrides a `binary` attribute.
5. Crowd test: closed, plausible. 12/12 green at load 20–26, each run 7–30 s; a hidden deadlock would show as 60 s or more. Still a 15 ms poll for 60 s (`K/KeySession.cs:64,97`), not a kernel-blocking lock; a busy pool still fails closed.

Glance items: exposed is final (mutation red, P2; ids are a 48-bit SHA-256 prefix, never text, `K/KeyIdentity.cs:33`); `LimitCORE=0` (mutation red, 2 tests); keys file hidden (mutation red).

## Should-change (not blocking E2)

- An exposed mark can be lost: `K/KeyPool.cs:227` records it with the 2 s wait and `J/RoleKeyRoad.cs:255` drops the result; with a busy lock the key stays enabled and the id is not written (by reading). Fix before the first real key: long wait, notice on failure.
- Two mutations stayed green: the refusal for a keyed session with no secret (`CoderFixLoop.cs:210`, unreachable today: all three session constructors carry the secret); the key left in the `systemd-run` client's own environment with no `--setenv`.
- No test for a search git cannot make; the per-blob fallback has only the parser test (recorded follow-up).
- No test that the prompt arrives whole after the key line (only the hand probe).
- K16 asserts 15 of the 29 names; the read-only road is a deny-list over the whole inherited environment. Proxy and CA names cross on both roads (accepted as the person's own network setting).
- Outside s7: a row's own `ANTHROPIC_API_KEY` still travels by `--setenv`, with the same unit-property and 0644-file exposure.
