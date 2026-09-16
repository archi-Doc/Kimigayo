// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FunctionTypeConstraintBindingTest
{
    [Theory]
    [InlineData("() -> i32 is Owned", true)]
    [InlineData("(i32) -> bool is not Copy", true)]
    [InlineData("(i32) -> bool is Copy", false)]
    [InlineData("(i32) -> bool is (i32) -> bool", true)]
    [InlineData("(i32) -> bool is (i32) -> i32", false)]
    [InlineData("() -> bool is () -> bool", true)]
    [InlineData("((i32) -> bool) is not Copy", true)]
    [InlineData("(i32) -> bool is ((i32) -> bool)", true)]
    [InlineData("(i32, bool) -> string is (i32, bool) -> string", true)]
    [InlineData("(() -> i32) -> bool is (() -> i32) -> bool", true)]
    [InlineData("() -> (i32) -> bool is () -> (i32) -> bool", true)]
    [InlineData("(i32) -> bool is (i32) -> bool and Owned", true)]
    [InlineData("(i32) -> bool is ((i32) -> i32 or Copy)", false)]
    [InlineData("[2 of (i32) -> bool] is not Copy", true)]
    public void FunctionTypesRetainCapabilitiesAndIdentity(string clause, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target\n    " + clause);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single().BindingState);
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = MinimalEmissionTest.Analyze(builder.ToString());
            Assert.Empty(written.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
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
        var c = MinimalEmissionTest.Analyze("public " + kind + " Target\n    (i32) -> bool is (i32) -> bool" + (kind == "enum" ? "\n    A" : string.Empty));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("bool", true)]
    [InlineData("i32", false)]
    public void FunctionInputRequirementsAreDischargedAtCalls(string result, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("group Consumer\n    func take<T>()\n        T is (i32) -> bool\n        ()\n    func call() => take<(i32) -> " + result + ">()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var call = c.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(call.ExpressionBody).BoundCall is not null);
    }

    [Theory]
    [InlineData("(Hidden) -> i32 is Owned")]
    [InlineData("() -> Hidden is Owned")]
    [InlineData("(i32) -> i32 is (Hidden) -> i32 or Owned")]
    public void FunctionConditionTypesRetainNestedApiDomains(string clause)
    {
        var c = MinimalEmissionTest.Analyze("internal struct Hidden\npublic struct Target\n    " + clause);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
    }

    [Fact]
    public void WarmFunctionConditionChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target\n    (i32) -> bool is (i32) -> bool and Owned");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Function Type condition failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }
}
