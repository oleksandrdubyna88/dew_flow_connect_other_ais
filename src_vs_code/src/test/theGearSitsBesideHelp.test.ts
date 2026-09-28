import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * The gear that opens the Settings tab sits immediately after the help button in the panel's title bar
 * (`research/PLAN_settings_page.md`, D8).
 *
 * <p>VS Code orders a view's title-bar items by the number after `navigation@`, and by TITLE where two
 * share one — so the three entries at `@0` come out *Answer the open question…* then *Help*, and nothing
 * pinned where a new icon would land. Read from the manifest, because that is the only thing VS Code
 * reads; a test of anything else would be a test of our intentions.</p>
 */

interface MenuItem {
  readonly command: string;
  readonly when?: string;
  readonly group?: string;
}

const manifest = JSON.parse(readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
  contributes: { commands: { command: string; title: string; icon?: unknown }[]; menus: { 'view/title': MenuItem[] } };
};

const titleBar = manifest.contributes.menus['view/title'].filter((item) => item.when?.startsWith('view == coai.panel') === true);

function slot(command: string): string {
  const item = titleBar.find((each) => each.command === command);
  assert.ok(item !== undefined, `${command} is not in the panel's title bar`);

  return item.group ?? '';
}

test('the gear is in the title bar straight after help, and the rounds list after it', () => {
  assert.equal(slot('coai.help'), 'navigation@0');
  assert.equal(slot('coai.openSettings'), 'navigation@1', 'the gear is not the icon after help');
  assert.equal(slot('coai.showRounds'), 'navigation@2', 'the rounds list now shares the gear\'s slot, and VS Code orders ties by title');
});

test('the gear is a gear, and the palette names it', () => {
  const command = manifest.contributes.commands.find((each) => each.command === 'coai.openSettings');

  assert.ok(command !== undefined, 'the command is not declared, so the title-bar entry points at nothing');
  assert.equal(command.icon, '$(gear)');
  assert.equal(command.title, 'Settings');
});
