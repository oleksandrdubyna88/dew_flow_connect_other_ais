import assert from 'node:assert/strict';
import { test } from 'node:test';
import { editorRedirects, type RedirectPorts } from '../editorRedirects';
import { sourceOf } from './sourceReading';

/**
 * E5.1 step 4 of todo/PLAN_one_model_catalog.md: the Review roles, Gate commands and Chat presets tabs are deleted, and
 * the three commands that opened them stay for one more release as REDIRECTS — each opens the Settings page at the
 * place that holds its editor now, so a keybinding or a habit of somebody's still lands somewhere useful (the plan
 * round's finding 0).
 *
 * <p>One row per command, naming ITS OWN place: a table that checked only "some Settings place opened" would pass with
 * the wires crossed. The page that shows is part of each row too: while the preview switch exists, the current page is
 * the default, and it holds none of the three editors.</p>
 */

const ROWS: readonly (readonly [command: string, place: string])[] = [
  ['coai.editRoles', 'reviews/roles'],
  ['coai.editCommands', 'reviews/commands'],
  ['coai.editChatPresets', 'chat'],
];

/** The redirects registered against recording ports, with the preview switch OFF — the default today. */
function registered() {
  const handlers = new Map<string, () => Promise<void>>();
  let preview = false;
  const opened: { place: string; page: string }[] = [];
  const ports: RedirectPorts = {
    useTheNewPage: () => { preview = true; return Promise.resolve(); },
    openSettingsAt: (place) => { opened.push({ place, page: preview ? 'new' : 'current' }); },
  };
  editorRedirects((command, run) => { handlers.set(command, run); return command; }, ports);

  return { handlers, opened };
}

for (const [command, place] of ROWS) {
  test(`${command} opens the NEW Settings page at ${place}, even with the preview switch off`, async () => {
    const { handlers, opened } = registered();
    const run = handlers.get(command);
    assert.ok(run !== undefined, `${command} is not registered as a redirect`);

    await run();

    assert.deepEqual(opened, [{ place, page: 'new' }], `${command} did not open its own place on the new page`);
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
