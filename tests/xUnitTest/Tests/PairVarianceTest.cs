// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.3.5: a pair layer whose binder admits uniq, obj, objuniq or raw is invariant in its target, as the concrete exclusive
// layer is, so a relation between its targets must hold as an equality in the generic body; a binder admitting only shared or
// owning Semantics keeps the covariant target.
public sealed class PairVarianceTest
{
    private const string Generic = "func smuggle<s/T>(t: s/T, longer: s/(ref/i32 during a)) -> s/(ref/i32 during b) during b\n    {0}\n    origin a outlives b\n    return longer@move\n" +
        "public func main() -> ()\n    let outer: i32 = 1\n    var holder = outer@ref\n    var n: i32 = 0\n    let shorter = smuggle(n@uniq, holder@uniq)\n    Console.writeLine(\"\\(shorter@follow@follow)\")\n";

    private const string Concrete = "func smuggle(t: uniq/i32, longer: uniq/(ref/i32 during a)) -> uniq/(ref/i32 during b) during b\n    origin a outlives b\n    return longer@move\n" +
        "public func main() -> ()\n    let outer: i32 = 1\n    var holder = outer@ref\n    var n: i32 = 0\n    let shorter = smuggle(n@uniq, holder@uniq)\n    Console.writeLine(\"\\(shorter@follow@follow)\")\n";

    // The target of an exclusive-admitting layer is invariant: the result fit requires a == b, as the concrete uniq form does.
    [Theory]
    [InlineData("s is owner or uniq")]
    [InlineData("s is uniq")]
    [InlineData("s is owner or objuniq")]
    public void AnExclusiveAdmittingLayerRequiresEqualTargets(string premise)
    {
        var generic = Relations(DiagnosticCorpus.Check(Generic.Replace("{0}", premise, StringComparison.Ordinal)));
        Assert.Contains(("==", "a", "b"), generic);
        Assert.Contains(("outlives", "longer", "b"), generic);
        Assert.Equal(Relations(DiagnosticCorpus.Check(Concrete)), generic);
    }

    // A layer admitting only shared or owning Semantics keeps the covariant target: `a outlives b` suffices, so no equality is required.
    [Theory]
    [InlineData("s is owner or ref")]
    [InlineData("s is ref")]
    [InlineData("s is owner")]
    public void ASharedOrOwningLayerKeepsTheCovariantTarget(string premise)
    {
        var relations = Relations(DiagnosticCorpus.Check(Generic.Replace("{0}", premise, StringComparison.Ordinal)));
        Assert.DoesNotContain(relations, static x => x.Relation == "==");
    }

    private static List<(string Relation, string Longer, string Shorter)> Relations(CheckOutput output)
    {
        var relations = new List<(string, string, string)>();
        foreach (var record in output.Diagnostics)
        {
            if (record.Code != nameof(DiagnosticCode.UnprovenOriginRelation_Kd) || record.Reason is not { } facts)
            {
                continue;
            }

            relations.Add((Fact(facts, "relation"), Fact(facts, "longer"), Fact(facts, "shorter")));
        }

        relations.Sort();
        return relations;
    }

    private static string Fact(DiagnosticValue[] facts, string name)
    {
        foreach (var fact in facts)
        {
            if (fact.Name == name)
            {
                return fact.Value;
            }
        }

        return string.Empty;
    }
}
