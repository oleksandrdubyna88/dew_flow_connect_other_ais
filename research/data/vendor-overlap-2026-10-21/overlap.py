"""Cross-vendor finding overlap in coai rounds, last 30 days, both data stores.

Usage: python -I overlap.py <db1> <db2> [since] > report.txt
Writes rounds.json (multi-vendor rounds with their findings) next to the script for judge sampling.
"""
import sqlite3, sys, re, math, json, random, itertools, collections, os

SINCE = sys.argv[3] if len(sys.argv) > 3 else '2026-09-21'
FAMILY = {
    'codex': 'openai', 'codex-2': 'openai',
    'gemini': 'google', 'antigravity': 'google', 'chat-preset-mtwxbymp-4': 'google',
    'vibe': 'mistral', 'kimi': 'moonshot',
    'qwen': 'qwen', 'qwen-2': 'qwen',
    'claude': 'anthropic', 'claude-2': 'anthropic',
    'local': 'local',
}
STOP = set("""the a an and or of to in on for is are be by with as at from this that it its not no but if
then than into when which while can will would should could does do has have had was were been being
there their they them also only any all each every one two more most other such same so very just
instead without within because before after over under about via per plan code file line method function
""".split())

def tokens(s):
    return [w for w in re.findall(r"[a-z][a-z0-9_]{2,}", s.lower()) if w not in STOP]

def load(db):
    c = sqlite3.connect('file:' + db + '?mode=ro', uri=True)
    rows = c.execute("""select r.id, r.stage, r.session_id, f.id, f.providers, f.role, f.file, f.line,
                               f.severity, f.title, f.why, f.resolution
                        from findings f join rounds r on r.id = f.round_id
                        where r.started_utc >= ? """, (SINCE,)).fetchall()
    return [dict(store=os.path.basename(os.path.dirname(db)) or db, rid=r[0], stage=r[1], fid=r[3],
                 providers=r[4].split(','), role=r[5], file=r[6].replace('\\', '/').lower(), line=r[7],
                 sev=r[8], title=r[9], why=r[10], res=r[11]) for r in rows]

findings = load(sys.argv[1]) + load(sys.argv[2])
for f in findings:
    f['fams'] = sorted({FAMILY.get(p, p) for p in f['providers']})
    f['prov'] = f['providers'][0]

# TF-IDF, title counted twice
docs = [tokens(f['title']) * 2 + tokens(f['why'])[:120] for f in findings]
df = collections.Counter(w for d in docs for w in set(d))
N = len(docs)
for f, d in zip(findings, docs):
    tf = collections.Counter(d)
    v = {w: (1 + math.log(n)) * math.log(N / df[w]) for w, n in tf.items()}
    norm = math.sqrt(sum(x * x for x in v.values())) or 1
    f['vec'] = {w: x / norm for w, x in v.items()}

def cos(a, b):
    if len(a) > len(b): a, b = b, a
    return sum(x * b.get(w, 0) for w, x in a.items())

T_TEXT, T_ANCH = float(os.environ.get('T_TEXT', 0.30)), float(os.environ.get('T_ANCH', 0.12))
def match(f, g):
    s = cos(f['vec'], g['vec'])
    anchored = f['file'] and f['file'] == g['file'] and abs(f['line'] - g['line']) <= 10
    return s >= T_TEXT or (anchored and s >= T_ANCH), s

rounds = collections.defaultdict(list)
for f in findings:
    rounds[(f['store'], f['rid'])].append(f)

def stage_of(fs): return fs[0]['stage']

