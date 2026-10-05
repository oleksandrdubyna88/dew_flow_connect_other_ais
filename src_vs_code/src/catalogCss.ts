import { SETTINGS_CSS } from './settingsPage';

/**
 * The new Settings page's stylesheet (todo/PLAN_one_model_catalog.md, E3), from the accepted mockup (`new_design/`).
 *
 * <p>The mockup named its own colours with Dark Modern and Light Modern values; here every token is the editor's own
 * theme variable, so a light, dark or high-contrast theme needs nothing of this sheet. It extends the old page's sheet
 * (tabs, the text controls, `[hidden]` winning) rather than copying it.</p>
 */
const TOKENS = `
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

/** The shell: the two levels of tabs, the panes, the dialog and the shared marks. */
const SHELL = `
  .catalog .pane { max-width: none; }
  .catalog .pane .tabs { margin: 10px 0 6px; gap: 6px; border-bottom: none; }
  .catalog .pane .tabs .tab {
    border: 1px solid var(--border-strong); border-radius: 14px; padding: 3px 12px; color: var(--muted);
  }
  .catalog .pane .tabs .tab.on { color: var(--vscode-foreground); border-color: var(--focus); background: var(--secondary); }
  .settingsHead .preview-badge {
    font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.06em; vertical-align: middle;
    border: 1px solid var(--vscode-textLink-foreground); color: var(--vscode-textLink-foreground); border-radius: 3px; padding: 0 5px;
  }
  .settingsHead button { width: auto; margin: 0; }
  .catalog .hint { color: var(--muted); }
  .catalog button { width: auto; }
  .tag-new {
    font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.05em; vertical-align: middle;
    border: 1px solid var(--link); color: var(--link); border-radius: 3px; padding: 0 4px;
  }
  .skew {
    border-left: 3px solid var(--err); background: color-mix(in srgb, var(--err) 8%, transparent);
    padding: 4px 8px; margin: 6px 0; border-radius: 0 3px 3px 0;
  }
  dialog#confirm-dialog {
    background: var(--vscode-editorWidget-background); color: var(--vscode-foreground);
    border: 1px solid var(--border-strong); border-radius: 6px; padding: 14px 16px; width: min(560px, calc(100vw - 32px));
  }
  dialog#confirm-dialog::backdrop { background: rgba(0, 0, 0, 0.45); }
  dialog#confirm-dialog h2 { font-size: 1.1rem; margin: 0 0 8px; }
  dialog#confirm-dialog p { white-space: pre-line; }
  .dialog-buttons { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }
  .dialog-buttons button { width: auto; margin: 0; }
  #confirm-go.danger { background: var(--err); color: var(--vscode-button-foreground, #fff); }`;

/** The whole sheet the new page carries beside the shared one `pageDocument` draws. */
export const CATALOG_CSS = `${SETTINGS_CSS}${TOKENS}${SHELL}`;
