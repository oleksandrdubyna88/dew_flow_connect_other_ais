# Deploying the ConnectOtherAIs Team server

One company subscription per vendor, on one machine, behind your company's sign-in.

> **`coai.remsoft.dev` is deployed and running** as a **systemd unit**, not a container — see *How
> this host actually runs it* below for why, and *The container* for when you would want the other
> shape. The steps here are the ones that were run, in the order they were run.

---

## What this adds to the machine, and what it does not touch

`dew_flow_creds_for_devs` **is not modified at all.** No shared docker network, no `EXTRA_DOMAINS`, no
edit to the vault's site file or its certificate. That is possible because of how this box is put
together, which was read on the machine rather than assumed:

| | |
|---|---|
| The vault's compose nginx | binds `127.0.0.1:8081` / `127.0.0.1:8443`, `TLS_MODE=none`. **Not the edge.** |
| The edge | a **host** nginx under systemd, one site file per service in `/etc/nginx/sites-enabled/` |
| TLS | **host** certbot (`certbot.timer`, `authenticator = nginx`), **one certificate per name** |
| How services are reached | loopback, proxied by the host. Nothing needs a shared network. |

---

## How this host actually runs it — systemd, not a container

The three vendor CLIs are **already installed on the host** (`/root/.local/bin/{codex,claude,agy}`,
symlinked into `/usr/local/bin`), and every account slot under `/opt/coai/data/accounts/` is
**already signed in**. A container that installed its own copies would add ~750 MB to the image to
duplicate binaries that are already there, and the slots' credentials live on disk either way.

So the deployment is:

| | |
|---|---|
| `/opt/coai/src` | a checkout, used only to build |
| `/opt/coai/bin/coai-server` | the Native AOT binary — **21 MB** |
| `/opt/coai/data` | `vendors.json`, the signed-in account slots, sessions, `usage.jsonl` |
| `/opt/coai/logs` | one file per run |
| `/etc/coai-server.env` | the configuration, `0600` |
| `/etc/systemd/system/coai-server.service` | the unit |

`MemoryMax=1500M` in the unit does what a container's `mem_limit` would: this box has 3.8 GB with
~1.9 GB free beside SQL Server (866 MB), postgres and the vault, and a **4 GB swapfile**. Without a
limit the kernel picks its own victim when a review overshoots, and it may pick postgres.

**The unit runs as root, and that is a consequence rather than a default.** `claude` and `codex` in
`/root/.local/bin` are SYMLINKS into `/root`, and every slot was signed in as root. Another user
would find two of the three CLIs pointing into a home it cannot read, and none of the sign-ins
usable. Tightening this means re-installing the CLIs and re-signing every slot as a service account
— worth doing, and not worth doing silently as part of a first deployment.

### Building and installing

```bash
# once
apt-get install -y clang            # Native AOT links with the platform toolchain
git clone --depth 1 -b main https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais.git /opt/coai/src

# each release — one command; it publishes, inspects, switches, canaries and self-rolls-back
cd /opt/coai/src && git fetch origin main && git reset --hard origin/main
COAI_TOKEN=<a Team-server session token> deploy/systemd-release.sh <version>
```

The AOT publish takes several minutes — run it detached rather than inside a command with a timeout.

### The release script, and why it is a script

[`systemd-release.sh`](systemd-release.sh) is the whole procedure, because the family rule
(`.claude/rules/shared/common/development-workflow.md`) asks for things a list of commands in a README
cannot give: an immutable version-addressed artefact, the last three retrievable, a rollback that
builds nothing and is **one command**, and a look inside the artefact before it is trusted.

| | |
|---|---|
| `systemd-release.sh 0.5.4` | publish → inspect → switch → canary, rolling itself back if any step fails |
| `systemd-release.sh --rollback` | pop one deployment off the trail. No build. Seconds. |
| `systemd-release.sh --list` | what is live and what is retained |

What it does that the old hand-run sequence did not:

