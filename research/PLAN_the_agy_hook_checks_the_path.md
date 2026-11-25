# Plan: the agy read-only hook checks the path, not only the tool name

> Status: **IMPLEMENTED, 2026-11-17** — S1 and S2 shipped in #1006 (merge 2c84a462, 2026-11-16; its first pull request #1005 was closed and replaced), the macOS link-fixture test fix in #1010; released in mcp 0.65.4 (kept in 0.66.0) and server 0.16.3 (#1008). **Deployed 2026-11-18** (run 44349602108): the Team server serves 0.16.3. "Release" step 3, the check with the release `coai-server` binary on the host, passed on 2026-11-17 before the deploy, and step 4, the deploy and its stamp, on 2026-11-18. Both are recorded in `POST_DEPLOY.md`'s "Last verified" of 2026-11-18. The open tail this line named on 2026-11-17 is closed. Plan gate session 3816a96b: `proceed`, 2 reviewers, both
> findings accepted and folded in (`## Stories`). Allowed over the PR cap by the owner on 2026-11-16. Scope:
> `src_mcp/runners/Reviewers/AntigravityReadOnly.cs`, its tests, the server binary's hook test, the documents of S2.
>
> **Deviations from the plan as approved.** (1) `Decide` gained an optional `TextWriter? log` and its last-resort catch covers the WHOLE of it, not only the place check: a lone UTF-16 surrogate in `toolCall.name` threw `InvalidOperationException` outside the old catch, so the hook answered nothing — found by the code review, fixed red-green-red (tests D1–D8 of `TheAgyHookReadsOnlyInsideItsRootsTests`). A payload whose tool name cannot be read is denied with `DenyReason` (the tool reason); one whose place cannot be read keeps `DenyOutsideReason`. Each catch writes one line to standard error naming the exception TYPE only (`coai --agy-hook: denied after InvalidOperationException`), never its message, which can quote the model's path; stdout stays exactly the JSON answer. (2) The two Windows gaps (`..` after a link or junction; a UNC or device `Path`) were moved to a follow-up plan, [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md](PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md), and are stated as limits in `SECURITY.md`. (3) The macOS CI leg showed the link fixture must point at the fixture as `TempDir` spells it (#1010). Nothing was checked on a Windows machine or on the Team server host. (4) 2026-11-18: "Release" step 3 asked for the hook folder the RELEASE binary prepared. The check before the deploy (2026-11-17) did not use one. It copied the server's folder (`hooks.json` and the agent file, both constants this change did not touch) and pointed the copy's script at the release `coai-server`. The binary and its answers were the release's, but the folder was not of its making. The server's OWN folder, written by 0.16.3 itself, was checked after the deploy, with the same four results (`POST_DEPLOY.md`, Last verified 2026-11-18). The sentence before this item was true on 2026-11-17: the Team server host has since been checked, both before and after the deploy.

## Plan changes
> Epoch 1 · approved 2026-11-16 · by reviewers · state: open
- 1.1 · 2026-11-16 · agent · approach — the S1 spec hardens the rule: a path inside the resolved cwd is denied (in place of subtracting cwd from the roots), `args` must carry exactly one spelling of `Path`, and duplicate JSON properties are refused (spec section 7)

## Problem (measured, agy 1.5.9, WSL, 2026-11-16)

`AntigravityReadOnly.Decide` (src_mcp/runners/Reviewers/AntigravityReadOnly.cs) allows any `read_file` / `view_file`
call by NAME. agy's own root boundary holds for `$HOME` but not for `/tmp`: a consultant read a marker file under
`/tmp` outside its `--add-dir` root by absolute path, by `root/../outside/x`, and through a symlink inside the root
(3 of 3, research/RESULTS_agy_read_file.md §3, re-measured today). On the Team server every job root is
`/tmp/coai-server-job-*` (ReviewLauncher.cs:150), so one tenant's review could read another job's folder.

The hook payload (captured today, verbatim fields): `cwd`, `workspacePaths` (the agy cwd first, then every
`--add-dir` root, absolute), `toolCall.name`, `toolCall.args.Path` (exactly as the model wrote it: absolute or
relative, `..` and links unresolved). The hook runs BEFORE agy's own checks. The hook's cwd is `<coai folder>/.agents`.

## Change

