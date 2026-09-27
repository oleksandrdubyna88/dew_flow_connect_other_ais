/**
 * What `coai-mcp --providers` of this branch printed as `api` for three rows (2026-09-27): `qwen3.8-max` on
 * `dashscope`, `grok-4.7` on `xai` with a refused `thinking: false` and a 30-minute limit, and an unmeasured
 * model on the generic row — copied from the binary's answer, never typed from a reading of the C# records.
 *
 * <p>A module of its own rather than exports of a test file: importing a `.test` module from another one
 * registers its tests a second time in the importing process.</p>
 */

export const QWEN_ANSWER = {
  module: 'qwen', measuredModel: 'qwen3.8-max', priceRoute: 'dashscope',
  capabilities: { thinkingSwitchable: true, effortLevels: ['low', 'medium', 'xhigh'], effortExcludesThinkingBudget: true, thinkingOffLevel: 'none' },
  defaults: { effort: 'medium', thinkingOn: true, maxTokens: 65536, followUps: 3, reviewMinutes: 20 },
  effective: { effort: 'medium', thinkingOn: true, maxTokens: 65536, followUps: 3, reviewMinutes: 20 },
  refusal: '', note: '',
};

export const XAI_ANSWER = {
  module: 'xai', measuredModel: 'grok-4.7', priceRoute: 'xai',
  capabilities: { thinkingSwitchable: false, effortLevels: ['low', 'medium', 'high', 'xhigh'], effortExcludesThinkingBudget: false, thinkingOffLevel: '' },
  defaults: { effort: 'medium', thinkingOn: true, maxTokens: 8192, followUps: 3, reviewMinutes: 20 },
  effective: { effort: 'medium', thinkingOn: false, maxTokens: 8192, followUps: 3, reviewMinutes: 30 },
  refusal: 'xai has no thinking switch — the vendor documents no way to turn reasoning off for this family',
  note: '',
};

export const GENERIC_ANSWER = {
  module: 'openai', measuredModel: '', priceRoute: '',
  capabilities: { thinkingSwitchable: false, effortLevels: [], effortExcludesThinkingBudget: false, thinkingOffLevel: '' },
  defaults: { effort: '', thinkingOn: true, maxTokens: 8192, followUps: 3, reviewMinutes: 20 },
  effective: { effort: '', thinkingOn: true, maxTokens: 8192, followUps: 3, reviewMinutes: 20 },
  refusal: '', note: '',
};
