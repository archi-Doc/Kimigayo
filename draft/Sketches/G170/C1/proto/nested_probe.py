# Per-query probe of the nested profile (R3-N3): each generated model's queries, C against the bounded reference (time mode), each
# query a separate process with a wall-clock budget. A query over the budget is run again with C alone (trace mode, same budget) to
# tell C's time from the reference's.
# Usage: python nested_probe.py <G170C1.dll> <seed> <models> <budget seconds> <model dir> (results/nested-probe-seed-77.txt: 77 40 60).
import os, subprocess, sys, re, time, concurrent.futures as cf
dll, seed, count, budget, out_dir = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
os.makedirs(out_dir, exist_ok=True)
env = dict(os.environ, G170_REVIEW_PROFILE='nested', G170_ITEMS='3000')
jobs = []
for i in range(count):
    dump = subprocess.run(['dotnet', dll, 'random', str(count), str(seed)], env=dict(env, G170_MODEL=str(i)), capture_output=True, text=True).stdout
    text = dump[:dump.index('QUERIES: ')]
    queries = dump[dump.index('QUERIES: ') + 9:].split('\n')[0].split(' | ')
    path = os.path.join(out_dir, f'm{i}.txt')
    open(path, 'w', encoding='utf-8').write(text)
    for q in queries:
        jobs.append((i, path, q))

def run(job):
    i, path, q = job
    try:
        r = subprocess.run(['dotnet', dll, 'time', path, q], env=env, capture_output=True, text=True, timeout=budget)
        return i, q, r.stdout.strip(), None
    except subprocess.TimeoutExpired:
        start = time.time()
        try:
            r = subprocess.run(['dotnet', dll, 'trace', path, q], env=env, capture_output=True, text=True, timeout=budget)
            return i, q, 'TIMEOUT', f'C alone {r.stdout.strip().splitlines()[-1]} {time.time() - start:.1f} s wall'
        except subprocess.TimeoutExpired:
            return i, q, 'TIMEOUT', 'C alone over the budget too'

agree = disagree = timeout = resource = unstable = overflow = 0
c_ms = []
lines = []
with cf.ThreadPoolExecutor(max_workers=8) as ex:
    for i, q, line, alone in ex.map(run, jobs):
        if line == 'TIMEOUT':
            timeout += 1
            lines.append(f'OVER BUDGET model {i}: {q}; {alone}')
            continue
        m = re.search(r': C (\S+) (\S+) .* (\d+) ms; ref5 (\S+) .*; ref7 (\S+) ', line)
        if not m:
            lines.append(f'UNPARSED model {i}: {line}')
            continue
        verdict, outcome, ms, r5, r7 = m.groups()
        c_ms.append(int(ms))
        if 'overflow' in (r5, r7):
            overflow += 1
        elif r5 != r7:
            unstable += 1
        elif verdict == 'Resource':
            resource += 1
            lines.append(f'RESOURCE model {i}: {line}')
        elif verdict == r5:
            agree += 1
        else:
            disagree += 1
            lines.append(f'DISAGREE model {i}: {line}')
for l in lines:
    print(l)
c_ms.sort()
print(f'== NESTED seed {seed}: {count} models, {len(jobs)} queries (budget {budget} s each, C and both reference bounds): agree {agree}, disagree {disagree}, C Resource {resource}, reference unstable {unstable}, overflow {overflow}, over budget {timeout}; C time on finished queries median {c_ms[len(c_ms) // 2]} ms, p95 {c_ms[len(c_ms) * 95 // 100]} ms, max {c_ms[-1]} ms')
