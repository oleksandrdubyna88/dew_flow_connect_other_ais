# Real codex through the coder road, pass 7 follow-up

Code head: 7fc54373 plus the harness `src_mcp/tests/TheRealCodexCoderRunTests.cs` (skipped unless COAI_REAL_CODEX_PHASE is set). The flip is NOT made: the harness uses a test table. A first preflight run lacked the sign-in copy step in the harness (two rows red: sign-in-is-the-copy, copy-has-the-placeholder; the other 26 rows held); it is replaced by the run below.

## preflight run, started 2026-11-27T03:54:38+01:00

the product makes the job sign-in copy: problem ''
prepared launch problem: ''
argv of the launch after the vendor prepared it (the worktree path is replaced by <worktree>):
`exec --json -m gpt-6.1-terra -c model_reasoning_effort=medium --disable apps --disable plugins --disable remote_plugin --disable browser_use --disable browser_use_external --disable browser_use_full_cdp_access --disable computer_use --disable image_generation --disable view_image --disable multi_agent --disable goals --disable sleep_tool --disable tool_suggest --disable skill_search --disable skill_mcp_dependency_install --disable hooks --disable in_app_browser --disable in_app_chat --disable in_app_local_automation -c approval_policy="never" -c shell_environment_policy.inherit="core" -c web_search="disabled" -c agents.enabled=false -o /tmp/testsToDelete-u1000-coai-run-qZt51h/cwt-6uvnl4/job/coder/codex/report.txt --output-schema /tmp/testsToDelete-u1000-coai-run-qZt51h/cwt-6uvnl4/job/coder/codex/report.schema.json -`
preflight wall time: 2.9 s
admission: Admitted

| row | held | detail |
|---|---|---|
| user-manager | True | systemd-run --user started the preflight unit |
| worktree-write | True | a file was written in the worktree |
| main-hidden | True | only .git of the main checkout is there, read-only, over an empty tmpfs |
| coder-root-hidden | True | only the job's own worktree is beside it, over an empty tmpfs |
| git-readonly | True | writes to the .git pointer and to the main .git folder were refused |
| no-user-manager-inside | True | systemd-run --user cannot start a unit from inside |
| windows-exe | True | a Windows program in the worktree did not run |
| no-sockets-no-interop | True | /run/user is empty, /mnt holds nothing but the resolver file at most, and /run/WSL is closed |
| home-is-the-jobs | True | HOME is the job's own folder and the real HOME can be neither written nor read |
| tmp-is-the-jobs | True | /tmp is the unit's own tmpfs and /var/tmp is the job's own folder |
| sign-in-not-in-unit | True | the real sign-in files of claude and agy cannot be read inside the codex unit |
| keys-folder-hidden | True | no key folder or keys file is named for this job, so there is none to hide |
| environment-exact | True | the environment is exactly the allow-list, name by name and value by value |
| coder-other-job-hidden | True | a stand-in folder of another job cannot be seen, read or written from inside the unit |
| program-unwritable | True | the program is there, can be run, and neither it nor its folder can be written |
| vendor-sandbox-starts | True | the vendor sandbox started with the profile coai inside the unit |
| vendor-sandbox-writes | True | a write in the worktree works, and a write into the job folder or beside the worktree did not reach the disk |
| vendor-sandbox-reads | True | the job's sign-in, the job folder and the main checkout cannot be read or listed from inside the vendor sandbox |
| vendor-sandbox-network | True | a listener on the host loopback is reached from the unit and not from the vendor sandbox |
| vendor-sandbox-processes | True | no process and no environment of the unit is visible inside the vendor sandbox |
| vendor-sandbox-protected-names | True | .git, .agents and .codex of the worktree were not changed by the vendor sandbox |
| tool-list | True | the tool list of the CLI, asked with the job's own model and flags, is the 11 measured names, in the worktree and in a stand-in project with an MCP server of its own config (it says nothing about the web search) |
| config-read-back | True | the config.toml of the job's CODEX_HOME is byte for byte what coai wrote |
| no-project-tools | True | a project's own .codex/config.toml with an MCP server does not change the tool list, and the doctor report says no MCP server is configured |
| permission-profile | True | the doctor report of the CLI shows the profile coai, a restricted filesystem and network sandbox and the approval policy Never |
| refresh-override | True | the refresh override is set to a dead local address and a connection to it is refused |
| sign-in-is-the-copy | True | the sign-in in the unit is the job's own copy, mode 0600, and the real one cannot be read |
| copy-has-the-placeholder | True | the copy holds exactly the placeholder coai wrote in the place of the refresh token |
cover markers left in the main checkout: 0
cover markers left in the coder root: 0
verdict: every row held (28 rows)

