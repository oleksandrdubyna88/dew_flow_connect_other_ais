import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CATALOG_TABS, OLD_TAB_PLACES, oldIdOf, placeOf } from '../catalogPlaces';
import { settingsSections } from '../panelView';

/**
 * Where the new Settings page opens (todo/PLAN_one_model_catalog.md, E3.1; D11): six tabs, their sub-tabs, and a place
 * for every id the OLD page answered to — `coai.openSettings('gate')` from a help article, a notification or another
 * extension must land on the gate, not on the first tab.
 */

const PLACES = CATALOG_TABS.flatMap((tab) => (tab.subs.length === 0 ? [tab.id] : tab.subs.map((sub) => `${tab.id}/${sub.id}`)));

test('the six tabs, in order, with the sub-tabs the design gives them', () => {
  assert.deepEqual(CATALOG_TABS.map((tab) => tab.id), ['models', 'reviews', 'consultants', 'security', 'chat', 'setup']);
  assert.deepEqual(CATALOG_TABS.find((tab) => tab.id === 'reviews')?.subs.map((sub) => sub.id),
    ['stages', 'roles', 'prompts', 'gate', 'commands', 'limits']);
  assert.deepEqual(CATALOG_TABS.find((tab) => tab.id === 'setup')?.subs.map((sub) => sub.id), ['keys', 'team', 'mcp', 'side']);
});

test('every old tab id has a place on the new page — read from the old page itself, so a new old tab cannot be missed', () => {
  for (const section of settingsSections()) {
    const place = OLD_TAB_PLACES[section.id];
    assert.ok(place !== undefined, `the old tab '${section.id}' has no place on the new page`);
    assert.ok(PLACES.includes(place), `'${section.id}' maps to '${place}', which is not a place on the new page`);
  }
});

test('an old id, a new place or a bare tab is taken; anything else leaves the held place as it was', () => {
  assert.equal(placeOf('gate', 'models'), 'reviews/gate', 'an old id opens its new place');
  assert.equal(placeOf('setup/team', 'models'), 'setup/team');
  assert.equal(placeOf('reviews', 'models'), 'reviews/stages', 'a bare tab opens its first sub-tab');
  assert.equal(placeOf('security', 'models'), 'security');
  for (const stray of [undefined, 42, { tab: 'gate' }, 'nonsense', 'reviews/nonsense', '']) {
    assert.equal(placeOf(stray, 'chat'), 'chat', `${JSON.stringify(stray)} changes nothing`);
  }
  assert.equal(placeOf(undefined, ''), 'models', 'nothing held opens the first tab');
});

test('going back to the old page keeps the person where they were, where the old page has the place', () => {
  assert.equal(oldIdOf('reviews/gate'), 'gate');
  assert.equal(oldIdOf('setup/mcp'), 'server');
  assert.equal(oldIdOf('reviews/roles'), '', 'a place the old page never had opens its first tab');
});
