# Security lane calibration — Windows Ollama

Status: the initial live campaign exposed a response-contract defect; the corrected twelve-module
campaign is pending. No quality claim is made yet.

## Prediction recorded before the run

On the small synthetic invoice-search regression, the SQL prompt is expected to identify lost
parameterization and lost tenant authorization in each of three repetitions. The authorization prompt
is expected to identify at least the tenant authorization regression. Any unrelated claim needs
manual checking. No numerical quality score will be reported without reading every finding.

## Pinned conditions

The operator selected `Qwen3.5-35B-A3B-Q5_vk128:latest` from Windows Ollama at
`http://localhost:11434`. The installed model reports `num_ctx 131072`. The harness explicitly
requests a 131072-token context budget, 8192 output tokens, one local request at a time and an
eight-minute reviewer timeout. It makes three real `PanelService.ReviewCodeAsync` calls, each with
the SQL and authorization Git-authored default prompts. Only the paid ordinary reviewer is replaced with a clean
`FakeCli` response, isolating the incremental findings from the local lane.

Harness: `src_mcp/tests/SecurityLaneCalibrationTests.cs`. It creates a committed synthetic Git
fixture, records both commit SHAs, fixture hash and embedded prompt hashes, saves each composed
request and answer immediately, and retains the complete session/history and round response.
It never executes the fixture or model-produced reproduction instructions. The isolated fixture
session starts with plan review already satisfied; this is not a bypass used by a product session.

Run on Windows after building the test project:

```powershell
$env:COAI_SECURITY_CALIBRATION_OUT = 'D:/chosen-output/security-lane-qwen'
& src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --explicit only --filter-class '*SecurityLaneCalibrationTests' --timeout 60m
```

Follow the host GPU lease policy when launching the command, or record an explicit operator
override. The artifact directory is operator-owned; retain the measured campaign and remove
superseded exploratory runs after their results have been recorded.

## Observations

CPU-only validation on Windows, 2026-10-01: the full Release MCP suite passed 6395 tests with
7 explicit/platform skips; the subsequent source-selection correction passed its 104-test focused
Debug suite. The extension's full suite passed 4825 tests with 2 skips, followed by 42 passing
settings/page tests for the validation warnings. Windows Native AOT publication passed, and the
released MCP 0.40.3 compatibility check confirmed the new setting is withheld, an accidentally sent
unknown key is inert, and the new binary accepts it. These checks do not measure model quality.

The first committed-code gate returned `proceed` with 5/8 reviewers answering (three Gemini service/
response failures). Its eight findings were resolved; a configured Claude/Opus consultation confirmed
the detector-limit routing gap. The correction passed 109 focused tests with two explicit GPU tests
skipped. Removing the correction reproduced three failures; restoring it passed all five targeted
cases. The 54-request Team HTTP contract suite passed. The follow-up code gate and final feature
gate remain separate from this local-model campaign.

The operator also requested an audit of the committed implementation through **each of the twelve
modules**, followed by a COAI consultation over the findings. `SecurityLaneAuditTests` runs those
cells sequentially and retains failures without abandoning the remaining modules. It uses the real
security lane with a synthetic clean ordinary reviewer, so it does not replace the code or feature
gate. Set `COAI_SECURITY_AUDIT_REPO`, `COAI_SECURITY_AUDIT_BASE` and `COAI_SECURITY_AUDIT_OUT`, then
run that explicit test through the GPU lease wrapper. Every module must reach the local shim;
a trigger skip cannot count as a completed model review. The ordinary three-repeat calibration
above remains a separate measurement.

Initial feature-only campaign on `ff833afce42a832148b3a5b358481d49c3722bec`, 2026-10-01:
authorization and SQL both reached Windows Ollama, using 124040/123465 input and 1331/2083 output
tokens respectively. Both returned prose `notes`, which the derived security schema allowed but the
security validator refused. Neither was a usable security answer. The campaign was stopped after
those two completed cells (the third was cancelled), retaining artifacts under
`D:/rsd/_wt/security-lane-tools/qwen-twelve-ff833afc`. The schema contradiction was reproduced by a
failing test and corrected without changing the ordinary schema or the operator's prompt bodies.
The focused security suite then passed 58 tests, with the two explicit hardware tests skipped.

