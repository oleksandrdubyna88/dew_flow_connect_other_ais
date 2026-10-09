import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';

import { admit } from '../capabilityAdmission';
import { QCONSULT_SINCE, type QuestionRowSetting } from '../qconsultSettings';
import type { RootPlaces } from '../qconsultWrite';
import { DEFAULTS } from '../settingsShape';
import { type Control, type Page, click, panelState, runPanel, withoutSeq } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * The Question consultant tab, RUN (todo/PLAN_question_consultant.md, S4 acceptance 2): its own script over its
 * own markup, through the panel harness — what a person can switch on, pick and press, and what the page then
 * posts. No assertion here reads the page's source for a behaviour (`.agents/PROJECT.md`).
 */

const repo = (...parts: string[]): string => path.join(__dirname, '..', '..', '..', ...parts);

function row(id: string, overrides: Partial<QuestionRowSetting> = {}): QuestionRowSetting {
  return {
    id, vendor: 'claude', runtime: 'claude', model: '', baseUrl: '', executablePath: '', key: '',
    prompt: 'question-opinion', enabled: false, ...overrides,
  };
}

function page(rows: readonly QuestionRowSetting[], extra: Parameters<typeof panelState>[1] = {}): Page {
  return runPanel(panelState('questionconsultant', {
    settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, rows } },
    ...extra,
  }));
}

/** One row's control, by the key it writes and the row it belongs to. */
function controlOf(on: Page, setting: string, rowId: string): Control {
  const found = on.controls.find((one) => one.dataset['setting'] === setting && one.dataset['caller'] === rowId);
  assert.ok(found !== undefined, `the page has no ${setting} for row ${rowId}`);

  return found;
}

test('a seventh row cannot be switched on: its switch is disabled, saying six are on — and the six can be switched off', () => {
  const six = ['a', 'b', 'c', 'd', 'e', 'f'].map((id) => row(id, { enabled: true }));
  const on = page([...six, row('g')]);

  const seventh = controlOf(on, 'qconsultRowEnabled', 'g');
  assert.equal(seventh.disabled, true, 'a person could tick a seventh active row');
  assert.equal(seventh.checked, false);
  assert.ok(six.every((one) => !controlOf(on, 'qconsultRowEnabled', one.id).disabled), 'a row that is on must stay switchable off');

  // With one of the six off, the seventh may run.
  const freed = page([...six.slice(0, 5), row('f'), row('g')]);
  assert.equal(controlOf(freed, 'qconsultRowEnabled', 'g').disabled, false);
});

test('switching a row on posts the write the host routes by row', () => {
  const on = page([row('sonnet-1')]);
  const toggle = controlOf(on, 'qconsultRowEnabled', 'sonnet-1');

  toggle.checked = true;
  toggle.fire('change');

  const written = on.posted.filter((one) => one['type'] === 'setting').at(-1);
  assert.deepEqual(
    { key: written?.['key'], value: written?.['value'], caller: written?.['caller'] },
    { key: 'qconsultRowEnabled', value: true, caller: 'sonnet-1' },
  );
});

test('a row without a prompt cannot be switched on, and its picker offers no empty choice a person can pick', () => {
  const on = page([row('bare', { prompt: '' })]);

  assert.equal(controlOf(on, 'qconsultRowEnabled', 'bare').disabled, true);
  const prompt = controlOf(on, 'qconsultRowPrompt', 'bare');
  assert.ok(prompt.options.filter((one) => one.value === '').every((one) => one.disabled), 'an empty prompt is offered as a choice');
  assert.ok(prompt.options.some((one) => one.value !== '' && !one.disabled), 'and a real prompt is there to pick');
});

/** The capability vectors both halves answer — read from the file, so a pair added there is a pair checked here. */
const VECTORS = (JSON.parse(fs.readFileSync(repo('shared', 'capability-matrix-vectors.json'), 'utf8')) as {
  vectors: { runtime: string; capability: string; admitted: boolean; flag: string }[];
}).vectors;

/** The vendor a row of each runtime is, from the catalogue the row's picker offers. */
const VENDOR_OF: Readonly<Record<string, string>> = { claude: 'claude', codex: 'codex', antigravity: 'antigravity', local: 'local', api: 'api' };

/** The shipped prompt of each capability, read from the seed the server embeds. */
const PROMPT_OF: Readonly<Record<string, string>> = Object.fromEntries(
  (JSON.parse(fs.readFileSync(repo('shared', 'question-prompts.json'), 'utf8')) as { prompts: { id: string; capability: string }[] }).prompts
    .map((p) => [p.capability, p.id]),
);

