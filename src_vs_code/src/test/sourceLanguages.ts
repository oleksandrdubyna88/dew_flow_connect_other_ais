import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The members of `SourceLanguage`, read off its declaration in `src_mcp` — the collector's own
 * list of what it can read, which is the producer every language decision on this side is pinned
 * against.
 *
 * <p>Shared by the highlighter's test and the language table's test rather than copied into each:
 * two readers of one enum agree until one of them stops matching, and a second copy is the
 * duplication `testing.md` names. It is a positive pin on a DECLARATION, not a match on prose: the
 * enum is two projects away, and a source read is the only thing that can cross that gap in a suite
 * with no server running.</p>
 *
 * <p>The two assertions are what keep the reader honest — a regex matching nothing would otherwise
 * hand every caller an empty loop that passes in silence.</p>
 */
export function collectorLanguages(): readonly string[] {
  const enumFile = join(__dirname, '..', '..', '..', 'src_mcp', 'core', 'Normalising', 'IAstNormalizer.cs');
  const declared = /public enum SourceLanguage\s*\{([^}]*)\}/u.exec(readFileSync(enumFile, 'utf8'));
  assert.ok(declared !== null, 'SourceLanguage has moved — this reader is reading nothing');

  const members = (declared[1] ?? '')
    .split(',').map((m) => m.trim()).filter((m) => m.length > 0 && !m.startsWith('//'));
  assert.ok(members.length >= 2, 'the enum was read but no members came out of it');
  assert.ok(members.includes('Unsupported'), 'the honest "we do not read this one" value is gone');

  return members;
}
