import { randomUUID } from 'node:crypto';
import * as fsp from 'node:fs/promises';
import * as path from 'node:path';
import * as vscode from 'vscode';

import { usersPanel } from './bugsKeysPanel';
import { openChatPresets, presetsReadDiscoveriesFrom } from './chatPresetsPanel';
import { askWhereDataLives, deleteTheOldDataFolder, moveDataDirectory } from './dataCommands';
import { openPhrases } from './phrasesPanel';
import { openRoles } from './rolesPanel';
import { ChatPanels } from './chatPanels';
import {
  chatWithOtherAi,
  noteChatDoor,
  takeTheQuestion,
} from './chatCommand';
import { followRenames } from './chatFollow';
import { heldConversationIds } from './chatRegistry';
import { keepChatsIn, pulseChatsThrough, rememberChatsIn, retireMemento } from './chatHost';
import { chatReadsThisSide } from './chatConfig';
import { conversationWorkspace } from './chatRoots';
import { forgetPickedConversation, switchConversations } from './conversationPickerCommand';
import { goToConversation } from './chatGotoCommand';
import { ChatTabMemory } from './chatTabs';
import { ChatStoreFile, conversationsDir } from './chatStoreFile';
import { startHousekeeping } from './chatStoreHousekeeping';
import { ImportReport, describe as describeImport, importLegacyTabs, importSucceeded } from './chatStoreImport';
import { RestoreDeps, restoreAfterReload } from './chatRestorePanel';
import { openLedger, reconcile } from './chatOrphans';
import { coaiDataDir, DATA_TO_MOVE, serverEnv } from './dataDir';
import { installFailureHint, SingleFlight } from './coaiInstall';
import { claudeSnippet, copiedMessage } from './claudeSnippet';
import { pastedSnippetStatus } from './snippetInWorkspace';
import { clientTargetsLine, CLIENT_TARGETS, installedMessage, mcpServerBlock } from './mcpBlock';
import { installedVersion, installLatest, latestServerVersion, serverExists, serverOnThisSide, serverPath } from './installer';
import { EscalationWatcher } from './escalationWatcher';
import { ConsultationWatcher } from './consultationWatcher';
import { PanelProvider } from './panelProvider';
import { showHelp } from './helpPanel';
import { parseSession, SessionFile } from './rounds';
import { blindSpotsHtml, chatRows, LogRow, mergedRows, rowsFrom } from './roundsLog';
import { ASK_ABOVE, ExportOutcome, ExportPorts, oneAtATime, readAndExport } from './roundsExport';
import { ExportableRow } from './roundsCsv';
import { asText } from './asText';
import { writeFileAtomically } from './atomicFile';
import { notify, notifyAndAsk, notifyThen } from './notify';
import { DbLog } from './roundsDb';
import { readLog, serverRunAt } from './roundsDbRead';
import { StorageFingerprint } from './dataMove';
import { flushChatUsage } from './chatUsageFile';
import { RoundsLogPanel } from './roundsLogPanel';
import { ExistingFile, ServerSettingsSync } from './serverSettingsSync';
import { LOCK_STALE_AFTER_MS, lockIsStale } from './settingsLock';
import { ConfigReader, settingsFrom } from './settingsShape';
import { readerFor, storageReadsThisSide } from './sideConfig';
import { vendorsFrom } from './vendors';

/**
 * ConnectOtherAIs — the human surface. Five commands, one directory watcher, and no port: the
 * review itself lives in `coai-mcp`, which an MCP client owns and starts.
 *
 * <p>The restraint is deliberate and inherited from CredsForDevs: the config block is OFFERED on
 * the clipboard, never written into another program's file; the binary goes into extension
 * storage, never onto the `PATH`. The one thing the server needs to reach us for — a question for
 * a person — arrives as a FILE in the data directory this extension already reads, which is why
 * there is still nothing listening on a socket.</p>
 */
