// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FunctionParameterIdentityBindingTest
{
    [Theory]
    [InlineData("(i32) -> bool", "(i32,) -> bool", true)]
    [InlineData("(()) -> bool", "((),) -> bool", true)]
    [InlineData("() -> bool", "(()) -> bool", false)]
    [InlineData("((i32, bool)) -> bool", "(i32, bool) -> bool", false)]
    [InlineData("((i32,)) -> bool", "(i32,) -> bool", false)]
    [InlineData("((i32)) -> bool", "(i32) -> bool", true)]
    [InlineData("(i32, bool) -> ()", "(i32, bool,) -> ()", true)]
    [InlineData("((i32) -> bool) -> ()", "((i32,) -> bool,) -> ()", true)]
    [InlineData("() -> (i32) -> bool", "() -> (i32,) -> bool", true)]
    [InlineData("() -> () -> bool", "() -> (()) -> bool", false)]
    public void ParameterListIdentityPreservesArityAndTupleElements(string left, string right, bool same)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target\n    " + left + " is " + right);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(same, c.Binding.Result.IsComplete);
        Assert.Equal(same, c.Bind().IsComplete);
        Assert.Equal(same, Reload(c).Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = MinimalEmissionTest.Analyze(builder.ToString());
            Assert.Empty(written.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
            Assert.Equal(same, written.Binding.Result.IsComplete);
        }
        finally
        {
            builder.Dispose();
        }

        var signatures = MinimalEmissionTest.Analyze("group G\n    func f(a: " + left + ", b: " + right + ") => ()");
        Assert.True(signatures.Binding.Result.IsComplete, MinimalEmissionTest.Describe(signatures, null));
        var function = signatures.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>().Single();
        var a = function.Parameters[0].Type.BoundType!;
        var b = function.Parameters[1].Type.BoundType!;
        Assert.Equal(same, Binding.SameType(a, b));
        Assert.Equal(same, Binding.FitsType(a, b));
        Assert.Equal(same, Binding.FitsType(b, a));
    }

    [Theory]
    [InlineData("(i32,)", true)]
    [InlineData("((i32,))", false)]
    [InlineData("()", false)]
    [InlineData("(())", false)]
    public void GenericRequirementsUseTheCanonicalParameterList(string parameters, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("group G\n    func take<T>()\n        T is (i32) -> bool\n        ()\n    func call() => take<" + parameters + " -> bool>()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var call = c.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(call.ExpressionBody).BoundCall is not null);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("(i32)", "(i32,)", true)]
    [InlineData("()", "(())", false)]
    [InlineData("((i32, bool))", "(i32, bool)", false)]
    public void OverloadIdentityPreservesParameterArity(string left, string right, bool duplicate)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: " + left + " -> bool) => ()\nfunc f(x: " + right + " -> bool) => ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(!duplicate, c.Binding.Result.IsComplete);
        Assert.Equal(duplicate, c.Binding.Issues.Any(x => x.Node.BindingFailure == BindingFailure.Duplicate));
    }

    [Theory]
    [InlineData("(T,)", "(i32)", true)]
    [InlineData("((T,))", "(i32)", false)]
    [InlineData("((T,))", "((i32,))", true)]
    public void SubstitutionRetainsParameterListStructure(string parameters, string required, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    " + parameters + " -> bool is " + required + " -> bool\npublic func use(value: Target<i32>) => ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void ChangingParameterArityRevokesAndRestoresProofs()
    {
        const string source = "public struct Target\n    () -> bool is () -> bool";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var function = Assert.IsType<FunctionTypeKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0].Left);
        var original = function.Parameters;
        var donor = MinimalEmissionTest.Analyze(source.Replace("() -> bool is", "(()) -> bool is", StringComparison.Ordinal));
        var replacement = Assert.IsType<FunctionTypeKoto>(donor.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0].Left).Parameters;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmCanonicalParameterListsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target\n    (i32) -> bool is (i32,) -> bool\n    (()) -> bool is ((),) -> bool");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Function parameter identity failed.");
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
