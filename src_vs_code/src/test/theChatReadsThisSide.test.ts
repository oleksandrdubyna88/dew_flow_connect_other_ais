import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { OVERLAID_SETTINGS } from '../settingsShape';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md: the chat's model and its model name are per side (D8 — chat models are per
 * side; the model the chat opens on names one of THIS side's rows). So the chat's settings are read through this side —
 * `chatRead` — never the user layer alone, which answers the shared value and opens a side on a row it may not have.
 * Structural, because these hosts import vscode and no test here can run them; `thePanelReadsThisSide.test.ts` counts
 * reads BY NAME and cannot see a reader handed to `chatSettingsFrom`.
 */

const SRC = path.join(__dirname, '..', '..', 'src');

test('the chat model and its name are per side', () => {
  assert.ok(OVERLAID_SETTINGS.includes('chatModel') && OVERLAID_SETTINGS.includes('chatModelName'));
});

test('no file hands the chat\'s settings the user layer alone', () => {
  const sites = fs.readdirSync(SRC).filter((name) => name.endsWith('.ts'))
    .filter((name) => /chatSettingsFrom\(\s*userLayer\(/.test(fs.readFileSync(path.join(SRC, name), 'utf8')));

  assert.deepEqual(sites, []);
});

test('the scan still finds what it looks for — a guard that matches nothing passes on anything', () => {
  assert.ok(/chatSettingsFrom\(\s*userLayer\(/.test('chatSettingsFrom(userLayer(config))'));
  assert.ok(fs.readdirSync(SRC).some((name) => /chatSettingsFrom\(/.test(fs.readFileSync(path.join(SRC, name), 'utf8'))), 'no file reads the chat\'s settings at all');
});