1. `Decide(payload)` allows a file reader only when ALL hold, else deny:
   - `toolCall.name` is `read_file` or `view_file` (unchanged);
   - `toolCall.args.Path` is a string and fully qualified (`Path.IsPathFullyQualified`; a relative or drive-relative
     path is denied — agy rejects relative paths itself anyway);
   - `workspacePaths` is an array of fully qualified strings; the roots are those entries MINUS the payload's `cwd`
     (the coai-owned folder with the hook script — never a root; zero `--add-dir` means zero roots, so a no-file-access
     row or a plan-stage review reads nothing, which matches its grant);
   - the path, resolved with `RealPath.Resolve` (links and `..` followed, the one resolver — TheLinkResolverIsOneRoadTests),
     is `RealPath.IsInside` at least one root resolved the same way. Any resolution failure denies; no fallback to the
     raw spelling. Comparison: the same one the existing containment code uses (WorkspaceLookup.Contained pattern).
2. A distinct deny reason for a path outside: `coai: this consultation reads only inside the folders it was given`
   (the tool-name reason is unchanged), so a log tells the two apart. The path itself is not echoed (it is model text).
3. Roots come from the PAYLOAD, not an environment variable. Reason: agy builds `workspacePaths` from the `--add-dir`
   argv that coai sets; the model cannot change it. An env var would need plumbing through three launch paths and two
   environment builders that drop or replace variables (the server's ReviewLauncher replaces `Environment`; the
   question row's allow-list) — more places to get wrong for no stronger authority.
4. Fail closed on a payload without `workspacePaths` or `Path`: an agy older than 1.5.9 whose payload lacks them
   would have every read denied. Accepted: both the local install and the Team server run 1.5.9, and a denial is
   visible, a leak is not.

## Not changed / limits (stated in the docs)

- A symlink swapped between the hook's check and agy's open (TOCTOU) is not closed: a pre-tool hook cannot bind its
  check to the handle agy opens. This closes the measured routes (absolute, `..`, link), not a concurrent race.
- Windows: 8.3 short names that `RealPath` does not canonicalise are denied (fail closed), not leaked. Case follows the
  comparison Change 1 names (`DocumentId.Comparison`, ignored on Windows), so in a directory made case-sensitive a sibling
  differing only by case would be accepted (repaired 2026-11-16: this line contradicted Change 1; spec section 6).
- Hook denials do not appear in agy's `denied_actions`, so coai's read follow-up does not fire for them (as today for
  tool-name denials).

## Tests (red first)

src_mcp/tests/AntigravityReadOnlyTests.cs, following its `Payload(...)`/`Decision()` helpers, with real temp dirs:
inside a root → allow (both tool names); absolute path outside every root → deny; `root/../outside` → deny; a link
inside the root to a file outside → deny; a link inside the root to a file inside → allow; sibling prefix
(`/x/root-secrets` vs root `/x/root`) → deny; two roots, file in the second → allow; a path in the payload's `cwd`
folder only → deny; zero roots → deny; relative path, missing Path, missing/non-array workspacePaths, non-string
entries, malformed JSON → deny; write tools still deny. The server binary test (TheServerBinaryAnswersTheAgyHookTests)
gains payloads with real `workspacePaths`/`Path` so it still pins allow/deny through the built binary. Plus one live
agy run (local, 1.5.9): in-root read works, `/tmp` outside-root read by absolute path, `..` and link is denied.

## Docs

research/RESULTS_agy_read_file.md (new section: the path check and today's payload capture), research/architecture.md
(agy calls back into coai), SECURITY.md (what the sites guarantee), src_vs_code/CHANGELOG.md (new entry; the "path
check is a follow-up" bullet superseded, history kept), POST_DEPLOY.md "Last verified" stamp (already updated in this
branch for the 0.16.2 deploy).

## Release

mcp patch release via release-please after merge; a server release + deploy so the Team server gets it (the server
answers the hook with its own binary). Rollback: re-dispatch deploy-server.yml with 0.16.2.

## Stories

Split on 2026-11-16 by the gate operator's commands: stories, no epics; a Small plan, so two stories. Both land on this
branch, in one pull request. Order: S1, then the local live run (step 1 below), then S2.

Cornerstones: 1 of 1 — S1

The two accepted gate findings are folded in here:

- (a) cwd and every `workspacePaths` entry are resolved with `RealPath` before cwd is taken out of the roots, and a missing
  or unresolvable cwd denies: S1, spec steps D3, D4 and D6, scenarios B13 and B19.
- (b) the check on the Team server host with the release binary, before the deploy: step 3 below. It is a step of the
  release, not a story.

### S1 — the hook allows a reader only inside the folders it was given (cornerstone)

- **Goal.** `AntigravityReadOnly.Decide` allows `read_file` / `view_file` only when the resolved path is inside a resolved
  granted root and not inside the coai folder. Everything else is denied, and the hook always answers. The in-process
  tests and both built binaries (`coai-mcp`, `coai-server`) pin allow and deny.
- **Spec.** [PLAN_the_agy_hook_checks_the_path_S1_spec.md](PLAN_the_agy_hook_checks_the_path_S1_spec.md): the rule (D0–D7), the two deny
  reasons, the API shape, the scenarios, the server binary test, the limits.
- **Files.** `src_mcp/runners/Reviewers/AntigravityReadOnly.cs`;
  `src_mcp/tests/TheAgyHookReadsOnlyInsideItsRootsTests.cs` (new); `src_mcp/tests/AntigravityReadOnlyTests.cs` (the two
  allow assertions and the built-binary test); `src_server/tests/TheServerBinaryAnswersTheAgyHookTests.cs`. No other
  production file.
- **Done when.**
  - The rows the spec marks FAIL were watched failing against today's `Decide` with "expected deny, found allow", and the
    rows marked pass were watched passing. Both observations are in the pull request.
  - Every scenario of spec sections 4 and 5 passes.
  - Each row of the spec's "red again" table was tried: with that one step removed, the named scenarios go red.
  - `CoaiMcp.Tests` and `CoaiServer.Tests` pass whole, in Release, by their MTP executables. `TheLinkResolverIsOneRoadTests`
    and the leftovers guard pass.
  - `DenyReason` is unchanged, and no reason carries a path.
- **Who.** Spec: Fable. Code and tests: Sonnet. Review: Opus. Fixes: Sonnet.

### S2 — the documents say what the hook guarantees, and what it does not

- **Goal.** Every document that describes the hook matches S1, with the limits stated.
- **Files.** `research/RESULTS_agy_read_file.md` (new section: the path check, the payload captured 2026-11-16, the local
  live run's record); `research/architecture.md` (the section "agy calls BACK into coai" and the diagram arrow that
  says what the hook allows); `SECURITY.md` (one bullet under "What the sites guarantee", and the limits of spec section 6);
  `research/module_tests.md` (the rows that name the hook tests: the new class, the renamed server test);
  `src_vs_code/CHANGELOG.md` (new entry; the "path check is a follow-up" bullet superseded, history kept).
- **Done when.**
  - No document still says the hook allows a reader by name alone.
  - `SECURITY.md` carries every limit of spec section 6, in the spec's words or shorter with the qualifier kept.
  - `research/module_tests.md` names each new test, and says what the tests do not prove (agy honouring the hook; the race).
  - The post-deploy question is answered in the pull request: does this change what must be true on the box? `POST_DEPLOY.md`
    has twelve items, its cap, so a new item needs a named one removed.
  - `node .agents/conventions/tools/plan-lifecycle.mjs` is clean for this plan (status line, folder, index row).
- **Who.** Brief: Opus. Text: Sonnet. Review: Opus.
- **Depends on.** S1 as it stands on the branch, and the record of the local live run.

### Steps that are not stories — owned by the main session

1. **The local live run, before the merge** (this plan's Tests section). agy 1.5.9, WSL, the hook is the branch's built
   `coai-mcp` in the folder `Prepare` writes. Root under `/tmp`. Four probes: a file inside the root; outside by absolute
   path; by `root/../outside/x`; through a link inside the root. Evidence as in step 3. The record goes into
   `research/RESULTS_agy_read_file.md` (S2).
2. **Merge and release**, as in "Release" above: the mcp patch release, then the server release.
3. **The check on the Team server host, before the deploy** (gate finding b). It is a release step, not a coding story:
   nothing is written for it. With the RELEASE `coai-server` binary and the hook folder that binary itself prepared (agy's
   real generated hook, not a hand-written script), on the host's own agy:
   - an in-root read is allowed and the marker is read;
   - a read outside the job root is denied by absolute path, by `..`, and through a link inside the root.
   Evidence is, per probe, the tool call the model ATTEMPTED (name and `Path`) and the hook's decision with its reason
   sentence, taken from agy's output. An answer that only omits the secret is not evidence: a hook denial is not in agy's
   `denied_actions`, and a model may simply not try. A probe with no attempted call is repeated, not counted.
   The in-root probe matters as much as the three denials: an agy on the host whose payload lacks `cwd`, `workspacePaths`
   or `Path` would have EVERY read denied.
   Any outside read allowed, or the in-root read denied: no deploy.
4. **Deploy, then `POST_DEPLOY.md`** against the box, and its stamp.
5. **Rollback** if needed: re-dispatch `deploy-server.yml` with 0.16.2, as in "Release" above.