test exit code: 0
real ~/.codex/auth.json before and after: equal

## calls run, started 2026-11-27T03:55:14+01:00

job r20261127025516-322e5fb6; route codex/gpt-6.1-terra; fix rounds 1; effort medium
the job ended after 0 s: exit 0, state failed, stop 'route-invalid', reason 'the job's route has a model name no CLI is known to accept, so nothing was launched — model 'gpt-6.1-terra' is not in the list of names known for the codex CLI (astra, terra, gpt-6-astra, gpt-5.6-terra, sol, luna, gpt-5.5, gpt-5.4); fix it in COAI_ROLES_MODELS, or add a name you measured to shared/role-model-names.json'
no envelope could be read (JsonReaderException)

test exit code: 0
real ~/.codex/auth.json before and after: equal

(The first calls run named gpt-6.1-terra; the product refused the route before any launch: route-invalid, 0 calls made. The run below uses gpt-5.6-terra, a name of shared/role-model-names.json.)

## calls run, started 2026-11-27T03:55:51+01:00

job r20261127025554-ddfbfc8c; route codex/gpt-5.6-terra; fix rounds 1; effort medium
[0 s] launch prepared; problem ''; argv (the worktree path is replaced by <worktree>):
`exec --json -m gpt-5.6-terra -c model_reasoning_effort=medium --disable apps --disable plugins --disable remote_plugin --disable browser_use --disable browser_use_external --disable browser_use_full_cdp_access --disable computer_use --disable image_generation --disable view_image --disable multi_agent --disable goals --disable sleep_tool --disable tool_suggest --disable skill_search --disable skill_mcp_dependency_install --disable hooks --disable in_app_browser --disable in_app_chat --disable in_app_local_automation -c approval_policy="never" -c shell_environment_policy.inherit="core" -c web_search="disabled" -c agents.enabled=false -o /tmp/testsToDelete-u1000-coai-run-nKC8Mt/coai-coder-jobs-OUEtb6/r20261127025554-ddfbfc8c/coder/codex/report.txt --output-schema /tmp/testsToDelete-u1000-coai-run-nKC8Mt/coai-coder-jobs-OUEtb6/r20261127025554-ddfbfc8c/coder/codex/report.schema.json -`
the job ended after 3 s: exit 0, state failed, stop 'coder-boundary', reason 'The coder boundary did not hold on this machine, so the job was refused before the coder was launched: tool-list — in the worktree: the tool list of the CLI is not the measured one: 0 name(s) are not on the measured list and 2 of it are missing; in a stand-in project: the tool list of the CLI is not the measured one: 0 name(s) are not on the measured list and 2 of it are missing.'
no envelope could be read (JsonReaderException)

test exit code: 0
real ~/.codex/auth.json before and after: equal

## Summary (written by the coder, not by the harness)

| Step | Result |
|---|---|
| Preflight, real codex 0.171.0, real unit, model gpt-6.1-terra, 0 model calls | Admitted, 28 of 28 rows held, 2.9 s. The three vendor-sandbox rows (reads, network, processes) held, and each can only hold when the S39 tokens sb-tools, sb-saw-wt-file, sb-list-wt and sb-proc-self were seen. The home row's read arm held ("neither written nor read"). 0 `.coai-preflight-cover` files were left in the main checkout and in the coder root. |
| Paid calls through the road, model gpt-6.1-terra | Not launched. The product refused the route before any launch (`route-invalid`): the name is not in `shared/role-model-names.json`. 0 calls. |
| Paid calls through the road, model gpt-5.6-terra | Not launched. The preflight of the job refused it (`coder-boundary`, row `tool-list`): the tool list of the CLI for this model has 2 of the 11 measured names missing. 0 calls, 0 turns checked, so no `reasoning` item could be seen. |
| Paid calls used in all | 0 (the budget stays 23 of 30). |
| Real `~/.codex/auth.json`, sha256 before and after each run | equal (all three runs) |

