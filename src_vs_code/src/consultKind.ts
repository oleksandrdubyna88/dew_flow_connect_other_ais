/**
 * A consultation's kind, said the same way everywhere a person reads one: the log's table and the
 * sidebar's card (research/PLAN_consult_limits_kinds_and_help.md, story 2).
 *
 * <p>Three kinds exist. <b>stuck</b>: an AI asked because it was not getting out — the one the calls-per-
 * session cap counts. <b>cadence</b>: the gate ordered one for a group of epics. <b>risk</b>: the gate
 * ordered one for a piece the caller named as risky. Pure functions only, so the two surfaces cannot
 * word it differently.</p>
 */
import { repoNameOf } from './pathTail';
import type { DbConsultation } from './roundsDb';

/** A record from before the kinds existed was a stuck consultation: that is all there was. */
export function kindOf(kind: string): string {
  return kind.length === 0 ? 'stuck' : kind;
}

/**
 * What an ordered consultation covered — `epics 4-6`, `epic 7`, `story 7.2` — or `''` for a stuck one and
 * for a kind this build does not know, whose `epics` it cannot read.
 */
export function coveredSaid(kind: string, epics: string): string {
  // `Object.hasOwn`, because the kind is a string off a disk and a plain object answers for every name
  // it inherited.
  return epics.length > 0 && Object.hasOwn(COVERED, kind) ? COVERED[kind]!(epics) : '';
}

/** How each ordered kind this build knows names what it covered. */
const COVERED: Readonly<Record<string, (epics: string) => string>> = {
  cadence: (epics) => `${epics.includes('-') ? 'epics' : 'epic'} ${epics}`,
  risk: (epics) => {
    const story = epics.split('/')[1];
    return story === undefined ? `epic ${epics}` : `story ${story}`;
  },
};

/** One line for a card: `cadence · epics 1-3 · PLAN_x.md`, `risk · story 5.1 · PLAN_x.md`, or `stuck`. */
export function kindLine(kind: string, epics: string, plan: string): string {
  return [kindOf(kind), coveredSaid(kind, epics), plan.length > 0 ? repoNameOf(plan) : '']
    .filter((part) => part.length > 0)
    .join(' · ');
}

/** The title of the pick that records how a consultation ended — naming WHICH one, by its kind. */
export function closeTitle(kind: string, epics: string, plan: string): string {
  return `How did this consultation end? — ${kindLine(kind, epics, plan)}`;
}

/** What the record-outcome pick needs for one consultation, or `gone` when the log no longer has it. */
export type CloseTarget = { readonly kind: 'pick'; readonly repo: string; readonly title: string } | { readonly kind: 'gone' };

/**
 * The decision half of closing a consultation by hand, apart from the VS Code calls around it: which row,
 * which checkout's lock to take (THE CONSULTATION'S OWN, never this window's first folder — the log lists
 * every repository a person has reviewed), and the pick's title naming its kind. A row gone from the log,
 * or one with no checkout on it, is `gone`, said before anybody chooses an outcome.
 */
export function closeTarget(consultations: readonly DbConsultation[], id: string): CloseTarget {
  const found = consultations.find((one) => one.id === id);

  return found === undefined || found.repoPath.length === 0
    ? { kind: 'gone' }
    : { kind: 'pick', repo: found.repoPath, title: closeTitle(found.kind, found.epics, found.plan) };
}
