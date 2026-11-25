// The chat registration (v2 E4, story s4-chat-carried, task t2): what the old `activate()` did for chat, cut out of `src/extension.ts` WITHOUT a change of
// logic — the chat store, its migration, housekeeping and orphan sweep, the rename follower, the chat commands and the `coaiChat` serializer. Only the place
// moved: it runs when the chat module first loads (`./index.ts`), not at start-up and not with the legacy module. The `chat*.ts` files it wires are untouched.
//
// What the old activation also did for chat and the legacy module STILL does for itself, because the page and the settings editor use it too:
// `chatReadsThisSide`, `bindChatSide` and `bindChatPresets` (each only stores the context in a module of that bundle's copy, which starts nothing).
// Everything that STARTS something — the store, the heartbeat, the sweep, the rename listener, the terminal listeners, the commands, the serializer —
// is here and nowhere else, so a window that loads both modules starts each once.
import { statSync } from 'node:fs';
import * as vscode from 'vscode';

import { ChatPanels } from '../../../src/chatPanels';
import {
  askFromTerminal,
  chatWithOtherAi,
  addFromTerminal,
  noteChatDoor,
  takeFromTerminal,
  takeTheQuestion,
} from '../../../src/chatCommand';
import { followRenames } from '../../../src/chatFollow';
import { heldConversationIds } from '../../../src/chatRegistry';
import { keepChatsIn, pulseChatsThrough, rememberChatsIn, retireMemento } from '../../../src/chatHost';
import { bindChatSide, chatReadsThisSide } from '../../../src/chatConfig';
import { bindChatPresets } from '../../../src/chatPresetsHost';
import { conversationWorkspace } from '../../../src/chatRoots';
import { forgetPickedConversation, switchConversations } from '../../../src/conversationPickerCommand';
import { goToConversation, goToFromTerminal } from '../../../src/chatGotoCommand';
import { backToSource } from '../../../src/chatReturnCommand';
import { ChatTabMemory } from '../../../src/chatTabs';
import { ChatStoreFile, conversationsDir } from '../../../src/chatStoreFile';
import { type Housekeeping, startHousekeeping } from '../../../src/chatStoreHousekeeping';
import { type ImportReport, describe as describeImport, importLegacyTabs, importSucceeded } from '../../../src/chatStoreImport';
import { type RestoreDeps, restoreAfterReload } from '../../../src/chatRestorePanel';
import { openLedger, reconcile } from '../../../src/chatOrphans';
import { coaiDataDir } from '../../../src/dataDir';
import { fileAt, useFileProbe } from '../../../src/pathFamily';
import { startPathWatch } from '../../../src/pathWatchHost';
import { hostRuntime } from '../../../src/hostRuntime';
import { storageReadsThisSide } from '../../../src/sideConfig';
import { bindTerminalAccounts, type TerminalAccounts } from '../../../src/remoteAccountsHost';
import { bindRemoteSessions, type RemoteSessions } from '../../../src/remoteSessionHost';
import { bindReattach } from '../../../src/terminalHost';
import { bindSkipShell, type SkipShellTestDoor } from '../../../src/skipShellHost';
import { asText } from '../../../src/asText';
import { notify } from '../../../src/notify';
import * as bridge from '../../../src/shellBridge';
import { mark } from '../../measure';

/** What the chat registration made, kept by the chat module: the registry the legacy sidebar's bug chat goes through, and what teardown stops. */
export interface ChatRegistration {
  readonly chatPanels: ChatPanels;
  readonly remote: RemoteSessions;
  /** The folder of this side's storage that holds the refusals file: named in ONE place, handed to the road and to its test door. */
  readonly hostsFolder: string;
  /** The accounts of the open ssh hosts (accounts plan E1.S3), bound beside the remote sessions. */
  readonly terminalAccounts: TerminalAccounts;
  readonly skipShell: SkipShellTestDoor;
  /** Stops the heartbeat's timer (its FILE is left on purpose — see `chatStoreHeartbeat.ts`); a second call does nothing more. */
  readonly stopHeartbeat: () => void;
}

