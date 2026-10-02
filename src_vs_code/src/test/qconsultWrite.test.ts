import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';

import type { QuestionRowSetting } from '../qconsultSettings';
import {
  type RootPlaces,
  CREDENTIAL_DIRECTORIES,
  customPromptEdited,
  promptAdded,
  promptRemoved,
  questionPrompts,
  rootAdded,
  rootRefusal,
  rowAdded,
  rowEdited,
  shippedPromptWrite,
} from '../qconsultWrite';
import { SHIPPED_QUESTION_PROMPTS } from '../questionPrompts.generated';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * The Question consultant's write path (S4): what an edit stores, and what it REFUSES — each refusal one the
 * server also makes, so a hand edit meets the same answer. Pure; the page that draws these is
 * `qconsultSection.test.ts`.
 */

const context = { prompts: questionPrompts([]), vendors: DEFAULT_VENDORS };

function row(id: string, overrides: Partial<QuestionRowSetting> = {}): QuestionRowSetting {
  return {
    id, vendor: 'claude', runtime: 'claude', model: '', baseUrl: '', executablePath: '', key: '',
    prompt: 'question-opinion', enabled: false, acknowledged: false, ...overrides,
  };
}

test('the shipped prompts the panel shows are the seed the server embeds, field for field', () => {
  const seed = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', '..', 'shared', 'question-prompts.json'), 'utf8')) as {
    prompts: { id: string; title: string; capability: string; text: string }[];
  };

  assert.deepEqual(SHIPPED_QUESTION_PROMPTS.map((p) => ({ ...p })), seed.prompts.map((p) => ({ ...p, title: p.title.trim(), text: p.text.trim() })));
});

test('a seventh row is refused on; the sixth is not', () => {
  const five = ['a', 'b', 'c', 'd', 'e'].map((id) => row(id, { enabled: true }));
  const sixth = rowEdited([...five, row('f')], 'f', 'qconsultRowEnabled', true, context);
  assert.ok(sixth !== undefined && sixth.find((r) => r.id === 'f')!.enabled);

  assert.equal(rowEdited([...sixth, row('g')], 'g', 'qconsultRowEnabled', true, context), undefined, 'a seventh active row was stored');
  assert.ok(rowEdited([...sixth, row('g')], 'a', 'qconsultRowEnabled', false, context) !== undefined, 'switching one off is always allowed');
});

test('a row is never stored without a prompt, nor with one its runtime cannot run (A3)', () => {
  assert.equal(rowEdited([row('r')], 'r', 'qconsultRowPrompt', '', context), undefined);
  assert.equal(rowEdited([row('r', { vendor: 'antigravity', runtime: 'antigravity' })], 'r', 'qconsultRowPrompt', 'question-web', context), undefined, 'agy + web');
  assert.equal(rowEdited([row('r', { vendor: 'api', runtime: 'api' })], 'r', 'qconsultRowPrompt', 'question-disk', context), undefined, 'api + disk');
  assert.equal(rowEdited([row('r')], 'r', 'qconsultRowPrompt', 'question-disk', context)?.[0]?.prompt, 'question-disk');
});

test('a flagged row runs only once acknowledged, and taking the tick away switches it off (D13)', () => {
  const codex = row('astra', { vendor: 'codex', runtime: 'codex', prompt: 'question-web' });

  assert.equal(rowEdited([codex], 'astra', 'qconsultRowEnabled', true, context), undefined, 'switched on without the tick');
  const ticked = rowEdited([codex], 'astra', 'qconsultRowAcknowledged', true, context)!;
  assert.equal(ticked[0]!.acknowledged, true, 'the tick writes acknowledged: true into the row');
  const running = rowEdited(ticked, 'astra', 'qconsultRowEnabled', true, context)!;
  assert.equal(running[0]!.enabled, true);
  assert.deepEqual(rowEdited(running, 'astra', 'qconsultRowAcknowledged', false, context)![0], { ...codex, acknowledged: false, enabled: false });
});

test('choosing another vendor or prompt takes the row off and drops the acknowledgement made for the old pair', () => {
  const acknowledged = row('astra', { vendor: 'codex', runtime: 'codex', prompt: 'question-web', acknowledged: true, enabled: true });

  assert.deepEqual(
    rowEdited([acknowledged], 'astra', 'qconsultRowVendor', 'claude', context)![0],
    { ...acknowledged, vendor: 'claude', runtime: 'claude', model: 'haiku', enabled: false, acknowledged: false },
  );
  assert.deepEqual(
    rowEdited([acknowledged], 'astra', 'qconsultRowPrompt', 'question-opinion', context)![0],
    { ...acknowledged, prompt: 'question-opinion', enabled: false, acknowledged: false },
  );
  assert.equal(rowEdited([acknowledged], 'astra', 'qconsultRowVendor', 'no-such-vendor', context), undefined);
});

test('a new row is added off, with a prompt its runtime admits, under an id no row holds', () => {
  const rows = rowAdded([row('claude-1')], questionPrompts([]));

  assert.equal(rows.length, 2);
  assert.deepEqual({ id: rows[1]!.id, enabled: rows[1]!.enabled }, { id: 'claude-2', enabled: false });
  assert.ok(rows[1]!.prompt.length > 0);
});

test('an emptied shipped-prompt box, or one holding exactly the shipped words, removes the override', () => {
  const shipped = SHIPPED_QUESTION_PROMPTS.find((p) => p.id === 'question-web')!.text;

  assert.deepEqual(shippedPromptWrite('question-web', '  '), { kind: 'remove' });
  assert.deepEqual(shippedPromptWrite('question-web', `${shipped}\n`), { kind: 'remove' });
  assert.deepEqual(shippedPromptWrite('question-web', 'Search the vendor docs only.'), { kind: 'write', text: 'Search the vendor docs only.' });
});

