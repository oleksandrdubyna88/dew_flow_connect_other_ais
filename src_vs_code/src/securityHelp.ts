export const SECURITY_HELP = {
  id: 'security-lane',
  en: {
    title: 'Security lane: extra reviewer and prompt pairs',
    whatItIs: 'An optional set of security reviews beside ordinary code and feature reviews. Each pair selects an existing reviewer and a redteam prompt. It is off by default and requires an ordinary gate.',
    why: 'A targeted prompt can examine a security boundary separately while preserving its findings and evidence in the same review history.',
    setup: 'Open Settings → Security lane. Enable the lane, tick the desired checks for each reviewer, and choose stages, source mode and context token budget. Twelve presets cover authorization, SQL, concurrency, auth tokens, SSRF, webhooks, files, commands, deserialization, secrets, prompt injection and XSS. Presets require matching code conditions; an empty trigger list refuses their execution. Only custom prompts can run unconditionally with empty triggers. Focus prioritizes source. The defaults are two rounds and zero allowed major/blocking findings. Shipped prompts are versioned in src_mcp/src/prompts/redteam-*.md. Edit local prompt override opens an optional file in your data directory; it does not change the Git default.',
    usage: 'The lane shares existing concurrency limits, keeps a separate identity for each pair, and continues until its round budget is spent. A reviewer row may be enabled only for security. Reproduction evidence appears in the rounds log and is never executed. A major/blocking finding without complete reproduction evidence is capped at minor before findings are merged.',
    whatCanGoWrong: 'An older MCP server cannot run the lane; update to 0.41.0 or later. Missing prompt text, unsupported Team runtime, invalid configuration and failed reviews are reported. Malformed settings are preserved under invalidConfiguration: correct that object in settings JSON and replace coai.securityLane with it. Context is bounded and omissions are named. Local input coverage remains unverified even if a model answers successfully. A silent lane does not certify a security review, and a successful lane answer cannot replace a failed ordinary reviewer.',
  },
};
