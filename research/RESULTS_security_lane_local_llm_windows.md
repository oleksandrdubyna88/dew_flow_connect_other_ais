# Local LLM security-lane experiments — consolidated Windows report

> Measurement checkpoint: **2026-10-02**, feature branch `feat/security-lane-modules`, SQL-term correction `98b01d3abce5ccf40e007cc5e5cd48d23c2ab193`, draft PR [#634](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/pull/634). Historical campaign groups used `cc9173dd9235f392ac7e36acc32a5627915735ed`. The campaign is incomplete. This records observed experiments, including failures; it does not mark the feature implemented or the model qualified for general security auditing.

## Results at this checkpoint

Qwen did not achieve the required three consecutive adequate feature answers: its successful streak remained **0/3**. It often produced syntactically valid findings about behavior that the visible code prevented, or consumed its output budget without a final answer. This does not prove that Qwen cannot perform the task under other conditions. Historical COAI measurements did find useful Qwen findings on a different review task.

Gemma achieved **3/3 adequate AuthZ feature replies**, followed by usable replies from all eleven other selectable security presets. The custom general prompt was also exercised through the actual local shim. All these feature-slice replies were empty `SECURE` JSON. Separate planted positive controls detected SQL injection and removed tenant authorization, but the explanations still contained unsupported consequences, invented objects, or incomplete reproduction preconditions. **Format reliability improved; semantic fidelity is not yet an acceptance pass.**

The subsequent all-Gemma campaign has **four fully assessed groups, 12 of 30 frozen merged PRs**: 78 answered model calls, including 21 strict empty security replies. Seventy-two ordinary plan/code/document findings were individually rejected against the frozen source and scope. Group 5's ordinary findings are now adjudicated and its sessions resolved, but the group remains incomplete: one security reviewer never acquired the engine, and SQL routing was activated by a prose comment. Groups 6–10 have not run. These are distinct populations; their counts must not be combined into an accuracy score.

## 1. What was being measured

The operator requested Windows-native execution, Git-tracked red-team prompts, conditional checkbox selection, source-only auditing, strict JSON, a three-success preflight before the remaining modules, consultant discussion, and a final feature gate. The subject was this security-lane feature, not an unsolicited audit of the whole repository. Later, the operator requested a frozen 30-PR historical campaign to test routing and the complete MCP workflow.

Four questions were kept separate:

| Question | Evidence needed | What does not establish it |
|---|---|---|
| Does the runtime answer reliably? | Actual request reaches the model, finishes, and yields usable output | Starting a process or a reviewer being scheduled |
| Does the answer obey the contract? | Raw JSON, exact schema, consistent status, accepted product validation | JSON syntax alone |
| Is a finding correct and readable? | Visible execution path, exact preconditions, supported impact, complete explanation | A real identifier, plausible title, or model self-approval |
| Does the lane select the right checks? | Frozen diff, recorded trigger prediction, actual reviewer list, manual relevance check | All boxes checked or classifier prediction agreeing with itself |

The harness does not execute application fixtures or model-proposed exploits. Manual judgments therefore establish whether statements follow from supplied source; they are not live exploit demonstrations. An empty list is not evidence of recall. Input token counts cannot prove that the model received and used every required source fragment.

## 2. Environment, identities, and reproducibility

All current work uses native Windows, PowerShell, and Windows Ollama at `http://localhost:11434`. No WSL runtime was used for the measured series described here. The operator initially called the model Qwen 3.6, then explicitly selected the installed tag **`Qwen3.5-35B-A3B-Q5_vk128:latest`**. The later selected model is **`Gemma4-26B-A4B-Uncensored_vk128:latest`**.

| Condition | Recorded value and qualification |
|---|---|
| Ollama | Windows 0.35.0; Qwen diagnostic checkpoint records AMD/ROCm and q8_0 KV |
| Context | 131072 requested; Gemma `/api/ps` also reported `context_length: 131072` |
| Gemma identity | Digest `6548c373a4c1a01c84d5bb3166f0971ae14d3e8216bb676c2f9f393527b6dc2e`; model metadata Q5_K_M, 25.2B |
| Observed Gemma GPU residency | `size_vram: 22313749708` bytes; not a measured peak-memory requirement |
| Default audit output | 8192 tokens; individual diagnostic variants explicitly changed this |
| Sampling | Temperature 0 on the product path; frequency penalty 0.2 in the historical campaign; reasoning effort `none` unless the arm says otherwise |
| Seed | Product FNV-1a over prompt UTF-8 bytes modulo 100000; frozen priority request seed 83047; fresh nonce fences change the seed |
| Security slice | Later default 24000 tokens; earlier 128K/64K-budget trials are separate conditions |
| Concurrency | Each MCP process sets overall/per-provider/local concurrency 1. The harness launched separate PR processes concurrently; these limits do not serialize independent MCP processes. Cooperating inference is serialized by the engine lease |
| Deadlines | Initial calibration eight minutes per reviewer; historical PR campaign ten minutes |
| Native preview | Initial `0.41.0-security.71c5dab0`; corrected routing `0.41.0-security.cc9173dd` |
| Corrected native executable SHA-256 | `a1c8462517ef33fd82381aeec0530c14ef2e54ae58a3b57562ace35bba54e048` |
| Extension preview | 0.61.0; VSIX SHA-256 `9bbe013af3f26cf3f74956232452ecb04b531dc9f9b4c227666166ac72226351` |

The family GPU lease daemon refused connections at `127.0.0.1:5455`; the previously operator-authorized native fallback was used. Other machine activity was not controlled. Product engine locking serialized cooperating local requests, but did not provide a fair queue or exclusive control over unrelated GPU workloads. No cross-model latency or throughput comparison is claimed.

Raw local evidence lives under **`D:/rsd/_wt/security-lane-tools`**, abbreviated **ART** below. This is an operator-owned scratch directory, not a public artifact server or a Git-tracked attachment. Exact requests, raw responses, manifests, hashes, and failure records were retained as individual calls completed. The report preserves the material measurements in Git so its conclusions remain readable without that directory. Do not publish settings backups wholesale: they are not a curated evidence bundle.

## 3. Measurement levels and their limits

| Level | Harness | What actually ran |
|---|---|---|
| Synthetic positive calibration | `src_mcp/tests/SecurityLaneCalibrationTests.cs` | Three real `PanelService.ReviewCodeAsync` rounds; SQL/AuthZ local reviewers; clean `FakeCli` ordinary collaborators |
| Feature/module preflight | `src_mcp/tests/SecurityLaneAuditTests.cs` | Committed feature slices, real lane and validator, four clean ordinary test collaborators; selected modules must reach the local shim |
| Captured-request diagnosis | `ART/probe-security-wire.mjs`, `ART/probe-gemma-wire.mjs` | Actual product `--ask-local` through a loopback recorder; retains original/sent request and raw HTTP response; not a complete panel/gate round |
| Runtime/contract probes | `ART/probe-*.mjs` | Direct bounded API controls, including selected direct-runner comparisons; diagnostic only |
| Historical PR workflow | `ART/gate-windows.mjs`, `ART/invoke-pr30.ps1` | Actual MCP stdio `open`, review, and per-finding `resolve`; every enabled reviewer real Gemma; no clean doubles |

The synthetic fixture starts with its test plan state satisfied; this is harness setup, not a product gate bypass. Historical document deliverables use `review_document`; ordinary changes use plan then code. The actual security-lane feature's final `review_feature` is a separate obligation and must remain last.

## 4. Historical research and benchmark lessons

The operator remembered roughly 30% correct findings from the same model family. The checked COAI records use different denominators:

- [Manually judged findings](RESULTS_findings_that_are_worth_something.md): Qwen3.5 35B had four useful findings, one rule-only finding, one duplicate, and four wrong findings out of ten. The document's 50% precision includes the rule-only finding; useful findings alone were 40%.
- [128K local-model study](RESULTS_local_models_128k.md): five of eight planted plan defects found in each of two runs; code reviewers received approximately 23.5K input tokens.
- Those successful historical tasks used ordinary roles, scope plus a bounded change, and a seven-field finding schema. The specialized lane added evidence/reproduction obligations and eleven required finding fields. Historical success therefore did not establish that the new task or contract would work.

`dew_flow_benchmark` was inspected at `4dc98df5da2390348413fbd16ff4e5d1b31373fd`, without modification. Evidence: `ART/benchmark-transfer-evidence.json`. Its `OpenAiCompatibleRuntime.cs:104` separated system/user messages and retained sampling, raw output, and finish reason. Its architecture recorded nine questions × five repeats with zero repeat variance under fixed temperature/seed. Its `research/MEASURED_LESSONS.md:4d` recorded an unreliable Qwen self-judge. Lessons applied here: pin and read back the wire request; distinguish repeatability from correctness; manually inspect findings; include safe and unsafe twins. Role separation was measured and did not itself cure the security-lane speculation.

## 5. Qwen: failures, changes, and observed results

### 5.1 First product rounds and real feature defects

Initial subject `ff833afce42a832148b3a5b358481d49c3722bec`: AuthZ used **124040/1331** input/output tokens, SQL **123465/2083**. Both returned `notes`. The derived schema allowed this field while the security validator refused it: a real schema contradiction. Two completed cells failed usability; the third was cancelled and the matrix stopped. Evidence: `ART/qwen-twelve-ff833afc`.

AuthZ also evaluated examples from the audit prompt as if they were application defects. SQL speculated about cap order, coverage, and query routing; source inspection contradicted those claims. Corrections separated instructions from source using explicit start/end labels and a nonce fence, tightened the security-only schema, and made unusable answers fail the harness rather than count as successful requests.

At `5f3fe8734149902573d8b18d31e0da81bb8a9828`, base `1056aed99d968ce04a2f12dbe29461b2ee6aa5d4`, the source-boundary preflight returned valid JSON at **122962/1585** tokens, all five reviewers answered. Its three claims remained wrong: the intended severity cap, evidence already retained in `AlsoSeenBy`, and already-reported partial coverage. Quality streak remained zero. A separate harness assertion expected `5 of 5` instead of the actual `all 5`; fixing that wording did not turn those findings into adequate answers.

Source selection exposed another real defect: keyword-rich prompts/docs/tests could consume all sixteen selected file slots. Two failing tests reproduced the reader/packer behavior. Production/configuration source was prioritized ahead of labelled support material, and subsequent trials used the 24000-token slice default. These changes varied together; their effects were not isolated statistically.

All thirteen prompts received source-only, anti-hallucination, severity, and JSON rules. The security schema/validator reject `nit`; CRITICAL/HIGH/MEDIUM correspond to blocking/major/minor on the existing wire. Temperature was already zero, so high temperature cannot explain the initial failures. The proposed identifier-presence filter was not installed: observed false claims named real methods and would pass it. No postprocessor silently removed findings to improve the score.

### 5.2 Frozen feature-request comparisons

The later shim series froze feature source at `8d933f72738cda239d9f38f235513a668f19fd29`, base `1056aed9`. Each result below is a diagnostic arm, not a successful three-round qualification. Counts are observed input/output tokens.

| Attempt | Tokens | Observation |
|---|---:|---|
| Separate system instructions and user source | 15667 / 552 | Valid JSON; invented polymorphic-deserialization exploit |
| Increase slice to 64K in a product round | 54547 / 601 | Generated help dominates context; intended cap called a vulnerability |
| Add the cited serializer body | 16184 / 569 | Claim shifts to the intentional reproduction cap |
| Add caller/privilege proof paragraph | 15996 / 656 | Unsupported JSON allegation remains |
| Temperature 0.1, top_p 0.1 | 15868 / 766 | Unsupported JSON allegation remains |
| Thinking low, 16384 output ceiling | 15866 / 16384 | Budget exhausted; empty final content |
| Thinking low, temperature 0.6/top_p 0.95, penalties zero | 15866 / 16384 | Budget exhausted; empty final content |
| Task wording with long evidence blocks retained | 15480 / 776 | Speculation about an absent callee |
| Concise ordinary-role-style authorization task | 14817 / 615 | Intended cap called an authorization flaw |
| Half-length AuthZ prompt | 15076 / 520 | Valid JSON; invented malformed serializer output |
| Half-length prompt plus serializer body | 15392 / 707 | Invented downstream failure from retained incomplete evidence |
| Scope added | 14930 / 625 | Cap still reported as a vulnerability |
| Explicit contract near scope | 15173 / 783 | Same cap-policy objection |
| Same contract moved to tail | 15173 / 949 | Same cap-policy objection |

The thirteen Git prompts were reduced from **11506 to 5705 words (49.6%)**. Repeated examples/rhetoric were removed while specialized checks, evidence requirements, source boundaries, and JSON constraints remained. Output instructions were aligned with the real required fields instead of forbidding legitimate title/why/fix/reproduction keys. Generated help was synchronized. This was a measured size reduction and requested wording change, not a demonstrated Qwen-quality repair.

### 5.3 Safe/unsafe fixture and contract diagnosis

The safe invoice method visibly compares the trusted `tenant_id` claim with `tenantId`, throws on mismatch, and uses a parameterized query. The unsafe twin removes the guard; the positive SQL fixture also removes parameterization. A model has to distinguish these, not merely recognize that the file contains authorization and database APIs.

| Family of attempts and ART directory | Observed result |
|---|---|
| Original AuthZ prompt: `qwen-authz-fixture-preflight`, `qwen-clean-fixture-preflight` | Unsafe: 2266/473, recognizable tenant-bypass finding. Safe: 2292/8192, spurious bypass and cut-off answer |
| Half-length prompt: `qwen-half-clean-preflight` | Safe: 1504/1668, valid JSON with two unsupported bypass findings |
| Example order and JSON mode: `qwen-example-order-preflight`, `qwen-json-object-clean-preflight` | Safe: 1504/994 and 1504/1464; false findings persisted |
| Minimal prompt: `qwen-minimal-invoice` | Safe: 448/365, invented SQL injection despite parameters; internally inconsistent `SECURE` with nonempty findings |
| Few-shot and conversational examples: `qwen-fewshot-review`, `qwen-conversation-examples` | Both safe and unsafe produced findings; no safe-twin qualification |
| Concrete request, execution-shaped review, test-writing task: `qwen-concrete-case`, `qwen-execution-clean-preflight`, `qwen-flow-review`, `qwen-test-task` | Narrowing the task did not reliably eliminate safe-code false positives; some safe answers exhausted 2048 tokens |
| Guard braces/newlines and explicit source fence: `qwen-guard-format`, `qwen-fenced-invoice` | Safe still falsely flagged or cut off; unsafe still flagged |
| Small Boolean/status contracts: `qwen-boolean-authz`, `qwen-status-review`, `qwen-binary-*-preflight` | Safe/unsafe often received the same answer: both `crossTenantRead: true`, both `FINDINGS`, or both `NO`; a short output is not necessarily correct |
| Control-flow variants: `qwen-execution-controls` | Ordinary guard reported query-called true for both match/mismatch; inverted guard false for both; no-guard mismatch true. No consistent execution discriminator |
| Output-shape controls: `qwen-output-contract` | One object-form pair correctly distinguished safe false/unsafe true; free-text unsafe answer was `NO`. Isolated success did not transfer reliably to findings |
| Empty-answer controls: `qwen-empty-contract` | Copy/identity tasks returned empty `SECURE` at 244/11 and 265/20; runtime can emit the empty branch |
| Historical seven-field contract: `qwen-historical-contract` | Safe and unsafe each produced three findings; reverting field count alone did not qualify the task |
| Status-first / union schema: `qwen-status-first`, `qwen-schema-union` | Safe still flagged; union safe answer contained two findings |
| Strict/unconstrained/plain wording: `qwen-plain-review` | Safe variants exhausted 2048 tokens while describing a false bypass; removing strict output mode did not establish a fix |
| Plain execution explanation: `qwen-plain-execution` | Safe 244/182 correctly says mismatch throws before query, then invents a framework 401 mapping; unsafe 222/263. Partial reasoning competence, not fully grounded output |
| Trace then review: `qwen-two-stage` | Safe trace 265/467 described the guard; final review 2071/688 again alleged tenant bypass. Unsafe trace hit its 768-token limit; no completed unsafe final review was recorded |

This last pair is particularly informative: a correct local control-flow statement can be followed by an incorrect finding in the same experiment. The problem cannot simply be labelled “cannot read C#,” but neither can a good intermediate explanation certify the final answer.

### 5.4 Runtime and reasoning controls

| Attempt | Observation and limit |
|---|---|
| OpenAI-compatible endpoint versus direct Windows runner (`qwen-runtime-path`) | Both still falsely flagged safe source. Direct safe 1504/495; direct unsafe 1482/526. The temporary runner port was 62850, not a permanent supported endpoint |
| Neutral system text, both routes | Safe hit 2048 tokens on both; unsafe finished in 407/413 tokens. Changing system text did not establish a repair |
| Prompt cache disabled (`qwen-no-cache`) | Safe 1504/641 still false; unsafe 1482/502. Cache removal did not eliminate the failure |
| Native 131072 profile (`qwen-native-profile/window-131072`) | Safe 1504/899 still false; unsafe 1482/432 |
| Literal backslashes versus real template newlines (`qwen-template-newlines`) | Safe and unsafe both returned `NO` under both variants; newline rendering alone did not discriminate them |
| Schema enforcement sentinel (`qwen-schema-enforcement`) | Ollama native, Ollama OpenAI, and direct runner each returned the schema-only sentinel at 37/10 tokens. This checked that these paths could constrain output, not that all schema keywords or semantic rules were honored |
| Thinking with penalty (`qwen-thinking-penalty`) | Safe exhausted 8192 tokens without final content; unsafe completed with 5279 tokens |
| Bounded thinking (`qwen-bounded-thinking`) | Final answers became available, but safe still flagged. 2048-budget safe/unsafe output 2509/2434; neutral-system version 2595/2422. Total output includes reasoning; boundedness did not establish correctness |

The recorded resumed Qwen checkpoint still had no qualified feature fix. No separate stock-Qwen download was started; therefore custom model/system/template/quantization versus stock weights was not isolated. A proposed five-seed paired experiment must not be reported as completed merely because the consultant suggested it. Raw exploratory arms vary in task, contract, and sampling; they are not one statistically controlled leaderboard.

The historical plan was also reconstructed from the retained plan/schema and the `939175d` role prompt (`ART/qwen-historical-plan`). It returned seven findings at **1517/1273** tokens, including recognizable deletion-before-load and credential-on-argv concerns, but also speculative volume/performance claims. The exact historical request, nonce/seed, and executable were not retained, so this was not an exact replay or a newly adjudicated precision score. It reinforces the need to distinguish plan critique from the specialized security task.

## 6. Gemma: what improved and what did not

### 6.1 Required three-reply preflight, then every preset

Feature subject `1308108cd5ac903c60df62d6a445de3338abd1e0`, base `1056aed99d968ce04a2f12dbe29461b2ee6aa5d4`. Git prompts stayed at their shortened versions. All three AuthZ rounds and every remaining preset returned `SECURE` with no findings and passed the real validator; all five harness reviewers answered per round.

| Prompt / repetition | Input tokens | Output tokens |
|---|---:|---:|
| AuthZ 1 | 16894 | 10 |
| AuthZ 2 | 16900 | 10 |
| AuthZ 3 | 16904 | 10 |
| SQL | 16999 | 10 |
| Concurrency | 16935 | 10 |
| Auth tokens | 18024 | 10 |
| SSRF | 16599 | 10 |
| Webhooks | 17395 | 10 |
| Files | 18267 | 10 |
| Command | 17792 | 10 |
| Deserialization | 17732 | 10 |
| Secrets | 18276 | 10 |
| Prompt injection | 17147 | 10 |
| XSS | 17137 | 10 |
| Custom general, shim only | 16914 | 10 |

Evidence: `ART/gemma-feature`, `ART/gemma-general`. Only after AuthZ reached three adequate replies were the remaining eleven presets launched. General's captured slice call was not a routing or panel test. All local rounds still reported unverified/incomplete input coverage. These observations qualified the requested preflight for these slices, not the entire security product.

### 6.2 Positive controls and explanation defects

The direct safe/unsafe comparison gave safe **1535/10**, empty; unsafe **1511/391**, a grounded tenant-bypass finding (`ART/gemma-controls`). Changing the model tag also selects its template/system text, so this is not a weights-only comparison with Qwen.

The first full positive test failed because its fixture lacked written repository rules: Conventions was skipped, yielding five replies where six were expected. Adding fixture `AGENTS.md` restored the intended setup without weakening the assertion. The corrected three-round test completed in **2m15.649s**, six local answers, and found both planted defect classes in each round. This is a test duration under those conditions, not a model latency benchmark.

Pinned synthetic fixture: base `2804c54391c35bf5f68135af1fd5bae40dd59764`, head `4ab8c7dc28b503cff3eea21c560d5c3c6e55913d`, source SHA-256 `7639062B29B9BDB957395737307C3CCA74FE78286F7EF3D3FB3CE4BDE32B87EA`. Evidence: `ART/gemma-calibration-corrected-fixture`.

Manual reading of all six answers found these problems:

- Valid JSON can contain a clipped explanation, for example a sentence ending at an embedded C# quotation.
- AuthZ reproduction instructions introduced a `Users` table not present in the source.
- A normal unauthorized tenant read still requires a row matching the remaining `Name` predicate; saying an arbitrary `search` returns tenant invoices omits that precondition.
- SQL injection in the visible SELECT supports unauthorized reads; arbitrary writes, corruption, or provider-dependent commands need additional evidence.

Two scratch prompt variants requested complete prose, visible database objects, exact remaining predicates, and one proved impact. Both improved some wording but retained unsupported SQL impacts; one described the wrong row set. Variant 2's safe twin remained empty (**1625/10**). Its SQL positive was **1955/661** and still claimed arbitrary commands “depending on the database provider.” Its AuthZ answer combined missing authorization and SQL injection, then added corruption despite the narrower instruction. Neither variant was applied to Git prompts. Evidence: `ART/gemma-refinement`.

The automated positive test passing is therefore **wiring/detection evidence**, not proof that every field of every finding meets the acceptance threshold. Neither schema validation nor identifier matching establishes semantic fidelity.

### 6.3 Follow-up prompt and thinking probes, 2026-10-02

These are actual `--ask-local` requests through `ART/probe-gemma-wire.mjs`, not full MCP panel rounds. They retain raw requests, raw responses, finish reasons and exit codes under `ART/gemma-refinement`. Model, schema and 8192-token output ceiling remain the same. The positive source remains the frozen `4ab8c7dc` invoice fixture above; the safe source includes its tenant guard and parameterized query. Safe and unsafe request framing differ, so this pair is not a one-line mutation experiment.

| Arm | Input/output tokens | Observed result | Manual assessment |
|---|---:|---|---|
| `sql-v2-low-run`, temperature 0, seed 44792 | 1953/8192 | `finish_reason=length`, empty content, exit 70 | Repeated identical final checks consumed the entire budget; no usable answer |
| `sql-v3-run`, shorter instructions, no thinking, derived seed 52203 | 1399/359 | One SQL finding, stop, exit 0 | Concrete cross-tenant SQL read; independent removed tenant guard missed |
| `sql-v3-pinned-run`, seed 44792 | 1399/598 | Two findings, stop, exit 0 | Both classes detected, but tenant reproduction still assumes an arbitrary search matches a row; embedded quotations overescaped |
| `safe-v3-pinned-run`, seed 44792 | 1170/10 | Strict empty `SECURE`, stop, exit 0 | Correct abstention for this supplied safe source |
| `sql-v4-pinned-run`, requested null reproduction, seed 44792 | 1481/393 | Two findings with null reproduction, stop, exit 0 | Worse: says an unparenthesized `OR 1=1` returns only the requested tenant, and adds unseen-table access; tenant trace still omits matching-name precondition |
| `sql-v2-thinking-sampling-run`, low thinking, temperature 0.6, top-p 0.95, seed 44792 | 1953/6036 | Two findings, stop, exit 0 | Escaped the measured repetition loop, but retained the tenant-name omission and an incomplete SQL row-set explanation; quality still fails |
| `sql-v5-pinned-run`, generic predicate/precedence example, seed 44792 | 1566/365 | One SQL finding, stop, exit 0 | Correct all-tenant read and a concrete sample row; independent removed tenant guard missed; source quotations still overescaped |
| `safe-v5-pinned-run`, seed 44792 | 1337/10 | Strict empty `SECURE`, stop, exit 0 | Correct abstention for this supplied safe source |
| `authz-v5-pinned-run`, authorization-only task, seed 44792 | 1648/299 | One tenant-guard finding, stop, exit 0 | Matching-row precondition finally present; consequence still overclaims all invoice names/IDs and expected result offers unsupported alternatives |
| `authz-v6-pinned-run`, explicit impact quantifiers, seed 44792 | 1721/286 | One tenant-guard finding, stop, exit 0 | Concrete matching row/request and the exact old guard; readable prose without invented tables or write impacts |
| `authz-safe-v6-pinned-run`, seed 44792 | 1492/10 | Strict empty `SECURE`, stop, exit 0 | Correct abstention for this supplied safe source |
| `authz-v6-repeat-1`, identical wire request | 1721/317 | One tenant-guard finding, stop, exit 0 | Complete concrete reproduction; matching tenant/name values |
| `authz-v6-repeat-2`, identical wire request | 1721/310 | One tenant-guard finding, stop, exit 0 | Complete concrete reproduction; matching tenant/name values |
| `authz-v6-repeat-3`, identical wire request | 1721/310 | One tenant-guard finding, stop, exit 0 | Complete concrete reproduction; same answer as repeat 2 |

Variant 3 put the operation and remaining-predicate constraints first and shortened the SQL payload from 7850 to 4989 characters, retaining its schema and source suffix. Variant 4 requested the schema-permitted null reproduction and required the concrete trace in the mandatory evidence fields instead. The latter did not cure grounding; removing a duplicate output obligation did not remove the model's error. Variant 5 added a generic worked example of preserving filters and evaluating SQL precedence, outside the source fence. It improved the one reported SQL trace but did not meet the predeclared two-defect recall criterion.

The separate AuthZ task then excluded SQL injection and requested an independent access-control trace with ordinary inputs. Variant 6 also constrained impact quantifiers and the expected prior behavior. `authz-v6-pinned-run` was the exploratory positive check. After its safe twin returned empty, the separate `authz-v6-repeat-1/2/3` sequence each produced one manually checked, readable, schema-shaped finding with a matching sample row and request; only these three repetitions constitute the reported streak. All had wire SHA-256 `d93a846a9263c42441852e1de81643a8c41b8dbfc7881cdafa0f92d5a2413978`, seed 44792 and no thinking. Reported cached input tokens were 1155, 1720 and 1720; the first answer differed in wording/sample values, while the last two matched. Thus fixed wire inputs did not establish byte-level determinism.

This is a **3/3 positive AuthZ diagnostic streak on one fixture**, not a new full-product qualification or a general accuracy score. The reproduced access is to matching invoice rows in another tenant; it does not prove arbitrary table access or all rows. The model cites the method block at line 11 rather than the query at line 12. Severity calibration, other defect classes, unseen fixtures and the final product-composed prompt remain separate checks. No scratch variant has replaced a Git prompt at this checkpoint, and no generated reproduction was executed.

The thinking/sampling comparison kept the variant-2 prompt and low effort unchanged, requesting temperature 0.6 and top-p 0.95 instead of the previous greedy configuration. Its existing harness preset is named `qwen-coding`, but both the manifest and actual request identify **Gemma**. The final answer arrived within the ceiling, with 20429 reasoning characters; this one observation supports neither a general repetition cure nor semantic qualification. Sampling changed as a configuration bundle, not as separately measured temperature and top-p effects.

The harness initially derived a different seed from the changed prompt. Its first variant-3 run therefore changed both prompt and seed and cannot isolate a prompt effect. An explicit optional seed was then added to the existing recorder, and the sent request was read back before assessing the controlled follow-up. Another harness error is retained: preparation wrote the positive variant before failing on the safe fixture's different context prefix; that positive request was launched before preparation succeeded. The prediction text existed in `prepare-v3.mjs` before launch, but its JSON manifest was saved during inference, before reading the answer. Later runs have separate pre-run prediction files. These qualifications matter more than a cleaner-looking table.

Evidence: `low-reasoning-prediction.json`, `v3-prediction.json`, `v3-seed-prediction.json`, `v4-prediction.json`, `thinking-sampling-prediction.json`, `v5-prediction.json`, `authz-v5-prediction.json`, `authz-v6-prediction.json`, and each named arm's `sent-request.json`, `observation.json`, `raw-response.json` and `exit.json` under `ART/gemma-refinement`.

### 6.4 AuthZ v6 through the real product composer

The existing explicit C# calibration harness then loaded v6 as a private prompt override and ran
three real `PanelService.ReviewCodeAsync` rounds. Ordinary reviewers remained clean test doubles;
SQL used its shipped prompt as a separate control. This transfer changes the wrapper and derived
nonce/seed as well as the execution path, so it is not a single-variable comparison with §6.3.
All three captured AuthZ requests were checked to start with the actual override.

The harness passed in 2m12.145s, with six usable local replies. Manual AuthZ assessment was **2/3,
not the required three consecutive adequate replies**: repeat 1 invented an alternative expected
empty-row response where the removed guard explicitly throws `UnauthorizedAccessException`.
Repeats 2 and 3 supplied matching row/request values and the exact prior exception. Input/output
tokens were 1845/332, 1847/318 and 1847/325. The SQL controls still contained unsupported provider
impacts, wrong row-set reasoning or incomplete text; a usable response is not a correct finding.

Evidence: `ART/gemma-refinement/authz-v6-product-resumed`, fixture base
`55dc06b32907535eb4415f27e4ceb90c488cd3e9`, head
`29e1110d56947f3c5e6aa978a96720d96b950c91`, source SHA-256
`7639062b29b9bdb957395737307c3cca74fe78286f7ef3d3fb3ce4bde32b87ea`.
`override-manifest.json` records the actual prompt hash
`b69cf38a62231c8a0dcdecb87bb69be9d5bda9be1319f7736c79f5b52cc2fdfe`;
the existing harness's `manifest.json` hashes embedded defaults rather than overrides and cannot
identify this candidate by itself. The earlier `authz-v6-product` attempt was interrupted by the
session lifecycle; its partial artifacts remain separate. No candidate has been deployed.

## 7. Frozen 30-PR campaign through MCP

### 7.1 Setup and selection

The latest thirty merged PRs were selected by merge time and frozen with stable final ranges, including complete multi-commit rebases. Final file lists/blob hashes were checked against GitHub. Manifests and scopes are under `ART/pr30-campaign`; selection is `ART/security-pr30-selection.json`.

Gemma was configured on every active ordinary stage and consultant route. All twelve presets plus custom general were selected; general uses the union of catalog triggers. Git-embedded prompt bodies had no override files. Configuration is recorded in `ART/pr30-config/campaign-env.json`; ordinary role names/default selection still matter. In particular, a configured per-round Architecture prompt name did not mean that prompt was accepted: recorded Architecture requests used the valid role fallback. A declared setting is not evidence of the actual prompt sent.

**Configuration incident and correction, 11:48 UTC on 2026-10-02:** this experimental all-Gemma configuration had also been applied to shared VS Code/MCP settings. The operator reported that fifteen other agents were now sending their COAI calls to the local engine. That is a scope error in the experiment setup, not a model defect. Shared reviewers were restored to `gpt-6-luna` and `gemini-3.8-flash-medium`, with cloud consultant routes, the previous shared concurrency of seven, and no global security lane. Both settings files were read back and the native `--providers` command reported only Codex and Gemini, both found, with no unrecognized settings. The installed `agy models` list confirmed the requested Flash medium identifier.

For subsequent measurements, `ART/pr30-config/campaign-security-only-env.json` and the private campaign settings retain Gemma solely in security pairings; its ordinary plan/code/document/feature ticks are off. Ordinary roles and consultants use the two restored cloud models. The original all-Gemma configuration and completed measurements remain frozen as historical evidence. Results under this new mixed-reviewer configuration must be labelled separately, not pooled with the all-Gemma table. Already-running calls can finish with the prior configuration; live settings reload affects following MCP calls. No other agent's process was terminated.

Scopes came from original PR intent rather than reverse-engineered diffs. No inspected historical PR demonstrated a completed feature with three or more epics under the default threshold; stories/build steps were not renamed to force `review_feature`. The actual security-lane feature still requires its own final feature gate with its two real epics and the operator-authorized **minimum of two epics** (`COAI_FEATURE_MIN_EPICS=2`), not a minimum reviewer count.

### 7.2 Baseline, correction, and completed groups

Initial server `71c5dab0`: group 1 produced **27/27 replies**, including **12 empty security replies**, and 26 ordinary findings (11 plan, 15 code), all individually rejected. Routing incorrectly selected security checks from a changelog and SQL from generic `Where`/`update` prose.

Commit `cc9173dd` excludes Markdown/reStructuredText from trigger evidence and narrows SQL call/statement shapes, retaining actual source in documentation folders, SQL scripts, config, and removed calls. Seven new regression cases failed before, passed after, failed again when the old matcher was restored, then passed on restoration. Focused suite: **76 passed, two explicit hardware skips**. Group 1 was rerun before proceeding; the baseline is retained but excluded from the totals below.

| Group | PRs | Answered model calls | Security replies | Ordinary findings, all individually rejected | State |
|---|---|---:|---:|---:|---|
| 1, corrected r2 | 631, 630, 629 | 21 | 6 | 23 | Resolved; assessed |
| 2 | 628, 627, 626 | 24 | 9 | 16 | Historical complete run; routing qualification provisional pending SQL-term rerun |
| 3 | 625, 622, 621 | 19 | 4 | 17 | Resolved; assessed |
| 4 | 620, 619, 618 | 14 | 2 | 16 | Resolved; assessed; 619 document stage |
| Historical completed-call subtotal | 12 PRs | **78** | **21** | **72** | Not qualification under the latest classifier; group 2 requires rerun |

Group 5's initial calls also used **all Gemma**, before the shared-setting correction. They remain an incomplete historical measurement and are excluded from the subtotal. The following is a continuation schedule, not measured mixed-reviewer results:

| Group | PRs | State / next configuration |
|---|---|---|
| 5 | 617, 616, 615 | Hold: initial all-Gemma results have routing/engine-wait failures; affected rerun will use cloud ordinary roles and local security only |
| 6 | 614, 612, 602 | Not run; mixed reviewers |
| 7 | 611, 608, 606 | Not run; mixed reviewers |
| 8 | 604, 605, 603 | Not run; mixed reviewers |
| 9 | 600, 599, 598 | Not run; mixed reviewers |
| 10 | 597, 596, 595 | Not run; mixed reviewers |

Keep subsequent results in a new configuration-labelled assessment/table. Do not rerun unaffected groups 1–4 merely to erase the configuration transition; rerun any group affected by a product fix and record that rerun separately.

The 21 admitted security replies are strict empty `SECURE` JSON. Typical rejected ordinary findings: treating removed lines as current, calling valid sibling links broken while proposing the same link, assuming absent earlier implementation in a release-only PR, and reading a comment rejecting `font-size: 1.5em` as code setting `zoom: 1.5em`. These failures make ordinary-role precision unsatisfactory in this sample. There is no independently labelled vulnerability population here from which to estimate recall; merged PRs are not automatically proven safe.

### 7.3 Group 5 failures surfaced while compiling this report

PR #617 completed and was resolved. PR #616's code result reports all 13 reviewers answered. PR #615 reports a local `redteam-command` failure: **exit 69**, engine busy for the entire **590-second** allotted wait, so the question was never sent. The round nevertheless returned `proceed` with explicit incomplete-lane reporting. A gate verdict is not a claim that every selected audit completed; this group cannot count as a complete model matrix.

The remaining eleven code findings for #616 and ten for #615 were read against their frozen heads and individually rejected through actual MCP `resolve`; both replies now say `Done`, `awaitingResolve=false`, with eleven and ten decisions recorded respectively. Across this incomplete group there are eleven plan and 28 code findings, all adjudicated. Its eighteen returned raw security replies are strict empty `SECURE`; the nineteenth expected reply is absent. `ART/pr30-campaign/group-5-incomplete-assessment.json` explicitly retains `HOLD_INCOMPLETE`; the normal complete-group assessor still refuses missing reviewers. These eighteen replies and 39 ordinary findings are excluded from the completed-group subtotal.

PR #615 also activated SQL solely from unchanged source context in `src_vs_code/src/roundsLog.ts`: “HTML from the database.” The fragment contains no SQL code. Five additional test cases were written; **four failed** against the original production behavior: two prose false positives and two missed `DbConnection`/`DbCommand` shapes. The `MigrationBuilder` positive case already passed.

The follow-up now replaces weak `database`/`migration` terms with `dbconnection`, `dbcommand` and `migrationbuilder`, retaining SQL/DbContext/Dapper terms and the existing bounded query/statement matcher. The 36-case preset suite passed; restoring the old production terms made the same four tests fail; restoring the fix passed the broader security selection: **81 passed, two explicit hardware skips**. Evidence: `ART/sql-routing-terms-{red,green,mutation,restored}-tests.log`. These deterministic observations do not establish local-model quality.

All thirty frozen diffs were then replayed through the actual old and rebuilt classifier. SQL selection changed **from selected to not selected** in exactly three PRs: **#627 (group 2), #615 (group 5), #608 (group 7)**. Manual inspection found migration prose in #627's `Program.cs`, database prose in #615's `roundsLog.ts`, and help strings/test descriptions in #608; those fragments do not implement SQL. Other signal lists were unchanged. The replay retains input and assembly hashes, both outputs and the comparison under `ART/pr30-campaign/sql-terms-replay`. Completed group 2 and incomplete group 5 require new runtime-labelled reruns, both with **cloud ordinary reviewers and local Gemma security only**, recorded separately from the all-Gemma historical subtotal. Group 1 r2 used `cc9173dd`; this later SQL-term correction did not change any of its three predictions, so it does not require another rerun. Group 7 has not run and needs its updated prediction. No new-model campaign round has yet been counted from this classifier-only replay.

Cross-process engine waiting consumes the same reviewer deadline as generation. Launching several large PR rounds in parallel can therefore exhaust a reviewer before inference, despite per-process concurrency one. Large remaining PRs should execute sequentially within each group. Any further routing change requires another classifier replay of all thirty frozen diffs and reruns of affected groups; recorded predictions alone cannot replace manually checking relevance.

## 8. Consultant interactions and disposition

Actual COAI consultations were requested rather than replacing them with this session's own judgment. Earlier configured Claude/Opus advice helped identify the schema contradiction and source-selection limits. Key retained consultations:

| Consultation / evidence | Advice or observation | Verified disposition |
|---|---|---|
| `df969a82a9c34a76ae5b6040bcddacbb` | Schema permits notes but validator refuses; evidence survives in `AlsoSeenBy` | Contract corrected; evidence-retention claim checked in source |
| `fc975f1c8e914679b53a1fb5134b398b` | Unsupported claims and support files dominating source selection; real names make grep filtering insufficient | Reader/packer correction verified; quality remained unresolved; closed `not_solved` |
| `ec9473686211458fa8830ed7fb61d7fd` | Negative controls, distinguish tracing from willingness to abstain; compare historical contract; propose paired closed questions | Safe/unsafe and contract probes performed; no reliable end-to-end repair; closed `not_solved` |
| Later Gemma/campaign requests | Sent observations and quoted unsupported model output | Refused at existing **10 calls / 24 hours** caller-session cap; no new advice obtained |

Advice is not ground truth. A consultant's strong inference that a failed control proves no future prompt can work was not accepted; the experiments support a narrower failure statement. No consultation budget, identity, data directory, or history was reset to evade the cap. A refused consultation is recorded as unavailable, not agreement.

## 9. Product, harness, and model problems are different

| Problem | Category | Disposition at this checkpoint |
|---|---|---|
| Schema allowed fields validator rejected | Product contract | Corrected and regression-tested |
| Prompt examples interpreted as application code | Input framing / model behavior | Explicit boundaries and rules added; Qwen quality still failed |
| Supporting assets consumed source slots | Product context selection | Production/config prioritized; tested |
| `nit` accepted in security contract | Product contract | Excluded from schema and validation |
| Harness accepted merely reaching Ollama | Measurement | Usable-reply requirement added |
| Harness expected wrong summary wording | Measurement | Corrected without changing quality judgment |
| Missing fixture rules skipped Conventions | Measurement | Fixture corrected; expected reviewer count retained |
| Generic prose activated lane/SQL | Product routing | Both corrections implemented and regression-tested; follow-up affected campaign reruns still open |
| Schema-valid but invented finding details | Model semantic quality | Open; not hidden by filtering |
| Low thinking exhausted completion budget | Model/runtime interaction | Measured; bounded thinking returned content without curing false claims |
| Engine wait exhausted reviewer deadline | Scheduling / campaign execution | Observed in group 5; sequential large-PR rerun required |
| Consultant cap / cloud reviewer quota | Service availability | Recorded; not counted as model judgments |
| Old sessions crash while serializing missing lane sightings | Product backward compatibility | Reproduced and corrected at the finding properties; live retry remains required |

## 10. Validation, gates, and conclusions that remain open

CPU tests establish implementation behavior, not model quality. Earlier full Release MCP validation reported **6395 passed / 7 skipped**. Later full extension validation reported **4847 passed / 2 skipped**, clean lint. At committed routing head `cc9173dd`, the focused security suite had **76 passed / 2 explicit hardware skips**, and Debug build had zero warnings/errors. The SQL-term follow-up has **81 passed / 2 explicit hardware skips**, with the four failing-before observations recorded above; its Release build has zero warnings/errors. These follow-up observations concern the working-tree patch, not the older committed head.

Full-suite verification exposed a separate test-environment dependency: inherited `CODEX_SESSION_ID` made the consultant fixture choose the shipped Claude route instead of its fake Codex process, causing a 30-second timeout. The owned test process was stopped, preserving the log. Clearing only caller/session variables in the test child process made the affected method pass (one test, 4.249 seconds). The full rerun under that same isolated environment reached **6356 passed / 5 skipped / 0 failed**, then its global ten-minute deadline aborted it: this is incomplete, not a suite pass. A thirty-minute bounded full run is pending. Actual COAI caller identity and consultant limits were not altered. Evidence: `ART/sql-routing-terms-release-tests.log`, `ART/sql-routing-terms-consult-clean-env.log`, `ART/sql-routing-terms-release-clean-env-tests.log`, and `ART/sql-routing-terms-release-clean-env-30m-tests.log`.

Released MCP 0.40.3 compatibility was measured on Windows: the new setting is withheld, an accidentally sent unknown key is inert, and the new server accepts it. These compatibility results are unrelated to finding accuracy.

Own-feature `review_code` at predecessor `71c5dab0` returned **proceed, 8/8 answered**; fourteen findings were individually rejected and resolved. Some Gemini reviewers reported denied shell access, limiting their inspection. The first final feature attempt answered **0/1** because a cloud Codex reviewer hit quota; it was not a pass. Current/final-head code review and the admitted final feature retry remain pending, with the feature gate last.

At this report's checkpoint, current-head CI build/test/platform/extension/CodeQL checks passed; SonarCloud quality gate remained red (6.0% new-code duplication versus 3% allowed and security rating C versus A required). CodeRabbit skipped the draft; its successful check is not a performed review. These are independent acceptance conditions and must not be conflated with local-model calibration.

The data supports continuing with Gemma and narrower controlled experiments. It does **not** support saying the feature is secure, Qwen is intrinsically incapable, all thirty PRs completed, all checked prompts always run, or all detected positive findings have accurate reproductions. Next work: complete the routing correction and affected reruns; separate engine waiting from model failure; test positive finding fidelity with pinned safe/unsafe controls; finish the historical groups; then current-head code review and final feature gate.

### 10.1 A real code-gate crash exposed old-session incompatibility

The actual `review_code` attempt on committed `98b01d3a` failed before reviewers with a
`NullReferenceException`. The Native-AOT log points to generated
`ImmutableArraySecuritySightingSerializeHandler`, reached through a prior rejection while
`LiveRound.Persist` saves the session. Pre-lane findings lack `alsoSeenBy`; source-generated
deserialization supplies a default `ImmutableArray` despite its property initializer. Missing
`capReason` similarly becomes null. This is a product defect, not a model failure or a gate pass.

`SecuritySessionCompatibilityTests` reproduced it through the actual `SessionStore` and generated
JSON context: **two failed, one positive control passed**. Normalizing both values at their owning
`Finding` properties passed the focused security suite: **84 passed, two explicit hardware skips**.
Removing the normalization again failed the same two cases; restoring it passed all three
compatibility cases. Pending findings, rejection reasons and nonempty new sightings/caps survive
the roundtrip. No live session was reset or rewritten to avoid the failure.

Evidence: `ART/code-sql-terms-98b01d3a-result.json`,
`ART/security-session-{red,green,mutation,restored}-tests.log`, and installed preview log
`%LOCALAPPDATA%/coai-mcp/logs/2026-10-02/coai-mcp-13-46-49-46472.log`.
The full Release run predates this compatibility patch; a rebuilt Native-AOT live retry and
Release validation of the fix remain open.

## 11. Evidence index and continuation commands

| Evidence | Location |
|---|---|
| Earlier detailed chronology and initial protocol | [RESULTS_security_lane_qwen_windows.md](RESULTS_security_lane_qwen_windows.md) |
| Design and operator prompt catalog | [module_security_lane.md](module_security_lane.md), [security_prompt_catalog.md](security_prompt_catalog.md) |
| Benchmark transfer and Qwen resumed state | `ART/benchmark-transfer-evidence.json`, `ART/resumed-checkpoint-2026-10-02.json` |
| Frozen diagnostic requests, answers, finish reasons | `ART/qwen-*`, `ART/gemma-*`; individual families named above |
| Shortened prompt measurements | `ART/short-prompt-sizes.json`; Git assets `src_mcp/src/prompts/redteam-*.md` |
| Gemma feature/positive manifests | `ART/gemma-feature`, `ART/gemma-calibration-corrected-fixture` |
| Historical campaign | `ART/pr30-campaign/*-manifest.json`, `*.diff`, `*-scope.md`, requests/results, judgments, group assessments |
| Routing classifier replay | `ART/pr30-routing-probe/Program.cs`; product `DiffSplitter`/`SecuritySignals` |
| Committed routing regression evidence | `ART/pr30-routing-{red,green,mutation,restored-tests}.log` |
| SQL noun/type red, green and mutation checks | `ART/sql-routing-terms-{red,green,mutation,restored}-tests.log` |
| Consultant requests/replies | `ART/consult-*-result.json`, matching request and close files |

Run product tests through their MTP executable, not `dotnet test`. For live audit reproduction after obtaining the GPU lease or using the already authorized native fallback:

```powershell
$env:COAI_SECURITY_CALIBRATION_MODEL = 'Gemma4-26B-A4B-Uncensored_vk128:latest'
$env:COAI_SECURITY_CALIBRATION_OUT = 'D:/chosen-new-output/security-calibration'
& src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --explicit only --filter-class '*SecurityLaneCalibrationTests' --timeout 60m
```

For the bounded full Release suite, run the following in a **dedicated test child PowerShell process**. Clearing these variables isolates the consultant fixture; never apply it to actual COAI calls or use it to change their caller identity or budget.

```powershell
foreach ($callerVariable in @('COAI_CALLER_SESSION', 'CLAUDE_CODE_SESSION_ID', 'CODEX_SESSION_ID', 'GEMINI_CLI_SESSION_ID')) {
    [Environment]::SetEnvironmentVariable($callerVariable, $null, 'Process')
}
& src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests.exe --timeout 30m
```

Use a new artifact directory for each measurement. Record the actual implementation/fixture SHAs, prompt/schema hashes, model digest, requests and raw responses. Predict the distinction before running; inspect every finding before calling the result adequate. Preserve failed and superseded measurements separately. Do not infer missing data as zero, or turn a successful validator check into a security-quality score.

Document review of the earlier `f07fffb5` checkpoint returned `proceed`, **4/4 reviewers answered** (Codex Luna and Gemini Flash Medium, review and summary roles). Two findings were accepted as clarity improvements: concurrency applies per MCP process, and the future mixed-reviewer schedule is separate from measured all-Gemma results. Two were rejected with evidence: the executable's `--help` lists `--filter-class`, and `COAI_FEATURE_MIN_EPICS` counts epics rather than reviewers. All four decisions were recorded; that document session is `Done`.

A follow-up document review returned `proceed`, **4/4 answered**, with six findings resolved through
MCP. Five clarity improvements were accepted: changed SQL selection direction, historical group 2's
provisional routing status, mixed-reviewer rerun configuration, the exploratory run versus the
three-repeat streak, and the full-suite continuation command. The claim that §7.3 required another
group 1 rerun was rejected against the actual text and unchanged classifier predictions; its exact
runtime was nevertheless clarified. Both reviewers' summaries supported bounded continuation,
not a quality qualification. That session is `Done`, with six recorded decisions. The subsequent
product-transfer and compatibility-crash observations still need document review. Neither document
review is the pending final feature gate.
