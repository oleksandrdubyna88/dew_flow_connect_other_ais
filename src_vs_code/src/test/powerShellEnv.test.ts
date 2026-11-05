import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { blanked } from './blankedSource';
import { MODULE_CACHE_NAME, powerShellEnv } from './powerShellEnv';

/**
 * C28 of todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md (S12): every PowerShell a test starts gets ONE
 * profile-free environment (`powerShellEnv()`) and a working folder of the run's temp, so its module-analysis cache can
 * never land in `src_vs_code` — whatever the caller's profile looks like.
 *
 * <p>The scan is the deterministic half. A test of the cache itself would have to start a PowerShell that lives longer
 * than its deferred cache write (about 10 s) with a dirty cache, and on a machine whose profile resolves it stays green
 * either way — so it is not here, on purpose.</p>
 */

const TESTS = join(__dirname, '..', '..', 'src', 'test');

/** A call that starts a process. */
const STARTS_A_PROCESS = /\b(?:spawnSync|spawn|execFileSync|execFile)\(/gu;

/** What the first argument looks like when it names a PowerShell: the literal, or the identifiers the tests hold it in. */
const NAMES_A_POWERSHELL = /powershell|pwsh|^(?:SHELL|shell|exe)$/iu;

interface Site {
  readonly where: string;
  readonly first: string;
  readonly args: string;
}

function sourcesUnder(dir: string): readonly string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? sourcesUnder(join(dir, entry.name)) : entry.name.endsWith('.ts') ? [join(dir, entry.name)] : []);
}

/** The index of the parenthesis closing the one opened at `open`, over text whose strings and comments are blanked. */
function closeOf(code: string, open: number): number {
  let depth = 0;
  for (let i = open; i < code.length; i += 1) {
    depth += '([{'.includes(code.charAt(i)) ? 1 : ')]}'.includes(code.charAt(i)) ? -1 : 0;
    if (depth === 0) {
      return i;
    }
  }

  return code.length;
}

/** The end of the first argument: the first comma outside every bracket. */
function firstArgumentEnd(code: string, from: number, to: number): number {
  let depth = 0;
  for (let i = from; i < to; i += 1) {
    depth += '([{'.includes(code.charAt(i)) ? 1 : ')]}'.includes(code.charAt(i)) ? -1 : 0;
    if (depth === 0 && code.charAt(i) === ',') {
      return i;
    }
  }

  return to;
}

function sitesIn(file: string): readonly Site[] {
  const raw = readFileSync(file, 'utf8');
  if (!/powershell|pwsh/iu.test(raw)) {
    return [];
  }
  const code = blanked(raw);

  return [...code.matchAll(STARTS_A_PROCESS)].flatMap((match) => {
    const open = match.index + match[0].length - 1;
    const close = closeOf(code, open);
    const first = raw.slice(open + 1, firstArgumentEnd(code, open + 1, close)).trim();
    const line = raw.slice(0, open).split('\n').length;

    return NAMES_A_POWERSHELL.test(first)
      ? [{ where: `${file.slice(TESTS.length + 1)}:${line}`, first, args: code.slice(open, close + 1) }]
      : [];
  });
}

const SITES = sourcesUnder(TESTS).flatMap(sitesIn);

test('everyPowerShellSpawnInTheTests_usesThePowerShellEnv: each one passes env: powerShellEnv(...) and cwd: tmpdir()', () => {
  const offenders = SITES
    .filter((site) => !/\benv:\s*powerShellEnv\(/u.test(site.args) || !/\bcwd:\s*tmpdir\(\)/u.test(site.args))
    .map((site) => `${site.where} (${site.first})`);

  assert.deepEqual(offenders, [], 'these PowerShell spawns lack `env: powerShellEnv(...)` or `cwd: tmpdir()` — a PowerShell with the caller\'s '
    + 'profile (or none) and the checkout as its working folder can leave its module cache there');
});

test('the scan sees the PowerShell spawns there are — a scan that finds none proves nothing', () => {
  assert.ok(SITES.length >= 9, `expected at least the nine known PowerShell spawns, found ${SITES.length}: ${SITES.map((s) => s.where).join(', ')}`);
});

// ---------------------------------------------------------------- the helper itself

test('powerShellEnv_carriesTheModuleCacheInsideTheRunsTemp: PSModuleAnalysisCachePath names a file under the temp folder it was given', () => {
  const temp = join(tmpdir(), 'a run temp');
  const env = powerShellEnv({}, temp);

  assert.equal(env['PSModuleAnalysisCachePath'], join(temp, MODULE_CACHE_NAME), 'the cache must be pointed into the temp folder of the run');
  assert.equal(env['TEMP'], temp);
  assert.equal(env['TMP'], temp);
});

test('powerShellEnv_defaultsToThisProcessesTmpdir, and what a caller passes wins over a default', () => {
  assert.equal(powerShellEnv()['PSModuleAnalysisCachePath'], join(tmpdir(), MODULE_CACHE_NAME));
  assert.equal(powerShellEnv({ PATH: 'only-this' })['PATH'], 'only-this');
  assert.equal(powerShellEnv({ MY_SCENARIO: '1' })['MY_SCENARIO'], '1');
});

test('powerShellEnv_isMinimal: it carries none of the caller\'s profile — no home, no module path, no token', () => {
  const names = Object.keys(powerShellEnv()).map((name) => name.toLowerCase());

  for (const forbidden of ['userprofile', 'appdata', 'psmodulepath', 'home', 'homepath', 'azure_devops_ext_pat']) {
    assert.ok(!names.includes(forbidden), `powerShellEnv carries ${forbidden}`);
  }
});
