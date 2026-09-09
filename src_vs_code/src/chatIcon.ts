/**
 * Where the chat tab's icon lives, as path segments and nothing else.
 *
 * <p>Its own module because `chatPanel.ts` imports `vscode`, which does not exist outside the
 * extension host — so a constant declared there could not be read by a test, and the one failure
 * mode an icon has is a path that names a file nobody shipped. Nothing type-checks a `Uri`: it is
 * built from strings, joined, handed to the workbench, and a wrong one produces the generic tab icon
 * and no error anywhere. Here the SAME segments the panel joins are what the test opens on disk.</p>
 *
 * <p>Two files rather than one, because a tab icon cannot read the theme: it is workbench chrome,
 * `var(--vscode-…)` never reaches it, and `iconPath` takes a `Uri` or a `{ light, dark }` pair. The
 * colour is baked into each file, which is why `help-yellow.svg` / `help-yellow-light.svg` is a pair
 * already.</p>
 */
export interface IconFiles {
  /** Drawn on a light ground: `#2E8B3E`, the same hue darkened, because a bright green on white reads pale. */
  readonly light: readonly string[];
  /** Drawn on a dark ground: `#5CC46F`, palette slot 5 — the green `gemini` wears. */
  readonly dark: readonly string[];
}

export const CHAT_ICON: IconFiles = {
  light: ['media', 'chat-light.svg'],
  dark: ['media', 'chat.svg'],
};
