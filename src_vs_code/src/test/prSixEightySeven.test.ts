import assert from 'node:assert/strict';
import { test } from 'node:test';
import { savedInOrder } from '../catalogCommands';
import { catalogHtml } from '../catalogPage';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * PR #687's review (CodeRabbit): keyboard focus survives a confirmed removal, and a removal's two writes — the rows and
 * the Bugz ranking model — never leave one without the other.
 */

/** A node the page can focus and open, as a dialog and its buttons are. */
class Element extends Node {
  open = false;
  textContent = '';

  showModal(): void {
    this.open = true;
  }

  close(): void {
    this.open = false;
  }
}

function pageWith(state: { value: unknown }, extra: Readonly<Record<string, readonly Node[]>> = {}) {
  const go = new Element({}, 'BUTTON');
  const page = runPageHtml(catalogHtml(panelState('reviewers'), 'test-nonce', 'models'), {
    '#confirm-dialog': [new Element({}, 'DIALOG')],
    '#confirm-title': [new Element({}, 'H2')],
    '#confirm-body': [new Element({}, 'P')],
    '#confirm-go': [go],
    '#confirm-keep': [new Element({}, 'BUTTON')],
    ...extra,
  }, undefined, state);

  return { page, go };
}

test('a confirmed removal leaves the caret a place to go once the card is gone', () => {
  const asks = new Node({ asks: 'removeModel', id: 'codex', askTitle: 't', askBody: 'b', askAction: 'Remove', askDanger: 'true' }, 'BUTTON');
  const state = { value: undefined as unknown };
  const { page, go } = pageWith(state, { '[data-asks]': [asks] });

  page.fire('click', asks);
  go.click();

  assert.equal((state.value as { focusAfter?: string } | undefined)?.focusAfter, 'model-search');
});

test('the next document puts the caret there, once', () => {
  const search = new Node({}, 'INPUT');
  const state = { value: { focusAfter: 'model-search' } as unknown };

  pageWith(state, { '#model-search': [search] });

  assert.equal(search.focused, true, 'the caret was left nowhere after the card it was on went away');
  assert.equal((state.value as { focusAfter?: string }).focusAfter, undefined, 'a later repaint must not pull the caret back');
});

test('a removal\'s two writes: Bugz first, and put back when the rows are refused — never one without the other', async () => {
  const writes: string[] = [];
  const refusing = (refused: string) => (key: string, value: unknown): Promise<boolean> => {
    writes.push(`${key}=${JSON.stringify(value)}`);
    return Promise.resolve(key !== refused);
  };

  assert.equal(await savedInOrder({ rows: [], bugzModel: '', refused: '', said: '' }, 'local/m', refusing('vendors')), false);
  assert.deepEqual(writes, ['bugzModel=""', 'vendors=[]', 'bugzModel="local/m"'], 'the Bugz model is put back when the rows were not saved');

  writes.length = 0;
  assert.equal(await savedInOrder({ rows: [], bugzModel: '', refused: '', said: '' }, 'local/m', refusing('bugzModel')), false);
  assert.deepEqual(writes, ['bugzModel=""'], 'the rows are not touched when the Bugz model could not be saved');

  writes.length = 0;
  assert.equal(await savedInOrder({ rows: [], refused: '', said: '' }, 'local/m', refusing('none')), true);
  assert.deepEqual(writes, ['vendors=[]'], 'a change that moves no Bugz model writes the rows alone');
});
