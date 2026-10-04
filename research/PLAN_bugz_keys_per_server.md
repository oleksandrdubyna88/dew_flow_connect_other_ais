# PLAN — A Bugz key is filed under the server it was issued by

> Status: **IMPLEMENTED, 2026-10-04.** Scope: `src_vs_code/src/bugsAdminKey.ts` and its callers (the Bugz keys pages,
> the pair upload in `panelProvider.ts`, the contributor-key commands). Deviations, recorded:
> - **No automatic migration.** The plan round (coai session `d5cdb1b2`) showed the premise false: nothing proves which
>   server issued an old fixed-name key (a side may have named its own, a workspace may have overridden it, the shared
>   value may have changed), and filing one under the wrong server is the replay itself. The old keys are held aside,
>   sent nowhere, and the person is asked once per window to ADOPT them for this side's server (only if it issued
>   them) or DISCARD them, with the advice to revoke and re-issue if their sides ever named different servers.
>   The choice is a command (*ConnectOtherAIs: Settle old Bugz keys*), offered by a toast whose one button only opens
>   it, and pointed to by the keys page; a modal names the server and what each answer costs, and every outcome is
>   said. Adopting writes the new name, reads back THE VALUE, and only then deletes the old one (code round).
> - `credentialsFor(secrets, server)` is the one road to a request: the server and its key from one value. A discard
>   whose record names no issuing server is refused and the record kept, never revoked against this side's server.
>   A key typed with no server set is refused out loud (`bugz-key-needs-a-server`), never dropped behind a success.
> - The wiring scan became compile-time plus one scan: the readers and setters REQUIRE the server, and a test proves
>   the old fixed names appear in the key module alone. The key names are pinned by literal tests.
>
> Related docs: [module_extension.md](module_extension.md). Found on the code round of the per-side fix
> (`fix/prompts-per-round-per-side`, coai session `1e7363d0`, 2026-10-04).

## The symptom

`coai.bugzServer` is a per-side setting, and since the per-side fix every place that reads it reads THIS side's
value. The keys it is used with are not per server: the admin key and the contributor key are stored in VS Code's
secret storage under two fixed names, `coai.bugs.adminKey` (`bugsAdminKey.ts:33`) and `coai.bugs.contributorKey`
(`bugsAdminKey.ts:43`). So a person whose sides name two different Bugz servers — production on one, a staging or
mistyped host on the other — has one admin key that the keys page on either side sends to that side's server, and
one contributor key the pair upload sends the same way (`panelProvider.ts:3163`). The host that receives a key it
was not issued for can replay it against the server that issued it.

This is not new in kind: the pair upload already read the per-side server with the single contributor key. The
per-side fix made the keys pages consistent with it, which is what made the gap visible.

## The goal

A key is filed under the normalised address of the server that issued it, and is only ever sent to that address.
A side whose server has no key of its own asks for one, rather than borrowing another server's.

## Design

- Secret names gain the server: `coai.bugs.adminKey:<normalised origin>` and `coai.bugs.contributorKey:<normalised
  origin>`, normalised by the existing `canonicalTeamServerUrl` (reuse, not a second normaliser). One pure function names them, unit-tested on the URL shapes that matter (trailing slash,
  case, default port, a path).
- Migration, once: a key under the old fixed name is moved to the name of the server the SHARED `coai.bugzServer`
  names — the only server it can have been issued by before per-side servers were read — and the old name deleted.
  No shared server: the old key is left and the page says it cannot tell which server it belongs to.
- `adminKey(secrets, server)` / `contributorKey(secrets, server)` take the server; every caller passes
  `bugzServerThisSide(context)`.

## Build order

1. The naming function and its tests (RED first).
2. The migration and its tests (old name present / absent, shared server set / unset, run twice).
3. The callers, through `bugzServerThisSide`.
4. Help text and `research/module_extension.md`.

## Test plan

- Unit: names per origin; a key stored for server A is not returned for server B; migration moves exactly once.
- Wiring: no call of `adminKey`/`contributorKey` without a server argument (a scan, with a companion that finds a
  planted call).

## Definition of Done

- [ ] Keys are filed and read per server; the fixed names are gone after the migration.
- [ ] A side whose server has no key asks for one.
- [ ] Tests red without the change, green with it; full suite green; docs updated.
