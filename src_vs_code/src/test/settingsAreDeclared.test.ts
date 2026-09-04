import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * Every control the panel offers must be a setting VS Code will actually keep.
 *
 * <p>Reported by the operator: the three switches in *The gate* came unticked again — after a run,
 * or just with time. They were never declared in `contributes.configuration`, and VS Code refuses to
 * persist an update to a key no extension has registered. So the box lit up, the panel repainted
 * from what it had in hand, and the next honest read of the configuration found nothing there.</p>
 *
 * <p>This is the durable-status rule pointed at a checkbox: clicked, reloaded, gone. The test is
 * written against every control rather than those three, because the next setting somebody adds will
 * fail the same way for the same reason.</p>
 */

const state = (): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  questions: [],
  sessions: [],
  openSections: ['reviewers', 'language', 'prompts', 'gate', 'limits', 'keys', 'server', 'usage', 'rounds'],
  openRounds: [],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
});

/** The tag each `data-setting` sits in, so a per-vendor control can be told from a global one. */
function controls(html: string): { setting: string; scoped: boolean }[] {
  return [...html.matchAll(/<[^>]*\bdata-setting="([a-zA-Z.]+)"[^>]*>/g)].map((m) => ({
    setting: m[1]!,
    // A per-vendor or per-role control is stored INSIDE `coai.vendors` or a role record, so it is
    // not a settings key of its own and must not be required to be one.
    scoped: /\bdata-(vendor|role)=/.test(m[0]),
  }));
}

test('every global control the panel renders is a declared setting', () => {
  const manifest = JSON.parse(
    readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8'),
  ) as { contributes: { configuration: { properties: Record<string, unknown> } } };
  const declared = new Set(Object.keys(manifest.contributes.configuration.properties));

  const missing = [...new Set(
    controls(panelHtml(state(), 'n0nce'))
      .filter((c) => !c.scoped)
      .map((c) => c.setting),
  )].filter((setting) => !declared.has(`coai.${setting}`));

  assert.deepEqual(
    missing,
    [],
    `VS Code will not persist these — they are not registered configuration: ${missing.join(', ')}`,
  );
});

test('the three gate switches are booleans that default to off', () => {
  // Off by default is the whole promise of the feature: a switch nobody set changes nothing.
  const manifest = JSON.parse(
    readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8'),
  ) as { contributes: { configuration: { properties: Record<string, { type: string; default: unknown }> } } };
  const props = manifest.contributes.configuration.properties;

  for (const key of ['coai.autonomous', 'coai.splitPlan', 'coai.splitWithFable']) {
    assert.ok(props[key], `${key} is not registered, so ticking it cannot survive a reload`);
    assert.equal(props[key]!.type, 'boolean', `${key} must be a boolean`);
    assert.equal(props[key]!.default, false, `${key} must be off until somebody turns it on`);
  }
});
