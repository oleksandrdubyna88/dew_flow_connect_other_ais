#!/bin/bash
# usage: mkcxconfig.sh <variant> ; writes $CODEX_HOME/config.toml. variants: base | coai (restricted-read permission profile)
S=${E3S4_SCRATCH:?set E3S4_SCRATCH to your scratch folder}
H=$S/copies/codex/home; PD=$(dirname $(readlink -f $HOME/.local/bin/codex))
cat > $H/config.toml <<EOT
model = "gpt-6-luna"
model_reasoning_effort = "low"
EOT
if [ "$1" = coai ]; then cat >> $H/config.toml <<EOT
default_permissions = "coai"

[permissions.coai.filesystem]
":minimal" = "read"
"$PD" = "read"
":project_roots" = { "." = "write" }
EOT
fi
chmod 600 $H/config.toml
