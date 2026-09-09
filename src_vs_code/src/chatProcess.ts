import { ProcessHandle, launch } from './processLauncher';
import { Vendor } from './vendors';
import { launchSpecFor } from './cliChatLaunch';
import { forget, remember } from './chatOrphans';

/**
 * How to start a turn for one vendor, as one function a session can hold.
 *
 * <p>The command was building this itself — the spec, the executable, the shell decision, the
 * working directory — which put process-launching detail in the file that coordinates a webview.
 * The day a vendor needs an environment variable or a container, this is the only file that should
 * have to change. (gemini, the second code round.)</p>
 *
 * <p><b>Every child is written down here, and struck out when it ends.</b> A chat is a vendor CLI
 * signed in as the person; closing the tab kills it and disposing the extension kills them all, but
 * neither runs when the editor is FORCE-killed. The ledger is what the next activation reads —
 * `chatLedger.ts` decides what may be killed on the strength of it, and this is the one place that
 * knows a child was born at all.</p>
 *
 * <p><b>Its own module, and the reason is a test that failed.</b> The obvious home was
 * `cliChatLaunch.ts`, next to `launchSpecFor` — and putting it there made `bundledPage.test.ts` go
 * red with `require is not defined`. `chatModels` reads the runtime list from `cliChatLaunch` and
 * the PANEL reads `chatModels`, so a `node:child_process` import there is reachable from the page
 * bundle and esbuild dutifully inlined a `require` into a webview. The guard exists for exactly
 * this, it caught it in one run, and the fix is a file the page's import graph never touches.</p>
 */
export function chatProcessFor(
  vendor: Vendor,
  home: string,
  resolved: string,
  /** Where the ledger lives. Empty means no ledger — the tests, and nothing else. */
  storageDir = '',
): (resume: string) => ProcessHandle {
  return (resume) => {
    const spec = launchSpecFor(vendor, home, resume, resolved);
    const child = launch(spec.executable, spec.args, { cwd: spec.cwd, shell: spec.shell });
    if (storageDir.length === 0) {
      return child;
    }

    // Written down BEFORE anything is asked of it, because the window this guards against is the
    // whole of the child's life — including the first second of it.
    void remember(storageDir, child.pid, spec.executable);
    const strike = (): void => {
      void forget(storageDir, child.pid);
    };
    child.onExit(strike);
    child.onError(strike);

    return child;
  };
}
