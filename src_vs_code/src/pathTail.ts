import { asText } from './asText';

/**
 * The last segment of a path, whichever separator it was written with — a repository's folder name, a
 * plan's file name.
 *
 * <p>A leaf module, moved out of `roundsLog.ts` when the sidebar's cadence line needed it
 * (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.2): importing it from there would have made
 * `panelView` → `cadenceLine` → `roundsLog` → `panelView` a cycle. `roundsLog` re-exports it.</p>
 */
export function repoNameOf(repoPath: string): string {
  // Coerced for the reason the escapers are: this reads `session.state.repoPath` straight out of a
  // JSON file that nothing validates, and `.replace` on a number is the error that stopped a person
  // opening the log on 2026-09-08 — at the moment a question was waiting on them.
  const text = asText(repoPath);
  const parts = text.replace(/\\/g, '/').replace(/\/+$/, '').split('/');
  return parts[parts.length - 1] ?? text;
}
