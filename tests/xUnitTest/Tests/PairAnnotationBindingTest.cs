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
                var annotated = f.Parameters[0].Type.TypeOf()!;
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

    // SPEC 8.1.2, 15.6.1: the original `W` fits an annotated occurrence when its implicit `o` outlives the annotation, in owner and ref
    // instances alike, and a common element Type meets two annotated occurrences (F16).
    [Fact]
    public void TheOriginalPairFitsAnAnnotatedOccurrence()
    {
        const string Source = "struct Holder<s/T>\n    s is owner or ref\n    T is Copy\n    var item: s/T\n    public init(item: s/T) => self.item = item@move\n" +
            "    public func peek(self: ref/Self during a) -> s/T during a\n        return self.item\n" +
            "func k<s/T>(x: ref/(s/T) during a) -> s/T during a\n    s is owner or ref\n    T is Copy\n    return x@follow\n" +
            "func both<s/T>(x: s/T during a, y: s/T during b) -> isize\n    s is owner or ref\n    T is Copy\n    let items = [x, y]\n    return items.length\n" +
            "let owned = Holder<i32>.init(4)\nlet n: i32 = 3\nlet shared = Holder<ref/i32>.init(n@ref)\nlet five: i32 = 5\nlet c = five@ref\n" +
            "require owned.peek() == 4 and shared.peek() == 3 and k(five@ref) == 5 and k(c@ref) == 5 else => $abort(\"fit\")\n" +
            "require both(n@ref, five@ref) == 2 and both(1, 2) == 2 else => $abort(\"meet\")\nConsole.writeLine(\"annotated fit\")";
        ScalarEmissionTest.EmitFixture("PairAnnotatedFit", Source, "annotated fit\n");
    }

    // SPEC 8.1.2, 15.6.1: a difference in Origin presence between pair layers of one binder relates their slots, never a Type mismatch,
    // and the relation needs a premise like any other (F16); a common element Type keeps no such difference, so the element fits it.
    [Theory]
    [InlineData("func widen<s/T>(x: s/T during b, q: ref/i32 during a) -> s/T during a\n    s is ref\n    origin b outlives a\n    return x@move", null)]
    [InlineData("func widen<s/T>(x: s/T, q: ref/i32 during a) -> s/T during a\n    s is ref\n    return x@move", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func back<s/T>(x: s/T during a) -> s/T\n    s is owner or ref\n    return x@move", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func fwd<s/T>(x: s/T) -> s/T during a\n    s is owner or ref\n    return x@move", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func both<s/T>(x: s/T, y: s/T during b) -> isize\n    s is owner or ref\n    T is Copy\n    let items = [x, y]\n    return items.length", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    public void AnOriginPresenceDifferenceIsARelation(string source, string? code)
    {
        var c = Parse(source);
        c.Bind();
        Assert.Equal(code is null ? [] : [code], c.Binding.Issues.Select(static x => x.Code.ToString()));
    }

    // SPEC 15.6.1, 23.3.6.4: an unformed pair Field reports its formation once; a fit against it derives from that failure instead of
    // adding a Type mismatch, and a call that reads the Field rests on it (F17, A.11).
    [Fact]
    public void AnUnformedPairFieldReportsOnlyItsFormation()
    {
        var c = Parse("struct Holder<s/T, U>\n    s is value or valueborrow\n    public let first: s/T\n    public let other: s/U\n" +
            "    public init(first: s/T, other: s/U)\n        self.first = first@move\n        self.other = other@move\n" +
            "let x: i32 = 1\nlet y: i64 = 2\nlet h = Holder<ref/i32, i64>.init(x@ref, y@ref)\nConsole.writeLine(\"\\(h.other)\")");
        c.Bind();
        Assert.Equal([DiagnosticCode.MissingOriginBinding_Kd], c.Binding.Issues.Select(static x => x.Code));
    }

    // A static Origin admits no exclusive borrow, so an admitted `uniq` cannot form the Type: a Language cause (SPEC 8.1.2).
    [Theory]
    [InlineData("s/T during static", "uniq")]
    [InlineData("s/U during static", "owner or uniq")]
    public void AStaticOuterOriginRejectsExclusiveBindings(string type, string semantics)
    {
        var c = Parse($"func f<s/T, U>(value: {type})\n    s is {semantics}\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal([DiagnosticCode.InvalidOriginBinding_Kd], c.Binding.Issues.Select(static x => x.Code));
    }

    // SPEC 8.1.2, 23.3.6.4: a pair occurrence's formation publishes each independent cause, a Language cause before a target role that
    // is not proven: a missing slot under an admitted borrow is MissingOriginBinding_Kd, as for its concrete twin, and an unproven Object
    // Target of an admitted object binding is UnprovenConstraint_Kd beside it. An associated specification omits no slot either
    // (SPEC 15.3.3, 15.4.4).
    [Theory]
    [InlineData("struct Holder<s/T, U>\n    s is ref\n    let value: s/T\n    let other: s/U\n", new[] { DiagnosticCode.MissingOriginBinding_Kd })]
    [InlineData("struct Holder<s/T, U>\n    s is value or valueborrow\n    let value: s/T\n    let other: s/U\n", new[] { DiagnosticCode.MissingOriginBinding_Kd })]
    [InlineData("contract Source\n    associate Element\nstruct Holder<s/T, U>\n    s is value or valueborrow\n    Self is Source\n    associate Source.Element is s/U\n    var item: s/T\n", new[] { DiagnosticCode.MissingOriginBinding_Kd })]
    [InlineData("struct Holder<s/T, U>\n    let value: s/T\n    let other: s/U\n", new[] { DiagnosticCode.MissingOriginBinding_Kd, DiagnosticCode.UnprovenConstraint_Kd })]
    [InlineData("struct Holder<s/T, U> {a}\n    let value: s/T\n    let other: s/U during a\n", new[] { DiagnosticCode.UnprovenConstraint_Kd })]
    public void FormationCausesKeepTheirOwnCodes(string source, DiagnosticCode[] codes)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(codes, c.Binding.Issues.Select(static x => x.Code));
        Assert.False(c.Binding.CheckBound().IsComplete);
        Assert.Equal(codes, c.Binding.Issues.Select(static x => x.Code));
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
        Assert.Equal(BoundTypeKind.Parameter, Function(c).Parameters[0].Type.TypeOf()!.Kind);
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
        Assert.Same(schema.GenericSlots[0].Symbol.WholeType, f.Parameters[0].Type.TypeOf());
        Assert.Null(f.Parameters[0].Type.TypeOf()!.Origin);
        Assert.Same(schema.Origins[0].Origin, f.Parameters[1].Type.TypeOf()!.Origin);
        Assert.Same(schema.Origins[1].Origin, f.Parameters[2].Type.TypeOf()!.Origin);
        Assert.NotSame(f.Parameters[1].Type.TypeOf(), f.Parameters[2].Type.TypeOf());
    }

    [Fact]
    public void NestedAnnotationsDoNotAddSemanticsLayers()
    {
        var c = Parse("func f<s/T>(value: (raw/(s/T during a), [2 of s/T during a])) => ()");
        Assert.False(c.Bind().IsComplete);
        var f = Function(c);
        var tuple = f.Parameters[0].Type.TypeOf()!;
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
