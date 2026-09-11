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
/**
 * @param model
 * The model the CONVERSATION is on, which is NOT the row's `vendor.model`. `readyToChat` keeps the
 * two apart deliberately — the row's is the default a person is choosing away from — and passing the
 * row's here would have been the same defect in a new place: the tab would go on labelling an answer
 * with a model the CLI was never told about. Empty means the CLI's own default, which is what both a
 * Team-server row with no model and the picker's "the first one that can answer" come to.
 */
export function chatProcessFor(
  vendor: Vendor,
  home: string,
  resolved: string,
  model: string,
): (resume: string) => ProcessHandle {
  return (resume) => {
    const spec = launchSpecFor(vendor, home, { resume, model }, resolved);
    const child = launch(spec.executable, spec.args, { cwd: spec.cwd, shell: spec.shell });

    // Written down BEFORE anything is asked of it, and SYNCHRONOUSLY: the window this guards
    // against is a force-kill, which is exactly what a queued write does not survive. A child
    // launched and orphaned a millisecond later would otherwise never have been written down.
    // Where the ledger lives is not this function's business — it was bound once, at activation.
    remember(child.pid, spec.executable);
    const strike = (): void => {
      forget(child.pid);
    };
    child.onExit(strike);
    child.onError(strike);

    return child;
  };
}
