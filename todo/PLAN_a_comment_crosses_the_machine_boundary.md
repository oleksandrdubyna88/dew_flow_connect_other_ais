# PLAN — a comment crosses the machine boundary

> Status: **story 4.1 IMPLEMENTED 2026-09-22; stories 4.2a and 4.2b are open.** The ingest server
> takes a comment on `POST /ingest/commented`, stores it verbatim beside its pair, carries it into
> the corpus at promotion and shows it to the operator; `POST /ingest` refuses a pair that carries
> one rather than dropping it. The CLIENT half — `coai-mcp`'s local column and `--pairs-decide`
> (4.2a), and the extension's box with its "it leaves the machine" notice (4.2b) — is **not built**,
> so nothing a person types can reach the server yet. This plan stays in `todo/` until 4.2b ships,
> and is promoted then. **On 2026-09-22 the operator folded 4.2a and 4.2b into ONE pull request with
> ONE review gate** — see *4.2 in one pull request* below; the two release lines stay separate.
>
> Scope: `src_mcp/core` (the wire type
> both halves compile against), `src_bugs` (the ingest server, its schema and its one-shots),
> `src_mcp/src` (the local store, a new one-shot mode, the send), `src_vs_code` (the review page and
> its panel), and — deliberately, visibly, once — `src_mcp/tests/OnlyThreeFieldsLeaveTests.cs`
> (renamed `OnlyFourFieldsLeaveTests.cs` in 4.1).
> This is epic 4 of [PLAN_the_review_page_can_be_read.md](PLAN_the_review_page_can_be_read.md)
> (story 6 there), split as that plan split it: **4.1 the server accepts a comment**, which ships
> and is deployed BEFORE **4.2 the client sends one**.
>
> Related: [PLAN_a_corpus_of_real_defects.md](../research/PLAN_a_corpus_of_real_defects.md) (the
> corpus and the three-field promise), [PLAN_who_holds_a_key.md](../research/PLAN_who_holds_a_key.md)
> (the keys, the send, `bugs-v0.2.0`), [PLAN_the_bugs_release_line.md](../research/PLAN_the_bugs_release_line.md)
> (the frozen schema step and the migration discipline), `deploy/bugs/README.md` (the promise table
> this widens).

## How this document was written, and what in it is verified

Authored by Fable at max, the model this epic is assigned in the parent plan's table
(`PLAN_the_review_page_can_be_read.md:664`). Its session was read-only, so it produced the text and
this file was written from it.

**The five facts the four decisions rest on were re-verified independently before it was committed**,
because a plan is worth what its references are worth:

| claim | verified |
|---|---|
| `BugsJson` declares only `CamelCase` → an unknown field is silently dropped | `src_bugs/src/Program.cs:699` — `[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]`, nothing else; class at `:710` |
| a null property is OMITTED from the client's JSON | `src_mcp/src/Server/ServerJsonContext.cs:174` — `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` |
| the bearer gate is POST-only on `/ingest` | `src_bugs/src/IngestGate.cs:101-102` — `HttpMethods.IsPost(request.Method)` |
| quarantine is bounded at 20 000 rows | `src_bugs/src/Corpus.cs:60` — `public const int MostWaiting = 20_000;` |
| one request is bounded at 200 pairs and 1 MiB | `src_bugs/src/Ingest.cs:31,34` |

The remaining line references are the author's and are exercised by the build order below; where one
is wrong the compiler or a test says so, and the record is corrected rather than quietly followed.

## What the plan round changed, 2026-09-21

One round, 17 findings, `good_enough`. **14 accepted, 3 rejected with reasons**, and one of them was
better than the plan it reviewed:

**The probe was advisory, and a capability probe cannot make an old server refuse.** codex put it
exactly: the probe reaches a new node and the POST reaches an old one during a rollout or a failover;
the old node accepts the pair, drops the comment, and the client learns only after the write — when
the comment is already gone and a retry is answered `duplicate`. **So the mechanism changed**: a
comment travels on a POST route an old binary does not have, and its refusal is a 404 rather than a
promise. The `GET /ingest` probe is gone with it, which also removes gemini's question about a proxy
405 on a POST-only path and the whole "the gate must admit a new method" step.

The other twelve, each a real hole:

- **Encoding at every output sink was unspecified** while decision 2 deliberately allows `</div><script>`.
  Named now, per sink, with the rendered-page test carrying the payload.
- **"Nothing is in flight" was false.** A local transaction followed by a network request has a gap,
  and a crash inside it would leave a pair read-only and unsent. `sentUtc` is written on
  ACKNOWLEDGEMENT, never before the POST — said, and tested at the boundary.
- **A timeout, a 5xx and an expired token were collapsed into "an old server"**, which would have
  reported exit 69 and a wrong diagnosis. Each gets its own answer.
- **A refused comment had no recovery path**: the draft stays, the row is never marked sent, and the
  message names the offending code point.
- **The second contributor's comment was discarded silently.** The `duplicate` answer now says so.
- **The old-binary test artefact was unpinned** — now a tag, a checksum and a disposable data dir.
- **`--pairs-decide` returning 64 for a bad file** would be read by the extension as an old binary;
  the exit-code contract is now tested rather than asserted in prose.
- **Upgrade and rollback** were unstated; `NOT NULL DEFAULT ''` is the reason both are safe, and that
  is now the reason given.
