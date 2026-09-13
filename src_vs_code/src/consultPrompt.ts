/**
 * The consultant's own prompt — the one control here whose value is a FILE rather than a setting.
 *
 * <p>`coai-mcp` reads its prompts override-first out of its own data directory (`RolePrompts`), so
 * the text a person edits belongs at `<dataDir>/prompts/consult.md` and nowhere else. Mirroring it
 * from a `coai.*` setting would have given the same words two homes and one of them authoritative;
 * the first hand-edit of the file — which is a documented way to change a prompt — would then have
 * been reverted by whichever window synced next.</p>
 *
 * <p>Pure and `vscode`-free: the decision below is a unit test, and the filesystem belongs to the
 * provider, like every other file this panel touches.</p>
 */

/** Where the server looks, relative to its data directory. `RolePrompts.OverrideDir` + the id. */
export const CONSULT_PROMPT_PATH: readonly string[] = ['prompts', 'consult.md'];

/** What a write of the box means: words to keep, or an override to take away. */
export type ConsultPromptWrite =
  | { readonly kind: 'write'; readonly text: string }
  | { readonly kind: 'remove' };

/**
 * An emptied box REMOVES the override rather than writing an empty prompt.
 *
 * <p>Because a prompt file that exists and says nothing is the one state the server cannot read as
 * "use the shipped default" — `RolePrompts.Has` was changed to ask for TEXT rather than a file for
 * exactly this, and a reviewer launched with nothing to review by is what it cost. Whitespace counts
 * as empty for the same reason: a box somebody cleared usually keeps a newline.</p>
 *
 * <p>Text that is kept is written VERBATIM, never trimmed. A prompt's trailing blank line is the
 * author's, and the composer puts its own sections after it.</p>
 */
export function consultPromptWrite(raw: unknown): ConsultPromptWrite {
  const text = typeof raw === 'string' ? raw : '';

  return text.trim().length === 0 ? { kind: 'remove' } : { kind: 'write', text };
}

/**
 * What the box shows: whatever the server would actually read, or nothing.
 *
 * <p>An unreadable file is shown as an empty box rather than as an error, and that is honest here:
 * the panel cannot say what the shipped prompt says — it is embedded in the binary — so "empty
 * means the shipped one" is the only sentence it can make true. The hint beside the box says it.</p>
 */
export function consultPromptShown(file: string | undefined): string {
  return typeof file === 'string' ? file : '';
}
