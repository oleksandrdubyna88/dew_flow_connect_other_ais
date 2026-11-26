# Ledger epoch 1: sorting the done code (plan-change-control, step 5)

Judge: Opus, read only, 2026-11-26. Plan read: `v2/PLAN.md` ledger entries 1.1 to 1.17 and the record `### Re-review for epoch 2`
(tip `9157dc94`, feat/v2-ep3-e3-roles); `v2/epoch-3/e3-roles-complete/SPEC_CHANGES.md` sections M, N and O, and SPEC.md C2 and C9;
`v2/epoch-3/e4-adapters-coder-hardening/SPEC.md` sections A1, A3, A4 (D12, D13, D15 to D17) and R (tip `2a4ef2ee`, feat/v2-ep3-e4-plan).
Rule: `common.plan-change-control` (sha256 d890c388…), read in full through the resolver with `--task plan`.

Scope: `git log --no-merges c24e5141..HEAD -- src_mcp shared` in both trees. That gives 161 commits on E3 and 144 on E4, 123 of them
on both branches, so **182 distinct commits**. Three more code commits touch only the extension or the test seam (`d331eb67`,
`df6f7dae`, `fd6bc6cf`); they are added to G1, which makes **185**. Every verdict below comes from the code at the committed tip
(`git show HEAD:…`). Neither working tree was clean: E3 has uncommitted edits to `CoderBoundaryTable.cs`, the table and 4 tests, and
E4 has uncommitted s4 digest files. I did not judge either set.

## Table

