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
  addVendor:
    'Add another independent reviewer: a preset, or any OpenAI-compatible endpoint by name and URL. More vendors means more independent eyes — and more cost per round.',

  language:
    'The language you are asked and answer in. A question the AI already wrote in this language is left exactly as it was; anything else is translated first.',
  translator:
    'Which small, fast model translates a question that is not already in your language. It is a one-sentence job in front of someone who is waiting, so a flash or mini model is the right choice. "Nobody" shows every question in the language it arrived in.',
  translatorModel:
    "The translator's model. Empty uses the CLI's own default. If the translator cannot run at all, you get the original text with the reason — never an error in its place.",

  maxRounds:
    'How many times one stage may be reviewed before the policy below takes over. There are two stages: the plan, then the code. Three is usually enough — a fourth round rarely changes a verdict.',
  gateThreshold:
    'The gate opens when this many findings are left, or fewer. Only blocking and major findings count, and the same defect raised by two vendors counts once. Zero demands a clean review; two tolerates a couple of disagreements.',
  onExhausted:
    'What happens when the rounds run out and findings still gate. Ask a human: the review stops and waits for you. Continue: it proceeds and says out loud what is still open. Climb the ladder: raise the reviewers’ effort, then their model, then the arbiter’s — and try again.',

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
