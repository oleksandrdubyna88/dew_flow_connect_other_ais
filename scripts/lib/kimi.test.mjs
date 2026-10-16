// node --test scripts/lib/*.test.mjs — the Kimi probe's scratch folder must be private before the person's sign-in is
// copied into it (epic 2's code round, codex, security: a caller-supplied --scratch could be a folder another local user
// can read, and the copied refresh token would sit there until the scrub). Runs nothing of Kimi's; makes folders and links
// only under this user's own temp folder, and removes them. CI runs it on Linux (ci.yml, "Probe script tests"), where the
// two mode-bit cases run; on Windows they skip, because Windows has no mode bits and the profile's ACL is what it relies on.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, rmdirSync, statSync, symlinkSync, unlinkSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join, parse } from 'node:path';
import { after, test } from 'node:test';
import { CredentialGuard, acpTurn, copyHome, parseStreamJson, scratchRefusal, withoutTrailingSeparators } from './kimi.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'kimi-scratch-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

/** A junction on Windows (no privilege needed); a plain symlink on POSIX, where the type argument is ignored. */
const makeLink = (target, at) => symlinkSync(target, at, 'junction');

/** Removes the link and never its target: rmdir unlinks a junction on Windows; on POSIX rmdir refuses a symlink (ENOTDIR). */
const removeLink = (at) => (WINDOWS ? rmdirSync(at) : unlinkSync(at));

test('a fresh folder under this user\'s own temp folder is admitted', () => {
  assert.equal(scratchRefusal(join(base, 'kcp-new')), '');
  mkdirSync(join(base, 'kcp-made'), { mode: 0o700 });
  assert.equal(scratchRefusal(join(base, 'kcp-made')), '');
});

test('a folder outside the profile is refused, naming it', () => {
  const outside = join(parse(homedir()).root, 'coai-kimi-scratch-not-private');
  const why = scratchRefusal(outside);
  assert.match(why, /refusing --scratch/u);
  assert.ok(why.includes(outside), why);
});

test('a folder reached through a link is refused — even one whose target is private', () => {
  const target = join(base, 'target');
  mkdirSync(target);
  const link = join(base, 'link');
  makeLink(target, link);
  try {
    assert.match(scratchRefusal(link), /link/u);
    assert.match(scratchRefusal(join(link, 'kcp-under')), /link/u, 'a link anywhere on the way');
  } finally {
    removeLink(link);
  }
});

test('a link pointing OUT of the profile is refused', () => {
  const link = join(base, 'out');
  makeLink(parse(homedir()).root, link);
  try {
    assert.match(scratchRefusal(join(link, 'kcp')), /link/u);
  } finally {
    removeLink(link);
  }
});

test('a dangling link is refused, never treated as an absent folder', () => {
  const target = join(base, 'gone');
  mkdirSync(target);
  const link = join(base, 'dangling');
  makeLink(target, link);
  rmdirSync(target);
  try {
    assert.notEqual(scratchRefusal(link), '');
  } finally {
    removeLink(link);
  }
});

test('a copied home is created owner-only where the platform has modes', { skip: WINDOWS && 'Windows has no mode bits: the profile ACL is what keeps it private' }, () => {
  const real = join(base, 'real-home');
  mkdirSync(real);
  const home = join(base, 'copied-home');
  copyHome(real, home, { credentials: false, config: false });
  assert.equal(statSync(home).mode & 0o077, 0);
});

test('an existing folder other users can read is refused where the platform has modes', { skip: WINDOWS && 'Windows has no mode bits' }, () => {
  const open = join(base, 'open');
  mkdirSync(open, { mode: 0o755 });
  assert.match(scratchRefusal(open), /other users/u);
});

// SonarCloud S8786 on #803: an end-anchored separator run backtracked quadratically; the loop is linear and says the same.
test('trailing separators go, in linear time on a pathological run', () => {
  assert.equal(withoutTrailingSeparators('C:/a/b//'), 'C:/a/b');
  assert.equal(withoutTrailingSeparators('C:\\a\\b\\\\'), 'C:\\a\\b');
  assert.equal(withoutTrailingSeparators('/'), '');
  assert.equal(withoutTrailingSeparators('a'), 'a');
  const evil = `${'/'.repeat(200_000)}x`;
  const started = performance.now();
  assert.equal(withoutTrailingSeparators(evil), evil);
  assert.ok(performance.now() - started < 200, 'a run of separators that does not end the path is one pass');
});

