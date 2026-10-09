import { SETTINGS_CSS } from './settingsPage';

/**
 * The Settings page's stylesheet (research/PLAN_one_model_catalog.md, E3), from the accepted mockup (`new_design/`).
 *
 * <p>The mockup named its own colours with Dark Modern and Light Modern values; here every token is the editor's own
 * theme variable, so a light, dark or high-contrast theme needs nothing of this sheet. It extends `SETTINGS_CSS`
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
  /* The old page's sections, moved whole (E4.1): the width they were written for. */
  .catalog .moved { max-width: 760px; padding: 4px 2px 16px; }
  /* The MCP server, one and a half times the size, as on the old page — the tab people read rather than set. */
  .catalog [data-pane="setup/mcp"] .moved { zoom: 1.5; }
  .catalog .used-by { margin: 8px 0 10px; color: var(--muted); }
  /* Setup's tables (E4.5): the CLIs and the MCP clients. */
  .catalog table.map { border-collapse: collapse; margin: 6px 0 12px; }
  .catalog table.map th, .catalog table.map td { text-align: left; vertical-align: top; padding: 5px 16px 5px 0; border-bottom: 1px solid var(--border); }
  .catalog table.map td button { width: auto; margin: 0 4px 0 0; }
  .catalog .moved-from { margin: 10px 0; }
  /* Roles & prompts (E4.3): the Review roles tab's own layout, scoped — the panel's sheet uses .role for Stages. */
  .roles-embed .role { padding: 6px 10px; margin: 8px 0; }
  .roles-embed .role > summary { cursor: pointer; display: flex; align-items: baseline; gap: 8px; }
  .roles-embed .role .title { font-weight: 600; }
  .roles-embed .role .id, .roles-embed .badge { font-size: 0.82em; opacity: 0.65; }
  .roles-embed .badge { border: 1px solid var(--border); border-radius: 3px; padding: 0 4px; }
  .roles-embed .fields { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin: 8px 0; }
  .roles-embed .fields label { display: flex; gap: 6px; align-items: center; }
  .roles-embed .fields input[type="text"], .roles-embed .fields select { width: auto; min-width: 14em; }
  .roles-embed .hint { flex-basis: 100%; }
  .roles-embed .prompt { border-left: 2px solid var(--border); padding: 4px 8px; margin: 6px 0; }
  .roles-embed .prompt.mine { border: 1px solid var(--vscode-charts-green, var(--ok)); border-radius: 3px; }
  .roles-embed .prompt .head { display: flex; gap: 6px; margin-bottom: 4px; }
  .roles-embed .prompt .head input { flex: 1; width: auto; }
  .roles-embed .prompt .head .purpose { flex: 2; }
  .roles-embed .prompt textarea { width: 100%; box-sizing: border-box; resize: vertical; }
  .roles-embed button { width: auto; margin: 4px 6px 0 0; }
  .roles-embed .prompt .head button { margin: 0; }
  /* Commands (E4.4): the Gate commands tab's cards, scoped like the roles. */
  .commands-embed .command { border-left: 3px solid var(--link); padding: 6px 10px; margin: 8px 0; background: var(--card); }
  .commands-embed .command h3 { font-size: 1em; margin: 0 0 6px; }
  .commands-embed .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-bottom: 8px; }
  .commands-embed .row input[type="text"] { flex: 1 1 12rem; min-width: 0; width: auto; }
  .commands-embed .row select { width: auto; }
  .commands-embed textarea { width: 100%; box-sizing: border-box; font-family: var(--vscode-editor-font-family); }
  .commands-embed button { width: auto; margin: 6px 6px 0 0; }
  .commands-embed .note, .commands-embed .lead { color: var(--muted); }
  .commands-embed .badge { font-size: 0.8em; color: var(--link); }
  /* Chat (E4.6b): the opening model's blocks and the prompt presets, scoped like the commands. */
  .chat-embed .block { border-left: 3px solid var(--border-strong); padding: 6px 10px; margin: 8px 0; background: var(--card); }
  .chat-embed .block label.inline { display: flex; gap: 8px; align-items: baseline; justify-content: flex-start; }
  .chat-embed .block label.inline .hint { flex-basis: auto; }
  .chat-embed .block label.field { display: block; margin-top: 6px; }
  .chat-embed .block label.field > span { display: block; margin-bottom: 2px; color: var(--muted); }
  .chat-embed .block.stranded { border-left-color: var(--vscode-editorWarning-foreground, var(--link)); }
  .chat-embed .preset { border-left: 3px solid var(--link); padding: 6px 10px; margin: 8px 0; background: var(--card); }
  .chat-embed .preset .head { display: flex; gap: 8px; align-items: center; margin-bottom: 6px; }
  .chat-embed .preset .head input[type="text"] { flex: 1 1 12rem; min-width: 0; width: auto; }
  .chat-embed textarea { width: 100%; box-sizing: border-box; resize: vertical; }
  .chat-embed button { width: auto; margin: 6px 6px 0 0; }
  .chat-embed .preset .head button { margin: 0; }
  .chat-embed .note, .chat-embed .lead { color: var(--muted); }
  .roles-embed button.remove, .roles-embed button.restore, .commands-embed button.remove, .chat-embed button.remove {
    background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground);
  }
  .catalog .used-by button.link {
    background: none; border: none; padding: 0; margin: 0 0 0 6px; color: var(--link); cursor: pointer; text-decoration: underline;
  }
  .catalog .pane .tabs { margin: 10px 0 6px; gap: 6px; border-bottom: none; }
  .catalog .pane .tabs .tab {
    border: 1px solid var(--border-strong); border-radius: 14px; padding: 3px 12px; color: var(--muted);
  }
  .catalog .pane .tabs .tab.on { color: var(--vscode-foreground); border-color: var(--focus); background: var(--secondary); }
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

