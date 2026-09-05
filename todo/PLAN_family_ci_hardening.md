# PLAN — CI hardening across the dew_flow family: formatting gates, supply chain, PR culture, releases

> Status: **plan only, nothing implemented yet — except the repository settings, applied 2026-09-05 and
> listed below as done.** Scope: every `dew_flow_*` repository's `.github/` (workflows, dependabot,
> PR template), `.editorconfig` where missing, and `dew_flow_conventions` for the rule that binds them.
>
> Related docs: [research/PLAN_local_db.md](../research/PLAN_local_db.md) (unrelated, same week);
> `dew_flow_conventions/common/pull-requests.md` (the rule this plan enforces mechanically);
> [module_bench.md](../research/module_bench.md) — the bench stays out of every release.

## The ask, 2026-09-05

The operator connected CodeRabbit to the three public repositories, closed `main` to direct pushes,
and then listed what a serious repository has beside a reviewer: formatting checked in CI without
auto-formatting; secret scanning with push protection; Dependabot; a PR template and semantic PR
titles; release-please; and a branch ruleset that requires every check, an up-to-date branch, and no
bypass for admins. *"Вот это всё тоже включи и настрой для всех дев флоу проектов."*

## The family, audited 2026-09-05

| repo | stack | CI today | formatting check | dependabot | PR template | notes |
|---|---|---|---|---|---|---|
| `dew_flow_connect_other_ais` | .NET 10 AOT + TS extension + .NET bench | build·test·family checks, extension | none | none | none | public, CodeRabbit |
| `dew_flow_conventions` | markdown + node tools | **none until PR #1** (tools selftest) | none | none | none | public, CodeRabbit |
| `dew_flow_creds_for_devs` | .NET 10 server + TS extension + .NET cli/mcp/broker | server, extension (path-filtered), docs | eslint (extension only) | none | none | public, CodeRabbit; **cli/mcp/broker tests never run in CI** |
| `dew_flow_rag_qln` | .NET 10 + TS extension | build-test, contract (postgres), extension, plans | none | none | none | **private** — secret scanning unavailable without Advanced Security |
| `dew_flow_mcp` | .NET 10 | build-test, contract, plans | none | none | none | public |
| `dew_flow_benchmark` | .NET 10 | build-test, contract (postgres), plans | none | none | none | public |
| `dew_flow_sidecar_rust` | Rust | build-test (matrix), contract, plans | `cargo fmt --check` ✓ | none | none | public |

No `.editorconfig` anywhere; no Python (ruff is not applicable).

## Done already — repository settings, via the API (2026-09-05)

- **Branch protection on `main`** (classic protection; the same flags a ruleset carries): pull request
  required with zero approvals (one human, who cannot approve their own PR), `enforce_admins`,
  required linear history, force-push and deletion blocked, **required conversation resolution**,
  **required status checks with `strict: true`** (the branch must be up to date with `main`).
  Applied to `connect_other_ais`, `conventions`, `creds_for_devs`; the other four in epic 1.
- **Merge methods**: merge commits off; rebase and squash on; delete branch on merge; auto-merge allowed.
- **Secret scanning + push protection**: enabled on every public repository. `dew_flow_rag_qln` is
  private and GitHub refuses it there (422) — it needs Advanced Security or the repository made public.
- **Dependabot alerts + automated security fixes**: enabled on all seven.

## What must be true when this is done

1. Every repository's CI fails a PR whose code is not formatted to the repository's rules, without
   reformatting anything: `dotnet format --verify-no-changes` (with an `.editorconfig` that states the
   rules, committed once with the one-time reformat), `eslint` for every TS package, `cargo fmt --check`
   (already), `actionlint` for workflows.
2. Every runnable test project runs on every PR — including `creds_for_devs`' cli, mcp and broker
   tests, which have never run in CI — and the path filters that let a PR skip a job are gone from the
   `pull_request` triggers, so every job can be a **required** check.
