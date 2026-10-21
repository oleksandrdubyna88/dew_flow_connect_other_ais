# PLAN — a spent Kimi quota is not a lost sign-in

> Status: **partly built, 2026-10-20: stories 1, 2, 3 and 4 are coded on `docs/kimi-quota-is-not-a-sign-in`; the capture step (§5 step 1) has NOT run, so the real ACP sentence is
> still unmeasured, and the plan is NOT promoted.** Deviations are in §8 (story 3). The 0.61.0 and 0.62.0 releases shipped (tags
> `extension-v0.61.0`, `extension-v0.62.0`), so the owner's "release first, fix after" block is lifted. The plan gate ran once
> (session def84902, `good_enough`, 11 findings, all accepted), an Opus judge read it (12 findings, all verified by the lead)
> and the question consultant was asked (consult 4eaa848c); all three are folded below and the plan is split into four stories
> (§8), one of them the cornerstone. Scope: `src_mcp/runners/Acp/KimiAcpProfile.cs`, `src_mcp/runners/Acp/IAcpProfile.cs`,
> `src_mcp/runners/Kimi/KimiRuntime.cs`, `src_mcp/runners/Reviewers/VendorDiagnosis.cs`,
> `src_mcp/runners/Reviewers/ReviewerExecutor.cs` (`RateLimit`), `src_mcp/runners/Reviewers/BoundedScheduler.cs` (`Because`,
> `DescribeWhole`), `src_mcp/core/Notices/Redaction.cs`, `src_vs_code/src/modelCardWorld.ts`, `src_vs_code/src/modelCard.ts`,
> their tests, `research/module_runners.md`, `research/module_extension.md`, `research/module_tests.md`.
>
> Related docs: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (the Kimi shim, M-K14),
> [PLAN_kimi_and_vibe_on_linux.md](../research/PLAN_kimi_and_vibe_on_linux.md),
> [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md) (the measured ACP sentences).

## 1. The symptom

On 2026-10-18 and 2026-10-19 every Windows Kimi reviewer of several gate rounds failed with:

> exit 77: Kimi is not signed in on this machine — run `kimi login` once in a terminal, as the user coai runs as, and try again

The owner checked on 2026-10-19. The owner's own `kimi` (2.2.0, K3-256k) was **signed in**; its usage panel showed the 5-hour plan
limit **100% used** and the monthly limit at 47%, and a plain prompt in the CLI answered:

> Error: [provider.api_error] 429 You've reached your 5-hour usage limit. Your quota will reset …

So coai told the person to sign in again when the real state was a spent quota. Following that cure (logout and login) does
nothing for a quota, and an earlier session's notes even suspected a token-rotation fault (M-K14) partly on this evidence.

**This CLI line is the baseline fixture of the plan** (gate finding 10). It is the one real quota sentence anybody has seen. How
the same answer looks when it comes over ACP is a hypothesis until step 1 of §5 records it.

## 2. Why (read from the code on `origin/main`, re-read 2026-10-20 — every line below was opened, not guessed)

- The shim matches Kimi's ACP failure **by text only**: `AcpDiagnosis.Of` (`src_mcp/runners/Acp/IAcpProfile.cs:93-96`) is an
  ordinal, case-blind substring match over the error text plus stderr, first row wins. Kimi's table
  (`src_mcp/runners/Acp/KimiAcpProfile.cs:53-69`) has, in order: "Your current subscription does not have access to" →
  `subscription_model` (:55-59); "Authentication required" → `not_signed_in` (:60-64, Refused, exit 77); "insufficient balance"
  → `balance_spent` (:65-69, Spent, exit 75 — `AcpWords.SpentExit`, `src_mcp/runners/Acp/AcpWords.cs:27`).
- **Hypothesis (constructed, not measured):** Kimi's ACP server puts the prefix "Authentication required:" on provider errors. The
  one recorded sample is a 401 (`-32000 "Authentication required: 401 Your current subscription…"`,
  `research/RESULTS_kimi_confinement.md:75`). If every provider error is prefixed the same way, a 429 quota answer arrives as
  `-32000 "Authentication required: 429 You've reached your 5-hour usage limit. Your quota will reset …"` and is filed as
  `not_signed_in`. Step 1 of §5 confirms or corrects this.
- **If the hypothesis holds, the existing `balance_spent` row is unreachable over ACP** (judge J1): "Authentication required:
  429 Your account … is suspended due to insufficient balance" contains the sign-in row's phrase, and the sign-in row (:60-64) is
  listed before the balance row (:65-69). A transient 429 and a 5xx with the same prefix also read "run `kimi login`". The
  defect is wider than one missing row: the sign-in row is too greedy.
- **`KimiRuntime.RateLimited` accepts only `balance_spent`** (`src_mcp/runners/Kimi/KimiRuntime.cs:278`), so even a correct Spent
  row for the quota would not reach the rate-limit path.
- **Corrected 2026-10-20 (judge J2):** the earlier draft said a quota would "be retried as transient" because `RateLimit.Spent`
  (`src_mcp/runners/Reviewers/ReviewerExecutor.cs:354`) lacks Kimi's phrase. That is wrong: `SpentReason`
  (`KimiRuntime.cs:304-309`) appends " (insufficient balance)" whenever `RateLimit.Hopeless` would say no, so Hopeless is always
  true for a Kimi Spent verdict. The real defect is the **false phrase**: a quota would be reported with words the vendor never said.
- **The raw vendor sentence is lost:** `BoundedScheduler.Because` (`src_mcp/runners/Reviewers/BoundedScheduler.cs:816-819`)
  replaces a diagnosed stderr with the cure alone, and `DescribeWhole` (:755-756, used for the ledger and the log) calls the same
  path; `VendorDiagnosis.Known` (`src_mcp/runners/Reviewers/VendorDiagnosis.cs:50-68`) is a table of fixed sentences; the shim's
  Debug line (`KimiRuntime.cs:288-302`) leaves the reason out on purpose. Nobody could see the 429.
