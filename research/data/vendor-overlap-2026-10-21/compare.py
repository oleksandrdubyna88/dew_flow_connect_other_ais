"""Compare judged clusters with the automatic matcher on the 30 sampled rounds."""
import json, re, math, collections, itertools, os
HERE = os.path.dirname(os.path.abspath(__file__))
FAM = {'codex': 'openai', 'codex-2': 'openai', 'gemini': 'google', 'antigravity': 'google',
       'chat-preset-mtwxbymp-4': 'google', 'vibe': 'mistral', 'kimi': 'moonshot', 'qwen': 'qwen',
       'qwen-2': 'qwen', 'claude': 'anthropic', 'claude-2': 'anthropic', 'local': 'local'}
STOP = set("""the a an and or of to in on for is are be by with as at from this that it its not no but if
then than into when which while can will would should could does do has have had was were been being
there their they them also only any all each every one two more most other such same so very just
instead without within because before after over under about via per plan code file line method function
""".split())
tok = lambda s: [w for w in re.findall(r"[a-z][a-z0-9_]{2,}", s.lower()) if w not in STOP]

allr = json.load(open(os.path.join(HERE, 'rounds.json')))
docs = [tok(f['title']) * 2 + tok(f['why'])[:120] for r in allr for f in r]
df = collections.Counter(w for d in docs for w in set(d)); N = len(docs)
def vec(f):
    tf = collections.Counter(tok(f['title']) * 2 + tok(f['why'])[:120])
    v = {w: (1 + math.log(n)) * math.log(N / df[w]) for w, n in tf.items() if df[w]}
    n = math.sqrt(sum(x * x for x in v.values())) or 1
    return {w: x / n for w, x in v.items()}
cos = lambda a, b: sum(x * b.get(w, 0) for w, x in a.items())
def auto(f, g):
    s = cos(f['v'], g['v'])
    anch = f['file'] and f['file'] == g['file'] and abs(f['line'] - g['line']) <= 10
    return s >= 0.30 or (anch and s >= 0.12)

judged = {}
for j in range(3):
    p = os.path.join(HERE, f'judged{j}.json')
    if os.path.exists(p): judged.update(json.load(open(p)))
sample = json.load(open(os.path.join(HERE, 'sample.json')))

fam = lambda f: FAM.get(f['providers'][0], f['providers'][0])
res = collections.defaultdict(lambda: [0, 0])
conf = collections.Counter()
for r in sample:
    key = f"{r[0]['store']}:{r[0]['rid']}"
    if key not in judged: continue
    st = r[0]['stage']
    group = {fid: i for i, g in enumerate(judged[key]) for fid in g}
    for f in r: f['v'] = vec(f)
    for f in r:
        fams_f = {FAM.get(p, p) for p in f['providers']}
        j_cross = len(fams_f) > 1 or any(group.get(g['fid']) == group.get(f['fid']) and fam(g) != fam(f) for g in r if g is not f)
        a_cross = len(fams_f) > 1 or any(auto(f, g) and fam(g) != fam(f) for g in r if g is not f)
        j_lens = any(group.get(g['fid']) == group.get(f['fid']) and g['providers'] == f['providers'] and g['role'] != f['role'] for g in r if g is not f)
        for k, v in (('judged', j_cross), ('auto', a_cross)):
            res[(st, k)][0] += v; res[(st, k)][1] += 1
            res[('ALL', k)][0] += v; res[('ALL', k)][1] += 1
        res[('ALL', 'judged same model, other role')][0] += j_lens; res[('ALL', 'judged same model, other role')][1] += 1
        if f['res'] in ('accept', 'reject'):
            a = res[('accept', 'shared' if j_cross else 'alone')]; a[0] += f['res'] == 'accept'; a[1] += 1
    for f, g in itertools.combinations(r, 2):
        if fam(f) == fam(g): continue
        conf[(auto(f, g), group.get(f['fid']) == group.get(g['fid']))] += 1
    # distinct defects and how many vendors raised each
    for g in judged[key]:
        fs = [f for f in r if f['fid'] in g]
        res[('groups', st)][1] += 1
        res[('groups', st)][0] += len({FAM.get(p, p) for f in fs for p in f['providers']}) > 1

pct = lambda a: f"{100 * a[0] / a[1]:.1f}% ({a[0]}/{a[1]})" if a[1] else '-'
print("rounds judged:", sum(1 for r in sample if f"{r[0]['store']}:{r[0]['rid']}" in judged))
for k in sorted(res): print(k, pct(res[k]))
tp, fp, fn = conf[(True, True)], conf[(True, False)], conf[(False, True)]
print(f"cross-vendor PAIRS: auto precision {tp}/{tp + fp}, auto recall {tp}/{tp + fn}, true pairs {tp + fn} of {sum(conf.values())}")
