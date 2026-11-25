import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { HOSTS_SAID, notKeptSaid } from '../remoteHostsSaid';
import type { Committed } from '../remoteHosts';
import { dayOf } from '../remoteHosts';
import { bodyOf, sourceFiles, sourceOf } from './sourceReading';

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
  // The list's choice binding in remoteAccountsHost.ts is the ONE call site of the accounts modules (E1.S4, A4-T20, in remoteAccountsList.test.ts); the round has none.
  for (const file of ['remoteAccountsRound.ts']) {
    assert.ok(!/saveWaiting\(/u.test(sourceOf(file)), `the round calls saveWaiting: ${file}`);
  }
  assert.equal(countIn(host, /kind: 'mend'/gu), 1, 'kind: mend is written in more than one place of the host file');
  // The hosts folder is named in ONE place, the chat registration (v2 E4); the old activation names it no more.
  assert.equal(countIn(sourceOf('../shell/modules/chat/register.ts'), /'terminal-hosts'/gu), 1);
  assert.equal(countIn(sourceOf('extension.ts'), /'terminal-hosts'/gu), 0, 'the old activation names the hosts folder again, beside the chat module');
  assert.equal(countIn(host, /MARKER_PREFIX = 'unsure-'/gu), 1);
  assert.deepEqual(filesWith(/claimFile\([^)]*'refused'|LOCK_ID = 'refused'/u), ['remoteRefusalsDisk.ts']);
  assert.deepEqual(filesWith(/take: 'person'|takenByPerson/u).filter((file) => file !== 'chatStoreLock.ts'), ['remoteKeeper.ts', 'remoteRefusals.ts', 'remoteRefusalsDisk.ts'].filter((file) => filesWith(/take: 'person'|takenByPerson/u).includes(file)));
  assert.equal(countIn(host, /actions?: /gu) >= 1, true);
  const config = sourceOf('sideConfig.ts');
  const allows = config.slice(config.indexOf('export const allowsNewHosts'), config.indexOf('export const ignoredLayerFor'));
  assert.ok(allows.includes('userLayer(') && !allows.includes('readerFor('), 'allowsNewHosts reads through an overlay or not through the user layer');
  assert.ok(/allowNew: \(\) => allowsNewHosts\(vscode\.workspace\.getConfiguration\('coai'\)\)/u.test(host), 'the keeper\'s allowNew seam does not read a fresh configuration');
});

const U = '2026-11-24T10:00:00.000Z';
const W2 = '“Read” allows, with no further question for this host: reading the Claude session of its Creds terminals when you press take or add the question; and, every 10 minutes while such a terminal is open, reading which Claude Code, Codex and Antigravity accounts are signed in there, and their usage. For the usage, Claude Code is started on the host and asks Anthropic with the host’s own login; agy is started for its version. “Not this host” stops every ssh call of CoAI to this host.';
const FORGET_ON = '“Forget” removes the answer: the host then has none, and the next accounts round allows it again by itself.';
const FORGET_OFF = '“Forget” removes the answer: the host is then not read until you answer the question about it.';
const SIDE = (side: string): string => `A “Not this host” is kept on the side of this machine where you gave it — ${side}. “Read” or “Forget” in any window of that side lifts it. If the host is still not read a moment later, another window had not caught up yet: choose it once more. On another side (Windows, a WSL distro, a remote machine) choose it there.`;
const KEYSAID = 'coai.terminalAccountsAllowNewHosts';

