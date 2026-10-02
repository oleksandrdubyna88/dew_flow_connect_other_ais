# Security lane calibration — Windows Ollama

For the consolidated experiment inventory and subsequent PR-group/configuration results, see
[Local LLM security-lane experiments](RESULTS_security_lane_local_llm_windows.md).
The dated sections below retain their original checkpoint; their pending-work statements are not
the latest campaign status.

Status, 2026-10-02: Gemma completed three consecutive adequate feature replies and all twelve
modules. Positive controls still expose inaccurate explanation/reproduction details; broader quality
calibration remains open. Qwen has not qualified. The dated campaigns below retain their own results.

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
# Optional; the audit harness uses the same model selector. Unset retains the Qwen default.
$env:COAI_SECURITY_CALIBRATION_MODEL = 'Qwen3.5-35B-A3B-Q5_vk128:latest'
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
used for this next preflight. Temperature was already zero; the seed is derived from the prompt's
UTF-8 bytes by FNV-1a modulo 100000 (83047 for the frozen priority request), not a global 4242.
The earlier noise cannot
be attributed to high temperature. Prediction: better source selection and the explicit evidence
threshold should eliminate the three unsupported authz claims. Every new raw answer will still
be read before counting a successful repetition; an empty list alone does not establish recall.

This fixture is a wiring and calibration check on one model and one change, not a security benchmark
or evidence that the lane finds vulnerabilities generally. Larger changes, other languages and
different model/runtime versions require separate measurements.

## Historical comparison and shorter prompts (2026-10-01)

The operator requested re-reading the COAI research and then halving every redteam prompt.
[The manually judged local-model study](RESULTS_findings_that_are_worth_something.md) records
Qwen3.5 35B with four useful findings, one rule-only finding, one duplicate and four wrong findings
out of ten. Its 50% precision includes the rule-only finding; useful findings alone were 40%.
[The 128K study](RESULTS_local_models_128k.md) records five of eight planted plan defects twice,
and approximately 23.5K input tokens per code reviewer. These are different tasks and metrics,
not a security-lane success rate. The original session records and schema still exist under
`C:/Users/strug/qwen128`; they identify the universal code roles, including `security-reliability`.

The historical role prompt asks for a concrete situation and wrong outcome, accepts an empty list
and reviews the bounded change. The ordinary product path carries scope as well as diff. The lane
currently sends its specialized prompt, schema and source slice. Both use one local request with
thinking disabled; the old schema had seven finding fields, while this lane requires eleven.
Neither the short role wording nor the historical precision proves that a rewritten prompt works.

Sequential diagnostics on the frozen feature source at `8d933f72`, base `1056aed9`, used the real
product `--ask-local` shim with a loopback recording proxy. These are shim probes, not additional
full `PanelService` rounds. Requests, raw responses, hashes, seeds and failure artifacts remain in
`D:/rsd/_wt/security-lane-tools/qwen-*-preflight`. No output is silently repaired or suppressed.

| Comparison | Observation | Quality result |
|---|---|---|
| System instructions / user source split | 552 output tokens; valid JSON, unsupported polymorphic-deserialization claim | fail |
| Full product round with 64K slice budget | 54547 input / 601 output tokens; generated help text dominates the slice; intended reproduction cap called a vulnerability | fail |
| Exact cited serializer body added | 16184 input / 569 output; JSON allegation replaced by an objection to the reproduction cap | fail |
| Additional caller/privilege proof paragraph | 15996 input / 656 output; unsupported JSON allegation remains | fail |
| Temperature 0.1, top_p 0.1 | 15868 input / 766 output; unsupported JSON allegation remains | fail |
| Thinking low, 16384 output limit | output limit exhausted, empty answer | fail |
| Thinking low, temperature 0.6/top_p 0.95, penalties zero | 16384 output tokens, empty answer | fail |
| Task wording with long evidence blocks retained | 15480 input / 776 output; speculation about a missing callee | fail |
| Concise universal-style authorization task | 14817 input / 615 output; intended reproduction cap called an authorization flaw | fail |
| Original operator prompt on safe invoice fixture | 2292 input / 8192 output; reasoning leaked into a finding, answer cut off despite visible tenant guard | fail; diagnostic, not feature coverage |
| Half-length AuthZ on unchanged feature source | 15076 input / 520 output; valid JSON but invents malformed serializer output | fail |
| Half-length AuthZ with cited serializer body | 15392 input / 707 output; invents a downstream failure from intentionally retained incomplete evidence | fail |
| Half-length AuthZ on safe invoice fixture | 1504 input / 1668 output; valid JSON, two unsupported tenant-bypass findings despite the explicit guard | fail; diagnostic, not feature coverage |

