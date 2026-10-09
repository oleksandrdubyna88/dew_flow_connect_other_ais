# PLAN — the extension reaches the Marketplace through Microsoft Entra ID, not a global PAT, before 2026-12-01

> Status: **plan only, nothing implemented yet — 2026-10-09.** Scope: the Marketplace publish of the
> `extension-v*` line in `.github/workflows/release.yml`, one new dispatch-only workflow, the GitHub
> `marketplace` environment, and the owner's one-time Azure / Marketplace setup. No product code changes.
>
> Hard deadline: **2026-12-01**, the day Azure DevOps stops accepting global PATs. The owner decided on
> 2026-10-09 to write this plan and NOT to implement it yet. The same plan exists in
> `dew_flow_creds_for_devs · todo/PLAN_marketplace_entra_publish.md` and is being written for
> `wsl_care · todo/PLAN_marketplace_entra_publish.md` (§9 draws the boundary between them).

## 1. Symptom and goal

**What runs today.** An `extension-v*` tag packages the `.vsix` (`.github/workflows/release.yml:548-552`),
attaches it to the GitHub release and publishes that release (`release.yml:558-589`), and only then pushes
it to the Marketplace with a stored token:

```yaml
# release.yml:594-604
- name: Publish to the Marketplace
  env:
    VSCE_PAT: ${{ secrets.VSCE_PAT }}
  run: |
    if [[ -z "$VSCE_PAT" ]]; then echo "::warning::VSCE_PAT is not set — …"; exit 0; fi
    ./node_modules/.bin/vsce publish --no-dependencies --packagePath ./*.vsix -p "$VSCE_PAT"
```

`VSCE_PAT` is a repository secret, set 2026-09-01 (`gh secret list`, read 2026-10-09). The comment at
`release.yml:591-593` says it is a PAT with scope *Marketplace: Manage*. A Marketplace PAT has to be
created for **"All accessible organizations"**. The VS Code publishing guide calls a PAT scoped to one
organization the cause of its 401/403 FAQ entry. That makes it a **global** PAT.

**What breaks.** Azure DevOps decommissions every global PAT on **2026-12-01**. The owner saw the banner
on the PAT page on 2026-10-09. After that date the token is refused. The step does not take its
"secret absent" branch (`release.yml:600-603`), because the secret is still present. So `vsce publish`
fails, the job goes red, and the order of the job leaves the worst split behind:

- the GitHub release is **already public**, because `release.yml:589` ran before the publish step;
- the Marketplace still serves the previous version, so everybody who installs from the Marketplace
  stays on it;
- `POST_DEPLOY.md` item 2 (`POST_DEPLOY.md:94`) fails, but only when somebody runs it.

The 401/403 response itself is **expected, not observed**, because nobody has run a global PAT after the
retirement date.

**Goal.** Before 2026-12-01, an `extension-v*` tag publishes to the Marketplace with **no stored
long-lived credential**. A short-lived Microsoft Entra ID token, obtained through GitHub Actions OIDC,
does the publishing, and only a job running in a protected `marketplace` environment can obtain it.
The PAT stays available as a rollback until the new road has published one real release. After that the
PAT path, the secret, and the token are removed, all before the deadline.

## 2. What was checked, with sources (2026-10-09)

