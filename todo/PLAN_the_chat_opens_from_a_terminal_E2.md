# PLAN — E2 stories: the remote Claude session, read over ssh

> Status: **plan only, nothing implemented yet, 2026-11-11.** Epic E2 of
> [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (its `## Epics`, its § *Stage 2*, its
> § *Security* and its ledger entry 1.1). Written by Opus when the epic started, on `feat/chat-from-a-terminal-e2` from
> `main` `45b070a2`. A stories document keeps no change ledger: a change to this epic is an entry in the parent (epoch 2,
> 0 changes today; the next one is 2.1, and a second one is a full stop).
>
> Scope: `src_vs_code` only, plus `SECURITY.md`, `research/` and the help. Five stories. **Cornerstone: E2.S2** — the
> stdin script and its typed result, areas (d) and (e). Fable writes its scenarios and brief in
> `todo/PLAN_the_chat_opens_from_a_terminal_E2_S2_spec.md` (not written yet). E2.S3, the ssh edge, gets a Fable
> **security judgement** outside the quota (`subagent-models.md`, the security row). The parent's ledger line reads
> `Cornerstones: 2 of 2 — E1.S2, E2.S2`.

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

## 2. What exists — verified at `45b070a2`, 2026-11-11

All paths are under `src_vs_code/` unless they start with `research/` or are `SECURITY.md`. Every name below was opened at
this commit.

| Fact | Where |
|---|---|
| The terminal door (E1): the name guard, the capture, the match, the delivery with `fromSession` `false` and `sourceOfTerminal` | `src/chatCommand.ts:302` `terminalDoorName`, `:341` `askFromTerminal`, `:364-366` |
| `newConversation` decides the flag by the source, then pins in the background | `src/chatCommand.ts:65`, `:85` (`readsSession(state.source, state.fromSession)`), `:201` `pinSession(entry, state.title, fromSession)` |
| `deliverPassage` (the one delivery of every door; `append` for *add*) | `src/chatCommand.ts:377-497` |
| The waiting question in this window, and its sentence mapping (local only, `os.homedir()`) | `src/chatCommand.ts:550-592` `questionWaitingHere`; `:607` `takeTheQuestion` (never sent by itself, `:648-651`) |
| The source kind, its builders, the identity rule | `src/chatStore.ts:75-83` `ConversationSource`; `:101` `TERMINAL_NAME_MAX`; `:107` `isTerminalName`; `:110` `sourceOfTerminal` |
| The origin rule: `terminal` is `'not yet'`, so `fromSession` reads `false` on every road | `src/chatStore.ts:290-306` `OriginRule`, `ORIGIN`; `:325` `agreeOnOrigin`; `:336` `readsSession`; `:403` (used by `recordFrom`) |
| The pin: runs on `fromSession` and a title only, walks the LOCAL home | `src/chatSessionJoin.ts:185` `pinSession`; `:61` `findSessionIn` (`os.homedir()`, `:64`) |
| The Asked press with no file: by id, then by NAME in the local home | `src/chatSessionJoin.ts:75` `resolveAndPin`, `:105` |
| The Asked press: the file first, else `resolveAndPin` | `src/chatHooks.ts:361` `onShowAsked`, `:395-397` |
| The restore: the page and the thread take `saved.fromSession` (from `recordFrom`); the re-pin only for `none` | `src/chatConversationRestore.ts:93`, `:205`, `:265-267` |
| The page draws Asked only when `fromSession`; a refusal shows only when there are NO prompts | `src/chatPage.ts:1181`, `:1675`; the `asked` message `:2121-2134` |
| The pure line rules E2 reuses | `src/claudeQuestion.ts:110` `collectNames`, `:291` `lastAsked`, `:327` `humanSaid`, `:356` `titleFrom`, `:379` `customTitleFrom`, `:454` `askedAsText` |
| The pure session rules E2 reuses | `src/claudeSessions.ts:72` `projectDirNames`, `:333` `namedAmong`, `:400` `SCAN_BUDGET`, `:512` `waitingIn`, `:686` `MOST_PROMPTS`, `:687` `MOST_PER_PROMPT`; `Asked` `:690`, `Found` `:717` |
| The session-id shape (private today) and its reader | `src/chatSource.ts:68` `SESSION_ID`, `:56` `sessionIdOf` |
| The one spawn site; a child's working directory; the stderr tail (8000) | `src/processLauncher.ts:141` `launch`, `:125` `workingDirectory`, `:34` `STDERR_TAIL_MAX`, `:110` `stderrTail` |
| The capture over it: stdin, a timeout that kills the tree — no stdout cap, no stderr in the result | `src/versionProbe.ts:92` `capture`, `:69` `CaptureEnd`, `:72` `Captured`; `:196` `resolvedExecutable` |
| The registrations of the doors | `src/extension.ts:677` `coai.askFromTerminal`, `:708` `coai.takeTheQuestion`, `:731` `coai.addTheQuestion` |
| The manifest: commands, the terminal button, the palette, the chords, the skip list | `package.json:274-287`, `:1181-1185` (`view/title`), `:1238-1255` (`commandPalette`), `:1309-1330` (`keybindings`), `:1349-1352` |
| The door census reads `webview/context`, `editor/context` and keybindings — NOT `view/title` | `src/test/chatWiring.test.ts:480-488` `doorCommands`, `:555-585` |
| Tests that pin the stage-1 rule E2 changes | `src/test/chatStore.test.ts:529-550`; `src/test/chatSourceWiring.test.ts:305-311` |
| A person's answer, with the wait made visible | `src/personWait.ts:35` `askPerson` |
| A context key, set the way this extension sets one | `src/extension.ts:334` (`setContext`, `coai.hasQuestions`) |
| The help article and its four translations; the help census | `src/helpContent.ts:294`, `src/helpDe.ts:53`, `src/helpEs.ts:53`, `src/helpRu.ts:53`, `src/helpUk.ts:117`; `src/test/helpCoverage.test.ts` |
| The host modules list that `sonarExclusions.test.ts` holds exact | `.github/workflows/sonarcloud.yml:268` |
| The docs that describe E1 | `research/module_extension.md:13364` (*The chat opens from a terminal (E1)*); `research/module_tests.md:306`; `research/architecture.md:615` |
| `SECURITY.md` today states only `coai-mcp`'s path guarantees | `SECURITY.md:1-4` |

## 3. Conventions every story follows

- **Worktree.** `/home/jinx/git/wt/coai-chat-terminal-e2`, branch `feat/chat-from-a-terminal-e2`. Never the main checkout.
- **Models.** Spec (scenarios and brief): Opus for S1, S3, S4 and S5 (this document), **Fable for S2**; a **Fable security
  judgement** on S3 before S3 is coded. Code and tests: Sonnet. Review: Opus. Fixes: Sonnet.
- **RED-GREEN-RED.** Each scenario names its assertion and its break-it: the production line to remove, and the failure
  message the test must then show. For new code the first RED is the test written before the code; the break-it proves the
  test has teeth. Report both observations in the summary.
- **Every test file** starts with `import '../../scripts/inRunFolder.cjs';`.
- **Pure first.** Decisions live in pure modules (no `vscode`, also not through another module): `credsSession.ts`,
  `remoteScript.ts`, `remoteSsh.ts` (node only), `remoteRead.ts`. ONE new host module is allowed in E2,
  `src/remoteSessionHost.ts` (S4), and it gets its row in `sonar.coverage.exclusions` in `sonarcloud.yml` in the same commit
  (`sonarExclusions.test.ts` holds the list exact). Other host code goes into the host files that exist (`chatCommand.ts`,
  `chatHooks.ts`, `chatSessionJoin.ts`, `extension.ts`).
- **Reuse first.** No second copy of a rule: `projectDirNames`, `namedAmong`, `lastAsked`, `humanSaid`, `collectNames`,
  `waitingIn`, `SESSION_ID` and the sentence mapping are CALLED, never re-typed (in TypeScript or in `sh`). The ssh child
  goes through `launch` (the one spawn site), by WIDENING `capture`, never by a second launcher.
- **Lint.** A new function keeps `complexity` ≤ 4 and ≤ 50 lines; no suppression count grows (CI step *Lint suppressions
  only shrink*). Split, do not suppress.
- **A new `notify` call site** changes the census: `node scripts/count-notifications.mjs --write`, commit
  `notification-sites.json`.
- **A webview page change** (S5 only, the Asked note) is tested by RUNNING the page (`bundledPage.test.ts`), never by a
  source assertion (`.agents/PROJECT.md`).
- **No stdout in a log.** No `console.*`, `notify` detail or error message may carry the child's stdout or any text cut
  from it. Logs carry host, session, exit code and the stderr tail only (parent § *Limits*).

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

## 4. Shared contracts — fixed here so S1, S3, S4 and S5 can be briefed while Fable writes S2's spec

S2's spec may refine C2 and C3. If it changes a name or a signature below, it updates this section and the briefs that
name it in the same commit.

- **C1 — which terminal reads a session (S1 owns).** NEW pure `src/credsSession.ts` (no imports):
  `credsSession(name: string): CredsName` with
  `CredsName = { kind: 'session'; host: string; session: string } | { kind: 'other' } | { kind: 'malformed'; refusal: string }`.
  The pattern is the parent's, exactly: `^Creds: ([A-Za-z0-9][A-Za-z0-9._-]*) / ([A-Za-z0-9][A-Za-z0-9_-]*)$`. `other` =
  the name does not start with `Creds: ` (so `Creds run: x` and `Creds VPN: x` are `other`). `malformed` = it starts with
  `Creds: ` and fails; the refusal names the part that failed. `readsSession` (`chatStore.ts:336`) reads it.
- **C2 — the script and its result (S2 owns; Fable).** NEW pure `src/remoteScript.ts`: `REMOTE_SCRIPT: string` (the POSIX
  `sh` text sent on stdin — the text under test IS the text sent); the operands of each call (C2a); and
  `readRemote(stdout: string, ...): RemoteRead`, the parser. `RemoteRead` is one union with an arm per outcome — at least a
  `found` arm (session id, `cwd`, name, the tail's lines, whether the tail was cut), a `fallback` arm (the pane's
  `cwd` and the folder names, or the title rows), and arms for **absent**, **ambiguous** (with the candidates), **unreadable**
  and **truncated**. No arm is an optional field to interpret.
  - **C2a — the operands.** The registry path keeps the parent's form: `sh -s -- <session>`. The spec fixes the operands of
    a call that carries a picked session id, and of the fallback's calls (ii) (the candidate folder names) and (iii) (the
    id). Every operand has a validation rule that S3 enforces before the argv is built.
