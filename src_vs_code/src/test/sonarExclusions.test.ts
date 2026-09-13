import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * The one coverage exclusion that is a LIST rather than a rule, kept honest.
 *
 * <p>A module that imports `vscode` cannot be loaded outside an extension host, and this repository
 * has no harness that provides one — `research/module_tests.md` records that as its largest single
 * gap. Node's runner therefore never executes a line of them, and they reach the quality gate as
 * 0 % of whatever they contributed: a number about the analysis rather than about the change. So
 * they are excluded from coverage, by name, because no glob can express "imports `vscode`".</p>
 *
 * <p><b>A list that is maintained by hand is a list that drifts</b>, and both directions of the
 * drift are silent. A new host module left out arrives at the gate as 0 % and fails a pull request
 * for a reason nobody can act on; a module that sheds the import and stays in is production code
 * nobody measures any more. This asserts the list is EXACTLY the set, so neither can happen
 * quietly — and the day an extension-host harness exists, this test is what says the whole entry
 * can come out.</p>
 */

const HERE = path.join(__dirname, '..', '..');
const SOURCE = path.join(HERE, 'src');
const WORKFLOW = path.join(HERE, '..', '.github', 'workflows', 'sonarcloud.yml');

/** Every module in `src` that imports the editor, and therefore cannot run under `node --test`. */
const needsAHost = (): readonly string[] =>
  fs.readdirSync(SOURCE)
    .filter((name) => name.endsWith('.ts'))
    .filter((name) => /from 'vscode'/u.test(fs.readFileSync(path.join(SOURCE, name), 'utf8')))
    .sort();

/** What the scanner is told not to measure, as the workflow spells it. */
const excluded = (): readonly string[] => {
  const workflow = fs.readFileSync(WORKFLOW, 'utf8');
  const line = /sonar\.coverage\.exclusions="([^"]+)"/u.exec(workflow);
  assert.ok(line !== null, 'the workflow no longer passes sonar.coverage.exclusions at all');

  return (line[1] ?? '')
    .split(',')
    .filter((one) => one.startsWith('src_vs_code/src/') && one.endsWith('.ts'))
    .map((one) => one.slice('src_vs_code/src/'.length))
    .sort();
};

test('the modules excluded from coverage are EXACTLY the ones that cannot run without an editor', () => {
  const host = needsAHost();
  const listed = excluded();

  // Named individually rather than compared as sets, so a failure says WHICH module and in which
  // direction — that is the whole value of the test when somebody adds a file a year from now.
  for (const name of host) {
    assert.ok(
      listed.includes(name),
      `“${name}” imports vscode, so no test here can execute it — it must be in sonar.coverage.exclusions, `
      + 'or it arrives at the quality gate as 0 % of the new code it contributed',
    );
  }
  for (const name of listed) {
    assert.ok(
      host.includes(name),
      `“${name}” no longer imports vscode, so it CAN be tested — take it out of sonar.coverage.exclusions `
      + 'rather than leaving production code nobody measures',
    );
  }
  assert.deepEqual(listed, host);
});

test('the exclusion is a small minority of the source, and the decisions are on the measured side', () => {
  // The split is the justification. If the host half ever stopped being a minority it would mean the
  // decisions had moved into it, and the right answer would be to move them back out — into modules
  // that are values and can be tested — rather than to widen this list.
  const all = fs.readdirSync(SOURCE).filter((name) => name.endsWith('.ts'));
  const host = needsAHost();

  assert.ok(
    host.length * 4 < all.length,
    `${host.length} of ${all.length} modules in src now need an editor to run. That is no longer a thin `
    + 'host layer around decisions tested as values, and widening the coverage exclusion would be the '
    + 'wrong way to answer it.',
  );
});