/** A home whose credentials/<name>.json carries this refresh token. */
function homeWith(name, refresh, access = 'a') {
  const home = mkdtempSync(join(base, `${name}-`));
  mkdirSync(join(home, 'credentials'));
  writeFileSync(join(home, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: refresh, access_token: access, expires_at: 1 }));
  return home;
}
const refreshIn = (home) => JSON.parse(readFileSync(join(home, 'credentials', 'kimi.json'), 'utf8')).refresh_token;

// SonarCloud S3403 on #803 said `rotates !== false` is always true. It is not: `rotates` is null until the real home's
// first refresh, then whatever that refresh showed. All three states, and what each does to a copy that refreshed.
test('the credential guard: a copy that refreshed is written back unless the real home showed no rotation', () => {
  const unknown = homeWith('real-unknown', 'R1');
  const guardUnknown = new CredentialGuard(unknown);
  assert.equal(guardUnknown.rotates, null);
  assert.equal(guardUnknown.afterCopiedRun(homeWith('copy-unknown', 'R2'), 'u'), true, 'unknown counts as rotating: written back');
  assert.equal(refreshIn(unknown), 'R2');

  const rotating = homeWith('real-rotating', 'R1');
  const guardRotating = new CredentialGuard(rotating);
  writeFileSync(join(rotating, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: 'R3', access_token: 'b', expires_at: 2 }));
  assert.equal(guardRotating.afterRealRun('r'), true);
  assert.equal(guardRotating.rotates, true);
  assert.equal(guardRotating.afterCopiedRun(homeWith('copy-rotating', 'R4'), 'c'), true);
  assert.equal(refreshIn(rotating), 'R4');

  const steady = homeWith('real-steady', 'R1');
  const guardSteady = new CredentialGuard(steady);
  writeFileSync(join(steady, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: 'R1', access_token: 'b', expires_at: 2 }));
  assert.equal(guardSteady.afterRealRun('r'), true);
  assert.equal(guardSteady.rotates, false, 'the real home refreshed and kept its refresh token');
  assert.equal(guardSteady.afterCopiedRun(homeWith('copy-steady', 'R5'), 'c'), false, 'no rotation: nothing written back');
  assert.equal(refreshIn(steady), 'R1');
});

// SonarCloud S3776 on #803: parseStreamJson was split at a cognitive complexity of 20; what it reads must not move.
test('stream-json: the LAST non-blank assistant text, the LAST resume hint, the lines parsed, each role counted', () => {
  const stream = [
    JSON.stringify({ role: 'assistant', content: 'first' }),
    '',
    'not json',
    JSON.stringify({ role: 'assistant', content: [{ type: 'text', text: 'sec' }, 'ond'] }),
    JSON.stringify({ role: 'assistant', content: '   ' }),
    JSON.stringify({ role: 'meta', type: 'session.resume_hint', session_id: 's-1' }),
    JSON.stringify({ role: 'meta', type: 'other', session_id: 'ignored' }),
    JSON.stringify({ role: 'meta', type: 'session.resume_hint', session_id: 's-2' }),
    JSON.stringify({ content: 'no role' }),
    JSON.stringify({ role: 'assistant', content: { not: 'text' } }),
  ].join('\n');

  assert.deepEqual(parseStreamJson(stream), {
    answer: 'second', sessionId: 's-2', lines: 8, roles: { assistant: 4, meta: 3, '?': 1 },
  });
  assert.deepEqual(parseStreamJson(undefined), { answer: '', sessionId: '', lines: 0, roles: {} });
});

