# Deploying `coai-bugs` — the corpus ingest server

The one public thing in this repository. Anybody with a key may send anonymous before/after code
skeletons to it; it knows nothing about who they are, and keeping it that way is **half application
and half this document**.

> **Deployed.** `bugs-v0.1.0` was released on 2026-09-16 and installed on the host on 2026-09-17,
> so this file is the record of a machine now, the way `deploy/README.md` is for the Team server.
> The database that binary created — `/opt/coai-bugs/data/coai-bugs.db`, its tables present and
> `user_version = 0` — is the shape every later release migrates forward; step 1 of the schema is
> frozen against it, and the first start of the next release stamps it and adds what is new.

Same shape as the Team server, deliberately: **Native AOT under systemd, no container**, host nginx
as the edge, host certbot for TLS. `deploy/README.md` explains why that host runs binaries rather
than images, and every reason applies here.

---

## What it needs, and what it does not

| | |
|---|---|
| **Database** | **None to provision.** SQLite, one file, created on first start under `$COAI_BUGS_DATA`. |
| Postgres / SQL Server | **Not used.** Those belong to the vault stack on this box; no coai service touches them. |
| Container runtime | Not used. |
| Shared network | Not used — nginx reaches it over loopback. |
| Memory | Small. It parses skeletons and writes rows; it runs no vendor CLI and holds no model. |

---

## What this server promises, and which half of it is code

The promise is **scoped**, and it was rewritten on 2026-09-17 when the operator, offered the
narrowest thing that answers "is this key alive", took the month:

> **About a contributor**, the server records only the **month a key was last used** and how much it
> has sent. It records no date, no clock time, no address, no name, and no history: there is no way
> to ask which day a contributor worked, or at what hour, or what a key did in any month but its
> last.
>
> **Nothing that carries a key id carries a clock.** A pair awaiting review records the MONTH it
> arrived, not the instant — the one column that broke this promise was found by a review round and
> changed, rather than the promise being reworded around it. A test reads the schema and refuses any
> table that carries both a key id and a clock time.
>
> **About administrators**, the server records exact times: when a key was issued, when it was
> revoked, and who did it. That is a log about the people holding administrative power, not about
> the people contributing.
>
> **A comment is the one field a person TYPES, and it is public by their decision** (2026-09-18).
> Everything else a pair carries was derived from somebody's repository and anonymised before it
> left their machine; a comment is written into a box that says, beside it, that it leaves the
> machine unanonymised. So the server stores it verbatim, scans nothing in it, scrubs nothing in
> it — and refuses, naming what it refused, rather than silently changing what somebody wrote.

| Promise | Kept by |
|---|---|
| A key carries no identity — no name, no address | **Code.** The schema has no column for either. |
| A comment is the contributor's own words: stored verbatim, never scanned, never scrubbed | **Code.** The alphabet whitelist runs over the two skeletons only; `CommentRule` refuses a comment or takes it whole, and never edits one. A test sends a word that is refused in a skeleton and accepted in a comment. |
| A comment never reaches a server too old to store it | **Code.** It travels on `POST /ingest/commented`, which a binary older than `bugs-v0.3.0` answers 404 — so the refusal is structural rather than a matter of deployment order. One test runs the previous release, pinned by tag and SHA-256, to prove it. |
| **A comment is never dropped in silence, by any server** | **Code.** `POST /ingest` REFUSES a pair that carries one, naming the path that keeps it, and writes nothing — so the batch can simply be resent. A new server quietly discarding the field would be the same failure an old one commits, in the place nobody is watching for it. |
| A comment attaches to a pair that was already waiting without one | **Code.** The first non-empty comment wins and nobody can overwrite it; a contributor whose words were not kept is TOLD, never reported a silent success. |
| A comment is never written to the server's log | **Code.** A refusal names a length or a code point, never content. |
| Of WHEN a key was used, only the calendar month (`last_seen_month`, `yyyy-MM`, UTC) — never a day or an hour | **Code.** The column is typed as a month; nothing can write a finer value into it, and it moves only on an accepted ingest. |
| `submissions` is a lifetime count | **Code.** It is not a rate limit; the limit is per key, in memory, and below. |
| `admin_audit` names administrators, actions and key ids — never a note, a key or a hash | **Code.** A test pins what its two text columns may hold. |
| The route reads no client address | **Code.** `HttpRequest` is taken for its headers and nothing else. |
| No request-logging middleware is registered | **Code.** `UseSerilogRequestLogging` is deliberately not wired. |
| **No client address is recorded anywhere** | **THIS DOCUMENT.** See *The edge*. |

The last row is the one that matters and the one no test in this repository can reach. A reverse
proxy writes `$remote_addr` **before** the request reaches any route, so every line of code above
can be correct while nginx keeps a dated list of everyone who contributed.

---

## The edge — in this order, and check each step

### 1. DNS first

```bash
dig +short bugs.remsoft.dev @1.1.1.1
curl -fsS https://api.ipify.org; echo
```

