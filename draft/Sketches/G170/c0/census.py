import re, sys, os, glob
sys.stdout.reconfigure(encoding='utf-8')
pat = re.compile(r'\.Facts\b|DirectFacts|DerivedFacts|HasAssociatedProjection\b|HasParameterIdentity\b|\.Constraints\b|Constraints \?\?=|AddConstraintFact\(|AddContractPremises\(|ExpandScopeContractPremises\(|ExpandContractPremises\(|AddArithmeticPremises\(')
sig = re.compile(r'^\s*(?:(?:private|internal|public|protected|static|readonly|override|unsafe|new|partial)\s+)+[\w<>\[\]?,.() ]+?\s+(\w+)\s*(?:<[^>]*>)?\(')
local = re.compile(r'^\s+(?:static\s+)?[\w<>\[\]?,.() ]+\s+(\w+)\s*\([^;]*\)\s*$')
for path in sorted(glob.glob('*.cs')):
    lines = open(path, encoding='utf-8').read().split('\n')
    method = '?'
    for i, line in enumerate(lines, 1):
        m = sig.match(line)
        if m and not line.strip().startswith('//') and ' new(' not in line and '=' not in line.split('(')[0]:
            method = m.group(1)
        if pat.search(line) and not line.strip().startswith('//') and not line.strip().startswith('///'):
            print(f"{path}:{i}\t{method}\t{line.strip()[:150]}")
