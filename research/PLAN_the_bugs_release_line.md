# PLAN — the `coai-bugs` release line, and the half of its promise that is not code

> Status: **IMPLEMENTED, 2026-09-16.** Built as one unit and reviewed through the gate; the plan
> round's findings are folded in below where they changed the answer.
>
> **Released `bugs-v0.1.0` 2026-09-16 and DEPLOYED 2026-09-17.** `https://bugs.remsoft.dev/health`
> answers, `/ingest` refuses an unauthenticated request with 401, and the no-client-address promise
> has been checked against real internet traffic rather than a test. The first deployment found four
> defects no review round could have — they are the last section of this document, and what they have
> in common is worth more than any one of them.
>
> **The boundary with [PLAN_the_corpus_tail.md](../todo/PLAN_the_corpus_tail.md)**, named there as well as
> here: this plan builds the release line, the vhost and the deploy notes. The tail plan keeps the
> ranking pass's transport, the `Math.random()` nonce, and the AUTOMATED post-deploy log check —
> which cannot be written until there is a deployment to run it against.
>
> Related docs: [module_server.md](module_server.md),
> [PLAN_a_corpus_of_real_defects.md](PLAN_a_corpus_of_real_defects.md).

## The symptom

`coai-bugs` shipped with story 6. It builds, 39 tests drive it including two real processes over a
real socket, and it ships **nowhere**: the release workflow has three tag shapes and none is this
binary's.

## Why the deploy notes had to come first

`coai-bugs` accepts anonymous code skeletons from anybody holding a key. The key carries no identity
— no name, no address, no `last_seen_utc` — and `submissions` is a counter without a clock,
deliberately, because with timestamps it is a record of when a person we handed a key to was
working. The route reads no client address and registers no request-logging middleware.

**None of that proves anything.** nginx writes `$remote_addr` before the request reaches any route,
so every line above can be correct while the edge keeps a dated list of everyone who contributed.
Three story-6 plan reviewers said an in-process test cannot reach this. Releasing the binary without
the config that keeps the promise would be shipping the promise without the thing that keeps it.

## What shipped

### `deploy/nginx/coai-bugs`

Three rules, and the third is the one the plan round changed:

1. **`access_log off`** — off, not a format with the address removed. A format is one edit away from
   carrying it again and nothing fails when it does.
2. **The forwarding headers are CLEARED, not omitted.** `proxy_set_header X-Real-IP "";` and its
   four siblings. "Must not set them" is weaker than it sounds: a location that picks up a distro
   `proxy_params` or an inherited include acquires them without anybody choosing to, and an empty
   value means nginx sends no such header whatever else in the config tried. (Plan round, codex.)
3. **`error_log /dev/null crit`, and NOT `warn`, which is what this plan first said.** `limit_req`
   writes `client: <address>` to the ERROR log at error level, so a vhost with `access_log off` and
   `error_log warn` still records exactly the contributors it throttled — and the verification
   recipe, which sent one *successful* pair, would never have produced one. A plan reviewer caught
   it before the first deployment. **The cost is stated rather than hidden:** this vhost has no
   error diagnostics. Keeping a log "just for errors" means keeping the addresses of everyone the
   server was rude to, which is not a better set of people to have a list of.

What legitimately holds an address: `limit_req_zone` keys on `$binary_remote_addr`, so nginx holds
it in shared memory for the window. Never written to disk, never reaches the application. A rate
limit that does not know who is calling is not a rate limit; a log is a record.

### The server watches its own edge

A reviewer asked for a mode that reads the nginx file and checks it. **That is the wrong
instrument** and the finding was accepted in a different shape: this process cannot know where that
file is, `include` and templating mean the file on disk is not the effective config, and a check
that passes on a file nginx never loaded is worse than no check at all.

**The request is the evidence.** Any of `X-Forwarded-For`, `X-Real-IP`, `Forwarded`,
`CF-Connecting-IP` or `True-Client-IP` arriving means the edge sends one, whatever any file says —
and `coai-bugs` logs a warning naming **the header, never the value**, because a warning quoting the
address would be the leak it exists to report.

It also **refuses to start on an empty keyword list**. `/health` answers from a route that touches
neither the list nor the database, so a build accident could otherwise start, satisfy the release
smoke, publish, and then refuse every submission as if the contributor were at fault.

### `.github/workflows/release.yml`

A `bugs-v*` line, **two Linux RIDs rather than six**: `coai-mcp` runs on whatever machine a person
develops on and `coai-server` is handed to other people; this one is deployed by us to a Linux host.
It reuses `draft-release.sh` and `verify-and-publish-release.sh` unchanged — the second already
takes its RIDs as a parameter for exactly this reason.

The smoke is why the line is worth having:

- **`--waiting` is the `e_sqlite3` check.** It opens the database through `Corpus.Open`, a P/Invoke
  into SQLite — the call that threw `DllNotFoundException` when `mcp-v0.18.1` shipped its executable
  alone. A reviewer pointed out that `/health` touches no database and would pass with the library
  missing. Both steps are there because they check different things.
- **`/health` is the keyword-list check**, and nothing short of running the published binary can be.
- **An unknown mode must exit 64**, asserted here too.
- **The architecture is asserted on every leg**, runnable or not: a name-presence check on
  `e_sqlite3` passes just as happily for a library of the wrong architecture. (Plan round, codex.)
- **The whole process group is killed on every exit**, including cancellation — a child holding the
  port is the next leg's problem and reads as an unrelated flake. (Plan round, codex.)

The archive carries the binary, `e_sqlite3`, the deploy notes and the vhost. An operator holding the
binary without them has a server recording every contributor.