The authorization output alleged application defects from the prompt's own examples; no such
application implementation was identified. The SQL output speculated about cap order, coverage
counting and `ExecuteRawAsync` routing; inspection showed cap-before-gate, explicit incomplete local
coverage and a case-insensitive `execute` match already present. Claude/Opus consultation
`df969a82a9c34a76ae5b6040bcddacbb` confirmed the schema contradiction and that deduplicated lane
evidence survives in `AlsoSeenBy`. The shared framing now requires implementation evidence for
claims about documentation, prompts or examples. Those files remain reviewable: excluding an
instruction asset from its own audit could hide a defect in this feature. The campaign uses `slice`,
so the consultant's proposed change to `diff` ordering would not address this observed failure.

The operator subsequently supplied an explicit source-only / prompt-immunity and JSON-hygiene block
for all thirteen `redteam-*.md` files (twelve presets plus the custom general prompt), and requested
matching source start/end labels around the MCP's nonce fence. Before restarting the matrix, one
selected module must answer the same feature request successfully three consecutive times. Each
answer is checked by hand for grounded, readable findings and by the real product validator for
JSON compliance. The harness now fails a cell if the local reply is unusable; merely reaching Ollama
does not count as success. `COAI_SECURITY_AUDIT_PROMPTS` selects the comma-separated module IDs for
this preflight and the remaining matrix. Every run retains its pinned manifest and raw answers.

The first source-boundary preflight (`5f3fe8734149902573d8b18d31e0da81bb8a9828`, base
`1056aed99d968ce04a2f12dbe29461b2ee6aa5d4`) returned schema-valid JSON, 122962 input and 1585 output
tokens, with all five reviewers answering. Its three authz claims were not adequate: the cap finding
described the intended behavior with equivalent expected/actual outcomes; merge evidence remained in
`AlsoSeenBy`; timeout/partial coverage was already reported. It therefore counts as **zero successful
quality repetitions**, independent of a harness assertion that incorrectly expected the summary
wording `5 of 5` instead of `all 5` (corrected). Raw evidence is retained under
`D:/rsd/_wt/security-lane-tools/qwen-boundary-preflight/attempt-1`.

Consultation `fc975f1c8e914679b53a1fb5134b398b` verified all three contradictions and the selection
problem: keyword-rich documentation/prompts/tests could consume all sixteen source slots. Two RED
tests reproduced it in the source reader and the packer; both now prioritize production/config
paths before labelled supporting material. The next series uses the existing 24000-token local
slice default; the model window remains 131072. `COAI_SECURITY_AUDIT_CONTEXT_TOKENS` records an
explicit override. These preparation changes are qualified together, not claimed as an isolated
causal comparison against the earlier 128K-budget attempt. The focused suite passed 142 tests with
two hardware skips; the rebased extension passed 4847 tests with two skips and clean lint after its
generated prompt help was synchronized.

No initial cells have been counted as successful. Token-usage plausibility cannot establish
full local input coverage; that remains unverified even if all model answers are usable.

The next candidate also includes the operator's code-proven-only and no-hedging block in all
thirteen prompts. The security-only severity schema excludes `nit`; CRITICAL/HIGH/MEDIUM map
to the existing blocking/major/minor wire values, and the response validator rejects `nit` too.
The new schema test failed on its extra enum member before the fix; the focused suite then passed
143 tests with two explicit hardware skips. Consultation turn 2 confirmed the old false claims
named real methods: a name-presence filter would not have caught them. No finding suppression is
used for this next preflight. Temperature was already zero, seed 4242; the earlier noise cannot
be attributed to high temperature. Prediction: better source selection and the explicit evidence
threshold should eliminate the three unsupported authz claims. Every new raw answer will still
be read before counting a successful repetition; an empty list alone does not establish recall.

This fixture is a wiring and calibration check on one model and one change, not a security benchmark
or evidence that the lane finds vulnerabilities generally. Larger changes, other languages and
different model/runtime versions require separate measurements.