test('every pair the vectors refuse is DISABLED in that runtime\'s picker, with the table\'s reason; every pair they admit is not (A3)', () => {
  const inTable = VECTORS.filter((v) => VENDOR_OF[v.runtime] !== undefined && PROMPT_OF[v.capability] !== undefined);
  assert.ok(inTable.some((v) => v.runtime === 'antigravity' && v.capability === 'web' && !v.admitted), 'the vectors no longer hold agy + web');
  assert.ok(inTable.some((v) => v.runtime === 'api' && v.capability === 'disk' && !v.admitted), 'the vectors no longer hold api + disk');

  for (const vector of inTable) {
    const id = `${vector.runtime}-row`;
    const on = page([row(id, { vendor: VENDOR_OF[vector.runtime]!, runtime: vector.runtime, prompt: 'question-opinion' })]);
    const offered = controlOf(on, 'qconsultRowPrompt', id).options.find((one) => one.value === PROMPT_OF[vector.capability]);

    assert.ok(offered !== undefined, `${vector.runtime} × ${vector.capability}: the prompt is not listed at all`);
    assert.equal(offered.disabled, !vector.admitted, `${vector.runtime} × ${vector.capability}: disabled must be ${!vector.admitted}`);
    assert.equal(offered.title, vector.admitted ? '' : admit(vector.runtime, vector.capability).reason);
  }
});

test('a codex row says it can read this machine, its tick is on and cannot be taken off, and it switches on (D13, revised)', () => {
  const on = page([row('astra-web', { vendor: 'codex', runtime: 'codex', prompt: 'question-web' })]);

  assert.equal(controlOf(on, 'qconsultRowEnabled', 'astra-web').disabled, false, 'the flag is enough: nothing stands between the row and its switch');
  const tick = /<input type="checkbox" id="qconsultRowCanRead-astra-web"([^>]*)>/.exec(on.html)?.[1];
  assert.ok(tick !== undefined, 'the row shows no tick for what it can read');
  assert.match(tick, /\bchecked\b/, 'the tick is shown ON');
  assert.match(tick, /\bdisabled\b/, 'and cannot be taken off');
  assert.doesNotMatch(tick, /data-setting=/, 'it is not a setting: nothing a person does to it is stored');
  assert.match(on.html, /Codex has no setting that limits what it reads/, 'the reason is written beside the tick');
});

test('a confined pair asks for no acknowledgement at all', () => {
  const on = page([row('sonnet-disk', { prompt: 'question-disk' })]);

  assert.equal(on.controls.some((one) => one.dataset['setting'] === 'qconsultRowAcknowledged'), false);
  assert.equal(controlOf(on, 'qconsultRowEnabled', 'sonnet-disk').disabled, false);
});

test('Restore default is pressed on an edited shipped prompt — and the box then shows the shipped words again', () => {
  const shipped = (JSON.parse(fs.readFileSync(repo('shared', 'question-prompts.json'), 'utf8')) as { prompts: { id: string; text: string }[] })
    .prompts.find((p) => p.id === 'question-opinion')!.text;
  const edited = page([], { qconsultPromptOverrides: { 'question-opinion': 'Answer as a pessimist.' } });

  assert.equal(controlOf(edited, 'qconsultPromptText', 'question-opinion').value, '', 'a textarea carries its words between its tags, not in value');
  click(edited, 'qconsultRestorePrompt', 'question-opinion');
  assert.deepEqual(withoutSeq(edited.posted.at(-1)!), { type: 'command', command: 'qconsultRestorePrompt', id: 'question-opinion' });

  // What the host repaints once the override file is gone: the shipped words, read from the seed the server embeds.
  const restored = page([], { qconsultPromptOverrides: {} });
  assert.ok(boxText(restored.html, 'question-opinion') === shipped, 'the box does not show the shipped words after a restore');
  assert.ok(boxText(edited.html, 'question-opinion') === 'Answer as a pessimist.', 'the box does not show the override while there is one');
  assert.equal(restored.commands.some((one) => one.dataset['command'] === 'qconsultRestorePrompt'), false, 'nothing to restore, so no button');
});

/** A textarea's text as the page drew it — the harness reads a control's attributes, and a textarea's value is its body. */
function boxText(html: string, promptId: string): string {
  const open = html.indexOf(`id="qconsultPromptText-${promptId}"`);
  const start = html.indexOf('>', open) + 1;

  return html.slice(start, html.indexOf('</textarea>', start)).replace(/&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, '&');
}

test('typing in a shipped prompt\'s box posts the write the host turns into its override file', () => {
  const on = page([]);
  const box = controlOf(on, 'qconsultPromptText', 'question-web');

  assert.equal(box.dataset['file'], 'question-web.md', 'the box does not say it is a FILE, so the declared-settings test would require a setting');
});

test('the tab carries a banner for a server known to be older than QCONSULT_SINCE, and none for a current or unknown one', () => {
  const older = page([], { server: { kind: 'known', version: '0.40.5', remembered: false, updateOffered: false } });
  const current = page([], { server: { kind: 'known', version: QCONSULT_SINCE, remembered: false, updateOffered: false } });

  assert.ok(older.html.includes(`update it to ${QCONSULT_SINCE}`), 'an older server is not named on the tab');
  assert.ok(!current.html.includes(`update it to ${QCONSULT_SINCE}`));
  assert.ok(!page([]).html.includes(`update it to ${QCONSULT_SINCE}`), 'an unknown version is not an old one');
});

