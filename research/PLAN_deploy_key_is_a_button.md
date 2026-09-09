# PLAN — the deploy key is an update button, not root

> Status: **IMPLEMENTED, 2026-09-09.** Scope: a forced-command wrapper on the Team-server
> host, `.github/workflows/deploy-server.yml`, the file mode of `deploy/systemd-release.sh`, and the
> tests in `src_vs_code/src/test/install.test.ts` that hold the workflow to its promises.
>
> Related docs: [deploy/README.md](../deploy/README.md),
> [PLAN_the_server_has_a_release_line.md](PLAN_the_server_has_a_release_line.md).

## The symptom

`server-v0.5.5` was deployed through `deploy-server.yml` on 2026-09-08. It took two runs, and the
first one failed in a way that was not a fluke:

```
live:     /opt/coai/releases/0.5.5-STAMP
release: unknown option '--from' — usage: systemd-release.sh <version> | --rollback | --list
```

The host's checkout of the release script was three commits behind the workflow that invokes it, and
nothing in the system had any opinion about that. Two more defects were found while getting the same
deploy to pass:

1. **`deploy/systemd-release.sh` is committed `100644`.** The workflow invokes it directly over ssh,
   so on any host whose checkout is fresh the very first deploy fails with `permission denied`. It
   worked here only because it was `chmod +x`-ed by hand — twice, because `git checkout` put the mode
   back.
2. **The CI key is unrestricted root.** `COAI_DEPLOY_KEY` opens a full root shell on the box that
   holds the company's whole AI subscription. Nothing about a deploy needs that.

## What the sibling already does

`dew_flow_creds_for_devs` deploys to the SAME host and has none of these three problems, because its
key cannot do anything except deploy (`.github/workflows/rsd-server-deploy.yml`,
`/root/rsd-deploy-cmd.sh` on the host):

```
authorized_keys:  restrict,command="/root/rsd-deploy-cmd.sh"  gh-actions-rsd-deploy
```

```sh
case "$CMD" in
  "deploy --rollback") ARG="--rollback" ;;
  deploy\ *)  ARG="${CMD#deploy }"
              printf %s "$ARG" | grep -Eq "^[0-9A-Za-z._-]{1,40}$" || exit 90 ;;
  *)          echo "refused: this key only accepts 'deploy <version>' or 'deploy --rollback'"; exit 90 ;;
esac
cd "$DEPLOY"; [ "$PULL" = 1 ] && git pull --ff-only
exec ./update.sh "$ARG"
```

Its own comment states the model: **"a leaked key is an update button, not root"**. The staleness
problem is solved on the side that holds the truth — the wrapper pulls before it runs — and the
deploy path lives on the server rather than as an absolute string in a workflow.

This plan brings ConnectOtherAIs to that model. It is not a new design; it is the one already running
on this host for the other product.

## What must be true when it is done

1. The `coai-deploy-ci` key is `restrict,command=…` and can start **nothing** but the wrapper — no
   shell, no pty, no forwarding, no `scp`.
2. The wrapper accepts exactly three shapes and refuses everything else with a non-zero exit:
   `deploy <version>`, `deploy --rollback`, `health`. An optional trailing ` nopull` suppresses the
   pull, as CredsForDevs spells it.
3. The version is validated against `^[0-9A-Za-z._-]{1,40}$` **on the server**, before it reaches any
   command line.
4. The wrapper refreshes `/opt/coai/src` with `git pull --ff-only` before running anything, so a host
   whose checkout has drifted repairs itself rather than failing on an option it has never heard of.
5. The artefact is the PUBLISHED release, fetched by the wrapper from the release the version names —
   `scp` is impossible under a forced command, and downloading what was published is the more honest
   artefact anyway: the runner's copy is one hop further from the thing users can verify.
6. A version with no published `linux-x64` asset is refused by the wrapper with a message naming what
   is missing, before anything is unpacked or swapped.
7. `COAI_TOKEN_FILE` is set by the wrapper, not passed by the client — the canary's token path is a
   fact about the host.
8. The workflow's loopback probe survives: it asks the wrapper's `health` verb instead of running a
   `curl` in a root shell, so a failed deploy can still be told apart into "the unit is wrong" and
   "the edge is wrong".
9. `deploy/systemd-release.sh` is committed executable (`100755`).
10. The rollback path still works, from the workflow and by hand.

## The change

### 1. `deploy/coai-deploy-cmd.sh` — new, in the repository

