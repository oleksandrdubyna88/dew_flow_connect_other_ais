import assert from 'node:assert/strict';
import { test } from 'node:test';

import { claudeExecutableFor, claudeIsWanted } from '../claudeCli';
import { CALLER_KINDS, ConsultSettings, DEFAULT_CONSULT, resolveConsultant } from '../consultSettings';
import { Runtime } from '../models';
import { Vendor } from '../vendors';

/**
 * Which Claude CLI gets asked, and whether anything is asked at all.
 *
 * <p>Both questions are about a person's CONFIGURATION, and both were answered wrongly by reading
 * only half of it: the probe resolved its binary from reviewer rows, so a Claude consultant with its
 * own CLI path and no reviewer row was probed with whatever PATH answered — a different
 * installation, possibly a different account. Three reviewers raised it independently on the code
 * round of issue #301.</p>
 */

function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'claude',
    runtime: 'claude',
    model: '',
    baseUrl: '',
    executablePath: '',
    enabled: true,
    plan: true,
    code: true,
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

/** The consultant map with ONE caller pointed somewhere, resolved the way the reader resolves it. */
function consulting(runtime: Runtime | '', executablePath = '', vendorId = 'claude', caller = 'claude'): ConsultSettings {
  return {
    ...DEFAULT_CONSULT,
    byCaller: {
      // Every other caller is put beyond reach, so a test says what it means: the shipped map points
      // three of the four at Claude, and a fixture that left them would pass without its own row.
      ...Object.fromEntries(CALLER_KINDS.map(({ id }) => [
        id,
        resolveConsultant({ vendor: 'nothing-here', runtime: '', model: '', baseUrl: '', executablePath: '' }, []),
      ])),
      [caller]: resolveConsultant({ vendor: vendorId, runtime, model: '', baseUrl: '', executablePath }, []),
    },
  };
}

test('a reviewer row that names its CLI is the one asked', () => {
  assert.equal(
    claudeExecutableFor([vendor({ executablePath: 'C:/tools/claude.cmd' })], consulting('')),
    'C:/tools/claude.cmd',
  );
});

test("a consultant's own CLI is asked when no reviewer row names one", () => {
  // The defect: `wanted` was true because of this consultant, and the binary was resolved without
  // ever looking at it — so the probe asked PATH and reported about a CLI this product would not run.
  assert.equal(
    claudeExecutableFor([], consulting('claude', 'D:/other/claude.exe')),
    'D:/other/claude.exe',
    'the consultant said where its CLI is and the probe asked somewhere else',
  );
});

test('a reviewer row wins over a consultant, because a row is what every other probe reads', () => {
  assert.equal(
    claudeExecutableFor([vendor({ executablePath: 'C:/tools/claude.cmd' })], consulting('claude', 'D:/other/claude.exe')),
    'C:/tools/claude.cmd',
  );
});

test('with nothing configured anywhere, the runtime is its own name', () => {
  assert.equal(claudeExecutableFor([], consulting('')), 'claude');
  assert.equal(claudeExecutableFor([vendor()], consulting('')), 'claude', 'a row that set no path shadows nothing');
});

test('an entry the rule could not place offers no path, and is not read for one', () => {
  // `resolveConsultant` answers `unavailable` for an id that is neither a row nor a runtime. It has
  // no `executablePath` field at all, so asking it for one is asking for something that is not there.
  assert.equal(claudeExecutableFor([], consulting('')), 'claude');
});

test('nothing is asked when nothing would use a Claude model', () => {
  // The vendor ID matters as much as the runtime: rule (b) of the resolution reads a bare 'claude'
  // AS the runtime, so a fixture naming it would be a Claude consultant however empty its runtime is.
  assert.equal(claudeIsWanted([], consulting('', '', 'gemini')), false, 'four billed requests for a dropdown nobody opens');
  assert.equal(claudeIsWanted([vendor({ runtime: 'codex' })], consulting('codex', '', 'codex')), false);
});

test('a Claude reviewer row, or a Claude consultant, is reason enough', () => {
  assert.equal(claudeIsWanted([vendor()], consulting('')), true);
  assert.equal(claudeIsWanted([], consulting('claude')), true,
    'the consultant section can be the only thing on this machine that runs Claude');
});