// ---------- a root of the other operating system (operator, 2026-10-09) ----------

const WINDOWS_PLACES: RootPlaces = {
  dataDir: 'C:\\Users\\me\\AppData\\Local\\coai-mcp', profile: 'C:\\Users\\me', systemDirs: ['C:\\Windows'], caseless: true, windows: true, existingHere: [], systemDrive: 'C:',
};

const LINUX_PLACES: RootPlaces = { dataDir: '/home/me/.local/share/coai-mcp', profile: '/home/me', systemDirs: ['/usr/share'], caseless: false, windows: false, existingHere: [], systemDrive: 'C:' };

/** The page RUN with these roots stored and this side's places, read back as the tree it drew. */
function rootsOn(roots: readonly string[], places: RootPlaces): PageNode {
  const on = runPanel(panelState('questionconsultant', {
    settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, roots } },
    qconsultPlaces: places,
  }));

  return pageTree(on.html);
}

/** The line the page drew for one stored root. */
function rootLine(tree: PageNode, root: string): PageNode {
  return tree.one((node) => node.className.split(' ').includes('qconsult-root')
    && node.find((child) => child.tagName === 'CODE' && child.text() === root).length > 0, `line for the root ${root}`);
}

test('in a Windows window a WSL root is named the other side\'s folder — not refused — and this machine\'s root beside it is plain', () => {
  // VS Code settings are shared by the WSL window and the Windows one: the WSL window wrote /home/jinx/git.
  const tree = rootsOn(['/home/jinx/git', 'D:\\rsd\\work'], WINDOWS_PLACES);

  const foreign = rootLine(tree, '/home/jinx/git');
  assert.match(foreign.text(), /the other side's folder/, 'the WSL root is not explained in a Windows window');
  assert.equal(foreign.find((node) => node.className.split(' ').includes('stale')).length, 0, 'the WSL root is drawn as broken, which the server no longer says it is');
  assert.doesNotMatch(rootLine(tree, 'D:\\rsd\\work').text(), /other side/, 'this machine\'s own folder is no other side\'s');
  assert.doesNotMatch(tree.text(), /not asked on this side/, 'one folder of this machine is left, so a disk row still runs here');
});

test('in a Windows window a root-relative folder that EXISTS here (/work) is never called the other side\'s, and a disk row still runs', () => {
  // `/work` is a legal Windows path to a folder on the system drive; the host found it there, so it is this side's.
  const tree = rootsOn(['/work', '/home/jinx/git'], { ...WINDOWS_PLACES, existingHere: ['/work'] });

  assert.doesNotMatch(rootLine(tree, '/work').text(), /other side/, 'an existing folder of this machine is called the other side\'s');
  assert.match(rootLine(tree, '/home/jinx/git').text(), /the other side's folder/, 'the WSL root that is no folder here still is');
  assert.doesNotMatch(tree.text(), /not asked on this side/, '/work is a folder of this machine, so a disk row runs here');
});

test('in a Windows window a root-relative root is judged at its system-drive path: /Windows, the profile and the data folder are refused as the server refuses them', () => {
  // The server qualifies `/Windows` to `C:\Windows` before D14 (c) and refuses it; the page must judge the same path.
  const roots = ['/Windows', '/Users/me', '/Users/me/AppData/Local/coai-mcp/x'];
  const tree = rootsOn(roots, { ...WINDOWS_PLACES, existingHere: roots });

  const stale = (root: string): string => rootLine(tree, root).find((node) => node.className.split(' ').includes('stale')).map((node) => node.text()).join(' ');
  assert.match(stale('/Windows'), /system folder/, 'the system folder spelled from the root of the drive is not refused on the page');
  assert.match(stale('/Users/me'), /profile folder itself/);
  assert.match(stale('/Users/me/AppData/Local/coai-mcp/x'), /inside the data folder/);
});

test('in a WSL window a Windows root is the other side\'s folder, and with no folder of this side a disk row is said to be not asked here', () => {
  const tree = rootsOn(['C:\\Users\\jinx\\git'], LINUX_PLACES);

  assert.match(rootLine(tree, 'C:\\Users\\jinx\\git').text(), /the other side's folder/);
  assert.match(tree.text(), /not asked on this side/, 'every folder is the other side\'s, and the page does not say what that means for a disk row here');
});

test('Add a row, Remove, Add a prompt and Add a folder are buttons the page binds, each posting its command', () => {
  const on = page([row('sonnet-1')]);

  for (const [command, id] of [['qconsultAddRow', ''], ['qconsultRemoveRow', 'sonnet-1'], ['qconsultAddPrompt', ''], ['qconsultAddRoot', '']] as const) {
    click(on, command, id);
    assert.deepEqual(withoutSeq(on.posted.at(-1)!), { type: 'command', command, id });
  }
});
