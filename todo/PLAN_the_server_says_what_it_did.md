# PLAN — the server writes down what it refused, what failed, and that it died

> Status: **EPIC 1 IMPLEMENTED, 2026-09-21; epics 2 and 3 open.** Stories 1.1 (the credential list),
> 1.2 (the notice line), 1.3 (the path) and 1.4 (the writer, and the append it had to fix) have
> shipped. What remains is every CALL SITE — the census (2.1), the three refusal roads (2.2), the
> reviewer and startup notices (2.3), the live seam leg (2.4) — and the deaths (3.1, 3.2).
> Scope: `src_mcp` — a notice record and its serialiser, the append, the instrumentation sites, a
> run-start marker, and the `try/catch/finally` that `Program.cs` has never had.
>
> **This is S8 of [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md)**,
> extracted into its own file as that plan's section *G* says it must be. Defect 4 of the parent
> comes with it, for the reason the parent gives: it changes the `coai-mcp` binary, and its
> externally-killed half depends on the run marker this step builds.
>
> Its declared prerequisite has shipped. The parent ordered S8 after
> [PLAN_the_settings_file_ignores_the_side.md](../research/PLAN_the_settings_file_ignores_the_side.md);
> that landed on 2026-09-18 and went out as **`coai-mcp 0.30.0`**.
>
> **Through its plan round, 2026-09-21: three reviewers, 19 findings, 15 accepted.** The round
> changed the shape of two things — where the credential list lives, and what the crash handler
> writes — and both are marked *(round)* below. One accepted finding is about this document rather
> than the code: the parent may not be promoted while S6 is open.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md), [module_tests.md](../research/module_tests.md).

## The symptom

**The reader shipped on 2026-09-17 and has had nothing to read ever since.**

`server-notices.jsonl` is named in [notificationsFile.ts:41](../src_vs_code/src/notificationsFile.ts),
its path is derived by `serverNoticesPath`, and three modules already merge it with the extension's
own ledger — [notificationsGlance.ts:57](../src_vs_code/src/notificationsGlance.ts),
[notificationsPanel.ts:274](../src_vs_code/src/notificationsPanel.ts) and
[notificationsSnapshot.ts:70](../src_vs_code/src/notificationsSnapshot.ts). The panel section, the
page, the tab strip, the unread watermark and the derived count all handle its rows.

Nothing writes it. Every one of those surfaces reports on half the product.

| What happens | Where it is recorded today |
|---|---|
| Every refusal returned to the calling AI — 28 sites in `PanelService`, 25 in `ConsultationService` | nowhere durable |
| A reviewer that failed, timed out, hit a rate limit or returned unparseable output | the round's own summary, which is not kept |
| Settings this build cannot understand, storage notes, swept orphans, killed children | a Serilog line in a file nobody reads afterwards |
| **The process dying** | nowhere at all |

### Defect 4, verified rather than remembered

- `Main` ([Program.cs:204](../src_mcp/src/Program.cs)) has **no `try`, no `catch`, no `finally`**.
- `ServeAsync` catches exactly two types — [Program.cs:1531](../src_mcp/src/Program.cs) is
  `catch (Exception e) when (e is IOException or ObjectDisposedException)`.
- **`Log.CloseAndFlush` appears 0 times in the `src_mcp` source** — `grep -rc` finds it only inside
  `Serilog.dll` under `bin/`. [logging-serilog.md](../.agents/conventions/common/logging-serilog.md)
  requires it in as many words.

## What must be true when this is done

### 1. One record shape, two writers, and the second one is held to the first

`notificationLine` ([notifications.ts:291](../src_vs_code/src/notifications.ts)) is the contract, not
a reference. The .NET writer must produce a line the TypeScript parser accepts and that the
TypeScript serialiser would have produced from the same record:

| Rule | Where it is today |
|---|---|
| Non-printable characters removed — code point `>= 32` and `!= 127`, by NUMBER, never a literal or a class | `isPrintable`, and the comment saying a literal put a real NUL byte in the file twice |
| URL/query parameters whose NAME reads as a credential → `[redacted]` | `PARAMETER` + `namesACredential` |
| Three secret shapes: credentials in a URL authority, `bearer\|basic\|token <value>`, vendor key prefixes | `SECRETS` |
| A credential NAMED and then given in ordinary text — `password=letmein`, `"client_secret": "…"` | `LABELLED`, applied LAST so recognisable shapes are already gone |
| `detail` capped at 4096, every other string at 1000, cut text marked `…(truncated)` | `DETAIL_LIMIT`, `TITLE_LIMIT`, `limitFor` |
| **Every** string field redacted, identity fields included, by iterating the record | `notificationLine`, and the docstring recording that the exemption was tried and rejected |
| Unknown fields flattened from `more` BEFORE redaction | `notificationLine` |

**Bounded repetition everywhere**, because these run over text nobody chose. .NET adds an obligation
the TypeScript side does not have: `RegexOptions.NonBacktracking`, or a stated reason per pattern why
it cannot be used.

### 2. One credential word list — embedded at BUILD time, and it fails CLOSED *(round)*

The first draft said both languages read `shared/credential-words.json` from disk. **Two reviewers
independently found that this is unshippable and, worse, silently unsafe.** A published Native-AOT
binary and an installed VSIX both run where no `shared/` directory exists; the read throws; and
because every notice write is best-effort and swallows its exceptions (§4), redaction would then run
with an **empty list** and put raw credentials into the file. A security measure that degrades to
nothing on the one machine that matters is worse than not having it.

So `shared/credential-words.json` is the **build-time** source of truth and nothing reads it at
runtime:

- **C#** embeds it (`EmbeddedResource`) and reads it from the assembly.
- **TypeScript** gets a generated module, bundled by esbuild like every other import.
- **Neither may start with an empty list.** If the resource is absent or does not parse, the
  redactor **throws at construction** — fail closed, loudly, at startup, where a test and a smoke run
  can see it. This is the one place in this plan where best-effort is the wrong answer.
- **Both suites assert the shipped artefact**, not only the source tree: the C# test reads the
  embedded resource and the extension test reads the bundle, each compared against `shared/`.

The two lists stay two: `ANYWHERE` matches inside a name, `WHOLE_PART` only a whole part after
splitting on separators and camelCase. That distinction was earned by a real defect — `author`,
`assignee`, `signal`, `monkey` — so the JSON carries both lists, never their union.

### 3. The path is asked for, never composed

`SettingsFile.DataDirFrom`, which since 2026-09-18 IS `PanelSettings.DataDirectoryFor`.
[data-side-vectors.json](../shared/data-side-vectors.json) gains a `serverNoticesPath` per case,
asserted by both suites exactly as `settingsPath` and `logsPath` are — refused cases included, and
built with `Path.Combine`/`join` rather than string concatenation, which is why the existing pair
survives a separator difference.

### 4. A notice never takes a round down — and the append is MEASURED, not asserted *(round)*

Every write is best-effort: caught, counted, never thrown. A refusal that fails to be written is
still returned to the caller. That is why the instrumentation goes **inside** the two `Error` helpers
rather than at their 53 call sites.

Three reviewers asked what happens when two processes append at once. The answer is not a new
mechanism — it is the bargain [jsonlLedger.ts](../src_vs_code/src/jsonlLedger.ts) already documents,
and the parent plan already demanded that this step re-measure it:

- **`O_APPEND`, one write per line.** Measured by `npm run measure:append`, which forks real
  processes at one file and checks every line back: 8 × 1000 records including 60 KB lines on local
  NTFS, 8000 of 8000, 0 torn; and 4 × 200 over **SMB to a NAS share**, 800 of 800, 0 torn.
