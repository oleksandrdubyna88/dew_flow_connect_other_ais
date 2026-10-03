import assert from 'node:assert/strict';
import { test } from 'node:test';

import { rolesHtml } from '../rolesPage';
import { stylesheet } from './cssRules';

/**
 * The Review roles page draws in its own text size from the first paint, and styles its size control.
 *
 * <p>Found while planning `research/PLAN_every_page_reads_alike.md`: the page wrote its size — a bare
 * `font-size: 13px;` — at the top level of its stylesheet, outside any rule. A browser reads that and the
 * rule after it as ONE invalid selector and drops both, so the page opened at the theme's size with an
 * unstyled control, and only the host's later push corrected the size. Read through the parsed sheet,
 * because that is the only way a declaration's PLACE shows.</p>
 */

const html = (uiScale: number): string => rolesHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, uiScale }, 'n');

test('the page\'s own text size is inside its body rule, not loose at the top of the sheet', () => {
  // Every body rule, joined: the tone control ships one of its own for its two colour targets.
  const body = stylesheet(html(3)).filter((rule) => rule.selector.trim() === 'body').map((rule) => rule.body).join(' ');

  assert.ok(body.length > 0, 'the page has no body rule');
  assert.match(body, /font-size:\s*17\.3px/, 'the body rule does not carry the chosen size, so the first paint ignores it');
});

test('no declaration is swallowed into a selector, which is how the size control lost its styling', () => {
  const rules = stylesheet(html(0));
  const swallowed = rules.filter((rule) => rule.selector.includes(':') && /[a-z-]+\s*:\s*[^:]+;/.test(rule.selector));

  assert.deepEqual(swallowed.map((rule) => rule.selector.trim()), [], 'a loose declaration turned the rule after it into an invalid selector');
  assert.ok(rules.some((rule) => rule.selector.trim() === '.zoomCtl'), 'the size control has no styling rule');
});
