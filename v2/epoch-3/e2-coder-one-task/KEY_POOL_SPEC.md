# Spec — epoch 3, E2, story s7 "Claude key pool: a round robin of setup-tokens for role jobs"

> Status: **spec only, nothing built, 2026-11-23.** Written by Fable (security judgement) from `v2/` and the server code
> at `e265b558` (read-only). Owner decisions of 2026-11-23 are fixed; the rest is decided here. Stories and tasks:
> `stories/s7-claude-key-pool/`. The real proof is `stories/s6-proof-and-measure/tasks/t5-pool-key-proof`.

## 0. The problem and the owner's decisions

Today a coder job uses the person's own claude sign-in: `~/.claude/.credentials.json` is bound read-only into the coder
unit (`src_mcp/runners/Toolchain/CoderUnit.cs:54`, `BoundaryHost.cs:83`). `RESULTS_s2_check_boundary.md` (t5) records the
open risk: a token refresh inside the unit cannot be saved, and a vendor refresh-token rotation could end the person's
sign-in. The read-only roles (BA, probe) run with the person's whole home.

Owner, 2026-11-23:

1. Claude role jobs (the coder first, then the other roles) do NOT use the person's sign-in. They use long-lived tokens
   from `claude setup-token`.
2. Round robin over MANY tokens. The owner adds them. A separate settings page "Keys" shows the pool. The page is a face
   only (`architecture/extension.md` § 3 on `feat/v2-plan`): one transport-neutral page API, all logic in the server.
3. The pool works on the standalone orchestrator AMI (`architecture/server.md` § 23 on `feat/v2-plan`): headless, no
   VS Code, no hub. Keys arrive at boot and are never in the image.

Words: a **key** is one setup-token in the pool. The **pool** is the set of keys of one data folder. A **pool launch**
is a claude launch of a role job that carries a key.

## 1. What was checked, and how (2026-11-23, this machine: WSL2, systemd 259.5, claude 2.1.330)

No model was called and no secret was read. A dummy value was used where a value was needed.

| # | Question | How | Answer |
|---|---|---|---|
| C1 | Does the help text name the variable? | `claude setup-token --help`, `claude --help`, `claude auth --help` | No. `setup-token --help` says only "Set up a long-lived authentication token (requires Claude subscription)". `claude --help` (`--bare`) says: "Anthropic auth is strictly ANTHROPIC_API_KEY or apiKeyHelper via --settings (OAuth and keychain are never read)" |
| C2 | Which variable carries a setup-token? | `grep -a` over the installed binary `~/.local/share/claude/versions/2.1.330` for its own message texts | **`CLAUDE_CODE_OAUTH_TOKEN`**. The CLI's own texts: "use `claude setup-token` and set CLAUDE_CODE_OAUTH_TOKEN for unattended jobs"; "Long-lived tokens (from `claude setup-token` or CLAUDE_CODE_OAUTH_TOKEN) are limited to inference-only"; "OAuth 401: keeping the user-supplied CLAUDE_CODE_OAUTH_TOKEN instead of adopting the stored credential"; the lifetime text is "1-year" for 365 days; the token test is the prefix `sk-ant-oat` |
| C3 | Is that name in use in this repository? | `grep` | Yes. The Team server hands a slot's `claude.token` over as `CLAUDE_CODE_OAUTH_TOKEN` (`src_server/src/Slots/SlotEnvironment.cs:76`), deployed 2026-11-09 (`POST_DEPLOY.md:159`) |
| C4 | Does a value handed by NAME stay out of every argv? | The inner command of `CoderUnit.cs:109` (`exec /usr/bin/env -i "NAME=$NAME" "$@"`) run 300 times outside a unit; a second process read `/proc/<pid>/cmdline` | **No.** The value was in the argv of `/usr/bin/env` in **228 of 300** runs. A shell that only exports the name (`export NAME; exec "$@"`) showed it in **0 of 300** |
| C5 | Where does `systemd-run --user --setenv=NAME` put the value? | A unit with a dummy value; `systemctl --user show -p Environment`, the transient unit file, the journal, the unit's argv | In `systemctl --user show` and in `/run/user/<uid>/systemd/transient/<unit>.service` (file 0644 in a folder 0700 of the user). Not in an argv. Not in the journal |
| C6 | Can a boundary unit read the environment of another process of the same user? | A unit with `PrivateUsers=yes` read `/proc/<pid>/environ` of a process outside, and of a process in a second unit | **Permission denied** both times. `/proc/<pid>/cmdline` WAS readable. A process of the same user outside any unit read the environment |
| C7 | Is `PrivatePIDs=yes` usable in a user unit? | A unit listed `/proc` | Yes on systemd 259: 3 processes visible, not 106 |
| C8 | Is the systemd credential road usable in a user unit? | `-p LoadCredential=claude-key:<file>` with `ProtectHome=tmpfs`; the unit read `$CREDENTIALS_DIRECTORY/claude-key` | Yes on systemd 259: the file was read, mode 0400 |

NOT checked (each needs a paid call or a real key; task t3 does them): a real launch with the variable in the coder unit
with no credentials file; what the CLI answers for a refused key; which credential wins when the variable and a
credentials file or `ANTHROPIC_API_KEY` are both there; two sessions at once on one key.

