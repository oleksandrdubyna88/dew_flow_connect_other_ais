CREATE TABLE IF NOT EXISTS quarantine (
    entry_id        TEXT PRIMARY KEY,
    language        TEXT NOT NULL,
    skeleton_before TEXT NOT NULL,
    skeleton_after  TEXT NOT NULL,
    received_utc    TEXT NOT NULL,
    -- WHICH key sent it, never who holds the key. It is here so one contributor's mistake
    -- can be undone in bulk without touching anybody else's work.
    key_id          TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS corpus (
    entry_id        TEXT PRIMARY KEY,
    language        TEXT NOT NULL,
    skeleton_before TEXT NOT NULL,
    skeleton_after  TEXT NOT NULL,
    promoted_utc    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS api_keys (
    id           TEXT PRIMARY KEY,
    key_hash     TEXT NOT NULL UNIQUE,
    created_utc  TEXT NOT NULL,
    revoked_utc  TEXT NOT NULL DEFAULT '',
    -- OUR record of why a key exists, not the holder's data. "for the tuesday workshop",
    -- never a name or an address.
    note         TEXT NOT NULL DEFAULT '',
    -- A counter WITHOUT a clock, deliberately. With timestamps it would be a record of when
    -- a person we handed a key to was working. It is not a rate limit either, and the plan
    -- said so after a reviewer pointed out that a lifetime count has no window and no reset.
    submissions  INTEGER NOT NULL DEFAULT 0
);
