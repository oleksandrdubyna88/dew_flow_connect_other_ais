import { readFile, writeFile } from 'node:fs/promises';
const seed = JSON.parse(await readFile(new URL('../../shared/security-lane.json', import.meta.url), 'utf8'));
await writeFile(new URL('../src/securityLane.generated.ts', import.meta.url),
  '// Generated from shared/security-lane.json; run scripts/generate-security-lane.mjs.\n'
  + `export const SECURITY_SEED = ${JSON.stringify(seed, null, 2)} as const;\n`);
