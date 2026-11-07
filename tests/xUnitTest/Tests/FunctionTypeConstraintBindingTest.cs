// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FunctionTypeConstraintBindingTest
{
    [Theory]
    [InlineData("() -> i32", "Owned", true)]
    [InlineData("(i32) -> bool", "not Copy", true)]
    [InlineData("(i32) -> bool", "Copy", false)]
    [InlineData("(i32) -> bool", "(i32) -> bool", true)]
    [InlineData("(i32) -> bool", "(i32) -> i32", false)]
    [InlineData("() -> bool", "() -> bool", true)]
    [InlineData("((i32) -> bool)", "not Copy", true)]
    [InlineData("(i32) -> bool", "((i32) -> bool)", true)]
    [InlineData("(i32, bool) -> string", "(i32, bool) -> string", true)]
    [InlineData("(() -> i32) -> bool", "(() -> i32) -> bool", true)]
    [InlineData("() -> (i32) -> bool", "() -> (i32) -> bool", true)]
    [InlineData("(i32) -> bool", "(i32) -> bool and Owned", true)]
    [InlineData("(i32) -> bool", "((i32) -> i32 or Copy)", false)]
    [InlineData("[2 of (i32) -> bool]", "not Copy", true)]
    public void FunctionTypesRetainCapabilitiesAndIdentity(string type, string requirement, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    T is " + requirement + "\npublic func use(value: Target<" + type + ">) => ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single().StateOf());
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = MinimalEmissionTest.Analyze(builder.ToString());
            Assert.Empty(TestDiagnostics.Of(written, "Hello.kimi"));
            Assert.Equal(valid, written.Binding.Result.IsComplete);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    [InlineData("contract")]
    public void EveryTypeDeclarationRegionParsesFunctionRequirements(string kind)
    {
        var c = MinimalEmissionTest.Analyze("public " + kind + " Target<T>\n    T is (i32) -> bool" + (kind == "enum" ? "\n    A" : string.Empty));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("bool", true)]
    [InlineData("i32", false)]
    public void FunctionInputRequirementsAreDischargedAtCalls(string result, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("group Consumer\n    func take<T>()\n        T is (i32) -> bool\n        ()\n    func call() => take<(i32) -> " + result + ">()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var call = c.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(call.ExpressionBody).CallOf() is not null);
    }

    [Theory]
    [InlineData("(Hidden) -> i32")]
    [InlineData("() -> Hidden")]
    [InlineData("(Hidden) -> i32 or Owned")]
    public void FunctionConditionTypesRetainNestedApiDomains(string requirement)
    {
        var c = MinimalEmissionTest.Analyze("internal struct Hidden\npublic struct Target<T>\n    T is " + requirement);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFunctionConditionChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    T is (i32) -> bool and Owned\npublic func use(value: Target<(i32) -> bool>) => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Function Type condition failed.");
            }
        }));
    }
}
