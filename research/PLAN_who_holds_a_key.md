# PLAN — who holds a key, and what they have sent

> Status: **IMPLEMENTED, 2026-09-17.** All five stories shipped: story 1 as `bugs-v0.2.0`, story 2
> (admin keys and the admin API) in PR #353, story 3 (the Users tab) in #363, story 4 (the delivery
> and the deploy's own proof) in #366, and story 5 (the extension can send) in the change that
> promoted this document. Story 5 was added on 2026-09-17 and is not in the original four.
>
> Scope: `src_bugs` (schema, rate limit, admin API), `src_mcp` (the send's durable run row),
> `src_vs_code` (the Users tab and the Send action), the deployment, and the privacy promise in
> `research/module_server.md` and `deploy/bugs/README.md`, which this change **deliberately
> rewrites**. Each story's deviations from the text below are recorded at the end of its own
> section; the four that changed the SHAPE of the work are listed here.
>
> **What shipped differently from this text.**
>
> 1. **The admin keys travel base64 with a marker line.** The plan said newline-separated; a systemd
>    `EnvironmentFile` assignment cannot hold a newline, and the operator chose base64 over any
>    custom separator on 2026-09-17. A code round then showed that base64 alone does not make the
>    two shapes disjoint — `aCE0` sixteen times is a plausible raw key that decodes to printable
>    text — so the encoded form carries `# coai-bugs-admin-keys v1`, which a raw list cannot have.
> 2. **The post-deploy admin check runs on the HOST and needs no new root helper.** The plan
>    proposed one, installed by hand; four findings said a manual step nobody performs makes a check
>    that silently never runs.
> 3. **A send records a run row.** The plan expected the button to read the funnel. It cannot: a
>    pair is marked only on the server's acknowledgement, so during a send the counts say what they
>    said before it. `upload_runs` is the answer, and it is the collector's own shape.
> 4. **The issuance window is narrowed, not closed**, and that is the honest limit of story 3: the
>    server commits a key before the extension hears anything. Closing it needs a server-side
>    idempotency record in `coai-bugs`, which is
>    [PLAN_the_issuance_window_closes.md](../todo/PLAN_the_issuance_window_closes.md).
>
> Related docs: [module_server.md](module_server.md),
> [PLAN_a_corpus_of_real_defects.md](PLAN_a_corpus_of_real_defects.md),
> [PLAN_the_bugs_release_line.md](PLAN_the_bugs_release_line.md),
> [module_extension.md](module_extension.md),
> [module_tests.md](module_tests.md),
> [PLAN_the_issuance_window_closes.md](../todo/PLAN_the_issuance_window_closes.md) (its open tail).

## The goal

`coai-bugs` issues keys and has no way to look at them. The only answers to "is this key alive",
"who is flooding us" and "can I stop them" are an SSH session and three CLI modes. The operator asked
for a **Users tab in the Bugz section**: keys listed with what they have sent, a revoke button, and a
rate limit that is a setting rather than a constant.

## FIRST: this gives up a promise, and the honest version is worse than the comfortable one

`coai-bugs` was built so the server **cannot** say when a contributor was working
(`src_bugs/src/Corpus.cs:85`):

```sql
-- A counter WITHOUT a clock, deliberately. With timestamps it would be a record of when
-- a person we handed a key to was working.
```

The operator reconsidered deliberately and narrowed the ask to a last use and a total. An earlier
draft then stored a DATE and claimed it was materially different from a TIMESTAMP; a reviewer refused
that argument and was right — an operator seeing `2026-09-16` learns which day that contributor
worked. **Offered the narrower option, the operator took it**, so what is stored is the MONTH and the
promise below survives in a form worth printing.

**This is the CANONICAL wording — every other file quotes it, so it is edited here first.** It has
been narrowed three times, each time because a reviewer refused an evasion, and each refusal is worth
keeping:

1. The first version said "no clock time" while `admin_audit`, `createdUtc` and `revokedUtc` all
   store exact times. So it is **scoped**: contributors and administrators are different promises.
2. It then claimed a DATE was materially different from a TIMESTAMP. It is not — `2026-09-16` tells
   you which day somebody worked. So it is a **month**.
3. It said "no history" while `quarantine.received_utc` sat beside `key_id`, which made it plainly
   false for any pair awaiting review. The **data** changed rather than the wording (story 1, step 3).

> **About a contributor**, the server records only the **month a key was last used** and how much it
> has sent. It records no date, no clock time, no address, no name, and no history: there is no way
> to ask which day a contributor worked, or at what hour, or what a key did in any month but its
> last. **Nothing that carries a key id carries a clock**, and a test over the schema refuses any
> table that does.
>
> **An administrator can see the current minute**, because the rate limiter holds it in memory —
> `/admin/active` answers who is sending right now. It records nothing and there is no yesterday to
> ask about; this is the one thing a live view can tell you that no stored history does.
>
> **About administrators**, the server records exact times: when a key was issued, when it was
> revoked, and who did it. That is a log about the people holding administrative power, not about the
> people contributing.

### The operator chose the MONTH, and that is what is built

**`last_seen_month`, holding `2026-09`.** It answers "is this key alive" for any practical purpose
— used this month, last month, not since March — and does **not** say which day anybody worked.
The operator took the narrower option when offered it, which is why the promise below is still
worth printing.

## Decisions taken by the operator

Last use + totals, no histogram · rate limit per **key**, configurable · `COAI_BUGS_ADMIN_KEYS`
newline-separated, many, may also upload · admin API never returns keys except the one issuance ·
admin actions audited · `/admin` published normally · issuing through the API · extension's key in
**SecretStorage**.

## Facts that close three questions before they are asked

**There IS a production database now** — this paragraph said the opposite when the plan was written,
and it stopped being true before story 1 began: `bugs-v0.1.0` was released on 2026-09-16 and deployed
on 2026-09-17, and `/opt/coai-bugs/data/coai-bugs.db` exists with the released tables and
`user_version = 0`. It holds no content yet (no keys issued, nothing in quarantine), so no backfill is
needed — but its SHAPE is in the field, which is exactly the case the migration discipline exists
for: step 1 is that shape, frozen against the released tree, and a test migrates a file written by
step 1 only.

**One process, one Kestrel**, on loopback behind nginx, not load-balanced. The limiter is
per-service, and **resets on restart by design** — a deploy rolls the limiter back too, which is
acceptable here and is stated rather than discovered.

**The limiter never sees a client address, and must not.** This is the plan's sharpest correction and
it came from the round: our own vhost *clears* `X-Forwarded-For` and its four siblings, so Kestrel
sees `127.0.0.1` for every request. An application-level per-address limit would therefore put the
whole internet in one bucket — one flooder locking out everybody — and the only way to fix that would
be `ForwardedHeaders`, which re-admits a spoofable, promise-breaking address.

> **So the application limits by KEY ONLY. Unauthenticated traffic is nginx's job**, which it already
> does (`limit_req zone=bugs_req rate=2r/s burst=10`), at the one layer that legitimately sees the
> address and keeps it in memory rather than in a log.

That also removes a denial-of-service a reviewer found in the first draft: with no unauthenticated
callers in the table, the tracked set is bounded by **keys that exist**, so no flood of invented
credentials can fill it and start refusing real ones.

## The stories

> **Model policy**, per the gate's standing order: the split itself and stories **1** and **2** —
> schema migration, authentication, rate limiting — are done on **Fable (max)**, because being wrong
> there is expensive. Stories **3**, **4** and **5** are ordinary work on **Opus**. Each story is reviewed
> through `review_code`, resolved, documented, tested and committed **before the next begins**.

### Story 1 — the schema, and a limit that is a setting *(Fable)*

`Corpus.Schema` is one statement with a note saying it gains append-only discipline "when it ships".
It ships now: `PRAGMA user_version` + ordered `Steps`, as `src_mcp/src/Store/Schema.cs` does it.

- **Step 1** is today's schema verbatim. **Step 2** adds:
  - `api_keys.last_seen_month TEXT NOT NULL DEFAULT ''` — named `_month`, not `_utc` or `_date`, because `_utc`
    reads as a timestamp and invites one. **A UTC calendar month**, `yyyy-MM`, produced by an
    injectable clock so a test can sit either side of a UTC month boundary.
  - `admin_audit(id, admin_id, action, target, at_utc)`, indexed on `at_utc` — omitted from the first
    draft, where the first revoke would have had nowhere to write.
- **A test that migrates a database written by STEP 1 ONLY.** A fresh file proves nothing.
- Empty `last_seen_month` means *never used*; the UI says "never", not a blank.

**The growth budget, named before the first write** (`planning-docs.md` requires it, and the first
draft's "no retention policy" broke that rule outright):

| Table | Rate | Budget | Retirement |
|---|---|---|---|
| `api_keys` | issued by hand, tens per year | ~200 B/row | none — revoked rows are the audit trail |
| `admin_audit` | one row per administrative action | ~150 B/row | **swept to the newest 50 000 rows** in the same transaction as each write that crosses the mark |
| the limiter's `ConcurrentDictionary<string, Window>` (memory, one process) | one window per key that sent in the last minute; a window holds at most `limit` stamps | 8 B a stamp: ~80 B a window at the default 10, ~8 KB at the cap of 1 000. **Worst case is every issued key active inside one minute at the cap** — 200 keys ≈ 1.6 MB, 1 000 keys ≈ 8 MB | a `PeriodicTimer` sweep every 60 s drops windows with no stamp inside the minute; bounded STRUCTURALLY because subjects are authenticated key ids (the 401 comes before the limiter), so it can hold no more entries than `api_keys` has live rows plus configured administrators — a flood of invented credentials fills nothing |

50 000 rows is ~7 MB and decades of hand-driven administration; the sweep is four lines and exists
from the first write rather than as a CLI mode nobody will ask for. The dictionary's row was added by
story 1 when a reviewer named it as a growth surface (finding 22): it is the one bound here that
lives in memory, and it is stated per process because there is exactly one — see the serve lock
below.

**The rate limit:**

- `COAI_BUGS_RATE_PER_MINUTE`, default **10**; `0` disables and the disabling is tested, because a
  limit nobody can switch off takes the service down at 03:00. **Validated at startup and capped at
  1 000** — a reviewer computed that an unvalidated rate times the caller ceiling is a billion
  timestamps.
- **Per key only.** See above.
- Sliding window — a fixed bucket allows 2× the limit across its boundary.
- `ConcurrentDictionary<string, Window>`, `Window` a ring of UTC ticks **capped at the limit**: a key
  cannot hold more stamps than it is allowed requests. Bounded by the number of issued keys.
- A `PeriodicTimer` sweep every 60 s drops windows with no stamp inside them.
- **429 with `Retry-After`** and a body naming the limit. A 429 with no number is a client that
  retries immediately for ever.
- Applies to `/ingest` and `/admin/*`.
- **A revoked key is refused before the limiter and before any write.** `Corpus.KeyFor` already
  filters `revoked_utc = ''`; the story adds the test that proves revoking actually stops an ingest,
  because "the operator's stop button does not stop anything" is the failure worth pinning.

**Atomicity, both of which a reviewer asked for:** the accepted ingest, its counter and the
conditional `last_seen_month` update commit in **one transaction**, so a kill between them cannot
leave an accepted pair with a stale month. The update is conditional
(`... AND last_seen_month <> @month`), so a busy key rewrites its row at most once a month.

**What story 1 shipped, and where it deviates from the text above (2026-09-17).**

- **The runner is shared, not copied.** "`PRAGMA user_version` + ordered steps, as `Schema.cs` does
  it" became ONE runner, `CoaiMcp.Storage.SqliteMigrator`, extracted from `RoundsDb.Migrate` and
  referenced by both binaries; the gate ruled against a second copy. Not in `CoaiMcp.Core`, which is
  declared pure.
- **Step 1 is frozen against the RELEASE, not against itself.**
  `src_bugs/tests/fixtures/corpus-schema-step1-bugs-v0.1.0.sql` is the SQL `f7e5170b` evaluates, and
  `TheSchemaIsFrozenTests` holds the constant to it. `TheMigrationTests` migrates a file written by
  step 1 only, and the scenario over the real binary migrates the same shape.
- **A rate past 1 000 is refused at startup (78), not clamped** — a clamp is a silent fallback, and
  the doctrine says an illegal value fails naming the legal ones.
- **The limiter is immutable windows and compare-and-swap, not a locked ring.** Two findings said
  a `ConcurrentDictionary` protects the dictionary and not the `Window` inside it; admit and eviction
  are each one atomic dictionary operation, and two threaded races assert it.
- **"One process" is enforced, not assumed.** `ServeLock`, an exclusive open of `coai-bugs.serving`
  in the data directory, held for the server's lifetime; a second server exits 78; the one-shots do
  not take it. A restart still resets the limiter, by design.
- **The audit is written from story 1, not story 2.** `--issue-key` and `--revoke` audit as `cli`, in
  the same transaction as the mutation and the sweep — a table nothing writes to has a sweep nothing
  exercises. The sweep is an INDEXED cutoff on the primary key, never `NOT IN (SELECT …)`.
- **`/admin/*` has its limiter identity now**: `LimiterSubject.Administrator(hash)` →
  `admin-` + 8 hex, the spelling story 2 must use, so admin routes cannot land in the contributor
  bucket.
- **The order on `/ingest` is 401 → 429 → 400 → work**, stated in code and tested: a malformed body
  from an authenticated key consumes a slot; nothing but an accepted ingest touches the key's row.

**What the CODE round then changed (2026-09-17, 48 findings — 29 accepted, 19 rejected).**

- **The promise was false, and the DATA changed rather than the wording.** `quarantine.received_utc`
  is an exact instant beside `key_id`, so a pair awaiting review could be asked which day and hour a
  contributor worked. **Step 3** adds `received_month`; `received_utc` stays as a deliberately dead
  column because step 1 is frozen. `TheQuarantineHoldsNoClockTests` now asserts the general rule —
  every table carrying a `key_id` carries no clock — so the next such table is covered by default.
  Rewording the promise to exempt quarantine was refused for the reason an earlier draft's
  "a date is not a timestamp" was refused: it is the same evasion.
- **The counter and the month are ONE `UPDATE`.** The conditional month write was justified as
  sparing a row rewrite, and the round refuted it: the counter rewrites that row on every ingest
  anyway, so the condition bought a second statement and a second B-tree lookup and saved nothing.
  `MonthAdvanced` went with it, and the test that asserted it now asserts what is true instead —
  three ingests count three, and the month follows the clock.
- **A revoke racing the write is refused by the write.** `KeyFor` authenticates before the limiter,
  and a `--revoke` can commit in the gap; `Corpus.Accept` re-checks inside its own transaction and
  answers `Accepted<T>.KeyNotInForce`, so nothing is stored, counted or stamped for a dead key.
- **Authentication and admission are middleware, ahead of body binding.** Binding `UploadRequest?`
  from the body made ASP.NET deserialize before the handler ran, so a flood was parsed in full
  before most of it was refused. `TheGateComesBeforeTheBodyTests` pins the order over real requests.
- **The audit trail reads NEWEST FIRST, paged by an id to read before** — not `OFFSET`. Page one was
  the first day of the deployment, and recent history cost a scan across everything before it.
- **`Usage` is a union, and absent is not zero.** `UsageOf` answers `NoSuchKey` or
  `Known(SubmissionCount, LastSeen)`, and `LastSeen` is `Never` or `In(UtcMonth)` — so a UI cannot
  render a blank for "never", and a rolled-back issuance reads as no such key rather than as a key
  that has sent nothing. The test helper deliberately THROWS on the absent case instead of
  answering 0, because a convenience that defaulted would put the same collapse back into the tests.
- **`/health` does NOT report the rate limit.** A finding asked for the limit to be visible and the
  problem is fair, but the first implementation put it on the unauthenticated public endpoint, where
  it tells a flooder exactly how fast it may go unrefused — and the same reasoning had already kept
  the VERSION out of `/health`. The existing test `HealthSaysNothingAboutTheCorpus` is what caught
  it. The limit is named in the startup log on every boot, and story 2's authenticated `/admin/*` is
  where a UI will read it.
- **Nineteen findings were rejected with reasons**, most of them from one reviewer asserting the
  audit sweep runs outside the mutation transaction (it does not; the drop-the-table test pins it),
  that a missing `admin_audit` is reachable (it is not — `Corpus.Open` migrates before handing out a
  `Corpus`), and that the 429 lacks a `Retry-After` it demonstrably sets.
- **The unit gains `UMask=0027`** (finding 13), so the `.db`, `-wal` and `-shm` this process creates
  are not world-readable by construction; the directory's `0750` stays the primary boundary, and the
  file the earlier unit created needs one `chmod`.
- **Left to story 4:** the four-place promise rewrite with the test that pins it. Story 1 rewrote
  `research/module_server.md`, `deploy/bugs/README.md` and the `Program.cs` remark because the new
  column made their old wording false; the nginx vhost's header comment and the pinning test are
  story 4's.

### Story 2 — admin keys and the admin API *(Fable)*

- `COAI_BUGS_ADMIN_KEYS`: newline-separated; blank lines and `#` comments ignored.
- **Hashes in an ARRAY, not a `FrozenSet`.** A reviewer was right that `FrozenSet.Contains` is a
  hash-and-probe with data-dependent timing. Every configured hash is compared with
  `CryptographicOperations.FixedTimeEquals` and the results accumulated **without an early return**,
  exactly as `Corpus.KeyFor` already does for contributor keys.
- An admin id is derived: `admin-` + the first 8 hex of its hash. Deterministic, no table. **It is a
  truncation, so a collision is silent**: two admin keys whose hashes share those 32 bits would be
  indistinguishable in the audit. With a handful of administrators the probability is negligible, and
  it is recorded rather than assumed because the audit's whole job is saying who did it.
- An admin key **may also upload**; the derived id serves at the ingest boundary.
- **Admin credentials are not rows in `api_keys`** and therefore appear nowhere in the Users tab, have
  no counters and no `lastSeenMonth`. Accepted, and the tab **says so**: an admin credential is
  rotated by editing the secret and redeploying, not by revoking a row.
- Startup logs once at Information when the variable is absent, because an admin API with no admins
  answers 401 to everybody and looks broken.

> ⚠️ **THE PAGING IN THIS SUBSECTION AND THE TABLE BELOW IT IS SUPERSEDED.** Read
> *"Story 2's contract as the plan round RESOLVED it"* further down before implementing any of it.
> `skip` and `total` are gone: paging is keyset (`?limit&before`) on both routes, because offset
> paging is not insert-stable and story 1 already removed `OFFSET` from `AuditTrail`. The rest of
> this subsection — the JSON-object rule, the 400-not-a-clamp rule, the key-return rule — still
> stands. It is left in place rather than rewritten so the change is visible and the reasoning has
> something to argue against.

Every response is a JSON object, never a bare array. Paging parameters are non-negative integers —
anything else is **400**, not a silent clamp — `limit` defaults to 50 and is capped at 200. **Every
list is ordered so a row inserted between pages cannot duplicate or hide one** (the resolved section
says how; `created_utc, id` with an offset does not achieve it).

```
GET  /admin/keys?skip&limit
  200 { items:[ {id,note,createdUtc,revokedUtc,lastSeenMonth,sent,waiting} ], total,skip,limit }

POST /admin/keys  { note }
  201 { id, key, note, createdUtc }        <- the ONLY response that ever carries a key
  400  note over 200 chars, or one shaped like an email address

POST /admin/keys/{id}/revoke
  200 { id, revokedUtc, changed:true|false }      idempotent
  404  no such key

GET  /admin/audit?skip&limit      200 { items:[ {id,adminId,action,target,atUtc} ], total,skip,limit }
GET  /admin/active                200 { items:[ {id,inWindow,limited} ], windowSeconds }
```

- **The key-return rule, precisely** — the first draft contradicted itself: no response ever carries
  an existing key's value or any hash; the single successful issuance carries the new key once and it
  is never retrievable. Both halves are tested.
- **`/admin/active` answers "who is flooding us NOW"**, which no historical count can. It is read
  straight from the limiter, persists nothing, and lists **keys only** — there are no address buckets
  to expose, which is the second thing the key-only limiter bought.
- **A lost issuance response is recoverable without idempotency machinery**: the key exists as a row
  with its note and creation time, and the admin revokes it and issues another. The tab says this
  where it shows the key. (An idempotency token was considered and rejected as more machinery than a
  two-click recovery deserves.)
- **Every mutation and its audit row commit in ONE transaction**, so the API cannot report success for
  an unaudited action nor failure for a completed one.
- `note` is validated: over 200 characters, or shaped like an email address, is a 400. **It catches
  one shape and no other** — `bob@example.com` is refused and `Bob Smith` is not, which is the very
  mistake the guard exists for. It is worth having because it is free, and it is written down here
  because a guard that stops one spelling of a problem invites the belief that the problem is
  handled. What actually protects the promise is that the note is OUR record of why a key exists and
  nothing reads it as identity.

#### Story 2's contract as the plan round RESOLVED it (2026-09-17)

The round returned 21 findings — 18 accepted, 3 rejected. Four of them were questions this plan had
left open and I put to the round rather than deciding alone; all three providers converged on the
same two, which is why the answers below are the contract and the table above is superseded where
they disagree. **Story 3 consumes this section, not the table.**

**1. Paging is KEYSET everywhere, and `total` is gone.** The table's `?skip&limit` with
`total,skip,limit` contradicts what story 1 shipped: a code round replaced `AuditTrail`'s `OFFSET`
with keyset paging because page one was the first day of the deployment and recent history cost an
`O(N)` scan while holding the corpus gate. Three providers said the same thing, and one added the
part I had missed — **offset paging is not insert-stable for `/admin/keys` either**: issuing a key
between page one and page two shifts the boundary and duplicates or hides a row.

```
GET /admin/keys?limit&before    200 { items:[…], limit, nextBefore? }
GET /admin/audit?limit&before   200 { items:[…], limit, nextBefore? }
```

Both newest-first, `limit` default 50 and capped 200, `before` an exclusive id. `nextBefore` is
present only when another page exists, so it doubles as `hasMore`; a client pages until it is
absent. No `skip` and no `total` on either: with keyset paging `total` is the one field that cannot
be answered cheaply, and on `admin_audit` it is a `COUNT(*)` over 50 000 rows on every page while
holding the gate. `limit=0` is **400**, not "everything" — a limit nobody can exceed is how a
listing endpoint becomes a full-table read. A `before` past the end returns `items: []` and no
`nextBefore`, which is not an error.

**2. Constant-time means constant with respect to the CREDENTIAL, not to the number of admins.** The
DoD's "independent of how many admins are configured" is impossible and two providers said so: N
comparisons take O(N). The guarantee that matters is that timing reveals nothing about the presented
credential — no early return, so it cannot leak WHICH configured key matched or whether one matched
early. The count of configured administrators is not a secret: it is in the operator's own secret
store, and nothing about it is inferable from a 401 that always costs the same walk.

**3. An admin key uploads through its OWN path, and the in-force re-check is not bypassed — it does
not apply.** This was the question with no good answer, and all three providers refused to let it
stand. `Corpus.Accept` re-checks `InForce(key)` inside its transaction against `api_keys`, which an
admin credential is deliberately absent from, so the plan's "an admin key may also upload" was
either a rejection, a silent weakening of story 1's revoke-race guard, or an uncounted write.

The resolution: a separate `Corpus.AcceptAdmin` path, validated against the in-memory admin set.
**Why that is not the weakening it looks like** — the in-force re-check exists for a race that
cannot happen to an admin credential. A contributor key is revoked by a `--revoke` one-shot that can
commit between the gate and the write, in another process, against the same file. An admin
credential is rotated by editing the Actions secret and redeploying, which **restarts the process**:
the in-memory set is immutable for the lifetime of the server that authenticated against it, so
there is no window between the gate and the write for it to change in. The guard is not skipped;
there is nothing for it to guard.

The quarantine row carries the derived `admin-…` id. No counter and no `last_seen_month` are
recorded, because admin credentials are not rows in `api_keys` and there is nothing to increment —
so an admin's contributions are invisible to the Users tab, which the plan already accepts and which
story 3 must say out loud.

**4. Recovering a lost issuance needs no token, but it does need the listing.** The plan rejected an
idempotency token, and the round pointed out what that leaves: if the row commits and the response is
lost, a retry creates a second key and with duplicate or empty notes there is no handle to find the
first — an unknown valid credential stays active. The recovery is the listing, and it only works
because of point 1: `/admin/keys` is **newest-first with `createdUtc`**, so an admin who lost a
response sees a key created moments ago that they do not hold, and revokes it. That path is tested
rather than asserted. A committed transaction cannot be rolled back after the fact and the audit row
cannot be moved outside it, so those were not available fixes.

**5. Absent and invalid admin credentials are indistinguishable from outside.** The same JSON 401,
the same body, no body binding, and no admission to the contributor limiter. A distinct status for
"no administrators are configured" was proposed and refused: it turns the admin surface into an
oracle for whether administration is enabled, answerable by anybody. The operator learns it from the
startup log, which is on the host.

**6. `Corpus.Revoke` must distinguish "no such key" from "already revoked".** A `bool` cannot, so
the route could not answer 404 against 200 `changed:false` without a second query and a race between
them. It returns a closed union instead.

**7. `/admin/active`'s shape never varies** — an empty limiter answers `{ items: [], windowSeconds }`,
not a different document.

**8. The scenario harness covers the admin routes in THIS story**, not story 4. Story 2 can pass
in-process and over HTTP while the deployed binary, the environment parsing and the real
authentication are untested; deferring that is how a broken shipped route gets accepted as complete.

Rejected, with reasons recorded in the gate: a compensating delete or an audit row written after the
response (a committed transaction cannot be undone, and moving the audit out of it reintroduces the
defect story 1 closed); a distinct status for the absent variable (see point 5); and a retention
policy for `api_keys` (the plan's own growth budget already sets it — none, because a revoked row IS
the record that the key existed and was stopped).

#### Four more the OPERATOR settled (2026-09-17), after the round missed them

**9. `/admin/*` gets its own limit: `COAI_BUGS_ADMIN_RATE_PER_MINUTE`, default 120.** Validated and
capped the same way as the contributor setting, `0` disables. The arithmetic nobody had done: the
audit retains 50 000 rows and pages at most 200, so reading it is **250 requests**, and at the
contributor default of 10 a minute the Users tab hits its own `429` after ten pages and needs 25
minutes for the whole table. One number cannot serve both surfaces — the contributor limit is flood
control on a public endpoint, and admins are a closed set of authenticated people. Raising the
shared default was the wrong trade: it would loosen flood control to suit a UI.

**10. `total` for keys, none for the audit.** Removing `total` everywhere left no way to count
anything, because `limit=0` is a 400 — so a UI could not say "N keys" without paging all of them.
`/admin/keys` returns `total`: it is tens of rows and the `COUNT(*)` is free. `/admin/audit` returns
only `nextBefore`, because there a count is 50 000 rows scanned on every page while holding the
corpus gate. The asymmetry is deliberate and each side is honest about what it can afford; story 3
shows an exact key count and the audit as a paged list with no total.

```
GET /admin/keys?limit&before    200 { items:[…], limit, total, nextBefore? }
GET /admin/audit?limit&before   200 { items:[…], limit, nextBefore? }
```

**11. Revoking an admin credential is NOT immediate, and that is accepted.** A consequence of point
3 that point 3 did not state: if the in-memory set is immutable for the process's lifetime, then
removing a key from `COAI_BUGS_ADMIN_KEYS` does nothing until a **successful** redeploy — and a
deploy that fails or rolls back leaves the credential you just revoked still live. Contributor keys
have `--revoke` and die instantly; admin keys have no equivalent.

The operator accepted this rather than closing it, and the alternatives are recorded because they
are the obvious suggestions: an in-memory kill switch is state a restart silently discards, which is
the opposite failure and easier to forget; and persisting admin revocations means admin credentials
become rows in `api_keys`, which the operator had already settled against and which reopens what the
tab shows. **So it must be said loudly** — in `deploy/bugs/README.md` and in the tab — that an admin
credential dies on a successful redeploy and that the emergency measure is stopping the service.

**12. `/admin/active` is a real-time view of contributor activity, and the promise must say so.** It
records nothing, so it keeps the letter of the promise — but "there is no way to ask which day a
contributor worked" does not obviously cover "there IS a way to ask whether they are sending right
now". That capability is the point of the endpoint and it is defensible; leaving a reader to
reconcile it against the promise is not. The promise's wording gains a sentence: the server keeps no
history, and an administrator can see the current minute.

#### What story 2 actually shipped, where it differs from the text above

- **Built by Opus, not Fable.** Fable's spend limit had not reset; the model policy is a preference
  and a blocked model is not a reason to stop. Said plainly rather than quietly.
- **`/admin/active` reads BOTH limiters.** The two limits are separate settings and therefore two
  `RateLimiter` instances, each holding only its own callers — so the route as first wired was handed
  the ADMIN limiter and would have answered with administrators and no contributors, the exact
  inverse of decision 12. Found by reading decision 12 against the wiring, not by a test. The merge
  and its ordering happen in the route, and `RateLimiter`'s remark no longer claims one instance sees
  both kinds.
- **The absent-variable startup line is a WARNING, not Information.** The subsection above says
  Information. The disclosure rule makes this line the operator's ONLY channel for the difference
  between a missing secret and their own typo, and at Information it sits among a dozen startup
  lines. Two tests read it — the warning when absent, and the count beside the admin limit when
  present. Recorded as a deviation rather than taken silently.
- **The 429 names the limit the caller reached.** `IngestGate` had one wording, "per key", so an
  administrator at the admin limit was told the contributor noun — which sends them to the wrong
  environment variable, the very thing that method's own remark claims it avoids. `Uploader.Noun`
  carries the word. Caught by a test written for the admin upload path.
- **Every refusal now spells its one field the same way.** A route's `TypedResults.BadRequest` goes
  through the host's JSON options, which camel-case names; a gate writes its body with a
  source-generated `JsonTypeInfo` directly, which uses the CONTEXT's options — and `BugsJson` had
  none, so one server answered `why` from a route and `Why` from a gate. The policy is declared on
  the context; a test reads the bytes of all three refusal paths. **This was a pre-existing defect**
  in story 1's gate, surfaced by story 2 adding admin error bodies a UI must parse.
- **`/ingest` takes either credential through an `Uploader` union.** `AcceptAdmin` existed with NO
  CALLER — the DoD item was unsatisfiable as built, and an admin key at `/ingest` got a 401. The gate
  resolves the credential, the endpoint switches on the union, and each kind is limited by its own
  setting. `LimiterSubject.Administrator` now takes an `AdminId` rather than a raw hash, which
  removes the second place the id was derived.
- **`AdminId.Of` records the truncation collision**, as the subsection above requires and the code
  did not: eight hex digits is 32 bits, two colliding admin keys would be one id in the audit and one
  bucket in the limiter, and lengthening it later would rename past administrators in the trail.
- **Three stranded docblocks were fixed**, from inserting methods between a comment and its member:
  `Corpus.AuditTrail` had lost its summary to `AcceptAdmin`, `RateLimiter.StampsOf` to `ActiveNow`,
  and `Corpus.Revoke` carried its superseded pair above the new one. A sweep now looks for the shape.
- **`deploy/bugs/README.md` and `.agents/PROJECT.md` gained the two variables now**, though the
  subsection above assigns the registration to story 4: shipping a credential variable undocumented
  is worse than doing a later story's documentation item early, and `AdminKeys`' own remark claims
  the README warns about admin rotation — a claim that was false until this story made it true. The
  README also says plainly that **delivering the variable is not automated yet** and points at story
  4, because the failure is quiet: a release starts perfectly and answers 401 to every admin call.
- **Still story 4's, untouched here:** the deploy workflow delivering `COAI_BUGS_ADMIN_KEYS` from
  Actions Secrets (the forced command's `secret` verb takes one line on stdin today, and a
  newline-separated value does not fit an `EnvironmentFile` assignment — that is a real design
  question, not a typing task), and the four-place promise rewrite with its pinning test.
- **`/admin/active`'s empty case is asserted one layer down.** It cannot be observed through the
  route: reading it IS a request the admin limiter admitted a moment earlier, so the reader is always
  in its own answer. The HTTP test pins the shape and the absence of any contributor;
  `TheRateLimiterTests` pins the genuinely empty list. The first version of that test asserted an
  empty list over HTTP and went red for exactly this reason.

#### What the code round changed (round 1 of 2, verdict `proceed`, 28 findings)

Ten of twelve reviewers answered; the local provider's security and UX reviewers timed out at the
round's ten-minute limit. **15 accepted, 13 rejected with reasons.** The accepted ones, and what
each actually was:

- **A timing oracle the identical-bytes test could not see.** `AdminKeys.Match` returned early when
  no administrator was configured, so it never hashed the presented credential — while a configured
  deployment paid for an HMAC on every attempt, and failed credentials are not rate-limited. The
  anti-oracle contract was kept on the bytes and open on the clock. An empty configuration now
  compares against one stand-in of the same length.
- **The keys cursor was `rowid`**, whose implicit numbering a `VACUUM` renumbers — and the comment
  defending it argued a different risk (reuse after deletion). It is `(created_utc, id)` now, and
  BOTH listings hand out an opaque token; `admin_audit.id` is a real `INTEGER PRIMARY KEY` and is
  preserved, which is why only one of them needed a composite key. The separator is `~` because `|`
  is not legal in a query string.
- **Any integer was accepted as a cursor**, so `?before=-1` answered an empty page and a broken
  pager looked like a finished one.
- **A credential could be both an administrator and a contributor key**, and revoking the key would
  then PROMOTE it to administrator. Refused at startup with 78; both precedence rules are wrong in
  one direction, so there is no rule, only a refusal.
- **`/admin/active` was unbounded** — a thousand live keys in one minute is a thousand records for
  one request, so the response grew with the flood it diagnoses. It takes the same `limit` and
  carries `total`.
- **`Accept` and `AcceptAdmin` each owned their transaction lifecycle**, so an invariant added to one
  would miss the other. The lock, the transaction, the scope's lease and the commit are one shared
  method; what differs is the in-force check and the counter, which is the whole point.
- **The route catalogue in the gate tests was retyped**, and named three of five routes — both POSTs,
  the issuance among them, were outside every shared assertion. It reads the host's own
  `EndpointDataSource` now.
- **`ActiveNow` answered a three-field tuple**, against the doctrine's "a data container is a
  record"; and it sorted its own rows, which the route then re-sorted with the same comparator.
- Plus the smaller ones: the hash computed once instead of twice in the matching loop, and
  `Refused` renamed to `Malformed` — it sat in a file beside a 401 refusal and a 429 refusal and
  said nothing about which of the three it was.

**Four of the thirteen rejections were self-refuting findings**, where the reviewer's own text
reached the right answer and the title kept the wrong one — including one that asked for an early
return to be ADDED to the constant-time loop, and one whose analysis ends "This is compliant. Wait,
I". Two were factually wrong about an API (`StartsWithSegments` matches segments, not characters —
now pinned by a test that `/administrator` is not gated) or about reachable state (`--revoke` cannot
revoke an administrator, which has no `api_keys` row). Five asked for documentation that is already
there, in one case three times over. One — a 16-hex id colliding and answering 500 — is real and
was rejected with the arithmetic: 64 bits of entropy against a table of tens of rows a year is less
likely than a disk fault this code also does not retry, and the insert is inside a transaction, so a
collision rolls back rather than corrupting anything. It is recorded rather than fixed.

**Two defects were found by the round's own fixes, not by the round.** The structural
no-early-return test went red on the word "returned" in the loop's own comment (it strips comments
now), and the docblock guard written after four stranded comments in this story MISSED the very
shape it was written for until its break-it step exposed that single-line summaries close on their
own line.

