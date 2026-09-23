# PLAN — the server writes down what it refused, what failed, and that it died

> Status: **IMPLEMENTED, 2026-09-23.** Epic 1 (1.1 the credential list, 1.2 the notice line, 1.3 the
> path, 1.4 the writer and the append it had to fix) shipped 2026-09-21. Epic 2 shipped 2026-09-22:
> 2.1 bounded the population to ONE refusal road (`Refusal.Answer`), 2.2 wrote every refusal down
> through one writer thread, 2.3 was split into three and wrote down the drain, reviewer failures and
> startup notes, and 2.4 proved the whole thing over the wire. **Epic 3 shipped 2026-09-23**: every
> run keeps a heartbeat in `runs/{run}.json` and the next start records one that never finished,
> exactly once; every notice carries the run and pid that wrote it; and an exception nothing else
> caught is written down, said redacted, flushed and exits 70 — in two layers, because the first
> thing a server can fail at is resolving the directory its logger lives in. The deviations are under
> *What epic 3 found*. **Open tail, not built here:** the `coai-mcp` release that carries epic 3 (a
> release is the operator's call). The parent stays in `todo/` on that release AND on the
> EXTENSION half of its section *H*'s run marker — the boundary table below gives that half to the
> parent, and nothing built it.
> Scope: `src_mcp` — a notice record and its serialiser, the append, the instrumentation sites, a
> run-start marker, and the `try/catch/finally` that `Program.cs` has never had.
>
> **This is S8 of [PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md)**,
> extracted into its own file as that plan's section *G* says it must be. Defect 4 of the parent
> comes with it, for the reason the parent gives: it changes the `coai-mcp` binary, and its
> externally-killed half depends on the run marker this step builds.
>
> Its declared prerequisite has shipped. The parent ordered S8 after
> [PLAN_the_settings_file_ignores_the_side.md](PLAN_the_settings_file_ignores_the_side.md);
> that landed on 2026-09-18 and went out as **`coai-mcp 0.30.0`**.
>
> **Through its plan round, 2026-09-21: three reviewers, 19 findings, 15 accepted.** The round
> changed the shape of two things — where the credential list lives, and what the crash handler
> writes — and both are marked *(round)* below. One accepted finding is about this document rather
> than the code: the parent may not be promoted while S6 is open.
>
> Related docs: [module_server.md](module_server.md),
> [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).

## The symptom

**The reader shipped on 2026-09-17 and has had nothing to read ever since.**

`server-notices.jsonl` is named in [notificationsFile.ts:41](../src_vs_code/src/notificationsFile.ts),
its path is derived by `serverNoticesPath`, and three modules already merge it with the extension's
own ledger — [notificationsGlance.ts:57](../src_vs_code/src/notificationsGlance.ts),
[notificationsPanel.ts:274](../src_vs_code/src/notificationsPanel.ts) and
[notificationsSnapshot.ts:70](../src_vs_code/src/notificationsSnapshot.ts). The panel section, the
page, the tab strip, the unread watermark and the derived count all handle its rows.

Nothing wrote it, and every one of those surfaces reported on half the product — until story 2.2,
2026-09-21, which instrumented the refusal road. The table below is the symptom AS IT WAS.

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
rather than at their 53 call sites — and story 2.1 then found that both helpers reach ONE boundary,
`Refusal.Answer`, so 2.2 instruments a single point rather than two. Best-effort also turned out to
mean NOT WAITING: the write is handed to one thread behind a bounded queue, because a 2 s budget on a
stalled share still keeps a pool worker forever (2.2's code round, twelve findings).

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
the worst case is what it *could* do, which is why the ceiling is named now.

> **CORRECTED by story 2.2, 2026-09-21.** This section said *"no rotation and no sampling ship here"*
> and left 256 MB as a trigger for a roll-up whoever touched the file next would build. Story 2.2's
> plan round refused that on the convention, and was right to:
> `.agents/conventions/common/planning-docs.md` requires a plan that creates something that GROWS to
> name its budget, its owner and its retirement rule **before the first write**, and 2.2 is the first
> story with a repeating writer — 48 refusal sites, every one reachable on every round. So the
> retirement rule shipped with it: `ServerNotices.Append` rolls the live file to
> `server-notices.1.jsonl` at **128 MB**, two generations, which makes 256 MB a hard MAXIMUM for the
> pair instead of a trigger for unscheduled work. The per-record figure is a fact rather than an
> estimate now as well: `Redaction.SafeText` cuts every string field to `TitleLimit`, and
> `TheWorstCaseLine_IsWithinTheDocumentedCeiling` asserts a megabyte of refusal sentence produces a
> line under 10 KB, measured on the file's bytes.

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
> - ~~**the LIVE cross-implementation check is story 2.4's seam leg.**~~ **Discharged by 2.4,
>   2026-09-22.** Two reviewers called it Blocking, at 1.1 and at 1.3, and both times the honest
>   answer was that there was nothing live to check before a writer existed. There is one now: the
>   seam's sixth leg drives the real binary over stdio and reads the file with the extension's own
>   path function, reader and parser, asserted on the persisted bytes.

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

- ~~**No rotation and no sampling**~~ — **shipped with 2.2** instead, at its plan round's insistence:
  the live file rolls to `server-notices.1.jsonl` at 128 MB, two generations, so §7's 256 MB is a hard
  maximum rather than a trigger. See the correction in §7.
- **No new page, panel section or count.** S1–S5 shipped all of it and already handles these rows.
- **It does not touch `src_server`** — that is
  [PLAN_refusals_that_explain_themselves.md](../todo/PLAN_refusals_that_explain_themselves.md).

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
| ~~2.1~~ | ~~Every refusal road and every reviewer ending is counted, and the count only falls~~ — **shipped 2026-09-21** as a BOUNDARY rather than a count (PR #449): `Refusal.Answer` is the one place an `ErrorAnswer` is built, and `shared/refusal-sites.json` carries the numbers | — | Opus |
| ~~2.2~~ | ~~Every refusal returned to the calling AI is written down~~ — **shipped 2026-09-21**: written at `Refusal.Answer`, handed to ONE writer thread behind a bounded queue so the refusal waits for nothing, `subject` from `[CallerMemberName]`, and the file's ceiling (§7) ships with it rather than as a trigger | 1.4, 2.1 | Opus |
| ~~2.3~~ | **SPLIT into three on the gate's own command, 2026-09-22** (the split was done by Fable against the code, and found two things the plan round had assumed wrong — see below) | 2.2 | — |
| ~~2.3.1~~ | ~~A notice offered before the process leaves still lands, and the ledger's promise says what it delivers~~ — **shipped 2026-09-22**: `NoticeWriter.Drain` waits on the writer TASK, `ServeAsync` drains in a `finally` covering both exits, and the delivery promise is one paragraph in `module_server.md` instead of two sentences that disagreed | 2.2 | Opus |
| ~~2.3.2~~ | ~~A reviewer that fails is written down once, by a writer the host owns, and a sixth ending cannot arrive unnamed~~ — **shipped 2026-09-22**: `Noticing` is the shared seam and boundary, the host holds one instance across settings rebuilds, and `ReviewerNotices.ByType` is data a reflection census holds to the assembly's sealed subtypes | 2.3.1 | Opus |
| ~~2.3.3~~ | ~~A setting this build cannot read, a directory that surprised it, and a settings file it adopted reach the page~~ — **shipped 2026-09-22**: keys and kinds typed at the source, `kind:place` subjects, canonical paths, and the log lines kept beside the notices. The code round took the settings RELOAD off the owed list below and into the story, and turned the storage class from a condition with a default into a map with a census | 2.3.2 | Opus |
| ~~2.4~~ | ~~A real refusal over stdio lands with its secret taken out — asserted on the bytes~~ — **shipped 2026-09-22** as the seam's SIXTH leg: the real binary refuses `open` on a path that carries a token, the extension's own `coaiDataDir` + `serverNoticesPath` + `readServerNotices` find the record, and `notificationLine(parseNotificationLine(line))` gives the line back byte for byte. Deviations below | 2.2 | Opus |

**Owed by what 2.3 found, and not done inside it:**

- ~~**The settings REBUILD path.**~~ **Done inside 2.3.3 after all, 2026-09-22.** Written here as
  owed and out of scope; five findings across three vendors on that story's code round said no, and
  they were right — the rebuild runs whenever the panel writes the file, so a person typing a bad
  value was the likeliest case and the only silent one. `PanelServiceHost.Build` now logs each
  mismatch and writes it through the host's own `Noticing`, and an adoption on that path goes both
  ways too. The host's FIRST build stays silent (startup has already said those) and the disk survey
  is not re-run (it would stat a configured NAS on a settings change, issue #115).
- **`Canonical` does not fold case.** A person who edits `COAI_DATA_DIR`'s casing between restarts
  gets two rows for one settings file, because `Path.GetFullPath` normalises separators and dot
  segments but preserves casing. Traded away on 2.3.3's code round rather than fixed: the subject is
  also what the panel DISPLAYS, so folding it shows a Windows person a path their own shell prints
  differently, and resolving the filesystem's true casing is a disk call on a directory that may be
  an unreachable mount. (codex, minor.)
- **`SweptRounds`, `SweptConsultations`, `KilledReviewers`.** Three codes in `ServerNoticeCodes` with
  no owner story. The events are in the `PanelService` constructor, which runs at startup AND on
  every rebuild; the symptom table at the top of this plan names them ("swept orphans, killed
  children") and §C does not. They need a story or a sentence retiring the codes.
- **One-shot modes lose a refusal notice.** `--close-consult` builds a `PanelService` and can refuse;
  it takes `Noticing.None`, so that refusal is answered on stdout and written nowhere. Deliberate
  (a one-shot must not start a writer thread nobody drains) and worth revisiting if those refusals
  turn out to matter.

**What the split found that the plan round had wrong.** (1) An exception escaping `LiveRound.Report`
does NOT kill the round — `BoundedScheduler.cs:359-376` already wraps `onProgress?.Invoke` in a
`catch (Exception)` with the comment *"Reporting is not the work."* What it costs is different and
real: the progress lambda at `PanelService.cs:1249-1265` runs `live.Report`, then `audit.Moved`, then
`_ledger.Record` in sequence, so a throw in the first skips that reviewer's audit line and its
spending row — 2.3.2 claims that consequence. (2) `PanelSettings.Unrecognised` can be typed without
parsing prose: all three of its sources know their key where the sentence is built
(`PanelSettings.cs:736-779`), so a `(Key, Sentence)` pair costs edits in ONE file — but the list is
also on the wire (`ProvidersAnswer.Unrecognised`), so it stays as a projection. That is 2.3.3's.

**What story 2.4 shipped differently from what it planned.**

- **It does NOT prove the drain, and it said it would.** The plan listed "the drain removed from
  `ServeAsync`'s `finally` — the file is missing" among its teeth. Planted, that stayed GREEN: one
  refusal is written by the writer thread long before stdin closes, so the drain never has anything
  to do on this road. The drain is story 2.3.1's and is proved by its own tests. The leg still ends
  its session by EOF rather than by kill, because that is how a real client leaves and it is the
  only exit on which the file is guaranteed complete — but that is a correctness condition of the
  leg, not something the leg demonstrates.
- **It found a third notice producer 2.3.3 had missed.** `ServerNotice.Shortened` was introduced
  there as "one helper, because there were two"; there were three, and `RefusalNotices` kept a
  plain cut, so an over-long refusal was truncated with no mark while an over-long startup note
  said it was cut. Fixed here, RED first, and `ANoticeProducerCutsOnlyThroughTheSharedHelper` now
  lists every producer instead of remembering them.
- **It checks more sinks than the notices file.** The plan round (codex) pointed out that a
  refusal's sentence could land in a log line as easily as in the notices file, and the operator's
  standing rule is that no secret reaches a log line. So the leg also asserts the secret is absent
  from the server's stderr and from EVERY file the run left under its data root — and asserts that
  the run did leave a log file, so that check is not vacuous.
- **A defect in the leg itself, found by one of its own plants.** When a check failed before the
  clean close, the cleanup removed the directory while the dotnet child still held its files open;
  on Windows `rmSync` threw `EPERM`, and a stack trace was printed where the leg's own sentence
  belonged. `session.killAndWait()` now waits for the process to be gone, and a removal that still
  fails is SAID rather than thrown past the reason.
- **What its code round changed.** The session reads a child's end at `close`, not `exit`: Node
  documents that stdio "might still be open" at `exit`, and the stderr claim needs the whole stream
  (codex — not reproduced here, 0 of 25 runs of a 4 MB burst, and fixed on the documented contract).
  A timeout kill now waits for the process too, and the close is remembered from spawn time so a late
  caller cannot hang. The leg moved into its own module, `scripts/seam-refusal.mjs`, because
  `run-seam.mjs` had passed the repository's 800-line ceiling with it; the runner is 792 lines again
  and the leg's own body is 25. The producer census catches `Substring(0, Redaction.…Limit)` as well
  as a range, and says what it cannot catch — a limit copied into a local — and why the companion
  answers that.
- **What CodeRabbit found on the pull request**, both in the harness and both reproduced RED first:
  a write in flight to a dying server's stdin was an unhandled `'error'` event that ended the whole
  run before cleanup (`Error: write EOF`, 3 of 3 without a listener), and a malformed line merely
  mentioning a refusal was handed to the parser, whose `undefined` then threw past cleanup. The
  session moved into `scripts/seam-session.mjs` so a test could load it at all, and
  `seamLegs.test.mjs` holds both.

### Epic 3 — the deaths

| | Story | Depends on | Model |
|---|---|---|---|
| ~~3.1~~ | ~~A run says when it started, keeps a heartbeat, and the next start records the one that never finished~~ — **shipped 2026-09-23**: `RunMarkers` (a pure planner and the disk it acts on), `RunLife` (the beat, on a thread of its own), and `Noticing.Stamped`, which puts this run's id and pid on every notice it writes | 1.4, 2.2 | **Fable (max)** — built on Opus 5.5 by the operator's choice, one gate for the epic |
| ~~3.2~~ | ~~A third exception type is written down, redacted in the log, flushed, and exits non-zero~~ — **shipped 2026-09-23**: `HostCrash`, the third `catch` in `ServeAsync`, `Program.EndedAsync`, and a last-resort catch in `Main` | 1.4, 2.2, 3.1 | as 3.1 |

### What epic 3 found

- **A rename is not a claim — measured, and found by the race test before anything shipped.** The
  first design claimed a death by renaming its marker, on the reasoning that a rename is atomic. Two
  sweepers racing over fifty stale markers recorded **72** deaths. Isolated: two threads renaming one
  file to two targets through .NET on Windows had BOTH renames succeed in **975 of 1000** attempts;
  `FileMode.CreateNew` let both succeed in **0 of 1000**. So a claim is an exclusive create of
  `runs/{run}.claim`, and a claim's AGE says whether the sweeper holding it is still alive.
- **An exclusive create was not enough either: 68–75 records for 50.** A released claim could be won
  again by a sweeper still holding a listing from before the first one finished. Two invariants fixed
  it: the marker is removed BEFORE the claim is released, and the marker is re-read UNDER the claim
  (`StillDead`) so a sweeper acts on what is there now rather than on what it listed. After: 50 of 50
  across eight runs, and a stress run of four sweepers over 500 deaths recorded each exactly once,
  three times out of three.
- **Liveness is a heartbeat, refined on the same machine.** A stale marker written on THIS host is
  checked against the process table too — pid AND start time, through the very function the orphan
  sweep already uses — so a server stalled under a debugger is not recorded as a death. (gemini and
  local, the plan round.) From another machine on the share, a sleeping laptop is still a false
  death, as *Costs accepted* says.
- **The crash record goes through the CONFIRMED append**, not the queue, and the marker is cleared
  only when the run ended cleanly or its crash is known to be on disk; a crash that could not be
  written leaves the marker behind on purpose, so the next start records an unclean exit instead of
  nothing. (local, the plan round.) The beat stops before the clear, so a late beat cannot re-create a
  cleared marker (gemini). The ORDER — stop, drain, clear only over a landed crash, flush — is pinned
  as a whole by `TheHostDrainsOnEveryRoadOut`, which used to pin the one-line `finally` it replaced.
- **Two layers, and the second was not optional.** The plan's correction 2 named a last-resort catch
  in `Main`, and the real-binary test showed why: an unusable `COAI_DATA_SIDE` throws while the data
  directory is resolved — before the logger, which is rooted in that directory — and its message
  QUOTES the value. RED, it printed `Unhandled exception. … COAI_DATA_SIDE='token sk-ant-api03-…'` to
  stderr in clear. `HostCrash.Unlogged` now says it redacted and exits 70, and writes no notice: the
  directory a notice would go to may be the very thing that failed. If the redactor itself cannot run
  (the word list loads lazily), what is said is the exception's TYPE, never its raw message.
- **`Log.CloseAndFlush` is still not called, on purpose.** This host's logger is a local instance, not
  Serilog's static one, so the rule's intent is met as `HostCrash.Flushed(log)` — the host's own
  logger disposed LAST, under a guard, on every road out.
- **`runs/` joined `shared/data-inventory.json`** as live state (`move: false`): a copied live marker
  would be recorded at the destination as a death it is not. The extension's inventory scan could not
  have found it — the folder is composed from a constant on `dataDir.Path`, which its patterns do not
  match — so a C# test holds `RunMarkers.Folder` to the inventory instead.
- **A plant that stayed green found a gap in the scenario.** Naming the marker with one run id and
  stamping the run's notices with another passed the first end-to-end test, because nothing joined
  the death to the dead run's OWN records. The scenario now makes the run write a real refusal over
  stdio before it is killed, and asserts the death carries that refusal's run id.
- **A beat could die in silence, found re-reading the story before its code round.** `Write` names
  the disk's two failures; anything else escaped the loop, faulted a task nobody observed, and ended
  the beat without a word — the live run's marker then goes stale and a peer records a death that is
  not one. RED on a clock that throws (*"to have an item matching … heartbeat"*, with nothing
  logged at all); each beat now has the sweep's catch-all, says what failed, and the next one tries.
- **One locator, not four.** Three process-level test classes each carried an identical copy of the
  code that finds the `coai-mcp` binary; the epic's scenario would have been the fourth. It is
  `tests/ServerBinary.cs` now, and all four use it.
- **What the code round changed — one round for the whole epic, 23 findings, 5 accepted.** codex
  could not answer (its configured model is refused on a ChatGPT account), so eight of twelve
  reviewers ran. Accepted: the four marker types are `internal`, not `public`; and a stop that
  arrives while the start-up sweep is still recording deaths is honoured BETWEEN deaths — the one
  being written finishes, the rest stay on disk for the next start — where it used to hold a quick
  exit for the full stop budget and then blame a heartbeat (gemini; RED first, three deaths recorded
  after the stop). The same pass removed a second resolution of the data directory inside
  `ServeAsync` that contradicted the comment saying it was resolved once. Rejected, each with its
  reason in the gate's record: a crash NOTICE from the one-shot modes (they answer on stdout, and
  `Main`'s throw may be the directory itself failing), a UI spinner in a third-party client, and
  ten findings the code refutes — among them that `RunLife.Start` runs inside the `try`, that
  `Read()` scans on every beat, and that a vanished marker is recorded anyway.

Order: 1.1 → 1.2 → 1.3 → 1.4 → 2.1 → 2.2 → 2.3 → 2.4 → 3.1 → 3.2. 1.3 may run beside 1.1/1.2.

### What the split found that this plan had wrong

Six corrections, all verified in the repository before they were written down:

1. ~~**There are THREE refusal roads, not two.**~~ **CORRECTED by story 2.1, 2026-09-21: there were
   two, and now there is ONE.** The split wrote that `PanelService.Refused` *"serves document rounds
   through a `DocumentTurn.Refused` and never touches `Error`"*, and said it was verified in the
   repository. It was not what the code did:

   ```csharp
   private string Refused(string sentence)
   {
       _log.Warning("review_document refused: {Why}", sentence);

       return Error(sentence);            // a logging wrapper in FRONT of Error, not a road past it
   }
   ```

   The wire shape of a refusal was built in exactly two places — one `new ErrorAnswer(` in each
   service — and story 2.1 made it one: both now go through `Refusal.Answer`, held to a single file
   by `TheRefusalRoadsAreCountedTests`. So 2.2 instruments ONE point, and "every refusal is written
   down" is a fact about the type rather than a number anybody maintains.

   **This is the correction the census existed to make.** A claim about how many roads there are is
   exactly the kind a person verifies once, writes down, and is then wrong about for a year — which
   is what happened here, in this document, in the sentence that said it had been checked.
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

- **Every panel refusal gets one `code`** — but not one ROW. Instrumenting inside `Error(sentence)`
  means the helper knows only the sentence, so every panel refusal shares `code: refused` with the
  sentence as `title`, and the alternative — `Error(code, sentence)` at 48 call sites — is one this
  plan declined in writing. **Story 2.2's plan round found the middle**: the extension keys repeats on
  `(code, subject)`, and `[CallerMemberName]` fills the subject with the calling member at every site
  for free, so the reasons separate by the method that refused. What remains accepted is coarser than
  a per-site literal — two refusal branches in one method still share a row, and a rename splits the
  history — and 2.1's census is what will hold a follow-up to completeness if that proves too coarse.
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

- [x] Every test above written RED first, with its failure message recorded, and each guard broken.
      *(Epic 3's: the scenario RED on "a serving run writes runs/<run>.json at start", the crash RED
      on the runtime's own `Unhandled exception.` with the key in clear; six defects planted in the host
      wiring and the crash handler, every one red on its own symptom once the scenario learned to
      join the death to the run's own records.)*
- [x] One credential list, embedded at build time into both deliverables, failing CLOSED, asserted
      against `shared/` by both suites **through the shipped artefact**. *(1.1.)*
- [x] The two serialisers agree as a property over every shape. *(1.2; `npm run test:parity`.)*
- [x] `measure:append` re-run with the .NET writer as one of the processes, outcome in the docstring.
      *(1.4; recorded in `module_server.md`.)*
- [x] No write can fail a round; proved by a test. *(2.2, 2.3.)*
- [x] `research/module_server.md` records the writer, the redaction, the append bargain and the run
      marker; `research/module_tests.md` names the scenario and the seam leg.
- [ ] A `coai-mcp` release. **Open** — the release is the operator's call, and it is what the
      parent's promotion waits for.
- [ ] Through `review_plan` (done — 19 findings, 15 accepted) and `review_code` per story. *(Epic 3:
      one plan round and one code round for the whole epic, on the operator's command of 2026-09-23.)*
- [x] ~~**S6 is extracted into its own `todo/` plan, and only THEN is the parent promoted**~~ *(round)*.
      **Overtaken, found while promoting this plan:** S6 needed no extraction — it had already
      SHIPPED, as [PLAN_the_rounds_log_in_line.md](PLAN_the_rounds_log_in_line.md), `IMPLEMENTED
      2026-09-17`, four days before this DoD was written; the parent said *"S6 open"* on the same
      evidence. The parent's status line is corrected in the same task (its *"Defect 2 remains"* had
      already been fixed on 2026-09-21), and it stays in `todo/` for one reason only: its own DoD
      promotes it *"when S8 has shipped and the server release is out"*.
