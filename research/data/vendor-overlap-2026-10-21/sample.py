import json,random,collections
R=json.load(open('rounds.json'))
FAM={'codex':'openai','codex-2':'openai','gemini':'google','antigravity':'google','chat-preset-mtwxbymp-4':'google','vibe':'mistral','kimi':'moonshot','qwen':'qwen','qwen-2':'qwen','claude':'anthropic','claude-2':'anthropic','local':'local'}
ok=[r for r in R if 6<=len(r)<=28]
random.seed(21)
code=[r for r in ok if r[0]['stage']=='CodeReview']; plan=[r for r in ok if r[0]['stage']=='PlanReview']
pick=random.sample(code,15)+random.sample(plan,15); random.shuffle(pick)
json.dump(pick,open('sample.json','w'))
for j in range(3):
  out=[]
  for i,r in enumerate(pick[j*10:(j+1)*10]):
    out.append(f"### ROUND {r[0]['store']}:{r[0]['rid']}  stage={r[0]['stage']}")
    for f in r:
      v='+'.join(sorted({FAM.get(p,p) for p in f['providers']}))
      out.append(f"- id={f['fid']} vendor={v} role={f['role']} file={f['file'] or '-'}:{f['line']}\n  title: {f['title']}\n  why: {f['why'][:600]}")
    out.append('')
  open(f'judge{j}.md','w',encoding='utf-8').write('\n'.join(out))
print([len(r) for r in pick], sum(len(r) for r in pick))