- **That measurement is of the Node writer.** The parent plan says so: *"The .NET append is not the
  system call the harness measured."* So this step **re-runs `measure:append` with the .NET writer
  as one of the processes**, and the outcome goes in the docstring beside the existing three. A
  number that has not been measured on the writer being shipped is not evidence.
- **If it tears, the sanctioned answer already exists** and is not invented here:
  one ledger per process (`server-notices-<pid>.jsonl`, the `chatOrphans.ts` precedent) with a glob
  in the reader.
- **A torn tail is quarantined rather than concatenated onto.** A process killed mid-line leaves a
  file that does not end in a newline, and the next append would join its record to the wreck —
  costing not one record but the one after it too. On its first write, a run checks the last byte and
  writes a newline first if one is missing.

### 5. A run says when it started, and the next one says it never finished *(round)*

Two reviewers were right that *"stale"* was undefined, and one named the exact failure: a marker
identified by PID alone is unsafe under PID reuse, and a naive sweep deletes a LIVE server's marker
and invents a death that did not happen.

- **The marker carries a run identity, not a pid**: `{ run, pid, host, startedUtc, heartbeatUtc }`,
  named for the `run` — the same id the record's `run` field carries.
- **Liveness is a heartbeat, not a pid.** [durable-status.md](../.agents/conventions/common/durable-status.md)
  and this repository's own scar say so: a pid is wrong the moment the data directory is a NAS, where
  the process that owns it is on another machine. A marker whose heartbeat is older than the agreed
  window is stale; a marker being refreshed is live, whoever owns it.
- **Recording is idempotent.** A run that dies after writing the unclean-exit record but before
  removing its marker must not produce a second record on the next start, so the record is keyed and
  the marker is removed only after the record has landed.
- **One marker per run, never a singleton** — the parent's *H*: a singleton cleared by one clean exit
  erases the evidence that the other process was killed.

### 6. The host is wrapped, the crash is WRITTEN, the log is sanitised, the flush is guarded *(round)*

This plan is titled *"and that it died"* and its first draft only logged. Corrected:

1. `catch` writes a **crash notice through `ServerNotices.Append`** — the exception type, the
   sanitised message, the frames — so the death is in the file the page reads, not only in a log.
2. **The exception text is redacted before it is logged**, with the same redactor. A reviewer failure
   carrying an authorization URL or a bearer token would otherwise put the secret in a Serilog file:
   redacting the notice and not the log is `security.md`'s named defect, applied to two sinks.
3. `Log.CloseAndFlush()` in a `finally`, itself guarded — a `finally` does run when the `catch`
   throws, so the reviewer's premise was inexact, but the flush can throw on its own and must not
   replace the exception that is being reported.
4. The marker is cleared **only on clean termination**, and the exit code is non-zero.

### 7. Its growth is bounded before its first write — worst case, not a hopeful average *(round)*

The first draft carried the parent's 400 B/record. A reviewer was right that the schema permits far
more: `detail` alone may be 4096, and several other fields 1000 each.

| | per record | 300/day |
|---|---|---|
| **Typical** — a refusal sentence and its context | ~400 B | 44 MB/year |
| **Worst case the serialiser permits** | ~10 KB | **1.1 GB/year** |

So the bound is stated as a rule rather than an average: the typical figure is what this will do, and
the worst case is what it *could* do, which is why the ceiling is named now. **No rotation and no
sampling ship here** — but the trigger is written down: if the file passes **256 MB**, the roll-up is
built before anything else is added to this plan's family. The owner is whoever next touches this
file, and the DoD of that work is the retention job, not another measurement.

### 8. Coverage is enumerated mechanically, not promised *(round)*

"Every refusal" and "every reviewer failure" are claims a list can make and a codebase can quietly
break. A refusal returned from a branch that bypasses the two `Error` helpers, or a reviewer that is
cancelled before a summary exists, would be missing while the plan said otherwise.

