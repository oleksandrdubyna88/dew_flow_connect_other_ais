import assert from 'node:assert/strict';
import { test } from 'node:test';
import { confirmButton, NEW_FOR_MS, newTag, skew } from '../catalogShell';

/**
 * The new page's shared pieces (todo/PLAN_one_model_catalog.md, E3.1): `skew` by capability, `newTag` by control, and
 * the confirmed button every paid or destructive action draws.
 */

test('skew: by capability — nothing when the binary takes it, a sentence when it does not', () => {
  const able = { installed: true, features: ['systemPrompt', 'cliEffort'] };
  const older = { installed: true, features: ['bugzRuntime'] };

  assert.equal(skew('systemPrompt', 'its system prompt', able), '');
  assert.match(skew('systemPrompt', 'its system prompt', older), /does not take its system prompt yet.*update it under Setup › MCP server/);
});

test('skew: nothing at all while the binary has not answered — a cold start is not an older binary', () => {
  assert.equal(skew('systemPrompt', 'its system prompt', { installed: true, features: undefined }), '');
});

test('skew: "not installed" only when there is no binary on this side', () => {
  assert.match(skew('systemPrompt', 'its system prompt', { installed: false, features: undefined }), /not installed on this side/);
});

test('newTag: shown for a week after the control was first seen, then gone', () => {
  const seen = { 'model.thinking': 1_000 };

  assert.match(newTag('model.thinking', seen, 1_000), /tag-new/, 'the day it is first seen');
  assert.match(newTag('model.thinking', seen, 1_000 + NEW_FOR_MS - 1), /tag-new/, 'the last moment of the week');
  assert.equal(newTag('model.thinking', seen, 1_000 + NEW_FOR_MS), '', 'a week on');
  assert.equal(newTag('model.other', seen, 1_000), '', 'a control never seen as new is not marked');
  assert.equal(newTag('model.old', { 'model.old': 0 }, 1_000), '', 'a control that was there before the marks began is not new');
});

test('a confirmed button asks first — it carries no data-command the shared wiring would send on the first click', () => {
  const html = confirmButton({
    label: '✕', command: 'removeVendor', id: 'codex-2', title: 'Remove codex-2?', body: 'It is the consultant for "claude".',
    action: 'Remove', danger: true,
  });

  assert.doesNotMatch(html, /data-command=/);
  assert.match(html, /data-asks="removeVendor" data-id="codex-2"/);
  assert.match(html, /data-ask-body="It is the consultant for &quot;claude&quot;\."/, 'the body is escaped');
  assert.match(html, /data-ask-danger="true"/);
});

test('a confirmed button that may not be pressed now says why, and cannot be pressed', () => {
  const html = confirmButton({
    label: '✕', command: 'removeVendor', id: 'codex', title: 't', body: 'b', action: 'Remove', danger: true,
    refused: 'the only model switched on for plan review',
  });

  assert.match(html, / disabled title="the only model switched on for plan review"/);
});
