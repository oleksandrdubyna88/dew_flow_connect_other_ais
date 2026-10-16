# PLAN — E1.S2 spec: the reader's integrity proof (CORNERSTONE)

> Status: **coded and tested on the branch `feat/plan-gate-reads-committed-plan`, 2026-10-16 — not merged; cornerstone spec by Fable, written before its code.** Story
> E1.S2 of [PLAN_the_plan_gate_reads_the_committed_plan_E1.md](PLAN_the_plan_gate_reads_the_committed_plan_E1.md) (parent
> [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md) §4 steps 3–5, D5–D7).
> Its own file because the stories document is sent whole to a review gate and must stay short; it is promoted with E1's.
>
> Scope: `src_mcp` only — five pure units in `core/`, one reader in `runners/Collecting/`, one line moved out of
> `runners/Snapshots/SnapshotPlanner.cs`, three test files, three additions to `tests/TempGitRepo.cs`. Depends on E1.S1's
> four git reads (`EntryAtAsync`, `BlobAsync`, `GitAnswer.Truncated`); E1.S3 wraps this reader with steps 1, 2 and 6.

## S2.0 — What was measured (git 2.55.0.windows.3, .NET 10.0.12, Windows 11, 2026-10-15)

Every rule below rests on one of these; a reviewer can re-run them in a temp repository in two minutes.

| # | Measurement | Result |
|---|---|---|
| M1 | `git --literal-pathspecs ls-tree -l -z --full-tree <sha> -- <path>` | one record per entry: `<mode> <type> <oid> <size>\t<path>\0`; no quoting under `-z` (a path with a space came back verbatim); the size is `%7s` — right-aligned in seven columns (`      11`, `  263170`), **one** space before an eight-digit size (`10000000`), so "left-padded" means *one or more spaces* |
| M2 | the size of a non-blob | `-` for `160000 commit` (submodule) and `040000 tree` (directory) |
| M3 | a committed symlink | `120000 blob <oid>       7\tlink.md` — **its type is `blob`**; only the MODE says it is a link |
| M4 | a missing path; `PLAN.MD` for `plan.md` under `core.ignorecase=true`; `*.md` | empty output, **exit 0**, in all three — ls-tree matches a tree case-sensitively and `--literal-pathspecs` stops the glob |
| M5 | `todo/` (trailing slash) | lists the entries UNDER `todo` — one record whose path is `todo/PLAN x.md`; a two-file directory gives two records. So "several entries" and "a path that differs" are both reachable through a trailing slash |
| M6 | a bad sha to ls-tree; `cat-file blob` of a missing oid, or of a tree's oid | exit 128 each; `cat-file blob` of the empty blob `e69de29b…` exits 0 with nothing |
| M7 | `Process.StandardOutput` with `StandardOutputEncoding = Encoding.UTF8` (the launcher's decoder, `ProcessLauncher.cs:227`) reading a blob `EF BB BF` + `bom text\n` (12 bytes, oid `da68b30f…`) | **the BOM is dropped**: 9 characters, first `U+0062`; re-encoded 9 bytes; SHA-1 over `blob 9\0`+bytes = `3b3c2fc6…` (git's id for the BOM-less file), over `blob 12\0`+`EF BB BF`+bytes = `da68b30f…` = git's. The second step of D7 fires for EVERY BOM blob, not for a rare one |
| M8 | the same decoder reading `abc \xFF\xFE def\n` (11 bytes, oid `ab2a99ec…`) | 11 characters, two `U+FFFD`, re-encodes to **15** bytes — neither id matches |
| M9 | `git init --object-format=sha256`: HEAD, ls-tree oids, `hash-object --stdin` | all 64 hex; `hash-object` of the same bytes gives SHA-1 outside that repository and SHA-256 inside it; `sha256sum` over `blob 11\0hello plan\n` equals git's sha256 oid — the hash is the REPOSITORY's, not a constant |
| M10 | `update-index --add --cacheinfo 120000,<blob>,link.md` and `160000,<any 40 hex>,sub` on Windows, unprivileged | both accepted and committed; `100664` was silently normalised to `100644` (an odd mode can only come from a hand-built tree) |
| M11 | `hash-object -w --no-filters <file>` vs with filters under `core.autocrlf=true` | CRLF content hashes differently with filters; `--no-filters` hashes the bytes as they are |
| M12 | `git add -A` after a `--cacheinfo` entry with no working file | stages its DELETION — the fixture must commit the index without `add -A` |

