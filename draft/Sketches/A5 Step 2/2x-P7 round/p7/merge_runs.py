# Merges round 2x-P7's probe results into runs.tsv: case, folder, closure receivers, default, dev MIR, 2a-1b MIR (where run).
# Run: python -I merge_runs.py <p7-dir>
import os
import sys

root = sys.argv[1]

def load(path):
    rows, cur = {}, None
    if not os.path.exists(path):
        return rows
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('=== '):
            cur = line[4:]
            rows[cur] = {'cl': []}
        elif line.startswith('  closure'):
            rows[cur]['cl'].append(line.strip().split(': receiver ')[1].split(';')[0])
        elif line.startswith('  default:'):
            rows[cur]['default'] = line[10:].strip()
        elif line.startswith('  mir:'):
            rows[cur]['mir'] = line[6:].strip()
    return rows

a5 = load(os.path.join(root, 'cases-a5', 'results.txt'))
out = ['case\tfolder\treceivers\tdefault (dev)\tMIR (dev)\tMIR (2a-1b held)']
for folder in ['cases', 'cases-x', 'param-slot', 'failed-closure', 'analogs']:
    for name, r in sorted(load(os.path.join(root, folder, 'results.txt')).items()):
        other = a5.get(name, {}).get('mir', '') if folder in ('cases', 'cases-x') else ''
        out.append('\t'.join([name, folder, ','.join(r['cl']) or '-', r.get('default', ''), r.get('mir', ''), other]))
open(os.path.join(root, 'runs.tsv'), 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
print(len(out) - 1, 'rows')