/** What the chat commands and the serializer reach, made once by {@link openTheStore}. */
interface OpenedStore {
  readonly chatStore: ChatStoreFile;
  readonly restoreDeps: RestoreDeps;
  readonly migration: Promise<ImportReport>;
}

/** Where the chat's own disposals and registrations go: the `vscode.ExtensionContext` of the host, as the old `activate` took it. */
export function registerChat(context: vscode.ExtensionContext): ChatRegistration {
  const { remote, hostsFolder, terminalAccounts, skipShell, chatPanels } = bindThisSide(context);
  const { chatStore, restoreDeps, migration } = openTheStore(context, chatPanels);
  // HOUSEKEEPING, and the INDEX the picker reads (story B1), then the follower of renames and the sweep of what the last run left behind.
  const housekeeping = startKeeping(chatPanels, chatStore, migration);
  followAndSweep(context, chatPanels, housekeeping);
  let heartbeatStopped = false;
  const stopHeartbeat = (): void => {
    if (!heartbeatStopped) {
      heartbeatStopped = true;
      housekeeping.dispose();
    }
  };
  context.subscriptions.push(
    // The heartbeat's timer. Its FILE is left on purpose — see `chatStoreHeartbeat.ts`. Stopped once, whether by the host's disposal or by `deactivate`.
    { dispose: stopHeartbeat },
    ...registerFileDoors(context, chatPanels),
    ...registerQuestionDoors(context, chatPanels),
    ...registerTerminalQuestionDoors(chatPanels, context),
    ...registerGoToDoors(context, chatPanels, chatStore, housekeeping),
    ...registerPickerCommands(context, chatPanels, chatStore, housekeeping),
    // Deactivation is not a tab closing: nobody has told VS Code about these panels, so both the
    // panel and the vendor process behind it have to be ended here or they outlive the extension.
    { dispose: () => chatPanels.closeAll() },
    registerSerializer(restoreDeps, migration),
  );

  return { chatPanels, remote, hostsFolder, terminalAccounts, skipShell, stopHeartbeat };
}

/** The bindings the old `activate` made for chat, in its order: the data root, the one path watch, the side, the remote road, the skip-list check, the registry. */
function bindThisSide(context: vscode.ExtensionContext): { readonly remote: RemoteSessions; readonly hostsFolder: string; readonly terminalAccounts: TerminalAccounts; readonly skipShell: SkipShellTestDoor; readonly chatPanels: ChatPanels } {
  // WHERE this window keeps its data, before anything below resolves a path (the chat store is built from one); the host's one runtime holds the answer, so
  // the legacy module, loaded or not, reads the same. And how this host asks the disk whether a CLI path of the other OS's spelling is a file HERE.
  storageReadsThisSide(context);
  useFileProbe(fileAt(statSync));
  // The paths of THIS side, asked of the disk and said when missing: ONE watch per host, whichever module starts it first, because the storage guard every
  // chat writer asks reads its findings (`findingsBoard.ts`, held in the shared runtime) and a chat-only window must refuse to create a missing named root too.
  const pathWatch = hostRuntime().once('pathWatch', () => startPathWatch(context));
  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration((e) => {
    if (e.affectsConfiguration('coai')) {
      // Re-read WHERE, and ask the disk again, as the old activation's listener did; the legacy module's listener (when loaded) does the same, harmlessly.
      storageReadsThisSide(context);
      pathWatch.forget();
    }
  }));
  // FIRST, before anything is constructed and long before a command can be invoked: the side whose settings the chat reads (`chatWiring.test.ts` holds the order).
  chatReadsThisSide(context);
  // The chat reads this side's catalog rows (E4.6a).
  bindChatSide(context);
  bindChatPresets(context);
  // The road to a `Creds: <host> / <session>` terminal's Claude session (E2.S3): the consent, the picked ids, the hosts command.
  // The refusals file lives in a folder of this side's storage: the ONE place that names it, handed on to the road and to the test door.
  const hostsFolder = vscode.Uri.joinPath(context.globalStorageUri, 'terminal-hosts').fsPath;
  const remote = bindRemoteSessions(context, hostsFolder);
  // The accounts of the open ssh hosts (E1.S3): a round 30 s after the first eligible terminal, then 10 minutes after each one ended. In test mode it never arms itself.
  const terminalAccounts = bindTerminalAccounts(context, remote, context.extensionMode === vscode.ExtensionMode.Test);
  // The person's own skip list may hide the terminal keys of CoAI (E3.S3): tell once, offer Add for the user list, never write a workspace.
  const skipShell = bindSkipShell(context);
  // One registry per window: a conversation belongs to a Claude Code tab, and tabs are per window. The legacy sidebar's bug chat reaches it through the chat module.
  const chatPanels = new ChatPanels();
  // A terminal that opens (creds opens its terminals late, after the serializer restored the chats) takes back the conversation of its name (E3.S1).
  bindReattach(context, chatPanels);

  return { remote, hostsFolder, terminalAccounts, skipShell, chatPanels };
}