## S2.1 — Units and where they live

Every unit in `core/` is `static`, takes values and returns values: no process, no clock, no file, no logger
(`ArchitectureTests.Core_ReferencesNoProcessOrNetworkAssemblies`). `CA1502: 4` (`src_mcp/CodeMetricsConfig.txt`) holds per
function without a suppression; the shapes in §S2.7 are cut for it.

| File | Unit | Why there |
|---|---|---|
| `src_mcp/core/Collecting/TreeEntry.cs` | `TreeEntry(Mode, Type, Oid, Size, Path)`, `TreeListing(Entries, Malformed)`, `TreeEntries.Parse(string)` | what git SAID, before any plan rule — beside `FileAtRevision.cs`, the core's other git-object shape; `CatFile.cs:10`'s `-1 = no size` convention is reused |
| `src_mcp/core/Collecting/BlobProof.cs` | `BlobId.Sha1Of(ReadOnlySpan<byte>)`, `BlobProof.Prove(captured, oid, size)` → `ProofVerdict` | the one comparison that decides (D7); SHA-1 beside the SHA-256 `DocumentRules.cs:118` already uses in core |
| `src_mcp/core/Collecting/LfsPointer.cs` | `LfsPointer.Header` (const string), `IsPointer(string)` | **the header moves here from `SnapshotPlanner.cs:60`** (reuse-first move 4 — two projects need it, runners references core): `SnapshotPlanner.LfsHeader` becomes `Encoding.UTF8.GetBytes(LfsPointer.Header)`; `ReviewSnapshotTests.AnLfsPointerFile_IsListedUnavailable_NotCopiedAsSource` green unchanged is the proof the move changed nothing |
| `src_mcp/core/Rounds/PlanEntry.cs` | `PlanEntry.Pick(TreeListing, path, sha)` → `EntryVerdict`; the entry sentences | what a PLAN requires of an entry: `DocumentRules.MaxBytes` lives in this folder |
| `src_mcp/core/Rounds/PlanSource.cs` | `PlanOrigin`, `PlanSource` — C1, **unchanged** | the record the session file carries (E1.S6) |
| `src_mcp/runners/Collecting/CommittedPlanReader.cs` | `CommittedPlanReader.ReadAsync(GitHistory git, string repoPath, string sha, string canonicalPath, CancellationToken ct)` → `Task<PlanRead>`; `PlanRead` — C2, **unchanged** | sequences the two git reads and the pure verdicts; beside `CommittedFile.cs`, whose `Reading` shape it does not reuse (it cannot carry an entry) |

## S2.2 — The types, final

C1 and C2 stand exactly as E1 §4 states them. The new core types:

```csharp
namespace CoaiMcp.Core.Collecting;
/// <summary>One `ls-tree -l -z` record. Size is -1 where git printed `-` (a submodule, a directory).</summary>
public sealed record TreeEntry(string Mode, string Type, string Oid, long Size, string Path);
/// <summary>Every record parsed, or — when one could not be — the entries before it and the head of the bad record.</summary>
public sealed record TreeListing(IReadOnlyList<TreeEntry> Entries, string Malformed);
public abstract record ProofVerdict
{
    /// <summary>The capture IS the blob. Text: what the reviewers get. BomDropped: the id matched only over EF BB BF + bytes.</summary>
    public sealed record Proved(string Text, bool BomDropped) : ProofVerdict;
    public sealed record Refused(string Sentence) : ProofVerdict;
}
namespace CoaiMcp.Core.Rounds;
public abstract record EntryVerdict
{
    public sealed record Accepted(Collecting.TreeEntry Entry) : EntryVerdict;
    public sealed record Refused(string Sentence) : EntryVerdict;
}
```

No computed instance property on any of them (the `SourceOutline.IsOutlined` lesson, `ISourceOutliner.cs:116-117`); none is
serialised — only C1's `PlanSource` reaches a file, and E1.S6 registers it.

## S2.3 — The `ls-tree -l -z` grammar and the entry rules (question 2, 3)

**Grammar** (M1–M3). Split the output on `\0` (empty entries removed — the output ends in `\0`). Each record: the text
before its FIRST `\t` split on `' '` with empty entries removed (that absorbs the padding) must be exactly four fields —
`mode` (six digits), `type` (letters), `oid` (40 or 64 hex), `size` (`-` → -1, else `long.TryParse`, `NumberStyles.None`,
invariant) — and the path is everything after the first `\t` (a path may itself contain a tab). A record that is not this
shape stops the parse: `Malformed` = the record's first 60 characters, the entries before it kept. Nothing throws.

