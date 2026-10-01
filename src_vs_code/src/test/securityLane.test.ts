import assert from 'node:assert/strict';
import { test } from 'node:test';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_SECURITY, securityWrite, securityLaneFrom } from '../securityLane';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { SECURITY_SEED } from '../securityLane.generated';
import { securityLaneBody } from '../securityLaneView';
import { settingMessageFrom } from '../settingRoute';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

test('each of the twelve presets has a real pairing checkbox and switching it off preserves other pairs', () => {
  assert.equal(SECURITY_SEED.prompts.length, 12);
  const page = runPanel(panelState('securityLane'));
  const vendor = DEFAULT_VENDORS.find(v => v.enabled)!;
  let lane = DEFAULT_SECURITY;
  for (const preset of SECURITY_SEED.prompts) {
    assert.ok(preset.triggers.length > 0);
    const field = 'pair:' + vendor.id + ':' + preset.id;
    const box = page.controls.find(c => c.dataset['securityField'] === field);
    assert.ok(box, field);
    box.checked = true;
    box.fire('change');
    const message = lastWrite(page);
    lane = securityWrite(lane, String(message['securityField']), message['value'], DEFAULT_VENDORS);
  }
  assert.equal(lane.runs.length, 12);
  const reloaded = securityLaneFrom(JSON.parse(JSON.stringify(lane)));
  const oneOff = securityWrite(reloaded, 'pair:' + vendor.id + ':redteam-auth-tokens', false, DEFAULT_VENDORS);
  assert.equal(oneOff.runs.length, 11);
  assert.ok(oneOff.runs.every(r => r.prompt !== 'redteam-auth-tokens'));
});

test('security controls run the page and write the selected field through the server settings seam', () => {
  const page = runPanel(panelState('securityLane'));
  const enabled = page.controls.find(c => c.dataset['setting'] === 'securityLane' && c.dataset['securityField'] === 'enabled');
  assert.ok(enabled);
  enabled.checked = true;
  enabled.fire('change');
  const message = lastWrite(page);
  assert.equal(message['key'], 'securityLane');
  assert.equal(message['securityField'], 'enabled');
  const routed = settingMessageFrom({ key: String(message['key']), securityField: String(message['securityField']), value: message['value'] });
  const securityLane = securityWrite(DEFAULT_SECURITY, routed.securityField!, routed.value, DEFAULT_VENDORS);
  const env = envBlock({ ...DEFAULTS, securityLane }, DEFAULT_VENDORS);
  assert.equal(JSON.parse(env['COAI_SECURITY_LANE']!).enabled, true);
});

test('a pair survives serialization and editing its stage leaves its prompt and vendor intact', () => {
  const added = securityWrite(DEFAULT_SECURITY, 'addRun', true, DEFAULT_VENDORS);
  assert.equal(added.runs.length, 1);
  const changed = securityWrite(added, 'run:0:code', false, DEFAULT_VENDORS);
  const loaded = securityLaneFrom(JSON.parse(JSON.stringify(changed)));
  assert.deepEqual(loaded.runs[0]?.stages, ['feature']);
  assert.equal(loaded.runs[0]?.vendor, added.runs[0]?.vendor);
  assert.equal(loaded.runs[0]?.prompt, 'redteam-authz');
});

test('opening prompt text posts its stable redteam id', () => {
  const page = runPanel(panelState('securityLane'));
  const edit = page.commands.find(c => c.dataset['command'] === 'editSecurityPrompt');
  assert.ok(edit);
  edit.fire('click');
  assert.deepEqual(page.posted.at(-1), { type: 'command', command: 'editSecurityPrompt', id: 'redteam-authz' });
});

test('editing a known field preserves unknown root, prompt and run fields', () => {
  const future = {
    ...DEFAULT_SECURITY, futurePolicy: 'refuse-on-old-server',
    prompts: [{ id: 'redteam-general', triggers: [], focus: [], futureDetector: 'v2' }],
    runs: [{ vendor: 'codex', prompt: 'redteam-general', futureSource: 'v2' }],
  };
  const edited = securityWrite(securityWrite(securityLaneFrom(future), 'enabled', true, DEFAULT_VENDORS),
    'run:0:code', false, DEFAULT_VENDORS);
  const wire = JSON.parse(JSON.stringify(edited));
  assert.equal(wire.futurePolicy, future.futurePolicy);
  assert.equal(wire.prompts[0].futureDetector, 'v2');
  assert.equal(wire.runs[0].futureSource, 'v2');
});

test('malformed trigger metadata cannot disappear and activate the seeded prompt', () => {
  const invalid = { ...DEFAULT_SECURITY, enabled: true, prompts: [{ id: 'redteam-general', triggers: null, focus: [] }] };
  const loaded = securityLaneFrom(invalid);
  assert.equal(loaded.enabled, false);
  assert.deepEqual(JSON.parse(JSON.stringify(loaded))['invalidConfiguration'], invalid);
});

test('generated security metadata agrees with the shared catalog', () => {
  assert.deepEqual(SECURITY_SEED, JSON.parse(readFileSync(resolve(__dirname, '../../../shared/security-lane.json'), 'utf8')));
});

