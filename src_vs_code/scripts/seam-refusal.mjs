/**
 * The seam's SIXTH leg: a REAL refusal over stdio lands with its secret taken out, asserted on the
 * bytes — story 2.4 of `PLAN_the_server_says_what_it_did.md`, owed since story 1.1.
 *
 * <p>`server-notices.jsonl` is written by C# and read by TypeScript, and BOTH halves redact before
 * anything reaches disk. The parity harness (`test:parity`) proves the two REDACTORS agree, by driving
 * both through `NoticeTool`, a third executable built for the check. What it cannot prove is that the
 * PRODUCT does what they agree on: nothing there starts the shipped server, provokes a refusal down
 * the road a real client takes, and reads what landed with the extension's own reader. A product that
 * serialised past `ServerNoticeLine.Of`, wrote somewhere the extension does not look, or wrote a line
 * the extension's parser rejects would leave the harness green, because the harness never runs
 * `coai-mcp`.</p>
 *
 * <p>So the trigger is a REAL refusal, not a test mode: `open` on a `repoPath` that is not a
 * directory, which `PanelService.OpenAsync` refuses with the path QUOTED — `'<path>' is not a
 * directory on this machine`. That quoting is the whole reason this refusal was chosen: a secret
 * placed in the path genuinely reaches the writer.</p>
 *
 * <p><b>Its own module</b> because `run-seam.mjs` passed the repository's 800-line ceiling when this
 * leg was added (codex, on the code round). What it needs from the runner — the live session, the
 * extension's resolver, the reply reader and the failure road — is handed in rather than imported, so
 * there is still exactly one of each.</p>
 */
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// The EXTENSION's own path, reader, parser and writer — every one of them imported, none re-derived.
const { serverNoticesPath, readServerNotices } = await import('../out/notificationsFile.js');
const { notificationLine, parseNotificationLine } = await import('../out/notifications.js');

/**
 * Run the leg. Answers what it saw when every claim held; ends the process through `fail` when one
 * did not.
 *
 * @param {{serverSession: Function, resolvedFor: Function, answerOf: Function, fail: Function, timeoutMs: number}} runner
 */
export async function refusalSeam({ serverSession, resolvedFor, answerOf, fail, timeoutMs }) {
  const root = mkdtempSync(join(tmpdir(), 'coai-seam-refusal-'));
  const side = 'seam';
  mkdirSync(join(root, side), { recursive: true });
  const sideDir = resolvedFor(root, side);
  const forget = forgetting(root);
  const session = serverSession({ COAI_DATA_DIR: root, COAI_DATA_SIDE: side });
  const secret = SECRET();

  const provoked = await provoke(session, join(root, 'no-such-checkout', secret), secret, answerOf);
  if (provoked !== '') {
    await session.killAndWait();
    forget();
    fail(provoked);
  }

  // A CLEAN end, by EOF — never a kill, which skips `ServeAsync`'s `finally`.
  const verdict = await judged(root, sideDir, await session.close(), secret, timeoutMs);
  forget();
  if (verdict.why !== '') {
    fail(verdict.why);
  }

  return verdict;
}

/**
 * Claims 2-6, in order; the first that fails is the answer, and `why` is `''` when all of them hold.
 *
 * <p>Each claim answers a reason rather than exiting, so the leg above reads as the sequence of what
 * it asserts and the directory is removed on exactly one road.</p>
 */
async function judged(root, sideDir, ended, secret, timeoutMs) {
  if (!ended.exited) {
    return {
      why: `the server did not exit within ${timeoutMs} ms of its stdin closing, and was killed. A `
        + 'client that ends its session this way would leave the notices undrained.',
      title: '',
      logs: 0,
    };
  }
  const sinks = noSinkCarries(root, ended.stderr, secret);
  if (sinks.why !== '') {
    return { ...sinks, title: '' };
  }

  return { ...(await theRecordCrosses(root, sideDir)), logs: sinks.logs };
}

/**
 * The secret: `ghp_` and 36 of [A-Za-z0-9], assembled at run time so no token-shaped literal sits in
 * the source for a scanner to report.
 *
 * <p>It is what the server's vendor-prefix pattern takes out (`Redaction.cs`:
 * `(?<![A-Za-z0-9_])(sk-|ghp_|…)[A-Za-z0-9._-]{8,512}`), and the path separator before it satisfies
 * the lookbehind on both platforms — which the leg OBSERVES rather than assumes: a miss would put the
 * secret on disk and turn claim 3 red.</p>
 */
const SECRET = () => ['ghp', '_', 'S34mLeg2p4Refusal', 'CrossesTheWire', 'Now12'].join('');

/**
 * Claim 1, the teeth: the refusal QUOTES the path it refused, secret and all.
 *
 * <p>The answer to the calling AI is not redacted — it is the AI's own argument, echoed — so finding
 * the secret there is what proves the writer was HANDED it. Without this, every claim after it would
 * pass on a server that wrote nothing sensitive because it was never given anything sensitive.</p>
 *
 * @returns {Promise<string>} why it failed, or `''`
 */