/** The chat store, and the migration that carries the old memento into it: what a restored tab reads and every command writes. */
function openTheStore(context: vscode.ExtensionContext, chatPanels: ChatPanels): OpenedStore {
  // Where conversations are kept so a window reload does not empty every chat tab: the store on
  // disk, one file per conversation beside the two chat ledgers, under the same data directory the
  // MCP server uses. It is the source of truth — the serializer below reads it.
  const chatStore = new ChatStoreFile(conversationsDir(coaiDataDir()));
  // And the MEMENTO every build before this one wrote — `workspaceState`, because a chat tab belongs
  // to THIS workspace. It is carried into the store ONCE, below, and until that migration has
  // confirmed every record is on disk the chat writes BOTH: a store that cannot be reached must not
  // leave a person's next words written nowhere. `retireMemento` is what ends the dual write, and
  // only a successful migration calls it. Nothing prunes the memento any more: nothing may shrink it
  // before the migration has read it, and the store's own retention is story B1's sweep.
  const chatTabMemory = new ChatTabMemory(context.workspaceState);
  rememberChatsIn(chatTabMemory);
  keepChatsIn(chatStore);
  mark('chatStore');
  const restoreDeps: RestoreDeps = {
    panels: chatPanels,
    store: chatStore,
    memento: chatTabMemory,
    extensionUri: context.extensionUri,
    workspace: conversationWorkspace,
  };
  const migration = migrate(context, chatStore, chatTabMemory);

  return { chatStore, restoreDeps, migration };
}

/** The migration (`chatStoreImport.ts`), started before anything can read the store. */
function migrate(context: vscode.ExtensionContext, chatStore: ChatStoreFile, chatTabMemory: ChatTabMemory): Promise<ImportReport> {
  // THE MIGRATION, started before anything can read the store and waited for by the serializer — VS
  // Code deserializes panels during activation, and a conversation not yet carried across would read
  // as absent and be disposed on the first reload after the upgrade. It compares transcripts, never
  // ids, and empties the key only once every record is confirmed and indexed; `chatStoreImport.ts`
  // says why each of those rules exists. It never throws, so the catch below is the outer edge of a
  // detached call and a defect if it ever speaks — and it re-binds the memento, because a defect
  // after the seal would otherwise leave this window writing to one store while the other is in charge.
  return importLegacyTabs({
    memento: context.workspaceState,
    store: chatStore,
    workspace: conversationWorkspace(),
    // THE SEAL — the one place the memento is retired. Unbind first, so nothing new can queue a
    // write; then drain the queue, so a write issued a moment before the clear cannot land after it
    // and fill the key again (gemini, A4's code round). The migration calls it only when the key is
    // empty or every record is confirmed, immediately before the clear.
    seal: async () => {
      retireMemento();
      await chatTabMemory.settled();
    },
    // And the way back, when the clear did not happen after all: the memento is still in charge, so
    // it is written again.
    unseal: () => {
      rememberChatsIn(chatTabMemory);
    },
  })
    .then((report) => {
      (importSucceeded(report) ? console.info : console.warn)(describeImport(report));

      return report;
    })
    .catch((reason: unknown): ImportReport => {
      console.error('ConnectOtherAIs: the chat conversation migration threw; the old store stays in charge', reason);
      rememberChatsIn(chatTabMemory);

      return { kind: 'incomplete', fates: [], reason: 'the migration threw' };
    });
}