| # | Work (story) | Commits | Where | Verdict | Action |
|---|---|---|---|---|---|
| G1 | v2 E1: only four verdicts; held gates, cards and ask_human removed; the data migration; the E1 gate fixes | 358b51f1 a44310ec afb213d7 d60b6949 83642b02 8d69f390 bafd56b8 + d331eb67 df6f7dae fd6bc6cf | both, pushed | fits | kept |
| G2 | v2 E3 (the v2 plan's epoch 1): cons and consask, cadence, security lane and operator commands removed, the conventions no longer enforced, --band-json, the client installer, the roles tool and its hook, workItem and --plan-report, the migration tables | 7e2a3770 c79c8533 dc67aea8 adfe93d9 c053a3ce 4ffc702c 23d33c5e 4a34ad63 d9c6100a ecb4bc53 2618bad3 f216f240 f90ad8c6 8e1aa94b 60037978 3b95b635 5af19ccb db769e79 83c6c070 ca65a435 31549ef1 | both, pushed | fits | kept. The installer only records versions (`ClientFinder`, `SetupRun`): agy is `unmeasured` by platform, not by version |
| G3 | epoch 3 E1 role jobs: work-plan reader and validator, RoleJobStore, the detached launcher, start_role, the router, the stops, BA, --role-delta, the acceptance, the E1 gate and CI fixes | 9dfbe4f6 35284f79 e2592965 f0e41bb9 f6c38eda d1f7914c 039b0e9e bbb2ec00 daed9f98 984e445a 0667f5d6 fbeb1042 17fed84a 3150d7de 967e0204 72ac14f4 4328e1cc 3d835875 aff0b0dd | both, pushed | fits | kept |
| G4 | epoch 3 E2 coder s1–s6: toolchain file, check runner, digest, CheckBoundary, worktree, the claude write launch, the OS layer, the commit and the checks of each turn, fix turns, failover, the inherited plan, the acceptance | 9d993bd9 80d3d7c4 3984a964 71d394bf 065223da 2f15e167 c621b0eb 93bc8d42 fcac290d 2598f90c 93692f98 e151d675 ca9e4038 a0d18e7c a22d795b f2e61f77 3298abca 6e3c1a1f b740d517 d02c3af6 1ce418c6 f9624d2b 5c92c2e0 739ebce2 5e0a34da af9b959a 732e9510 cdf5b10b 2bf2b69d 5b9b5ec0 7e13437e 38d79309 788a6462 85d4921e 69e9c74f 37d82b6d | both, pushed | fits; the claude-only route of `e151d675` and `3298abca` was superseded in e8101102 and fdf8a12f (E3) | kept |
| G4b | E2 s2 t3: the claude version floor 2.1.330 in `ClaudeWriteCapability` (entry 1.8 takes it out) | 7e47376e | both, pushed | fits (superseded in eabcbea1) | kept on E3, where `JudgeVersion` returns `Ready` for any release and `LastMeasuredRelease` is only a record. **The E4 tip still has `VersionFloor = new(2, 1, 330)` and refuses older releases** (`src_mcp/runners/Reviewers/ClaudeWriteCapability.cs:23`, `JudgeVersion`). E4 has to take eabcbea1 before it merges |
| G5 | E2 s7: the claude key store, --keys, the key pool, the no-leak scans, the three security judgements, a role job's key from the pool | f389e75a db229d46 566f51cf 65ae1f0e cca029ee bbc0e2a8 c6d37460 f4f2d953 3aa05038 a45768b9 11aec9d4 cb82b7ee 9392d443 f19a7fcb 75baf73a 26e5024f | both, pushed | fits | kept |
| G6 | E2 follow-ups: submodules read-only, the mounts and the retention, the check unit hides keys and other jobs, refusals that never echo a value, --expand-environment=no, TimeoutStopSec=10 | 7057cec6 901fdf3f 35d347e7 363fd49c 0dc0f94c 8e0a2d9b f288fff8 bf51e37b 8a4cd6ef e36136c1 afe3d20f dc084971 6786502c 7d644867 964f037a 96b3bba3 37c899e6 916bbf9c 424cf25f 2f80bed2 ececd61a 0689ffb5 f27ad93d | both, pushed | fits | kept |
| G7 | E3 s1–s2: the scout and the judge, their contracts, prompts, evidence check and scenarios | 83bffafe 6ebacbd2 51aa76e5 4640df10 c4a0a6c0 c82dba16 e2d01205 2a7eee70 fd099d97 | E3, pushed | fits | kept |
| G8 | E3 s3: review_plan(featureFolder), WorkPack, the coder bound to what the reviewers read, the outside dependencies in the pack text of every scope (entry 1.6) | 92190548 cffc8bf8 0d972821 21e29960 504dc6ab 8216af35 893382a3 68fbb3e3 2ca58a8c | E3, pushed | fits | kept. Entry 1.10 (the approval id covers a pack format version `V <n>`, s3 t4 d6) is not built yet: no format-version constant at the tip. The work adds to this code and removes nothing |
| G9 | E3 s4–s6 measures: role-stop fixtures, the inventory exemption, and the judgement addenda written into `shared/coder-boundary-releases.json` | 269a64d8 c1fcf843 a55296bc e748007f 653eeefe 220add50 d0dca16d | E3 (via the merged measure branch), pushed | fits | kept. The table's `releases` is empty, `measured` is history, and CX-P1, AG-P1 and AG-P2 are data that no code enforces |
| G10 | E3 s7: the vendor seam, prompt and runtime per vendor, the route and failover across vendors, preflight K2, the security-verify fixes | e8101102 3c101f27 fdf8a12f ca5f831d a7358b69 f5033a58 2eb40e31 f0b6832f c3eddb38 | E3, pushed | fits, except the rule "ship means launchable" (see G12) | kept |
| G11 | E3 s7 t3: the instruction step | ca1acc05 c905ed1d e2fe7a75 | E3, pushed | fits. The secret stop of c905ed1d (must-change M1) was superseded in e2fe7a75 | kept. At the tip `CoderInstructions.Judge` records `secrets [{class, line}]`, one `instructions-secret` notice and one log line, and the job goes on (entry 1.5). Open: entry 1.9 (s7 t3 d7). The stderr tail is redacted (`TailOf`), but `CoderInstructionsRecord.Argv` is still written as run. The fix adds to the code; nothing is cut |
| G12 | entry 1.8: a CLI version gates nothing (claude's floor becomes history, an unknown release is recorded, the installed release is shown beside the measured one) | eabcbea1 | E3, pushed | **cut**, in part. The version half fits. The rule "the table ships by verdict" contradicts entry 1.16, which was in the plan (bafc871e, 13:50) before this commit (14:03) | **fix forward** (pushed, unmerged: no revert of history). This is s7 t1 d3: the `launchable` mark, off for codex and agy, and `Ships = verdict ship AND launchable`. That work is uncommitted in the E3 tree now |
| G13 | E4 s1: the dotnet, node and formatter commands measured inside the check unit | 5795899a 202fd51e | E4, pushed | fits | kept (F3 rejected, SPEC R4) |
| G14 | E4 s2: shared/toolchains.json, manifests at a commit, the .NET and Node detectors, golden detection, the security verify fixes; ToolchainResolve | a3ae68db e9b8ee06 6b30d3d1 49ca9e01 f37927d5 a5136abb 7d0a83e7 1bff1660 9f9c7840 | E4, pushed | fits. In 9f9c7840 "the base's file wins" and "a lost step is refused" were superseded in afb4c834 | kept. At the tip the head's file runs (`ChecksAtBase.RunCoderJobCoreAsync` reads it at `headSha`), a deleted file falls to detection, and `FileChanges` and `Merged.Lost` report. That matches entry 1.13 (A1, D15 to D17) |
| G15 | E4 s3: the toolchain file's new keys, CheckRunner, the resolved checks, the clean before each round, the definition compare, the frozen gate, the allow list, and the owner decision | ce91556d 56fc5cc9 ff720ac2 5b146103 01f3548e ee1bfc38 a1c2715c 9cbd5e57 0c048ebc afb4c834 | E4, pushed | fits. The refusals of a1c2715c (the definition compare), 9cbd5e57 (the frozen gate) and 0c048ebc (the allow list) were superseded in afb4c834. The M16 half of 0c048ebc (a fresh home and temp per step, caches writable only in restore) is kept by A4 | kept. At the tip `FrozenDefinitions.IsAllowedChange` only classifies for `ChangedFiles`, no `ToolchainRefused` comes from a definition change, and the tests hold D12 and D13 as reported (`AChangedCheckDefinitionIsReportedTests`, `AHeadsChangesAreReportedTests`) |

Counts: 185 commits in 16 groups (G1 to G15 and G4b). **184 commits fit** (8 of them are fits (superseded)). **1 commit is cut, in part:
eabcbea1**, together with the parts of e8101102 and fdf8a12f that rely on it.

## The cut, named

- **`src_mcp/runners/Consultation/CoderBoundaryTable.cs` at E3 tip `9157dc94`:**
  - `CoderVendorRow.Ships => Verdict == Ship` (line 40), with its doc (lines 37 to 39).
  - `ShippedVendors` (line 73) and `WhyNot` (lines 78 to 91).
  - The type remark "they are launched by the verdict alone as soon as a vendor TYPE for them exists" (line 51).
  - `CoderVendorSet.Launchable` in `CoderVendors.cs:237`, which is built on these.
  - The plan says (SPEC C2 item 1, SPEC_CHANGES O, entry 1.16): a `ship` vendor can be launched only when a `launchable` mark is
    on. The mark is off for codex and agy, and ONE commit turns it on (s5 t5 for codex, s8 t6 for agy). Until then the product
    refuses that vendor's coder by data.
  - Harm today: none can be reached. The shipped `CoderVendorSet` implements only claude, so codex and agy are refused with "no
    launch built". But refusal sentences already list codex and agy as "shipped". And the first commit that adds a codex or agy
    vendor type would make that vendor launchable before G15, CX-T4, the environments.toml refusal and the refresh (A11) are
    built.
  - Action: fix forward in s7 t1 d3, as the next commit on feat/v2-ep3-e3-roles. The same commit should also fix the table's
    `note`, which still says "claude … keeps the floor of ClaudeWriteCapability".

## Not cut, but to carry

1. **The E4 branch lacks eabcbea1.** Its tip still refuses a claude below 2.1.330, a version gate that entry 1.8 removes. E4 must
   take E3's fix before it merges or ships a coder run.
2. **Stale text at the E4 tip.** These describe refusals the code no longer makes. Fix them forward with the next E4 commit:
   - the `<remarks>` of `ToolchainResolve` (lines 9 to 13: "the file at the base wins", "a step … is `toolchain_refused`", "must
     be the same at the head, or the job is refused");
   - the summary of `ChecksAtBase.RunDetectedAsync` ("the file at the base wins");
   - `shared/toolchains.json` `frozen.comment` ("A coder job's checks are refused … unless every path … is a plain SOURCE file");
   - `ToolchainDefaults.cs:92` ("the allow list of the interim gate").
3. **Open dod items** that add to code that fits: 1.9 (s7 t3 d7, argv redaction), 1.10 (s3 t4 d6, pack format version), and on
   E4 1.12 and 1.14 (s5 t5 d5, s5 t1 d5). Entry 1.13 is already met by afb4c834.
4. **Code written during the stop.** Stop commit 66bc4eea is at 12:52. After it came 2ca58a8c, e2fe7a75 and eabcbea1 on E3 (14:03)
   and afb4c834 on E4 (12:57; the E4 branch does not contain the stop commit). Each implements an owner decision of that day:
   1.6, 1.5, 1.8 and 1.7. SPEC_CHANGES M allows the server side of 1.5 and the measures s5 t6 and s6 t7 as "work that was
   running". It does not name 1.6, 1.7 or 1.8. Their content is sorted above. Whether they break step 2 of the rule is for the
   lead to record.
5. **Not sorted: code from before c24e5141.** Examples are `KimiAcpProfile.MeasuredVersions` and the codex tier range in
   `CodexTierSupport`. The second changes the argv and refuses nothing.

## Verdict

The code that ledger epoch 1 produced fits the accepted plan, with one exception. The refusals that entries 1.5 and 1.7 replaced
(the secret stop, the definition compare, the frozen gate, the allow list) are gone from both tips and are reported instead. The
claude floor that entry 1.8 removed is gone from the E3 tip only. The one cut is eabcbea1's "a vendor ships by its verdict"
(`CoderVendorRow.Ships`). It was written after entry 1.16 required a `launchable` mark. Fix it forward in s7 t1 d3; that work is
uncommitted in the E3 tree now. Codex and agy cannot be launched today, so it does no harm yet.
