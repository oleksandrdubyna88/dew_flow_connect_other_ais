/**
 * The block a person pastes into their MCP client — offered on the clipboard, never written into
 * another program's config file (the config belongs to the client, a person may run several, and
 * they should SEE what they are granting; creds' `mcpClientConfig.ts` reasoning, kept).
 *
 * <p>Pure. The path travels through `JSON.stringify`, which is what makes `C:\Users\…` survive —
 * hand-built strings hold invalid escapes and the client reports a malformed config instead of a
 * bad path.</p>
 */

/** Where the block goes, in the form a person would type it. */
export const CLIENT_TARGETS: readonly { label: string; path: string }[] = [
  { label: 'Claude Code (this machine)', path: '~/.claude.json' },
  { label: 'Claude Code (one project)', path: '<project>/.mcp.json' },
  { label: 'VS Code', path: '.vscode/mcp.json' },
];

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
