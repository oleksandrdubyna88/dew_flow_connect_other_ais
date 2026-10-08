/**
 * The three commands that opened the Review roles, Gate commands and Chat presets tabs — tabs deleted by
 * todo/PLAN_one_model_catalog.md E5.1 step 4, whose editors are places of the Settings page now. Each command stays for
 * one more release as a REDIRECT to its place, the way T5 keeps the restore command: a keybinding of somebody's own,
 * or a habit, still lands somewhere useful (the plan round's finding 0).
 *
 * <p>Pure: the host hands in how to register a command and how to open the page, so a test runs every row.</p>
 */

/** Each redirected command, and the place of the Settings page that holds its editor (`catalogPlaces.ts`). */
export const EDITOR_PLACES = {
  'coai.editRoles': 'reviews/roles',
  'coai.editCommands': 'reviews/commands',
  'coai.editChatPresets': 'chat',
} as const satisfies Readonly<Record<string, string>>;

/** What a redirect needs from the host. */
export interface RedirectPorts {
  /**
   * Makes the Settings slot paint the NEW page. While the preview switch exists the current page is the default, and it
   * holds none of the three editors — so a redirect that opened "the Settings page" would land where nothing is.
   */
  readonly useTheNewPage: () => Promise<void>;
  /** Opens the one Settings tab of this window at a place, or brings it back there. */
  readonly openSettingsAt: (place: string) => void;
}

/**
 * Registers the three redirects.
 *
 * @param register the host's `registerCommand`, handed each command and what it runs
 * @returns what each registration returned (the host's disposables)
 */
export function editorRedirects<T>(register: (command: string, run: () => Promise<void>) => T, ports: RedirectPorts): readonly T[] {
  return Object.entries(EDITOR_PLACES).map(([command, place]) => register(command, async () => {
    await ports.useTheNewPage();
    ports.openSettingsAt(place);
  }));
}
