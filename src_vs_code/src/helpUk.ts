import { HelpBody } from './helpContent';

/**
 * The help catalog in Ukrainian.
 *
 * <p>A file per language: a translation pass is then reviewable on its own, and eighteen articles
 * in five languages inline would be one literal nobody could edit without breaking a quote. A key
 * that is missing here falls back to the English body, visibly — see `bodyFor`.</p>
 */
export const UK: Readonly<Record<string, HelpBody>> = {};