**C4 is a defect in the code as it is.** The comment at `CoderUnit.cs:16` and the test
`ACoderBoundaryTests.cs:128` say a value "travels by NAME, never in an argv"; the test asserts that
`--setenv=ANTHROPIC_API_KEY` is PRESENT (line 147) and never looks at the inner command. It is latent today: no role job
sets a key on a launch (`RoleModelLauncher.cs:44` sets no `ApiKey`). The pool would make it live, so t5 changes the inner
command first.

## 2. The six decisions, in short

| # | Decision |
|---|---|
| 1 Storage | `<dataDir>/keys/claude/`: one `k-<12 hex>.key` (the token only) and one `k-<12 hex>.json` (label, state, times, counts) per key, `pool.json` (cursor), `pool.lock`. Files 0600, folder 0700; Windows: owner-only ACL. Write-only: add, relabel, disable, enable, remove. No API, page or one-shot answers a token. The hub may know metadata only, never a token in any form |
| 2 Entry | One-shot `coai-mcp --keys add --label X`: the token is ONE line on stdin (no echo on a terminal). `--keys import` reads many from stdin for the AMI boot. The page sends the token once in a write-only field; the server writes it. Never argv, never a log line, never a page-message log |
| 3 Round robin | Per fresh session: the next usable key after the cursor, under `pool.lock`. A resumed turn keeps the key of its session. A limit puts the key in cooldown until the stated reset (else 30 min, doubled, 5 h at most). An auth failure makes the key `dead`, with a notice. All keys unusable: **no wait** — the claude models of the route are skipped (the next vendor runs if the route has one), else the job fails `limit` and a new job is refused at the start, with the earliest free time |
| 4 Launch | The value reaches the claude child only as the environment variable `CLAUDE_CODE_OAUTH_TOKEN`: `systemd-run --setenv=CLAUDE_CODE_OAUTH_TOKEN` for the coder unit, the child's environment block for a read-only role. No argv holds it, the inner command included. With a key the person's `.credentials.json` is not bound and not read. Empty pool: role jobs are **refused**; today's bind stays only behind an explicit device switch |
| 5 Roles | Every claude launch of a ROLE JOB (coder, BA, probe, and the roles of E3) through the one road `RoleModelLauncher`. `cons`, `consask` and the gate reviewers stay as they are in this epic. The Team server slots stay separate and unchanged |
| 6 Tests | FakeCli scenarios K1–K16 with made-up tokens; one test puts a canary token in the pool and proves it is in no argv, log, ledger, page message, result or commit. Real proof in s6: one coder job with a pool key, at most 0.10 USD |

## 3. Storage (decision 1)

```
<dataDir>/keys/                   0700   (shared VM instance: 2770, group coai)
  claude/
    pool.lock                     empty; held only while one pick or one change runs
    pool.json                     {schema, adminRevision, cursor, exhaustedNoticeUtc}
    k-3f9a1c0b7e42.key            the token text, one line, nothing else        0600 (shared: 0660)
    k-3f9a1c0b7e42.json           metadata, no token                            0600 (shared: 0660)
```

- **`<dataDir>`** is the resolved data folder of the device, the one `roles/` is in (`PanelSettings.cs:582`:
  `~/.local/share/coai-mcp` on Linux and WSL, `%LOCALAPPDATA%\coai-mcp` on Windows; `COAI_DATA_DIR` when set).
- **Id** = `k-` + the first 12 hex characters of `SHA-256("coai-claude-key-v1\n" + token)`. The prefix text makes the id
  different from a plain hash of the token (the Team server and the hub store plain hashes of their own tokens). 48 bits
  of a hash of a random token say nothing about the token. The same token added twice gives the same id and is refused
  ("already in the pool as <label>").
- **Metadata**: `id`, `kind: "claude-setup-token"`, `label`, `state` (`enabled` | `disabled` | `dead`), `deadReason`
  (`auth` | `exposed`), `addedUtc`, `lastUsedUtc`, `cooldownUntilUtc`, `consecutiveCooldowns`, `launches`, `limitStops`,
  `authStops`, `note` (the last stop, through `Redaction.SafeText`). The cooldown fields are the Team server's own
  (`SlotRegistry.cs:343`, `:364`, `:374`) — the same shape, not the same class (§ 7).
- **Label**: 1 to 40 characters of `A-Z a-z 0-9 space . _ @ + -`, unique without case. A label that
  `Redaction.FirstSecretClass` reads as a secret is refused, and the answer says: a token was on a command line, revoke
  it. The label is never the token.
- **Modes are read back, not assumed.** After the folder and each file are made, their mode is read again. If it is not
  0700 / 0600 (a data folder on `/mnt/c`, where modes are not kept), the add is refused with the folder's name and the
  cure (put the data folder on the Linux file system). A key file found with a wider mode is not used and the page shows
  it (the check of `TeamServerAuth.cs:134`). Windows: the owner-only ACL of `runners/Files/PrivateAcl.cs`, read back.
