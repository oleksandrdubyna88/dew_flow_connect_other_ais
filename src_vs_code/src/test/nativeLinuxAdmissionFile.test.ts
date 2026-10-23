import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { ON_NATIVE_LINUX_SINCE } from '../runtimeSince';

/**
 * The extension's half of the native-Linux admission (PR #922's CI round): "which runtime runs on a native Linux host, and from which
 * `coai-mcp`" is ONE file, `shared/native-linux-admission.json`. The server LOADS it (`NativeLinuxAdmission`, whose names ARE
 * `HostAdmission.MeasuredOnNativeLinux`, held by `NativeLinuxAdmissionFileTests`); this test holds `ON_NATIVE_LINUX_SINCE` to the same
 * file. Neither side parses the other's source (`NothingReadsAnotherProgramsSourceTests`) — a shared fact both sides are held to
 * goes red where a regular expression over source goes quiet.
 */

const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

interface AdmissionFile { runtimes: { runtime: string; since: string; source: string }[] }

const file = JSON.parse(readFileSync(repo('shared', 'native-linux-admission.json'), 'utf8')) as AdmissionFile;
const fileSince = Object.fromEntries(file.runtimes.map((entry) => [entry.runtime.toLowerCase(), entry.since]));

test('the shared admission file names at least one runtime — an empty file would make the comparison below vacuous', () => {
  assert.ok(file.runtimes.length > 0, 'shared/native-linux-admission.json names no runtime');
  assert.ok(file.runtimes.every((entry) => /^\d+\.\d+\.\d+$/u.test(entry.since) && entry.source.length > 0), 'every entry carries a release number and the RESULTS section it was measured in');
});

test('ON_NATIVE_LINUX_SINCE names exactly the runtimes shared/native-linux-admission.json names', () => {
  const extension = Object.keys(ON_NATIVE_LINUX_SINCE).sort();
  const shared = Object.keys(fileSince).sort();

  const promisedNotAdmitted = extension.filter((runtime) => !shared.includes(runtime));
  const admittedNotPromised = shared.filter((runtime) => !extension.includes(runtime));

  assert.deepEqual(promisedNotAdmitted, [],
    'ON_NATIVE_LINUX_SINCE (src/runtimeSince.ts) promises these runtimes on a native Linux host but shared/native-linux-admission.json — the file the server admits by — does not name them: the card would promise a launch the server refuses');
  assert.deepEqual(admittedNotPromised, [],
    'shared/native-linux-admission.json admits these runtimes on a native Linux host but ON_NATIVE_LINUX_SINCE (src/runtimeSince.ts) has no entry: the card would tell a person the row is refused where the server runs it');
});

test('every ON_NATIVE_LINUX_SINCE release is the one shared/native-linux-admission.json gives', () => {
  const different = Object.entries(ON_NATIVE_LINUX_SINCE)
    .filter(([runtime, since]) => fileSince[runtime] !== undefined && fileSince[runtime] !== since)
    .map(([runtime, since]) => `${runtime}: the extension says ${since}, shared/native-linux-admission.json says ${fileSince[runtime]}`);

  assert.deepEqual(different, [], 'the extension would tell a person the wrong first release for a native Linux host');
});