| # | Fact | Source |
|---|---|---|
| F1 | Global PATs are fully decommissioned on **2026-12-01**: *"Tokens will stop working after this date."* The block on creating new global PATs announced for 2026-03-15 was **withdrawn** (update of 03/05): *"You may continue creating global PATs until December 1."* Azure DevOps Server is not affected. | <https://devblogs.microsoft.com/devops/retirement-of-global-personal-access-tokens-in-azure-devops/> (Angel Wong, 2025-12-12) |
| F2 | The VS Code publishing guide (DateApproved 10/7/2026) says: *"On December 1, 2026, global Personal Access Tokens (PATs) in Azure DevOps are retired. To keep publishing extensions, use secure automated publishing with Microsoft Entra ID instead of PATs."* Its recipe uses a **user-assigned managed identity** and a federated credential. It reads the identity's id from `az rest -u https://app.vssps.visualstudio.com/_apis/profile/profiles/me --resource 499b84ac-1321-427f-aa17-267ca6975798` (*"the `id` field"*). It then has you *"Add the managed identity (using its resource ID) as a member of your publisher"* with the **Contributor** role, and publish with `vsce publish --azure-credential` (vsce ≥ 2.26.1). The recipe is written for **Azure Pipelines** (`AzureCLI@2`, an ARM service connection). It has no GitHub Actions example. | <https://code.visualstudio.com/api/working-with-extensions/publishing-extension#secure-automated-publishing-to-visual-studio-marketplace>, source `microsoft/vscode-docs · api/working-with-extensions/publishing-extension.md:65-158` |
| F3 | Manual upload of a `.vsix` on the publisher management page is a documented way to publish. It needs no PAT. | same page, source line 236; <https://marketplace.visualstudio.com/manage> |
| F4 | `vsce --azure-credential` gets its token from a `ChainedTokenCredential` in this order: `EnvironmentCredential`, then `AzureCliCredential`, then `ManagedIdentityCredential`, then `AzurePowerShellCredential`, then `AzureDeveloperCliCredential`. It asks for scope `499b84ac-1321-427f-aa17-267ca6975798/.default`, the Azure DevOps resource. So an `az` CLI session opened by `azure/login` is enough. | <https://github.com/microsoft/vscode-vsce/blob/v4.0.0/src/auth.ts> |
| F5 | **A PAT beats `--azure-credential`.** `getPAT` returns `options.pat` first and tries the Azure credential only after that. `--pat` **defaults to `process.env.VSCE_PAT`** on `publish` and `verify-pat`. So a step that passes `--azure-credential` while `VSCE_PAT` is in its environment silently publishes with the PAT. This holds in 3.9.2 (the version this repository runs) and in 4.0.0. | <https://github.com/microsoft/vscode-vsce/blob/v4.0.0/src/publish.ts> (`getPAT`) and <https://github.com/microsoft/vscode-vsce/blob/v4.0.0/src/main.ts> (`publish`, `verify-pat` options); the same order at <https://github.com/microsoft/vscode-vsce/blob/v3.9.2/src/publish.ts> and <https://github.com/microsoft/vscode-vsce/blob/v3.9.2/src/main.ts> |
| F6 | `vsce verify-pat [publisher] --azure-credential` exists. It calls `getRoleAssignments` on the publisher and succeeds for **any** role: Creator, Owner, Contributor **or Reader**. So a green `verify-pat` proves **membership, not publish rights.** `vsce show <id>` takes no credential; it is a public gallery read. | <https://github.com/microsoft/vscode-vsce/blob/v4.0.0/src/store.ts> (`verifyPat`), <https://github.com/microsoft/vscode-vsce/blob/v4.0.0/src/main.ts> and <https://github.com/microsoft/vscode-vsce/blob/v4.0.0/src/show.ts> (`show`) |
| F7 | This repository runs vsce **3.9.2** (`src_vs_code/package-lock.json:1747`, range `^3.0.0` at `src_vs_code/package.json:1324`), with `@azure/identity` 4.13.2. Both are above the 2.26.1 floor. | this repository, read 2026-10-09; <https://github.com/microsoft/vscode-vsce/releases/tag/v3.9.2> |
| F8 | Federated credential for GitHub: issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`. The subject must match **exactly**, and a wrong subject *"is created successfully without error"* and fails only at exchange time. There is a maximum of **20** federated credentials per identity, and no wildcards. | <https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust-user-assigned-managed-identity> |
| F9 | A job that references an environment gets a subject of the form `…:environment:<NAME>`, not the ref form. The job needs `id-token: write`. | <https://docs.github.com/en/actions/reference/security/oidc> |
| F10 | **This repository emits the IMMUTABLE subject.** `gh api repos/oleksandrdubyna88/dew_flow_connect_other_ais/actions/oidc/customization/sub` answers `use_immutable_subject: true` with prefix `repo:oleksandrdubyna88@71817001/dew_flow_connect_other_ais@1352342534`. So the subject to trust is `repo:oleksandrdubyna88@71817001/dew_flow_connect_other_ais@1352342534:environment:marketplace`, **not** the name-only form. | API read 2026-10-09; <https://learn.microsoft.com/en-us/entra/workload-id/workload-identities-github-immutable-subjects>; <https://github.blog/changelog/2026-04-23-immutable-subject-claims-for-github-actions-oidc-tokens> |
| F11 | Azure DevOps service principals and managed identities: a managed identity is *"a special type of service principal"*. Tokens are short-lived. Access is managed as for any member. | <https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/service-principal-managed-identity?view=azure-devops> (ms.date 2026-09-21) |
| F12 | `azure/login` takes `client-id`, `tenant-id`, `subscription-id` (optional) and `allow-no-subscriptions`. Latest release is **v3.1.0** (2026-09-10), whose tag points at commit `a641126d1b8aa4d1fa005f4f92df94a3a4c4c906`. | `Azure/login · action.yml`; `gh release list -R Azure/login`, `gh api …/git/tags/053f728…` |
| F13 | Environments: "Selected branches and tags" limits which refs may deploy. Environment variables and secrets are visible **only** to jobs that reference the environment. Required reviewers are available on public repositories, and this one is public. | <https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments> |
| F14 | **The alternative that is not ready: OIDC "trusted publishing".** `vsce publish --oidc` exchanges a GitHub OIDC token directly with the Marketplace, with no Azure identity. It was added in vsce 4.0.0 (PR #1291) and **hidden from `--help`** as *"unannounced"* (#1297). Its exchange was realigned with the Marketplace contract only in the **pre-release** 4.0.1-1 (#1337, merged 2026-09-29), whose PR says *"Live end-to-end publishing has not been tested."* On 2026-09-14 the Marketplace team wrote on microsoft/vsmarketplace#2121 that trusted publishing for GitHub Actions is *"not complete yet"*. | `gh pr view 1291/1297/1337 -R microsoft/vscode-vsce`; <https://github.com/microsoft/vsmarketplace/issues/2121> |
| F15 | Organization-scoped PATs for the Marketplace are **not** supported. Issue #2121 asks for them and is still open. | <https://github.com/microsoft/vsmarketplace/issues/2121> |

*Later pre-releases:* vsce 4.0.1-2 and 4.0.1-3, both 2026-10-03, came after 4.0.1-1. Their notes name
no OIDC change (`gh release view v4.0.1-2|v4.0.1-3 -R microsoft/vscode-vsce`, read 2026-10-09). F14's
verdict stands.

**Not confirmed: each of these is checked by a step below, not assumed.**

- **N1 — which Marketplace UI adds the identity.** F2 says *"as a member of your publisher"*. The path
  *manage → publisher → Members → Add*, the use of the profile `id` rather than the client, object or
  resource id, and the claim that an **app registration** authenticates but then fails at publish with
  `InvalidAccessException` all come from one secondary source: a DEV Community post by Emre Ozsahin,
  dated "Jul 10" with no year (<https://dev.to/emrecodes/publishing-to-the-vs-code-marketplace-with-ci-4p2h>).
  No Microsoft page found says this. Step O5 shows what actually happens.
- **N2 — whether a publisher owned by a personal Microsoft account accepts an identity from an Entra
  tenant.** The publisher `remsoftdev` was created with a Microsoft account (`dew_flow_creds_for_devs ·
  src_vs_code/docs/PUBLISHING.md:77-82`). Not found in any document. Step O5 and S1's probe answer it.
- **N3 — whether the identity needs an Azure role.** F2 gives the identity **Reader**, because an Azure
  Pipelines service connection is scoped to a subscription. `azure/login` with `allow-no-subscriptions:
  true` does not need a subscription. The secondary source above uses it that way and assigns no role.
  This plan therefore assigns **no** Azure role. If the probe's login fails for lack of a subscription,
  assign Reader on the identity's own resource group, and record that as a deviation.
- **N4 — whether the Azure portal's "GitHub Actions" scenario can produce the immutable subject (F10).**
  Not checked. The plan therefore creates the credential with an explicit subject, through
  `az identity federated-credential create` or the portal's "Other issuer" scenario.
- **N5 — whether a global PAT can be created or renewed today.** F1 says yes, until 2026-12-01. The
  owner's banner of 2026-10-09 agrees. The current PAT's **expiry date** is not readable from here,
  because secrets are write-only. The owner records it (O7). If it is earlier than the switch, the
  rollback is gone earlier than this plan assumes.
- **N6 — whether `profiles/me` answers for an identity that belongs to no Azure DevOps organization.**
  F2's recipe runs inside Azure Pipelines, where the identity is already known to an organization. Here
  it is not. This is an inference, not checked. V1 shows it at O4.
  - **If it fails:** add the identity to a free Azure DevOps organization connected to the same tenant
    (F11, *Add the identity to Azure DevOps*, Stakeholder access), then re-run O4.
  - **If that fails too:** the managed-identity road stops. Revise the plan to the service principal,
    or to road B.

## 3. Design

### 3.1 Which road

| Road | Verdict |
|---|---|
| **A. `--azure-credential` + a user-assigned managed identity + GitHub OIDC (`azure/login`)** | **Chosen.** It is the documented Entra road (F2). It works with the vsce this repository already pins (F7) and stores no secret. |
| B. `--oidc` trusted publishing | Not usable before the deadline. It is hidden and pre-release, and the Marketplace side is not complete (F14). It is simpler when it ships, so it is recorded as a follow-up (§8), not as the plan. |
| C. An organization-scoped PAT | Not supported (F15). |
| D. Manual `.vsix` upload on the management page | The **break-glass** path (F3). It is not automation, but it needs no token and works after the deadline. |

### 3.2 The trap the design must close first

F5: **if `VSCE_PAT` is in a step's environment, `--azure-credential` is ignored.** A step that "moved to
Entra" with the old `env:` block still in place would keep publishing with the PAT and pass every
review. Then it would break on 2026-12-01, exactly as if nothing had been done. `verify-pat` reads the
PAT the same way, so a green preflight could also be the PAT. Three layers close it:

- **Structural, in every `run:` that passes `--azure-credential`** (the preflight and the publish, here
  and in the probe): the command is `env -u VSCE_PAT ./node_modules/.bin/vsce … --azure-credential`.
  The variable cannot reach vsce, whatever `env:` block someone adds at step, job or workflow level.
  `env:` is per step, so a separate "guard step" would not see a variable added to the publish step.
- **No `VSCE_PAT` anywhere in the Entra path.** No such key in that job's `env:` or in the workflow's
  `env:`. Its only secret is `GITHUB_TOKEN`.
- **Pinned by T1** (§6), which fails when `VSCE_PAT` appears in any Entra step, the job's `env:`, or the
  workflow's `env:`, and when an `--azure-credential` command lacks `env -u VSCE_PAT`.

### 3.3 The publish leaves the build job

Today one job runs `npm ci`, the whole test suite, the bundler and `vsce package`, and then publishes
(`release.yml:475-604`). Giving that job `id-token: write` would hand a Marketplace-capable token to
every lifecycle script and test in the dependency tree. So the publish moves into its own job:

```yaml
extension-marketplace:
  name: extension · marketplace
  needs: extension
  if: startsWith(github.ref, 'refs/tags/extension-v')
  runs-on: ubuntu-latest
  timeout-minutes: 15
  environment: marketplace
  permissions:
    contents: read      # gh release download of the asset the extension job attached
    id-token: write     # the GitHub OIDC token azure/login exchanges; this job only
  steps:
    - checkout (persist-credentials: false) + setup-node, as in the extension job
    - npm ci --ignore-scripts   (src_vs_code; the lockfile decides which vsce runs, as release.yml:549-551 already argues)
    - gh release download "$GITHUB_REF_NAME" --pattern '*.vsix'   (env GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}, as release.yml:561
                                                                   does; the exact bytes release.yml:579 attached; never a rebuild)
    - id: mode   # one step decides; it writes outputs, never exits 0 to "skip" (a later step would still run)
      reads vars.MARKETPLACE_AUTH and vars.AZURE_CLIENT_ID; writes `path=entra|pat|manual|skip`:
        unset/entra + client id present -> entra;  unset/entra + no client id -> skip (::warning::, as release.yml:600-603)
        pat -> pat (refused, red, on/after 2026-12-01);  manual -> manual;  ANY other value -> red, naming the three
    - [entra] provenance: checkout with fetch-depth: 0 (a depth-1 clone cannot prove a tag behind main's tip is an ancestor), then git merge-base --is-ancestor "$GITHUB_SHA" origin/main, else red
    - [entra] azure/login@a641126d1b8aa4d1fa005f4f92df94a3a4c4c906 # v3.1.0      if: steps.mode.outputs.path == 'entra'
              with: client-id: ${{ vars.AZURE_CLIENT_ID }}, tenant-id: ${{ vars.AZURE_TENANT_ID }}, allow-no-subscriptions: true
    - [entra] env -u VSCE_PAT ./node_modules/.bin/vsce verify-pat remsoftdev --azure-credential     (membership preflight, F6)
    - [entra] env -u VSCE_PAT ./node_modules/.bin/vsce publish --no-dependencies --skip-duplicate --packagePath ./*.vsix --azure-credential
    - [pat]   the current release.yml:604 command, with VSCE_PAT in THIS step's env only (rollback, 3.4)
    - [manual] ./node_modules/.bin/vsce show remsoftdev.connect-other-ais --json: parse .versions[0].version; green only if it EQUALS
              the tag's version (the person uploaded it by hand, F3), red otherwise; vsce show exits 0 for ANY existing extension
```

Every `[x]` step carries `if: steps.mode.outputs.path == 'x'`. The variables are environment-scoped, and
those are not readable in a job-level `if:`, so the switch is step-level by necessity. **`azure/login`
sits directly before the two vsce steps**, with nothing in between: the az session exchanges the
short-lived GitHub assertion for the Azure DevOps token when vsce asks. T1 pins that order.

- **The vsix is downloaded, never rebuilt.** It is the file the release already carries, and
  `vsce publish --packagePath` does not run `vscode:prepublish`.
- **The skip contract stays.** Today an absent credential warns and exits 0, and the `.vsix` waits on
  the release for a manual upload (`release.yml:600-603`, header `release.yml:5-7`). Here `path=skip`
  does the same: the `::warning::` is written, and every later step is skipped by its `if:`. A login or
  publish **failure** is red, never a skip.
- **`--skip-duplicate`** (vsce's `skipDuplicate`) makes a re-run idempotent. A publish can succeed on the
  server and time out on the client, and a re-run would then fail on "version already exists". The
  version itself is still guarded by the tag/manifest check (`release.yml:533-541`), so this cannot
  hide a wrong version.
- **Provenance.** The environment admits any `extension-v*` tag (O3), and this repository has no tag
  ruleset (D2). So the job also refuses a tag whose commit is not on `main`. Without that, a tag on an
  unmerged commit, possibly one carrying a modified `release.yml`, could mint the token. Reviewed code
  is the only code that reaches `azure/login`.
- **The order stays the same:** GitHub release first, Marketplace second (§1). Reordering is out of scope.
- **Pin the action by SHA, not by tag**, like every other `uses:` line here. The SHA above was resolved
  on 2026-10-09; the implementer re-resolves it (F12), and Dependabot's `github-actions` ecosystem
  (`.github/dependabot.yml:51`) keeps it fresh.
- **No `AZURE_*` variable is exported into the vsce step.** They go to `azure/login` as `with:` inputs.
  `EnvironmentCredential` is first in vsce's chain (F4). With a client id and no secret it is
  "unavailable", and the chain moves on, but the cleanest proof that `AzureCliCredential` answered is that
  nothing else could have.

### 3.4 The rollback switch, and its expiry

An environment variable `MARKETPLACE_AUTH` in `marketplace`:

- unset or `entra` → the Entra branch.
- `pat` → the old command, reading `secrets.VSCE_PAT`, which is still a repository secret. The branch
  **refuses on or after 2026-12-01** with a message naming the break-glass upload (F3). It would fail
  anyway with a 401, and saying why is cheaper than a 401 nobody can explain.
- `manual` → the break-glass case. The person has uploaded the tag's `.vsix` by hand, and the job only
  checks that the gallery serves that version. A release done by hand still ends with a green run that
  says what happened.
- **Anything else is red**, with a message listing the accepted values. A typo is never read as one of
  them.

**Rolling back:** set `MARKETPLACE_AUTH=pat`, then re-run **this job**: *Re-run jobs →
extension · marketplace*. A `skip` ends green, so *Re-run failed jobs* would not include it. Never use
*Re-run all jobs*. That would re-run `extension`, whose attach step refuses a release that is already
public (`release.yml:570-571`).

The rollback needs no code change and no rebuild, and the downloaded `.vsix` is the same file. Story
S3 deletes the `pat` branch, the secret and the token once one real release has gone out through
Entra. `manual` stays.

### 3.5 The one-time probe, kept as the dry check

A new `.github/workflows/marketplace-identity.yml`, `workflow_dispatch` only, one job in environment
`marketplace`, with permissions `contents: read` and `id-token: write`:

0. checkout (`persist-credentials: false`), `setup-node`, and `npm ci --ignore-scripts` in `src_vs_code`,
   as in 3.3. Every `vsce` below means `./node_modules/.bin/vsce`, the lockfile's version (F7). A fresh
   runner has no bare `vsce`, and an unpinned `npx` would choose its own version;
1. `azure/login` (same pin and inputs as 3.3);
2. `az rest -u https://app.vssps.visualstudio.com/_apis/profile/profiles/me --resource 499b84ac-1321-427f-aa17-267ca6975798`,
   printing **only** `id` and `displayName` through `--query`. The id is an identifier, not a secret, and
   it is what O5 pastes;
3. `env -u VSCE_PAT vsce verify-pat remsoftdev --azure-credential`. **No `continue-on-error`.** Before
   O5 it fails red, which is expected, and step 2 has already printed the id. After O5 the run is green
   only if this passes, so a green run is the evidence, not a log line somebody has to read;
4. `vsce show remsoftdev.connect-other-ais --json`, with `if: always()`, printing the version the
   gallery serves. This is a public read with no credential (F6);
5. an input `check_pat` (boolean, default false). When it is true, steps 1–3 are **skipped**, and one
   step runs `vsce verify-pat remsoftdev` with `VSCE_PAT` in its own `env:`. That is a separate run that
   proves the **rollback** still authenticates, without publishing anything.

It stays after the migration. It is the cheapest way to answer "does publishing still authenticate?"
before a release, or after anyone touches the Azure side.

### 3.6 Owner decisions this plan needs

- **D1 — one identity for the publisher, or one per repository.** Recommended: **one** user-assigned
  managed identity, `remsoftdev-marketplace-publisher`, with **one federated credential per repository**
  (this one, `dew_flow_creds_for_devs`, `wsl_care`; 3 of the 20 allowed by F8).
  - The Contributor role is granted **per publisher**, so a per-repo identity does not narrow what each
    one may publish.
  - Revoking one repository means deleting its federated credential either way.
  - One identity means one member on `remsoftdev` and one O5.
  - The cost: the Marketplace shows one publisher identity for all three repositories.

  Nothing in §3.3–3.5 depends on this choice: the code reads `vars.AZURE_CLIENT_ID`.
- **D2 — a required reviewer on `marketplace`.** Recommended: **no**. The tag is already the human
  decision, and a reviewer would stall a release that is designed to finish unattended. The owner may choose yes; it changes no code.

  **Who can then reach the environment.** Read on 2026-10-09:
  - this repository has **no** ruleset (`gh api repos/oleksandrdubyna88/dew_flow_connect_other_ais/rulesets`
    answers `[]`), so no tag is protected;
  - the only collaborator is the owner;
  - `main` changes only through a pull request.

  So anyone with write access can push an `extension-v*` tag, which is exactly who can publish today
  with the repository secret `VSCE_PAT`. The environment does not widen that set; its tag filter
  narrows the **ref**, not the person. `dew_flow_creds_for_devs` closes the gap with a tag ruleset
  that blocks creation except for its release App. This repository pushes its release tags **by hand**
  (`.agents/conventions/common/task-lifecycle.md` §3), so copying that ruleset would change the
  release procedure, and that is out of scope here. What this plan adds instead is the provenance step
  (3.3): a tag whose commit is not on `main` cannot reach `azure/login`. If the owner also wants the
  tag's AUTHOR guarded, the choice is between D2 = yes and a separate decision about a tag ruleset.

## 4. Owner steps, once, in Azure and on the Marketplace (no code)

Done by the person who owns the `remsoftdev` publisher. Each step says where.

- **O0 — go/no-go, THIS WEEK (by 2026-10-16): an Azure subscription exists.** A user-assigned managed
  identity is an ARM resource, so it needs a subscription in an Entra tenant. The publisher was created
  with a Microsoft account (N2), and nothing here shows the owner has a subscription. Check
  portal.azure.com → *Subscriptions*.
  - If there is none, create a **pay-as-you-go** subscription, not a free trial. A trial is disabled
    when it ends, and what then happens to an identity living in it was not checked. The identity costs
    nothing.
  - Add a budget alert (*Cost Management → Budgets*, e.g. 1 USD) so a mistake is visible.

  Nothing below can start without O0, which is why it is first and dated earliest.
- **O1 — Azure: the identity** (portal.azure.com). Needs a subscription in the Entra tenant the owner
  uses; a user-assigned managed identity is an ARM resource. *Resource groups → Create*
  `rg-marketplace-publishing`, then *Managed Identities → Create* `remsoftdev-marketplace-publisher` in it.
  Record **Client ID** and **Directory (tenant) ID** from *Overview*. **No role assignment** (N3).
  *If D1 is "shared" and another repository did this first, reuse that identity and skip O1.*
- **O2 — Azure: trust this repository.** On the identity, *Settings → Federated credentials → Add
  credential → Other issuer*. Use issuer `https://token.actions.githubusercontent.com`, subject
  `repo:oleksandrdubyna88@71817001/dew_flow_connect_other_ais@1352342534:environment:marketplace`
  (F10, **re-read** with `gh api repos/oleksandrdubyna88/dew_flow_connect_other_ais/actions/oidc/customization/sub`
  first), audience `api://AzureADTokenExchange`, and name `coai-marketplace`. Or, in Cloud Shell:
  `az identity federated-credential create --name coai-marketplace --identity-name remsoftdev-marketplace-publisher --resource-group rg-marketplace-publishing --issuer https://token.actions.githubusercontent.com --subject '<subject above>' --audiences api://AzureADTokenExchange`.
- **O3 — GitHub: the environment.** *Settings → Environments → New environment* `marketplace`.
  *Deployment branches and tags → Selected branches and tags*: tag `extension-v*` and branch `main`
  (`main` only so the probe of 3.5 can be dispatched). *Environment variables*: `AZURE_CLIENT_ID`,
  `AZURE_TENANT_ID`. Or:
  `gh variable set AZURE_CLIENT_ID --env marketplace -R oleksandrdubyna88/dew_flow_connect_other_ais` (same for the tenant).
  D2 decides the reviewer.
- **O4 — after S1 merges: read the identity's profile id.** *Actions → marketplace-identity → Run
  workflow* on `main`. Step 2 prints `id`. Step 3 is expected to fail until O5.
- **O5 — Marketplace: make the identity a member** (marketplace.visualstudio.com/manage →
  `remsoftdev`). *Members → Add*, paste the `id` from O4, role **Contributor** (F2). The UI path is N1;
  if it differs, record what it was. *If D1 is "shared" and another repository did this, skip.*
- **O6 — prove it dry.** Run the probe again: step 3 must now pass. Remember that this proves
  membership, not Contributor (F6). Check the role on the Members page by eye. Run once more with
  `check_pat: true`; it must pass, which proves the rollback is alive.
- **O7 — record the PAT's expiry** (dev.azure.com → *Personal access tokens*). First record it in this
  plan's §11, by 2026-10-31. S2 then moves it into `POST_DEPLOY.md` beside the row it adds, because that
  row does not exist before S2. It cannot be later than 2026-12-01. **The rollback is only as long as
  the PAT.** If the expiry falls before S3 plus a few days, the owner chooses one of two things before
  S2 merges:
  - **Renew the PAT** with an expiry of 2026-12-01, which F1 still allows, then re-run O6's
    `check_pat`.
  - **Declare the rollback to be manual upload only (F3).** S2 then ships without the `pat` branch.

  The choice is recorded here as a deviation.
- **O8 — after S3 merges: retire the PAT.** `gh secret delete VSCE_PAT -R oleksandrdubyna88/dew_flow_connect_other_ais`,
  then revoke the token on dev.azure.com. **The token may be shared, three ways:**
  - `dew_flow_creds_for_devs` holds a `VSCE_PAT` repository secret;
  - `wsl_care` holds one as an **environment** secret in its `marketplace` environment
    (`wsl_care · .github/workflows/release-extension.yml:45-48`).

  The token is revoked only when none of the three still needs it as a rollback.

## 5. Build order

Target dates leave one week of slack before the deadline.

| Step | What | Who | By |
|---|---|---|---|
| O0 | a subscription exists (go/no-go) | owner | **2026-10-16** |
| O1–O3 | identity, trust, environment | owner | 2026-10-24 |
| **S1** | PR: `marketplace-identity.yml` (3.5) + test T2. No change to `release.yml`. Small enough to land as soon as O0 says go, so that O4/O5, the real go/no-go, can run earlier than the date. | agent | 2026-10-24 |
| O4–O7 | profile id, membership, dry proof, PAT expiry recorded | owner | 2026-10-31 |
| **S2** | PR: `extension-marketplace` job (3.3), switch (3.4); header `release.yml:5-7` and comment `release.yml:591-593` rewritten; `POST_DEPLOY.md` gains a row for "the publish credential authenticates" (the probe, manual) and the PAT-expiry line; test T1 | agent | 2026-11-07 |
| R1 | the next `extension-v*` tag publishes through Entra, observed (§6, V4). If no release is due by 2026-11-14, the owner decides whether to cut a patch release only to prove it. | owner + agent | 2026-11-14 |
| **S3** | PR: delete the `pat` branch and the `check_pat` input; `release.yml` header says Entra only; then O8 | agent, then owner | 2026-11-24 |
| — | **deadline** | | **2026-12-01** |

Each S is its own branch and pull request through the review gate. Why S1 comes before S2: S2's publish
cannot be tried without a real tag, while S1 proves every Azure and Marketplace step with nothing at
stake. If O5 fails (N1, N2), S2 never starts. The fallback then is break-glass (F3) from 2026-12-01,
and this plan is revised: a service principal instead of the managed identity, or road B if it has
shipped by then.

**Rolling back:**

- before 2026-12-01: `MARKETPLACE_AUTH=pat`, then re-run the `extension · marketplace` job (3.4);
- after S3, or on or after 2026-12-01: download the `.vsix` from the GitHub release, upload it at
  marketplace.visualstudio.com/manage (F3), then set `MARKETPLACE_AUTH=manual` and re-run that job, so
  the run ends green on a check of what the gallery serves;
- a broken S2 itself is reverted by a revert PR.

The tag is never moved.

## 6. Test and verification plan

Tests (in `src_vs_code/src/test/`, in the style of the existing workflow-reading tests in
`changelogNamesTheRelease.test.ts:347-366`: the job sliced from its header, CRLF normalised):

- **T1 (S2)** — in `release.yml`:
  - the **only** job with `id-token: write` is `extension-marketplace`, and it declares
    `environment: marketplace`;
  - every command carrying `--azure-credential` starts with `env -u VSCE_PAT`;
  - `VSCE_PAT` appears in no Entra step's `env:`, nor in that job's or the workflow's `env:`;
  - `secrets.VSCE_PAT` appears **only** in the `pat` step, and `vsce publish` appears **only** in
    `extension-marketplace`, so the old publish cannot be left behind in `extension`. If it were, it
    would publish with the PAT first, and R1 would prove nothing;
  - `azure/login@` is followed by a 40-hex SHA, and it is immediately followed by the two vsce steps;
  - the provenance step precedes `azure/login`;
  - the `mode` step rejects unknown values, and the `pat` branch carries the 2026-12-01 refusal;
  - the provenance checkout has `fetch-depth: 0`, and the `manual` step compares the served version with
    the tag's rather than trusting `vsce show`'s exit code;
  - the `gh release download` step has `GH_TOKEN` in its `env:`.

  Break-it, per the testing rule: add `VSCE_PAT: ${{ secrets.VSCE_PAT }}` to the Entra publish step, and
  separately drop `env -u VSCE_PAT` from it. Watch T1 fail naming F5 each time, then restore and watch
  it pass. Both observations go in the PR. **Do not** move or disable an existing test to make room.
- **T2 (S1)** — `marketplace-identity.yml` is `workflow_dispatch` only, its one job is in
  `marketplace`, it installs from the lockfile (`npm ci --ignore-scripts`) before any `vsce` and calls
  `./node_modules/.bin/vsce`. Its `verify-pat --azure-credential` starts with `env -u VSCE_PAT` and has
  no `continue-on-error`, its `vsce show` is `if: always()`, and its `az rest` prints a `--query`
  projection rather than the raw response.
- The new jobs must **not** run `npm test`. If one ever does, `jobsRunningTheSuite`
  (`changelogNamesTheRelease.test.ts:308-319`) requires it to fetch tags, which is correct and is left
  alone.
- `ci.yml`'s actionlint job (`ci.yml:429`) lints both files with shellcheck present.
- The extension suite (`cd src_vs_code && npm test`) and the family checks
  (`node .agents/conventions/tools/plan-lifecycle.mjs`, `pin-check.mjs`) pass on the staged tree.

Verification on the real services:

- **V1 (O4)** — the probe prints a profile `id`. This proves the federated credential, the subject (F10)
  and `azure/login`.
- **V2 (O6)** — `verify-pat --azure-credential` prints *"The Personal Access Token verification succeeded
  for the publisher 'remsoftdev'."* (the string in `src/store.ts`; it says "Personal Access Token" even
  for an Entra token). This proves membership (F6), not the role.
- **V3 (O6)** — the `check_pat` run passes, so the rollback authenticates.
- **V4 (R1)** — the tag's `extension · marketplace` job log shows `Published remsoftdev.connect-other-ais
  v<version>` and **no** `VSCE_PAT` in its environment dump. `POST_DEPLOY.md` item 2 passes once the
  gallery catches up; it takes minutes (`POST_DEPLOY.md:89`).
- **V5 (negative, S1)** — dispatching the probe from a non-`main` branch is refused by the
  environment's deployment policy (F13). That proves no other **branch** can mint the token. It does
  not prove anything about tags: any `extension-v*` tag passes the policy. For tags, the provenance
  step (3.3) is the guard, and T1 pins it.

## 7. Growth surfaces

None that grows on its own. The probe runs on dispatch and keeps no artefact. The identity carries one
federated credential per repository, 3 of the 20 allowed (F8). The rollback is a branch with a date on
it, and S3 deletes it.

## 8. Risks and follow-ups

- **N1/N2 fail at O5.** The plan stops before any code depends on it (§5).
- **The managed identity needs a subscription.** O0 is a go/no-go with the earliest date in the plan.
  An app registration (service principal) is the other documented Azure DevOps identity (F11) and
  needs no subscription, but the secondary source says it fails at publish (N1). Try it only after the
  managed identity has failed, or if O0 says no.
- **The immutable subject (F10) changes nothing about names.** It does change the subject string, so a
  credential copied from a name-based example fails silently at exchange (F8). §6 V1 catches it on day one.
- **Follow-up, not part of this plan:** when `--oidc` trusted publishing (F14) is announced and released
  in a non-pre-release vsce, evaluate dropping Azure entirely. Open it as its own `todo/` plan then.

## 9. Across repositories

| Item | `dew_flow_connect_other_ais` (this plan) | `dew_flow_creds_for_devs` | `wsl_care` |
|---|---|---|---|
| Managed identity + Marketplace membership (O1, O5) | builds it if first (D1 shared) | reuses it, or builds it if first | its own plan decides; the other agent is writing it |
| Federated credential | `coai-marketplace`, immutable subject of THIS repo | its own credential, its own subject | its own |
| `marketplace` environment, workflow changes, tests | here | there (`.github/workflows/release.yml:702-710`) | there |
| Deleting `VSCE_PAT` and revoking the token | the secret here; the token only when no repo needs the rollback | the same | the same |

**Order:** the identity first, built by whichever repository goes first. Then each repository's S1–S3
are independent. **Disjoint:** no file is shared between the three. **Observed while planning, for
`wsl_care`'s author:** `wsl_care`'s `.github/workflows/release-extension.yml:51` (its *MARKETPLACE
CREDENTIAL* comment, `:45-55`, in the local checkout read 2026-10-09) sketches the subject
as `repo:oleksandrdubyna88/wsl_care:environment:marketplace`. That repository also answers
`use_immutable_subject: true` (prefix `repo:oleksandrdubyna88@71817001/wsl_care@1401318131`, read
2026-10-09), so the name-only subject would not match (F8, F10). The same comment puts
`--azure-credential` on `vsce show`, which takes no credential (F6). Neither was edited from here.

## 10. Definition of Done

- [ ] O0 answered by 2026-10-16. O1–O7 done and recorded: identity, federated credential with the
      immutable subject, `marketplace` environment with tag `extension-v*` + `main` and the two
      variables, Contributor membership, and the PAT expiry (§11, then `POST_DEPLOY.md` in S2).
- [ ] S1 merged; the probe printed a profile id (V1) and `verify-pat --azure-credential` passed (V2);
      the `check_pat` run passed (V3); a non-`main` dispatch was refused (V5).
- [ ] S2 merged; the only `id-token: write` job is `extension-marketplace` in `marketplace`; T1 watched
      failing with `VSCE_PAT` in the Entra step, and without `env -u VSCE_PAT`, then passing.
- [ ] One real `extension-v*` release published through Entra, observed in its job log, and
      `POST_DEPLOY.md` item 2 passed (V4) — **before 2026-12-01**.
- [ ] S3 merged before 2026-11-24 (and in any case before 2026-12-01); `VSCE_PAT` deleted from this
      repository; the token revoked once no repository needs it.
- [ ] Deviations recorded here before promotion, especially N1–N6 as they were observed, including what
      the Marketplace's member UI actually asked for. Then the plan is promoted to `research/` with
      `IMPLEMENTED <date>`.

## 11. Recorded by the owner

Fill these in as the steps run. They are identifiers and dates, never a secret.

| Item | Value | Date |
|---|---|---|
| O0 — subscription exists (yes/no, offer type) | | |
| O1 — identity client id / tenant id | | |
| O4 — profile `id` | | |
| O5 — what the Members UI asked for (N1) | | |
| O7 — `VSCE_PAT` expiry | | |