- **Shared VM instance** (`server.md` § 5, data folder `/var/lib/coai`, group `coai`): the files are 0660 and the folder
  2770, because the owner decided that this instance shares its CLI logins. **Every member of `coai` can read every key
  there.** That is said in the answer of the first `--keys add` on such a folder.
- **Write-only.** The store has ONE member that returns the token text. Its only caller is the launch road (§ 6). There
  is no `show`, no `export`, no field of a `list` answer. A scan test fails on a second caller.
- **No encryption at rest.** A key beside its own decryption key protects nothing on Linux. The protection is the file
  mode, and that the keys folder is not visible in any boundary unit (§ 6). Anyone who runs as the same OS user can read
  the keys, as they can read `~/.claude/.credentials.json` today.
- **Moves and backups**: `shared/data-inventory.json` gets `keys/` with `move: false, mention: true` (the fate of
  `servers/`): a moved data folder does not carry keys to another machine.
- **Hub**: the hub may hold, per device and key: id, label, state, the times and the counts. It never gets a token, a
  longer hash, or an encrypted token. The device document (settings) has no key field. A key is added to a device ON that
  device; the Keys page of another device shows the paste-ready `coai-mcp --keys add --label <label>` line.

| Platform | Folder | Protection | Coder jobs |
|---|---|---|---|
| Linux, WSL (one user) | `~/.local/share/coai-mcp/keys/claude` | 0700 / 0600; under `/home`, so hidden in units | yes |
| Shared VM instance | `/var/lib/coai/keys/claude` | 2770 / 0660 group `coai`; `InaccessiblePaths` in units | yes |
| Windows (native) | `%LOCALAPPDATA%\coai-mcp\keys\claude` | owner-only ACL | no (no boundary there); read-only roles use the pool |
| Orchestrator AMI | as Linux, one user | as Linux; empty in the image; filled at boot | yes |

## 4. Entry (decision 2)

One-shot mode `--keys`, chosen by `args[0]` before any transport (the rule of `.agents/PROJECT.md`; the mode is added to
its list there). It never exits 64. Wrong arguments or data: 65 with a sentence. Every verb takes `pool.lock`.

| Verb | Input | Answer (stdout, JSON with `--json`) |
|---|---|---|
| `--keys list` | — | rows of metadata, `usable` count, the fallback switch; never a token |
| `--keys add --label X` | the token: ONE line on stdin | `{id, label, state, addedUtc}` |
| `--keys import` | stdin: one JSON object per line, `{"label","token"}`, 200 lines at most | counts: added, already there, refused (line numbers, reasons) |
| `--keys relabel --id K --label Y` | — | the row |
| `--keys disable --id K` / `enable --id K` | — | the row; `enable` also ends `dead` and a cooldown |
| `--keys remove --id K` | — | `{removed: K}`; the `.key` file is deleted first |
| `--keys scan --path P` | — | files under `P` that hold the exact text of a pool key: `{path, keyId}`; never the text |

- **stdin, one shot.** `add` reads at most 4096 bytes, one line, and trims the line end. When stdin is a terminal it
  prompts on stderr and reads with no echo. No verb takes a token as an argument, and none reads a token from a named
  file or from the environment. So a token is in no argv, no shell history line and no scrollback.
