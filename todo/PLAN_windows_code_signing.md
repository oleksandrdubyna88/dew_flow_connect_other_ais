# PLAN — the Windows `coai-mcp` package is signed, so Smart App Control lets it run and load its libraries

> Status: **plan only, nothing implemented yet, 2026-11-03.** Scope: the `mcp-binaries` and
> `mcp-release-complete` jobs of `.github/workflows/release.yml`, one new caller job and one new
> attach job, `.github/scripts/archive-carries.sh` (zip support), the owner's GitHub environment here.
> No product code. Twin of `dew_flow_creds_for_devs · todo/PLAN_windows_code_signing.md`, which owns
> the shared design: the reusable workflow `oleksandrdubyna88/dew_flow_release_signing ·
> .github/workflows/sign-windows.yml`, the Azure setup (its O1–O9) and the facts F1–F11. Findings that
> matter are carried over below rather than linked.

## 1. The symptom and the goal

**Symptom.** On Windows 11 with Smart App Control (SAC) on, the extension's *Install the MCP server*
unpacks `coai-mcp-<v>-win-<arch>.zip`, and SAC blocks the unsigned `coai-mcp.exe`. SAC also blocks
unsigned **DLLs** a process loads, and this package ships native libraries beside the binary:
`e_sqlite3.dll` and the tree-sitter grammars (`release.yml:361-424`). A signed `coai-mcp.exe` alone
would start and then fail on its first database write — the failure `release.yml:355-360` records from
0.18.1, where the library was missing, now caused by a block instead.

**Goal.** Every `mcp-v*` release carries `win-x64` and `win-arm64` zips in which `coai-mcp.exe` is
signed by the owner's organization and every other PE file is either signed by the same signer or
already carries a valid vendor signature (owner decision D6, 2026-11-03). A tag whose Windows package
is not signed fails and stays a draft. The signing is the shared workflow's — this repository writes
none of it.

**Out of scope.** `coai-server` and `coai-bugs` Windows binaries (`release.yml:619`, `:1047`): the
owner asked for the MCP binary. The same call fits those jobs later. macOS/Linux: see the twin's §1.

## 2. Facts carried over from the twin (checked there 2026-11-03)

- The signing action does not run on Windows ARM runners, so the shared workflow signs both
  architectures on `windows-2025` (twin F3).
- Federation needs an exact subject; this repository emits the immutable one, prefix
  `repo:oleksandrdubyna88@71817001/dew_flow_connect_other_ais@1352342534` (read 2026-11-03), so the
  trusted subject is that prefix + `:environment:release-signing` (twin F6, F7).
- In a reusable workflow the environment and the federated subject are the CALLER's (twin F8, proven
  only by V1).
- release-please here skips a commit that touches only `.github/**` (`release-please-config.json:65`);
  and the mcp line is GUARDED: `changelog-names-the-release.mjs` refuses an mcp release that
  `src_vs_code/CHANGELOG.md` does not name (`release-please-config.json:18-24`).

## 3. Design

**The build leg** (`mcp-binaries`, `release.yml:176-453`), Windows RIDs only:

- `Package` (`:361-424`) still builds `coai-mcp-$VERSION-$RID/` with the binary, `e_sqlite3` and the
  grammars, and still runs both carry checks — on a listing of that FOLDER (prefix included) instead of
  the zip's, because the zip is now made after signing. Non-Windows RIDs are untouched.
- `Checksum` (`:425-427`) and `Attach to the draft release` (`:439-453`) get a non-Windows `if:`.
- A new upload: the folder's parent as `unsigned-mcp-${{ matrix.rid }}`, `retention-days: 7`.

**`mcp-sign`** (new) — `needs: mcp-binaries`, the same `if:` as `mcp-binaries`, permissions
`contents: read` + the federation permission only, and:

```yaml
uses: oleksandrdubyna88/dew_flow_release_signing/.github/workflows/sign-windows.yml@<40-hex> # v1.0.0
with:
  artifacts: '["mcp-win-x64","mcp-win-arm64"]'
  own-files: '["coai-mcp.exe"]'
  required: ${{ startsWith(github.ref, 'refs/tags/') }}
```