**Rules**, in `PlanEntry.Pick(listing, path, sha)`, in this order; `sha12`/`oid12` = the first 12 hex; every sentence
names the cure. **None of them runs a process** — `Pick` is pure, and the reader calls `BlobAsync` only on `Accepted`;
the real-git tests prove it with a watching launcher that records every `git` argument list (§S2.8).

| Case | Decided by | Sentence |
|---|---|---|
| malformed record | `Malformed.Length > 0` | `git ls-tree answered a record this reader cannot parse for '{path}' at commit {sha12} ('{head}') — not a state of the repository; re-run, and if it repeats report it with the git version` |
| zero entries (M4) | `Entries.Count == 0` | `'{path}' is not in commit {sha12} — commit the plan on this branch first, or pass the path exactly as committed (case and separators count)` |
| several entries (M5) | `Count > 1` | `'{path}' names {n} entries at commit {sha12} — pass the path of ONE committed file` |
| a path that differs (M5) | `Entry.Path != path`, **ordinal** | `git listed '{entry}' for '{path}' at commit {sha12} — pass the path exactly as committed` |
| directory | `Mode == "040000"` | `'{path}' is a directory at commit {sha12} — pass the plan file inside it (todo/PLAN_x.md)` |
| symlink (M3) | `Mode == "120000"` — the TYPE is `blob` | `'{path}' is a symbolic link at commit {sha12} — pass the path of the file it points to` |
| submodule | `Mode == "160000"` | `'{path}' is a submodule at commit {sha12} — a plan is a file of this repository` |
| other mode | not `100644`/`100755` | `'{path}' has mode {mode} at commit {sha12} — only a regular file (100644 or 100755) can be a plan` |
| not a blob (defensive) | regular mode, `Type != "blob"` | `'{path}' is a {type}, not a blob, at commit {sha12} — report it; a regular file is always a blob` |
| size 0 | `Size == 0` | `'{path}' is empty at commit {sha12} (0 bytes) — commit the plan's text first` |
| size over the ceiling | `Size > DocumentRules.MaxBytes` | `'{path}' is {size} bytes at commit {sha12}, over the {MaxBytes}-byte ceiling — split it (an epic's stories document) and review the parts` |

Numbers are invariant, unformatted (`263170`), so a sentence is one line a caller can match.

## S2.4 — The `cat-file` outcomes (question 4)

One `CaptureGate(GitAnswer answer, string what)` in the reader answers a sentence or `""`, and is applied to BOTH reads
(`what` = `listing '{path}' at commit {sha12}` / `reading blob {oid12} of '{path}'`), in this order — each later word
presumes the earlier one did not hold, because a cut capture of a command that failed is still a failed command:

1. `!Ran` — timed out or cancelled (`GitHistory.cs:271`): `git did not finish {what} within 30 s — re-run when the
   machine is less busy`.
2. `!Ok` — exit ≠ 0 (M6): `git could not {what} — the commit or the object is not in this repository, or the object
   database is damaged; run git fsck`.
3. `Truncated` — exit 0, the flag set: `{what} was cut short — an incomplete read; nothing was reviewed; re-run, and if it
   repeats report it`. **Refused on the flag alone, whatever the proof would say** (T7's fake hands the WHOLE text with the
   flag set, and the id would match): the flag is evidence the id cannot see, and D6 exists beside D7 for that case. The
   launcher's ceiling is 8 Mi characters (`ProcessLauncher.cs:55`) against a 256 KiB blob, so in production the flag can
   only come from the drain grace — a killed child — which is exactly a capture nobody should trust.

## S2.5 — The proof (question 5, D7's table, measured M7–M8)

`BlobProof.Prove(captured, oid, size)`: `bytes = UTF8.GetBytes(captured)`; if `BlobId.Sha1Of(bytes) == oid` →
`Proved(captured, false)`; else if `Sha1Of([EF BB BF, ..bytes]) == oid` → `Proved(captured, true)`; else `Refused` —
worded **not UTF-8** when `captured.Contains('�')`, **differs from the blob** when it does not. The comparison is
ordinal over lower-case hex (`Convert.ToHexStringLower`; git prints lower-case). `Sha1Of(bytes)` = SHA-1 of
`blob {bytes.Length}\0` + bytes, `System.Security.Cryptography.SHA1.HashData` over one concatenated buffer.

