import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * Add a reviewer lets a person filter on what the entry SAYS, not only on its label.
 *
 * <p>The whole of issue #294 was that typing "Claude Code" matched nothing: the words were not in
 * the label, and `showQuickPick` was given neither `matchOnDetail` nor `matchOnDescription`, so
 * VS Code filtered on the label alone. The label is fixed in `vendors.ts` and tested by calling it.
 * These two flags cannot be: they are arguments to a VS Code API, and this suite has no extension
 * host to drive (`research/module_tests.md` names that as a known gap).</p>
 *
 * <p>So this is a source assertion, deliberately and narrowly. The prohibition on source assertions
 * in `.agents/PROJECT.md` is about webview PAGES — those are programs, and a page test that reads
 * the page's text cannot see a control wired to the wrong branch. There is no program here to run;
 * the thing being asserted IS a literal in a call. The idiom, and the reasoning, are
 * `customEndpointIsOneFlow.test.ts`'s.</p>
 *
 * <p><b>Each assertion pins both halves.</b> Asserting a flag is merely present would survive the
 * defect where it is set on some other quick pick in the file, so the options object is matched as
 * a whole, and the filter it replaced is asserted GONE rather than the new call asserted present.</p>
 */

const PROVIDER = path.join(__dirname, '..', '..', 'src', 'panelProvider.ts');

const source = (): string => fs.readFileSync(PROVIDER, 'utf8');

test('the Add a reviewer pick filters on the detail and the description, not the label alone', () => {
  const options = /\{\s*title: 'Add a reviewer',[\s\S]*?\}/.exec(source())?.[0];

  assert.ok(options, 'the Add a reviewer quick pick no longer passes a title, so this test is asserting nothing');
  assert.match(options, /matchOnDetail: true/, 'typing words from an entry’s hint matches nothing');
  assert.match(
    options,
    /matchOnDescription: true/,
    'the new row’s id is shown in the description, and matchOnDetail does not reach it — typing claude-2 returns an empty list',
  );
});

test('the catalogue is offered whole: the filter that dropped a configured preset is gone', () => {
  const host = source();

  assert.ok(
    !host.includes('VENDOR_PRESETS.filter('),
    'a preset already configured is still being dropped from the list, which is the one-way door',
  );
  // The WHOLE expression, `existing` included. Asserting only that `presetsOffered(VENDOR_PRESETS`
  // appears would stay green if the host passed `new Set()` — and then a configured claude would be
  // offered as `claude` again and refused by saveVendor, which is the defect this line is for.
  // (codex, the code round.)
  assert.ok(
    host.includes('reviewerPickItems(presetsOffered(VENDOR_PRESETS, existing))'),
    'the offering no longer goes through the function that allocates a free id, or is not told what is already taken',
  );
});

test('a picked preset is saved under the id the offering resolved, and the blank one still asks', () => {
  const host = source();

  // Both halves: the row is built from the OFFERED preset AND takes the OFFERED id. Matching only
  // the second would pass on a line that resolved the id and then spread a different preset over it.
  assert.match(
    host,
    /\.\.\.chosen\.preset,\s*id: chosen\.id/,
    'the row is saved under the preset’s own id, so a second one collides with the first',
  );
  assert.ok(
    host.includes("await this.askCustomEndpoint('Add a reviewer')"),
    'the blank preset no longer asks for a name and a URL, and would be written with no id',
  );
});
