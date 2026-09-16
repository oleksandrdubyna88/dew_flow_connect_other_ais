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
bugs.remsoft.dev.    A    <this host's IPv4>
```

Add an `AAAA` too **only if the host really answers on IPv6**, and if you do, read *Verifying the
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

### The environment file

**Every command below reads the secret from this file.** Nothing ever puts it on a command line:
`sudo COAI_BUGS_SECRET=… …` places it in `sudo`'s argv, where `ps` and `/proc/<pid>/cmdline` show it
to any local user and the auth log records it in plaintext.

```bash
install -d -m 0750 -o root -g coai-bugs /etc/coai-bugs
umask 077
cat > /etc/coai-bugs/env <<'ENV'
COAI_BUGS_SECRET=<32+ random bytes; openssl rand -base64 32>
COAI_BUGS_DATA=/opt/coai-bugs/data
ENV
chown root:coai-bugs /etc/coai-bugs/env
chmod 0640 /etc/coai-bugs/env
```

`COAI_BUGS_DATA` lives **in the environment file, not in the unit**, so the daemon and every
maintenance command below read the same value and therefore the same database. Setting it in one
place and not the other is how keys get issued into a file the server never opens, and every upload
is then rejected with a 401 nobody can explain.

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
# never secret. This reports the real reason instead of leaving a 0644 file working quietly.
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

```bash
useradd --system --home /opt/coai-bugs --shell /usr/sbin/nologin coai-bugs
install -d -m 0755 -o coai-bugs -g coai-bugs /opt/coai-bugs/bin /opt/coai-bugs/data

# from a bugs-v* release: the archive carries the binary, e_sqlite3, DEPLOY.md and the vhost
curl -fsSLO https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/releases/download/bugs-v<version>/coai-bugs-<version>-linux-x64.tar.gz
curl -fsSLO https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/releases/download/bugs-v<version>/coai-bugs-<version>-linux-x64.tar.gz.sha256
sha256sum -c coai-bugs-<version>-linux-x64.tar.gz.sha256
tar xzf coai-bugs-<version>-linux-x64.tar.gz
install -m 0755 -o coai-bugs -g coai-bugs coai-bugs-<version>-linux-x64/coai-bugs /opt/coai-bugs/bin/
install -m 0644 -o coai-bugs -g coai-bugs coai-bugs-<version>-linux-x64/*e_sqlite3* /opt/coai-bugs/bin/

systemctl daemon-reload && systemctl enable --now coai-bugs
systemctl status coai-bugs --no-pager
```

`e_sqlite3` must sit **beside** the binary. Native AOT compiles managed code; the P/Invoke into
SQLite still resolves at run time through the OS loader, which searches the executable's directory.
This server reads its key table on every request, so without it every upload answers 401 and the log
says nothing about why. `mcp-v0.18.1` shipped an executable alone and learned this the expensive way.

---

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
