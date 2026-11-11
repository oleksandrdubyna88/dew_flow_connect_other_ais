# PLAN — E2 stories: the remote Claude session, read over ssh

> Status: **plan only, nothing implemented yet, 2026-11-11; plan gate `proceed`, its findings folded in.** Epic E2 of
> [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (its `## Epics`, its § *Stage 2*, its
> § *Security* and its ledger entry 1.1). Written by Opus when the epic started, on `feat/chat-from-a-terminal-e2` from
> `main` `45b070a2`. A stories document keeps no change ledger: a change to this epic is an entry in the parent (epoch 2,
> 0 changes today; the next one is 2.1, and a second one is a full stop).
>
> Scope: `src_vs_code` only, plus `SECURITY.md`, `research/` and the help. Four stories (the gate's order: an epic has 3–4
> stories). **Cornerstone: E2.S2** — the remote reader's contract: the ssh argv, the stdin script and the typed result,
> areas (d) and (e). Fable writes its scenarios and brief, INCLUDING the security judgement on the ssh edge (which stays
> outside the quota, `subagent-models.md`, the security row), in `todo/PLAN_the_chat_opens_from_a_terminal_E2_S2_spec.md`
> (not written yet). The parent's ledger line reads `Cornerstones: 2 of 2 — E1.S2, E2.S2`.

## 1. Goal, before any solution

E1 is released (PR #971, squash `0851f074`, extension 0.86.0, tag `extension-v0.86.0` on `45b070a2`). From a terminal, the
person can now open a chat with the text they copied. But for a `Creds: <host> / <session>` terminal the useful part is
still missing: Claude Code runs on the VM, in tmux, and its session file is on the VM. So *take the question*, *add the
question* and *Asked* cannot read it. The owner used them several times an hour (parent § *The symptom*, decision 5).

When E2 is done:

1. In a terminal called `Creds: <host> / <session>`, two items in the terminal title bar's overflow menu, **CoAI: take the
   question** and **CoAI: add the question**, read the Claude session that runs in tmux session `<session>` on `<host>`,
   and put its waiting question into the chat of that terminal. They never send it by themselves.
2. In a chat opened from such a terminal, **Asked** shows what the person wrote in that remote session.
3. The read is ONE read-only ssh call on the registry path, by the exact session id. The title fallback takes at most three
   calls and gives a list when it is not sure, never a guess.
4. The first read for a host asks the person once. A "no" is kept, and that host stays clipboard-only until the person
   changes it.
5. Absent, ambiguous, unreadable and truncated session data give four different sentences. A failed ssh call gives its own
   sentence, never one of those four.
6. The AI still runs locally. Every other terminal stays clipboard-only, as in E1.

The silent failures this epic must not ship:

- **Somebody else's session, delivered as this one** — a pick between two panes, two `claude` processes, two transcripts
  or two titles; or a local namesake read instead of the remote session. Nothing would look wrong.
- **Cut data that looks whole** — a 4 MiB tail that holds no question, reported as "nothing was asked".
- **An outward call nobody agreed to** — an ssh call to a host the person has not said yes to, or one that forwards a port,
  an agent or X11.
- **The conversation in a log** — stdout is the transcript; it must never reach a log line or the disk.
- **Something left running** — an ssh child, a multiplexing master or a remote command that outlives the read.

## 2. What exists — verified at `45b070a2`, 2026-11-11

All paths are under `src_vs_code/` unless they start with `research/` or are `SECURITY.md`. Every name below was opened at
this commit.

| Fact | Where |
|---|---|
| The terminal door (E1): the name guard, the capture, the match, the delivery with `fromSession` `false` and `sourceOfTerminal` | `src/chatCommand.ts:302` `terminalDoorName`, `:341` `askFromTerminal`, `:364-366` |
| `newConversation` decides the flag by the source, then pins in the background | `src/chatCommand.ts:65`, `:85` (`readsSession(state.source, state.fromSession)`), `:201` `pinSession(entry, state.title, fromSession)` |
| `deliverPassage` (the one delivery of every door; `append` for *add*) | `src/chatCommand.ts:377-497` |
| The waiting question in this window, and its sentence mapping (local only, `os.homedir()`) | `src/chatCommand.ts:550-592` `questionWaitingHere` (the mapping `:562-591`); `:607` `takeTheQuestion` (never sent by itself, `:648-651`) |
| The source kind, its builders, the identity rule | `src/chatStore.ts:75-83` `ConversationSource`; `:101` `TERMINAL_NAME_MAX`; `:107` `isTerminalName`; `:110` `sourceOfTerminal` |
| The origin rule: `terminal` is `'not yet'`, so `fromSession` reads `false` on every road | `src/chatStore.ts:290-306` `OriginRule`, `ORIGIN`; `:325` `agreeOnOrigin`; `:336` `readsSession`; `:403` (used by `recordFrom`) |
| The pin: runs on `fromSession` and a title only, walks the LOCAL home | `src/chatSessionJoin.ts:185` `pinSession`; `:61` `findSessionIn` (`os.homedir()`, `:64`) |
| The Asked press with no file: by id, then by NAME in the local home | `src/chatSessionJoin.ts:75` `resolveAndPin`, `:105` |
| The Asked press: the file first, else `resolveAndPin` | `src/chatHooks.ts:361` `onShowAsked`, `:395-397` |
| The restore: the page and the thread take `saved.fromSession` (from `recordFrom`); the re-pin only for `none` | `src/chatConversationRestore.ts:93`, `:205`, `:265-267` |
| The page draws Asked only when `fromSession`; a refusal shows only when there are NO prompts | `src/chatPage.ts:1181`, `:1675`; the `asked` message `:2121-2134` |
| The pure line rules E2 reuses | `src/claudeQuestion.ts:110` `collectNames`, `:291` `lastAsked`, `:327` `humanSaid`, `:356` `titleFrom`, `:379` `customTitleFrom`, `:454` `askedAsText` |
| The pure session rules E2 reuses (`claudeSessions.ts` is 1191 lines) | `src/claudeSessions.ts:72` `projectDirNames`, `:333` `namedAmong`, `:400` `SCAN_BUDGET`, `:512` `waitingIn`, `:686` `MOST_PROMPTS`, `:687` `MOST_PER_PROMPT`; `Asked` `:690`, `Found` `:717` |
| The session-id shape (private today) and its reader | `src/chatSource.ts:68` `SESSION_ID`, `:56` `sessionIdOf` |
| The one spawn site: `spawn(target, [...args], { shell, windowsHide: true, cwd?, env? })` — `shell` false unless asked, NO `windowsVerbatimArguments`, so on win32 node quotes each argument by the C runtime rules (an argument with no space, tab or `"` is passed as it is) | `src/processLauncher.ts:141-160` `launch`; `:125` `workingDirectory` (no `cwd` and no shell → the extension host's own); `:34` `STDERR_TAIL_MAX`, `:110` `stderrTail` |
| The capture over it: stdin, a timeout that kills the tree — seven POSITIONAL parameters, no stdout cap, no stderr in the result | `src/versionProbe.ts:92-120` `capture`, `:69` `CaptureEnd`, `:72` `Captured`; `:196` `resolvedExecutable` |
| A one-at-a-time latch | `src/oneAtATime.ts:32` `oneAtATime` |
| The registrations of the doors, and their catch (the error's text goes to `console.error` and to `notify`'s `detail`) | `src/extension.ts:677` `coai.askFromTerminal`, `:708-729` `coai.takeTheQuestion`, `:731` `coai.addTheQuestion` |
| The manifest: commands, the terminal button, the palette, the chords, the skip list | `package.json:274-287`, `:1181-1185` (`view/title`), `:1238-1255` (`commandPalette`), `:1309-1330` (`keybindings`), `:1349-1352` |
| The door census reads `webview/context`, `editor/context` and keybindings — NOT `view/title` | `src/test/chatWiring.test.ts:480-488` `doorCommands`, `:555-585` |
| Tests that pin the stage-1 rule E2 changes | `src/test/chatStore.test.ts:529-550`; `src/test/chatSourceWiring.test.ts:305-311` |
| A person's answer, with the wait made visible | `src/personWait.ts:35` `askPerson` |
| A context key, set the way this extension sets one | `src/extension.ts:334` (`setContext`, `coai.hasQuestions`) |
| Unit tests cannot load a module that imports `vscode` (no stub): host logic is tested through pure modules with seams | `src/test/` (no `vscode` stub in `scripts/`) |
| The help article and its four translations; the help census | `src/helpContent.ts:294`, `src/helpDe.ts:53`, `src/helpEs.ts:53`, `src/helpRu.ts:53`, `src/helpUk.ts:117`; `src/test/helpCoverage.test.ts` |
| The host modules list that `sonarExclusions.test.ts` holds exact | `.github/workflows/sonarcloud.yml:268` |
| The docs that describe E1 | `research/module_extension.md:13364` (*The chat opens from a terminal (E1)*); `research/module_tests.md:306`; `research/architecture.md:615` |
| `SECURITY.md` today states only `coai-mcp`'s path guarantees | `SECURITY.md:1-4` |

## 3. Conventions every story follows

- **Worktree.** `/home/jinx/git/wt/coai-chat-terminal-e2`, branch `feat/chat-from-a-terminal-e2`. Never the main checkout.
- **Models.** Spec (scenarios and brief): Opus for S1, S3 and S4 (this document), **Fable for S2** (with its security
  judgement); an **Opus review of the S2 spec** before S2 is coded (§ 6, gate G6). Code and tests: Sonnet. Review: Opus.
  Fixes: Sonnet.
- **RED-GREEN-RED.** Each scenario names its assertion and its break-it: the production line to remove, and the failure
  message the test must then show. For new code the first RED is the test written before the code; the break-it proves the
  test has teeth. Report both observations in the summary.
- **Every test file** starts with `import '../../scripts/inRunFolder.cjs';`.
- **Pure first, with seams.** Unit tests cannot load a module that imports `vscode`. So every decision AND every
  orchestration that a test must see lives in a pure module and takes its effects as parameters: `credsSession.ts`,
  `remoteScript.ts`, `remoteSsh.ts` (node only), `remoteRead.ts`, `waitingSaid.ts`. ONE new host module is allowed in E2,
  `src/remoteSessionHost.ts` (S3); it only supplies the `vscode` seams, and it gets its row in `sonar.coverage.exclusions`
  in `sonarcloud.yml` in the same commit (`sonarExclusions.test.ts` holds the list exact). Other host code goes into the
  host files that exist (`chatCommand.ts`, `chatHooks.ts`, `chatSessionJoin.ts`, `extension.ts`).
- **Reuse first.** No second copy of a rule: `projectDirNames`, `namedAmong`, `lastAsked`, `humanSaid`, `collectNames`,
  `waitingIn`, `SESSION_ID`, `oneAtATime` and the sentence mapping are CALLED, never re-typed (in TypeScript or in `sh`).
  The ssh child goes through `launch` (the one spawn site), by WIDENING `capture`, never by a second launcher.
- **Lint.** A new function keeps `complexity` ≤ 4 and ≤ 50 lines; no suppression count grows (CI step *Lint suppressions
  only shrink*). Split, do not suppress.
- **A new `notify` call site** changes the census: `node scripts/count-notifications.mjs --write`, commit
  `notification-sites.json`.
- **A webview page change** (S4 only, the Asked note) is tested by RUNNING the page (`bundledPage.test.ts`), never by a
  source assertion (`.agents/PROJECT.md`).
- **No stdout in a log.** No `console.*`, `notify` detail or error message may carry the child's stdout, any text cut from
  it, an error whose message may quote it (a V8 `JSON.parse` error quotes its input), or a folder-name operand. Logs carry
  host, session, exit code and the stderr tail only (parent § *Limits*).

Commands (from `src_vs_code/`):

```bash
npm ci                                                        # once
npm run compile                                               # read the exit code: tsc emits even when red
node scripts/run-tests.mjs out/test/<name>.test.js            # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test   # the whole suite (compile + run-tests)
npm run lint                                                  # eslint src
node scripts/count-notifications.mjs --write                  # after any new notify call
npm run test:host                                             # CANNOT start on this machine (no libnss3) — see § 8
```

From the worktree root: `node .agents/conventions/tools/plan-lifecycle.mjs` before every commit that touches `todo/`.

## 4. Shared contracts — fixed here so S1, S3 and S4 can be briefed while Fable writes S2's spec

S2's spec may refine C2 and C3. **Gate G6:** Opus reviews that spec, and any change it makes to C2 or C3 is written into
this section AND into the briefs of S3 and S4 in the same commit, before S3 or S4 is coded.

- **C1 — which terminal reads a session (S1 owns).** NEW pure `src/credsSession.ts` (no imports):
  `credsSession(name: string): CredsName` with
  `CredsName = { kind: 'session'; host: string; session: string } | { kind: 'other' } | { kind: 'malformed'; refusal: string }`.
  The pattern is the parent's, exactly: `^Creds: ([A-Za-z0-9][A-Za-z0-9._-]*) / ([A-Za-z0-9][A-Za-z0-9_-]*)$`. `other` =
  the name does not start with `Creds: ` (so `Creds run: x` and `Creds VPN: x` are `other`). `malformed` = it starts with
  `Creds: ` and fails; the refusal names the part that failed. `readsSession` (`chatStore.ts:336`) reads it.
- **C2 — the script and its parsed result (S2 owns; Fable).** NEW pure `src/remoteScript.ts`: `REMOTE_SCRIPT: string` (the
  POSIX `sh` text sent on stdin — the text under test IS the text sent); the operands of each call (C2a); and
  `readRemote(stdout: string, ...): RemoteRead`, the parser. `RemoteRead` is one union with an arm per outcome: `found`
  (session id, `cwd`, name, the tail's lines, and `cut: boolean`), `fallback` (the pane's `cwd` and the folder names, or the
  title rows), `gone` (a picked id whose file is not there), `absent`, `ambiguous` (with the candidates, possibly none) and
  `unreadable`. **There is no `truncated` arm**: the parser reports `found{cut}` only, and what a cut MEANS is decided once,
  by the purpose, in C4. No arm is an optional field to interpret.
  - **C2a — the operands.** The registry path keeps the parent's form: `sh -s -- <session>`. The spec fixes the operands of
    a call that carries a picked session id, and of the fallback's calls (ii) (the candidate folder names) and (iii) (the
    id). Every operand has a validation rule, and **every rule's alphabet has no space, no tab, no quote and no shell
    character** — so on win32 node passes each argv word to `ssh.exe` unchanged (`processLauncher.ts:149-154`: no shell,
    no `windowsVerbatimArguments`; the C runtime rules quote only a word with a space, a tab or `"`), `ssh.exe` joins the
    remote words with spaces, and no quoting has to cross the Windows boundary. The parent's single quotes are for the
    REMOTE login shell only.
- **C3 — the ssh edge (S2 owns; Fable, with the security judgement).** NEW `src/remoteSsh.ts` (node only, no `vscode`):
  `sshArgv(host: string, operands: readonly string[]): readonly string[]` (the parent's options, in the parent's order, the
  host after `--`) and `runRemote(host: string, session: string, operands: readonly string[], script: string, limits?:
  RemoteLimits): Promise<RemoteRun>` — `session` is its own argument because it is the LOG LABEL; the operands (folder
  names among them) never reach a log. `RemoteRun = { kind: 'answered'; code: number; stdout: string } | { kind: 'refused';
  refusal: string }` — `refused` covers a timeout at 15 s, stdout over 8 MiB (counted in BYTES, `Buffer.byteLength`), a
  child that did not start, an operand that failed validation, and an ssh exit 255. It never rejects. Its log line (host,
  session, exit code, the stderr tail) is written here and only here. It goes through `capture`, widened by ONE options
  object `{ cwd, maxStdoutBytes }` (no more positional parameters), with `shell: false`.
- **C4 — one road to the VM (S3 owns).** Pure `src/remoteRead.ts` holds the road as a function of seams (the prompt, the
  answer store, `runRemote`, the pick list, the progress line): `remoteRoad(seams, terminalName, purpose: 'question' |
  'prompts', picked?: string): Promise<RemoteAnswer>`, and `answerFrom(found, purpose): RemoteAnswer` — the ONE place that
  decides what a cut tail means for that purpose. `RemoteAnswer = { kind: 'lines'; lines: readonly string[]; note: string }
  | { kind: 'refused'; refusal: string }` (`note` is the cut note for *Asked*, `''` otherwise). The host module only wires
  the `vscode` seams: `readRemoteSession(terminal, purpose)`. Nothing else in the extension spawns ssh or reads
  `globalState` for the consent.
- **C5 — the words (S4 owns).** The `Waiting` → sentence mapping of `questionWaitingHere` (`chatCommand.ts:562-591`) moves,
  unchanged in its words, to one exported pure function `waitingSaid(found: readonly Waiting[]): { text: string; refusal:
  string }` in a NEW pure module `src/waitingSaid.ts` (not into `claudeSessions.ts`, which is 1191 lines). The local path
  and the remote path both call it.

## 5. Stories

Build order and its gates: § 6.

### E2.S1 — Which terminal reads a session, and no local walk for a terminal

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (c).

- **Goal.** A conversation of a `Creds: <host> / <session>` terminal reads its session (`fromSession` true); every other
  terminal does not. The flag is DERIVED from the source on every road into a thread, whatever a record says — so an E1
  `Creds:` conversation, saved with `fromSession: false`, gains *Asked* when it is restored or reused under E2 (parent change
  1.1, point 1). And no road walks the LOCAL home for a terminal source.
- **Files and functions (what and how).**
  1. NEW `src/credsSession.ts` (C1). The refusal of a `malformed` name names the part, in one of three sentences (quote the
     part as it is, at most 80 characters, `…` after a cut):
     - no ` / ` separator, or more than one: *"This terminal’s name is not `Creds: <host> / <session>`, so its Claude session
       cannot be read. Only the text you copied can be sent."*;
     - the host part fails: *"The host “<part>” in this terminal’s name is not a name ssh can be given safely (letters,
       digits, `.`, `_` and `-`, not first), so its Claude session is not read."*;
     - the session part fails: *"The tmux session “<part>” in this terminal’s name cannot be read: only letters, digits, `_`
       and `-` are allowed (tmux changes a `.`)."*.
     A leading `-` is refused by the pattern itself (no ssh option injection), and the code says so in a comment.
     Also in this file, pure: `walksLocalHome(source, fromSession): boolean` (false for any `terminal` source) and
     `NO_LOCAL_SESSION = 'This chat came from a terminal: its Claude session is not on this computer, so it is not looked
     for here.'` — the seams S1-T8 and S1-T9 test as values.
  2. `src/chatStore.ts` — the `terminal` row of `ORIGIN` (`:301-306`) becomes a new rule `'by name'` (*"either on disk; read
     as what the name says: a `Creds:` session name reads its session, any other name does not"*). `readsSession(source,
     fromSession)` (`:336`): for a `terminal` source → `credsSession(source.name).kind === 'session'`, whatever
     `fromSession` was written. `agreeOnOrigin` still accepts either value for a terminal (a record an E1 build wrote, and a
     record an E1 build reads after a rollback, both stay readable). Rewrite the comments at `:297`, `:322-323`, `:332-335`.
     `CONVERSATION_VERSION` does not move.
  3. `src/chatSessionJoin.ts` — `pinSession(entry, title, fromSession, source: ConversationSource)` (REQUIRED parameter, so
     the compiler lists both callers): its first statement is `if (!walksLocalHome(source, fromSession) || …) return;`,
     before any walk. This is the one guard; the callers `chatCommand.ts:201` (`state.source`) and
     `chatConversationRestore.ts:266` (`saved.source`) only pass it. `resolveAndPin` (`:75`): for a `terminal` source it
     answers `{ kind: 'none', refusal: NO_LOCAL_SESSION }` without a walk. **Until S4 lands, a `Creds:` chat shows *Asked*
     (`fromSession` true) and its press reaches here — so this sentence is what the person sees between S1 and S4, and it
     is true in both states.** From S4 on, the remote road takes the press first and this is the second line of defence.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S1-T1 | a `Creds:` name gives its host and session (`credsSession.test.ts`, new) | `Creds: ai1-dev2 / access-slack-integration` → `session`, `ai1-dev2`, `access-slack-integration`; `Creds: a.b-c_d / s_1-2` → `session` | drop `.` from the host class → `a host with a dot was refused` |
| S1-T2 | other terminals are `other`, never `malformed` | `bash`, `pwsh`, `Creds run: x`, `Creds VPN: x`, `creds: h / s`, `` → `other` | treat any `Creds` prefix as creds → `Creds run: x was read as a creds terminal` |
| S1-T3 | a bad `Creds:` name says which part failed | `Creds: before alice` and `Creds: h / s / t` → the shape sentence; `Creds: -h / s`, `Creds: h;id / s`, `Creds: h$(x) / s`, `Creds: h s / t` → the host sentence with that part; `Creds: h / s.x`, `Creds: h / s ` (trailing space), `Creds: h / ` → the session sentence; none of them is `other` or `session` | answer one sentence for all → `the refusal does not name the part that failed` |
| S1-T4 | the hostile names never pass | 30 generated names (`;`, `` ` ``, `$(`, `'`, `"`, `\`, `|`, `&`, `>`, `*`, space, tab, NUL, a newline, `-o`, `--`, a non-ASCII letter, 300 characters) in host or session → `malformed` | loosen the session class to `\S+` → the first hostile name listed |
| S1-T5 | a `Creds:` conversation reads its session, others never (`chatStore.test.ts`, REPLACES the test at `:544-550`) | `readsSession(sourceOfTerminal('Creds: h / s'), false)` true; `(…'bash', true)` false; `(…'Creds: before x', true)` false; the other kinds unchanged | return `fromSession` for a terminal → `a Creds terminal conversation does not offer Asked` |
| S1-T6 | an E1 `Creds:` record gains Asked on restore (change 1.1 point 1; REPLACES the test at `:529-542`) | `recordFrom` of a terminal record `Creds: ai1-dev2 / small-tasks` with `fromSession: false`, `true` and absent → `fromSession` true all three times; `bash` with `true` → false; the record is still read (not dropped) in all cases | keep `'not yet'` → `an E1 Creds conversation came back without Asked` |
| S1-T7 | every road into a thread takes the derived flag (`chatSourceWiring.test.ts`, REWRITES `:305-311`) | `newConversation` keeps `readsSession(state.source, state.fromSession)` and reads `state.fromSession` once; its pin is `pinSession(entry, state.title, fromSession, state.source)`; the restore's page and thread read `saved.fromSession` only (the value `recordFrom` derived), never `row.fromSession` | pass `state.fromSession` to the pin → red naming the pin |
| S1-T8 | the pin never walks for a terminal — through the SEAM (`credsSession.test.ts`) | `walksLocalHome(sourceOfTerminal('Creds: h / s'), true)` and `(…'bash', true)` false; `({kind:'none'}, true)` and a `claude` source with `true` → true; `fromSession` false → false. Companion (the only source check, where no seam reaches): the FIRST statement of `pinSession` calls `walksLocalHome(` | answer `fromSession` for a terminal → `a terminal conversation walked the local home`; move the call after `findSessionIn(` → the companion is red |
| S1-T9 | the Asked fallback never walks for a terminal, and says why | `NO_LOCAL_SESSION` is exactly the sentence above; `resolveAndPin`'s body checks the source and returns `{ kind: 'none', refusal: NO_LOCAL_SESSION }` before `findSessionById(` and `findSessionIn(` | remove the check → red; answer another sentence → `the person is not told why Asked found nothing` |

  The existing tests in `chatStore.test.ts` that state E1's rule are REPLACED, not kept beside the new ones (two tests that
  disagree about one rule cannot both be green).
- **Docs.** `research/module_extension.md`, a new section *The chat reads the remote Claude session (E2)* after the E1
  section: one bullet — which names read a session, the three sentences, the derived flag and why (change 1.1), the two
  guards and `NO_LOCAL_SESSION`. `research/module_tests.md`: a new row *Chat with other AI — THE REMOTE SESSION (E2)* with
  S1's tests.
- **Done when.** S1-T1…T9 green, each break-it seen red; `npm run compile` exit 0; the whole suite and lint green.
- **Size.** About 280 lines (≈ 70 code, ≈ 210 tests).

### E2.S2 — The remote reader's contract: the ssh argv, the stdin script and the typed result (CORNERSTONE)

`model: fable` — the scenarios, the brief AND the security judgement on the ssh edge are Fable's, in
`todo/PLAN_the_chat_opens_from_a_terminal_E2_S2_spec.md`. The cornerstone counts once against the quota (2 of 2); the
security judgement inside it is the security row's, outside the quota. Coder: sonnet · review: opus (of the spec first,
gate G6; then of the code) · areas (d) and (e).

**Why this story is the cornerstone.** It is the only outward edge, and the only place where wrong or cut data from another
machine can arrive and look correct. The argv decides which host is called with which options; the script decides which
pane, which process, which registry file and which transcript are read; the parser decides what their bytes mean. A wrong
pick, a cut tail read as whole, a banner read as data or a forwarded agent gives no error. This is the parent's own
recommendation, word for word: *"the story that defines the contract of the remote reader — the ssh argv, the stdin script
and the typed result, areas (d) and (e)"*.

- **Goal.** `sshArgv` and `runRemote` (C3) are the one road to `ssh`; `REMOTE_SCRIPT` (POSIX `sh`, read on stdin by `sh -s`)
  does the five steps of the parent's § *What the script does*, read-only, leaving nothing behind; and `readRemote` turns its
  stdout into a `RemoteRead` (C2) that never makes a guess and never mistakes a failure for an absence.
- **Scope.** NEW `src/remoteScript.ts` and `src/remoteSsh.ts`; `src/versionProbe.ts` (`capture` widened by an options
  object); `src/chatSource.ts` (EXPORT `SESSION_ID`, `:68`, so the extension checks the UUID shape with the one rule); tests,
  including the script run by a real `sh -s` and operands through a real `sh -c`. Not in scope: the consent, the pick list
  and the meaning of a cut (S3), the doors (S4).
- **Gate G2 + G4 — the security judgement, BEFORE any S2 code.** The spec's security part decides and writes down:
  1. **Which `ssh` runs, from which folder.** A bare `ssh` on win32 is searched in the CURRENT FOLDER first by the spawn
     layer, then on the PATH, and with no `cwd` the child inherits the extension host's folder (`processLauncher.ts:125`).
     So: an absolute path on Windows (for example `%SystemRoot%\System32\OpenSSH\ssh.exe`, never resolved from the current
     folder), `resolvedExecutable` or a fixed path elsewhere, and an explicit `cwd` (C3's options object) — or another
     answer, with the reason.
  2. **Which host configuration is accepted.** The host comes from the LIVE terminal name (E1 § 11, carried note) and the
     consent is per host (S3). Is the person's own `Host` alias the only authority, with what it can carry (`ProxyCommand`,
     `ProxyJump`, `IdentityFile`, `StrictHostKeyChecking`)? What `BatchMode=yes` stops and does not stop (a password or
     passphrase prompt is a refusal; an unknown host key is a refusal unless the person's config says `accept-new`).
  3. **Whose keys.** The person's keys and agent, through their config; nothing handled by the extension; no secret in argv
     or a log.
  4. **Multiplexing.** `ControlMaster`/`ControlPersist` in the person's config could reuse the open terminal's connection
     or leave a background master after the 15 s kill. The spec first CHECKS whether Win32-OpenSSH supports `ControlMaster`
     at all (it is the side the owner's terminals run on), then decides.
  5. **No forwarding** — whether the parent's options (`ClearAllForwardings`, `ForwardAgent=no`, `ForwardX11=no`,
     `RemoteCommand=none`, `RequestTTY=no`) are enough.
  6. **Nothing left behind on the VM.** The whole script is ONE function, called with `</dev/null` after its definition is
     read, so no data command can read the rest of the script from stdin; every data command runs with `2>/dev/null` and
     its own stdin from `/dev/null`; a `trap` where needed, so a broken connection ends the remote work.
  7. **The window between press and read** (a rename, a close): what is re-checked just before the spawn.
  **An ssh option added to the parent's list (for example `-o ControlPath=none`) is plan change 2.1.** The spec reports it;
  the caller records it in the parent's ledger and gets it approved BEFORE S2 is coded. The coder never adds an option.
- **Areas the spec must cover.**
  1. **The argv (C3).** The parent's options in the parent's order; **no `-n`** (it would give `sh -s` an empty stdin);
     `--` right before the host; `sh`, `-s`, `--` after it; each operand single-quoted for the REMOTE login shell (ssh joins
     the command words into one line and the login shell parses it). A NUL, a CR or an LF in the host or an operand THROWS;
     the host must pass C1's host pattern and each operand its C2a rule — re-checked here, at the one road in, not only at
     the door. **Windows (G0):** the validation alphabets have no space, no tab, no quote and no shell character, so the
     argv node hands `ssh.exe` is byte for byte the argv built (`launch` spawns with `shell: false` and no
     `windowsVerbatimArguments`, `processLauncher.ts:149-154`).
  2. **The child (C3).** Through `capture` (`versionProbe.ts:92`), WIDENED by one options object `{ cwd, maxStdoutBytes }`
     with `shell: false` — never more positional parameters, never a copy. The cap counts BYTES (`Buffer.byteLength` of each
     chunk) and kills the tree when passed (a new `CaptureEnd` value, for example `'capped'`); the result carries the stderr
     tail (`ProcessHandle.stderrTail`, `processLauncher.ts:110`). Every existing caller of `capture` keeps its contract and
     its tests. The script goes on stdin (`input`). Limits: kill at 15 s, stdout at most 8 MiB (`RemoteLimits` lets tests
     make them small). ssh exit 255 → `refused` with the stderr tail's last line; a timeout → *"“<host>” did not answer
     over ssh within 15 s."*; capped → *"“<host>” sent more than 8 MiB, so the read was stopped."*; not started → *"ssh
     could not be started: <reason>"*.
  3. **The log (C3).** ONE line per call that did not answer 0, from `runRemote` only: host, the session label, the exit code
     or the end, the stderr tail — never stdout, never an operand.
  4. **The framing.** A `COAI-BEGIN` sentinel first, so a login banner or a `motd` before it is ignored; a banner that
     CONTAINS the sentinel; no sentinel at all (unreadable, not absent). Sections the parser can tell apart whatever bytes
     the transcript holds: the transcript goes LAST, after a header that gives its byte count and whether it was cut — a
     transcript line that holds `COAI-BEGIN` or a section marker must not end it. UTF-8: a tail cut inside a character.
  5. **The panes — parent change 1.1, point 2.** A tmux session with SEVERAL PANES (in one window or in several windows) or
     with SEVERAL `claude` processes is ambiguity: `ambiguous` with every candidate the script could name (its pane, its
     `claude` pid, its registry `sessionId`, `name` and `cwd` when they could be read) — a list for S3's QuickPick — or with
     NO candidates when none can be named. **Never a pick**, not even "the active pane". So step 1 cannot be
     `display-message` alone, which answers the ACTIVE pane only and hides the others: the script lists every pane of the
     session (for example `tmux list-panes -s -t "=$1" -F '#{pane_pid}'`), `=` forcing the exact session. One pane with one
     `claude` is the only shape that goes on to step 3 by itself. A session that does not exist is `absent`.
  6. **The process walk.** `ps -o pid=,comm= --ppid …`, walked to every descendant, bounded (depth and count), digits-only
     pids. **A branch counts its TOP-MOST `claude` match only**: a `claude` with a `claude` or `node` child below it is ONE
     candidate, not two. **Not measured:** whether the Claude CLI's `comm` on the VMs is `claude` or `node` (R7 measures
     it); the spec says which it accepts, and a wrong guess must show as the fallback or a refusal, never as another process.
  7. **The registry file.** `"$HOME/.claude/sessions/<pid>.json"`, at most 64 KiB (more → unreadable, never a cut JSON
     parsed); `sessionId` must match `SESSION_ID` (in `sh` before it is used in a path, and again in the extension); `cwd`
     and `name` are text. A pid that recycled, or a file left by an old process (`CallerIdentity.cs:67`'s warning): the spec
     says what the script checks and what is `unreadable`.
  8. **The transcript, by its id.** `"$HOME"/.claude/projects/*/<sessionId>.jsonl`: exactly ONE match, else zero →
     `absent`, two → `ambiguous`; `tail -c` of at most 4 MiB; the first partial line dropped and `cut` set; the extension
     checks that the file's folder is one of `projectDirNames(cwd)` for the registry's `cwd` and refuses otherwise (no copy of
     the folder rule in `sh`). An EMPTY glob (no folder, no match) must not make a command read stdin: the script is already
     past its own stdin (G4 item 6), and a test proves it.
  9. **A picked id (with S3).** A press with a picked id sends it as an operand (C2a). The script still tries the registry
     path first: one pane with one `claude` and a readable registry → that answer (S3 drops the id); an `ambiguous` registry
     path whose candidates include the picked id → that id's tail; the registry path FAILS (no live `claude`, no registry
     file) → that id's tail by id, as in area 8, instead of the fallback listing. That id's file not there → `gone`, never
     `absent` and never a fallback in the same call.
  10. **The fallback, at most three calls.** (i) no live `claude` or no registry file, and no picked id → the pane's
      `#{pane_current_path}` and the folder names in `~/.claude/projects` (a NUL-separated list); (ii) the title rows of the
      candidate folders only (`projectDirNames` computed locally), newest first, inside `SCAN_BUDGET`, with `grep -F -e
      '"custom-title"' -e '"ai-title"'`; a title-row line is BOUNDED, because a prompt that quotes the word
      `"custom-title"` is a long line that must not fill the cap; (iii) the chosen file's tail, by id, as in area 8. The rows
      are parsed locally by `titleFrom`, `customTitleFrom` and `collectNames`, and chosen by `namedAmong` with the tmux
      session name as the name looked for (the creds command types `/rename <session>`). Nothing is matched in `sh`.
  11. **The operands (C2a), and the script's own quoting.** Every operand is `"$1"`-style quoted in the script; the script
      uses `--` before an operand in every command that takes options (a folder name derived from `/home/x` is `-home-x`,
      which STARTS with `-`); no `eval`, no unquoted expansion, no `cd` into data. Read-only: no command that writes, creates
      or deletes, on any path. The spec gives the regex of each operand kind (no space, quote or shell character — G0).
  12. **The proof runs the text that is sent.** The script is run by `sh -s` on Linux CI against a temporary `HOME` with a
      fake `tmux` and a fake `ps` on `PATH` (the parent's test plan, *Stage 2*). The same constant is what `runRemote` sends;
      a test fails if it sends any other text. A host without `sh` (Windows) skips with that reason; CI (ubuntu) must not
      skip it, and a companion assertion says so.
- **Scenarios the spec must contain at least** (it may refine, split or add; it keeps these ids or maps them in its table).

| # | Name | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S2-T1 | the argv is the parent's, in its order (`remoteSsh.test.ts`) | `sshArgv('ai1-dev2', ['small-tasks'])` deep-equals the parent's list; `-n` is absent; `--` right before the host; `sh`, `-s`, `--` after it | add `-n` → `the remote sh -s would read an empty stdin` |
| S2-T2 | every forwarding is off | `ClearAllForwardings=yes`, `ForwardAgent=no`, `ForwardX11=no`, `RemoteCommand=none`, `RequestTTY=no`, `BatchMode=yes` are there | remove `ClearAllForwardings=yes` → `the owner's LocalForward would be opened a second time` |
| S2-T3 | a bad host or operand throws before any spawn | host `-oProxyCommand=x`, `h;id`, `h\n`, `h\0`; an operand with `'`, a space, LF, NUL, or failing its C2a rule → throws; the fake launcher saw no call | drop the NUL check → red naming NUL |
| S2-T4 | operands reach the remote shell as ONE literal word each (a real `sh -c`) | for each valid operand, and for 20 hostile strings fed to the quoting function directly: `sh -c "printf '%s\n' <the quoted words>"` prints exactly the operand, one line each | quote with double quotes → `$(…)` runs: red naming the string |
| S2-T5 | on win32 the argv crosses unchanged (G0) | for every valid operand and host, each argv word holds no space, tab or `"` (so node's win32 quoting leaves it as it is) and equals the built word byte for byte; the only quote characters are the parent's single quotes for the remote shell; the call to `capture` passes `shell: false` | allow a space in a session operand → `this operand would be re-quoted on its way to ssh.exe` |
| S2-T6 | the script travels on stdin and nothing else does | a fake `capture` records `input === REMOTE_SCRIPT`; no argv word holds the script or a newline | pass the script as an argv word → red |
| S2-T7 | a slow host is killed at the limit | a fake child that never closes, `limits.ms` 50 → `refused` with the timeout sentence; `kill` called once | stop waiting without `kill` → `a timed-out ssh was left running` |
| S2-T8 | the cap counts bytes | a fake child writing a multi-byte text whose `.length` is under the cap and whose `Buffer.byteLength` is over it → `refused` with the capped sentence; `kill` called; stdout NOT in the result | count `.length` → `8 MiB of characters is not 8 MiB` |
| S2-T9 | ssh's own failure is not "no session" | exit 255 with stderr `Permission denied (publickey).` → `refused`, its refusal ends with that line | map 255 to `answered` → `a refused key read as an empty session` |
| S2-T10 | no stdout and no operand in any log (a structural scan of `remoteSsh.ts`, `remoteRead.ts`, `remoteSessionHost.ts`, the door catches in `extension.ts` and the Asked hook in `chatHooks.ts`) | no `console.*` or `notify(` argument names `stdout`, `run.stdout`, `lines`, `tail`, `operands`, a value derived from them, or a caught error's `message`/`asText(reason)` in a terminal door's catch; companion: the one log call with `stderrTail` and the session label IS found | log `run.stdout` → red naming the file and line; put `asText(reason)` back in a terminal door's catch → red |
| S2-T11 | `capture` is widened, not changed | the existing `versionProbe` and `roundsDbRead` tests green with no edit; a call with the options object passes `cwd` and `shell: false` to `launch` (asserted both) and stops at `maxStdoutBytes`; a call without it behaves as before | a positional parameter added → red naming the signature |
| S2-T12 | an empty glob does not eat the script (real `sh -s`) | a temporary `HOME` with no `~/.claude/projects` at all and a fake tool that would read stdin → the script still prints its sentinel and its closing marker, and the result is `absent` or `fallback`, never `unreadable` for a cut script | call the function without `</dev/null` → `the rest of the script was read as data` |
| S2-T13 | a `claude` with a child is one candidate (real `sh -s`, fake `ps`) | one pane, a `claude` pid with a `node` child and a `claude` grandchild → ONE candidate, the top-most pid; two sibling `claude` processes → `ambiguous` with two | count every match → `one Claude CLI was read as two sessions` |
| S2-T14 | several panes are ambiguity, never a pick (change 1.1 point 2) | two panes in one window; two windows; one pane with two `claude` → `ambiguous` with every candidate; a two-pane session where no pane runs `claude` → `ambiguous` with none or `fallback`, as the spec decides, never `found` | read only the active pane → `a two-pane session was read without a choice` |
| S2-T15 | nothing is left behind (real `sh -s`) | a fake data command that would wait for stdin returns at once (its stdin is `/dev/null`); the `sh -s` process and its children have ended when its stdin closes | let a data command inherit stdin → the test's own timeout, naming the command |

  Also, in the spec's own table: the framing (banner, sentinel inside a banner, no sentinel, a transcript line with the
  marker, a tail cut inside a UTF-8 character), the registry file (over 64 KiB, a bad `sessionId`, a recycled pid), the
  transcript by id (zero, two, a folder not in `projectDirNames(cwd)`), the picked id (area 9: each of its four cases, and
  `gone`), the fallback's three calls, and the bounded title row.
- **Docs.** `research/module_extension.md` (E2 section): the ssh edge — the argv and why each option, the executable and the
  folder as the judgement decided, the Windows argv, the limits, the log rule; the script and its result. `research/
  architecture.md`: the new OUTWARD edge — the extension host → the local `ssh` → a VM the person agreed to, read-only (the
  edges section and the cross-cutting security part). `research/module_tests.md`: S2's tests and **not covered**: the real
  ssh against a real host and the real `comm` (R6, R7).
- **Done when.** Gate G2 + G4 written in the spec BEFORE the code, and any added option recorded and approved as 2.1; the
  spec reviewed by Opus and § 4 updated (gate G6); the spec's tests and break-its, S2-T1…T15 among them; `npm run compile`
  exit 0; the whole suite green on CI with the `sh -s` and `sh -c` tests RUN (not skipped); lint suppressions unchanged.
- **Size (Opus's estimate, the spec decides).** About 1000 lines (≈ 340 script, argv, runner and parser, ≈ 660 tests and
  fakes).

### E2.S3 — One road to the VM: the consent, the registry path, the fallback, the pick list

`model: opus` (spec, this section) · coder: sonnet · review: opus · areas (b) and (e), the host side.

- **Goal.** `remoteRoad` (C4) is the one road from a terminal to its remote session. It asks the person once per host; it
  never spawns ssh for a host without a "yes"; it takes the registry path, and the fallback only when the script says so;
  it shows a list whenever more than one session is possible and nothing is chosen by itself; it keeps a picked session id
  for that terminal only; it decides once what a cut tail means; and a double press starts one child and one question.
- **Files and functions (what and how).**
  1. NEW `src/remoteRead.ts` (pure; every effect is a seam, so the whole road is unit-tested):
     - **The consent.** `HostAnswer = { host: string; answer: 'yes' | 'no'; askedUtc: string }`;
       `consentOf(list: unknown, host): 'yes' | 'no' | 'ask'` (a list that is not an array of valid rows reads as empty —
       ask again, never "yes"); `withAnswer(list, host, answer, now): readonly HostAnswer[]` (a new array; the host's old row
       replaced; at 100 rows the OLDEST `askedUtc` is dropped, so that host is simply asked again); host compared exactly.
     - **The identity.** `nextStep(read: RemoteRead, picked: string | undefined): Step`, where a `Step` is `done` (a
       `found`), `fallback-titles` (call ii, with the folders = `projectDirNames(paneCwd)` ∩ the listed folders),
       `fallback-tail` (call iii, with the id), `pick` (a QuickPick of candidates, newest first) or `refused` (a sentence).
       Never more than three calls in one press.
     - **The meaning of a cut** — `answerFrom(found, purpose)`, the ONE place it is decided (S4 does not decide it again):
       for `'question'`, a cut tail is handed over as it is and S4's `waitingSaid` path turns "nothing asked" into the
       truncated sentence only through this function's answer (`answerFrom` returns `refused` with *"No question was found
       in the last 4 MiB of the session on “<host>” that were read; an older one may be beyond the cut."* when the cut tail
       holds no `AskUserQuestion`, and `lines` otherwise); for `'prompts'`, `lines` with `note` *"Only the last 4 MiB of
       this session were read, so its first prompts are not shown."* when cut, `''` when not.
     - **The picked id** (G5, Opus 9). After a pick, the next press of that terminal sends the id; its tail is read (S2
       area 9). The id is dropped ONLY on: a registry success (S2 area 9's first case), `gone` (its file is not there), or
       the terminal's close. It is NEVER dropped on an ssh refusal, a timeout, a cap, an unreadable answer or a cancel.
       On `gone`: the press answers its own sentence — *"The session you chose for this terminal (<name or id>) is no longer
       on “<host>”. Press again to look for it."* — and drops the id; the NEXT press runs discovery again from the start,
       inside the three-call bound.
     - **The road.** `remoteRoad(seams, terminalName, purpose, picked)`: `credsSession` (a `malformed` name → its refusal;
       `other` → *"This is not a creds terminal, so it has no Claude session to read."*) → `consentOf` (`ask` → the prompt
       seam; Escape writes NOTHING and refuses this press) → `runRemote` (S2), under the progress seam → `readRemote` (S2)
       → `nextStep`, repeated within the bound → `answerFrom`.
     - **The words** of a refused press that are this story's: the host answered "no" — *"Reading the Claude session on
       “<host>” is turned off. Only the text you copied can be sent; turn it on in **CoAI: hosts read over ssh**."*; a pick
       cancelled — *"No session was chosen, so nothing was read."*; a second press while a read runs — *"The Claude session
       of this terminal is still being read — the second press was ignored."*.
  2. NEW `src/remoteSessionHost.ts` (host; its row in `sonarcloud.yml`) — the seams only:
     - `readRemoteSession(terminal, purpose)`: the LIVE `terminal.name`; the answers in `context.globalState` key
       `coai.terminalHosts`; the prompt — a modal `showWarningMessage` *"Read the Claude session on “<host>” over ssh?"* with
       **Read** and **Not this host**; the progress line — `withProgress` (`ProgressLocation.Window`, *"Reading the Claude
       session on “<host>”…"*); the pick list — `askPerson(() => showQuickPick(…))` (`personWait.ts:35`): one row per
       candidate (its name, its `cwd`, the pane or the file's time), newest first, `canPickMany: false`, nothing
       preselected; ONE candidate is still a list when it came from an ambiguity.
     - **One read per terminal at a time** (Opus 10): a `WeakMap<vscode.Terminal, latch>` of `oneAtATime` latches
       (`oneAtATime.ts:32`), refusing with the sentence above — so a double press starts ONE ssh child and ONE consent modal.
     - **The picked id:** `const picked = new WeakMap<vscode.Terminal, string>()`, read and written only as `remoteRoad`
       says; deleted on `onDidCloseTerminal` too.
     - **The reach of a conversation** (for *Asked*, S4): `terminalOf(thread, panels)` — the live keyed terminal when it is
       still in `window.terminals` and has not exited; else `oneLiveNamed` (`terminalSource.ts:87`) over the live terminals
       with the RECORDED name: one → that terminal; none → *"The terminal “<name>” is not open — open it to read its Claude
       session."*; several → *"<N> terminals are called “<name>” — open the one you mean and press again."*. The host is
       always taken from a LIVE terminal's name (E1 § 11, carried note), so a restored E1 conversation reads its session
       when its terminal is open (change 1.1 point 1), and says why when it is not. The decision is pure (`reachOf(live,
       keyed, recordedName)` in `remoteRead.ts`); the host function only reads `window.terminals`.
     - **Changing an answer** (*"until the person changes it from the panel"*): a command `coai.terminalHosts`, title
       **CoAI: hosts read over ssh**, offered in the ConnectOtherAIs panel's title bar overflow (`view/title`, `when: view ==
       coai.panel`, a non-`navigation` group) and in the palette. It lists every remembered host with its answer; choosing
       one asks *"Read the Claude session on “<host>” over ssh?"* again and writes the new answer, or **Forget** removes the
       row. No webview page changes.
  3. `package.json` — the `coai.terminalHosts` command and its `view/title` row. It opens no chat, so it records no door.
- **Scenarios** (all over `remoteRead.ts` with fake seams, unless named).

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S3-T1 | an answer is kept per host, and a bad list is not "yes" (`remoteRead.test.ts`, new) | `consentOf([], 'h')` `ask`; after `withAnswer(…'h','no')` → `no`; `'H'` is still `ask`; `consentOf('junk')`, `consentOf([{host:'h'}])` → `ask` | read a malformed row as `yes` → `a damaged answer list let ssh run` |
| S3-T2 | the list is capped at 100, oldest out | 100 rows, one more host → 100 rows; the oldest `askedUtc` is gone; the new one is there; the input array is unchanged | push without the cap → 101 |
| S3-T3 | no ssh before a "yes" | `ask` + **Not this host** → `no` written, the refusal sentence, 0 runs; `ask` + Escape → nothing written, 0 runs; `no` → 0 runs and no prompt; `ask` + **Read** → `yes` written, then 1 run | run before the prompt → `ssh was spawned for a host nobody agreed to` |
| S3-T4 | the registry path is ONE call | a `found` on the first call → `lines`, exactly 1 run | always run the fallback → 3 runs |
| S3-T5 | the fallback is at most three calls and picks only one exact title | `fallback` → titles call with the folders ∩; one title row naming the session → tail call → `lines`; 3 runs; two rows of that name → `pick`, no third call before the choice | choose the first of two → `two sessions of one name and one was taken` |
| S3-T6 | an ambiguity is a list, even of one (change 1.1 point 2, host side) | an `ambiguous` read with two candidates → `pick` with both rows, none preselected; with one candidate → still `pick`; with none → a refusal naming host and session; cancel → the cancel sentence, 0 more runs | take the only candidate without asking → `a two-pane session was read without a choice` |
| S3-T7 | a picked id is sent, and kept or dropped by the parent's rule only | after a pick, the next press sends that id; a registry success drops it; a `gone` drops it; a refusal, a timeout, a cap, an `unreadable` and a cancel each KEEP it | drop it on a timeout → `a slow host made the person choose again` |
| S3-T8 | a picked file gone between the pick and the next press (G5) | press 1 picks id A; press 2 → the script answers `gone` → its own sentence naming A and the host, A dropped, no fallback call in that press; press 3 → discovery from the start (registry path, then the fallback), within 3 runs | treat `gone` as `absent` → `a vanished pick was reported as no session`; keep A → `press 3 asked for a file that is gone` |
| S3-T9 | the reach takes the host from a LIVE terminal | `reachOf`: a restored thread (key `{}`) with source `Creds: h / s` and one live terminal of that name → host `h`; none → the "not open" sentence, 0 runs; two → the "N terminals" sentence, 0 runs; a renamed live keyed terminal → its new name is used | read the host from the recorded name → `a closed terminal's VM was read` |
| S3-T10 | a cut is decided once, by the purpose | `answerFrom(found{cut:true, no question}, 'question')` → the truncated sentence; with the question in the tail → `lines`; `found{cut:false, no question}` → `lines` (S4 then says "asked nothing"); `'prompts'` with `cut:true` → `lines` with the cut note, `cut:false` → no note. Companion (source check): `waitingSaid.ts` and the doors never read `cut` | ignore `cut` → `a cut tail was reported as nothing asked` |
| S3-T11 | the host road says five different things (Opus 5) | `absent`, `unreadable`, `ambiguous` with no candidates, a truncated `'question'` answer and an ssh `refused` → five refusals, all different, each naming the host | map `unreadable` to absent's sentence → `an unreadable session was reported as no session` |
| S3-T12 | a double press starts one child and one question (Opus 10) | two presses on one terminal at once (`Promise.all`) under one latch → the fake prompt was shown ONCE, the fake `runRemote` ran ONCE, the second press answers the "still being read" sentence; two terminals at once → two runs | one latch for the whole window, or none → `a double press asked twice and spawned two ssh` |

- **Host scenario S3-H1** (new `src/test/host/terminalHostsScenario.ts`, registered in `scenarios.ts`): through the REAL
  `globalState`: write `no` for host `h`; with a terminal `Creds: h / s` active, `readRemoteSession` answers the "turned
  off" sentence and the injected run seam saw 0 calls; write `yes` through `withAnswer` and read the list back from a new
  `globalState.get` — the row is there. It runs only in CI (§ 8).
- **Docs.** `research/module_extension.md` (E2 section): the consent (asked once, kept, capped, changed from the panel), the
  identity order, the pick list, the picked id's life and `gone`, the meaning of a cut, one read per terminal, the reach.
  `research/module_tests.md`: S3's tests and S3-H1; **not covered**: the modal's wording on screen and the real QuickPick —
  R7.
- **Done when.** S3-T1…T12 green, each break-it seen red; S3-H1 green in CI's `extension · a real editor` job on the head
  commit, read from that job; `sonarExclusions.test.ts` green with the new host module listed; `notification-sites.json`
  regenerated; the whole suite and lint green.
- **Size.** About 600 lines (≈ 220 code, ≈ 380 tests).

### E2.S4 — The doors: take and add from a terminal, *Asked* through the reach, the words, the help

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (c), and the trigger.

- **Goal.** The person can use it: the two overflow items in a `Creds:` terminal take or add its waiting question, *Asked*
  shows the remote prompts, and the local and the remote paths say the same words for the same situation.
- **Files and functions (what and how).**
  1. **First, as its own commit inside the story:** NEW pure `src/waitingSaid.ts` with `waitingSaid(found)` (C5) — the body
     of `questionWaitingHere` from `chatCommand.ts:562` to `:591`, moved word for word; `questionWaitingHere` (`:550`)
     becomes the walk under its progress line plus one call. Nothing else changes in that commit, so its diff is a move.
  2. `src/chatCommand.ts` — `takeFromTerminal(panels, extensionUri, terminal, append = false)`: `readyForChat()` →
     `terminalDoorName(terminal)` (E1's guard, `:302`) → `readRemoteSession(terminal, 'question')` (S3); a refusal → a
     warning with code `no-question-from-terminal` and the refusal as its title; else `lastAsked(lines)` → `waitingIn` over
     the one session (`looking` empty: the identity is already exact) → `waitingSaid`. The cut was already decided by
     S3's `answerFrom`; this function does not read `cut`. → `deliverPassage(panels, extensionUri, ready,
     terminalMatch(panels.known(), terminal, name), { text }, false, true, sourceOfTerminal(name), append)`. NEVER sent by
     itself, whatever `autoSend` says (as `takeTheQuestion`, `:648-651`). It never calls `matchedSource`, `snapshots`,
     `sourceSession`, `questionWaitingHere` or `waitingQuestion` (the local home). `addFromTerminal` is the same with
     `append` true.
  3. `src/chatHooks.ts` — `onShowAsked` (`:361`): for a thread whose source is `terminal`, the answer comes from
     `terminalOf` + `readRemoteSession(…, 'prompts')` (S3), its lines through `humanSaid` (earliest of the tail first, at
     most `MOST_PROMPTS`, each cut at `MOST_PER_PROMPT` as `promptsOf` does — one shared function, not a copy), and the
     answer's `note` goes in the message's `refusal` beside the prompts. Every other source keeps `:395-397` exactly.
     `mine.sessionFile` stays `''` for a terminal (it is a LOCAL path). Its catch logs a fixed sentence.
  4. `src/chatPage.ts` — the Asked note: when the message carries prompts AND a `refusal`, the box shows the prompts and the
     refusal as a line above them (today `:1675` shows the refusal only when there are no prompts). The local path never
     sends both, so its page is unchanged. Tested by running the page (`bundledPage.test.ts`).
  5. `package.json` — commands `coai.takeFromTerminal` (**CoAI: take the question**) and `coai.addFromTerminal` (**CoAI:
     add the question**), hidden from the palette (`when: false`, beside `:1239-1242`); two `view/title` rows `when: view ==
     terminal && coai.terminalReadsSession`, group `coai@1` and `coai@2` (not `navigation`, so they sit in the overflow).
  6. `src/extension.ts` — the two registrations after `coai.addTheQuestion` (`:731`), each `noteChatDoor('take')` /
     `noteChatDoor('add')` FIRST (they take a question, so *Asked* on the spending page counts them). **Their catch logs a
     FIXED sentence, without the error's message** (Opus 3: a V8 `JSON.parse` error quotes its input, and the input is the
     transcript): `console.error('coai.takeFromTerminal failed')` and a `notify` failure with code
     `question-not-taken-from-terminal` / `question-not-added-from-terminal`, a fixed title and NO `detail: asText(reason)`.
     The context key `coai.terminalReadsSession` = the active terminal's name starts with `Creds: ` and its host is not
     answered `no` (a `malformed` name is TRUE, so its press gives the sentence that names the failed part — parent §
     *Which terminals*); set at activation, on `onDidChangeActiveTerminal`, and after `coai.terminalHosts` writes. The
     decision is pure (`readsKey(name, answers)` in `remoteRead.ts`).
  7. `src/test/chatWiring.test.ts` — `doorCommands()` (`:480`) also reads the `view/title` rows whose `when` names `view ==
     terminal`; without it the census (`:577-585`) goes red when the new handlers record their doors.
  8. Help, five languages (`helpContent.ts:294` and the four translations, the bold labels and the italic sentences in
     English): one paragraph — in a `Creds: <host> / <session>` terminal, the overflow menu of the terminal panel has
     **CoAI: take the question** and **CoAI: add the question**; they read the Claude session on that machine over ssh,
     read-only, with your own ssh settings and keys; the first time for each host you are asked, and the answer is kept;
     **CoAI: hosts read over ssh** in the ConnectOtherAIs panel changes it; **Asked** in that chat shows what you wrote
     there; when two sessions could be meant you choose from a list; the model still runs on this computer.
  9. `SECURITY.md` — a section *The extension's one outward ssh call* (the parent's § *Security* as fact, after it is
     built): read-only; only to a host the person agreed to; the person's own `~/.ssh/config`, keys and agent; `BatchMode`;
     no forwarding; argv only, validated operands with no space, quote or shell character, single-quoted for the remote
     shell, the script on stdin; 15 s, 8 MiB; nothing left running; the transcript in memory only, never logged or written
     (the extracted question and the prompts shown are saved with the conversation, like every passage); what S2's security
     judgement decided about the executable, its folder and multiplexing.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S4-T1 | the words moved, not changed (`waitingSaid.test.ts`, new — written in the move commit) | `waitingSaid` gives, for each case of `chatCommand.ts:562-591` (several waiting, one waiting, elsewhere, answered, failed, nothing asked), the same text or sentence as today, byte for byte; the body of `questionWaitingHere` calls it | change one sentence → red naming it |
| S4-T2 | the terminal door never reads the local home or the tabs (`terminalDoorWiring.test.ts`) | the bodies of `takeFromTerminal`/`addFromTerminal` hold none of `matchedSource(`, `snapshots(`, `sourceSession(`, `questionWaitingHere(`, `waitingQuestion(`, `os.homedir(`; companion: `takeTheQuestion`'s body holds `matchedSource(` | call `questionWaitingHere` → red |
| S4-T3 | a question taken from a terminal is never sent by itself | the `deliverPassage` call in `takeFromTerminal` passes `false` for `send`, `true` for `fromSession`, `sourceOfTerminal(` for the origin | pass `sendsImmediately(…)` → `a remote question was sent without the person` |
| S4-T4 | a door turns S3's answer into the passage without deciding the cut again | over the pure part: `lines` with a waiting question → that question as text; `lines` with none (an uncut tail) → *"Claude Code has asked nothing in this folder yet."*; a `refused` (S3's truncated sentence among them) → that refusal, unchanged | read `cut` here → `the cut is decided in two places` (S3-T10's companion is red too) |
| S4-T5 | Asked for a terminal goes the remote road, and the others keep theirs (`chatHooks` wiring) | `onShowAsked` checks the source BEFORE `resolveAndPin(` and `promptsFrom(`; a terminal thread reaches `readRemoteSession(`; companion: `resolveAndPin(` and `promptsFrom(` are still there for the others | take the local road for a terminal → red (and the person sees `NO_LOCAL_SESSION`, S1-T9) |
| S4-T6 | the page shows prompts and the cut note together (`bundledPage.test.ts`, RUN) | post `asked` with two prompts and a refusal → the box shows the first prompt AND the note; with no refusal → no note line; with no prompts → the refusal alone, as today | keep `:1675`'s branch only → `the cut note is not shown beside the prompts` |
| S4-T7 | the two items sit in the terminal's overflow (`terminalDoorWiring.test.ts`) | two `view/title` rows, `when` exactly `view == terminal && coai.terminalReadsSession`, groups not `navigation`; two palette rows `when: false`; the titles | put them in `navigation` → red |
| S4-T8 | every terminal door records itself first (`chatWiring.test.ts`, with the widened `doorCommands`) | the census finds `coai.takeFromTerminal` and `coai.addFromTerminal` and their `noteChatDoor` comes first; companion: `coai.askFromTerminal` is still found | do not widen `doorCommands` → `the number of recorders and the number of doors … have drifted apart` |
| S4-T9 | the context key follows the name and the answer (`remoteRead.test.ts`) | `readsKey('Creds: h / s', ask)` true; `(…, no)` false; `('Creds: before x', …)` true; `('bash', …)` false | make a `malformed` name false → `a bad creds name hides the items that explain it` |
| S4-T10 | every language names the items (`helpCoverage.test.ts`) | each of the five articles, not a fallback, contains `**CoAI: hosts read over ssh**` and the remote paragraph; the help census passes for the three new titles | leave `helpDe.ts` as it is → `the de chat article never says the session is read over ssh` |

  The doors' catches are scanned by S2-T10 (no error message, no stdout).
- **Docs.** `research/module_extension.md` (E2 section): the doors, the context key, *Asked* through the reach, the page
  note, the door count, the fixed catch. `research/module_tests.md`: the E2 row completed; the doors row (`:300`) notes that
  take/add from a terminal count as `take`/`add`; **not covered**: the items' visibility in a real window and the real VM —
  R7. `research/architecture.md`: the `chat` module's new dependency on the remote reader (one line in the module map).
- **Done when.** S4-T1…T10 green, each break-it seen red; the door census and the help census green;
  `notification-sites.json` regenerated; `SECURITY.md` has the section; the whole suite and lint green.
- **Size.** About 540 lines (≈ 160 code and manifest, ≈ 70 help in five languages, ≈ 40 `SECURITY.md` and docs, ≈ 270
  tests).

## 6. Build order, and its gates

1. **S1** — spec here; Sonnet codes; Opus reviews; Sonnet fixes. Meanwhile Fable writes the S2 spec with its security part.
2. **Gate G2 + G4 — the security judgement, before any S2 code.** The S2 spec's security part (§ 5, E2.S2) decides the ssh
   executable and its folder, the host configuration it accepts, multiplexing (after checking Win32-OpenSSH), and that the
   remote script leaves nothing behind. If it adds an ssh option to the parent's list, the caller records plan change 2.1
   in the parent and gets it approved BEFORE S2 is coded. The coder never adds an option.
3. **Gate G6 — the spec review.** Opus reviews the S2 spec. Any change it makes to C2 or C3 goes into § 4 and into the briefs
   of S3 and S4 in the same commit, before S3 or S4 is coded.
4. **S2** — from the Fable spec; Sonnet codes; Opus reviews; Sonnet fixes.
5. **S3**, then **S4** — spec here (as updated by G6); Sonnet codes; Opus reviews; Sonnet fixes. S4 starts with its move
   commit (`waitingSaid.ts`).
6. One pull request for the epic. The code round of the review gate gets the epic's scope (this document's § 1, § 4 and
   § 11) as `planText`. Every CI check green, and the `extension · a real editor` job read by its own result (§ 8).
7. Merge. Build the pre-release VSIX from the merged head (`npm run package`). The owner installs it and does **R6 and R7
   on a VM** (parent ledger entry 1.1, and E2 *Done when*). Each result goes into the parent's R-table with its date, the
   VSIX version and its sha. A failed check is fixed before the tag.
8. Release the extension: the release-please pull request carries the `## Extension <version> — <date>` prose; the tag is
   pushed alone and its run observed. E3 does not start before that.

## 7. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*, *Stage 2*) | Story | Tests |
|---|---|---|
| Operand validation against hostile strings through a real `sh -c` | S1, S2 | S1-T3, S1-T4, S2-T3, S2-T4, S2-T5 |
| The parsers: banner before the sentinel, registry JSON, NUL lists, a cut tail, the title rows | S2 | the S2 spec |
| The script run by `sh -s` on Linux CI, temporary `HOME`, fake `tmux` and `ps` — the text sent is the text under test | S2 | the S2 spec; S2-T6, S2-T12, S2-T13, S2-T15 |
| Several panes or several `claude` descendants: a list or a refusal, never a pick (change 1.1 point 2) | S2, S3 | S2-T13, S2-T14; S3-T6 |
| The pick list: one, several, cancel | S3 | S3-T5, S3-T6 |
| The transcript found by id: zero or two refused; a folder not in `projectDirNames(cwd)` refused | S2 | the S2 spec (area 8) |
| The per-host consent: asked once, "no" remembered, no ssh before a "yes" | S3 | S3-T1…T3, S3-T12, S3-H1 |
| `fromSession` stays `false` for a non-`Creds:` terminal; `pinSession` never reads the local home for a `terminal` source | S1 | S1-T5, S1-T8, S1-T9 |
| An E1 `Creds:` conversation restored under E2 gains *Asked* (change 1.1 point 1) | S1, S3 | S1-T6, S1-T7, S3-T9 |
| Absent, ambiguous, unreadable, truncated: four sentences, each with a test; the ssh failure a fifth | S2, S3, S4 | the S2 spec; S3-T10, S3-T11; S4-T4 |
| The picked id: kept, dropped, and a file gone after the pick | S2, S3 | the S2 spec (area 9); S3-T7, S3-T8 |
| No log line carries stdout | S2 | S2-T10 |
| The 15 s kill and the 8 MiB cap | S2 | S2-T7, S2-T8 |

## 8. Where each proof runs

- **Unit and wiring tests**: `npm test`, here and in CI (`ci.yml:575`, required, `ubuntu-latest`). The `sh -s` and `sh -c`
  tests RUN there; on a host with no `sh` they skip with that reason, and CI must show them run (S2 area 12).
- **Host scenario** S3-H1: `npm run test:host` CANNOT run on this machine (no `libnss3`). It runs only in CI's job
  `extension · a real editor` (`ci.yml:593`, `xvfb-run -a npm run test:host` at `:627`). That job has `continue-on-error:
  true` (`:596`), so a red scenario does NOT turn the pull request red: read the job's own conclusion and log on the head
  commit and write the result (with the sha) in the pull request description. A red host scenario blocks the merge as if it
  were required.
- **Real window, on a VM** (parent R-table), on the pre-release VSIX of the merged head, BEFORE the tag:
  - **R6** — one ssh call from Windows: the time to the first answer (target under 4 s), with the owner's real `Host`
    blocks that carry `LocalForward`; the open terminal keeps working while the read runs. Recorded with it (the parent's
    R6 row is not edited): no `ssh` process is left on Windows after a read, and none after a read killed at the limit
    (and none of the script's commands on the VM).
  - **R7** — the registry path finds the right session on each of the three users (`ai1-dev2`, `ai1-dev3`, `ai1-root`);
    the fallback list works when two sessions share a name. Also seen in R7, and recorded with it: the two items appear in
    a `Creds:` terminal's overflow and not in a plain one; the consent question appears once per host; a two-pane tmux
    session gives a list; the `comm` of the Claude CLI process on the VMs (S2 area 6).

## 9. Growth and security

- **Growth** (the parent's § *Growth surfaces*, made concrete): the consent list `coai.terminalHosts` in `globalState`, at
  most 100 rows of about 80 bytes (under 10 KiB), the oldest answer dropped at the cap; the picked ids, a `WeakMap` keyed by
  the `Terminal`, one entry per open terminal at most, gone with the terminal; the per-terminal latches, a `WeakMap`, the
  same; one ssh child per press (one at a time per terminal), at most three per press on the fallback, each killed at 15 s,
  stdout at most 8 MiB in memory and dropped after the parse; nothing on disk, nothing left on the VM. Conversation records:
  unchanged (E1's store and its sweep).
- **Security.** One new outward edge: the extension host's local `ssh`, read-only, to a host the person agreed to, as the
  user their own `~/.ssh/config` logs in as. Its argv, its options, its executable and its folder are E2.S2's, under
  Fable's security judgement (gate G2 + G4). The name of a terminal chooses the host and is not proof — hence the consent.
  No secret is handled. The transcript stays in memory. `SECURITY.md` gets its section in S4.

## 10. What E2 does not do, and open points

Not in E2: re-attach after a reload, go-to from a terminal, the skip-list repair (E3); a session read for a terminal that
is not `Creds: <host> / <session>`; the AI running on the VM; agent mode for a terminal conversation; anything installed on
the VM; any change to CredsForDevs, `coai-mcp` or the Team server.

Judgements this document made inside the parent's text (no plan change, said so the caller can see them):

1. **Several panes (change 1.1 point 2).** The parent's script step 1 reads `display-message`, which answers the ACTIVE pane
   only. Ledger entry 1.1 says several panes are ambiguity, a list or a refusal; so S2 lists every pane of the session.
   This is read as the plan as written (entry 1.1 sent this point here).
2. **"Changes it from the panel."** Read as an overflow item of the ConnectOtherAIs panel's title bar,
   `coai.terminalHosts`, with a QuickPick: no webview page changes.
3. **The host of a restored conversation** comes from a LIVE terminal of the recorded name, never from the record itself
   (E1 § 11, carried note): a restored `Creds:` conversation gains *Asked*, and it reads when its terminal is open.
4. **The Asked note beside prompts** is a small page change, tested by running the page — the parent allows it (*"if one
   must be, it is tested by running it"*).
5. **The parent's single quotes and Windows.** The parent says every operand is validated and single-quoted. The operand
   alphabets hold no space, no quote and no shell character, so node passes each word to `ssh.exe` unchanged and the single
   quotes are seen only by the remote login shell (C2a, S2-T5).

Open, for the people named:

1. **Multiplexing and any added ssh option** (S2's security judgement, gate G2 + G4): an extra option is plan change 2.1,
   recorded by the caller and approved before S2 is coded; a second change after it is a full stop.
2. **The `comm` of the Claude CLI** on the VMs is not measured (S2 area 6, R7).
3. **The extension host on another machine.** In a Remote window the extension host is remote, and so is its `ssh`. The
   owner's windows are local Windows windows; not measured, not built for.

## 11. Definition of Done — epic E2

- [ ] S1–S4 coded to their specs (S2 to Fable's spec, with its security judgement written before its code and reviewed by
      Opus, gate G6); every scenario green; every break-it observed red, then green, and both observations in the summary.
- [ ] Change 1.1's two points hold, each with its test: an E1 `Creds:` conversation restored under E2 gains *Asked*
      (S1-T6, S1-T7, S3-T9); several panes or several `claude` descendants give a list or a refusal, never a pick (S2-T13,
      S2-T14, S3-T6).
- [ ] The terminal door still only READS the clipboard (change 1.2): E2 adds no copy command and no borrow, and E1's
      terminal-read tests (S1r-T1…T7, S1r-H1) are green with no edit.
- [ ] Absent, ambiguous, unreadable and truncated each give their own sentence, each with a test; an ssh failure gives its
      own (S3-T11); no log line carries stdout, an operand or a caught error's message (S2-T10).
- [ ] No ssh option beyond the parent's list, unless recorded and approved as plan change 2.1 before S2 was coded.
- [ ] `npm run compile` exit 0; `npm test` green on CI with the `sh -s` tests RUN; `npm run lint` green; no suppression count
      grew; `sonarExclusions.test.ts` green with `remoteSessionHost.ts` listed.
- [ ] `notification-sites.json` regenerated; the door census and the help census green.
- [ ] S3-H1 green in CI's `extension · a real editor` job on the head commit, read from that job, named in the pull request.
- [ ] `SECURITY.md` has the paragraph on the ssh edge; `research/module_extension.md` (the E2 section),
      `research/module_tests.md` (the E2 row) and `research/architecture.md` (the new outward edge) updated; the help in
      five languages.
- [ ] The pull request merged; **R6 and R7 measured on a VM with the pre-release VSIX of the merged head, before the tag**,
      and recorded in the parent's R-table — R6 with the record that no `ssh` process is left after a read and after a
      killed read.
- [ ] The extension release cut from `main`: its changelog prose in the release-please pull request, the tag pushed alone,
      its run observed.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