### Story 3 — the Users tab *(Opus)*

- A button in the existing Bugz section (`src_vs_code/src/bugzView.ts`), opening a panel beside
  `bugzReviewPanel.ts` — the closest well-written neighbour.
- **`SecretStorage`, and the flow that fills it.** This is the repository's first use of it, and a
  reviewer caught that the first draft never said how a key gets in: a command
  **“ConnectOtherAIs: Set the bugs admin key”**, plus the tab offering it when the key is absent or
  rejected. Empty, invalid and rotated keys each have a state and a test. Never `settings.json`: that
  syncs.
- **Issuing a key requires copying it.** The panel shows the key with a Copy button and will not
  dismiss until it is copied or the admin confirms discarding it — otherwise a misclick leaves an
  active key nobody has.
- Revoke **asks first**, naming the key's note and last-use month, because ids are hex and look alike.
- Failures show the server's own words; a tab that silently does nothing is worse than no tab.
- Says plainly that keys cannot be shown and that admin credentials are not listed.
- **The page is RUN by its test**, not read as text — `.agents/PROJECT.md` refuses a behavioural
  assertion over page source, and a list with a destructive button is exactly where a control wired to
  the wrong row is the defect that matters.

#### Story 3's contract as the plan round RESOLVED it (2026-09-17)

