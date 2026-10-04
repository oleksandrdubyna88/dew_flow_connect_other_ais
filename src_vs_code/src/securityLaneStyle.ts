/**
 * The Security lane tab's own stylesheet, appended to the Settings page's (research/PLAN_the_security_tab_reads_at_a_glance.md,
 * epic 3, sections A–C).
 *
 * <p>Prefixed `seclane-`: `sec-*` is the Settings page's SECTION namespace (`.sec-prompts > summary`) and `.badge` is
 * already a pill in the panel's stylesheet, so nothing here may restyle what is outside this tab. Sizes are in
 * `rem/13` like the rest of the page, so the Settings text-size control scales them; the two-column layouts are
 * column-WIDTH forms (`columns: <width> 2`, `auto-fit`), which drop to one column on their own — a `rem` media query
 * would read the initial size, not the one the person chose.</p>
 *
 * <p>The card colours are the charts tokens with the fallbacks `roleTone.ts` uses, and colour is never the only
 * signal: every card also carries a text badge (WCAG 1.4.1), drawn at full strength.</p>
 */
export const SECURITY_LANE_CSS = `
  .seclane-tags { columns: calc(240rem / 13) 2; column-gap: calc(24rem / 13); list-style: none; padding: 0; margin: calc(6rem / 13) 0; }
  .seclane-tags li { break-inside: avoid; margin: 0 0 calc(3rem / 13); }
  .seclane-prompt { border: 2px solid var(--vscode-panel-border); border-radius: 3px; margin: calc(10rem / 13) 0; padding: calc(6rem / 13) calc(10rem / 13); }
  .seclane-prompt--shipped { border-color: var(--vscode-charts-green, #b5cea8); }
  .seclane-prompt--custom { border-color: var(--vscode-charts-purple, #c586c0); }
  .seclane-prompt--edited { border-color: var(--vscode-charts-orange, #ce9178); }
  .seclane-name { font-size: calc(16rem / 13); font-weight: 600; }
  .seclane-badge { font-size: calc(11rem / 13); font-weight: 600; border: 1px solid currentColor; border-radius: 3px;
    padding: 0 calc(4rem / 13); margin-left: calc(6rem / 13); }
  .seclane-prompt--shipped .seclane-badge { color: var(--vscode-charts-green, #b5cea8); }
  .seclane-prompt--custom .seclane-badge { color: var(--vscode-charts-purple, #c586c0); }
  .seclane-prompt--edited .seclane-badge { color: var(--vscode-charts-orange, #ce9178); }
  .seclane-conditions > summary { cursor: pointer; margin: calc(4rem / 13) 0; }
  .seclane-conditions-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(calc(240rem / 13), 1fr));
    gap: calc(6rem / 13) calc(24rem / 13); }
  .seclane-conditions-grid label { display: block; }
  .seclane-conditions-grid h4 { margin: calc(4rem / 13) 0; font-size: inherit; }
  .seclane-actions { display: flex; flex-wrap: wrap; gap: calc(8rem / 13); align-items: center; margin: calc(6rem / 13) 0; }
  /* The panel's stylesheet makes every button full width with a top margin (a sidebar's); these take their own back. */
  .seclane-actions button { width: auto; margin: 0; flex: 0 0 auto; }
  /* A control that cannot be pressed must not look pressable (issue #297, roundsLog.ts): dimmed, no hand, no hover. */
  .seclane-actions button:disabled { opacity: .5; cursor: default; }
  .seclane-actions button:disabled:hover { background: var(--vscode-button-background); }
  .seclane-count { opacity: .85; }
`;
