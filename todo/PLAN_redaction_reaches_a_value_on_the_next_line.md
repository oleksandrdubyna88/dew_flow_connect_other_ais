# PLAN — Redaction reaches a value on the next line

> Status: **partly built, 2026-10-23 — S1, S2, S2b and S2c are built (see *S1+S2*, *S2b* and *S2c — status* after the build order); S3 (the product
> checks, the docs, Q6) and S4 (the gated swallow) are open.** Revised the same day after the coai plan round (a0bc0720,
> proceed) and two consults (cadence a1c21b20, risk f1d48728); every point verified against the code and folded (see the
> last section). Scope: `src_mcp/core/Notices/Redaction.cs` (one new procedural pass beside `PrivateKeyBlocks`, one
> widened joiner in the labelled rule, the pass order of BOTH roads), its mirror `src_vs_code/src/notifications.ts` and a
> new `src_vs_code/src/labelContinuations.ts` (the pass, ported byte for byte), the parity corpus
> `src_vs_code/src/test/noticeShapes.ts`, the tests on both sides, `research/module_core.md` and
> `research/module_tests.md`. No new caller, no new road: every call of `SafeText`, `SafeSource` and `FirstSecretClass`
> gets the cure by running the same passes.
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
  spaces or tabs after the `:`/`=` — so an indentation of five or more spaces leaks (a tab is dropped and cannot leak) —
  and a value of SEVERAL lines (a block scalar) keeps every line but the first readable, because the rule masks one token.