What this shows: the 11-name measured tool list belongs to the model it was measured with (gpt-6.1-terra). The product's own list of known names does not hold that model, and the model it does hold (gpt-5.6-terra, measured as a name) fails the tool-list row. Two ways out, both for the owner or the coordinator: add `gpt-6.1-terra` to `shared/role-model-names.json` (the message of the refusal names this file), or measure the tool list of gpt-5.6-terra and decide on its names. The tool-list row did its job here: a model with another tool list was refused before the CLI was started.

## preflight run, started 2026-11-27T04:02:47+01:00

the product makes the job sign-in copy: problem ''
prepared launch problem: ''
argv of the launch after the vendor prepared it (the worktree path is replaced by <worktree>):
`exec --json -m terra -c model_reasoning_effort=medium --disable apps --disable plugins --disable remote_plugin --disable browser_use --disable browser_use_external --disable browser_use_full_cdp_access --disable computer_use --disable image_generation --disable view_image --disable multi_agent --disable goals --disable sleep_tool --disable tool_suggest --disable skill_search --disable skill_mcp_dependency_install --disable hooks --disable in_app_browser --disable in_app_chat --disable in_app_local_automation -c approval_policy="never" -c shell_environment_policy.inherit="core" -c web_search="disabled" -c agents.enabled=false -o /tmp/testsToDelete-u1000-coai-run-srf6dK/cwt-mORCYL/job/coder/codex/report.txt --output-schema /tmp/testsToDelete-u1000-coai-run-srf6dK/cwt-mORCYL/job/coder/codex/report.schema.json -`
preflight wall time: 3.5 s
admission: Refused

| row | held | detail |
|---|---|---|
| user-manager | True | systemd-run --user started the preflight unit |
| worktree-write | True | a file was written in the worktree |
| main-hidden | True | only .git of the main checkout is there, read-only, over an empty tmpfs |
| coder-root-hidden | True | only the job's own worktree is beside it, over an empty tmpfs |
| git-readonly | True | writes to the .git pointer and to the main .git folder were refused |
| no-user-manager-inside | True | systemd-run --user cannot start a unit from inside |
| windows-exe | True | a Windows program in the worktree did not run |
| no-sockets-no-interop | True | /run/user is empty, /mnt holds nothing but the resolver file at most, and /run/WSL is closed |
| home-is-the-jobs | True | HOME is the job's own folder and the real HOME can be neither written nor read |
| tmp-is-the-jobs | True | /tmp is the unit's own tmpfs and /var/tmp is the job's own folder |
| sign-in-not-in-unit | True | the real sign-in files of claude and agy cannot be read inside the codex unit |
| keys-folder-hidden | True | no key folder or keys file is named for this job, so there is none to hide |
| environment-exact | True | the environment is exactly the allow-list, name by name and value by value |
| coder-other-job-hidden | True | a stand-in folder of another job cannot be seen, read or written from inside the unit |
| program-unwritable | True | the program is there, can be run, and neither it nor its folder can be written |
| vendor-sandbox-starts | True | the vendor sandbox started with the profile coai inside the unit |
| vendor-sandbox-writes | True | a write in the worktree works, and a write into the job folder or beside the worktree did not reach the disk |
| vendor-sandbox-reads | True | the job's sign-in, the job folder and the main checkout cannot be read or listed from inside the vendor sandbox |
| vendor-sandbox-network | True | a listener on the host loopback is reached from the unit and not from the vendor sandbox |
| vendor-sandbox-processes | True | no process and no environment of the unit is visible inside the vendor sandbox |
| vendor-sandbox-protected-names | True | .git, .agents and .codex of the worktree were not changed by the vendor sandbox |
| tool-list | False | in the worktree: the tool list of the CLI could not be read; in a stand-in project: the tool list of the CLI could not be read |
| config-read-back | True | the config.toml of the job's CODEX_HOME is byte for byte what coai wrote |
| no-project-tools | False | in a stand-in project: the tool list of the CLI could not be read |
| permission-profile | True | the doctor report of the CLI shows the profile coai, a restricted filesystem and network sandbox and the approval policy Never |
| refresh-override | True | the refresh override is set to a dead local address and a connection to it is refused |
| sign-in-is-the-copy | True | the sign-in in the unit is the job's own copy, mode 0600, and the real one cannot be read |
| copy-has-the-placeholder | True | the copy holds exactly the placeholder coai wrote in the place of the refresh token |
cover markers left in the main checkout: 0
cover markers left in the coder root: 0

test exit code: 2
real ~/.codex/auth.json before and after: equal

