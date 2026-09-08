# PLAN — the shim scenario's wait is tuned to the fastest runner

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/tests/RemoteShimScenarioTests.cs`.
>
> Related docs: [module_tests.md](../research/module_tests.md),
> [PLAN_a_release_is_visible_before_it_is_whole.md](PLAN_a_release_is_visible_before_it_is_whole.md)
> — the release this cost.

## The symptom, measured

`mcp-v0.18.13`, 2026-09-08 17:35 UTC. The `win-arm64` leg of the release matrix failed:

```
failed CoaiMcp.Tests.RemoteShimScenarioTests.AShimKilledMidClaim_LeavesEitherNothingOrAWholeClaim_NeverHalf (30s 359ms)
  Xunit.MicrosoftTestingPlatform.XunitException: System.TimeoutException : the condition was still false after 30s
total: 1091, failed: 1, succeeded: 1090
```

Five of six legs passed the same test. The build before it was `0 Warning(s), 0 Error(s)`, so this
is a WAIT that ran out, not a binary that was wrong — and it cost the release a platform: no
`coai-mcp-0.18.13-win-arm64.zip` was ever published, and every Windows ARM install of that version
answers 404.

## What the test is doing, and why 30 s is the wrong number

`RemoteShimScenarioTests.cs:371` launches the REAL `coai-mcp --ask-remote` in its own process and
waits up to 30 seconds for it to create its claim file (or the `.writing` sibling), then kills it
mid-write to prove the claim is whole or absent and never half.

Thirty seconds is generous for a process start — on the runner the test was written on. It is spent
by: a cold .NET start on an ARM runner, a sign-in, an HTTP round trip to a loopback `HttpListener`,
and the first write — immediately after a full Release build of the whole solution, on a machine
whose disk and CPU are already the slowest of the six. The number was chosen once and has never been
measured against the runner it fails on.

**The deeper problem is that the wait is a fixed constant in a scenario about a RACE.** The test
exists to kill the shim at a precise moment; how long the shim needs to reach that moment is a
property of the machine, and a constant cannot be right for six of them.

## The change

Three parts, cheapest first:

1. **Raise the bound and say what it is for.** The wait is not measuring performance — it is waiting
   for a prerequisite before the interesting part begins. A prerequisite deadline should be generous
   (120 s) and should FAIL with the reason: what it was waiting for, how long it waited, and whether
   the process is still alive. The current message names none of the three.
2. **Say whether the child is still running.** `the condition was still false after 30s` and `the
   process died before it got there` are different failures with different cures, and the test
   cannot currently tell them apart — which is why this one has to be diagnosed from a release log.
3. **Separate the phases' budgets**, per the family testing rule: the wait for the process to reach
   the claim, and the assertions after the kill, must not share one token. A slow start currently
   eats the budget of the thing being tested.

## Test plan

This is a test fixing a test, so the honest checks are:

| # | Check | Holds |
|---|---|---|
| 1 | The failing scenario passes on win-arm64 | Verified by a release re-run rather than asserted locally — the machine is the variable |
| 2 | `WaitForAsync`'s failure names what it waited for and whether the child lived | Read the message from a deliberately-failing wait in a unit test |
| 3 | No other test in the file shares a token across phases | A grep-shaped assertion over the file, with a known instance to prove the scan matches |

## Definition of Done

- [ ] The prerequisite wait is generous, named, and reports whether the child process is alive.
- [ ] A re-run of the `win-arm64` leg passes.
- [ ] `research/module_tests.md` records that a scenario driving a real child process bounds its
      SETUP separately from its assertion.