So the sites are a **ratchet**, in the shape this repository already uses for
`notification-sites.json`: a generated census of every refusal return and every reviewer terminal
state, a count that may only fall, and a test that fails when a new site appears uninstrumented.

## Build order

**A. The serialiser and its list.** `shared/credential-words.json`; the embedded C# resource and the
generated TypeScript module; `ServerNotice` and its serialiser. The parity property. No call sites.

**B. The append.** `ServerNotices.Append`, best-effort, through `DataDirFrom`; the torn-tail
quarantine; the `serverNoticesPath` vector; `measure:append` re-run with the .NET writer.

> **Carried into 1.4 by story 1.3's code round**, written down here rather than remembered:
> - the writer's ONLY file-opening path goes through `ServerNotices`, with a test that invokes the
>   writer and **inspects the emitted JSONL** — not only that the file is at the shared resolved
>   path, but that what landed in it went through `ServerNoticeLine.Of`. Story 1.2's round named the
>   gap exactly: until the serialiser is wired to the writer, a call site can serialise a notice
>   directly and the parity harness stays green, because it exercises `NoticeTool` rather than the
>   product's write path;
> - a reviewer asked for an opaque resolved-data-directory TYPE, so that a root path cannot satisfy
>   the writer's seam. `DataDirFrom` returns `string` and has seven callers, so the cost is only
>   visible once a writer exists: **decide it in 1.4**, and say which way and why;
> - the PUBLISHED Native-AOT artefact is exercised — `CredentialWords.EnsureLoaded` is the hook that
>   turns a missing embedded list into a startup refusal a smoke run can see (carried from 1.1);
> - **the LIVE cross-implementation check is story 2.4's seam leg.** Two reviewers have now called
>   it Blocking, at 1.1 and at 1.3, and both times the honest answer was the same: there is nothing
>   live to check before a writer exists. When 2.4 lands it must drive the real binary and read the
>   file with the extension's own parser, and until then the fixture agreement is what there is.

> **Discharged by story 1.4, 2026-09-21** — each answered where it was asked to be:
> - the writer's only road onto the disk is `ServerNotices.Append`, and it is held by an ALLOWLIST
>   rather than by a name count: `TheOneAppendTests` enumerates every production call site of
>   `JsonlLedger.AppendLine` and refuses a third. `ServerNoticesAppendTests` reads the file back and
>   compares the BYTES with `ServerNoticeLine.Of`;
> - the type is **yes**: `ResolvedDataDir` (in `ServiceDefaults`, beside `CoaiLogPath`), minted by
>   `PanelSettings.DataDirectoryFor` and by nothing else — an internal constructor, one granted
>   assembly, and a census that asks the ASSEMBLY which members answer the type. `DataRootFor` keeps
>   returning a string deliberately: giving the root the same type would make the type describe the
>   confusion instead of preventing it. `CoaiLogPath.RootFor` and `UsageLedger` keep strings because
>   `coai-bugs` resolves its own directory by its own rule and would otherwise mint a value whose
>   guarantee it does not have;
> - `CredentialWords.EnsureLoaded()` is the first statement of **`ServeAsync`**, not of `Main`. The
>   plan round refused `Main`: `.agents/PROJECT.md` makes the one-shot CLI shape a non-negotiable,
>   and `--version` must not be able to fail on a word list it never uses. Nothing is lost — the
>   release smoke runs a real `initialize` over stdio against the PUBLISHED binary.
>
> **What story 1.4's measurement found, which no round predicted.** §4's insistence that the .NET
> append be MEASURED rather than argued from the node result was right in the way nobody wanted:
> `FileMode.Append` is **not an append**. It is a positional write at the offset .NET remembered when
> it opened, probed directly on Windows 11 and on Linux/ext4 with the same answer, and at scale it
> cost **2488 of 8000 records** to eight processes where node's writer lost none. `UsageLedger` has
> written that way since it was built, so two servers sharing one data directory — which its own
> docstring calls the normal case — have been overwriting each other's spending rows. Both ledgers
> now go through `AppendOnlyFile` (`FILE_APPEND_DATA` / `O_APPEND`, the same system call node makes),
> and the same run answers 8000 of 8000. The sanctioned fallback in §4 — a ledger per process — was
> therefore NOT needed, and the reason it was not is written down: the shared file was never the
> problem.
>
> **Handed on by story 1.4**, written down for the same reason:
> - **the extension's own ledger has the torn tail this story fixed on the server side.**
>   `jsonlLedger.appendLine` does not read the last byte, so a host killed mid-append costs
>   `notifications.jsonl`, `chat-usage.jsonl` or the door ledger the NEXT record as well as the
>   fragment. It is ten lines and one test on that side and it belongs to this plan's parent, which
>   owns all three files;
> - **a ceiling on the server's ledger is owed by the first story that adds a REPEATING site.** The
>   extension's `suppression.ts` bounds a ledger with repeating writers; the server has none yet, so
>   a bound here would bound nothing — but 2.2 and 2.3 are where one appears;
> - **2.4 still owes the live leg**, now for the third round running, and now with something live to
>   check.

