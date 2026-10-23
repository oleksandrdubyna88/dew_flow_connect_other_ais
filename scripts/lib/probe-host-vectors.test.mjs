// node --test scripts/lib/probe-host-vectors.test.mjs — E2 code gate G2 (finding 1): the probes' native-Linux detection (host-facts.mjs runsOnNativeLinux, which reads the banner with
// readBanner and decides with vibe.mjs isNativeLinuxHost) is a THIRD implementation of a rule the product holds twice (C# WslGenerations/HostKind, the extension's hostKindOf). The server
// and the extension both assert their reading against shared/wsl-banner-vectors.json; the probes' reading is held to the same file here, so a probe that calls a WSL kernel "native" (and so
// launches Vibe with the keyring road closed on a host that was measured open, or the reverse) is a red test naming the vector.
//
// Expected kind, from the vector: NATIVE <=> the product's HostKind.Linux with a READ banner (generation `none` and a non-empty banner); a `wsl1` or `wsl2` kernel is not native; an empty
// (unread) banner is not native. Nothing here runs Vibe or opens /proc: the banner is handed in through the injected file system.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { runsOnNativeLinux } from './host-facts.mjs';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const { vectors } = JSON.parse(readFileSync(join(repo, 'shared', 'wsl-banner-vectors.json'), 'utf8'));

/** What the product says for this vector: native Linux only when the banner was READ and names no WSL. */
const expectedNative = (vector) => vector.generation === 'none' && vector.banner.trim().length > 0;

/** The probe's own reading of one banner, through its injected file system; an empty banner is an unread one (the product's WslGeneration.Unreadable). */
const probeSays = (banner) => runsOnNativeLinux({ platform: 'linux', fsx: { readFile: () => banner } });

test('the vectors file is read and holds a native, a WSL1, a WSL2 and an unread case (a vacuous loop proves nothing)', () => {
  assert.ok(vectors.length >= 8, `only ${vectors.length} vectors`);
  for (const generation of ['none', 'wsl1', 'wsl2']) assert.ok(vectors.some((v) => v.generation === generation), `no ${generation} vector`);
  assert.ok(vectors.some((v) => v.banner === ''), 'no empty-banner vector');
  assert.ok(vectors.some(expectedNative), 'no vector expects native');
});

for (const vector of vectors) {
  test(`the probe's classification of ${vector.id} matches the product's (${expectedNative(vector) ? 'native' : 'not native'})`, () => {
    assert.equal(probeSays(vector.banner), expectedNative(vector),
      `vector ${vector.id} (${vector.generation}): the product reads this banner as ${expectedNative(vector) ? 'native Linux' : 'NOT native Linux'}, the probe's runsOnNativeLinux says the opposite — banner: ${JSON.stringify(vector.banner)}`);
  });
}

test('off Linux no vector is native, whatever the banner says', () => {
  for (const platform of ['win32', 'darwin']) {
    for (const vector of vectors) {
      assert.equal(runsOnNativeLinux({ platform, fsx: { readFile: () => vector.banner } }), false, `${vector.id} on ${platform}`);
    }
  }
});
