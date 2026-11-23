# coai v2 — epoch 1, epics E2 (thin shell) + E4 (shell modules) — combined epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/extension.md (incl. §10 decisions) and research/RESULTS_v2_start.md (measurements).

## v2/epoch-1/e2-thin-shell/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-thin-shell
title: "Thin extension shell, ≤ 2 s start, chat carried"
mvp: true
half: extension
dependsOn: []
parallelWith:
  - e1-gate-simplify
doneWhen:
  - "no onStartupFinished; activate() only registers (entryPurity test)"
  - "entry firewall checks inputs and output imports; byte budgets green"
  - "M2 = command → the REAL sidebar menu (section headers Notifications, Active gates, Active consultations, Phrases, Bugz) painted AND interactive (page posts menuReady; a skeleton or inert snapshot does not count) ≤ 2000 ms in every sample, on Linux CI, Windows CI and Remote-WSL (owner, 2026-11-23); M1 ≤ 100 ms"
  - "v1 baseline and per-step marks of the old activate() recorded"
  - "joint smoke with the E1 binary passes; settings-mirror check done"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-shell-core
title: "Entry, lazy loader, ids, per-module bundles, firewall, budgets"
dependsOn: []
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t1-entry-lazy/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-entry-lazy
title: "shell/entry.ts, lazy.ts, ids.ts; activation events onView/onWebviewPanel/onUri and implicit command events"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_vs_code/shell
  - src_vs_code/package.json
dod:
  - id: d1
    item: "activate() makes only register* and subscriptions.push calls"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t2-bundles/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-bundles
title: "scripts/bundle-entries.json and one CommonJS esbuild entry per module and page"
kind: code
complexity: 5
dependsOn:
  - t1-entry-lazy
parallel: false
files:
  - src_vs_code/scripts
  - src_vs_code/package.json
dod:
  - id: d1
    item: "dist/extension.js and dist/modules/*.js are emitted; the packaged VSIX contains every chunk"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t3-firewall-budgets/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-firewall-budgets
title: "entry-firewall.mjs (metafile inputs and outputs[*].imports) + byte budgets + ESLint no-restricted-imports on the shell"
kind: code
complexity: 5
dependsOn:
  - t2-bundles
parallel: false
files:
  - src_vs_code/scripts/entry-firewall.mjs
  - src_vs_code/eslint.config.mjs
dod:
  - id: d1
    item: "a planted require('node:fs') in the entry fails CI"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t4-entry-purity/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-entry-purity
title: "entryPurity.test.ts runs the entry against a recording vscode fake"
kind: test
complexity: 4
dependsOn:
  - t1-entry-lazy
parallel: true
files:
  - src_vs_code/src/test/entryPurity.test.ts
dod:
  - id: d1
    item: "the test fails on any call other than register* or subscriptions.push"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s2-measure
title: "Cold-start measurement M1/M2/M3"
dependsOn:
  - s1-shell-core
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t0-v1-baseline/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t0-v1-baseline
title: "Measure the v1 cold start (same harness) and record it as the baseline"
kind: measure
complexity: 3
dependsOn: []
parallel: true
files:
  - research/RESULTS_v2_start.md
dod:
  - id: d1
    item: "v1 M2 recorded on Linux with three samples"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t1-measure-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-measure-launch
title: "run-host.mjs measure launch: fresh profile and data dir per sample, isActive false asserted, driver clock, fresh ready ack, M1/M2/M3"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_vs_code/scripts/run-host.mjs
  - src_vs_code/src/test/host/measureScenario.ts
  - src_vs_code/shell/measure.ts
dod:
  - id: d1
    item: "a planted 3 s sleep in activation fails the launch"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t2-ci-jobs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-ci-jobs
title: "CI jobs: measure on ubuntu (xvfb) and windows-latest"
kind: config
complexity: 4
dependsOn:
  - t1-measure-launch
parallel: false
files:
  - .github/workflows/ci.yml
dod:
  - id: d1
    item: "both jobs required; a missing ack fails the job"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t3-remote-wsl/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-remote-wsl
title: "scripts/measure-remote-wsl.ps1 + driver VSIX; marks readable with COAI_MEASURE=1 in any mode"
kind: code
complexity: 5
dependsOn:
  - t1-measure-launch
parallel: true
files:
  - src_vs_code/scripts/measure-remote-wsl.ps1
dod:
  - id: d1
    item: "the result names the extension-host location"
```

## v2/epoch-1/e2-thin-shell/stories/s6-joint-smoke/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-joint-smoke
title: "Joint smoke: shell VSIX + E1 binary"
dependsOn:
  - s2-measure
```

## v2/epoch-1/e2-thin-shell/stories/s6-joint-smoke/tasks/t1-joint-round/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-joint-round
title: "Install the shell VSIX with the E1 binary; one real round shown; no hold/card path runs"
kind: measure
complexity: 4
dependsOn: []
parallel: false
files:
  - research/RESULTS_v2_start.md
dod:
  - id: d1
    item: "round shown in the sidebar; no card or hold code reached"
