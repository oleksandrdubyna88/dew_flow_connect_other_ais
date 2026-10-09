import assert from 'node:assert/strict';
import { test } from 'node:test';
import { editorRedirects } from '../editorRedirects';
import { sourceOf } from './sourceReading';

/**
 * E5.1 step 4 of research/PLAN_one_model_catalog.md: the Review roles, Gate commands and Chat presets tabs are deleted, and
 * the three commands that opened them stay for one more release as REDIRECTS — each opens the Settings page at the
 * place that holds its editor now, so a keybinding or a habit of somebody's still lands somewhere useful (the plan
 * round's finding 0).
 *
 * <p>One row per command, naming ITS OWN place: a table that checked only "some Settings place opened" would pass with
 * the wires crossed. (Until step 5 removed the preview switch, each redirect also made the slot paint the new page.)</p>
 */

const ROWS: readonly (readonly [command: string, place: string])[] = [
  ['coai.editRoles', 'reviews/roles'],
  ['coai.editCommands', 'reviews/commands'],
  ['coai.editChatPresets', 'chat'],
];

/** The redirects registered against a recording opener. */
function registered() {
  const handlers = new Map<string, () => void>();
  const opened: string[] = [];
  editorRedirects((command, run) => { handlers.set(command, run); return command; }, (place) => { opened.push(place); });

  return { handlers, opened };
}

for (const [command, place] of ROWS) {
  test(`${command} opens the Settings page at ${place}`, () => {
    const { handlers, opened } = registered();
    const run = handlers.get(command);
    assert.ok(run !== undefined, `${command} is not registered as a redirect`);

    run();

    assert.deepEqual(opened, [place], `${command} did not open its own place`);
  });
}

test('exactly the three commands are redirected — none added, none dropped', () => {
  assert.deepEqual([...registered().handlers.keys()].sort(), ROWS.map(([command]) => command).sort());
});

test('the extension registers the three commands through the redirects, never through a page of their own', () => {
  const extension = sourceOf('extension.ts');

  // `ok` over `match`: a failure names the wiring, not the whole of `extension.ts`.
  assert.ok(extension.includes('editorRedirects('), 'the extension does not register the redirects');
  for (const [command] of ROWS) {
    assert.ok(!extension.includes(`registerCommand('${command}'`), `${command} is still registered to open a page of its own`);
  }
});
