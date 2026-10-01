/**
 * One entry a model dropdown offers: the id a CLI or endpoint is handed, and the words a person reads.
 *
 * <p>Its own module so the two places that build lists — `models.ts` and `endpointModels.ts` — can both
 * name it without importing each other: a type-only import still counts as an edge for
 * `importCycles.test.mjs`, and that pair would be a new ring. `models.ts` re-exports it, so every existing
 * importer that names it through the models module keeps working. (No import is quoted here: the scanner
 * reads comments too.)</p>
 */
export interface ModelChoice {
  readonly id: string;
  readonly label: string;
}