- **Shape check, with no echo.** A token must start with `sk-ant-oat` (the CLI's own test, C2), hold only
  `A-Z a-z 0-9 _ -`, and be 40 to 512 characters. A refusal names the rule that failed and the length — never a
  character of the input. `sk-ant-api…` is refused with "that is an API key; the pool takes `claude setup-token` tokens".
  No vendor call is made by `add`: a wrong key is found by its first launch (§ 5, `dead`).
- **The page**: the Keys page sends `{label, secret}` ONCE over the page API. `secret` is the only field the page API
  schema marks `writeOnly`; no `get`, `list` or `subscribe` answer has it. The extension host is a transport: it starts
  `coai-mcp --keys add --label <label>` and writes the secret to the child's stdin. It makes no temporary file. A
  transport that logs page messages drops every `writeOnly` field before it logs — one function, driven by the schema.
  The page uses a password field, clears it after the send, and never puts it in saved webview state.
- **The AMI**: a boot unit that runs as the orchestrator user, before the orchestrator, pipes the instance secret source
  into `coai-mcp --keys import`. A second boot adds nothing (same ids). The token is never in instance user-data (any
  process on the instance can read user-data) and never in the image: the bake script fails if `--keys list --json` is
  not empty.
- **Redaction.** `Redaction` already matches the shape (`sk-` + 8 or more characters, `core/Notices/Redaction.cs:84`); a
  named test pins `sk-ant-oat01-…`. Two more measures on the launch road (§ 6): the exact text of the launch's own key is
  replaced in everything the launch prints, and the coder's changed files are scanned for it before the commit.

## 5. Round robin, cooldown, dead (decision 3)

**Pick** (under `pool.lock`, a file held with `FileShare.None` — the lock of `SessionClaim.cs`, kept for milliseconds,
never for a launch; `pool.json` and metadata written through `AtomicJson`):

1. usable = `enabled`, not `dead`, `cooldownUntilUtc` not in the future, key file there with the right mode;
2. order = `addedUtc`, then id; the pick is the first usable key AFTER `cursor` in that ring (cursor gone: the first);
3. write `cursor`, the key's `lastUsedUtc` and `launches + 1`; read the token; release the lock.

- **Many processes, many users.** The lock is in the kernel, so it holds across N `coai-mcp` processes of one user (the
  AMI) and across users of the shared instance (files 0660). A dead holder frees it. No file is ever half written. The
  lock is waited for 2 s at most; then the launch stops as `crash` ("the key pool is busy").
- **No lease per key.** A setup-token is not refreshed in place, so two sessions on one key do not race for a refresh
  token (the reason the Team server locks a slot). The round robin spreads the launches; with more keys than running jobs
  each job has its own key. Not measured: the vendor's view of two sessions on one key (t3).
- **A session keeps its key.** A pick happens only for a FRESH session. The key id is written to the attempt in
  `job.json` and into the coder's resume handle (`CoderHandle`: job, worktree, session, **key id**). Every later turn of
  that session (`--resume`: a repair turn, a fix turn) uses the same key. A handle with another key id is refused. A
  runner that takes a job over reads the key id from `job.json`.
  `disabled` during a session: the session ends its turns with the key (disable stops NEW sessions; the page says so).
  `removed`, `dead` or a missing file: the next turn stops, and the loop goes on as after a limit.
- **Limit → cooldown.** A stop with the word `limit` on vendor claude (`RoleStops.cs:81`, `:90`) puts the key of the
  launch in cooldown: until the reset the vendor stated (`UsageLedger.StatedResetOf`), else
  `CooldownParser.Until(reason, now, consecutiveCooldowns)` (30 min, doubled, 5 h at most; "weekly" with no date: 24 h —
  `CooldownParser.cs:26`, `:34`). A throttle (`RateLimited`, transient) is 60 s. A launch that ends well clears the
  cooldown and the count (`MarkSucceeded` shape).
- **Key switch.** After a limit on a key, the loop of `RoleFailover` does not skip the claude vendor (today
  `RoleFailover.cs:177`) while the pool has a usable key this job has not used. It runs the SAME model again on the next
  key: a new session, through the one restart road of a model switch (read-only roles: from turn 0; the coder: the same
  worktree from the last coai commit, s4-fix-loop t3). At most 3 key switches per job, each key once per job. Each is one
  attempt in `job.json` (`keyId`), one log line, no notice.
- **Auth failure → dead.** Only on a signal with a recorded fixture (the rule of `RoleStops`: no phrase without a file in
  `tests/fixtures/role-stops`). t3 records the envelope of a refused key; `ClaudeSignInLost` (`RoleStops.cs:112`) is the
  shape today. The key becomes `dead` (`deadReason: auth`), is never picked, and ONE notice `claude-key-dead` names the
  label and the id and the cure (`--keys remove`, or `--keys enable` after a check). A network error, a 5xx or a crash
  never makes a key dead. The loop goes on with the next key, like after a limit.
- **Exposed → dead.** If the exact text of a key is found in what its launch printed or in the coder's changed files
  (§ 6), the key becomes `dead` (`deadReason: exposed`) with the notice `claude-key-exposed`: revoke it at the vendor.
- **All keys unusable — decision: no wait.**
  - In a job: the claude models of the route are skipped ("no usable claude key until <UTC>"). A model of another vendor
    in the route runs (important: fable, then codex astra). No other vendor: the job ends `failed`, stop `limit`, with
    the earliest free time.
  - At `start_role`: a route with only claude models is refused in 2 s or less, before any folder or worktree, with the
    same sentence. A route with another vendor starts.
  - One notice `claude-keys-exhausted` per 30 minutes at most, with the count of keys by state and the earliest free
    time.
  - The only wait that stays is today's: the last model's `RetryLadder` for a throttle, inside the job's budget.

  Why not wait: a reset is hours away (a 5-hour window, a week) and a job's budget is 30 minutes; a waiting job holds one
  of 3 job slots (2 on WSL) and a worktree; the cap already says "refuse, never queue" (E1 spec A); in auto mode a
  failure is a notice and the main AI decides what to do now. Why not "the next claude model": the limit is on the
  account of the key, so another claude model on the same keys meets the same limit; the failover to another VENDOR
  stays, because that is what § 20 of server.md is for.
- **The ledger.** `UsageEntry` (`UsageLedger.cs:82`) gets `KeyId` (appended; empty for a line that is not a pool
  launch; `person-sign-in` for the fallback). `RoleAttempt` (`RoleJobModels.cs:27`) gets `KeyId`, `role_status` shows it,
  and `role_attempts` gets `key_id` (a new appended RoundsDb step). The id is not a secret.

## 6. Launch (decision 4)

**The one road.** `RoleModelLauncher.LaunchAsync` (`RoleModelLauncher.cs:40`) is where every role launch is made. For
vendor claude it asks the pool for the key of the session and hands it to the claude adapter. The adapter sets the
variable in the one place it sets a credential today (`ClaudeConsultant.cs:229`). A launch has ONE credential: a pool key
and `ANTHROPIC_API_KEY` together is a contract violation (an exception), and `--bare` is never in the argv of a pool
launch (C1: with `--bare` OAuth is not read).

**The secret is a type that cannot print itself.** It is not a `string` property of a record (a record prints its
members). `ToString()` is `claude key <id>`. No JSON context has it. One member gives the text, used in two places: the
child's environment and the exact-text scrub.

**Coder (in the unit).**

- `request.Environment["CLAUDE_CODE_OAUTH_TOKEN"]` → `CoderUnit.ByName` (`CoderUnit.cs:82`) →
  `--setenv=CLAUDE_CODE_OAUTH_TOKEN` (`:120`). The systemd-run argv has the NAME only.
- **The inner command changes** (C4). It must not spell `NAME=$NAME` into the argv of `env`. Shape to build and measure:
  the unit's shell unsets every variable that is not a by-name name, exports the fixed pairs (they are not secret and
  stay positional arguments), then `exec "$@"`. The measure of done is C4's own: the old shape is seen by a reader of
  `/proc/<pid>/cmdline` (the RED), the new shape in 0 of 300 runs.