The round returned `good_enough` with 18 findings and **all 18 were accepted** — which is not
agreeableness: the questions put to it were the ones this story was least sure of, and every answer
came back with a specific this plan had not written down. Where a finding's proposed FIX is not what
is being built, that is said here, because accepting a finding commits you to the problem and not to
its suggested cure.

**1. ONE honest state for every 401, and it must not name a cause.** The plan said "empty, invalid
and rotated keys each have a state". Two reviewers refused that independently: story 2 makes an
absent `COAI_BUGS_ADMIN_KEYS`, a wrong key and a revoked one *deliberately indistinguishable* — same
status, same body — so a tab claiming "your key is invalid" sends an administrator to re-enter a
correct key when the server simply has none configured. There is one rejected state. It renders the
server's own `why`, offers **Set the bugs admin key**, and names BOTH possible causes without
choosing: the key held here, or the server's own configuration, which only its startup log can
settle.

**2. The issuance is COMMITTED before the panel can show it, so a modal cannot keep the promise.**
Three reviewers found this, and it is the sharpest thing the round produced. `POST /admin/keys`
creates the key; the panel then displays it. A webview cannot veto its own disposal — the editor's
tab control, a window close and an extension-host reload all dispose it — so "will not dismiss until
copied" is enforced by nothing, and the key is unrecoverable because story 2 returns it exactly once.