The vulnerable invoice fixture previously produced a grounded tenant-isolation finding. The safe
twin failure means that positive case alone cannot qualify the prompt. The consultant's proposed
negative control was useful; its stronger inference that one failed control proves no future prompt
can work is not supported. The current feature-quality streak remains zero.

All thirteen Git-authored redteam prompts now total **5705 words instead of 11506 (49.6%)**.
Specialized checks, source isolation, evidence requirements and severity/JSON rules remain; repeated
attack examples and rhetoric were removed. The JSON instructions now defer to exactly the schema's
required fields instead of contradicting its title/why/fix/reproduction fields. The generated help
copy is updated with them. This is an operator-requested wording change, not a claimed model-quality
fix. Three consecutive adequate feature answers, the remaining modules and the final feature gate
are still required.

Validation of the shortened prompt assets: Debug server build with zero warnings/errors, 29
protocol/preset tests passed, 16 generated-help/coverage tests passed, TypeScript typecheck and the
help generator's consistency check passed. Consultation `fc975f1c8e914679b53a1fb5134b398b` was
closed as `not_solved`; its source-selection correction is implemented, but answer quality remains
unresolved. No successful repetition is inferred from schema compliance alone.

## Gemma comparison (2026-10-02)

The operator selected the installed `Gemma4-26B-A4B-Uncensored_vk128:latest`, digest
`6548c373a4c1a01c84d5bb3166f0971ae14d3e8216bb676c2f9f393527b6dc2e` (Q5_K_M, 25.2B).
Windows Ollama 0.35.0 reported `context_length: 131072` in `/api/ps`; this is observed runtime
capacity, not just the requested setting. Calls remained sequential on the native Windows endpoint.
The lease daemon refused connection at `127.0.0.1:5455`; the existing operator-authorized native
fallback was used. No model was downloaded and no global model selection was changed.

Subject: committed feature `1308108cd5ac903c60df62d6a445de3338abd1e0`, base
`1056aed99d968ce04a2f12dbe29461b2ee6aa5d4`. `SecurityLaneAuditTests` used the existing 24000-token
slice budget, 8192 output ceiling and a single local request. The Git prompt assets remained exactly
those of the shortened-prompt commit. The measured harness adds the model environment selector and
records its assembly hash before each run. Temperature stayed zero; the product derives its seed
from each composed request, so fresh nonce fences make these independent rounds rather than
byte-identical API replays. Predictions and raw requests/answers are retained under
`D:/rsd/_wt/security-lane-tools/gemma-feature`.

| Full product round | Input / output tokens | Observed result |
|---|---|---|
| AuthZ repetition 1 | 16894 / 10 | SECURE, empty findings, all 5 reviewers answered |
| AuthZ repetition 2 | 16900 / 10 | SECURE, empty findings, all 5 reviewers answered |
| AuthZ repetition 3 | 16904 / 10 | SECURE, empty findings, all 5 reviewers answered |
| SQL | 16999 / 10 | SECURE, empty findings |
| Concurrency | 16935 / 10 | SECURE, empty findings |
| Auth tokens | 18024 / 10 | SECURE, empty findings |
| SSRF | 16599 / 10 | SECURE, empty findings |
| Webhooks | 17395 / 10 | SECURE, empty findings |
| Files | 18267 / 10 | SECURE, empty findings |
| Command | 17792 / 10 | SECURE, empty findings |
| Deserialization | 17732 / 10 | SECURE, empty findings |
| Secrets | 18276 / 10 | SECURE, empty findings |
| Prompt injection | 17147 / 10 | SECURE, empty findings |
| XSS | 17137 / 10 | SECURE, empty findings |