- With a key, `BindReadOnlyPaths=<real home>/.claude/.credentials.json` is NOT in the properties, and the preflight has a
  row that proves it (a read of that path from the unit fails).
- The keys folder is not visible in ANY boundary unit, coder or check: it is under a hidden path (`/home`), or the unit
  gets `InaccessiblePaths=<dataDir>/keys`. A preflight row reads it from inside and must fail.
- Where the value then exists, and who can read it (C5, C6): the runner's memory; the environment of the `systemd-run`
  client; the unit's `Environment` property and its transient unit file under `/run/user/<uid>` (same user only; hidden
  in every boundary unit by `TemporaryFileSystem=/run/user`, `BoundaryUnit.cs:63`); the environment of the unit's
  processes (not readable from another unit: `PrivateUsers=yes`, `:73`, measured C6). A process of the same user outside
  a unit can read all of them — and the key file too.
- **Alternative, measured, not chosen:** `LoadCredential=` (C8). The manager would hand the key FILE to the unit and the
  runner would never hold the text. Not chosen: the owner's decision names `--setenv=NAME`, that road is built, and a
  read-only role needs the text in coai's memory in any case. It is the fallback if t3 finds that a boundary unit can
  read the unit property or the transient file.
- `env.json` of the runner never holds the variable: `RoleRunEnvironment` (`RoleRunEnvironment.cs:19`) does not allow the
  name, and a test asserts it. The runner reads the key from the store itself.

**Read-only roles (BA, probe; not in a unit).** The value is set in the child's environment block by the launcher. With
a key the launch also gets a home of the job's own — `HOME`, the `XDG_*` names and `CLAUDE_CONFIG_DIR` under the job
folder (the `SlotEnvironment` shape, `SlotEnvironment.cs:29`) — so the person's `.credentials.json` is not read and not
refreshed by a role job. Consequence, decided: such a launch no longer sees the person's user-level settings, hooks and
memory. That is the state the coder already has.

**Output.** Before anything a pool launch printed is stored or read (stdout, stderr, the `attempts/<n>/` files, the
result), the exact text of its key is replaced with `[claude key <id>]`. Before a coder commit, the changed files are
searched for the exact text; a hit means no commit, the job fails `key-in-output`, the key is `dead (exposed)`.
The claude write launch has no shell tool and `--restricted` file tools (R11), so the model cannot read its own
environment by a tool; t3 measures `Read /proc/self/environ`. The scrub and the search are for a CLI that does not keep
that promise.

**Empty pool.** Setting `COAI_ROLE_CLAUDE_PERSON_SIGN_IN` (the `COAI_ROLE_` prefix reaches the runner through
`env.json`):

- **unset (the default, every device):** a claude role job with an empty pool is refused at `start_role` with the cure
  (`coai-mcp --keys add --label <name>`, or the Keys page) and one notice `claude-keys-empty` per day. This is the AMI's
  behaviour: its baked settings never hold the switch, the bake check asserts that, and it has no person's sign-in.
- **`1` (an explicit device switch):** ONLY while the pool holds no key at all (a `disabled` or `dead` key counts as a
  key), a claude role job runs as today: the coder with the read-only bind, a read-only role with the person's home. The
  ledger line and the attempt say `keyId: person-sign-in`; one notice `claude-person-sign-in-used` per day.

Why refuse by default: the owner's rule is that role jobs do not use the person's sign-in, and a silent fallback is the
same risk again (the refresh that cannot be saved) at the moment nobody looks. Why the switch exists: a device with no
key yet (this developer machine, until the first key is added) must still be able to run the epic's tests and proofs.
Why the switch does nothing when keys exist but cool down: then the person's own limit would pay for role jobs.

## 7. Roles, and the Team server (decision 5)

| Launch | Pool? | Why |
|---|---|---|
| coder | yes (s7) | the owner's first case; the open risk of s2 t5 |
| BA, probe, and scout / important of E3 | yes (s7) | the same road (`RoleModelLauncher`); a measure on one road, not per role |
| `cons`, `consask`, gate reviewers (claude rows) | no, unchanged | three other roads (`ConsultationService`, `QuestionFanOut`, `ReviewerExecutor`); they run inside the person's session, read-only and short, and a row has its own key field. Moving them is one decision for E3, done on all three roads with a scan — not a part of this story |
| Team server reviewers | no, unchanged | `accounts/<vendor>/<slot>/claude.token` is one token per slot, with a HOME and an exclusive lock per slot |