The two must match. **One A record is the whole prerequisite** — certbot uses HTTP-01, so it needs
the name to resolve to this box and port 80 reachable. No TXT, no CNAME, no wildcard:

```
bugs.remsoft.dev.    A    82.165.44.219
```

**Done on 2026-09-16** — `bugs.remsoft.dev` resolves to 82.165.44.219 on both 1.1.1.1 and
8.8.8.8, and the name has **no AAAA**, so this host is IPv4-only for the purpose below.

Add an `AAAA` only if the host really answers on IPv6, and if you do, read *Verifying the
promise* below with that in mind: the checks there look for v6 addresses as well as v4 precisely
because a v6-only client leaks through a v4-shaped grep.

An A record that has not propagated fails HTTP-01 with an error about the *challenge*, not about
DNS, and that is a confusing half hour.

### 2. The site file — HTTP only, at first

```bash
cp deploy/nginx/coai-bugs /etc/nginx/sites-available/coai-bugs
ln -sfn /etc/nginx/sites-available/coai-bugs /etc/nginx/sites-enabled/coai-bugs
nginx -t && systemctl reload nginx
curl -fsS -H 'Host: bugs.remsoft.dev' http://127.0.0.1/health
```

Port 80 only, deliberately, for the same reason `deploy/nginx/coai` is: `certbot --nginx` runs
`nginx -t` **before** it issues, so a file naming a certificate that does not exist yet makes the
config invalid, certbot aborts, and nginx will not reload for **any** site on the box.

**Rollback:** `rm /etc/nginx/sites-enabled/coai-bugs && nginx -t && systemctl reload nginx`.

### 3. The certificate

```bash
certbot --nginx -d bugs.remsoft.dev
nginx -t && systemctl reload nginx          # certbot usually reloads; do it anyway and read the output
curl -fsS https://bugs.remsoft.dev/health
```

After this the installed file is **no longer** what this repository holds: certbot's plugin edits it
in place, adding `listen 443 ssl`, the certificate paths and a 301 from `:80`. Re-copying this
repository's version over it later would remove the TLS listener.

### The three rules this vhost exists for

1. **`access_log off`** — off, not a format with the address removed. A format is one edit away from
   carrying it again and nothing fails when it does.
2. **The forwarding headers are CLEARED, not merely unset** — `proxy_set_header X-Real-IP "";` and
   its four siblings. "Must not set them" is weaker than it sounds: a location that picks up a distro
   `proxy_params` or an inherited include acquires them without anybody choosing to.
3. **`error_log /dev/null crit;`** — and **not `warn`**, which is what these notes first said.
   `limit_req` writes `client: <address>` at **error** level, so a vhost with `access_log off` and
   `error_log warn` still keeps a dated list of exactly the contributors it throttled. The cost is
   stated rather than hidden: this vhost has **no error diagnostics**. Debug a 502 from the service's
   own journal, or point this at a file temporarily and delete what it records afterwards.

**What legitimately holds an address:** `limit_req_zone` keys on `$binary_remote_addr`, so nginx
holds it in shared memory for the length of the window. Never written to disk, never reaches the
application. A rate limit that does not know who is calling is not a rate limit; a log is a record.

---

## The service

Native AOT, one binary, no runtime to install. **Configuration is environment variables only** —
never arguments, because an argument is in process listings and shell history.

| Variable | Meaning |
|---|---|
| `COAI_BUGS_SECRET` | **Required.** The HMAC secret keys are hashed with. Without it the server exits **78** rather than hashing with no secret. |
| `COAI_BUGS_DATA` | Where `coai-bugs.db` and `logs/` live. |
| `COAI_BUGS_KEYWORDS` | Optional. A file that **replaces** the embedded keyword list, for correcting it without waiting for a release. |
| `COAI_BUGS_RATE_PER_MINUTE` | Optional. Requests a minute **per key**, a sliding window; `10` when unset, `0` switches the limit off, anything past `1000` or not a whole number makes the server exit **78** rather than start with a clamped value. A refused request is a `429` with `Retry-After`. Per key and never per address: this vhost clears every forwarding header, so the application sees `127.0.0.1` for everybody and an address limit would be one bucket for the whole internet — unauthenticated traffic is `limit_req`'s job above. In memory, one process, reset by a restart, by design. |
| `COAI_BUGS_ADMIN_KEYS` | Optional. The administrators who may use `/admin/*`, as **base64 of the key list** — one line, no whitespace, which is what `base64 -w0` produces, and the list itself must begin with **`# coai-bugs-admin-keys v1`**. INSIDE it, one key per line; blank lines and `#` comments are ignored, so keep a comment line saying whose each key is — the marker is a comment too, which is why nothing else had to learn about it. Anything else is **refused with 78**, never guessed at: a raw 64-character key can be valid base64 that decodes to ordinary text (`aCE0` sixteen times decodes to `h!4` sixteen times), so without the marker a server could start with administrators nobody holds — and the deploy's own check would pass, because it would send the same nonsense key. Each is hashed with `COAI_BUGS_SECRET` at startup and only the hash is held. **Absent is a legitimate configuration** — the server starts, keeps collecting, and answers `401` to every admin call — so it does NOT exit 78; it logs a warning naming this variable, and that log is the only place the difference is visible (see *[An admin credential cannot be revoked in a hurry](#an-admin-credential-cannot-be-revoked-in-a-hurry)*). An admin key may also upload through `/ingest`; those pairs are attributed to `admin-<8 hex>` and counted against no key row. |
| `COAI_BUGS_ADMIN_RATE_PER_MINUTE` | Optional. Requests a minute **per administrator**; `120` when unset, `0` switches it off, past `1000` or not a whole number makes the server exit **78**. Its own setting on purpose: the contributor number is flood control for a public endpoint, and using it here rate-limits the Users tab after ten pages. |
| `COAI_LOG_LEVEL` | `Information` by default. |