test('A1-T14: every word W1 to W13 equals the text of the spec', () => {
  const day = dayOf(U);
  const standing = (answer: 'yes' | 'no', how: 'person' | 'auto') => ({ host: 'h', answer, how, askedUtc: U }) as const;
  assert.equal(HOSTS_SAID.title('h'), 'Read the Claude session and the AI accounts on “h” over ssh?');
  assert.equal(HOSTS_SAID.askedDetail, W2);
  assert.equal(HOSTS_SAID.changeDetail(standing('yes', 'person'), true, 'WSL: Ubuntu'), `Your answer now: Read, given ${U}. ${W2} ${FORGET_ON} ${SIDE('WSL: Ubuntu')}`);
  assert.equal(HOSTS_SAID.changeDetail(standing('yes', 'auto'), true, 'Linux'), `This host was allowed automatically on ${day}; nobody was asked. ${W2} ${FORGET_ON} ${SIDE('Linux')}`);
  assert.equal(HOSTS_SAID.changeDetail(standing('no', 'person'), false, 'Windows'), `Your answer now: Not this host, given ${U}. ${W2} ${FORGET_OFF} ${SIDE('Windows')}`);
  assert.equal(HOSTS_SAID.rowDescription(standing('yes', 'person'), true), 'the Claude session and the AI accounts are read');
  assert.equal(HOSTS_SAID.rowDescription(standing('yes', 'auto'), true), `allowed automatically on ${day}: the Claude session and the AI accounts are read`);
  assert.equal(HOSTS_SAID.rowDescription(standing('yes', 'auto'), false), `allowed automatically on ${day}; not read while ${KEYSAID} is off`);
  assert.equal(HOSTS_SAID.rowDescription(standing('no', 'person'), true), 'not read');
  assert.equal(HOSTS_SAID.rowDetail(standing('yes', 'person')), `asked ${U}`);
  assert.equal(HOSTS_SAID.rowDetail(standing('yes', 'auto')), `nobody was asked; written ${U}`);
  assert.equal(HOSTS_SAID.noAnswerReadable, 'no answer can be read for this host');
  assert.equal(HOSTS_SAID.emptyOn, 'No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is allowed by itself at the first accounts round and is listed here then; choose it here to forbid it.');
  assert.equal(HOSTS_SAID.emptyOff, 'No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is listed here once you answer the question about it.');
  assert.equal(HOSTS_SAID.emptyDamaged, 'The list of hosts read over ssh cannot be read, and no answer in it can be read. No host is read over ssh while that is so. Open a “Creds: <host> / <session>” terminal and run this again: your answer for its host repairs the list.');
  assert.equal(HOSTS_SAID.cannotRead, 'The list of hosts read over ssh cannot be read, so nothing was read. Run CoAI: hosts read over ssh and answer for a host there: your answer repairs the list.');
  assert.equal(HOSTS_SAID.fullOfRefusals('h'), 'The list of hosts read over ssh holds 100 hosts you refused, which is all it keeps, so your answer for “h” was not kept and nothing was read. Forget one of them in CoAI: hosts read over ssh, then answer again.');
  assert.equal(HOSTS_SAID.notSaved('h'), 'Your answer for “h” could not be saved, so the host is still not read and nothing was read. Answer again in a moment.');
  assert.equal(HOSTS_SAID.damagedList(3, 1, 2), `The list of hosts read over ssh is damaged, so no host is read by itself until it is repaired. 3 answers can still be read, 1 of them “Not this host”. 2 entries cannot be read, and one of them may have been a “Not this host”. Your answer here repairs the list: the answers that can be read are kept, and the entries that cannot are dropped. A host of a dropped entry has no answer afterwards, and with ${KEYSAID} on it is allowed again by itself. Look through the list afterwards.`);
  assert.equal(HOSTS_SAID.damagedFile('Linux', 4), 'The file of hosts you refused on this side (Linux) cannot be read, so no host is read from this side until it is repaired. 4 “Not this host” can still be read and are kept. A “Not this host” that was kept only in the file may be lost, and so may a stop of the accounts round. Your answer here writes the file again, with the accounts round STOPPED: look through CoAI: hosts read over ssh, then start the round from the list of AI accounts.');
  const whys = { busy: 'another window is writing', disk: 'the disk refused the write', lost: 'the write did not stay', elsewhere: 'another window of this side could not save it' } as const;
  for (const [why, phrase] of Object.entries(whys)) {
    const key = why as keyof typeof whys;
    assert.equal(HOSTS_SAID.unkeptHost('h', key, 'Linux'), `Your “Not this host” for “h” is not saved yet (${phrase}). No window of this side (Linux) reads a host over ssh until it is. Each window tries again by itself.`);
    assert.equal(HOSTS_SAID.unkeptStop(key, 'Linux'), `The stop of the accounts round is not saved yet (${phrase}). No window of this side (Linux) reads a host over ssh until it is. Each window tries again by itself.`);
  }
  // T1: with no marker the notice must not say that every window of the side is stopped
  assert.equal(HOSTS_SAID.unkeptHost('h', 'disk', 'Linux', false), 'Your “Not this host” for “h” is not saved yet (the disk refused the write). This window reads no host over ssh until it is. Each window tries again by itself.');
  assert.equal(HOSTS_SAID.unkeptStop('disk', 'Linux', false), 'The stop of the accounts round is not saved yet (the disk refused the write). This window reads no host over ssh until it is. Each window tries again by itself.');
  assert.ok(!HOSTS_SAID.unkeptHost('h', 'disk', 'Linux', false).includes('No window of this side'));
  assert.equal(HOSTS_SAID.cureMarked, '“Save now” writes it at once, also when another window holds the file and does not finish. If it still cannot be saved, check that the extension’s storage folder can be written.');
  assert.equal(HOSTS_SAID.cureUnmarked, 'Only THIS window knows your answer: the disk refused the note that tells the other windows. Keep this window open, and check that the extension’s storage folder can be written. Other windows of this side may still read hosts.');
  assert.equal(HOSTS_SAID.cureTorn, 'A note that could not be read was removed. It may have held a “Not this host” or a stop, so the accounts round is now stopped: look through CoAI: hosts read over ssh, then start the round.');
  assert.equal(HOSTS_SAID.ignoredWorkspace, `This workspace sets \`${KEYSAID}\`. It is ignored: only your own user settings count.`);
  assert.equal(HOSTS_SAID.ignoredFolder, `A folder of this workspace sets \`${KEYSAID}\`. It is ignored: only your own user settings count.`);
  assert.equal(HOSTS_SAID.rowDamagedList, 'the list of hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the list.');
  assert.equal(HOSTS_SAID.rowDamagedFile, 'the file of refused hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the file and leaves the round stopped.');
  assert.equal(HOSTS_SAID.rowWaiting, 'a “Not this host” or a stop is not saved yet on this side, so no host is read from it. It is tried again before every read. Choose this row to save it now.');
  assert.equal(HOSTS_SAID.startDamaged, 'The accounts round cannot be started: the file of refused hosts cannot be read. Answer for a host in CoAI: hosts read over ssh first.');
  assert.equal(HOSTS_SAID.startUnwritten, 'The start could not be saved, so the accounts round is still stopped. Choose this row again in a moment.');
  assert.equal(HOSTS_SAID.stoppedElsewhere, 'The accounts round is stopped. Choose this row to start it.');
});

