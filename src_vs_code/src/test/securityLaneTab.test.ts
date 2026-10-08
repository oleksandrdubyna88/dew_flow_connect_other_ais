import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_SECURITY, securityLaneFrom, securityWrite, type SecurityLane } from '../securityLane';
import { securityCommandWrite } from '../securityLaneState';
import { SECURITY_FOCUS_FRESH_MS } from '../securityLaneScript';
import { SECURITY_SEED } from '../securityLane.generated';
import type { SecurityTextState } from '../securityPromptFiles';
import type { PanelState } from '../panelView';
import { DEFAULT_VENDORS } from '../vendors';
import { type Control, type Page, panelState, runPanel, withoutSeq } from './panelPageHarness';
import { paneIn } from './panelPages';

// The Security lane tab as drawn (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 3): every test RUNS the page.

const CURRENT_SERVER = { kind: 'known', version: '0.43.0', remembered: false, updateOffered: false } as const;
const OLD_SERVER = { ...CURRENT_SERVER, version: '0.40.3' } as const;
const DIR = 'C:/data/prompts';
const VENDOR = DEFAULT_VENDORS.find(v => v.enabled)!.id;

/**
 * The shipped reviewers, each ticked Security lane on Models: the page a person sees (E5.1 step 3) offers a pair the rows
 * ticked for the lane (E4.2), where the old page offered every reviewer.
 */
const TICKED = DEFAULT_VENDORS.map(v => ({ ...v, uses: ['security' as const] }));

function tab(lane: SecurityLane = DEFAULT_SECURITY, text: Record<string, SecurityTextState> = {}, saved?: unknown, server: PanelState['server'] = CURRENT_SERVER): Page {
  return runPanel(panelState('securityLane', {
    vendors: TICKED, settings: { ...DEFAULTS, securityLane: lane }, server, securityPromptText: text, securityPromptDir: DIR,
  }), { saved });
}

/** The Security lane pane, as the Settings page drew it — the new page's Security lane (E5.1 step 3). */
function pane(page: Page): string {
  return paneIn(page.html, 'security');
}

/** One prompt's card, from its name to the end of its fieldset. */
function card(page: Page, id: string): string {
  const html = pane(page);
  const name = html.indexOf(`<span class="seclane-name">${id}</span>`);
  assert.ok(name >= 0, `${id} has no card`);
  const start = html.lastIndexOf('<fieldset', name);
  return html.slice(start, html.indexOf('</fieldset>', name));
}

const control = (page: Page, field: string): Control => {
  const found = page.controls.find(c => c.dataset['securityField'] === field);
  assert.ok(found, `the tab has no control for ${field}`);
  return found;
};
const button = (page: Page, command: string, id?: string): Control => {
  const found = page.commands.find(c => c.dataset['command'] === command && (id === undefined || c.dataset['id'] === id));
  assert.ok(found, `the tab has no ${command} button${id === undefined ? '' : ' for ' + id}`);
  return found;
};

test('the tag legend is a list the page lays out in two columns, every signal on its own line', () => {
  const html = pane(tab());
  const list = html.slice(html.indexOf('<ul class="seclane-tags">'), html.indexOf('</ul>', html.indexOf('<ul class="seclane-tags">')));
  assert.equal((list.match(/<li>/g) ?? []).length, SECURITY_SEED.signals.length);
  assert.ok(list.includes('<li><code>entry-point</code> Entry points and middleware (focus only)</li>'));
});

