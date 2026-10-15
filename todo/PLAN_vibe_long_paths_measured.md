# PLAN — measure whether Vibe writes past 259 characters (long paths ON, a `\\?\` home, 8.3 names), before the path budget is relaxed

> Status: **plan only, nothing measured yet, 2026-10-15 — a follow-up measurement; nothing waits for it.** Scope: no product
> code. One RESULTS section ([RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §30) and, only if a cell
> passes, a later plan for `VibePathBudget` (`src_mcp/runners/Vibe/VibePathBudget.cs:24-33`) or `VibeLaunch.Built`
> (`src_mcp/runners/Vibe/VibeLaunch.cs:114`).
>
> Extracted from [PLAN_vibe_live_round_fixes.md](../research/PLAN_vibe_live_round_fixes.md) §6 (M-V18), as its DoD asked when
> that plan was promoted. Evidence it stands on: RESULTS §27 (the 259-character limit and the 156 characters Vibe writes below
> its home, probed with Vibe's own interpreter with `LongPathsEnabled = 0`) and §29 (the released 0.56.0 on short homes).
> Parent: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (D17 — the owned home).

## 1. Goal

Today the launch control refuses, on Windows, any Vibe home longer than 102 characters (`259 - 1 - 156`), whatever the
registry says. The refusal is right while nobody has checked that Vibe's interpreter is long-path aware: the flag is a promise
to long-path-AWARE processes only. If a cell below passes 3 of 3, a person with a long profile name or a deep homes root is
not refused for nothing. If none passes, the budget stays and this plan ends with a recorded "no".

Nobody is hurt while this is open: the budget only refuses early, with a sentence that names the cure (a short homes folder,
the `Vibe homes folder` setting).

## 2. Measurements (a scout's brief)

**Question.** Can the budget be relaxed — does Vibe 2.26.1 write past 259 characters (a) with `LongPathsEnabled = 1`, or
(b) given `VIBE_HOME=\\?\<full path>` with the flag at 0? And (c): the budget counts `Path.GetFullPath` as written, which
keeps an 8.3 short name (`PROFIL~1`) as written — does Windows count the long or the short spelling against the limit?

**Method.** Free first: Vibe's own `python.exe` (`%APPDATA%\uv\tools\mistral-vibe\Scripts\python.exe -I`) writes and
`os.replace`s files at 258 / 259 / 260 / 300 characters under `C:\testsToDelete-mp<n>` — RESULTS §27's probe, re-run in each
arm.

- Arm (a) needs an administrator to set the flag and a sign-out: **on a scratch machine or VM only, never the owner's without
  their explicit yes.** If no scratch machine exists, arm (a) is NOT run and §30 says so.
- Arm (b) needs no administrator.
- Arm (c) needs no administrator, and a volume with 8.3 names on (`fsutil 8dot3name query`); a folder whose long name is
  more than eight characters, made under `C:\testsToDelete-mp<n>`.

Then, per arm that passes the free probe: one paid tiny turn (`Reply with exactly: OK`) and one `--resume`d turn on a home of
130 characters (so the chunk lands at about 287), 3 of 3: exit, export, the session written and resumed, and the home
validation still passing (does `\\?\` survive into every path Vibe builds?). Never `--trust`, never `--add-dir`, never the
person's own `~/.vibe`. Spend stops at 100 000 input tokens across the arms.

**Output.** RESULTS §30, redacted as §29 is (`%TEMP%`, `%USERPROFILE%`, `<scratch>`; no profile name, key or e-mail), with
each cell's n and what was NOT checked beside it. A conclusion may not be wider than the cells that ran.

**Use.** A follow-up plan may then give `VibePathBudget` a measured exemption (a) or have `VibeLaunch.Built` hand Vibe a
`\\?\` home (b) — each only with its cell 3 of 3, and with a scenario written red first.

## 3. What grows, who retires it

Nothing in the product. The measurement makes folders only under `C:\testsToDelete-mp<n>` and one scratch `VIBE_HOME`; the
scout deletes only what it made, in the same session. Spend is capped in §2.

## 4. Build order

1. Free probe, arm (b) and arm (c), on this machine's real Vibe interpreter.
2. Arm (a) on a scratch machine, if there is one.
3. The paid turns for each arm that passed.
4. RESULTS §30; `todo/README.md` row updated or this plan promoted with a "no".

## 5. Definition of Done

- [ ] Each arm that ran has its cell (n = 3) in RESULTS §30; each arm that did not run says why.
- [ ] The conclusion names the conditions measured (Windows build, Vibe version, volume with or without 8.3 names).
- [ ] Either a follow-up plan for the budget exists in `todo/`, or §30 records that the budget stays.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` is clean.