test('a prompt of your own is added under an id from its title — refused for no id, a taken id or an unknown capability', () => {
  assert.deepEqual(promptAdded([], 'The Docs', 'web').prompts.map((p) => [p.id, p.capability]), [['the-docs', 'web']]);
  assert.match(promptAdded([], '!!!', 'web').refusal, /needs a title/);
  assert.match(promptAdded([], 'Question web', 'web').refusal, /already a prompt/, 'a shipped id is taken');
  assert.match(promptAdded([], 'Shell', 'shell').refusal, /not a capability/);
  assert.equal(customPromptEdited([{ id: 'x', title: 'X', capability: 'none', text: 'a' }], 'x', '  '), undefined, 'a prompt that says nothing');
});

test('a prompt a row still runs cannot be removed', () => {
  const custom = [{ id: 'the-docs', title: 'The docs', capability: 'web', text: 'Read them.' }];

  assert.match(promptRemoved(custom, [row('r', { prompt: 'the-docs' })], 'the-docs').refusal, /the prompt of r/);
  assert.deepEqual(promptRemoved(custom, [], 'the-docs').prompts, []);
});

const windows: RootPlaces = {
  dataDir: 'C:\\Users\\me\\AppData\\Local\\coai-mcp',
  profile: 'C:\\Users\\me',
  systemDirs: ['C:\\Windows', 'C:\\Program Files', 'C:\\ProgramData'],
  caseless: true,
};

test('D14 (c): a drive root, the profile itself, a system folder, the data folder and a relative path are refused by name', () => {
  assert.match(rootRefusal('C:\\', windows), /drive root/);
  assert.match(rootRefusal('D:', windows), /drive root/);
  assert.match(rootRefusal('/', { ...windows, caseless: false }), /drive root/);
  assert.match(rootRefusal('c:\\users\\ME\\', windows), /profile folder itself/, 'Windows compares without case');
  assert.match(rootRefusal('C:\\Program Files\\Git', windows), /system folder/);
  assert.match(rootRefusal('C:\\Users\\me\\AppData\\Local\\coai-mcp\\escalations', windows), /inside the data folder/);
  assert.match(rootRefusal('projects', windows), /not an absolute path/);
  assert.equal(rootRefusal('C:\\Users\\me\\projects', windows), '', 'a folder UNDER the profile is the ordinary case');
  assert.equal(rootRefusal('D:\\rsd', windows), '');
});

test('a root added twice in two spellings is one root', () => {
  const once = rootAdded([], 'D:\\rsd\\', windows);
  assert.deepEqual(once, { roots: ['D:\\rsd'], refusal: '' });
  assert.deepEqual(rootAdded(once.roots, 'd:/rsd', windows).roots, ['D:\\rsd']);
  assert.match(rootAdded([], 'C:\\', windows).refusal, /drive root/);
});

// ---------- S4b item 2: ancestors, credential folders, links ----------

test('a folder that CONTAINS the profile, the data folder or a system folder is refused — reading it reads them', () => {
  // Forward slashes, which every check reads as the backslashes the fixture's places carry.
  const nested: RootPlaces = { ...windows, systemDirs: ['D:/tools/sys'] };

  assert.match(rootRefusal('C:/Users', windows), /contains your profile folder/);
  assert.match(rootRefusal('c:/users/ME/AppData', windows), /contains the data folder/, 'Windows compares without case');
  assert.match(rootRefusal('D:/tools', nested), /contains a system folder/);
  assert.equal(rootRefusal('D:/rsd', nested), '', 'a sibling of the places is the ordinary case');
});

test('a credential folder, anything inside one, and a folder holding one are refused — inside the profile or not', () => {
  for (const credentials of CREDENTIAL_DIRECTORIES) {
    const at = `C:/Users/me/${credentials}`;
    assert.match(rootRefusal(at, windows), /credential folder/, at);
    assert.match(rootRefusal(`${at}/nested`, windows), /credential folder/, `${at}/nested`);
  }
  assert.match(rootRefusal('D:/backup/.ssh', windows), /credential folder/, 'a .ssh copied elsewhere is still keys');
  assert.match(rootRefusal('C:/Users/me/.config', windows), /contains a credential folder/, 'the profile\'s .config holds gcloud\'s');
});

test('a folder reached through a link is judged at its target, and stored as it was picked', () => {
  assert.match(rootAdded([], 'D:/links/home', windows, 'C:/Users/me').refusal, /profile folder itself/);
  assert.match(rootAdded([], 'D:/links/keys', windows, 'C:/Users/me/.ssh').refusal, /credential folder/);
  assert.deepEqual(rootAdded([], 'D:/links/work', windows, 'E:/work'), { roots: ['D:/links/work'], refusal: '' });
});

test("the credential folders are the server's own list — read out of the C#, never retyped", () => {
  // reads-another-program: a POSITIVE assertion — the list must be found in QuestionConsultSettings.cs in this
  // shape (assert.ok below), so a moved or reshaped list turns this red instead of comparing two empty lists.
  const cs = fs.readFileSync(path.join(__dirname, '..', '..', '..', 'src_mcp', 'src', 'Server', 'QuestionConsult', 'QuestionConsultSettings.cs'), 'utf8');
  const listed = /CredentialDirectories \{ get; \} = \[([^\]]*)\];/.exec(cs);
  assert.ok(listed, 'CredentialDirectories is not in QuestionConsultSettings.cs in the shape this test reads');

  assert.deepEqual([...(listed[1] ?? '').matchAll(/"([^"]+)"/g)].map((m) => m[1]), [...CREDENTIAL_DIRECTORIES]);
});