export function activate(context: vscode.ExtensionContext): void {
  // BEFORE even that: WHERE this window keeps its data. Every line below that resolves a path — the
  // two watchers immediately after this, the chat store, the panel — asks `dataDir.ts`, and until
  // this has run it answers the DEFAULT directory. A window that read the choice late would watch
  // the wrong directory for escalations and write a Team-server token where nothing reads it.
  storageReadsThisSide(context);
  // FIRST, before anything is constructed and long before a command can be invoked: the side whose
  // settings the chat reads. Its reader falls back to the shared configuration while unbound, which
  // is the behaviour this branch exists to end — so the window in which that fallback could be
  // reached is closed by ordering rather than argued about. Four reviewers raised it in one round,
  // and `chatWiring.test.ts` now fails if this line ever drifts below a `registerCommand`.
  chatReadsThisSide(context);
  presetsReadDiscoveriesFrom(context);
  const watcher = new EscalationWatcher(dataDir());
  // The second watcher, and it asks for nothing from anybody: a consultation blocks nothing, so it
  // has no modal and no status-bar item — it appears in the sidebar where a person is already
  // looking, and nowhere else.
  const consultations = new ConsultationWatcher(dataDir());

  // Exports run one at a time: two dialogs answered with the same path would otherwise race,
  // and the file would hold whichever write finished last while both reported success.
  const exportQueue = oneAtATime();
  // Declared before the panel so the hooks can reach it; assigned right after.
  let roundsLog: RoundsLogPanel;
  let panelRef: PanelProvider;
  roundsLog = new RoundsLogPanel({
    onAnswer: async (id) => {
      const question = watcher.openQuestions.find((q) => q.id === id);
      if (question !== undefined) {
        await watcher.answerCommand(question);
      }
    },
    // The spending tab's two commands go to the sidebar's provider, which owns the window choice,
    // the price cache and the "forget" marks — one owner, whichever surface shows the numbers.
    onUsageWindow: async (window) => {
      panelRef.setUsageWindow(window);
      await refreshRoundsLog(roundsLog, watcher, panelRef, true);
    },
    onForget: async (provider) => {
      await panelRef.forgetUsage(provider);
      await refreshRoundsLog(roundsLog, watcher, panelRef, true);
    },
    // The log is where every consultation is, including the ones that have lapsed out of the
    // sidebar — so this is the only surface from which the state the issue describes can be ended.
    onCloseConsultation: async (id) => {
      await panelRef.closeConsultation(id);
      await refreshRoundsLog(roundsLog, watcher, panelRef, true);
    },
    onForgetChat: async (provider, model) => {
      await panelRef.forgetChatUsage(provider, model);
      await refreshRoundsLog(roundsLog, watcher, panelRef, true);
    },
    // A row was opened. The list no longer carries findings — 3.78 MB of a 3.83 MB payload, for
    // rounds nobody had opened — so this is the read that replaces them, for the one round clicked.
    // The round's identity arrives WITH the request rather than being looked up in state the host
    // happens to be holding, which is what three reviewers of the code round asked for.
    // The file the person asked for. Everything that DECIDES anything is in `roundsExport`, which
    // imports no `vscode` and is therefore reachable from a test; this supplies the three things
    // that genuinely need the editor — the dialog, the write, and the two ways of saying what
    // happened. The in-flight state is the notification itself, so there is nothing to leave stuck.
    onExport: async (rows) => {
      // The whole job — the reads AND the write — runs inside the queue. It used to read first and
      // queue afterwards, so a hundred quick clicks started a hundred batches of four and stepped
      // straight around the cap that exists to protect the machine. (Code round, three reviewers.)
      // ONE round needs no progress bar and no cancel button — the save dialog is the whole of the
      // interaction. A SELECTION is a job: it spawns a process per round, takes seconds, and a
      // person who started it by ticking the header box must be able to stop it.
      const asJob = rows.length > 1;
      // ASKED BEFORE the progress notification exists. Raising a progress bar and then a modal the
      // person may decline means tearing the bar down again, which inverts the order of a
      // pre-condition and the job it guards. (Code round, gemini.)
      if (rows.length > ASK_ABOVE && await notifyAndAsk({
        as: 'warning',
        class: 'confirmation',
        source: 'roundsExport',
        code: 'export-many-rounds',
        modal: true,
        title: `Export ${rows.length} rounds? Each one is read separately, so this will take a while.`,
        action: 'Export',
      }) !== 'Export') {
        return;
      }
      // withProgress answers a Thenable, and the queue takes a Promise; awaiting it inside the
      // callback is what makes the two agree without a cast.
      await exportQueue(async () => (asJob
        ? vscode.window.withProgress({
          location: vscode.ProgressLocation.Notification,
          title: `Exporting ${rows.length} rounds…`,
          cancellable: true,
        }, (progress, token) => {
          // `increment` is a DELTA, so it has to be the distance travelled since the last report —
          // not one round's share. The batch read reports once, with every round done at once, and a
          // flat `100 / total` moved the bar 0.2% for a selection of five hundred and left it there
          // until the file was written. (CodeRabbit, on the pull request.)
          let reported = 0;

          return runExport(rows, {
            progress: (done, total) => {
              const step = ((done - reported) * 100) / total;
              reported = done;
              progress.report({ message: `read ${done} of ${total}`, increment: step });
            },
            cancelled: () => token.isCancellationRequested,
          }, panelRef);
        })
        : runExport(rows, {}, panelRef)));
    },
    onFindings: async (key, round) => {
      try {
        const found = await panelRef.roundFindings(round.sessionId, round.stage, round.number);
        await roundsLog.tell(key, found.state, found.findings);
      } catch (reason: unknown) {
        // The row is already showing "Reading…" and only this call can move it off. A rejection
        // here — the binary gone between the list read and the click — would otherwise leave it
        // spinning for ever, which is the state this whole change exists to end.
        console.error('ConnectOtherAIs: a round\'s findings could not be read', reason);
        await roundsLog.tell(key, 'failed', []);
      }
    },
  });
  const panel = panelRef = new PanelProvider(context, watcher, dataDir(), async (id) => {
    const question = watcher.openQuestions.find((q) => q.id === id);
    if (question !== undefined) {
      await watcher.answerCommand(question);
    }
  }, consultations);
  // The panel repaints whenever the watcher's state moves, so a question answered in the modal
  // disappears from the sidebar without anyone asking it to.
  watcher.onChanged = () => {
    // What the title bar switches on: green while somebody is waiting on an answer.
    void vscode.commands.executeCommand('setContext', 'coai.hasQuestions', watcher.openQuestions.length > 0);
    void panel.render();
    // And the rounds log, if it is open: a round advances on its own, so the page it is shown on
    // has to as well. Nothing is read for it when nobody is looking, and nothing is pushed when
    // nothing changed.
    void refreshRoundsLog(roundsLog, watcher, panel);
  };
  watcher.start();

  // The same contract as above, and only when something a person can SEE moved: the watcher
  // compares what it read before telling anybody, so a directory polled every five seconds for an
  // afternoon repaints the sidebar exactly as often as a consultation changes.
  consultations.onChanged = () => {
    void panel.render();
    // And the LOG, which draws the same consultations on its own tab. Repainting only the sidebar
    // left an open Consultations tab showing history from before the conversation started, advanced
    // or ended — and the page's own ten-second cache could hold that past the LAST watcher event,
    // so it stayed wrong until somebody clicked something. (CodeRabbit, on the pull request.)
    panel.forgetRoundsLog();
    void refreshRoundsLog(roundsLog, watcher, panel, true);
  };
  consultations.start();

  // The settings the server reads, mirrored from activation — never from the panel.
  //
  // This was the defect a colleague hit on macOS: the write lived in `PanelProvider.render()`
  // behind its view guard, and the configuration listener was registered inside
  // `resolveWebviewView`. VS Code resolves a webview view LAZILY, so in a window where nobody had
  // opened the ConnectOtherAIs panel, nothing watched `coai.*` and nothing wrote the file. They
  // set `onExhausted` to `good_enough`, restarted, and the server went on answering `call_human`
  // from an `env` block pasted months earlier — ten third rounds in a row.
  //
  // And it stamps what it writes, because this file has more than one writer: every open window
  // has its own extension host, they all hear `onDidChangeConfiguration`, and a host loaded before
  // the last update is still running the build it started with. One of those reverted a
  // Team-server reviewer on 2026-09-07 by rewriting a runtime it did not know. An older build now
  // stands down and says so, once.
  const settingsSync = new ServerSettingsSync(
    () => readCoaiConfiguration(context),
    (json) => writeSettingsFile(json),
    extensionVersion(context),
    readSettingsFile,
    reportStandDown,
    underSettingsLock,
  );
  mirrorSettings(settingsSync);

  // One registry per window: a conversation belongs to a Claude Code tab, and tabs are per window.
  const chatPanels = new ChatPanels();
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
  // What restoring a tab needs; one object, so the serializer and every retry read the same store,
  // the same memento and the same workspace the writers use.
  const restoreDeps: RestoreDeps = {
    panels: chatPanels,
    store: chatStore,
    memento: chatTabMemory,
    extensionUri: context.extensionUri,
    workspace: conversationWorkspace,
  };
  // THE MIGRATION, started before anything can read the store and waited for by the serializer — VS
  // Code deserializes panels during activation, and a conversation not yet carried across would read
  // as absent and be disposed on the first reload after the upgrade. It compares transcripts, never
  // ids, and empties the key only once every record is confirmed and indexed; `chatStoreImport.ts`
  // says why each of those rules exists. It never throws, so the catch below is the outer edge of a
  // detached call and a defect if it ever speaks — and it re-binds the memento, because a defect
  // after the seal would otherwise leave this window writing to one store while the other is in charge.
  const migration: Promise<ImportReport> = importLegacyTabs({
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
  // HOUSEKEEPING, and the INDEX the picker reads (story B1): this window's heartbeat from now on, the
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

  context.subscriptions.push(
    {
      dispose: () => {
        clearTimeout(deferred);
        // And forget it. Clearing the timer without clearing the marker means a reactivation that
        // finds the lock busy schedules nothing, and the configuration then waits for a change that
        // may never come. Accepted finding, this story's code round.
        deferred = undefined;
      },
    },
    // The heartbeat's timer. Its FILE is left on purpose — see `chatStoreHeartbeat.ts`.
    {
      dispose: () => {
        housekeeping.dispose();
      },
    },
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('coai')) {
        // Re-read WHERE first: `mirrorSettings` writes the settings file into the data directory, so
        // a changed `coai.dataDirectory` has to be in effect before that write chooses its path.
        storageReadsThisSide(context);
        mirrorSettings(settingsSync);
        void panel.render();
      }
    }),
    watcher,
    consultations,
    vscode.window.registerWebviewViewProvider(PanelProvider.viewType, panel),
    // The key is typed HERE or in the tab, and stored in the editor's secret storage either way.
    // A command as well as a button because the tab cannot be opened usefully without a key, and a
    // door that only exists behind the thing it unlocks is not a door.
    vscode.commands.registerCommand('coai.setBugsAdminKey', async () => {
      // The SAME panel the section button opens, so a key set here redraws a tab that is already
      // open instead of leaving it on the face it had before the key existed.
      await usersPanel(
        context.secrets,
        () => vscode.workspace.getConfiguration('coai').get<string>('bugzServer', '').trim(),
      ).askForKey();
    }),
    vscode.commands.registerCommand('coai.editChatPresets', () => { openChatPresets(); }),
    // The CONTEXT goes with it: the roles page reads and writes `coai.roles`, which is a per-side
    // setting, and only the context says which side this window is.
    vscode.commands.registerCommand('coai.editRoles', () => { openRoles(context); }),
    vscode.commands.registerCommand('coai.editPhrases', () => { openPhrases(context); }),
    // The same question the first install on a side asks, reachable afterwards. One flow: two ways
    // of asking it would be two ways of answering it differently.
    vscode.commands.registerCommand('coai.changeDataDirectory', async () => {
      await askWhereDataLives(context);
      await panel.render();
    }),
    vscode.commands.registerCommand('coai.moveDataDirectory', async () => {
      // The counter is passed IN rather than reached for, because it is the one part of a move that
      // needs a process: rounds are counted in SQL by the server binary, and counting them at the
      // DESTINATION means running that binary against a directory this window is not pointed at yet.
      await moveDataDirectory(context, (directory) => countStorage(context, directory));
      await panel.render();
    }),
    vscode.commands.registerCommand('coai.deleteOldDataFolder', async () => {
      // The same counter the move used: the old folder is read ONE more time, right before it is
      // deleted, and compared with what the move recorded. It is the only thing standing between a
      // round written after the copy and a directory that is gone.
      await deleteTheOldDataFolder(context, (directory) => countStorage(context, directory));
      await panel.render();
    }),
    vscode.commands.registerCommand('coai.help', showHelp),
    // Chat with another vendor about a passage. Two doors reach it — this keybinding and the
    // 'Chat with other AI' item in Claude Code's own right-click menu — and the command tells them
    // apart by what VS Code hands it, because only one of them can copy the selection itself.
    vscode.commands.registerCommand('coai.chatWithOtherAi', (...args: unknown[]) => {
      // RECORDED FIRST, in every one of these six. The count on the spending page is of how often
      // the chat was reached for, so a door that then refuses — no CLI, nothing captured — is still
      // one of them. Nothing waits for the write.
      noteChatDoor('key');
      void chatWithOtherAi(chatPanels, context.extensionUri, args);
    }),
    // The two menu items, which differ from the chord in one way: each SAYS what it will do, so
    // neither reads `coai.chatAutoSend`. An item whose behaviour depends on a setting in another
    // window is an item nobody can predict from its own label.
    vscode.commands.registerCommand('coai.chatNow', (...args: unknown[]) => {
      noteChatDoor('default');
      void chatWithOtherAi(chatPanels, context.extensionUri, args, true);
    }),
    vscode.commands.registerCommand('coai.chatChoose', (...args: unknown[]) => {
      noteChatDoor('choose');
      void chatWithOtherAi(chatPanels, context.extensionUri, args, false);
    }),
    // Take the question Claude Code is asking and hand it to a second model. Its own door, because
    // the passage comes from Claude's session file rather than from a selection — that box cannot
    // be selected, which is why a screenshot was the only way before this.
    //
    // The rejection is CAUGHT rather than dropped: a command that fails silently at the activation
    // boundary is a keypress that does nothing and explains nothing. (CodeRabbit, PR #206.)
    vscode.commands.registerCommand('coai.takeTheQuestion', () => {
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
    vscode.commands.registerCommand('coai.addTheQuestion', () => {
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
    vscode.commands.registerCommand('coai.goToConversation', () => {
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
    vscode.commands.registerCommand('coai.switchConversations', () => {
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
    vscode.commands.registerCommand('coai.forgetPickedConversation', () => {
      forgetPickedConversation();
    }),
    // Deactivation is not a tab closing: nobody has told VS Code about these panels, so both the
    // panel and the vendor process behind it have to be ended here or they outlive the extension.
    { dispose: () => chatPanels.closeAll() },
    // How a chat tab comes back after a window reload. VS Code hands back the panel and whatever the
    // page saved with `setState` — here, the conversation's own id — and `chatRestorePanel.ts` does
    // the rest: validates the id, draws *Restoring…* before anything is awaited, waits for the
    // migration under a ceiling so a record that was still in the memento a moment ago is found, and
    // reads the STORE. The store answers four ways and `chatRestore.ts` decides each: a record
    // restores; absent — in the store AND in the memento — disposes, because an empty tab pretending
    // to be a conversation is worse than none; a record this build cannot read, or a disk that would
    // not answer, keeps the tab and SAYS so, with a retry where one makes sense. Disposing over either
    // of those would throw a person's tab away over a permissions error or a downgrade.
    vscode.window.registerWebviewPanelSerializer('coaiChat', {
      deserializeWebviewPanel: (panel: vscode.WebviewPanel, state: unknown) =>
        restoreAfterReload(restoreDeps, panel, state, migration).catch((reason: unknown) => {
          // The store answers in outcomes and never rejects, so anything here is a defect — said on
          // the console rather than dropped, and the panel is NOT disposed over it: the rule of this
          // whole serializer is that a tab is thrown away only for a conversation that is nowhere.
          console.error('ConnectOtherAIs: a chat tab could not be restored after a reload', reason);
        }),
    }),
    // Both doors repaint. The panel's own button used to be the only path that did — it awaits
    // the command and then renders — so an update started from THIS menu left the Server section
    // showing the version it had replaced, which is the very symptom the button was fixed for.
    vscode.commands.registerCommand('coai.installServer', async () => {
      await installServer(context);
      await panel.render();
    }),
    vscode.commands.registerCommand('coai.copyConfigBlock', () => copyConfigBlock(context)),
    vscode.commands.registerCommand('coai.copyClaudeSnippet', copyClaudeSnippet),
    vscode.commands.registerCommand('coai.showRounds', () => showRoundsLog(roundsLog, watcher, panel)),
    vscode.commands.registerCommand('coai.answerQuestion', () => answerQuestion(watcher)),
    // The same action under a second id, so the title bar can show a green icon while a question
    // is waiting — a menu icon cannot be recoloured by state, but which command is shown can.
    vscode.commands.registerCommand('coai.answerQuestionWaiting', () => answerQuestion(watcher)),
  );

  void offerUpdate(context);
}

export async function deactivate(): Promise<void> {
  // The watcher is a subscription; VS Code disposes it. No server, no port — but the chat ledger's
  // writes are deliberately NOT awaited by the turn that made them (a person's answer must not wait
  // on a disk), so a host closed the instant a turn ends could take the record with it. VS Code
  // awaits what this returns, which is the one moment the queue can be drained for free.
  // (codex and the local reviewer, the code round.)
  //
  // The drain has a CEILING and can therefore give up, which is the right trade — a window that
  // will not close is answered by VS Code killing the host, and that loses more than the ceiling
  // gives up. What it must not do is give up in silence: the console is the only surface left at
  // this point in the lifecycle, since the panel and every webview are already gone, and a record
  // saying "records were lost" cannot be written to the ledger that is what failed.
  if (!await flushChatUsage()) {
    // What it says is exactly what is known, and no more. Giving up on the WAIT does not cancel the
    // writes — they are already queued and they land if the host lives long enough — so "records
    // were lost" would be an overstatement, and an alarm that overstates is one people learn to
    // ignore. What is true is that nobody confirmed them. (local reviewer, the code round.)
    console.error(
      'ConnectOtherAIs: the ledgers were still writing when this window closed, and the drain gave '
      + 'up waiting. The queued records are not cancelled and normally still land; if the host was '
      + 'killed first, they did not. The usual cause is a data directory on a drive that stopped '
      + 'answering.',
    );
  }
}

/**
 * The pending retry, and there is at most one.
 *
 * <p>A window that finds another one in the file writes nothing — which is right, because nobody
 * waits for a settings mirror — and then nothing fires again on its own. A configuration change that
 * happened to land while another window was writing would sit unwritten until the person changed
 * something else, and there may not be a next time. So one deferred attempt, scheduled just past the
 * window in which any lock is either released or breakable as stale.</p>
 *
 * <p>ONE, and not a ladder: by the time it runs there is no lock left that this window cannot take,
 * so a second failure is a different problem and the next configuration change will carry it.
 * Accepted finding, this story's plan round.</p>
 */
let deferred: ReturnType<typeof setTimeout> | undefined;

const RETRY_AFTER_MS = LOCK_STALE_AFTER_MS + 2_000;

function mirrorSettings(settingsSync: ServerSettingsSync, isTheRetry = false): void {
  void settingsSync.sync().then((outcome) => {
    if (outcome !== 'busy' || isTheRetry || deferred !== undefined) {
      return;
    }
    deferred = setTimeout(() => {
      deferred = undefined;
      mirrorSettings(settingsSync, true);
    }, RETRY_AFTER_MS);
  });
}

/**
 * The `coai.*` settings as the sync wants them: one read, both halves, no VS Code type leaving.
 *
 * <p><b>Read as THIS SIDE has them.</b> The file this feeds is what `coai-mcp` reads, so it decides
 * which binaries the GATE's reviewers are launched from — and it used to be filled from the shared
 * configuration alone. With *Separate settings for each side* on, that ran a WSL window's rounds off
 * the vendor rows of another side: the widest blast radius of the same bypass the chat had, because
 * a review is what the product is for. The reader is the one in `sideSettings`, the same one the
 * panel and the chat go through.</p>
 */
function readCoaiConfiguration(context: vscode.ExtensionContext) {
  const config: ConfigReader = readerFor(context, vscode.workspace.getConfiguration('coai'));

  return {
    settings: settingsFrom(config),
    vendors: vendorsFrom(config('vendors')),
  };
}

/**
 * Writes the settings file the server reads out of its own data directory.
 *
 * <p>The directory is created first: on a machine where no review has ever run it does not exist
 * yet, and the settings are the one thing that has to be there BEFORE the first round rather than
 * after it.</p>
 */
async function writeSettingsFile(json: string): Promise<void> {
  await vscode.workspace.fs.createDirectory(dataDir());
  // Written beside it and renamed over it, the way an answered escalation already is. `writeFile`
  // truncates before it fills, so a host killed between the two leaves every other window — and the
  // server — reading a truncated file. That is worse than the revert this epic is about, and it
  // cannot be recovered from, because the original is gone. Raised on story 2.1's plan round.
  const target = vscode.Uri.joinPath(dataDir(), 'settings.json');
  const temp = vscode.Uri.joinPath(dataDir(), `settings.json.${process.pid}.tmp`);
  await vscode.workspace.fs.writeFile(temp, new TextEncoder().encode(json));

  // The last thing before the rename: are we still the holder? A process suspended past the stale
  // window — a laptop closing mid-write is the ordinary way — is broken as dead by another window,
  // and would otherwise wake up and land a payload decided before that window's write existed. This
  // narrows that to the microseconds between this check and the rename, which is as far as it can be
  // taken without a lease that renews. Raised on this story's plan round.
  if (held.length > 0 && !(await lockHolds(held))) {
    await vscode.workspace.fs.delete(temp).then(undefined, () => undefined);
    throw new Error(
      'the settings lock was taken over while this write was in flight, so these settings are not on '
        + 'disk; another window is writing and an attempt is scheduled',
    );
  }

  await vscode.workspace.fs.rename(temp, target, { overwrite: true });
}

/**
 * Runs the settings read, the version comparison and the write with nobody else in the file.
 *
 * <p>Every window on this machine writes this one path, so the guard that reads a stamp and then
 * overwrites it is a time-of-check-to-time-of-use race: two guard-aware hosts can both read a stamp
 * they are allowed to overwrite, and the second to finish wins with a payload decided before the
 * first one's write existed.</p>
 *
 * <p><b>The lock is an O_EXCL create, and the reason is worth keeping.</b> The first version claimed
 * exclusion from `vscode.workspace.fs.rename(…, { overwrite: false })` — and whether THAT is atomic
 * is not documented anywhere; the disk provider checks for existence and then renames, which is
 * check-then-act, so the whole guarantee rested on an implementation detail. `fs.open(path, 'wx')`
 * is O_EXCL on every platform this runs on, which is a promise the operating system makes.</p>
 *
 * <p>A lock that cannot be taken is not an error and nothing WAITS — but the caller is told, because
 * nothing else will fire on its own and the configuration would otherwise sit unwritten until the
 * person happened to change something else.</p>
 */
async function underSettingsLock(work: () => Promise<void>): Promise<boolean> {
  await vscode.workspace.fs.createDirectory(dataDir());
  const token = await takeLock();
  if (token === '') {
    return false;
  }

  held = token;
  try {
    await work();
  } finally {
    held = '';
    // ONLY while it is still ours. A window whose lock was broken as stale would otherwise delete
    // its SUCCESSOR's lock on the way out, and a third window would walk in while the second was
    // still writing — the race, produced by the release. Raised on this story's plan round.
    if (await lockHolds(token)) {
      await fsp.rm(lockPath()).catch(() => undefined);
    }
  }

  return true;
}

/** The token this window currently holds, or empty. Read by the write, just before it renames. */
let held = '';

function lockPath(): string {
  return path.join(coaiDataDir(), 'settings.lock');
}

/** Whether the lock on disk is still the one we took. */
async function lockHolds(token: string): Promise<boolean> {
  return await fsp.readFile(lockPath(), 'utf8').then((t) => t === token, () => false);
}

/**
 * Take the lock, or answer empty.
 *
 * <p>The token is this process and this moment, so a window can tell its OWN lock from the one that
 * replaced it. `wx` fails with `EEXIST` when the file is there, which is the whole mechanism.</p>
 */
async function takeLock(mayBreakAStaleOne = true): Promise<string> {
  // `randomUUID`, not `Math.random`: uniqueness is all this needs, but a pseudorandom generator in a
  // token is a shape worth not having — and an analyser is right to ask about one without reading
  // what the token is for. The crypto one is the same line and answers the question.
  const token = `${process.pid}:${Date.now()}:${randomUUID()}`;
  try {
    await fsp.writeFile(lockPath(), token, { encoding: 'utf8', flag: 'wx' });

    return token;
  } catch (e) {
    // Only EEXIST means somebody holds it. A permissions error or a dead volume is not contention,
    // and sending those into stale-breaking would have this window reading and deleting a lock it
    // had no business touching. Accepted finding, this story's code round.
    if (!isAlreadyThere(e)) {
      return '';
    }

    // ONE attempt at breaking a stale lock, never a loop: a lock somebody keeps re-taking between
    // our delete and our create would otherwise spin the extension host instead of skipping a write
    // nobody would have missed.
    return mayBreakAStaleOne ? breakIfStale() : '';
  }
}

function isAlreadyThere(e: unknown): boolean {
  return typeof e === 'object' && e !== null && (e as { code?: unknown }).code === 'EEXIST';
}

/**
 * A lock older than any write could take belonged to a window that died.
 *
 * <p>The token is read before and after the staleness decision, so a lock that was RELEASED and
 * re-taken while this looked at it is left alone rather than deleted out from under its new owner.
 * That narrows the window; it does not close it, and the limit is recorded honestly in
 * `module_extension.md` — deleting a file by path is check-then-act, and there is no
 * compare-and-unlink to be had.</p>
 */
async function breakIfStale(): Promise<string> {
  try {
    const before = await fsp.readFile(lockPath(), 'utf8');
    const onDisk = await fsp.stat(lockPath());
    if (!lockIsStale(onDisk.mtimeMs, Date.now())) {
      return '';
    }
    if ((await fsp.readFile(lockPath(), 'utf8')) !== before) {
      return '';
    }
    await fsp.rm(lockPath());
  } catch {
    return '';
  }

  // Deleted it; take it the ordinary way rather than assuming the gap is ours. Two windows can
  // reach this line together and only one of their exclusive creates can succeed, which is the point.
  return takeLock(false);
}

/**
 * The settings file as it is now, or which of the two ways it could not be read.
 *
 * <p><b>Absent and unreadable are different answers and the difference decides a write.</b> An
 * earlier draft answered `''` for both, so a file that is locked, or on a volume that blinked, was
 * indistinguishable from a file that is not there — and "not there" is permission to overwrite. That
 * is the exact revert this whole epic is about, reached through a different door. Three reviewers
 * found it independently on this story's code round.</p>
 *
 * <p>Only a confirmed `FileNotFound` is absent. Everything else — a permission error, a disconnected
 * volume, a provider that threw — stands the write down and is retried on the next change.</p>
 */
async function readSettingsFile(): Promise<ExistingFile> {
  try {
    return {
      kind: 'contents',
      text: new TextDecoder().decode(
        await vscode.workspace.fs.readFile(vscode.Uri.joinPath(dataDir(), 'settings.json')),
      ),
    };
  } catch (e) {
    return e instanceof vscode.FileSystemError && e.code === 'FileNotFound'
      ? { kind: 'absent' }
      : { kind: 'unreadable' };
  }
}

/**
 * This build's own version, from the manifest VS Code loaded it from.
 *
 * <p>`packageJSON` is `any`, so it is narrowed here rather than asserted: a manifest without a
 * version string yields an empty one, and an empty version makes the sync behave exactly as it did
 * before the stamp existed. Being unable to name yourself is a reason to stand aside from the
 * comparison, never a reason to guess at it.</p>
 */
function extensionVersion(context: vscode.ExtensionContext): string {
  const manifest: unknown = context.extension.packageJSON;
  if (typeof manifest !== 'object' || manifest === null) {
    return '';
  }
  const version = (manifest as Record<string, unknown>)['version'];

  return typeof version === 'string' ? version : '';
}

/**
 * Says that a newer build's settings were left alone, and offers the one action that fixes it.
 *
 * <p>Once per session, from the sync, which cannot import this module. A window running an older
 * build reverts settings SILENTLY today — that silence is the whole defect, seen from the other
 * side — and the cure is not a setting or a reinstall: it is reloading this window, which is what
 * makes VS Code pick up the extension it has already downloaded.</p>
 */
function reportStandDown(theirVersion: string): void {
  // Through the funnel, and through `notifyThen` rather than `notifyAndAsk`: this is raised from
  // inside `serverSettingsSync`'s critical section, so waiting here for somebody to press a button
  // would hold that section for as long as the toast sat on screen and turn every later sync into
  // a 'busy'. The record lands either way — which is the point, because on 2026-09-16 this exact
  // warning was shown once, missed, and the machine ran ninety minutes on stale settings.
  void notifyThen(
    {
      as: 'warning',
      class: 'stand-down',
      source: 'serverSettingsSync',
      code: 'settings-stood-down',
      title: `ConnectOtherAIs left the server settings alone: they were written by version ${theirVersion}, `
        + 'which is newer than the build this window is running. Reload the window to catch up.',
      cure: 'Reload this window — it picks up the extension VS Code has already downloaded.',
      action: 'Reload Window',
    },
    (choice) => {
      if (choice === 'Reload Window') {
        void vscode.commands.executeCommand('workbench.action.reloadWindow');
      }
    },
  );
}

/** The one answer, as the Uri the rest of this file wants. It lives in `dataDir.ts`. */
function dataDir(): vscode.Uri {
  return vscode.Uri.file(coaiDataDir());
}

/** Answer an open question by hand — the same path the modal's button takes. */
async function answerQuestion(watcher: EscalationWatcher): Promise<void> {
  await watcher.refresh();
  const open = watcher.openQuestions;
  if (open.length === 0) {
    void notify({
      as: 'information',
      class: 'refusal',
      source: 'escalations',
      code: 'no-question-waiting',
      title: 'No ConnectOtherAIs review is waiting on an answer.',
    });
    return;
  }

  const picked =
    open.length === 1
      ? open[0]
      : await vscode.window
          .showQuickPick(
            open.map((e) => ({ label: e.branch, detail: e.question, escalation: e })),
            { title: 'Which question?' },
          )
          .then((choice) => choice?.escalation);

  if (picked !== undefined) {
    await watcher.answerCommand(picked);
  }
}


/** One install at a time: the panel button and the ⋯ menu are two doors to the same work. */
const installing = new SingleFlight<void>();

async function installServer(context: vscode.ExtensionContext): Promise<void> {
  await installing.run(() => install(context));
}

async function install(context: vscode.ExtensionContext): Promise<void> {
  try {
    // Asked BEFORE the download, because `installLatest` writes this side's record at the end of it
    // and the question is "has this side ever installed", not "did that just succeed".
    const firstTime = installedVersion(context.globalState, context.globalStorageUri) === undefined;
    const target = await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Notification, title: 'Installing coai-mcp…' },
      () => installLatest(context.globalStorageUri, context.globalState),
    );
    if (firstTime && await askWhereDataLives(context, true) === 'refused') {
      // The choice did not land, so the block below would carry a configuration nobody asked for —
      // and the install record written moments ago stops the question ever being asked again. Say
      // so instead of copying something misleading. (codex, code round.)
      void notify({
        as: 'error',
        class: 'failure',
        source: 'installServer',
        code: 'install-data-directory-not-saved',
        title: `coai-mcp is installed at ${target.fsPath}, but where its data should live could not be `
          + 'saved. Nothing has been copied to your clipboard: set it with "ConnectOtherAIs: Change '
          + 'where your data lives" and paste the block it gives you.',
        cure: 'Set the folder with "ConnectOtherAIs: Change where your data lives", then copy the block again.',
      });

      return;
    }
    // The block carries the two variables — the ONE configuration a client entry must hold, because
    // the settings file that carries everything else lives inside the directory they select. See
    // `serverEnv`, and the guard in `install.test.ts` that allows these two keys and no others.
    await vscode.env.clipboard.writeText(mcpServerBlock(target.fsPath, serverEnv()));
    const targets = clientTargetsLine(CLIENT_TARGETS);
    void notify({
      as: 'information',
      class: 'outcome',
      source: 'installServer',
      code: 'server-installed',
      title: `${installedMessage(target.fsPath)} Paste it into: ${targets}`,
    });
  } catch (error) {
    const raw = message(error);
    const hint = installFailureHint(raw, codeOf(error));
    void notify({
      as: 'error',
      class: 'failure',
      source: 'installServer',
      code: 'server-not-installed',
      title: hint.length > 0 ? `coai-mcp was not updated: ${hint}` : `coai-mcp was not installed: ${raw}`,
      // The raw reason as well as the hint: a hint that matched nothing is exactly the case where
      // somebody needs what the process actually said.
      detail: raw,
    });
  }
}


async function copyConfigBlock(context: vscode.ExtensionContext): Promise<void> {
  const path = serverPath(context.globalStorageUri);
  if (path === undefined) {
    void notify({
      as: 'error',
      class: 'refusal',
      source: 'installServer',
      code: 'no-build-for-this-platform',
      subject: `${process.platform}-${process.arch}`,
      title: 'There is no published coai-mcp build for this platform yet.',
    });
    return;
  }
  // Whether it is there is a question about THIS side's disk. It used to be a question about a
  // remembered version shared by every window of the profile, so a WSL window handed out a path
  // that did not exist and called it installed. `stat` answers it; asking for the full status would
  // launch a `--version` process to learn something the stat already knew.
  const installed = await serverExists(context.globalStorageUri);
  // The same block the install flow copies, carrying the same two variables: a person who comes back
  // to the ⋯ menu after choosing a directory must not be handed an entry that ignores the choice.
  await vscode.env.clipboard.writeText(mcpServerBlock(path.fsPath, serverEnv()));
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'installServer',
    code: 'mcp-block-copied',
    title: installed
      ? 'The MCP config block is on your clipboard — paste it into your client and restart it.'
      : 'The block is on your clipboard, but coai-mcp is not installed yet — run "Install the MCP Server…" first.',
  });
}

async function copyClaudeSnippet(): Promise<void> {
  // The CLIPBOARD first, because what goes on it does not depend on what this workspace has: the
  // artefact is all four rules, and a paste missing any of them is what `snippetStatus` calls
  // `older`. The snippet names no repository either — it is pasted into whichever one you are
  // adopting it for, and the AI reading it is already in a checkout it can name for itself.
  await vscode.env.clipboard.writeText(claudeSnippet());
  // What was taken, and what this repository already has. The version is in the menu item too, but
  // a menu is read BEFORE the click; this is the sentence that says whether the click mattered.
  const status = await pastedSnippetStatus();
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'claudeSnippet',
    code: 'gate-snippet-copied',
    // The KIND, not the whole status: `current`, `older`, `ahead`, `unversioned`, `absent` is the
    // distinction worth counting, and a version number in the key would mint one per release.
    subject: status.kind,
    title: copiedMessage(status),
  });
}

/**
 * The rounds log, opened — or brought forward, if it is already open.
 *
 * <p>It was a markdown FILE, `rounds.md` under the data directory, written and then opened as a
 * text document and rewritten every five seconds while its tab was open. Fifty-three lines of
 * tables nobody could sort, filter or search, and a rewrite that reloaded the editor on every tick.
 * The page keeps the same command and the same data; only the surface changed.</p>
 */
async function showRoundsLog(log: RoundsLogPanel, watcher: EscalationWatcher, panel: PanelProvider): Promise<void> {
  await watcher.refresh();

  // The page FIRST, from the session files alone — everything it has ever shown comes from those, so
  // it is complete without the database. Reading the database spawns the server, and a person who
  // opened a log should not wait on a process to see it: the findings arrive in the next push, a
  // moment later. Raised by the gate as blocking the panel on an unannounced read.
  log.show(
    await logRows(panel, undefined),
    watcher.openQuestions,
    await panel.usageTab());
  await refreshRoundsLog(log, watcher, panel, true);
}

/** Keeps an OPEN log current while a round runs. Nothing is read when nobody is looking. */
async function refreshRoundsLog(log: RoundsLogPanel, watcher: EscalationWatcher, panel: PanelProvider, force = false): Promise<void> {
  if (!log.isOpen) {
    return;
  }
  const fresh = await panel.roundsLog();
  log.update(
    await logRows(panel, fresh),
    watcher.openQuestions,
    await panel.usageTab(),
    force,
    blindSpotsHtml(fresh),
    fresh.totals,
    await panel.consultationsTab(fresh));
}

/**
 * Every row the log shows: the review rounds and the conversations, in one table, newest first.
 *
 * <p>One function because there are two call sites — the first paint and every tick after it — and
 * they must not drift. The first paint used to pass `undefined` for the database and the tick a real
 * one, and that difference is the whole of what varies between them; everything else being written
 * out twice is how a column ends up on one of the two.</p>
 *
 * <p><b>The same {@link PanelProvider.modelPrice} prices both halves.</b> A conversation and a review
 * round on one model are then worked out by one rule, which is the point of merging them into one
 * table at all: the answer to "what did today cost" is a sum, and a sum of two differently-derived
 * numbers is not one.</p>
 */
async function logRows(panel: PanelProvider, database: DbLog | undefined): Promise<LogRow[]> {
  const priceOf = await panel.modelPrice();

  return mergedRows(
    rowsFrom(
      await readSessions(), Date.now(), priceOf, await panel.usageLines(), database, panel.vendorIds()),
    chatRows(await panel.chatLines(), priceOf),
  );
}

/**
 * The three fields the rounds database keys a round by, checked rather than asserted.
 *
 * <p>A type PREDICATE, not an `as`: the value came off a webview bridge, and a cast there tells the
 * compiler to stop checking exactly where checking is the point. (Code round, codex.)</p>
 */
/** The three fields a round is keyed by, as one string — so a Map can hold the whole tuple. */
function keyOf(key: { sessionId: string; stage: string; number: number }): string {
  return `${key.sessionId}\u0000${key.stage}\u0000${key.number}`;
}

function isRoundKey(value: unknown): value is { sessionId: string; stage: string; number: number } {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const maybe = value as Record<string, unknown>;

  return typeof maybe['sessionId'] === 'string' && typeof maybe['stage'] === 'string'
    && typeof maybe['number'] === 'number';
}

/**
 * One export, with whatever progress and cancellation the caller can offer.
 *
 * <p>Outside `activate` so the wiring stays a wiring: this is the half that needs the editor — the
 * dialog, the atomic write, the two ways of speaking — and everything that decides anything is in
 * `roundsExport`, which imports no `vscode` at all.</p>
 */
async function runExport(
  rows: readonly ExportableRow[],
  extra: Partial<ExportPorts>,
  panel: PanelProvider,
): Promise<ExportOutcome> {
  return readAndExport(rows, async (row) => {
    // A row we cannot build a database key from is FAILED, not `absent`. `absent` says the database
    // has no record of this round, which is a claim — and we have not asked it anything. Failing
    // closed is the rule this story is built on. (Code round, gemini.)
    const key = row['dbKey'];
    if (!isRoundKey(key)) {
      return { state: 'failed' as const, findings: [] };
    }
    const found = await panel.roundFindings(key.sessionId, key.stage, key.number);

    // Spread rather than cast: `DbFinding` is a closed type and the CSV writer takes the open shape
    // a bridge message has, so the copy is what makes the two agree honestly.
    return { state: found.state, findings: found.findings.map((one) => ({ ...one })) };
  }, {
    pickPath: async (name) => (await vscode.window.showSaveDialog({
      defaultUri: vscode.Uri.file(name),
      filters: { 'Comma-separated values': ['csv'] },
      saveLabel: 'Export',
    }))?.fsPath,
    write: (path, text) => writeFileAtomically(path, text),
    report: (message) => void notify({
      as: 'information', class: 'outcome', source: 'roundsExport', code: 'rounds-exported', title: message,
    }),
    reportError: (message) => void notify({
      as: 'error', class: 'failure', source: 'roundsExport', code: 'rounds-not-exported', title: message,
    }),
    ...extra,
  }, new Date(), async (all) => {
    // ONE spawn for the whole selection — story C2, and the reason the per-round reader above is
    // now only the fallback inside `readManyFindings`.
    //
    // A row we cannot build a key from is never SENT: the server answers one entry per key it was
    // given, so asking about a row that has no key would shift every answer after it by one. It
    // keeps its slot here and is failed, exactly as it is above.
    const keys = all.map((row) => {
      const key: unknown = row['dbKey'];

      return isRoundKey(key) ? key : undefined;
    });
    // The token goes with it. `readAndExport` asks `cancelled` again when the read returns, but a
    // selection is ONE process now, so a cancel that did not reach the child would do nothing at all
    // until that process finished. (Plan round, three reviewers.)
    const found = await panel.roundFindingsMany(
      keys.filter((key) => key !== undefined), extra.cancelled);

    // Matched by the WHOLE key, never by position. The reader answers in the order asked, so an
    // index would work today — and would go on working right up until something streamed, retried or
    // de-duplicated, at which point one round's findings would be written onto another round's row
    // and nothing would look wrong. Three reviewers of the code round asked for this. (Code round.)
    const byKey = new Map(found.map((one) => [keyOf(one.key), one.found]));

    // Each answer travels back attached to the ROW it is about, so the coordinator never has to
    // pair by position either — the same objection, one boundary further out. (Code round, codex.)
    return all.map((row, at) => {
      const key = keys[at];
      const one = key === undefined ? undefined : byKey.get(keyOf(key));

      return {
        row,
        found: one === undefined
          ? { state: 'failed' as const, findings: [] }
          : { state: one.state, findings: one.findings.map((finding) => ({ ...finding })) },
      };
    });
  });
}

/**
 * What a data directory holds, counted — the before and after of a move.
 *
 * <p>Three counts, because they fail differently: the database is one file and nearly always
 * arrives whole, the sessions are a hundred small ones and are exactly what a partial copy loses,
 * and the ledger is a single growing file. `verificationFailure` compares them; this only counts.</p>
 *
 * <p>The rounds are counted by the SERVER, in SQL, because there is no SQLite in this extension —
 * which is also why this needs the binary and a directory to point it at. The other two are files,
 * and are read wherever they are.</p>
 */
async function countStorage(
  context: vscode.ExtensionContext,
  resolvedDirectory: string,
): Promise<StorageFingerprint> {
  const server = serverPath(context.globalStorageUri);
  const root = vscode.Uri.file(resolvedDirectory);

  // `limit: 1` because only the TOTALS are wanted, and they are counted in SQL rather than over the
  // page that comes back — asking for two hundred rounds to count them would be reading a history
  // to learn how long it is.
  const log = server === undefined
    ? undefined
    : await readLog(server.fsPath, { limit: 1 }, serverRunAt(server.fsPath, resolvedDirectory));

  return {
    // `log.read` is the server saying it ANSWERED, as opposed to `readLog` turning a spawn that
    // failed into an empty log. Without carrying it, a source and a destination that both failed to
    // read produce identical all-zero counts, verify each other, and offer a delete for a directory
    // nothing ever read. (CodeRabbit, Major.)
    read: log?.read === true,
    rounds: log?.totals.rounds ?? 0,
    sessions: await countIn(vscode.Uri.joinPath(root, 'sessions'), '.json'),
    usageLines: await countLines(vscode.Uri.joinPath(root, 'usage.jsonl')),
    // Every moved DIRECTORY, so an edited prompt, a new picture or an audit record is seen — three
    // counts covered the database, the sessions and the ledger, and the inventory moves sixteen
    // things. (codex, code round.) What this still cannot see is a file edited in place without
    // changing the count; hashing every byte of a copy that is on a network drive by definition is
    // the price of that last increment, and it is not taken.
    entries: await countEach(root),
  };
}

/** How many entries each moved directory holds, by name. Absent directories are not named at all. */
async function countEach(root: vscode.Uri): Promise<Record<string, number>> {
  const counted: Record<string, number> = {};
  for (const entry of DATA_TO_MOVE.filter((name) => name.endsWith('/'))) {
    const held = await countIn(vscode.Uri.joinPath(root, entry.replace(/\/$/u, '')), '');
    if (held > 0) {
      counted[entry] = held;
    }
  }

  return counted;
}

/**
 * How many entries of a kind a directory holds. A directory that is not there holds none.
 *
 * <p>An empty `extension` counts everything, including subdirectories: for `chat-conversations/` or
 * `consultations/` what matters is that the count moves when something is added, not what it is.</p>
 */
async function countIn(directory: vscode.Uri, extension: string): Promise<number> {
  try {
    const entries = await vscode.workspace.fs.readDirectory(directory);

    return extension.length === 0
      ? entries.length
      : entries.filter(([name, kind]) => kind === vscode.FileType.File && name.endsWith(extension)).length;
  } catch {
    return 0;
  }
}

/**
 * How many non-empty lines a file holds. A file that is not there holds none.
 *
 * <p>Counted over the BYTES. Decoding the ledger into a string and splitting it allocated three
 * full-size copies of a file that grows for ever — a gigabyte of it froze the flow before the
 * progress notification existed to say anything. (codex, code round.) A newline is one byte in
 * UTF-8 and cannot appear inside a multi-byte sequence, so counting them needs no decoder.</p>
 */
async function countLines(file: vscode.Uri): Promise<number> {
  const NEWLINE = 0x0a;
  try {
    const bytes = await vscode.workspace.fs.readFile(file);
    let lines = 0;
    let started = false;
    for (const byte of bytes) {
      if (byte === NEWLINE) {
        lines += started ? 1 : 0;
        started = false;
      } else if (byte !== 0x0d && byte !== 0x20 && byte !== 0x09) {
        started = true;
      }
    }

    return lines + (started ? 1 : 0);
  } catch {
    return 0;
  }
}

/**
 * The server's own session files, from the ONE place that knows where they are.
 *
 * <p>It used to resolve the directory here, for a third time in this product and differently from
 * both of the others: it read `COAI_DATA_DIR` raw, ignored `COAI_DATA_SIDE` entirely, and did not
 * trim. On a side-partitioned installation that read `<root>/sessions` while everything else wrote
 * `<root>/<side>/sessions`, so the rounds-log page listed somebody else's sessions or none.</p>
 *
 * <p>The settings layer made it worse rather than exposing it: a directory chosen in the panel lives
 * in a setting, which a `process.env` read cannot see at all — so this would have gone on reading
 * `%LOCALAPPDATA%` for every person who used the new feature. One rule, asked once.</p>
 */
async function readSessions(): Promise<SessionFile[]> {
  const sessionsDir = vscode.Uri.joinPath(dataDir(), 'sessions');
  const sessions: SessionFile[] = [];
  try {
    for (const [name, kind] of await vscode.workspace.fs.readDirectory(sessionsDir)) {
      if (kind !== vscode.FileType.File || !name.endsWith('.json')) {
        continue;
      }
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(sessionsDir, name));
      const session = parseSession(new TextDecoder().decode(bytes));
      if (session !== undefined) {
        sessions.push(session);
      }
    }
  } catch {
    // No data dir yet — an empty view says so honestly.
  }
  return sessions;
}

async function offerUpdate(context: vscode.ExtensionContext): Promise<void> {
  const server = await serverOnThisSide(
    context.globalStorageUri,
    context.globalState,
    (await latestServerVersion()) ?? '',
  );
  // `absent` never offers: an install is not an update, and this runs at activation — a machine
  // with nothing on this side would otherwise be told to update something it does not have.
  if (!server.updateOffered) {
    return;
  }
  const answer = await notifyAndAsk({
    as: 'information',
    class: 'offer',
    source: 'installServer',
    code: 'newer-server-published',
    title: 'A newer coai-mcp is published.',
    action: 'Install it',
  });
  if (answer === 'Install it') {
    await installServer(context);
  }
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Node puts an errno here; `vscode.FileSystemError` puts its own name. Absent is empty, never a guess. */
function codeOf(error: unknown): string {
  const code = (error as { code?: unknown } | null)?.code;
  return typeof code === 'string' ? code : '';
}