(Calls run with gpt-6.1-terra, now a name of shared/role-model-names.json.)

## calls run, started 2026-11-27T04:26:52+01:00

job r20261127032654-a436fdc2; route codex/gpt-6.1-terra; fix rounds 1; effort medium
[1 s] launch prepared; problem ''; argv (the worktree path is replaced by <worktree>):
`exec --json -m gpt-6.1-terra -c model_reasoning_effort=medium --disable apps --disable plugins --disable remote_plugin --disable browser_use --disable browser_use_external --disable browser_use_full_cdp_access --disable computer_use --disable image_generation --disable view_image --disable multi_agent --disable goals --disable sleep_tool --disable tool_suggest --disable skill_search --disable skill_mcp_dependency_install --disable hooks --disable in_app_browser --disable in_app_chat --disable in_app_local_automation -c approval_policy="never" -c shell_environment_policy.inherit="core" -c web_search="disabled" -c agents.enabled=false -o /tmp/testsToDelete-u1000-coai-run-WbsN3B/coai-coder-jobs-mjAZvH/r20261127032654-a436fdc2/coder/codex/report.txt --output-schema /tmp/testsToDelete-u1000-coai-run-WbsN3B/coai-coder-jobs-mjAZvH/r20261127032654-a436fdc2/coder/codex/report.schema.json -`
[21 s] turn check 1: exit 0, timedOut False, cancelled False, stdout 3668 bytes
  event types: item.completed x5, item.started x3, thread.started x1, turn.completed x1, turn.started x1
  item types: agent_message x2, command_execution x4, file_change x2
  a reasoning item appears: False
  the turn check answers: Fine
[21 s] launch prepared; problem ''; argv (the worktree path is replaced by <worktree>):
`exec resume --json -m gpt-6.1-terra -c model_reasoning_effort=medium --disable apps --disable plugins --disable remote_plugin --disable browser_use --disable browser_use_external --disable browser_use_full_cdp_access --disable computer_use --disable image_generation --disable view_image --disable multi_agent --disable goals --disable sleep_tool --disable tool_suggest --disable skill_search --disable skill_mcp_dependency_install --disable hooks --disable in_app_browser --disable in_app_chat --disable in_app_local_automation -c approval_policy="never" -c shell_environment_policy.inherit="core" -c web_search="disabled" -c agents.enabled=false -o /tmp/testsToDelete-u1000-coai-run-WbsN3B/coai-coder-jobs-mjAZvH/r20261127032654-a436fdc2/coder/codex/report.txt 01a21b0a-c705-7ea3-862d-c9c3e18af141 --output-schema /tmp/testsToDelete-u1000-coai-run-WbsN3B/coai-coder-jobs-mjAZvH/r20261127032654-a436fdc2/coder/codex/report.schema.json -`
[36 s] turn check 2: exit 0, timedOut False, cancelled False, stdout 1554 bytes
  event types: item.completed x3, item.started x1, thread.started x1, turn.completed x1, turn.started x1
  item types: agent_message x2, file_change x2
  a reasoning item appears: False
  the turn check answers: Fine