- Every Kimi lens of a round launches anyway, one at a time (`BoundedScheduler.CapFor`/`LaneOf`, :194-203): four launches per
  round each hit the same per-sign-in quota and each fails the same way. (Stand-down is deferred — §3 D5.)

## 3. Decisions

- **D1 — Every Spent row before the sign-in row; the sign-in row stops being greedy** (judge J1, gate 11). `KimiAcpProfile.Failures`
  is reordered and extended:
  1. "Your current subscription does not have access to" → `subscription_model` (unchanged, still first: the 401 form).
  2. **new** "reached your 5-hour usage limit" → `kimi_quota_spent`, Kind `Spent`, exit 75. The phrase is taken from the owner's CLI
     line of §1 — narrower than a bare "usage limit" (which would also match the sales footer `RateLimit.Spent`'s remarks warn
     about, :346-352). The monthly form is **not measured** and gets no row until someone sees it; until then it lands in row 5.
  3. "insufficient balance" → `balance_spent` (unchanged text, moved above the sign-in rows).
  4. **new** "Authentication required: 401" → `not_signed_in` (the 401 form of a sign-in refusal keeps the login cure).
  5. **new** "Authentication required:" (with the colon) → `provider_error`, Kind `Failed`, exit 70 (`AskAcpMode.Failed`,
     `src_mcp/src/Acp/AskAcpMode.cs:50`) — the **neutral row**: it quotes the vendor's sentence and gives **no cure**. It has no
     `VendorDiagnosis` marker, so the vendor's own words stand (`AcpDiagnosis.None` / `Known == false`, `IAcpProfile.cs:83-86`).
  6. "Authentication required" → `not_signed_in` (the bare form, measured at `RESULTS_kimi_confinement.md:263` and `:413`).
  The colon is the discriminator between the bare sign-in and a prefixed provider error; the ordering is what makes substring
  matching safe. If a future Kimi writes the bare form with a colon, it lands in the neutral row and still gets no false cure.
- **D2 — A spent quota is a hopeless rate limit for this round, said honestly** (judge J2, J3).
  - `KimiRuntime.RateLimited` accepts exit 75 together with **any** failure name whose Kind is `Spent`. The set of names is
    derived from the profile table (`KimiAcpProfile.SpentNames` = the `Failures` rows with `AcpFailureKind.Spent`), never a second
    hand-kept list beside it.
  - `SpentReason` stops appending " (insufficient balance)" to a quota. Instead the observed phrase "reached your 5-hour usage
    limit" joins `RateLimit.Spent` (`ReviewerExecutor.cs:354`) and its verbatim sample — the owner's CLI line — joins the `observed`
    dictionary of `GateReportingTests` (`src_mcp/tests/GateReportingTests.cs:202-212`), the test that refuses a phrase with no
    vendor answer behind it. `Hopeless` is then true because the vendor said so, not because coai appended a phrase it did not.
  - **A Kimi quota is hopeless for the round even when a reset is stated.** Unlike agy (`RetryLadder.ResetOutlasts`,
    `src_mcp/runners/Reviewers/RetryLadder.cs:90-91`, which waits a reset inside the budget), Kimi's lane is one-at-a-time and
    shared by every Kimi row on the machine (`BoundedScheduler.cs:194-203`, `KimiRuntime.LaunchesOneAtATime`, :108). A Kimi
    reviewer waiting in that lane would block every round's Kimi. So no ladder step, whatever the clause says.
- **D3 — The sentence names the quota and the vendor's reset clause, and never says "kimi login"** (judge J4). `VendorDiagnosis`
  gains the Kimi-specific marker `kimi_quota_spent:` with the fixed sentence "the Kimi plan quota is used up — signing in again
  will not help". The vendor's reset clause cannot go into a fixed `VendorDiagnosis` row, so the **seam is `KimiRuntime.Said`**
  (`KimiRuntime.cs:260-266`, today `"{cure} (the shim said: {text})"`): for the `kimi_quota_spent` failure it places the vendor's reset
  clause, **quoted verbatim** from the shim's reason (the sentence that begins "Your quota will reset", redacted), between the cure
  and the "(the shim said: …)" tail: "the Kimi plan quota is used up — "Your quota will reset …"; signing in again will not
  help". When the reason has no such sentence, Said writes "the vendor named no reset" and guesses nothing. The clause is a
  quotation, **never parsed into a time**: `CooldownParser.StatedReset` (`src_mcp/runners/Reviewers/CooldownParser.cs:83`) and
  `Until` (:62) are not taught the form — D2 makes the time irrelevant for Kimi, and a parsed guess would only be wrong somewhere.
  No "waiting is the only cure": an upgrade also cures it.
