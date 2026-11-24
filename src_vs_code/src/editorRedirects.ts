/**
 * The commands that opened the Review roles and Chat presets tabs (and the Gate commands tab, removed in v2) — tabs deleted by
 * research/PLAN_one_model_catalog.md E5.1 step 4, whose editors are places of the Settings page now. Each command stays for
 * one more release as a REDIRECT to its place, the way T5 keeps the restore command: a keybinding of somebody's own,
 * or a habit, still lands somewhere useful (the plan round's finding 0).
 *
 * <p>Pure: the host hands in how to register a command and how to open the page, so a test runs every row.</p>
 */

/** Each redirected command, and the place of the Settings page that holds its editor (`catalogPlaces.ts`). */
export const EDITOR_PLACES = {
  'coai.editRoles': 'reviews/roles',
  'coai.editChatPresets': 'chat',
} as const satisfies Readonly<Record<string, string>>;

/**
 * Registers the redirects.
 *
 * @param register the host's `registerCommand`, handed each command and what it runs
 * @param openSettingsAt opens the one Settings tab of this window at a place, or brings it back there
 * @returns what each registration returned (the host's disposables)
 */
export function editorRedirects<T>(register: (command: string, run: () => void) => T, openSettingsAt: (place: string) => void): readonly T[] {
  return Object.entries(EDITOR_PLACES).map(([command, place]) => register(command, () => { openSettingsAt(place); }));
}