test('each card names its prompt large, says its state in words and in colour, and general comes first', () => {
  const lane = securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }, { id: 'redteam-sql', triggers: ['sql', 'xss'] }] });
  const page = tab(lane, { 'redteam-authz': 'written', 'redteam-mine': 'written' });
  const html = pane(page);
  const general = html.indexOf('seclane-name">redteam-general<');
  assert.ok(general >= 0 && general < html.indexOf('seclane-name">redteam-authz<'), 'general is not first');
  assert.ok(html.indexOf('seclane-name">redteam-xss<') < html.indexOf('seclane-name">redteam-mine<'), 'a custom prompt comes after the shipped ones');
  assert.match(card(page, 'redteam-xss'), /seclane-prompt--shipped[\s\S]*seclane-badge">default</);
  assert.match(card(page, 'redteam-authz'), /seclane-prompt--edited[\s\S]*seclane-badge">edited</, 'an override with text is an edit');
  assert.match(card(page, 'redteam-sql'), /seclane-prompt--edited/, 'changed conditions are an edit');
  assert.match(card(page, 'redteam-mine'), /seclane-prompt--custom[\s\S]*seclane-badge">custom</);
});

test('Restore default is on an edited card only, Remove custom prompt on a custom card only, and both are buttons', () => {
  const lane = securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }] });
  const page = tab(lane, { 'redteam-authz': 'written', 'redteam-mine': 'written' });
  assert.deepEqual(page.commands.filter(c => c.dataset['command'] === 'restoreSecurityPrompt').map(c => c.dataset['id']), ['redteam-authz']);
  assert.deepEqual(page.commands.filter(c => c.dataset['command'] === 'removeSecurityPrompt').map(c => c.dataset['id']), ['redteam-mine']);
  assert.equal(page.controls.some(c => c.dataset['securityField'] === 'prompt:redteam-mine:remove'), false, 'the old checkbox is gone');
  button(page, 'restoreSecurityPrompt', 'redteam-authz').fire('click');
  assert.deepEqual(withoutSeq(page.posted.at(-1)!), { type: 'command', command: 'restoreSecurityPrompt', id: 'redteam-authz' });
});

test('a custom prompt with no usable text names the file to write it in, and an unusable override says why', () => {
  const lane = securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }] });
  const page = tab(lane, { 'redteam-sql': 'placeholder' });
  assert.match(card(page, 'redteam-mine'), /No usable prompt text — write it in C:\/data\/prompts\/redteam-mine\.md/);
  assert.match(card(page, 'redteam-sql'), /redteam-sql\.md holds only the operator placeholder, so the lane reports its pairs as unable to run/);
  assert.doesNotMatch(card(page, 'redteam-xss'), /unable to run/);
});

test('conditions sit in a fold drawn closed, in two columns, with a one-line summary and the warning outside it', () => {
  const lane = securityWrite(DEFAULT_SECURITY, 'prompt:redteam-authz:triggers', '', DEFAULT_VENDORS);
  const page = tab(lane);
  assert.ok(page.folds.length >= 12, 'every conditional preset has a fold');
  assert.ok(page.folds.every(f => !f.open), 'a fold drawn open');
  const authz = card(page, 'redteam-authz');
  const warning = authz.indexOf('has no triggers');
  assert.ok(warning >= 0 && warning < authz.indexOf('<details'), 'the warning is missing, or inside the fold');
  assert.match(card(page, 'redteam-sql'), /<summary>Runs on: SQL and raw queries; focus: SQL and raw queries · Entry points and middleware<\/summary>/);
  assert.match(card(page, 'redteam-sql'), /class="seclane-conditions-grid">\s*<div><h4>Run when code matches<\/h4>[\s\S]*<div><h4>Prioritize source<\/h4>/);
  assert.equal(page.folds.some(f => f.dataset['seclaneOpen'] === 'redteam-general'), false, 'general has no conditions to fold');
});

test('a fold the person opened is open again after the repaint, and a fresh page has every fold closed', () => {
  const first = tab();
  const sql = first.folds.find(f => f.dataset['seclaneOpen'] === 'redteam-sql')!;
  sql.open = true;
  sql.fire('toggle');
  const again = tab(DEFAULT_SECURITY, {}, first.saved());
  assert.deepEqual(again.folds.filter(f => f.open).map(f => f.dataset['seclaneOpen']), ['redteam-sql']);
  const closedAgain = again.folds.find(f => f.dataset['seclaneOpen'] === 'redteam-sql')!;
  closedAgain.open = false;
  closedAgain.fire('toggle');
  assert.deepEqual(tab(DEFAULT_SECURITY, {}, again.saved()).folds.filter(f => f.open), []);
});