// ------------------------------------------------------------------- the small ACP client's two features (E5.1, S5.1.1–S5.1.2)
// A fake agent in node — never Kimi — so these run on every CI leg. It answers `initialize`, `session/new` and `session/load`
// (the load AFTER replaying two old answer chunks and one tool call, as M-K16 saw Kimi replay its history), writes what it saw
// to its own log synchronously, stays alive after its stdin ends (so a kill is the only way out), and writes `exited` only on
// a NORMAL exit — a tree kill skips that handler, so "no `exited` line" is how a test sees the kill on Windows and POSIX alike.
const FAKE_AGENT = String.raw`
import { appendFileSync } from 'node:fs';
const log = process.argv[2];
const note = (what) => appendFileSync(log, what + '\n');
const write = (o) => process.stdout.write(JSON.stringify(o) + '\n');
const update = (sessionId, u) => write({ jsonrpc: '2.0', method: 'session/update', params: { sessionId, update: u } });
const chunk = (text) => ({ sessionUpdate: 'agent_message_chunk', content: { type: 'text', text } });
const answers = {
  initialize: () => ({ protocolVersion: 1, agentCapabilities: { loadSession: true } }),
  'session/new': () => ({ sessionId: 's-new' }),
  'session/load': (p) => {
    if (p.sessionId === 's-long') update(p.sessionId, chunk('x'.repeat(300)));
    update(p.sessionId, chunk('OLD'));
    update(p.sessionId, chunk('OLD'));
    update(p.sessionId, { sessionUpdate: 'tool_call', toolCallId: 't-old', title: 'Read old', kind: 'read', status: 'completed' });
    return {};
  },
  'session/prompt': (p) => { update(p.sessionId, chunk('NEW')); return { stopReason: 'end_turn' }; },
};
let buffered = '';
process.stdin.on('data', (data) => {
  buffered += data.toString('utf8');
  let at;
  while ((at = buffered.indexOf('\n')) >= 0) {
    const m = JSON.parse(buffered.slice(0, at));
    buffered = buffered.slice(at + 1);
    if (m.method === 'session/cancel') note('cancel');
    else if (answers[m.method]) write({ jsonrpc: '2.0', id: m.id, result: answers[m.method](m.params ?? {}) });
  }
});
process.stdin.on('end', () => note('eof'));
process.on('exit', () => note('exited'));
setInterval(() => {}, 1000);
`;

const acpBase = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-kimi-acp-'));
after(() => rmSync(acpBase, { recursive: true, force: true }));
writeFileSync(join(acpBase, 'fake-agent.mjs'), FAKE_AGENT);

/** One turn of the small client against the fake agent; its log's lines and the turn's state. */
async function fakeTurn(name, options) {
  const log = join(acpBase, `${name}.log`);
  writeFileSync(log, '');
  const state = await acpTurn({ exe: process.execPath, args: [join(acpBase, 'fake-agent.mjs'), log], cwd: acpBase, env: process.env, deadlineMs: 20_000, ...options });
  const seen = readFileSync(log, 'utf8').split('\n').filter(Boolean);
  return { state, order: seen.includes('exited') ? seen : [...seen, 'killed'] };
}

test('S5.1.1 acpTurn records what arrives during session/load, and none of it is the answer', async () => {
  const { state } = await fakeTurn('load-updates', { load: 's-1' });

  assert.deepEqual(state.loadUpdates, [
    { kind: 'agent_message_chunk', text: 'OLD' },
    { kind: 'agent_message_chunk', text: 'OLD' },
    { kind: 'tool_call' },
  ]);
  assert.equal(state.answer, '');
});

test('S5.1.1 a load update keeps only the first 120 characters of its text, and the answer is only what follows the prompt', async () => {
  const { state } = await fakeTurn('load-then-prompt', { load: 's-long', prompt: 'say it' });

  assert.deepEqual(state.loadUpdates?.[0], { kind: 'agent_message_chunk', text: 'x'.repeat(120) });
  assert.equal(state.answer, 'NEW', 'the answer is what arrives after the prompt — the replay is not');
});

test('S5.1.2 endLikeTheShim cancels, waits, kills, then closes stdin', async () => {
  const { order, state } = await fakeTurn('end-like-the-shim', { endLikeTheShim: true });

  assert.deepEqual(order, ['cancel', 'killed']);
  assert.equal(state.sessionId, 's-new');
});
