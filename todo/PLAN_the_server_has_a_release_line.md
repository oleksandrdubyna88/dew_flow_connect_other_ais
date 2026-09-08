# PLAN — the Team server gets a release line, and a deploy that runs it

> Status: **plan only, nothing implemented yet.** Scope: `.github/workflows/release.yml`,
> a new `.github/workflows/deploy-server.yml`, `deploy/systemd-release.sh`, and the tests in
> `src_vs_code/src/test/install.test.ts` that hold the workflow to its promises.
>
> Related docs: [deploy/README.md](../deploy/README.md),
> [research/module_server.md](../research/module_server.md).

## The symptom

`coai.remsoft.dev` reports `{"ok":true,"version":"0.5.5"}` and there is **no `server-v0.5.5`
anywhere** — no tag, no release, no artefact. The running binary was built from commit `fe85c45`
on the host, and its release directory is literally named `0.5.5-STAMP`: the deploy script's
version placeholder was never substituted, so the one number the server tells the world about
itself traces back to nothing.

Three separate holes make that possible, and each is worth naming because each fails differently:

1. **`server-v*` publishes no binaries.** The tag builds two container images and stitches a
   manifest (`release.yml:335-460`). The host does not run a container — it runs a Native AOT
   binary under systemd (`deploy/README.md`, *How this host actually runs it*) — so the one
   artefact the deployment actually consumes is the one the release line does not produce.
