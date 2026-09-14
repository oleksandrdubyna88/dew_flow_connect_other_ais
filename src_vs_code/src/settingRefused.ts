/**
 * Why a `coai.*` setting would not save, in words the person can act on.
 *
 * <p>Pure — no `vscode` handle — because what to SAY is a decision, and every decision in this
 * extension that lived inside a webview host turned out to be a decision no test could reach.</p>
 *
 * <p><b>It exists because the phrases tab told somebody the wrong thing.</b> On 2026-09-14, pressing
 * *Add a phrase* in a window that had been open since morning produced "your settings file may be
 * read-only or held by another program". The settings file was neither. VS Code's own words —
 * discarded into `console.error` one line above the sentence the person actually read — were:</p>
 *
 * <pre>Unable to write to User Settings because coai.phrases is not a registered configuration.</pre>
 *
 * <p>The extension had been updated under the running window. The extension HOST restarted and
 * loaded the new code, which is why the tab was there to press at all; the workbench's configuration
 * registry still held the previous version's keys, and `coai.phrases` was one of six the new version
 * added. Nothing was read-only, and the cure was `Developer: Reload Window` — so the sentence sent
 * somebody to check file permissions for a problem that was a reload.</p>
 *
 * <p><b>Hence the rule: never invent a cause when the thrower named one.</b> An error's own message
 * goes through verbatim. The single case worth RECOGNISING is the one above, for two reasons: it is
 * the only refusal here with a one-click cure, and this product has now hit it twice. The first time
 * is recorded on `panelProvider.save` — three gate switches were missing from
 * `contributes.configuration`, and "the box lit up, nothing was saved, and nothing said a word".</p>
 */

export interface SettingRefusal {
  /** What the person reads. Never a guess: VS Code's own reason, or the one cause recognised below. */
  readonly text: string;
  /**
   * Whether reloading this window is the cure.
   *
   * <p>The caller decides what to do with that — the panel offers the button, a page puts it in a
   * banner — but WHICH failure it is, is decided here, once.</p>
   */
  readonly reloadCures: boolean;
}

/**
 * VS Code's wording for a key the workbench does not know.
 *
 * <p>Matched on the part that is the product's own phrasing rather than on the whole sentence: the
 * key name and the target ("User Settings", "Workspace Settings") vary, and a match anchored to all
 * three would quietly stop recognising this the first time either changed.</p>
 */
const NOT_REGISTERED = /is not a registered configuration/u;

/** The words a thrower used, whatever it was. The same shape `saveSetting` has always reported. */
function saidBy(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function settingRefusal(key: string, error: unknown): SettingRefusal {
  const said = saidBy(error);

  if (NOT_REGISTERED.test(said)) {
    return {
      text: `ConnectOtherAIs was updated while this window was open, so the window does not know `
        + `"coai.${key}" yet and refuses to store it. Reload the window (Developer: Reload Window) `
        + `and the change will save.`,
      reloadCures: true,
    };
  }

  return { text: `ConnectOtherAIs could not save "coai.${key}": ${said}`, reloadCures: false };
}
