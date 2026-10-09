# PLAN — a WSL consultant can be checked from a Windows window

> Status: **plan only, nothing implemented yet, 2026-10-02.** Scope: `src_vs_code/src/consultantCheckRun.ts`,
> `src_vs_code/src/processLauncher.ts`, the server's data directory (a published executable path).
>
> Related docs: [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (this is its
> deferred slice), [module_extension.md](../research/module_extension.md).

## The goal

[PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) gives every
side its own Check: a Remote-WSL window checks the WSL consultant, a Windows window checks the Windows
one, and a Windows window shows the WSL side's health READ-ONLY. The operator chose (2026-10-02) to leave
"press Check on the WSL row from a plain Windows window" for later. This plan is that later.

## Why it was not built with the rest

- The Windows side does not know where the WSL `coai-mcp` is: the install record keeps a version, not a
  path (`src_vs_code/src/coaiInstall.ts:325`).
- Nothing in the extension launches `wsl.exe` today (no call site in `src_vs_code/src`), and the one
  launcher (`processLauncher.ts:141`) has no Windows→WSL shape.
- Guessing a distro is refused elsewhere on purpose (`escalationDirs.ts:38-40`).

## Build order

1. The server writes its own executable path and version into its data directory at startup
   (`<dataDir>/server.json`, atomic), so the other side can read it through the already-watched store.
2. The extension resolves a WSL side's distro from the watched UNC path and that `server.json`, and
   launches `wsl.exe -d <distro> -- <path> --check-consultant --caller <kind>` through `processLauncher`,
   with the tree-kill timeout of the main plan's check.
3. The read-only WSL block gains the Check button only when both are known; otherwise it keeps its
   sentence saying where Check runs.

## Test plan

- Distro resolution from `\\wsl.localhost\<d>\…` and `\\wsl$\<d>\…`; refusal without a `server.json`.
- The argv handed to the launcher, exact.
- A live run on this machine (Windows window, WSL `coai-mcp`).

## Definition of Done

- [ ] The server publishes its path; the extension never guesses one.
- [ ] Check works on the WSL row from a Windows window, live-verified.
- [ ] The read-only fallback still shows when the path is unknown.

## Boundary with the model catalog (2026-10-04)

| Item | Here | [PLAN_one_model_catalog.md](../research/PLAN_one_model_catalog.md) |
|---|---|---|
| the consultant check record | as written above | generalises `ConsultCheckState` into a check of any model (its D10); this plan keeps the cross-side launch |