/** The sweep after the migration, the index after the sweep, the heartbeat from the first moment: `chatStoreHousekeeping.ts` says why each order holds. */
function startKeeping(chatPanels: ChatPanels, chatStore: ChatStoreFile, migration: Promise<ImportReport>): Housekeeping {
  // sweep after the migration, the index published after the sweep. `chatStoreHousekeeping.ts` says
  // why each order holds; what stays here is the wiring — the keeper bound to the store's own directory
  // through the store itself, the heartbeat fed by the registry, and the chat pulsing it whenever the
  // set of open conversations changes. Its `ready` catches its own defects, so nothing is left unhandled here.
  const housekeeping = startHousekeeping({
    store: chatStore,
    held: () => heldConversationIds(chatPanels),
    after: migration,
  });
  pulseChatsThrough(() => {
    housekeeping.heartbeat.pulse();
  });
  mark('chatHousekeeping');

  return housekeeping;
}

/** The rename follower and the sweep of what the LAST run left behind. */
function followAndSweep(context: vscode.ExtensionContext, chatPanels: ChatPanels, housekeeping: Housekeeping): void {
  // A FILE THAT MOVES takes its conversation with it. `onDidRenameFiles` is the only event that
  // carries an explicit old-to-new mapping, which is why it is the one followed: saving an untitled
  // buffer reports no previous uri at all, so a conversation opened from one keeps its `untitled:`
  // source and is found in the picker instead — `chatSource.ts` says why guessing there would be
  // worse than not following. The subscription is pushed so a reload takes it down with everything
  // else; the work itself is detached and catches its own edge.
  context.subscriptions.push(vscode.workspace.onDidRenameFiles((moved) => {
    followRenames(chatPanels, housekeeping.index, moved.files.map((one) => ({
      from: one.oldUri.fsPath,
      to: one.newUri.fsPath,
    })));
  }));
  // Bound once, and never asked for again: an extension host has one storage directory for its
  // whole life, and threading it through six functions paired a per-call path with a module-level
  // list of children — two things that must agree, with nothing making them.
  openLedger(context.globalStorageUri.fsPath);
  // What the LAST run left behind. Deactivation ends every chat child, and so does closing a tab —
  // but neither runs when the editor is force-killed, and what survives that is a vendor CLI signed
  // in as the person with nobody to stop it. Every candidate is re-identified before anything is
  // killed; see `chatLedger.ts`, which is where that judgement lives.
  void reconcile(context.globalStorageUri.fsPath)
    .then((ended) => {
      for (const one of ended) {
        // Named rather than counted: a line saying "ended 1 process" is the one thing nobody can act
        // on if it was ever the wrong one.
        console.warn(`[coai] ended a chat process left by a previous session: ${one}`);
      }
    })
    // The outer edge of a detached call ends in a catch that says something — `reliability.md`, and
    // an unhandled rejection during activation is a defect this repository has already paid for.
    .catch((reason: unknown) => {
      console.warn(`[coai] the chat orphan sweep failed: ${reason instanceof Error ? reason.message : reason}`);
    });
}