## All secrets live in Actions Secrets

**Nothing below is typed on the host by hand.** Operator decision, and it is the right one: a
secret that exists only in a file on one machine cannot be rotated without somebody opening a
terminal, and nobody can tell you when it was last changed. Every one of these lives in this
repository's secret store, and `deploy the ingest server` delivers them.

| Secret | What it is | How to produce it |
|---|---|---|
| `COAI_BUGS_SECRET` | The HMAC secret keys are hashed with. | `openssl rand -base64 32` |
| `COAI_BUGS_ADMIN_KEYS` | The administrators, **base64**, marked. | `printf '# coai-bugs-admin-keys v1\n# alice\n<alice-key>\n' \| base64 -w0` |
| `BUGS_DEPLOY_HOST` | The host. | `82.165.44.219` |
| `BUGS_DEPLOY_USER` | The account the forced-command key belongs to. | `coai-bugs-deploy` |
| `BUGS_DEPLOY_KEY` | The PRIVATE half of the deploy key. | `ssh-keygen -t ed25519 -f coai-bugs-deploy-ci -C coai-bugs-deploy-ci -N ""` |
| `BUGS_DEPLOY_KNOWN_HOSTS` | The host’s public key, pinned. | `ssh-keyscan -t ed25519 82.165.44.219` |

**Changing `COAI_BUGS_SECRET` invalidates every issued key**, because the stored hashes were
computed with the old one. That is a deliberate property — it is the one lever that ends every
key at once — but it is not a rotation you do casually. It invalidates every **admin** key too, for
the same reason: those lines are hashed with it as well.

### An admin credential cannot be revoked in a hurry

> ⚠️ **Removing a line from `COAI_BUGS_ADMIN_KEYS` does nothing until a SUCCESSFUL redeploy — and a
> deploy that fails or rolls back leaves the credential you just revoked still live.**

A contributor key dies the moment `--revoke` commits, in the running server, because the server
re-reads `api_keys` on every request. Administrators are deliberately not rows in that table: the set
is read from the environment once at startup and is immutable for the process's lifetime. That is
what makes an administrator's own upload safe without a second in-force check — there is no window
between the gate and the write for the set to change in — and this is the other side of the same
coin. An in-memory kill switch was considered and rejected: it is state a restart silently discards,
which is the opposite failure and easier to forget.

**So the emergency measure is stopping the service**, not editing a variable:

```bash
sudo systemctl stop coai-bugs          # the admin surface is gone; so is ingest
# edit COAI_BUGS_ADMIN_KEYS in Actions Secrets, redeploy, and CONFIRM the deploy succeeded
```

Confirm it took: an authenticated `GET /admin/keys` with the removed key must answer `401`. A
`200` means the old binary is still running — check `systemctl status coai-bugs` for a rollback.

**The deploy delivers this variable**, from Actions Secrets, on every run — the same path as
`COAI_BUGS_SECRET` and in the same step, two lines on stdin. It did not, until story 4 of
[PLAN_who_holds_a_key.md](../../research/PLAN_who_holds_a_key.md), and the failure was quiet in
exactly the way that matters: a release started perfectly, served `/health`, and answered `401` to
every admin call with nothing in CI noticing.

**The value travels base64-encoded** — operator decision, 2026-09-17. A newline-separated value
cannot live in an `EnvironmentFile` assignment, and a custom separator would put a second parsing
rule into a credential boundary. So the whole variable is one base64 blob in the secret box, on the
wire and in the file, and the server decodes it. Nothing in between encodes or re-encodes it:

```bash
# what you paste into Actions Secrets, and what /etc/coai-bugs/env then holds
printf '# coai-bugs-admin-keys v1\n# alice\n<alice-key>\n# bob\n<bob-key>\n' | base64 -w0
```