- **C3 — the ssh edge (S3 owns).** NEW `src/remoteSsh.ts` (node only, no `vscode`): `sshArgv(host: string, operands:
  readonly string[]): readonly string[]` (the parent's options, in the parent's order, the host after `--`) and
  `runRemote(host: string, operands: readonly string[], script: string, limits?: RemoteLimits): Promise<RemoteRun>`, with
  `RemoteRun = { kind: 'answered'; code: number; stdout: string } | { kind: 'refused'; refusal: string }` — `refused`
  covers a timeout at 15 s, stdout over 8 MiB, a child that did not start, an operand that failed validation, and an ssh
  exit 255. It never rejects. Its log line (host, session, exit code, the stderr tail) is written here and only here.
- **C4 — one road to the VM (S4 owns).** In `src/remoteRead.ts` (pure) and `src/remoteSessionHost.ts` (host):
  `readRemoteSession(terminal: vscode.Terminal, purpose: 'question' | 'prompts'): Promise<RemoteAnswer>` — the consent,
  then the registry path, the fallback, the pick list, the picked id. `RemoteAnswer` is
  `{ kind: 'lines'; lines: readonly string[]; cut: boolean } | { kind: 'refused'; refusal: string }`. Nothing else in the
  extension spawns ssh or reads `globalState` for the consent.
- **C5 — the words (S5 owns).** The `Waiting` → sentence mapping of `questionWaitingHere` (`chatCommand.ts:562-591`) moves,
  unchanged in its words, to one exported pure function `waitingSaid(found: readonly Waiting[]): { text: string; refusal:
  string }` in `src/claudeSessions.ts`. The local path and the remote path both call it.

## 5. Stories