/** The doors a selection, a menu item or a chord reaches: each records its door FIRST. */
function registerFileDoors(context: vscode.ExtensionContext, chatPanels: ChatPanels): vscode.Disposable[] {
  return [
    // Chat with another vendor about a passage. Two doors reach it — this keybinding and the
    // 'Chat with other AI' item in Claude Code's own right-click menu — and the command tells them
    // apart by what VS Code hands it, because only one of them can copy the selection itself.
    bridge.registerCommand('coai.chatWithOtherAi', (...args: unknown[]) => {
      // RECORDED FIRST, in every one of these. The count on the spending page is of how often
      // the chat was reached for, so a door that then refuses — no CLI, nothing captured — is still
      // one of them. Nothing waits for the write.
      noteChatDoor('key');
      void chatWithOtherAi(chatPanels, context.extensionUri, args);
    }),
    // The terminal's own door: the icon in the terminal panel's title bar and Ctrl+Alt+A typed in a terminal. A terminal is
    // not a tab, so it has its own command and never goes through the file door's tab match. RECORDED FIRST, like the rest.
    // The rejection is caught for the same reason `coai.takeTheQuestion`'s is: a press that does nothing explains nothing.
    bridge.registerCommand('coai.askFromTerminal', () => {
      noteChatDoor('terminal');
      askFromTerminal(chatPanels, context.extensionUri, vscode.window.activeTerminal).catch((reason: unknown) => {
        console.error('coai.askFromTerminal failed', reason);
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'askFromTerminal',
          code: 'terminal-door-failed',
          title: 'The copied text could not be sent to a chat.',
          detail: asText(reason),
        });
      });
    }),
    // The two menu items, which differ from the chord in one way: each SAYS what it will do, so
    // neither reads `coai.chatAutoSend`. An item whose behaviour depends on a setting in another
    // window is an item nobody can predict from its own label.
    bridge.registerCommand('coai.chatNow', (...args: unknown[]) => {
      noteChatDoor('default');
      void chatWithOtherAi(chatPanels, context.extensionUri, args, true);
    }),
    bridge.registerCommand('coai.chatChoose', (...args: unknown[]) => {
      noteChatDoor('choose');
      void chatWithOtherAi(chatPanels, context.extensionUri, args, false);
    }),
  ];
}

/** The question Claude Code is asking, taken or added: its own door, because the passage comes from Claude's session file. */
function registerQuestionDoors(context: vscode.ExtensionContext, chatPanels: ChatPanels): vscode.Disposable[] {
  return [
    // Take the question Claude Code is asking and hand it to a second model. Its own door, because
    // the passage comes from Claude's session file rather than from a selection — that box cannot
    // be selected, which is why a screenshot was the only way before this.
    //
    // The rejection is CAUGHT rather than dropped: a command that fails silently at the activation
    // boundary is a keypress that does nothing and explains nothing. (CodeRabbit, PR #206.)
    bridge.registerCommand('coai.takeTheQuestion', () => {
      noteChatDoor('take');
      takeTheQuestion(chatPanels, context.extensionUri).catch((reason: unknown) => {
        // The DETAIL to the console, a short sentence to the person. A stack or a filesystem path in
        // a toast is neither readable nor theirs to act on, and the path rule says so; dropping it
        // altogether would leave a keypress that does nothing and explains nothing. (CodeRabbit.)
        console.error('coai.takeTheQuestion failed', reason);
        // The short sentence still goes to the person and the detail still goes to the console —
        // neither changes. What changes is that the detail is also WRITTEN DOWN, so the next
        // person to ask "why did that key press do nothing" has somewhere to look.
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'takeTheQuestion',
          code: 'question-not-taken',
          title: 'The question could not be taken.',
          detail: asText(reason),
        });
      });
    }),
    // The same reader, the other verb: ADDED to what the composer already holds rather than put in
    // its place. Asked for after using the pair — `choose` had just composed a turn and `take` threw
    // it away, when what was wanted was the question underneath it as more material.
    bridge.registerCommand('coai.addTheQuestion', () => {
      noteChatDoor('add');
      takeTheQuestion(chatPanels, context.extensionUri, true).catch((reason: unknown) => {
        console.error('coai.addTheQuestion failed', reason);
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'addTheQuestion',
          code: 'question-not-added',
          title: 'The question could not be added.',
          detail: asText(reason),
        });
      });
    }),
  ];
}

