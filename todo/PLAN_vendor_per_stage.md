# PLAN — a vendor can be chosen per STAGE, from the panel

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/vendors.ts`,
> `settingsShape.ts` (the env block), the panel's Reviewers section, and on the server
> `PanelSettings.ProviderSettings` plus wherever a round picks its providers.
>
> Related: [RESULTS_vendor_overlap_2026-09-06.md](../research/RESULTS_vendor_overlap_2026-09-06.md) —
> the measurement that asks for this; [PLAN_settings_per_side.md](PLAN_settings_per_side.md), which
> this must compose with.

## Why — measured, not guessed

The 0.18.0 matrix, judged by `claude-opus-5` over fourteen runs:

| provider | plan stage | code stage |
|---|---|---|
| `codex` | 17 / 35 useful (49 %) | 13 / 40 (33 %) |
| `gemini` | 17 / 28 (61 %) | 9 / 24 (38 %) |
| `local` | 9 / 48 (19 %) | **2 / 70 (3 %)** |

`local` writes more than codex and gemini together, and two of its seventy code-stage findings were
worth having. On a PLAN it is a fifth useful — a real contribution at no marginal cost. On CODE it is
a 3 % signal that a person pays for in reading time.

So the useful setting is not "local on or off". It is **on for the plan, off for the code** — and
today that cannot be expressed anywhere: `ProviderSettings.Enabled` is one boolean, in the extension
and on the server. (An earlier version of the RESULTS document claimed this switch already existed.
It does not; the claim is corrected there.)

## What must be true when this is done

1. Each vendor can be enabled for the **plan** stage, the **code** stage, both, or neither, from the
   panel's Reviewers section — no JSON editing.
2. The default for an existing configuration is **both**, so nobody's gate changes shape on update.
3. `enabled` stays the master switch: off means off everywhere, and the two stage boxes are then
   irrelevant rather than contradictory.
4. A stage with no enabled vendor is refused with a sentence that says so — never a round that runs
   with zero reviewers and reports `proceed`.
5. **It composes with the per-side split**: the flags live inside `coai.vendors`, which is in
   `OVERLAID_SETTINGS`, so one WSL distro can run local on plans while another does not. This is an
   explicit requirement from the operator, not a side effect to be discovered later.
6. The server is told per stage, and the round asks for the stage it is actually running.

## The shape

**Extension.** `Vendor` gains `plan: boolean` and `code: boolean` (default true, absent = true), read
through `vendorsFrom` like every other field so a malformed value falls back rather than throwing.
The Reviewers row gets two checkboxes beside the vendor's existing switch.

**The env block.** One variable per vendor, listing the stages it serves — `COAI_<VENDOR>_STAGES=plan`
— written only when it differs from both, so a pristine configuration still produces no env at all
(the rule the block already follows).

**Server.** `ProviderSettings` gains `Stages` (a set, defaulting to both). Every place that filters by
`Enabled` takes the stage it is running: the reviewer launch, the "how many reviewers will answer"
count that the panel shows, and the refusal in point 4.

## Build order

1. `vendorsFrom` reads the two flags with their defaults; tests for absent, malformed, and both-false.
2. The env block emits `COAI_<VENDOR>_STAGES` only when it differs from both; a test on a pristine
   configuration asserting the block is still empty.
3. `PanelSettings` parses it; `ProviderSettings.Serves(stage)`.
4. The launch path and the reviewer count take a stage. RED first: a code round with only `local`
   enabled-for-plan must refuse, and today it would run.
5. The panel's Reviewers section gains the two boxes and the sentence under them says what the
   measurement says, in one line, so the person is choosing with the number in front of them.
6. `research/module_server.md` + `module_extension.md`; the manifest's `coai.vendors` description.

## Test plan

- A vendor with no flags in settings serves both stages (the update path).
- `enabled: false` beats any stage flag.
- A code round with no vendor serving `code` is refused, with the stage named.
- The env block stays empty for a pristine configuration and carries exactly one variable for a
  vendor restricted to plans.
- With the per-side switch on, two sides hold different stage flags for the same vendor.

## Definition of Done

- [ ] Two checkboxes per vendor in the panel; no JSON editing to express "plan only".
- [ ] Existing configurations serve both stages after the update.
- [ ] A stage with no reviewer is refused, not run empty.
- [ ] The flags are per side when the per-side switch is on.
- [ ] Tests above pass; module docs and the manifest description updated.