> So the guarantee moves OUT of the modal. The moment the `201` arrives, the extension writes a
> **pending issuance** (id and key) to `SecretStorage`, and clears it only on an explicit copy or an
> explicit discard. Whenever the tab opens it checks for one and surfaces it: *a key was issued and
> never copied*, with **Copy** and **Revoke**. **Discard REVOKES through the API** rather than
> forgetting — a discarded key that stays alive is the exact defect the rule was written against.

Rejected fix: gemini suggested generating keys client-side and sending only hashes. That is a server
contract change in a UI story, and it would move key generation off the box that owns the secret.

**3. Paging is a client-side cursor STACK, and there are no page numbers anywhere.** `nextBefore` is
opaque and forward-only; a client never composes one, so "previous" cannot be computed and a `page N
of M` cannot be rendered. Each page pushes the cursor that fetched it; **Back** pops; Back is
disabled at the root. `total` on the keys page is shown as *"N keys"* — a count of the table, never a
page count. Rejected fix: local suggested querying the server for a previous page, which this
contract cannot answer.

**4. The audit and `/admin/active` are NOT in this story**, and that is now written down rather than
discovered. Story 3's bullets name the keys listing, issuance, revocation and the key-entry flow.
Three findings arrived about an audit view with no `total`; the honest answer is that this tab does
not render one yet, so the rule they imply is recorded for whoever does: **cursor/batch navigation,
Next enabled only while `nextBefore` is present, and no claim about how much is left.** Shipping the
audit API with no reader is a known gap from today, not a surprise later.