**The first line is not optional.** `# coai-bugs-admin-keys v1` is what tells an encoded list from a
raw one, and without it the two are not distinguishable: a 64-character key is inside the base64
alphabet, so `aCE0` repeated sixteen times decodes to `h!4` repeated sixteen times — printable text
that would have been accepted as an administrator nobody holds. It is spelled as a comment, so every
reader of the list already ignores it.

**`-w0` is not decoration either.** `base64` without it wraps at 76 columns; a wrapped value is
refused by the workflow before it is sent, and by the installer if it arrives anyway, because only
its first chunk would survive — which decodes to a PREFIX of the list, where the first key works and
later administrators silently do not. A trailing carriage return, which a Windows editor adds
invisibly, is refused the same way. Every refusal names the variable and prints the command.

**And the deploy now proves it worked.** After the edge check, the workflow sends the whole encoded
list to the host's forced command as `admin-check`; the HOST decodes it, takes the first usable key,
makes one authenticated `GET /admin/keys?limit=1` on loopback and prints only the status code.
Anything but `200` fails the run. The extraction is on the host rather than in the runner so that
one implementation decides which line is a key — a copy in the workflow would drift from the
server's, and a check that tests a key the server does not hold reports a failure nobody can find.
That implementation is `deploy/bugs/first-key.sh`, and `TheDeliveryAgreesWithTheServerTests` runs it
against the server's own parser so the two cannot part company quietly. It deliberately does NOT roll back: a wrong key is a configuration failure and
the previous release has no admin surface at all, so retreating would take a good build out of
service to fix nothing. Fix the secret and deploy again.

### Why the secret travels on stdin

The workflow pipes it to the forced command, which writes `/etc/coai-bugs/env` itself. It is
never an argument: `sudo COAI_BUGS_SECRET=… …` puts it in argv, where `ps` and
`/proc/<pid>/cmdline` show it to any local user and the auth log records it in plaintext. The
wrapper prints nothing back, because a wrapper that confirmed the value would put it in a CI log.

It writes BOTH values into one file:

```
COAI_BUGS_SECRET=<from Actions Secrets>
COAI_BUGS_DATA=/opt/coai-bugs/data
```

`COAI_BUGS_DATA` lives here rather than in the unit so the daemon and every maintenance command
read the same value and therefore the same database. Setting it in one place and not the other is
how keys get issued into a file the server never opens, and every upload is then rejected with a
401 nobody can explain.

### The deploy key is an update button, not root

On the host, in `~coai-bugs-deploy/.ssh/authorized_keys`:

```
restrict,command="/opt/coai-bugs/src/deploy/bugs/deploy-cmd.sh" ssh-ed25519 AAAA... coai-bugs-deploy-ci
```

`restrict` turns off the pty, forwarding and everything else; the forced command means the key
can run that one file whatever the client asks for. It answers exactly five things —
`deploy <version>`, `deploy --rollback`, `secret`, `admin-check` and `health` — and refuses anything
else. A leaked key can update or roll back this one service and can do nothing whatsoever besides.

**Its own key, its own user, its own wrapper.** It shares nothing with the Team server’s deploy
key: this is a fully independent deployment, so a mistake in one cannot reach the other.

### One-time, on the host

**Who owns what, and why it is not all one account.** Two accounts touch this deployment and they
want opposite things. `coai-bugs` RUNS the service and owns its data; it must never be able to
replace the binary it executes. `coai-bugs-deploy` INSTALLS releases and owns the release area; it
must never be able to read the corpus or the secret. So the release directories belong to the
deploy account and the data directory does not, and the one file that needs root is reached through
a helper rather than by making the deploy account root.