/**
 * The Models tab (E3.2): the toolbar, the three chip rows, and the cards — two columns from 1100 px, each card four rows
 * of the shared grid through `subgrid`, so the same part of two neighbouring cards starts on the same line and the cards
 * end together (the mockup's check 3). The columns are {@link COLUMNS}.
 */
const MODELS = `
  .catalog .lead { margin: 4px 0 10px; }
  .catalog .toolbar { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; margin-bottom: 10px; }
  .catalog .toolbar .spacer { flex: 1; }
  .catalog .toolbar label { display: inline-flex; gap: 6px; align-items: center; color: var(--muted); white-space: nowrap; }
  .catalog .toolbar input[type="search"] { width: 16em; }
  .catalog .filters { display: grid; gap: 6px; margin-bottom: 14px; }
  .catalog .chip-row { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
  .catalog .chip-row .group { color: var(--muted); min-width: 6em; }
  .catalog .chip {
    display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--border-strong); border-radius: 12px;
    padding: 2px 10px; background: transparent; color: var(--vscode-foreground); cursor: pointer; margin: 0;
  }
  .catalog .chip:hover { background: var(--hover); }
  .catalog .chip[aria-pressed="true"] { border-color: var(--focus); background: color-mix(in srgb, var(--focus) 12%, transparent); }
  .catalog .chip .n { color: var(--muted); font-variant-numeric: tabular-nums; }
  .catalog .chip.empty { border-style: dashed; color: var(--warn); }
  .catalog .card {
    display: grid; grid-row: span 4; grid-template-rows: subgrid; row-gap: 0; background: var(--card);
    border: 1px solid var(--border); border-left: 3px solid var(--vc, var(--border-strong)); border-radius: 4px; padding: 12px 14px 10px;
  }
  .catalog .card.disabled { opacity: 0.62; }
  .catalog .card-head { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 4px 10px; align-items: start; }
  .catalog .card-head .name-edit { font-weight: 600; width: 100%; }
  .catalog .card-head .sub { color: var(--muted); font-size: 0.9em; margin-top: 2px; }
  .catalog .card .actions { display: flex; gap: 4px; }
  .catalog .card .actions button { margin: 0; }
  .catalog .card .actions .ask { background: transparent; color: var(--err); border: 1px solid transparent; padding: 0 6px; }
  .catalog .card .actions .ask:hover { border-color: var(--err); }
  .catalog .card .actions .ask:disabled { color: var(--muted); }
  .catalog .badges { display: flex; flex-wrap: wrap; gap: 6px; margin: 8px 0 2px; }
  .catalog .badge { border: 1px solid var(--border-strong); border-radius: 3px; padding: 0 7px; font-size: 0.88em; }
  .catalog .badge.verdict.ok, .catalog .badge.health.ok { border-color: color-mix(in srgb, var(--ok) 60%, transparent); }
  .catalog .badge.health.ok::before { content: "● "; color: var(--ok); }
  .catalog .badge.health.warn::before { content: "● "; color: var(--warn); }
  .catalog .badge.health.err::before { content: "● "; color: var(--err); }
  .catalog .badge.health.busy::before { content: "◌ "; color: var(--link); }
  .catalog .world { display: flex; flex-wrap: wrap; gap: 4px; margin: 4px 0 0; }
  .catalog .world button { margin: 0; width: auto; }
  /* A model CARD's block: Chat draws its models as .block too, and a page-wide .block gave them this line and space. */
  .catalog .card .block { border-top: 1px solid var(--border); margin-top: 10px; padding-top: 8px; }
  .catalog .block-title { font-size: 0.85em; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); margin: 0 0 6px; opacity: 1; }
  .catalog .boxes { display: flex; flex-wrap: wrap; gap: 4px 14px; }
  .catalog .feat { display: inline-flex; gap: 6px; align-items: center; white-space: nowrap; }
  .catalog .feat.off { color: var(--muted); text-decoration: line-through; text-decoration-color: var(--border-strong); }
  .catalog .notes-list { margin: 6px 0 0; padding: 0; list-style: none; font-size: 0.9em; color: var(--muted); }
  .catalog .settings-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px 14px; align-items: start; }
  .catalog .settings-grid .wide { grid-column: 1 / -1; }
  @media (max-width: 560px) { .catalog .settings-grid { grid-template-columns: minmax(0, 1fr); } }
  .catalog .card textarea { width: 100%; box-sizing: border-box; }
  .catalog .card-foot { align-self: start; }
  .catalog details.more { margin-top: 8px; border-top: 1px solid var(--border); padding-top: 6px; }
  .catalog details.more summary { cursor: pointer; color: var(--muted); }
  .catalog .used-by { margin-top: 8px; font-size: 0.9em; color: var(--muted); }
  .catalog .empty-state { border: 1px dashed var(--border-strong); border-radius: 4px; padding: 24px; text-align: center; color: var(--muted); }`;

