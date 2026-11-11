# PLAN — E1.S2 spec: the terminal source and its identity (CORNERSTONE)

> Status: **coded, 2026-11-10 — written by Fable before its code; implemented in 1f7c14a5, Opus review accept with fixes (applied); not merged.** Cornerstone spec, written before its code. Story
> E1.S2 of [PLAN_the_chat_opens_from_a_terminal_E1.md](PLAN_the_chat_opens_from_a_terminal_E1.md) (parent
> [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md), § *Stage 1*, ledger entry 1.1). Its own
> file because the stories document goes whole to a review gate and must stay short. It keeps no change ledger: a change
> is an entry in the parent. It is promoted with the E1 document.
>
> Scope: `src_vs_code` only. `src/chatStore.ts`, `src/chatSource.ts`, `src/terminalSource.ts`, `src/chatReturn.ts`,
> `src/chatReturnCommand.ts`, `src/chatCapture.ts` (the steal filter only), `src/chatSessionJoin.ts` (four lines),
> `src/chatCommand.ts` (`deliverPassage`, its three callers, one line in `newConversation`), comments in
> `src/chatGoto.ts`, `src/chatThread.ts`; tests; two host scenarios; `research/module_extension.md`,
> `research/module_tests.md`. Coder: Sonnet. Review: Opus. Contracts C1–C3 of the E1 document stand as written.
> **E1.S1 is committed as `9ac886c9`.** S2 builds on it: it adds to S1's `src/terminalSource.ts`, and its edits in
> `chatCapture.ts` are only the ones in § S2.7.

## S2.0 — What was read and what was measured

