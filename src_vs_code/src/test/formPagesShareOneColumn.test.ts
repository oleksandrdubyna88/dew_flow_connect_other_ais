import assert from 'node:assert/strict';
import { test } from 'node:test';

import { chatPresetsHtml } from '../chatPresetsPage';
import { commandsHtml } from '../commandsPage';
import { rolesHtml } from '../rolesPage';
import { stylesheet, type Rule } from './cssRules';

/**
 * Gate commands and Review roles sit in Chat presets' column, and Gate commands looks like Chat presets —
 * themed fields, a card per command (`research/PLAN_every_page_reads_alike.md`, S3). Read through the PARSED
 * sheet: what a page looks like is decided by which rule wins, not by which text is present.
 */

const PRESETS = chatPresetsHtml({ prompts: [], models: [], providers: [], unreadable: [], uiScale: 0 }, 'n');
const COMMANDS = commandsHtml({ rows: [], texts: {}, serverVersion: '', perSide: false }, 'n');
const ROLES = rolesHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, uiScale: 0 }, 'n');

const rulesFor = (html: string, selector: string): readonly Rule[] =>
  stylesheet(html).filter((rule) => rule.selector.split(',').map((part) => part.trim()).includes(selector));

/** The value the LAST rule naming `selector` gives `property` — the one that wins at equal specificity. */
function winning(html: string, selector: string, property: string): string {
  const found = rulesFor(html, selector)
    .flatMap((rule) => [...rule.body.matchAll(new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`, 'g'))].map((m) => m[1]!.trim()));

  return found.at(-1) ?? '';
}

for (const [name, html] of [['Gate commands', COMMANDS], ['Review roles', ROLES]] as const) {
  test(`${name} sits in the Chat presets column: 900px, centred, on the editor background`, () => {
    assert.equal(winning(html, 'body', 'max-width'), winning(PRESETS, 'body', 'max-width'));
    assert.equal(winning(html, 'body', 'max-width'), '900px');
    assert.equal(winning(html, 'body', 'margin'), '0 auto');
    assert.equal(winning(html, 'body', 'background'), 'var(--vscode-editor-background)');
  });
}

test('Gate commands draws its fields in the theme\'s colours, not the browser\'s white boxes', () => {
  assert.equal(winning(COMMANDS, 'textarea', 'background'), 'var(--vscode-input-background)');
  assert.equal(winning(COMMANDS, 'input', 'color'), 'var(--vscode-input-foreground)');
  assert.equal(winning(COMMANDS, 'button', 'background'), 'var(--vscode-button-background)');
});

test('Gate commands keeps its text boxes in the editor font, which the fields\' font: inherit would reset', () => {
  const textarea = rulesFor(COMMANDS, 'textarea');
  const inherit = textarea.findIndex((rule) => /font:\s*inherit/.test(rule.body));
  const mono = textarea.findIndex((rule) => /font-family:\s*var\(--vscode-editor-font-family\)/.test(rule.body));

  assert.ok(inherit >= 0 && mono >= 0, 'the fields rule or the monospace rule is missing');
  assert.ok(mono > inherit, 'the monospace rule comes BEFORE the fields rule, so font: inherit wins and resets it');
});

test('each command is a card, as each preset is', () => {
  assert.equal(winning(COMMANDS, '.command', 'border-left-width'), winning(PRESETS, '.preset', 'border-left-width'));
  assert.equal(winning(COMMANDS, '.command', 'border-radius'), '4px');
});