- **LF was allowed while the listing it breaks was in another section.** Decision 2 says it now.

Rejected: that the probe belongs to `--pairs-decide` (it is a LOCAL one-shot that touches no network,
and falling back on a network failure would DROP the comment — the one outcome this exists to
prevent); that `PairId.Of` is ambiguous (the finding answers itself twice); and that `--pairs-decide`
is missing from `.agents/PROJECT.md` (it is in the plan, in those words, and in the Definition of Done).

## The goal, and what is already decided

A person reviewing pairs on the **Review bugs** page can type a free-text comment on a pair, and the
comment is sent to the ingest server WITH the pair, stored beside it in quarantine, carried into the
corpus when the pair is promoted, and shown to whoever reads the queue.

Two things the operator decided on 2026-09-18 and this plan does not re-open
(`PLAN_the_review_page_can_be_read.md:676-687`):

1. **A comment is PUBLIC.** *"A comment a person wrote is public; it goes everywhere, including to the
   server, for storage and later processing."* So there is no PII scanner, no local-only fallback,
   and no normaliser touches it. The story that would have written a scanner does not exist.
2. **The page must SAY, beside the box, that a comment leaves the machine.** Not a confirmation, not
   a gate — the policy is decided — but a person choosing what to put in a public field can only
   choose it knowingly if the page tells them, and every other surface here says where things go.

## Why it is not already built: four decisions and one constraint

### The constraint first: the comment must sit OUTSIDE the pair's identity

`UploadedPair` carries exactly three properties (`src_mcp/core/Collecting/UploadWire.cs:24-25`), its
docblock says *no id — the server derives one with `PairId.Of` from exactly these three fields*, and
`PairId.Of` hashes exactly those three, length-prefixed. The server derives the id at
`src_bugs/src/Ingest.cs:74-75` and the client matches acknowledgements BY that id. If a comment
entered the derivation, every entry already in quarantine and in the corpus would change identity,
the client's matching would break against every old row, and two people who found the same defect
would stop deduplicating.

**Decided: the comment is NOT part of the id.** `Whole()` widens to four values; `PairId.Of`
deconstructs three and ignores the fourth, with a comment saying so; a test pins that two pairs
differing only in their comment have one id.

The consequence is accepted, and — after the round — **it is also SAID**: the same skeleton pair from
two contributors is ONE row, the first comment to arrive is the one stored, and the second is answered
`duplicate`. `duplicate` is a success, so the second person would otherwise be told their comment
landed when it did not. `Why` on that result now carries *"this pair was already held; your comment
was not stored"* when the refused-duplicate carried one, and `--waiting` shows whose comment the row
holds. A comments table holding one per (entry, key) is NOT built; if it is ever wanted it is a story
of its own, and this sentence is what it would replace.

### Decision 1 — size: **1 000 characters, and a longer one is REFUSED**

**The unit is UTF-16 code units** — `string.Length` in C#, `.length` in JavaScript — because it is
the one measure both halves already share, so no encoding arithmetic crosses the boundary and the
page's `maxlength` counts the same thing the server does.

**The number.** One request is bounded by `Ingest.MostBytes` = 1 MiB (`src_bugs/src/Ingest.cs:34`)
and `MostPerBatch` = 200 (`:31`). A skeleton is 381 B at the median, so a pair is 762 B. A
1 000-unit comment is at most 3 000 B of UTF-8. So a full batch of 200 median pairs each carrying a
worst-case comment is ≈ 752 KB, inside the cap with room for skeletons up to ~1.1 KB each. At 2 000
units the same batch is ~1.15 MB and does not fit; that is why it is not 2 000.

**What happens to one that exceeds it.** The server refuses the PAIR — `Took.Refused`, `Why` naming
the length — and never truncates, because a truncated comment is text the person did not write.

**And a refusal is something a person can act on**, which the first draft left unsaid:

| where | what they see | what stays true |
|---|---|---|
| typing | `maxlength="1000"` and a live counter that turns warning-coloured at 900 | the box never accepts the 1 001st character, so the common case never reaches a refusal at all |
| deciding | `--pairs-decide` exits **65** naming the finding id and the reason, and the panel already renders that as *"The decision could not be saved: …"* | nothing is written — the draft is still in the box, still editable |
| sending | `Took.Refused` for that PAIR only; the rest of the batch is unaffected | the row is **never** marked sent, `sentUtc` stays unset, the box stays editable, and the refusal text names the limit or the code point |

The refusal path is tested on the server whatever the client does, because the server does not get to
assume its client — and a foreign client is exactly what an ingest endpoint must survive.

**Two things this does not fix, named rather than hidden.** A batch can still exceed the body cap and
get a 413, which records `Trouble`, marks nothing and stops — today's behaviour for oversized
skeletons, a tail of the send rather than of this plan. And 1 000 is a DESIGN number: no real comment
exists yet to measure. After the first fifty real comments the median is read off `quarantine` and
this number is revisited in the record.

### Decision 2 — character set: **any text, except what breaks a terminal, a log or a reader**

Allowed: every Unicode scalar value, including LF (U+000A) and TAB (U+0009).

Refused, and the PAIR with it, naming the first offender as `U+XXXX`:

