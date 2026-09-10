import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const LIMIT = 256 * 1024;
export const SOURCE = '.agents/conventions/common/coai-review-gate.md';
export const OUTPUT = 'src_vs_code/src/generated/gateRule.ts';

/** Strip delivery metadata only; separators in the instruction body remain verbatim. */
export function gateBody(source) {
  const text = source.replaceAll('\r\n', '\n');
  if (!text.startsWith('---\n')) { throw new Error(`${SOURCE}: leading metadata is required`); }
  const end = text.indexOf('\n---\n', 4);
  if (end < 0) { throw new Error(`${SOURCE}: unterminated leading metadata`); }
  const body = text.slice(end + 5);
  if (!/^<!-- coai-snippet v\d+ -->\n## Multi-model review gate \(ConnectOtherAIs\)/.test(body)) {
    throw new Error(`${SOURCE}: missing canonical gate marker`);
  }
  return body;
}

function removeOutput(file) {
  try { fs.unlinkSync(file); } catch (error) { if (error.code !== 'ENOENT') { throw error; } }
}

function boundedSource(file) {
  const descriptor = fs.openSync(file, 'r');
  try {
    const buffer = Buffer.alloc(LIMIT + 1);
    let length = 0;
    for (;;) {
      const read = fs.readSync(descriptor, buffer, length, buffer.length - length, null);
      length += read;
      if (length > LIMIT) { throw new Error(`${SOURCE}: exceeds 256 KiB`); }
      if (!read) { return buffer.subarray(0, length).toString('utf8'); }
    }
  } finally { fs.closeSync(descriptor); }
}

/** Every build invalidates its previous output before verifying the pinned canonical source. */
export function prepareGate(repo) {
  const output = path.join(repo, OUTPUT);
  const temporary = output + '.tmp';
  removeOutput(output);
  removeOutput(temporary);
  const resolver = path.join(repo, '.agents/conventions/tools/rules.mjs');
  if (!fs.existsSync(resolver)) {
    throw new Error('Missing conventions resolver. Run git submodule update --init .agents/conventions, then npm ci --ignore-scripts --prefix .agents/conventions.');
  }
  execFileSync(process.execPath, [resolver, 'check', '--repo', repo], {
    encoding: 'utf8', timeout: 30_000, maxBuffer: 1024 * 1024, windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const body = gateBody(boundedSource(path.join(repo, SOURCE)));
  fs.mkdirSync(path.dirname(output), { recursive: true });
  try {
    fs.writeFileSync(temporary, '// Generated from pinned conventions; do not edit.\nexport const GATE_RULE = '
      + JSON.stringify(body) + ';\n', { flag: 'wx' });
    fs.renameSync(temporary, output);
  } finally { removeOutput(temporary); }
  return body;
}

const here = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === here) {
  try {
    prepareGate(path.resolve(path.dirname(here), '../..'));
  } catch (error) {
    console.error(`prepare-gate: ${error.message}`);
    process.exitCode = 1;
  }
}