test('the box a person ticked has the focus again after the repaint the tick causes', async () => {
  // The change handler releases focus to the host, so the repaint carries none: before this, every tick put the person
  // back at the top of a tab of thirteen cards.
  const first = tab();
  const box = control(first, 'trigger:redteam-sql:xss');
  box.checked = true;
  box.fire('change');
  const again = tab(DEFAULT_SECURITY, {}, first.saved());
  await Promise.resolve();
  assert.equal(control(again, 'trigger:redteam-sql:xss').focused, true);
  assert.equal(again.controls.filter(c => c.focused).length, 1);
});

test('after Remove pair, the focus lands on + Add pair rather than at the top of the page', async () => {
  const lane = securityWrite(DEFAULT_SECURITY, 'addRun', true, DEFAULT_VENDORS);
  const first = tab(lane);
  const remove = button(first, 'removeSecurityRun');
  assert.deepEqual(JSON.parse(remove.dataset['id']!), [0, VENDOR, 'redteam-authz'], 'the button names its pair by identity');
  remove.fire('click');
  const again = tab(DEFAULT_SECURITY, {}, first.saved());
  await Promise.resolve();
  assert.equal(button(again, 'addSecurityRun').focused, true);
});

test('+ New custom prompt… on a pair asks for a prompt for THAT pair, and the select goes back to what it showed', () => {
  const lane = securityWrite(DEFAULT_SECURITY, 'addRun', true, DEFAULT_VENDORS);
  const page = tab(lane);
  const picker = control(page, 'run:0:prompt');
  assert.equal(picker.options.at(-1)?.value, '__newSecurityPrompt__');
  picker.value = '__newSecurityPrompt__';
  picker.fire('change');
  // The change handler then releases focus to the host, so the command is the post before that, not the last one.
  const asked = page.posted.filter(m => m['type'] === 'command');
  assert.deepEqual(asked.map(withoutSeq), [{ type: 'command', command: 'newSecurityPrompt', id: JSON.stringify([0, VENDOR, 'redteam-authz']) }]);
  assert.equal(picker.value, 'redteam-authz');
  assert.equal(page.posted.some(m => m['securityField'] === 'run:0:prompt'), false, 'the sentinel was written as a prompt');
});

test('typing in the pair\'s prompt search never hides + New custom prompt…', () => {
  // Fifteen options are where the search box starts: thirteen shipped prompts, two of the person's, and the sentinel.
  const lane = securityWrite(securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }, { id: 'redteam-also', triggers: [], focus: [] }] }),
    'addRun', true, DEFAULT_VENDORS);
  const page = tab(lane);
  const picker = control(page, 'run:0:prompt');
  const box = page.inserted().find(one => one.before === picker)?.node;
  assert.ok(box, 'fourteen prompts and the sentinel get a search box');
  box.value = 'sql';
  box.fire('input');
  assert.ok(picker.options.some(o => o.value === '__newSecurityPrompt__'), 'the search filtered the sentinel out');
});

test('the caps are visible text, and + Add pair is disabled when it could add nothing', () => {
  const full: SecurityLane = { ...DEFAULT_SECURITY, runs: Array.from({ length: 16 }, (_, i) => ({ vendor: 'reviewer-' + i, prompt: 'redteam-sql' })) };
  const page = tab(full);
  assert.equal(button(page, 'addSecurityRun').disabled, true);
  assert.match(pane(page), /Pairs: 16 of 16 — the most a lane holds/);
  assert.equal(button(tab(), 'addSecurityRun').disabled, false);
  assert.match(pane(tab()), /Prompts: 13 of 32 \(13 shipped\)/);
  assert.equal(button(tab(), 'newSecurityPrompt').disabled, false);
});