Build order: S1, then S2, then S3, then S4, then S5, one coder at a time on the one branch. Fable may write S2's spec while
S1 is coded; Fable's security judgement on S3 is written before S3 is coded.

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
  2. `src/chatStore.ts` — the `terminal` row of `ORIGIN` (`:301-306`) becomes a new rule `'by name'` (*"either on disk; read
     as what the name says: a `Creds:` session name reads its session, any other name does not"*). `readsSession(source,
     fromSession)` (`:336`): for a `terminal` source → `credsSession(source.name).kind === 'session'`, whatever
     `fromSession` was written. `agreeOnOrigin` still accepts either value for a terminal (a record an E1 build wrote, and a
     record an E1 build reads after a rollback, both stay readable). Rewrite the comments at `:297`, `:322-323`, `:332-335`.
     `CONVERSATION_VERSION` does not move.
  3. `src/chatSessionJoin.ts` — `pinSession(entry, title, fromSession, source: ConversationSource)` (REQUIRED parameter, so
     the compiler lists both callers): it returns at once for a `terminal` source, before any walk. This is the one guard;
     the callers `chatCommand.ts:201` (`state.source`) and `chatConversationRestore.ts:266` (`saved.source`) only pass it.
     `resolveAndPin` (`:75`): for a `terminal` source it answers `{ kind: 'none', refusal: … }` without a walk — a second
     line of defence; S5 routes the Asked press of a terminal conversation to the remote reader before it gets here.
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
| S1-T8 | the pin never walks for a terminal (`chatSourceWiring.test.ts` or a unit test over a seam) | the body of `pinSession` returns on `source.kind === 'terminal'` BEFORE `findSessionIn(`; companion: a `claude`/`none` source still reaches `findSessionIn(` | move the guard after the walk → `a terminal conversation walked the local home` |
| S1-T9 | the Asked fallback never walks for a terminal | `resolveAndPin` for a thread with a terminal source returns `none` and its body checks the source before `findSessionIn(mine.title)` | remove the check → red |

  The existing tests in `chatStore.test.ts` that state E1's rule are REPLACED, not kept beside the new ones (two tests that
  disagree about one rule cannot both be green).
- **Docs.** `research/module_extension.md`, a new section *The chat reads the remote Claude session (E2)* after the E1
  section: one bullet — which names read a session, the three sentences, the derived flag and why (change 1.1), the two
  guards. `research/module_tests.md`: a new row *Chat with other AI — THE REMOTE SESSION (E2)* with S1's tests.
- **Done when.** S1-T1…T9 green, each break-it seen red; `npm run compile` exit 0; the whole suite and lint green.
- **Size.** About 260 lines (≈ 60 code, ≈ 200 tests).

### E2.S2 — The stdin script and its typed result (CORNERSTONE)

`model: fable` — the scenarios and the brief are Fable's, in `todo/PLAN_the_chat_opens_from_a_terminal_E2_S2_spec.md`.
Coder: sonnet · review: opus · areas (e) and the script half of (d).

**Why this story is the cornerstone.** It is the only place where wrong or cut data from another machine can arrive and
look correct: the script decides which pane, which process, which registry file and which transcript are read, and the
parser decides what their bytes mean. A wrong pick, a cut tail read as whole, or a banner read as data gives no error —
the person gets another session's question, or "nothing was asked". The parent recommends this story (*Recommendation for
the Opus story writer*, E2), and its split rule: *"If these become two stories, mark the script with its result type"* —
so the ssh edge is E2.S3, not a cornerstone, with its own security judgement.

- **Goal.** `REMOTE_SCRIPT` (POSIX `sh`, read on stdin by `sh -s`) does the five steps of the parent's § *What the script
  does*, read-only, and `readRemote` turns its stdout into a `RemoteRead` (C2) that never makes a guess and never mistakes a
  cut or a failure for an absence.
- **Scope.** NEW `src/remoteScript.ts` (pure: the script text, the operand rules of C2a, the parser, the result type);
  `src/chatSource.ts` (EXPORT `SESSION_ID`, `:68`, so the extension checks the UUID shape with the one rule); tests,
  including the script run by a real `sh -s`. Not in scope: the argv and the child (S3), the consent and the pick list
  (S4), the doors (S5).
- **Areas the spec must cover.**
  1. **The framing.** A `COAI-BEGIN` sentinel first, so a login banner or a `motd` before it is ignored; a banner that
     CONTAINS the sentinel; no sentinel at all (unreadable, not absent). Sections the parser can tell apart whatever bytes
     the transcript holds: the transcript goes LAST, after a header that gives its byte count and whether it was cut — a
     transcript line that holds `COAI-BEGIN` or a section marker must not end it. UTF-8: a tail cut inside a character.
  2. **The panes — parent change 1.1, point 2.** A tmux session with SEVERAL PANES (in one window or in several windows) or
     with SEVERAL `claude` descendants is ambiguity: the result is `ambiguous` with every candidate the script could name
     (its pane, its `claude` pid, its registry `sessionId`, `name` and `cwd` when they could be read) — a list for S4's
     QuickPick — or a refusal when no candidate can be named. **Never a pick**, not even "the active pane". So step 1 cannot
     be `display-message` alone, which answers the ACTIVE pane only and hides the others: the script lists every pane of the
     session (for example `tmux list-panes -s -t "=$1" -F '#{pane_pid}'`), `=` forcing the exact session. One pane with one
     `claude` is the only shape that goes on to step 3 by itself. A session that does not exist is `absent`, with its own
     sentence. Tests: one pane; two panes in one window; two windows; one pane with two `claude` processes; a pane whose
     shell runs no `claude` (the fallback, step 5); no such session.
  3. **The process walk.** `ps -o pid=,comm= --ppid …`, walked to every descendant, bounded (depth and count), digits-only
     pids. **Not measured:** whether the Claude CLI's `comm` on the VMs is `claude` or `node`; R7 measures it. The spec says
     which it accepts and how a wrong guess shows (it must show as the fallback or a refusal, never as another process).
  4. **The registry file.** `"$HOME/.claude/sessions/<pid>.json"`, at most 64 KiB (more → unreadable, never a cut JSON
     parsed); `sessionId` must match `SESSION_ID` (in `sh` before it is used in a path, and again in the extension); `cwd`
     and `name` are text. A pid that recycled, or a file left by an old process (`CallerIdentity.cs:67`'s warning): the spec
     says what the script checks and what is `unreadable`.
  5. **The transcript, by its id.** `"$HOME"/.claude/projects/*/<sessionId>.jsonl`: exactly ONE match, else zero →
     `absent`, two → `ambiguous`; `tail -c` of at most 4 MiB; the first partial line dropped and the cut reported; the
     extension checks that the file's folder is one of `projectDirNames(cwd)` for the registry's `cwd` and refuses otherwise
     (no copy of the folder rule in `sh`).
  6. **The fallback, at most three calls.** (i) no live `claude` or no registry file → the pane's `#{pane_current_path}` and
     the folder names in `~/.claude/projects` (a NUL-separated list); (ii) the title rows of the candidate folders only
     (`projectDirNames` computed locally), newest first, inside `SCAN_BUDGET`, with `grep -F -e '"custom-title"' -e
     '"ai-title"'`; a title-row line is BOUNDED, because a prompt that quotes the word `"custom-title"` is a long line that
     must not fill the 8 MiB cap; (iii) the chosen file's tail, by id, as in area 5. The rows are parsed locally by
     `titleFrom`, `customTitleFrom` and `collectNames`, and chosen by `namedAmong` with the tmux session name as the name
     looked for (the creds command types `/rename <session>`). Nothing is matched in `sh`.
  7. **The four results.** Absent, ambiguous, unreadable and truncated are four arms and four sentences (the spec writes
     them, naming host, session and, where it helps, the folder). **Truncated** is about MEANING: a cut tail that holds the
     waiting question is a good answer; a cut tail that holds NO question is `truncated` (*"…not in the last 4 MiB that were
     read"*), never "Claude Code has asked nothing". Each result has its test.
  8. **The operands (C2a), and the script's own quoting.** Every operand is `"$1"`-style quoted in the script; the script
     uses `--` before an operand in every command that takes options (a folder name derived from `/home/x` is `-home-x`, which
     STARTS with `-`); no `eval`, no unquoted expansion, no `cd` into data. Read-only: no command that writes, creates or
     deletes, on any path. The spec gives the regex of each operand kind.
  9. **The proof runs the text that is sent.** The script is run by `sh -s` on Linux CI against a temporary `HOME` with a
     fake `tmux` and a fake `ps` on `PATH` (the parent's test plan, *Stage 2*). The same constant is read by S3's runner;
     a test fails if the runner sends any other text. A host without `sh` (Windows) skips with that reason; the suite on CI
     (ubuntu) must not skip it, and a companion assertion says so.
  10. **Break-its.** At least: the active pane taken when there are two (an ambiguity read as a pick); the sentinel check
      removed (a banner parsed as data); the cut flag dropped (a cut tail with no question reads as "nothing asked"); the
      folder check removed (a transcript outside `projectDirNames(cwd)` accepted); `SESSION_ID` not checked in `sh`.
- **Done when.** The spec's tests and break-its; `npm run compile` exit 0; the whole suite green on CI with the `sh -s`
  tests RUN (not skipped); lint suppressions unchanged.
- **Size (Opus's estimate, the spec decides).** About 650 lines (≈ 220 script and parser, ≈ 430 tests and fakes).

### E2.S3 — The ssh edge: the argv, the options, the child and its limits

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (d). **A Fable security judgement first** (outside
the quota): which host, whose keys, no forwarding — authentication and authorisation (parent § *Epics*, the recommendation).

- **Goal.** One function builds the ssh argv and one function runs it, so the escaping, the options, the kill and the cap
  are written once. A failed call says why, in its own sentence, and never looks like "no session".
- **Files and functions (what and how).**
  1. NEW `src/remoteSsh.ts` (C3), node only.
     - `sshArgv(host, operands)` → `['-T', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=5', '-o', 'RequestTTY=no', '-o',
       'RemoteCommand=none', '-o', 'ClearAllForwardings=yes', '-o', 'ForwardAgent=no', '-o', 'ForwardX11=no', '-o',
       'LogLevel=ERROR', '--', host, 'sh', '-s', '--', ...quoted operands]`. **No `-n`** (it would give `sh -s` an empty
       stdin). Each operand is single-quoted for the REMOTE login shell (ssh joins the command words into one line and the
       login shell parses it). A NUL, a CR or an LF in the host or an operand THROWS; the host must pass C1's host pattern
       and each operand its C2a rule — re-checked here, at the one road in, not only at the door.
     - `runRemote(host, operands, script, limits)` — through `capture` (`versionProbe.ts:92`), WIDENED rather than copied:
       an optional stdout byte cap that kills the tree when passed (a new `CaptureEnd` value, for example `'capped'`), and
       the stderr tail on the result (`ProcessHandle.stderrTail`, `processLauncher.ts:110`). Every existing caller of
       `capture` keeps its contract and its tests. The script goes on stdin (`input`). Limits: kill at 15 s, stdout at most
       8 MiB (`RemoteLimits` exists so tests can make them small). ssh exit 255 (its own failure: refused key, unknown host,
       no route) → `refused` with the stderr tail's last line; a timeout → *"“<host>” did not answer over ssh within 15 s."*;
       capped → *"“<host>” sent more than 8 MiB, so the read was stopped."*; not started → *"ssh could not be started: <reason>"*.
     - The log: ONE `console.warn` (or the extension's log) per call that did not answer 0, carrying host, session, the exit
       code or the end, and the stderr tail — never stdout, never an operand other than host and session.
  2. The executable and the working directory: decided by the security judgement (below) and written here only.
- **The security judgement (Fable, outside the quota) must decide and write down.**
  1. **Which `ssh` runs.** A bare `ssh` on Windows is searched in the CURRENT DIRECTORY first by the spawn layer, then the
     PATH; the extension host's working directory is not ours to trust. Absolute path (`%SystemRoot%\System32\OpenSSH\
     ssh.exe` where the host is Windows, `resolvedExecutable` elsewhere) and an explicit `cwd`, or another answer — with the
     reason.
  2. **Which host.** The host comes from the LIVE name of the terminal at the time of the read (E1 § 11, carried note), and
     the consent (S4) is per host. Is the `Host` alias from the person's own `~/.ssh/config` acceptable as the only
     authority (it can carry `ProxyCommand`, `ProxyJump`, `IdentityFile`, `StrictHostKeyChecking`)? What `BatchMode=yes`
     does and does not stop (a password or passphrase prompt is a refusal; an unknown host key is a refusal unless the
     person's config says `accept-new`).
  3. **Whose keys.** The person's keys and agent, through their config; nothing handled by the extension; no secret in
     argv or a log.
  4. **No forwarding**, and whether the parent's options are ENOUGH: `ClearAllForwardings`, `ForwardAgent=no`,
     `ForwardX11=no`, `RemoteCommand=none`, `RequestTTY=no`. **Open point for the judgement:** a person's config with
     `ControlMaster`/`ControlPersist` can make this call reuse the open terminal's connection or leave a background master
     after the 15 s kill (*"nothing left behind"*, parent § *Growth surfaces*). Measure it or decide it; **an option added
     to the parent's list (for example `-o ControlPath=none`) is a contract change** — the judgement reports it to the
     caller as a candidate plan change 2.1, it is not added by the coder.
  5. **The remote side.** Each command the script runs is read-only; a login banner cannot inject (the sentinel); the remote
     login shell parses the command line, so only validated, single-quoted operands reach it.
  6. **The window between press and read** (rename, close): what is re-checked just before the spawn.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S3-T1 | the argv is the parent's, in its order (`remoteSsh.test.ts`, new) | `sshArgv('ai1-dev2', ['small-tasks'])` deep-equals the list above; `-n` is absent; `--` comes right before the host; `sh`, `-s`, `--` follow it | add `-n` → `the remote sh -s would read an empty stdin` |
| S3-T2 | every forwarding is off | the argv holds `ClearAllForwardings=yes`, `ForwardAgent=no`, `ForwardX11=no`, `RemoteCommand=none`, `RequestTTY=no`, `BatchMode=yes` | remove `ClearAllForwardings=yes` → `the owner's LocalForward would be opened a second time` |
| S3-T3 | a bad host or operand throws before any spawn | host `-oProxyCommand=x`, `h;id`, `h\n`, `h\0`; an operand with `'`, LF, NUL, or failing its C2a rule → throws; the fake launcher saw no call | drop the NUL check → red naming NUL |
| S3-T4 | operands reach the remote shell as ONE literal word each (a real `sh -c`) | for each valid operand and for 20 hostile strings that pass no validation BUT are fed to the quoting function directly: `sh -c "printf '%s\n' <the quoted words>"` prints exactly the operand, one line per operand | quote with double quotes → `$(…)` runs: red naming the string |
| S3-T5 | the script travels on stdin and nothing else does | a fake `capture` records `input === REMOTE_SCRIPT` and that no argv word holds the script or a newline | pass the script as an argv word → red |
| S3-T6 | a slow host is killed at the limit | a fake child that never closes, `limits.ms` 50 → `refused` with the timeout sentence; the fake's `kill` was called once | stop waiting without `kill` → `a timed-out ssh was left running` |
| S3-T7 | stdout past the cap is stopped | a fake child writing 2 × cap → `refused` with the capped sentence; `kill` called; stdout NOT in the result | keep reading past the cap → red |
| S3-T8 | ssh's own failure is not "no session" | exit 255 with stderr `Permission denied (publickey).` → `refused`, its refusal ends with that line | map 255 to `answered` → `a refused key read as an empty session` |
| S3-T9 | no stdout in any log (a structural scan of `remoteSsh.ts`, `remoteSessionHost.ts`, and the S5 call sites) | no `console.*` or `notify(` argument names `stdout`, `run.stdout`, `lines`, `tail` or a value derived from them; companion: the log call with `stderrTail` IS found | log `run.stdout` → red naming the file and line |
| S3-T10 | `capture`'s old callers keep their contract | the existing `versionProbe` and `roundsDbRead` tests green with no edit; a call without the new options behaves as before | — (the regression net for the widening) |

- **Docs.** `research/module_extension.md` (E2 section): the ssh edge — the argv and why each option, the executable and
  the cwd as the judgement decided, the limits, the log rule. `research/architecture.md`: the new OUTWARD edge — the
  extension host → the local `ssh` → a VM the person agreed to, read-only (the container/edges section and the cross-cutting
  security part). `research/module_tests.md`: S3's tests and **not covered**: the real ssh against a real host (R6).
- **Done when.** The judgement written into this section (or into S2's spec, linked from here) BEFORE the code; S3-T1…T10
  green, each break-it seen red; `npm run compile` exit 0; the whole suite and lint green.
- **Size.** About 380 lines (≈ 120 code, ≈ 260 tests).

### E2.S4 — One road to the VM: the consent, the registry path, the fallback, the pick list

`model: opus` (spec, this section) · coder: sonnet · review: opus · areas (b) and (e), the host side.

- **Goal.** `readRemoteSession` (C4) is the one road from a terminal to its remote session. It asks the person once per
  host; it never spawns ssh for a host without a "yes"; it takes the registry path, and the fallback only when the script
  says so; it shows a list whenever more than one session is possible and nothing is chosen by itself; and it keeps a
  picked session id for that terminal only.
- **Files and functions (what and how).**
  1. NEW `src/remoteRead.ts` (pure, the decisions):
     - **The consent.** `HostAnswer = { host: string; answer: 'yes' | 'no'; askedUtc: string }`;
       `consentOf(list: unknown, host): 'yes' | 'no' | 'ask'` (a list that is not an array of valid rows reads as empty —
       ask again, never "yes"); `withAnswer(list, host, answer, now): readonly HostAnswer[]` (a new array; the host's old row
       replaced; at 100 rows the OLDEST `askedUtc` is dropped, so that host is simply asked again); host compared exactly.
     - **The identity.** `nextStep(read: RemoteRead, picked: string | undefined): Step`, where a `Step` is
       `lines` (done), `fallback-titles` (call ii, with the folders = `projectDirNames(paneCwd)` ∩ the listed folders),
       `fallback-tail` (call iii, with the id), `pick` (a QuickPick of candidates, newest first) or `refused` (a sentence).
       It never has more than three calls in one press, and a `fallback` arm after a picked id was sent drops that id.
     - **The words** of a refused press that are this story's: the host answered "no" — *"Reading the Claude session on
       “<host>” is turned off. Only the text you copied can be sent; turn it on in **CoAI: hosts read over ssh**."*;
       a pick cancelled — *"No session was chosen, so nothing was read."*.
  2. NEW `src/remoteSessionHost.ts` (host; its row in `sonarcloud.yml`):
     - `readRemoteSession(terminal, purpose)`: the live `terminal.name` → `credsSession` (a `malformed` name → its refusal;
       `other` → a refusal that this is not a creds terminal) → the consent from `context.globalState` key
       `coai.terminalHosts` (`ask` → a modal `showWarningMessage` *"Read the Claude session on “<host>” over ssh?"* with
       **Read** and **Not this host**; Escape writes NOTHING and refuses this press) → `runRemote` (S3) →
       `readRemote` (S2) → `nextStep`, repeated within the three-call bound → `RemoteAnswer`.
     - **The pick list.** `askPerson(() => showQuickPick(…))` (`personWait.ts:35`): one row per candidate — its name, its
       `cwd`, the pane or the file's time — newest first, `canPickMany: false`, nothing preselected. ONE candidate is still a
       list when it came from an ambiguity (two panes of which one runs `claude` — the person sees the one row and chooses).
     - **The picked id.** `const picked = new WeakMap<vscode.Terminal, string>()`: set after a pick; sent as the id on the
       next press of that terminal; deleted when the registry path succeeds, when the script says its file is gone, and
       with the terminal (`onDidCloseTerminal`, and the `WeakMap` itself).
     - **The reach of a conversation** (for *Asked*, S5): `terminalOf(thread, panels)` — the live keyed terminal when it is
       still in `window.terminals` and has not exited; else `oneLiveNamed` (`terminalSource.ts:87`) over the live terminals
       with the RECORDED name: one → that terminal; none → *"The terminal “<name>” is not open — open it to read its Claude
       session."*; several → *"<N> terminals are called “<name>” — open the one you mean and press again."*. The host is
       always taken from a LIVE terminal's name (E1 § 11, carried note), so a restored E1 conversation reads its session
       when its terminal is open (change 1.1 point 1), and says why when it is not.
     - **Changing an answer** (*"until the person changes it from the panel"*): a command `coai.terminalHosts`, title
       **CoAI: hosts read over ssh**, offered in the ConnectOtherAIs panel's title bar overflow (`view/title`, `when: view ==
       coai.panel`, a non-`navigation` group) and in the palette. It lists every remembered host with its answer; choosing
       one asks *"Read the Claude session on “<host>” over ssh?"* again and writes the new answer, or **Forget** removes the
       row. No webview page changes.
  3. `package.json` — the `coai.terminalHosts` command and its `view/title` row. It opens no chat, so it records no door.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S4-T1 | an answer is kept per host, and a bad list is not "yes" (`remoteRead.test.ts`, new) | `consentOf([], 'h')` `ask`; after `withAnswer(…'h','no')` → `no`; `'H'` is still `ask`; `consentOf('junk')`, `consentOf([{host:'h'}])` → `ask` | read a malformed row as `yes` → `a damaged answer list let ssh run` |
| S4-T2 | the list is capped at 100, oldest out | 100 rows, one more host → 100 rows; the oldest `askedUtc` is gone; the new one is there; the input array is unchanged | push without the cap → 101 |
| S4-T3 | no ssh before a "yes" (`remoteSessionHost` over seams: a fake prompt, a fake `runRemote`) | `ask` + **Not this host** → `no` written, the refusal sentence, 0 runs; `ask` + Escape → nothing written, 0 runs; `no` → 0 runs and no prompt; `ask` + **Read** → `yes` written, then 1 run | run before the prompt → `ssh was spawned for a host nobody agreed to` |
| S4-T4 | the registry path is ONE call | a `found` read on the first call → `lines`, exactly 1 run | always run the fallback → 3 runs |
| S4-T5 | the fallback is at most three calls and picks only one exact title | `fallback` → titles call with the folders ∩; one title row naming the session → tail call → `lines`; 3 runs; two rows of that name → `pick`, no third call before the choice | choose the first of two → `two sessions of one name and one was taken` |
| S4-T6 | an ambiguity is a list, even of one (change 1.1 point 2, host side) | an `ambiguous` read with two panes → `pick` with both rows, none preselected; with one row from a two-pane ambiguity → still `pick`; cancel → the cancel sentence, 0 more runs | take the only candidate without asking → `a two-pane session was read without a choice` |
| S4-T7 | a picked id is used, then dropped when it should be | after a pick, the next press sends that id; a `found` by the registry path deletes it; a `fallback` or absent-file answer for that id deletes it | keep it after the registry path succeeds → red |
| S4-T8 | the reach takes the host from a LIVE terminal | a restored thread (key `{}`) with source `Creds: h / s` and one live terminal of that name → the run's host is `h`; no live one → the "not open" sentence, 0 runs; two → the "N terminals" sentence, 0 runs; a renamed live keyed terminal → its new name is used | read the host from the recorded name → `a closed terminal's VM was read` |

- **Host scenario S4-H1** (new `src/test/host/terminalHostsScenario.ts`, registered in `scenarios.ts`): through the REAL
  `globalState`: write `no` for host `h`; with a terminal `Creds: h / s` active, `readRemoteSession` answers the "turned
  off" sentence and the injected run seam saw 0 calls; write `yes` through `withAnswer` and read the list back from a new
  `globalState.get` — the row is there. It runs only in CI (§ 8).
- **Docs.** `research/module_extension.md` (E2 section): the consent (asked once, kept, capped, changed from the panel), the
  identity order, the pick list, the picked id's life, the reach. `research/module_tests.md`: S4's tests and S4-H1; **not
  covered**: the modal's wording on screen and the real QuickPick — R7.
- **Done when.** S4-T1…T8 green, each break-it seen red; S4-H1 green in CI's `extension · a real editor` job on the head
  commit, read from that job; `sonarExclusions.test.ts` green with the new host module listed; `notification-sites.json`
  regenerated; the whole suite and lint green.
- **Size.** About 520 lines (≈ 190 code, ≈ 330 tests).

### E2.S5 — The doors: take and add from a terminal, *Asked* through the reach, the words, the help

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (c), and the trigger.

- **Goal.** The person can use it: the two overflow items in a `Creds:` terminal take or add its waiting question, *Asked*
  shows the remote prompts, and the local and the remote paths say the same words for the same situation.
- **Files and functions (what and how).**
  1. `src/claudeSessions.ts` — `waitingSaid(found)` (C5): the body of `questionWaitingHere` from `:562` to `:591`, moved
     word for word. `questionWaitingHere` (`chatCommand.ts:550`) becomes the walk under its progress line plus one call.
  2. `src/chatCommand.ts` — `takeFromTerminal(panels, extensionUri, terminal, append = false)`: `readyForChat()` →
     `terminalDoorName(terminal)` (E1's guard, `:302`) → `readRemoteSession(terminal, 'question')` (S4); a refusal → a
     warning with code `no-question-from-terminal` and the refusal as its title; else `lastAsked(lines)` → `waitingIn` over
     the one session (`looking` empty: the identity is already exact) → `waitingSaid` — a cut answer with nothing waiting
     gives S2's truncated sentence, never the "asked nothing" one → `deliverPassage(panels, extensionUri, ready,
     terminalMatch(panels.known(), terminal, name), { text }, false, true, sourceOfTerminal(name), append)`. NEVER sent by
     itself, whatever `autoSend` says (as `takeTheQuestion`, `:648-651`). It never calls `matchedSource`, `snapshots`,
     `sourceSession`, `questionWaitingHere` or `waitingQuestion` (the local home). `addFromTerminal` is the same with
     `append` true.
  3. `src/chatHooks.ts` — `onShowAsked` (`:361`): for a thread whose source is `terminal`, the answer comes from
     `terminalOf` + `readRemoteSession(…, 'prompts')` (S4), its lines through `humanSaid` (earliest of the tail first, at
     most `MOST_PROMPTS`, each cut at `MOST_PER_PROMPT` as `promptsOf` does — one shared function, not a copy), and a cut
     tail adds its note. Every other source keeps `:395-397` exactly. `mine.sessionFile` stays `''` for a terminal (it is
     a LOCAL path).
  4. `src/chatPage.ts` — the Asked note: when the message carries prompts AND a `refusal`, the box shows the prompts and
     the refusal as a line above them (today `:1675` shows the refusal only when there are no prompts). The local path
     never sends both, so its page is unchanged. Tested by running the page (`bundledPage.test.ts`).
  5. `package.json` — commands `coai.takeFromTerminal` (**CoAI: take the question**) and `coai.addFromTerminal` (**CoAI:
     add the question**), hidden from the palette (`when: false`, beside `:1239-1242`); two `view/title` rows `when: view ==
     terminal && coai.terminalReadsSession`, group `coai@1` and `coai@2` (not `navigation`, so they sit in the overflow).
  6. `src/extension.ts` — the two registrations after `coai.addTheQuestion` (`:731`), each `noteChatDoor('take')` /
     `noteChatDoor('add')` FIRST (they take a question, so *Asked* on the spending page counts them), the catch shaped like
     `:708-729`. The context key `coai.terminalReadsSession` = the active terminal's name starts with `Creds: ` and its host
     is not answered `no` (a `malformed` name is TRUE, so its press gives the sentence that names the failed part — parent §
     *Which terminals*); set at activation, on `onDidChangeActiveTerminal`, and after `coai.terminalHosts` writes.
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
     no forwarding; argv only, validated and single-quoted operands, the script on stdin; 15 s, 8 MiB; the transcript in
     memory only, never logged or written (the extracted question and the prompts shown are saved with the conversation,
     like every passage); what the security judgement of S3 decided about the executable and multiplexing.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S5-T1 | the words moved, not changed (`claudeAsked.test.ts` or a new `waitingSaid.test.ts`) | `waitingSaid` gives, for each case of `:562-591` (several waiting, one waiting, elsewhere, answered, failed, nothing asked), the same text or sentence as today, byte for byte; the body of `questionWaitingHere` calls it | change one sentence → red naming it |
| S5-T2 | the terminal door never reads the local home or the tabs (`terminalDoorWiring.test.ts`) | the bodies of `takeFromTerminal`/`addFromTerminal` hold none of `matchedSource(`, `snapshots(`, `sourceSession(`, `questionWaitingHere(`, `waitingQuestion(`, `os.homedir(`; companion: `takeTheQuestion`'s body holds `matchedSource(` | call `questionWaitingHere` → red |
| S5-T3 | a question taken from a terminal is never sent by itself | the `deliverPassage` call in `takeFromTerminal` passes `false` for `send`, `true` for `fromSession`, `sourceOfTerminal(` for the origin | pass `sendsImmediately(…)` → `a remote question was sent without the person` |
| S5-T4 | a cut tail with no question says it was cut (a unit over the pure part) | lines with no `AskUserQuestion`, `cut: true` → the truncated sentence; `cut: false` → *"Claude Code has asked nothing in this folder yet."*; a cut tail that holds the waiting question → that question | ignore `cut` → `a cut tail was reported as nothing asked` |
| S5-T5 | Asked for a terminal goes the remote road, and the others keep theirs (`chatHooks` wiring) | `onShowAsked` checks the source BEFORE `resolveAndPin(` and `promptsFrom(`; a terminal thread reaches `readRemoteSession(`; companion: `resolveAndPin(` and `promptsFrom(` are still there for the others | take the local road for a terminal → red |
| S5-T6 | the page shows prompts and the cut note together (`bundledPage.test.ts`, RUN) | post `asked` with two prompts and a refusal → the box shows the first prompt AND the note; with no refusal → no note line; with no prompts → the refusal alone, as today | keep `:1675`'s branch only → `the cut note is not shown beside the prompts` |
| S5-T7 | the two items sit in the terminal's overflow (`terminalDoorWiring.test.ts`) | two `view/title` rows, `when` exactly `view == terminal && coai.terminalReadsSession`, groups not `navigation`; two palette rows `when: false`; the titles | put them in `navigation` → red |
| S5-T8 | every terminal door records itself first (`chatWiring.test.ts`, with the widened `doorCommands`) | the census finds `coai.takeFromTerminal` and `coai.addFromTerminal` and their `noteChatDoor` comes first; companion: `coai.askFromTerminal` is still found | do not widen `doorCommands` → `the number of recorders and the number of doors … have drifted apart` |
| S5-T9 | the context key follows the name and the answer (pure part in `remoteRead.ts`) | `readsKey('Creds: h / s', ask)` true; `(…, no)` false; `('Creds: before x', …)` true; `('bash', …)` false | make a `malformed` name false → `a bad creds name hides the items that explain it` |
| S5-T10 | every language names the items (`helpCoverage.test.ts`) | each of the five articles, not a fallback, contains `**CoAI: hosts read over ssh**` and the remote paragraph; the help census passes for the three new titles | leave `helpDe.ts` as it is → `the de chat article never says the session is read over ssh` |

- **Docs.** `research/module_extension.md` (E2 section): the doors, the context key, *Asked* through the reach, the page
  note, the door count. `research/module_tests.md`: the E2 row completed; the doors row (`:300`) notes that take/add from a
  terminal count as `take`/`add`; **not covered**: the items' visibility in a real window and the real VM — R7.
  `research/architecture.md`: the `chat` module's new dependency on the remote reader (one line in the module map).
- **Done when.** S5-T1…T10 green, each break-it seen red; the door census and the help census green;
  `notification-sites.json` regenerated; `SECURITY.md` has the section; the whole suite and lint green.
- **Size.** About 520 lines (≈ 150 code and manifest, ≈ 70 help in five languages, ≈ 40 `SECURITY.md` and docs, ≈ 260
  tests).

## 6. Build order

1. **S1** — spec here; Sonnet codes; Opus reviews; Sonnet fixes. Meanwhile Fable writes the S2 spec.
2. **S2** — from the Fable spec; Sonnet codes; Opus reviews; Sonnet fixes. Meanwhile Fable writes S3's security judgement.
3. **S3** — this section plus the judgement; Sonnet codes; Opus reviews; Sonnet fixes. If the judgement asks for a change to
   the parent's option list, the caller decides it as plan change 2.1 BEFORE S3 is coded.
4. **S4**, then **S5** — spec here; Sonnet codes; Opus reviews; Sonnet fixes.
5. One pull request for the epic. The code round of the review gate gets the epic's scope (this document's § 1, § 4 and
   § 11) as `planText`. Every CI check green, and the `extension · a real editor` job read by its own result (§ 8).
6. Merge. Build the pre-release VSIX from the merged head (`npm run package`). The owner installs it and does **R6 and R7
   on a VM** (parent ledger entry 1.1, and E2 *Done when*). Each result goes into the parent's R-table with its date, the
   VSIX version and its sha. A failed check is fixed before the tag.
7. Release the extension: the release-please pull request carries the `## Extension <version> — <date>` prose; the tag is
   pushed alone and its run observed. E3 does not start before that.

## 7. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*, *Stage 2*) | Story | Tests |
|---|---|---|
| Operand validation against hostile strings through a real `sh -c` | S1, S3 | S1-T3, S1-T4, S3-T3, S3-T4 |
| The parsers: banner before the sentinel, registry JSON, NUL lists, a cut tail, the title rows | S2 | the S2 spec |
| The script run by `sh -s` on Linux CI, temporary `HOME`, fake `tmux` and `ps` — the text sent is the text under test | S2, S3 | the S2 spec; S3-T5 |
| Several panes or several `claude` descendants: a list or a refusal, never a pick (change 1.1 point 2) | S2, S4 | the S2 spec (area 2); S4-T6 |
| The pick list: one, several, cancel | S4 | S4-T5, S4-T6 |
| The transcript found by id: zero or two refused; a folder not in `projectDirNames(cwd)` refused | S2 | the S2 spec (area 5) |
| The per-host consent: asked once, "no" remembered, no ssh before a "yes" | S4 | S4-T1…T3, S4-H1 |
| `fromSession` stays `false` for a non-`Creds:` terminal; `pinSession` never reads the local home for a `terminal` source | S1 | S1-T5, S1-T8, S1-T9 |
| An E1 `Creds:` conversation restored under E2 gains *Asked* (change 1.1 point 1) | S1, S4 | S1-T6, S1-T7, S4-T8 |
| Absent, ambiguous, unreadable, truncated: four sentences, each with a test | S2, S5 | the S2 spec (area 7); S5-T4 |
| No log line carries stdout | S3 | S3-T9 |
| The 15 s kill and the 8 MiB cap | S3 | S3-T6, S3-T7 |

## 8. Where each proof runs

- **Unit and wiring tests**: `npm test`, here and in CI (`ci.yml:575`, required, `ubuntu-latest`). The `sh -s` and `sh -c`
  tests RUN there; on a host with no `sh` they skip with that reason, and CI must show them run (S2 area 9).
- **Host scenario** S4-H1: `npm run test:host` CANNOT run on this machine (no `libnss3`). It runs only in CI's job
  `extension · a real editor` (`ci.yml:593`, `xvfb-run -a npm run test:host` at `:627`). That job has `continue-on-error:
  true` (`:596`), so a red scenario does NOT turn the pull request red: read the job's own conclusion and log on the head
  commit and write the result (with the sha) in the pull request description. A red host scenario blocks the merge as if it
  were required.
- **Real window, on a VM** (parent R-table), on the pre-release VSIX of the merged head, BEFORE the tag:
  - **R6** — one ssh call from Windows: the time to the first answer (target under 4 s), with the owner's real `Host`
    blocks that carry `LocalForward`; the open terminal keeps working while the read runs.
  - **R7** — the registry path finds the right session on each of the three users (`ai1-dev2`, `ai1-dev3`, `ai1-root`);
    the fallback list works when two sessions share a name. Also seen in R7, and recorded with it: the two items appear in
    a `Creds:` terminal's overflow and not in a plain one; the consent question appears once per host; a two-pane tmux
    session gives a list; the `comm` of the Claude CLI process on the VMs (S2 area 3).

## 9. Growth and security

- **Growth** (the parent's § *Growth surfaces*, made concrete): the consent list `coai.terminalHosts` in `globalState`, at
  most 100 rows of about 80 bytes (under 10 KiB), the oldest answer dropped at the cap; the picked ids, a `WeakMap` keyed by
  the `Terminal`, one entry per open terminal at most, gone with the terminal; one ssh child per press, at most three per
  press on the fallback, each killed at 15 s, stdout at most 8 MiB in memory and dropped after the parse; nothing on disk,
  nothing on the VM. Conversation records: unchanged (E1's store and its sweep).
- **Security.** One new outward edge: the extension host's local `ssh`, read-only, to a host the person agreed to, as the
  user their own `~/.ssh/config` logs in as. Its argv, its options and its executable are E2.S3's, under Fable's security
  judgement. The name of a terminal chooses the host and is not proof — hence the consent. No secret is handled. The
  transcript stays in memory. `SECURITY.md` gets its section in S5.

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

Open, for the people named:

1. **Multiplexing** (S3's security judgement): a person's `ControlMaster`/`ControlPersist` config could leave a process
   after the kill or reuse the terminal's own connection. An extra ssh option is a contract change: the caller decides it
   as 2.1, and a second change after it is a full stop.
2. **The `comm` of the Claude CLI** on the VMs is not measured (S2 area 3, R7).
3. **The extension host on another machine.** In a Remote window the extension host is remote, and so is its `ssh`. The
   owner's windows are local Windows windows; not measured, not built for.

## 11. Definition of Done — epic E2

- [ ] S1–S5 coded to their specs (S2 to Fable's spec; S3 to this section and Fable's security judgement); every scenario
      green; every break-it observed red, then green, and both observations in the summary.
- [ ] Change 1.1's two points hold, each with its test: an E1 `Creds:` conversation restored under E2 gains *Asked*
      (S1-T6, S1-T7, S4-T8); several panes or several `claude` descendants give a list or a refusal, never a pick (S2,
      S4-T6).
- [ ] The terminal door still only READS the clipboard (change 1.2): E2 adds no copy command and no borrow, and E1's
      terminal-read tests (S1r-T1…T7, S1r-H1) are green with no edit.
- [ ] Absent, ambiguous, unreadable and truncated each give their own sentence, each with a test; an ssh failure gives its
      own; no log line carries stdout (S3-T9).
- [ ] `npm run compile` exit 0; `npm test` green on CI with the `sh -s` tests RUN; `npm run lint` green; no suppression count
      grew; `sonarExclusions.test.ts` green with `remoteSessionHost.ts` listed.
- [ ] `notification-sites.json` regenerated; the door census and the help census green.
- [ ] S4-H1 green in CI's `extension · a real editor` job on the head commit, read from that job, named in the pull request.
- [ ] `SECURITY.md` has the paragraph on the ssh edge; `research/module_extension.md` (the E2 section),
      `research/module_tests.md` (the E2 row) and `research/architecture.md` (the new outward edge) updated; the help in
      five languages.
- [ ] The pull request merged; **R6 and R7 measured on a VM with the pre-release VSIX of the merged head, before the tag**,
      and recorded in the parent's R-table.
- [ ] The extension release cut from `main`: its changelog prose in the release-please pull request, the tag pushed alone,
      its run observed.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
