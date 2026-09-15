// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PairGroupingBindingTest
{
    [Theory]
    [InlineData("s/T")]
    [InlineData("s/(T)")]
    [InlineData("s/((T))")]
    public void OriginalPairGroupingPreservesTheWholeType(string type)
    {
        var c = Parse($"func identity<s/T>(value: {type}) -> {type} => value\nfunc use(value: ref/i32) -> ref/i32 => identity(value)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var identity = Function(c, "identity");
        Assert.Same(identity.BoundSymbol!.Schema!.GenericSlots[0].Symbol.WholeType, identity.Parameters[0].Type.BoundType);
        Assert.Same(identity.Parameters[0].Type.BoundType, identity.ReturnType!.BoundType);
        var use = Function(c, "use");
        Assert.Same(use.Parameters[0].Type.BoundType, use.ExpressionBody!.BoundType);
        Assert.DoesNotContain(c.Binding.Obligations, x => x.Deadline == BindingDeadline.Definition);
    }

    [Theory]
    [InlineData("(s/(T))")]
    [InlineData("owner/(s/((T)))")]
    [InlineData("ref/(s/(T)) from static")]
    [InlineData("unsafe/(s/((T)))")]
    [InlineData("[2 of s/(T)]")]
    [InlineData("(s/(T), s/((T)))")]
    [InlineData("Box<s/(T)>")]
    public void NestedWholeTypesRemainValidAcrossReloadAndWriting(string type)
    {
        var source = $"struct Box<U>\n    var value: U\nfunc identity<s/T>(value: {type}) -> {type} => value";
        var c = Parse(source);
        Verify(c);
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

        static void Verify(Compilation c)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
            var identity = Function(c, "identity");
            var bound = identity.Parameters[0].Type.BoundType;
            Assert.Same(bound, identity.ReturnType!.BoundType);
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.Same(bound, identity.Parameters[0].Type.BoundType);
        }
    }

    [Theory]
    [InlineData("s/(U)")]
    [InlineData("s/((U))")]
    [InlineData("T")]
    [InlineData("(T)")]
    [InlineData("s/(owner/T)")]
    public void GroupingDoesNotProveAnUnrelatedApplicationOrStandaloneTarget(string type)
    {
        var c = Parse($"func f<s/T, r/U>(value: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.Contains(c.Binding.Obligations, x => x.Deadline == BindingDeadline.Definition);
    }

    [Theory]
    [InlineData("s/(T from static)")]
    [InlineData("s/((T from missing))")]
    [InlineData("s/(T from (wrong => static))")]
    public void GroupingNeverErasesTargetOriginAnnotations(string type)
    {
        var c = Parse($"func f<s/T>(value: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidOriginBinding_Kd);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void PairIdentityIsNotInferredFromEqualTargetConstraints()
    {
        var c = Parse("func f<s/T, r/U>(value: s/(U))\n    T is i32\n    U is i32\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        var f = Function(c, "f");
        Assert.Equal(BoundTypeKind.SemanticsApplication, f.Parameters[0].Type.BoundType!.Kind);
        Assert.NotSame(f.BoundSymbol!.Schema!.GenericSlots[0].Symbol.WholeType, f.Parameters[0].Type.BoundType);
    }

    [Fact]
    public void RemovingGroupingDoesNotChangeDuplicateSignatureIdentity()
    {
        var c = Parse("func f<s/T>(value: s/(T)) => ()\nfunc f<r/U>(value: r/U) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Fact]
    public void ReplacingTheGroupedTargetInvalidatesPairReconstruction()
    {
        var c = Parse("func f<s/T, r/U>(value: s/(T)) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var semantics = Assert.IsType<TypeSemanticsKoto>(Function(c, "f").Parameters[0].Type);
        var group = Assert.IsType<ParenthesizedTypeKoto>(semantics.Type);
        var replacement = Function(Parse("func f<U>(value: U) => ()"), "f").Parameters[0].Type;
        Assert.True(KotoHelper.Replace(group, group.Type, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.Equal(BoundTypeKind.SemanticsApplication, semantics.BoundType!.Kind);
    }

    [Fact]
    public void GenericGenerationStillRequiresItsOwnImplementation()
    {
        var c = Parse("func identity<s/T>(value: s/(T)) -> s/(T) => value\nidentity(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void WarmGroupedPairBindingAllocatesNothing()
    {
        var c = Parse("func identity<s/T>(value: s/((T))) -> s/(T) => value\nfunc use(value: ref/i32) -> ref/i32 => identity(value)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Grouped pair Binding failed.");
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

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("pair.kimi", source));
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static FunctionKoto Function(Compilation c, string name)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == name);

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
