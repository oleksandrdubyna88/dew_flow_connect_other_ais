# PLAN — the Team server stops running as root

> Status: **plan only, nothing implemented yet, 2026-09-06.** Scope: the deployed
> `coai.remsoft.dev` host (`coai-server.service`, the vendor CLI installs, every account slot under
> `/opt/coai/data/accounts/`) and `deploy/README.md`.
>
> Related docs: [PLAN_team_server.md](../research/PLAN_team_server.md),
> [architecture.md](../research/architecture.md), [deploy/README.md](../deploy/README.md).

## The symptom

`coai-server.service` runs as **root**, and a review is this server executing three third-party
agentic CLIs with a prompt somebody wrote. Under root, a compromised CLI, a CLI vulnerability, or a
prompt that talks one of them into running a tool reaches:

- `/etc/coai-server.env` — the Entra configuration;
- `/opt/coai/data/accounts/**` — every vendor account this company shares;
- everything else on a box that also runs CredsForDevs, postgres and SQL Server.

Raised on the code round of epic 4 and accepted there. It was **not** fixed in that change, and the
reason is the thing this plan has to solve rather than repeat.

## Why it is root today, which is the whole difficulty

Nothing chose root. The CLIs were installed as root before the server existed:

- `/root/.local/bin/agy` is a real 210 MB file;
- `/root/.local/bin/claude` is a **symlink** into `/root/.local/share/claude/versions/…`;
- `/root/.local/bin/codex` is a **symlink** into `/root/.codex/packages/standalone/…`;
- `/usr/local/bin/{codex,claude,agy}` are symlinks to those.

And every slot under `/opt/coai/data/accounts/<vendor>/a/` was **signed in as root** — `codex/a/.codex/auth.json`
exists and is root-owned.

So a service account today would find two of the three CLIs pointing into a home it cannot read, and
none of the sign-ins usable. Changing `User=` alone produces a server that starts and cannot review,
which is worse than the current state because it looks fine.

## What must be true when it is done

1. `coai-server.service` runs as a dedicated unprivileged account (`coai`), and `systemctl show
   coai-server -p User` says so.
2. All three CLIs resolve and execute AS that account — `sudo -u coai codex --version` and the other
   two answer.
3. Every account slot is signed in again under that account, and `GET /api/catalog` reports
   `ready > 0` for each vendor.
4. `/etc/coai-server.env` is readable by that account and by nobody else (`0640`, group `coai`).
5. `/opt/coai/data` and `/opt/coai/logs` are owned by it.
6. One real review completes end to end after the change.
7. `ProtectHome=` can be turned ON in the unit, because nothing under `/root` is needed any more.

## Build order

1. **Create the account and re-install the CLIs as it.** `useradd --system --home /opt/coai/home coai`,
   then the three vendors' own installers run as `coai` — the same commands `src_server/Dockerfile`
   uses, which is the one place they are already written down and verified.
2. **Move ownership**: `/opt/coai/{data,logs,home}` to `coai:coai`; `/etc/coai-server.env` to
   `root:coai` `0640`.
3. **Re-sign every slot.** `coai-server login <vendor> <slot>` as `coai`, per vendor. This is the step
   with real downtime and the one an operator must be present for — a device-code flow per account.
4. **Switch the unit**: `User=coai`, add `ProtectHome=true`, keep `MemoryMax`.
5. **Verify** against the seven statements above, then delete `/root/.local/bin/{codex,claude,agy}`
   and `/root/.codex` — leaving them is leaving a second, root-owned copy of the same credentials.

## The rollback

Keep the old slots. Step 3 signs NEW ones under `/opt/coai/data/accounts/` — take a `tar` of that
directory first (`umask 077`), and if a vendor's sign-in cannot be completed, `User=root` plus the
restored tar puts the server back exactly as it is today. Nothing here is one-way except step 5, which
is deliberately last.

## Test plan

There is nothing to unit-test: this is host state, and the assertions are the seven statements. What
CAN be tested first is the one piece of code involved — `SlotEnvironment` already redirects `HOME`
and every `XDG_*` per slot, and `src_server/tests` covers that; re-read those tests before step 3 so
the expected on-disk shape is known rather than discovered.

The honest risk this plan carries: **step 3 is manual and interactive**, and a vendor that changes its
device-code flow makes it longer than planned. Do it when somebody can watch, not on a Friday.

## Definition of Done

- [ ] The seven statements above are true and were checked, not assumed.
- [ ] One real review completed after the change.
- [ ] The root-owned CLI copies and `/root/.codex` are gone.
- [ ] `deploy/README.md` no longer explains why the unit runs as root, because it does not.
- [ ] This plan is promoted to `research/` with what shipped differently.
