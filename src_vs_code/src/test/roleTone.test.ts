import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { ROLE_TONE_CSS, roleTone } from '../roleTone';

/**
 * One palette for the seven roles, in one file.
 *
 * <p>The sidebar has given each role its own colour since the settings panel was written; the
 * Edit-roles page gave every role the same blue. Two views of the same seven roles, coloured by two
 * different rules — which is issue #293's third picture.</p>
 *
 * <p><b>What this file cannot prove alone</b>, and the code round of the plan said so: every test
 * here passes while `panelView.ts` keeps its own private copy of the map. The check that there is
 * ONE palette is in `editRolesInTabs.test.ts`, where both renderers are run and their output
 * compared. This file is about what the palette SAYS.</p>
 */

test('every shipped role keeps the tone the sidebar has always given it', () => {
  // By name, one at a time. A loop over the map would assert the map against itself; these five
  // values are what a person has been looking at, and changing one is a decision, not a refactor.
  assert.equal(roleTone('PlanCritique', 'plan'), 'plan');
  assert.equal(roleTone('Conventions', 'result'), 'conv');
  assert.equal(roleTone('Architecture', 'result'), 'arch');
  assert.equal(roleTone('SecurityReliability', 'result'), 'sec');
  assert.equal(roleTone('UxDxPerformance', 'result'), 'uxdx');
});

test('a role of your own is toned by its stage, which is what the sidebar does too', () => {
  // Not a colour of its own: matching the menu means matching it where it is arbitrary as well.
  assert.equal(roleTone('Requirements', 'plan'), 'plan');
  assert.equal(roleTone('Requirements', 'result'), 'arch');
});

test('a role the stage does not name still gets a tone rather than nothing', () => {
  // The page writes `class="role role-${tone}"`. An empty tone would emit `role-` — a class that
  // matches no rule, so the edge falls back to whatever `.role` set and the role looks like another.
  assert.notEqual(roleTone('Requirements', ''), '');
  assert.notEqual(roleTone('', ''), '');
});

test('every tone the stylesheet colours is a tone it also defines', () => {
  // A `.role-x` rule reading an undefined `--tone-x` draws no edge at all, and nothing about the
  // page's text says so. Both directions, because an orphan definition is a palette entry nobody
  // can reach — which is how a colour silently stops being used.
  const rules = [...ROLE_TONE_CSS.matchAll(/\.role-([a-z]+)\s*\{/gu)].map((m) => m[1]);
  const defined = new Set([...ROLE_TONE_CSS.matchAll(/--tone-([a-z]+)\s*:/gu)].map((m) => m[1]));

  assert.ok(rules.length >= 6, `the stylesheet colours ${rules.length} tones, which is fewer than the roles`);
  for (const tone of rules) {
    assert.ok(defined.has(tone), `.role-${tone} reads --tone-${tone}, which nothing defines`);
  }
});

test('every tone a shipped role is given has a rule that colours it', () => {
  // The join between the two halves: a map entry with no stylesheet rule is a role whose edge is
  // the default. Asserted over the real catalog, so a role added to it is covered without an edit.
  const rules = new Set([...ROLE_TONE_CSS.matchAll(/\.role-([a-z]+)\s*\{/gu)].map((m) => m[1]));

  for (const role of BUILTIN_ROLES) {
    const tone = roleTone(role.id, role.stage);
    assert.ok(rules.has(tone), `${role.id} is toned ${tone}, and no .role-${tone} rule colours it`);
  }
});

test('each tone falls back to a colour of its own when the theme defines no charts palette', () => {
  // The house convention, stated where this block came from: a charts token with the hex it falls
  // back to, so a theme that defines them wins and one that does not still gets the intended
  // colour. A bare `var(--vscode-charts-x)` in a theme without it is an edge nobody can see.
  const tones = [...ROLE_TONE_CSS.matchAll(/--tone-[a-z]+:\s*([^;]+);/gu)].map((m) => m[1]);

  assert.ok(tones.length >= 6, 'the palette lost its definitions');
  for (const value of tones) {
    assert.match(value, /var\(--vscode-[a-z-]+,\s*#[0-9a-f]{6}\)/u, `${value} has no fallback of its own`);
  }
});
