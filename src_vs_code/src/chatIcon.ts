/**
 * Where the chat TAB's icon lives, as path segments and nothing else.
 *
 * <p>Its own module because `chatPanel.ts` imports `vscode`, which does not exist outside the
 * extension host — so a constant declared there could not be read by a test, and the one failure
 * mode an icon has is a path that names a file nobody shipped. Nothing type-checks a `Uri`: it is
 * built from strings, joined, handed to the workbench, and a wrong one produces the generic tab icon
 * and no error anywhere. Here the SAME segments the panel joins are what a test opens on disk.</p>
 *
 * <p>The TAB, not the activity bar: `media/panel.svg` is the activity-bar glyph, drawn in
 * `currentColor` so the workbench can recolour it. These two are its descendants and cannot be
 * recoloured — see below.</p>
 *
 * <p>Two files rather than one, because a tab icon cannot read the theme: it is workbench chrome,
 * `var(--vscode-…)` never reaches it, and `iconPath` takes a `Uri` or a `{ light, dark }` pair. The
 * colour is baked into each file, which is why `help-yellow.svg` / `help-yellow-light.svg` is a pair
 * already.</p>
 */
export interface ThemedIcon<T> {
  /** Drawn on a light ground: `#2E8B3E`, the same hue darkened, because a bright green on white reads pale. */
  readonly light: T;
  /** Drawn on a dark ground: `#5CC46F`, palette slot 5 — the green `gemini` wears. */
  readonly dark: T;
}

/** The two files, relative to the extension's own folder. */
export const CHAT_TAB_ICON: ThemedIcon<readonly string[]> = {
  light: ['media', 'chat-light.svg'],
  dark: ['media', 'chat.svg'],
};

/**
 * The pair, resolved by whatever knows how to join a path.
 *
 * <p>A function rather than the constant alone so that the RESOLUTION can be tested: the panel hands
 * it `vscode.Uri.joinPath` and a test hands it a joiner of its own, and both then observe the same
 * two paths. Asserting the panel's source text instead would only have proved that somebody wrote a
 * string down.</p>
 */
export function chatTabIcon<T>(join: (...segments: readonly string[]) => T): ThemedIcon<T> {
  return { light: join(...CHAT_TAB_ICON.light), dark: join(...CHAT_TAB_ICON.dark) };
}