## What this deliberately did NOT do — and what has happened since

- ~~**It does not deploy anything.** There is no `bugs.remsoft.dev` and no DNS record~~ — **it does
  now.** `bugs-v0.1.0` was released on 2026-09-16 and deployed on 2026-09-17; the file became the
  record it said the first machine would make it.
- The post-deploy log check was a recipe for a person, and it has been **run**: a request from the
  public internet left no address anywhere. The only IPv4 in the service journal is `127.0.0.1`,
  there were zero `the edge sent` warnings — so both halves of the boundary agree, in production and
  not only in a test — and the shared nginx access log holds no mention of `bugs.remsoft.dev` and no
  `/health` or `/ingest` line. Automating it stays in the tail plan; what changed is that it is no
  longer unrunnable.
- `KeyFor` still scans every active key row — rejected three times now on scale rather than
  principle: hand-issued keys, tens of rows.

## What the FIRST DEPLOYMENT changed, which no review round could have

Three defects, and they share something the review rounds could not reach: each one was a
difference between what the deployment *did* and what every document about it *said*. A reviewer
reads the intent; only the host knows.

- **The deploy account could not authenticate at all.** `BUGS_DEPLOY_KEY` held a passphrase, which
  an unattended deploy has nobody to type. It arrived as `Permission denied (publickey,password)` —
  one message for five causes, none of them named. `deploy-bugs.yml` now prints the fingerprint the
  run is offering and separates a passphrase from a damaged paste, because telling them apart
  otherwise needs root on the host.
- **The notes named a `systemctl` the host does not have.** sudo resolves through `secure_path` to
  `/usr/bin/systemctl`; a rule naming `/bin/systemctl` authorises nothing. And `is-active --quiet`
  needs its own entry, because sudoers matches arguments exactly and the canary passes `--quiet`.
  The live host only worked because somebody had corrected both by hand.
- **The copy undid the setgid on the line after it was granted.** `cp -a "$found/." "$RELEASE/"`
  applies the source directory's mode and group to the destination, so the service reached its
  binary through the WORLD bits — the exact thing the setgid bit had been added to prevent. Measured
  on the host: `mkdir` under the 2750 parent gives `coai-bugs 2755`, and after the copy the same
  directory reads `root 755`.
- **And the unit was `disabled`** — installed, deployed, serving, and gone after the next reboot.
  Nothing reports this state: `systemctl is-active` says `active` either way, and so does the
  canary, which asks whether the service answers rather than whether it will exist tomorrow.

## What the review round changed after this was written

Six real defects, and the theme is worth keeping: **everything here runs once, on the day it
matters.** A release line, a deploy wrapper and a rollback have no ordinary path — nothing exercises
them until the thing they exist for is happening, and by then the cost of being wrong is the
outage. Three of the six were cases of exactly that, and the fix in each case was to give the logic
somewhere it runs on every push.

- **The deploy account could not deploy.** The notes gave `releases` to the service account and the
  deploy account sudo for `systemctl` alone. Under that, `release.sh` could not take its lock in
  `/opt/coai-bugs`, create the release directory or swap the `bin` symlink, and the `secret` verb —
  which runs *before* anything is installed — could not write `/etc` at all. The release area now
  belongs to `coai-bugs-deploy` with the service's group for traversal; `data` deliberately does
  not, so the account that delivers releases cannot read the corpus.
- **The one root operation is a separate, installed file.** `deploy/bugs/install-env.sh` goes to
  `/usr/local/sbin` and is named in sudoers with a trailing `""` so it takes no arguments. It is the
  one file here that does *not* update itself with the checkout, and for the opposite of the usual
  reason: the deploy account owns the checkout, and a root script in a directory its caller can
  write is a way to become root.
- **A rollback that ran twice.** `if: failure()` fired for either failure, and the deploy failing is
  the case where the host has *already* rolled back. The second `--rollback` retreated past the
  release that was serving, or stopped the service outright — a canary doing its job ending as an
  outage.
- **A rollback that reported success while the service was down.** `switch_to … || say …` returns
  the status of `say`.
- **An archive check satisfied by debug symbols.** `grep "$NAME/[^/]*coai-bugs"` matches
  `coai-bugs.dbg`. The check whose entire job is to prove the executable is in the archive passed on
  an archive without it. It is `.github/scripts/archive-carries.sh` now, matching basenames as
  globs, and `TheArchiveCheckTests` runs the real script against archives built to be wrong.
- **Two tests with no teeth**, both on the promise this plan is about. `proxy_set_header X-Real-IP`
  is a substring of `proxy_set_header X-Real-IP $remote_addr;`, so the header test would have passed
  on a vhost forwarding every contributor's address; the condition is a function now, held against
  both spellings, because the file under test is not one to weaken on disk to watch an assertion
  fire. The keyword-override test asserted two fragments the *embedded* list also contains.

## Definition of done

- [x] `bugs-v*` is a trigger, and the two RIDs are named in both the matrix and the verifier.
- [x] The archive carries the binary, `e_sqlite3`, the deploy notes and the vhost, asserted against
      the ARCHIVE rather than the publish directory.
- [x] The smoke starts the published binary, waits for `/health`, touches the database, checks the
      architecture and cleans up its process group.
- [x] The vhost clears the forwarding headers and writes no log that can hold an address.
- [x] The server warns when its edge sends one, and refuses to start without a keyword list.
- [x] The deploy notes carry the rules, the systemd guard on the secret's file mode, and a
      verification recipe covering the REFUSED path as well as the successful one.
- [x] The boundary with the tail plan is written into both documents.
