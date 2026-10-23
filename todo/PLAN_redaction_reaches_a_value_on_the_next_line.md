# PLAN — Redaction reaches a value on the next line

> Status: **plan only, nothing implemented yet, 2026-10-23.** Scope: `src_mcp/core/Notices/Redaction.cs` (one new
> procedural pass beside `PrivateKeyBlocks`, one widened quantifier in the labelled rule), its mirror
> `src_vs_code/src/notifications.ts` (the quantifier only), the parity corpus `src_vs_code/src/test/noticeShapes.ts`,
> the tests on both sides, `research/module_core.md` and `research/module_tests.md`. No new caller, no new road: every
> call of `SafeText`, `SafeSource` and `FirstSecretClass` gets the cure by running the same passes.
>
> Found 2026-10-22 by the risk consult 722eeb66 (R1) on the spent-Kimi-quota work and verified with a RED there — recorded in
> [PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md](PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md) § *The feature
> gate* ("not reproduced for the JSON shape — a limit of Redaction itself"). PR #917's whole-text redaction cannot help,
> because the rule itself stops at a line's end.
>
> Related docs: [module_core.md](../research/module_core.md) (`Redaction`, `Redaction.SafeSource`, `PrivateKeyBlocks`
> rows), [module_tests.md](../research/module_tests.md), the security rule
> `.agents/conventions/common/security.md` § *A measure applied at SOME of its sites*.

## 1. The symptom

The shared redactor masks a credential that is NAMED by a label (`api_key`, `password`, `client_secret`, `X-Api-Key` —
the words of `shared/credential-words.json`) only when the label and its value stand on the SAME line. The labelled rule
(`Redaction.cs:102-103`, `notifications.ts:206`) is

```
([A-Za-z][A-Za-z0-9_.-]{0,63})("?[ \t]{0,4}[:=][ \t]{0,4}"?)([^\s"',;)}]{1,4096})
```

