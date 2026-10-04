# PLAN — A Bugz key is filed under the server it was issued by

> Status: **IMPLEMENTED, 2026-10-04.** Scope: `src_vs_code/src/bugsAdminKey.ts` and its callers (the Bugz keys pages,
> the pair upload in `panelProvider.ts`, the contributor-key commands). Deviations, recorded:
> - "No shared server: the page says it cannot tell" was not built: with no shared server the old keys stay under
>   their old names and, since every reader now asks BY server, are sent nowhere; setting the shared server and
>   restarting moves them. A sentence on the page is left to whoever next touches the keys page.
> - The wiring scan in the test plan became compile-time: `adminKey`/`contributorKey` and their setters REQUIRE the
>   server argument, so a call without one does not build — stronger than a scan, and nothing to keep in step.
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
