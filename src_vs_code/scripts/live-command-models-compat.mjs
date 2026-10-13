/** Measure the COAI_COMMAND_MODELS boundary (the third `coder` slot) against a real released MCP.
 * No reviewer or GPU is run. Build Debug MCP and compile the extension first. Set COAI_OLD_SERVER or pass
 * --tag=mcp-vX.Y.Z. Exit 2 means a binary could not be obtained; it is not a passing compatibility result.
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
import { flag, newServer, releasedServer, run } from './releasedHalves.mjs';

const require = createRequire(import.meta.url);
const { commandModelsEnv, commandModelsFrom } = require('../out/commandModels.js');
const work = mkdtempSync(join(tmpdir(), 'coai-command-models-compat-'));
const old = releasedServer(work, flag('tag', ''));
const current = flag('new', newServer());
if (!old || !current) {
  console.error('Both released and current binaries are required; no compatibility result.');
  process.exit(2);
}
// A user's ambient settings must not change an isolated compatibility fixture.
for (const key of Object.keys(process.env)) if (key.startsWith('COAI_')) delete process.env[key];
const versionReply = await run(old, ['--version'], work);
assert.equal(versionReply.code, 0, versionReply.err);
const version = versionReply.out.match(/\d+\.\d+\.\d+/)?.[0];
assert.ok(version, 'released binary must identify its version');

const wire = commandModelsEnv(commandModelsFrom({
  claude: { coder: 'x-fast' },
  codex: { strongest: 'gpt-6-astra', implementation: 'gpt-6-luna', coder: 'gpt-6-mini' },
}));
assert.ok(wire.COAI_COMMAND_MODELS, 'the extension must put a changed coder on the wire');
assert.equal(JSON.parse(wire.COAI_COMMAND_MODELS).claude.coder, 'x-fast');

async function providers(binary, name, env = wire) {
  const data = join(work, name);
  mkdirSync(data);
  writeFileSync(join(data, 'settings.json'), JSON.stringify(env));
  const reply = await run(binary, ['--providers'], data, env);
  assert.equal(reply.code, 0, reply.err);
  writeFileSync(join(work, name + '.json'), reply.out);
  return JSON.parse(reply.out);
}
const unknownKey = (reply) => (reply.unrecognised ?? []).some((entry) =>
  JSON.stringify(entry).includes('COAI_COMMAND_MODELS'));
// The control: an UNREADABLE value must be reported by the old binary, or the check below could pass
// against a `--providers` that never reports this key at all.
const control = await providers(old, 'old-unreadable', { COAI_COMMAND_MODELS: '{ not json' });
assert.equal(unknownKey(control), true, 'the old binary must report an unreadable COAI_COMMAND_MODELS, or this check sees nothing');
const before = await providers(old, 'old-with-coder');
assert.equal(unknownKey(before), false, 'an older server must ignore the extra coder field, not reject the setting');
const latest = await providers(current, 'new-with-coder');
assert.deepEqual(latest.unrecognised ?? [], [], 'the current server must accept the serialized command models');
console.log(JSON.stringify({ oldVersion: version, oldSha256: createHash('sha256').update(readFileSync(old)).digest('hex'),
  controlReported: true, oldIgnoresCoder: true, currentAccepted: true, artifacts: work }, null, 2));