- **C0 and C1 controls other than LF and TAB.** NUL truncates `sqlite3`'s own output of a TEXT
  column; the rest corrupt `--waiting`'s terminal listing and the server's logs. CR is refused rather
  than normalised on the server because the client normalises CRLF and CR to LF before writing, so a
  bare CR arriving is a client defect and is said to be one.
- **The bidirectional controls** U+061C, U+200E, U+200F, U+202A–U+202E, U+2066–U+2069. A corpus shown
  to people as precedent is worth poisoning; a comment that renders its own text backwards or hides
  part of it is the "Trojan Source" shape, and a reviewer reading `--waiting` cannot see it.
- **Unpaired surrogates**, which are not scalar values and cannot be encoded as UTF-8.

Not refused: emoji, CJK, U+2028/U+2029, any punctuation, leading or trailing whitespace. The CLIENT
trims and treats a comment that is empty after trimming as *no comment*; the server stores what it is
given.

**LF is allowed AND `--waiting` stays one row per line** — a reviewer caught the inconsistency: the
rule refuses C0 controls *because* they corrupt that listing and then permits the one that would.
`--waiting` prints the comment's FIRST line, truncated to 72 characters, with an ellipsis when there
is more; `--find` prints it whole, because that command is already multi-line. Refusing LF instead
was considered and rejected: a person writing two sentences about a defect will press Enter, and a
refusal there would be the product arguing with the one thing it asked for.

**Nothing is scrubbed.** The alphabet whitelist runs over the two skeletons and does NOT run over the
comment. A test, `TheCommentIsNotScannedByTheAlphabet`, sends a word in a comment and asserts it is
accepted while the same word in a skeleton is refused — that is the operator's decision written as
something that fails if somebody "helpfully" extends the whitelist to the new field.

**Where it is ENCODED, sink by sink** — because decision 2 deliberately allows `</div><script>` and
`&` and quotes, and a field that may contain anything must be escaped by whoever renders it, not
sanitised by whoever stores it:

| sink | encoding | what it would be without it |
|---|---|---|
| the review page | `escapeHtml`, the same function every other pair field goes through, and the page test carries `</textarea><img src=x onerror=…>` as a draft | script in the extension's own webview context |
| `--waiting`, `--find` | the first line only, truncated, controls already refused at ingest | a forged row in a listing a person reads to decide |
| the server's log | the comment is NEVER logged — not its text, not a prefix. `Why` names a length or a code point, never content | a public field written into an operator's log by accident |
| an admin JSON answer | System.Text.Json's own escaping, which is not optional | the same as the page, one hop further |

**One implementation.** `CommentRule` (new, `src_mcp/core/Collecting/CommentRule.cs`) holds
`MostChars = 1000` and `Refuse(string)`, referenced by the server's ingest and by `--pairs-decide`.
The extension holds the NUMBER only, and a structural test asserts the two agree. The charset rule is
never re-implemented in TypeScript: a comment the rule refuses is refused by `--pairs-decide` with 65
and a sentence, which the panel already shows.

### Decision 3 — version negotiation: **a route an old binary does not have**

**Verified, not assumed** (see the table at the top). A four-field pair posted to `/ingest` on the
DEPLOYED server is accepted, its comment dropped, and its answer `accepted` — the silent truncation
the story named, with both halves' suites green either way. Also measured: the client omits a null
property, so **a pair without a comment serialises byte-identically to today's wire** — asserted
against a fixture captured BEFORE the type changes, which is what keeps every deployment as it is
until somebody types.

**The first design was a capability probe, and the plan round was right that it cannot work.** A
probe is advisory: it reaches one node and the POST reaches another during a rollout or a failover,
the old node accepts the pair and drops the comment, and the client learns only after the write —
when the comment is gone and a retry is answered `duplicate`. A promise that the client checks is not
the same as a door the old server does not have.

**So a comment travels on its own route.** `POST /ingest/commented` accepts `UploadRequest` exactly
as `/ingest` does, behind the same bearer gate, and is the ONLY route that reads `Comment`. The
client posts a batch there when any pair in it carries a comment, and to `/ingest` otherwise.

| server | what happens |
|---|---|
| `bugs-v0.3.0` or newer | the route exists; the comment is stored with the pair |
| anything older | **404** — no route, nothing written, nothing lost. The run records `Trouble: "this server is older than comments (404 for /ingest/commented); 3 of the pairs waiting carry one. Deploy bugs-v0.3.0 or clear them; nothing was sent."` and exits 69 |

The refusal is now structural. There is no window in which an old server can accept a commented pair,
because it has nothing to accept it with, and no rollout ordering can produce one. `/ingest` itself
is untouched — same route, same behaviour, same bytes for a comment-free batch.

**`Contract` on the answer stays**, as a second belt rather than the mechanism: every answer carries
it, and a batch sent to `/ingest/commented` whose answer comes back without `Contract >= 2` marks
nothing. That catches a proxy answering 200 for a route it does not really have.

**Each failure gets its own answer** — the round was right that collapsing them would report a wrong
diagnosis and block an actionable retry:

