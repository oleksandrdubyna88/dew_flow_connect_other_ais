// GENERATED FILE — do not edit by hand.
//
// Written by `node scripts/generate-builtin-roles.mjs` from `shared/builtin-roles.json`, the seed
// coai-mcp embeds. Edit the seed and run the script; `builtinRoleCatalog.test.ts` fails if this
// file and the seed disagree.

import type { RoleDefinition } from './prompts';

/** The roles this product ships, in the order a round runs them and the panel draws them. */
export const BUILTIN_ROLES: readonly RoleDefinition[] = [
  {
    id: 'PlanCritique',
    name: 'Plan review',
    stage: 'plan',
    programmingTask: true,
    prompts: [
      { id: 'plan-critique', label: 'Universal', purpose: 'The whole plan: assumptions, failure paths, order, testability.' },
      { id: 'plan-assumptions', label: 'Assumptions & verification', purpose: 'What the plan takes for granted, and what it promises but never checks.' },
      { id: 'plan-human-path', label: 'The human path', purpose: 'What a person does with it, and what happens when they do it wrong.' },
      { id: 'plan-data-loss', label: 'Data loss & recovery', purpose: 'What the plan destroys, overwrites or moves — and whether a failure halfway can be undone.' },
      { id: 'plan-operability', label: 'Operability', purpose: 'What it is like to run this at 3 a.m.: what is observable, what is alertable, what is diagnosable.' },
      { id: 'plan-scope-creep', label: 'Scope & budget', purpose: 'What this plan quietly takes on beyond its goal, and what it will cost to keep.' },
    ],
  },
  {
    id: 'Conventions',
    name: 'Conventions',
    stage: 'result',
    programmingTask: true,
    prompts: [
      { id: 'conventions', label: 'Conventions', purpose: 'Only the rules this project wrote down — CLAUDE.md, AGENTS.md, GEMINI.md, .claude/rules. A convention the reviewer believes in but the project never wrote is not a finding.' },
    ],
  },
  {
    id: 'Architecture',
    name: 'Architecture',
    stage: 'result',
    programmingTask: true,
    prompts: [
      { id: 'architecture', label: 'Universal', purpose: 'Boundaries, abstractions, consistency, and the plan-to-code gap.' },
      { id: 'arch-boundaries', label: 'Boundaries & duplication', purpose: 'Dependency direction, layers reaching around each other, capabilities implemented twice.' },
      { id: 'arch-evolution', label: 'Cost of the next change', purpose: 'What this change makes harder, and what is hard-coded that will have to vary.' },
      { id: 'arch-coupling', label: 'Coupling & knowledge', purpose: 'What this change makes one part know about another, and what breaks when either moves.' },
      { id: 'arch-naming', label: 'Names & the shape they imply', purpose: 'Where a name promises a shape the code does not have — the misreading it invites next.' },
      { id: 'arch-testability', label: 'Testability of the seams', purpose: 'Which decision here can only be tested by starting a server, a browser or a clock.' },
    ],
  },
  {
    id: 'SecurityReliability',
    name: 'Security & reliability',
    stage: 'result',
    programmingTask: true,
    prompts: [
      { id: 'security-reliability', label: 'Universal', purpose: 'Secrets, input, failure behaviour, state, trust boundaries.' },
      { id: 'sec-memory-leaks', label: 'What it holds and leaves', purpose: 'Secrets that outlive their use, resources leaked on the error path, what a kill -9 leaves behind.' },
      { id: 'sec-attack', label: 'Attack surface', purpose: 'What is trusted that was never checked, injection, privilege, and checks that fail open.' },
      { id: 'sec-blast-radius', label: 'Blast radius', purpose: 'If this one thing is wrong or compromised, how far does it reach before anything stops it.' },
      { id: 'sec-concurrency', label: 'Two at once', purpose: 'The same code running twice, a millisecond apart, over the state they share.' },
      { id: 'sec-supply-chain', label: 'What this change trusts', purpose: 'Every input, dependency and endpoint it believes without checking — and who can change them.' },
    ],
  },
  {
    id: 'UxDxPerformance',
    name: 'Performance & UX-DX',
    stage: 'result',
    programmingTask: true,
    prompts: [
      { id: 'uxdx-performance', label: 'Universal', purpose: 'Performance, UI state as code, and the ergonomics of a new API.' },
      { id: 'perf-scale', label: 'Cost at scale', purpose: 'Which input grows, and what this code does when it does.' },
      { id: 'dx-ergonomics', label: 'Ergonomics & waiting', purpose: 'Names that mislead, errors that name no cure, and work a person waits on.' },
      { id: 'perf-first-run', label: 'The first run and the empty case', purpose: 'A brand-new machine, no cache, no config, nothing yet: the first thirty seconds, narrated.' },
      { id: 'perf-wasted-work', label: 'Work done twice', purpose: 'What this recomputes, refetches or re-renders that it already had.' },
      { id: 'ux-undo', label: 'What cannot be taken back', purpose: 'Using it wrongly on purpose: what state that leaves, and how somebody gets back.' },
    ],
  },
];
