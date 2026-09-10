# PLAN — one canonical instruction source for Claude Code and Codex

> Status: **IN PROGRESS, 2026-09-10.** Scope: neutral-layout canary and its product discovery adapters; layout, discovery and generated delivery are implemented locally. Reviewed source pin and first code-review corrections are applied; final verification, repeat review and publication remain open.

The shared conventions repository now provides AGENTS/CLAUDE bootstraps, a metadata-driven
resolver and a neutral mount. This repository still discovers only legacy rule folders in
`src_mcp/runners/Context/RuleFiles.cs:79` and only legacy shared locations in
`src_vs_code/src/claudeSnippet.ts:54`. Moving instructions alone would hide the actual rule
bodies from reviewers and make the extension report the gate missing. The extension also
maintains a manual copy of the shared gate body in `claudeSnippet.ts:169`.

This is the ConnectOtherAIs canary of conventions' shared-rules rollout. The conventions
repository owns the resolver, metadata, migrator and canonical rule bodies. This repository
owns reviewer-context discovery, snippet display/distribution and its build/CI wiring.
Rust/Creds/MCP/benchmark/rag migrations are separate consumers of the same reviewed source.
Finish the canary before their publication; keep the user's original checkout untouched.

| Item | Implemented by | Dependency |
|---|---|---|
| Canonical rules, resolver, migration and bounded smoke tools | Conventions S1/S2 | Reviewed source lands first |
| Reviewer discovery, panel lookup, generated gate delivery and CI | This adoption plan | Pin reviewed source; verify canary before other consumers publish |
| Rust/Creds and MCP/benchmark/rag adoption | Conventions S3/S4 | Canary accepted; pinned-by repositories last |

Plan gate session `8e812028`: good_enough, all three reviewers answered; all sixteen returned
findings received decisions. Conventions tooling PR #17 merged as `895b24ce2dfcc2c09ba3af970a57e5b40f5eaf13`;
this canary now pins that immutable source. The legacy journal pin/base remain the rollback record.

## Required outcome

1. Exactly one conventions gitlink at `.agents/conventions`, fixed to the reviewed source
   commit. One project body in `.agents/PROJECT.md`, local bodies in `.agents/rules`; root
   CLAUDE imports only AGENTS. Restore no legacy auto-loaded policy copy. Preserve all existing
   runtime settings, code dependencies and unrelated unfinished work.
2. The C# collector sees the neutral PROJECT/local/shared rule bodies and reports missing
   neutral mounts. Existing Claude/Cursor consumers continue to work. Only the neutral mount's
   canonical rule directories are candidates; its research, tools, todo and source-project
   instructions are not consumer policy. Local-first ordering, whole-file selection, shuffle
   and explicit omissions remain the existing review-context contract. This is a bounded
   reviewer sample, not a second metadata selector or proof of primary-agent equivalence.
3. The panel finds the neutral gate rule and still detects older local/root pasted snippets
   before treating a shared current rule as sufficient. All location readers use the existing
   shared location list; no second discovery implementation is introduced.
4. The extension's distributable gate text comes from the pinned canonical rule at build time.
   Remove its manually maintained body from TypeScript. A fixed ignored generated source is
   prepared before compile and bundle, so both tests and VSIX consume it. Keep the version/hash
   contract and assert canonical body identity after stripping only delivery frontmatter.
   Missing/dirty/wrong mount or malformed canonical input fails preparation explicitly.
   Generated build output is not an independently edited or committed policy source.
5. CI, fresh clone and developer instructions initialize the mount and its locked Node
   dependencies before shared checks/build preparation. Correct current operational references,
   including PROJECT and architecture. Historical audit/plan citations retain their context.

## Build order and verification

- Reconcile this isolated worktree with current main before publication; retain its recorded
  base/old gitlink for rollback. Use the reviewed conventions commit, not a remote-HEAD fallback.
- First initialize that exact gitlink, run `npm ci --ignore-scripts --prefix .agents/conventions`,
  then `rules check`; CI pins Node 22. Record the immutable approved SHA in the adoption journal
  before building. The reviewed source SHA is recorded above and in the adoption journal.
- Add failing neutral-layout discovery scenarios in `src_mcp/tests/RuleFilesTests.cs`: PROJECT,
  nested local rules, shared C#/TypeScript bodies, excluded mount housekeeping, missing mount
  and local-first selection. Preserve the existing legacy cases.
- Extend `SNIPPET_LOCATIONS`; exercise the real filesystem reader and snippet-body comparison.
  Generate the canonical body through existing Node build hooks, not through a runtime fetch.
  Check a missing input fails and the bundled artifact actually contains the canonical marker
  and a sentence from the new generated source. No shipping artifact is inferred from typecheck.
  Frontmatter removal uses the leading opening delimiter and first subsequent delimiter after
  LF normalization, preserving all later body delimiters; C# does not parse it. Keep the existing
  independent content-hash/version guard. A generated file from a prior build is invalidated
  before preparation; compile, typecheck, bundle and packaging all require preparation. A failed
  prepare must leave no usable old generated source. Errors name the missing input and restore
  command; no cached body or alternate source substitutes for it.
  Named acceptance cases: older root or local snippet plus current neutral shared snippet must
  report the older one; legacy automatic policy copies must make `rules check` fail.
- Run Debug/Release solution builds and their MTP test executables (never dotnet test), the
  extension's normal compile/test/package commands, and relevant family checks. A code pin
  is not updated to make a rules freshness check pass. No live Team server deploy is involved.
- Run the shared resolver from root and a nested directory; record actual native Claude/Codex
  reads separately. A quota-limited CLI stays incomplete. Fresh clone/init/check/read and a
  disposable rollback restore the previous mount/adapters together.
  Rollback checks out/reverts the complete migration commit, including product adapters and CI,
  not only the .agents directory. Missing native CLI cells remain a blocker to claiming the
  complete six-consumer compatibility rollout; completed build/discovery work is recorded
  separately and cannot be relabelled as native behavioral acceptance.
- Update module docs and POST_DEPLOY where installed behavior changes; review the exact diff
  through the repository's own coai session, address CI/reviewer feedback, and publish the PR.
  Install a separately versioned local build only after its artifact and tests are verified;
  do not overwrite a released version with different bytes.

## Bounds and recovery

Reuse the existing reviewer budget/omission contract; do not redesign the review queue or gate
verdict here. The build generator has one source, capped at 256 KiB, and one fixed generated
file plus an atomic temporary file; build cleanup owns these, and there is no accumulating
directory, cache, model call or runtime download. Native smoke uses the shared 180-second,
256-KiB process cap and two fixed 32-KiB report slots. Git and the shared migration journal
retain the old/new pins and rollback base. Product audit fixes unrelated to rule delivery remain
in their existing plans.

## Definition of Done

- [ ] Neutral root/local/shared sources resolve with one clean pinned conventions checkout.
- [ ] Reviewer and panel discovery scenarios pass for neutral and legacy consumers.
- [ ] No manually maintained gate body remains in the extension; packaged bytes match canonical input.
- [ ] Builds, MTP executables, extension tests/package and family checks were observed.
- [ ] Native-agent evidence names actual reads, missing cells and limits; no general obedience claim.
- [ ] Fresh clone and disposable rollback work; settings and unrelated working files remain intact.
- [ ] Review gate and PR feedback resolved, canary merged, installed artifact checked, docs current.
