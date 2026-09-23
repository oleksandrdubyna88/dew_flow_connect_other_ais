import { escapeHtml } from './escapeHtml';

/**
 * One tab of the rounds log page, built on its own so that it can only fail on its own.
 *
 * <p>`refreshRoundsLog` used to build every tab's HTML as an argument to ONE `update` call. On
 * 2026-09-23 the consultations tab threw — an unpriced consultation reached `toFixed` — and the throw
 * skipped the whole push: the spending tab's new window, the blind-spot tab and the totals were all
 * built or about to be, and none of them was sent. Somebody using the page saw two unrelated bugs,
 * a date switch that did nothing and a *What it keeps missing* tab that never opened, and neither of
 * those tabs was at fault. 64 of that TypeError in one day's extension-host log, and nothing on the
 * page said anything.</p>
 *
 * <p>So a tab that cannot be built becomes a SENTENCE in its own place — which tab, and the reason as
 * text — and everything else is pushed as usual. The reason is also written where the extension host
 * keeps its errors, because that log is where the real exception was found and a caught error must
 * not stop being findable. Free of `vscode`, so this is a test rather than a hope.</p>
 *
 * @param name what the tab is called in the sentence a person reads
 * @param build the tab's HTML, or a promise of it
 * @param warn where a failure is written down; the extension host's console by default
 */
export async function regionOr(
  name: string,
  build: () => string | Promise<string>,
  warn: (message: string, reason: unknown) => void = console.error,
): Promise<string> {
  try {
    return await build();
  } catch (reason: unknown) {
    warn(`ConnectOtherAIs: the rounds log could not build its ${name} tab`, reason);

    return brokenRegion(name, reason);
  }
}

function brokenRegion(name: string, reason: unknown): string {
  const said = reason instanceof Error ? reason.message : String(reason);

  return `<div class="empty">The ${escapeHtml(name)} tab could not be built: ${escapeHtml(said)}. `
    + 'The other tabs are still current. Please copy this sentence into an issue.</div>';
}
