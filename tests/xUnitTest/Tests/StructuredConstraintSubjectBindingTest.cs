// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StructuredConstraintSubjectBindingTest
{
    [Theory]
    [InlineData("() is Copy")]
    [InlineData("(i32, bool) is Copy")]
    [InlineData("[2 of i32] is Copy")]
    [InlineData("Box<i32> is Copy")]
    [InlineData("ref/i32 during static is Copy")]
    [InlineData("((i32)) is Copy")]
    [InlineData("Box<Box<i32>> is Copy")]
    [InlineData("Box<[2 of i32]> is Copy")]
    [InlineData("Box<[(8 / 2) of i32]> is Copy")]
    [InlineData("([2 of i32], Box<i32>) is Copy")]
    [InlineData("raw/i32 is Copy")]
    [InlineData("() is ()")]
    [InlineData("[2 of string] is not Copy")]
    public void StructuredClosedSubjectsReachTheirProof(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete);
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = MinimalEmissionTest.Analyze(builder.ToString());
            Assert.Empty(TestDiagnostics.Of(written, "Hello.kimi"));
            Assert.True(written.Binding.Result.IsComplete, MinimalEmissionTest.Describe(written, null));
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData("Box<string> is Box<string>")]
    [InlineData("Box<string> is Copy or Owned")]
    [InlineData("(i32, string) is Copy")]
    [InlineData("[2 of i32] is [3 of i32]")]
    [InlineData("ref/i32 during static is i32")]
    public void InvalidFormationAndRefutedProofsRejectTheOwner(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Target(c).StateOf());
        Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("(i32, bool) is Copy")]
    [InlineData("Box<i32> is Copy")]
    public void EnumRegionsShareCompleteTypeSubjectParsing(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause).Replace("struct Target", "enum Target", StringComparison.Ordinal) + "\n    A");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void AssociateRemainsAContextualTypeNameInAConstructedSubject()
    {
        var c = MinimalEmissionTest.Analyze(Source("Box<i32> is Copy").Replace("Box", "associate", StringComparison.Ordinal));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("[2 of Hidden] is Owned")]
    [InlineData("(Hidden, i32) is Owned")]
    [InlineData("Box<Hidden> is Copy")]
    public void NestedClosedSubjectsRetainApiDomains(string clause)
    {
        var c = MinimalEmissionTest.Analyze("internal struct Hidden\n    Self is Copy\n" + Source(clause));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
    }

    [Theory]
    [InlineData("(value is Dog)")]
    [InlineData("make() is Dog")]
    public void GenericFunctionRuntimeTestsDoNotBecomeTypeConstraints(string expression)
    {
        var c = MinimalEmissionTest.Analyze("struct Dog\nfunc make() -> Dog => make()\nfunc f<T>(value: Dog)\n    " + expression);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Empty(function.TypeConstraints);
        Assert.Single(KotoTree.Walk(function.Body!).OfType<IsKoto>(), x => x.IsRuntimeTest);
    }

    [Theory]
    [InlineData("Box<i32 is Copy")]
    [InlineData("(i32,, bool) is Copy")]
    [InlineData("[2 i32] is Copy")]
    [InlineData("Box<i32>> is Copy")]
    public void MalformedSubjectsAreDiagnosedWithoutLosingFollowingDeclarations(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause) + "\npublic struct After");
        Assert.NotEmpty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Contains(c.Kotonoha.RootKoto.NestedContainers, x => x.Name == "After");
    }

    [Theory]
    [InlineData("(i32, bool is Copy")]
    [InlineData("[2 of i32 is Copy")]
    [InlineData("Box<[(8 >> 1) of i32]> is Copy")]
    public void UnterminatedGroupsAndUnsupportedLengthOperatorsAreDiagnosed(string clause)
    {
        var c = MinimalEmissionTest.Analyze(Source(clause));
        Assert.NotEmpty(TestDiagnostics.Of(c, "Hello.kimi"));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmStructuredSubjectChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("([2 of i32], Box<i32>) is Copy"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Structured constraint failed.");
            }
        }));
    }

    private static DeclarationContainerKoto Target(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");

    private static string Source(string clause)
        => "public struct Box<T>\n    T is Copy\n    Self is Copy\n    let value: T\npublic struct Target\n    " + clause;
}