The first version of these notes had `releases` owned by `coai-bugs` and gave the deploy account
sudo for nothing but `systemctl`. Under it the first deploy could not get as far as failing
usefully: `release.sh` could not take its lock in `/opt/coai-bugs`, could not create the release
directory, could not create the `bin` symlink, and the `secret` verb — which runs BEFORE anything is
installed — could not write `/etc` at all. (CodeRabbit, #328.)

```bash
useradd --system --home /opt/coai-bugs --shell /usr/sbin/nologin coai-bugs
useradd --create-home --shell /bin/sh coai-bugs-deploy

# The release area belongs to the DEPLOY account: it creates `releases/<version>-<stamp>`, swaps
# the `bin` symlink in the parent, writes `releases/.trail` and takes `.release.lock` there. The
# group is `coai-bugs` so the service can traverse in and execute what it finds; 0750 keeps
# everybody else out.
install -d -m 0750 -o coai-bugs-deploy -g coai-bugs /opt/coai-bugs

# SETGID on `releases`, and it is not decoration. `release.sh` runs as `coai-bugs-deploy`, so a
# release directory it creates would otherwise carry that account's OWN group — and the service,
# which is in `coai-bugs`, would be reading the binary through the world bits or not at all. The
# setgid bit makes every release directory inherit `coai-bugs`, so group access is what the service
# actually uses and `releases` can stay closed to everybody else.
#
# SETGID IS THE ONLY MECHANISM AVAILABLE HERE, and that is worth knowing before anybody "improves"
# it. `coai-bugs-deploy` is deliberately NOT a member of `coai-bugs` — that is what stops the
# account which delivers releases from reading the corpus — and on Linux a non-root user may only
# `chgrp` a file to a group they belong to. So the deploy account cannot set this group by hand at
# all; the kernel has to grant it, which is what this bit does.
#
# `bugs-v0.2.0` was rejected by its own canary for exactly this. An earlier `release.sh` tried
# `chgrp -R --reference` after unpacking, followed by `chmod 0750`. The `chgrp` was refused and the
# `chmod` was not, so the release landed `0750 coai-bugs-deploy:coai-bugs-deploy`: wrong group AND no
# world bits, so the service could not read its own binary and never started. The canary caught it
# and the host rolled back. `release.sh` therefore copies the archive ENTRY BY ENTRY rather than with
# `cp -a "$found/."`, because the trailing `/.` applies the archive directory's mode and group to the
# release directory and there is then no way to undo it from this account.
install -d -m 2750 -o coai-bugs-deploy -g coai-bugs /opt/coai-bugs/releases

# READING THE JOURNAL, so the canary's diagnostic is not blind. `release.sh` prints
# `journalctl -u coai-bugs -n 30` when the unit dies during the canary, and as an ordinary account it
# got `-- No entries --` with a hint about needing `adm` or `systemd-journal` — so the one moment the
# logs matter most produced nothing. `systemd-journal` is read-only and grants no access to
# `/opt/coai-bugs/data`, so it does not weaken the split above.
usermod -aG systemd-journal coai-bugs-deploy

# The DATA belongs to the service, and the deploy account is deliberately not in reach of it: it
# delivers releases, it does not get to read the corpus.
install -d -m 0750 -o coai-bugs -g coai-bugs /opt/coai-bugs/data

# the checkout the wrapper and the release script are read FROM
git clone --depth 1 -b main https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais.git \
  /opt/coai-bugs/src
chown -R coai-bugs-deploy:coai-bugs-deploy /opt/coai-bugs/src

# The one thing that needs root: writing /etc/coai-bugs/env as root:coai-bugs 0640. It is INSTALLED
# to /usr/local/sbin rather than run from the checkout, which the deploy account can write — a root
# script in a directory its caller owns is a way to become root. Reinstall it if it ever changes;
# it is the only file here that does not update itself with the checkout.
install -m 0755 -o root -g root /opt/coai-bugs/src/deploy/bugs/install-env.sh \
  /usr/local/sbin/coai-bugs-install-env

# The deploy account may restart this one unit, and run that one helper with NO arguments. The
# trailing "" is what says "no arguments": a sudoers command with no argument spec permits any.
#
# `/usr/bin/systemctl`, NOT `/bin/systemctl`. sudo matches the command by the path it resolves
# through `secure_path`, and that is `/usr/bin/systemctl`; `/bin` is a symlink to `/usr/bin` on a
# usrmerge system but sudo compares the path it was given, so a rule naming `/bin/systemctl`
# matches nothing and every `systemctl` call in `release.sh` is refused. The first host to run
# these notes had the `/usr/bin` spelling because it was corrected by hand, and the notes still
# carried the one that does not work.
#
# `is-active --quiet` IS A SEPARATE ENTRY, and must be. sudoers matches arguments exactly, and
# `release.sh`'s canary asks `$SYSTEMCTL is-active --quiet "$SERVICE"` — an entry for
# `systemctl is-active coai-bugs` does not authorise it. Without this line the canary's
# "did the unit stop during the canary" check is refused by sudo on every deploy.
cat > /etc/sudoers.d/coai-bugs-deploy <<'SUDO'
coai-bugs-deploy ALL=(root) NOPASSWD: /usr/bin/systemctl restart coai-bugs, /usr/bin/systemctl stop coai-bugs, /usr/bin/systemctl is-active coai-bugs, /usr/bin/systemctl is-active --quiet coai-bugs
coai-bugs-deploy ALL=(root) NOPASSWD: /usr/local/sbin/coai-bugs-install-env ""
SUDO
chmod 0440 /etc/sudoers.d/coai-bugs-deploy
visudo -cf /etc/sudoers.d/coai-bugs-deploy   # a bad drop-in locks sudo for everybody

# Read back what the account may ACTUALLY do, rather than what the file says it may do. This is
# the check that would have caught both corrections above on the day they were written.
sudo -u coai-bugs-deploy sudo -n -l

mkdir -p ~coai-bugs-deploy/.ssh && chmod 700 ~coai-bugs-deploy/.ssh
# paste the authorized_keys line above, with the PUBLIC half of BUGS_DEPLOY_KEY
chown -R coai-bugs-deploy:coai-bugs-deploy ~coai-bugs-deploy/.ssh
```

Then install the unit below — and **enable** it, which is a separate thing from installing it:

```bash
systemctl daemon-reload
systemctl enable coai-bugs    # NOT --now: there is no binary to start until the first deploy
```

`enable` is what writes the `multi-user.target.wants` symlink the unit's `[Install]` section asks
for, and without it the service runs until the machine reboots and then does not come back. The
first host to run these notes was left exactly like that: installed, deployed, serving, `disabled`.
Nothing reports it — `systemctl is-active` says `active` either way, and so does the deploy's own
canary. `systemctl is-enabled` is the only thing that answers the question.

Then run **Actions → deploy the ingest server** with the version.
The first run writes the environment file before it installs anything, so the service has its
secret the first time it starts.

### The root helper is the one thing a deploy does not update — and it now says so

`/usr/local/sbin/coai-bugs-install-env` is a **copy**. Every other script here is read out of
`/opt/coai-bugs/src`, so a fix to it reaches the host with the next deploy; this one cannot be,
because it runs as root and that checkout is writable by the deploy account. A root script in a
directory its caller owns is a way to become root, so the copy stays a copy.

**What that cost, on 2026-09-22.** The helper gained its second record — the administrator list —
in `c38637e3`. The host's copy predated it by ten hours, and the older helper's entire reading of
stdin was `head -1`: it took the secret, **discarded the key list without a word**, wrote an
environment file holding no administrators, printed success and exited 0. So the deploy's delivery
step was green while it delivered nobody; the server started perfectly, because an absent
`COAI_BUGS_ADMIN_KEYS` legitimately means "no administrators"; and every `/admin` call answered 401.
`admin-check` caught it — a whole deploy later, which is the right net at the wrong distance.

**So the two ends now declare a protocol.** `install-env.sh` states which one it implements,
`deploy/bugs/helper-protocol.sh` demands it, and `deploy-cmd.sh` runs that check **before** the
secret is sent and before anything is written. A helper that is behind fails the deploy immediately,
naming the one command that repairs it. The check never repairs anything itself — a script that
reinstalled a root helper would hand the deploy account exactly the escalation the copy prevents.

```bash
# as root, when a deploy refuses with "does not speak coai-bugs-install-env protocol N"
install -m 0755 -o root -g root \
  /opt/coai-bugs/src/deploy/bugs/install-env.sh /usr/local/sbin/coai-bugs-install-env
```

The marker is spelled once, in `install-env.sh`, and the check demands that **whole line** — not the
text inside it, because `protocol 2` is a substring of `protocol 20` and a containment check would
wave through the very helper it exists to stop. `TheHostsHelperIsTheOneThisDeployNeedsTests` runs the
real check against the shipped helper, against the pre-administrator body that was actually on the
host, and against four helpers that are not this protocol — so the two ends cannot drift apart
without a red test on somebody's machine rather than a 401 on a live one.

**Bump the number whenever what arrives on stdin changes meaning**, and reinstall. `1` was one
record, the secret; `2` is two, the secret then the base64 administrator list.

### The unit

```ini
# /etc/systemd/system/coai-bugs.service
[Unit]
Description=ConnectOtherAIs corpus ingest
After=network-online.target

[Service]
Type=simple
ExecStart=/opt/coai-bugs/bin/coai-bugs --urls http://127.0.0.1:8110
WorkingDirectory=/opt/coai-bugs
# A leading dash so systemd does not fail before ExecStartPre can say something useful about WHY.
EnvironmentFile=-/etc/coai-bugs/env
# The secret must not be world-readable, and a service that starts anyway is a secret that was
# never secret. The deploy wrapper writes this file 0640 root:coai-bugs; this is what notices if
# anything ever changes that.
ExecStartPre=/bin/sh -c '[ -r /etc/coai-bugs/env ] || { echo "/etc/coai-bugs/env is missing or unreadable"; exit 1; }'
ExecStartPre=/bin/sh -c '[ "$(stat -c %%a /etc/coai-bugs/env)" = "640" ] || { echo "/etc/coai-bugs/env must be 0640, is $(stat -c %%a /etc/coai-bugs/env)"; exit 1; }'
Restart=always
RestartSec=5
# 78 is EX_CONFIG: a missing secret or an unusable keyword list. Those do not get better by trying
# again, and `Restart=always` would otherwise restart the same broken binary every five seconds for
# ever while the operator reads a crash loop instead of one clear line.
RestartPreventExitStatus=78
User=coai-bugs
Group=coai-bugs
# Defence in depth for the corpus, not the boundary. Everything this process CREATES — the database
# on a fresh host, and the `-wal` and `-shm` files WAL mode keeps beside it — is born without the
# world-readable bit. The directory's 0750 (`install -d` above) is the PRIMARY boundary and stays so;
# this is what still holds if that one is ever loosened by hand.
UMask=0027

[Install]
WantedBy=multi-user.target
```

**Loopback only.** `--urls http://127.0.0.1:8110` — nginx is the only thing that may reach it, and a
server bound to `0.0.0.0` behind a firewall is one rule away from being reachable without the vhost
that keeps the promise above.

**`UMask=0027` is defence in depth, and the directory is the boundary.** The first deployment
created `coai-bugs.db` as `0644` — readable by anyone who can reach the directory — and the
directory's `0750` was the only thing keeping the corpus private. The mask is what we ASK for; what
it achieves is read back, never assumed: after the first start under this unit,
`stat -c %a /opt/coai-bugs/data/coai-bugs.db /opt/coai-bugs/data/coai-bugs.db-wal` should read
`640`.

**What the mask does NOT do: fix the files that already exist.** A file the earlier unit created
keeps its old mode, and — this was wrong here until a review caught it — **the `-wal` and `-shm`
companions do not inherit anything from the database.** They are separate files with their own modes,
created when SQLite opens the database, so a `chmod` on the `.db` alone leaves the sidecars
world-readable and the `.db` is not the only file holding corpus rows. Stop the service first, so
nothing recreates them mid-change:

```bash
systemctl stop coai-bugs
chmod 0640 /opt/coai-bugs/data/coai-bugs.db*      # the db AND its -wal / -shm
systemctl start coai-bugs
stat -c '%n %U:%G %a' /opt/coai-bugs/data/coai-bugs.db*
```

Record that `stat` output here when it has been read. Every line should end `640`.

### Installing it

**Ordinarily: Actions → deploy the ingest server → Run workflow**, and type the version. That
path refuses a version whose release is incomplete before an approval is spent, delivers the
secret, verifies the archive’s checksum on the host, and hands the rest to
`deploy/bugs/release.sh` — which keeps an immutable release directory per version, swaps `bin`
with ONE rename, keeps the last three, and rolls itself back when its canary says no.

The canary is the two things that have actually broken a build here: `--waiting` opens the
database through the SQLite P/Invoke (the call that threw when `mcp-v0.18.1` shipped without
`e_sqlite3`, and this server reads its key table on EVERY request), and `/health` answers only
after the embedded keyword list has parsed.

| | |
|---|---|
| `release.sh 0.1.0` | build from the checkout, inspect, switch, canary, self-roll-back |
| `release.sh --from <archive> 0.1.0` | the same, from an artefact CI already built |
| `release.sh --rollback` | pop one deployment off the trail. No build. Seconds. |
| `release.sh --list` | what is live and what is retained |

`e_sqlite3` must sit **beside** the binary, and `release.sh` refuses a release without it.
Native AOT compiles managed code; the P/Invoke into SQLite still resolves at run time through
the OS loader, which searches the executable’s directory. `mcp-v0.18.1` shipped an executable
alone and learned this the expensive way.

## Issuing and ending keys

Every command loads the environment file rather than naming the secret:

```bash
run() { sudo -u coai-bugs env $(grep -v '^#' /etc/coai-bugs/env | xargs) /opt/coai-bugs/bin/coai-bugs "$@"; }

run --issue-key --note "the tuesday workshop"
run --revoke --id <id>
run --waiting --limit 20
run --promote --entry <id>
```

The key is printed **once**, on stdout, and only its hash is stored — a stolen database must not
become a set of working keys. If somebody loses theirs they get a new one; there is nothing here to
recover. `--note` is *our* record of why a key exists, never a name.

A revoked key and a key that never existed answer identically, so the endpoint cannot be used to
discover which keys are real.

## Quarantine, and why nothing is automatic

Everything accepted lands in `quarantine`, not in the corpus. The alphabet check stops leaked
identifiers; it cannot tell a real skeleton from a crafted one, and a corpus shown to people as
precedent is worth poisoning. `--waiting` shows what arrived and the total; `--promote` moves one
pair across, printing what it moved.

There is no timer and no automatic promotion. Past `Corpus.MostWaiting` (20 000) the server refuses
new pairs and says so — a queue somebody must work through, rather than a disk that fills.

---

## Verifying the promise after the first real ingest

This is the step that closes the gap, and it is the only one that can.

**Both paths, not just the happy one.** A plan reviewer caught the version of this recipe that sent
one successful pair: `limit_req` writes the client address to the ERROR log, so the case that leaks
is the case that gets **refused**, and a single accepted upload would never have produced it.

```bash
# 1. The path that succeeds.
COAI_BUGS_KEY=... coai-mcp --upload-pairs --server https://bugs.remsoft.dev

# 2. The paths that FAIL: an unknown key, and enough requests to trip the rate limit.
for i in $(seq 1 40); do
  curl -s -o /dev/null -H 'Authorization: Bearer not-a-real-key' \
    -H 'Content-Type: application/json' -d '{"items":[]}' \
    https://bugs.remsoft.dev/ingest
done

# 3. Look for ANY address, v4 or v6. Expect nothing from all four.
#    The v6 alternation matters: a v6-only client leaks straight through a v4-shaped grep, and
#    nothing else in this recipe would notice.
ADDR='([0-9]{1,3}[.]){3}[0-9]{1,3}|([0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}'
sudo grep -REn "$ADDR" /var/log/nginx/ | head
sudo grep -REn "$ADDR" /opt/coai-bugs/data/logs/ | head
sudo journalctl -u coai-bugs --since '10 min ago' | grep -E "$ADDR"
sudo journalctl -u nginx     --since '10 min ago' | grep -E "$ADDR"

# 4. And ask the SERVER whether its edge is behaving. This line appearing means the vhost is
#    wrong, and it names the header rather than the address:
sudo journalctl -u coai-bugs --since '10 min ago' | grep 'the edge sent'

# 5. The per-KEY limit, with a real key: past COAI_BUGS_RATE_PER_MINUTE inside a minute the answer
#    is 429 with a Retry-After header and a body naming the limit. An unknown key never reaches it
#    (step 2 is 401s, throttled by nginx), which is the whole point of limiting by key.
for i in $(seq 1 12); do
  curl -s -o /dev/null -w '%{http_code} retry-after=%header{retry-after}\n' \
    -H "Authorization: Bearer $COAI_BUGS_KEY" -H 'Content-Type: application/json' \
    -d '{"items":[]}' https://bugs.remsoft.dev/ingest
done

# 6. The file modes the unit's UMask asks for, read back rather than assumed.
sudo stat -c '%a %n' /opt/coai-bugs/data/coai-bugs.db /opt/coai-bugs/data/coai-bugs.db-wal

# 7. The admin surface. A release can start perfectly and answer 401 to every admin call — that is
#    what an unset COAI_BUGS_ADMIN_KEYS looks like from outside, and it looks identical to a wrong
#    key ON PURPOSE, so this pair of lines is how you tell which you have.
curl -s -o /dev/null -w 'no credential: %{http_code}\n' https://bugs.remsoft.dev/admin/keys
curl -s -w '\n' -H "Authorization: Bearer $COAI_BUGS_ADMIN_KEY" https://bugs.remsoft.dev/admin/keys
#    Expect 401 for the first and a JSON page for the second. A 401 for BOTH means either the
#    variable is unset or that key is not in it — and the SERVER's startup log is the only thing
#    that distinguishes them:
sudo journalctl -u coai-bugs --since '10 min ago' | grep -E 'administrators'

#    Who is sending right now, which no stored count can answer. It persists nothing.
curl -s -w '\n' -H "Authorization: Bearer $COAI_BUGS_ADMIN_KEY" https://bugs.remsoft.dev/admin/active
```

A hit in any of them is a defect in the deployment, not in the code, and this file is where it gets
fixed. **Record the date this check was run here when it is.**

## What the server checks for itself

It cannot read the nginx config — `include` and templating mean the file on disk is not the
effective configuration, and a check that passed on a file nginx never loaded would be worse than
none. So it watches the **requests**, which are the evidence: any of `X-Forwarded-For`, `X-Real-IP`,
`Forwarded`, `CF-Connecting-IP` or `True-Client-IP` arriving makes it log a warning naming the
header — never the address, because a warning quoting it would be the leak it exists to report.
Step 4 above is how you ask.

It also refuses to start on an empty keyword list, exiting **78**, because `/health` answers from a
route that touches neither the list nor the database: a build accident could otherwise start,
satisfy the release smoke, publish, and then refuse every submission as if the contributor were at
fault.

Four more refusals at startup, all **78**, since the limits became settings: a
`COAI_BUGS_RATE_PER_MINUTE` or a `COAI_BUGS_ADMIN_RATE_PER_MINUTE` that is not a whole number from 0
to 1000 (refused, never clamped — the message names the range and which variable it read), a
**second server on the same data directory**, and a line in `COAI_BUGS_ADMIN_KEYS` that is **also an
issued contributor key**.

That last one is worth knowing before you hit it. The two credential stores are checked in order, so
one string in both would upload as a contributor — counted against its key row, on the contributor
limit — and then become an administrator the moment that key was revoked. **Revoking it would grant
administration**, which is the opposite of what you pressed the button for. The server refuses to
start, names the contributor key id, and the fix is either to remove that line from the variable or
to revoke and reissue the contributor key. Do not "fix" it by reusing the key elsewhere: there is no
configuration in which one string should be both. The rate limit is
one process's memory, so two servers would each admit the whole limit; the first holds
`$COAI_BUGS_DATA/coai-bugs.serving` open exclusively for its lifetime and the second says so and
stops. The one-shots (`--issue-key`, `--revoke`, `--waiting`, `--promote`) never take that lock:
running one beside the service is the ordinary case, and the database's busy timeout is what makes
it wait for the server's write rather than fail.

## Backing up

One file, and it is the whole corpus:

```bash
sudo -u coai-bugs sqlite3 /opt/coai-bugs/data/coai-bugs.db ".backup '/opt/coai-bugs/data/backup.db'"
```

Take it with the service running — `.backup` is consistent against a live writer, which `cp` is not.
