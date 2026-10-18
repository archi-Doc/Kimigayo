# A5 step 2 unit 2a0-0: aggregates the F4CensusProbe TSVs (default run plus the F4_NOSKIP and F4_CALLBORROWS runs).
# Usage: python census.py <out-dir> [ledger raw tsv]
# Line format: set, name, sha, tag, fields...; Where() = location, line, form, function, text (5 fields).
import collections
import sys

out = sys.argv[1]
ORIGIN = {"UnsatisfiedOriginRelation_Kd", "UnprovenOriginRelation_Kd"}


def load(name):
    rows = []
    try:
        with open(f"{out}/{name}", encoding="utf-8") as f:
            for line in f:
                rows.append(line.rstrip("\n").split("\t"))
    except FileNotFoundError:
        pass
    return rows


def records(rows):
    # The verdict-bearing lines: old-path records and extra Binding Origin records, per input.
    result = collections.defaultdict(set)
    for r in rows:
        if r[3] in ("REC", "MORE", "EXC", "SKIP"):
            result[(r[0], r[1])].add("\t".join(r[3:]))
    return result


default = load("default.tsv")
sets = ["library", "milestone", "golden", "spec", "fixture", "ledger"]
inputs = collections.Counter(r[0] for r in default if r[3] in ("SUM", "EXC", "SKIP"))
print("inputs:", dict(inputs))
print("exceptions:", sum(1 for r in default if r[3] == "EXC"), "unparsed:", sum(1 for r in default if r[3] == "SKIP"))

events = [r for r in default if r[3] == "EV"]


def ev(kind):
    return [r for r in events if r[4] == kind]


def by_set(rows):
    c = collections.Counter(r[0] for r in rows)
    return " ".join(f"{s}={c[s]}" for s in sets if c[s])


# (a) the containment skip
print("\n(a) bodies the skip covers (independent functions):", by_set(ev("independent")))
for r in ev("independent"):
    if r[0] == "library":
        print("   ", r[8])
skip = ev("skip")
print("(a) skip sites:", by_set(skip), "| distinct bodies:", by_set(list({(r[0], r[1], r[8]): r for r in skip}.values())))
gains = [r for r in skip if r[-1] == "gains"]
print("(a) sites that gain containment without the skip:", by_set(gains))
for r in gains[:20]:
    print("   ", r[0], r[1], r[5], r[6], r[8], r[9], "|", r[10], "->", r[11])
for variant in ("noskip", "callborrows"):
    rows = load(f"{variant}.tsv")
    if not rows:
        continue
    a, b = records(default), records(rows)
    changed = [k for k in set(a) | set(b) if a.get(k) != b.get(k) and k[0] in {r[0] for r in rows}]
    print(f"({'a' if variant == 'noskip' else 'd'}) {variant}: inputs compared {len({(r[0], r[1]) for r in rows})}, verdict changes {len(changed)}")
    for k in sorted(changed)[:20]:
        print("   ", k, "| added:", sorted(b.get(k, set()) - a.get(k, set()))[:3], "| removed:", sorted(a.get(k, set()) - b.get(k, set()))[:3])

# (b) RecordMismatch records and their chains
mm = ev("mm-chain")
print("\n(b) RecordMismatch Origin chains:", by_set(mm))
c = collections.Counter((r[0], r[10], r[11], r[12], r[13], r[14], r[15], r[16]) for r in mm)
for k, n in sorted(c.items()):
    print("   ", n, k)
inner = [r for r in mm if r[16] in ("sources", "judge-region")]
print("(b) chains through a local region (LocalRegionSources or JudgeLocalRegion):", by_set(inner))
for r in inner[:10]:
    print("   ", r[0], r[1], r[5], r[6], r[8], r[9], "|", r[12], r[14], r[15], r[16], r[17], "->", r[18])
nonfixed = [r for r in mm if r[16] == "direct" and (r[14] != "fixed" or r[15] != "fixed")]
print("(b) direct chains with a non-fixed end:", by_set(nonfixed))
for r in nonfixed[:10]:
    print("   ", r[0], r[1], r[5], r[6], r[8], r[9], "|", r[12], r[14], r[15], r[17], "->", r[18])
for kind in ("gate985", "mm-unbound", "mm-dependent"):
    rows = ev(kind)
    print(f"(b) {kind}:", by_set(rows))
    for r in rows[:10]:
        print("   ", r[0], r[1], r[5], r[6], r[8], r[9], "|", "\t".join(r[10:]))

# (c) several Origin records of one code at one node
nodes = collections.defaultdict(list)
for r in default:
    if r[3] == "REC" and r[5] in ORIGIN:
        nodes[(r[0], r[1], r[5], r[6])].append("Binding" if r[4].startswith("Binding.") else "Ownership")
    elif r[3] == "MORE" and r[5] in ORIGIN:
        nodes[(r[0], r[1], r[5], r[6])].append("Binding+")
