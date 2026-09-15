// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericTypeArgumentBindingTest
{
    [Theory]
    [InlineData("[2 of i32]")]
    [InlineData("([2 of i32])")]
    [InlineData("(([2 of i32]))")]
    [InlineData("([2 of i32], i32)")]
    [InlineData("([0 of i32])")]
    [InlineData("([2 of [3 of i32]])")]
    [InlineData("([(1 + 2) of i32])")]
    [InlineData("(unsafe/[2 of i32])")]
    [InlineData("(Box<[2 of i32]>)")]
    [InlineData("(([2 of i32]) -> i32)")]
    public void CompleteArrayTypesAreValidGenericArguments(string type)
    {
        var c = Parse($"struct Box<T>\nfunc f(value: Box<{type}>) => ()\nfunc take<T>() => ()\ntake<{type}>()");
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "f");
        var argument = Assert.Single(f.Parameters[0].Type.BoundType!.Components);
        var call = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<InvocationKoto>().Single();
        Assert.Same(argument, Assert.Single(call.BoundCall!.TypeArguments.ToArray()));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(argument, Assert.Single(f.Parameters[0].Type.BoundType!.Components));
        Verify(Reload(c));
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            Verify(Parse(builder.ToString()));
        }
        finally
        {
            builder.Dispose();
        }

        static void Verify(Compilation c) => Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("G", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("C", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("1", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("(1 + 2)", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("Box", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("([2 of Box])", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("([2 of C])", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("(Box<C>)", DiagnosticCode.InvalidTypeFormation_Kd)]
    public void NonTypesAndIncompleteNestedTypesRemainInvalid(string type, DiagnosticCode expected)
    {
        foreach (var use in new[] { $"func f(value: Box<{type}>) => ()", $"take<{type}>()" })
        {
            var c = Parse($"group G\ncontract C\nstruct Box<T>\nfunc take<T>() => ()\n{use}");
            Assert.False(c.Bind().IsComplete);
            Assert.Contains(c.Binding.Issues, x => x.Code == expected);
            Assert.False(Reload(c).Bind().IsComplete);
        }
    }

    [Theory]
    [InlineData("([2 of ref/i32 from a])")]
    [InlineData("([2 of ref/i32 from static])")]
    public void NestedBorrowTypesRetainTheirOrigins(string type)
    {
        var c = Parse($"struct Box<T>\nfunc f origin a(value: Box<{type}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "f");
        var array = Assert.Single(f.Parameters[0].Type.BoundType!.Components);
        var element = Assert.Single(array.Components);
        Assert.Equal(SemanticsKind.Ref, element.Semantics);
        Assert.Same(type.Contains("static", StringComparison.Ordinal) ? BoundOrigin.Static : f.BoundSymbol!.Schema!.Origins[0].Origin, element.Origin);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("(N + 1)")]
    [InlineData("((N * 2) + 1)")]
    [InlineData("(1 + 2)")]
    public void NumericLengthArgumentSyntaxIsPreserved(string length)
    {
        var c = Parse($"func f<length N>() => ()\nf<{length}>()");
        var call = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<InvocationKoto>().Single();
        var arguments = Assert.IsType<GenericsKoto>(call.Method);
        Assert.IsType<ParenthesizedKoto>(Assert.Single(arguments.TypeArguments));
        // General explicit length calls are outside this group; parsing is the assertion.
    }

    [Fact]
    public void GroupingKeepsTheSameConstructedTypeIdentity()
    {
        var c = Parse("struct Box<T>\nfunc f(x: Box<[2 of i32]>, y: Box<([2 of i32])>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "f");
        Assert.Same(f.Parameters[0].Type.BoundType, f.Parameters[1].Type.BoundType);
    }

    [Fact]
    public void WarmNestedTypeArgumentBindingAllocatesNothing()
    {
        var c = Parse("struct Box<T>\nfunc f(value: Box<([2 of i32])>) => ()\nfunc take<T>() => ()\ntake<([2 of i32])>()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Nested Type argument Binding failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Parse(string.Empty);
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        return restored;
    }

    private static FunctionKoto Function(Compilation c, string name)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == name);

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("arguments.kimi", source));
        return c;
    }

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
