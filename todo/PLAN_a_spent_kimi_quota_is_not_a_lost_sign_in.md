# PLAN — a spent Kimi quota is not a lost sign-in

> Status: **plan only, nothing implemented yet, 2026-10-19.** It waits for the Kimi-on-Linux release (0.61.0); the owner's
> order is "release first, fix after". Scope: `src_mcp/runners/Acp/KimiAcpProfile.cs`, `src_mcp/runners/Kimi/KimiRuntime.cs`,
> `src_mcp/runners/Reviewers/VendorDiagnosis.cs`, `RateLimit` and `CooldownParser`, `BoundedScheduler`'s failure line, their tests,
> `research/module_runners.md`, `research/module_tests.md`.
>
> Related docs: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (the Kimi shim, M-K14),
> [PLAN_kimi_and_vibe_on_linux.md](PLAN_kimi_and_vibe_on_linux.md).

## 1. The symptom

On 2026-10-18 and 2026-10-19 every Windows Kimi reviewer of several gate rounds failed with:

> exit 77: Kimi is not signed in on this machine — run `kimi login` once in a terminal, as the user coai runs as, and try again

The owner checked on 2026-10-19. The owner's own `kimi` (2.2.0, K3-256k) was **signed in**; its usage panel showed the 5-hour plan
limit **100% used** and the monthly limit at 47%, and a plain prompt answered:

> Error: [provider.api_error] 429 You've reached your 5-hour usage limit. Your quota will reset …

So coai told the person to sign in again when the real state was a spent quota. Following that cure (logout and login) does
nothing for a quota, and an earlier session's notes even suspected a token-rotation fault (M-K14) partly on this evidence.

## 2. Why (read from the code on `origin/main`, 2026-10-19 — a scout's read, to be re-verified red-first)

- The shim matches Kimi's ACP failure **by text only**: `AcpDiagnosis.Of` (`src_mcp/runners/Acp/IAcpProfile.cs:93-96`) is an
  ordinal, case-blind substring match over the error text plus stderr, first row wins. Kimi's table
  (`KimiAcpProfile.cs:53-70`) has, in order: "Your current subscription does not have access to" → `subscription_model`;
  "Authentication required" → `not_signed_in` (Refused, exit 77); "insufficient balance" → `balance_spent` (Spent, exit 75).
- Kimi's ACP server puts the prefix "Authentication required:" on provider errors. The one recorded sample is a 401
  (`-32000 "Authentication required: 401 Your current subscription…"`, `research/RESULTS_kimi_confinement.md:75`). A 429 quota answer
  most likely arrives as `-32000 "Authentication required: 429 You've reached your 5-hour usage limit…"` and is filed as
  `not_signed_in`. **Not yet measured** — the first build step records it.
- `KimiRuntime.RateLimited` (`KimiRuntime.cs:278`) accepts only `balance_spent`, so even a correct Spent row for the quota would not
  reach the rate-limit path; and `RateLimit.Spent` (`ReviewerExecutor.cs:354`) lists "hit your usage limit", which Kimi's
  "reached your … usage limit" does not match, so it would be retried as transient (5/30/60/120 s).
- The raw vendor sentence is lost: `BoundedScheduler.Because` (`:816-819`) replaces a diagnosed stderr with the cure, and
  `DescribeWhole` (`:755`, used for the ledger and logs) calls it too; the shim's debug line (`KimiRuntime.cs:288-302`) leaves the
  reason out on purpose. Nobody could see the 429.
- Every Kimi lens of a round launches anyway: four launches per round each spend the same per-account quota and each fails the same way.

## 3. Decisions

- **D1 — A quota row before the sign-in row.** `KimiAcpProfile.Failures` gains `quota_spent` (Kind `Spent`, exit 75) for the
  phrase "usage limit", listed BEFORE "Authentication required". The sign-in row is unchanged: a bare
  `-32000 Authentication required` (an empty home, measured at RESULTS_kimi_confinement.md:263/413) stays `not_signed_in`.
  The phrase is the measured sentence of step 1, never "429" alone.