multi = {k: v for k, v in nodes.items() if len(v) > 1}
print("\n(c) nodes with Origin records:", by_set([(k[0],) for k in nodes]), "| with several of one code:", by_set([(k[0],) for k in multi]))
for k, v in sorted(multi.items())[:20]:
    print("   ", k, collections.Counter(v))

# (d) CallBorrows rows
rows = [r for r in default if r[3] == "ROW" and r[5] == "CallBorrows"]
print("\n(d) CallBorrows rows:", by_set(rows), "| verdicts:", dict(collections.Counter(r[12] for r in rows)), "| classes:", dict(collections.Counter((r[10], r[11]) for r in rows)))
for r in rows[:3]:
    print("   ", r[0], r[1], r[13], r[14], r[16], r[17], "|", r[18], "->", r[19])
allrows = [r for r in default if r[3] == "ROW" and r[4] == "OriginOutlives"]
print("(d) all OriginOutlives rows:", by_set(allrows), "| verdicts:", dict(collections.Counter(r[12] for r in allrows)))

# (e) T1
t1 = ev("t1")
t1rows = ev("t1-row")
print("\n(e) T1 judgments (CheckTypeOriginRelations):", by_set(t1), "| outcomes:", dict(collections.Counter((r[10], r[11]) for r in t1)))
print("(e) Type-clause rows (AddTypeClauseObligations):", by_set(t1rows))
judged = {(r[0], r[1], r[12], r[13], r[14]) for r in t1}
both = [r for r in t1rows if (r[0], r[1], r[10], r[11], r[12]) in judged]
print("(e) Type-clause rows whose clause was also judged as a Constraint (T1 occurrences):", by_set(both))
for r in (both or t1rows or t1)[:5]:
    print("   ", r[0], r[1], r[5], r[6], r[8], r[9], "|", "\t".join(r[10:]))

# (f) Anchor ends
proofs = ev("anchor-proof")
bounds = ev("anchor-bound")
anchored = [r for r in default if r[3] == "ROW" and ("anchor" in r[10] or "anchor" in r[11] or "A:" in r[-1] or "A:" in r[-2])]
print("\n(f) anchor relations proven or judged:", by_set(proofs), "| positions:", dict(collections.Counter((r[10], r[11], r[12]) for r in proofs)))
print("(f) region bounds with an anchor end:", by_set(bounds), "| rows with an anchor end:", by_set(anchored))
for r in (bounds + anchored)[:10]:
    print("   ", "\t".join(r[:2] + r[5:]))
into = [r for r in proofs if "anchor" in r[12] and "anchor" not in r[11]] + [r for r in bounds if "anchor" in r[11]]
print("(f) relations with the anchor as the shorter end (an in-edge):", by_set(into))
for r in into[:10]:
    print("   ", "\t".join(r[:2] + r[5:]))

# (g) the Unrepresentable gate
gate = ev("gate79")
print("\n(g) gate records (OwnershipAnalysis.Borrows.cs:74-79):", by_set(gate), "| kinds:", dict(collections.Counter((r[10], r[11], r[14], r[15]) for r in gate)))
for r in gate[:10]:
    print("   ", r[0], r[1], r[5], r[6], r[7], r[8], r[9], "|", "\t".join(r[10:]))
unsupported = [r for r in default if r[3] == "REC" and r[4] == "Ownership.Unsupported"]
print("(g) Ownership.Unsupported records:", by_set(unsupported))
if len(sys.argv) > 2:
    import os
    def norm(loc):
        path, _, at = loc.rpartition(":")
        return os.path.basename(path.replace("\\", "/")) + ":" + at

    gates = {(r[2], norm(r[5])) for r in gate}
    recs = {(r[2], norm(r[6])) for r in unsupported}
    seen = {r[2] for r in default if r[3] == "SUM"}
    ledger = collections.Counter()
    examples = {}
    with open(sys.argv[2], encoding="utf-8") as f:
        for line in f:
            x = line.rstrip("\n").split("\t")
            if x[3] == "U" and x[2] == "old" and x[5] == "Ownership.Unsupported":
                loc = x[6]
                key = (x[9], norm(loc))
                state = "gate" if key in gates else "other" if key in recs else "absent" if x[9] in seen else "not-run"
                ledger[(x[1], x[7], state)] += 1
                examples.setdefault((x[7], state), (x[9], loc))
    print("(g) a5-1b5-b1 U old rows by section, form and state at the base:")
    for k, n in sorted(ledger.items()):
        print("   ", n, k)
    for k, v in sorted(examples.items()):
        print("    e.g.", k, v)
