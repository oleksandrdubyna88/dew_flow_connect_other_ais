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
const CI = path.join(HERE, '..', '.github', 'workflows', 'ci.yml');

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

/**
 * Every .NET test project is COLLECTED, and this has now been learned twice.
 *
 * <p>The scanner measures what it is handed. A test project left out of the collect list does not
 * report low coverage — the projects it covers report NONE, and a pull request fails an 80 %
 * new-code gate for a reason that is about the analysis rather than about the change.</p>
 *
 * <p><b>It happened to `coai-server`</b> — 37 passing tests, `new_coverage` 0.0 — and the workflow
 * gained a comment saying every project must be listed. <b>Then it happened to `coai-bugs`</b>: a
 * whole new binary, 29 passing tests, and a gate reading 9.0 %. A comment is not a check, which is
 * the entire difference between that note and this test.</p>
 *
 * <p><b>And the same omission was worse in `ci.yml`.</b> Looking for the coverage gap turned up a
 * bigger one beside it: the job that gates every pull request ran two of the four .NET suites, so
 * `coai-server`'s 320 tests and `coai-bugs`' 29 were never executed by the check that is allowed to
 * say no. A suite nobody runs is not a suite. Both workflows are asserted here, because the reason
 * is the same in both and so is the silence.</p>
 */

/** Every runner this repository builds, by the name its executable is given. */
const dotnetTestProjects = (): readonly string[] => {
  const root = path.join(HERE, '..');
  const found: string[] = [];
  const walk = (dir: string, depth: number): void => {
    if (depth > 4) {
      return;
    }
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      // `bin` and `obj` hold COPIES of a project file's output and would double every name.
      if (entry.isDirectory() && !['bin', 'obj', 'node_modules', '.git'].includes(entry.name)) {
        walk(path.join(dir, entry.name), depth + 1);
      } else if (entry.isFile() && entry.name.endsWith('.Tests.csproj')) {
        found.push(entry.name.replace(/\.csproj$/u, ''));
      }
    }
  };
  walk(root, 0);

  return [...new Set(found)].sort();
};

/** What the coverage job actually runs under `dotnet-coverage collect`. */
const collected = (): readonly string[] => {
  const workflow = fs.readFileSync(WORKFLOW, 'utf8');
  const names = [...workflow.matchAll(/dotnet-coverage collect[^\n]*?\/([A-Za-z.]+)$/gmu)]
    .map((match) => match[1] ?? '');

  return [...new Set(names)].sort();
};

/** What the pull-request gate actually executes. */
const executedByCi = (): readonly string[] => {
  const workflow = fs.readFileSync(CI, 'utf8');
  const names = [...workflow.matchAll(/run:\s*\.\/\S*?\/([A-Za-z.]+Tests)\s*$/gmu)]
    .map((match) => match[1] ?? '');

  return [...new Set(names)].sort();
};

test('every .NET test project is collected for coverage, or the code it covers reads as zero', () => {
  const projects = dotnetTestProjects();
  const runs = collected();

  assert.ok(projects.length > 0, 'no .Tests.csproj was found at all — the walk is wrong, not the repo');
  for (const project of projects) {
    assert.ok(
      runs.includes(project),
      `${project} is not run by the coverage step in sonarcloud.yml, so everything it covers arrives `
      + 'at the quality gate as 0 % of new code. That has failed a pull request twice — coai-server '
      + 'and coai-bugs — for a reason about the analysis rather than about the change.',
    );
  }
});

test('every .NET test project is EXECUTED by the pull-request gate', () => {
  // The louder half of the same omission. Coverage that reads zero is a number nobody can act on;
  // a suite the gate never runs is a regression nobody hears about at all, until a release or
  // until somebody runs it by hand. `ci.yml` ran two of four.
  const projects = dotnetTestProjects();
  const runs = executedByCi();

  for (const project of projects) {
    assert.ok(
      runs.includes(project),
      `${project} is built by ci.yml and never run by it. Its tests cannot fail a pull request, `
      + 'which means they cannot stop anything — and a suite nobody runs is not a suite.',
    );
  }
});
