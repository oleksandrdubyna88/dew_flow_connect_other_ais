import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { notificationLine } from '../../out/notifications.js';
import { theRecordCrosses } from '../../scripts/seam-refusal.mjs';
import { sessionsFor } from '../../scripts/seam-session.mjs';

/**
 * The seam's own machinery, held to what its legs rely on.
 *
 * <p>`npm run test:seam` drives the real `coai-mcp`, so a defect in the HARNESS shows up as a seam
 * run that dies with a stack trace, or leaves its temporary directory behind, rather than as a
 * failing assertion. These are the two CodeRabbit found on #465, each reproduced here against the
 * real function before it was fixed. Neither needs a server: the first spawns a stand-in that dies,
 * the second hands the leg a notices file built by the extension's own writer.</p>
 */

test('a server that dies mid-request rejects the request, rather than ending the runner', async () => {
  // A stand-in that never reads its stdin, so the pipe fills, and then exits with writes still in
  // flight — the shape of a server that crashes mid-request. Without a listener on the child's
  // STDIN, that write error is an unhandled 'error' event and it ends this whole process: every
  // leg's cleanup skipped. Measured before the fix: `Error: write EOF` 3 of 3 (EPIPE off Windows).
  const serverSession = sessionsFor({
    command: process.execPath,
    args: ['-e', 'setTimeout(() => process.exit(0), 150)'],
    dataDir: '',
    timeoutMs: 20_000,
  });
  const session = serverSession();
  const pad = 'y'.repeat(60_000);

  const settled = await Promise.allSettled([
    session.ready,
    ...Array.from({ length: 60 }, () => session.call('x', { pad })),
  ]);

  assert.ok(settled.every((one) => one.status === 'rejected'),
    'a request to a server that went away was answered, which nothing could have answered');
  assert.ok(settled.some((one) => /stdin failed/u.test(String(one.reason?.message))),
    'no request was told the server\'s stdin failed, so the reason a leg would print is lost');
});

test('a malformed line mentioning a refusal cannot throw past the leg\'s cleanup', async () => {
  // The leg picked "the first line that mentions a refusal" and handed it to the parser — which
  // answers `undefined` for a line it cannot read, and `notificationLine(undefined)` throws while
  // destructuring. The reader skips a malformed line, so this only bites when a malformed line comes
  // FIRST and a good refusal after it: the reader's own check passes, and the throw escaped
  // `theRecordCrosses` before `forget()` ran, leaving the temporary root behind. (CodeRabbit, #465.)
  const root = mkdtempSync(join(tmpdir(), 'coai-seam-legs-'));
  try {
    const good = notificationLine({
      utc: '2026-09-22T12:00:00.000Z',
      class: 'refusal',
      source: 'coai-mcp',
      code: 'refused',
      subject: 'OpenAsync',
      title: "'somewhere' is not a directory on this machine",
    });
    writeFileSync(join(root, 'server-notices.jsonl'), `{"code":"refused", this is not JSON\n${good}`, 'utf8');

    const crossed = await theRecordCrosses(root, root);

    assert.equal(crossed.why, '',
      'the leg judged the malformed line instead of the record the extension\'s reader returned');
    assert.match(crossed.title, /is not a directory on this machine/u);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