| what came back | what it means | what the run does |
|---|---|---|
| 404 on `/ingest/commented` | the server predates comments | stop, exit 69, the sentence above |
| 401 / 403 | the key is wrong or revoked | the existing auth path, unchanged — never "the server is old" |
| 429 | the existing rate-limit path, unchanged | back off as today |
| 5xx | the server is unwell | `Trouble: "the server answered 503"`, nothing marked, retry later — never "old" |
| a timeout or a transport failure | nothing was learned | `Trouble` naming it, nothing marked — never "old" |
| 200 with `Contract < 2` or absent | a proxy, or a half-rolled deployment | `Trouble`, nothing marked |

**Nothing is in flight, said correctly this time.** The plan claimed that and it was false: a local
transaction followed by a network request has a gap. The correction is an ordering, not a state
machine — **`sentUtc` is written when the server ACKNOWLEDGES, in the same `Record` transaction that
marks the pair sent, never before the POST.** So a crash anywhere before the acknowledgement leaves
the pair unsent, editable and queued; a crash after it leaves a pair marked sent that really was.
There is no state in which the box is read-only and the comment never left. `MarkedAsSent` is tested
at both boundaries: kill between the write and the POST, and between the POST and the mark.

*`coai-mcp` ↔ the extension.* An old binary handed a `comment` in `--pairs-keep`'s file would
deserialise past it and answer `{"decided": N}` — plausible and wrong, which is the case PROJECT.md
made a MODE for. So comments travel through a **new one-shot, `--pairs-decide --in <decisions.json>`**;
an old binary exits 64 and the extension refuses a decision carrying comments, falling back to
`--pairs-keep` when none is. **And 64 must mean ONLY "never heard of this mode":** a missing file,
malformed JSON, a bad keep, a refused comment and an unopenable database are 65/65/65/65/74, and a
test asserts each of those five, because a 64 from a NEW binary would be read by the extension as an
old one and a failed decision would look like a compatibility fallback.

**Testable rather than hoped** — the story demanded a LIVE test against an old and a new server:

| test | runs | proves |
|---|---|---|
| `TheBuiltBinariesTests.OldServer` (new file) | the PREVIOUS released `coai-bugs`, **pinned**: tag `bugs-v0.2.0`, its archive verified by SHA-256 **per architecture** before it runs, started on a disposable data directory that is removed afterwards. CI downloads it through `.github/actions/fetch-old-bugs`; the variable being unset SKIPS locally and FAILS on CI | `POST /ingest/commented` is 404; a four-field POST to `/ingest` is accepted with the comment dropped (the defect this route exists to prevent, demonstrated rather than assumed) |
| `BothHalvesTests` (+3) | the real `UploadRun` against the real server in-process | a commented batch goes to `/ingest/commented` and arrives whole; a comment-free batch goes to `/ingest` and its bytes equal the fixture; a 404 there stops the run and marks nothing |
| `UploadRunTests` (new, stubbed handler) | `UploadRun` alone | each row of the failure table above, including that a 503 and a timeout do NOT say "old" |
| `ThePairModesTests` (+5) | the real binary | the five non-64 exits, and 64 only for an unknown mode |

**What is NOT done, and why.** No `GET` probe, for the reason above. No contract on the
unauthenticated `/health`. No route that counts a submission against a key just to be asked a
question.

### Decision 4 — retention: **a comment has its pair's lifetime and no sweep of its own**

| surface | bound | projected size | who retires it | interrupted |
|---|---|---|---|---|
| `quarantine.comment` (server) | the rows: `MostWaiting` = 20 000 (`Corpus.cs:60`) | worst 60 MB; realistic ~12 MB — against ~15 MB of skeletons | a person: `--promote` copies it to `corpus`; `Reject` deletes it with the row. No timer, as for the pair | none: written inside the batch's one transaction |
| `corpus.comment` (server) | one per promoted pair | ≤ 3 KB each; ≤ **1.4 MB** at the 462 usable candidates measured; **kept forever with the pairs** — a retention window here would delete the product | nobody, deliberately | none |
| `collect_pairs.comment` (this machine) | one per pair | ≤ 1.4 MB, beside 344 KB of skeletons | `ForgetPair` when the finding stops being collected; otherwise kept with the pair | none: written in the decision's transaction, sent in the batch's |

A second comment for an id the server already holds is dropped with the `duplicate` answer, which
says so. Backups are the one-file `.backup` the deploy notes already describe.

**Upgrade and rollback, which the round found unstated.** The migration is
`ALTER TABLE … ADD COLUMN comment TEXT NOT NULL DEFAULT ''` on both tables — appended as a new step,
never merged into step 1, which stays byte-identical and is pinned by `TheSchemaIsFrozenTests`.

- *Forward, with data:* SQLite rewrites no rows for an ADD COLUMN with a constant default; every
  existing quarantine and corpus row reads back `''`, which is exactly *no comment*. `NOT NULL
  DEFAULT ''` rather than nullable is chosen for this: it means no reader anywhere needs a null check,
  and an old row and a new row without a comment are indistinguishable.
- *Backward:* rolling the binary back to `bugs-v0.2.0` leaves the column in place and unread —
  SQLite does not mind a column nobody selects, and `bugs-v0.2.0`'s statements name their columns.
  Comments written in between survive the rollback silently and reappear if the new binary returns.
  The one thing a rollback loses is the `/ingest/commented` route, which is the correct behaviour:
  the client gets a 404 and refuses to send, rather than sending into a version that would drop it.
