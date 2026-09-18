# PLAN — release-please, and the narrative changelog it would write over

> Status: **partially implemented, 2026-09-18 — the decision is taken and what remains is not
> scriptable.** The operator chose a GitHub App over a PAT; `release-please.yml` mints an
> installation token and refuses in words until the secrets exist. Creating and installing a GitHub
> App cannot be done without a browser, so the next move is two browser steps, written out below. Scope: Epic 4 of
> [PLAN_family_ci_hardening.md](PLAN_family_ci_hardening.md) — `release-please` in the three
> repositories that release. Blocks Epic 5 step 3, which is the step that actually closes CWE-522.
>
> Related docs: [PLAN_family_ci_hardening.md](PLAN_family_ci_hardening.md) (Epic 4 and Epic 5),
> [research/PLAN_a_release_says_what_it_shipped.md](../research/PLAN_a_release_says_what_it_shipped.md)
> (the guard this collides with).

## The goal, and the thing that was not looked at first

Epic 4 says: `release-please-config.json` + `.release-please-manifest.json` per releasing repository,
a `release-please.yml` workflow, `changelog-path: RELEASES.md`, `include-component-in-tag: true`,
`tag-separator: "-"` so the tags are `mcp-v0.17.4`.

What it does not say is what release-please would be writing INTO, and that turns out to decide most
of the work. Measured 2026-09-18, before anything was configured.

## The three subjects, measured

Epic 5 step 0 established that three of the seven repositories release. They are not alike:

| repository | tag patterns | products | changelog today |
|---|---|---|---|
| `connect_other_ais` | `mcp-v*` `extension-v*` `server-v*` `bugs-v*` | **four** | `src_vs_code/CHANGELOG.md` — **122 entries of hand-written prose**, one narrative paragraph per release |
| `creds_for_devs` | `server-v*` `extension-v*` `cli-v*` `mcp-v*` | four | **none** |
| `sidecar_rust` | **`v*`** | one | **none** |

`creds_for_devs` and `sidecar_rust` have nothing to collide with: release-please would create a
changelog where there is none. `connect_other_ais` is the whole of the difficulty.

## The collision, measured

`connect_other_ais` ships a guard — `.github/scripts/changelog-names-the-release.mjs` — that refuses
a release whose version is not NAMED in the changelog. It recognises exactly two heading shapes
(`namesTheRelease`, line 98):

```
^## (?:[^\r\n]*? · )?<Word> <version>(?=\s|$)      bare or joint
^## [^\r\n]*\(<word> <version>\)                   parenthesised
```

A release-please heading is neither. Its default is `## [0.29.0](compare-link) (2026-09-18)`, which
carries no product word at all — and the whole point of the word is that this repository releases
four products from one changelog.

**But the collision is NARROWER than it first looks, and that is the useful measurement.** `LINES`
(line 64) marks only ONE of the four as enforced:

| tag line | word | `guarded` |
|---|---|---|
| `mcp-v` | `Server` | **true** |
| `extension-v` | `Extension` | false |
| `server-v` | `Team server` | false |
| `bugs-v` | — | false |

So today a release-please changelog would break exactly one release line, not four. That is a fact
with a shelf life: the other three are unguarded because nobody has turned them on yet, and a design
that only works while three guards are off is a design that breaks the day somebody finishes the job.

## The second collision, which is about people rather than files

Even with the changelog question settled, release-please changes WHO decides a version. Its flow is:
it opens a release pull request, and merging that pull request cuts the tag. The guard requires the
narrative entry to exist by then — so either

* the prose is written BEFORE the release pull request merges (the guard stays meaningful, and the
  release pull request is a checkpoint rather than a button), or
* the tag is cut and the release job fails on the guard, which is the worst of both.

This is not an argument against release-please. It is the thing to decide deliberately, because it is
the difference between a guard that shapes the workflow and a guard that ambushes it.

## The options

| | what release-please writes | what happens to the narrative | cost |
|---|---|---|---|
| **A — `RELEASES.md`, narrative untouched** | a generated `RELEASES.md` per component | stays exactly as it is, still the human record, still what the guard reads | two changelogs; a reader has to be told which is which |
| **B — teach the guard release-please's shape** | `src_vs_code/CHANGELOG.md`, in its own format | **destroyed** — 122 paragraphs become bullet lists of commit subjects | the guard survives; the thing it was guarding does not |
| **C — release-please for versioning only** (`skip-changelog`) | nothing | untouched | loses the epic's changelog half; keeps the tagging half, which is what Epic 5 step 3 actually needs |
| **D — adopt in the two repositories with no changelog first** | creates one in each | not applicable | proves the mechanism on `creds_for_devs` and `sidecar_rust` before touching the hard case |

**Recommendation: D, then A.** D is where the epic's own acceptance test belongs — *"measured on one
release before the others adopt it"* — and it costs nothing to get wrong twice. A is then the shape
for `connect_other_ais`, and `changelog-path: RELEASES.md` is what Epic 4 already says, which reads
like its author had seen this coming.

