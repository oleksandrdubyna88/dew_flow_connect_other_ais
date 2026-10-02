# Conditional security prompt catalog

The twelve presets follow the operator's module specification. A checked pairing runs only
when its trigger matches committed code or a changed path, including removed lines. These are bounded
lexical routing heuristics, not vulnerability findings or a complete static analysis.
Enabling an unconfigured lane selects the authorization module for the first enabled reviewer.
It still requires a matching controller/endpoint/authorization signal. An explicit checkbox change
can disable that pair. Saved reviewer/prompt selections are preserved when re-enabling the lane.

Author the bodies in `src_mcp/src/prompts/` and commit them. Replace the entire `OPERATOR:` comment;
a comment-only placeholder is unavailable, never a successful empty review. The MCP appends its JSON
schema with the four reproduction fields. Optional private overrides use the same filename under
`<dataDir>/prompts/` and are not copied into Git automatically.

| File | Review subject | Default trigger | Examples of routing evidence |
|---|---|---|---|
| `redteam-authz.md` | BOLA/IDOR, tenants, mass assignment, mutation permissions | `authz` | Controllers/endpoints, Authorize, User.Claims, TenantId, CompanyId, FromBody, UpdateAsync, Patch |
| `redteam-sql.md` | SQL injection | `sql` | SQL files, raw queries, database calls |
| `redteam-concurrency.md` | Races, TOCTOU, billing, lost updates, transaction isolation | `concurrency` | Stripe, PaymentIntent, RowVersion, balance, credit, TransactionScope, SemaphoreSlim, lock |
| `redteam-auth-tokens.md` | OAuth/OIDC, PKCE, JWT validation and sessions | `auth-token`, `oauth` | AddOAuth, AddOpenIdConnect, AddJwtBearer, TokenValidationParameters, redirect_uri, client_secret |
| `redteam-ssrf.md` | Server-side request forgery | `ssrf` | HTTP clients, URL fetches and redirects |
| `redteam-webhooks.md` | Webhook signatures, timing and replay protection | `webhooks` | webhook, Stripe-Signature, X-Hub-Signature, HMACSHA256, FixedTimeEquals, timestamp |
| `redteam-files.md` | Path traversal, Zip-slip, uploads and overwrites | `path`, `upload` | Path.Combine, File.Open, Directory, archives, IFormFile, Request.Form.Files |
| `redteam-command.md` | OS command injection | `command` | Process creation, shell, exec and spawn |
| `redteam-deserialize.md` | Unsafe polymorphic deserialization and XXE | `deserialize` | TypeNameHandling, BinaryFormatter, DtdProcessing, pickle, YAML, Type.GetType |
| `redteam-secrets.md` | Secret disclosure | `secrets` | Credential, password, API-key and connection-string handling |
| `redteam-prompt-injection.md` | Direct/indirect prompt injection and tool misuse | `prompt-injection` | IChatClient, Kernel, OpenAIClient, Anthropic, ToolDefinition, system_prompt, user_input |
| `redteam-xss.md` | Cross-site scripting | `xss` | MarkupString, HtmlString, innerHTML, webviews, Response.WriteAsync, v-html |

`redteam-general.md` is retained as an additional custom prompt, outside these twelve. Register its ID in
the prompt library to pair it. Custom prompts may explicitly use an empty trigger list; presets may
not. An absent signal means only that this detector did not find its lexical evidence in the bounded
change. Binary files, credential files and material outside the context budget are not fully reviewed.

Each operator-authored body should define its narrow review role and antipattern checklist, ignore
formatting/naming/code style, and require Trigger, Mechanism and Consequence evidence. A clean answer
must use the agreed structured SECURE status. The transport contract is composed by the MCP; prompt
authors should not replace it with an incompatible prose-only answer.

This selection is the operator's engineering choice, not a claim of an exact popularity ranking.
It follows the application risk families in [OWASP Top 10](https://top10.owasp.org/2025/0x00_2025-Introduction/)
and the source inspection areas in [OWASP Secure Code Review](https://cheatsheetseries.owasp.org/cheatsheets/Secure_Code_Review_Cheat_Sheet.html).
OAuth selection includes the protocol controls discussed in the [OAuth2 cheat sheet](https://cheatsheetseries.owasp.org/cheatsheets/OAuth2_Cheat_Sheet.html);
the upload surface is described in the [File Upload cheat sheet](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html).

Detector metadata has one source, `shared/security-lane.json`; the C# matchers live in
`src_mcp/core/Security/SecuritySignals.cs`. The reviewed model decides whether a real defect exists.
Reproduction text is evidence and is never executed by the MCP.

Source/reuse review, 2026-10-02: the existing `security-reliability.md` and `sec-attack.md`
already require concrete inputs, consequences and changed-code scope. The lane reuses that
discipline and the existing prompt loader/runtime; their broad ordinary roles cannot provide
independent conditional checkboxes. The operator supplied the specialized bodies and later
authorized shorter experimental variants. The four OWASP sources above were rechecked: they
support access-control/injection risk families, source-level authorization checks, OAuth PKCE
and redirect handling, and upload filename/type/storage checks. They guide inspection areas;
they do not validate our lexical detectors, rank these twelve presets, or qualify model accuracy.