- *Tested:* `TheMigrationTests` runs the steps against a store seeded with rows from before the
  column and asserts they read back with an empty comment, then re-runs the migration to prove it is
  idempotent.

## 4.2 in one pull request — what changed on 2026-09-22, and what the code said

The operator folded the two client stories into **one pull request with one review gate**. The two
release LINES stay: the merge proposes `mcp-v0.31.0` and `extension-v0.51.0` as separate release
PRs. Shipping both from one merge is safe for the reason the old order was safe — an extension that
meets an older `coai-mcp` gets 64 from `--pairs-decide`, and the page says the server is too old for
comments rather than dropping one.

The 4.2 sections below were read against the code on 2026-09-22 before this was written. Each item
changes what they say, and the file-by-file lists are corrected to match:

1. **There is no probe.** 4.2a said *"the batch loop probes when any sendable pair has a comment"* —
   a sentence from before the plan round replaced the probe with a route (decision 3). A batch in
   which any pair carries a comment is POSTed to `/ingest/commented`; every other batch goes to
   `/ingest`, byte-identical to today. `Record` requires `Contract >= Contract.Comments` on the
   answer to a commented batch and marks nothing without it.
2. **`sent_utc` is already written on the acknowledgement.** `WhatWasSent` (step 8) added it, and
   `RoundsDb.RecordSendOutcome` writes it in the batch's one transaction. 4.2 does not introduce it:
   it exposes it to the page (`ReviewPair.SentUtc`) and adds the test the Definition of Done asks
   for — a run killed before the POST and one killed after it leave no pair marked sent that the
   server never acknowledged.
3. **A comment the server did NOT store is recorded, not lost inside a success.** The server already
   answers `accepted`/`duplicate` with a `Why` of *"this pair already carries a comment, and the first
   one stays, so yours was not stored"* or *"… has already been promoted into the corpus …"*
   (`Ingest.Lost`, 4.1). With nowhere to put it, the client marks the pair sent and the page then
   shows *"Sent on …"* above words that never crossed — the silent loss this plan exists to prevent,
   moved one hop. So step 13 adds a second column, **`comment_lost TEXT NOT NULL DEFAULT ''`**,
   written in the same acknowledgement transaction from a non-refusal `Why`; the page's sent sentence
   becomes *"Sent on <date>, but your comment was not stored: <the server's reason>"*, and
   `--upload-pairs` prints the same line. **A deviation from 4.2a as reviewed**, which had one column.
4. **A sent pair's comment cannot change in the store either.** The page makes the box read-only;
   `--pairs-decide` must not be the way around it, or the local row diverges from what crossed and
   the page renders the divergence as sent. A decision whose comment DIFFERS from the stored one on a
   pair with `sent_utc` set is refused, 65, naming the finding. An unchanged one is accepted, so
   changing a sent pair's keep still works.
5. **`Program.cs` is 1 878 lines against the doctrine's 800.** `--pairs-decide` does not make it
   longer: `Program` becomes `partial` and the mode lives in `Program.PairsDecide.cs`, beside the
   helpers it shares (`Flags`, `Note`, `Unreadable`). Splitting the existing file is not this epic's
   work and is not done here.
6. **`StoredPair` gains `Comment` as its LAST positional parameter, defaulted to empty**, so every
   existing construction — `OnlyFourFieldsLeaveTests` builds one by name — compiles and means what it
   did; `Sendable` reads it as ordinal 9, after the nine it already reads.

## What changes, file by file

### 4.1 — the server accepts a comment (one PR, released as `bugs-v0.3.0`, DEPLOYED before 4.2 merges)

**Core (`src_mcp/core/Collecting/`)**
- `UploadWire.cs` → `UploadedPair(Language, SkeletonBefore, SkeletonAfter, Comment = null)`; `Whole()`
  answers four; `PairId.Of` deconstructs `(language, before, after, _)` and says why. `UploadAnswer`
  gains `int Contract = 0`; `public static class Contract { public const int Comments = 2; }` beside
  `Took`. The docblock's *"three fields and no more"* becomes four, dated, quoting the operator.
- `CommentRule.cs` (new): `MostChars`, `Refuse(string)`. Small, pure, one method per rule.

**Server (`src_bugs/src/`)**
- `CorpusSchema.cs` — step 5, `TheContributorsWords`:
  `ALTER TABLE quarantine ADD COLUMN comment TEXT NOT NULL DEFAULT ''; ALTER TABLE corpus ADD COLUMN comment TEXT NOT NULL DEFAULT '';`
  Appended, never merged; step 1 stays byte-identical.
- `Corpus.cs` — `Keep`/`KeepInside` take the comment and write it in the same INSERT; `Moving`'s
  `INSERT INTO corpus … SELECT …` carries it; `Waiting`/`Find`/`Reading` answer a fifth value.
- `Ingest.cs` — `var (name, before, after, comment) = pair.Whole();` — the id still from three;
  `CommentRule.Refuse` runs AFTER the alphabet, so a pair that both leaks and over-runs reports the
  leak, which is the refusal a person must see first.
- `Program.cs` — `app.MapPost("/ingest/commented", …)`, the ONLY route that reads `Comment`, sharing
  the handler with `/ingest` and differing in one flag. `IngestGate.IsIngest` admits the new PATH
  (still POST-only, so no method question and no proxy surprise). Every answer carries `Contract`.
