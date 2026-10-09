import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CATALOG_TABS, OLD_TAB_PLACES, placeOf } from '../catalogPlaces';

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

/**
 * Every id the old Settings page answered to, in its order — LISTED, not read off the old page's sections as it was until
 * E5's prerequisite (b): E5.1 deletes those sections, and a loop over them would then check nothing and pass. The ids
 * outlive the page: a help article, a notification or another extension goes on naming them to `coai.openSettings`.
 */
const OLD_IDS = ['reviewers', 'chat', 'consultant', 'questionconsultant', 'securityLane', 'prompts', 'gate', 'limits', 'keys', 'teamServers', 'side', 'server'];

test('every old tab id has a place on the new page, and each place is one the page has', () => {
  assert.deepEqual(Object.keys(OLD_TAB_PLACES), OLD_IDS, 'an old id dropped is a deep link that opens the first tab instead');
  let checked = 0;
  for (const id of OLD_IDS) {
    const place = OLD_TAB_PLACES[id];
    assert.ok(place !== undefined, `the old tab '${id}' has no place on the new page`);
    assert.ok(PLACES.includes(place), `'${id}' maps to '${place}', which is not a place on the new page`);
    checked += 1;
  }
  assert.equal(checked, 12, 'twelve old tab ids, each with a place');
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
