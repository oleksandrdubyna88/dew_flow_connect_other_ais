/** Measure the security settings boundary against a real released MCP. No reviewer or GPU is run.
 * Build Debug MCP and compile the extension first. Set COAI_OLD_SERVER or pass --tag=mcp-vX.Y.Z.
 * Exit 2 means a released binary could not be obtained; it is not a passing compatibility result.
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
import { flag, newServer, releasedServer, run } from './releasedHalves.mjs';

const require = createRequire(import.meta.url);
const { DEFAULTS, envBlock } = require('../out/settingsShape.js');
const { DEFAULT_SECURITY } = require('../out/securityLane.js');
const { DEFAULT_VENDORS } = require('../out/vendors.js');
const work = mkdtempSync(join(tmpdir(), 'coai-security-compat-'));
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
const vendors = DEFAULT_VENDORS.map(v => ({ ...v, enabled: false }));
const lane = { ...DEFAULT_SECURITY, enabled: true, runs: [{ vendor: 'codex', prompt: 'redteam-authz' }] };
const settings = { ...DEFAULTS, securityLane: lane };
const held = envBlock(settings, vendors, version);
assert.equal(held.COAI_SECURITY_LANE, undefined, 'the old binary must receive no security setting');
const supported = envBlock(settings, vendors, '0.41.0');
assert.ok(supported.COAI_SECURITY_LANE);
assert.equal(settings.securityLane.runs.length, 1, 'version gating must retain the saved checkbox');

async function providers(binary, name, env) {
  const data = join(work, name);
  mkdirSync(data);
  writeFileSync(join(data, 'settings.json'), JSON.stringify(env));
  const reply = await run(binary, ['--providers'], data, env);
  assert.equal(reply.code, 0, reply.err);
  writeFileSync(join(work, name + '.json'), reply.out);
  const parsed = JSON.parse(reply.out);
  delete parsed.vaultReadUtc;
  return parsed;
}
const previous = await providers(old, 'old-held', held);
const inert = await providers(old, 'old-unknown-key', supported);
assert.deepEqual(inert, previous, 'a manually supplied future key must remain inert on the old half');
const latest = await providers(current, 'new-supported', supported);
assert.deepEqual(latest.unrecognised, [], 'the new half must accept the serialized security setting');
console.log(JSON.stringify({ oldVersion: version, oldSha256: createHash('sha256').update(readFileSync(old)).digest('hex'),
  heldBack: true, oldUnknownKeyInert: true, currentAccepted: true, artifacts: work }, null, 2));