- `Admin.cs` — `--waiting` prints the first line of a comment under its pair; `--promote` prints it
  beside what it moved.
- `deploy/bugs/README.md` promise table gains: *A comment is the contributor's own words, public by
  their decision; stored verbatim, never scanned, never scrubbed* — kept by CODE.

**The deliberate test change (`src_mcp/tests/`, in THIS PR, because the core type forces it)**
- `OnlyThreeFieldsLeaveTests.cs` → **`OnlyFourFieldsLeaveTests.cs`**, class renamed with it. A test
  named "three" asserting four is exactly the promise-drift the suite exists to refuse.
- The name assertion becomes `["Language", "SkeletonBefore", "SkeletonAfter", "Comment"]`. The
  docblock keeps *the only test whose failure means a privacy regression* and gains a dated
  paragraph: widened once, on the operator's decision that a comment is public.
- `TheStoredRowStillCarriesWhatMustNotLeave` unchanged — the mapping still narrows.
- **New:** `APairWithoutACommentIsByteIdenticalToTheWireBeforeComments` — compares against
  `src_mcp/tests/fixtures/upload-request-before-comments.json`, **captured from the CURRENT build in
  step 0, before the type changes**. This is the assertion that keeps every existing deployment
  behaving as it does today until somebody types.
- Live references updated in `src_mcp/src`, `src_mcp/tests` and the `research/module_*.md` docs;
  mentions inside `research/PLAN_*.md` are RECORDS and stay.

**Why widening is safe now.** The test guards against fields a person did not choose to publish — the
id, the symbol, the severity, the category, the title, all derived from their repository. A comment is
the one field a person types, into a box that says where it goes. The five private fields stay
asserted absent; the wire without a comment is byte-identical to today; and the comment never enters
the id.

### 4.2a — `coai-mcp` (released as `mcp-v0.31.0`; one PR with 4.2b since 2026-09-22)

- `Schema.cs` — step 13, `WhatAPersonSaid`:
  `ALTER TABLE collect_pairs ADD COLUMN comment TEXT NOT NULL DEFAULT ''; ALTER TABLE collect_pairs ADD COLUMN comment_lost TEXT NOT NULL DEFAULT '';`
  — the second column is item 3 of *4.2 in one pull request*.
- `CollectedPair.cs` — `StoredPair` gains `Comment` LAST and defaulted; `RoundsDb.Sendable` selects
  it LAST, because that reader is ordinal and its own docblock warns about it. `SendOutcome` gains the
  lost-comment sentence, so it is written in the acknowledgement's one transaction.
- `ReviewPair.cs` gains `Comment`, `SentUtc` and `CommentLost`. **`SentUtc` exists so the page can
  make a sent pair's box read-only and SAY the send has happened** — a comment edited after a send
  never crosses, and a box that let it be edited in silence would be the failure this product
  refuses. `ThePagesRow` selects the three by name.
- `PairsWire.cs` — `DecideAsk(FindingId, Keep, Comment = "")` and its request; `RoundsDb.RecordDecide`
  writes keep AND comment in one transaction and refuses a changed comment on a sent pair (item 4);
  `RecordKeep` stays for `--pairs-keep`.
- `Program.PairsDecide.cs` (new; `Program` becomes `partial`, item 5) —
  `"--pairs-decide" => Startup.PairsDecide`: reads the file (65 on a fault), validates every keep and
  every comment through `CommentRule.Refuse` (65, naming the finding id and the reason), normalises
  CRLF/CR to LF, trims, writes, prints `{"decided": N}`. 74 for a database that will not open. Never 64.
- `UploadRun.cs` — `Wire` maps an empty comment to **null**, which is what makes the byte-identity
  assertion true; a batch with any comment goes to `/ingest/commented` and every other to `/ingest`
  (item 1); `Record` requires `answer.Contract >= Contract.Comments` for a commented batch, writes a
  non-refusal `Why` as `comment_lost`, and says it; a 404 on the commented route is the sentence in
  decision 3's table, never a generic status.
- `.agents/PROJECT.md`: `--pairs-decide` added to the list, no prose. `StageRulesTests
  .TheRotatedTail_CurrentlyFitsAtMostOneRule` is run afterwards — it is the canary for that file.

### 4.2b — the extension (released as `extension-v0.51.0`; one PR with 4.2a since 2026-09-22)

- `src_vs_code/src/reviewComment.ts` (new; held to complexity 4 and 50 lines per function by the
  rules that landed on 2026-09-21): renders inside the DETAIL row a `<label for>`, a
  `<textarea data-comment maxlength="1000" rows="2">`, a counter, and beside it the sentence
  *"Sent with this pair to the corpus server, in public, exactly as typed — it is not anonymised."*
  When `pair.sentUtc` is set the box is `readonly` and the sentence becomes *"Sent on &lt;date&gt;. A
  change here will not follow it."* — or, when `pair.commentLost` is set, *"Sent on &lt;date&gt;, but
  your comment was not stored: &lt;the server's reason&gt;"* (item 3).
- `bugzReviewPage.ts` — `ReviewView` gains `comments`; `row()` calls `commentBlock`; the script gains
  ONE delegated `change` listener posting `{type: 'comment', id, text}`. The `click` handler needs no
  new branch — the textarea is in the detail row, outside `[data-toggle]` — and the page test presses
  it to prove the row does not toggle and nothing is decided.