and its value class excludes every whitespace character, line breaks included. A value on the next line is never the
value of its label. Every shape below was run on 2026-10-23 through the shipped pass order copied verbatim (the regexes
of `notifications.ts:174-206`, the word list, both roads' character filters), with fake canaries. The two roads differ,
and the plan has to cure both:

- **`SafeSource` keeps the layout** (`Redaction.cs:322-324`: tab, LF and CR stay — `IsSourceCharacter`, `:327`). The
  line break is still there when the labelled rule runs, so EVERY next-line shape leaks.
- **`SafeText` drops every control character first** (`Redaction.cs:391`, `notifications.ts:237`). The line break is
  gone and the label and value are glued together WITH the next line's indentation between them. The joiner allows four
  spaces or tabs after the `:`/`=` — so an indentation of five or more spaces leaks (a tab is dropped and cannot leak).

| # | Shape (canaries are fake) | `SafeSource` road | `SafeText` road |
|---|---|---|---|
| R1 | JSON, value on the next line, 4-space indent: `{` LF `  "api_key":` LF `    "FAKE-CANARY-0001",` LF `}` | **leaks** | masked (the glued gap is 4) |
| R2 | JSON nested, 8-space indent: `"api_key":` LF `        "FAKE-CANARY-0002"` | **leaks** | **leaks** (gap 8 > 4) |
| R3 | YAML, top-level key, value indented: `api_key:` LF `  FAKE-CANARY-0003` | **leaks** | masked |
| R4 | YAML block scalar: `password: >` LF `  FAKE-CANARY-0004` | **leaks** (`>` is taken as the value) | **leaks** (same) |
| R5 | Folded header (RFC 7230 obs-fold), one space or a tab: `X-Api-Key:` LF ` FAKE-CANARY-0005` | **leaks** | masked |
| R6 | Folded header, five spaces: `X-Api-Key:` LF `     FAKE-CANARY-0006` | **leaks** | **leaks** |
| R7 | Shell continuation: `export TOKEN=\` LF `FAKE-CANARY-0007` | **leaks** (the `\` is redacted as the value) | masked |
| R8 | Label, CRLF, value: `client_secret=` CR LF `FAKE-CANARY-0008` | **leaks** | masked |
| R9 | A value split by CRLF (a wrapped line): `client_secret=FAKE-CANA` CR LF `RY-0009` | **leaks** the remainder `RY-0009` | masked |
| R10 | YAML nested under an ordinary key: `service:` LF `  api_key:` LF `    FAKE-CANARY-0010` | **leaks** | **leaks** |

Two findings beside the defect, recorded here because the measurement produced them:

- **A folded `Authorization:` header is already masked on both roads when its scheme word is `Bearer`, `Basic` or
  `Token`** — the bearer rule (`Redaction.cs:80-81`) matches `Bearer FAKE…` on its own line. The header shape leaks only
  for other header names (R5/R6) or another scheme word; a scheme word the rule does not know (`ApiKey FAKE…`) leaks on
  ONE line today as well (the labelled rule takes one token), which is a limit of the one-line rule and not of this plan.
- **R10 leaks on the `SafeText` road for a second reason, and so does the COMMON nested YAML `service:` LF
  `  api_key: FAKE-CANARY-0011` (value on the same line as its key).** After the line breaks are dropped the text is
  `service:  api_key: FAKE-CANARY-0011`; the leftmost match starts at `service`, an ordinary word, and its value
  `api_key:` swallows the credential's label, which is then never seen on its own. This is the mechanism the German
  row of `ServerNoticeLineTests.cs:197-198` pins as the one-line CONTRACT (`Fehler: password=letmein` is kept). It is
  not a line-bound defect; it is the one-line rule's leftmost-match defect, made frequent by the strip gluing lines.
  §3 D10 proposes its cure as a lead-gated story.

### Which callers carry such text today (verified call sites)

`SafeSource` — layout kept, every next-line shape leaks:

| Call site | What it feeds | Bound on the input |
|---|---|---|
| `src_mcp/runners/Feature/SourceResolver.cs:188` | a file served to a feature reviewer on demand — `appsettings*.json`, `config.yaml`, `docker-compose.yml`, a `.http` file with folded headers, a shell script; the NAME filter (`CredentialFiles`) refuses `.env*`, `*.pem`… and deliberately not these | `SourceBudget.WholeFileBytes` 16 Ki a file, 64 Ki a turn (`SourceBudget.cs:23,32`) |
| `src_mcp/runners/Feature/FeatureOutlineBuilder.cs:189,248` | a changed file's blob at head and its hunks | the blob |
| `src_mcp/core/Feature/FeatureContext.cs:58-61,134` | the feature context's plan, epics, lessons and gate history | `FeatureBudget` |
| `src_mcp/core/Security/SecuritySignals.cs:49,108` | every file text of the security lane | **`MaxFileCharacters` 262 144** (`:18`) — the largest real input any road gets |
| `src_mcp/src/Server/Consultation/ConsultantTurnBooks.cs:37` | a failed consultation's kept transcript (`--- stdout ---` / `--- stderr ---` of the vendor launch, `ConsultFailures.cs:52-55`) written to an evidence file | `EvidenceFile.Cap` 64 Ki (`EvidenceFile.cs:27`) |
| `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:430` | a consultant's advice — a model may quote a config it read | the answer |
| `src_mcp/runners/Acp/IAcpProfile.cs:149-151` (`AcpDiagnosis.LineWith`) | the WHOLE captured stderr of an ACP agent before the quoted line is isolated — the R1 site | `AcpTransport.StdErrChars` 64 Ki (`AcpTransport.cs:45`) |
| `src_mcp/src/Server/WorkspaceLookup.cs:389` | one search-hit LINE, cut at `LineChars` — a single line, so no next-line shape can occur here | one line |
| `src_mcp/core/QuestionConsult/SecretCheck.cs:63` → `Redaction.FirstSecretClass` (`Redaction.cs:349-359`) | the consultant context bound for an internet row — a secret on the next line is NOT recognised, so the context is SENT instead of refused | the context |

`SafeText` — one line; leaks R2, R4, R6 and R10 (and its same-line sibling):

| Call site | What it feeds |
|---|---|
| `src_mcp/runners/Reviewers/VendorReason.cs:31` | the vendor's reason as the round summary, the ledger and the log write it — a CLI's stderr tail, 4 000 characters |
| `src_mcp/runners/Acp/AcpMessages.cs:110` | an ACP error's `data` — JSON a vendor pretty-prints, 2 000 characters |
| `src_mcp/src/Server/HostCrash.cs:46,70,143` | an exception's text, HTTP bodies included, 16 Ki |
| `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:251,440` | a question row's notes and its reason (a CLI's stderr tail) |
| `src_mcp/src/Server/Consultation/OneShotTally.cs:51`, `ConsultationSweeper.cs:43,117`, `ConsultationFailedTurns.cs:128`, `RunLife.cs:133,147` | notes and failure messages |
| `src_mcp/runners/Kimi/KimiQuotaWords.cs:42`, `ConsultFailures.cs:57,82,107` | Kimi's reset clause; a vendor's FIRST line (`FirstLine` — a value on the next line is cut away, never quoted) |
| `src_mcp/src/Api/AskApiMode.cs:394`, `ProbeApiMode.cs:373` | an API error body, `\r`/`\n` replaced by a SPACE before `SafeText` — so the glued gap is one wider than the indentation |
| `src_mcp/core/Notices/ServerNoticeLine.cs:154`, `ServerNotice.cs:153` | every string field of every server notice |
| `src_vs_code/src/notifications.ts:324` | every string field of every extension notice — the only `safeText` call site in the extension |

## 2. Why

1. **The value class forbids a line break.** `[^\s"',;)}]{1,4096}` (`Redaction.cs:103`, spelled with `JsSpace` which
   holds LF and CR, `:69`) must match at least one character right after the joiner. On the `SafeSource` road the
   character after `api_key:` is LF, so the match fails at that label and the engine moves on; nothing later names the
   value. The joiner `"?[ \t]{0,4}[:=][ \t]{0,4}"?` admits only spaces and tabs around the `:`/`=`.
2. **`SafeText` drops the break and keeps the indentation.** `Passes` (`Redaction.cs:388-394`) filters with `IsPrintable`
   (`:255`: code unit ≥ 32 and ≠ 127) before any pass; `safeText` does the same (`notifications.ts:237`). LF, CR and TAB
   vanish, SPACES stay. The line break's indentation becomes the gap after the joiner, and `[ \t]{0,4}` is the bound the
   author chose so that `password     =x` (five spaces BEFORE the `=`) stays, pinned at `ServerNoticeLineTests.cs:166`.
3. **The redactor has exactly one multi-line shape and it is procedural.** `PrivateKeyBlocks` (`PrivateKeyBlocks.cs:36`)
   exists because "the five redaction patterns are ONE-LINE shapes" (`:9`); it runs first, keeps every line break and
   turns each body line into its indentation plus `[redacted]` (`:154-179`) so a served file's line count survives.
   Nothing like it exists for a label.
4. **The documentation says so.** `Redaction.SafeSource`'s remark: "the labelled pass therefore stops at each line's end
   exactly as it does in a notice" (`Redaction.cs:316-317`). The limit was known as a property and never measured as a
   leak until R1.

## 3. Decisions

**D1 — Two roads, two symptoms, two cures.** On the `SafeSource` road the symptom is the LINE BREAK; on the `SafeText`
road the line break is gone and the symptom is the GAP it left. One procedural pass cures the break (D2–D5); one widened
quantifier cures the gap (D6). Both run in the same pass list, so a caller changes nothing.

**D2 — The break is cured by a PROCEDURAL pass, `LabelContinuations`, not by letting the regex joiner cross a line.**
The tempting cure — `[:=][ \t]{0,4}(?:\r?\n[ \t]{0,32})?` in the joiner — was evaluated and REJECTED for a regression
it causes on the most common served shape, nested YAML with the value on its key's line:

```
service:
  api_key: FAKE-CANARY-0011
```

Today `SafeSource` masks this (`service:` fails at the LF; `api_key: FAKE…` matches on its own). With a joiner that
crosses the line, the leftmost match starts at `service`, crosses the break, takes `api_key:` as its VALUE, keeps it
(`service` is no credential) and the scan resumes after it — `FAKE-CANARY-0011` is never seen. A regex cannot know
that the label is a credential (that is the callback's job) and cannot compare indentation. The procedural pass can do
both: it reaches across the line ONLY for a label `CredentialWords.NamesACredential` admits, so an ordinary label never
swallows the next line. It follows `PrivateKeyBlocks` (`IndexOf`-driven, linear, layout kept) and the hand-rolled
`CredentialWords.Split` (`CredentialWords.cs:82-95`: "every repetition in that path has to be bounded"), which is the
repository's stated reason for procedural scanning over text nobody chose.

**D3 — The matching model: a credential label that ENDS a line continues onto the next line only with continuation
EVIDENCE.** At every line break the pass reads backwards over at most `[ \t]{0,4}`, an optional `\` or a YAML block
indicator `[|>][-+]?`, then `[ \t]{0,4}`, then `[:=]`, an optional `"`, `[ \t]{0,4}`, and the label — the LAST 64
label characters of the run `[A-Za-z0-9_.-]`, cut on the left to begin at a letter (what the regex's leftmost start
produces for `9key:`, which it masks as `key`). The label must name a credential. The next line continues the label when
at least one of these holds:

| Evidence | Covers |
|---|---|
| the next line is indented DEEPER than the label's line (`[ \t]` count, a tab counting one) | YAML (R3, R10's inner pair), nested JSON (R2), folded headers (R5, R6) |
| the next line begins, after its indentation, with `"` or `'` | pretty JSON with no indent — R1's exact shape (`{"api_key":` LF `"FAKE…"`) |
| the label's line ended with `\` | shell continuation (R7) |
| the label's line ended with `\|` or `>` (`[-+]?`) | a YAML block scalar (R4): every following line indented deeper than the label's line is value, until the first that is not or until `BlockLimit` characters |

Without evidence the pass declines and the text is untouched: `password:` LF `the one you chose last week` (a label
then prose), `the key` LF `to this is patience` (a label WORD with no joiner), a blank next line, no next line, a next
line that is itself a label with no deeper indent. Exactly ONE line break is crossed (`\r`, `\n` or `\r\n`); two in a
row decline. The declined case R8 (`client_secret=` CR LF `FAKE…`, no indent, no quote) is a recorded residual (§7, Q3).

**D4 — What is replaced, and how the layout survives.** On the continuation line the pass keeps the indentation and the
opening quote, replaces the VALUE TOKEN — the run of `[^JsSpace"',;)}]`, at most 4 096 characters, the regex's own
class and bound, so the tail past 4 096 stays "where both halves leave the tail behind" — with `[redacted]`, and keeps
the rest of the line. `"api_key":` LF `    "FAKE-CANARY-0001",` becomes `"api_key":` LF `    "[redacted]",`. A block
scalar's lines become indentation plus `[redacted]` each, as `PrivateKeyBlocks.AppendRedactedLines` does. No line
break is added or removed: `FeatureOutlineBuilder.SafeHunks` (`:248-249`) compares line counts, and the outline's spans
are counted in lines. An R7 shell line reads `export TOKEN=\` LF `[redacted]`; the labelled rule then also masks the `\`
(as today) — two placeholders, nothing readable.

**D5 — Order: after `PrivateKeyBlocks`, before the parameter pass, in `SecretsTaken` (`Redaction.cs:406-421`) — for
both roads and for `ClassOf` (`:362-376`).** After the block pass, so `"private_key":` LF `"-----BEGIN PRIVATE KEY-----`
has already lost its body before the pass takes `-----BEGIN` as a token (the 2026-09-26 defect, `PrivateKeyBlocks.cs:12`,
cannot recur). Before the regexes, so the placeholder is what they read (the labelled rule fails at the LF and does not
mask twice). On the `SafeText` road the text holds no line break by then (`Passes` strips first, `:391`), so the pass is
one `IndexOfAny` and a no-op — no branch per road, one order. `ClassOf` answers **`labelled`** when the pass changes the
text: it is the labelled rule's reach, not a new shape; `SecretCheckTests` pins the class. Under the same
`StopIfOverdue` as every pass (`:429-436`).

**D6 — The gap is cured in the regex, in both halves, byte for byte.** The joiner becomes

```
"?[ \t]{0,4}[:=][ \t]{0,32}(?:[|>][-+]?[ \t]{0,32})?"?
```

The gap AFTER the joiner widens from 4 to 32 (eight JSON levels at four spaces; the bound is a bound, not a measurement
— D7 says what is measured); the gap BEFORE it stays `{0,4}` so `password     =x` stays as pinned; the optional block
indicator makes `password: >  FAKE-CANARY-0004` (R4 after the strip) mask the value instead of the `>`. This is what
cures R2, R4 and R6 on the `SafeText` road, and the only part of the plan the extension carries (D9). The pattern
stays `NonBacktracking` — the labelled pattern built in 1 ms (`Redaction.cs:202`), the automaton's problem was the
bearer rule's alternation in front of its loop (`:195-197`), and this joiner has no alternation. If the generator
refuses it at build, the fallback is the backtracking engine with `BacktrackingCeilingMs` and a reason beginning
`NOT NonBacktracking because:` (the audit `EveryPattern_IsNonBacktrackingOrSaysWhyNot` admits either) — stated, not
assumed, in the story's summary.

**D7 — Linear and bounded, measured where it matters.** The pass has no regex: one forward scan for line breaks; at each
break at most ~80 characters are read backwards (bounded by the label's 64 and the joiner's gaps) and the continuation
line forward, each character of a consumed continuation or block visited once; a block is bounded by `BlockLimit` =
65 536 characters — `PrivateKeyBlocks.BodyLimit`'s value (`:48`), made ONE shared constant rather than a second copy
(reuse-first, step 2.4). Measured, as `TheRedactorCannotBeMadeToHangTests` measures (`Ceiling` 10 s, `:32`; the
corpus at `:34-58`), with new adversarial shapes: 200 KB of `key:` LF lines (every label declines, each once); `key: |`
LF then 200 KB of indented lines (one block, consumed once); 50 000 `key:` LF `  v` pairs; a 200 KB text with no line
break (the no-op road). And on the sizes the roads really see: `StdErrChars` 64 Ki (R1's road) and
`SecuritySignals.MaxFileCharacters` 262 144 — the largest real `SafeSource` input — both well under the existing
"130 ms or less over 200 KB" class (`Redaction.cs:111`). The whole-call promise ("about four seconds in the worst
case", `:152-158`) is unchanged.

**D8 — Fail-closed is unchanged.** `WhenRedactionTimesOut` (`:294-304`) still answers `[redacted]` for a regex ceiling
and `StopIfOverdue` still throws between passes; the new pass itself throws nothing — it cannot time out and therefore
has nothing to fail closed on, exactly like `PrivateKeyBlocks`. `FirstSecretClass` still answers `unredactable` on a
ceiling.

**D9 — The extension mirror and parity.** `notifications.ts:206` takes the D6 joiner byte for byte, and
`noticeShapes.ts` `SHAPES` gains R1–R10 with REAL line breaks and the two deep-indent shapes, so `run-parity.mjs`
compares `safeText` of both halves over them (the harness already carries real-LF shapes: `privateKey('RSA ', '\n')`).
The procedural pass is **server-only**, and that is a decision, not an omission: the extension has no layout-keeping
road — `safeText` is called from one place (`notifications.ts:324`) and strips every line break first, and
`redactPrivateKeyBlocks` is called only from `safeText` — so a TypeScript port would run only to be stripped. The
header remark of `Redaction.cs` ("this file is a PORT, not a re-design", `:9-14`) gets one paragraph naming the pass as
the one server-only step and why; `NoticeTool` is unchanged (`safe-text` already exercises `SecretsTaken` end to end on
real-LF inputs, where the pass is active before the strip removes nothing — see Q2 for the alternative).

**D10 — The swallow (R10 on the `SafeText` road, the German row) — a lead-gated story, S4.** Cure: a negative
lookahead before the value, `(?!"?[A-Za-z][A-Za-z0-9_.-]{0,63}"?[ \t]{0,4}[:=])` — "the value is not itself a
label" — so the match at `service` or `Fehler` fails and the engine finds `api_key:`/`password=` on its own. It FLIPS
the pinned German row (`password=letmein` becomes `password=[redacted]`, the better direction) and keeps every other
pinned case (`Set-Cookie: session=abc`: `Set-Cookie`'s value `session=` is a label, the match fails, `session` is no
credential, unchanged; `?sig=abc&author=me` unchanged, its value begins with `[`). Cost: a lookaround takes the pattern
off `NonBacktracking` (unsupported there, as the Moonshot rule's look-ahead already is, `:190-192`), so it gains
`BacktrackingCeilingMs` and a reason; the backtracking cost stays one try per start (label characters never contain
`:`/`=`, the value is greedy with nothing after it), measured on the corpus. Both halves, parity, the German row's test
rewritten with the reason. Recommended; gated on Q1 because it changes a pinned contract and an engine.

**D11 — Residuals this plan records and does not cure.** (a) R9, a value WRAPPED mid-way: the remainder on the next
line is indistinguishable from the next line of a file; no evidence, no reach. (b) R8 without indent or quote. (c) The
two API quote sites that replace line breaks by a SPACE before `SafeText` (`AskApiMode.cs:394`, `ProbeApiMode.cs:373`)
get D6's cure only (the gap is indentation + 1). (d) A scheme word the bearer rule does not know after `Authorization:`
(one-line limit). (e) Two line breaks between label and value.

## 4. Scenarios (each RED is the message before the fix)

C# through `Redaction.SafeSource` / `SafeText` / `FirstSecretClass` and, for S-9, the product (`TheStderrLineIsQuotedSafelyTests.ThroughTheProduct`).

| # | Given | Expected | RED today |
|---|---|---|---|
| S-1 | R1 through `SafeSource` | `{` LF `  "api_key":` LF `    "[redacted]",` LF `}` — same line count | `Did not expect text "{\n  \"api_key\":\n    \"FAKE-CANARY-0001\",\n}" to contain "FAKE-CANARY-0001".` |
| S-2 | R2 through `SafeText` (DetailLimit) | `{  "nested": {    "api_key":        "[redacted]"  }}` | `Expected text to be "…\"api_key\":        \"[redacted]\"…", but "…\"api_key\":        \"FAKE-CANARY-0002\"…" differs near "FAK" (index 46).` |
| S-3 | R3, R5, R6, R7, R10's inner pair through `SafeSource` | each value `[redacted]`, indentation kept, `\` kept | `Did not expect … to contain "FAKE-CANARY-0003"` (one row per shape) |
| S-4 | R4 through `SafeSource` and through `SafeText` | `password: >` LF `  [redacted]`; `password: >  [redacted]` | `Did not expect "password: [redacted]\n  FAKE-CANARY-0004\n" to contain "FAKE-CANARY-0004".` |
| S-5 | the declines: `password:` LF `the one you chose last week`; `the key` LF `to this is patience`; `api_key:` LF LF `  x`; `api_key:` at end of text; `service:` LF `  api_key: FAKE-CANARY-0011` | first four UNCHANGED; the fifth `service:` LF `  api_key: [redacted]` | green today and MUST stay green — the fifth is the test that goes red under a crossing joiner (D2): `Did not expect … to contain "FAKE-CANARY-0011".` |
| S-6 | every pinned row of `ServerNoticeLineTests` (`:131-213`), `password     =x` included | unchanged | green before and after |
| S-7 | R1 through `FirstSecretClass` | `labelled` | `Expected text to be "labelled" with a length of 8, but "" has length 0.` |
| S-8 | the D7 adversarial shapes through `SafeText`/`SafeSource` | answered under `Ceiling`; a 262 144-character YAML of `key:` LF `  v` pairs under 1 s | green before (the pass does not exist) — the row is the guard the pass ships under |
| S-9 | an ACP stderr of 64 Ki whose first lines are R1 and whose last holds the monthly phrase, through the product | no canary fragment in the diagnosis line, the report JSON, the summary, the ledger, the log | `Did not expect seen.Report "…" to contain "FAKE-CANARY-0001" (the shim's report JSON).` |
| S-10 | (S4, gated) the German row and `service:  api_key: FAKE-CANARY-0011` through `SafeText` | `Fehler: password=[redacted] …`; `service:  api_key: [redacted]` | `Expected text to be "Fehler: password=[redacted] für …", but "Fehler: password=letmein für …" differs near "let" (index 17).` |

TypeScript (`notifications.test.ts`, `safeText`): R2 and R4 as S-2/S-4 — RED `assert.ok(!safe.includes('FAKE-CANARY-0002'))`
fails with the glued text; `noticeShapes.ts` corpus rows for R1–R10 and the two deep-indent shapes — `run-parity.mjs`
RED on a one-sided change: `safeText at 4096: N of M cases differ — one half of this product would write a secret the
other would have taken out`.

## 5. Build order

One epic, four stories. **S1 is the cornerstone**: the matching model and its declines (D3) are the whole security
argument, and a wrong decline is a leak while a wrong reach is a regression on served files.

1. **S1 — `LabelContinuations` (server).** RED: S-1, S-3, S-4 (`SafeSource` half), S-5 (all five, the fifth as the
   regression guard), S-7, S-8. New file `src_mcp/core/Notices/LabelContinuations.cs` (procedural, `internal static`,
   the shared `BlockLimit`), wired into `SecretsTaken` and `ClassOf` after the block pass; `PrivateKeyBlocks.BodyLimit`
   reads the shared constant. Test file `src_mcp/tests/ALabelledValueOnTheNextLineIsRedactedTests.cs`; rows in
   `TheRedactorCannotBeMadeToHangTests` and `SecretCheckTests`. GREEN; break-it: remove the deeper-indent evidence and
   watch S-3's YAML row go red; remove the credential check and watch S-5's fifth row go red.
2. **S2 — the joiner's gap and block indicator (both halves).** RED: S-2, S-4 (`SafeText` half) in C#; the two
   `notifications.test.ts` cases; the corpus rows, with `run-parity.mjs` red when only one half has changed. Change
   `LabelledPattern` and `LABELLED`, byte for byte; confirm the `NonBacktracking` build (D6) or record the fallback.
   GREEN on `CoaiMcp.Tests`, `npm test`, `npm run test:parity`.
3. **S3 — the product check and the docs.** S-9 through `TheStderrLineIsQuotedSafelyTests`; `research/module_core.md`:
   a `LabelContinuations` row beside `PrivateKeyBlocks`, the `Redaction.SafeSource` row's "stops at each line's end"
   corrected, the labelled rule's new joiner in the `Redaction` rows; `research/module_tests.md` rows for the new
   tests with their RED messages; the `SafeSource` remark at `Redaction.cs:316-317` rewritten; one pointer from the
   Kimi plan's R1 bullet to this plan. Promote this plan (`/promote-plan`) when S1–S3 ship and Q1 is answered.
4. **S4 — the swallow (gated on Q1).** RED: S-10, the German row's expectation flipped; `EveryPattern_…` sees the
   pattern leave `NonBacktracking` with its reason; the hang test's labelled row re-measured. Both halves, parity.

Releases: one coai-mcp release carries S1–S3 (the server masks every served and quoted shape); one extension release
carries S2's mirror. The server release is the one that closes R1; the extension's cures only R2/R4/R6 in its own notices.

## 6. Test plan

- **C# — the test EXECUTABLE, never `dotnet test`** (xunit.v3 on Microsoft Testing Platform, `CoaiMcp.Tests.csproj:8`;
  CI does the same, `ci.yml:55-58`):
  `dotnet build dew_flow_connect_other_ais.slnx` then
  `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ALabelledValueOnTheNextLine*"`,
  `… --filter-class "*ServerNoticeLineTests"`, `… --filter-class "*TheRedactorCannotBeMadeToHang*"`,
  `… --filter-class "*SecretCheckTests"`, `… --filter-class "*TheStderrLineIsQuotedSafely*"`; then the whole
  executable once, at lowered priority if other suites run on the machine (the Kimi plan's note on wall-clock tests).
- **Extension:** `cd src_vs_code && npm test` (compiles, then `scripts/run-tests.mjs` over `out/test`);
  `npm run test:parity` (BUILDS `NoticeTool` first — `run-parity.mjs` says why a found DLL is not enough — then
  compares `safeText` over the corpus byte for byte and the notice lines through the parser).
- **RED-GREEN-RED on both sides.** Every scenario's RED message is observed on the unfixed code and recorded in
  `module_tests.md`; after GREEN, the break-its of S1 (D3's evidence removed; the credential check removed) and the
  one-sided parity red of S2 are run and recorded. S-5 and S-6 are the regression guards and must be green BEFORE the
  change too — a guard that was red before proves the wrong thing.
- **Measurement** (D7): the adversarial rows' elapsed time printed in the test's own message, at the two real sizes.

## 7. Risks

| Risk | Mitigation |
|---|---|
| Performance on large text — a 262 144-character file of label lines, or a block scalar that never ends | the pass is linear by construction (each character consumed once, backward reads bounded to ~80), the block bounded by `BlockLimit`; S-8 measures at the two real sizes under the existing hang ceiling; the whole-call ceiling stands |
| False positives on served files — an ordinary word before a colon at a line's end, a markdown heading `## API key:` followed by indented prose | reach only for a credential-named label AND with evidence (D3); `## API key:` LF `Here is …` declines (no indent, no quote). A credential label followed by an indented paragraph masks the paragraph's first token — the same one token the one-line rule masks today, recorded as expected |
| A regression on the most common served shape (nested YAML) | D2 rejected the crossing joiner for exactly this; S-5's fifth row is the permanent guard |
| The parity mirror drifting — a server-only pass is a second place where the two halves legitimately differ | the pass runs BEFORE the strip only in effect, never in the extension's road; the corpus rows exercise both halves' `safeText` over real-LF shapes so an extension-side change to the strip or the regex is caught; the header remark names the asymmetry (D9) |
| `NonBacktracking` refuses the widened joiner at build | the fallback is the backtracking engine with the ceiling and a reason (D6); the audit test admits it; decided at build time and written in the summary |
| D6 masks more one-line text (`key:        value`-style aligned tables where a credential word labels a value) | that is the rule's purpose; the pre-joiner gap keeps the pinned negative; the corpus rows show every new positive |
| S4 flips a pinned contract and moves a pattern off `NonBacktracking` | gated on Q1; its own RED, its own measurement, both halves at once |

## 8. Questions for the lead (assumed where marked)

- **Q1 — Fold S4 (the swallow, D10) into this epic?** It is the `SafeText` road's REAL everyday leak (nested YAML on one
  glued line) and changes the German row's pinned contract. *Assumed: planned as a gated story; not built until answered.*
- **Q2 — Mirror `LabelContinuations` into TypeScript although nothing there keeps a layout?** A port that runs only to be
  stripped is drift waiting to happen; a comment naming the asymmetry is the cheaper guard. *Assumed: server-only (D9).*
- **Q3 — Reach without evidence for `label=` CR LF `value` (R8)?** Dotenv never writes it; a `.http`/JSON/YAML writer
  always indents or quotes. *Assumed: declined and recorded (D11 b).*
- **Q4 — The post-joiner gap bound: 32 or 64?** 32 is eight JSON levels at four spaces. *Assumed: 32.*
- **Q5 — Class name for the secret check: `labelled` or a new `labelled-continuation`?** One rule reaching one line
  further argues for one class; a separate class would let `SecretCheckTests` tell them apart. *Assumed: `labelled`.*

## 9. Definition of Done

- [ ] S-1…S-9 written RED first, each RED message observed on the unfixed code and recorded in `research/module_tests.md`;
      then GREEN; then the two S1 break-its and S2's one-sided parity red observed and recorded (RED-GREEN-RED, both sides).
- [ ] S-5 and S-6 green before AND after — no pinned negative changed, nested YAML with the value on its key's line still
      masked on the `SafeSource` road.
- [ ] `LabelContinuations` is procedural, after `PrivateKeyBlocks`, before the parameter pass, in `SecretsTaken` and
      `ClassOf`; `BlockLimit` is one shared constant; the hang test's new rows run under `Ceiling` at 64 Ki and 262 144.
- [ ] The labelled joiner is identical in `Redaction.cs` and `notifications.ts`; `npm run test:parity` green over the
      corpus with R1–R10 and the deep-indent rows; the `NonBacktracking` outcome of D6 stated in the story summary.
- [ ] `CoaiMcp.Tests.exe` (whole), `npm test`, `npm run test:parity` green; run as executables, never `dotnet test`.
- [ ] `research/module_core.md`: `LabelContinuations` row, the `Redaction.SafeSource` row and the `Redaction.cs:316-317`
      remark no longer say the labelled pass stops at a line's end; the joiner documented; the server-only asymmetry
      named. `research/module_tests.md` rows for every new test.
- [ ] The Kimi plan's R1 bullet points here; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
- [ ] Q1 answered; S4 built or extracted into its own `todo/` plan; this plan promoted to `research/` with its
      deviations and residuals (D11) recorded.
- [ ] No real-looking secret in any fixture (fake canaries only); the hygiene scan on the diff prints nothing.
