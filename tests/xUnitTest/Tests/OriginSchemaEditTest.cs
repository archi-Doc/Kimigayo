// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginSchemaEditTest
{
    [Theory]
    [InlineData("before", "after")]
    [InlineData("a", "longerName")]
    [InlineData("same", "same")]
    public void ReplacingASignatureOriginRebuildsItsNameAndLocation(string before, string after)
    {
        var c = MinimalEmissionTest.Analyze(Source(before));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var function = Function(c);
        var previous = Assert.Single(function.SymbolOf()!.Schema!.Origins);
        var donor = Function(MinimalEmissionTest.Analyze("\n" + Source(after)));
        var expectedSpan = Assert.Single(donor.SymbolOf()!.Schema!.Origins).Span;
        var replacement = ParseTestHelper.Detach(donor.ReturnType!, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(function, function.ReturnType!, replacement));
        Assert.False(c.Binding.Result.IsComplete);

        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var current = Assert.Single(function.SymbolOf()!.Schema!.Origins);
        Assert.Equal(after, current.Name);
        Assert.Equal(after, current.Origin.Name);
        Assert.Equal(expectedSpan, current.Span);
        Assert.Same(function, current.Origin.Binder);
        Assert.Same(current.Origin, function.ReturnType!.TypeOf()!.Origin);
        Assert.Equal(before, previous.Name);
        Assert.Equal(before, previous.Origin.Name);
        Assert.True(c.Bind().IsComplete);
        Assert.Same(current, Assert.Single(function.SymbolOf()!.Schema!.Origins));
    }

    [Theory]
    [InlineData("U", GenericSlotKind.Type)]
    [InlineData("s/U", GenericSlotKind.Pair)]
    [InlineData("length N", GenericSlotKind.Length)]
    public void ReplacingAGenericSlotRebuildsTheSchema(string parameter, GenericSlotKind kind)
    {
        var c = MinimalEmissionTest.Analyze("func f<T>() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var function = Function(c);
        var donor = Function(MinimalEmissionTest.Analyze($"func f<{parameter}>() => ()"));
        var replacement = donor.GenericArguments[0];
        var reader = new TokenReader(replacement.CodeContext, string.Empty, []);
        ParseTestHelper.Detach(replacement, new GenericParameterKoto(ref reader, default, "Placeholder", null));
        Assert.True(KotoHelper.Replace(function, function.GenericArguments[0], replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var slot = Assert.Single(function.SymbolOf()!.Schema!.GenericSlots);
        Assert.Equal(kind, slot.Kind);
        Assert.Same(replacement.SymbolOf(), slot.Symbol);
        Assert.Same(slot.Symbol.Pair, slot.Semantics);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void UnchangedAndEditedSchemasReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze(Source("before"));
        var function = Function(c);
        var donor = Function(MinimalEmissionTest.Analyze(Source("after")));
        var replacement = ParseTestHelper.Detach(donor.ReturnType!, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(function, function.ReturnType!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var schema = function.SymbolOf()!.Schema;
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid);
        Assert.Same(schema, function.SymbolOf()!.Schema);
        Assert.Equal("after", Assert.Single(schema!.Origins).Name);
    }

    [Fact]
    public void NestedSchemasFollowEditedOuterSlots()
    {
        var c = MinimalEmissionTest.Analyze("struct Outer<T> {a, b}\n    struct Inner\n        let value: ref/i32 during a");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var outer = c.Kotonoha.RootKoto.NestedContainers.Single();
        var inner = outer.NestedContainers.Single();
        var previous = inner.SymbolOf()!.Schema!;
        var donor = MinimalEmissionTest.Analyze("struct Outer<s/U> {b, a}").Kotonoha.RootKoto.NestedContainers.Single();
        var reader = new TokenReader(donor.CodeContext, string.Empty, []);
        var replacement = ParseTestHelper.Detach(donor.GenericParameterNodes[0], new GenericParameterKoto(ref reader, default, "Placeholder", null));
        Assert.True(KotoHelper.Replace(outer, outer.GenericParameterNodes[0], replacement));
        outer.Origins[0] = "b";
        outer.Origins[1] = "a";
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var current = inner.SymbolOf()!.Schema!;
        Assert.Equal(["b", "a"], current.Origins.Select(x => x.Name));
        Assert.Equal(GenericSlotKind.Pair, Assert.Single(current.GenericSlots).Kind);
        Assert.Same(outer.SymbolOf()!.Schema!.GenericSlots[0].Symbol, current.GenericSlots[0].Symbol);
        Assert.Same(outer.SymbolOf()!.Schema!.Origins[1].Origin, current.Origins[1].Origin);
        Assert.Same(current.Origins[1].Origin, inner.Members.OfType<VariableKoto>().Single().SymbolOf()!.Type!.Origin);
        Assert.Equal(["a", "b"], previous.Origins.Select(x => x.Name));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(current, inner.SymbolOf()!.Schema);
    }

    private static string Source(string origin) => $"func f() -> ref/i32 during {origin} => $abort(\"unused\")";

    private static FunctionKoto Function(Compilation c) => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single();
}
