/**
 * Where a prompt's TEXT lives, and the one function that turns an id into that path.
 *
 * <p><b>A body is a file, not a setting.</b> `<dataDir>/prompts/<id>.md` is where `RolePrompts` on
 * the server has read overrides from since before roles were data — `Has`, `Text` and `FileToWrite`
 * all name it, and deleting the file is how a shipped prompt goes back to what it ships with. The
 * extension writes the same path, as it already writes the Team-server token file into the same
 * directory: the *one interface neither container owns* pattern `architecture.md` records twice.</p>
 *
 * <p>Keeping the body out of the setting matters for a reason a person feels. Twenty-five prompts of
 * prose in `settings.json` would be copied into every `mcpServers` block they paste, mirrored into
 * every settings file, and shown to them in the settings editor as a wall of JSON with their own
 * writing inside it.</p>
 *
 * <p><b>An id reaches a path here, so this is a boundary.</b> The page generates slugs and
 * `RoleComposition` refuses anything else, which makes a bad id unreachable in theory — and this is
 * the place that actually opens the file, so it refuses one itself. The same argument the server's
 * `FileOf` records for its own guard: a rule held up only by another rule is one rename from
 * neither.</p>
 */

/** A prompt id is a slug: lower-case, digits and hyphens, starting with a letter or a digit. */
const PROMPT_ID = /^[a-z0-9][a-z0-9-]*$/;

/** Names Windows will not give a file, whatever the extension. Mirrors the server's own list. */
const RESERVED = new Set([
  'con', 'prn', 'aux', 'nul',
  'com1', 'com2', 'com3', 'com4', 'com5', 'com6', 'com7', 'com8', 'com9',
  'lpt1', 'lpt2', 'lpt3', 'lpt4', 'lpt5', 'lpt6', 'lpt7', 'lpt8', 'lpt9',
]);

/** The folder the server reads overrides from, under the data directory. */
export function promptsDir(dataDir: string): string {
  return `${dataDir}/prompts`;
}

/**
 * The file this prompt's text belongs in — or nothing, for an id that may not become a path.
 *
 * <p>Nothing rather than a sanitised path: a caller holding an id this refuses is a caller whose id
 * came from somewhere it should not have, and quietly writing `....escaped.md` would hide that.</p>
 */
export function promptFile(dataDir: string, promptId: string): string | undefined {
  if (!PROMPT_ID.test(promptId) || RESERVED.has(promptId)) {
    return undefined;
  }

  return `${promptsDir(dataDir)}/${promptId}.md`;
}
