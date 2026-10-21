import assert from 'node:assert/strict';
import { test } from 'node:test';

import { parseRoundCounts } from '../roundCounts';
import { storeName, storeNameOf } from '../storeWords';
import { MERGED_JSON, THIS_STORE, WSL_STORE } from './roundCountsFixtures';

/** How a person names a store (E6): where the numbers came from, never whose account they are. */

const stores = parseRoundCounts(MERGED_JSON)?.stores ?? [];
const base = { live: false, host: 'linux', wslDistro: '', platform: 'linux', directory: '/srv/coai' };

test('this side is this machine; a WSL distribution is WSL: <distro>; any other store its operating system', () => {
  assert.equal(storeNameOf(stores, THIS_STORE), 'this machine');
  assert.equal(storeNameOf(stores, WSL_STORE), 'WSL: Ubuntu');
  assert.equal(storeName({ ...base, platform: 'windows', host: 'windows' }), 'Windows');
  assert.equal(storeName({ ...base, platform: 'linux' }), 'Linux');
  assert.equal(storeName({ ...base, platform: 'macos', host: 'macos' }), 'macOS');
});

test('a WSL store with no distribution is WSL; an unknown platform is named by its directory; an unnamed id is itself', () => {
  assert.equal(storeName({ ...base, host: 'wsl' }), 'WSL');
  assert.equal(storeName({ ...base, platform: 'other' }), '/srv/coai');
  assert.equal(storeName({ ...base, platform: 'other', directory: '' }), 'another store');
  assert.equal(storeNameOf(stores, 'D:\\coai-nas'), 'D:\\coai-nas', 'a directory standing for its own store is named as the directory');
});