3. Dependabot version updates: `.github/dependabot.yml` per repository, grouped (NuGet minor+patch in
   one PR, npm in one, cargo in one, github-actions in one), weekly, with the family's pinned
   exceptions respected (FluentAssertions stays 7.2.2; Aspire SDK and hosting bump as a pair).
   Renovate is not added: Dependabot with groups covers the ask without a second bot to configure.
4. A PR template with the checklist the rules already demand: what changed, the red-green test, the
   docs/README/manifest touched, the DoD of the rule the change is under, what the reviewer said that
   was not acted on and why.
5. Semantic PR titles enforced by `amannn/action-semantic-pull-request` (types: feat, fix, docs, test,
   chore, refactor, perf, ci, build) — a required check.
6. **release-please in manifest mode**, one component per released artefact with the tag shapes the
   release workflows already trigger on (`mcp-v*` / `extension-v*` in `connect_other_ais`; `server-v*`
   and the extension in `creds_for_devs`; `v*` in the sidecar): it opens a release PR that bumps the
   manifest (and `package.json` for the extensions), and on merge cuts the tag that the existing
   release workflow builds. **The narrative `CHANGELOG.md` stays hand-written**; release-please writes
   its generated notes to `RELEASES.md` (`changelog-path`), because a changelog assembled from PR
   titles is the record this family deliberately does not keep.
7. The branch protection on all seven repositories requires every job above.
8. `dew_flow_conventions` carries the rule (`common/pull-requests.md`, already in PR #1) and a tool,
   `tools/repo-settings-check.mjs`, that reads a repository's protection and settings through `gh api`
   and fails when a flag above is off — so the settings cannot drift back silently.

## Epics

### Epic 1 — every PR runs every test, and the settings cover every repo

1. `creds_for_devs`: `ci-clients.yml` for cli/mcp/broker tests; `paths` filters removed from the
   `pull_request` triggers of `ci-server.yml` and `ci-extension.yml`; protection requires all jobs.
2. Protection + merge settings on `rag_qln`, `mcp`, `benchmark`, `sidecar_rust`, requiring their
   existing jobs (contract jobs included — they run on every PR with their own services).
3. `tools/repo-settings-check.mjs` in conventions, with its selftest; run by hand for now (it needs a
   token), documented in the README.

### Epic 2 — formatting gates

Per .NET repository: an `.editorconfig` encoding the rules `CLAUDE.md` already states (file-scoped
namespaces, `var`, expression bodies, records) — then ONE reformat commit (`dotnet format`), then the
`--verify-no-changes` step. Per TS package: eslint where missing (flat config, typescript-eslint,
recommended + no-floating-promises), then the `lint` step. `actionlint` everywhere.

### Epic 3 — dependabot, PR template, semantic titles

Three files per repository from one template each; the semantic-title workflow made a required check.

### Epic 4 — release-please

`release-please-config.json` + `.release-please-manifest.json` per repository that releases, the
`release-please.yml` workflow (`googleapis/release-please-action@v4`), `changelog-path: RELEASES.md`,
`include-component-in-tag: true`, `tag-separator: "-"` so the tags are `mcp-v0.17.4`. Measured on one
release before the others adopt it: the tag it cuts must trigger the existing release workflow and
produce the same artefacts.

## Test plan

Each epic lands as a PR per repository; the PR is the test — CI, CodeRabbit, and the protection
itself refusing what it should. `repo-settings-check.mjs` has fixtures for a compliant and a drifted
repository. Release-please is verified on `dew_flow_mcp` first (smallest release surface).

## Definition of Done

- [ ] Every job of every repository is a required check, and no PR can skip one.
- [ ] `dotnet format --verify-no-changes` / eslint / cargo fmt / actionlint fail a badly formatted PR.
- [ ] `dependabot.yml`, the PR template and the semantic-title check exist in every repository.
- [ ] release-please cuts the tags the release workflows already build from, with the narrative
      changelog untouched.
- [ ] `repo-settings-check.mjs` passes against all seven repositories.
- [ ] This plan promoted with what shipped differently.
