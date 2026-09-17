import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The half of sending that no unit test here can reach.
 *
 * <p>`panelProvider.ts` and `roundsDbRead.ts` both import `vscode`, which throws at the first
 * `require` outside an extension host, so the decisions live in `bugsSend.ts` where a test can drive
 * them and the WIRING stays where it is. This repository has no extension-host harness — the house
 * pattern for that is a source assertion, as `chatFreshWiring.test.ts` and `bugsKeysWiring.test.ts`
 * do — and what is pinned here is the order of two things and the absence of a third.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/** Where one member begins and the next one does, so an assertion cannot match a neighbour. */
const between = (text: string, from: string, to: string): string => {
  const start = text.indexOf(from);
  const end = text.indexOf(to);
  assert.ok(start >= 0, `the region does not begin: ${from}`);
  assert.ok(end > start, `the region does not end: ${to}`);

  return text.slice(start, end);
};

/**
 * The same text with every comment gone.
 *
 * <p>Because a structural assertion that reads PROSE goes red on the sentence explaining why the
 * code is the way it is — this repository has been caught by that twice, and both times the comment
 * was right and the test was wrong.</p>
 */
const code = (text: string): string =>
  text.replace(/\/\*[\s\S]*?\*\//gu, '').split('\n').filter((line) => !/^\s*(\/\/|\*)/u.test(line)).join('\n');

/**
 * THE PREFLIGHT RUNS BEFORE THE CHILD EXISTS.
 *
 * <p>A plan reviewer put the failure precisely: with a stored key and a non-loopback `http://`
 * address, checking AFTER the spawn would put the credential into a process environment and let the
 * CLI answer 65 — the key having already been handed to a process pointed at an address it must not
 * cross. Both halves refusing is deliberate; only this one runs before the key moves.</p>
 */
test('nothing is spawned until the address, the key and the send row have been judged', () => {
  const sending = code(between(source('panelProvider.ts'), 'private async sendBugs(', 'private async watchSend('));

  const judged = sending.indexOf('mayStart(');
  const spawned = sending.indexOf('uploadRun(');

  assert.ok(judged >= 0, 'the send does not consult the preflight at all');
  assert.ok(spawned > judged,
    'the child is built before the address is judged, so a key reaches a process pointed at it');
  assert.match(sending, /if \(refusal !== undefined\)[\s\S]{0,400}return;/u,
    'a refusal must STOP the send rather than be reported beside one');
});

/** And the credential goes through the one door named for carrying one. */
test('the key reaches the child through uploadRun, and never as an argument', () => {
  const sending = code(between(source('panelProvider.ts'), 'private async sendBugs(', 'private async watchSend('));

  assert.match(sending, /uploadRun\(server\.fsPath, key\)/u, 'the send uses the door that carries a key');
  assert.doesNotMatch(sending, /'--key'/u, 'an argument is in ps, in /proc and in a shell history');
  assert.doesNotMatch(sending, /--key-file/u, 'a file is a credential on disk and a cleanup a crash skips');
  // THE COMPANION the repository's testing rule requires: a scan that asserts an ABSENCE proves
  // nothing until something proves the pattern can still match. Both spellings exist in the CLI that
  // would accept them, so a rename there turns this red rather than turning the ban vacuous.
  const cli = source('../../src_mcp/src/Program.cs');
  assert.match(cli, /--key-file/u, 'the pattern this forbids no longer matches anything anywhere');
  assert.match(sending, /'--upload-pairs', '--server', where/u,
    'the address is an argument, because an address is not a secret');
});

/**
 * The door itself: one function, and the environment is the only thing that carries the key.
 */
test('uploadRun is the only place a credential enters a child process', () => {
  const spawning = code(source('roundsDbRead.ts'));

  assert.match(spawning, /export function uploadRun\(executable: string, key: string\): Run/u);
  assert.match(spawning, /COAI_BUGS_KEY: key/u, 'the environment is how the CLI takes a key');

  const others = spawning.split('COAI_BUGS_KEY').length - 1;
  assert.equal(others, 1, 'a second place that can hand a credential to a child is a second place to audit');
});

/** A send that finished has to SAY what it came to, whatever it came to. */
test('every ending of a send is reported through the funnel', () => {
  const watching = code(between(source('panelProvider.ts'), 'private async watchSend(', 'private async setBugsKey('));

  assert.match(watching, /outcomeOf\(answer\.code, answer\.output\)/u,
    'the exit code and what it printed are both what decides the sentence');
  assert.match(watching, /await notify\(/u, 'through the one door, like everything else this extension says');
  assert.match(watching, /await this\.render\(\)/u,
    'and the section repaints, because the funnel moved under it');
});

/** The contributor key is stored where settings cannot sync it. */
test('the contributor key goes to secret storage, never to a setting', () => {
  const asking = code(between(source('panelProvider.ts'), 'private async setBugsKey(', 'private async setBugsServer('));

  assert.match(asking, /setContributorKey\(this\.context\.secrets/u);
  assert.match(asking, /password: true/u, 'a key typed in the clear is a key in a screen recording');
  assert.doesNotMatch(asking, /updateConfiguration|ConfigurationTarget/u,
    'settings sync, and a credential that follows somebody to another machine is unaccountable');
});