- **D2 — A spent quota is a hopeless rate limit for this round.** `KimiRuntime.RateLimited` / `SpentReason` accept every Spent
  failure, not only `balance_spent`, and the reason is `RateLimit.Hopeless` (no retry ladder inside a 5-hour window).
- **D3 — The sentence names the reset and never says "kimi login".** `VendorDiagnosis` gains the marker `quota_spent:` with:
  "the Kimi plan quota is used up — it resets <the vendor's own reset clause>; waiting is the only cure; signing in again
  will not help". The reset clause is lifted from the vendor text; when none is given, the sentence says so and guesses nothing.
  `CooldownParser` learns the measured reset form only after step 1 has recorded it ("every pattern is a line somebody saw").
- **D4 — The vendor's own words survive.** A diagnosed failure keeps the vendor's sentence after the cure in `Because` and
  `DescribeWhole` (bounded, redacted like every reason), so the next wrong diagnosis is visible at once.
- **D5 — One quota answer stands the provider's other lenses down for the round.** When a Kimi reviewer of a round comes back
  `Limited` with a hopeless reason, the not-yet-started reviewers of the same provider in that round end as `StoodDown`
  (`ReviewerExecutor.cs:254`) with the same sentence instead of launching. Never across rounds: a re-login or a reset can
  change the account (`RetryLadder.cs:86-88` keeps that on purpose). A separate story, shippable after D1–D4.
- **D6 — M-K14 is re-judged.** Every exit-77 since 2026-10-16 is re-read against this cause before the refresh-rotation
  suspicion is pursued further; the M-K14 record says which runs were a spent quota.

## 4. Scenarios (each red is the message before the fix)

- S1 `Of("the agent answered with an error (-32000): Authentication required: 429 You've reached your 5-hour usage limit. …")`
  → `quota_spent`, Spent. Red: `not_signed_in`.
- S2 The bare `-32000 Authentication required` and the 401 subscription sample keep their names (guards).
- S3 `VendorDiagnosis.For("quota_spent: … resets …")` → the D3 sentence with the reset clause and no "kimi login". Red: no marker.
- S4 A shim exit 75 with `failure: quota_spent` → `RateLimitVerdict.Limited`, hopeless. Red: only `balance_spent` accepted.
- S5 The reviewer's summary line reads "rate limited … quota … resets …" — not "exit 77", not "kimi login". Red: today's line.
- S6 A diagnosed failure's `DescribeWhole` still carries the vendor sentence. Red: the cure only.
- S7 (D5) Four Kimi lenses, the first comes back quota-spent → the other three are `StoodDown` with the sentence and never start.
  Red: four launches.

## 5. Build order

1. **Measure the real sentence** (free: a 429 is answered at once and spends nothing). Only with the owner's yes, and only when no
   other `kimi` process runs on that side (the Kimi cell rule): one `coai-mcp --ask-acp` turn on the spent account, its raw ACP
   error and stderr recorded in `research/RESULTS_kimi_confinement.md`. If the window has reset, wait for the next spent window
   or record what a gate round leaves — never spend quota on purpose.
2. D1–D4 with S1–S6, red-first.
3. D5 with S7, red-first.
4. Docs: `research/module_runners.md` (the Kimi failure table), `research/module_tests.md`; D6 in the M-K14 record.

## 6. Test plan

Unit tests over the profile table, `VendorDiagnosis`, `KimiRuntime` and the scheduler with recorded fixtures (the measured
sentence of step 1 committed as a fixture, no account data in it). Run as MTP executables, never `dotnet test`; Windows and
Linux CI.

## Definition of Done

- [ ] The real 429-over-ACP sentence recorded (step 1), or the plan says why it could not be.
- [ ] S1–S7 green, each seen red first; the existing Kimi suites green.
- [ ] A spent Kimi quota reads as a spent quota with its reset, in the round summary, the ledger and the log; never "kimi login".
- [ ] One quota answer stops the other Kimi lenses of the round.
- [ ] Docs updated; M-K14 re-judged; this plan promoted when shipped.
