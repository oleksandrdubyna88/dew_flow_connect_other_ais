import assert from 'node:assert/strict';
import { test } from 'node:test';

import { NOTHING_CHOSEN, useStorageSettings } from '../dataDir';
import { currentFindings, forgetFindingsBoard } from '../findingsBoard';
import { type ExecutableSide, type FilePresence } from '../pathFamily';
import { type PathFinding, findingSubject } from '../pathFindings';
import { PathProbes } from '../pathProbes';
import { PathWatch } from '../pathWatch';
import type { SettingProblem } from '../providers';

/**
 * The watch over this side's paths (todo/PLAN_paths_per_side.md E5.3), run without an editor: what the settings name is
 * asked of a disk the test answers for, published for the page, and SAID — once per missing path, when it appears, with
 * its place; resolved when it comes back, so going missing again is news. The operator's case first.
 */

interface World {
  readonly roots?: readonly string[];
  readonly vendors?: readonly { readonly id: string; readonly executablePath: string }[];
  readonly watched?: readonly string[];
  readonly dataRoot?: string;
  /** What the disk says per path; a path with no entry is absent. */
  readonly disk: Readonly<Record<string, FilePresence>>;
  readonly family?: 'windows' | 'posix';
}

interface Run {
  readonly watch: PathWatch;
  readonly said: PathFinding[];
  readonly resolved: PathFinding[];
  readonly disk: Record<string, FilePresence>;
  readonly settle: () => Promise<void>;
}

const tick = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

function run(world: World): Run {
  forgetFindingsBoard();
  const disk: Record<string, FilePresence> = { ...world.disk };
  const family = world.family ?? 'windows';
  const side: ExecutableSide = { family, systemDrive: 'C:', fileAt: (path) => disk[path] ?? 'absent' };
  const probes = new PathProbes(async (path) => disk[path] ?? 'absent');
  const said: PathFinding[] = [];
  const resolved: PathFinding[] = [];
  const watch = new PathWatch({
    reads: {
      roots: () => world.roots ?? [],
      vendors: () => world.vendors ?? [],
      watched: () => world.watched ?? [],
      ownDataDir: () => (family === 'windows' ? 'C:\\Users\\me\\AppData\\Local\\coai-mcp' : '/home/me/.local/share/coai-mcp'),
      dataRoot: () => (world.dataRoot === undefined ? undefined : { root: world.dataRoot, source: 'this side' }),
    },
    probes,
    side,
    platform: family === 'windows' ? 'win32' : 'linux',
    say: (finding) => { said.push(finding); },
    resolved: (finding) => { resolved.push(finding); },
  });

  // The probes answer on the next turn of the loop; a settle is "every answer has landed and been told".
  return { watch, said, resolved, disk, settle: async () => { await tick(); await tick(); } };
}

test('the operator\'s case: a deleted root of this OS is said ONCE as setting-path-missing with its place — and not again on the next refresh', async () => {
  const it = run({ roots: ['D:\\old-project', 'D:\\rsd\\work'], disk: { 'D:\\rsd\\work': 'here' } });
  it.watch.start();
  await it.settle();

  assert.deepEqual(it.said.map((one) => [findingSubject(one), one.place, one.state]), [['coai.qconsultRoots|D:\\old-project', 'consultants/qconsult', 'missing']],
    'today the operator\'s case produces the generic "server did not understand a setting" toast and no notice of its own');
  assert.deepEqual(currentFindings().map((one) => one.value), ['D:\\old-project'], 'the page reads the same finding');

  it.watch.refresh();
  it.watch.refresh();
  await it.settle();
  assert.equal(it.said.length, 1, 'a refresh with nothing new re-said the finding — the funnel would record a row for every repaint');
  assert.equal(it.resolved.length, 0);
});

test('a path that comes back is RESOLVED, so its going missing again is news — said once more', async () => {
  const it = run({ roots: ['D:\\gone'], disk: {} });
  it.watch.start();
  await it.settle();
  assert.equal(it.said.length, 1);

  it.disk['D:\\gone'] = 'here';
  it.watch.forget();
  await it.settle();
  assert.deepEqual(it.resolved.map(findingSubject), ['coai.qconsultRoots|D:\\gone'], 'the folder came back and nothing resolved the notice');
  assert.deepEqual(currentFindings(), []);

  delete it.disk['D:\\gone'];
  it.watch.forget();
  await it.settle();
  assert.equal(it.said.length, 2, 'the same folder missing again is a new occurrence, not a repeat the funnel swallows');
});

test('a path the disk could not answer for is published as UNKNOWN and never said', async () => {
  const it = run({ roots: ['\\\\nas\\share\\git'], disk: { '\\\\nas\\share\\git': 'unknown' } });
  it.watch.start();
  await it.settle();

  assert.deepEqual(currentFindings().map((one) => one.state), ['unknown']);
  assert.equal(it.said.length, 0, 'an unknown path raised the notice');
});

test('this side\'s missing CLI is said with its owner; the other side\'s CLI and a bare name are never findings', async () => {
  const it = run({
    vendors: [
      { id: 'my-codex', executablePath: 'D:\\tools\\codex.exe' },
      { id: 'wsl-codex', executablePath: '/usr/local/bin/codex' },
      { id: 'plain', executablePath: 'codex' },
      { id: 'node-cli', executablePath: '/Program Files/nodejs/node.exe' },
    ],
    disk: { 'C:\\Program Files\\nodejs\\node.exe': 'here' },
  });
  it.watch.start();
  await it.settle();

  assert.deepEqual(it.said.map((one) => [one.owner, one.value, one.place]), [['my-codex', 'D:\\tools\\codex.exe', 'models']]);
});