**5. Every answer the server can give has a state.** The plan listed three and the round found five
more:

| What arrives | What the tab does |
|---|---|
| `401` | The one rejected state of point 1. Never a diagnosis. |
| `429` | Says it is rate-limited and when to try again, from `Retry-After` and `why`. **No automatic retry** — a silent retry hides the limit and spends the next window. |
| A network failure, or nothing at all | *Cannot reach the server* — explicitly NOT a credential problem, because that is the misreading that sends somebody to rotate a working key. |
| `400` | The server's `why`, verbatim. It is written to be read. |
| `404` on revoke | No such key: the row is stale, so the listing refreshes. |
| `200` with `changed:false` | Already revoked — show the ORIGINAL `revokedUtc` the server returned, not the time of this attempt. |

**6. An issuance is never retried automatically.** If the request fails without an answer the key may
exist: the tab refreshes the newest-first listing and says so, because that ordering IS story 2's
lost-issuance recovery and this is the case it was designed for.

**7. After any mutation the row is re-read from the server, never patched locally.** State drifts —
another administrator, another window, the CLI on the host — and a tab that edits its own copy is a
tab that disagrees with the truth silently.

**8. The page test RUNS the page, and its load-bearing assertions are named here** so "it executes"
cannot be mistaken for "it is tested": with **two** keys rendered, clicking each row's revoke control
asserts the confirmation names THAT row's note and month; the copy and discard branches each assert a
changed rendering; and a click must not also trigger row navigation. A list with a destructive button
is exactly where a control wired to the neighbouring row is the defect that matters, and that is the
one a source-text assertion cannot see.

#### What story 3's code round changed (round 1 of 2, verdict `revise`)

All twelve reviewers answered, 41 findings: **28 accepted, 13 rejected**. Four of the rejections
were about text that is not in the files — a backtick in a CSS comment, a `JSON.stringify`
interpolated into the page's script, and twice that `safe()` leaks script context — and the page's
script interpolates nothing at all, which is the shape that makes those impossible rather than
merely absent. Three more were self-refuting, reaching the right answer mid-paragraph while the
title kept the wrong one.

The accepted ones were mostly ways to LOSE A KEY, which is the one thing this tab exists to prevent:

- **Issuing twice overwrote the pending record.** One slot, so the second issuance destroyed the
  only copy of the first — a live key nobody holds. Issuing is refused while one is pending.
- **A discard could be sent to a server that never issued the key.** The address is a setting and
  can change while a key is held, so B's 404 read as proof that A's key was gone. The record carries
  its issuer and a discard goes there.
- **The response was CAST, not read.** A `201` with no `key` passed the cast, was written to
  `SecretStorage`, and was rejected on the way back out — the sole copy of a committed credential
  destroyed by the code meant to preserve it. `bugsAdminWire.ts` reads every body field by field.
- **The admin key could be sent over plain `http`.** Checked before the request now, loopback
  excepted, because a key disclosed to a mistyped host is disclosed even when the answer is 401.
- **The sentence explaining an action was cleared on every draw** and rendered only by the listing —
  so *a key may exist, do not ask again* was discarded exactly when the server was unwell and a
  different face appeared. Every face carries it, and it is cleared only once rendered.
- **A 400 was rendered as "the server could not be reached"**, blaming the connection for a reply
  and offering a retry of the same refused request.
- **Nothing said an action was happening.** A ten-second request with every control live, so Issue
  pressed twice queued two issuances.
- **The command built a second panel**, so a key set through it left an open tab on its rejected
  face. One shared instance.
- **`noEmitOnError` was off**, so `tsc` emitted despite errors and `node --test out/` would run
  stale output — which fooled this session twice before the round named it.

