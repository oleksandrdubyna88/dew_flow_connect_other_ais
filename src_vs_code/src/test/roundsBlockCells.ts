import assert from 'node:assert/strict';

/**
 * The cells of the Rounds block's table by the header over them — a column added or moved changes nothing here, so a test
 * names WHAT it reads ('Money (7 d)'), not where it sits.
 */

/** The header labels, in order. */
export function headersOf(html: string): readonly string[] {
  return [...html.matchAll(/<th scope="col">([\s\S]*?)<\/th>/gu)].map((match) => match[1]!);
}

/** The cells of the `tr` whose markup includes `label`, keyed by the header over them. */
export function cellsOf(html: string, label: string): Readonly<Record<string, string>> {
  const headers = headersOf(html);
  const row = html.split('<tr').find((one) => one.includes(label));
  assert.ok(row !== undefined, `the block has no line named ${label}`);
  const cells = [...row.matchAll(/<td>([\s\S]*?)<\/td>/gu)].map((match) => match[1]!);

  return Object.fromEntries(headers.map((header, at) => [header, cells[at] ?? '']));
}

/** How many cells each body row of the table has — every row must have as many as the header. */
export function rowWidths(html: string): readonly number[] {
  return html.split('<tr class=').slice(1).map((row) => (row.match(/<td>/gu) ?? []).length);
}
