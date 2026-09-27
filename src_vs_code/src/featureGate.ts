/**
 * The feature gate on the panel's side (story S3.3 of `todo/PLAN_feature_review.md`, §4.14): which
 * vendors review a whole FEATURE, and whether the installed server can run the stage at all.
 *
 * <p>Pure and `vscode`-free, so the rules are unit tests rather than a paint. `vendors.ts` imports from
 * here; this file names only a structural row type, so there is no import cycle (`importCycles.test.mjs`
 * froze the count this repository had, and a new one bundles and fails at run time).</p>
 */

import { compareVersions } from './coaiInstall';

/** The two things this file asks of a vendor row — structural, because `vendors.ts` imports from here. */
export interface FeatureRow {
  readonly runtime: string;
  readonly feature?: boolean | undefined;
}

/**
 * The first `coai-mcp` that has the feature stage.
 *
 * <p>The release the stage ships in: the next minor after `mcp-v0.38.0`, the newest tag when this was
 * written (2026-09-26), which does not contain S2.1's stage — checked by ancestry, not assumed from the
 * date. If another release is cut before the feature epics merge, move this with it. An older server has no
 * `FeatureReview` stage and ignores the `feature` field it does not know, so a tick crossing to it is
 * harmless on the wire — it is kept out anyway, like the api gate's rows, so the settings file never
 * claims a tick the running server cannot honour and the card can say why the box is off.</p>
 */
export const FEATURE_SINCE = '0.39.0';

/** The sentence a Team server row carries beside its switched-off box (D10: v1 runs no feature review remotely). */
export const TEAM_SERVER_FEATURE_NOTE = 'Team servers do not run feature reviews yet.';

/**
 * Whether the installed server runs the feature stage — true for an UNKNOWN version too.
 *
 * <p>Unknown is not old, as in every other skew gate here: dropping a person's tick on a guess would
 * send the next feature review nowhere on a server perfectly able to run it.</p>
 */
export function featureOnServer(installedServerVersion: string): boolean {
  return installedServerVersion.length === 0 || compareVersions(FEATURE_SINCE, installedServerVersion) <= 0;
}

/**
 * Whether this vendor reviews features: only a written `true`, and never a Team server row.
 *
 * <p><b>Absent is NO</b>, unlike the plan and code ticks, because the server reads it that way
 * (`ProviderSettings.Feature`, S2.1): a settings file written before the stage existed must not start
 * sending whole features to every configured vendor. <b>A Team server is never asked</b> (D10) —
 * `ProviderSettings.Serves(FeatureReview)` is `Feature && !IsRemote`, so a tick there would be a box
 * the round ignores.</p>
 */
export function reviewsFeatures(vendor: FeatureRow): boolean {
  return vendor.feature === true && vendor.runtime !== 'remote';
}

/**
 * Why a card's feature switch is OFF — a Team server row, or a server too old for the stage — or
 * nothing when the switch is live (including against a server the panel cannot version).
 */
export function featureNote(vendor: FeatureRow, installedServerVersion: string): string {
  if (vendor.runtime === 'remote') {
    return TEAM_SERVER_FEATURE_NOTE;
  }

  return featureOnServer(installedServerVersion)
    ? ''
    : `The coai-mcp you have installed (${installedServerVersion}) has no feature review; it runs from ${FEATURE_SINCE} `
      + '— update it in the MCP server section below.';
}
