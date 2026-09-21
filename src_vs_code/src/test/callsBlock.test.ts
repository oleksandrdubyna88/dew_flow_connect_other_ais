import assert from 'node:assert/strict';
import { test } from 'node:test';

import { CallEnd, Calls } from '../callHierarchy';
import { calledOut, callsBlock } from '../callsBlock';

/**
 * The markup a row shows about who calls its method.
 *
 * <p>Two things are load-bearing here and neither is decoration: the list is BOUNDED, because a
 * method with ten thousand callers would otherwise become ten thousand buttons in one `innerHTML`
 * assignment; and an entry is named by INDEX, because a page that carried file paths would be a page
 * that could be asked to open one.</p>
 */

const END = (name: string): CallEnd => ({ name, file: 'src/A.cs', line: 3, character: 4, detail: '' });

const answered = (ends: readonly CallEnd[]): Calls => ({
  findingId: 7, about: 'src/A.cs:3:counted', attempt: 'a1', prepared: 'ok',
  incoming: { asked: true, failed: false, ends },
  outgoing: { asked: false, failed: false, ends: [] },
});

test('the three phases each say one thing, and only the unasked and answered ones can be pressed', () => {
  assert.match(callsBlock(7, { phase: 'unasked' }), /data-calls="7"/u);
  assert.doesNotMatch(callsBlock(7, { phase: 'asking' }), /data-calls=/u,
    'a row already asking must not take a second press');
  assert.match(callsBlock(7, { phase: 'asking' }), /asking the language support/u);
  assert.match(callsBlock(7, { phase: 'answered', calls: answered([END('uses')]) }), /data-calls="7"/u,
    'an answered row can always be asked again — that is what makes it survive a branch switch');
});

test('the list stops, and says how many it did not show — the COUNT is never truncated', () => {
  const many = Array.from({ length: 120 }, (_, at) => END(`caller${at}`));
  const html = callsBlock(7, { phase: 'answered', calls: answered(many) });

  assert.match(html, /120 methods call this/u, 'the sentence counts all of them');
  assert.equal((html.match(/data-open-call="7:in:/gu) ?? []).length, 50,
    'one innerHTML assignment must not carry ten thousand buttons');
  assert.match(html, /and 70 more/u, 'and a person is told the list was cut, never left to assume it was all');
});

test('a list that fits is shown whole, with no tail', () => {
  const html = callsBlock(7, { phase: 'answered', calls: answered([END('one'), END('two')]) });

  assert.equal((html.match(/data-open-call=/gu) ?? []).length, 2);
  assert.doesNotMatch(html, /more<\/span>/u);
});

test('a name a provider invented cannot become markup', () => {
  const html = callsBlock(7, { phase: 'answered', calls: answered([END('</button><img src=x onerror=alert(1)>')]) });

  assert.doesNotMatch(html, /<img/u, 'a language server answers with strings from somebody else\u2019s repository');
  assert.match(html, /&lt;img/u);
});

test('a row that could not be asked shows the reason instead of a number, and never a state token', () => {
  const html = callsBlock(7, { phase: 'answered', calls: { ...answered([]), prepared: 'no-provider' } });

  assert.doesNotMatch(html, /nothing calls this|\d+ methods? calls this/u,
    'a count nobody could take must not be printed as zero');
  assert.match(html, /no language support is installed/u);
});

test('which entry a press named is read back, and anything malformed names nothing', () => {
  assert.deepEqual(calledOut('7:in:2'), { id: 7, which: 'in', at: 2 });
  assert.deepEqual(calledOut('7:out:0'), { id: 7, which: 'out', at: 0 });

  for (const bad of ['7:in', '7:in:2:3', '7:sideways:2', '7:in:-1', '-1:in:2', 'x:in:2', '7:in:x', '', '7:in:1.5']) {
    assert.equal(calledOut(bad), undefined, `${bad} must name nothing rather than something`);
  }
});
