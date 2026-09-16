# Deploying `coai-bugs` — the corpus ingest server

The one public thing in this repository. Anybody with a key may send anonymous before/after code
skeletons to it; it knows nothing about who they are, and keeping it that way is **half application
and half this document**.

> **Not deployed yet.** These are the notes the release was written against, not a record of a
> machine. When the first host runs it, this file becomes the record and says so — the way
> `deploy/README.md` does for the Team server.

---

## What this server promises, and which half of it is code

| Promise | Kept by |
|---|---|
| A key carries no identity — no name, no address, no `last_seen_utc` | **Code.** The schema has no column for any of them. |
| `submissions` counts without a clock | **Code.** With timestamps it is a record of when a person was working. |
| The route reads no client address | **Code.** `HttpRequest` is taken for its headers and nothing else. |
| No request-logging middleware is registered | **Code.** `UseSerilogRequestLogging` is deliberately not wired. |
| **No client address is recorded anywhere** | **THIS DOCUMENT.** See below. |

The last row is the one that matters and the one no test in this repository can reach. A reverse
proxy writes `$remote_addr` to its access log **before** the request reaches any route, so every
line of code above can be correct while nginx keeps a dated list of everyone who contributed. Three
code-round reviewers made this point about an in-process test that asserted no middleware was
registered; they were right that it proved nothing about a deployment.

---

## The edge

`deploy/nginx/coai-bugs` is the vhost, and it carries three rules in its own comments:

1. **`access_log off`** — off, not a custom format with the address removed. A format is one edit
   away from carrying it again and nothing fails when it does.
2. **No `X-Real-IP`, no `X-Forwarded-For`.** The Team server's vhost sets both because it partitions
   a rate limit on the real address. This one must not: a header set here is a header the
   application could log, and the application is built not to have it to log.
3. **`error_log … warn`** — `info` and below record addresses.

**What legitimately holds an address:** `limit_req_zone` keys on `$binary_remote_addr`, so nginx
keeps it in shared memory for the length of the window. It is never written to disk and never
reaches the application. A rate limit that does not know who is calling is not a rate limit; a log is
a record. That distinction is the design, not a loophole in it.

Install it exactly as the Team server's is (`deploy/README.md`, *The edge*): copy to
`/etc/nginx/sites-available/coai-bugs`, symlink into `sites-enabled`, `nginx -t`, reload, then
`certbot --nginx -d bugs.remsoft.dev`, which edits the file in place to add the TLS listener and the
301 from :80.

---

## The service

Native AOT, one binary, no runtime to install. Configuration is environment variables only — never
arguments, because an argument is in process listings and shell history.

| Variable | Meaning |
|---|---|
| `COAI_BUGS_SECRET` | **Required.** The HMAC secret keys are hashed with. Without it the server exits **78** rather than hashing with no secret. |
| `COAI_BUGS_DATA` | Where `coai-bugs.db` and `logs/` live. Defaults to the working directory. |
| `COAI_BUGS_KEYWORDS` | Optional. A file that REPLACES the embedded keyword list, for correcting it without waiting for a release. |
| `COAI_LOG_LEVEL` | `Information` by default. |

```ini
# /etc/systemd/system/coai-bugs.service
[Unit]
Description=ConnectOtherAIs corpus ingest
After=network-online.target

[Service]
Type=simple
ExecStart=/opt/coai-bugs/coai-bugs --urls http://127.0.0.1:8110
WorkingDirectory=/opt/coai-bugs
# The secret is a file root alone can read, never a line in this unit: `systemctl show` prints
# Environment= to anybody who can ask, and the unit file is world-readable by default.
#   install -d -m 0750 -o root -g coai-bugs /etc/coai-bugs
#   install -m 0640 -o root -g coai-bugs /dev/null /etc/coai-bugs/env
EnvironmentFile=/etc/coai-bugs/env
# Refuse to start on a secret file anybody can read. Without it the service starts perfectly on
# a 0644 env file and the only symptom is that the secret was never secret. (Plan round, local.)
ExecStartPre=/bin/sh -c '[ "$(stat -c %a /etc/coai-bugs/env)" = "640" ] || { echo "/etc/coai-bugs/env must be 0640"; exit 1; }'
Restart=always
RestartSec=5
User=coai-bugs
Group=coai-bugs

[Install]
WantedBy=multi-user.target
```

