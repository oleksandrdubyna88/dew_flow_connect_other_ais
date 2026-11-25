# A waiting marker has its own sentence at the session door

> Status: TODO, not planned. Found 2026-11-25 by the review of PR #1047 (CodeRabbit thread T8, accepted by the judge as a
> plan change, not a fix in E1).

## The finding

While a refusal waits behind a held lock (a marker, or a change held in this window), the keeper answers
`consent(host)` with `damaged` for every host that the waiting change does not name (`waitingConsent`). The session road
(`remoteRead.ts`, the `damaged` door) then says W6: *"The list of hosts read over ssh cannot be read, so nothing was
read. Run CoAI: hosts read over ssh and answer for a host there: your answer repairs the list."* The list is readable;
nothing needs repairing. Answering for another host does not clear the pending refusal (A1-T24), so the person is sent
to a cure that does not work.

## Why it is a plan change, not a fix

W6 is a decided word of the S1 spec (§ S1.8, the table of words), and the keeper's `Consent` has four values, `yes`,
`no`, `ask` and `damaged`, which the spec fixes. A fifth sentence needs either a fifth consent value or a second read
of the keeper by the road, and a new word W-number with its own test (A1-T14 compares every word with the spec).

## What a fix needs

- A new word: *a “Not this host” or a stop is not saved yet on this side, so nothing was read. Choose “Save now” in the
  notice, or the first row of the accounts list.* (W12 already says it for the list's row.)
- The road tells `waiting` from `damaged` (a new `Consent` value or `keeper.held() > 0`), and the door answers with it.
- Tests: a held refusal for `h`, a press on `g` → the new sentence, never W6; a damaged list → W6 as today.
- The S1 spec's table and the plan's ledger.