**The residual window is narrowed and NOT closed, and that is the honest limit of this story.** The
server commits the key before the extension hears anything, so a host death in that instant leaves a
live key with no local record; two commits cannot be made atomic from one side. An ATTEMPT is now
written before the request leaves — it cannot name the key, but the next open says one may exist and
points at the newest row, which is what story 2 ordered the listing for. **Closing it needs
`coai-bugs` to keep a server-side pending record or accept an idempotency token**, and three
reviewers independently said the client-only version is not enough. That is a SERVER change and it
belongs to a later story; it is written here so it is a decision rather than an oversight.

**And the fix for one finding produced another**, caught by a guard rather than by a reviewer: the
validator added for the cast findings imported its types from the client while the client imported
the validator, and `importCycles` went red with the sentence that says why it is not a matter of
taste — *it will bundle and fail at runtime*. The shapes now live in `bugsAdminShapes.ts`,
underneath both.

#### And what round 2 changed (round 2 of 2, verdict `good_enough`)

All three reviewers answered, 10 findings: **4 accepted, 6 rejected**. The rounds were exhausted, so
the verdict is the policy's rather than the reviewers' — which makes reading them properly the whole
point of the setting.

The three accepted gating ones are all the same defect seen from different sides: **the panel owned a
flag without owning the repaint that clears it.**

- **The tab could be left dead.** `busy` disables every control, Refresh included — deliberately,
  because a second Issue is a second live key. The flag dropped in a `finally`, and the page on
  screen had been painted before that. Nothing repainted, and nothing on the page could be pressed to
  make it. Only closing the tab helped. (codex)
- **And it said nothing while it worked.** The other end of the same thing, and a defect round 1's
  own fix introduced: nothing painted when an action BEGAN, so during the ten seconds that matter
  every control was live and Issue pressed twice still queued two issuances.
- **Two doors bypassed the serializer.** Opening the tab and the key command drew straight out, so a
  listing fetched before an issuance completed could land after it and paint a page with no pending
  key on it — hiding the only copy of a live credential behind a redraw nobody asked for. (codex)

Both now live in `bugsKeysTurns.ts`, a coordinator with no editor import, which is what makes them
testable at all: the panel imports `vscode` and cannot be loaded by a test. The repaints ask the
server nothing — `withControls` hands back the page already on screen with its controls changed, or
nothing when they already are.

The other two:

- **Setting the key walked the administrator back to the newest page** (gemini). The cursors are a
  walk through one listing and there is one admin surface, so a new key sees the same rows: the reset
  bought nothing and cost a position.
- **Discarding revoked without asking** (gemini), while the table's Revoke button confirms. Now it
  asks, through the same funnel, naming both losses — the key stops working for whoever holds it, and
  the local copy is gone whatever is answered.

**Six were rejected, and three of those were gating**, which is the reason to verify rather than
comply: one described a race in `discard()` and then, mid-paragraph, read the code again and
described the fix that is already there; one asked for a disposal check that `draw()` performs twice,
before and after its only await; and one said the **Who holds a key** button reaches an unregistered
command, when it is not a command at all — it is a `PanelCommand` whose switch has an exhaustiveness
`never` that would fail the build if it were unhandled. The other three were naming and speculative
generality.

**And one thing this round is on record as NOT finding.** The break-it check for the repaint was run
twice: the first attempt left an unused parameter, `tsc` refused to emit under `noEmitOnError`, and
the suite ran the PREVIOUS build and reported 28 green — a test with no teeth looking exactly like a
test with teeth. It was caught by the numbers being identical, and the second attempt went red with
*issue is dead after the action finished*. The lesson is in the same family as `noEmitOnError`
itself, which round 1 added: a build whose output is hidden is a result that proves nothing.

### Story 4 — the promise, the deployment, and the scenario *(Opus)*

- The promise's REMAINING wording — "four places" is no longer the count. Story 1 already rewrote it
  in `research/module_server.md`, `deploy/bugs/README.md` and the `Program` remark. What is left is
  the stale "a counter without a clock" remarks in `deploy/nginx/coai-bugs`, `src_bugs/src/Ingest.cs`
  and `src_bugs/tests/TheRouteTests.cs` — **and NOT** the one in step 1's SQL, which is frozen against
  the released commit and must stay exactly as it shipped. Also the sentence from decision 12: the
  server keeps no history, and an administrator can see the current minute. **With a test** that the phrase
  "a counter without a clock" cannot survive beside a column that has one.
- `COAI_BUGS_ADMIN_KEYS` and `COAI_BUGS_RATE_PER_MINUTE` registered in `.agents/PROJECT.md`. **No new
  one-shot modes**; `--issue-key` and `--revoke` keep working unchanged.
- **The deploy workflow delivers the admin keys** as it already delivers the secret — and a reviewer
  was right that nothing would have caught its absence: a release can start perfectly and answer 401
  to every admin call. The forced command's `secret` verb takes both, and the deploy asserts an
  authenticated `/admin/keys` afterwards.
- **They travel BASE64-ENCODED, and the operator decided so on 2026-09-17.** The problem story 2
  surfaced: `COAI_BUGS_ADMIN_KEYS` is newline-separated, the forced command reads ONE line from
  stdin, and a systemd `EnvironmentFile` assignment cannot hold a newline. Three answers were
  possible and only one was taken.

  > **No custom separators in an `EnvironmentFile`.** The value is base64 on the wire and base64 in
  > the file; the server decodes it.

  Rejected, and why it matters that they were: a second separator (`,`, `;`) would put a SECOND
  parsing rule into a credential boundary — `AdminKeys.Lines` splits on newlines, so the delivery
  path and the code would disagree about what a key is, and a key containing the separator would
  silently become two keys that match nothing. Quoted escapes in the unit file depend on systemd
  version-specific parsing of `\n` inside double quotes, which is a thing to discover on a host at
  02:00 rather than to rely on.

  What this costs story 4, stated now so it is not discovered later: `AdminKeys.Read` gains a decode
  step, and the two shapes must not be ambiguous — a base64 payload and a raw newline-separated one
  can both be non-empty text, so the variable is base64 ALWAYS, not "base64 if it decodes". A value
  that is not valid base64 is a startup refusal naming the variable, like every other unusable
  setting here; it must not fall back to reading the raw text, because a fallback turns a typo into
  a server that starts with the wrong administrators. `deploy/bugs/README.md` gains the
  `base64 -w0` line an operator actually types, and the real-binary scenario feeds the variable the
  way the workflow will.
- **A scenario over the real artefacts**, catalogued in `research/module_tests.md`: the built binary
  with real environment, issuance, listing, revocation, and a revoked key refused. The unit and HTTP
  suites can all pass while the deployed binary, the SecretStorage wiring and the webview fail
  together.

#### What story 4's plan round changed (round 1 of 1, verdict `good_enough`)

All three reviewers answered, 12 findings: **11 accepted, 1 rejected**. The round paid for itself
twice over, and the biggest change is one nobody had asked for.

**The root helper is gone.** The plan proposed a second root-owned script on the host —
`coai-bugs-admin-check`, installed by hand with its own sudoers line — so the post-deploy check could
read the key out of `/etc/coai-bugs/env` rather than putting one in a runner. FOUR findings, from all
three reviewers, said the same thing about it: a manual install that somebody forgets makes the check
silently never run, for ever, which defeats the only purpose it has. They were right, and the
objection I had raised against the simpler design was weaker than I had stated: the runner ALREADY
holds the list, because delivering it is the step above. So the check is now a verb on the forced
command that reads one key on stdin — no new root helper, no manual step, and it always runs.

**And the empty-list contradiction.** The plan said an absent variable is legitimate AND that the
workflow refuses when it is unset. Both are kept, because they are different layers, and the plan now
says which is which: the SERVER may be run with no administrators by anybody; this REPOSITORY'S
deployment exists to carry them, so an empty secret box here is a mistake rather than a configuration.

The rest of the accepted findings are the details that make the two ends agree: `IFS= read` under
`set -e` aborting on a missing second line (so the helper reads a bounded blob and `sed -n` splits
it); the first usable key having to be EXTRACTED from the decoded list with the same comment and
blank-line rule `AdminKeys.Lines` uses; the curl credential going through a 0600 file a trap removes
rather than an argument; a bounded retry because the unit was restarted moments earlier; the refusal
message naming carriage returns, which a Windows editor adds invisibly; and `.agents/PROJECT.md`
already documenting the variable as "one key per line" — a correction, not a registration.

**The one rejection, verified rather than argued.** codex asked for an atomic rollout of binary and
environment together, because an old binary meeting a base64 value reads it as one administrator and
a new binary meeting a raw one exits 78. Real in general, impossible here: `git cat-file -e
bugs-v0.2.0:src_bugs/src/AdminKeys.cs` fails — the released and deployed binary has NO admin surface
and reads no `COAI_BUGS_ADMIN_KEYS` at all, so there is no code for the first half to happen in. Nor
can it arise later: the admin surface has never shipped, so every released binary that reads this
variable will carry the base64 rule from its first release. The remaining direction is loud and safe —
a new binary meeting a raw value exits 78, the canary refuses the deploy and restores 0.2.0, which
ignores the variable. The cheap half of the fix was taken anyway: the refusal names the variable and
the command, the README says the secret must be base64 before the first deploy carrying the admin
surface, and the check that always runs is what makes a wrong value loud instead of silent.