Only after the three AuthZ successes were the remaining eleven modules launched. Every module
actually reached Ollama, passed the real reply validator and answered alongside all four ordinary
test reviewers. The custom `redteam-general.md` also returned SECURE (16914 / 10 tokens), through
the real `--ask-local` shim on the captured AuthZ source slice; that additional run is not a full
panel round or an automatic routing test. All thirteen prompt files were exercised.

These empty feature replies were adequate for the supplied slices, not evidence that the whole
feature is secure. The ordinary reviewers are `FakeCli` clean replies, and every round still says
local input coverage is unverified/incomplete. These runs cannot pass the real code/feature gates
or close local-trust §6. There were no feature findings to fix from this campaign.

### Positive and negative controls

A frozen safe invoice fixture returned SECURE (1535 / 10 tokens); removing only its tenant guard
returned a grounded tenant-bypass finding (1511 / 391). The direct API comparison changed the model
tag, which also selects that model's template/system text; it does not isolate weights alone.
Artifacts: `D:/rsd/_wt/security-lane-tools/gemma-controls`.

The full `SecurityLaneCalibrationTests` fixture removes both tenant validation and SQL parameters.
Its first run exposed a harness error: no written repository rules meant Conventions was skipped,
so the assertion expecting six replies received five. Adding fixture `AGENTS.md` reproduced the
intended six-reviewer setup without weakening that assertion. The corrected test passed all three
rounds (six local answers) in 2m15.649s. Artifacts:
`D:/rsd/_wt/security-lane-tools/gemma-calibration-corrected-fixture`.

Reading all six raw answers found the two planted defect classes every time, but also defects in
the explanations: clipped C# quotations, invented `Users` tables in AuthZ reproductions, missing
search-value preconditions and hypothetical database writes in SQL consequences. The product
accepted these answers; its schema/anchor checks do not prove semantic grounding. Therefore the
passing hardware test is wiring evidence, not a claim that all returned findings meet the operator's
quality threshold. No synthetic application or model-proposed reproduction was executed.

Two bounded scratch prompt variants requested complete prose, visible database objects, exact
remaining predicates and a single proved impact. Each was measured against both frozen positive
requests through the product shim. Readability improved, but both retained unsupported SQL impacts;
one also described the wrong row set. Neither variant was applied to the Git prompts. Raw probes:
`D:/rsd/_wt/security-lane-tools/gemma-refinement`. No finding was suppressed or silently repaired.
The second variant's safe twin still returned SECURE (1625 / 10 tokens); this did not repair its
positive-control explanation failures.

The configured consultant was requested with these observations and verbatim unsupported model
text. It refused because the caller session had reached its 10-call/24-hour cap. No fresh advice
was obtained and the cap was not changed. Remaining quality work is explanation/reproduction fidelity
and broader positive controls; this Gemma result does not turn Qwen's failed series
into successes. The model selector and corrected fixture build with zero warnings/errors; the
focused security suite passed 62 tests with two explicit hardware skips before the fixture fix,
and the corrected fixture itself subsequently passed its live three-round test.
Code/feature gate outcomes and current CI disposition are recorded on draft PR #634 separately.

## Historical merged-PR campaign: first group, 2026-10-02

The operator requested the latest thirty merged PRs, ordered by merge time, with stable final diffs,
all security selections enabled and Gemma on every active reviewer and consultant route. Selection
and merged ranges are frozen under `D:/rsd/_wt/security-lane-tools/pr30-campaign`; multi-commit rebases
are included in full. The final changed-file list and blob hashes for all thirty were checked against GitHub.
No later group has been run yet.