2. **`server-v*` creates no GitHub Release.** The extension's update check reads
   `api.github.com/repos/…/releases?per_page=30` ([installer.ts:255](../src_vs_code/src/installer.ts#L255))
   and filters it by tag prefix. A bare tag is invisible to it, so the panel's *Server 0.5.5 ·
   latest published* line has nothing to compare against and can never fire.
3. **Nothing updates the server from CI.** `deploy/systemd-release.sh` is a good script — release
   trail, artefact inspection, per-vendor canary, one-command rollback — and it is only ever run
   by hand, over SSH, by the person who remembers it exists.

## What this changes

| | Today | After |
|---|---|---|
| `server-v0.5.5` builds | 2 images + manifest | + **six Native AOT binaries** (linux/win/osx × x64/arm64) |
| `server-v0.5.5` publishes | `ghcr.io/…/coai-server:0.5.5` | + a **GitHub Release** carrying those six archives |
| Updating the live host | ssh, by hand, from memory | **`deploy-server.yml`**, dispatched, approval-gated, canaried |

## Build order

### 1. `server-binaries` — six Native AOT builds, tested and smoked

A sibling of `mcp-binaries` (`release.yml:33-244`), which already solved every hard part of this:
the six-RID matrix, `macos-latest` cross-building `osx-x64` because `macos-13` was retired and a
job targeting it QUEUES rather than fails, the Linux `clang`/`zlib1g-dev` toolchain step, the
version stamped once and asserted from the built binary afterwards.

The server is already `PublishAot=true` (`src_server/src/CoaiServer.csproj:11`), so this is a
publish, not a porting job.

**The smoke is the part that is NOT copied.** `coai-mcp` answers `--version` on stdout; the server
has no argument surface at all — it answers `/api/health` with `{ok, version}`
(`Program.cs:272-273`), from the assembly's informational version (`Startup.cs:15-18`). So the
smoke starts the published binary on a loopback port and asks it:

```
Coai__DataDir=<temp>  Coai__AllowedDomains=example.test
Auth__Local__SigningKey=<≥32 bytes>  ASPNETCORE_URLS=http://127.0.0.1:5099
```

Those three are exactly what `Startup.Guard` demands (`Startup.cs:30-76`): an authentication
scheme, and a domain boundary. The local signing key is the scheme that needs no tenant — and the
guard refuses it *alongside* a real provider, so a smoke that used it against a real config would
fail loudly rather than quietly weaken anything.

The assertion is `version == $VERSION`, for the same reason the mcp job asserts it: the panel asks
a binary what it is instead of remembering what it downloaded, so a stamping step that silently
stopped working would put a lie back into that line one release later.

`osx-x64` keeps the mcp job's skip-with-a-named-reason (`Bad CPU type in executable`), and only
that reason.

### 2. The release itself

The mcp job's shape, unchanged: `gh release create "$GITHUB_REF_NAME" … || true` then
`gh release upload --clobber` per asset. Each archive carries the binary **and
`appsettings.json`** — the publish output's non-symbol files — because the server reads its
Serilog levels and its rate-limit defaults from it, and a binary shipped alone starts with
different behaviour than the one that was tested.

The header comment at `release.yml:8-10` currently states the opposite of what this plan does
(*"there is nothing to attach to a release — the artefact IS the image"*). It was true when the
only consumer was `docker pull`. It is rewritten, not deleted: the reason a release exists now is
the panel's update check, and that is what the comment should say.

### 3. `deploy-server.yml` — updating the host, without re-implementing the release

The script on the host already does the work, and it is far better than anything a workflow step
would grow: an immutable version-addressed release directory, `bin` swapped by a single `mv -T`
rename, a trail that a second consecutive rollback still reads correctly, and a canary that runs
**one real review per configured vendor** because `systemctl is-active` said "healthy" every day
the server ran without completing a single review.

So the workflow's whole job is: get the host to the right commit, and run that script.

```
workflow_dispatch(version) → environment: production (approval) → ssh → git fetch --tags
  → git checkout server-v<version> → deploy/systemd-release.sh <version>
  → curl https://coai.remsoft.dev/api/health, assert version == <version>
```

Deliberate choices, each with its reason:

- **Dispatch, not `on: release`.** A deploy that fires itself on a tag would have restarted the
  live server in the middle of the load campaign that ran against it on 2026-09-08. The person
  cutting a release and the person choosing to interrupt live reviews are the same person here,
  but they are not making the same decision.
- **`environment: production`.** It is where a required reviewer and the deploy secrets both live;
  a workflow that could reach the host from any branch is a workflow anyone with push access can
  point at the company's subscriptions.
- **Plain `ssh`, no third-party action.** One fewer supply-chain surface on the job that holds a
  root key, and nothing to pin.
- **The host key is checked.** `StrictHostKeyChecking=yes` against a `KNOWN_HOSTS` secret. A blind
  first connection is how a deploy key is handed to whoever answers on port 22.
- **Missing secrets fail LOUDLY**, in their own step, naming each one — the family's rule for the
  `VSCE_PAT` skip, applied to a job whose silent no-op would be a server everyone believes was
  updated.
- The final health assertion is made **by the workflow**, not read from the script's output: the
  script's canary proves reviews work, this proves the version serving the internet is the one
  that was asked for.

### 4. `systemd-release.sh` takes a published binary (`--from <dir>`)

Building on the host costs ~3 minutes of a 3.8 GB box's RAM with `MemoryMax=1500M` in the unit
next door, to produce a binary CI has already built, tested and smoked for that exact RID. The
script keeps its build path as the default — an air-gapped or offline release must stay possible —
and gains one flag that skips the publish and takes the artefact instead.

The artefact inspection (`grep -qa "$VERSION" "$RELEASE/$SERVICE"`) applies to a downloaded binary
exactly as it does to a built one, and matters more: it is the check that a download landed the
version it was named for.

## Test plan

Every one of these fails first, against the workflow as it is today.

| # | Test | Holds |
|---|---|---|
| 1 | `every RID the workflow builds is a RID the extension will install` — rescoped to the `mcp-binaries` job | The existing test scrapes **every** `- rid:` line in the file, so it turns red the moment a second matrix exists. Rescoping keeps its real guarantee (the extension can install everything the MCP line publishes) instead of demanding the extension install server binaries it never downloads. |
| 2 | `a server tag publishes binaries for every platform the mcp line does` | The two matrices stay the same six RIDs; a platform added to one and forgotten in the other is a red test, not a missing asset. |
| 3 | `a server tag creates a GitHub release, because that is what the panel reads` | Ties `release.yml` to `installer.ts:255`. This is hole #2, and it is invisible from either file alone. |
| 4 | `the server smoke asks the binary its version the only way the server can be asked` | The smoke greps `/api/health` and asserts the stamped version — the mcp job's `--version` assertion has no equivalent here, and a copied-but-unadapted smoke would pass while testing nothing. |
| 5 | `the deploy workflow refuses to run without the secrets it needs` | The named-secret guard exists and lists all four. |
| 6 | `the deploy workflow runs the release script rather than its own steps` | The anti-duplication guarantee: the day a workflow step starts doing its own `systemctl restart`, the canary and the rollback trail are gone and nothing says so. |
| 7 | `--from takes a published binary and still inspects it` (bats-less: a shell test in the smoke job) | The new flag cannot skip the artefact inspection — the check that a download is what it claims. |

Run: `cd src_vs_code && npm test` (tests 1–6), `node -e` parse of both workflows (the existing
*Workflows parse* step covers 3–6 structurally), and the release itself for 7.

## Definition of Done

- [ ] `server-v*` builds six Native AOT binaries, each tested and smoked on its own architecture
      (except the one cross-built RID, which says so).
- [ ] `server-v*` creates a GitHub Release carrying those six archives + checksums.
- [ ] The panel's *latest published server* line has something to read.
- [ ] `deploy-server.yml` updates `coai.remsoft.dev` by running `deploy/systemd-release.sh`,
      behind an approval, with a host-key check and a post-deploy version assertion.
- [ ] `systemd-release.sh --from <dir>` uses a published binary and still inspects it.
- [ ] Tests 1–7 above are written, watched fail, and pass.
- [ ] `deploy/README.md` documents the workflow beside the manual steps it does not replace.
- [ ] Promoted to `research/` when it ships, per
      [planning-docs.md](../.claude/rules/shared/common/planning-docs.md).

## What this plan does NOT do

- **It does not cut `server-v0.5.5`.** That was asked for, and it is the first thing that happens
  *after* this lands — cutting it now would publish the incomplete line this plan exists to fix,
  and 0.5.5 would be a release with no binaries and no GitHub Release.
- It does not change the container path. The images stay; they are what a different deployment
  shape would consume, and `deploy/update.sh` still drives that one.
- It does not move the live host off systemd, or off root. Both are recorded decisions with
  reasons in `deploy/README.md`.
