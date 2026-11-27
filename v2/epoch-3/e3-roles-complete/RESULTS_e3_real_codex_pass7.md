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
