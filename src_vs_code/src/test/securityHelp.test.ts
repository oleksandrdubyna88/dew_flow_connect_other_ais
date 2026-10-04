import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bodyFor, HELP_ARTICLES, HELP_LANGUAGES } from '../helpContent';
import { DEFAULTS } from '../settingsShape';
import { securityLaneFrom } from '../securityLane';
import { SECURITY_COMMANDS } from '../securityLaneState';
import { DEFAULT_VENDORS } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';

// The Security lane help describes the tab as it is drawn (research/PLAN_the_security_tab_reads_at_a_glance.md,
// epic 4): every button the tab draws is named, by its label, in every language — the labels are read from the RUN
// page, so renaming a button turns this red until the five articles follow — and the count of shipped prompts the old
// article fixed at twelve (general made it thirteen) is not written into any language.

const VENDOR = DEFAULT_VENDORS.find((v) => v.enabled)!.id;
const SERVER = { kind: 'known', version: '0.43.0', remembered: false, updateOffered: false } as const;

/** A lane with every card state on it, so every button the tab can draw is drawn: an edited preset, a custom prompt,
 * a hand-added general with stored conditions, and a pair. */
const LANE = securityLaneFrom({
  enabled: true,
  prompts: [{ id: 'redteam-general', triggers: ['sql'], focus: [] }, { id: 'redteam-mine', triggers: [], focus: [] }],
  runs: [{ vendor: VENDOR, prompt: 'redteam-authz' }],
});
const PAGE = runPanel(panelState('securityLane', {
  settings: { ...DEFAULTS, securityLane: LANE }, server: SERVER,
  securityPromptText: { 'redteam-authz': 'written', 'redteam-mine': 'written' }, securityPromptDir: 'C:/data/prompts',
}));
const TAB_COMMANDS: ReadonlySet<string> = new Set([...SECURITY_COMMANDS, 'editSecurityPrompt']);
/** Every label the tab's buttons carry, as the page drew them. */
const LABELS = [...new Set(PAGE.commands.filter((c) => TAB_COMMANDS.has(c.dataset['command'] ?? '')).map((c) => c.textContent))];

const article = HELP_ARTICLES.find((one) => one.id === 'security-lane');
/** "Twelve" in each help language — a number the catalogue outgrew the day general shipped. */
const TWELVE = /\b12\b|twelve|zwölf|\bdoce\b|двенадцат|дванадцят/iu;

test('the census draws every Security lane button, so an empty list of labels cannot pass the tests below', () => {
  const drawn = new Set(PAGE.commands.map((c) => c.dataset['command']));
  const undrawn = [...TAB_COMMANDS].filter((command) => !drawn.has(command));
  assert.deepEqual(undrawn, [], `the census lane draws no button for ${undrawn.join(', ')}`);
});

for (const language of HELP_LANGUAGES) {
  const text = (): string => {
    assert.ok(article, 'there is no security lane article');
    return Object.values(bodyFor(article, language).body).join('\n');
  };

  test(`the ${language} security lane help names every button the tab draws, and general`, () => {
    const missing = [...LABELS, 'redteam-general'].filter((label) => !text().includes(label));
    assert.deepEqual(missing, [], `the ${language} article never names ${missing.join(', ')}`);
  });

  test(`the ${language} security lane help says which server runs general on code changes only`, () => {
    assert.ok(text().includes('0.43.0'), `the ${language} article does not say an older server runs general on every change`);
  });

  test(`the ${language} security lane help does not count the shipped prompts as twelve`, () => {
    assert.doesNotMatch(text(), TWELVE);
  });
}