- **D4 — The vendor's own words survive, redacted** (judge J5; consultants). A diagnosed failure keeps the vendor's sentence after
  the cure in `Because` and `DescribeWhole` — bounded by `VendorReason.SafetyCap` (4,000 characters,
  `src_mcp/runners/Reviewers/VendorReason.cs:22`) and written through `VendorReason.Written` (:31, the product's one redactor).
  `VendorReason` is reused, not re-made: the 160-character cut once lost a reset clause (:10), which is exactly the clause D3
  needs. The vendor part of the 160-character `Describe` summary passes the same redactor, after the cure. **Redaction gains a
  pattern for Moonshot account ids** (`org-…`, `ak-…`): the one measured balance sample carries both
  (`GateReportingTests.cs:209-211`, redacted by hand there), and `Redaction.VendorPattern` (`src_mcp/core/Notices/Redaction.cs:83-84`)
  lists OpenAI's, GitHub's and Slack's token prefixes and nothing of Moonshot's. Without the pattern D4 would write
  an account id into the ledger that the 160-character cut used to hide by accident. One pattern, one test.
- **D5 — DROPPED from this plan (decision G, judge J6+J7).** The earlier draft stood the provider's other Kimi lenses down for the
  round after one quota answer. Two verified facts remove it: a 429 on a spent quota is answered at once and spends no quota, so
  the stand-down would save seconds and noise only; and `StoodDown` is the wrong outcome for a feature second round —
  `FeatureSecondRound.FailedInRoundOne` (`src_mcp/src/Server/Stages/FeatureSecondRound.cs:74`) retries only `Failed` and names the
  rest "answered in round 1" (:65-68). **Recorded as a possible follow-up with three constraints**, so whoever builds it does not
  rediscover them: (1) it must be keyed by the **sign-in**, not the provider id — two Kimi rows with different ids share one quota
  (`BoundedScheduler.LaneOf`, :197-203, keys the lane by the adapter's type for the same reason); (2) security lenses are
  excluded from stand-down today (`BoundedScheduler.cs:257`) and would stay excluded; (3) its outcome must be one a feature second
  round retries, which `StoodDown` is not. The precedents to copy are Kimi's one-at-a-time lane and the local engine's stand-down
  re-check after taking the slot (`EngineLaneAsync`, :260) — cited here only as the reason the work is separable.
- **D6 — M-K14 is annotated, not re-judged** (gate 5; judge J10). Every historical exit-77 is **unclassifiable**: the raw ACP text
  was never kept (§2), and no `kimi-code.log` survives — the lead searched `%TEMP%`'s coai homes on 2026-10-20; the homes are
  removed after the round. The M-K14 record (`research/module_runners.md`, `research/RESULTS_kimi_confinement.md:36,561,568`)
  gets one note: the 2026-10-18/19 exit-77s coincided with a 100 %-used 5-hour quota and may have been quota answers; the
  refresh-rotation suspicion is neither confirmed nor cleared by them.
  **Annotated 2026-10-20 (story 3)**, in these places: `RESULTS_kimi_confinement.md` (the summary line and the end of §25), `research/module_runners.md` (the residual sentence of the Kimi home
  section) and `PLAN_vibe_and_kimi_runtimes.md` (the M-K14 risk, one line pointing here). Each carries the same facts; none re-judges M-K14.
- **D7 — Check on a Kimi (or any non-consultant) row is disabled, with a plain title; no paid-turn prompt** (gate 2, 3, 6, 7; judge
  J8+J9). Seen 2026-10-19: the owner pressed **Check** on the Kimi card after the 0.61.0 Update and got "The model-kimi consultant's
  check ended as unavailable — the vendor 'kimi' runs on 'kimi', which cannot hold a consultation in this build — consultants run
  on: codex, claude, antigravity, local, api, vibe." The server already refuses such a row by name before any lock or launch
  (`ConsultantCheckMode.CheckRowAsync`, `src_mcp/src/Server/Consultation/ConsultantCheckMode.cs:210-221`, through
  `ConsultantResolution.CannotConsult`, `src_mcp/runners/Consultation/ConsultantResolution.cs:86-90`). The defect is the
  **extension's**: `modelCard.ts:149` renders `checkButton` for every row, and `checkButton` (`src_vs_code/src/modelCardWorld.ts:91-93`)
  promises "One real, paid turn of this model in a scratch folder — you are asked first" whatever the runtime. **The one behaviour
  chosen:** for every runtime outside the consultant list (`shared/feature-availability.json:25` — today that is `kimi`, `gemini`
  and `remote` rows; the extension reads it through the generated `src/featureAvailability.generated.ts`), the Check button is
  rendered **disabled** with the title "the consultant check is not offered for <runtime> — this runtime cannot hold a consultation"; it never asks for a
  paid turn and never sends `checkModel`. A real reviewer-shaped check (one short review turn through `KimiRuntime`/`KimiLaunch`,
  so it takes the cross-process gate at `src_mcp/runners/Kimi/KimiLaunch.cs:134`, with a short deadline, asked first) is a
  **named follow-up**, not this plan.

## 4. Scenarios (each red is the message before the fix)

- **S1** `AcpDiagnosis.Of("the agent answered with an error (-32000): Authentication required: 429 You've reached your 5-hour
  usage limit. Your quota will reset …", KimiAcpProfile.Failures)` → `kimi_quota_spent`, Spent. Red: `not_signed_in`. **The envelope
  is constructed** (judge J12): the inner sentence is the owner's CLI line, the "Authentication required: " wrapper is the §2
  hypothesis. When step 1 of §5 records the real line, the fixture is replaced and this scenario re-run.
- **S2 guards:** the bare `-32000 Authentication required` → `not_signed_in`; the 401 subscription sample
  (`RESULTS_kimi_confinement.md:75`) → `subscription_model`; a constructed "Authentication required: 401 <anything else>" →
  `not_signed_in`; a constructed "Authentication required: 503 …" → `provider_error` with the vendor text and no cure; the
  balance sample wrapped the same way → `balance_spent`. Red: the 503 and the wrapped balance sample both read `not_signed_in`.
- **S3** `VendorDiagnosis.For("kimi_quota_spent: …")` → the D3 sentence, no "kimi login". Red: no marker (`null`).
- **S3b** `KimiRuntime.Said` on a `kimi_quota_spent` report whose reason carries "Your quota will reset at 14:00 UTC." → the cure, then
  the quoted clause, then "(the shim said: …)"; the same report without a reset sentence → "the vendor named no reset". Red: the
  cure alone, no clause.
- **S4** A shim exit 75 with `failure: kimi_quota_spent` → `RateLimitVerdict.Limited`, hopeless. Red: `RateLimitVerdict.No` (only
  `balance_spent` accepted).
- **S4b** `RateLimit.Hopeless("kimi_quota_spent: … You've reached your 5-hour usage limit …")` is true **without** any appended phrase,
  and `GateReportingTests`' `observed` dictionary carries the owner's line for "reached your 5-hour usage limit". Red:
  `Hopeless` false on the bare text; the phrase-list test refuses the new phrase as "a guess".
- **S5** The reviewer's summary line reads "rate limited … the Kimi plan quota is used up … "Your quota will reset …"" — not
  "exit 77", not "kimi login". Red: today's line.
- **S6** A diagnosed failure's `DescribeWhole` carries the vendor sentence after the cure, whole up to 4,000 characters. Red: the
  cure only.
- **S6b** `Redaction.SafeText` on the measured balance sample with `org-abc123…` and `ak-xyz…` present → both ids redacted; the
  same ids inside `DescribeWhole`'s output → redacted. Red: the ids pass through.
- **S7 — the product-level scenario** (gate 4): one round with a Kimi reviewer whose shim answers the S1 fixture, driven through
  `BoundedScheduler` with a fake adapter; asserted together: the round summary line (S5), the ledger line (`DescribeWhole`) and
  the log line each say "quota is used up", each quote the reset clause, none says "kimi login" or "exit 77", and the reviewer
  ends `RateLimited`, hopeless, after **one** launch. Red: today's three lines.
- **S8** (D7, extension node tests) A rendered Kimi card and a rendered Gemini card each show `✓ Check` **disabled** with the title
  "the consultant check is not offered for kimi — this runtime cannot hold a consultation" (resp. gemini) and no `data-command="checkModel"`; a Codex card
  is unchanged. Red: both cards render the enabled button with "One real, paid turn … you are asked first".
- **S9 — the outcome where D1 never fires** (judge J10): if the real ACP answer to a spent quota is a **bare** "Authentication
  required" with no 429 text, S1 cannot be made green by any row; then `not_signed_in` is what the vendor said, D4's kept sentence
  (S6) is the only place the truth can show, and the plan records the finding in `RESULTS_kimi_confinement.md` and stops at D4 +
  D7. This outcome is named so nobody "fixes" it by widening the sign-in row's cure.

## 5. Build order

1. **The capture step — measure the real ACP sentence** (judge J10, J11; gate 8). No free evidence exists: a 429 on a spent quota
   is answered at once and spends no quota, but every probe needs a spent account at the moment of the probe.
   - **Tried 2026-10-19 with the owner's yes, deferred.** The probe ran `kimi acp` **directly** (not through the shim), which takes
     **no** `KimiCredentialsLock` — the server gate at `KimiLaunch.cs:134` — so coai could not wait for it; and a `coai-mcp
     --ask-acp` turn on its own takes only the shim's **tree hold** (`--locks`, `src_mcp/src/Acp/AskAcpMode.cs:66,146-148`), not
     the server gate either. Another session's gate rounds kept a `kimi.exe` running (PIDs 50920, then 51284); a free gap lasted
     seconds, and starting a probe in a gap risks two refreshers of one sign-in (M-K14's own risk).
   - **So the measurement is D4.** Once story 2 ships, the first real quota answer in a gate round is written whole into the
     ledger and the server log. **Who:** the lead of the session that owns the round (or the owner, who sees the card). **Where:**
     the round's ledger line and the coai-mcp server log for that reviewer (`DescribeWhole`'s output). **How it becomes the
     fixture:** the line is copied verbatim, account ids redacted (`org-…`, `ak-…` → `REDACTED`), into
     `research/RESULTS_kimi_confinement.md` (a new section "the quota answer over ACP") and replaces S1's constructed envelope in
     `KimiAcpProfileTests` and the `observed` row in `GateReportingTests`. If the line shows S9's outcome, that is recorded instead.
   - Never spend quota on purpose; never run a probe while any coai session launches Kimi.
   - **The step as written (story 3, 2026-10-20) — a procedure, never executed by a coder; nobody runs `kimi` for it.** The full text is `research/RESULTS_kimi_confinement.md` §31, with an
     empty "The record" table to fill. In short:
     - **When:** the first gate round after stories 1 and 2 ship in a released coai-mcp, in which a Kimi reviewer meets a spent quota. **Who:** the lead of the session that owns that round; if the
       owner sees it first on the card or in the Rounds block, the owner tells that lead, and the lead does the copy.
     - **Which lines:** the ledger line of that launch (`usage.jsonl` in the data directory of the side that ran the round, field `outcome`) and the log line `reviewer kimi/<role> FAILED after …s: …`
       (`<data dir>/<side>/logs/<UTC day>/`); both are `DescribeWhole`. Copy the text after ` — the vendor said: ` with the tool.
     - **The real Moonshot id shape is NOT measured, so the first real line is checked against the `moonshot-id` pattern** (`org-`/`ak-` + 8–128 letters and digits with at least one digit; `Redaction.cs` and its
       twin in `src_vs_code/src/notifications.ts`): an id that is still readable in the ledger or the log line was not matched — record only its SHAPE (prefix, length, character classes), never the id; if the real
       id has no digit or has `_`/`-`, widen the pattern in both halves with a test in each, before anything else. Whatever is left is replaced by hand with `org-REDACTED` / `<ak-REDACTED>`.
       **A real id or any credential content is never written into the repository** (fixture, record, commit message or plan).
     - **How it becomes the fixture:** §31 gets the record (date, Kimi version, round id, the line); the real inner text replaces the constructed envelope in `KimiAcpProfileTests` and `PromptError` in
       `ASpentKimiQuotaReadsAsAQuotaTests`, and goes in the `observed` row of `GateReportingTests` for `reached your 5-hour usage limit`; the Kimi profile tests, `GateReportingTests` and S7 are re-run.
     - **If the line is S9's outcome** (a bare `Authentication required`, no 429 text): record that in §31, do NOT widen the sign-in cure, and say here that D1's quota row never fires over ACP.
     - **Promotion** waits for this record (§7 item 1). Until then the status line says: stories 1–4 coded, capture pending.
2. **Story 1** (cornerstone): D1, D2, D3 with S1–S4b, S9's guard, red-first.
3. **Story 2:** D4 with S6, S6b, red-first; then the capture step's first chance.
4. **Story 3:** S7 (the product-level scenario, S5 inside it), the docs, D6's annotation.
5. **Story 4:** D7 with S8, red-first, in the extension.

Stories 1 and 2 are independent of each other; 3 needs both; 4 needs none of them.

## 6. Test plan

- Unit tests over the profile table (`src_mcp/tests/KimiAcpProfileTests.cs`), `VendorDiagnosis`, `KimiRuntime`, `RateLimit`
  (`GateReportingTests`), `Redaction` and the scheduler's `DescribeWhole`, with the fixtures of §4 (no account data in any of
  them). Run as MTP executables, never `dotnet test`; Windows and Linux CI.
- One product-level scenario test (S7) through `BoundedScheduler` with a fake `IReviewerRuntime`, asserting the summary, the ledger
  and the log together.
- Extension node tests (`src_vs_code/src/test/*.test.ts`, run by `npm test`) rendering a Kimi, a Gemini and a Codex card (S8).
- Each test is seen red first with the message named in §4; the summary reports the red and the green.

## 7. Definition of Done

- [ ] The real quota-over-ACP sentence is recorded by the **capture step** of §5 (who, where, how), with the fixture replaced — or
      the plan records S9's outcome. **Required before promotion.**
- [ ] S1–S9 green, each seen red first with the message named in §4; the existing Kimi suites green.
- [ ] A spent Kimi quota reads as a spent quota with the vendor's reset clause in the round summary, the ledger and the log (S7);
      never "kimi login"; a 5xx behind the same prefix reads as the vendor's words with no cure.
- [ ] `RateLimit.Spent` holds Kimi's phrase with its verbatim sample; no "(insufficient balance)" is appended to a quota.
- [ ] Moonshot account ids are redacted wherever a vendor sentence is written.
- [ ] Check on a Kimi or Gemini card is disabled with the "not offered — this runtime cannot hold a consultation" title; no paid-turn prompt (S8).
- [ ] D5 is recorded as a follow-up with its three constraints; D6's annotation is in the M-K14 record.
- [ ] Docs updated (`module_runners.md`'s Kimi failure table and rate-limit path, `module_extension.md`'s card, `module_tests.md`);
      this plan promoted when shipped.

## 8. Stories (the split — 2026-10-20)

Four stories, no epics, one cornerstone. The consultant (consult 4eaa848c, both rows) proposed classification+wording+no-retry,
evidence retention, round stand-down and the Check button; decision G drops the stand-down, and the product-level scenario the gate
asked for takes its place as the story that proves the first two together. The gate runs **once** over the whole diff.

Model roles (the operator's): **Fable** specs the cornerstone; **Opus** writes the other stories' scenarios and briefs and reviews
every story; **Sonnet** codes.

### Story 1 — CORNERSTONE: the right name, said honestly (D1, D2, D3)

- **Why the cornerstone:** this is the rule every other story renders. A wrong row hides a real sign-out behind a quota sentence
  or sells a 5xx as "run `kimi login`"; a false Hopeless phrase puts words in the vendor's mouth. If this story is wrong, stories
  2 and 3 faithfully record a wrong diagnosis.
- **Owns:** D1, D2, D3; scenarios S1, S2, S3, S3b, S4, S4b, S9 (the guard).
- **Files:** `src_mcp/runners/Acp/KimiAcpProfile.cs` (the table, `SpentNames`), `src_mcp/runners/Acp/IAcpProfile.cs` (only if the
  neutral row needs a `Kind` or an exit it lacks), `src_mcp/runners/Kimi/KimiRuntime.cs` (`RateLimited`, `SpentReason`, `Said`),
  `src_mcp/runners/Reviewers/VendorDiagnosis.cs` (the `kimi_quota_spent:` row), `src_mcp/runners/Reviewers/ReviewerExecutor.cs:354`
  (`RateLimit.Spent`); tests `src_mcp/tests/KimiAcpProfileTests.cs`, `src_mcp/tests/GateReportingTests.cs`, the `KimiRuntime`
  tests (`src_mcp/tests/AKimiShimScenarioTests.cs` or a new `KimiQuotaIsNotASignInTests.cs`).
- **Red-first, expected reds:** S1 → `not_signed_in`; S2's 503 and wrapped-balance cases → `not_signed_in`; S3 → `null`; S3b →
  the cure alone; S4 → `RateLimitVerdict.No`; S4b → `Hopeless` false and "a phrase with no vendor answer behind it is the guess
  this list exists to keep out".
- **Done when:** the six rows stand in D1's order with the measured guards green; `RateLimited` accepts every Spent name derived
  from the table; no "(insufficient balance)" on a quota; the sentence quotes the reset and never says "kimi login";
  `CooldownParser` untouched.

### Story 2 — the vendor's words survive, redacted (D4)

- **Owns:** D4; scenarios S6, S6b; the capture step of §5 (its first chance to fire).
- **Files:** `src_mcp/runners/Reviewers/BoundedScheduler.cs` (`Because` :811-819, `DescribeWhole` :755-756, `Describe`'s vendor
  part), `src_mcp/runners/Reviewers/VendorReason.cs` (reused as is), `src_mcp/core/Notices/Redaction.cs` (the Moonshot id
  pattern beside `VendorPattern` :83-84); tests: the scheduler's describe tests, `src_mcp/tests/GateReportingTests.cs`, the
  `Redaction` tests (`src_mcp/tests/` — the redaction suite that already feeds an `Authorization: Bearer …` line).
- **Red-first, expected reds:** S6 → the cure only; S6b → `org-abc123…` and `ak-xyz…` pass through `SafeText` unchanged.
- **Done when:** a diagnosed failure's ledger and log lines carry the vendor sentence after the cure, whole to 4,000 characters
  and redacted; Moonshot ids never reach a line; the 160-character summary's vendor part passes the same redactor.

### Story 3 — one round proves it, and the record says so (S7, docs, D6)

- **Owns:** S7 (S5 inside it); D6's annotation; the module docs; the plan's deviations line.
- **Files:** a new scenario test beside the scheduler's (`src_mcp/tests/ASpentKimiQuotaReadsAsAQuotaTests.cs`), driving
  `BoundedScheduler` with a fake `IReviewerRuntime` that answers the S1 fixture; `research/module_runners.md` (the Kimi failure
  table, the rate-limit path, the M-K14 note), `research/module_tests.md`, `research/RESULTS_kimi_confinement.md` (the capture
  step's section, when it has fired).
- **Red-first, expected reds:** S7 against stories 1+2 unmerged → the three lines read "exit 77 … kimi login"; against story 1
  alone → the summary is right and the ledger lacks the clause.
- **Done when:** S7 is green on the merged tree; the docs describe the six rows, the derived Spent set and the kept sentence;
  the M-K14 record carries D6's note; the plan is promoted only after the capture step (DoD item 1).
- **Status and deviations (2026-10-20, coded; capture pending):**
  - **Built:** `src_mcp/tests/ASpentKimiQuotaReadsAsAQuotaTests.cs` (both S7 tests); the docs in `module_runners.md`, `module_tests.md` and `module_core.md`; D6's note in all four places named
    in D6 (`RESULTS_kimi_confinement.md` summary and §25, `module_runners.md` at the residual sentence, `PLAN_vibe_and_kimi_runtimes.md` at the M-K14 risk); the capture step written into §5 and
    `RESULTS_kimi_confinement.md` §31 (empty record). The help sentence in the five languages was story 2's.
  - **Deviation 1 — S7 found an integration bug the unit tests of stories 1 and 2 could not, and a production line changed.** Every line said the cure TWICE: story 1's `KimiRuntime` reason for a Spent
    failure led with the cure (`{cure} (the shim said: …)`), and story 2's `Because` then diagnosed that reason by its marker and put the table's cure in front of "the vendor said: {the whole reason}".
    S7's red was `Expected line "… quota is used up - signing in again will not help - the vendor said: the Kimi plan quota is used up - "Your quota will reset at 14:00 UTC."; signing in again will not help (the
    shim said: kimi_quota_spent: …" to contain "the Kimi plan quota is used up" exactly 1 time, but found it 2 times` — in the summary, the ledger and the log. **The fix:** `KimiRuntime.SpentReason` returns the shim's
    own words (`name: reason`) with no cure; the cure comes from the table, once, in `Because`. Two assertions of story 1 (`KimiQuotaIsNotASignInTests`) that pinned the old reason were updated.
    **Consequence (first version, superseded by K1 below):** the quoted reset clause (`KimiQuotaWords.Said`, D3) then led only the `WhyItFailed` road; on the rate-limit road the clause was in the vendor's line, whole in the ledger and the log, and in the
    160-character summary only while the vendor's sentence was short (the fixture's is 165 characters with its name, and keeps `Your quota will reset at 14:00 U…`). If the real sentence is longer, the capture step shows it.
  - **Deviation 2 — the same doubling existed on the NonZeroExit road. FIXED 2026-10-20 (follow-up commit on the same branch).** The shim's stderr line is `<name>: <reason> — <cure>`, and `Because` quoted all of it after its own cure,
    so an exit-77 line said `kimi login` twice in the summary, the ledger and the log. Red: `Expected line "… exit 77: Kimi is not signed in … — the vendor said: not_signed_in: … Authentication required — run kimi login in a terminal and try again" to contain "kimi login" exactly 1 time … but found it 2 times`.
    Fix: `AcpDiagnosis.Note` is the one place the note's shape is written and `AcpDiagnosis.WithoutCure` reads it back; `Because` quotes `<name>: <reason>` (the vendor's words whole) behind the table's cure. Test: `TheCureIsSaidOnceAndTheIdIsHiddenOnEveryRoadTests`.
  - **Deviation 3 — an undiagnosed vendor line was cut without redaction, and a tagged one reached the summary whole. FIXED 2026-10-20 (same follow-up).** `Quote` (the road of every undiagnosed line, `provider_error` included) cut a plain line at
    160 characters with no redaction, so `org-0a1b` could show, and returned a shim-tagged line whole and unredacted, so a whole id could show in the summary. Rule now: redact BEFORE any cut. `Quote` runs `VendorReason.Written` and then `Cut`;
    a tagged line is redacted whole. Red: `Did not expect line "… exit 1: Error: wwww… org-0a1." to contain "org-0"`, and the whole id in the `provider_error` summary. Not changed: the reasons of `NotStarted`, `StoodDown` and `Unparseable`, which are adapter-composed and not cut.

### Story 4 — the Check button tells the truth (D7)

- **Owns:** D7; scenario S8.
- **Files:** `src_vs_code/src/modelCardWorld.ts:90-94` (`checkButton` takes the row's runtime and the consultant list),
  `src_vs_code/src/modelCard.ts:149` (passes the vendor), the generated `src_vs_code/src/featureAvailability.generated.ts` (read,
  not edited — `shared/feature-availability.json:25` is the source); tests: a new `src_vs_code/src/test/theCheckButtonIsNotOfferedOnAReviewerOnlyRow.test.ts`
  beside `aModelsCardPricesItsOwnRow.test.ts`.
- **Red-first, expected reds:** S8 → the Kimi and Gemini cards render an enabled `✓ Check` with `data-command="checkModel"` and
  the title "One real, paid turn of this model in a scratch folder — you are asked first".
- **Done when:** Kimi, Gemini and remote rows render the disabled button with "the consultant check is not offered for <runtime>
  — this runtime cannot hold a consultation"; Codex, Claude, antigravity, local, api and Vibe rows are unchanged; `module_extension.md` names the rule and
  the reviewer-shaped check as a follow-up. No server change.
- **Status and deviation (2026-10-20, coded on `feat/kimi-quota-s4`, merged into this branch; title changed twice, 2026-10-21 the second time):** the plan's first draft said "— ▶ runs a review". That was false: ▶ on a card opens the vendor's CLI in a terminal, it does not run a review. The first fix said "— this runtime cannot hold a consultation". That was false too: a row can be configured for document review (`modelCard.ts`), so a Kimi or Gemini row does review documents. CodeRabbit on PR #900 found it. The title now says only what is true of the button: "the consultant check is not offered for <runtime> — this runtime cannot hold a consultation" (it is a fact about the runtime, read from the generated consultant list, and it claims nothing about what the row reviews). The wording lives in one place (`checkNotOffered`, `src_vs_code/src/modelCardWorld.ts`); the test pins it for kimi, gemini and remote rows, and every case of that test RUNS the page (`runPageHtml`) and presses the button there, instead of reading parsed or helper-built HTML.

## Review record (2026-10-20)

### The plan gate (session def84902, verdict `good_enough`, 11 findings, all ACCEPTED)

| # | Finding | How it was folded |
|---|---|---|
| 1 | D5 must state its scheduling guarantee | **Superseded** by decision G: D5 is dropped; its three constraints are recorded in §3. |
| 2 | D7 into the build order | §5 step 5, story 4. |
| 3 | D7 into the DoD (S1–S8) | §7, S8. |
| 4 | A product-level scenario proving the summary, the ledger and the log | S7, story 3. |
| 5 | D6 reduced to an annotation — historical exit-77s are unclassifiable | §3 D6: the raw text was never kept; one note in the M-K14 record. |
| 6 | D7's files in scope | Status line and story 4: `modelCardWorld.ts`, `modelCard.ts`. |
| 7 | D7: ONE behaviour chosen | The disabled button with the plain title; the reviewer-shaped check is a named follow-up. |
| 8 | The DoD requires the real sentence recorded before promotion, with a named capture step | §5 step 1 (who, where, how); §7 item 1. |
| 9 | The status line says 0.61.0/0.62.0 shipped and the block is lifted | Status line (tags verified). |
| 10 | Red-first uses the owner's CLI-observed line as the baseline fixture; the ACP envelope is a constructed hypothesis | §1, §2 bullet 2, S1. |
| 11 | The match is narrower than a bare "usage limit" | D1 row 2: "reached your 5-hour usage limit"; the monthly form waits for a sighting. |

### The Opus judge (12 findings, each verified by the lead against `origin/main`, re-verified here)

| # | Finding | Verified at | Folded as |
|---|---|---|---|
| J1 | BLOCKING: the sign-in row is greedy; `balance_spent` unreachable over ACP; a 429/5xx reads "run kimi login" | `KimiAcpProfile.cs:60-69`, `IAcpProfile.cs:93-96` | D1: Spent rows first, the 401 form keeps the cure, a neutral row for other statuses, the bare form last. |
| J2 | §2 bullet 3 wrong: `SpentReason` makes Hopeless always true; the defect is the false phrase | `KimiRuntime.cs:304-309`, `ReviewerExecutor.cs:354,370-371` | §2 corrected; D2: the observed phrase + sample join `RateLimit.Spent`/`GateReportingTests`; Spent names derived from the profile. |
| J3 | State why a stated reset is still hopeless for Kimi | `BoundedScheduler.cs:194-203`, `KimiRuntime.cs:108`, `RetryLadder.cs:90-91` | D2, third bullet. |
| J4 | D3 needs a seam for the reset clause; marker Kimi-specific; no "waiting is the only cure"; quoted, never parsed | `VendorDiagnosis.cs:50-68`, `KimiRuntime.cs:260-266`, `CooldownParser.cs:62,83` | D3: `kimi_quota_spent:`, the clause placed by `Said`, `CooldownParser` untouched. |
| J5 | D4 can leak Moonshot ids; `Redaction` has no pattern | `GateReportingTests.cs:209-211`, `Redaction.cs:83-84` | D4: one pattern, one test; the summary's vendor part redacted too. |
| J6 | A spent-quota 429 is answered at once and spends nothing — D5 saves little | the owner's CLI observation | Decision G, D5 dropped. |
| J7 | `StoodDown` is wrong for a feature second round; keyed by provider while the quota is per sign-in; security excluded | `FeatureSecondRound.cs:65-68,74`, `BoundedScheduler.cs:197-203,257` | Decision G; the three constraints in D5. |
| J8 | D7 is an extension change; the server already refuses | `modelCard.ts:149`, `modelCardWorld.ts:91-93`, `ConsultantCheckMode.cs:210-221` (the lead cited :200-210; that range is the caller-kind preflight, the row refusal is `CheckRowAsync` at :210-221 — same substance) | D7, story 4. |
| J9 | Disabled button with the plain title for runtimes outside the consultant list; node tests for kimi and gemini | `shared/feature-availability.json:25` | D7, S8. |
| J10 | No free evidence exists; measurement = D4; name the bare-"Authentication required" outcome | the lead's `%TEMP%` search 2026-10-20 | §5 step 1 (the capture step), S9. |
| J11 | Step 1 must say which launch path was tried and what each lock covers | `KimiLaunch.cs:134`, `AskAcpMode.cs:66,146-148` | §5 step 1, first bullet. |
| J12 | S1's wrapping is constructed, not measured; extension node tests for S8 | — | S1's note, §6. |

### The consultants (consult 4eaa848c, both rows)

- Four stories proposed: classification+wording+no-retry; evidence retention; round stand-down; the Check button. Taken, with the
  stand-down replaced by the product-level scenario story (decision G).
- Precedents named: `BoundedScheduler`'s one-at-a-time Kimi lane and the local engine's stand-down re-check after taking the slot
  (`EngineLaneAsync`). Verified (:194-203, :260); cited in D5 only as the reason the stand-down is separable, not as work here.
- `VendorReason` keeps 4,000 redacted characters because the 160-character cut once lost a reset clause (`VendorReason.cs:10,22,31`).
  Verified; D4 reuses it instead of adding a bound of its own.

### Refuted or corrected (reviews are input, not orders)

- **The plan's own §2 bullet 3** ("retried as transient") was wrong — judge J2 — and is corrected above.
- **The plan's D5** is dropped on the judge's two verified facts, over the gate's finding 1, which asked only that its guarantee be
  stated. A guarantee for a mechanism with the wrong key and the wrong outcome is not worth writing; the constraints are.
- **The plan's D3 wanted `CooldownParser` to learn the reset form.** Refuted by J3+J4 together: the time is irrelevant for Kimi
  (hopeless either way) and the clause is a quotation. `CooldownParser` leaves the scope.
- **The plan's D7 wanted a reviewer-shaped check through the shim.** Narrowed to the disabled button; the real check is a follow-up
  because it needs the cross-process gate and a deadline of its own — more than a card change.
- **One line citation of the lead's** (`ConsultantCheckMode.cs:200-210`) pointed at the consultant-kind preflight; the row refusal
  is `CheckRowAsync` at :210-221. Substance unchanged, lines corrected in J8's row.

## Code gate and judge (2026-10-20)

The coai code gate (session def84902, verdict `proceed`) and an Opus judge (no blockers) read the whole diff. Findings are input, not orders: each below is accepted or rejected with its reason; every accepted behaviour fix was
seen red first (the messages are in `research/module_tests.md`, "Code gate and judge fixes").

**Accepted and fixed (K1–K7):**
- **K1 (judge 1, MAJOR) — the reset clause (D3) was lost from the round summary on the real road.** A spent quota is exit 75 → `RateLimited`, which returns before `WhyItFailed`, so `KimiQuotaWords.Said` never ran for a real quota, and the
  160-character summary cut the vendor's line before "Your quota will reset". S7 asserted only a prefix of the clause, which is why it stayed green. Red: `… to contain ""Your quota will reset at 14:00 UTC.""`. Fix: `VendorDiagnosis.CureOn` returns
  `KimiQuotaWords.Said(text)` for the quota marker, so the cure — never cut — carries the clause; the cure is still said once. `KimiRuntime.Cured` (the dead road) is removed. This supersedes deviation 1's consequence.
- **K2 — the vendor-neutral reviewers layer named one vendor's table.** `BoundedScheduler.VendorSaid` stripped the shim's cure with `KimiAcpProfile.Failures`. Now `AcpProfiles.WithoutCure` goes through the profile catalog (`AcpProfiles.All`); a second profile needs a row there and nothing in `BoundedScheduler`. Red: the catalog API did not exist (CS0117).
- **K3 — a `provider_error` line reached the summary whole, with our tag.** A shim-tagged note whose row has no cure is the vendor's text: `AcpDiagnosis.CurelessReason` takes the tag and the failure name off, then it is redacted and cut at 160 (4,000 in the ledger and the log). Red: summary 730 characters, showing `[coai-mcp] provider_error:`.
- **K4 — reason and stderr were diagnosed in ONE first-match pass.** A bare sign-in reason plus any stderr line holding "Authentication required:" became `provider_error` (no `kimi login` cure). Now the reason is matched first, stderr only when the reason names no row. Red: `provider_error` for `not_signed_in`.
- **K5 — `error.data` was dropped.** A bare "Authentication required" with the 429 in `data` read as S9's lost sign-in. Now `data` is kept in the reason (`message (data: …)`, a string as it is, other JSON compact, redacted, capped at 2,000). Red: exit 77, `not_signed_in`, for a quota. RESULTS §31 says so.
- **K6 — the `moonshot-id` shape also refuses a consultant context (`SecretCheck`) and is applied to served files.** Kept (an account id is a personal identifier; refusing to send it to an internet row is the conservative choice), and pinned: two `SecretCheckTests` rows, `org-settings-panel` stays clean, `module_core.md` says where it applies.
- **K7 — a comment in `theCheckButtonIsNotOfferedOnAReviewerOnlyRow.test.ts` cited `research/PLAN_…`; the plan is in `todo/`.** Path fixed; comment only, no test.

**Rejected (with the reason):**
- German help text: localisation is by design, not a defect of this change.
- Widening the id pattern: its real shape is not measured; the capture step (§5 step 1, RESULTS §31) is where that is decided, with a test in each half.
- A literal instead of `KimiAcpProfile.QuotaSpent`: the constant is the one name shared by the table, the shim and the diagnosis row — a literal would be a second copy.
- Performance of `FirstLineWith` and of `Classify`: both run once per failed reviewer over a short stderr tail; measured nothing, so nothing is changed.