#### And one decision the round did not raise

**A failed admin check does NOT roll back.** The rollback step's condition was `failure()` after a
successful deploy, which would now retreat a perfectly good build because a KEY was wrong — and the
previous release has no admin surface at all, so the retreat fixes nothing and takes a working server
out of service. The condition names the edge check specifically, which is the one thing a rollback
actually repairs.

#### What story 4's code round changed (round 1 of 2, verdict `revise`)

All twelve reviewers answered, 36 findings: **29 accepted, 7 rejected**. One of them found a hole the
plan had claimed was closed, and it is the most valuable finding of the whole story.

**THE THREE RULES DID NOT MAKE THE SHAPES DISJOINT.** The plan argued that a raw key list could not
survive "no whitespace, strict UTF-8, no control characters". codex produced one that does: `aCE0`
repeated sixteen times is a plausible 64-character key, has no whitespace, is valid base64, and
decodes to `h!4` repeated sixteen times — printable ASCII, perfectly good UTF-8. It would have been
configured as an administrator nobody holds. **And the deployment's own check would have said 200**,
because the workflow decoded the same value and would have sent the same nonsense key: a check that
agrees with the defect. The answer could not be a better check, so the encoded form now carries a
MARKER — `# coai-bugs-admin-keys v1`, spelled as a comment so that every existing reader of the list
already drops it — and a raw list cannot have one. It costs the operator one line in the command they
paste, and it is free to introduce today for a reason that will not come again: the admin surface has
never shipped, so there is no deployed value to migrate. A byte-order mark is refused by name for the
same family of reasons: invisible, and it would silently become part of the first key.

**The extraction moved to the host.** The workflow decoded the list and picked the first usable key
itself, which put a second implementation of "which line is a key" in a runner, to drift from the
server's. `admin-check` now takes the whole encoded blob and does it on the host — one rule, one
place, and the credential still never crosses the edge.

**The delivery is two RECORDS, not two lines that happen to arrive.** Four findings, from all three
providers, about the same thing: `sed -n 2p` cannot tell an EMPTY second line from a MISSING one.
Empty means "no administrators", which is legitimate; missing means a caller this script does not
understand — an old workflow, a truncated transfer, a person running `secret` by hand — and it would
have silently removed every administrator and printed success. `read` tells them apart. A THIRD line
is refused too, because that is what a base64 value pasted without `-w0` arrives as: only its first
chunk would survive, decoding to a prefix of the list where the first key works and later
administrators silently do not — and the check, which tests the first key, would pass. The workflow
refuses whitespace in the secret before sending it, which is where an operator can act on it.

Smaller, and all accepted: the key's characters are checked before it is written into a curl config
file, because curl gives meaning to quotes and backslash escapes inside a quoted value; the trap is
armed before `mktemp` rather than after; `head -n 1` rather than the obsolescent `head -1`; an ssh
`ConnectTimeout` and a transport exit code kept apart from the answer, so "the host could not be
asked" and "the server said 401" are two sentences; retry progress on stderr, so forty seconds of
waiting looks like waiting; the rollback condition reads `!= 'success'` rather than `== 'failure'`,
because a step cancelled by a job timeout has neither; the promise scan gained the companion
assertion the repository's own rule requires (that the pattern still MATCHES a known instance, or it
is matching nothing anywhere) and became conditional on the column rather than asserting it; and
`AnAbsentVariableConfiguresNobody` was pointed back at `Read(null)`, which is the path its name
promises — a mechanical rewrite had quietly aimed it at the text parser.

**Seven rejections.** Two reviewers reported that `sed -n '1p'` causes a SIGPIPE failure under
`pipefail` and should be replaced with `head -n 1` — which is backwards: `sed -n '1p'` reads its
input to the end, which is exactly why it was chosen, and `head -n 1` is what closes the pipe early.
One reported that `pipefail` was not set in a step whose first line is `set -euo pipefail`. Two said
the admin check could hang indefinitely, where `--max-time 3` bounds every attempt and ten attempts
bound the whole thing at forty seconds. One asked for `Configured` to be renamed to `Result`, where
mirroring `RatePerMinute.Parsed` is the point. One objected that `admin-check` calls a real endpoint
rather than a dedicated canary — but a canary that can pass while the real surface fails is the
failure this story is about, and this repository already settled that argument once, in
`TheEdgeIsWatchedTests`: the request itself is the evidence. And one reported that `.agents/PROJECT.md`
still describes the raw format, in a paragraph that reads, in the finding's own words, *"the diff
fixes it"*.

#### And what round 2 changed (round 2 of 2, verdict `good_enough`)

All three reviewers answered, 9 findings: **5 accepted, 4 rejected**. Two of the accepted ones are
the same defect the first round had already taught: **two parsers, one rule, and nothing watching
them.**

- **The host extraction disagreed with the server.** `AdminKeys.Lines` trims a line and THEN asks
  whether it starts with `#`, so an indented `  # alice` is a comment; the shell filtered before
  trimming, so it became the "key" — and the character check refused it, failing a deployment over a
  list the server was perfectly happy with. A key written with a trailing space failed the same way.
- **And nothing would have caught that.** Every other test hands the server an environment built in
  C#, so the shell could have said anything. Both findings are answered by the same change: the rule
  moved into `deploy/bugs/first-key.sh`, ONE file, and `TheDeliveryAgreesWithTheServerTests` runs
  that file through `sh` over a table of lists an operator plausibly writes, asserting that whatever
  it picks is a credential the server admits. Putting the old filter order back turns five of its
  cases red.
- **The two-record framing now reads the VALUE, not only the status.** `read` returns non-zero at EOF
  — which is how an absent record is told from an empty one — but it also returns non-zero for a
  record that arrived without a trailing newline, while setting the variable. Taking the status alone
  refused a complete delivery in one direction and, worse, silently DISCARDED an unterminated third
  record in the other: exactly the wrapped-base64 truncation the third-record check exists to catch.

**What has no automated test, said here rather than left to be discovered:** `install-env.sh`'s
framing. It writes a `root:coai-bugs 0640` file under `/etc`, so driving it means running the suite
as root or giving the script an override for its destination — and a root-owned writer taking its
path from the environment is a privilege escalation in a checkout the deploy account can write. It
was exercised by hand over six shapes of stdin with a harness built from the script itself, and every
one behaved: both records, an empty administrator record, an absent one, an unterminated one, three
records, an unterminated third.

**Four rejections.** One said `read` needs `|| ADMINS=''` or an empty second line would be mistaken
for a missing one — the opposite of what `read` does, and the probe shows an empty line returning
zero and an absent one returning non-zero, which is the whole basis of the framing. One said the
extraction could fail silently when a list holds no key, where `|| true` and an explicit refusal are
both there and the message is *"the key list holds no key: every line is blank or a comment"*. One
asked whether a CRLF marker line would be refused — it is not, because the first line is trimmed, and
that is now pinned by a test rather than left to a reader. And one reported the environment variable
being read twice in `Program.cs`, in a paragraph that notes the second read was replaced.

### Story 5 — the extension can actually send *(Opus)*

> **Added 2026-09-17, by operator decision, sequenced AFTER story 4.** Not part of the original
> plan: found while answering "where do I put the key even if I had one?" — a question with a worse
> answer than expected.

**The symptom.** There is **no send path in the extension at all.** Every `bugz*.ts` file was
searched: the only matches for "send" or "upload" are the English words inside prose comments. The
panel Collects and Reviews — the review panel writes its decisions straight to the local database —
and then stops. Uploading is a `coai-mcp --upload-pairs --server … ` invocation with `COAI_BUGS_KEY`
in the environment, which is a CLI operation a person does by hand.

**And the setting that looks like it works does not.** `state.server`, set through "Set the ingest
server…", is read in exactly **one** place — `bugzView.ts:137`, to render its own button's label.
Nothing else consumes it. It is a control that stores a value nothing uses, which is the same failure
class as a test asserting a fragment: it looks right, so nobody checks.

**Why this is sequenced after story 4 and not before story 3.** The operator's call. It is worth
recording the argument that lost: a Users tab managing keys for a server the extension cannot send
to is a tab for a workflow that does not exist end to end. The argument that won is that the plan was
approved as four stories and reordering it mid-flight costs more than the wait.

**Scope, when it comes:** a Send action in the Bugz section that invokes the real one-shot; the
contributor key in `SecretStorage` (never `settings.json`, which syncs) with the same three states
story 3 defines for the admin key; the stored server address actually read; and the acknowledgement
handled — `--upload-pairs` marks nothing as sent until the server answers, so the panel must not
either. The page RUN by its test, per `.agents/PROJECT.md`.

