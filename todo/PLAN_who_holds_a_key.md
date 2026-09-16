# PLAN — who holds a key, and what they have sent

> Status: **plan only, nothing implemented yet.** Scope: `src_bugs` (schema, rate limit, admin API),
> `src_vs_code` (the Bugz section's Users tab), and the privacy promise in
> `research/module_server.md` and `deploy/bugs/README.md`, which this change **deliberately
> rewrites**.
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

**There is no production database yet** — `coai-bugs` is not deployed and `bugs-v0.1.0` is not cut.
No backfill exists or is needed. The migration discipline is still required: the first release must
not be the one that makes an unversioned `ALTER` normal.

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
| `admin_audit` | one row per administrative action | ~150 B/row | **swept to the newest 50 000 rows** after each write that crosses the mark |

50 000 rows is ~7 MB and decades of hand-driven administration; the sweep is four lines and exists
from the first write rather than as a CLI mode nobody will ask for.

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
