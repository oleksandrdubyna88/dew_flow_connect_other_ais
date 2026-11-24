import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { HOSTS_SAID } from '../remoteHostsSaid';
import { dayOf } from '../remoteHosts';
import { sourceFiles, sourceOf } from './sourceReading';

/**
 * The host half is wired as written (E1.S1, A1-T2, A1-T5, A1-T14, A1-T18): source scans of the code that imports `vscode`, each with a
 * companion that finds the sanctioned site, so a scan that reads nothing is red. Where there is no program to run, a source
 * assertion is legitimate (a literal, a call site).
 */

const host = sourceOf('remoteSessionHost.ts');
const all = sourceFiles().map((file) => ({ file, text: sourceOf(file) }));
const countIn = (text: string, pattern: RegExp): number => [...text.matchAll(pattern)].length;
const filesWith = (pattern: RegExp): readonly string[] => all.filter((one) => pattern.test(one.text)).map((one) => one.file);

test('A1-T2: one function decides, and one writes', () => {
  assert.deepEqual(filesWith(/\bTERMINAL_HOSTS_KEY\b/u), ['extension.ts', 'remoteSessionHost.ts'].filter((file) => filesWith(/\bTERMINAL_HOSTS_KEY\b/u).includes(file)), 'the hosts key is named outside the host file');
  assert.ok(countIn(host, /TERMINAL_HOSTS_KEY/gu) >= 3, 'companion: the scan finds the key in the host file');
  assert.deepEqual(filesWith(/'coai\.terminalHosts'/u), ['remoteSessionHost.ts'], 'the literal key is in more than one file');
  assert.deepEqual([...filesWith(/\bconsentOf\(/u)].sort(), ['remoteHosts.ts', 'remoteKeeper.ts'], 'a second place decides whether a host may be read');
  assert.deepEqual(filesWith(/refused\.json/u), ['remoteSessionHost.ts'], 'the refusals file is named outside the host file');
  assert.equal(countIn(host, /'refused\.json'/gu), 1);
  assert.deepEqual(filesWith(/\.update\(\s*(?:TERMINAL_HOSTS_KEY|this\.seams\.key)\b/u), ['remoteSessionHost.ts'], 'a second writer of the list beside commit and the test door');
});

test('A1-T5: no list is exported to write, and the file has no content writer', () => {
  assert.deepEqual(filesWith(/export (?:function|const) (?:withAnswer|withoutHost|answersOf)\b/u), [], 'a function that takes a list to write is exported');
  assert.deepEqual(filesWith(/writeFileAtomically\(/u).filter((file) => file.startsWith('remoteRefusals')), ['remoteRefusalsDisk.ts'], 'the refusals path is written outside its one disk');
});

test('A1-T14: the words are the spec\'s, and the codes are literals of the host file', () => {
  assert.equal(HOSTS_SAID.title('h'), 'Read the Claude session and the AI accounts on “h” over ssh?');
  assert.ok(!HOSTS_SAID.emptyOn.includes('asks once') && !HOSTS_SAID.emptyOff.includes('asks once'));
  assert.equal(HOSTS_SAID.notSaved('h'), 'Your answer for “h” could not be saved, so the host is still not read and nothing was read. Answer again in a moment.');
  assert.equal(HOSTS_SAID.unkeptHost('h', 'busy', 'WSL: Ubuntu'), 'Your “Not this host” for “h” is not saved yet (another window is writing). No window of this side (WSL: Ubuntu) reads a host over ssh until it is. Each window tries again by itself.');
  assert.equal(HOSTS_SAID.unkeptStop('elsewhere', 'Linux'), 'The stop of the accounts round is not saved yet (another window of this side could not save it). No window of this side (Linux) reads a host over ssh until it is. Each window tries again by itself.');
  assert.ok(HOSTS_SAID.changeDetail({ host: 'h', answer: 'yes', how: 'auto', askedUtc: '2026-11-24T10:00:00.000Z' }, true, 'Linux').includes('nobody was asked'));
  assert.ok(HOSTS_SAID.changeDetail(undefined, false, 'Linux').includes('Linux') && !HOSTS_SAID.changeDetail(undefined, false, 'Linux').includes('writes a refusal again'));
  assert.equal(HOSTS_SAID.rowDescription({ host: 'h', answer: 'no', how: 'person', askedUtc: 'x' }, true), 'not read');
  assert.equal(dayOf(new Date(2026, 10, 24, 12).toISOString()), '2026-11-24');
  assert.ok(host.indexOf('Read the Claude session on') < 0, 'the question still says “Read the Claude session on …”');
  for (const code of ['read-remote-session', 'change-remote-host-answer', 'no-remote-host-asked', 'remote-host-refusal-not-kept']) {
    assert.equal(countIn(host, new RegExp(`code: '${code}'`, 'gu')), 1, `the code ${code} is not a literal of the host file exactly once`);
  }
  assert.deepEqual(filesWith(/mend-remote-hosts|remote-hosts-damaged|remote-host-refusal-restored/u), [], 'a code that does not exist is named');
  for (const text of [HOSTS_SAID.cureMarked, HOSTS_SAID.cureUnmarked, HOSTS_SAID.cureTorn, HOSTS_SAID.unkeptHost('h', 'disk', 'Linux')]) {
    assert.ok(!/[\\/]/u.test(text.replace(/CoAI: hosts read over ssh|“Not this host”|“Save now”/gu, '')), `a path separator is in a notice text: ${text}`);
  }
});

test('A1-T18: the host half is wired as written', () => {
  assert.ok(!/setInterval\(/u.test(host), 'something here looks on a timer');
  assert.match(host, /registerCommand\('coai\.terminalHosts', \(\) =>/u, 'the handler of the command takes a parameter');
  assert.equal(countIn(host, /'person'\s*\)|take: 'person'/gu) === 0, true, 'the host passes the person take itself');
  assert.equal(countIn(host, /saveWaiting\(\)/gu) >= 1, true);
  for (const file of ['remoteAccountsRound.ts', 'remoteAccountsHost.ts']) {
    assert.ok(!(() => { try { return /saveWaiting\(/u.test(readFileSync(join(__dirname, '..', '..', 'src', file), 'utf8')); } catch { return false; } })(), `the round calls saveWaiting: ${file}`);
  }
  assert.equal(countIn(host, /kind: 'mend'/gu), 1, 'kind: mend is written in more than one place of the host file');
  assert.equal(countIn(sourceOf('extension.ts'), /'terminal-hosts'/gu), 1);
  assert.equal(countIn(host, /MARKER_PREFIX = 'unsure-'/gu), 1);
  assert.deepEqual(filesWith(/claimFile\([^)]*'refused'|LOCK_ID = 'refused'/u), ['remoteRefusalsDisk.ts']);
  assert.deepEqual(filesWith(/take: 'person'|takenByPerson/u).filter((file) => file !== 'chatStoreLock.ts'), ['remoteKeeper.ts', 'remoteRefusals.ts', 'remoteRefusalsDisk.ts'].filter((file) => filesWith(/take: 'person'|takenByPerson/u).includes(file)));
  assert.equal(countIn(host, /actions?: /gu) >= 1, true);
  const config = sourceOf('sideConfig.ts');
  const allows = config.slice(config.indexOf('export const allowsNewHosts'), config.indexOf('export const ignoredLayerFor'));
  assert.ok(allows.includes('userLayer(') && !allows.includes('readerFor('), 'allowsNewHosts reads through an overlay or not through the user layer');
  assert.ok(/allowNew: \(\) => allowsNewHosts\(vscode\.workspace\.getConfiguration\('coai'\)\)/u.test(host), 'the keeper\'s allowNew seam does not read a fresh configuration');
});
