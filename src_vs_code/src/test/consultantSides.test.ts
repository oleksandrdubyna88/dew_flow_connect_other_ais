import assert from 'node:assert/strict';
import { test } from 'node:test';

import { healthSides, otherSideLabel, thisSideLabel } from '../consultantSides';

/**
 * Which sides of this machine the Consultant tab shows, and what each is called (E5.1).
 *
 * <p>THIS side is the extension host's own — named the way the panel's MCP server tab names it (`sideLabel`), and by
 * its operating system in a local window, where `sideLabel` says nothing. The OTHER sides are the data directories a
 * person named in `coai.alsoWatchDataDirectories`, and each is labelled from the shape of its path: a WSL store reached
 * from Windows, or the Windows store reached from WSL. The directory rules themselves (which spellings are one place,
 * which paths cannot work from here) are `escalationDirs.ts`'s, reused rather than written twice.</p>
 */

test('this side is named as the MCP server tab names it, and by its operating system in a local window', () => {
  assert.equal(thisSideLabel('wsl', 'Ubuntu', 'linux'), 'WSL: Ubuntu');
  assert.equal(thisSideLabel(undefined, '', 'win32'), 'Windows');
  assert.equal(thisSideLabel(undefined, '', 'linux'), 'Linux');
  assert.equal(thisSideLabel(undefined, '', 'darwin'), 'macOS');
});

test('a WSL store reached from Windows is labelled with its distribution, in either spelling of the share', () => {
  assert.equal(otherSideLabel('\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp'), 'WSL: Ubuntu');
  assert.equal(otherSideLabel('\\\\wsl$\\Debian\\home\\u\\.local\\share\\coai-mcp'), 'WSL: Debian');
  assert.equal(otherSideLabel('//wsl.localhost/Ubuntu-24.04/home/u/.local/share/coai-mcp'), 'WSL: Ubuntu-24.04');
});

test('the Windows store reached from WSL is labelled Windows', () => {
  assert.equal(otherSideLabel('/mnt/c/Users/u/AppData/Local/coai-mcp'), 'Windows');
});

test('any other folder is named by its path, never given a side it might not be', () => {
  assert.equal(otherSideLabel('\\\\nas\\share\\coai'), 'another installation at \\\\nas\\share\\coai');
});

test('this side comes first, then every usable folder named — a refused one and a second spelling of this one are left out', () => {
  const sides = healthSides(
    { label: 'Windows', dir: 'C:\\Users\\u\\AppData\\Local\\coai-mcp' },
    ['\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp', '/home/u/.local/share/coai-mcp', 'c:\\users\\u\\appdata\\local\\coai-mcp\\', ''],
    'win32',
  );

  assert.deepEqual(sides, [
    { kind: 'this', label: 'Windows', dir: 'C:\\Users\\u\\AppData\\Local\\coai-mcp' },
    { kind: 'other', label: 'WSL: Ubuntu', dir: '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp' },
  ]);
});