Read at `9ac886c9` (S1's commit; worktree `/home/jinx/git/wt/coai-chat-terminal`). `git diff --stat extension-v0.85.0
HEAD -- src_vs_code/src` lists S1's six files and nothing else (`chatCapture.ts`, `selectionCapture.ts`,
`terminalSource.ts`, three test files): **every other source file IS the released 0.85.0**, the store, the index, the
sweep and the restore among them. The E1 document cites `matchedSource` at `chatCapture.ts:174`; S1 moved it to `:188`.
Line numbers below are those of `9ac886c9`. All paths are under `src_vs_code/`.

| # | Fact | Where |
|---|---|---|
| F1 | The source type; its builders; the private validator; the private origin check; the record and meta readers; `sameSource`; the version | `src/chatStore.ts:70-76`; `:87`, `:90`; `:244-260`; `:276-285`; `:302` (source `:334`, origin `:338`, `fromSession` `:350`), `:363` (`:383`); `:458-467`; `:43` |
| F2 | Lint: `complexity` 4, and every `case` counts. A switch over FOUR kinds is 5 (measured: three cases pass, four fail, grouped cases count too). `chatStore.ts` holds four complexity suppressions today: `sourceFrom`, `recordFrom`, `metaFrom`, `sameSource`. `chatSource.ts`, `chatReturn.ts`, `chatReturnCommand.ts` hold none | `eslint.config.mjs:113-115`; `eslint-suppressions.json` |
| F3 | The steal. `sourceSession` reads a known panel whose key is not one of the TABS as "its tab has gone", and re-keys it to a Claude tab of the same label. The re-key is EXECUTED in two places only. It is fed by `matchedSource`. `askedForGoto` calls the same two `sourceSession` lines but reads only `matched.key`: nothing is re-keyed there | `src/sessionKey.ts:162-171`; `src/chatCommand.ts:244-246`, `:528-530`; `src/chatCapture.ts:188-214` (`:194-198`); `src/chatSessionJoin.ts:252-256` |
| F4 | A restored conversation is keyed by a fresh `{}` and labelled with its title. Its thread carries the record's source and `fromSession`. It pins again only when `fromSession && source.kind === 'none'` | `src/chatConversationRestore.ts:256`; `:176`, `:93`, `:205`; `:265-267` |
| F5 | A thread is made in two places and nowhere else | `src/chatCommand.ts:140`, `src/chatConversationRestore.ts:162` |
| F6 | `pinSession` runs on `fromSession` and a title. The Asked press has no gate of its own; the page draws the button only for `fromSession`. `resolveAndPin` asks `findSessionById` (empty for a source that is not `claude`), then walks the LOCAL home by TITLE, and `adoptFound` REPLACES `mine.source` and saves | `src/chatSessionJoin.ts:185-209`; `src/chatHooks.ts:361-404`, `src/chatPage.ts:1181`; `src/chatSessionJoin.ts:75-113`, `:122-131`, `:145-170` (`:163`) |
| F7 | Back to source: the decision, its exhaustive switch, the tab recovery; the executor: the tab must be one of the TABS, the exhaustive switch, one `notify` site | `src/chatReturn.ts:20-28`, `:40-57`, `:69-77`; `src/chatReturnCommand.ts:45-55`, `:100-106`, `:108-114`, `:120-127`, `:130-145`, `:201-209` |
| F8 | Go-to binds only through `bindingTo` → `bindable` = `sameSource` AND `sameRoot`. The tab's source is built as `file`, `none` or `claude`, never anything else | `src/chatGotoCommand.ts:261-279`, `:186`, `:208`, `:212`; `src/conversationPickerCommand.ts:281-305`, `:332`; `src/chatGoto.ts:431-437`; `src/chatSessionJoin.ts:274-284` |
| F9 | The store: the save self-check; `refile` and its compare; `probe`; the torn-entry log; the listing; the index reads every row it does not hold on EVERY refresh; `bySource`; the sweep nominates expired records from READABLE metas and orphans by file NAME | `src/chatStoreFile.ts:531`; `:469`, `:491`; `:576-596`; `:692-710` (`:706`); `:916`, `:939`; `src/chatStoreCache.ts:188-227` (`:198`, `:210-211`); `:153-155`; `src/chatStoreSweep.ts:450-477` |
| F10 | `deliverPassage`; its rule; its three callers; `newConversation` reads `state.fromSession` three times | `src/chatCommand.ts:275-286`; `:336`; `:264`, `:549`, `:580`; `:123`, `:163`, `:197` |
| F11 | Two tests pin the old text | `src/test/chatSourceWiring.test.ts:171`; `src/test/bugzReviewWiring.test.ts:482` |
| F12 | A new temp-folder line in a test is a `raw-temp` finding: the baseline holds every existing line and only shrinks | `raw-temp-baseline.json`; rule `common/test-leftovers-baseline.md` |
| F13 | CredsForDevs titles: `Creds: <folder> / <entry>` (the folder escaped, the entry written as it is), `Creds: before <owner>`, `Creds run: <name>` | `dew_flow_creds_for_devs · src_vs_code/src/terminalTitles.ts:44-58` at `cb9be475` |

**Measured 2026-11-10: what the released 0.85.0 does with a `terminal` record.** A record file and its index entry with
`"source":{"kind":"terminal","name":"Creds: ai1-dev2 / small-tasks"}`, `version` 1, last used 200 days ago, were put in an
empty store folder. The real `ChatStoreFile`, `ChatStoreKeeper`, `ConversationIndex`, `planSweep`, `restoreDecision` and
`opening` of the unchanged tree were run on it (bundled from `src/`; these modules are 0.85.0 byte for byte).

| # | What was done | Result |
|---|---|---|
| M1 | `recordFrom`, `metaFrom` | `undefined`, `undefined` |
| M2 | `store.read(id)` | `incompatible` — *"the conversation on disk is not one this build can read"* |
| M3 | `store.listMeta()`, `store.entry(id)`, `index.size`, `index.bySource(...)` | 0 rows; `undefined`; 0; 0 — the picker and go-to do not show it |
| M4 | `index.refresh()` twice | the entry is read again on EACH refresh; one `console.error` each time: *"a conversation index entry is torn or not one this build can read: `<path>`"* |
| M5 | `store.save(another record, same id, base 0)` | `incompatible`; nothing written |
| M6 | `store.retireIfExpired(id, …)` at 200 days | `kept`, `incompatible` |
| M7 | `planSweep(survey, listing, now, …)` | `expired: []`, `orphanMetas: []`, `orphanRecords: []` — the sweep leaves both files |
| M8 | the reload serializer's decision; the picker's answer | a notice tab: *"… written in a form this build cannot read — most likely by a newer version of ConnectOtherAIs. Nothing has been deleted …"*, no retry; *"… could not be opened … Nothing has been deleted."* |
| M9 | both files after all of it | byte-identical |

Each read of the RECORD file also logs *"a conversation file is not one this build can read: `<path>`"*. No notification
is raised anywhere. The index is refreshed at activation, at each picker open and after a followed rename
(`chatStoreHousekeeping.ts:105`, `conversationPickerCommand.ts:558`, `chatFollow.ts:132`).

The coder repeats this in the repository as step 0 of § S2.14 (scenario T34 with the kind `terminal`, on the unchanged tree).

## S2.1 — Decisions

1. **The identity is the FULL terminal name, as the host reports it at the first press, compared exactly.** No strip, no
   trim, no case fold, no Unicode normalisation. `Creds: A / s` and `Creds: a / s` are two terminals. A case-blind compare
   makes `oneLiveNamed` answer `several` for two different terminals, or `one` for the wrong one.
2. **A terminal name is valid when it is a string of 1 to 500 UTF-16 units and is ONE LINE**: no control character
   (Unicode category `Cc`: U+0000–U+001F, U+007F, U+0080–U+009F) and no line separator (U+2028, U+2029). An invalid name
   builds `none` and is refused on read. Why control characters are refused:
   - the name is external data. A program in the terminal sets it with a title sequence — also a program on the remote
     machine — and another extension can give `createTerminal` any string;
   - the name goes into two sentences a person reads, and in E2 it selects an ssh host. One check at the one boundary
     makes each later reader safe without a check of its own (`security.md`: put the measure on the single road in);
   - a name is one line by nature. CredsForDevs already keeps control characters out of the folder half of its titles (F13);
   - the identity is exact, so a name cannot be cleaned and kept: a cleaned name never equals the live `terminal.name`.
     It is all or nothing.

   The cost: a conversation from such a terminal is saved with `none` (§ S2.12, residual 1). An honest name does not carry one.
   500 is the parent's number. It bounds a field that is copied into every index entry (the index holds all of them in
   memory) and into a notification. The unit is `name.length`; a test proves the unit.
3. **The name is recorded once and never updated.** The parent rules out following a rename. While the window lives, the
   key decides (decision 4); the recorded name is used only when the key is gone.
4. **The live key is the `vscode.Terminal` object. `terminalMatch` never reads a name to match.** The same terminal
   object is the same conversation, also after the terminal's name changed. A new terminal of the same name is a new
   conversation. Two live terminals of one name are two conversations with equal sources. Reason: VS Code's default
   terminal title follows the running program, so a name-keyed match starts a new conversation each time the program
   changes. (The default is from VS Code's documentation, not measured here. R4 measures the `Creds:` case.)
5. **`fromSession` reads as `false` for every `terminal` source, on both roads into a thread**: the door
   (`newConversation`) and the disk (`recordFrom`). One pure rule, `readsSession`. On disk either value is accepted
   (`agreeOnOrigin`), so a record that E2 wrote with `true` is not dropped by an E1 build after a rollback — but it does
   not offer *Asked* there, because *Asked* would walk the LOCAL home by title and `adoptFound` would replace the terminal
   source with a local session's (F6). This makes the parent's sentence true — *"`fromSession` is `false` for every terminal
   conversation in stage 1"* — for a record too. E2 changes this one rule when it adds its guard.
6. **Every switch over the source kind is exhaustive, in a form that passes `complexity` 4** (F2): one arm is taken off
   with a comparison and the other three go through a `switch` whose `default` takes a `never`; or a table typed by the
   kind. `agreeOnOrigin` and `sameSource` are not exhaustive today (a new kind falls into `return true` and `return false`
   in silence); both become so. `sourceFrom` stays a chain of tests — it reads unknown data, and `undefined` is the right
   answer for a kind it does not know — and a derived test (T5) holds it to the type.
7. **The steal filter is on ONE road.** A new `tabMatch(panels)` in `chatCapture.ts` takes the snapshot, the filtered
   panel list and both `sourceSession` calls. `matchedSource` and `askedForGoto` both call it. No other code calls
   `sourceSession`; a scan test holds that. The filter judges by the THREAD's durable source, never by the key (F4).
   The filter and its reading of the registry are pure (`tabSourced`, `tabPanels` in `terminalSource.ts`), so a unit
   test runs them against a real `ChatPanels`; only the three-line `tabMatch` is host code.
8. **`deliverPassage` takes `origin: ConversationSource` in place of `sourceUri`** (C2, unchanged). The two tab doors
   build it with ONE function, `sourceOfTab(fromSession, uri)`. The bug door passes `sourceOfFile(fileUriOf(chat))`.
9. **Back to source: the chat's own terminal first, then the ONE live terminal of the recorded name, else a sentence.**
   `oneLiveNamed` takes the live terminals and hands back the KEY of the one it found, so no second lookup can pick among
   several. Exited terminals are left out inside the pure rule.
10. **Go-to is not changed in E1.** A terminal conversation is never bound to a tab, by `bindable`. Tests pin it. The two
    comments are corrected.
11. **Rollback: E1 adds nothing to the record for an older build.** No version move, no marker, no second field. 0.85.0
    keeps both files, hides the conversation, never overwrites and never expires it, and says so in a restored tab
    (M1–M9). The only cost is console lines. Reasons and tests: § S2.11.
12. **`CONVERSATION_VERSION` stays 1.** Moving it hides EVERY conversation from 0.85.0 after a downgrade, not only the
    terminal ones.

## S2.2 — The kind, its builders, its reader (`src/chatStore.ts`)

```ts
export type ConversationSource =
  | { readonly kind: 'claude'; readonly sessionId: string }
  | { readonly kind: 'file'; readonly uri: string }
  /** A terminal, by its FULL name as the host reported it at the first press. See the note below. */
  | { readonly kind: 'terminal'; readonly name: string }
  | { readonly kind: 'none' };

/** The longest terminal name that is an identity, in UTF-16 units. ONE constant: the builder and the reader both read it. */
export const TERMINAL_NAME_MAX = 500;

/** A control character (category Cc) or a line separator: what makes a name more than one line. */
const NOT_ONE_LINE = /[\p{Cc}\u{2028}\u{2029}]/u;

/** Whether this value is a terminal name this build keeps as an identity. The ONE rule, for the builder and the reader. */
export const isTerminalName = (name: unknown): name is string =>
  typeof name === 'string' && name.length > 0 && name.length <= TERMINAL_NAME_MAX && !NOT_ONE_LINE.test(name);

export const sourceOfTerminal = (name: string): ConversationSource =>
  (isTerminalName(name) ? { kind: 'terminal', name } : { kind: 'none' });

/** What a TAB door files a new conversation under: a Claude tab has no id yet (`pinSession` writes it), a file tab has its document. */
export const sourceOfTab = (fromSession: boolean, uri: string): ConversationSource =>
  (fromSession ? { kind: 'none' } : sourceOfFile(uri));
```

- Write the two line separators as `\u{2028}` and `\u{2029}` (the braces form), in the regular expression and in the
  test fixtures. `\p{Cc}` is accepted by `no-control-regex`; a `\x00-\x1f` range is not.
- The doc comment of the type says **"Never a title"** (`:65-68`). Add one paragraph under it: a terminal has no uri and
  no session id, so its NAME is its durable identity. A name is therefore allowed to do two things only: compare equal
  (`sameSource`), and find ONE live terminal to show on request (`oneLiveNamed`, which never picks). It never re-keys and
  never binds. Several conversations can carry one terminal source — that is normal, not damage.
- `sourceFrom` (`:244`): the row type gains `name?: unknown`; add, before the final `return undefined`:

```ts
  if (row.kind === 'terminal') {
    return isTerminalName(row.name) ? sourceOfTerminal(row.name) : undefined;
  }
```

  A stored terminal name that is not valid is `undefined` (the record is dropped as `incompatible` and stays on disk),
  NEVER `none`: present and wrong is a record this build cannot trust (`:295-300`). `sourceFrom` stays one suppressed
  function; its count does not grow.

## S2.3 — Origin: what `fromSession` may say, and what it reads as (`src/chatStore.ts`)

Replace the body of `agreeOnOrigin` (`:276-285`) and add `readsSession`. Keep the doc comment of `agreeOnOrigin` and add
the terminal sentence to it.

```ts
/** What a kind of source says about `fromSession`. */
type OriginRule =
  /** It must be true: a Claude session. */
  | 'must'
  /** It must be false: a document. */
  | 'must not'
  /** Either, and it is read as written: a migrated record recorded the door and never the session. */
  | 'as written'
  /** Either on disk, and it is read as FALSE: no terminal conversation reads a Claude session in stage 1. */
  | 'not yet';

/** Exhaustive: a fifth kind of source does not compile until it has a row here. */
const ORIGIN: Readonly<Record<ConversationSource['kind'], OriginRule>> = {
  claude: 'must',
  file: 'must not',
  none: 'as written',
  terminal: 'not yet',
};

function agreeOnOrigin(source: ConversationSource, fromSession: boolean): boolean {
  const rule = ORIGIN[source.kind];

  return (rule !== 'must' || fromSession) && (rule !== 'must not' || !fromSession);
}

/**
 * Whether a conversation of this source reads a Claude session back — what its thread's `fromSession` is.
 * A terminal never does in stage 1: `pinSession` and the Asked press walk the LOCAL home by title, and a terminal's
 * session is on another machine. Stage 2 changes the `terminal` row together with its guard.
 */
export function readsSession(source: ConversationSource, fromSession: boolean): boolean {
  return ORIGIN[source.kind] === 'not yet' ? false : fromSession;
}
```

In `recordFrom`, `:350` becomes `fromSession: readsSession(source, row.fromSession === true),`. Nothing else in
`recordFrom` or `metaFrom` changes. `source.kind` is safe as a table key there: `sourceFrom` has validated it.

## S2.4 — `sameSource`, exhaustive (`src/chatStore.ts:458-467`)

```ts
/** What a source is compared BY — nothing for `none`, which is the absence of an identity. */
function identityOf(source: ConversationSource): string | undefined {
  return source.kind === 'none' ? undefined : namedBy(source);
}

/** Exhaustive: a fifth kind of source does not compile until it says what it is compared by. */
function namedBy(source: Exclude<ConversationSource, { kind: 'none' }>): string {
  switch (source.kind) {
    case 'claude':
      return source.sessionId;
    case 'file':
      return source.uri;
    case 'terminal':
      return source.name;
    default: {
      const unhandled: never = source;

      throw new Error(`a conversation source this build cannot compare: ${JSON.stringify(unhandled)}`);
    }
  }
}

export function sameSource(left: ConversationSource, right: ConversationSource): boolean {
  const one = identityOf(left);

  return left.kind === right.kind && one !== undefined && one === identityOf(right);
}
```

Keep the doc comment (*`none` matches nothing*). The regression net is the existing tests
`two conversations match a tab only when their source is the same source`, `a conversation that names no source matches
NOTHING` and `an identity that is empty is no identity` (`chatStore.test.ts:233`, `:242`, `:364`): **green with no edit**.
`sameSource` stops being a lint violation, so the complexity count of `src/chatStore.ts` in `eslint-suppressions.json`
goes from 4 to 3 (§ S2.14, commands).

In E1 no production code asks `sameSource` or `bySource` about a terminal source on purpose; `refile`, `bindable` and
`bySource` meet one only as the OTHER side and must answer `false`. E3's re-attach is the first reader of the `true` arm.

## S2.5 — Every reader of the source — what a `terminal` source does there

The list is the E1 document's § 6. "No change" means the code is right as it is and a test pins it.

| Reader | Where | With a `terminal` source | Change | Test |
|---|---|---|---|---|
| The type and builders | `chatStore.ts:70`, `:87`, `:90` | the new kind; `sourceOfTerminal`; `sourceOfTab` | § S2.2 | T1–T3, T10 |
| `sourceFrom` (record and meta) | `chatStore.ts:244`, `:334`, `:383` | a valid name is read; any other is `undefined` | § S2.2 | T4–T6 |
| `agreeOnOrigin` | `chatStore.ts:276`, `:338` | either `fromSession` is accepted | § S2.3 | T7 |
| `recordFrom`'s `fromSession` | `chatStore.ts:350` | reads `false` | § S2.3 | T7, T8 |
| `sameSource` | `chatStore.ts:458` | equal names only; never equal to another kind | § S2.4 | T9 |
| The save self-check | `chatStoreFile.ts:531` | a valid terminal record passes; one with a refused name is not written | no change | T31, T32 |
| `refile` | `chatStoreFile.ts:469`, `:491` | `kept: 'moved on'` — a file's `was` never equals a terminal source | no change | T33 |
| `bySource` | `chatStoreCache.ts:153` | through `sameSource`; several rows for one terminal source are legal | no change | T30 |
| `knownFolder` | `chatSource.ts:95` | `''`; `fsPath` is not called | § S2.6 | T12 |
| `followable` | `chatSource.ts:284` | `false`, also for a name that looks like a `file:` uri | no change | T13 |
| `filedFor`, `originOf`, `reorigin` | `chatRoots.ts:77`, `:89`, `:94` | through `knownFolder` → `filedUnder('', …)` → the fallback, which is the first root | no change | T12 |
| Rename follow, live and on disk | `chatFollow.ts:58`, `:85` | skipped by `followable` | no change | T13, T33 |
| The thread's field | `chatThread.ts:284` | holds it | doc comment `:276-283`: add the terminal's full name | — |
| The record writer | `chatPersist.ts:69` | writes `thread.source` and `thread.fromSession` as they are | no change | T31 (and the existing pin in `chatSourceWiring.test.ts`) |
| A new conversation | `chatCommand.ts:76`, `:104`, `:151`, `:336` | `source: origin`; filed through `originOf`; `:104` is S3's | § S2.8 | T35, T37 |
| `newConversation`'s `fromSession` | `chatCommand.ts:123`, `:163`, `:197` | `false`: no *Asked*, no `pinSession` | § S2.8 | T8, T37 |
| The restore's re-pin | `chatConversationRestore.ts:265` | not reached: the source is not `none`. The thread gets `fromSession` false from `recordFrom` | no change | T7 |
| `resolveAndPin`, `findSessionById`, `adoptFound` | `chatSessionJoin.ts:75`, `:122`, `:145` (`:163`) | not reached: the page has no *Asked* button when `fromSession` is false | no change in E1 (E2 owns the guard) | T7, T8 |
| Go-to's tab source | `chatSessionJoin.ts:274-284` | never built as `terminal` | no change | — |
| Go-to's decision and bind check | `chatGoto.ts:253`, `:273`, `:305`, `:373`, `:431-437` | § S2.10 | comments only | T28, T29 |
| Back to source: the decision | `chatReturn.ts:20`, `:40`, `:69` | the `terminal` road; no tab is recovered | § S2.9 | T21, T22 |
| Back to source: the executor | `chatReturnCommand.ts:53-54`, `:100`, `:108`, `:120`, `:130` | `show()` or a sentence | § S2.9 | T23–T27, T39, S2-H1 |
| Agent mode | six sites | S3's story, not S2's | — | — |
| Where `known` is matched by label | `chatCapture.ts:194-198`, `chatSessionJoin.ts:253-255` | the panel is not in the list | § S2.6, § S2.7 | T18–T20, T36, S2-H2 |
| `revealBound` | `chatRegistry.ts:107-115` | `onto` is always `undefined` for a terminal conversation, so it is only revealed | no change | T28 |

`chatOrphans.ts`, `chatLedger.ts` and `chatStoreSweep.ts` read no source (checked again; the sweep is M6–M7).

## S2.6 — The pure rules (`src/chatSource.ts`, `src/terminalSource.ts`)

`knownFolder` (`chatSource.ts:95-111`) — four kinds do not fit one switch (F2):

```ts
export function knownFolder(source: ConversationSource, fsPath: (uri: string) => string): string {
  return source.kind === 'file' ? fsPath(source.uri) : noFolder(source);
}

/** The kinds that name no folder. Exhaustive: a fifth kind does not compile until it says whether it names one. */
function noFolder(source: Exclude<ConversationSource, { kind: 'file' }>): string {
  switch (source.kind) {
    case 'claude':
    case 'terminal':
    case 'none':
      return '';
    default: {
      const unhandled: never = source;

      throw new Error(
        `a conversation source this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the kinds it may be are file, claude, terminal and none',
      );
    }
  }
}
```

Add to the doc comment: a terminal names no folder — its session can be on another machine — so its conversation is
filed under the fallback root, as a record with no source is.

`src/terminalSource.ts` — add below S1's code (`9ac886c9`: `TERMINAL_COPY`, `NO_TERMINAL_SELECTION`, `terminalPassage`,
`captureTerminalSelection`; none of it changes). New imports: `import type { ChatPanels } from './chatPanels';`,
`import type { ConversationSource } from './chatStore';`, `import { threads } from './chatThread';` and
`import type { KnownPanel, SessionMatch } from './sessionKey';`. The module stays pure: `chatPanels.ts` and
`chatThread.ts` have no `vscode` and are loaded by unit tests today (`chatPanel.test.ts`, `chatHost.test.ts`).

```ts
const CREDS = 'Creds: ';

/** The chat's title: the terminal's name without a leading `Creds: ` — at the start only, and only when something is left. */
export function terminalLabel(name: string): string {
  return name.startsWith(CREDS) && name.length > CREDS.length ? name.slice(CREDS.length) : name;
}

/** The conversation of THIS terminal object, or a new one. It never matches by name and never answers `rekey`. */
export function terminalMatch(known: readonly KnownPanel[], terminal: object, name: string): SessionMatch {
  const label = terminalLabel(name);

  return known.some((panel) => panel.key === terminal)
    ? { kind: 'existing', key: terminal, label }
    : { kind: 'new', key: terminal, label };
}

/** One terminal of the window, narrowed to what the rules read. `key` is the host's `vscode.Terminal` object. */
export interface LiveTerminal {
  readonly key: object;
  readonly name: string;
  /** The terminal's process has ended (`exitStatus` is set). Such a terminal is never shown. */
  readonly exited: boolean;
}

export type NamedTerminal =
  | { readonly kind: 'one'; readonly key: object }
  | { readonly kind: 'none' }
  | { readonly kind: 'several'; readonly count: number };

/** The ONE live terminal of this exact name — or that there is none, or how many there are. It never picks. */
export function oneLiveNamed(live: readonly LiveTerminal[], name: string): NamedTerminal {
  const named = live.filter((one) => !one.exited && one.name === name);
  if (named.length === 1) {
    return { kind: 'one', key: named[0]!.key };
  }

  return named.length === 0 ? { kind: 'none' } : { kind: 'several', count: named.length };
}

/**
 * The panels a TAB may be matched against: every panel but the ones opened from a terminal.
 * Judged by the conversation's durable source, never by its key — a restored terminal conversation is keyed by a plain object.
 */
export function tabSourced(
  known: readonly KnownPanel[],
  sourceOf: (key: object) => ConversationSource | undefined,
): readonly KnownPanel[] {
  return known.filter((panel) => sourceOf(panel.key)?.kind !== 'terminal');
}

/** The durable source of the conversation registered under this key, or nothing for a key that holds none. */
function sourceUnder(panels: Pick<ChatPanels, 'get'>, key: object): ConversationSource | undefined {
  const entry = panels.get(key);

  return entry === undefined ? undefined : threads.get(entry.id)?.source;
}

/** The registry's panels a tab may be matched against — `tabSourced`, read from each panel's own thread. */
export function tabPanels(panels: Pick<ChatPanels, 'known' | 'get'>): readonly KnownPanel[] {
  return tabSourced(panels.known(), (key) => sourceUnder(panels, key));
}
```

- `terminalLabel` does not clean the name. A title is shown as text and the page escapes it (`chatPage.ts:1179`, `:2409`),
  as it does for a Claude tab's label. Cleaning it would let a Claude tab's label equal it by accident.
- The parent wrote `oneLiveNamed(names, name)`. It takes the terminals and returns the key instead (decision 9). E3's
  `reattachPlan` can pass `{ key, name, exited: false }` rows.
- `terminalMatch` is given `panels.known()` UNFILTERED (S3's door): the terminal's own panel must be in the list.

## S2.7 — The steal filter, on one road (`src/chatCapture.ts`, `src/chatSessionJoin.ts`)

**These are S2's only edits in `chatCapture.ts`.** The rest of that file is S1's and stays as `9ac886c9` left it.

1. Imports: add `tabPanels` to the `./terminalSource` import (`:9`); add `SessionMatch` to the `./sessionKey` import (`:10`).
2. Add above `matchedSource`:

```ts
/** What one look at the tabs found: the snapshot, the Claude match, and the match of either door. */
export interface TabMatch {
  readonly active: TabSnapshot | undefined;
  readonly all: readonly TabSnapshot[];
  readonly claude: SessionMatch | undefined;
  readonly matched: SessionMatch | undefined;
}

/**
 * The active tab, matched against the panels a TAB may own — one snapshot and one list, for every caller.
 *
 * <p>A panel opened from a terminal is not in that list. Its key was never a tab, so `sourceSession` reads it as a
 * panel whose tab has gone, and would hand it to a Claude tab of the same name: the answer then arrives in the
 * terminal's conversation and nothing says so. This is the ONLY place that calls `sourceSession`.</p>
 */
export function tabMatch(panels: ChatPanels): TabMatch {
  const { active, all } = snapshots();
  const known = tabPanels(panels);
  const claude = sourceSession(active, all, known);

  return { active, all, claude, matched: claude ?? sourceSession(active, all, known, isOrdinaryEditorTab) };
}
```

   The return type is a NAMED interface on purpose: a signature that ends in a line `} {` cuts `bodyOf` short (it
   reads to the first `}` at the start of a line), and T36 reads this function's body.
3. In `matchedSource`, replace the four statements `const { active, all } = snapshots();` … `const matched = claude ??
   sourceSession(…);` by `const { all, claude, matched } = tabMatch(panels);`. The `return` stays as it is, and the line
   `uri: all.find((tab) => tab.key === matched?.key)?.uri ?? '',` stays character for character
   (`chatSourceWiring.test.ts:169` pins it).
4. In `askedForGoto` (`chatSessionJoin.ts:252-255`), replace the four statements by
   `const { active, all, matched } = tabMatch(panels);`. Import `tabMatch` from `./chatCapture` in place of `snapshots`;
   drop `sourceSession` from the `./sessionKey` import. Nothing else in that function changes. Nothing is re-keyed there
   today (F3); the change is for the one road, so a later reader of the match kind cannot bring the steal back.

What the filter does not change: `terminalMatch` (it needs the terminal's panel), `chatReturnCommand.ts:81`, `:88`,
`chatRegistry.ts`, `chatFollow.ts:55` — they read `panels.known()` for other questions.

A second effect, wanted: with a terminal panel `main` and an orphaned Claude panel `main`, the label was ambiguous (two
of one name) and a Claude tab `main` got a new conversation. Now only the Claude panel counts, and the tab is re-keyed to
it, which is what the label fallback is for.

## S2.8 — `deliverPassage` and the session flag (`src/chatCommand.ts`)

1. Signature (`:275-286`): rename the fourth parameter `source` → `match` (C2's name), and replace `sourceUri: string`
   and its comment by `/** What the conversation is opened FROM, built by the door. */ origin: ConversationSource,`.
   In the body, `source.key`/`source.label` become `match.key`/`match.label` (`:327-328`), and `:336` becomes
   `source: origin,`. Rewrite the two comment lines above it: the door builds the origin; a Claude tab's is `none` until
   `pinSession` writes its session.
2. Callers:
   - `:264` → `await deliverPassage(panels, extensionUri, ready, source, passage, plan.send, match !== undefined, sourceOfTab(match !== undefined, uri));`
   - `:549` → `await deliverPassage(panels, extensionUri, ready, source, { text: question.text }, false, claude !== undefined, sourceOfTab(claude !== undefined, uri), append);`
   - `:580` → `await deliverPassage(panels, extensionUri, ready, { kind: 'new', key, label: chat.label }, { text: chat.text }, false, false, sourceOfFile(fileUriOf(chat)));`
   Import `sourceOfTab` from `./chatStore`.
3. `newConversation`: add as its first statement
   `const fromSession = readsSession(state.source, state.fromSession);` and use `fromSession` at `:123`, `:163` and `:197`
   (`pinSession(entry, state.title, fromSession);`). `state.fromSession` then appears once in that function. Import
   `readsSession`. Extend the doc comment of `state.source` (`:72-75`): a terminal's full name for the terminal door.
   This is what keeps `pinSession` off a terminal conversation whatever a door passes.

`deliverPassage` and `newConversation` are suppressed functions already; their counts do not change.

## S2.9 — Back to source

`src/chatReturn.ts`:

```ts
import { type LiveTerminal, type NamedTerminal, oneLiveNamed } from './terminalSource';

export type BackTo =
  | { readonly kind: 'tab' }
  | { readonly kind: 'session'; readonly sessionId: string }
  | { readonly kind: 'file'; readonly uri: string }
  /** The terminal the chat was opened from, by its recorded full name. */
  | { readonly kind: 'terminal'; readonly name: string }
  | { readonly kind: 'nowhere' };

/** Everything but the live tab: what the conversation RECORDED. */
export type Recorded = Exclude<BackTo, { kind: 'tab' }>;

export function recordedRoute(source: ConversationSource): Recorded {
  return source.kind === 'none' ? { kind: 'nowhere' } : namedRoute(source);
}

/** Exhaustive: a new kind of source is a compile error here, not a chat that goes nowhere in silence. */
function namedRoute(source: Exclude<ConversationSource, { kind: 'none' }>): Recorded {
  switch (source.kind) {
    case 'claude':
      return { kind: 'session', sessionId: source.sessionId };
    case 'file':
      return { kind: 'file', uri: source.uri };
    case 'terminal':
      return { kind: 'terminal', name: source.name };
    default:
      return unknownSource(source);
  }
}

/** Which terminal to show, or the sentence that says why none is. */
export type TerminalRoad =
  | { readonly kind: 'show'; readonly key: object }
  | { readonly kind: 'refuse'; readonly sentence: string };

/**
 * The chat's OWN terminal while it is open and running — exact, like the live tab — else the ONE live terminal of
 * the recorded name. A restored chat is keyed by a plain object, so it always takes the second road.
 */
export function terminalRoad(chatKey: object, live: readonly LiveTerminal[], name: string): TerminalRoad {
  const own = live.find((one) => one.key === chatKey && !one.exited);

  return own === undefined ? namedRoad(oneLiveNamed(live, name), name) : { kind: 'show', key: own.key };
}

function namedRoad(found: NamedTerminal, name: string): TerminalRoad {
  switch (found.kind) {
    case 'one':
      return { kind: 'show', key: found.key };
    case 'none':
      return { kind: 'refuse', sentence: terminalNotOpen(name) };
    case 'several':
      return { kind: 'refuse', sentence: terminalsShareName(found.count, name) };
    default: {
      const unhandled: never = found;

      throw new Error(`a terminal lookup answer this build has no arm for: ${JSON.stringify(unhandled)}`);
    }
  }
}

export function terminalNotOpen(name: string): string {
  return `The terminal “${name}” is not open.`;
}

export function terminalsShareName(count: number, name: string): string {
  return `${count} terminals are called “${name}” — open the one you mean.`;
}
```

- The two sentences are the parent's, character for character (curly quotes U+201C/U+201D, an em dash). `name` is the
  full recorded name.
- `unknownSource` keeps its body; its return type becomes `Recorded`. `recoveredTab` (`:69-77`) is NOT changed: for a
  `terminal` source it already answers `undefined`, and T22 pins it. Add one sentence to its doc comment: a terminal
  source never takes a tab — a Claude tab that wears the chat's name is not where the chat came from.
- Update the header comment (`:4-12`) and `backTo`'s: a recorded terminal is the fourth road.

`src/chatReturnCommand.ts`:

```ts
/** One terminal as the decision reads it. */
const asLive = (one: vscode.Terminal): LiveTerminal => ({ key: one, name: one.name, exited: one.exitStatus !== undefined });

/**
 * The terminal, through VS Code's own `Terminal.show()` — no other extension is called. ONE read of the window's
 * terminals decides and acts, so the two cannot see two lists.
 */
function toTerminal(key: object, name: string): void {
  const all = vscode.window.terminals;
  const road = terminalRoad(key, all.map(asLive), name);
  if (road.kind === 'refuse') {
    refuse(road.sentence);

    return;
  }
  all.find((one) => one === road.key)?.show();
}
```

- `backToSource` (`:54`): `await go(backTo({ liveTab: tab !== undefined, source: thread.source }), tab, thread.source, chat.key);`
- `go(answer, tab, source, key: object)`: passes `key` to `toTabOrRecord(tab, source, key)` and `fromRecord(answer, key)`.
  `toTabOrRecord` passes it to `fromRecord(recorded, key)`.
- `fromRecord` (`:130-141`) — four arms do not fit one switch (F2):

```ts
async function fromRecord(answer: Recorded, key: object): Promise<void> {
  return answer.kind === 'nowhere' ? refuse(NOWHERE) : toRecorded(answer, key);
}

/** Exhaustive — a new arm fails to compile. */
async function toRecorded(answer: Exclude<Recorded, { kind: 'nowhere' }>, key: object): Promise<void> {
  switch (answer.kind) {
    case 'session':
      return toSession(answer.sessionId);
    case 'file':
      return toFile(answer.uri);
    case 'terminal':
      return toTerminal(key, answer.name);
    default:
      return unreachable(answer);
  }
}
```

- `show()` takes no argument: the terminal comes forward and gets the focus, as a tab does on this command (owner
  decision 4; R5 is the real-window check, in the panel and in the editor area).
- `refuse` is the existing function: no new `notify` site, so `notification-sites.json` does not change.
- `sourceTab` (`:100-106`) is not changed. For a terminal chat it finds no tab: the key is not a `Tab`, and
  `recoveredTab` answers nothing. So `liveTab` is false and the decision is the recorded road.

**A restored conversation** (key `{}`, F4): `terminalRoad` finds no own terminal and goes by the recorded name — one
live terminal of that name is shown; none or several is a sentence. A conversation is not attached to the terminal
again in E1 (that is E3): a press in that terminal after a reload starts a new conversation with an equal source.

## S2.10 — Go-to and the other doors, in E1

- **A terminal has no tab kind.** `TabKind` stays `claude`, `document`, `other`. *Go to* reads the active TAB. A terminal
  in the panel is not a tab; a terminal in the editor area is a tab of kind `other`, and gets the whole list.
- **A terminal record is never matched by source.** `asked.source` is `file`, `none` or `claude` (F8), so `bySource` and
  the loop at `chatGoto.ts:304-308` never see an equal source.
- **A terminal record is never bound to a tab.** `bindable` needs `sameSource(record.source, asked.source)`, which is
  false. So `bindingTo` answers `undefined`; a LIVE terminal conversation is only revealed (`revealBound(panels, where,
  undefined)`) and stays under its terminal key; a STORED one is restored under a key of its own.
- **The name fallback can still OPEN one.** For an ambiguous Claude tab, `sameName` (`:373`) reads titles. A terminal
  conversation titled like that tab is listed, and it is opened by itself when it is the only one of that name and the
  three conditions of `:284` hold. It is opened, not bound. If the person then presses Ctrl+Alt+A on that Claude tab,
  the restored terminal conversation is in the registry under `{}` with that label — and the filter of § S2.7 is what
  stops the tab from taking it. T29 and T19 pin both halves.
- **Comments.** They said that a conversation cannot belong to a terminal. The doc comment of `'other'`
  (`chatGoto.ts:44`) and the comment in `goto` (`:235-236`) become:

```ts
  /**
   * The Output pane, a settings tab, our own chat tab, a terminal in the editor area — a TAB this command cannot start
   * from. A terminal CAN own a conversation (the terminal door, terminalSource.ts); go-to does not start from one
   * before stage 3 of that plan.
   */
  | 'other';
```

```ts
    // The Output pane, our own chat tab, a terminal in the editor area. The operator's decision: a tab with nothing
    // behind it gets the LIST, never an error and never silence. A terminal's own conversation is in that list.
```

## S2.11 — Disk, rollback and the older build

**What is written.** A terminal conversation is one record and one index entry, `version` 1, with
`"source":{"kind":"terminal","name":"<full name>"}` and `"fromSession":false`. Nothing else is new. A conversation from a
Claude tab or a file is written exactly as 0.85.0 writes it.

**What 0.85.0 does with it** (M1–M9): it keeps both files; it does not show the conversation in the picker or to go-to;
it does not overwrite, expire or sweep it; a chat tab restored from it shows the notice that a newer version wrote it;
installing this version again brings it back, with its age counted from its last use. It writes one console line per
terminal conversation on disk at each index refresh, and one per read of the record. No notification.

**The decision: E1 adds nothing for the older build.**

- The store was designed for this: a record a build cannot read is kept and never overwritten
  (`chatStoreFile.ts:71-81`, which names the downgrade). The measurement shows that the design holds for a new source kind.
- A version marker changes nothing. 0.85.0 already refuses the record at `sourceFrom`; a different `version` is refused
  one line earlier, with the same result and the same log lines.
- Moving `CONVERSATION_VERSION` for all records is worse: 0.85.0 then hides every conversation this build wrote.
- Writing the terminal as `none` plus a side field, so that 0.85.0 shows it, is refused: 0.85.0 drops unknown fields
  when it saves, so the terminal identity is lost on the first turn there; and it is not the record the plan approved.
- The console lines are the whole cost. They are at most one per terminal conversation of the last ninety days, in a
  state the person chose to be in for a short time.

**The other direction — a record E2 wrote, read by E1 after a rollback** — is decision 5: accepted, read as
`fromSession` false, no local session is read.

**Release note** (for the release-please pull request of this epic; the stories document's § 2 says where prose goes):

> **Going back to 0.85.0.** A conversation opened from a terminal is saved with a source that 0.85.0 does not know.
> 0.85.0 does not show it in the conversation list, and a chat tab restored from one says that a newer version wrote it.
> 0.85.0 does not delete, change or expire these files. Install this version again and the conversations are back.
> Conversations opened from a Claude Code tab or from a file are not affected.

**Tests of the decision:** T11 (the version did not move), T4 (the exact bytes of the source field, and `version` 1), T34
(a record whose source kind a build does not know is kept, hidden and never overwritten — the mechanism M2–M9, run on
the real store), and step 0 of § S2.14 (T34 with the kind `terminal` on the unchanged tree, which is 0.85.0).

## S2.12 — What S2 does not close

1. **A terminal whose name is not valid** (empty, over 500 units, or not one line) gets a conversation with source
   `none`. It has no road back (the *does not know where it came from* sentence) and the filter does not see it. A Claude
   tab can take it only when that tab's label equals the terminal's label exactly — an empty label, a label of more
   than 500 units, or one with a control character. Accepted.
2. **The recorded name is the name at the first press.** For a terminal whose title follows the running program, the
   road back after a reload can say *not open*, or show another terminal that wears the name now. It is on request and
   visible. Not measured here.
3. **The name fallback of go-to** can open a terminal conversation for an ambiguous Claude tab (§ S2.10).
4. **Whether `Terminal` objects are stable** when a terminal moves between the panel and the editor area is not
   measured. If the object changes, the name road still finds it. S2-H1 logs only that the object `createTerminal`
   returned is the one the window lists; R5 is the real check.

## S2.13 — Scenarios

Unit tests: `node --test` through `scripts/run-tests.mjs`, written like the files they join (`import
'../../scripts/inRunFolder.cjs';` first, `node:assert/strict`, test names that state the guarantee). No page changes, so
no page test. **RED-GREEN-RED for every row**: (1) the test is written first and seen RED for the symptom in the *first
RED* column — where the code does not exist yet, add the stub named there so the file compiles; (2) GREEN with the code;
(3) the break-it of § S2.13.6 is applied, the test is seen RED with its message, and the code is restored. All three
observations go in the summary. A row marked *pins* is green as soon as it compiles (some need the new kind to exist):
it has no first RED, and its break-it is its proof. T13 and T18 have no break-it: T13 is held by the type (a terminal
source has no `uri`), and T18 shows the defect itself.

**No new temp-folder line** (F12): T30–T34 use the `home()` their file already has.

### S2.13.1 — `src/test/chatStore.test.ts` (add)

`N = 'Creds: ai1-dev2 / small-tasks'`. `record(over)` is the file's helper.

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| T1 | a terminal source is its FULL name, and an empty name is no source | `sourceOfTerminal(N)` deep-equals `{ kind: 'terminal', name: N }` (the prefix is still there); `sourceOfTerminal('')` is `{ kind: 'none' }` | stub returns `none` → *a terminal conversation has no source to go back to* |
| T2 | a terminal name is at most 500 UTF-16 units, read from the one constant | `'x'.repeat(TERMINAL_NAME_MAX)` → terminal; one more → `none`. `G = '\u{10348}'` (one character, two units — assert `G.length === 2`): `G.repeat(250)` → terminal; `G.repeat(250) + 'x'` → `none`; `G.repeat(250) + 'x'.repeat(250)` (500 characters, 750 units) → `none` | no bound → *a name of 501 units became an identity* |
| T3 | a terminal name is one line | each of `'a\nb'`, `'a\rb'`, `'a\tb'`, `'a\u0000b'`, `'a\u001bb'`, `'a\u007fb'`, `'a\u0085b'`, `'a\u{2028}b'`, `'a\u{2029}b'` → `none`. Each of `N`, `' '`, `'имя / 名前 – “x”'`, `'a\u00a0b'` → a terminal source with that exact string | no rule → *a name carrying a newline became a durable identity* |
| T4 | a terminal record and its index entry round trip, at the version every record has | `r = record({ source: sourceOfTerminal(N), fromSession: false })`: `recordFrom(JSON.parse(JSON.stringify(r)))` deep-equals `r`; the same for `metaFrom` over `metaOf(r)`; `r.version === 1`; `JSON.stringify(r.source) === '{"kind":"terminal","name":"Creds: ai1-dev2 / small-tasks"}'` | no reader arm → `recordFrom` is `undefined`: *a terminal conversation could not be read back* (this is 0.85.0's answer, M1) |
| T5 | every kind of source the type names is read back | `SAMPLE: { readonly [K in ConversationSource['kind']]: Extract<ConversationSource, { kind: K }> }` with one value per kind; for each, `recordFrom(record({ source, fromSession: source.kind === 'claude' }))?.source` deep-equals it. A fifth kind does not compile here until it has a sample | (with T4) |
| T6 | a terminal record whose name the builder would refuse is dropped, not read as sourceless | for `source` in `{ kind: 'terminal' }`, `name: ''`, `name: 5`, `name: 'a\nb'`, `name: 'x'.repeat(501)`: `recordFrom({ ...r, source })` and `metaFrom({ ...metaOf(r), source })` are `undefined` | reader returns `sourceOfTerminal(row.name)` with no guard → *a damaged terminal record read as a conversation from nowhere* |
| T7 | a terminal record may say either thing about a session on disk, and reads as not from one | `recordFrom({ ...r, fromSession: true })` is defined and its `fromSession` is `false`; with `false`, and with the field absent: `false`. Unchanged for the old kinds: a `claude` record with `false` → `undefined`; a `file` record with `true` → `undefined`; a `none` record with `true` reads `true` | `agreeOnOrigin` unchanged → passes the first half by accident, so write the second assertion first: *a terminal conversation came back offering to read a LOCAL Claude session* |
| T8 | no terminal conversation reads a local session; every other kind says what it was told | `readsSession(sourceOfTerminal(N), true)` is `false`; `(none, true)` → `true`; `(none, false)` → `false`; `(sourceOfSession(id), true)` → `true`; `(sourceOfFile(u), false)` → `false` | stub returns its second argument → the first assertion |
| T9 | two terminal sources are one source only when the names are the same string | same `N` → `true`; `'Creds: A / s'` against `'Creds: a / s'` → `false`; `N` against `N + ' '` → `false`; `sourceOfTerminal('file:///a/README.md')` against `sourceOfFile('file:///a/README.md')` → `false`; `sourceOfTerminal(id)` against `sourceOfSession(id)` → `false`; against `none` → `false` | unchanged `sameSource` → *two conversations from one terminal name are not the same source* |
| T10 | a tab door's origin: a Claude tab names no source yet, a file tab names its document | `sourceOfTab(true, 'file:///x')` → `none`; `sourceOfTab(false, 'file:///x')` → `sourceOfFile('file:///x')`; `sourceOfTab(false, '')` → `none` | stub returns `none` → the second assertion |
| T11 | the store's version did not move (*pins*) | `assert.equal(CONVERSATION_VERSION, 1, 'moving the version hides EVERY conversation from 0.85.0 after a downgrade, not only the ones from a terminal')` | — |

### S2.13.2 — `src/test/chatSource.test.ts` (add) and `src/test/terminalSource.test.ts` (new)

`chatSource.test.ts`:

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| T12 | a terminal names no folder, so its conversation is filed under the fallback root | `spy` counts calls and returns its argument. `knownFolder(sourceOfTerminal('file:///D:/rsd/one/a.md'), spy)` is `''` and `spy` was not called (the name LOOKS like a file); `filedUnder('', ['D:\\rsd\\one'], 'D:\\rsd\\zero', true)` is `'D:\\rsd\\zero'`. Positive: for `sourceOfFile(u)` the spy is called once with `u` | does not compile until the arm exists; write the arm as `fsPath(source.name)` first → *a terminal's name was read as a path* |
| T13 | a rename never follows a terminal conversation, even one named like a file (*pins*) | `followable(sourceOfTerminal('file:///D:/rsd/one/a.md'))` is `false` | — |

`terminalSource.test.ts` (new; S1's tests stay in `terminalCapture.test.ts`). `tab(label)` and `panelFor` are copied
from `sessionKey.test.ts:15-24` in shape: a fresh key object per call.

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| T14 | the title drops “Creds: ” from the start only, and never leaves nothing | `'Creds: ai1-dev3 / small-tasks'` → `'ai1-dev3 / small-tasks'`; `'Creds: before deploy'` → `'before deploy'`; unchanged: `'Creds run: x'`, `'Creds VPN: x'`, `'my Creds: x'`, `'creds: x'`, `'Creds: '`, `'bash'`, `''` | stub returns the name → the first assertion |
| T15 | the same terminal is its own conversation; another terminal of the same name is a new one | keys `A`, `B` (two objects). `terminalMatch([{ key: A, label: 'h / s' }], A, 'Creds: h / s')` → `{ kind: 'existing', key: A, label: 'h / s' }`; with `B` → `{ kind: 'new', key: B, label: 'h / s' }`; with a restored panel `[{ key: {}, label: 'h / s' }]` and `B` → `new`; after a rename, `terminalMatch([{ key: A, label: 'h / s' }], A, 'other')` → `existing`, key `A`. No answer in the file has kind `rekey` | stub always `new` → the first assertion |
| T16 | two live terminals of one name are two conversations with equal sources | `terminalMatch([], A, n)` and `terminalMatch([{ key: A, label: n }], B, n)` are both `new` with keys `A` and `B`; `sameSource(sourceOfTerminal(n), sourceOfTerminal(n))` is `true` | (with T15, T9) |
| T17 | one live terminal of a name is found; none and several are said, and never picked | `live(key, name, exited = false)`. `[]` → `none`; `[a('x')]`, `'x'` → `{ kind: 'one', key: a }`; two of `'x'` → `{ kind: 'several', count: 2 }`; three → 3; `[a('x', exited), b('x')]` → `one`, key `b`; `[a('x', exited)]` → `none`; `[a('X')]`, `'x'` → `none` | stub returns `none` → the second assertion |
| T18 | without the filter a Claude tab of the same name takes the terminal’s conversation, and that is the point (*pins* the defect) | `claude = tab('main')`, `known = [{ key: T, label: 'main' }]` (T stands for a `Terminal`): `sourceSession(claude, [claude], known)` deep-equals `{ kind: 'rekey', from: T, key: claude.key, label: 'main' }` | — |
| T19 | a Claude tab named like a terminal conversation gets a NEW conversation, never that one | the same world, `sourceOf = (key) => (key === T ? sourceOfTerminal('main') : undefined)`: `sourceSession(claude, [claude], tabSourced(known, sourceOf))` deep-equals `{ kind: 'new', key: claude.key, label: 'main' }`. Again with the RESTORED form: the panel's key is `{}` and `sourceOf` answers the terminal source for it → `new` | stub `tabSourced = (known) => known` → `rekey`: *a Claude tab called “main” took the terminal’s conversation* |
| T20 | the filter takes out terminal conversations and nothing else — judged by the thread, read from the real registry | (a) `tabSourced`: panels with sources `claude`, `file`, `none` and one with no thread (`sourceOf` → `undefined`) all stay; an orphaned panel `main` with source `none` still re-keys to the Claude tab through the filter; with BOTH a terminal panel `main` and that orphan, the answer is `rekey` from the orphan. (b) `tabPanels` over a real `new ChatPanels()`: four entries opened with `panels.open(key, label, () => entry)` (entries built like `registeredChat` in `host/scenarios.ts:963`, their threads set with `threads.set(id, { source } as unknown as Thread)`): a terminal source under a key that stands for a `Terminal`; a terminal source under `{}` (the restored form); source `none` under `{}`; and one entry with NO thread. `tabPanels(panels)` deep-equals the last two, as `{ key, label }` | filter written as `=== 'claude'` only → the `none` and no-thread assertions of (a); `tabPanels` returning `panels.known()` → (b) |

### S2.13.3 — `src/test/chatReturn.test.ts` and `src/test/chatGoto.test.ts` (add)

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| T21 | with no live tab, a terminal source goes back to its terminal by its full name | `backTo({ liveTab: false, source: sourceOfTerminal(N) })` and `recordedRoute(sourceOfTerminal(N))` deep-equal `{ kind: 'terminal', name: N }` | arm written as `{ kind: 'nowhere' }` → *a chat from a terminal goes nowhere* |
| T22 | a Claude tab that wears the chat’s name is never taken for the terminal it came from (*pins*) | `all = [claudeTab('main'), fileTab]`: `recoveredTab(all, sourceOfTerminal('main'), 'main')` is `undefined`. Companion, same `all`: with source `none` it IS the Claude tab | — |
| T23 | the chat’s own terminal is shown, also when a namesake is open | `terminalRoad(A, [live(A, N), live(B, N)], N)` → `{ kind: 'show', key: A }` | stub goes by name only → *2 terminals are called …* where `show A` is expected |
| T24 | the chat’s own terminal is shown by its key after it was renamed | `terminalRoad(A, [live(A, 'other')], N)` → `show A` | (with T23) |
| T25 | a restored chat goes to the ONE live terminal of its recorded name | `terminalRoad({}, [live(B, N), live(C, 'x')], N)` → `show B` | stub refuses → the assertion |
| T26 | no live terminal of that name is said in the plan’s sentence | `terminalRoad({}, [live(C, 'x')], N)` → `{ kind: 'refuse', sentence: 'The terminal “Creds: ai1-dev2 / small-tasks” is not open.' }`; the chat's own terminal EXITED and nothing else → the same sentence; own exited and one live namesake `B` → `show B` | (with T25) |
| T27 | several terminals of one name are counted and never picked | `terminalRoad({}, [live(A, 'bash'), live(B, 'bash')], 'bash')` → sentence `'2 terminals are called “bash” — open the one you mean.'`; three → `'3 terminals …'` | (with T25) |
| T28 | a terminal conversation is never bound to a tab | `rec = { source: sourceOfTerminal(HERE), workspace: mine }` (the name EQUALS the tab's uri string): `bindable(rec, sourceOfFile(HERE), mine, true)`, `bindable(rec, { kind: 'none' }, mine, true)` and `bindable(rec, sourceOfSession(id), mine, true)` are all `false` | red only before the kind exists (compile); then *pins* |
| T29 | an ambiguous Claude tab named like a terminal conversation may OPEN it by name, and nothing more (*pins*) | `t = meta({ id: 'term', title: 'main', source: sourceOfTerminal('main') })`: `goto(asked({ tab: { kind: 'claude', label: 'main', path: '' }, ambiguous: true, source: { kind: 'none' }, inRoot: [t] }))` deep-equals `{ kind: 'reopen', meta: t }`; and `bindable` of that record against `{ kind: 'none' }` is `false`; `goto(asked({ tab: { kind: 'other', label: 'x', path: '' } }))` is `{ kind: 'everything' }` | — |

### S2.13.4 — The store on a real folder

| # | File | Name | Input → assertion | First RED |
|---|---|---|---|---|
| T30 | `chatStoreCache.test.ts` | go-to’s candidates never mix a terminal with a file of the same string | save three records: `f` with `sourceOfFile(U)`, `t1` and `t2` with `sourceOfTerminal(U)`; refresh. `index.bySource(sourceOfFile(U))` is `[f]`; `index.bySource(sourceOfTerminal(U))` holds `t1` and `t2`, newest first; `index.bySource({ kind: 'none' })` is `[]` | before the kind → the save is refused |
| T31 | `chatStoreRefile.test.ts` | a terminal conversation is saved, listed and read back | `store.save(record({ source: sourceOfTerminal(N), fromSession: false }), 0)` is `ok`; `store.read` gives the record, deep-equal; `store.listMeta()` has one row whose source is the terminal source | before the kind: `failed` — *not one this build could read back* |
| T32 | `chatStoreRefile.test.ts` | the store refuses to write a terminal record its reader would refuse | `store.save(record({ source: { kind: 'terminal', name: '' } }), 0)` is `failed` with the self-check's reason; the folder holds no file | reader accepts any string → `ok` |
| T33 | `chatStoreRefile.test.ts` | a file rename never refiles a terminal conversation (*pins*, once the kind exists) | stored: a record with `sourceOfTerminal(WAS)` (the name equals the old uri string). `store.refile('a1', sourceOfFile(WAS), sourceOfFile(NOW), 'D:\\rsd\\two')` is `{ kind: 'kept', why: 'moved on' }`; the record read back is unchanged | — (break-it B5: `followed`) |
| T34 | `chatStoreFile.test.ts` | a record whose source kind this build does not know is kept, hidden and never overwritten (*pins*) | write `a1.json` and `a1.meta.json` by hand, valid in every field but `source: { kind: UNKNOWN_KIND, name: 'x' }` with `UNKNOWN_KIND = 'a-kind-from-a-newer-build'`, `updatedAt` 200 days ago. `read` → `incompatible`; `listMeta` → no row; `entry('a1')` → `undefined`; `save(record({ id: 'a1' }), 0)` → `incompatible`; `retireIfExpired('a1', rev, now)` → `{ kind: 'kept', why: 'incompatible' }`; both files byte-identical at the end | — (step 0 runs it with `'terminal'` on the unchanged tree) |

### S2.13.5 — Wiring tests and host scenarios

Wiring tests read the source with `bodyOf` (`src/test/sourceReading.ts`), over ONE function body each. A whole file is
read only for an absence or a count (the last assertion of T35; T36 (b) and (c)).

| # | File | Name | Assertion | First RED |
|---|---|---|---|---|
| T35 | `chatSourceWiring.test.ts` — REWRITES the assertion at `:171` | a new conversation is given the origin its door built | in `bodyOf(command, 'async function deliverPassage(')`: matches `/source: origin,/u` and `/origin: ConversationSource,/u`, does not match `/sourceUri/u`. In `chatWithOtherAi`'s body: `/sourceOfTab\(match !== undefined, uri\)/u`. In `takeTheQuestion`'s body: `/sourceOfTab\(claude !== undefined, uri\)/u`. The whole file does not match `/fromSession \? \{ kind: 'none' \}/u` (the rule lives in `chatStore.ts` now; T10 runs it) | the old code → *a new conversation is not given its source* |
| T36 | `chatSourceWiring.test.ts` | every tab match goes through the one filtered road | (a) in `bodyOf(capture, 'export function tabMatch(')`: `/const known = tabPanels\(panels\);/u`, and `sourceSession(` occurs twice; (b) the scan — over `sourceFiles()`, outside `sessionKey.ts`, the text `sourceSession(` occurs in `chatCapture.ts` only, and there exactly twice (its companion is (a): the scan still finds the two known calls); (c) `chatCapture.ts` matches `/const \{ all, claude, matched \} = tabMatch\(panels\);/u` and holds no `panels.known()`; `askedForGoto`'s body contains `tabMatch(panels)`, and `chatSessionJoin.ts` holds no `panels.known()`. (`matchedSource`'s own signature ends in `} {`, so `bodyOf` cannot read its body — (c) reads the file for it) | the old code → (b) names `chatSessionJoin.ts` |
| T37 | `chatSourceWiring.test.ts` | a new conversation’s session flag is decided by its source | in `bodyOf(command, 'function newConversation(')`: `/const fromSession = readsSession\(state\.source, state\.fromSession\);/u`; `state.fromSession` occurs once; `/pinSession\(entry, state\.title, fromSession\);/u` | the old code → *a terminal conversation could be pinned to a LOCAL Claude session of its name* |
| T38 | `bugzReviewWiring.test.ts` — REWRITES the pattern at `:482` | (the test's name is unchanged) | in the `deliver` pattern, `false, false, fileUriOf\(chat\)\);` becomes `false, false, sourceOfFile\(fileUriOf\(chat\)\)\);` | the old code |
| T39 | `chatSourceWiring.test.ts` | the road back to a terminal uses the chat’s own key and one read of the window | in `toTerminal`'s body: `/const all = vscode\.window\.terminals;/u`, `/terminalRoad\(key, all\.map\(asLive\), name\)/u`, `/all\.find\(\(one\) => one === road\.key\)\?\.show\(\);/u`, and `vscode.window.terminals` occurs once; in `backToSource`'s body `/thread\.source, chat\.key\);/u`; `asLive` holds `/exited: one\.exitStatus !== undefined/u` | the old code → there is no `toTerminal` to read |

Host scenarios — in `src/test/host/scenarios.ts`, beside the four back-to-source scenarios (`:785-890`), because they
use that file's `registeredChat` (`:963`). They run only in CI's job `extension · a real editor` (`npm run test:host` cannot
start on this machine). Read that job's own result on the head commit; a red scenario blocks the merge.

- **S2-H1 — back to where a chat came from brings its terminal forward: its own, then by name, and never a guess.**
  `A = createTerminal({ name: 'Creds: h / s' })`, `B = createTerminal({ name: 'other' })`. Log whether
  `vscode.window.terminals.includes(A)`. `away()` is: `B.show()`, then poll (at most 5 s) until
  `vscode.window.activeTerminal === B`. It runs as the LAST thing before each `backToSource`, so the terminal in front
  is never the one the step expects.
  (1) `registeredChat(panels, A, 'host-back-to-terminal', sourceOfTerminal('Creds: h / s'))`; `away()`; `backToSource`
  → within 5 s `activeTerminal === A`. (2) a second registry, `registeredChat(panels2, {}, 'host-back-restored', the
  same source)`; `away()`; `backToSource` → `activeTerminal === A`. (3) `C = createTerminal({ name: 'Creds: h / s' })`;
  `away()`; `backToSource` on the restored chat → after a fixed 1 s `activeTerminal` is still `B`. (4) `away()`;
  `backToSource` on the chat of step 1 → `activeTerminal === A` (its own terminal, with the namesake open). Dispose
  all three terminals in `finally`.
  RED when `toTerminal` skips the own terminal (step 4) or picks the first namesake (step 3).
- **S2-H2 — a tab that looks like Claude’s and is named like a terminal conversation does not take it.**
  `panel = vscode.window.createWebviewPanel('claudeVSCodePanel', 'host-terminal-steal', vscode.ViewColumn.One, {})`;
  wait until the active tab's label is `host-terminal-steal`. Precondition, asserted with what was seen:
  `isClaudeSessionTab(snapshots().active)` — if the host reports another view type, the scenario FAILS and says which;
  report it, do not work around it. `T = createTerminal({ name: 'host-terminal-steal' })`.
  (1) `registeredChat(panels, T, 'host-terminal-steal', sourceOfTerminal('host-terminal-steal'))` (the label is the save
  id, so it equals the tab's): `matchedSource(panels).source` has kind `new` and its key is the tab, and `panels.get(T)`
  is still there. (2) restored form, a second registry keyed by `{}` with the same source → kind `new`. (3) the positive,
  a third registry keyed by `{}` with source `{ kind: 'none' }` → kind `rekey`: the scenario's tab really is matched by
  label, so (1) and (2) mean something. Dispose the panel and the terminal in `finally`.
  RED when `tabMatch` passes `panels.known()`: (1) and (2) answer `rekey`.

### S2.13.6 — Break-its (production changes; each seen RED with its message, then restored)

| # | Change | Goes red |
|---|---|---|
| B1 | `tabSourced` returns `known` | T19 — `rekey` where `new` is expected (the steal test of the parent's test plan) |
| B2 | `tabMatch` passes `panels.known()` to `sourceSession` | T36 (a) and (c); S2-H2 (1) and (2) in CI |
| B3 | `tabPanels` judges by the key: `sourceUnder` answers `undefined` for a key that has no `name` property | T20 (b) — the restored terminal conversation is still in the list; S2-H2 (2) in CI |
| B4 | `namedBy`: `case 'terminal': return 'terminal';` | T9 — two different names are one source |
| B5 | `sameSource` without `left.kind === right.kind` | T9 (terminal against file), T28 — a terminal conversation was bound to a tab, T33 — `followed` |
| B6 | `terminalRoad` without the `own` lookup | T23 — the *2 terminals are called …* sentence; T24 — *not open* |
| B7 | `oneLiveNamed` returns the first of several | T17, T27 |
| B8 | `oneLiveNamed` and `terminalRoad` without `!one.exited` | T17, T26 — an exited terminal is shown |
| B9 | `ORIGIN.terminal = 'must not'` | T7 — a terminal record a later build wrote was dropped |
| B10 | `readsSession` returns `fromSession` | T7, T8 |
| B11 | `pinSession(entry, state.title, state.fromSession)` in `newConversation` | T37 |
| B12 | `isTerminalName` without `!NOT_ONE_LINE.test(name)`; then with `<` for `<=`; then counting `[...name].length` | T3; T2 (500 refused); T2 (750 units accepted) |
| B13 | `sourceFrom`'s terminal arm returns `sourceOfTerminal(row.name)` for any string | T6, T32 |
| B14 | `terminalMatch` finds the panel by `panel.label === label` | T15 — a second terminal of the name got the first one's conversation |
| B15 | `terminalLabel` with `name.replace('Creds: ', '')` | T14 — `'my Creds: x'` |
| B16 | `noFolder`'s terminal case moved out, and `knownFolder` returns `fsPath(source.name)` for it | T12 |
| B17 | `namedRoute`: `case 'terminal': return { kind: 'nowhere' };` | T21 |
| B18 | `recoveredTab`: `source.kind !== 'file'` in place of `source.kind === 'none'` | T22 — a Claude tab was taken for the terminal |
| B19 | `CONVERSATION_VERSION = 2` | T11 (and most of the file — say so, restore) |
| B20 | `sameName` in `chatGoto.ts` leaves out rows whose source is `terminal` | T29 — the name fallback changed, which this story does not do |
| B21 | `probe` in `chatStoreFile.ts` answers `absent` for a file `recordFrom` refuses | T34 — the save over it is `ok`: a record this build cannot read was overwritten |

## S2.14 — The brief: order, files, commands

One coder, on `feat/chat-from-a-terminal`, on top of S1's commit `9ac886c9`. Each commit is green on its own (`npm run compile` exit 0,
the whole suite, lint). Stage by path; S1's and S3's files are not yours.

0. **Step 0, no commit — the measurement.** Write T34. Run it once with `UNKNOWN_KIND = 'terminal'` on the tree BEFORE
   any production change of S2: it must be GREEN (the store of that tree is 0.85.0's; it is M2–M9 in the repository). Put the runner's
   output in the summary. Set the constant back.
1. **`test(chat-store): what a build does with a source kind it does not know`** — T34, T11. No production change.
2. **`feat(chat-store): a terminal is a conversation source`** — the compiler decides what is in this commit: adding the
   kind breaks `knownFolder` and `recordedRoute` (their `never`), and the new `BackTo` arm breaks `fromRecord`.
   `chatStore.ts` (§ S2.2–S2.4), `chatSource.ts` and `terminalSource.ts` (§ S2.6), `chatReturn.ts` and
   `chatReturnCommand.ts` (§ S2.9), the doc comment in `chatThread.ts`. Tests T1–T10, T12–T28, T30–T33, T39.
   Order inside: the pure tests first, each RED, then the code.
3. **`fix(chat): a Claude tab can not take a terminal's conversation`** — `tabSourced` and `tabPanels` are already there
   from commit 2 with T18–T20; this commit is the road: `chatCapture.ts` and `chatSessionJoin.ts` (§ S2.7), T36.
4. **`refactor(chat): a door hands deliverPassage its origin`** — `chatCommand.ts` (§ S2.8); T35 and T38 (the two
   rewrites), T37.
5. **`docs(chat-goto): a terminal can own a conversation`** — the two comments (§ S2.10), T29.
6. **`test(host): the road back to a terminal, and the steal, in a real editor`** — S2-H1, S2-H2 in `scenarios.ts`.
7. **`docs(research): the terminal source`** — § S2.15.

Commands, from `src_vs_code/` (the stories document's § 3 has the full list):

```bash
npm run compile                                                # read the exit code
node scripts/run-tests.mjs out/test/chatStore.test.js          # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test    # the whole suite
npm run lint                                                   # eslint src
npx eslint src --prune-suppressions                            # after commit 2: chatStore.ts complexity 4 -> 3
node scripts/count-notifications.mjs --write                   # expect NO change: no new notify site
```

- `git diff eslint-suppressions.json` after the prune must show only numbers that fell, and only for `src/chatStore.ts`.
  A count that grows means a function is over the limit: split it, do not suppress.
- No new host module: `terminalSource.ts` stays without `vscode`; the host code goes in `chatCapture.ts` and
  `chatReturnCommand.ts`, which are host files already (`sonarExclusions.test.ts`).
- Do not touch: the capture (`fromTheTerminal`, `captureTerminalSelection`, `borrowClipboard`), the manifest, the door,
  `chatAccessRules.ts`, `chatDoors.ts`, the help — S1 and S3.
- From the worktree root, before any commit that touches `todo/`: `node .agents/conventions/tools/plan-lifecycle.mjs`.

## S2.15 — Documents (commit 7)

- `research/module_extension.md`, section *The chat opens from a terminal (E1)*: add the S2 part — the `terminal` kind
  and what a name may do (compare, find one live terminal; never re-key, never bind); the name rule and its reasons
  (decision 2); the name is recorded once; the key is the `Terminal` object; `readsSession` and why (F6); the steal and
  the one road `tabMatch`; the road back (own terminal, then the one of the name, the two sentences); go-to in E1; the
  rollback facts as the table M1–M9 with its date; the four residuals of § S2.12.
  In *Which conversation belongs to this tab (2026-09-13)*: one sentence at *"A source of `none` matches nothing"* — a
  fourth kind exists, a terminal's full name, and it is never bound to a tab; point to the E1 section.
  In *The chat goes back to where it came from (2026-09-24, issue #314)*: the fourth road, `Terminal.show()`.
- `research/module_tests.md`: extend the rows *WHICH CONVERSATION BELONGS TO THIS TAB* (T1–T13, T30–T34),
  *BACK TO WHERE IT CAME FROM* (T21–T27, T39, S2-H1) and *WHICH CONVERSATION IS THIS TAB’S* (T28, T29); add the tests of
  `terminalSource.test.ts` (T14–T20) and the wiring T35–T38 to the row S1 extended; add S2-H1 and S2-H2 to the
  real-editor row. **Not covered**, with the reason: `show()` in the panel and in the editor area on a real window (R5,
  the owner); the name of a terminal under title sequences (R4); a `Terminal` object's identity when the terminal moves;
  the steal with the real Claude Code extension (S2-H2 uses a webview of the same view type).
- `research/architecture.md` is S3's (one sentence on `Terminal.show()`); S2 does not edit it.

## S2.16 — What the Opus reviewer checks

1. `git diff` of `chatCapture.ts` holds only § S2.7; nothing of S1's capture changed.
2. `sourceSession(` is called in `tabMatch` only. `tabPanels` reads the thread's source through `sourceUnder`; no code
   asks whether a key is a terminal. `chatCapture.ts` gained no import but `tabPanels` and `SessionMatch`.
3. The four exhaustive places (`namedBy`, `noFolder`, `namedRoute`, `toRecorded`) end in a `never`; `ORIGIN` is typed by
   the kind. Add a fifth kind in a scratch edit: the compiler must name all five. No `default` returns a value in silence.
4. `isTerminalName` is the ONLY name rule: the builder and the reader call it; `500` is written once.
5. `recordFrom` drops a terminal record with a refused name (never `none`), accepts either `fromSession`, and returns
   `false`. `metaFrom` reads the same source. `CONVERSATION_VERSION` is 1.
6. In `newConversation`, the page flag, the thread's `fromSession` and `pinSession` all take `readsSession(...)`.
   Nothing new was added to `pinSession`, `resolveAndPin` or `adoptFound` (E2's).
7. `deliverPassage` has no `sourceUri`; its three callers pass an origin; `sourceOfTab` is the one copy of the tab rule.
   T35 and T38 were rewritten, not deleted, and each new pattern is matched against ONE function body.
8. `toTerminal` reads `vscode.window.terminals` once, shows by the key the decision returned, and calls `show()` with
   no argument. The two sentences equal the parent's text. `refuse` is the existing site; `notification-sites.json` is unchanged.
9. `terminalMatch` never compares a label or a name, and no answer is `rekey`.
10. No behaviour change in `chatGoto.ts`; the two comments say what is true.
11. Every scenario has its three observations in the summary, with the RED messages; step 0's output is there.
    Each break-it of § S2.13.6 was applied to the PRODUCTION line named.
12. No new temp-folder line in a test; `eslint-suppressions.json` only fell; no new module imports `vscode`.
13. The host scenarios are in `SCENARIOS`; the CI job's own result on the head commit is quoted.
14. The existing tests named in § S2.4 are green with no edit.

## Definition of Done — E1.S2

- [ ] T1–T39 green; each first RED and each break-it B1–B21 observed with its message; all in the summary.
- [ ] Step 0 run on the unchanged tree and its output recorded.
- [ ] `npm run compile` exit 0; the whole suite green; `npm run lint` green; the suppression count of `src/chatStore.ts`
      fell by one and no other count changed.
- [ ] `chatSourceWiring.test.ts:171` and `bugzReviewWiring.test.ts:482` rewritten as T35 and T38.
- [ ] S2-H1 and S2-H2 green in CI's `extension · a real editor` job on the head commit, read from that job.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated; the comments at `chatGoto.ts:44` and `:235` corrected.
- [ ] The release note of § S2.11 is carried into the release-please pull request of the epic.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
