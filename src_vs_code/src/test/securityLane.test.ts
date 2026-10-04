import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_SECURITY, securityAlways, securityEnv, securityWrite, securityLaneFrom, type SecurityLane } from '../securityLane';
import { DEFAULT_VENDORS } from '../vendors';
import { SECURITY_SEED } from '../securityLane.generated';
import { settingMessageFrom } from '../settingRoute';
import { type Control, lastWrite, type Page, panelState, runPanel, withoutSeq } from './panelPageHarness';

const CONDITIONAL = SECURITY_SEED.prompts.filter(p => !securityAlways(p.id));

test('each of the twelve presets has a real pairing checkbox and switching it off preserves other pairs', () => {
  assert.equal(CONDITIONAL.length, 12);
  const page = runPanel(panelState('securityLane'));
  const vendor = DEFAULT_VENDORS.find(v => v.enabled)!;
  let lane = DEFAULT_SECURITY;
  for (const preset of CONDITIONAL) {
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
  // General is the first card since it shipped (2026-10-04).
  assert.deepEqual(withoutSeq(page.posted.at(-1)!), { type: 'command', command: 'editSecurityPrompt', id: 'redteam-general' });
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

test('stored custom metadata keeps all thirteen shipped prompts selectable and condition edits preserve other tags', () => {
  const loaded = securityLaneFrom({ prompts: [{ id: 'redteam-custom', triggers: ['future-detector'] }] });
  assert.equal(loaded.prompts.length, 14);
  const tagged = securityWrite(loaded, 'trigger:redteam-custom:oauth', true, DEFAULT_VENDORS);
  assert.deepEqual(tagged.prompts.find(p => p.id === 'redteam-custom')?.triggers, ['future-detector', 'oauth']);
  const untagged = securityWrite(tagged, 'trigger:redteam-custom:oauth', false, DEFAULT_VENDORS);
  assert.deepEqual(untagged.prompts.find(p => p.id === 'redteam-custom')?.triggers, ['future-detector']);
});

const OLD_SERVER = { kind: 'known', version: '0.40.3', remembered: false, updateOffered: false } as const;
const CURRENT_SERVER = { ...OLD_SERVER, version: '0.41.0' } as const;

/** The Security lane tab's own pane, as the Settings page drew it. */
function lanePane(page: Page): string {
  const start = page.html.indexOf('id="pane-securityLane"');
  assert.ok(start >= 0, 'the Settings page has no Security lane tab');
  return page.html.slice(start, page.html.indexOf('</section>', start));
}

/** A person changing one control the way the page lets them: a box flipped, a field typed into. */
function change(control: Control): void {
  const typed: Record<string, string> = { number: '3', text: 'redteam-extra' };
  if (control.type === 'checkbox') control.checked = !control.checked;
  else if (control.type in typed) control.value = typed[control.type]!;
  control.fire('change');
}

test('no control on an older server\'s Security lane tab can switch the lane on', () => {
  const page = runPanel(panelState('securityLane', { server: OLD_SERVER }));
  const lane = page.controls.filter(c => c.dataset['setting'] === 'securityLane');
  const enabled = lane.find(c => c.dataset['securityField'] === 'enabled');
  assert.ok(enabled, 'the tab has no lane switch at all');
  // A disabled box is one a person cannot change in a webview; that is the whole of its guarantee.
  assert.equal(enabled.disabled, true, 'an older server\'s lane switch can be ticked');
  assert.match(lanePane(page), /does not run this lane/);

  // Everything the page still lets a person change, changed, each write routed as the host routes it.
  let written = DEFAULT_SECURITY;
  let writes = 0;
  for (const control of lane.filter(c => !c.disabled)) {
    const before = page.posted.length;
    change(control);
    if (page.posted.length === before) continue;
    const message = lastWrite(page);
    written = securityWrite(written, String(message['securityField']), message['value'], DEFAULT_VENDORS);
    writes += 1;
  }
  assert.ok(writes > 0, 'nothing on the tab could be changed, so this proved nothing about what can');
  assert.equal(written.enabled, false, 'a control on an older server\'s tab switched the lane on');
  assert.equal(envBlock({ ...DEFAULTS, securityLane: written }, DEFAULT_VENDORS, OLD_SERVER.version)['COAI_SECURITY_LANE'], undefined);
});

test('a server new enough for the lane offers its switch, and ticking it turns the lane on', () => {
  const page = runPanel(panelState('securityLane', { server: CURRENT_SERVER }));
  const enabled = page.controls.find(c => c.dataset['securityField'] === 'enabled');
  assert.ok(enabled);
  assert.equal(enabled.disabled, false);
  assert.doesNotMatch(lanePane(page), /does not run this lane/);
  change(enabled);
  const message = lastWrite(page);
  assert.equal(securityWrite(DEFAULT_SECURITY, String(message['securityField']), message['value'], DEFAULT_VENDORS).enabled, true);
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

function pageWith(securityLane: SecurityLane): Page {
  return runPanel(panelState('securityLane', { settings: { ...DEFAULTS, securityLane }, server: CURRENT_SERVER }));
}

function routed(page: Page, lane: SecurityLane): SecurityLane {
  const message = lastWrite(page);
  return securityWrite(lane, String(message['securityField']), message['value'], DEFAULT_VENDORS);
}

test('a saved preset with no triggers says why it cannot run, and ticking a condition on the tab clears it', () => {
  const lane = securityWrite(DEFAULT_SECURITY, 'prompt:redteam-authz:triggers', '', DEFAULT_VENDORS);
  const page = pageWith(lane);
  const warning = /redteam-authz has no triggers; select at least one condition to run this preset/;
  assert.match(lanePane(page), warning);

  const box = page.controls.find(c => c.dataset['securityField'] === 'trigger:redteam-authz:authz');
  assert.ok(box, 'the tab offers no condition to tick');
  change(box);
  const repaired = routed(page, lane);
  assert.deepEqual(repaired.prompts.find(p => p.id === 'redteam-authz')?.triggers, ['authz']);
  assert.doesNotMatch(lanePane(pageWith(repaired)), warning);
});

test('a removed reviewer stays visible with a repair instruction, and choosing one on the tab repairs the pair', () => {
  const lane = securityLaneFrom({ ...DEFAULT_SECURITY,
    runs: [{ vendor: 'removed-reviewer', prompt: 'redteam-removed' }],
  });
  assert.equal(lane.runs[0]?.vendor, 'removed-reviewer');
  const page = pageWith(lane);
  assert.match(lanePane(page), /Reviewer removed-reviewer is unavailable; select an enabled reviewer/);
  assert.match(lanePane(page), /Prompt redteam-removed is missing; select a registered prompt/);

  const vendor = DEFAULT_VENDORS.find(v => v.enabled)!.id;
  const picker = page.controls.find(c => c.dataset['securityField'] === 'run:0:vendor');
  assert.ok(picker, 'the pair has no reviewer picker');
  assert.ok(picker.options.some(o => o.value === vendor), 'the picker does not offer an enabled reviewer');
  picker.value = vendor;
  picker.fire('change');
  const repaired = routed(page, lane);
  assert.equal(repaired.runs[0]?.vendor, vendor);
  assert.doesNotMatch(lanePane(pageWith(repaired)), /Reviewer removed-reviewer is unavailable/);
});

// General is a shipped "always" prompt (todo/PLAN_the_security_tab_reads_at_a_glance.md, D1): first in the
// list, only on or off, and its `always` flag is a catalogue fact that never reaches the wire.

const GENERAL = 'redteam-general';

test('general is the first shipped prompt and the only one that always runs', () => {
  assert.equal(SECURITY_SEED.prompts[0]?.id, GENERAL);
  assert.equal(securityAlways(GENERAL), true);
  assert.deepEqual(SECURITY_SEED.prompts.filter(p => securityAlways(p.id)).map(p => p.id), [GENERAL]);
  assert.equal(securityAlways('redteam-custom'), false);
});

test('the catalogue\'s always flag never reaches the wire, before or after a write', () => {
  // A 0.41/0.42 server refuses a prompt entry with any member besides id, triggers and focus.
  const enabled = securityWrite(DEFAULT_SECURITY, 'enabled', true, DEFAULT_VENDORS);
  const paired = securityWrite(enabled, 'pair:' + DEFAULT_VENDORS.find(v => v.enabled)!.id + ':' + GENERAL, true, DEFAULT_VENDORS);
  for (const lane of [DEFAULT_SECURITY, enabled, paired, securityLaneFrom({}), securityLaneFrom(JSON.parse(JSON.stringify(paired)))]) {
    const wire = JSON.parse(securityEnv({ ...lane, enabled: true }, '0.42.0')['COAI_SECURITY_LANE']!);
    for (const prompt of wire.prompts) assert.deepEqual(Object.keys(prompt).sort(), ['focus', 'id', 'triggers'], JSON.stringify(prompt));
  }
});

test('general\'s conditions cannot be set, but restore clears a hand-registered leftover', () => {
  const leftover = securityLaneFrom({ prompts: [{ id: GENERAL, triggers: ['sql'], focus: [] }] });
  for (const [field, value] of [['trigger:' + GENERAL + ':xss', true], ['focus:' + GENERAL + ':xss', true],
    ['prompt:' + GENERAL + ':triggers', 'xss'], ['prompt:' + GENERAL + ':focus', 'xss']] as const) {
    assert.deepEqual(securityWrite(leftover, field, value, DEFAULT_VENDORS), leftover, field);
  }
  const restored = securityWrite(leftover, 'prompt:' + GENERAL + ':restore', true, DEFAULT_VENDORS);
  const general = restored.prompts.find(p => p.id === GENERAL)!;
  assert.deepEqual(general.triggers, []);
  assert.deepEqual(general.focus, ['entry-point']);
});

test('restore puts a preset\'s shipped conditions back and keeps an unknown member', () => {
  const edited = securityLaneFrom({ prompts: [{ id: 'redteam-sql', triggers: ['xss'], focus: [], futureDetector: 'v2' }] });
  const restored = securityWrite(edited, 'prompt:redteam-sql:restore', true, DEFAULT_VENDORS);
  const sql = restored.prompts.find(p => p.id === 'redteam-sql') as unknown as Record<string, unknown>;
  assert.deepEqual(sql['triggers'], ['sql']);
  assert.deepEqual(sql['focus'], ['sql', 'entry-point']);
  assert.equal(sql['futureDetector'], 'v2');
  assert.deepEqual(securityWrite(edited, 'prompt:redteam-custom:restore', true, DEFAULT_VENDORS), edited, 'a custom prompt has nothing shipped to restore');
});

test('Add pair never switches general on — it costs a reviewer on every change', () => {
  let lane = DEFAULT_SECURITY;
  for (let i = 0; i < 16; i += 1) lane = securityWrite(lane, 'addRun', true, DEFAULT_VENDORS);
  assert.ok(lane.runs.length > 0);
  assert.ok(lane.runs.every(r => r.prompt !== GENERAL), JSON.stringify(lane.runs));
  assert.equal(securityWrite(DEFAULT_SECURITY, 'addRun', true, DEFAULT_VENDORS).runs[0]?.prompt, 'redteam-authz');
});

test('general\'s card draws no condition boxes and no missing-trigger warning, and says it runs on every change', () => {
  const page = pageWith(securityWrite(DEFAULT_SECURITY, 'enabled', true, DEFAULT_VENDORS));
  const conditions = page.controls.filter(c => /^(trigger|focus|prompt):redteam-general(:|$)/.test(String(c.dataset['securityField'])));
  assert.deepEqual(conditions.map(c => c.dataset['securityField']), []);
  assert.ok(page.controls.some(c => c.dataset['securityField'] === 'trigger:redteam-sql:sql'), 'a conditional preset lost its boxes too');
  // Scoped to general's own card, so a sentence elsewhere on the pane cannot satisfy it.
  const pane = lanePane(page);
  const start = pane.indexOf('<span class="seclane-name">redteam-general</span>');
  assert.ok(start >= 0, 'general has no card');
  const card = pane.slice(start, pane.indexOf('</fieldset>', start));
  assert.doesNotMatch(card, /has no triggers/);
  assert.match(card, /runs on every code change/i);
  assert.equal(page.commands.some(c => c.dataset['command'] === 'clearSecurityConditions'), false, 'nothing to clear, so no button');
});

/** General's own card, as the Security lane tab drew it. */
function generalCard(page: Page): string {
  const pane = lanePane(page);
  const start = pane.indexOf('<span class="seclane-name">redteam-general</span>');
  assert.ok(start >= 0, 'general has no card');
  return pane.slice(start, pane.indexOf('</fieldset>', start));
}

test('a general registered by hand with conditions says so on its card, and one button clears them', () => {
  // The coai code round on epic 1: the card claimed "runs on every code change" while an older server still ran
  // this leftover only on a signal, and restore had no control anywhere.
  const leftover = securityLaneFrom({ enabled: true, prompts: [{ id: GENERAL, triggers: ['sql'], focus: [] }] });
  const page = pageWith(leftover);
  assert.match(generalCard(page), /stored conditions/i);
  assert.doesNotMatch(generalCard(page), /runs on every code change while/i, 'the card claims what an older server will not do');

  const clear = page.commands.find(c => c.dataset['command'] === 'clearSecurityConditions');
  assert.ok(clear, 'the leftover has no button to clear it');
  assert.equal(clear.dataset['id'], GENERAL);
  clear.fire('click');
  assert.deepEqual(withoutSeq(page.posted.at(-1)!), { type: 'command', command: 'clearSecurityConditions', id: GENERAL });

  // What the host's case writes, routed the way it routes it.
  const cleared = securityWrite(leftover, 'prompt:' + GENERAL + ':restore', true, DEFAULT_VENDORS);
  assert.match(generalCard(pageWith(cleared)), /runs on every code change/i);
  assert.equal(pageWith(cleared).commands.some(c => c.dataset['command'] === 'clearSecurityConditions'), false);
});

// The live settings contract — the extension's serialized lane read by the REAL server — is a leg of
// `scripts/run-seam.mjs` (`npm run test:seam`), which CI runs against the binary it built and which
// refuses rather than skips when there is none. It used to live here, skipped whenever no Debug build
// sat beside the suite, which in CI was always.