Reused from the Team server: the variable name, the cooldown fields, `CooldownParser`, "a success clears the cooldown".
Not reused: `SlotRegistry` itself. A slot is a HOME with a lock because a signed-in slot refreshes its token in place; a
pool key has no refresh, so it needs no lock for the life of a job and no home per key. The pool is device-local; the
Team server is not told about it.

Vendors other than claude: no pool in this story. `kind` in the metadata and the `claude/` folder leave room.

## 8. The Keys page and the page API

The page is epoch 2 E5 (one `doneWhen` line there). It holds no rule. Server side, fixed here (the message names are
fixed when the page API schema is written in `shared/`):

- a list of kind `claude-keys`: `{adminRevision, rows[], usable, personSignInSwitch}`; a row is the metadata of § 3;
- a write for one row: label, `enabled` / `disabled`, removed — with `adminRevision` as the revision ("changed
  elsewhere" when stale). `adminRevision` moves on a change by a person, never on a pick or a counter;
- an add: `{label, secret}`, `secret` write-only (§ 4);
- every write is checked again by the server; the page shows the server's sentence.

On the standalone AMI the same list and writes are the `--keys` one-shot today, and `coai-mcp --serve-pages` later.

## 9. Growth and interruption

| Surface | Size | Who retires it | Interrupted |
|---|---|---|---|
| `keys/claude/` | 200 keys at most (the 201st add is refused); about 6 KiB per key, so 1.2 MiB at most | the owner (`remove`); nothing is appended, counters are numbers in place | an add writes `.key` (temp file, then rename), then `.json`: a `.key` with no `.json` is deleted by the next verb or pick under the lock (the add did not answer `ok`); a `.json` with no `.key` shows as `missing` and is not picked |
| `KeyId` in `usage.jsonl`, `key_id` in `role_attempts` | about 20 bytes per role launch | the retention those stores already have | — |
| notices | `claude-key-dead` and `claude-key-exposed`: one per key; `claude-keys-exhausted`: one per 30 min; `claude-keys-empty`, `claude-person-sign-in-used`: one per day | the notice ledger's own retention | — |

A pick has no in-flight state: nothing is leased, so a crash after a pick strands nothing (the launch is counted).

## 10. Tests (decision 6)

All with FakeCli and made-up tokens, built at run time from parts (no token-shaped literal in the source, so the
repository's own scanners stay quiet). FakeCli gets one verb: it reads `CLAUDE_CODE_OAUTH_TOKEN` and answers by a marker
in the made-up token (ok / limit with a stated reset / the recorded refused-key envelope / print the token / write the
token into a file).

| # | Scenario | Must be true |
|---|---|---|
| K1 | add, list, relabel, disable, enable, remove | modes 0700 / 0600 read back; `list` has no token; the same token twice is refused; label rules |
| K2 | add with the token as an argument, in `--label`, from a file flag | refused; no such flag exists; the answer never echoes input |
| K3 | a data folder where modes are not kept | add refused, with the cure |
| K4 | three keys, six fresh sessions | key ids A B C A B C; the cursor survives a restart |
| K5 | 8 processes pick 400 times at once | every pick is one whole key; counts differ by 1 at most; no torn file; a killed holder frees the lock |
| K6 | key B is limited with a stated reset | B is skipped until that time, then picked again; `limitStops` + 1; no notice |
| K7 | a limit in the middle of a job | the same model runs on the next key in a new session; the attempt has `keyId`; 3 switches at most |
| K8 | a refused key | `dead`, one `claude-key-dead` notice, never picked; a 5xx or a timeout does not make a key dead |
| K9 | all keys cool down | claude-only route: `start_role` refused in 2 s with the earliest time; a route with codex: codex runs; one `claude-keys-exhausted` notice |
| K10 | a resumed coder turn | the same key id; a handle with another key id is refused; a removed key stops the turn |
| K11 | the coder unit with a key | `--setenv=CLAUDE_CODE_OAUTH_TOKEN` (name only); no credentials bind; the preflight rows "sign-in file not in the unit" and "keys folder not readable" hold, and each goes red when its property is dropped (`Degrade`) |
| K12 | the inner command | the old shape is seen in `/proc/<pid>/cmdline` (RED); the new shape in 0 of 300 runs; the child's environment is exactly the allow-list plus the one name |
| K13 | **the canary** | a canary token is added through the real one-shot; a coder job runs to green with FakeCli. Then every argv the test launcher recorded, `job.json`, `request.json`, `env.json` (read before it is deleted), `attempts/`, `result.json`, `usage.jsonl`, `server-notices.jsonl`, the log files, `coai.db`, the page API answers and the page-message log of the fake transport, and the commit hold NO canary text. A companion check proves the search can see it: the same search finds the canary in the `.key` file |
| K14 | FakeCli prints its token, and writes it into a worktree file | stdout is stored with `[claude key <id>]`; no commit; `key-in-output`; the key is `dead (exposed)`; one notice |
| K15 | empty pool | refused with the cure; with the switch: the bind is back and the ledger says `person-sign-in`; the switch does nothing when a key exists |
| K16 | a read-only role with a key | its own `HOME` and `CLAUDE_CONFIG_DIR`; `ANTHROPIC_API_KEY` and `--bare` are absent; a canary `.credentials.json` in the test's home is byte-equal and not opened |

Structural tests (each with its companion "the scan matches something"): one caller of the store's token member; no
`ILogger` or `Console` call takes the secret type; the `--keys` mode is in the one-shot list of `.agents/PROJECT.md`;
`keys/` is in `shared/data-inventory.json`.

**Real measures (t3, with one real key the owner adds through `--keys add`; haiku; 6 calls and 0.05 USD at most):**
the coder unit with the key and no credentials file answers; nothing under the real home is read or written (bytes and
mtime of `.credentials.json` before and after); the unit property set of § 6 with the real CLI; the envelope of a refused
key (a made-up token: no cost) saved as a fixture; `Read /proc/self/environ` by the model; a read-only launch with its
own config folder; C5 and C6 again inside the real property set. Results: `RESULTS_s7_key_pool.md`.

**Real proof (s6 `t5-pool-key-proof`, before `t3-real-coder-run`):** one real coder job to green with a pool key on
haiku, at most 0.10 USD and 2 paid runs. The result file has: the key id in the ledger line and in the attempt; the
unit's properties with no credentials bind; `.credentials.json` of the person byte-equal with the same mtime;
`--keys scan` over the data folder, the log folder and the worktree with 0 hits; the key's `lastUsedUtc` and `launches`
moved. It is the first time the code's own argv is given to the real CLI with a key.

## 11. Risks, and what is not known

1. **The variable is verified from the CLI's text, not from a launch here** (C2). The Team server uses it in production
   (C3). t3 is the first launch on a device.
2. **A process of the same OS user can read every key** (file, unit property, `/proc`). The pool does not change that
   class of risk; it removes the person's sign-in from it. Inside boundary units the roads are closed and measured (C6).
3. **The shared VM instance**: every member of `coai` reads every key (owner's decision for that instance). Also seen
   while reading: a boundary unit there can READ the rest of `/var/lib/coai` (`ProtectSystem=strict` makes it read-only,
   not hidden) — the hub device token and the shared vendor homes of § 5. Not a part of this story; epoch 2 E2 must hide
   the data folder in units.
4. **One auth failure makes a key dead.** A vendor fault that refuses good keys would make many keys dead at once; the
   owner enables them again by hand. Kept simple on purpose: only a recorded signal counts.
5. **The CLI's promise is the first guard against the model reading its environment** (no shell, `--restricted`). The
   scrub and the commit search are the second. A CLI transcript in the job's own home can still hold a printed token
   until the job folder is retired (30 days after the end; the coder's home is in the job folder).
6. **A setup-token lasts one year** (C2) and coai does not know its end date. An ended key shows as `dead (auth)` with
   its notice. Not in this story: an end date on the page.
7. **Read-only roles lose the person's user-level claude settings** when they run with a key (§ 6). The BA proof of E1
   was made with them. The FakeCli tests do not see the difference; a real BA run after s7 is the check.

## 12. Brief for the coder (Sonnet), in build order

- t1 store: new `src_mcp/src/Server/Keys/` — the store (files, id, modes read back, `PrivateAcl`, `AtomicJson`, the lock
  of `SessionClaim.cs`), the secret type, the data-inventory entry. Lock test first.
- t2 one-shot: `--keys` in `Program.cs` beside `--roles`; stdin one line, no echo; `import`; `scan`; the page API
  answers as JSON; `.agents/PROJECT.md`.
- t3 measure (the main session, or a scout under it): § 10 "Real measures".
- t4 pool: pick, cursor, cooldown (`CooldownParser`), dead, exposed, the notices in `ServerNoticeCodes`.
- t5 wiring: `RoleModelLauncher`, `RoleFailover` (key switch, vendor skip, start refusal), `ClaudeConsultant.Request`,
  `CoderUnit` (inner command, no bind), `BoundaryPreflight` (two rows), `CoderHandle` (key id), `UsageEntry.KeyId`, the
  RoundsDb step, the scrub, the commit search, the switch. The RED of K12 first.
- t6 tests: K13 and the structural scans; the FakeCli verb.

No phrase is matched without a recorded fixture. No new package. Nothing here calls a vendor in a test.

## 13. Not in this story

Keys of other vendors; `cons`, `consask` and reviewers on the pool; a key added to another device through the hub;
encryption at rest; an end date per key; `--serve-pages`; a check call at `add`; the same-vendor "next model" after all
keys cool down.

## 14. Questions only the owner can answer

1. **The first keys.** t3 and the s6 proof need at least one real setup-token in the pool of this machine, added by the
   owner with `coai-mcp --keys add --label <name>` (two keys show the round robin with real keys). When?
2. **The shared VM instance.** Is "every member of group `coai` can read every pool key" accepted for that instance, as
   for its shared CLI logins? (If not: one pool per OS user there, and no round robin across users.)
3. **The fallback switch on this developer machine.** May `COAI_ROLE_CLAUDE_PERSON_SIGN_IN=1` be set here until the
   first key is added, so the epic's real runs before s7 keep working? After the first key it does nothing.
4. **Accounts behind the keys.** Are the many keys from several subscriptions (then a limit on one key does not touch
   the others), or several tokens of one subscription (then one limit stops all of them, and the round robin only
   spreads sessions)? coai cannot see this; it decides how many keys make sense.

## Addendum 2026-11-23 (owner) — key sources

The pool reads keys from more than one SOURCE; the round robin, cooldown, dead state and ledger work over the union,
each key keeping its id (sha256 prefix) whatever its source:

1. **local** — `coai-mcp --keys add|import` (stdin), the 0600 files of decision 1. Always available.
2. **creds config** (the owner's machines) — one CredsForDevs `config` entry `coai claude keys`, format json, file
   `coai-claude-keys.json`, body `{"keys":[{"label","account","token"}]}`, read by coai through the entry's code-access
   key (the person mints it with "Enable Code Access…"; it reaches coai only as an environment variable, never argv,
   never a log). coai keeps no second copy when the source can be read at launch time. OPEN, measure before build:
   does the read work headless (VS Code closed, vault locked)? If not, the creds read fills the local pool at coai
   start and jobs use the local pool. The code-access key is a long-lived secret with the same rules as a pool key.
3. **boot import** (the orchestrator AMI, no creds) — a systemd oneshot `coai-keys-sync.service` reads AWS SSM
   Parameter Store SecureStrings under one path (e.g. `/coai/claude-keys/*`) with the instance role and pipes them to
   `coai-mcp --keys import`; the image holds the unit, never a key (pending the owner's answer on IAM role + AWS CLI).

`account` (subscription email) lets the pool know which keys share one limit: a limit signal cools down every key of
the same account, not only the one that hit it.

### Measured 2026-11-23 — the creds source

- The owner's entry exists: CredsForDevs `config` entry **"claude long tokens"** (folder `configs`, format json).
- `creds config -` (installed CLI at ~/.local/bin/creds) "asks the VS Code window holding your vault for this config and
  prints it"; the code-access key goes on stdin (never argv) or in `CREDSFORDEVS_KEY`. So a read works ONLY while that
  window is open and the vault unlocked — never headless, never on the AMI.
- Decision: no standing link. The creds source is a one-shot IMPORT into the local pool:
  `creds config | coai-mcp --keys import --format creds-json` (owner's terminal, key only for that command), and a VS
  Code command "coai: Import keys from creds" that asks for the code-access key in a password field. coai NEVER stores
  the creds code-access key. Re-import after the entry changes; a key gone from the entry is disabled, not deleted, on
  re-import (the import names its source so only keys of that source are touched).
- `--format creds-json` reads `{"version":1,"keys":[{"label","account","token"}]}`; unknown keys refused; labels unique.

### Owner decision 2026-11-23 — the AMI source is an env file (replaces the SSM idea)

- File `/etc/coai/claude-keys.env`, owned by the orchestrator user, mode 0600 (wrong owner or mode = refused with a
  sentence, nothing imported). Placed by the instance launch step, never in the image (the image ships only the empty
  folder and the rule).
- Lines: `COAI_CLAUDE_KEY_<label>=<token>` and optional `COAI_CLAUDE_ACCOUNT_<label>=<email>`; label = `[a-z0-9-]{1,40}`
  after lower-casing; comments `#` and blank lines allowed; any other line refused (named by line number, never echoed).
- coai syncs the pool from the file at every start (source `file:/etc/coai/claude-keys.env`): new → added, changed →
  replaced, missing → disabled; only keys of this source are touched. Rotation = edit + restart. No AWS dependency.
- The same `--format env` import works on any device (`coai-mcp --keys import --format env < file`).

### Owner correction 2026-11-23 — generic sources, no fixed path (supersedes the fixed /etc path above)

How a key gets onto a machine does not matter. coai reads keys from two generic sources, on every device:
1. **Environment variables of the coai process:** `COAI_CLAUDE_KEY_<label>=<token>` and optional
   `COAI_CLAUDE_ACCOUNT_<label>=<email>` (set by whatever starts coai: a systemd unit, the MCP client config, a shell).
2. **A file at a path the person chooses:** `COAI_CLAUDE_KEYS_FILE=<path>` or the device setting `keysFile`. Format is
   detected: the env-line format above, or the creds json `{"version":1,"keys":[{"label","account","token"}]}`. On Linux
   the file must not be readable by group/other (refused with a sentence otherwise); on Windows the owner-only ACL rule.
Both are synced into the pool at every start (new → added, changed → replaced, missing → disabled; only keys of that
source are touched). The `--keys import` one-shot stays for manual adds and the creds import. The COAI_CLAUDE_KEY_*
values never reach any child process except the one chosen key as `CLAUDE_CODE_OAUTH_TOKEN` (the minimal environment
already drops every COAI_ name; a test proves it for these names).
