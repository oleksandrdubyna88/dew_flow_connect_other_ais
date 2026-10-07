import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A chat's claude fast-mode settings file (todo/PLAN_fast_mode.md, Story C) — the extension's OWN pair, under the folder
 * the chat already owns, so a chat never depends on coai-mcp having run (the plan round's finding). One key, `fastMode`:
 * `--settings` loads ADDITIONAL settings merged over the person's, so nothing else changes. Written only when missing or
 * saying something else, through a temporary file renamed into place, so no launch reads half of it.
 */
export function chatFastSettingsFile(home: string, on: boolean): string {
  const dir = join(home, 'fast-mode');
  const path = join(dir, on ? 'fast-on.json' : 'fast-off.json');
  const text = on ? '{"fastMode":true}' : '{"fastMode":false}';
  if (!says(path, text)) {
    mkdirSync(dir, { recursive: true });
    const staging = `${path}.${process.pid}.tmp`;
    writeFileSync(staging, text, 'utf8');
    renameSync(staging, path);
  }

  return path;
}

/** Whether the file is there and says exactly this. */
function says(path: string, text: string): boolean {
  return existsSync(path) && readFileSync(path, 'utf8') === text;
}
