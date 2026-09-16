# Deploying `coai-bugs` — the corpus ingest server

The one public thing in this repository. Anybody with a key may send anonymous before/after code
skeletons to it; it knows nothing about who they are, and keeping it that way is **half application
and half this document**.

> **Not deployed yet.** These are the notes the release was written against, not a record of a
> machine. When the first host runs it, this file becomes the record and says so — the way
> `deploy/README.md` does for the Team server.

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

| Promise | Kept by |
|---|---|
| A key carries no identity — no name, no address, no `last_seen_utc` | **Code.** The schema has no column for any of them. |
| `submissions` counts without a clock | **Code.** With timestamps it is a record of when a person was working. |
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
cat > /etc/sudoers.d/coai-bugs-deploy <<'SUDO'
coai-bugs-deploy ALL=(root) NOPASSWD: /bin/systemctl restart coai-bugs, /bin/systemctl stop coai-bugs, /bin/systemctl is-active coai-bugs
coai-bugs-deploy ALL=(root) NOPASSWD: /usr/local/sbin/coai-bugs-install-env ""
SUDO
chmod 0440 /etc/sudoers.d/coai-bugs-deploy
visudo -cf /etc/sudoers.d/coai-bugs-deploy   # a bad drop-in locks sudo for everybody

mkdir -p ~coai-bugs-deploy/.ssh && chmod 700 ~coai-bugs-deploy/.ssh
# paste the authorized_keys line above, with the PUBLIC half of BUGS_DEPLOY_KEY
chown -R coai-bugs-deploy:coai-bugs-deploy ~coai-bugs-deploy/.ssh
```

Then install the unit below, and run **Actions → deploy the ingest server** with the version.
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

[Install]
WantedBy=multi-user.target
```

**Loopback only.** `--urls http://127.0.0.1:8110` — nginx is the only thing that may reach it, and a
server bound to `0.0.0.0` behind a firewall is one rule away from being reachable without the vhost
that keeps the promise above.

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

## Backing up

One file, and it is the whole corpus:

```bash
sudo -u coai-bugs sqlite3 /opt/coai-bugs/data/coai-bugs.db ".backup '/opt/coai-bugs/data/backup.db'"
```

Take it with the service running — `.backup` is consistent against a live writer, which `cp` is not.