/** The same two verbs for a `Creds: <host> / <session>` terminal. */
function registerTerminalQuestionDoors(chatPanels: ChatPanels, context: vscode.ExtensionContext): vscode.Disposable[] {
  return [
    // The same two verbs for a `Creds: <host> / <session>` terminal: the question is read off the Claude session on that machine over ssh
    // (the road of `remoteSessionHost`), and the items sit in the terminal panel's overflow while the terminal reads a session. Each
    // records its door FIRST. THE CATCH LOGS A FIXED SENTENCE and passes no error and no message on: a V8 `JSON.parse` error quotes its
    // input, and here the input is the transcript.
    bridge.registerCommand('coai.takeFromTerminal', () => {
      noteChatDoor('take');
      takeFromTerminal(chatPanels, context.extensionUri, vscode.window.activeTerminal, false).catch(() => {
        console.error('coai.takeFromTerminal failed');
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'takeFromTerminal',
          code: 'question-not-taken-from-terminal',
          title: 'The question could not be taken from the terminal.',
        });
      });
    }),
    bridge.registerCommand('coai.addFromTerminal', () => {
      noteChatDoor('add');
      addFromTerminal(chatPanels, context.extensionUri, vscode.window.activeTerminal).catch(() => {
        console.error('coai.addFromTerminal failed');
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'addFromTerminal',
          code: 'question-not-added-from-terminal',
          title: 'The question could not be added from the terminal.',
        });
      });
    }),
  ];
}

/** Go to the conversation a tab, or a terminal, already has. */
function registerGoToDoors(context: vscode.ExtensionContext, chatPanels: ChatPanels, chatStore: ChatStoreFile, housekeeping: Housekeeping): vscode.Disposable[] {
  return [
    // The list of conversations — open and closed, this folder or every folder. It opens no chat by
    // itself: what it opens is a choice, and the choice may be to reveal a tab that is already there.
    // It is a DOOR all the same, because *Opened* counts the chat being reached for and this is one
    // of the ways it is reached for; the ledger's own note says a door that then refuses still counts.
    //
    // The index it draws from is the housekeeping's, published after the sweep — never a listing taken
    // here, which at ninety days of conversations is thousands of files on the way to the first frame.
    // GO TO the conversation this tab already has — story C3, and the other half of what the
    // operator asked for: the list above is searched by hand, this one is pressed on the tab you are
    // working in. Its decision is `chatGoto.ts`; everything here is the registration and the door.
    // Detached with a catch of its own, like every command that awaits: the store answers in
    // outcomes and never rejects, so anything arriving there is a defect worth a line.
    bridge.registerCommand('coai.goToConversation', () => {
      noteChatDoor('goto');
      void goToConversation(chatPanels, housekeeping.index, chatStore, context.extensionUri)
        .catch((reason: unknown) => {
          console.error('ConnectOtherAIs: going to a conversation threw', reason);
          void notify({
            as: 'warning',
            class: 'failure',
            source: 'goToConversation',
            code: 'conversation-not-opened',
            title: 'That conversation could not be opened.',
            detail: asText(reason),
          });
        });
    }),
    // GO TO from a terminal (E3.S2): the same go-to, pressed IN a terminal, for THAT terminal — a command of its own because
    // `coai.goToConversation` reads the active TAB and its chord keeps `!terminalFocus`. It is a door like the others, so it is
    // recorded FIRST; it reads the active terminal as `coai.askFromTerminal` does and never goes through the tab match.
    bridge.registerCommand('coai.goToFromTerminal', () => {
      noteChatDoor('goto');
      void goToFromTerminal(chatPanels, housekeeping.index, chatStore, context.extensionUri, vscode.window.activeTerminal)
        .catch((reason: unknown) => {
          console.error('ConnectOtherAIs: going to a terminal conversation threw', reason);
          void notify({
            as: 'warning',
            class: 'failure',
            source: 'goToConversation',
            code: 'conversation-not-opened',
            title: 'That conversation could not be opened.',
            detail: asText(reason),
          });
        });
    }),
  ];
}

