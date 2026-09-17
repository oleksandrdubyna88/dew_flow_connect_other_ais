# PLAN — who holds a key, and what they have sent

> Status: **in progress — story 1 (the schema, the audit, a limit that is a setting) shipped
> 2026-09-17 as `bugs-v0.2.0`; story 2 (admin keys and the admin API) is code complete on
> 2026-09-17 and not yet tagged; stories 3–5 not started.** Story 5 was added on 2026-09-17 and is
> not in the original four. Scope: `src_bugs`
> (schema, rate limit, admin API), `src_vs_code` (the Bugz section's Users tab), and the privacy
> promise in `research/module_server.md` and `deploy/bugs/README.md`, which this change
> **deliberately rewrites**. Each story's deviations from the text below are recorded at the end of
> its own section.
>
> Related docs: [module_server.md](../research/module_server.md),
> [PLAN_a_corpus_of_real_defects.md](../research/PLAN_a_corpus_of_real_defects.md),
> [PLAN_the_bugs_release_line.md](../research/PLAN_the_bugs_release_line.md),
> [module_extension.md](../research/module_extension.md),
> [module_tests.md](../research/module_tests.md).

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
- **A scenario over the real artefacts**, catalogued in `research/module_tests.md`: the built binary
  with real environment, issuance, listing, revocation, and a revoked key refused. The unit and HTTP
  suites can all pass while the deployed binary, the SecretStorage wiring and the webview fail
  together.

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

**Stories 3–5 — not started.**

- [ ] Users tab: key-entry command and states, copy-before-dismiss, confirmed revoke, failures shown,
      page tested by RUNNING it, and it says what it cannot show — keys, and admin credentials.
- [ ] The three remaining "a counter without a clock" remarks rewritten, with a test pinning the
      phrase against a column that has one.
- [ ] That an admin credential dies only on a successful redeploy is written in the deploy notes and
      shown in the tab.
- [ ] Both admin settings in `.agents/PROJECT.md`; the deploy delivers them and verifies an
      authenticated admin call.
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
