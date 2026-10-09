# PLAN — a question-consultant root of the other OS is skipped on this side, not refused

> Status: **IMPLEMENTED, 2026-10-09 — to ship with the next coai-mcp and extension releases.** Scope: coai-mcp
> `QuestionConsultSettings.cs` / `QuestionAdmission.cs` / `StartupNotices.cs`; the extension's `pathFamily.ts`,
> `qconsultView.ts`, `qconsultWrite.ts`, `qconsultHost.ts`, `escalationDirs.ts`; `shared/path-family-vectors.json`.
> **Deviations:** the plan round added that a root spelled like the other OS but EXISTING here is this side's (Windows
> root-relative `/work`); the first code round added that such a root is resolved against the SYSTEM drive on both sides
> (never the process's current drive) and kept qualified, and that the page's existence answers are cached per root list;
> the cadence consultant found the page's refusal checks still judged the root AS WRITTEN (`/Windows` got no refusal on
> the page while the server refused `C:\Windows`), so `rootRefusal` and Add a folder's duplicate check now judge the
> qualified path too. **Compatibility decision (coordinator, 2026-10-09):** before this change the server kept
> `Full(root)`, so a hand-written `/work` meant the SERVER's current drive (`D:\work` for a server started from D:); it
> now means `%SystemDrive%\work` on both sides. Kept deliberately — one base for the page and the server — and pinned by
> `TheDeliberateChange_AHandWrittenRootRelativeRoot_NowMeansTheSystemDrive_NotTheServersCurrentDrive`; only a
> hand-written root-relative root is affected, because Add a folder always writes a drive-qualified path.
> The second code round added the live seam leg `scripts/seam-qconsult-roots.mjs` (the page's decision against the real
> binary's, through `--providers` and `ask_consultants` — no new CLI flag), an UNKNOWN state for a `stat` that fails with
> anything but ENOENT/ENOTDIR (never called the other side's, never cached), and one disk probe per other-side root.
> The third code round made the SERVER answer the same three states: `QuestionRoots.PresenceOf` tells a folder, a
> confirmed absence (not found, not a directory, a file, a name this OS cannot spell) and UNKNOWN (access denied, an
> I/O failure) apart — `Directory.Exists` had answered false for all of them, so an inaccessible `/work` was skipped
> as the other side's. **Rule: only a CONFIRMED absence makes a root the other side's; an unknown root stays this
> side's, goes through the ordinary checks, and is refused by name ("could not be checked on this machine").** The
> `existence` vectors carry `unknownHere`, and both halves read every vector row checked rather than cast.
> The plan was written down after the work, when the gate asked for the file; the gate's plan round ran before the code
> round on the same text.
>
> Related docs: [module_server.md](module_server.md), [module_extension.md](module_extension.md),
> [module_tests.md](module_tests.md).

## Symptom

Reported by the operator on 2026-10-09 (extension 0.65.0, coai-mcp 0.44.2). The operator works in a WSL remote window and
in a plain Windows window; VS Code's user settings are shared between them, so a question-consultant disk row's root
written from WSL (`/home/jinx/git`) is read on Windows too. On every Windows start coai-mcp raised a FAILURE toast
(`server-did-not-understand-a-setting`):

> COAI_QCONSULT_ROOTS: '/home/jinx/git' is not a directory on this machine — a disk row needs a folder that exists

with the cure *"The server is running without those settings"* — which overstated it: only that root was dropped. The
operator's words: *"it should see which OS it is running on."*

## Goal

A root that belongs to the other OS is not this machine's folder: on this side it is skipped — no error, no toast — and
the rest of the question-consultant settings apply. The Settings page says plainly that the root is the other side's.
A disk row with no root of this machine is inactive here, said so; on the other side it keeps working.

## Design

1. **Spelling** — `QuestionRoots.OtherSide(root, windows)` (`src_mcp/src/Server/QuestionConsult/QuestionConsultSettings.cs:263`)
   and `spelledForTheOtherOs` (`src_vs_code/src/pathFamily.ts:33`): on Windows a POSIX absolute path with ONE leading
   `/`; on Linux/WSL/macOS a drive path `X:\`/`X:/` or a leading `\` (UNC, `\\wsl.localhost\…`). `//server/share` is
   nobody's; relative paths and `C:work` stay this side's. The platform is injected (`SystemPlaces.Windows`).
2. **Existence** (the plan round) — `OtherSideHere(root, windows, existsHere)` (`QuestionConsultSettings.cs:277`,
   `pathFamily.ts:42`): a root spelled like the other OS that EXISTS here as a directory is this side's and goes through
   the normal D14(c) checks.
3. **One base drive** (the first code round) — on Windows a root-relative root is resolved against the system drive
   (`%SystemDrive%`, else `C:`): `Qualified` (`QuestionConsultSettings.cs:225`) and `qualified` (`pathFamily.ts:77`),
   used for the existence decision, every D14(c) check and the root the server keeps, so grants, `--add-dir` and the
   prompt see `C:\work`.
4. **Skipped, not refused** — such a root leaves the list before the checks, is kept in
   `QuestionConsultSettings.OtherSideRoots` (`QuestionConsultSettings.cs:38`), and is said only as an Information line in
   the server's log (`StartupNotices`). A disk row with no root of this machine is `disabled` ("inactive on this side —
   add a folder of this machine"); one with no roots at all keeps `blocked`.
5. **The page** — the host stats only other-OS-spelled roots (`existingHere`), cached per (platform, system drive, root
   list) by `RootExistence` and forgotten on a `coai.qconsultRoots` change, Add a folder or Remove; the page decides as
   the server does and names such a root *the other side's folder — this side skips it*.
6. **One rule, two languages** — both halves read `shared/path-family-vectors.json` (`vectors` = spelling, `existence`,
   `resolution`), as `capability-matrix-vectors.json` holds the capability matrix. `escalationDirs.ts`'s own POSIX-on-
   Windows check now calls `pathFamily.isPosixAbsolute` (`pathFamily.ts:19`).

Not this plan's: a missing root of THIS OS keeps today's behaviour (a named warning) — proposed separately; other
settings that carry paths across the shared layer (`executablePath`, `COAI_ROUND_WORKTREES`, the data directory) were
not audited.

## Build order (as it happened)

1. RED (C#) with the operator's exact message, then the skip, the `disabled` status and the log line.
2. RED (extension) on the page, then `pathFamily.ts` and the vector file.
3. Plan round → existence (`/work`), RED first.
4. Code round → the system drive and the cache, RED first with an injected drive `Q:`.

## Test plan

- C#: `QuestionConsultSettingsTests`, `QuestionAdmissionTests`, `TheStartupNotesAreWrittenDownTests`; the vector file.
- Extension: `pathFamily.test.ts` (the three vector sets, the cache), `qconsultSection.test.ts` (the page RUN through
  `panelPageHarness` + `pageTree`), `qconsultWrite.test.ts`.
- Break-it per filter, branch, log line, existence point, qualification point and cache key.
- Full C# suite, `npm test`, eslint, the family checks.

## Definition of Done

- [x] A WSL root in a Windows window raises no toast; the rest of the settings apply (RED first, teeth shown).
- [x] A root-relative Windows root that exists is this side's, judged and kept on the system drive.
- [x] The page and the server decide the same, from one vector file.
- [x] Docs and help (five languages) updated; this record in `research/`.
