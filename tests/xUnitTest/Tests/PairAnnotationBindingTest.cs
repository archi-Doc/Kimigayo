// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PairAnnotationBindingTest
{
    // Structs whose slot only a conditional pair slot uses (SPEC 15.3.5), by what the binder admits, and nested users of them.
    internal const string HolderUniq = "struct Holder<s/T> {a}\n    s is uniq\n    public let item: s/T during a\n";

    internal const string HolderOwnerUniq = "struct Holder<s/T> {a}\n    s is owner or uniq\n    public let item: s/T during a\n";

    internal const string HolderRefUniq = "struct Holder<s/T> {a}\n    s is ref or uniq\n    public let item: s/T during a\n";

    internal const string InnerOwnerUniq = "struct Inner<r/U> {b}\n    r is owner or uniq\n    public let x: r/U during b\n";

    private const string OuterOwnerUniq = "struct Outer<q/U> {b}\n    q is owner or uniq\n    public let h: Holder<q/U during b> during b\n";

    // ABinderKeepsItsCasesAtAnyOrdinal's shapes, after 64 Type parameters or pair slots ({F}, their premises {Q}, 64 arguments {A}): Holder's
    // slot used by its 65th binder; a nested Inner whose slot that binder of Outer uses; N storing a Holder at its 65th binder; an owner
    // case's Loan.
    private const string WideHolder = "struct Holder<{F}s/T> {a}\n{Q}    s is owner or uniq\n    public let item: s/T during a\n";

    private const string WideOuter = "struct Outer<{F}s/T>\n{Q}    s is owner or uniq\n    public struct Inner {a}\n        public let x: s/T during a\n";

    private const string WideNested = "struct N<q/U> {b}\n    q is owner or uniq\n    public let h: Holder<{A}q/U during b> during b\n";

    private const string WideLoan = "func make(r: ref/i32) -> Holder<{A}i32> during r => $abort(\"x\")\nfunc use(h: ref/Holder<{A}i32>) => ()\n" +
        "func run() -> i32\n    var y: i32 = 1\n    let h = make(y@ref)\n    y = 3\n    use(h@ref)\n    return y";

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
                Assert.Same(f.SymbolOf()!.Schema!.GenericSlots[0].Symbol, annotated.Symbol);
                Assert.Same(f.SymbolOf()!.Schema!.Origins[0].Origin, annotated.Origin);
                Assert.Empty(annotated.Components);
                Assert.Null(f.SymbolOf()!.Schema!.GenericSlots[0].Symbol.WholeType!.Origin);
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
    // and the relation needs a premise like any other (F16); a common element Type keeps no such difference, so the element fits it, in
    // either order and nested (the common-Type search's own Position fact, G216 (a) U2.1 e2c-1).
    [Theory]
    [InlineData("func widen<s/T>(x: s/T during b, q: ref/i32 during a) -> s/T during a\n    s is ref\n    origin b outlives a\n    return x@move", null)]
    [InlineData("func widen<s/T>(x: s/T, q: ref/i32 during a) -> s/T during a\n    s is ref\n    return x@move", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func back<s/T>(x: s/T during a) -> s/T\n    s is owner or ref\n    return x@move", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func fwd<s/T>(x: s/T) -> s/T during a\n    s is owner or ref\n    return x@move", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func both<s/T>(x: s/T, y: s/T during b) -> isize\n    s is owner or ref\n    T is Copy\n    let items = [x, y]\n    return items.length", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func back2<s/T>(x: s/T, y: s/T during b) -> isize\n    s is owner or ref\n    T is Copy\n    let items = [y, x]\n    return items.length", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData("func nest<s/T>(x: s/T, y: s/T during b) -> isize\n    s is owner or ref\n    T is Copy\n    let items = [[x], [y]]\n    return items.length", nameof(DiagnosticCode.UnprovenOriginRelation_Kd))]
    public void AnOriginPresenceDifferenceIsARelation(string source, string? code)
    {
        var c = Parse(source);
        c.Bind();
        Assert.Equal(code is null ? [] : [code], c.Binding.Issues.Select(static x => x.Code.ToString()));
    }

    // SPEC 15.6.1, 23.1.2: an unformed pair Field reports its formation once; a fit against it derives from that failure instead of
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

    // SPEC 15.3.5, 15.2.3, 8.15.2 (DECISIONS BU-44): a slot only a conditional pair slot uses is `uniq` in its binder's `uniq` cases and
    // none elsewhere, so `static` binds it only where the instance's case stores no exclusive Loan: a concrete argument selects its case
    // (`s is uniq`'s only case included), a pair layer of a binder in scope that binder's cases, each of which must be valid, through a
    // nested Type, an enclosing declaration's binder, an enum's Case or a method's Container binder too; a Type parameter selects no case
    // and adds no record beside its UnprovenConstraint_Kd.
    [Theory]
    [InlineData(HolderUniq + "func g(r: uniq/i32) -> Holder<uniq/i32 during r> during static => $abort(\"u\")", "InvalidOriginBinding_Kd: Holder<uniq/i32 during r> during static")]
    [InlineData(HolderOwnerUniq + "func g() -> Holder<i32> during static => $abort(\"u\")", "")]
    [InlineData(HolderRefUniq + "func g(r: ref/i32) -> Holder<ref/i32 during r> during static => $abort(\"u\")", "")]
    [InlineData(HolderRefUniq + "func g(r: uniq/i32) -> Holder<uniq/i32 during r> during static => $abort(\"u\")", "InvalidOriginBinding_Kd: Holder<uniq/i32 during r> during static")]
    [InlineData(HolderOwnerUniq + "func g<q/U>(x: q/U) -> Holder<q/U> during static\n    q is owner or uniq\n    $abort(\"u\")", "InvalidOriginBinding_Kd: Holder<q/U> during static")]
    [InlineData(HolderOwnerUniq + "func g<q/U>(x: q/U) -> Holder<q/U> during static\n    q is owner\n    $abort(\"u\")", "")]
    [InlineData(HolderOwnerUniq + OuterOwnerUniq + "func g(r: uniq/i32) -> Outer<uniq/i32 during r> during static => $abort(\"u\")", "InvalidOriginBinding_Kd: Outer<uniq/i32 during r> during static")]
    [InlineData(HolderOwnerUniq + OuterOwnerUniq + "func g() -> Outer<i32> during static => $abort(\"u\")", "")]
    [InlineData(InnerOwnerUniq + "struct Outer {a, c}\n    let j: Inner<uniq/i32 during c>{v}\n        origin v.b == a\nfunc lent(h: Outer{w}) -> i32\n    origin w.a == static\n    return 0", "InvalidOriginBinding_Kd: Outer{w}")]
    [InlineData(InnerOwnerUniq + "struct Outer {a}\n    let j: Inner<i32> during a\nfunc cached(h: Outer during static) => ()", "")]
    [InlineData(
        "struct Outer<s/T>\n    s is owner or uniq\n    public struct Inner<U> {a}\n        public let u: U\n        public let x: s/T during a\n" +
        "func g(r: uniq/i32) -> Outer<uniq/i32 during r>.Inner<i64> during static => $abort(\"u\")\nfunc h() -> Outer<i32>.Inner<i64> during static => $abort(\"u\")",
        "InvalidOriginBinding_Kd: Outer<uniq/i32 during r>.Inner<i64> during static")]
    [InlineData(
        "struct M<s/T, t/U> {a}\n    s is owner or uniq\n    t is owner or ref\n    public let x: s/T during a\n    public let y: t/U during a\nfunc f1() -> M<i32, i32> during static => $abort(\"u\")\n" +
        "func f2(r: ref/i32) -> M<i32, ref/i32 during r> during static => $abort(\"u\")\nfunc f3(r: uniq/i32) -> M<uniq/i32 during r, i32> during static => $abort(\"u\")",
        "InvalidOriginBinding_Kd: M<uniq/i32 during r, i32> during static")]
    [InlineData(
        "enum E<s/T> {a}\n    s is owner or uniq\n    One(s/T during a)\n    Two\nfunc g(r: uniq/i32) -> E<uniq/i32 during r> during static => $abort(\"u\")\nfunc h() -> E<i32> during static => $abort(\"u\")",
        "InvalidOriginBinding_Kd: E<uniq/i32 during r> during static")]
    [InlineData(
        "struct Holder<s/T> {a}\n    s is owner or uniq\n    public let item: s/T during a\n    public func peek(self: ref/Self, w: s/T during b) => ()\nfunc g(r: uniq/i32) -> Holder<uniq/i32 during r> during static => $abort(\"u\")",
        "InvalidOriginBinding_Kd: Holder<uniq/i32 during r> during static")]
    [InlineData(
        "struct Holder<s/T> {a}\n    s is owner or ref or uniq\n    public let item: s/T during a\nstruct Outer<U> {b}\n    public let h: Holder<U> during b\nfunc g() -> Outer<i32> during static => $abort(\"u\")",
        "UnprovenConstraint_Kd: let h: Holder<U> during b | UnprovenConstraint_Kd: h | UnprovenConstraint_Kd: Holder<U> | UnprovenConstraint_Kd: Holder<U> during b")]
    public void AStaticSlotArgumentIsJudgedInTheInstancesCase(string declarations, string records)
        => Assert.Equal(records, string.Join(" | ", RawPointerSyntaxTest.Records(declarations)));

    // DECISIONS BU-44 (SPEC 15.3.5, 15.2.3, 6.1.3): a slot's binders are pair-slot ordinals with no width limit, so a binder keeps its
    // cases at any ordinal, as in the narrow Holder above: after 64 Type parameters (`types`, which take none) and after 64 pair slots
    // (`pairs`, each `is owner`), `static` binds its owner case and not its uniq one, a raw Field states a slot Phantom in its owner case and the
    // default pipeline keeps no Loan of it; so through a nested declaration's inherited 65th binder, a nested instance's mapping of a
    // 65th binder to a narrow one, and an outer 65th binder that a narrow nested slot maps to.
    [Theory]
    [InlineData(WideHolder + "func g() -> Holder<{A}i32> during static => $abort(\"u\")", "types", "")]
    [InlineData(WideHolder + "func g(r: uniq/i32) -> Holder<{A}uniq/i32 during r> during static => $abort(\"u\")", "types", "InvalidOriginBinding_Kd: Holder<{A}uniq/i32 during r> during static")]
    [InlineData(WideHolder + "    public let p: raw/i32 during a", "types", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData(WideHolder + WideLoan, "types", "")]
    [InlineData(WideHolder + "func g() -> Holder<{A}i32> during static => $abort(\"u\")", "pairs", "")]
    [InlineData(WideHolder + "func g(r: uniq/i32) -> Holder<{A}uniq/i32 during r> during static => $abort(\"u\")", "pairs", "InvalidOriginBinding_Kd: Holder<{A}uniq/i32 during r> during static")]
    [InlineData(WideHolder + "    public let p: raw/i32 during a", "pairs", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData(WideHolder + WideLoan, "pairs", "")]
    [InlineData(WideOuter + "func g(r: uniq/i32) -> Outer<{A}uniq/i32 during r>.Inner during static => $abort(\"u\")", "pairs", "InvalidOriginBinding_Kd: Outer<{A}uniq/i32 during r>.Inner during static")]
    [InlineData(WideOuter + "func g() -> Outer<{A}i32>.Inner during static => $abort(\"u\")", "pairs", "")]
    [InlineData(WideHolder + WideNested + "func g(r: uniq/i32) -> N<uniq/i32 during r> during static => $abort(\"u\")", "pairs", "InvalidOriginBinding_Kd: N<uniq/i32 during r> during static")]
    [InlineData(WideHolder + WideNested + "func g() -> N<i32> during static => $abort(\"u\")", "pairs", "")]
    [InlineData(
        HolderOwnerUniq + "struct O<{F}q/U> {b}\n{Q}    q is owner or uniq\n    public let h: Holder<q/U during b> during b\nfunc g(r: uniq/i32) -> O<{A}uniq/i32 during r> during static => $abort(\"u\")",
        "pairs",
        "InvalidOriginBinding_Kd: O<{A}uniq/i32 during r> during static")]
    public void ABinderKeepsItsCasesAtAnyOrdinal(string template, string prefix, string records)
    {
        var pairs = prefix == "pairs";
        var parameters = string.Concat(Enumerable.Range(0, 64).Select(i => pairs ? $"p{i}/V{i}, " : $"T{i}, "));
        var premises = pairs ? string.Concat(Enumerable.Range(0, 64).Select(static i => $"    p{i} is owner\n")) : string.Empty;
        string Fill(string text) => text.Replace("{F}", parameters, StringComparison.Ordinal).Replace("{Q}", premises, StringComparison.Ordinal)
            .Replace("{A}", string.Concat(Enumerable.Repeat("i32, ", 64)), StringComparison.Ordinal);
        Assert.Equal(Fill(records), string.Join(" | ", RawPointerSyntaxTest.Records(Fill(template))));
    }

    // SPEC 8.1.2, 23.1.2: a pair occurrence's formation publishes each independent cause, a Language cause before a target role that
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
        var schema = f.SymbolOf()!.Schema!;
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
        Assert.Same(f.SymbolOf()!.Schema!.Origins[0].Origin, pointerTarget.Origin);
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
        Assert.Equal(Function(plain).SymbolOf()!.Type!.Origin?.Kind, Function(clause).SymbolOf()!.Type!.Origin?.Kind);
    }

    private static Compilation Parse(string source)
        => CompilationTestHelper.ParseSuccess(source, "annotation.kimi");

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single();

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