**Loopback only.** `--urls http://127.0.0.1:8110` — nginx is the only thing that may reach it, and a
server bound to `0.0.0.0` behind a firewall is one firewall rule away from being reachable without
the vhost that keeps the promise above.

---

## Issuing and ending keys

```bash
sudo -u coai-bugs COAI_BUGS_SECRET=… COAI_BUGS_DATA=/opt/coai-bugs/data \
  /opt/coai-bugs/coai-bugs --issue-key --note "the tuesday workshop"
```

The key is printed **once**, on stdout, and only its hash is stored — a stolen database must not
become a set of working keys. If somebody loses theirs, they get a new one; there is nothing here to
recover. `--note` is *our* record of why a key exists ("the tuesday workshop"), never a name.

`--revoke --id <id>` ends one. A revoked key and a key that never existed answer identically, so the
endpoint cannot be used to discover which keys are real.

---

## Quarantine, and why nothing is automatic

Everything accepted lands in `quarantine`, not in the corpus. The alphabet check stops leaked
identifiers; it cannot tell a real skeleton from a crafted one, and a corpus shown to people as
precedent is worth poisoning.

```bash
coai-bugs --waiting --limit 20            # what arrived, with the total
coai-bugs --promote --entry <id>          # one pair into the corpus, printing what it moved
```

There is no timer and no automatic promotion. Past `Corpus.MostWaiting` (20 000) the server refuses
new pairs and says so — a queue somebody must work through, rather than a disk that fills.

---

## Verifying the promise after the first real ingest

This is the step that closes the gap, and it is the only one that can:

**Both paths, not just the happy one.** A plan reviewer caught the version of this recipe that
sent one successful pair: `limit_req` writes the client address to the ERROR log, so the case
that leaks is the case that gets REFUSED, and a single accepted upload would never have produced
it.

```bash
# 1. The path that succeeds.
COAI_BUGS_KEY=... coai-mcp --upload-pairs --server https://bugs.remsoft.dev

# 2. The paths that FAIL: an unknown key, and enough requests to trip the rate limit.
for i in $(seq 1 40); do
  curl -s -o /dev/null -H 'Authorization: Bearer not-a-real-key' \
    -H 'Content-Type: application/json' -d '{"items":[]}' \
    https://bugs.remsoft.dev/ingest
done

# 3. Look for ANY address. Expect nothing from all four.
sudo grep -RE '([0-9]{1,3}[.]){3}[0-9]{1,3}' /var/log/nginx/ | head
sudo grep -RE '([0-9]{1,3}[.]){3}[0-9]{1,3}' /opt/coai-bugs/logs/ | head
sudo journalctl -u coai-bugs --since '10 min ago' | grep -E '([0-9]{1,3}[.]){3}[0-9]{1,3}'
sudo journalctl -u nginx      --since '10 min ago' | grep -E '([0-9]{1,3}[.]){3}[0-9]{1,3}'

# 4. And ask the SERVER whether its edge is behaving. This line appearing means the vhost is
#    wrong, and it names the header rather than the address:
sudo journalctl -u coai-bugs --since '10 min ago' | grep 'the edge sent'
```

A hit in any of them is a defect in the deployment, not in the code, and this file is where it
gets fixed. Record the date this check was run here when it is.

## What the server checks for itself

It cannot read the nginx config — `include` and templating mean the file on disk is not the
effective configuration, and a check that passed on a file nginx never loaded would be worse than
none. So it watches the REQUESTS instead, which are the evidence: any of `X-Forwarded-For`,
`X-Real-IP`, `Forwarded`, `CF-Connecting-IP` or `True-Client-IP` arriving makes it log a warning
naming the header — never the address, because a warning quoting it would be the leak it exists
to report. Step 4 above is how you ask.

It also refuses to start on an empty keyword list, because `/health` answers from a route that
touches neither the list nor the database: a build accident could otherwise start, satisfy the
release smoke, publish, and then refuse every submission as if the contributor were at fault.
