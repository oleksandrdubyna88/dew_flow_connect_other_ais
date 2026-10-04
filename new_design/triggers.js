/* ConnectOtherAIs settings mockup: how a change is matched to security checks.
   A copy of coai-mcp's SecuritySignals (src_mcp/core/Security/SecuritySignals.cs, read at mcp 0.42.0): each
   signal is found by lexical WORDS in a changed file's path and text (case-insensitive), `sql` also by
   statement SHAPES. A preset runs when any changed file carries one of its triggers; a custom prompt with no
   trigger runs on every change; a preset with none never runs. The words are compiled into the server today —
   editing them here is new in this design and needs a server setting behind it. Plain script. */
(function () {
  'use strict';

  const WORDS = {
    sql: ['sql', 'dbcontext', 'dbconnection', 'dbcommand', 'migrationbuilder', 'dapper'],
    'auth-token': ['jwt', 'bearer', 'cookie', 'session', 'authenticate', 'oauth', 'openid', 'oidc', 'pkce', 'tokenvalidationparameters', 'validateissuersigningkey', 'redirect_uri', 'client_secret'],
    oauth: ['oauth', 'openid', 'oidc', 'pkce', 'redirect_uri', 'redirecturi', 'code_verifier', 'code_challenge', 'authorization_code', 'acquiretoken', 'msal'],
    authz: ['authorize', 'authorization', 'permission', 'tenant', 'role', 'allowanonymous', 'mapget', 'mappost', 'mapput', 'mapdelete', 'httppost', 'httpget', 'controller', 'route(', 'user.claims', 'companyid', 'frombody', 'dbcontext.update', '.updateasync', 'patch', 'endpoint'],
    xss: ['innerhtml', 'outerhtml', 'dangerouslysetinnerhtml', 'document.write', 'webview', '<script', 'markupstring', 'htmlstring', 'response.writeasync', 'v-html'],
    ssrf: ['httpclient', 'httprequestmessage', 'fetch(', 'axios', 'webrequest', 'restsharp', 'requests.get', 'urllib', 'redirect', 'url'],
    path: ['path.', 'file.', 'directory.', 'readfile', 'writefile', 'extract', 'archive'],
    upload: ['iformfile', 'multipart', 'uploadedfile', 'uploadfile', 'fileupload', 'multer', 'formdata', 'request.files', 'request.form.files'],
    command: ['process', 'exec(', 'execfile', 'spawn(', 'shell', 'subprocess', 'cmd.exe', '/bin/sh'],
    deserialize: ['deserialize', 'pickle', 'yaml.load', 'binaryformatter', 'json.parse', 'typenamehandling', 'dtdprocessing', 'type.gettype'],
    secrets: ['secret', 'password', 'credential', 'apikey', 'api_key', 'connectionstring', 'ilogger', 'loginformation', 'logerror', 'bearer'],
    crypto: ['encrypt', 'decrypt', 'sha1', 'md5', 'random', 'cryptograph', 'cipher'],
    concurrency: ['stripe', 'paymentintent', 'rowversion', 'dbupdateconcurrencyexception', 'balance', 'credit', 'transactionscope', 'semaphoreslim', 'lock (', 'lock('],
    webhooks: ['webhook', 'stripe-signature', 'x-hub-signature', 'hmacsha256', 'fixedtimeequals', 'crypto.createhmac', 'timestamp'],
    'prompt-injection': ['ichatclient', 'kernel', 'openaiclient', 'anthropic', 'tooldefinition', 'system_prompt', 'user_input'],
    'entry-point': ['mapget', 'mappost', 'controller', 'endpoint', 'handler', 'route', 'main('],
  };

  /* The SqlCode pattern, ported: query/execute calls and statement shapes. Ordinary words are not SQL. */
  const SQL_SHAPES = /\b(?:(?:query(?:first|single|multiple)?(?:ordefault)?|execute(?:reader|nonquery|scalar)?)(?:async)?\s*(?:<[^>\r\n]{1,160}>)?\s*\(|select\s+[\s\S]{1,256}?\s+from\b|insert\s+into\b|update\s+\S+\s+set\b|delete\s+from\b|(?:create|alter|drop)\s+table\b)/i;
  const SQL_SHAPES_TEXT = 'SELECT … FROM · INSERT INTO · UPDATE … SET · DELETE FROM · CREATE / ALTER / DROP TABLE · query(…) / execute(…) calls';

  /* overrides: { [signal]: { added: [], removed: [] } } — what the person changed against the shipped words. */
  function wordsOf(signal, overrides) {
    const o = overrides[signal] || { added: [], removed: [] };
    return [...WORDS[signal].filter((w) => !o.removed.includes(w)), ...o.added];
  }

  /* Which signals a piece of text carries, and the first word (or shape) that found each. */
  function detect(text, overrides) {
    const lower = text.toLowerCase();
    const found = [];
    Object.keys(WORDS).forEach((signal) => {
      const word = wordsOf(signal, overrides).find((w) => lower.includes(w.toLowerCase()));
      if (word) found.push({ signal, by: word });
      else if (signal === 'sql' && SQL_SHAPES.test(text)) found.push({ signal, by: text.match(SQL_SHAPES)[0].slice(0, 40) });
    });
    return found;
  }

  /* A check's own word: a literal (case-insensitive, spaces and code kept), or /pattern/flags. */
  const REGEX = /^\/(.+)\/([a-z]*)$/s;

  function validPattern(word) {
    const m = word.match(REGEX);
    if (!m) return true;
    try { new RegExp(m[1], m[2]); return true; } catch { return false; }
  }

  function patternMatches(word, text) {
    const m = word.match(REGEX);
    if (!m) return text.toLowerCase().includes(word.toLowerCase());
    try { return new RegExp(m[1], m[2].includes('i') ? m[2] : `${m[2]}i`).test(text); } catch { return false; }
  }

  /* What would run, by the server's rule plus a check's own words (new): any ticked signal found, or any own
     word in the text. Nothing ticked and no own word: your prompt runs always, a shipped one never. */
  function wouldRun(signals, prompts, text) {
    const ids = new Set(signals.map((s) => s.signal));
    return prompts.filter((p) => {
      const own = p.patterns || [];
      if (p.always || (!p.triggers.length && !own.length)) return p.always || !p.shipped;
      return p.triggers.some((t) => ids.has(t)) || own.some((w) => patternMatches(w, text));
    });
  }

  window.COAI_TRIGGERS = { WORDS, SQL_SHAPES_TEXT, wordsOf, detect, wouldRun, validPattern };
})();
