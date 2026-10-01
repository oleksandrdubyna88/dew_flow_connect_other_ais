# Security lane calibration — Windows Ollama

Status: preparation complete; live measurements pending. No quality claim is made yet.

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

The operator also requested an audit of the committed implementation through **each of the twelve
modules**, followed by a COAI consultation over the findings. `SecurityLaneAuditTests` runs those
cells sequentially and retains failures without abandoning the remaining modules. It uses the real
security lane with a synthetic clean ordinary reviewer, so it does not replace the code or feature
gate. Set `COAI_SECURITY_AUDIT_REPO`, `COAI_SECURITY_AUDIT_BASE` and `COAI_SECURITY_AUDIT_OUT`, then
run that explicit test through the GPU lease wrapper. Every module must reach the local shim;
a trigger skip cannot count as a completed model review. The ordinary three-repeat calibration
above remains a separate measurement.

Not run yet. No cells have been counted as successful. Token-usage plausibility cannot establish
full local input coverage; that remains unverified even if all model answers are usable.

This fixture is a wiring and calibration check on one model and one change, not a security benchmark
or evidence that the lane finds vulnerabilities generally. Larger changes, other languages and
different model/runtime versions require separate measurements.
