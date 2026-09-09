import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHAT_TAB_ICON, chatTabIcon } from '../chatIcon';

/**
 * The chat tab wears this product's own glyph, in green, in both themes.
 *
 * <p>2026-09-09: every chat tab wore the generic `≡`, because `createWebviewPanel` never set
 * `iconPath` — a search of `src_vs_code/src` found not one. A person with three conversations open
 * had three tabs that looked like everything else.</p>
 *
 * <p>The failure mode of an icon is not logic, it is a PATH: nothing type-checks a `Uri`, and a wrong
 * one produces the generic icon and no error anywhere. So the resolution is a pure function —
 * `chatTabIcon` takes a joiner, the panel hands it `vscode.Uri.joinPath` and this file hands it one of
 * its own — and the paths it produces are opened on disk here. The code round refused the first
 * version's regex over `chatPanel.ts`, correctly: a regex could only have proved that somebody wrote
 * a string down.</p>
 */

const EXTENSION_ROOT = path.join(__dirname, '..', '..');
const read = (file: string): string => fs.readFileSync(path.join(EXTENSION_ROOT, file), 'utf8');
/** The joiner a test can watch, standing where `vscode.Uri.joinPath` stands in the panel. */
const asPath = (...segments: readonly string[]): string => segments.join('/');

test('the icon resolves to two files, and both are in the tree', () => {
  const resolved = chatTabIcon(asPath);

  assert.deepEqual(resolved, { light: 'media/chat-light.svg', dark: 'media/chat.svg' });

  for (const file of [resolved.light, resolved.dark]) {
    const full = path.join(EXTENSION_ROOT, file);

    assert.ok(fs.existsSync(full), `${file} is what the panel asks the workbench for, and is not here`);
    assert.match(fs.readFileSync(full, 'utf8'), /^<svg /, `${file} is not an svg`);
  }
});

test('the icon cannot name anything outside the folder this extension shipped', () => {
  for (const segments of [CHAT_TAB_ICON.light, CHAT_TAB_ICON.dark]) {
    for (const segment of segments) {
      assert.ok(segment !== '..' && !/[\\/]/.test(segment),
        `an icon segment that walks the tree resolves somewhere nobody meant: ${segment}`);
    }
  }
});

test('each icon is drawn for the ground it sits on, full bleed', () => {
  // The colours are the decision, so they are the assertion. `#5CC46F` is palette slot 5 — the green
  // `gemini` wears, picked with eleven others to stay apart in a light theme as well as a dark one;
  // `#2E8B3E` is the same hue darkened, derived the way `help-yellow-light.svg` was from
  // `help-yellow.svg`, because a bright colour on white reads pale. Measured against the grounds they
  // actually sit on: 4.30:1 on white and 3.64:1 on a Light+ tab, 7.61:1 on Dark+ and 8.10:1 on Dark
  // Modern — every one of them past the 3:1 that WCAG asks of a graphical object.
  const icons = chatTabIcon(asPath);
  const dark = read(icons.dark);
  const light = read(icons.light);

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

test('the chat tab asks the workbench for that pair', () => {
  // The one thing no test in this suite can observe: `chatPanel.ts` imports `vscode`, which does not
  // exist outside the extension host. Everything else about the icon is a call away, above.
  const source = read(path.join('src', 'chatPanel.ts'));

  assert.match(source, /panel\.iconPath = chatTabIcon\(/,
    'no iconPath is set, so the tab wears the generic glyph');
  assert.match(read(path.join('src', 'extension.ts')),
    /chatWithOtherAi\(chatPanels, context\.extensionUri, args\)/,
    'the extension URI never leaves activate, so the panel has nothing to join');
});

test('a panel restored after a reload must be built the same way', () => {
  // The code round's open tail, held rather than written down. There is no `WebviewPanelSerializer`
  // in this extension today, so no tab loses its icon — but the plan that adds one restores a panel
  // WITHOUT passing through `chatWithOtherAi`, and would hand back tabs wearing the generic glyph
  // again. The day a serializer appears, the file that registers it has to reach `createChatPanel`.
  const root = path.join(EXTENSION_ROOT, 'src');
  const walk = (dir: string): string[] => fs.readdirSync(dir, { withFileTypes: true })
    .flatMap((entry) => {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        return entry.name === 'test' ? [] : walk(full);
      }

      return entry.name.endsWith('.ts') ? [full] : [];
    });

  const files = walk(root);
  // Whoever builds a chat panel — `createChatPanel` itself, and anything that calls it. A serializer
  // may reach the builder through one of those rather than call it directly, which is what
  // `extension.ts` does; what it may not do is make a panel of its own, because the builder is the
  // only place that sets the icon, the message wiring and the disposal.
  const builders = files
    .filter((file) => /createChatPanel\(/.test(fs.readFileSync(file, 'utf8')))
    .map((file) => path.basename(file, '.ts'));

  assert.ok(builders.length > 0, 'nothing builds a chat panel any more, so this scan guards nothing');

  for (const file of files) {
    const source = fs.readFileSync(file, 'utf8');
    if (!source.includes('registerWebviewPanelSerializer')) {
      continue;
    }

    assert.ok(
      builders.some((builder) => source.includes(`'./${builder}'`)),
      `${path.basename(file)} restores a chat panel without going through the one place that builds one`,
    );
    assert.doesNotMatch(
      source, /createWebviewPanel\(/,
      `${path.basename(file)} makes a chat panel of its own, so a restored tab would wear no icon`,
    );
  }
});

test('nothing excludes the icons from the package a person installs', () => {
  // The tests run against the source tree; a person runs a `.vsix`. Checking that no line STARTS with
  // `media` was the first version and the code round was right about it: `**/*.svg` or `**/media/**`
  // would exclude the icons and leave this green. Each pattern is turned into the regex the packager's
  // glob means by it and run against the paths the panel actually asks for.
  const patterns = read('.vscodeignore').split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith('#') && !line.startsWith('!'));
  // One pass, character by character. A chain of `.replace` calls cannot do this: the `.*` that `**/`
  // expands to contains a `*`, which the later `*` rule then rewrites into `[^/]*` — and the first
  // version of this test passed happily while `**/*.svg` sat in the ignore file.
  const asRegex = (pattern: string): RegExp => {
    let out = '';
    for (let i = 0; i < pattern.length; i += 1) {
      const here = pattern[i];
      if (here === '*' && pattern[i + 1] === '*' && pattern[i + 2] === '/') {
        out += '(?:.*/)?';
        i += 2;
      } else if (here === '*' && pattern[i + 1] === '*') {
        out += '.*';
        i += 1;
      } else if (here === '*') {
        out += '[^/]*';
      } else if (here === '?') {
        out += '[^/]';
      } else {
        // Global, although `here` is one character: a sanitiser that escapes only the first match is
        // a sanitiser that is wrong the day its input grows, and CodeQL is right to refuse to reason
        // about which of those two this is.
        out += here.replace(/[.+^${}()|[\]\\]/g, '\\$&');
      }
    }

    return new RegExp(`^${out}(?:/.*)?$`);
  };
  const icons = chatTabIcon(asPath);

  for (const pattern of patterns) {
    for (const icon of [icons.light, icons.dark]) {
      assert.ok(!asRegex(pattern).test(icon),
        `.vscodeignore keeps ${icon} out of the package, so an installed tab wears the generic glyph: ${pattern}`);
    }
  }
});
