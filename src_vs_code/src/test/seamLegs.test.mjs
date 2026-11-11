import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import path, { join } from 'node:path';
import { test } from 'node:test';
import { notificationLine } from '../../out/notifications.js';
import { owned } from '../../scripts/owned.mjs';
import { heldGateSeam } from '../../scripts/seam-held-gate.mjs';
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

test('a malformed line mentioning a refusal cannot throw past the leg\'s cleanup', async (t) => {
  // The leg picked "the first line that mentions a refusal" and handed it to the parser — which
  // answers `undefined` for a line it cannot read, and `notificationLine(undefined)` throws while
  // destructuring. The reader skips a malformed line, so this only bites when a malformed line comes
  // FIRST and a good refusal after it: the reader's own check passes, and the throw escaped
  // `theRecordCrosses` before `forget()` ran, leaving the temporary root behind. (CodeRabbit, #465.)
  const root = owned.ownedDir(t, 'coai-seam-legs', { parent: realpathSync.native(owned.tempRoot()) });
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

test('only a server the run DECLARES older may skip the text leg; a refusal alone, from any binary, still fails', async () => {
  // Measured 2026-10-04: `COAI_MCP_DLL` pointed at mcp 0.42.0 — the mixed-version run the plan's DoD asks for —
  // ended the WHOLE seam at the ninth leg (`exited 64: unknown argument '--security-prompt-text'`), so the legs
  // after it never ran against the older server. The mode is new in 0.43.0; an older binary cannot answer it.
  // Against the binary this repository just built, the same refusal is a regression and must still fail — and so must
  // a NAMED binary that refuses it unannounced (CodeRabbit on #675): a 0.43 build that lost the mode would otherwise
  // skip and go green. The skip needs COAI_SEAM_OLDER_SERVER=1 beside COAI_MCP_DLL: the person running it says so.
  const { textLegVerdict } = await import('../../scripts/seam-security-text.mjs');
  const refused = "[coai-mcp] unknown argument '--security-prompt-text' - this binary takes none; an MCP client speaks to it over stdin.";

  assert.equal(textLegVerdict({ olderExpected: true, code: 64, stderr: refused }), 'older');
  assert.equal(textLegVerdict({ olderExpected: false, code: 64, stderr: refused }), 'fail', 'an unannounced refusal skipped');
  assert.equal(textLegVerdict({ olderExpected: true, code: 1, stderr: 'boom' }), 'fail');
  assert.equal(textLegVerdict({ olderExpected: true, code: 0, stderr: '' }), 'ok');
});

/**
 * The folders the held-gate leg makes in this run's temp folder, claims aside: the wrap's own prefix for each name the leg gives `ownedDir`
 * (`owned.ownedPrefix`) followed by an id of exactly the wrap's six-character alphabet (`ID_ALPHABET` in the reference, letters and digits).
 */
const heldFolders = () => readdirSync(owned.tempRoot()).filter((name) => ['held', 'held-home', 'held-repo']
  .some((what) => name.startsWith(owned.ownedPrefix(what)) && /^[A-Za-z0-9]{6}$/u.test(name.slice(owned.ownedPrefix(what).length))));

test('the scan finds a held-gate folder made the natural way — a scan that matches nothing would pass forever', (t) => {
  for (const what of ['held', 'held-home', 'held-repo']) {
    const made = path.basename(owned.ownedDir(t, what));

    assert.ok(heldFolders().includes(made), `${made} is an owned folder of the leg's kind and the scan did not see it`);
  }
});

test('the held-gate leg removes its folders when something throws after they exist', async () => {
  assert.deepEqual(heldFolders(), [], 'the run folder starts without the leg\'s folders');
  const serverSession = () => { throw new Error('the session could not start'); };

  await assert.rejects(heldGateSeam({ serverSession, answerOf: () => ({}), fakeCli: 'fake-cli', fail: (why) => { throw new Error(`fail: ${why}`); } }), /the session could not start/u);

  assert.deepEqual(heldFolders(), [], 'a throw left the leg\'s folders (and their claims) behind');
});

test('the held-gate leg kills its session and removes its folders when a claim fails', async () => {
  const killed = [];
  const serverSession = () => ({
    ready: Promise.resolve(),
    call: async () => ({}),
    killAndWait: async () => { killed.push(true); },
    close: async () => { killed.push('closed'); },
  });

  await assert.rejects(heldGateSeam({ serverSession, answerOf: () => ({ verdict: 'proceed' }), fakeCli: 'fake-cli', fail: (why) => { throw new Error(`fail: ${why}`); } }), /did not end in call_human/u);

  assert.deepEqual(killed, [true], 'the session was killed once');
  assert.deepEqual(heldFolders(), [], 'a failed claim left the leg\'s folders behind');
});

test('the held-gate leg removes its folders even when killing its session rejects, and reports the claim that failed', async () => {
  const serverSession = () => ({
    ready: Promise.resolve(),
    call: async () => ({}),
    killAndWait: async () => { throw new Error('the kill could not be waited for'); },
    close: async () => {},
  });

  await assert.rejects(heldGateSeam({ serverSession, answerOf: () => ({ verdict: 'proceed' }), fakeCli: 'fake-cli', fail: (why) => { throw new Error(`fail: ${why}`); } }), /fail: .*did not end in call_human/u);

  assert.deepEqual(heldFolders(), [], 'a rejected kill left the leg\'s folders behind');
});
