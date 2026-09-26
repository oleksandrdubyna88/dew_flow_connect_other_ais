# PLAN — `PanelSettings.cs` is 1 330 lines against a ceiling of 800

> Status: **plan only, nothing implemented yet, 2026-09-26.** Scope: `src_mcp/src/Server/PanelSettings.cs`
> and new files beside it, plus the tests that call the members that move. No behaviour change anywhere
> in the series.
>
> Related docs: [PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md)
> (the same method, on `PanelService.cs`), [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md)
> (the extension's largest file), [PLAN_consult_on_a_cadence.md](../research/PLAN_consult_on_a_cadence.md)
> (whose open tail this is), [module_server.md](../research/module_server.md).

## 1. The problem

The coding rule allows 800 lines per file. On `origin/main` (24eb0451, 2026-09-26) two server files are
over it, and the consultation cadence added to both:

| File | Lines | Owner of the fix |
|---|---|---|
| `src_mcp/src/Server/PanelService.cs` | 1 714 | **already planned:** steps 5–7 of [PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md) (sweeps, resolve path, document stage). That plan's own DoD line says those steps are what bring it under 800. This plan does NOT touch `PanelService.cs` |
| `src_mcp/src/Server/PanelSettings.cs` | 1 330 | this plan |

`PanelSettings.cs` holds six things in one file. The line numbers below are from 24eb0451:

| Lines | What | External callers |
|---|---|---|
| `:9`–`:157` | `ProviderSettings` (one vendor row) and `DocumentReviews` | `ProviderSettings` is used across `src_mcp`; `DocumentReviews.` in 4 files |
| `:158`–`:487` | the `PanelSettings` record's SHAPE: every setting, its default, its docstring | everything |
| `:488`–`:696` | the data directory: `DefaultDataDir`, `DataSide`, `StorageNotes`, `DataDirectoryFor`, `DataRootFor`, `ResolveDataDir` | `DataDirectoryFor` 22, `DefaultDataDir` 6, `StorageNotes` 4, `DataRootFor` 3 |
| `:697`–`:1018` | the environment reader: `FromEnvironment` (`:697`), the three `WithCatalog` overloads, `Key` (`:869`), the `Why*` checks that fill `UnrecognisedSettings` (`:895`–`:988`), `ParsePromptRounds` | `FromEnvironment` 71 |
| `:1019`–`:1175` | vendors: `WithProvidersFrom`, `WithExecutable`, `ExecutableVariable`, `ParseVendors` (`:1054`), `DocumentReviewsOf`, `RuntimeOf` | `ParseVendors` 13, all in tests |
| `:1176`–`:1325` | roles and the small readers: `RolesSetting`, `ParseRoles` (`:1182`), `RoleGates`, `GateFor`, `Config`, `Flag`, `NotSwitchedOff`, `IntVar` (`:1300`), `CountVar` | internal |

Caller counts are `git grep -F "PanelSettings.<member>"` over `src_mcp/*.cs`, excluding the file itself.

## 2. The shape

- **`ProviderSettings.cs`**: `ProviderSettings`, `DocumentReviews` and `ProviderIdentity` (`:1326`). All three are
  top-level types already, so the move changes no caller. About 160 lines.
- **`PanelSettings.DataDirectory.cs`**: the data-directory block, as a `partial` of the record. It has 35
  external call sites, all spelled `PanelSettings.X`. A separate class would rewrite every one of them for
  no behaviour gained. About 210 lines.
- **`PanelSettings.Environment.cs`**: `FromEnvironment`, `WithCatalog`, `Key`, the `Why*` checks and the
  small readers (`Flag`, `IntVar`, `CountVar`, `NotSwitchedOff`), also as a `partial`, because
  `FromEnvironment` has 71 callers. About 450 lines.
- **`VendorSettingsReader.cs`**: `internal static class`, with `ParseVendors`, `ExecutableVariable`,
  `RuntimeOf`, `DocumentReviewsOf` and `WithExecutable`. This is a real extraction: its callers are 13
  tests, re-pointed in the same commit. About 160 lines.
- **`RolesSettingReader.cs`**: `internal static class`, with `RolesSetting`, `ParseRoles`, `Detail`,
  `Sentence`, `RoleGates`, `GateFor` and `Config`. About 110 lines.
- **`PanelSettings.cs` keeps the shape**: `:158`–`:487`, about 330 lines.

`partial` is used only where the caller count makes a real extraction all cost and no gain. Each
`partial` file holds ONE responsibility. The rule is about what a reader has to hold in their head, and a
split that keeps two jobs in one file does not meet it.

**Open for the plan gate:** whether `WithCatalog` (three overloads, `:704`–`:813`) belongs with the
environment reader or with the roles reader. It composes both. The default is the environment reader.

## 3. Build order

One commit per step, each proved on its own: the build is green, the full suites pass, and `git diff
--stat` shows a move and not a rewrite (`git diff -M --color-moved=zebra`).

1. `ProviderSettings.cs` (types only, no caller changes).
2. `PanelSettings.DataDirectory.cs` (`partial`).
3. `VendorSettingsReader.cs`, with the 13 test call sites re-pointed.
4. `RolesSettingReader.cs`.
5. `PanelSettings.Environment.cs` (`partial`), last, because steps 3–4 take its callees out first.
6. `research/module_server.md`: the settings section names the new files.

## 4. Test plan

- No new behaviour, so no new behaviour tests. The existing suites are the guard: `CoaiMcp.Tests` (5 324
  passed on 2026-09-26) must pass unchanged after every step, and so must the TypeScript
  defaults-agreement tests (`cadenceSettings.test.ts:20`, `panelServerDefaultsAgreement.test.ts:81`).
  Those read `PanelSettings.cs` BY PATH to check that the extension's defaults match the server's.
- **That is the trap in this plan:** a test that reads a default out of `PanelSettings.cs` as text breaks
  when the default moves file. Step 0 of each commit: `git grep -n "PanelSettings.cs" -- src_vs_code` and
  re-point every reader in the same commit. Watch it go red first by moving without re-pointing.
- Every step also checks every new file is under 800 lines, and `PanelSettings.cs` under 800 after step 5.

## 5. Definition of Done

- [ ] `PanelSettings.cs` at or under 800 lines; every file this plan creates under 800.
- [ ] No caller of `FromEnvironment` or `DataDirectoryFor` changed. The `partial`s kept them.
- [ ] Every test that reads `PanelSettings.cs` by path re-pointed in the commit that moved its text,
      each watched red first.
- [ ] `CoaiMcp.Tests` and `npm test` green after every step; `dotnet format` clean on the touched files.
- [ ] `research/module_server.md` names the new files; this plan promoted to `research/`.
- [ ] `PanelService.cs` is NOT in this plan's diff. Its tail stays with steps 5–7 of the round-engine plan.
