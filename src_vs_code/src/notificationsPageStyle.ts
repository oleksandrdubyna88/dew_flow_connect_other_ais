/**
 * The notifications page's stylesheet, as a string.
 *
 * <p>Its own module from the first commit, which is the shape `PLAN_two_files_outgrew_the_rule.md`
 * is moving the other two pages towards: 322 lines of CSS sitting in the middle of `panelView.ts`
 * and 149 in `roundsLog.ts` are most of why those files are twice the size the rule allows. A new
 * page joins no backlog.</p>
 *
 * <p>Every colour comes from a `--vscode-*` variable, so the page follows the theme a person chose
 * rather than deciding for them; nothing here hard-codes a hue.</p>
 */
export const PAGE_STYLE = `
  body {
    font-family: var(--vscode-font-family);
    font-size: var(--vscode-font-size);
    color: var(--vscode-foreground);
    background: var(--vscode-editor-background);
    margin: 0 16px 24px;
  }
  h1 { font-size: 1.2em; margin: 16px 0 8px; }
  .scope, .ack { margin: 4px 0; opacity: .85; }
  .scope code { opacity: 1; }
  .failed { color: var(--vscode-errorForeground); }
  .warn { color: var(--vscode-editorWarning-foreground); margin-left: 8px; }

  .filters { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin: 10px 0; }
  .filters label { display: flex; gap: 4px; align-items: center; }
  input, select, button {
    font-family: inherit; font-size: inherit;
    color: var(--vscode-input-foreground);
    background: var(--vscode-input-background);
    border: 1px solid var(--vscode-input-border, transparent);
    padding: 2px 6px;
  }
  button { color: var(--vscode-button-foreground); background: var(--vscode-button-background); cursor: pointer; }
  button:disabled { opacity: .5; cursor: default; }

  .tabs { display: flex; flex-wrap: wrap; gap: 2px; margin: 12px 0 0; }
  [role="tab"] {
    background: transparent; color: var(--vscode-foreground);
    border: 1px solid transparent; border-bottom: none; padding: 4px 10px;
  }
  [role="tab"][aria-selected="true"] {
    background: var(--vscode-editorWidget-background);
    border-color: var(--vscode-widget-border);
  }
  /* A tab with nothing under it is dimmed rather than removed: a person looking for storms wants
     to see that there are none, not to wonder where the tab went. */
  [role="tab"].empty { opacity: .5; }
  .many { opacity: .7; }

  [data-section] {
    border: 1px solid var(--vscode-widget-border);
    padding: 8px;
    overflow-x: auto;
  }
  table { border-collapse: collapse; width: 100%; }
  /* Sticky, because the point of eight columns is lost the moment the headings scroll away. */
  thead th { position: sticky; top: 0; background: var(--vscode-editor-background); z-index: 1; }
  th, td { text-align: left; padding: 3px 8px; border-bottom: 1px solid var(--vscode-widget-border); vertical-align: top; }
  th.figure, td.figure { text-align: right; white-space: nowrap; }
  th .sort { background: none; color: inherit; border: none; padding: 0; font-weight: 600; }
  th[aria-sort="ascending"] .sort::after { content: " \\2191"; }
  th[aria-sort="descending"] .sort::after { content: " \\2193"; }
  /* A row nobody has opened yet is marked, because the whole feature exists for the one message
     somebody missed. */
  tr[data-read="no"] td:first-child { border-left: 3px solid var(--vscode-editorWarning-foreground); padding-left: 5px; }
  .empty-note { opacity: .7; font-style: italic; margin: 6px 0; }
  .pager { display: flex; gap: 10px; align-items: center; margin: 10px 0; }

  .sr-only {
    position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px;
    overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; border: 0;
  }
`;
