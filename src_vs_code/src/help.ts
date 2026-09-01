/**
 * What each setting actually means, in a sentence a person can act on.
 *
 * <p>Written because "Per vendor" explains nothing: a label is a reminder for someone who already
 * knows, and every one of these was chosen by somebody who did not yet. Each entry says what the
 * setting does AND why it exists — a number whose purpose is invisible gets set to whatever looks
 * biggest.</p>
 *
 * <p>Kept apart from the markup so the wording is reviewable on its own, and so a missing entry is
 * a compile error rather than a blank tooltip.</p>
 */
export const HELP = {
  vendorEnabled:
    'Whether this reviewer takes part. Switching one off keeps its settings — the next round simply runs without it.',
  vendorModel:
    "Which model this vendor reviews with. Empty means the CLI's own default, which is usually its newest. A stronger model finds more and costs more; the panel exists so you can mix.",
  vendorBaseUrl:
    'The OpenAI-compatible endpoint this vendor is reached at. Its API key lives in the CredsForDevs config entry under this vendor’s name, never here.',
  vendorExecutablePath:
    'Where this vendor’s CLI is, for when PATH cannot be trusted to answer. Empty means "look it up on PATH", which is right almost everywhere — and wrong in WSL, where codex and gemini resolve to the WINDOWS npm shims through the interop PATH and die on a missing Linux binary. Put the native path here and the round stops depending on which shell happened to start the server.',

  runVendor:
    'Open this vendor’s own CLI in a terminal, with its usage command ready at the prompt — press Enter to see what you have spent. This is also where you sign a CLI in: a vendor whose CLI is not authenticated fails every round with a timeout.',

  vendorPrice:
    'What this vendor bills per million tokens, in and out. From YOU, never from a table this product ships: a shipped price list is wrong for anyone on a flat subscription, wrong the first time a vendor changes a price, and wrong silently in both cases. Only Claude reports its own cost; codex and antigravity report tokens and nothing else, so their money read as a dash until you fill these in. What is computed from your rate is marked with a tilde — ~$0.42 is what the tokens work out to, $0.42 is what a vendor actually charged, and the totals keep the two apart.',

  installVendorCli:
    'Install this vendor’s CLI. It opens a terminal with the exact command typed and waiting, picked for the OS the terminal will actually run in — PowerShell on Windows, the shell in your distribution when VS Code is attached to WSL — plus how to get node first if this machine has none. Only the vendors’ own published sources: npm for Codex, Google’s own script for Antigravity. A fresh WSL box has none of these CLIs, and the answer being on somebody else’s docs page is why a reviewer never gets added.',

  addVendor:
    'Add another independent reviewer: a preset, or any OpenAI-compatible endpoint by name and URL. More vendors means more independent eyes — and more cost per round.',

  maxRounds:
    'How many times THIS ROLE may be asked before the policy below takes over. Each role has its own count, because they are not worth the same number of passes: architecture may deserve two with different lenses while performance deserves one, and a shared budget makes the cheapest role pay for the most expensive. A round runs the roles that still have a count left — all of them, not only the ones that gated, because the next round reads a REVISED diff and a role that was clean on the old one can find something in the fix. When a role’s count is spent it simply stops being asked, and the stage keeps going for the roles that have not.',
  gateThreshold:
    'The gate opens for THIS ROLE when this many of its findings are left, or fewer — a finding is counted against the threshold of the role that raised it, so a noisy role cannot spend another role’s tolerance. Only blocking and major findings count, and the same defect raised by two vendors counts once. Zero demands a clean review from this role; two tolerates a couple of disagreements. The stage passes when every role is at or under its own number.',
  onExhausted:
    'What happens when the rounds run out and findings still gate. Ask a human: the review stops and waits for you. Continue: it proceeds and says out loud what is still open, touching none of them. Good enough: the AI reads what is open, applies the findings that are true and useful, rejects the rest with reasons, and moves on. Climb the ladder: raise the reviewers’ effort, then their model, then the arbiter’s — and try again.',

  maxConcurrency:
    'How many reviewer processes may run at the same time, across every vendor. Higher finishes a round sooner and loads the machine harder; each one is a full CLI.',
  maxPerProvider:
    'Of those, how many may belong to ONE vendor. Rate limits are per vendor: without this cap a single slow or throttled vendor would hold every slot and the others would wait behind it.',
  reviewerTimeout:
    'How long one reviewer may take before its process is killed and the round records a timeout for it. A round with a missing reviewer still produces a verdict, and says who was missing.',
  escalationMinutes:
    'How long a question waits for your answer before the AI is told to ask you in the chat instead. The question stays open in this panel either way — nothing is decided by your silence.',

  credsKey:
    'The CredsForDevs config-entry key that unlocks the vendor API keys. It is a pass to one vault entry — revocable, and useless while VS Code is closed — not a secret itself. Vendors whose CLI is signed in need no key at all.',
} as const;

export type HelpKey = keyof typeof HELP;