test('A1-T14/B25/B27: the host raises the notice of an unsaved refusal, and the test door puts the file back', () => {
  assert.match(host, /unkept: \(host, why, marked\) => \{[\s\S]*?raiseUnkept\(live, host \?\? 'accounts-round'/u, 'the keeper\'s unkept seam does not raise the notice');
  const door = host.slice(host.indexOf('export const testDoorOf'));
  assert.ok(door.includes('rm(join(folder, REFUSED_FILE)') && door.includes('writeFile(join(folder, REFUSED_FILE)'), 'restore does not put the refusals file back');
  assert.match(door, /await state\.update\(TERMINAL_HOSTS_KEY, before\.list\);\n {4}if \(before\.file === undefined\) \{\n {6}await rm\(/u, 'restore does not put the file back in the step after the list');
  assert.ok(door.includes('MARKER_PREFIX') && door.includes('forgetHeld()'), 'restore leaves markers or what the window holds');
});

test('R9: W6\'s third sentence is for a Read or a Forget, and the torn cure of W9 has a W9 title', () => {
  const none = (kind: 'unwritten'): Committed => ({ kind, why: 'busy' });
  assert.equal(notKeptSaid('h', 'no', none('unwritten')), undefined, 'an unsaved "no" said W6\'s third sentence');
  for (const answer of ['yes', 'forget'] as const) {
    assert.equal(notKeptSaid('h', answer, none('unwritten')), HOSTS_SAID.notSaved('h'));
  }
  for (const answer of ['yes', 'no', 'forget'] as const) {
    assert.equal(notKeptSaid('h', answer, { kind: 'full' }), HOSTS_SAID.fullOfRefusals('h'));
    assert.equal(notKeptSaid('h', answer, { kind: 'damaged' }), HOSTS_SAID.cannotRead);
    assert.equal(notKeptSaid('h', answer, { kind: 'written', row: undefined, evicted: [] }), undefined);
  }
  assert.ok(!host.includes("case 'unwritten'"), 'the host has its own table of what was not kept');
});

test('R8: the torn cure is raised with the title of W9 and its own cure', () => {
  assert.ok(!host.includes('HOSTS_SAID.cureTorn, HOSTS_SAID.cureMarked'), 'W9\'s torn cure is passed as the title, with the marked cure as its cure');
  assert.match(host, /raiseUnkept\(live, 'accounts-round', HOSTS_SAID\.unkeptStop\('elsewhere', sideLabel\(\)\), HOSTS_SAID\.cureTorn/u, 'the torn notice has not the title of W9 and the torn cure');
});

test('R7: the host tells the person what "Save now" came to', () => {
  assert.match(host, /saveWaiting\(\)\.then\(\(result\) => after\(result\)\)/u, 'the result of "Save now" is dropped');
  assert.match(host, /pressSaid\(failedHostOf\(result\), result\)/u, 'the result of "Save now" is not turned into W6\'s words');
  assert.equal(countIn(host, /notKeptSaid\(/gu), 2, 'what was not kept is said in more than the change and the press');
});

test('A1-T15 (T15, host): the hosts list builds its choices through choicesOf, over the keeper\'s standings, and not over the profile list alone', () => {
  const body = bodyOf(host, 'async function changeHostsOf(');
  assert.match(body, /choicesOf\(listedHosts\(keeper\.stored\(\), liveHostsOf\(\)\), standings\)/u, 'the choices are not the listed hosts joined with the standings');
  assert.equal(countIn('const rows = listedHosts(stored, live).map(hostChoice);', /choicesOf\(/gu), 0, 'companion: the scan tells a list built from the profile alone');
});