**B is the one to say no to out loud.** The 122 paragraphs are the most valuable artefact in the
release path: they are why anybody can tell what a version changed. A generated list of commit
subjects is not a cheaper version of that, it is a different thing that happens to live at the same
path.

## The sidecar's tag shape — the decision Epic 4 asks for

`dew_flow_sidecar_rust` releases on `v*`; `include-component-in-tag: true` would cut
`sidecar-v0.1.3`, which its own workflow does not trigger on, and the release would silently produce
nothing.

**Recommendation: `include-component-in-tag: false` for the sidecar**, as a per-repository setting
rather than a family rule. It ships ONE product, so a component prefix distinguishes it from nothing;
the prefix exists in the other two because four products share a tag namespace. Changing the
sidecar's trigger instead would mean a release workflow edit, a tag-shape change and a protected-tag
pattern change, for consistency nobody reads.

## The blocker, measured 2026-09-18 — and it is not a tag-shape question at all

Everything below assumes the tag release-please cuts starts the release workflow already in the
repository. **With the default token it does not.** From the action's own README:

> By default, Release Please uses the built-in `GITHUB_TOKEN` secret. However, all resources created
> by `release-please` (release tag or release pull request) **will not trigger future GitHub actions
> workflows**, and workflows normally triggered by `release.created` events will also not run.

GitHub's own documentation gives the reason — events from `GITHUB_TOKEN` create no workflow run, to
prevent recursion. So the acceptance test below would fail for a reason that has nothing to do with
the tag's shape: the release would produce **nothing, silently**, which is the failure this plan was
written to avoid.

**What it needs is a decision, not work.** Checked 2026-09-18: no repository in this family holds a
token that would do.

| | costs | buys |
|---|---|---|
| a **PAT** with `contents: write` + `pull-requests: write` | one long-lived credential to mint, rotate and guard | the shortest path; the token sits in a secret |
| a **GitHub App** installation token, minted per run | an app to create and install | short-lived and scoped — a far smaller blast radius, and the shape Epic 5 argues for everywhere else |

This is the operator's call for the same reason the CodeRabbit PAT question is, and the two should
probably be answered together. **Until it is answered, `release-please.yml` ships on
`workflow_dispatch` only** — a no-op that looks like a release is worse than no automation. Turning
it on is two lines, both written out and commented in the workflow.

### ANSWERED 2026-09-18: the GitHub App

The operator chose the app. `dew_flow_sidecar_rust`'s `release-please.yml` is wired for it —
`actions/create-github-app-token@bcd2ba4 # v3.2.0` mints a token per run, an hour long and scoped to
the installation, and `release-please-action` is handed that instead of `GITHUB_TOKEN`.

**What cannot be done from here, and it is a boundary rather than a task left undone.** Creating a
GitHub App **cannot be done without a browser**, and **installing** one cannot either. So the two
secrets below cannot be produced by scripting alone, and a workflow that pretended otherwise would
be the same class of thing this plan exists to stop: a mechanism that looks like it works and
quietly does not.

> **An earlier version of this paragraph said GitHub has "no REST endpoint that creates a GitHub
> App", and that is wrong** — corrected 2026-09-18 after review. `POST /app-manifests/{code}/conversions`
> creates one, and it is what the manifest flow calls. What it needs is the `code`, which GitHub
> hands out only by redirecting a browser that has just submitted the manifest form and had a human
> confirm it; the code expires in an hour. **The conclusion is unchanged — the flow is not
> browser-free — but the reason is the `code`, not the absence of an endpoint.** Measured: a bogus
> code on that route answers 404 with `documentation_url` = `…/rest/apps/apps#create-a-github-app-from-a-manifest`,
> while a route that genuinely does not exist answers with the generic `…/rest`. The endpoint
> recognised itself and rejected the code.

**Which makes a faster route available, and it is worth knowing before picking the form.** The
manifest can be posted from a one-file local page — `<form method="post"
action="https://github.com/settings/apps/new">` with the manifest below in a hidden input — so the
human's whole part is *click, confirm*. Set `redirect_url` to something that will not load, e.g.
`http://localhost:1/callback`: the browser shows a connection error and the address bar carries
`?code=…`, which is the one thing that has to come back. That code then goes to the conversion
endpoint, which returns the App ID and the private key in one answer.

**Weigh that against the form, honestly.** The conversion response contains the PEM, so on this
route the private key passes through whatever runs the call. Typing the two values from the
`/settings/apps/new` page into `gh secret set` keeps the key between the browser and the vault. The
faster route is not the safer one.

What the workflow does instead is **refuse in words**. Its first step reads whether the two secrets
exist — never the private key itself, only `!= ''` — and when they do not it exits 1 saying so and
naming this document, rather than letting the mint fail with a message about a malformed key, which
reads like a broken secret instead of an absent one.

