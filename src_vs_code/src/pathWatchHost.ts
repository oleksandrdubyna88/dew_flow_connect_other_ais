import { promises as fsp } from 'node:fs';
import * as vscode from 'vscode';
import { coaiDataDir, defaultDataDir, namedRoot } from './dataDir';
import { alsoWatchDataDirectories } from './escalationWatcher';
import { hostExecutableSide } from './hostSide';
import { notifyOnceThen, notifyResolved } from './notify';
import { type PathFinding, SETTING_PATH_MISSING, findingSubject, missingCure, missingTitle } from './pathFindings';
import { PathProbes, statProbe } from './pathProbes';
import { PathWatch } from './pathWatch';
import { readerFor } from './sideConfig';
import { settingsFrom } from './settingsShape';
import { vendorsFrom } from './vendors';

/**
 * The host half of the path watch (todo/PLAN_paths_per_side.md E5.3): the settings as THIS side reads them, the real
 * disk through the bounded probes, and the notice through the funnel — once per window run and per missing path, with
 * **Open in Settings** landing on the place that draws the value. Every decision is `pathWatch.ts`'s; this is the
 * `vscode` around it.
 */

/** The one button: it runs `coai.openSettings` with the finding's place (`catalogPlaces.ts`). */
export const OPEN_IN_SETTINGS = 'Open in Settings';

/** Starts the watch over this side's paths; `activate` calls it before anything writes the data directory. */
export function startPathWatch(context: vscode.ExtensionContext): PathWatch {
  const reader = (): ReturnType<typeof readerFor> => readerFor(context, vscode.workspace.getConfiguration('coai'));
  const watch = new PathWatch({
    reads: {
      roots: () => settingsFrom(reader()).qconsult.roots,
      vendors: () => vendorsFrom(reader()('vendors')),
      watched: () => alsoWatchDataDirectories(),
      ownDataDir: ownDataDir,
      dataRoot: () => namedRoot(),
    },
    probes: new PathProbes(statProbe((path) => fsp.stat(path))),
    side: hostExecutableSide(),
    platform: process.platform,
    say: sayMissing,
    resolved: (finding) => { notifyResolved(SETTING_PATH_MISSING, findingSubject(finding)); },
    // The recheck of what was found absent (E5.7, risk item F): a folder made again is seen with no settings edit.
    every: (work, ms) => {
      const timer = setInterval(work, ms);
      timer.unref();
      context.subscriptions.push({ dispose: () => { clearInterval(timer); } });
    },
  });
  watch.start();

  return watch;
}

/** This window's own data directory — the default when a refused side makes it unresolvable, so the watch still runs. */
function ownDataDir(): string {
  try {
    return coaiDataDir();
  } catch {
    return defaultDataDir();
  }
}

/**
 * The notice at every start (todo/PLAN_paths_per_side.md, design (e)): a warning, keyed per `(key, value)` so fixing one
 * clears one, worded "not found now" and never "deleted" — an unmounted drive or a sleeping share reports absence too.
 */
function sayMissing(finding: PathFinding): void {
  void notifyOnceThen(
    {
      as: 'warning',
      class: 'failure',
      source: 'pathWatch',
      code: 'setting-path-missing',
      subject: findingSubject(finding),
      title: missingTitle(finding),
      cure: missingCure(finding),
      action: OPEN_IN_SETTINGS,
    },
    (answer) => {
      if (answer === OPEN_IN_SETTINGS) {
        void vscode.commands.executeCommand('coai.openSettings', finding.place);
      }
    },
  );
}
