/**
 * The new Settings design's tokens, chips and badges — the part of the Settings sheet another page may wear too.
 *
 * <p>A LEAF: it imports nothing. `catalogCss.ts` imports `settingsPage`, so a page importing that sheet for its tokens
 * would drag `SETTINGS_CSS` along with them; the Review rounds page's Team server tab takes these three and nothing
 * else (todo/PLAN_team_usage_by_person.md, story 1.3). Moved here by EXTRACTION, text unchanged and inserted where it
 * was, so the Settings page's `CATALOG_CSS` is byte-identical — `catalogSheet.test.ts` holds it to that.</p>
 *
 * <p>Every rule is scoped under `.catalog`, which is where the tokens live: a page that wants them puts the class on the
 * element they should reach.</p>
 */

/**
 * The mockup named its own colours with Dark Modern and Light Modern values; here every token is the editor's own
 * theme variable, so a light, dark or high-contrast theme needs nothing of this sheet.
 */
export const CATALOG_TOKENS = `
  .catalog {
    --ok: var(--vscode-testing-iconPassed, #5cc46f);
    --warn: var(--vscode-editorWarning-foreground, #e8b02a);
    --err: var(--vscode-errorForeground, #f48771);
    --link: var(--vscode-textLink-foreground, #4daafc);
    --muted: var(--vscode-descriptionForeground, #9d9d9d);
    --border: var(--vscode-widget-border, var(--vscode-panel-border, #2b2b2b));
    --border-strong: var(--vscode-input-border, var(--vscode-panel-border, #3c3c3c));
    --card: var(--vscode-editorWidget-background, transparent);
    --secondary: var(--vscode-button-secondaryBackground, #313131);
    --focus: var(--vscode-focusBorder, #0078d4);
    --hover: var(--vscode-list-hoverBackground, rgba(127, 127, 127, 0.08));
  }`;

/** The chips: a filter row on Models, a window or a sort elsewhere — `aria-pressed` is what marks the chosen one. */
export const CATALOG_CHIPS = `
  .catalog .chip-row { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
  .catalog .chip-row .group { color: var(--muted); min-width: 6em; }
  .catalog .chip {
    display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--border-strong); border-radius: 12px;
    padding: 2px 10px; background: transparent; color: var(--vscode-foreground); cursor: pointer; margin: 0;
  }
  .catalog .chip:hover { background: var(--hover); }
  .catalog .chip[aria-pressed="true"] { border-color: var(--focus); background: color-mix(in srgb, var(--focus) 12%, transparent); }
  .catalog .chip .n { color: var(--muted); font-variant-numeric: tabular-nums; }
  .catalog .chip.empty { border-style: dashed; color: var(--warn); }`;

/** The small bordered marks beside a card's name: a verdict, a health dot. */
export const CATALOG_BADGES = `
  .catalog .badges { display: flex; flex-wrap: wrap; gap: 6px; margin: 8px 0 2px; }
  .catalog .badge { border: 1px solid var(--border-strong); border-radius: 3px; padding: 0 7px; font-size: 0.88em; }
  .catalog .badge.verdict.ok, .catalog .badge.health.ok { border-color: color-mix(in srgb, var(--ok) 60%, transparent); }
  .catalog .badge.health.ok::before { content: "● "; color: var(--ok); }
  .catalog .badge.health.warn::before { content: "● "; color: var(--warn); }
  .catalog .badge.health.err::before { content: "● "; color: var(--err); }
  .catalog .badge.health.busy::before { content: "◌ "; color: var(--link); }`;
