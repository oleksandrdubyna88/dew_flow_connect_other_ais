# PLAN — who holds a key, and what they have sent

> Status: **in progress — story 1 (the schema, the audit, a limit that is a setting) shipped
> 2026-09-17 on `feat/a-limit-that-is-a-setting`; stories 2–4 not started.** Scope: `src_bugs`
> (schema, rate limit, admin API), `src_vs_code` (the Bugz section's Users tab), and the privacy
> promise in `research/module_server.md` and `deploy/bugs/README.md`, which this change
> **deliberately rewrites**. Story 1's deviations from the text below are recorded at the end of its
> section.
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

**The promise becomes this, in all four places, with no softening — and it is now SCOPED, because a
reviewer caught the second evasion:** the first version said "no clock time", while `admin_audit`,
`createdUtc` and `revokedUtc` all store exact times.

> **About a contributor**, the server records only the **month a key was last used** and how much it
> has sent. It records no date, no clock time, no address, no name, and no history: there is no way
> to ask which day a contributor worked, or at what hour, or what a key did in any month but its
> last.
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

## The four stories

> **Model policy**, per the gate's standing order: the split itself and stories **1** and **2** —
> schema migration, authentication, rate limiting — are done on **Fable (max)**, because being wrong
> there is expensive. Stories **3** and **4** are ordinary work on **Opus**. Each story is reviewed
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
- An admin id is derived: `admin-` + the first 8 hex of its hash. Deterministic, no table.
- An admin key **may also upload**; the derived id serves at the ingest boundary.
- **Admin credentials are not rows in `api_keys`** and therefore appear nowhere in the Users tab, have
  no counters and no `lastSeenMonth`. Accepted, and the tab **says so**: an admin credential is
  rotated by editing the secret and redeploying, not by revoking a row.
- Startup logs once at Information when the variable is absent, because an admin API with no admins
  answers 401 to everybody and looks broken.

Every response is a JSON object, never a bare array. `skip`/`limit` are non-negative integers —
anything else is **400**, not a silent clamp — `limit` defaults to 50 and is capped at 200. **Every
list is ordered by `created_utc, id`** so a row inserted between pages cannot duplicate or hide one.

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
- `note` is validated: it is the one field where somebody types a name out of habit, and an
  email-shaped note is refused as a cheap guard on the promise.

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

- The promise rewritten in four places, in the scoped wording above, **with a test** that the phrase
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

## Test plan

Migration from a **step-1 file**. `last_seen_month` matches `^\d{4}-\d{2}$` asserted on the
stored value, with a frozen clock either side of a UTC month boundary. The month advances **only on an
accepted ingest** — not on a 401, a 429, a malformed body or an admin call — each a test. The
conditional write does not rewrite a same-month row. The limit at the boundary, over it, after the
window, at `0`, and at the validated maximum. A **revoked key is refused**, before and after. Admin
comparison constant-time and independent of how many admins are configured. The admin API over real
HTTP: every route, every error code, ordering stable across an insert, and **no response but the
issuance one containing a key or a hash**. `/admin/active` reflects the limiter and survives no
restart. Mutation and audit roll back together. The audit sweep keeps the newest rows. The Users tab
RUN, revoke driven through the rendered markup, and the key-entry flow in all three states.

## Definition of done

- [ ] `user_version` migrations incl. `admin_audit` + its sweep, tested from a step-1 file.
- [ ] Rate limit **per key only**, configurable, validated maximum, `0` disables, 429 carries
      `Retry-After`; unauthenticated traffic is left to nginx and the plan says why.
- [ ] Ingest + counter + `last_seen_month` in one transaction; conditional update.
- [ ] Revoked keys refused, with a test that proves revoking stops an ingest.
- [ ] Admin keys: array + `FixedTimeEquals`, derived ids, absent-variable log, excluded from the tab.
- [ ] Admin API: shapes above, stable ordering, validated paging, notes validated, one transaction
      per mutation+audit, only the issuance response carrying a key.
- [ ] `/admin/active`, from the limiter, persisting nothing, keys only.
- [ ] Users tab: key-entry command and states, copy-before-dismiss, confirmed revoke, failures shown,
      page tested by RUNNING it.
- [ ] Promise rewritten in four places, scoped to contributors, with a test pinning it.
- [ ] Both variables in `.agents/PROJECT.md`; deploy delivers them and verifies an admin call.
- [ ] Scenario over the real artefacts, catalogued in `module_tests.md`.

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