The wrapper lives in the repo and the forced command names it **in the checkout**
(`/opt/coai/src/deploy/coai-deploy-cmd.sh`), so it is reviewable, versioned, and refreshed by the same
pull that refreshes everything else. It is not copied to `/root`: see deviation 1 below — that copy
was in this plan as written, and it was the plan's own defect. Modelled line for line on
`rsd-deploy-cmd.sh`, with two differences the artefact-based deploy forces:

- it downloads `coai-server-<version>-linux-x64.tar.gz` from the `server-v<version>` release and
  verifies the published `.sha256` beside it;
- it hands that archive to `systemd-release.sh --from`, which already inspects the artefact and
  asserts the binary reports the version being deployed.

`deploy/README.md` gains the install line (`install -m 700 deploy/coai-deploy-cmd.sh /root/` plus the
`authorized_keys` option string) so the next host is set up from the document rather than from memory.

### 2. `.github/workflows/deploy-server.yml`

- The deploy step becomes one `ssh … "deploy $VERSION"`. The `scp`, the staging directory, the
  `--list` round trip and the `env COAI_TOKEN_FILE=…` all go: each was a thing the client was trusted
  to get right, and none of them can cross a forced command.
- The preflight keeps its job — refusing a version whose release is incomplete before an approval is
  spent — and gains nothing, because the wrapper's refusal is the second line of the same defence.
- The verification step's loopback probe changes from `ssh … 'curl …'` to `ssh … 'health'`.
- `HOST_RID` moves to the wrapper: the RID the host runs is a fact about the host.

### 3. The key

A new restricted line replaces the unrestricted `coai-deploy-ci` in `authorized_keys`, and
`COAI_DEPLOY_KEY` is re-issued for it. The old key is removed in the same pass — leaving an
unrestricted root key behind because a better one now exists is the failure this plan is about.

### 4. The file mode

`git update-index --chmod=+x deploy/systemd-release.sh`, and the same for the new wrapper.

## Constraints

- **No behaviour change to `systemd-release.sh` itself.** It already does the hard part — the release
  trail, the one-syscall swap, the per-vendor canary, the rollback — and this plan only changes who is
  allowed to call it and with what.
- **The wrapper is `/bin/sh`, not bash**, like its sibling: it runs as a forced command on a box where
  the login shell is not something to depend on.
- **Nothing in the workflow may name an absolute path on the host.** That is the coupling that broke
  the first deploy.
- The workflow stays dispatch-only: cutting a release and choosing to interrupt live reviews are the
  same person's decisions but not the same decision.
- `install.test.ts` asserts the workflow's promises; those assertions are updated to the new shape
  rather than deleted, and the strongest of them — "every value that crosses into the remote root
  shell is validated first" — becomes stronger, because under a forced command nothing crosses into a
  root shell at all.

## Test plan

- **RED first:** a test asserting the workflow sends a fixed verb rather than an absolute host path
  fails against today's file.
- The wrapper's refusals are covered by a shell test over its `case` block: `deploy 0.5.5`,
  `deploy --rollback`, `health`, ` nopull`, and the refusals — `rm -rf /`, `deploy ; rm -rf /`,
  `deploy ../../etc`, an empty command, a 41-character version.
- `install.test.ts`: the workflow names no `/opt/` path; it passes only a validated version; the
  verification step still runs on failure; the rollback step still exists.
- A mode test: `deploy/systemd-release.sh` and `deploy/coai-deploy-cmd.sh` are `100755` in git — this
  is the defect that cost the first real deploy, and a comment cannot hold it.
- Both suites in full.
- **A real deploy** of `0.5.5` through the new path, on the live host, verified by
  `readlink /opt/coai/bin` and `/api/health` from the internet.

## Definition of Done

- [ ] The key is `restrict,command=` and refuses everything but the three verbs.
- [ ] The wrapper pulls before it runs and downloads the published artefact, checksum verified.
- [ ] The workflow names no absolute host path and passes only a validated version.
- [ ] The loopback probe survives as a verb.
- [ ] Both scripts are committed executable.
- [ ] The old unrestricted key is gone from the host.
- [ ] Documentation updated: `deploy/README.md`, `research/module_team_server.md`, this plan promoted
      with its deviations, `research/README.md`. **No CHANGELOG entry**: nothing users install
      changed — the reasoning is recorded under *What shipped differently*.
- [ ] Both suites green, and a real deploy done through the new path.

## What shipped differently

The plan round returned eighteen findings and every one was accepted. Four changed the design:

1. **The forced command names the file in the CHECKOUT**, not a copy under `/root`. The plan had the
   wrapper installed to `/root/coai-deploy-cmd.sh` while the pull refreshed `/opt/coai/src` — so a
   fix to the wrapper would never have reached the host. That is the same defect the plan was written
   about, one level up, and two reviewers caught it independently.
