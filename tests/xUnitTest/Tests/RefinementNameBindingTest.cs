// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class RefinementNameBindingTest
{
    [Theory]
    [InlineData("Origin", true)]
    [InlineData("Api.Origin", true)]
    [InlineData("::Origin", true)]
    [InlineData("::Api.Origin", true)]
    [InlineData("::Origin<i32>", false)]
    [InlineData("::Api.Origin<i32>", false)]
    public void RefinementParentsAreContractNames(string parent, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic group Api\n    public contract Origin\npublic contract Child: " + parent);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").StateOf());
        if (valid)
        {
            var root = c.Kotonoha.RootKoto;
            var expected = parent.Contains("Api.", StringComparison.Ordinal) ? root.NestedContainers.Single(x => x.Name == "Api").NestedContainers.Single() : root.NestedContainers.Single(x => x.Name == "Origin");
            Assert.Same(expected.SymbolOf(), root.NestedContainers.Single(x => x.Name == "Child").SymbolOf()!.Contract!.Ancestors.Single());
        }

        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Origin<>")]
    [InlineData("Api.Origin<>")]
    [InlineData("::Origin<>")]
    [InlineData("::Api.Origin<>")]
    [InlineData("::ref/Origin")] // SPEC F.5: only a TypeSegment follows '::'.
    [InlineData("::(Origin)")]
    [InlineData("ref/Origin")] // SPEC F.6.2, F.5: a Contract parent is a NamedReference.
    [InlineData("ref/::Origin")]
    [InlineData("(Origin)")]
    public void ParentsOutsideTheGrammarAreSyntaxErrors(string parent)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic group Api\n    public contract Origin\npublic contract Child: " + parent);
        Assert.NotEmpty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").StateOf());
    }

    [Theory]
    [InlineData("Future<i32>")]
    [InlineData("::Future<i32>")]
    [InlineData("Api.Future<i32>")]
    // A missing generic parent is pending as a non-generic one is (BU-151: a missing name decides nothing; SPEC 6.1.2: a later fragment
    // of Api may declare it), and rejected at the final pass.
    public void MissingNamesLeaveGenericParentsPending(string parent)
    {
        var c = MinimalEmissionTest.Analyze("public group Api\npublic contract Child: " + parent);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").StateOf());
        Assert.False(c.Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRootQualifiedParentBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public group Api\n    public contract Origin\npublic contract Child: ::Api.Origin");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Qualified parent binding failed.");
            }
        }));
    }
}