#### What story 5's plan round changed (round 1 of 1, verdict `good_enough`)

All three reviewers answered, 15 findings: **13 accepted, 2 rejected**. They converged, from three
directions, on the question the plan had asked them:

**A SEND HAD NO DURABLE STATE, AND NOTHING STOPPED TWO.** The plan proposed deriving the button from
the funnel. It cannot be derived from the funnel: a pair is marked sent only on the server's
acknowledgement — the rule that makes a killed upload safe to retry — so for the whole of a
multi-minute run the counts say exactly what they said before it began. A panel reading them shows an
idle button, a reload shows an idle button, and a second Send starts a second process against the
same waiting pairs. So `coai-mcp` now opens an `upload_runs` row before the first request, beats it
per batch and ends it in a `finally`; `--bugs-json` carries it as `lastSend`, and the button reads
THAT. It is the collector's own shape, down to the heartbeat rather than a pid — the data directory
can be a NAS, where a pid belongs to another machine.

The rest, all accepted: the preflight must run BEFORE the child is built (a stored key and an
`http://` address would otherwise put a credential into a process pointed at an address it must not
cross); `serverRun` is not widened with a generic extra-environment parameter but joined by a NAMED
`uploadRun`, so "what can hand a credential to a child" is one grep with one result; the outcome
matrix gained exit 64 (an installed server older than sending), a malformed summary, and a run that
claims nothing rather than reporting zero; and the summary consumer needed a check against the real
producer rather than against a fixture.

**That last one paid for itself the first time it ran.** `bugzLiveContract.test.ts` spawns the REAL
`--upload-pairs` and parses what it actually prints — and the reader failed, because the server writes
its summary with indentation on: the real output is six lines and the last one is `}`. Every unit test
here had passed, because every fixture was written on one line. Both halves agreed with each other and
disagreed with the binary, which is the exact failure that style of test exists for.

**Two rejections, both verified rather than argued.** Two reviewers said a partial send risks
duplicating data. It cannot: `POST /ingest` is idempotent on the DERIVED id — `Corpus.cs` is
`ON CONFLICT(entry_id) DO NOTHING`, `entry_id` is `PairId.Of(payload)`, a pure function the client
computes identically, and `PLAN_a_corpus_of_real_defects.md` pins it twice. The resume mechanism they
asked for exists too: nothing is written unless the whole answer lines up, and an unacknowledged pair
is left waiting. A retry costs a redundant request, never a duplicate row. What was true is that the
person could not SEE any of that, and the sentences now say it.

## Test plan

> **Corrected 2026-09-17.** Four lines here specified behaviour that story 1 and the plan round had
> already replaced, and one of them was dangerous: it told a reader the month update is conditional.
> That condition was REMOVED because the counter rewrites the same row on every ingest, so it saved
> nothing — and adding it back to the now-combined `UPDATE` would gate the counter too and stop
> same-month ingests being counted at all. A stale test plan is how a removed defect gets
> reintroduced by somebody following instructions.

**Story 1, shipped.** Migration from a **step-1 file** whose SQL is frozen against the released
commit. `last_seen_month` matches `^\d{4}-(0[1-9]|1[0-2])$` — bounded to real months, not two digits
— asserted on the value READ BACK, with a frozen clock either side of a UTC month boundary. The
month advances **only on an accepted ingest**: not on a 401, a 429, a malformed body or an admin
call, each a test. The counter and the month are **one unconditional `UPDATE`**, and a test asserts
three ingests count three. The limit at the boundary, over it, after the window, at `0`, and at the
validated maximum. A **revoked key is refused**, before and after, and again inside the transaction
when the revoke lands between the gate and the write. N threads released by a barrier against a
limit of L admit exactly L. No table carrying a `key_id` carries a clock. A spent `IngestScope`
refuses.

**Story 2.** Admin comparison constant-time **with respect to the presented credential** — no early
return, so timing cannot reveal which configured key matched; the number of configured admins is not
a secret and is not claimed to be hidden. The admin API over real HTTP: every route, every error
code, `limit=0` refused, a `before` past the end answering an empty page rather than an error,
keyset paging stable across an insert between two page requests, `total` present for keys and absent
for the audit. **No response but the single issuance one contains a key or a hash**, asserted in both
directions. A lost issuance is recoverable from the newest-first listing alone. An absent variable
and a wrong credential are indistinguishable from outside. `/admin/active` reflects the limiter,
persists nothing, and answers the same shape when empty. The two limits are independent settings.
Mutation and audit roll back together; the audit sweep keeps the newest rows across the real 50 000
bound. The admin routes exercised by the scenario harness over the REAL built binary.

**Stories 3–5.** The Users tab RUN rather than read as text, revoke driven through the rendered
markup, the key-entry flow in all three states, and the tab saying what it cannot show. The promise's
remaining wording pinned by a test. The deploy delivering both admin settings and asserting an
authenticated `/admin/keys` afterwards. The send path's own tests, per story 5.

## Definition of done

**Story 1 — done, `bugs-v0.2.0` tagged 2026-09-17.**

- [x] `user_version` migrations incl. `admin_audit` + its sweep, tested from a step-1 file whose SQL
      is frozen against the released commit.
- [x] Rate limit **per key only**, configurable, validated maximum, `0` disables, 429 carries
      `Retry-After`; unauthenticated traffic is left to nginx and the plan says why.
- [x] Ingest + counter + `last_seen_month` in one transaction, the counter atomic in SQL, the two
      columns in ONE unconditional `UPDATE`.
- [x] Revoked keys refused, with a test that proves revoking stops an ingest — and refused again
      inside the transaction when the revoke lands after the gate.
- [x] No table carrying a `key_id` carries a clock time, asserted over the schema.
- [x] One server per data directory, enforced rather than assumed.

**Story 2 — code complete, deviations recorded above; not yet tagged.**

- [x] Admin keys: array + `FixedTimeEquals`, no early return (pinned structurally, teeth proved by
      adding one), derived ids, absent-variable log — a WARNING rather than Information, and read
      by a test.
- [x] Admin API: the shapes in *"Story 2's contract as the plan round RESOLVED it"* — **not the route
      table above it, which is superseded** — keyset paging, `total` for keys only, notes validated,
      one transaction per mutation+audit, only the issuance response carrying a key.
- [x] `/admin/active`, from the limiter, persisting nothing, keys only, shape invariant — reading
      BOTH limiters, and its empty case asserted one layer down where it can exist.
- [x] `COAI_BUGS_ADMIN_RATE_PER_MINUTE` as its own setting, default 120, validated and capped, and
      an illegal value names the ADMIN variable rather than the contributor one.
- [x] An admin uploads through `AcceptAdmin` — now actually WIRED, through an `Uploader` union at
      the gate — with no counter and no month, and the reason the in-force re-check does not apply
      stated in the code and asserted by `Accept` refusing the id `AcceptAdmin` stores under.
- [x] Admin routes in the scenario harness, over the real binary, catalogued in `module_tests.md`:
      issue, list, audit, active, revoke, the revoked key really refused, an out-of-range admin
      limit exiting 78, and the variable absent.

**Story 3 — SHIPPED 2026-09-17 (PR #363). Story 4 — in this branch. Story 5 — not started.**

- [x] Users tab: key-entry command and states, copy-before-dismiss, confirmed revoke, failures shown,
      page tested by RUNNING it, and it says what it cannot show — keys, and admin credentials.
- [x] The three remaining "a counter without a clock" remarks rewritten, with a test pinning the
      phrase against a column that has one — `ThePromiseMatchesTheSchemaTests`, conditional on the
      column so that removing it makes the old sentence true again.
- [x] That an admin credential dies only on a successful redeploy is written in the deploy notes and
      shown in the tab.
- [x] Both admin settings in `.agents/PROJECT.md` — they were already there, and the entry for
      `COAI_BUGS_ADMIN_KEYS` was CORRECTED rather than added: it said "one key per line", which the
      base64 rule makes wrong. The deploy delivers them and verifies an authenticated admin call.
- [ ] Story 5: the extension can SEND, and the stored ingest-server address is read by something
      other than a button label.

## What this deliberately does NOT do

No day/month histogram · no identity · no self-service sign-up · no bulk deletion from the tab · no
idempotency token on issuance (revoke-and-reissue is the recovery) · **no application-level limit on
unauthenticated traffic**, which is nginx's and cannot be done correctly here.

## Settled, so nobody reopens them

- **The month, not the date.** Asked and answered; see above.
- **The corpus does NOT gain `key_id`.** The third count — pairs promoted into the corpus — needs
  permanent attribution on the one table meant to outlive everything else here, and quarantine's
  attribution is deliberately temporary: a row is reviewed and deleted within days. **The tab shows
  TWO counts**, sent and waiting. If it is ever wanted, it is `corpus.key_id`, `Promote` carrying it
  transactionally, and a fifth story — not a migration somebody adds quietly.
