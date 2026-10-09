// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PairAnnotationBindingTest
{
    [Theory]
    [InlineData("s/T during a")]
    [InlineData("s/(T) during a")]
    [InlineData("owner/(s/T during a)")]
    public void OriginalPairAnnotationRetainsWholeTypeAndPendingProof(string type)
    {
        var c = Parse($"func f<s/T>(value: {type}) => ()");
        Verify(c);
        Verify(CompilationTestHelper.Reload(c));
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

    // SPEC 8.1.2: a written outer Origin is a conditional slot, active only for the admitted borrow bindings, so the same
    // annotation is valid whatever `s` admits.
    [Theory]
    [InlineData("s/T during a", "owner")]
    [InlineData("s/T during a", "raw")]
    [InlineData("s/U during a", "owner")]
    [InlineData("s/U during a", "raw")]
    [InlineData("s/T during a", "owner or ref")]
    [InlineData("s/U during a", "value or valueborrow")]
    [InlineData("s/U during static", "owner or ref")]
    public void AnOuterOriginIsInactiveForValueBindings(string type, string semantics)
    {
        var c = Parse($"func f<s/T, U>(value: {type})\n    s is {semantics}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // SPEC 8.1.2: so is a struct Field's conditional slot, whatever the pair binder of the struct admits.
    [Theory]
    [InlineData("s/T during a", "owner")]
    [InlineData("s/T during a", "owner or ref")]
    [InlineData("s/U during a", "value or valueborrow")]
    [InlineData("s/Option<U> during a", "owner or uniq")]
    [InlineData("Array<s/U during a>", "value or valueborrow")]
    public void AFieldOuterOriginIsInactiveForValueBindings(string type, string semantics)
    {
        var c = Parse($"struct Holder<s/T, U> {{a}}\n    s is {semantics}\n    let anchor: ref/i32 during a\n    let value: {type}\n");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // The same signature serves a value and a borrow: the slot binds `a` only for the borrow instance.
    [Fact]
    public void OneAnnotatedSignatureServesValueAndBorrowInstances()
    {
        const string Source = "struct Box<T>\n    public let item: T\n    public init(item: T) => self.item = item@move\n" +
            "func wrap<s/T>(value: s/T during a) -> Box<s/T during a>\n    s is value or valueborrow\n    return Box<s/T during a>.init(value@move)\n" +
            "let owned = wrap(5)\nlet n = 7\nlet shared = wrap(n@ref)\nrequire owned.item == 5 and shared.item == 7 else => $abort(\"wrap\")\nConsole.writeLine(\"conditional slot\")";
        ScalarEmissionTest.EmitFixture("PairConditionalOuterOrigin", Source, "conditional slot\n");
    }

    // A static Origin admits no exclusive borrow, so an admitted `uniq` cannot form the Type.
    [Theory]
    [InlineData("s/T during static", "uniq")]
    [InlineData("s/U during static", "owner or uniq")]
    public void AStaticOuterOriginRejectsExclusiveBindings(string type, string semantics)
    {
        var c = Parse($"func f<s/T, U>(value: {type})\n    s is {semantics}\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Theory]
    [InlineData("s/U", "owner")]
    [InlineData("s/U", "raw")]
    [InlineData("s/(U)", "owner")]
    [InlineData("s/T", "owner")]
    [InlineData("s/T", "raw")]
    public void UnannotatedTypesKeepTheirExistingProofs(string type, string semantics)
    {
        var c = Parse($"func f<s/T, U>(value: {type})\n    s is {semantics}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("s/T during a")]
    public void BorrowAnnotationAcceptsDefinitionSideBorrowProof(string type)
    {
        var c = Parse($"func f<s/T>(value: {type})\n    s is ref\n    T is i32\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(BoundTypeKind.Parameter, Function(c).Parameters[0].Type.BoundType!.Kind);
    }

    [Theory]
    [InlineData("s/T during missing", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("s/T during missing.slot", DiagnosticCode.InvalidOriginBinding_Kd)]
    public void InvalidAnnotationsRetainOrdinaryDiagnostics(string type, DiagnosticCode code)
    {
        var c = Parse($"func f<s/T>(value: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void AnnotationDoesNotChangeAnotherUseOfTheOriginalPair()
    {
        var c = Parse("func f<s/T>(x: s/T, y: s/T during a, z: s/T during b) => ()");
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
        var c = Parse("func f<s/T>(value: (raw/(s/T during a), [2 of s/T during a])) => ()");
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

    [Fact]
    public void ATautologicalClauseDoesNotChangeAnOmittedPairOrigin()
    {
        // SPEC 15.4.1: a function with Origin clauses completes its omitted slots in their pending order, as it would without the
        // clauses; the Semantics condition of the pair result once fell away there, leaving the result without an Origin.
        var plain = Parse("func g<s/T>(x: s/i32) -> s/i32 => x");
        var clause = Parse("func g<s/T>(x: s/i32) -> s/i32\n    origin static outlives static\n    return x");
        Assert.True(plain.Bind().IsComplete, Describe(plain));
        Assert.True(clause.Bind().IsComplete, Describe(clause));
        Assert.Equal(Function(plain).BoundSymbol!.Type!.Origin?.Kind, Function(clause).BoundSymbol!.Type!.Origin?.Kind);
    }

    private static Compilation Parse(string source)
        => CompilationTestHelper.ParseSuccess(source, "annotation.kimi");

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single();

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
