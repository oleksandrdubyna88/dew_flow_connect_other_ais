import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';

import { PATH_FIELD_NAME, PATH_SETTINGS } from '../pathSettings';
import { sectionsOf } from '../settingRefused';

/**
 * The census over the manifest (todo/PLAN_paths_per_side.md E1.1): every setting, and every field nested in one, whose
 * name says it holds a path must be in `PATH_SETTINGS` — so the rule "a path of the other side is skipped, not refused"
 * cannot miss the next path setting the way it missed six of the seven before the registry existed.
 */

const manifest: unknown = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));

function recordOf(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

/** The keys of every object a default holds — a list of rows (`[]`) or a map of entries (`.*`). */
function defaultFields(schema: Record<string, unknown>, at: string): string[] {
  const value = schema['default'];
  const rows = Array.isArray(value) ? value.map((row) => [`${at}[]`, row] as const) : Object.values(recordOf(value)).map((entry) => [`${at}.*`, entry] as const);

  return rows.flatMap(([where, row]) => Object.keys(recordOf(row)).map((key) => `${where}.${key}`));
}

/** Every field a schema declares, at any depth: its properties, its rows' (`[]`) and its map entries' (`.*`). */
function schemaFields(schema: Record<string, unknown>, at: string): string[] {
  const own = Object.entries(recordOf(schema['properties'])).flatMap(([key, child]) => [`${at}.${key}`, ...schemaFields(recordOf(child), `${at}.${key}`)]);
  const items = schema['items'] === undefined ? [] : schemaFields(recordOf(schema['items']), `${at}[]`);
  const entries = schema['additionalProperties'] === undefined ? [] : schemaFields(recordOf(schema['additionalProperties']), `${at}.*`);

  return [...own, ...items, ...entries, ...defaultFields(schema, at)];
}

/** Every setting and nested field the manifest declares whose NAME says it holds a path. */
function declaredPathFields(): readonly string[] {
  const all = sectionsOf(manifest).flatMap((section) =>
    Object.entries(recordOf(section['properties'])).flatMap(([key, schema]) => [key, ...schemaFields(recordOf(schema), key)]));

  return [...new Set(all.filter((field) => PATH_FIELD_NAME.test(field)))].sort();
}

/** The settings the manifest declares, by their full key. */
function declaredSettings(): ReadonlySet<string> {
  return new Set(sectionsOf(manifest).flatMap((section) => Object.keys(recordOf(section['properties']))));
}

test('every path setting and nested path field the manifest declares is in the registry', () => {
  const registered = new Set(PATH_SETTINGS.map((one) => one.id));
  const missing = declaredPathFields().filter((field) => !registered.has(field));

  assert.deepEqual(missing, [], `path fields with no entry in PATH_SETTINGS (pathSettings.ts): ${missing.join(', ')}`);
});

test('the census still finds the path fields it knows — a scan that matched nothing would pass forever', () => {
  const found = declaredPathFields();

  for (const known of ['coai.qconsultRoots', 'coai.dataDirectory', 'coai.alsoWatchDataDirectories', 'coai.vendors[].executablePath', 'coai.consultants.*.executablePath']) {
    assert.ok(found.includes(known), `the census no longer finds ${known}; it found ${found.join(', ')}`);
  }
});

test('every registry entry names a setting this build declares, once, and its id starts with that setting', () => {
  const declared = declaredSettings();
  const ids = PATH_SETTINGS.map((one) => one.id);

  assert.equal(new Set(ids).size, ids.length, 'an entry is registered twice');
  for (const one of PATH_SETTINGS) {
    assert.ok(declared.has(one.setting), `${one.id}: ${one.setting} is not declared by the manifest`);
    assert.ok(one.id === one.setting || one.id.startsWith(`${one.setting}[].`) || one.id.startsWith(`${one.setting}.*.`), `${one.id} does not live in ${one.setting}`);
    assert.ok(PATH_FIELD_NAME.test(one.id), `${one.id} is not named like a path field, so the census could never find it`);
  }
});

test('a folder list is said by the half that reads it, and a field no page draws has no place', () => {
  for (const one of PATH_SETTINGS) {
    assert.equal(one.place.length === 0, one.saidBy === 'nobody', `${one.id}: a place is drawn exactly when somebody says something about it`);
    assert.ok(one.noun.length > 0 && one.there.length > 0, `${one.id}: the other-side note needs a noun and what that side does`);
  }
});
