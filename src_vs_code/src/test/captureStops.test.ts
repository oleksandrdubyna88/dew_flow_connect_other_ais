import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { setTimeout as after } from 'node:timers/promises';
import { capture } from '../versionProbe';

/**
 * Giving up on a child that is still working.
 *
 * <p>The cap has always been a ceiling on a child that has STOPPED answering. This is the other
 * question: the child is fine, and the person no longer wants the answer. It arrived with the batch
 * findings read, where one process answers for a whole selection — before it, cancelling a bulk
 * export could only take effect between batches of four, and with one process it could not take
 * effect at all until that process returned. Three reviewers of that story said so.</p>
 *
 * <p>Against a REAL child, like `processLauncher.test.ts` and for the same reason: what is being
 * asserted is that the process actually goes away, and a fake would assert that we called `kill`.</p>
 */

const NODE = process.execPath;

/** A child that will outlive any cap this test would be willing to wait for. */
const SLEEPS = ['-e', 'setInterval(() => {}, 1000);'];

test('a read that is given up on stops at once, rather than at its deadline', async () => {
  let cancelled = false;
  setTimeout(() => {
    cancelled = true;
  }, 150);

  const began = Date.now();
  // A cap far beyond what this test will tolerate: if the stop is not honoured, the only way this
  // resolves is the deadline, and the elapsed time says which one answered.
  const { code, output } = await capture(NODE, SLEEPS, false, 60_000, () => cancelled);
  const took = Date.now() - began;

  assert.equal(code, -1, 'a read nobody waited for is not a successful read');
  assert.equal(output, '', 'and it carries nothing, because it was never finished');
  assert.ok(took < 10_000, `it should stop when asked, not at the cap — took ${took} ms`);
});

test('the child is KILLED, not merely stopped being waited for', async () => {
  // The point of the whole thing: a read that only stopped LISTENING would leave a process reading
  // the database for as long as it liked, and the person who cancelled would have changed nothing
  // but what they can see. So the child says it is alive by writing to a file, and the test watches
  // that stop.
  const folder = await mkdtemp(join(tmpdir(), 'coai-capture-test-'));
  const alive = join(folder, 'alive');
  try {
    // Cancel only once the child has PROVED it is running, by writing its first line. A fixed
    // 150 ms could expire before a slow worker had started node at all, and the read would then be
    // cancelled before there was anything to kill — the assertion below would fail on a missing file
    // rather than on a child that outlived its cancellation, which tests nothing.
    // (CodeRabbit, on the pull request.)
    const started = (): boolean => existsSync(alive);

    await capture(
      NODE,
      ['-e', 'const fs = require("node:fs"); const write = () => fs.writeFileSync(process.argv[1], String(Date.now())); write(); setInterval(write, 50);', alive],
      false, 60_000, started);

    // Generous margins: the assertion is "it stopped", and a slow machine must not turn that into
    // "it stopped within exactly one tick".
    await after(400);
    const first = await readFile(alive, 'utf8');
    await after(600);

    assert.equal(await readFile(alive, 'utf8'), first,
      'the child went on writing after the read was given up on — it was never killed');
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});

test('a child that is never given up on runs to its own end, and is answered normally', async () => {
  const { code, output } = await capture(
    NODE, ['-e', 'process.stdout.write("done")'], false, 30_000, () => false);

  assert.equal(code, 0);
  assert.equal(output, 'done', 'asking the question is not the same as answering it yes');
});

test('a read with nothing watching it behaves exactly as it always has', async () => {
  const { code, output } = await capture(NODE, ['-e', 'process.stdout.write("plain")'], false, 30_000);

  assert.equal(code, 0);
  assert.equal(output, 'plain');
});
