import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { blanked } from './blankedSource';

/**
 * Work running inside the panel's write queue never waits for a repaint.
 *
 * <p>`render()` waits for the write queue before it reads anything (`WriteQueue.settled`), and a write
 * still RUNNING in that queue is part of what it waits for — so a queued write that awaits a render
 * waits on itself, for ever, and every later write in the panel queues behind it until the window is
 * reloaded. That froze the panel from PR #561 on and was fixed with `afterTheWrite`, which STARTS the
 * repaint instead of awaiting it. On 2026-09-28 the same shape was found at one more site the fix never
 * reached — a refused setting on an `api` reviewer's card (`writeApiSetting`, plan F9) — by a
 * consultation, not by a test (`research/PLAN_settings_page.md`).</p>
 *
 * <p>So the guard follows CALL CHAINS, because a scan of `write()` alone would have missed that very
 * site: it starts at every method the dispatcher hands to `enqueue`, follows every call that method
 * AWAITS or RETURNS (the two ways a queued write can end up waiting on a promise), and fails on any chain
 * that reaches `render`. A call that is merely started — `void this.x()`, `afterTheWrite(() => this.x())`
 * — does not hold the queue and is not followed.</p>
 *
 * <p>Structural, because `PanelProvider` imports `vscode` and no unit test here can construct it. It is
 * paired with fixtures that prove it finds the shape, so a scan that stopped matching cannot pass by
 * matching nothing.</p>
 */

const PROVIDER = path.join(__dirname, '..', '..', 'src', 'panelProvider.ts');

interface Method {
  readonly name: string;
  readonly from: number;
  readonly to: number;
}

/** Class methods, by the lines a method header sits on — two-space indent, the house layout. */
function methods(clean: string): readonly Method[] {
  const headers = [...clean.matchAll(/^ {2}(?:(?:private|public|protected|static|readonly|override)\s+)*(?:async\s+)?(?:get\s+)?(\w+)\s*(?:<[^>\n]*>)?\s*\(/gm)]
    .filter((m) => !['if', 'for', 'while', 'switch', 'catch', 'return', 'constructor'].includes(m[1]!));

  return headers.map((m, i) => ({ name: m[1]!, from: m.index, to: headers[i + 1]?.index ?? clean.length }));
}

/** The methods a method AWAITS or RETURNS, with where the call is. */
function held(clean: string, method: Method): readonly { callee: string; at: number }[] {
  const body = clean.slice(method.from, method.to);

  return [...body.matchAll(/\b(?:await|return)\s+this\.(\w+)\s*\(/g)].map((m) => ({ callee: m[1]!, at: method.from + m.index }));
}

/** Every chain from a queued method to `render`, as `file:line` of its first awaited call. */
export function queuedWaitsOnRender(source: string): readonly string[] {
  const clean = blanked(source);
  const byName = new Map(methods(clean).map((m) => [m.name, m]));
  const roots = [...clean.matchAll(/\.enqueue\(\s*\(\)\s*=>\s*this\.(\w+)\s*\(/g)].map((m) => m[1]!);
  const lineOf = (at: number): number => source.slice(0, at).split('\n').length;
  const found: string[] = [];

  for (const root of roots) {
    const seen = new Set<string>();
    const walk = (name: string, chain: readonly string[], firstAt: number): void => {
      if (name === 'render') {
        found.push(`panelProvider.ts:${lineOf(firstAt)} — ${[...chain, 'render'].join(' → ')}`);
        return;
      }
      const method = byName.get(name);
      if (method === undefined || seen.has(name)) {
        return;
      }
      seen.add(name);
      for (const call of held(clean, method)) {
        walk(call.callee, [...chain, name], firstAt < 0 ? call.at : firstAt);
      }
    };
    walk(root, [], -1);
  }

  return found;
}

export function queuedRoots(source: string): readonly string[] {
  return [...blanked(source).matchAll(/\.enqueue\(\s*\(\)\s*=>\s*this\.(\w+)\s*\(/g)].map((m) => m[1]!);
}

test('no write the panel queues waits for a repaint, however many calls down', () => {
  const found = queuedWaitsOnRender(fs.readFileSync(PROVIDER, 'utf8'));

  assert.deepEqual(found, [], `a queued write awaits render() and so waits on itself: ${found.join('; ')}`);
});

test('the scan still reads the provider’s own methods, so its walk is not a walk through nothing', () => {
  const names = methods(blanked(fs.readFileSync(PROVIDER, 'utf8'))).map((m) => m.name);
  for (const known of ['write', 'choosePrompt', 'writeApiSetting', 'snapBack', 'render', 'saveWrite']) {
    assert.ok(names.includes(known), `the method scan no longer finds ${known}, so a chain through it would be invisible`);
  }
});

test('the scan still finds the writes the panel queues', () => {
  const roots = queuedRoots(fs.readFileSync(PROVIDER, 'utf8'));

  assert.ok(roots.includes('write'), `the setting write is queued, and the scan did not see it: ${roots.join(', ')}`);
  assert.ok(roots.includes('choosePrompt'), `the prompt choice is queued, and the scan did not see it: ${roots.join(', ')}`);
});

test('the scan finds the shape it exists for, two calls down', () => {
  const fixture = [
    'class P {',
    '  go(): void {',
    '    this.enqueue(() => this.write(m));',
    '  }',
    '  private async write(m: string): Promise<void> {',
    '    await this.inner(m);',
    '  }',
    '  private async inner(m: string): Promise<void> {',
    '    await this.snapBack();',
    '  }',
    '  private snapBack(): Promise<void> {',
    '    return this.render();',
    '  }',
    '  private async render(): Promise<void> {',
    '    await this.writes.settled();',
    '  }',
    '}',
  ].join('\n');

  assert.deepEqual(queuedWaitsOnRender(fixture), ['panelProvider.ts:6 — write → inner → snapBack → render']);
});

test('a repaint that is only STARTED does not hold the queue, and is not reported', () => {
  const fixture = [
    'class P {',
    '  go(): void {',
    '    this.enqueue(() => this.write(m));',
    '  }',
    '  private async write(m: string): Promise<void> {',
    '    await afterTheWrite(() => this.snapBack())();',
    '    void this.render();',
    '    // await this.render() in a comment is not a call',
    "    const s = 'await this.render()';",
    '  }',
    '  private snapBack(): Promise<void> {',
    '    return this.render();',
    '  }',
    '  private async render(): Promise<void> {',
    '  }',
    '}',
  ].join('\n');

  assert.deepEqual(queuedWaitsOnRender(fixture), []);
});
