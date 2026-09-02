/**
 * The block a person pastes into their MCP client — offered on the clipboard, never written into
 * another program's config file (the config belongs to the client, a person may run several, and
 * they should SEE what they are granting; creds' `mcpClientConfig.ts` reasoning, kept).
 *
 * <p>Pure. The path travels through `JSON.stringify`, which is what makes `C:\Users\…` survive —
 * hand-built strings hold invalid escapes and the client reports a malformed config instead of a
 * bad path.</p>
 */

/**
 * Where the block goes, in the form a person would type it, and what can quietly overrule it.
 *
 * <p><b>`note` exists because of a real hour lost.</b> Claude Code reads `~/.claude.json` at two
 * levels: a top-level `mcpServers` object (user scope) and a per-project one under
 * `projects["…"].mcpServers` (local scope). The project entry WINS, silently — so somebody who
 * pastes at the top level, restarts, and finds nothing changed has no way to tell from the file
 * that their paste was read and outranked. Naming it costs one sentence.</p>
 */
export const CLIENT_TARGETS: readonly { label: string; path: string; note: string }[] = [
  {
    label: 'Claude Code (this machine)',
    path: '~/.claude.json',
    note: 'at the top level. An entry under projects["<your repo>"].mcpServers.coai in the same file '
      + 'takes precedence over it — check there first, and edit that one if it exists.',
  },
  { label: 'Claude Code (one project)', path: '<project>/.mcp.json', note: '' },
  { label: 'VS Code', path: '.vscode/mcp.json', note: '' },
];

/** The targets as one line for a notification, each carrying its caveat when it has one. */
export function clientTargetsLine(targets: readonly { label: string; path: string; note: string }[]): string {
  return targets
    .map((t) => (t.note.length === 0 ? `${t.label} (${t.path})` : `${t.label} (${t.path}) — ${t.note}`))
    .join('; ');
}

/**
 * The `mcpServers` block for the `coai` server id — the id that namespaces every tool as
 * `mcp__coai__…`, which is why it is short. `env` is omitted when empty: a field that does
 * nothing invites the question of what it is for.
 */
export function mcpServerBlock(binaryPath: string, env: Readonly<Record<string, string>>): string {
  const server =
    Object.keys(env).length === 0 ? { command: binaryPath } : { command: binaryPath, env };
  return JSON.stringify({ mcpServers: { coai: server } }, null, 2);
}

/** What the person is told once the binary is in place. */
export function installedMessage(binaryPath: string): string {
  return (
    `coai-mcp is installed at ${binaryPath}. ` +
    'Its configuration is on your clipboard — paste it into your MCP client and restart it. ' +
    'That is a one-time paste: settings you change later are saved for the server itself.'
  );
}
