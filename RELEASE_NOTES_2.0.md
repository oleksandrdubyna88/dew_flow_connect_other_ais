# coai 2.0 — release notes (DRAFT)

> Draft. Do not publish until the owner approves the merge. It covers `coai-mcp` 2.0.0 and the VS Code extension 2.0.0.
> The numbers marked "see the PR" come from the CI of the integration pull request and are copied in before the release.

## What changes for you

**The gate never waits for a person ("auto only").** A review round ends with one of four outcomes: `proceed`, `revise`,
`good_enough` or `not_reviewed`. The old "hold" and "call a human" paths are gone. A failure is a notification, not a question.
The AI keeps working. You see the result in the sidebar and in **Show review rounds**.

**Two consultants, two short names.** `cons` is the consultant an AI asks when it is stuck (its tools are `cons` and
`cons_close`). `consask` is the question consultant an AI asks before it asks you. The old names (`consult`, `close_consult`,
`ask_consultants`) still answer as hidden aliases for older clients, and they go away in 2.1. The old settings names (`COAI_CONSULT_*`, `COAI_QCONSULT_*`) are still read
while the new ones (`COAI_CONS_*`, `COAI_CONSASK_*`) are not set.

**The sidebar starts in 2 seconds or less.** The extension is now a thin shell. It does not start at editor start-up. It
loads when you open the sidebar, run a coai command or open a chat. The sidebar draws its own menu (Notifications, Active
gates, Active consultations, Phrases, Bugz) before the older code has loaded, and polls the server only while it is visible.
Measured time from the command to the usable menu: Linux 0.3 to 0.5 s. Windows 0.7 to 2.4 s on a CI runner (see "Known limits").

**Install is one step.** After the sidebar is ready, the extension runs `coai-mcp --setup-clients`. It finds the AI command
lines on this machine (Claude Code, Codex, agy), writes the `coai` MCP entry for each, and finishes an update that stopped
half way. You can run the same thing by hand: `coai-mcp --install` on a clean machine, `coai-mcp --setup-clients --dry-run`
to look first, `--remove` or `--restore` to undo. A client that cannot be set up is one notification, not a failure of the rest.

**Roles.** The new `roles` tool (and a SessionStart hook) tells an AI which main AI is set, which models serve each role in
order, which model band fits a task of complexity 0 to 10, and how the work method is set. `review_*`, `cons` and `consask`
accept a `workItem`, and `--plan-report` shows gates, cons, consask and time per work item. `--band-json` answers phrases,
consultations and failures for the sidebar and the Claude mod.

**Conventions are optional.** coai no longer ships or enforces coding conventions. A repository that mounts them still has
them read.

**Your v1 data is migrated for you.** See "Upgrade notes".

## Removed in 2.0

- Held gates, the `call_human` verdict, the escalation card, its answer flow and its watcher. The "Wait for you" limit.
- The "When the rounds run out" choice (it is always Auto now) and the escalation-wait setting.
- The Commands page and the operator-commands settings; the Security lane tab and its settings (the lane is deferred);
  the prompt picker and the prompts-per-round settings.
- The consultation cadence: its sidebar section, its settings and its orders in review replies.
- Split-plan, split-with-Fable, gate-per, stop-local-when-quiet, deal-plan, deal-code and rotate-prompts settings; the
  question consultant's mode and free-batch settings; the autonomous switch.
- The status-bar item and modal that showed a waiting question.
- The directory watchers, the sidebar snapshot and the old start-up work in the extension.

## Upgrade notes

1. **Restart every client.** Close and start again every Claude Code, Codex and agy session, and reload every VS Code
   window. A client keeps the old `coai-mcp` process until it restarts.
2. **The migration is automatic.** The first start of `coai-mcp` 2.0 on a data folder runs `--migrate-v2` by itself
   (it runs only from major version 2). It releases sessions that were held, withdraws open escalation cards and moves them
   (and answered ones older than 30 days) to `escalations/archive/`, archives the `qphase/` and `cadence/` folders to
   `migrations/v1-archive/`, and renames settings keys. It is safe to run twice, and a run that was stopped continues where
   it stopped. To look first: `coai-mcp --migrate-v2` (a dry run that creates nothing). To run it by hand: add `--apply`.
   The marker `migrations/v2.done` says it finished.
3. **Removed settings are moved, not deleted.** Every setting 2.0 does not read leaves `settings.json` and is kept in
   `settings.v1-removed.json` in the same data folder. Copy a value back by hand if you ever need it.
4. **Update the server and the extension together.** An old extension with the new `coai-mcp`, and the new extension with an
   old Team server, were both checked in the contract suite (see the PR); update everywhere in the same hour anyway.

## Known limits

- **Windows host time.** On a CI runner the editor sometimes needs 1.9 s or more before it even asks for the sidebar. That
  time is the editor's, not coai's, so one slow sample can pass 2000 ms on a slow Windows machine. Three runs of three
  samples each were measured; this does not prove the margin is safe on every machine.
- **agy on Windows is not measured.** The agy road (hook and setup) is tested on Linux. Its Windows behaviour is not measured yet.
- **A settings edit made outside coai may reach the server late.** The extension copies its settings to the server file only
  after the old code has loaded (when you open a coai surface). A `coai.*` value you edit in VS Code's own settings, in a window
  where you have not opened coai, reaches the server the next time any window opens one. See `research/RESULTS_v2_start.md`, section 6.
- **A real round shown in a real VS Code by hand** is checked by CI's real-editor job, not by a person, before this draft.
- **YamlDotNet is not used.** The work-plan files the new work method will read are an epoch 3 task. 2.0 reads none of them.
- **Not in 2.0:** the hub, the shared VM instance, the one-click update everywhere, the AskUserQuestion hook, coder
  delegation, the Claude mod. They are in epochs 2 to 5 of `v2/PLAN.md`.
