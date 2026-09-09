import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHAT_ICON } from '../chatIcon';

/**
 * The chat tab wears this product's own glyph, in green, in both themes.
 *
 * <p>2026-09-09: every chat tab wore the generic `≡`, because `createWebviewPanel` never set
 * `iconPath` — a search of `src_vs_code/src` found not one. A person with three conversations open
 * had three tabs that looked like everything else.</p>
 *
 * <p>Structural where it has to be: `chatPanel.ts` imports `vscode`, so no test here can call it.
 * But the failure mode of an icon is not logic, it is a PATH — nothing type-checks a `Uri`, a wrong
 * one produces the generic icon and no error anywhere — so the segments the panel joins live in
 * `chatIcon.ts`, with no `vscode` import, and this file opens them on disk. A regex over the source
 * could only have proved that some string was written down.</p>
 */

const EXTENSION_ROOT = path.join(__dirname, '..', '..');
const read = (file: string): string => fs.readFileSync(path.join(EXTENSION_ROOT, file), 'utf8');

test('both icon files are where the panel will look for them', () => {
  for (const segments of [CHAT_ICON.light, CHAT_ICON.dark]) {
    const file = path.join(EXTENSION_ROOT, ...segments);

    assert.ok(fs.existsSync(file), `${segments.join('/')} is named by the panel and is not in the tree`);
    assert.match(fs.readFileSync(file, 'utf8'), /^<svg /, `${segments.join('/')} is not an svg`);
  }
});

test('each icon is drawn for the ground it sits on, full bleed', () => {
  // The colours are the decision, so they are the assertion. `#5CC46F` is palette slot 5 — the green
  // `gemini` wears, picked with eleven others to stay apart in a light theme as well as a dark one;
  // `#2E8B3E` is the same hue darkened, derived the way `help-yellow-light.svg` was from
  // `help-yellow.svg`, because a bright colour on white reads pale. Measured against the grounds they
  // actually sit on: 4.30:1 on white and 3.64:1 on a Light+ tab, 7.61:1 on Dark+ and 8.10:1 on Dark
  // Modern — every one of them past the 3:1 that WCAG asks of a graphical object.
  const dark = read(path.join(...CHAT_ICON.dark));
  const light = read(path.join(...CHAT_ICON.light));

  assert.match(dark, /fill="#5CC46F"/);
  assert.match(light, /fill="#2E8B3E"/);
  assert.ok(!light.includes('#5CC46F'), 'the light icon still carries the dark theme’s green');

  // "Big" is not a size the workbench lets you ask for: it draws a tab icon at its own fixed size, and
  // the Welcome tab's looks big because its glyph fills the box edge to edge. So the viewBox is cut to
  // the content and the strokes are heavy — a hairline at 16 px is a smudge.
  for (const svg of [dark, light]) {
    assert.match(svg, /viewBox="2\.7 0\.85 18\.6 18\.6"/, 'the viewBox is not cut to the glyph');
    assert.match(svg, /stroke-width="1\.7"/, 'the lines are back to the activity bar’s hairline');
    assert.match(svg, /stroke-width="1\.8"/, 'the rings are back to the activity bar’s hairline');
  }
});

test('the chat tab asks for that icon, in both variants, from the extension’s own folder', () => {
  const source = read(path.join('src', 'chatPanel.ts'));

  assert.match(source, /panel\.iconPath = \{/, 'no iconPath is set, so the tab wears the generic glyph');
  assert.match(source, /light: vscode\.Uri\.joinPath\(extensionUri, \.\.\.CHAT_ICON\.light\)/);
  assert.match(source, /dark: vscode\.Uri\.joinPath\(extensionUri, \.\.\.CHAT_ICON\.dark\)/);
  // Built from the SHARED segments, not from a path written out a second time here — the second copy
  // is the one that goes stale when a file is renamed, and nothing would say so.
  assert.ok(!/['"]media\/chat/.test(source), 'the panel spells the icon path out instead of sharing it');
});

test('the extension URI is handed to the panel rather than invented by it', () => {
  // `createChatPanel` had no context and no URI, and neither did either of its callers; `context`
  // exists only in `activate`. The URI is threaded, not the whole `ExtensionContext`: a function that
  // needs a media folder should say that, and this one needs neither storage nor subscriptions.
  assert.match(read(path.join('src', 'chatPanel.ts')), /extensionUri: vscode\.Uri/);
  assert.match(read(path.join('src', 'chatCommand.ts')), /extensionUri: vscode\.Uri/);
  assert.match(read(path.join('src', 'extension.ts')), /chatWithOtherAi\(chatPanels, context\.extensionUri, args\)/);
});

test('nothing excludes the icons from the package a person installs', () => {
  // The tests run against the source tree; a person runs a `.vsix`. `media/` is not excluded today,
  // and this is what says so the day somebody tidies the ignore file — a shipped extension whose icon
  // files were left out looks exactly like an extension that never set `iconPath`.
  const ignored = read('.vscodeignore').split(/\r?\n/).map((line) => line.trim());

  for (const line of ignored) {
    assert.ok(
      line.length === 0 || line.startsWith('#') || !line.startsWith('media'),
      `.vscodeignore excludes the icons the tab needs: ${line}`,
    );
  }
});
