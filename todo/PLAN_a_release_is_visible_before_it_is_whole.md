# PLAN — a release must not be visible before it is whole

> Status: **plan only, nothing implemented yet.** Scope: `.github/workflows/release.yml` — the
> `mcp-binaries`, `server-binaries` and `extension` jobs and the completeness check beside them.
>
> Related docs: [POST_DEPLOY.md](../POST_DEPLOY.md) item 1,
> [module_extension.md](../research/module_extension.md),
> `PLAN_the_server_has_a_release_line.md` (in review as PR #106) — whose completeness job
> checks this AFTER the fact.

## The symptom, observed twice

**2026-09-08, ~17:31 UTC.** The operator pressed Install and the panel said:

```
coai-mcp was not installed: downloading
https://github.com/…/releases/download/mcp-v0.18.13/coai-mcp-0.18.13-win-x64.zip answered 404
```

That asset exists. It was uploaded at 17:31:54Z — after the click. The release had been created
minutes earlier by whichever matrix leg finished first, and the extension's update check reads
`…/releases` immediately, so for the whole length of the matrix the release is **published, visible,
and missing most of its assets**. Every link it offers in that window answers 404 honestly.

**And a second failure was hiding underneath it.** `mcp-v0.18.13` is still incomplete: five
platforms of six, no `win-arm64`, because that leg failed its own test step (1090 of 1091 passed —
`RemoteShimScenarioTests.AShimKilledMidClaim_LeavesEitherNothingOrAWholeClaim_NeverHalf` timed out
after 30 s on that runner alone). Anyone on Windows ARM gets the same 404 permanently rather than
for a minute.

`POST_DEPLOY.md` item 1 exists for exactly the second one and answers `missing: win-arm64` in a
second — it had not been run. It cannot see the first one at all, because by the time anybody runs
it the window has closed.

**This has happened before**, and the fix chosen then was to burn the tag: `mcp-v0.16.0` shipped
five RIDs and no `win-x64`, and the entry recording it is in `POST_DEPLOY.md` today.

## Why the current shape produces this

`gh release create` runs inside every matrix leg, tolerated when it fails because five of the six
will lose that race. It is the earliest possible moment a release can exist, and nothing anywhere
distinguishes "this release exists" from "this release is finished".

The completeness job added for the server line on 2026-09-08 checks the assets **after** they are
all uploaded — which turns an incomplete release into a red build, and does nothing for the client
that downloaded during the window.

## The change

**Create the release as a DRAFT; publish it when it is complete.**

A draft release is invisible to `GET /releases` for anyone but a writer, so the extension's update
check cannot see it and cannot offer a download from it. The completeness job — which already
verifies every expected asset by NAME for the server line — becomes the thing that flips
`draft: false`.

```
matrix leg (first)   gh release create --draft …      ← invisible to clients
matrix legs (all)    gh release upload …
completeness job     every expected name present?  →  gh release edit --draft=false
```

Three consequences worth stating:

1. **A failed leg leaves the release a draft**, permanently, until somebody re-runs it. That is the
   correct outcome and the opposite of today's: `mcp-v0.18.13` would not be offering five platforms
   to five sixths of its users while the sixth gets a 404 — it would be offering nothing, visibly,
   with a red build beside it.
2. **The mcp line gains the completeness job it never had.** Today only the server line has one, and
   the mcp line is the one that has now shipped an incomplete release twice.
3. **`extension-v*` needs the same treatment or an explicit exemption.** It is one job producing one
   asset, so its window is small — but "small" is what this defect was called last time.

## What this does NOT do

- It does not make the extension defensive about missing assets. It could check before offering, and
  that is a second belt; the release being honest about its own state is the braces, and braces
  first.
- It does not fix the flaky test that made `win-arm64` fail. That is its own change:
  [PLAN_the_shim_scenario_waits_too_briefly.md](../research/PLAN_the_shim_scenario_waits_too_briefly.md).
- It does not re-cut `mcp-v0.18.13`. Re-running the failed leg uploads the missing asset to the
  existing release, which is cheaper than burning a tag and is what was done on the day.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `every release line creates its release as a draft` | The window cannot reopen by somebody copying the old three-line pattern into a fourth line |
| 2 | `a release is published only by the job that checked it is complete` | `--draft=false` appears exactly once per line, in the completeness job |
| 3 | `the mcp line has a completeness check, by NAME` | The line that shipped incomplete twice gains what the server line has |
| 4 | `the completeness job needs every matrix leg` | A job that published before the last upload would recreate the window with more steps |

All four read the workflow text, beside the tests that already hold this file
(`src_vs_code/src/test/install.test.ts`).

## Definition of Done

- [ ] Every release line creates a DRAFT and publishes it from its completeness job.
- [ ] `mcp-binaries` has a completeness job that checks each expected asset by name.
- [ ] Tests 1–4 written, watched fail, and passing.
- [ ] `POST_DEPLOY.md` item 1 keeps working, and its entry records that a draft release is now the
      state a failed matrix leaves behind.
- [ ] `research/module_extension.md` records why the update check can trust `…/releases` again.
