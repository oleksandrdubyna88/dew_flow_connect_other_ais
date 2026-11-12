# PLAN — a vendor's error written to stdout reaches the Team server's job reason

> Status: **plan only, nothing implemented yet, 2026-11-12.** Scope: `src_server` job runner (the failure reason of a
> job whose vendor exits non-zero); no wire field changes, no client change.

## Symptom

On 2026-11-09 two deploys of server 0.15.0 (runs 42680645389 and 42682410067) were stopped by the host's canary with
`claude: failed — the vendor exited 1 without saying why`. The real cause was `OAuth session expired and could not be
refreshed` in the server's claude slot, and it had to be found by hand on the host. The `claude` CLI writes that error
to **stdout**, and the Team server's job reason is built from **stderr only**, so the one sentence that named the cause
was dropped. The slot has a one-year token now (`POST_DEPLOY.md`, the 2026-11-11 record), so this does not fail
anything today; it hides the reason the next time a vendor fails this way.

## Where it is

- `src_server/src/Jobs/JobRunner.cs:283` — `ReviewerOutcome.NonZeroExit exit => (FailureKind.NonZeroExit,
  Tail(exit.StdErrTail, exit.ExitCode))`: only `StdErrTail` is read.
- `src_server/src/Jobs/JobRunner.cs:392-393` — `Tail` returns `the vendor exited {exitCode} without saying why` when
  stderr is empty.
- `src_mcp/runners/Reviewers/ReviewerExecutor.cs:131` — `NonZeroExit(int ExitCode, string StdErrTail, string
  FailureReason = "")`: the outcome ALREADY carries `FailureReason`, "what the vendor said about its own failure
  somewhere OTHER than stderr" (doc comment at :118-129). The Team runner ignores it.
- `src_mcp/runners/Reviewers/ClaudeRuntime.cs:145-172` — the claude adapter fills `FailureReason` from the JSON
  envelope only when `is_error` is the boolean `true`.
- `src_mcp/runners/Reviewers/BoundedScheduler.cs:782` — the local path already uses both:
  `Because(e.StdErrTail, cap, e.FailureReason)`.
- `src_mcp/runners/Reviewers/VendorReason.cs:31` — `Written(reason)`: redaction and a cap for a vendor's text.
- `src_server/src/Jobs/ReviewEndpoints.cs:360` — `job.Reason` goes into the HTTP answer, so whatever reaches the reason
  is shown to the client.

## Decided against (consultant advice, 2026-11-12, verified against the lines above)

**A raw "last N bytes of stdout" fallback.** stdout of a review CLI can carry the reviewed code and the model's answer;
a length cap limits volume, not confidentiality, and the reason goes into the HTTP answer. Limiting it to claude does
not remove that. The reason must come from the adapter's parsed `FailureReason`, never from a stream tail.

## Unknown before the build

The exact shape of the 2026-11-09 OAuth error on stdout was not captured. If it is plain text OUTSIDE claude's JSON
envelope, `ClaudeRuntime`'s parser (which ignores plain text by design) will not produce a `FailureReason` for it. Step
1 settles this.

## Build order

1. Capture the real output: run the slot's `claude -p` with an expired or invalid `CLAUDE_CODE_OAUTH_TOKEN` in a
   throwaway HOME (never the live slot) and record stdout, stderr and the exit code as a test fixture.
2. Make the Team runner choose its reason the way `BoundedScheduler.Because` does: a meaningful stderr first, the
   adapter's `FailureReason` when stderr is empty or whitespace, the fixed sentence only when both are empty. Share the
   selection logic rather than copying it (reuse-first), and pass the result through `VendorReason.Written` —
   redaction before the cap.
3. Only if step 1 shows the error outside the envelope: teach `ClaudeRuntime` to recognise that one failure and map it
   to a FIXED sentence (for example "the claude sign-in expired"), not to the raw text.

## Test plan

Through the real launcher → runner → HTTP answer, with exit 1:

- empty stderr + the captured OAuth fixture → the reason names the sign-in failure;
- whitespace-only stderr → same as empty;
- a meaningful stderr AND a `FailureReason` → stderr wins;
- a successful review's text on stdout with exit 1 and no `is_error` → the reason is the fixed sentence, and no review
  text appears in the answer;
- malformed stdout → the fixed sentence, no crash;
- a secret-shaped string in the `FailureReason` → redacted, and redaction happens before the cap.

## Definition of Done

- [ ] The real stdout/stderr shape of an expired claude sign-in is captured as a fixture.
- [ ] A Team job that fails like 2026-11-09 says why, in the HTTP answer and in the canary line.
- [ ] No stdout content other than an adapter's parsed reason can reach `job.Reason`.
- [ ] All cases of the test plan pass on the MTP executables.
- [ ] `todo/README.md` row removed and this plan promoted to `research/` with its deviations.