This campaign calls the actual MCP stdio tools through `gate-windows.mjs`, with
`invoke-pr30.ps1` retaining each request/result and dedicated per-PR temporary directories retaining
model prompts and raw replies. It exercises `open`, `review_plan`, explicit per-finding `resolve`,
`review_code`, and `resolve`. Every reviewer here is real Gemma, with no clean reviewer doubles.
Document-only deliverables will use the document gate; release metadata/configuration changes still
require the plan/code cycle. Feature-stage applicability is assessed from the actual original plan,
without inventing epics to reach its minimum. The security feature's own final gate remains last.

The baseline server is native Windows AOT from `71c5dab0`, version `0.41.0-security.71c5dab0`.
Model/context are the same Gemma tag and observed 131072-token window above. Requests use temperature
zero, frequency penalty 0.2, an 8192 output ceiling and `reasoning_effort: none`; security slices use
24000 tokens. There is one active local reviewer row for all stages, local concurrency one, all
twelve presets plus custom general. General uses the union of catalog triggers, so it is conditional
too. Prompts are the Git-embedded bodies, without overrides. Hashes and settings are in
`runtime-manifest.json` and `pr30-config/campaign-env.json` beside the campaign.

| PR | Final base → head | Plan replies | Code replies | Security replies | Code findings |
|---|---|---:|---:|---:|---:|
| #631, extension release metadata | `1bf3453` → `1056aed9` | 1/1 | 7/7 | 3, all empty | 6 ordinary |
| #630, endpoint model UI | `288b910` → `1bf3453` | 1/1 | 13/13 | 9, all empty | 9 ordinary |
| #629, release baseline | `b4f5fe7` → `288b910` | 1/1 | 4/4 | 0; all 13 selections skipped | 0 |

All 27 model calls answered; every admitted security call returned usable `SECURE` JSON. All three
plan and code verdicts were `proceed`. The eleven plan findings and fifteen ordinary code findings
were individually inspected and rejected with reasons recorded through MCP. Examples include a
release note treated as missing implementation, a nonexistent vault probe from a pure list-selection
function, and a race already prevented by the visible synchronous in-flight guard. This is evidence
of ordinary-role false positives, not a claim of excellent overall model accuracy. The scopes quoted
historical PR descriptions; conflating background release history with requested work remains a
measurement-input concern. No reproduction was executed.

The group exposed two routing defects before advancement: a changelog alone activated AuthZ/SSRF,
and SQL matched ordinary `Where`/`update` comments in TypeScript. The actual classifier probe is
`pr30-routing-probe/Program.cs`, referencing the product's `DiffSplitter` and `SecuritySignals`.
The correction excludes Markdown/reStructuredText from trigger evidence while retaining them as
supporting context, and uses database terms/call/statement shapes for SQL. Source in documentation
folders, SQL scripts, configuration and removed query calls remain eligible. New behavioral tests
failed seven cases against the old matcher, then passed after correction; restoring the old matcher
made the same seven fail again. The focused security suite passed 76 tests with two explicit hardware
skips before and after that mutation check. A classifier replay of the three frozen diffs now has no
signals for #631/#629 and six conditional pairings for #630 (AuthZ, SSRF, files, secrets, XSS and
general), dropping SQL, tokens and commands. The corrected historical group must still be repeated; no later
group is counted as accepted.

The native preview MCP and extension `0.61.0` were installed locally with backups of the prior binary
and settings. Existing editor windows need reload to display the new UI. This is a local preview,
not a published release. The consultant was requested again about the observed routing/grounding
failures and refused at the unchanged ten-call/24-hour cap; no fresh advice was obtained. The GPU
lease daemon was unavailable, so the previously authorized native fallback was used. Other machine
activity was not controlled, and no latency comparison is claimed. Empty findings on merged PRs do
not establish recall, complete input coverage or resolution of the positive-control fidelity issues.