- `bugzReviewPanel.ts` — `drafts` held for `expanded`'s reason, cleared on dispose; the message
  validated as `fetchReal` validates its id; handled by updating the draft AND queueing one write
  through the same `inFlight` chain, so a comment never changes a decision and a decision never
  erases a comment.
- `roundsDbRead.ts` — `pairOf` reads the three new fields through `textOf`, so a server older than
  the field yields empty and the page renders an empty editable box; `writeDecide` maps **64 →
  `tooOld`**; the provider falls back to `writeKeep` only when every comment in the batch is empty.
- `reviewPair.ts` gains the three fields; `bugzLiveContract.test.ts` derives both sides' names.

## Build order

0. **Before any type changes** (its own small commit): capture
   `src_mcp/tests/fixtures/upload-request-before-comments.json` from the CURRENT build — the bytes a
   comment-less pair sends today. Record, with the date, which `bugs-v*` is serving
   `bugs.remsoft.dev`: the deploy notes say v0.1.0 was installed on 2026-09-17 while
   `PLAN_who_holds_a_key.md` calls v0.2.0 released and deployed the same day, and an authenticated
   `POST /ingest/commented` returning 404 settles both questions at once — it is the answer the
   client will act on, asked before any code depends on it. Pin the `bugs-v0.2.0` archive's SHA-256
   for the old-server test in the same commit.
1. **4.1 — core + server + the deliberate test flip** (PR 1). `coai-mcp` still sends three fields.
   Tag `bugs-v0.3.0`. Deploy. Verify: an authenticated `POST /ingest/commented` is accepted where it
   answered 404 in step 0, and its answer carries `contract: 2`; the migration stamped
   `user_version = 5`; existing quarantine rows read back with an empty comment. Record the date here.
2. **4.2a + 4.2b — `coai-mcp` and the extension, ONE PR** (operator, 2026-09-22; it was PR 2 and
   PR 3). Safe even before step 1 is deployed, because an older server answers the commented route
   404 and the run stops marking nothing. The merge proposes two releases, `mcp-v0.31.0` and
   `extension-v0.51.0`; the extension needs the new `coai-mcp` on the machine, and an older binary is
   told apart by 64 and the page says so — so the two may ship in either order without losing a word.
3. *(folded into step 2.)*
4. The parent plan's epic-4 row is updated per story; when 4.2b ships, this plan is promoted with its
   deviations and the parent plan with it.

## Test plan — each test and what it would catch

**Server** (`./src_bugs/tests/bin/Release/net10.0/CoaiBugs.Tests`, never `dotnet test`)
- **`TheCommentTests` (new file, +18)** — a class of its own rather than more rows in
  `TheIngestTests`, because what it asserts is one subject: a comment accepted and carried into
  `--waiting`'s tuple (a column written and never read); an empty comment accepted as none (the old
  client's payload); 1 004 characters refused naming the length (a cap that is not enforced); NUL,
  CR, ESC, U+202E and a lone surrogate each refused naming the code point (a rule with a hole); a
  leaky word accepted in a comment while refused in a skeleton (the alphabet reaching the new
  field); the same pair twice with two comments → `duplicate`, first kept, second TOLD (the id
  absorbing the comment; a silent loss); promotion carrying the comment into `corpus` (the quietest
  failure available); a refusal that never quotes the comment back.
  `PairId.Of` equal across comments is asserted on the client side, where the type lives.
- `TheRouteTests` (+4): `POST /ingest/commented` with a key → 200 and `contract: 2` through the real
  AOT binding; without a key → 401 (a route reached around the gate); **`/ingest` REFUSES a pair that
  carries a comment**, per item, naming the path that keeps it and writing nothing, so the two routes
  really do differ (the whole mechanism in one assertion); every answer from both routes carries
  `contract`.
  *(Revised in code round 1: the plan first said `/ingest` would ignore a comment it was sent. Three
  reviewers pointed out that a new server dropping the field in silence is the same failure an old one
  commits, in the place nobody is watching for it — so it refuses instead, and a batch loses
  nothing because nothing was written.)*
- `TheMigrationTests` (+1): step 1 unchanged, both tables altered, and a row written BEFORE step 5
  reading back as having no comment — the half of the promise that makes this safe on a live host.
- `TheAdminUploadTests` (+1): an administrator's comment stored on the `AcceptAdmin` path, which no
  other test here walks, and still counted against no key row.
- **The old release, RUN** (`TheBuiltBinariesTests.OldServer`, gated on `COAI_BUGS_OLD_ARCHIVE`):
  `POST /ingest/commented` is 404 and the SAME document on `/ingest` is accepted with nowhere to put
  the field. Without it, every other test here is two halves of one checkout agreeing with each
  other. The archive is pinned by tag and by SHA-256 per architecture and verified before anything
  in it is executed; CI downloads it through a composite action used by all three workflows that run
  this suite, and the variable being unset SKIPS locally and FAILS on CI.
- `TheBuiltBinariesTests` (+2): the published binary takes a comment on its own route, states
  `contract`, and shows the comment in `--waiting`; and its plain route still stores none.

