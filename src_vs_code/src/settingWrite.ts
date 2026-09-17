import { asText } from './asText';
import { notify } from './notify';

/**
 * A view setting that could not be written says so — once, in one place, for every page.
 *
 * <p>It existed twice. `helpPanel.ts` had a private `said()` over `vscode.window.showWarningMessage`
 * — the last direct notification call left in this extension — and the review panel grew a second
 * one over `notify` when it adopted the same two controls. Three reviewers across two providers
 * named the duplication in one round, and they were right about why: a change to the wording, the
 * severity or the persistence policy would have had to find both, and until it did one page would
 * report a failed zoom save while the other handled the same failure differently.</p>
 *
 * <p><b>Through `notify`, not `showWarningMessage`.</b> The funnel is what makes a notice
 * inspectable, repeat-suppressible and countable, and the census (`notificationSites.test.mjs`) is
 * a ratchet that only ever falls — so unifying here also takes the direct count from one to zero,
 * which is the direction that file exists to enforce.</p>
 *
 * <p><b>It never waits for a person.</b> `notify` returns when the record is on DISK; a door that
 * waits for a toast to be dismissed would hold whatever the caller does next, which is the defect
 * `noticesDoNotBlock.test.ts` pins for the review panel.</p>
 *
 * @param writing the settings write already in flight
 * @param source which surface asked, so the notice says where it came from
 */
export async function settingWritten(writing: Promise<void>, source: string): Promise<void> {
  try {
    await writing;
  } catch (reason: unknown) {
    await notify({
      as: 'warning',
      class: 'failure',
      source,
      code: 'view-setting-not-saved',
      title: `That view setting could not be saved: ${asText(reason)}`,
      detail: asText(reason),
    });
  }
}