test('a prompt id carrying markup is escaped everywhere it is drawn, and its fold still reopens by comparison', () => {
  const hostile = 'redteam-x"><script>alert(1)</script>';
  const lane = securityLaneFrom({ prompts: [{ id: hostile, triggers: [], focus: [] }], runs: [{ vendor: VENDOR, prompt: hostile }] });
  const page = tab(lane);
  assert.doesNotMatch(pane(page), /<script>alert/);
  const fold = page.folds.find(f => f.dataset['seclaneOpen'] === hostile);
  assert.ok(fold, 'the fold carries the id as written, for the page to COMPARE — it never becomes a selector');
  fold.open = true;
  fold.fire('toggle');
  assert.equal(tab(lane, {}, page.saved()).folds.find(f => f.dataset['seclaneOpen'] === hostile)?.open, true);
  assert.ok(page.commands.some(c => c.dataset['command'] === 'removeSecurityRun' && JSON.parse(c.dataset['id']!)[2] === hostile),
    'the identity triple round-trips through the attribute');
});

test('on an older server\'s tab, no new button can switch the lane on', () => {
  const page = tab(securityLaneFrom({ runs: [{ vendor: VENDOR, prompt: 'redteam-sql' }] }), {}, undefined, OLD_SERVER);
  let written = securityLaneFrom({ runs: [{ vendor: VENDOR, prompt: 'redteam-sql' }] });
  for (const one of page.commands.filter(c => c.dataset['command'] !== undefined && !c.disabled)) {
    const write = securityCommandWrite(written, one.dataset['command']!, one.dataset['id']);
    if (!('refusal' in write)) written = securityWrite(written, write.field, write.value, DEFAULT_VENDORS);
  }
  assert.equal(written.enabled, false);
  assert.equal(envBlock({ ...DEFAULTS, securityLane: written }, DEFAULT_VENDORS, OLD_SERVER.version)['COAI_SECURITY_LANE'], undefined);
});

test('a repaint held while the person typed in a fold\'s tag field puts the caret back in that field', () => {
  // The host names the focused control by an id whose last part is the Security field — `prompt:redteam-sql:triggers`,
  // with colons. The pattern that guards that id used to refuse a colon, so the caret was dropped on every such repaint.
  const held = { id: 'securityLane||||prompt:redteam-sql:triggers', start: 1, end: 3 };
  const page = runPanel(panelState('securityLane', { vendors: TICKED, server: CURRENT_SERVER }, held));
  const field = control(page, 'prompt:redteam-sql:triggers');
  assert.equal(field.focused, true);
  assert.deepEqual(field.selection, [1, 3]);
});

test('a focus note nobody acted on goes stale: a later, unrelated repaint does not pull the focus there', async () => {
  // A cancelled modal writes nothing, so no repaint consumes the note; the next repaint may come from anything.
  const first = tab(securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }] }));
  button(first, 'removeSecurityPrompt', 'redteam-mine').fire('click');
  const note = first.saved() as { seclaneFocus: { at: number } };
  const stale = { ...note, seclaneFocus: { ...note.seclaneFocus, at: note.seclaneFocus.at - SECURITY_FOCUS_FRESH_MS - 1 } };
  const later = tab(DEFAULT_SECURITY, {}, stale);
  await Promise.resolve();
  assert.equal([...later.commands, ...later.controls].some(c => c.focused), false);
});

test('a shipped card whose override holds anything unusable offers Restore default, still green, still saying why', () => {
  const page = tab(DEFAULT_SECURITY, { 'redteam-sql': 'oversized', 'redteam-xss': 'placeholder' });
  assert.deepEqual(page.commands.filter(c => c.dataset['command'] === 'restoreSecurityPrompt').map(c => c.dataset['id']).sort(), ['redteam-sql', 'redteam-xss']);
  assert.match(card(page, 'redteam-sql'), /seclane-prompt--shipped[\s\S]*is over 64 KiB/);
});

test('an id that cannot name a file says so, offers no dead Edit button, and can still be removed', () => {
  const lane = securityLaneFrom({ prompts: [{ id: 'redteam-Bad', triggers: [], focus: [] }] });
  const page = tab(lane);
  assert.match(card(page, 'redteam-Bad'), /redteam-Bad cannot name a prompt file/);
  assert.equal(page.commands.some(c => c.dataset['command'] === 'editSecurityPrompt' && c.dataset['id'] === 'redteam-Bad'), false);
  const remove = securityCommandWrite(lane, 'removeSecurityPrompt', 'redteam-Bad');
  assert.deepEqual(remove, { field: 'prompt:redteam-Bad:remove', value: true });
});
