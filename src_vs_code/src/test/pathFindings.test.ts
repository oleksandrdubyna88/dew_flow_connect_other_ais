import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  type PathFinding,
  type PathValue,
  type Presence,
  findingFor,
  findingOf,
  findingSubject,
  missingAt,
  missingCure,
  missingTitle,
  markSentence,
  pathFindings,
  probeKindOf,
  probeable,
} from '../pathFindings';
import { DATA_DIRECTORY, QCONSULT_ROOTS, VENDOR_EXECUTABLE, WATCHED_DIRECTORIES } from '../pathSettings';

/**
 * The findings model (research/PLAN_paths_per_side.md E5.1): what the disk said about a path of THIS side, as the one value
 * the notice and the page mark are drawn from. Until it existed the page asked the disk about roots spelled for the
 * OTHER OS alone, and a root of this OS that was gone (`D:\old-project`) was a generic toast and nothing beside it.
 */

const root = (value: string): PathValue => ({ setting: QCONSULT_ROOTS, value, path: value });

/** A disk that answers from a table — a path it has no row for was never asked. */
function disk(table: Readonly<Record<string, Presence>>): (path: string) => Presence {
  return (path) => table[path] ?? 'pending';
}

test('a root of this OS the disk says is not there is a MISSING finding; one that is there is none', () => {
  const findings = pathFindings([root('D:\\old-project'), root('D:\\rsd\\work')], 'windows', disk({ 'D:\\old-project': 'absent', 'D:\\rsd\\work': 'here' }));

  assert.deepEqual(findings, [{
    key: 'coai.qconsultRoots', place: 'consultants/qconsult', value: 'D:\\old-project', family: 'windows', state: 'missing', owner: '',
  }], 'the deleted root of this OS is not a finding — today the page asks the disk about other-OS roots only');
});

test('a path the disk could not answer for is UNKNOWN — never missing, and it counts for nothing', () => {
  const findings = pathFindings([root('/srv/share/git')], 'posix', disk({ '/srv/share/git': 'unknown' }));

  assert.equal(findings[0]?.state, 'unknown', 'EACCES, EBUSY or a probe past its deadline is not an absence');
  assert.equal(missingAt(findings, 'consultants/qconsult'), 0, 'an unknown path was counted as missing');
});

test('a path not answered yet is no finding at all — the page says nothing until the disk has spoken', () => {
  assert.deepEqual(pathFindings([root('D:\\x')], 'windows', disk({})), []);
  assert.equal(findingOf(root('D:\\x'), 'windows', 'pending'), undefined);
});

test('a value of the other side, a bare CLI name and a relative root are never asked — E1 decides those', () => {
  // The other side's values are `otherSideHere`'s / `otherSideClis`'s to call; a second, lexical copy of that decision
  // here would drift from the one that asks the disk. A bare `codex` is a PATH lookup, a relative root is refused by name.
  assert.equal(probeable('/home/jinx/git', 'windows'), false, 'a WSL root on the Windows side is the other side\'s');
  assert.equal(probeable('C:\\tools\\codex.exe', 'posix'), false, 'a Windows CLI in WSL is the other side\'s');
  assert.equal(probeable('codex', 'windows'), false, 'a bare name is looked up on PATH, never statted as a path');
  assert.equal(probeable('src/alpha', 'posix'), false);
  assert.equal(probeable('  ', 'posix'), false);
  assert.equal(probeable('D:\\old-project', 'windows'), true);
  assert.equal(probeable('\\\\nas\\coai', 'windows'), true, 'a UNC share is this Windows side\'s');
  assert.equal(probeable('/home/jinx/git', 'posix'), true);

  const asked: string[] = [];
  pathFindings([root('/home/jinx/git'), root('src/alpha'), root('D:\\here')], 'windows', (path) => { asked.push(path); return 'absent'; });
  assert.deepEqual(asked, ['D:\\here'], 'the disk was asked about a value that is not this side\'s absolute path');
});

test('each setting asks for its own kind: a folder list and a folder ask about folders, a CLI path about a file', () => {
  const kinds: string[] = [];
  pathFindings([
    root('D:\\a'),
    { setting: VENDOR_EXECUTABLE, value: 'D:\\tools\\codex.exe', path: 'D:\\tools\\codex.exe', owner: 'codex' },
    { setting: WATCHED_DIRECTORIES, value: 'D:\\other', path: 'D:\\other' },
  ], 'windows', (_path, kind) => { kinds.push(kind); return 'here'; });

  assert.deepEqual(kinds, ['folder', 'file', 'folder']);
  assert.equal(probeKindOf(DATA_DIRECTORY.kind), 'folder');
});