| # | Shape (canaries are fake) | `SafeSource` road | `SafeText` road |
|---|---|---|---|
| R1 | JSON, value on the next line, 4-space indent: `{` LF `  "api_key":` LF `    "FAKE-CANARY-0001",` LF `}` | **leaks** | masked (the glued gap is 4) |
| R2 | JSON nested, 8-space indent: `"api_key":` LF `        "FAKE-CANARY-0002"` | **leaks** | **leaks** (gap 8 > 4) |
| R3 | YAML, top-level key, value indented: `api_key:` LF `  FAKE-CANARY-0003` | **leaks** | masked |
| R4 | YAML block scalar: `password: >` LF `  FAKE-CANARY-0004` | **leaks** (`>` is taken as the value) | **leaks** (same) |
| R4b | YAML block scalar of TWO lines: `password: \|` LF `  FAKE-FIRST` LF `  FAKE-SECOND` | **leaks** both | **leaks** both; with the gap cure alone (D6) still leaks the second (cadence C1) |
| R5 | Folded header (RFC 7230 obs-fold), one space or a tab: `X-Api-Key:` LF ` FAKE-CANARY-0005` | **leaks** | masked |
| R6 | Folded header, five spaces: `X-Api-Key:` LF `     FAKE-CANARY-0006` | **leaks** | **leaks** |
| R7 | Shell continuation: `export TOKEN=\` LF `FAKE-CANARY-0007` | **leaks** (the `\` is redacted as the value) | masked |
| R8 | Label, CRLF, value: `client_secret=` CR LF `FAKE-CANARY-0008` | **leaks** | masked |
| R9 | A value split by CRLF (a wrapped line): `client_secret=FAKE-CANA` CR LF `RY-0009` | **leaks** the remainder `RY-0009` | masked |
| R10 | YAML nested under an ordinary key: `service:` LF `  api_key:` LF `    FAKE-CANARY-0010` | **leaks** | **leaks** |
| R11 | TOML triple-quoted: `password = """` LF `FAKE-CANARY-0011` LF `"""` | **leaks** | **leaks** (`"""` is not the joiner's one optional quote) |

Three findings beside the defect, recorded here because the measurement produced them:

- **A folded `Authorization:` header is already masked on both roads when its scheme word is `Bearer`, `Basic` or
  `Token`** — the bearer rule (`Redaction.cs:80-81`) matches `Bearer FAKE…` on its own line. The header shape leaks only
  for other header names (R5/R6) or another scheme word; a scheme word the rule does not know (`ApiKey FAKE…`) leaks on
  ONE line today as well (the labelled rule takes one token), which is a limit of the one-line rule and not of this plan.
- **R10 leaks on the `SafeText` road for a second reason, and so does the COMMON nested YAML `service:` LF
  `  api_key: FAKE-CANARY-0012` (value on the same line as its key).** After the line breaks are dropped the text is
  `service:  api_key: FAKE-CANARY-0012`; the leftmost match starts at `service`, an ordinary word, and its value
  `api_key:` swallows the credential's label, which is then never seen on its own. This is the mechanism the German
  row of `ServerNoticeLineTests.cs:197-198` pins as the one-line CONTRACT (`Fehler: password=letmein` is kept). It is
  not a line-bound defect; it is the one-line rule's leftmost-match defect, made frequent by the strip gluing lines.
  §3 D10 proposes its cure as a lead-gated story.
- **A single- or triple-quoted value on ONE line is not masked today either:** `password='FAKE'` and
  `password = """FAKE"""` — the joiner admits one `"`, the value class excludes `"` and `'`, so the match fails at the
  quote. One-line, both halves; folded into D6 because R11's cure needs the same quote handling.

### Which callers carry such text today (verified call sites)

`SafeSource` — layout kept, every next-line shape leaks:

| Call site | What it feeds | Bound on the input |
|---|---|---|
| `src_mcp/runners/Feature/SourceResolver.cs:188` | a file served to a feature reviewer on demand — `appsettings*.json`, `config.yaml`, `docker-compose.yml`, a `.http` file with folded headers, a shell script, a `.toml`; the NAME filter (`CredentialFiles`) refuses `.env*`, `*.pem`… and deliberately not these | `SourceBudget.WholeFileBytes` 16 Ki a file, 64 Ki a turn (`SourceBudget.cs:23,32`) |
| `src_mcp/runners/Feature/FeatureOutlineBuilder.cs:189,248` | a changed file's blob at head and its hunks | the blob |
| `src_mcp/core/Feature/FeatureContext.cs:58-61,134` | the feature context's plan, epics, lessons and gate history | `FeatureBudget` |
| `src_mcp/core/Security/SecuritySignals.cs:49,108` | every file text of the security lane | **`MaxFileCharacters` 262 144** (`:18`) — the largest real input any road gets |
| `src_mcp/src/Server/Consultation/ConsultantTurnBooks.cs:37` | a failed consultation's kept transcript (`--- stdout ---` / `--- stderr ---` of the vendor launch, `ConsultFailures.cs:52-55`) written to an evidence file | `EvidenceFile.Cap` 64 Ki (`EvidenceFile.cs:27`) |
| `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:430` | a consultant's advice — a model may quote a config it read | the answer |
| `src_mcp/runners/Acp/IAcpProfile.cs:149-151` (`AcpDiagnosis.LineWith`) | the WHOLE captured stderr of an ACP agent before the quoted line is isolated — the R1 site | `AcpTransport.StdErrChars` 64 Ki (`AcpTransport.cs:45`) |
| `src_mcp/src/Server/WorkspaceLookup.cs:389` | one search-hit LINE, cut at `LineChars` — a single line, so no next-line shape can occur here | one line |
| `src_mcp/core/QuestionConsult/SecretCheck.cs:63` → `Redaction.FirstSecretClass` (`Redaction.cs:349-359`) | the consultant context bound for an internet row — a secret on the next line is NOT recognised, so the context is SENT instead of refused | the context |

`SafeText` — one line; leaks R2, R4, R4b, R6, R10, R11 (and R10's same-line sibling). **Every server notice reaches the
extension's page on this road**: the server writes `server-notices.jsonl` through `ServerNoticeLine` and the extension
reads it back with its own parser (`notificationsFile.ts:108-110`, `readServerNotices`) and renders it — the page
escapes HTML and redacts nothing again, so what `SafeText` lets through is what the person sees (cadence C1).

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
   value. The joiner `"?[ \t]{0,4}[:=][ \t]{0,4}"?` admits only spaces and tabs around the `:`/`=`, and one `"`.
2. **`SafeText` drops the break and keeps the indentation — and the information that there WAS a break.** `Passes`
   (`Redaction.cs:388-394`) filters with `IsPrintable` (`:255`: code unit ≥ 32 and ≠ 127) before any pass; `safeText`
   does the same (`notifications.ts:237`). LF, CR and TAB vanish, SPACES stay. The line break's indentation becomes the
   gap after the joiner, and `[ \t]{0,4}` is the bound the author chose so that `password     =x` (five spaces BEFORE
   the `=`) stays, pinned at `ServerNoticeLineTests.cs:166`. A value of several lines becomes several tokens, of which the
   rule masks the first — once the breaks are gone no pass can tell the second line of a block scalar from prose.
3. **The redactor has exactly one multi-line shape and it is procedural.** `PrivateKeyBlocks` (`PrivateKeyBlocks.cs:36`)
   exists because "the five redaction patterns are ONE-LINE shapes" (`:9`); it keeps every line break and turns each body
   line into its indentation plus `[redacted]` (`:154-179`) so a served file's line count survives. On the `SafeSource`
   road it runs first on the raw text; on the `SafeText` road it runs AFTER the strip (`:391` then `:408`), on one glued
   line. Nothing like it exists for a label.
4. **The documentation says so.** `Redaction.SafeSource`'s remark: "the labelled pass therefore stops at each line's end
   exactly as it does in a notice" (`Redaction.cs:316-317`). The limit was known as a property and never measured as a
   leak until R1.
5. **`ClassOf` has no deadline between its passes.** `Redaction.cs:362-376` runs the block pass, the parameter pass, the
   shapes and the labelled pass with no `StopIfOverdue` — the per-pattern ceilings are the only bound there (risk R3).

## 3. Decisions

**D1 — Two roads, one symptom seen twice, one pass that runs BEFORE the strip.** On the `SafeSource` road the line break
is present when the regexes run; on the `SafeText` road it is gone and only its indentation is left. The first draft
cured the second face with a wider regex gap alone, and the cadence consult (C1) showed that cannot work for a value of
SEVERAL lines (R4b): once the breaks are stripped the second line is a token like any other. So the procedural pass
(D2–D5) runs on BOTH roads before the strip, in both halves (D9), and the regex gap (D6) stays as the cure for text a
caller glued BEFORE handing it over (`AskApiMode.cs:394`, `ProbeApiMode.cs:373`) and for one-line gaps.

**D2 — The break is cured by a PROCEDURAL pass, `LabelContinuations`, not by letting the regex joiner cross a line.**
The tempting cure — `[:=][ \t]{0,4}(?:\r?\n[ \t]{0,32})?` in the joiner — was evaluated and REJECTED for a regression
it causes on the most common served shape, nested YAML with the value on its key's line:

```
service:
  api_key: FAKE-CANARY-0012
```

Today `SafeSource` masks this (`service:` fails at the LF; `api_key: FAKE…` matches on its own). With a joiner that
crosses the line, the leftmost match starts at `service`, crosses the break, takes `api_key:` as its VALUE, keeps it
(`service` is no credential) and the scan resumes after it — `FAKE-CANARY-0012` is never seen. A regex cannot know
that the label is a credential (that is the callback's job) and cannot compare indentation. The procedural pass can do
both: it reaches across the line ONLY for a label `CredentialWords.NamesACredential` admits, so an ordinary label never
swallows the next line. It follows `PrivateKeyBlocks` (`IndexOf`-driven, linear, layout kept) and the hand-rolled
`CredentialWords.Split` (`CredentialWords.cs:82-95`: "every repetition in that path has to be bounded"), which is the
repository's stated reason for procedural scanning over text nobody chose.

**D3 — The matching model: a credential label that ENDS a line continues onto the next line only with continuation
EVIDENCE, and never onto a child that is itself an assignment.** At every line break the pass examines the line just
passed: at most `[ \t]{0,4}` of trailing space, an optional `\`, an optional YAML block indicator `[|>][-+]?[0-9]?`, an
optional opening quote run `"{1,3}` or `'{1,3}`, `[ \t]{0,32}`, `[:=]`, an optional `"`, `[ \t]{0,4}`, and the label —
the LAST 64 label characters of the run `[A-Za-z0-9_.-]`, cut on the left to begin at a letter (what the regex's
leftmost start produces for `9key:`, which it masks as `key`). The label must name a credential. Five kinds of evidence:

| Evidence | Kind | Covers |
|---|---|---|
| the next line is indented DEEPER than the label's line (`[ \t]` count, a tab counting one) | plain | YAML (R3, R10's inner pair), nested JSON (R2), folded headers (R5, R6) |
| the next line begins, after its indentation, with `"` or `'` | plain | pretty JSON with no indent — R1's exact shape (`{"api_key":` LF `"FAKE…"`) |
| the label's line ended with `\` | plain | shell continuation (R7) |
| the label's line ended with `\|` or `>` (`[-+]?[0-9]?`) | block | a YAML block scalar (R4, R4b): every following line that is BLANK or indented deeper than the label's line is value, until the first non-blank line that is not deeper, or the end of the text (risk R2: a blank line inside a block continues it; `ordinary: visible` after the block survives) |
| the label's line ended with `"""` or `'''` | block | TOML / Python triple-quoted (R11): every following line is value until the line holding the closing triple quote (kept, its text before the quotes masked), or the end of the text |

**The child-assignment rule (risk R1).** A PLAIN continuation declines when the next line, after its indentation and an
optional quote, is itself a label assignment — `[A-Za-z][A-Za-z0-9_.-]{0,63}"?[ \t]{0,4}[:=]`. The reason is a
credential-NAMED container: `credentials:` LF `  password: FAKE-CANARY-CHILD` — `credentials` names a credential and
the child is deeper, so without this rule the pass would mask the TOKEN `password:` and leave the canary without the
label the labelled rule needs; today the one-line rule masks it. With the rule the pass leaves the child to the labelled
rule, which masks `password: [redacted]`. Inside a BLOCK (indicator or triple quote) every line is value, assignment-shaped
or not — `password: |` LF `  user=x` LF `  pass=y` is a secret of two lines.

Without evidence the pass declines and the text is untouched: `password:` LF `the one you chose last week` (a label
then prose), `the key` LF `to this is patience` (a label WORD with no joiner), a blank next line after a plain joiner,
no next line, a next line that is a label with no deeper indent. A PLAIN continuation crosses exactly ONE line break
(`\r`, `\n` or `\r\n`); two in a row decline. The declined case R8 (`client_secret=` CR LF `FAKE…`, no indent, no
quote) is a recorded residual (§7, Q3).

**D4 — What is replaced, how the layout survives, and the fail-closed end of a block (plan round P1).** On a plain
continuation line the pass keeps the indentation and the opening quote, replaces the VALUE TOKEN — the run of
`[^JsSpace"',;)}]`, at most 4 096 characters, the regex's own class and bound, so the tail past 4 096 stays "where both
halves leave the tail behind" — with `[redacted]`, and keeps the rest of the line. `"api_key":` LF `    "FAKE-CANARY-0001",`
becomes `"api_key":` LF `    "[redacted]",`. A block's lines become indentation plus `[redacted]` each (a blank line
stays blank), as `PrivateKeyBlocks.AppendRedactedLines` does. **A block has NO length bound of its own and therefore no
overflow:** the first draft stopped a block at `BlockLimit` 65 536 characters and the plan round showed the tail of a
longer scalar inside a 262 144-character security-lane file would stay readable. A block is ended by its STRUCTURE — the
first non-blank line not indented deeper, the closing triple quote — or by the end of the text, and a cursor that
consumes every line once needs no cap to stay linear (D7). There is no "bound reached" state to fail closed from; the
assertion that pins it is S-11 (no canary anywhere past the old boundary). No line break is added or removed:
`FeatureOutlineBuilder.SafeHunks` (`:248-249`) compares line counts, and the outline's spans are counted in lines. An R7
shell line reads `export TOKEN=\` LF `[redacted]`; the labelled rule then also masks the `\` (as today) — two placeholders,
nothing readable.

**D5 — Order: after `PrivateKeyBlocks`, before the strip, before the parameter pass — on BOTH roads, in BOTH halves, and
in `ClassOf`.** The pass order of `SafeText` becomes the pass order of `SafeSource` with the strip in the middle:

```
PrivateKeyBlocks → LabelContinuations → [SafeText only: drop every control character] → parameter → the four shapes → labelled → [SafeText only: the cut]
```

After the block pass, so `"private_key":` LF `"-----BEGIN PRIVATE KEY-----` has already lost its body before the pass
could take `-----BEGIN` as a token (the 2026-09-26 defect, `PrivateKeyBlocks.cs:12`, cannot recur). Before the strip, so
the pass sees the line breaks on the notice road too (C1). Before the regexes, so the placeholder is what they read (the
labelled rule fails at the LF and does not mask twice). **One deliberate output change follows:** the block pass moves
ahead of the strip on the `SafeText` road, so a private key with REAL line breaks quoted in a notice becomes its header,
the body's indentation and one `[redacted]` PER BODY LINE glued together, then the footer — instead of one placeholder
between the markers. No test pins the single placeholder (`APrivateKeyBlockIsNeverServedTests.cs:249,303` and
`privateKeyBlocks.test.ts:110,131` assert only that the body marker is gone); the parity corpus row
`privateKey('RSA ', '\n')` changes identically in both halves; an escaped one-line block (`\n` as two characters) is
unchanged. `ClassOf` (`Redaction.cs:362-376`) takes the same order — block, continuation, parameter, shapes, labelled —
and answers **`labelled`** when the continuation pass changes the text: it is the labelled rule's reach, not a new
shape; `SecretCheckTests` pins the class. Precedence, pinned as rows (risk R3): a PEM block whose label continues
(`"private_key":` LF `"-----BEGIN…`) answers `private-key`; a continuation beside a query credential answers
`labelled`; a query credential alone answers `parameter`. **`ClassOf` also gains the clock** (`Stopwatch` +
`StopIfOverdue` between its passes, as `SecretsTaken` has at `:409-418`), closing the gap §2.5 names — the whole-call
promise of `:152-158` then holds for `FirstSecretClass` too.

**D6 — The gap and the quotes are cured in the regex, in both halves, byte for byte.** The joiner becomes

```
["']{0,3}[ \t]{0,4}[:=][ \t]{0,32}(?:[|>][-+]?[0-9]?[ \t]{0,32})?["']{0,3}
```

The gap AFTER the joiner widens from 4 to 32 (eight JSON levels at four spaces; the bound is a bound, not a
measurement — D7 says what is measured); the gap BEFORE it stays `{0,4}` so `password     =x` stays as pinned; the
optional block indicator makes `password: >  FAKE-CANARY-0004` (R4 after a caller's own gluing) mask the value instead
of the `>`; the quote run `["']{0,3}` masks `password='FAKE'`, `password = """FAKE"""` and `'client_secret': 'FAKE'`,
which the one `"?` left whole (§1, third finding). This cures the pre-glued API quotes and one-line gaps; with D5 in
place it is no longer what the notice road relies on for a line break. The pattern stays `NonBacktracking` — the
labelled pattern built in 1 ms (`Redaction.cs:202`), the automaton's problem was the bearer rule's alternation in front
of its loop (`:195-197`), and this joiner has no alternation. If the generator refuses it at build, the fallback is the
backtracking engine with `BacktrackingCeilingMs` and a reason beginning `NOT NonBacktracking because:` (the audit
`EveryPattern_IsNonBacktrackingOrSaysWhyNot` admits either) — stated, not assumed, in the story's summary.

**D7 — Linear and bounded by construction, measured at the real sizes (risk R3).** The pass has no regex and ONE cursor
that only advances: it walks the text once; at each line start it counts the indentation and remembers it for that line
(computed once, never recomputed — a line of 100 000 spaces costs 100 000 steps once); at each line break it re-reads a
window of at most ~100 characters of the line just passed (the label's 64, the joiner's gaps, the quotes and indicator —
a bounded re-read of a bounded window, not a rescan); a consumed continuation or block is walked once and the cursor
resumes after it; a declined line is never read again. Every character is visited at most a small constant number of
times whatever the text is shaped like. It needs no deadline for its own bound — `PrivateKeyBlocks` has none either —
and runs under the between-pass `StopIfOverdue` in `SecretsTaken` and (D5) in `ClassOf`. Measured, as
`TheRedactorCannotBeMadeToHangTests` measures (`Ceiling` 10 s, `:32`; the corpus at `:34-58`), with new adversarial
shapes through ALL THREE entry points (`SafeText`, `SafeSource`, `FirstSecretClass`) at **262 144 characters** —
`SecuritySignals.MaxFileCharacters`, the largest real input — and at `StdErrChars` 64 Ki (R1's road): 262 144 characters
of `key:` LF lines (every label declines, each once); `key: |` LF then 262 000 characters of indented lines (one block,
consumed once — S-11's confidentiality row doubles as its timing row); a blank-heavy block (every other line empty);
lines indented by 100 000 spaces then a label; 50 000 `key:` LF `  v` pairs; a 262 144-character text with no line break
(the no-op road). Expected well under the existing "130 ms or less over 200 KB" class (`Redaction.cs:111`); the
whole-call promise ("about four seconds in the worst case", `:152-158`) is unchanged.

**D8 — Fail-closed is unchanged.** `WhenRedactionTimesOut` (`:294-304`) still answers `[redacted]` for a regex ceiling
and `StopIfOverdue` still throws between passes; the new pass itself throws nothing — it cannot time out and therefore
has nothing to fail closed on, exactly like `PrivateKeyBlocks`, and (D4) it has no bound to overflow. `FirstSecretClass`
still answers `unredactable` on a ceiling.

**D9 — The extension mirror and parity: the pass is ported, byte for byte.** The first draft kept the pass server-only
because the extension has no layout-keeping road; C1 showed the notice road is the SAME road on both sides and the pass
has to run before the strip there too. So: `src_vs_code/src/labelContinuations.ts` is the port of
`LabelContinuations.cs`, as `privateKeyBlocks.ts` is of `PrivateKeyBlocks.cs` — same evidence, same declines, same
replacement, same block end — and `safeText` calls it after `redactPrivateKeyBlocks` and before its printable filter
(`notifications.ts:237-241` reordered). `notifications.ts:206` takes the D6 joiner byte for byte. `noticeShapes.ts`
`SHAPES` gains R1–R11 with REAL line breaks, the child-container shape, the blank-line block and the two-line scalar, so
`run-parity.mjs` compares `safeText` of both halves over them (the harness already carries real-LF shapes:
`privateKey('RSA ', '\n')`). `NoticeTool` is unchanged (`safe-text` already exercises `Passes` end to end). The
`SafeSource` road stays server-only, as it is today — the pass under it is the parity-tested one.

**D10 — The swallow (R10 on the `SafeText` road, the German row) — a lead-gated story, S4, redesigned after cadence C2.**
The first draft put a negative lookahead ("the value is not itself a label") in the one labelled pattern, and C2 showed
it UN-MASKS `password=abc=def`: the lookahead rejects the value `abc=def` because `abc=` looks like a label, the match
at `password` fails, and the scan resumes at the ordinary label `abc` — whole. The ambiguity is real (`abc=def` is both
a value and an assignment) and only the word list resolves it, which a lookahead cannot consult. Cure: **two labelled
passes.** The first is the D6 rule exactly as it is, which masks every value whose OUTER label names a credential
(`password=abc=def` → `password=[redacted]`, `password: abc=def` likewise). The second — the *inner-label pass* — is
the same rule with the lookahead `(?!["']?[A-Za-z][A-Za-z0-9_.-]{0,63}["']?[ \t]{0,4}[:=])` before the value, run over
the first pass's output: on `Fehler: password=letmein` the match at `Fehler` fails and `password=letmein` is found on
its own; on `service:  api_key: FAKE` likewise; on the first pass's own output it is idempotent (`password=[redacted]`:
the value begins with `[`, no label); `Set-Cookie: session=abc` (`Set-Cookie` fails, `session` is no credential) and
`?sig=[redacted]&author=me` (the value begins with `[`) stay as pinned. It FLIPS the German row (`password=letmein`
becomes `password=[redacted]`, the better direction) and nothing else that is pinned. Cost: a lookaround takes the
second pattern off `NonBacktracking` (unsupported there, as the Moonshot rule's look-ahead already is, `:190-192`), so it
carries `BacktrackingCeilingMs` and a reason; its backtracking cost stays one try per start (label characters never
contain `:`/`=`, the value is greedy with nothing after it); measured on the corpus. Regression rows in both halves and
in `FirstSecretClass`: `password=abc=def`, `password: abc=def`, `token=a==` (base64 padding), and the German row.
Both halves, parity. Recommended; gated on Q1 because it changes a pinned contract and adds a pattern.

**D11 — Residuals this plan records and does not cure.** (a) R9, a value WRAPPED mid-way: the remainder on the next
line is indistinguishable from the next line of a file; no evidence, no reach. (b) R8 without indent or quote. (c) The
two API quote sites that replace line breaks by a SPACE before `SafeText` (`AskApiMode.cs:394`, `ProbeApiMode.cs:373`)
get D6's cure only (the gap is indentation + 1; a two-line block scalar glued by the CALLER keeps its second line) —
one line each to drop the replace and let `SafeText` see the breaks is proposed as Q6. (d) A scheme word the bearer
rule does not know after `Authorization:` (one-line limit). (e) Two line breaks between a label and a PLAIN
continuation. (f) A child assignment under a credential-named container whose own label is NOT a credential
(`credentials:` LF `  user: FAKE`) — the container's rule declines the child, the child's label is ordinary; masking
it would be the container-wide masking this plan does not attempt.

## 4. Scenarios (each RED is the message before the fix)

C# through `Redaction.SafeSource` / `SafeText` / `FirstSecretClass` and, for S-9 and S-12, the product
(`TheStderrLineIsQuotedSafelyTests.ThroughTheProduct`; `ServerNoticeLine` → `notificationLine`/`parseNotificationLine`).

| # | Given | Expected | RED today |
|---|---|---|---|
| S-1 | R1 through `SafeSource` | `{` LF `  "api_key":` LF `    "[redacted]",` LF `}` — same line count | `Did not expect text "{\n  \"api_key\":\n    \"FAKE-CANARY-0001\",\n}" to contain "FAKE-CANARY-0001".` |
| S-2 | R2 through `SafeText` (DetailLimit) | `{  "nested": {    "api_key":        "[redacted]"  }}` | `Expected text to be "…\"api_key\":        \"[redacted]\"…", but "…\"api_key\":        \"FAKE-CANARY-0002\"…" differs near "FAK" (index 46).` |
| S-3 | R3, R5, R6, R7, R10's inner pair through `SafeSource` | each value `[redacted]`, indentation kept, `\` kept | `Did not expect … to contain "FAKE-CANARY-0003"` (one row per shape) |
| S-4 | R4 through `SafeSource` and through `SafeText`; R4b through both | `password: >` LF `  [redacted]`; `password: >  [redacted]`; R4b: two placeholders, no `FAKE-SECOND` on either road | `Did not expect "password: [redacted]\n  FAKE-CANARY-0004\n" to contain "FAKE-CANARY-0004".`; R4b on `SafeText`: `Did not expect "password: \|  [redacted]  FAKE-SECOND" to contain "FAKE-SECOND".` |
| S-5 | the declines: `password:` LF `the one you chose last week`; `the key` LF `to this is patience`; `api_key:` LF LF `  x`; `api_key:` at end of text; `service:` LF `  api_key: FAKE-CANARY-0012` | first four UNCHANGED; the fifth `service:` LF `  api_key: [redacted]` | green today and MUST stay green — the fifth is the test that goes red under a crossing joiner (D2): `Did not expect … to contain "FAKE-CANARY-0012".` |
| S-6 | every pinned row of `ServerNoticeLineTests` (`:131-213`), `password     =x` included | unchanged | green before and after |
| S-7 | R1 through `FirstSecretClass`; the precedence rows: `"private_key":` LF `"-----BEGIN…` → `private-key`; `api_key:` LF `  FAKE` + `?access_token=x` → `labelled`; `?access_token=x` alone → `parameter` | as named | `Expected text to be "labelled" with a length of 8, but "" has length 0.` |
| S-8 | the D7 adversarial shapes through all three entry points at 262 144 and 64 Ki | answered under `Ceiling`, each elapsed time printed | green before (the pass does not exist) — the row is the guard the pass ships under |
| S-9 | an ACP stderr of 64 Ki whose first lines are R1 and whose last holds the monthly phrase, through the product | no canary fragment in the diagnosis line, the report JSON, the summary, the ledger, the log | `Did not expect seen.Report "…" to contain "FAKE-CANARY-0001" (the shim's report JSON).` |
| S-10 | (S4, gated) the German row, `service:  api_key: FAKE-CANARY-0012`, `password=abc=def`, `password: abc=def`, `token=a==` through `SafeText` and `FirstSecretClass` | `Fehler: password=[redacted] …`; `service:  api_key: [redacted]`; `password=[redacted]`; `password: [redacted]`; `token=[redacted]`; class `labelled` for each | `Expected text to be "Fehler: password=[redacted] für …", but "Fehler: password=letmein für …" differs near "let" (index 17).` — and the C2 guard: under the single-lookahead design `Did not expect "password=abc=def" to contain "abc=def".` |
| S-11 | **P1, confidentiality past the old bound:** a 262 144-character text — `password: \|` LF then 262 000 characters of indented lines whose every 1 000th line carries `FAKE-CANARY-DEEP-<n>`, then `ordinary: visible` — through `SafeSource` and `FirstSecretClass` | no `FAKE-CANARY-DEEP` anywhere, `ordinary: visible` present, line count unchanged, class `labelled` | `Did not expect text to contain "FAKE-CANARY-DEEP-66"` (the first canary past 65 536 characters) — the row an elapsed-time test cannot write |
| S-12 | **C1, through the notice road both halves:** a server notice whose `detail` is R4b, written by `ServerNoticeLine`, read by the extension's `parseNotificationLine`; and the extension's own `notificationLine` of the same record | neither line carries `FAKE-FIRST` or `FAKE-SECOND`; `safeText` and `SafeText` agree byte for byte (parity) | `Did not expect line "…\"detail\":\"password: \|  [redacted]  FAKE-SECOND\"…" to contain "FAKE-SECOND".` |
| S-13 | **R1 (risk), the child label kept:** `credentials:` LF `  password: FAKE-CANARY-CHILD` through `SafeSource` and `SafeText` | `credentials:` LF `  password: [redacted]` — the child label present, the canary gone | green today (the one-line rule masks the child) and MUST stay green; under D4 without the child rule: `Expected text to contain "password:" but found "credentials:\n  [redacted] FAKE-CANARY-CHILD"` |
| S-14 | **R2 (risk), blank lines inside a block:** `password: \|` LF `  FAKE-FIRST` LF `` LF `  FAKE-SECOND` LF `ordinary: visible` | `password: \|` LF `  [redacted]` LF `` LF `  [redacted]` LF `ordinary: visible`; five lines in, five out | `Did not expect … to contain "FAKE-SECOND".` |
| S-15 | compound labels as continuation labels: `db_password:` LF `  FAKE`, `apiKey:` LF `  "FAKE"`, `X-Api-Key:` LF ` FAKE`, `"client_secret":` LF `    "FAKE"` | each masked | `Did not expect … to contain "FAKE"` (one row each) |
| S-16 | R11 TOML through `SafeSource`; `password='FAKE'` and `password = """FAKE"""` on one line through `SafeText` | `password = """` LF `[redacted]` LF `"""`; `password='[redacted]'`; `password = """[redacted]"""` | `Did not expect … to contain "FAKE"` (three rows) |

TypeScript (`notifications.test.ts`, `labelContinuations.test.ts`): S-2, S-4, S-12, S-13, S-14, S-16's one-line rows as
`assert.ok(!safe.includes('FAKE-…'))` — RED with the glued text; `noticeShapes.ts` corpus rows for R1–R11, the child
container, the blank-line block and the two-line scalar — `run-parity.mjs` RED on a one-sided change: `safeText at
4096: N of M cases differ — one half of this product would write a secret the other would have taken out`.

## 5. Build order

Four stories, no epics. **S1 is the only cornerstone**: the matching model, its declines and the child rule (D3) are the
whole security argument — a wrong decline is a leak, a wrong reach is a regression on served files, and a wrong child
rule takes a label away from the rule that needed it. **S1 and S2 are interdependent and land together**: S1 changes the
server's `safeText` output on real-LF shapes, S2 ports the same change to the extension, and `run-parity.mjs` is red
until BOTH halves agree — so they are one pull request (or S2 lands first behind nothing and the parity red is the
proof, then S1).

1. **S1 — `LabelContinuations` (server) and the pass order of both roads.** RED: S-1, S-3, S-4, S-5 (all five, the fifth
   as the regression guard), S-7 (with the precedence rows), S-8, S-11, S-13, S-14, S-15, S-16's R11 row. New file
   `src_mcp/core/Notices/LabelContinuations.cs` (procedural, one advancing cursor, `internal static`), wired into
   `SecretsTaken` after the block pass; `Passes` reordered (block → continuation → strip → …); `ClassOf` reordered and
   given the clock. Test file `src_mcp/tests/ALabelledValueOnTheNextLineIsRedactedTests.cs`; rows in
   `TheRedactorCannotBeMadeToHangTests`, `SecretCheckTests`, `APrivateKeyBlockIsNeverServedTests` (the real-LF notice
   output of D5, now N placeholders). GREEN; break-its: remove the deeper-indent evidence and watch S-3's YAML row go
   red; remove the credential check and watch S-5's fifth row go red; remove the child rule and watch S-13 go red;
   reinstate a 65 536 block cap and watch S-11 go red.
2. **S2 — the extension's port, the joiner, the corpus (both halves).** RED: `labelContinuations.test.ts` (S-4, S-13,
   S-14 through `safeText`), `notifications.test.ts` (S-2, S-12, S-16's one-line rows), S-2 and S-16's one-line rows in
   C#; the corpus rows, with `run-parity.mjs` red while only one half has changed. Port the pass byte for byte; reorder
   `safeText`; change `LabelledPattern` and `LABELLED` identically; confirm the `NonBacktracking` build (D6) or record
   the fallback. GREEN on `CoaiMcp.Tests`, `npm test`, `npm run test:parity`.
3. **S3 — the product checks and the docs.** S-9 through `TheStderrLineIsQuotedSafelyTests`; S-12 through
   `ServerNoticeLine` and the extension's parser; `research/module_core.md`: a `LabelContinuations` row beside
   `PrivateKeyBlocks`, the `Redaction.SafeSource` row's "stops at each line's end" corrected, the new pass order and the
   joiner in the `Redaction` rows, the real-LF notice output change; `research/module_tests.md` rows for the new tests
   with their RED messages; the `SafeSource` remark at `Redaction.cs:316-317` and the "control characters go first"
   remark at `:264-270` rewritten; the header remark of `Redaction.cs` (`:9-14`) and of `notifications.ts` naming the
   pass as the second procedural port; one pointer from the Kimi plan's R1 bullet to this plan; Q6's two one-line
   changes if the lead agrees. Promote this plan (`/promote-plan`) when S1–S3 ship and Q1 is answered.
4. **S4 — the swallow (gated on Q1).** RED: S-10 (the German row's expectation flipped; the C2 guard rows);
   `EveryPattern_…` sees a seventh pattern with its reason; the hang test's labelled rows re-measured. Both halves,
   parity.

### S1+S2 — status: built 2026-10-23 (S3 and S4 not started; the plan is not promoted)

**What shipped.** `src_mcp/core/Notices/LabelContinuations.cs` (the pass, `internal static`, one advancing cursor, no regex) and its
byte-for-byte port `src_vs_code/src/labelContinuations.ts`; the pass order of both roads and of `ClassOf` in `Redaction.cs` and
`safeText` (D5); `ClassOf` carries the clock; the D6 joiner in both halves (`Redaction.LabelledPattern`, `notifications.ts`
`LABELLED`); the parity corpus rows with REAL line breaks. Tests: `ALabelledValueOnTheNextLineIsRedactedTests` (C#, 55 rows),
`labelContinuations.test.ts` (TypeScript, 19), one row in `SecretCheckTests`, the line-spanning shapes of
`TheRedactorCannotBeMadeToHangTests` (7+1 shapes × 2 sizes × 3 entry points), and the parity corpus (445 → 936 shapes, 4 → 5 records).

**The D6 joiner builds as `NonBacktracking`** — no fallback, no reason string: the pattern still has no alternation in front of a
loop, and `EveryPattern_IsNonBacktrackingOrSaysWhyNot` stays green with six patterns.

**RED first, on the unfixed code (exact messages, abridged only where the string was a whole file).**

| Scenario | Test | Seen red as |
|---|---|---|
| S-1 | `R1_AJsonValueOnTheNextLine_IsRedacted_AndTheLineCountSurvives` | `Did not expect safe "{\n  \"api_key\":\n    \"FAKE-CANARY-0001\",\n}" to contain "FAKE-CANARY-0001".` |
| S-2 | `R2_ANestedJsonValue_EightSpacesDeep_IsRedacted_OnTheNoticeRoad` | `Did not expect safe "{  "nested": {    "api_key":        "FAKE-CANARY-0002"  }}" to contain "FAKE-CANARY-0002".` |
| S-3 | `EachShapeWithEvidence_HasItsValueRedacted_ThroughSafeSource` (R3, R5 with a space and with a tab, R6, R7, R10's inner pair) | `Did not expect safe "api_key:\n  FAKE-CANARY-0003" to contain "FAKE-CANARY".` (one row per shape); `ACrLfBreak_…` / `ALoneCarriageReturn_…`: `Expected Source(given) to be the same string, but they differ on line 2 and column 3 (index 12)` |
| S-4 | `R4_ABlockScalarsValue_IsRedacted_OnBothRoads`, `R4_AGluedBlockIndicator_…`, `R4b_ATwoLineBlockScalar_…` | `Did not expect source "password: [redacted]\n  FAKE-CANARY-0004\n" to contain "FAKE-CANARY-0004".`; `Did not expect Notice("password: >  FAKE-CANARY-0004") "password: [redacted]  FAKE-CANARY-0004" to contain "FAKE-CANARY-0004".`; `Did not expect source "password: [redacted]\n  FAKE-FIRST\n  FAKE-SECOND" to contain "FAKE-FIRST".` The notice-road face of R4b — `Did not expect notice "password: |  [redacted]  FAKE-SECOND" to contain "FAKE-SECOND".` — is the message of break-it 5 below (the strip first) |
| S-5 | the six declines, `ANestedKeyWithItsValueOnItsOwnLine_…` (the fifth row), `ALabelThatIsNotACredential_…`, `ATwoBreakGap_…`, `ALabelAfterALabel_…` | **green before and after**, as designed |
| S-6 | `ServerNoticeLineTests` (every pinned row, `password     =x` included) | green before and after, unchanged |
| S-7 | `R1_IsRecognisedAsALabelledSecret_ByTheSecretCheck`, `ThePrecedenceOfTheClasses_IsPinned`, a row of `SecretCheckTests.ASecretShape_IsRefused_…` | `Expected Redaction.FirstSecretClass(given) to be "labelled"` (found `""`); the continuation-beside-a-query-credential row: `… but they differ at index 0: "parameter" / "labelled"`; `Expected type to be …SecretCheckResult+Refused, but found …SecretCheckResult+Clean.` The `private-key`, `parameter`-alone and nothing rows are green before |
| S-8 | `ALineSpanningShape_IsAnsweredUnderTheCeiling_ThroughAllThreeEntryPoints` | green before (the pass did not exist) — the guard the pass ships under; timings below |
| S-11 | `ABlockScalar_OfTwoHundredSixtyTwoThousandCharacters_HasNoCanaryAnywhere` | with the pass disabled: `Expected FirstCanary(safe) to be "none" … but they differ at index 0: "FAKE-CANARY-DEEP-1 (at"`; with a 65 536 cap reinstated (break-it 4): `… "FAKE-CANARY-DEEP-63 (at"` |
| S-13 | `ACredentialNamedContainer_KeepsItsChildsLabel_OnTheSourceRoad`, `AChildAssignmentWithItsOwnQuote_…` | green before and after on the SOURCE road. **The notice-road half is NOT green today and is not cured here — see deviation 3** |
| S-14 | `ABlankLineInsideABlockScalar_DoesNotEndTheBlock`, `ABlockEndsAtTheFirstLineThatIsNotDeeper_…` | `Did not expect safe "password: [redacted]\n  FAKE-FIRST\n\n  FAKE-SECOND\nordinary: visible" to contain "FAKE-FIRST".` |
| S-15 | `ACompoundLabel_ReachesItsNextLineValue` (7 rows, `db_password`, `apiKey`, `X-Api-Key`, `"client_secret"`, `refresh_token =`, `'api_key' :`, a 100-character prefix) | `Did not expect Source(given) "db_password:\n  FAKE-CANARY-0015" to contain "FAKE-CANARY-0015".` (one row each) |
| S-16 | `R11_ATomlTripleQuotedValue_…`, `ATripleQuotedValue_OfSeveralLines_…`, `AnUnclosedTripleQuote_…`, `AnySingleOrTripleQuotedValue_OnOneLine_…` | `Did not expect … to contain "FAKE-"`; `Expected Notice(given) to be the same string, but they differ at index 10: "password='FAKE-CANAR" / "password='[redacted]"` (three one-line rows) |
| D6 gap | `AGapOfThirtyTwo_AfterTheJoiner_IsStillTheValuesGap` | `Did not expect Notice(given) "api_key:                                FAKE-CANARY-0017" to contain "FAKE-CANARY-0017".` |
| D5 output | `ARealLineBreakPrivateKey_InANotice_LosesItsBody_OnePlaceholderPerBodyLine` | `Expected safe to be the same string, but they differ at index 58: ".[redacted]-----END." / ".[redacted][redacted]."` |
| S-2, S-4, S-12, S-14, S-16 (TypeScript) | `labelContinuations.test.ts` | `AssertionError … {  "nested": {    "api_key":        "FAKE-CANARY-0002"  }}`; `X-Api-Key:     FAKE-CANARY-0006`; 8 of the first 9 tests red, the pinned `password     =x` green |

**Break-its (each run on the built code, the file restored by WRITING it and rebuilding, the restore checked byte for byte).**

| # | Break | Went red as |
|---|---|---|
| 1 | the deeper-indent evidence removed (`HasEvidence`) | 14 of 55: the S-3 rows R3, R5 (tab), R6, R10, the CRLF and CR rows, 4 of the 7 S-15 rows, the base64 row and the query-credential precedence row |
| 2 | the credential check removed (`NamesACredential` in `TryReadEnding`) | 1: `ALabelThatIsNotACredential_NeverReachesTheNextLine` — **S-5's fifth row stays green**, because the child-assignment rule alone declines `service:` LF `  api_key: …`; with the credential check AND the child rule removed it goes red too (5 red: that row, S-3's R10 pair, S-13 ×2, the `title:` row): `Did not expect safe "service:\n  api_key: …" to contain "FAKE-CANARY-0012"` |
| 3 | the child-assignment rule removed | 2: `ACredentialNamedContainer_KeepsItsChildsLabel_OnTheSourceRoad` and `AChildAssignmentWithItsOwnQuote_…`: `Expected Source(given) to be the same string, but they differ on line 2 and column 3 (index 15)` |
| 4 | a 65 536-character cap on a block reinstated | 1: S-11, `"FAKE-CANARY-DEEP-63 (at" / "none"` |
| 5 | the order of the first draft: strip every control character first, then the passes | 2: R4b on the notice road (`password: |  [redacted]  FAKE-SECOND`) and the real-line-break PEM row |
| 6 | the spec's literal order: no first strip at all (D5 as worded) | 1: `AControlCharacterInsideAFooterlessKeyBody_DoesNotEndTheRun`: `Did not expect Notice(cut) "-----BEGIN PRIVATE KEY-----[redacted]FAKEKEYBODYLINETWO" to contain "FAKEKEYBODYLINETWO".` — see deviation 1 |
| 7 | S2's one-sided parity: `safeText` no longer calls the pass (TypeScript only) | `npm run test:parity`: `safeText at 1000: 101 of 936 cases differ — one half of this product would write a secret the other would have taken out` (first diff: `password: |\n  FAKE-FIRST\n  FAKE-SECOND` — TypeScript `password: |  [redacted]  FAKE-SECOND`, C# `password: |  [redacted]  [redacted]`) |
| 8 | the TypeScript evidence moved (`nextIndent > … + 1000`) | `labelContinuations.test.ts`: S-3, S-15 and the base64 row red |

**Deviations from the plan — each one a decision, none silent.**

1. **A two-stage strip on the notice road (D1/D5).** The plan says the block pass and the continuation run before "drop every
   control character". Built literally, a footerless key whose body has one stray control byte (an ANSI escape in a stderr tail, a NUL)
   ends the block's run at the byte and the rest of the body is left readable after the strip — a regression against today's order
   (break-it 6 shows it). So `SafeText` / `safeText` drop every control character EXCEPT tab, LF and CR first (exactly what
   `SafeSource` reads), run the two passes, then drop the layout. Same result as the plan's order for every input without a control
   byte; safer for the rest. Both halves, parity-tested (the corpus carries a control byte beside a label and inside a key body).
2. **The child-assignment rule does not count base64 padding.** A line `abc=` followed by `=` is not an assignment (`[:=]` then
   another `=`): without this, `authorization:` LF `  dXNlcjpwYXNz==` declines as a "child label" and the secret stays. A tightening;
   pinned by `ABase64ValueEndingInPadding_IsNotTakenForAChildLabel` and its TypeScript twin.
3. **S-13's notice-road half is red today and stays red until S4.** The plan calls it "green today"; it is green on the SOURCE
   road only. On the notice road the strip glues `credentials:` LF `  password: FAKE-CANARY-CHILD` into `credentials:  password:
   FAKE-CANARY-CHILD`, the leftmost match starts at `credentials` and takes `password:` for its value —
   `credentials:  [redacted] FAKE-CANARY-CHILD`, measured on the unfixed code and after. That is D10's swallow, the plan's gated S4.
   The pass cannot cure it without the container-wide masking D11(f) refuses. The test pins the source half and says why.
4. **S-4's `password: >` line does not keep its `>`.** The plan's expected `password: >` LF `  [redacted]` cannot hold under a
   regex: with a LF after the indicator the optional indicator group backtracks off and the VALUE is `>` — the labelled rule prints
   `password: [redacted]` LF `  [redacted]`. Harmless (`|` and `>` are not secrets) and not avoidable without a look-ahead, which
   `NonBacktracking` refuses. The tests assert the canary gone, the line count and the second line.
5. **S-5 is a SafeSource row, and the glued road does not "decline".** On the notice road the strip glues every pair, so
   `password:` LF `the one you chose last week` still becomes `password:[redacted] one you…` — today's behaviour, unchanged.
   S-5 is asserted through `SafeSource` and the pass alone, as the plan's table says ("through `SafeSource`").
6. **R7's `\` is masked by the labelled rule** (`export TOKEN=[redacted]` LF `[redacted]`), as D4 says — S-3's "`\` kept" is the
   pass's output, not the final one.
7. **`ClassOf` answers `labelled` before `bearer`** for `Authorization:` LF `  Bearer abcdefghijkl` (the continuation comes
   first, D5). Still a refusal; only the class name differs.
8. **Where the tests live.** The TypeScript rows of S-2, S-12 and S-16 are in `labelContinuations.test.ts`, not
   `notifications.test.ts`. S-12's SERVER half (`ServerNoticeLine` written, the extension's parser reading) is S3's; the parity
   corpus already carries a record whose `title` is R1 and whose `detail` is R4b, so both halves' notice lines and the parser's
   round trip are compared byte for byte. S-11's first leaked canary under a reinstated cap is DEEP-63 (the plan guessed 66; the
   line length differs).

**Measured** (`Release`, best of three after a warm-up, a scratch console against `CoaiMcp.Core`; the test rows assert only the 10 s
`Ceiling`). Milliseconds, `SafeText` / `SafeSource` / `FirstSecretClass`:

| Shape (about 262 144 characters unless noted) | 262 144 | 65 536 |
|---|---|---|
| `api_key:` LF repeated — every label declines | 48.0 / 39.5 / 43.6 | 3.8 / 2.5 / 2.3 |
| `api_key: \|` and one endless block of 98-character lines | 4.3 / 4.2 / 2.7 | 0.4 / 0.3 / 0.2 |
| a blank-heavy block (every other line empty) | 30.1 / 19.2 / 11.1 | 2.8 / 4.6 / 0.9 |
| lines indented by a quarter of the size, then a label | 19.2 / 9.2 / 9.2 | 0.8 / 0.4 / 0.4 |
| `token:` LF `  v` pairs | 70.7 / 19.0 / 13.0 | 8.0 / 3.0 / 1.0 |
| 50 000 `token:` LF `  v` pairs (550 000 characters) | 82.5 / 17.3 / 6.4 | 68.3 / 26.9 / 8.4 |
| no line break at all (the no-op road) | 12.2 / 12.0 / 12.4 | 3.7 / 3.7 / 2.4 |
| an unclosed triple quote | 8.9 / 5.6 / 1.1 | 1.8 / 1.6 / 0.5 |

Nothing exceeds 100 ms — the "130 ms or less over 200 KB" class of the existing patterns. `SafeText` costs more than the other two
on line-heavy text because it builds the printable string twice (the two strips).

**Residuals found while building, not in D11 (a and c were then cured by S2b, below).** (a) A YAML LIST under a credential-named key (`api_key:` LF `  - FAKE-1`):
the first token of the next line is the dash, so the pass masks the dash and leaves `FAKE-1` — measured, `SafeSource` answers
`api_key:` LF `  [redacted] FAKE-1` LF `  - FAKE-2`; the one-line rule has always masked the dash of `api_key: - FAKE` too. A list
item is a plausible continuation; skipping a leading `- ` is a one-line change in both halves if the lead wants it.
(b) `credentials:` LF `  password: x` on the notice road (deviation 3, S4).
(c) **False positives on CODE, measured on this repository.** Run over the 2 885 `.cs`, `.ts`, `.md` and `.json` files of
`src_mcp`, `src_vs_code/src` and `research`, the pass changes 11 files — almost all code where a credential-named name ends a
line with `=` or `:` and the expression continues indented: `WithoutTheKey =` LF `new Dictionary…` (`KeyVault.cs`), `type KeyRead =`
LF `| { … }`, `tokensIn:` LF `count(usage[…])`, `credsKey:` LF a quoted sentence, and `internal const string TheRoundsKey = """` whose
whole raw-string SQL body is masked up to its closing quotes (`Schema.cs`). A reviewer then reads `[redacted] Dictionary<…>`
instead of code: the plan's "expected" false positive (§7), at 0.4 % of files. A cheap narrowing, if the lead wants it: leave a
label whose line begins like a declaration (a type or a keyword before the name) to the one-line rule. Not built.

**Suites (run on the committed code, as executables, never `dotnet test`).** `CoaiMcp.Tests.exe` whole, through
`low-priority.mjs`: 13 725 tests, 13 604 passed, 0 failed, 121 skipped (the Linux legs), 20 min 5 s. `npm test`: 6 528 tests in
the main run, 6 526 passed, 0 failed, 2 skipped. `npm run test:parity`: 936 shapes at both limits, 5 records, 17 codes, byte for
byte. `plan-lifecycle.mjs`: clean. **An earlier whole run failed four tests** (`ConsultAntigravityDenialScenarioTests`,
`QuestionRowOnAgyScenarioTests`, `AKimiRowRunsThroughTheServiceOnWsl2Tests`, `AVibeReviewerRunsThroughAFakeVibeTests`: an 11 s
wall-clock bound, a "started by now" bound, a shared temp folder). Measured cause: I had started TWO whole suites of this
worktree at once, on a machine where a third session was running its own. The four passed alone (30 of 30) and the clean whole
run above passed all of them; recorded so the failure is not read as a property of the change.

### S2b — status: built 2026-10-23 (the lead's two decisions on the S1+S2 findings; S3 and S4 not started)

Two findings of the S1+S2 build were decided by the lead and built on this branch, same method (RED on the code before, GREEN,
break-its restored by writing the file and rebuilding, both halves, parity corpus). **They change what S1+S2 recorded above:**
residuals (a) and (c) of that block are CURED, and D3's two block evidences are narrower for an `=` label (see 2 and deviation 1).

1. **A YAML list under a credential key.** A plain continuation line whose first non-blank is `- ` masks the VALUE after the dash
   and keeps the dash — and so does every CONSECUTIVE item of the same indentation after it (`MaskListItems`): `api_key:` LF
   `  - FAKE-CANARY-L1` LF `  - FAKE-CANARY-L2` → `  - [redacted]` twice. An item that is itself an assignment (`- name: ci`) ends the
   list and is left to the labelled rule; a deeper or non-item line ends it. The evidence is still S1's (deeper indent, quote,
   backslash): the list must be indented under its key.
2. **A label whose separator is `=` never opens a block.** `WithoutTheKey =`, `type KeyRead =`, `TheRoundsKey = """` are code
   assignments. The pass takes AT MOST the one next line after such a label, and only when it is value-shaped (`IsValueShaped`): one
   token, optionally quoted, no space, quote, bracket, brace or parenthesis inside, not starting with `|` or `<`, at most a trailing
   `,` or `;` — so never `new X(…)`, `| { … }`, `[a, b]`, `(expr)`, a heredoc or a raw-string body. An indicator or a triple quote
   on the `=` label's line counts as the evidence (`password = """` LF `FAKE` is still masked). `:` labels (YAML, JSON, headers,
   `password: """`) keep their blocks. No look-ahead: the rule is procedural, the regex is unchanged and stays `NonBacktracking`.

**RED first, on the S1+S2 code (exact messages).**

| Test | Seen red as |
|---|---|
| `AYamlList_UnderACredentialKey_…` | `Did not expect source "api_key:\n  [redacted] FAKE-CANARY-L1\n  - FAKE-CANARY-L2\nname: visible" to contain "FAKE-CANARY-L".` |
| `AListItem_WithAQuotedValue_…` | `Expected Source(quoted) to be the same string, but they differ on line 2 and column 3 (index 10): "tokens:\n  [redacted]." / "tokens:\n  - "[redact."` |
| `AListThatStopsBeingAList_EndsTheMasking` | `Did not expect safe "api_key:\n  [redacted] FAKE-CANARY-L1\n    nested continuation\n  - FAKE-CANARY-L2\nother: x" to contain "FAKE-CANARY-L1".` |
| `ACodeAssignment_IsServedByteForByte` — `WithoutTheKey =` (the `KeyVault.cs` shape) | `Expected Source(given) to be the same string because KeyVault.cs, but they differ on line 2 and column 5 (index 81): ".=\n    [redacted]." / ".=\n    new Dictionary."` |
| the same — `type KeyRead =` (`chatStoreImport.ts`) | `… but they differ on line 2 and column 3 (index 17): ".KeyRead =\n  [redacted] {." / ".KeyRead =\n  | { readonly."` |
| the same — `TheRoundsKey = """` (`Schema.cs`, the SQL body) | `… but they differ on line 2 and column 5 (index 45): ".= """\n    [redacted]\n   ." / ".= """\n    ALTER TABLE."` |
| the same — `var token =` LF `(await …)`, `const secret =` LF `[first, second];` | `… ".token =\n    [redacted]." / ".token =\n    (await."`; `… ".secret =\n  [redacted],." / ".secret =\n  [first,."` |
| `AnAssignment_TakesAtMostTheOneNextLine_AndNeverOpensABlock` | `Expected Source(given) to be the same string, but they differ on line 3 and column 3 (index 28): ". [redacted]\n  [redac." / ". [redacted]\n  FAKE-S."` |
| `AnAssignmentWithAValueShapedNextLine_IsStillRedacted` (4 rows, the `client_secret =` LF `  FAKE-CANARY-EQ1` control), `AColonLabel_KeepsItsBlocks` | **green before and after** — the controls |
| `labelContinuations.test.ts` S2b rows (the S1+S2 port against the new tests) | `actual: 'api_key:\n  [redacted] FAKE-CANARY-L1\n  - FAKE-CANARY-L2\nname: visible'`; `actual: 'private static readonly … WithoutTheKey =\n    [redacted] Dictionary<string, string?> { [KeyVariable] = null };'`; `actual: "secret = '''\n  [redacted]\n  [redacted]\n'''"` |

**Break-its (the file restored by writing it and rebuilding, checked byte for byte).** (9) the list branch disabled: 3 red
(the list, the quoted list, the broken list). (10) an `=` label opens its blocks again: 2 red (`Schema.cs`, the one-next-line row).
(11) the value-shape test removed: 5 red (the five code shapes). (12) one-sided parity, the S1+S2 TypeScript port against the new
corpus: `safeText at 1000` differs — first diffs `api_key:\n  - FAKE-CANARY-L1\n  - FAKE-CANARY-L2\nname: visible` (TypeScript
`api_key:  [redacted] FAKE-CANARY-L1  - FAKE-CANARY-L2name: visible`, C# `api_key:  [redacted] [redacted]  - [redacted]name: visible`) and
`secret = '''…` (TypeScript masks the second line, C# does not).

**Moved on purpose (the plan's "pins that move").** `ATripleQuotedValue_OfSeveralLines_…` and `AnUnclosedTripleQuote_…` (and the
TypeScript twin) now use a `:` label — the block behaviour they pin; the `=` TOML shape of S-16 (`password = """` LF one token
LF `"""`) is unchanged and green. The hang shape `unclosed-triple-quote` uses `password: """`; two shapes were added
(`assignment-lines`, `list-items`) at both sizes through the three entry points, under the `Ceiling`.

**Repository-wide re-measure** (the same probe, the 2 885 `.cs`, `.ts`, `.md`, `.json` files of `src_mcp`, `src_vs_code/src`,
`research`): the pass changed **11** files before S2b and changes **5** now. The three `=` shapes are gone (`KeyVault.cs`,
`chatStoreImport.ts`, `Schema.cs`), and so is the rest of the `=` class. What remains, all `:` labels:
`AskApiMode.cs:227` (`case ApiOutcome.KeyRefused:` — a switch label), `panelProvider.ts:2795` (`case 'setBugsKey':`),
`claudeAdapter.ts:114` (`tokensIn:` LF `count(usage[…])` — an object property), `help.ts:178` (`credsKey:` LF a quoted sentence — an
object property), and `ProbeApiModeTests.cs:64` (a string literal ending `Authorization: "` before a `+` continuation).
A `case X:` label is the one remaining class worth a narrowing (a label whose line begins with `case ` is a switch label, never a
mapping key); not built — the lead said `:` labels keep today's behaviour.

**Deviations and consequences.**
1. **(Cured by S2c, below.) A multi-line TOML triple-quoted secret under `=` is no longer fully masked.** `secret = '''` LF `  FAKE-FIRST` LF `  FAKE-SECOND`
   masks the first line only; the second stays (measured: parity row `secret = '''…`, and `AnAssignment_TakesAtMostTheOneNextLine_…`
   pins it). This is the price of the lead's rule: `TheRoundsKey = """` and `password = """` are the same shape to a line reader.
   A line that STARTS with the label (only indentation or a quote before it) is a config assignment, a line with a type or a keyword
   before it is code — a narrowing that would keep multi-line TOML and still spare `TheRoundsKey`. Not built; the lead decides.
2. **"The one next non-blank line" is the immediate next line.** A blank line after an `=` label declines (S1's one-break rule,
   D3), as for every plain continuation.
3. **A list item at the label's own indentation** (`key:` LF `- a`, valid YAML) is not evidence — the evidence is S1's, so only an
   item indented under its key is reached. On the notice road the strip glues the list into `api_key:  - [redacted]  - [redacted]`
   and the labelled rule masks the first dash as well; the canaries are gone either way.
4. **Only the first token of an item** (`- FAKE one two`) is its value, as everywhere else.

5. **CI found what the local runs did not: `eslint` holds `src/**` to a cyclomatic complexity of 4.** The first push of S1+S2
   (run 40004943400) was green on every .NET job and red on `extension · typecheck · test · package`, step *Lint*: ten functions of
   `labelContinuations.ts` over the limit (`readOpening` 16, `isAssignment` 20, `maskPlain` 11…). `npm test` does not lint; `npm run
   lint` does, and was not in this plan's test list. `labelContinuations.ts` is rewritten as small functions (nothing over 4,
   `eslint-suppressions.json` untouched — new code is held to the rule); the C# half keeps its longer methods, which no rule
   limits. The two answer identically: parity 1 090 shapes byte for byte, `labelContinuations.test.ts` 23 of 23. **`npm run lint`
   belongs in the extension's Definition of Done for any story that adds a `.ts` file.**

**Suites (the committed code, as executables).** `CoaiMcp.Tests.exe` whole through `low-priority.mjs`: 13 743 tests, 13 622
passed, 0 failed, 121 skipped, 19 min 18 s (an earlier run of this series ended without a summary — another session stopped
it by PID — and was rerun alone). `npm test`: 6 532 tests, 6 530 passed, 0 failed, 2 skipped; `npm run lint`: clean;
`npm run test:parity`: 1 090 shapes at both limits, 5 records, 17 codes, byte for byte; `plan-lifecycle.mjs`: clean.

### S2c — status: built 2026-10-23 (the lead's answer to S2b's open question; S3 and S4 not started)

1. **An `=` label is CONFIG or CODE by what precedes it on its line.** With only indentation and quotes before the label
   (`password = """`, `  "password" = """`, `secret = '''`, `password = |`) it is a config assignment — TOML, INI, dotenv — and
   keeps its blocks, so a multi-line triple-quoted secret is masked whole again (S2b's deviation 1 is cured). With anything else
   before it — a type, a modifier, a keyword, `export` (`internal const string TheRoundsKey = """`, `type KeyRead =`, `var x =`,
   `const secret =`) — it is a code assignment and keeps S2b's narrow rule: at most the one next line, only when value-shaped.
   (`Ending.Assignment` now means "code assignment"; `StartsTheLine` decides.)
2. **A `case X:` label is not a credential label.** A `:` label with the keyword `case` (a whole word) before it, past its
   quotes and blanks, is a switch label and is skipped (`IsSwitchLabel`). `default:` is not special-cased: `default` does not
   name a credential, so it never reached the pass. A label that merely contains the word (`showcase_token:`, `case_token:`)
   is still a label (a test pins it).

**RED first, on the S2b code (exact messages).**

| Test | Seen red as |
|---|---|
| `ATomlTripleQuotedSecret_OfSeveralLines_IsMaskedWhole` | `Did not expect safe "secret = '''\n  [redacted]\n\n  FAKE-SECOND'''  # note\nafter = 1" to contain "FAKE-".` |
| `AConfigAssignment_OpensItsBlocks` ×3 (`password = """`, `  "password" = """`, `password = |`) | `Expected Source(given) to be the same string, but they differ on line 3 and column 1 (index 26): ".\"\"\"\n[redacted]\nFAKE-." / ".\"\"\"\n[redacted]\n[reda."`; the `|` row `… line 3 and column 3 (index 37): ". [redacted]\n  FAKE-S." / ". [redacted]\n  [redac."` |
| `ASwitchCaseLabel_IsNotACredentialLabel` ×3 | `… line 4 and column 9 (index 59): ".       [redacted]." / ".       return Refuse."`; `… line 3 and column 5 (index 41): ".Key':\n    [redacted]." / ".Key':\n    await."`; `… line 2 and column 9 (index 29): ".       [redacted]);" / ".       Use(token);"` |
| `ACodeAssignment_IsServedByteForByte` (`KeyVault.cs`, `type KeyRead =`, `TheRoundsKey = """`, …), `AnAssignmentWithSomethingBeforeItsLabel_IsCode`, the `client_secret =` control, `AColonLabel_KeepsItsBlocks`, `AColonLabelThatMerelyContainsTheWordCase_…` | **green before and after** — the controls |
| `labelContinuations.test.ts` S2c rows (S2b port) | `actual: "secret = '''\n  [redacted]\n\n  FAKE-SECOND'''  # note\nafter = 1"`; `actual: 'switch (outcome)\n{\n    case ApiOutcome.KeyRefused:\n        [redacted] Refused(body);\n}'` |

**Break-its (restored by writing the file and rebuilding, byte for byte).** (13) every `=` label is code again: 4 red (the TOML
row and the three config rows). (14) the `case` rule removed: 3 red (the three switch rows). (15) every `=` label is config:
8 red (the five shapes of `ACodeAssignment_…`, two of `AnAssignmentWithSomethingBeforeItsLabel_IsCode`, and `ACodeAssignment_TakesAtMostTheOneNextLine_…`: `KeyVault.cs`, `Schema.cs`, `type KeyRead =` among them).
(16) one-sided parity, the S2b TypeScript port against the new corpus: `safeText at
1000 … differs` — `secret = '''\n  FAKE-FIRST\n\n  FAKE-SECOND'''…` TypeScript `secret = '''  [redacted]  FAKE-SECOND'''…`, C#
`secret = '''  [redacted]  [redacted]'''…`.

**Moved on purpose.** `AnAssignment_TakesAtMostTheOneNextLine_…` (S2b) now reads `const secret = '''` (code); the same three lines
with `secret = '''` at the start are the config row and are masked whole. Corpus: 1 090 → 1 189 shapes (9 new shapes × 11 wraps).

**Repository-wide re-measure** (the same probe): 5 files before S2c, **3** now. The two `case` labels are gone
(`AskApiMode.cs:227`, `panelProvider.ts:2795`). What remains, all `:` labels that are object properties or a string:
`claudeAdapter.ts:114` (`tokensIn:` LF `count(usage[…])`), `help.ts:178` (`credsKey:` LF a quoted sentence),
`ProbeApiModeTests.cs:64` (a string literal ending `Authorization: "` before a `+` continuation).

**Consequence to know.** The config test is "only indentation and quotes before the LABEL RUN", and a label run includes dots:
a statement `this.secret =` LF `compute(a, b);` at the start of a line is therefore CONFIG, not code, and its next line is masked
by token. Over-masking, never a leak; not pinned by a test.

**Suites (the committed code, as executables).** `CoaiMcp.Tests.exe` whole through `low-priority.mjs`: 13 753 tests, 13 632 passed,
0 failed, 121 skipped, 17 min 49 s. `npm run lint`: clean. `npm test`: 6 534 tests, 6 532 passed, 0 failed, 2 skipped. **One
first `npm test` run failed one test**, `chatTestsLeaveNoTemp.test.mjs` ("chatStoreLock.test.js removes every temp folder it makes"),
because `chatStoreLock.test.js` hit \`EPERM: operation not permitted, open …\\a1.lock\` in the Windows temp folder; the same two
files pass alone (16 of 16, 4 of 4) and a second whole run passed everything — an environment error of that run (the file system,
not this change). \`npm run test:parity\`: 1 189 shapes at both limits, 5 records, 17 codes, byte for byte. \`plan-lifecycle.mjs\`: clean.

Releases: one coai-mcp release and one extension release carry S1–S3 together (the notice road is shared, D9); the
server release is the one that closes R1 on the served-file and stderr roads.

## 6. Test plan

- **C# — the test EXECUTABLE, never `dotnet test`** (xunit.v3 on Microsoft Testing Platform, `CoaiMcp.Tests.csproj:8`;
  CI does the same, `ci.yml:55-58`):
  `dotnet build dew_flow_connect_other_ais.slnx` then
  `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ALabelledValueOnTheNextLine*"`,
  `… --filter-class "*ServerNoticeLineTests"`, `… --filter-class "*TheRedactorCannotBeMadeToHang*"`,
  `… --filter-class "*SecretCheckTests"`, `… --filter-class "*APrivateKeyBlockIsNeverServed*"`,
  `… --filter-class "*TheStderrLineIsQuotedSafely*"`; then the whole executable once, at lowered priority if other
  suites run on the machine (the Kimi plan's note on wall-clock tests).
- **Extension:** `cd src_vs_code && npm test` (compiles, then `scripts/run-tests.mjs` over `out/test`);
  `npm run test:parity` (BUILDS `NoticeTool` first — `run-parity.mjs` says why a found DLL is not enough — then
  compares `safeText` over the corpus byte for byte and the notice lines through the parser).
- **RED-GREEN-RED on both sides.** Every scenario's RED message is observed on the unfixed code and recorded in
  `module_tests.md`; after GREEN, the four S1 break-its and S2's one-sided parity red are run and recorded. S-5, S-6 and
  S-13 are the regression guards and must be green BEFORE the change too — a guard that was red before proves the wrong
  thing.
- **Confidentiality beyond the boundary (P1):** S-11 asserts content, not time — no canary past 65 536 — through
  `SafeSource` and `FirstSecretClass`; a byte-position assertion, so a future cap cannot return unnoticed.
- **Measurement** (D7): the adversarial rows' elapsed time printed in the test's own message, at 262 144 and 64 Ki,
  through all three entry points.

## 7. Risks

| Risk | Mitigation |
|---|---|
| Performance on large text — a 262 144-character file of label lines, a block scalar that never ends, lines of enormous indentation | one advancing cursor, indentation counted once per line, a bounded re-read window at a line's end, no rescans (D7); S-8 measures at the two real sizes through all three entry points under the existing hang ceiling; the whole-call ceiling stands, now in `ClassOf` too |
| A block's tail past a cap stays readable | the cap is gone (D4): a block ends by structure or at the end of the text; S-11 pins content past the old boundary |
| False positives on served files — an ordinary word before a colon at a line's end, a markdown heading `## API key:` followed by indented prose | reach only for a credential-named label AND with evidence (D3); `## API key:` LF `Here is …` declines (no indent, no quote). A credential label followed by an indented paragraph masks the paragraph's first token — the same one token the one-line rule masks today, recorded as expected |
| A credential-named CONTAINER takes its child's label away (risk R1) | the child-assignment rule (D3); S-13 is the permanent guard, green before and after |
| A regression on the most common served shape (nested YAML) | D2 rejected the crossing joiner for exactly this; S-5's fifth row is the permanent guard |
| The notice road differs between halves — the pass runs before the strip on one side only | the pass is ported (D9) and the corpus carries real-LF rows, so `run-parity.mjs` is red on any one-sided change; S1 and S2 land together |
| The parity mirror drifting over time — two procedural ports (`privateKeyBlocks.ts`, `labelContinuations.ts`) | the corpus rows exercise both through `safeText`; the header remarks name both ports; `NoticeTool` unchanged |
| The real-LF PEM-in-a-notice output changes (D5) | still fully masked; no test pins the single placeholder; both halves change identically; recorded in `module_core.md` |
| `NonBacktracking` refuses the widened joiner at build | the fallback is the backtracking engine with the ceiling and a reason (D6); the audit test admits it; decided at build time and written in the summary |
| D6 masks more one-line text (`key:        value`-style aligned tables; single-quoted values) | that is the rule's purpose; the pre-joiner gap keeps the pinned negative; the corpus rows show every new positive |
| S4 flips a pinned contract and adds a backtracking pattern | gated on Q1; its own RED, its own measurement, both halves at once; the C2 guard rows (`password=abc=def`) pin that the second pass never un-masks the first's work |

## 8. Questions for the lead (assumed where marked)

- **Q1 — Fold S4 (the swallow, D10) into this epic?** It is the `SafeText` road's REAL everyday leak (nested YAML on one
  glued line) and changes the German row's pinned contract. *Assumed: planned as a gated story; not built until answered.*
- **Q2 — (answered by C1) the pass IS ported to TypeScript** and runs before the strip on both sides. No question left.
- **Q3 — Reach without evidence for `label=` CR LF `value` (R8)?** Dotenv never writes it; a `.http`/JSON/YAML/TOML writer
  always indents or quotes. *Assumed: declined and recorded (D11 b).*
- **Q4 — The post-joiner gap bound: 32 or 64?** 32 is eight JSON levels at four spaces. *Assumed: 32.*
- **Q5 — Class name for the secret check: `labelled` or a new `labelled-continuation`?** One rule reaching one line
  further argues for one class; a separate class would let `SecretCheckTests` tell them apart. *Assumed: `labelled`.*
- **Q6 — Drop the `\r`/`\n` → space replace at `AskApiMode.cs:394` and `ProbeApiMode.cs:373`** so the API quote road
  gets the pass instead of only D6's gap (D11 c)? One line each; the only visible change is that glued words lose the
  space between them, which `SafeText` does to every other caller already. *Assumed: yes, in S3, unless the lead objects.*

## 9. Definition of Done

- [ ] S-1…S-9 and S-11…S-16 written RED first, each RED message observed on the unfixed code and recorded in
      `research/module_tests.md`; then GREEN; then the four S1 break-its and S2's one-sided parity red observed and
      recorded (RED-GREEN-RED, both sides).
- [ ] S-5, S-6 and S-13 green before AND after — no pinned negative changed, nested YAML with the value on its key's
      line still masked on the `SafeSource` road, a credential-named container's child keeps its label.
- [ ] S-11 asserts no canary past 65 536 characters in a 262 144-character block scalar through `SafeSource` and
      `FirstSecretClass` — content, not time.
- [ ] `LabelContinuations` is procedural with one advancing cursor, after `PrivateKeyBlocks`, before the strip and the
      parameter pass, on both roads and in `ClassOf`; `ClassOf` carries the clock; the hang test's new rows run under
      `Ceiling` at 64 Ki and 262 144 through all three entry points.
- [ ] `labelContinuations.ts` is the byte-for-byte port and `safeText` runs it before its printable filter; the labelled
      joiner is identical in `Redaction.cs` and `notifications.ts`; `npm run test:parity` green over the corpus with
      R1–R11, the child container, the blank-line block and the two-line scalar; the `NonBacktracking` outcome of D6
      stated in the story summary.
- [ ] S-12 proves a two-line scalar in a server notice reaches the extension's parser with neither line readable.
- [ ] `CoaiMcp.Tests.exe` (whole), `npm test`, `npm run test:parity` green; run as executables, never `dotnet test`.
- [ ] `research/module_core.md`: `LabelContinuations` row, the new pass order of both roads, the real-LF notice output
      change, the joiner; the `Redaction.SafeSource` row and the `Redaction.cs:316-317` remark no longer say the labelled
      pass stops at a line's end; the header remarks of both halves name the second procedural port.
      `research/module_tests.md` rows for every new test.
- [ ] The Kimi plan's R1 bullet points here; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
- [ ] Q1 answered; S4 built or extracted into its own `todo/` plan; Q6 decided; this plan promoted to `research/` with
      its deviations and residuals (D11) recorded.
- [ ] No real-looking secret in any fixture (fake canaries only); the hygiene scan on the diff prints nothing.

## 10. The boundary with the CLI-reviewer-gaps plan (both sides)

`todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md` (branch `docs/cli-reviewer-gaps`, its §4 and its epic E3) also
touches `Redaction.cs`'s neighbourhood. Two plans on one file need the table the planning convention asks for; this is the
reciprocal half of its §4 (the cadence consult 74decbae).

| item | which plan builds it | the other plan's part |
|---|---|---|
| a new pass (`LabelContinuations`), the pass ORDER of both roads, the labelled joiner, the parity corpus's shape (real line breaks) | **this plan** (S1, S2) | the other plan does not touch them |
| three new one-line vendor shapes in `VendorPattern` and `notifications.ts` (`AIza…`, `ya29.…`, JWT), both halves | the other plan, its E3.S5 — **after this plan merges** | this plan adds NO vendor shape |
| callers that redact before they cut — `OpeningOfLastLine`, the `VendorProbe` note, kept evidence through `SafeSource`, the `ReviewerExecutor` keep-failure note (its E3.S1–S4) | the other plan; they call the redactor and do not edit it, so they can land before or after this plan | none of them is a caller this plan edits |
| `AskApiMode.cs` and `ProbeApiMode.cs` (this plan's S3 / Q6: drop the `\r`/`\n` → space replace) | this plan, S3 | the other plan does not touch them |

Order: this plan first for `Redaction.cs` itself (S1+S2 are built, not merged); the other plan's E3.S5 rebases onto the merged
file and adds its three shapes to `VendorPattern`, `Secrets`, `Patterns`, `Shapes` and the extension's `SECRETS`, with their own
parity rows. E3.S1–S4 are independent of this plan. What is disjoint: this plan changes no caller that E3 names, and the other
plan never changes a pass, the pass order or the joiner.

## The plan round (a0bc0720) and the two consults (a1c21b20 cadence, f1d48728 risk), 2026-10-23

Each point was verified against the code on `main` 151cc6526 (the reviewers ran isolated replays of the regexes, not the
unbuilt pass) before it was folded. All six hold; two of them found a design error, not only a gap.

| Point | Verified | Folded |
|---|---|---|
| **P1 (plan round, blocking)** — a block scalar longer than `BlockLimit` 65 536 inside a 262 144-character `SecuritySignals` input (`SecuritySignals.cs:18`) leaves its tail readable | Yes. The first draft's D3 ended a block "until `BlockLimit` characters"; everything after was ordinary text to the labelled rule, which cannot see the label. | D4: the cap is removed — a block ends by its structure or at the end of the text; there is no bound-reached state to fail closed from, which is the stronger fail-closed. S-11 asserts content past the old boundary through `SafeSource` and `FirstSecretClass`; a break-it reinstates the cap and watches it go red. |
| **C1 (cadence)** — `SafeText` strips line breaks before any pass on both halves (`Redaction.cs:391`, `notifications.ts:237`), so under D6 alone `password: \|` LF `  FAKE-FIRST` LF `  FAKE-SECOND` becomes `password: \|  [redacted]  FAKE-SECOND` on BOTH sides — parity, not safety — and server notices reach the extension's page through `notificationsFile.ts:108-110` with no second redaction | Yes, replayed: the glued text has two tokens and the rule masks one. The first draft's D5 declared the pass a no-op on the `SafeText` road and D9 kept it server-only — wrong for the notice road, which is one road with two writers. | D1/D5: the pass runs BEFORE the strip on both roads; D9: ported to `labelContinuations.ts`, `safeText` reordered, parity rows with real line breaks; S-12 through notice serialisation and the extension's parser; §5 says S1 and S2 are interdependent and land together. One consequence named: the block pass also moves ahead of the strip (it must precede the continuation pass), so a real-LF PEM in a notice becomes one placeholder per body line — nothing pins the single placeholder. |
| **C2 (cadence)** — D10's lookahead un-masks `password=abc=def`: the lookahead rejects the value `abc=def` (`abc=` looks like a label), the match at `password` fails, scanning resumes at the ordinary label `abc` | Yes, replayed. D6 alone masks it (`password=[redacted]`); D6 with the lookahead in the one pattern leaves it whole. | D10 redesigned as two labelled passes: the plain D6 rule first (every credential OUTER label masked), then the inner-label pass with the lookahead over its output (idempotent on `[redacted]`). Regression rows `password=abc=def`, `password: abc=def`, `token=a==` in both halves and `FirstSecretClass` (S-10). TOML triple-quoted values: covered — R11 and D3's fifth evidence kind (block until the closing `"""`), and D6's `["']{0,3}` for the one-line form (S-16). |
| **R1 (risk)** — a credential-NAMED container: `credentials:` LF `  password: FAKE-CANARY-CHILD`; `credentials` passes `NamesACredential` (`credential` is an *anywhere* word), the child is deeper, D4 replaces the token `password:`, the canary loses its label and survives every later pass — masked today by the one-line rule | Yes. A regression the first draft would have shipped. | D3's child-assignment rule: a PLAIN continuation declines when the next line is itself a label assignment, leaving it to the labelled rule; inside a block every line is value. S-13 pins "child label kept, canary gone", green before and after; a break-it removes the rule. D11 (f) records the ordinary-named child under a credential container as out of scope. |
| **R2 (risk)** — a blank line inside a block scalar ended the first draft's block early: `password: \|` LF `  FAKE-FIRST` LF `` LF `  FAKE-SECOND` LF `ordinary: visible` exposed `FAKE-SECOND` | Yes: the draft said "every following line indented deeper", and a blank line has no indentation. | D3/D4: a block continues through blank lines (left blank) and ends at the first NON-BLANK line not indented deeper; `ordinary: visible` survives; every line break kept. S-14. |
| **R3 (risk)** — boundedness: an advancing cursor, per-line indentation computed once, no rescans; measure all three entry points at 262 144 characters including long indentation and blank-heavy blocks; `ClassOf` (`Redaction.cs:362-376`) has NO `StopIfOverdue`, so a between-pass deadline cannot interrupt a procedural pass there; keep the precedence proofs; pin compound labels | Yes: `ClassOf` runs five steps with no clock (zero `StopIfOverdue` calls in `:362-376`); the draft's D7 measured only `SafeText`/`SafeSource` at 64 Ki and 262 144. | D7 rewritten around the cursor invariant and the once-per-line indentation; the adversarial corpus runs through `SafeText`, `SafeSource` and `FirstSecretClass` at 262 144 with the long-indent and blank-heavy shapes (S-8); the pass needs no deadline for its own bound — and `ClassOf` gets the clock anyway (D5), so the whole-call promise holds there. Precedence rows in S-7 (PEM + continuation → `private-key`; continuation + query credential → `labelled`; query credential alone → `parameter`). Compound labels `db_password`, `apiKey`, `X-Api-Key`, `client_secret` pinned as continuation labels (S-15). |

Kept from the first draft, because the round and the consults did not move them: the rejection of a crossing regex
joiner (D2, with S-5's fifth row as the guard), the evidence model for a plain continuation (D3), the layout and line
count kept (D4), the regex gap in both halves (D6), the fail-closed answers (D8), the residuals (D11), the four
stories with S1 as the only cornerstone, and the lead questions Q1, Q3–Q5 (Q2 is answered by C1; Q6 is new).