```

## v2/epoch-1/e2-thin-shell/stories/s6-joint-smoke/tasks/t2-settings-mirror/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-settings-mirror
title: "Settings mirror under lazy activation: a window that never opens coai still gives the server current settings, or the gap is closed"
kind: test
complexity: 5
dependsOn: []
parallel: true
files:
  - src_vs_code/src/test/host
dod:
  - id: d1
    item: "result recorded; a gap is fixed or the server reads settings without the mirror"
```

## v2/epoch-1/e4-shell-modules/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-shell-modules
title: "Shell modules: sidebar, chat carried, install after ready, dead v1 out"
half: extension
dependsOn:
  - e2-thin-shell
doneWhen:
  - "sidebar polls only while visible"
  - "all chat host scenarios pass"
  - "M2 still ≤ 2000 ms"
  - "no escalation or cadence control or watcher in the bundle"
  - "--setup-clients runs after every activation (after ready) and finishes an interrupted client update"
  - "the sidebar module draws the real interactive menu itself (collapsed section headers) without the legacy bundle; opening a section before the old code is ready shows loading for that section only; menuReady ≤ 2000 ms in every sample"
```

## v2/epoch-1/e4-shell-modules/stories/s3-sidebar/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s3-sidebar
title: "Sidebar on the kit: failures, active cons/consask, last rounds"
dependsOn: []
```

## v2/epoch-1/e4-shell-modules/stories/s3-sidebar/tasks/t2-sidebar-module/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-sidebar-module
title: "shell/modules/sidebar + pages/sidebar.ts on kit 0.2: skeleton first, ready after paint, data after ready"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_vs_code/shell/modules/sidebar
  - src_vs_code/pages/sidebar.ts
  - src_vs_code/package.json
dod:
  - id: d1
    item: "the sidebar polls only while visible"
```

## v2/epoch-1/e4-shell-modules/stories/s3-sidebar/tasks/t3-sidebar-page-test/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-sidebar-page-test
title: "Bundled page test of the sidebar (runs the minified page against a DOM shim)"
kind: test
complexity: 4
dependsOn:
  - t2-sidebar-module
parallel: false
files:
  - src_vs_code/src/test
dod:
  - id: d1
    item: "deleting the ready post makes the test fail"
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-chat-carried
title: "Chat carried unchanged: one registration, one lazy instance, init and teardown"
dependsOn:
  - s1-shell-core
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/tasks/t1-shared-runtime/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-shared-runtime
title: "One runtime module for notify, ledger and data root; other bundles reach it through the shell"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_vs_code/shell/modules/runtime
dod:
  - id: d1
    item: "notify state exists once per host"
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/tasks/t2-chat-register/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-chat-register
title: "shell/modules/chat/register.ts: commands and coaiChat serializer registered once by the shell; data root, side models, remote sessions, presets initialised; deactivate forwards heartbeat stop and usage drain"
kind: code
complexity: 7
dependsOn:
  - t1-shared-runtime
parallel: false
files:
  - src_vs_code/shell/modules/chat
  - src_vs_code/src/extension.ts
dod:
  - id: d1
    item: "no chat logic changed (diff touches registration only)"
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/tasks/t3-chat-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-chat-scenarios
title: "Host scenarios: cold chat command and restored legacy tab without the sidebar, custom data root, side model, close right after a turn"
kind: test
complexity: 5
dependsOn:
  - t2-chat-register
parallel: false
files:
  - src_vs_code/src/test/host
dod:
  - id: d1
    item: "all existing chat host scenarios pass"
```

## v2/epoch-1/e4-shell-modules/stories/s5-install-module/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s5-install-module
title: "Binary install/update moved after the first ready"
dependsOn:
  - s3-sidebar
```

## v2/epoch-1/e4-shell-modules/stories/s5-install-module/tasks/t1-install-after-ready/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-install-after-ready
title: "shell/modules/install carries installer.ts/coaiInstall.ts; runs only after the first sidebar ready or first command"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - src_vs_code/shell/modules/install
dod:
  - id: d1
    item: "M2 does not include install work"
  - id: d2
    item: "after the binary check, runs coai --setup-clients (no --binary unless needed) once per activation, after the first ready; an interrupted Claude update is finished from its manifest (E3 plan-gate decision on finding 5)"
```

## v2/epoch-1/e4-shell-modules/stories/s6-dead-v1-out/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-dead-v1-out
title: "Dead v1 watchers and controls out of the bundle"
dependsOn:
  - s4-chat-carried
```

## v2/epoch-1/e4-shell-modules/stories/s6-dead-v1-out/tasks/t1-remove-watchers/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-remove-watchers
title: "Remove escalation/consultation/question watchers, cadence and hold controls from the extension"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_vs_code/src
dod:
  - id: d1
    item: "no escalation or cadence control or watcher in the bundle"
```

## v2/epoch-1/e4-shell-modules/stories/s6-dead-v1-out/tasks/t2-recheck-m2/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-recheck-m2
title: "Re-run the measure launch; M2 still ≤ 2000 ms"
kind: measure
complexity: 2
dependsOn:
  - t1-remove-watchers
parallel: false
files:
  - research/RESULTS_v2_start.md
dod:
  - id: d1
    item: "new M2 recorded"
```