**C. The instrumentation and its ratchet.** Both `Error` helpers; `ReviewerSummaryFactory.Describe`
([BoundedScheduler.cs:489](../src_mcp/runners/Reviewers/BoundedScheduler.cs)); `LiveRound.Report`
([LiveRound.cs:65](../src_mcp/src/Server/LiveRound.cs)); the startup notes — `Unrecognised`,
`StorageNotes`, and the adoption sentence `SettingsFile.Layer` hands its caller. The census.

**D. The run marker and defect 4.** They ship together: the marker is what records the deaths the
`catch` cannot see.

**E. The seam leg.** The trigger is a **real refusal over stdio**, not a test-only CLI mode — the
seam suite already drives the real binary that way, and a refusal provoked by a bad tool argument
exercises the shipped path instead of one built for the test. A secret-bearing record is written by
the real binary and read by the extension's own parser, asserted on the persisted BYTES.

## Test plan

Every item RED first, with its failure message recorded, and each guard broken to prove it.

1. **Parity as a property**, over every shape rather than a list of cases: a bearer header, a URL
   authority credential, `?api-version=` (which must survive), `?access_token=` (which must not),
   `password=letmein`, each vendor prefix, a NUL, a string past each limit — through both
   serialisers, asserting the same output.
2. **Every string field is redacted**, a secret in each one, none reaching the line.
3. **A `code` literal is never rewritten** by the redactor, over the catalog.
4. **The list fails CLOSED**: with the embedded resource absent, construction throws rather than
   redacting with an empty list — and the shipped artefacts are asserted, not only the source tree.
5. **A failing disk does not fail a refusal.**
6. **A torn tail does not poison the next record.**
7. **A live marker is not swept** and a dead one is, by heartbeat rather than pid; and the
   unclean-exit record is written exactly once across a repeated start.
8. **A third exception type** leaving the host writes a crash NOTICE, logs a redacted exception,
   flushes, and exits non-zero.
9. **An empty `server-notices.jsonl`** is the normal case and reads as no rows, not as an error.
10. **The seam**: bytes on disk, the real binary, both parsers.

## What this plan deliberately does not do

- **No rotation and no sampling** — §7 names the trigger and the owner instead.
- **No new page, panel section or count.** S1–S5 shipped all of it and already handles these rows.
- **It does not touch `src_server`** — that is
  [PLAN_refusals_that_explain_themselves.md](PLAN_refusals_that_explain_themselves.md).

## The boundary with the parent plan

| Item | Built by |
|---|---|
| `server-notices.jsonl`: writer, serialiser, redaction, path, caps | **this plan** |
| Reading, merging, counting and showing those rows | the parent, S1–S5, shipped |
| The run-start marker on the EXTENSION side | the parent's *H* |
| The run-start marker on the SERVER side | **this plan** |
| Defect 4 | **this plan**, by the parent's own ordering |
| The rounds log (S6) | the parent — and it is what stops the parent being promoted here |