- **Every release is its own directory**, `/opt/coai/releases/<version>-<utc>`, never written to
  again. `/opt/coai/bin` is a **symlink**, swapped with one `mv -T` — a single rename, so there is no
  instant at which the live tree is half-new. Copying file-by-file over the running directory (the
  previous instructions) leaves old and new assemblies mixed if it is interrupted, and the unit then
  starts a combination nobody has tested.
- **Four are kept** — the current one plus the three the rule requires — and the oldest is pruned only
  *after* a release passes its canary. The previous instructions did `rm -rf bin.rollback` **before**
  copying the replacement, so two bad releases in a row left nothing to go back to, and an interruption
  between the two commands left nothing at all.
- **`--rollback` pops a trail**, it does not overwrite a single "previous" pointer. That exact defect
  was measured in this family: a rollback that did not record where it rolled back *from*, so a second
  consecutive rollback returned to the place it had just left.
- **The artefact is opened before it is trusted**: the binary must exist and must contain the version
  string it was asked to build. A build reporting success while silently copying nothing is the trap
  the rule was written for, and a clean `0 error(s)` is not evidence about the file.
- **The token never reaches argv.** `curl` reads it from a `600` config file, so it is in no `ps aux`
  listing and in no `~/.bash_history`. Putting it in `-H "Authorization: Bearer $TOKEN"` — which is
  what this README said for one commit — publishes a live credential to every other account on the box.

### The canary decides, not `systemctl`

The script submits **one real review per vendor in `vendors.json`** and polls each to a terminal
state — never judging on a single bounded wait, because a `wait` shorter than the review's own budget
returns `running` for a healthy slow reviewer and calling that a failure rolls back a deployment that
was fine.

A release passes only when every vendor reaches `done` with a non-empty answer. Anything else — a
`failed`, an empty answer, or no terminal state inside the budget — rolls back automatically and exits
non-zero.

That check is not ceremony. Until 2026-09-07 this server had never completed a single review: every
one was recorded `NotStarted` while the vendor CLI's own transcript showed a perfect answer, and
`usage.jsonl` held exactly two lines, weeks apart, both failures. `systemctl is-active` was green
throughout, and so was `/api/health`. One canary review would have found it the day it was deployed —
and a canary that asked only about `claude` would still have missed the two vendors whose CLIs need a
schema file on disk, which is why it asks about every vendor the catalog offers.

### Checking it

```bash
systemctl is-active coai-server
ss -ltn | grep 8090                      # MUST be 127.0.0.1:8090, never 0.0.0.0
curl -sS https://coai.remsoft.dev/api/health
journalctl -u coai-server -n 50 --no-pager
```

---

## One-time: the Entra app registration

An administrator, once, in portal.azure.com:

1. **Microsoft Entra ID → App registrations → New registration**: name *ConnectOtherAIs Team Server*,
   **single tenant**, no redirect URI.
2. **Expose an API → Set** the Application ID URI (`api://<client-id>`).
3. **Add a scope** `coai.access`, *Admins and users*.
4. **Add a client application** `aebc6443-996d-45c2-90f0-388ff96faa56` (Visual Studio Code),
   authorised for that scope. **This is the step that is forgotten**, and it produces a 401 with
   everything else correct: without it the extension can only obtain a Graph token, which Microsoft
   makes unverifiable by third parties.

Then in `/etc/coai-server.env`: `Auth__Microsoft__Tenant`, `Auth__Microsoft__Audiences`
(`<client-id>,api://<client-id>`), `Auth__Microsoft__ClientScope`
(`api://<client-id>/coai.access`), `Coai__AllowedDomains`.

The extension refuses any advertised scope that is not `api://<guid>/coai.access`, and asks the
person to confirm the application id the first time it signs into a given server — so a typo here
surfaces as a refusal with a sentence, not as a token minted for the wrong resource.

---

## The edge — in this order, and check each step

Already applied on `coai.remsoft.dev`; this is the sequence for the next host.

### 1. DNS first

