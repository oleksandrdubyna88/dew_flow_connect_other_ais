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

/**
 * Whether the BUILD that is running declares a setting — the discriminator the reload advice needs.
 *
 * <p><b>Two different faults produce one sentence from VS Code.</b> "not a registered configuration"
 * means the workbench will not store the key, and there are exactly two reasons it would not: the
 * window has not caught up with an update that declares it, or this build genuinely does not declare
 * it. Reloading cures the first and does nothing whatever for the second — and the second is not
 * hypothetical here, it is how this product met this error the FIRST time, with three gate switches
 * missing from `contributes.configuration`. Telling somebody to reload then would send them round a
 * loop that cannot end. (Gate finding, codex, on this change's plan round.)</p>
 *
 * <p>Pure, over the manifest the host hands in (`context.extension.packageJSON`), so the shape-walking
 * is a unit test rather than a claim. `contributes.configuration` is allowed to be a single object or
 * an array of them, and a manifest that cannot be read at all answers `unknown` — which is neither
 * diagnosis, and deliberately offers no cure rather than guessing one.</p>
 */
export type Declared = 'declared' | 'absent' | 'unknown';

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

/**
 * `contributes.configuration` is allowed to be one section or an array of them. Both, here.
 *
 * <p>Exported for `configTransfer`, which reads every declared default from the same walk.</p>
 */
export function sectionsOf(manifest: unknown): readonly Record<string, unknown>[] {
  const configuration = asRecord(asRecord(manifest)?.['contributes'])?.['configuration'];
  const many = Array.isArray(configuration) ? configuration : [configuration];

  return many.flatMap((one) => {
    const section = asRecord(one);

    return section === undefined ? [] : [section];
  });
}

export function declaresSetting(manifest: unknown, key: string): Declared {
  const sections = sectionsOf(manifest);
  if (sections.length === 0) {
    // No manifest, or one with no configuration contribution at all. Both mean this function cannot
    // tell the two faults apart, and saying so is better than picking the likelier one.
    return 'unknown';
  }

  // `Object.hasOwn`, never `in` or a bare property read: a key named `toString` must not answer
  // "declared" because every object has one.
  return sections.some((section) => Object.hasOwn(asRecord(section['properties']) ?? {}, `coai.${key}`))
    ? 'declared'
    : 'absent';
}

/** The words a thrower used, whatever it was. The same shape `saveSetting` has always reported. */
function saidBy(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * The stale-window sentence — the only one here with a cure, and it says what the cure COSTS.
 *
 * <p><b>It used to end "and the change will save", which was false in the way that matters.</b>
 * Reloading cures the REFUSAL; it does not replay the write. It also tears down the webview, so the
 * words still sitting in the box — the ones the banner has just promised are safe — go with it. A
 * one-click button under a sentence like that is an invitation to lose work, and three reviewers
 * across two vendors said so independently in the code round. The cure is still offered, because it
 * is the only one there is; what changed is that it no longer hides its price.</p>
 */
function staleWindow(key: string, said: string): SettingRefusal {
  return {
    text: `ConnectOtherAIs was updated while this window was open, so the window does not know `
      + `"coai.${key}" yet and refuses to store it. Reloading the window (Developer: Reload Window) `
      + `is the cure — it also closes this tab, so copy anything you have typed and not saved, and `
      + `make the change again afterwards. VS Code said: ${said}`,
    reloadCures: true,
  };
}

/** The same refusal when this BUILD is the one at fault, where a reload would change nothing. */
function notInThisBuild(key: string, said: string): SettingRefusal {
  return {
    text: `ConnectOtherAIs cannot store "coai.${key}": this build of the extension does not declare `
      + `it, so reloading will not help — it is a fault in the extension rather than in your settings. `
      + `VS Code said: ${said}`,
    reloadCures: false,
  };
}

/**
 * A caller's own wording, where it has better wording than the classifier does.
 *
 * <p>Two overrides rather than one, because the two paths are not the same question. The previous
 * round had a single `instead` that won everywhere, and a reviewer found what that costs: the roles
 * page's "could not save that change to your roles" then replaced the stale-window diagnosis — hiding
 * the reason, the warning about unsaved text, and the fact that the edit has to be made again. A
 * caller cannot write that sentence itself, because it does not know which failure this is.</p>
 */
export interface RefusalSentences {
  /** For a failure with no recognised cause. The roles page's argument about errnos lives here. */
  readonly ordinary?: string;
  /** For the recognised one — only for a caller whose own BANNER is already carrying the reasoning. */
  readonly recognised?: string;
}

export interface RefusalNotice {
  readonly text: string;
  /** Whether this notification carries the reload action. Once per window; the TEXT is always shown. */
  readonly withAction: boolean;
}

/**
 * What a notification should say, and whether it carries the button.
 *
 * <p><b>Separating those two is the whole point.</b> Gating them together is a defect this module
 * introduced and a reviewer caught: with one flag over the entire notification, a second refused
 * write in a stale window said nothing at all — and for the sidebar and the roles tab, which have no
 * banner, that is silence about a save that did not happen. The ACTION is what must not repeat; the
 * reason must be said every time.</p>
 */
export function refusalNotice(
  refusal: SettingRefusal,
  sentences: RefusalSentences,
  alreadyOffered: boolean,
): RefusalNotice {
  if (!refusal.reloadCures) {
    return { text: sentences.ordinary ?? refusal.text, withAction: false };
  }

  return { text: sentences.recognised ?? refusal.text, withAction: !alreadyOffered };
}

export function settingRefusal(key: string, error: unknown, declared: Declared): SettingRefusal {
  const said = saidBy(error);

  if (NOT_REGISTERED.test(said)) {
    if (declared === 'declared') {
      return staleWindow(key, said);
    }
    if (declared === 'absent') {
      return notInThisBuild(key, said);
    }
    // 'unknown' falls through to the verbatim reason below: no invented cause, and no cure offered
    // that this code cannot stand behind.
  }

  return { text: `ConnectOtherAIs could not save "coai.${key}": ${said}`, reloadCures: false };
}
