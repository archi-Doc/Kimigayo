// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

// Origin proof workloads (PLAN G74), linked into tests and benchmarks so their inputs cannot drift apart. Each family scales one
// shape by `size`; the expected outcome follows from SPEC 15.3.4-15.3.7 and 15.6.1, never from a compiler's speed.
internal static class OriginProofWorkloads
{
    // Every family, with the diagnostic code a size-independent check reports, or null when the program is accepted.
    internal static readonly (string Family, string? Code)[] Families =
    [
        ("valid", null),
        ("composite", "UnprovenOriginRelation_Kd"),
        ("composite-nokeep", "UnprovenOriginRelation_Kd"),
        ("atomic", "UnprovenOriginRelation_Kd"),
        ("proven", null),
        ("transitive", null),
        ("result", null),
        ("result-failed", "UnprovenOriginRelation_Kd"),
        ("anonymous", null),
        ("generic", null),
        ("chain", null),
        ("chain-broken", "UnprovenOriginRelation_Kd"),
        ("cycle", "UnprovenOriginRelation_Kd"),
    ];

    // `size` borrowed Holder inputs (or chain links) around the family's relation.
    internal static string Create(string family, int size)
    {
        const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n    public init(item: ref/i32 during a) => self.item = item\n";
        var source = new StringBuilder(Holder);
        if (family is "chain" or "chain-broken" or "cycle")
        {
            // A chain o0 >= o1 >= ... >= oN of clauses; the broken chain omits its middle link, and the cycle closes the chain
            // without reaching the queried Origin q.
            source.Append("func g(");
            for (var i = 0; i <= size; i++)
            {
                source.Append(i == 0 ? string.Empty : ", ").Append('o').Append(i).Append(": ref/i32");
            }

            source.Append(family == "cycle" ? ", q: ref/i32) -> ref/i32 during q\n" : ") -> ref/i32 during o" + size + "\n");
            for (var i = 0; i < size; i++)
            {
                if (family != "chain-broken" || i != size / 2)
                {
                    source.Append("    origin o").Append(i).Append(" outlives o").Append(i + 1).Append('\n');
                }
            }

            if (family == "cycle")
            {
                source.Append("    origin o").Append(size).Append(" outlives o0\n");
            }

            source.Append("    return o0\npublic func main() => ()\n");
            return source.ToString();
        }

        var holders = new StringBuilder();
        for (var i = 1; i <= size; i++)
        {
            holders.Append(", h").Append(i).Append(": ref/Holder");
        }

        var keep = family == "composite-nokeep" ? string.Empty : ", s: ref/i32, keep: uniq/(ref/i32 during s)";
        switch (family)
        {
            case "valid":
                source.Append("func g(c: bool, a: ref/i32, b: ref/i32, x: ref/i32").Append(holders).Append(keep).Append(") -> ref/i32 during (a and b)\n    let k = if c => a else => b\n    return k\n");
                break;
            case "composite" or "composite-nokeep":
                source.Append("func g(c: bool, a: ref/i32, b: ref/i32, x: ref/i32").Append(holders).Append(keep).Append(") -> i32\n    let k: ref/i32 during x = if c => a else => b\n    return k@follow\n");
                break;
            case "atomic":
                source.Append("func g(c: bool, a: ref/i32, b: ref/i32, x: ref/i32").Append(holders).Append(keep).Append(") -> i32\n    let k: ref/i32 during x = a\n    return k@follow\n");
                break;
            case "proven":
                source.Append("func g(c: bool, a: ref/i32, b: ref/i32, x: ref/i32").Append(holders).Append(keep).Append(") -> i32\n    origin a outlives x\n    origin b outlives x\n    let k: ref/i32 during x = if c => a else => b\n    return k@follow\n");
                break;
            case "transitive":
                source.Append("func g(c: bool, a: ref/i32, b: ref/i32, d: ref/i32, x: ref/i32").Append(holders).Append(keep).Append(") -> i32\n    origin b outlives x\n    origin d outlives x\n    origin a outlives d\n    let k: ref/i32 during x = if c => a else => b\n    return k@follow\n");
                break;
            case "result":
                source.Append("func g(p: ref/Holder").Append(holders).Append(keep).Append(") -> ref/(ref/i32 during p) during p => p.item@ref\n");
                break;
            case "result-failed":
                source.Append("func g(p: ref/Holder, q: ref/i32").Append(holders).Append(keep).Append(") -> ref/(ref/i32 during q) during p => p.item@ref\n");
                break;
            case "anonymous":
                source.Append("func g(c: bool, a: ref/i32, x: ref/i32").Append(holders).Append(keep).Append(") -> i32\n    origin a outlives x\n    let f = func (n: ref/i32 during x) -> ref/i32 during x => n\n    return f(a)@follow\n");
                break;
            case "generic":
                source.Append("func g<T>(c: bool, a: ref/T, b: ref/T, x: ref/T").Append(holders).Append(keep).Append(") -> ref/T during (a and b)\n    let k = if c => a else => b\n    return k\n");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }

        source.Append("public func main() => ()\n");
        return source.ToString();
    }
}
