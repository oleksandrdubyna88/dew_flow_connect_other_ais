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
> **About administrators**, the server records exact times: when a key was issued, when it was
> revoked, and who did it. That is a log about the people holding administrative power, not about
> the people contributing.

| Promise | Kept by |
|---|---|
| A key carries no identity — no name, no address | **Code.** The schema has no column for either. |
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
| `COAI_LOG_LEVEL` | `Information` by default. |

## All secrets live in Actions Secrets

**Nothing below is typed on the host by hand.** Operator decision, and it is the right one: a
secret that exists only in a file on one machine cannot be rotated without somebody opening a
terminal, and nobody can tell you when it was last changed. Every one of these lives in this
repository's secret store, and `deploy the ingest server` delivers them.

| Secret | What it is | How to produce it |
|---|---|---|
| `COAI_BUGS_SECRET` | The HMAC secret keys are hashed with. | `openssl rand -base64 32` |
| `BUGS_DEPLOY_HOST` | The host. | `82.165.44.219` |
| `BUGS_DEPLOY_USER` | The account the forced-command key belongs to. | `coai-bugs-deploy` |
| `BUGS_DEPLOY_KEY` | The PRIVATE half of the deploy key. | `ssh-keygen -t ed25519 -f coai-bugs-deploy-ci -C coai-bugs-deploy-ci -N ""` |
| `BUGS_DEPLOY_KNOWN_HOSTS` | The host’s public key, pinned. | `ssh-keyscan -t ed25519 82.165.44.219` |

**Changing `COAI_BUGS_SECRET` invalidates every issued key**, because the stored hashes were
computed with the old one. That is a deliberate property — it is the one lever that ends every
key at once — but it is not a rotation you do casually.

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
can run that one file whatever the client asks for. It answers exactly four things —
`deploy <version>`, `deploy --rollback`, `secret` and `health` — and refuses anything else. A
leaked key can update or roll back this one service and can do nothing whatsoever besides.

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
# The setgid bit alone is NOT enough, and the first deployment proved it: `release.sh` unpacks with
# `cp -a "$found/." "$RELEASE/"`, and that trailing `/.` applies the ARCHIVE directory's mode and
# group to the release directory, wiping the inherited group on the line after it was granted. The
# script puts it back explicitly — see the `chgrp --reference` in `release.sh`. Both halves are
# needed: this bit for the `dotnet publish` path, that line for the archive path.
install -d -m 2750 -o coai-bugs-deploy -g coai-bugs /opt/coai-bugs/releases

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
`640`. Two things it does not do on its own: a file the earlier unit already created keeps its old
mode until `chmod 0640 /opt/coai-bugs/data/coai-bugs.db` is run once, as `coai-bugs`; and the
`-wal`/`-shm` companions take the mode of the database they belong to, so that one `chmod` is what
makes them `640` too. Record the `stat` output here when it has been read.

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

Two more refusals at startup, both **78**, since the limit became a setting: a
`COAI_BUGS_RATE_PER_MINUTE` that is not a whole number from 0 to 1000 (refused, never clamped —
the message names the range), and a **second server on the same data directory**. The rate limit is
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