test('a finding carries its owner — the model whose CLI path is gone — and the stored spelling, not the probed one', () => {
  // On Windows `/Program Files/nodejs/node.exe` is this side's and runs qualified (E1.2); it is spelled for the other OS
  // and so never asked here. A Windows path is: the page matches the finding by the STORED value on the card.
  const [finding] = pathFindings([{ setting: VENDOR_EXECUTABLE, value: 'D:\\tools\\codex.exe', path: 'D:\\tools\\codex.exe', owner: 'my-codex' }], 'windows', () => 'absent');

  assert.equal(finding?.owner, 'my-codex');
  assert.equal(finding?.value, 'D:\\tools\\codex.exe');
  assert.equal(finding?.place, 'models');
});

const MISSING: PathFinding = { key: 'coai.qconsultRoots', place: 'consultants/qconsult', value: 'D:\\old-project', family: 'windows', state: 'missing', owner: '' };
const UNKNOWN: PathFinding = { ...MISSING, state: 'unknown', value: 'D:\\share\\x' };

test('the tab count is the MISSING paths under a place, sub-places included; unknown and other places count for nothing', () => {
  const data: PathFinding = { key: 'coai.dataDirectory', place: 'setup/mcp', value: 'Z:\\coai', family: 'windows', state: 'missing', owner: '' };

  assert.equal(missingAt([MISSING, UNKNOWN, data], 'consultants'), 1, 'the top tab counts its sub-tab\'s findings');
  assert.equal(missingAt([MISSING, UNKNOWN, data], 'consultants/qconsult'), 1);
  assert.equal(missingAt([MISSING, UNKNOWN, data], 'setup'), 1);
  assert.equal(missingAt([MISSING, UNKNOWN, data], 'models'), 0);
  assert.equal(missingAt([], 'consultants'), 0);
});

test('a finding is found by its setting and its stored value, trimmed', () => {
  assert.equal(findingFor([MISSING, UNKNOWN], 'coai.qconsultRoots', ' D:\\old-project '), MISSING);
  assert.equal(findingFor([MISSING], 'coai.qconsultRoots', 'D:\\other'), undefined);
  assert.equal(findingFor([MISSING], 'coai.dataDirectory', 'D:\\old-project'), undefined, 'the same value under another setting is another finding');
});

test('the notice says "not found now", names the place, and never says "deleted" — an unmounted drive reports absence too', () => {
  assert.equal(findingSubject(MISSING), 'coai.qconsultRoots|D:\\old-project', 'one counter per path, so fixing one clears one');
  assert.equal(missingTitle(MISSING), "The question consultant's folder D:\\old-project was not found on this machine (Windows) just now — a disk row skips it.");
  assert.doesNotMatch(missingTitle(MISSING), /deleted/u);
  assert.match(missingCure(MISSING), /Create the folder, or remove it from the list in Settings › Consultants › Question consultant/u);

  const cli: PathFinding = { key: 'coai.vendors[].executablePath', place: 'models', value: 'C:\\t\\codex.exe', family: 'windows', state: 'missing', owner: 'codex' };
  assert.equal(missingTitle(cli), 'The CLI path of the model codex C:\\t\\codex.exe was not found on this machine (Windows) just now — that model cannot run here until it is.');
  assert.match(missingCure(cli), /Models card/u);

  const data: PathFinding = { key: 'coai.dataDirectory', place: 'setup/mcp', value: '/mnt/z/coai', family: 'posix', state: 'missing', owner: '' };
  assert.equal(missingTitle(data), "This window's data folder /mnt/z/coai was not found on this machine (WSL / Linux) just now — nothing is written there until it exists.");
  assert.match(missingCure(data), /Connect the drive, create the folder, or choose another/u);
});

test('the mark beside a value carries the word missing in red, or "could not be checked" in the warning tone', () => {
  assert.match(markSentence(MISSING), /^missing — not found on this machine \(Windows\) just now$/u);
  assert.match(markSentence(UNKNOWN), /^could not be checked on this machine \(Windows\)/u);
});
