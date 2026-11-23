import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * `shell/ids.ts`, read as data: `src/` is compiled with `rootDir` = `src`, so a test cannot import the shell's TypeScript, and it must not copy the lists —
 * the lists are what these tests hold the rest of the tree to.
 */
export function ids(): { readonly CHAT_COMMANDS: readonly string[]; readonly INSTALL_COMMANDS: readonly string[]; readonly COMMANDS: readonly string[]; readonly MODULE_CHAT: string; readonly MODULE_INSTALL: string; readonly SHELL_KEY_NAME: string } {
  const text = readFileSync(join(process.cwd(), 'shell', 'ids.ts'), 'utf8');
  const list = (name: string): readonly string[] => {
    const body = new RegExp(`export const ${name}: readonly string\\[\\] = \\[([\\s\\S]*?)\\];`, 'u').exec(text)?.[1] ?? '';

    return [...body.matchAll(/'([^']+)'/gu)].map((one) => one[1] ?? '');
  };
  const one = (name: string): string => new RegExp(`export const ${name} = '([^']+)'`, 'u').exec(text)?.[1] ?? '';

  return { CHAT_COMMANDS: list('CHAT_COMMANDS'), INSTALL_COMMANDS: list('INSTALL_COMMANDS'), COMMANDS: list('COMMANDS'), MODULE_CHAT: one('MODULE_CHAT'), MODULE_INSTALL: one('MODULE_INSTALL'), SHELL_KEY_NAME: one('SHELL_KEY_NAME') };
}
