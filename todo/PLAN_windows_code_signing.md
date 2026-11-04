# PLAN — the Windows `coai-mcp` package is signed, so Smart App Control lets it run and load its libraries

> Status: **stopped for re-review, 2026-11-04 — re-reviewed baseline for epoch 2, pending approval. Built and gated 2026-11-03 on `ci/windows-code-signing` (PR #944). Next: adopt the shared workflow v1.1.0 and merge with `ALLOW_UNSIGNED_RELEASES=true`; signing is activated later by the twin's E5 cutover.** Scope: the `mcp-binaries` and
> `mcp-release-complete` jobs of `.github/workflows/release.yml`, one new caller job and one new
> attach job, `.github/scripts/archive-carries.sh` (zip support), the owner's GitHub environment here.
> No product code. Twin of `dew_flow_creds_for_devs · todo/PLAN_windows_code_signing.md`, which owns
> the shared design: the reusable workflow `oleksandrdubyna88/dew_flow_release_signing ·
> .github/workflows/sign-windows.yml`, the switch and the cutover (its §3.4, E3–E5), the provider
> setup (its §4) and the facts F1–F14. Findings that
> matter are carried over below rather than linked.

## Plan changes

> Epoch 1 · approved 2026-11-03 · by reviewers · state: stopped

  (The approving round's accepted findings 0, 1, 2, 4 and 5 are part of the approved text, not changes of it.)

- 1.1 · 2026-11-03 · reviewer · contract — the twin's re-review made the shared workflow sign only declared files: coai passes its libraries in `extra-files`, and an undeclared unsigned PE is red
- 1.2 · 2026-11-04 · owner · merge-conditions, contract, approach — merge before any signing provider exists, behind the shared workflow's explicit `allow-unsigned-release` exception driven by the repository variable ALLOW_UNSIGNED_RELEASES (the twin's epoch 3)
  (This second change stopped the plan.)

### Re-review for epoch 2

Narrowed: yes — the merge no longer waits for a provider; activation moved to the twin's E5 cutover

| Work | Where | Verdict | Action |
|---|---|---|---|
| code and tests: 14c0ae22, cae8383a, 980ec6e0, 43c67837, 24433f99, 73eb7769, 0f03123d, 373ef7b6, c116c396, 08c8cde1 | pushed, unmerged | fits | kept; the switch line and the v1.1.0 pin are added |
| claim text: a38e9b38 / 3bf899ea (research/architecture.md) and the release.yml comment that a tag without signing fails | pushed, unmerged | cut | fix forward: reworded in new commits, then the squash merge |
| this plan (4d2d6652 … e763d81b) | pushed, unmerged | fits | kept; superseded by this revision |

  (Sorted by the Opus judge of the twin's epoch-3 re-review, which judged these rows. Consultation 64934326: the
  changelog guard checks version headings only, so interim release prose must say the Windows package is unsigned.)

## 1. The symptom and the goal

**Symptom.** On Windows 11 with Smart App Control (SAC) on, the extension's *Install the MCP server*
unpacks `coai-mcp-<v>-win-<arch>.zip`, and SAC blocks the unsigned `coai-mcp.exe`. SAC also blocks
unsigned **DLLs** a process loads, and this package ships nine native libraries beside the binary —
`e_sqlite3.dll`, `tree-sitter.dll` and seven grammars, all unsigned (seen in the skip-mode trial,
run 41659084165: `to sign: 10, kept: 0`). A signed `coai-mcp.exe` alone would start and then fail on its
first database write.

**Goal.** Once a signing provider is active, every `mcp-v*` release carries `win-x64` and `win-arm64`
zips in which `coai-mcp.exe` and every library is signed by the owner's organization (D6, 2026-11-03),
and a tag whose Windows package cannot be signed fails and stays a draft. The signing is the shared
workflow's — this repository writes none of it.

**Now (owner, 2026-11-04, change 1.2).** No provider exists yet (Azure not set up; SignPath Foundation
asked for reputation references). The wiring merges anyway, behind the shared workflow's
`allow-unsigned-release` exception: while the repository variable `ALLOW_UNSIGNED_RELEASES` is `true`
and none of the six signing variables is set, a tag packs the Windows zips **unsigned, exactly as
today**, with a warning and a job-summary banner. The goal above is reached only by the twin's E5
cutover. Several mcp releases will ship unsigned before it — the mcp line releases often.

**Out of scope.** `coai-server` and `coai-bugs` Windows binaries (`release.yml:619`, `:1047`): the
owner asked for the MCP binary. The same call fits those jobs later. macOS/Linux: see the twin's §1.

## 2. Facts carried over from the twin (checked there 2026-11-03/04)

- The signing action does not run on Windows ARM runners, so the shared workflow signs both
  architectures on `windows-2025` (twin F3).
- Federation needs an exact subject; this repository emits the immutable one, prefix
  `repo:oleksandrdubyna88@71817001/dew_flow_connect_other_ais@1352342534`, so the trusted subject is that
  prefix + `:environment:release-signing` (twin F6, F7).
- In a reusable workflow the environment and the federated subject are the CALLER's (twin F8); a `with:`
  of the call reads REPOSITORY variables, never the environment's (twin F12).
- v1.0.0 cannot let a tag ship unsigned; v1.1.0's input can, only with zero signing variables (twin F14,
  §3.4).