async function provoke(session, repoPath, secret, answerOf) {
  let said;
  try {
    await session.ready;
    said = String(answerOf(await session.call('open', { repoPath, branch: 'main' }))?.error ?? '');
  } catch (e) {
    return `the binary could not be asked to open a missing checkout: ${e.message}`;
  }

  return said.includes('is not a directory on this machine') && said.includes(secret)
    ? ''
    : 'the refusal did not quote the path it refused, so nothing below could tell a redacted secret '
      + `from one that never reached the writer. It answered: ${said.slice(0, 200)}`;
}

/**
 * Remove the leg's directory, and never let that removal be what the run reports.
 *
 * <p>A failing leg must end on ITS sentence. A removal that still fails after the retries is SAID and
 * left behind — not thrown past the reason the leg is failing, which is what the first version did:
 * it removed the directory while the dotnet child still held its files, `rmSync` threw `EPERM` on
 * Windows, and a stack trace was printed where the leg's sentence belonged. Found by one of the
 * leg's own plants.</p>
 */
function forgetting(root) {
  return () => {
    try {
      rmSync(root, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    } catch (e) {
      console.error(`seam: could not remove ${root} (${e.code ?? e.message}); it is left behind`);
    }
  };
}

/**
 * Claim 3: no sink of this run carries the secret — not stderr, and not ONE file under its data root.
 *
 * <p>The notices file is not the only place a refusal's sentence could land, and the operator's
 * standing rule is that no secret reaches a log line at all. (codex, on the plan round.) The companion
 * is what stops "no file carries it" passing on a run that wrote nothing: the run must have left a
 * log file for the check to have covered one.</p>
 */
function noSinkCarries(root, stderr, secret) {
  const shown = (files) => files.map((f) => f.slice(root.length)).join(', ') || '(nothing)';
  if (stderr.includes(secret)) {
    return { why: 'the secret reached the server\'s STDERR in clear, which is where a stdio host sends its console log.', logs: 0 };
  }
  const everything = filesUnder(root);
  const leaking = everything.filter((file) => readFileSync(file).includes(secret));
  if (leaking.length > 0) {
    return { why: `the secret reached the disk in clear, in: ${shown(leaking)}`, logs: 0 };
  }
  const logs = everything.filter((file) => file.endsWith('.log'));

  return logs.length === 0
    ? { why: `the run left no log file under ${root}, so "no log carries the secret" was vacuous. It left: ${shown(everything)}`, logs: 0 }
    : { why: '', logs: logs.length };
}

/**
 * Claims 4-6: the record is where the EXTENSION looks, its own reader returns it, and its own parser
 * gives the line back unchanged.
 *
 * <p>The raw bytes come first, so an empty or missing file cannot pass claim 3 by having nothing in
 * it (gemini, on the plan round). The fixed point is the bar `ServerNoticeLine.cs` sets itself —
 * <i>"a line the extension would have WRITTEN from the same record"</i> — checked for the first time
 * against the shipped binary rather than `NoticeTool`.</p>
 */
async function theRecordCrosses(root, sideDir) {
  const noticesFile = serverNoticesPath(sideDir);
  const failed = (why) => ({ why, title: '' });
  if (!existsSync(noticesFile)) {
    return failed(`the extension looks for the server's notices at ${noticesFile.slice(root.length)} and there is nothing there. `
      + `The run left: ${filesUnder(root).map((f) => f.slice(root.length)).join(', ')}`);
  }
  const bytes = readFileSync(noticesFile, 'utf8');
  const refusedLine = bytes.split('\n').find((line) => line.includes('"code":"refused"'));
  if (refusedLine === undefined || !bytes.endsWith('\n')) {
    return failed(`the notices file has no refused line, or its last line is not terminated: ${bytes.slice(0, 300)}`);
  }
  const record = (await readServerNotices(sideDir)).find((one) => one.code === 'refused' && one.subject === 'OpenAsync');
  if (record === undefined || record.class !== 'refusal' || record.source !== 'coai-mcp'
    || !String(record.title).includes('is not a directory on this machine')) {
    return failed(`the extension's reader did not return the refusal the server wrote. It returned: ${JSON.stringify(record ?? null).slice(0, 400)}`);
  }
  const again = notificationLine(parseNotificationLine(refusedLine));

  return again === `${refusedLine}\n`
    ? { why: '', title: String(record.title) }
    : failed(`the server's line is not a fixed point of the extension's parser.\n  server:    ${refusedLine}\n  extension: ${again.trimEnd()}`);
}

/** Every file under `dir`, recursively. */
function filesUnder(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);

    return statSync(path).isDirectory() ? filesUnder(path) : [path];
  });
}