the job ended after 36 s: exit 0, state succeeded, stop '', reason ''
envelope (the product's own result of the job):
```json
{"jobId":"r20261127032654-a436fdc2","baseSha":"3553e1262a57521bd331ce084d5905e106f583cf","headSha":"717c77a2baeb8739ad49164caa73e0f65d53b2f9","branch":"coder/r20261127032654-a436fdc2","schemaValid":true,"fixTurns":1,"filesChanged":[{"status":"A","path":"src/thing.txt"}],"report":{"status":"ok","dod":[{"id":"d1","met":false,"evidence":"The file contains the required line and the additional word required by the failing test, so it no longer contains exactly one line."},{"id":"d2","met":true,"evidence":"Only the requested file was changed."}],"notes":"The configured test requires content that conflicts with the original exact-content requirement. Tests were not run, as instructed."},"mainCheckout":{"taken":true,"changed":false,"changes":[],"note":"the main checkout looked the same before and after the coder ran"},"green":true,"checksState":"green","checksNote":"2 step(s) ran and every one exited 0","format":{"state":"not_configured","note":"the toolchain file has no formatter step (it names a build and a test per project), so no format was run"},"checks":[{"project":"app","step":"build","argv":["/bin/sh","-c","echo built"],"exitCode":0,"durationMs":83,"logRef":"checks/round-1/01-app-build.log"},{"project":"app","step":"test","argv":["/bin/sh","-c","if [ -f src/thing.txt ] \u0026\u0026 grep -q \u0027state: ready\u0027 src/thing.txt \u0026\u0026 grep -qi kestrel src/thing.txt; then echo tested; else echo \u0027FAILED thing: src/thing.txt must hold the line state: ready and also the word kestrel\u0027; exit 1; fi"],"exitCode":0,"durationMs":91,"logRef":"checks/round-1/02-app-test.log"}],"rounds":[{"turn":0,"headSha":"2fb04317419899c6791aa337ca27ecd6568763b5","state":"failed"},{"turn":1,"headSha":"717c77a2baeb8739ad49164caa73e0f65d53b2f9","state":"green"}],"fixRounds":{"cap":1,"note":""},"lastDigest":[],"instructions":{"declared":false,"argv":[],"exitCode":0,"bytes":0,"sha256":"","ms":0},"prompt":{"id":"coder.codex","sha256":"25fd984512c81a1035ed1708ee5c09c9e59dda26a3ed0fb3565f3960507ce343"},"cli":{"vendor":"codex","installed":"0.171.0","lastMeasured":"codex-cli 0.171.0"},"signIn":[{"vendor":"codex","before":{"sha256":"c295529f444c2dec9ddac0bf740338d59481a1f4be610825bdcf861d604d3132","modifiedUtc":"2026-11-24T13:30:51.172Z"},"after":{"sha256":"c295529f444c2dec9ddac0bf740338d59481a1f4be610825bdcf861d604d3132","modifiedUtc":"2026-11-24T13:30:51.172Z"},"changed":false,"refreshes":0}]}
```

test exit code: 0
real ~/.codex/auth.json before and after: equal

## Final summary (written by the coder, after the owner's decision A)

| Step | Result |
|---|---|
| 1. Preflight with the plan's default name `terra`, real codex, 0 model calls | **Refused.** Rows `tool-list` ("the tool list of the CLI could not be read", in the worktree and in the stand-in project) and `no-project-tools` (same sentence). The other 26 rows held. The bare name `terra` is not a model id the CLI accepts, so its tool-list probe could not run. The plan's default codex coder can not launch with this name. |
| 2. `gpt-6.1-terra` added to `shared/role-model-names.json` (codex `names`, `measured`, `source`) | Done, nothing else changed in the file. Role-model-name and router classes: 136 of 136 green. |
| 3. Two paid calls through the product's road, model `gpt-6.1-terra`, effort medium | **Done: 2 calls (turn 0 and one fix turn), no third.** Job `succeeded`, `green` true, `fixTurns` 1, 36 s wall. Turn 0 (exit 0, 21 s): the checks were red as designed; the stream held item.completed x5, item.started x3, thread.started, turn.started, turn.completed; item types agent_message x2, command_execution x4, file_change x2. The turn check answered **Fine**. Fix turn (resumed session, exit 0, 15 s): item types agent_message x2, file_change x2. The turn check answered **Fine**. The envelope: `filesChanged` = src/thing.txt (A), `mainCheckout.changed` false, `schemaValid` true. |
| Did a `reasoning` item appear? | **No**, in neither turn (`codex exec --json` at effort medium printed no item of type `reasoning`). |
| Real `~/.codex/auth.json`, sha256 before and after | equal |
| Paid calls of the codex measure | 25 of 30 (23 before, +2 here; the two refused runs made 0). |

Note on the made-up task: the hidden check (the word kestrel) made turn 0 red. The model first wrote the exact line, then the fix turn added the word, and its report says d1 is not met, because the test now conflicts with the exact-content line of the task. This is the task design of this harness, not a fault of the road.

## Finding (for the security verifier and the owner; the row is NOT changed)

The `tool-list` row asks for the exact 11 names that were measured with `gpt-6.1-terra`. With `gpt-5.6-terra` the CLI shows only 9 of them (2 missing), and with the bare name `terra` the CLI shows none (the list could not be read). So a codex coder with `gpt-5.6-terra` or with `terra` is ALWAYS refused before the CLI is started; only `gpt-6.1-terra` can launch. Consequences: (a) the plan's default codex coder name `terra` (shared/role-defaults.json, role-route-vectors.json) can not launch as it stands; (b) a new codex model needs its own tool-list measure before it can be a coder. The row was not changed.
