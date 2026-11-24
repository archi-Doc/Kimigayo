// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DeepRefinementPremiseBindingTest
{
    [Theory]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(64, false)]
    [InlineData(64, true)]
    public void DeepRefinementDoesNotReexpandFlattenedAncestors(int depth, bool diamond)
    {
        var c = MinimalEmissionTest.Analyze(Source(depth, diamond));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
        Assert.Equal(depth + 1, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C" + depth).SymbolOf()!.Contract!.Ancestors.Count);
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepPendingPrerequisitesRemainPending(bool diamond)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "public contract Origin\npublic struct Source\n" + Source(32, diamond, "Source is Origin")));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
    }

    [Fact]
    public void FlattenedTraversalRetainsClosedObligationCycleGuards()
    {
        var c = MinimalEmissionTest.Analyze("public struct Source\n    Self is C32\n" + Source(32, true, "Source is C32"));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDeepRefinementBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source(16, true));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Deep refinement verification failed.");
            }
        }));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDeepConformanceVerificationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source(16, true) + "\npublic struct Target\n    Self is Copy\n    Self is C16");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Deep conformance verification failed.");
            }
        }));
    }

    // R7-N5 B8 (DECISIONS BU-220; the Front's C-521 C2): ImplTable finds a path in its identity's row by declaring clause and root, so
    // a Type declaring n Contracts that each refine one Contract C names n paths of (Type, C), and each lookup of one scans that row:
    // a warm Bind compares about 3n^2 entries (5n^2 when C has a requirement), the bound the row form accepts (the suite's longest row
    // is 13). Each program is valid; n is 64 and 512.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideRefinementPathLookupsStayWithinTheRowBound(bool requirement)
    {
        int[] sizes = [64, 512];
        foreach (var n in sizes)
        {
            var c = MinimalEmissionTest.Analyze(WideSource(n, requirement));
            Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
            var before = c.Binding.ConformancePathScans;
            Assert.True(c.Bind().IsComplete);
            var scans = c.Binding.ConformancePathScans - before;
            Assert.True(scans >= n && scans <= (requirement ? 6L : 4L) * n * n, $"n {n}: {scans} path entries compared in a warm Bind");
        }
    }

    private static string Source(int depth, bool diamond, string? condition = null)
    {
        var source = new StringBuilder("public contract C0: Copy\n");
        if (condition is not null)
        {
            source.Append("    ").Append(condition).Append('\n');
        }

        for (var i = 1; i <= depth; i++)
        {
            source.Append("public contract C").Append(i).Append(": C").Append(i - 1);
            if (diamond && i > 1)
            {
                source.Append(", C").Append(i - 2);
            }

            source.Append('\n');
        }

        return source.Append("group G\n    func inspect<T>(value: T)\n        T is C").Append(depth).Append("\n        ()").ToString();
    }

    // C, n Contracts D1..Dn refining it, and a struct declaring each Di; with a requirement, C's `m` and the struct's witness.
    private static string WideSource(int n, bool requirement)
    {
        var source = new StringBuilder("public contract C\n").Append(requirement ? "    func m(self: ref/Self) -> i32\n" : string.Empty);
        for (var i = 1; i <= n; i++)
        {
            source.Append("public contract D").Append(i).Append(": C\n");
        }

        source.Append("public struct S\n");
        for (var i = 1; i <= n; i++)
        {
            source.Append("    Self is D").Append(i).Append('\n');
        }

        return source.Append(requirement ? "    public func m(self: ref/Self) -> i32 => 1\n" : string.Empty).ToString();
    }
}