test('a watched folder of this side that is missing is a finding under Setup › MCP server; the other side\'s is not', async () => {
  const it = run({ watched: ['D:\\elsewhere\\coai-mcp', '/home/user/.local/share/coai-mcp'], disk: {} });
  it.watch.start();
  await it.settle();

  assert.deepEqual(it.said.map((one) => [one.key, one.value, one.place]), [['coai.alsoWatchDataDirectories', 'D:\\elsewhere\\coai-mcp', 'setup/mcp']]);
});

test('the NAMED data root that is missing is a finding and is said; one that is there, or the default location, is not', async () => {
  const gone = run({ dataRoot: 'Z:\\coai', disk: {} });
  gone.watch.start();
  await gone.settle();
  assert.deepEqual(gone.said.map((one) => [one.key, one.value]), [['coai.dataDirectory', 'Z:\\coai']]);

  const there = run({ dataRoot: 'Z:\\coai', disk: { 'Z:\\coai': 'here' } });
  there.watch.start();
  await there.settle();
  assert.equal(there.said.length, 0);

  const none = run({ disk: {} });
  none.watch.start();
  await none.settle();
  assert.deepEqual(currentFindings(), [], 'the default location is never a finding');
  useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
});

test('once the server has answered, its problems replace this window\'s own for the roots — and only for the roots', async () => {
  // The disk here says the root is there; the SERVER — which judges the roots — says it is missing. The server wins for
  // the roots (the page and the server cannot disagree), while a CLI finding of this window's own stays.
  const it = run({ roots: ['D:\\server-says-gone', 'D:\\server-says-fine'], vendors: [{ id: 'cli', executablePath: 'D:\\gone.exe' }], disk: { 'D:\\server-says-gone': 'here' } });
  it.watch.start();
  await it.settle();
  assert.deepEqual(currentFindings().map((one) => one.value).sort(), ['D:\\gone.exe', 'D:\\server-says-fine'], 'before the server answers the window\'s own probe stands');

  const problems: SettingProblem[] = [{ key: 'COAI_QCONSULT_ROOTS', kind: 'missing-folder', value: 'D:\\server-says-gone', sentence: 'gone' }];
  it.watch.serverSaid(problems);
  await it.settle();
  assert.deepEqual(currentFindings().map((one) => one.value).sort(), ['D:\\gone.exe', 'D:\\server-says-gone'], 'the server\'s answer did not replace the window\'s own for the roots');
  assert.deepEqual(it.said.map((one) => one.value).sort(), ['D:\\gone.exe', 'D:\\server-says-fine', 'D:\\server-says-gone']);
  assert.deepEqual(it.resolved.map((one) => one.value), ['D:\\server-says-fine'], 'the root the server accepts was not resolved');

  it.watch.serverSaid(undefined);
  await it.settle();
  assert.deepEqual(currentFindings().map((one) => one.value).sort(), ['D:\\gone.exe', 'D:\\server-says-fine'], 'an older server leaves the window\'s own findings');
});

test('a settings change while a path is still missing re-asks the disk without resolving it or saying it again', async () => {
  // What a real editor showed on E5's first host run: sixteen ledger rows for ONE CLI path. Every change to a `coai`
  // setting drops the disk's answers (`forget`) and asks again; the gap between the drop and the answer read as "no
  // longer missing", the notice was resolved, and the answer landing said it again — a toast per keystroke in the
  // settings. Only a SETTLED answer may resolve a said path: the disk saying it is there, or the value leaving the list.
  const it = run({ roots: ['D:\\gone'], vendors: [{ id: 'cli', executablePath: 'D:\\gone.exe' }], disk: {} });
  it.watch.start();
  await it.settle();
  assert.equal(it.said.length, 2);

  for (let change = 0; change < 3; change += 1) {
    it.watch.forget();
    await it.settle();
  }
  assert.deepEqual([it.said.length, it.resolved.length], [2, 0],
    `three settings changes re-said the still-missing paths: said ${it.said.length} times, resolved ${it.resolved.length} — a settings change must re-ask the disk without resolving a path that is still missing`);
  assert.deepEqual(currentFindings().map((one) => one.value).sort(), ['D:\\gone', 'D:\\gone.exe'], 'the page lost the marks');
});

test('a missing path whose next answer is UNKNOWN stays said — neither resolved nor said again until the disk answers', async () => {
  const it = run({ roots: ['D:\\flaky'], disk: {} });
  it.watch.start();
  await it.settle();
  assert.equal(it.said.length, 1);

  it.disk['D:\\flaky'] = 'unknown';
  it.watch.forget();
  await it.settle();
  assert.deepEqual([it.said.length, it.resolved.length], [1, 0], 'an answer the disk could not give resolved or re-said the notice');
  assert.deepEqual(currentFindings().map((one) => one.state), ['unknown'], 'the page shows the unknown tone meanwhile');

  delete it.disk['D:\\flaky'];
  it.watch.forget();
  await it.settle();
  assert.equal(it.said.length, 1, 'the path still missing after an unknown answer was said again');

  it.disk['D:\\flaky'] = 'here';
  it.watch.forget();
  await it.settle();
  assert.deepEqual(it.resolved.map(findingSubject), ['coai.qconsultRoots|D:\\flaky'], 'the folder came back and nothing resolved the notice');
});