| Blob | Capture | id over bytes | id over BOM+bytes | Verdict | Text handed on | `Bytes` recorded | `TextId` over |
|---|---|---|---|---|---|---|---|
| valid UTF-8, no BOM | the file | = oid | — | accepted | the capture | the entry's `Size` | the text handed on |
| starts with a BOM (M7) | the file **minus the BOM** | ≠ | = oid | accepted | the BOM-less capture | the entry's `Size` (12, not 9) | the BOM-less text |
| holds a literal U+FFFD (`EF BF BD`) — T19 | identical | = oid | — | accepted | the capture | `Size` | the text |
| invalid bytes (M8) | U+FFFD where they were | ≠ | ≠ | refused **not UTF-8** | — | — | — |
| whole id mismatch, no U+FFFD (a cut the flag missed; a wrong oid) | — | ≠ | ≠ | refused **differs from the blob** | — | — | — |
| two leading BOMs | one dropped; text starts `U+FEFF` | ≠ | = | accepted, text keeps its `U+FEFF` | the capture | `Size` | the text |
| a BOM and nothing else (3 bytes) | `""` | ≠ | = | proved, then refused by §S2.6 (`only whitespace`) | — | — | — |

The two sentences: `'{path}' at commit {sha12} is not UTF-8 — the capture does not re-encode to blob {oid12}; save the
plan as UTF-8 and commit it` · `the capture of blob {oid12} ('{path}') differs from the blob — {n} bytes re-encoded
against {size} declared; nothing was reviewed; re-run, and if it repeats report it`. **`Bytes` is always the entry's
`Size`** — what git holds, which the prompt's source line prints — never the re-encoded count (B18 pins it). **`TextId` is
`DocumentRules.ArtifactIdOf(text handed on)`**: the same text E1.S4's `Compose` hashes for `END OF PLAN <id>`, so the end
line and the record carry one id (a BOM re-added for the hash would split them — B19).

## S2.6 — Empty and LFS, after the proof (question 6)

Both run on the TEXT HANDED ON — after the proof, never on the raw capture — because an unproved capture is not evidence of
anything, and because the proof is what strips the BOM question away: a pointer file committed with a BOM decodes to text
that STARTS with the header (M7), so the prefix test on the proved text catches it and a test on the raw bytes would not.

- **Empty**: every character is whitespace or `U+FEFF` → `'{path}' at commit {sha12} holds only whitespace — commit the
  plan's text first`. (`Size == 0` was refused in §S2.3 before any read; this is the whitespace-only blob.)
- **LFS**: `text.StartsWith(LfsPointer.Header, StringComparison.Ordinal)` → `'{path}' at commit {sha12} is a git-lfs
  pointer, not the plan — commit the plan's text itself (git lfs untrack), or pass planText`.
- Then `Accepted(new PlanSource(PlanOrigin.Commit, sha, path, entry.Oid, entry.Size, ArtifactIdOf(text)), text, "")`.

## S2.7 — The SHA-256 repository (question 7)