```bash
dig +short coai.remsoft.dev @1.1.1.1
curl -fsS https://api.ipify.org; echo
```

The two must match. An A record that has not propagated fails HTTP-01 with an error about the
*challenge*, not about DNS, and that is a confusing half hour.

### 2. The site file — HTTP only, at first

```bash
cp deploy/nginx/coai /etc/nginx/sites-available/coai
ln -sfn /etc/nginx/sites-available/coai /etc/nginx/sites-enabled/coai
nginx -t && systemctl reload nginx
curl -fsS -H 'Host: coai.remsoft.dev' http://127.0.0.1/api/health
```

`deploy/nginx/coai` is **port 80 only, deliberately.** Copying the neighbouring `credsfordevs` file
whole cannot work: it names a certificate that does not exist yet, `certbot --nginx` runs `nginx -t`
before it issues, so the config is invalid, certbot aborts, and nginx will not reload for **any**
site on the box — the vault included.

**Rollback:** `rm /etc/nginx/sites-enabled/coai && nginx -t && systemctl reload nginx`.

### 3. The certificate

```bash
certbot --nginx -d coai.remsoft.dev
nginx -t && systemctl reload nginx
curl -fsS https://coai.remsoft.dev/api/health
```

After this the installed file is **no longer** what this repository holds: certbot's plugin edits it
in place, adding `listen 443 ssl`, the certificate paths and a 301 from `:80`. Re-copying this
repository's version over it later would remove the TLS listener.

**If issuance fails or is interrupted:** nothing is broken — the port 80 vhost still serves. Fix the
cause and run the same command again; certbot is idempotent. To back out entirely,
`certbot delete --cert-name coai.remsoft.dev` then the step-2 rollback.

---

## Two things this deployment already taught

**The HTTPS gate refused everything on a correctly configured host.** `UseForwardedHeaders` consumes
`X-Forwarded-Proto` when the proxy is TRUSTED, and the gate read the raw header — so setting
`Coai:TrustedProxies` correctly is what broke it, and leaving the proxy untrusted is what made it
appear to work. Fixed, with `ForwardedHttpsTests` covering the production pair; every other test in
the suite sets `RequireForwardedHttps=false`, which is why 165 of them missed it.

**The vendor CLIs are installed LATEST, not pinned** — the operator's decision, recorded with its
cost. No installer takes a version argument and two of the three publish no checksum, so "fail on a
digest mismatch" is not implementable against them. A vendor that changes a flag between two builds
passes every test in CI (which drives a fake CLI) and fails every real review. The server probes each
CLI's version at startup and carries it in `/api/catalog` — that is **observability**, not a control.
One pin exists if wanted: Antigravity publishes `manifests/linux_amd64.json` with a sha512.

---

## The container — for a host that has none of this

`src_server/Dockerfile` and `deploy/docker-compose.yml` build a self-contained image that installs
the three CLIs itself (~1.27 GB, of which 750 MB is the CLIs and 21 MB is this server). It is the
right shape for a **fresh** machine with no CLIs and no signed-in slots, and it pins the whole
dependency set to one artefact you can roll back.

It is **not** what `coai.remsoft.dev` runs, because that host already had the CLIs and the sign-ins.

```bash
cd deploy && cp .env.example .env    # edit the required values
docker compose up -d
```

`init` runs first and gives `./data` and `./logs` to uid 10001 — without it, first boot fails on any
normal Linux host, since a freshly created bind-mount source belongs to root. Wait for the health
check rather than assuming readiness; `update.sh` does that for you.

---

## Backing up

`/opt/coai/data` is the signed-in accounts, `vendors.json`, the sessions and `usage.jsonl`. Losing it
means every account signs in again and the spending history is gone. Nothing is unrecoverable, and
nothing is automatic — `deploy/backup.sh` covers the compose layout; on this host the equivalent is a
`tar` of `/opt/coai/data` with the unit stopped, because `usage.jsonl` is appended to and a slot's
lock file is held open.
