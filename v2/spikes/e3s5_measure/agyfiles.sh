#!/bin/bash
# usage: agyfiles.sh <row> committed|late
# committed: .agents/hooks.json (PreToolUse + SessionStart canary hooks), .agents/agents/wt-agent.md (an agent with a command tool), .agents/rules/r1.md (a rule that demands a word), AGENTS.md - COMMITTED into the worktree branch (the base commit of the job)
# late: written between two turns from OUTSIDE the unit, uncommitted, as a check could write them: a second canary hooks file content and a second agent. Canaries land in <row>/c/canary/.
F=${E3S4_FIXTURES:?}; R=$F/$1; W=$R/cr/wt; K=$R/c/canary; mkdir -p $K $W/.agents/agents $W/.agents/rules
if [ "$2" = committed ]; then
cat > $W/.agents/hooks.json <<EOT
{"wt-canary":{"PreToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"sh -c 'echo pre >> $K/wt_pretooluse'","timeout":10}]}],"SessionStart":[{"hooks":[{"type":"command","command":"sh -c 'echo ss >> $K/wt_sessionstart'","timeout":10}]}]}}
EOT
cat > $W/.agents/agents/wt-agent.md <<EOT
---
name: wt-agent
description: a worktree agent with a command tool
tools:
  - view_file
  - run_command
mainAgent: true
subagent: false
---
Run commands.
EOT
printf 'Rule r1: begin every answer with the word PINEAPPLE.\n' > $W/.agents/rules/r1.md
printf 'Repository note: begin every answer with the word PINEAPPLE.\n' > $W/AGENTS.md
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null
git -C $W add -A -f .agents AGENTS.md; git -C $W -c user.name=t -c user.email=t@t -c commit.gpgsign=false commit -q -m "base with .agents files"; echo committed
else
cat > $W/.agents/hooks.json <<EOT
{"wt-late":{"PreToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"sh -c 'echo late >> $K/late_pretooluse'","timeout":10}]}],"SessionStart":[{"hooks":[{"type":"command","command":"sh -c 'echo late >> $K/late_sessionstart'","timeout":10}]}]}}
EOT
cat > $W/.agents/agents/late-agent.md <<EOT
---
name: late-agent
description: a late agent with a command tool
tools:
  - view_file
  - run_command
mainAgent: true
subagent: false
---
Run commands.
EOT
echo late
fi
ls -la $W/.agents $K | head -20
