import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { type PanelState } from '../panelView';
import { type PathFamily } from '../pathFamily';
import { parseProviderNotes } from '../providers';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * The Models card of a row whose CLI path is the OTHER side's (todo/PLAN_paths_per_side.md E1.5): VS Code shares the row
 * between a WSL window and a Windows window, so a WSL window's `/usr/local/bin/codex` is on the Windows page too. The card
 * says it quietly — "the other side's CLI … this Windows side skips it and looks the CLI up on PATH" — and nothing on it
 * calls the row broken because of that path. The page is RUN (`runPanel`) and read back as the tree it drew.
 */

const row = (id: string, executablePath: string): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id, runtime: 'codex', model: 'gpt-6.1-sol', enabled: true, executablePath }])[0]!;

function modelsPage(vendors: readonly Vendor[], family: PathFamily): PageNode {
  const state: PanelState = {
    ...panelState('reviewers'),
    vendors,
    hostFamily: family,
    server: { kind: 'known', version: '0.50.0', remembered: false, updateOffered: false },
    // What a server that skips the other side's path answers: it looked the CLI up on PATH and found it.
    providers: {
      reported: Object.fromEntries(vendors.map((v) => [v.id, { provider: v.id, auth: 'own auth', note: 'the CLI\'s own sign-in is used' }])),
      asked: true,
      answered: true,
      notes: parseProviderNotes('{}'),
    },
  };
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });

  return pageTree(page.html);
}

/** The card that holds this row's CLI-path box. */
function cardOf(tree: PageNode, id: string): PageNode {
  const isBox = (node: PageNode): boolean => node.tagName === 'INPUT' && node.dataset['setting'] === 'executablePath' && node.dataset['vendor'] === id;

  return tree.one((node) => node.tagName === 'ARTICLE' && node.find(isBox).length === 1, `the card holding ${id}'s CLI-path box`);
}

const notes = (card: PageNode): readonly PageNode[] => card.find((node) => node.className.split(' ').includes('path-other-side'));

test('on a Windows page a POSIX CLI path says it is the other side\'s CLI, quietly, and nothing on the card blames it', () => {
  const tree = modelsPage([row('wsl-codex', '/usr/local/bin/codex'), row('win-codex', 'C:\\tools\\codex.exe')], 'windows');
  const theirs = cardOf(tree, 'wsl-codex');
  const [note] = notes(theirs);

  assert.ok(note !== undefined, 'the card of the WSL path says nothing about it');
  assert.ok(note.className.split(' ').includes('hint'), `drawn as ${note.className}, not the quiet hint`);
  assert.match(note.text(), /^a Linux, WSL or macOS path — the other side's CLI: the server on that side runs it, and this Windows side skips it and looks the CLI up on PATH$/);
  assert.equal(theirs.find((node) => node.className.split(' ').includes('cannot-run')).length, 0, 'a "cannot review" badge on the card');
  assert.equal(theirs.find((node) => node.className.split(' ').includes('stale')).length, 0, 'the path is drawn as a refusal');
  assert.equal(notes(cardOf(tree, 'win-codex')).length, 0, 'this side\'s own path is called the other side\'s');
});

test('on a WSL page it is the Windows path that is the other side\'s', () => {
  const tree = modelsPage([row('wsl-codex', '/usr/local/bin/codex'), row('win-codex', 'C:\\tools\\codex.exe')], 'posix');

  assert.equal(notes(cardOf(tree, 'wsl-codex')).length, 0);
  assert.match(notes(cardOf(tree, 'win-codex'))[0]?.text() ?? '', /^a Windows path — the other side's CLI: .*, and this side skips it and looks the CLI up on PATH$/);
});
