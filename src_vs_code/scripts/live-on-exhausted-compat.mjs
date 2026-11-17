/** Measure what the panel writes for When the rounds run out against a real RELEASED coai-mcp that predates Auto, and the
 * current one (todo/PLAN_auto_never_asks_a_person.md § 4.6, E2.S1). No reviewer is run. Build the current server (the
 * Release configuration or Debug) and compile the extension first (`npm run test:on-exhausted-compat` compiles). Set
 * COAI_OLD_SERVER or pass --tag=mcp-v0.65.4; --new=<path> names the current binary when it is not in the usual build
 * folder. Exit 2 means a binary could not be obtained, or the released one already knows Auto; it is not a passing
 * compatibility result.
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
import { HERE, flag, newServer, releasedServer, run as runServer } from './releasedHalves.mjs';
import { startOwnedRun } from './ownedRun.mjs';
// This script owns ONE run folder (TEMP, TMP and TMPDIR name it from here on, so what it and its children make in temp lands inside and is removed with it).
const run = await startOwnedRun({ what: 'compat-policy' });

const require = createRequire(import.meta.url);
const { FEATURES, readBinaryFeatures } = require('../out/binaryFeatures.js');
const { serverSettingsJsonWith } = require('../out/serverSettingsFile.js');
const { DEFAULTS } = require('../out/settingsShape.js');
const { vendorsFrom } = require('../out/vendors.js');

/** The build this checkout just made: the shared finder (Debug), then the Release folder a Release build fills. */
function currentServer() {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';
  const release = join(HERE, 'src_mcp', 'src', 'bin', 'Release', 'net10.0', exe);

  return flag('new', newServer() || (existsSync(release) ? release : ''));
}

// testsToDelete-exempt: raw-temp — runs inside this script's own run folder, startOwnedRun moved TEMP there
const work = mkdtempSync(join(tmpdir(), 'coai-on-exhausted-compat-'));
const old = releasedServer(work, flag('tag', ''));
const current = currentServer();
if (!old || !current) {
  console.error('Both released and current binaries are required; no compatibility result.');
  process.exit(2);
}
// A user's ambient settings must not change an isolated compatibility fixture.
for (const key of Object.keys(process.env)) if (key.startsWith('COAI_')) delete process.env[key];

const versionOf = async (binary) => {
  const reply = await runServer(binary, ['--version'], work);
  assert.equal(reply.code, 0, reply.err);

  return reply.out.match(/\d+\.\d+\.\d+/)?.[0];
};
const oldVersion = await versionOf(old);
const currentVersion = await versionOf(current);
assert.ok(oldVersion && currentVersion, 'both binaries must identify their version');

/** What a binary says it accepts, through the extension's own reader — the answer the panel would settle on. */
const featuresOf = (binary) => readBinaryFeatures(async (args) => {
  const reply = await runServer(binary, args, work);

  return { code: reply.code, output: reply.out };
});
const olderAnswer = await featuresOf(old);
if (olderAnswer.features.includes(FEATURES.onExhaustedAuto)) {
  console.error(`The released server ${oldVersion} already knows Auto; pass --tag=mcp-v0.65.4 or older. No result.`);
  process.exit(2);
}
const currentAnswer = await featuresOf(current);
assert.ok(currentAnswer.features.includes(FEATURES.onExhaustedAuto), 'the current server must list onExhaustedAuto (--features)');

/** `--providers` of one binary over a settings file with exactly this content. */
async function providers(binary, name, content) {
  const data = join(work, name);
  mkdirSync(data);
  writeFileSync(join(data, 'settings.json'), content);
  const reply = await runServer(binary, ['--providers'], data);
  assert.equal(reply.code, 0, reply.err);
  writeFileSync(join(work, name + '.json'), reply.out);

  return JSON.parse(reply.out);
}
const complainsAbout = (reply) => (reply.unrecognised ?? []).some((entry) => JSON.stringify(entry).includes('COAI_ON_EXHAUSTED'));

/** The settings file the extension's OWN writer makes for a pristine panel, given what a binary said. */
function writtenFor(answer) {
  return serverSettingsJsonWith(DEFAULTS, vendorsFrom([]), {
    writtenBy: '', installedServerVersion: '', priceOf: () => undefined, features: answer.features, featuresSettled: true,
  });
}

// The control: an Auto the old binary does not know must be reported by it, or the checks below could pass against a
// `--providers` that never reports this key at all.
const control = await providers(old, 'old-auto', JSON.stringify({ COAI_ON_EXHAUSTED: 'auto' }));
assert.equal(complainsAbout(control), true, 'the old binary must report an unrecognised COAI_ON_EXHAUSTED=auto, or this check sees nothing');

const oldFile = writtenFor(olderAnswer);
assert.equal(JSON.parse(oldFile).COAI_ON_EXHAUSTED, 'good_enough', 'a panel meeting a binary without Auto must write Good enough');
const oldReply = await providers(old, 'old-written', oldFile);
assert.equal(complainsAbout(oldReply), false, 'the old binary must read what the panel wrote for it without complaint');

const currentFile = writtenFor(currentAnswer);
assert.equal('COAI_ON_EXHAUSTED' in JSON.parse(currentFile), false, 'a panel meeting a binary with Auto must write no policy');
const currentReply = await providers(current, 'new-written', currentFile);
assert.deepEqual(currentReply.unrecognised ?? [], [], 'the current server must accept what the panel writes for it');

console.log(JSON.stringify({
  oldVersion,
  oldSha256: createHash('sha256').update(readFileSync(old)).digest('hex'),
  currentVersion,
  controlReported: true,
  oldWroteGoodEnoughAndAccepted: true,
  currentWroteNothingAndAccepted: true,
  artifacts: work,
}, null, 2));