## Epics and stories

Split 2026-09-21 on the operator's instruction, by Fable at its highest version, against the
repository rather than against this document. **A story is a branch, a gate round and a pull request**,
because this project's gate holds one session per repo+branch and closes it when the code round ends.

### Epic 1 — the writer: one list, one line, one path, one append

| | Story | Depends on | Model |
|---|---|---|---|
| 1.1 | One credential word list, embedded into both halves, and neither starts without it | — | Opus |
| 1.2 | The server's notice line is the extension's line, held as a property rather than a resemblance | 1.1 | **Fable (max)** — this IS the security measure |
| 1.3 | The server-notices path is asked for, and the shared vectors hold both halves to it | — | Opus |
| 1.4 | The server appends best-effort, quarantines a torn tail, and the .NET append is measured | 1.2, 1.3 | **Fable (max)** — silent record loss |

### Epic 2 — the sites: counted first, then instrumented, then proved over the wire

| | Story | Depends on | Model |
|---|---|---|---|
| 2.1 | Every refusal road and every reviewer ending is counted, and the count only falls | — | Opus |
| 2.2 | Every refusal returned to the calling AI is written down, inside the helpers that return it | 1.4, 2.1 | Opus |
| 2.3 | A reviewer that fails, and a setting this build cannot read, reach the file the page reads | 2.2 | Opus |
| 2.4 | A real refusal over stdio lands with its secret taken out — asserted on the bytes | 2.2 | Opus |

### Epic 3 — the deaths

| | Story | Depends on | Model |
|---|---|---|---|
| 3.1 | A run says when it started, keeps a heartbeat, and the next start records the one that never finished | 1.4, 2.2 | **Fable (max)** — liveness across a NAS, PID reuse, exactly-once across a crash |
| 3.2 | A third exception type is written down, redacted in the log, flushed, and exits non-zero | 1.4, 2.2, 3.1 | **Fable (max)** — exit semantics and a second redaction sink |

Order: 1.1 → 1.2 → 1.3 → 1.4 → 2.1 → 2.2 → 2.3 → 2.4 → 3.1 → 3.2. 1.3 may run beside 1.1/1.2.

### What the split found that this plan had wrong

Six corrections, all verified in the repository before they were written down:

1. **There are THREE refusal roads, not two.** `PanelService.Refused` ([PanelService.cs:709](../src_mcp/src/Server/PanelService.cs))
   serves document rounds through a `DocumentTurn.Refused` and never touches `Error`. This document
   said "both helpers"; 2.2 routes the third, and 2.1's census is what stops a fourth appearing
   unnoticed.
2. **`Log.CloseAndFlush()` does not exist to be called.** This repository's logger is an INSTANCE —
   `using var log = CoaiLogging.CreateDewFlowLogger(...)` inside `ServeAsync` — not the static
   `Log.Logger`, so §6.3's wording describes an API this code does not use. 3.2 is two layers
   instead: a guarded wrapper inside `ServeAsync` where the logger exists, and a last-resort catch
   in `Main` for a throw that happens before there is one.
3. **The .NET append precedent already exists and is not invented here.**
   [UsageLedger.Append](../src_mcp/runners/Reviewers/UsageLedger.cs) is `FileMode.Append` +
   `FileShare.ReadWrite`, one `Write` per line, under an in-process lock, swallowing `IOException`
   and `UnauthorizedAccessException` with a comment saying why. `ServerNotices.Append` follows it.
4. **A C# test cannot drive the TypeScript serialiser.** CI runs the .NET tests before `npm ci`, so
   the parity property needs a third artefact: a companion executable built by the solution and
   never shipped (the `FakeCli` pattern), driven by a new `test:parity` script from the extension
   job. A `shared/notice-vectors.json` would have been simpler and is a LIST of cases, which this
   plan forbids in words.
