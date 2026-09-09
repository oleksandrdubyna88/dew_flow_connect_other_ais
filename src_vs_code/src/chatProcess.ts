import { ProcessHandle, launch } from './processLauncher';
import { Vendor } from './vendors';
import { launchSpecFor } from './cliChatLaunch';

/**
 * How to start a turn for one vendor, as one function a session can hold.
 *
 * <p>The command was building this itself — the spec, the executable, the shell decision, the
 * working directory — which put process-launching detail in the file that coordinates a webview.
 * The day a vendor needs an environment variable or a container, this is the only file that should
 * have to change. (gemini, the second code round.)</p>
 *
 * <p><b>Its own module, and the reason is a test that failed.</b> The obvious home was
 * `cliChatLaunch.ts`, next to `launchSpecFor` — and putting it there made `bundledPage.test.ts` go
 * red with `require is not defined`. `chatModels` reads the runtime list from `cliChatLaunch` and
 * the PANEL reads `chatModels`, so a `node:child_process` import there is reachable from the page
 * bundle and esbuild dutifully inlined a `require` into a webview. The guard exists for exactly
 * this, it caught it in one run, and the fix is a file the page's import graph never touches.</p>
 */
export function chatProcessFor(vendor: Vendor, home: string, resolved: string): (resume: string) => ProcessHandle {
  return (resume) => {
    const spec = launchSpecFor(vendor, home, resume, resolved);

    return launch(spec.executable, spec.args, { cwd: spec.cwd, shell: spec.shell });
  };
}