stats = collections.defaultdict(lambda: [0, 0])          # (stage, family) -> [shared, total]
pair = collections.defaultdict(lambda: [0, 0])           # (famA, famB, kind) -> [matched, total] (A's findings matched in B)
same_role = collections.defaultdict(lambda: [0, 0])
accept = collections.defaultdict(lambda: [0, 0])          # shared|alone -> [accepted, resolved]
within_lens = [0, 0]                                     # same provider, different role
multi_rounds = []
for key, fs in rounds.items():
    provs = {f['prov'] for f in fs}
    fams = {fam for f in fs for fam in f['fams']}
    if len(fams) < 2 and len(provs) < 2:
        continue
    multi = len(fams) >= 2
    if multi:
        multi_rounds.append(key)
    for f in fs:
        shared = len(f['fams']) > 1
        by_prov = collections.defaultdict(bool); by_prov_role = collections.defaultdict(bool)
        for g in fs:
            if g is f or g['prov'] == f['prov']:
                if g is not f and g['prov'] == f['prov'] and g['role'] != f['role']:
                    pass
                continue
            m, _ = match(f, g)
            if m:
                by_prov[g['prov']] = True
                if g['role'] == f['role']:
                    by_prov_role[g['prov']] = True
        for q in provs - {f['prov']}:
            fa, fb = FAMILY.get(f['prov'], f['prov']), FAMILY.get(q, q)
            kind = 'same-vendor' if fa == fb else 'cross-vendor'
            pair[(fa, fb, kind)][0] += by_prov[q] or q in f['providers']
            pair[(fa, fb, kind)][1] += 1
            same_role[kind][1] += 1
            same_role[kind][0] += by_prov_role[q] or q in f['providers']
        if multi:
            cross = shared or any(by_prov[q] for q in provs if FAMILY.get(q, q) != FAMILY.get(f['prov'], f['prov']))
            for fam in [f['fams'][0]]:
                st = stats[(stage_of(fs), FAMILY.get(f['prov'], f['prov']))]
                st[0] += cross; st[1] += 1
                tot = stats[(stage_of(fs), 'ALL')]; tot[0] += cross; tot[1] += 1
            if f['res'] in ('accept', 'reject'):
                a = accept[('shared' if cross else 'alone', stage_of(fs))]
                a[0] += f['res'] == 'accept'; a[1] += 1
    # within one provider: different roles (lens control)
    byp = collections.defaultdict(list)
    for f in fs: byp[f['prov']].append(f)
    for p, pf in byp.items():
        for f in pf:
            others = [g for g in pf if g['role'] != f['role']]
            if others:
                within_lens[1] += 1
                within_lens[0] += any(match(f, g)[0] for g in others)

pct = lambda a: f"{100 * a[0] / a[1]:.1f}% ({a[0]}/{a[1]})" if a[1] else '-'
print(f"window since {SINCE}; findings loaded {len(findings)}; rounds {len(rounds)}; multi-vendor rounds {len(multi_rounds)}")
print(f"thresholds T_TEXT={T_TEXT} T_ANCH={T_ANCH}")
print("\n== share of a finding also raised by ANOTHER VENDOR in the same round (multi-vendor rounds) ==")
for k in sorted(stats): print(k, pct(stats[k]))
print("\n== pairwise: A's findings matched by provider of B (same round), aggregated ==")
for k in sorted(pair, key=lambda k: (k[2], k[0], k[1])):
    if pair[k][1] >= 100: print(k, pct(pair[k]))
agg = collections.defaultdict(lambda: [0, 0])
for k, v in pair.items(): agg[k[2]][0] += v[0]; agg[k[2]][1] += v[1]
print("\npairwise totals:", {k: pct(v) for k, v in agg.items()})
print("same role only:", {k: pct(v) for k, v in same_role.items()})
print("same provider, other role (lens control):", pct(within_lens))
print("\n== acceptance by caller ==")
for k in sorted(accept): print(k, pct(accept[k]))

random.seed(7)
json.dump([[{k: f[k] for k in ('store', 'rid', 'stage', 'fid', 'prov', 'role', 'file', 'line', 'sev', 'title', 'why', 'res', 'providers')}
             for f in rounds[k]] for k in multi_rounds],
          open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'rounds.json'), 'w'))
