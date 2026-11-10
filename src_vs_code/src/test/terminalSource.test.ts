import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatEntry, ChatPanels } from '../chatPanels';
import { sameSource, sourceOfFile, sourceOfTerminal, type ConversationSource } from '../chatStore';
import { Thread, threads } from '../chatThread';
import { CLAUDE_PANEL_VIEW_TYPE, type KnownPanel, type TabSnapshot, sourceSession } from '../sessionKey';
import { type LiveTerminal, oneLiveNamed, tabPanels, tabSourced, terminalLabel, terminalMatch } from '../terminalSource';

/**
 * The terminal as a conversation source: its title, its live key, the ONE live terminal of a name,
 * and the filter that keeps a Claude tab from taking a terminal's conversation (E1.S2).
 *
 * <p>Pure, like the module: no `vscode`. A `Terminal` is any object here, which is all the rules
 * ask of it — the key is the object, never its name.</p>
 */

let made = 0;
/** A fresh object every call: identity is the point, so two terminals must never accidentally be one. It carries a `name`, as a real `Terminal` does. */
const terminalKey = (): object => {
  made += 1;

  return { terminal: made, name: 'a terminal' };
};

function tab(label: string): TabSnapshot {
  made += 1;

  return { key: { tab: made }, label, viewType: CLAUDE_PANEL_VIEW_TYPE };
}

const live = (key: object, name: string, exited = false): LiveTerminal => ({ key, name, exited });

test('the title drops “Creds: ” from the start only, and never leaves nothing', () => {
  assert.equal(terminalLabel('Creds: ai1-dev3 / small-tasks'), 'ai1-dev3 / small-tasks');
  assert.equal(terminalLabel('Creds: before deploy'), 'before deploy');
  for (const unchanged of ['Creds run: x', 'Creds VPN: x', 'my Creds: x', 'creds: x', 'Creds: ', 'bash', '']) {
    assert.equal(terminalLabel(unchanged), unchanged, `the name ${JSON.stringify(unchanged)} was cut`);
  }
});

test('the same terminal is its own conversation; another terminal of the same name is a new one', () => {
  const [a, b] = [terminalKey(), terminalKey()];
  const name = 'Creds: h / s';

  assert.deepEqual(terminalMatch([{ key: a, label: 'h / s' }], a, name), { kind: 'existing', key: a, label: 'h / s' });
  assert.deepEqual(
    terminalMatch([{ key: a, label: 'h / s' }], b, name),
    { kind: 'new', key: b, label: 'h / s' },
    'a second terminal of the name got the first one’s conversation',
  );
  assert.deepEqual(terminalMatch([{ key: {}, label: 'h / s' }], b, name), { kind: 'new', key: b, label: 'h / s' });
  // The key decides, not the name: the same terminal after a rename is the same conversation.
  assert.deepEqual(terminalMatch([{ key: a, label: 'h / s' }], a, 'other'), { kind: 'existing', key: a, label: 'other' });
  for (const answer of [terminalMatch([], a, name), terminalMatch([{ key: a, label: 'x' }], a, name)]) {
    assert.notEqual(answer.kind, 'rekey', 'a terminal conversation was handed to another key');
  }
});

test('two live terminals of one name are two conversations with equal sources', () => {
  const [a, b] = [terminalKey(), terminalKey()];
  const name = 'bash';

  assert.deepEqual(terminalMatch([], a, name), { kind: 'new', key: a, label: 'bash' });
  assert.deepEqual(terminalMatch([{ key: a, label: 'bash' }], b, name), { kind: 'new', key: b, label: 'bash' });
  assert.equal(sameSource(sourceOfTerminal(name), sourceOfTerminal(name)), true);
});

test('one live terminal of a name is found; none and several are said, and never picked', () => {
  const [a, b] = [terminalKey(), terminalKey()];

  assert.deepEqual(oneLiveNamed([], 'x'), { kind: 'none' });
  assert.deepEqual(oneLiveNamed([live(a, 'x')], 'x'), { kind: 'one', key: a }, 'the one terminal of the name was not found');
  assert.deepEqual(oneLiveNamed([live(a, 'x'), live(b, 'x')], 'x'), { kind: 'several', count: 2 });
  assert.deepEqual(oneLiveNamed([live(a, 'x'), live(b, 'x'), live(terminalKey(), 'x')], 'x'), { kind: 'several', count: 3 });
  assert.deepEqual(oneLiveNamed([live(a, 'x', true), live(b, 'x')], 'x'), { kind: 'one', key: b }, 'an exited terminal was counted');
  assert.deepEqual(oneLiveNamed([live(a, 'x', true)], 'x'), { kind: 'none' });
  assert.deepEqual(oneLiveNamed([live(a, 'X')], 'x'), { kind: 'none' }, 'a name was compared without its case');
});

