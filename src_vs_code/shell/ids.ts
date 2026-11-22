// Every id the shell registers, as constants. This file imports nothing: the entry firewall
// (scripts/entry-firewall.mjs) allows the entry bundle four files (entry, lazy, ids, measure) and no others.

/** The module a command, view or panel belongs to; for this story every one of them belongs to `legacy`. */
export const MODULE_LEGACY = 'legacy';

/** The module that paints the sidebar's last snapshot before the legacy module has loaded, then hands the view to the legacy one (v2 E4). */
export const MODULE_SIDEBAR = 'sidebar';

/** The sidebar view (`contributes.views`). */
export const VIEW_PANEL = 'coai.panel';

/** The chat tab's webview type, restored after a reload by a serializer. */
export const PANEL_CHAT = 'coaiChat';

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
  'coai.editCommands',
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
  'coai.answerQuestion',
  'coai.answerQuestionWaiting',
  'coai.chatWithOtherAi',
  'coai.askFromTerminal',
  'coai.takeTheQuestion',
  'coai.addTheQuestion',
  'coai.takeFromTerminal',
  'coai.addFromTerminal',
  'coai.terminalHosts',
  'coai.chatNow',
  'coai.chatChoose',
  'coai.goToConversation',
  'coai.goToFromTerminal',
  'coai.backToSource',
  'coai.switchConversations',
  'coai.forgetPickedConversation',
];
