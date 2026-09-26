import { escapeHtml } from './escapeHtml';
import { HELP, HelpKey } from './help';

/**
 * Two controls every section of the panel draws, in a module of their own.
 *
 * <p>Moved out of `panelView.ts` when a section in ANOTHER module needed them — the consultation
 * cadence (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.1). Copying them would have given the page's
 * script and styles two markups to rely on; importing `panelView` from a module `panelView` imports
 * would have been a cycle. So they live here, and both import them.</p>
 */

/**
 * A setting with a few named values, as the segmented control the panel draws for them. ONE place,
 * extracted when a second such choice arrived (issue #131) beside `codeWorkspace`, so the markup the
 * page's script and styles rely on cannot drift between the two. Values and labels are the panel's
 * own literals.
 */
export function segmentedRadio(setting: string, current: string, label: string, choices: readonly (readonly [string, string])[]): string {
  const option = ([value, words]: readonly [string, string]): string =>
    `<label class="${current === value ? 'on' : ''}"><input type="radio" name="${setting}" data-setting="${setting}" value="${value}"${current === value ? ' checked' : ''}> ${words}</label>`;

  return `<div class="seg" role="radiogroup" aria-label="${label}">
      ${choices.map(option).join('\n      ')}
    </div>`;
}

/**
 * The little "?" that explains a setting on hover.
 *
 * <p>A native `title` rather than a scripted popup: it works with the keyboard, it cannot escape
 * the webview, and it needs no state of its own.</p>
 */
export function help(key: HelpKey): string {
  return `<span class="help" title="${escapeHtml(HELP[key])}" role="img" aria-label="What this means">?</span>`;
}