#### The app, exactly

| field | value | why |
|---|---|---|
| name | `dew-flow-release-please` | |
| owner | this account, **not public** | it serves three repositories of one account |
| webhook | **disabled** | nothing listens; an app whose webhook url 404s fills its own delivery log |
| `contents` | **write** | the commits, the branch and the tag release-please creates |
| `pull_requests` | **write** | the release pull request it opens and updates |
| `issues` | **write** | the `autorelease: pending` / `autorelease: tagged` LABELS — labels live on the issues API even when they sit on a pull request, and release-please's state machine reads them back |
| `metadata` | read | mandatory for every app |

As a manifest, if the form is the slower route:

```json
{
  "name": "dew-flow-release-please",
  "url": "https://github.com/oleksandrdubyna88",
  "public": false,
  "hook_attributes": { "active": false },
  "default_events": [],
  "default_permissions": {
    "contents": "write",
    "pull_requests": "write",
    "issues": "write",
    "metadata": "read"
  }
}
```

#### The four steps

1. **Create** it — <https://github.com/settings/apps/new> — with the table above. Note the **App ID**
   shown after creation.
2. **Generate a private key** on the same page; a `.pem` downloads. It is shown once.
3. **Install** it — *Install App* → this account → **Only select repositories** →
   `dew_flow_sidecar_rust` first, then `dew_flow_creds_for_devs` and `dew_flow_connect_other_ais`
   when steps 3 and 4 of the build order reach them. *All repositories* would hand the app write
   access to four that never release.
4. **Add the secrets**, per repository:

   ```bash
   gh secret set RELEASE_PLEASE_APP_ID          -R oleksandrdubyna88/dew_flow_sidecar_rust --body '<app id>'
   gh secret set RELEASE_PLEASE_APP_PRIVATE_KEY -R oleksandrdubyna88/dew_flow_sidecar_rust < path/to/key.pem
   ```

   The `<` form matters: a multi-line PEM passed as `--body` from a Windows shell arrives mangled,
   and the failure surfaces an hour later as a token that will not mint.

Then dispatch `release-please` once and watch a release pull request appear. Only after that does
`push: branches: [main]` get uncommented — step 2 of the build order below is the acceptance test,
and it has not run yet.

## Build order

1. **`sidecar_rust` first** — one product, no changelog, `include-component-in-tag: false`. The whole
   mechanism in its simplest form. **DONE 2026-09-18**, on `workflow_dispatch` pending the token
   above. It found something on the way in: `Cargo.toml` says 0.1.0 while the tags say v0.1.0,
   v0.1.1, v0.1.2 — the source version has not moved in two releases, and nothing noticed because
   nothing reads it (`CARGO_PKG_VERSION` appears nowhere, so the binary reports no version at all).
   The manifest records 0.1.2, what was actually released, and the first release-please pull request
   will bring `Cargo.toml` into line with reality for the first time.
2. **Cut one real release through it** and compare the artefacts against the previous release, by
   name and by size. This is Epic 4's acceptance test and it is not optional: a tag that does not
   trigger the existing workflow produces nothing, silently. **BLOCKED on the two browser steps
   above** — creating and installing the app. Everything downstream of them is written and linted;
   steps 3 and 4 wait behind this one deliberately, because this plan's own recommendation is to
   measure on one release before the others adopt anything.
3. **`creds_for_devs`** — four components, `include-component-in-tag: true`, a generated changelog
   where none exists.
4. **`connect_other_ais`** — option A. `RELEASES.md` generated, `src_vs_code/CHANGELOG.md` left
   alone, and the guard's relationship to the release pull request written down in the repository's
   own docs rather than discovered.
5. **Turn on the other three `guarded` flags** once the shape is settled, because a design that only
   works while three guards are off is not settled.

## Test plan

* The action is pinned by SHA with `# v4` after it (requirement 9), and `actionlint` passes.
* `sidecar_rust`: the tag release-please cuts matches `v*` — asserted by a test that reads the
  workflow's trigger and the config's `include-component-in-tag` together, not by eye.
* `connect_other_ais`: a test that the guard still refuses a release whose narrative entry is
  missing, AFTER `RELEASES.md` exists — the failure mode being that a generated file satisfies a
  guard that was watching a different one.
* Step 2's release is compared artefact by artefact against its predecessor.

## Definition of Done

- [ ] The sidecar's tag shape is recorded as a decision, with its reason, in this document.
- [ ] One real release has been cut through release-please and its artefacts compared.
- [ ] `connect_other_ais`'s narrative changelog is untouched and still what the guard reads.
- [ ] The release pull request's relationship to the guard is documented where a releaser will meet it.
- [ ] Every `uses:` added is SHA-pinned; `actionlint` is clean.
- [ ] Epic 4's line in [PLAN_family_ci_hardening.md](PLAN_family_ci_hardening.md) points here.
