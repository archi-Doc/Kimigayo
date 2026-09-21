// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PairAnnotationBindingTest
{
    [Theory]
    [InlineData("s{a}/T")]
    [InlineData("s{a}/(T)")]
    [InlineData("owner/(s{a}/T)")]
    public void OriginalPairAnnotationRetainsWholeTypeAndPendingProof(string type)
    {
        var c = Parse($"func f<s/T>(value: {type}) => ()");
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
            BoundType? previous = null;
            for (var pass = 0; pass < 2; pass++)
            {
                Assert.False(c.Bind().IsComplete);
                var f = Function(c);
                var annotated = f.Parameters[0].Type.BoundType!;
                Assert.Equal(BoundTypeKind.Parameter, annotated.Kind);
                Assert.Same(f.BoundSymbol!.Schema!.GenericSlots[0].Symbol, annotated.Symbol);
                Assert.Same(f.BoundSymbol.Schema.Origins[0].Origin, annotated.Origin);
                Assert.Empty(annotated.Components);
                Assert.Null(f.BoundSymbol.Schema.GenericSlots[0].Symbol.WholeType!.Origin);
                Assert.Contains(c.Binding.Obligations, x => x.Kind == BindingObligationKind.TypeFormation && x.Deadline == BindingDeadline.Definition);
                Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnprovenConstraint_Kd, x.Code));
                if (previous is not null)
                {
                    Assert.Same(previous, annotated);
                }

                previous = annotated;
            }
        }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("unsafe")]
    public void NonBorrowSemanticsCannotCertifyAnOuterOrigin(string semantics)
    {
        var c = Parse($"func f<s/T>(value: s{{a}}/T)\n    s is {semantics}\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("s{a}/T", "owner")]
    [InlineData("s{a}/T", "unsafe")]
    [InlineData("s{a}/U", "owner")]
    [InlineData("s{a}/U", "unsafe")]
    public void ApplicationEvidenceNeverDischargesAnExplicitOrigin(string type, string semantics)
    {
        var c = Parse($"func f<s/T, U>(value: {type})\n    s is {semantics}\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Theory]
    [InlineData("s/U", "owner")]
    [InlineData("s/U", "unsafe")]
    [InlineData("s/(U)", "owner")]
    [InlineData("s/T", "owner")]
    [InlineData("s/T", "unsafe")]
    public void UnannotatedTypesKeepTheirExistingProofs(string type, string semantics)
    {
        var c = Parse($"func f<s/T, U>(value: {type})\n    s is {semantics}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("s{a}/T")]
    public void BorrowAnnotationAcceptsDefinitionSideBorrowProof(string type)
    {
        var c = Parse($"func f<s/T>(value: {type})\n    s is ref\n    T is i32\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(BoundTypeKind.Parameter, Function(c).Parameters[0].Type.BoundType!.Kind);
    }

    [Theory]
    [InlineData("s{missing}/T", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("s{wrong => a}/T", DiagnosticCode.InvalidOriginBinding_Kd)]
    public void InvalidAnnotationsRetainOrdinaryDiagnostics(string type, DiagnosticCode code)
    {
        var c = Parse($"func f<s/T>(value: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void AnnotationDoesNotChangeAnotherUseOfTheOriginalPair()
    {
        var c = Parse("func f<s/T>(x: s/T, y: s{a}/T, z: s{b}/T) => ()");
        Assert.False(c.Bind().IsComplete);
        var f = Function(c);
        var schema = f.BoundSymbol!.Schema!;
        Assert.Same(schema.GenericSlots[0].Symbol.WholeType, f.Parameters[0].Type.BoundType);
        Assert.Null(f.Parameters[0].Type.BoundType!.Origin);
        Assert.Same(schema.Origins[0].Origin, f.Parameters[1].Type.BoundType!.Origin);
        Assert.Same(schema.Origins[1].Origin, f.Parameters[2].Type.BoundType!.Origin);
        Assert.NotSame(f.Parameters[1].Type.BoundType, f.Parameters[2].Type.BoundType);
    }

    [Fact]
    public void NestedAnnotationsDoNotAddSemanticsLayers()
    {
        var c = Parse("func f<s/T>(value: (unsafe/(s{a}/T), [2 of s{a}/T])) => ()");
        Assert.False(c.Bind().IsComplete);
        var f = Function(c);
        var tuple = f.Parameters[0].Type.BoundType!;
        Assert.Equal(BoundTypeKind.Tuple, tuple.Kind);
        var pointerTarget = Assert.Single(tuple.Components[0].Components);
        var arrayElement = Assert.Single(tuple.Components[1].Components);
        Assert.Same(pointerTarget, arrayElement);
        Assert.Equal(BoundTypeKind.Parameter, pointerTarget.Kind);
        Assert.Same(f.BoundSymbol!.Schema!.Origins[0].Origin, pointerTarget.Origin);
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
        c.Kotonoha.AddSource(new SourceDocument("annotation.kimi", source));
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single();

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
