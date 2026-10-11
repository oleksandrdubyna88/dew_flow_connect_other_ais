import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { nodeTestArgs } from '../../scripts/run-tests.mjs';

/**
 * Local test runs here take at most 8 workers and run at lowered CPU priority — the family rule
 * `common.local-cpu-budget` (.agents/conventions/common/local-cpu-budget.md).
 *
 * <p>Measured 2026-10-10 on the owner's 24-thread workstation: a plain `node --test` started 23
 * test-file processes and held the CPU at 100 % while several agent sessions and the editor lagged.
 * Every runner's default is "one worker per CPU", so the cap and the priority have to be written
 * down where the runner is started — and this file is what notices when somebody stops writing
 * them.</p>
 */

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const scripts = path.join(root, 'src_vs_code', 'scripts');
const lowered = process.platform === 'win32' ? os.constants.priority.PRIORITY_BELOW_NORMAL : 19;
const read = (file) => fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');

test('every node --test this repository starts carries a worker cap of 8 or fewer', () => {
  const args = nodeTestArgs(['a.test.js', 'b.test.js'], ['--test-timeout=60000']);
  assert.equal(args[0], '--test');
  const cap = args.find((arg) => arg.startsWith('--test-concurrency='));
  assert.ok(cap, `no --test-concurrency in ${JSON.stringify(args)}`);
  const n = Number(cap.split('=')[1]);
  assert.ok(Number.isInteger(n) && n >= 1 && n <= 8, `the cap is ${n}`);
  // A cap, never a raise: below nine threads the value is node's own default (CPUs - 1).
  assert.equal(n, Math.max(1, Math.min(8, os.availableParallelism() - 1)));
  assert.deepEqual(args.slice(-3), ['--test-timeout=60000', 'a.test.js', 'b.test.js']);
});

test('both runner scripts build their node --test command through nodeTestArgs and start lowered', () => {
  // The runners are scripts that start servers and suites when loaded, so they cannot be imported
  // here; what can be pinned is that neither assembles its own `--test` arguments any more.
  // run-tests.mjs holds the ONE list — inside nodeTestArgs — and run-contract.mjs holds none.
  for (const [name, allowed] of [['run-tests.mjs', 1], ['run-contract.mjs', 0]]) {
    const source = read(path.join(scripts, name));
    assert.equal((source.match(/\[\s*'--test'\s*,/g) ?? []).length, allowed, `${name} builds a node --test argument list of its own`);
    assert.match(source, /import\(\s*'\.\/lower-priority\.mjs'\s*\)|import\s+'\.\/lower-priority\.mjs'/,
      `${name} never loads the priority preload`);
  }
  assert.match(read(path.join(scripts, 'run-contract.mjs')), /nodeTestArgs\(/);
});

test('the preload is the family template, verbatim but for its MOUNT line', () => {
  const template = read(path.join(root, '.agents/conventions/tools/templates/lower-priority-preload.mjs'));
  const copy = read(path.join(scripts, 'lower-priority.mjs'));
  const mountLine = /^const MOUNT = new URL\("([^"]+)", import\.meta\.url\);$/m;
  assert.match(copy, mountLine);
  assert.equal(copy.replace(mountLine, 'MOUNT'), template.replace(mountLine, 'MOUNT'),
    'scripts/lower-priority.mjs drifted from .agents/conventions/tools/templates/lower-priority-preload.mjs');
  const mount = new URL(copy.match(mountLine)[1], new URL('file:' + path.join(scripts, 'lower-priority.mjs').replace(/\\/g, '/')));
  assert.equal(path.resolve(fileURLToPath(mount)), path.resolve(root, '.agents/conventions'));
});

test('the preload really lowers the runner and every process it starts', () => {
  const env = { ...process.env };
  delete env.CI; // the tool exempts CI; this case is about what a developer's run does
  delete env.LOW_PRIORITY_PRELOAD_NOTICE;
  const child = "require('node:child_process').spawnSync(process.execPath,['-e',\"console.log(require('node:os').getPriority(0))\"],{stdio:'inherit'})";
  const result = spawnSync(process.execPath,
    ['--import', './scripts/lower-priority.mjs', '-e', `console.log(require('node:os').getPriority(0));${child}`],
    { cwd: path.join(root, 'src_vs_code'), env, encoding: 'utf8', timeout: 30_000 });
  assert.equal(result.status, 0, result.stderr);
  const [self, grandchild] = result.stdout.trim().split(/\r?\n/).map(Number);
  const expected = Math.max(os.getPriority(0), lowered);
  assert.equal(self, expected, result.stdout);
  assert.equal(grandchild, expected, result.stdout);
});

/** Every xUnit test project here, found by walking rather than by a list somebody must keep. */
function testProjects() {
  const found = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (['node_modules', 'bin', 'obj', '.git', 'artifacts', '.agents'].includes(entry.name)) continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.Tests.csproj')) found.push(full);
    }
  };
  walk(root);
  return found;
}

test('every xUnit test project caps its threads at 8 or fewer, and the cap reaches the runner', () => {
  const projects = testProjects();
  assert.ok(projects.length >= 4, `found only ${projects.join(', ')}`);
  for (const project of projects) {
    // Measured 2026-10-11, xunit.v3 4.0.1: an xunit.runner.json that is not copied to the output is
    // never read (24 threads); with the Content item it is (8). The item is half of the cap.
    // Copied for local runs only: CI is exempt, and an exact 8 would RAISE a 4-vCPU runner's threads.
    assert.match(read(project), /<Content Include="xunit\.runner\.json" CopyToOutputDirectory="PreserveNewest" Condition="'\$\(CI\)' != 'true'" \/>/,
      `${path.relative(root, project)} does not copy xunit.runner.json to its output`);
    const config = JSON.parse(read(path.join(path.dirname(project), 'xunit.runner.json')));
    assert.ok(Number.isInteger(config.maxParallelThreads) && config.maxParallelThreads >= 1 && config.maxParallelThreads <= 8,
      `${path.relative(root, project)}: maxParallelThreads is ${config.maxParallelThreads}`);
  }
});

test('the documented way to run a test executable runs it lowered', () => {
  const lines = read(path.join(root, '.agents/PROJECT.md')).split('\n').filter((line) => /\.Tests\.exe/.test(line));
  assert.ok(lines.length > 0, 'PROJECT.md no longer documents how to run a test executable');
  for (const line of lines) {
    assert.match(line, /low-priority\.mjs/, `PROJECT.md runs a test executable at normal priority: ${line.trim()}`);
  }
});