5. **The measurement cannot use the real serialiser.** `measure-append.mjs` checks each line's
   `pad.length`, and the serialiser caps `detail` at 4096 — so every long line would read as torn.
   1.4 measures the append PRIMITIVE, the same `FileStream` call, with the harness's own shape, and
   the docstring must say that the atomicity claim covers the system call and not the serialiser.
6. **`shared/data-inventory.json` is not mentioned anywhere above** and has a test that every thing
   in the data directory is named — this repository has already shipped a commit about that
   inventory naming four of the sixteen things it moves. `server-notices.jsonl` (1.3) and `runs/`
   (3.1) are added there or an unrelated suite goes red.

### Costs accepted with open eyes

- **Every panel refusal gets one `code`.** Instrumenting inside a `static Error(sentence)` means the
  helper knows only the sentence, so all 27 panel refusals share `code: refused` with the sentence as
  `title`, and the page groups them into one row — the opposite of what the extension's per-site
  `code` literal was designed for. The alternative is `Error(code, sentence)` at 51 call sites, which
  this plan chose not to do. If the grouping proves useless, a follow-up adds the literals and 2.1's
  census is what will hold it to completeness.
- **`Describe` is not edited.** It lives in `runners` and is a pure sentence; instrumenting it would
  make that assembly know about the ledger. 2.3 writes at `LiveRound.Report`, where the outcome is
  OBSERVED, and uses `Describe` for the title.
- **A sleeping laptop is a false death.** A machine asleep longer than the stale window has its
  marker swept by another server on the same NAS and an `unclean-exit` recorded; its next beat
  re-creates the marker, so its own clean exit still clears, but the false record stands.
  `CollectRuns` solves the same problem with "a beat takes a swept run back" because its row
  survives — a deleted marker cannot be taken back. This is the residual the heartbeat-over-pid
  decision leaves and 3.1's docstring owns it.
- **The bundle assertion is weaker than the C# one.** `dist/extension.js` is minified and exports
  only `activate`/`deactivate`, so 1.1 can assert that every word survives as a quoted literal and
  cannot prove the redactor USES them; `--check` on the generated module is the real guard.

### The largest unknown

**`\b` and case folding under `RegexOptions.NonBacktracking`.** .NET's `\b` is Unicode-aware; the
JavaScript `SECRETS` patterns use `gi` without `u`, so theirs is ASCII. Around Cyrillic prose — which
this product's ledgers demonstrably carry — the two may disagree about where a word begins. It is
measured by running the parity property with every shape wrapped in Russian and German prose. If they
disagree, the fix is a lookaround, which `NonBacktracking` does not support, and §1's "a stated
reason per pattern" clause is what applies.

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded, and each guard broken.
- [ ] One credential list, embedded at build time into both deliverables, failing CLOSED, asserted
      against `shared/` by both suites **through the shipped artefact**.
- [ ] The two serialisers agree as a property over every shape.
- [ ] `measure:append` re-run with the .NET writer as one of the processes, outcome in the docstring.
- [ ] No write can fail a round; proved by a test.
- [ ] `research/module_server.md` records the writer, the redaction, the append bargain and the run
      marker; `research/module_tests.md` names the scenario and the seam leg.
- [ ] A `coai-mcp` release.
- [ ] Through `review_plan` (done — 19 findings, 15 accepted) and `review_code` per story.
- [ ] **S6 is extracted into its own `todo/` plan, and only THEN is the parent promoted** *(round)*.
      Promoting a plan with an open phase is what `planning-docs.md` forbids in as many words, and
      the first draft of this DoD asked for exactly that. The parent's own status line is corrected
      in the same task: it says *"Defect 2 remains"* while defect 2's heading says **BUILT
      2026-09-18** and its plan was promoted that day.
