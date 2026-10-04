// Generated from shared/security-lane.json; run scripts/generate-security-lane.mjs.
export const SECURITY_SEED = {
  "signals": [
    {
      "id": "sql",
      "label": "SQL and raw queries",
      "trigger": true
    },
    {
      "id": "auth-token",
      "label": "Tokens and sessions",
      "trigger": true
    },
    {
      "id": "oauth",
      "label": "OAuth and OpenID Connect",
      "trigger": true
    },
    {
      "id": "authz",
      "label": "Authorization and ownership",
      "trigger": true
    },
    {
      "id": "xss",
      "label": "HTML and script injection",
      "trigger": true
    },
    {
      "id": "ssrf",
      "label": "Outbound requests",
      "trigger": true
    },
    {
      "id": "path",
      "label": "Paths, files and archives",
      "trigger": true
    },
    {
      "id": "upload",
      "label": "File uploads and multipart data",
      "trigger": true
    },
    {
      "id": "command",
      "label": "Processes and shell commands",
      "trigger": true
    },
    {
      "id": "deserialize",
      "label": "Deserialization",
      "trigger": true
    },
    {
      "id": "secrets",
      "label": "Credentials and disclosure",
      "trigger": true
    },
    {
      "id": "crypto",
      "label": "Cryptography and randomness",
      "trigger": true
    },
    {
      "id": "concurrency",
      "label": "Races, payments and transaction isolation",
      "trigger": true
    },
    {
      "id": "webhooks",
      "label": "Webhook signatures and replay",
      "trigger": true
    },
    {
      "id": "prompt-injection",
      "label": "LLM prompts and tool calling",
      "trigger": true
    },
    {
      "id": "entry-point",
      "label": "Entry points and middleware",
      "trigger": false
    }
  ],
  "prompts": [
    {
      "id": "redteam-general",
      "always": true,
      "triggers": [],
      "focus": [
        "entry-point"
      ]
    },
    {
      "id": "redteam-authz",
      "triggers": [
        "authz"
      ],
      "focus": [
        "authz",
        "entry-point",
        "sql"
      ]
    },
    {
      "id": "redteam-sql",
      "triggers": [
        "sql"
      ],
      "focus": [
        "sql",
        "entry-point"
      ]
    },
    {
      "id": "redteam-concurrency",
      "triggers": [
        "concurrency"
      ],
      "focus": [
        "concurrency",
        "sql",
        "entry-point"
      ]
    },
    {
      "id": "redteam-auth-tokens",
      "triggers": [
        "auth-token",
        "oauth"
      ],
      "focus": [
        "auth-token",
        "oauth",
        "entry-point"
      ]
    },
    {
      "id": "redteam-ssrf",
      "triggers": [
        "ssrf"
      ],
      "focus": [
        "ssrf",
        "entry-point"
      ]
    },
    {
      "id": "redteam-webhooks",
      "triggers": [
        "webhooks"
      ],
      "focus": [
        "webhooks",
        "crypto",
        "entry-point"
      ]
    },
    {
      "id": "redteam-files",
      "triggers": [
        "path",
        "upload"
      ],
      "focus": [
        "path",
        "upload",
        "entry-point"
      ]
    },
    {
      "id": "redteam-command",
      "triggers": [
        "command"
      ],
      "focus": [
        "command",
        "entry-point"
      ]
    },
    {
      "id": "redteam-deserialize",
      "triggers": [
        "deserialize"
      ],
      "focus": [
        "deserialize",
        "entry-point"
      ]
    },
    {
      "id": "redteam-secrets",
      "triggers": [
        "secrets"
      ],
      "focus": [
        "secrets",
        "auth-token",
        "crypto"
      ]
    },
    {
      "id": "redteam-prompt-injection",
      "triggers": [
        "prompt-injection"
      ],
      "focus": [
        "prompt-injection",
        "entry-point"
      ]
    },
    {
      "id": "redteam-xss",
      "triggers": [
        "xss"
      ],
      "focus": [
        "xss",
        "entry-point"
      ]
    }
  ]
} as const;
