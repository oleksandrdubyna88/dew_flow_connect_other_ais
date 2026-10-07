import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * A chat's claude fast-mode settings file (todo/PLAN_fast_mode.md, Story C) — the extension's OWN pair, so a chat never
 * depends on coai-mcp having run (the plan round's finding). One key, `fastMode`: `--settings` loads ADDITIONAL settings
 * merged over the person's, so nothing else changes. Written only when missing or saying something else, through a
 * temporary file renamed into place, so no launch reads half of it.
 *
 * Where (the code round): never in the chat's own text-mode folder, which is "a directory with nothing in it"; and never a
 * fixed shared temp path, which another local user could plant first — a private folder made once per extension process
 * with a random name (`mkdtemp`, owner-only where the platform has modes).
 */
export function chatFastSettingsFile(on: boolean, dir: string = ownDir()): string {
  const { name, text } = on ? STATES.on : STATES.off;
  const path = join(dir, name);
  if (!says(path, text)) {
    replace(dir, path, text);
  }

  return path;
}

/** Each state's file and the one key it holds. */
const STATES = {
  on: { name: 'fast-on.json', text: '{"fastMode":true}' },
  off: { name: 'fast-off.json', text: '{"fastMode":false}' },
} as const;

/** Through a staging file of this write's OWN, renamed into place. */
function replace(dir: string, path: string, text: string): void {
  mkdirSync(dir, { recursive: true });
  const staging = `${path}.${randomUUID()}.tmp`;
  writeFileSync(staging, text, 'utf8');
  renameSync(staging, path);
}

let made = '';

/** This extension process's private folder for the pair — made on first use, kept for its life. */
function ownDir(): string {
  made = made.length > 0 ? made : mkdtempSync(join(tmpdir(), 'coai-chat-fast-'));

  return made;
}

/** Whether the file is there and says exactly this. */
function says(path: string, text: string): boolean {
  return existsSync(path) && readFileSync(path, 'utf8') === text;
}