test('without the filter a Claude tab of the same name takes the terminal’s conversation, and that is the point', () => {
  // The defect itself, pinned: `sourceSession` reads a known panel whose key is not one of the tabs as
  // "its tab has gone" and hands it to a Claude tab of the same label. A terminal is never a tab.
  const claude = tab('main');
  const terminal = terminalKey();
  const known: readonly KnownPanel[] = [{ key: terminal, label: 'main' }];

  assert.deepEqual(sourceSession(claude, [claude], known), { kind: 'rekey', from: terminal, key: claude.key, label: 'main' });
});

test('a Claude tab named like a terminal conversation gets a NEW conversation, never that one', () => {
  const claude = tab('main');
  const terminal = terminalKey();
  const restored = {};
  const known: readonly KnownPanel[] = [{ key: terminal, label: 'main' }];
  const sourceOf = (key: object): ConversationSource | undefined => (key === terminal ? sourceOfTerminal('main') : undefined);

  assert.deepEqual(
    sourceSession(claude, [claude], tabSourced(known, sourceOf)),
    { kind: 'new', key: claude.key, label: 'main' },
    'a Claude tab called “main” took the terminal’s conversation',
  );
  // The restored form: the panel's key is a plain object and only its thread says where it came from.
  assert.deepEqual(
    sourceSession(claude, [claude], tabSourced([{ key: restored, label: 'main' }], (key) => (key === restored ? sourceOfTerminal('main') : undefined))),
    { kind: 'new', key: claude.key, label: 'main' },
    'a Claude tab took a RESTORED terminal conversation',
  );
});

test('the filter takes out terminal conversations and nothing else — judged by the thread, read from the real registry', () => {
  // (a) the pure filter.
  const keys = { claude: {}, file: {}, none: {}, orphan: {}, noThread: {}, terminal: {} };
  const known: readonly KnownPanel[] = Object.entries(keys).map(([label, key]) => ({ key, label }));
  const sources = new Map<object, ConversationSource>([
    [keys.claude, { kind: 'claude', sessionId: '9f1c-uuid' }],
    [keys.file, sourceOfFile('file:///a/README.md')],
    [keys.none, { kind: 'none' }],
    [keys.orphan, { kind: 'none' }],
    [keys.terminal, sourceOfTerminal('terminal')],
  ]);
  const kept = tabSourced(known, (key) => sources.get(key)).map((panel) => panel.label);

  assert.deepEqual(kept, ['claude', 'file', 'none', 'orphan', 'noThread'], 'the filter did not take out exactly the terminal conversations');

  // An orphaned Claude panel `main` still re-keys to the Claude tab through the filter, and with BOTH a
  // terminal panel and that orphan the answer is the orphan.
  const claude = tab('main');
  const terminalPanel = { key: terminalKey(), label: 'main' };
  const orphan = { key: {}, label: 'main' };
  const byKey = (key: object): ConversationSource | undefined => (key === terminalPanel.key ? sourceOfTerminal('main') : { kind: 'none' });

  assert.deepEqual(
    sourceSession(claude, [claude], tabSourced([terminalPanel, orphan], byKey)),
    { kind: 'rekey', from: orphan.key, key: claude.key, label: 'main' },
    'the orphaned Claude panel was not the one re-keyed',
  );

  // (b) the registry, for real.
  const panels = new ChatPanels();
  const open = (key: object, label: string, source: ConversationSource | undefined): void => {
    const id = {};
    if (source !== undefined) {
      // A thread built with only the field the filter reads: if it ever reads another, it throws here.
      threads.set(id, { source } as unknown as Thread);
    }
    const entry = (): ChatEntry => ({
      id,
      panel: { reveal() {}, dispose() {}, post() {}, isActive: () => false },
      session: { dispose() {} },
    });
    panels.open(key, label, entry);
  };
  const [liveTerminal, restoredTerminal, plain, withoutThread] = [terminalKey(), {}, {}, {}];
  open(liveTerminal, 'a-terminal', sourceOfTerminal('a-terminal'));
  open(restoredTerminal, 'a-restored-terminal', sourceOfTerminal('a-restored-terminal'));
  open(plain, 'plain', { kind: 'none' });
  open(withoutThread, 'no-thread', undefined);

  assert.deepEqual(
    tabPanels(panels),
    [{ key: plain, label: 'plain' }, { key: withoutThread, label: 'no-thread' }],
    'a terminal conversation was offered to a tab',
  );
});