test('stored custom metadata keeps all twelve presets selectable and condition edits preserve other tags', () => {
  const loaded = securityLaneFrom({ prompts: [{ id: 'redteam-custom', triggers: ['future-detector'] }] });
  assert.equal(loaded.prompts.length, 13);
  const tagged = securityWrite(loaded, 'trigger:redteam-custom:oauth', true, DEFAULT_VENDORS);
  assert.deepEqual(tagged.prompts.find(p => p.id === 'redteam-custom')?.triggers, ['future-detector', 'oauth']);
  const untagged = securityWrite(tagged, 'trigger:redteam-custom:oauth', false, DEFAULT_VENDORS);
  assert.deepEqual(untagged.prompts.find(p => p.id === 'redteam-custom')?.triggers, ['future-detector']);
});

test('an older server cannot enable the lane through its checkbox', () => {
  const html = securityLaneBody(DEFAULT_SECURITY, DEFAULT_VENDORS, '0.40.3');
  assert.match(html, /data-security-field="enabled" disabled/);
  assert.match(html, /does not run this lane/);
  assert.doesNotMatch(securityLaneBody(DEFAULT_SECURITY, DEFAULT_VENDORS, '0.41.0'), /data-security-field="enabled" disabled/);
});

test('security configuration is held back from older servers without erasing the saved selection', () => {
  const securityLane = securityWrite(DEFAULT_SECURITY, 'addRun', true, DEFAULT_VENDORS);
  const settings = { ...DEFAULTS, securityLane };
  assert.equal(envBlock(settings, DEFAULT_VENDORS, '0.40.3')['COAI_SECURITY_LANE'], undefined);
  assert.equal(JSON.parse(envBlock(settings, DEFAULT_VENDORS, '0.41.0')['COAI_SECURITY_LANE']!).runs.length, 1);
  assert.equal(JSON.parse(envBlock(settings, DEFAULT_VENDORS, '')['COAI_SECURITY_LANE']!).runs.length, 1);
  assert.equal(settings.securityLane.runs.length, 1);
});

test('a newly enabled lane selects its conditional authorization check by default', () => {
  const enabled = securityWrite(DEFAULT_SECURITY, 'enabled', true, DEFAULT_VENDORS);
  assert.equal(enabled.runs.length, 1);
  assert.equal(enabled.runs[0]?.prompt, 'redteam-authz');
  const vendor = enabled.runs[0]!.vendor;
  assert.equal(securityWrite(enabled, 'pair:' + vendor + ':redteam-authz', false, DEFAULT_VENDORS).runs.length, 0);
});

test('settings explain why a saved preset with no triggers cannot run', () => {
  const lane = securityWrite(DEFAULT_SECURITY, 'prompt:redteam-authz:triggers', '', DEFAULT_VENDORS);
  const html = securityLaneBody(lane, DEFAULT_VENDORS, '0.41.0');
  assert.match(html, /redteam-authz has no triggers; select at least one condition to run this preset/);
});

test('a removed reviewer or prompt remains visible with a repair instruction', () => {
  const lane = securityLaneFrom({ ...DEFAULT_SECURITY,
    runs: [{ vendor: 'removed-reviewer', prompt: 'redteam-removed' }],
  });
  const html = securityLaneBody(lane, DEFAULT_VENDORS, '0.41.0');
  assert.match(html, /Reviewer removed-reviewer is unavailable; select an enabled reviewer/);
  assert.match(html, /Prompt redteam-removed is missing; select a registered prompt/);
  assert.equal(lane.runs[0]?.vendor, 'removed-reviewer');
});

const binary = resolve(__dirname, '../../../src_mcp/src/bin/Debug/net10.0', process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp');
test('the real server refuses the unknown trigger preserved by panel serialization',
  { skip: existsSync(binary) ? false : 'build the Debug MCP server for the live settings contract' }, () => {
    const data = mkdtempSync(join(tmpdir(), 'coai-security-contract-'));
    const vendors = vendorsFrom([{ id: 'qwen', runtime: 'local', model: 'fixture' }]);
    const securityLane = securityLaneFrom({ ...DEFAULT_SECURITY, enabled: true,
      prompts: [{ id: 'redteam-general', triggers: ['future-detector'], focus: [] }],
      runs: [{ vendor: 'qwen', prompt: 'redteam-general' }],
    });
    try {
      writeFileSync(join(data, 'settings.json'), JSON.stringify(envBlock({ ...DEFAULTS, securityLane }, vendors)));
      const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('COAI_')));
      const result = spawnSync(binary, ['--providers'], { env: { ...env, COAI_DATA_DIR: data, COAI_CREDS_KEY: '' },
        encoding: 'utf8', windowsHide: true, timeout: 60_000, maxBuffer: 1024 * 1024 });
      assert.equal(result.status, 0, result.stderr);
      const reply = JSON.parse(result.stdout);
      assert.ok(reply.unrecognised.some((s: string) => s.includes('COAI_SECURITY_LANE') && s.includes('trigger')));
    } finally { rmSync(data, { recursive: true, force: true }); }
  });
