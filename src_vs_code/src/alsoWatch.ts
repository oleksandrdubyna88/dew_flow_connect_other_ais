import * as vscode from 'vscode';

/** The setting that names other installations' data directories. */
export const ALSO_WATCH_SETTING = 'coai.alsoWatchDataDirectories';

/**
 * The directories named in the setting, as typed.
 *
 * <p>Trimmed and emptied here and normalised in `escalationDirs.ts`, which is where the rules a test
 * can reach live. A value that is not a list of strings is no list at all rather than a guess. (It lived in the escalation
 * watcher, which v2 E4 deleted; the path watch and the health panel still ask which directories this window answers for.)</p>
 */
export function alsoWatchDataDirectories(): readonly string[] {
  const value: unknown = vscode.workspace.getConfiguration().get(ALSO_WATCH_SETTING);

  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : [];
}