- release-please here skips a commit that touches only `.github/**` (`release-please-config.json:65`), and
  a `ci:` commit is never releasable. The mcp line is GUARDED: `changelog-names-the-release.mjs` refuses an
  mcp release that `src_vs_code/CHANGELOG.md` does not name — it checks the heading, NOT what the prose
  claims, and the prose becomes the release notes (consultation 64934326).

## 3. Design

**The build leg** (`mcp-binaries`), Windows RIDs only: `Package` builds `unsigned/coai-mcp-$VERSION-$RID/`
with the binary, `e_sqlite3` and the grammars, and runs both carry checks on a listing of that folder
(prefix `NAME/` included); no zip, no checksum, no attach on Windows legs; `unsigned/` is uploaded as
`unsigned-mcp-<rid>`, `retention-days: 7`. Non-Windows RIDs are untouched.

**`mcp-sign`** — `needs: mcp-binaries`, `if: ${{ !cancelled() && needs.mcp-binaries.result == 'success' }}`
(a bare result test would get an implicit `success()` over ALL upstream jobs, and the skipped `mcp-draft` of
a dispatch would skip it — actions/runner#491), `permissions: { contents: read, id-token: write }` and
nothing else:

```yaml
uses: oleksandrdubyna88/dew_flow_release_signing/.github/workflows/sign-windows.yml@<v1.1.0 merge commit> # v1.1.0
with:
  artifacts: '["mcp-win-x64","mcp-win-arm64"]'
  own-files: '["coai-mcp.exe"]'
  extra-files: '["e_sqlite3.dll","tree-sitter*.dll"]'
  required: ${{ startsWith(github.ref, 'refs/tags/') }}
  allow-unsigned-release: ${{ vars.ALLOW_UNSIGNED_RELEASES == 'true' }}
```

The globs are the shipped names (the count check against `shared/kept-grammars.txt` ties them to the
list). The exception is a permission for MISSING configuration: with all six variables set it signs
whatever the variable says; a partial configuration is red; an absent or misspelled variable means
enforcement.

**`mcp-sign-attach`** — `needs: mcp-sign`, `if: ${{ !cancelled() && needs.mcp-sign.result == 'success' }}`,
`ubuntu-latest`, `contents: write`: downloads `mcp-win-*`, runs `archive-carries.sh <zip> coai-mcp.exe
'*e_sqlite3*'` on each FINAL zip, then, only on an `mcp-v` tag, `gh release upload … --clobber` of the zip
and its `.sha256`.

**`mcp-release-complete`** (tag only) — `needs: [mcp-binaries, mcp-sign-attach]`; its
`verify-and-publish-release.sh` stays the last word on completeness.

**Docs** say what is true during the switch-on period: the release is WIRED for signing, and the Windows
package stays unsigned until the owner activates a provider. Reworded: `research/architecture.md` (the
section added by a38e9b38 / 3bf899ea) and the `release.yml` comments that say a tag without signing fails.

**Releases during the switch-on period.** Each mcp release PR still carries its exact-version
`src_vs_code/CHANGELOG.md` section, as the guard requires; its prose must not claim a signature — where it
mentions Windows, it says the package is unsigned. The signing announcement belongs to the first release
after the cutover.

**Merge.** #944 is squash-merged with a `ci(release): …` title and a clean body: nothing in it is a
release trigger.

## 4. Build order

1. Wait for the shared workflow v1.1.0 (twin E3).
2. This PR: pin bump to v1.1.0's merge commit, the switch line, the structure test extended, the docs
   reworded; gates; CI.
3. At the merge: the repository variable `ALLOW_UNSIGNED_RELEASES=true` (twin M1); the `release-signing`
   environment holds no variables (twin M2; its deployment rule `main` + `ci/windows-code-signing` +
   `mcp-v*` is set); squash merge; O9 removes this branch from the rule.
4. Later — the twin's E5 cutover: pause mcp releases; the provider and the six variables at once; V1 on
   `main` (`target: mcp`); V2 on the owner's SAC machine; A6 deletes the variable AND removes the
   `allow-unsigned-release` line from `mcp-sign` with a test that fails if any job passes it; the next real
   `src_mcp` change cuts the first signed release, its prose announcing the signature; V5 and V3.

## 5. Test and verification plan

- `archive-carries.sh` with a `.zip`, in `TheArchiveCheckTests.cs` (done: RED 9/13 → GREEN 13/13).
- `windowsPackageIsSigned.test.ts` (done: Windows legs upload `unsigned-mcp-<rid>` and do not attach; the
  two `!cancelled()` gates; the tag-scoped upload; `mcp-release-complete` needs `mcp-sign-attach`; no other
  job has `id-token`). Extended now: the pin is the v1.1.0 merge commit; `allow-unsigned-release` is exactly
  `${{ vars.ALLOW_UNSIGNED_RELEASES == 'true' }}`. Break-it: write it as a bare string → the test fails.
- A docs grep before the merge: no present-tense signature or SAC claim, and no "a tag without signing
  fails" claim, in `research/architecture.md` or the `release.yml` comments.
- The whole extension suite, actionlint, `plan-lifecycle.mjs`, `pin-check.mjs`.
- The exception itself (zero / partial / full configuration with the permission on, a tag ref) is proven by
  the shared workflow's own tests (twin E3); this repository adds a skip-mode dispatch trial after the pin
  bump.
- **At the cutover (twin E5):** V1 — the signed dispatch trial on `main`: `coai-mcp.exe` and every DLL
  `Valid`, signer = the organization, timestamped; V2 — the `win-x64` trial package on the owner's SAC
  machine: `coai-mcp.exe --version`, `--log` (opens the rounds database, loads `e_sqlite3.dll`),
  `--normalize` on a small file (loads a grammar); V5 — the PUBLISHED assets of the first signed release
  downloaded and checked (checksums, signatures); V3 — *Install the MCP server* and one real review round.

## 6. Growth surfaces

None that grows on its own: two `unsigned-mcp-*` and two `mcp-win-*` artifacts per run, a few tens of MB
each, `retention-days: 7`.

## 7. Definition of Done

**Wiring (this PR):**
- [ ] v1.1.0 pinned, the switch line in, the structure test green with its break-it; the docs grep empty;
      the whole suite and actionlint green; `coai` gate rounds resolved.
- [ ] `ALLOW_UNSIGNED_RELEASES=true` set; the environment holds no variables; #944 squash-merged with a
      `ci(release)` title; O9 done.

**Activation (the twin's E5 — the goal; this plan stays in `todo/` until ticked):**
- [ ] V1 and V2 observed; A6 done in settings and in code; the first signed mcp release out, its prose
      announcing the signature; V5 and V3 observed.
- [ ] Plan promoted to `research/` with its deviations.