**Client** (`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`)
- `OnlyFourFieldsLeaveTests`: the four names; the five that must not leave; the mapping sends the
  comment; **the comment-less wire is byte-identical to the pre-change fixture** (a default that
  serialises as `null` or `""` would change what every deployed server receives).
- `ThePairsThemselvesTests` (+3): `RecordDecide` writes keep and comment together; `Sendable` reads
  the comment back BY VALUE (the ordinal reader answering the wrong column).
- `ThePairModesTests` (+3): `--pairs-decide` 65 on a long comment, 65 naming `U+0007`, never 64.
- `UploadRunTests` (new, stubbed handler): the route chosen per batch; every row of decision 3's
  failure table, including that a 503 and a timeout do NOT say "old"; a commented batch answered
  without `contract >= 2` marks nothing; a lost-comment `Why` is written as `comment_lost`; and a run
  killed before the POST and one killed after it leave no pair sent that was never acknowledged.
- `BothHalvesTests` (+3; it lives in the INGEST suite, `src_bugs/tests`, because it runs the real
  `UploadRun` against the real server in-process): the comment crosses end to end on the new route; a
  comment-free batch goes to `/ingest` with bytes equal to the fixture; a 404 on the commented route
  stops the run marking nothing.
- `ThePairsThemselvesTests` (+2 on top of the three above): a changed comment on a sent pair is
  refused and an unchanged one is not; `comment_lost` reads back through `ThePagesRow` by name.

**Extension** (`cd src_vs_code && npm test`)
- `bugzReviewPage.test.ts`, by RUNNING the page: typing and blurring posts the message; a redraw with
  a draft renders it back; a row with `sentUtc` renders `readonly` and the sent sentence; the notice
  sits beside every box, **read off the rendered element** (a rendered value, inside the 2026-09-14
  ruling's carve-out); `</textarea><img onerror>` renders as text; pressing inside the box neither
  toggles the row nor decides; the decide message still carries exactly `ids`, `keep`, `type`.
- `bugzReviewWiring.test.ts` (+3): the seams a value test cannot reach.
- `roundsDbRead.test.ts` (+3): the two new fields default on an older server; 64 → `tooOld`, 65 → not.
- The structural pin that the page's `maxlength` and `CommentRule.MostChars` agree.

## What this plan deliberately does not build

- A PII scanner or a local-only comment (operator decision, 2026-09-18).
- A contract number on `/health`, or a probe that counts a submission.
- A wider `--pairs-keep`.
- A change to `PairId.Of`, to the alphabet, or to `Took`.
- Editing a comment after its pair was sent — impossible by design, and the page says so.
- Any automatic retention.

## Definition of Done

- [ ] Step 0's fixture is committed BEFORE the type changes; the deployed server's 404 for
      `POST /ingest/commented` and its version are recorded here with a date; and the `bugs-v0.2.0`
      archive's SHA-256 is pinned in the same commit.
- [ ] 4.1 shipped as `bugs-v0.3.0`, deployed, and an authenticated `POST /ingest/commented` is
      accepted where it was 404 before — recorded with a date BEFORE PR 3 merges.
- [ ] `OnlyFourFieldsLeaveTests` exists, `OnlyThreeFieldsLeaveTests` does not, the four names are
      asserted, the five private fields are still asserted absent, and the comment-less wire is
      byte-identical to the captured fixture.
- [ ] `TheOldServerHasNoCommentedRouteAndDropsACommentSentToTheOldOne` ran against the real
      `bugs-v0.2.0` binary — pinned by tag and SHA-256, on a disposable data directory — and proved
      both halves: 404 on the new route, and a silent drop on the old one.
- [ ] Every failure mode is told apart: 404, 401/403, 429, 5xx, timeout and a 200 under the wrong
      contract each have their own answer, and only the first says the server is old.
- [ ] `sentUtc` is written in the acknowledgement transaction and NOWHERE earlier, with a test that
      kills the run at both boundaries and finds no pair that is read-only and unsent.
- [ ] A refused comment leaves the draft in the box, the row unmarked and the reason on screen.
- [ ] A `duplicate` whose comment was not stored SAYS so.
- [ ] The migration is proven against a store seeded with pre-column rows, and proven idempotent.
- [ ] `--pairs-decide` returns 65/65/65/65/74 for its five fault kinds and 64 for nothing but an
      unknown mode, each asserted by the real binary.
- [ ] The comment is escaped at every sink and logged at none.
- [ ] `BothHalvesTests` proves a comment crosses on the new route, that a comment-free batch still
      goes to `/ingest` with bytes equal to the fixture, and that a 404 stops the run marking nothing.
- [ ] `--pairs-decide` is in `.agents/PROJECT.md`'s list, adds no prose, and the rules-budget canary
      is green.
- [ ] The page RUNS in its tests: the box posts, survives a redraw, goes read-only when sent, escapes,
      and does not toggle its row; the notice is read off the rendered element.
- [ ] Every C# suite run as its executable, both halves; `npm test` green; every new TypeScript
      function within complexity 4 and 50 lines with no new suppression.
- [ ] `deploy/bugs/README.md`'s promise table and the `research/module_*.md` docs updated; the parent
      plan's epic-4 row updated per story; `todo/README.md` row committed with this plan.
- [ ] After fifty real comments exist, the median length is read off `quarantine` and decision 1 is
      revisited in the promoted record.
