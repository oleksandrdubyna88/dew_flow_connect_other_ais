import * as vscode from 'vscode';

/**
 * Files matching a glob under a folder, reported on create, change and delete; the returned call stops watching.
 *
 * <p>Extracted from the consultant health panel when the Security lane tab needed the same thing for its prompt files
 * (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 2) — one watcher shape, not two that drift.</p>
 */
export function watchGlob(base: string, pattern: string, changed: () => void): () => void {
  const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.file(base), pattern));
  const subscriptions = [watcher, watcher.onDidCreate(changed), watcher.onDidChange(changed), watcher.onDidDelete(changed)];

  return () => {
    for (const one of subscriptions) {
      one.dispose();
    }
  };
}