/**
 * The columns (`cardColumns.ts`, 2026-10-09): Models' cards, and the repeated cards of Reviews (every sub-tab but Limits),
 * Security lane and Chat — one column, two from 1100 px. Models' own rule, made the page's: ONE breakpoint for every tab.
 *
 * <p>Last in the sheet, so the cells' margins it clears win over the single-column margins the places' own rules give
 * their cards (`.chat-embed .block`, `.commands-embed .command`, `.roles-embed .prompt` — the same weight, written earlier).
 * On a wide editor a place with columns leaves the 760 px `.moved` column, which left the right half of the screen
 * empty; on a narrow one it keeps it.</p>
 */
const COLUMNS = `
  .catalog .card-columns { display: grid; grid-template-columns: minmax(0, 1fr); gap: 14px; align-items: stretch; }
  @media (min-width: 1100px) {
    .catalog .card-columns { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .catalog .moved:has(.card-columns) { max-width: none; }
  }
  /* The gap spaces the cells. A fieldset keeps its min-content width unless told otherwise and would push its track wide. */
  .catalog .card-columns > * { margin: 0; min-width: 0; }
  /* A role's prompts: the space its prompts kept above and below themselves, kept by the grid that holds them now. */
  .roles-embed .card-columns { margin: 6px 0; }`;

/** The whole sheet the Settings page carries beside the shared one `pageDocument` draws. */
export const CATALOG_CSS = `${SETTINGS_CSS}${TOKENS}${SHELL}${MODELS}${COLUMNS}`;