M9: in a `--object-format=sha256` repository every id is 64 hex and a blob's id is SHA-256 over the same header — so a
recomputation with SHA-1 would refuse every valid plan as "differs", and one that picked the hash by oid length would be
guessing the repository's format from one string. `IsCommitish` (40 hex exactly, `GitHistory.cs:62`) refuses the sha, and
E1.S1's reads answer `Refused` without a process — but `Refused` is `Ran && !Ok && Out == ""`, which the reader cannot tell
from "git exited non-zero". So the reader checks **first, itself**, before any read: `!GitHistory.IsCommitish(sha)` →
`commit {sha} is {n} hex characters — committedPlan reads SHA-1 repositories (40-hex ids) only; for a SHA-256 repository
pass planText`. `BlobId.Sha1Of` stays SHA-1 only; the parser accepts a 64-hex oid (it is git's shape) and never sees one.

## S2.8 — The algorithm, per function ≤ 4

```
ReadAsync(git, repo, sha, path, ct):                       // 3 branches
  if !IsCommitish(sha)                     → Refused(§S2.7)
  listing = await git.EntryAtAsync(repo, sha, path, ct)
  if CaptureGate(listing, "listing …") is {Length>0} s → Refused(s)
  return PlanEntry.Pick(TreeEntries.Parse(listing.Out), path, sha) switch
    { Refused r → Refused(r.Sentence), Accepted a → await ReadBlobAsync(git, repo, sha, path, a.Entry, ct) }
ReadBlobAsync(…, entry, ct):                               // 2
  answer = await git.BlobAsync(repo, entry.Oid, ct)
  gate = CaptureGate(answer, "reading blob …"); if gate.Length > 0 → Refused(gate)
  return Prove(answer.Out, sha, path, entry)
Prove(captured, sha, path, entry):                         // 3
  BlobProof.Prove(captured, entry.Oid, entry.Size) switch { Refused r → Refused(r.Sentence), Proved p → Body(p.Text, …) }
Body(text, sha, path, entry):                              // 2 — whitespace, LFS, then Accepted (§S2.6)
CaptureGate(answer, what): !Ran → s1; !Ok → s2; Truncated → s3; else ""   // 3
TreeEntries.Parse(out): foreach record → Entry(record) ?? return new(seen, Head(record)); return new(seen, "")   // 2
TreeEntries.Entry(record): tab = IndexOf('\t'); tab < 0 → null; Fields(record[..tab]) is 4 fields → Of(fields, path) : null   // 3
TreeEntries.Of(fields, path): IsMode && IsHex && TrySize → entry : null   // 3 (TrySize: "-" → -1, else long.TryParse)
PlanEntry.Pick(listing, path, sha): Shape(…) ?? One(listing.Entries[0], path, sha)   // Shape: malformed/zero/several = 3
PlanEntry.One(entry, path, sha): path differs → refused; else ByMode(entry, …)        // 1
PlanEntry.ByMode: switch Mode { 040000, 120000, 160000, 100644 or 100755 → Regular, _ → other }   // 4 arms, ≤ 4
PlanEntry.Regular: Type != blob → ; Size == 0 → ; Size > MaxBytes → ; Accepted   // 3
BlobProof.Prove: id == ; bomId == ; Contains(FFFD) ? notUtf8 : differs             // 3
```

## S2.9 — Worked examples (the fixture of §S2.0; oids are git's, measured)

| Path | ls-tree record (`\t` shown as `→`) | Outcome |
|---|---|---|
| `plan.md` (`hello plan\n`) | `100644 blob 707279fd268816a6390d22d1db277a907635e45f      11→plan.md` | accepted: `PlanSource(commit, sha, "plan.md", "707279fd…e45f", 11, ArtifactIdOf("hello plan\n"))`, text `hello plan\n` |
| `bom.md` (`EF BB BF bom text\n`) | `100644 blob da68b30f1b6e09152fb89ef7f458b8c88fa8ddd5      12→bom.md` | capture `bom text\n` (9 chars, M7); `3b3c2fc6…` ≠, BOM id `da68b30f…` = → accepted, `Bytes` **12**, text 9 chars, `TextId` over `bom text\n` |
| `bad.md` (`abc FF FE def\n`) | `100644 blob ab2a99ec86659da93ded257f43192597b1820798      11→bad.md` | capture re-encodes to 15 bytes, two U+FFFD → refused `not UTF-8` |
| `empty.md` | `100644 blob e69de29bb2d1d6434b8b29ae775ad8c2e48c5391       0→empty.md` | refused `is empty … (0 bytes)` — no `cat-file` |
| `big.md` (263 170 bytes) | `100644 blob 2d65614f…  263170→big.md` | refused `over the 262144-byte ceiling` — no `cat-file` |
| `lfs.md` (the `ReviewSnapshotTests.cs:308` pointer) | `100644 blob 60c8d8ab…     130→lfs.md` | proved whole, then refused `git-lfs pointer` |
| `link.md` | `120000 blob 76cc6a121170ba33ca63285b71c1eec4e7dfdabb       7→link.md` | refused `symbolic link` — type `blob`, mode decides (M3); no `cat-file` |
| `sub` | `160000 commit 0123456789012345678901234567890123456789       -→sub` | `Size = -1`; refused `submodule`; no `cat-file` |
| `todo` | `040000 tree 954c0de97d2bcaae55f9b6dc32b876fd9d6f24d0       -→todo` | refused `directory`; no `cat-file` |
| `todo/` | one record, path `todo/PLAN x.md` | refused `git listed 'todo/PLAN x.md' for 'todo/'` (E1.S3 refuses the slash earlier; the rule stands on its own) |
| `d/` (two files) | two records | refused `names 2 entries` |
| `missing.md`, `PLAN.MD`, `*.md` | nothing, exit 0 (M4) | refused `not in commit {sha12}` (T21) |
| any path, 64-hex sha | no process | refused `commit … is 64 hex characters` |
| `plan.md`, launcher answers the whole text with `Truncated: true` | — | refused `cut short — an incomplete read` (T7) |

## S2.10 — Tests (xUnit v3 + FluentAssertions; RED on the assertion under a compiling stub)

The stubs: `TreeEntries.Parse` → `new([], "")`; `PlanEntry.Pick` → `Refused("")`; `BlobId.Sha1Of` → forty zeros;
`BlobProof.Prove` → `Refused("")`; `LfsPointer.IsPointer` → `false`; `CommittedPlanReader.ReadAsync` → `Refused("")`.
A test the stub already satisfies is marked (stub-green) and earns its red through a break-it in §S2.11.

`src_mcp/tests/ATreeEntryIsReadExactlyTests.cs` — pure, the records are string literals of §S2.9:

| Test | RED under the stub |
|---|---|
| `ARecord_IsModeTypeOidPaddedSizeTabPath` (`      11`, `  263170`, `10000000` — one, two, zero extra spaces) | `Expected Entries to contain 1 item(s), but found 0` |
| `ADashSize_IsMinusOne` (submodule and tree records) | same |
| `APathMayHoldATab_TheFirstTabSplits` (`…\tdocs/a\tb.md`) | same |
| `AMalformedRecord_StopsTheParse_AndKeepsTheOnesBefore` (three fields; a non-hex oid; `12x` size; no tab) | `Expected Malformed not to be empty` |
| `AnEmptyOutput_IsNoEntries_NotMalformed` | stub-green; red under B21's sibling check (`Malformed == ""`) is held by `ASeveralEntries…` |
| `ZeroEntries_IsNotInCommit` / `SeveralEntries_IsOneFileWanted` / `AnEntryWhosePathDiffers_IsRefused` (`todo/PLAN x.md` for `todo/`; `Plan.md` for `plan.md`) | `Expected sentence to contain "not in commit" but found ""` (each its own word) |
| `AMode_DecidesBeforeTheType` ([Theory]: `120000 blob` → symbolic link, `160000 commit` → submodule, `040000 tree` → directory, `100664 blob` → `mode 100664`, `100644 tree` → `not a blob`) | `… to contain "symbolic link" but found ""` |
| `ASizeOfZero_AndOneOverTheCeiling_AreRefusedWithTheNumbers` (`0`, `262145` → refused naming `262144`; `262144` → accepted) | `… to contain "empty" but found ""` |
| `TheSentencesNameThePathAndTheShortSha` (every refusal holds `'{path}'` and the 12-hex sha) | `… to contain "'plan.md'"` |

`src_mcp/tests/ACaptureIsProvedToBeTheBlobTests.cs` — pure:

| Test | RED under the stub |
|---|---|
| `TheBlobIdOfAText_IsGits` (`hello plan\n` → `707279fd…`; `""` → `e69de29b…`; `EF BB BF bom text\n` → `da68b30f…`) | `Expected "707279fd…" but found "0000…"` |
| `AValidCapture_IsProved_AndItsTextIsTheCapture` | `Expected Proved, found Refused` |
| `ACaptureMissingTheBlobsBom_IsProvedOverTheBom_AndSaysSo` (`bom text\n` against `da68b30f…`, 12 → `Proved(…, BomDropped: true)`) | same |
| `ALiteralReplacementCharacter_IsProvedLikeAnyOtherCharacter` (text with `�`, oid computed by `Sha1Of` of its own bytes) | same; under B8: `Refused "not UTF-8"` |
| `ALossyDecode_IsNotUtf8` (`abc �� def\n` against `ab2a99ec…`, 11) | `… to contain "not UTF-8" but found ""` |
| `AWholeMismatchWithoutReplacement_DiffersFromTheBlob` (`hello plan` against `707279fd…`, 11 → sentence names `10` and `11`) | `… to contain "differs from the blob"` |
| `TheHeaderIsGitLfsSpecV1_AndOnlyAPrefixMatches` (`LfsPointer.Header` equals the `ReviewSnapshotTests.cs:308` pointer's first line; a plan MENTIONING the header mid-text is not a pointer) | `Expected IsPointer to be true` |

`src_mcp/tests/ACommittedPlanIsReadByItsBlobTests.cs` — real git through `TempGitRepo` (§S2.12); a private `Watching`
launcher (the `AReviewTreeTests.cs:707` shape) records every `git` argument list; `FailsOn`-style fakes (`AReviewTreeIsGivenBackTests.cs:539`)
answer `cat-file` only:

| Test | Row | RED under the stub |
|---|---|---|
| `ACommittedPlan_IsReadWhole_AndItsSourceIsTheEntry` (`plan.md`: `PlanSource` fields exact, `Note == ""`) | — | `Expected Accepted, found Refused ""` |
| `ASymlink_ASubmodule_ADirectory_AreRefusedByTheirWord_BeforeAnyCatFile` (M10 entries; `Watching` saw `ls-tree` and no `cat-file`) | T6 | `… to contain "symbolic link" but found ""` |
| `ACaptureTheLauncherSaysWasCut_IsAnIncompleteRead` (fake: whole text, `Truncated: true`, exit 0) | T7 | `… "incomplete read"`; under B1: `Expected Refused, found Accepted` |
| `AnInvalidUtf8Blob_IsNotUtf8` / `ABomBlob_IsAccepted_WithTheBlobsByteCount` (`Bytes == 12`, text `bom text\n`, `TextId == ArtifactIdOf("bom text\n")`) | T8 | `… "not UTF-8"`; `Expected Bytes 12` — under B18: `found 9` |
| `TheTextIdIsOverTheTextHandedOn` (BOM blob: `TextId == ArtifactIdOf(accepted.Text)`) | — | stub-red with the row above; under B19: two different ids |
| `AnEmptyFile_AWhitespaceFile_A257KiBFile_AnLfsPointer_AreRefused` (`big.md` and `empty.md`: no `cat-file`) | T9 | `… "empty"`; under B20: `Watching` lists a `cat-file` for `big.md` |
| `APlanHoldingALiteralReplacementCharacter_IsAccepted` | T19 | `Expected Accepted` |
| `APathNotInTheCommit_IsRefusedNamingIt_BeforeAnyCatFile` (file written, not committed; `PLAN.MD` for `plan.md`) | T21 | `… "not in commit"` |
| `ASha256Repository_IsRefused_WithNoProcess` (`InitAsync(…, objectFormat: "sha256")`; `Watching` saw nothing after init) | — | `… "SHA-1"` |
| `ATrailingSlash_IsNotOneFile` (`d/` → `names 2 entries`; `todo/` → `git listed`) | — | `… "entries"` |

## S2.11 — Break-its (production mutations; red with the symptom, restored, green; both runs in the summary)

| # | Mutation | Test that goes red | Symptom |
|---|---|---|---|
| B1 (parent) | drop step 3 of `CaptureGate` | `ACaptureTheLauncherSaysWasCut_IsAnIncompleteRead` | `Expected Refused, but found Accepted` — the whole text, id matching, flag ignored |
| B4 (parent) | `Prove` answers `Proved(captured, false)` without hashing | `AnInvalidUtf8Blob_IsNotUtf8` | `Expected Refused … found Accepted` with two U+FFFD in `Text` |
| B8 (parent) | test `Contains('�')` before the id | `ALiteralReplacementCharacter_IsProvedLikeAnyOtherCharacter`, T19 | `Expected Proved, found Refused "… not UTF-8"` |
| B16 | decide symlink by `Type` instead of `Mode` | `AMode_DecidesBeforeTheType`, T6 | the `120000 blob` row accepted — `Expected … "symbolic link"` |
| B17 | parse the size with `long.Parse(field)` over the padded text (no empty-entry removal) | `ARecord_IsModeTypeOidPaddedSizeTabPath` | `Malformed` names the `      11` record |
| B18 | `Bytes = UTF8.GetByteCount(text)` | `ABomBlob_IsAccepted_WithTheBlobsByteCount` | `Expected Bytes to be 12, but found 9` |
| B19 | `TextId = ArtifactIdOf("﻿" + text)` when the BOM was dropped | `TheTextIdIsOverTheTextHandedOn` | two ids |
| B20 | move the ceiling check after `cat-file` | `…A257KiBFile…` | `Watching` lists `cat-file blob 2d65614f…` |
| B21 | drop the `Count > 1` rule (take `Entries[0]`) | `SeveralEntries_IsOneFileWanted`, `ATrailingSlash_IsNotOneFile` | `d/a.md` accepted for `d/` |
| B22 | compare the entry's path `OrdinalIgnoreCase` | `AnEntryWhosePathDiffers_IsRefused` | `Plan.md` accepted for `plan.md` |
| B23 | `IsPointer` = `Contains(Header)` | `TheHeaderIsGitLfsSpecV1_AndOnlyAPrefixMatches` | a plan quoting the header refused as a pointer |

## S2.12 — Fixtures on Windows (question 8; M10–M12)

Three additions to `src_mcp/tests/TempGitRepo.cs`, each a few lines; `WriteAsync` stays text-only:

- `StageBlobAsync(string name, byte[] bytes, string mode = "100644")`: `File.WriteAllBytes` into the working tree, then
  `git hash-object -w --no-filters -- <name>` (M11 — bytes as they are, whatever `core.autocrlf` says) → oid, then
  `git update-index --add --cacheinfo <mode>,<oid>,<name>`; returns the oid. BOM, invalid-UTF-8, whitespace-only and
  pointer blobs are built this way — `ProcessRequest.StdIn` is a string, so `--stdin` cannot carry invalid bytes.
- `StageEntryAsync(string name, string mode, string oid)`: `update-index --add --cacheinfo` alone — `120000,<blob oid>`
  for a symlink (no symlink privilege needed, M10), `160000,<any 40 hex>` for a submodule (no submodule needed).
- `CommitIndexAsync(string message)`: the commit half of `CommitAsync` (`TempGitRepo.cs:54-59`), extracted — `CommitAsync`
  calls `add -A` then it. The staged-entry tests commit through `CommitIndexAsync`, because `add -A` would stage the
  deletion of an entry with no working file (M12).
- `InitAsync` gains `string objectFormat = "sha1"` → `git init -b main --object-format=<x>` (accepted by git 2.55 for
  both values, measured); the one SHA-256 test passes `"sha256"`.

## S2.13 — Decisions closed (the nine questions, one line each)

1. Six files (§S2.1): git-object shapes in `core/Collecting`, plan rules in `core/Rounds`, the reader in
   `runners/Collecting`; **the LFS header is one constant in core** (`LfsPointer.Header`), `SnapshotPlanner` reads it.
2. Grammar: `mode type oid size\tpath\0`, size right-aligned in ≥ 7 columns (one or more leading spaces), `-` → -1; zero
   entries = exit 0 + empty; several and "differs" are reachable through a trailing slash; a bad record stops the parse.
3. Eleven refusal sentences (§S2.3), decided by MODE before type — a symlink's type is `blob`; `Pick` is pure, `cat-file`
   runs only on `Accepted`, proved by the watching launcher.
4. `!Ran` → `!Ok` → `Truncated`, one gate for both reads; a cut capture is refused on the flag even when the id matches.
5. D7 as the table of §S2.5: the decoder DROPS a leading BOM (measured), the BOM step fires for every BOM blob; text handed
   on = the BOM-less capture; `Bytes` = the entry's size; `TextId` over the text handed on; "not UTF-8" iff U+FFFD present.
6. Whitespace/U+FEFF-only and LFS are tested AFTER the proof, on the text handed on; prefix match, ordinal.
7. A sha that is not 40 hex is refused by the reader itself, first, naming SHA-256 and `planText` as the cure; SHA-1 only.
8. Fixtures: `StageBlobAsync` (bytes via `hash-object -w --no-filters` + `--cacheinfo`), `StageEntryAsync` (`120000`/`160000`
   without privileges), `CommitIndexAsync`, `InitAsync(objectFormat)`.
9. C1 and C2 confirmed unchanged; eight break-its added (B16–B23) beside B1, B4, B8.

*Stated limits:* an odd mode (`100664`) cannot be committed by `update-index` (M10) and is tested only purely; a hand-built
tree with two case-twins is one entry each under a literal pathspec; the empty check cannot be observed before or after the
proof on a BOM-only blob (both refuse), so no break-it moves it.

- **Done when.** The three test files green with every row above; B1, B4, B8 and B16–B23 recorded red-then-green;
  `ReviewSnapshotTests` and `ArchitectureTests` green unchanged; `dotnet format --verify-no-changes` clean over the new
  files; the whole `CoaiMcp.Tests.exe` green. No `module_server.md` change here — E1.S5 writes the tool paragraph.
- **Size.** ≈ 520 changed lines: ≈ 170 code (six files, none past 110 lines, one line in `SnapshotPlanner.cs`, ≈ 20 in
  `TempGitRepo.cs`), ≈ 350 tests. Fable's cut, replacing the ≈ 450 estimate: the pure tables are most of the growth.
- **Cornerstone — spec by Fable** · **Coder: sonnet** · review: opus.
