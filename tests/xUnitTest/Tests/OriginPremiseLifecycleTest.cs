// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

// PLAN G74 U5: premise environments live for one proof request. Their storage is reused, keeps no Origin, Type or syntax
// reference between requests, and its capacity is the largest environment it has held, so edits and repeated analyses neither
// see stale premises nor grow it.
public class OriginPremiseLifecycleTest
{
    // SPEC 15.3.3, 15.3.6: removing a clause the body needs makes its relation unproven, and restoring it proves the relation
    // again with the same obligations and verdicts; storage after each analysis holds no reference and does not grow.
    [Fact]
    public void RemovingAndRestoringAClauseChangesOnlyItsJudgment()
    {
        var source = OriginProofWorkloads.Create("proven", 4);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var obligations = c.Binding.Obligations.Count;
        var storage = c.Binding.OriginPremiseStorage;
        Assert.False(storage.Retains);

        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        var clause = Clause(source, function, "origin a outlives x");
        var edited = source.Replace("origin a outlives x", "origin b outlives a", StringComparison.Ordinal);
        var donor = MinimalEmissionTest.Analyze(edited);
        var replacement = Clause(edited, KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g"), "origin b outlives a");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(KotoHelper.Replace(clause.Parent!, clause, replacement));
            Reanalyze(c);
            Assert.Contains(DiagnosticCode.UnprovenOriginRelation_Kd, c.Binding.Issues.Select(static x => x.Code).Concat(c.Ownership.Issues.Select(static x => x.Code)));
            Assert.Equal((storage.Environments, storage.Nodes, storage.Edges, false), c.Binding.OriginPremiseStorage);

            Assert.True(KotoHelper.Replace(replacement.Parent!, replacement, clause));
            Reanalyze(c);
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.Equal(obligations, c.Binding.Obligations.Count);
            Assert.Equal((storage.Environments, storage.Nodes, storage.Edges, false), c.Binding.OriginPremiseStorage);
        }
    }

    // SPEC 15.3.6: each Compilation proves from its own premises; alternating an accepted and a rejected program, and a large one
    // before a small one, changes neither result.
    [Fact]
    public void CompilationsKeepTheirOwnPremises()
    {
        var accepted = MinimalEmissionTest.Analyze(OriginProofWorkloads.Create("valid", 16));
        var rejected = MinimalEmissionTest.Analyze(OriginProofWorkloads.Create("composite", 16));
        var small = MinimalEmissionTest.Analyze(OriginProofWorkloads.Create("proven", 1));
        for (var i = 0; i < 3; i++)
        {
            foreach (var (c, verified) in new[] { (accepted, true), (rejected, false), (small, true) })
            {
                Reanalyze(c);
                Assert.Equal(verified, c.Ownership.Result.IsVerified);
                Assert.False(c.Binding.OriginPremiseStorage.Retains);
            }
        }

        Assert.True(small.Binding.OriginPremiseStorage.Nodes <= accepted.Binding.OriginPremiseStorage.Nodes);
    }

    // PLAN G74 U5: warm Binding and ownership analysis of the workload families, accepted and rejected, allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("valid")]
    [InlineData("composite")]
    [InlineData("proven")]
    [InlineData("result")]
    [InlineData("chain-broken")]
    public void WarmProofAllocatesNothing(string family)
    {
        var c = MinimalEmissionTest.Analyze(OriginProofWorkloads.Create(family, 8));
        for (var i = 0; i < 20; i++)
        {
            Reanalyze(c);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }

    private static void Reanalyze(Compilation c)
    {
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
    }

    private static OriginRelationKoto Clause(string source, FunctionKoto function, string text)
        => KotoTree.Walk(function).OfType<OriginRelationKoto>().Single(x => source.Substring(x.Span.Start, x.Span.Length).Contains(text["origin ".Length..], StringComparison.Ordinal));
}
