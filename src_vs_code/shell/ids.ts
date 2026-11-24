// Every id the shell registers, as constants. This file imports nothing: the entry firewall
// (scripts/entry-firewall.mjs) allows the entry bundle four files (entry, lazy, ids, measure) and no others.

/** The module a command, view or panel belongs to when no other module has taken it: the old code (`moduleOf` says which commands have gone). */
export const MODULE_LEGACY = 'legacy';

/** The module that draws the sidebar's real menu before the legacy module has loaded, then hands the view to the legacy one (v2 E4). */
export const MODULE_SIDEBAR = 'sidebar';

/**
 * The chat (v2 E4, story s4-chat-carried): every chat command and the `coaiChat` serializer load this module, never the legacy one. It starts
 * what chat needs of the old activation (data root, side models, remote sessions, presets, the chat store and its housekeeping) on its first load.
 */
export const MODULE_CHAT = 'chat';

/**
 * The binary install and update, and `coai-mcp --setup-clients` (v2 E4, story s5-install-module): the module the shell loads only AFTER the first `menuReady`
 * of the sidebar or the first command, never inside the cold start's M2 window. `coai.installServer` is its one command.
 */
export const MODULE_INSTALL = 'install';

/**
 * The state that must exist once per host (notify suppressor, the notification ledger's gap, the data root, the findings board): one module the
 * loader memoises and hands to every bundle by reference, so no bundle carries a copy (`src/runtimeState.ts`).
 */
export const MODULE_RUNTIME = 'runtime';

/** The `Symbol.for` key of the handle the loader publishes on `globalThis`; `src/hostRuntime.ts` reads it by the same name (a test holds the two together). */
export const SHELL_KEY_NAME = 'coai.shell';

/** The sidebar view (`contributes.views`). */
export const VIEW_PANEL = 'coai.panel';

/** The chat tab's webview type, restored after a reload by a serializer. */
export const PANEL_CHAT = 'coaiChat';

/**
 * The commands that belong to the chat module: each one is a way to open, find or leave a chat. `COMMANDS` below holds them too, in
 * `package.json` order; every other command id belongs to the legacy module (`moduleOf`).
 */
export const CHAT_COMMANDS: readonly string[] = [
  'coai.terminalHosts',
  'coai.terminalAccounts',
  'coai.chatWithOtherAi',
  'coai.askFromTerminal',
  'coai.takeTheQuestion',
  'coai.addTheQuestion',
  'coai.takeFromTerminal',
  'coai.addFromTerminal',
  'coai.chatNow',
  'coai.chatChoose',
  'coai.goToConversation',
  'coai.goToFromTerminal',
  'coai.backToSource',
  'coai.switchConversations',
  'coai.forgetPickedConversation',
];

/** The commands that belong to the install module: the one that puts the published `coai-mcp` on this side. `COMMANDS` holds it too, in `package.json` order. */
export const INSTALL_COMMANDS: readonly string[] = [
  'coai.installServer',
];

/** The module that runs a command: chat for the chat commands, install for the install command, legacy for everything else. */
export const moduleOf = (command: string): string => (CHAT_COMMANDS.includes(command) ? MODULE_CHAT : INSTALL_COMMANDS.includes(command) ? MODULE_INSTALL : MODULE_LEGACY);

/**
 * Every command `contributes.commands` lists, in that order. `shellIds.test.ts` fails when this list and
 * package.json drift apart, and when a command the old activation registers is missing here.
 */
export const COMMANDS: readonly string[] = [
  'coai.reviewTrees',
  'coai.setBugsAdminKey',
  'coai.settleOldBugzKeys',
  'coai.setBugsContributorKey',
  'coai.editChatPresets',
  'coai.showNotifications',
  'coai.editRoles',
  'coai.editPhrases',
  'coai.changeDataDirectory',
  'coai.moveDataDirectory',
  'coai.deleteOldDataFolder',
  'coai.help',
  'coai.openSettings',
  'coai.installServer',
  'coai.copyConfigBlock',
  'coai.copyClaudeSnippet',
  'coai.restoreCatalogBackup',
  'coai.exportConfig',
  'coai.importConfig',
  'coai.showRounds',
  'coai.chatWithOtherAi',
  'coai.askFromTerminal',
  'coai.takeTheQuestion',
  'coai.addTheQuestion',
  'coai.takeFromTerminal',
  'coai.addFromTerminal',
  'coai.terminalHosts',
  'coai.terminalAccounts',
  'coai.chatNow',
  'coai.chatChoose',
  'coai.goToConversation',
  'coai.goToFromTerminal',
  'coai.backToSource',
  'coai.switchConversations',
  'coai.forgetPickedConversation',
];