2. **A dirty working tree is repaired, not merged around.** `git pull --ff-only` aborts on local
   modifications, and this host had two — a hand `chmod +x` and a submodule pointer. Left alone, the
   first deploy after this change would have failed on the working tree. Tracked modifications are
   discarded; untracked files are not touched.
3. **`--rollback` skips the download entirely.** The plan described the wrapper fetching an artefact
   for "any deploy invocation", which would have made a rollback demand a version it does not have.
4. **Every network call is bounded** (`timeout`, `curl --retry`), and the deploy path does not `exec`
   — the staging directory is removed by a trap and the release script's status is carried out by
   hand.

Smaller ones taken: the version grammar strips ` nopull` before the version token is parsed and
validated; `sha256sum -c` checks the recorded basename as well as the digest, and fails closed; the
`health` verb answers with the unit's own body, version and all, so the workflow can compare it; the
workflow gained a `concurrency` group so two deploys queue rather than race the release trail.

Two things the plan asked for that did **not** ship as written:

- **`HOST_RID` stayed in the workflow.** The plan moved it to the wrapper, and the wrapper does own
  the RID it deploys — but the preflight needs a RID to check the release against *before* an
  approval is spent, and asking the host for it would mean an ssh round trip outside the environment
  that guards ssh. The workflow's copy is an early warning; the wrapper's is the decision.
- **No CHANGELOG entry and no version bump.** Nothing users install changed: this is the deploy path,
  and the products it deploys are unmoved.

## The boundary with PLAN_the_server_has_a_release_line.md

That plan built the release LINE — the six Native AOT builds, the GitHub Release, the images, and
the first version of this workflow. This one changes only who may call the host and how.

| item | built by | the other one's part |
|---|---|---|
| `server-v*` builds the six RIDs and publishes a Release | the release line | this plan consumes one asset of it, and refuses a version that has none |
| `deploy-server.yml` exists, dispatch-only, approval-gated | the release line | this plan rewrites its remote half into one verb; the gate, the preflight and the verification are unchanged in purpose |
| `systemd-release.sh` — trail, swap, canary, rollback | the release line | **disjoint**: this plan changes none of its behaviour, only its caller |
| the ssh key's authority on the host | **this plan** | the release line assumed an unrestricted account and said nothing about it |
| the host's checkout being current | **this plan** | the release line's workflow assumed it, which is the defect this plan was written after |

Order: the release line had to exist first — there is nothing to deploy without a published
artefact, and this plan's wrapper downloads one.

## What this makes grow, and who empties it

| surface | projected size | who retires it | interrupted |
|---|---|---|---|
| `/root/.coai-deploy.XXXXXX` — one per deploy | one archive, ~30 MB | the wrapper's `trap … EXIT INT TERM HUP` | HUP is in the list because sshd sends it when a runner is cancelled, and an untrapped HUP kills a POSIX shell without running its EXIT trap. A `SIGKILL` or a power cut still strands one directory; they are `mktemp` names under `/root`, visible as `.coai-deploy.*`, and the next operator on the box can remove them. |
| `/opt/coai/releases/<version>-<stamp>` | one release directory per deploy, ~60 MB | `systemd-release.sh`, which retains the last three | unchanged by this plan — the trail and its pruning are the release script's, and this plan does not touch its behaviour |

## The open tail

- ~~The old unrestricted key is removed only after a real deploy has passed through the restricted
  one.~~ **Done, 2026-09-09.** It was not replaced: the same key already in `COAI_DEPLOY_KEY` was
  given the `restrict,command=` prefix in place, which reaches the goal — that key can no longer open
  a root shell — without any private-key material moving anywhere. `authorized_keys` was backed up to
  `authorized_keys.before-coai-lockdown` first, and the proof is a real deploy of `0.5.5` through the
  new path afterwards: run 34329776612, every step green, the rollback step skipped because nothing
  needed it.

  What that run showed, in its own words: the wrapper found `.claude/rules/shared` modified and said
  so before discarding it, refreshed the checkout to `56831d3`, fetched the archive, reported
  `sha256 verified`, and handed it to the release script — which canaried one real review per vendor.
  `bin` moved to `0.5.5-20260909T085527Z`, the trail kept the previous three, loopback and the public
  URL both answered `{"ok":true,"version":"0.5.5"}`, and no staging directory was left behind.
- The wrapper is not covered by a shell test suite of its own; its refusals are executed by
  `install.test.ts` through `sh`, which is the boundary that matters, but the accept paths are
  asserted only as text because running them would deploy something.