The shared workflow signs every unsigned `exe`/`dll`, keeps a vendor-signed one as it is, verifies, and
packs with today's `7z` command and checksum format, proving by hash that the zip holds the verified
files.

**`mcp-sign-attach`** (new) — `needs: mcp-sign`, tag only, `ubuntu-latest`, `contents: write`.
Downloads `mcp-win-*`, runs the product check on each FINAL zip —
`archive-carries.sh <zip> coai-mcp.exe '*e_sqlite3*'` — then `gh release upload … --clobber` of the zip
and its `.sha256`, exactly as the leg does today. `archive-carries.sh` learns `.zip` (listing by
`unzip -Z1`), with tests beside its existing ones (`src_bugs/tests/TheArchiveCheckTests.cs:154` runs the
script on archives built to be wrong).

**`mcp-release-complete`** (`:455-473`) — `needs: [mcp-binaries, mcp-sign-attach]`. Its
`verify-and-publish-release.sh` stays the last word on completeness.

**Releasable commit.** A `fix(mcp): …` commit touching a file under `src_mcp/` that honestly describes
the change (decided at implementation: there is no `src_mcp/README.md`; `RELEASES.md` is
release-please's), and the release pull request carries the `src_vs_code/CHANGELOG.md` prose the guard
asks for. The owner merges it only after V1.

**Owner steps here** (the Azure side is the twin's O1–O6): the environment `release-signing` with the
six variables (twin O7), deployment rule `main` + `mcp-v*` + this PR branch until the merge, which O9
removes.

## 4. Build order

1. Wait for the shared repository's `v1.0.0` (twin E1).
2. This PR: the leg change, `mcp-sign`, `mcp-sign-attach`, the `needs`, `archive-carries.sh` zip
   support and its test, the workflow test (§5), docs (`research/architecture.md` where it describes the
   release, the `release.yml` comments).
3. Owner: twin O1–O8 including this repository's credential and environment.
4. V1 here: dispatch `target: mcp` on the PR branch.
5. Merge, O9, then the next mcp release.

## 5. Test and verification plan

- `archive-carries.sh` with a `.zip`, in `TheArchiveCheckTests.cs`: a zip carrying both files passes; one missing `coai-mcp.exe`
  fails; one carrying `coai-mcp.exe.dbg` only fails (the #328 hole, now for zip). RED first.
- A structure test of `release.yml` in `src_vs_code/src/test/` (where `install.test.ts` already reads it): Windows legs upload
  `unsigned-mcp-<rid>` and do not attach; `mcp-sign` calls the shared workflow at a 40-hex SHA with
  `own-files` `["coai-mcp.exe"]` and the tag-bound `required`; `mcp-sign-attach` runs
  `archive-carries.sh` before uploading; `mcp-release-complete` needs it; no other job has the
  federation permission. Break-it: drop `mcp-sign-attach` from `needs`, watch it fail, restore.
- The whole existing suite, actionlint, `plan-lifecycle.mjs`, `pin-check.mjs`.
- **V1** — the dispatch trial signs both zips; downloaded and extracted on the owner's machine,
  `coai-mcp.exe` and every DLL report `Valid`.
- **V2** — after the release, on the owner's SAC machine: *Install the MCP server*, then one real
  review round, which writes the rounds database (loads `e_sqlite3.dll`) — not just `--help`.

## 6. Growth surfaces

None that grows on its own: two `unsigned-mcp-*` and two `mcp-win-*` artifacts per run, a few tens of
MB each, `retention-days: 7`.

## 7. Definition of Done

- [ ] PR merged with the tests green and the break-it output in it; `coai` gate plan and code rounds
      resolved.
- [ ] V1 observed before the merge; the next mcp release signed; V2 observed on a SAC machine.
- [ ] Plan promoted to `research/` with its deviations.
