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
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        Assert.Equal(depth + 1, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C" + depth).BoundSymbol!.Contract!.Ancestors.Count);
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepPendingPrerequisitesRemainPending(bool diamond)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "public contract Origin\npublic struct Source {}\n" + Source(32, diamond, "Source is Origin")));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
    }

    [Fact]
    public void FlattenedTraversalRetainsClosedObligationCycleGuards()
    {
        var c = MinimalEmissionTest.Analyze("public struct Source {}\n    Self is C32\n" + Source(32, true, "Source is C32"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void WarmDeepRefinementBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source(16, true));
        for (var i = 0; i < 20; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Deep refinement verification failed.");
            }
        }));
    }

    [Fact]
    public void WarmDeepConformanceVerificationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source(16, true) + "\npublic struct Target {}\n    Self is Copy\n    Self is C16");
        for (var i = 0; i < 20; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Deep conformance verification failed.");
            }
        }));
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
}