/** The road back, the list of conversations, and forgetting a row of that list. */
function registerPickerCommands(context: vscode.ExtensionContext, chatPanels: ChatPanels, chatStore: ChatStoreFile, housekeeping: Housekeeping): vscode.Disposable[] {
  return [
    // The road back (issue #314): from a coai chat to the tab, session or file it came from. NOT a
    // chat door — it opens no conversation — so nothing is recorded for it. Its decision is
    // `chatReturn.ts`; a right-click hands it the page's conversation id, the chord hands it nothing.
    bridge.registerCommand('coai.backToSource', (asked?: unknown) => {
      void backToSource(chatPanels, asked)
        .catch((reason: unknown) => {
          console.error('ConnectOtherAIs: going back from a chat threw', reason);
          void notify({
            as: 'warning',
            class: 'failure',
            source: 'backToSource',
            code: 'chat-back-failed',
            title: 'CoAI could not go back to where this chat came from.',
            detail: asText(reason),
          });
        });
    }),
    bridge.registerCommand('coai.switchConversations', () => {
      noteChatDoor('switch');
      switchConversations(chatPanels, {
        index: housekeeping.index,
        store: chatStore,
        extensionUri: context.extensionUri,
        workspace: conversationWorkspace,
      });
    }),
    // And forgetting the row under the cursor of that list. NOT a door: it opens no chat, it acts on
    // the list — which is why it records no invocation, and why `chatWiring.test.ts` excludes a
    // keybinding scoped to `coai.conversationsPickerOpen` from the doors it derives from the manifest.
    // It takes no arguments because a keybinding passes none: the row it means is the one being
    // looked at, and only the picker knows which that is.
    bridge.registerCommand('coai.forgetPickedConversation', () => {
      forgetPickedConversation();
    }),
  ];
}

/**
 * How a chat tab comes back after a window reload. VS Code hands back the panel and whatever the
 * page saved with `setState` — here, the conversation's own id — and `chatRestorePanel.ts` does
 * the rest: validates the id, draws *Restoring…* before anything is awaited, waits for the
 * migration under a ceiling so a record that was still in the memento a moment ago is found, and
 * reads the STORE. The store answers four ways and `chatRestore.ts` decides each: a record
 * restores; absent — in the store AND in the memento — disposes, because an empty tab pretending
 * to be a conversation is worse than none; a record this build cannot read, or a disk that would
 * not answer, keeps the tab and SAYS so, with a retry where one makes sense. Disposing over either
 * of those would throw a person's tab away over a permissions error or a downgrade.
 */
function registerSerializer(restoreDeps: RestoreDeps, migration: Promise<ImportReport>): vscode.Disposable {
  return bridge.registerWebviewPanelSerializer('coaiChat', {
    deserializeWebviewPanel: (panel: vscode.WebviewPanel, state: unknown) =>
      restoreAfterReload(restoreDeps, panel, state, migration).catch((reason: unknown) => {
        // The store answers in outcomes and never rejects, so anything here is a defect — said on
        // the console rather than dropped, and the panel is NOT disposed over it: the rule of this
        // whole serializer is that a tab is thrown away only for a conversation that is nowhere.
        console.error('ConnectOtherAIs: a chat tab could not be restored after a reload', reason);
      }),
  });
}
